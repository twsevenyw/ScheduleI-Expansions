using Expansions.Core.Actions;
using Expansions.Core.Configuration;
using Expansions.Core.Diagnostics;
using Expansions.Core.Logging;
using Expansions.Core.Tutorial;
using Expansions.Core.UI.Native;
using UnityEngine;

namespace Expansions.Core.UI;

/// <summary>
/// The shipping UI: a native uGUI/TMP screen built in the game's own visual language, reachable from a
/// cloned main-menu nav entry and from the in-game hotkey. It carries the module toggles and the
/// Actions surface that replaces the in-game console.
/// <para>
/// One screen serves both surfaces. It lives on its own <c>DontDestroyOnLoad</c> overlay canvas, so the
/// main-menu entry and the hotkey open the same pages with the same code — the alternative (a cloned
/// <c>MenuScreen</c>) would exist only in the menu and would drag along a <c>MonoState</c> and the
/// game's navigation components.
/// </para>
/// <para>
/// Every game lookup degrades: missing fonts fall back down a chain, missing sprites fall back to a
/// generated rounded rect, missing sounds go silent, and a main menu we cannot find hands over to
/// <see cref="ImguiExpansionMenu"/> rather than leaving the user with no way to reach anything.
/// </para>
/// </summary>
public sealed class NativeExpansionMenu : IExpansionMenu
{
    private static readonly ModuleLogger Log = new("UI");

    private readonly ExpansionsScreen _screen;
    private readonly MainMenuInjector _injector;

    /// <summary>
    /// Clicks are queued rather than run inside the handler: an action can take long enough to matter
    /// (a probe suite writes a file) and rebuilding the rows from inside a <c>Button.onClick</c> would
    /// destroy the button mid-event.
    /// </summary>
    private readonly Queue<PendingAction> _pending = new();

    private Action<IExpansionModule>? _registeredHandler;
    private Action<IExpansionModule, bool>? _toggledHandler;
    private Action? _statusHandler;
    private Action? _actionsChangedHandler;
    private Action? _chaptersChangedHandler;

    private CursorLockMode _previousLock = CursorLockMode.Locked;
    private bool _previousCursorVisible;
    private bool _open;
    private bool _dirty = true;
    private bool _broken;
    private bool _probeRequested;
    private bool _hintDirty = true;
    private bool _reportedInjectionFailure;

    public NativeExpansionMenu()
    {
        _screen = new ExpansionsScreen(Log);
        _screen.CloseRequested += Close;
        _screen.ActionRequested += action => _pending.Enqueue(new PendingAction(action, null));
        _screen.ChoiceRequested += (action, choice) => _pending.Enqueue(new PendingAction(action, choice));
        _injector = new MainMenuInjector(Log, Open);
    }

    public string Name => "Native main-menu screen";

    public bool IsOpen => _open;

    public void Open()
    {
        if (_open || _broken)
            return;

        if (!EnsureScreen())
            return;

        _previousLock = Cursor.lockState;
        _previousCursorVisible = Cursor.visible;
        _open = true;

        // Suspends movement, camera look and the crosshair through the game's own state stack, which
        // is what stops typing into a picker's search box also walking the player.
        GameUiState.Enter();

        // Visible first: the canvas has to be enabled before the layout pass inside SyncFromRegistry,
        // and the lists only settle once TMP has measured their text.
        _screen.SetVisible(true);
        _screen.SetHint(Hint());
        _screen.SyncFromRegistry();
        _dirty = false;
        _hintDirty = false;
    }

    public void Close()
    {
        if (!_open)
            return;

        _open = false;
        _screen.SetVisible(false);
        _pending.Clear();

        GameUiState.Exit();

        // Restored verbatim. In the menu scene the cursor was already free, and during gameplay the
        // game re-locks it every frame anyway, so forcing Locked here would only break the menu case.
        Cursor.lockState = _previousLock;
        Cursor.visible = _previousCursorVisible;
    }

    public void OnAttached()
    {
        _registeredHandler = _ => _dirty = true;
        _toggledHandler = (_, _) => _dirty = true;
        _statusHandler = () => _hintDirty = true;
        _actionsChangedHandler = () => _dirty = true;
        _chaptersChangedHandler = () => _dirty = true;

        ExpansionRegistry.ModuleRegistered += _registeredHandler;
        ExpansionRegistry.ModuleToggled += _toggledHandler;
        TutorialDirector.StatusChanged += _statusHandler;
        ActionRegistry.Changed += _actionsChangedHandler;
        TutorialRegistry.Changed += _chaptersChangedHandler;
    }

    public void OnDetached()
    {
        Close();

        if (_registeredHandler is not null)
            ExpansionRegistry.ModuleRegistered -= _registeredHandler;

        if (_toggledHandler is not null)
            ExpansionRegistry.ModuleToggled -= _toggledHandler;

        if (_statusHandler is not null)
            TutorialDirector.StatusChanged -= _statusHandler;

        if (_actionsChangedHandler is not null)
            ActionRegistry.Changed -= _actionsChangedHandler;

        if (_chaptersChangedHandler is not null)
            TutorialRegistry.Changed -= _chaptersChangedHandler;

        _registeredHandler = null;
        _toggledHandler = null;
        _statusHandler = null;
        _actionsChangedHandler = null;
        _chaptersChangedHandler = null;

        _injector.Destroy();
        _screen.Destroy();
        GameSounds.Release();
    }

    public void OnUpdate()
    {
        if (_broken)
            return;

        _injector.Tick();

        if (_injector.GaveUp && !_reportedInjectionFailure)
        {
            _reportedInjectionFailure = true;

            if (!_injector.EverInjected)
            {
                // Last statement on this path: the swap detaches this menu, which destroys the screen.
                ExpansionMenu.FallBackToPlaceholder($"the main menu could not be found ({_injector.Failure})");
                return;
            }

            // It has worked before this menu visit, so the screen itself is sound and the hotkey still
            // reaches it. Downgrading to the IMGUI menu here would be strictly worse.
            Log.Warn(
                $"The main-menu entry could not be re-added ({_injector.Failure}) — " +
                $"press {ExpansionConfig.MenuHotkey} to open the Expansions screen.");
        }

        if (_open)
            PumpInput();

        if (_screen.IsBuilt)
            _screen.Tick(_open);

        DrainPending();

        if (!_probeRequested)
            return;

        _probeRequested = false;
        ActionRegistry.Invoke("core.probes.run_all");
    }

    public void OnGui()
    {
    }

    public void OnSceneChanged(int buildIndex, string sceneName)
    {
        // Before Close, so it does not push the outgoing scene's input state onto the incoming one.
        GameUiState.Forget();
        Close();

        // Sprite, font and audio-clip residency is per-scene, and the thin sprites (trophy,
        // Rect shade) have a single referencing Image each in the menu scene. Rather than gamble on
        // whether the ones we bound survive the transition, the screen and every cached handle are
        // dropped here and re-harvested from the new scene on the next open.
        _screen.Destroy();
        GameFonts.Forget();
        GameSprites.Forget();
        GameSounds.Release();

        _injector.OnSceneChanged(sceneName);
        _dirty = true;
        _hintDirty = true;
        _reportedInjectionFailure = false;
    }

    /// <summary>
    /// ASCII only. The game's TMP font assets ship pre-generated SDF atlases, so a character the
    /// shipped UI never uses is not guaranteed to have a glyph in them.
    /// </summary>
    private static string Hint()
    {
        var status = TutorialDirector.Status;
        if (TutorialDirector.IsStarted && status.Length > 0)
            return status;

        return $"{ExpansionConfig.MenuHotkey} opens this in-game. Escape closes. " +
               $"Mouse wheel, PgUp/PgDn and Home/End scroll the list.";
    }

    /// <summary>
    /// Escape and typing mean different things with a picker up, so the picker gets first refusal on
    /// both. Without that, filtering an NPC list by name would trip the probe hotkey on every "p".
    /// </summary>
    private void PumpInput()
    {
        // Belt to GameUiState's braces: the state stack already frees the cursor, but on a build where
        // it could not be reached the game re-locks the cursor every frame during gameplay.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (_screen.ConsumeTyping())
        {
            if (Input.GetKeyDown(KeyCode.Escape))
                _screen.ClosePicker();

            return;
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Close();
            return;
        }

        if (Input.GetKeyDown(ExpansionConfig.ProbeHotkey))
            _probeRequested = true;

        if (_dirty)
        {
            _dirty = false;
            _screen.SyncFromRegistry(resetScroll: false);
        }

        if (_hintDirty)
        {
            _hintDirty = false;
            _screen.SetHint(Hint());
        }
    }

    /// <summary>
    /// Runs one queued click per frame. A picker action opens its list instead of running, and the
    /// rows are re-measured afterwards because a result can change what is available.
    /// </summary>
    private void DrainPending()
    {
        if (_pending.Count == 0)
            return;

        var pending = _pending.Dequeue();
        var action = pending.Action;

        if (pending.Choice is null && action.HasPicker)
        {
            _screen.OpenPicker(action);
            return;
        }

        if (pending.Choice is null)
            ActionRegistry.Invoke(action);
        else
            ActionRegistry.Invoke(action, pending.Choice);

        // Remeasured but not re-scrolled: a result can change a row's height, and the owner is still
        // looking at wherever in the list they just clicked.
        if (_open)
            _screen.SyncFromRegistry(resetScroll: false);
    }

    /// <summary>Builds the canvas on first open. A construction failure is the one case that really
    /// leaves the user with nothing, so it hands over to the IMGUI menu.</summary>
    private bool EnsureScreen()
    {
        if (_screen.IsBuilt)
            return true;

        try
        {
            // Harvested immediately before building, which is the latest point at which the current
            // scene's fonts and sprites are guaranteed to be resident.
            GameFonts.Harvest();
            GameSprites.Harvest();
            GameSounds.Harvest();

            _screen.EnsureBuilt();
            return true;
        }
        catch (Exception ex)
        {
            _broken = true;
            Log.Error("The native Expansions screen could not be built.", ex);
            ExpansionMenu.FallBackToPlaceholder($"the screen failed to build ({ex.GetType().Name})");
            return false;
        }
    }

    private readonly struct PendingAction
    {
        internal PendingAction(ExpansionAction action, ActionChoice? choice)
        {
            Action = action;
            Choice = choice;
        }

        internal ExpansionAction Action { get; }

        /// <summary>Null means "the owner clicked the row", not "the owner picked nothing".</summary>
        internal ActionChoice? Choice { get; }
    }
}

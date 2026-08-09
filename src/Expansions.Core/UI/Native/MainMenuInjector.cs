using Expansions.Core.Diagnostics;
using Expansions.Core.Logging;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Expansions.Core.UI.Native;

/// <summary>
/// Adds an "Expansions" entry to the main menu's nav column by cloning one of the game's own nav
/// buttons — the SideHustle pattern. No Harmony patch, no asset bundle, and it works before any save
/// is loaded, which is when "is Police Improvements on" has to be decided.
/// <para>
/// The root screen is found by its <c>OpenOnStart</c> flag rather than by a path like
/// <c>MainMenu/Home/Bank</c>, so a renamed GameObject does not break it. <c>MenuScreen</c> is reached
/// by name through reflection, keeping Core free of any compile-time <c>Assembly-CSharp</c> reference.
/// </para>
/// </summary>
internal sealed class MainMenuInjector
{
    /// <summary>Also the adoption key: a re-entered menu finds this and rewires it instead of stacking
    /// a second button.</summary>
    private const string CloneName = "Expansions_NavButton";

    private const string NavLabel = "Expansions";
    private const string MenuScreenType = "Il2CppScheduleOne.UI.MainMenu.MenuScreen";
    private const string SaveManagerType = "Il2CppScheduleOne.Persistence.SaveManager";
    private const string FallbackMenuScene = "Menu";

    /// <summary>
    /// SideHustle's value. Cloning and reparenting into the nav column while the menu's own
    /// UIScreen/UISelectable navigation is still iterating its selectables can corrupt it and hard
    /// crash, and heavy mods loading alongside shift that timing.
    /// </summary>
    private const int WarmupFrames = 20;

    private const int GiveUpFrames = 180;

    /// <summary>How often the idle state re-checks the active scene, in frames.</summary>
    private const int RearmInterval = 60;

    /// <summary>Settings has the most side-effect-free click, so it is the safest thing to clone.</summary>
    private static readonly string[] PreferredLabels =
    {
        "settings", "options", "load", "continue", "new game", "quit", "exit",
    };

    private readonly ModuleLogger _log;
    private readonly Action _onOpen;

    /// <summary>Rooted for as long as the cloned button lives. See <see cref="UiCallback"/>.</summary>
    private UiCallback? _openCallback;

    private GameObject? _clone;
    private int _frames;
    private InjectorState _state = InjectorState.Idle;
    private string _failure = string.Empty;
    private bool _everInjected;

    public MainMenuInjector(ModuleLogger log, Action onOpen)
    {
        _log = log;
        _onOpen = onOpen;
    }

    private enum InjectorState
    {
        Idle,
        Waiting,
        Injected,
        GaveUp,
    }

    /// <summary>True once the nav entry is live and still alive.</summary>
    public bool IsInjected => _state == InjectorState.Injected && InteropObjects.Alive(_clone);

    /// <summary>Set when the retry window closed without finding the menu. <see cref="Failure"/> says why.</summary>
    public bool GaveUp => _state == InjectorState.GaveUp;

    /// <summary>
    /// True once the entry has landed at least once this session. A later failure is then not worth
    /// giving up the native screen over — the hotkey still opens the same screen.
    /// </summary>
    public bool EverInjected => _everInjected;

    public string Failure => _failure;

    /// <summary>Arms the injector when the menu scene loads and stands down everywhere else.</summary>
    public void OnSceneChanged(string sceneName)
    {
        // Whatever we cloned belonged to the outgoing scene and has already been destroyed with it.
        _clone = null;
        _frames = 0;

        if (!string.Equals(sceneName, MenuSceneName(), StringComparison.Ordinal))
        {
            _state = InjectorState.Idle;
            return;
        }

        _state = InjectorState.Waiting;
        _failure = string.Empty;
    }

    /// <summary>One attempt per frame after the warm-up, until it lands or the window closes.</summary>
    public void Tick()
    {
        if (_state == InjectorState.Idle)
        {
            RearmIfInMenu();
            return;
        }

        if (_state != InjectorState.Waiting)
            return;

        _frames++;
        if (_frames < WarmupFrames)
            return;

        if (TryInject(out var failure))
        {
            _state = InjectorState.Injected;
            _everInjected = true;
            _failure = string.Empty;
            return;
        }

        if (_frames < GiveUpFrames)
            return;

        _state = InjectorState.GaveUp;
        _failure = failure;
    }

    public void Destroy()
    {
        if (InteropObjects.Alive(_clone))
            UnityEngine.Object.Destroy(_clone);

        _clone = null;
        _openCallback = null;
        _state = InjectorState.Idle;
    }

    /// <summary>
    /// <c>SaveManager</c> owns the scene-name constants, so read them off the game rather than
    /// hardcoding. Falls back to the literal if the member is gone or IL2CPP inlined it away.
    /// </summary>
    private static string MenuSceneName()
    {
        if (GameReflection.TryReadStatic(SaveManagerType, "MENU_SCENE_NAME", out var value, out _) &&
            value is string name && name.Length > 0)
        {
            return name;
        }

        return FallbackMenuScene;
    }

    private static int LabelScore(Button button)
    {
        var label = button.transform.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label == null || string.IsNullOrEmpty(label.text))
            return 0;

        var text = label.text.Trim();
        for (var i = 0; i < PreferredLabels.Length; i++)
        {
            if (text.Contains(PreferredLabels[i], StringComparison.OrdinalIgnoreCase))
                return PreferredLabels.Length - i;
        }

        return 0;
    }

    private static void SetLabel(GameObject go)
    {
        var label = go.transform.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label == null)
            return;

        label.text = NavLabel;
        // "Expansions" can be wider than the button it was cloned from; extend rather than wrap so it
        // does not break onto a second row the way none of the vanilla entries do.
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Overflow;
    }

    /// <summary>
    /// Insurance against a missed scene-load callback: without it the nav entry would silently never
    /// appear. Polls the active scene name a couple of times a second, which is cheap next to the
    /// per-frame injection attempts it guards.
    /// </summary>
    private void RearmIfInMenu()
    {
        if (++_frames < RearmInterval)
            return;

        _frames = 0;

        if (string.Equals(GameReflection.ActiveSceneName(), MenuSceneName(), StringComparison.Ordinal))
            _state = InjectorState.Waiting;
    }

    private bool TryInject(out string failure)
    {
        try
        {
            return Inject(out failure);
        }
        catch (Exception ex)
        {
            failure = $"{ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    private bool Inject(out string failure)
    {
        var home = FindHomeScreen(out failure);
        if (home == null)
            return false;

        var buttons = home.transform.GetComponentsInChildren<Button>(true);
        if (buttons == null || buttons.Length == 0)
        {
            failure = "the root menu screen has no Button children to clone.";
            return false;
        }

        if (Adopt(buttons))
            return true;

        var template = PickTemplate(buttons);
        if (template == null)
        {
            failure = "no usable nav-button template under the root menu screen.";
            return false;
        }

        var parent = template.transform.parent;
        if (parent == null)
        {
            failure = "the nav-button template has no parent to clone into.";
            return false;
        }

        // Deliberately the non-generic overload: it is a plain native call, where the generic form
        // depends on Instantiate<GameObject> having been instantiated in the IL2CPP binary.
        UnityEngine.Object original = template.gameObject;
        var clone = UnityEngine.Object.Instantiate(original, parent, false).Cast<GameObject>();
        clone.name = CloneName;
        clone.transform.localScale = Vector3.one;
        clone.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + 1);

        var button = clone.GetComponent<Button>();
        if (button == null)
        {
            UnityEngine.Object.Destroy(clone);
            failure = "the cloned nav button lost its Button component.";
            return false;
        }

        SetLabel(clone);
        Rewire(button);

        if (!clone.activeSelf)
            clone.SetActive(true);

        _clone = clone;
        _log.Msg($"Added the '{NavLabel}' entry to the main menu (cloned '{template.gameObject.name}').");
        return true;
    }

    /// <summary>The menu scene can re-initialise more than once per visit, so an existing entry is
    /// rewired rather than duplicated.</summary>
    private bool Adopt(Il2CppArrayBase<Button> buttons)
    {
        for (var i = 0; i < buttons.Length; i++)
        {
            var button = buttons[i];
            if (button == null || !string.Equals(button.gameObject.name, CloneName, StringComparison.Ordinal))
                continue;

            SetLabel(button.gameObject);
            Rewire(button);
            _clone = button.gameObject;
            _log.Debug("Adopted the existing main-menu entry instead of cloning a second one.");
            return true;
        }

        return false;
    }

    private Button? PickTemplate(Il2CppArrayBase<Button> buttons)
    {
        Button? best = null;
        var bestScore = int.MinValue;

        for (var i = 0; i < buttons.Length; i++)
        {
            var button = buttons[i];
            if (button == null || button.transform.parent == null)
                continue;

            if (string.Equals(button.gameObject.name, CloneName, StringComparison.Ordinal))
                continue;

            // Rank on the label first, then on how many Button siblings the parent has: the nav column
            // is by definition the container holding most of them.
            var score = (LabelScore(button) * 100) +
                        button.transform.parent.GetComponentsInChildren<Button>(true).Length;

            if (score <= bestScore)
                continue;

            bestScore = score;
            best = button;
        }

        return best;
    }

    /// <summary>
    /// Points the cloned button at our screen. <c>RemoveAllListeners()</c> alone is not enough:
    /// inspector-serialised persistent calls survive it, so each one is switched off individually.
    /// </summary>
    private void Rewire(Button button)
    {
        button.onClick.RemoveAllListeners();

        var persistent = button.onClick.GetPersistentEventCount();
        for (var i = 0; i < persistent; i++)
            button.onClick.SetPersistentListenerState(i, UnityEventCallState.Off);

        var callback = new UiCallback(_onOpen);
        _openCallback = callback;
        button.onClick.AddListener(callback.Listener);
        button.interactable = true;
    }

    /// <summary>
    /// The root main-menu screen. <c>OpenOnStart</c> is the flag the game itself uses to mark it and it
    /// needs no path string; the "most buttons" heuristic covers a build where the property is gone.
    /// </summary>
    private Component? FindHomeScreen(out string failure)
    {
        var menuScreen = GameReflection.FindType(MenuScreenType);
        if (menuScreen is null)
        {
            failure = $"'{MenuScreenType}' is not on this build.";
            return null;
        }

        var screens = InteropObjects.FindInScene(menuScreen);
        if (screens.Count == 0)
        {
            failure = "no MenuScreen instances are live yet.";
            return null;
        }

        Component? busiest = null;
        var busiestButtons = -1;

        foreach (var screen in screens)
        {
            var component = screen.TryCast<Component>();
            if (component == null)
                continue;

            if (InteropObjects.Read(screen, menuScreen, "OpenOnStart") is true)
            {
                failure = string.Empty;
                return component;
            }

            var count = component.transform.GetComponentsInChildren<Button>(true).Length;
            if (count <= busiestButtons)
                continue;

            busiestButtons = count;
            busiest = component;
        }

        if (busiest == null)
        {
            failure = $"none of the {screens.Count} live MenuScreens has a Button child.";
            return null;
        }

        failure = string.Empty;
        _log.Warn(
            $"No MenuScreen reports OpenOnStart among {screens.Count}; falling back to the one with the " +
            $"most buttons ('{busiest.gameObject.name}').");
        return busiest;
    }
}

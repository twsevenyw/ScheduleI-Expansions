using Expansions.Core.Actions;
using Expansions.Core.Configuration;
using Expansions.Core.Diagnostics;
using Expansions.Core.Game;
using Expansions.Core.Logging;
using Expansions.Core.UI;
using Expansions.Core.UI.Native;
using UnityEngine;

namespace Expansions.Core.Events;

/// <summary>
/// The event hotkey: Up arrow by default, configurable as <c>event_hotkey</c>.
/// <para>
/// Closed, this reads one key per frame and nothing else, so it cannot take input away from the game.
/// The chooser only opens during gameplay and only when the Expansions screen is not already up —
/// that screen owns the arrow keys for scrolling, and two surfaces fighting over Up would be worse
/// than no hotkey at all. While the chooser <em>is</em> up, gameplay input is suspended through the
/// game's own <c>StateProperties.UIDefault</c> preset, which is what stops the arrow keys walking the
/// player while they are being used to pick a row.
/// </para>
/// <para>
/// Two paths, deliberately: tap the key for the list (last-fired pre-selected, so Enter repeats), or
/// hold shift and tap it to re-fire the last event with no list at all. The second is the one for
/// testing a change over and over.
/// </para>
/// </summary>
public static class EventHotkey
{
    private static readonly ModuleLogger Log = new("Events");

    private static readonly TimedCache<GameSessionState> Session = new(0.5f, GameSessionState.Capture);

    private static readonly KeyCode[] DirectKeys =
    {
        KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5,
        KeyCode.Alpha6, KeyCode.Alpha7, KeyCode.Alpha8, KeyCode.Alpha9,
    };

    private static EventChooser? _chooser;
    private static bool _chooserBroken;
    private static bool _holdingUiState;

    /// <summary>True while the in-world chooser has the keyboard.</summary>
    public static bool IsChoosing => _chooser is { IsChoosing: true };

    /// <summary>Times the chooser has been raised this session. The tutorial baselines against it.</summary>
    public static int ChooserOpens { get; private set; }

    /// <summary>Times the shift fast path has re-fired an event this session.</summary>
    public static int Repeats { get; private set; }

    /// <summary>
    /// Why the chooser is unavailable, or empty. Non-empty means the hotkey has degraded to firing
    /// straight from the registry without a list.
    /// </summary>
    public static string Failure { get; private set; } = string.Empty;

    /// <summary>Per-frame pump, driven by <c>ExpansionHost.Update</c>. Never throws.</summary>
    internal static void Tick()
    {
        try
        {
            TickCore();
        }
        catch (Exception ex)
        {
            Log.Error("The event hotkey threw; closing the chooser and carrying on.", ex);
            CloseChooser();
        }
    }

    /// <summary>Drops the overlay so the next open re-harvests the incoming scene's fonts and sprites.</summary>
    internal static void OnSceneChanged()
    {
        ReleaseUiState();

        _chooser?.Destroy();
        _chooser = null;
    }

    internal static void Shutdown()
    {
        ReleaseUiState();

        _chooser?.Destroy();
        _chooser = null;
        _chooserBroken = false;
        Failure = string.Empty;
    }

    /// <summary>
    /// Opens the chooser from somewhere other than the hotkey — the Actions tab, or a mod that wants
    /// to hand the owner the list. Reports why if it could not.
    /// </summary>
    public static ActionResult OpenChooser()
    {
        if (!IsInGame())
            return ActionResult.Failed("The event chooser only opens in-game; load a save first.");

        var chooser = EnsureChooser();
        if (chooser is null)
            return ActionResult.Failed($"The event chooser could not be built ({Failure}).");

        ExpansionMenu.Close();
        chooser.OpenChooser();
        AcquireUiState();
        ChooserOpens++;
        return ActionResult.NoChange(string.Empty);
    }

    private static void TickCore()
    {
        var chooser = _chooser;

        if (chooser is not null && chooser.IsVisible)
        {
            // The Expansions screen opening over the top wins outright: it also wants the arrow keys.
            if (ExpansionMenu.Current.IsOpen)
            {
                CloseChooser();
                return;
            }

            chooser.Tick();

            if (chooser.IsChoosing)
            {
                PumpChooserInput(chooser);
                return;
            }
        }

        if (ExpansionMenu.Current.IsOpen)
            return;

        var key = ExpansionConfig.EventHotkey;
        if (key == KeyCode.None || !Input.GetKeyDown(key))
            return;

        // Gameplay only. In the main menu there is no world for an event to happen in, and the
        // arrow keys belong to the game's own menu navigation.
        if (!IsInGame())
            return;

        if (IsShiftHeld())
        {
            FireLastFromHotkey();
            return;
        }

        Open();
    }

    private static void Open()
    {
        var chooser = EnsureChooser();

        if (chooser is null)
        {
            // No list to show, so the key still does the most useful thing it can: repeat the last
            // event, or fire the only sensible candidate.
            FireWithoutChooser();
            return;
        }

        chooser.OpenChooser();
        AcquireUiState();
        ChooserOpens++;
        GameSounds.PlayClick();
    }

    private static void PumpChooserInput(EventChooser chooser)
    {
        chooser.TickScroll();
        chooser.PollHover();

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            CloseChooser();
            return;
        }

        var key = ExpansionConfig.EventHotkey;

        // The hotkey doubles as "move up" while the list is open, which is what makes Up-arrow feel
        // like one control rather than two. Shift plus the key still means repeat.
        if (key != KeyCode.None && Input.GetKeyDown(key))
        {
            if (IsShiftHeld())
            {
                CloseChooser();
                FireLastFromHotkey();
                return;
            }

            if (key != KeyCode.UpArrow)
                chooser.Move(-1);
        }

        if (Input.GetKeyDown(KeyCode.UpArrow))
            chooser.Move(-1);

        if (Input.GetKeyDown(KeyCode.DownArrow))
            chooser.Move(1);

        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            chooser.PickSelected();

        for (var i = 0; i < DirectKeys.Length; i++)
        {
            if (Input.GetKeyDown(DirectKeys[i]))
                chooser.PickIndex(i);
        }

        var picked = chooser.TakePick();
        if (picked is null)
            return;

        // Input goes back to the player before the event runs: an event can take a frame or two and
        // the owner should not be pinned in place while it does.
        ReleaseUiState();

        var result = EventRegistry.Fire(picked);
        chooser.ShowToast(Describe(picked, result), result.Outcome);
    }

    private static void FireLastFromHotkey()
    {
        Repeats++;

        if (EventRegistry.Count == 0)
        {
            Toast("No expansion events are registered yet.", ActionOutcome.Failed);
            return;
        }

        var last = EventRegistry.LastFired;
        if (last is null)
        {
            Toast(
                $"Nothing to repeat yet - press {ExpansionConfig.EventHotkey} on its own and pick an event first.",
                ActionOutcome.Failed);
            return;
        }

        // Fire already wrote the result to the output pane and the log, so this only draws it.
        var result = EventRegistry.Fire(last);
        EnsureChooser()?.ShowToast(Describe(last, result), result.Outcome);
    }

    /// <summary>
    /// The degraded path, used only when the overlay cannot be built. Repeats the last event, or
    /// fires the single available one when there is exactly one; otherwise it says why it cannot.
    /// </summary>
    private static void FireWithoutChooser()
    {
        var last = EventRegistry.LastFired;
        if (last is not null)
        {
            EventRegistry.Fire(last);
            return;
        }

        ExpansionEvent? only = null;
        var candidates = 0;

        foreach (var candidate in EventRegistry.Events)
        {
            if (!candidate.GetAvailability().IsAvailable)
                continue;

            candidates++;
            only ??= candidate;
        }

        if (candidates == 1 && only is not null)
        {
            EventRegistry.Fire(only);
            return;
        }

        ActionLog.Fail(candidates == 0
            ? $"No expansion event can run right now, and the chooser is unavailable ({Failure})."
            : $"{candidates} events are available but the chooser is unavailable ({Failure}); " +
              $"fire one from the Actions tab instead.");
    }

    private static void Toast(string message, ActionOutcome outcome)
    {
        ActionLog.Write(outcome, message);

        var chooser = EnsureChooser();
        chooser?.ShowToast(message, outcome);
    }

    private static string Describe(ExpansionEvent fired, ActionResult result) =>
        result.Message.Length > 0 ? result.Message : $"{fired.CurrentLabel()}: fired.";

    private static EventChooser? EnsureChooser()
    {
        if (_chooserBroken)
            return null;

        try
        {
            // Harvested immediately before building, which is the latest point at which the current
            // scene's fonts and sprites are guaranteed to be resident.
            if (_chooser is null || !_chooser.IsBuilt)
            {
                GameFonts.Harvest();
                GameSprites.Harvest();
                GameSounds.Harvest();
            }

            _chooser ??= new EventChooser(Log);
            _chooser.EnsureBuilt();
            Failure = string.Empty;
            return _chooser;
        }
        catch (Exception ex)
        {
            _chooserBroken = true;
            Failure = $"{ex.GetType().Name}: {ex.Message}";
            Log.Error(
                "The event chooser could not be built; the hotkey will repeat the last event instead " +
                "of showing a list.",
                ex);
            _chooser = null;
            return null;
        }
    }

    private static void CloseChooser()
    {
        ReleaseUiState();
        _chooser?.Hide();
    }

    private static void AcquireUiState()
    {
        if (_holdingUiState)
            return;

        _holdingUiState = true;
        GameUiState.Enter();
    }

    private static void ReleaseUiState()
    {
        if (!_holdingUiState)
            return;

        _holdingUiState = false;
        GameUiState.Exit();
    }

    private static bool IsShiftHeld() =>
        Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

    private static bool IsInGame()
    {
        try
        {
            return Session.Value.IsSaveLoaded;
        }
        catch
        {
            return false;
        }
    }
}

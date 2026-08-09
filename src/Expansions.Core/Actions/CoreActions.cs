using Expansions.Core.Diagnostics;
using Expansions.Core.Game;

namespace Expansions.Core.Actions;

/// <summary>
/// Registers Core's own actions — everything that used to need a console command, plus the console
/// itself.
/// <para>
/// These are process-lifetime, like the Core probes: they are not owned by a feature module and must
/// stay reachable whether or not any module is enabled. In particular, re-enabling the console has to
/// work on a save where the console is off, which is precisely when no other route exists.
/// </para>
/// </summary>
internal static class CoreActions
{
    private static IDisposable? _registration;

    /// <summary>
    /// Capturing the session state reaches through <c>LoadManager</c> by reflection, and every action's
    /// availability predicate wants the answer on every frame the screen is open.
    /// </summary>
    private static readonly TimedCache<GameSessionState> Session = new(0.5f, GameSessionState.Capture);

    internal static void RegisterAll()
    {
        if (_registration is not null)
            return;

        _registration = ActionRegistry.RegisterAll(
            ConsoleActions.Open(),
            ConsoleActions.Enable(),
            ConsoleActions.ShowBinding(),
            TeleportActions.ToNpc(),
            TeleportActions.ToMarcusVale(),
            ProbeActions.RunAll(),
            ProbeActions.RunArea(),
            ProbeActions.OpenNewestReport(),
            ProbeActions.ShowQuarantine(),
            ProbeActions.ClearQuarantine(),
            TutorialActions.StartOrRestart(),
            TutorialActions.Reset(),
            TutorialActions.ShowStatus(),
            FileActions.OpenSettings(),
            FileActions.OpenUserData(),
            UpdateActions.ShowStatus(),
            UpdateActions.ShowVersions(),
            UpdateActions.RevealUpdateFolder(),
            OutputActions.Clear());

        // Registered after Core's own rows so the mirrored event section lands beneath them, and so
        // the count logged below is the whole surface rather than half of it.
        EventActions.RegisterAll();

        ExpansionHost.Log.Msg(
            $"Actions: {ActionRegistry.Count} registered. Open the Expansions screen and pick the Actions tab; " +
            "nothing here needs the in-game console.");
    }

    internal static void Unregister()
    {
        EventActions.Unregister();
        _registration?.Dispose();
        _registration = null;
    }

    /// <summary>
    /// Shared availability predicate. Most actions are meaningless in the main menu, and saying so is
    /// more useful than a button that silently does nothing.
    /// </summary>
    internal static ActionAvailability RequiresLoadedSave()
    {
        try
        {
            return Session.Value.IsSaveLoaded
                ? ActionAvailability.Ready
                : ActionAvailability.Unavailable("needs a loaded save - you are in the main menu");
        }
        catch (Exception ex)
        {
            return ActionAvailability.Unavailable($"the save state could not be read ({ex.GetType().Name})");
        }
    }
}

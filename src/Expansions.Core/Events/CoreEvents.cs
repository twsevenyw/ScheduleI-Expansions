using Expansions.Core.Actions;
using Expansions.Core.Configuration;
using Expansions.Core.Diagnostics;
using Expansions.Core.Game;

namespace Expansions.Core.Events;

/// <summary>
/// The events Core registers itself.
/// <para>
/// They exist for a structural reason as much as a useful one: a chooser that is empty until a
/// feature mod happens to be installed and enabled reads as broken, and the tutorial chapter that
/// teaches the hotkey has to be completable on a bare install. These two are always there.
/// </para>
/// </summary>
internal static class CoreEvents
{
    private static readonly TimedCache<GameSessionState> Session = new(0.5f, GameSessionState.Capture);

    private static IDisposable? _registration;

    internal static void RegisterAll()
    {
        if (_registration is not null)
            return;

        _registration = EventRegistry.RegisterAll(
            Ping(),
            RunProbes());

        ExpansionHost.Log.Msg(
            $"Events: {EventRegistry.Count} registered. Press {ExpansionConfig.EventHotkey} in-game for the " +
            $"chooser, or Shift+{ExpansionConfig.EventHotkey} to repeat the last one.");
    }

    internal static void Unregister()
    {
        _registration?.Dispose();
        _registration = null;
    }

    /// <summary>
    /// Proves the wiring end to end without changing anything in the world, which is the first thing
    /// worth knowing when a hotkey appears to do nothing.
    /// </summary>
    private static ExpansionEvent Ping() => new(
        id: "core.ping",
        label: "Test the event hotkey",
        description: "Changes nothing. Confirms the key, the chooser and the output pane are wired up.",
        isAvailable: null,
        invoke: static () => ActionResult.Ok(
            $"Event hotkey works. {EventRegistry.Count} event(s) registered; " +
            $"Shift+{ExpansionConfig.EventHotkey} repeats whichever one you fired last."),
        order: 0);

    /// <summary>The diagnostics sweep, reachable without opening the menu.</summary>
    private static ExpansionEvent RunProbes() => new(
        id: "core.run_probes",
        label: "Run the diagnostics probes",
        description: "Same sweep as the Actions tab. Writes a Markdown report to UserData.",
        isAvailable: static () => Session.Value.IsSaveLoaded
            ? ActionAvailability.Ready
            : ActionAvailability.Unavailable("needs a loaded save"),
        invoke: static () =>
        {
            // The action reports its own result to the output pane, so this returns the documented
            // silent outcome rather than logging the same sentence twice.
            ActionRegistry.Invoke("core.probes.run_all");
            return ActionResult.NoChange(string.Empty);
        },
        order: 10);
}

using Expansions.Core.Game;

namespace Expansions.Core.Actions;

/// <summary>
/// The console actions. These are the reason this surface exists: on a save with
/// <c>ConsoleEnabled = false</c> there is no keypress that opens the console, so without a menu route
/// the console is unreachable and every console-only feature with it.
/// </summary>
internal static class ConsoleActions
{
    internal static ExpansionAction Open() => new(
        id: "core.console.open",
        label: "Enable and open the console",
        description:
        "Turns the console on for this save if it is off, then opens it. The flag lives in the save's " +
        "Game.json, so it stays on next time.",
        isAvailable: Reachable,
        invoke: () =>
        {
            var (ok, message) = GameConsole.Open();
            return ok ? ActionResult.Ok(message) : ActionResult.Failed(message);
        },
        order: 0);

    internal static ExpansionAction Enable() => new(
        id: "core.console.enable",
        label: "Enable the console (do not open it)",
        description: "Sets the per-save console flag without putting the window on screen.",
        isAvailable: Reachable,
        invoke: () =>
        {
            var enabled = GameConsole.IsEnabled();
            if (enabled == true)
                return ActionResult.NoChange($"The console is already enabled. Press the {GameConsole.Binding()}.");

            var (ok, message) = GameConsole.Enable();
            return ok
                ? ActionResult.Ok($"{message} Press the {GameConsole.Binding()} to open it.")
                : ActionResult.Failed(message);
        },
        order: 1);

    /// <summary>
    /// Reads the toggle key back off <c>ConsoleUI.ToggleConsoleReference</c> rather than quoting a
    /// constant. The binding goes through the new Input System, so a rebind in the game's own options
    /// would silently make any hardcoded answer wrong.
    /// </summary>
    internal static ExpansionAction ShowBinding() => new(
        id: "core.console.binding",
        label: "Show the console key binding",
        description: "Reads the live toggle binding out of the game's input asset.",
        isAvailable: null,
        invoke: () =>
        {
            var enabled = GameConsole.IsEnabled();
            var state = enabled switch
            {
                true => "The console is enabled on this save.",
                false => "The console is DISABLED on this save - the key will do nothing until you enable it above.",
                _ => "Whether the console is enabled cannot be read from here (no loaded save).",
            };

            return ActionResult.Ok($"{state} Toggle key: {GameConsole.Binding()}.");
        },
        order: 2);

    /// <summary>
    /// Available whenever a live <c>ConsoleUI</c> exists, carrying the current flag state and the toggle
    /// key as the row's note — the two things the owner needs and could not otherwise see.
    /// </summary>
    private static ActionAvailability Reachable()
    {
        var reason = GameConsole.UnavailableReason();
        return reason.Length == 0
            ? ActionAvailability.Available(GameConsole.StatusNote())
            : ActionAvailability.Unavailable(reason);
    }
}

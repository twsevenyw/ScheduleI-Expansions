using Expansions.Core.Actions;

namespace Expansions.Tweaks.AutoReorder;

/// <summary>
/// The off-phone surface: read the standing orders, stop them, and — if the phone's rows ever refuse
/// a tick box on some future build — arm one straight from the delivery history.
/// <para>
/// Ids are <c>tweaks.autoreorder.*</c>, which groups them under the Quality of Life module because
/// the section is resolved from the prefix before the first dot.
/// </para>
/// </summary>
internal static class AutoReorderActions
{
    internal static ExpansionAction[] All(AutoReorderSettings settings, Func<AutoReorderEngine?> engine) =>
        new[]
        {
            new ExpansionAction(
                id: "tweaks.autoreorder.list",
                label: "Show standing auto-reorders",
                description: "Lists every delivery set to repeat and what each one is waiting for.",
                isAvailable: () => Ready(engine),
                invoke: () => List(engine())),

            new ExpansionAction(
                id: "tweaks.autoreorder.arm",
                label: "Repeat a past delivery",
                description:
                    "Picks an order out of the phone's delivery history and sets it to repeat. Does the same " +
                    "thing as ticking its box in the Deliveries app.",
                choices: () => Choices(),
                invokeChoice: choice => Arm(engine(), choice),
                isAvailable: () => Ready(engine)),

            new ExpansionAction(
                id: "tweaks.autoreorder.stop_all",
                label: "Stop all auto-reorders",
                description: "Unticks every repeating delivery in this save. Nothing already on the road is cancelled.",
                isAvailable: () => Ready(engine),
                invoke: () => StopAll(engine())),
        };

    private static ActionAvailability Ready(Func<AutoReorderEngine?> engine)
    {
        if (engine() is null)
            return "the Quality of Life module is off";

        return AutoReorderState.BindToLoadedSave(out var reason) ? ActionAvailability.Ready : reason;
    }

    private static ActionResult List(AutoReorderEngine? engine)
    {
        if (engine is null)
            return ActionResult.Failed("Auto-reorder is not running.");

        var orders = engine.Orders;
        if (orders.Count == 0)
            return ActionResult.NoChange("Nothing is set to repeat. Tick a row in the phone's Deliveries app.");

        foreach (var order in orders)
        {
            var reason = order.WaitReason.Length == 0 ? "ready to fire" : order.WaitReason;
            ActionLog.Note($"{order.Summary} - placed {order.Data.PlacedCount}x - {reason}");
        }

        return ActionResult.Ok(
            engine.Blocker.Length > 0
                ? $"{orders.Count} standing order(s), all held up: {engine.Blocker}."
                : $"{orders.Count} standing order(s) listed above.");
    }

    private static ActionResult StopAll(AutoReorderEngine? engine)
    {
        if (engine is null)
            return ActionResult.Failed("Auto-reorder is not running.");

        var count = engine.DisarmAll();
        return count == 0
            ? ActionResult.NoChange("Nothing was set to repeat.")
            : ActionResult.Ok($"Stopped {count} standing auto-reorder(s). Deliveries already on the road still arrive.");
    }

    /// <summary>Every distinct order shape in the delivery history, newest first.</summary>
    private static IReadOnlyList<ActionChoice> Choices()
    {
        var manager = DeliveryApi.Manager();
        if (manager is null)
            return Array.Empty<ActionChoice>();

        var history = DeliveryApi.History(manager);
        var choices = new List<ActionChoice>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var i = history.Count - 1; i >= 0; i--)
        {
            if (!OrderShape.TryRead(history[i], out var contents) || contents.Items.Count == 0)
                continue;

            if (!seen.Add(contents.Key))
                continue;

            var armed = AutoReorderState.IsArmed(contents.Key) ? " (already repeating)" : string.Empty;

            choices.Add(new ActionChoice(
                contents.Key,
                $"{OrderShape.Describe(contents.Items, 3)} from {contents.Store}",
                $"to {(contents.Destination.Length == 0 ? "your property" : contents.Destination)}, " +
                $"bay {contents.Dock + 1}{armed}"));
        }

        return choices;
    }

    private static ActionResult Arm(AutoReorderEngine? engine, ActionChoice choice)
    {
        if (engine is null)
            return ActionResult.Failed("Auto-reorder is not running.");

        var manager = DeliveryApi.Manager();
        if (manager is null)
            return ActionResult.Failed("There is no loaded game to read the delivery history from.");

        foreach (var receipt in DeliveryApi.History(manager))
        {
            if (!OrderShape.TryRead(receipt, out var contents))
                continue;

            if (!string.Equals(contents.Key, choice.Id, StringComparison.Ordinal))
                continue;

            var key = engine.Arm(contents, string.Empty, out var message);
            return key.Length == 0 ? ActionResult.Failed(message) : ActionResult.Ok(message);
        }

        return ActionResult.Failed("That order is no longer in the delivery history.");
    }
}

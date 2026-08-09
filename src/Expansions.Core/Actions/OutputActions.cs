namespace Expansions.Core.Actions;

/// <summary>Housekeeping for the output pane itself.</summary>
internal static class OutputActions
{
    internal static ExpansionAction Clear() => new(
        id: "core.output.clear",
        label: "Clear the output below",
        description: "Empties the output pane. The MelonLoader log keeps everything.",
        isAvailable: () => ActionLog.Snapshot.Count == 0
            ? ActionAvailability.Unavailable("there is nothing to clear")
            : ActionAvailability.Ready,
        invoke: () =>
        {
            ActionLog.Clear();

            // Deliberately no result line: writing one would immediately refill what was just emptied.
            return ActionResult.NoChange(string.Empty);
        },
        order: 50);
}

using Expansions.Core.Configuration;
using Expansions.Core.Events;

namespace Expansions.Core.Actions;

/// <summary>
/// Puts every registered event on the Actions tab as well as behind the hotkey.
/// <para>
/// A hotkey nobody knows about is a feature nobody uses, so the events are mirrored as ordinary
/// action rows under one heading. They are mirrored rather than re-implemented: the row calls
/// <see cref="EventRegistry.Fire(ExpansionEvent)"/>, so the availability text, the output line and
/// the repeat target are the same whichever route the owner took.
/// </para>
/// <para>
/// Mirrored ids are prefixed with <c>core.event.</c> because an event and an action may legitimately
/// share a module-scoped id — a module can offer both "raid now" as an event and "explain the raid
/// rules" as an action.
/// </para>
/// </summary>
internal static class EventActions
{
    /// <summary>Heading the mirrored rows sit under. Sorts directly after Core's own section.</summary>
    private const string GroupTitle = "Expansion Events";

    private const string MirrorPrefix = "core.event.";

    private static readonly List<IDisposable> Mirrored = new();

    private static Action? _changedHandler;
    private static IDisposable? _controls;

    /// <summary>The two controls that are not one-per-event: open the chooser, repeat the last one.</summary>
    internal static void RegisterAll()
    {
        if (_controls is not null)
            return;

        _controls = ActionRegistry.RegisterAll(OpenChooser(), Repeat());

        _changedHandler = Sync;
        EventRegistry.Changed += _changedHandler;
        Sync();
    }

    internal static void Unregister()
    {
        if (_changedHandler is not null)
        {
            EventRegistry.Changed -= _changedHandler;
            _changedHandler = null;
        }

        DropMirrors();

        _controls?.Dispose();
        _controls = null;
    }

    /// <summary>
    /// Rebuilds the mirrored rows wholesale. The set changes only when a module is toggled, so the
    /// simple thing is also the cheap thing.
    /// </summary>
    private static void Sync()
    {
        DropMirrors();

        var order = 0;
        foreach (var expansionEvent in EventRegistry.Events)
            Mirrored.Add(ActionRegistry.Register(Mirror(expansionEvent, order++)));
    }

    private static void DropMirrors()
    {
        for (var i = Mirrored.Count - 1; i >= 0; i--)
        {
            try
            {
                Mirrored[i].Dispose();
            }
            catch (Exception ex)
            {
                ExpansionHost.Log.Debug($"Dropping a mirrored event row threw ({ex.GetType().Name}: {ex.Message}).");
            }
        }

        Mirrored.Clear();
    }

    private static ExpansionAction Mirror(ExpansionEvent expansionEvent, int order) =>
        ExpansionAction.WithDynamicLabel(
            id: MirrorPrefix + expansionEvent.Id,
            label: expansionEvent.CurrentLabel,
            description: Describe(expansionEvent),
            invoke: () => EventRegistry.Fire(expansionEvent),
            isAvailable: expansionEvent.GetAvailability,
            group: GroupTitle,
            order: order);

    private static string Describe(ExpansionEvent expansionEvent)
    {
        var owner = expansionEvent.Group.Length > 0
            ? expansionEvent.Group
            : ExpansionRegistry.Find(expansionEvent.ModuleId)?.DisplayName ?? string.Empty;

        var description = expansionEvent.Description.Length > 0
            ? expansionEvent.Description
            : "No description.";

        return owner.Length > 0 ? $"{description} ({owner})" : description;
    }

    private static ExpansionAction OpenChooser() => new(
        id: "core.events.open",
        label: "Open the event chooser",
        description: $"The same in-world list {ExpansionConfig.EventHotkey} raises during play. " +
                     $"Closes this screen so the list has the keyboard.",
        isAvailable: () => EventRegistry.Count == 0
            ? ActionAvailability.Unavailable("no events are registered")
            : ActionAvailability.Ready,
        invoke: EventHotkey.OpenChooser,
        order: 60);

    private static ExpansionAction Repeat() => ExpansionAction.WithDynamicLabel(
        id: "core.events.repeat",
        label: static () =>
        {
            var last = EventRegistry.LastFired;
            return last is null ? "Repeat the last event" : $"Repeat: {last.CurrentLabel()}";
        },
        description: $"Fires whatever you ran last. Shift+{ExpansionConfig.EventHotkey} does the same " +
                     $"thing in-game without opening anything.",
        isAvailable: static () => EventRegistry.LastFired is null
            ? ActionAvailability.Unavailable("no event has been fired yet")
            : ActionAvailability.Ready,
        invoke: EventRegistry.FireLast,
        order: 61);
}

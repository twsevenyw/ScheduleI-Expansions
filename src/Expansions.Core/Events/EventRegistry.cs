using Expansions.Core.Actions;

namespace Expansions.Core.Events;

/// <summary>
/// Process-wide list of triggerable events, the fourth of the registries a module contributes to
/// alongside <see cref="ActionRegistry"/>, <see cref="Diagnostics.ProbeRegistry"/> and
/// <see cref="Tutorial.TutorialRegistry"/>. Core ships a couple of cross-cutting events; each feature
/// module adds its own on enable and drops them on disable:
/// <code>Lifetime.Add(EventRegistry.Register(myEvent));</code>
/// <para>
/// Everything the owner can fire from the event hotkey goes through <see cref="Fire(ExpansionEvent)"/>,
/// so the chooser, the mirrored action rows and the repeat fast path can never disagree about what
/// happened or what was written to the output pane.
/// </para>
/// </summary>
public static class EventRegistry
{
    /// <summary>Prefix Core's own events use, and the group they land in.</summary>
    public const string CoreModuleId = "core";

    private static readonly Dictionary<string, ExpansionEvent> ById = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    private static ExpansionEvent[] _ordered = Array.Empty<ExpansionEvent>();
    private static EventGroup[] _groups = Array.Empty<EventGroup>();
    private static string _lastFiredId = string.Empty;
    private static int _firedCount;

    /// <summary>Fires whenever the event set changes, so an open chooser or menu rebuilds its rows.</summary>
    public static event Action? Changed;

    /// <summary>Fires after any event runs, with the event and its result.</summary>
    public static event Action<ExpansionEvent, ActionResult>? Fired;

    public static int Count => _ordered.Length;

    /// <summary>Ordered by group heading, then <see cref="ExpansionEvent.Order"/>, then label.</summary>
    public static IReadOnlyList<ExpansionEvent> Events => _ordered;

    /// <summary>The same events bucketed by owning module, which is how the chooser labels them.</summary>
    public static IReadOnlyList<EventGroup> Groups => _groups;

    /// <summary>
    /// Id of the last event fired this session, or empty. This is what the repeat fast path re-runs;
    /// it survives the event being re-registered, because it is stored as an id rather than a handle.
    /// </summary>
    public static string LastFiredId => _lastFiredId;

    /// <summary>The last fired event if it is still registered, else null.</summary>
    public static ExpansionEvent? LastFired => Find(_lastFiredId);

    /// <summary>Events actually run this session, however they were reached. Tutorial chapters
    /// baseline against this rather than subscribing to <see cref="Fired"/>.</summary>
    public static int FiredCount => _firedCount;

    /// <summary>
    /// Adds an event. Returns a token that removes it again; a duplicate id is rejected with a
    /// warning and an inert token rather than throwing, so one bad module cannot empty the chooser.
    /// </summary>
    public static IDisposable Register(ExpansionEvent expansionEvent)
    {
        if (expansionEvent is null)
            throw new ArgumentNullException(nameof(expansionEvent));

        if (expansionEvent.Invoke is null)
        {
            ExpansionHost.Log.Warn(
                $"Event '{expansionEvent.Id}' has no invoke callback; ignoring it.");
            return Registration.Inert;
        }

        lock (Gate)
        {
            if (ById.TryGetValue(expansionEvent.Id, out var existing))
            {
                ExpansionHost.Log.Warn(
                    $"Event id '{expansionEvent.Id}' is already held by '{existing.Label}'; " +
                    $"ignoring the duplicate '{expansionEvent.Label}'.");
                return Registration.Inert;
            }

            ById[expansionEvent.Id] = expansionEvent;
            Rebuild();
        }

        Announce();
        return new Registration(expansionEvent.Id);
    }

    /// <summary>Registers several at once, handing back one token that drops them all.</summary>
    public static IDisposable RegisterAll(params ExpansionEvent[] events)
    {
        var tokens = new List<IDisposable>(events.Length);
        foreach (var expansionEvent in events)
            tokens.Add(Register(expansionEvent));

        return new Bundle(tokens);
    }

    public static bool Unregister(string id)
    {
        if (string.IsNullOrEmpty(id))
            return false;

        lock (Gate)
        {
            if (!ById.Remove(id))
                return false;

            Rebuild();
        }

        Announce();
        return true;
    }

    public static ExpansionEvent? Find(string id)
    {
        if (string.IsNullOrEmpty(id))
            return null;

        lock (Gate)
            return ById.TryGetValue(id, out var found) ? found : null;
    }

    /// <summary>
    /// Runs an event by id, honouring its availability and never throwing. Everything the chooser and
    /// the mirrored action rows do goes through here.
    /// </summary>
    public static ActionResult Fire(string id)
    {
        var found = Find(id);
        return found is null
            ? Report(null, ActionResult.Failed($"No event is registered with id '{id}'."))
            : Fire(found);
    }

    public static ActionResult Fire(ExpansionEvent expansionEvent)
    {
        if (expansionEvent is null)
            throw new ArgumentNullException(nameof(expansionEvent));

        var availability = expansionEvent.GetAvailability();
        if (!availability.IsAvailable)
        {
            return Report(
                expansionEvent,
                ActionResult.Failed($"{expansionEvent.CurrentLabel()}: {availability.Reason}."),
                remember: false);
        }

        if (expansionEvent.Invoke is null)
            return Report(expansionEvent, ActionResult.Failed($"{expansionEvent.CurrentLabel()} has nothing to run."), remember: false);

        try
        {
            return Report(expansionEvent, expansionEvent.Invoke());
        }
        catch (Exception ex)
        {
            ExpansionHost.Log.Error($"Event '{expansionEvent.Id}' threw.", ex);
            return Report(expansionEvent, ActionResult.Error($"{expansionEvent.CurrentLabel()} failed", ex));
        }
    }

    /// <summary>
    /// Re-runs the last event fired this session. The whole point of the repeat fast path: testing a
    /// change to one event should be a keypress, not a keypress and two menu clicks.
    /// </summary>
    public static ActionResult FireLast()
    {
        var last = LastFired;
        if (last is not null)
            return Fire(last);

        return _lastFiredId.Length == 0
            ? ActionLogged(ActionResult.Failed("No event has been fired yet, so there is nothing to repeat."))
            : ActionLogged(ActionResult.Failed($"The last event ('{_lastFiredId}') is no longer registered."));
    }

    /// <summary>Forgets the repeat target. Used when the last event's module is switched off.</summary>
    public static void ForgetLastFired() => _lastFiredId = string.Empty;

    /// <summary>
    /// Mirrors the result into the on-screen output pane and the MelonLoader log, exactly the way
    /// <see cref="ActionRegistry"/> does, and records the repeat target.
    /// </summary>
    private static ActionResult Report(ExpansionEvent? expansionEvent, ActionResult result, bool remember = true)
    {
        if (expansionEvent is not null && remember)
        {
            _lastFiredId = expansionEvent.Id;
            _firedCount++;
        }

        if (result.Message.Length > 0)
            ActionLog.Write(result.Outcome, result.Message);
        else if (result.Outcome != ActionOutcome.NoChange)
            ActionLog.Write(result.Outcome, $"'{expansionEvent?.Id ?? "event"}' fired without saying anything.");

        if (expansionEvent is null)
            return result;

        try
        {
            Fired?.Invoke(expansionEvent, result);
        }
        catch (Exception ex)
        {
            ExpansionHost.Log.Debug($"An event listener threw ({ex.GetType().Name}: {ex.Message}).");
        }

        return result;
    }

    private static ActionResult ActionLogged(ActionResult result)
    {
        if (result.Message.Length > 0)
            ActionLog.Write(result.Outcome, result.Message);

        return result;
    }

    private static void Announce()
    {
        try
        {
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            ExpansionHost.Log.Debug($"An event-registry listener threw ({ex.GetType().Name}: {ex.Message}).");
        }
    }

    private static void Rebuild()
    {
        var ordered = new ExpansionEvent[ById.Count];
        ById.Values.CopyTo(ordered, 0);

        Array.Sort(ordered, static (a, b) =>
        {
            var byGroup = string.CompareOrdinal(GroupSortKey(a), GroupSortKey(b));
            if (byGroup != 0)
                return byGroup;

            var byOrder = a.Order.CompareTo(b.Order);
            if (byOrder != 0)
                return byOrder;

            var byLabel = string.CompareOrdinal(a.Label, b.Label);
            return byLabel != 0 ? byLabel : string.CompareOrdinal(a.Id, b.Id);
        });

        var groups = new List<EventGroup>();
        var current = new List<ExpansionEvent>();
        var currentKey = string.Empty;

        foreach (var expansionEvent in ordered)
        {
            var key = GroupSortKey(expansionEvent);
            if (current.Count > 0 && !string.Equals(key, currentKey, StringComparison.Ordinal))
            {
                groups.Add(new EventGroup(GroupTitle(current[0]), current.ToArray()));
                current.Clear();
            }

            currentKey = key;
            current.Add(expansionEvent);
        }

        if (current.Count > 0)
            groups.Add(new EventGroup(GroupTitle(current[0]), current.ToArray()));

        _ordered = ordered;
        _groups = groups.ToArray();
    }

    /// <summary>Core sorts first, then the feature modules alphabetically.</summary>
    private static string GroupSortKey(ExpansionEvent expansionEvent)
    {
        if (expansionEvent.Group.Length > 0)
            return "1" + expansionEvent.Group;

        return string.Equals(expansionEvent.ModuleId, CoreModuleId, StringComparison.OrdinalIgnoreCase)
            ? "0"
            : "1" + GroupTitle(expansionEvent);
    }

    /// <summary>
    /// The heading. A registered module supplies its own display name, so "police_overhaul" shows as
    /// "Police Improvements" the way it does on its toggle card.
    /// </summary>
    private static string GroupTitle(ExpansionEvent expansionEvent)
    {
        if (expansionEvent.Group.Length > 0)
            return expansionEvent.Group;

        if (string.Equals(expansionEvent.ModuleId, CoreModuleId, StringComparison.OrdinalIgnoreCase))
            return "Expansions Core";

        var module = ExpansionRegistry.Find(expansionEvent.ModuleId);
        if (module is not null && !string.IsNullOrWhiteSpace(module.DisplayName))
            return module.DisplayName;

        return Titleize(expansionEvent.ModuleId);
    }

    private static string Titleize(string moduleId)
    {
        var words = moduleId.Split(new[] { '_', '-', '.' }, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < words.Length; i++)
        {
            var word = words[i];
            words[i] = word.Length <= 1 ? word.ToUpperInvariant() : char.ToUpperInvariant(word[0]) + word[1..];
        }

        return words.Length == 0 ? moduleId : string.Join(" ", words);
    }

    private sealed class Registration : IDisposable
    {
        internal static readonly IDisposable Inert = new Registration(null);

        private string? _id;

        internal Registration(string? id) => _id = id;

        public void Dispose()
        {
            var id = Interlocked.Exchange(ref _id, null);
            if (id is not null)
                Unregister(id);
        }
    }

    private sealed class Bundle : IDisposable
    {
        private List<IDisposable>? _tokens;

        internal Bundle(List<IDisposable> tokens) => _tokens = tokens;

        public void Dispose()
        {
            var tokens = Interlocked.Exchange(ref _tokens, null);
            if (tokens is null)
                return;

            // Reverse, matching ModuleLifetime: last registered is first dropped.
            for (var i = tokens.Count - 1; i >= 0; i--)
                tokens[i].Dispose();
        }
    }
}

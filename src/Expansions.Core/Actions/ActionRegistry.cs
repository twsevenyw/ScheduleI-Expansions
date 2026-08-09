namespace Expansions.Core.Actions;

/// <summary>
/// Process-wide action list, the third of the registries a module contributes to alongside
/// <see cref="Diagnostics.ProbeRegistry"/> and <see cref="Tutorial.TutorialRegistry"/>. Core ships the
/// cross-cutting actions; each feature module adds its own on enable and drops them on disable:
/// <code>Lifetime.Add(ActionRegistry.Register(myAction));</code>
/// </summary>
public static class ActionRegistry
{
    /// <summary>Prefix Core's own actions use, and the group they land in.</summary>
    public const string CoreModuleId = "core";

    private static readonly Dictionary<string, ExpansionAction> ById = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    private static ExpansionAction[] _ordered = Array.Empty<ExpansionAction>();
    private static ActionGroup[] _groups = Array.Empty<ActionGroup>();

    /// <summary>Fires whenever the action set changes, so an open menu rebuilds its rows.</summary>
    public static event Action? Changed;

    public static int Count => _ordered.Length;

    /// <summary>Ordered by group heading, then <see cref="ExpansionAction.Order"/>, then label.</summary>
    public static IReadOnlyList<ExpansionAction> Actions => _ordered;

    /// <summary>The same actions bucketed by owning module, which is how the UI lays them out.</summary>
    public static IReadOnlyList<ActionGroup> Groups => _groups;

    /// <summary>
    /// Adds an action. Returns a token that removes it again; a duplicate id is rejected with a
    /// warning and an inert token rather than throwing, so one bad module cannot empty the screen.
    /// </summary>
    public static IDisposable Register(ExpansionAction action)
    {
        if (action is null)
            throw new ArgumentNullException(nameof(action));

        if (action.Invoke is null && !action.HasPicker)
        {
            ExpansionHost.Log.Warn(
                $"Action '{action.Id}' has neither an invoke callback nor a picker; ignoring it.");
            return Registration.Inert;
        }

        lock (Gate)
        {
            if (ById.TryGetValue(action.Id, out var existing))
            {
                ExpansionHost.Log.Warn(
                    $"Action id '{action.Id}' is already held by '{existing.Label}'; ignoring the duplicate '{action.Label}'.");
                return Registration.Inert;
            }

            ById[action.Id] = action;
            Rebuild();
        }

        Changed?.Invoke();
        return new Registration(action.Id);
    }

    /// <summary>Registers several at once, handing back one token that drops them all.</summary>
    public static IDisposable RegisterAll(params ExpansionAction[] actions)
    {
        var tokens = new List<IDisposable>(actions.Length);
        foreach (var action in actions)
            tokens.Add(Register(action));

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

        Changed?.Invoke();
        return true;
    }

    public static ExpansionAction? Find(string id)
    {
        if (string.IsNullOrEmpty(id))
            return null;

        lock (Gate)
            return ById.TryGetValue(id, out var action) ? action : null;
    }

    /// <summary>
    /// Runs an action by id, honouring its availability and never throwing. Everything the UI shows
    /// goes through here, so the result and the output-pane line cannot disagree.
    /// </summary>
    public static ActionResult Invoke(string id)
    {
        var action = Find(id);
        return action is null
            ? Report(id, ActionResult.Failed($"No action is registered with id '{id}'."))
            : Invoke(action);
    }

    public static ActionResult Invoke(ExpansionAction action)
    {
        if (action is null)
            throw new ArgumentNullException(nameof(action));

        var availability = action.GetAvailability();
        if (!availability.IsAvailable)
            return Report(action.Id, ActionResult.Failed($"{action.CurrentLabel()}: {availability.Reason}."));

        if (action.Invoke is null)
        {
            return Report(
                action.Id,
                ActionResult.Failed($"{action.CurrentLabel()} needs a target; pick one from its list."));
        }

        try
        {
            return Report(action.Id, action.Invoke());
        }
        catch (Exception ex)
        {
            ExpansionHost.Log.Error($"Action '{action.Id}' threw.", ex);
            return Report(action.Id, ActionResult.Error($"{action.CurrentLabel()} failed", ex));
        }
    }

    /// <summary>Runs a picker action against the entry the owner chose.</summary>
    public static ActionResult Invoke(ExpansionAction action, ActionChoice choice)
    {
        if (action is null)
            throw new ArgumentNullException(nameof(action));

        if (action.InvokeChoice is null)
            return Invoke(action);

        var availability = action.GetAvailability();
        if (!availability.IsAvailable)
            return Report(action.Id, ActionResult.Failed($"{action.CurrentLabel()}: {availability.Reason}."));

        try
        {
            return Report(action.Id, action.InvokeChoice(choice));
        }
        catch (Exception ex)
        {
            ExpansionHost.Log.Error($"Action '{action.Id}' threw for choice '{choice.Id}'.", ex);
            return Report(action.Id, ActionResult.Error($"{action.CurrentLabel()} failed", ex));
        }
    }

    /// <summary>A picker's entries, never throwing: a list that cannot be built is an empty one.</summary>
    public static IReadOnlyList<ActionChoice> ChoicesFor(ExpansionAction action)
    {
        if (action?.Choices is null)
            return Array.Empty<ActionChoice>();

        try
        {
            return action.Choices() ?? Array.Empty<ActionChoice>();
        }
        catch (Exception ex)
        {
            ExpansionHost.Log.Warn($"Action '{action.Id}' could not list its choices ({GameReflectionMessage(ex)}).");
            return Array.Empty<ActionChoice>();
        }
    }

    private static string GameReflectionMessage(Exception exception) =>
        Diagnostics.GameReflection.Unwrap(exception);

    /// <summary>
    /// Mirrors the result into the on-screen output pane and the MelonLoader log. A silent
    /// <see cref="ActionOutcome.NoChange"/> is the one case that writes nothing — it is how an action
    /// that manages the pane itself opts out. Any other empty message is an authoring mistake worth
    /// surfacing, because a button with no visible response looks broken.
    /// </summary>
    private static ActionResult Report(string id, ActionResult result)
    {
        if (result.Message.Length > 0)
            ActionLog.Write(result.Outcome, result.Message);
        else if (result.Outcome != ActionOutcome.NoChange)
            ActionLog.Write(result.Outcome, $"'{id}' finished without saying anything.");

        return result;
    }

    private static void Rebuild()
    {
        var ordered = new ExpansionAction[ById.Count];
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

        var groups = new List<ActionGroup>();
        var current = new List<ExpansionAction>();
        var currentKey = string.Empty;

        foreach (var action in ordered)
        {
            var key = GroupSortKey(action);
            if (current.Count > 0 && !string.Equals(key, currentKey, StringComparison.Ordinal))
            {
                groups.Add(new ActionGroup(GroupTitle(current[0]), current.ToArray()));
                current.Clear();
            }

            currentKey = key;
            current.Add(action);
        }

        if (current.Count > 0)
            groups.Add(new ActionGroup(GroupTitle(current[0]), current.ToArray()));

        _ordered = ordered;
        _groups = groups.ToArray();
    }

    /// <summary>
    /// Core sorts first, then the feature modules alphabetically. Prefixing rather than special-casing
    /// keeps the comparison a single string compare.
    /// </summary>
    private static string GroupSortKey(ExpansionAction action)
    {
        if (action.Group.Length > 0)
            return "1" + action.Group;

        return string.Equals(action.ModuleId, CoreModuleId, StringComparison.OrdinalIgnoreCase)
            ? "0"
            : "1" + GroupTitle(action);
    }

    /// <summary>
    /// The heading. A registered module supplies its own display name, so "police_overhaul" shows as
    /// "Police Improvements" the way it does on its toggle card.
    /// </summary>
    private static string GroupTitle(ExpansionAction action)
    {
        if (action.Group.Length > 0)
            return action.Group;

        if (string.Equals(action.ModuleId, CoreModuleId, StringComparison.OrdinalIgnoreCase))
            return "Expansions Core";

        var module = ExpansionRegistry.Find(action.ModuleId);
        if (module is not null && !string.IsNullOrWhiteSpace(module.DisplayName))
            return module.DisplayName;

        return Titleize(action.ModuleId);
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

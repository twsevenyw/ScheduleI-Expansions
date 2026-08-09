namespace Expansions.Core.Actions;

/// <summary>
/// One thing the owner can do from the Expansions screen, replacing what used to need a console
/// command.
/// <para>
/// The module-facing shape is deliberately five named arguments:
/// <code>
/// Lifetime.Add(ActionRegistry.Register(new ExpansionAction(
///     id: "special_customers.tp_visitor",
///     label: "Teleport to Marcus Vale",
///     description: "Puts you a step in front of the Northtown motel visitor.",
///     isAvailable: () => VisitorRuntime.StatusOf(slot).WrapperResolved,
///     invoke: () => Teleport())));
/// </code>
/// <see cref="ActionAvailability"/> and <see cref="ActionResult"/> both convert implicitly from
/// <c>string</c> (and <c>bool</c>), so a predicate can be a bare boolean and an invoke body can just
/// return the sentence the owner should read.
/// </para>
/// </summary>
public sealed class ExpansionAction
{
    private const string DefaultUnavailableReason = "not available right now";

    /// <summary>The ordinary form: a button that does something and reports what it did.</summary>
    public ExpansionAction(
        string id,
        string label,
        string description,
        Func<ActionAvailability>? isAvailable = null,
        Func<ActionResult>? invoke = null,
        string group = "",
        int order = 0)
        : this(id, label, description, isAvailable, invoke, null, null, null, group, order)
    {
    }

    /// <summary>
    /// For a body that has nothing to report. The owner still sees a confirmation line, because a
    /// button that produces no visible response is indistinguishable from a broken one.
    /// </summary>
    public ExpansionAction(
        string id,
        string label,
        string description,
        Action invoke,
        Func<ActionAvailability>? isAvailable = null,
        string group = "",
        int order = 0)
        : this(
            id,
            label,
            description,
            isAvailable,
            invoke is null ? null : () => Run(invoke, label),
            null,
            null,
            null,
            group,
            order)
    {
    }

    /// <summary>
    /// The picker form: clicking opens a searchable list and the choice is passed to
    /// <paramref name="invokeChoice"/>. Use it whenever the action needs a target it cannot guess.
    /// </summary>
    public ExpansionAction(
        string id,
        string label,
        string description,
        Func<IReadOnlyList<ActionChoice>> choices,
        Func<ActionChoice, ActionResult> invokeChoice,
        Func<ActionAvailability>? isAvailable = null,
        string group = "",
        int order = 0)
        : this(id, label, description, isAvailable, null, choices, invokeChoice, null, group, order)
    {
    }

    private ExpansionAction(
        string id,
        string label,
        string description,
        Func<ActionAvailability>? isAvailable,
        Func<ActionResult>? invoke,
        Func<IReadOnlyList<ActionChoice>>? choices,
        Func<ActionChoice, ActionResult>? invokeChoice,
        Func<string>? dynamicLabel,
        string group,
        int order)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Action Id must be a non-empty, stable string.", nameof(id));

        Id = id.Trim();
        Label = string.IsNullOrWhiteSpace(label) ? Id : label.Trim();
        Description = description?.Trim() ?? string.Empty;
        Availability = isAvailable;
        Invoke = invoke;
        Choices = choices;
        InvokeChoice = invokeChoice;
        DynamicLabel = dynamicLabel;
        Order = order;
        ModuleId = ModuleIdFromId(Id);
        Group = string.IsNullOrWhiteSpace(group) ? string.Empty : group.Trim();
    }

    /// <summary>Stable, lowercase, <c>&lt;module_id&gt;.&lt;verb&gt;</c>. Never shown to the owner.</summary>
    public string Id { get; }

    /// <summary>The module id the <see cref="Id"/> prefix names, used to group and title the section.</summary>
    public string ModuleId { get; }

    /// <summary>Explicit section heading. Empty means "work it out from <see cref="ModuleId"/>".</summary>
    public string Group { get; }

    public string Label { get; }

    /// <summary>One line under the label. Say what it does, not that it is a button.</summary>
    public string Description { get; }

    /// <summary>Sort key inside the group; ties fall back to the label.</summary>
    public int Order { get; }

    public Func<ActionAvailability>? Availability { get; }

    public Func<ActionResult>? Invoke { get; }

    public Func<IReadOnlyList<ActionChoice>>? Choices { get; }

    public Func<ActionChoice, ActionResult>? InvokeChoice { get; }

    /// <summary>Overrides <see cref="Label"/> per frame, for a button whose verb changes with state.</summary>
    public Func<string>? DynamicLabel { get; }

    /// <summary>True when clicking opens a picker rather than running immediately.</summary>
    public bool HasPicker => Choices is not null && InvokeChoice is not null;

    /// <summary>
    /// For a button whose verb changes with state — "Enable Quest" until the line has been started, then
    /// "Restart Quest". The label is re-read on every frame the screen is up.
    /// </summary>
    public static ExpansionAction WithDynamicLabel(
        string id,
        Func<string> label,
        string description,
        Func<ActionResult> invoke,
        Func<ActionAvailability>? isAvailable = null,
        string group = "",
        int order = 0) =>
        new(id, label(), description, isAvailable, invoke, null, null, label, group, order);

    /// <summary>
    /// Availability, never throwing. A predicate that blows up must grey the button out with the
    /// reason rather than take the screen down.
    /// </summary>
    public ActionAvailability GetAvailability()
    {
        if (Availability is null)
            return ActionAvailability.Ready;

        try
        {
            var availability = Availability();
            return availability.IsAvailable || availability.Reason.Length > 0
                ? availability
                : ActionAvailability.Unavailable(DefaultUnavailableReason);
        }
        catch (Exception ex)
        {
            return ActionAvailability.Unavailable($"its availability check threw ({ex.GetType().Name})");
        }
    }

    /// <summary>The label to draw right now.</summary>
    public string CurrentLabel()
    {
        if (DynamicLabel is null)
            return Label;

        try
        {
            var label = DynamicLabel();
            return string.IsNullOrWhiteSpace(label) ? Label : label;
        }
        catch
        {
            return Label;
        }
    }

    private static ActionResult Run(Action body, string label)
    {
        body();
        return ActionResult.Ok($"{label}: done.");
    }

    /// <summary>The id up to the first dot. <c>core.probes.run_all</c> belongs to <c>core</c>.</summary>
    private static string ModuleIdFromId(string id)
    {
        var dot = id.IndexOf('.');
        return dot > 0 ? id[..dot] : id;
    }
}

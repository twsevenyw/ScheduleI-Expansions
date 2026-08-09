using Expansions.Core.Actions;

namespace Expansions.Core.Events;

/// <summary>
/// One thing a module can make happen on demand — a group of special customers arriving, a raid, a
/// driver run — bound to the event hotkey and listed on the Expansions screen.
/// <para>
/// The module-facing shape is deliberately identical to <see cref="ExpansionAction"/>, down to reusing
/// <see cref="ActionAvailability"/> and <see cref="ActionResult"/>, so there is one set of conventions
/// to learn rather than two that drift:
/// <code>
/// Lifetime.Add(EventRegistry.Register(new ExpansionEvent(
///     id: "special_customers.force_arrival",
///     label: "Bring a group into town",
///     description: "Skips the wait and warps the next scheduled group in now.",
///     isAvailable: () =&gt; Director.CanForceArrival,
///     invoke: () =&gt; Director.ForceArrival())));
/// </code>
/// </para>
/// <para>
/// An event is not just an action with a different name: actions are the owner's console replacement
/// and are read from a 46-row list, whereas events are the things worth reaching for mid-play without
/// opening a menu. Keeping them in their own registry is what makes a single hotkey useful.
/// </para>
/// </summary>
public sealed class ExpansionEvent
{
    private const string DefaultUnavailableReason = "not available right now";

    /// <summary>The ordinary form: fire it and report what happened.</summary>
    public ExpansionEvent(
        string id,
        string label,
        string description,
        Func<ActionAvailability>? isAvailable = null,
        Func<ActionResult>? invoke = null,
        string group = "",
        int order = 0)
        : this(id, label, description, isAvailable, invoke, null, group, order)
    {
    }

    /// <summary>
    /// For a body that has nothing to report. The owner still sees a confirmation line: an event that
    /// fires silently is indistinguishable from one that did not fire.
    /// </summary>
    public ExpansionEvent(
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
            group,
            order)
    {
    }

    private ExpansionEvent(
        string id,
        string label,
        string description,
        Func<ActionAvailability>? isAvailable,
        Func<ActionResult>? invoke,
        Func<string>? dynamicLabel,
        string group,
        int order)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Event Id must be a non-empty, stable string.", nameof(id));

        Id = id.Trim();
        Label = string.IsNullOrWhiteSpace(label) ? Id : label.Trim();
        Description = description?.Trim() ?? string.Empty;
        Availability = isAvailable;
        Invoke = invoke;
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

    /// <summary>One line under the label in the chooser. Say what will happen in the world.</summary>
    public string Description { get; }

    /// <summary>Sort key inside the group; ties fall back to the label.</summary>
    public int Order { get; }

    public Func<ActionAvailability>? Availability { get; }

    public Func<ActionResult>? Invoke { get; }

    /// <summary>Overrides <see cref="Label"/> per read, for an event whose verb changes with state.</summary>
    public Func<string>? DynamicLabel { get; }

    /// <summary>For an event whose label depends on state — "Bring the bikers in" vs "Send them home".</summary>
    public static ExpansionEvent WithDynamicLabel(
        string id,
        Func<string> label,
        string description,
        Func<ActionResult> invoke,
        Func<ActionAvailability>? isAvailable = null,
        string group = "",
        int order = 0) =>
        new(id, label(), description, isAvailable, invoke, label, group, order);

    /// <summary>
    /// Availability, never throwing. A predicate that blows up greys the row out with the reason
    /// rather than taking the chooser down mid-keypress.
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
        return ActionResult.Ok($"{label}: fired.");
    }

    /// <summary>The id up to the first dot. <c>special_customers.force_arrival</c> belongs to
    /// <c>special_customers</c>.</summary>
    private static string ModuleIdFromId(string id)
    {
        var dot = id.IndexOf('.');
        return dot > 0 ? id[..dot] : id;
    }
}

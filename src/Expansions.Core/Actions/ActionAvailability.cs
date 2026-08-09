namespace Expansions.Core.Actions;

/// <summary>
/// Whether an action can run right now, and — when it cannot — the reason the UI shows beside the
/// greyed-out button.
/// <para>
/// The implicit conversions exist so a module can declare availability the shortest honest way:
/// <c>() =&gt; true</c>, <c>() =&gt; SomeBool</c>, or <c>() =&gt; "needs a loaded save"</c>. A string
/// is always a refusal — there is nothing to explain about an action that works.
/// </para>
/// </summary>
public readonly struct ActionAvailability
{
    private ActionAvailability(bool isAvailable, string reason)
    {
        IsAvailable = isAvailable;
        Reason = reason ?? string.Empty;
    }

    /// <summary>Ready to run, nothing to explain.</summary>
    public static ActionAvailability Ready => new(true, string.Empty);

    public bool IsAvailable { get; }

    /// <summary>Empty when <see cref="IsAvailable"/>. Lower case, no trailing stop: it is rendered inline.</summary>
    public string Reason { get; }

    public static ActionAvailability Unavailable(string reason) =>
        new(false, string.IsNullOrWhiteSpace(reason) ? "not available right now" : reason.Trim());

    /// <summary>Available with a note the UI shows anyway, e.g. "host only - you are the host".</summary>
    public static ActionAvailability Available(string note) => new(true, note?.Trim() ?? string.Empty);

    public static implicit operator ActionAvailability(bool available) =>
        available ? Ready : Unavailable("not available right now");

    public static implicit operator ActionAvailability(string reason) => Unavailable(reason);
}

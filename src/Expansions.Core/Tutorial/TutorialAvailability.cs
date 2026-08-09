namespace Expansions.Core.Tutorial;

/// <summary>
/// Whether the thing a chapter teaches is reachable right now.
/// <para>
/// A chapter that reports <see cref="ComingSoon"/> is <em>deferred</em>, not stubbed: it contributes
/// no quest and no journal objectives, the line plays on past it, and the director comes back to it
/// as soon as it reports itself ready. It is never ticked off on the player's behalf, so a chapter
/// whose module is switched off mid-line is still there to play when it is switched back on.
/// </para>
/// <para>
/// Answer it however the world actually is, including transiently. It is asked repeatedly rather than once,
/// so a false during a save load or before a module has wired itself in costs nothing: the chapter is
/// stepped over and picked up on a later tick, with no restart and no tutorial reset. There is no need to
/// hedge, to answer <see cref="Available"/> defensively, or to withdraw the chapter over a condition that
/// will clear on its own — withdraw only when the chapter has no business being in the line at all, such as
/// a mod that is not installed on this machine.
/// </para>
/// <para>
/// The reason is shown verbatim on the Tutorial tab, so write it as the answer to "why can't I do
/// this yet" — "Special Customers is switched off", not "unavailable".
/// </para>
/// </summary>
public readonly struct TutorialAvailability
{
    private TutorialAvailability(bool isAvailable, string reason)
    {
        IsAvailable = isAvailable;
        Reason = reason;
    }

    public bool IsAvailable { get; }

    /// <summary>Player-facing explanation, shown as the objective title when unavailable.</summary>
    public string Reason { get; }

    public static TutorialAvailability Available { get; } = new(true, string.Empty);

    public static TutorialAvailability ComingSoon(string reason) =>
        new(false, string.IsNullOrWhiteSpace(reason) ? "not implemented yet" : reason);
}

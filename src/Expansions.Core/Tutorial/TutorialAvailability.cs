namespace Expansions.Core.Tutorial;

/// <summary>
/// Whether the feature a chapter teaches actually exists on this install.
/// <para>
/// The three feature mods are planned before they are written, so their chapters ship as metadata
/// long before there is anything to do in them. A chapter that reports <see cref="ComingSoon"/> still
/// appears in the quest line — the director turns it into one self-completing objective carrying the
/// reason, rather than an objective the player can never tick off.
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

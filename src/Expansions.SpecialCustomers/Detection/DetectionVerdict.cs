namespace Expansions.SpecialCustomers.Detection;

/// <summary>What the detector decided, and everything needed to explain it to the player.</summary>
internal sealed class DetectionVerdict
{
    internal DetectionVerdict(DetectionOutcome outcome, int score, string mode, string reason, IReadOnlyList<string> evidence)
    {
        Outcome = outcome;
        Score = score;
        Mode = mode;
        Reason = reason;
        Evidence = evidence;
    }

    /// <summary>Before the first evaluation the module behaves as if enabled but reports "not run".</summary>
    internal static DetectionVerdict NotRun { get; } = new(
        DetectionOutcome.Enabled,
        0,
        "auto",
        "detection has not run yet",
        Array.Empty<string>());

    internal DetectionOutcome Outcome { get; }

    internal int Score { get; }

    internal string Mode { get; }

    internal string Reason { get; }

    internal IReadOnlyList<string> Evidence { get; }

    internal bool IsEnabled => Outcome == DetectionOutcome.Enabled;

    internal string Headline => Outcome switch
    {
        DetectionOutcome.Enabled => $"active (score {Score}, {Mode})",
        DetectionOutcome.DisabledOfficial => $"disabled — the official feature looks present (score {Score})",
        DetectionOutcome.DisabledAmbiguous => $"disabled — the evidence is ambiguous (score {Score})",
        DetectionOutcome.DisabledByUser => "disabled by detection_mode = always_off",
        _ => $"disabled (score {Score})",
    };
}

internal enum DetectionOutcome
{
    Enabled,
    DisabledOfficial,
    DisabledAmbiguous,
    DisabledByUser,
}

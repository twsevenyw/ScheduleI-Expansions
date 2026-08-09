using UnityEngine;

namespace Expansions.SpecialCustomers.Archetypes;

/// <summary>
/// Observed ranges from the 117 readable <c>AvatarSettings</c> in
/// <c>research/raw/avatars-dump-2026-08-03.json</c>. Anything outside these is wrong by definition —
/// writing it is how a dressed visitor ends up stretched across the screen.
/// </summary>
internal static class AvatarMorphRanges
{
    // Dump: Gender 0..1 (0 ≈ male end, 1 ≈ female end — Donna/Lisa high, Trent/Marcus 0).
    internal static readonly Range Gender = new(0f, 1f);

    // Dump: Height 0.90..1.10 (p05..p95 ≈ 0.94..1.04). Scale factor on the rig.
    internal static readonly Range Height = new(0.90f, 1.10f);
    internal static readonly Range HeightSafe = new(0.94f, 1.04f);

    // Dump: Weight 0..1 (p05..p95 ≈ 0..0.90).
    internal static readonly Range Weight = new(0f, 1f);
    internal static readonly Range WeightSafe = new(0.15f, 0.90f);

    // Dump: EyebrowScale 0..1.57 (p05..p95 ≈ 0.84..1.36). Median 1.0.
    internal static readonly Range EyebrowScale = new(0f, 1.57f);
    internal static readonly Range EyebrowScaleSafe = new(0.84f, 1.36f);

    // Dump: EyebrowThickness 0.55..3.0 (p05..p95 ≈ 0.77..2.12). Median 1.12.
    // The old wardrobe drew 0.45..0.8 — almost entirely below the shipped median.
    internal static readonly Range EyebrowThickness = new(0.55f, 3f);
    internal static readonly Range EyebrowThicknessSafe = new(0.77f, 2.12f);

    // Dump: PupilDilation 0.20..0.79 (p05..p95 ≈ 0.46..0.75).
    internal static readonly Range PupilDilation = new(0.20f, 0.79f);
    internal static readonly Range PupilDilationSafe = new(0.46f, 0.75f);

    internal readonly struct Range
    {
        internal Range(float min, float max)
        {
            Min = min;
            Max = max;
        }

        internal float Min { get; }

        internal float Max { get; }

        internal bool Contains(float value) => value >= Min && value <= Max;

        internal float Clamp(float value) => Mathf.Clamp(value, Min, Max);
    }

    /// <summary>
    /// Returns <paramref name="proposed"/> when it sits inside the hard dump range; otherwise
    /// <paramref name="donor"/> (also clamped) and a one-line reason for the log.
    /// </summary>
    internal static float Guard(string field, float proposed, float donor, Range hard, out string? rejected)
    {
        if (hard.Contains(proposed))
        {
            rejected = null;
            return proposed;
        }

        rejected =
            $"{field}={proposed:0.###} is outside the shipped range [{hard.Min:0.###}..{hard.Max:0.###}]; " +
            $"keeping donor {donor:0.###}";
        return hard.Clamp(donor);
    }
}

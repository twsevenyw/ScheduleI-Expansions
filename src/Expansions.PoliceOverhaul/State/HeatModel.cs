namespace Expansions.PoliceOverhaul.State;

/// <summary>
/// The whole balance of the mod as pure arithmetic. No game types, no Unity types, no singletons —
/// which is what makes the numbers auditable without a running game, and what stops a balance change
/// turning into an interop bug.
/// </summary>
internal static class HeatModel
{
    internal const float MinHeat = 0f;
    internal const float MaxHeat = 100f;

    /// <summary>The game refuses to dispatch more than four officers and logs an error if you ask.</summary>
    internal const int HardOfficerCap = 4;

    /// <summary>The console's <c>setlawintensity</c> range, mirrored by S1API's Min/MaxIntensity.</summary>
    internal const int MinLawIntensity = 1;

    internal const int MaxLawIntensity = 10;

    /// <summary>
    /// Heat per unit of fine. The game already ranked its own crimes by what it charges for them, so
    /// the severity ladder is read from <c>PenaltyHandler</c> at runtime rather than invented here:
    /// a $5 possession is +1, a $150 attempt-to-sell is +30.
    /// </summary>
    internal const float HeatPerFineDollar = 0.2f;

    /// <summary>Calibrated to the assault tier — an arrest should cost more than one misdemeanour.</summary>
    internal const float ArrestHeat = 15f;

    /// <summary>$2,000 is the smallest shipped business laundering cap, so 1 heat is one small launder.</summary>
    internal const float DealPaymentPerHeat = 2000f;

    /// <summary>Stops a legitimate high-volume day pushing a careful player into the federal band.</summary>
    internal const float MaxDealHeatPerDay = 15f;

    internal const float CurfewMultiplier = 1.5f;

    internal static float Clamp(float heat) => heat < MinHeat ? MinHeat : heat > MaxHeat ? MaxHeat : heat;

    internal static HeatTier TierFor(float heat) => heat switch
    {
        >= 80f => HeatTier.Federal,
        >= 60f => HeatTier.TaskForce,
        >= 40f => HeatTier.Crackdown,
        >= 20f => HeatTier.Alert,
        _ => HeatTier.Calm,
    };

    internal static string TierName(HeatTier tier) => tier switch
    {
        HeatTier.Federal => "FEDERAL",
        HeatTier.TaskForce => "TASK FORCE",
        HeatTier.Crackdown => "CRACKDOWN",
        HeatTier.Alert => "ALERT",
        _ => "CALM",
    };

    /// <summary>
    /// Richer districts complain louder. Mirrors the shipped per-region customer budget tiers rather
    /// than inventing a second opinion about which end of town is which.
    /// </summary>
    internal static float RegionMultiplier(string? regionName) => regionName switch
    {
        "Uptown" or "Suburbia" => 1.25f,
        "Northtown" or "Docks" => 0.85f,
        _ => 1.0f,
    };

    /// <summary>
    /// What heat adds to the game's own law intensity. Never replaces the vanilla baseline, only adds
    /// to it, so at heat 0 the world is exactly vanilla — the property that makes the mod feel shipped.
    /// </summary>
    internal static int IntensityContribution(float heat, float masterScalar)
    {
        var raw = heat / 12.5f * masterScalar;
        return (int)Math.Round(Math.Clamp(raw, 0d, 8d), MidpointRounding.AwayFromZero);
    }

    internal static int TargetIntensity(int vanillaBaseline, float heat, float masterScalar) =>
        Math.Clamp(vanillaBaseline + IntensityContribution(heat, masterScalar), MinLawIntensity, MaxLawIntensity);

    /// <summary>
    /// Officers requested per patrol, sentry or checkpoint post.
    /// <para>
    /// These are a floor, never a ceiling. Callers take the larger of this and the designer's own
    /// number for that post, because a mod called "police improvements" must never quietly staff a
    /// checkpoint more thinly than the base game did — at zero heat the world has to be exactly
    /// vanilla, which is the property that makes the whole feature feel shipped rather than bolted on.
    /// </para>
    /// </summary>
    internal static (int Min, int Max) OfficerBand(HeatTier tier, float masterScalar, int configuredMax)
    {
        var (min, max) = tier switch
        {
            HeatTier.Federal => (3, 4),
            HeatTier.TaskForce => (2, 4),
            HeatTier.Crackdown => (2, 3),
            HeatTier.Alert => (1, 2),
            _ => (1, 1),
        };

        // A scalar below 1 walks the band back towards vanilla rather than below it: one officer per
        // post is the floor the game itself uses.
        if (masterScalar < 1f)
        {
            min = 1 + (int)Math.Round((min - 1) * masterScalar);
            max = 1 + (int)Math.Round((max - 1) * masterScalar);
        }

        var cap = Math.Clamp(configuredMax, 1, HardOfficerCap);
        min = Math.Clamp(min, 1, cap);
        max = Math.Clamp(max, min, cap);
        return (min, max);
    }

    /// <summary>Hours added to each end of a checkpoint's shipped window.</summary>
    internal static int CheckpointWindowWidening(HeatTier tier, float masterScalar)
    {
        var hours = tier switch
        {
            HeatTier.Federal => 12,
            HeatTier.TaskForce => 6,
            HeatTier.Crackdown => 2,
            _ => 0,
        };

        return (int)Math.Round(hours * Math.Max(0f, masterScalar));
    }

    /// <summary>Multiplier on a vision cone's attentiveness, memory and effective range.</summary>
    internal static float DetectionMultiplier(HeatTier tier, float masterScalar)
    {
        var baseline = tier switch
        {
            HeatTier.Federal => 1.75f,
            HeatTier.TaskForce => 1.5f,
            HeatTier.Crackdown => 1.25f,
            HeatTier.Alert => 1.1f,
            _ => 1.0f,
        };

        return Lerp(baseline, masterScalar);
    }

    internal static float MemoryMultiplier(HeatTier tier, float masterScalar) =>
        tier == HeatTier.Federal ? Lerp(2.0f, masterScalar) : DetectionMultiplier(tier, masterScalar);

    internal static float BodySearchMultiplier(HeatTier tier, float masterScalar)
    {
        var baseline = tier switch
        {
            HeatTier.Federal => 2.5f,
            HeatTier.TaskForce => 2.0f,
            HeatTier.Crackdown => 1.5f,
            HeatTier.Alert => 1.25f,
            _ => 1.0f,
        };

        return Lerp(baseline, masterScalar);
    }

    /// <summary>
    /// Fine multiplier from heat alone, before the outlaw multiplier. Kept gentle: the shipped fine
    /// table tops out at $150, and the real teeth are in the debt conversion, not the headline number.
    /// </summary>
    internal static float FineMultiplier(HeatTier tier, float masterScalar)
    {
        var baseline = tier switch
        {
            HeatTier.Federal => 2.0f,
            HeatTier.TaskForce => 1.75f,
            HeatTier.Crackdown => 1.5f,
            HeatTier.Alert => 1.25f,
            _ => 1.0f,
        };

        return Lerp(baseline, masterScalar);
    }

    /// <summary>
    /// Daily decay net of whatever already drained live, so a full clean day always totals
    /// <paramref name="perDay"/> however much of it the player saw tick past on the read-out.
    /// </summary>
    internal static float SleepDecay(float perDay, float perCleanMinute, int cleanMinutesToday)
    {
        var alreadyDrained = Math.Max(0f, perCleanMinute) * Math.Max(0, cleanMinutesToday);
        return Math.Max(0f, perDay - alreadyDrained);
    }

    /// <summary>A scalar of 0 flattens every effect to 1.0; 1 is the tuned value; 2 doubles the delta.</summary>
    private static float Lerp(float baselineAtScalarOne, float masterScalar) =>
        1f + (baselineAtScalarOne - 1f) * Math.Max(0f, masterScalar);
}

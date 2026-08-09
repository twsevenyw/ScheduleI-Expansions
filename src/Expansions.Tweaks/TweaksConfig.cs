using Expansions.Core.Configuration;
using Expansions.Tweaks.Runtime;

namespace Expansions.Tweaks;

/// <summary>
/// Every setting the module owns, in one place.
/// <para>
/// The three multipliers all read the same way — bigger is faster — so the owner never has to
/// remember which direction a given number goes. Each one is clamped on read rather than rewritten
/// in the file: an out-of-range value is the owner experimenting, not a mistake to correct behind
/// their back, and a clamp that also edits their file makes the next edit confusing.
/// </para>
/// </summary>
internal sealed class TweaksConfig
{
    /// <summary>Below this the game stops feeling like a game; above it, timers round to nothing.</summary>
    internal const float MinMultiplier = 0.25f;

    internal const float MaxMultiplier = 10f;

    internal const float MinDepositLimit = 100f;

    internal const float MaxDepositLimit = 10_000_000f;

    internal TweaksConfig(ModuleConfig config)
    {
        EnableFasterMixing = config.Bind("enable_faster_mixing", true, "Faster mixing",
            "Speed up the mixing station. Off leaves mixing exactly vanilla.");

        MixSpeedMultiplier = config.Bind("mix_speed_multiplier", 2f, "Mixing speed multiplier",
            "How much faster mixing runs. 2 = half the time (a one-hour mix takes 30 minutes), " +
            "1 = vanilla, 0.5 = twice as long. Clamped to 0.25-10. Applied to the station's " +
            "per-item mix time, so the station screen and the management app show the new figure too.");

        MixSpeedAppliesToEmployees = config.Bind("mix_speed_applies_to_employees", true,
            "Speed up chemist mixing too",
            "On, a chemist working the station gets the same speed-up you do. Off, only mixes you " +
            "start yourself are faster. Off is best-effort: the station is shared, so the exclusion " +
            "depends on the game having recorded the chemist as the station's user before the mix " +
            "begins, and the station screen still shows the sped-up figure.");

        EnableDepositLimit = config.Bind("enable_atm_deposit_limit", true, "Custom ATM deposit limit",
            "Replace the game's weekly ATM deposit ceiling with your own. Off leaves it vanilla.");

        WeeklyDepositLimit = config.Bind("weekly_deposit_limit", 25_000f, "Weekly ATM deposit limit",
            "How much cash you may bank per in-game week. Vanilla is $10,000. Lower than vanilla " +
            "works too, if you want it tighter. Note: Police Improvements deliberately takes " +
            "unpayable fines out of your online balance, and its difficulty tuning assumes the " +
            "vanilla $10,000 ceiling - raising this weakens that lever.");

        EnableFasterDeliveries = config.Bind("enable_faster_deliveries", true, "Faster deliveries",
            "Speed up the shop deliveries you order from the phone. Off leaves them exactly vanilla.");

        DeliverySpeedMultiplier = config.Bind("delivery_speed_multiplier", 2f, "Delivery speed multiplier",
            "How much faster a delivery arrives. 2 = arrives in half the time, 1 = vanilla, " +
            "0.5 = takes twice as long. Clamped to 0.25-10. The quoted arrival time on the order " +
            "screen already accounts for this, so the phone never disagrees with the truck.");

        DebugLogging = config.Bind("debug_logging", false, "Debug logging",
            "Write per-station and per-patch detail to the MelonLoader console.");
    }

    internal ConfigValue<bool> EnableFasterMixing { get; }

    internal ConfigValue<float> MixSpeedMultiplier { get; }

    internal ConfigValue<bool> MixSpeedAppliesToEmployees { get; }

    internal ConfigValue<bool> EnableDepositLimit { get; }

    internal ConfigValue<float> WeeklyDepositLimit { get; }

    internal ConfigValue<bool> EnableFasterDeliveries { get; }

    internal ConfigValue<float> DeliverySpeedMultiplier { get; }

    internal ConfigValue<bool> DebugLogging { get; }

    /// <summary>The mixing multiplier actually in force. 1 when the tweak is switched off.</summary>
    internal float ResolvedMixMultiplier =>
        EnableFasterMixing.Value ? Clamp(MixSpeedMultiplier, MinMultiplier, MaxMultiplier) : 1f;

    /// <summary>The delivery multiplier actually in force. 1 when the tweak is switched off.</summary>
    internal float ResolvedDeliveryMultiplier =>
        EnableFasterDeliveries.Value ? Clamp(DeliverySpeedMultiplier, MinMultiplier, MaxMultiplier) : 1f;

    /// <summary>The deposit ceiling actually in force, or null when the tweak is switched off.</summary>
    internal float? ResolvedDepositLimit =>
        EnableDepositLimit.Value ? Clamp(WeeklyDepositLimit, MinDepositLimit, MaxDepositLimit) : null;

    private static float Clamp(ConfigValue<float> value, float min, float max)
    {
        var raw = value.Value;

        if (float.IsNaN(raw))
        {
            WarnOnce(value.Id, raw, value.DefaultValue);
            return value.DefaultValue;
        }

        if (raw >= min && raw <= max)
            return raw;

        var clamped = Math.Clamp(raw, min, max);
        WarnOnce(value.Id, raw, clamped);
        return clamped;
    }

    private static readonly HashSet<string> Warned = new(StringComparer.Ordinal);

    private static void WarnOnce(string id, float raw, float used)
    {
        if (!Warned.Add(id))
            return;

        TweakLog.Warn($"'{id}' is set to {raw}, which is outside the supported range; using {used} instead.");
    }
}

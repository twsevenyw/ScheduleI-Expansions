using Expansions.Core.Configuration;

namespace Expansions.PoliceOverhaul;

/// <summary>
/// Every setting the module owns, bound once in <c>OnRegistered</c>.
/// <para>
/// Split into two kinds. The scalars are hot: <see cref="IntensityScalar"/> and
/// <see cref="HeatGainScalar"/> are read on every tick, so editing them in the menu or the .cfg
/// changes the game immediately. The pillar switches are structural — they are read at wiring time
/// and a mid-session change is logged rather than half-applied, because turning a pillar off has to
/// run its snapshot restore, not merely stop writing.
/// </para>
/// </summary>
internal sealed class PoliceConfig
{
    internal PoliceConfig(ModuleConfig config)
    {
        IntensityScalar = config.Bind(
            "intensity_scalar",
            1.0f,
            "Master intensity scalar",
            "Multiplies every heat-driven world effect: law intensity, officers per post, detection and search rates. 0 tracks heat without changing anything; 2 is brutal.");

        HeatGainScalar = config.Bind(
            "heat_gain_scalar",
            1.0f,
            "Heat gain scalar",
            "Multiplies every heat input. Heat per crime is derived from the game's own fine table at runtime, so this is the only place to make crimes cheaper or dearer.");

        HeatDecayPerDay = config.Bind(
            "heat_decay_per_day",
            10.0f,
            "Heat decay per slept day",
            "Heat shed across one full day of lying low. Some of it is delivered live (see the per-minute drain) so the read-out moves during play.");

        HeatDecayPerCleanMinute = config.Bind(
            "heat_decay_per_clean_minute",
            0.004f,
            "Heat decay per clean minute",
            "Live drain while you have no crimes on record and nobody is pursuing you. Subtracted from the daily amount, so a clean day still totals the daily figure.");

        HeatFromDeals = config.Bind(
            "heat_from_deals",
            true,
            "Deals raise heat",
            "Adds 1 heat per $2,000 of contract payment, capped per in-game day. Turn off if you want only crimes to matter.");

        EnableIntensity = config.Bind(
            "enable_dynamic_intensity",
            true,
            "Pillar: dynamic intensity",
            "Drive the game's own law scheduler from your heat, so patrols, sentries, checkpoints and curfews scale with how wanted you are.");

        EnableScheduleTuning = config.Bind(
            "enable_schedule_tuning",
            true,
            "Pillar: officers per post",
            "Also widen the officers-per-post bands and checkpoint hours on the game's schedule data. Snapshotted on enable and written back on disable.");

        EnableConsequences = config.Bind(
            "enable_consequences",
            true,
            "Pillar: heavier consequences",
            "Heat- and outlaw-scaled fines, wider confiscation, vehicle cargo seizure and unpaid fines becoming bank debt.");

        EnableOutlaw = config.Bind(
            "enable_outlaw",
            true,
            "Pillar: outlaw status",
            "A latched Clean / Marked / Hunted status that changes the rules rather than the world: always-suspicious, searches always find, pursuits do not evaporate.");

        EnableFederalAgents = config.Bind(
            "enable_federal_agents",
            true,
            "Pillar: federal agents",
            "Spawn plain-clothes agents at very high heat. Detected at runtime; if this build cannot spawn them the rest of the mod carries on unaffected.");

        MaxOfficersPerPost = config.Bind(
            "max_officers_per_post",
            4,
            "Max officers per post",
            "Upper bound for patrol, sentry and checkpoint member counts. The game refuses to dispatch more than 4 whatever this says.");

        FederalHeatThreshold = config.Bind(
            "federal_heat_threshold",
            80,
            "Federal heat threshold",
            "Heat at which a federal event becomes eligible. It also has to be held for a full in-game day.");

        FederalAgentsPerEvent = config.Bind(
            "federal_agents_per_event",
            2,
            "Federal agents per event",
            "Two is a team; four is the game's dispatch cap and would leave no room for local police.");

        FederalEventHours = config.Bind(
            "federal_event_hours",
            6,
            "Federal event length (in-game hours)",
            "How long agents stay out before withdrawing. One in-game hour is roughly one real minute.");

        FederalCooldownDays = config.Bind(
            "federal_cooldown_days",
            2,
            "Federal cooldown (in-game days)",
            "Minimum gap between federal events, so they stay an event rather than a permanent tax.");

        OutlawHeatThreshold = config.Bind(
            "outlaw_heat_threshold",
            90,
            "Outlaw heat threshold",
            "Heat at which the outlaw status latches on. Latched status does not fall off with heat; see the clear conditions.");

        OutlawClearDays = config.Bind(
            "outlaw_clear_days",
            3,
            "Outlaw clear days",
            "Consecutive in-game days with no crimes and no arrests needed to drop one outlaw tier.");

        OutlawLegalFee = config.Bind(
            "outlaw_legal_fee",
            25000,
            "Outlaw legal fee",
            "Cash cost of buying your way down one outlaw tier. Priced at the Barn, so it is a real mid-game decision.");

        OutlawFineMultiplier = config.Bind(
            "outlaw_fine_multiplier",
            2.0f,
            "Outlaw fine multiplier",
            "Multiplies the fine you are charged while outlawed, on top of the heat-tier multiplier.");

        EnableDebt = config.Bind(
            "enable_debt",
            true,
            "Unpaid fines become debt",
            "Any part of a fine you cannot cover in cash is taken out of your online balance instead. This is the single biggest difficulty change in the mod.");

        ConfiscateVehicleCargo = config.Bind(
            "confiscate_vehicle_cargo",
            true,
            "Confiscate vehicle cargo",
            "Contraband in the vehicle you were arrested next to is taken too. Vanilla leaves vehicle storage untouched.");

        ShowHud = config.Bind(
            "show_heat_hud",
            true,
            "Announce heat changes",
            "Notify on every heat-tier change and outlaw transition. Turn off for a silent run; the F7 menu still reports the numbers.");

        DebugLogging = config.Bind(
            "debug_logging",
            false,
            "Verbose logging",
            "Log every heat change, intensity write and lever decision. Useful once, noisy forever.");
    }

    internal ConfigValue<float> IntensityScalar { get; }

    internal ConfigValue<float> HeatGainScalar { get; }

    internal ConfigValue<float> HeatDecayPerDay { get; }

    internal ConfigValue<float> HeatDecayPerCleanMinute { get; }

    internal ConfigValue<bool> HeatFromDeals { get; }

    internal ConfigValue<bool> EnableIntensity { get; }

    internal ConfigValue<bool> EnableScheduleTuning { get; }

    internal ConfigValue<bool> EnableConsequences { get; }

    internal ConfigValue<bool> EnableOutlaw { get; }

    internal ConfigValue<bool> EnableFederalAgents { get; }

    internal ConfigValue<int> MaxOfficersPerPost { get; }

    internal ConfigValue<int> FederalHeatThreshold { get; }

    internal ConfigValue<int> FederalAgentsPerEvent { get; }

    internal ConfigValue<int> FederalEventHours { get; }

    internal ConfigValue<int> FederalCooldownDays { get; }

    internal ConfigValue<int> OutlawHeatThreshold { get; }

    internal ConfigValue<int> OutlawClearDays { get; }

    internal ConfigValue<int> OutlawLegalFee { get; }

    internal ConfigValue<float> OutlawFineMultiplier { get; }

    internal ConfigValue<bool> EnableDebt { get; }

    internal ConfigValue<bool> ConfiscateVehicleCargo { get; }

    internal ConfigValue<bool> ShowHud { get; }

    internal ConfigValue<bool> DebugLogging { get; }
}

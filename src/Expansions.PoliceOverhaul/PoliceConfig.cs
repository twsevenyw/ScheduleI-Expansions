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

        PoliceDensity = config.Bind(
            "police_density",
            3.0f,
            "Police density multiplier",
            "How many police this town has, independent of how badly they want you. 3 is roughly triple the vanilla street presence: it multiplies the officers each post asks for (still capped at the engine's 4) and divides each post's intensity requirement so far more posts run at once. 1 is vanilla. Snapshotted and written back on disable.");

        RespawnOfficersDaily = config.Bind(
            "respawn_officers_daily",
            true,
            "Return dead officers to duty each day",
            "The map ships a fixed set of officers and nothing creates more, so a violent week permanently empties the police stations. This calls the game's own NPCHealth.Revive() on dead officers at each day rollover instead of waiting out its multi-day timer.");

        EventOfficerCount = config.Bind(
            "event_officer_count",
            3,
            "Officers guaranteed per police event",
            "Raids and federal events make sure this many live officers are near the scene before they fire, dispatching from the nearest station if the map is empty. An event with no police behind it reads as a bug.");

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
            "Cost of buying your way down one outlaw tier, spent from the Police Improvements menu. Priced at the Barn, so it is a real mid-game decision. Cash first, then your bank balance.");

        OutlawDealerCutBonus = config.Bind(
            "outlaw_dealer_cut_bonus",
            0.05f,
            "Outlaw dealer cut surcharge",
            "Extra share every recruited dealer takes while you are outlawed, on top of their flat 20%. A hazard premium. Snapshotted and restored when the status clears.");

        OutlawSnitchBonus = config.Bind(
            "outlaw_snitch_bonus",
            0.10f,
            "Outlaw customer snitch bonus",
            "Added to every unlocked customer's chance of calling the police on you while you are outlawed. Shipped chances already span 0-67%, so this stays inside the existing spread.");

        OutlawBlocksCardVendors = config.Bind(
            "outlaw_blocks_card_vendors",
            true,
            "Card-only vendors refuse outlaws",
            "Shops that only take card (the realtor and the car dealership) will not serve you while you are outlawed. Cash trade is unaffected — you are an outlaw, not bankrupt.");

        EnablePropertyRaids = config.Bind(
            "enable_property_raids",
            true,
            "Property raids",
            "While outlawed, the police will come for a property you own. You are warned first and lose nothing if you are there when they arrive.");

        RaidDelayMinutes = config.Bind(
            "raid_delay_minutes",
            30,
            "Raid warning (in-game minutes)",
            "How long you get between the warning and the raid. Thirty is the game's own logistics quantum, and about half a real minute of running.");

        RaidConfiscationFraction = config.Bind(
            "raid_confiscation_fraction",
            0.5f,
            "Raid confiscation fraction",
            "Share of the contraband stacks in each container that a raid takes. Never all of it: a total wipe reads as a bug, and a partial loss is what makes you move the rest.");

        RaidCooldownDays = config.Bind(
            "raid_cooldown_days",
            3,
            "Raid cooldown (in-game days)",
            "Minimum gap between raids, so they stay an event rather than a rent.");

        EnableStakeouts = config.Bind(
            "enable_stakeouts",
            true,
            "Stakeouts",
            "Federal agents park outside the property you were last seen at instead of chasing you, so being at home stops being safe.");

        EnableJailDay = config.Bind(
            "enable_jail_day",
            true,
            "Lose the day when arrested as an outlaw",
            "An outlaw arrest holds you until morning and charges a processing fee. The game has no jail, so this reuses the shipped clock skip and your own payroll.");

        JailProcessingFeeScalar = config.Bind(
            "jail_processing_fee_scalar",
            1.0f,
            "Processing fee scalar",
            "Multiplies the processing fee, which is one day of your total employee wages (minimum $250). Set to 0 to lose the day for free.");

        EnableEquipmentLoss = config.Bind(
            "enable_equipment_loss",
            true,
            "Confiscate tools and equipment",
            "An outlaw arrest also takes the tools and equipment in your inventory, not just the product. Never touches seeds, soil, furniture or lighting.");

        EnableRelationshipDamage = config.Bind(
            "enable_relationship_damage",
            true,
            "Informants fall out with you",
            "Whoever called the police loses relationship with you when the arrest lands, and the game tells you who it was.");

        SnitchRelationshipDamage = config.Bind(
            "snitch_relationship_damage",
            0.25f,
            "Informant relationship damage",
            "How much relationship the caller loses. The shipped scale runs 0 to 5, so a quarter point is noticeable without being a wipe.");

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
            "Announce heat / outlaw / raid / federal events",
            "Sends phone texts from the Dispatch contact for substantive events. Turn off for a silent run; the F7 menu still reports the numbers. Imminent raid warnings still toast so you can react in time.");

        EnableEventScheduler = config.Bind(
            "enable_event_scheduler",
            true,
            "Randomised event cadence",
            "Drive federal visits and property raids on a per-save randomised schedule (heat and outlaw still gate whether a roll can fire). Turn off to fall back to the old 'fire as soon as thresholds are met' behaviour. Manual event triggers always work.");

        EventReadyGraceMinutes = config.Bind(
            "event_ready_grace_minutes",
            30,
            "Post-load event grace (in-game minutes)",
            "No scheduled federal or raid event fires in this window after a save loads, so nothing lands while the world is still settling.");

        FederalIntervalHoursMin = config.Bind(
            "federal_interval_hours_min",
            6,
            "Federal check interval min (hours)",
            "Shortest gap between scheduled federal eligibility rolls. The actual gap is rolled per save between min and max.");

        FederalIntervalHoursMax = config.Bind(
            "federal_interval_hours_max",
            18,
            "Federal check interval max (hours)",
            "Longest gap between scheduled federal eligibility rolls.");

        RaidIntervalHoursMin = config.Bind(
            "raid_interval_hours_min",
            8,
            "Raid check interval min (hours)",
            "Shortest gap between scheduled raid eligibility rolls while you are outlawed.");

        RaidIntervalHoursMax = config.Bind(
            "raid_interval_hours_max",
            24,
            "Raid check interval max (hours)",
            "Longest gap between scheduled raid eligibility rolls.");

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

    internal ConfigValue<float> PoliceDensity { get; }

    internal ConfigValue<bool> RespawnOfficersDaily { get; }

    internal ConfigValue<int> EventOfficerCount { get; }

    internal ConfigValue<int> FederalHeatThreshold { get; }

    internal ConfigValue<int> FederalAgentsPerEvent { get; }

    internal ConfigValue<int> FederalEventHours { get; }

    internal ConfigValue<int> FederalCooldownDays { get; }

    internal ConfigValue<int> OutlawHeatThreshold { get; }

    internal ConfigValue<int> OutlawClearDays { get; }

    internal ConfigValue<int> OutlawLegalFee { get; }

    internal ConfigValue<float> OutlawDealerCutBonus { get; }

    internal ConfigValue<float> OutlawSnitchBonus { get; }

    internal ConfigValue<bool> OutlawBlocksCardVendors { get; }

    internal ConfigValue<bool> EnablePropertyRaids { get; }

    internal ConfigValue<int> RaidDelayMinutes { get; }

    internal ConfigValue<float> RaidConfiscationFraction { get; }

    internal ConfigValue<int> RaidCooldownDays { get; }

    internal ConfigValue<bool> EnableStakeouts { get; }

    internal ConfigValue<bool> EnableJailDay { get; }

    internal ConfigValue<float> JailProcessingFeeScalar { get; }

    internal ConfigValue<bool> EnableEquipmentLoss { get; }

    internal ConfigValue<bool> EnableRelationshipDamage { get; }

    internal ConfigValue<float> SnitchRelationshipDamage { get; }

    internal ConfigValue<float> OutlawFineMultiplier { get; }

    internal ConfigValue<bool> EnableDebt { get; }

    internal ConfigValue<bool> ConfiscateVehicleCargo { get; }

    internal ConfigValue<bool> ShowHud { get; }

    internal ConfigValue<bool> EnableEventScheduler { get; }

    internal ConfigValue<int> EventReadyGraceMinutes { get; }

    internal ConfigValue<int> FederalIntervalHoursMin { get; }

    internal ConfigValue<int> FederalIntervalHoursMax { get; }

    internal ConfigValue<int> RaidIntervalHoursMin { get; }

    internal ConfigValue<int> RaidIntervalHoursMax { get; }

    internal ConfigValue<bool> DebugLogging { get; }
}

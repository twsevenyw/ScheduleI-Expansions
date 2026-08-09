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

        HeatResetOnArrest = config.Bind(
            "heat_reset_on_arrest",
            true,
            "Arrest clears heat to zero",
            "When you get arrested, heat drops to 0 — you paid for it. Marked/Hunted outlaw status is NOT cleared (that still needs clean days or the legal fee). Turn off to use the old +arrest_heat behaviour instead.");

        ArrestHeat = config.Bind(
            "arrest_heat",
            State.HeatModel.ArrestHeat,
            "Heat added on arrest (legacy)",
            "Only used when heat_reset_on_arrest is off. Positive values add heat on arrest (vanilla mod behaviour was +15). Ignored when arrests clear heat.");

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
            "At very high heat, designate a few of the game's own shipped officers as a federal team: relocate them, buff instance stats, restore when the event ends. Never clones or creates NPCs.");

        MaxOfficersPerPost = config.Bind(
            "max_officers_per_post",
            4,
            "Max officers per post",
            "Upper bound for patrol, sentry and checkpoint member counts. The game refuses to dispatch more than 4 whatever this says.");

        PoliceDensity = config.Bind(
            "police_density",
            3.0f,
            "Police density multiplier",
            "How many police this town fields from the shipped officer set, independent of how badly they want you. 3 multiplies officers each post asks for (still capped at the engine's 4) and divides each post's intensity requirement so far more posts run at once. 1 is vanilla. Snapshotted and written back on disable. Does not invent bodies — the map's closed officer set is the ceiling.");

        RespawnOfficersDaily = config.Bind(
            "respawn_officers_daily",
            true,
            "Also revive on day rollover",
            "Extra safety net on top of officer_revive_interval_minutes. Still uses the game's own NPCHealth.Revive().");

        OfficerReviveIntervalMinutes = config.Bind(
            "officer_revive_interval_minutes",
            45,
            "Officer revive interval (in-game minutes)",
            "How often dead officers are brought back via NPCHealth.Revive(). Default 45 is well under a day (~1440 minutes), so wiping the force buys minutes, not permanent safety. 0 disables the interval (day rollover may still revive).");

        OfficerThreatScalar = config.Bind(
            "officer_threat_scalar",
            1.0f,
            "Master officer difficulty scalar",
            "Multiplies every per-officer combat/vision/health lever below. 0 flattens those levers toward vanilla; 1 is the tuned curve; 2 is brutal. Heat and outlaw tier still scale on top.");

        OfficerHealthMultiplier = config.Bind(
            "officer_health_multiplier",
            1.5f,
            "Officer health multiplier",
            "Target Health (and MaxHealth when writable) on each living officer, scaled by heat/outlaw/threat scalar. Instance fields only.");

        OfficerDamageTakenMultiplier = config.Bind(
            "officer_damage_taken_multiplier",
            0.55f,
            "Officer damage-taken multiplier",
            "Harmony scales NPCHealth.TakeDamage on officers. Lower = tankier. Further reduced as heat/outlaw rise.");

        OfficerDamageDealtMultiplier = config.Bind(
            "officer_damage_dealt_multiplier",
            1.35f,
            "Officer damage-dealt multiplier",
            "Multiplies AvatarRangedWeapon.Damage (and melee Damage when present) on each officer's gun/baton prefabs.");

        OfficerAccuracyMultiplier = config.Bind(
            "officer_accuracy_multiplier",
            1.4f,
            "Officer accuracy multiplier",
            "Multiplies HitChance_MinRange / HitChance_MaxRange on each officer's ranged weapon.");

        OfficerFireRateMultiplier = config.Bind(
            "officer_fire_rate_multiplier",
            1.25f,
            "Officer fire-rate multiplier",
            "Multiplies MaxFireRate and divides ReloadTime on each officer's ranged weapon.");

        OfficerAimSpeedMultiplier = config.Bind(
            "officer_aim_speed_multiplier",
            1.3f,
            "Officer aim-speed multiplier",
            "Divides AimTime_Min / AimTime_Max so officers get shots off sooner.");

        OfficerVisionRangeMultiplier = config.Bind(
            "officer_vision_range_multiplier",
            1.2f,
            "Officer vision-range multiplier",
            "Multiplies VisionCone.RangeMultiplier per officer (instance field).");

        OfficerAttentivenessMultiplier = config.Bind(
            "officer_attentiveness_multiplier",
            1.25f,
            "Officer attentiveness multiplier",
            "Multiplies VisionCone.Attentiveness per officer.");

        OfficerMemoryMultiplier = config.Bind(
            "officer_memory_multiplier",
            1.35f,
            "Officer memory multiplier",
            "Multiplies VisionCone.Memory per officer so they stay suspicious longer.");

        OfficerMovementSpeedMultiplier = config.Bind(
            "officer_movement_speed_multiplier",
            1.2f,
            "Officer chase-speed multiplier",
            "Multiplies CombatBehaviour/PursuitBehaviour.DefaultMovementSpeed.");

        OfficerGiveUpRangeMultiplier = config.Bind(
            "officer_give_up_range_multiplier",
            1.4f,
            "Officer give-up range multiplier",
            "Multiplies CombatBehaviour.GiveUpRange so pursuits stick farther.");

        OfficerSearchTimeMultiplier = config.Bind(
            "officer_search_time_multiplier",
            1.4f,
            "Officer search-time multiplier",
            "Multiplies CombatBehaviour.DefaultSearchTime after losing sight.");

        OfficerBackupOnSight = config.Bind(
            "officer_backup_on_sight",
            true,
            "Cops call backup when they see you",
            "When an officer fully notices or is attacked by the player, pull colleagues via BeginFootPursuit_Networked(includeColleagues) and a sighted dispatch.");

        EnableForceWipeResponse = config.Bind(
            "enable_force_wipe_response",
            true,
            "Escalate when the police force is wiped",
            "If living officers drop below force_wipe_alive_threshold, spike heat and trigger a federal response. Killing every cop in town is the loudest possible crime.");

        ForceWipeAliveThreshold = config.Bind(
            "force_wipe_alive_threshold",
            3,
            "Force-wipe alive threshold",
            "When living non-federal officers fall below this count, the wipe response fires (once per cooldown).");

        ForceWipeHeat = config.Bind(
            "force_wipe_heat",
            40f,
            "Heat gained on force wipe",
            "Added when the living force drops below the wipe threshold.");

        OfficerKillsPerDaySevere = config.Bind(
            "officer_kills_per_day_severe",
            5,
            "Officer kills/day before severe response",
            "At this many officer kills in one in-game day, heat spikes harder and a federal team is forced even if the force is not fully wiped.");

        OfficerKillsSevereHeat = config.Bind(
            "officer_kills_severe_heat",
            55f,
            "Heat for severe kill-cap response",
            "Heat added when officer_kills_per_day_severe is reached.");

        EventOfficerCount = config.Bind(
            "event_officer_count",
            3,
            "Officers guaranteed per police event",
            "Raids and federal events make sure this many live officers are near the scene before they fire, dispatching from the nearest station if the map is empty. An event with no police behind it reads as a bug.");

        EnableOfficerKillResponse = config.Bind(
            "enable_officer_kill_response",
            true,
            "Escalate hard when a cop dies",
            "Killing a PoliceOfficer spikes heat, raises wanted, and dispatches a sighted response to the kill site. Federal agents do not trigger this.");

        OfficerKillHeat = config.Bind(
            "officer_kill_heat",
            25f,
            "Heat gained for killing an officer",
            "Added on top of whatever DeadlyAssault already contributed through the normal crime path.");

        OfficerKillDispatchCount = config.Bind(
            "officer_kill_dispatch_count",
            4,
            "Officers dispatched after a cop kill",
            "Clamped to the engine's hard dispatch cap of 4. They arrive beginAsSighted.");

        OfficerKillWantedLevel = config.Bind(
            "officer_kill_wanted_level",
            3,
            "Wanted level after a cop kill",
            "S1API PursuitLevel ordinal: 1 Investigating, 2 Arresting, 3 NonLethal, 4 Lethal. Default 3.");

        FederalHeatThreshold = config.Bind(
            "federal_heat_threshold",
            80,
            "Federal heat threshold",
            "Heat at which a federal event becomes eligible. It also has to be held for a full in-game day.");

        FederalAgentsPerEvent = config.Bind(
            "federal_agents_per_event",
            4,
            "Federal agents per event",
            "Team size. Four matches the game's dispatch cap; they are never added to OfficerPool so they do not starve local PD.");

        FederalEventHours = config.Bind(
            "federal_event_hours",
            8,
            "Federal event length (in-game hours)",
            "How long agents stay out before withdrawing. One in-game hour is roughly one real minute.");

        FederalCooldownDays = config.Bind(
            "federal_cooldown_days",
            2,
            "Federal cooldown (in-game days)",
            "Minimum gap between federal events, so they stay an event rather than a permanent tax.");

        FederalHealthMultiplier = config.Bind(
            "federal_health_multiplier",
            2.0f,
            "Federal health multiplier",
            "Multiplies each agent's Health (and MaxHealth when writable). Instance fields only — never a const.");

        FederalVisionMultiplier = config.Bind(
            "federal_vision_multiplier",
            1.75f,
            "Federal vision multiplier",
            "Multiplies VisionCone RangeMultiplier / Attentiveness / Memory on each agent.");

        FederalSearchChance = config.Bind(
            "federal_search_chance",
            1.0f,
            "Federal body-search chance",
            "Written to each agent's BodySearchChance instance field (0-1).");

        FederalMovementSpeedMultiplier = config.Bind(
            "federal_movement_speed_multiplier",
            1.25f,
            "Federal movement speed multiplier",
            "Multiplies CombatBehaviour.DefaultMovementSpeed on each agent.");

        FederalGiveUpRangeMultiplier = config.Bind(
            "federal_give_up_range_multiplier",
            1.5f,
            "Federal give-up range multiplier",
            "Multiplies CombatBehaviour.GiveUpRange so agents stay on you farther before quitting.");

        FederalSearchTimeMultiplier = config.Bind(
            "federal_search_time_multiplier",
            1.5f,
            "Federal search-time multiplier",
            "Multiplies CombatBehaviour.DefaultSearchTime so agents hunt longer after losing sight.");

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

        LegalFeeMarked = config.Bind(
            "legal_fee_marked",
            25000,
            "Legal fee — Marked",
            "Cash (then bank) to drop from Marked to Clean at the police station door. The F7 menu only offers this as a repair path if the door hook failed.");

        LegalFeeHunted = config.Bind(
            "legal_fee_hunted",
            50000,
            "Legal fee — Hunted",
            "Cash (then bank) to drop from Hunted to Marked at the police station door. Max-wanted price. The F7 menu only offers this as a repair path if the door hook failed.");

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
            "Police will come for a property you own when heat is high enough (see raid_heat_threshold), or whenever you are outlawed. You are warned first and lose nothing if you are there when they arrive.");

        RaidHeatThreshold = config.Bind(
            "raid_heat_threshold",
            55,
            "Raid heat threshold",
            "Minimum heat for a raid to roll without outlaw status. Outlawed players always qualify regardless of this number.");

        RaidOutlawIntervalScale = config.Bind(
            "raid_outlaw_interval_scale",
            0.5f,
            "Outlaw raid interval scale",
            "Multiplies the raid check interval while you are outlawed. 0.5 means raids are considered twice as often.");

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
            1,
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
            "Master switch for player-facing announcements. When on, every announcement is an on-screen toast labelled Dispatch Office. There is no custom Dispatch NPC (that path crashed the game). Turn off for a silent run; the F7 menu still reports the numbers.");

        AnnounceMode = config.Bind(
            "announce_mode",
            "toast_and_text",
            "Announcement delivery",
            "Kept for config compatibility. All announcements are toast-only — phone text via a custom Dispatch NPC was retired because incomplete NPCs killed the process.");

        ResponseDelaySeconds = config.Bind(
            "response_delay_seconds",
            1.5f,
            "Police response delay (seconds)",
            "Real-time delay between a crime or police call and this module dispatching officers to the scene. Lower is faster. The engine's own hard dispatch cap of 4 officers is still respected.");

        PursuitAggressiveness = config.Bind(
            "pursuit_aggressiveness",
            1.0f,
            "Pursuit aggressiveness",
            "How hard responding officers come in. 0 is closest to vanilla (foot units, not pre-sighted). 1 dispatches already-sighted and prefers cruisers sooner. 2 is the ceiling: denser immediate response and pursuits that hang on longer while outlawed.");

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
            4,
            "Raid check interval min (hours)",
            "Shortest gap between scheduled raid eligibility rolls (further scaled down while outlawed).");

        RaidIntervalHoursMax = config.Bind(
            "raid_interval_hours_max",
            12,
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

    internal ConfigValue<bool> HeatResetOnArrest { get; }

    internal ConfigValue<float> ArrestHeat { get; }

    internal ConfigValue<bool> EnableIntensity { get; }

    internal ConfigValue<bool> EnableScheduleTuning { get; }

    internal ConfigValue<bool> EnableConsequences { get; }

    internal ConfigValue<bool> EnableOutlaw { get; }

    internal ConfigValue<bool> EnableFederalAgents { get; }

    internal ConfigValue<int> MaxOfficersPerPost { get; }

    internal ConfigValue<float> PoliceDensity { get; }

    internal ConfigValue<bool> RespawnOfficersDaily { get; }

    internal ConfigValue<int> OfficerReviveIntervalMinutes { get; }

    internal ConfigValue<float> OfficerThreatScalar { get; }

    internal ConfigValue<float> OfficerHealthMultiplier { get; }

    internal ConfigValue<float> OfficerDamageTakenMultiplier { get; }

    internal ConfigValue<float> OfficerDamageDealtMultiplier { get; }

    internal ConfigValue<float> OfficerAccuracyMultiplier { get; }

    internal ConfigValue<float> OfficerFireRateMultiplier { get; }

    internal ConfigValue<float> OfficerAimSpeedMultiplier { get; }

    internal ConfigValue<float> OfficerVisionRangeMultiplier { get; }

    internal ConfigValue<float> OfficerAttentivenessMultiplier { get; }

    internal ConfigValue<float> OfficerMemoryMultiplier { get; }

    internal ConfigValue<float> OfficerMovementSpeedMultiplier { get; }

    internal ConfigValue<float> OfficerGiveUpRangeMultiplier { get; }

    internal ConfigValue<float> OfficerSearchTimeMultiplier { get; }

    internal ConfigValue<bool> OfficerBackupOnSight { get; }

    internal ConfigValue<bool> EnableForceWipeResponse { get; }

    internal ConfigValue<int> ForceWipeAliveThreshold { get; }

    internal ConfigValue<float> ForceWipeHeat { get; }

    internal ConfigValue<int> OfficerKillsPerDaySevere { get; }

    internal ConfigValue<float> OfficerKillsSevereHeat { get; }

    internal ConfigValue<int> EventOfficerCount { get; }

    internal ConfigValue<bool> EnableOfficerKillResponse { get; }

    internal ConfigValue<float> OfficerKillHeat { get; }

    internal ConfigValue<int> OfficerKillDispatchCount { get; }

    internal ConfigValue<int> OfficerKillWantedLevel { get; }

    internal ConfigValue<int> FederalHeatThreshold { get; }

    internal ConfigValue<int> FederalAgentsPerEvent { get; }

    internal ConfigValue<int> FederalEventHours { get; }

    internal ConfigValue<int> FederalCooldownDays { get; }

    internal ConfigValue<float> FederalHealthMultiplier { get; }

    internal ConfigValue<float> FederalVisionMultiplier { get; }

    internal ConfigValue<float> FederalSearchChance { get; }

    internal ConfigValue<float> FederalMovementSpeedMultiplier { get; }

    internal ConfigValue<float> FederalGiveUpRangeMultiplier { get; }

    internal ConfigValue<float> FederalSearchTimeMultiplier { get; }

    internal ConfigValue<int> OutlawHeatThreshold { get; }

    internal ConfigValue<int> OutlawClearDays { get; }

    internal ConfigValue<int> LegalFeeMarked { get; }

    internal ConfigValue<int> LegalFeeHunted { get; }

    /// <summary>Tiered lawyer price. Clean → 0; Marked → <see cref="LegalFeeMarked"/>; Hunted → <see cref="LegalFeeHunted"/>.</summary>
    internal int FeeFor(State.OutlawTier tier) => tier switch
    {
        State.OutlawTier.Hunted => Math.Max(0, LegalFeeHunted.Value),
        State.OutlawTier.Marked => Math.Max(0, LegalFeeMarked.Value),
        _ => 0,
    };

    internal ConfigValue<float> OutlawDealerCutBonus { get; }

    internal ConfigValue<float> OutlawSnitchBonus { get; }

    internal ConfigValue<bool> OutlawBlocksCardVendors { get; }

    internal ConfigValue<bool> EnablePropertyRaids { get; }

    internal ConfigValue<int> RaidHeatThreshold { get; }

    internal ConfigValue<float> RaidOutlawIntervalScale { get; }

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

    internal ConfigValue<string> AnnounceMode { get; }

    internal ConfigValue<float> ResponseDelaySeconds { get; }

    internal ConfigValue<float> PursuitAggressiveness { get; }

    internal ConfigValue<bool> EnableEventScheduler { get; }

    internal ConfigValue<int> EventReadyGraceMinutes { get; }

    internal ConfigValue<int> FederalIntervalHoursMin { get; }

    internal ConfigValue<int> FederalIntervalHoursMax { get; }

    internal ConfigValue<int> RaidIntervalHoursMin { get; }

    internal ConfigValue<int> RaidIntervalHoursMax { get; }

    internal ConfigValue<bool> DebugLogging { get; }
}

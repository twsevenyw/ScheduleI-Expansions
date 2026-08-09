using Expansions.Core.Actions;
using Expansions.Core.Diagnostics;
using Expansions.Core.Events;
using Expansions.PoliceOverhaul.Patches;
using Expansions.PoliceOverhaul.Runtime;
using Expansions.PoliceOverhaul.State;

namespace Expansions.PoliceOverhaul.Diagnostics;

/// <summary>
/// Ten questions the owner cannot answer by looking at the game: heat, levers, patches, officers,
/// federal agents, world state, consequences, controls, Dispatch message delivery, and the event
/// scheduler's next fire. All read-only.
/// </summary>
internal static class PoliceOverhaulProbes
{
    /// <summary>Sorts after Core's seven areas and the Special Customers block.</summary>
    private const string Area = "9. Police Improvements";

    internal static IEnumerable<IProbe> All()
    {
        yield return new DelegateProbe(
            "police.heat_state",
            "What is each player's heat, tier and outlaw status?",
            Area,
            HeatState);

        yield return new DelegateProbe(
            "police.levers",
            "Which police tuning levers are writable on this build, and which fallbacks are carrying the load?",
            Area,
            Levers,
            requiresLoadedSave: false);

        yield return new DelegateProbe(
            "police.patches",
            "Did every Harmony target resolve?",
            Area,
            Patches,
            requiresLoadedSave: false);

        yield return new DelegateProbe(
            "police.officers",
            "How many officers are actually on the map, and is anything stopping more from appearing?",
            Area,
            Officers);

        yield return new DelegateProbe(
            "police.federal_agents",
            "Can this build spawn a federal agent, and how?",
            Area,
            Federal);

        yield return new DelegateProbe(
            "police.world_state",
            "What is the mod currently doing to the world?",
            Area,
            WorldState);

        yield return new DelegateProbe(
            "police.consequences",
            "Are raids, custody, equipment loss and the outlaw economy wired and reachable?",
            Area,
            Consequences);

        yield return new DelegateProbe(
            "police.controls",
            "Did the menu actions and triggerable events register?",
            Area,
            Controls,
            requiresLoadedSave: false);

        yield return new DelegateProbe(
            "police.messages",
            "Is the Dispatch contact ready, and what was the last delivery?",
            Area,
            Messages);

        yield return new DelegateProbe(
            "police.scheduler",
            "When does the event scheduler next allow a federal or raid roll?",
            Area,
            Scheduler);
    }

    private static void HeatState(ProbeContext context, ProbeResult result)
    {
        if (PoliceRuntime.Heat is not { } heat)
        {
            result.Inconclusive("The module is not wired this session, so there is no heat to report.");
            return;
        }

        result.Fact("Fine table", PenaltyTable.IsLoaded
            ? $"{PenaltyTable.ReadFromGame}/{PenaltyTable.All.Count} values read from the running game"
            : "not loaded yet");

        result.Fact("Persistence", PoliceSaveState.Live is null
            ? "**no save state** — heat is session-only until a save loads"
            : "live; written to Modded/Saveables/expansions_police.json");

        var rows = new List<IReadOnlyList<string>>();
        foreach (var record in heat.Records)
        {
            rows.Add(new[]
            {
                record.PlayerKey,
                record.PlayerName.Length > 0 ? record.PlayerName : "-",
                $"{record.Heat:0.#}",
                HeatModel.TierName(record.Tier),
                OutlawState.Describe(record.Outlaw),
                record.CleanDayStreak.ToString(),
                $"${record.PoliceTakeTotal:0}",
                record.MinutesAtFederalHeat.ToString(),
            });
        }

        if (rows.Count == 0)
        {
            result.Inconclusive("No player records yet. Heat records are created lazily on the first crime or the first tick with a player in the world.");
            return;
        }

        result.Table(
            new[] { "Key", "Name", "Heat", "Tier", "Outlaw", "Clean days", "Police take", "Mins at fed heat" },
            rows);

        result.Ok(
            $"{rows.Count} player record(s). Heat persists across sleep and reload, which the vanilla wanted level does not — " +
            "if these numbers survive a save/quit/load cycle, the persistence half of the mod is proven.");
    }

    private static void Levers(ProbeContext context, ProbeResult result)
    {
        var levers = PoliceRuntime.Levers;
        if (levers is null)
        {
            result.Inconclusive("The module is not wired this session, so nothing has been surveyed.");
            return;
        }

        var rows = levers.Status.Values
            .Select(status => (IReadOnlyList<string>)new[]
            {
                status.Key,
                status.Purpose,
                status.IsWritable ? "writable" : "**no**",
                status.Verdict,
            })
            .ToList();

        result.Table(new[] { "Static", "What it controls", "Writable", "Verdict" }, rows);

        result.Heading("Fallbacks doing the actual work");
        result.Bullet("Officers per post: the game's own `MinMembers`/`MaxMembers` schedule data, plus a clamp on `PoliceStation.Dispatch`.");
        result.Bullet("Detection range and search rate: per-officer `VisionCone.RangeMultiplier` and `BodySearchChance`, which are instance fields and cannot be const-inlined.");
        result.Bullet("Everything else: Harmony postfixes on the decision methods themselves.");

        var writable = levers.Status.Values.Count(s => s.IsWritable);
        result.Ok(
            $"{writable}/{levers.Status.Count} global statics writable. The mod is built so this number can be zero and every " +
            "pillar still works — the global statics are an optimisation, not a dependency.");
    }

    private static void Patches(ProbeContext context, ProbeResult result)
    {
        if (PolicePatches.AppliedTargets.Count == 0 && PolicePatches.MissingTargets.Count == 0)
        {
            result.Inconclusive("Patches have not been applied this session. They land on the first gameplay scene, not at mod load.");
            return;
        }

        result.Fact("Applied", PolicePatches.AppliedTargets.Count.ToString());
        result.Code(PolicePatches.AppliedTargets);

        if (PolicePatches.MissingTargets.Count == 0)
        {
            result.Ok("Every law-enforcement target resolved. No S1API patch shares any of these methods.");
            return;
        }

        result.Heading("Not present on this build");
        result.Code(PolicePatches.MissingTargets);
        result.Fail(
            $"{PolicePatches.MissingTargets.Count} target(s) did not resolve, so the features that depend on them are inert. " +
            "Everything else still runs — check whether the game updated and renamed them.");
    }

    /// <summary>
    /// The census, the density dial and every path that can put an officer on the street.
    /// <para>
    /// This probe exists because "there are no cops" was reported from a playtest and could not be
    /// answered from the outside: an empty map looks identical whether the schedule writes never
    /// landed, the station pool ran dry, or every officer in town is lying dead in a field. Those
    /// have completely different fixes, so all three are counted separately here.
    /// </para>
    /// </summary>
    private static void Officers(ProbeContext context, ProbeResult result)
    {
        var census = PoliceForce.Count();

        result.Table(
            new[] { "Count", "Officers", "What it means" },
            new List<IReadOnlyList<string>>
            {
                new[] { "On duty", census.OnDuty.ToString(), "alive, awake and active — officers you can actually meet" },
                new[] { "Dead", census.Dead.ToString(), "will not come back on their own for several in-game days" },
                new[] { "Out cold", census.KnockedOut.ToString(), "knocked out; restored with the dead ones" },
                new[] { "Inactive", census.Inactive.ToString(), "present but switched off: pooled in a station or culled out of sight" },
                new[] { "Pooled", census.Pooled.ToString(), "the reserve `PoliceStation.Dispatch` can draw from" },
                new[] { "Stations", census.Stations.ToString(), "police stations found in the scene" },
                new[] { "Federal agents", census.Agents.ToString(), "ours, temporary, never in the station pool" },
                new[] { "Total objects", census.Total.ToString(), "every officer the scene shipped — nothing creates more" },
            });

        var config = PoliceRuntime.Config;
        var schedule = PoliceRuntime.Schedule;

        result.Heading("Density");
        result.Fact("Requested multiplier", config is null ? "-" : $"x{config.PoliceDensity.Value:0.##}");
        result.Fact("Officers per post written", schedule is { AppliedBand: (> 0, > 0) } ? $"{schedule.AppliedBand.Min}-{schedule.AppliedBand.Max} (vanilla is 1-2)" : "**nothing written yet**");
        result.Fact("Posts opened early by density", schedule?.PostsOpened.ToString() ?? "0");
        result.Fact("Posts snapshotted", schedule?.TrackedPosts.ToString() ?? "0");

        if (schedule is not null && PoliceRuntime.Heat is { } heat)
        {
            var (vanilla, tuned) = schedule.DemandAt(heat.CurrentIntensity);
            result.Fact(
                "Officers the schedule is asking for",
                vanilla > 0
                    ? $"{tuned} vs {vanilla} vanilla at intensity {heat.CurrentIntensity} — **x{(float)tuned / vanilla:0.00} in effect**"
                    : $"{tuned} (no vanilla posts qualify at intensity {heat.CurrentIntensity})");
        }

        result.Heading("Spawn paths");
        result.Fact("Daily restore", config is null ? "-" : config.RespawnOfficersDaily.Value ? "on" : "**off**");
        result.Fact("Restored so far", $"{PoliceForce.TotalRevived} officer(s), {PoliceForce.LastRevived} at the last day rollover");
        result.Fact("Dispatched by this module", $"{PoliceForce.TotalDispatched} officer(s)");
        result.Fact("Schedule pillar", config is null ? "-" : config.EnableScheduleTuning.Value ? "on" : "**off**");
        result.Fact("Intensity pillar", config is null ? "-" : config.EnableIntensity.Value ? "on" : "**off**");
        result.Fact("Dispatch clamp bound", PolicePatches.AppliedTargets.Contains("PoliceStation.Dispatch") ? "yes" : "**no**");

        if (PoliceForce.LastShortfall.Length > 0)
            result.Fact("Last event shortfall", PoliceForce.LastShortfall);

        if (census.Total == 0)
        {
            result.Fail(
                "There are no `PoliceOfficer` objects in the scene at all. Nothing this module does can help — the " +
                "static officer list is empty, which means either no save is loaded or the police prefabs never woke up.");
            return;
        }

        if (census.OnDuty == 0)
        {
            result.Fail(
                $"Every one of the {census.Total} officer(s) in town is dead, out cold or switched off. This is the " +
                "state the map ends up in after a violent week, because the game ships a fixed set of officers and " +
                "never makes more. Turn on the daily restore, or trigger any police event to force one now.");
            return;
        }

        if (census.Dead > census.OnDuty)
        {
            result.Fail(
                $"{census.Dead} dead against {census.OnDuty} on duty. The force is losing officers faster than they " +
                "come back, so patrols and checkpoints will thin out over the next few days even though the schedule " +
                "is asking for them.");
            return;
        }

        result.Ok(
            $"{census.OnDuty} officer(s) on duty with {census.Pooled} in reserve. Density and staffing are " +
            "snapshot-and-restore: disabling the module writes every `MinMembers`, `MaxMembers` and " +
            "`IntensityRequirement` back to the shipped value. Reviving officers uses the game's own " +
            "`NPCHealth.Revive()`, so nothing to undo there either.");
    }

    private static void Federal(ProbeContext context, ProbeResult result)
    {
        var status = FederalAgents.Survey();

        result.Fact("Can spawn", status.CanSpawn ? "yes" : "**no**");
        result.Fact("Strategy", status.Strategy.ToString());
        result.Fact("Reason", status.Reason);
        result.Fact("Live agents", FederalAgents.LiveCount.ToString());

        if (PoliceRuntime.Federal is { } events)
        {
            result.Fact("Event active", events.IsActive ? $"yes, {events.HoursRemaining} in-game hour(s) left" : "no");
            result.Fact("Mode", events.IsActive ? events.IsStakeout ? $"stakeout on {events.StakeoutProperty}" : "foot pursuit" : "-");
            result.Fact("Target", events.TargetKey.Length > 0 ? events.TargetKey : "-");
        }

        if (!status.CanSpawn)
        {
            result.Fail(
                "Federal agents cannot be spawned on this build. Every other pillar is unaffected — heat, intensity, " +
                "consequences and outlaw status do not depend on this. Run this probe again from inside a loaded save " +
                "before concluding anything: the clone path needs at least one live officer to exist.");
            return;
        }

        result.Ok(
            $"Agents are viable via the {status.Strategy} path, for both pursuits and stakeouts. They are tagged by " +
            "native pointer, never added to `PoliceStation.OfficerPool`, and `ShouldSave` is forced false for them, so " +
            "they cannot leak into the save. Spawn one from the F7 menu and then save, reload and check the NPC set is " +
            "unchanged.");
    }

    private static void WorldState(ProbeContext context, ProbeResult result)
    {
        if (PoliceRuntime.Heat is not { } heat || PoliceRuntime.Config is not { } config)
        {
            result.Inconclusive("The module is not wired this session.");
            return;
        }

        result.Fact("Authority", HostGate.Evaluate(out var reason) ? $"yes ({reason})" : $"no ({reason})");
        result.Fact("Master scalar", config.IntensityScalar.Value.ToString("0.00"));
        result.Fact("Vanilla intensity baseline", heat.BaselineIntensity.ToString());
        result.Fact("Driven intensity", heat.CurrentIntensity.ToString());
        result.Fact("Peak heat", $"{heat.PeakHeat:0.#} ({HeatModel.TierName(heat.PeakTier)})");

        var (min, max) = HeatModel.OfficerBand(heat.PeakTier, config.IntensityScalar.Value, config.MaxOfficersPerPost.Value, config.PoliceDensity.Value);
        result.Fact("Officers per post", $"{min}-{max} (vanilla is 1-2, density x{config.PoliceDensity.Value:0.##})");
        result.Fact("Schedule posts snapshotted", PoliceRuntime.Schedule?.TrackedPosts.ToString() ?? "0");
        result.Fact("Officers snapshotted", PoliceRuntime.Detection?.TrackedOfficers.ToString() ?? "0");
        result.Fact("Anyone outlawed", PoliceRuntime.Outlaw is { AnyoneOutlawed: true } ? "yes" : "no");

        if (PoliceRuntime.Consequences is { } consequences)
        {
            result.Fact("Base game charges its own fine", consequences.GameChargesItsOwnFine switch
            {
                true => "yes — this module only adds the surcharge",
                false => "no — this module charges the whole penalty",
                _ => "unknown until the first arrest",
            });
        }

        result.Ok(
            "Everything above is snapshot-and-restore. Disabling the module writes back the vanilla intensity, every " +
            "schedule number and every per-officer value, then calls `Evaluate()` so the game re-derives the world. " +
            "`internalLawIntensity` is never written, so Law.json stays byte-identical.");
    }

    /// <summary>
    /// The five consequences that reach outside the arrest screen. Each one is reported with the
    /// thing it needs in order to fire, because the usual reason one of these looks broken is that
    /// its precondition was never met rather than that the code did not run.
    /// </summary>
    private static void Consequences(ProbeContext context, ProbeResult result)
    {
        if (PoliceRuntime.Config is not { } config || PoliceRuntime.Consequences is not { } consequences)
        {
            result.Inconclusive("The module is not wired this session, so none of these are live.");
            return;
        }

        var owned = Estate.Owned();
        var storages = 0;
        var contraband = 0;

        foreach (var property in owned)
        {
            foreach (var storage in Estate.StoragesIn(property))
            {
                storages++;
                contraband += Estate.ContrabandIn(storage).Count;
            }
        }

        result.Table(
            new[] { "Consequence", "Enabled", "State" },
            new List<IReadOnlyList<string>>
            {
                new[]
                {
                    "Property raids",
                    Yes(config.EnablePropertyRaids.Value),
                    PoliceRuntime.Raids is { } raids
                        ? raids.IsPending
                            ? $"inbound on {raids.PendingProperty} in {raids.MinutesUntilRaid} min"
                            : $"idle; {raids.RaidsExecuted} run this session, ${raids.ValueSeized:0} seized"
                        : "**not constructed**",
                },
                new[] { "Stakeouts", Yes(config.EnableStakeouts.Value), PoliceRuntime.Federal is { IsStakeout: true } stake ? $"holding {stake.StakeoutProperty}" : "no stakeout running" },
                new[] { "Jail day + processing fee", Yes(config.EnableJailDay.Value), $"{consequences.Custody.DaysServed} served, ${consequences.Custody.FeesCharged:0} charged; next fee ${consequences.Custody.ProcessingFeeDue():N0}" },
                new[] { "Equipment loss", Yes(config.EnableEquipmentLoss.Value), $"{consequences.EquipmentSeized} stack(s) taken this session" },
                new[] { "Informant fallout", Yes(config.EnableRelationshipDamage.Value), consequences.Informants.LastCaller.Length > 0 ? $"last caller {consequences.Informants.LastCaller}; {consequences.Informants.RelationshipLost:0.00} lost" : "nobody has called the police yet" },
                new[] { "Dealer cut surcharge", Yes(config.OutlawDealerCutBonus.Value > 0f), $"{Economy()?.DealersAffected ?? 0} dealer(s) raised" },
                new[] { "Customer snitch bonus", Yes(config.OutlawSnitchBonus.Value > 0f), $"{Economy()?.CustomersAffected ?? 0} customer(s) raised" },
                new[] { "Card-only vendors closed", Yes(config.OutlawBlocksCardVendors.Value), Economy() is { IsApplied: true } ? "applied" : "not applied (nobody is outlawed)" },
            });

        result.Fact("Owned properties", owned.Count.ToString());
        result.Fact("Containers found on them", storages.ToString());
        result.Fact("Contraband stacks in those containers", contraband.ToString());
        result.Fact("Daily payroll (the processing fee base)", $"${consequences.Custody.DailyPayroll():N0}");

        if (owned.Count == 0)
        {
            result.Inconclusive(
                "You do not own a property yet, so raids and stakeouts have nothing to target. Buy one and run this " +
                "probe again — everything else above is still live.");
            return;
        }

        if (storages == 0)
        {
            result.Fail(
                $"{owned.Count} owned propert(ies) but no storage containers were found under any of them. A raid " +
                "would resolve as 'they found nothing'. If you have racks placed, the container scan is looking in " +
                "the wrong place — report the property code.");
            return;
        }

        result.Ok(
            $"All five consequence paths are constructed. Raids have {storages} container(s) and {contraband} " +
            "contraband stack(s) to work with. The dealer and customer numbers are snapshotted before they are " +
            "raised and written back on leaving outlaw status, on scene unload and on disable.");
    }

    /// <summary>
    /// Whether the two control surfaces actually took. This probe exists because "Registered 0/8" is
    /// a failure mode this module has shipped before, and it is invisible from inside the game.
    /// </summary>
    private static void Controls(ProbeContext context, ProbeResult result)
    {
        result.Fact("Menu actions bound", $"{PoliceMenuActions.All.Count} declared, registry binding {(PoliceMenuActions.BoundToRegistry ? "succeeded" : "**failed**")}");
        result.Fact("Events registered", $"{Events.PoliceEvents.RegisteredCount}/5");

        var actionIds = ActionRegistry.Actions
            .Where(action => action.Id.StartsWith(PoliceOverhaulModule.ModuleId + ".", StringComparison.Ordinal))
            .Select(action => action.Id)
            .ToList();

        var eventIds = EventRegistry.Events
            .Where(item => item.Id.StartsWith(PoliceOverhaulModule.ModuleId + ".", StringComparison.Ordinal))
            .Select(item => item.Id)
            .ToList();

        result.Heading("Live in Core's registries");
        result.Code(actionIds.Concat(eventIds).ToList());

        if (actionIds.Count == 0 && eventIds.Count == 0)
        {
            result.Fail(
                "Nothing from this module reached Core's action or event registry, so the F7 screen and the event " +
                "hotkey have no police controls at all. Check the MelonLoader log for a registration warning.");
            return;
        }

        if (actionIds.Count < PoliceMenuActions.All.Count || eventIds.Count < 5)
        {
            result.Fail(
                $"{actionIds.Count}/{PoliceMenuActions.All.Count} action(s) and {eventIds.Count}/5 event(s) are live. " +
                "A missing id usually means another module claimed it first; the log names the duplicate.");
            return;
        }

        result.Ok(
            $"{actionIds.Count} action(s) and {eventIds.Count} event(s) are registered under '{PoliceOverhaulModule.ModuleId}.', " +
            "so they appear on the Expansions screen and in the event chooser grouped as \"Police Improvements\".");
    }

    private static void Messages(ProbeContext context, ProbeResult result)
    {
        result.Fact("Contact id", DispatchContact.ContactId);
        result.Fact("Contact display", $"{DispatchContact.ContactFirstName} {DispatchContact.ContactLastName}");
        result.Fact("Contact ready", PoliceMessages.ContactReady ? "yes" : "**no**");
        result.Fact("Messages sent this session", PoliceMessages.Sent.ToString());
        result.Fact("Last delivery", PoliceMessages.LastDelivery);
        result.Fact("Last body", string.IsNullOrEmpty(PoliceMessages.LastBody) ? "-" : PoliceMessages.LastBody);

        if (!PoliceMessages.ContactReady)
        {
            result.Inconclusive(
                "Dispatch has not reported OnCreated yet. Load a save (S1API builds custom NPCs during NPCsLoader) " +
                "and run this again. Until then, substantive events fall back to toasts.");
            return;
        }

        result.Ok(
            "Phone texts come from the non-physical Dispatch contact, not from federal-agent clones. " +
            "Imminent raid warnings and custody clock-skips still toast on purpose.");
    }

    private static void Scheduler(ProbeContext context, ProbeResult result)
    {
        if (PoliceRuntime.Scheduler is not { } scheduler || PoliceRuntime.Config is not { } config)
        {
            result.Inconclusive("The module is not wired this session, so there is no scheduler.");
            return;
        }

        result.Fact("Enabled", Yes(config.EnableEventScheduler.Value));
        result.Fact("Seed", scheduler.Seed == 0 ? "**unset**" : scheduler.Seed.ToString());
        result.Fact("Rolls consumed", scheduler.Rolls.ToString());
        result.Fact("Post-load grace", $"{config.EventReadyGraceMinutes.Value} in-game minute(s)");
        result.Fact(
            "Federal interval",
            $"{config.FederalIntervalHoursMin.Value}-{config.FederalIntervalHoursMax.Value} in-game hour(s)");
        result.Fact(
            "Raid interval",
            $"{config.RaidIntervalHoursMin.Value}-{config.RaidIntervalHoursMax.Value} in-game hour(s)");
        result.Fact(
            "Next federal check",
            scheduler.MinutesUntilFederalCheck < 0
                ? "not queued"
                : $"{GameClock.Describe(scheduler.MinutesUntilFederalCheck)} (minute {scheduler.NextFederalCheckMinute})");
        result.Fact(
            "Next raid check",
            scheduler.MinutesUntilRaidCheck < 0
                ? "not queued"
                : $"{GameClock.Describe(scheduler.MinutesUntilRaidCheck)} (minute {scheduler.NextRaidCheckMinute})");
        result.Fact("Last decision", scheduler.LastDecision);
        result.Fact("World accepts events", scheduler.WorldAcceptsEvents(out var reason) ? $"yes ({reason})" : $"no ({reason})");

        if (!config.EnableEventScheduler.Value)
        {
            result.Ok(
                "Scheduler is off — federal and raid directors fire as soon as their heat/outlaw thresholds are met, " +
                "same as before this feature. Manual EventRegistry triggers are unchanged either way.");
            return;
        }

        result.Ok(
            "Cadence is save-deterministic (hand-rolled xorshift, never UnityEngine.Random). Heat and outlaw still " +
            "gate whether a due roll actually starts an event. Manual triggers bypass the wait and push the next slot.");
    }

    private static OutlawEconomy? Economy() => PoliceRuntime.Outlaw?.Economy;

    private static string Yes(bool value) => value ? "yes" : "**off**";
}

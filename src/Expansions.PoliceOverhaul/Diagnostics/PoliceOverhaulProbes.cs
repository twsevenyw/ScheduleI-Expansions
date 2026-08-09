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
            "police.enable_cycle",
            "Is disable→re-enable safe, and why were menu actions red?",
            Area,
            EnableCycle,
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

        var config = PoliceRuntime.Config;
        result.Fact(
            "Arrest → heat",
            config is null
                ? "-"
                : config.HeatResetOnArrest.Value
                    ? "heat_reset_on_arrest=true — booking clears heat to 0 (outlaw latch kept)"
                    : $"heat_reset_on_arrest=false — booking adds +{config.ArrestHeat.Value:0.#} (legacy)");

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
            result.Inconclusive(
                "Patches have not been applied this session. Services may already be live (actions green) while " +
                "Harmony is deferred a few frames after wire — re-run this probe.");
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

    private static void EnableCycle(ProbeContext context, ProbeResult result)
    {
        result.Fact("Wire status", PoliceRuntime.WireStatus);
        result.Fact("PoliceRuntime.IsLive", PoliceRuntime.IsLive ? "yes — actions should be green" : "**no — actions stay red**");
        result.Fact("HostGate", HostGate.Evaluate(out var authority) ? $"authority ({authority})" : $"blocked ({authority})");
        result.Fact("Active scene", GameReflection.ActiveSceneName());
        result.Fact("Gameplay scene?", PoliceOverhaulModule.IsGameplayScene() ? "yes" : "no");
        result.Fact("Harmony applied", PolicePatches.AppliedTargets.Count.ToString());
        result.Fact(
            "NPC policy",
            "no create/clone/re-identify/destroy — federal = designate shipped officers only");
        result.Fact(
            "Ownership check",
            $"managed IntPtr set — federal designated={FederalAgents.OwnedCount}; " +
            "empty set ⇒ CheckDeactivation short-circuits without touching officers");
        result.Fact("Deferred work", Deferred.Pending.ToString());

        result.Heading("What a clean cycle must prove");
        result.Bullet("Disable logs `disable-cycle OK` (restores + released federal designations).");
        result.Bullet("Re-enable logs `services attached` then `enable-cycle OK` a few frames later (deferred patches).");
        result.Bullet("F7 actions turn green as soon as services attach — they must not wait on Harmony.");
        result.Bullet("Save load must not AV — no ShouldSave ownership patch, no NPC cloning.");

        if (!PoliceRuntime.IsLive)
        {
            result.Fail(
                "Runtime is not live, so every police action is red. Wire retries from OnUpdate once Main is up and " +
                "HostGate passes — if this sticks, read Wire status above.");
            return;
        }

        if (PolicePatches.AppliedTargets.Count == 0)
        {
            result.Inconclusive(
                "Services are live (actions should work) but patches have not landed yet. Wait a moment and re-run, " +
                "or check the MelonLoader log for `enable-cycle OK`.");
            return;
        }

        result.Ok(
            "Runtime live and patches applied. Toggle the module off and on once; both cycle OK lines must appear " +
            "with no process death.");
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
                new[] { "Visible", census.Visible.ToString(), "rendered + FishNet-spawned + plausible world position — what you can actually see" },
                new[] { "Ghosts", census.Ghost.ToString(), "registered/hierarchy-active but invisible (the 'log said 8, I saw 0' bucket)" },
                new[] { "Hierarchy-active", census.OnDuty.ToString(), "old lying metric: GameObject active only — not the same as visible" },
                new[] { "Dead", census.Dead.ToString(), "will not come back on their own for several in-game days" },
                new[] { "Out cold", census.KnockedOut.ToString(), "knocked out; restored with the dead ones" },
                new[] { "Inactive", census.Inactive.ToString(), "present but switched off: pooled in a station or culled out of sight" },
                new[] { "Pooled", census.Pooled.ToString(), "the reserve `PoliceStation.Dispatch` can draw from" },
                new[] { "Stations", census.Stations.ToString(), "police stations found in the scene" },
                new[] { "Federal designated", $"{census.VisibleAgents}/{census.Agents}", "visible / tagged shipped officers on federal assignment" },
                new[] { "Total objects", census.Total.ToString(), "every shipped officer object in the scene" },
            });

        result.Heading("Per-officer presence (truth — position + distance, not manager counts)");
        var sightings = PoliceForce.Sightings(24);
        if (sightings.Count == 0)
        {
            result.Fact("Sightings", "none");
        }
        else
        {
            result.Code(sightings.ConvertAll(s =>
            {
                var dist = s.DistanceToPlayer?.ToString("0.0") ?? "?";
                var role = FederalAgents.IsAgent(s.Officer) ? "FED" : "shipped";
                return $"{(s.Visible ? "VISIBLE" : "HIDDEN"),-7} {role,-7} " +
                       $"go={(s.HierarchyActive ? "on" : "OFF")} " +
                       $"avatar={(s.AvatarActive ? "on" : "OFF")} ren={(s.RendererEnabled ? "on" : "OFF")} " +
                       $"net={(s.NetworkSpawned ? "spawned" : "UNSPAWNED")} " +
                       $"@ ({s.Position.x:0.0},{s.Position.y:0.0},{s.Position.z:0.0}) dist={dist}m " +
                       $"{s.Name}" +
                       (s.Visible ? "" : $" [{s.WhyNotVisible}]");
            }));
        }

        result.Heading("Deployment");
        result.Fact("Last relocate", OfficerDeployment.LastReport);
        result.Fact("Scene radius", $"{OfficerDeployment.SceneRadiusMetres:0}m");
        result.Fact("NPC policy", "relocate + designate only — never create/clone/destroy");
        if (PoliceRuntime.Raids is { IsPending: true } raids)
        {
            var point = Estate.Owned().FirstOrDefault(p =>
                string.Equals(Estate.NameOf(p), raids.PendingProperty, StringComparison.Ordinal));
            if (point is not null)
            {
                var at = OfficerDeployment.CountWithin(Estate.SpawnPointOf(point), OfficerDeployment.SceneRadiusMetres);
                result.Fact(
                    "Officers at pending raid property",
                    $"{at.Count} within {OfficerDeployment.SceneRadiusMetres:0}m " +
                    $"(nearest {(at.Count > 0 ? at.NearestMetres.ToString("0.0") + "m" : "n/a")})");
            }
        }

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

        result.Heading("Force size");
        result.Fact(
            "Federal designations",
            $"tagged={FederalAgents.OwnedCount} — managed IntPtr set; no native deref for ownership");
        result.Fact(
            "Honest ceiling note",
            "Closed officer set only. Density opens more posts and asks for more per post (Dispatch hard-caps 4); it never invents bodies.");

        result.Heading("Detection writes (idempotency probe)");
        var detection = PoliceRuntime.Detection;
        if (detection is null)
        {
            result.Fact("Managed officers", "detection tuner not wired");
        }
        else
        {
            result.Fact("Managed officers", detection.TrackedOfficers.ToString());
            result.Fact(
                "Total instance writes this session",
                $"{detection.TotalOfficerWrites} — must stay flat between ticks once heat is stable; a climbing number is the stutter bug returning");
            var writeLines = detection.ProbeLines(24);
            if (writeLines.Count == 0)
                result.Fact("Per-officer write counts", "none managed yet (first minute tick after wire applies once)");
            else
                result.Code(writeLines);
        }

        result.Heading("Revive / dispatch paths");
        result.Fact("Daily restore", config is null ? "-" : config.RespawnOfficersDaily.Value ? "on" : "**off**");
        result.Fact("Restored so far", $"{PoliceForce.TotalRevived} officer(s), {PoliceForce.LastRevived} at the last day rollover");
        result.Fact("Dispatched by this module", $"{PoliceForce.TotalDispatched} officer(s)");
        result.Fact("Response delay", config is null ? "-" : $"{config.ResponseDelaySeconds.Value:0.##}s");
        result.Fact(
            "Pursuit aggressiveness",
            config is null
                ? "-"
                : $"{config.PursuitAggressiveness.Value:0.##} (beginAsSighted={(PoliceRuntime.Response?.BeginAsSighted ?? false ? "yes" : "no")})");
        result.Fact("Officer-kill responses", PoliceRuntime.OfficerKills is { } kills ? $"{kills.KillsThisSession} this session; {kills.LastResponse}" : "-");
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

        if (census.Visible == 0)
        {
            result.Fail(
                $"0 visible officers (hierarchy-active={census.OnDuty}, ghosts={census.Ghost}, inactive={census.Inactive}). " +
                "If the log claimed responders near the scene, they were ghosts — check the per-officer table for " +
                "UNSPAWNED / Avatar OFF / bad position.");
            return;
        }

        if (census.Ghost > 0)
        {
            result.Fail(
                $"{census.Ghost} ghost unit(s) are registered but not a rendered/networked presence. " +
                "That is the 'I don't see any cops' bug — fix presence before trusting OnDuty counts.");
            return;
        }

        if (census.Dead > census.Visible)
        {
            result.Fail(
                $"{census.Dead} dead against {census.Visible} visible. The force is losing officers faster than they " +
                "come back, so patrols and checkpoints will thin out over the next few days even though the schedule " +
                "is asking for them.");
            return;
        }

        result.Ok(
            $"{census.Visible} visible officer(s) ({census.OnDuty} hierarchy-active) with {census.Pooled} in reserve. " +
            "Density and staffing are snapshot-and-restore over the shipped closed set — no cloning.");
    }

    private static void Federal(ProbeContext context, ProbeResult result)
    {
        var status = FederalAgents.Survey();
        var fedConfig = PoliceRuntime.Config;

        result.Fact("Path", "designate shipped officers (relocate + buff + restore) — never clone");
        result.Fact("Last deploy", OfficerDeployment.LastReport);
        result.Fact("Designate viable", status.CanDesignate ? $"yes — {status.Reason}" : $"no — {status.Reason}");
        result.Fact("Currently designated", FederalAgents.LiveCount.ToString());
        result.Fact("Configured team size", fedConfig?.FederalAgentsPerEvent.Value.ToString() ?? "-");
        result.Fact("Configured duration (h)", fedConfig?.FederalEventHours.Value.ToString() ?? "-");

        if (PoliceRuntime.Federal is { } events)
        {
            result.Fact("Event active", events.IsActive ? $"yes, {events.HoursRemaining} in-game hour(s) left" : "no");
            result.Fact("Designated officers", events.FieldedOfficers.ToString());
            result.Fact("Mode", events.IsActive ? events.IsStakeout ? $"stakeout on {events.StakeoutProperty}" : "foot pursuit" : "-");
            result.Fact("Target", events.TargetKey.Length > 0 ? events.TargetKey : "-");
        }

        result.Ok(
            "Federal / stakeout designate the town's own officers by relocate + reversible buffs. " +
            "Release restores stats; nothing is created or destroyed.");
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
        result.Fact(
            "Legal fee desk",
            LegalFeeDesk.IsAttached
                ? $"attached — {LegalFeeDesk.Status}; {LegalFeeDesk.AttachedTo}"
                : $"**not attached** — {LegalFeeDesk.Status}" +
                  (LegalFeeDesk.LastFailure.Length > 0 ? $" ({LegalFeeDesk.LastFailure})" : string.Empty) +
                  "; F7 repair path stays visible");
        result.Fact("Legal fee Marked", $"${config.LegalFeeMarked.Value:N0} (legal_fee_marked)");
        result.Fact("Legal fee Hunted", $"${config.LegalFeeHunted.Value:N0} (legal_fee_hunted)");

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
                new[]
                {
                    "Legal fee at station door",
                    Yes(config.EnableOutlaw.Value),
                    LegalFeeDesk.IsAttached
                        ? $"{LegalFeeDesk.DoorCount} door(s) on {LegalFeeDesk.StationCount} station(s): {LegalFeeDesk.AttachedTo}"
                        : $"**door hook down** — {LegalFeeDesk.Status}; menu repair path exposed",
                },
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
    /// Whether the two control surfaces actually took, and — for every registered action — whether
    /// clicking Run right now would change live game state or refuse with a named precondition.
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

        result.Heading("Would Run do something right now?");
        var readinessRows = new List<IReadOnlyList<string>>();
        var blocked = 0;

        foreach (var entry in PoliceMenuActions.All)
        {
            var availability = SafeAvailability(entry.Availability);
            var label = entry.DynamicLabel?.Invoke() ?? entry.Label ?? entry.Id;
            if (!availability.IsAvailable)
                blocked++;

            readinessRows.Add(new[]
            {
                label,
                availability.IsAvailable ? "READY" : "**blocked**",
                availability.IsAvailable
                    ? LiveEffectHint(entry.Id)
                    : availability.Reason.Length > 0 ? availability.Reason : "unavailable",
            });
        }

        foreach (var item in EventRegistry.Events.Where(e => e.Id.StartsWith(PoliceOverhaulModule.ModuleId + ".", StringComparison.Ordinal)))
        {
            var availability = item.GetAvailability();
            if (!availability.IsAvailable)
                blocked++;

            readinessRows.Add(new[]
            {
                $"[event] {item.Label}",
                availability.IsAvailable ? "READY" : "**blocked**",
                availability.IsAvailable
                    ? LiveEffectHint(item.Id)
                    : availability.Reason.Length > 0 ? availability.Reason : "unavailable",
            });
        }

        result.Table(new[] { "Control", "Status", "What Run would do / why not" }, readinessRows);

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
            $"{actionIds.Count} action(s) and {eventIds.Count} event(s) registered. {blocked} control(s) are blocked " +
            "by a named precondition right now — that is expected (no property, already Hunted, etc.), not a silent no-op.");
    }

    private static void Messages(ProbeContext context, ProbeResult result)
    {
        result.Fact("Channel", "toast-only (no custom Dispatch NPC)");
        result.Fact("Display label", DispatchContact.DisplayName);
        result.Fact("Legacy contact id", $"{DispatchContact.ContactId} (not spawned)");
        result.Fact("Channel ready", PoliceMessages.ContactReady ? "yes" : "**no**");
        result.Fact("Announcements", PoliceRuntime.Config is { ShowHud.Value: false } ? "**off** (show_heat_hud)" : "on");
        result.Fact("Announce mode", PoliceMessages.DescribeMode());
        result.Fact("Messages sent this session", PoliceMessages.Sent.ToString());
        result.Fact("Last delivery", PoliceMessages.LastDelivery);
        result.Fact("Last body", string.IsNullOrEmpty(PoliceMessages.LastBody) ? "-" : PoliceMessages.LastBody);

        result.Ok(
            "Police announcements are on-screen toasts labelled Dispatch Office. A custom messaging NPC " +
            "was retired because S1API could not finalize it and the half-built body crashed the game.");
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
            $"{config.RaidIntervalHoursMin.Value}-{config.RaidIntervalHoursMax.Value}h " +
            $"(outlaw scale x{config.RaidOutlawIntervalScale.Value:0.##}, heat threshold {config.RaidHeatThreshold.Value}, cooldown {config.RaidCooldownDays.Value}d)");
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

    private static ActionAvailability SafeAvailability(Func<ActionAvailability>? predicate)
    {
        if (predicate is null)
            return ActionAvailability.Unavailable("no availability predicate");

        try
        {
            return predicate();
        }
        catch (Exception ex)
        {
            return ActionAvailability.Unavailable($"availability threw: {ex.GetType().Name}");
        }
    }

    /// <summary>One-line "what the player should see" for a READY control — not a substitute for clicking it.</summary>
    private static string LiveEffectHint(string id) => id switch
    {
        "police_overhaul.show_heat" => "read-out toast+text of heat/tier/outlaw/raid/federal",
        "police_overhaul.explain_outlaw" => "itemised outlaw bill as toast+text",
        "police_overhaul.heat_up" => $"heat +25 and immediate intensity rewrite (now {PoliceRuntime.Heat?.LocalRecord.Heat:0})",
        "police_overhaul.heat_max" => "heat=100, Federal band, posts/detection applied now",
        "police_overhaul.heat_clear" => "heat=0, world back toward vanilla intensity now",
        "police_overhaul.outlaw_next" => "promote Clean→Marked or Marked→Hunted + economy sync",
        "police_overhaul.outlaw_clear" => "demote one outlaw tier and pull heat under the latch",
        "police_overhaul.outlaw_pay" => LegalFeeDesk.IsAttached
            ? "hidden — knock on the police station door instead"
            : $"repair path: spend ${PoliceRuntime.Config?.FeeFor(PoliceRuntime.LocalRecord?.Outlaw ?? OutlawTier.Marked):N0} to demote one tier",
        "police_overhaul.spawn_federal" or "police_overhaul.federal_team" => "spawn plain-clothes agents (or stakeout if you are home)",
        "police_overhaul.raid_trigger" or "police_overhaul.raid_warning" => "schedule a warned raid on an owned property",
        "police_overhaul.raid_now" => "execute a raid immediately on an owned property",
        "police_overhaul.restore_force" => "Revive() every dead/KO officer; report the census",
        "police_overhaul.reset" => "wipe heat/outlaw/raid/agents and restore vanilla levers",
        "police_overhaul.escalate_outlaw" => "promote outlaw tier + apply economy/visibility",
        "police_overhaul.call_police" => "CallPolice + sighted Dispatch under response_delay/aggressiveness",
        _ => "invoke the registered handler",
    };
}

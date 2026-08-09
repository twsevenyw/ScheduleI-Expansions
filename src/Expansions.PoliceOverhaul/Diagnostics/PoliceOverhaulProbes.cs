using Expansions.Core.Diagnostics;
using Expansions.PoliceOverhaul.Patches;
using Expansions.PoliceOverhaul.Runtime;
using Expansions.PoliceOverhaul.State;

namespace Expansions.PoliceOverhaul.Diagnostics;

/// <summary>
/// Five questions the owner cannot answer by looking at the game: what heat is doing, which levers
/// this build actually let us pull, whether the patches landed, whether federal agents are viable,
/// and where the outlaw state machine is. All read-only.
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
            "police.federal_agents",
            "Can this build spawn a federal agent, and how?",
            Area,
            Federal);

        yield return new DelegateProbe(
            "police.world_state",
            "What is the mod currently doing to the world?",
            Area,
            WorldState);
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
            $"Agents are viable via the {status.Strategy} path. They are tagged by native pointer, never added to " +
            "`PoliceStation.OfficerPool`, and `ShouldSave` is forced false for them, so they cannot leak into the save. " +
            "Spawn one from the F7 menu and then save, reload and check the NPC set is unchanged.");
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

        var (min, max) = HeatModel.OfficerBand(heat.PeakTier, config.IntensityScalar.Value, config.MaxOfficersPerPost.Value);
        result.Fact("Officers per post", $"{min}-{max} (vanilla is 1-2)");
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
}

using Expansions.Core;
using Expansions.Core.Actions;
using Expansions.Core.Diagnostics;
using Expansions.PoliceOverhaul.Runtime;
using Expansions.PoliceOverhaul.State;

namespace Expansions.PoliceOverhaul;

/// <summary>
/// Everything the owner can do to this module from the F7 screen.
/// <para>
/// The in-game console is unusable on this install, so the menu is the only control surface — which
/// makes these actions load-bearing rather than a convenience.
/// </para>
/// </summary>
internal static class PoliceMenuActions
{
    private static readonly List<Entry> Entries = new();

    /// <summary>Set by the show-heat action; the tutorial's first objective watches it.</summary>
    internal static bool HeatWasRead { get; private set; }

    internal static bool BoundToRegistry { get; private set; }

    internal static IReadOnlyList<Entry> All => Entries;

    internal static void Register(ModuleLifetime lifetime)
    {
        Entries.Clear();
        HeatWasRead = false;

        // Ids carry the module id so Core groups them under "Police Improvements" automatically.
        Add("police_overhaul.show_heat", "Show heat",
            "Report heat, tier, outlaw status and what the module is currently doing to the world.",
            ShowHeat);

        Add("police_overhaul.heat_up", "Heat +25",
            "Raise your heat by 25, for testing the tier ladder without committing 25 crimes.",
            () => Nudge(+25f));

        Add("police_overhaul.heat_max", "Set heat to 100",
            "Jump straight to the Federal band: maximum officers per post, all-hours checkpoints, federal agents eligible.",
            () => SetHeat(100f));

        Add("police_overhaul.heat_clear", "Clear heat",
            "Drop heat to zero. The world returns to exactly vanilla within one game minute.",
            () => SetHeat(0f));

        Add("police_overhaul.outlaw_next", "Next outlaw tier",
            "Force Clean to Marked, or Marked to Hunted, without waiting for the heat threshold.",
            NextOutlawTier);

        Add("police_overhaul.outlaw_clear", "Clear outlaw status",
            "Drop one outlaw tier and pull heat back under the latch threshold, the same as serving three clean days.",
            ClearOutlawTier);

        Add("police_overhaul.spawn_federal", "Spawn federal agent",
            "Dispatch a plain-clothes team to your position now. Reports why if this build cannot spawn them.",
            SpawnFederal);

        Add("police_overhaul.reset", "Reset all police state",
            "Wipe every heat record, clear outlaw status, withdraw any agents and put the world back to vanilla.",
            ResetEverything);

        BindToRegistry(lifetime);
    }

    internal static void Invoke(string id)
    {
        foreach (var entry in Entries)
        {
            if (string.Equals(entry.Id, id, StringComparison.Ordinal))
            {
                entry.Invoke();
                return;
            }
        }
    }

    // ── Actions ───────────────────────────────────────────────────────────────────────────────

    private static void ShowHeat()
    {
        HeatWasRead = true;

        if (PoliceRuntime.Heat is not { } heat || PoliceRuntime.Config is not { } config)
        {
            Report("Police Improvements", "Not wired into a loaded game yet.");
            return;
        }

        var record = heat.LocalRecord;
        var (min, max) = HeatModel.OfficerBand(heat.PeakTier, config.IntensityScalar.Value, config.MaxOfficersPerPost.Value);

        Report(
            $"Heat {record.Heat:0} — {HeatModel.TierName(record.Tier)}",
            $"Outlaw: {OutlawState.Describe(record.Outlaw)}. Law intensity {heat.CurrentIntensity} " +
            $"(vanilla baseline {heat.BaselineIntensity}). {min}-{max} officers per post.");

        PoliceLog.Msg(
            $"Heat {record.Heat:0.#} ({HeatModel.TierName(record.Tier)}) · outlaw {OutlawState.Describe(record.Outlaw)} · " +
            $"clean-day streak {record.CleanDayStreak} · police take ${record.PoliceTakeTotal:0} · " +
            $"intensity {heat.CurrentIntensity} over baseline {heat.BaselineIntensity} · " +
            $"officers per post {min}-{max} · federal agents {(FederalAgents.Status.CanSpawn ? "viable" : "unavailable: " + FederalAgents.Status.Reason)}.");
    }

    private static void Nudge(float delta)
    {
        if (PoliceRuntime.Heat is not { } heat)
            return;

        var record = heat.LocalRecord;
        heat.SetHeat(record, record.Heat + delta);
        Report($"Heat {record.Heat:0}", HeatModel.TierName(record.Tier) + " — the world catches up within a game minute.");
    }

    private static void SetHeat(float value)
    {
        if (PoliceRuntime.Heat is not { } heat)
            return;

        var record = heat.LocalRecord;
        heat.SetHeat(record, value);
        Report($"Heat set to {record.Heat:0}", HeatModel.TierName(record.Tier));
    }

    private static void NextOutlawTier()
    {
        if (PoliceRuntime.Heat is not { } heat || PoliceRuntime.Outlaw is not { } outlaw)
            return;

        var record = heat.LocalRecord;
        var next = record.Outlaw == OutlawTier.Clean ? OutlawTier.Marked : OutlawTier.Hunted;
        outlaw.Promote(record, next);
        outlaw.Sync();

        Report($"Outlaw: {OutlawState.Describe(next)}",
            "Searches will find something, pursuits will not let go, and fines are doubled.");
    }

    private static void ClearOutlawTier()
    {
        if (PoliceRuntime.Heat is not { } heat || PoliceRuntime.Outlaw is not { } outlaw)
            return;

        var record = heat.LocalRecord;
        outlaw.Demote(record);
        outlaw.Sync();

        Report($"Outlaw: {OutlawState.Describe(record.Outlaw)}", $"Heat pulled back to {record.Heat:0}.");
    }

    private static void SpawnFederal()
    {
        if (PoliceRuntime.Federal is not { } federal)
            return;

        federal.ForceBegin(out var message);
        Report("Federal agents", message);
        PoliceLog.Msg(message);
    }

    private static void ResetEverything()
    {
        if (PoliceRuntime.Heat is not { } heat)
            return;

        PoliceRuntime.Federal?.Abort();
        PoliceRuntime.Consequences?.ClearAllCharges();

        foreach (var record in heat.Records)
        {
            record.Heat = 0f;
            record.Outlaw = OutlawTier.Clean;
            record.CleanDayStreak = 0;
            record.ArrestsWhileOutlaw = 0;
            record.FederalEncounters = 0;
            record.MinutesAtFederalHeat = 0;
            record.PoliceTakeTotal = 0f;
            record.DealHeatToday = 0f;
            record.ArrestDays.Clear();
            record.LastFederalEventDay = -1;
        }

        // Same order as module teardown: the schedule restore ends in Evaluate(), so it goes last.
        PoliceRuntime.Outlaw?.Revert();
        PoliceRuntime.Detection?.Restore();
        PoliceRuntime.Heat?.Restore();
        PoliceRuntime.Schedule?.Restore();

        Report("Police state reset", "Every record cleared and the world put back to vanilla.");
        PoliceLog.Msg("All police state reset by the menu action.");
    }

    // ── Registry binding ──────────────────────────────────────────────────────────────────────

    /// <summary>Hands the actions to Core's registry, ordered as declared.</summary>
    private static void BindToRegistry(ModuleLifetime lifetime)
    {
        BoundToRegistry = false;

        var bound = 0;
        for (var i = 0; i < Entries.Count; i++)
        {
            var entry = Entries[i];

            try
            {
                lifetime.Add(ActionRegistry.Register(new ExpansionAction(
                    id: entry.Id,
                    label: entry.Label,
                    description: entry.Description,
                    invoke: entry.Invoke,
                    isAvailable: Availability,
                    order: i)));

                bound++;
            }
            catch (Exception ex)
            {
                PoliceLog.Warn($"Could not register menu action '{entry.Id}': {PoliceLog.Describe(ex)}");
            }
        }

        BoundToRegistry = bound > 0;
        PoliceLog.Msg($"Registered {bound}/{Entries.Count} police menu action(s) with Core.");
    }

    private static ActionAvailability Availability() =>
        PoliceRuntime.IsLive
            ? ActionAvailability.Ready
            : ActionAvailability.Unavailable("needs a loaded save with the module running");

    private static void Add(string id, string label, string description, Action invoke)
    {
        Entries.Add(new Entry(id, label, description, invoke));
        PoliceLog.Detail($"Menu action available: {label}");
    }

    /// <summary>Toast plus log, because the console does not work on this install.</summary>
    private static void Report(string title, string body)
    {
        GameBridge.Notify(title, body, 8f);
        PoliceLog.Msg($"{title} — {body}");
    }

    internal sealed class Entry
    {
        internal Entry(string id, string label, string description, Action invoke)
        {
            Id = id;
            Label = label;
            Description = description;
            Invoke = invoke;
        }

        internal string Id { get; }

        internal string Label { get; }

        internal string Description { get; }

        internal Action Invoke { get; }
    }
}

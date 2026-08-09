using Expansions.Core;
using Expansions.Core.Actions;
using Expansions.PoliceOverhaul.Runtime;
using Expansions.PoliceOverhaul.State;

namespace Expansions.PoliceOverhaul;

/// <summary>
/// Everything the owner can do to this module from the F7 screen.
/// <para>
/// The in-game console is unusable on this install, so the menu is the only control surface — which
/// makes these actions load-bearing rather than a convenience. Two rules follow from that and are
/// worth stating: every action returns a sentence describing what it did, and no action ever returns
/// early in silence. A button that produces no visible response is indistinguishable from a broken
/// one, and this module has already shipped that bug once.
/// </para>
/// <para>
/// <see cref="ActionRegistry"/> is called directly rather than through reflection. Registering these
/// via <c>Activator.CreateInstance</c> against a guessed constructor is exactly how all eight of them
/// silently failed to appear in a previous build.
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
        Add("police_overhaul.show_heat", "Show heat and status",
            "Heat, tier, outlaw status, what the mod is doing to the world, and whether a raid is inbound.",
            Readiness.Live, ShowHeat);

        Add("police_overhaul.explain_outlaw", "What is outlaw status costing me?",
            "Itemises the business side: the dealer surcharge, the customer snitch bonus, the closed vendors and what an arrest would cost you right now.",
            Readiness.Live, ExplainOutlaw);

        Add("police_overhaul.heat_up", "Heat +25",
            "Raise your heat by 25, for walking up the tier ladder without committing 25 crimes.",
            Readiness.Live, () => Nudge(+25f));

        Add("police_overhaul.heat_max", "Set heat to 100",
            "Jump straight to the Federal band: maximum officers per post, all-hours checkpoints, federal agents eligible.",
            Readiness.Live, () => SetHeat(100f));

        Add("police_overhaul.heat_clear", "Clear heat",
            "Drop heat to zero. The world returns to exactly vanilla within one game minute.",
            Readiness.Live, () => SetHeat(0f));

        Add("police_overhaul.outlaw_next", "Next outlaw tier",
            "Force Clean to MARKED, or MARKED to HUNTED, without waiting for the heat threshold.",
            Readiness.Escalate, NextOutlawTier);

        Add("police_overhaul.outlaw_clear", "Serve out one outlaw tier",
            "Drop one tier and pull heat back under the latch threshold, the same as serving three clean days. Free, unlike the lawyer.",
            Readiness.Outlaw, ClearOutlawTier);

        AddDynamic("police_overhaul.outlaw_pay", LegalFeeLabel,
            "Buy one outlaw tier down with money instead of time. Cash first, then your bank balance.",
            Readiness.LegalFee, PayLegalFee);

        Add("police_overhaul.spawn_federal", "Send a federal team",
            "Dispatch plain-clothes agents now. If you are inside a property you own they stake it out instead of chasing you.",
            Readiness.Federal, SpawnFederal);

        Add("police_overhaul.raid_trigger", "Put a raid on the clock",
            "Starts the warning for a raid on one of your properties, so you can watch the whole sequence without waiting for the trigger.",
            () => Readiness.Raid(immediate: false), TriggerRaid);

        Add("police_overhaul.restore_force", "Put the police back on duty",
            "Counts every officer in town and revives the dead ones now, instead of waiting for the day rollover. Use this the moment the streets look empty.",
            Readiness.Live, RestoreForce);

        Add("police_overhaul.reset", "Reset all police state",
            "Wipe every heat record, clear outlaw status, call off any raid, withdraw agents and put the world back to vanilla.",
            Readiness.Live, ResetEverything);

        BindToRegistry(lifetime);
    }

    // ── Actions ───────────────────────────────────────────────────────────────────────────────

    private static ActionResult ShowHeat()
    {
        HeatWasRead = true;

        if (PoliceRuntime.Heat is not { } heat || PoliceRuntime.Config is not { } config)
            return NotWired();

        var record = heat.LocalRecord;
        var (min, max) = HeatModel.OfficerBand(heat.PeakTier, config.IntensityScalar.Value, config.MaxOfficersPerPost.Value, config.PoliceDensity.Value);

        var headline = $"Heat {record.Heat:0} — {HeatModel.TierName(record.Tier)}";
        var lines = new List<string>
        {
            $"Outlaw {OutlawState.Describe(record.Outlaw)}, clean-day streak {record.CleanDayStreak}/{config.OutlawClearDays.Value}.",
            $"Law intensity {heat.CurrentIntensity} against a vanilla baseline of {heat.BaselineIntensity}; {min}-{max} officers per post.",
            $"The police have taken ${record.PoliceTakeTotal:N0} off you in total.",
        };

        if (PoliceRuntime.Raids is { } raids)
        {
            lines.Add(raids.IsPending
                ? $"A raid on {raids.PendingProperty} lands in {GameClock.Describe(raids.MinutesUntilRaid)} — be there and you lose nothing."
                : $"No raid inbound. {record.RaidsSuffered} suffered, {record.RaidsAvoided} avoided.");
        }

        if (PoliceRuntime.Federal is { } federal)
        {
            lines.Add(federal.IsActive
                ? federal.IsStakeout
                    ? $"Agents are staking out {federal.StakeoutProperty} for another {federal.HoursRemaining}h."
                    : $"A federal team is hunting you for another {federal.HoursRemaining}h."
                : FederalAgents.Status.CanDesignate
                    ? "No federal team out. Shipped officers can be designated on this save."
                    : $"No federal team out: {FederalAgents.Status.Reason}.");
        }

        var body = string.Join(" ", lines);
        PoliceMessages.Announce(headline, body);
        return ActionResult.Ok($"{headline}. {body}");
    }

    /// <summary>
    /// The itemised bill for being outlawed. This exists because every effect it lists is a number
    /// change the player would otherwise only feel as "things seem worse", and a consequence nobody
    /// can point at is a consequence that reads as imaginary.
    /// </summary>
    private static ActionResult ExplainOutlaw()
    {
        if (PoliceRuntime.Config is not { } config || PoliceRuntime.Outlaw is not { } outlaw ||
            PoliceRuntime.Consequences is not { } consequences || PoliceRuntime.Heat is not { } heat)
        {
            return NotWired();
        }

        var record = heat.LocalRecord;
        var economy = outlaw.Economy;

        if (record.Outlaw == OutlawTier.Clean)
        {
            var preview =
                $"You are CLEAN, so none of this is being charged. If you were outlawed: dealers would take an extra " +
                $"{config.OutlawDealerCutBonus.Value:P0}, every customer would be {config.OutlawSnitchBonus.Value:P0} more likely to " +
                $"call the police, card-only vendors would refuse you, fines would be x{config.OutlawFineMultiplier.Value:0.0}, and an " +
                $"arrest would cost you the rest of the day plus ${consequences.Custody.ProcessingFeeDue():N0} in processing.";

            PoliceMessages.Announce("Outlaw: CLEAN", preview);
            return ActionResult.Ok(preview);
        }

        var bill =
            $"{economy.DealersAffected} dealer(s) are taking an extra {config.OutlawDealerCutBonus.Value:P0}. " +
            $"{economy.CustomersAffected} customer(s) are {config.OutlawSnitchBonus.Value:P0} more likely to call the police. " +
            $"{(config.OutlawBlocksCardVendors.Value ? "Card-only vendors are refusing you." : "Card-only vendors are still serving you (setting off).")} " +
            $"Fines are x{config.OutlawFineMultiplier.Value:0.0} on top of the heat multiplier. " +
            $"An arrest right now costs the rest of the day plus ${consequences.Custody.ProcessingFeeDue():N0} in processing, " +
            $"and takes your tools. Buying the tier down costs ${config.OutlawLegalFee.Value:N0}; serving it out takes " +
            $"{config.OutlawClearDays.Value - record.CleanDayStreak} more clean day(s).";

        PoliceMessages.Announce($"Outlaw: {OutlawState.Describe(record.Outlaw)}", bill);
        return ActionResult.Ok(bill);
    }

    private static ActionResult Nudge(float delta)
    {
        if (PoliceRuntime.Heat is not { } heat)
            return NotWired();

        var record = heat.LocalRecord;
        var before = record.Heat;
        heat.SetHeat(record, record.Heat + delta);

        if (Math.Abs(record.Heat - before) < 0.01f)
        {
            return ActionResult.NoChange(
                $"Heat is already pinned at {record.Heat:0} ({HeatModel.TierName(record.Tier)}); it cannot go higher.");
        }

        var message =
            $"Heat {before:0} -> {record.Heat:0} ({HeatModel.TierName(record.Tier)}). " +
            $"Law intensity is now {heat.CurrentIntensity} (baseline {heat.BaselineIntensity}); posts and detection re-evaluated immediately.";
        PoliceMessages.Announce($"Heat {record.Heat:0}", message);
        return ActionResult.Ok(message);
    }

    private static ActionResult SetHeat(float value)
    {
        if (PoliceRuntime.Heat is not { } heat)
            return NotWired();

        var record = heat.LocalRecord;
        var before = record.Heat;
        heat.SetHeat(record, value);

        if (Math.Abs(record.Heat - before) < 0.01f)
            return ActionResult.NoChange($"Heat was already {record.Heat:0} ({HeatModel.TierName(record.Tier)}).");

        var message =
            $"Heat set to {record.Heat:0} ({HeatModel.TierName(record.Tier)}). " +
            $"Law intensity is now {heat.CurrentIntensity} (baseline {heat.BaselineIntensity}); posts and detection re-evaluated immediately.";
        PoliceMessages.Announce($"Heat {record.Heat:0}", message);
        return ActionResult.Ok(message);
    }

    private static ActionResult NextOutlawTier()
    {
        if (PoliceRuntime.Heat is not { } heat || PoliceRuntime.Outlaw is not { } outlaw)
            return NotWired();

        var record = heat.LocalRecord;
        var next = record.Outlaw == OutlawTier.Clean ? OutlawTier.Marked : OutlawTier.Hunted;
        outlaw.Promote(record, next);
        outlaw.Sync();

        var message = $"You are now {OutlawState.Describe(next)}. Searches will find something, pursuits will not let go, " +
                      "fines are doubled, dealers charge more and card-only vendors have closed to you.";

        PoliceMessages.Announce($"Outlaw: {OutlawState.Describe(next)}", message);
        return ActionResult.Ok(message);
    }

    private static ActionResult ClearOutlawTier()
    {
        if (PoliceRuntime.Heat is not { } heat || PoliceRuntime.Outlaw is not { } outlaw)
            return NotWired();

        var record = heat.LocalRecord;
        if (record.Outlaw == OutlawTier.Clean)
            return ActionResult.NoChange("Your record is already clean.");

        var previous = record.Outlaw;
        outlaw.Demote(record);
        outlaw.Sync();

        var message = $"{OutlawState.Describe(previous)} down to {OutlawState.Describe(record.Outlaw)}, " +
                      $"and heat pulled back to {record.Heat:0} so it does not immediately re-latch.";

        PoliceMessages.Announce($"Outlaw: {OutlawState.Describe(record.Outlaw)}", message);
        return ActionResult.Ok(message);
    }

    private static ActionResult PayLegalFee()
    {
        if (PoliceRuntime.Heat is not { } heat || PoliceRuntime.Outlaw is not { } outlaw)
            return NotWired();

        var paid = outlaw.PayLegalFee(heat.LocalRecord, out var message);
        if (!paid)
            PoliceMessages.Announce("Legal fee", message);
        return paid ? ActionResult.Ok(message) : ActionResult.Failed(message);
    }

    private static string LegalFeeLabel()
    {
        // Null before the module wires into a scene, which is also when this label is first built.
        // Showing "$0" then would be a lie about the price rather than an absence of one.
        var fee = PoliceRuntime.Config?.OutlawLegalFee.Value;
        return fee is null ? "Pay the legal fee" : $"Pay the legal fee (${fee.Value:N0})";
    }

    private static ActionResult SpawnFederal()
    {
        if (PoliceRuntime.Federal is not { } federal)
            return NotWired();

        var started = federal.ForceBegin(out var message);
        // ForceBegin already texts the rich FederalBegan copy on success.
        if (!started)
            PoliceMessages.Announce("Federal agents", message);
        return started ? ActionResult.Ok(message) : ActionResult.Failed(message);
    }

    private static ActionResult TriggerRaid()
    {
        if (PoliceRuntime.Raids is not { } raids)
            return NotWired();

        var started = raids.Force(immediate: false, out var message);
        // RaidWarning already announces on success; still surface failures as a Dispatch text.
        if (!started)
            PoliceMessages.Announce("Raid", message);
        return started ? ActionResult.Ok(message) : ActionResult.Failed(message);
    }

    /// <summary>
    /// The manual half of the daily restore, because "there are no cops" is a thing the owner notices
    /// mid-session and should not have to sleep through a night to test a fix for. Reports the census
    /// either way, so pressing it on a healthy map is still an answer rather than nothing happening.
    /// </summary>
    private static ActionResult RestoreForce()
    {
        var before = PoliceForce.Count();
        var revived = PoliceForce.ReturnToDuty();
        var after = PoliceForce.Count();

        var message = before.Total == 0 && revived == 0
            ? "There are no officer objects in this scene at all, so there was nothing to revive. Load a save and try again."
            : $"{revived} revived via NPCHealth.Revive(). " +
              $"Force now {after.Visible} visible / {after.OnDuty} hierarchy-active " +
              $"({after.Ghost} ghost) / {after.Total - after.Agents} shipped total. " +
              "No cloning — the closed officer set is the ceiling.";

        PoliceMessages.Announce("Police force", message);
        return before.Total == 0 && revived == 0
            ? ActionResult.Failed(message)
            : ActionResult.Ok(message);
    }

    private static ActionResult ResetEverything()
    {
        if (PoliceRuntime.Heat is not { } heat)
            return NotWired();

        PoliceRuntime.Raids?.Cancel("the owner reset police state");
        PoliceRuntime.Federal?.Abort();
        PoliceRuntime.Consequences?.ClearAllCharges();

        var wiped = 0;
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
            record.LastRaidDay = -1;
            record.LastRaidWarnedDay = -1;
            record.RaidsSuffered = 0;
            record.RaidsAvoided = 0;
            record.LegalFeesPaid = 0f;
            wiped++;
        }

        // Same order as module teardown: the schedule restore ends in Evaluate(), so it goes last.
        PoliceRuntime.Outlaw?.Revert();
        PoliceRuntime.Detection?.Restore();
        PoliceRuntime.Heat?.Restore();
        PoliceRuntime.Schedule?.Restore();

        var message = $"{wiped} player record(s) cleared, dealer cuts and snitch chances restored, and the world put back to vanilla.";
        PoliceMessages.Announce("Police state reset", message);
        return ActionResult.Ok(message);
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
                ExpansionAction action;

                if (entry.DynamicLabel is { } dynamicLabel)
                {
                    action = ExpansionAction.WithDynamicLabel(
                        id: entry.Id,
                        label: dynamicLabel,
                        description: entry.Description,
                        invoke: entry.Invoke,
                        isAvailable: entry.Availability,
                        order: i);
                }
                else
                {
                    action = new ExpansionAction(
                        id: entry.Id,
                        label: entry.Label ?? entry.Id,
                        description: entry.Description,
                        isAvailable: entry.Availability,
                        invoke: entry.Invoke,
                        order: i);
                }

                lifetime.Add(ActionRegistry.Register(action));
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

    private static ActionResult NotWired() => ActionResult.Failed(
        "Police Improvements is not wired into a loaded game, so there is no police state to change. " +
        "Load a save; if you are a client in co-op, the host owns this.");

    private static void Add(string id, string label, string description, Func<ActionAvailability> availability, Func<ActionResult> invoke)
    {
        Entries.Add(new Entry(id, label, null, description, availability, invoke));
        PoliceLog.Detail($"Menu action available: {label}");
    }

    private static void AddDynamic(string id, Func<string> label, string description, Func<ActionAvailability> availability, Func<ActionResult> invoke)
    {
        Entries.Add(new Entry(id, null, label, description, availability, invoke));
        PoliceLog.Detail($"Menu action available: {label()}");
    }

    internal sealed class Entry
    {
        internal Entry(
            string id,
            string? label,
            Func<string>? dynamicLabel,
            string description,
            Func<ActionAvailability> availability,
            Func<ActionResult> invoke)
        {
            Id = id;
            Label = label;
            DynamicLabel = dynamicLabel;
            Description = description;
            Availability = availability;
            Invoke = invoke;
        }

        internal string Id { get; }

        /// <summary>Null when the label depends on state; see <see cref="DynamicLabel"/>.</summary>
        internal string? Label { get; }

        internal Func<string>? DynamicLabel { get; }

        internal string Description { get; }

        internal Func<ActionAvailability> Availability { get; }

        internal Func<ActionResult> Invoke { get; }
    }
}

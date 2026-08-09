using Expansions.Core.Diagnostics;
using Expansions.PoliceOverhaul.State;
using S1API.GameTime;
using UnityEngine;

namespace Expansions.PoliceOverhaul.Runtime;

/// <summary>
/// Decides when federal agents show up, and sends them home again.
/// <para>
/// Evaluated once per in-game hour rather than per minute, because a federal visit is a chapter, not
/// a state. Every trigger is anchored to a number the shipped game already uses — a full day of held
/// heat, the weekly ATM deposit cap, the weekly order cycle — so the thresholds read as part of the
/// game's own economy rather than as a modder's opinion.
/// </para>
/// </summary>
internal sealed class FederalEvents
{
    private readonly PoliceConfig _config;
    private readonly HeatDirector _heat;
    private readonly EventScheduler _scheduler;

    private int _endsAtHour = -1;
    private string _targetKey = string.Empty;

    internal FederalEvents(PoliceConfig config, HeatDirector heat, EventScheduler scheduler)
    {
        _config = config;
        _heat = heat;
        _scheduler = scheduler;
    }

    internal bool IsActive => _endsAtHour >= 0;

    /// <summary>True while the current event is a stakeout rather than a pursuit.</summary>
    internal bool IsStakeout { get; private set; }

    /// <summary>The property being watched, or empty. Shown in the menu and the probe.</summary>
    internal string StakeoutProperty { get; private set; } = string.Empty;

    /// <summary>Per-minute upkeep: posted agents/officers drift, so they are walked back to their post.</summary>
    internal void MinutePass()
    {
        if (!IsStakeout)
            return;

        OfficerDeployment.HoldPosts();
        FederalAgents.HoldPosts();
    }


    /// <summary>In-game hours remaining, or 0 when nothing is happening. Shown in the menu and the probe.</summary>
    internal int HoursRemaining => _endsAtHour < 0 ? 0 : Math.Max(0, _endsAtHour - ElapsedHours());

    internal string TargetKey => _targetKey;

    internal void HourPass()
    {
        if (!_config.EnableFederalAgents.Value)
            return;

        if (IsActive)
        {
            var anyoneLeft = FederalAgents.LiveCount > 0;
            if (HoursRemaining <= 0 || !anyoneLeft)
                End(HoursRemaining <= 0 ? "the assignment expired" : "the team dispersed");

            return;
        }

        // Scheduler owns cadence when enabled; without it, keep the old threshold-immediate path for
        // owners who turn the randomisation off. Manual EventRegistry triggers never come through here.
        if (_config.EnableEventScheduler.Value)
        {
            if (!_scheduler.DueFederal(out var detail))
            {
                PoliceLog.Detail($"Federal scheduler: {detail}");
                return;
            }

            PoliceLog.Detail($"Federal scheduler: {detail}");
        }

        foreach (var player in GameBridge.Players())
        {
            if (player is null || GameBridge.IsArrested(player))
                continue;

            var record = _heat.RecordFor(player);
            var trigger = TriggerFor(record);
            if (trigger.Length == 0)
                continue;

            Begin(player, record, trigger);
            return;
        }
    }

    /// <summary>Manual start, for the menu action. Returns false with a reason the caller can show.</summary>
    internal bool ForceBegin(out string message)
    {
        if (!_config.EnableFederalAgents.Value)
        {
            message = "Federal agents are switched off in this module's settings (enable_federal_agents).";
            return false;
        }

        var player = GameBridge.LocalPlayer();
        if (player is null)
        {
            message = "There is no local player yet — load a save first.";
            return false;
        }

        if (IsActive)
        {
            message =
                $"A federal event is already running ({FederalAgents.LiveCount} designated officer(s), " +
                $"{HoursRemaining}h left).";
            return false;
        }

        var designated = Begin(player, _heat.RecordFor(player), "requested from the menu");
        if (designated > 0)
        {
            _scheduler.NoteManualFederal();
            message = IsStakeout
                ? $"{designated} shipped officer(s) are setting up a federal watch outside {StakeoutProperty}."
                : $"{designated} shipped officer(s) on federal assignment at your position.";

            return true;
        }

        message =
            $"Could not field a federal team. Deploy: {OfficerDeployment.LastReport}. " +
            $"Survey: {FederalAgents.Status.Reason}.";
        return false;
    }

    internal void End(string reason)
    {
        if (!IsActive)
            return;

        OfficerDeployment.ClearHeldPosts();
        FederalAgents.ReleaseAll();

        var record = _heat.Find(_targetKey);
        if (record is not null)
        {
            record.LastFederalEventDay = TimeManager.ElapsedDays;
            record.FederalEncounters++;
        }

        PoliceLog.Msg($"Federal event over ({reason}).");

        PoliceMessages.FederalEnded(reason);

        Clear();
    }

    /// <summary>Teardown: designations released, but no encounter is recorded — the module quitting is not an event.</summary>
    internal void Abort()
    {
        OfficerDeployment.ClearHeldPosts();
        FederalAgents.ReleaseAll();
        Clear();
    }

    private void Clear()
    {
        _endsAtHour = -1;
        _targetKey = string.Empty;
        IsStakeout = false;
        StakeoutProperty = string.Empty;
        FieldedOfficers = 0;
    }

    /// <summary>Shipped officers designated for the current federal event.</summary>
    internal int FieldedOfficers { get; private set; }

    private string TriggerFor(PlayerHeatRecord record)
    {
        if (record.LastFederalEventDay >= 0 &&
            TimeManager.ElapsedDays - record.LastFederalEventDay < _config.FederalCooldownDays.Value)
        {
            return string.Empty;
        }

        if (record.MinutesAtFederalHeat >= 1440)
            return $"heat held at {_config.FederalHeatThreshold.Value}+ for a full day";

        if (record.PoliceTakeTotal >= 10000f)
            return $"${record.PoliceTakeTotal:0} taken in penalties";

        if (record.ArrestsInLastWeek(TimeManager.ElapsedDays) >= 3)
            return "three arrests inside a week";

        return string.Empty;
    }

    /// <summary>
    /// Starts an event, choosing between a pursuit and a stakeout.
    /// <para>
    /// A stakeout is picked whenever the player is holed up in a property they own, because a pursuit
    /// against someone standing indoors resolves as officers milling about at the door and reads as
    /// broken AI. Posting them outside is the same fiction, legible, and costs nothing to run.
    /// </para>
    /// </summary>
    private int Begin(object player, PlayerHeatRecord record, string trigger)
    {
        var code = Members.Read(player, "PlayerCode", string.Empty);
        var stakeoutAt = StakeoutTarget(player, out var propertyName);
        var origin = stakeoutAt ?? SpawnOrigin(player);
        var wanted = Math.Max(1, _config.FederalAgentsPerEvent.Value);

        if (!PoliceForce.EnsureAnyLive(origin))
            PoliceLog.Warn("No live officer available before federal designate.");

        // Relocate + tag + buff shipped officers only — never clone or re-identify.
        var designated = FederalAgents.Designate(wanted, origin, code, stakeoutAt);

        if (designated <= 0)
        {
            PoliceLog.Warn(
                $"Federal trigger fired ({trigger}) but nobody could be designated. " +
                $"Deploy: {OfficerDeployment.LastReport}. Survey: {FederalAgents.Status.Reason}.");
            record.LastFederalEventDay = TimeManager.ElapsedDays;
            return 0;
        }

        _endsAtHour = ElapsedHours() + Math.Max(1, _config.FederalEventHours.Value);
        _targetKey = record.PlayerKey;
        IsStakeout = stakeoutAt is not null;
        StakeoutProperty = propertyName;
        FieldedOfficers = designated;
        record.MinutesAtFederalHeat = 0;

        PoliceLog.Msg(
            $"Federal event started ({(IsStakeout ? "stakeout on " + propertyName : "pursuit")}): {trigger}. " +
            $"{designated} shipped officer(s) designated for {_config.FederalEventHours.Value} in-game hour(s). " +
            OfficerDeployment.LastReport);

        PoliceMessages.FederalBegan(
            IsStakeout,
            propertyName,
            trigger,
            designated,
            _config.FederalEventHours.Value);

        return designated;
    }

    /// <summary>
    /// Where to post a stakeout, or null to run a pursuit instead. Only ever a property the player
    /// owns and is currently inside.
    /// </summary>
    private Vector3? StakeoutTarget(object player, out string propertyName)
    {
        propertyName = string.Empty;

        if (!_config.EnableStakeouts.Value)
            return null;

        var position = Components.TransformOf(player)?.position;
        if (position is null)
            return null;

        foreach (var property in Estate.Owned())
        {
            if (!Estate.Contains(property, position.Value))
                continue;

            propertyName = Estate.NameOf(property);
            return Estate.SpawnPointOf(property);
        }

        return null;
    }

    /// <summary>
    /// The nearest police station's own spawn point, so agents arrive somewhere plausible rather than
    /// materialising in front of the player. Falls back to a point behind the player if the map is
    /// not reachable.
    /// </summary>
    private static Vector3 SpawnOrigin(object player)
    {
        var position = Members.ReadPath(player, "transform") is Transform transform
            ? transform.position
            : Vector3.zero;

        var stationType = GameReflection.FindType(GameTypes.PoliceStation);
        if (stationType is not null &&
            GameReflection.TryInvoke(stationType, null, "GetClosestPoliceStation", new object?[] { position }, out var station, out _) &&
            GameReflection.IsPresent(station) &&
            Members.ReadPath(station, "SpawnPoint") is Transform spawnPoint)
        {
            return spawnPoint.position;
        }

        return position - (Vector3.forward * 12f);
    }

    private static int ElapsedHours() => (TimeManager.ElapsedDays * 24) + (TimeManager.CurrentTime / 100);
}

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

    private int _endsAtHour = -1;
    private string _targetKey = string.Empty;

    internal FederalEvents(PoliceConfig config, HeatDirector heat)
    {
        _config = config;
        _heat = heat;
    }

    internal bool IsActive => _endsAtHour >= 0;

    /// <summary>In-game hours remaining, or 0 when nothing is happening. Shown in the menu and the probe.</summary>
    internal int HoursRemaining => _endsAtHour < 0 ? 0 : Math.Max(0, _endsAtHour - ElapsedHours());

    internal string TargetKey => _targetKey;

    internal void HourPass()
    {
        if (!_config.EnableFederalAgents.Value)
            return;

        if (IsActive)
        {
            if (HoursRemaining <= 0 || FederalAgents.LiveCount == 0)
                End("the assignment expired");

            return;
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
        var player = GameBridge.LocalPlayer();
        if (player is null)
        {
            message = "No local player yet — load a save first.";
            return false;
        }

        if (IsActive)
        {
            message = $"A federal event is already running ({FederalAgents.LiveCount} agent(s), {HoursRemaining}h left).";
            return false;
        }

        var spawned = Begin(player, _heat.RecordFor(player), "requested from the menu");
        message = spawned > 0
            ? $"{spawned} federal agent(s) dispatched to your position."
            : $"Could not dispatch: {FederalAgents.Status.Reason}.";

        return spawned > 0;
    }

    internal void End(string reason)
    {
        if (!IsActive)
            return;

        FederalAgents.DespawnAll();

        var record = _heat.Find(_targetKey);
        if (record is not null)
        {
            record.LastFederalEventDay = TimeManager.ElapsedDays;
            record.FederalEncounters++;
        }

        PoliceLog.Msg($"Federal event over ({reason}).");

        if (_config.ShowHud.Value)
            GameBridge.Notify("They pulled out", "The plain-clothes team has left the area. For now.", 7f);

        _endsAtHour = -1;
        _targetKey = string.Empty;
    }

    /// <summary>Teardown: agents go, but no encounter is recorded — the module quitting is not an event.</summary>
    internal void Abort()
    {
        FederalAgents.DespawnAll();
        _endsAtHour = -1;
        _targetKey = string.Empty;
    }

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

    private int Begin(object player, PlayerHeatRecord record, string trigger)
    {
        var origin = SpawnOrigin(player);
        var code = Members.Read(player, "PlayerCode", string.Empty);
        var spawned = FederalAgents.Spawn(_config.FederalAgentsPerEvent.Value, origin, code);

        if (spawned == 0)
        {
            // Not viable on this build. Log it once per attempt and let the rest of the mod carry on;
            // a failed federal spawn must never look like a broken save.
            PoliceLog.Warn($"Federal trigger fired ({trigger}) but no agents could be spawned: {FederalAgents.Status.Reason}.");
            record.LastFederalEventDay = TimeManager.ElapsedDays;
            return 0;
        }

        _endsAtHour = ElapsedHours() + Math.Max(1, _config.FederalEventHours.Value);
        _targetKey = record.PlayerKey;
        record.MinutesAtFederalHeat = 0;

        PoliceLog.Msg($"Federal event started: {trigger}. {spawned} agent(s) for {_config.FederalEventHours.Value} in-game hour(s).");

        if (_config.ShowHud.Value)
            GameBridge.Notify("Not local police", "Two people who are not from around here just started asking about you.", 8f);

        return spawned;
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

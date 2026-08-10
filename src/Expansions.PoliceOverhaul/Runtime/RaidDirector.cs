using Expansions.Core.Actions;
using Expansions.Core.Diagnostics;
using Expansions.PoliceOverhaul.State;
using S1API.GameTime;
using UnityEngine;

namespace Expansions.PoliceOverhaul.Runtime;

/// <summary>
/// Property raids: the consequence that reaches the one thing an arrest never touches — the stash you
/// left at home.
/// <para>
/// A raid is deliberately a two-stage event with a warning in the middle. The player is told a
/// property is being watched, given a configurable window to get there, and only loses product if
/// they are somewhere else when the window closes. That turns it from a tax into a decision, and it
/// is why the warning names the property.
/// </para>
/// <para>
/// It never empties a container. <c>ClearContents()</c> exists and is exactly the wrong call: taking
/// legitimate items alongside the product reads as a bug, and a total wipe reads as a bug twice over.
/// A fraction is taken, biggest-value stacks first, and the notification says what went.
/// </para>
/// </summary>
internal sealed class RaidDirector
{
    private readonly PoliceConfig _config;
    private readonly HeatDirector _heat;
    private readonly OutlawState _outlaw;
    private readonly EventScheduler _scheduler;

    private object? _target;
    private string _targetName = string.Empty;
    private int _executeAtMinute = -1;
    private string _reason = string.Empty;
    private string _lastScheduleFailure = string.Empty;

    internal RaidDirector(PoliceConfig config, HeatDirector heat, OutlawState outlaw, EventScheduler scheduler)
    {
        _config = config;
        _heat = heat;
        _outlaw = outlaw;
        _scheduler = scheduler;
    }

    internal bool IsPending => _executeAtMinute >= 0;

    internal string PendingProperty => _targetName;

    internal int MinutesUntilRaid => _executeAtMinute < 0 ? 0 : Math.Max(0, _executeAtMinute - GameClock.Minutes());

    /// <summary>Lifetime count for the probe and the menu read-out.</summary>
    internal int RaidsExecuted { get; private set; }

    internal float ValueSeized { get; private set; }

    /// <summary>Last candidate enumeration for probes (codes + why kept/dropped).</summary>
    internal string LastCandidateReport { get; private set; } = "not enumerated yet";

    /// <summary>
    /// Hourly eligibility check. High heat is enough; outlaw status always qualifies and shortens
    /// the scheduler interval.
    /// </summary>
    internal void HourPass()
    {
        if (!_config.EnablePropertyRaids.Value || IsPending)
            return;

        var player = GameBridge.LocalPlayer();
        if (player is null || GameBridge.IsArrested(player))
            return;

        var record = _heat.RecordFor(player);
        var outlawed = record.Outlaw != OutlawTier.Clean && _outlaw.IsOutlawed(player);
        var heatQualified = record.Heat >= _config.RaidHeatThreshold.Value;

        if (!outlawed && !heatQualified)
            return;

        var intervalScale = outlawed ? Math.Clamp(_config.RaidOutlawIntervalScale.Value, 0.15f, 2f) : 1f;

        if (_config.EnableEventScheduler.Value)
        {
            if (!_scheduler.DueRaid(out var detail, intervalScale))
            {
                PoliceLog.Detail($"Raid scheduler: {detail}");
                return;
            }

            PoliceLog.Detail($"Raid scheduler: {detail}");
        }

        if (record.LastRaidDay >= 0 && TimeManager.ElapsedDays - record.LastRaidDay < _config.RaidCooldownDays.Value)
            return;

        var reason = record.Outlaw == OutlawTier.Hunted
            ? "you are Hunted and they know where you sleep"
            : outlawed
                ? "your record put a warrant on your address"
                : $"heat {record.Heat:0} cleared the raid threshold ({_config.RaidHeatThreshold.Value})";

        Schedule(player, record, reason);
    }

    /// <summary>Per-minute countdown. Cheap enough to sit on the law controller's own tick.</summary>
    internal void MinutePass()
    {
        if (!IsPending)
            return;

        if (!_config.EnablePropertyRaids.Value)
        {
            Cancel("property raids were switched off");
            return;
        }

        if (!GameReflection.IsPresent(_target) || !Estate.IsValidOwned(_target))
        {
            Cancel("the property is no longer loaded or owned");
            return;
        }

        if (_config.IsRaidExcluded(Estate.CodeOf(_target)))
        {
            Cancel($"'{Estate.CodeOf(_target)}' is on raid_excluded_properties");
            return;
        }

        if (GameClock.Minutes() < _executeAtMinute)
            return;

        Execute();
    }

    /// <summary>
    /// Manual trigger for the menu action and the event hotkey. Returns false with a reason the caller
    /// shows verbatim, because "nothing happened" is the one outcome a trigger must never produce
    /// silently.
    /// </summary>
    internal bool Force(bool immediate, out string message)
    {
        if (!_config.EnablePropertyRaids.Value)
        {
            message = "Property raids are switched off in this module's settings (enable_property_raids).";
            return false;
        }

        if (IsPending && immediate)
        {
            Execute();
            _scheduler.NoteManualRaid();
            message = $"The raid on {_targetName} was brought forward and has just happened.";
            return true;
        }

        if (IsPending)
        {
            message = $"A raid on {_targetName} is already inbound in {GameClock.Describe(MinutesUntilRaid)}.";
            return false;
        }

        var player = GameBridge.LocalPlayer();
        if (player is null)
        {
            message = "There is no local player yet — load a save first.";
            return false;
        }

        // Manual triggers must always pick a property you own — including the one you are standing
        // in. Being home is the defence at execute time (they drive past), not a reason for the
        // button to do nothing.
        if (!Schedule(
                player,
                _heat.RecordFor(player),
                "requested from the menu",
                warn: !immediate,
                allowOccupied: true,
                preferCurrent: true))
        {
            message = _lastScheduleFailure.Length > 0
                ? _lastScheduleFailure
                : "You do not own a property yet, so there is nothing to raid. Buy one and try again.";
            return false;
        }

        var outlawed = _heat.RecordFor(player).Outlaw != OutlawTier.Clean;
        _scheduler.NoteManualRaid(outlawed ? Math.Clamp(_config.RaidOutlawIntervalScale.Value, 0.15f, 2f) : 1f);

        var propertyName = _targetName;
        var standingIn = player is not null && _target is not null &&
                         Estate.Contains(_target, Components.TransformOf(player)?.position ?? Vector3.zero);

        if (immediate)
        {
            Execute();
            message = standingIn
                ? $"Raid team reached {propertyName} while you were there and drove on. Nothing taken."
                : $"Raided {propertyName} immediately.";
            return true;
        }

        message = standingIn
            ? $"A raid on {propertyName} is inbound in {GameClock.Describe(MinutesUntilRaid)}. " +
              "You are already there — stay put and they drive past; leave and they take product."
            : $"A raid on {propertyName} is inbound in {GameClock.Describe(MinutesUntilRaid)}. Get there or lose the stash.";
        return true;
    }

    /// <summary>Drops a pending raid without executing it. Used by teardown and by clearing outlaw status.</summary>
    internal void Cancel(string reason)
    {
        if (!IsPending)
            return;

        PoliceLog.Msg($"Raid on {_targetName} called off ({reason}).");
        PoliceMessages.RaidCalledOff(_targetName, reason);
        Forget();
    }

    /// <summary>Scene teardown: the world is gone, so drop the references without touching them.</summary>
    internal void Forget()
    {
        _target = null;
        _targetName = string.Empty;
        _executeAtMinute = -1;
        _reason = string.Empty;
    }

    // ── Internals ─────────────────────────────────────────────────────────────────────────────

    private bool Schedule(
        object player,
        PlayerHeatRecord record,
        string reason,
        bool warn = true,
        bool allowOccupied = false,
        bool preferCurrent = false)
    {
        _lastScheduleFailure = string.Empty;
        var target = PickTarget(player, allowOccupied, preferCurrent);
        if (target is null)
            return false;

        _target = target;
        _targetName = Estate.NameOf(target);
        _reason = reason;
        _executeAtMinute = GameClock.Minutes() + Math.Max(1, _config.RaidDelayMinutes.Value);

        PoliceLog.Msg($"Raid scheduled on '{_targetName}' in {_config.RaidDelayMinutes.Value} minute(s): {reason}.");
        if (IsNatural(reason))
            ActionLog.Note($"Natural raid scheduled: {_targetName}, due in {GameClock.Describe(MinutesUntilRaid)} ({reason}).");

        // Physical inbound team at warn time — a text with nobody walking up is not a raid.
        var wanted = Math.Max(1, _config.EventOfficerCount.Value);
        var onScene = PoliceForce.EnsureAt(Estate.SpawnPointOf(target), wanted, player, beginAsSighted: false);
        PoliceLog.Msg(
            $"Raid warn '{_targetName}': {onScene}/{wanted} officer(s) within " +
            $"{OfficerDeployment.SceneRadiusMetres:0}m — {OfficerDeployment.LastReport}");

        if (IsNatural(reason))
        {
            ActionLog.Note(
                $"Natural raid deployment: {_targetName} has {onScene}/{wanted} visibly present officer(s) " +
                $"within {OfficerDeployment.SceneRadiusMetres:0}m.");
        }

        if (onScene <= 0)
        {
            var failedProperty = _targetName;
            _lastScheduleFailure =
                $"Raid on {failedProperty} was not scheduled because no visible officers reached the property. " +
                PoliceForce.LastShortfall;
            PoliceLog.Warn(_lastScheduleFailure);
            if (IsNatural(reason))
                ActionLog.Fail($"Natural raid aborted before warning: {_lastScheduleFailure}");
            Forget();
            return false;
        }

        // Urgent toast + phone text naming the property. Always attempt delivery.
        if (warn)
            PoliceMessages.RaidWarning(_targetName, GameClock.Describe(MinutesUntilRaid), reason);

        record.LastRaidWarnedDay = TimeManager.ElapsedDays;
        return true;
    }

    /// <summary>
    /// Automatic raids prefer an owned property the player is away from. Manual menu/event triggers
    /// first choose the owned property physically containing the player, then fall back to the normal
    /// selection only when the player is not standing in one.
    /// </summary>
    private object? PickTarget(object player, bool allowOccupied, bool preferCurrent)
    {
        var owned = RaidCandidates();
        if (owned.Count == 0)
        {
            _lastScheduleFailure =
                "Raid skipped — no valid owned property left after exclusions " +
                $"(raid_excluded_properties='{_config.RaidExcludedProperties.Value}') and validity checks. " +
                LastCandidateReport;
            PoliceLog.Msg(_lastScheduleFailure);
            return null;
        }

        var position = Components.TransformOf(player)?.position ?? Vector3.zero;

        if (preferCurrent)
        {
            foreach (var property in owned)
            {
                if (Estate.Contains(property, position))
                    return property;
            }
        }

        var recent = Members.ReadPath(player, "LastVisitedProperty");
        if (GameReflection.IsPresent(recent) && recent is not null && IsRaidable(recent, position, owned, requireAbsent: true))
            return recent;

        foreach (var property in owned)
        {
            if (IsRaidable(property, position, owned, requireAbsent: true))
                return property;
        }

        if (!allowOccupied)
            return null;

        if (GameReflection.IsPresent(recent) && recent is not null && IsRaidable(recent, position, owned, requireAbsent: false))
            return recent;

        return owned[0];
    }

    private static bool IsRaidable(object property, Vector3 playerPosition, IReadOnlyList<object> owned, bool requireAbsent)
    {
        var pointer = Components.PointerOf(property);
        var isOwned = false;
        foreach (var candidate in owned)
        {
            if (Components.PointerOf(candidate) == pointer)
            {
                isOwned = true;
                break;
            }
        }

        if (!isOwned)
            return false;

        return !requireAbsent || !Estate.Contains(property, playerPosition);
    }

    /// <summary>
    /// Owned properties that still exist and are not on <c>raid_excluded_properties</c>.
    /// Updates <see cref="LastCandidateReport"/> every call.
    /// </summary>
    internal IReadOnlyList<object> RaidCandidates()
    {
        var kept = new List<object>();
        var notes = new List<string>();

        foreach (var property in Estate.Owned())
        {
            var code = Estate.CodeOf(property);
            var label = code.Length > 0 ? code : Estate.NameOf(property);

            if (!Estate.IsValidOwned(property))
            {
                notes.Add($"{label}=skip(invalid/unowned/destroyed)");
                continue;
            }

            if (_config.IsRaidExcluded(code))
            {
                notes.Add($"{label}=skip(excluded)");
                continue;
            }

            kept.Add(property);
            notes.Add($"{label}=candidate");
        }

        LastCandidateReport =
            notes.Count == 0
                ? "no owned properties"
                : string.Join("; ", notes) +
                  $" | kept {kept.Count}, excluded cfg='{_config.RaidExcludedProperties.Value}'";

        return kept;
    }

    private void Execute()
    {
        var target = _target;
        var name = _targetName;
        var reason = _reason;
        Forget();

        if (target is null)
            return;

        var player = GameBridge.LocalPlayer();
        var position = Components.TransformOf(player)?.position ?? Vector3.zero;
        var propertyPoint = Estate.SpawnPointOf(target);

        // Relocate shipped officers to the property. No bodies within range ⇒ no raid — never a
        // silent stash wipe dressed up as policing.
        var wanted = Math.Max(1, _config.EventOfficerCount.Value);
        var onScene = PoliceForce.EnsureAt(propertyPoint, wanted, player, beginAsSighted: false);
        var presence = OfficerDeployment.CountWithin(propertyPoint, OfficerDeployment.SceneRadiusMetres);
        var visible = PoliceForce.VisibleWithin(propertyPoint, OfficerDeployment.SceneRadiusMetres);
        PoliceLog.Msg(
            $"Raid execute '{name}': {visible}/{wanted} visible officer(s), {presence.Count} physically within " +
            $"{OfficerDeployment.SceneRadiusMetres:0}m (nearest " +
            $"{(presence.Count > 0 ? presence.NearestMetres.ToString("0.0") : "n/a")}m). " +
            OfficerDeployment.LastReport);
        if (IsNatural(reason))
            ActionLog.Note($"Natural raid executing: {name} has {visible}/{wanted} visible officer(s) on scene.");

        if (visible <= 0)
        {
            PoliceLog.Warn(
                $"Raid on '{name}' aborted — zero visible officers within {OfficerDeployment.SceneRadiusMetres:0}m. " +
                "Nothing seized. " + PoliceForce.LastShortfall);
            if (IsNatural(reason))
                ActionLog.Fail($"Natural raid on {name} aborted: zero visible officers reached the property; nothing seized.");
            PoliceMessages.RaidCalledOff(name, "the raid team never made it to the property");
            return;
        }

        if (player is not null && Estate.Contains(target, position))
        {
            PoliceLog.Msg(
                $"Raid on '{name}' aborted: the player was there with {presence.Count} officer(s) on scene.");

            var present = _heat.RecordFor(player);
            present.LastRaidDay = TimeManager.ElapsedDays;
            present.RaidsAvoided++;

            PoliceMessages.RaidResolved(name, present: true, stacks: 0, value: 0f, haul: string.Empty);
            if (IsNatural(reason))
                ActionLog.Ok($"Natural raid on {name} resolved: you were present, so nothing was seized.");

            TutorialSignalRaid();
            return;
        }

        var haul = Seize(target, out var stacks, out var value);

        var record = _heat.RecordFor(player);
        record.LastRaidDay = TimeManager.ElapsedDays;
        record.RaidsSuffered++;

        RaidsExecuted++;
        ValueSeized += value;

        if (value > 0f)
            _heat.AddPoliceTake(player, value);

        PoliceLog.Msg(stacks > 0
            ? $"Raid on '{name}' ({reason}): {stacks} stack(s) seized, worth about ${value:0}. " +
              $"{presence.Count} officer(s) on scene. {haul}"
            : $"Raid on '{name}' ({reason}): nothing worth taking was in the containers. " +
              $"{presence.Count} officer(s) on scene.");

        PoliceMessages.RaidResolved(name, present: false, stacks, value, haul);
        if (IsNatural(reason))
        {
            ActionLog.Ok(
                stacks > 0
                    ? $"Natural raid on {name} completed: {stacks} stack(s), about ${value:0}, seized. {haul}"
                    : $"Natural raid on {name} completed: officers found nothing worth taking.");
        }

        TutorialSignalRaid();
        _ = onScene;
    }

    /// <summary>
    /// Takes the configured fraction of the contraband in every container on the property, biggest
    /// stacks first, and returns a human-readable list of what went.
    /// </summary>
    private string Seize(object property, out int stacks, out float value)
    {
        stacks = 0;
        value = 0f;

        var fraction = Math.Clamp(_config.RaidConfiscationFraction.Value, 0.05f, 1f);
        var taken = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var storage in Estate.StoragesIn(property))
        {
            var contraband = Estate.ContrabandIn(storage);
            if (contraband.Count == 0)
                continue;

            // Round up, so a property holding a single stack still loses something. A raid that
            // provably reached the containers and took nothing is worse than no raid at all.
            var stacksToTake = Math.Max(1, (int)Math.Ceiling(contraband.Count * fraction));

            for (var i = 0; i < contraband.Count && i < stacksToTake; i++)
            {
                var entry = contraband[i];
                if (!Remove(storage, entry))
                    continue;

                stacks++;
                value += entry.Value;
                taken[entry.Name] = taken.TryGetValue(entry.Name, out var already) ? already + entry.Quantity : entry.Quantity;
            }

            Members.Invoke(storage, "ContentsChanged");
        }

        if (taken.Count == 0)
            return "nothing";

        return string.Join(", ", taken.OrderByDescending(pair => pair.Value).Take(4).Select(pair => $"{pair.Value}x {pair.Key}"));
    }

    /// <summary>
    /// <c>conn: null</c> is the server-side path; <c>SetStoredInstance</c> is a ServerRpc whose logic
    /// body runs locally when we are the server, which the host gate has already guaranteed.
    /// </summary>
    private static bool Remove(object storage, Estate.Contraband entry) =>
        Members.Invoke(storage, "SetStoredInstance", null, entry.SlotIndex, null);

    private static bool IsNatural(string reason) =>
        !reason.Contains("requested from the menu", StringComparison.OrdinalIgnoreCase);

    private static void TutorialSignalRaid() =>
        Expansions.Core.Tutorial.TutorialSignals.Raise(Tutorial.PoliceChapter.RaidSignal);
}

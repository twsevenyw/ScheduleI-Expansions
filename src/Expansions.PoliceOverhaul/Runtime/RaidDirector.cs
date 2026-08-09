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

    /// <summary>
    /// Hourly eligibility check. Raids are outlaw-only on purpose: heat alone already fills the street
    /// with officers, and taking someone's stash for a curfew violation would be indefensible.
    /// </summary>
    internal void HourPass()
    {
        if (!_config.EnablePropertyRaids.Value || IsPending)
            return;

        if (_config.EnableEventScheduler.Value)
        {
            if (!_scheduler.DueRaid(out var detail))
            {
                PoliceLog.Detail($"Raid scheduler: {detail}");
                return;
            }

            PoliceLog.Detail($"Raid scheduler: {detail}");
        }

        var player = GameBridge.LocalPlayer();
        if (player is null || GameBridge.IsArrested(player))
            return;

        var record = _heat.RecordFor(player);
        if (record.Outlaw == OutlawTier.Clean || !_outlaw.IsOutlawed(player))
            return;

        if (record.LastRaidDay >= 0 && TimeManager.ElapsedDays - record.LastRaidDay < _config.RaidCooldownDays.Value)
            return;

        Schedule(player, record, record.Outlaw == OutlawTier.Hunted
            ? "you are Hunted and they know where you sleep"
            : "your record put a warrant on your address");
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

        if (!GameReflection.IsPresent(_target))
        {
            Cancel("the property is no longer loaded");
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

        if (!Schedule(player, _heat.RecordFor(player), "requested from the menu", warn: !immediate))
        {
            message = Owned().Count == 0
                ? "You do not own a property yet, so there is nothing to raid. Buy one and try again."
                : "Every property you own has you standing in it. A raid only happens when you are somewhere else.";
            return false;
        }

        _scheduler.NoteManualRaid();

        if (immediate)
        {
            Execute();
            message = $"Raided {_targetName} immediately.";
            return true;
        }

        message = $"A raid on {_targetName} is inbound in {GameClock.Describe(MinutesUntilRaid)}. Get there or lose the stash.";
        return true;
    }

    /// <summary>Drops a pending raid without executing it. Used by teardown and by clearing outlaw status.</summary>
    internal void Cancel(string reason)
    {
        if (!IsPending)
            return;

        PoliceLog.Msg($"Raid on {_targetName} called off ({reason}).");

        if (_config.ShowHud.Value)
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

    private bool Schedule(object player, PlayerHeatRecord record, string reason, bool warn = true)
    {
        var target = PickTarget(player);
        if (target is null)
            return false;

        _target = target;
        _targetName = Estate.NameOf(target);
        _reason = reason;
        _executeAtMinute = GameClock.Minutes() + Math.Max(1, _config.RaidDelayMinutes.Value);

        PoliceLog.Msg($"Raid scheduled on '{_targetName}' in {_config.RaidDelayMinutes.Value} minute(s): {reason}.");

        // Toast on purpose: the player has a short window to get home; a phone text is too slow.
        if (warn && _config.ShowHud.Value)
            PoliceMessages.RaidWarning(_targetName, GameClock.Describe(MinutesUntilRaid), reason);

        record.LastRaidWarnedDay = TimeManager.ElapsedDays;
        return true;
    }

    /// <summary>
    /// The property they would actually hit: one the player owns and is not currently standing in,
    /// preferring the one they were last seen at. Being inside is the whole defence, so it is checked
    /// again at execution time and not merely here.
    /// </summary>
    private object? PickTarget(object player)
    {
        var owned = Owned();
        if (owned.Count == 0)
            return null;

        var position = Components.TransformOf(player)?.position ?? Vector3.zero;

        var recent = Members.ReadPath(player, "LastVisitedProperty");
        if (GameReflection.IsPresent(recent) && recent is not null && IsRaidable(recent, position, owned))
            return recent;

        foreach (var property in owned)
        {
            if (IsRaidable(property, position, owned))
                return property;
        }

        return null;
    }

    private static bool IsRaidable(object property, Vector3 playerPosition, IReadOnlyList<object> owned)
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

        return isOwned && !Estate.Contains(property, playerPosition);
    }

    private static IReadOnlyList<object> Owned() => Estate.Owned();

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

        // A raid with no officers behind it is a notification, and the player reads that as the mod
        // being broken. Officers are guaranteed before either branch runs, because the "they drove
        // past" outcome is the one that most needs something visible to have happened.
        var wanted = Math.Max(1, _config.EventOfficerCount.Value);
        var onScene = PoliceForce.EnsureAt(Estate.SpawnPointOf(target), wanted, player);
        PoliceLog.Detail($"Raid on '{name}' has {onScene}/{wanted} officer(s) on scene.");

        if (player is not null && Estate.Contains(target, position))
        {
            PoliceLog.Msg($"Raid on '{name}' aborted: the player was there.");

            var present = _heat.RecordFor(player);
            present.LastRaidDay = TimeManager.ElapsedDays;
            present.RaidsAvoided++;

            if (_config.ShowHud.Value)
                PoliceMessages.RaidResolved(name, present: true, stacks: 0, value: 0f, haul: string.Empty);

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
            ? $"Raid on '{name}' ({reason}): {stacks} stack(s) seized, worth about ${value:0}. {haul}"
            : $"Raid on '{name}' ({reason}): nothing worth taking was in the containers.");

        if (_config.ShowHud.Value)
            PoliceMessages.RaidResolved(name, present: false, stacks, value, haul);

        TutorialSignalRaid();
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

    private static void TutorialSignalRaid() =>
        Expansions.Core.Tutorial.TutorialSignals.Raise(Tutorial.PoliceChapter.RaidSignal);
}

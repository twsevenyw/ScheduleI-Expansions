using Expansions.PoliceOverhaul.State;
using S1API.GameTime;

namespace Expansions.PoliceOverhaul.Runtime;

/// <summary>
/// Randomised, save-deterministic cadence for federal visits and property raids.
/// <para>
/// Eligibility (heat, outlaw, cooldowns) still lives on the directors; this class only answers
/// <em>when</em> an eligible event is allowed to roll. Manual <c>EventRegistry</c> triggers bypass it.
/// </para>
/// </summary>
internal sealed class EventScheduler
{
    private readonly PoliceConfig _config;

    private uint _seed;
    private int _rolls;
    private int _readyAtMinute = -1;
    private int _nextFederalCheckMinute = -1;
    private int _nextRaidCheckMinute = -1;
    private string _lastDecision = "not started";

    internal EventScheduler(PoliceConfig config) => _config = config;

    internal uint Seed => _seed;

    internal int Rolls => _rolls;

    internal int NextFederalCheckMinute => _nextFederalCheckMinute;

    internal int NextRaidCheckMinute => _nextRaidCheckMinute;

    internal string LastDecision => _lastDecision;

    /// <summary>Minutes until the next federal eligibility roll, or -1 when none is queued.</summary>
    internal int MinutesUntilFederalCheck =>
        _nextFederalCheckMinute < 0 ? -1 : Math.Max(0, _nextFederalCheckMinute - GameClock.Minutes());

    internal int MinutesUntilRaidCheck =>
        _nextRaidCheckMinute < 0 ? -1 : Math.Max(0, _nextRaidCheckMinute - GameClock.Minutes());

    /// <summary>Called once the world is live so the first roll cannot fire during load.</summary>
    internal void Arm(PoliceSaveState? save)
    {
        EnsureSeed(save);
        var now = GameClock.Minutes();
        var grace = Math.Max(0, _config.EventReadyGraceMinutes.Value);
        _readyAtMinute = now + grace;

        if (_nextFederalCheckMinute < _readyAtMinute)
            _nextFederalCheckMinute = ScheduleNext(now, _config.FederalIntervalHoursMin.Value, _config.FederalIntervalHoursMax.Value);

        if (_nextRaidCheckMinute < _readyAtMinute)
            _nextRaidCheckMinute = ScheduleNext(now, _config.RaidIntervalHoursMin.Value, _config.RaidIntervalHoursMax.Value);

        Persist(save);
        _lastDecision = $"armed; federal check in {GameClock.Describe(MinutesUntilFederalCheck)}, " +
                        $"raid check in {GameClock.Describe(MinutesUntilRaidCheck)} (grace {grace} min)";
        PoliceLog.Msg($"Event scheduler armed (seed {_seed}, rolls {_rolls}). {_lastDecision}");
    }

    internal void Forget()
    {
        _readyAtMinute = -1;
        _lastDecision = "forgotten with the scene";
    }

    /// <summary>True when the world can accept a scheduled event the player can react to.</summary>
    internal bool WorldAcceptsEvents(out string reason)
    {
        if (!_config.EnableEventScheduler.Value)
        {
            reason = "event scheduler switched off (enable_event_scheduler) — directors may still fire on their own thresholds";
            return false;
        }

        if (!PoliceRuntime.IsLive)
        {
            reason = "module not wired";
            return false;
        }

        if (!HostGate.IsAuthority)
        {
            reason = "not authoritative";
            return false;
        }

        if (GameBridge.LocalPlayer() is null)
        {
            reason = "no local player";
            return false;
        }

        if (_readyAtMinute >= 0 && GameClock.Minutes() < _readyAtMinute)
        {
            reason = $"still inside post-load grace ({_readyAtMinute - GameClock.Minutes()} min left)";
            return false;
        }

        if (GameBridge.IsArrested(GameBridge.LocalPlayer()))
        {
            reason = "player is under arrest";
            return false;
        }

        reason = "ready";
        return true;
    }

    /// <summary>
    /// Whether a scheduled federal roll is due. When due, the next check is always re-queued so a
    /// refused attempt (not eligible yet) does not spin every hour.
    /// </summary>
    internal bool DueFederal(out string detail)
    {
        if (!WorldAcceptsEvents(out detail))
            return false;

        if (!_config.EnableFederalAgents.Value)
        {
            detail = "federal agents disabled";
            return false;
        }

        var now = GameClock.Minutes();
        if (_nextFederalCheckMinute < 0)
            _nextFederalCheckMinute = ScheduleNext(now, _config.FederalIntervalHoursMin.Value, _config.FederalIntervalHoursMax.Value);

        if (now < _nextFederalCheckMinute)
        {
            detail = $"next federal check in {GameClock.Describe(_nextFederalCheckMinute - now)}";
            return false;
        }

        _nextFederalCheckMinute = ScheduleNext(now, _config.FederalIntervalHoursMin.Value, _config.FederalIntervalHoursMax.Value);
        Persist(PoliceSaveState.Live);
        detail = $"federal slot opened; next check in {GameClock.Describe(_nextFederalCheckMinute - now)}";
        _lastDecision = detail;
        return true;
    }

    /// <param name="intervalScale">
    /// Multiplier on the configured raid interval (e.g. 0.5 while outlawed). Applied to the
    /// <em>next</em> slot after this one opens.
    /// </param>
    internal bool DueRaid(out string detail, float intervalScale = 1f)
    {
        if (!WorldAcceptsEvents(out detail))
            return false;

        if (!_config.EnablePropertyRaids.Value)
        {
            detail = "property raids disabled";
            return false;
        }

        var now = GameClock.Minutes();
        var (min, max) = ScaledRaidHours(intervalScale);

        if (_nextRaidCheckMinute < 0)
            _nextRaidCheckMinute = ScheduleNext(now, min, max);

        if (now < _nextRaidCheckMinute)
        {
            detail = $"next raid check in {GameClock.Describe(_nextRaidCheckMinute - now)}";
            return false;
        }

        _nextRaidCheckMinute = ScheduleNext(now, min, max);
        Persist(PoliceSaveState.Live);
        detail = $"raid slot opened (scale x{intervalScale:0.##}); next check in {GameClock.Describe(_nextRaidCheckMinute - now)}";
        _lastDecision = detail;
        return true;
    }

    /// <summary>Manual triggers still bump the cadence so a test fire does not double up with a scheduled one.</summary>
    internal void NoteManualFederal()
    {
        var now = GameClock.Minutes();
        _nextFederalCheckMinute = ScheduleNext(now, _config.FederalIntervalHoursMin.Value, _config.FederalIntervalHoursMax.Value);
        Persist(PoliceSaveState.Live);
        _lastDecision = "manual federal trigger; schedule pushed";
    }

    internal void NoteManualRaid(float intervalScale = 1f)
    {
        var now = GameClock.Minutes();
        var (min, max) = ScaledRaidHours(intervalScale);
        _nextRaidCheckMinute = ScheduleNext(now, min, max);
        Persist(PoliceSaveState.Live);
        _lastDecision = $"manual raid trigger; schedule pushed (scale x{intervalScale:0.##})";
    }

    private (int Min, int Max) ScaledRaidHours(float intervalScale)
    {
        var scale = Math.Clamp(intervalScale, 0.15f, 2f);
        var min = Math.Max(1, (int)Math.Round(_config.RaidIntervalHoursMin.Value * scale));
        var max = Math.Max(min, (int)Math.Round(_config.RaidIntervalHoursMax.Value * scale));
        return (min, max);
    }

    private void EnsureSeed(PoliceSaveState? save)
    {
        if (_seed != 0)
            return;

        if (save is not null && save.EventSeed != 0)
        {
            _seed = unchecked((uint)save.EventSeed);
            _rolls = save.EventRolls;
            _nextFederalCheckMinute = save.NextFederalCheckMinute;
            _nextRaidCheckMinute = save.NextRaidCheckMinute;
            return;
        }

        var key = GameBridge.KeyFor(GameBridge.LocalPlayer());
        _seed = PoliceRng.SeedFor(key, TimeManager.ElapsedDays, 0x504F4C49); // "POLI"
        _rolls = 0;
        Persist(save);
    }

    private int ScheduleNext(int nowMinute, int hoursMin, int hoursMax)
    {
        var min = Math.Max(1, hoursMin);
        var max = Math.Max(min, hoursMax);
        var rng = new PoliceRng(PoliceRng.SeedFor("slot", (int)_seed, _rolls, nowMinute));
        _rolls++;
        var hours = rng.RangeInclusive(min, max);
        return nowMinute + (hours * 60);
    }

    private void Persist(PoliceSaveState? save)
    {
        if (save is null)
            return;

        save.EventSeed = unchecked((int)_seed);
        save.EventRolls = _rolls;
        save.NextFederalCheckMinute = _nextFederalCheckMinute;
        save.NextRaidCheckMinute = _nextRaidCheckMinute;
    }
}

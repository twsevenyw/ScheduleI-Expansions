using Expansions.Core.Diagnostics;
using Expansions.PoliceOverhaul.State;
using S1API.GameTime;

namespace Expansions.PoliceOverhaul.Runtime;

/// <summary>
/// Owns heat: what raises it, what sheds it, and what the world does about it.
/// <para>
/// Heat is not the shipped wanted level. Sleeping wipes the wanted level outright, which is exactly
/// why the ballot asked for police improvements — there is no memory in the system. Heat is a
/// separate 0-100 scalar that persists across sleep, save and reload, and it is deliberately kept out
/// of <c>LawController.internalLawIntensity</c> so <c>Law.json</c> stays byte-identical and disabling
/// the module leaves no trace in the save.
/// </para>
/// </summary>
internal sealed class HeatDirector
{
    private readonly PoliceConfig _config;
    private readonly LawScheduleTuner _schedule;
    private readonly DetectionTuner _detection;
    private readonly OutlawState _outlaw;
    private readonly Dictionary<string, PlayerHeatRecord> _records = new(StringComparer.Ordinal);

    /// <summary>The vanilla law intensity we are adding to. Re-derived whenever anything else moves it.</summary>
    private int _baselineIntensity = 1;

    private int? _intensityOnEnable;
    private int _lastWrittenIntensity = int.MinValue;
    private HeatTier _lastAnnouncedTier = (HeatTier)(-1);

    internal HeatDirector(PoliceConfig config, LawScheduleTuner schedule, DetectionTuner detection, OutlawState outlaw)
    {
        _config = config;
        _schedule = schedule;
        _detection = detection;
        _outlaw = outlaw;
    }

    /// <summary>Reads back the number the world is currently being driven to, for probes and the menu.</summary>
    internal int CurrentIntensity { get; private set; }

    internal int BaselineIntensity => _baselineIntensity;

    /// <summary>True once the player has slept through a night and still had heat on the other side.</summary>
    internal bool SleptWithHeatRemaining { get; private set; }

    internal IReadOnlyCollection<PlayerHeatRecord> Records => _records.Values;

    /// <summary>Heat of whoever is hottest. Law intensity is a world-level dial, so it tracks the max.</summary>
    internal float PeakHeat
    {
        get
        {
            var peak = 0f;
            foreach (var record in _records.Values)
                peak = Math.Max(peak, record.Heat);

            return peak;
        }
    }

    internal HeatTier PeakTier => HeatModel.TierFor(PeakHeat);

    internal PlayerHeatRecord LocalRecord => RecordFor(GameBridge.LocalPlayer());

    internal PlayerHeatRecord RecordFor(object? player)
    {
        var key = GameBridge.KeyFor(player);
        var name = GameBridge.NameOf(player);

        if (_records.TryGetValue(key, out var existing))
        {
            if (name.Length > 0)
                existing.PlayerName = name;

            return existing;
        }

        // Route creation through the saveable when one exists, so the record we mutate is the same
        // object S1API will serialise rather than a copy that quietly never gets written.
        var record = PoliceSaveState.Live?.GetOrCreate(key, name) ?? new PlayerHeatRecord { PlayerKey = key, PlayerName = name };
        _records[key] = record;
        return record;
    }

    internal PlayerHeatRecord? Find(string key) => _records.TryGetValue(key, out var record) ? record : null;

    /// <summary>Replaces the working set with what came off disk. Pre-load heat is discarded, as it should be.</summary>
    internal void Adopt(PoliceSaveState state)
    {
        _records.Clear();
        foreach (var record in state.Players)
            _records[record.PlayerKey] = record;

        _lastAnnouncedTier = (HeatTier)(-1);
        PoliceLog.Msg($"Loaded heat for {_records.Count} player(s) from the save.");
    }

    // ── Inputs ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// One crime, one heat change. The amount comes from the game's own fine for that crime, so the
    /// severity ranking is the developer's rather than ours.
    /// </summary>
    internal void AddCrimeHeat(object? player, string crimeClassName, int quantity)
    {
        var amount = PenaltyTable.HeatFor(crimeClassName) * Math.Max(1, quantity);
        if (amount <= 0f)
            return;

        if (GameBridge.CurfewActive())
            amount *= HeatModel.CurfewMultiplier;

        amount *= HeatModel.RegionMultiplier(GameBridge.RegionOf(player));

        var record = RecordFor(player);
        record.CleanDayStreak = 0;
        record.MinutesCleanToday = 0;
        record.DirtyToday = true;
        Add(record, amount, crimeClassName);
    }

    internal void AddArrestHeat(object? player)
    {
        var record = RecordFor(player);
        record.RecordArrest(TimeManager.ElapsedDays);
        record.CleanDayStreak = 0;
        record.DirtyToday = true;

        if (record.Outlaw != OutlawTier.Clean)
            record.ArrestsWhileOutlaw++;

        Add(record, HeatModel.ArrestHeat, "arrest");
    }

    /// <summary>
    /// Volume attracts attention. Capped per day so an honest grind can never push someone into the
    /// federal band — the ballot asked for police improvements, not anti-dealing improvements.
    /// </summary>
    internal void AddDealHeat(float payment)
    {
        if (!_config.HeatFromDeals.Value || payment <= 0f)
            return;

        var record = LocalRecord;
        var amount = Math.Min(payment / HeatModel.DealPaymentPerHeat, HeatModel.MaxDealHeatPerDay - record.DealHeatToday);
        if (amount <= 0f)
            return;

        record.DealHeatToday += amount;
        Add(record, amount, "volume");
    }

    internal void AddPoliceTake(object? player, float value)
    {
        if (value > 0f)
            RecordFor(player).PoliceTakeTotal += value;
    }

    internal void SetHeat(PlayerHeatRecord record, float heat)
    {
        record.Heat = HeatModel.Clamp(heat);
        AnnounceTierIfChanged();
    }

    // ── Ticks ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Once per uncapped game minute, from a postfix on the law controller's own tick. This is where
    /// the whole world-facing side of the mod happens, so it stays cheap and never throws upward.
    /// </summary>
    internal void MinutePass(object? lawController)
    {
        var scalar = Math.Max(0f, _config.IntensityScalar.Value);

        DrainAndCount();

        if (_config.EnableOutlaw.Value)
            _outlaw.Sync();

        if (!_config.EnableIntensity.Value || lawController is null)
            return;

        SyncBaseline(lawController);

        var target = HeatModel.TargetIntensity(_baselineIntensity, PeakHeat, scalar);
        CurrentIntensity = target;

        var current = Members.Read(lawController, "LE_Intensity", target);
        if (current != target && Members.TryWrite(lawController, "LE_Intensity", target))
        {
            _lastWrittenIntensity = target;

            // Take effect this minute rather than at the next day rollover: Evaluate() is what makes
            // the scheduler start and stop the individual patrol, sentry and checkpoint entries.
            if (GameReflection.TryRead(lawController, "CurrentSettings", out var settings, out _) && settings is not null)
                Members.Invoke(settings, "Evaluate");

            PoliceLog.Detail($"Law intensity {current} -> {target} (baseline {_baselineIntensity}, heat {PeakHeat:0.#}).");
        }

        var tier = PeakTier;

        if (_config.EnableScheduleTuning.Value)
            _schedule.Apply(tier, scalar, _config.MaxOfficersPerPost.Value);

        _detection.Apply(tier, scalar, FederalAgents.IsAgent);

        AnnounceTierIfChanged();
    }

    /// <summary>Clean-day streak, the daily deal-heat allowance, and outlaw promotion or release.</summary>
    internal void DayPass()
    {
        foreach (var record in _records.Values)
        {
            record.CleanDayStreak = record.DirtyToday ? 0 : record.CleanDayStreak + 1;
            record.DirtyToday = false;
            record.MinutesCleanToday = 0;
            record.DealHeatToday = 0f;

            if (_outlaw.Evaluate(record, out var previous))
                AnnounceOutlaw(record, previous);
        }
    }

    /// <summary>
    /// Sleeping is the shed valve. The daily figure is netted against whatever already drained live,
    /// so a full clean day always totals the configured amount however much of it the player watched
    /// tick past, and a multi-day skip sheds proportionally rather than once.
    /// </summary>
    internal void SleepEnd(int minutesSkipped)
    {
        var days = Math.Max(1, minutesSkipped / 1440 + 1);

        foreach (var record in _records.Values)
        {
            // Sampled before decay: the objective is "you slept and heat was still there afterwards",
            // which is the exact behaviour the vanilla wanted level does not have.
            if (record.Heat > 0.5f)
                SleptWithHeatRemaining = true;

            var decay = HeatModel.SleepDecay(
                _config.HeatDecayPerDay.Value,
                _config.HeatDecayPerCleanMinute.Value,
                record.MinutesCleanToday) * days;

            if (decay > 0f)
                Add(record, -decay, "rest");

            record.MinutesCleanToday = 0;
        }

        AnnounceTierIfChanged();
    }

    // ── Teardown ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Puts law intensity back where we found it. Deliberately runs before the schedule restore, so
    /// that restore's <c>Evaluate()</c> is the last thing to touch the world and sees fully vanilla
    /// numbers when it re-derives which posts should be staffed.
    /// </summary>
    internal void Restore()
    {
        if (_intensityOnEnable is not { } original)
            return;

        var controller = GameBridge.Singleton(GameTypes.LawController);
        if (controller is not null && Members.TryWrite(controller, "LE_Intensity", original))
            PoliceLog.Msg($"Law intensity restored to {original}.");

        _intensityOnEnable = null;
        _lastWrittenIntensity = int.MinValue;
    }

    internal void Forget()
    {
        _intensityOnEnable = null;
        _lastWrittenIntensity = int.MinValue;
        _lastAnnouncedTier = (HeatTier)(-1);
    }

    // ── Internals ─────────────────────────────────────────────────────────────────────────────

    private void Add(PlayerHeatRecord record, float amount, string reason)
    {
        if (amount > 0f)
            amount *= Math.Max(0f, _config.HeatGainScalar.Value);

        var before = record.Heat;
        record.Heat = HeatModel.Clamp(record.Heat + amount);

        if (Math.Abs(record.Heat - before) > 0.001f)
            PoliceLog.Detail($"Heat {before:0.#} -> {record.Heat:0.#} ({(amount >= 0 ? "+" : string.Empty)}{amount:0.##}, {reason}).");

        AnnounceTierIfChanged();
    }

    /// <summary>
    /// Live drain plus the two counters the federal triggers read. Only players with a clean rap
    /// sheet and nobody chasing them drain, so lying low is a visible reward rather than a wait.
    /// </summary>
    private void DrainAndCount()
    {
        var perMinute = Math.Max(0f, _config.HeatDecayPerCleanMinute.Value);
        var federalThreshold = _config.FederalHeatThreshold.Value;

        foreach (var player in GameBridge.Players())
        {
            if (player is null)
                continue;

            var record = RecordFor(player);

            if (record.Heat >= federalThreshold)
                record.MinutesAtFederalHeat++;
            else
                record.MinutesAtFederalHeat = 0;

            if (!IsLyingLow(player))
                continue;

            record.MinutesCleanToday++;

            if (perMinute > 0f && record.Heat > 0f)
                record.Heat = HeatModel.Clamp(record.Heat - perMinute);
        }
    }

    private static bool IsLyingLow(object player)
    {
        var crimeData = Members.ReadPath(player, "CrimeData");
        if (crimeData is null)
            return true;

        var pursuit = Members.Read<object?>(crimeData, "CurrentPursuitLevel", null);
        if (pursuit is not null && Convert.ToInt32(pursuit) != 0)
            return false;

        var crimes = Members.ReadPath(crimeData, "Crimes");
        return crimes is null || Members.Read(crimes, "Count", 0) == 0;
    }

    /// <summary>
    /// Keeps the vanilla curve as our floor. If law intensity is not the number we last wrote, the
    /// game's own daily arithmetic — or the player's <c>setlawintensity</c> — moved it, and that new
    /// value becomes the baseline instead of something we fight every minute.
    /// </summary>
    private void SyncBaseline(object lawController)
    {
        var current = Members.Read(lawController, "LE_Intensity", 1);

        _intensityOnEnable ??= current;

        if (current == _lastWrittenIntensity)
            return;

        _baselineIntensity = Math.Clamp(current, HeatModel.MinLawIntensity, HeatModel.MaxLawIntensity);
        PoliceLog.Detail($"Adopted law intensity baseline {_baselineIntensity} (something outside this module set it).");
    }

    private void AnnounceTierIfChanged()
    {
        var tier = PeakTier;
        if (tier == _lastAnnouncedTier)
            return;

        var previous = _lastAnnouncedTier;
        _lastAnnouncedTier = tier;

        if (previous == (HeatTier)(-1) || !_config.ShowHud.Value)
            return;

        var rising = tier > previous;
        GameBridge.Notify(
            rising ? "Police attention rising" : "Police attention easing",
            $"{HeatModel.TierName(tier)} — heat {PeakHeat:0}");

        PoliceLog.Msg($"Heat tier {HeatModel.TierName(previous)} -> {HeatModel.TierName(tier)} at heat {PeakHeat:0.#}.");
    }

    private void AnnounceOutlaw(PlayerHeatRecord record, OutlawTier previous)
    {
        PoliceLog.Msg($"Outlaw status for '{record.PlayerKey}': {OutlawState.Describe(previous)} -> {OutlawState.Describe(record.Outlaw)}.");

        if (!_config.ShowHud.Value)
            return;

        GameBridge.Notify(
            record.Outlaw == OutlawTier.Clean ? "Record cleared" : "Outlaw status",
            record.Outlaw switch
            {
                OutlawTier.Hunted => "You are HUNTED. Every officer recognises you and nobody is letting you walk.",
                OutlawTier.Marked => "You are MARKED. Searches will find something and pursuits will not let go.",
                _ => "You are no longer flagged. Fines and searches are back to normal.",
            },
            7f);
    }
}

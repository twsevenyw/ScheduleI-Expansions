using Expansions.Core;
using Expansions.Core.Configuration;
using Expansions.Core.Diagnostics;
using Expansions.Core.Tutorial;
using Expansions.PoliceOverhaul.Diagnostics;
using Expansions.PoliceOverhaul.Events;
using Expansions.PoliceOverhaul.Patches;
using Expansions.PoliceOverhaul.Runtime;
using Expansions.PoliceOverhaul.State;
using Expansions.PoliceOverhaul.Tutorial;
using S1API.GameTime;
using UnityEngine.SceneManagement;

namespace Expansions.PoliceOverhaul;

/// <summary>
/// Dynamic police intensity, heavier consequences, outlaw status and federal agents.
/// <para>
/// The design in one line: the game already ships a complete minute-ticked law scheduler that gates
/// every patrol, sentry, checkpoint and curfew on one integer, so this module does not build a
/// parallel police system — it keeps a persistent per-player heat score, maps it onto that integer
/// and the schedule data behind it, and lets the game put the officers out itself. That is what makes
/// the feature feel shipped, and what makes the whole thing unwind from one snapshot restore.
/// </para>
/// </summary>
public sealed class PoliceOverhaulModule : ExpansionModule
{
    public const string ModuleId = "police_overhaul";

    /// <summary>Frames to wait after a failed HostGate before retrying Wire.</summary>
    private const int WireRetryBackoffFrames = 30;

    /// <summary>
    /// Harmony patches are applied this many frames after services attach. Patching
    /// <c>CheckDeactivation</c> / action ticks mid-frame while officers are already updating is what
    /// killed the process on disable→re-enable during gameplay.
    /// </summary>
    private const int PatchDeferFrames = 3;

    private PoliceConfig? _config;
    private LawLevers? _levers;
    private LawScheduleTuner? _schedule;
    private DetectionTuner? _detection;
    private OutlawState? _outlaw;
    private HeatDirector? _heat;
    private ConsequenceService? _consequences;
    private FederalEvents? _federal;
    private RaidDirector? _raids;
    private EventScheduler? _scheduler;
    private ResponseTuner? _response;
    private OfficerKillResponse? _officerKills;

    private bool _wired;
    private bool _patchesApplied;
    private int _wireBackoffFrames;
    private int _enableGeneration;
    private string _lastWireBlock = "not attempted";

    // Cached so -= actually removes the same delegate S1API received on +=.
    private Action? _onDayPass;
    private Action? _onHourPass;
    private Action<int>? _onSleepEnd;

    public override string Id => ModuleId;

    public override string DisplayName => "Police Improvements";

    public override string Description =>
        "Persistent heat drives the game's own patrol scheduler. Push it far enough and you get outlaw " +
        "status, federal agents, stakeouts, raids on your properties, and an arrest that costs you the " +
        "day, your kit and your bank balance.";

    public override string Version => "0.3.1";

    public override string ConfigCategory => ExpansionConfig.CategoryFor("PoliceImprovements");

    protected override void OnRegistered()
    {
        PoliceLog.Attach(Log);
        _config = new PoliceConfig(Config);
        PoliceLog.Verbose = _config.DebugLogging.Value;
        Log.Debug("Registered.");
    }

    public override void OnEnabled()
    {
        if (_config is null)
            return;

        _enableGeneration++;
        _wireBackoffFrames = 0;
        _lastWireBlock = "enable started";
        PoliceRuntime.WireStatus = "enabled; waiting to wire into Main";

        PoliceLog.Verbose = _config.DebugLogging.Value;
        _config.DebugLogging.Changed += OnVerboseChanged;
        Lifetime.OnDispose(() => _config.DebugLogging.Changed -= OnVerboseChanged);

        Try("noting Dispatch toast channel", PoliceMessages.NoteReady);

        Try("registering probes", () =>
        {
            foreach (var probe in PoliceOverhaulProbes.All())
                Lifetime.Add(ProbeRegistry.Register(probe));
        });

        Try("registering the tutorial chapter", () => Lifetime.Add(TutorialRegistry.Register(new PoliceChapter())));

        Try("registering menu actions", () => PoliceMenuActions.Register(Lifetime));

        Try("registering triggerable events", () => PoliceEvents.Register(Lifetime));

        // Mid-session re-enable must not wait for another scene load — and the first enable often
        // lands on Menu before Main exists, so Wire is also retried from OnUpdate.
        TryWire("OnEnabled");
    }

    public override void OnDisabled()
    {
    }

    public override void OnUpdate()
    {
        if (!_wired)
        {
            TryWire("OnUpdate");
            // FinishWire is queued on Deferred during Wire — must pump even before patches land.
            Deferred.Pump();
            return;
        }

        Deferred.Pump();
    }

    public override void OnSceneLoaded(int buildIndex, string sceneName)
    {
        TryWire($"OnSceneLoaded({sceneName}/idx{buildIndex})");

        // Scene unload clears Deferred; if services attached but patches never landed, re-queue.
        if (_wired && !_patchesApplied && IsGameplayScene(sceneName, buildIndex))
        {
            var gen = _enableGeneration;
            Deferred.After(
                PatchDeferFrames,
                "police patch apply after scene load",
                () => FinishWire(gen, $"OnSceneLoaded-requeue({sceneName})", "re-queue"));
        }
    }

    public override void OnSceneUnloaded(int buildIndex, string sceneName)
    {
        if (!IsGameplayScene(sceneName, buildIndex))
            return;

        // Scene objects are gone, so restoring into them would write destroyed natives. Drop those
        // references; services stay wired and re-snapshot on the next minute tick after Main returns.
        FederalAgents.Forget();
        PoliceForce.Forget();
        OfficerDeployment.ClearHeldPosts();
        Deferred.Clear();
        Estate.Forget();
        _raids?.Forget();
        _scheduler?.Forget();
        _schedule?.Forget();
        _detection?.Forget();
        _heat?.Forget();
        _consequences?.ClearAllCharges();
        _outlaw?.Economy.Revert();

        if (_wired && !_patchesApplied)
        {
            _patchesApplied = false;
            PoliceRuntime.WireStatus = "scene unloaded during patch deferral; will finish wire on next Main";
        }
    }

    /// <summary>
    /// Idempotent attempt to attach services. Retries when the scene is not ready or HostGate is
    /// briefly false during FishNet startup — that one-shot failure is why actions stayed red after
    /// a successful "Enabled." at boot.
    /// </summary>
    private void TryWire(string trigger)
    {
        if (_wired || _config is null)
            return;

        if (_wireBackoffFrames > 0)
        {
            _wireBackoffFrames--;
            return;
        }

        if (!IsGameplayScene())
        {
            _lastWireBlock = $"waiting for Main (active='{GameReflection.ActiveSceneName()}', via {trigger})";
            PoliceRuntime.WireStatus = _lastWireBlock;
            return;
        }

        if (!HostGate.Evaluate(out var authority))
        {
            _lastWireBlock = $"HostGate not ready ({authority}) via {trigger}";
            PoliceRuntime.WireStatus = _lastWireBlock;
            _wireBackoffFrames = WireRetryBackoffFrames;
            PoliceLog.Detail($"Wire deferred: {_lastWireBlock}");
            return;
        }

        Wire(trigger, authority);
    }

    private void Wire(string trigger, string authority)
    {
        if (_wired || _config is null)
            return;

        var generation = _enableGeneration;

        try
        {
            PenaltyTable.Load();

            _levers = new LawLevers();
            _levers.Survey();

            _schedule = new LawScheduleTuner();
            _detection = new DetectionTuner(_levers);
            _outlaw = new OutlawState(_config, key => _heat?.Find(key));
            _heat = new HeatDirector(_config, _schedule, _detection, _outlaw);
            _consequences = new ConsequenceService(_config, _heat, _outlaw);
            _scheduler = new EventScheduler(_config);
            _response = new ResponseTuner(_config, _levers);
            _officerKills = new OfficerKillResponse(_config, _heat);
            _federal = new FederalEvents(_config, _heat, _scheduler);
            _raids = new RaidDirector(_config, _heat, _outlaw, _scheduler);

            PoliceRuntime.Attach(
                _config, _levers, _schedule, _detection, _outlaw, _heat, _consequences,
                _federal, _raids, _scheduler, _response, _officerKills);

            // IsLive is true from this point — menu actions turn green before patches land.
            if (PoliceSaveState.Live is { } state)
            {
                _heat.Adopt(state);
                _scheduler.Arm(state);
            }
            else
            {
                _scheduler.Arm(null);
            }

            PoliceSaveState.Loaded += OnSaveLoaded;
            FederalAgents.AgentActivated += OnAgentActivated;

            _onDayPass = OnDayPass;
            _onHourPass = OnHourPass;
            _onSleepEnd = OnSleepEnd;
            TimeManager.OnDayPass += _onDayPass;
            TimeManager.OnHourPass += _onHourPass;
            TimeManager.OnSleepEnd += _onSleepEnd;

            _wired = true;
            _patchesApplied = false;
            Lifetime.OnDispose(Unwire);

            PoliceRuntime.WireStatus =
                $"services live (via {trigger}, {authority}); patches deferred {PatchDeferFrames} frame(s)";
            PoliceLog.Msg(
                $"Police Improvements services attached via {trigger} ({authority}). " +
                $"Menu actions are live; Harmony patches land in {PatchDeferFrames} frame(s).");

            var gen = generation;
            Deferred.After(PatchDeferFrames, "police patch apply", () => FinishWire(gen, trigger, authority));
        }
        catch (Exception ex)
        {
            Log.Error("Police Improvements failed to wire up; the module is inert this session and nothing was changed.", ex);
            PoliceRuntime.WireStatus = "wire failed: " + PoliceLog.Describe(ex);
            Unwire();
        }
    }

    /// <summary>
    /// Second stage: patches + world writes. Must not run on the same stack as the F7 toggle —
    /// that is the disable→re-enable crash.
    /// </summary>
    private void FinishWire(int generation, string trigger, string authority)
    {
        if (generation != _enableGeneration || !_wired || _config is null || _patchesApplied)
            return;

        try
        {
            // Belt-and-suspenders: a prior cycle's instance should already be UnpatchSelf'd by Core,
            // but never stack prefixes on CheckDeactivation.
            try
            {
                Harmony.UnpatchSelf();
            }
            catch (Exception ex)
            {
                PoliceLog.Detail($"Pre-apply UnpatchSelf: {PoliceLog.Describe(ex)}");
            }

            PolicePatches.Apply(Harmony);
            _patchesApplied = true;

            _response?.Apply();

            if (_config.EnableFederalAgents.Value)
            {
                var viability = FederalAgents.Survey();
                PoliceLog.Msg(viability.CanDesignate
                    ? $"Federal agents: {viability.Reason}."
                    : $"Federal agents unavailable this session: {viability.Reason}. Every other pillar is unaffected.");
            }

            var summary =
                $"enable-cycle OK via {trigger} ({authority}): " +
                $"unpatched→repatched {PolicePatches.AppliedTargets.Count} target(s), " +
                $"levers {_levers?.Status.Values.Count(s => s.IsWritable) ?? 0} writable, " +
                $"schedule posts pending first minute tick, detection snapshots empty until Apply, " +
                $"npc-policy=no-create/clone/re-id/destroy, " +
                $"federal=designate-shipped (tagged={FederalAgents.OwnedCount}), " +
                $"statics re-resolved (no pointers carried across disable).";

            PoliceRuntime.WireStatus = summary;
            PoliceLog.Msg(summary);

            PoliceLog.Msg(
                $"Police Improvements active. Intensity scalar {_config.IntensityScalar.Value:0.00}, " +
                $"pillars: intensity {On(_config.EnableIntensity.Value)}, schedule {On(_config.EnableScheduleTuning.Value)}, " +
                $"consequences {On(_config.EnableConsequences.Value)}, outlaw {On(_config.EnableOutlaw.Value)}, " +
                $"federal {On(_config.EnableFederalAgents.Value)}, stakeouts {On(_config.EnableStakeouts.Value)}, " +
                $"raids {On(_config.EnablePropertyRaids.Value)}, jail day {On(_config.EnableJailDay.Value)}, " +
                $"equipment loss {On(_config.EnableEquipmentLoss.Value)}, informant fallout {On(_config.EnableRelationshipDamage.Value)}.");
        }
        catch (Exception ex)
        {
            Log.Error("Police Improvements failed while applying patches after wire; unwinding.", ex);
            PoliceRuntime.WireStatus = "finish-wire failed: " + PoliceLog.Describe(ex);
            Unwire();
        }
    }

    private void Unwire()
    {
        var generation = _enableGeneration;
        var hadPatches = _patchesApplied;
        var appliedCount = PolicePatches.AppliedTargets.Count;

        PoliceSaveState.Loaded -= OnSaveLoaded;
        FederalAgents.AgentActivated -= OnAgentActivated;

        // S1API declares these fields as non-nullable Action; Delegate.Remove can yield null.
        if (_onDayPass is not null)
            TimeManager.OnDayPass = (Action)Delegate.Remove(TimeManager.OnDayPass, _onDayPass)!;
        if (_onHourPass is not null)
            TimeManager.OnHourPass = (Action)Delegate.Remove(TimeManager.OnHourPass, _onHourPass)!;
        if (_onSleepEnd is not null)
            TimeManager.OnSleepEnd = (Action<int>)Delegate.Remove(TimeManager.OnSleepEnd, _onSleepEnd)!;

        _onDayPass = null;
        _onHourPass = null;
        _onSleepEnd = null;

        SafeStep("dropping deferred work", Deferred.Clear);
        SafeStep("calling off any raid", () => _raids?.Cancel("the module was switched off"));
        SafeStep("clearing deployment posts", OfficerDeployment.ClearHeldPosts);
        SafeStep("releasing federal designations", () =>
        {
            _federal?.Abort();
            FederalAgents.Forget();
        });
        SafeStep("clearing police-force counters", PoliceForce.Forget);
        SafeStep("clearing outlaw effects", () => _outlaw?.Revert());
        SafeStep("restoring detection + global levers", () => _detection?.Restore());
        SafeStep("restoring response tuner state", () => _response?.Restore());
        SafeStep("restoring any leftover levers", () => _levers?.Restore());
        SafeStep("restoring law intensity", () => _heat?.Restore());
        SafeStep("restoring the law schedule", () => _schedule?.Restore());

        PoliceRuntime.Detach();
        PolicePatches.ResetTracking();

        _raids = null;
        _federal = null;
        _scheduler = null;
        _response = null;
        _officerKills = null;
        _consequences = null;
        _heat = null;
        _outlaw = null;
        _detection = null;
        _schedule = null;
        _levers = null;
        _wired = false;
        _patchesApplied = false;
        _wireBackoffFrames = 0;

        var summary =
            $"disable-cycle OK (gen {generation}): " +
            $"restored detection/levers/schedule/intensity, " +
            $"released federal designations + cleared deferred/deployment, " +
            $"dropped TimeManager delegates, " +
            $"patches were {(hadPatches ? $"live ({appliedCount}) — Core UnpatchSelf follows" : "not yet applied")}.";

        PoliceRuntime.WireStatus = summary;
        PoliceLog.Msg(summary);
    }

    private void OnSaveLoaded(PoliceSaveState state)
    {
        _heat?.Adopt(state);
        _scheduler?.Arm(state);
    }

    private void OnDayPass() => Try("the day rollover", () => _heat?.DayPass());

    private void OnHourPass()
    {
        Try("the hourly federal check", () => _federal?.HourPass());
        Try("the hourly raid check", () => _raids?.HourPass());
    }

    private static void OnAgentActivated() => TutorialSignals.Raise(PoliceChapter.AgentSignal);

    private void OnSleepEnd(int minutesSkipped) => Try("sleep decay", () => _heat?.SleepEnd(minutesSkipped));

    private void OnVerboseChanged(bool previous, bool current) => PoliceLog.Verbose = current;

    internal static bool IsGameplayScene(string? sceneName = null, int? buildIndex = null)
    {
        var name = sceneName ?? GameReflection.ActiveSceneName();
        if (string.Equals(name, "Main", StringComparison.OrdinalIgnoreCase))
            return true;

        try
        {
            var index = buildIndex ?? SceneManager.GetActiveScene().buildIndex;
            return index == 1;
        }
        catch
        {
            return false;
        }
    }

    private static string On(bool value) => value ? "on" : "off";

    private void SafeStep(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Error($"Police Improvements threw while {what} during teardown; continuing with the rest.", ex);
        }
    }

    private void Try(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Error($"Police Improvements failed while {what}; the rest of the module carries on.", ex);
        }
    }
}

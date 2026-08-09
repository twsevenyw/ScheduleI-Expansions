using Expansions.Core;
using Expansions.Core.Configuration;
using Expansions.Core.Diagnostics;
using Expansions.Core.Tutorial;
using Expansions.PoliceOverhaul.Diagnostics;
using Expansions.PoliceOverhaul.Patches;
using Expansions.PoliceOverhaul.Runtime;
using Expansions.PoliceOverhaul.State;
using Expansions.PoliceOverhaul.Tutorial;
using S1API.GameTime;

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

    private PoliceConfig? _config;
    private LawLevers? _levers;
    private LawScheduleTuner? _schedule;
    private DetectionTuner? _detection;
    private OutlawState? _outlaw;
    private HeatDirector? _heat;
    private ConsequenceService? _consequences;
    private FederalEvents? _federal;

    private bool _wired;

    public override string Id => ModuleId;

    public override string DisplayName => "Police Improvements";

    public override string Description =>
        "Dynamic police intensity, federal agents, outlaw status, and heavier consequences when caught.";

    public override string Version => "0.2.0";

    // The id predates the "Police Improvements" name, so the default id-derived category would
    // read PoliceOverhaul_01_Main and not match how the mod presents itself anywhere else.
    public override string ConfigCategory => ExpansionConfig.CategoryFor("PoliceImprovements");

    protected override void OnRegistered()
    {
        // Runs once, outside any enable cycle and with no teardown hook, so nothing here may touch
        // the world. Settings only.
        PoliceLog.Attach(Log);
        _config = new PoliceConfig(Config);
        PoliceLog.Verbose = _config.DebugLogging.Value;
        Log.Debug("Registered.");
    }

    public override void OnEnabled()
    {
        if (_config is null)
            return;

        PoliceLog.Verbose = _config.DebugLogging.Value;
        _config.DebugLogging.Changed += OnVerboseChanged;
        Lifetime.OnDispose(() => _config.DebugLogging.Changed -= OnVerboseChanged);

        Try("registering probes", () =>
        {
            foreach (var probe in PoliceOverhaulProbes.All())
                Lifetime.Add(ProbeRegistry.Register(probe));
        });

        Try("registering the tutorial chapter", () => Lifetime.Add(TutorialRegistry.Register(new PoliceChapter())));

        Try("registering menu actions", () => PoliceMenuActions.Register(Lifetime));

        // Re-enabling mid-session has to work without waiting for another scene load, which would
        // otherwise leave the module inert until the player went back to the menu and in again.
        if (string.Equals(GameReflection.ActiveSceneName(), "Main", StringComparison.Ordinal))
            Wire();
    }

    /// <summary>
    /// Deliberately empty. Everything reversible is registered with <see cref="ExpansionModule.Lifetime"/>
    /// during <see cref="Wire"/>, and the registry unpatches this module's Harmony instance for us —
    /// which is the only way the "disable puts the world back exactly as it was" promise can survive
    /// a partial initialisation.
    /// </summary>
    public override void OnDisabled()
    {
    }

    public override void OnUpdate() => FederalAgents.Pump();

    public override void OnSceneLoaded(int buildIndex, string sceneName)
    {
        if (string.Equals(sceneName, "Main", StringComparison.Ordinal))
            Wire();
    }

    public override void OnSceneUnloaded(int buildIndex, string sceneName)
    {
        if (!string.Equals(sceneName, "Main", StringComparison.Ordinal))
            return;

        // The world is gone, so there is nothing to restore into — attempting it would write into
        // destroyed objects. Drop the references and let the next scene re-snapshot from vanilla.
        FederalAgents.Forget();
        _schedule?.Forget();
        _detection?.Forget();
        _heat?.Forget();
        _consequences?.ClearAllCharges();
    }

    /// <summary>
    /// Builds the service set and hooks the world. Runs on the first gameplay scene rather than at
    /// mod load, because the game's own types are not reliably ready before then.
    /// </summary>
    private void Wire()
    {
        if (_wired || _config is null)
            return;

        if (!HostGate.Evaluate(out var authority))
        {
            // Clients still show the menu and the config; only the host owns law state. The game's
            // own logs are explicit that dispatch from a client is rejected outright.
            PoliceLog.Msg($"Not authoritative on this peer ({authority}); police state is left to the host.");
            return;
        }

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
            _federal = new FederalEvents(_config, _heat);

            PoliceRuntime.Attach(_config, _levers, _schedule, _detection, _outlaw, _heat, _consequences, _federal);

            if (PoliceSaveState.Live is { } state)
                _heat.Adopt(state);

            PoliceSaveState.Loaded += OnSaveLoaded;
            FederalAgents.AgentActivated += OnAgentActivated;
            TimeManager.OnDayPass += OnDayPass;
            TimeManager.OnHourPass += OnHourPass;
            TimeManager.OnSleepEnd += OnSleepEnd;

            PolicePatches.Apply(Harmony);

            if (_config.EnableFederalAgents.Value)
            {
                var viability = FederalAgents.Survey();
                PoliceLog.Msg(viability.CanSpawn
                    ? $"Federal agents viable via the {viability.Strategy} path."
                    : $"Federal agents unavailable this session: {viability.Reason}. Every other pillar is unaffected.");
            }

            _wired = true;
            Lifetime.OnDispose(Unwire);

            PoliceLog.Msg(
                $"Police Improvements active. Intensity scalar {_config.IntensityScalar.Value:0.00}, " +
                $"pillars: intensity {On(_config.EnableIntensity.Value)}, schedule {On(_config.EnableScheduleTuning.Value)}, " +
                $"consequences {On(_config.EnableConsequences.Value)}, outlaw {On(_config.EnableOutlaw.Value)}, " +
                $"federal {On(_config.EnableFederalAgents.Value)}.");
        }
        catch (Exception ex)
        {
            Log.Error("Police Improvements failed to wire up; the module is inert this session and nothing was changed.", ex);
            Unwire();
        }
    }

    /// <summary>
    /// The single unwind path, used by both disable and a failed wiring.
    /// <para>
    /// Order matters. Agents go first, because despawning one mutates the officer list the detection
    /// restore walks. Then the rule changes, then the per-officer values, then law intensity, and the
    /// schedule numbers last — that restore ends in <c>Evaluate()</c>, so making it last means the
    /// game re-derives which posts should be staffed exactly once, from fully vanilla data.
    /// </para>
    /// </summary>
    private void Unwire()
    {
        PoliceSaveState.Loaded -= OnSaveLoaded;
        FederalAgents.AgentActivated -= OnAgentActivated;
        Detach(ref TimeManager.OnDayPass, OnDayPass);
        Detach(ref TimeManager.OnHourPass, OnHourPass);
        Detach(ref TimeManager.OnSleepEnd, OnSleepEnd);

        SafeStep("withdrawing federal agents", () => _federal?.Abort());
        SafeStep("clearing outlaw effects", () => _outlaw?.Revert());
        SafeStep("restoring detection settings", () => _detection?.Restore());
        SafeStep("restoring law intensity", () => _heat?.Restore());
        SafeStep("restoring the law schedule", () => _schedule?.Restore());

        PoliceRuntime.Detach();

        _federal = null;
        _consequences = null;
        _heat = null;
        _outlaw = null;
        _detection = null;
        _schedule = null;
        _levers = null;
        _wired = false;
    }

    private void OnSaveLoaded(PoliceSaveState state) => _heat?.Adopt(state);

    private void OnDayPass() => Try("the day rollover", () => _heat?.DayPass());

    private void OnHourPass() => Try("the hourly federal check", () => _federal?.HourPass());

    private static void OnAgentActivated() => TutorialSignals.Raise(PoliceChapter.AgentSignal);

    private void OnSleepEnd(int minutesSkipped) => Try("sleep decay", () => _heat?.SleepEnd(minutesSkipped));

    private void OnVerboseChanged(bool previous, bool current) => PoliceLog.Verbose = current;

    /// <summary>
    /// S1API's time hooks are plain static delegate fields compiled without nullable annotations, so
    /// <c>-=</c> on them reads as a possibly-null assignment. Removing through the field by reference
    /// keeps the unsubscribe honest without scattering null-forgiving operators over teardown.
    /// </summary>
    private static void Detach<T>(ref T field, T handler) where T : Delegate =>
        field = (T)Delegate.Remove(field, handler)!;

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

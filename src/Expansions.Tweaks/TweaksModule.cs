using Expansions.Core;
using Expansions.Core.Diagnostics;
using Expansions.Core.Tutorial;
using Expansions.Tweaks.Diagnostics;
using Expansions.Tweaks.Menu;
using Expansions.Tweaks.Patches;
using Expansions.Tweaks.Runtime;
using Expansions.Tweaks.Tutorial;

namespace Expansions.Tweaks;

/// <summary>
/// Three small quality-of-life tweaks that share one toggle card: faster mixing, a configurable
/// weekly ATM deposit ceiling, and faster shop deliveries.
/// <para>
/// They have nothing in common mechanically, and that is the point of shipping them together — each
/// one is a handful of lines against a different subsystem, and three toggle cards for three numbers
/// would be worse than one card for a category the owner can reason about. What they do share is the
/// rule that made all three tractable: none of them writes an IL2CPP <c>const</c>, and none of them
/// patches a small getter and hopes it was not inlined. Every one moves a value that has real
/// storage, so the change cannot be optimised away, and every one is put back exactly on disable.
/// </para>
/// </summary>
public sealed class TweaksModule : ExpansionModule
{
    public const string ModuleId = "tweaks";

    private const string GameplayScene = "Main";

    /// <summary>How long to keep retrying the world-facing half of the wiring, in frames at ~60fps.</summary>
    private const int WireAttemptLimit = 240;

    /// <summary>Frames the stuck-delivery sweep stays armed for; roughly twenty seconds at 60fps.</summary>
    private const int RepairSweepLimit = 1200;

    /// <summary>Frames between sweeps, so a save with nothing to fix is not scanned every frame.</summary>
    private const int RepairSweepSpacing = 60;

    private readonly MixingSpeed _mixing = new();
    private readonly DepositLimit _deposit = new();
    private readonly DeliverySpeed _delivery = new();

    private TweaksConfig? _config;
    private bool _patched;
    private bool _settled;
    private int _attempts;
    private int _repairSweeps;

    public override string Id => ModuleId;

    public override string DisplayName => "Quality of Life";

    public override string Description =>
        "Mixing runs at twice the speed, the ATM takes $25,000 a week instead of $10,000, and shop " +
        "deliveries arrive in half the time. Every number is a multiplier or an amount you can tune, " +
        "1x is exactly vanilla, and switching this off puts all three back.";

    public override string Version => "0.1.0";

    protected override void OnRegistered()
    {
        // Runs once, outside any enable cycle and with no teardown hook, so nothing here may touch
        // the world. Settings only.
        TweakLog.Attach(Log);
        _config = new TweaksConfig(Config);
        TweakLog.Verbose = _config.DebugLogging.Value;
        Log.Debug("Registered.");
    }

    public override void OnEnabled()
    {
        if (_config is null)
            return;

        TweakLog.Verbose = _config.DebugLogging.Value;
        TweaksRuntime.Attach(_config, _mixing, _deposit, _delivery);
        TweaksRuntime.Reapply = Resettle;
        Lifetime.OnDispose(Unwire);

        Watch(_config.DebugLogging, (_, current) => TweakLog.Verbose = current);
        Watch(_config.EnableFasterMixing, (_, _) => Resettle());
        Watch(_config.MixSpeedMultiplier, (_, _) => Resettle());
        Watch(_config.EnableDepositLimit, (_, _) => Resettle());
        Watch(_config.WeeklyDepositLimit, (_, _) => Resettle());
        Watch(_config.EnableFasterDeliveries, (_, _) => Resettle());
        Watch(_config.DeliverySpeedMultiplier, (_, _) => Resettle());

        Try("registering probes", () =>
        {
            foreach (var probe in TweakProbes.All())
                Lifetime.Add(ProbeRegistry.Register(probe));
        });

        Try("registering the tutorial chapter", () => Lifetime.Add(TutorialRegistry.Register(new TweaksChapter())));

        Try("registering menu actions", () => TweakActions.Register(Lifetime));

        // Re-enabling mid-session has to work without waiting for another scene load, which would
        // otherwise leave the module inert until the player went back to the menu and in again.
        if (string.Equals(GameReflection.ActiveSceneName(), GameplayScene, StringComparison.Ordinal))
        {
            BeginWiring();

            // Only on the enable transition, and only with a world already loaded: an order that was
            // already on its way gets the speed-up it would have had if the module had been on when
            // it was placed. Doing this on every scene load instead would halve the same delivery
            // again on every reload.
            Try("speeding up deliveries already on the way", () => _delivery.RescaleInFlight());
        }
    }

    /// <summary>
    /// Deliberately empty. Everything reversible is registered with <see cref="ExpansionModule.Lifetime"/>,
    /// and the registry unpatches this module's Harmony instance for us — which is the only way the
    /// "disable puts the world back exactly as it was" promise survives a partial initialisation.
    /// </summary>
    public override void OnDisabled()
    {
    }

    public override void OnUpdate()
    {
        if (_config is null)
            return;

        if (_settled)
        {
            SweepForStuckDeliveries();
            return;
        }

        if (_attempts <= 0)
            return;

        // Retried rather than done once: the ATM statics and the delivery list are not reliably
        // resolvable on the frame the gameplay scene reports itself loaded.
        if (--_attempts % 15 != 0)
            return;

        Settle();
    }

    /// <summary>
    /// Keeps trying the repair pass for a short while after the world settles.
    /// <para>
    /// One shot at startup is not enough for the case this exists to solve: repairing a poisoned
    /// countdown needs a live delivery shop to quote a replacement from, and the phone's shops are
    /// not necessarily built on the frame everything else resolves. So the sweep runs until a pass
    /// comes back with nothing it could not handle, then stops for good. On a healthy save the very
    /// first pass qualifies and this costs one call.
    /// </para>
    /// </summary>
    private void SweepForStuckDeliveries()
    {
        if (_repairSweeps <= 0 || --_repairSweeps % RepairSweepSpacing != 0)
            return;

        Try("checking deliveries for implausible arrival times", () =>
        {
            if (_delivery.Repair("the startup sweep").BeyondHelp == 0)
                _repairSweeps = 0;
        });
    }

    public override void OnSceneLoaded(int buildIndex, string sceneName)
    {
        if (string.Equals(sceneName, GameplayScene, StringComparison.Ordinal))
            BeginWiring();
    }

    public override void OnSceneUnloaded(int buildIndex, string sceneName)
    {
        if (!string.Equals(sceneName, GameplayScene, StringComparison.Ordinal))
            return;

        // The stations and the delivery list are gone, so restoring into them would be writing to
        // destroyed natives. Drop those references and let the next scene re-snapshot from vanilla.
        _mixing.Forget();
        _delivery.Forget();

        // ATM.WeeklyDepositSum is a static that survives into the main menu and the next save, so
        // it is put back properly. The delivery speed-up owns no world state at all — disarming it
        // is just clearing the multiplier the quote postfix reads.
        SafeStep("clearing the deposit-limit offset", _deposit.Restore);
        SafeStep("disarming the delivery speed-up", _delivery.Restore);

        _settled = false;
        _attempts = 0;
        _repairSweeps = 0;
    }

    private void BeginWiring()
    {
        if (_config is null)
            return;

        if (!_patched)
        {
            Try("patching", () =>
            {
                TweakPatches.Apply(Harmony, _config);
                _patched = true;
            });
        }

        _settled = false;
        _attempts = WireAttemptLimit;
        Settle();
    }

    /// <summary>
    /// Applies the world-facing half: the per-station mix time, the deposit-counter offset and the
    /// delivery multiplier. Idempotent, so the retry loop can call it as often as it likes.
    /// </summary>
    private void Settle()
    {
        if (_config is null)
            return;

        // Non-short-circuiting: a feature that cannot bind yet must not stop the other two settling.
        var done = Step("scaling the mixing stations", () => { _mixing.Apply(_config.ResolvedMixMultiplier); return true; })
                   & Step("raising the deposit limit", () => _deposit.Apply(_config.ResolvedDepositLimit))
                   & Step("speeding up deliveries", () => _delivery.Apply(_config.ResolvedDeliveryMultiplier));

        if (!done)
            return;

        _settled = true;
        _attempts = 0;

        // Armed on every settle, including after a save load, and deliberately so: the pass only
        // rewrites a countdown that is not a plausible duration, so on a healthy save it writes
        // nothing at all. That is what gets an order poisoned by an earlier build fixed without the
        // owner having to know the repair action exists.
        _repairSweeps = RepairSweepLimit;

        TweakLog.Msg(
            $"Quality of Life active. Mixing x{_config.ResolvedMixMultiplier:0.##} " +
            $"({_mixing.StationsScaled} station(s) scaled), deposit ceiling " +
            $"{Describe(_config.ResolvedDepositLimit)}, deliveries x{_config.ResolvedDeliveryMultiplier:0.##}.");
    }

    /// <summary>Re-applies from the current config, for a value the owner changed mid-session.</summary>
    private void Resettle()
    {
        if (_config is null)
            return;

        _settled = false;
        _attempts = WireAttemptLimit;
        Settle();
    }

    private bool Step(string what, Func<bool> action)
    {
        try
        {
            return action();
        }
        catch (Exception ex)
        {
            Log.Error($"Quality of Life failed while {what}; the rest of the module carries on.", ex);
            return false;
        }
    }

    private void Unwire()
    {
        SafeStep("restoring the mixing stations", _mixing.Restore);
        SafeStep("restoring deliveries already on the way", _delivery.RestoreInFlight);
        SafeStep("disarming the delivery speed-up", _delivery.Restore);
        SafeStep("clearing the deposit-limit offset", _deposit.Restore);

        TweaksRuntime.Detach();

        _patched = false;
        _settled = false;
        _attempts = 0;
    }

    private static string Describe(float? limit) =>
        limit is { } value ? "$" + value.ToString("N0") : "vanilla";

    private void Watch<T>(Core.Configuration.ConfigValue<T> value, Action<T, T> handler)
    {
        value.Changed += handler;
        Lifetime.OnDispose(() => value.Changed -= handler);
    }

    private void SafeStep(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Error($"Quality of Life threw while {what} during teardown; continuing with the rest.", ex);
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
            Log.Error($"Quality of Life failed while {what}; the rest of the module carries on.", ex);
        }
    }
}

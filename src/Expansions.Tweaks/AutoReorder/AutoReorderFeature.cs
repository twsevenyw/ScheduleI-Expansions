using System.Runtime.CompilerServices;
using Expansions.Core;
using Expansions.Core.Actions;
using Expansions.Core.Diagnostics;
using Expansions.Tweaks.Runtime;
using MelonLoader;

namespace Expansions.Tweaks.AutoReorder;

/// <summary>
/// Wires auto-reorder into the Quality of Life module without that module having to know it exists.
/// <para>
/// A module initializer runs before any other code in this assembly, which is early enough to
/// subscribe to <see cref="ExpansionRegistry"/> before <c>OnInitializeMelon</c> registers the module.
/// The feature then declares its settings on the same <c>ModuleConfig</c> — so the owner still sees
/// one <c>[Tweaks_01_Main]</c> section — and enables and disables in step with the module's own
/// toggle, using the module's per-cycle <c>Lifetime</c> for teardown.
/// </para>
/// <para>
/// The point of the indirection is file ownership: everything auto-reorder needs lives under
/// <c>AutoReorder\</c> and nothing outside it is edited, so this feature and the rest of the module
/// can be worked on at the same time without either overwriting the other.
/// </para>
/// </summary>
internal static class AutoReorderFeature
{
    /// <summary>A pump that keeps throwing is switched off rather than logging at 60 Hz.</summary>
    private const int MaxPumpErrors = 10;

    private static readonly object Gate = new();

    private static ModuleContext? _context;
    private static AutoReorderSettings? _settings;
    private static AutoReorderEngine? _engine;
    private static AutoReorderUi? _ui;
    /// <summary>
    /// <c>MelonEvents.OnUpdate</c> takes MelonLoader's own <c>LemonAction</c>, not <c>System.Action</c>,
    /// and unsubscribing needs the same instance the subscription was made with.
    /// </summary>
    private static LemonAction? _pump;

    private static Action? _onSaveLoaded;
    private static int _pumpErrors;

    // CA2255 warns that a module initializer belongs in application code. It is exactly the right
    // tool here: it is the only hook that is guaranteed to run before MelonLoader constructs the
    // melon in this assembly, which is what lets the feature attach itself without a line of code in
    // a file another agent owns. All it does is subscribe to two events.
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Install()
    {
        try
        {
            ExpansionRegistry.ModuleRegistered += OnModuleRegistered;
            ExpansionRegistry.ModuleToggled += OnModuleToggled;

            // Belt and braces for a future load order where the module beat us to it.
            foreach (var context in ExpansionRegistry.Contexts)
            {
                if (!IsOurs(context.Module))
                    continue;

                Declare(context);

                if (context.IsActive)
                    Enable(context);
            }
        }
        catch (Exception ex)
        {
            // A throw here would fail the whole assembly's initialisation and take the melon with it.
            MelonLogger.Warning($"[Expansions/tweaks] Auto-reorder could not install itself: {ex.Message}");
        }
    }

    private static bool IsOurs(IExpansionModule module) =>
        ReferenceEquals(module.GetType().Assembly, typeof(AutoReorderFeature).Assembly);

    private static void OnModuleRegistered(IExpansionModule module)
    {
        if (!IsOurs(module))
            return;

        var context = ExpansionRegistry.ContextOf(module.Id);
        if (context is not null)
            Declare(context);
    }

    private static void OnModuleToggled(IExpansionModule module, bool enabled)
    {
        if (!IsOurs(module))
            return;

        var context = ExpansionRegistry.ContextOf(module.Id);
        if (context is null)
            return;

        if (enabled)
            Enable(context);
        else
            Detach();
    }

    /// <summary>
    /// Declares the settings, once, on the module's own config. Deliberately separate from
    /// <see cref="Enable"/>: settings must survive a toggle, and re-binding them on every enable
    /// would be pointless work at best.
    /// </summary>
    private static void Declare(ModuleContext context)
    {
        lock (Gate)
        {
            if (_settings is not null && ReferenceEquals(_context, context))
                return;

            _context = context;

            try
            {
                _settings = new AutoReorderSettings(context.Config);
            }
            catch (Exception ex)
            {
                context.Log.Error("Auto-reorder could not declare its settings; the feature stays off.", ex);
            }
        }
    }

    private static void Enable(ModuleContext context)
    {
        lock (Gate)
        {
            if (_engine is not null)
                return;

            if (_settings is null)
            {
                Declare(context);

                if (_settings is null)
                    return;
            }

            var settings = _settings;
            var engine = new AutoReorderEngine(settings);
            var ui = new AutoReorderUi(engine, settings);

            _engine = engine;
            _ui = ui;
            _pumpErrors = 0;

            // Everything below is undone by the module's own enable/disable cycle: Lifetime is
            // disposed in reverse order on disable, so the pump stops before the objects it drives go.
            context.Lifetime.OnDispose(Detach);
            context.Lifetime.Add(ProbeRegistry.Register(AutoReorderProbe.Create(settings, () => _engine, () => _ui)));
            context.Lifetime.Add(ActionRegistry.RegisterAll(AutoReorderActions.All(settings, () => _engine)));

            _onSaveLoaded = () => _engine?.Rebind();
            AutoReorderState.Loaded += _onSaveLoaded;

            _pump = Pump;
            MelonEvents.OnUpdate.Subscribe(_pump);

            context.Log.Msg("Auto-reorder is on: tick the box on a row in the phone's Deliveries app to repeat it.");
        }
    }

    /// <summary>Stops everything this feature does. Idempotent — disable can reach it twice.</summary>
    private static void Detach()
    {
        lock (Gate)
        {
            if (_pump is not null)
            {
                MelonEvents.OnUpdate.Unsubscribe(_pump);
                _pump = null;
            }

            if (_onSaveLoaded is not null)
            {
                AutoReorderState.Loaded -= _onSaveLoaded;
                _onSaveLoaded = null;
            }

            try
            {
                _ui?.Destroy();
            }
            catch (Exception ex)
            {
                TweakLog.Detail($"Removing the auto-reorder tick boxes threw: {TweakLog.Describe(ex)}");
            }

            _ui = null;
            _engine?.Stop();
            _engine = null;
        }
    }

    /// <summary>
    /// One frame's work. The engine is rate-limited to once a second and the UI to about seven times
    /// a second internally, so this is a pair of clock comparisons on almost every frame.
    /// </summary>
    private static void Pump()
    {
        var engine = _engine;
        var ui = _ui;

        if (engine is null)
            return;

        try
        {
            engine.Tick();
            ui?.Tick();
            _pumpErrors = 0;
        }
        catch (Exception ex)
        {
            _pumpErrors++;

            if (_pumpErrors <= MaxPumpErrors)
                TweakLog.Error($"Auto-reorder's frame hook threw ({_pumpErrors}/{MaxPumpErrors}).", ex);

            if (_pumpErrors < MaxPumpErrors)
                return;

            TweakLog.Warn(
                "Switching auto-reorder off for this session after repeated errors. " +
                "The ticks you set are still saved and will be honoured next launch.");

            Detach();
        }
    }
}

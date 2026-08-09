using Expansions.Core.Configuration;
using Expansions.Core.Logging;

namespace Expansions.Core;

/// <summary>
/// Everything a module is handed at registration. <see cref="Harmony"/> and <see cref="Lifetime"/>
/// are replaced on every enable, so disabling reverses exactly what that cycle created.
/// </summary>
public sealed class ModuleContext
{
    /// <summary>A module that throws this many times in a frame hook is switched off rather than
    /// spamming the log at 60 Hz.</summary>
    private const int MaxPhaseErrors = 10;

    private int _phaseErrors;

    internal ModuleContext(IExpansionModule module, ModuleLogger log, ModuleConfig config)
    {
        Module = module;
        Log = log;
        Config = config;
        HarmonyId = $"com.evan.expansions.{module.Id}";
        Harmony = new HarmonyLib.Harmony(HarmonyId);
        Lifetime = new ModuleLifetime(log);
        EnabledSetting = config.Bind("enabled", true, "Enabled", $"Load the {module.DisplayName} expansion.");
    }

    public IExpansionModule Module { get; }

    public ModuleLogger Log { get; }

    public ModuleConfig Config { get; }

    /// <summary>Persisted toggle. Writing it drives the module through enable/disable.</summary>
    public ConfigValue<bool> EnabledSetting { get; }

    public string HarmonyId { get; }

    /// <summary>
    /// Scoped to the current enable cycle and keyed on the module id, so <c>UnpatchSelf()</c> only
    /// ever removes this module's patches. Modules must apply every patch through this instance;
    /// see <see cref="ExpansionModule.Harmony"/> for why attribute auto-patching is not an option.
    /// </summary>
    public HarmonyLib.Harmony Harmony { get; private set; }

    /// <summary>Disposed on disable. See <see cref="ModuleLifetime"/>.</summary>
    public ModuleLifetime Lifetime { get; private set; }

    /// <summary>True between a successful <c>OnEnabled</c> and the matching <c>OnDisabled</c>.</summary>
    public bool IsActive { get; internal set; }

    internal Action<bool, bool>? EnabledChangedHandler { get; set; }

    /// <summary>
    /// Set while the registry is driving this module through a toggle. Persisting the new value
    /// fires the config entry's change event on the same stack, which comes straight back into the
    /// registry; this is what stops that from re-entering the enable/disable path.
    /// </summary>
    internal bool IsApplyingToggle { get; set; }

    internal void BeginCycle()
    {
        _phaseErrors = 0;
        Harmony = new HarmonyLib.Harmony(HarmonyId);
        Lifetime = new ModuleLifetime(Log);
    }

    internal void EndCycle()
    {
        Lifetime.Dispose();

        try
        {
            Harmony.UnpatchSelf();
        }
        catch (Exception ex)
        {
            Log.Error("Harmony.UnpatchSelf() failed; some patches may still be live.", ex);
        }
    }

    internal void ReportPhaseError(string phase, Exception ex)
    {
        _phaseErrors++;

        if (_phaseErrors <= MaxPhaseErrors)
            Log.Error($"{phase} threw ({_phaseErrors}/{MaxPhaseErrors}).", ex);

        if (_phaseErrors < MaxPhaseErrors)
            return;

        Log.Error($"Switching '{Module.Id}' off after {MaxPhaseErrors} errors. The saved toggle stays on, so it retries next launch.");
        ExpansionRegistry.SetEnabled(Module.Id, false, persist: false);
    }
}

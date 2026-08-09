using Expansions.Core.Configuration;
using Expansions.Core.Logging;

namespace Expansions.Core;

/// <summary>
/// Base class for expansion modules. Override only the hooks you need; the rest are no-ops that the
/// fan-out skips cheaply.
/// </summary>
public abstract class ExpansionModule : IExpansionModule
{
    private ModuleContext? _context;

    public abstract string Id { get; }

    public abstract string DisplayName { get; }

    public abstract string Description { get; }

    public virtual string Version => "0.1.0";

    /// <summary>
    /// Defaults to the PascalCase form of <see cref="Id"/> plus the community's
    /// <c>_01_Main</c> suffix, so <c>hireable_drivers</c> becomes <c>HireableDrivers_01_Main</c>.
    /// Override with <c>ExpansionConfig.CategoryFor("...")</c> when the module presents under a
    /// different name than its id — the two are deliberately not coupled.
    /// </summary>
    public virtual string ConfigCategory => ExpansionConfig.CategoryForId(Id);

    public bool IsEnabled => _context is { IsActive: true };

    protected ModuleContext Context =>
        _context ?? throw new InvalidOperationException($"Module '{Id}' used its context before registration.");

    protected ModuleLogger Log => Context.Log;

    protected ModuleConfig Config => Context.Config;

    /// <summary>
    /// Patch here on enable; the registry unpatches this instance on disable.
    /// <para>
    /// This is the only supported way to patch. Attribute auto-patching must never be relied on:
    /// MelonLoader runs <c>PatchAll</c> over each melon assembly under the melon's own Harmony id,
    /// so an <c>[HarmonyPatch]</c> class would outlive a disable. Every expansion mod assembly
    /// carries <c>[assembly: HarmonyDontPatchAll]</c> to switch that off, which means patches only
    /// exist if they are applied explicitly through this instance — not through
    /// <c>MelonMod.HarmonyInstance</c> and not through a hand-made <c>Harmony</c>.
    /// </para>
    /// </summary>
    protected HarmonyLib.Harmony Harmony => Context.Harmony;

    /// <summary>Register teardown here so <see cref="OnDisabled"/> stays empty.</summary>
    protected ModuleLifetime Lifetime => Context.Lifetime;

    void IExpansionModule.OnRegistered(ModuleContext context)
    {
        _context = context;
        OnRegistered();
    }

    /// <summary>
    /// Runs once, before the first enable, with the context live. Declare settings here — not in
    /// <see cref="OnEnabled"/>, which runs again on every toggle.
    /// </summary>
    protected virtual void OnRegistered()
    {
    }

    public virtual void OnEnabled()
    {
    }

    public virtual void OnDisabled()
    {
    }

    public virtual void OnUpdate()
    {
    }

    public virtual void OnFixedUpdate()
    {
    }

    public virtual void OnLateUpdate()
    {
    }

    public virtual void OnGui()
    {
    }

    public virtual void OnSceneLoaded(int buildIndex, string sceneName)
    {
    }

    public virtual void OnSceneUnloaded(int buildIndex, string sceneName)
    {
    }

    public virtual void OnApplicationQuit()
    {
    }
}

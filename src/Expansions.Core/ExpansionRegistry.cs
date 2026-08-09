using System.Reflection;
using Expansions.Core.Configuration;
using Expansions.Core.Logging;
using MelonLoader;

namespace Expansions.Core;

/// <summary>
/// Process-wide list of expansion modules. Mods register from <c>OnInitializeMelon</c> in whatever
/// order MelonLoader loads them; the registry sorts by display name so the menu is stable regardless.
/// </summary>
public static class ExpansionRegistry
{
    private static readonly Dictionary<string, ModuleContext> ById = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    private static ModuleContext[] _contexts = Array.Empty<ModuleContext>();
    private static IExpansionModule[] _modules = Array.Empty<IExpansionModule>();

    public static event Action<IExpansionModule>? ModuleRegistered;

    public static event Action<IExpansionModule, bool>? ModuleToggled;

    public static int Count => _contexts.Length;

    /// <summary>Ordered by display name. Backed by a snapshot, so it is safe to hold across a toggle.</summary>
    public static IReadOnlyList<IExpansionModule> Modules => _modules;

    /// <summary>Same ordering as <see cref="Modules"/>, but with the toggle state the menu needs.</summary>
    public static IReadOnlyList<ModuleContext> Contexts => _contexts;

    /// <summary>Rebuilt on registration only, so the per-frame fan-out can index it without copying.</summary>
    internal static ModuleContext[] Snapshot => _contexts;

    /// <summary>
    /// Adds a module and, if its persisted toggle is on, enables it immediately. Duplicate ids and
    /// unusable config categories are rejected with a warning rather than throwing, so one bad mod
    /// can't take the others down.
    /// </summary>
    public static bool Register(IExpansionModule module)
    {
        if (module is null)
            throw new ArgumentNullException(nameof(module));

        var id = module.Id;
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Module Id must be a non-empty, stable string.", nameof(module));

        var category = module.ConfigCategory;
        if (string.IsNullOrWhiteSpace(category))
            throw new ArgumentException("Module ConfigCategory must be a non-empty, stable string.", nameof(module));

        ExpansionHost.EnsureInitialized();

        ModuleContext context;
        lock (Gate)
        {
            if (ById.TryGetValue(id, out var existing))
            {
                ExpansionHost.Log.Warn(
                    $"Module id '{id}' is already held by '{existing.Module.DisplayName}'; ignoring the duplicate from '{module.DisplayName}'.");
                return false;
            }

            if (!IsCategoryUsable(id, category))
                return false;

            context = new ModuleContext(
                module,
                new ModuleLogger(id),
                new ModuleConfig(category, module.DisplayName));

            ById[id] = context;
            Rebuild();
        }

        WarnIfAutoPatched(module);

        var registrationFailed = false;
        try
        {
            module.OnRegistered(context);
        }
        catch (Exception ex)
        {
            registrationFailed = true;
            context.Log.Error("OnRegistered threw; the module stays listed but will not be enabled.", ex);
        }

        // Applies the toggle live, whoever wrote it: our menu, a third-party settings app editing
        // the same MelonPreferences entry, or a hand edit to the .cfg the loader reloads.
        context.EnabledChangedHandler = (_, wanted) => SetEnabled(id, wanted, persist: false);
        context.EnabledSetting.Changed += context.EnabledChangedHandler;

        ModuleRegistered?.Invoke(module);

        if (!registrationFailed && context.EnabledSetting.Value)
            SetEnabled(id, true, persist: false);

        return true;
    }

    /// <summary>Disables and drops a module. Call from <c>OnDeinitializeMelon</c>.</summary>
    public static bool Unregister(string id)
    {
        ModuleContext? context;
        lock (Gate)
        {
            if (!ById.TryGetValue(id, out context))
                return false;

            ById.Remove(id);
            Rebuild();
        }

        if (context.EnabledChangedHandler is not null)
        {
            context.EnabledSetting.Changed -= context.EnabledChangedHandler;
            context.EnabledChangedHandler = null;
        }

        Apply(context, false, persist: false);
        return true;
    }

    public static bool IsRegistered(string id) => ContextOf(id) is not null;

    public static IExpansionModule? Find(string id) => ContextOf(id)?.Module;

    public static ModuleContext? ContextOf(string id)
    {
        if (string.IsNullOrEmpty(id))
            return null;

        lock (Gate)
            return ById.TryGetValue(id, out var context) ? context : null;
    }

    public static bool IsEnabled(string id) => ContextOf(id) is { IsActive: true };

    public static bool Enable(string id) => SetEnabled(id, true);

    public static bool Disable(string id) => SetEnabled(id, false);

    public static bool Toggle(string id) => SetEnabled(id, !IsEnabled(id));

    /// <summary>Applies the toggle and persists it. Returns false if the id is unknown or the
    /// module refused to enable.</summary>
    public static bool SetEnabled(string id, bool enabled) => SetEnabled(id, enabled, persist: true);

    internal static bool SetEnabled(string id, bool enabled, bool persist)
    {
        var context = ContextOf(id);
        if (context is null)
        {
            ExpansionHost.Log.Warn($"No module registered with id '{id}'.");
            return false;
        }

        return Apply(context, enabled, persist);
    }

    private static bool Apply(ModuleContext context, bool enabled, bool persist)
    {
        // Persisting sets the entry, which fires its change event on this same stack and lands back
        // here through EnabledChangedHandler. The module already holds the requested state by then,
        // so the re-entrant call has nothing left to do.
        if (context.IsApplyingToggle)
            return true;

        context.IsApplyingToggle = true;
        try
        {
            var transitioned = context.IsActive != enabled;

            if (transitioned && !Transition(context, enabled))
                return false;

            if (persist)
                context.EnabledSetting.Value = enabled;

            if (!transitioned)
                return true;

            context.Log.Msg(enabled ? "Enabled." : "Disabled.");
            ModuleToggled?.Invoke(context.Module, enabled);
            return true;
        }
        finally
        {
            context.IsApplyingToggle = false;
        }
    }

    /// <summary>Runs the module's enable or disable cycle. False means the enable failed.</summary>
    private static bool Transition(ModuleContext context, bool enabled)
    {
        if (enabled)
        {
            context.BeginCycle();

            try
            {
                context.Module.OnEnabled();
            }
            catch (Exception ex)
            {
                context.Log.Error("OnEnabled threw; rolling back to disabled.", ex);
                context.EndCycle();
                return false;
            }

            context.IsActive = true;
            return true;
        }

        context.IsActive = false;

        try
        {
            context.Module.OnDisabled();
        }
        catch (Exception ex)
        {
            context.Log.Error("OnDisabled threw; continuing teardown.", ex);
        }

        context.EndCycle();
        return true;
    }

    /// <summary>
    /// A category identifier reaches the .cfg as a raw TOML table key. A dot in it reads as a table
    /// path, which nests the module inside the parent table and silently discards its entries; a
    /// duplicate would leave two modules sharing one 'enabled' switch.
    /// </summary>
    private static bool IsCategoryUsable(string id, string category)
    {
        if (category.Contains('.'))
        {
            ExpansionHost.Log.Warn(
                $"Module '{id}' asked for config category '{category}'. Dots are read as TOML table paths and the module's settings would never be written; skipping registration.");
            return false;
        }

        if (string.Equals(category, ExpansionConfig.CategoryId, StringComparison.OrdinalIgnoreCase))
        {
            ExpansionHost.Log.Warn(
                $"Module '{id}' asked for config category '{category}', which is the shared category; skipping registration.");
            return false;
        }

        foreach (var other in ById.Values)
        {
            if (!string.Equals(other.Config.Identifier, category, StringComparison.OrdinalIgnoreCase))
                continue;

            ExpansionHost.Log.Warn(
                $"Module '{id}' asked for config category '{category}', already held by '{other.Module.Id}'; skipping registration.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// MelonLoader runs <c>Harmony.PatchAll</c> over every melon assembly under the melon's own
    /// Harmony id, which the per-module <c>UnpatchSelf()</c> cannot reverse.
    /// <c>[assembly: HarmonyDontPatchAll]</c> switches that off; without it a disabled module keeps
    /// patching the game.
    /// </summary>
    private static void WarnIfAutoPatched(IExpansionModule module)
    {
        var assembly = module.GetType().Assembly;

        if (assembly.GetCustomAttribute<MelonInfoAttribute>() is null)
            return;

        if (assembly.GetCustomAttribute<HarmonyDontPatchAllAttribute>() is not null)
            return;

        ExpansionHost.Log.Warn(
            $"'{assembly.GetName().Name}' is a melon assembly without [assembly: HarmonyDontPatchAll], so MelonLoader will apply every [HarmonyPatch] class in it under its own Harmony id. Disabling '{module.Id}' cannot undo those patches.");
    }

    private static void Rebuild()
    {
        var contexts = new ModuleContext[ById.Count];
        ById.Values.CopyTo(contexts, 0);
        Array.Sort(contexts, static (a, b) => string.CompareOrdinal(a.Module.DisplayName, b.Module.DisplayName));

        var modules = new IExpansionModule[contexts.Length];
        for (var i = 0; i < contexts.Length; i++)
            modules[i] = contexts[i].Module;

        _contexts = contexts;
        _modules = modules;
    }
}

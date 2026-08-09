using Expansions.Core.Actions;
using Expansions.Core.Configuration;
using Expansions.Core.Diagnostics;
using Expansions.Core.Diagnostics.Probes;
using Expansions.Core.Events;
using Expansions.Core.Game;
using Expansions.Core.Logging;
using Expansions.Core.Tutorial;
using Expansions.Core.UI;
using Expansions.Core.Updates;
using UnityEngine;

namespace Expansions.Core;

/// <summary>
/// Drives the shared per-frame and per-scene work.
/// <para>
/// Core is a plain library loaded from <c>UserLibs</c>, so it has no MelonMod of its own. Each
/// expansion mod forwards its MelonMod callbacks here; the first caller becomes the driver and the
/// rest are ignored, so N installed mods still produce exactly one fan-out per frame. If the driver
/// goes away the next caller takes over.
/// </para>
/// </summary>
public static class ExpansionHost
{
    private static readonly object Gate = new();

    private static object? _driver;
    private static bool _initialized;

    public static ModuleLogger Log { get; } = new("Core");

    public static bool IsInitialized => _initialized;

    public static bool HasDriver => _driver is not null;

    /// <summary>Idempotent. Call from every mod's <c>OnInitializeMelon</c> before registering.</summary>
    public static void EnsureInitialized()
    {
        lock (Gate)
        {
            if (_initialized)
                return;

            _initialized = true;
        }

        ExpansionConfig.Initialize();

        try
        {
            GameSave.Initialize();
        }
        catch (Exception ex)
        {
            Log.Error("Autosave-on-quit could not be hooked; everything else still works.", ex);
        }

        try
        {
            CoreProbes.RegisterAll();
        }
        catch (Exception ex)
        {
            Log.Error("The diagnostics probes could not be registered; everything else still works.", ex);
        }

        try
        {
            TutorialDirector.Initialize();
        }
        catch (Exception ex)
        {
            Log.Error("The tutorial quest line could not be initialised; everything else still works.", ex);
        }

        try
        {
            CoreEvents.RegisterAll();
        }
        catch (Exception ex)
        {
            Log.Error("Core's triggerable events could not be registered; everything else still works.", ex);
        }

        try
        {
            CoreActions.RegisterAll();
        }
        catch (Exception ex)
        {
            Log.Error("Core's menu actions could not be registered; everything else still works.", ex);
        }

        // Reported, not driven. Updating is the Expansions.Updater plugin's job and it has already
        // finished by the time any mod initialises - plugins load before mods, which is the only window
        // in which a mod DLL can be replaced. All Core does is read what the plugin wrote.
        try
        {
            Log.Msg($"Updates: {UpdateReport.Current.StatusLine}");
        }
        catch (Exception ex)
        {
            Log.Error("The updater's status could not be read; everything else still works.", ex);
        }

        Log.Msg(
            $"Ready. Settings: {ExpansionConfig.FilePath}. Open the screen from the Expansions entry in the " +
            $"main menu, or with {ExpansionConfig.MenuHotkey} in-game.");
        Log.Msg(
            $"Diagnostics: {ProbeRegistry.Count} probe(s). Run them from the Actions tab, with " +
            $"{ExpansionConfig.ProbeHotkey} while the menu is open, or with the console command " +
            $"'{ProbeRunner.CommandWord}'.");
    }

    /// <summary>Claims the frame pump. Returns true if <paramref name="owner"/> now owns it.</summary>
    public static bool TryBecomeDriver(object owner)
    {
        if (owner is null)
            throw new ArgumentNullException(nameof(owner));

        lock (Gate)
        {
            if (_driver is not null)
                return ReferenceEquals(_driver, owner);

            _driver = owner;
        }

        return true;
    }

    public static bool IsDriver(object? owner) => owner is not null && ReferenceEquals(_driver, owner);

    public static void ReleaseDriver(object owner)
    {
        lock (Gate)
        {
            if (ReferenceEquals(_driver, owner))
                _driver = null;
        }
    }

    /// <summary>Release the pump. Call from <c>OnDeinitializeMelon</c>.</summary>
    public static void Shutdown(object owner)
    {
        var wasDriver = IsDriver(owner);
        ReleaseDriver(owner);

        if (!wasDriver)
            return;

        try
        {
            TutorialDirector.Shutdown();
        }
        catch (Exception ex)
        {
            Log.Error("The tutorial did not shut down cleanly.", ex);
        }

        try
        {
            EventHotkey.Shutdown();
            CoreEvents.Unregister();
        }
        catch (Exception ex)
        {
            Log.Error("The event hotkey did not shut down cleanly.", ex);
        }

        try
        {
            CoreActions.Unregister();
        }
        catch (Exception ex)
        {
            Log.Error("Core's menu actions did not unregister cleanly.", ex);
        }

        try
        {
            GameSave.Shutdown();
        }
        catch (Exception ex)
        {
            Log.Error("Autosave-on-quit did not unhook cleanly.", ex);
        }
    }

    public static void Update(object owner)
    {
        if (!ShouldPump(owner))
            return;

        var hotkey = ExpansionConfig.MenuHotkey;
        if (hotkey != KeyCode.None && Input.GetKeyDown(hotkey))
            ExpansionMenu.Toggle();

        PumpMenu(static menu => menu.OnUpdate(), "OnUpdate");

        // After the menu, so a hotkey press that just opened the Expansions screen is seen as
        // "the screen owns the keyboard now" rather than as a request to open the event chooser.
        EventHotkey.Tick();

        TutorialDirector.Tick();
        GameSave.Tick();
        Fanout(static module => module.OnUpdate(), "OnUpdate");
    }

    public static void FixedUpdate(object owner)
    {
        if (!ShouldPump(owner))
            return;

        Fanout(static module => module.OnFixedUpdate(), "OnFixedUpdate");
    }

    public static void LateUpdate(object owner)
    {
        if (!ShouldPump(owner))
            return;

        Fanout(static module => module.OnLateUpdate(), "OnLateUpdate");
    }

    public static void Gui(object owner)
    {
        if (!ShouldPump(owner))
            return;

        Fanout(static module => module.OnGui(), "OnGui");
        PumpMenu(static menu => menu.OnGui(), "OnGui");
    }

    public static void SceneWasLoaded(object owner, int buildIndex, string sceneName)
    {
        if (!ShouldPump(owner))
            return;

        var contexts = ExpansionRegistry.Snapshot;
        for (var i = 0; i < contexts.Length; i++)
        {
            var context = contexts[i];
            if (!context.IsActive)
                continue;

            try
            {
                context.Module.OnSceneLoaded(buildIndex, sceneName);
            }
            catch (Exception ex)
            {
                context.ReportPhaseError("OnSceneLoaded", ex);
            }
        }

        PumpMenu(menu => menu.OnSceneChanged(buildIndex, sceneName), "OnSceneChanged");

        // After the menu, which is what clears the shared font/sprite caches the chooser also uses.
        try
        {
            EventHotkey.OnSceneChanged();
        }
        catch (Exception ex)
        {
            Log.Error("The event chooser's scene-change handler threw; it rebuilds on next use.", ex);
        }

        try
        {
            TutorialDirector.OnSceneChanged(sceneName);
        }
        catch (Exception ex)
        {
            Log.Error("The tutorial's scene-change handler threw; the quest line may need restarting.", ex);
        }
    }

    public static void SceneWasUnloaded(object owner, int buildIndex, string sceneName)
    {
        if (!ShouldPump(owner))
            return;

        var contexts = ExpansionRegistry.Snapshot;
        for (var i = 0; i < contexts.Length; i++)
        {
            var context = contexts[i];
            if (!context.IsActive)
                continue;

            try
            {
                context.Module.OnSceneUnloaded(buildIndex, sceneName);
            }
            catch (Exception ex)
            {
                context.ReportPhaseError("OnSceneUnloaded", ex);
            }
        }
    }

    public static void ApplicationQuit(object owner)
    {
        if (!ShouldPump(owner))
            return;

        try
        {
            GameSave.OnApplicationQuit();
        }
        catch (Exception ex)
        {
            Log.Error("Autosave-on-quit threw during ApplicationQuit; exit continues.", ex);
        }

        Fanout(static module => module.OnApplicationQuit(), "OnApplicationQuit");
    }

    private static bool ShouldPump(object owner)
    {
        if (ReferenceEquals(_driver, owner))
            return true;

        return _driver is null && TryBecomeDriver(owner);
    }

    private static void Fanout(Action<IExpansionModule> phase, string phaseName)
    {
        var contexts = ExpansionRegistry.Snapshot;
        for (var i = 0; i < contexts.Length; i++)
        {
            var context = contexts[i];
            if (!context.IsActive)
                continue;

            try
            {
                phase(context.Module);
            }
            catch (Exception ex)
            {
                context.ReportPhaseError(phaseName, ex);
            }
        }
    }

    private static void PumpMenu(Action<IExpansionMenu> phase, string phaseName)
    {
        var menu = ExpansionMenu.Current;

        try
        {
            phase(menu);
        }
        catch (Exception ex)
        {
            Log.Error($"Menu '{menu.Name}' {phaseName} threw.", ex);
        }
    }
}

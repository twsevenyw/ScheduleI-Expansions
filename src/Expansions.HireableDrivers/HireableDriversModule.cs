using Expansions.Core;
using Expansions.Core.Diagnostics;
using Expansions.Core.Tutorial;
using Expansions.HireableDrivers.Config;
using Expansions.HireableDrivers.Diagnostics;
using Expansions.HireableDrivers.Patches;
using Expansions.HireableDrivers.Persistence;
using Expansions.HireableDrivers.Runtime;
using Expansions.HireableDrivers.Tutorial;
using Expansions.HireableDrivers.UI;
using UnityEngine;

namespace Expansions.HireableDrivers;

/// <summary>
/// Driver employees who move goods between properties, businesses and dealers.
/// <para>
/// A driver is a real vanilla <c>Packager</c> whose packaging brain is suppressed and whose movement a
/// managed state machine owns. Everything the mod touches on the game is late-bound by name, so a
/// renamed type costs a feature and a logged warning rather than the melon's load — which matters
/// because <c>Expansions.Core</c> is shared with two sibling mods.
/// </para>
/// </summary>
public sealed class HireableDriversModule : ExpansionModule
{
    public const string ModuleId = "hireable_drivers";

    private const string GameplayScene = "Main";

    private Action? _loadedHandler;
    private bool _inGameplayScene;

    public override string Id => ModuleId;

    public override string DisplayName => "Hireable Drivers";

    public override string Description =>
        "Driver employees who can transport items between your properties, businesses, and dealers.";

    public override string Version => "0.7.5";

    protected override void OnRegistered()
    {
        // Runs once, before the first enable. The settings have to exist before anything reads them,
        // including the static mirror the registry and the patch bodies use.
        DriverSettings.Bind(Config);
        Log.Debug("Registered.");
    }

    public override void OnEnabled()
    {
        // A failure anywhere below must cost this module and nothing else: Core and the other two mods
        // share this process, and a driver that cannot drive is still better than three dead mods.
        Try("applying patches", () => DriverPatches.Apply(Harmony, Lifetime));

        Try("attaching the save store", () =>
        {
            _loadedHandler = OnDriverDataLoaded;
            DriverSaveStore.Loaded += _loadedHandler;
            Lifetime.OnDispose(() =>
            {
                DriverSaveStore.Loaded -= _loadedHandler;
                _loadedHandler = null;
            });
        });

        Try("registering probes", () =>
        {
            foreach (var probe in DriverProbes.All())
                Lifetime.Add(ProbeRegistry.Register(probe));

            foreach (var note in DriverProbes.Notes())
                Lifetime.Add(MutatingTestCatalogue.Register(note));
        });

        Try("registering the tutorial chapter", () => Lifetime.Add(TutorialRegistry.Register(new DriversChapter())));

        Try("registering menu actions", () => DriverActions.Register(Lifetime));

        Try("attaching the drivers diagnostic panel", () =>
        {
            DriverPanel.Attach();
            Lifetime.OnDispose(DriverPanel.Detach);
        });

        // The Harmony hooks come off automatically; this clears per-conversation selection state.
        Lifetime.OnDispose(HiringDesk.Detach);

        // Returns every driver to being an ordinary Handler: patches come off with the Harmony
        // instance, and this drops the brains, the vehicle claims and the slot locks.
        Lifetime.OnDispose(DriverRegistry.SuspendAndClear);

        // Enabling mid-session must pick up drivers the save already loaded.
        _inGameplayScene = string.Equals(GameReflection.ActiveSceneName(), GameplayScene, StringComparison.Ordinal);

        if (_inGameplayScene)
        {
            Try("expanding property backing slots for drivers", DriverPropertyCapacity.EnsureAll);
            Try("binding Driver into the Fixer's native hiring flow", HiringDesk.Attach);
            Log.Msg($"Hiring desk after enable: {HiringDesk.StatusLine}");
        }

        Try("binding existing drivers", BindExisting);

        Log.Msg($"Enabled. Diagnostics: {DriverSettings.PanelHotkey}. Transport mode: {DriverSettings.Mode}.");
    }

    /// <summary>
    /// Deliberately empty. Everything reversible is registered with <see cref="ExpansionModule.Lifetime"/>,
    /// and the patches come off with the module's own Harmony instance.
    /// <para>
    /// The employees themselves are the deliberate exception: they are real vanilla employees created by
    /// the game's own factory and persisted by the game's own save path, so disabling the module stops
    /// them driving without unmaking them. They revert to ordinary Handlers with no stations and no
    /// routes, which is the right uninstall behaviour — the save still loads and the NPCs still work.
    /// </para>
    /// </summary>
    public override void OnDisabled()
    {
    }

    public override void OnUpdate()
    {
        DriverPanel.OnUpdate();

        if (!_inGameplayScene)
            return;

        // A clipboard pick has to apply on the frame after the option screen closes, or the shipped
        // worldspace picker would be opened while that screen is still sliding out.
        RoutePicker.Pump();

        // Pump throttles itself to the configured tick interval and gates on authority and the world
        // clock, so this costs one float add per frame outside of a tick.
        DriverRegistry.Pump(Time.deltaTime);
    }

    public override void OnGui() => DriverPanel.OnGui();

    public override void OnSceneLoaded(int buildIndex, string sceneName)
    {
        if (!string.Equals(sceneName, GameplayScene, StringComparison.Ordinal))
            return;

        // Parking lots, storage entities, the hiring NPC and the clock all belong to the scene entered.
        _inGameplayScene = true;
        EndpointCatalog.Invalidate();
        GameClock.Reset();
        Try("expanding property backing slots for drivers", DriverPropertyCapacity.EnsureAll);
        Try("binding Driver into the Fixer's native hiring flow", HiringDesk.Attach);
        Log.Msg($"Hiring desk on save/scene load: {HiringDesk.StatusLine}");
        Try("binding existing drivers", BindExisting);
    }

    public override void OnSceneUnloaded(int buildIndex, string sceneName)
    {
        if (!string.Equals(sceneName, GameplayScene, StringComparison.Ordinal))
            return;

        // Every wrapper the registry holds points at objects that are about to be destroyed.
        _inGameplayScene = false;
        DriverRegistry.Clear();
        HiringDesk.Detach();
        EndpointCatalog.Invalidate();
        DriverPropertyCapacity.ForgetAll();
    }

    /// <summary>
    /// Fires once the save's driver blob has been read. The roster is rebuilt from scratch rather than
    /// merged, because the deserialised records are new objects and a stale brain would keep editing the
    /// previous save's copy.
    /// </summary>
    private void OnDriverDataLoaded()
    {
        DriverRegistry.Clear();
        BindExisting();
    }

    private void BindExisting()
    {
        var records = DriverStore.Records;
        if (records.Count == 0)
            return;

        foreach (var record in records)
            DriverStore.EnsureRouteSlots(record);

        DriverRegistry.RebindAll(records);
    }

    private void Try(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Error($"Hireable Drivers failed while {what}; the rest of the module carries on.", ex);
        }
    }
}

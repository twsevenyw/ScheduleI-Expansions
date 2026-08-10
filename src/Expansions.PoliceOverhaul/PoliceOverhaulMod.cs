using Expansions.Core;
using MelonLoader;

[assembly: MelonInfo(typeof(Expansions.PoliceOverhaul.PoliceOverhaulMod), "Police Improvements", "0.5.3", "Evan")]
[assembly: MelonGame("TVGS", "Schedule I")]
[assembly: MelonColor(255, 240, 110, 90)]
[assembly: MelonPlatformDomain(MelonPlatformDomainAttribute.CompatibleDomains.IL2CPP)]

// Without this MelonLoader runs Harmony.PatchAll over this assembly under the melon's own Harmony
// id, and the module's UnpatchSelf() could never undo those patches. Modules patch exclusively
// through their per-module Harmony instance; see ExpansionModule.Harmony.
[assembly: HarmonyDontPatchAll]

namespace Expansions.PoliceOverhaul;

/// <summary>
/// MelonLoader entry point. Nothing but wiring: the module holds the behaviour and Expansions.Core
/// (loaded from UserLibs) fans these callbacks out to whichever modules are enabled.
/// </summary>
public sealed class PoliceOverhaulMod : MelonMod
{
    private readonly PoliceOverhaulModule _module = new();

    public override void OnInitializeMelon()
    {
        ExpansionHost.EnsureInitialized();
        ExpansionHost.TryBecomeDriver(this);
        ExpansionRegistry.Register(_module);
    }

    public override void OnDeinitializeMelon()
    {
        ExpansionRegistry.Unregister(_module.Id);
        ExpansionHost.Shutdown(this);
    }

    public override void OnUpdate() => ExpansionHost.Update(this);

    public override void OnFixedUpdate() => ExpansionHost.FixedUpdate(this);

    public override void OnLateUpdate() => ExpansionHost.LateUpdate(this);

    public override void OnGUI() => ExpansionHost.Gui(this);

    public override void OnApplicationQuit() => ExpansionHost.ApplicationQuit(this);

    public override void OnSceneWasLoaded(int buildIndex, string sceneName) =>
        ExpansionHost.SceneWasLoaded(this, buildIndex, sceneName);

    public override void OnSceneWasUnloaded(int buildIndex, string sceneName) =>
        ExpansionHost.SceneWasUnloaded(this, buildIndex, sceneName);
}

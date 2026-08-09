using System.Reflection;
using Expansions.Core.Diagnostics;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Expansions.PoliceOverhaul.Runtime;

/// <summary>
/// Plain-clothes federal agents: instances of the game's own <c>PoliceOfficer</c> that this module
/// owns outright.
/// <para>
/// Never a new type and never a subclass. FishNet's code generator runs at build time and never sees
/// a mod assembly, so a derived <c>NetworkBehaviour</c> would have no RPC links, no SyncVars and no
/// spawnable prefab id — it would look fine in the editor and replicate nothing. Agents are also
/// never added to <c>PoliceStation.OfficerPool</c>, or the vanilla dispatcher would start sending a
/// federal agent to jaywalking calls and never get it back.
/// </para>
/// <para>
/// This is the riskiest pillar in the mod, so it is built to fail politely: <see cref="Survey"/>
/// decides read-only whether spawning is possible at all, everything else refuses to run when it is
/// not, and the reason is reported to the probe and the menu instead of thrown.
/// </para>
/// <para>
/// Clones used to keep the donor officer's <c>NPCData</c> reference, so <c>GetNPC(donorId)</c> and any
/// messaging through the agent attributed events to that civilian/officer (playtest: LeRoy). After
/// instantiate we deep-copy <c>NPCData</c>, stamp a unique id/name/GUID, and never send texts from
/// agents — Dispatch is the only messaging contact.
/// </para>
/// </summary>
internal static class FederalAgents
{
    private const string PrefabName = "PoliceNPC";
    private const string AgentNamePrefix = "ExpansionsFederalAgent_";
    private const string AgentIdPrefix = "expansions_fed_";

    /// <summary>
    /// Tagged by native pointer, not by a marker component. Registering a type with
    /// <c>ClassInjector</c> is process-global and irreversible, which would break the module's
    /// reversible-toggle guarantee for the sake of a dictionary lookup.
    /// </summary>
    private static readonly HashSet<IntPtr> Tagged = new();

    private static readonly List<PendingSpawn> Pending = new();
    private static readonly List<object> Live = new();

    /// <summary>Where each posted agent is supposed to be standing. Empty for a pursuit team.</summary>
    private static readonly Dictionary<IntPtr, Vector3> Posts = new();

    private static int _nextProvisionalObjectId = 30000;

    internal static Viability Status { get; private set; } = Viability.Unknown;

    internal static int LiveCount => Live.Count;

    /// <summary>Raised once per agent, the moment it is active and pursuing.</summary>
    internal static event Action? AgentActivated;

    /// <summary>True for an officer this module spawned. Cheap enough for a per-officer sweep.</summary>
    internal static bool IsAgent(object? officer) =>
        Tagged.Count > 0 && officer is Il2CppObjectBase native && Tagged.Contains(native.Pointer);

    /// <summary>
    /// Works out, without spawning anything, whether this build can produce a federal agent and how.
    /// Called at wiring time and again from the probe, so the answer is always current.
    /// </summary>
    internal static Viability Survey()
    {
        var officerType = GameReflection.FindType(GameTypes.PoliceOfficer);
        if (officerType is null)
        {
            Status = new Viability(false, SpawnStrategy.None, $"'{GameTypes.PoliceOfficer}' is not on this build");
            return Status;
        }

        if (FindPrefabTemplate() is not null)
        {
            Status = new Viability(true, SpawnStrategy.RegisteredPrefab,
                $"'{PrefabName}' is a registered FishNet spawnable, so agents are instantiated from the network prefab");
            return Status;
        }

        var donor = FirstOfficer();
        if (donor is not null)
        {
            Status = new Viability(true, SpawnStrategy.CloneLiveOfficer,
                $"'{PrefabName}' is not in SpawnablePrefabs, so agents are cloned from a live officer instead");
            return Status;
        }

        Status = new Viability(false, SpawnStrategy.None,
            $"'{PrefabName}' is not a registered spawnable and there is no live officer to clone " +
            "(normal outside a loaded save; re-run this from in-game)");
        return Status;
    }

    /// <summary>
    /// Requests agents. Returns how many were queued, which is zero whenever the pillar is not viable
    /// — the caller treats that as "no federal event happened", never as an error.
    /// <para>
    /// <paramref name="postPosition"/> is what separates a chase from a stakeout. With a post, the
    /// agents never start a pursuit: they walk to the spot and stay there, and because an outlawed
    /// player is always eligible to be investigated and searched, walking past them is the event.
    /// </para>
    /// </summary>
    internal static int Spawn(int count, Vector3 position, string targetPlayerCode, Vector3? postPosition = null)
    {
        if (!HostGate.IsAuthority)
            return 0;

        if (!Status.CanSpawn && !Survey().CanSpawn)
        {
            PoliceLog.Warn($"Federal agents unavailable: {Status.Reason}.");
            return 0;
        }

        var queued = 0;
        for (var i = 0; i < count; i++)
        {
            var offset = Vector3.right * (i * 1.2f);
            var officer = Create(position + offset);
            if (officer is null)
                continue;

            Pending.Add(new PendingSpawn(officer, targetPlayerCode, postPosition is { } post ? post + offset : null));
            queued++;
        }

        if (queued > 0)
        {
            PoliceLog.Msg($"Queued {queued} federal agent(s) via the {Status.Strategy} path" +
                          (postPosition is null ? " to pursue." : " to hold a position."));
        }

        return queued;
    }

    /// <summary>
    /// Keeps posted agents on their post. Called from the minute tick, because an NPC that finishes a
    /// walk simply stands there and the game's idle behaviours will eventually wander it off.
    /// </summary>
    internal static void HoldPosts()
    {
        if (Posts.Count == 0)
            return;

        foreach (var (pointer, post) in Posts)
        {
            var officer = FindLive(pointer);
            if (officer is null)
                continue;

            var here = Components.TransformOf(officer)?.position;
            if (here is null || (here.Value - post).sqrMagnitude < 9f)
                continue;

            var movement = Members.ReadPath(officer, "Movement");
            if (movement is not null)
                Members.Invoke(movement, "SetDestination", post);
        }
    }

    /// <summary>
    /// Drives the staged activation. Instantiating, activating and network-spawning in the same frame
    /// does not work — the clone's avatar and nav agent populate their unassigned references on their
    /// first enabled frame — so each stage waits a few frames for the one before it.
    /// </summary>
    internal static void Pump()
    {
        if (Pending.Count == 0)
            return;

        for (var i = Pending.Count - 1; i >= 0; i--)
        {
            var pending = Pending[i];
            pending.Frames++;

            try
            {
                if (pending.Frames == 1)
                    SetActive(pending.Officer, true);
                else if (pending.Frames == 20)
                    NetworkSpawn(pending.Officer);
                else if (pending.Frames >= 40)
                {
                    Finalise(pending);
                    Pending.RemoveAt(i);
                }
            }
            catch (Exception ex)
            {
                PoliceLog.Error("A federal agent failed while spawning; abandoning that one.", ex);
                Pending.RemoveAt(i);
                Destroy(pending.Officer);
            }
        }
    }

    /// <summary>Removes every agent this module created. Idempotent and safe half-initialised.</summary>
    internal static void DespawnAll()
    {
        foreach (var pending in Pending)
            Destroy(pending.Officer);

        Pending.Clear();

        var removed = Live.Count;
        foreach (var officer in Live)
            Destroy(officer);

        Live.Clear();
        Tagged.Clear();
        Posts.Clear();

        if (removed > 0)
            PoliceLog.Msg($"Withdrew {removed} federal agent(s).");
    }

    private static object? FindLive(IntPtr pointer)
    {
        foreach (var officer in Live)
        {
            if (Components.PointerOf(officer) == pointer && GameReflection.IsPresent(officer))
                return officer;
        }

        return null;
    }

    /// <summary>
    /// Scene teardown. The GameObjects are already gone, so reaching into them to deactivate and
    /// despawn would be writing to destroyed natives; dropping the references is the whole job.
    /// </summary>
    internal static void Forget()
    {
        Pending.Clear();
        Live.Clear();
        Tagged.Clear();
        Posts.Clear();
        Status = Viability.Unknown;
    }

    // ── Construction ──────────────────────────────────────────────────────────────────────────

    private static object? Create(Vector3 position)
    {
        var template = Status.Strategy == SpawnStrategy.RegisteredPrefab ? FindPrefabTemplate() : null;
        var source = template is not null ? Members.ReadPath(template, "gameObject") : null;

        object? clone;
        if (source is GameObject prefabObject)
        {
            clone = Object.Instantiate(prefabObject, position, Quaternion.identity);
        }
        else
        {
            var donor = FirstOfficer();
            var donorObject = Members.ReadPath(donor, "gameObject") as GameObject;
            if (donorObject is null)
                return null;

            // Cloning while inactive keeps the donor's Awake from running twice on the copy before
            // its network identity has been rebuilt. The finally is not decoration: a throw between
            // the two calls would leave a real officer switched off for the rest of the session,
            // which is one more permanently missing policeman.
            var wasActive = donorObject.activeSelf;
            donorObject.SetActive(false);
            try
            {
                clone = Object.Instantiate(donorObject, position, Quaternion.identity);
            }
            finally
            {
                donorObject.SetActive(wasActive);
            }
        }

        if (clone is not GameObject gameObject)
            return null;

        gameObject.name = AgentNamePrefix + Guid.NewGuid().ToString("N")[..8];

        var officerType = GameReflection.FindType(GameTypes.PoliceOfficer);
        var officer = officerType is null ? null : Components.Get(gameObject, officerType);
        if (officer is null)
        {
            Object.Destroy(gameObject);
            return null;
        }

        if (officer is Il2CppObjectBase native)
            Tagged.Add(native.Pointer);

        // Must run while inactive and before Activate: Awake/registry lookup keys off ID, and a shared
        // NPCData reference would rename the donor (or make GetNPC return this clone as that person).
        if (!Reidentify(officer))
        {
            PoliceLog.Warn("Federal agent clone could not be re-identified; abandoning it rather than leaking a donor id.");
            if (officer is Il2CppObjectBase tagged)
                Tagged.Remove(tagged.Pointer);
            Object.Destroy(gameObject);
            return null;
        }

        return officer;
    }

    /// <summary>
    /// Gives the agent its own <c>NPCData</c>, id, display name and GUID so nothing attributes the
    /// clone to a civilian or to the donor officer.
    /// </summary>
    private static bool Reidentify(object officer)
    {
        var npcData = Members.ReadPath(officer, "NPCData");
        if (npcData is null)
            return false;

        // Never mutate npcData in place: the clone may still share the donor's runtime NPCData, and
        // writing ID there is exactly how a federal spawn renamed a civilian in playtest.
        var copy = Members.InvokeFor(npcData, "GetDeepCopy");
        if (copy is null || ReferenceEquals(copy, npcData))
            return false;

        var basic = Members.ReadPath(copy, "BasicInfo");
        if (basic is null)
            return false;

        var token = Guid.NewGuid().ToString("N")[..8];
        var id = AgentIdPrefix + token;

        if (!Members.TryWrite(basic, "ID", id))
            return false;

        Members.TryWrite(basic, "FirstName", "Federal");
        Members.TryWrite(basic, "HasLastName", true);
        Members.TryWrite(basic, "LastName", "Agent");

        if (!Members.Invoke(officer, "ApplyNPCData", copy))
            return false;

        Members.TryWrite(officer, "BakedGUID", Guid.NewGuid().ToString("D"));
        TrySetNewGuid(officer);

        // Clones must never open a message thread under any name — Dispatch owns all texts.
        Members.TryWrite(officer, "MSGConversation", null);

        PoliceLog.Detail($"Federal agent re-identified as '{id}'.");
        return true;
    }

    private static void TrySetNewGuid(object officer)
    {
        try
        {
            var guidType = GameReflection.FindType("Il2CppSystem.Guid");
            if (guidType is null)
                return;

            object? boxed = null;
            foreach (var method in guidType.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (method.Name != "Parse" || method.GetParameters().Length != 1)
                    continue;

                boxed = method.Invoke(null, new object[] { Guid.NewGuid().ToString("D") });
                break;
            }

            if (boxed is not null)
                Members.Invoke(officer, "SetGUID", boxed);
        }
        catch (Exception ex)
        {
            PoliceLog.Detail($"SetGUID on a federal agent failed: {PoliceLog.Describe(ex)}");
        }
    }

    private static void Finalise(PendingSpawn pending)
    {
        var officer = pending.Officer;

        // Never pooled, never saved, never on the radio: silence is what says "not on the PD net".
        Members.TryWrite(officer, "AutoDeactivate", false);
        Members.TryWrite(officer, "ChatterEnabled", false);
        Members.TryWrite(officer, "BodySearchChance", 1f);

        var cone = Members.ReadPath(officer, "Awareness.VisionCone");
        if (cone is not null)
        {
            Members.TryWrite(cone, "RangeMultiplier", Members.Read(cone, "RangeMultiplier", 1f) * 1.5f);
            Members.TryWrite(cone, "Attentiveness", Members.Read(cone, "Attentiveness", 1f) * 1.5f);
            Members.TryWrite(cone, "Memory", Members.Read(cone, "Memory", 1f) * 2f);
        }

        if (!AgentLook.Apply(officer))
            PoliceLog.Warn("A federal agent spawned but could not be re-dressed; it will look like a uniformed officer.");

        Members.Invoke(officer, "Activate");

        if (pending.PostPosition is { } post)
        {
            Posts[Components.PointerOf(officer)] = post;

            var movement = Members.ReadPath(officer, "Movement");
            if (movement is not null)
                Members.Invoke(movement, "SetDestination", post);
        }
        else if (pending.TargetPlayerCode.Length > 0)
        {
            // includeColleagues: false — a federal agent dragging the local PD into every call is
            // exactly the "why is the whole town chasing me" complaint this pillar has to avoid.
            Members.Invoke(officer, "BeginFootPursuit_Networked", pending.TargetPlayerCode, false);
        }

        Live.Add(officer);
        PoliceLog.Msg(pending.PostPosition is null
            ? $"Federal agent active and tracking '{pending.TargetPlayerCode}'."
            : "Federal agent active and holding a position.");

        try
        {
            AgentActivated?.Invoke();
        }
        catch (Exception ex)
        {
            PoliceLog.Warn($"An agent-activated handler threw: {PoliceLog.Describe(ex)}");
        }
    }

    private static void SetActive(object officer, bool active)
    {
        if (Members.ReadPath(officer, "gameObject") is GameObject gameObject)
            gameObject.SetActive(active);
    }

    /// <summary>
    /// Hands the clone to FishNet. On the registered-prefab path the object already carries a valid
    /// prefab id; on the live-clone path the behaviour table has to be rebuilt first or the server
    /// spawns an object whose RPC indices point at nothing.
    /// </summary>
    private static void NetworkSpawn(object officer)
    {
        var manager = NetworkManager();
        if (manager is null)
            return;

        var networkObject = GetComponentByName(officer, GameTypes.NetworkObject);
        if (networkObject is null)
            return;

        if (Status.Strategy == SpawnStrategy.CloneLiveOfficer)
            RebuildNetworkIdentity(networkObject, manager);

        var serverManager = Members.ReadPath(manager, "ServerManager");
        if (serverManager is null)
            return;

        var spawn = FindOverload(serverManager.GetType(), "Spawn", GameTypes.NetworkObject);
        if (spawn is null)
        {
            PoliceLog.Warn("FishNet's ServerManager.Spawn(NetworkObject, ...) was not found; the agent stays local to this machine.");
            return;
        }

        try
        {
            spawn.Invoke(serverManager, new object?[] { networkObject, null, default(UnityEngine.SceneManagement.Scene) });
        }
        catch (Exception ex)
        {
            PoliceLog.Warn($"Network-spawning a federal agent failed ({PoliceLog.Describe(ex)}); it stays local to this machine.");
        }
    }

    /// <summary>
    /// Re-runs FishNet's own initialisation trio on a clone.
    /// <para>
    /// A copy of a live officer inherits the donor's behaviour table wholesale, so its RPC indices
    /// point at the donor's components. Rebuilding the table and re-running preinitialise is what
    /// makes the copy a distinct network identity rather than a second reference to the original.
    /// <c>componentIndex</c> is a <c>ref byte</c>, which reflection can only express as a boxed slot
    /// in the argument array.
    /// </para>
    /// </summary>
    private static void RebuildNetworkIdentity(object networkObject, object manager)
    {
        var update = GameReflection.FindMethod(networkObject.GetType(), "UpdateNetworkBehaviours", 2);
        if (update is not null)
        {
            try
            {
                var arguments = new object?[] { networkObject, (byte)0 };
                update.Invoke(networkObject, arguments);
            }
            catch (Exception ex)
            {
                PoliceLog.Warn($"UpdateNetworkBehaviours failed on a federal agent clone: {PoliceLog.Describe(ex)}");
            }
        }

        // The object id here is provisional — ServerManager.Spawn assigns the real one — but it has
        // to be something no live object is using, so it counts up from well above the scene range.
        Members.Invoke(networkObject, "Preinitialize_Internal", manager, _nextProvisionalObjectId++, null, true);
        Members.Invoke(networkObject, "Initialize", true, true);
    }

    private static void Destroy(object? officer)
    {
        if (officer is null)
            return;

        try
        {
            if (officer is Il2CppObjectBase native)
            {
                Tagged.Remove(native.Pointer);
                Posts.Remove(native.Pointer);
            }

            Members.Invoke(officer, "Deactivate");

            var pursuit = Members.ReadPath(officer, "PursuitBehaviour");
            if (pursuit is not null)
            {
                Members.Invoke(pursuit, "EndCombat");
                Members.Invoke(pursuit, "Disable");
            }

            RemoveFromRegistries(officer);

            var networkObject = GetComponentByName(officer, GameTypes.NetworkObject);
            var manager = NetworkManager();
            var serverManager = manager is null ? null : Members.ReadPath(manager, "ServerManager");

            if (networkObject is not null && serverManager is not null && Members.Read(networkObject, "IsSpawned", false))
            {
                var despawn = FindOverload(serverManager.GetType(), "Despawn", GameTypes.NetworkObject);
                despawn?.Invoke(serverManager, new object?[] { networkObject, null });
            }

            if (Members.ReadPath(officer, "gameObject") is GameObject gameObject)
                Object.Destroy(gameObject);
        }
        catch (Exception ex)
        {
            PoliceLog.Warn($"Cleaning up a federal agent threw: {PoliceLog.Describe(ex)}");
        }
    }

    /// <summary>
    /// There is no generic NPC despawn in the game — only the cartel goon and employee variants — so
    /// pulling the object out of the two registries by hand is the documented route.
    /// </summary>
    private static void RemoveFromRegistries(object officer)
    {
        var officerType = GameReflection.FindType(GameTypes.PoliceOfficer);
        if (officerType is not null && GameReflection.TryReadStatic(officerType, "Officers", out var officers, out _))
            Members.Invoke(officers, "Remove", officer);

        var manager = GameBridge.Singleton(GameTypes.NpcManager);
        if (manager is not null && GameReflection.TryRead(manager, "NPCRegistry", out var registry, out _))
            Members.Invoke(registry, "Remove", officer);
    }

    // ── Lookups ───────────────────────────────────────────────────────────────────────────────

    private static object? NetworkManager()
    {
        var finder = GameReflection.FindType(GameTypes.InstanceFinder);
        if (finder is null)
            return null;

        return GameReflection.TryReadStatic(finder, "NetworkManager", out var manager, out _) && GameReflection.IsPresent(manager)
            ? manager
            : null;
    }

    /// <summary>
    /// Looks for the officer prefab in FishNet's spawnable collection. Its presence there was read
    /// out of a Mono decompile of another mod and has never been confirmed on this IL2CPP build,
    /// which is exactly why the answer is computed rather than assumed.
    /// </summary>
    private static object? FindPrefabTemplate()
    {
        var manager = NetworkManager();
        if (manager is null)
            return null;

        var prefabs = Members.ReadPath(manager, "SpawnablePrefabs");
        if (prefabs is null)
            return null;

        if (Members.InvokeFor(prefabs, "GetObjectCount") is not int count || count <= 0)
            return null;

        for (var i = 0; i < count; i++)
        {
            var entry = Members.InvokeFor(prefabs, "GetObject", true, i);
            if (entry is null)
                continue;

            if (Members.ReadPath(entry, "gameObject") is GameObject gameObject &&
                string.Equals(gameObject.name, PrefabName, StringComparison.Ordinal))
            {
                return entry;
            }
        }

        return null;
    }

    private static object? FirstOfficer()
    {
        foreach (var officer in DetectionTuner.Officers())
        {
            if (officer is not null && !IsAgent(officer))
                return officer;
        }

        return null;
    }

    private static object? GetComponentByName(object officer, string typeName)
    {
        var type = GameReflection.FindType(typeName);
        return type is null ? null : Components.Get(Components.GameObjectOf(officer), type);
    }

    /// <summary>
    /// Picks an overload by its first parameter's type name. FishNet's <c>Spawn</c> and
    /// <c>Despawn</c> each have a <c>GameObject</c> and a <c>NetworkObject</c> form with the same
    /// arity, so matching on argument count alone would pick whichever the runtime listed first.
    /// </summary>
    private static MethodInfo? FindOverload(Type type, string methodName, string firstParameterTypeName)
    {
        foreach (var candidate in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (candidate.Name != methodName)
                continue;

            var parameters = candidate.GetParameters();
            if (parameters.Length > 0 && parameters[0].ParameterType.FullName == firstParameterTypeName)
                return candidate;
        }

        return null;
    }

    internal enum SpawnStrategy
    {
        None = 0,
        RegisteredPrefab = 1,
        CloneLiveOfficer = 2,
    }

    internal readonly struct Viability
    {
        internal Viability(bool canSpawn, SpawnStrategy strategy, string reason)
        {
            CanSpawn = canSpawn;
            Strategy = strategy;
            Reason = reason;
        }

        internal static Viability Unknown { get; } = new(
            false,
            SpawnStrategy.None,
            "this build has not been surveyed yet - that happens on the first gameplay scene");

        internal bool CanSpawn { get; }

        internal SpawnStrategy Strategy { get; }

        /// <summary>Plain-language reason, shown in the probe report and the menu.</summary>
        internal string Reason { get; }
    }

    private sealed class PendingSpawn
    {
        internal PendingSpawn(object officer, string targetPlayerCode, Vector3? postPosition)
        {
            Officer = officer;
            TargetPlayerCode = targetPlayerCode;
            PostPosition = postPosition;
        }

        internal object Officer { get; }

        internal string TargetPlayerCode { get; }

        /// <summary>Set for a stakeout; null for a pursuit team.</summary>
        internal Vector3? PostPosition { get; }

        internal int Frames { get; set; }
    }
}

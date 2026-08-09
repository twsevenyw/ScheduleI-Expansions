# Schedule I — Networking & Console API Reference

Target: MelonLoader / IL2CPP mods against `Assembly-CSharp.dll`, game version **0.4.6f11**.

Every type, member and command string below was read out of the metadata dumps
in `research/raw/`. Anything I could **not** confirm is explicitly tagged
`UNVERIFIED`.

Sources: `ns-Il2CppFishNet*.txt` (52 namespace dumps),
`ns-Il2CppScheduleOne.Networking.txt`, `ns-Il2CppScheduleOne.txt` (the `Console`
class), `02-types-index.txt`, `literals-sorted.txt`, `literals-ordered.txt`,
`_cmdclasses.txt`.

---

## Part 1 — Networking

### 1.1 The stack

The game ships **FishNet** as `Il2CppFishNet.Runtime.dll`
(`research/raw/00-assemblies.txt`; `Assembly-CSharp` references it directly).
Transports present in the dumps:

- `Il2CppFishNet.Transporting.Multipass` — the wrapper that picks between them
- `Il2CppFishNet.Transporting.Tugboat` (+ `.Client` / `.Server`) — LiteNetLib/UDP
- `Il2CppFishNet.Transporting.Yak` (+ `.Client` / `.Server`) — local/offline loopback
- `Il2CppFishySteamworks.FishySteamworks` — Steam datagram relay, referenced by
  `LoadManager+<>c__DisplayClass73_1` when the host starts

Lobby/session management is Steam-side, in `Il2CppScheduleOne.Networking`:

```csharp
public class Lobby : PersistentSingleton<Lobby>
{
    static int    PlayerLimit        { get; set; }
    static string JoinReadyMessage   { get; set; }
    static string LoadTutorialMessage{ get; set; }
    static string HostLoadingMessage { get; set; }

    bool   IsHost      { get; }
    bool   IsInLobby   { get; }
    int    PlayerCount { get; }
    ulong  LobbyID     { get; set; }
    Il2CppSystem.Action OnLobbyChange { get; set; }
    ILobbyService _lobbyService { get; set; }

    void CreateLobby();
    void LeaveLobby();
    void TryOpenInviteInterface();
    void SendLobbyMessage(string message);
    void SetLobbyData(string key, string value);
    string GetLaunchLobby();
    string GetSessionConnectionIdentifier();
    bool   IsSessionReadyForClient();
    Il2CppSystem.Collections.Generic.List<string> GetLobbyMemberIDs();
}

// two implementations of ILobbyService:
public class SteamLobbyService  { /* Steamworks callbacks; JoinAsClient(string steamId64) */ }
public class MockLobbyService   { /* offline / testing */ }
```

`Lobby.SetLobbyData(key, value)` / `GetLobbyData(key)` and
`SendLobbyMessage(string)` are a **real, usable side-channel** for mod config
sync between host and clients that does not require any FishNet codegen. That
is the recommended way to push mod settings to peers.

Also in that namespace, proving the game itself thinks in host-only terms:

```csharp
public class NetworkConditionalObject : MonoBehaviour
{
    ECondition condition { get; set; }
    enum ECondition { All = 0, HostOnly = 1 }
}

public class AutoNetworkStart : MonoBehaviour
{
    enum EAutoStartType { Disabled = 0, Host = 1, Server = 2, Client = 3 }
}
```

### 1.2 Server-vs-client authority, as this game structures it

The pattern is uniform and easy to read off the dumps. Every networked type is
either a `NetworkBehaviour` or a `NetworkSingleton<T>` (which *is* a
`NetworkBehaviour` — see `ns-Il2CppScheduleOne.DevUtilities.txt`), and FishNet's
IL weaver has already generated, for every RPC method `Foo`, a triple:

```
RpcWriter___Server_Foo_<hash>(...)      // caller side: serialize + send to server
RpcWriter___Observers_Foo_<hash>(...)   // server side: serialize + broadcast to observers
RpcWriter___Target_Foo_<hash>(...)      // server side: serialize + send to one connection
RpcReader___Server_Foo_<hash>(PooledReader, Channel)      // receiver side: deserialize
RpcReader___Observers_Foo_<hash>(...)
RpcReader___Target_Foo_<hash>(...)
RpcLogic___Foo_<hash>(...)              // THE ACTUAL METHOD BODY
```

**`RpcLogic___*` is the real implementation and it runs on every peer that
receives the RPC.** The public `Foo(...)` you see in the dump is just the
send-side stub. This is the single most important fact for patching: if you
Harmony-patch `TimeManager.SetTime`, you patch the *sender*; if you patch
`TimeManager.RpcLogic___...`, you patch the thing that actually happens
everywhere. (Already recorded as a project decision in `CONTEXT.md`; the dumps
confirm it — e.g. `TimeManager` exposes `RpcLogic___PassMinute_Client_3316948804`,
`RpcLogic___OnTimeSkip_Client_1692629761`, `RpcLogic___StartSleep_2166136261`,
`RpcLogic___SetTimeData_Client_1794730778`, `RpcLogic___SetHostSleepDone_1140765316`.)

**Game managers that are `NetworkSingleton<T>` (i.e. host-authoritative
singletons)** — all 28 verified from `02-types-index.txt`:

`Building.BuildManager`, `Cartel.Cartel`, `Combat.CombatManager`,
`Delivery.DeliveryManager`, `DevUtilities.GameManager`,
`Dragging.DragManager`, `Employees.EmployeeManager`,
`GamePhysics.PhysicsManager`, `GameTime.TimeManager`,
`Graffiti.GraffitiManager`, `Law.CheckpointManager`, `Law.CurfewManager`,
`Levelling.LevelManager`, `Map.DarkMarket`, `Map.SewerManager`,
`Messaging.MessagingManager`, `Money.MoneyManager`, `NPCs.NPCManager`,
`Networking.ReplicationQueue`, `Product.ProductManager`,
`Quests.QuestManager`, `Storage.StorageManager`, `Trash.TrashManager`,
`UI.DailySummary`, `UI.Shop.ShopManager`, `Variables.VariableDatabase`,
`Vehicles.VehicleManager`, `Weather.EnvironmentManager`.

**Game types that are plain `NetworkBehaviour`** (55 verified) include
`NPCs.NPC`, `NPCs.NPCMovement`, `NPCs.NPCHealth`, `NPCs.NPCInventory`,
`NPCs.NPCAnimation`, `NPCs.Behaviour.Behaviour`, `NPCs.Behaviour.NPCBehaviour`,
`NPCs.Actions.NPCActions`, `NPCs.Schedules.NPCAction`,
`PlayerScripts.Player`, `PlayerScripts.PlayerCrimeData`,
`PlayerScripts.PlayerClothing`, `PlayerScripts.Health.PlayerHealth`,
`Economy.Customer`, `EntityFramework.BuildableItem`,
`Storage.StorageEntity`, `Property.Property`, `Property.Tap`,
`Vehicles.LandVehicle`, `Vehicles.VehicleLights`, `Money.ATM`,
`Police.RoadCheckpoint`, `Doors.DoorController`,
`ObjectScripts.Bed`, `ObjectScripts.VendingMachine`, `ObjectScripts.Recycler`,
`Trash.TrashContainer`, `Graffiti.SpraySurface`, `Growing.ShroomColony`,
`Casino.*`, `Cartel.*`, and more.

#### NPCs

`NPCs.NPC : NetworkBehaviour`. Everything meaningful about an NPC — movement,
health, inventory, animation, relationship, panic state — goes through
ServerRpcs. Verified send-side stubs on `NPC`:

```csharp
RpcWriter___Server_AimedAtByPlayer_3323014238(NetworkObject player)
RpcWriter___Server_PlayVO_Server_1710085680(VoiceOver.EVOLineType lineType)
RpcWriter___Server_SendAnimationTrigger_3615296227(string trigger)
RpcWriter___Server_SendImpact_427288424(Combat.Impact impact)
RpcWriter___Server_SendRelationship_431000436(float relationship)
RpcWriter___Server_SendWorldSpaceDialogue_606697822(string text, float duration)
RpcWriter___Server_SetIsBeingPickPocketed_1140765316(bool pickpocketed)
RpcWriter___Server_SetPanicked_Server_2166136261()
```

The registry is static and host-mirrored:

```csharp
public class NPCManager : NetworkSingleton<NPCManager>
{
    static Il2CppSystem.Collections.Generic.List<NPC> NPCRegistry { get; set; }
    static NPC GetNPC(string id);
    static Il2CppSystem.Collections.Generic.List<NPC> GetNPCsInRegion(Map.EMapRegion region);
    Transform NPCContainer { get; set; }
    Il2CppReferenceArray<Transform> NPCWarpPoints { get; set; }
    Persistence.Loaders.NPCsLoader loader { get; set; }   // also an ISaveable
}
```

Clients get NPC state replicated to them; they do not author it.

#### Spawning

There is no game-level "spawn an NPC" API. Spawning is raw FishNet, and it is
**server-only** by contract:

```csharp
// Il2CppFishNet.Managing.Server.ServerManager
void Spawn(UnityEngine.GameObject go, NetworkConnection ownerConnection = null,
           UnityEngine.SceneManagement.Scene scene = null);
void Spawn(NetworkObject nob, NetworkConnection ownerConnection = null,
           UnityEngine.SceneManagement.Scene scene = null);
void Despawn(UnityEngine.GameObject go, Nullable<DespawnType> despawnType = null);
void Despawn(NetworkObject networkObject, Nullable<DespawnType> despawnType = null);

// NetworkBehaviour also forwards them:
void Despawn(GameObject go, Nullable<DespawnType> despawnType = null);
void Despawn(NetworkObject nob, Nullable<DespawnType> despawnType = null);
void Despawn(Nullable<DespawnType> despawnType = null);
void GiveOwnership(NetworkConnection newOwner);
```

Two hard constraints that follow from the dumps:

1. `NetworkObject` carries `PrefabId` and `SpawnableCollectionId` (both
   `UInt16`), resolved through `NetworkManager._spawnablePrefabs` /
   `_runtimeSpawnablePrefabs` (`Dictionary<ushort, PrefabObjects>`). A
   `GameObject` you build at runtime has no prefab id, so clients cannot
   reconstruct it. You can only network-spawn something that is already in a
   `PrefabObjects` collection, or clone a scene object and accept that it stays
   host-local.
2. On IL2CPP, FishNet's IL weaver never ran on *your* assembly. Custom
   `[ServerRpc]` / `[ObserversRpc]` / `SyncVar` in a mod produce no
   `RpcWriter___`/`RpcReader___`/`RpcLogic___` triple and no
   `NetworkInitialize___Early/__Late` bodies, so **a mod cannot define new
   networked behaviour at all**. Everything you do either rides an existing
   game RPC or is host-local.

For NPC creation specifically, `research/API-NPCS.md` already established the
only real runtime factory is
`EmployeeManager.CreateEmployee_Server(...)` — note the `_Server` suffix, which
is the game's own naming convention for host-only entry points.

#### Economy

`Money.MoneyManager : NetworkSingleton<MoneyManager>`:

```csharp
void ChangeCashBalance(float change, bool visualizeChange = true, bool playCashSound = false);
void ChangeLifetimeEarnings(float change);
void CreateOnlineTransaction(string _transaction_Name, float _unit_Amount,
                             float _quantity, string _transaction_Note);
// weaver output:
RpcWriter___Server_ChangeLifetimeEarnings_431000436(float change)
RpcWriter___Server_CreateOnlineTransaction_1419830531(string, float, float, string)
RpcWriter___Observers_ReceiveOnlineTransaction_1419830531(string, float, float, string)
```

So: **cash is client-local, online balance is server-authoritative.**
`ChangeCashBalance` mutates the caller's own wallet; `CreateOnlineTransaction`
and `ChangeLifetimeEarnings` are ServerRpcs whose result is broadcast back via
`ReceiveOnlineTransaction`. A client calling `CreateOnlineTransaction` sends a
request; the host applies it. (Details of the money model are in
`research/API-ECONOMY.md`.)

`Money.ATM : NetworkBehaviour` similarly has `RpcWriter___Server_DropCash_...`
and `RpcWriter___Server_SendBreak_...` with `Observers_Break` / `Observers_Repair`
broadcasts — the ATM's *broken* state is host state.

#### Loading & replication

`Networking.ReplicationQueue : NetworkSingleton<ReplicationQueue>` is the
throttle the host uses to stream world state to a joining client:

```csharp
static int RATE_LIMIT_BYTES_PER_SECOND { get; set; }
static int MAX_REPLICATION_DURATION    { get; set; }
static void Enqueue(string taskName, NetworkConnection target,
                    Il2CppSystem.Action<NetworkConnection> callback,
                    int approximateSizeBytes = 32);
bool   ReplicationDoneForLocalPlayer   { get; set; }
bool   LocalPlayerReplicationTimedOut  { get; }
string CurrentReplicationTask          { get; set; }
```

Anything that adds a lot of networked objects will push through this queue and
lengthen every client's join. If your mod spawns hundreds of things, joining
players will time out (`LocalPlayerReplicationTimedOut`). Related:
`Networking.IStaggeredReplicator { bool IsDoneReplicating; void SetIsDoneReplicating(); }`,
registered via `LoadManager.AddStaggeredReplicator`.

### 1.3 `InstanceFinder`, `NetworkObject`, `NetworkBehaviour`

```csharp
public static class Il2CppFishNet.InstanceFinder
{
    static NetworkManager     NetworkManager     { get; }
    static ServerManager      ServerManager      { get; }
    static ClientManager      ClientManager      { get; }
    static TransportManager   TransportManager   { get; }
    static TimeManager        TimeManager        { get; }   // FishNet's, NOT the game's
    static SceneManager       SceneManager       { get; }
    static RollbackManager    RollbackManager    { get; }
    static PredictionManager  PredictionManager  { get; }
    static StatisticsManager  StatisticsManager  { get; }

    static bool IsServer     { get; }
    static bool IsServerOnly { get; }
    static bool IsClient     { get; }
    static bool IsClientOnly { get; }
    static bool IsHost       { get; }
    static bool IsOffline    { get; }

    static T    GetInstance<T>();
    static bool TryGetInstance<T>(out T component);
    static bool HasInstance<T>();
}
```

Beware the name collision: `InstanceFinder.TimeManager` is
`Il2CppFishNet.Managing.Timing.TimeManager` (network tick), not
`Il2CppScheduleOne.GameTime.TimeManager` (in-game clock). Alias one of them in
your `using`s.

`NetworkBehaviour` (`Il2CppFishNet.Object.NetworkBehaviour : MonoBehaviour`),
the members you'll actually touch:

```csharp
bool IsSpawned            { get; }
bool IsClient             { get; }   // is a client active in this process
bool IsClientOnly         { get; }
bool IsClientInitialized  { get; }
bool IsServer             { get; }
bool IsServerOnly         { get; }
bool IsServerInitialized  { get; }
bool IsHost               { get; }
bool IsOffline            { get; }
bool IsNetworked          { get; }
bool IsOwner              { get; }
bool IsDeinitializing     { get; }

NetworkObject     NetworkObject   { get; }
NetworkConnection Owner           { get; }
int               OwnerId         { get; }
int               ObjectId        { get; }
NetworkConnection LocalConnection { get; }
Il2CppSystem.Collections.Generic.HashSet<NetworkConnection> Observers { get; }

NetworkManager   NetworkManager   { get; }
ServerManager    ServerManager    { get; }
ClientManager    ClientManager    { get; }

void GiveOwnership(NetworkConnection newOwner);
void Despawn(Nullable<DespawnType> despawnType = null);

// FishNet lifecycle callbacks the game overrides everywhere:
virtual void OnStartServer();
virtual void OnStartClient();
virtual void OnSpawnServer(NetworkConnection connection);
virtual void OnDespawnServer(NetworkConnection connection);
virtual void OnOwnershipClient(NetworkConnection prevOwner);
virtual void NetworkInitialize___Early();
virtual void NetworkInitialize__Late();
virtual void NetworkInitializeIfDisabled();
```

`NetworkObject` (`: MonoBehaviour`), the relevant bits:

```csharp
int    ObjectId       { get; set; }
ushort PrefabId       { get; }        // via _PrefabId_k__BackingField
ushort SpawnableCollectionId { get; }
ulong  SceneId        { get; }
bool   IsSceneObject  { get; }
bool   IsNetworked    { get; set; }
bool   IsNested       { get; set; }
bool   IsDeinitializing { get; set; }
NetworkObjectState State { get; set; }
NetworkConnection  PredictedSpawner { get; set; }
Il2CppReferenceArray<NetworkBehaviour> NetworkBehaviours { get; set; }
Il2CppSystem.Collections.Generic.HashSet<NetworkConnection> Observers { get; set; }
Il2CppSystem.Action<NetworkObject> OnObserversActive { get; set; }
static int UNSET_OBJECTID_VALUE { get; set; }
static int UNSET_PREFABID_VALUE { get; set; }
```

`NetworkConnection`:

```csharp
int  ClientId       { get; set; }
bool Authenticated  { get; set; }
bool IsActive       { get; }
bool IsValid        { get; }
NetworkObject FirstObject { get; set; }
Il2CppSystem.Collections.Generic.HashSet<NetworkObject> Objects { get; set; }
Il2CppSystem.Object CustomData { get; set; }
static int UNSET_CLIENTID_VALUE { get; set; }
Il2CppSystem.Action<NetworkConnection, bool> OnLoadedStartScenes { get; set; }
```

### 1.4 "Am I the host/server?" — the one-liner

**Use this. It is correct in singleplayer, correct as host, correct as client.**

```csharp
using Il2CppFishNet;

public static bool IsHostOrSingleplayer =>
    InstanceFinder.NetworkManager == null || InstanceFinder.IsServer;
```

Why not the obvious alternatives:

| Expression | Problem |
|---|---|
| `InstanceFinder.IsHost` | **False in singleplayer-with-no-network and false for a dedicated server.** `IsHost` means server *and* client are both active. Singleplayer in this game actually runs the Yak loopback transport as a host, so it usually happens to be true — but there is no guarantee, and it is false before the network starts. |
| `InstanceFinder.IsServer` alone | Throws/returns false if `NetworkManager` is null (before the network object exists, in the menu scene, during early load). The null check is the important half. |
| `Lobby.Instance.IsHost` | Only meaningful when `IsInLobby`; returns false for a solo game with no lobby. Fine as a *co-op* check, not as an authority check. |
| `NetworkBehaviour.IsServer` on some component | Correct but requires you to already hold a spawned networked component, and is false before `IsSpawned`. |

Companion checks worth having:

```csharp
// "is anyone else actually in this session?"
public static bool IsCoop =>
    Lobby.InstanceExists && Lobby.Instance.IsInLobby && Lobby.Instance.PlayerCount > 1;

// "am I a guest?"
public static bool IsRemoteClient =>
    InstanceFinder.NetworkManager != null && InstanceFinder.IsClientOnly;

// "is the world actually up?"  (combine with the persistence doc)
public static bool WorldReady =>
    LoadManager.InstanceExists && LoadManager.Instance.IsGameLoaded;
```

Guard every one of these behind `InstanceFinder.NetworkManager != null` before
touching `ServerManager`/`ClientManager` — those are properties on a possibly
null manager and will hard-crash the IL2CPP domain, not throw a catchable
`NullReferenceException`.

### 1.5 What a mod must do to not break co-op

**The rule of thumb, stated as bluntly as I can:**

> **Write game state only when `InstanceFinder.NetworkManager == null ||
> InstanceFinder.IsServer`. On a client, your mod is a read-only observer and a
> UI. If a feature cannot be expressed that way, make it host-only and say so in
> the mod description.**

Corollary rules, each of which is forced by something in the dumps:

1. **Never create a networked object on a client.** `ServerManager.Spawn` on a
   client either no-ops or desyncs; `NetworkObject.PrefabId` won't resolve on
   peers for anything you built at runtime.
2. **Never write to a `NetworkSingleton<T>`'s state from a client.** Those 28
   managers are host state. A client writing `TimeManager.CurrentTime` directly
   creates a local-only clock that the next `PassMinute_Client` RPC stomps —
   worse, some systems key off it before it's stomped.
3. **Do not add custom RPCs or SyncVars.** The FishNet weaver did not process
   your assembly. This is not a "probably won't work" — the codegen simply does
   not exist. Use `Lobby.SetLobbyData` / `SendLobbyMessage` for mod↔mod comms.
4. **Prefer patching `RpcLogic___*` over the public method** when you need a
   behaviour change to apply on all peers. Patching the public method only
   changes the sender.
5. **Everyone installs the same version.** There is no version handshake. If the
   host has a mod that spawns things and a client doesn't, the client sees
   whatever the host spawned as vanilla objects (fine) — but if the *client*
   has a patch that changes a shared calculation and the host doesn't, you get
   divergent local prediction. Ship a single build and check
   `Lobby.GetLobbyData("<yourmod>_version")`.
6. **Budget your spawns.** `ReplicationQueue.RATE_LIMIT_BYTES_PER_SECOND` and
   `MAX_REPLICATION_DURATION` mean a mod that adds a few hundred networked
   objects makes joining slow or impossible.
7. **Don't hold `NetworkConnection` references across a disconnect.** Check
   `IsValid`/`IsActive`; `CustomData` is the sanctioned per-connection stash.

**Is host-only a reasonable constraint? Yes — and it is the right default here.**
Concretely:

- The game is already host-authoritative for everything a gameplay mod cares
  about. A host-only mod is not a degraded experience for clients; the host's
  world *is* the world.
- The alternative (client-side authority) is not available. Without FishNet
  codegen you literally cannot build a synchronized mod feature.
- Prior art agrees: the closest existing driver mod
  (`bwyan-HireableDeliveryDriver_IL2CPP_port`) is host-only.
- The cost of getting it wrong is a corrupted save or a desynced session, and
  Schedule I saves are the player's whole progression.

The one place clients legitimately need code is **UI and read-only display**.
That is safe, because UI reads replicated state.

### 1.6 Multiplayer risk for the three planned features

| Feature | Risk | Why |
|---|---|---|
| **Hireable Drivers** | **High** | It creates and moves NPCs and moves items between containers. `EmployeeManager.CreateEmployee_Server` is host-only by name and by contract. `NPCMovement.SetDestination`, `Warp` and the item transfers all go through `NetworkBehaviour` state. A client running this logic in parallel would double-move items or spawn a phantom employee. Must be **strictly host-only**, with clients running nothing but the config UI. Secondary risk: each driver is another networked NPC through `ReplicationQueue` on join. |
| **Special Customers** | **High** | Same shape, worse volume. `Economy.Customer : NetworkBehaviour`; a "group of bikers visiting town" is N new networked NPCs plus deal/relationship state that lives on the host. Also collides with TVGS's own in-progress v0.5.0 implementation. Host-only, hard-capped group size, and a kill switch. |
| **Police Improvements** | **Medium** | Mostly *tuning* existing host state — `Law.LawController.Intensity`, `LawManager` wanted-level calls, `CurfewManager.Enable/Disable`, `CheckpointManager` officer counts. All of it already funnels through `NetworkSingleton`s and `PlayerCrimeData : NetworkBehaviour` ServerRpcs, so if you only write on the host it replicates for free. The risk is narrower: **per-player** wanted state. `LawManager.SetWantedLevel(Player target, PursuitLevel)` takes a target, so make sure your logic is per-player and not "the local player", or in co-op you will escalate the host for a client's crime. If you add *new* officers (federal agents) you're back in the spawning problem — reuse `PoliceStation.PullOfficer` pools instead. |

Lowest-risk ordering if you want something shippable in co-op early:
**Police Improvements → Hireable Drivers → Special Customers.**

---

## Part 2 — Console

Source: `ns-Il2CppScheduleOne.txt` (the `Il2CppScheduleOne.Console` class and its
64 nested command classes), `research/raw/_cmdclasses.txt`,
`literals-sorted.txt` / `literals-ordered.txt` for the exact command words,
descriptions and example-usage strings.

### 2.1 The `Console` type

```csharp
namespace Il2CppScheduleOne;   // root namespace, NOT Il2CppScheduleOne.Console.*

public class Console : DevUtilities.Singleton<Console>
{
    static Il2CppSystem.Collections.Generic.List<ConsoleCommand>              Commands { get; set; }
    static Il2CppSystem.Collections.Generic.Dictionary<string, ConsoleCommand> commands { get; set; }
    static PlayerScripts.Player player { get; }

    Transform TeleportPointsContainer { get; set; }
    Il2CppSystem.Collections.Generic.List<LabelledGameObject> LabelledGameObjectList { get; set; }
    Il2CppSystem.Collections.Generic.List<string> startupCommands { get; set; }
    Il2CppSystem.Collections.Generic.Dictionary<UnityEngine.KeyCode, string> keyBindings { get; set; }

    static void SubmitCommand(string args);
    static void SubmitCommand(Il2CppSystem.Collections.Generic.List<string> args);
    void AddCommand(ConsoleCommand command);
    void AddBinding(UnityEngine.KeyCode key, string command);
    void RemoveBinding(UnityEngine.KeyCode key);
    void ClearBindings();
    void RunStartupCommands();

    static void Log(Il2CppSystem.Object message, UnityEngine.Object context = null);
    static void LogWarning(Il2CppSystem.Object message, UnityEngine.Object context = null);
    static void LogError(Il2CppSystem.Object message, UnityEngine.Object context = null);
    static void LogCommandError(string error);
    static void LogUnrecognizedFormat(Il2CppStringArray correctExamples);

    public class ConsoleCommand
    {
        string CommandWord        { get; }
        string CommandDescription { get; }
        string ExampleUsage       { get; }
        virtual void Execute(Il2CppSystem.Collections.Generic.List<string> args);
    }

    public class LabelledGameObject
    {
        string Label { get; set; }
        UnityEngine.GameObject GameObject { get; set; }
    }
}
```

Every command is a nested class `Il2CppScheduleOne.Console+<Name>` deriving from
`Console+ConsoleCommand`. There are exactly **64** of them (verified by count in
`_cmdclasses.txt` and by the `02-types-index.txt` entries).

### 2.2 The complete command table

Command words and example strings below are **verbatim string literals from
`global-metadata.dat`** (`literals-sorted.txt` line numbers cited in the raw
dump). Descriptions are the `CommandDescription` literals. Where the game only
ships a bare word with no example literal, the args column says so.

#### Player

| Command | Class | Args / example | Description (verbatim) |
|---|---|---|---|
| `give` | `AddItemToInventoryCommand` | `give ogkush 5` · `give watering_can` | Gives the player the specified item. Optionally specify a quantity. |
| `clearinventory` | `ClearInventoryCommand` | *(none)* | Clears the player's inventory |
| `sethealth` | `SetHealth` | `sethealth 100` | Sets the player's health to the specified amount |
| `setstaminareserve` | `SetStaminaReserve` | `setstaminareserve 200` | Sets the player's stamina reserve (default 100) to the specified amount. |
| `setmovespeed` | `SetMoveSpeedCommand` | `setmovespeed 1` | Sets the player's move speed multiplier |
| `setjumpforce` | `SetJumpMultiplier` | `setjumpforce 1` | Sets the player's jump force multiplier |
| `setemotion` | `SetEmotion` | `setemotion cheery` | Sets the facial expression of the player's avatar. |
| `teleport` | `Teleport` | `teleport townhall` · `teleport barn` · `teleport jessi_waters` · `teleport docks` | Teleports the player to the specified location, property, or NPC. |
| `freecam` | `FreeCamCommand` | *(none)* | Toggles free cam mode |

#### Money & progression

| Command | Class | Args / example | Description |
|---|---|---|---|
| `changecash` | `ChangeCashCommand` | `changecash 5000` · `changecash -5000` | Changes the player's cash balance by the specified amount |
| `changebalance` | `ChangeOnlineBalanceCommand` | `changebalance 5000` · `changebalance -5000` | Changes the player's online balance by the specified amount |
| `addxp` | `GiveXP` | `addxp 100` | Adds the specified amount of experience points. |
| `setregionunlocked` | `SetRegionUnlocked` | `setregionunlocked downtown` | Unlocks the given region |
| `setunlocked` | `SetUnlocked` | `setunlocked <npc_id>` | Unlocks the given NPC |
| `setrelationship` | `SetRelationship` | `setrelationship <npc_id> 5` | Sets the relationship scalar of the given NPC. Range is 0-5. |
| `setowned` | `SetPropertyOwned` | `setowned barn` · `setowned laundromat` · `setowned manor` | Sets the specified property or business as owned |

#### Products & items

| Command | Class | Args / example | Description |
|---|---|---|---|
| `setdiscovered` | `SetDiscovered` | `setdiscovered ogkush` | Sets the specified product as discovered |
| `packageproduct` | `PackageProduct` | `packageproduct baggie` · `packageproduct jar` | Packages the equipped product with the specified packaging |
| `setquality` | `SetQuality` | `setquality standard` · `setquality heavenly` | Sets the quality of the currently equipped item. |
| `setquantity` | `SetQuantity` | `setquantity 5` | Sets the quantity of the currently equipped item. |
| `growplants` | `GrowPlants` | *(none)* | Sets ALL plants in the world fully grown |

#### World / time / weather

| Command | Class | Args / example | Description |
|---|---|---|---|
| `settime` | `SetTimeCommand` | `settime 1530` | Sets the time of day to the specified 24-hour time |
| `settimescale` | `SetTimeScale` | `settimescale 1` | Sets the time scale. Default 1 |
| `setdayduration` | `SetDayDuration` | `setdayduration 24` | Sets the (real life) duration of an in-game 24-hour cycle. Measured in real minutes. |
| `forcesleep` | `ForceSleep` | *(none)* | Forces all players to immediately sleep. |
| `setweather` | `SetWeather` | `setweather clear` · `setweather lightrain` · `setweather heavyrain` | Sets the weather to the specified type |
| `triggerlightning` | `TriggerLightning` | optional target (player or npc); empty = random | Triggers a lightning event. You can specify a target (player or npc) or leave it empty for a random location. |
| `triggerdistantthunder` | `TriggerDistantThunder` | *(none)* | Triggers distant thunder. |
| `setgravitymultiplier` | `SetGravityMultiplier` | `setgravitymultiplier 0.5` | Sets the multiplier of the gravity strength. |
| `cleartrash` | `ClearTrash` | *(none)* | *(no matching literal found — see UNVERIFIED)* |
| `spawnvehicle` | `SpawnVehicleCommand` | `spawnvehicle shitbox` | Spawns a vehicle at the player's location |

#### Law

| Command | Class | Args / example | Description |
|---|---|---|---|
| `setlawintensity` | `SetLawIntensity` | `setlawintensity 6` | Sets the intensity of law enforcement activity on a scale of 0-10. |
| `setpoliceignoreplayers` | `SetPoliceIgnorePlayers` | `setpoliceignoreplayers true` · `... false` | Sets whether police ignore players. |
| `raisewanted` | `RaisedWanted` | *(none)* | Raises the player's wanted level |
| `lowerwanted` | `LowerWanted` | *(none)* | Lowers the player's wanted level |
| `clearwanted` | `ClearWanted` | *(none)* | Clears the player's wanted level |

Note the class name is `RaisedWanted` (past tense) — that is what is in the
assembly, not a typo in this document.

#### Quests / variables / NPCs

| Command | Class | Args / example | Description |
|---|---|---|---|
| `setqueststate` | `SetQuestState` | `setqueststate <quest name> <state>` | Sets the state of the specified quest |
| `setquestentrystate` | `SetQuestEntryState` | `setquestentrystate <quest name> <entry index> <state>` | Sets the state of the specified quest entry |
| `setvar` | `SetVariableValue` | `setvar <variable> <value>` | Sets the value of the specified variable |
| `endtutorial` | `EndTutorial` | *(none)* | Forces the tutorial to end immediately (only if the player is actually in the tutorial). |
| `playcutscene` | `PlayCutscene` | `playcutscene Tutorial end` | Plays the cutscene with the given name |
| `destroynpcs` | `DestroyNPCs` | *(none)* | Destroys all NPCs in the scene, including employees and dealers. |
| `addemployee` | `AddEmployeeCommand` | `addemployee botanist barn` | Adds an employee of the specified type to the given property. |

#### Save / session

| Command | Class | Args / example | Description |
|---|---|---|---|
| `save` | `Save` | *(none)* | Forces a save |
| `quit` | `QuitGame` | *(none)* | *(no matching literal found — see UNVERIFIED)* |

#### Binds

| Command | Class | Args / example | Description |
|---|---|---|---|
| `bind` | `Bind` | `bind t 'settime 1200'` | Binds the given key to the given command. |
| `unbind` | `Unbind` | `unbind t` | Removes the given bind. |
| `clearbinds` | `ClearBinds` | *(none)* | Clears ALL binds. |

#### Debug / performance

| Command | Class | Args / example | Description |
|---|---|---|---|
| `enable` | `Enable` | `enable pp` | Enables the specified GameObject |
| `disable` | `Disable` | `disable pp` | Disables the specified GameObject |
| `disablenpcs` | `DisableNPCs` | *(none)* | Disables all NPCs in the scene (sets their GameObjects to inactive). |
| `disablenpcasset` | `DisableNPCAsset` | `disablenpcasset avatar` | *(no dedicated literal found)* |
| `disablemeshes` | `DisableMeshes` | *(none)* | Disables all MeshRenderers in the scene. |
| `enableinstancing` / `disableinstancing` | `EnableInstancing` / `DisableInstancing` | *(none)* | Enables/Disables instancing in the game |
| `enableocclusionculling` / `disableocclusionculling` | `EnableOcclusion` / `DisableOcclusion` | *(none)* | Enables/Disables occlusion culling in the game |
| `enablephysics` / `disablephysics` | `EnablePhysics` / `DisablePhysics` | *(none)* | Enables/Disables physics in the game |
| `enableterrain` / `disableterrain` | `EnableTerrain` / `DisableTerrain` | *(none)* | Enables/Disables terrain in the game |
| `showfps` | `ShowFPS` | *(none)* | Shows FPS label. |
| `hidefps` | `HideFPS` | *(none)* | Hides FPS label. |
| `hideui` | `HideUI` | *(none)* | Hides all on-screen UI. |

`enable pp` / `disable pp` operate on entries in
`Console.LabelledGameObjectList` (`{ string Label; GameObject GameObject; }`),
so the valid argument set is whatever labels the scene author wired up. `pp` is
the only one with a literal; enumerate `Console.Instance.LabelledGameObjectList`
at runtime for the rest.

`teleport` targets come from `Console.TeleportPointsContainer` (a `Transform`
whose children are the named points) plus property codes plus NPC ids —
verified by the description text and the three different example forms.

### 2.3 Invoking a console command from a mod

Two static overloads, both on the game's `Console`:

```csharp
using Il2CppScheduleOne;   // note: Console lives in the ROOT namespace

// whole line, space-separated
Console.SubmitCommand("settime 1530");

// pre-tokenised
var args = new Il2CppSystem.Collections.Generic.List<string>();
args.Add("give"); args.Add("ogkush"); args.Add("5");
Console.SubmitCommand(args);
```

The `string` overload is a convenience wrapper; the `List<string>` overload is
the real dispatcher (it is what `ConsoleCommand.Execute(List<string> args)`
receives, minus the command word). Prefer the list form when an argument can
contain a space — `playcutscene Tutorial end` is a two-token argument and the
string form's naive split is why `bind`'s example uses quotes.

Watch out for the `System.Console` collision. In a file that also uses BCL
types, alias it:

```csharp
using S1Console = Il2CppScheduleOne.Console;
S1Console.SubmitCommand("save");
```

Preconditions:

- The command registry is static (`Console.Commands` / `Console.commands`) and
  is populated in `Console.Awake()`, so it only exists once the game scene has
  loaded. Gate on `LoadManager.Instance.IsGameLoaded` (see the persistence doc).
- `GameData.Settings.ConsoleEnabled` (visible in `Game.json` as
  `"Settings": { "ConsoleEnabled": true, ... }`) is the save's console flag. I
  did **not** find the check inside `SubmitCommand` itself in the dumps — it
  most likely gates the console *UI*. Treat "does `SubmitCommand` respect
  `ConsoleEnabled`?" as **UNVERIFIED**; if you need certainty, read
  `GameData.Settings` and refuse to call when it is false.
- Bad arguments do not throw — the command calls
  `Console.LogUnrecognizedFormat(string[] correctExamples)` and returns. You get
  no return value and no success signal. If you need confirmation, read the
  state back (e.g. `TimeManager.Instance.CurrentTime` after `settime`).
- Several commands act on `Console.player` (== `Player.Local`). In co-op, a
  client running `changecash` changes *its own* cash. Commands touching
  host-authoritative state (`setowned`, `addemployee`, `growplants`,
  `destroynpcs`, `forcesleep`, `settime`) should be issued **on the host only**.

**Registering your own command.** `Console.Instance.AddCommand(ConsoleCommand)`
exists, but `ConsoleCommand` is an IL2CPP class whose `CommandWord` /
`CommandDescription` / `ExampleUsage` are get-only properties — subclassing it
from managed code requires `ClassInjector.RegisterTypeInIl2Cpp` and overriding
IL2CPP virtuals. **Don't.** Use S1API instead, which already did the injection:

```csharp
public abstract class S1API.Console.BaseConsoleCommand
{
    string CommandWord        { get; }
    string CommandDescription { get; }
    string ExampleUsage       { get; }
    public abstract void ExecuteCommand(System.Collections.Generic.List<string> args);
}
```

registered through `S1API.Console.CustomConsoleRegistry` (internal;
registration happens via S1API's own patch on the game's console submit path —
see `research/API-S1API.md` §Patches).

S1API also wraps the common commands as typed managed calls, which is what
`CreativeMode` already uses:

```csharp
public static class S1API.Console.ConsoleHelper
{
    static void Submit(string command);
    static void Submit(Il2CppSystem.Collections.Generic.IEnumerable<string> arguments);

    static void AddItemToInventory(string itemCode, int? quantity = null);
    static void ClearInventory();
    static void ClearTrash();
    static void ClearWanted();
    static void RaiseWanted();
    static void LowerWanted();
    static void DiscoverProduct(string productCode);
    static void GiveXp(int amount);
    static void GrowPlants();
    static void RunCashCommand(int amount);
    static void RunOnlineBalanceCommand(int amount);
    static void SaveGame();
    static void SetLawIntensity(float intensity);
    static void SetNpcRelationship(string npcId, float level);
    static void SetNpcRelationship(S1API.Entities.NPC npc, float level);
    static void SetPlayerHealth(float amount);
    static void SetPlayerJumpMultiplier(float multiplier);
    static void SetPlayerMoveSpeedMultiplier(float multiplier);
    static void SetQuality(S1API.Products.Quality quality);
    static void SetQuestState(string questName, S1API.Quests.Constants.QuestState state);
    static void SetTime(string hhmm);
    static void SpawnVehicle(string vehicleCode);
    static void UnlockNpc(S1API.Entities.NPC npc);

    [Obsolete] static void SetPlayerEnergyLevel(float amount);   // no-op on this build
}

public static class S1API.Console.ConsoleItemAliases
{
    static void Register(string alias, string canonicalItemId);   // aliases for `give`
}
```

**Why the console is the version-stable path:** command *words* are string
literals baked into save-visible behaviour and referenced in the game's own
`startupCommands` and key-bind system, so TVGS has strong reasons not to rename
them. Internal method names, RPC hashes and `Method_*_PDM_*` fallback names all
churn between builds. `Console.SubmitCommand("settime 1530")` will survive a
patch that renames `TimeManager.SetTime`.

The trade-off is that you get no return value, no error signal, and a string
parse in the middle. Use the console for *fire-and-forget world mutation*
(`setowned`, `addemployee`, `give`, `setunlocked`) and the typed API for
anything you need to read back.

---

## UNVERIFIED summary for this document

| Item | Status |
|---|---|
| FishNet major version | The assembly is `Il2CppFishNet.Runtime v0.0.0.0` — IL2CPP strips version info. The API shape (`InstanceFinder`, `NetworkBehaviour.RpcLogic___*`, `Multipass`/`Tugboat`/`Yak`, `PredictionManager`) matches FishNet 3.x, but the version number itself is **not** in the dumps. |
| `cleartrash` description | Command word literal `cleartrash` confirmed at `literals-sorted.txt:18548`; no matching `CommandDescription` literal isolated. |
| `quit` description | Command word literal `quit` confirmed at `literals-sorted.txt:21577`; class is `Console+QuitGame`. No description literal isolated. |
| `disablenpcasset` description | Word + example `disablenpcasset avatar` confirmed; no description literal isolated. |
| Does `Console.SubmitCommand` honour `GameData.Settings.ConsoleEnabled`? | Not determinable from metadata (it would be inside the method body). Assume it does not and check yourself. |
| `enable` / `disable` valid labels beyond `pp` | Come from `Console.LabelledGameObjectList`, populated in the scene. Enumerate at runtime. |
| `teleport` full destination list | Comes from `Console.TeleportPointsContainer` children + property codes + NPC ids. Only `townhall`, `barn`, `docks`, `jessi_waters` appear as literals. |
| `ReplicationQueue.RATE_LIMIT_BYTES_PER_SECOND` / `MAX_REPLICATION_DURATION` values | Static field values are not in IL2CPP metadata. Read at runtime. |

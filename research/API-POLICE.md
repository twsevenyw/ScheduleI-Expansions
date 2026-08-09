# API-POLICE.md — Schedule I law-enforcement system

Source of truth: the IL2CPP dumps under `research/raw/` for the installed build
(`C:\Program Files (x86)\Steam\steamapps\common\Schedule I`, MelonLoader `Il2CppAssemblies`).
Every type name, member name and signature below is copied verbatim from those dumps.

**Reading conventions used in this document**

- Namespaces are reported with the `Il2Cpp` prefix exactly as Il2CppInterop emits them
  (`Il2CppScheduleOne.Police`). The original CLR name has no prefix
  (`ScheduleOne.Police`) and is what you need for IL2CPP-runtime type lookups
  (`Il2CppSystem.Type`, NPC/prefab registries, save keys). Both are noted where it matters.
- Il2CppInterop exposes IL2CPP **instance and static fields as C# properties**. So
  `static System.Single ARREST_TIME { public get; public set; }` in a dump is a *field* in
  the game, and `_Foo_k__BackingField` + `Foo` are the same storage — always use `Foo`.
- `field_Private_Boolean_0`, `Method_Protected_Virtual_Void_0`, `Method_Private_IEnumerator_PDM_0`
  are Il2CppInterop fallback names for members whose real name is not a legal C# identifier.
  **These are index-based and WILL renumber between game versions.** Never bind to them by
  name in shipping code without a version guard.
- `(inferred …)` marks a conclusion drawn from names/adjacency rather than read off a dump.
  **UNVERIFIED** marks something I could not confirm at all.

---

## 1. Assemblies & namespaces

Everything in scope lives in **`Assembly-CSharp`** (3597 types, path
`…\Schedule I\MelonLoader\Il2CppAssemblies\Assembly-CSharp.dll`).

| Namespace (Il2Cpp form) | Original CLR namespace | Types | Role |
|---|---|---|---|
| `Il2CppScheduleOne.Police` | `ScheduleOne.Police` | 6 | The officer NPC, its response set, road checkpoints, investigation timer, legacy offense DTO |
| `Il2CppScheduleOne.Law` | `ScheduleOne.Law` | 30 | Crime taxonomy (17 `Crime` subclasses), law intensity/schedule controller, dispatcher, curfew, checkpoint + patrol + sentry scheduling, fines |
| `Il2CppScheduleOne.Vision` | `ScheduleOne.Vision` | 12 | Vision cones, visibility scoring, visual-state notice events |
| `Il2CppScheduleOne.Combat` | `ScheduleOne.Combat` | 13 | Impacts, damage interfaces, the NPC combat behaviour that `PursuitBehaviour` extends |
| `Il2CppScheduleOne.NPCs.Behaviour` | `ScheduleOne.NPCs.Behaviour` | (large) | `PursuitBehaviour`, `VehiclePursuitBehaviour`, `BodySearchBehaviour`, `CheckpointBehaviour`, `FootPatrolBehaviour`, `VehiclePatrolBehaviour`, `SentryBehaviour`, `CallPoliceBehaviour`, patrol routes/groups |
| `Il2CppScheduleOne.PlayerScripts` | `ScheduleOne.PlayerScripts` | (large) | **`PlayerCrimeData` — this is where wanted level lives**, plus `Player.Arrest_*`/`Free_*` |
| `Il2CppScheduleOne.Map` | `ScheduleOne.Map` | (large) | `PoliceStation` — officer pool + dispatch |
| `Il2CppScheduleOne.UI` | `ScheduleOne.UI` | (large) | `ArrestScreen`, `ArrestNoticeScreen` (**confiscation lives here**), `CrimeStatusUI`, `OffenceNoticeUI` |
| `Il2CppScheduleOne.Persistence.Datas` | `ScheduleOne.Persistence.Datas` | (large) | `LawData` → `Law.json` |
| `Il2CppScheduleOne.Persistence.Loaders` | `ScheduleOne.Persistence.Loaders` | (large) | `LawLoader` |

**Out of scope after checking, as promised:**

- `Il2CppScheduleOne.Levelling` (5 types: `ERank`, `FullRank`, `LevelManager`, `RankData`,
  `Unlockable`) is the player XP/rank system. It has **no** member referencing law, police,
  crime, wanted or intensity. Not related.
- `Il2CppScheduleOne.Reporting` (9 types: `ReportManager`, `ReportInterface`,
  `ReportSubmission`, `ScreenshotUtil`, …) is the **bug-report / telemetry uploader**
  (`ReportManager.ServerUrl`, `SubmitReport`, `GetSaveGameBytes`). It has nothing to do with
  crime reporting. Crime reporting is `Il2CppScheduleOne.NPCs.Behaviour.CallPoliceBehaviour`
  → `Il2CppScheduleOne.Law.LawManager.PoliceCalled`.

Cross-reference: the installed **S1API.Forked** modding library ships mirrors
`S1API.Law` (11 types) and `S1API.Entities.NPCs.PoliceOfficers` (9 types). Those are a
second-hand but very useful corroboration and are cited explicitly where used.

---

## 2. `Il2CppScheduleOne.Police.PoliceOfficer` — full member dump

`public class Il2CppScheduleOne.Police.PoliceOfficer : Il2CppScheduleOne.NPCs.NPC`
(and `NPC : Il2CppFishNet.Object.NetworkBehaviour`). It has **no subclasses** in the shipped
game (verified in `03-subclasses.txt`; the only entry naming it is as a direct subclass of
`Il2CppScheduleOne.NPCs.NPC`, which has 81 direct subclasses).

### 2.1 Static members — the tuning knobs

```csharp
// Il2CppScheduleOne.Police.PoliceOfficer  (static)
static System.Single OutOfSightTimeToDeactivate    { public get; public set; }
static System.Single INVESTIGATION_COOLDOWN        { public get; public set; }
static System.Single INVESTIGATION_MAX_DISTANCE    { public get; public set; }
static System.Single INVESTIGATION_MIN_VISIBILITY  { public get; public set; }
static System.Single INVESTIGATION_CHECK_INTERVAL  { public get; public set; }
static System.Single BODY_SEARCH_CHANCE_DEFAULT    { public get; public set; }
static System.Single MIN_CHATTER_INTERVAL          { public get; public set; }
static System.Single MAX_CHATTER_INTERVAL          { public get; public set; }

static Il2CppSystem.Action<Il2CppScheduleOne.Vision.VisionEventReceipt> OnPoliceVisionEvent { public get; public set; }
static Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Police.PoliceOfficer> Officers { public get; public set; }

public static Il2CppScheduleOne.Police.PoliceOfficer GetNearestOfficer(
    UnityEngine.Vector3 position,
    out System.Single& distanceToTarget,
    System.Boolean onlyConscious = True);
```

`PoliceOfficer.Officers` is the global live registry of every officer instance — this is the
single cheapest handle a mod has on "all cops right now".
`PoliceOfficer.OnPoliceVisionEvent` is a global static broadcast fired for police vision
events; subscribing to it gives you every "a cop noticed something" event without patching
(inferred from name + the fact that `ProcessVisionEvent(VisionEventReceipt)` is the only
consumer-shaped method on the class).

### 2.2 Instance members

```csharp
// --- behaviour component references (assigned in the prefab) ---
Il2CppScheduleOne.NPCs.Behaviour.PursuitBehaviour        PursuitBehaviour        { public get; public set; }
Il2CppScheduleOne.NPCs.Behaviour.VehiclePursuitBehaviour VehiclePursuitBehaviour { public get; public set; }
Il2CppScheduleOne.NPCs.Behaviour.BodySearchBehaviour     BodySearchBehaviour     { public get; public set; }
Il2CppScheduleOne.NPCs.Behaviour.CheckpointBehaviour     CheckpointBehaviour     { public get; public set; }
Il2CppScheduleOne.NPCs.Behaviour.FootPatrolBehaviour     FootPatrolBehaviour     { public get; public set; }
Il2CppScheduleOne.NPCs.Behaviour.VehiclePatrolBehaviour  VehiclePatrolBehaviour  { public get; public set; }
Il2CppScheduleOne.NPCs.Behaviour.SentryBehaviour         SentryBehaviour         { public get; public set; }
Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppScheduleOne.NPCs.Behaviour.Behaviour> DeactivationBlockingBehaviours { public get; public set; }

// --- visuals / audio / equipment ---
Il2CppScheduleOne.FX.ProximityCircle                        ProxCircle        { public get; public set; }
Il2CppScheduleOne.VoiceOver.PoliceChatterVO                 ChatterVO         { public get; public set; }
Il2CppScheduleOne.Dialogue.DialogueContainer                CheckpointDialogue{ public get; public set; }
Il2CppScheduleOne.AvatarFramework.Equipping.AvatarEquippable BatonPrefab      { public get; public set; }
Il2CppScheduleOne.AvatarFramework.Equipping.AvatarEquippable TaserPrefab      { public get; public set; }
Il2CppScheduleOne.AvatarFramework.Equipping.AvatarEquippable GunPrefab        { public get; public set; }
Il2CppScheduleOne.AvatarFramework.PoliceBelt                 belt             { public get; public set; }

// --- tuning / state ---
System.Boolean AutoDeactivate      { public get; public set; }
System.Boolean ChatterEnabled      { public get; public set; }
System.Single  BodySearchDuration  { public get; public set; }
System.Single  BodySearchChance    { public get; public set; }   // backed by _BodySearchChance_k__BackingField
System.Single  timeSinceReadyToPool{ public get; public set; }
System.Single  timeSinceOutOfSight { public get; public set; }
System.Single  chatterCountDown    { public get; public set; }
Il2CppScheduleOne.Police.Investigation currentBodySearchInvestigation { public get; public set; }

// --- networked state ---
System.Boolean IgnorePlayers { public get; public set; }         // SyncVar
Il2CppFishNet.Object.Synchronizing.SyncVar<System.Boolean> syncVar____IgnorePlayers_k__BackingField { public get; public set; }
System.Boolean SyncAccessor_<IgnorePlayers>k__BackingField { public get; public set; }
Il2CppFishNet.Object.NetworkObject      PursuitTarget   { public get; }        // read-only
Il2CppScheduleOne.Vehicles.LandVehicle  AssignedVehicle { public get; public set; }

// --- Il2CppInterop fallback names — UNSTABLE ACROSS VERSIONS ---
System.Boolean field_Private_Boolean_0 { public get; public set; }
System.Boolean field_Private_Boolean_1 { public get; public set; }
```

### 2.3 Methods (71) — grouped, `virtual` marked exactly as dumped

**Pursuit (the entry points a mod calls or patches)**

```csharp
public         System.Void BeginFootPursuit(System.String playerCode);                                            // ObserversRpc entry
public virtual System.Void BeginFootPursuit_Networked(System.String playerCode, System.Boolean includeColleagues = True);  // ServerRpc entry
public         System.Void BeginVehiclePursuit(System.String playerCode, Il2CppFishNet.Object.NetworkObject vehicle, System.Boolean beginAsSighted);
public virtual System.Void BeginVehiclePursuit_Networked(System.String playerCode, Il2CppFishNet.Object.NetworkObject vehicle, System.Boolean beginAsSighted);
public         System.Void SetIgnorePlayers(System.Boolean ignore);                                               // ServerRpc entry
```

**Body search**

```csharp
public         System.Void    BeginBodySearch(System.String playerCode);                     // ObserversRpc entry
public virtual System.Void    BeginBodySearch_Networked(System.String playerCode);           // ServerRpc entry
public         System.Void    BodySearchLocalPlayer();
public         System.Void    ConductBodySearch(Il2CppScheduleOne.PlayerScripts.Player player);
public virtual System.Void    UpdateBodySearch();
public         System.Void    StopBodySearchInvestigation();
public         System.Boolean CanInvestigate();
public         System.Boolean CanInvestigatePlayer(Il2CppScheduleOne.PlayerScripts.Player player);
public         System.Void    CheckNewInvestigation();
public         System.Void    UpdateExistingInvestigation();
```

**Assignment / patrol / checkpoint / sentry**

```csharp
public virtual System.Void AssignToCheckpoint(Il2CppScheduleOne.Law.CheckpointManager+ECheckpointLocation location);  // ObserversRpc entry
public         System.Void UnassignFromCheckpoint();
public virtual System.Void AssignToSentryLocation(Il2CppScheduleOne.Law.SentryLocation location);
public         System.Void UnassignFromSentryLocation();
public         System.Void StartFootPatrol(Il2CppScheduleOne.NPCs.Behaviour.PatrolGroup group, System.Boolean warpToStartPoint);
public         System.Void StartVehiclePatrol(Il2CppScheduleOne.NPCs.Behaviour.VehiclePatrolRoute route, Il2CppScheduleOne.Vehicles.LandVehicle vehicle);
```

**Pooling / lifetime (this is the officer "spawn" mechanism, see §9)**

```csharp
public         System.Void Activate();
public         System.Void Deactivate();
public         System.Void CheckDeactivation();
public virtual System.Boolean ShouldSave();
public         System.Void SetAvoidancePriority(System.Int32 priority);
public         System.Void SetRandomAvoidancePriority();
```

**Perception**

```csharp
public         System.Void    ProcessVisionEvent(Il2CppScheduleOne.Vision.VisionEventReceipt visionEventReceipt);
public         System.Void    UpdateVision();
public         System.Boolean ShouldNoticeGeneralCrime(Il2CppScheduleOne.PlayerScripts.Player player);
```

**Unity / FishNet lifecycle**

```csharp
public virtual System.Void Awake();
public virtual System.Void Start();
public         System.Void Update();          // NOT virtual
public virtual System.Void OnTick();
public virtual System.Void OnDestroy();
public virtual System.Void NetworkInitializeIfDisabled();
public virtual System.Void NetworkInitialize__Late();
public virtual System.Void NetworkInitialize___Early();
public virtual System.Boolean ReadSyncVar___ScheduleOne_Police_PoliceOfficer(Il2CppFishNet.Serializing.PooledReader PooledReader0, System.UInt32 UInt321, System.Boolean Boolean2);
public         System.Boolean sync___get_value__IgnorePlayers_k__BackingField();
public         System.Void    sync___set_value__IgnorePlayers_k__BackingField(System.Boolean value, System.Boolean asServer);
```

**Misc**

```csharp
public virtual System.String GetNameAddress();
public         System.Void   UpdateChatter();
```

**Il2CppInterop fallback-named methods — UNSTABLE**

```csharp
public Il2CppSystem.Collections.IEnumerator Method_Private_IEnumerator_PDM_0();
public virtual System.Void                  Method_Protected_Virtual_Void_0();
public System.Boolean                       _Deactivate_b__66_1();
```

Mapping (inferred from `strings-global-metadata-ordered.txt`, which lists this type's real
member names in code order):
`Method_Private_IEnumerator_PDM_0` ⇒ `<Deactivate>g__Wait|66_0` (the local coroutine inside
`Deactivate()`), and `Method_Protected_Virtual_Void_0` ⇒
`Awake_UserLogic_ScheduleOne.Police.PoliceOfficer_Assembly-CSharp.dll`, i.e. **the real body
of `Awake()`** after FishNet's code generator split it. The same pattern holds across the
whole codebase: every type that exposes `Method_Protected_Virtual_Void_0` /
`Method_Protected_Virtual_New_Void_0` / `Method_Private_Void_PDM_0` also has exactly one
`Awake_UserLogic_<Type>_Assembly-CSharp.dll` string in the metadata table (checked for
`PlayerVisibility`, `EntityVisibility`, `VisionCone`, `CurfewManager`, `PoliceOfficer`,
`RoadCheckpoint`, `BodySearchBehaviour`, `PursuitBehaviour`, `SentryBehaviour`,
`VehiclePatrolBehaviour`, `CombatBehaviour`, `CombatManager`, `PlayerCrimeData`).
Consequence: **to run code before the officer's own `Awake` body, patch `Awake()`; to replace
it, patch the fallback-named method** — with a version guard.

### 2.4 The FishNet RPC triples on `PoliceOfficer`

| Public entry point | Direction | Writer | Real body | Reader |
|---|---|---|---|---|
| `BeginFootPursuit_Networked` | ServerRpc | `RpcWriter___Server_BeginFootPursuit_Networked_310431262` | `RpcLogic___BeginFootPursuit_Networked_310431262` | `RpcReader___Server_BeginFootPursuit_Networked_310431262` |
| `BeginFootPursuit` | ObserversRpc | `RpcWriter___Observers_BeginFootPursuit_3615296227` | `RpcLogic___BeginFootPursuit_3615296227` | `RpcReader___Observers_BeginFootPursuit_3615296227` |
| `BeginVehiclePursuit_Networked` | ServerRpc | `RpcWriter___Server_BeginVehiclePursuit_Networked_1834136777` | `RpcLogic___BeginVehiclePursuit_Networked_1834136777` | `RpcReader___Server_BeginVehiclePursuit_Networked_1834136777` |
| `BeginVehiclePursuit` | ObserversRpc | `RpcWriter___Observers_BeginVehiclePursuit_1834136777` | `RpcLogic___BeginVehiclePursuit_1834136777` | `RpcReader___Observers_BeginVehiclePursuit_1834136777` |
| `BeginBodySearch_Networked` | ServerRpc | `RpcWriter___Server_BeginBodySearch_Networked_3615296227` | `RpcLogic___BeginBodySearch_Networked_3615296227` | `RpcReader___Server_BeginBodySearch_Networked_3615296227` |
| `BeginBodySearch` | ObserversRpc | `RpcWriter___Observers_BeginBodySearch_3615296227` | `RpcLogic___BeginBodySearch_3615296227` | `RpcReader___Observers_BeginBodySearch_3615296227` |
| `AssignToCheckpoint` | ObserversRpc | `RpcWriter___Observers_AssignToCheckpoint_4087078542` | `RpcLogic___AssignToCheckpoint_4087078542` | `RpcReader___Observers_AssignToCheckpoint_4087078542` |
| `SetIgnorePlayers` | ServerRpc | `RpcWriter___Server_SetIgnorePlayers_1140765316` | `RpcLogic___SetIgnorePlayers_1140765316` | `RpcReader___Server_SetIgnorePlayers_1140765316` |

The `_Networked` suffix marks the **server-authoritative** entry; the un-suffixed twin is the
**observers broadcast** that plays it out on clients. So the canonical order is
`BeginFootPursuit_Networked` (client→server) → server runs
`RpcLogic___BeginFootPursuit_Networked_310431262` → server calls `BeginFootPursuit`
(server→all observers) → each client runs `RpcLogic___BeginFootPursuit_3615296227`.
Patch `RpcLogic___BeginFootPursuit_Networked_310431262` for authoritative changes.

### 2.5 The rest of `Il2CppScheduleOne.Police` (6 types total)

| Type | Base | Purpose |
|---|---|---|
| `Il2CppScheduleOne.Police.PoliceOfficer` | `Il2CppScheduleOne.NPCs.NPC` | The officer NPC (above) |
| `Il2CppScheduleOne.Police.NPCResponses_Police` | `Il2CppScheduleOne.NPCs.Responses.NPCResponses` | Turns perception events into police reactions. **This is the crime→pursuit bridge.** |
| `Il2CppScheduleOne.Police.NPCResponses_CartelGoon` | `Il2CppScheduleOne.NPCs.Responses.NPCResponses` | Cartel goon reaction set; lives in this namespace but is not law enforcement |
| `Il2CppScheduleOne.Police.RoadCheckpoint` | `Il2CppFishNet.Object.NetworkBehaviour` | Physical road checkpoint: two gates, car stoppers, vehicle detectors, traffic cones, stand points |
| `Il2CppScheduleOne.Police.Investigation` | `Il2CppSystem.Object` | Progress timer for "officer is deciding to body-search this player" |
| `Il2CppScheduleOne.Police.Offense` (+ nested `Offense+Charge`) | `Il2CppSystem.Object` | Charge-sheet DTO. Apparently legacy — see below |

There is **no `PoliceManager`, no `JailManager`, no holding cell, and no police spawn-point
type** anywhere in the dumps. Dispatch lives on `Il2CppScheduleOne.Map.PoliceStation` and
`Il2CppScheduleOne.Law.LawManager`; the checkpoint *scheduler* lives on
`Il2CppScheduleOne.Law.CheckpointManager`.

```csharp
public class Il2CppScheduleOne.Police.NPCResponses_Police : Il2CppScheduleOne.NPCs.Responses.NPCResponses
{
    Il2CppScheduleOne.Police.PoliceOfficer officer { public get; public set; }

    public virtual System.Void Awake();
    // --- vision-driven ---
    public virtual System.Void NoticedSuspiciousPlayer(Il2CppScheduleOne.PlayerScripts.Player player);
    public virtual System.Void NoticedViolatingCurfew(Il2CppScheduleOne.PlayerScripts.Player player);
    public virtual System.Void NoticedVandalism(Il2CppScheduleOne.PlayerScripts.Player player);
    public virtual System.Void NoticedPettyCrime(Il2CppScheduleOne.PlayerScripts.Player player);
    public virtual System.Void NoticedDrugDeal(Il2CppScheduleOne.PlayerScripts.Player player);
    public virtual System.Void NoticedWantedPlayer(Il2CppScheduleOne.PlayerScripts.Player player);
    public virtual System.Void NoticePlayerBrandishingWeapon(Il2CppScheduleOne.PlayerScripts.Player player);
    public virtual System.Void NoticePlayerDischargingWeapon(Il2CppScheduleOne.PlayerScripts.Player player);
    public virtual System.Void SawPickpocketing(Il2CppScheduleOne.PlayerScripts.Player player);
    public virtual System.Void PlayerFailedPickpocket(Il2CppScheduleOne.PlayerScripts.Player player);
    // --- sound / physical ---
    public virtual System.Void GunshotHeard(Il2CppScheduleOne.Noise.NoiseEvent gunshotSound);
    public virtual System.Void HitByCar(Il2CppScheduleOne.Vehicles.LandVehicle vehicle);
    public virtual System.Void ImpactReceived(Il2CppScheduleOne.Combat.Impact impact);
    public virtual System.Void RespondToAimedAt(Il2CppScheduleOne.PlayerScripts.Player player);
    public virtual System.Void RespondToAnnoyingImpact(Il2CppScheduleOne.PlayerScripts.Player perpetrator, Il2CppScheduleOne.Combat.Impact impact);
    public virtual System.Void RespondToFirstNonLethalAttack(Il2CppScheduleOne.PlayerScripts.Player perpetrator, Il2CppScheduleOne.Combat.Impact impact);
    public virtual System.Void RespondToRepeatedNonLethalAttack(Il2CppScheduleOne.PlayerScripts.Player perpetrator, Il2CppScheduleOne.Combat.Impact impact);
    public virtual System.Void RespondToLethalAttack(Il2CppScheduleOne.PlayerScripts.Player perpetrator, Il2CppScheduleOne.Combat.Impact impact);
}
```

Every one of these 19 methods is `virtual` — this is by far the cleanest surface for a
"police behave differently" mod.

```csharp
public class Il2CppScheduleOne.Police.RoadCheckpoint : Il2CppFishNet.Object.NetworkBehaviour
{
    static System.Single MAX_TIME_OPEN { public get; public set; }

    Il2CppScheduleOne.Police.RoadCheckpoint+ECheckpointState ActivationState { public get; public set; }
    Il2CppScheduleOne.Police.RoadCheckpoint+ECheckpointState appliedState    { public get; public set; }
    System.Boolean Gate1Open { public get; public set; }   // SyncVar
    System.Boolean Gate2Open { public get; public set; }   // SyncVar
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.NPCs.NPC> AssignedNPCs { public get; public set; }
    Il2CppScheduleOne.Product.Packaging.EStealthLevel MaxStealthLevel { public get; public set; }
    System.Boolean OpenForNPCs   { public get; public set; }
    System.Boolean EnabledOnStart{ public get; public set; }
    UnityEngine.GameObject container { public get; public set; }
    Il2CppScheduleOne.Misc.CarStopper Stopper1 { public get; public set; }
    Il2CppScheduleOne.Misc.CarStopper Stopper2 { public get; public set; }
    Il2CppScheduleOne.DevUtilities.VehicleDetector SearchArea1 { public get; public set; }
    Il2CppScheduleOne.DevUtilities.VehicleDetector SearchArea2 { public get; public set; }
    Il2CppScheduleOne.Vehicles.VehicleObstacle VehicleObstacle1 { public get; public set; }
    Il2CppScheduleOne.Vehicles.VehicleObstacle VehicleObstacle2 { public get; public set; }
    Il2CppScheduleOne.DevUtilities.VehicleDetector NPCVehicleDetectionArea1 { public get; public set; }
    Il2CppScheduleOne.DevUtilities.VehicleDetector NPCVehicleDetectionArea2 { public get; public set; }
    Il2CppScheduleOne.DevUtilities.VehicleDetector ImmediateVehicleDetector { public get; public set; }
    Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<UnityEngine.Rigidbody> TrafficCones { public get; public set; }
    Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<UnityEngine.Transform> StandPoints { public get; public set; }
    UnityEngine.Events.UnityEvent<Il2CppScheduleOne.PlayerScripts.Player> onPlayerWalkThrough { public get; public set; }
    System.Single  timeSinceGate1Open { public get; public set; }
    System.Boolean vehicleDetectedSinceGate1Open { public get; public set; }
    System.Single  timeSinceGate2Open { public get; public set; }
    System.Boolean vehicleDetectedSinceGate2Open { public get; public set; }

    public virtual System.Void ApplyState();
    public         System.Void Enable(Il2CppFishNet.Connection.NetworkConnection conn);   // ObserversRpc + TargetRpc, id 328543758
    public         System.Void Disable();                                                 // ObserversRpc, id 2166136261
    public         System.Void SetGate1Open(System.Boolean o);
    public         System.Void SetGate2Open(System.Boolean o);
    public         System.Void PlayerDetected(Il2CppScheduleOne.PlayerScripts.Player player);
    public         System.Void ResetTrafficCones();
    public         System.Boolean TryGetNearestAssignedNPC(out Il2CppScheduleOne.NPCs.NPC& npc, out System.Single& distance);
    public virtual System.Void Update();
    public virtual System.Void Method_Protected_Virtual_New_Void_0();  // = Awake_UserLogic_ScheduleOne.Police.RoadCheckpoint_… (inferred)
}

public enum Il2CppScheduleOne.Police.RoadCheckpoint+ECheckpointState : System.Int32
{
    Disabled = 0,
    Enabled  = 1,
}
```

```csharp
public class Il2CppScheduleOne.Police.Investigation : Il2CppSystem.Object
{
    System.Single                              CurrentProgress { public get; public set; }
    Il2CppScheduleOne.PlayerScripts.Player      Target          { public get; public set; }

    public .ctor(Il2CppScheduleOne.PlayerScripts.Player target);
    public System.Void ChangeProgress(System.Single progress);
}

// Legacy / apparently-unused charge sheet.
public class Il2CppScheduleOne.Police.Offense : Il2CppSystem.Object
{
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Police.Offense+Charge> charges   { public get; public set; }
    Il2CppSystem.Collections.Generic.List<System.String>                           penalties { public get; public set; }
    public .ctor(Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Police.Offense+Charge> _charges);
}

public class Il2CppScheduleOne.Police.Offense+Charge : Il2CppSystem.Object
{
    System.String chargeName { public get; public set; }
    System.Int32  crimeIndex { public get; public set; }
    System.Int32  quantity   { public get; public set; }
    public .ctor(System.String _chargeName, System.Int32 _crimeIndex, System.Int32 _quantity);
}
```

`Offense` is referenced by exactly one member in the entire dump set:
`Il2CppScheduleOne.UI.OffenceNoticeUI.ShowOffenceNotice(Il2CppScheduleOne.Police.Offense offence)`.
The live arrest UI is `Il2CppScheduleOne.UI.ArrestNoticeScreen`, which works on
`Dictionary<Il2CppScheduleOne.Law.Crime, System.Int32>` instead. Treat `Offense`/`Charge`
(and the `crimeIndex` int) as **dead legacy code** — but note I could not prove
`OffenceNoticeUI` is never shown, so that is *(inferred)*.

**Enums in `Il2CppScheduleOne.Police`:** only one, `RoadCheckpoint+ECheckpointState`
(above). `EPursuitLevel`, `EPursuitAction`, `ECheckpointLocation`, `EDispatchType`,
`EVisualState` live in other namespaces — see §4, §5, §7.

---

## 3. `Il2CppScheduleOne.Law` — 30 types

### 3.1 Type inventory

| Type | Base | One-line purpose |
|---|---|---|
| `LawController` | `Il2CppScheduleOne.DevUtilities.Singleton<LawController>` | **The intensity brain.** Holds `LE_Intensity`, `internalLawIntensity`, the seven per-day `LawActivitySettings`, saves `Law.json` |
| `LawManager` | `Il2CppScheduleOne.DevUtilities.Singleton<LawManager>` | **The dispatcher.** `PoliceCalled(player, crime)`, `StartFootpatrol`, `StartVehiclePatrol` |
| `LawActivitySettings` | `Il2CppSystem.Object` | One day's schedule: arrays of patrol / checkpoint / curfew / vehicle-patrol / sentry instances |
| `PatrolInstance` | `Il2CppSystem.Object` | A scheduled foot patrol (route, member count, start/end hour, `IntensityRequirement`) |
| `VehiclePatrolInstance` | `Il2CppSystem.Object` | A scheduled cruiser patrol |
| `CheckpointInstance` | `Il2CppSystem.Object` | A scheduled road checkpoint |
| `SentryInstance` | `Il2CppSystem.Object` | A scheduled static sentry post |
| `SentryLocation` (+ nested `SentryRoute`) | `UnityEngine.MonoBehaviour` | Scene-placed sentry post with patrol micro-routes |
| `CurfewInstance` | `Il2CppSystem.Object` | A scheduled curfew night |
| `CurfewManager` | `Il2CppScheduleOne.DevUtilities.NetworkSingleton<CurfewManager>` | Curfew clock, alarms, VMS boards, 7 UnityEvents |
| `CheckpointManager` (+ nested `ECheckpointLocation`) | `Il2CppScheduleOne.DevUtilities.NetworkSingleton<CheckpointManager>` | Owns the 4 `RoadCheckpoint`s and enables/disables them |
| `PenaltyHandler` | `Il2CppSystem.Object` (static class) | Fine table + `ProcessCrimeList` |
| `Crime` | `Il2CppSystem.Object` | Base crime |
| 17 × crime subclasses | `Il2CppScheduleOne.Law.Crime` | See §3.5 |

### 3.2 `LawController` — full dump

```csharp
public class Il2CppScheduleOne.Law.LawController
    : Il2CppScheduleOne.DevUtilities.Singleton<Il2CppScheduleOne.Law.LawController>
{
    static System.Single DAILY_INTENSITY_DRAIN { public get; public set; }

    System.Int32  LE_Intensity          { public get; public set; }
    System.Single internalLawIntensity  { public get; public set; }
    System.Single IntensityIncreasePerDay { public get; public set; }

    Il2CppScheduleOne.Law.LawActivitySettings MondaySettings    { public get; public set; }
    Il2CppScheduleOne.Law.LawActivitySettings TuesdaySettings   { public get; public set; }
    Il2CppScheduleOne.Law.LawActivitySettings WednesdaySettings { public get; public set; }
    Il2CppScheduleOne.Law.LawActivitySettings ThursdaySettings  { public get; public set; }
    Il2CppScheduleOne.Law.LawActivitySettings FridaySettings    { public get; public set; }
    Il2CppScheduleOne.Law.LawActivitySettings SaturdaySettings  { public get; public set; }
    Il2CppScheduleOne.Law.LawActivitySettings SundaySettings    { public get; public set; }

    System.Boolean                            OverrideSettings   { public get; public set; }
    Il2CppScheduleOne.Law.LawActivitySettings OverriddenSettings { public get; public set; }
    Il2CppScheduleOne.Law.LawActivitySettings CurrentSettings    { public get; public set; }

    Il2CppScheduleOne.Persistence.Loaders.LawLoader loader { public get; public set; }
    System.String SaveFolderName { public get; }
    System.String SaveFileName   { public get; }
    Il2CppScheduleOne.Persistence.Loaders.Loader Loader { public get; }
    System.Boolean ShouldSaveUnderFolder { public get; }
    System.Boolean HasChanged { public get; public set; }
    System.Int32   LoadOrder  { public get; }
    Il2CppSystem.Collections.Generic.List<System.String> LocalExtraFiles   { public get; public set; }
    Il2CppSystem.Collections.Generic.List<System.String> LocalExtraFolders { public get; public set; }

    public virtual System.Void   Awake();
    public virtual System.Void   Start();
    public virtual System.Void   OnDestroy();
    public         System.Void   ChangeInternalIntensity(System.Single change);
    public         System.Void   SetInternalIntensity(System.Single intensity);
    public         System.Void   DayPass();
    public         System.Void   OnUncappedMinPass();
    public         Il2CppScheduleOne.Law.LawActivitySettings GetSettings();
    public         Il2CppScheduleOne.Law.LawActivitySettings GetSettings(Il2CppScheduleOne.GameTime.EDay day);
    public         System.Void   OverrideSetings(Il2CppScheduleOne.Law.LawActivitySettings settings);   // sic: "Setings"
    public         System.Void   EndOverride();
    public virtual System.String GetSaveString();
    public virtual System.Void   InitializeSaveable();
    public         System.Void   Load(Il2CppScheduleOne.Persistence.Datas.LawData data);
    public         System.Void   OnLoadComplete();
}

public enum Il2CppScheduleOne.GameTime.EDay : System.Int32
{
    Monday = 0, Tuesday = 1, Wednesday = 2, Thursday = 3, Friday = 4, Saturday = 5, Sunday = 6,
}
```

Note the typo `OverrideSetings` (one `t`) — copy it exactly.

### 3.3 `LawManager` — full dump (tiny, and that is the point)

```csharp
public class Il2CppScheduleOne.Law.LawManager
    : Il2CppScheduleOne.DevUtilities.Singleton<Il2CppScheduleOne.Law.LawManager>
{
    static System.Int32  OfficerDispatchMin             { public get; public set; }
    static System.Int32  OfficerDispatchMax             { public get; public set; }
    static System.Single DISPATCH_VEHICLE_USE_THRESHOLD { public get; public set; }

    public         System.Void Start();                       // virtual
    public         System.Void PoliceCalled(Il2CppScheduleOne.PlayerScripts.Player target,
                                            Il2CppScheduleOne.Law.Crime crime);
    public Il2CppScheduleOne.NPCs.Behaviour.PatrolGroup StartFootpatrol(
                                            Il2CppScheduleOne.NPCs.Behaviour.FootPatrolRoute route,
                                            System.Int32 requestedMembers);
    public Il2CppScheduleOne.Police.PoliceOfficer StartVehiclePatrol(
                                            Il2CppScheduleOne.NPCs.Behaviour.VehiclePatrolRoute route);
}

// nested closure holder
public sealed class Il2CppScheduleOne.Law.LawManager+__c   // ObfuscatedName("ScheduleOne.Law.LawManager+<>c")
{
    static Il2CppScheduleOne.Law.LawManager+__c __9      { public get; public set; }
    static UnityEngine.Events.UnityAction        __9__3_0 { public get; public set; }
    public System.Void _Start_b__3_0();
}
```

`LawManager` has **no `Update`, no state, no officer list.** Everything it does happens in
`PoliceCalled`. `Start` registers a single `UnityAction` (`_Start_b__3_0`) — *(inferred: a
time-manager or curfew callback; I could not identify the event it subscribes to.)*

Relevant log literals (verbatim from `literals-sorted.txt`) that constrain `PoliceCalled`
and the dispatch path:

```
Police called on
Player is already being pursued, ignoring call police request.
Attempted to dispatch officers from a client, this is not allowed.
Attempted to dispatch more than 4 officers, this is not allowed.
Attempted to dispatch officers, but there are no officers in the pool.
Failed to pull officer from station
 has no officers in its pool!
Attempted to begin foot pursuit with null target
Attempted to deactivate an officer on the client
Target ({0}) is no longer wanted. (Pursuit level = {1})
 new pursuit level:
Registering pursuit level change event
```

So: dispatch is **server-only**, hard-capped at **4 officers**, and re-calling police on an
already-pursued player is a no-op.

### 3.4 `CurfewManager` — full dump

```csharp
public class Il2CppScheduleOne.Law.CurfewManager
    : Il2CppScheduleOne.DevUtilities.NetworkSingleton<Il2CppScheduleOne.Law.CurfewManager>
{
    static System.String NORMAL_MESSAGE  { public get; public set; }
    static System.String CURFEW_MESSAGE  { public get; public set; }
    static System.String WARNING_MESSAGE { public get; public set; }
    static System.Int32  HOUR_BEFORE_CURFEW     { public get; public set; }
    static System.Int32  WARNING_TIME           { public get; public set; }
    static System.Int32  CURFEW_START_TIME      { public get; public set; }
    static System.Int32  HARD_CURFEW_START_TIME { public get; public set; }
    static System.Int32  CURFEW_END_TIME        { public get; public set; }

    System.Boolean IsEnabled          { public get; public set; }
    System.Boolean IsCurrentlyActive  { public get; public set; }
    System.Boolean IsHardCurfewActive { public get; public set; }

    Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppScheduleOne.ObjectScripts.VMSBoard> VMSBoards { public get; public set; }
    Il2CppScheduleOne.Audio.AudioSourceController CurfewWarningSound { public get; public set; }
    Il2CppScheduleOne.Audio.AudioSourceController CurfewAlarmSound   { public get; public set; }

    UnityEngine.Events.UnityEvent onCurfewEnabled   { public get; public set; }
    UnityEngine.Events.UnityEvent onCurfewDisabled  { public get; public set; }
    UnityEngine.Events.UnityEvent onCurfewHint      { public get; public set; }
    UnityEngine.Events.UnityEvent onCurfewWarning   { public get; public set; }
    UnityEngine.Events.UnityEvent onCurfewStart     { public get; public set; }
    UnityEngine.Events.UnityEvent onCurfewHardStart { public get; public set; }
    UnityEngine.Events.UnityEvent onCurfewEnd       { public get; public set; }

    public virtual System.Void Awake();
    public virtual System.Void Start();
    public         System.Void Enable(Il2CppFishNet.Connection.NetworkConnection conn);   // ObserversRpc + TargetRpc 328543758
    public         System.Void Disable();                                                 // ObserversRpc 2166136261
    public         System.Void OnUncappedMinPass();
    public virtual System.Void OnSpawnServer(Il2CppFishNet.Connection.NetworkConnection connection);
    public virtual System.Void Method_Protected_Virtual_Void_0();   // = Awake_UserLogic_ScheduleOne.Law.CurfewManager_… (inferred)
    // + NetworkInitialize*/RpcWriter/RpcLogic/RpcReader triples for Enable & Disable
}
```

**Constant values.** The IL2CPP dump gives names and types only. `S1API.Law.CurfewManager`
(shipped `S1API.Forked` 3.1.4, which reads these at runtime) mirrors them as:

```csharp
public const System.Int32 HourBeforeCurfew     = 2000;   // 20:00
public const System.Int32 WarningTime          = 2030;   // 20:30
public const System.Int32 CurfewStartTime      = 2100;   // 21:00
public const System.Int32 HardCurfewStartTime  = 2115;   // 21:15
public const System.Int32 CurfewEndTime        =  500;   // 05:00
```

Corroborated by the in-game literals `CURFEW ACTIVE\n UNTIL 5AM`,
`CURFEW TONIGHT\n9PM - 5AM`, `Police curfew in effect until 5AM`,
`Police curfew starting soon`, `CURFEW SOON\n{0} MINS`. **These values are second-hand
(S1API), not read out of `global-metadata.dat`** — high confidence, but flagged.

```csharp
public class Il2CppScheduleOne.Law.CurfewInstance : Il2CppSystem.Object
{
    static Il2CppScheduleOne.Law.CurfewInstance ActiveInstance { public get; public set; }
    System.Int32   IntensityRequirement { public get; public set; }
    System.Boolean Enabled              { public get; public set; }
    System.Boolean shouldDisable        { public get; public set; }

    public System.Void Evaluate(System.Boolean ignoreSleepReq = False);
    public System.Void MinPass();
    public System.Void Enable();
    public System.Void Disable();
}
```

### 3.5 `Crime` and its 17 subclasses

```csharp
public class Il2CppScheduleOne.Law.Crime : Il2CppSystem.Object
{
    System.String _CrimeName_k__BackingField { public get; public set; }
    System.String CrimeName                  { public get; public set; }

    private .ctor();
    public  .ctor();
    public  .ctor(System.IntPtr pointer);
}
```

That is the **entire** base class. There is:

- **no severity field** — severity is encoded purely in *which subclass* you instantiate
  (`PossessingLowSeverityDrug` vs `PossessingHighSeverityDrug`) and in the matching
  `PenaltyHandler` fine constant;
- **no ID, no index, no static registry, no `Crime.All`** — nothing to register into;
- **no methods at all.**

Every one of the 17 subclasses has *exactly* the same shape (a re-declared `CrimeName`
auto-property with its own backing field, three constructors, no methods). Two
representative dumps, verbatim:

```csharp
public class Il2CppScheduleOne.Law.Evading : Il2CppScheduleOne.Law.Crime
{
    System.String _CrimeName_k__BackingField { public get; public set; }
    System.String CrimeName                  { public get; public set; }
    private .ctor();
    public  .ctor();
    public  .ctor(System.IntPtr pointer);
}

public class Il2CppScheduleOne.Law.PossessingHighSeverityDrug : Il2CppScheduleOne.Law.Crime
{
    System.String _CrimeName_k__BackingField { public get; public set; }
    System.String CrimeName                  { public get; public set; }
    private .ctor();
    public  .ctor();
    public  .ctor(System.IntPtr pointer);
}

public class Il2CppScheduleOne.Law.ViolatingCurfew : Il2CppScheduleOne.Law.Crime
{
    System.String _CrimeName_k__BackingField { public get; public set; }
    System.String CrimeName                  { public get; public set; }
    private .ctor();
    public  .ctor();
    public  .ctor(System.IntPtr pointer);
}
```

Because each subclass redeclares the backing field, `Crime.CrimeName` is `virtual` in the
original source and each subclass does `public override string CrimeName { get; set; } = "…";`
— *(inferred; the dumper does not print property-accessor virtualness, so this is
**UNVERIFIED** in the strict sense.)*

**All 17 subclasses**, alphabetical as listed in `03-subclasses.txt`
(`BASE Il2CppScheduleOne.Law.Crime   (17 direct subclasses)`):

`Assault`, `AttemptingToSell`, `BrandishingWeapon`, `DeadlyAssault`, `DischargeFirearm`,
`DrugTrafficking`, `Evading`, `FailureToComply`, `PossessingControlledSubstances`,
`PossessingHighSeverityDrug`, `PossessingLowSeverityDrug`, `PossessingModerateSeverityDrug`,
`Theft`, `TransportingIllicitItems`, `Vandalism`, `VehicularAssault`, `ViolatingCurfew`.

**Declaration order** (from `strings-global-metadata-ordered.txt`, which is code order — this
is the order that would matter if anything ever used `Offense+Charge.crimeIndex`):

```
0  PossessingControlledSubstances
1  PossessingLowSeverityDrug
2  PossessingModerateSeverityDrug
3  PossessingHighSeverityDrug
4  Evading
5  VehicularAssault
6  DrugTrafficking
7  FailureToComply
8  TransportingIllicitItems
9  ViolatingCurfew
10 AttemptingToSell
11 Assault
12 DeadlyAssault
13 Vandalism
14 Theft
15 BrandishingWeapon
16 DischargeFirearm
```

*(inferred mapping to `crimeIndex`; nothing in the dumps proves that index is used.)*

Known `CrimeName` string literals present in the metadata: `Violating curfew`,
`Failure to comply with police instruction`. The rest were not isolated (see §12).

### 3.6 How a crime is *recorded*

There is no `CrimeData` type. The recorder is `PlayerCrimeData` (§4):

```csharp
public System.Void    AddCrime(Il2CppScheduleOne.Law.Crime crime, System.Int32 quantity = 1);
public System.Void    ClearCrimes();
public System.Boolean IsCrimeOnRecord(Il2CppSystem.Type crime);
Il2CppSystem.Collections.Generic.Dictionary<Il2CppScheduleOne.Law.Crime, System.Int32> Crimes { public get; public set; }
```

`IsCrimeOnRecord` takes an **`Il2CppSystem.Type`**, so lookups are by runtime type, not by
name or id. From a mod: `Il2CppInterop.Runtime.Il2CppType.Of<Il2CppScheduleOne.Law.Evading>()`.

### 3.7 `PenaltyHandler` — the fine table

```csharp
public static class Il2CppScheduleOne.Law.PenaltyHandler : Il2CppSystem.Object
{
    static System.Single CONTROLLED_SUBSTANCE_FINE { public get; public set; }
    static System.Single LOW_SEVERITY_DRUG_FINE    { public get; public set; }
    static System.Single MED_SEVERITY_DRUG_FINE    { public get; public set; }
    static System.Single HIGH_SEVERITY_DRUG_FINE   { public get; public set; }
    static System.Single FAILURE_TO_COMPLY_FINE    { public get; public set; }
    static System.Single EVADING_ARREST_FINE       { public get; public set; }
    static System.Single VIOLATING_CURFEW_TIME     { public get; public set; }   // name says TIME, siblings say FINE
    static System.Single ATTEMPT_TO_SELL_FINE      { public get; public set; }
    static System.Single ASSAULT_FINE              { public get; public set; }
    static System.Single DEADLY_ASSAULT_FINE       { public get; public set; }
    static System.Single VANDALISM_FINE            { public get; public set; }
    static System.Single THEFT_FINE                { public get; public set; }
    static System.Single BRANDISHING_FINE          { public get; public set; }
    static System.Single DISCHARGE_FIREARM_FINE    { public get; public set; }

    public static Il2CppSystem.Collections.Generic.List<System.String> ProcessCrimeList(
        Il2CppSystem.Collections.Generic.Dictionary<Il2CppScheduleOne.Law.Crime, System.Int32> crimes);
}
```

Only **14** fine constants for **17** crimes: `DrugTrafficking`, `TransportingIllicitItems`
and `VehicularAssault` have no dedicated constant. *(inferred: they either reuse another
constant or produce no monetary penalty.)*

`ProcessCrimeList` returns the human-readable penalty strings shown on the arrest notice. The
matching literals in `literals-sorted.txt` are (verbatim, leading spaces preserved):

```
 fine
 controlled substances confiscated
 low-severity drugs confiscated
 moderate-severity drugs confiscated
 high-severity drugs confiscated
```

**There are no jail / detention / "time served" literals anywhere in the metadata.** I
searched `jail`, `detain`, `custody`, `held`, `overnight`, `night in`, `Time served`,
`Released` — nothing law-related. Confirmed: **there is no jail or holding-cell system.**

### 3.8 Scheduling instances (patrol / checkpoint / sentry / vehicle patrol)

All five share the same shape: designer-authored data objects evaluated once a game minute.

```csharp
public class Il2CppScheduleOne.Law.LawActivitySettings : Il2CppSystem.Object
{
    Il2CppReferenceArray<Il2CppScheduleOne.Law.PatrolInstance>        Patrols        { public get; public set; }
    Il2CppReferenceArray<Il2CppScheduleOne.Law.CheckpointInstance>    Checkpoints    { public get; public set; }
    Il2CppReferenceArray<Il2CppScheduleOne.Law.CurfewInstance>        Curfews        { public get; public set; }
    Il2CppReferenceArray<Il2CppScheduleOne.Law.VehiclePatrolInstance> VehiclePatrols { public get; public set; }
    Il2CppReferenceArray<Il2CppScheduleOne.Law.SentryInstance>        Sentries       { public get; public set; }

    public System.Void Evaluate();
    public System.Void OnLoaded();
    public System.Void End();
}

public class Il2CppScheduleOne.Law.PatrolInstance : Il2CppSystem.Object
{
    Il2CppScheduleOne.NPCs.Behaviour.FootPatrolRoute Route { public get; public set; }
    System.Int32   MinMembers           { public get; public set; }
    System.Int32   MaxMembers           { public get; public set; }
    System.Int32   StartTime            { public get; public set; }
    System.Int32   EndTime              { public get; public set; }
    System.Int32   IntensityRequirement { public get; public set; }
    System.Boolean OnlyIfCurfewEnabled  { public get; public set; }
    Il2CppScheduleOne.NPCs.Behaviour.PatrolGroup ActiveGroup { public get; public set; }

    public System.Void Evaluate();
    public System.Void MinPass();
    public System.Void StartPatrol();
    public System.Void EndPatrol();
}

public class Il2CppScheduleOne.Law.CheckpointInstance : Il2CppSystem.Object
{
    static System.Single MIN_ACTIVATION_DISTANCE { public get; public set; }
    Il2CppScheduleOne.Law.CheckpointManager+ECheckpointLocation Location { public get; public set; }
    System.Int32   MinMembers           { public get; public set; }
    System.Int32   MaxMembers           { public get; public set; }
    System.Int32   StartTime            { public get; public set; }
    System.Int32   EndTime              { public get; public set; }
    System.Int32   IntensityRequirement { public get; public set; }
    System.Boolean OnlyIfCurfewEnabled  { public get; public set; }
    Il2CppScheduleOne.Police.RoadCheckpoint checkPoint        { public get; public set; }
    Il2CppScheduleOne.Police.RoadCheckpoint activeCheckpoint  { public get; public set; }

    public System.Void    Evaluate();
    public System.Void    MinPass();
    public System.Boolean DistanceRequirementsMet();
    public System.Void    EnableCheckpoint();
    public System.Void    DisableCheckpoint();
}

public class Il2CppScheduleOne.Law.VehiclePatrolInstance : Il2CppSystem.Object
{
    Il2CppScheduleOne.NPCs.Behaviour.VehiclePatrolRoute Route { public get; public set; }
    System.Int32   StartTime            { public get; public set; }
    System.Int32   IntensityRequirement { public get; public set; }
    System.Boolean OnlyIfCurfewEnabled  { public get; public set; }
    Il2CppScheduleOne.Police.PoliceOfficer activeOfficer   { public get; public set; }
    System.Int32   latestStartTime      { public get; public set; }
    System.Boolean startedThisCycle     { public get; public set; }
    Il2CppScheduleOne.Map.PoliceStation nearestStation { public get; }

    public System.Void Evaluate();
    public System.Void StartPatrol();
    public System.Void CheckEnd();
}

public class Il2CppScheduleOne.Law.SentryInstance : Il2CppSystem.Object
{
    Il2CppReferenceArray<Il2CppScheduleOne.Law.SentryLocation> _potentialLocations { public get; public set; }
    System.Int32   MinMembers           { public get; public set; }
    System.Int32   MaxMembers           { public get; public set; }
    System.Int32   StartTime            { public get; public set; }
    System.Int32   EndTime              { public get; public set; }
    System.Int32   IntensityRequirement { public get; public set; }
    System.Boolean OnlyIfCurfewEnabled  { public get; public set; }
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Police.PoliceOfficer> _activeOfficers { public get; public set; }
    Il2CppScheduleOne.Law.SentryLocation _activeLocation { public get; public set; }

    public System.Void Evaluate();
    public System.Void MinPass();
    public System.Void StartEntry();      // sic: "StartEntry", not "StartSentry"
    public System.Void EndSentry();
    public Il2CppScheduleOne.Law.SentryLocation GetRandomUnoccupiedLocation();
}

public class Il2CppScheduleOne.Law.SentryLocation : UnityEngine.MonoBehaviour
{
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Law.SentryLocation+SentryRoute> Routes { public get; public set; }
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Police.PoliceOfficer> AssignedOfficers { public get; public set; }
}

public class Il2CppScheduleOne.Law.SentryLocation+SentryRoute : Il2CppSystem.Object
{
    Il2CppReferenceArray<UnityEngine.Transform> RoutePoints    { public get; public set; }
    System.Int32                                MinutesPerPoint{ public get; public set; }
}
```

`IntensityRequirement` on every one of these is the single mechanism by which
`LawController.LE_Intensity` translates into more cops on the street. **This is the lever a
"dynamic intensity" mod wants.**

### 3.9 `CheckpointManager`

```csharp
public class Il2CppScheduleOne.Law.CheckpointManager
    : Il2CppScheduleOne.DevUtilities.NetworkSingleton<Il2CppScheduleOne.Law.CheckpointManager>
{
    Il2CppScheduleOne.Police.RoadCheckpoint WesternCheckpoint         { public get; public set; }
    Il2CppScheduleOne.Police.RoadCheckpoint DocksCheckpoint           { public get; public set; }
    Il2CppScheduleOne.Police.RoadCheckpoint NorthResidentialCheckpoint{ public get; public set; }
    Il2CppScheduleOne.Police.RoadCheckpoint WestResidentialCheckpoint { public get; public set; }

    public virtual System.Void Awake();
    public virtual System.Void OnSpawnServer(Il2CppFishNet.Connection.NetworkConnection connection);
    public Il2CppScheduleOne.Police.RoadCheckpoint GetCheckpoint(Il2CppScheduleOne.Law.CheckpointManager+ECheckpointLocation loc);
    public System.Void SetCheckpointEnabled(Il2CppScheduleOne.Law.CheckpointManager+ECheckpointLocation checkpoint,
                                            System.Boolean enabled,
                                            System.Int32 requestedOfficers = 1);
}

public enum Il2CppScheduleOne.Law.CheckpointManager+ECheckpointLocation : System.Int32
{
    Western          = 0,
    Docks            = 1,
    NorthResidential = 2,
    WestResidential  = 3,
}
```

There are exactly **4 road checkpoints in the game**.

---

## 4. Wanted level, pursuit, and the arrest flow

### 4.1 Where the wanted level lives — answered

**`Il2CppScheduleOne.PlayerScripts.PlayerCrimeData`**, a `NetworkBehaviour` component on the
player object. The wanted level itself is the FishNet **SyncVar**
`CurrentPursuitLevel` of type `PlayerCrimeData+EPursuitLevel`.

It is **per-player**, **server-authoritative**, and **not persisted** (see §10 — no crime or
pursuit field exists in any `Persistence.Datas` type).

```csharp
public enum Il2CppScheduleOne.PlayerScripts.PlayerCrimeData+EPursuitLevel : System.Int32
{
    None          = 0,
    Investigating = 1,
    Arresting     = 2,
    NonLethal     = 3,
    Lethal        = 4,
}
```

### 4.2 `PlayerCrimeData` — full dump

```csharp
public class Il2CppScheduleOne.PlayerScripts.PlayerCrimeData : Il2CppFishNet.Object.NetworkBehaviour
{
    // ---- statics (tuning) ----
    static System.Single SEARCH_TIME_INVESTIGATING { public get; public set; }
    static System.Single SEARCH_TIME_ARRESTING     { public get; public set; }
    static System.Single SEARCH_TIME_NONLETHAL     { public get; public set; }
    static System.Single SEARCH_TIME_LETHAL        { public get; public set; }
    static System.Single ESCALATION_TIME_ARRESTING { public get; public set; }
    static System.Single ESCALATION_TIME_NONLETHAL { public get; public set; }
    static System.Single SHOT_COOLDOWN_MIN         { public get; public set; }
    static System.Single SHOT_COOLDOWN_MAX         { public get; public set; }
    static System.Single VEHICLE_COLLISION_LIFETIME{ public get; public set; }
    static System.Single VEHICLE_COLLISION_LIMIT   { public get; public set; }

    // ---- state ----
    Il2CppScheduleOne.PlayerScripts.Player  Player        { public get; public set; }
    Il2CppScheduleOne.Police.PoliceOfficer  NearestOfficer{ public get; public set; }
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Police.PoliceOfficer> Pursuers { public get; public set; }

    Il2CppScheduleOne.PlayerScripts.PlayerCrimeData+EPursuitLevel CurrentPursuitLevel { public get; public set; }  // SyncVar
    UnityEngine.Vector3 LastKnownPosition { public get; public set; }                                              // SyncVar

    System.Single  CurrentArrestProgress      { public get; public set; }
    System.Single  CurrentBodySearchProgress  { public get; public set; }
    System.Int32   MinsSinceLastArrested      { public get; public set; }
    System.Single  TimeSincePursuitStart      { public get; public set; }
    System.Single  CurrentPursuitLevelDuration{ public get; public set; }
    System.Single  TimeSinceSighted           { public get; public set; }
    System.Boolean BodySearchPending          { public get; public set; }
    System.Single  TimeSinceLastBodySearch    { public get; public set; }
    System.Boolean EvadedArrest               { public get; public set; }

    Il2CppSystem.Collections.Generic.Dictionary<Il2CppScheduleOne.Law.Crime, System.Int32> Crimes { public get; public set; }
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.PlayerScripts.PlayerCrimeData+VehicleCollisionInstance> Collisions { public get; public set; }

    Il2CppScheduleOne.Audio.AudioSourceController onPursuitEscapedSound { public get; public set; }
    Il2CppSystem.Action<Il2CppScheduleOne.PlayerScripts.PlayerCrimeData+EPursuitLevel,
                        Il2CppScheduleOne.PlayerScripts.PlayerCrimeData+EPursuitLevel> onPursuitLevelChange { public get; public set; }

    Il2CppFishNet.Object.Synchronizing.SyncVar<Il2CppScheduleOne.PlayerScripts.PlayerCrimeData+EPursuitLevel> syncVar____CurrentPursuitLevel_k__BackingField { public get; public set; }
    Il2CppFishNet.Object.Synchronizing.SyncVar<UnityEngine.Vector3> syncVar____LastKnownPosition_k__BackingField { public get; public set; }

    // ---- wanted-level control ----
    public System.Void SetPursuitLevel(Il2CppScheduleOne.PlayerScripts.PlayerCrimeData+EPursuitLevel level);
    public System.Void SetPursuitLevel_Server(Il2CppScheduleOne.PlayerScripts.PlayerCrimeData+EPursuitLevel level);  // ServerRpc 2979171596
    public System.Void Escalate();
    public System.Void Deescalate();
    public System.Void UpdateEscalation();
    public System.Void UpdateTimeout();
    public System.Void TimeoutPursuit();
    public System.Void SetEvaded();

    // ---- crime record ----
    public System.Void    AddCrime(Il2CppScheduleOne.Law.Crime crime, System.Int32 quantity = 1);
    public System.Void    ClearCrimes();
    public System.Boolean IsCrimeOnRecord(Il2CppSystem.Type crime);

    // ---- arrest / search progress ----
    public System.Void SetArrestProgress(System.Single progress);
    public System.Void SetBodySearchProgress(System.Single progress);
    public System.Void ResetBodysearchCooldown();

    // ---- tracking ----
    public System.Void RecordLastKnownPosition(System.Boolean resetTimeSinceSighted);   // ObserversRpc 1140765316
    public System.Void RecordVehicleCollision(Il2CppScheduleOne.NPCs.NPC victim);
    public System.Void CheckNearestOfficer();
    public System.Single GetSearchTime();
    public System.Single GetShotAccuracyMultiplier();

    // ---- lifecycle hooks ----
    public System.Void OnPlayerFreed();
    public System.Void OnDie();
    public System.Void OnSleepStart();
    public System.Void MinPass();
    public virtual System.Void Awake();
    public         System.Void Start();
    public virtual System.Void Update();
    public virtual System.Void LateUpdate();
    public         System.Void OnDestroy();
    public         System.Void Method_Private_Void_PDM_0();   // = Awake_UserLogic_ScheduleOne.PlayerScripts.PlayerCrimeData_… (inferred)
    public         System.Single _CheckNearestOfficer_b__78_0(Il2CppScheduleOne.Police.PoliceOfficer x);
}

public class Il2CppScheduleOne.PlayerScripts.PlayerCrimeData+VehicleCollisionInstance : Il2CppSystem.Object
{
    Il2CppScheduleOne.NPCs.NPC Victim    { public get; public set; }
    System.Single              TimeSince { public get; public set; }
    public .ctor(Il2CppScheduleOne.NPCs.NPC victim, System.Single timeSince);
}
```

`onPursuitLevelChange` is an `Action<EPursuitLevel /*old*/, EPursuitLevel /*new*/>` — a
**public, no-patch-required subscription point** for any mod that reacts to wanted level.

### 4.3 Arrest state on `Player`

```csharp
// Il2CppScheduleOne.PlayerScripts.Player  (selected members)
System.Boolean IsArrested    { public get; public set; }
System.Boolean IsTased       { public get; public set; }
System.Boolean IsRagdolled   { public get; public set; }
System.Boolean IsUnconscious { public get; public set; }
System.String  PlayerCode    { public get; public set; }   // SyncVar; the id passed to BeginFootPursuit(playerCode)

Il2CppSystem.Action onArrested { public get; public set; }
Il2CppSystem.Action onFreed    { public get; public set; }
Il2CppSystem.Action onTased    { public get; public set; }
Il2CppSystem.Action onTasedEnd { public get; public set; }

public System.Void Arrest_Server();    // ServerRpc     2166136261
public System.Void Arrest_Client();    // ObserversRpc  2166136261
public System.Void Free_Server();      // ServerRpc     2166136261
public System.Void Free_Client();      // ObserversRpc  2166136261
public System.Void SetRagdolled(System.Boolean ragdolled);
public static Il2CppScheduleOne.PlayerScripts.Player GetRandomPlayer(System.Boolean excludeArrestedOrDead = True, System.Boolean excludeSleeping = True);
```

RPC triples: `RpcWriter___Server_Arrest_Server_2166136261` / `RpcLogic___Arrest_Server_2166136261` /
`RpcReader___Server_Arrest_Server_2166136261`, and the `Observers_Arrest_Client_*` /
`Observers_Free_Client_*` / `Server_Free_Server_*` counterparts.
`Player.onArrested` and `Player.onFreed` are the cheap subscription points.

### 4.4 Confiscation — **yes, it exists**

```csharp
public class Il2CppScheduleOne.UI.ArrestNoticeScreen
    : Il2CppScheduleOne.DevUtilities.Singleton<Il2CppScheduleOne.UI.ArrestNoticeScreen>
{
    static System.Single VEHICLE_POSSESSION_TIMEOUT { public get; public set; }

    System.Boolean         isOpen              { public get; public set; }
    UnityEngine.Canvas     Canvas              { public get; public set; }
    UnityEngine.CanvasGroup CanvasGroup        { public get; public set; }
    UnityEngine.RectTransform CrimeEntryContainer   { public get; public set; }
    UnityEngine.RectTransform PenaltyEntryContainer { public get; public set; }
    UnityEngine.RectTransform CrimeEntryPrefab      { public get; public set; }
    UnityEngine.RectTransform PenaltyEntryPrefab    { public get; public set; }
    Il2CppScheduleOne.State.MonoState State       { public get; public set; }
    Il2CppSystem.Collections.Generic.Dictionary<Il2CppScheduleOne.Law.Crime, System.Int32> recordedCrimes { public get; public set; }
    Il2CppScheduleOne.Vehicles.LandVehicle vehicle { public get; public set; }

    public virtual System.Void Awake();
    public         System.Void Open();
    public         System.Void OnClose();
    public         System.Void Exit();
    public         System.Void RecordCrimes();
    public         System.Void RecordPossession(Il2CppScheduleOne.Product.Packaging.EStealthLevel maxStealthLevel);
    public         System.Void ConfiscateItems(Il2CppScheduleOne.Product.Packaging.EStealthLevel maxStealthLevel);   // <=== CONFISCATION
    public         System.Void ClearEntries();
    public         System.Void PlayerSpawned();
    public Il2CppSystem.Collections.IEnumerator Method_Private_IEnumerator_PDM_0();   // = <OnClose>g__CloseRoutine|18_0 (inferred)
    public System.Boolean _Awake_b__14_0();
}
```

**The method is `Il2CppScheduleOne.UI.ArrestNoticeScreen.ConfiscateItems(EStealthLevel maxStealthLevel)`.**
`RecordPossession(EStealthLevel)` builds the "what you were carrying" list;
`ConfiscateItems(EStealthLevel)` removes it. The `maxStealthLevel` argument means
**better packaging survives confiscation** — anything at or below the officer's/checkpoint's
`MaxStealthLevel` gets taken.

```csharp
public enum Il2CppScheduleOne.Product.Packaging.EStealthLevel : System.Int32
{
    None     = 0,
    Basic    = 1,
    Advanced = 2,
}
```

Confiscation result strings (verbatim literals): ` controlled substances confiscated`,
` low-severity drugs confiscated`, ` moderate-severity drugs confiscated`,
` high-severity drugs confiscated`, ` fine`.

**Cash:** I found no `cash seized` / `cash confiscated` literal, and no cash-removal method on
`ArrestNoticeScreen`. Penalties are delivered as a `List<System.String>` from
`PenaltyHandler.ProcessCrimeList`, and the fine amount presumably reaches
`Il2CppScheduleOne.Money.MoneyManager` from `ArrestNoticeScreen.OnClose` or `Exit` —
**UNVERIFIED**; I could not find the call site from metadata alone.

### 4.5 Other arrest UI

```csharp
public class Il2CppScheduleOne.UI.ArrestScreen
    : Il2CppScheduleOne.DevUtilities.Singleton<Il2CppScheduleOne.UI.ArrestScreen>
{
    System.Boolean isOpen { public get; public set; }
    UnityEngine.Canvas canvas { public get; public set; }
    UnityEngine.CanvasGroup group { public get; public set; }
    Il2CppScheduleOne.Audio.AudioSourceController Sound { public get; public set; }
    UnityEngine.Animation Anim { public get; public set; }
    Il2CppScheduleOne.State.MonoState State { public get; public set; }

    public virtual System.Void Awake();
    public         System.Void Open();
    public         System.Void Continue();
    public         System.Void Close();
}

public class Il2CppScheduleOne.UI.CrimeStatusUI : UnityEngine.MonoBehaviour
{
    static System.Single SmallTextSize { public get; public set; }
    static System.Single LargeTextSize { public get; public set; }
    UnityEngine.RectTransform CrimeStatusContainer { public get; public set; }
    UnityEngine.CanvasGroup   CrimeStatusGroup     { public get; public set; }
    UnityEngine.GameObject    BodysearchLabel      { public get; public set; }
    UnityEngine.UI.Image      InvestigatingMask    { public get; public set; }
    UnityEngine.UI.Image      UnderArrestMask      { public get; public set; }
    UnityEngine.UI.Image      WantedMask           { public get; public set; }
    UnityEngine.UI.Image      WantedDeadMask       { public get; public set; }
    UnityEngine.GameObject    ArrestProgressContainer { public get; public set; }

    public System.Void UpdateStatus();
    public Il2CppSystem.Collections.IEnumerator Routine();
}
```

`CrimeStatusUI` is the on-screen wanted indicator; its four masks map 1:1 onto
`EPursuitLevel.Investigating / Arresting / NonLethal / Lethal` *(inferred from names)*.
`CrimeStatusUI.UpdateStatus()` is the cleanest place to inject a custom "outlaw" indicator.

### 4.6 `PursuitBehaviour` — the arrest driver

```csharp
public class Il2CppScheduleOne.NPCs.Behaviour.PursuitBehaviour : Il2CppScheduleOne.Combat.CombatBehaviour
{
    static System.Single ARREST_RANGE                   { public get; public set; }
    static System.Single ARREST_TIME                    { public get; public set; }
    static System.Single EXTRA_VISIBILITY_TIME          { public get; public set; }
    static System.Single MOVE_SPEED_INVESTIGATING       { public get; public set; }
    static System.Single MOVE_SPEED_ARRESTING           { public get; public set; }
    static System.Single MOVE_SPEED_CHASE               { public get; public set; }
    static System.Single CHASE_SPEED_DISTANCE_THRESHOLD { public get; public set; }
    static System.Single ARREST_MAX_DISTANCE            { public get; public set; }
    static System.Int32  LEAVE_ARREST_CIRCLE_LIMIT      { public get; public set; }

    Il2CppScheduleOne.PlayerScripts.Player TargetPlayer { public get; public set; }
    System.Single ArrestCircle_MaxVisibleDistance { public get; public set; }
    System.Single ArrestCircle_MaxOpacity         { public get; public set; }
    Il2CppScheduleOne.AvatarFramework.Equipping.AvatarWeapon Weapon_Baton { public get; public set; }
    Il2CppScheduleOne.AvatarFramework.Equipping.AvatarWeapon Weapon_Taser { public get; public set; }
    Il2CppScheduleOne.AvatarFramework.Equipping.AvatarWeapon Weapon_Gun   { public get; public set; }
    System.Boolean arrestingEnabled            { public get; public set; }
    System.Single  currentPursuitLevelDuration { public get; public set; }
    System.Single  timeWithinArrestRange       { public get; public set; }
    System.Single  distanceOnPursuitStart      { public get; public set; }
    Il2CppScheduleOne.Police.PoliceOfficer officer { public get; public set; }
    System.Boolean targetWasDrivingOnPursuitStart { public get; public set; }
    System.Boolean wasInArrestCircleLastFrame     { public get; public set; }
    System.Int32   leaveArrestCircleCount         { public get; public set; }

    public virtual System.Void   Activate();
    public virtual System.Void   Awake();
    public virtual System.Void   BehaviourUpdate();
    public virtual System.Void   OnActiveTick();
    public virtual System.Void   Disable();
    public virtual System.Void   Resume();
    public virtual System.Void   EndCombat();
    public virtual System.Boolean IsTargetValid();
    public virtual System.Void   SetTarget(Il2CppFishNet.Object.NetworkObject target);
    public virtual System.Void   TargetSpotted();
    public virtual System.Void   TargetResighted();
    public         System.Void   OnThirdPartyVisionEvent(Il2CppScheduleOne.Vision.VisionEventReceipt receipt);
    public         System.Void   UpdateArrest(System.Single tick);
    public virtual System.Void   UpdateArrestBehaviour();
    public virtual System.Void   UpdateInvestigatingBehaviour();
    public virtual System.Void   UpdateNonLethalBehaviour();
    public virtual System.Void   UpdateLethalBehaviour();
    public         System.Void   ResetArrestProgress();
    public virtual System.Void   UpdateArrestCircle();
    public         System.Void   SetArrestCircleAlpha(System.Single alpha);
    public         System.Void   SetArrestCircleColor(UnityEngine.Color col);
    public         System.Void   ClearSpeedControls();
    public virtual System.Single GetIdealRangedWeaponDistance();
    public virtual System.Void   OnCurrentWeaponChanged(Il2CppScheduleOne.AvatarFramework.Equipping.AvatarWeapon weapon);
    public         System.Void   OnDestroy();
    public virtual System.Void   Method_Protected_Virtual_Void_0();   // = Awake_UserLogic_…PursuitBehaviour… (inferred)
}

public enum Il2CppScheduleOne.NPCs.Behaviour.PursuitBehaviour+EPursuitAction : System.Int32
{
    None         = 0,
    Move         = 1,
    Shoot        = 2,
    MoveAndShoot = 3,
}
```

The four `Update…Behaviour()` methods map 1:1 onto `EPursuitLevel` 1–4 and are **all
`virtual`** — the natural place to change how each escalation tier behaves.

### 4.7 `VehiclePursuitBehaviour`

```csharp
public class Il2CppScheduleOne.NPCs.Behaviour.VehiclePursuitBehaviour : Il2CppScheduleOne.NPCs.Behaviour.Behaviour
{
    static System.Single RECENT_VISIBILITY_THRESHOLD { public get; public set; }
    static System.Single EXIT_VEHICLE_MAX_SPEED      { public get; public set; }
    static System.Single CLOSE_ENOUGH_THRESHOLD      { public get; public set; }
    static System.Single UPDATE_FREQUENCY            { public get; public set; }
    static System.Single STATIONARY_THRESHOLD        { public get; public set; }
    static System.Single TIME_STATIONARY_TO_EXIT     { public get; public set; }

    Il2CppScheduleOne.PlayerScripts.Player Target { public get; public set; }
    UnityEngine.AnimationCurve RepathDistanceThresholdMap { public get; public set; }
    Il2CppScheduleOne.Vehicles.LandVehicle vehicle { public get; public set; }
    System.Boolean initialContactMade      { public get; public set; }
    System.Boolean aggressiveDrivingEnabled{ public get; public set; }
    System.Boolean beginAsSighted          { public get; public set; }
    Il2CppScheduleOne.Vehicles.AI.VehicleAgent Agent { public get; }

    public virtual System.Void AssignTarget(Il2CppScheduleOne.PlayerScripts.Player target);
    public         System.Void StartPursuit();
    public         System.Void BeginAsSighted();
    public         System.Void SetAggressiveDriving(System.Boolean aggressive);
    public         System.Void CheckExitVehicle();
    public         System.Void CheckTargetVisibility();
    public         System.Void UpdateDestination();
    public         System.Void DriveTo(UnityEngine.Vector3 location);
    public UnityEngine.Vector3 GetPlayerChasePoint();
    public         System.Void NavigationCallback(Il2CppScheduleOne.Vehicles.AI.VehicleAgent+ENavigationResult status);
    public         System.Void ProcessVisionEvent(Il2CppScheduleOne.Vision.VisionEventReceipt visionEventReceipt);
    public         System.Void ProcessThirdPartyVisionEvent(Il2CppScheduleOne.Vision.VisionEventReceipt visionEventReceipt);
    public         System.Void NotifyServerTargetSeen();     // ServerRpc 2166136261
    public virtual System.Void TargetSpotted();
    // + Activate/Deactivate/Pause/Resume/BehaviourUpdate/OnActiveTick/FixedUpdate/Start/OnDestroy
}
```

### 4.8 `BodySearchBehaviour`

```csharp
public class Il2CppScheduleOne.NPCs.Behaviour.BodySearchBehaviour : Il2CppScheduleOne.NPCs.Behaviour.Behaviour
{
    static Il2CppScheduleOne.Product.Packaging.EStealthLevel MAX_STEALTH_LEVEL { public get; public set; }
    static System.Single BODY_SEARCH_RANGE      { public get; public set; }
    static System.Single MAX_SEARCH_TIME        { public get; public set; }
    static System.Single MAX_TIME_OUTSIDE_RANGE { public get; public set; }
    static System.Single RANGE_TO_ESCALATE      { public get; public set; }
    static System.Single MOVE_SPEED             { public get; public set; }
    static System.Single BODY_SEARCH_COOLDOWN   { public get; public set; }
    static System.Single BODY_SEARCH_TIME       { public get; }              // read-only

    Il2CppScheduleOne.PlayerScripts.Player TargetPlayer { public get; public set; }
    System.Boolean ShowPostSearchDialogue { public get; public set; }
    Il2CppScheduleOne.Product.Packaging.EStealthLevel MaxStealthLevel { public get; public set; }
    Il2CppScheduleOne.Police.PoliceOfficer officer { public get; public set; }
    System.Single targetDistanceOnStart { public get; public set; }
    System.Single searchTime            { public get; public set; }
    System.Single timeOutsideRange      { public get; public set; }
    System.Single timeSinceCantReach    { public get; public set; }
    UnityEngine.Events.UnityEvent onSearchComplete_Clear      { public get; public set; }
    UnityEngine.Events.UnityEvent onSearchComplete_ItemsFound { public get; public set; }

    public virtual System.Void    AssignTarget(Il2CppFishNet.Connection.NetworkConnection conn, Il2CppFishNet.Object.NetworkObject target);  // ObserversRpc 1824087381
    public virtual System.Boolean DoesPlayerContainItemsOfInterest();
    public virtual System.Void    ConcludeSearch(System.Boolean clear);
    public virtual System.Void    NoItemsOfInterestFound();
    public virtual System.Void    Escalate();
    public         System.Void    UpdateEscalation();
    public         System.Void    UpdateSearch();
    public virtual System.Void    UpdateMovement();
    public virtual System.Void    UpdateCircle();
    public virtual System.Void    UpdateLookAt();
    public         System.Void    SearchClean();
    public         System.Void    SearchFail();
    public         System.Boolean IsTargetValid(Il2CppScheduleOne.PlayerScripts.Player player);
    public UnityEngine.Vector3    GetNewDestination();
    public         System.Void    SetArrestCircleAlpha(System.Single alpha);
    public         System.Void    SetArrestCircleColor(UnityEngine.Color col);
    public         System.Void    ClearSpeedControls();
    // + Activate/Deactivate/Pause/Resume/BehaviourUpdate/Awake and Method_Protected_Virtual_Void_0
}
```

`DoesPlayerContainItemsOfInterest()` is `virtual` and is the exact decision point for "does
this search find anything" — the single best hook for changing what counts as contraband.

### 4.9 `CheckpointBehaviour`

```csharp
public class Il2CppScheduleOne.NPCs.Behaviour.CheckpointBehaviour : Il2CppScheduleOne.NPCs.Behaviour.Behaviour
{
    static System.Single LOOK_TIME { public get; public set; }

    Il2CppScheduleOne.Law.CheckpointManager+ECheckpointLocation AssignedCheckpoint { public get; public set; }
    Il2CppScheduleOne.Police.RoadCheckpoint  Checkpoint           { public get; public set; }
    System.Boolean                           IsSearching          { public get; public set; }
    Il2CppScheduleOne.Vehicles.LandVehicle    CurrentSearchedVehicle { public get; public set; }
    Il2CppScheduleOne.PlayerScripts.Player    Initiator            { public get; public set; }
    System.Single  currentLookTime { public get; public set; }
    System.Boolean trunkOpened     { public get; public set; }
    UnityEngine.Transform standPoint { public get; }
    Il2CppScheduleOne.Dialogue.DialogueDatabase dialogueDatabase { public get; }

    public System.Void    SetCheckpoint(Il2CppScheduleOne.Law.CheckpointManager+ECheckpointLocation loc);  // ObserversRpc 4087078542
    public System.Void    StartSearch(Il2CppFishNet.Object.NetworkObject targetVehicle, Il2CppFishNet.Object.NetworkObject initiator);  // ServerRpc 3694055493
    public System.Void    StopSearch();                                                                    // ServerRpc 2166136261
    public System.Void    ConcludeSearch();                                                                // ObserversRpc 2166136261
    public System.Void    SetIsSearching(System.Boolean s);                                                // ObserversRpc 1140765316
    public System.Void    SetInitiator(Il2CppFishNet.Object.NetworkObject init);                           // ObserversRpc 3323014238
    public System.Boolean DoesVehicleContainIllicitItems();
    public System.Void    PlayerWalkedThroughCheckPoint(Il2CppScheduleOne.PlayerScripts.Player player);
    public UnityEngine.Vector3 GetSearchPoint();
    // + Activate/Deactivate/Pause/Resume/OnActiveTick/Awake
}
```

### 4.10 Foot / vehicle patrol and sentry behaviours

```csharp
public class Il2CppScheduleOne.NPCs.Behaviour.FootPatrolBehaviour : Il2CppScheduleOne.NPCs.Behaviour.Behaviour
{
    static System.Single MOVE_SPEED             { public get; public set; }
    static System.Int32  FLASHLIGHT_MIN_TIME    { public get; public set; }
           System.Int32  FLASHLIGHT_MAX_TIME    { public get; public set; }   // NOTE: instance, not static
    static System.String FLASHLIGHT_ASSET_PATH  { public get; public set; }
    System.Boolean UseFlashlight      { public get; public set; }
    System.Boolean flashlightEquipped { public get; public set; }
    Il2CppScheduleOne.NPCs.Behaviour.PatrolGroup Group { public get; public set; }

    public System.Void    SetGroup(Il2CppScheduleOne.NPCs.Behaviour.PatrolGroup group);
    public System.Void    SetFlashlightEquipped(System.Boolean equipped);
    public System.Boolean IsAtDestination(System.Single threshold = 2);
    public System.Boolean IsReadyToAdvance();
    // + Activate/Deactivate/Pause/Resume/OnActiveTick/Awake
}

public class Il2CppScheduleOne.NPCs.Behaviour.PatrolGroup : Il2CppSystem.Object
{
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.NPCs.NPC> Members { public get; public set; }
    Il2CppScheduleOne.NPCs.Behaviour.FootPatrolRoute Route { public get; public set; }
    System.Int32 CurrentWaypoint { public get; public set; }

    public .ctor(Il2CppScheduleOne.NPCs.Behaviour.FootPatrolRoute route);
    public System.Void    AdvanceGroup();
    public System.Void    DisbandGroup();
    public UnityEngine.Vector3 GetDestination(Il2CppScheduleOne.NPCs.NPC member);
    public UnityEngine.Vector3 GetMemberOffset(Il2CppScheduleOne.NPCs.NPC member);
    public System.Boolean IsGroupReadyToAdvance();
    public System.Boolean IsPaused();
}

public class Il2CppScheduleOne.NPCs.Behaviour.FootPatrolRoute : UnityEngine.MonoBehaviour
{
    System.String    RouteName  { public get; public set; }
    UnityEngine.Color PathColor { public get; public set; }
    Il2CppReferenceArray<UnityEngine.Transform> Waypoints { public get; public set; }
    System.Int32     StartWaypointIndex { public get; public set; }
    public System.Void UpdateWaypoints();
}

public class Il2CppScheduleOne.NPCs.Behaviour.VehiclePatrolRoute : UnityEngine.MonoBehaviour
{
    System.String RouteName { public get; public set; }
    Il2CppReferenceArray<UnityEngine.Transform> Waypoints { public get; public set; }
    System.Int32  StartWaypointIndex { public get; public set; }
}

public class Il2CppScheduleOne.NPCs.Behaviour.VehiclePatrolBehaviour : Il2CppScheduleOne.NPCs.Behaviour.Behaviour
{
    static System.Single MAX_CONSECUTIVE_PATHING_FAILURES { public get; public set; }
    static System.Single PROGRESSION_THRESHOLD            { public get; public set; }
    System.Int32 CurrentWaypoint { public get; public set; }
    Il2CppScheduleOne.NPCs.Behaviour.VehiclePatrolRoute Route { public get; public set; }
    Il2CppScheduleOne.Vehicles.LandVehicle Vehicle { public get; public set; }
    System.Boolean aggressiveDrivingEnabled { public get; public set; }

    public System.Void SetRoute(Il2CppScheduleOne.NPCs.Behaviour.VehiclePatrolRoute route);
    public System.Void StartPatrol();
    public System.Void DriveTo(UnityEngine.Vector3 location);
}

public class Il2CppScheduleOne.NPCs.Behaviour.SentryBehaviour : Il2CppScheduleOne.NPCs.Behaviour.Behaviour
{
    static System.Single BodySearchChance        { public get; public set; }
    static System.Int32  FlashlightMinTime       { public get; public set; }
           System.Int32  FlashlightMaxTime       { public get; public set; }   // NOTE: instance, not static
    static System.String FlashlightAssetPath     { public get; public set; }
    static System.Single AngularSpeedMultiplier  { public get; public set; }
    static System.Single WalkSpeed               { public get; public set; }
    System.Boolean UseFlashlight { public get; public set; }
    Il2CppScheduleOne.Law.SentryLocation AssignedLocation { public get; public set; }
    Il2CppScheduleOne.Police.PoliceOfficer officer { public get; public set; }

    public System.Void AssignLocation(Il2CppScheduleOne.Law.SentryLocation loc);
    public System.Void UnassignLocation();
    public System.Void ApplyMovementModifiers();
    public System.Void RemoveMovementModifiers();
    public System.Boolean IsAtStandPoint();
    public virtual System.Void OnActiveUncappedMinutePass();
}

public class Il2CppScheduleOne.NPCs.Behaviour.CallPoliceBehaviour : Il2CppScheduleOne.NPCs.Behaviour.Behaviour
{
    static System.Single CALL_POLICE_TIME { public get; public set; }
    Il2CppScheduleOne.UI.WorldspacePopup.WorldspacePopup PhoneCallPopup { public get; public set; }
    Il2CppScheduleOne.AvatarFramework.Equipping.AvatarEquippable PhonePrefab { public get; public set; }
    Il2CppScheduleOne.Audio.AudioSourceController CallSound { public get; public set; }
    System.Single currentCallTime { public get; public set; }
    Il2CppScheduleOne.PlayerScripts.Player Target { public get; public set; }
    Il2CppScheduleOne.Law.Crime ReportedCrime { public get; public set; }

    public System.Void SetData(Il2CppFishNet.Object.NetworkObject player, Il2CppScheduleOne.Law.Crime crime);
    public System.Void FinalizeCall();      // ObserversRpc 2166136261 -> calls LawManager.PoliceCalled (inferred)
    public System.Boolean IsTargetValid();
    public System.Void RefreshIcon();
}
```

Literals that pin `CallPoliceBehaviour` down: `CallPoliceBehaviour started on player`,
`CallPoliceBehaviour doesn't have a crime set, disabling.`, ` is calling the police on `.

---

## 5. Law intensity / difficulty

### 5.1 The console commands

Four law-related commands exist. All derive from `Il2CppScheduleOne.Console+ConsoleCommand`
(64 subclasses total).

```csharp
public class Il2CppScheduleOne.Console+ConsoleCommand : Il2CppSystem.Object
{
    System.String CommandWord        { public get; }
    System.String CommandDescription { public get; }
    System.String ExampleUsage       { public get; }
    public virtual System.Void Execute(Il2CppSystem.Collections.Generic.List<System.String> args);
}
```

| Class (verbatim) | `CommandWord` literal | `CommandDescription` literal |
|---|---|---|
| `Il2CppScheduleOne.Console+SetLawIntensity` | `setlawintensity` (example: `setlawintensity 6`) | `Sets the intensity of law enforcement activity on a scale of 0-10.` |
| `Il2CppScheduleOne.Console+RaisedWanted` | `raisewanted` | `Raises the player's wanted level` |
| `Il2CppScheduleOne.Console+LowerWanted` | `lowerwanted` | `Lowers the player's wanted level` |
| `Il2CppScheduleOne.Console+ClearWanted` | `clearwanted` | `Clears the player's wanted level` |
| `Il2CppScheduleOne.Console+SetPoliceIgnorePlayers` | `setpoliceignoreplayers` (example: `setpoliceignoreplayers true, setpoliceignoreplayers false`) | `Sets whether police ignore players.` |

Note the class is **`RaisedWanted`** (past tense — a typo in the game), not `RaiseWanted`.
Other log literals confirming behaviour: `Setting law enforcement intensity to `,
`Raising wanted level...`, `Lowering wanted level...`, `Clearing wanted level...`.

### 5.2 What `setlawintensity` drives

The only intensity storage in the game is on `Il2CppScheduleOne.Law.LawController`:

```csharp
System.Int32  LE_Intensity           { public get; public set; }   // the 0-10 dial, compared against IntensityRequirement
System.Single internalLawIntensity   { public get; public set; }   // the persisted float, saved to Law.json
System.Single IntensityIncreasePerDay{ public get; public set; }
static System.Single DAILY_INTENSITY_DRAIN { public get; public set; }

public System.Void SetInternalIntensity(System.Single intensity);
public System.Void ChangeInternalIntensity(System.Single change);
public System.Void DayPass();
```

`LE_Intensity` is the value each `PatrolInstance` / `CheckpointInstance` / `CurfewInstance` /
`SentryInstance` / `VehiclePatrolInstance` compares its `IntensityRequirement` against
*(inferred: both are `System.Int32`, no other int intensity value exists, and the console
description says "scale of 0-10")*.

`internalLawIntensity` is the persisted float (it is the sole field of `LawData` →
`Law.json`) and is moved by `DayPass()` using `IntensityIncreasePerDay` and
`DAILY_INTENSITY_DRAIN`.

**Which of the two `setlawintensity` writes is UNVERIFIED.** S1API exposes both paths
separately (`SetIntensityLevel(int level)` → `Intensity` → `LE_Intensity`, and
`SetInternalIntensity(float)` → `InternalIntensity` → `internalLawIntensity`), and declares
`MinIntensity = 1`, `MaxIntensity = 10`, `DailyIntensityDrain = 0.05f`. A dynamic-intensity
mod should set **both** and re-run `LawController.Instance.GetSettings()` /
`LawActivitySettings.Evaluate()` to make it take effect immediately.

### 5.3 Other difficulty-shaped globals

Grepping `literals-sorted.txt` for `intensity` produced only:
`Sets the intensity of law enforcement activity on a scale of 0-10.`,
`Setting law enforcement intensity to `, `GODRAYS_VARIABLE_INTENSITY`, and a long list of
shader `_…Intensity` uniforms. **There is no second law-difficulty knob and no
difficulty-setting enum anywhere in the game.**

The nearest things to global difficulty dials are:

```csharp
static System.Single Il2CppScheduleOne.Vision.VisionCone.UniversalAttentivenessScale { public get; public set; }
static System.Single Il2CppScheduleOne.Vision.VisionCone.UniversalMemoryScale        { public get; public set; }
static System.Int32  Il2CppScheduleOne.Law.LawManager.OfficerDispatchMin             { public get; public set; }
static System.Int32  Il2CppScheduleOne.Law.LawManager.OfficerDispatchMax             { public get; public set; }
static System.Single Il2CppScheduleOne.Law.LawManager.DISPATCH_VEHICLE_USE_THRESHOLD { public get; public set; }
static System.Single Il2CppScheduleOne.Police.PoliceOfficer.BODY_SEARCH_CHANCE_DEFAULT { public get; public set; }
```

`UniversalAttentivenessScale` / `UniversalMemoryScale` are, by name and by being static
non-`_CONST`-styled scalars, explicitly designed as global multipliers — the safest
"make cops sharper/dumber" lever in the game.

---

## 6. Vision / detection

### 6.1 Types in `Il2CppScheduleOne.Vision` (12)

| Type | Base | Purpose |
|---|---|---|
| `VisionCone` (+ `EEventLevel`, `EventStateChange`, `SightableData`, `StateContainer`) | `Il2CppFishNet.Object.NetworkBehaviour` | The eye. Frustum + LoS + notice-time accumulation |
| `EntityVisibility` | `Il2CppFishNet.Object.NetworkBehaviour` | How visible a thing is + the visual states it is broadcasting |
| `PlayerVisibility` | `EntityVisibility` | Player specialisation, incl. the curfew flag |
| `EVisualState` | `System.Enum` | The 10 things a cop can notice |
| `EntityVisualState` | `Il2CppSystem.Object` | One applied state (`state` + `label` + destroy callback) |
| `VisionEvent` | `Il2CppSystem.Object` | A single in-progress "I am noticing X" accumulator |
| `VisionEventReceipt` | `Il2CppSystem.Object` | Network-serialisable `(NetworkObject Target, EVisualState State)` |
| `ISightable` | interface | Implemented by `Player` and `NPC` |
| `VisibilityAttribute` / `UniqueVisibilityAttribute` | `Il2CppSystem.Object` | Additive/multiplicative visibility modifiers |
| `LightVisibilityAffector` | `UnityEngine.MonoBehaviour` | Lights raise visibility |
| `VisionObscurer` | `UnityEngine.MonoBehaviour` | Bushes/smoke lower it |

```csharp
public enum Il2CppScheduleOne.Vision.EVisualState : System.Int32
{
    Visible            = 0,
    Suspicious         = 1,
    DisobeyingCurfew   = 2,
    Vandalizing        = 3,
    PettyCrime         = 4,
    DrugDealing        = 5,
    Wanted             = 6,
    Pickpocketing      = 7,
    DischargingWeapon  = 8,
    Brandishing        = 9,
}

public enum Il2CppScheduleOne.Vision.VisionCone+EEventLevel : System.Int32
{
    Start = 0, Half = 1, Full = 2, Zero = 3,
}
```

`EVisualState` is the crime taxonomy *as seen by eyes*, and it maps onto
`NPCResponses_Police.Noticed…` one-for-one. `EVisualState.Wanted` is what makes a cop who
has never seen you commit anything still chase you.

### 6.2 `VisionCone` — full signatures

```csharp
public class Il2CppScheduleOne.Vision.VisionCone : Il2CppFishNet.Object.NetworkBehaviour
{
    static System.Single VISION_UPDATE_INTERVAL       { public get; public set; }
    static System.Single MinVisionDelta               { public get; public set; }
    static System.Single ExclamationSoundCooldown     { public get; public set; }
    static System.Single TimeOnLastExclamationSound   { public get; public set; }
    static System.Single UniversalAttentivenessScale  { public get; public set; }
    static System.Single UniversalMemoryScale         { public get; public set; }
    static System.Single HorizontalFOV                { public get; public set; }
    static System.Single VerticalFOV                  { public get; public set; }
    static System.Single Range                        { public get; public set; }
    static System.Single MinorWidth                   { public get; public set; }
    static System.Single MinorHeight                  { public get; public set; }

    System.Boolean DEBUG { public get; public set; }
    UnityEngine.Transform      VisionOrigin  { public get; public set; }
    UnityEngine.AnimationCurve VisionFalloff { public get; public set; }
    UnityEngine.LayerMask VisibilityBlockingLayers { public get; public set; }
    System.Single RangeMultiplier { public get; public set; }
    System.Single Attentiveness   { public get; public set; }
    System.Single Memory          { public get; public set; }
    System.Single effectiveRange  { public get; }
    System.Boolean noticeGeneralCrime { public get; public set; }
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Vision.VisionCone+StateContainer> DefaultStatesOfInterest { public get; public set; }
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Vision.ISightable> sightablesOfInterest { public get; public set; }
    Il2CppSystem.Collections.Generic.Dictionary<Il2CppScheduleOne.Vision.ISightable, Il2CppScheduleOne.Vision.VisionCone+SightableData> sightableDatas { public get; public set; }
    Il2CppSystem.Collections.Generic.Dictionary<Il2CppScheduleOne.Vision.ISightable,
        Il2CppSystem.Collections.Generic.Dictionary<Il2CppScheduleOne.Vision.EVisualState, Il2CppScheduleOne.Vision.VisionCone+StateContainer>> stateSettings { public get; public set; }
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Vision.VisionEvent> activeVisionEvents { public get; public set; }
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Vision.VisionEvent> cachedVisionEvents { public get; public set; }
    Il2CppScheduleOne.NPCs.NPC npc { public get; public set; }
    Il2CppScheduleOne.UI.WorldspacePopup.WorldspacePopup QuestionMarkPopup    { public get; public set; }
    Il2CppScheduleOne.UI.WorldspacePopup.WorldspacePopup ExclamationPointPopup{ public get; public set; }
    Il2CppScheduleOne.Audio.AudioSourceController ExclamationSound { public get; public set; }

    Il2CppScheduleOne.Vision.VisionCone+EventStateChange onVisionEventStarted { public get; public set; }
    Il2CppScheduleOne.Vision.VisionCone+EventStateChange onVisionEventHalf    { public get; public set; }
    Il2CppScheduleOne.Vision.VisionCone+EventStateChange onVisionEventFull    { public get; public set; }
    Il2CppScheduleOne.Vision.VisionCone+EventStateChange onVisionEventExpired { public get; public set; }

    public virtual System.Void    VisionUpdate();
    public virtual System.Void    UpdateVision(System.Single tickTime);
    public virtual System.Void    UpdateEvents(System.Single tickTime);
    public virtual System.Boolean IsPointWithinSight(UnityEngine.Vector3 point, System.Boolean ignoreLoS = False, Il2CppScheduleOne.Vehicles.LandVehicle vehicleToIgnore = null);
    public         System.Boolean IsPlayerVisible(Il2CppScheduleOne.PlayerScripts.Player player);
    public         System.Boolean IsPlayerVisible(Il2CppScheduleOne.PlayerScripts.Player player, out Il2CppScheduleOne.Vision.VisionCone+SightableData& data);
    public         System.Single  GetPlayerVisibility(Il2CppScheduleOne.PlayerScripts.Player player);
    public         System.Boolean IsTargetVisible(Il2CppScheduleOne.Vision.ISightable target);
    public         System.Boolean WasSightableVisibleThisFrame(Il2CppScheduleOne.Vision.ISightable sightable);
    public         System.Void    AddSightableOfInterest(Il2CppScheduleOne.Vision.ISightable s);
    public         System.Void    RemoveSightableOfInterest(Il2CppScheduleOne.Vision.ISightable s);
    public         System.Void    SetSightableStateEnabled(Il2CppScheduleOne.Vision.ISightable sightable, Il2CppScheduleOne.Vision.EVisualState state, System.Boolean enabled);
    public virtual System.Void    SetNoticePlayerCrimes(Il2CppScheduleOne.PlayerScripts.Player player, System.Boolean active);
    public Il2CppScheduleOne.Vision.VisionEvent GetEvent(Il2CppScheduleOne.Vision.ISightable target, Il2CppScheduleOne.Vision.EntityVisualState state);
    public virtual System.Void    EventHalfNoticed(Il2CppScheduleOne.Vision.VisionEvent _event);
    public virtual System.Void    EventFullyNoticed(Il2CppScheduleOne.Vision.VisionEvent _event);
    public virtual System.Void    EventReachedZero(Il2CppScheduleOne.Vision.VisionEvent _event);
    public         System.Void    ClearEvents();
    public         System.Void    SendEventReceipt(Il2CppScheduleOne.Vision.VisionEventReceipt receipt, Il2CppScheduleOne.Vision.VisionCone+EEventLevel level);      // ServerRpc    3486014028
    public virtual System.Void    ReceiveEventReceipt(Il2CppScheduleOne.Vision.VisionEventReceipt receipt, Il2CppScheduleOne.Vision.VisionCone+EEventLevel level);   // ObserversRpc 3486014028
    public Il2CppStructArray<UnityEngine.Plane>   GetFrustumPlanes();
    public Il2CppStructArray<UnityEngine.Vector3> GetFrustumVertices();
    public System.Void PrintSightableStates();
    public System.Void OnDie();
    public System.Void OnEnable();
    public System.Void OnDisable();
    public virtual System.Void Method_Protected_Virtual_New_Void_0();   // = Awake_UserLogic_ScheduleOne.Vision.VisionCone_… (inferred)
    public System.Void Method_Private_Void_Player_0(Il2CppScheduleOne.PlayerScripts.Player plr);
}

public class Il2CppScheduleOne.Vision.VisionCone+StateContainer : Il2CppSystem.Object
{
    Il2CppScheduleOne.Vision.EVisualState state { public get; public set; }
    System.Boolean Enabled              { public get; public set; }
    System.Single  NoticeTimeMultiplier { public get; public set; }
    System.Single  RequiredNoticeTime   { public get; }        // read-only, derived
    public Il2CppScheduleOne.Vision.VisionCone+StateContainer GetCopy();
}

public class Il2CppScheduleOne.Vision.VisionCone+SightableData : Il2CppSystem.Object
{
    Il2CppScheduleOne.Vision.ISightable Sightable  { public get; public set; }
    System.Single                       VisionDelta{ public get; public set; }
    System.Single                       TimeVisible{ public get; public set; }
}

public sealed class Il2CppScheduleOne.Vision.VisionCone+EventStateChange : Il2CppSystem.MulticastDelegate
{
    public virtual System.Void Invoke(Il2CppScheduleOne.Vision.VisionEventReceipt _event);
    public static Il2CppScheduleOne.Vision.VisionCone+EventStateChange op_Implicit(System.Action<Il2CppScheduleOne.Vision.VisionEventReceipt>);
    public static Il2CppScheduleOne.Vision.VisionCone+EventStateChange op_Addition(...);
    public static Il2CppScheduleOne.Vision.VisionCone+EventStateChange op_Subtraction(...);
}
```

**Detection ranges and progression:** the cone is `HorizontalFOV` × `VerticalFOV` ×
`Range`, scaled per-instance by `RangeMultiplier` (exposed as `effectiveRange`). Per
`EVisualState`, a `StateContainer` supplies `NoticeTimeMultiplier` → `RequiredNoticeTime`.
Each frame `VisionUpdate()` → `UpdateVision(tickTime)` computes a `VisionDelta` per visible
`ISightable`; `UpdateEvents(tickTime)` feeds that into `VisionEvent.UpdateEvent(...)`, which
raises `NormalizedNoticeLevel` from 0 → 1. Crossing thresholds fires
`EventHalfNoticed` (question-mark popup) and `EventFullyNoticed` (exclamation popup + sound),
which produce a `VisionEventReceipt` sent to the server. `Memory` /
`UniversalMemoryScale` govern decay when the target leaves sight;
`VisionEvent.NOTICE_DROP_THRESHOLD` and `VisionCone.MinVisionDelta` gate the decay.
*(Assembled from names and call-shape — the exact arithmetic is **UNVERIFIED**, since only
metadata was dumped, not IL.)*

### 6.3 `VisionEvent`, `VisionEventReceipt`, `EntityVisualState`

```csharp
public class Il2CppScheduleOne.Vision.VisionEvent : Il2CppSystem.Object
{
    static System.Single NOTICE_DROP_THRESHOLD { public get; public set; }

    Il2CppScheduleOne.Vision.ISightable         Target      { public get; public set; }
    Il2CppScheduleOne.Vision.EntityVisualState  State       { public get; public set; }
    Il2CppScheduleOne.Vision.VisionCone         Owner       { public get; public set; }
    System.Single FullNoticeTime        { public get; public set; }
    System.Single timeSinceSighted      { public get; public set; }
    System.Single currentNoticeTime     { public get; public set; }
    System.Boolean playTremolo          { public get; public set; }
    System.Single NormalizedNoticeLevel { public get; }

    public .ctor(Il2CppScheduleOne.Vision.VisionCone _owner,
                 Il2CppScheduleOne.Vision.ISightable _target,
                 Il2CppScheduleOne.Vision.EntityVisualState _state,
                 System.Single _noticeTime,
                 System.Boolean _playTremolo);
    public System.Void UpdateEvent(System.Single visionDeltaThisFrame, System.Single tickTime);
    public System.Void EndEvent();
}

public class Il2CppScheduleOne.Vision.VisionEventReceipt : Il2CppSystem.Object
{
    Il2CppFishNet.Object.NetworkObject     Target { public get; public set; }
    Il2CppScheduleOne.Vision.EVisualState  State  { public get; public set; }
    public .ctor(Il2CppFishNet.Object.NetworkObject target, Il2CppScheduleOne.Vision.EVisualState state);
}

public class Il2CppScheduleOne.Vision.EntityVisualState : Il2CppSystem.Object
{
    Il2CppScheduleOne.Vision.EVisualState state { public get; public set; }
    System.String                         label { public get; public set; }
    Il2CppSystem.Action           stateDestroyed{ public get; public set; }
}
```

### 6.4 `EntityVisibility` / `PlayerVisibility` / `ISightable`

```csharp
public class Il2CppScheduleOne.Vision.EntityVisibility : Il2CppFishNet.Object.NetworkBehaviour
{
    static System.Single MAX_VISIBLITY { public get; public set; }    // sic: MAX_VISIBLITY, missing 'I'

    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Vision.VisibilityAttribute>  ActiveAttributes { public get; public set; }
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Vision.EntityVisualState>    VisualStates     { public get; public set; }
    UnityEngine.LayerMask VisibilityCheckMask { public get; public set; }
    UnityEngine.Transform CentralVisibilityPoint { public get; public set; }
    Il2CppSystem.Collections.Generic.List<UnityEngine.Transform> VisibilityPoints { public get; public set; }
    Il2CppScheduleOne.Vision.VisibilityAttribute environmentalVisibility { public get; public set; }
    System.Single       CurrentVisibility { public get; }
    System.Single       Suspiciousness    { public get; }
    UnityEngine.Vector3 CenterPoint       { public get; }

    public         System.Void   ApplyState(System.String label, Il2CppScheduleOne.Vision.EVisualState state, System.Single autoRemoveAfter = 0);  // ServerRpc 2910447583
    public         System.Void   RemoveState(System.String label, System.Single delay = 0);                                                       // ServerRpc  606697822
    public         System.Void   ClearStates();
    public         Il2CppScheduleOne.Vision.EntityVisualState GetState(System.String label);
    public         Il2CppScheduleOne.Vision.VisibilityAttribute GetAttribute(System.String name);
    public         System.Single CalculateVisibility();
    public         System.Single CalculateExposureToPoint(UnityEngine.Vector3 point, System.Single checkRange = 50, Il2CppScheduleOne.NPCs.NPC checkingNPC = null);
    public virtual Il2CppSystem.Collections.Generic.List<UnityEngine.Vector3> GetVisibilityPoints();
    public         System.Void   UpdateEnvironmentalVisibilityAttribute();
    public virtual System.Void   OnStartClient();
}

public class Il2CppScheduleOne.Vision.PlayerVisibility : Il2CppScheduleOne.Vision.EntityVisibility
{
    Il2CppScheduleOne.PlayerScripts.Player player { public get; public set; }
    System.Boolean disobeyingCurfewStateApplied { public get; public set; }
    System.Single  Suspiciousness { public get; }

    public System.Void AddFlag_DisobeyingCurfew();
    public System.Void RemoveFlag_DisobeyingCurfew();
    public virtual Il2CppSystem.Collections.Generic.List<UnityEngine.Vector3> GetVisibilityPoints();
}

public class Il2CppScheduleOne.Vision.ISightable    // interface, surfaced as a class by the dumper
{
    Il2CppFishNet.Object.NetworkObject           NetworkObject          { public get; }
    Il2CppScheduleOne.Vision.VisionEvent         HighestProgressionEvent{ public get; public set; }
    Il2CppScheduleOne.Vision.EntityVisibility    VisibilityComponent    { public get; }
    public virtual System.Boolean IsCurrentlySightable();
}

public class Il2CppScheduleOne.Vision.VisibilityAttribute : Il2CppSystem.Object
{
    System.String name         { public get; public set; }
    System.Single pointsChange { public get; public set; }
    System.Single multiplier   { public get; public set; }
    public .ctor(System.String _name, System.Single _pointsChange, System.Single _multiplier = 1, System.Int32 attributeIndex = -1);
    public System.Void Delete();
}

public class Il2CppScheduleOne.Vision.UniqueVisibilityAttribute : Il2CppScheduleOne.Vision.VisibilityAttribute
{
    System.String uniquenessCode { public get; public set; }
    public .ctor(System.String _name, System.Single _pointsChange, System.String _uniquenessCode, System.Single _multiplier = 1, System.Int32 attributeIndex = -1);
}

public class Il2CppScheduleOne.Vision.LightVisibilityAffector : UnityEngine.MonoBehaviour
{
    static System.Single PointLightEffect { public get; public set; }
    static System.Single SpotLightEffect  { public get; public set; }
    System.Single EffectMultiplier        { public get; public set; }
    System.String uniquenessCode          { public get; public set; }
    System.Int32  updateDistanceThreshold { public get; public set; }
    public System.Void UpdateAttribute(System.Single visibity);   // sic: "visibity"
    public virtual System.Void UpdateVisibility();
}

public class Il2CppScheduleOne.Vision.VisionObscurer : UnityEngine.MonoBehaviour
{
    System.Single ObscuranceAmount { public get; public set; }
}
```

`ApplyState`/`RemoveState` take a **string `label`** — so a mod can add its own visual state
without colliding with the game's labels, e.g.
`playerVisibility.ApplyState("MyMod.Outlaw", EVisualState.Wanted, 0f)`.

### 6.5 How an officer actually sees you

`Il2CppScheduleOne.NPCs.NPCAwareness` (on every NPC) wires vision → responses:

```csharp
public class Il2CppScheduleOne.NPCs.NPCAwareness : UnityEngine.MonoBehaviour
{
    static System.Single PLAYER_AIM_DETECTION_RANGE { public get; public set; }
    System.Boolean AwarenessActiveByDefault { public get; public set; }
    Il2CppScheduleOne.Vision.VisionCone   VisionCone { public get; public set; }
    Il2CppScheduleOne.Noise.Listener      Listener   { public get; public set; }
    Il2CppScheduleOne.NPCs.Responses.NPCResponses Responses { public get; public set; }

    UnityEngine.Events.UnityEvent<Il2CppScheduleOne.PlayerScripts.Player> onNoticedGeneralCrime { public get; public set; }
    UnityEngine.Events.UnityEvent<Il2CppScheduleOne.PlayerScripts.Player> onNoticedPettyCrime   { public get; public set; }
    UnityEngine.Events.UnityEvent<Il2CppScheduleOne.PlayerScripts.Player> onNoticedDrugDealing  { public get; public set; }
    UnityEngine.Events.UnityEvent<Il2CppScheduleOne.PlayerScripts.Player> onNoticedPlayerViolatingCurfew { public get; public set; }
    UnityEngine.Events.UnityEvent<Il2CppScheduleOne.PlayerScripts.Player> onNoticedSuspiciousPlayer      { public get; public set; }
    UnityEngine.Events.UnityEvent<Il2CppScheduleOne.Noise.NoiseEvent>     onGunshotHeard  { public get; public set; }
    UnityEngine.Events.UnityEvent<Il2CppScheduleOne.Noise.NoiseEvent>     onExplosionHeard{ public get; public set; }
    UnityEngine.Events.UnityEvent<Il2CppScheduleOne.Vehicles.LandVehicle> onHitByCar      { public get; public set; }

    public System.Void VisionEvent(Il2CppScheduleOne.Vision.VisionEventReceipt vEvent);
    public System.Void NoiseEvent(Il2CppScheduleOne.Noise.NoiseEvent nEvent);
    public System.Void HitByCar(Il2CppScheduleOne.Vehicles.LandVehicle vehicle);
    public System.Void SetAwarenessActive(System.Boolean active);
}
```

Chain: `VisionCone.EventFullyNoticed` → `SendEventReceipt`/`ReceiveEventReceipt` →
`NPCAwareness.VisionEvent(receipt)` → the matching `UnityEvent` → `NPCResponses_Police.Noticed…`
→ `PoliceOfficer.BeginFootPursuit_Networked` / `BeginBodySearch_Networked`
*(inferred wiring; the `UnityEvent` targets are prefab-serialised so I cannot read them from
metadata)*. `PoliceOfficer.ProcessVisionEvent(VisionEventReceipt)` and the static
`PoliceOfficer.OnPoliceVisionEvent` sit on the same path.

---

## 7. Combat — consequences of resisting

```csharp
public class Il2CppScheduleOne.Combat.Impact : Il2CppSystem.Object
{
    UnityEngine.Vector3 HitPoint             { public get; public set; }
    UnityEngine.Vector3 ImpactForceDirection { public get; public set; }
    System.Single       ImpactForce          { public get; public set; }
    System.Single       ImpactDamage         { public get; public set; }
    Il2CppScheduleOne.Combat.EImpactType    ImpactType    { public get; public set; }
    Il2CppFishNet.Object.NetworkObject      ImpactSource  { public get; public set; }
    System.Int32                            ImpactID      { public get; public set; }
    Il2CppScheduleOne.Combat.EExplosionType ExplosionType { public get; public set; }

    public .ctor(UnityEngine.Vector3 hitPoint, UnityEngine.Vector3 impactForceDirection,
                 System.Single impactForce, System.Single impactDamage,
                 Il2CppScheduleOne.Combat.EImpactType impactType,
                 Il2CppFishNet.Object.NetworkObject impactSource, System.Int32 impactID);
    public .ctor(UnityEngine.Vector3 hitPoint, UnityEngine.Vector3 impactForceDirection,
                 System.Single impactForce, System.Single impactDamage,
                 Il2CppScheduleOne.Combat.EImpactType impactType,
                 Il2CppFishNet.Object.NetworkObject impactSource);

    public static System.Boolean IsLethal(Il2CppScheduleOne.Combat.EImpactType impactType);
    public        System.Boolean IsPlayerImpact(out Il2CppScheduleOne.PlayerScripts.Player& player);
}

public enum Il2CppScheduleOne.Combat.EImpactType : System.Int32
{
    Punch = 0, BluntMetal = 1, SharpMetal = 2, Bullet = 3, PhysicsProp = 4, Explosion = 5,
}

public enum Il2CppScheduleOne.Combat.EExplosionType : System.Int32 { Default = 0, Lightning = 1 }
public enum Il2CppScheduleOne.Combat.ERangedWeaponAction : System.Int32 { None = 0, Shoot = 1, Reposition = 2, RepositionAndShoot = 3 }

public class Il2CppScheduleOne.Combat.IDamageable        // interface
{
    UnityEngine.GameObject gameObject { public get; }
    public virtual System.Void ReceiveImpact(Il2CppScheduleOne.Combat.Impact impact);
    public virtual System.Void SendImpact(Il2CppScheduleOne.Combat.Impact impact);
}

public class Il2CppScheduleOne.Combat.ICombatTargetable   // interface
{
    Il2CppFishNet.Object.NetworkObject NetworkObject { public get; }
    UnityEngine.Vector3   CenterPoint          { public get; }
    UnityEngine.Transform CenterPointTransform { public get; }
    UnityEngine.Vector3   LookAtPoint          { public get; }
    System.Boolean        IsCurrentlyTargetable{ public get; }
    System.Single         RangedHitChanceMultiplier { public get; }
    UnityEngine.Vector3   Velocity             { public get; }
    System.Boolean        IsPlayer             { public get; }
    Il2CppScheduleOne.PlayerScripts.Player AsPlayer { public get; }

    public virtual System.Single  GetSearchTime();
    public virtual System.Boolean IsNull();
    public virtual System.Void    RecordLastKnownPosition(System.Boolean resetTimeSinceLastSeen);
}
```

Both `Il2CppScheduleOne.NPCs.NPC` and `Il2CppScheduleOne.PlayerScripts.Player` implement
`ICombatTargetable` and `IDamageable` (both expose `CenterPoint`, `LookAtPoint`,
`RangedHitChanceMultiplier`, `IsCurrentlyTargetable`, `GetSearchTime`,
`RecordLastKnownPosition`, `ReceiveImpact`).

Damage is networked identically on both, with RPC id **427288424**:

```csharp
public virtual System.Void SendImpact(Il2CppScheduleOne.Combat.Impact impact);      // ServerRpc:    RpcWriter___Server_SendImpact_427288424    / RpcLogic___SendImpact_427288424    / RpcReader___Server_SendImpact_427288424
public virtual System.Void ReceiveImpact(Il2CppScheduleOne.Combat.Impact impact);   // ObserversRpc: RpcWriter___Observers_ReceiveImpact_427288424 / RpcLogic___ReceiveImpact_427288424 / RpcReader___Observers_ReceiveImpact_427288424
```

`RpcLogic___ReceiveImpact_427288424` on `Il2CppScheduleOne.NPCs.NPC` is the single point where
"the player hit a cop" becomes real, and is the right prefix target for damage scaling.

`Il2CppScheduleOne.Combat.CombatBehaviour` (base of `PursuitBehaviour`) is what actually
swings and shoots:

```csharp
public class Il2CppScheduleOne.Combat.CombatBehaviour : Il2CppScheduleOne.NPCs.Behaviour.Behaviour
{
    static System.Single RECENT_VISIBILITY_THRESHOLD      { public get; public set; }
    static System.Single REPOSITION_TIME                  { public get; public set; }
    static System.Single SEARCH_RADIUS_MIN                { public get; public set; }
    static System.Single SEARCH_RADIUS_MAX                { public get; public set; }
    static System.Single SEARCH_SPEED                     { public get; public set; }
    static System.Single CONSECUTIVE_MISS_ACCURACY_BOOST  { public get; public set; }
    static System.Single REACHED_DESTINATION_DISTANCE     { public get; public set; }
    static System.Single DelayBeforeFirstAttack           { public get; public set; }

    Il2CppScheduleOne.Combat.ICombatTargetable Target { public get; public set; }
    System.Single GiveUpRange                { public get; public set; }
    System.Int32  GiveUpAfterSuccessfulHits  { public get; public set; }
    System.Single DefaultMovementSpeed       { public get; public set; }
    System.Single DefaultSearchTime          { public get; public set; }
    System.Boolean CombatOnStart             { public get; public set; }
    Il2CppScheduleOne.AvatarFramework.Equipping.AvatarMeleeWeapon VirtualPunchWeapon { public get; public set; }
    Il2CppScheduleOne.AvatarFramework.Equipping.AvatarWeapon      currentWeapon      { public get; public set; }
    System.Int32 successfulHits         { public get; public set; }
    System.Int32 consecutiveMissedShots { public get; public set; }
    Il2CppSystem.Action onSuccessfulHit { public get; public set; }

    public virtual System.Void    StartCombat();
    public virtual System.Void    EndCombat();
    public virtual System.Void    Attack();                                     // ObserversRpc 2166136261
    public         System.Boolean Shoot();
    public virtual System.Boolean ReadyToAttack(System.Boolean checkTarget = True);
    public         System.Void    SucessfulHit();                                // sic: "Sucessful"
    public virtual System.Void    SetTarget(Il2CppFishNet.Object.NetworkObject target);
    public         System.Void    SetTargetAndEnable_Server(Il2CppFishNet.Object.NetworkObject target);   // ServerRpc 3323014238
    public         System.Void    SetTarget_Client(Il2CppFishNet.Connection.NetworkConnection conn, Il2CppFishNet.Object.NetworkObject target);  // Observers+Target 1824087381
    public virtual System.Void    SetWeapon(System.String weaponPath);           // ObserversRpc 3615296227
    public         System.Void    ClearWeapon();                                 // ObserversRpc 2166136261
    public         System.Void    SetWeaponRaised(System.Boolean raised);
    public         System.Void    SetDefaultWeapon(Il2CppScheduleOne.AvatarFramework.Equipping.AvatarWeapon weapon);
    public         System.Void    SetMovementSpeed(System.Single speed, System.String label = "combat", System.Int32 priority = 5);
    public virtual System.Single  GetIdealRangedWeaponDistance();
    public UnityEngine.Vector3    GetPredictedFutureTargetPosition(System.Single lead_Min = 0, System.Single lead_Max = 2);
    public         System.Void    StartSearching();
    public         System.Void    StopSearching();
    public virtual System.Single  GetSearchTime();
    // + RangedWeaponRoutine / SearchRoutine / RepositionToRangedWeaponRange coroutines
}

public class Il2CppScheduleOne.Combat.CombatManager
    : Il2CppScheduleOne.DevUtilities.NetworkSingleton<Il2CppScheduleOne.Combat.CombatManager>
{
    UnityEngine.LayerMask MeleeLayerMask        { public get; public set; }
    UnityEngine.LayerMask ExplosionLayerMask    { public get; public set; }
    UnityEngine.LayerMask RangedWeaponLayerMask { public get; public set; }
    Il2CppScheduleOne.Combat.Explosion ExplosionPrefab { public get; public set; }
    public System.Void CreateExplosion(UnityEngine.Vector3 origin, Il2CppScheduleOne.Combat.ExplosionData data);
    public System.Void CreateExplosion(UnityEngine.Vector3 origin, Il2CppScheduleOne.Combat.ExplosionData data, System.Int32 id);
    public System.Void Explosion(UnityEngine.Vector3 origin, Il2CppScheduleOne.Combat.ExplosionData data, System.Int32 id);
}
```

**Consequences of resisting, concretely:** hitting a cop routes
`Impact` → `NPC.ReceiveImpact` → `NPCResponses_Police.ImpactReceived` →
`RespondToFirstNonLethalAttack` / `RespondToRepeatedNonLethalAttack` / `RespondToLethalAttack`
(the split is decided by `Impact.IsLethal(EImpactType)`), which escalates
`PlayerCrimeData.CurrentPursuitLevel` toward `NonLethal` / `Lethal` and logs `Assault` /
`DeadlyAssault` crimes. At `Lethal`, `PursuitBehaviour.UpdateLethalBehaviour()` runs and
`Weapon_Gun` is used; accuracy is modulated by
`PlayerCrimeData.GetShotAccuracyMultiplier()`, `CombatBehaviour.CONSECUTIVE_MISS_ACCURACY_BOOST`
and `ICombatTargetable.RangedHitChanceMultiplier`, with rate limited by
`PlayerCrimeData.SHOT_COOLDOWN_MIN`/`SHOT_COOLDOWN_MAX`.

---

## 8. Police NPC spawning & schedules

### 8.1 Officers are pre-placed, pooled, and dispatched — not spawned

`Il2CppScheduleOne.Map.PoliceStation` is the pool owner:

```csharp
public class Il2CppScheduleOne.Map.PoliceStation : Il2CppScheduleOne.Map.NPCEnterableBuilding
{
    static Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Map.PoliceStation> PoliceStations { public get; public set; }

    UnityEngine.Transform SpawnPoint { public get; public set; }
    Il2CppReferenceArray<UnityEngine.Transform> VehicleSpawnPoints          { public get; public set; }
    Il2CppReferenceArray<UnityEngine.Transform> PossessedVehicleSpawnPoints { public get; public set; }
    Il2CppScheduleOne.Map.ParkingLot PoliceVehicleParkingLot { public get; public set; }
    Il2CppReferenceArray<Il2CppScheduleOne.Vehicles.LandVehicle> PoliceVehicles { public get; public set; }

    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Police.PoliceOfficer> OfficerPool { public get; public set; }
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Vehicles.LandVehicle> deployedVehicles { public get; public set; }
    System.Single TimeSinceLastDispatch { public get; public set; }
    System.Int32  AvailableVehicleCount { public get; }
    System.Int32  deployedVehicleCount  { public get; }

    public System.Void Dispatch(System.Int32 requestedOfficerCount,
                                Il2CppScheduleOne.PlayerScripts.Player targetPlayer,
                                Il2CppScheduleOne.Map.PoliceStation+EDispatchType type = 0,
                                System.Boolean beginAsSighted = False);
    public Il2CppScheduleOne.Police.PoliceOfficer PullOfficer();
    public Il2CppScheduleOne.Vehicles.LandVehicle DeployVehicle();
    public System.Boolean TryDeployVehicle(out Il2CppScheduleOne.Vehicles.LandVehicle& vehicle, UnityEngine.Transform spawnPoint);
    public System.Void ReturnVehicle(Il2CppScheduleOne.Vehicles.LandVehicle vehicle);
    public static Il2CppScheduleOne.Map.PoliceStation GetClosestPoliceStation(UnityEngine.Vector3 point);
    public virtual System.Void NPCEnteredBuilding(Il2CppScheduleOne.NPCs.NPC npc, Il2CppScheduleOne.Doors.StaticDoor door);
    public virtual System.Void NPCExitedBuilding(Il2CppScheduleOne.NPCs.NPC npc, Il2CppScheduleOne.Doors.StaticDoor door);
    public static System.Boolean Method_Internal_Static_Boolean_Transform_PDM_0(UnityEngine.Transform spawnPoint);
}

public enum Il2CppScheduleOne.Map.PoliceStation+EDispatchType : System.Int32
{
    Auto       = 0,
    UseVehicle = 1,
    OnFoot     = 2,
}
```

Officer lifecycle, verified from members + log literals:

1. Officers are **authored NPC GameObjects in the scene**, not runtime-instantiated prefabs.
   `PoliceOfficer` is a `Il2CppScheduleOne.NPCs.NPC` subclass with
   `public virtual System.Boolean ShouldSave()`, i.e. it participates in NPC persistence like
   every other named NPC.
2. Idle officers sit in `PoliceStation.OfficerPool` (they enter via `NPCEnteredBuilding`).
3. `Dispatch(requestedOfficerCount, targetPlayer, type, beginAsSighted)` →
   `PullOfficer()` per officer → `PoliceOfficer.Activate()` →
   `BeginFootPursuit_Networked` / `BeginVehiclePursuit_Networked`.
4. When the pursuit ends, `PoliceOfficer.CheckDeactivation()` /
   `Deactivate()` return them, gated by `AutoDeactivate`,
   `OutOfSightTimeToDeactivate`, `timeSinceOutOfSight`, `timeSinceReadyToPool` and
   `DeactivationBlockingBehaviours`.

Constraining log literals: `Attempted to dispatch officers from a client, this is not allowed.`,
`Attempted to dispatch more than 4 officers, this is not allowed.`,
`Attempted to dispatch officers, but there are no officers in the pool.`,
`Failed to pull officer from station`, ` has no officers in its pool!`,
`Attempted to deactivate an officer on the client`.

**There are 9 named officers.** `S1API.Entities.NPCs.PoliceOfficers` (which resolves them by
NPC id from `NPCManager`) declares exactly: `OfficerBailey`, `OfficerCooper`, `OfficerGreen`,
`OfficerHoward`, `OfficerJackson`, `OfficerLee`, `OfficerLopez`, `OfficerMurphy`,
`OfficerOakley`.

**No officer prefab path exists in `literals-sorted.txt`.** The only `Police`-bearing literals
are the log/UI/console strings listed above plus `sample_offer_rejected_police`,
`Police icon start` / `Police icon end` / `Police icon discover`. Contrast with e.g.
`FootPatrolBehaviour.FLASHLIGHT_ASSET_PATH` and `SentryBehaviour.FlashlightAssetPath`, which
*are* runtime asset paths. **Conclusion: officers cannot be spawned from a path; a new
officer variant must be cloned from an existing `PoliceOfficer` GameObject** (see §12).

### 8.2 Where the schedules come from

Not from `NPCScheduleManager` (the normal NPC daily-schedule system). Police activity is
driven top-down:

```
LawController (Singleton)
  └── {Monday..Sunday}Settings : LawActivitySettings      // designer-authored per weekday
        ├── Patrols[]        : PatrolInstance        -> LawManager.StartFootpatrol(FootPatrolRoute, requestedMembers)
        ├── VehiclePatrols[] : VehiclePatrolInstance -> LawManager.StartVehiclePatrol(VehiclePatrolRoute)
        ├── Checkpoints[]    : CheckpointInstance    -> CheckpointManager.SetCheckpointEnabled(loc, true, requestedOfficers)
        ├── Sentries[]       : SentryInstance        -> PoliceOfficer.AssignToSentryLocation(SentryLocation)
        └── Curfews[]        : CurfewInstance        -> CurfewManager.Enable(conn) / Disable()
```

`LawController.OnUncappedMinPass()` ticks; each instance's `MinPass()` / `Evaluate()` checks
`StartTime`, `EndTime`, `OnlyIfCurfewEnabled` and `IntensityRequirement`, and starts/ends its
activity. `LawController.OverrideSetings(LawActivitySettings)` / `EndOverride()` let you swap
the whole day's schedule at runtime — this is the cleanest scripted-event hook in the system.

The per-officer behaviour objects (`FootPatrolBehaviour`, `VehiclePatrolBehaviour`,
`SentryBehaviour`, `CheckpointBehaviour`) are prefab components on the officer, arbitrated by
`Il2CppScheduleOne.NPCs.Behaviour.NPCBehaviour`:

```csharp
public class Il2CppScheduleOne.NPCs.Behaviour.NPCBehaviour : Il2CppFishNet.Object.NetworkBehaviour
{
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.NPCs.Behaviour.Behaviour> behaviourStack   { public get; public set; }
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.NPCs.Behaviour.Behaviour> enabledBehaviours{ public get; public set; }
    Il2CppScheduleOne.NPCs.Behaviour.Behaviour activeBehaviour { public get; public set; }
    Il2CppScheduleOne.NPCs.Behaviour.CallPoliceBehaviour CallPoliceBehaviour { public get; public set; }
    Il2CppScheduleOne.Combat.CombatBehaviour             CombatBehaviour     { public get; public set; }
    // ... plus Cowering/Ragdoll/Flee/Dead/Unconscious/etc.

    public Il2CppScheduleOne.NPCs.Behaviour.Behaviour GetBehaviour(System.String BehaviourName);
    public T                                          GetBehaviour<T>();
    public System.Void AddEnabledBehaviour(Il2CppScheduleOne.NPCs.Behaviour.Behaviour b);
    public System.Void RemoveEnabledBehaviour(Il2CppScheduleOne.NPCs.Behaviour.Behaviour b);
    public System.Void SortBehaviourStack();
    public System.Void EnableBehaviour_Server(System.Int32 behaviourIndex);
    public System.Void DisableBehaviour_Server(System.Int32 behaviourIndex);
    public System.Void ActivateBehaviour_Server(System.Int32 behaviourIndex);
    public System.Void DeactivateBehaviour_Server(System.Int32 behaviourIndex);
    public System.Void PauseBehaviour_Server(System.Int32 behaviourIndex);
    public System.Void ResumeBehaviour_Server(System.Int32 behaviourIndex);
}

public class Il2CppScheduleOne.NPCs.Behaviour.Behaviour : Il2CppFishNet.Object.NetworkBehaviour
{
    static System.Int32 MAX_CONSECUTIVE_PATHING_FAILURES { public get; public set; }
    System.Boolean EnabledOnAwake { public get; public set; }
    System.Boolean Enabled { public get; public set; }
    System.String  Name    { public get; public set; }
    System.Int32   Priority{ public get; public set; }   // higher priority wins the behaviour stack
    System.Boolean Started { public get; public set; }
    System.Boolean Active  { public get; public set; }
    System.Int32   BehaviourIndex { public get; public set; }
    UnityEngine.Events.UnityEvent onEnable  { public get; public set; }
    UnityEngine.Events.UnityEvent onDisable { public get; public set; }
    UnityEngine.Events.UnityEvent onBegin   { public get; public set; }
    UnityEngine.Events.UnityEvent onEnd     { public get; public set; }
    Il2CppScheduleOne.NPCs.NPC Npc { public get; }

    public virtual System.Void Enable();
    public virtual System.Void Disable();
    public virtual System.Void Activate();
    public virtual System.Void Deactivate();
    public virtual System.Void Pause();
    public virtual System.Void Resume();
    public         System.Void Enable_Server();
    public         System.Void Disable_Server();
    public         System.Void Activate_Server(Il2CppFishNet.Connection.NetworkConnection conn);
    public         System.Void Deactivate_Server();
    public virtual System.Void BehaviourUpdate();
    public virtual System.Void BehaviourLateUpdate();
    public virtual System.Void OnActiveTick();
    public virtual System.Void OnActiveUncappedMinutePass();
    public virtual System.Void SetDestination(UnityEngine.Vector3 position, System.Boolean teleportIfFail = True, System.Single successThreshold = 1);
}
```

**`Behaviour.Priority` is a public `System.Int32`** and `NPCBehaviour.SortBehaviourStack()` is
public — that is how a mod inserts its own behaviour ahead of pursuit.

Related dialogue components on the officer:

```csharp
public class Il2CppScheduleOne.Dialogue.DialogueController_Police : Il2CppScheduleOne.Dialogue.DialogueController
{
    Il2CppScheduleOne.Police.PoliceOfficer officer { public get; public set; }
    public virtual System.Boolean CanStartDialogue();
    public virtual System.Void Start();
}

public class Il2CppScheduleOne.Dialogue.DialogueHandler_Police : Il2CppScheduleOne.Dialogue.ControlledDialogueHandler
{
    Il2CppScheduleOne.Dialogue.DialogueContainer CheckpointRequestDialogue { public get; public set; }
    Il2CppScheduleOne.Police.PoliceOfficer officer { public get; public set; }
    public System.Boolean CanTalk_Checkpoint();
    public virtual System.Int32 CheckBranch(System.String branchLabel);
    public virtual System.Void Hovered();
    public virtual System.Void Interacted();
}

public class Il2CppScheduleOne.VoiceOver.PoliceChatterVO : Il2CppScheduleOne.VoiceOver.VOEmitter
{
    Il2CppScheduleOne.Audio.AudioSourceController StartBeep    { public get; public set; }
    Il2CppScheduleOne.Audio.AudioSourceController StartEndBeep { public get; public set; }
    Il2CppScheduleOne.Audio.AudioSourceController Static       { public get; public set; }
    public System.Void PlayChatter();
    public virtual System.Void Play(Il2CppScheduleOne.VoiceOver.EVOLineType lineType);
}

public class Il2CppScheduleOne.FX.ProximityCircle : UnityEngine.MonoBehaviour
{
    UnityEngine.Rendering.Universal.DecalProjector Circle { public get; public set; }
    public System.Void SetAlpha(System.Single alpha);
    public System.Void SetColor(UnityEngine.Color col);
    public System.Void SetRadius(System.Single rad);
}
```

`PoliceChatterVO` is the radio chatter, driven by `PoliceOfficer.UpdateChatter()`,
`ChatterEnabled`, `chatterCountDown`, `MIN_CHATTER_INTERVAL`, `MAX_CHATTER_INTERVAL`.
**There is no dispatch-dialogue line table in the string literals** — chatter is audio-only
(`EVOLineType`), not text.

---

## 9. Save data

Exactly one save file holds law state.

```csharp
public class Il2CppScheduleOne.Persistence.Datas.LawData : Il2CppScheduleOne.Persistence.Datas.SaveData
{
    System.Single InternalLawIntensity { public get; public set; }
    public .ctor(System.Single internalLawIntensity);
}

public class Il2CppScheduleOne.Persistence.Loaders.LawLoader : Il2CppScheduleOne.Persistence.Loaders.Loader
{
    public virtual System.Void Load(System.String mainPath);
}
```

Written and read by `LawController`:

```csharp
System.String SaveFolderName { public get; }   // literal "Law" is present in the metadata (inferred to be this)
System.String SaveFileName   { public get; }
Il2CppScheduleOne.Persistence.Loaders.Loader Loader { public get; }   // returns the LawLoader
System.Boolean ShouldSaveUnderFolder { public get; }
System.Int32   LoadOrder { public get; }
public virtual System.String GetSaveString();
public         System.Void   Load(Il2CppScheduleOne.Persistence.Datas.LawData data);
public         System.Void   OnLoadComplete();
```

So `Law.json` in a real save contains **exactly one field, `InternalLawIntensity` (float)**.

**Nothing else about police or crime is persisted.** I checked every type in
`Il2CppScheduleOne.Persistence.Datas` for `Pursuit`, `Crime`, `Wanted` and `Law` — `LawData`
is the only hit. Consequences:

- `PlayerCrimeData.CurrentPursuitLevel`, `Crimes`, `EvadedArrest`, `MinsSinceLastArrested`
  are **runtime-only** and reset on load.
- `RoadCheckpoint` gate state, officer assignments and patrol groups are **runtime-only**.
- A mod that adds persistent state (e.g. an "outlaw" flag) must ship its own
  `Il2CppScheduleOne.Persistence`-compatible saveable, or use S1API's `ISaveable`.

For Harmony-by-string / save-key work, remember the **unprefixed** names:
`ScheduleOne.Law.LawController`, `ScheduleOne.Persistence.Datas.LawData`,
`ScheduleOne.Persistence.Loaders.LawLoader`. The `$\Assets\Scripts\Law\LawController.cs`
and `\Assets\Scripts\Police\Offense.cs` source paths are also in the metadata, confirming the
original folder layout `Assets/Scripts/Law/` and `Assets/Scripts/Police/`.

---

## 10. Runtime flow: commit-a-crime → wanted → dispatch → pursuit → arrest → consequences

Sides are marked **[S]** server-only, **[C]** client, **[S→C]** server broadcast to observers,
**[C→S]** client request to server.

```
 1. [C] Player does something visible.
        PlayerVisibility.ApplyState(label, EVisualState.X, autoRemoveAfter)   [C→S ServerRpc 2910447583]
        (e.g. PlayerVisibility.AddFlag_DisobeyingCurfew() when CurfewManager.IsCurrentlyActive)

 2. [C] Every VisionCone within effectiveRange runs VisionUpdate() -> UpdateVision(tick)
        -> UpdateEvents(tick) -> VisionEvent.UpdateEvent(delta, tick).
        Crossing half:  VisionCone.EventHalfNoticed  (question-mark popup)
        Crossing full:  VisionCone.EventFullyNoticed (exclamation popup + sound)

 3. [C→S] VisionCone.SendEventReceipt(VisionEventReceipt, EEventLevel)        [ServerRpc 3486014028]
    [S→C] VisionCone.ReceiveEventReceipt(...)                                 [ObserversRpc 3486014028]
          -> NPCAwareness.VisionEvent(receipt) -> the matching UnityEvent
          -> Il2CppScheduleOne.Police.NPCResponses_Police.Noticed<X>(player)
          -> also fires the static PoliceOfficer.OnPoliceVisionEvent

 4a. Witness is a civilian NPC:
        NPCBehaviour.CallPoliceBehaviour.SetData(playerNetObj, crime)
        -> CALL_POLICE_TIME elapses -> CallPoliceBehaviour.FinalizeCall()      [ObserversRpc 2166136261]
        -> [S] Il2CppScheduleOne.Law.LawManager.PoliceCalled(player, crime)
           (log: "Police called on"; "Player is already being pursued, ignoring call police request.")

 4b. Witness is an officer:
        NPCResponses_Police goes straight to PoliceOfficer.BeginFootPursuit_Networked
        or PoliceOfficer.BeginBodySearch_Networked, skipping LawManager.

 5. [S] LawManager.PoliceCalled
        -> PlayerCrimeData.AddCrime(crime, quantity)
        -> PlayerCrimeData.SetPursuitLevel(EPursuitLevel.Investigating|Arresting)
           (SyncVar CurrentPursuitLevel replicates; onPursuitLevelChange fires;
            log: " new pursuit level: ", "Registering pursuit level change event")
        -> Il2CppScheduleOne.Map.PoliceStation.GetClosestPoliceStation(pos)
        -> PoliceStation.Dispatch(count in [OfficerDispatchMin..OfficerDispatchMax],
                                  targetPlayer, EDispatchType.Auto, beginAsSighted)
           (hard cap 4; vehicle vs foot chosen against DISPATCH_VEHICLE_USE_THRESHOLD)
        -> PoliceStation.PullOfficer() per officer  (+ DeployVehicle/TryDeployVehicle for cruisers)
        -> PoliceOfficer.Activate()

 6. [C→S] PoliceOfficer.BeginFootPursuit_Networked(playerCode, includeColleagues)
          -> [S] RpcLogic___BeginFootPursuit_Networked_310431262
    [S→C] PoliceOfficer.BeginFootPursuit(playerCode)
          -> RpcLogic___BeginFootPursuit_3615296227
          -> PursuitBehaviour.SetTarget(playerNetObj) / Activate()
    (vehicles: BeginVehiclePursuit_Networked -> VehiclePursuitBehaviour.AssignTarget/StartPursuit;
     VehiclePursuitBehaviour.CheckExitVehicle() drops them to foot after
     TIME_STATIONARY_TO_EXIT at < STATIONARY_THRESHOLD)

 7. Pursuit loop, per tick:
        PursuitBehaviour.OnActiveTick() / BehaviourUpdate()
          -> one of UpdateInvestigatingBehaviour / UpdateArrestBehaviour
                  / UpdateNonLethalBehaviour / UpdateLethalBehaviour   (by EPursuitLevel)
          -> UpdateArrest(tick): if within ARREST_RANGE, accumulate timeWithinArrestRange
             toward ARREST_TIME, pushing PlayerCrimeData.SetArrestProgress(0..1)
          -> UpdateArrestCircle() drives ProximityCircle; leaving it
             LEAVE_ARREST_CIRCLE_LIMIT times triggers escalation
        PlayerCrimeData.UpdateEscalation() escalates on ESCALATION_TIME_ARRESTING /
        ESCALATION_TIME_NONLETHAL; UpdateTimeout()/TimeoutPursuit() de-escalate when
        TimeSinceSighted exceeds GetSearchTime() (SEARCH_TIME_* by level)
        -> on escape: SetEvaded(), onPursuitEscapedSound,
           log "Target ({0}) is no longer wanted. (Pursuit level = {1})"

 8. Body search variant (no crime required):
        PoliceOfficer.CanInvestigate() / CanInvestigatePlayer(player) gated by
        INVESTIGATION_COOLDOWN, INVESTIGATION_MAX_DISTANCE, INVESTIGATION_MIN_VISIBILITY,
        INVESTIGATION_CHECK_INTERVAL and BodySearchChance
        -> CheckNewInvestigation() -> new Investigation(target); ChangeProgress()
        -> ConductBodySearch(player) -> BeginBodySearch_Networked(playerCode)
        -> BodySearchBehaviour.AssignTarget / UpdateSearch
        -> DoesPlayerContainItemsOfInterest() (compares against MaxStealthLevel)
             clean  -> ConcludeSearch(true)  -> onSearchComplete_Clear      / SearchClean()
             dirty  -> ConcludeSearch(false) -> onSearchComplete_ItemsFound / SearchFail()
                       -> BodySearchBehaviour.Escalate() -> EPursuitLevel.Arresting

 9. Arrest completes ([S], PursuitBehaviour side):
        Player.Arrest_Server()                                   [C→S ServerRpc 2166136261]
        -> Player.Arrest_Client()                                [S→C ObserversRpc 2166136261]
           sets Player.IsArrested, fires Player.onArrested
    [C] Il2CppScheduleOne.UI.ArrestScreen.Open() -> Continue()
    [C] Il2CppScheduleOne.UI.ArrestNoticeScreen.Open()
          -> RecordCrimes()                                   (fills recordedCrimes from PlayerCrimeData.Crimes)
          -> RecordPossession(EStealthLevel maxStealthLevel)   (lists what will be taken)
          -> Il2CppScheduleOne.Law.PenaltyHandler.ProcessCrimeList(recordedCrimes)
             -> List<string> penalties: "$… fine",
                "… controlled substances confiscated", "… low/moderate/high-severity drugs confiscated"
          -> ConfiscateItems(EStealthLevel maxStealthLevel)     <=== ITEMS ACTUALLY REMOVED HERE
          -> OnClose() / Exit()  (fine applied to MoneyManager here — UNVERIFIED)

10. Release ([S]):
        Player.Free_Server()                                     [C→S ServerRpc 2166136261]
        -> Player.Free_Client()                                  [S→C ObserversRpc 2166136261]
           clears IsArrested, fires Player.onFreed, respawns the player
        -> PlayerCrimeData.OnPlayerFreed()
           -> ClearCrimes(), SetPursuitLevel(EPursuitLevel.None), MinsSinceLastArrested = 0
        Vehicles left behind are reclaimed after ArrestNoticeScreen.VEHICLE_POSSESSION_TIMEOUT.
        There is NO jail time and NO holding cell.
```

Everything from step 5 onward is **server-authoritative**; the client only ever sends the
`_Networked` / `_Server` request variants. Any mod that changes police behaviour in
multiplayer must run its logic on the host.

---

## 11. Hooks & extension points

All hooks below are on real `virtual`/public methods verified in the dumps. Use
`HarmonyLib` via MelonLoader; remember Harmony sees the **Il2Cpp-prefixed** proxy types.

### 11.1 Raising / lowering law intensity dynamically

Best hook — **no patch needed at all**:

```csharp
var lc = Il2CppScheduleOne.Law.LawController.Instance;
lc.SetInternalIntensity(newFloat);     // persisted value (goes to Law.json)
lc.LE_Intensity = newInt;              // the 0-10 dial the schedule instances compare against
// then force a re-evaluation so it takes effect this minute rather than next day:
lc.CurrentSettings.Evaluate();
```

If you need to react to the game changing it, patch:

| Target | Why |
|---|---|
| `Il2CppScheduleOne.Law.LawController.SetInternalIntensity(System.Single)` | postfix — observe/clamp every intensity write |
| `Il2CppScheduleOne.Law.LawController.ChangeInternalIntensity(System.Single)` | prefix — scale deltas (e.g. `__0 *= myMultiplier`) |
| `Il2CppScheduleOne.Law.LawController.DayPass()` | postfix — apply your own daily curve on top of `IntensityIncreasePerDay` / `DAILY_INTENSITY_DRAIN` |
| `Il2CppScheduleOne.Law.LawController.OnUncappedMinPass()` | postfix — per-minute dynamic intensity (heat that rises while you deal, decays while you idle) |
| `Il2CppScheduleOne.Law.LawController.GetSettings()` and `GetSettings(EDay)` | postfix — return a fully synthetic `LawActivitySettings` |
| `Il2CppScheduleOne.Console+SetLawIntensity.Execute(List<String>)` | postfix — keep your mod in sync with the console command |

**Cleanest hook for "dynamic intensity"**: postfix
`Il2CppScheduleOne.Law.LawController.OnUncappedMinPass()`, recompute your own heat value from
whatever inputs you want, write `LE_Intensity` + `SetInternalIntensity`, then call
`CurrentSettings.Evaluate()`. It runs on a fixed game-minute cadence, it is on a plain
`Singleton` (no FishNet RPC plumbing to fight), and every downstream system
(`PatrolInstance`, `CheckpointInstance`, `SentryInstance`, `VehiclePatrolInstance`,
`CurfewInstance`) already reads `IntensityRequirement` against it every minute.

Second-best, no-patch alternative: `LawController.OverrideSetings(myLawActivitySettings)`
with a hand-built `LawActivitySettings` whose arrays you generate per intensity tier, and
`EndOverride()` to restore.

Difficulty scalars you can just write at startup:
`Il2CppScheduleOne.Vision.VisionCone.UniversalAttentivenessScale`,
`Il2CppScheduleOne.Vision.VisionCone.UniversalMemoryScale`,
`Il2CppScheduleOne.Law.LawManager.OfficerDispatchMin` / `OfficerDispatchMax`,
`Il2CppScheduleOne.Police.PoliceOfficer.BODY_SEARCH_CHANCE_DEFAULT`.
**Caveat:** if any of these is a C# `const` in the original source, IL2CPP inlined the value
at every call site and writing the static field does nothing. Verify empirically per field
(set it to an absurd value at runtime and watch behaviour) before relying on it.
`UniversalAttentivenessScale` / `UniversalMemoryScale` / `OfficerDispatchMin` /
`OfficerDispatchMax` are the ones most likely to be real mutable statics *(inferred from
naming style: the SCREAMING_CASE ones read like consts, the PascalCase ones like fields).*

### 11.2 Adding a new crime type

`Il2CppScheduleOne.Law.Crime` has **no registry**, so there is nothing to register into — but
there is also nothing stopping you, because everything downstream is keyed on the
`Il2CppSystem.Type` / the object identity, not on an id table.

Two routes:

**A. Real IL2CPP subclass (full fidelity).**

```csharp
// once, before any use:
Il2CppInterop.Runtime.Injection.ClassInjector.RegisterTypeInIl2Cpp<MyCrime>();

public class MyCrime : Il2CppScheduleOne.Law.Crime
{
    public MyCrime(System.IntPtr ptr) : base(ptr) { }
    public MyCrime() : base(ClassInjector.DerivedConstructorPointer<MyCrime>())
        { ClassInjector.DerivedConstructorBody(this); CrimeName = "Operating an unlicensed lab"; }
}
```

Then `playerCrimeData.AddCrime(new MyCrime(), 1)`. It will flow into
`PlayerCrimeData.Crimes`, into `ArrestNoticeScreen.recordedCrimes`, and be rendered as a
crime entry. **UNVERIFIED**: whether `ClassInjector` handles this base cleanly here — `Crime`
is a plain `Il2CppSystem.Object` with no virtual methods to override, which is the easy case,
but it needs a runtime test.

**B. Reuse an existing subclass and just change `CrimeName`.** `CrimeName` has a public
setter on every subclass, so `new Il2CppScheduleOne.Law.FailureToComply { CrimeName = "…" }`
gives you a custom-labelled crime with zero injection risk. This is the safe route.

Either way you must also patch the penalty side, because
`Il2CppScheduleOne.Law.PenaltyHandler.ProcessCrimeList(Dictionary<Crime,Int32>)` is a static
with a fixed per-type fine table and will produce **no penalty line** for an unknown crime
*(inferred from it being static with 14 hardcoded fine constants)*:

| Target | Patch kind |
|---|---|
| `Il2CppScheduleOne.Law.PenaltyHandler.ProcessCrimeList(Il2CppSystem.Collections.Generic.Dictionary<Il2CppScheduleOne.Law.Crime, System.Int32>)` | **postfix** — append your own penalty strings to `__result` |
| `Il2CppScheduleOne.PlayerScripts.PlayerCrimeData.AddCrime(Il2CppScheduleOne.Law.Crime, System.Int32)` | prefix/postfix — inject extra crimes, or veto |
| `Il2CppScheduleOne.UI.ArrestNoticeScreen.RecordCrimes()` | postfix — add UI rows |

To *emit* your crime, either call `LawManager.PoliceCalled(player, myCrime)` directly (which
also dispatches), or `playerCrimeData.AddCrime(myCrime)` for a silent record.

### 11.3 Changing arrest consequences

| Target | What it buys you |
|---|---|
| `Il2CppScheduleOne.UI.ArrestNoticeScreen.ConfiscateItems(Il2CppScheduleOne.Product.Packaging.EStealthLevel)` | **prefix returning false** = no confiscation; **prefix altering `__0`** = confiscate more/less by stealth tier; postfix = take extra things (cash, vehicle) |
| `Il2CppScheduleOne.UI.ArrestNoticeScreen.RecordPossession(Il2CppScheduleOne.Product.Packaging.EStealthLevel)` | change what the notice claims you had |
| `Il2CppScheduleOne.Law.PenaltyHandler.ProcessCrimeList(...)` | rewrite the whole fine/penalty list |
| `Il2CppScheduleOne.UI.ArrestNoticeScreen.OnClose()` / `Exit()` | apply your own consequence (debt, rank loss, property seizure) |
| `Il2CppScheduleOne.PlayerScripts.Player.Free_Server()` / `Free_Client()` | change release location/state; add "released on bail" state |
| `Il2CppScheduleOne.PlayerScripts.PlayerCrimeData.OnPlayerFreed()` | prefix returning false = **crimes survive the arrest** (a rap sheet) |
| `Il2CppScheduleOne.PlayerScripts.Player.onArrested` / `onFreed` (`Il2CppSystem.Action`) | no-patch subscription |
| `Il2CppScheduleOne.PlayerScripts.PlayerCrimeData.onPursuitLevelChange` (`Action<EPursuitLevel,EPursuitLevel>`) | no-patch subscription |

For "jail time", note there is nothing to extend — you would be building it from scratch on
top of `Free_Server()`.

### 11.4 Spawning a new officer variant (e.g. a federal agent)

There is no officer prefab path, so the practical recipe is:

1. Get a template: `Il2CppScheduleOne.Police.PoliceOfficer.Officers[0]`, or
   `Il2CppScheduleOne.Map.PoliceStation.PoliceStations[i].OfficerPool[j]`, or
   `PoliceOfficer.GetNearestOfficer(pos, out dist, onlyConscious: false)`.
2. `UnityEngine.Object.Instantiate(template.gameObject)` **on the server**, then spawn it
   through FishNet (`NetworkManager.ServerManager.Spawn`) so `NetworkBehaviour` initialises.
3. Re-skin via the inherited `Il2CppScheduleOne.NPCs.NPC` members: `Avatar`,
   `ApplyNPCData(NPCData)`, `NPCData`, `GetNameAddress()` (which is `virtual` on
   `PoliceOfficer`).
4. Re-arm via `PursuitBehaviour.Weapon_Baton` / `Weapon_Taser` / `Weapon_Gun` and
   `PoliceOfficer.BatonPrefab` / `TaserPrefab` / `GunPrefab`, or
   `CombatBehaviour.SetWeapon(System.String weaponPath)` at runtime.
5. Tune per-instance (not global): `PoliceOfficer.BodySearchChance`,
   `BodySearchDuration`, `AutoDeactivate`, `ChatterEnabled`,
   `VisionCone.RangeMultiplier`, `VisionCone.Attentiveness`, `VisionCone.Memory`,
   `CombatBehaviour.GiveUpRange`, `GiveUpAfterSuccessfulHits`, `DefaultMovementSpeed`.
6. Register it: add to `PoliceStation.OfficerPool` so `PullOfficer()` can dispatch it, or
   drive it directly with `BeginFootPursuit_Networked(playerCode, includeColleagues: false)`.

Patch points to make feds appear only at high intensity:

| Target | Use |
|---|---|
| `Il2CppScheduleOne.Map.PoliceStation.Dispatch(System.Int32, Il2CppScheduleOne.PlayerScripts.Player, Il2CppScheduleOne.Map.PoliceStation+EDispatchType, System.Boolean)` | prefix — swap in your agents when `LawController.Instance.LE_Intensity >= N`, or raise the officer count |
| `Il2CppScheduleOne.Map.PoliceStation.PullOfficer()` | postfix — return your variant |
| `Il2CppScheduleOne.Law.LawManager.PoliceCalled(Player, Crime)` | prefix — full control over what responds to a given crime |
| `Il2CppScheduleOne.Law.LawManager.StartFootpatrol(FootPatrolRoute, System.Int32)` | postfix — add a fed to each patrol group |
| `Il2CppScheduleOne.Police.PoliceOfficer.Activate()` / `Deactivate()` | tag/untag your variant on entry/exit of the pool |
| `Il2CppScheduleOne.Police.PoliceOfficer.CheckDeactivation()` | prefix returning false — keep a fed on the map permanently |

Subclassing `PoliceOfficer` itself via `ClassInjector` is possible in principle but it is a
`NetworkBehaviour` with FishNet-generated `NetworkInitialize*` plumbing and a SyncVar — I
**do not recommend it** and could not verify it works. Instantiate-and-configure is the
supported path.

### 11.5 Adding an "outlaw" status

Nothing in the game persists a reputation-with-the-law value, so an outlaw flag is a mod-owned
value plus these injection points:

| Goal | Target |
|---|---|
| Make every cop recognise you on sight | Apply `Il2CppScheduleOne.Vision.EVisualState.Wanted` permanently: `player.VisibilityComponent.ApplyState("MyMod.Outlaw", EVisualState.Wanted, 0f)` (label-scoped, so it will not collide) |
| Make cops react harder | postfix each `Il2CppScheduleOne.Police.NPCResponses_Police.Noticed*` / `Notice*` / `RespondTo*` (all 19 are `virtual`) |
| Skip the investigate stage | prefix `Il2CppScheduleOne.PlayerScripts.PlayerCrimeData.SetPursuitLevel(EPursuitLevel)` and floor the level |
| Never let heat fully drop | prefix `Il2CppScheduleOne.PlayerScripts.PlayerCrimeData.TimeoutPursuit()` / `Deescalate()` and return false while outlaw |
| Keep the rap sheet across arrests | prefix `Il2CppScheduleOne.PlayerScripts.PlayerCrimeData.OnPlayerFreed()` (skip `ClearCrimes`) |
| Force body searches | set `PoliceOfficer.BodySearchChance = 1f` per officer, or postfix `PoliceOfficer.CanInvestigatePlayer(Player)` to return true |
| Close checkpoints to you | postfix `Il2CppScheduleOne.NPCs.Behaviour.CheckpointBehaviour.DoesVehicleContainIllicitItems()` and `Il2CppScheduleOne.NPCs.Behaviour.BodySearchBehaviour.DoesPlayerContainItemsOfInterest()` |
| Show it in the HUD | postfix `Il2CppScheduleOne.UI.CrimeStatusUI.UpdateStatus()` |
| Persist it | your own saveable; **not** `LawData` (it has one float and no room) |

### 11.6 FishNet patching rule of thumb

For any networked method `X`, the triple is
`RpcWriter___{Server|Observers|Target}_X_<hash>` (send),
`RpcLogic___X_<hash>` (**the real body — patch this**),
`RpcReader___{Server|Observers|Target}_X_<hash>` (receive).
Patching the public `X` intercepts the *caller*; patching `RpcLogic___X_<hash>` intercepts
the *effect* on whichever machine executes it. Hashes are stable within a game version and
change when the signature changes, so resolve them by prefix at runtime rather than hardcoding:

```csharp
var m = typeof(Il2CppScheduleOne.Police.PoliceOfficer)
    .GetMethods(BindingFlags.Public | BindingFlags.Instance)
    .First(x => x.Name.StartsWith("RpcLogic___BeginFootPursuit_Networked_"));
```

---

## 12. Tuning constants

Every static field in the police/law/vision/combat surface, with its exact declaring type and
name as dumped. All are exposed by Il2CppInterop as `{ public get; public set; }` static
properties. **Values are not in the metadata dump** — only names and types — except where a
literal or S1API mirror is cited. See the `const`-inlining caveat in §11.1.

| Declaring type | Static member | Type | Effect |
|---|---|---|---|
| `Il2CppScheduleOne.Law.LawController` | `DAILY_INTENSITY_DRAIN` | `System.Single` | Daily decay of `internalLawIntensity` (S1API mirrors `0.05f`) |
| `Il2CppScheduleOne.Law.LawManager` | `OfficerDispatchMin` | `System.Int32` | Min officers per dispatch |
| `Il2CppScheduleOne.Law.LawManager` | `OfficerDispatchMax` | `System.Int32` | Max officers per dispatch (hard-capped at 4 by a runtime check) |
| `Il2CppScheduleOne.Law.LawManager` | `DISPATCH_VEHICLE_USE_THRESHOLD` | `System.Single` | Distance/score above which a cruiser is sent instead of foot units |
| `Il2CppScheduleOne.Law.CheckpointInstance` | `MIN_ACTIVATION_DISTANCE` | `System.Single` | Player must be this far from a checkpoint for it to spin up |
| `Il2CppScheduleOne.Law.CurfewManager` | `HOUR_BEFORE_CURFEW` | `System.Int32` | Hint time (S1API: `2000`) |
| `Il2CppScheduleOne.Law.CurfewManager` | `WARNING_TIME` | `System.Int32` | Warning time (S1API: `2030`) |
| `Il2CppScheduleOne.Law.CurfewManager` | `CURFEW_START_TIME` | `System.Int32` | Soft curfew (S1API: `2100`) |
| `Il2CppScheduleOne.Law.CurfewManager` | `HARD_CURFEW_START_TIME` | `System.Int32` | Hard curfew (S1API: `2115`) |
| `Il2CppScheduleOne.Law.CurfewManager` | `CURFEW_END_TIME` | `System.Int32` | Curfew end (S1API: `500`) |
| `Il2CppScheduleOne.Law.CurfewManager` | `NORMAL_MESSAGE` / `CURFEW_MESSAGE` / `WARNING_MESSAGE` | `System.String` | VMS board text |
| `Il2CppScheduleOne.Law.PenaltyHandler` | `CONTROLLED_SUBSTANCE_FINE` | `System.Single` | Fine |
| `Il2CppScheduleOne.Law.PenaltyHandler` | `LOW_SEVERITY_DRUG_FINE` | `System.Single` | Fine |
| `Il2CppScheduleOne.Law.PenaltyHandler` | `MED_SEVERITY_DRUG_FINE` | `System.Single` | Fine |
| `Il2CppScheduleOne.Law.PenaltyHandler` | `HIGH_SEVERITY_DRUG_FINE` | `System.Single` | Fine |
| `Il2CppScheduleOne.Law.PenaltyHandler` | `FAILURE_TO_COMPLY_FINE` | `System.Single` | Fine |
| `Il2CppScheduleOne.Law.PenaltyHandler` | `EVADING_ARREST_FINE` | `System.Single` | Fine |
| `Il2CppScheduleOne.Law.PenaltyHandler` | `VIOLATING_CURFEW_TIME` | `System.Single` | Curfew penalty (name says `TIME`, siblings say `FINE`) |
| `Il2CppScheduleOne.Law.PenaltyHandler` | `ATTEMPT_TO_SELL_FINE` | `System.Single` | Fine |
| `Il2CppScheduleOne.Law.PenaltyHandler` | `ASSAULT_FINE` | `System.Single` | Fine |
| `Il2CppScheduleOne.Law.PenaltyHandler` | `DEADLY_ASSAULT_FINE` | `System.Single` | Fine |
| `Il2CppScheduleOne.Law.PenaltyHandler` | `VANDALISM_FINE` | `System.Single` | Fine |
| `Il2CppScheduleOne.Law.PenaltyHandler` | `THEFT_FINE` | `System.Single` | Fine |
| `Il2CppScheduleOne.Law.PenaltyHandler` | `BRANDISHING_FINE` | `System.Single` | Fine |
| `Il2CppScheduleOne.Law.PenaltyHandler` | `DISCHARGE_FIREARM_FINE` | `System.Single` | Fine |
| `Il2CppScheduleOne.Police.PoliceOfficer` | `OutOfSightTimeToDeactivate` | `System.Single` | How long unseen before an officer can be pooled |
| `Il2CppScheduleOne.Police.PoliceOfficer` | `INVESTIGATION_COOLDOWN` | `System.Single` | Gap between body-search investigations |
| `Il2CppScheduleOne.Police.PoliceOfficer` | `INVESTIGATION_MAX_DISTANCE` | `System.Single` | Max range to start investigating a player |
| `Il2CppScheduleOne.Police.PoliceOfficer` | `INVESTIGATION_MIN_VISIBILITY` | `System.Single` | Min `EntityVisibility.CurrentVisibility` to investigate |
| `Il2CppScheduleOne.Police.PoliceOfficer` | `INVESTIGATION_CHECK_INTERVAL` | `System.Single` | Poll rate of `CheckNewInvestigation` |
| `Il2CppScheduleOne.Police.PoliceOfficer` | `BODY_SEARCH_CHANCE_DEFAULT` | `System.Single` | Default per-officer `BodySearchChance` |
| `Il2CppScheduleOne.Police.PoliceOfficer` | `MIN_CHATTER_INTERVAL` / `MAX_CHATTER_INTERVAL` | `System.Single` | Radio chatter cadence |
| `Il2CppScheduleOne.Police.RoadCheckpoint` | `MAX_TIME_OPEN` | `System.Single` | Auto-close time for a checkpoint gate |
| `Il2CppScheduleOne.PlayerScripts.PlayerCrimeData` | `SEARCH_TIME_INVESTIGATING` | `System.Single` | How long cops search after losing you at level 1 |
| `Il2CppScheduleOne.PlayerScripts.PlayerCrimeData` | `SEARCH_TIME_ARRESTING` | `System.Single` | …at level 2 |
| `Il2CppScheduleOne.PlayerScripts.PlayerCrimeData` | `SEARCH_TIME_NONLETHAL` | `System.Single` | …at level 3 |
| `Il2CppScheduleOne.PlayerScripts.PlayerCrimeData` | `SEARCH_TIME_LETHAL` | `System.Single` | …at level 4 |
| `Il2CppScheduleOne.PlayerScripts.PlayerCrimeData` | `ESCALATION_TIME_ARRESTING` | `System.Single` | Time at `Arresting` before escalating |
| `Il2CppScheduleOne.PlayerScripts.PlayerCrimeData` | `ESCALATION_TIME_NONLETHAL` | `System.Single` | Time at `NonLethal` before escalating |
| `Il2CppScheduleOne.PlayerScripts.PlayerCrimeData` | `SHOT_COOLDOWN_MIN` / `SHOT_COOLDOWN_MAX` | `System.Single` | Rate limit on being shot at |
| `Il2CppScheduleOne.PlayerScripts.PlayerCrimeData` | `VEHICLE_COLLISION_LIFETIME` | `System.Single` | How long a vehicular hit stays on record |
| `Il2CppScheduleOne.PlayerScripts.PlayerCrimeData` | `VEHICLE_COLLISION_LIMIT` | `System.Single` | Hits before `VehicularAssault` |
| `Il2CppScheduleOne.NPCs.Behaviour.PursuitBehaviour` | `ARREST_RANGE` | `System.Single` | Distance at which arrest progress accrues |
| `Il2CppScheduleOne.NPCs.Behaviour.PursuitBehaviour` | `ARREST_TIME` | `System.Single` | Seconds in range to complete an arrest |
| `Il2CppScheduleOne.NPCs.Behaviour.PursuitBehaviour` | `EXTRA_VISIBILITY_TIME` | `System.Single` | Grace period after losing sight |
| `Il2CppScheduleOne.NPCs.Behaviour.PursuitBehaviour` | `MOVE_SPEED_INVESTIGATING` | `System.Single` | Speed at level 1 |
| `Il2CppScheduleOne.NPCs.Behaviour.PursuitBehaviour` | `MOVE_SPEED_ARRESTING` | `System.Single` | Speed at level 2 |
| `Il2CppScheduleOne.NPCs.Behaviour.PursuitBehaviour` | `MOVE_SPEED_CHASE` | `System.Single` | Sprint speed |
| `Il2CppScheduleOne.NPCs.Behaviour.PursuitBehaviour` | `CHASE_SPEED_DISTANCE_THRESHOLD` | `System.Single` | Distance at which they switch to chase speed |
| `Il2CppScheduleOne.NPCs.Behaviour.PursuitBehaviour` | `ARREST_MAX_DISTANCE` | `System.Single` | Give-up distance for arrest attempts |
| `Il2CppScheduleOne.NPCs.Behaviour.PursuitBehaviour` | `LEAVE_ARREST_CIRCLE_LIMIT` | `System.Int32` | Times you can flee the circle before escalation |
| `Il2CppScheduleOne.NPCs.Behaviour.BodySearchBehaviour` | `MAX_STEALTH_LEVEL` | `Il2CppScheduleOne.Product.Packaging.EStealthLevel` | Highest packaging tier a search can detect |
| `Il2CppScheduleOne.NPCs.Behaviour.BodySearchBehaviour` | `BODY_SEARCH_RANGE` | `System.Single` | Range to conduct the search |
| `Il2CppScheduleOne.NPCs.Behaviour.BodySearchBehaviour` | `MAX_SEARCH_TIME` | `System.Single` | Hard cap on search duration |
| `Il2CppScheduleOne.NPCs.Behaviour.BodySearchBehaviour` | `MAX_TIME_OUTSIDE_RANGE` | `System.Single` | Grace before non-compliance |
| `Il2CppScheduleOne.NPCs.Behaviour.BodySearchBehaviour` | `RANGE_TO_ESCALATE` | `System.Single` | Distance that triggers `FailureToComply` |
| `Il2CppScheduleOne.NPCs.Behaviour.BodySearchBehaviour` | `MOVE_SPEED` | `System.Single` | Approach speed |
| `Il2CppScheduleOne.NPCs.Behaviour.BodySearchBehaviour` | `BODY_SEARCH_COOLDOWN` | `System.Single` | Cooldown before you can be searched again |
| `Il2CppScheduleOne.NPCs.Behaviour.BodySearchBehaviour` | `BODY_SEARCH_TIME` | `System.Single` (get-only) | Derived search duration |
| `Il2CppScheduleOne.NPCs.Behaviour.CheckpointBehaviour` | `LOOK_TIME` | `System.Single` | Vehicle inspection duration |
| `Il2CppScheduleOne.NPCs.Behaviour.FootPatrolBehaviour` | `MOVE_SPEED` | `System.Single` | Patrol walk speed |
| `Il2CppScheduleOne.NPCs.Behaviour.FootPatrolBehaviour` | `FLASHLIGHT_MIN_TIME` | `System.Int32` | Earliest hour flashlights come out |
| `Il2CppScheduleOne.NPCs.Behaviour.FootPatrolBehaviour` | `FLASHLIGHT_ASSET_PATH` | `System.String` | Flashlight equippable path |
| `Il2CppScheduleOne.NPCs.Behaviour.SentryBehaviour` | `BodySearchChance` | `System.Single` | Search chance for sentries specifically |
| `Il2CppScheduleOne.NPCs.Behaviour.SentryBehaviour` | `FlashlightMinTime` | `System.Int32` | Flashlight window start |
| `Il2CppScheduleOne.NPCs.Behaviour.SentryBehaviour` | `FlashlightAssetPath` | `System.String` | Flashlight equippable path |
| `Il2CppScheduleOne.NPCs.Behaviour.SentryBehaviour` | `AngularSpeedMultiplier` | `System.Single` | Head/turn speed while on post |
| `Il2CppScheduleOne.NPCs.Behaviour.SentryBehaviour` | `WalkSpeed` | `System.Single` | Micro-route walk speed |
| `Il2CppScheduleOne.NPCs.Behaviour.VehiclePatrolBehaviour` | `MAX_CONSECUTIVE_PATHING_FAILURES` | `System.Single` | Abort threshold |
| `Il2CppScheduleOne.NPCs.Behaviour.VehiclePatrolBehaviour` | `PROGRESSION_THRESHOLD` | `System.Single` | Waypoint arrival distance |
| `Il2CppScheduleOne.NPCs.Behaviour.VehiclePursuitBehaviour` | `RECENT_VISIBILITY_THRESHOLD` | `System.Single` | "recently seen" window |
| `Il2CppScheduleOne.NPCs.Behaviour.VehiclePursuitBehaviour` | `EXIT_VEHICLE_MAX_SPEED` | `System.Single` | Speed below which cops bail out |
| `Il2CppScheduleOne.NPCs.Behaviour.VehiclePursuitBehaviour` | `CLOSE_ENOUGH_THRESHOLD` | `System.Single` | Arrival distance to chase point |
| `Il2CppScheduleOne.NPCs.Behaviour.VehiclePursuitBehaviour` | `UPDATE_FREQUENCY` | `System.Single` | Repath rate |
| `Il2CppScheduleOne.NPCs.Behaviour.VehiclePursuitBehaviour` | `STATIONARY_THRESHOLD` | `System.Single` | Speed considered stopped |
| `Il2CppScheduleOne.NPCs.Behaviour.VehiclePursuitBehaviour` | `TIME_STATIONARY_TO_EXIT` | `System.Single` | Stuck time before bailing out on foot |
| `Il2CppScheduleOne.NPCs.Behaviour.CallPoliceBehaviour` | `CALL_POLICE_TIME` | `System.Single` | Seconds a witness spends on the phone |
| `Il2CppScheduleOne.NPCs.Behaviour.Behaviour` | `MAX_CONSECUTIVE_PATHING_FAILURES` | `System.Int32` | Global behaviour abort threshold |
| `Il2CppScheduleOne.Combat.CombatBehaviour` | `RECENT_VISIBILITY_THRESHOLD` | `System.Single` | "recently seen" window |
| `Il2CppScheduleOne.Combat.CombatBehaviour` | `REPOSITION_TIME` | `System.Single` | Repositioning cadence in ranged combat |
| `Il2CppScheduleOne.Combat.CombatBehaviour` | `SEARCH_RADIUS_MIN` / `SEARCH_RADIUS_MAX` | `System.Single` | Search sweep radius |
| `Il2CppScheduleOne.Combat.CombatBehaviour` | `SEARCH_SPEED` | `System.Single` | Search movement speed |
| `Il2CppScheduleOne.Combat.CombatBehaviour` | `CONSECUTIVE_MISS_ACCURACY_BOOST` | `System.Single` | Rubber-banding accuracy after misses |
| `Il2CppScheduleOne.Combat.CombatBehaviour` | `REACHED_DESTINATION_DISTANCE` | `System.Single` | Arrival tolerance |
| `Il2CppScheduleOne.Combat.CombatBehaviour` | `DelayBeforeFirstAttack` | `System.Single` | Grace before the first swing/shot |
| `Il2CppScheduleOne.Combat.PhysicsDamageable` | `VELOCITY_HISTORY_LENGTH` | `System.Int32` | Physics-hit smoothing window |
| `Il2CppScheduleOne.Vision.VisionCone` | `VISION_UPDATE_INTERVAL` | `System.Single` | Vision tick rate |
| `Il2CppScheduleOne.Vision.VisionCone` | `MinVisionDelta` | `System.Single` | Floor for per-frame notice accrual |
| `Il2CppScheduleOne.Vision.VisionCone` | `UniversalAttentivenessScale` | `System.Single` | **Global notice-speed multiplier** |
| `Il2CppScheduleOne.Vision.VisionCone` | `UniversalMemoryScale` | `System.Single` | **Global memory-decay multiplier** |
| `Il2CppScheduleOne.Vision.VisionCone` | `HorizontalFOV` / `VerticalFOV` | `System.Single` | Cone shape |
| `Il2CppScheduleOne.Vision.VisionCone` | `Range` | `System.Single` | Base sight range (× `RangeMultiplier` = `effectiveRange`) |
| `Il2CppScheduleOne.Vision.VisionCone` | `MinorWidth` / `MinorHeight` | `System.Single` | Near-field cone dimensions |
| `Il2CppScheduleOne.Vision.VisionCone` | `ExclamationSoundCooldown` / `TimeOnLastExclamationSound` | `System.Single` | Alert-sound rate limit |
| `Il2CppScheduleOne.Vision.VisionEvent` | `NOTICE_DROP_THRESHOLD` | `System.Single` | Below this, a notice event is dropped |
| `Il2CppScheduleOne.Vision.EntityVisibility` | `MAX_VISIBLITY` | `System.Single` | Visibility clamp (note the typo) |
| `Il2CppScheduleOne.Vision.LightVisibilityAffector` | `PointLightEffect` / `SpotLightEffect` | `System.Single` | How much light raises visibility |
| `Il2CppScheduleOne.NPCs.NPCAwareness` | `PLAYER_AIM_DETECTION_RANGE` | `System.Single` | Range at which aiming a gun is noticed |
| `Il2CppScheduleOne.NPCs.NPC` | `PanicDuration` | `System.Int32` | How long NPCs stay panicked |
| `Il2CppScheduleOne.NPCs.NPC` | `HeadLightStartTime` / `HeadLightsEndTime` | `System.Int32` | Night window for vehicle lights |
| `Il2CppScheduleOne.UI.ArrestNoticeScreen` | `VEHICLE_POSSESSION_TIMEOUT` | `System.Single` | How long your vehicle survives after arrest |
| `Il2CppScheduleOne.UI.CrimeStatusUI` | `SmallTextSize` / `LargeTextSize` | `System.Single` | Wanted-indicator text sizes |

Non-constant statics worth knowing (registries and events, not tuning):

| Declaring type | Static member | Type |
|---|---|---|
| `Il2CppScheduleOne.Police.PoliceOfficer` | `Officers` | `Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Police.PoliceOfficer>` |
| `Il2CppScheduleOne.Police.PoliceOfficer` | `OnPoliceVisionEvent` | `Il2CppSystem.Action<Il2CppScheduleOne.Vision.VisionEventReceipt>` |
| `Il2CppScheduleOne.Map.PoliceStation` | `PoliceStations` | `Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Map.PoliceStation>` |
| `Il2CppScheduleOne.Law.CurfewInstance` | `ActiveInstance` | `Il2CppScheduleOne.Law.CurfewInstance` |

---

## 13. Open questions / unverified

1. **Constant values.** The dumps carry names and types only. No numeric value for any
   `PenaltyHandler` fine, `PursuitBehaviour.ARREST_TIME`, `VisionCone.Range`, etc. was
   recovered. Only the five `CurfewManager` times and `DAILY_INTENSITY_DRAIN` have
   second-hand values (from S1API's mirrors). **Next step:** read them at runtime with a tiny
   MelonMod that dumps the static properties, or run a value-carrying IL2CPP dumper.
2. **`const` vs mutable static.** If any of these fields is a C# `const`, IL2CPP inlined it
   and writing the exposed static property will silently do nothing. Not determinable from
   the current dumps. Must be probed empirically per field.
3. **Which field `setlawintensity` writes** — `LawController.LE_Intensity`,
   `internalLawIntensity`, or both. `Il2CppScheduleOne.Console+SetLawIntensity.Execute` body
   was not decompiled.
4. **How `LE_Intensity` is derived from `internalLawIntensity`** (rounding, clamping, when).
   `DayPass()` is the likely place but the arithmetic is unknown.
5. **Whether the fine is actually deducted, and where.** `PenaltyHandler.ProcessCrimeList`
   returns strings only; no `MoneyManager` call site was found in metadata. The deduction
   presumably happens in `ArrestNoticeScreen.OnClose()` / `Exit()` — unconfirmed.
6. **Whether cash is confiscated.** No `cash seized`/`cash confiscated` literal exists and no
   cash-removal member is on `ArrestNoticeScreen`. Probably only items are taken, but I did
   not prove it.
7. **`Crime.CrimeName` virtualness.** The dumper does not print property-accessor
   virtualness. The per-subclass backing fields strongly imply `virtual` + `override`, but it
   is not proven.
8. **`Offense` / `Offense+Charge` / `OffenceNoticeUI` live-ness.** Nothing references
   `Offense` except `OffenceNoticeUI.ShowOffenceNotice`. I believe it is dead legacy code
   (superseded by `ArrestNoticeScreen`) but could not prove `OffenceNoticeUI` is never shown.
   By extension, the `crimeIndex` ordering in §3.5 is untested.
9. **`Offense+Charge.crimeIndex` semantics.** Assumed to index the `Crime` declaration order;
   never verified against a call site.
10. **UnityEvent wiring.** `NPCAwareness.onNoticed*`, `Behaviour.onEnable/onDisable/onBegin/onEnd`,
    `BodySearchBehaviour.onSearchComplete_*`, `RoadCheckpoint.onPlayerWalkThrough` and the
    seven `CurfewManager.onCurfew*` events are serialised in prefabs/scenes, not in metadata.
    Their listener lists are unknown; the chains in §6.5 and §10 are inferred.
11. **`LawManager.Start`'s `_Start_b__3_0` subscription.** A single `UnityAction` is
    registered; which event it attaches to is unknown.
12. **`LawController.SaveFolderName` / `SaveFileName` values.** The bare literal `Law` exists
    in the metadata and `LawData`/`LawLoader` exist, so `Law.json` is essentially certain, but
    the getters' bodies were not read.
13. **Il2CppInterop fallback-name mapping.** The `Method_Protected_Virtual_Void_0` ⇒
    `Awake_UserLogic_<Type>_Assembly-CSharp.dll` correspondence is an inference from
    string-table adjacency (consistent across 13 checked types), not a proof. And the numeric
    suffixes will change between game versions regardless.
14. **`ClassInjector` viability** for a modded `Crime` subclass (§11.2 route A) and for a
    `PoliceOfficer` subclass (§11.4). Untested; the officer case involves FishNet codegen and
    a SyncVar and is likely to break.
15. **Individual `CrimeName` string values.** Only `Violating curfew` and
    `Failure to comply with police instruction` were positively isolated in
    `literals-sorted.txt`; the other 15 display names were not separated from the general
    string pool.
16. **`PenaltyHandler` coverage gap.** `DrugTrafficking`, `TransportingIllicitItems` and
    `VehicularAssault` have no dedicated fine constant. Whether they reuse another constant,
    are free, or are handled elsewhere is unknown.
17. **Whether `Il2CppScheduleOne.Police.NPCResponses_CartelGoon` shares any law plumbing.**
    It lives in the `Police` namespace and has an overlapping method set, but its
    `Goon` property points at `Il2CppScheduleOne.Cartel.CartelGoon`; I treated it as
    non-law-enforcement without investigating further.
18. **Nothing here was tested against a running game.** Per the brief, the game was never
    launched. All of the above is static analysis of `global-metadata.dat` and the
    Il2CppInterop-generated assemblies.

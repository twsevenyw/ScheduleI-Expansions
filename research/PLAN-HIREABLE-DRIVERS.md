# PLAN-HIREABLE-DRIVERS.md — implementation plan for the Hireable Drivers expansion

**Feature brief (Community Vote #2, 2025-09-01, the canonical spec):**
> "This new employee can transport items between your properties, businesses, and dealers. You'll be
> able to designate automatic transit routes and specify the required conditions (e.g. number of
> items in the vehicle) for a route to begin."

**Target:** `src/Expansions.HireableDrivers/`, module id `hireable_drivers`, hanging off
`Expansions.Core.ExpansionModule`.

**Reading conventions.** Every type/member below is copied from the API docs in `research/`, which
were transcribed verbatim from the IL2CPP dumps. Anything not provable from the dumps is tagged
**[UNVERIFIED]** and has a matching runtime probe in §9. Do not invent API names; if something in
here turns out not to exist, treat that as a bug in this plan and fix the plan.

---

## 0. Decisions up front

| # | Decision | Alternative rejected | Why |
|---|---|---|---|
| D1 | **Driver = a vanilla `Packager` (`EEmployeeType.Handler`) with the vanilla brain suppressed**, not a new `Employee` subclass and not a repurposed civilian | Injected `Employee` subclass; `(EEmployeeType)4`; cloned civilian NPC | §3.1 |
| D2 | **Driver logic lives in a plain managed class (`DriverBrain`) in a registry, not an injected `MonoBehaviour`** | `ClassInjector.RegisterTypeInIl2Cpp<DriverController>()` marker component | §2.2 |
| D3 | **Routes are stored in the vanilla `PackagerConfiguration.Routes : RouteListField`** — persistence and replication come free | Custom route DTOs in our own save blob | §2.4 |
| D4 | **`DriverBrain` owns the whole transport loop; `MoveItemBehaviour` is not used for the driver's own trips** | Harmony-patching `MoveItemBehaviour.IsTransitRouteValid` / `CanGetToSource` / `CanGetToDestination` to lie about cross-property reachability | §4.1 |
| D5 | **The payload container is the assigned vehicle's `LandVehicle.Storage`**, not `NPC.Inventory` | NPC-inventory carry with `skipPickup: true` | §5.2 |
| D6 | **The driver uses a player-owned vehicle selected by `LandVehicle.GUID`**; spawning a van is an opt-in fallback | Always spawn a mod-owned van | §5.1 |
| D7 | **Host-only.** All world mutation gated on `InstanceFinder.NetworkManager == null \|\| InstanceFinder.IsServer` | Client-side simulation | §2.5 |
| D8 | **Per-save mod data via `S1API.Internal.Abstraction.Saveable`**, never a hand-rolled `ISaveable` | Own `ISaveable` injection; patching `SaveManager.Save` | §2.4 |
| D9 | **Time-skip is resolved by snapping to the end state**, exactly like `NPCSignal_DriveToCarPark.JumpTo()` | Simulating the drive through the skip | §6.1 |
| D10 | **UI ships in two phases**: IMGUI page in `ExpansionMenu` first, native `PackagerConfigPanel` clone second | Native-only from day one | §1.6 |

---

## 1. Design — what the player actually does

### 1.1 The pitch

You already have Handlers who shuffle items *inside* one property. A **Driver** does the same job
*between* properties, businesses and dealers, using one of your cars. You hire them the same way,
pay them the same way, house them the same way, and program them with the same 5-route clipboard
grammar. The only new verbs are **"assign a vehicle"** and **"depart when the van holds N items."**

That parity is the whole design. Everything the player already knows about Handlers transfers.

### 1.2 Hiring

Identical to every shipped employee — the driver is created through
`EmployeeManager.CreateEmployee_Server(property, EEmployeeType.Handler, firstName, lastName, id,
male, appearanceIndex, position, rotation, guid)` on the server, then flagged as a driver in our
registry.

| Parameter | Value | Anchor |
|---|---|---|
| Signing fee | **$1,500 base, +$100 per existing employee** | Between Handler ($1,000) and Chemist ($2,000); the +$100/employee escalation is the shipped Handler rule (`research-ext/DESIGN-INTENT.md` §14.1) |
| Daily wage | **$250/day** | Between Handler ($200) and Chemist ($300) |
| Employee slot | **1 slot on the home property** (`Property.EmployeeCapacity`) | Shipped employee contract |
| Bed | **Required.** Bound through `PackagerConfiguration.Home : ObjectField` → `EmployeeHome.SetAssignedEmployee(employee)` | Shipped: no bed ⇒ `Employee.BedNotAssignedDialogue`, `CanWork()` false |
| Wage payment | Cash placed in the bed's `EmployeeHome.Storage`; drawn by the vanilla `IsPayAvailable()` → `SetIsPaid()` → `RemoveDailyWage()` → `EmployeeHome.RemoveCash(DailyWage)` chain | Zero new economy code |
| Working hours | **7:00 AM – 4:00 AM** | Shipped day window; `TimeManager.CurrentTime` |

**Read the real numbers at runtime first.** `Employee.SigningFee` / `Employee.DailyWage` are
Unity-serialized prefab floats and are **[UNVERIFIED]** in the dumps. Read
`EmployeeManager.Instance.PackagerPrefab.SigningFee` / `.DailyWage`, log them, and only then decide
whether to override the instance fields on our drivers (they are `public get; public set;`).

### 1.3 Where the driver lives and idles

- **Home property** = the `Property` passed to `CreateEmployee_Server`. Registered via
  `Property.RegisterEmployee(emp)`, appears in `Property.Employees`.
- **Idle** = the vanilla `Employee.WaitOutside : IdleBehaviour`. Between trips we set
  `employee.WaitOutside.IdlePoint` to the assigned vehicle's `LandVehicle.driverEntryPoint` when the
  van is parked at the home property, otherwise leave it on the vanilla
  `Property.EmployeeIdlePoints` entry. Small touch, sells the role.
- **Bed** = a `BedItem` on the home property, same as any employee.

### 1.4 Assigning a route

Five route rows per driver — exact parity with the Handler
(`RouteListField.MaxRoutes`; read the real value at runtime, expected 5). Each row is:

| Field | Type | Notes |
|---|---|---|
| **From** | `ITransitEntity` on **any owned property or business** | Any of the 12 verified implementors (`PlaceableStorageEntity`, `PackagingStation`, `BrickPress`, `Pot`, `MushroomBed`, `DryingRack`, `LabOven`, `Cauldron`, `MixingStation`, `ChemistryStation`, `TrashContainerItem`, `MushroomSpawnStation`) **plus `Delivery.LoadingDock`** |
| **To** | `ITransitEntity` **or a `Dealer`** | Dealer destinations are stored separately — `Dealer` is an `NPC`, not an `ITransitEntity` (§4.5) |
| **Item filter** | `ManagementItemFilter` (`Whitelist` / `Blacklist` over `ItemDefinition`s) | The vanilla filter widget |
| **Depart when cargo ≥ N** | int, default = half the trunk's unit capacity | Tyler's exact phrasing: *"the required conditions (e.g. number of items in the vehicle)"* |
| **Depart anyway after H hours** | int, default 4 in-game hours | Prevents a slow source deadlocking a route. Our addition; config-exposed |

Routes execute **top-to-bottom in list order**, and within a route the item tie-break is
**alphabetical by item name** — verbatim shipped Handler semantics, so the mental model transfers.

### 1.5 A trip, as the player sees it

1. The driver is standing by the van at the barn. A route says *Barn shelf → Bungalow shelf, OG Kush,
   depart at 40 items*.
2. He walks to the shelf, works for a while, walks to the van, loads it. Repeats until the van holds
   40 units or the shelf is dry.
3. He gets in, the engine starts, the van pulls out of the barn's parking lot and drives across town
   on real roads, stopping at lights.
4. It parks in the Bungalow's lot. He gets out, carries the cargo to the destination shelf, and fills
   it.
5. He drives home, parks, and goes back to idling next to the van.

Everything visible in that list is a shipped system: `VehicleAgent` drives, `LandVehicle.Park` parks,
`NPCMovement` walks, `ItemSlot`/`StorageEntity` moves goods. We are sequencing, not simulating.

### 1.6 Surfacing it in UI

**Phase 1 (v0.1 — ships first).** A "Drivers" page in the existing `ExpansionMenu` (F7) IMGUI
surface. Per the project's IL2CPP gotchas: absolute `GUI.Button` / `GUI.Label` only — **no
`GUILayout.TextField`, no `GUI.DrawTexture`** (both stripped/broken on this build). Shows the driver
roster, per-driver route rows with cycle-through pickers, live state readout
(`Idle / Loading / Driving / Unloading / Recovering`), and hire/fire. Ugly but complete, and it is
what lets M4–M8 be tested at all.

**Phase 2 (v0.2 — the native feel).** Postfix
`ManagementInterface.GetConfigPanelPrefab(EConfigurableType)`; when the selected `IConfigurable` is a
registered driver, return a **runtime clone** of the vanilla `PackagerConfigPanel` with `StationsUI`
deactivated, `RoutesUI` kept, and two new rows (vehicle picker, departure threshold) built by cloning
the existing `NumberFieldUI` / `ObjectFieldUI` widgets. The whole route editor —
`RouteListFieldUI`, `RouteEntryUI`, `TransitEntitySelector` — comes for free.

Clone, never author. There is no `ui/` Resources path and no asset bundles in this build, so nothing
is loadable by name (`CONTEXT.md`, verified).

---

## 2. Architecture

### 2.1 File layout

```
src/Expansions.HireableDrivers/
  HireableDriversMod.cs                  (exists) MelonMod shim, [assembly: HarmonyDontPatchAll]
  HireableDriversModule.cs               (exists) ExpansionModule — owns config, patches, lifetime, tick
  Config/DriverSettings.cs               all ConfigValue<T> handles, bound in OnRegistered()
  Runtime/DriverRegistry.cs              static roster: guid -> DriverBrain, IntPtr -> DriverBrain
  Runtime/DriverBrain.cs                 the per-driver state machine (plain managed class)
  Runtime/DriverState.cs                 EDriverState + TripContext
  Runtime/DriverHiring.cs                CreateEmployee_Server wrapper, re-skin, fee/wage, fire
  Runtime/VehicleAssignment.cs           pick/validate/park/recover a LandVehicle
  Runtime/CargoTransfer.cs               the lock-safe slot-mutation primitives
  Runtime/TransitEndpoint.cs             union of { ITransitEntity, Dealer }; resolve by GUID / npcID
  Runtime/DriverClock.cs                 game-minute pacing; TimeManager event wiring
  Persistence/DriverSaveStore.cs         S1API Saveable subclass; reattach on load
  Persistence/DriverSaveData.cs          the blob DTOs
  Patches/PackagerBrainPatches.cs        UpdateBehaviour / ShouldIdle / IsAnyWorkInProgress
  Patches/EmployeeLifecyclePatches.cs    Fire / OnSleepEnd
  Patches/ManagementPanelPatches.cs      GetConfigPanelPrefab / RouteEntryUI.ObjectValid   (phase 2)
  Diagnostics/DriverProbe.cs             M0 probes + S1API console command
  UI/DriverRouteMenu.cs                  IMGUI page in ExpansionMenu               (phase 1)
```

### 2.2 How it hangs off `ExpansionModule`

`ExpansionModule` gives us `Log`, `Config`, `Harmony`, `Lifetime` and the frame hooks. Nothing else
is needed.

```csharp
public sealed class HireableDriversModule : ExpansionModule
{
    public const string ModuleId = "hireable_drivers";
    public override string Id => ModuleId;                       // -> HireableDrivers_01_Main

    private DriverSettings _settings = null!;
    private float _tickAccumulator;

    // Runs once, before the first enable. Settings must be declared here, not in OnEnabled.
    protected override void OnRegistered() => _settings = new DriverSettings(Config);

    public override void OnEnabled()
    {
        // Everything below registers its own teardown so OnDisabled stays empty.
        PackagerBrainPatches.Apply(Harmony, Lifetime);
        EmployeeLifecyclePatches.Apply(Harmony, Lifetime);

        DriverClock.Subscribe(Lifetime);            // TimeManager.onMinutePass / onSleepStart / onTimeSkip
        DriverSaveStore.Register(Lifetime);         // S1API Saveable
        Lifetime.OnDispose(DriverRegistry.Clear);   // drop every DriverBrain, stop every trip cleanly
    }

    public override void OnUpdate()
    {
        if (!HostGate.IsAuthoritative) return;      // InstanceFinder.NetworkManager == null || IsServer
        _tickAccumulator += UnityEngine.Time.deltaTime;
        if (_tickAccumulator < _settings.TickIntervalSeconds.Value) return;
        _tickAccumulator = 0f;
        DriverRegistry.Tick();                      // try/catch inside; ModuleContext auto-disables after 10 throws
    }

    public override void OnSceneLoaded(int buildIndex, string sceneName)
    {
        if (sceneName != "Main") return;
        // Patch/attach only once the game scene is live — community-standard, avoids patching
        // before game types are ready (CONTEXT.md decision, 2026-08-03).
        DriverRegistry.RebindAfterSceneLoad();
    }

    public override void OnDisabled() { }           // deliberately empty; Lifetime + UnpatchSelf do it
}
```

Everything the module creates is registered with `Lifetime`, and every patch goes through the
injected per-module `Harmony` instance. Never `[HarmonyPatch]` attributes — the mod assembly carries
`[assembly: HarmonyDontPatchAll]` precisely so that disabling the module genuinely unpatches.

### 2.3 Why `DriverBrain` is a plain managed class, not an injected `MonoBehaviour`

`research/FEASIBILITY.md` and `research/API-EMPLOYEES.md` §12.4 both recommend an injected
`MonoBehaviour` marker (`DriverController`). **Deviate, for three reasons:**

1. **`ClassInjector.RegisterTypeInIl2Cpp<T>()` is process-global and irreversible.** That directly
   contradicts the module's reversible-toggle guarantee. `ModuleLifetime` cannot undo it.
2. **We do not need Unity to tick us.** `ExpansionModule.OnUpdate` is already a frame pump, and the
   registry gives us deterministic iteration order (which matters when five drivers compete for one
   van).
3. **A dictionary is just as good a marker.** Harmony patches receive `__instance`;
   `DriverRegistry.TryGet(__instance)` is an O(1) lookup and avoids the `IntPtr` ctor,
   `[HideFromIl2Cpp]` rules, and the "fields are not Unity-serialized" caveats entirely.

Keying: primary key `employee.GUID.ToString()` (stable across save/load); secondary hot-path key
`employee.Pointer` (`IntPtr`, stable for the object's lifetime) for patch lookups. Every tick, reap
brains whose `Employee` has gone Unity-null.

The one thing we lose is automatic destruction when the GameObject dies. Handle it explicitly:

```csharp
// DriverRegistry.Tick()
for (int i = _brains.Count - 1; i >= 0; i--)
{
    var brain = _brains[i];
    if (brain.Employee == null) { brain.AbortAndRelease(); _brains.RemoveAt(i); continue; }
    brain.Tick();
}
```

### 2.4 The route data model and where it persists

**Routes live in the vanilla config.** `PackagerConfiguration.Routes : RouteListField` holds
`List<AdvancedTransitRoute>`, and the vanilla save writes it to
`PackagerConfigurationData.Routes : RouteListData` as
`AdvancedTransitRouteData { SourceGUID, DestinationGUID, FilterMode, FilterItemIDs }`. It also
replicates over `ConfigurationReplicator.SendRouteListField` / `ReceiveRouteListField`.

Use it. Free persistence, free replication, free UI in phase 2. Our sidecar stores only what vanilla
has no field for:

```csharp
// Persistence/DriverSaveData.cs — serialized by S1API's Newtonsoft pipeline
public sealed class DriverSaveData
{
    public int    Version = 1;                      // fail soft on a newer blob
    public List<DriverRecord> Drivers = new();
}

public sealed class DriverRecord
{
    public string  EmployeeGuid = "";               // Employee.GUID.ToString()
    public string  VehicleGuid  = "";               // LandVehicle.GUID.ToString(); "" = none assigned
    public bool    SpawnedVehicle;                  // true => we own it, destroy it on unhire
    public List<RouteExtras> Routes = new();        // index-parallel with PackagerConfiguration.Routes
    public List<DealerRoute> DealerRoutes = new();  // dealers can't live in a RouteListField
}

public sealed class RouteExtras
{
    public int  DepartAtItemCount;                  // 0 => auto (half the trunk's unit capacity)
    public int  DepartAfterGameHours = 4;
    public bool Enabled = true;
}

public sealed class DealerRoute
{
    public string SourceGuid  = "";                 // ITransitEntity.GUID
    public string DealerNpcId = "";                 // NPC.ID, e.g. the dealer's npcID string
    public int    FilterMode;                       // ManagementItemFilter.EMode
    public List<string> FilterItemIds = new();
    public int    TopUpCap = 10;
}
```

Persisted with `S1API.Internal.Abstraction.Saveable` + `[SaveableField("hireable_drivers")]`, which
lands in the save slot's `Modded\Saveables\` subtree. **Do not implement `ISaveable` yourself** and
**do not patch `SaveManager.Save(string)`** — S1API already has three postfixes there
(`research/API-S1API.md` §23.1) and a throw from your `GetSaveString()` runs inside the vanilla save
coroutine, i.e. you can abort the player's save.

In-flight cargo needs no bespoke persistence: it is sitting in a real `LandVehicle.Storage`, which
vanilla already round-trips through `VehicleData` / `LandVehicle.GetContentsSet()`.

### 2.5 Networking posture

Host-only, stated explicitly. Gate every mutation:

```csharp
public static class HostGate
{
    public static bool IsAuthoritative =>
        Il2CppFishNet.InstanceFinder.NetworkManager == null ||
        Il2CppFishNet.InstanceFinder.IsServer;

    public static bool IsRemoteClient =>
        Il2CppFishNet.InstanceFinder.NetworkManager != null &&
        Il2CppFishNet.InstanceFinder.IsClientOnly;
}
```

Do **not** use `InstanceFinder.IsHost` — it is false for a dedicated server and false before the
network starts (`research/API-NETWORKING-CONSOLE.md`). The null check is the important half.

On a remote client the module runs **nothing** but a read-only UI that greys out management controls
and says "Host only" — the exact posture `bwyan-HireableDeliveryDriver_IL2CPP_port` ships and the
ecosystem norm. Clients still see the driver and the moving van, because both are vanilla
`NetworkBehaviour`s that FishNet replicates on their own. Expect the van to look smooth on the host
and interpolated on clients: `VehicleAgent` is a plain `MonoBehaviour` and is **not** networked; only
`LandVehicle`'s transform and its `CurrentSteerAngle` / `BrakesApplied` / `IsReversing` SyncVars
replicate.

---

## 3. The driver NPC

### 3.1 The role decision: repurposed `Packager`, not a new `Employee` subclass

**Verified constraint:** `EEmployeeType` is a closed enum `{ Botanist = 0, Handler = 1, Chemist = 2,
Cleaner = 3 }`. `Employee` has exactly four subclasses: `Botanist`, `Chemist`, `Cleaner`, `Packager`.
`Handler` is only the *enum name* for the `Packager` class — there is no `Handler` type. No `Driver`,
`Courier`, `Deliverer` or `Transporter` exists. That is verified-absent, not merely unfound.

Three options, analysed:

**Option A — inject an `Employee` subclass (`ClassInjector.RegisterTypeInIl2Cpp<Driver>()`).**
Rejected. `Employee : NPC : Il2CppFishNet.Object.NetworkBehaviour`, and FishNet's IL weaver generates
`NetworkInitialize___Early()`, `NetworkInitialize__Late()`, `NetworkInitializeIfDisabled()`,
`ReadSyncVar___ScheduleOne_Employees_<Type>` and every `RpcWriter/Reader/Logic` triple **at build
time**. An injected type gets none of them. Concretely: `Employee.Initialize(...)` is an
ObserversRpc/TargetRpc pair, so the driver's name, appearance and property assignment would never
reach any client — and `PaidForToday` is a SyncVar that would never serialize. Worse, FishNet only
spawns prefabs whose `NetworkObject` carries a valid prefabId registered in `SpawnableObjects`; the
game itself logs *"Spawned object has an invalid prefabId."* A runtime-created GameObject has none.

**Option B — synthetic `(EEmployeeType)4`.** Rejected. IL2CPP enums are plain ints so the cast
compiles, but `EmployeeManager.GetEmployeePrefab` returns null for it and you inherit an unbounded
patch surface: `GetEmployeePrefab`, `GetEmployeesByType`, `ConfigurableType.GetTypeName`,
`ManagementInterface.GetConfigPanelPrefab`, `Quest_Employees`, `AddEmployeeCommand.Execute`, plus
every vanilla `switch` with no case for it — which fails silently, not loudly. You also need a new
`EmployeeData` subclass and a new `EmployeeLoader`, and the `DataType` JSON discriminator is
**[UNVERIFIED]**. Enormous risk for a cosmetic win.

**Option C — repurpose a civilian NPC (clone from `NPCManager.NPCRegistry`).** Rejected. You lose
wages, the bed contract, firing, the employee clipboard, the employee quest chain, `Property`
registration and the whole employee save path — and you hit the same invalid-prefabId problem for the
network spawn.

**Chosen: Option D — a real `Packager`, hired through the real factory, with its packaging brain
suppressed and a `DriverBrain` steering it.**

```csharp
// Host only.
var mgr = Il2CppScheduleOne.Employees.EmployeeManager.Instance;   // NetworkSingleton<EmployeeManager>
mgr.GenerateRandomName(male, out var first, out var last);
mgr.GetRandomAppearance(male, out int appearanceIndex, out var avatarSettings);

var guid = System.Guid.NewGuid().ToString();
var spawn = homeProperty.NPCSpawnPoint;

Il2CppScheduleOne.Employees.Employee emp = mgr.CreateEmployee_Server(
    homeProperty,
    Il2CppScheduleOne.Employees.EEmployeeType.Handler,   // == PackagerPrefab
    first, last,
    id: $"driver_{guid[..8]}",
    male, appearanceIndex,
    spawn.position, spawn.rotation,
    guid);
```

What that single call buys, all verified: a networked `NetworkObject` with a valid prefabId, a rigged
`Avatar`, a `NavMeshAgent` sized for the humanoid agent type, `NPCInventory`, `NPCMovement`,
`DialogueHandler`, a voice, a mugshot, `Employee.MoveItemBehaviour`, `Employee.WaitOutside`,
`PackagerConfiguration` (Home + Stations + **Routes**), `PackagerConfigPanel`, `PackagerUIElement`,
bed binding, the wage pipeline, `Employee.Fire()` / `LeavePropertyAndDespawn()`, and vanilla save/load
through `PackagerLoader`.

Cost: the clipboard says "Handler" and offers a Stations row. Both are fixable in phase 2 (§7 patches
7–8), and neither blocks shipping.

### 3.2 Making it look and read like a driver

Appearance is data, not a type (proved by `GoonPool.CartelGoonAppearance`). Two levers, in order of
preference:

```csharp
// 1. Cheapest: pick an existing employee appearance and tweak the colours.
var appearance = mgr.GetAppearance(male, appearanceIndex);   // EmployeeManager+EmployeeAppearance
emp.Avatar.LoadAvatarSettings(appearance.Settings);

// 2. Authored look: build a BasicAvatarSettings (31 semantic fields) and convert.
//    NOTE the sub-namespace split: BasicAvatarSettings is in AvatarFramework.Customization,
//    AvatarSettings and Avatar are in AvatarFramework. There is NO ApplyBasicSettings method.
var basic = UnityEngine.ScriptableObject
    .CreateInstance<Il2CppScheduleOne.AvatarFramework.Customization.BasicAvatarSettings>();
// ... set the 31 fields (see research/API-NPCS.md §6.4) ...
emp.Avatar.LoadAvatarSettings(basic.GetAvatarSettings());
```

Slot budget is 6 face layers / 8 body layers / 9 accessories. A cap plus a work jacket is enough to
read as "driver" at a glance.

**Naming.** `EntityConfiguration.Name : StringField` is inherited by `PackagerConfiguration` and
`AllowRename()` is virtual, so set the config name to `"Driver {FirstName}"`. That is what the
worldspace `PackagerUIElement` and the clipboard header display. Do not touch
`Employee.InitializeInfo` after creation — it is an RPC and re-running it post-spawn is untested.

### 3.3 Behaviour tree — what we enable and disable

The driver keeps the vanilla `NPCBehaviour` stack. We do not add a `Behaviour` subclass — injecting
one is explicitly **[UNVERIFIED]** on this build (`API-EMPLOYEES.md` §12.5) and would hit the same
FishNet codegen wall as an injected `Employee`.

Instead, on trip start:

```csharp
employee.SetIdle(false);
employee.SetWaitOutside(false);
employee.MoveItemBehaviour.Disable_Server();     // stop vanilla item work competing for movement
employee.WaitOutside.Disable_Server();           // stop IdleBehaviour re-issuing SetDestination
```

and on trip end / abort, the inverse (`employee.SetWaitOutside(true)`). With
`Packager.UpdateBehaviour()` prefixed to return `false` for our drivers (§7 patch 1), nothing in
vanilla will re-enable a competing behaviour, so `NPCBehaviour.activeBehaviour` stays null/idle and
`DriverBrain`'s `NPCMovement.SetDestination` calls are uncontested.

> Note the casing trap: the property is **`activeBehaviour`**, lower-case `a`.

Movement uses the callback overload, so we own completion:

```csharp
// Cache the managed delegate in a field. Il2CppInterop wraps it; if it is GC'd mid-walk the
// callback never fires. (research/IL2CPP-NOTES.md §5)
_walkCallback ??= (Il2CppSystem.Action<Il2CppScheduleOne.NPCs.NPCMovement.WalkResult>)OnWalkResult;

employee.Movement.SetDestination(standPoint, _walkCallback,
                                 interruptExistingCallback: true,
                                 successThreshold: 1f, cacheMaxDistSqr: 1f);
```

Stand points come from `Il2CppScheduleOne.DevUtilities.NavMeshUtility.GetReachableAccessPoint(entity,
npc)`, with arrival confirmed by `NavMeshUtility.IsAtTransitEntity(entity, npc, 0.4f)` — the same
predicates `MoveItemBehaviour` uses, so we inherit its tuning.

### 3.4 Registration and cleanup

**Registration.** `CreateEmployee_Server` already does the FishNet spawn, the
`Property.RegisterEmployee` and the `EmployeeManager.AllEmployees` insert. All we add is
`DriverRegistry.Register(emp, record)`.

**Cleanup on module disable / unload** — registered with `Lifetime`, in this order:

1. Abort every in-flight trip: `Agent.StopNavigating()`, `vehicle.StopVehicle()`,
   `destination.RemoveSlotLocks(employee.NetworkObject)` (**never skip this — a leaked lock
   permanently jams the destination**), leave cargo where it physically is.
2. Restore each driver to vanilla: re-enable `MoveItemBehaviour` and `WaitOutside`,
   `employee.SetWaitOutside(true)`.
3. Destroy any vehicle we spawned ourselves (`SpawnedVehicle == true` →
   `LandVehicle.DestroyVehicle()`); never touch player-owned vehicles.
4. `Harmony.UnpatchSelf()` (done by `ModuleContext.EndCycle`) — the drivers revert to being ordinary
   Handlers with empty station lists. **They survive as valid vanilla employees**, which is the right
   uninstall behaviour: the save still loads, the NPCs still work, they just stop driving.
5. `DriverRegistry.Clear()`.

---

## 4. The transport loop

### 4.1 Why we own it instead of using `MoveItemBehaviour`

`MoveItemBehaviour` is a complete walk → grab → walk → place courier, and it is tempting. It is also
the wrong shape:

- Its route validity gate is `IsTransitRouteValid(route, templateItem, out reason)` →
  `CanGetToSource` / `CanGetToDestination` → `NavMeshUtility.GetReachableAccessPoint(...)`, which is a
  **walking NavMesh** check. Cross-property routes will almost certainly fail it
  (**[UNVERIFIED]**, probe P4 in §9).
- Making it pass requires patching three methods to lie about reachability, and then the behaviour
  still walks the whole way instead of driving.
- `LandVehicle` is **definitively not** an `ITransitEntity` — it matches 1 of 16 members (`GUID`, via
  `IGUIDRegisterable`), and `LandVehicle.Storage` exposes `ItemSlots`, not the `InputSlots` /
  `OutputSlots` split a `TransitRoute` needs. So the trunk can never be a route endpoint and
  `MoveItemBehaviour` can never load it.

Owning ~200 lines of state machine that only calls verified primitives is less fragile than three
Harmony patches that make a shipped behaviour lie. **Own the loop.** Keep `MoveItemBehaviour` on the
employee, disabled, as a same-property fallback if a route's source and destination turn out to be
on the same property (then a Handler could already do the job and we just delegate).

### 4.2 The state machine

```
                 ┌──────────────────────────────────────────────────────────┐
                 v                                                          │
  Idle ──(route trigger armed)──> Preparing ──> WalkToSource ──> LoadFromSource ──┐
                 ^                    │                                          │
                 │                    └──(invalid)──> Idle + SubmitNoWorkReason   │
                 │                                                                v
                 │                                                    (cargo >= threshold
                 │                                                     OR timeout OR source dry)
                 │                                                                │
                 │                                                                v
                 │                                                       WalkToVehicle
                 │                                                                │
                 │                                                                v
                 │                                                       BoardVehicle
                 │                                                                │
                 │                                                                v
                 │                                                       DriveToDestination
                 │                                                                │
                 │                                        ┌──(Failed)─────────────┤
                 │                                        v                       v (Complete)
                 │                                   Recovering              ParkAtDestination
                 │                                        │                       │
                 │                                        │                       v
                 │                                        │                  Disembark
                 │                                        │                       │
                 │                                        │                       v
                 │                                        │              WalkToDestination
                 │                                        │                       │
                 │                                        │                       v
                 │                                        │               UnloadToDestination
                 │                                        │                       │
                 │                                        v                       v
                 └───────────────────────────────── DriveHome  <─────────────  BoardVehicle
```

`EDriverState { Idle, Preparing, WalkToSource, LoadFromSource, WalkToVehicle, BoardVehicle,
DriveToDestination, ParkAtDestination, Disembark, WalkToDestination, UnloadToDestination, DriveHome,
Recovering }`

### 4.3 Transition table — the exact calls

| From → To | Trigger | Calls |
|---|---|---|
| `Idle` → `Preparing` | Tick: `HostGate.IsAuthoritative`, `employee.CanWork()`, clock in 07:00–04:00, `!TimeManager.Instance.IsSleepInProgress`, a route is armed | — |
| `Preparing` → `WalkToSource` | All validity checks pass | `route.AreEntitiesNonNull()`; `!src.IsDestroyed && !dst.IsDestroyed`; `dst.IsAcceptingItems`; `item = route.GetItemReadyToMove()` (non-null); `VehicleAssignment.TryClaim(driver, out vehicle)`; then disable competing behaviours (§3.3) and `employee.Movement.SetDestination(NavMeshUtility.GetReachableAccessPoint(src, npc).position, _walkCallback, …)` |
| `Preparing` → `Idle` | Any check fails | `employee.SubmitNoWorkReason(reason, fix, priority)` (ObserversRpc — the vanilla "why isn't my employee working" channel), `employee.SetWaitOutside(true)` |
| `WalkToSource` → `LoadFromSource` | `WalkResult.Success`, or `NavMeshUtility.IsAtTransitEntity(src, npc, 0.4f)` | Start the load clock |
| `LoadFromSource` (self-loop) | One batch per `LoadMinutesPerStop / batches` game-minutes | `CargoTransfer.SourceToTrunk(src, vehicle, template, npc)` — see §4.4 |
| `LoadFromSource` → `WalkToVehicle` | `trunkUnits >= departThreshold` **or** `DepartAfterGameHours` elapsed **or** source dry **or** trunk full | `employee.MarkIsWorking()` |
| `WalkToVehicle` → `BoardVehicle` | `WalkResult.Success` at `vehicle.driverEntryPoint.position` | — |
| `BoardVehicle` → `DriveToDestination` | Boarding succeeded | `employee.EnterVehicle(null, vehicle)` (fires `NPC.onEnterVehicle`, calls `LandVehicle.AddNPCOccupant(npc)`); `vehicle.StartVehicle()`; set `DriveFlags`; `vehicle.Agent.Navigate(target, navSettings, _navCallback)` — see §4.6 |
| `DriveToDestination` → `ParkAtDestination` | `NavigationCallback(ENavigationResult.Complete)` | `vehicle.Park(null, new ParkData { lotGUID = lot.GUID, spotIndex = i, alignment = lot.ParkingSpots[i].Alignment }, network: true)` |
| `DriveToDestination` → `Recovering` | `ENavigationResult.Failed`, or `Agent.GetIsStuck()` | §5.4 |
| `ParkAtDestination` → `Disembark` | Park returned | `employee.ExitVehicle()`; `vehicle.StopVehicle()` |
| `Disembark` → `WalkToDestination` | — | `employee.Movement.SetDestination(NavMeshUtility.GetReachableAccessPoint(dst, npc).position, _walkCallback, …)` |
| `WalkToDestination` → `UnloadToDestination` | `WalkResult.Success` / `IsAtTransitEntity(dst, npc, 0.4f)` | `dst.ReserveInputSlotsForItem(payload, employee.NetworkObject)` |
| `UnloadToDestination` (self-loop) | One batch per game-minute quantum | `CargoTransfer.TrunkToDestination(vehicle, dst, npc)` — see §4.4 |
| `UnloadToDestination` → `DriveHome` | Trunk empty, or destination full, or timeout | **`dst.RemoveSlotLocks(employee.NetworkObject)` in a `finally`**; `employee.MarkIsWorking()` |
| `DriveHome` → `Idle` | `ENavigationResult.Complete` at the home lot | Park; `ExitVehicle()`; `StopVehicle()`; re-enable `WaitOutside`; `VehicleAssignment.Release(vehicle)` |
| any → `Recovering` | Vehicle destroyed / player boarded / abort requested | §5.4 |
| `Recovering` → `Idle` | Recovery finished or gave up | Locks released, cargo left in place, work issue submitted |

### 4.4 Item transfer — the exact primitives

Both directions use the verified recipes from `research/API-PROPERTY.md` §12. Server only.

**Source `ITransitEntity` → trunk `StorageEntity`:**

```csharp
// src is already an ITransitEntity (obtained via TryCast<ITransitEntity>() — `is`/`as` will NOT work,
// Il2CppInterop emits the interface as a class).
Il2CppScheduleOne.ItemFramework.ItemSlot from =
    src.GetFirstSlotContainingTemplateItem(template, Il2CppScheduleOne.Management.ITransitEntity.ESlotType.Output);
if (from?.ItemInstance == null || from.IsLocked || from.IsRemovalLocked) return 0;

int available = src.GetOutputCapacityForItem(from.ItemInstance, asker: npc);
int space     = vehicle.Storage.HowManyCanFit(from.ItemInstance);
int amount    = Math.Min(Math.Min(available, space), from.Quantity);
if (amount <= 0) return 0;

var payload = from.ItemInstance.GetCopy(overrideQuantity: amount);   // never alias the source instance
from.ChangeQuantity(-amount);                                        // _internal = false -> replicates
vehicle.Storage.InsertItem(payload, network: true);
vehicle.Storage.ContentsChanged();
return amount;
```

**Trunk → destination `ITransitEntity`:**

```csharp
var reserved = dst.ReserveInputSlotsForItem(payload, npc.NetworkObject);   // lock so nothing else claims it
try
{
    foreach (var slot in vehicle.Storage.ItemSlots)
    {
        if (slot.ItemInstance == null) continue;
        int space = dst.GetInputCapacityForItem(slot.ItemInstance, asker: npc, checkPlayerFilters: true);
        int n     = Math.Min(space, slot.Quantity);
        if (n <= 0) continue;                       // destination full or filtered -> leave it in the van

        var chunk = slot.ItemInstance.GetCopy(n);
        slot.ChangeQuantity(-n);
        dst.InsertItemIntoInput(chunk, inserter: npc);
    }
    vehicle.Storage.ContentsChanged();
}
finally
{
    dst.RemoveSlotLocks(npc.NetworkObject);          // ALWAYS. A leaked lock jams the entity forever.
}
```

Traps that will bite, all documented and all real:

- **Slot filters are two-layered** — `ItemSlot.HardFilters` (code-defined) and `ItemSlot.PlayerFilter`
  (`SlotFilter` the player set). Pass `checkPlayerFilters: true` or you will shove a mixer into a
  product slot.
- **Stacking needs `ItemInstance.CanStackWith(other, checkQuantities)`** — quality-bearing instances,
  cash and water only stack when the extra data matches. Always `GetCopy(overrideQuantity)`.
- **`IsAcceptingItems` is a real gate**, not decoration; stations set it false mid-operation.
- **Shelf visuals do not auto-refresh.** `ItemSlot.onItemDataChanged` / `StorageEntity.ContentsChanged()`
  update the inventory UI, but `StorageVisualizer` needs `QueueRefresh()` / `RefreshVisuals()`.
- **Never touch `field_Private_*` / `Method_*_PDM_*`** — positional Il2CppInterop names that shift
  between game versions.

### 4.5 Dealer destinations differ

`Il2CppScheduleOne.Economy.Dealer : Il2CppScheduleOne.NPCs.NPC`. It is **not** an `ITransitEntity`
(no `AccessPoints`, no `InputSlots`/`OutputSlots`, no `LinkOrigin`), so a dealer can never be an
`AdvancedTransitRoute.Destination`. Three concrete differences:

| Aspect | `ITransitEntity` destination | `Dealer` destination |
|---|---|---|
| Storage | `dst.InsertItemIntoInput(item, npc)` | **`dealer.AddItemToInventory(item)`** — the game's own "give the dealer product" call |
| Capacity check | `GetInputCapacityForItem(item, npc, true)` | `dealer.GetTotalInventoryItemCount()` vs the config `DealerTopUpCap` (default **10**, matching the dealer's 10 hidden slots); also `dealer.overflowSlots` + `dealer.TryMoveOverflowItems()` |
| Stand point | `NavMeshUtility.GetReachableAccessPoint(dst, npc)` | Dealers move. Use `dealer.Home : Map.NPCEnterableBuilding` for the parking target, then `NavMeshUtility.SamplePosition(dealer.transform.position, out hit, maxDistance, areaMask)` for the walk target, gated on `employee.Movement.CanGetTo(dealer.transform.position, 2f)` |
| Locks | `ReserveInputSlotsForItem` / `RemoveSlotLocks` | None — no reservation API on `Dealer`. Do the transfer in one tick |
| Route storage | Vanilla `RouteListField` | Our sidecar `DealerRoute` list |
| Gate | — | `dealer.IsRecruited` must be true |

If the dealer is unreachable (inside a building, mid-deal, or in `DealerAttendDealBehaviour`), do not
fight it: `SubmitNoWorkReason("Couldn't find your dealer.", "Your driver will retry later.", 0)`,
park at the lot, and retry on the next tick window.

**Cash-collection leg** — the PC Gamer roadmap line *"collect cash and bring them to your businesses
to be laundered"* — maps to `dealer.CanCollectCash(out reason)` → `dealer.CollectCash()` →
`Business.StartLaunderingOperation(amount, minutesSinceStarted: 0)` clamped to
`business.LaunderCapacity - business.currentLaunderTotal`. **[UNVERIFIED]** whether `CollectCash()`
credits the local player's `MoneyManager` directly (which would make an NPC calling it wrong). Probe
P8. Ship this as a v2 feature behind a config flag, default off.

### 4.6 Driving — the exact call

```csharp
using Il2CppScheduleOne.Vehicles;
using Il2CppScheduleOne.Vehicles.AI;

var f = vehicle.Agent.Flags;                       // DriveFlags, mutate before Navigate
f.UseRoads                = true;
f.IgnoreTrafficLights     = false;
f.ObstacleMode            = DriveFlags.EObstacleMode.Default;
f.AutoBrakeAtDestination  = true;
f.TurnBasedSpeedReduction = true;
f.StuckDetection          = true;

var nav = new NavigationSettings {
    endAtRoad                        = true,
    ensureProximityToGraph           = true,
    teleportToGraphIfCalculationFails = true      // the anti-stuck escape hatch
};

// Snap the raw destination onto the drivable A* graph first.
var target = NavigationUtility.SampleVehicleGraph(lot.EntryPoint.position);

// Cache the delegate in a field — VehicleAgent+NavigationCallback is a MulticastDelegate wrapper and
// will be collected if only a temporary holds it. op_Implicit(Action<ENavigationResult>) exists.
_navCallback ??= (VehicleAgent.NavigationCallback)
                 (System.Action<VehicleAgent.ENavigationResult>)OnNavigationResult;

vehicle.Agent.Navigate(target, nav, _navCallback);
```

`ENavigationResult { Failed = 0, Complete = 1, Stopped = 2 }`. Treat `Stopped` as "we cancelled it"
and `Failed` as "escalate to recovery".

Two things not to do:
- **Do not read `Rb.velocity` for progress.** A vehicle beyond `LandVehicle.KINEMATIC_THRESHOLD_DISTANCE`
  from any player goes kinematic (`Agent.KinematicMode`, `Agent.UpdateKinematic(dt)`) — which is good
  (cheap, can't get physics-stuck) but the rigidbody is not driving. Read `vehicle.Speed_Kmh` and
  `Agent.TargetLocation` instead.
- **Do not poll for arrival.** Arrival is a callback, not a pollable enum. There is no
  `Agent.HasArrived`.

---

## 5. Vehicle handling

### 5.1 Which vehicle

**Player-owned, assigned per driver by `LandVehicle.GUID`.** Enumerate
`VehicleManager.Instance.PlayerOwnedVehicles`; the picker shows `VehicleName`, `VehicleCode`,
`Storage.SlotCount` and current parking lot. Persist `LandVehicle.GUID.ToString()` in the sidecar.

Why not always spawn a van: (a) `SpawnAndReturnVehicle` creates a networked object we then own for
save/load and cleanup; (b) a spawned vehicle inflates the player's net worth through
`LandVehicle.GetNetworth`; (c) the balance design deliberately anchors cargo capacity to the vehicle
you actually bought — Shitbox 5 slots / Dinkler 8 / **Veeper 16** — so a free van deletes a real
decision.

**Fallback (config `allow_spawned_vans`, default `false`).** If enabled and the driver has no assigned
vehicle, spawn one at a free spot in the home property's `LoadingDock.Parking` lot:

```csharp
var veh = VehicleManager.Instance.SpawnAndReturnVehicle(code, pos, rot, playerOwned: false);
```

Mark `SpawnedVehicle = true`, `Lifetime.OnDispose(() => veh.DestroyVehicle())`.
**Never hardcode `vehicleCode`.** No vehicle-code literals exist in the metadata; only `shitbox` (from
the `spawnvehicle` example usage) and `veeper` (from a real `OwnedVehicles.json`) are even
circumstantially provable. Enumerate `VehicleManager.Instance.VehiclePrefabs` at runtime and pick the
largest `Storage.SlotCount` whose `Agent != null`.

### 5.2 Capacity

`vehicle.Storage.SlotCount` (authored per prefab, plain writable int), clamped by the static
`StorageEntity.MAX_SLOTS`. Both values are **[UNVERIFIED]** in the dumps — read them at runtime,
never guess. Config `allow_trunk_expansion` defaults `false`; we do not enlarge trunks.

Departure threshold is expressed in **items**, matching Tyler's wording. Default =
`SlotCount × StackLimit / 2` computed from the route's item at assignment time (fall back to
`SlotCount` if the stack limit can't be read), clamp ≥ 1.

### 5.3 Parking

Priority order for the target lot at each end:

1. `LoadingDock.Parking : Map.ParkingLot` — the dock's own lot. **This is the best endpoint in the
   game for a driver**: `LoadingDock` is the only `ITransitEntity` that already carries a
   `LandVehicle`, a `ParkingLot` and a `Property`. Reach it via `Property.LoadingDocks` /
   `Property.LoadingDockCount`.
2. Nearest `ParkingLot` to the destination's `LinkOrigin` / `AccessPoints[0]`, from a cached
   `Object.FindObjectsOfType<Il2CppScheduleOne.Map.ParkingLot>()` sweep taken once per scene load.
3. If no lot is within `MaxParkingSearchRadius`, drive to `NavigationUtility.SampleVehicleGraph(
   accessPoint.position)` and stop there without parking. Ugly but functional.

Parking is not something the AI does — nothing in `Vehicles.AI` references parking at all.
`LandVehicle.Park` is an instantaneous geometric snap. The sequence is: navigate to
`lot.EntryPoint.position`, wait for `ENavigationResult.Complete`, then park yourself:

```csharp
int spot = lot.GetRandomFreeSpotIndex();
if (spot >= 0)
    vehicle.Park(null, new ParkData { lotGUID   = lot.GUID,
                                      spotIndex = spot,
                                      alignment = lot.ParkingSpots[spot].Alignment },
                 network: true);
```

Leaving: `vehicle.ExitPark(moveToExitPoint: true)` before the next `Navigate`.

### 5.4 Failure modes

| Failure | Detection | Response |
|---|---|---|
| **Vehicle destroyed** | `vehicle == null` (Unity null) on tick, or `vehicle.IsDestroyed`-equivalent | Abort trip, `RemoveSlotLocks`, `SubmitNoWorkReason("Your driver's vehicle is gone.", "Assign a new vehicle.", 5)`, walk/`Movement.Warp` the driver home, clear `VehicleGuid` |
| **Player is in the van** | `vehicle.CurrentPlayerOccupancy > 0` / `vehicle.DriverPlayer != null` / `vehicle.IsOccupied`, polled on tick | Refuse to start. Mid-trip: `Agent.StopNavigating()`, park if possible, `ExitVehicle()`, → `Recovering`. **Do not** plan around `LandVehicle.onPlayerEnter`/`onPlayerExit` — v0.4.1f6 **deleted** them. `OnLocalPlayerEnter`/`OnLocalPlayerExit` exist but are local-player-only, useless on a dedicated host |
| **Two drivers want one van** | `VehicleAssignment.TryClaim` — a simple `HashSet<string> claimedGuids` | Second driver waits, `SubmitNoWorkReason("Waiting for the van.", …)` |
| **Stuck** | `vehicle.Agent.GetIsStuck()` (backed by `StuckTimeThreshold` / `StuckDistanceThreshold` / `StuckSamples` / `PositionHistoryTracker`) | Escalate: (1) `Agent.StartReverse()`; (2) `Agent.Teleporter.MoveToRoadNetwork(resetRotation: true)` + `Agent.RecalculateNavigation()`; (3) `VehicleRecoveryPoint.GetClosestRecoveryPoint(pos)` + `vehicle.RecoverVehicle()` (gate on `CanBeRecovered()`); (4) abort |
| **Repeated pathing failure** | Count `ENavigationResult.Failed` like `VehiclePatrolBehaviour.consecutivePathingFailures` does | Give up after `Behaviour.MAX_CONSECUTIVE_PATHING_FAILURES` (read the static at runtime), submit a work issue, → `Idle`. Retry no sooner than the next in-game hour |
| **Blocked by traffic** | Not a failure. `Sensor` × 5 + `ObstructionDetector` + sweep tests handle it | Only escalate via the stuck detector |
| **Out of bounds** | `vehicle.UpdateOutOfBounds()` exists; also `Agent.IsOnVehicleGraph()` / `GetDistanceFromVehicleGraph()` | Same escalation ladder |

---

## 6. Edge cases

### 6.1 Sleep and time-skip

We do not own an `NPCAction`, so we never receive `JumpTo()`. Subscribe to `TimeManager` directly:

```csharp
// Il2Cpp.ActionList — has Add/Remove
TimeManager.Instance.onMinutePass.Add(_nativeMinutePass);

// Il2CppSystem.Action / Action<int> — no Add/Remove; use op_Addition/op_Subtraction with a CACHED
// managed delegate, or you can never unsubscribe. (research/API-PERSISTENCE-TIME.md §2.5)
var tm = TimeManager.Instance;
tm.onSleepStart = (Il2CppSystem.Action)(tm.onSleepStart + _nativeSleepStart);
tm.onTimeSkip   = (Il2CppSystem.Action<int>)(tm.onTimeSkip + _nativeTimeSkip);
```

Register the exact inverse with `Lifetime.OnDispose`. `TimeManager` is a `NetworkSingleton` and is
destroyed on scene unload, so re-subscribe from `OnSceneLoaded("Main")`, not from `OnEnabled`.

**On `onSleepStart` / `onTimeSkip(minutes)`: snap every in-flight trip to its end state.** This is
exactly what `NPCSignal_DriveToCarPark.JumpTo()` does, and if we don't, the player wakes up to a van
parked in the middle of a road with an NPC standing in traffic.

```
DriveToDestination / ParkAtDestination
  -> vehicle.SetTransform_Server(parkPos, parkRot) or vehicle.Park(null, parkData, true)
  -> employee.Movement.Warp(destinationAccessPoint.position)
  -> commit the pending cargo transfer immediately (CargoTransfer.TrunkToDestination)
  -> vehicle drives home is ALSO snapped: Park at the home lot, Warp the driver to the idle point
  -> state = Idle
LoadFromSource / UnloadToDestination
  -> finish the transfer instantly, release locks, state = Idle
```

Also honour the working day: the clock pauses 4:00 AM → 7:00 AM. Gate the tick on
`TimeManager.Instance.CurrentTime` being in `[700, 2359] ∪ [0, 400)` and on
`!TimeManager.Instance.IsSleepInProgress`. Use `TimeManager.IsGivenTimeWithinRange(givenTime, min,
max)` rather than hand-rolling clock arithmetic — the game's clock is a 24-hour *integer* (1530 =
15:30), not minutes.

### 6.2 Save / load mid-route

**Conservative resume, deliberately.** On `OnLoaded()`:

1. Re-resolve every `DriverRecord` → `Employee` by GUID from `EmployeeManager.Instance.AllEmployees`.
2. Re-attach `DriverBrain`, restore routes from the vanilla `PackagerConfiguration.Routes` and extras
   from the sidecar.
3. Re-resolve the vehicle by `LandVehicle.GUID` from `VehicleManager.Instance.AllVehicles`.
4. **Force state to `Idle`.** Do not try to resume a half-simulated drive. Cargo already in the trunk
   is a real, vanilla-persisted `StorageEntity`, so nothing is lost; the next tick re-plans and the
   driver simply finishes the delivery from wherever it is.
5. If a driver's employee, vehicle or route endpoint no longer resolves, drop that piece and log at
   `Log.Debug`. Never throw from `OnLoaded` — it runs inside the vanilla load path.

Version the blob (`DriverSaveData.Version`) and fail soft on a newer version: log, ignore, keep the
employees as plain Handlers.

### 6.3 Player interference

| Interference | Handling |
|---|---|
| Player takes the van | §5.4 |
| Player empties the source shelf | `GetItemReadyToMove()` → null; abort load, drive what's already aboard (if any), else → `Idle` |
| Player fills the destination | Partial deposit of what fits; remainder stays in the trunk; `SubmitNoWorkReason("Destination is full.", "Free up space at <name>.", 3)` |
| Player picks up the destination shelf | `ITransitEntity.IsDestroyed` → abort, `RemoveSlotLocks`, blank that route row. Mirrors the shipped Handler rule: *picking up and replacing a machine resets any route using it to "none"* |
| Player opens the trunk mid-load | `StorageEntity.IsOpened` / `CurrentPlayerAccessor != null`. Pause the transfer loop until closed; do not mutate slots a player has open |
| Player fires the driver | §6.5 |

### 6.4 Destination full / source empty

Covered above. The rule: **never destroy items and never drop them on the ground.** Anything that
can't be delivered stays in the trunk, which is a real container the player can open.

### 6.5 Driver fired mid-route

Postfix `Employee.Fire()` (virtual, server-side, runs before `ReceiveFire()`/`LeavePropertyAndDespawn()`):

```
if (!DriverRegistry.TryGet(__instance, out var brain)) return;
brain.AbortAndRelease();                 // StopNavigating, RemoveSlotLocks, park if possible
if (brain.OwnsVehicle) brain.Vehicle.DestroyVehicle();   // only if we spawned it
DriverRegistry.Unregister(__instance);
DriverSaveStore.Remove(__instance.GUID.ToString());
```

Cargo in the trunk stays in the trunk — it's the player's van. Vanilla handles the despawn.

### 6.6 Game paused / time scale

All timers must be in **game minutes**, never `Time.time`, or a driver will finish a 30-minute load
in 30 real seconds at 10× and in 30 real minutes at 1×. Drive the load/unload quanta from
`TimeManager.onMinutePass`; use `Time.deltaTime` only for the registry's tick throttle (which is a
polling rate, not a game mechanic). Respect `TimeManager.TimeSpeedMultiplier` implicitly by
never reading wall-clock time for anything mechanical.

### 6.7 Curfew

`Il2CppScheduleOne.Law.CurfewManager : NetworkSingleton<CurfewManager>` exposes
`IsEnabled` / `IsCurrentlyActive` / `IsHardCurfewActive` plus `UnityEvent`s
(`onCurfewStart`, `onCurfewHardStart`, `onCurfewEnd`, …).

**Design call: drivers are police-immune by default** (`driver_police_immune`, default `true`),
matching the shipped precedent that dealers cannot be arrested. A driver that gets arrested halfway
through a delivery is a support nightmare and breaks parity with the only comparable shipped NPC.

Config `respect_curfew` (default `false`) makes drivers park at the nearest lot at 21:00 and resume
at 05:00 for players who want the friction.

**[UNVERIFIED] and worth probing:** whether an NPC-driven civilian vehicle trips
`CheckpointBehaviour` / `RoadCheckpoint`, and whether
`CheckpointBehaviour.DoesVehicleContainIllicitItems()` inspects `LandVehicle.Storage` for a car with
no player in it. If it does, a driver hauling product past a checkpoint will get stopped. Probe P7.

---

## 7. Harmony patches

Rules for all of them: applied through the **injected per-module `Harmony` instance** only; target
resolution via `AccessTools.DeclaredMethod(typeof(X), "Name", argTypes)` so we hit the exact
override slot; wrapped in try/catch; **prefer Postfix**; every patch checks
`DriverRegistry.TryGet(__instance, …)` first so we never affect a vanilla Handler.

| # | Target | Kind | Priority | Purpose | S1API collision |
|---|---|---|---|---|---|
| 1 | `Il2CppScheduleOne.Employees.Packager.UpdateBehaviour()` | **Prefix**, returns `bool` | Normal (400) | Return `false` for registered drivers so the packaging/station brain never runs. This is the one place a skipping prefix is genuinely required — `UpdateBehaviour` is the per-tick work dispatcher and there is no other way to silence it | **None.** S1API patches `NPCs.NPC.*`, `Economy.*`, `PlayerScripts.*` — nothing in `Employees.*` |
| 2 | `Packager.ShouldIdle()` | Postfix | 400 | Force `__result = false` while a trip is running, so nothing calls `SetIdle(true)` under us | None |
| 3 | `Packager.IsAnyWorkInProgress()` | Postfix | 400 | Force `__result = true` while a trip is running, so vanilla treats the driver as busy | None |
| 4 | `Il2CppScheduleOne.Employees.Employee.Fire()` | Postfix | 400 | Tear the driver down cleanly (§6.5) | None |
| 5 | `Employee.OnSleepEnd()` | Postfix | 400 | Reset per-day trip counters and the departure timeout clocks | None |
| 6 | `Il2CppScheduleOne.Management.PackagerConfiguration.IsStationValid(BuildableItem, out string)` | Postfix | 400 | `__result = false`, `reason = "Drivers don't run stations."` on driver configs, so the clipboard's Stations row can't be misused | None |
| 7 | `Il2CppScheduleOne.Management.ManagementInterface.GetConfigPanelPrefab(EConfigurableType)` | Postfix | 400 | **Phase 2.** Return the cloned driver panel when the selected `IConfigurable` is a driver | None |
| 8 | `Il2CppScheduleOne.UI.Management.RouteEntryUI.ObjectValid(ITransitEntity, out string)` | Postfix | 400 | **Phase 2.** Allow cross-property endpoints for driver routes (vanilla restricts to the local property; `TransitEntitySelector.SELECTION_RANGE` also applies) | None |
| 9 | `Il2CppScheduleOne.Console+AddEmployeeCommand.Execute(List<string>)` | Postfix | 400 | **Optional.** Accept `addemployee driver <propertyCode>` for testing. Note the type-token parse is **[UNVERIFIED]** — probe before relying on it | None |

**Explicitly NOT patched** (collision map, `research/API-S1API.md` §23):

| Method | Why we stay off it |
|---|---|
| `NPCs.NPC.Awake` | S1API `NPCPatches` Prefix at **priority 800** |
| `NPCs.NPC.GetSaveData`, `NPC.ShouldSave` | S1API `NPCPatches` — **skipping** prefixes |
| `NPCs.NPCInventory.Awake` | S1API Prefix(800) **and** Postfix(0) |
| `NPCs.NPCManager.GetNPC`, `GetSaveString` | S1API `NPCPatches` |
| `Economy.Dealer.Awake`, `Dealer.Load` | S1API Prefix(800), skipping |
| `Persistence.SaveManager.Save(string)` | Already three S1API postfixes; use `S1API.GameLifecycle.OnSaveComplete` |
| `Persistence.LoadManager.QueueLoadRequest` | Already two S1API prefixes; use `OnLoadComplete` |
| `Persistence.Loaders.NPCLoader.Load`, `NPCsLoader.Load` | S1API Prefix(800) + Postfix ×2 |
| `MoveItemBehaviour.IsTransitRouteValid` / `CanGetToSource` / `CanGetToDestination` | Not an S1API collision — we simply don't need them (D4) |

Harmony's default priority is `Priority.Normal` (400); S1API's `HarmonyPriority(800)` prefixes beat
us and its `HarmonyPriority(0)` postfixes run last. Since every patch above is on a method S1API
doesn't touch, ordering is a non-issue — but if that changes after an S1API update, add an explicit
`[HarmonyPriority]` rather than hoping.

**FishNet hash suffixes:** if patch 9's target or any `RpcLogic___*` becomes necessary, resolve it
**by name prefix at runtime** (`m.Name.StartsWith("RpcLogic___CreateEmployee_")`), never by the
hard-coded hash — the hashes are derived from the method signature and change when a parameter does.

---

## 8. Config

Category `HireableDrivers_01_Main` (from `ExpansionConfig.CategoryForId("hireable_drivers")`), file
`UserData/Expansions.cfg`. All bound in `OnRegistered()`, not `OnEnabled()`.

```csharp
public sealed class DriverSettings
{
    public readonly ConfigValue<float>  SigningFee;
    public readonly ConfigValue<float>  SigningFeePerEmployee;
    public readonly ConfigValue<float>  DailyWage;
    public readonly ConfigValue<int>    MaxDriversPerProperty;
    public readonly ConfigValue<int>    MaxRoutesPerDriver;
    public readonly ConfigValue<int>    DefaultDepartThresholdPercent;
    public readonly ConfigValue<int>    DepartAfterGameHours;
    public readonly ConfigValue<int>    LoadMinutesPerStop;
    public readonly ConfigValue<int>    DealerTopUpCap;
    public readonly ConfigValue<bool>   AllowDealerDestinations;
    public readonly ConfigValue<bool>   AllowCashCollection;
    public readonly ConfigValue<bool>   AllowSpawnedVans;
    public readonly ConfigValue<string> SpawnedVanCode;
    public readonly ConfigValue<bool>   AllowTrunkExpansion;
    public readonly ConfigValue<bool>   RespectCurfew;
    public readonly ConfigValue<bool>   DriverPoliceImmune;
    public readonly ConfigValue<float>  TickIntervalSeconds;
    public readonly ConfigValue<float>  MaxParkingSearchRadius;
    public readonly ConfigValue<bool>   DebugLogging;
    public readonly ConfigValue<bool>   DebugDrawPaths;

    public DriverSettings(ModuleConfig cfg)
    {
        SigningFee            = cfg.Bind("signing_fee", 1500f, "Signing fee",
                                         "One-off hire cost. Vanilla Handler is 1000, Chemist 2000.");
        SigningFeePerEmployee = cfg.Bind("signing_fee_per_employee", 100f, "Signing fee escalation",
                                         "Added per employee you already have. Matches the shipped Handler rule.");
        DailyWage             = cfg.Bind("daily_wage", 250f, "Daily wage",
                                         "Drawn from the driver's bed locker like any employee.");
        MaxDriversPerProperty = cfg.Bind("max_drivers_per_property", 2, "Max drivers per property");
        MaxRoutesPerDriver    = cfg.Bind("max_routes_per_driver", 5, "Routes per driver",
                                         "5 = parity with the shipped Handler. Clamped by RouteListField.MaxRoutes.");
        DefaultDepartThresholdPercent
                              = cfg.Bind("default_depart_threshold_percent", 50, "Default departure threshold (%)",
                                         "Percent of trunk capacity that must be loaded before the van leaves.");
        DepartAfterGameHours  = cfg.Bind("depart_after_game_hours", 4, "Depart anyway after (in-game hours)",
                                         "Stops a slow source deadlocking a route. 0 = never.");
        LoadMinutesPerStop    = cfg.Bind("load_minutes_per_stop", 30, "Load/unload time (in-game minutes)",
                                         "30 matches the shipped supplier logistics quantum.");
        DealerTopUpCap        = cfg.Bind("dealer_topup_cap", 10, "Dealer top-up cap",
                                         "Items delivered per dealer visit. Dealers have 10 inventory slots.");
        AllowDealerDestinations = cfg.Bind("allow_dealer_destinations", true, "Allow dealer destinations");
        AllowCashCollection   = cfg.Bind("allow_cash_collection", false, "Allow dealer cash -> business laundering",
                                         "Experimental; see the plan's risk register.");
        AllowSpawnedVans      = cfg.Bind("allow_spawned_vans", false, "Spawn a van if none is assigned",
                                         "Off by default so drivers use vehicles you actually bought.");
        SpawnedVanCode        = cfg.Bind("spawned_van_code", "", "Spawned van vehicle code",
                                         "Empty = pick the largest-trunk prefab that has a VehicleAgent.");
        AllowTrunkExpansion   = cfg.Bind("allow_trunk_expansion", false, "Let drivers use expanded trunks");
        RespectCurfew         = cfg.Bind("respect_curfew", false, "Drivers stop during curfew");
        DriverPoliceImmune    = cfg.Bind("driver_police_immune", true, "Drivers are police-immune",
                                         "Parity with the shipped rule that dealers cannot be arrested.");
        TickIntervalSeconds   = cfg.Bind("tick_interval_seconds", 1.0f, "State machine tick interval");
        MaxParkingSearchRadius= cfg.Bind("max_parking_search_radius", 60f, "Parking search radius (m)");
        DebugLogging          = cfg.Bind("debug_logging", false, "Verbose logging");
        DebugDrawPaths        = cfg.Bind("debug_draw_paths", false, "Draw vehicle paths",
                                         "Uses NavigationUtility.DrawPath.");
    }
}
```

---

## 9. Risks and unknowns

Everything this plan depends on that the docs mark UNVERIFIED, with the exact way to settle it.
All probes are read-only unless stated, and all belong in `Diagnostics/DriverProbe.cs` behind an
S1API console command (`S1API.Console.BaseConsoleCommand` — do **not** try to subclass the game's
`Console+ConsoleCommand`, it needs `ClassInjector` and IL2CPP virtual overrides).

| # | Unknown | Impact if wrong | Runtime verification |
|---|---|---|---|
| **P1** | **Do civilian vehicle prefabs actually carry a `VehicleAgent`?** `LandVehicle.Agent` is inspector-wired and only police consumers are visible in the dumps | **Fatal to the whole road leg.** You cannot add a `VehicleAgent` at runtime — it needs two `Seeker`s, five `Sensor`s, four sweep origins, axle transforms, two `Wheel`s and a `VehicleTeleporter`, all inspector-wired | `drv probe vehicles`: `foreach (var p in VehicleManager.Instance.VehiclePrefabs) Log($"{p.VehicleCode,-12} agent={(p.Agent != null)} seats={p.Seats?.Length} trunk={p.Storage?.SlotCount} price={p.VehiclePrice}")` — and repeat over `AllVehicles` for live instances, since a prefab and a spawned instance can differ |
| **P2** | **Is `NPC.EnterVehicle(null, veh)` a legal null-connection call?** The param is a FishNet `NetworkConnection` on an ObserversRpc/TargetRpc pair; `null` normally means broadcast but the game may branch | Driver never boards; whole loop stalls at `BoardVehicle` | `drv probe board`: on a listen server, take a hired Handler, call `emp.EnterVehicle(null, veh)`, then assert `emp.IsInVehicle`, `emp.CurrentVehicle == veh`, and `veh.OccupantNPCs.Length` incremented. Log all three before and after. If null fails, retry with `InstanceFinder.ClientManager.Connection` |
| **P3** | **Does `VehicleAgent.Navigate` actually drive a non-police car with no player nearby?** Kinematic mode kicks in past `KINEMATIC_THRESHOLD_DISTANCE` | Trips complete instantly, never, or the car teleports | `drv drive <lotName>`: pick a parked player-owned car, `ExitPark`, set `DriveFlags`, `Navigate(SampleVehicleGraph(lot.EntryPoint.position), nav, cb)`. Log at 1 Hz: `Agent.AutoDriving`, `Agent.KinematicMode`, `veh.Speed_Kmh`, `Vector3.Distance(veh.transform.position, Agent.TargetLocation)`, `Agent.GetIsStuck()`. Log the callback's `ENavigationResult`. **This is milestone 1 for exactly this reason.** |
| **P4** | **Does `NavMeshUtility.GetReachableAccessPoint(entity, npc)` return non-null cross-property?** | If **yes**, walking drivers work already and the vehicle becomes an optimisation. If **no**, the vehicle leg is mandatory | `drv probe reach`: for each pair of owned properties, take an `ITransitEntity` from each and log `NavMeshUtility.GetReachableAccessPoint(e, driverNpc) != null` plus `driverNpc.Movement.CanGetTo(e, 1f)`. A 10-minute test that reshapes the mod |
| **P5** | **`Employee.SigningFee` / `DailyWage` real values** — Unity-serialized prefab floats, absent from metadata | Balance table is guesswork | `drv probe wages`: `var p = EmployeeManager.Instance.PackagerPrefab; Log($"{p.SigningFee} {p.DailyWage}")` for all four prefabs |
| **P6** | **`StorageEntity.MAX_SLOTS`, per-vehicle `Storage.SlotCount`, `RouteListField.MaxRoutes`, `Behaviour.MAX_CONSECUTIVE_PATHING_FAILURES`, `TimeManager.EndOfDay`/`WakeTime`** — all runtime statics | Capacity and retry logic mis-tuned | `drv probe consts`: print each static once at load. Never hardcode |
| **P7** | **Does an NPC-driven car trip `CheckpointBehaviour` / police?** And does `DoesVehicleContainIllicitItems()` inspect a driverless-looking car's `Storage`? | A hauling driver gets stopped every trip; the police-immunity design collapses | `drv probe checkpoint`: drive a loaded van through an active `RoadCheckpoint` and watch for `CheckpointBehaviour` activating and for a `Crime`/pursuit event on `PlayerCrimeData` |
| **P8** | **Does `Dealer.CollectCash()` credit the local player's `MoneyManager` directly?** | A driver "collecting" cash would silently pay the player instead of carrying it. Would kill the cash-collection leg | `drv probe dealercash`: read `dealer.Cash` and `MoneyManager` online/cash balance before and after calling `CanCollectCash` + `CollectCash` on the host. Keep `allow_cash_collection` default `false` until settled |
| **P9** | **`Il2CppScheduleOne.DevUtilities.Singleton<T>.Instance` / `NetworkSingleton<T>.Instance` accessor names** — never dumped | Compile-time failure, caught immediately | Trivially settled at build time. Prefer `<T>.InstanceExists` guards before `.Instance` everywhere |
| **P10** | **Is `SurfaceStorageEntity` an `ITransitEntity`?** It declares `Selectable` and `IsAcceptingItems` but not `InputSlots`/`OutputSlots`/`LinkOrigin`/`AccessPoints` | A route endpoint the UI offers but the loop can't use | `drv probe transit`: for every `MonoBehaviour` in the owned properties' `BuildableItems`, log `TryCast<ITransitEntity>() != null && .InputSlots != null && .AccessPoints != null`. Treat anything failing all three as not selectable |
| **P11** | **`addemployee` type-token parsing** — case-insensitive `Enum.TryParse` vs hand-written switch | Only affects the optional debug command | `drv probe addemployee`: run `addemployee handler barn`, `addemployee Handler barn`, `addemployee packager barn` and diff `EmployeeManager.Instance.AllEmployees.Count` |
| **P12** | **Whether an `AdvancedTransitRoute` with cross-property endpoints survives the vanilla save/load round trip** (`AdvancedTransitRouteData` stores GUIDs, which should be property-agnostic — but `PackagerConfiguration` is saved under the employee's property) | Routes silently blank on reload | `drv probe roundtrip`: build a cross-property route into `packager.configuration.Routes`, `Saveable.RequestGameSave(immediate: true)`, reload, and re-read `Routes.Routes`. If it fails, mirror routes into our sidecar and restore them in `OnLoaded` |

Two structural risks with no probe, just discipline:

- **FishNet RPC hash suffixes change with any signature change.** Resolve by name prefix, never by
  number.
- **Interop delegates get collected.** Cache every `Il2CppSystem.Action`, `NavigationCallback` and
  `Action<WalkResult>` in a field for the lifetime of its subscription, and unsubscribe with the same
  managed instance. Assigning (`npc.onEnterVehicle = handler`) instead of adding wipes every other
  subscriber.

---

## 10. Implementation order

Each milestone is independently testable and each one is useful even if the next never lands. The
sequence deliberately front-loads the two things that can kill the mod (P1/P3, then P2).

### M0 — Probe harness *(no feature, no world mutation)*
`Diagnostics/DriverProbe.cs` + an S1API console command `drv`. Implements probes P1, P4, P5, P6, P10,
P11. Prints the vehicle prefab table (code / agent / seats / trunk / price), every `ParkingLot` and
its free spots, every `LoadingDock` and its `Parking` lot, owned properties and businesses with their
codes, all `ITransitEntity` casts, employee prefab fees and wages, and every relevant runtime static.
**Done when:** you can paste the probe output into this document and delete four rows from §9.

### M1 — Drive an empty car across town *(the risky part, first)*
`drv drive <lotName>`. Picks the nearest player-owned vehicle, `ExitPark`, sets `DriveFlags`,
`SampleVehicleGraph`, `Navigate`, logs telemetry at 1 Hz, and on `Complete` calls `Park`.
No NPC, no cargo, no state machine. Settles **P3**, the single biggest risk in the mod.
**Done when:** a car drives from the barn's lot to the docks' lot on real roads, stops at a light,
parks in a free spot, and the callback reports `Complete`.

### M2 — Put an NPC in the car and repeat
`drv ferry <employeeName> <lotName>`. Walk the employee to `veh.driverEntryPoint` with the callback
overload, `EnterVehicle(null, veh)`, `StartVehicle()`, drive, `Park`, `ExitVehicle()`,
`StopVehicle()`. Settles **P2**.
**Done when:** a hired Handler walks to a car, gets in, drives to another lot, parks, and gets out —
and looks right doing it (check `NPCMovement.SetSeat` / the sitting pose; if the NPC is standing
inside the car, that's a cosmetic follow-up, not a blocker).

### M3 — One hard-coded cargo run
`drv haul <srcGuid> <dstGuid> <itemId> <amount>`. Implements `CargoTransfer` with the lock-safe
recipes: source → trunk, drive, trunk → destination, with `ReserveInputSlotsForItem` /
`RemoveSlotLocks` in a `finally`. Still hand-driven from the console.
**Done when:** 20 units of OG Kush move from a barn shelf to a bungalow shelf via a real drive, the
counts are exactly right on both ends, and no slot is left locked.

### M4 — `DriverBrain` + the state machine + suppression patches
`DriverRegistry`, `DriverBrain`, `EDriverState`, patches 1–3. Routes in memory only, set from the
console. This is where the feature becomes autonomous.
**Done when:** you register a driver, give it one route, and it loops forever without intervention;
and disabling the module via config cleanly returns it to being an ordinary Handler.

### M5 — Hiring, wages, bed, work issues
`DriverHiring`: `CreateEmployee_Server` wrapper, fee/wage override, avatar re-skin, config `Name`.
Patches 4–6. `SubmitNoWorkReason` on every abort path.
**Done when:** you can hire a driver from the console, it demands a bed, it refuses to work unpaid,
the clipboard shows a sensible reason when it's stuck, and firing it tears everything down.

### M6 — Persistence
`DriverSaveStore` (S1API `Saveable`) + `DriverSaveData`. Reattach on `OnLoaded`, force `Idle`.
Settles **P12**.
**Done when:** save mid-trip, quit to menu, reload — the driver is still a driver, still has its
routes and vehicle, and finishes the delivery.

### M7 — Time integration
`DriverClock`: `onMinutePass` pacing for load/unload, `onSleepStart` / `onTimeSkip` snap-resolve, the
07:00–04:00 window, curfew handling. Settles **P7**.
**Done when:** you sleep mid-delivery and wake to a van parked at the destination with the cargo
delivered — not a van in the middle of a road.

### M8 — Dealer destinations
`TransitEndpoint` union, `DealerRoute` sidecar, `dealer.AddItemToInventory`, reachability gating,
top-up cap. Settles **P8** (cash leg stays off).
**Done when:** a driver tops up Benji with 10 jars and Benji sells them.

### M9 — UI phase 1 (IMGUI)
`UI/DriverRouteMenu.cs` in `ExpansionMenu` (F7). Roster, route rows, vehicle picker, threshold,
hire/fire, live state. Absolute `GUI.Button`/`GUI.Label` only.
**Done when:** the console is no longer required for anything.

### M10 — UI phase 2 (native clipboard)
Patches 7–8: cloned `PackagerConfigPanel` with `StationsUI` hidden, cross-property
`RouteEntryUI.ObjectValid`, plus vehicle and threshold rows cloned from existing widgets.
**Done when:** a driver's clipboard reads as a shipped feature and the player never sees our IMGUI.

### M11 — Polish
Cash-collection leg behind its flag; the spawned-van fallback; `DriveFlags` tuning per vehicle;
`TransitLineVisuals` for driver routes; `Employee.WaitOutside.IdlePoint` next to the parked van;
multiplayer smoke test with one client watching.

---

## Appendix — API quick reference for the implementer

Everything the loop touches, in one place. All verbatim from the dumps.

```csharp
// ── Employees ────────────────────────────────────────────────────────────────────────────────
Il2CppScheduleOne.Employees.EmployeeManager : NetworkSingleton<EmployeeManager>
  Employee CreateEmployee_Server(Property, EEmployeeType, string firstName, string lastName,
                                 string id, bool male, int appearanceIndex,
                                 Vector3 position, Quaternion rotation, string guid);
  Employee GetEmployeePrefab(EEmployeeType);   List<Employee> GetEmployeesByType(EEmployeeType);
  void GenerateRandomName(bool male, out string first, out string last);
  void GetRandomAppearance(bool male, out int index, out AvatarSettings settings);
  Botanist BotanistPrefab; Packager PackagerPrefab; Chemist ChemistPrefab; Cleaner CleanerPrefab;
  List<Employee> AllEmployees;
enum EEmployeeType { Botanist = 0, Handler = 1, Chemist = 2, Cleaner = 3 }   // Handler == Packager

Il2CppScheduleOne.Employees.Employee : Il2CppScheduleOne.NPCs.NPC
  Property AssignedProperty; float SigningFee, DailyWage; bool PaidForToday /*SyncVar*/, Fired;
  MoveItemBehaviour MoveItemBehaviour;   IdleBehaviour WaitOutside;
  bool CanWork(); void MarkIsWorking(); virtual void UpdateBehaviour(); virtual bool ShouldIdle();
  virtual void SetIdle(bool); void SetWaitOutside(bool); virtual bool IsAnyWorkInProgress();
  void SubmitNoWorkReason(string reason, string fix, int priority = 0);
  virtual EmployeeHome GetHome(); bool IsPayAvailable(); void SetIsPaid(); void RemoveDailyWage();
  virtual void AssignProperty(Property, bool warp); virtual void Fire(); void OnSleepEnd();
  void SetDestination(ITransitEntity, bool teleportIfFail = true);
  void SetDestination(Vector3, bool teleportIfFail = true);

Il2CppScheduleOne.Employees.Packager : Employee
  PackagerConfiguration configuration;  int MaxAssignedStations;
  AdvancedTransitRoute GetTransitRouteReady(out ItemInstance item);
  override void UpdateBehaviour(); override bool IsAnyWorkInProgress(); override bool ShouldIdle();

Il2CppScheduleOne.Employees.EmployeeHome : MonoBehaviour
  Employee AssignedEmployee; StorageEntity Storage;
  void SetAssignedEmployee(Employee); float GetCashSum(); void RemoveCash(float);

// ── Routes and transit ───────────────────────────────────────────────────────────────────────
Il2CppScheduleOne.Management.ITransitEntity          // emitted as a CLASS — use TryCast<T>()
  string Name; List<ItemSlot> InputSlots, OutputSlots; Transform LinkOrigin;
  Il2CppReferenceArray<Transform> AccessPoints; bool Selectable, IsAcceptingItems, IsDestroyed;
  Il2CppSystem.Guid GUID;
  ItemSlot GetFirstSlotContainingItem(string id, ESlotType);
  ItemSlot GetFirstSlotContainingTemplateItem(ItemInstance, ESlotType);
  int  GetInputCapacityForItem(ItemInstance, NPC asker = null, bool checkPlayerFilters = true);
  int  GetOutputCapacityForItem(ItemInstance, NPC asker = null);
  void InsertItemIntoInput(ItemInstance, NPC inserter = null);
  List<ItemSlot> ReserveInputSlotsForItem(ItemInstance, NetworkObject locker);
  void RemoveSlotLocks(NetworkObject locker);
  enum ESlotType { Input = 0, Output = 1, Both = 2 }

Il2CppScheduleOne.Management.AdvancedTransitRoute : TransitRoute
  .ctor(ITransitEntity source, ITransitEntity destination); .ctor(AdvancedTransitRouteData);
  ManagementItemFilter Filter; ItemInstance GetItemReadyToMove(); AdvancedTransitRouteData GetData();
Il2CppScheduleOne.Management.RouteListField : ConfigField
  List<AdvancedTransitRoute> Routes; int MaxRoutes;
  void AddItem(AdvancedTransitRoute); void RemoveItem(...); void SetList(list, bool network, bool bypass = false);
  void Replicate(); RouteListData GetData(); void Load(RouteListData);
Il2CppScheduleOne.Management.PackagerConfiguration : EntityConfiguration
  ObjectField Home; ObjectListField Stations; RouteListField Routes;
  bool IsStationValid(BuildableItem, out string reason);

Il2CppScheduleOne.ItemFramework.ItemSlot
  ItemInstance ItemInstance; int SlotIndex, Quantity; bool IsAtCapacity, IsLocked, IsRemovalLocked;
  void ChangeQuantity(int change, bool _internal = false); void SetQuantity(int, bool _internal = false);
  virtual void AddItem(ItemInstance, bool _internal = false); virtual void ClearStoredInstance(bool _internal = false);
  virtual int GetCapacityForItem(ItemInstance, bool checkPlayerFilters = false);
Il2CppScheduleOne.ItemFramework.ItemInstance
  ItemDefinition Definition; virtual ItemInstance GetCopy(int overrideQuantity = -1);
  virtual bool CanStackWith(ItemInstance other, bool checkQuantities = true);

// ── Vehicles ─────────────────────────────────────────────────────────────────────────────────
Il2CppScheduleOne.Vehicles.VehicleManager : NetworkSingleton<VehicleManager>
  List<LandVehicle> AllVehicles, VehiclePrefabs, PlayerOwnedVehicles;
  LandVehicle GetVehiclePrefab(string code);
  LandVehicle SpawnAndReturnVehicle(string code, Vector3, Quaternion, bool playerOwned);
Il2CppScheduleOne.Vehicles.LandVehicle : NetworkBehaviour
  string VehicleName, VehicleCode; Il2CppSystem.Guid GUID; bool IsPlayerOwned, isParked, IsOccupied;
  VehicleAgent Agent; StorageEntity Storage; StorageDoorAnimation Trunk;
  Il2CppReferenceArray<VehicleSeat> Seats; Il2CppReferenceArray<NPC> OccupantNPCs;
  int Capacity, CurrentPlayerOccupancy; Player DriverPlayer; Transform driverEntryPoint;
  ParkingLot CurrentParkingLot; ParkingSpot CurrentParkingSpot; ParkData CurrentParkData;
  float Speed_Kmh; static float KINEMATIC_THRESHOLD_DISTANCE;
  void StartVehicle(); void StopVehicle(); void AddNPCOccupant(NPC); void RemoveNPCOccupant(NPC);
  void Park(NetworkConnection conn, ParkData, bool network); void ExitPark(bool moveToExitPoint = true);
  void SetTransform_Server(Vector3, Quaternion); void TeleportToNavMesh(bool resetVelocity);
  virtual void RecoverVehicle(); virtual bool CanBeRecovered(); void DestroyVehicle();
  List<ItemInstance> GetContents();
Il2CppScheduleOne.Vehicles.AI.VehicleAgent : MonoBehaviour
  void Navigate(Vector3 location, NavigationSettings settings = null, NavigationCallback cb = null);
  void RecalculateNavigation(); void StopNavigating(); void EndDriving();
  bool AutoDriving, KinematicMode; Vector3 TargetLocation; DriveFlags Flags; VehicleTeleporter Teleporter;
  bool GetIsStuck(); void StartReverse(); bool IsOnVehicleGraph();
  enum ENavigationResult { Failed = 0, Complete = 1, Stopped = 2 }
Il2CppScheduleOne.Vehicles.AI.NavigationUtility
  static Vector3 SampleVehicleGraph(Vector3 destination);
  static void DrawPath(PathGroup group, float duration = 10f);
Il2CppScheduleOne.Map.ParkingLot : MonoBehaviour
  Il2CppSystem.Guid GUID; List<ParkingSpot> ParkingSpots; Transform EntryPoint, ExitPoint;
  List<ParkingSpot> GetFreeParkingSpots(); int GetRandomFreeSpotIndex();
Il2CppScheduleOne.Vehicles.ParkData { Guid lotGUID; int spotIndex; EParkingAlignment alignment; }

// ── Storage ──────────────────────────────────────────────────────────────────────────────────
Il2CppScheduleOne.Storage.StorageEntity : NetworkBehaviour
  static int MAX_SLOTS; int SlotCount, ItemCount; List<ItemSlot> ItemSlots;
  bool IsOpened; Player CurrentPlayerAccessor;
  void InsertItem(ItemInstance, bool network = true); bool CanItemFit(ItemInstance, int quantity = 1);
  int HowManyCanFit(ItemInstance); List<ItemInstance> GetAllItems(); virtual void ContentsChanged();

// ── NPC movement / navmesh ───────────────────────────────────────────────────────────────────
Il2CppScheduleOne.NPCs.NPC : NetworkBehaviour
  virtual void EnterVehicle(NetworkConnection, LandVehicle); virtual void ExitVehicle();
  LandVehicle CurrentVehicle; bool IsInVehicle;
  Il2CppSystem.Action<LandVehicle> onEnterVehicle, onExitVehicle;
Il2CppScheduleOne.NPCs.NPCMovement
  void SetDestination(Vector3 pos, Il2CppSystem.Action<WalkResult> cb = null,
                      bool interruptExistingCallback = true, float successThreshold = 1, float cacheMaxDistSqr = 1);
  void SetDestination(ITransitEntity entity);
  bool CanGetTo(ITransitEntity, float proximityReq = 1); bool CanGetTo(Vector3, float proximityReq = 1);
  void Warp(Vector3); void WarpToNavMesh(); void Stop(); float MoveSpeedMultiplier; bool IsMoving;
  enum WalkResult { Failed = 0, Interrupted = 1, Stopped = 2, Partial = 3, Success = 4 }
Il2CppScheduleOne.DevUtilities.NavMeshUtility
  static Transform GetReachableAccessPoint(ITransitEntity, NPC);
  static bool IsAtTransitEntity(ITransitEntity, NPC, float distanceThreshold = 0.4f);
  static bool SamplePosition(Vector3, out NavMeshHit, float maxDistance, int areaMask, bool useCache = true);

// ── Dealers, properties, time ────────────────────────────────────────────────────────────────
Il2CppScheduleOne.Economy.Dealer : NPC
  bool IsRecruited; List<ItemSlot> ItemSlots; Il2CppReferenceArray<ItemSlot> overflowSlots;
  float Cash; Map.NPCEnterableBuilding Home;  static List<Dealer> AllPlayerDealers;
  void AddItemToInventory(ItemInstance); int GetTotalInventoryItemCount(); void TryMoveOverflowItems();
  bool CanCollectCash(out string reason); void CollectCash();
Il2CppScheduleOne.Property.Property : NetworkBehaviour
  static List<Property> Properties, OwnedProperties, UnownedProperties;
  string PropertyCode, PropertyName; bool IsOwned; int EmployeeCapacity;
  List<Employee> Employees; Il2CppReferenceArray<Transform> EmployeeIdlePoints; Transform NPCSpawnPoint;
  Il2CppReferenceArray<Delivery.LoadingDock> LoadingDocks; int LoadingDockCount;
  List<Bed> GetUnassignedBeds(); List<T> GetBuildablesOfType<T>();
  int RegisterEmployee(Employee); void DeregisterEmployee(Employee);
Il2CppScheduleOne.Property.Business : Property
  static List<Business> Businesses, OwnedBusinesses;
  float LaunderCapacity, currentLaunderTotal; void StartLaunderingOperation(float amount, int minutesSinceStarted = 0);
Il2CppScheduleOne.Delivery.LoadingDock : MonoBehaviour     // full ITransitEntity
  LandVehicle DynamicOccupant, StaticOccupant; Property ParentProperty; Map.ParkingLot Parking;
  bool IsInUse; void SetOccupant(LandVehicle); void SetStaticOccupant(LandVehicle);
Il2CppScheduleOne.GameTime.TimeManager : NetworkSingleton<TimeManager>
  int CurrentTime, ElapsedDays; bool IsSleepInProgress, IsNight; float TimeSpeedMultiplier;
  static int EndOfDay, WakeTime; static float MinuteDuration;
  static bool IsGivenTimeWithinRange(int given, int min, int max);
  Il2Cpp.ActionList onMinutePass, onUncappedMinutePass, onTick;      // .Add / .Remove
  Il2CppSystem.Action onSleepStart, onSleepEnd, onHourPass, onDayPass;   // op_Addition / op_Subtraction
  Il2CppSystem.Action<int> onTimeSkip;
Il2CppScheduleOne.Law.CurfewManager : NetworkSingleton<CurfewManager>
  bool IsEnabled, IsCurrentlyActive, IsHardCurfewActive;
  UnityEvent onCurfewStart, onCurfewHardStart, onCurfewEnd;
```

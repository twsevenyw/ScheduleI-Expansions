# Feasibility & implementation strategy — the three planned mods

Read `IL2CPP-NOTES.md` first; it establishes the constraints that shape every decision here.

Verdict up front:

| Mod | Verdict | Hardest part |
|---|---|---|
| **Hireable Drivers** | **Feasible.** ~80% of the machinery already exists, driving included. | Writing the one missing behaviour that says "drive to point B once", and confirming civilian vehicle prefabs actually carry a `VehicleAgent`. |
| **Police Improvements** | **Feasible, lowest risk of the three.** | Reading tuning constants that may be `const`-inlined; no jail system exists to extend. |
| **Special Customers** | **Feasible with one real unknown.** | Whether a `Customer` component added *after* `NetworkObject` spawn replicates in co-op. |

Four findings apply to all three and are non-negotiable:

1. **Do not create new NPC prefabs, and do not subclass game types.** FishNet only spawns prefabs with a
   pre-registered, positionally-assigned prefabId, and an Il2CppInterop-injected subclass of a
   `NetworkBehaviour` has none of the build-time-generated RPC/SyncVar plumbing. See `IL2CPP-NOTES.md` §8
   and §13. Reuse registered prefabs and pooled instances; attach injected plain `MonoBehaviour`s alongside.
2. **Custom NPC identity is data, not a type.** Both settings classes derive from
   `UnityEngine.ScriptableObject`, so a mod can author a look at runtime:

```csharp
// Il2CppScheduleOne.AvatarFramework.Customization.BasicAvatarSettings : UnityEngine.ScriptableObject
//   ~31 semantic fields (Gender, Weight, SkinColor, HairStyle, Mouth, FacialHair,
//   Top/Bottom/Shoes/Headwear/Eyewear + colours, Tattoos, eyebrow/eyelid/pupil tuning)
public Il2CppScheduleOne.AvatarFramework.AvatarSettings GetAvatarSettings();

// Il2CppScheduleOne.AvatarFramework.AvatarSettings : UnityEngine.ScriptableObject   (~72 raw fields)
// Il2CppScheduleOne.AvatarFramework.Avatar : UnityEngine.MonoBehaviour
public void LoadAvatarSettings(Il2CppScheduleOne.AvatarFramework.AvatarSettings settings);
public void ApplySettings(Il2CppScheduleOne.AvatarFramework.AvatarSettings settings);
```

   **Author against `BasicAvatarSettings` (31 semantic fields), not `AvatarSettings` (72 raw fields)**, then
   `GetAvatarSettings()` → `Avatar.LoadAvatarSettings(...)`. Note `BasicAvatarSettings` lives in the
   `.Customization` sub-namespace, and there is **no** `ApplyBasicSettings` method. Slot budget is 6 face
   layers / 8 body layers / 9 accessories. The game does exactly this for cartel goons and employees.

3. **You cannot implement the game's `ISaveable`, so use S1API for per-save data.** Because Il2CppInterop
   emits IL2CPP interfaces as classes, there are **zero** `public interface` types in `Assembly-CSharp` —
   `ISaveable`, `IBaseSaveable` and `IGenericSaveable` are all classes, a managed type cannot implement
   them, and `SaveManager.RegisterSaveable(...)` is therefore unreachable. This is exactly why S1API
   Harmony-patches the pipeline instead and writes into `<saveslot>\Modded\Saveables\`. Ranked options:
   S1API's `Saveable` first; then your own JSON in a **subfolder** of
   `LoadManager.Instance.LoadedGameFolderPath` written from `SaveManager.onSaveComplete` (root-level files
   risk the save pruner, nested folders demonstrably survive); then `VariableDatabase` for a handful of
   bool/float flags. Full detail in `API-PERSISTENCE-TIME.md`.

4. **The S1API NuGet package is the *Mono* build — do not reference it if you touch game types.**
   Verified by hashing: `%USERPROFILE%\.nuget\packages\s1api.forked\3.1.4\lib\netstandard2.1\S1API.dll` is
   **byte-identical** (SHA256 `C018BBF5B0C29370…`, 1,321,984 bytes) to
   `...\Schedule I\Mods\S1API.Mono.MelonLoader.dll.disabled`. It contains **0** `Il2CppScheduleOne`
   references and **194** bare `ScheduleOne` ones. The runtime DLL actually loaded,
   `...\Mods\S1API.Il2Cpp.MelonLoader.dll` (1,358,336 bytes), is the exact inverse: **194**
   `Il2CppScheduleOne`, **0** bare `ScheduleOne`.

   Both assemblies are named `S1API` and expose the same type names, which is why the swap works at all and
   why the existing `CreativeMode` mod is fine — it only touches S1API's own managed types. But **any S1API
   member whose signature mentions a game type has a different signature in the two builds**, so code
   compiled against the NuGet reference binds to `ScheduleOne.X`, which does not exist at runtime, and fails
   with a type/method load error. 194 occurrences means this leak is not marginal.

```xml
<!-- Safe: compile against the same build that will actually load. -->
<Reference Include="S1API">
  <HintPath>$(GameDir)\Mods\S1API.Il2Cpp.MelonLoader.dll</HintPath>
  <Private>false</Private>
</Reference>
```

   Keep the `PackageReference` only if the mod restricts itself to game-type-free S1API members, and in that
   case keep the `PreventS1APICopy` target (`IL2CPP-NOTES.md` §2). Referencing the runtime DLL directly is
   the safer default for all three mods.

---

## Mod 1 — Hireable Drivers

**Goal:** driver employees that transport items between the player's properties, businesses and dealers.

### What already exists (verified)

The entire take → carry → place cycle is already implemented and already lives on **every** `Employee`:

```csharp
// Il2CppScheduleOne.Employees.Employee
Il2CppScheduleOne.NPCs.Behaviour.MoveItemBehaviour MoveItemBehaviour { get; set; }
public void MarkIsWorking();
public virtual Il2CppScheduleOne.Employees.EmployeeHome GetHome();

// Il2CppScheduleOne.NPCs.Behaviour.MoveItemBehaviour  (: Behaviour)
Il2CppScheduleOne.Management.TransitRoute assignedRoute { get; set; }
Il2CppScheduleOne.ItemFramework.ItemInstance itemToRetrieveTemplate { get; set; }
int grabbedAmount { get; set; }
int maxMoveAmount { get; set; }
MoveItemBehaviour.EState currentState { get; set; }
bool skipPickup { get; set; }
bool Initialized { get; set; }
public bool CanGetToSource(Il2CppScheduleOne.Management.TransitRoute route);
public bool CanGetToDestination(Il2CppScheduleOne.Management.TransitRoute route);
public UnityEngine.Transform GetSourceAccessPoint(Il2CppScheduleOne.Management.TransitRoute route);
public UnityEngine.Transform GetDestinationAccessPoint(Il2CppScheduleOne.Management.TransitRoute route);
public int GetAmountToGrab();
public void EndTransit();
public Il2CppScheduleOne.Persistence.Datas.MoveItemData GetSaveData();
public void Initialize(TransitRoute route, ItemInstance itemTemplate, int maxMoveAmount, bool skipPickup);
// state machine: enum MoveItemBehaviour.EState { Idle, WalkingToSource, Grabbing, WalkingToDestination, Placing }
```

**And `Employee` can already be sent to an arbitrary destination, with a built-in teleport fallback:**

```csharp
// Il2CppScheduleOne.Employees.Employee
public void SetDestination(Il2CppScheduleOne.Management.ITransitEntity transitEntity, bool teleportIfFail = true);
public void SetDestination(UnityEngine.Vector3 position, bool teleportIfFail = true);
```

`teleportIfFail` defaults to **`true`**, which means the vanilla game already teleports an employee when
pathing fails. That is a strong hint that a "walk, and teleport if you can't" driver is consistent with the
game's own behaviour, and it makes the no-vehicle fallback (below) far less of a hack than it sounds.

Routes are **not** restricted to a single property by their type — both route classes take arbitrary
endpoints through a public constructor:

```csharp
// Il2CppScheduleOne.Management.TransitRoute : Il2CppSystem.Object
public TransitRoute(ITransitEntity source, ITransitEntity destination);
ITransitEntity Source { get; set; }
ITransitEntity Destination { get; set; }
Il2CppSystem.Action<ITransitEntity> onSourceChange, onDestinationChange;
public virtual void SetSource(ITransitEntity source);
public virtual void SetDestination(ITransitEntity destination);
public bool AreEntitiesNonNull();
public void ValidateEntities();
public void SetVisualsActive(bool active);
public void Destroy();

// Il2CppScheduleOne.Management.AdvancedTransitRoute : TransitRoute
public AdvancedTransitRoute(ITransitEntity source, ITransitEntity destination);
public AdvancedTransitRoute(Il2CppScheduleOne.Persistence.Datas.AdvancedTransitRouteData data);
Il2CppScheduleOne.Management.ManagementItemFilter Filter { get; set; }
public Il2CppScheduleOne.ItemFramework.ItemInstance GetItemReadyToMove();
public Il2CppScheduleOne.Persistence.Datas.AdvancedTransitRouteData GetData();
```

The item-movement primitive is `ITransitEntity` (remember: Il2CppInterop emits it as a **class**, so reach
it with `TryCast<ITransitEntity>()`):

```csharp
// Il2CppScheduleOne.Management.ITransitEntity
System.String Name { get; }
Il2CppSystem.Collections.Generic.List<ItemSlot> InputSlots { get; set; }
Il2CppSystem.Collections.Generic.List<ItemSlot> OutputSlots { get; set; }
UnityEngine.Transform LinkOrigin { get; }
Il2CppReferenceArray<UnityEngine.Transform> AccessPoints { get; }
System.Boolean Selectable { get; }
System.Boolean IsAcceptingItems { get; }
System.Boolean IsDestroyed { get; }
Il2CppSystem.Guid GUID { get; }

public virtual ItemSlot GetFirstSlotContainingItem(string id, ITransitEntity.ESlotType searchType);
public virtual ItemSlot GetFirstSlotContainingTemplateItem(ItemInstance templateItem, ITransitEntity.ESlotType searchType);
public virtual int  GetInputCapacityForItem(ItemInstance item, Il2CppScheduleOne.NPCs.NPC asker = null, bool checkPlayerFilters = true);
public virtual int  GetOutputCapacityForItem(ItemInstance item, Il2CppScheduleOne.NPCs.NPC asker = null);
public virtual ItemSlot GetOutputItemContainer(ItemInstance item);
public virtual void InsertItemIntoInput(ItemInstance item, Il2CppScheduleOne.NPCs.NPC inserter = null);
public virtual void InsertItemIntoOutput(ItemInstance item, Il2CppScheduleOne.NPCs.NPC inserter = null);
public virtual Il2CppSystem.Collections.Generic.List<ItemSlot> ReserveInputSlotsForItem(ItemInstance item, Il2CppFishNet.Object.NetworkObject locker);
public virtual void RemoveSlotLocks(Il2CppFishNet.Object.NetworkObject locker);
public virtual void ShowOutline(UnityEngine.Color color);
public virtual void HideOutline();
// nested: enum ITransitEntity.ESlotType { Input = 0, Output = 1, Both = 2 }
```

Note the `asker` / `inserter` parameters are typed `NPC` — employees are the *intended* actor for this API,
which is a strong signal the design supports what this mod wants.

Property/business enumeration and the natural cross-property endpoints are also ready:

```csharp
// Il2CppScheduleOne.Property.Property  (7 subclasses: Bungalow, Business, Manor, MotelRoom, RV, SewerOffice, Sweatshop)
static Il2CppSystem.Collections.Generic.List<Property> OwnedProperties   { get; set; }
static Il2CppSystem.Collections.Generic.List<Property> UnownedProperties { get; set; }
static Il2CppSystem.Collections.Generic.List<Business> OwnedBusinesses   { get; set; }
static Il2CppSystem.Collections.Generic.List<Business> UnownedBusinesses { get; set; }
System.String PropertyCode { get; }
System.Boolean IsOwned { get; set; }
Il2CppReferenceArray<Il2CppScheduleOne.Delivery.LoadingDock> LoadingDocks { get; set; }
System.Int32 LoadingDockCount { get; }
Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Employees.Employee> Employees { get; set; }
public Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ObjectScripts.Bed> GetUnassignedBeds();
public void SetOwned();          // + SetOwned_Server() / RpcLogic___SetOwned_Server_2166136261()
```

`Il2CppScheduleOne.Delivery.LoadingDock` implements `ITransitEntity`, so **loading docks are the natural
source/destination pair for inter-property haulage** — they are the game's own "goods enter and leave here"
abstraction, and they exist per property.

Employee creation is a supported runtime path:

```csharp
// Il2CppScheduleOne.Employees.EmployeeManager : NetworkSingleton<EmployeeManager>
public void CreateEmployee(Property property, EEmployeeType type, string firstName, string lastName,
        string id, bool male, int appearanceIndex, Vector3 position, Quaternion rotation, string guid = "");
public Employee CreateEmployee_Server(Property property, EEmployeeType type, string firstName, string lastName,
        string id, bool male, int appearanceIndex, Vector3 position, Quaternion rotation, string guid);
public void CreateNewEmployee(Property property, EEmployeeType type);
public Employee GetEmployeePrefab(EEmployeeType type);
public Il2CppSystem.Collections.Generic.List<Employee> GetEmployeesByType(EEmployeeType type);
public void GenerateRandomName(bool male, out string firstName, out string lastName);
public void GetRandomAppearance(bool male, out int index, out AvatarSettings settings);
public EmployeeManager.EmployeeAppearance GetAppearance(bool male, int index);
public VODatabase GetVoice(bool male, int index);
public void RegisterAppearance(bool male, int index);
public void RegisterName(string name);
Employee BotanistPrefab, PackagerPrefab, ChemistPrefab, CleanerPrefab { get; set; }
Il2CppSystem.Collections.Generic.List<Employee> AllEmployees { get; set; }
// nested: class EmployeeAppearance { AvatarSettings Settings; UnityEngine.Sprite Mugshot; }
```

`CreateEmployee` is a `[ServerRpc]` (`RpcWriter___Server_CreateEmployee_311954683` /
`RpcLogic___CreateEmployee_311954683`); `CreateEmployee_Server` is the authoritative body.

### What does NOT exist

- **No `Driver`, `Handler` or `Courier` `Employee` subclass.** Exactly four exist: `Botanist`, `Chemist`,
  `Cleaner`, `Packager`. Watch the trap: `EEmployeeType.Handler = 1` is the *enum name* for the `Packager`
  class (`Botanist=0, Handler=1, Chemist=2, Cleaner=3`), so the console command is
  `addemployee handler <propertyCode>`.
- **No behaviour that both carries items and drives** — but every *primitive* exists, and none of them is
  police-gated. Only the two existing behaviours are (`VehiclePatrolBehaviour` = waypoint loop,
  `VehiclePursuitBehaviour` = chase the player). What's missing is purely a behaviour expressing
  "drive to point B once". The driving stack itself is open:

```csharp
// Il2CppScheduleOne.Vehicles.AI.VehicleAgent : UnityEngine.MonoBehaviour   (no faction check anywhere)
//   reached as LandVehicle.Agent  -- Il2CppScheduleOne.Vehicles.AI.VehicleAgent Agent { get; set; }
public void Navigate(UnityEngine.Vector3 location,
                     Il2CppScheduleOne.Vehicles.AI.NavigationSettings settings = null,
                     Il2CppScheduleOne.Vehicles.AI.VehicleAgent.NavigationCallback callback = null);

// arrival is a CALLBACK, not a pollable state enum
public sealed class VehicleAgent.NavigationCallback   // : Il2CppSystem.MulticastDelegate
{
    public void Invoke(VehicleAgent.ENavigationResult status);
    public static NavigationCallback op_Implicit(System.Action<VehicleAgent.ENavigationResult>);
}
public enum VehicleAgent.ENavigationResult { Failed = 0, Complete = 1, Stopped = 2 }

// snap a raw world position onto the drivable graph before navigating
public static UnityEngine.Vector3 Il2CppScheduleOne.Vehicles.AI.NavigationUtility.SampleVehicleGraph(UnityEngine.Vector3 destination);

// Il2CppScheduleOne.Vehicles.VehicleManager
public LandVehicle SpawnAndReturnVehicle(string vehicleCode, UnityEngine.Vector3 position,
                                         UnityEngine.Quaternion rotation, bool playerOwned);
public LandVehicle GetVehiclePrefab(string vehicleCode);   // null for a bad code = free validity check
```

  `Il2CppScheduleOne.Employees.Employee : Il2CppScheduleOne.NPCs.NPC`, so all four employee types inherit
  `EnterVehicle(NetworkConnection, LandVehicle)`, `ExitVehicle()` and `CurrentVehicle` for free.
  `NavigationCallback` has `op_Implicit(System.Action<ENavigationResult>)`, so wiring arrival from managed
  code is a one-liner.

- **`LandVehicle` is definitively NOT an `ITransitEntity`.** Independently verified: **no type anywhere in
  `Il2CppScheduleOne.Vehicles` declares `InputSlots`, `OutputSlots`, `AccessPoints`, `LinkOrigin`,
  `IsAcceptingItems` or `InsertItemIntoInput`.** The trunk is reached as
  `LandVehicle.Storage` (type `Il2CppScheduleOne.Storage.StorageEntity`), which has no
  `InputSlots`/`OutputSlots` split. `Il2CppReferenceArray<UnityEngine.Transform> AccessPoints` is declared
  only in the `Delivery`, `Growing`, `Management`, `ObjectScripts` and `StationFramework` namespaces —
  `Delivery.LoadingDock` being the relevant one. **So a trunk can never be a `TransitRoute` endpoint.**
  Route dock→dock and treat the trunk as manual `LandVehicle.Storage` bookkeeping, or skip it entirely.

- **AI cannot park.** Nothing in `.AI` references parking at all. `LandVehicle.Park(NetworkConnection conn,
  ParkData parkData, bool network)` is an instantaneous geometric snap. An AI "parks" by navigating to
  `ParkingLot.EntryPoint`, waiting for `ENavigationResult.Complete`, then calling `Park` itself.

- **The game's own delivery trucks never drive.** `DeliveryInstance` is `TimeUntilArrival` +
  `OnTimePass(int minutes)` + `SetStatus(EDeliveryStatus)`, and `LoadingDock.SetStaticOccupant(vehicle)`
  places the truck. That makes the timer-based fallback below the *shipped-feeling* option, not a compromise.

### Two S1API leads worth checking before writing anything

- **`S1API.Entities.Schedule.DriveToCarParkSpec`** — verified present. It is the only shipped primitive that
  makes an NPC drive somewhere. Dump its members first; it may hand you the missing "drive to point B"
  behaviour outright.
- **`S1API.Entities.NPCDealer`** — recruitable, holds cash, takes a signing fee and a cut, has assignable
  customers, and persists. If a driver can be *dealer-shaped* rather than employee-shaped, recruitment,
  payment and persistence all come free. Worth an hour of evaluation before committing to the `Packager`
  route below. Note `S1API.Entities.Employees` is confirmed dead for this purpose — two types, no hire/fire/
  wage/assign API at all.

### Strategy

**Reuse a `Packager`, suppress its vanilla brain, and drive `MoveItemBehaviour` + `VehicleAgent.Navigate`
yourself from an injected component.** Concretely:

1. **Spawn.** `EmployeeManager.Instance.CreateEmployee_Server(property, EEmployeeType.Handler, first, last,
   id, male, appearanceIndex, pos, rot, guid)` — server only. You get a fully networked NPC with avatar,
   voice, mugshot and a valid prefabId, for free.
2. **Re-skin.** `ScriptableObject.CreateInstance<BasicAvatarSettings>()` (namespace
   `Il2CppScheduleOne.AvatarFramework.Customization`), populate its 31 semantic fields, then
   `employee.Avatar.LoadAvatarSettings(basic.GetAvatarSettings())`. Or pick an existing entry from
   `EmployeeManager.MaleAppearances` / `FemaleAppearances` (`EmployeeAppearance.Settings`) and tweak.
3. **Mark and control.** `employee.gameObject.AddComponent<DriverController>()` where `DriverController` is
   an injected plain `MonoBehaviour` (see `IL2CPP-NOTES.md` §7.2). This is your marker *and* your brain.
4. **Suppress the packaging brain.** Harmony prefix returning `false` on `Packager.UpdateBehaviour()`,
   gated on `GetComponent<DriverController>() != null` so you only affect your own instances. Patch the
   **declared** method on `Packager` with `AccessTools.DeclaredMethod` (§6.4).
5. **Route.** Build `new AdvancedTransitRoute(sourceLoadingDock, destLoadingDock)` from
   `Property.OwnedProperties[i].LoadingDocks`, cast endpoints with `TryCast<ITransitEntity>()`, and use
   `GetItemReadyToMove()` to decide what to haul.
6. **Test reachability first.** Call `driverEmployee.MoveItemBehaviour.CanGetToDestination(route)`. **This
   predicate is the crux of the whole mod.** If it returns true for cross-property routes, you may not need
   vehicles at all — just feed the route to `MoveItemBehaviour` and let the NPC walk. Even if it returns
   false, `Employee.SetDestination(entity, teleportIfFail: true)` gives you a sanctioned recovery, so the
   worst case is "walks where it can, teleports where it can't" rather than a stuck NPC.
7. **Vehicle leg (only if needed).** Verified sequence:

```csharp
var veh   = VehicleManager.Instance.SpawnAndReturnVehicle("shitbox", spawnPos, spawnRot, playerOwned: true);
employee.EnterVehicle(null, veh);                                   // null conn = UNVERIFIED, test it
var target = NavigationUtility.SampleVehicleGraph(destDock.AccessPoints[0].position);
veh.Agent.Navigate(target, null, (System.Action<VehicleAgent.ENavigationResult>)(status =>
{
    if (status != VehicleAgent.ENavigationResult.Complete) { /* fall back to SetDestination */ return; }
    employee.ExitVehicle();
    employee.MoveItemBehaviour.Initialize(route, template, amount, skipPickup: true);
}));
```

   `skipPickup: true` matters — the goods are already "on" the driver, so the destination leg should place
   without re-grabbing. Only two vehicle codes are provable from metadata (`shitbox` from the
   `spawnvehicle` example usage, `veeper` from a real `OwnedVehicles.json`); enumerate
   `VehicleManager.Instance.VehiclePrefabs` at runtime for the rest.
8. **Persist** your driver roster and in-flight state per save (see `API-PERSISTENCE-TIME.md`); the vanilla
   `MoveItemBehaviour` already round-trips mid-delivery via `MoveItemData`.

### Risks

- **`CanGetToDestination` semantics are unverified.** Everything above branches on it. **Spend the first
  hour of implementation testing this one predicate in-game** — it decides whether this is a two-day mod or
  a two-week mod.
- **`LandVehicle.Agent` is an inspector-wired field, and whether civilian prefabs have a `VehicleAgent`
  component at all is UNVERIFIED.** If `veh.Agent` is null on `shitbox`/`veeper`, the entire AI-driving path
  is unavailable regardless of how open the API looks. **This is the first thing to test.** Adding a
  `VehicleAgent` at runtime is not realistic — it needs two `Seeker`s, five `Sensor`s, four sweep origins,
  axle transforms, two `Wheel`s and a `VehicleTeleporter`, all inspector-wired.
- **Long routes may be flaky.** `VehiclePatrolBehaviour` tracks `consecutivePathingFailures` against a
  `MAX_CONSECUTIVE_PATHING_FAILURES`, which implies pathing failures are routine enough that the shipped
  code needs a counter. Budget for retry/recovery, and always handle `ENavigationResult.Failed`.
- **`VehicleAgent` is not networked** — only `LandVehicle`'s transform and three SyncVars replicate. A
  mod-driven vehicle will look smooth on the host and interpolated on clients.
- **Fallback if driving proves unworkable:** simulate, exactly the way the game's own deliveries do. Send
  the driver into the vehicle, hide both, run a timer (`TimeManager.TimedCallback` or the
  `DeliveryInstance.OnTimePass(int minutes)` pattern), then move the items with
  `ITransitEntity.InsertItemIntoInput` at the destination and reappear. Because `DeliveryVehicle` has no
  `VehicleAgent` reference and `LoadingDock.SetStaticOccupant(vehicle)` is how the game parks its own
  trucks, this **is** the shipped pattern — not a compromise. It uses only verified APIs and I'd ship it
  first, then upgrade to real driving once `Agent` is confirmed non-null.
- **`(EEmployeeType)4` for a genuine "Driver" type** requires a `GetEmployeePrefab` postfix and will hit
  vanilla `switch` statements with no case for it. Prefer reusing `Handler` plus your marker component.

---

## Mod 2 — Police Improvements

**Goal:** dynamic police intensity, federal agents, outlaw status, heavier consequences when caught.

### What already exists (verified)

Wanted level is a per-player, server-authoritative FishNet SyncVar — **not persisted**, it resets on load:

```csharp
// Il2CppScheduleOne.PlayerScripts.PlayerCrimeData : NetworkBehaviour
PlayerCrimeData.EPursuitLevel CurrentPursuitLevel { get; set; }   // SyncVar
System.Single CurrentPursuitLevelDuration { get; set; }
Il2CppSystem.Action<PlayerCrimeData.EPursuitLevel, PlayerCrimeData.EPursuitLevel> onPursuitLevelChange { get; set; }
Il2CppFishNet.Object.Synchronizing.SyncVar<PlayerCrimeData.EPursuitLevel> syncVar____CurrentPursuitLevel_k__BackingField;
public void SetPursuitLevel(PlayerCrimeData.EPursuitLevel level);
public void SetPursuitLevel_Server(PlayerCrimeData.EPursuitLevel level);   // + RpcWriter___Server_/RpcLogic___
// enum EPursuitLevel { None=0, Investigating=1, Arresting=2, NonLethal=3, Lethal=4 }
```

`onPursuitLevelChange` is a public `Action<,>` — **subscribe to it, no Harmony needed.**

The intensity dial and the tick to drive it:

```csharp
// Il2CppScheduleOne.Law.LawController : Il2CppScheduleOne.DevUtilities.Singleton<LawController>
//   NOTE: a plain Singleton<T>, NOT a NetworkSingleton<T> -- LE_Intensity is UNSYNCHRONISED.
System.Int32 LE_Intensity { get; set; }
public void OnUncappedMinPass();
public void SetInternalIntensity(System.Single intensity);
public void OverrideSetings(Il2CppScheduleOne.Law.LawActivitySettings settings);   // sic, one 't'
```

Confiscation is real and tier-aware — better packaging survives:

```csharp
// Il2CppScheduleOne.UI.ArrestNoticeScreen
public void ConfiscateItems(Il2CppScheduleOne.Product.Packaging.EStealthLevel maxStealthLevel);
```

Officers are **pre-placed and pooled**, dispatched from stations, hard-capped at 4 — there is no officer
prefab path anywhere in the string literals. `PoliceStation.OfficerPool` / `Dispatch(...)` / `PullOfficer()`.
`Il2CppScheduleOne.Police.NPCResponses_Police` has 19 `virtual` `Noticed*`/`RespondTo*` methods, and
`PursuitBehaviour`'s four `Update<Level>Behaviour()` methods are virtual too — an unusually good hook
surface. The 17 `Crime` subclasses are listed in `API-POLICE.md`.

### What does NOT exist

- **No jail, holding cell, `JailManager` or `PoliceManager`.** The arrest flow is
  `Player.Arrest_Server` → notice screens → confiscate + fine → `Player.Free_Server` →
  `PlayerCrimeData.OnPlayerFreed()`. "Heavier consequences" must therefore be *invented*, not extended.
- **No `Crime` registry.** `Crime` is a bare `Il2CppSystem.Object` with one `CrimeName` property.
  Downstream code keys on the runtime `Il2CppSystem.Type`, so an injected subclass can flow through, but
  `PenaltyHandler.ProcessCrimeList` is a static with hardcoded fine constants and will emit nothing for an
  unknown type — you must postfix it.

### S1API already covers much of this

`S1API.Law` (11 public types, not previously on the radar) wraps most of what this mod needs:
`LawController` (including `SetIntensityLevel(1..10)`), `LawManager`, `PursuitLevel`, `PlayerCrimeData`,
`CheckpointManager`, `CheckpointInfo`, `CheckpointLocation`, `CurfewManager`, `FootPatrolRoute`,
`VehiclePatrolRoute`, `PatrolGroup`. `S1API.Entities.NPCs.PoliceOfficers` exposes all nine existing officers
as `NPC` wrappers, and `S1API.Entities.Appearances.AccessoryFields` has `PoliceCap`,
`BulletProofVestPolice` and `PoliceBelt` constants for dressing a federal-agent variant.

**But heed finding 4 above**: `LawManager.GetAssignedOfficers` and `LawController.OverrideActivitySettings`
are among the ~15 members that leak `Il2Cpp*` types, so reference the runtime IL2CPP DLL rather than the
NuGet package if you use them.

### Strategy

- **Dynamic intensity:** postfix `LawController.OnUncappedMinPass()`, compute your own target from time of
  day / player wealth / heat, then write `LE_Intensity` and `SetInternalIntensity(float)`. Every scheduling
  instance (`PatrolInstance`, `CheckpointInstance`, `SentryInstance`, `VehiclePatrolInstance`,
  `CurfewInstance`) already re-compares its `IntensityRequirement` against it each game minute, so you get
  patrol/checkpoint density for free. For wholesale schedule swaps use `OverrideSetings(...)` with no patch
  at all.
- **Federal agents:** clone an existing pooled `PoliceOfficer` GameObject, re-skin via `AvatarSettings`
  (dark suit), attach a marker component, and override behaviour through the virtual
  `NPCResponses_Police` methods. Do **not** try to inject a `PoliceOfficer` subclass — it is a
  `NetworkBehaviour` with a SyncVar.
- **Outlaw status:** a mod-owned scalar persisted per save, fed into the intensity calculation above and
  surfaced through existing UI. Subscribe to `onPursuitLevelChange` to accumulate it.
- **Heavier consequences:** postfix `ArrestNoticeScreen.ConfiscateItems` to widen the `EStealthLevel` tier,
  and postfix `PenaltyHandler.ProcessCrimeList` to scale fines. Cash seizure has **no** existing API —
  implement it yourself against `MoneyManager`.

### Risks

- **The tuning constants may be C# `const` and therefore inlined by IL2CPP**, in which case writing the
  exposed static property changes nothing. This is the single biggest risk for a tuning mod. Probe it early
  with a throwaway build that writes one constant and observes behaviour.
- **`LawController` is `Singleton<T>`, not `NetworkSingleton<T>`, so `LE_Intensity` is not replicated.**
  Intensity changes are correct solo but will diverge between host and clients in co-op unless the mod
  computes the value deterministically on every peer from replicated inputs (time, pursuit level), or you
  accept host-authoritative-only behaviour and drive officers exclusively through server-side spawns.
- Officer count is capped at 4 by `PoliceStation.Dispatch`. Escalation has a ceiling unless you patch it.
- Pursuit level is not saved, so "outlaw" persistence is entirely on you.

---

## Mod 3 — Special Customers

**Goal:** special customer groups (bikers, hippies, businessmen) that periodically visit town and buy large
quantities.

### What already exists (verified)

`Customer` is a **standalone component, not an `NPC` subclass** — and it has zero subclasses:

```csharp
// Il2CppScheduleOne.Economy.Customer : Il2CppFishNet.Object.NetworkBehaviour
static Il2CppSystem.Collections.Generic.List<Customer> LockedCustomers   { get; set; }
static Il2CppSystem.Collections.Generic.List<Customer> UnlockedCustomers { get; set; }
static Il2CppSystem.Action<Customer> onCustomerUnlocked { get; set; }
static System.Int32 MaxOrderQuantityPerProduct { get; set; }
public void ForceDealOffer();                    // <-- forces an order, server-side, no RPC hop
public virtual void OnCustomerUnlocked(Il2CppScheduleOne.NPCs.Relation.NPCRelationData.EUnlockType unlockType, bool notify);
```

There is **no `CustomerManager`** — the registry is literally those two static lists, and order scheduling
is per-customer. `Il2CppScheduleOne.NPCs.Behaviour.CustomerAttendDealBehaviour` exists and, per
`API-ECONOMY.md`, must be present on the GameObject or `Customer` disables itself on Awake.

`CustomerData` is a `ScriptableObject` with the full authoring schema (16 fields — `MinWeeklySpend`,
`MaxWeeklySpend`, `MinOrdersPerWeek`, `MaxOrdersPerWeek`, `OrderTime`, `PreferredOrderDay`, `Standards`,
`CallPoliceChance`, `BaseAddiction`, affinities, …). See `API-ECONOMY.md` for the verbatim list.

The **cartel system is a ready-made template for "a group visits town"**: `CartelActivities` /
`CartelRegionActivities` / `CartelActivity` is an existing save-persisted per-region periodic scheduler with
hour cooldowns, and `GoonPool` + `CartelGoon.Spawn/Despawn/AddGoonMate` is a working pooled group spawn that
walks NPCs out of a building and back:

```csharp
// Il2CppScheduleOne.Cartel.GoonPool : UnityEngine.MonoBehaviour
static System.Single MALE_CHANCE { get; set; }
Il2CppReferenceArray<CartelGoon> goons { get; set; }
Il2CppReferenceArray<Il2CppScheduleOne.Map.NPCEnterableBuilding> exitBuildings { get; set; }
Il2CppReferenceArray<AvatarSettings> MaleBaseAppearances, FemaleBaseAppearances, MaleClothing, FemaleClothing;
Il2CppReferenceArray<Il2CppScheduleOne.VoiceOver.VODatabase> MaleVoices, FemaleVoices;
Il2CppStructArray<UnityEngine.Color> SkinTones, HairColors;
Il2CppSystem.Collections.Generic.List<CartelGoon> spawnedGoons, unspawnedGoons;
System.Int32 UnspawnedGoonCount { get; }
public CartelGoon SpawnGoon(UnityEngine.Vector3 spawnPoint);
public Il2CppSystem.Collections.Generic.List<CartelGoon> SpawnMultipleGoons(Vector3 spawnPoint, int requestedAmount, bool setAsGoonMates = true);
public void ReturnToPool(CartelGoon goon);
public CartelGoonAppearance GetRandomAppearance();
public Il2CppScheduleOne.Map.NPCEnterableBuilding GetNearestExitBuilding(UnityEngine.Vector3 position);

// Il2CppScheduleOne.Cartel.CartelGoonAppearance : Il2CppSystem.Object
public CartelGoonAppearance(bool isMale, int baseAppearanceIndex, UnityEngine.Color skinColor,
                            UnityEngine.Color hairColor, int clothingIndex, int voiceIndex);
```

**Study `GoonPool` closely — it is the exact shape this mod wants**: a fixed pool of pre-placed, already-
registered NPC instances, recycled and re-skinned per appearance rather than instantiated.

### What does NOT exist

- **No customer-group / faction / cohort concept anywhere.** No such field on `Customer` or `CustomerData`,
  and no `NPCRegion` type. The mod must invent grouping; the natural key is `NPC.Region`
  (`Il2CppScheduleOne.Map.EMapRegion`, 6 values) plus `NPCManager.GetNPCsInRegion(EMapRegion)`.

### Strategy

1. **Group definition** is mod-owned data: name, member count, `AvatarSettings` set, a `CustomerData`
   template, visit cadence, and an order-size multiplier.
2. **Spawning:** mirror `GoonPool`. Either reuse the goon pool's own instances when the cartel isn't using
   them, or reserve a pool of employee-prefab NPCs via `EmployeeManager.CreateEmployee_Server` and hide
   them until a visit. **Do not build custom prefabs.**
3. **Making them buy:** add a `Customer` component plus a `CustomerAttendDealBehaviour` to the spawned NPC,
   assign a `ScriptableObject.CreateInstance<CustomerData>()` template with large
   `Min/MaxWeeklySpend`, then call `Customer.ForceDealOffer()` — a single public parameterless server-side
   call. Raise `Customer.MaxOrderQuantityPerProduct` for the visit window if you need bulk.
4. **Scheduling visits:** subscribe to the game clock and model the cadence on `CartelActivities`' cooldown
   pattern. Verified events on `Il2CppScheduleOne.GameTime.TimeManager`:

```csharp
Il2Cpp.ActionList          onMinutePass;   // subscribe with .Add / .Remove, fires staggered across frames
Il2CppSystem.Action        onHourPass, onDayPass, onWeekPass, onSleepStart, onSleepEnd;
Il2CppSystem.Action<int>   onTimeSkip;
```

   Two traps: a **different** type also declares a `UnityEngine.Events.UnityEvent onHourPass`, so bind
   against `TimeManager` explicitly rather than by member name; and each `op_Implicit` conversion of a
   managed delegate allocates a *new* native delegate, so cache it in a static field or `-=` will silently
   no-op and leak. `TimeManager` is a scene-scoped `NetworkSingleton`, so re-bind on every load. Prefer
   `TimedCallback(action, durationMinutes, tickAtEndOfDay, tickOnTimeSkip)` for delayed work — it already
   handles sleep skips. Full detail in `API-PERSISTENCE-TIME.md`.

5. **Take the S1API shortcut — it covers this mod end to end.** There is no registration call: subclass
   `S1API.Entities.NPC` and override two methods.

```csharp
// S1API.Entities.NPC : S1API.Internal.Abstraction.Saveable
protected virtual void ConfigurePrefab(S1API.Entities.NPCPrefabBuilder builder);  // identity, appearance,
                                                                                 // customer data, schedule
protected virtual void OnCreated();                                              // runtime state
// retrieve later with NPC.Get<T>(); NPC.CustomNpcsReady flips true when the pass finishes
```

   S1API's `NPCPatches` hooks `NPCsLoader.Load`, pre-registers prefabs under `@S1API_PersistentPrefabs`,
   instantiates in save order, picks up NPCs added to an *existing* save, and network-spawns them. That is
   precisely the prefab/FishNet/save work steps 2-3 would otherwise cost you.
   `S1API.Entities.Customer.CustomerDataBuilder` maps fluent setters 1:1 onto `CustomerData`, and
   `S1API.Entities.Schedule.*` gives ready-made schedule specs (`WalkToSpec`, `StayInBuildingSpec`,
   `LocationBasedActionSpec`, `SitSpec`, `UseATMSpec`, `PrefabScheduleBuilder`, …) for the "arrive, mill
   about, leave" loop a visiting group needs. **Read `API-S1API.md` before writing any custom-NPC code.**

### Risks

- **Whether a `Customer` added *after* `NetworkObject` spawn actually replicates is UNVERIFIED and is this
  mod's biggest technical risk.** FishNet normally fixes the `NetworkBehaviour` set at spawn time. S1API's
  `TryNetworkInitialize` implies it can be made to work, but it needs a live two-client test. **If it does
  not replicate, host-only is the fallback** and should be stated in the mod description.
- Pool exhaustion: `GoonPool` has a finite `goons` array. A large group visit may need its own reserved pool.
- Order-generation formulas (`GetAdjustedWeeklySpend`, `GetWeightedRandomProduct`) are signature-only —
  balance tuning will be empirical.

---

## Cross-cutting requirement — the native-looking "Mods" menu screen

All three mods must be toggleable from a main-menu screen that looks native. This is **feasible and
low-risk**, because the game's menu is built from a small set of reusable, clonable widget components.
`API-UI-MENU.md` has the full picture; this is the verified skeleton.

The main menu is 17 types in `Il2CppScheduleOne.UI.MainMenu`. The load-bearing one is a shared screen base:

```csharp
// Il2CppScheduleOne.UI.MainMenu.MenuScreen : UnityEngine.MonoBehaviour
static Il2CppScheduleOne.UI.MainMenu.MenuScreen Current { get; set; }
static System.Single LerpTime, LerpScale { get; set; }
System.Boolean IsOpen { get; set; }
System.Boolean OpenOnStart { get; set; }
System.Int32 ExitInputPriority { get; set; }
Il2CppScheduleOne.UI.MainMenu.MenuScreen PreviousScreen { get; set; }
UnityEngine.CanvasGroup Group { get; set; }
Il2CppScheduleOne.UIScreen uiScreen { get; set; }
Il2CppScheduleOne.UIPanel  uiPanel  { get; set; }
UnityEngine.RectTransform rect { get; set; }
public void Open();
public void Open(System.Boolean b);
public void Close();
public void Lerp(System.Boolean open);
public virtual void OnOpen();
public virtual void OnClose();
public virtual void Exit(Il2CppScheduleOne.ExitAction action);
public virtual void Awake();
public void Start();
```

Its subclasses are the actual screens: `SettingsScreen`, `ContinueScreen`, `NewGameScreen`, `SetupScreen`,
`ImportScreen`, `ConfirmExitScreen`, `ConfirmOverwriteScreen`, `JoinLocal`, `Disclaimer`. Screen switching is
just `Open()` / `Close()` plus `PreviousScreen`, and `MenuScreen.Current` is a static you can read to know
where the user is. `MainMenuController` itself is nearly empty — the structure lives in the prefab, so
**discover objects by component type, not by path string.**

`Il2CppScheduleOne.UI.Settings` gives you 28 types, of which three are the reusable row widgets and the rest
are concrete instances of them (`GodRaysToggle`, `VSyncToggle`, `SSAOToggle`, `AudioSlider`, `FOVSLider`
[sic], `QualityDropdown`, …):

```csharp
// Il2CppScheduleOne.UI.Settings.SettingsToggle : UnityEngine.MonoBehaviour
Il2CppScheduleOne.UIToggle uiToggle { get; set; }
public virtual void Awake();
public void GetReferences();
public virtual void OnValueChanged(System.Boolean value);
public void SetIsOnWithoutNotify(System.Boolean value);
// siblings: SettingsSlider, SettingsDropdown
```

### Recommended approach — add a *category tab* to the existing settings screen

Do not clone the whole screen. `SettingsScreen` already models its tabs as a public, mutable array of
`{ Toggle, Panel }` pairs, so a mod can append one and inherit every visual and behavioural detail for free:

```csharp
// Il2CppScheduleOne.UI.MainMenu.SettingsScreen : MenuScreen
Il2CppReferenceArray<SettingsScreen.SettingsCategory> Categories { get; set; }   // public + settable
Il2CppScheduleOne.UITab Tab { get; set; }
UnityEngine.UI.Button ApplyDisplayButton { get; set; }
Il2CppReferenceArray<UnityEngine.GameObject> HostOnlyGameObjects { get; set; }
System.Boolean _initialized { get; set; }
public void ShowCategory(System.Int32 index);
public void ApplyDisplaySettings(System.Boolean showRevertMenu);
public virtual void Awake();
public void OnEnable();
public virtual void OnOpen();

// nested
public class SettingsScreen.SettingsCategory : Il2CppSystem.Object
{
    UnityEngine.UI.Toggle Toggle { get; set; }
    UnityEngine.GameObject Panel { get; set; }
}
```

1. **Harmony postfix `SettingsScreen.Awake()`.** Clone an existing category's `Toggle` and `Panel`
   GameObjects, then append a new `SettingsCategory` to `Categories`. Locate the screen with
   `Resources.FindObjectsOfTypeAll<SettingsScreen>()` — component-type search, **no path strings**.
2. **`Categories` is an `Il2CppReferenceArray<T>`, which is fixed-length.** You cannot append in place —
   allocate a new array one element longer, copy, add yours, and assign back. This is the one real gotcha.
3. **Let the game wire your tab for you.** `SettingsScreen.OnEnable()` iterates `Categories` and attaches a
   per-index listener (visible as the closure `_OnEnable_b__0(System.Boolean on)` over a captured `index`)
   that calls `ShowCategory`. If you append during the `Awake` postfix — i.e. before `OnEnable` — the
   vanilla code hooks your toggle to your panel with no extra work.
4. **Fill the panel with cloned `SettingsToggle` rows**, one per mod, and drive them from `MelonPreferences`
   (`IL2CPP-NOTES.md` §10). A cloned row still carries a *concrete* vanilla component (e.g. `GodRaysToggle`)
   whose `OnValueChanged` is `virtual`, so either destroy that component and wire `uiToggle` yourself, or
   Harmony-patch the concrete type gated on a marker component.
5. **Same component type serves the pause menu**, so one implementation covers both entry points.
6. **Persist toggles in `MelonPreferences`, not the save.** These are install-level switches; per-save state
   belongs in the save folder (`API-PERSISTENCE-TIME.md`).

Fallback if appending to `Categories` misbehaves: clone the entire `SettingsScreen` GameObject as a
standalone `MenuScreen` and add a main-menu button whose `Button.onClick` calls `yourScreen.Open()`
(`AddListener` takes `UnityEngine.Events.UnityAction`, reachable via `op_Implicit(System.Action)` —
`IL2CPP-NOTES.md` §5.2). Strictly worse-looking, but it cannot be blocked by array-resize semantics.

Note `HostOnlyGameObjects` — the game already has a concept of settings UI visible only to the host, which
is a useful precedent for gating host-authoritative mod toggles.

**Do not use legacy IMGUI for this.** See `IL2CPP-NOTES.md` §12 — the shipped `CreativeMode` mod hit real
runtime failures with `GUI.DrawTexture` and `GUILayout.TextField` on this build, and IMGUI cannot look
native anyway.

## Top risks across the programme

1. **`Customer`-after-spawn replication in co-op (Mod 3).** Unverified, structural, and there is no clean
   workaround if FishNet refuses. Test with two clients before writing anything else.
2. **Tuning constants may be `const`-inlined (Mod 2).** If `LE_Intensity` and friends are compile-time
   constants, the whole dynamic-intensity premise needs a different mechanism (`OverrideSetings` swaps).
3. **`MoveItemBehaviour.CanGetToDestination` semantics (Mod 1).** Determines whether the vehicle leg is
   required at all — a 10× difference in scope. Test first.
4. **`LandVehicle.Agent` may be null on civilian vehicle prefabs (Mod 1).** The navigation API itself is
   fully open and not faction-gated — `VehicleAgent.Navigate(Vector3, NavigationSettings, NavigationCallback)`
   with an `ENavigationResult` callback — but `Agent` is inspector-wired and a `VehicleAgent` cannot
   realistically be added at runtime. Test one `SpawnAndReturnVehicle` result for a non-null `Agent` before
   committing to real driving; ship the timer-simulated delivery (the game's own delivery-truck pattern)
   either way.
5. **The S1API Mono-vs-IL2CPP reference mismatch (all three mods).** Compiling against the NuGet package and
   calling any S1API member that exposes a game type produces a build that loads and then fails at the first
   such call. It is invisible at compile time, which is what makes it dangerous. Mitigation is one line of
   csproj (finding 4) — do it before writing code, not after debugging a `TypeLoadException`.

6. **Version fragility.** FishNet RPC name hashes, Il2CppInterop fallback names
   (`field_Private_Boolean_0`, `Method_Protected_Virtual_Void_0`) and positional FishNet prefabIds all shift
   between game versions. Resolve RPC methods by prefix at runtime, never hardcode a hash, and fail soft
   with a logged warning rather than throwing on a missing member.

Honourable mention: **co-op scope.** Given risks 1 and 5, declaring these mods **host-authoritative and
requiring the mod on all peers** is the right engineering call. Do all state mutation on the server
(`InstanceFinder.IsServer`), never write SyncVars from a client, and treat client-only installs as
unsupported rather than half-working.

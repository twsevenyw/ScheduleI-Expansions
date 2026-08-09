# API-EMPLOYEES.md — Schedule I employee system & NPC behaviour state machine

> **Audience:** modding team building **Hireable Drivers** (driver employees that transport items between properties, businesses and dealers).
> **Source of truth:** the IL2CPP dumps under `research/raw/` generated from
> `C:\Program Files (x86)\Steam\steamapps\common\Schedule I\MelonLoader\Il2CppAssemblies\`.
> Every type name, member name and signature below is copied verbatim from those dumps.
> Anything not directly readable from the dumps is labelled `(inferred …)` or **UNVERIFIED**.

### How to read this document

* Namespaces really are prefixed with `Il2Cpp` (`Il2CppScheduleOne.Employees`). The **original** namespace
  (`ScheduleOne.Employees`) is what appears in save-file keys, `ObfuscatedName` attributes, FishNet type
  strings and anything Harmony-by-string. Both are given where it matters.
* Il2CppInterop exposes IL2CPP **instance fields as C# properties**. You will see both
  `_Foo_k__BackingField` and `Foo`; **always prefer `Foo`**.
* `field_Private_Boolean_0`, `Method_Protected_Virtual_Void_0`, `Method_Private_IEnumerator_PDM_0` etc.
  are Il2CppInterop **fallback names** for members whose real name was stripped from metadata.
  ⚠️ These are **unstable across game versions and even across Il2CppInterop regenerations** —
  never bind to them by name from a mod if you can avoid it.
* FishNet RPCs appear as a method group: public `X` (entry point) →
  `RpcWriter___Server_X_<hash>` / `RpcWriter___Observers_X_<hash>` / `RpcWriter___Target_X_<hash>` (send) →
  `RpcReader___…_X_<hash>` (receive) → **`RpcLogic___X_<hash>` (the real body)**.
  Patch `RpcLogic___X_<hash>` to change behaviour; call `X` to trigger it.

---

## 1. Assemblies & namespaces

| Assembly | Path | Types | Notes |
|---|---|---|---|
| `Assembly-CSharp` v0.0.0.0 | `…\MelonLoader\Il2CppAssemblies\Assembly-CSharp.dll` | 3597 | Everything in this document except `EItemCategory`. |
| `Assembly-CSharp-firstpass` v0.0.0.0 | same folder | — | referenced by `Assembly-CSharp` |
| `Il2CppScheduleOne.Core` v0.0.0.0 | same folder | — | holds `Il2CppScheduleOne.Core.Items.Framework.*` (incl. `EItemCategory`, `BaseItemInstance`) |

Namespaces in scope (counts from `research/raw/01-namespaces.txt`, `<public>/<total>`):

| Namespace (Il2Cpp-prefixed) | Original name | Types | Role |
|---|---|---|---|
| `Il2CppScheduleOne.Employees` | `ScheduleOne.Employees` | 9/9 | `Employee` + 4 subclasses, `EmployeeManager`, `EmployeeHome`, `EEmployeeType` |
| `Il2CppScheduleOne.NPCs.Behaviour` | `ScheduleOne.NPCs.Behaviour` | 55/55 | `NPCBehaviour` controller + `Behaviour` base + 42 direct subclasses |
| `Il2CppScheduleOne.Management` | `ScheduleOne.Management` | 38/38 | `ITransitEntity`, `TransitRoute`, `AdvancedTransitRoute`, all `*Configuration` types, `ConfigField` types |
| `Il2CppScheduleOne.Management.UI` | `ScheduleOne.Management.UI` | 1/1 | just `ConfigPanel` (base of every clipboard panel) |
| `Il2CppScheduleOne.UI.Management` | `ScheduleOne.UI.Management` | 49/49 | clipboard screens, config panels, worldspace UI elements, selectors |
| `Il2CppScheduleOne.Storage` | `ScheduleOne.Storage` | 17/17 | `StorageEntity`, `WorldStorageEntity` |
| `Il2CppScheduleOne.ItemFramework` | `ScheduleOne.ItemFramework` | 40/40 | `ItemSlot`, `ItemInstance`, `IItemSlotOwner`, `SlotFilter` |
| `Il2CppScheduleOne.EntityFramework` | `ScheduleOne.EntityFramework` | 8/8 | `BuildableItem` (everything placeable) |

---

## 2. `Il2CppScheduleOne.Employees.Employee`

`public class Il2CppScheduleOne.Employees.Employee : Il2CppScheduleOne.NPCs.NPC`
(and `NPC : Il2CppFishNet.Object.NetworkBehaviour`).

### 2.1 Type list for this namespace (9 types, all verified)

```
public class  Il2CppScheduleOne.Employees.Employee            : Il2CppScheduleOne.NPCs.NPC
public class  Il2CppScheduleOne.Employees.Employee+NoWorkReason : Il2CppSystem.Object
public class  Il2CppScheduleOne.Employees.Botanist            : Il2CppScheduleOne.Employees.Employee
public class  Il2CppScheduleOne.Employees.Chemist             : Il2CppScheduleOne.Employees.Employee
public class  Il2CppScheduleOne.Employees.Cleaner             : Il2CppScheduleOne.Employees.Employee
public class  Il2CppScheduleOne.Employees.Packager            : Il2CppScheduleOne.Employees.Employee
public class  Il2CppScheduleOne.Employees.EmployeeManager     : Il2CppScheduleOne.DevUtilities.NetworkSingleton<Il2CppScheduleOne.Employees.EmployeeManager>
public class  Il2CppScheduleOne.Employees.EmployeeManager+EmployeeAppearance : Il2CppSystem.Object
public class  Il2CppScheduleOne.Employees.EmployeeHome        : UnityEngine.MonoBehaviour
public class  Il2CppScheduleOne.Employees.NPCResponses_Employee : Il2CppScheduleOne.NPCs.Responses.NPCResponses
public enum   Il2CppScheduleOne.Employees.EEmployeeType       : System.Enum
```

### 2.2 `EEmployeeType` — **note the `Handler` name**

```csharp
// [OriginalName("Assembly-CSharp.dll", "ScheduleOne.Employees", "EEmployeeType")]
public enum EEmployeeType {
    Botanist = 0,
    Handler  = 1,   // <-- this is the enum name of the class `Packager`. There is no class named "Handler".
    Chemist  = 2,
    Cleaner  = 3,
}
```

`EmployeeManager.GetEmployeePrefab(EEmployeeType)` maps `Handler` → `EmployeeManager.PackagerPrefab`
(inferred from the prefab field list — `BotanistPrefab`, `PackagerPrefab`, `ChemistPrefab`, `CleanerPrefab`
are the only four prefabs, and `Packager` is the only class without a matching enum name).

### 2.3 `Employee` — properties (39, verbatim)

```csharp
static System.Int32 MAX_CONSECUTIVE_PATHING_FAILURES { public get; public set; }
System.Boolean DEBUG { public get; public set; }
Il2CppScheduleOne.Property.Property _AssignedProperty_k__BackingField { public get; public set; }
System.Int32 _EmployeeIndex_k__BackingField { public get; public set; }
System.Boolean _PaidForToday_k__BackingField { public get; public set; }
System.Boolean _Fired_k__BackingField { public get; public set; }
System.Boolean _IsMale_k__BackingField { public get; public set; }
System.Int32 _AppearanceIndex_k__BackingField { public get; public set; }
Il2CppScheduleOne.Employees.EEmployeeType Type { public get; public set; }
Il2CppScheduleOne.Tools.FloatStack WorkSpeedController { public get; public set; }
System.Single SigningFee { public get; public set; }
System.Single DailyWage { public get; public set; }
Il2CppScheduleOne.NPCs.Behaviour.IdleBehaviour WaitOutside { public get; public set; }
Il2CppScheduleOne.NPCs.Behaviour.MoveItemBehaviour MoveItemBehaviour { public get; public set; }
Il2CppScheduleOne.Dialogue.DialogueContainer BedNotAssignedDialogue { public get; public set; }
Il2CppScheduleOne.Dialogue.DialogueContainer NotPaidDialogue { public get; public set; }
Il2CppScheduleOne.Dialogue.DialogueContainer WorkIssueDialogueTemplate { public get; public set; }
Il2CppScheduleOne.Dialogue.DialogueContainer FireDialogue { public get; public set; }
Il2CppScheduleOne.Dialogue.DialogueContainer TransferDialogue { public get; public set; }
Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Employees.Employee+NoWorkReason> WorkIssues { public get; public set; }
System.Int32 _TicksSinceLastWork_k__BackingField { public get; public set; }
System.Boolean initialized { public get; public set; }
System.Int32 consecutivePathingFailures { public get; public set; }
System.Single timeOnLastPathingFailure { public get; public set; }
UnityEngine.Transform cachedNPCSpawnPoint { public get; public set; }
Il2CppFishNet.Object.Synchronizing.SyncVar<System.Boolean> syncVar____PaidForToday_k__BackingField { public get; public set; }
System.Boolean field_Private_Boolean_0 { public get; public set; }   // interop fallback, unstable
System.Boolean field_Private_Boolean_1 { public get; public set; }   // interop fallback, unstable
Il2CppScheduleOne.Property.Property AssignedProperty { public get; public set; }
System.Int32 EmployeeIndex { public get; public set; }
System.Boolean PaidForToday { public get; public set; }
System.Boolean Fired { public get; public set; }
System.Boolean IsWaitingOutside { public get; }
System.Boolean IsMale { public get; public set; }
System.Int32 AppearanceIndex { public get; public set; }
Il2CppScheduleOne.Employees.EEmployeeType EmployeeType { public get; }
System.Single CurrentWorkSpeed { public get; }
System.Int32 TicksSinceLastWork { public get; public set; }
System.Boolean SyncAccessor_<PaidForToday>k__BackingField { public get; public set; }
```

Key notes:

* **`MoveItemBehaviour MoveItemBehaviour`** lives on the **base** `Employee`, so *every* employee type
  already owns a generic item-mover. This is the single most important field for the driver mod.
* **`IdleBehaviour WaitOutside`** — the behaviour used when the employee can't get inside / isn't working.
* **`SigningFee` / `DailyWage`** are **instance** `float`s serialized on the prefab. Their runtime values are
  **not** in the metadata dump — read them at runtime from
  `EmployeeManager.Instance.GetEmployeePrefab(type).SigningFee` / `.DailyWage`. **UNVERIFIED**: actual numbers.
* `WorkSpeedController` is a `Il2CppScheduleOne.Tools.FloatStack`; `CurrentWorkSpeed` is its resolved value.
* `PaidForToday` is a FishNet **SyncVar** (`syncVar____PaidForToday_k__BackingField`,
  `sync___get_value__PaidForToday_k__BackingField()` / `sync___set_value__PaidForToday_k__BackingField(bool, bool asServer)`).

### 2.4 `Employee` — constructors + methods (75, verbatim)

```csharp
private .ctor()
public .ctor()
public .ctor(System.IntPtr pointer)

// ---- lifecycle / Unity + FishNet ----
public virtual System.Void Awake()
public virtual System.Void Start()
public virtual System.Void OnDestroy()
public virtual System.Void OnStartServer()
public virtual System.Void OnSpawnServer(Il2CppFishNet.Connection.NetworkConnection connection)
public virtual System.Void OnTick()
public virtual System.Void NetworkInitializeIfDisabled()
public virtual System.Void NetworkInitialize__Late()
public virtual System.Void NetworkInitialize___Early()
public virtual System.Void Method_Protected_Virtual_Void_0()          // interop fallback name — real name stripped. UNVERIFIED which method this is.
public virtual System.Boolean ReadSyncVar___ScheduleOne_Employees_Employee(Il2CppFishNet.Serializing.PooledReader PooledReader0, System.UInt32 UInt321, System.Boolean Boolean2)

// ---- creation / identity ----
public virtual System.Void Initialize(Il2CppFishNet.Connection.NetworkConnection conn, System.String firstName, System.String lastName, System.String id, System.String guid, System.String propertyID, System.Boolean male, System.Int32 appearanceIndex)
public virtual System.Void InitializeInfo(System.String firstName, System.String lastName, System.String id)
public virtual System.Void InitializeAppearance(System.Boolean male, System.Int32 index)

// ---- property assignment / transfer / firing ----
public virtual System.Void AssignProperty(Il2CppScheduleOne.Property.Property prop, System.Boolean warp)
public virtual System.Void UnassignProperty()
public System.Void TransferToProperty(System.String code)
public virtual System.Void TransferToProperty(Il2CppScheduleOne.Property.Property prop)
public System.Void SendTransfer(System.String propertyCode)
public virtual System.Void Fire()
public System.Void SendFire()
public System.Void ReceiveFire()
public System.Void LeavePropertyAndDespawn()
public virtual Il2CppScheduleOne.Employees.EmployeeHome GetHome()

// ---- work state ----
public System.Boolean CanWork()
public virtual System.Boolean IsAnyWorkInProgress()
public System.Void MarkIsWorking()                                    // <-- there is NO `SetIsWorking`. This is the real name.
public virtual System.Void UpdateBehaviour()
public virtual System.Boolean ShouldIdle()
public virtual System.Void SetIdle(System.Boolean idle)
public System.Void SetWaitOutside(System.Boolean wait)
public virtual System.Boolean GetWorkIssue(out Il2CppScheduleOne.Dialogue.DialogueContainer& notWorkingReason)
public System.Void SubmitNoWorkReason(System.String reason, System.String fix, System.Int32 priority = 0)
public System.Void OnNotWorkingDialogue()
public System.Boolean ShouldShowNoWorkDialogue(System.Boolean enabled)
public System.Boolean ShouldShowFireDialogue(System.Boolean enabled)
public virtual System.Void CheckDialogueChoice(System.String choiceLabel)
public System.Void OnSleepEnd()

// ---- pay ----
public System.Boolean IsPayAvailable()
public System.Void SetIsPaid()
public System.Void RemoveDailyWage()

// ---- items / movement ----
public Il2CppScheduleOne.ItemFramework.ItemSlot GetFirstInventorySlotContainingProduct()
public virtual System.Boolean CanConsumeProduct()
public System.Void UpdateConsumeProduct()
public System.Void TradeItems()
public System.Void TradeItemsDone()
public System.Void SetDestination(Il2CppScheduleOne.Management.ITransitEntity transitEntity, System.Boolean teleportIfFail = True)
public System.Void SetDestination(UnityEngine.Vector3 position, System.Boolean teleportIfFail = True)
public virtual System.Void WalkCallback(Il2CppScheduleOne.NPCs.NPCMovement+WalkResult result)

// ---- save ----
public virtual Il2CppScheduleOne.Persistence.Datas.NPCData GetNPCData()
public virtual System.Boolean ShouldSave()
public virtual System.Void ResetConfiguration()

// ---- FishNet RPC groups (public entry point / RpcWriter / RpcLogic / RpcReader) ----
public virtual System.Void RpcLogic___Initialize_2260823878(Il2CppFishNet.Connection.NetworkConnection conn, System.String firstName, System.String lastName, System.String id, System.String guid, System.String propertyID, System.Boolean male, System.Int32 appearanceIndex)
public System.Void RpcWriter___Observers_Initialize_2260823878(Il2CppFishNet.Connection.NetworkConnection conn, System.String firstName, System.String lastName, System.String id, System.String guid, System.String propertyID, System.Boolean male, System.Int32 appearanceIndex)
public System.Void RpcWriter___Target_Initialize_2260823878(Il2CppFishNet.Connection.NetworkConnection conn, System.String firstName, System.String lastName, System.String id, System.String guid, System.String propertyID, System.Boolean male, System.Int32 appearanceIndex)
public System.Void RpcReader___Observers_Initialize_2260823878(Il2CppFishNet.Serializing.PooledReader PooledReader0, Il2CppFishNet.Transporting.Channel channel)
public System.Void RpcReader___Target_Initialize_2260823878(Il2CppFishNet.Serializing.PooledReader PooledReader0, Il2CppFishNet.Transporting.Channel channel)

public System.Void RpcLogic___SendTransfer_3615296227(System.String propertyCode)
public System.Void RpcWriter___Server_SendTransfer_3615296227(System.String propertyCode)
public System.Void RpcReader___Server_SendTransfer_3615296227(Il2CppFishNet.Serializing.PooledReader PooledReader0, Il2CppFishNet.Transporting.Channel channel, Il2CppFishNet.Connection.NetworkConnection conn)

public System.Void RpcLogic___TransferToProperty_3615296227(System.String code)
public System.Void RpcWriter___Observers_TransferToProperty_3615296227(System.String code)
public System.Void RpcReader___Observers_TransferToProperty_3615296227(Il2CppFishNet.Serializing.PooledReader PooledReader0, Il2CppFishNet.Transporting.Channel channel)

public System.Void RpcLogic___SendFire_2166136261()
public System.Void RpcWriter___Server_SendFire_2166136261()
public System.Void RpcReader___Server_SendFire_2166136261(Il2CppFishNet.Serializing.PooledReader PooledReader0, Il2CppFishNet.Transporting.Channel channel, Il2CppFishNet.Connection.NetworkConnection conn)

public System.Void RpcLogic___ReceiveFire_2166136261()
public System.Void RpcWriter___Observers_ReceiveFire_2166136261()
public System.Void RpcReader___Observers_ReceiveFire_2166136261(Il2CppFishNet.Serializing.PooledReader PooledReader0, Il2CppFishNet.Transporting.Channel channel)

public System.Void RpcLogic___SubmitNoWorkReason_15643032(System.String reason, System.String fix, System.Int32 priority = 0)
public System.Void RpcWriter___Observers_SubmitNoWorkReason_15643032(System.String reason, System.String fix, System.Int32 priority = 0)
public System.Void RpcReader___Observers_SubmitNoWorkReason_15643032(Il2CppFishNet.Serializing.PooledReader PooledReader0, Il2CppFishNet.Transporting.Channel channel)

// ---- SyncVar plumbing ----
public System.Boolean sync___get_value__PaidForToday_k__BackingField()
public System.Void sync___set_value__PaidForToday_k__BackingField(System.Boolean value, System.Boolean asServer)

// ---- compiler-generated ----
public System.Void _Awake_b__53_0(System.Single newValue)
```

**`virtual` vs non-virtual (matters for Harmony + subclassing).** Virtual on `Employee`:
`AssignProperty`, `Awake`, `CanConsumeProduct`, `Fire`, `GetHome`, `GetNPCData`, `GetWorkIssue`, `Initialize`,
`InitializeAppearance`, `InitializeInfo`, `IsAnyWorkInProgress`, `OnDestroy`, `OnSpawnServer`, `OnStartServer`,
`OnTick`, `ResetConfiguration`, `SetIdle`, `ShouldIdle`, `ShouldSave`, `Start`, `TransferToProperty(Property)`,
`UnassignProperty`, `UpdateBehaviour`, `WalkCallback`, plus the `NetworkInitialize*` / `ReadSyncVar___*` /
`RpcLogic___Initialize_*` / `Method_Protected_Virtual_Void_0` set.
**Non-virtual** (cannot be overridden, must be Harmony-patched or shadowed):
`CanWork`, `MarkIsWorking`, `SetWaitOutside`, `SetIsPaid`, `IsPayAvailable`, `RemoveDailyWage`,
`LeavePropertyAndDespawn`, `SendFire`, `ReceiveFire`, `SendTransfer`, `TransferToProperty(string)`,
`SubmitNoWorkReason`, `TradeItems`, `TradeItemsDone`, both `SetDestination` overloads,
`GetFirstInventorySlotContainingProduct`, `UpdateConsumeProduct`, `OnSleepEnd`, `OnNotWorkingDialogue`,
`ShouldShowFireDialogue`, `ShouldShowNoWorkDialogue`.

Static members: only `MAX_CONSECUTIVE_PATHING_FAILURES` (`System.Int32`). There are no static methods.

### 2.5 `Employee+NoWorkReason`

```csharp
public class Il2CppScheduleOne.Employees.Employee+NoWorkReason : Il2CppSystem.Object
    System.String Reason   { public get; public set; }
    System.String Fix      { public get; public set; }
    System.Int32  Priority { public get; public set; }
    public .ctor(System.String reason, System.String fix, System.Int32 priority)
```

---

## 3. The four subclasses (and the absence of a Driver)

> ### ⛔ **There is no `Driver`, `Handler`, `Courier`, `Deliverer` or `Transporter` class.**
> `research/raw/03-subclasses.txt` lists exactly **4** direct subclasses of
> `Il2CppScheduleOne.Employees.Employee`: `Botanist`, `Chemist`, `Cleaner`, `Packager`.
> `EEmployeeType.Handler = 1` is only the **enum name** for the `Packager` class — the game ships no
> Handler/Driver type, no `DriverConfiguration`, no `DriverConfigPanel`, no `DriverUIElement`,
> no `DriverData`, no `DriverLoader`. The driver mod must add all of these.

### 3.1 `Botanist` — grow containers, drying racks, mushroom spawn stations

`public class Il2CppScheduleOne.Employees.Botanist : Il2CppScheduleOne.Employees.Employee`
Also acts as an `Il2CppScheduleOne.Management.IConfigurable` (it exposes `Configuration`, `ConfigReplicator`,
`ConfigurableType`, `WorldspaceUI`, `CurrentPlayerConfigurer`, `TypeIcon`, `Transform`, `UIPoint`,
`CanBeSelected`, `ParentProperty`).

**Assigned objects:** `Il2CppScheduleOne.ObjectScripts.Pot`, `Il2CppScheduleOne.ObjectScripts.MushroomBed`
(both derive from `Il2CppScheduleOne.Growing.GrowContainer`), `Il2CppScheduleOne.ObjectScripts.DryingRack`,
`Il2CppScheduleOne.StationFramework.MushroomSpawnStation`, plus a **Supplies** container and a
`Il2CppScheduleOne.Employees.EmployeeHome` — all via `Il2CppScheduleOne.Management.BotanistConfiguration`.

**Behaviours it drives** (fields on the class, all `Il2CppScheduleOne.NPCs.Behaviour.*`):

```csharp
Il2CppScheduleOne.NPCs.Behaviour.StartDryingRackBehaviour _startDryingRackBehaviour { public get; public set; }
Il2CppScheduleOne.NPCs.Behaviour.StopDryingRackBehaviour _stopDryingRackBehaviour { public get; public set; }
Il2CppScheduleOne.NPCs.Behaviour.UseSpawnStationBehaviour _useSpawnStationBehaviour { public get; public set; }
Il2CppScheduleOne.NPCs.Behaviour.AddSoilToGrowContainerBehaviour _addSoilToGrowContainerBehaviour { public get; public set; }
Il2CppScheduleOne.NPCs.Behaviour.ApplyAdditiveToGrowContainerBehaviour _applyAdditiveToGrowContainerBehaviour { public get; public set; }
Il2CppScheduleOne.NPCs.Behaviour.SowSeedInPotBehaviour _sowSeedInPotBehaviour { public get; public set; }
Il2CppScheduleOne.NPCs.Behaviour.WaterPotBehaviour _waterPotBehaviour { public get; public set; }
Il2CppScheduleOne.NPCs.Behaviour.HarvestPotBehaviour _harvestPotBehaviour { public get; public set; }
Il2CppScheduleOne.NPCs.Behaviour.MistMushroomBedBehaviour _mistMushroomBedBehaviour { public get; public set; }
Il2CppScheduleOne.NPCs.Behaviour.HarvestMushroomBedBehaviour _harvestMushroomBedBehaviour { public get; public set; }
Il2CppScheduleOne.NPCs.Behaviour.ApplySpawnToMushroomBedBehaviour _applySpawnToMushroomBedBehaviour { public get; public set; }
Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.NPCs.Behaviour.Behaviour> _workBehaviours { public get; public set; }
```

**The method that performs its work:** `public virtual System.Void UpdateBehaviour()` — the per-tick
scheduler. `IsAnyWorkInProgress()` short-circuits it (both use lambdas over `_workBehaviours`:
`_IsAnyWorkInProgress_b__62_0(Behaviour b)`, `_UpdateBehaviour_b__63_0(Behaviour b)`).

Notable Botanist-only members:

```csharp
static System.Single CriticalWateringThreshold / WateringThreshold / MoistureLevelRandomMin / MoistureLevelRandomMax
static System.Single SoilPourTime / WaterPourTime / AdditivePourTime / SeedSowTime / IndividualHarvestTime / ApplySpawnTime
System.Int32 MaxAssignedPots { public get; public set; }        // instance, prefab-serialized
Il2CppScheduleOne.Management.BotanistConfiguration configuration { public get; public set; }
Il2CppScheduleOne.UI.Management.BotanistUIElement WorldspaceUIPrefab { public get; public set; }
Il2CppScheduleOne.Dialogue.DialogueContainer NoAssignedStationsDialogue / UnspecifiedPotsDialogue / NullDestinationPotsDialogue / MissingMaterialsDialogue / NoPotsRequireWorkDialogue

public System.Boolean CanMoveDryableToRack(out Il2CppScheduleOne.ItemFramework.QualityItemInstance& dryable, out Il2CppScheduleOne.ObjectScripts.DryingRack& destinationRack, out System.Int32& moveQuantity)
public Il2CppScheduleOne.ObjectScripts.DryingRack GetAssignedDryingRackFor(Il2CppScheduleOne.ItemFramework.QualityItemInstance dryable, out System.Int32& rackInputCapacity)
public Il2CppScheduleOne.ItemFramework.QualityItemInstance GetDryableInSupplies()
public Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ObjectScripts.MushroomBed> GetBedsReadyForSpawn()
public Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Growing.GrowContainer> GetGrowContainersForAdditives()
public Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Growing.GrowContainer> GetGrowContainersForSoilPour()
public Il2CppScheduleOne.ObjectScripts.MushroomBed GetMushroomBedForMisting(System.Single threshold)
public Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ObjectScripts.MushroomBed> GetMushroomBedsForHarvest()
public Il2CppScheduleOne.ObjectScripts.Pot GetPotForWatering(System.Single threshold)
public Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ObjectScripts.Pot> GetPotsForHarvest()
public Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ObjectScripts.Pot> GetPotsReadyForSeed()
public Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ObjectScripts.DryingRack> GetRacksReadyToMove()
public Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ObjectScripts.DryingRack> GetRacksToStart()
public Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ObjectScripts.DryingRack> GetRacksToStop()
public Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.StationFramework.MushroomSpawnStation> GetSpawnStationsReadyToMove()
public Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.StationFramework.MushroomSpawnStation> GetSpawnStationsReadyToUse()
public Il2CppScheduleOne.Management.ITransitEntity GetSuppliesAsTransitEntity()      // <-- supplies chest as a transit source
public System.Boolean IsEntityAccessible(Il2CppScheduleOne.Management.ITransitEntity entity)
public System.Void StartDryingRack(Il2CppScheduleOne.ObjectScripts.DryingRack rack)
public System.Void StopDryingRack(Il2CppScheduleOne.ObjectScripts.DryingRack rack)
public virtual Il2CppScheduleOne.Persistence.Datas.DynamicSaveData GetSaveData()
public virtual Il2CppSystem.Collections.Generic.List<System.String> WriteData(System.String parentFolderPath)
public virtual Il2CppScheduleOne.UI.Management.WorldspaceUIElement CreateWorldspaceUI()
public virtual System.Void DestroyWorldspaceUI()
public virtual System.Void SendConfigurationToClient(Il2CppFishNet.Connection.NetworkConnection conn)
public virtual System.Void SetConfigurer(Il2CppFishNet.Object.NetworkObject player)
public virtual System.Void RpcLogic___SetConfigurer_3323014238(Il2CppFishNet.Object.NetworkObject player)
public System.Void RpcWriter___Server_SetConfigurer_3323014238(Il2CppFishNet.Object.NetworkObject player)
public System.Void RpcReader___Server_SetConfigurer_3323014238(Il2CppFishNet.Serializing.PooledReader PooledReader0, Il2CppFishNet.Transporting.Channel channel, Il2CppFishNet.Connection.NetworkConnection conn)
public virtual System.Boolean ReadSyncVar___ScheduleOne_Employees_Botanist(Il2CppFishNet.Serializing.PooledReader PooledReader0, System.UInt32 UInt321, System.Boolean Boolean2)
```

`Il2CppScheduleOne.Management.ITransitEntity IsEntityAccessible(...)` and `GetSuppliesAsTransitEntity()`
are the Botanist's own hooks into the transit system — the driver mod can copy this pattern.

### 3.2 `Chemist` — chemistry stations, lab ovens, cauldrons, mixing stations

```csharp
public class Il2CppScheduleOne.Employees.Chemist : Il2CppScheduleOne.Employees.Employee

static System.Int32 MAX_ASSIGNED_STATIONS { public get; public set; }
Il2CppScheduleOne.NPCs.Behaviour.StartChemistryStationBehaviour StartChemistryStationBehaviour { public get; public set; }
Il2CppScheduleOne.NPCs.Behaviour.StartLabOvenBehaviour StartLabOvenBehaviour { public get; public set; }
Il2CppScheduleOne.NPCs.Behaviour.FinishLabOvenBehaviour FinishLabOvenBehaviour { public get; public set; }
Il2CppScheduleOne.NPCs.Behaviour.StartCauldronBehaviour StartCauldronBehaviour { public get; public set; }
Il2CppScheduleOne.NPCs.Behaviour.StartMixingStationBehaviour StartMixingStationBehaviour { public get; public set; }
Il2CppScheduleOne.Management.ChemistConfiguration configuration { public get; public set; }
Il2CppScheduleOne.UI.Management.ChemistUIElement WorldspaceUIPrefab { public get; public set; }

public System.Void TryStartNewTask()                              // <-- the work dispatcher
public virtual System.Void UpdateBehaviour()
public virtual System.Boolean IsAnyWorkInProgress()
public virtual System.Boolean ShouldIdle()
public System.Void StartChemistryStation(Il2CppScheduleOne.ObjectScripts.ChemistryStation station)
public System.Void StartCauldron(Il2CppScheduleOne.ObjectScripts.Cauldron cauldron)
public System.Void StartLabOven(Il2CppScheduleOne.ObjectScripts.LabOven oven)
public System.Void FinishLabOven(Il2CppScheduleOne.ObjectScripts.LabOven oven)
public System.Void StartMixingStation(Il2CppScheduleOne.ObjectScripts.MixingStation station)
public Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ObjectScripts.Cauldron> GetCauldronsReadyToMove()
public Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ObjectScripts.Cauldron> GetCauldronsReadyToStart()
public Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ObjectScripts.ChemistryStation> GetChemStationsReadyToMove()
public Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ObjectScripts.ChemistryStation> GetChemistryStationsReadyToStart()
public Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ObjectScripts.LabOven> GetLabOvensReadyToFinish()
public Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ObjectScripts.LabOven> GetLabOvensReadyToMove()
public Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ObjectScripts.LabOven> GetLabOvensReadyToStart()
public Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ObjectScripts.MixingStation> GetMixStationsReadyToMove()
public Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ObjectScripts.MixingStation> GetMixingStationsReadyToStart()
```

The `…ReadyToMove()` methods return stations whose outputs need transporting — the Chemist then drives the
inherited `MoveItemBehaviour` (inferred from naming + the `MoveItemData` in `ChemistData`).

### 3.3 `Cleaner` — trash

```csharp
public class Il2CppScheduleOne.Employees.Cleaner : Il2CppScheduleOne.Employees.Employee

static System.Int32 MAX_ASSIGNED_BINS { public get; public set; }
Il2CppScheduleOne.ObjectScripts.WateringCan.TrashGrabberDefinition TrashGrabberDef { public get; public set; }
Il2CppScheduleOne.ObjectScripts.WateringCan.TrashGrabberInstance trashGrabberInstance { public get; public set; }
Il2CppScheduleOne.NPCs.Behaviour.PickUpTrashBehaviour PickUpTrashBehaviour { public get; public set; }
Il2CppScheduleOne.NPCs.Behaviour.EmptyTrashGrabberBehaviour EmptyTrashGrabberBehaviour { public get; public set; }
Il2CppScheduleOne.NPCs.Behaviour.BagTrashCanBehaviour BagTrashCanBehaviour { public get; public set; }
Il2CppScheduleOne.NPCs.Behaviour.DisposeTrashBagBehaviour DisposeTrashBagBehaviour { public get; public set; }
Il2CppScheduleOne.Management.CleanerConfiguration configuration { public get; public set; }
Il2CppScheduleOne.UI.Management.CleanerUIElement WorldspaceUIPrefab { public get; public set; }

public System.Void TryStartNewTask()                              // <-- the work dispatcher
public virtual System.Void UpdateBehaviour()
public virtual System.Void SetIdle(System.Boolean idle)           // only subclass that overrides SetIdle
public System.Void EnsureTrashGrabberInInventory()
public System.Int32 GetTrashGrabberAmount()
public Il2CppScheduleOne.ObjectScripts.TrashContainerItem GetFirstNonFullBin(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppScheduleOne.ObjectScripts.TrashContainerItem> bins)
public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppScheduleOne.ObjectScripts.TrashContainerItem> GetTrashContainersOrderedByDistance()
```

Assigned objects: `Il2CppScheduleOne.ObjectScripts.TrashContainerItem` bins (via
`CleanerConfiguration.Bins` / `binItems`).

### 3.4 `Packager` — **the closest thing to a driver that already exists**

```csharp
public class Il2CppScheduleOne.Employees.Packager : Il2CppScheduleOne.Employees.Employee

Il2CppScheduleOne.NPCs.Behaviour.PackagingStationBehaviour PackagingBehaviour { public get; public set; }
Il2CppScheduleOne.NPCs.Behaviour.BrickPressBehaviour BrickPressBehaviour { public get; public set; }
System.Int32 MaxAssignedStations { public get; public set; }
System.Single PackagingSpeedMultiplier { public get; public set; }
Il2CppScheduleOne.Management.PackagerConfiguration configuration { public get; public set; }
Il2CppScheduleOne.UI.Management.PackagerUIElement WorldspaceUIPrefab { public get; public set; }

public virtual System.Void UpdateBehaviour()                                       // <-- work dispatcher
public virtual System.Boolean IsAnyWorkInProgress()
public virtual System.Boolean ShouldIdle()
public Il2CppScheduleOne.ObjectScripts.PackagingStation GetStationToAttend()
public Il2CppScheduleOne.ObjectScripts.PackagingStation GetStationMoveItems()
public Il2CppScheduleOne.ObjectScripts.BrickPress GetBrickPress()
public Il2CppScheduleOne.ObjectScripts.BrickPress GetBrickPressMoveItems()
public Il2CppScheduleOne.Management.AdvancedTransitRoute GetTransitRouteReady(out Il2CppScheduleOne.ItemFramework.ItemInstance& item)   // ★ route picker
public System.Void StartMoveItem(Il2CppScheduleOne.ObjectScripts.PackagingStation station)
public System.Void StartMoveItem(Il2CppScheduleOne.ObjectScripts.BrickPress press)
public System.Void StartPackaging(Il2CppScheduleOne.ObjectScripts.PackagingStation station)
public System.Void StartPress(Il2CppScheduleOne.ObjectScripts.BrickPress press)
```

`Packager` is the **only** employee whose configuration owns a user-editable list of routes
(`PackagerConfiguration.Routes : RouteListField`, max `RouteListField.MaxRoutes`). `GetTransitRouteReady`
returns the first `AdvancedTransitRoute` that has an item ready to move plus the item template. **This is the
exact template the driver mod should copy.**

---

## 4. Hiring / management

### 4.1 `EmployeeManager` (the real name — verified)

`public class Il2CppScheduleOne.Employees.EmployeeManager : Il2CppScheduleOne.DevUtilities.NetworkSingleton<Il2CppScheduleOne.Employees.EmployeeManager>`
→ access via `NetworkSingleton<EmployeeManager>.Instance` (inferred from the base type name).

```csharp
static System.Single MALE_EMPLOYEE_CHANCE { public get; public set; }
Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Employees.Employee> AllEmployees { public get; public set; }
Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppScheduleOne.Quests.Quest_Employees> EmployeeQuests { public get; public set; }
Il2CppScheduleOne.Employees.Botanist BotanistPrefab { public get; public set; }
Il2CppScheduleOne.Employees.Packager PackagerPrefab { public get; public set; }
Il2CppScheduleOne.Employees.Chemist  ChemistPrefab  { public get; public set; }
Il2CppScheduleOne.Employees.Cleaner  CleanerPrefab  { public get; public set; }
Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Employees.EmployeeManager+EmployeeAppearance> MaleAppearances { public get; public set; }
Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Employees.EmployeeManager+EmployeeAppearance> FemaleAppearances { public get; public set; }
Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppScheduleOne.VoiceOver.VODatabase> MaleVoices / FemaleVoices { public get; public set; }
Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray MaleFirstNames / FemaleFirstNames / LastNames { public get; public set; }
Il2CppSystem.Collections.Generic.List<System.String> takenNames { public get; public set; }
Il2CppSystem.Collections.Generic.List<System.Int32> takenMaleAppearances / takenFemaleAppearances { public get; public set; }

public System.Void CreateNewEmployee(Il2CppScheduleOne.Property.Property property, Il2CppScheduleOne.Employees.EEmployeeType type)
public System.Void CreateEmployee(Il2CppScheduleOne.Property.Property property, Il2CppScheduleOne.Employees.EEmployeeType type, System.String firstName, System.String lastName, System.String id, System.Boolean male, System.Int32 appearanceIndex, UnityEngine.Vector3 position, UnityEngine.Quaternion rotation, System.String guid = "")
public Il2CppScheduleOne.Employees.Employee CreateEmployee_Server(Il2CppScheduleOne.Property.Property property, Il2CppScheduleOne.Employees.EEmployeeType type, System.String firstName, System.String lastName, System.String id, System.Boolean male, System.Int32 appearanceIndex, UnityEngine.Vector3 position, UnityEngine.Quaternion rotation, System.String guid)
public Il2CppScheduleOne.Employees.Employee GetEmployeePrefab(Il2CppScheduleOne.Employees.EEmployeeType type)
public Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Employees.Employee> GetEmployeesByType(Il2CppScheduleOne.Employees.EEmployeeType type)
public System.Void GenerateRandomName(System.Boolean male, out System.String& firstName, out System.String& lastName)
public Il2CppScheduleOne.Employees.EmployeeManager+EmployeeAppearance GetAppearance(System.Boolean male, System.Int32 index)
public System.Void GetRandomAppearance(System.Boolean male, out System.Int32& index, out Il2CppScheduleOne.AvatarFramework.AvatarSettings& settings)
public Il2CppScheduleOne.VoiceOver.VODatabase GetVoice(System.Boolean male, System.Int32 index)
public System.Void RegisterName(System.String name)
public System.Void RegisterAppearance(System.Boolean male, System.Int32 index)
public System.Boolean IsFloatValid(System.Single value)
public System.Boolean IsPositionValid(UnityEngine.Vector3 position)
public System.Boolean IsRotationValid(UnityEngine.Quaternion rotation)
public virtual System.Void Awake()

// RPC group for CreateEmployee (ServerRpc):
public System.Void RpcWriter___Server_CreateEmployee_311954683(… same args …)
public System.Void RpcLogic___CreateEmployee_311954683(… same args …)   // ★ real body
public System.Void RpcReader___Server_CreateEmployee_311954683(Il2CppFishNet.Serializing.PooledReader PooledReader0, Il2CppFishNet.Transporting.Channel channel, Il2CppFishNet.Connection.NetworkConnection conn)
```

Flow (inferred from the RPC shape + naming):
`CreateNewEmployee(property, type)` → rolls name/appearance/gender → `CreateEmployee(...)` (ServerRpc) →
`RpcLogic___CreateEmployee_311954683` on the server → `CreateEmployee_Server(...)` instantiates
`GetEmployeePrefab(type)`, spawns the `NetworkObject`, calls `Employee.Initialize(...)` and
`Employee.AssignProperty(property, warp)`, and updates `EmployeeQuests`
(`_CreateEmployee_Server_b__0(Quest_Employees x)` filters the quest list by `type`).

`EmployeeManager+EmployeeAppearance`:
```csharp
Il2CppScheduleOne.AvatarFramework.AvatarSettings Settings { public get; public set; }
UnityEngine.Sprite Mugshot { public get; public set; }
```

### 4.2 Hiring cost & wages

* **Signing fee** = `Employee.SigningFee` (instance `System.Single`, prefab-serialized).
* **Daily wage** = `Employee.DailyWage` (instance `System.Single`, prefab-serialized).
* Payment is taken from the employee's **bed/home storage cash**, not the player's balance:
  `EmployeeHome.GetCashSum()` / `EmployeeHome.RemoveCash(System.Single amount)`, driven by
  `Employee.IsPayAvailable()` → `Employee.SetIsPaid()` → `Employee.RemoveDailyWage()`
  (inferred from the method set; the exact call order is **UNVERIFIED** — no IL body in the dumps).
* `Employee.PaidForToday` is a SyncVar that presumably resets on sleep (`Employee.OnSleepEnd()` exists,
  and `Quest_Employees.PayEntry` / `AreAnyEmployeesPaid()` track it). **UNVERIFIED**: which method resets it.
* **UNVERIFIED**: the numeric values of `SigningFee` / `DailyWage`. They are Unity-serialized prefab data,
  not IL2CPP metadata, so they are not recoverable from these dumps. Read at runtime.

### 4.3 `Il2CppScheduleOne.Management` configuration types

`EConfigurableType` (this is the enum a new employee type must extend — see §9):

```csharp
public enum Il2CppScheduleOne.Management.EConfigurableType {
    Pot = 0, PackagingStation = 1, LabOven = 2, Botanist = 3, Packager = 4,
    ChemistryStation = 5, Chemist = 6, Cauldron = 7, Cleaner = 8, BrickPress = 9,
    MixingStation = 10, DryingRack = 11, SpawnStation = 12, MushroomBed = 13, Storage = 14,
}
public static class Il2CppScheduleOne.Management.ConfigurableType {
    public static System.String GetTypeName(Il2CppScheduleOne.Management.EConfigurableType type)
}
```

`EntityConfiguration` (base of every `*Configuration`):

```csharp
public class Il2CppScheduleOne.Management.EntityConfiguration : Il2CppSystem.Object
    static System.Int32 NameCharacterLimit { public get; public set; }
    Il2CppScheduleOne.Management.ConfigurationReplicator Replicator { public get; public set; }
    Il2CppScheduleOne.Management.IConfigurable Configurable { public get; public set; }
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Management.ConfigField> Fields { public get; public set; }
    UnityEngine.Events.UnityEvent onChanged { public get; public set; }
    System.Boolean IsSelected { public get; public set; }
    Il2CppScheduleOne.Management.StringField Name { public get; public set; }

    public .ctor(Il2CppScheduleOne.Management.ConfigurationReplicator replicator, Il2CppScheduleOne.Management.IConfigurable configurable, System.String defaultName)
    public virtual System.Boolean AllowRename()
    public virtual System.Void Selected()
    public virtual System.Void Deselected()
    public virtual System.Void Destroy()
    public T GetField<T>()
    public virtual System.String GetSaveString()
    public System.Void InvokeChanged()
    public System.Void ReplicateAllFields(Il2CppFishNet.Connection.NetworkConnection conn = null, System.Boolean replicateDefaults = True)
    public System.Void ReplicateField(Il2CppScheduleOne.Management.ConfigField field, Il2CppFishNet.Connection.NetworkConnection conn = null)
    public virtual System.Void Reset()
    public virtual System.Boolean ShouldSave()
```

The four employee configurations (all `: EntityConfiguration`):

```csharp
public class BotanistConfiguration
    static Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppSystem.Type> AssignableTypes { public get; public set; }
    Il2CppScheduleOne.Management.ObjectField     Home     { public get; public set; }
    Il2CppScheduleOne.Management.ObjectField     Supplies { public get; public set; }
    Il2CppScheduleOne.Management.ObjectListField Assigns  { public get; public set; }
    List<Pot> AssignedPots; List<DryingRack> AssignedRacks; List<MushroomBed> AssignedBeds;
    List<MushroomSpawnStation> AssignedSpawnStations; EmployeeHome AssignedHome;
    public .ctor(ConfigurationReplicator replicator, IConfigurable configurable, Il2CppScheduleOne.Employees.Botanist _botanist)
    public System.Void AssignsChanged(Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.EntityFramework.BuildableItem> objects)
    public System.Void HomeChanged(Il2CppScheduleOne.EntityFramework.BuildableItem newItem)
    public System.Boolean IsStationValid(Il2CppScheduleOne.EntityFramework.BuildableItem obj, out System.String& reason)
    public Il2CppScheduleOne.Management.NPCField GetNPCField(Il2CppScheduleOne.Management.IConfigurable configurable)

public class ChemistConfiguration
    ObjectField Home; ObjectListField Stations;
    List<ChemistryStation> ChemStations; List<LabOven> LabOvens; List<Cauldron> Cauldrons; List<MixingStation> MixStations;
    System.Int32 TotalStations { public get; }
    public .ctor(ConfigurationReplicator replicator, IConfigurable configurable, Il2CppScheduleOne.Employees.Chemist _chemist)
    public System.Void AssignedStationsChanged(List<BuildableItem> objects)
    public System.Void HomeChanged(BuildableItem newItem)
    public System.Boolean IsStationValid(BuildableItem obj, out System.String& reason)

public class CleanerConfiguration
    ObjectField Home; ObjectListField Bins; List<TrashContainerItem> binItems; EmployeeHome assignedHome;
    public .ctor(ConfigurationReplicator replicator, IConfigurable configurable, Il2CppScheduleOne.Employees.Cleaner _cleaner)
    public System.Void AssignedBinsChanged(List<BuildableItem> objects)
    public System.Boolean IsObjValid(BuildableItem obj, out System.String& reason)

public class PackagerConfiguration                                        // ★ the model for a Driver config
    Il2CppScheduleOne.Management.ObjectField     Home    { public get; public set; }
    Il2CppScheduleOne.Management.ObjectListField Stations{ public get; public set; }
    Il2CppScheduleOne.Management.RouteListField  Routes  { public get; public set; }
    List<PackagingStation> AssignedStations; List<BrickPress> AssignedBrickPresses;
    System.Int32 AssignedStationCount { public get; }
    public .ctor(ConfigurationReplicator replicator, IConfigurable configurable, Il2CppScheduleOne.Employees.Packager _packager)
    public System.Void AssignedStationsChanged(List<BuildableItem> objects)
    public System.Void HomeChanged(BuildableItem newItem)
    public System.Boolean IsStationValid(BuildableItem obj, out System.String& reason)
```

`ConfigField` subclasses used by employees: `ObjectField`, `ObjectListField`, `RouteListField`, `NPCField`,
`StringField`, `NumberField`, `QualityField`, `ItemField`, `StationRecipeField`. Each has
`GetData()` / `Load(<XData>)` / `IsValueDefault()` and a `Set…(value, System.Boolean network)` setter.

`RouteListField` — the driver mod's route storage:
```csharp
public class Il2CppScheduleOne.Management.RouteListField : Il2CppScheduleOne.Management.ConfigField
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Management.AdvancedTransitRoute> Routes { public get; public set; }
    System.Int32 MaxRoutes { public get; public set; }
    UnityEngine.Events.UnityEvent<Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Management.AdvancedTransitRoute>> onListChanged { public get; public set; }
    public .ctor(Il2CppScheduleOne.Management.EntityConfiguration parentConfig)
    public System.Void AddItem(Il2CppScheduleOne.Management.AdvancedTransitRoute item)
    public System.Void RemoveItem(Il2CppScheduleOne.Management.AdvancedTransitRoute item)
    public System.Void SetList(Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Management.AdvancedTransitRoute> list, System.Boolean network, System.Boolean bypassSequenceCheck = False)
    public System.Void Replicate()
    public Il2CppScheduleOne.Persistence.Datas.RouteListData GetData()
    public System.Void Load(Il2CppScheduleOne.Persistence.Datas.RouteListData data)
    public virtual System.Boolean IsValueDefault()
```

`ConfigurationReplicator : Il2CppFishNet.Object.NetworkBehaviour` is the network transport for config fields.
Route-relevant members:
```csharp
public System.Void SendRouteListField(System.Int32 fieldIndex, Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppScheduleOne.Persistence.Datas.AdvancedTransitRouteData> value)   // ServerRpc entry
public System.Void RpcLogic___SendRouteListField_3226448297(System.Int32 fieldIndex, …)                 // server body
public System.Void ReceiveRouteListField(System.Int32 fieldIndex, …)                                     // ObserversRpc entry
public System.Void RpcLogic___ReceiveRouteListField_3226448297(System.Int32 fieldIndex, …)              // client body
public System.Void ReplicateField(Il2CppScheduleOne.Management.ConfigField field, Il2CppFishNet.Connection.NetworkConnection conn = null)
```
(equivalent `Send…`/`Receive…` pairs exist for Item, NPC, Number, Object, ObjectList, Quality, Recipe, String).

`IConfigurable` (Il2CppInterop emits it as a class because the original interface has default members):
```csharp
public class Il2CppScheduleOne.Management.IConfigurable : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
    EntityConfiguration Configuration { get; }        ConfigurationReplicator ConfigReplicator { get; }
    EConfigurableType ConfigurableType { get; }       WorldspaceUIElement WorldspaceUI { get; set; }
    NetworkObject CurrentPlayerConfigurer { get; set; } System.Boolean IsBeingConfiguredByOtherPlayer { get; }
    UnityEngine.Sprite TypeIcon { get; }              UnityEngine.Transform Transform { get; }
    UnityEngine.Transform UIPoint { get; }            System.Boolean IsDestroyed { get; }
    System.Boolean CanBeSelected { get; }             Il2CppScheduleOne.Property.Property ParentProperty { get; }
    public virtual WorldspaceUIElement CreateWorldspaceUI()
    public virtual System.Void DestroyWorldspaceUI()
    public virtual System.Void Selected() / Deselected() / ShowOutline(UnityEngine.Color color) / HideOutline()
    public virtual System.Void SendConfigurationToClient(Il2CppFishNet.Connection.NetworkConnection conn)
    public virtual System.Void SetConfigurer(Il2CppFishNet.Object.NetworkObject player)
```

### 4.4 Assigning an employee to a property / station

* **Property:** `Employee.AssignProperty(Property prop, System.Boolean warp)` /
  `Employee.UnassignProperty()`, mirrored by
  `Il2CppScheduleOne.Property.Property.RegisterEmployee(Employee emp) : System.Int32` (returns the
  `EmployeeIndex`) and `Property.DeregisterEmployee(Employee emp)`.
  `Property.Employees : List<Employee>`, `Property.EmployeeCapacity : System.Int32`,
  `Property.EmployeeContainer : UnityEngine.Transform`,
  `Property.EmployeeIdlePoints : Il2CppReferenceArray<UnityEngine.Transform>`,
  `Property.NPCSpawnPoint : UnityEngine.Transform`.
  Transfer at runtime: `Employee.SendTransfer(System.String propertyCode)` (client → server) →
  `Employee.TransferToProperty(System.String code)` (server → observers) →
  `Employee.TransferToProperty(Property prop)`.
* **Stations/objects:** always through the configuration's `ObjectField` / `ObjectListField`:
  `ObjectField.SetObject(BuildableItem obj, System.Boolean network)`,
  `ObjectListField.SetList(List<BuildableItem> list, System.Boolean network)` /
  `.AddItem(BuildableItem)` / `.RemoveItem(BuildableItem)`.
  Reverse direction (station → worker) is `NPCField.SetNPC(Il2CppScheduleOne.NPCs.NPC npc, System.Boolean network)`
  on e.g. `PackagingStationConfiguration.AssignedPackager`, `PotConfiguration.AssignedBotanist`.
* **Employee limit** message string in metadata: `"Employee limit reached ("`.

### 4.5 Console command `addemployee`

```csharp
public class Il2CppScheduleOne.Console+AddEmployeeCommand : Il2CppScheduleOne.Console+ConsoleCommand
    System.String CommandWord        { public get; }
    System.String CommandDescription { public get; }
    System.String ExampleUsage       { public get; }
    public virtual System.Void Execute(Il2CppSystem.Collections.Generic.List<System.String> args)

public sealed class Il2CppScheduleOne.Console+AddEmployeeCommand+__c__DisplayClass6_0
    System.String code { public get; public set; }                       // <-- the property code arg
    public System.Boolean _Execute_b__0(Il2CppScheduleOne.Property.Property x)   // finds Property by code
```

Registered as `ScheduleOne.Console|AddEmployeeCommand` in the metadata type table.

**String literals recovered directly from `global-metadata.dat`** (the pre-generated
`research/raw/literals-*.txt` files are corrupted — they contain raw binary, not text — so these were
re-extracted read-only from
`…\Schedule I_Data\il2cpp_data\Metadata\global-metadata.dat`):

| Member | Literal |
|---|---|
| `CommandWord` | `addemployee` |
| `CommandDescription` | `Adds an employee of the specified type to the given property.` |
| `ExampleUsage` | `addemployee botanist barn` |

Related error literals in the same blob:
`Employee type not found: `, `Unrecognized employee type '`, `Failed to recognize employee type: `,
`Employee limit reached (`, `Failed to cast employee to botanist`, `…to chemist`, `…to packager`,
`Failed to cast employee to Cleaner`.

So usage is `addemployee <type> <propertyCode>`. The **type token is parsed against `EEmployeeType`**
(inferred from `Unrecognized employee type '`), i.e. the valid tokens are
**`botanist`, `handler`, `chemist`, `cleaner`** — note it is `handler`, **not** `packager`.
**UNVERIFIED:** whether the parse is case-insensitive `Enum.TryParse` or a hand-written switch; the IL body
is not in the dumps. A driver mod that adds a new `EEmployeeType` value would need this command patched too.

---

## 5. THE BEHAVIOUR STATE MACHINE — `Il2CppScheduleOne.NPCs.Behaviour`

### 5.1 `NPCBehaviour` — the per-NPC controller

`public class Il2CppScheduleOne.NPCs.Behaviour.NPCBehaviour : Il2CppFishNet.Object.NetworkBehaviour`
Reachable from any NPC via `Il2CppScheduleOne.NPCs.NPC.Behaviour`.

```csharp
// ---- properties (25) ----
System.Boolean DEBUG_MODE { public get; public set; }
Il2CppScheduleOne.NPCs.NPCScheduleManager ScheduleManager { public get; public set; }
Il2CppScheduleOne.NPCs.Behaviour.CoweringBehaviour CoweringBehaviour { public get; public set; }
Il2CppScheduleOne.NPCs.Behaviour.RagdollBehaviour RagdollBehaviour { public get; public set; }
Il2CppScheduleOne.NPCs.Behaviour.CallPoliceBehaviour CallPoliceBehaviour { public get; public set; }
Il2CppScheduleOne.NPCs.Behaviour.GenericDialogueBehaviour GenericDialogueBehaviour { public get; public set; }
Il2CppScheduleOne.NPCs.Behaviour.HeavyFlinchBehaviour HeavyFlinchBehaviour { public get; public set; }
Il2CppScheduleOne.NPCs.Behaviour.FaceTargetBehaviour FaceTargetBehaviour { public get; public set; }
Il2CppScheduleOne.NPCs.Behaviour.DeadBehaviour DeadBehaviour { public get; public set; }
Il2CppScheduleOne.NPCs.Behaviour.UnconsciousBehaviour UnconsciousBehaviour { public get; public set; }
Il2CppScheduleOne.NPCs.Behaviour.Behaviour SummonBehaviour { public get; public set; }
Il2CppScheduleOne.NPCs.Behaviour.ConsumeProductBehaviour ConsumeProductBehaviour { public get; public set; }
Il2CppScheduleOne.Combat.CombatBehaviour CombatBehaviour { public get; public set; }
Il2CppScheduleOne.NPCs.Behaviour.FleeBehaviour FleeBehaviour { public get; public set; }
Il2CppScheduleOne.NPCs.Behaviour.StationaryBehaviour StationaryBehaviour { public get; public set; }
Il2CppScheduleOne.NPCs.Behaviour.RequestProductBehaviour RequestProductBehaviour { public get; public set; }
Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.NPCs.Behaviour.Behaviour> behaviourStack { public get; public set; }
Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.NPCs.Behaviour.Behaviour> enabledBehaviours { public get; public set; }
Il2CppScheduleOne.NPCs.Behaviour.Behaviour activeBehaviour { public get; public set; }   // ★ the current behaviour
Il2CppScheduleOne.NPCs.NPC Npc { public get; public set; }
System.Boolean field_Private_Boolean_0 / field_Private_Boolean_1 { public get; public set; }   // interop fallbacks
```

> ⚠️ The property is **`activeBehaviour`** (lower-case `a`), *not* `ActiveBehaviour`.
> The backing field is `_activeBehaviour_k__BackingField`.

```csharp
// ---- registration / query ----
public System.Void AddEnabledBehaviour(Il2CppScheduleOne.NPCs.Behaviour.Behaviour b)
public System.Void RemoveEnabledBehaviour(Il2CppScheduleOne.NPCs.Behaviour.Behaviour b)
public System.Void SortBehaviourStack()
public Il2CppScheduleOne.NPCs.Behaviour.Behaviour GetEnabledBehaviour()
public Il2CppScheduleOne.NPCs.Behaviour.Behaviour GetBehaviour(System.String BehaviourName)
public T GetBehaviour<T>()

// ---- ticking / Unity ----
public virtual System.Void Awake()
public virtual System.Void Start()
public virtual System.Void Update()
public virtual System.Void LateUpdate()
public virtual System.Void OnTick()
public virtual System.Void OnUncappedMinutePass()
public virtual System.Void OnValidate()
public System.Void OnDestroy()
public virtual System.Void OnDie()
public System.Void OnKnockOut()
public System.Void OnRevive()
public virtual System.Void OnSpawnServer(Il2CppFishNet.Connection.NetworkConnection connection)
public virtual System.Void Method_Protected_Virtual_New_Void_0()                     // interop fallback, unstable
public System.Void Method_Private_Void_NetworkConnection_PDM_0(Il2CppFishNet.Connection.NetworkConnection conn)

// ---- misc ----
public System.Void ConsumeProduct(Il2CppScheduleOne.Product.ProductItemInstance product, System.Boolean removeFromInventory = False)
public System.Void Summon(System.String buildingGUID, System.Int32 doorIndex, System.Single duration)

// ---- ★ the 6 networked state transitions, each a ServerRpc + ObserversRpc/TargetRpc pair ----
public System.Void EnableBehaviour_Server(System.Int32 behaviourIndex)
public System.Void EnableBehaviour_Client(Il2CppFishNet.Connection.NetworkConnection conn, System.Int32 behaviourIndex)
public System.Void DisableBehaviour_Server(System.Int32 behaviourIndex)
public System.Void DisableBehaviour_Client(Il2CppFishNet.Connection.NetworkConnection conn, System.Int32 behaviourIndex)
public System.Void ActivateBehaviour_Server(System.Int32 behaviourIndex)
public System.Void ActivateBehaviour_Client(Il2CppFishNet.Connection.NetworkConnection conn, System.Int32 behaviourIndex)
public System.Void DeactivateBehaviour_Server(System.Int32 behaviourIndex)
public System.Void DeactivateBehaviour_Client(Il2CppFishNet.Connection.NetworkConnection conn, System.Int32 behaviourIndex)
public System.Void PauseBehaviour_Server(System.Int32 behaviourIndex)
public System.Void PauseBehaviour_Client(Il2CppFishNet.Connection.NetworkConnection conn, System.Int32 behaviourIndex)
public System.Void ResumeBehaviour_Server(System.Int32 behaviourIndex)
public System.Void ResumeBehaviour_Client(Il2CppFishNet.Connection.NetworkConnection conn, System.Int32 behaviourIndex)

// bodies (patch these):
RpcLogic___EnableBehaviour_Server_3316948804(System.Int32 behaviourIndex)
RpcLogic___EnableBehaviour_Client_2681120339(Il2CppFishNet.Connection.NetworkConnection conn, System.Int32 behaviourIndex)
… identical shape for Disable / Activate / Deactivate / Pause / Resume …
RpcLogic___ConsumeProduct_3964170259(Il2CppScheduleOne.Product.ProductItemInstance product, System.Boolean removeFromInventory = False)
RpcLogic___Summon_900355577(System.String buildingGUID, System.Int32 doorIndex, System.Single duration)
```

**How selection works** (verified structure + inferred semantics):

1. Every `Behaviour` component sitting under the NPC registers itself into `behaviourStack` and is given a
   `BehaviourIndex` (the index used by every RPC above).
2. `SortBehaviourStack()` orders `behaviourStack` by `Behaviour.Priority`
   (`_SortBehaviourStack_b__43_0(Behaviour x) : System.Int32` is the key selector — *inferred* that the key is
   `Priority`).
3. `Behaviour.Enable()` → `NPCBehaviour.AddEnabledBehaviour(b)` inserts into `enabledBehaviours`, again
   ordered by an `System.Int32` key (`_AddEnabledBehaviour_b__45_0`); `Disable()` →
   `RemoveEnabledBehaviour(b)` (`_RemoveEnabledBehaviour_b__46_0`).
4. `Update()` picks `GetEnabledBehaviour()` (highest-priority enabled behaviour) and, if it differs from
   `activeBehaviour`, deactivates the old one and activates the new one. `_Update_b__39_0(Behaviour x) : System.String`
   is a name projection used for the debug label.
5. `OnTick()` forwards to `activeBehaviour.OnActiveTick()`; `Update()`/`LateUpdate()` forward to
   `BehaviourUpdate()`/`BehaviourLateUpdate()`; `OnUncappedMinutePass()` forwards to
   `OnActiveUncappedMinutePass()`. (Inferred from the matching method names.)

### 5.2 `Behaviour` — the base class

`public class Il2CppScheduleOne.NPCs.Behaviour.Behaviour : Il2CppFishNet.Object.NetworkBehaviour`

```csharp
// ---- properties (22) ----
static System.Int32 MAX_CONSECUTIVE_PATHING_FAILURES { public get; public set; }
System.Boolean EnabledOnAwake { public get; public set; }
System.Boolean _Enabled_k__BackingField { public get; public set; }
System.String Name { public get; public set; }
System.Int32 Priority { public get; public set; }                 // ★ selection key
System.Boolean _canUseUmbrellaDuringBehaviour { public get; public set; }
System.Boolean _Started_k__BackingField { public get; public set; }
System.Boolean _Active_k__BackingField { public get; public set; }
System.Int32 BehaviourIndex { public get; public set; }           // ★ index used by every NPCBehaviour RPC
Il2CppScheduleOne.NPCs.Behaviour.NPCBehaviour _beh_k__BackingField { public get; public set; }
UnityEngine.Events.UnityEvent onEnable  { public get; public set; }
UnityEngine.Events.UnityEvent onDisable { public get; public set; }
UnityEngine.Events.UnityEvent onBegin   { public get; public set; }
UnityEngine.Events.UnityEvent onEnd     { public get; public set; }
System.Int32 consecutivePathingFailures { public get; public set; }
System.Boolean field_Private_Boolean_0 / field_Private_Boolean_1 { public get; public set; }   // interop fallbacks
System.Boolean Enabled { public get; public set; }
System.Boolean Started { public get; public set; }
System.Boolean Active  { public get; public set; }
Il2CppScheduleOne.NPCs.Behaviour.NPCBehaviour beh { public get; public set; }
Il2CppScheduleOne.NPCs.NPC Npc { public get; }

// ---- methods (29) ----
public virtual System.Void Awake()
public virtual System.Void Enable()
public System.Void Enable_Server()
public System.Void Enable_Networked()
public virtual System.Void Disable()
public System.Void Disable_Server()
public System.Void Disable_Networked(Il2CppFishNet.Connection.NetworkConnection conn)
public virtual System.Void Activate()
public System.Void Activate_Server(Il2CppFishNet.Connection.NetworkConnection conn)
public virtual System.Void Deactivate()
public System.Void Deactivate_Server()
public System.Void Deactivate_Networked(Il2CppFishNet.Connection.NetworkConnection conn)
public virtual System.Void Pause()
public System.Void Pause_Server()
public virtual System.Void Resume()
public System.Void Resume_Server()
public virtual System.Void BehaviourUpdate()
public virtual System.Void BehaviourLateUpdate()
public virtual System.Void OnActiveTick()
public virtual System.Void OnActiveUncappedMinutePass()
public System.Void SetCanUseUmbrellaDuringBehaviour(System.Boolean canUse)
public System.Void SetDestination(Il2CppScheduleOne.Management.ITransitEntity transitEntity, System.Boolean teleportIfFail = True)
public virtual System.Void SetDestination(UnityEngine.Vector3 position, System.Boolean teleportIfFail = True, System.Single successThreshold = 1)
public virtual System.Void WalkCallback(Il2CppScheduleOne.NPCs.NPCMovement+WalkResult result)
public System.Void UpdateGameObjectName()
public virtual System.Void Method_Protected_Virtual_New_Void_0()   // interop fallback, unstable
public virtual System.Void NetworkInitializeIfDisabled() / NetworkInitialize__Late() / NetworkInitialize___Early()
```

> ### ✋ Corrections to the assumed API
> The following members **do not exist** on `Behaviour` in this build:
> `Begin`, `End`, `ActiveThisFrame`, `SendEnable`, `SendDisable`, and there are **no FishNet RPC triples on
> `Behaviour` at all** (no `RpcWriter___*` / `RpcLogic___*` / `RpcReader___*` methods).
> The real equivalents are:
> * `Begin`/`End` → the `onBegin` / `onEnd` `UnityEvent`s plus `Activate()` / `Deactivate()`.
> * `ActiveThisFrame` → `Active` (`System.Boolean`).
> * `SendEnable`/`SendDisable` → `Enable_Networked()` / `Disable_Networked(NetworkConnection conn)`, which
>   forward to the RPCs on **`NPCBehaviour`** (`EnableBehaviour_Server(int)` / `EnableBehaviour_Client(conn, int)`
>   etc.) using this behaviour's `BehaviourIndex` (inferred — the naming and the index parameter make this
>   unambiguous, but the IL body is not in the dumps).
>
> Naming convention across the base class: **`_Server` = call on the server**, **`_Networked` = replicate to
> clients**, plain (`Enable`, `Disable`, `Activate`, `Deactivate`, `Pause`, `Resume`) = local, virtual, the
> ones you override.

### 5.3 All 42 direct `Behaviour` subclasses (from `03-subclasses.txt`)

| # | Type | One-line purpose |
|---|---|---|
| 1 | `Il2CppScheduleOne.Combat.CombatBehaviour` | melee/ranged combat root (**outside** the Behaviour namespace); base of `PursuitBehaviour` |
| 2 | `BagTrashCanBehaviour` | Cleaner: bag a full `TrashContainerItem` |
| 3 | `BodySearchBehaviour` | police: stop-and-search the player's inventory |
| 4 | `BrickPressBehaviour` | Packager: operate a `BrickPress` |
| 5 | `CallPoliceBehaviour` | civilian: phone the police about a `Crime` |
| 6 | `CheckpointBehaviour` | police: man a `RoadCheckpoint`, search vehicles |
| 7 | `ConsumeProductBehaviour` | NPC consumes a `ProductItemInstance` (weed/meth/coke/shrooms) |
| 8 | `CoweringBehaviour` | cower when threatened |
| 9 | `CustomerAttendDealBehaviour` | customer walks to the deal `DeliveryLocation` |
| 10 | `DeadBehaviour` | corpse state / medical-centre transfer |
| 11 | `DealerAttendDealBehaviour` | dealer walks to a customer and runs the handover |
| 12 | `DisposeTrashBagBehaviour` | Cleaner: grab a `TrashBag` and drop it in the disposal area |
| 13 | `EmptyTrashGrabberBehaviour` | Cleaner: empty the trash grabber into a bin |
| 14 | `FaceTargetBehaviour` | turn to face a player or a world position |
| 15 | `FinishLabOvenBehaviour` | Chemist: collect a finished `LabOven` batch |
| 16 | `FleeBehaviour` | run away from an entity or a point |
| 17 | `FootPatrolBehaviour` | police: walk a `FootPatrolRoute` as a `PatrolGroup` |
| 18 | `GenericDialogueBehaviour` | stand and talk to a player |
| 19 | `GraffitiBehaviour` | cartel NPC sprays a `WorldSpraySurface` |
| 20 | `GrowContainerBehaviour` | **base** for all Botanist grow-container work (7 subclasses) |
| 21 | `HeavyFlinchBehaviour` | heavy hit reaction |
| 22 | `IdleBehaviour` | stand at an `IdlePoint` — used by `Employee.WaitOutside` |
| 23 | **`MoveItemBehaviour`** | **★ generic "take N of item X from A, carry it, put it in B"** |
| 24 | `PackagingStationBehaviour` | Packager: operate a `PackagingStation` |
| 25 | `PickUpTrashBehaviour` | Cleaner: pick up a loose `TrashItem` |
| 26 | `RagdollBehaviour` | ragdoll / seizure state |
| 27 | `RequestProductBehaviour` | customer approaches the player and asks to buy |
| 28 | `ScheduleBehaviour` | drives the NPC's `NPCScheduleManager` daily schedule |
| 29 | `SentryBehaviour` | police: stand a `SentryLocation` post |
| 30 | `SewerGoblinRetrieveBehaviour` | sewer goblin fetches an item from the player |
| 31 | `SmokeBreakBehaviour` | NPC takes a cigarette break at a location |
| 32 | `StartCauldronBehaviour` | Chemist: load and start a `Cauldron` |
| 33 | `StartChemistryStationBehaviour` | Chemist: run a `ChemistryStation` cook (beaker, stir, burner) |
| 34 | `StartDryingRackBehaviour` | Botanist: load and start a `DryingRack` |
| 35 | `StartLabOvenBehaviour` | Chemist: pour into and start a `LabOven` |
| 36 | `StartMixingStationBehaviour` | Chemist: insert product+mixer into a `MixingStation` |
| 37 | `StationaryBehaviour` | do nothing, stay put |
| 38 | `StopDryingRackBehaviour` | Botanist: unload a finished `DryingRack` |
| 39 | `UnconsciousBehaviour` | knocked-out state (snoring, particles) |
| 40 | `UseSpawnStationBehaviour` | Botanist: use a `MushroomSpawnStation` |
| 41 | **`VehiclePatrolBehaviour`** | **★ NPC drives a `LandVehicle` along a `VehiclePatrolRoute`** |
| 42 | **`VehiclePursuitBehaviour`** | **★ NPC drives a `LandVehicle` chasing a player** |

`GrowContainerBehaviour`'s own 7 subclasses (indirect, for completeness):
`AddSoilToGrowContainerBehaviour`, `ApplyAdditiveToGrowContainerBehaviour`, `ApplySpawnToMushroomBedBehaviour`,
`HarvestMushroomBedBehaviour`, `HarvestPotBehaviour`, `SowSeedInPotBehaviour`, `WaterPotBehaviour`
(and `MistMushroomBedBehaviour : WaterPotBehaviour`).

Non-`Behaviour` helper types in the namespace: `FootPatrolRoute`, `VehiclePatrolRoute`, `PatrolGroup`,
`PursuitBehaviour : Il2CppScheduleOne.Combat.CombatBehaviour`.

### 5.4 Full dumps — movement / item / vehicle / work behaviours

#### 5.4.1 `MoveItemBehaviour` — ★ the item courier

```csharp
public class Il2CppScheduleOne.NPCs.Behaviour.MoveItemBehaviour : Il2CppScheduleOne.NPCs.Behaviour.Behaviour

// ---- properties (14) ----
System.Boolean _Initialized_k__BackingField { public get; public set; }
Il2CppScheduleOne.Management.TransitRoute assignedRoute { public get; public set; }
Il2CppScheduleOne.ItemFramework.ItemInstance itemToRetrieveTemplate { public get; public set; }
System.Int32 grabbedAmount { public get; public set; }
System.Int32 maxMoveAmount { public get; public set; }
Il2CppScheduleOne.NPCs.Behaviour.MoveItemBehaviour+EState currentState { public get; public set; }
UnityEngine.Coroutine walkToSourceRoutine { public get; public set; }
UnityEngine.Coroutine grabRoutine { public get; public set; }
UnityEngine.Coroutine walkToDestinationRoutine { public get; public set; }
UnityEngine.Coroutine placingRoutine { public get; public set; }
System.Boolean skipPickup { public get; public set; }
System.Boolean field_Private_Boolean_0 / field_Private_Boolean_1 { public get; public set; }
System.Boolean Initialized { public get; public set; }

public enum MoveItemBehaviour+EState { Idle = 0, WalkingToSource = 1, Grabbing = 2, WalkingToDestination = 3, Placing = 4 }

// ---- methods (41) ----
public System.Void Initialize(Il2CppScheduleOne.Management.TransitRoute route, Il2CppScheduleOne.ItemFramework.ItemInstance _itemToRetrieveTemplate, System.Int32 _maxMoveAmount = -1, System.Boolean _skipPickup = False)
public System.Void Resume(Il2CppScheduleOne.Management.TransitRoute route, Il2CppScheduleOne.ItemFramework.ItemInstance _itemToRetrieveTemplate, System.Int32 _maxMoveAmount = -1)
public virtual System.Void Resume()
public virtual System.Void Pause()
public virtual System.Void Activate()
public virtual System.Void Deactivate()
public virtual System.Void Disable()
public virtual System.Void Awake()
public virtual System.Void OnActiveTick()

public System.Void StartTransit()
public System.Void EndTransit()
public System.Void StopCurrentActivity()
public System.Void WalkToSource()
public System.Void GrabItem()
public System.Void TakeItem()
public System.Void WalkToDestination()
public System.Void PlaceItem()
public System.Int32 GetAmountToGrab()

public System.Boolean IsAtSource()
public System.Boolean IsAtDestination()
public System.Boolean CanGetToSource(Il2CppScheduleOne.Management.TransitRoute route)
public System.Boolean CanGetToDestination(Il2CppScheduleOne.Management.TransitRoute route)
public UnityEngine.Transform GetSourceAccessPoint(Il2CppScheduleOne.Management.TransitRoute route)
public UnityEngine.Transform GetDestinationAccessPoint(Il2CppScheduleOne.Management.TransitRoute route)

public System.Boolean IsNpcInventoryItemValid(Il2CppScheduleOne.ItemFramework.ItemInstance item)
public System.Boolean IsDestinationValid(Il2CppScheduleOne.Management.TransitRoute route, Il2CppScheduleOne.ItemFramework.ItemInstance item)
public System.Boolean IsDestinationValid(Il2CppScheduleOne.Management.TransitRoute route, Il2CppScheduleOne.ItemFramework.ItemInstance item, out System.String& invalidReason)
public System.Boolean IsTransitRouteValid(Il2CppScheduleOne.Management.TransitRoute route, System.String itemID)
public System.Boolean IsTransitRouteValid(Il2CppScheduleOne.Management.TransitRoute route, System.String itemID, out System.String& invalidReason)
public System.Boolean IsTransitRouteValid(Il2CppScheduleOne.Management.TransitRoute route, Il2CppScheduleOne.ItemFramework.ItemInstance templateItem, out System.String& invalidReason)

public Il2CppScheduleOne.Persistence.Datas.MoveItemData GetSaveData()
public System.Void Load(Il2CppScheduleOne.Persistence.Datas.MoveItemData moveItemData)

public virtual System.Void NetworkInitializeIfDisabled() / NetworkInitialize__Late() / NetworkInitialize___Early()
public Il2CppSystem.Collections.IEnumerator Method_Private_IEnumerator_PDM_0()   // = <WalkToSource>g__Routine|26_0   (unstable interop name)
public Il2CppSystem.Collections.IEnumerator Method_Private_IEnumerator_PDM_1()   // = <GrabItem>g__Routine|27_0
public Il2CppSystem.Collections.IEnumerator Method_Private_IEnumerator_PDM_2()   // = <WalkToDestination>g__Routine|29_0
public Il2CppSystem.Collections.IEnumerator Method_Private_IEnumerator_PDM_3()   // = <PlaceItem>g__Routine|30_0
public System.Boolean _WalkToSource_b__26_1()
public System.Boolean _WalkToDestination_b__29_1()
```

> The four `Method_Private_IEnumerator_PDM_*` → routine mappings above are derived from the
> `ObfuscatedName` attributes on the generated state-machine classes
> (`MoveItemBehaviour+<<WalkToSource>g__Routine|26_0>d` etc.) **in dump order**; the exact PDM index ↔ routine
> pairing is **inferred**, not proven.
>
> Note there are **no RPCs on `MoveItemBehaviour`** — the whole transit runs server-side and is replicated
> only through the item-slot RPCs on the source/destination entities.

#### 5.4.2 `IdleBehaviour`

```csharp
public class Il2CppScheduleOne.NPCs.Behaviour.IdleBehaviour : Il2CppScheduleOne.NPCs.Behaviour.Behaviour
    UnityEngine.Transform IdlePoint { public get; public set; }
    System.Boolean facingDir { public get; public set; }
    public virtual System.Void Activate() / Deactivate() / Pause() / Resume() / Awake() / OnActiveTick()
    public System.Boolean IsAtIdleLocation()
```

#### 5.4.3 `VehiclePatrolBehaviour` — **the only "NPC drives to a destination" behaviour**

```csharp
public class Il2CppScheduleOne.NPCs.Behaviour.VehiclePatrolBehaviour : Il2CppScheduleOne.NPCs.Behaviour.Behaviour
    static System.Single MAX_CONSECUTIVE_PATHING_FAILURES { public get; public set; }
    static System.Single PROGRESSION_THRESHOLD { public get; public set; }
    System.Int32 CurrentWaypoint { public get; public set; }
    Il2CppScheduleOne.NPCs.Behaviour.VehiclePatrolRoute Route { public get; public set; }
    Il2CppScheduleOne.Vehicles.LandVehicle Vehicle { public get; public set; }
    System.Boolean aggressiveDrivingEnabled { public get; public set; }
    System.Int32 consecutivePathingFailures { public get; public set; }
    System.Boolean isDriving { public get; }
    Il2CppScheduleOne.Vehicles.AI.VehicleAgent Agent { public get; }

    public virtual System.Void Activate() / Deactivate() / Pause() / Resume() / Awake() / OnActiveTick()
    public System.Void DriveTo(UnityEngine.Vector3 location)                                          // ★
    public System.Void SetRoute(Il2CppScheduleOne.NPCs.Behaviour.VehiclePatrolRoute route)
    public System.Void StartPatrol()
    public System.Void NavigationCallback(Il2CppScheduleOne.Vehicles.AI.VehicleAgent+ENavigationResult status)
    public System.Boolean IsAsCloseAsPossible(UnityEngine.Vector3 pos, out UnityEngine.Vector3& closestPosition)
    public virtual System.Void Method_Protected_Virtual_Void_0()   // interop fallback, unstable

public class Il2CppScheduleOne.NPCs.Behaviour.VehiclePatrolRoute : UnityEngine.MonoBehaviour
    System.String RouteName { public get; public set; }
    Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<UnityEngine.Transform> Waypoints { public get; public set; }
    System.Int32 StartWaypointIndex { public get; public set; }
    public System.Void OnDrawGizmos()
```

#### 5.4.4 `VehiclePursuitBehaviour` — second (and last) vehicle driver

```csharp
public class Il2CppScheduleOne.NPCs.Behaviour.VehiclePursuitBehaviour : Il2CppScheduleOne.NPCs.Behaviour.Behaviour
    static System.Single RECENT_VISIBILITY_THRESHOLD / EXIT_VEHICLE_MAX_SPEED / CLOSE_ENOUGH_THRESHOLD
    static System.Single UPDATE_FREQUENCY / STATIONARY_THRESHOLD / TIME_STATIONARY_TO_EXIT
    Il2CppScheduleOne.PlayerScripts.Player Target { public get; public set; }
    UnityEngine.AnimationCurve RepathDistanceThresholdMap { public get; public set; }
    Il2CppScheduleOne.Vehicles.LandVehicle vehicle { public get; public set; }
    System.Boolean isDriving { public get; }
    Il2CppScheduleOne.Vehicles.AI.VehicleAgent Agent { public get; }
    UnityEngine.Vector3 currentDriveTarget { public get; public set; }

    public System.Void DriveTo(UnityEngine.Vector3 location)                                          // ★
    public System.Void StartPursuit()
    public System.Void UpdateDestination()
    public System.Void CheckExitVehicle()
    public System.Void SetAggressiveDriving(System.Boolean aggressive)
    public System.Void NavigationCallback(Il2CppScheduleOne.Vehicles.AI.VehicleAgent+ENavigationResult status)
    public System.Boolean IsAsCloseAsPossible(UnityEngine.Vector3 pos, out UnityEngine.Vector3& closestPosition)
    public virtual System.Void AssignTarget(Il2CppScheduleOne.PlayerScripts.Player target)
    public virtual System.Void Activate() / Deactivate() / Pause() / Resume() / Awake() / BehaviourUpdate() / FixedUpdate() / OnActiveTick()
    public System.Void NotifyServerTargetSeen()                       // ServerRpc → RpcLogic___NotifyServerTargetSeen_2166136261()
```

Driving is done through `Il2CppScheduleOne.Vehicles.AI.VehicleAgent`:
```csharp
public System.Void Navigate(UnityEngine.Vector3 location, Il2CppScheduleOne.Vehicles.AI.NavigationSettings settings = null, Il2CppScheduleOne.Vehicles.AI.VehicleAgent+NavigationCallback callback = null)
public System.Void StopNavigating()
public System.Void EndDriving()
public System.Void RecalculateNavigation()
System.Boolean AutoDriving { public get; public set; }
UnityEngine.Vector3 TargetLocation { public get; public set; }
Il2CppScheduleOne.Vehicles.AI.DriveFlags Flags { public get; public set; }
```
and NPC↔vehicle coupling:
```csharp
// on Il2CppScheduleOne.NPCs.NPC
public virtual System.Void EnterVehicle(Il2CppFishNet.Connection.NetworkConnection connection, Il2CppScheduleOne.Vehicles.LandVehicle veh)
public virtual System.Void ExitVehicle()
Il2CppScheduleOne.Vehicles.LandVehicle CurrentVehicle { public get; public set; }
System.Boolean IsInVehicle { public get; }
// on Il2CppScheduleOne.Vehicles.LandVehicle
public System.Void AddNPCOccupant(Il2CppScheduleOne.NPCs.NPC npc)
public System.Void RemoveNPCOccupant(Il2CppScheduleOne.NPCs.NPC npc)
Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppScheduleOne.NPCs.NPC> OccupantNPCs { public get; public set; }
public Il2CppScheduleOne.Vehicles.VehicleSeat GetFirstFreeSeat()
Il2CppScheduleOne.Vehicles.AI.VehicleAgent Agent { public get; public set; }
```

#### 5.4.5 `GrowContainerBehaviour` — the "walk to entity, grab supplies, do action" template

```csharp
public class Il2CppScheduleOne.NPCs.Behaviour.GrowContainerBehaviour : Il2CppScheduleOne.NPCs.Behaviour.Behaviour
    Il2CppScheduleOne.Growing.GrowContainer _growContainer { public get; public set; }
    Il2CppScheduleOne.NPCs.Behaviour.GrowContainerBehaviour+EState _currentState { public get; public set; }
    Il2CppScheduleOne.Employees.Botanist _botanist { public get; public set; }
    Il2CppScheduleOne.Management.BotanistConfiguration _botanistConfiguration { public get; }
    UnityEngine.Coroutine _walkRoutine / _grabRoutine / _performActionRoutine { public get; public set; }

public enum GrowContainerBehaviour+EState { Idle = 0, Walking = 1, GrabbingSupplies = 2, PerformingAction = 3 }

    public virtual System.Void AssignAndEnable(Il2CppScheduleOne.Growing.GrowContainer growContainer)
    public virtual System.Boolean AreTaskConditionsMetForContainer(Il2CppScheduleOne.Growing.GrowContainer container)
    public System.Void WalkTo(Il2CppScheduleOne.Management.ITransitEntity entity)                      // ★
    public System.Boolean IsAtGrowContainer()
    public System.Boolean IsAtSupplies()
    public System.Void GrabRequiredItemFromSupplies()
    public System.Void PerformAction()
    public virtual System.Void OnActionSuccess(Il2CppScheduleOne.ItemFramework.ItemInstance usedItem)
    public virtual System.Boolean CheckSuccess(Il2CppScheduleOne.ItemFramework.ItemInstance usedItem)
    public System.Boolean DoSuppliesContainRequiredItem(Il2CppScheduleOne.Growing.GrowContainer growContainer)
    public System.Boolean DoesBotanistHaveAccessToRequiredSupplies(Il2CppScheduleOne.Growing.GrowContainer container)
    public System.Boolean DoesTaskRequireItem(Il2CppScheduleOne.Growing.GrowContainer growContainer, out Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray& suitableItemIDs)
    public System.Boolean IsRequiredItemInInventory(Il2CppScheduleOne.Growing.GrowContainer growContainer)
    public Il2CppScheduleOne.ItemFramework.ItemSlot GetItemSlotContainingRequiredItem(Il2CppScheduleOne.ItemFramework.IItemSlotOwner itemSlotOwner, Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray suitableItemIDs)
    public Il2CppScheduleOne.ItemFramework.ItemSlot GetSuppliesSlotContainingRequiredItem(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray suitableItemIDs)
    public virtual Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray GetRequiredItemSuitableIDs(Il2CppScheduleOne.Growing.GrowContainer growContainer)
    public virtual System.Single GetActionDuration()
    public virtual System.String GetAnimationBool()
    public virtual Il2CppScheduleOne.AvatarFramework.Equipping.AvatarEquippable GetActionEquippable()
    public virtual UnityEngine.Vector3 GetGrowContainerLookPoint()
    public virtual Il2CppScheduleOne.Trash.TrashItem GetTrashPrefab(Il2CppScheduleOne.ItemFramework.ItemInstance usedItem)
    public virtual System.Void OnStartPerformAction() / OnStopPerformAction()
    public System.Void StopAllRoutines()
    public virtual System.Void Activate() / Deactivate() / Pause() / Resume() / Awake() / OnActiveTick()
```

#### 5.4.6 Station work behaviours (all follow the same 5-method contract)

`PackagingStationBehaviour`, `BrickPressBehaviour`, `StartCauldronBehaviour`, `StartDryingRackBehaviour`,
`StopDryingRackBehaviour`, `UseSpawnStationBehaviour` share this shape:

```csharp
// PackagingStationBehaviour (BrickPressBehaviour is identical with BrickPress/Press instead)
static System.Single BASE_PACKAGING_TIME { public get; public set; }
Il2CppScheduleOne.ObjectScripts.PackagingStation Station { public get; public set; }
System.Boolean PackagingInProgress { public get; public set; }
UnityEngine.Coroutine packagingRoutine { public get; public set; }
public System.Void AssignStation(Il2CppScheduleOne.ObjectScripts.PackagingStation station)
public System.Void GoToStation()
public System.Boolean IsAtStation()
public System.Boolean IsStationReady(Il2CppScheduleOne.ObjectScripts.PackagingStation station)
public System.Void StartPackaging()
public System.Void BeginPackaging()          // ObserversRpc → RpcLogic___BeginPackaging_2166136261()
public System.Void StopPackaging()
public virtual System.Void Activate() / Deactivate() / Disable() / Pause() / Resume() / Awake() / OnActiveTick()

// StartCauldronBehaviour / StartDryingRackBehaviour / StopDryingRackBehaviour
public System.Void AssignStation(Il2CppScheduleOne.ObjectScripts.Cauldron station)   // or AssignRack(DryingRack rack)
public System.Void StartWork()
public System.Void BeginCauldron()  /  public System.Void BeginAction()              // ObserversRpc
public System.Void StopCauldron()
static System.Single START_CAULDRON_TIME  /  static System.Single TIME_PER_ITEM

// UseSpawnStationBehaviour
static System.Single TaskDuration / ProximityThreshold ; static System.String AnimationBoolName
Il2CppScheduleOne.StationFramework.MushroomSpawnStation Station { public get; public set; }
public System.Void AssignStation(Il2CppScheduleOne.StationFramework.MushroomSpawnStation station)
public System.Void BeginWork()      // ObserversRpc → RpcLogic___BeginWork_2166136261()
public System.Void StopWork()

// StartChemistryStationBehaviour / StartLabOvenBehaviour / StartMixingStationBehaviour / FinishLabOvenBehaviour
public System.Void SetTargetStation(Il2CppScheduleOne.ObjectScripts.ChemistryStation station)   // / SetTargetOven(LabOven) / AssignStation(MixingStation)
public UnityEngine.Vector3 GetStationAccessPoint()
public System.Boolean IsAtStation()
public System.Boolean CanCookStart()          // FinishLabOvenBehaviour uses CanActionStart()
public System.Void StartCook() / StopCook()   // FinishLabOvenBehaviour uses StartAction() / StopAction()
```

#### 5.4.7 Cleaner behaviours

```csharp
// PickUpTrashBehaviour / EmptyTrashGrabberBehaviour / BagTrashCanBehaviour / DisposeTrashBagBehaviour
static System.Single ACTION_MAX_DISTANCE / GRAB_MAX_DISTANCE ; static System.String EQUIPPABLE_ASSET_PATH
System.String TRASH_BAG_ASSET_PATH { public get; public set; }        // DisposeTrashBagBehaviour, instance
Il2CppScheduleOne.Employees.Cleaner Cleaner { public get; }
public System.Void SetTargetTrash(Il2CppScheduleOne.Trash.TrashItem trash)
public System.Void SetTargetTrashCan(Il2CppScheduleOne.ObjectScripts.TrashContainerItem trashCan)
public System.Void SetTargetBag(Il2CppScheduleOne.Trash.TrashBag bag)
public System.Boolean AreActionConditionsMet(System.Boolean checkAccess)
public System.Void GoToTarget()
public System.Boolean IsAtDestination()
public System.Void StartAction() / StopAllActions()
public System.Void PerformAction()        // ObserversRpc → RpcLogic___PerformAction_2166136261()
public System.Void GrabTrash() / DropTrash()   // DisposeTrashBagBehaviour, both ObserversRpc
```

---

## 6. HOW AN EMPLOYEE MOVES ITEMS BETWEEN CONTAINERS

### 6.1 `ITransitEntity` — complete verified signature list

Il2CppInterop emits this as a **class** (`: Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase`), not a C#
interface, because the original `ScheduleOne.Management.ITransitEntity` is an interface with **default
implementations** (it has a compiler-generated `+<>c__DisplayClass27_0` for `GetOutputItemContainer`).
That means **implementors do not list it in `implements:`** and it does **not** appear in
`04-interface-implementors.txt`.

```csharp
public class Il2CppScheduleOne.Management.ITransitEntity : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase

// ---- properties (9) ----
System.String Name { public get; }
Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemSlot> InputSlots  { public get; public set; }
Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemSlot> OutputSlots { public get; public set; }
UnityEngine.Transform LinkOrigin { public get; }
Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<UnityEngine.Transform> AccessPoints { public get; }
System.Boolean Selectable { public get; }
System.Boolean IsAcceptingItems { public get; }
System.Boolean IsDestroyed { public get; }
Il2CppSystem.Guid GUID { public get; }

// ---- methods (11) ----
public virtual Il2CppScheduleOne.ItemFramework.ItemSlot GetFirstSlotContainingItem(System.String id, Il2CppScheduleOne.Management.ITransitEntity+ESlotType searchType)
public virtual Il2CppScheduleOne.ItemFramework.ItemSlot GetFirstSlotContainingTemplateItem(Il2CppScheduleOne.ItemFramework.ItemInstance templateItem, Il2CppScheduleOne.Management.ITransitEntity+ESlotType searchType)
public virtual System.Int32 GetInputCapacityForItem(Il2CppScheduleOne.ItemFramework.ItemInstance item, Il2CppScheduleOne.NPCs.NPC asker = null, System.Boolean checkPlayerFilters = True)
public virtual System.Int32 GetOutputCapacityForItem(Il2CppScheduleOne.ItemFramework.ItemInstance item, Il2CppScheduleOne.NPCs.NPC asker = null)
public virtual Il2CppScheduleOne.ItemFramework.ItemSlot GetOutputItemContainer(Il2CppScheduleOne.ItemFramework.ItemInstance item)
public virtual System.Void InsertItemIntoInput(Il2CppScheduleOne.ItemFramework.ItemInstance item, Il2CppScheduleOne.NPCs.NPC inserter = null)
public virtual System.Void InsertItemIntoOutput(Il2CppScheduleOne.ItemFramework.ItemInstance item, Il2CppScheduleOne.NPCs.NPC inserter = null)
public virtual Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemSlot> ReserveInputSlotsForItem(Il2CppScheduleOne.ItemFramework.ItemInstance item, Il2CppFishNet.Object.NetworkObject locker)
public virtual System.Void RemoveSlotLocks(Il2CppFishNet.Object.NetworkObject locker)
public virtual System.Void ShowOutline(UnityEngine.Color color)
public virtual System.Void HideOutline()

public enum Il2CppScheduleOne.Management.ITransitEntity+ESlotType { Input = 0, Output = 1, Both = 2 }
```

**Verified implementors** (found by locating every type in the dumps that exposes the
`Il2CppReferenceArray<UnityEngine.Transform> AccessPoints { public get; }` member — this is the reliable
signature since the interface itself is emitted as a class):

| Type | Notes |
|---|---|
| `Il2CppScheduleOne.ObjectScripts.PlaceableStorageEntity` | all placeable storage (shelves, safes, briefcases). Subclass: `Il2CppScheduleOne.ObjectScripts.BedItem` |
| `Il2CppScheduleOne.Growing.GrowContainer` | subclasses: `ObjectScripts.Pot`, `ObjectScripts.MushroomBed` |
| `Il2CppScheduleOne.ObjectScripts.PackagingStation` | |
| `Il2CppScheduleOne.ObjectScripts.BrickPress` | |
| `Il2CppScheduleOne.ObjectScripts.ChemistryStation` | |
| `Il2CppScheduleOne.ObjectScripts.LabOven` | |
| `Il2CppScheduleOne.ObjectScripts.Cauldron` | |
| `Il2CppScheduleOne.ObjectScripts.MixingStation` | subclass: `ObjectScripts.MixingStationMk2` |
| `Il2CppScheduleOne.ObjectScripts.DryingRack` | |
| `Il2CppScheduleOne.ObjectScripts.TrashContainerItem` | |
| `Il2CppScheduleOne.StationFramework.MushroomSpawnStation` | |
| **`Il2CppScheduleOne.Delivery.LoadingDock`** | ★ the property's delivery bay — `Property.LoadingDocks : Il2CppReferenceArray<LoadingDock>`, `Property.LoadingDockCount`. Prime driver destination. |

`Il2CppScheduleOne.ObjectScripts.SurfaceStorageEntity` does **not** expose `AccessPoints` and is therefore
**not** an `ITransitEntity` in this build.

`Il2CppScheduleOne.Storage.StorageEntity` is **not** itself an `ITransitEntity` — it is the item container
that `PlaceableStorageEntity` wraps (`PlaceableStorageEntity.StorageEntity`,
`EmployeeHome.Storage`, `BedItem.Storage`).

### 6.2 `TransitRoute` / `AdvancedTransitRoute` — complete signatures

```csharp
public class Il2CppScheduleOne.Management.TransitRoute : Il2CppSystem.Object
    Il2CppScheduleOne.Management.ITransitEntity Source      { public get; public set; }
    Il2CppScheduleOne.Management.ITransitEntity Destination { public get; public set; }
    Il2CppScheduleOne.Management.TransitLineVisuals visuals  { public get; public set; }
    Il2CppSystem.Action<Il2CppScheduleOne.Management.ITransitEntity> onSourceChange      { public get; public set; }
    Il2CppSystem.Action<Il2CppScheduleOne.Management.ITransitEntity> onDestinationChange { public get; public set; }

    public .ctor(Il2CppScheduleOne.Management.ITransitEntity source, Il2CppScheduleOne.Management.ITransitEntity destination)
    public virtual System.Void SetSource(Il2CppScheduleOne.Management.ITransitEntity source)
    public virtual System.Void SetDestination(Il2CppScheduleOne.Management.ITransitEntity destination)
    public System.Boolean AreEntitiesNonNull()
    public System.Void ValidateEntities()
    public System.Void SetVisualsActive(System.Boolean active)
    public System.Void Update()
    public System.Void Destroy()

public class Il2CppScheduleOne.Management.AdvancedTransitRoute : Il2CppScheduleOne.Management.TransitRoute
    Il2CppScheduleOne.Management.ManagementItemFilter Filter { public get; public set; }
    public .ctor(Il2CppScheduleOne.Management.ITransitEntity source, Il2CppScheduleOne.Management.ITransitEntity destination)
    public .ctor(Il2CppScheduleOne.Persistence.Datas.AdvancedTransitRouteData data)
    public Il2CppScheduleOne.Persistence.Datas.AdvancedTransitRouteData GetData()
    public Il2CppScheduleOne.ItemFramework.ItemInstance GetItemReadyToMove()          // ★ picks the item to move
```

Filtering:

```csharp
public class Il2CppScheduleOne.Management.ManagementItemFilter : Il2CppSystem.Object
    Il2CppScheduleOne.Management.ManagementItemFilter+EMode Mode { public get; public set; }
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemDefinition> Items { public get; public set; }
    public .ctor(Il2CppScheduleOne.Management.ManagementItemFilter+EMode mode)
    public System.Void AddItem(Il2CppScheduleOne.ItemFramework.ItemDefinition item)
    public System.Void RemoveItem(Il2CppScheduleOne.ItemFramework.ItemDefinition item)
    public System.Boolean Contains(Il2CppScheduleOne.ItemFramework.ItemDefinition item)
    public System.Boolean DoesItemMeetFilter(Il2CppScheduleOne.ItemFramework.ItemInstance item)     // ★
    public System.String GetDescription()
    public System.Void SetMode(Il2CppScheduleOne.Management.ManagementItemFilter+EMode mode)

public enum ManagementItemFilter+EMode { Whitelist = 0, Blacklist = 1 }
```

There is **no `EItemCategory` on the route filter**. `ManagementItemFilter` filters on concrete
`ItemDefinition` lists. The category enum lives in the other assembly and is used by the item framework,
not by transit routes:

```csharp
// [OriginalName("ScheduleOne.Core.dll", "ScheduleOne.Core.Items.Framework", "EItemCategory")]
public enum Il2CppScheduleOne.Core.Items.Framework.EItemCategory {
    Product = 0, Packaging = 1, Agriculture = 2, Tools = 3, Furniture = 4, Lighting = 5,
    Cash = 6, Consumable = 7, Equipment = 8, Ingredient = 9, Decoration = 10, Clothing = 11, Storage = 12,
}
```

Slot-level filtering (a second, independent layer):

```csharp
public class Il2CppScheduleOne.ItemFramework.SlotFilter : Il2CppSystem.Object
    Il2CppScheduleOne.ItemFramework.SlotFilter+EType Type { public get; public set; }
    Il2CppSystem.Collections.Generic.List<System.String> ItemIDs { public get; public set; }
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.EQuality> AllowedQualities { public get; public set; }
    public System.Boolean DoesItemMatchFilter(Il2CppScheduleOne.ItemFramework.ItemInstance instance)
    public Il2CppScheduleOne.ItemFramework.SlotFilter Clone()
    public System.Boolean IsDefault()
public enum SlotFilter+EType { None = 0, Whitelist = 1, Blacklist = 2 }
```

### 6.3 `ItemSlot` / `ItemInstance` — the exact take/put methods

```csharp
public class Il2CppScheduleOne.ItemFramework.ItemSlot : Il2CppSystem.Object
    Il2CppScheduleOne.ItemFramework.ItemInstance ItemInstance { public get; public set; }
    Il2CppScheduleOne.ItemFramework.IItemSlotOwner SlotOwner { public get; public set; }
    System.Int32 SlotIndex { public get; }
    System.Int32 Quantity { public get; }
    System.Boolean IsAtCapacity { public get; }
    System.Boolean IsLocked { public get; }
    Il2CppScheduleOne.ItemFramework.ItemSlotLock ActiveLock { public get; public set; }
    System.Boolean IsRemovalLocked / IsAddLocked { public get; public set; }
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemFilter> HardFilters { public get; public set; }
    Il2CppScheduleOne.ItemFramework.SlotFilter PlayerFilter { public get; public set; }
    Il2CppSystem.Action onItemDataChanged / onItemInstanceChanged / onLocked / onUnlocked / onFilterChange

    // ---- TAKE ----
    public System.Void ChangeQuantity(System.Int32 change, System.Boolean _internal = False)    // ★ negative = remove
    public System.Void SetQuantity(System.Int32 amount, System.Boolean _internal = False)
    public virtual System.Void ClearStoredInstance(System.Boolean _internal = False)
    // ---- PUT ----
    public virtual System.Void AddItem(Il2CppScheduleOne.ItemFramework.ItemInstance item, System.Boolean _internal = False)
    public virtual System.Void InsertItem(Il2CppScheduleOne.ItemFramework.ItemInstance item)
    public virtual System.Void SetStoredItem(Il2CppScheduleOne.ItemFramework.ItemInstance instance, System.Boolean _internal = False)
    public static System.Boolean TryInsertItemIntoSet(Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemSlot> ItemSlots, Il2CppScheduleOne.ItemFramework.ItemInstance item)
    // ---- capacity / filters / locks ----
    public virtual System.Int32 GetCapacityForItem(Il2CppScheduleOne.ItemFramework.ItemInstance item, System.Boolean checkPlayerFilters = False)
    public virtual System.Boolean DoesItemMatchHardFilters(Il2CppScheduleOne.ItemFramework.ItemInstance item)
    public virtual System.Boolean DoesItemMatchPlayerFilters(Il2CppScheduleOne.ItemFramework.ItemInstance item)
    public System.Void ApplyLock(Il2CppFishNet.Object.NetworkObject lockOwner, System.String lockReason, System.Boolean _internal = False)
    public System.Void RemoveLock(System.Boolean _internal = False)
    public System.Void SetIsAddLocked(System.Boolean locked) / SetIsRemovalLocked(System.Boolean locked)
    public System.Void SetPlayerFilter(Il2CppScheduleOne.ItemFramework.SlotFilter filter, System.Boolean _internal = False)
    public System.Void AddFilter(Il2CppScheduleOne.ItemFramework.ItemFilter filter)
    public System.Void ReplicateStoredInstance()
    public System.Void SetSlotOwner(Il2CppScheduleOne.ItemFramework.IItemSlotOwner owner)

public class Il2CppScheduleOne.ItemFramework.ItemInstance : Il2CppScheduleOne.Core.Items.Framework.BaseItemInstance
    Il2CppScheduleOne.ItemFramework.ItemDefinition Definition { public get; }
    Il2CppScheduleOne.Equipping.Equippable Equippable { public get; }
    public .ctor(Il2CppScheduleOne.ItemFramework.ItemDefinition definition, System.Int32 quantity)
    public virtual Il2CppScheduleOne.ItemFramework.ItemInstance GetCopy(System.Int32 overrideQuantity = -1)   // ★ template → real instance
    public virtual System.Boolean CanStackWith(Il2CppScheduleOne.ItemFramework.ItemInstance other, System.Boolean checkQuantities = True)
    public virtual Il2CppScheduleOne.Persistence.Datas.ItemData GetItemData()
    public static Il2CppScheduleOne.ItemFramework.ItemInstance CreateInstanceAndRead(Il2CppFishNet.Serializing.Reader reader)
    public virtual System.Void Read(Il2CppFishNet.Serializing.Reader reader) / Write(Il2CppFishNet.Serializing.Writer writer)
    // NOTE: `Quantity` and `ID` live on the base `Il2CppScheduleOne.Core.Items.Framework.BaseItemInstance`.

public class Il2CppScheduleOne.ItemFramework.IItemSlotOwner : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemSlot> ItemSlots { public get; public set; }
    public virtual Il2CppScheduleOne.ItemFramework.ItemSlot GetFirstSlotContaining(System.String id)
    public virtual System.Int32 GetQuantityOfItem(System.String id) / GetQuantitySum() / GetNonEmptySlotCount()
    public virtual System.Void SetStoredInstance(Il2CppFishNet.Connection.NetworkConnection conn, System.Int32 itemSlotIndex, Il2CppScheduleOne.ItemFramework.ItemInstance instance)
    public virtual System.Void SetItemSlotQuantity(System.Int32 itemSlotIndex, System.Int32 quantity)
    public virtual System.Void SetSlotFilter(Il2CppFishNet.Connection.NetworkConnection conn, System.Int32 itemSlotIndex, Il2CppScheduleOne.ItemFramework.SlotFilter filter)
    public virtual System.Void SetSlotLocked(Il2CppFishNet.Connection.NetworkConnection conn, System.Int32 itemSlotIndex, System.Boolean locked, Il2CppFishNet.Object.NetworkObject lockOwner, System.String lockReason)
    public virtual System.Void SendItemSlotDataToClient(Il2CppFishNet.Connection.NetworkConnection conn)
```

`Il2CppScheduleOne.NPCs.NPCInventory` is the employee's carrying inventory
(`NPC.Inventory`, `: Il2CppFishNet.Object.NetworkBehaviour`, implements `IItemSlotOwner`):

```csharp
Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemSlot> ItemSlots { public get; public set; }
public System.Void InsertItem(Il2CppScheduleOne.ItemFramework.ItemInstance item, System.Boolean network = True)
public System.Boolean CanItemFit(Il2CppScheduleOne.ItemFramework.ItemInstance item)
public System.Int32 GetCapacityForItem(Il2CppScheduleOne.ItemFramework.ItemInstance item)
public Il2CppScheduleOne.ItemFramework.ItemInstance GetFirstItem(System.String id, Il2CppScheduleOne.NPCs.NPCInventory+ItemFilter filter = null)
public Il2CppScheduleOne.ItemFramework.ItemInstance GetFirstIdenticalItem(Il2CppScheduleOne.ItemFramework.ItemInstance item, Il2CppScheduleOne.NPCs.NPCInventory+ItemFilter filter = null)
public System.Int32 GetIdenticalItemAmount(Il2CppScheduleOne.ItemFramework.ItemInstance item)
public System.Int32 GetMaxItemCount(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray ids)
public Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemSlot> GetSlots(Il2CppSystem.Func<Il2CppScheduleOne.ItemFramework.ItemSlot, System.Boolean> predicate)
public System.Boolean IsInventoryEmpty()
public System.Void Clear()
public System.Void SetStoredInstance(Il2CppFishNet.Connection.NetworkConnection conn, System.Int32 itemSlotIndex, Il2CppScheduleOne.ItemFramework.ItemInstance instance)   // ObserversRpc + ServerRpc pair
public System.Void SetItemSlotQuantity(System.Int32 itemSlotIndex, System.Int32 quantity)
```

### 6.4 The full item-transfer call chain (server side)

Structure below is **verified** from method names/signatures; the ordering inside each step is **inferred**
(no IL bodies in the dumps).

```
Employee.OnTick()                                         [server]
 └─ Employee.UpdateBehaviour()                            [virtual, overridden by all 4 subclasses]
     └─ e.g. Packager.GetTransitRouteReady(out ItemInstance item) -> AdvancedTransitRoute
         │      └─ AdvancedTransitRoute.GetItemReadyToMove() -> ItemInstance
         │           ├─ route.Source.GetOutputItemContainer(item) / GetFirstSlotContainingTemplateItem(item, ESlotType.Output)
         │           └─ AdvancedTransitRoute.Filter.DoesItemMeetFilter(item)      [Whitelist/Blacklist]
         └─ Employee.MoveItemBehaviour.Initialize(route, itemTemplate, maxMoveAmount = -1, skipPickup = false)
             └─ MoveItemBehaviour.IsTransitRouteValid(route, templateItem, out invalidReason)
                 ├─ route.AreEntitiesNonNull()
                 ├─ MoveItemBehaviour.CanGetToSource(route)      -> NavMeshUtility.GetReachableAccessPoint(route.Source, Npc)
                 ├─ MoveItemBehaviour.CanGetToDestination(route) -> NavMeshUtility.GetReachableAccessPoint(route.Destination, Npc)
                 └─ MoveItemBehaviour.IsDestinationValid(route, item, out invalidReason)
                      -> route.Destination.IsAcceptingItems && route.Destination.GetInputCapacityForItem(item, Npc, checkPlayerFilters:true) > 0
             └─ Behaviour.Enable()  ->  NPCBehaviour.AddEnabledBehaviour(this)
                                    ->  NPCBehaviour.Update() picks it as activeBehaviour
                                    ->  Behaviour.Activate()

MoveItemBehaviour.Activate() / OnActiveTick()   — state machine over EState
 1. StartTransit()
 2. EState.WalkingToSource   : WalkToSource()
        - GetSourceAccessPoint(route)   -> NavMeshUtility.GetReachableAccessPoint(route.Source, Npc)
        - Behaviour.SetDestination(route.Source, teleportIfFail: true)
              -> NPC.Movement.SetDestination(ITransitEntity entity)
        - completion checked by IsAtSource() -> NavMeshUtility.IsAtTransitEntity(route.Source, Npc, 0.4f)
        - WalkCallback(NPCMovement+WalkResult) handles Failed/Interrupted/Partial/Success
 3. EState.Grabbing          : GrabItem()  ->  TakeItem()
        - GetAmountToGrab()   (min of route.Source output quantity,
                               NPC.Inventory.GetCapacityForItem(item),
                               maxMoveAmount)
        - ItemSlot slot = route.Source.GetFirstSlotContainingTemplateItem(itemToRetrieveTemplate, ESlotType.Output)
        - slot.ChangeQuantity(-amount)                         ★ REMOVE FROM SOURCE
        - NPC.Inventory.InsertItem(itemToRetrieveTemplate.GetCopy(amount))   ★ INTO NPC
        - route.Destination.ReserveInputSlotsForItem(item, Npc.NetworkObject)  (slot lock, "Employee is about to place an item here")
        - grabbedAmount = amount
 4. EState.WalkingToDestination : WalkToDestination()
        - GetDestinationAccessPoint(route) ; Behaviour.SetDestination(route.Destination)
        - IsAtDestination() -> NavMeshUtility.IsAtTransitEntity(route.Destination, Npc, 0.4f)
 5. EState.Placing           : PlaceItem()
        - IsNpcInventoryItemValid(item)
        - route.Destination.InsertItemIntoInput(item, Npc)      ★ INTO DESTINATION
              -> ItemSlot.TryInsertItemIntoSet(destination.InputSlots, item) / ItemSlot.AddItem(item)
        - NPC.Inventory slot.ChangeQuantity(-grabbedAmount) or ClearStoredInstance()
        - route.Destination.RemoveSlotLocks(Npc.NetworkObject)
 6. EndTransit()  ->  Behaviour.Disable()  ->  NPCBehaviour.RemoveEnabledBehaviour(this)
```

Navigation helpers used throughout:

```csharp
public static class Il2CppScheduleOne.DevUtilities.NavMeshUtility
    public static UnityEngine.Transform GetReachableAccessPoint(Il2CppScheduleOne.Management.ITransitEntity entity, Il2CppScheduleOne.NPCs.NPC npc)
    public static System.Boolean IsAtTransitEntity(Il2CppScheduleOne.Management.ITransitEntity entity, Il2CppScheduleOne.NPCs.NPC npc, System.Single distanceThreshold = 0.4)
    public static System.Boolean SamplePosition(UnityEngine.Vector3 sourcePosition, out UnityEngine.AI.NavMeshHit& hit, System.Single maxDistance, System.Int32 areaMask, System.Boolean useCache = True)
    public static System.Single GetPathLength(UnityEngine.AI.NavMeshPath path)
    public static System.Int32 GetNavMeshAgentID(System.String name)
    public static UnityEngine.Vector3 Quantize(UnityEngine.Vector3 position, System.Single precision = 0.1)
    public static System.Void CacheSampleResult(UnityEngine.Vector3 sourcePosition, UnityEngine.Vector3 hitPosition) / ClearCache()

// Il2CppScheduleOne.NPCs.NPCMovement (NPC.Movement)
public System.Void SetDestination(Il2CppScheduleOne.Management.ITransitEntity entity)
public System.Void SetDestination(UnityEngine.Vector3 pos)
public System.Void SetDestination(UnityEngine.Transform target)
public System.Void SetDestination(UnityEngine.Vector3 pos, Il2CppSystem.Action<Il2CppScheduleOne.NPCs.NPCMovement+WalkResult> callback = null, System.Single maximumDistanceForSuccess = 1, System.Single cacheMaxDistSqr = 1)
public System.Void SetDestination(UnityEngine.Vector3 pos, Il2CppSystem.Action<Il2CppScheduleOne.NPCs.NPCMovement+WalkResult> callback = null, System.Boolean interruptExistingCallback = True, System.Single successThreshold = 1, System.Single cacheMaxDistSqr = 1)
public System.Boolean CanGetTo(Il2CppScheduleOne.Management.ITransitEntity entity, System.Single proximityReq = 1)
public System.Boolean CanGetTo(UnityEngine.Vector3 position, System.Single proximityReq = 1)
public System.Boolean CanGetTo(UnityEngine.Vector3 position, System.Single proximityReq, out UnityEngine.AI.NavMeshPath& path)
public System.Boolean GetClosestReachablePoint(UnityEngine.Vector3 targetPosition, out UnityEngine.Vector3& closestPoint)
public System.Void Warp(UnityEngine.Vector3 position) / Warp(UnityEngine.Transform target) / WarpToNavMesh()
public System.Void Stop() / PauseMovement() / ResumeMovement()
public System.Void SetAgentType(Il2CppScheduleOne.NPCs.NPCMovement+EAgentType type)
System.Single MoveSpeedMultiplier { public get; public set; }
System.Boolean HasDestination { public get; public set; }   System.Boolean IsMoving { public get; }
UnityEngine.Vector3 CurrentDestination { public get; public set; }

public enum NPCMovement+WalkResult { Failed = 0, Interrupted = 1, Stopped = 2, Partial = 3, Success = 4 }
public enum NPCMovement+EAgentType { Humanoid = 0, BigHumanoid = 1, IgnoreCosts = 2 }
public enum NPCMovement+EStance   { None = 0, Stanced = 1 }
```

### 6.5 Configuring a route at runtime

```csharp
// 1. build it
var route = new Il2CppScheduleOne.Management.AdvancedTransitRoute(sourceEntity, destinationEntity);
route.Filter = new Il2CppScheduleOne.Management.ManagementItemFilter(ManagementItemFilter.EMode.Whitelist);
route.Filter.AddItem(someItemDefinition);

// 2. persist it into an employee's configuration (Packager pattern)
var cfg   = packager.configuration;               // PackagerConfiguration
var field = cfg.Routes;                           // RouteListField
field.AddItem(route);                             // or field.SetList(list, network: true)
field.Replicate();                                // pushes AdvancedTransitRouteData[] over ConfigurationReplicator

// 3. or drive MoveItemBehaviour directly, bypassing the config UI (server only)
employee.MoveItemBehaviour.Initialize(route, itemTemplate, maxMoveAmount: -1, skipPickup: false);
employee.MoveItemBehaviour.Enable();
```

`AdvancedTransitRoute` round-trips through
`Il2CppScheduleOne.Persistence.Datas.AdvancedTransitRouteData { SourceGUID, DestinationGUID, FilterMode, FilterItemIDs }`
— GUIDs are `ITransitEntity.GUID` values, so any custom transit entity must have a stable `Guid`.

---

## 7. Employee UI

### 7.1 `Il2CppScheduleOne.Management.UI` — 1 type, the base of everything

```csharp
public class Il2CppScheduleOne.Management.UI.ConfigPanel : UnityEngine.MonoBehaviour
    Il2CppScheduleOne.UIContentPanel ContentPanel { public get; public set; }
    public System.Void Bind(Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Management.EntityConfiguration> configs, Il2CppScheduleOne.UIScreen screen = null)
    public virtual System.Void BindInternal(Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Management.EntityConfiguration> configs)   // ★ override point
    public System.Void ConfigureScreen(Il2CppScheduleOne.UIScreen screen)
```

### 7.2 `Il2CppScheduleOne.UI.Management` — all 49 types

**Clipboard root / screens**
`ClipboardScreen` (base: open/close slide panel), `ItemSelector : ClipboardScreen`,
`RecipeSelector : ClipboardScreen`, `StringSetter : ClipboardScreen`, `ObjectSelector`,
`TransitEntitySelector`, `SelectionInfoUI`, `ManagementWorldspaceCanvas`.

**Config panels (`: Il2CppScheduleOne.Management.UI.ConfigPanel`)** — one per `EConfigurableType`:
`BotanistConfigPanel`, `ChemistConfigPanel`, `CleanerConfigPanel`, `PackagerConfigPanel`,
`BrickPressConfigPanel`, `CauldronConfigPanel`, `ChemistryStationConfigPanel`, `DryingRackConfigPanel`,
`LabOvenConfigPanel`, `MixingStationConfigPanel`, `MushroomBedConfigPanel`, `PackagingStationConfigPanel`,
`PotConfigPanel`, `SpawnStationConfigPanel`.

**Worldspace UI elements (`: WorldspaceUIElement`)**:
`BotanistUIElement`, `ChemistUIElement`, `CleanerUIElement`, `PackagerUIElement`, `BrickPressUIElement`,
`CauldronUIElement`, `ChemistryStationUIElement`, `DryingRackUIElement`, `LabOvenUIElement`,
`MixingStationUIElement`, `MushroomBedUIElement`, `PackagingStationUIElement`, `PotUIElement`,
`SpawnStationUIElement`, `StorageUIElement`.

**Field widgets**: `ItemFieldUI`, `NPCFieldUI`, `NumberFieldUI`, `ObjectFieldUI`, `ObjectListFieldUI`,
`QualityFieldUI`, `RouteListFieldUI`, `RouteEntryUI`, `StationRecipeFieldUI`, `StringFieldUI`,
`AssignedWorkerDisplay`.

The four employee panels (this is the whole "hiring interface" surface a new employee type must fill):

```csharp
public class BotanistConfigPanel : ConfigPanel
    ObjectFieldUI BedUI; ObjectFieldUI SuppliesUI; ObjectListFieldUI PotsUI;
public class ChemistConfigPanel : ConfigPanel
    ObjectFieldUI BedUI; ObjectListFieldUI StationsUI;
public class CleanerConfigPanel : ConfigPanel
    ObjectFieldUI BedUI; ObjectListFieldUI BinsUI;
public class PackagerConfigPanel : ConfigPanel                       // ★ closest to a Driver panel
    ObjectFieldUI BedUI; ObjectListFieldUI StationsUI; RouteListFieldUI RoutesUI;
// each overrides:  public virtual System.Void BindInternal(List<EntityConfiguration> configs)
```

The route editor widgets (reuse these verbatim for a driver panel):

```csharp
public class Il2CppScheduleOne.UI.Management.RouteListFieldUI : UnityEngine.MonoBehaviour
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Management.RouteListField> Fields { public get; public set; }
    System.String FieldText { public get; public set; }
    Il2CppTMPro.TextMeshProUGUI FieldLabel { public get; public set; }
    Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppScheduleOne.UI.Management.RouteEntryUI> RouteEntries { public get; public set; }
    UnityEngine.RectTransform MultiEditBlocker { public get; public set; }
    UnityEngine.UI.Button AddButton { public get; public set; }
    public System.Void Bind(Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Management.RouteListField> field)
    public System.Void AddClicked()
    public System.Void EntryDeleteClicked(Il2CppScheduleOne.UI.Management.RouteEntryUI entry)
    public System.Void Refresh(Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Management.AdvancedTransitRoute> newVal)
    public System.Void RouteChanged(Il2CppScheduleOne.Management.ITransitEntity newEntity)
    public System.Void Start()

public class Il2CppScheduleOne.UI.Management.RouteEntryUI : UnityEngine.MonoBehaviour
    Il2CppScheduleOne.Management.AdvancedTransitRoute AssignedRoute { public get; public set; }
    UnityEngine.UI.Image SourceIcon / DestinationIcon / FilterIcon; Il2CppTMPro.TextMeshProUGUI SourceLabel / DestinationLabel;
    UnityEngine.Events.UnityEvent onDeleteClicked { public get; public set; }
    public System.Void AssignRoute(Il2CppScheduleOne.Management.AdvancedTransitRoute route)
    public System.Void ClearRoute() / SourceClicked() / DestinationClicked() / FilterClicked() / DeleteClicked() / RefreshUI()
    public System.Boolean ObjectValid(Il2CppScheduleOne.Management.ITransitEntity obj, out System.String& reason)
    public System.Void ObjectsSelected(Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Management.ITransitEntity> objs)
```

Selectors:

```csharp
public class Il2CppScheduleOne.UI.Management.TransitEntitySelector : UnityEngine.MonoBehaviour
    static System.Single SELECTION_RANGE { public get; public set; }
    public virtual System.Void Open(System.String _selectionTitle, System.String instruction, System.Int32 _maxSelectedObjects,
        Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Management.ITransitEntity> _selectedObjects,
        Il2CppSystem.Collections.Generic.List<Il2CppSystem.Type> _typeRequirements,
        Il2CppScheduleOne.UI.Management.TransitEntitySelector+ObjectFilter _objectFilter,
        Il2CppSystem.Action<Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Management.ITransitEntity>> _callback,
        Il2CppSystem.Collections.Generic.List<UnityEngine.Transform> transitLineSources = null,
        System.Boolean selectingDestination = True)
    public System.Void CloseAndSubmit() / CloseAndCancel() / ClearSelection()
    public Il2CppScheduleOne.Management.ITransitEntity GetHoveredObject()
    public System.Boolean IsObjectTypeValid(Il2CppScheduleOne.Management.ITransitEntity obj, out System.String& reason)
    public delegate System.Boolean ObjectFilter(Il2CppScheduleOne.Management.ITransitEntity obj, out System.String& reason)

public class Il2CppScheduleOne.UI.Management.ObjectSelector : UnityEngine.MonoBehaviour
    public virtual System.Void Open(System.String _selectionTitle, System.String instruction, System.Int32 _maxSelectedObjects,
        Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.EntityFramework.BuildableItem> _selectedObjects,
        Il2CppSystem.Collections.Generic.List<Il2CppSystem.Type> _typeRequirements,
        Il2CppScheduleOne.Property.Property property,
        Il2CppScheduleOne.UI.Management.ObjectSelector+ObjectFilter _objectFilter,
        Il2CppSystem.Action<Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.EntityFramework.BuildableItem>> _callback,
        Il2CppSystem.Collections.Generic.List<UnityEngine.Transform> transitLineSources = null)
    public delegate System.Boolean ObjectFilter(Il2CppScheduleOne.EntityFramework.BuildableItem obj, out System.String& reason)
```

Panel routing — **the exact hook for registering a new employee panel**:

```csharp
public class Il2CppScheduleOne.Management.ManagementInterface : Il2CppScheduleOne.DevUtilities.Singleton<Il2CppScheduleOne.Management.ManagementInterface>
    Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppScheduleOne.Management.ManagementInterface+ConfigurableTypePanel> ConfigPanelPrefabs { public get; public set; }
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Management.IConfigurable> Configurables { public get; public set; }
    Il2CppScheduleOne.Management.UI.ConfigPanel loadedPanel { public get; public set; }
    Il2CppScheduleOne.UI.Management.ClipboardScreen MainScreen { public get; public set; }
    Il2CppScheduleOne.UI.Management.ItemSelector ItemSelectorScreen { public get; public set; }
    Il2CppScheduleOne.UI.Management.ObjectSelector ObjectSelector { public get; public set; }
    Il2CppScheduleOne.UI.Management.RecipeSelector RecipeSelectorScreen { public get; public set; }
    Il2CppScheduleOne.UI.Management.TransitEntitySelector TransitEntitySelector { public get; public set; }
    Il2CppScheduleOne.UI.Management.StringSetter StringSetterScreen { public get; public set; }
    Il2CppScheduleOne.Tools.ManagementClipboard_Equippable EquippedClipboard { public get; public set; }
    public System.Void Open(Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Management.IConfigurable> configurables, Il2CppScheduleOne.Tools.ManagementClipboard_Equippable _equippedClipboard)
    public System.Void Close(System.Boolean preserveState = False)
    public Il2CppScheduleOne.Management.UI.ConfigPanel GetConfigPanelPrefab(Il2CppScheduleOne.Management.EConfigurableType type)   // ★ patch this
    public System.Void InitializeConfigPanel() / DestroyConfigPanel() / UpdateMainLabels() / RenameButtonClicked()

public class ManagementInterface+ConfigurableTypePanel : Il2CppSystem.Object
    Il2CppScheduleOne.Management.EConfigurableType Type { public get; public set; }
    Il2CppScheduleOne.Management.UI.ConfigPanel Panel { public get; public set; }
```

Worldspace layer:

```csharp
public class Il2CppScheduleOne.UI.Management.WorldspaceUIElement : UnityEngine.MonoBehaviour
    static System.Single TRANSITION_TIME { public get; public set; }
    UnityEngine.RectTransform RectTransform / Container; Il2CppTMPro.TextMeshProUGUI TitleLabel;
    Il2CppScheduleOne.UI.Management.AssignedWorkerDisplay AssignedWorkerDisplay { public get; public set; }
    System.Boolean IsEnabled { public get; public set; }   System.Boolean IsVisible { public get; }
    public virtual System.Void Show() / Hide(Il2CppSystem.Action callback = null) / Destroy()
    public virtual System.Void HoverStart() / HoverEnd() / SetInternalScale(System.Single scale)
    public System.Void SetAssignedNPC(Il2CppScheduleOne.NPCs.NPC npc)
    public System.Void SetScale(System.Single scale, Il2CppSystem.Action callback)
    public System.Void UpdatePosition(UnityEngine.Vector3 worldSpacePosition)

public class Il2CppScheduleOne.UI.Management.ManagementWorldspaceCanvas : Il2CppScheduleOne.DevUtilities.Singleton<…>
    static System.Single VISIBILITY_RANGE / PROPERTY_CANVAS_RANGE { public get; public set; }
    Il2CppScheduleOne.Management.TransitLineVisuals TransitRouteVisualsPrefab { public get; public set; }
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Management.IConfigurable> ShownConfigurables / SelectedConfigurables { public get; public set; }
    public System.Void Open() / Close(System.Boolean preserveSelection = False)
    public System.Void AddToSelection(Il2CppScheduleOne.Management.IConfigurable config) / RemoveFromSelection(…) / ClearSelection()
    public Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Management.IConfigurable> GetConfigurablesToShow()
    public Il2CppScheduleOne.Management.IConfigurable GetHoveredConfigurable()
    public System.Void ShowCrosshairPrompt(System.String message) / HideCrosshairPrompt() / SetCrosshairPromptMessage(System.String message)
    public System.Void UpdateUIs() / UpdateSelection() / RemoveNullConfigurables()

public class Il2CppScheduleOne.Tools.ManagementClipboard_Equippable : Il2CppScheduleOne.Equipping.Equippable_Viewmodel
    Il2CppScheduleOne.UI.Management.SelectionInfoUI SelectionInfo { public get; public set; }
    public Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Management.IConfigurable> GetSelectedConfigurables()
    public System.Boolean CanOpenClipboard() / CanCloseClipboard()
    public System.Void FullscreenEnter() / FullscreenExit()
    public System.Void OverrideClipboardText(System.String overriddenText) / EndOverride()
    public System.Void UpdateHeatmap() / ClearPropertyWithHeatmapShown()
    public static System.Boolean ResetHeatmapToggle()
```

**Minimum UI work to add a new employee type:** a new `EConfigurableType` value → a new
`ConfigPanel` subclass overriding `BindInternal` → registered into `ManagementInterface.ConfigPanelPrefabs`
(or intercept `GetConfigPanelPrefab`) → a new `WorldspaceUIElement` subclass with `Initialize(...)` and
`RefreshUI()` → assign it to your `Employee` subclass's `WorldspaceUIPrefab` field →
`ManagementUtilities.StorageTypeIcon` / your own `TypeIcon` sprite.

---

## 8. The `Bed` requirement

Two distinct types:

```csharp
public class Il2CppScheduleOne.ObjectScripts.Bed : Il2CppFishNet.Object.NetworkBehaviour
    static System.Int32 MIN_SLEEP_TIME { public get; public set; }
    Il2CppScheduleOne.Interaction.InteractableObject intObj { public get; public set; }
    Il2CppScheduleOne.Employees.EmployeeHome EmployeeStationThing { public get; public set; }   // ★ link to the home
    UnityEngine.MeshRenderer BlanketMesh { public get; public set; }
    UnityEngine.Material DefaultBlanket / BotanistBlanket / ChemistBlanket / PackagerBlanket / CleanerBlanket
    Il2CppScheduleOne.Employees.Employee AssignedEmployee { public get; }                        // ★ read-only, forwards to EmployeeStationThing
    public virtual System.Void Awake()
    public System.Boolean CanSleep(out System.String& noSleepReason)
    public System.Void Hovered() / Interacted()
    public System.Void UpdateMaterial()
    public System.Void Method_Private_Void_0()          // interop fallback, unstable

public class Il2CppScheduleOne.ObjectScripts.BedItem : Il2CppScheduleOne.ObjectScripts.PlaceableStorageEntity
    Il2CppScheduleOne.ObjectScripts.Bed Bed { public get; public set; }
    Il2CppScheduleOne.Storage.StorageEntity Storage { public get; public set; }
    UnityEngine.GameObject Briefcase { public get; public set; }
    public static System.Boolean IsBedValid(Il2CppScheduleOne.EntityFramework.BuildableItem obj, out System.String& reason)   // ★ ObjectField filter
    public System.Void UpdateBriefcase()
```

The actual binding object is `EmployeeHome`:

```csharp
public class Il2CppScheduleOne.Employees.EmployeeHome : UnityEngine.MonoBehaviour
    Il2CppScheduleOne.Employees.Employee AssignedEmployee { public get; public set; }
    System.String HomeType { public get; public set; }
    UnityEngine.GameObject Clipboard { public get; public set; }
    UnityEngine.SpriteRenderer MugshotSprite { public get; public set; }
    Il2CppTMPro.TextMeshPro NameLabel { public get; public set; }
    Il2CppScheduleOne.Storage.StorageEntity Storage { public get; public set; }              // ★ wage cash + supplies live here
    Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<UnityEngine.MeshRenderer> EmployeeSpecificMeshes { public get; public set; }
    UnityEngine.Material SpecificMat_Default / SpecificMat_Botanist / SpecificMat_Chemist / SpecificMat_Packager / SpecificMat_Cleaner
    UnityEngine.Events.UnityEvent onAssignedEmployeeChanged { public get; public set; }
    public System.Void SetAssignedEmployee(Il2CppScheduleOne.Employees.Employee employee)     // ★ the bind call
    public static System.Boolean IsBuildableEntityAValidEmployeeHome(Il2CppScheduleOne.EntityFramework.BuildableItem obj, out System.String& reason)
    public System.Single GetCashSum()
    public System.Void RemoveCash(System.Single amount)
    public System.Void UpdateMaterial() / UpdateStorageText()
    public System.Void Awake() / Start()
```

### How the binding happens

1. The player picks a bed in the clipboard via the employee configuration's `Home : ObjectField`
   (`BotanistConfiguration.Home`, `ChemistConfiguration.Home`, `CleanerConfiguration.Home`,
   `PackagerConfiguration.Home`).
2. `ObjectField.SetObject(BuildableItem obj, System.Boolean network)` fires
   `*Configuration.HomeChanged(BuildableItem newItem)`, which resolves the `BedItem` → `EmployeeHome`
   and calls `EmployeeHome.SetAssignedEmployee(employee)` (inferred; the `AssignedHome`/`assignedHome`
   property on each configuration holds the result).
3. `Employee.GetHome() : EmployeeHome` is the runtime accessor (virtual, overridden by all four subclasses).
4. The `ObjectField` filter is `BedItem.IsBedValid(BuildableItem, out string reason)` /
   `EmployeeHome.IsBuildableEntityAValidEmployeeHome(BuildableItem, out string reason)`.
5. `Property.GetUnassignedBeds() : List<Il2CppScheduleOne.ObjectScripts.Bed>` lists free beds.

### What happens without a bed

* `Employee.BedNotAssignedDialogue : DialogueContainer` is shown, surfaced via
  `Employee.GetWorkIssue(out DialogueContainer notWorkingReason)` and `Employee.WorkIssues`.
* `Employee.CanWork()` returns `false` (inferred — this is the gate `UpdateBehaviour` checks) and
  `Employee.ShouldIdle()` returns `true`, so the NPC falls back to `Employee.WaitOutside` (`IdleBehaviour`).
* Pay cannot be collected: `Employee.IsPayAvailable()` needs `GetHome().GetCashSum()`.
* Quest tracking: `Il2CppScheduleOne.Quests.Quest_Employees.AssignBedEntry : QuestEntry` and
  `AreAnyEmployeesAssignedBeds() : System.Boolean`.
* **UNVERIFIED:** the exact wording of the "no bed" dialogue and whether `CanWork()` returns false for the
  bed case specifically (no IL bodies in the dumps).

---

## 9. Save data

Real saves put employees at `Properties/<PropertyName>/Employees/*.json`. The chain:

```csharp
public class Il2CppScheduleOne.Persistence.Datas.NPCData : Il2CppScheduleOne.Persistence.Datas.SaveData
    System.String ID { public get; public set; }
    public .ctor(System.String id)

public class Il2CppScheduleOne.Persistence.Datas.EmployeeData : Il2CppScheduleOne.Persistence.Datas.NPCData
    System.String AssignedProperty { public get; public set; }
    System.String FirstName        { public get; public set; }
    System.String LastName         { public get; public set; }
    System.Boolean IsMale          { public get; public set; }
    System.Int32 AppearanceIndex   { public get; public set; }
    UnityEngine.Vector3 Position   { public get; public set; }
    UnityEngine.Quaternion Rotation{ public get; public set; }
    System.String GUID             { public get; public set; }
    System.Boolean PaidForToday    { public get; public set; }
    public .ctor(System.String id, System.String assignedProperty, System.String firstName, System.String lastName,
                 System.Boolean isMale, System.Int32 appearanceIndex, UnityEngine.Vector3 position,
                 UnityEngine.Quaternion rotation, Il2CppSystem.Guid guid, System.Boolean paidForToday)
```

Four concrete subclasses, **all identical** — each adds exactly one field:

```csharp
public class Il2CppScheduleOne.Persistence.Datas.BotanistData : EmployeeData
public class Il2CppScheduleOne.Persistence.Datas.ChemistData  : EmployeeData
public class Il2CppScheduleOne.Persistence.Datas.CleanerData  : EmployeeData
public class Il2CppScheduleOne.Persistence.Datas.PackagerData : EmployeeData
    Il2CppScheduleOne.Persistence.Datas.MoveItemData MoveItemData { public get; public set; }     // ★ in-flight transit is saved
    public .ctor(System.String id, System.String assignedProperty, System.String firstName, System.String lastName,
                 System.Boolean male, System.Int32 appearanceIndex, UnityEngine.Vector3 position,
                 UnityEngine.Quaternion rotation, Il2CppSystem.Guid guid, System.Boolean paidForToday,
                 Il2CppScheduleOne.Persistence.Datas.MoveItemData moveItemData)

public class Il2CppScheduleOne.Persistence.Datas.MoveItemData : Il2CppSystem.Object
    System.String TemplateItemJSON      { public get; public set; }
    System.Int32  GrabbedItemQuantity   { public get; public set; }
    System.String SourceGUID            { public get; public set; }
    System.String DestinationGUID       { public get; public set; }
    public .ctor(System.String templateItemJson, System.Int32 grabbedItemQuantity, Il2CppSystem.Guid sourceGUID, Il2CppSystem.Guid destinationGUID)
```

Configuration save data (separate files):

```csharp
public class Il2CppScheduleOne.Persistence.Datas.PackagerConfigurationData : SaveData
    Il2CppScheduleOne.Persistence.Datas.ObjectFieldData     Bed      { public get; public set; }
    Il2CppScheduleOne.Persistence.Datas.ObjectListFieldData Stations { public get; public set; }
    Il2CppScheduleOne.Persistence.Datas.RouteListData       Routes   { public get; public set; }
public class Il2CppScheduleOne.Persistence.Datas.BotanistConfigurationData : SaveData
public class Il2CppScheduleOne.Persistence.Datas.ChemistConfigurationData  : SaveData
public class Il2CppScheduleOne.Persistence.Datas.CleanerConfigurationData  : SaveData

public class Il2CppScheduleOne.Persistence.Datas.RouteListData : Il2CppSystem.Object
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Persistence.Datas.AdvancedTransitRouteData> Routes { public get; public set; }
public class Il2CppScheduleOne.Persistence.Datas.AdvancedTransitRouteData : Il2CppSystem.Object
    System.String SourceGUID / DestinationGUID { public get; public set; }
    Il2CppScheduleOne.Management.ManagementItemFilter+EMode FilterMode { public get; public set; }
    Il2CppSystem.Collections.Generic.List<System.String> FilterItemIDs { public get; public set; }
```

Write / read:

```csharp
// write (on each employee)
public virtual Il2CppScheduleOne.Persistence.Datas.DynamicSaveData GetSaveData()                    // Botanist/Chemist/Cleaner/Packager
public virtual Il2CppSystem.Collections.Generic.List<System.String> WriteData(System.String parentFolderPath)
public virtual Il2CppScheduleOne.Persistence.Datas.NPCData GetNPCData()
// on Property
public Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Persistence.Datas.DynamicSaveData> GetEmployeeSaveDatas()
Il2CppSystem.Collections.Generic.List<System.String> savedEmployeePaths { public get; public set; }

// read
public class Il2CppScheduleOne.Persistence.Loaders.EmployeeLoader : Il2CppScheduleOne.Persistence.Loaders.NPCLoader
    System.String NPCType { public get; }
    public virtual Il2CppScheduleOne.Employees.Employee CreateAndLoadEmployee(Il2CppScheduleOne.Persistence.Datas.DynamicSaveData saveData)
    public virtual System.Void Load(Il2CppScheduleOne.Persistence.Datas.DynamicSaveData saveData)
// 4 subclasses: BotanistLoader, ChemistLoader, CleanerLoader, PackagerLoader
```

`MoveItemBehaviour.GetSaveData() : MoveItemData` / `MoveItemBehaviour.Load(MoveItemData)` is what makes a
half-finished delivery survive a save/load. **A driver mod must implement the same pair.**

Original (unprefixed) names for save-file / JSON keys: `ScheduleOne.Persistence.Datas.PackagerData`, etc.
The `DataType` discriminator string inside each JSON is **UNVERIFIED** (not recoverable from these dumps —
inspect a real save file).

---

## 10. Runtime flow: hire → assign → tick → work → move → paid

| # | Step | Method(s) | Side |
|---|---|---|---|
| 1 | Player buys an employee | `EmployeeManager.CreateNewEmployee(Property property, EEmployeeType type)` | client (or server) |
| 2 | Request crosses the wire | `EmployeeManager.CreateEmployee(...)` → `RpcWriter___Server_CreateEmployee_311954683` → `RpcReader___Server_…` → **`RpcLogic___CreateEmployee_311954683`** | client → **server** |
| 3 | Instantiate + spawn | `EmployeeManager.CreateEmployee_Server(...)` using `GetEmployeePrefab(type)`; FishNet `NetworkObject` spawn | **server** |
| 4 | Identity replicated | `Employee.Initialize(conn, firstName, lastName, id, guid, propertyID, male, appearanceIndex)` → `RpcLogic___Initialize_2260823878` (Target + Observers) | server → all |
| 5 | Attach to property | `Employee.AssignProperty(Property prop, System.Boolean warp)` ↔ `Property.RegisterEmployee(Employee emp)` (returns `EmployeeIndex`) | **server** |
| 6 | Config created + replicated | `<Type>Configuration.ctor(ConfigurationReplicator, IConfigurable, <employee>)`; `EntityConfiguration.ReplicateAllFields(conn, replicateDefaults)`; `Employee.SendConfigurationToClient(conn)` (on the subclasses) | server → client |
| 7 | Player assigns bed / stations / routes | `ObjectField.SetObject(obj, network:true)`, `ObjectListField.SetList(list, true)`, `RouteListField.SetList(list, true)` → `ConfigurationReplicator.Send*Field(...)` (ServerRpc) → `Receive*Field(...)` (ObserversRpc) | client → server → all |
| 8 | Bed bound | `<Type>Configuration.HomeChanged(BuildableItem newItem)` → `EmployeeHome.SetAssignedEmployee(Employee)` | server |
| 9 | Per-tick decision | `Employee.OnTick()` → `Employee.UpdateBehaviour()` (virtual; `Botanist`/`Chemist`/`Cleaner`/`Packager` all override) | **server only** (`OnTick` is the FishNet tick) |
| 10 | Gate checks | `Employee.CanWork()`, `Employee.IsAnyWorkInProgress()`, `Employee.ShouldIdle()`; failures go to `Employee.SubmitNoWorkReason(reason, fix, priority)` (ObserversRpc) and `Employee.WorkIssues` | server, reason replicated |
| 11 | Behaviour chosen | `Behaviour.Enable()` → `NPCBehaviour.AddEnabledBehaviour(b)` → `NPCBehaviour.Update()` compares `GetEnabledBehaviour()` with `activeBehaviour` → `Behaviour.Deactivate()` old, `Behaviour.Activate()` new | server decides; replicated via `NPCBehaviour.EnableBehaviour_Client` / `ActivateBehaviour_Client` (Observers/Target RPCs) |
| 12 | Work performed | station behaviours (`StartPackaging()` → `BeginPackaging()` ObserversRpc → `RpcLogic___BeginPackaging_2166136261`); grow behaviours (`GrowContainerBehaviour.PerformAction()` → `OnActionSuccess(ItemInstance usedItem)`) | server drives, clients play the animation |
| 13 | Item moved | `MoveItemBehaviour.Initialize(route, template, maxMoveAmount, skipPickup)` → `StartTransit()` → `WalkToSource()` → `GrabItem()`/`TakeItem()` → `WalkToDestination()` → `PlaceItem()` → `EndTransit()`; the actual mutations are `ItemSlot.ChangeQuantity(-n)` and `ITransitEntity.InsertItemIntoInput(item, npc)` | **server only**; slot changes replicate via `StorageEntity` / `NPCInventory` `SetStoredInstance` / `SetItemSlotQuantity` ObserversRpcs |
| 14 | Progress marked | `Employee.MarkIsWorking()` resets `Employee.TicksSinceLastWork` | server |
| 15 | Payment | `Employee.IsPayAvailable()` (reads `GetHome().GetCashSum()`) → `Employee.SetIsPaid()` (sets the `PaidForToday` SyncVar) → `Employee.RemoveDailyWage()` → `EmployeeHome.RemoveCash(DailyWage)` | server; `PaidForToday` SyncVar auto-replicates |
| 16 | Daily reset | `Employee.OnSleepEnd()`; quest tracking via `Quest_Employees.OnUncappedMinPass()` / `AreAnyEmployeesPaid()` | server |
| 17 | Save | `Property.GetEmployeeSaveDatas()` → `Employee.GetSaveData()` / `WriteData(parentFolderPath)` → `Properties/<name>/Employees/*.json` | server |
| 18 | Load | `EmployeeLoader.CreateAndLoadEmployee(DynamicSaveData)` / `Load(DynamicSaveData)`; `MoveItemBehaviour.Load(MoveItemData)` restores in-flight transit | server |

**Firing / transferring:** `Employee.SendFire()` (ServerRpc) → `Employee.Fire()` (virtual, server) →
`Employee.ReceiveFire()` (ObserversRpc) → `Employee.LeavePropertyAndDespawn()`.
`Employee.SendTransfer(propertyCode)` (ServerRpc) → `Employee.TransferToProperty(string code)` (ObserversRpc)
→ `Employee.TransferToProperty(Property prop)`.

---

## 11. Hooks & extension points

### 11.1 Adding a new employee type

| Goal | Concrete hook |
|---|---|
| New enum value | `Il2CppScheduleOne.Employees.EEmployeeType` is a plain `System.Int32` enum. You **cannot** add a value to an IL2CPP enum at runtime, but you **can** cast an unused integer (`(EEmployeeType)4`) as long as every consumer is patched. Cleaner alternative: reuse `EEmployeeType.Handler` and distinguish by a marker `MonoBehaviour`. |
| Prefab | Postfix `EmployeeManager.GetEmployeePrefab(EEmployeeType type)` to return your prefab. |
| Spawning | Postfix/prefix `EmployeeManager.RpcLogic___CreateEmployee_311954683(...)` (server body) or call `EmployeeManager.CreateEmployee_Server(...)` yourself. |
| Registration in the manager | Add to `EmployeeManager.AllEmployees` (public `List<Employee>`). |
| Config type | New `EConfigurableType` value (same caveat) + subclass `Il2CppScheduleOne.Management.EntityConfiguration`. |
| Config panel | Subclass `Il2CppScheduleOne.Management.UI.ConfigPanel`, override `BindInternal(List<EntityConfiguration>)`; register through `ManagementInterface.ConfigPanelPrefabs` or postfix `ManagementInterface.GetConfigPanelPrefab(EConfigurableType)`. |
| Worldspace UI | Subclass `Il2CppScheduleOne.UI.Management.WorldspaceUIElement`; hook `IConfigurable.CreateWorldspaceUI()`. |
| Console | Postfix `Il2CppScheduleOne.Console+AddEmployeeCommand.Execute(List<string> args)` to accept a new type token. |
| Save | Subclass `Il2CppScheduleOne.Persistence.Datas.EmployeeData` + `Il2CppScheduleOne.Persistence.Loaders.EmployeeLoader` (override `NPCType`, `CreateAndLoadEmployee`, `Load`). |
| Quest tracking | `Il2CppScheduleOne.Quests.Quest_Employees` filters by `EmployeeType`; add or skip. |

### 11.2 Adding a new behaviour

1. Create a `MonoBehaviour`-derived component… **no** — it must derive from
   `Il2CppScheduleOne.NPCs.Behaviour.Behaviour`, which derives from `Il2CppFishNet.Object.NetworkBehaviour`.
   Under IL2CPP that requires registering a managed type with Il2CppInterop
   (`ClassInjector.RegisterTypeInIl2Cpp<T>()`) **and** the FishNet `NetworkBehaviour` codegen
   (`NetworkInitialize___Early` / `NetworkInitialize__Late`) that IL2CPP-injected types do not get.
   ⚠️ **This is the single riskiest part of the mod.** See §12.
2. Safer path: **reuse an existing behaviour instance**. `Employee.MoveItemBehaviour` and
   `Employee.WaitOutside` already exist on every employee prefab; `NPCBehaviour.GetBehaviour<T>()` and
   `NPCBehaviour.GetBehaviour(System.String BehaviourName)` fetch any other.
3. Register it: `NPCBehaviour.behaviourStack` (public `List<Behaviour>`), set `Behaviour.Priority`,
   `Behaviour.BehaviourIndex`, `Behaviour.Name`, then `NPCBehaviour.SortBehaviourStack()`.

### 11.3 Driving an existing behaviour from a mod (server side)

```csharp
var beh = employee.MoveItemBehaviour;                 // or employee.Behaviour.GetBehaviour<MoveItemBehaviour>()
beh.Initialize(route, itemTemplate, _maxMoveAmount: 20, _skipPickup: false);
beh.Enable();                                          // -> NPCBehaviour.AddEnabledBehaviour
// force it to the front:
beh.Priority = 999; employee.Behaviour.SortBehaviourStack();
// stop it:
beh.StopCurrentActivity(); beh.EndTransit(); beh.Disable();
```

Useful Harmony targets:

| Purpose | Method |
|---|---|
| Insert your own work decision | `Employee.UpdateBehaviour()` (virtual, per subclass) |
| Veto/allow work | `Employee.CanWork()`, `Employee.ShouldIdle()`, `Employee.IsAnyWorkInProgress()` |
| Change route selection | `Packager.GetTransitRouteReady(out ItemInstance& item)`, `AdvancedTransitRoute.GetItemReadyToMove()` |
| Change route legality | `MoveItemBehaviour.IsTransitRouteValid(TransitRoute, ItemInstance, out System.String&)`, `MoveItemBehaviour.IsDestinationValid(...)` |
| Change carried amount | `MoveItemBehaviour.GetAmountToGrab()` |
| Intercept the actual transfer | `MoveItemBehaviour.TakeItem()`, `MoveItemBehaviour.PlaceItem()` |
| Change behaviour arbitration | `NPCBehaviour.GetEnabledBehaviour()`, `NPCBehaviour.SortBehaviourStack()`, `NPCBehaviour.Update()` |
| Networked state change bodies | `NPCBehaviour.RpcLogic___EnableBehaviour_Server_3316948804(System.Int32)` and the Disable/Activate/Deactivate/Pause/Resume equivalents |
| Wage/pay | `Employee.IsPayAvailable()`, `Employee.SetIsPaid()`, `Employee.RemoveDailyWage()`, `EmployeeHome.RemoveCash(System.Single)` |
| Bed validation | `Il2CppScheduleOne.ObjectScripts.BedItem.IsBedValid(BuildableItem, out System.String&)` (static), `EmployeeHome.IsBuildableEntityAValidEmployeeHome(BuildableItem, out System.String&)` (static) |
| Config panel routing | `ManagementInterface.GetConfigPanelPrefab(EConfigurableType)` |
| Console | `Il2CppScheduleOne.Console+AddEmployeeCommand.Execute(List<string>)` |

### 11.4 Moving items programmatically (no employee at all)

```csharp
// direct, server-side, no NPC involved:
int cap = destination.GetInputCapacityForItem(item, asker: null, checkPlayerFilters: true);
if (cap > 0) {
    var src = source.GetFirstSlotContainingTemplateItem(template, ITransitEntity.ESlotType.Output);
    int n = System.Math.Min(cap, src.Quantity);
    src.ChangeQuantity(-n);
    destination.InsertItemIntoInput(template.GetCopy(n), inserter: null);
}
```
Everything above is server-authoritative; the slot mutations replicate through
`StorageEntity.SetStoredInstance` / `SetItemSlotQuantity` ObserversRpcs automatically.

### 11.5 FishNet RPC cheat sheet for this subsystem

| Public entry point | Real body to patch |
|---|---|
| `EmployeeManager.CreateEmployee(...)` | `EmployeeManager.RpcLogic___CreateEmployee_311954683(...)` |
| `Employee.Initialize(...)` | `Employee.RpcLogic___Initialize_2260823878(...)` |
| `Employee.SendFire()` / `Employee.ReceiveFire()` | `Employee.RpcLogic___SendFire_2166136261()` / `RpcLogic___ReceiveFire_2166136261()` |
| `Employee.SendTransfer(string)` / `Employee.TransferToProperty(string)` | `RpcLogic___SendTransfer_3615296227(string)` / `RpcLogic___TransferToProperty_3615296227(string)` |
| `Employee.SubmitNoWorkReason(string, string, int)` | `RpcLogic___SubmitNoWorkReason_15643032(string, string, int)` |
| `NPCBehaviour.EnableBehaviour_Server(int)` | `RpcLogic___EnableBehaviour_Server_3316948804(int)` |
| `NPCBehaviour.EnableBehaviour_Client(conn, int)` | `RpcLogic___EnableBehaviour_Client_2681120339(conn, int)` |
| `<Employee subclass>.SetConfigurer(NetworkObject)` | `RpcLogic___SetConfigurer_3323014238(NetworkObject)` |
| `ConfigurationReplicator.SendRouteListField(int, AdvancedTransitRouteData[])` | `RpcLogic___SendRouteListField_3226448297(...)` |
| `ConfigurationReplicator.ReceiveRouteListField(int, AdvancedTransitRouteData[])` | `RpcLogic___ReceiveRouteListField_3226448297(...)` |
| `StorageEntity.SetStoredInstance(conn, int, ItemInstance)` | `RpcLogic___SetStoredInstance_2652194801(...)` / `RpcLogic___SetStoredInstance_Internal_2652194801(...)` |
| `NPCBehaviour.ConsumeProduct(product, bool)` | `RpcLogic___ConsumeProduct_3964170259(...)` |

> ⚠️ The `_<hash>` suffixes are FishNet codegen hashes derived from the method signature. They **will change**
> if Tyler changes a parameter. Resolve them at runtime by prefix
> (`m.Name.StartsWith("RpcLogic___EnableBehaviour_Server_")`), never hard-code the number.

---

## 12. Building a "Driver" employee — what exists vs what must be written

### 12.1 Already exists (reuse it, don't rewrite it)

| Need | Existing type/member | Verdict |
|---|---|---|
| Walk to an arbitrary container and shuttle items | **`Il2CppScheduleOne.NPCs.Behaviour.MoveItemBehaviour`**, already instanced on **every** `Employee` as `Employee.MoveItemBehaviour` | ✅ **Complete.** Handles walk→grab→walk→place, slot reservation, save/load, path-failure retry. Just call `Initialize(route, template, max, skipPickup)` + `Enable()`. |
| Route model with source, destination and item filter | `AdvancedTransitRoute`, `ManagementItemFilter`, `AdvancedTransitRouteData` | ✅ Complete, persistable, networked. |
| Route list on an employee's config | `PackagerConfiguration.Routes : RouteListField` + `RouteListFieldUI` + `RouteEntryUI` + `TransitEntitySelector` | ✅ Complete UI + replication for a multi-route employee. Copy `PackagerConfiguration` / `PackagerConfigPanel` verbatim. |
| "Which route has something ready?" | `Packager.GetTransitRouteReady(out ItemInstance& item)` + `AdvancedTransitRoute.GetItemReadyToMove()` | ✅ Copy the pattern. |
| Delivery bay as a transit endpoint | `Il2CppScheduleOne.Delivery.LoadingDock` (is an `ITransitEntity`); `Property.LoadingDocks`, `Property.LoadingDockCount` | ✅ Usable as a driver source/destination today. |
| Bed / home / wages | `EmployeeHome`, `Bed`, `BedItem`, `Employee.SigningFee/DailyWage/PaidForToday` | ✅ Type-agnostic; works for any `Employee` subclass. |
| Idle fallback | `Employee.WaitOutside : IdleBehaviour` | ✅ |
| Persist an interrupted delivery | `MoveItemBehaviour.GetSaveData()` / `Load(MoveItemData)`, `MoveItemData` | ✅ |
| NPC gets in/out of a car | `NPC.EnterVehicle(NetworkConnection, LandVehicle)` / `NPC.ExitVehicle()`, `LandVehicle.AddNPCOccupant(NPC)`, `LandVehicle.GetFirstFreeSeat()`, `VehicleSeat.isDriverSeat` | ✅ Exists and is networked. |
| Car autopilot | `Il2CppScheduleOne.Vehicles.AI.VehicleAgent.Navigate(Vector3, NavigationSettings, NavigationCallback)`, `.AutoDriving`, `.StopNavigating()`, `.EndDriving()` | ✅ Full pathfinding + steering/throttle PID, road graph, obstacle sweep, stuck detection, teleporter. |

### 12.2 Does any behaviour already drive a vehicle?

**Yes — exactly two, and both are police-only.**

* `Il2CppScheduleOne.NPCs.Behaviour.VehiclePatrolBehaviour` — drives a `LandVehicle` around a
  `VehiclePatrolRoute` (`Waypoints : Transform[]`). Has `DriveTo(UnityEngine.Vector3 location)`,
  `SetRoute(VehiclePatrolRoute)`, `StartPatrol()`, `NavigationCallback(VehicleAgent+ENavigationResult)`,
  `IsAsCloseAsPossible(Vector3, out Vector3&)`, `isDriving`, `Agent`.
* `Il2CppScheduleOne.NPCs.Behaviour.VehiclePursuitBehaviour` — drives a `LandVehicle` at a player.
  Same `DriveTo(Vector3)` / `Agent` / `NavigationCallback` surface plus `CheckExitVehicle()`,
  `SetAggressiveDriving(bool)`, `UpdateDestination()`.

**Neither is an employee behaviour and neither carries items.** `VehiclePatrolBehaviour` is the right
skeleton to copy for a driver: it is the *only* behaviour in the game that combines
`Behaviour` state management with `VehicleAgent` navigation.

`Il2CppScheduleOne.Delivery.DeliveryVehicle` (`Vehicle : LandVehicle`, `ActiveDelivery : DeliveryInstance`,
`Activate(DeliveryInstance)` / `Deactivate()`) is *not* NPC-driven — it is the scripted supplier delivery
truck. Not reusable as a driver.

### 12.3 Must be written

| Missing piece | Why | Suggested approach |
|---|---|---|
| A driver `Employee` subclass | No `Driver`/`Handler`/`Courier` class exists. | See §12.4 — **prefer a companion component over a subclass.** |
| A "drive to X, then MoveItem" composite behaviour | `MoveItemBehaviour` only walks (`Behaviour.SetDestination` → `NPCMovement`), and `VehiclePatrolBehaviour` only drives. Nothing bridges them. | Either (a) drive the two existing behaviours from a plain `MonoBehaviour` state machine on the employee, or (b) inject a new `Behaviour` subclass (risky under IL2CPP, see §12.5). |
| Route validity across properties | `MoveItemBehaviour.CanGetToSource/CanGetToDestination` use `NavMeshUtility.GetReachableAccessPoint` — a *walking* NavMesh check. Cross-property routes will fail it. | Harmony-patch `MoveItemBehaviour.IsTransitRouteValid(...)` / `CanGetToSource` / `CanGetToDestination` to accept "reachable by road" when the driver has a vehicle, or bypass `MoveItemBehaviour` for the long leg and only use it at each end. |
| Vehicle ownership/assignment field | No `ObjectField`/config field type for vehicles; `LandVehicle` is not a `BuildableItem`, so `ObjectField`/`ObjectSelector` cannot select it. | New `ConfigField` subclass (`VehicleField`) + a selector, or select the vehicle by `LandVehicle.GUID` through a `StringField`. |
| A dealer as a transit endpoint | `Il2CppScheduleOne.Economy.Dealer` is an NPC, not an `ITransitEntity`. | Wrap the dealer's inventory in a custom `ITransitEntity`, or hand off through the dealer's existing contract system. |
| `EEmployeeType` / `EConfigurableType` values | IL2CPP enums are fixed at build time. | Reuse `EEmployeeType.Handler` + a marker component, **or** cast an out-of-range int and patch every consumer (`GetEmployeePrefab`, `GetEmployeesByType`, `ConfigurableType.GetTypeName`, `ManagementInterface.GetConfigPanelPrefab`, `Quest_Employees`, `AddEmployeeCommand.Execute`). |
| Save data + loader | `EmployeeData` subclasses and `EmployeeLoader` subclasses are one-per-type. | Subclass both; register the loader with the property save system. |
| Config panel + worldspace UI | No driver panel/element. | Clone `PackagerConfigPanel` (it already has Bed + Stations + **Routes**) and `PackagerUIElement`. |

### 12.4 Can `Employee` be subclassed from a mod?

**Technically yes, practically no — use a companion component instead. Strong recommendation.**

`Employee : NPC : Il2CppFishNet.Object.NetworkBehaviour`. To subclass it from a MelonLoader mod you must
`ClassInjector.RegisterTypeInIl2Cpp<Driver>()`. That gives you a valid IL2CPP type, **but**:

* FishNet's IL weaver generates `NetworkInitialize___Early()`, `NetworkInitialize__Late()`,
  `NetworkInitializeIfDisabled()`, `ReadSyncVar___ScheduleOne_Employees_<Type>` and all `RpcWriter/Reader/Logic`
  members **at build time**. An injected type gets none of them, so SyncVars and RPCs on your class will not work
  and FishNet's runtime `NetworkBehaviour` registration may throw.
* Il2CppInterop-injected types cannot override IL2CPP `virtual` slots that were not declared virtual-injectable,
  which breaks `UpdateBehaviour()` / `GetSaveData()` / `Fire()` overriding.
* The prefab must be a real `NetworkObject` prefab registered in FishNet's `PrefabObjects` — you cannot
  register a runtime-created GameObject as a spawnable network prefab without more patching.

**Recommended architecture instead:**

1. Instantiate `EmployeeManager.Instance.PackagerPrefab` (i.e. an `EEmployeeType.Handler` employee) via
   `EmployeeManager.CreateEmployee_Server(...)`. You get a fully-working, network-spawnable, savable employee
   with `MoveItemBehaviour`, bed support, wages and dialogue for free.
2. Attach your own plain injected `MonoBehaviour` — call it `DriverController` — to the same GameObject.
   A plain `MonoBehaviour` injects cleanly; it needs no FishNet codegen. Run it **server-only**
   (`if (!InstanceFinder.IsServer) return;`).
3. `DriverController` owns the driver state machine and calls into existing engine parts:
   * `employee.Behaviour.GetBehaviour<VehiclePatrolBehaviour>()` (or your own `VehicleAgent.Navigate` calls)
     for the road leg,
   * `employee.MoveItemBehaviour.Initialize(...)` + `.Enable()` for the load/unload legs,
   * `employee.Behaviour.SortBehaviourStack()` / `Behaviour.Priority` for arbitration.
4. Harmony-patch `Packager.UpdateBehaviour()` with a prefix that returns `false` when a `DriverController`
   is present, so the packaging logic doesn't fight your driver logic.
5. Harmony-patch `Packager.GetSaveData()` / `PackagerLoader.Load(...)` to append/restore your driver state.
6. For the clipboard UI, patch `ManagementInterface.GetConfigPanelPrefab(EConfigurableType.Packager)` to return
   your driver panel when the selected `IConfigurable` has a `DriverController`.

This keeps **zero** injected `NetworkBehaviour`s, which is the only reliable way to ship this on IL2CPP.

### 12.5 If you insist on a real `Behaviour` subclass

Test this in isolation before committing:
`ClassInjector.RegisterTypeInIl2Cpp<DriveToBehaviour>()` where
`DriveToBehaviour : Il2CppScheduleOne.NPCs.Behaviour.Behaviour`, then add it to the employee GameObject,
push it into `NPCBehaviour.behaviourStack`, set `Name`/`Priority`/`BehaviourIndex`, call
`NPCBehaviour.SortBehaviourStack()`, and verify that `Enable()`/`Activate()` are called and that FishNet does
not throw during `NetworkInitializeIfDisabled()`. **UNVERIFIED whether this works on this build** — nothing in
the dumps can answer it. If it fails, fall back to §12.4.

---

## 13. Open questions / unverified

1. **Wage numbers.** `Employee.SigningFee` and `Employee.DailyWage` are Unity-serialized prefab floats. Not in
   the IL2CPP metadata. Read at runtime from `EmployeeManager.Instance.<Type>Prefab`.
2. **`addemployee` type-token parsing.** Verified: command word `addemployee`, description
   `Adds an employee of the specified type to the given property.`, example `addemployee botanist barn`, and the
   error `Unrecognized employee type '`. **Not verified:** whether the token is parsed with a
   case-insensitive `Enum.TryParse<EEmployeeType>` or a hand-written switch, and therefore whether
   `packager` is accepted as an alias for `handler`.
3. **Interop fallback names.** `Employee.Method_Protected_Virtual_Void_0`,
   `Behaviour.Method_Protected_Virtual_New_Void_0`, `NPCBehaviour.Method_Protected_Virtual_New_Void_0`,
   `Bed.Method_Private_Void_0`, and every `Method_Private_IEnumerator_PDM_*` map to methods whose names were
   stripped from metadata. The `MoveItemBehaviour` PDM→routine mapping in §5.4.1 is inferred from dump order,
   not proven. **Do not bind to these names.**
4. **`Behaviour.Enable_Networked()` → `NPCBehaviour.EnableBehaviour_*`** is inferred from naming and the
   `BehaviourIndex` parameter. No IL body confirms it.
5. **Selection key in `SortBehaviourStack` / `AddEnabledBehaviour`.** The lambdas
   `_SortBehaviourStack_b__43_0`, `_AddEnabledBehaviour_b__45_0`, `_RemoveEnabledBehaviour_b__46_0` all return
   `System.Int32`; `Behaviour.Priority` is the only plausible key but this is inferred.
6. **Exact pay sequence.** `IsPayAvailable()` → `SetIsPaid()` → `RemoveDailyWage()` → `EmployeeHome.RemoveCash()`
   is inferred from names. Which method resets `PaidForToday` at day rollover is unknown
   (`Employee.OnSleepEnd()` is the likely candidate).
7. **`ITransitEntity` implementor list** was derived by searching for the `AccessPoints` member, since
   Il2CppInterop emits the interface as a class and `04-interface-implementors.txt` therefore has no entry for
   it. A type that implements `ITransitEntity` but inherits `AccessPoints` from a base would be missed —
   none was found in the ScheduleOne namespaces, but this is a structural inference, not a proof.
8. **`research/raw/literals-ordered.txt` and `literals-sorted.txt` are corrupt** — they contain raw binary,
   not the 23,869 IL2CPP string literals. Every literal in this document (`addemployee`, the command
   description, `Employee limit reached (`, `Employee is about to place an item here`, etc.) was re-extracted
   read-only from `…\Schedule I_Data\il2cpp_data\Metadata\global-metadata.dat`. The literal-extraction step of
   the research tooling should be re-run before anyone trusts those two files.
9. **Save-file JSON discriminators.** The `DataType` string written into `Properties/<name>/Employees/*.json`
   is not recoverable from the dumps. Inspect a real save.
10. **Whether an injected `Behaviour` subclass works on this build.** Untestable from static dumps
    (§12.5). Prototype it before designing around it.
11. **Cross-property pathing.** Whether `NavMeshUtility.GetReachableAccessPoint` returns non-null for an
    entity on a different property is unknown; if it does, a walking driver would already "work" (slowly) and
    the vehicle leg becomes an optimisation rather than a requirement. Worth a 10-minute in-game test.
12. **`Employee.TradeItems()` / `TradeItemsDone()`** — no callers visible in the dumps and no parameters.
    Purpose **UNVERIFIED**; possibly the player↔employee handover path.

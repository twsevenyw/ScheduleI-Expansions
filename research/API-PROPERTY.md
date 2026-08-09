# Schedule I — Property / Business / Management / Storage / Item API

Target: MelonLoader + Il2CppInterop modding. Everything here is transcribed **verbatim** from the
dumps in `research/raw/`. Nothing was tidied, pluralised, or invented.

## How to read this document

* Namespaces are the **Il2CppInterop-prefixed** ones (`Il2CppScheduleOne.Property`). The original
  IL2CPP/CLR name is the same string without the `Il2Cpp` prefix (`ScheduleOne.Property`) — that is
  what appears in `ObfuscatedName` / `OriginalName` attributes, in Harmony-by-string patches, and in
  save-file type discriminators.
* Il2CppInterop exposes IL2CPP **instance fields as C# properties**. That is why you see both
  `_Foo_k__BackingField` and `Foo`. **Always prefer `Foo`.**
* Names like `field_Private_Boolean_0`, `Method_Protected_Virtual_Void_0`,
  `Method_Private_IEnumerator_PDM_0` are Il2CppInterop fallbacks for members whose real name was
  stripped from `global-metadata.dat`. **They are index-based and WILL renumber between game
  versions.** Never bind to them by name in shipping code without a version guard.
* `(inferred …)` = deduced, not read off a signature. **UNVERIFIED** = could not be checked from the
  available dumps at all.

### ⚠ Two structural gotchas you must internalise before reading further

**1. IL2CPP interfaces are emitted by Il2CppInterop as `class : Il2CppObjectBase`, not as C#
interfaces.** `Il2CppScheduleOne.Management.ITransitEntity`, `IConfigurable`, `IUsable`,
`IItemSlotOwner`, `ISaveable`, `IGUIDRegisterable` all appear in `02-types-index.txt` as
`public class … | base: Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase`. Consequences:

* `research/raw/04-interface-implementors.txt` contains **no** `IFACE Il2CppScheduleOne.Management.ITransitEntity`
  entry — .NET reflection sees no implementors because the managed proxies do not inherit it.
* In your mod you must use pointer re-casting, not C# `is`/`as`:
  `var te = someGridItem.TryCast<ITransitEntity>();` / `.Cast<ITransitEntity>()`.
* The implementor list in this document was rebuilt by locating the concrete types that
  **declare `ITransitEntity`'s abstract members** (`AccessPoints`, `LinkOrigin`, `Selectable`,
  `IsAcceptingItems`, `InputSlots`, `OutputSlots`). See §3.

**2. The `global-metadata.dat` string table is de-duplicated.** In
`strings-global-metadata-ordered.txt` a type's block only lists strings that had not already been
emitted by an earlier type. So the absence of `get_Name` / `get_GUID` / `ShowOutline` from the
`ITransitEntity` block does **not** mean those members don't exist — they were emitted earlier by
other types. Do not read "member missing" into that file.

---

## 1. Assemblies & namespaces

### Assemblies

| Assembly | Types | Notes |
|---|---|---|
| `Assembly-CSharp` | 3597 | Everything under `Il2CppScheduleOne.*` covered here except `Core.*` |
| `Il2CppScheduleOne.Core` | — | `Il2CppScheduleOne.Core.Items.Framework` (base item types) lives here |
| `Assembly-CSharp-firstpass` | 625 | not used by anything in scope |
| `Il2CppFishNet.Runtime` | — | `NetworkBehaviour`, `NetworkObject`, `NetworkConnection`, RPC plumbing |
| `S1API` | — | third-party modding wrapper already present in the install; useful cross-check, not part of the game |

`Assembly-CSharp` path on this machine:
`C:\Program Files (x86)\Steam\steamapps\common\Schedule I\MelonLoader\Il2CppAssemblies\Assembly-CSharp.dll`

### Namespaces in scope (counts from `01-namespaces.txt`)

| Namespace | Types | Role |
|---|---|---|
| `Il2CppScheduleOne.Property` | 14 | Property/Business/managers/laundering |
| `Il2CppScheduleOne.Management` | 38 | `ITransitEntity`, `TransitRoute`, all `*Configuration` + `*Field` types |
| `Il2CppScheduleOne.Management.UI` | 1 | `ConfigPanel` base only |
| `Il2CppScheduleOne.UI.Management` | 49 | clipboard screens, per-type config panels, selectors, worldspace UI |
| `Il2CppScheduleOne.Storage` | 17 | `StorageEntity`, `StorageGrid`, `StoredItem`, `WorldStorageEntity` |
| `Il2CppScheduleOne.ItemFramework` | 40 | `ItemSlot`, `ItemInstance`, `ItemDefinition`, filters |
| `Il2CppScheduleOne.Core.Items.Framework` | 4 | `BaseItemDefinition`, `BaseItemInstance`, `EItemCategory`, `ELegalStatus` |
| `Il2CppScheduleOne.EntityFramework` | 8 | `BuildableItem` / `GridItem` / `SurfaceItem` / `ProceduralGridItem` |
| `Il2CppScheduleOne.StationFramework` | 18 | `StationRecipe`, `StationItem`, `MushroomSpawnStation`, liquid sim |
| `Il2CppScheduleOne.ObjectScripts` | 42 | all concrete stations & placeable storages |
| `Il2CppScheduleOne.Building` / `.Building.Doors` | 19 / 2 | `BuildManager`, `Surface`, `PropertyDoorController` |
| `Il2CppScheduleOne.Persistence` | 15 | `SaveManager`, `LoadManager`, `ISaveable` |
| `Il2CppScheduleOne.Persistence.Datas` | 131 | all save DTOs |
| `Il2CppScheduleOne.Persistence.Loaders` | 68 | one loader per saveable |
| `Il2CppScheduleOne.Growing` | 22 | `GrowContainer` (base of `Pot`, `MushroomBed`) — an `ITransitEntity` |
| `Il2CppScheduleOne.Delivery` | 7 | `LoadingDock` — an `ITransitEntity` |
| `Il2CppScheduleOne.Employees` | 9 | `Employee` + 4 worker types; owns `MoveItemBehaviour` |

---

## 2. `Il2CppScheduleOne.Property`

14 types: `Bungalow`, `Business`, `BusinessManager`, `LaunderingOperation`, `Manor`, `MotelRoom`,
`Property`, `PropertyContentsContainer`, `PropertyDisposalArea`, `PropertyManager`, `RV`,
`SewerOffice`, `Sweatshop`, `Tap`.

### 2.1 `Property` — full member dump

`base: Il2CppFishNet.Object.NetworkBehaviour`. It is an `ISaveable` (the `SaveFolderName` /
`SaveFileName` / `Loader` / `ShouldSaveUnderFolder` / `LocalExtraFiles` / `LocalExtraFolders` /
`HasChanged` property block plus `GetSaveString` / `InitializeSaveable` / `WriteData` /
`DeleteUnapprovedFiles` is exactly the `Il2CppScheduleOne.Persistence.ISaveable` surface).

```csharp
// ---- static registries (THIS is how you enumerate properties at runtime) ----
static System.Int32  IdealCullsPerSecond              { get; set; }
static System.Single MaxCullDuration                  { get; set; }
static System.Single CullingDistanceBuffer            { get; set; }
static System.Single DecullHardThreshold              { get; set; }
static Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Property.Property> Properties        { get; set; }
static Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Property.Property> UnownedProperties { get; set; }
static Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Property.Property> OwnedProperties   { get; set; }
static Il2CppScheduleOne.Property.Property+PropertyChange onPropertyAcquired { get; set; }

// ---- identity / economy ----
UnityEngine.Events.UnityEvent onThisPropertyAcquired { get; set; }
System.Boolean _IsOwned_k__BackingField              { get; set; }
System.String  propertyName                          { get; set; }   // serialized field
System.Boolean AvailableInDemo                       { get; set; }
System.String  propertyCode                          { get; set; }   // serialized field — the "barn"/"manor" code
System.Single  Price                                 { get; set; }
System.Single  DefaultRotation                       { get; set; }
System.Int32   EmployeeCapacity                      { get; set; }
System.Boolean OwnedByDefault                        { get; set; }
System.String  IsOwnedVariable                       { get; set; }   // name of a ScheduleOne.Variables variable
System.String  PropertyName                          { get; }        // public read-only view of propertyName
System.String  PropertyCode                          { get; }        // public read-only view of propertyCode
System.Boolean IsOwned                               { get; set; }

// ---- employees & beds ----
Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Employees.Employee> _Employees_k__BackingField { get; set; }
Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Employees.Employee> Employees                  { get; set; }
UnityEngine.Transform EmployeeContainer { get; set; }
Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<UnityEngine.Transform> EmployeeIdlePoints { get; set; }

// ---- contents / build system ----
Il2CppScheduleOne.Property.PropertyContentsContainer _Container_k__BackingField { get; set; }
Il2CppScheduleOne.Property.PropertyContentsContainer Container                  { get; set; }
Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.EntityFramework.BuildableItem> BuildableItems { get; set; }
Il2CppSystem.Action<Il2CppScheduleOne.EntityFramework.BuildableItem> onBuildableItemAdded   { get; set; }
Il2CppSystem.Action<Il2CppScheduleOne.EntityFramework.BuildableItem> onBuildableItemRemoved { get; set; }
Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Management.IConfigurable> Configurables { get; set; }
Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Tiles.Grid> Grids { get; set; }
Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<UnityEngine.BoxCollider> propertyBoundsColliders { get; set; }

// ---- world refs ----
UnityEngine.RectTransform _WorldspaceUIContainer_k__BackingField { get; set; }
UnityEngine.RectTransform WorldspaceUIContainer { get; set; }
System.Boolean _IsContentCulled_k__BackingField { get; set; }
System.Boolean IsContentCulled                  { get; set; }
System.Single  _AmbientTemperature_k__BackingField { get; set; }
System.Single  AmbientTemperature                  { get; set; }
System.Boolean ContentCullingEnabled  { get; set; }
System.Single  MinimumCullingDistance { get; set; }
Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<UnityEngine.GameObject> ObjectsToCull { get; set; }
UnityEngine.Transform SpawnPoint          { get; set; }
UnityEngine.Transform InteriorSpawnPoint  { get; set; }
UnityEngine.Transform NPCSpawnPoint       { get; set; }
UnityEngine.Transform ListingPoster       { get; set; }
UnityEngine.GameObject ForSaleSign        { get; set; }
UnityEngine.GameObject BoundingBox        { get; set; }
Il2CppScheduleOne.Map.POI PoI             { get; set; }
Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Misc.ModularSwitch> Switches { get; set; }
Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Interaction.InteractableToggleable> Toggleables { get; set; }
Il2CppScheduleOne.Property.PropertyDisposalArea DisposalArea { get; set; }
Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppScheduleOne.Delivery.LoadingDock> LoadingDocks { get; set; }
System.Int32 LoadingDockCount { get; }
UnityEngine.Coroutine cullingRoutine { get; set; }

// ---- ISaveable surface ----
Il2CppScheduleOne.Persistence.Loaders.PropertyLoader loader { get; set; }
Il2CppSystem.Collections.Generic.List<System.String> _LocalExtraFiles_k__BackingField   { get; set; }
Il2CppSystem.Collections.Generic.List<System.String> _LocalExtraFolders_k__BackingField { get; set; }
System.Boolean _HasChanged_k__BackingField { get; set; }
Il2CppSystem.Collections.Generic.List<System.String> savedObjectPaths   { get; set; }
Il2CppSystem.Collections.Generic.List<System.String> savedEmployeePaths { get; set; }
System.String  SaveFolderName        { get; }
System.String  SaveFileName          { get; }
Il2CppScheduleOne.Persistence.Loaders.Loader Loader { get; }
System.Boolean ShouldSaveUnderFolder { get; }
Il2CppSystem.Collections.Generic.List<System.String> LocalExtraFiles   { get; set; }
Il2CppSystem.Collections.Generic.List<System.String> LocalExtraFolders { get; set; }
System.Boolean HasChanged { get; set; }

// ---- interop fallbacks (unstable names) ----
System.Boolean field_Private_Boolean_0 { get; set; }
System.Boolean field_Private_Boolean_1 { get; set; }
```

Constructors:

```csharp
private .ctor()
public  .ctor()
public  .ctor(System.IntPtr pointer)
```

Methods (all 59, verbatim):

```csharp
public System.Void AddBuildableItem(Il2CppScheduleOne.EntityFramework.BuildableItem item)
public System.Void AddConfigurable(Il2CppScheduleOne.Management.IConfigurable configurable)
public virtual System.Void Awake()
public virtual System.Boolean CanBePurchased()
public virtual System.Boolean CanDeliverToProperty()
public virtual System.Boolean CanRespawnInsideProperty()
public virtual System.Void DeleteUnapprovedFiles(System.String parentFolderPath)
public System.Void DeregisterEmployee(Il2CppScheduleOne.Employees.Employee emp)
public System.Boolean DoBoundsContainPoint(UnityEngine.Vector3 point)
public virtual System.Void FixedUpdate()
public Il2CppSystem.Collections.Generic.List<T> GetBuildablesOfType<T>()
public Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Persistence.Datas.DynamicSaveData> GetEmployeeSaveDatas()
public virtual System.Void GetNetworth(Il2CppScheduleOne.Money.MoneyManager+FloatContainer container)
public Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Persistence.Datas.DynamicSaveData> GetObjectSaveDatas()
public virtual System.String GetSaveString()
public Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ObjectScripts.Bed> GetUnassignedBeds()
public virtual System.Void InitializeSaveable()
public System.Boolean IsPointInsideBox(UnityEngine.Vector3 worldPoint, UnityEngine.BoxCollider box)
public virtual System.Void Load(Il2CppScheduleOne.Persistence.Datas.PropertyData propertyData, System.String propertyDataString)
public Il2CppSystem.Collections.IEnumerator Method_Private_IEnumerator_PDM_0()
public virtual System.Void Method_Protected_Virtual_New_Void_0()
public virtual System.Void NetworkInitializeIfDisabled()
public virtual System.Void NetworkInitialize__Late()
public virtual System.Void NetworkInitialize___Early()
public virtual System.Void OnDestroy()
public virtual System.Void OnSpawnServer(Il2CppFishNet.Connection.NetworkConnection connection)
public virtual System.Void OnStartServer()
public System.Void ReceiveOwned_Networked()
public virtual System.Void RecieveOwned()                     // sic — misspelled in the game
public System.Int32 RegisterEmployee(Il2CppScheduleOne.Employees.Employee emp)
public System.Void RemoveBuildableItem(Il2CppScheduleOne.EntityFramework.BuildableItem item)
public System.Void RemoveConfigurable(Il2CppScheduleOne.Management.IConfigurable configurable)
public System.Void RpcLogic___ReceiveOwned_Networked_2166136261()
public System.Void RpcLogic___SendToggleableState_3658436649(System.Int32 index, System.Boolean state)
public System.Void RpcLogic___SetOwned_Server_2166136261()
public System.Void RpcLogic___SetToggleableState_338960014(Il2CppFishNet.Connection.NetworkConnection conn, System.Int32 index, System.Boolean state)
public System.Void RpcReader___Observers_ReceiveOwned_Networked_2166136261(Il2CppFishNet.Serializing.PooledReader PooledReader0, Il2CppFishNet.Transporting.Channel channel)
public System.Void RpcReader___Observers_SetToggleableState_338960014(Il2CppFishNet.Serializing.PooledReader PooledReader0, Il2CppFishNet.Transporting.Channel channel)
public System.Void RpcReader___Server_SendToggleableState_3658436649(Il2CppFishNet.Serializing.PooledReader PooledReader0, Il2CppFishNet.Transporting.Channel channel, Il2CppFishNet.Connection.NetworkConnection conn)
public System.Void RpcReader___Server_SetOwned_Server_2166136261(Il2CppFishNet.Serializing.PooledReader PooledReader0, Il2CppFishNet.Transporting.Channel channel, Il2CppFishNet.Connection.NetworkConnection conn)
public System.Void RpcReader___Target_SetToggleableState_338960014(Il2CppFishNet.Serializing.PooledReader PooledReader0, Il2CppFishNet.Transporting.Channel channel)
public System.Void RpcWriter___Observers_ReceiveOwned_Networked_2166136261()
public System.Void RpcWriter___Observers_SetToggleableState_338960014(Il2CppFishNet.Connection.NetworkConnection conn, System.Int32 index, System.Boolean state)
public System.Void RpcWriter___Server_SendToggleableState_3658436649(System.Int32 index, System.Boolean state)
public System.Void RpcWriter___Server_SetOwned_Server_2166136261()
public System.Void RpcWriter___Target_SetToggleableState_338960014(Il2CppFishNet.Connection.NetworkConnection conn, System.Int32 index, System.Boolean state)
public System.Void SendToggleableState(System.Int32 index, System.Boolean state)
public System.Void SetBoundsVisible(System.Boolean vis)
public virtual System.Void SetContentCulled(System.Boolean culled)
public System.Void SetOwned()
public System.Void SetOwned_Server()
public System.Void SetToggleableState(Il2CppFishNet.Connection.NetworkConnection conn, System.Int32 index, System.Boolean state)
public virtual System.Boolean ShouldSave()
public virtual System.Void Start()
public System.Void ToggleableActioned(Il2CppScheduleOne.Interaction.InteractableToggleable toggleable)
public System.Void UpdateCulling()
public virtual Il2CppSystem.Collections.Generic.List<System.String> WriteData(System.String parentFolderPath)
public System.Void _Awake_b__98_0(System.Boolean <p0>)
public System.Boolean _RecieveOwned_b__111_1()
```

Nested types:

```csharp
public sealed class Property+PropertyChange : Il2CppSystem.MulticastDelegate
    public virtual System.Void Invoke(Il2CppScheduleOne.Property.Property property)
    public static Property+PropertyChange op_Implicit(System.Action<Il2CppScheduleOne.Property.Property> = null)
    public static Property+PropertyChange op_Addition(Property+PropertyChange = null, Property+PropertyChange = null)
    public static Property+PropertyChange op_Subtraction(Property+PropertyChange = null, Property+PropertyChange = null)
private sealed class Property+MethodInfoStoreGeneric_GetBuildablesOfType_Public_List_1_T_0`1   // generic-method cache
public sealed class Property+ObjectCompilerGeneratedNPrivateSealedIEnumerator1ObjectIEnumeratorIDisposableInObPrObObUnique
    // [ObfuscatedName("ScheduleOne.Property.Property+<<RecieveOwned>g__Wait|111_0>d")]
public sealed class Property+__c                                // <>c   — GetUnassignedBeds predicate
public sealed class Property+__c__DisplayClass98_0              // Awake toggleable closure
public sealed class Property+__c__DisplayClass116_0             // SetContentCulled closure
public struct       Property+__c__DisplayClass116_1             // fields: cameraTooClose, decullHardSqr
```

**Ownership flow.** `SetOwned()` is the local/entry call. `SetOwned_Server()` is a FishNet
**ServerRpc** (`RpcWriter___Server_SetOwned_Server_2166136261` +
`RpcReader___Server_SetOwned_Server_2166136261`; body = `RpcLogic___SetOwned_Server_2166136261`).
`ReceiveOwned_Networked()` is the **ObserversRpc** that fans the result out
(`RpcWriter___Observers_ReceiveOwned_Networked_2166136261`); `RecieveOwned()` is the overridable
per-subclass reaction (inferred from the writer/reader prefixes — this is the standard FishNet
codegen triple, high confidence).

**There is no region / `EMapRegion` member on `Property`.** Grepping the whole
`ns-Il2CppScheduleOne.Property.txt` for `Region` returns nothing. The nearest thing is
`Il2CppScheduleOne.Map.POI PoI`, and `Il2CppScheduleOne.Map.EMapRegion` exists as an enum elsewhere.
Region association is **UNVERIFIED**.

**There is no `Beds` collection.** Beds are reached via `GetUnassignedBeds()` →
`List<Il2CppScheduleOne.ObjectScripts.Bed>`, or by filtering `BuildableItems` /
`GetBuildablesOfType<Il2CppScheduleOne.ObjectScripts.BedItem>()`.

**There is no `StorageEntities` collection either.** Use
`GetBuildablesOfType<Il2CppScheduleOne.ObjectScripts.PlaceableStorageEntity>()`.

### 2.2 The 7 `Property` subclasses

All 7 have `base: Il2CppScheduleOne.Property.Property`.

#### `Bungalow`
```csharp
Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ObjectScripts.Pot> pots { get; set; }
Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ObjectScripts.PackagingStation> packagingStations { get; set; }
System.Boolean field_Private_Boolean_0 { get; set; }
System.Boolean field_Private_Boolean_1 { get; set; }

public virtual System.Void Awake()
public System.Void BuildableItemAdded(Il2CppScheduleOne.EntityFramework.BuildableItem item)
public System.Void BuildableItemRemoved(Il2CppScheduleOne.EntityFramework.BuildableItem item)
public virtual System.Void NetworkInitializeIfDisabled()
public virtual System.Void NetworkInitialize__Late()
public virtual System.Void NetworkInitialize___Early()
public virtual System.Void Start()
public System.Void UpdateVariables()
```
Difference: tracks pots + packaging stations and pushes counts into `ScheduleOne.Variables`
(matching literals `Bungalow_Pots`, `Bungalow_Seed_Pots`, `Bungalow_Soil_Pots`,
`Bungalow_Watered_Pots`, `Bungalow_PackagingStations`). Nested `Bungalow+__c` holds a
`Predicate<AdditiveDefinition>`.

#### `MotelRoom`
Same shape as `Bungalow` plus `mixingStations`, plus one override:
```csharp
Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ObjectScripts.Pot> pots { get; set; }
Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ObjectScripts.PackagingStation> packagingStations { get; set; }
Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ObjectScripts.MixingStation> mixingStations { get; set; }

public virtual System.Boolean CanDeliverToProperty()   // <-- only MotelRoom + RV + Business override this
```
Variable literals: `Motel_Pots`, `Motel_Seed_Pots`, `Motel_Soil_Pots`, `Motel_Watered_Pots`,
`Motel_MixingStations`, `Motel_PackagingStations`.

#### `Sweatshop`
Identical member set to `MotelRoom` **minus** `CanDeliverToProperty`:
```csharp
List<Pot> pots; List<PackagingStation> packagingStations; List<MixingStation> mixingStations;
public virtual System.Void Awake()
public System.Void BuildableItemAdded(BuildableItem item)
public System.Void BuildableItemRemoved(BuildableItem item)
public virtual System.Void Start()
public System.Void UpdateVariables()
```
Variable literals: `Sweatshop_Pots`, `Sweatshop_MixingStations`, `Sweatshop_PackagingStations`.

#### `Manor` — the most divergent
```csharp
static System.Int32 REBUILD_AFTER_DAYS    { get; set; }
static System.Int32 REBUILD_DURATION_DAYS { get; set; }
Il2CppScheduleOne.Property.Manor+EManorState _ManorState_k__BackingField { get; set; }
System.Int32   _DaysSinceStateChange_k__BackingField { get; set; }
System.Boolean _TunnelDug_k__BackingField { get; set; }
UnityEngine.GameObject OriginalContainer     { get; set; }
UnityEngine.GameObject DestroyedContainer    { get; set; }
UnityEngine.GameObject RebuiltContainer      { get; set; }
UnityEngine.GameObject DestructionFXContainer{ get; set; }
UnityEngine.GameObject TunnelBlocker         { get; set; }
UnityEngine.GameObject TunnelCollapse        { get; set; }
UnityEngine.GameObject ConstructionContainer { get; set; }
Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppScheduleOne.Audio.AudioSourceController> ExplosionSounds { get; set; }
Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<UnityEngine.GameObject> DisableOnRebuild { get; set; }
Il2CppSystem.Action onRebuildComplete { get; set; }
Il2CppScheduleOne.Property.Manor+EManorState ManorState { get; set; }
System.Int32   DaysSinceStateChange { get; set; }
System.Boolean TunnelDug            { get; set; }

public virtual System.Boolean CanBePurchased()
public System.Void DigTunnel()
public System.Void Explode()
public virtual System.String GetSaveString()
public virtual System.Void Load(Il2CppScheduleOne.Persistence.Datas.PropertyData propertyData, System.String propertyDataString)
public System.Void OnSleepEnd()
public System.Void Rebuild()
public virtual System.Void RecieveOwned()
public System.Void SetDestroyedIfOriginal()
public System.Void SetManorState(Il2CppFishNet.Connection.NetworkConnection conn, Il2CppScheduleOne.Property.Manor+EManorState state, System.Boolean resetStateChangeTimer)
public System.Void SetTunnelDug(Il2CppFishNet.Connection.NetworkConnection conn, System.Boolean dug)
public virtual System.Boolean ShouldSave()
// + RpcLogic___/RpcReader___Observers_/RpcReader___Target_/RpcWriter___Observers_/RpcWriter___Target_
//   variants of SetManorState_365422978 and SetTunnelDug_214505783

public enum Manor+EManorState : System.Int32 { Original = 0, Destroyed = 1, Rebuilt = 2 }
```
Its save DTO is `Il2CppScheduleOne.Persistence.Datas.ManorData`. Note `SetManorState` /
`SetTunnelDug` have **Observers + Target** writers but no Server writer → they are server-authored
broadcasts, not client-callable RPCs (inferred from the writer prefixes).

#### `RV`
```csharp
UnityEngine.Transform ModelContainer { get; set; }
UnityEngine.Transform FXContainer    { get; set; }
UnityEngine.Events.UnityEvent onExplode        { get; set; }
UnityEngine.Events.UnityEvent onDestroyedState { get; set; }
System.Boolean _IsDestroyed_k__BackingField { get; set; }
System.Boolean _exploded { get; set; }
System.Boolean IsDestroyed { get; set; }

public System.Void BlowUp()
public virtual System.Boolean CanDeliverToProperty()
public virtual System.Boolean CanRespawnInsideProperty()
public static Il2CppSystem.Collections.IEnumerator Method_Internal_Static_IEnumerator_PDM_0()
public System.Void OnSleep()
public System.Void SetDestroyed()
public System.Void SetDestroyed_Client(Il2CppFishNet.Connection.NetworkConnection conn)
public virtual System.Boolean ShouldSave()
public System.Void UpdateVariables()
// + RpcLogic___SetDestroyed_Client_328543758 / RpcReader___Target_… / RpcWriter___Target_…
```

#### `SewerOffice`
```csharp
System.String DefaultSaveFilePath { get; }
public virtual System.Void Awake()
public System.String GetDefaultSaveFileFullPath()
public System.Void OnPasscodeCorrect()
public virtual System.Boolean ShouldSave()
```
Unique: ships with a *default* save file (used to preload the sewer office contents) and unlocks via
`OnPasscodeCorrect()` instead of a purchase.

#### `Business` — see §2.3.

### 2.3 `Il2CppScheduleOne.Property.Business`

`base: Il2CppScheduleOne.Property.Property`. **There are no `Business` subclasses** — all businesses
are `Business` instances differentiated only by `propertyCode` / prefab.

```csharp
static Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Property.Business> Businesses        { get; set; }
static Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Property.Business> UnownedBusinesses { get; set; }
static Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Property.Business> OwnedBusinesses   { get; set; }
static Il2CppSystem.Action<Il2CppScheduleOne.Property.LaunderingOperation> onOperationStarted  { get; set; }
static Il2CppSystem.Action<Il2CppScheduleOne.Property.LaunderingOperation> onOperationFinished { get; set; }

System.Single LaunderCapacity { get; set; }
Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Property.LaunderingOperation> LaunderingOperations { get; set; }
Il2CppScheduleOne.Persistence.Loaders.BusinessLoader loader { get; set; }
System.Single currentLaunderTotal   { get; }   // sum over LaunderingOperations (see Business+__c)
System.Single appliedLaunderLimit   { get; }
Il2CppScheduleOne.Persistence.Loaders.Loader Loader { get; }

public virtual System.Void Awake()
public virtual System.Boolean CanDeliverToProperty()
public System.Void CompleteOperation(Il2CppScheduleOne.Property.LaunderingOperation op)
public virtual System.Void GetNetworth(Il2CppScheduleOne.Money.MoneyManager+FloatContainer container)
public virtual System.String GetSaveString()
public virtual System.Void Load(Il2CppScheduleOne.Persistence.Datas.PropertyData propertyData, System.String propertyDataString)
public virtual System.Void Method_Protected_Virtual_Void_0()
public virtual System.Void MinPass()
public virtual System.Void MinsPass(System.Int32 mins)
public virtual System.Void OnDestroy()
public virtual System.Void OnSpawnServer(Il2CppFishNet.Connection.NetworkConnection connection)
public System.Void ReceiveLaunderingOperation(Il2CppFishNet.Connection.NetworkConnection conn, System.Single amount, System.Int32 minutesSinceStarted = 0)
public virtual System.Void RecieveOwned()
public System.Void StartLaunderingOperation(System.Single amount, System.Int32 minutesSinceStarted = 0)
public System.Void TimeSkipped(System.Int32 minsPassed)
public virtual System.Void Start()

// FishNet triples:
//   StartLaunderingOperation   -> RpcWriter___Server_StartLaunderingOperation_1481775633   (ServerRpc)
//                                 RpcLogic___StartLaunderingOperation_1481775633
//                                 RpcReader___Server_StartLaunderingOperation_1481775633
//   ReceiveLaunderingOperation -> RpcWriter___Observers_ReceiveLaunderingOperation_1001022388
//                                 RpcWriter___Target_ReceiveLaunderingOperation_1001022388
//                                 RpcLogic___ReceiveLaunderingOperation_1001022388
//                                 RpcReader___Observers_… / RpcReader___Target_…
```

`Il2CppScheduleOne.Property.LaunderingOperation` (plain `Il2CppSystem.Object`):

```csharp
Il2CppScheduleOne.Property.Business business { get; set; }
System.Single amount                { get; set; }
System.Int32  minutesSinceStarted   { get; set; }
System.Int32  completionTime_Minutes{ get; set; }
public .ctor(Il2CppScheduleOne.Property.Business _business, System.Single _amount, System.Int32 _minutesSinceStarted)
```

The in-world laundering hardware is `Il2CppScheduleOne.ObjectScripts.LaunderingStation`
(`base: GridItem`) holding an `Il2CppScheduleOne.UI.LaunderingInterface Interface` and an
`Il2CppScheduleOne.ObjectScripts.CashCounter CashCounter`.

### 2.4 The registries — `PropertyManager` / `BusinessManager`

Both are `Il2CppScheduleOne.DevUtilities.Singleton<T>` and `ISaveable`. **`PropertyManager` is the
name you asked me to verify — it is real.** Note however that the *runtime enumeration* of
properties does **not** go through the manager: it goes through the static lists on `Property` /
`Business`.

```csharp
public class Il2CppScheduleOne.Property.PropertyManager
    : Il2CppScheduleOne.DevUtilities.Singleton<Il2CppScheduleOne.Property.PropertyManager>
{
    Il2CppScheduleOne.Persistence.Loaders.PropertiesLoader loader { get; set; }
    System.String SaveFolderName { get; }      // "Properties"  (literal verified)
    System.String SaveFileName   { get; }
    Il2CppScheduleOne.Persistence.Loaders.Loader Loader { get; }
    System.Boolean ShouldSaveUnderFolder { get; }
    System.Int32 LoadOrder { get; }
    // + LocalExtraFiles / LocalExtraFolders / HasChanged

    public virtual System.Void Awake()
    public virtual System.Void DeleteUnapprovedFiles(System.String parentFolderPath)
    public Il2CppScheduleOne.Property.Property GetNearestProperty(UnityEngine.Vector3 point, System.Boolean includeOwned = True, System.Boolean includeUnowned = True, System.Boolean includeBusinesses = True)
    public Il2CppScheduleOne.Property.Property GetProperty(System.String code)
    public virtual System.String GetSaveString()
    public virtual System.Void InitializeSaveable()
    public System.Void LoadProperty(Il2CppScheduleOne.Persistence.Datas.PropertyData propertyData, System.String propertyDataString)
    public static Il2CppScheduleOne.Property.Property Method_Internal_Static_Property_Property_Single_Property_byref_Single_byref___c__DisplayClass31_0_PDM_0(Il2CppScheduleOne.Property.Property existingClosest, System.Single existingClosestDist, Il2CppScheduleOne.Property.Property candidate, out System.Single& dist, ref Il2CppScheduleOne.Property.PropertyManager+__c__DisplayClass31_0& A_4)
    public virtual Il2CppSystem.Collections.Generic.List<System.String> WriteData(System.String parentFolderPath)
}

public class Il2CppScheduleOne.Property.BusinessManager
    : Il2CppScheduleOne.DevUtilities.Singleton<Il2CppScheduleOne.Property.BusinessManager>
{
    Il2CppScheduleOne.Persistence.Loaders.BusinessesLoader loader { get; set; }
    // same ISaveable block; SaveFolderName is "Businesses" (literal verified)

    public virtual System.Void Awake()
    public virtual System.Void DeleteUnapprovedFiles(System.String parentFolderPath)
    public virtual System.String GetSaveString()
    public virtual System.Void InitializeSaveable()
    public System.Void LoadBusiness(Il2CppScheduleOne.Persistence.Datas.BusinessData businessData, System.String dataString)
    public virtual Il2CppSystem.Collections.Generic.List<System.String> WriteData(System.String parentFolderPath)
}
```

`PropertyManager.GetProperty(code)` searches both `Property` and `Business` — its two closures are
`_GetProperty_b__0(Il2CppScheduleOne.Property.Property p)` and
`_GetProperty_b__1(Il2CppScheduleOne.Property.Property p)`; `LoadProperty`'s closures are
`_LoadProperty_b__0(Property p)` and `_LoadProperty_b__1(Business p)`, which confirms the
"properties then businesses" two-pass lookup.

**Enumerating owned properties at runtime — the verified way:**

```csharp
// server or client, both work: these are plain static Lists maintained in Property.Awake/RecieveOwned
foreach (var p in Il2CppScheduleOne.Property.Property.OwnedProperties)   { /* p.PropertyCode, p.PropertyName */ }
foreach (var b in Il2CppScheduleOne.Property.Business.OwnedBusinesses)   { /* b.LaunderCapacity ... */ }
// or by code:
var barn = Il2CppScheduleOne.DevUtilities.Singleton<Il2CppScheduleOne.Property.PropertyManager>
             .Instance.GetProperty("barn");
```
(`Singleton<T>.Instance` accessor name is **UNVERIFIED** from these dumps — I did not dump
`Il2CppScheduleOne.DevUtilities.Singleton\`1`. Confirm the accessor before shipping.)

### 2.5 Property / business **codes**

`propertyCode` is a Unity-serialized `string` on each prefab, so the values live in the scene /
prefab assets, **not** in `global-metadata.dat`. Only these are verifiable from the literal dumps:

| Code | Evidence in `literals-sorted.txt` |
|---|---|
| `barn` | `setowned barn`, `setowned barn, setowned laundromat` |
| `laundromat` | `setowned barn, setowned laundromat` |
| `manor` | `setowned manor` |
| `rv` | standalone literal `rv` (weak — could be an unrelated string) |

Other property-ish literals that exist but are **variable names, not property codes**:
`Bungalow_Pots`, `Bungalow_Seed_Pots`, `Bungalow_Soil_Pots`, `Bungalow_Watered_Pots`,
`Bungalow_PackagingStations`, `Motel_Pots`, `Motel_Seed_Pots`, `Motel_Soil_Pots`,
`Motel_Watered_Pots`, `Motel_MixingStations`, `Motel_PackagingStations`, `Sweatshop_Pots`,
`Sweatshop_MixingStations`, `Sweatshop_PackagingStations`, `WarehouseUnlocked`.

Grep for `^barn$` etc. in `literals-sorted.txt` returns nothing for `barn`, `laundromat`, `manor`,
`motel`, `sweatshop`, `bungalow`, `docks`, `carwash`, `postoffice`, `taco`, `warehouse` — i.e. the
three known codes only exist inside the console-command help strings.

**Get the real list at runtime:**
```csharp
foreach (var p in Il2CppScheduleOne.Property.Property.Properties)
    MelonLogger.Msg($"{p.PropertyCode} | {p.PropertyName} | ${p.Price} | owned={p.IsOwned}");
foreach (var b in Il2CppScheduleOne.Property.Business.Businesses)
    MelonLogger.Msg($"{b.PropertyCode} | {b.PropertyName} | launder={b.LaunderCapacity}");
```

The console command itself:

```csharp
public class Il2CppScheduleOne.Console+SetPropertyOwned : Il2CppScheduleOne.Console+ConsoleCommand
{
    System.String CommandWord        { get; }   // "setowned"
    System.String CommandDescription { get; }
    System.String ExampleUsage       { get; }   // "setowned barn, setowned laundromat"
    public virtual System.Void Execute(Il2CppSystem.Collections.Generic.List<System.String> args)
}
public sealed class Console+SetPropertyOwned+__c__DisplayClass6_0
{
    System.String code { get; set; }
    public System.Boolean _Execute_b__0(Il2CppScheduleOne.Property.Property x)
    public System.Boolean _Execute_b__1(Il2CppScheduleOne.Property.Business x)
}
```

### 2.6 Remaining `Property`-namespace types

```csharp
public class Il2CppScheduleOne.Property.PropertyContentsContainer : UnityEngine.MonoBehaviour
    Il2CppScheduleOne.Property.Property Property { get; set; }
    public System.Void SetProperty(Il2CppScheduleOne.Property.Property property)

public class Il2CppScheduleOne.Property.PropertyDisposalArea : UnityEngine.MonoBehaviour
    UnityEngine.Transform StandPoint     { get; set; }
    UnityEngine.Transform TrashDropPoint { get; set; }

public class Il2CppScheduleOne.Property.Tap : Il2CppFishNet.Object.NetworkBehaviour
    // water tap; implements IUsable (NPCUserObject / PlayerUserObject / SetNPCUser / SetPlayerUser)
    static System.Single FlowRateMultiplier { get; set; }
    static System.Single HandleMoveSpeed    { get; set; }
    System.Boolean IsHeldOpen    { get; set; }
    System.Single  ActualFlowRate{ get; }
    public System.Void SetHeldOpen(System.Boolean open)
    public System.Void SetMaxTapOpen(System.Single max)
    public System.Single GetTapFlow()
    public virtual System.Void SetNPCUser(Il2CppFishNet.Object.NetworkObject npcObject)
    public virtual System.Void SetPlayerUser(Il2CppFishNet.Object.NetworkObject playerObject)
```

---

## 3. `ITransitEntity` — TOP PRIORITY

### 3.1 Exact identity

* **Full name:** `Il2CppScheduleOne.Management.ITransitEntity`
* **Original IL2CPP name:** `ScheduleOne.Management.ITransitEntity`
* **Source file (from metadata):** `\Assets\Scripts\Management\ITransitEntity.cs`
* **Metadata type-list entry:** `%ScheduleOne.Management|ITransitEntity`
* **Il2CppInterop shape:** `public class Il2CppScheduleOne.Management.ITransitEntity : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase`
* **NOT listed in `04-interface-implementors.txt`** — see the gotcha at the top of this doc.

It is a C# interface **with default implementations** (C# 8 DIM). Proof: the interface type itself
owns a compiler closure class
`Il2CppScheduleOne.Management.ITransitEntity+__c__DisplayClass27_0`
(`[ObfuscatedName("ScheduleOne.Management.ITransitEntity+<>c__DisplayClass27_0")]`) whose single
method is `_GetOutputItemContainer_b__0(Il2CppScheduleOne.ItemFramework.ItemSlot x)` — a lambda body
compiled **into the interface**. That means `GetOutputItemContainer` (and, by the same pattern,
the other `virtual` methods below) have bodies on the interface and implementors do not need to
supply them.

### 3.2 Complete verified member list

```csharp
public class Il2CppScheduleOne.Management.ITransitEntity   // = interface ScheduleOne.Management.ITransitEntity
{
    // ---- Properties (9) ----
    System.String Name { public get; }
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemSlot> InputSlots  { public get; public set; }
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemSlot> OutputSlots { public get; public set; }
    UnityEngine.Transform LinkOrigin { public get; }
    Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<UnityEngine.Transform> AccessPoints { public get; }
    System.Boolean Selectable        { public get; }
    System.Boolean IsAcceptingItems  { public get; }
    System.Boolean IsDestroyed       { public get; }
    Il2CppSystem.Guid GUID           { public get; }

    // ---- Methods (11) ----
    public virtual Il2CppScheduleOne.ItemFramework.ItemSlot GetFirstSlotContainingItem(System.String id, Il2CppScheduleOne.Management.ITransitEntity+ESlotType searchType)
    public virtual Il2CppScheduleOne.ItemFramework.ItemSlot GetFirstSlotContainingTemplateItem(Il2CppScheduleOne.ItemFramework.ItemInstance templateItem, Il2CppScheduleOne.Management.ITransitEntity+ESlotType searchType)
    public virtual System.Int32 GetInputCapacityForItem(Il2CppScheduleOne.ItemFramework.ItemInstance item, Il2CppScheduleOne.NPCs.NPC asker = null, System.Boolean checkPlayerFilters = True)
    public virtual System.Int32 GetOutputCapacityForItem(Il2CppScheduleOne.ItemFramework.ItemInstance item, Il2CppScheduleOne.NPCs.NPC asker = null)
    public virtual Il2CppScheduleOne.ItemFramework.ItemSlot GetOutputItemContainer(Il2CppScheduleOne.ItemFramework.ItemInstance item)
    public virtual System.Void HideOutline()
    public virtual System.Void InsertItemIntoInput(Il2CppScheduleOne.ItemFramework.ItemInstance item, Il2CppScheduleOne.NPCs.NPC inserter = null)
    public virtual System.Void InsertItemIntoOutput(Il2CppScheduleOne.ItemFramework.ItemInstance item, Il2CppScheduleOne.NPCs.NPC inserter = null)
    public virtual System.Void RemoveSlotLocks(Il2CppFishNet.Object.NetworkObject locker)
    public virtual Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemSlot> ReserveInputSlotsForItem(Il2CppScheduleOne.ItemFramework.ItemInstance item, Il2CppFishNet.Object.NetworkObject locker)
    public virtual System.Void ShowOutline(UnityEngine.Color color)
}

public enum Il2CppScheduleOne.Management.ITransitEntity+ESlotType : System.Int32
{
    Input  = 0,
    Output = 1,
    Both   = 2,
}
```

Declaration order in `global-metadata.dat` (from `strings-global-metadata-ordered.txt` lines
11565–11597) is: `get_InputSlots`, `set_InputSlots`, `get_OutputSlots`, `set_OutputSlots`,
`get_LinkOrigin`, `get_AccessPoints`, `get_Selectable`, `get_IsAcceptingItems`,
`InsertItemIntoInput`, `InsertItemIntoOutput`, `GetInputCapacityForItem`,
`GetOutputCapacityForItem`, `GetOutputItemContainer`, `ReserveInputSlotsForItem`,
`RemoveSlotLocks`, `GetFirstSlotContainingItem`, `GetFirstSlotContainingTemplateItem`, then the
nested `ESlotType` and the `<GetOutputItemContainer>b__0` closure. `Name`, `IsDestroyed`, `GUID`,
`ShowOutline`, `HideOutline` are absent from that block **only because of string de-duplication**;
they are genuinely part of the interface (proven by `Il2CppScheduleOne.Delivery.LoadingDock`, which
is a plain `MonoBehaviour` — not a `BuildableItem` — yet declares all five, plus every other
`ITransitEntity` member).

### 3.3 What each member is for

| Member | Purpose |
|---|---|
| `Name` | Display name used by the management UI and by transit-line labels. On `BuildableItem`-derived entities this generally forwards to `GetManagementName()` *(inferred)*. |
| `InputSlots` | Slots the entity **accepts deliveries into**. Get **and set** — you can swap the list wholesale from a mod. |
| `OutputSlots` | Slots a courier **takes from**. Also get+set. Storages set both to the same underlying `StorageEntity.ItemSlots` list *(inferred from `PlaceableStorageEntity` having both and delegating to `StorageEntity`)*. |
| `LinkOrigin` | `Transform` used as the endpoint of the drawn transit line (`TransitLineVisuals.SetSourcePosition` / `SetDestinationPosition`). |
| `AccessPoints` | Array of stand-points an NPC must reach to interact. `MoveItemBehaviour.GetSourceAccessPoint` / `GetDestinationAccessPoint` pick from this. **This is the navmesh contract for any courier.** |
| `Selectable` | Whether the entity can be picked in `TransitEntitySelector` / `ObjectSelector`. |
| `IsAcceptingItems` | Runtime gate — false while an entity is full, off, mid-operation, or destroyed. `MoveItemBehaviour.IsDestinationValid` consults it *(inferred)*. |
| `IsDestroyed` | Route validity check; `TransitRoute.ValidateEntities()` uses it *(inferred from the method name)*. |
| `GUID` | Stable identity used to (de)serialise a route: `AdvancedTransitRouteData.SourceGUID` / `.DestinationGUID` and `MoveItemData.SourceGUID` / `.DestinationGUID` are exactly these GUIDs. |
| `GetFirstSlotContainingItem(id, searchType)` | Find a slot holding item-ID `id`, restricted to Input/Output/Both. |
| `GetFirstSlotContainingTemplateItem(templateItem, searchType)` | Same but matched against a *template* `ItemInstance` (so quality/variant matters). This is what `MoveItemBehaviour` uses with its `itemToRetrieveTemplate`. |
| `GetInputCapacityForItem(item, asker, checkPlayerFilters)` | How many units of `item` can still be pushed in. `checkPlayerFilters = true` honours per-slot `SlotFilter`s the player set. `asker` lets an entity refuse specific NPCs. |
| `GetOutputCapacityForItem(item, asker)` | How many units are available to take out. |
| `GetOutputItemContainer(item)` | Returns the specific output `ItemSlot` holding `item` (this is the DIM whose lambda is `<GetOutputItemContainer>b__0`). |
| `InsertItemIntoInput(item, inserter)` | Push an item into the input side. **The single most useful call for a delivery mod.** |
| `InsertItemIntoOutput(item, inserter)` | Push into the output side (used when a station finishes producing). |
| `ReserveInputSlotsForItem(item, locker)` | Lock input slots so two couriers don't target the same space. Returns the slots it locked. |
| `RemoveSlotLocks(locker)` | Release everything `locker` reserved. **Call this on abort/cancel or you will permanently jam the entity.** |
| `ShowOutline(color)` / `HideOutline()` | Selection highlight, driven by `TransitEntitySelector.SetSelectionOutline`. |

### 3.4 Every implementor

Rebuilt by intersecting the declarers of `AccessPoints`, `LinkOrigin`, `Selectable`,
`IsAcceptingItems`, `InputSlots`, `OutputSlots`.

**Complete implementors (declare all six abstract members):**

| Type | Base | Kind |
|---|---|---|
| `Il2CppScheduleOne.Delivery.LoadingDock` | `UnityEngine.MonoBehaviour` | delivery van dock; also `IUsable`, `IGUIDRegisterable` |
| `Il2CppScheduleOne.Growing.GrowContainer` | `Il2CppScheduleOne.EntityFramework.GridItem` | base of `Pot` and `MushroomBed` |
| `Il2CppScheduleOne.ObjectScripts.BrickPress` | `GridItem` | station |
| `Il2CppScheduleOne.ObjectScripts.Cauldron` | `GridItem` | station |
| `Il2CppScheduleOne.ObjectScripts.ChemistryStation` | `GridItem` | station |
| `Il2CppScheduleOne.ObjectScripts.DryingRack` | `GridItem` | station |
| `Il2CppScheduleOne.ObjectScripts.LabOven` | `GridItem` | station |
| `Il2CppScheduleOne.ObjectScripts.MixingStation` | `GridItem` | station |
| `Il2CppScheduleOne.ObjectScripts.PackagingStation` | `GridItem` | station |
| `Il2CppScheduleOne.ObjectScripts.PlaceableStorageEntity` | `GridItem` | **storage** (wraps a `Storage.StorageEntity`) |
| `Il2CppScheduleOne.ObjectScripts.TrashContainerItem` | `GridItem` | trash bin |
| `Il2CppScheduleOne.StationFramework.MushroomSpawnStation` | `GridItem` | station |

**Inherited implementors (get everything from a base above):**

| Type | Base |
|---|---|
| `Il2CppScheduleOne.ObjectScripts.Pot` | `Il2CppScheduleOne.Growing.GrowContainer` |
| `Il2CppScheduleOne.ObjectScripts.MushroomBed` | `Il2CppScheduleOne.Growing.GrowContainer` |
| `Il2CppScheduleOne.ObjectScripts.MixingStationMk2` | `Il2CppScheduleOne.ObjectScripts.MixingStation` |
| `Il2CppScheduleOne.ObjectScripts.BedItem` | `Il2CppScheduleOne.ObjectScripts.PlaceableStorageEntity` |

**Partial / ambiguous — flagged:**

* `Il2CppScheduleOne.ObjectScripts.SurfaceStorageEntity` (`base: EntityFramework.SurfaceItem`)
  declares `Selectable { get; }` and `IsAcceptingItems { get; set; }` (with
  `_Selectable_k__BackingField` / `_IsAcceptingItems_k__BackingField`) plus the full `IUsable`
  surface — but it does **not** declare `InputSlots`, `OutputSlots`, `LinkOrigin` or `AccessPoints`,
  and no base in its chain (`SurfaceItem` → `BuildableItem` → `NetworkBehaviour`) supplies them.
  **UNVERIFIED** whether the game ever casts it to `ITransitEntity`. Treat it as *not* a valid
  route endpoint until proven otherwise at runtime (`obj.TryCast<ITransitEntity>() != null` and
  `.InputSlots != null`).

**Canonical worked example — `MushroomSpawnStation` declares the whole contract:**

```csharp
System.String Name { public get; }
Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemSlot> InputSlots  { public get; public set; }
Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemSlot> OutputSlots { public get; public set; }
UnityEngine.Transform LinkOrigin { public get; }
Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<UnityEngine.Transform> AccessPoints { public get; }
System.Boolean Selectable       { public get; }
System.Boolean IsAcceptingItems { public get; public set; }
// backing serialized fields:
UnityEngine.Transform _uiPoint { public get; public set; }
Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<UnityEngine.Transform> _accessPoints { public get; public set; }
```

---

## 4. `Il2CppScheduleOne.Management` (38 types)

### 4.1 `TransitRoute` — full dump

```csharp
public class Il2CppScheduleOne.Management.TransitRoute : Il2CppSystem.Object
{
    Il2CppScheduleOne.Management.ITransitEntity _Source_k__BackingField      { public get; public set; }
    Il2CppScheduleOne.Management.ITransitEntity _Destination_k__BackingField { public get; public set; }
    Il2CppScheduleOne.Management.TransitLineVisuals visuals                  { public get; public set; }
    Il2CppSystem.Action<Il2CppScheduleOne.Management.ITransitEntity> onSourceChange      { public get; public set; }
    Il2CppSystem.Action<Il2CppScheduleOne.Management.ITransitEntity> onDestinationChange { public get; public set; }
    Il2CppScheduleOne.Management.ITransitEntity Source      { public get; public set; }
    Il2CppScheduleOne.Management.ITransitEntity Destination { public get; public set; }

    private .ctor()
    public  .ctor(Il2CppScheduleOne.Management.ITransitEntity source, Il2CppScheduleOne.Management.ITransitEntity destination)
    public  .ctor(System.IntPtr pointer)

    public System.Boolean AreEntitiesNonNull()
    public System.Void Destroy()
    public virtual System.Void SetDestination(Il2CppScheduleOne.Management.ITransitEntity destination)
    public virtual System.Void SetSource(Il2CppScheduleOne.Management.ITransitEntity source)
    public System.Void SetVisualsActive(System.Boolean active)
    public System.Void Update()
    public System.Void ValidateEntities()
}
```

**Key facts.** `TransitRoute` is a plain `Il2CppSystem.Object` — **not** a `MonoBehaviour`, **not** a
`NetworkBehaviour`, **not** an `ISaveable`. It has a public two-arg constructor taking two
`ITransitEntity`. So: **yes, a route can be constructed purely from code at runtime, on either
side of the network, with no scene object and no UI.** It is a dumb pair-of-endpoints + a
line-renderer handle. It does **not** move items by itself — `MoveItemBehaviour` does (see §10).

`Update()` is a manual call (there's no MonoBehaviour to drive it); it presumably refreshes the
visuals from `LinkOrigin` *(inferred)*.

### 4.2 `AdvancedTransitRoute` — full dump

```csharp
public class Il2CppScheduleOne.Management.AdvancedTransitRoute
    : Il2CppScheduleOne.Management.TransitRoute
{
    Il2CppScheduleOne.Management.ManagementItemFilter _Filter_k__BackingField { public get; public set; }
    Il2CppScheduleOne.Management.ManagementItemFilter Filter                  { public get; public set; }

    private .ctor()
    public  .ctor(Il2CppScheduleOne.Management.ITransitEntity source, Il2CppScheduleOne.Management.ITransitEntity destination)
    public  .ctor(Il2CppScheduleOne.Persistence.Datas.AdvancedTransitRouteData data)
    public  .ctor(System.IntPtr pointer)

    public Il2CppScheduleOne.Persistence.Datas.AdvancedTransitRouteData GetData()
    public Il2CppScheduleOne.ItemFramework.ItemInstance GetItemReadyToMove()
}
```

`AdvancedTransitRoute` = `TransitRoute` + an item filter + round-trip serialisation. The
`.ctor(AdvancedTransitRouteData data)` overload resolves `SourceGUID`/`DestinationGUID` back into
live entities *(inferred from the DTO shape)*. `GetItemReadyToMove()` is the "what should the
courier grab next" query — it returns the first `ItemInstance` on the source's output side that
passes `Filter`. **This is the method a hireable-driver mod should call every tick.**

### 4.3 `ManagementItemFilter`

```csharp
public class Il2CppScheduleOne.Management.ManagementItemFilter : Il2CppSystem.Object
{
    Il2CppScheduleOne.Management.ManagementItemFilter+EMode Mode { public get; public set; }
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemDefinition> Items { public get; public set; }

    public .ctor(Il2CppScheduleOne.Management.ManagementItemFilter+EMode mode)

    public System.Void AddItem(Il2CppScheduleOne.ItemFramework.ItemDefinition item)
    public System.Boolean Contains(Il2CppScheduleOne.ItemFramework.ItemDefinition item)
    public System.Boolean DoesItemMeetFilter(Il2CppScheduleOne.ItemFramework.ItemInstance item)
    public System.String GetDescription()
    public System.Void RemoveItem(Il2CppScheduleOne.ItemFramework.ItemDefinition item)
    public System.Void SetMode(Il2CppScheduleOne.Management.ManagementItemFilter+EMode mode)
}
public enum ManagementItemFilter+EMode : System.Int32 { Whitelist = 0, Blacklist = 1 }
```

### 4.4 `TransitLineVisuals` / `TransitRouteMaterial`

```csharp
public class Il2CppScheduleOne.Management.TransitLineVisuals : UnityEngine.MonoBehaviour
    UnityEngine.LineRenderer Renderer { public get; public set; }
    public System.Void SetDestinationPosition(UnityEngine.Vector3 position)
    public System.Void SetSourcePosition(UnityEngine.Vector3 position)

public class Il2CppScheduleOne.Management.TransitRouteMaterial : UnityEngine.MonoBehaviour
    public System.Void Awake()
```

### 4.5 Configuration framework

```csharp
public class Il2CppScheduleOne.Management.EntityConfiguration : Il2CppSystem.Object
{
    static System.Int32 NameCharacterLimit { public get; public set; }
    Il2CppScheduleOne.Management.ConfigurationReplicator Replicator { public get; public set; }
    Il2CppScheduleOne.Management.IConfigurable Configurable         { public get; public set; }
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Management.ConfigField> Fields { public get; public set; }
    UnityEngine.Events.UnityEvent onChanged { public get; public set; }
    System.Boolean IsSelected { public get; public set; }
    Il2CppScheduleOne.Management.StringField Name { public get; public set; }

    public .ctor(Il2CppScheduleOne.Management.ConfigurationReplicator replicator, Il2CppScheduleOne.Management.IConfigurable configurable, System.String defaultName)

    public virtual System.Boolean AllowRename()
    public virtual System.Void Deselected()
    public virtual System.Void Destroy()
    public T GetField<T>()
    public virtual System.String GetSaveString()
    public System.Void InvokeChanged()
    public System.Void ReplicateAllFields(Il2CppFishNet.Connection.NetworkConnection conn = null, System.Boolean replicateDefaults = True)
    public System.Void ReplicateField(Il2CppScheduleOne.Management.ConfigField field, Il2CppFishNet.Connection.NetworkConnection conn = null)
    public virtual System.Void Reset()
    public virtual System.Void Selected()
    public virtual System.Boolean ShouldSave()
    public System.Void __ctor_b__20_0(System.String <p0>)
}

public class Il2CppScheduleOne.Management.ConfigField : Il2CppSystem.Object
{
    Il2CppScheduleOne.Management.EntityConfiguration ParentConfig { public get; public set; }
    public .ctor(Il2CppScheduleOne.Management.EntityConfiguration parentConfig)
    public virtual System.Boolean IsValueDefault()
}
```

`ConfigField` subclasses — each has `GetData()` / `Load(data)` / `IsValueDefault()` and a
`Set…(value, bool network)` writer. `network: true` pushes through `ConfigurationReplicator`.

```csharp
public class ItemField : ConfigField
    Il2CppScheduleOne.ItemFramework.ItemDefinition SelectedItem { public get; public set; }
    System.Boolean CanSelectNone { public get; public set; }
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemDefinition> Options { public get; public set; }
    UnityEngine.Events.UnityEvent<Il2CppScheduleOne.ItemFramework.ItemDefinition> onItemChanged { public get; public set; }
    public Il2CppScheduleOne.Persistence.Datas.ItemFieldData GetData()
    public System.Void Load(Il2CppScheduleOne.Persistence.Datas.ItemFieldData data)
    public System.Void SetItem(Il2CppScheduleOne.ItemFramework.ItemDefinition item, System.Boolean network)

public class NPCField : ConfigField
    Il2CppScheduleOne.NPCs.NPC SelectedNPC { public get; public set; }
    Il2CppSystem.Type TypeRequirement { public get; public set; }
    UnityEngine.Events.UnityEvent<Il2CppScheduleOne.NPCs.NPC> onNPCChanged { public get; public set; }
    public System.Boolean DoesNPCMatchRequirement(Il2CppScheduleOne.NPCs.NPC npc)
    public Il2CppScheduleOne.Persistence.Datas.NPCFieldData GetData()
    public System.Void Load(Il2CppScheduleOne.Persistence.Datas.NPCFieldData data)
    public System.Void SetNPC(Il2CppScheduleOne.NPCs.NPC npc, System.Boolean network)

public class NumberField : ConfigField
    System.Single Value { public get; public set; }
    System.Single MinValue { public get; public set; }
    System.Single MaxValue { public get; public set; }
    System.Boolean WholeNumbers { public get; public set; }
    UnityEngine.Events.UnityEvent<System.Single> onItemChanged { public get; public set; }
    public System.Void Configure(System.Single minValue, System.Single maxValue, System.Boolean wholeNumbers)
    public Il2CppScheduleOne.Persistence.Datas.NumberFieldData GetData()
    public System.Void Load(Il2CppScheduleOne.Persistence.Datas.NumberFieldData data)
    public System.Void SetValue(System.Single value, System.Boolean network)

public class ObjectField : ConfigField            // <-- destination picker on every station config
    Il2CppScheduleOne.EntityFramework.BuildableItem SelectedObject { public get; public set; }
    UnityEngine.Events.UnityEvent<Il2CppScheduleOne.EntityFramework.BuildableItem> onObjectChanged { public get; public set; }
    Il2CppScheduleOne.UI.Management.ObjectSelector+ObjectFilter objectFilter { public get; public set; }
    Il2CppSystem.Collections.Generic.List<Il2CppSystem.Type> TypeRequirements { public get; public set; }
    System.Boolean DrawTransitLine { public get; public set; }
    public Il2CppScheduleOne.Persistence.Datas.ObjectFieldData GetData()
    public System.Void Load(Il2CppScheduleOne.Persistence.Datas.ObjectFieldData data)
    public System.Void SelectedObjectDestroyed()
    public System.Void SetObject(Il2CppScheduleOne.EntityFramework.BuildableItem obj, System.Boolean network)

public class ObjectListField : ConfigField
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.EntityFramework.BuildableItem> SelectedObjects { public get; public set; }
    System.Int32 MaxItems { public get; public set; }
    Il2CppScheduleOne.UI.Management.ObjectSelector+ObjectFilter objectFilter { public get; public set; }
    Il2CppSystem.Collections.Generic.List<Il2CppSystem.Type> TypeRequirements { public get; public set; }
    UnityEngine.Events.UnityEvent<Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.EntityFramework.BuildableItem>> onListChanged { public get; public set; }
    public System.Void AddItem(Il2CppScheduleOne.EntityFramework.BuildableItem item)
    public Il2CppScheduleOne.Persistence.Datas.ObjectListFieldData GetData()
    public System.Void Load(Il2CppScheduleOne.Persistence.Datas.ObjectListFieldData data)
    public System.Void RemoveItem(Il2CppScheduleOne.EntityFramework.BuildableItem item)
    public System.Void SelectedObjectDestroyed(Il2CppScheduleOne.EntityFramework.BuildableItem item)
    public System.Void SetList(Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.EntityFramework.BuildableItem> list, System.Boolean network)

public class QualityField : ConfigField
    Il2CppScheduleOne.ItemFramework.EQuality Value { public get; public set; }
    UnityEngine.Events.UnityEvent<Il2CppScheduleOne.ItemFramework.EQuality> onValueChanged { public get; public set; }
    public Il2CppScheduleOne.Persistence.Datas.QualityFieldData GetData()
    public System.Void Load(Il2CppScheduleOne.Persistence.Datas.QualityFieldData data)
    public System.Void SetValue(Il2CppScheduleOne.ItemFramework.EQuality value, System.Boolean network)

public class RouteListField : ConfigField          // <-- the Packager's multi-route list
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Management.AdvancedTransitRoute> Routes { public get; public set; }
    System.Int32 MaxRoutes { public get; public set; }
    UnityEngine.Events.UnityEvent<Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Management.AdvancedTransitRoute>> onListChanged { public get; public set; }
    public System.Void AddItem(Il2CppScheduleOne.Management.AdvancedTransitRoute item)
    public Il2CppScheduleOne.Persistence.Datas.RouteListData GetData()
    public System.Void Load(Il2CppScheduleOne.Persistence.Datas.RouteListData data)
    public System.Void RemoveItem(Il2CppScheduleOne.Management.AdvancedTransitRoute item)
    public System.Void Replicate()
    public System.Void SetList(Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Management.AdvancedTransitRoute> list, System.Boolean network, System.Boolean bypassSequenceCheck = False)

public class StationRecipeField : ConfigField
    Il2CppScheduleOne.StationFramework.StationRecipe SelectedRecipe { public get; public set; }
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.StationFramework.StationRecipe> Options { public get; public set; }
    UnityEngine.Events.UnityEvent<Il2CppScheduleOne.StationFramework.StationRecipe> onRecipeChanged { public get; public set; }
    public Il2CppScheduleOne.Persistence.Datas.StationRecipeFieldData GetData()
    public System.Void Load(Il2CppScheduleOne.Persistence.Datas.StationRecipeFieldData data)
    public System.Void SetRecipe(Il2CppScheduleOne.StationFramework.StationRecipe recipe, System.Boolean network)

public class StringField : ConfigField
    System.String Value { public get; public set; }
    System.Int32  CharacterLimit { public get; public set; }
    UnityEngine.Events.UnityEvent<System.String> onItemChanged { public get; public set; }
    public .ctor(Il2CppScheduleOne.Management.EntityConfiguration parentConfig, System.String defaultValue)
    public System.Void Configure(System.Int32 characterLimit, System.Boolean canBeNullOrEmpty)
    public Il2CppScheduleOne.Persistence.Datas.StringFieldData GetData()
    public System.Void Load(Il2CppScheduleOne.Persistence.Datas.StringFieldData data)
    public System.Void SetValue(System.String value, System.Boolean network)
```

### 4.6 The 13 `*Configuration` types

Every one is `base: Il2CppScheduleOne.Management.EntityConfiguration` and every station-side one
exposes `ObjectField Destination` + `TransitRoute DestinationRoute` — **that pair is how the vanilla
game turns "pick a destination in the UI" into a live `TransitRoute`.**

| Configuration | Third ctor arg | Distinctive fields |
|---|---|---|
| `BotanistConfiguration` | `Il2CppScheduleOne.Employees.Botanist _botanist` | `ObjectField Home`, `ObjectListField Assigns`, `AssignedPots/Racks/Beds/SpawnStations`, `AssignedHome`, static `AssignableTypes` |
| `BrickPressConfiguration` | `Il2CppScheduleOne.ObjectScripts.BrickPress station` | `NPCField AssignedPackager`, `ObjectField Destination`, `TransitRoute DestinationRoute`, `BrickPress BrickPress` |
| `CauldronConfiguration` | `Il2CppScheduleOne.ObjectScripts.Cauldron cauldron` | `NPCField AssignedChemist`, `Destination`, `DestinationRoute`, `Cauldron Station` |
| `ChemistConfiguration` | `Il2CppScheduleOne.Employees.Chemist _chemist` | `ObjectField Home`, `ObjectListField Stations`, `ChemStations`, `LabOvens`, `Cauldrons`, `MixStations`, `Int32 TotalStations` |
| `ChemistryStationConfiguration` | `ChemistryStation station` | `NPCField AssignedChemist`, `StationRecipeField Recipe`, `Destination`, `DestinationRoute` |
| `CleanerConfiguration` | `Il2CppScheduleOne.Employees.Cleaner _cleaner` | `ObjectField Home`, `ObjectListField Bins`, `List<TrashContainerItem> binItems` |
| `DryingRackConfiguration` | `DryingRack rack` | `NPCField AssignedBotanist`, `QualityField TargetQuality`, `NumberField StartThreshold`, `Destination`, `DestinationRoute` |
| `LabOvenConfiguration` | `LabOven oven` | `NPCField AssignedChemist`, `Destination`, `DestinationRoute` |
| `MixingStationConfiguration` | `MixingStation station` | `NPCField AssignedChemist`, `NumberField StartThrehold` *(sic — typo in the game)*, `Destination`, `DestinationRoute` |
| `MushroomBedConfiguration` | `MushroomBed mushroomBed` | `ItemField Spawn`, `ItemField Additive1/2/3`, `NPCField AssignedBotanist`, `Destination`, `DestinationRoute`, `GetSelectedSeedIDs()`, `IsAdditiveSelected(ItemDefinition)` |
| `PackagerConfiguration` | `Il2CppScheduleOne.Employees.Packager _packager` | **`RouteListField Routes`**, `ObjectField Home`, `ObjectListField Stations`, `AssignedStations`, `AssignedBrickPresses`, `Int32 AssignedStationCount` |

| `PackagingStationConfiguration` | `PackagingStation station` | `NPCField AssignedPackager`, `Destination`, `DestinationRoute` |
| `PotConfiguration` | `Pot pot` | `ItemField Seed`, `ItemField Additive1/2/3`, `NPCField AssignedBotanist`, `Destination`, `DestinationRoute`, `GetSelectedSeedIDs()`, `IsAdditiveSelected(…)` |
| `SpawnStationConfiguration` | `MushroomSpawnStation station` | `NPCField AssignedBotanist`, `Destination`, `DestinationRoute` |

> **`Packager` is the internal name of the in-game "Handler" employee** *(inferred: the four
> `Employee` subclasses are `Botanist`, `Chemist`, `Cleaner`, `Packager`, and only `Packager` owns
> a multi-route `RouteListField`, matching the Handler's advertised 5-route behaviour)*.
> `PackagerConfiguration.Routes` with its `MaxRoutes` cap **is** the vanilla multi-route system a
> Hireable-Drivers mod should mirror.

Shared method shape on the station-side configs:

```csharp
public virtual System.Void Deselected()
public System.Void DestinationChanged(Il2CppScheduleOne.EntityFramework.BuildableItem item)
public System.Boolean DestinationFilter(Il2CppScheduleOne.EntityFramework.BuildableItem obj, out System.String& reason)
public virtual System.String GetSaveString()
public virtual System.Void Reset()
public virtual System.Void Selected()
public virtual System.Boolean ShouldSave()
```

`DestinationChanged(item)` is the hook: the UI sets `ObjectField Destination`, `onObjectChanged`
fires `DestinationChanged`, which (re)builds `DestinationRoute` *(inferred from the naming and from
`TransitRoute.SetDestination`)*.

### 4.7 `ConfigurationReplicator` — the network layer for configs

`base: Il2CppFishNet.Object.NetworkBehaviour`, 77 methods. Structure is 11 field kinds ×
(`Send…` ServerRpc / `Receive…` ObserversRpc) × (public, `RpcWriter___`, `RpcReader___`,
`RpcLogic___`).

```csharp
Il2CppScheduleOne.Management.EntityConfiguration Configuration { public get; public set; }
public System.Void ReplicateField(Il2CppScheduleOne.Management.ConfigField field, Il2CppFishNet.Connection.NetworkConnection conn = null)

// Client -> Server (ServerRpc entry points)
public System.Void SendItemField      (System.Int32 fieldIndex, System.String value)
public System.Void SendNPCField       (System.Int32 fieldIndex, Il2CppFishNet.Object.NetworkObject npcObject)
public System.Void SendNumberField    (System.Int32 fieldIndex, System.Single value)
public System.Void SendObjectField    (System.Int32 fieldIndex, Il2CppFishNet.Object.NetworkObject obj)
public System.Void SendObjectListField(System.Int32 fieldIndex, Il2CppSystem.Collections.Generic.List<Il2CppFishNet.Object.NetworkObject> objects)
public System.Void SendQualityField   (System.Int32 fieldIndex, Il2CppScheduleOne.ItemFramework.EQuality quality)
public System.Void SendRecipeField    (System.Int32 fieldIndex, System.Int32 recipeIndex)
public System.Void SendRouteListField (System.Int32 fieldIndex, Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppScheduleOne.Persistence.Datas.AdvancedTransitRouteData> value)
public System.Void SendStringField    (System.Int32 fieldIndex, System.String value)

// Server -> Clients (ObserversRpc entry points), same 9 + identical signatures
public System.Void ReceiveItemField(System.Int32 fieldIndex, System.String value)
public System.Void ReceiveRouteListField(System.Int32 fieldIndex, Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppScheduleOne.Persistence.Datas.AdvancedTransitRouteData> value)
// … etc

// real bodies:
public System.Void RpcLogic___SendRouteListField_3226448297(System.Int32 fieldIndex, Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppScheduleOne.Persistence.Datas.AdvancedTransitRouteData> value)
public System.Void RpcLogic___ReceiveRouteListField_3226448297(System.Int32 fieldIndex, Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppScheduleOne.Persistence.Datas.AdvancedTransitRouteData> value)
// hashes for the other kinds:
//   ItemField/StringField 2801973956 | NPCField/ObjectField 1687693739 | NumberField 1293284375
//   ObjectListField 690244341 | QualityField 3536682170 | RecipeField 1692629761
```

Note `ConfigurationReplicator+__c` caches
`Func<AdvancedTransitRoute, AdvancedTransitRouteData> _ReplicateField_b__1_0` and
`Func<AdvancedTransitRouteData, AdvancedTransitRoute> _ReceiveRouteListField_b__15_1` — routes
travel over the wire **as `AdvancedTransitRouteData` (GUID pairs + filter)**, never as objects.

### 4.8 `IConfigurable`, `IUsable`, `ManagementInterface`, `ManagementUtilities`, `EConfigurableType`

```csharp
public class Il2CppScheduleOne.Management.IConfigurable : Il2CppObjectBase   // interface
{
    Il2CppScheduleOne.Management.EntityConfiguration Configuration { public get; }
    Il2CppScheduleOne.Management.ConfigurationReplicator ConfigReplicator { public get; }
    Il2CppScheduleOne.Management.EConfigurableType ConfigurableType { public get; }
    Il2CppScheduleOne.UI.Management.WorldspaceUIElement WorldspaceUI { public get; public set; }
    Il2CppFishNet.Object.NetworkObject CurrentPlayerConfigurer { public get; public set; }
    System.Boolean IsBeingConfiguredByOtherPlayer { public get; }
    UnityEngine.Sprite TypeIcon { public get; }
    UnityEngine.Transform Transform { public get; }
    UnityEngine.Transform UIPoint { public get; }
    System.Boolean IsDestroyed { public get; }
    System.Boolean CanBeSelected { public get; }
    Il2CppScheduleOne.Property.Property ParentProperty { public get; }

    public virtual Il2CppScheduleOne.UI.Management.WorldspaceUIElement CreateWorldspaceUI()
    public virtual System.Void Deselected()
    public virtual System.Void DestroyWorldspaceUI()
    public virtual System.Void HideOutline()
    public virtual System.Void Selected()
    public virtual System.Void SendConfigurationToClient(Il2CppFishNet.Connection.NetworkConnection conn)
    public virtual System.Void SetConfigurer(Il2CppFishNet.Object.NetworkObject player)
    public virtual System.Void ShowOutline(UnityEngine.Color color)
}

public class Il2CppScheduleOne.Management.IUsable : Il2CppObjectBase          // interface
{
    System.Boolean IsInUse { public get; }
    System.Boolean IsUsedByLocalPlayer { public get; }
    Il2CppFishNet.Object.NetworkObject NPCUserObject { public get; public set; }
    Il2CppFishNet.Object.NetworkObject PlayerUserObject { public get; public set; }
    System.String UserName { public get; }
    public virtual System.Boolean IsInUseByNPC(Il2CppScheduleOne.NPCs.NPC npc)
    public virtual System.Void SetNPCUser(Il2CppFishNet.Object.NetworkObject playerObject)
    public virtual System.Void SetPlayerUser(Il2CppFishNet.Object.NetworkObject playerObject)
}

public enum Il2CppScheduleOne.Management.EConfigurableType : System.Int32
{
    Pot = 0, PackagingStation = 1, LabOven = 2, Botanist = 3, Packager = 4,
    ChemistryStation = 5, Chemist = 6, Cauldron = 7, Cleaner = 8, BrickPress = 9,
    MixingStation = 10, DryingRack = 11, SpawnStation = 12, MushroomBed = 13, Storage = 14,
}

public static class Il2CppScheduleOne.Management.ConfigurableType : Il2CppSystem.Object
    public static System.String GetTypeName(Il2CppScheduleOne.Management.EConfigurableType type)

public class Il2CppScheduleOne.Management.ManagementUtilities
    : Il2CppScheduleOne.DevUtilities.Singleton<Il2CppScheduleOne.Management.ManagementUtilities>
{
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Growing.SeedDefinition> Seeds { public get; public set; }
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ShroomSpawnDefinition> MushroomSpawns { public get; public set; }
    UnityEngine.Sprite StorageTypeIcon { public get; public set; }
    Il2CppScheduleOne.UI.Management.StorageUIElement StorageUIElementPrefab { public get; public set; }
    public virtual System.Void Awake()
}
```

`ManagementInterface` — see §8.

---

## 5. Storage & the item framework

### 5.1 `Il2CppScheduleOne.Storage` (17 types)

`CoordinateStorageFootprintTilePair`, `CoordinateStorageTilePair`, `LiquidMeth_Stored`,
`StorableItemInstance`, `StorageDoorAnimation`, `StorageEntity`, `StorageEntityInteractable`,
`StorageEntityVisualizer`, `StorageGrid`, `StorageManager`, `StorageTile`,
`StorageVisualizationUtility`, `StorageVisualizer`, `StoredItem`, `StoredItemRandomRotation`,
`StoredItem_GenericBox`, `WorldStorageEntity`.

#### `StorageEntity` — full dump

`base: Il2CppFishNet.Object.NetworkBehaviour`. It implements `IItemSlotOwner`
(`ItemSlots` + `SetItemSlotQuantity` / `SetSlotFilter` / `SetSlotLocked` / `SetStoredInstance`).
**It is NOT an `ITransitEntity`** — `PlaceableStorageEntity` / `SurfaceStorageEntity` wrap it and
provide the transit surface.

```csharp
static System.Int32 MAX_SLOTS { public get; public set; }

Il2CppScheduleOne.PlayerScripts.Player _CurrentPlayerAccessor_k__BackingField { public get; public set; }
System.String  StorageEntityName     { public get; public set; }
System.String  StorageEntitySubtitle { public get; public set; }
System.Int32   SlotCount             { public get; public set; }
System.Boolean EmptyOnSleep          { public get; public set; }
System.Boolean SlotsAreFilterable    { public get; public set; }
System.Int32   DisplayRowCount       { public get; public set; }
Il2CppScheduleOne.Storage.StorageEntity+EAccessSettings AccessSettings { public get; public set; }
System.Single  MaxAccessDistance     { public get; public set; }
Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemSlot> _ItemSlots_k__BackingField { public get; public set; }
Il2CppSystem.Action onOpened          { public get; public set; }
Il2CppSystem.Action onClosed          { public get; public set; }
Il2CppSystem.Action onContentsChanged { public get; public set; }
System.Boolean field_Private_Boolean_0 { public get; public set; }
System.Boolean field_Private_Boolean_1 { public get; public set; }
System.Boolean IsOpened   { public get; }
Il2CppScheduleOne.PlayerScripts.Player CurrentPlayerAccessor { public get; public set; }
System.Int32   ItemCount  { public get; }
Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemSlot> ItemSlots { public get; public set; }

public enum StorageEntity+EAccessSettings : System.Int32 { Closed = 0, SinglePlayerOnly = 1, Full = 2 }
```

Methods that matter (full list is 70; RPC scaffolding elided where noted):

```csharp
public virtual System.Void Awake()
public virtual System.Boolean CanBeOpened()
public System.Boolean CanItemFit(Il2CppScheduleOne.ItemFramework.ItemInstance item, System.Int32 quantity = 1)
public System.Void ClearContents()
public virtual System.Void ContentsChanged()
public Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemInstance> GetAllItems()
public Il2CppSystem.Collections.Generic.Dictionary<Il2CppScheduleOne.Storage.StorableItemInstance, System.Int32> GetContentsDictionary()
public System.Void GetNetworth(Il2CppScheduleOne.Money.MoneyManager+FloatContainer container)
public System.Int32 HowManyCanFit(Il2CppScheduleOne.ItemFramework.ItemInstance item)
public System.Void InsertItem(Il2CppScheduleOne.ItemFramework.ItemInstance item, System.Boolean network = True)
public System.Void LoadFromItemSet(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppScheduleOne.ItemFramework.ItemInstance> items)
public System.Void Open()
public virtual System.Void OnOpened()
public virtual System.Void OnClosed()
public virtual System.Void OnDestroy()
public virtual System.Void OnSpawnServer(Il2CppFishNet.Connection.NetworkConnection connection)
public virtual System.Void Start()
public Il2CppSystem.Collections.IEnumerator UpdateWhileOpen()
public System.Void SendAccessor(Il2CppFishNet.Object.NetworkObject accessor)                              // ServerRpc
public System.Void SetAccessor(Il2CppFishNet.Object.NetworkObject accessor)                               // ObserversRpc
public virtual System.Void SetItemSlotQuantity(System.Int32 itemSlotIndex, System.Int32 quantity)         // ServerRpc
public System.Void SetItemSlotQuantity_Internal(System.Int32 itemSlotIndex, System.Int32 quantity)        // ObserversRpc
public virtual System.Void SetSlotFilter(Il2CppFishNet.Connection.NetworkConnection conn, System.Int32 itemSlotIndex, Il2CppScheduleOne.ItemFramework.SlotFilter filter)          // ServerRpc
public System.Void SetSlotFilter_Internal(Il2CppFishNet.Connection.NetworkConnection conn, System.Int32 itemSlotIndex, Il2CppScheduleOne.ItemFramework.SlotFilter filter)         // Observers + Target
public virtual System.Void SetSlotLocked(Il2CppFishNet.Connection.NetworkConnection conn, System.Int32 itemSlotIndex, System.Boolean locked, Il2CppFishNet.Object.NetworkObject lockOwner, System.String lockReason)   // ServerRpc
public System.Void SetSlotLocked_Internal(Il2CppFishNet.Connection.NetworkConnection conn, System.Int32 itemSlotIndex, System.Boolean locked, Il2CppFishNet.Object.NetworkObject lockOwner, System.String lockReason)  // Observers + Target
public virtual System.Void SetStoredInstance(Il2CppFishNet.Connection.NetworkConnection conn, System.Int32 itemSlotIndex, Il2CppScheduleOne.ItemFramework.ItemInstance instance)  // ServerRpc
public System.Void SetStoredInstance_Internal(Il2CppFishNet.Connection.NetworkConnection conn, System.Int32 itemSlotIndex, Il2CppScheduleOne.ItemFramework.ItemInstance instance) // Observers + Target
public System.Void Method_Private_Void_NetworkConnection_PDM_0(Il2CppFishNet.Connection.NetworkConnection conn)   // unstable name
public virtual System.Void Method_Protected_Virtual_New_Void_0()                                                  // unstable name
public System.Void _Open_b__45_0()
// RPC hashes: SendAccessor/SetAccessor 3323014238 | SetItemSlotQuantity(_Internal) 1692629761
//             SetSlotFilter(_Internal) 527532783 | SetSlotLocked(_Internal) 3170825843
//             SetStoredInstance(_Internal) 2652194801
```

**Server-authority model (inferred from the FishNet writer prefixes, high confidence).**
The `X` / `X_Internal` pairing is consistent across every slot mutator:
`X` has only `RpcWriter___Server_…` → it is a `[ServerRpc(RequireOwnership = false)]`;
`X_Internal` has `RpcWriter___Observers_…` **and** `RpcWriter___Target_…` → it is the
server→client broadcast. So a client mod calls `SetStoredInstance(...)`, the host executes
`RpcLogic___SetStoredInstance_2652194801`, and the host then calls `SetStoredInstance_Internal`
which fans out. Calling `*_Internal` from a client will not replicate.

#### `WorldStorageEntity`

`base: Il2CppScheduleOne.Storage.StorageEntity`, plus `IGUIDRegisterable` + `ISaveable`.
**This is the one with a global registry — use it to enumerate every world storage.**

```csharp
static Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Storage.WorldStorageEntity> All { public get; public set; }
Il2CppSystem.Guid GUID     { public get; public set; }
System.String BakedGUID    { public get; public set; }
Il2CppScheduleOne.GameTime.GameDateTime LastContentChangeTime { public get; public set; }
System.String SaveFolderName { public get; }
System.String SaveFileName   { public get; }
Il2CppScheduleOne.Persistence.Loaders.Loader Loader { public get; }
System.Boolean ShouldSaveUnderFolder { public get; }

public virtual System.Void ContentsChanged()
public Il2CppScheduleOne.Persistence.Datas.WorldStorageEntityData GetSaveData()
public virtual System.String GetSaveString()
public virtual System.Void InitializeSaveable()
public virtual System.Void Load(Il2CppScheduleOne.Persistence.Datas.WorldStorageEntityData data)
public System.Void RegenerateGUID()
public virtual System.Void SetGUID(Il2CppSystem.Guid guid)
public virtual System.Boolean ShouldSave()
```

#### The rest of `Il2CppScheduleOne.Storage`

```csharp
public class StorageGrid : UnityEngine.MonoBehaviour        // physical shelf grid for visualised storage
    static System.Single gridSize { public get; public set; }
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Storage.StorageTile> storageTiles { public get; public set; }
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Storage.CoordinateStorageTilePair> coordinateStorageTilePairs { public get; public set; }
    System.Int32 UnoccupiedTileCount { public get; }
    public System.Int32 CalculateUnoccupiedTileCount()
    public System.Void DeregisterTile(Il2CppScheduleOne.Storage.StorageTile tile)
    public System.Int32 GetActualX()
    public System.Int32 GetActualY()
    public Il2CppScheduleOne.Tiles.Coordinate GetMatchedCoordinate(Il2CppScheduleOne.Tiles.FootprintTile tileToMatch)
    public Il2CppScheduleOne.Storage.StorageTile GetTile(Il2CppScheduleOne.Tiles.Coordinate coord)
    public System.Int32 GetTotalFootprintSize()
    public System.Int32 GetUserEndCapacity()
    public System.Void RegisterTile(Il2CppScheduleOne.Storage.StorageTile tile)
    public System.Void TileOccupantChanged()
    public System.Boolean TryFitItem(System.Int32 sizeX, System.Int32 sizeY, Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Tiles.Coordinate> lockedCoordinates, out Il2CppScheduleOne.Tiles.Coordinate& originCoordinate, out System.Single& rotation)

public class StorageManager : Il2CppScheduleOne.DevUtilities.NetworkSingleton<StorageManager>   // ISaveable, owns StorageLoader
    Il2CppScheduleOne.Persistence.Loaders.StorageLoader loader { public get; public set; }
    System.String SaveFolderName { public get; }   // "WorldStorageEntities" (literal verified)
    System.Int32 LoadOrder { public get; }
    public virtual System.String GetSaveString()
    public virtual System.Void InitializeSaveable()

public class StorageVisualizer : UnityEngine.MonoBehaviour      // renders slot contents onto StorageGrids
    Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppScheduleOne.Storage.StorageGrid> StorageGrids { public get; public set; }
    UnityEngine.Transform ItemContainer { public get; public set; }
    System.Boolean FullRefreshOnItemRemoved { public get; public set; }
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemSlot> itemSlots { public get; public set; }
    System.Int32 totalFootprintCapacity { public get; public set; }
    System.Boolean BlockRefreshes { public get; public set; }
    public System.Void AddSlot(Il2CppScheduleOne.ItemFramework.ItemSlot slot, System.Boolean update = False)
    public System.Void DestroyExcessStoredItems(Il2CppScheduleOne.Storage.StorableItemInstance item, System.Int32 quantityRequirement)
    public Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Storage.StoredItem> EnsureSufficientStoredItems(Il2CppScheduleOne.Storage.StorableItemInstance item, System.Int32 quantityRequirement)
    public Il2CppSystem.Collections.Generic.Dictionary<Il2CppScheduleOne.Storage.StorableItemInstance, System.Int32> GetContentsDictionary()
    public Il2CppSystem.Collections.Generic.Dictionary<Il2CppScheduleOne.Storage.StorableItemInstance, System.Int32> GetVisualRepresentation()
    public System.Void QueueRefresh()
    public virtual System.Void RefreshVisuals()

public class StorageEntityVisualizer : StorageVisualizer
    Il2CppScheduleOne.Storage.StorageEntity storageEntity { public get; public set; }

public class StorageEntityInteractable : Il2CppScheduleOne.Interaction.InteractableObject
    Il2CppScheduleOne.Storage.StorageEntity StorageEntity { public get; public set; }
    public virtual System.Void Hovered()
    public virtual System.Void StartInteract()

public class StorageDoorAnimation : UnityEngine.MonoBehaviour
    System.Boolean IsOpen { public get; public set; }
    Il2CppScheduleOne.Storage.StorageEntity storageEntity { public get; public set; }
    public System.Void Open()  public System.Void Close()
    public System.Void OverrideState(System.Boolean open)   public System.Void ResetOverride()
    public System.Void SetIsOpen(System.Boolean open)       public virtual System.Void RefreshItemsVisible()

public class StorageTile : UnityEngine.MonoBehaviour
    System.Int32 x { public get; public set; }   System.Int32 y { public get; public set; }
    Il2CppScheduleOne.Storage.StorageGrid ownerGrid { public get; public set; }
    Il2CppSystem.Action onOccupantChanged { public get; public set; }
    Il2CppScheduleOne.Storage.StoredItem occupant { public get; public set; }
    public System.Void InitializeStorageTile(System.Int32 _x, System.Int32 _y, System.Single _available_Offset, Il2CppScheduleOne.Storage.StorageGrid _ownerGrid)
    public System.Void SetOccupant(Il2CppScheduleOne.Storage.StoredItem occ)

public class StoredItem : UnityEngine.MonoBehaviour                       // the visual box on a shelf
    Il2CppScheduleOne.Storage.StorableItemInstance item { public get; public set; }
    System.Boolean Destroyed { public get; public set; }
    Il2CppScheduleOne.Storage.StorageGrid parentGrid { public get; public set; }
    System.Int32 FootprintX { public get; }   System.Int32 FootprintY { public get; }
    System.Int32 totalArea { public get; }    System.Single Rotation { public get; }
    public System.Void ClearFootprintOccupancy()
    public virtual System.Void Destroy()
    public Il2CppScheduleOne.Tiles.FootprintTile GetTile(Il2CppScheduleOne.Tiles.Coordinate coord)
    public virtual System.Void InitializeStoredItem(Il2CppScheduleOne.Storage.StorableItemInstance _item, Il2CppScheduleOne.Storage.StorageGrid grid, UnityEngine.Vector2 _originCoordinate, System.Single _rotation)
    public System.Void RefreshTransform()
public class StoredItem_GenericBox : StoredItem      // + icon1/icon2/IconScale/ReferenceIconWidth
public class LiquidMeth_Stored     : StoredItem      // + Il2CppScheduleOne.Product.LiquidMethVisuals Visuals
public class StoredItemRandomRotation : UnityEngine.MonoBehaviour   public System.Void ApplyRotation()

public static class StorageVisualizationUtility : Il2CppSystem.Object
    public static Il2CppSystem.Collections.Generic.Dictionary<Il2CppScheduleOne.Storage.StorableItemInstance, System.Int32> GetVisualRepresentation(Il2CppSystem.Collections.Generic.Dictionary<Il2CppScheduleOne.Storage.StorableItemInstance, System.Int32> inputDictionary, System.Int32 TotalFootprintSize)

public class StorableItemInstance : Il2CppScheduleOne.ItemFramework.ItemInstance
    Il2CppScheduleOne.Storage.StoredItem StoredItem { public get; }
    public .ctor(Il2CppScheduleOne.ItemFramework.ItemDefinition definition, System.Int32 quantity)
    public virtual Il2CppScheduleOne.ItemFramework.ItemInstance GetCopy(System.Int32 overrideQuantity = -1)
    public virtual System.Single GetMonetaryValue()

public sealed struct CoordinateStorageTilePair          { Coordinate coord; StorageTile tile; }
public sealed struct CoordinateStorageFootprintTilePair { Coordinate coord; FootprintTile tile; }
```

### 5.2 `Il2CppScheduleOne.ItemFramework` (40 types)

#### `ItemSlot` — the atom of all item movement

```csharp
public class Il2CppScheduleOne.ItemFramework.ItemSlot : Il2CppSystem.Object
{
    Il2CppScheduleOne.ItemFramework.ItemInstance ItemInstance { public get; public set; }
    Il2CppScheduleOne.ItemFramework.IItemSlotOwner SlotOwner  { public get; public set; }
    Il2CppSystem.Action onItemDataChanged     { public get; public set; }
    Il2CppSystem.Action onItemInstanceChanged { public get; public set; }
    Il2CppScheduleOne.ItemFramework.ItemSlotLock ActiveLock { public get; public set; }
    Il2CppSystem.Action onLocked   { public get; public set; }
    Il2CppSystem.Action onUnlocked { public get; public set; }
    System.Boolean IsRemovalLocked { public get; public set; }
    System.Boolean IsAddLocked     { public get; public set; }
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemFilter> HardFilters { public get; public set; }
    System.Boolean CanPlayerSetFilter { public get; public set; }
    Il2CppScheduleOne.ItemFramework.SlotFilter PlayerFilter { public get; public set; }
    Il2CppSystem.Action onFilterChange { public get; public set; }
    Il2CppScheduleOne.ItemFramework.ItemSlotSiblingSet SiblingSet { public get; public set; }
    System.Int32   SlotIndex    { public get; }
    System.Int32   Quantity     { public get; }
    System.Boolean IsAtCapacity { public get; }
    System.Boolean IsLocked     { public get; }

    private .ctor()
    public  .ctor()
    public  .ctor(System.Boolean canPlayerSetFilter = False)
    public  .ctor(System.IntPtr pointer)

    public System.Void AddFilter(Il2CppScheduleOne.ItemFramework.ItemFilter filter)
    public virtual System.Void AddItem(Il2CppScheduleOne.ItemFramework.ItemInstance item, System.Boolean _internal = False)
    public System.Void ApplyLock(Il2CppFishNet.Object.NetworkObject lockOwner, System.String lockReason, System.Boolean _internal = False)
    public virtual System.Boolean CanSlotAcceptCash()
    public System.Void ChangeQuantity(System.Int32 change, System.Boolean _internal = False)
    public virtual System.Void ClearItemInstanceRequested()
    public virtual System.Void ClearStoredInstance(System.Boolean _internal = False)
    public virtual System.Boolean DoesItemMatchHardFilters(Il2CppScheduleOne.ItemFramework.ItemInstance item)
    public virtual System.Boolean DoesItemMatchPlayerFilters(Il2CppScheduleOne.ItemFramework.ItemInstance item)
    public virtual System.Int32 GetCapacityForItem(Il2CppScheduleOne.ItemFramework.ItemInstance item, System.Boolean checkPlayerFilters = False)
    public virtual System.Void InsertItem(Il2CppScheduleOne.ItemFramework.ItemInstance item)
    public virtual System.Void ItemDataChanged()
    public System.Void RemoveLock(System.Boolean _internal = False)
    public System.Void ReplicateStoredInstance()
    public System.Void SetFilterable(System.Boolean filterable)
    public System.Void SetIsAddLocked(System.Boolean locked)
    public System.Void SetIsRemovalLocked(System.Boolean locked)
    public System.Void SetPlayerFilter(Il2CppScheduleOne.ItemFramework.SlotFilter filter, System.Boolean _internal = False)
    public System.Void SetQuantity(System.Int32 amount, System.Boolean _internal = False)
    public System.Void SetSiblingSet(Il2CppScheduleOne.ItemFramework.ItemSlotSiblingSet set)
    public System.Void SetSlotOwner(Il2CppScheduleOne.ItemFramework.IItemSlotOwner owner)
    public virtual System.Void SetStoredItem(Il2CppScheduleOne.ItemFramework.ItemInstance instance, System.Boolean _internal = False)
    public static System.Boolean TryInsertItemIntoSet(Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemSlot> ItemSlots, Il2CppScheduleOne.ItemFramework.ItemInstance item)
}
```

**`_internal` semantics (inferred, but consistent across every call site):** `_internal = false`
(the default) means "this is an authoritative change — push it through the `SlotOwner`'s networked
`SetStoredInstance` / `SetItemSlotQuantity` RPC". `_internal = true` means "apply locally only,
I am already inside the RPC". **Mods should always leave it `false`.**

Note: the closest thing to a `TryInsertItem` you asked about is the **static**
`ItemSlot.TryInsertItemIntoSet(List<ItemSlot>, ItemInstance)` — there is no instance-level
`TryInsertItem` anywhere in `ItemFramework` or `Storage`.

#### `ItemSlotLock`, `ItemSlotSiblingSet`, `SlotFilter`

```csharp
public class ItemSlotLock : Il2CppSystem.Object
    Il2CppScheduleOne.ItemFramework.ItemSlot Slot { public get; public set; }
    Il2CppFishNet.Object.NetworkObject LockOwner  { public get; public set; }
    System.String LockReason { public get; public set; }
    public .ctor(Il2CppScheduleOne.ItemFramework.ItemSlot slot, Il2CppFishNet.Object.NetworkObject lockOwner, System.String lockReason)

public class ItemSlotSiblingSet : Il2CppSystem.Object      // slots that share a stack limit
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemSlot> Slots { public get; public set; }
    public .ctor(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppScheduleOne.ItemFramework.ItemSlot> slots)
    public .ctor(Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemSlot> slots)
    public .ctor(Il2CppScheduleOne.ItemFramework.ItemSlot[] slots)
    public System.Void AddSlot(Il2CppScheduleOne.ItemFramework.ItemSlot slot)

public class SlotFilter : Il2CppSystem.Object              // player-set filter, serialised into ItemSet
    Il2CppScheduleOne.ItemFramework.SlotFilter+EType Type { public get; public set; }
    Il2CppSystem.Collections.Generic.List<System.String> ItemIDs { public get; public set; }
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.EQuality> AllowedQualities { public get; public set; }
    public Il2CppScheduleOne.ItemFramework.SlotFilter Clone()
    public System.Boolean DoesItemMatchFilter(Il2CppScheduleOne.ItemFramework.ItemInstance instance)
    public System.Boolean IsDefault()
public enum SlotFilter+EType : System.Int32 { None = 0, Whitelist = 1, Blacklist = 2 }
```

#### `IItemSlotOwner`

```csharp
public class Il2CppScheduleOne.ItemFramework.IItemSlotOwner : Il2CppObjectBase   // interface
{
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemSlot> ItemSlots { public get; public set; }
    public virtual Il2CppScheduleOne.ItemFramework.ItemSlot GetFirstSlotContaining(System.String id)
    public virtual System.Int32 GetNonEmptySlotCount()
    public virtual System.Int32 GetQuantityOfItem(System.String id)
    public virtual System.Int32 GetQuantitySum()
    public System.Void Method_Private_Void_NetworkConnection_0(Il2CppFishNet.Connection.NetworkConnection conn)   // unstable name
    public virtual System.Void SendItemSlotDataToClient(Il2CppFishNet.Connection.NetworkConnection conn)
    public virtual System.Void SetItemSlotQuantity(System.Int32 itemSlotIndex, System.Int32 quantity)
    public virtual System.Void SetSlotFilter(Il2CppFishNet.Connection.NetworkConnection conn, System.Int32 itemSlotIndex, Il2CppScheduleOne.ItemFramework.SlotFilter filter)
    public virtual System.Void SetSlotLocked(Il2CppFishNet.Connection.NetworkConnection conn, System.Int32 itemSlotIndex, System.Boolean locked, Il2CppFishNet.Object.NetworkObject lockOwner, System.String lockReason)
    public virtual System.Void SetStoredInstance(Il2CppFishNet.Connection.NetworkConnection conn, System.Int32 itemSlotIndex, Il2CppScheduleOne.ItemFramework.ItemInstance instance)
}
```

#### `ItemInstance` / `ItemDefinition` and their `Core` bases — **stack limits live here**

```csharp
public class Il2CppScheduleOne.Core.Items.Framework.BaseItemDefinition : UnityEngine.ScriptableObject
{
    static System.Int32 DefaultStackLimit { public get; public set; }
    System.String Name        { public get; public set; }
    System.String ID          { public get; public set; }
    System.String Description { public get; public set; }
    UnityEngine.Sprite Icon   { public get; public set; }
    Il2CppScheduleOne.Core.Items.Framework.EItemCategory Category { public get; public set; }
    System.Int32   StackLimit     { public get; public set; }        // <-- THE stack limit
    System.Boolean UsableInFilters{ public get; public set; }
    Il2CppScheduleOne.Core.Equipping.Framework.EquippableData EquippableData { public get; public set; }
    Il2CppScheduleOne.Core.Items.Framework.ELegalStatus legalStatus { public get; public set; }
    public virtual System.Void ValidateDefinition()
}

public class Il2CppScheduleOne.Core.Items.Framework.BaseItemInstance : Il2CppSystem.Object
{
    static System.Int32 ApproximateByteSize { public get; public set; }
    Il2CppSystem.Action onDataChanged    { public get; public set; }
    Il2CppSystem.Action requestClearSlot { public get; public set; }
    Il2CppScheduleOne.Core.Items.Framework.BaseItemDefinition _definition { public get; public set; }
    System.String ID          { public get; }
    System.Int32  Quantity    { public get; public set; }
    System.String Name        { public get; }
    System.String Description { public get; }
    UnityEngine.Sprite Icon   { public get; }
    Il2CppScheduleOne.Core.Items.Framework.EItemCategory Category { public get; }
    System.Int32 StackLimit   { public get; }
    Il2CppScheduleOne.Core.Equipping.Framework.EquippableData EquippableData { public get; }
    public .ctor(Il2CppScheduleOne.Core.Items.Framework.BaseItemDefinition definition, System.Int32 quantity)
    public virtual System.Boolean CanStackWith(Il2CppScheduleOne.Core.Items.Framework.BaseItemInstance other, System.Boolean checkQuantities = True)
    public System.Void ChangeQuantity(System.Int32 change)
    public virtual System.Single GetMonetaryValue()
    public virtual System.Int32 GetTotalAmount()
    public System.Void InvokeDataChange()
    public virtual System.Boolean IsValidInstance()
    public System.Void RequestClearSlot()
    public System.Void SetQuantity(System.Int32 quantity)
}

public enum Il2CppScheduleOne.Core.Items.Framework.EItemCategory : System.Int32
{
    Product = 0, Packaging = 1, Agriculture = 2, Tools = 3, Furniture = 4, Lighting = 5,
    Cash = 6, Consumable = 7, Equipment = 8, Ingredient = 9, Decoration = 10,
    Clothing = 11, Storage = 12,
}
public enum Il2CppScheduleOne.Core.Items.Framework.ELegalStatus : System.Int32
{ Legal = 0, ControlledSubstance = 1, LowSeverityDrug = 2, ModerateSeverityDrug = 3, HighSeverityDrug = 4 }

public class Il2CppScheduleOne.ItemFramework.ItemDefinition : Il2CppScheduleOne.Core.Items.Framework.BaseItemDefinition
{
    System.Boolean AvailableInDemo { public get; public set; }
    Il2CppScheduleOne.ItemFramework.ItemDefinition+EEquipMode EquipMode { public get; public set; }
    Il2CppScheduleOne.Equipping.Equippable Equippable { public get; public set; }
    Il2CppScheduleOne.UI.Items.ItemUI CustomItemUI { public get; public set; }
    Il2CppScheduleOne.UI.Items.ItemInfoContent CustomInfoContent { public get; public set; }
    public virtual Il2CppScheduleOne.ItemFramework.ItemInstance GetDefaultInstance(System.Int32 quantity = 1)
}
public enum ItemDefinition+EEquipMode : System.Int32 { Legacy = 0, New = 1 }

public class Il2CppScheduleOne.ItemFramework.ItemInstance : Il2CppScheduleOne.Core.Items.Framework.BaseItemInstance
{
    Il2CppScheduleOne.ItemFramework.ItemDefinition Definition { public get; }
    Il2CppScheduleOne.Equipping.Equippable Equippable { public get; }
    public .ctor(Il2CppScheduleOne.ItemFramework.ItemDefinition definition, System.Int32 quantity)
    public virtual System.Boolean CanStackWith(Il2CppScheduleOne.ItemFramework.ItemInstance other, System.Boolean checkQuantities = True)
    public static Il2CppScheduleOne.ItemFramework.ItemInstance CreateInstanceAndRead(Il2CppFishNet.Serializing.Reader reader)
    public virtual Il2CppScheduleOne.ItemFramework.ItemInstance GetCopy(System.Int32 overrideQuantity = -1)
    public virtual Il2CppScheduleOne.Persistence.Datas.ItemData GetItemData()
    public virtual System.Void Read(Il2CppFishNet.Serializing.Reader reader)
    public virtual System.Void Write(Il2CppFishNet.Serializing.Writer writer)
}

public class Il2CppScheduleOne.ItemFramework.StorableItemDefinition : Il2CppScheduleOne.ItemFramework.ItemDefinition
{
    System.Single BasePurchasePrice { public get; public set; }
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.UI.Shop.ShopListing+CategoryInstance> ShopCategories { public get; public set; }
    System.Boolean RequiresLevelToPurchase { public get; public set; }
    Il2CppScheduleOne.Levelling.FullRank RequiredRank { public get; public set; }
    System.Single ResellMultiplier { public get; public set; }
    Il2CppScheduleOne.Storage.StoredItem StoredItem { public get; public set; }      // visual prefab on a shelf
    System.Single PickpocketDifficultyMultiplier { public get; public set; }
    Il2CppScheduleOne.StationFramework.StationItem StationItem { public get; public set; }
    System.Single CombatUtility { public get; public set; }
    System.Boolean IsUnlocked { public get; }
    public virtual Il2CppScheduleOne.ItemFramework.ItemInstance GetDefaultInstance(System.Int32 quantity = 1)
    public virtual System.Boolean GetIsUnlocked()
}
```

`StorableItemDefinition` subclasses: `AdditiveDefinition`, `BuildableItemDefinition`,
`CashDefinition`, `IntegerItemDefinition`, `QualityItemDefinition`, `ShroomSpawnDefinition`,
`SoilDefinition`, `SporeSyringeDefinition`, `WaterContainerDefinition`.
`StorableItemInstance` subclasses: `CashInstance`, `IntegerItemInstance`, `QualityItemInstance`,
`WaterContainerInstance`.

```csharp
public enum Il2CppScheduleOne.ItemFramework.EQuality : System.Int32
{ Trash = 0, Poor = 1, Standard = 2, Premium = 3, Heavenly = 4 }

public static class Il2CppScheduleOne.ItemFramework.ItemQuality : Il2CppSystem.Object
    static System.Single Heavenly_Threshold / Premium_Threshold / Standard_Threshold / Poor_Threshold
    static UnityEngine.Color Heavenly_Color / Premium_Color / Standard_Color / Poor_Color / Trash_Color
    public static UnityEngine.Color GetColor(Il2CppScheduleOne.ItemFramework.EQuality quality)
    public static Il2CppScheduleOne.ItemFramework.EQuality GetQuality(System.Single qualityScalar)
    public static Il2CppScheduleOne.ItemFramework.EQuality ShiftQuality(Il2CppScheduleOne.ItemFramework.EQuality baseQuality, System.Int32 shiftAmount)

public static class Il2CppScheduleOne.ItemFramework.ItemSerializers : Il2CppSystem.Object
    static System.String NullItem { public get; public set; }
    public static Il2CppScheduleOne.ItemFramework.ItemInstance ReadItemInstance(Il2CppFishNet.Serializing.Reader reader)
    public static Il2CppScheduleOne.Product.ProductItemInstance ReadProductItemInstance(Il2CppFishNet.Serializing.Reader reader)
    public static System.Void WriteItemInstance(Il2CppFishNet.Serializing.Writer writer, Il2CppScheduleOne.ItemFramework.ItemInstance value)
    public static System.Void WriteProductItemInstance(Il2CppFishNet.Serializing.Writer writer, Il2CppScheduleOne.Product.ProductItemInstance value)
```

Hard filters (`ItemSlot.HardFilters`) — `ItemFilter` hierarchy, each with
`public virtual System.Boolean DoesItemMatchFilter(Il2CppScheduleOne.ItemFramework.ItemInstance instance)`:
`ItemFilter` (base), `IDs` (`List<string> AcceptedIDs`), `ItemFilter_Category`
(`List<EItemCategory> AcceptedCategories`), `ItemFilter_ClothingSlot`, `ItemFilter_Dryable`
(+ `static System.Boolean IsItemDryable(ItemInstance instance)`), `ItemFilter_ID`
(`System.Boolean IsWhitelist`, `List<string> IDs`), `ItemFilter_LegalStatus`,
`ItemFilter_MixingIngredient`, `ItemFilter_PackagedProduct`, `ItemFilter_UnpackagedProduct`.

Also present: `ClipboardSlot : Il2CppScheduleOne.PlayerScripts.HotbarSlot`, `ItemGiver`,
`ItemRemover`, `ItemPickup`, `NetworkedItemPickup`, `CashPickup`.

---

## 6. `Il2CppScheduleOne.EntityFramework` (8 types)

This is the *placement* framework: anything the player can build, pick up, save, and give a GUID.

| Type | Base | Role |
|---|---|---|
| `BuildableItem` | `Il2CppFishNet.Object.NetworkBehaviour` | root of everything placeable; `IGUIDRegisterable` + `ISaveable` |
| `GridItem` | `BuildableItem` | snaps to a `Tiles.Grid` footprint (all stations, all placeable storage) |
| `ProceduralGridItem` | `BuildableItem` | free-form footprint over `ProceduralTile`s (grow lights) |
| `SurfaceItem` | `BuildableItem` | attaches to a `Building.Surface` (wall/roof) |
| `LabelledSurfaceItem` | `SurfaceItem` | + networked `Message` string |
| `ToggleableItem` | `GridItem` | + `IsOn` / `TurnOn` / `TurnOff` / `Toggle` |
| `ToggleableSurfaceItem` | `SurfaceItem` | same but wall-mounted |
| `IProceduralTileContainer` | interface | `List<Il2CppScheduleOne.Tiles.ProceduralTile> ProceduralTiles { get; }` |

Note the interface you asked about — `IGUIDRegisterable` — is **not** in `EntityFramework`. It is
the global-namespace `Il2CppScheduleOne.IGUIDRegisterable`:

```csharp
public class Il2CppScheduleOne.IGUIDRegisterable : Il2CppObjectBase   // interface
{
    Il2CppSystem.Guid GUID { public get; }
    public virtual System.Void SetGUID(System.String guid)
    public virtual System.Void SetGUID(Il2CppSystem.Guid guid)
}
```

### `BuildableItem` — the members a mod cares about

```csharp
Il2CppScheduleOne.ItemFramework.ItemInstance ItemInstance { public get; public set; }
Il2CppScheduleOne.Property.Property ParentProperty        { public get; public set; }
System.Boolean IsDestroyed  { public get; public set; }
System.Boolean Initialized  { public get; public set; }
Il2CppSystem.Guid GUID      { public get; public set; }
System.Boolean IsCulled     { public get; public set; }
System.Boolean isGhost      { public get; public set; }
UnityEngine.Transform BuildPoint         { public get; public set; }
UnityEngine.Transform MidAirCenterPoint  { public get; public set; }
UnityEngine.BoxCollider BoundingCollider { public get; public set; }
Il2CppSystem.Collections.Generic.List<UnityEngine.GameObject> OutlineRenderers { public get; public set; }
Il2CppEPOOutline.Outlinable OutlineEffect { public get; public set; }
UnityEngine.Events.UnityEvent onGhostModel  { public get; public set; }
UnityEngine.Events.UnityEvent onInitialized { public get; public set; }
UnityEngine.Events.UnityEvent onDestroyed   { public get; public set; }
Il2CppSystem.Action<Il2CppScheduleOne.EntityFramework.BuildableItem> onDestroyedWithParameter { public get; public set; }
System.Boolean _locallyBuilt { public get; public set; }
// + ISaveable block (SaveFolderName / SaveFileName / Loader / ShouldSaveUnderFolder / …)

public virtual System.Boolean CanBeDestroyed(out System.String& reason)
public System.Boolean CanBePickedUp(out System.String& reason)
public virtual System.Void Destroy()
public System.Void Destroy_Client()                      // ObserversRpc  (RpcWriter___Observers_Destroy_Client_2166136261)
public System.Void Destroy_Server()                      // ServerRpc     (RpcWriter___Server_Destroy_Server_2166136261)
public virtual Il2CppScheduleOne.Persistence.Datas.BuildableItemData GetBaseData()
public static UnityEngine.Color32 GetColorFromOutlineColorEnum(Il2CppScheduleOne.EntityFramework.BuildableItem+EOutlineColor col)
public virtual System.String GetDefaultManagementName()
public virtual System.String GetManagementName()
public System.Boolean GetPenetration(out System.Single& x, out System.Single& z, out System.Single& y)
public virtual Il2CppScheduleOne.Property.Property GetProperty(UnityEngine.Transform searchTransform = null)
public virtual Il2CppScheduleOne.Persistence.Datas.DynamicSaveData GetSaveData()
public virtual System.String GetSaveString()
public System.Boolean HasLoS_IgnoreBuildables(UnityEngine.Vector3 point)
public virtual System.Void HideOutline()
public System.Void InitializeBuildableItem(Il2CppScheduleOne.ItemFramework.ItemInstance instance, System.String GUID, System.String parentPropertyCode)
public virtual System.Void InitializeSaveable()
public System.Void PickupItem()
public virtual System.Void SendInitializationToClient(Il2CppFishNet.Connection.NetworkConnection conn)
public virtual System.Void SendInitializationToServer()
public virtual System.Void SetCulled(System.Boolean culled)
public virtual System.Void SetGUID(Il2CppSystem.Guid guid)
public System.Void SetLocallyBuilt()
public virtual System.Void ShowOutline(UnityEngine.Color color)
public System.Void ShowOutline(Il2CppScheduleOne.EntityFramework.BuildableItem+EOutlineColor color)
public virtual Il2CppSystem.Collections.Generic.List<System.String> WriteData(System.String parentFolderPath)

public enum BuildableItem+EOutlineColor : System.Int32 { White = 0, Blue = 1, LightBlue = 2 }
```

`InitializeBuildableItem(instance, GUID, parentPropertyCode)` is how an item gets bound to a
property — the closure `BuildableItem+__c__DisplayClass77_0` matches by
`_InitializeBuildableItem_b__0(Property p)` then `_InitializeBuildableItem_b__1(Business b)`, i.e.
by `PropertyCode` across both registries.

### `GridItem` (extra members)

```csharp
Il2CppScheduleOne.Tiles.Grid OwnerGrid { public get; public set; }
Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Tiles.CoordinateFootprintTilePair> CoordinateFootprintTilePairs { public get; public set; }
Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Tiles.CoordinatePair> CoordinatePairs { public get; public set; }
Il2CppScheduleOne.Tiles.FootprintTile OriginFootprint { public get; }
System.Int32 FootprintX { public get; }   System.Int32 FootprintY { public get; }

public virtual System.Void CalculateFootprintTileIntersections()
public virtual System.Boolean CanShareTileWith(Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.EntityFramework.GridItem> obstacles)
public System.Void ClearPositionData()
public System.Single GetAverageCosmeticTileTemperature()
public System.Single GetAverageTileTemperature()
public Il2CppScheduleOne.Tiles.FootprintTile GetFootprintTile(Il2CppScheduleOne.Tiles.Coordinate coord)
public Il2CppScheduleOne.Tiles.Tile GetParentTileAtFootprintCoordinate(Il2CppScheduleOne.Tiles.Coordinate footprintCoord)
public virtual System.Void InitializeGridItem(Il2CppScheduleOne.ItemFramework.ItemInstance instance, Il2CppScheduleOne.Tiles.Grid grid, UnityEngine.Vector2 originCoordinate, System.Int32 rotation, System.String GUID)
public System.Void InitializeGridItem_Client(Il2CppFishNet.Connection.NetworkConnection conn, Il2CppScheduleOne.ItemFramework.ItemInstance instance, System.String gridGUID, UnityEngine.Vector2 originCoordinate, System.Int32 rotation, System.String GUID)
public System.Void InitializeGridItem_Server(Il2CppScheduleOne.ItemFramework.ItemInstance instance, System.String gridGUID, UnityEngine.Vector2 originCoordinate, System.Int32 rotation, System.String GUID)
public System.Void ProcessGridData()
public System.Void RefreshTransform()
public System.Void SetFootprintTileVisiblity(System.Boolean visible)   // sic
public System.Void SetGridData(Il2CppSystem.Guid gridGUID, UnityEngine.Vector2 originCoordinate, System.Int32 rotation)
public System.Int32 ValidateRotation(System.Int32 rotation)
```

---

## 7. Stations

### 7.1 `Il2CppScheduleOne.StationFramework` (18 types)

There is **no single "Station" base class.** Stations are `EntityFramework.GridItem` subclasses that
each independently implement `ITransitEntity` + `IConfigurable` + `IItemSlotOwner` (+ usually
`IUsable`). `StationFramework` supplies the *shared machinery*, not a base type:

| Type | Base | Role |
|---|---|---|
| `StationRecipe` | `UnityEngine.ScriptableObject` | ingredient list → product, cook time/temp, quality calc |
| `StationItem` | `UnityEngine.MonoBehaviour` | the physical prop an ingredient becomes on a station |
| `ItemModule` | `UnityEngine.MonoBehaviour` | behaviour plugin attached to a `StationItem` |
| `CookableModule` | `ItemModule` | cook time / product / shards |
| `IngredientModule` | `ItemModule` | breakable pieces |
| `PourableModule` | `ItemModule` | pourable liquid source |
| `Fillable` | `UnityEngine.MonoBehaviour` | receives poured liquid |
| `BoilingFlask` | `Fillable` | temperature sim for chemistry |
| `LiquidContainer`, `LiquidVolumeCollider`, `LiquidLevelVisuals` | `MonoBehaviour` | liquid rendering |
| `IngredientPiece` | `MonoBehaviour` | dissolvable chunk |
| `PourableAngleLimit` | `MonoBehaviour` | drag constraint |
| `MushroomSpawnStation` | **`EntityFramework.GridItem`** | **the only actual station in this namespace** |
| `MushroomSpawnStationItem`, `SporeSyringeStationItem`, `ProductStationItem`, `LiquidMeth_StationItem` | `StationItem` | props |

```csharp
public class Il2CppScheduleOne.StationFramework.StationRecipe : UnityEngine.ScriptableObject
{
    System.Boolean IsDiscovered { public get; public set; }
    System.String  RecipeTitle  { public get; public set; }
    System.Boolean Unlocked     { public get; public set; }
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.StationFramework.StationRecipe+IngredientQuantity> Ingredients { public get; public set; }
    Il2CppScheduleOne.StationFramework.StationRecipe+ItemQuantity Product { public get; public set; }
    UnityEngine.Color FinalLiquidColor { public get; public set; }
    System.Int32  CookTime_Mins  { public get; public set; }
    System.Single CookTemperature{ public get; public set; }
    System.Single CookTemperatureTolerance { public get; public set; }
    Il2CppScheduleOne.StationFramework.StationRecipe+EQualityCalculationMethod QualityCalculationMethod { public get; public set; }
    System.Single CookTemperatureLowerBound { public get; }
    System.Single CookTemperatureUpperBound { public get; }
    System.String RecipeID { public get; }

    public Il2CppScheduleOne.ItemFramework.EQuality CalculateQuality(Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemInstance> ingredients)
    public System.Boolean DoIngredientsSuffice(Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemInstance> ingredients)
    public Il2CppScheduleOne.Storage.StorableItemInstance GetProductInstance(Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemInstance> ingredients)
    public Il2CppScheduleOne.Storage.StorableItemInstance GetProductInstance(Il2CppScheduleOne.ItemFramework.EQuality quality)
}
public enum StationRecipe+EQualityCalculationMethod : System.Int32 { Additive = 0 }
public class StationRecipe+IngredientQuantity { List<ItemDefinition> Items; Int32 Quantity; ItemDefinition Item { get; } }
public class StationRecipe+ItemQuantity       { ItemDefinition Item; Int32 Quantity; }

public class Il2CppScheduleOne.StationFramework.StationItem : UnityEngine.MonoBehaviour
{
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.StationFramework.ItemModule> Modules { public get; public set; }
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.StationFramework.ItemModule> ActiveModules { public get; public set; }
    Il2CppScheduleOne.Trash.TrashItem TrashPrefab { public get; public set; }
    public System.Void ActivateModule<T>()
    public System.Void Destroy()
    public T GetModule<T>()
    public System.Boolean HasModule<T>()
    public virtual System.Void Initialize(Il2CppScheduleOne.ItemFramework.StorableItemDefinition itemDefinition)
}
```

### 7.2 How a station exposes I/O

Every station declares the `ITransitEntity` contract **plus** its own named convenience slots, and
`ItemSlots` (the `IItemSlotOwner` view). `InputSlots` / `OutputSlots` are populated from the named
slots in `Awake` *(inferred)*.

| Station (`Il2CppScheduleOne.ObjectScripts.*` unless noted) | Named slots |
|---|---|
| `BrickPress` | `Il2CppReferenceArray<ItemSlot> ProductSlots`, `ItemSlot OutputSlot` |
| `Cauldron` | `Il2CppReferenceArray<ItemSlot> IngredientSlots`, `ItemSlot LiquidSlot`, `ItemSlot OutputSlot` |
| `ChemistryStation` | `Il2CppReferenceArray<ItemSlot> IngredientSlots`, `ItemSlot OutputSlot` |
| `DryingRack` | `Il2CppReferenceArray<ItemSlot> hangSlots`, `ItemSlot InputSlot`, `ItemSlot OutputSlot` |
| `LabOven` | `ItemSlot IngredientSlot`, `ItemSlot OutputSlot` |
| `MixingStation` | `ItemSlot ProductSlot`, `ItemSlot MixerSlot`, `ItemSlot OutputSlot` |
| `MixingStationMk2` | inherits `MixingStation` |
| `PackagingStation` | `ItemSlot PackagingSlot`, `ItemSlot ProductSlot`, `ItemSlot OutputSlot` |
| `StationFramework.MushroomSpawnStation` | `ItemSlot GrainBagSlot`, `ItemSlot SyringeSlot`, `ItemSlot OutputSlot` |
| `Pot` / `MushroomBed` | inherit `Growing.GrowContainer` (`InputSlots` / `OutputSlots` only) |
| `PlaceableStorageEntity` | none of its own — delegates to `StorageEntity StorageEntity` |
| `TrashContainerItem` | none of its own — `Il2CppScheduleOne.Trash.TrashContainer Container` |

### 7.3 `Il2CppScheduleOne.ObjectScripts` — complete type/base index

**I read all 42 top-level types' headers and bases; I only expanded the storage- and
transit-relevant ones in detail.** Skipped-in-detail (props, sub-widgets and minigame pieces, none
of which are `ITransitEntity`): `Beaker`, `BrickPressContainer`, `BrickPressHandle`, `BunsenBurner`,
`CashCounter`, `CauldronDisplayTub`, `ChemistryCookOperation`, `DryingOperation`, `FloorRack`,
`GrowLight`, `Jukebox`, `JukeboxInterface`, `LabOvenButton`, `LabOvenDoor`, `LabOvenHammer`,
`LabOvenWireTray`, `LabStand`, `MixOperation`, `OvenCookOperation`, `Recycler`, `SoilPourer`,
`Sprinkler`, `StirringRod`, `Toilet`, `VMSBoard`, `VendingMachine`.

```
Beaker                 : StationFramework.StationItem
Bed                    : Il2CppFishNet.Object.NetworkBehaviour
BedItem                : ObjectScripts.PlaceableStorageEntity        <-- ITransitEntity (inherited)
BrickPress             : EntityFramework.GridItem                    <-- ITransitEntity
BrickPressContainer    : UnityEngine.MonoBehaviour
BrickPressHandle       : UnityEngine.MonoBehaviour
BunsenBurner           : UnityEngine.MonoBehaviour
CashCounter            : UnityEngine.MonoBehaviour
Cauldron               : EntityFramework.GridItem                    <-- ITransitEntity
CauldronDisplayTub     : UnityEngine.MonoBehaviour
ChemistryCookOperation : Il2CppSystem.Object
ChemistryStation       : EntityFramework.GridItem                    <-- ITransitEntity
DryingOperation        : Il2CppSystem.Object
DryingRack             : EntityFramework.GridItem                    <-- ITransitEntity
FloorRack              : EntityFramework.GridItem
GrowLight              : EntityFramework.ProceduralGridItem
Jukebox                : EntityFramework.GridItem
JukeboxInterface       : UnityEngine.MonoBehaviour
LabOven                : EntityFramework.GridItem                    <-- ITransitEntity
LabOvenButton          : UnityEngine.MonoBehaviour
LabOvenDoor            : UnityEngine.MonoBehaviour
LabOvenHammer          : UnityEngine.MonoBehaviour
LabOvenWireTray        : UnityEngine.MonoBehaviour
LabStand               : UnityEngine.MonoBehaviour
LaunderingStation      : EntityFramework.GridItem
MixOperation           : Il2CppSystem.Object
MixingStation          : EntityFramework.GridItem                    <-- ITransitEntity
MixingStationMk2       : ObjectScripts.MixingStation                 <-- ITransitEntity (inherited)
MushroomBed            : Growing.GrowContainer                       <-- ITransitEntity (inherited)
OvenCookOperation      : Il2CppSystem.Object
PackagingStation       : EntityFramework.GridItem                    <-- ITransitEntity
PlaceableStorageEntity : EntityFramework.GridItem                    <-- ITransitEntity (STORAGE)
Pot                    : Growing.GrowContainer                       <-- ITransitEntity (inherited)
Recycler               : Il2CppFishNet.Object.NetworkBehaviour
SoilPourer             : EntityFramework.GridItem
Sprinkler              : EntityFramework.GridItem
StirringRod            : UnityEngine.MonoBehaviour
SurfaceStorageEntity   : EntityFramework.SurfaceItem                 <-- PARTIAL, see §3.4
Toilet                 : EntityFramework.GridItem
TrashContainerItem     : EntityFramework.GridItem                    <-- ITransitEntity
VMSBoard               : UnityEngine.MonoBehaviour
VendingMachine         : Il2CppFishNet.Object.NetworkBehaviour
```

#### `PlaceableStorageEntity` — the storage transit endpoint

```csharp
Il2CppScheduleOne.Storage.StorageEntity StorageEntity { public get; public set; }
Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<UnityEngine.Transform> accessPoints { public get; public set; }
UnityEngine.Transform _linkOrigin { public get; public set; }
Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppTMPro.TextMeshPro> _nameLabels { public get; public set; }
Il2CppScheduleOne.ObjectScripts.PlaceableStorageEntity+ENameLabelVisibility _nameLabelVisibility { public get; public set; }
// ITransitEntity surface:
System.String Name { public get; }
Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemSlot> InputSlots  { public get; public set; }
Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemSlot> OutputSlots { public get; public set; }
UnityEngine.Transform LinkOrigin { public get; }
Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<UnityEngine.Transform> AccessPoints { public get; }
System.Boolean Selectable { public get; }
System.Boolean IsAcceptingItems { public get; public set; }
// IConfigurable surface: Configuration / ConfigReplicator / ConfigurableType / WorldspaceUI /
//                        CurrentPlayerConfigurer / TypeIcon / Transform / UIPoint / CanBeSelected
// IUsable surface:       NPCUserObject / PlayerUserObject (+ SyncVars)

public virtual System.Boolean CanBeDestroyed(out System.String& reason)
public virtual Il2CppScheduleOne.UI.Management.WorldspaceUIElement CreateWorldspaceUI()
public virtual System.Void DestroyWorldspaceUI()
public virtual Il2CppScheduleOne.Persistence.Datas.BuildableItemData GetBaseData()
public virtual System.String GetManagementName()
public virtual Il2CppScheduleOne.Persistence.Datas.DynamicSaveData GetSaveData()
public virtual System.Void InitializeGridItem(Il2CppScheduleOne.ItemFramework.ItemInstance instance, Il2CppScheduleOne.Tiles.Grid grid, UnityEngine.Vector2 originCoordinate, System.Int32 rotation, System.String GUID)
public System.Void NameChanged(System.String newName)
public virtual System.Void SendConfigurationToClient(Il2CppFishNet.Connection.NetworkConnection conn)
public virtual System.Void SetConfigurer(Il2CppFishNet.Object.NetworkObject player)   // ServerRpc 3323014238
public virtual System.Void SetNPCUser(Il2CppFishNet.Object.NetworkObject npcObject)   // ServerRpc 3323014238
public virtual System.Void SetPlayerUser(Il2CppFishNet.Object.NetworkObject playerObject) // ServerRpc 3323014238
public System.Void UpdateNameLabels()

public enum PlaceableStorageEntity+ENameLabelVisibility : System.Int32 { None = 0, WhenNotDefault = 1, Always = 2 }
```

#### `TrashContainerItem` (the other non-station transit endpoint)

```csharp
static System.Single MAX_VERTICAL_OFFSET { public get; public set; }
Il2CppScheduleOne.Trash.TrashContainer Container { public get; public set; }
System.Boolean UsableByCleaners  { public get; public set; }
System.Single  PickupSquareWidth { public get; public set; }
Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Trash.TrashItem> TrashItemsInRadius { public get; public set; }
Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Trash.TrashBag>  TrashBagsInRadius  { public get; public set; }
// full ITransitEntity surface: Name / InputSlots / OutputSlots / LinkOrigin / AccessPoints / Selectable / IsAcceptingItems
public System.Void AddTrashBagToRadius(Il2CppScheduleOne.Trash.TrashBag trashBag)
public System.Void AddTrashToRadius(Il2CppScheduleOne.Trash.TrashItem trashItem)
public System.Void CheckTrashItems()
public System.Boolean IsPointInPickupZone(UnityEngine.Vector3 point)
public System.Boolean IsTrashValid(Il2CppScheduleOne.Trash.TrashItem trashItem)
public System.Void TrashAdded(System.String trashID)
public System.Void TrashLevelChanged()
```

#### `Il2CppScheduleOne.Delivery.LoadingDock` (transit endpoint, not a `BuildableItem`)

```csharp
Il2CppScheduleOne.Vehicles.LandVehicle DynamicOccupant { public get; public set; }
Il2CppScheduleOne.Vehicles.LandVehicle StaticOccupant  { public get; public set; }
Il2CppScheduleOne.Property.Property ParentProperty     { public get; public set; }
Il2CppScheduleOne.DevUtilities.VehicleDetector VehicleDetector { public get; public set; }
Il2CppScheduleOne.Map.ParkingLot Parking { public get; public set; }
System.Boolean IsInUse { public get; }
Il2CppSystem.Guid GUID { public get; public set; }
System.String BakedGUID { public get; public set; }
// full ITransitEntity surface incl. IsDestroyed, ShowOutline/HideOutline
public System.Void RefreshOccupant()
public System.Void RegenerateGUID()
public virtual System.Void SetGUID(Il2CppSystem.Guid guid)
public System.Void SetOccupant(Il2CppScheduleOne.Vehicles.LandVehicle occupant)
public System.Void SetStaticOccupant(Il2CppScheduleOne.Vehicles.LandVehicle vehicle)
```

**For a "Hireable Drivers" mod this is the single most interesting implementor** — it is the only
`ITransitEntity` that already has a `LandVehicle`, a `ParkingLot`, and a `Property` attached.

---

## 8. Management interface / UI

### `Il2CppScheduleOne.Management.UI` (1 type)

```csharp
public class Il2CppScheduleOne.Management.UI.ConfigPanel : UnityEngine.MonoBehaviour
{
    Il2CppScheduleOne.UIContentPanel ContentPanel { public get; public set; }
    public System.Void Bind(Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Management.EntityConfiguration> configs, Il2CppScheduleOne.UIScreen screen = null)
    public virtual System.Void BindInternal(Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Management.EntityConfiguration> configs)
    public System.Void ConfigureScreen(Il2CppScheduleOne.UIScreen screen)
}
```

### `ManagementInterface` — the clipboard root

```csharp
public class Il2CppScheduleOne.Management.ManagementInterface
    : Il2CppScheduleOne.DevUtilities.Singleton<Il2CppScheduleOne.Management.ManagementInterface>
{
    static System.Single PANEL_SLIDE_TIME { public get; public set; }
    Il2CppScheduleOne.Tools.ManagementClipboard_Equippable EquippedClipboard { public get; public set; }
    UnityEngine.Canvas Canvas { public get; public set; }
    Il2CppTMPro.TextMeshProUGUI NothingSelectedLabel        { public get; public set; }
    Il2CppTMPro.TextMeshProUGUI DifferentTypesSelectedLabel { public get; public set; }
    UnityEngine.RectTransform PanelContainer { public get; public set; }
    Il2CppScheduleOne.UI.Management.ClipboardScreen  MainScreen           { public get; public set; }
    Il2CppScheduleOne.UI.Management.ItemSelector     ItemSelectorScreen   { public get; public set; }
    Il2CppScheduleOne.UI.Management.ObjectSelector   ObjectSelector       { public get; public set; }
    Il2CppScheduleOne.UI.Management.RecipeSelector   RecipeSelectorScreen { public get; public set; }
    Il2CppScheduleOne.UI.Management.TransitEntitySelector TransitEntitySelector { public get; public set; }
    Il2CppScheduleOne.UI.Management.StringSetter     StringSetterScreen   { public get; public set; }
    UnityEngine.UI.Button RenameButton { public get; public set; }
    Il2CppScheduleOne.UIScreen UIScreen { public get; public set; }
    Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppScheduleOne.Management.ManagementInterface+ConfigurableTypePanel> ConfigPanelPrefabs { public get; public set; }
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Management.IConfigurable> Configurables { public get; public set; }
    System.Boolean areConfigurablesUniform { public get; public set; }
    Il2CppScheduleOne.Management.UI.ConfigPanel loadedPanel { public get; public set; }
    System.Int32 _lastSelectableIndex { public get; public set; }

    public System.Void Close(System.Boolean preserveState = False)
    public System.Void DestroyConfigPanel()
    public Il2CppScheduleOne.Management.UI.ConfigPanel GetConfigPanelPrefab(Il2CppScheduleOne.Management.EConfigurableType type)
    public System.Void InitializeConfigPanel()
    public System.Void Method_Private_Void_String_PDM_0(System.String newName)     // unstable name (rename callback)
    public System.Void Open(Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Management.IConfigurable> configurables, Il2CppScheduleOne.Tools.ManagementClipboard_Equippable _equippedClipboard)
    public System.Void RenameButtonClicked()
    public System.Void UpdateMainLabels()
}

public class ManagementInterface+ConfigurableTypePanel : Il2CppSystem.Object
{
    Il2CppScheduleOne.Management.EConfigurableType Type { public get; public set; }
    Il2CppScheduleOne.Management.UI.ConfigPanel Panel   { public get; public set; }
}
```

**To add your own management entry from a mod:** append a `ConfigurableTypePanel` to
`ManagementInterface.ConfigPanelPrefabs` (an `Il2CppReferenceArray`, so you must rebuild the array)
with a `Type` and a prefab whose root has your `ConfigPanel` subclass, and make your entity's
`IConfigurable.ConfigurableType` return that value. `GetConfigPanelPrefab(type)` does a linear
lookup (closure `ManagementInterface+__c__DisplayClass29_0._GetConfigPanelPrefab_b__0`). Because
`EConfigurableType` is a fixed IL2CPP enum you cannot add new values — you must reuse an existing
one (`Storage = 14` is the least-used) or Harmony-patch `GetConfigPanelPrefab`.

### `Il2CppScheduleOne.UI.Management` (49 types)

Screens: `ClipboardScreen` (base of `ItemSelector`, `RecipeSelector`, `StringSetter`),
`ObjectSelector`, `TransitEntitySelector`, `SelectionInfoUI`, `ManagementWorldspaceCanvas`,
`WorldspaceUIElement` (+ 13 `*UIElement` subclasses), `AssignedWorkerDisplay`.
Field editors: `ItemFieldUI`, `NPCFieldUI`, `NumberFieldUI`, `ObjectFieldUI`, `ObjectListFieldUI`,
`QualityFieldUI`, `RouteListFieldUI`, `RouteEntryUI`, `StationRecipeFieldUI`, `StringFieldUI`.
Panels: 14 `*ConfigPanel : Il2CppScheduleOne.Management.UI.ConfigPanel`.

The two that matter for routing:

```csharp
public class Il2CppScheduleOne.UI.Management.TransitEntitySelector : UnityEngine.MonoBehaviour
{
    static System.Single SELECTION_RANGE { public get; public set; }
    System.Boolean IsOpen { public get; public set; }
    Il2CppScheduleOne.State.MonoState State { public get; public set; }
    UnityEngine.LayerMask DetectionMask { public get; public set; }
    UnityEngine.Color HoverOutlineColor  { public get; public set; }
    UnityEngine.Color SelectOutlineColor { public get; public set; }
    System.Int32 maxSelectedObjects { public get; public set; }
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Management.ITransitEntity> selectedObjects { public get; public set; }
    Il2CppSystem.Collections.Generic.List<Il2CppSystem.Type> typeRequirements { public get; public set; }
    Il2CppScheduleOne.UI.Management.TransitEntitySelector+ObjectFilter objectFilter { public get; public set; }
    Il2CppSystem.Action<Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Management.ITransitEntity>> callback { public get; public set; }
    Il2CppScheduleOne.Management.ITransitEntity hoveredObj     { public get; public set; }
    Il2CppScheduleOne.Management.ITransitEntity highlightedObj { public get; public set; }
    System.String selectionTitle { public get; public set; }
    Il2CppSystem.Collections.Generic.List<UnityEngine.Transform> transitSources { public get; public set; }
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Management.TransitLineVisuals> transitLines { public get; public set; }
    System.Boolean selectDestination { public get; public set; }

    public System.Void ClearSelection()
    public System.Void ClipboardClosed()
    public System.Void CloseAndCancel()
    public System.Void CloseAndSubmit()
    public System.Void Exit(Il2CppScheduleOne.ExitAction exitAction)
    public Il2CppScheduleOne.Management.ITransitEntity GetHoveredObject()
    public System.Boolean IsObjectTypeValid(Il2CppScheduleOne.Management.ITransitEntity obj, out System.String& reason)
    public System.Void ObjectClicked(Il2CppScheduleOne.Management.ITransitEntity obj)
    public System.Void OnClose()
    public virtual System.Void Open(System.String _selectionTitle, System.String instruction, System.Int32 _maxSelectedObjects, Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Management.ITransitEntity> _selectedObjects, Il2CppSystem.Collections.Generic.List<Il2CppSystem.Type> _typeRequirements, Il2CppScheduleOne.UI.Management.TransitEntitySelector+ObjectFilter _objectFilter, Il2CppSystem.Action<Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Management.ITransitEntity>> _callback, Il2CppSystem.Collections.Generic.List<UnityEngine.Transform> transitLineSources = null, System.Boolean selectingDestination = True)
    public System.Void SetSelectionOutline(Il2CppScheduleOne.Management.ITransitEntity obj, System.Boolean on)
    public System.Void UpdateInstructions()
    public System.Void UpdateTransitLines()
}
public sealed class TransitEntitySelector+ObjectFilter : Il2CppSystem.MulticastDelegate
    public virtual System.Boolean Invoke(Il2CppScheduleOne.Management.ITransitEntity obj, out System.String& reason)

public class Il2CppScheduleOne.UI.Management.RouteEntryUI : UnityEngine.MonoBehaviour
{
    Il2CppScheduleOne.Management.AdvancedTransitRoute AssignedRoute { public get; public set; }
    UnityEngine.UI.Image SourceIcon      { public get; public set; }
    Il2CppTMPro.TextMeshProUGUI SourceLabel      { public get; public set; }
    UnityEngine.UI.Image DestinationIcon { public get; public set; }
    Il2CppTMPro.TextMeshProUGUI DestinationLabel { public get; public set; }
    UnityEngine.UI.Image FilterIcon      { public get; public set; }
    UnityEngine.Events.UnityEvent onDeleteClicked { public get; public set; }
    System.Boolean settingSource      { public get; public set; }
    System.Boolean settingDestination { public get; public set; }

    public System.Void AssignRoute(Il2CppScheduleOne.Management.AdvancedTransitRoute route)
    public System.Void ClearRoute()
    public System.Void DeleteClicked()
    public System.Void DestinationClicked()
    public System.Void FilterClicked()
    public System.Boolean ObjectValid(Il2CppScheduleOne.Management.ITransitEntity obj, out System.String& reason)
    public System.Void ObjectsSelected(Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Management.ITransitEntity> objs)
    public System.Void RefreshUI()
    public System.Void SourceClicked()
}

public class Il2CppScheduleOne.UI.Management.RouteListFieldUI : UnityEngine.MonoBehaviour
{
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Management.RouteListField> Fields { public get; public set; }
    System.String FieldText { public get; public set; }
    Il2CppTMPro.TextMeshProUGUI FieldLabel { public get; public set; }
    Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppScheduleOne.UI.Management.RouteEntryUI> RouteEntries { public get; public set; }
    UnityEngine.RectTransform MultiEditBlocker { public get; public set; }
    UnityEngine.UI.Button AddButton { public get; public set; }

    public System.Void AddClicked()
    public System.Void Bind(Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Management.RouteListField> field)
    public System.Void EntryDeleteClicked(Il2CppScheduleOne.UI.Management.RouteEntryUI entry)
    public System.Void Refresh(Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Management.AdvancedTransitRoute> newVal)
    public System.Void RouteChanged(Il2CppScheduleOne.Management.ITransitEntity newEntity)
}
```

`ObjectSelector` is the `BuildableItem` twin of `TransitEntitySelector` (same shape, plus
`Il2CppScheduleOne.Property.Property targetProperty` and `System.Boolean changesMade`, and its
`Open` takes a `Property property` parameter). `ObjectSelector+ObjectFilter` is
`Invoke(BuildableItem obj, out String& reason)`.

```csharp
public class Il2CppScheduleOne.UI.Management.WorldspaceUIElement : UnityEngine.MonoBehaviour
    static System.Single TRANSITION_TIME { public get; public set; }
    System.Boolean IsEnabled { public get; public set; }   System.Boolean IsVisible { public get; }
    UnityEngine.RectTransform RectTransform { public get; public set; }
    UnityEngine.RectTransform Container     { public get; public set; }
    Il2CppTMPro.TextMeshProUGUI TitleLabel  { public get; public set; }
    Il2CppScheduleOne.UI.Management.AssignedWorkerDisplay AssignedWorkerDisplay { public get; public set; }
    public virtual System.Void Destroy()
    public virtual System.Void Hide(Il2CppSystem.Action callback = null)
    public virtual System.Void HoverStart()   public virtual System.Void HoverEnd()
    public System.Void SetAssignedNPC(Il2CppScheduleOne.NPCs.NPC npc)
    public virtual System.Void SetInternalScale(System.Single scale)
    public System.Void SetScale(System.Single scale, Il2CppSystem.Action callback)
    public virtual System.Void Show()
    public System.Void UpdatePosition(UnityEngine.Vector3 worldSpacePosition)

public class Il2CppScheduleOne.UI.Management.StorageUIElement : WorldspaceUIElement
    Il2CppScheduleOne.ObjectScripts.PlaceableStorageEntity AssignedEntity { public get; public set; }
    UnityEngine.UI.Image Icon { public get; public set; }
    public System.Void Initialize(Il2CppScheduleOne.ObjectScripts.PlaceableStorageEntity entity)
    public virtual System.Void RefreshUI()
```

### `Il2CppScheduleOne.Building` / `.Building.Doors`

```csharp
public class Il2CppScheduleOne.Building.BuildManager
    : Il2CppScheduleOne.DevUtilities.NetworkSingleton<Il2CppScheduleOne.Building.BuildManager>
{
    System.Boolean isBuilding { public get; public set; }
    UnityEngine.GameObject currentBuildHandler { public get; public set; }
    UnityEngine.Material ghostMaterial_White { public get; public set; }
    UnityEngine.Material ghostMaterial_Red   { public get; public set; }
    public Il2CppScheduleOne.EntityFramework.GridItem CreateGridItem(Il2CppScheduleOne.ItemFramework.ItemInstance item, Il2CppScheduleOne.Tiles.Grid grid, UnityEngine.Vector2 originCoordinate, System.Int32 rotation, System.String guid = "", Il2CppSystem.Action<Il2CppScheduleOne.EntityFramework.GridItem> onBeforeSpawn = null)
    public Il2CppScheduleOne.EntityFramework.ProceduralGridItem CreateProceduralGridItem(Il2CppScheduleOne.ItemFramework.ItemInstance item, System.Int32 rotationAngle, Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Tiles.CoordinateProceduralTilePair> matches, System.String guid = "")
    public Il2CppScheduleOne.EntityFramework.SurfaceItem CreateSurfaceItem(Il2CppScheduleOne.ItemFramework.ItemInstance item, Il2CppScheduleOne.Building.Surface parentSurface, UnityEngine.Vector3 relativePosition, UnityEngine.Quaternion relativeRotation, System.String guid = "")
    public System.Void StartBuilding(Il2CppScheduleOne.ItemFramework.ItemInstance item)
    public System.Void StopBuilding()
    public System.Void PlayBuildSound(Il2CppScheduleOne.ItemFramework.BuildableItemDefinition+EBuildSoundType type, UnityEngine.Vector3 point)
    public System.Void ApplyMaterial(UnityEngine.GameObject obj, UnityEngine.Material mat, System.Boolean allMaterials = True)
    // + DisableCanvases / DisableColliders / DisableLights / DisableNavigation / DisableNetworking / DisableSpriteRenderers
}

public class Il2CppScheduleOne.Building.Surface : UnityEngine.MonoBehaviour   // IGUIDRegisterable
    Il2CppSystem.Guid GUID { public get; public set; }   System.String BakedGUID { public get; public set; }
    Il2CppScheduleOne.Building.Surface+ESurfaceType SurfaceType { public get; public set; }
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Building.Surface+EFace> ValidFaces { public get; public set; }
    Il2CppScheduleOne.Property.Property ParentProperty { public get; public set; }
    UnityEngine.Transform Container { public get; }
    public UnityEngine.Vector3 GetRelativePosition(UnityEngine.Vector3 worldPosition)
    public UnityEngine.Quaternion GetRelativeRotation(UnityEngine.Quaternion worldRotation)
    public System.Boolean IsFrontFace(UnityEngine.Vector3 point, UnityEngine.Collider collider)
    public System.Boolean IsPointValid(UnityEngine.Vector3 point, UnityEngine.Collider hitCollider)
    public System.Void RegenerateGUID()
    public virtual System.Void SetGUID(Il2CppSystem.Guid guid)
public enum Surface+ESurfaceType : System.Int32 { Wall = 0, Roof = 1 }
public enum Surface+EFace : System.Int32 { Front = 0, Back = 1, Top = 2, Bottom = 3, Left = 4, Right = 5 }

// Build lifecycle hooks (one per placement mode):
BuildStart_Base / BuildStart_Grid / BuildStart_ProceduralGrid / BuildStart_Surface / BuildStart_AirConditioner
BuildUpdate_Base / BuildUpdate_Grid / BuildUpdate_GrowContainer / BuildUpdate_ProceduralGrid / BuildUpdate_Surface / BuildUpdate_AirConditioner
BuildStop_Base / BuildStop_AirConditioner
ActivateDuringBuild / CornerObstacle / OverrideGhostMaterial / TileIntersection

public class Il2CppScheduleOne.Building.Doors.PropertyDoorController : Il2CppScheduleOne.Doors.DoorController
    static System.Single WANTED_PLAYER_CLOSE_DISTANCE { public get; public set; }
    Il2CppScheduleOne.Property.Property Property { public get; public set; }
    System.Boolean IsUnlocked { public get; public set; }
    public virtual System.Boolean CanPlayerAccess(Il2CppScheduleOne.Doors.EDoorSide side, out System.String& reason)
    public System.Void CheckClose()
    public Il2CppScheduleOne.PlayerScripts.Player GetNearestWantedPlayer()
    public System.Void Unlock()

public class Il2CppScheduleOne.Building.Doors.DoorKnocker : UnityEngine.MonoBehaviour
    UnityEngine.Animation Anim { public get; public set; }
    System.String KnockingSoundClipName { public get; public set; }
    UnityEngine.AudioSource KnockingSound { public get; public set; }
    public System.Void Knock()   public System.Void PlayKnockingSound()
```

---

## 9. Save data

### 9.1 The DTOs

```csharp
public class Il2CppScheduleOne.Persistence.Datas.PropertyData : SaveData
{
    System.String  PropertyCode { public get; public set; }
    System.Boolean IsOwned      { public get; public set; }
    Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<System.Boolean> SwitchStates     { public get; public set; }
    Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<System.Boolean> ToggleableStates { public get; public set; }
    Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppScheduleOne.Persistence.Datas.DynamicSaveData> Employees { public get; public set; }
    Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppScheduleOne.Persistence.Datas.DynamicSaveData> Objects   { public get; public set; }
    public .ctor(System.String propertyCode, System.Boolean isOwned, Il2CppStructArray<System.Boolean> switchStates, Il2CppStructArray<System.Boolean> toggleableStates, Il2CppReferenceArray<DynamicSaveData> employees, Il2CppReferenceArray<DynamicSaveData> objects)
}

public class Il2CppScheduleOne.Persistence.Datas.BusinessData : PropertyData
{
    Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppScheduleOne.Persistence.Datas.LaunderOperationData> LaunderingOperations { public get; public set; }
    public .ctor(System.String propertyCode, System.Boolean isOwned, Il2CppStructArray<System.Boolean> switchStates, Il2CppStructArray<System.Boolean> toggleableStates, Il2CppReferenceArray<DynamicSaveData> employees, Il2CppReferenceArray<DynamicSaveData> objects, Il2CppReferenceArray<LaunderOperationData> launderingOperations)
}

public class Il2CppScheduleOne.Persistence.Datas.ManorData : PropertyData
    Il2CppScheduleOne.Property.Manor+EManorState ManorState { public get; public set; }
    System.Int32   DaysSinceStateChange { public get; public set; }
    System.Boolean TunnelDug            { public get; public set; }
    public .ctor(System.String propertyCode, System.Boolean isOwned, Il2CppStructArray<System.Boolean> switchStates, Il2CppStructArray<System.Boolean> toggleableStates, Il2CppReferenceArray<DynamicSaveData> employees, Il2CppReferenceArray<DynamicSaveData> objects, Il2CppScheduleOne.Property.Manor+EManorState state, System.Int32 daysSinceStateChange, System.Boolean tunnelDug)

public class Il2CppScheduleOne.Persistence.Datas.WorldStorageEntitiesData : SaveData
    Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppScheduleOne.Persistence.Datas.WorldStorageEntityData> Entities { public get; public set; }

public class Il2CppScheduleOne.Persistence.Datas.WorldStorageEntityData : SaveData
    System.String GUID { public get; public set; }
    Il2CppScheduleOne.Persistence.Datas.ItemSet Contents { public get; public set; }
    Il2CppScheduleOne.GameTime.GameDateTime LastContentChangeTime { public get; public set; }
    public .ctor(Il2CppSystem.Guid guid, Il2CppScheduleOne.Persistence.Datas.ItemSet contents, Il2CppScheduleOne.GameTime.GameDateTime lastContentChangeTime)

public class Il2CppScheduleOne.Persistence.Datas.PlaceableStorageData : GridItemData
    Il2CppScheduleOne.Persistence.Datas.ItemSet Contents { public get; public set; }
    public .ctor(Il2CppSystem.Guid guid, Il2CppScheduleOne.ItemFramework.ItemInstance item, System.Int32 loadOrder, Il2CppScheduleOne.Tiles.Grid grid, UnityEngine.Vector2 originCoordinate, System.Int32 rotation, Il2CppScheduleOne.Persistence.Datas.ItemSet contents)

public class Il2CppScheduleOne.Persistence.Datas.StorageSurfaceItemData : SurfaceItemData
    Il2CppScheduleOne.Persistence.Datas.ItemSet Contents { public get; public set; }
    public .ctor(Il2CppSystem.Guid guid, Il2CppScheduleOne.ItemFramework.ItemInstance item, System.Int32 loadOrder, System.String parentSurfaceGUID, UnityEngine.Vector3 pos, UnityEngine.Quaternion rot, Il2CppScheduleOne.Persistence.Datas.ItemSet contents)

public class Il2CppScheduleOne.Persistence.Datas.ItemSet : Il2CppSystem.Object
    Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray Items { public get; public set; }   // one JSON string per slot
    Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppScheduleOne.ItemFramework.SlotFilter> SlotFilters { public get; public set; }
    public .ctor(Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Persistence.Datas.ItemData> items)
    public .ctor(Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemSlot> itemSlots)
    public .ctor(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppScheduleOne.ItemFramework.ItemSlot> itemSlots)
    public System.String GetJSON()
    public System.Void LoadTo(Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemSlot> slots)
    public System.Void LoadTo(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppScheduleOne.ItemFramework.ItemSlot> slots)
    public System.Void LoadTo(Il2CppScheduleOne.ItemFramework.ItemSlot slot, System.Int32 index = 0)
    public static System.Boolean TryDeserialize(System.String json, out Il2CppScheduleOne.Persistence.Datas.DeserializedItemSet& itemSet)
    public static System.Boolean TryDeserialize(Il2CppScheduleOne.Persistence.Datas.ItemSet set, out Il2CppScheduleOne.Persistence.Datas.DeserializedItemSet& itemSet)

public class Il2CppScheduleOne.Persistence.Datas.ItemData : SaveData
    System.String ID { public get; public set; }   System.Int32 Quantity { public get; public set; }
// subclasses: CashData, IntegerItemData, QualityItemData, ProductItemData, WeedData, MethData,
//             CocaineData, ShroomData, TrashItemData, WateringCanData …

public class Il2CppScheduleOne.Persistence.Datas.AdvancedTransitRouteData : Il2CppSystem.Object
    System.String SourceGUID      { public get; public set; }
    System.String DestinationGUID { public get; public set; }
    Il2CppScheduleOne.Management.ManagementItemFilter+EMode FilterMode { public get; public set; }
    Il2CppSystem.Collections.Generic.List<System.String> FilterItemIDs { public get; public set; }
    public .ctor(System.String sourceGUID, System.String destinationGUID, Il2CppScheduleOne.Management.ManagementItemFilter+EMode filtermode, Il2CppSystem.Collections.Generic.List<System.String> filterGUIDs)
    public .ctor()

public class Il2CppScheduleOne.Persistence.Datas.RouteListData : Il2CppSystem.Object
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Persistence.Datas.AdvancedTransitRouteData> Routes { public get; public set; }

public class Il2CppScheduleOne.Persistence.Datas.ObjectFieldData : Il2CppSystem.Object
    System.String ObjectGUID { public get; public set; }

public class Il2CppScheduleOne.Persistence.Datas.MoveItemData : Il2CppSystem.Object
    System.String TemplateItemJSON     { public get; public set; }
    System.Int32  GrabbedItemQuantity  { public get; public set; }
    System.String SourceGUID           { public get; public set; }
    System.String DestinationGUID      { public get; public set; }
    public .ctor(System.String templateItemJson, System.Int32 grabbedItemQuantity, Il2CppSystem.Guid sourceGUID, Il2CppSystem.Guid destinationGUID)

public class Il2CppScheduleOne.Persistence.Datas.DynamicSaveData : SaveData
    System.String BaseData { public get; public set; }
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Persistence.Datas.DynamicSaveData+AdditionalData> AdditionalDatas { public get; public set; }
    public System.Void AddData(System.String name, System.String contents)
    public System.Void AddData(System.String name, Il2CppScheduleOne.Persistence.Datas.SaveData data)
    public T ExtractBaseData<T>()
    public System.String GetData(System.String name)
    public T GetData<T>(System.String name, System.Boolean warn = True)
    public System.Boolean TryExtractBaseData<T>(out T& data)
    public System.Boolean TryGetData(System.String name, out System.String& data)
    public System.Boolean TryGetData<T>(System.String name, out T& data)
public class DynamicSaveData+AdditionalData { System.String Name; System.String Contents; }
```

Configuration DTOs (one per config, all `SaveData` or `RenamableConfigurationData`):
`BotanistConfigurationData`, `BrickPressConfigurationData`, `CauldronConfigurationData`,
`ChemistConfigurationData`, `ChemistryStationConfigurationData`, `CleanerConfigurationData`,
`DryingRackConfigurationData`, `LabOvenConfigurationData`, `MixingStationConfigurationData`,
`MushroomBedConfigurationData`, `PackagerConfigurationData`, `PackagingStationConfigurationData`,
`PotConfigurationData`, `SpawnStationConfigurationData`, `RenamableConfigurationData`.
Field DTOs: `ItemFieldData`, `NPCFieldData`, `NumberFieldData`, `ObjectFieldData`,
`ObjectListFieldData`, `QualityFieldData`, `RouteListData`, `StationRecipeFieldData`,
`StringFieldData`.

### 9.2 On-disk layout

Verified string literals in `literals-sorted.txt`: `Properties`, `Businesses`,
`WorldStorageEntities`, `Objects`, `Employees`. These are the `SaveFolderName` values of
`PropertyManager`, `BusinessManager`, `StorageManager` and the sub-folders written by
`Property.WriteData(parentFolderPath)`.

`Property` is an `ISaveable` that writes `savedObjectPaths` + `savedEmployeePaths` and exposes
`ShouldSaveUnderFolder` / `SaveFolderName` / `SaveFileName` as computed getters (their runtime
values are **UNVERIFIED** — `ShouldSaveUnderFolder` returning `true` and `SaveFolderName` being the
property's own name are *inferred* from the observed on-disk layout). Combined with
`ISaveable.WriteFolder` / `WriteSubfile` / `CompleteSave`, the layout is:

```
<SaveGame_N>/
├── Properties/
│   └── <PropertyName>/                 <-- Property.SaveFolderName
│       ├── Property.json               (PropertyData: PropertyCode, IsOwned, SwitchStates, ToggleableStates)
│       ├── Objects/                    (one folder/file per BuildableItem — DynamicSaveData)
│       │   └── <n>/  or <n>.json       (GridItemData / PlaceableStorageData / PotData / …)
│       └── Employees/                  (one per Employee — DynamicSaveData)
│           └── <n>.json                (EmployeeData / BotanistData / PackagerData / …)
├── Businesses/
│   └── <BusinessName>/                 <-- same shape + LaunderingOperations
│       ├── Business.json               (BusinessData)
│       ├── Objects/
│       └── Employees/
└── WorldStorageEntities.json           (WorldStorageEntitiesData -> WorldStorageEntityData[])
```

**The exact file names inside `Properties/<Name>/` and `Businesses/<Name>/` are UNVERIFIED** —
`SaveFolderName` and `SaveFileName` are computed properties whose returned literals are not
distinguishable in the dump. The folder names `Properties`, `Businesses`, `Objects`, `Employees`
and the file `WorldStorageEntities.json` are confirmed. Check a real save to pin the leaf names.

Loader chain (confirmed types):
`PropertiesLoader` → `PropertyLoader` (→ `BusinessLoader` for businesses) → `BuildableItemLoader`
subclasses (`GridItemLoader`, `PlaceableStorageEntityLoader`, `SurfaceItemLoader`,
`StorageSurfaceItemLoader`, `PotLoader`, `DryingRackLoader`, `MixingStationLoader`,
`PackagingStationLoader`, `LabOvenLoader`, `CauldronLoader`, `ChemistryStationLoader`,
`BrickPressLoader`, `MushroomBedLoader`, `SpawnStationLoader`, `TrashContainerLoader`,
`ToggleableItemLoader`, `ProceduralGridItemLoader`, `LabelledSurfaceItemLoader`,
`JukeboxLoader`, `AirConditionerLoader`, `SoilPourerLoader`, `GraffitiLoader`) and
`EmployeeLoader` subclasses (`BotanistLoader`, `ChemistLoader`, `PackagerLoader`, `CleanerLoader`,
plus `Legacy*Loader` variants for old saves).
`BusinessesLoader` → `BusinessLoader`. `StorageLoader` handles `WorldStorageEntities`.

```csharp
public class Il2CppScheduleOne.Persistence.Loaders.PropertyLoader : Loader
    public virtual System.Void Load(System.String mainPath)
    public virtual System.Void Load(Il2CppScheduleOne.Persistence.Datas.PropertyData propertyData, System.String propertDataString)   // sic
public class Il2CppScheduleOne.Persistence.Loaders.BusinessLoader : PropertyLoader
    public virtual System.Void Load(System.String mainPath)
public class Il2CppScheduleOne.Persistence.Loaders.PropertiesLoader : Loader
public class Il2CppScheduleOne.Persistence.Loaders.BusinessesLoader : Loader
public class Il2CppScheduleOne.Persistence.Loaders.StorageLoader : Loader
```

### 9.3 `ISaveable`

```csharp
public class Il2CppScheduleOne.Persistence.ISaveable : Il2CppObjectBase   // interface
{
    System.String SaveFolderName { public get; }
    System.String SaveFileName   { public get; }
    Il2CppScheduleOne.Persistence.Loaders.Loader Loader { public get; }
    System.Boolean ShouldSaveUnderFolder { public get; }
    Il2CppSystem.Collections.Generic.List<System.String> LocalExtraFiles   { public get; public set; }
    Il2CppSystem.Collections.Generic.List<System.String> LocalExtraFolders { public get; public set; }
    System.Boolean HasChanged { public get; public set; }

    public virtual System.Void CompleteSave(System.String parentFolderPath, System.Boolean writeDataFile)
    public virtual System.Void DeleteUnapprovedFiles(System.String parentFolderPath)
    public virtual System.String GetContainerFolder(System.String parentFolderPath)
    public virtual System.String GetLocalPath(out System.Boolean& isFolder)
    public virtual System.String GetSaveString()
    public virtual System.Void InitializeSaveable()
    public virtual System.String Save(System.String parentFolderPath)
    public virtual System.Boolean TryLoadFile(System.String parentPath, System.String fileName, out System.String& contents)
    public virtual System.Boolean TryLoadFile(System.String path, out System.String& contents, System.Boolean autoAddExtension = True)
    public virtual System.Void WriteBaseData(System.String parentFolderPath, System.String saveString)
    public virtual Il2CppSystem.Collections.Generic.List<System.String> WriteData(System.String parentFolderPath)
    public virtual System.String WriteFolder(System.String parentPath, System.String localPath_NoExtensions)
    public virtual System.String WriteSubfile(System.String parentPath, System.String localPath_NoExtensions, System.String contents)
}
```

---

## 10. Runtime flow

Server/client annotations are **inferred from FishNet `RpcWriter___Server_` vs
`RpcWriter___Observers_`/`RpcWriter___Target_` prefixes** unless stated otherwise. That mapping is
the standard FishNet codegen and is high confidence.

**Step 1 — buy a property.**
`Property.SetOwned()` *(any peer)* → `Property.SetOwned_Server()` *(ServerRpc)* → server body
`RpcLogic___SetOwned_Server_2166136261` sets `IsOwned` and moves the instance from
`Property.UnownedProperties` to `Property.OwnedProperties` *(inferred)* →
`Property.ReceiveOwned_Networked()` *(ObserversRpc)* → every peer runs
`RpcLogic___ReceiveOwned_Networked_2166136261` → virtual `Property.RecieveOwned()` fires
(`Manor` and `Business` override it) → `onThisPropertyAcquired` + static `onPropertyAcquired`
invoke. `Business.RecieveOwned()` additionally moves it into `Business.OwnedBusinesses`.
`PropertyDoorController.Unlock()` opens the door.

**Step 2 — place a storage chest.**
Player equips a `BuildableItemDefinition` → `BuildManager.StartBuilding(itemInstance)` *(local)* →
`BuildUpdate_Grid` previews → `BuildManager.CreateGridItem(item, grid, originCoordinate, rotation, guid, onBeforeSpawn)`
*(server-side spawn; `BuildManager` is a `NetworkSingleton`)* → the spawned
`PlaceableStorageEntity.InitializeGridItem(...)` runs, which chains
`GridItem.InitializeGridItem_Server(...)` *(ServerRpc)* /
`GridItem.InitializeGridItem_Client(...)` *(Observers + Target RPC)* →
`BuildableItem.InitializeBuildableItem(instance, GUID, parentPropertyCode)` resolves the owning
`Property` by code and calls `Property.AddBuildableItem(item)` → because it is `IConfigurable`,
`Property.AddConfigurable(configurable)` also runs → `CreateWorldspaceUI()` spawns a
`StorageUIElement`.

**Step 3 — configure a route (vanilla, via the clipboard).**
Player equips `ManagementClipboard_Equippable` →
`ManagementInterface.Open(configurables, _equippedClipboard)` →
`InitializeConfigPanel()` → `GetConfigPanelPrefab(EConfigurableType)` →
`ConfigPanel.Bind(configs, screen)` → `BindInternal(configs)`.
For a station: `ObjectFieldUI.Clicked()` → `ObjectSelector.Open(...)` → player clicks a target →
`ObjectFieldUI.ObjectSelected(obj)` → `ObjectField.SetObject(obj, network: true)` →
`ConfigurationReplicator.SendObjectField(fieldIndex, networkObject)` *(ServerRpc)* → server →
`ReceiveObjectField` *(ObserversRpc)* → each peer's `ObjectField.onObjectChanged` fires →
`XConfiguration.DestinationChanged(item)` → builds/updates
`XConfiguration.DestinationRoute` (a `TransitRoute`) and enables its `TransitLineVisuals`.
For a Packager: `RouteListFieldUI.AddClicked()` → `RouteEntryUI.SourceClicked()` /
`DestinationClicked()` → `TransitEntitySelector.Open(...)` → `RouteEntryUI.ObjectsSelected(objs)` →
`AdvancedTransitRoute.SetSource/SetDestination` → `RouteListField.SetList(list, network: true)` /
`Replicate()` → `ConfigurationReplicator.SendRouteListField(fieldIndex, AdvancedTransitRouteData[])`.

**Step 4 — items actually move.**
`Employee.MoveItemBehaviour` (an `Il2CppScheduleOne.NPCs.Behaviour.MoveItemBehaviour`) is the only
thing in the game that walks a `TransitRoute`:

```csharp
public class Il2CppScheduleOne.NPCs.Behaviour.MoveItemBehaviour : Il2CppScheduleOne.NPCs.Behaviour.Behaviour
{
    Il2CppScheduleOne.Management.TransitRoute assignedRoute { public get; public set; }
    Il2CppScheduleOne.ItemFramework.ItemInstance itemToRetrieveTemplate { public get; public set; }
    System.Int32 grabbedAmount  { public get; public set; }
    System.Int32 maxMoveAmount  { public get; public set; }
    Il2CppScheduleOne.NPCs.Behaviour.MoveItemBehaviour+EState currentState { public get; public set; }
    System.Boolean skipPickup   { public get; public set; }
    System.Boolean Initialized  { public get; public set; }
    UnityEngine.Coroutine walkToSourceRoutine / grabRoutine / walkToDestinationRoutine / placingRoutine

    public System.Void Initialize(Il2CppScheduleOne.Management.TransitRoute route, Il2CppScheduleOne.ItemFramework.ItemInstance _itemToRetrieveTemplate, System.Int32 _maxMoveAmount = -1, System.Boolean _skipPickup = False)
    public virtual System.Void Activate()
    public virtual System.Void Deactivate()
    public virtual System.Void Disable()
    public virtual System.Void Pause()
    public virtual System.Void Resume()
    public System.Void Resume(Il2CppScheduleOne.Management.TransitRoute route, Il2CppScheduleOne.ItemFramework.ItemInstance _itemToRetrieveTemplate, System.Int32 _maxMoveAmount = -1)
    public System.Void StartTransit()
    public System.Void EndTransit()
    public System.Void StopCurrentActivity()
    public System.Void WalkToSource()          public System.Boolean IsAtSource()
    public System.Void GrabItem()              public System.Void TakeItem()
    public System.Void WalkToDestination()     public System.Boolean IsAtDestination()
    public System.Void PlaceItem()
    public System.Int32 GetAmountToGrab()
    public System.Boolean CanGetToSource(Il2CppScheduleOne.Management.TransitRoute route)
    public System.Boolean CanGetToDestination(Il2CppScheduleOne.Management.TransitRoute route)
    public UnityEngine.Transform GetSourceAccessPoint(Il2CppScheduleOne.Management.TransitRoute route)
    public UnityEngine.Transform GetDestinationAccessPoint(Il2CppScheduleOne.Management.TransitRoute route)
    public System.Boolean IsDestinationValid(Il2CppScheduleOne.Management.TransitRoute route, Il2CppScheduleOne.ItemFramework.ItemInstance item)
    public System.Boolean IsDestinationValid(Il2CppScheduleOne.Management.TransitRoute route, Il2CppScheduleOne.ItemFramework.ItemInstance item, out System.String& invalidReason)
    public System.Boolean IsNpcInventoryItemValid(Il2CppScheduleOne.ItemFramework.ItemInstance item)
    public System.Boolean IsTransitRouteValid(Il2CppScheduleOne.Management.TransitRoute route, System.String itemID)
    public System.Boolean IsTransitRouteValid(Il2CppScheduleOne.Management.TransitRoute route, System.String itemID, out System.String& invalidReason)
    public System.Boolean IsTransitRouteValid(Il2CppScheduleOne.Management.TransitRoute route, Il2CppScheduleOne.ItemFramework.ItemInstance templateItem, out System.String& invalidReason)
    public Il2CppScheduleOne.Persistence.Datas.MoveItemData GetSaveData()
    public System.Void Load(Il2CppScheduleOne.Persistence.Datas.MoveItemData moveItemData)
    public virtual System.Void OnActiveTick()
}
public enum MoveItemBehaviour+EState : System.Int32
{ Idle = 0, WalkingToSource = 1, Grabbing = 2, WalkingToDestination = 3, Placing = 4 }
```

State machine: `Initialize(route, template, maxMoveAmount, skipPickup)` → `Activate()` →
`StartTransit()` → `WalkToSource()` (`GetSourceAccessPoint` picks from
`Source.AccessPoints`) → `IsAtSource()` → `GrabItem()`/`TakeItem()` (pulls from
`Source.GetOutputItemContainer` / `GetFirstSlotContainingTemplateItem`, amount =
`GetAmountToGrab()` clamped by `Destination.GetInputCapacityForItem`) → `WalkToDestination()` →
`IsAtDestination()` → `PlaceItem()` (`Destination.InsertItemIntoInput(item, npc)`) →
`EndTransit()`. Employees run this **on the server only** (NPC behaviours in this game are
server-authoritative) *(inferred — `Behaviour` base was not dumped here)*.

`Employee` also has `public System.Void SetDestination(Il2CppScheduleOne.Management.ITransitEntity transitEntity, System.Boolean teleportIfFail = True)`
— a direct "walk this NPC to that transit entity" call.

---

## 11. Hooks & extension points

### 11.1 Enumerate owned properties / businesses

No patch needed — read the statics:

```csharp
Il2CppScheduleOne.Property.Property.Properties          // all
Il2CppScheduleOne.Property.Property.OwnedProperties
Il2CppScheduleOne.Property.Property.UnownedProperties
Il2CppScheduleOne.Property.Business.Businesses
Il2CppScheduleOne.Property.Business.OwnedBusinesses
Il2CppScheduleOne.Property.Business.UnownedBusinesses
```

React to a purchase:
* subscribe to `Property.onPropertyAcquired` (a `Property+PropertyChange` multicast delegate with an
  `op_Implicit(System.Action<Property>)` — so you can assign a plain `Action<Property>`);
* or subscribe to the per-instance `UnityEvent onThisPropertyAcquired`;
* or Harmony-postfix `Il2CppScheduleOne.Property.Property.RecieveOwned` (virtual — patch the
  subclass too, or patch `RpcLogic___ReceiveOwned_Networked_2166136261` to catch the network path).

Lookup by code: `PropertyManager.GetProperty(string code)` (searches properties then businesses).
Spatial: `PropertyManager.GetNearestProperty(point, includeOwned, includeUnowned, includeBusinesses)`.

### 11.2 Enumerate storages

```csharp
Il2CppScheduleOne.Storage.WorldStorageEntity.All                                     // static: all world storages
property.GetBuildablesOfType<Il2CppScheduleOne.ObjectScripts.PlaceableStorageEntity>()  // per property
property.BuildableItems                                                              // everything placed
property.Configurables                                                               // everything clipboard-able
```
Track new ones: subscribe to `Property.onBuildableItemAdded` /
`Property.onBuildableItemRemoved` (`Il2CppSystem.Action<BuildableItem>`), or postfix
`Il2CppScheduleOne.Property.Property.AddBuildableItem`.

### 11.3 Read storage contents

```csharp
var se = placeable.StorageEntity;
se.ItemSlots                       // List<ItemSlot>, index == ItemSlot.SlotIndex
se.ItemCount                       // non-empty slot count (verify semantics)
se.GetAllItems()                   // List<ItemInstance>
se.GetContentsDictionary()         // Dictionary<StorableItemInstance,int>
se.HowManyCanFit(item)             // int
se.CanItemFit(item, quantity = 1)  // bool
// via ITransitEntity instead:
var te = placeable.Cast<Il2CppScheduleOne.Management.ITransitEntity>();
te.GetFirstSlotContainingItem("ogkush", ITransitEntity.ESlotType.Output);   // "ogkush" is a verified item-ID literal
te.GetOutputCapacityForItem(item);
```

### 11.4 Write storage contents

Server-side, cheapest path:
```csharp
storageEntity.InsertItem(itemInstance, network: true);   // stacks into the first fitting slot
storageEntity.ClearContents();
storageEntity.LoadFromItemSet(itemInstanceArray);
```
Slot-precise path (works from a client because the setters are ServerRpcs):
```csharp
storageEntity.SetStoredInstance(conn: null, itemSlotIndex: i, instance: itemInstance);
storageEntity.SetItemSlotQuantity(itemSlotIndex: i, quantity: n);
storageEntity.SetSlotFilter(conn: null, itemSlotIndex: i, filter: slotFilter);
storageEntity.SetSlotLocked(conn: null, itemSlotIndex: i, locked: true, lockOwner: myNetObj, lockReason: "reserved");
```
Slot-object path (routes through the owner's RPC because `_internal` defaults to `false`):
```csharp
slot.SetStoredItem(itemInstance);          // _internal = false
slot.AddItem(itemInstance);                // _internal = false
slot.ChangeQuantity(+5);
slot.SetQuantity(12);
slot.ClearStoredInstance();
Il2CppScheduleOne.ItemFramework.ItemSlot.TryInsertItemIntoSet(storageEntity.ItemSlots, itemInstance);
```
Then `storageEntity.ContentsChanged()` fires `onContentsChanged` and (on
`WorldStorageEntity`) stamps `LastContentChangeTime` + `HasChanged`.

Harmony targets for observing writes: `StorageEntity.RpcLogic___SetStoredInstance_2652194801`,
`StorageEntity.RpcLogic___SetItemSlotQuantity_Internal_1692629761`,
`StorageEntity.ContentsChanged`, `ItemSlot.SetStoredItem`, `ItemSlot.ChangeQuantity`.

### 11.5 Create a transit route from code

```csharp
using Il2CppScheduleOne.Management;

// 1. get two ITransitEntity views (pointer recast, NOT a C# cast)
var srcTe = srcPlaceableStorage.Cast<ITransitEntity>();
var dstTe = dstStation.Cast<ITransitEntity>();

// 2. simple route
var route = new TransitRoute(srcTe, dstTe);
route.SetVisualsActive(true);       // optional line renderer
route.ValidateEntities();           // clears the endpoints if either IsDestroyed
bool ok = route.AreEntitiesNonNull();

// 3. filtered route
var adv = new AdvancedTransitRoute(srcTe, dstTe);
adv.Filter = new ManagementItemFilter(ManagementItemFilter.EMode.Whitelist);
adv.Filter.AddItem(someItemDefinition);
var next = adv.GetItemReadyToMove();               // null when nothing matches

// 4. persist / replicate: routes travel as data, never as objects
Il2CppScheduleOne.Persistence.Datas.AdvancedTransitRouteData data = adv.GetData();
var rebuilt = new AdvancedTransitRoute(data);
```

To attach a route to the vanilla employee system, put it in a `PackagerConfiguration.Routes`
(`RouteListField`) and call `SetList(list, network: true)` — that pushes
`ConfigurationReplicator.SendRouteListField` and makes the Packager's `MoveItemBehaviour` use it.

### 11.6 Drive a courier yourself

```csharp
Il2CppScheduleOne.Employees.Employee emp = /* … */;
var mib = emp.MoveItemBehaviour;
mib.Initialize(route, templateItem, _maxMoveAmount: -1, _skipPickup: false);
mib.Activate();
// per-tick guards:
mib.IsTransitRouteValid(route, templateItem, out string why);
mib.CanGetToSource(route);  mib.CanGetToDestination(route);
mib.IsDestinationValid(route, item, out string why2);
// abort:
mib.StopCurrentActivity();  mib.EndTransit();  mib.Deactivate();
```
Harmony targets: `MoveItemBehaviour.GetAmountToGrab`, `MoveItemBehaviour.IsTransitRouteValid`,
`MoveItemBehaviour.GetSourceAccessPoint`, `MoveItemBehaviour.GetDestinationAccessPoint`,
`MoveItemBehaviour.PlaceItem`, `MoveItemBehaviour.OnActiveTick`.

### 11.7 FishNet RPC pattern reminder

Every networked call in this codebase is a **triple** (sometimes a quad):

| Piece | Meaning |
|---|---|
| `public void X(...)` | the entry point you call |
| `RpcWriter___Server_X_<hash>` | serialises + sends client → server (present ⇒ `X` is a `ServerRpc`) |
| `RpcWriter___Observers_X_<hash>` / `RpcWriter___Target_X_<hash>` | server → all / one client (present ⇒ `X` is an `ObserversRpc` / `TargetRpc`) |
| `RpcReader___Server_X_<hash>` / `RpcReader___Observers_X_<hash>` / `RpcReader___Target_X_<hash>` | deserialisers on the receiving side |
| `RpcLogic___X_<hash>` | **the real body — patch this, not `X`** |

Patching `X` sees the local call only. Patching `RpcLogic___X_<hash>` sees the authoritative
execution on whichever peer actually runs it. The `<hash>` is stable for a given signature but
**changes if the signature changes between game versions** — resolve it by prefix match, not by
hard-coded literal.

---

## 12. Moving items between two arbitrary places from a mod — the verified recipe

Goal: move `n` units of an item from storage/station **A** to storage/station **B**, no NPC, no UI.

### Recipe A — the `ITransitEntity` route (works for every implementor, no NPC required)

```csharp
using Il2CppScheduleOne.Management;
using Il2CppScheduleOne.ItemFramework;

// 0. Server only. Bail out on clients — the slot mutators below are ServerRpcs and the
//    *_Internal broadcasts will not fire if you call them from a client.
//    (Check InstanceFinder.IsServer, or NetworkBehaviour.IsServer on any of the entities.)

// 1. Recast both endpoints. This is a pointer reinterpretation — `is`/`as` will NOT work.
ITransitEntity src = sourceComponent.TryCast<ITransitEntity>();
ITransitEntity dst = destComponent.TryCast<ITransitEntity>();
if (src == null || dst == null) return;
if (src.IsDestroyed || dst.IsDestroyed) return;
if (!dst.IsAcceptingItems) return;

// 2. Find the item on the source's OUTPUT side.
ItemSlot from = src.GetFirstSlotContainingTemplateItem(templateItem, ITransitEntity.ESlotType.Output);
//   or by ID:
//   ItemSlot from = src.GetFirstSlotContainingItem("ogkush", ITransitEntity.ESlotType.Output);
if (from == null || from.ItemInstance == null) return;
if (from.IsLocked || from.IsRemovalLocked) return;

// 3. Work out how much can actually move.
int available = src.GetOutputCapacityForItem(from.ItemInstance, asker: null);
int space     = dst.GetInputCapacityForItem(from.ItemInstance, asker: null, checkPlayerFilters: true);
int amount    = Math.Min(Math.Min(available, space), from.Quantity);
if (amount <= 0) return;

// 4. Reserve the destination so nothing else claims it while you work.
var reserved = dst.ReserveInputSlotsForItem(from.ItemInstance, myNetworkObject);
try
{
    // 5. Split off the payload and remove it from the source.
    ItemInstance payload = from.ItemInstance.GetCopy(overrideQuantity: amount);
    from.ChangeQuantity(-amount);                      // _internal = false -> replicates

    // 6. Push it in.
    dst.InsertItemIntoInput(payload, inserter: null);  // NPC param may stay null
}
finally
{
    // 7. ALWAYS release. A leaked lock permanently jams the destination.
    dst.RemoveSlotLocks(myNetworkObject);
}
```

### Recipe B — the `StorageEntity` shortcut (storage → storage only)

```csharp
Il2CppScheduleOne.Storage.StorageEntity a = srcPlaceable.StorageEntity;
Il2CppScheduleOne.Storage.StorageEntity b = dstPlaceable.StorageEntity;

ItemSlot from = a.ItemSlots[i];
ItemInstance item = from.ItemInstance;
int amount = Math.Min(from.Quantity, b.HowManyCanFit(item));
if (amount <= 0) return;

ItemInstance payload = item.GetCopy(amount);
from.ChangeQuantity(-amount);
b.InsertItem(payload, network: true);       // stacks into the first fitting slot
a.ContentsChanged();
b.ContentsChanged();
```

### Caveats — all of these will bite you

1. **Server authority.** `SetStoredInstance`, `SetItemSlotQuantity`, `SetSlotFilter`,
   `SetSlotLocked` are `[ServerRpc]`s; their `*_Internal` twins are the server→client broadcasts.
   Doing the work on a client and calling `*_Internal` yourself changes nothing on other peers.
   *(Inferred from the RpcWriter prefixes — verify once with two clients.)*
2. **Slot filters are two-layered.** `ItemSlot.HardFilters` (code-defined, e.g. a `DryingRack`
   only accepts dryables) and `ItemSlot.PlayerFilter` (a `SlotFilter` the player set in the UI).
   `GetInputCapacityForItem(..., checkPlayerFilters: true)` honours both; passing `false` bypasses
   the player filter and will let you shove a mixer into a product slot.
3. **Stack limits are per-`ItemDefinition`.** `BaseItemDefinition.StackLimit` (default
   `BaseItemDefinition.DefaultStackLimit`). `ItemSlot.IsAtCapacity` and
   `ItemSlot.GetCapacityForItem(item, checkPlayerFilters)` already account for it. Also watch
   `ItemSlot.SiblingSet` — sibling slots share a limit.
4. **Stacking requires `CanStackWith`.** `ItemInstance.CanStackWith(other, checkQuantities = true)`
   — quality-bearing instances (`QualityItemInstance`), cash (`CashInstance.Balance`), water
   (`WaterContainerInstance.CurrentFillAmount`) and integer items only stack when the extra data
   matches. **Always `GetCopy(overrideQuantity)` rather than mutating the source instance**, or
   you will alias one `ItemInstance` into two slots.
5. **Locks.** `ReserveInputSlotsForItem(item, locker)` → always paired with
   `RemoveSlotLocks(locker)`. `ItemSlot.IsAddLocked` / `IsRemovalLocked` / `IsLocked` /
   `ActiveLock.LockReason` tell you why something is refusing.
6. **`IsAcceptingItems` is a real gate**, not decoration — stations set it false mid-operation.
7. **UI refresh.** Slot mutations fire `ItemSlot.onItemDataChanged` / `onItemInstanceChanged`, and
   `StorageEntity.ContentsChanged()` fires `onContentsChanged`; the **inventory UI** picks these up
   automatically. The **shelf visuals** do not: `StorageVisualizer` needs
   `QueueRefresh()` or `RefreshVisuals()`. `StorageVisualizer.BlockRefreshes` will silently swallow
   both. `PlaceableStorageEntity.UpdateNameLabels()` if you changed the name.
8. **Persistence.** For a `WorldStorageEntity` set `HasChanged = true` (or let
   `ContentsChanged()` do it) or the change will not be written on the next save.
9. **Never touch `field_Private_Boolean_0` / `Method_Protected_Virtual_Void_0`** — they are
   positional Il2CppInterop names and will point at a different member after any game patch.

---

## 13. Open questions / unverified

1. **`SurfaceStorageEntity`'s `ITransitEntity` status.** It declares `Selectable` and
   `IsAcceptingItems` plus the whole `IUsable` surface, but not `InputSlots`, `OutputSlots`,
   `LinkOrigin` or `AccessPoints`, and no base supplies them. Either the interface list is
   different from what I reconstructed, or those two properties belong to something I have not
   found. **Verify at runtime with `TryCast<ITransitEntity>()`.**
2. **Which `ITransitEntity` members are abstract vs default-implemented.** I proved
   `GetOutputItemContainer` has a body on the interface (its closure class lives there). The other
   ten `virtual` methods are *probably* also DIMs — none of the concrete implementors re-declare
   them — but I could not read the IL to confirm each one.
3. **Base interfaces of `ITransitEntity`.** `Name`, `IsDestroyed`, `GUID`, `ShowOutline`,
   `HideOutline` are part of the contract (proved via `LoadingDock`) but I could not tell whether
   they are declared on `ITransitEntity` itself or inherited from `Il2CppScheduleOne.IGUIDRegisterable`
   and some outline/selectable interface. Il2CppInterop flattens this and the metadata string table
   de-duplicates it away.
4. **Property codes beyond `barn`, `laundromat`, `manor`** (and a bare `rv` literal). They are
   Unity-serialized prefab fields, not IL2CPP literals. Enumerate `Property.Properties` at runtime.
5. **Exact save file leaf names** inside `Properties/<Name>/` and `Businesses/<Name>/`.
   `SaveFolderName` / `SaveFileName` are computed properties; only the container names
   `Properties`, `Businesses`, `Objects`, `Employees` and `WorldStorageEntities` are literal-verified.
6. **Property → map region.** `Il2CppScheduleOne.Property.Property` has no region member of any
   kind. If region matters, it must come from `Il2CppScheduleOne.Map.POI` or a spatial lookup —
   not documented here.
7. **Server/client annotations** throughout §10–§12 are inferred from the FishNet
   `RpcWriter___Server_` / `_Observers_` / `_Target_` naming. High confidence, but not read from IL.
8. **`Il2CppScheduleOne.DevUtilities.Singleton\`1` / `NetworkSingleton\`1` accessor names.** I never
   dumped them, so `Singleton<PropertyManager>.Instance` is an assumption.
9. **`TransitRoute.Update()` semantics.** `TransitRoute` is not a `MonoBehaviour`, so `Update()` is
   a manual call. I assume it re-reads `LinkOrigin` positions into `TransitLineVisuals`, but did not
   confirm. If you build routes from code and want the line to track moving endpoints, you must call
   it yourself.
10. **`StorageEntity.ItemCount` semantics** — non-empty slot count vs total item quantity. Not
    determinable from the signature.
11. **`ObjectScripts` types I did not expand** (listed in §7.3): `Beaker`, `BrickPressContainer`,
    `BrickPressHandle`, `BunsenBurner`, `CashCounter`, `CauldronDisplayTub`,
    `ChemistryCookOperation`, `DryingOperation`, `FloorRack`, `GrowLight`, `Jukebox`,
    `JukeboxInterface`, `LabOvenButton`, `LabOvenDoor`, `LabOvenHammer`, `LabOvenWireTray`,
    `LabStand`, `MixOperation`, `OvenCookOperation`, `Recycler`, `SoilPourer`, `Sprinkler`,
    `StirringRod`, `Toilet`, `VMSBoard`, `VendingMachine`. I verified none of them declare the
    `ITransitEntity` abstract members; their internals are out of scope for this document.
12. **`Il2CppScheduleOne.NPCs.Behaviour.Behaviour`** (the base of `MoveItemBehaviour`) was not
    dumped, so the exact activation/tick contract and its server-only-ness is inferred.

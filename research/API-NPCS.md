# API-NPCS.md — Schedule I NPC, avatar, behaviour, schedule and dialogue systems

Source of truth: the IL2CPP dumps under `research/raw/` for the installed build
(`Assembly-CSharp.dll` from MelonLoader `Il2CppAssemblies`). Every type name, member name and
signature below is copied verbatim from those dumps.

**Reading conventions used in this document**

- Namespaces are reported with the `Il2Cpp` prefix exactly as Il2CppInterop emits them
  (`Il2CppScheduleOne.NPCs`). The original CLR name has no prefix (`ScheduleOne.NPCs`) and is
  what you need for IL2CPP-runtime type lookups and save keys. Both are noted where it matters.
- Il2CppInterop exposes IL2CPP **instance and static fields as C# properties**. So
  `static System.Int32 PanicDuration { public get; public set; }` in a dump is a *field* in the
  game. `_Foo_k__BackingField` and `Foo` are the same storage — **always use `Foo`**.
- `field_Private_Boolean_0`, `Method_Protected_Virtual_Void_0`, `Method_Private_IEnumerator_PDM_0`
  are Il2CppInterop fallback names for members whose real name is not a legal C# identifier.
  **These are index-based and WILL renumber between game versions.** Never bind to them by name in
  shipping code without a version guard.
- `RpcLogic___*`, `RpcReader___*`, `RpcWriter___*` are FishNet codegen. The *public* method
  (`EnterVehicle`, `SetRelationship`, …) is the one you call; it routes to the right side. Methods
  suffixed `_Server` are `[ServerRpc]`, `_Client`/`_Networked` are `[ObserversRpc]`/`[TargetRpc]`.
- `(inferred …)` marks a conclusion drawn from names/adjacency rather than read off a dump.
  **UNVERIFIED** marks something I could not confirm at all. Section 16 collects every one.

**Relevance tags used inline:** `[HD]` Hireable Drivers, `[PI]` Police Improvements,
`[SC]` Special Customers.

---

## 1. Namespace map

| Namespace (Il2Cpp form) | Types | Role |
|---|---|---|
| `Il2CppScheduleOne.NPCs` | 19 | `NPC` base, `NPCManager`, `NPCMovement`, `NPCInventory`, `NPCHealth`, `NPCAwareness`, `NPCAnimation`, `NPCScheduleManager`, `NPCSpeedController`, `NPCPathCache`, plus 8 one-off scene characters |
| `Il2CppScheduleOne.NPCs.Framework` | 32 | **The NPC authoring model** — `NPCData`, `BaseNPCDataObject`, and the 12 `ValueOrReference` sub-blocks (`BasicInfo`, `Appearance`, `Health`, `Movement`, `Interaction`, `Relationship`, `Messaging`, `Dialogue`, `Voice`, `Inventory`, `Behaviour`, `WeatherBehaviour`) with a `*Preset` ScriptableObject each |
| `Il2CppScheduleOne.NPCs.Behaviour` | 56 | Behaviour state machine: `NPCBehaviour` (the controller) + `Behaviour` (base) + 41 concrete direct subclasses + 8 `GrowContainerBehaviour` subclasses + routes/groups |
| `Il2CppScheduleOne.NPCs.Schedules` | ~14 | `NPCAction` → `NPCEvent` / `NPCSignal` tree, `ConversationLocation` |
| `Il2CppScheduleOne.NPCs.Relation` | 5 | `NPCRelationData`, `ERelationshipCategory`, `RelationshipCategory`, `NPCUnlockTracker`, `NPCUnlockedVariable` |
| `Il2CppScheduleOne.NPCs.Responses` | 2+ | `NPCResponses` base + `NPCResponses_Civilian` (`NPCResponses_Employee`, `NPCResponses_Police`, `NPCResponses_CartelGoon` live in their own namespaces) |
| `Il2CppScheduleOne.NPCs.Actions` | 1 | `NPCActions` — small imperative facade (`Cower`, `FacePlayer`, `CallPolice_Networked`) |
| `Il2CppScheduleOne.NPCs.Other` | 6 | `NPCDiscreteAction` base + `UseUmbrella`, `HoldItem`, `DrinkItem`, `SmokeCigarette`, `SprayPaint` |
| `Il2CppScheduleOne.NPCs.CharacterClasses` | 79 | One class per named town character |
| `Il2CppScheduleOne.AvatarFramework` | ~15 | `Avatar`, `AvatarSettings`, `AvatarLayer`/`FaceLayer`, `Accessory`/`Hair`/`PoliceBelt`, `Eye`/`EyeController`, `Eyebrow`/`EyebrowController`, `AvatarEffects`, `MugshotGenerator` |
| `Il2CppScheduleOne.AvatarFramework.Customization` | ~14 | `BasicAvatarSettings` (**the flat, moddable appearance record**), `CharacterCreator`, `CustomizationManager`, AC* UI replicators |
| `Il2CppScheduleOne.AvatarFramework.Equipping` | 7 | `AvatarEquippable` → `AvatarWeapon` → `AvatarMeleeWeapon` / `AvatarRangedWeapon` → `AvatarGun`, `Taser`, `FlashlightAvatarEquippable` |
| `Il2CppScheduleOne.AvatarFramework.Animation` | ~10 | `AvatarAnimation`, `AvatarLookController`, `AvatarSeat`, `AvatarSeatSet`, `BoneTransform` |
| `Il2CppScheduleOne.Clothing` | 6 | `ClothingDefinition`, `ClothingInstance`, `ClothingUtility`, `EClothingSlot`, `EClothingColor`, `EClothingApplicationType` |
| `Il2CppScheduleOne.Dialogue` | ~30 | `DialogueHandler`, `DialogueController`, `DialogueContainer`, node/choice/link data, `DialogueDatabase`/`DialogueModule` |
| `Il2CppScheduleOne.Cartel` | ~20 | **`GoonPool` + `CartelGoon` + `CartelGoonAppearance` — the only runtime-procedural NPC in the game, and therefore the template for custom NPC spawning** |
| `Il2CppScheduleOne.Economy` | ~30 | `Customer`, `Dealer`, `Supplier` — NPC *components*, not NPC subclasses (except `Dealer`/`Supplier`, see §4) |
| `Il2CppScheduleOne.Employees` | ~20 | `Employee : NPC` + `Botanist`, `Chemist`, `Cleaner`, `Packager` |

---

## 2. `Il2CppScheduleOne.NPCs.NPC` — the base class

`public class Il2CppScheduleOne.NPCs.NPC : Il2CppFishNet.Object.NetworkBehaviour`

111 properties, 218 methods, 360 interop-internal fields. It implements (evidenced by the
explicit interface members in the dump) at minimum:
`ScheduleOne.Combat.ICombatTargetable`, `ScheduleOne.Combat.IDamageable`,
`ScheduleOne.Vision.ISightable`, `ScheduleOne.Core.Weather.IWeatherEntity`,
`ScheduleOne.Core.Equipping.Framework.IThirdPersonReferencesProvider`, and a saveable interface
(`GetSaveString`/`WriteData`/`InitializeSaveable`/`Loader`/`SaveFolderName`).

### 2.1 Static tuning fields

```csharp
static System.Int32   PanicDuration        { public get; public set; }
static System.Int32   HeadLightStartTime   { public get; public set; }
static System.Int32   HeadLightsEndTime    { public get; public set; }
static System.Single  NPC_WET_RATE         { public get; public set; }
static System.Single  NPC_DRY_RATE         { public get; public set; }
```

### 2.2 Identity

```csharp
Il2CppScheduleOne.NPCs.Framework.BaseNPCDataObject _npcData { public get; public set; }  // ScriptableObject assigned in the prefab
Il2CppScheduleOne.NPCs.Framework.NPCData          NPCData  { public get; public set; }   // runtime deep copy
Il2CppSystem.Action<Il2CppScheduleOne.NPCs.Framework.NPCData> OnNPCDataReady { public get; public set; }

System.String   ID          { public get; }        // -> NPCData.BasicInfo.ID   (the string key used everywhere)
System.String   FirstName   { public get; }
System.String   LastName    { public get; }
System.String   FullName    { public get; }
UnityEngine.Sprite MugshotSprite { public get; }   // -> NPCData.Appearance.Mugshot

System.String       BakedGUID { public get; public set; }   // string form, authored on the scene prefab
Il2CppSystem.Guid   GUID      { public get; public set; }
Il2CppScheduleOne.Map.EMapRegion Region { public get; public set; }  // Northtown=0, Westville=1, Downtown=2, Docks=3, Suburbia=4, Uptown=5
System.Single       Scale     { public get; public set; }
```

```csharp
public System.Void ApplyNPCData(Il2CppScheduleOne.NPCs.Framework.NPCData data)
public virtual System.Void SetGUID(Il2CppSystem.Guid guid)
public System.Void SetScale(System.Single scale)
public System.Void SetScale(System.Single scale, System.Single lerpTime)
public virtual System.Void ApplyScale()
public virtual System.String GetNameAddress()
```

### 2.3 Component references (all assigned in `Awake`/`GetAndValidateReferences`)

```csharp
Il2CppScheduleOne.NPCs.NPCMovement                 Movement          { public get; public set; }
Il2CppScheduleOne.NPCs.Behaviour.NPCBehaviour      Behaviour         { public get; public set; }
Il2CppScheduleOne.NPCs.NPCInventory                Inventory         { public get; public set; }
Il2CppScheduleOne.NPCs.NPCHealth                   Health            { public get; public set; }
Il2CppScheduleOne.NPCs.NPCAwareness                Awareness         { public get; public set; }
Il2CppScheduleOne.NPCs.Responses.NPCResponses      Responses         { public get; public set; }
Il2CppScheduleOne.NPCs.Actions.NPCActions          Actions           { public get; public set; }
Il2CppScheduleOne.Dialogue.DialogueHandler         DialogueHandler   { public get; public set; }
Il2CppScheduleOne.AvatarFramework.Avatar           Avatar            { public get; public set; }
Il2CppScheduleOne.VoiceOver.VOEmitter              VoiceOverEmitter  { public get; public set; }
Il2CppScheduleOne.Vision.EntityVisibility          Visibility        { public get; public set; }
Il2CppScheduleOne.Tools.FloatStack                 AggressionController { public get; public set; }
Il2CppScheduleOne.NPCs.Relation.NPCRelationData    RelationData      { public get; public set; }
Il2CppScheduleOne.Messaging.MSGConversation        MSGConversation   { public get; public set; }
Il2CppScheduleOne.Equipping.Framework.NetworkedEquipper _networkedEquipper { public get; public set; }

public System.Void GetAndValidateReferences()
```

Note `NPCScheduleManager` is **not** a direct `NPC` property — reach it through
`npc.Behaviour.ScheduleManager`.

### 2.4 World state

```csharp
Il2CppScheduleOne.Vehicles.LandVehicle CurrentVehicle { public get; public set; }
System.Boolean                         IsInVehicle    { public get; }
Il2CppSystem.Action<Il2CppScheduleOne.Vehicles.LandVehicle> onEnterVehicle { public get; public set; }
Il2CppSystem.Action<Il2CppScheduleOne.Vehicles.LandVehicle> onExitVehicle  { public get; public set; }

Il2CppScheduleOne.Map.NPCEnterableBuilding CurrentBuilding { public get; public set; }
System.Boolean                             isInBuilding    { public get; }
Il2CppScheduleOne.Doors.StaticDoor         LastEnteredDoor { public get; public set; }

UnityEngine.Vector3   CenterPoint          { public get; }
UnityEngine.Transform CenterPointTransform { public get; }
UnityEngine.Vector3   LookAtPoint          { public get; }
UnityEngine.Vector3   Velocity             { public get; }

System.Boolean IsConscious           { public get; }
System.Boolean IsCurrentlyTargetable { public get; }
System.Single  Aggression            { public get; }
System.Single  RangedHitChanceMultiplier { public get; }
System.Boolean isVisible    { public get; public set; }
System.Boolean isUnsettled  { public get; public set; }
System.Boolean IsPanicked   { public get; public set; }
System.Single  TimeSincePanicked { public get; public set; }
System.Boolean HasUmbrella  { public get; public set; }   // FishNet SyncVar
System.Boolean IsUnderCover { public get; public set; }
Il2CppSystem.Action<System.Boolean> onVisibilityChanged { public get; public set; }
```

### 2.5 Methods worth binding

Movement / placement, buildings, vehicles:

```csharp
public virtual System.Void EnterVehicle(Il2CppFishNet.Connection.NetworkConnection connection,
                                        Il2CppScheduleOne.Vehicles.LandVehicle veh)   // ObserversRpc + TargetRpc
public virtual System.Void ExitVehicle()
public virtual System.Void EnterBuilding(System.String buildingGUID, System.Int32 doorIndex)
public          System.Void EnterBuilding(Il2CppFishNet.Connection.NetworkConnection connection,
                                          System.String buildingGUID, System.Int32 doorIndex)
public          System.Void ExitBuilding(System.String buildingID = "")
public virtual System.Void ExitBuilding(Il2CppScheduleOne.Map.NPCEnterableBuilding building)
public          System.Void SetTransform(Il2CppFishNet.Connection.NetworkConnection conn,
                                         UnityEngine.Vector3 position, UnityEngine.Quaternion rotation)
```

`[HD]` `NPC.EnterVehicle(conn, veh)` is the *networked* path that makes an NPC an occupant of a
`LandVehicle`; it is mirrored by `LandVehicle.AddNPCOccupant(NPC)` / `RemoveNPCOccupant(NPC)`
(see API-VEHICLES.md §5.2). This is how police and `NPCSignal_DriveToCarPark` put an NPC behind
the wheel — **NPCs use it, players use `Player.EnterVehicle(veh, seat)`.**

Animation & equipping (all networked variants exist):

```csharp
public System.Void SetAnimationTrigger(System.String trigger)
public System.Void SetAnimationTrigger_Networked(Il2CppFishNet.Connection.NetworkConnection conn, System.String trigger)
public System.Void ResetAnimationTrigger(System.String trigger)
public System.Void SetAnimationBool(System.String trigger, System.Boolean val)
public System.Void SetAnimationBool_Networked(Il2CppFishNet.Connection.NetworkConnection conn, System.String id, System.Boolean value)
public System.Void SendAnimationTrigger(System.String trigger)          // ServerRpc
public System.Void SetCrouched_Networked(System.Boolean crouched)

public System.Void SetEquippable_Client(Il2CppFishNet.Connection.NetworkConnection conn, System.String assetPath)
public System.Void SetEquippable_Networked_ExcludeServer(Il2CppFishNet.Connection.NetworkConnection conn, System.String assetPath)
public Il2CppScheduleOne.AvatarFramework.Equipping.AvatarEquippable SetEquippable_Return(System.String assetPath)
public Il2CppScheduleOne.AvatarFramework.Equipping.AvatarEquippable SetEquippable_Networked_Return(
        Il2CppFishNet.Connection.NetworkConnection conn, System.String assetPath)
public virtual Il2CppScheduleOne.Core.Equipping.Framework.IEquippedItemHandler Equip(
        Il2CppScheduleOne.Core.Equipping.Framework.EquippableData equippable)
public virtual Il2CppScheduleOne.Core.Equipping.Framework.IEquippedItemHandler Equip(
        Il2CppScheduleOne.Core.Items.Framework.BaseItemInstance item)
public virtual System.Void Unequip(Il2CppScheduleOne.Core.Equipping.Framework.IEquippedItemHandler equippedItem)
public virtual System.Void UnequipAll()
```

Speech, messaging, relationships:

```csharp
public System.Void ShowWorldSpaceDialogue(System.String text, System.Single duration)
public System.Void SendWorldSpaceDialogue(System.String text, System.Single duration)     // ServerRpc
public System.Void PlayWorldspaceDialogueReaction(System.String key, System.Single duration)
public System.Void PlayVO(Il2CppScheduleOne.VoiceOver.EVOLineType lineType, System.Boolean network = false)
public System.Void SendTextMessage(System.String message)
public virtual System.Void CreateMessageConversation()
public virtual System.String GetMessagingName()
public virtual UnityEngine.Sprite GetMessagingIcon()
public System.Void SetRelationship(System.Single relationship)
public System.Void SendRelationship(System.Single relationship)                            // ServerRpc
public System.Void ReceiveRelationshipData(Il2CppFishNet.Connection.NetworkConnection conn,
                                           System.Single relationship, System.Boolean unlocked)
```

Combat / damage / state:

```csharp
public virtual System.Void ReceiveImpact(Il2CppScheduleOne.Combat.Impact impact)
public virtual System.Void SendImpact(Il2CppScheduleOne.Combat.Impact impact)
public virtual System.Void ProcessImpactForce(UnityEngine.Vector3 forcePoint, UnityEngine.Vector3 forceDirection, System.Single force)
public virtual System.Void AimedAtByPlayer(Il2CppFishNet.Object.NetworkObject player)
public virtual System.Void OnDie()
public virtual System.Void OnKnockedOut()
public          System.Void SetPanicked_Server()
public          System.Void RemovePanicked()
public          System.Void SetUnsettled(System.Single duration)
public virtual System.Void SetUnsettled_30s(Il2CppScheduleOne.PlayerScripts.Player player)
public          System.Void SetIsBeingPickPocketed(System.Boolean pickpocketed)
public virtual System.Void SetVisible(System.Boolean visible, System.Boolean networked = false)
public virtual System.Void ShowOutline(UnityEngine.Color color)
public virtual System.Void HideOutline()
public virtual System.Boolean IsCurrentlySightable()
public virtual System.Void RecordLastKnownPosition(System.Boolean resetTimeSinceLastSeen)
public virtual System.Single GetSearchTime()
```

Lifecycle & persistence (override targets for a custom subclass):

```csharp
public virtual System.Void Awake()
public virtual System.Void Start()
public virtual System.Void OnStartServer()
public virtual System.Void OnSpawnServer(Il2CppFishNet.Connection.NetworkConnection connection)
public virtual System.Void OnDestroy()
public virtual System.Void OnTick()
public virtual System.Void MinPass()
public virtual System.Void OnUncappedMinPass()
public virtual System.Void InitializeSaveable()
public virtual Il2CppScheduleOne.Persistence.Datas.NPCData GetNPCData()      // NOTE: Persistence.Datas.NPCData, a DIFFERENT type from NPCs.Framework.NPCData
public virtual Il2CppScheduleOne.Persistence.Datas.DynamicSaveData GetSaveData()
public virtual System.String GetSaveString()
public virtual Il2CppSystem.Collections.Generic.List<System.String> WriteData(System.String parentFolderPath)
public virtual System.Void Load(Il2CppScheduleOne.Persistence.Datas.NPCData data, System.String containerPath)
public virtual System.Void Load(Il2CppScheduleOne.Persistence.Datas.DynamicSaveData dynamicData,
                                Il2CppScheduleOne.Persistence.Datas.NPCData npcData)
public virtual System.Boolean ShouldSave()
public virtual System.Boolean ShouldSaveHealth()
public virtual System.Boolean ShouldSaveInventory()
public virtual System.Boolean ShouldSaveRelationshipData()
public          System.Boolean ShouldSaveMessages()
System.String SaveFolderName { public get; }
System.String SaveFileName   { public get; }
Il2CppScheduleOne.Persistence.Loaders.Loader Loader { public get; }   // -> NPCLoader
```

**Two different `NPCData` types exist.** `Il2CppScheduleOne.NPCs.Framework.NPCData` is the
*design-time definition* (name, appearance, behaviour tuning).
`Il2CppScheduleOne.Persistence.Datas.NPCData` is the *save record*. Do not confuse them.

---

## 3. `Il2CppScheduleOne.NPCs.NPCManager` — the registry

`public class NPCManager : Il2CppScheduleOne.DevUtilities.NetworkSingleton<NPCManager>`

`NetworkSingleton<T>` (verified in `ns-Il2CppScheduleOne.DevUtilities.txt`) provides:

```csharp
static T              Instance       { public get; public set; }
static System.Boolean InstanceExists { public get; }
```

`NPCManager` itself:

```csharp
// THE registry. Static list, not per-instance.
static Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.NPCs.NPC> NPCRegistry { public get; public set; }

Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<UnityEngine.Transform> NPCWarpPoints { public get; public set; }
UnityEngine.Transform NPCContainer { public get; public set; }   // parent transform for NPC GameObjects
Il2CppScheduleOne.Map.NPCPoI NPCPoIPrefab                  { public get; public set; }
Il2CppScheduleOne.Map.NPCPoI PotentialCustomerPoIPrefab    { public get; public set; }
Il2CppScheduleOne.Map.NPCPoI PotentialDealerPoIPrefab      { public get; public set; }
Il2CppScheduleOne.Persistence.Loaders.NPCsLoader loader    { public get; public set; }
System.Int32 LoadOrder { public get; }

public static Il2CppScheduleOne.NPCs.NPC GetNPC(System.String id)
public static Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.NPCs.NPC> GetNPCsInRegion(Il2CppScheduleOne.Map.EMapRegion region)
public Il2CppSystem.Collections.Generic.List<UnityEngine.Transform> GetOrderedDistanceWarpPoints(UnityEngine.Vector3 origin)
public virtual System.Void Awake()
public virtual System.Void InitializeSaveable()
public virtual System.String GetSaveString()
public virtual Il2CppSystem.Collections.Generic.List<System.String> WriteData(System.String parentFolderPath)
```

**There is no `NPCManager.SpawnNPC` / `DespawnNPC` / `RegisterNPC` / pooling API.** The registry
is a plain static `List<NPC>` that NPCs add themselves to (inferred: `NPC.Awake` / `Start`, since
no other type in the dumps mutates it). Consequences for a mod:

- To make a new NPC visible to the rest of the game, **add it to `NPCManager.NPCRegistry`**. That
  single list is what `GetNPC(id)`, `GetNPCsInRegion`, the phone contacts app, the relationship
  UI and the save writer all iterate.
- Lookup is by the **string `ID`**, not GUID. `GetNPC` is `static`, so it works before
  `NPCManager.Instance` is touched, as long as the registry is populated.
- `NPCContainer` is where you should parent a spawned NPC GameObject so it inherits the same
  activation/culling treatment as shipped NPCs.
- Pooling exists, but only for cartel goons — see §14.3.

---

## 4. Subclass tree (from `03-subclasses.txt`)

`Il2CppScheduleOne.NPCs.NPC` has **81 direct subclasses**:

| Group | Types |
|---|---|
| **Dealers** `[SC]` | `Il2CppScheduleOne.Economy.Dealer` (7 subclasses: `Il2CppScheduleOne.Cartel.CartelDealer`, and `CharacterClasses.Benji`, `Brad`, `Jane`, `Leo`, `Molly`, `Wei`) |
| **Suppliers** | `Il2CppScheduleOne.Economy.Supplier` (4 subclasses: `CharacterClasses.Albert`, `Phil`, `Salvador`, `Shirley`) |
| **Employees** `[HD]` | `Il2CppScheduleOne.Employees.Employee` (4 subclasses: `Botanist`, `Chemist`, `Cleaner`, `Packager`) |
| **Police** `[PI]` | `Il2CppScheduleOne.Police.PoliceOfficer` (no subclasses) |
| **Cartel** | `Il2CppScheduleOne.Cartel.CartelGoon` |
| **Scene one-offs** | `NPCs.Billy`, `NPCs.Chloe`, `NPCs.Donna`, `NPCs.Doris`, `NPCs.Jerry`, `NPCs.Meg`, `NPCs.Stan` |
| **Named townsfolk** | 66 `Il2CppScheduleOne.NPCs.CharacterClasses.*` classes: `Alison, Anna, Austin, Beth, Bruce, Carl, Charles, Chris, Dan, Dean, Dennis, Elizabeth, Eugene, Fiona, Fixer, Frank, Genghis, George, Geraldine, Greg, Harold, Herbert, Igor, Irene, Jack, Jackie, Javier, Jeff, Jen, Jennifer, Jeremy, Jessi, Joyce, Karen, Kathy, Keith, Kelly, Kevin, Kim, Kyle, Lily, Lisa, Louis, Lucy, Ludwig, Mac, Marco, Melissa, Michael, Mick, Ming, Oscar, Pearl, Peggy, Peter, Philip, Randy, Ray, Sam, SchizoGoblin, SewerGoblin, SewerKing, Sherman, Steve, Thomas, Tobias, Trent, UncleNelson, Walter` |

**Customers are not an NPC subclass.** `Il2CppScheduleOne.Economy.Customer` is a separate
`NetworkBehaviour` sitting on the same GameObject, with `Customer.NPC` pointing back. That is why
a plain `CharacterClasses.*` NPC can also be a customer. `[SC]` **To make a custom NPC a customer
you add a `Customer` component, not a subclass.** Customer statics that matter:

```csharp
static Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Economy.Customer> LockedCustomers   { public get; public set; }
static Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Economy.Customer> UnlockedCustomers { public get; public set; }
static Il2CppSystem.Action<Il2CppScheduleOne.Economy.Customer> onCustomerUnlocked { public get; public set; }

Il2CppScheduleOne.NPCs.NPC        NPC          { public get; public set; }
Il2CppScheduleOne.Economy.CustomerData CustomerData { public get; }
Il2CppScheduleOne.Quests.Contract CurrentContract { public get; public set; }
Il2CppScheduleOne.Economy.Dealer  AssignedDealer  { public get; public set; }
System.Single CurrentAddiction { public get; public set; }
Il2CppScheduleOne.NPCs.Behaviour.CustomerAttendDealBehaviour _attendDealBehaviour { public get; public set; }

public Il2CppScheduleOne.Quests.ContractInfo TryGenerateContract(Il2CppScheduleOne.Economy.Dealer dealer)
public virtual System.Void OfferContract(Il2CppScheduleOne.Quests.ContractInfo info)
public Il2CppScheduleOne.Quests.Contract ContractAccepted(Il2CppScheduleOne.Economy.EDealWindow window,
                                                          System.Boolean trackContract,
                                                          Il2CppScheduleOne.Economy.Dealer dealer)
public virtual System.Void ProcessHandover(Il2CppScheduleOne.UI.Handover.HandoverScreen+EHandoverOutcome outcome,
                                           Il2CppScheduleOne.Quests.Contract contract,
                                           Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemInstance> items,
                                           System.Boolean handoverByPlayer, System.Boolean giveBonuses = true)
public System.Void ProcessSample(Il2CppScheduleOne.UI.Handover.HandoverScreen+EHandoverOutcome outcome,
                                 Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemInstance> items,
                                 System.Single price)
public virtual System.Single EvaluateDelivery(Il2CppScheduleOne.Quests.Contract contract,
                                              Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemInstance> providedItems,
                                              out System.Single& highestAddiction,
                                              out Il2CppScheduleOne.Product.EDrugType& mainTypeType,
                                              out System.Int32& matchedProductCount,
                                              out System.Single& qualityDifference)
public System.Void AdjustAffinity(Il2CppScheduleOne.Product.EDrugType drugType, System.Single change)
public Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Product.ProductDefinition> GetOrderableProducts(Il2CppScheduleOne.Economy.Dealer dealer = null)
public System.Void RecommendCustomer(Il2CppScheduleOne.Economy.Customer friend)
public virtual System.Void OnCustomerUnlocked(Il2CppScheduleOne.NPCs.Relation.NPCRelationData+EUnlockType unlockType, System.Boolean notify)
public virtual System.Single GetSampleRequestSuccessChance()
```

Tunables you would override for a "special" customer `[SC]`: `QualityTierTolerance`,
`MaxOrderQuantityPerProduct`, `AFFINITY_MAX_EFFECT`, `PROPERTY_MAX_EFFECT`, `QUALITY_MAX_EFFECT`,
`DEAL_COOLDOWN`, `APPROACH_MIN_ADDICTION`, `APPROACH_CHANCE_PER_DAY_MAX`, `OFFER_EXPIRY_TIME_MINS`,
`MIN_ORDER_APPEAL`, `ADDICTION_DRAIN_PER_DAY`, `SAMPLE_REQUIRES_RECOMMENDATION`,
`DEAL_ATTENDANCE_TOLERANCE`, `MIN_TRAVEL_TIME`, `MAX_TRAVEL_TIME`. All are static, so changing
them is global — per-customer behaviour has to come from `CustomerData` /
`CustomerAffinityData` instead.

---

## 5. `Il2CppScheduleOne.NPCs.Framework` — the NPC definition model

This is the single most useful thing in this document for authoring characters. Every NPC prefab
carries a `BaseNPCDataObject` ScriptableObject; `NPC.Awake` pulls a **runtime deep copy** out of
it into `NPC.NPCData`.

```csharp
public class BaseNPCDataObject : UnityEngine.ScriptableObject
{
    public virtual NPCData GetOriginalData();   // the shared asset instance — DO NOT MUTATE
    public virtual NPCData GetRuntimeData();    // per-NPC deep copy — mutate this
    public virtual System.Void Initialize();
}

public class GenericNPCDataObject<T> : BaseNPCDataObject { T _data { get; set; } }
public class NPCDataObject         : GenericNPCDataObject<NPCData>         { }   // CreateAssetMenu "NPCs/NPC Data Object"
public class DealerNPCDataObject   : GenericNPCDataObject<DealerNPCData>   { }   // "NPCs/Dealer Data Object"
public class SupplierNPCDataObject : GenericNPCDataObject<SupplierNPCData> { }   // "NPCs/Supplier Data Object"
```

`NPCData` holds twelve blocks, each a `Il2CppScheduleOne.Core.ValueOrReference<TValue, TPreset>`
— i.e. **either an inline value or a reference to a shared `*Preset` ScriptableObject**. The
read-only convenience getter resolves whichever is set:

```csharp
public class NPCData
{
    ValueOrReference<BasicInfo,         BasicInfoPreset>         _basicInfo         { get; set; }
    ValueOrReference<Appearance,        AppearancePreset>        _appearance        { get; set; }
    ValueOrReference<Health,            HealthPreset>            _health            { get; set; }
    ValueOrReference<Movement,          MovementPreset>          _movement          { get; set; }
    ValueOrReference<Interaction,       InteractionPreset>       _interaction       { get; set; }
    ValueOrReference<Relationship,      RelationshipPreset>      _relationship      { get; set; }
    ValueOrReference<Messaging,         MessagingPreset>         _messaging         { get; set; }
    ValueOrReference<Dialogue,          DialoguePreset>          _dialogue          { get; set; }
    ValueOrReference<Voice,             VoicePreset>             _voice             { get; set; }
    ValueOrReference<Inventory,         InventoryPreset>         _inventory         { get; set; }
    ValueOrReference<Behaviour,         BehaviourPreset>         _behaviour         { get; set; }
    ValueOrReference<WeatherBehaviour,  WeatherBehaviourPreset>  _weatherBehaviour  { get; set; }

    BasicInfo BasicInfo { get; }  Appearance Appearance { get; }  Health Health { get; }
    Movement Movement { get; }    Interaction Interaction { get; } Relationship Relationship { get; }
    Messaging Messaging { get; }  Dialogue Dialogue { get; }       Voice Voice { get; }
    Inventory Inventory { get; }  Behaviour Behaviour { get; }     WeatherBehaviour WeatherBehaviour { get; }

    public virtual NPCData GetDeepCopy();
    public System.Void PopulateNPCData(NPCData data);
}
```

Every block also exposes `GetCopy()`. Full field lists:

```csharp
class BasicInfo   { System.String FirstName; System.Boolean HasLastName; System.String LastName; System.String ID; }
class Appearance  { AvatarSettings AvatarSettings; UnityEngine.Sprite Mugshot; AvatarSettings ChristmasAppearance; }
class Health      { System.Single MaxHealth; System.Boolean Invincible; System.Boolean CanRevive; System.Int32 DaysToRevive; }
class Movement    { System.Single WalkSpeed; System.Single SprintSpeed; System.Boolean CanOpenDoors; }
class Interaction { System.Boolean CanBeSummoned; }
class Relationship{ System.Single DefaultRelationshipValue; System.Boolean DisplayRelationshipValue; }
class Messaging   { System.Boolean IsKnownByDefault; System.Boolean ConversationCanBeHidden;
                    Il2CppStructArray<Il2CppScheduleOne.Messaging.EConversationCategory> ConversationCategories; }
class Dialogue    { Il2CppScheduleOne.Dialogue.DialogueDatabase DialogueDatabase; }
class Voice       { Il2CppScheduleOne.VoiceOver.VODatabase VoiceDatabase; System.Single VoicePitch; }
class Behaviour   { System.Boolean IgnorePhysicsImpacts; System.Boolean IgnoreCombatImpacts;
                    System.Single DefaultAggression; System.Boolean CanCallPolice; }
class WeatherBehaviour { System.Single UseUmbrellaChance; System.Single RainTolerance;
                         System.Single MaxWalkSpeedInRainMultiplier; }
class Inventory
{
    System.Int32   InventorySlotCount;
    System.Boolean ClearInventoryOnNewDay;
    System.Boolean RandomizeInventory;
    System.Boolean AllowDuplicateRandomItems;
    Il2CppReferenceArray<Inventory+WeightedItem> RandomInventoryItems;   // WeightedItem { ItemDefinition Item; Single Weight; }
    System.Boolean RandomizeCash;
    System.Int32   MinRandomCash;
    System.Int32   MaxRandomCash;
    Il2CppReferenceArray<Il2CppScheduleOne.ItemFramework.ItemDefinition> StartingInventoryItems;
    System.Boolean CanBePickpocketed;
    System.Single  PickpocketDifficulty;
    Il2CppScheduleOne.AvatarFramework.Equipping.AvatarWeapon DefaultCombatWeapon;
}
```

Role-specific extensions:

```csharp
class DealerNPCData : NPCData
{
    Il2CppScheduleOne.Economy.EDealerType DealerType;
    System.String HomeName;
    System.Single SigningFee;
    System.Single SalesCutPercentage;
    Il2CppScheduleOne.Dialogue.DialogueContainer RecruitDialogue;
    Il2CppScheduleOne.Dialogue.DialogueContainer CollectCashDialogue;
    Il2CppScheduleOne.Dialogue.DialogueContainer AssignCustomersDialogue;
    public virtual NPCData GetDeepCopy();
    public System.Void PopulateDealerData(DealerNPCData data);
}

class SupplierNPCData : NPCData
{
    System.Single MinimumDeaddropOrderLimit;
    System.Single MaximumDeaddropOrderLimit;
    Il2CppReferenceArray<Il2CppScheduleOne.UI.Phone.PhoneShopInterface+Listing> DeliveryShopListings;
    System.String SupplierRecommendMessage;
    System.String SupplierUnlockHint;
    public System.Void PopulateSupplierData(SupplierNPCData data);
}
```

**How this flows at runtime:** `NPC.Awake` → reads `_npcData` → `GetRuntimeData()` (deep copy) →
`NPC.ApplyNPCData(data)` → sets `NPCData`, fires `OnNPCDataReady`, and hands the blocks off to
subsystems: `NPCInventory.Initialize(NPCData)`, `DialogueHandler.Initialize(NPCData)`, and
`Avatar.LoadAvatarSettings(NPCData.Appearance.AvatarSettings)` (last one inferred — `Avatar` has
no `NPCData` overload, but `Appearance.AvatarSettings` has no other consumer).

**Mod pattern:** create a `ScriptableObject.CreateInstance<NPCDataObject>()`, fill `_data` with a
`NPCData` you build field by field, assign to `npc._npcData` **before** `Awake` runs (i.e. on a
prefab instance that is instantiated inactive), then activate. Or, post-`Awake`, mutate
`npc.NPCData` in place and re-drive the subsystems yourself.

---

## 6. Appearance / avatar system

### 6.1 `Il2CppScheduleOne.AvatarFramework.Avatar : UnityEngine.MonoBehaviour`

The renderer-side object. Statics: `MAX_ACCESSORIES`, `CombinedLayersEnabled`,
`DEFAULT_SMOOTHNESS`, `maleShoulderScale`, `femaleShoulderScale`.

Key references: `Animation` (`AvatarAnimation`), `LookController` (`AvatarLookController`),
`BodyMeshes` / `ShapeKeyMeshes` (`SkinnedMeshRenderer[]`), `FaceMesh`, `Eyes` (`EyeController`),
`EyeBrows` (`EyebrowController`), `EmotionManager`, `Effects` (`AvatarEffects`), `Impostor`,
bones (`Armature`, `HeadBone`, `HipBone`, `LeftShoulder`, `RightShoulder`, `LeftFootBone`,
`RightFootBone`, `MiddleSpine`, `LowerSpine`, `LowestSpine`), ragdoll arrays
(`RagdollRBs`, `RagdollColliders`, `ImpactForceRBs`), `DefaultAvatarMaterial`.

Applied state, readable at runtime: `appliedGender`, `appliedWeight`, `appliedHair` (`Hair`),
`appliedHairColor`, `appliedAccessories` (`Accessory[]`), `_appliedSkinColor`,
`_appliedEmissionColor`, `wearingHairBlockingAccessory`, `CurrentSettings` (`AvatarSettings`),
`CurrentEquippable`, `Ragdolled`.

The apply API — **this is the whole appearance surface**:

```csharp
public System.Void LoadAvatarSettings(AvatarSettings settings);                 // full apply, fires onSettingsLoaded
public System.Void LoadNakedSettings(AvatarSettings settings, System.Boolean keepOldLayers, System.Int32 maxLayerOrder = 19);
public System.Void ApplyBodySettings(AvatarSettings settings);                  // gender/weight/height/skin
public System.Void ApplyBodyLayerSettings(AvatarSettings settings, System.Int32 maxOrder = -1);
public System.Void ApplyFaceLayerSettings(AvatarSettings settings);
public System.Void ApplyAccessorySettings(AvatarSettings settings);
public System.Void ApplyHairSettings(AvatarSettings settings);
public System.Void ApplyHairColorSettings(AvatarSettings settings);
public System.Void ApplyEyebrowSettings(AvatarSettings settings);
public System.Void ApplyEyeBallSettings(AvatarSettings settings);
public System.Void ApplyEyeLidSettings(AvatarSettings settings);
public System.Void ApplyEyeLidColorSettings(AvatarSettings settings);
public System.Void ApplyShapeKeys(System.Single gender, System.Single weight);
public System.Void ApplyCurrentShapeKeys();

public System.Void SetBodyLayer(System.Int32 index, System.String assetPath, UnityEngine.Color color);
public System.Void SetFaceLayer(System.Int32 index, System.String assetPath, UnityEngine.Color color);
public System.Void SetFaceTexture(UnityEngine.Texture2D tex, UnityEngine.Color color);
public System.Void SetSkinColor(UnityEngine.Color color);
public System.Void SetEmission(UnityEngine.Color color);
public System.Void OverrideHairColor(UnityEngine.Color color);
public System.Void ResetHairColor();
public System.Void SetHairVisible(System.Boolean visible);
public System.Void SetWearingHairBlockingAccessory(System.Boolean blocked);
public System.Void SetBlockEyeFaceLayers(System.Boolean block);
public System.Void SetFeetShrunk(System.Boolean shrink, System.Single reduction);
public System.Void SetAdditionalGender(System.Single gender);
public System.Void SetAdditionalWeight(System.Single weight);
public System.Void DestroyAccessories();
public System.Void SetVisible(System.Boolean vis);

public virtual AvatarEquippable SetEquippable(System.String assetPath);
public System.Void GetMugshot(Il2CppSystem.Action<UnityEngine.Texture2D> callback);
public System.Boolean IsMale();
public System.Boolean IsWhite();
public System.String GetFormalAddress(System.Boolean capitalized = true);
public System.String GetThirdPersonAddress(System.Boolean capitalized = true);
public System.String GetThirdPersonPronoun(System.Boolean capitalized = true);

public System.Void EnableRagdoll(UnityEngine.Vector3 forcePoint = null, UnityEngine.Vector3 forceDir = null);
public System.Void DisableRagdoll(System.Boolean playStandUpAnim = true);
public System.Void SetRagdollPhysicsEnabled(System.Boolean ragdollEnabled, System.Boolean wait,
                                            System.Boolean playStandUpAnim = true,
                                            UnityEngine.Vector3 forcePoint = null, UnityEngine.Vector3 forceDir = null);
public System.Void ApplyRagdollForce(UnityEngine.Vector3 forcePoint, UnityEngine.Vector3 forceDir);
```

### 6.2 `AvatarSettings : UnityEngine.ScriptableObject` — the full record

This is the exact, complete field list. Everything about how an NPC looks is here.

```csharp
// --- Body ---
UnityEngine.Color SkinColor;      // RGB, no documented clamp
System.Single     Height;         // scale factor; NPC.Scale multiplies on top
System.Single     Gender;         // 0..1 shape-key blend (0 = female end, 1 = male end) (inferred from ApplyShapeKeys(gender, weight) + Avatar.IsMale())
System.Single     Weight;         // 0..1 shape-key blend

// --- Hair ---
System.String     HairPath;       // asset path of an AvatarFramework.Hair (an Accessory subclass)
UnityEngine.Color HairColor;

// --- Eyebrows ---
System.Single EyebrowScale;
System.Single EyebrowThickness;
System.Single EyebrowRestingHeight;   // normalized (Eyebrow.SetRestingHeight(normalizedHeight))
System.Single EyebrowRestingAngle;    // degrees (Eyebrow.SetRestingAngle)

// --- Eyes ---
UnityEngine.Color LeftEyeLidColor;
UnityEngine.Color RightEyeLidColor;
Eye+EyeLidConfiguration LeftEyeRestingState;    // struct { Single topLidOpen; Single bottomLidOpen; }
Eye+EyeLidConfiguration RightEyeRestingState;
System.String     EyeballMaterialIdentifier;
UnityEngine.Color EyeBallTint;
System.Single     PupilDilation;
System.Single UpperEyelidRestingPosition { get; }   // derived from LeftEyeRestingState
System.Single LowerEyelidRestingPosition { get; }

// --- Layers & accessories (the clothing/tattoo/detail system) ---
List<AvatarSettings+LayerSetting>     FaceLayerSettings;    // struct LayerSetting { String layerPath; Color layerTint; }
List<AvatarSettings+LayerSetting>     BodyLayerSettings;
List<AvatarSettings+AccessorySetting> AccessorySettings;    // class AccessorySetting { String path; Color color; }

// --- Impostor / perf ---
System.Boolean     UseCombinedLayer;
AvatarLayer        CombinedLayer;
UnityEngine.Texture2D ImpostorTexture;

// --- Indexed convenience accessors (read-only, resolve into the lists above) ---
System.String FaceLayer1Path .. FaceLayer6Path;        UnityEngine.Color FaceLayer1Color .. FaceLayer6Color;
System.String BodyLayer1Path .. BodyLayer8Path;        UnityEngine.Color BodyLayer1Color .. BodyLayer8Color;
System.String Accessory1Path .. Accessory9Path;        UnityEngine.Color Accessory1Color .. Accessory9Color;
Il2CppSystem.Object Item[System.String propertyName] { public get; }   // reflection-ish indexer by property name
public virtual System.String GetJson(System.Boolean prettyPrint = true);
```

**Hard capacities, read directly off the accessor ranges:** 6 face layers, 8 body layers,
9 accessories, plus hair. `Avatar.MAX_ACCESSORIES` is the authoritative accessory cap
(a static field; read it at runtime rather than assuming 9).

### 6.3 Layers and accessories

```csharp
public class AvatarLayer : UnityEngine.ScriptableObject
{
    System.String  Name;
    System.String  AssetPath;              // the string you put in LayerSetting.layerPath
    UnityEngine.Texture2D Texture;
    UnityEngine.Texture2D Normal;
    UnityEngine.Texture2D Normal_DefaultImportType;
    System.Int32   Order;                  // paint order; ApplyBodyLayerSettings(maxOrder) clips by this
    UnityEngine.Material CombinedMaterial;
}
public class FaceLayer : AvatarLayer { }

public class Accessory : UnityEngine.MonoBehaviour   // a PREFAB, not a texture
{
    System.String  Name;
    System.String  AssetPath;
    System.Boolean ReduceFootSize;   System.Single FootSizeReduction;
    System.Boolean ShouldBlockHair;  System.Boolean ColorAllMeshes;
    MeshRenderer[] meshesToColor;    SkinnedMeshRenderer[] skinnedMeshesToColor;
    SkinnedMeshRenderer[] skinnedMeshesToBind;   SkinnedMeshRenderer[] shapeKeyMeshRends;
    public System.Void ApplyColor(UnityEngine.Color col);
    public System.Void ApplyShapeKeys(System.Single gender, System.Single weight);
    public System.Void BindBones(Il2CppReferenceArray<UnityEngine.Transform> bones);
}
public class Hair       : Accessory { GameObject[] hairToHide; System.Boolean BlockedByHat;
                                      public virtual System.Void BlockHair(); public virtual System.Void UnBlockHair();
                                      public System.Void SetBlockedByHat(System.Boolean blocked); }
public class PoliceBelt : Accessory { GameObject BatonObject, TaserObject, GunObject;
                                      public System.Void SetBatonVisible(bool); SetTaserVisible(bool); SetGunVisible(bool); }  // [PI]
```

**Layers are textures composited onto the body/face mesh; accessories are 3D prefabs bound to the
armature.** So "t-shirt", "jeans", "tattoo", "stubble" are layers; "cap", "sunglasses",
"backpack", "police belt", "hair" are accessories.

### 6.4 `BasicAvatarSettings` — the flat, mod-friendly record

`Il2CppScheduleOne.AvatarFramework.Customization.BasicAvatarSettings : UnityEngine.ScriptableObject`

This is what the in-game character creator edits, and it converts straight into an
`AvatarSettings`. **For authoring custom NPCs this is by far the easiest entry point.**

```csharp
static System.Single GenderScaleMultiplier;
static System.String MaleUnderwearPath;     // "Avatar/Layers/Bottom/MaleUnderwear"   (verified literal)
static System.String FemaleUnderwearPath;   // "Avatar/Layers/Bottom/FemaleUnderwear" (verified literal)

System.Int32      Gender;          // NOTE: Int32 here, Single on AvatarSettings
System.Single     Weight;
UnityEngine.Color SkinColor;
System.String     HairStyle;       // accessory asset path
UnityEngine.Color HairColor;
System.String     Mouth;           // face-layer asset path
System.String     FacialHair;      // face-layer asset path
System.String     FacialDetails;   // face-layer asset path
System.Single     FacialDetailsIntensity;
UnityEngine.Color EyeballColor;
System.Single     UpperEyeLidRestingPosition;
System.Single     LowerEyeLidRestingPosition;
System.Single     PupilDilation;
System.Single     EyebrowScale;
System.Single     EyebrowThickness;
System.Single     EyebrowRestingHeight;
System.Single     EyebrowRestingAngle;
System.String     Top;        UnityEngine.Color TopColor;
System.String     Bottom;     UnityEngine.Color BottomColor;
System.String     Shoes;      UnityEngine.Color ShoesColor;
System.String     Headwear;   UnityEngine.Color HeadwearColor;
System.String     Eyewear;    UnityEngine.Color EyewearColor;
List<System.String> Tattoos;

public AvatarSettings GetAvatarSettings();                                  // <-- the conversion
public virtual System.String GetJson(System.Boolean prettyPrint = true);
public static UnityEngine.Color GetNippleColor(UnityEngine.Color skinColor);
public T GetValue<T>(System.String fieldName);
public T SetValue<T>(System.String fieldName, T value);
```

`GetValue<T>`/`SetValue<T>` by field name means **you can drive the whole appearance from a JSON
or config file** without hard-referencing fields. Combined with `GetJson()` you can round-trip
appearances: capture an existing NPC's look, edit strings, apply.

`CharacterCreator+ECategory` = `Body=0, Hair=1, Face=2, Eyes=3, Eyebrows=4, Clothing=5, Accessories=6`.
`CharacterCreator.Presets` is a `List<BasicAvatarSettings>` — **a shipped library of complete
character looks you can copy from**, plus `CharacterCreator.SelectPreset(string presetName)`.
`CustomizationManager.AppearancesFolderPath` is a static string pointing at the saved-appearance
folder, and `CustomizationManager.LoadSettings(string path, bool editOriginal = false)` /
`CreateSettings(string assetName, string assetPath)` are a working save/load pair for
`AvatarSettings` assets.

### 6.5 Clothing system (`Il2CppScheduleOne.Clothing`)

```csharp
public enum EClothingSlot { Feet=0, Bottom=1, Waist=2, Top=3, Outerwear=4, Hands=5, Neck=6, Eyes=7, Head=8, Wrist=9 }
public enum EClothingApplicationType { BodyLayer=0, FaceLayer=1, Accessory=2 }
public enum EClothingColor { White=0, LightGrey=1, DarkGrey=2, Charcoal=3, Black=4, LightRed=5, Red=6, Crimson=7,
                             Orange=8, Tan=9, Brown=10, Coral=11, Beige=12, Yellow=13, Lime=14, LightGreen=15,
                             DarkGreen=16, Cyan=17, SkyBlue=18, Blue=19, DeepBlue=20, Navy=21, DeepPurple=22,
                             Purple=23, Magenta=24, BrightPink=25, HotPink=26 }

public class ClothingDefinition : Il2CppScheduleOne.ItemFramework.StorableItemDefinition
{
    EClothingSlot            Slot;
    EClothingApplicationType ApplicationType;   // decides whether ClothingAssetPath is a body layer, face layer, or accessory
    System.String            ClothingAssetPath;
    System.Boolean           Colorable;
    EClothingColor           DefaultColor;
    List<EClothingSlot>      SlotsToBlock;      // e.g. a coat blocking Top
    public virtual ItemInstance GetDefaultInstance(System.Int32 quantity = 1);
}

public class ClothingInstance : Il2CppScheduleOne.Storage.StorableItemInstance
{
    EClothingColor Color;
    System.String  Name { get; }
    public virtual ItemInstance GetCopy(System.Int32 overrideQuantity = -1);
    public virtual Il2CppScheduleOne.Persistence.Datas.ItemData GetItemData();
}

public class ClothingUtility : Il2CppScheduleOne.DevUtilities.Singleton<ClothingUtility>
{
    List<ClothingUtility+ColorData>        ColorDataList;         // { EClothingColor ColorType; Color ActualColor; Color LabelColor; }
    List<ClothingUtility+ClothingSlotData> ClothingSlotDataList;  // { EClothingSlot Slot; String Name; Sprite Icon; }
    public ColorData        GetColorData(EClothingColor color);
    public ClothingSlotData GetSlotData(EClothingSlot slot);
}

public static class ClothingColorExtensions
{
    public static UnityEngine.Color GetActualColor(EClothingColor color);
    public static EClothingColor    GetClothingColor(UnityEngine.Color color);
    public static System.String     GetLabel(EClothingColor color);
    public static UnityEngine.Color GetLabelColor(EClothingColor color);
    public static System.Boolean    ColorEquals(UnityEngine.Color a, UnityEngine.Color b, System.Single tolerance = 0.004f);
}
```

**How a garment is applied at runtime (call sequence).** There is no `Avatar.WearClothing(...)`.
`ClothingDefinition` is an *item*; the avatar only knows about layers and accessories. The bridge
is `ApplicationType` + `ClothingAssetPath`:

1. `EClothingApplicationType.BodyLayer` → `avatar.SetBodyLayer(freeIndex, def.ClothingAssetPath, ClothingColorExtensions.GetActualColor(instance.Color))`, or append a `LayerSetting { layerPath, layerTint }` to `AvatarSettings.BodyLayerSettings` and re-`LoadAvatarSettings`.
2. `EClothingApplicationType.FaceLayer` → same with `SetFaceLayer` / `FaceLayerSettings`.
3. `EClothingApplicationType.Accessory` → append an `AccessorySetting { path, color }` to `AvatarSettings.AccessorySettings`, then `avatar.ApplyAccessorySettings(settings)`.

For a **custom NPC** you almost never want the item route. Build a `BasicAvatarSettings`, set
`Top`/`Bottom`/`Shoes`/`Headwear`/`Eyewear` + colors, call `GetAvatarSettings()`, then
`avatar.LoadAvatarSettings(...)`. That is exactly what the character creator does.

### 6.6 Can we author bikers / hippies / businessmen / federal agents from existing parts?

Yes, and the game already proves it — see `GoonPool` in §14.3, which builds a unique-looking NPC
every spawn out of nothing but: a base `AvatarSettings`, a *second* `AvatarSettings` used purely
as a clothing overlay, a skin colour, a hair colour, and a voice DB.

The mechanism you inherit:
- 8 body layers × arbitrary tint gives you shirts, jackets, trousers, boots, hi-vis, suits.
- 6 face layers gives you beards, stubble, face paint, scars, sunglasses-shaped decals.
- 9 accessories gives you hats, helmets, glasses, earpieces, belts (`PoliceBelt` is already an
  `Accessory`, so a **federal agent with a duty belt is achievable by reusing the police belt
  prefab** and re-tinting).
- Arbitrary `Color` on every layer/accessory — you are not limited to `EClothingColor`; that enum
  is only the *item* system's palette. `Avatar.SetBodyLayer` takes a raw `UnityEngine.Color`.
- `Gender` and `Weight` shape-key blends plus `Height` and `NPC.Scale` give body silhouette.

**The limiting factor is the asset catalogue, not the API.** The set of valid `AssetPath` strings
is **not present in the metadata dumps** — only four literals appear anywhere
(`Avatar/Layers/Bottom/MaleUnderwear`, `Avatar/Layers/Bottom/FemaleUnderwear`,
`Avatar/Layers/Face/EyeShadow`, `Avatar/Layers/Top/Nipples`, plus three under
`Avatar/Equippables/`). The rest live in `AvatarLayer` / `Accessory` assets. **Enumerate them at
runtime** — that is the only reliable way, and it is cheap:

```csharp
// Every shipped layer/accessory, harvested from loaded assets:
foreach (var l in Resources.FindObjectsOfTypeAll<Il2CppScheduleOne.AvatarFramework.AvatarLayer>())
    MelonLogger.Msg($"LAYER {l.Order,3} {l.Name} -> {l.AssetPath}");
foreach (var a in Resources.FindObjectsOfTypeAll<Il2CppScheduleOne.AvatarFramework.Accessory>())
    MelonLogger.Msg($"ACC   {a.Name} -> {a.AssetPath} blocksHair={a.ShouldBlockHair}");
// Or take the curated lists the character creator already holds:
//   ACSelection<AvatarLayer>.Options / ACSelection<Accessory>.Options   (Il2CppScheduleOne.AvatarFramework.Customization)
//   CharacterCreator.Instance.Presets  -> List<BasicAvatarSettings>
// Or scrape real characters:
foreach (var npc in Il2CppScheduleOne.NPCs.NPCManager.NPCRegistry)
    MelonLogger.Msg(npc.ID + " => " + npc.NPCData.Appearance.AvatarSettings.GetJson(false));
```

That last one is the highest-value move: **dump `GetJson()` for all ~80 shipped NPCs once, and
you have a corpus of every asset path the game actually uses, tagged by the character it belongs
to.** Uncle Nelson, the Fixer, the police officers, Ming, the sewer goblins — between them they
already cover scruffy, formal, uniformed and countercultural looks.

---

## 7. Behaviour system (`Il2CppScheduleOne.NPCs.Behaviour`)

### 7.1 `NPCBehaviour` — the controller

`public class NPCBehaviour : Il2CppFishNet.Object.NetworkBehaviour`

It is a **priority stack**, not a graph. Every `Behaviour` component on the NPC registers itself;
the highest-priority *enabled* one becomes `activeBehaviour`.

```csharp
Il2CppScheduleOne.NPCs.NPC              Npc              { public get; public set; }
Il2CppScheduleOne.NPCs.NPCScheduleManager ScheduleManager { public get; public set; }
List<Behaviour> behaviourStack     { public get; public set; }   // sorted by Priority
List<Behaviour> enabledBehaviours  { public get; public set; }
Behaviour       activeBehaviour    { public get; public set; }
System.Boolean  DEBUG_MODE         { public get; public set; }

// Direct handles the game itself keeps (these are the "always present" behaviours):
CoweringBehaviour         CoweringBehaviour;
RagdollBehaviour          RagdollBehaviour;
CallPoliceBehaviour       CallPoliceBehaviour;
GenericDialogueBehaviour  GenericDialogueBehaviour;
HeavyFlinchBehaviour      HeavyFlinchBehaviour;
FaceTargetBehaviour       FaceTargetBehaviour;
DeadBehaviour             DeadBehaviour;
UnconsciousBehaviour      UnconsciousBehaviour;
Behaviour                 SummonBehaviour;
ConsumeProductBehaviour   ConsumeProductBehaviour;
Il2CppScheduleOne.Combat.CombatBehaviour CombatBehaviour;
FleeBehaviour             FleeBehaviour;
StationaryBehaviour       StationaryBehaviour;
RequestProductBehaviour   RequestProductBehaviour;

public Behaviour GetBehaviour(System.String BehaviourName);
public T         GetBehaviour<T>();
public Behaviour GetEnabledBehaviour();
public System.Void AddEnabledBehaviour(Behaviour b);
public System.Void RemoveEnabledBehaviour(Behaviour b);
public System.Void SortBehaviourStack();

// Index-addressed network control (index = Behaviour.BehaviourIndex):
public System.Void EnableBehaviour_Server(System.Int32 behaviourIndex);
public System.Void DisableBehaviour_Server(System.Int32 behaviourIndex);
public System.Void ActivateBehaviour_Server(System.Int32 behaviourIndex);
public System.Void DeactivateBehaviour_Server(System.Int32 behaviourIndex);
public System.Void PauseBehaviour_Server(System.Int32 behaviourIndex);
public System.Void ResumeBehaviour_Server(System.Int32 behaviourIndex);
//  …and matching *_Client(NetworkConnection conn, int behaviourIndex) TargetRpc/ObserversRpc forms.

public System.Void Summon(System.String buildingGUID, System.Int32 doorIndex, System.Single duration);  // ServerRpc
public System.Void ConsumeProduct(Il2CppScheduleOne.Product.ProductItemInstance product, System.Boolean removeFromInventory = false);
public virtual System.Void OnTick();
public virtual System.Void OnUncappedMinutePass();
public virtual System.Void OnDie();
public System.Void OnKnockOut();
public System.Void OnRevive();
```

### 7.2 `Behaviour` — the base

`public class Behaviour : Il2CppFishNet.Object.NetworkBehaviour`

```csharp
static System.Int32 MAX_CONSECUTIVE_PATHING_FAILURES;
System.Boolean EnabledOnAwake;
System.String  Name;              // key for NPCBehaviour.GetBehaviour(string)
System.Int32   Priority;          // higher wins the stack
System.Int32   BehaviourIndex;    // network address
System.Boolean _canUseUmbrellaDuringBehaviour;
System.Boolean Enabled, Started, Active;
NPCBehaviour   beh;
NPC            Npc { get; }
UnityEngine.Events.UnityEvent onEnable, onDisable, onBegin, onEnd;
System.Int32   consecutivePathingFailures;

public virtual System.Void Enable();     public System.Void Enable_Server();     public System.Void Enable_Networked();
public virtual System.Void Disable();    public System.Void Disable_Server();    public System.Void Disable_Networked(NetworkConnection conn);
public virtual System.Void Activate();   public System.Void Activate_Server(NetworkConnection conn);
public virtual System.Void Deactivate(); public System.Void Deactivate_Server(); public System.Void Deactivate_Networked(NetworkConnection conn);
public virtual System.Void Pause();      public System.Void Pause_Server();
public virtual System.Void Resume();     public System.Void Resume_Server();
public virtual System.Void BehaviourUpdate();       // per frame while active
public virtual System.Void BehaviourLateUpdate();
public virtual System.Void OnActiveTick();          // per network tick while active
public virtual System.Void OnActiveUncappedMinutePass();
public          System.Void SetDestination(Il2CppScheduleOne.Management.ITransitEntity transitEntity, System.Boolean teleportIfFail = true);
public virtual System.Void SetDestination(UnityEngine.Vector3 position, System.Boolean teleportIfFail = true, System.Single successThreshold = 1f);
public virtual System.Void WalkCallback(Il2CppScheduleOne.NPCs.NPCMovement+WalkResult result);
public          System.Void SetCanUseUmbrellaDuringBehaviour(System.Boolean canUse);
public          System.Void UpdateGameObjectName();
```

**Lifecycle:** `Enable` puts it in the running set → `NPCBehaviour` picks the highest-priority
enabled one → `Activate` on that one → `BehaviourUpdate`/`OnActiveTick` fire → something higher
priority enables → `Pause`/`Deactivate` → later `Resume`. `Disable` removes it entirely.
`SetDestination` with `teleportIfFail` is the pathing escape hatch that keeps NPCs from getting
stuck; it counts `consecutivePathingFailures` up to `MAX_CONSECUTIVE_PATHING_FAILURES`.

### 7.3 Every concrete behaviour

Direct subclasses of `Behaviour` (42, including `Combat.CombatBehaviour`):

| Type | What it does | Useful for |
|---|---|---|
| `IdleBehaviour` | Walks to `IdlePoint` (a `Transform`) and stands there, optional facing dir. `IsAtIdleLocation()`. | **walking to a spot / parking an NPC** |
| `StationaryBehaviour` | Freezes the NPC where it stands. No destination. | holding position |
| `ScheduleBehaviour` | Wrapper that hands control to `NPCScheduleManager`; the default low-priority behaviour. | **the "live your daily life" behaviour** |
| `FootPatrolBehaviour` | Walks a `FootPatrolRoute` as part of a `PatrolGroup`; optional flashlight (`FLASHLIGHT_ASSET_PATH`, `FLASHLIGHT_MIN_TIME`/`MAX_TIME`), `MOVE_SPEED`, `SetGroup`, `IsReadyToAdvance()`. | **walking a route** `[PI]` |
| `SentryBehaviour` | Stands at a `Law.SentryLocation`, patrols its `SentryRoute` points, `AssignLocation`/`UnassignLocation`, `BodySearchChance`, flashlight. | static guard posts `[PI]` |
| `VehiclePatrolBehaviour` | **Drives** a `VehiclePatrolRoute` using `Vehicles.AI.VehicleAgent`. `DriveTo(Vector3)`, `NavigationCallback(ENavigationResult)`, `SetRoute`, `StartPatrol`, `isDriving`, `aggressiveDrivingEnabled`. | **driving** `[HD]` `[PI]` |
| `VehiclePursuitBehaviour` | Drives after a `Player`. `AssignTarget(Player)`, `DriveTo`, `UpdateDestination`, `CheckExitVehicle`, `SetAggressiveDriving`, vision integration. | **driving toward a moving target** `[HD]` `[PI]` |
| `Combat.CombatBehaviour` | Full melee/ranged combat: target acquisition, weapon selection, repositioning, searching. | fighting |
| `PursuitBehaviour : CombatBehaviour` | Police chase + arrest: `ARREST_RANGE`, `ARREST_TIME`, `MOVE_SPEED_CHASE/ARRESTING/INVESTIGATING`, arrest circle, baton/taser/gun. | **arrest flow** `[PI]` |
| `BodySearchBehaviour` | Officer searches a player: `BODY_SEARCH_RANGE`, `MAX_SEARCH_TIME`, `MaxStealthLevel`, `ConcludeSearch(bool clear)`, `Escalate()`. | `[PI]` |
| `CheckpointBehaviour` | Mans a `Police.RoadCheckpoint`; searches vehicles (`StartSearch(NetworkObject targetVehicle, NetworkObject initiator)`, `DoesVehicleContainIllicitItems()`, `trunkOpened`). | `[PI]` |
| `CallPoliceBehaviour` | NPC pulls out a phone (`PhonePrefab`, `PhoneCallPopup`, `CallSound`) and reports a `Law.Crime` over `CALL_POLICE_TIME`. `SetData(NetworkObject player, Crime crime)`, `FinalizeCall()`. | `[PI]` |
| `FleeBehaviour` | Runs away. `EFleeMode { Entity=0, Point=1 }`, `SetEntityToFlee(NetworkObject)`, `SetPointToFlee(Vector3)`, `FLEE_DIST_MIN/MAX`, `FLEE_SPEED`, `StartFlee()`, `Stop()`. | **fleeing** |
| `CoweringBehaviour` | Crouch-and-cower in place. `SetCowering(bool)`. | panic |
| `FaceTargetBehaviour` | Turns to face a player or a point for a countdown. `ETargetType { Player=0, Position=1 }`, `SetTarget(NetworkObject player, Single countDown = 5)`, `SetTarget(Vector3, Single)`. | conversation framing |
| `GenericDialogueBehaviour` | Holds the NPC still and faces the player while a conversation runs. `SetTargetPlayer(NetworkObject)`, `FaceConversationTarget`. | **talking** |
| `RequestProductBehaviour` | Customer walks up to the player, asks to buy, follows, and opens the handover. `EState { InitialApproach=0, FollowPlayer=1 }`, `CONVERSATION_RANGE`, `FOLLOW_MAX_RANGE`, `TicksBeforeAskAgain`, `AssignTarget`, `Follow()`, `RequestAccepted/Rejected()`, `HandoverClosed(EHandoverOutcome, List<ItemInstance>, Single askingPrice)`. | **following the player + conducting a transaction** `[SC]` |
| `CustomerAttendDealBehaviour` | Customer walks to a `DeliveryLocation` for a scheduled deal. `SetContract(Contract)`, `EnsureNPCHasEnoughCash()`, `CheckWarp()`, `DestinationThreshold`, `WalkSpeedMultiplier`. | **transaction** `[SC]` |
| `DealerAttendDealBehaviour` | Dealer side of the same. `AssignContract(Contract)`, `BeginHandover()`, `IsCustomerReadyForHandover()`, `GetStandPosition()`, `GetDirectionToFace()`. | **transaction** `[SC]` |
| `ConsumeProductBehaviour` | NPC smokes/snorts/eats a product. `SetProduct(ProductItemInstance, bool removeFromInventory)`, `ConsumeWeed/Meth/Cocaine/Shrooms()`, `ApplyEffects()`, joint/pipe/shroom equippable prefabs. | `[SC]` |
| `SmokeBreakBehaviour` | Walks to one of `SmokeBreakLocations` and smokes for `MinMaxSmokeBreak` minutes. | ambience |
| `GraffitiBehaviour` | Sprays a `WorldSpraySurface` over `_graffitiDurationInMinutes`, interruptible by `_interruptingBehaviours`. | cartel |
| `MoveItemBehaviour` | Employee logistics: walk to source, grab N, walk to destination, place. `EState { Idle=0, WalkingToSource=1, Grabbing=2, WalkingToDestination=3, Placing=4 }`, `Initialize(TransitRoute, ItemInstance template, Int32 maxMoveAmount = -1, Boolean skipPickup = false)`, `GetSaveData()`/`Load(MoveItemData)`. | **the closest existing template for a "go fetch/deliver" job** `[HD]` |
| `BagTrashCanBehaviour` | Cleaner bags a `TrashContainerItem`. | employees |
| `EmptyTrashGrabberBehaviour` | Cleaner empties the grabber into a can. | employees |
| `PickUpTrashBehaviour` | Cleaner picks up a loose `TrashItem`. | employees |
| `DisposeTrashBagBehaviour` | Cleaner carries a `TrashBag` to disposal (`TRASH_BAG_ASSET_PATH`). | employees |
| `PackagingStationBehaviour` | Packager works a `PackagingStation`. | employees |
| `BrickPressBehaviour` | Packager works a `BrickPress`. | employees |
| `StartChemistryStationBehaviour` | Chemist loads + starts a `ChemistryStation` (fills a `Beaker` from a `StationRecipe`). | employees |
| `StartMixingStationBehaviour` | Chemist starts a `MixingStation`. | employees |
| `StartLabOvenBehaviour` / `FinishLabOvenBehaviour` | Chemist pours into / harvests a `LabOven`. | employees |
| `StartCauldronBehaviour` | Chemist starts a `Cauldron`. | employees |
| `StartDryingRackBehaviour` / `StopDryingRackBehaviour` | Botanist loads / unloads a `DryingRack`. | employees |
| `UseSpawnStationBehaviour` | Botanist uses a `MushroomSpawnStation`. | employees |
| `GrowContainerBehaviour` | Base for all pot/bed work. `EState { Idle=0, Walking=1, GrabbingSupplies=2, PerformingAction=3 }`, `AssignAndEnable(GrowContainer)`, supply-fetch + action loop, `GetActionDuration()`, `GetActionEquippable()`, `GetAnimationBool()`. | employees |
| `SewerGoblinRetrieveBehaviour` | Sewer goblin runs at the player to snatch something. `PROXIMITY_THRESHOLD`, `TIMEOUT`, `onRetrieveComplete`/`onRetrieveCancelled`. | **a clean, minimal "chase the player and do a thing" template** |
| `HeavyFlinchBehaviour` | `Flinch()` for `FLINCH_DURATION`. | hit reactions |
| `RagdollBehaviour` | Holds the NPC ragdolled; `Seizure`, `SeizureForce`. | knockdown |
| `UnconsciousBehaviour` | Lies unconscious, optional snoring (`SnoreInterval`, `SnoreChance`). | KO |
| `DeadBehaviour` | Dead state; `EnterMedicalCentre()`, `IsInMedicalCenter`, revive timer. | death |

`GrowContainerBehaviour` subclasses (8): `AddSoilToGrowContainerBehaviour`,
`ApplyAdditiveToGrowContainerBehaviour`, `ApplySpawnToMushroomBedBehaviour`,
`HarvestMushroomBedBehaviour`, `HarvestPotBehaviour`, `SowSeedInPotBehaviour`,
`WaterPotBehaviour`, and `MistMushroomBedBehaviour : WaterPotBehaviour`.

Route/group helpers (not behaviours):

```csharp
public class FootPatrolRoute : UnityEngine.MonoBehaviour
{ System.String RouteName; UnityEngine.Color PathColor; Il2CppReferenceArray<UnityEngine.Transform> Waypoints; System.Int32 StartWaypointIndex;
  public System.Void UpdateWaypoints(); }

public class VehiclePatrolRoute : UnityEngine.MonoBehaviour
{ System.String RouteName; Il2CppReferenceArray<UnityEngine.Transform> Waypoints; System.Int32 StartWaypointIndex; }

public class PatrolGroup : Il2CppSystem.Object
{ List<NPC> Members; FootPatrolRoute Route; System.Int32 CurrentWaypoint;
  public System.Void AdvanceGroup(); public System.Void DisbandGroup();
  public UnityEngine.Vector3 GetDestination(NPC member); public UnityEngine.Vector3 GetMemberOffset(NPC member);
  public System.Boolean IsGroupReadyToAdvance(); public System.Boolean IsPaused(); }
```

**Answers to the tagged questions:**
- Walking a route → `FootPatrolBehaviour` + `FootPatrolRoute` (+ `PatrolGroup` for formations),
  or `NPCSignal_WalkToLocation` if it's schedule-driven.
- Entering/leaving buildings → `NPCEvent_StayInBuilding` (schedule) or `NPC.EnterBuilding(guid, doorIndex)` directly.
- Driving → `VehiclePatrolBehaviour` / `VehiclePursuitBehaviour` / `NPCSignal_DriveToCarPark`.
- Following the player → `RequestProductBehaviour` (state `FollowPlayer`) or `SewerGoblinRetrieveBehaviour`.
- Fleeing → `FleeBehaviour`.
- Conducting a transaction → `RequestProductBehaviour` (walk-up sale) or
  `CustomerAttendDealBehaviour` + `DealerAttendDealBehaviour` (scheduled deal).

---

## 8. Schedules (`Il2CppScheduleOne.NPCs.Schedules`)

### 8.1 `NPCScheduleManager : UnityEngine.MonoBehaviour`

Reached via `npc.Behaviour.ScheduleManager`. Every schedule entry is an `NPCAction`
**component sitting on a child GameObject of the NPC** — that is the authoring model.

```csharp
static NPCActionOrderByDescending orderByDescending;
System.Boolean ScheduleEnabled     { public get; public set; }
System.Boolean CurfewModeEnabled   { public get; public set; }
System.Boolean DEBUG_MODE;
NPCAction        ActiveAction        { public get; public set; }
List<NPCAction>  PendingActions      { public get; public set; }
List<NPCAction>  ActionList          { public get; public set; }   // all actions, sorted
List<NPCAction>  ActionsAwaitingStart{ public get; public set; }
List<Il2CppScheduleOne.NPCs.Other.NPCDiscreteAction> DiscreteActions { public get; }
NPC              Npc                 { public get; public set; }
Il2CppReferenceArray<UnityEngine.GameObject> EnabledDuringCurfew;
Il2CppReferenceArray<UnityEngine.GameObject> EnabledDuringNoCurfew;
System.Int32     lastProcessedTime;
Il2CppScheduleOne.GameTime.TimeManager Time { public get; }

public System.Void InitializeActions();        // collects child NPCAction components, sorts by start time
public System.Void EnableSchedule();
public System.Void DisableSchedule();
public System.Void SetCurfewModeEnabled(System.Boolean enabled);
public System.Void EnforceState();
public System.Void EnforceState(System.Boolean initial = false);   // snap the NPC to whatever it should be doing NOW
public System.Void StartAction(NPCAction action);
public System.Void UpdateActions();
public List<NPCAction> GetActionsOccurringAt(System.Int32 time);
public List<NPCAction> GetActionsTotallyOccurringWithinRange(System.Int32 min, System.Int32 max, System.Boolean checkShouldStart);
public virtual System.Void OnMinPass();
public virtual System.Void OnTick();
public virtual System.Void CurfewEnabled();
public virtual System.Void CurfewDisabled();
public System.Void LocalPlayerSpawned();
```

### 8.2 `NPCAction : Il2CppFishNet.Object.NetworkBehaviour`

```csharp
static System.Int32 MAX_CONSECUTIVE_PATHING_FAILURES;
System.Int32 priority;                 // and read-only Priority
System.Int32 StartTime      { public get; public set; }   // 24h clock as an int (e.g. 1430 = 14:30) — matches TimeManager convention
System.Boolean _canUseUmbrella;
NPC npc;  NPCScheduleManager schedule;  NPCMovement movement { get; }
Il2CppSystem.Action onEnded;
System.String  ActionName { get; }
System.Boolean IsEvent    { get; }   // true for NPCEvent
System.Boolean IsSignal   { get; }   // true for NPCSignal
System.Boolean IsActive   { get; }
System.Boolean HasStarted { public get; public set; }

public virtual System.Boolean ShouldStart();
public virtual System.Void SetStartTime(System.Int32 startTime);
public virtual System.Int32  GetEndTime();
public virtual System.String GetName();
public virtual System.String GetTimeDescription();
public virtual System.Void OnStart();      Started();      LateStarted();
public virtual System.Void ActiveUpdate(); OnActiveTick(); OnActiveMinPass(); MinPassed(); PendingMinPassed();
public virtual System.Void Interrupt();    Resume();       ResumeFailed();
public virtual System.Void JumpTo();       // fast-forward: snap to the end state (used when time is skipped / on load)
public virtual System.Void Skipped();
public virtual System.Void End();
public          System.Void SetDestination(UnityEngine.Vector3 position, System.Boolean teleportIfFail = true);
public virtual System.Void WalkCallback(NPCMovement+WalkResult result);
```

**`NPCEvent` = a timed span. `NPCSignal` = a one-shot trigger with a max duration.**

```csharp
class NPCEvent : NPCAction  { System.Int32 Duration; System.Int32 EndTime;
                              public System.Void ApplyDuration(); public System.Void ApplyEndTime(); }
class NPCSignal: NPCAction  { System.Int32 MaxDuration; System.Boolean StartedThisCycle; }
```

### 8.3 Every schedule action type

`NPCEvent` subclasses:

| Type | Fields | Behaviour |
|---|---|---|
| `NPCEvent_LocationBasedAction` | `Transform Destination`, `Boolean FaceDestinationDir`, `Single DestinationThreshold`, `Boolean WarpIfSkipped`, `Boolean IsActionStarted`, `UnityEvent onStartAction`/`onEndAction` | Walk to a transform, fire `onStartAction`, hold for `Duration`, fire `onEndAction`. **The generic "go here and do a thing" entry — hook `onStartAction` and you have arbitrary scheduled behaviour with zero new types.** |
| `NPCEvent_LocationDialogue` | same destination fields + dialogue | Walk to a transform and play a conversation there. |
| `NPCEvent_Sit` | destination fields + `StartAction(NetworkConnection conn, Int32 seatIndex)` | Walk to an `AvatarSeat` and sit. |
| `NPCEvent_StayInBuilding` | `Map.NPCEnterableBuilding Building`, `Doors.StaticDoor Door`, `Boolean IsEntering`, `Boolean InBuilding`; `EnterBuilding(Int32 doorIndex)`, `ExitBuilding()`, `GetDoor(out Int32 doorIndex)`, `GetEntryPoint()`, `PlayEnterAnimation()`, `CancelEnter()` | **Enter a building, disappear for the duration, come back out.** This is how NPCs "go home". |
| `NPCEvent_CartelGoonExit : NPCEvent_StayInBuilding` | `Cartel.CartelGoon Goon`, `FindExitBuilding()` | Goon despawns into the nearest exit building. |
| `NPCEvent_Conversate` | `ConversationLocation Location`, `EVOLineType[] ConversationLines`, `String[] AnimationTriggers`, `IsConversating`, `IsWaiting`, `OnWaitStart`/`OnWaitEnd`, `DESTINATION_THRESHOLD`, `TIME_BEFORE_WAIT_START`, `StandPoint`, `CanConversationStart()` | Two NPCs meet at a `ConversationLocation` and talk. |

`NPCSignal` subclasses:

| Type | Fields | Behaviour |
|---|---|---|
| `NPCSignal_WalkToLocation` | `Transform Destination`, `Boolean FaceDestinationDir`, `Single DestinationThreshold`, `Boolean WarpIfSkipped`, `ReachedDestination()` | Walk somewhere once. |
| `NPCSignal_DriveToCarPark` | `Map.ParkingLot ParkingLot`, `Vehicles.LandVehicle Vehicle`, `Boolean OverrideParkingType`, `Vehicles.EParkingAlignment ParkingType`, `isAtDestination`, `timeInVehicle`, `timeAtDestination`; `DriveCallback(VehicleAgent+ENavigationResult result)`, `GetWalkDestination()`, `GetParkingType()`, `Park()`, `CheckValidForStart()` | **NPC gets into a car, drives to a parking lot, parks, gets out.** `[HD]` **This is the complete, shipped, end-to-end NPC-driving flow.** |
| `NPCSignal_UseATM` | `Money.ATM ATM`, `destinationThreshold`, `Purchase()` | Walk to an ATM, withdraw. |
| `NPCSignal_UseVendingMachine` | `ObjectScripts.VendingMachine MachineOverride`/`TargetMachine`, `CheckItem()`, `GetTargetMachine()`, `ItemWasStolen()`, `Purchase()` | Walk to a vending machine, buy. |

Also `ConversationLocation : MonoBehaviour` — `Transform[] StandPoints`, `List<NPC> NPCs`,
`NPCsReady`, `GetStandPoint(NPC)`, `GetOtherNPC(NPC)`, `SetNPCReady(NPC, bool)`.

**Authoring a schedule from a mod:**

1. Create a child `GameObject` under the NPC.
2. `AddComponent<NPCEvent_LocationBasedAction>()` (or whichever).
3. Set `StartTime` (int clock), `Duration` (minutes) / `MaxDuration`, `priority`, and the
   type-specific fields (`Destination`, `Building`, `ParkingLot`, …).
4. Call `npc.Behaviour.ScheduleManager.InitializeActions()` to re-collect and re-sort, then
   `EnforceState(true)` to snap the NPC into whatever should be running now.

**Time binding:** `StartTime` is an int on the game clock; `NPCScheduleManager.OnMinPass` diffs
`lastProcessedTime` and calls `ShouldStart()`/`StartAction`/`End`. Actions overlapping a time skip
get `JumpTo()` or `Skipped()` rather than being simulated.

---

## 9. Movement (`Il2CppScheduleOne.NPCs.NPCMovement`)

`public class NPCMovement : Il2CppFishNet.Object.NetworkBehaviour` — 90 properties, 77 methods.

**Pathfinding: NPCs on foot use `UnityEngine.AI.NavMeshAgent`, not A\*.** The A\*
(`Il2CppPathfinding`) graphs are for *vehicles* only (see API-VEHICLES.md §3).

```csharp
UnityEngine.AI.NavMeshAgent Agent { public get; public set; }
NPCSpeedController          SpeedController { public get; public set; }
UnityEngine.CapsuleCollider CapsuleCollider;
NPCAnimation                Animation;
Il2CppScheduleOne.Tools.SmoothedVelocityCalculator VelocityCalculator;
Il2CppScheduleOne.Dragging.Draggable RagdollDraggable;  UnityEngine.Collider RagdollDraggableCollider;
NPC npc;
```

### 9.1 Destinations

```csharp
public System.Void SetDestination(UnityEngine.Vector3 pos);
public System.Void SetDestination(UnityEngine.Transform target);
public System.Void SetDestination(Il2CppScheduleOne.Management.ITransitEntity entity);
public System.Void SetDestination(UnityEngine.Vector3 pos,
                                  Il2CppSystem.Action<NPCMovement+WalkResult> callback = null,
                                  System.Single maximumDistanceForSuccess = 1f,
                                  System.Single cacheMaxDistSqr = 1f);
public System.Void SetDestination(UnityEngine.Vector3 pos,
                                  Il2CppSystem.Action<NPCMovement+WalkResult> callback = null,
                                  System.Boolean interruptExistingCallback = true,
                                  System.Single successThreshold = 1f,
                                  System.Single cacheMaxDistSqr = 1f);
public System.Void EndSetDestination(NPCMovement+WalkResult result);
public System.Void UpdateDestination();
public System.Void Stop();
public System.Void PauseMovement();
public System.Void ResumeMovement();

UnityEngine.Vector3 CurrentDestination { public get; public set; }
System.Boolean      HasDestination     { public get; public set; }
System.Boolean      IsMoving           { public get; }
System.Boolean      IsPaused           { public get; public set; }
UnityEngine.Vector3 Velocity           { public get; }
UnityEngine.Vector3 FootPosition       { public get; }

public enum WalkResult { Failed = 0, Interrupted = 1, Stopped = 2, Partial = 3, Success = 4 }
```

### 9.2 Reachability & navmesh sampling

```csharp
public System.Boolean CanGetTo(UnityEngine.Vector3 position, System.Single proximityReq = 1f);
public System.Boolean CanGetTo(Il2CppScheduleOne.Management.ITransitEntity entity, System.Single proximityReq = 1f);
public System.Boolean CanGetTo(UnityEngine.Vector3 position, System.Single proximityReq, out UnityEngine.AI.NavMeshPath& path);
public UnityEngine.AI.NavMeshPath GetPathTo(UnityEngine.Vector3 position, System.Single proximityReq = 1f);
public System.Boolean GetClosestReachablePoint(UnityEngine.Vector3 targetPosition, out UnityEngine.Vector3& closestPoint);
public System.Boolean IsAsCloseAsPossible(UnityEngine.Vector3 location, System.Single distanceThreshold = 0.5f);
public System.Boolean IsNPCPositionValid(UnityEngine.Vector3 position);
public System.Boolean SmartSampleNavMesh(UnityEngine.Vector3 position, out UnityEngine.AI.NavMeshHit& hit,
                                         System.Single minRadius = 1f, System.Single maxRadius = 10f, System.Int32 steps = 3);
public System.Boolean CanMove();
```

Path caching: `static System.Boolean USE_PATH_CACHE`, `NPCPathCache PathCache`, `cacheNextPath`,
`UpdateCache()`; plus a global `static Dictionary<Vector3,Vector3> cachedClosestReachablePoints`
with `CLOSEST_REACHABLE_POINT_CACHE_MAX_SQR_OFFSET`.

```csharp
public class NPCPathCache
{ List<NPCPathCache+PathCache> Paths;                                       // PathCache { Vector3 Start; Vector3 End; NavMeshPath Path; }
  public System.Void AddPath(UnityEngine.Vector3 start, UnityEngine.Vector3 end, UnityEngine.AI.NavMeshPath path);
  public UnityEngine.AI.NavMeshPath GetPath(UnityEngine.Vector3 start, UnityEngine.Vector3 end, System.Single sqrMaxDistance); }
```

### 9.3 Warping

```csharp
public System.Void Warp(UnityEngine.Vector3 position);
public System.Void Warp(UnityEngine.Transform target);
public System.Void ReceiveWarp(UnityEngine.Vector3 position);   // ObserversRpc target
public System.Void WarpToNavMesh();
public System.Void SetAgentEnabled(System.Boolean enabled);
```

`NPCManager.NPCWarpPoints` + `GetOrderedDistanceWarpPoints(origin)` give you the shipped set of
"legal places to teleport an NPC to".

### 9.4 Speed control

Never write `Agent.speed` directly — the game uses a **priority stack**:

```csharp
public class NPCSpeedController : UnityEngine.MonoBehaviour
{
    static System.Single DefaultNormalizedSpeed;
    System.Single SpeedMultiplier { public get; public set; }
    NPCSpeedController+SpeedControl ActiveSpeedControl { public get; public set; }
    List<NPCSpeedController+SpeedControl> speedControlStack;
    public System.Void AddSpeedControl(SpeedControl control);
    public System.Void RemoveSpeedControl(System.String id);
    public SpeedControl GetSpeedControl(System.String id);
    public System.Boolean DoesSpeedControlExist(System.String id);
    public System.Void UpdateActiveSpeedControl();
}
public class NPCSpeedController+SpeedControl
{ System.String id; System.Int32 priority; System.Single speed;
  public .ctor(System.String id, System.Int32 priority, System.Single speed); }
```

Usage: `npc.Movement.SpeedController.AddSpeedControl(new SpeedControl("mymod_sprint", 10, 1f))`
and remove by the same id. `NPCMovement.WalkSpeed`/`RunSpeed` are read-only and come from
`NPCData.Movement.WalkSpeed`/`SprintSpeed`. There is also a plain
`NPCMovement.MoveSpeedMultiplier`.

### 9.5 Agent type, stance, doors, ladders, ragdoll

```csharp
public enum EAgentType { Humanoid = 0, BigHumanoid = 1, IgnoreCosts = 2 }
public enum EStance    { None = 0, Stanced = 1 }
public System.Void SetAgentType(NPCMovement+EAgentType type);
public System.Void SetStance(NPCMovement+EStance stance);
public System.Void SetAngularSpeedMultiplier(System.Single multiplier);
public System.Void SetGravityMultiplier(System.Single multiplier);
public System.Void FaceDirection(UnityEngine.Vector3 forward, System.Single lerpTime = 0.5f);
public System.Void FacePoint(UnityEngine.Vector3 point, System.Single lerpTime = 0.5f);
public System.Void SetSeat(Il2CppScheduleOne.AvatarFramework.Animation.AvatarSeat seat);
public System.Void TraverseLadder(Il2CppScheduleOne.Map.Ladder ladder);
public System.Void CancelTraverseLadder();
public System.Void ActivateRagdoll(UnityEngine.Vector3 forcePoint, UnityEngine.Vector3 forceDir, System.Single forceMagnitude);
public System.Void ActivateRagdoll_Server();
public System.Void DeactivateRagdoll();
public System.Boolean CanRecoverFromRagdoll();
public System.Void SetRagdollDraggable(System.Boolean draggable);
public System.Void Stumble();
System.Boolean ObstacleAvoidanceEnabled;  UnityEngine.AI.ObstacleAvoidanceType DefaultObstacleAvoidanceType;
System.Boolean SlipperyMode;              System.Boolean Disoriented;
```

**Doors:** there is no door API on `NPCMovement`. Door permission is the flag
`NPCData.Movement.CanOpenDoors`; door *traversal* is navmesh links plus
`Il2CppScheduleOne.Doors.StaticDoor`, and building transitions go through
`NPC.EnterBuilding(buildingGUID, doorIndex)` / `NPCEnterableBuilding.GetClosestDoor(pos, useableOnly)`.

**Vehicle collisions:** `onHitByCar` (`UnityEvent<LandVehicle>`), `TimeSinceHitByCar`,
`VehicleRunoverSpeed`, `VehicleRunoverRelativeVelocityThreshold_Sqr`, `VehicleImpactCooldown`,
`VehicleImpactForceMultiplier`, `SkateboardRunoverSpeed`, `CheckHit(...)`. `[HD]` Relevant if a
hired driver mows down pedestrians.

---

## 10. Dialogue (`Il2CppScheduleOne.Dialogue`)

### 10.1 The pieces

- **`DialogueContainer : UnityEngine.ScriptableObject`** — a whole conversation graph:
  `List<DialogueNodeData> DialogueNodeData`, `List<BranchNodeData> BranchNodeData`,
  `List<NodeLinkData> NodeLinks`, `Boolean AllowExit`.
  Lookups: `GetDialogueNodeByLabel(string)`, `GetDialogueNodeByGUID(string)`,
  `GetBranchNodeByLabel(string)`, `GetBranchNodeByGUID(string)`,
  `GetLink(string baseChoiceOrOptionGUID)`, `SetAllowExit(bool)`.
- **`DialogueNodeData`** — `Guid`, `DialogueText`, `DialogueNodeLabel`, `Vector2 Position`,
  `DialogueChoiceData[] choices`, `VoiceOver.EVOLineType VoiceLine`, `GetCopy()`.
- **`DialogueChoiceData`** — `Guid`, `ChoiceText`, `ChoiceLabel`, `Boolean ShowWorldspaceDialogue`, `GetCopy()`.
- **`BranchNodeData`** — `Guid`, `BranchLabel`, `Vector2 Position`, `BranchOptionData[] options`.
- **`NodeLinkData`** — `BaseDialogueOrBranchNodeGuid`, `BaseChoiceOrOptionGUID`, `TargetNodeGuid`.
- **`DialogueDatabase : ScriptableObject`** — the NPC's *ad-lib* lines, keyed:
  `List<DialogueModule> Modules`, `List<Entry> GenericEntries`, `Initialize(DialogueHandler)`,
  `GetLine(EDialogueModule, string key)`, `GetChain(EDialogueModule, string key)`,
  `HasLine`/`HasChain`, `GetModule(EDialogueModule)`.
- **`EDialogueModule`** = `Generic=0, Greetings=1, Reactions=2, Customer=3, Police=4, Supplier=5, Dealer=6, CartelGoon=7`.
- **`DialogueModule : MonoBehaviour`** — `ModuleType`, `List<Entry> Entries`,
  `GetLine(key)`, `GetChain(key)`, `GetEntry(key)`, `HasLine`, `HasChain`, `GetWordCount()`.
- **`Entry` (struct)** — `Key`, `DialogueChain[] Chains`, `GetRandomChain()`, `GetRandomLine()`.
- **`DialogueChain`** — `String[] Lines`, `GetMessageChain()` (converts to a phone message chain).
- **`DialogueList`** — `String[] Lines`, `GetRandomLine()`.
- **`VocalReactionDatabase`** — keyed `Entry { Key, String[] Reactions, GetRandomReaction() }`.

### 10.2 `DialogueHandler : UnityEngine.MonoBehaviour`

The runtime driver. `npc.DialogueHandler`.

```csharp
static System.Single TimePerChar, WorldspaceDialogueMinDuration, WorldspaceDialogueMaxDuration;
static DialogueContainer ActiveDialogue     { public get; public set; }
static DialogueNodeData  ActiveDialogueNode { public get; public set; }

Il2CppSystem.Action OnDialogueEnd;
UnityEngine.Transform LookPosition;
Il2CppScheduleOne.UI.WorldspaceDialogueRenderer WorldspaceRend;
List<DialogueChoiceData> CurrentChoices;
Il2CppReferenceArray<DialogueEvent> DialogueEvents;     // DialogueEvent { DialogueContainer Dialogue; UnityEvent onDialogueEnded; DialogueNodeEvent[] NodeEvents; }
UnityEngine.Events.UnityEvent onConversationStart;
UnityEngine.Events.UnityEvent<System.String> onDialogueNodeDisplayed;
UnityEngine.Events.UnityEvent<System.String> onDialogueChoiceChosen;
List<DialogueContainer> dialogueContainers;             // everything this NPC can say
System.Boolean IsDialogueInProgress { public get; public set; }
DialogueDatabase Database { public get; public set; }
List<DialogueModule> RuntimeModules { public get; public set; }
NPC NPC { public get; public set; }
Il2CppScheduleOne.UI.DialogueCanvas canvas { get; }

public System.Void Initialize(Il2CppScheduleOne.NPCs.Framework.NPCData npcData);
public System.Void StartDialogue(DialogueContainer container);
public System.Void StartDialogue(DialogueContainer dialogueContainer, System.Boolean enableDialogueBehaviour = true,
                                 System.String entryNodeLabel = "ENTRY");
public System.Void StartDialogue(System.String dialogueContainerName, System.Boolean enableDialogueBehaviour = true,
                                 System.String entryNodeLabel = "ENTRY");
public virtual System.Void EndDialogue();
public System.Void ShowNode(DialogueNodeData node);
public System.Void ChoiceSelected(System.Int32 choiceIndex);
public System.Void ContinueSubmitted();
public System.Void EvaluateBranch(BranchNodeData node);
public System.Void CreateTempLink(System.String baseNodeGUID, System.String baseOptionGUID, System.String targetNodeGUID);
public NodeLinkData GetLink(System.String baseChoiceOrOptionGUID);
public virtual System.Int32   CheckBranch(System.String branchLabel);
public virtual System.Boolean CheckChoice(System.String choiceLabel, out System.String& invalidReason);
public virtual System.Void    ChoiceCallback(System.String choiceLabel);
public virtual System.Void    DialogueCallback(System.String dialogueLabel);
public virtual System.Boolean ShouldChoiceBeShown(System.String choiceLabel);
public virtual System.Void    ModifyChoiceList(System.String dialogueLabel, ref List<DialogueChoiceData>& existingChoices);
public virtual System.String  ModifyChoiceText(System.String choiceLabel, System.String choiceText);
public virtual System.String  ModifyDialogueText(System.String dialogueLabel, System.String dialogueText);
public virtual DialogueNodeData FinalizeDialogueNode(DialogueNodeData data);
public System.Void OverrideShownDialogue(System.String _overrideText);
public System.Void StopOverride();
public virtual System.Void PlayReaction(System.String key, System.Single duration, System.Boolean network);
public virtual System.Void PlayReaction_Local(System.String key);
public virtual System.Void PlayReaction_Networked(System.String key);
public virtual System.Void ShowWorldspaceDialogue(System.String text, System.Single duration);
public virtual System.Void ShowWorldspaceDialogue_5s(System.String text);
public virtual System.Void HideWorldspaceDialogue();
public virtual System.Void Hovered();
public virtual System.Void Interacted();
public System.Void SkipNextDialogueBehaviourEnd();
```

Subclasses: `DialogueHandler_Customer`, and `ControlledDialogueHandler` →
`DialogueHandler_Police` `[PI]`, `DialogueHandler_EstateAgent`, `DialogueHandler_VehicleSalesman`.

### 10.3 `DialogueController : UnityEngine.MonoBehaviour` — the extension point

**This is the class a mod should use to add conversation options to an existing NPC.**

```csharp
static System.Single GreetingCooldown, RainyGreetingThreshold, RainyGreetingChance;
Il2CppScheduleOne.Interaction.InteractableObject IntObj;
DialogueContainer GenericDialogue;
System.Boolean DialogueEnabled, UseDialogueBehaviour;
List<DialogueController+DialogueChoice>   Choices;
List<DialogueController+GreetingOverride> GreetingOverrides;
DialogueContainer OverrideContainer;
NPC npc;  DialogueHandler handler;

public virtual System.Int32 AddDialogueChoice(DialogueController+DialogueChoice data, System.Int32 priority = 0);
public virtual System.Int32 AddGreetingOverride(DialogueController+GreetingOverride data);
public List<DialogueController+DialogueChoice> GetActiveChoices();
public System.String GetActiveGreeting(out System.Boolean& playVO, out VoiceOver.EVOLineType& voLineType);
public virtual System.Boolean GetCustomGreeting(out System.String& greeting, out System.Boolean& playVO, out VoiceOver.EVOLineType& voLineType);
public System.Void SetOverrideContainer(DialogueContainer container);
public System.Void ClearOverrideContainer();
public System.Void SetDialogueEnabled(System.Boolean enabled);
public System.Void StartGenericDialogue(System.Boolean allowExit = true);
public virtual System.Boolean CanStartDialogue();
public virtual System.Boolean CheckChoice(System.String choiceLabel, out System.String& invalidReason);
public virtual System.Void    ChoiceCallback(System.String choiceLabel);
public virtual System.Boolean DecideBranch(System.String branchLabel, out System.Int32& index);
public virtual System.Void    ModifyChoiceList(System.String dialogueLabel, ref List<DialogueChoiceData>& existingChoices);
public virtual System.String  ModifyChoiceText(System.String choiceLabel, System.String choiceText);
public virtual System.String  ModifyDialogueText(System.String dialogueLabel, System.String dialogueText);

public class DialogueController+DialogueChoice
{
    System.Boolean    Enabled;
    System.String     ChoiceText;
    System.Boolean    ShowWorldspaceDialogue;
    DialogueContainer Conversation;
    UnityEngine.Events.UnityEvent onChoosen;                 // (sic — that is the real spelling)
    DialogueChoice+ShouldShowCheck shouldShowCheck;          // delegate: bool Invoke(bool enabled)
    DialogueChoice+IsChoiceValid   isValidCheck;             // delegate: bool Invoke(out string invalidReason)
    System.Int32      Priority;
    public System.Boolean IsValid(out System.String& invalidReason);
    public System.Boolean ShouldShow();
}
```

14 shipped `DialogueController` subclasses: `_ArmsDealer`, `_Billy`, `_Dan`, `_Dealer`,
`_Employee`, `_Fixer`, `_Jen`, `_Ming`, `_Oscar`, `_Police`, and four more not captured in the
truncated listing (see `03-subclasses.txt:1396`).

**How a mod adds a conversation:**

```csharp
var ctrl = npc.GetComponent<Il2CppScheduleOne.Dialogue.DialogueController>();
var choice = new Il2CppScheduleOne.Dialogue.DialogueController.DialogueChoice {
    Enabled = true,
    ChoiceText = "Want to earn some money driving?",
    ShowWorldspaceDialogue = true,
    Conversation = myDialogueContainer,     // ScriptableObject.CreateInstance<DialogueContainer>() + hand-built nodes/links
    Priority = 100
};
choice.onChoosen.AddListener(new System.Action(OnHireClicked));
ctrl.AddDialogueChoice(choice, 100);
```

For simple one-liners with no graph you can skip `DialogueContainer` entirely and use
`npc.ShowWorldSpaceDialogue(text, duration)` or
`npc.DialogueHandler.ShowWorldspaceDialogue_5s(text)`.

---

## 11. Relationships (`Il2CppScheduleOne.NPCs.Relation`)

```csharp
public enum ERelationshipCategory { Hostile = 0, Unfriendly = 1, Neutral = 2, Friendly = 3, Loyal = 4 }

public class RelationshipCategory
{
    static UnityEngine.Color32 Hostile_Color, Unfriendly_Color, Neutral_Color, Friendly_Color, Loyal_Color;
    public static ERelationshipCategory GetCategory(System.Single delta);
    public static UnityEngine.Color32   GetColor(ERelationshipCategory category);
}

public class NPCRelationData : Il2CppSystem.Object     // reached via npc.RelationData
{
    static System.Single MinRelationship;              // read at runtime — the tier boundaries are RelationshipCategory.GetCategory's business
    static System.Single MaxRelationship;

    List<NPC>     Connections;                          // who this NPC can recommend / unlock
    System.Single RelationDelta            { public get; public set; }
    System.Single NormalizedRelationDelta  { public get; }      // 0..1 across Min..Max
    System.Boolean Unlocked                { public get; public set; }
    NPCRelationData+EUnlockType UnlockType { public get; public set; }   // Recommendation = 0, DirectApproach = 1
    NPC NPC { public get; public set; }
    Il2CppSystem.Action<System.Single> OnRelationshipChange;
    Il2CppSystem.Action<NPCRelationData+EUnlockType, System.Boolean> OnUnlocked;

    public System.Void Init(NPC npc);
    public System.Void SetNPC(NPC npc);
    public virtual System.Void SetRelationship(System.Single newDelta, System.Boolean network = true);
    public virtual System.Void ChangeRelationship(System.Single deltaChange, System.Boolean network = true);
    public virtual System.Void Unlock(NPCRelationData+EUnlockType type, System.Boolean notify = true);
    public virtual System.Void UnlockConnections();
    public System.Boolean IsKnown();
    public System.Boolean IsMutuallyKnown();
    public System.Single  GetAverageMutualRelationship();
    public List<NPC> GetLockedConnections(System.Boolean excludeCustomers = false);
    public List<NPC> GetLockedDealers(System.Boolean excludeRecommended);
    public List<NPC> GetLockedSuppliers();
    public Il2CppScheduleOne.Persistence.Datas.RelationshipData GetSaveData();
}

public class NPCUnlockTracker    : MonoBehaviour { NPC Npc; UnityEvent onUnlocked; public System.Void Invoke(EUnlockType type, System.Boolean t); }
public class NPCUnlockedVariable : MonoBehaviour { System.String VariableName; }   // mirrors unlock state into the Variables system
```

Relationship changes also come from `NPCResponses` statics:
`ASSAULT_RELATIONSHIPCHANGE`, `DEADLYASSAULT_RELATIONSHIPCHANGE`, `AIMED_AT_RELATIONSHIPCHANGE`,
`PICKPOCKET_RELATIONSHIPCHANGE`.

`[SC]` To make a custom customer discoverable: `npc.RelationData.Unlock(EUnlockType.DirectApproach)`
(or `Recommendation`), which fires `OnUnlocked` → `Customer.OnCustomerUnlocked` → moves the
`Customer` from `LockedCustomers` to `UnlockedCustomers` and fires the static
`Customer.onCustomerUnlocked`.

---

## 12. Inventory and transactions (`Il2CppScheduleOne.NPCs.NPCInventory`)

`public class NPCInventory : Il2CppFishNet.Object.NetworkBehaviour`, implements
`Il2CppScheduleOne.ItemFramework.IItemSlotOwner` (inferred from the `SetStoredInstance` /
`SetSlotLocked` / `SetSlotFilter` triple that `StorageEntity` also has).

```csharp
static System.Single PickpocketCooldown;
static System.Int32  MinimumRandomItems, MaximumRandomItems;
List<Il2CppScheduleOne.ItemFramework.ItemSlot> ItemSlots { public get; public set; }
Il2CppScheduleOne.Interaction.InteractableObject _interactable;
Il2CppReferenceArray<Il2CppScheduleOne.ItemFramework.ItemDefinition> DebugItems;
NPC _npc;

public System.Void Initialize(Il2CppScheduleOne.NPCs.Framework.NPCData data);   // builds slots from NPCData.Inventory

// Items
public System.Void InsertItem(Il2CppScheduleOne.ItemFramework.ItemInstance item, System.Boolean network = true);
public System.Boolean CanItemFit(ItemInstance item);
public System.Int32   GetCapacityForItem(ItemInstance item);
public ItemInstance   GetFirstItem(System.String id, NPCInventory+ItemFilter filter = null);
public ItemInstance   GetFirstIdenticalItem(ItemInstance item, NPCInventory+ItemFilter filter = null);
public System.Int32   GetIdenticalItemAmount(ItemInstance item);
public System.Int32   GetMaxItemCount(Il2CppStringArray ids);
public List<ItemSlot> GetSlots(Il2CppSystem.Func<ItemSlot, System.Boolean> predicate);
public System.Boolean IsInventoryEmpty();
public System.Void    Clear();
public System.Void    PrintInventoryContents();
public Il2CppScheduleOne.ItemFramework.StorableItemDefinition GetRandomInventoryItem(List<System.String> excludeIDs);
public System.Void AddRandomItemsToInventory();
public System.Single GetTotalRandomInventoryItemWeight();

// Money
public System.Void   AddCash(System.Single amountToAdd);
public System.Void   RemoveCash(System.Single amountToRemove);
public System.Single GetCashInInventory();
public System.Void   AddRandomCashInstance();

// Slot mutation (networked; all have _Internal + Rpc variants)
public virtual System.Void SetStoredInstance(NetworkConnection conn, System.Int32 itemSlotIndex, ItemInstance instance);
public virtual System.Void SetItemSlotQuantity(System.Int32 itemSlotIndex, System.Int32 quantity);
public virtual System.Void SetSlotFilter(NetworkConnection conn, System.Int32 itemSlotIndex, Il2CppScheduleOne.ItemFramework.SlotFilter filter);
public virtual System.Void SetSlotLocked(NetworkConnection conn, System.Int32 itemSlotIndex, System.Boolean locked,
                                         Il2CppFishNet.Object.NetworkObject lockOwner, System.String lockReason);

// Pickpocketing
public System.Boolean CanPickpocket();
public System.Void    StartPickpocket();
public System.Void    ExpirePickpocket();

public delegate System.Boolean NPCInventory+ItemFilter(ItemInstance item);   // implicit-converts from Func<ItemInstance,bool>
```

**Giving an NPC money/items:** `npc.Inventory.AddCash(500f)` and
`npc.Inventory.InsertItem(def.GetDefaultInstance(qty))`.

**Receiving from the player** goes through the handover UI, not `NPCInventory`:
`Il2CppScheduleOne.UI.Handover.HandoverScreen+EHandoverOutcome` is the outcome enum, consumed by
`Customer.ProcessHandover(...)`, `Customer.ProcessSample(...)`,
`RequestProductBehaviour.HandoverClosed(...)`, and `NPCs.Billy.HandoverOutcome(...)`. Payment is
applied by the customer/quest layer, and `CustomerAttendDealBehaviour.EnsureNPCHasEnoughCash()`
tops up the NPC's wallet before a deal so the money actually exists.

---

## 13. Awareness, responses and actions

```csharp
public class NPCAwareness : UnityEngine.MonoBehaviour     // npc.Awareness
{
    static System.Single PLAYER_AIM_DETECTION_RANGE;
    System.Boolean AwarenessActiveByDefault;
    Il2CppScheduleOne.Vision.VisionCone VisionCone;
    Il2CppScheduleOne.Noise.Listener    Listener;
    NPCResponses Responses;   NPC npc;
    UnityEvent<Player> onNoticedGeneralCrime, onNoticedPettyCrime, onNoticedDrugDealing,
                       onNoticedPlayerViolatingCurfew, onNoticedSuspiciousPlayer;
    UnityEvent<Il2CppScheduleOne.Noise.NoiseEvent> onGunshotHeard, onExplosionHeard;
    UnityEvent<Il2CppScheduleOne.Vehicles.LandVehicle> onHitByCar;
    public System.Void SetAwarenessActive(System.Boolean active);
    public System.Void VisionEvent(Il2CppScheduleOne.Vision.VisionEventReceipt vEvent);
    public System.Void NoiseEvent(Il2CppScheduleOne.Noise.NoiseEvent nEvent);
    public System.Void HitByCar(Il2CppScheduleOne.Vehicles.LandVehicle vehicle);
}
```

`NPCResponses` (base, 4 subclasses: `NPCResponses_Civilian`, `Employees.NPCResponses_Employee`,
`Police.NPCResponses_Police`, `Police.NPCResponses_CartelGoon`) has one virtual per stimulus —
`NoticedDrugDeal`, `NoticedPettyCrime`, `NoticedVandalism`, `NoticedViolatingCurfew`,
`NoticedWantedPlayer`, `NoticedSuspiciousPlayer`, `NoticePlayerBrandishingWeapon`,
`NoticePlayerDischargingWeapon`, `SawPickpocketing`, `PlayerFailedPickpocket`,
`GunshotHeard`, `ExplosionHeard`, `HitByCar`, `ImpactReceived`, `RespondToAimedAt`,
`RespondToAnnoyingImpact`, `RespondToFirstNonLethalAttack`, `RespondToRepeatedNonLethalAttack`,
`RespondToLethalAttack`. **Harmony-patch these to change how a class of NPC reacts.**

`NPCResponses_Civilian` adds the decision table:

```csharp
public enum NPCResponses_Civilian+EAttackResponse { None = 0, Panic = 1, Flee = 2, CallPolice = 3, Fight = 4 }
public enum NPCResponses_Civilian+EThreatType     { None = 0, AimedAt = 1, GunshotHeard = 2, ExplosionHeard = 3 }
System.Boolean       OverrideThreatResponses;
EAttackResponse      ThreatResponseOverride;      // <-- set this + OverrideThreatResponses to force a reaction
public EAttackResponse GetThreatResponse(EThreatType type, Player threatSource);
public System.Void ExecuteThreatResponse(EAttackResponse response, Player target, UnityEngine.Vector3 threatOrigin,
                                         Il2CppScheduleOne.Law.Crime crime = null);
```

`NPCActions` (`npc.Actions`) is a thin facade:
`Cower()`, `FacePlayer(Player)`, `CallPolice_Networked(NetworkObject playerObj)`,
`SetCallPoliceBehaviourCrime(Crime)`, `SetCanUseUmbrella(bool)`, `GetRainAmount()`.

`NPCDiscreteAction` (`Il2CppScheduleOne.NPCs.Other`) is a tiny parallel system for
non-exclusive activities that run alongside a behaviour: `Begin()`/`End()` plus
`BeginOnServer`/`BeginOnClient`/`EndOnServer`/`EndOnClient` virtuals, `IsActive`. Shipped:
`UseUmbrella`. Siblings that are plain MonoBehaviours with the same `Begin`/`End` shape:
`HoldItem`, `DrinkItem`, `SmokeCigarette`, `SprayPaint`. **A custom "hold a clipboard" or
"smoke while waiting" behaviour is a 20-line `NPCDiscreteAction` subclass** — and
`NPCScheduleManager.DiscreteActions` already collects them.

---

## 14. Instantiation — how an NPC actually gets into the world

### 14.1 The shipped model: scene-baked

Every named NPC is a **prefab instance placed in the scene** with:
- an `NPC`-derived component whose `_npcData` points at a `BaseNPCDataObject` asset,
- a `BakedGUID` string authored at design time,
- child GameObjects for each `Behaviour` and each `NPCAction`,
- an `Avatar` rig, `NPCMovement` + `NavMeshAgent`, `NPCInventory`, `DialogueHandler`, etc.,
- a FishNet `NetworkObject`.

They register themselves into the static `NPCManager.NPCRegistry` and are re-hydrated from
`.../NPCs/<id>/` by `NPCsLoader` → `NPCLoader`. **`NPCManager` exposes no spawn API at all.**

### 14.2 The employee model: prefab + runtime identity

`Employee : NPC` shows how the game creates a character whose identity is decided at runtime:

```csharp
public virtual System.Void Initialize(Il2CppFishNet.Connection.NetworkConnection conn,
                                      System.String firstName, System.String lastName, System.String id,
                                      System.String guid, System.String propertyID,
                                      System.Boolean male, System.Int32 appearanceIndex);
public virtual System.Void InitializeInfo(System.String firstName, System.String lastName, System.String id);
public virtual System.Void InitializeAppearance(System.Boolean male, System.Int32 index);
public virtual System.Void AssignProperty(Il2CppScheduleOne.Property.Property prop, System.Boolean warp);
public System.Void LeavePropertyAndDespawn();
System.Boolean IsMale;  System.Int32 AppearanceIndex;
```

Note the pattern: **name, ID, GUID and appearance are all pushed in after instantiation**, via a
single networked `Initialize`. `EEmployeeType = { Botanist=0, Handler=1, Chemist=2, Cleaner=3 }`.
`[HD]` A "Driver" employee type does **not** exist — `EEmployeeType` has no driver member and
there is no `Driver`/`Chauffeur` class anywhere in `02-types-index.txt`. A Hireable Drivers mod
must either add a new `Employee` subclass or repurpose an existing NPC.

### 14.3 The goon model: pooled, procedural — **use this one**

`Il2CppScheduleOne.Cartel.GoonPool : UnityEngine.MonoBehaviour` is the only place in the game that
spawns a fully-functional, randomly-generated NPC at runtime. It is the exact blueprint you want.

```csharp
static System.Single MALE_CHANCE;
Il2CppReferenceArray<CartelGoon>            goons;              // the pre-instantiated pool
Il2CppReferenceArray<Map.NPCEnterableBuilding> exitBuildings;
Il2CppReferenceArray<AvatarFramework.AvatarSettings> MaleBaseAppearances;
Il2CppReferenceArray<AvatarFramework.AvatarSettings> FemaleBaseAppearances;
Il2CppReferenceArray<AvatarFramework.AvatarSettings> MaleClothing;      // <-- clothing shipped as AvatarSettings overlays
Il2CppReferenceArray<AvatarFramework.AvatarSettings> FemaleClothing;
Il2CppReferenceArray<VoiceOver.VODatabase>  MaleVoices, FemaleVoices;
Il2CppStructArray<UnityEngine.Color>        SkinTones, HairColors;
List<CartelGoon> spawnedGoons, unspawnedGoons;
System.Int32 UnspawnedGoonCount { get; }

public CartelGoonAppearance GetRandomAppearance();
public CartelGoon SpawnGoon(UnityEngine.Vector3 spawnPoint);
public List<CartelGoon> SpawnMultipleGoons(UnityEngine.Vector3 spawnPoint, System.Int32 requestedAmount, System.Boolean setAsGoonMates = true);
public System.Void ReturnToPool(CartelGoon goon);
public Map.NPCEnterableBuilding GetNearestExitBuilding(UnityEngine.Vector3 position);

public class CartelGoonAppearance      // the whole "recombine existing parts" recipe, in six fields
{
    System.Boolean    IsMale;
    System.Int32      BaseAppearanceIndex;   // index into Male/FemaleBaseAppearances
    UnityEngine.Color SkinColor;
    UnityEngine.Color HairColor;
    System.Int32      ClothingIndex;         // index into Male/FemaleClothing
    System.Int32      VoiceIndex;            // index into Male/FemaleVoices
}

public class CartelGoon : NPC
{
    List<CartelGoon> goonMates;   CartelGoonAppearance appearance;   Il2CppSystem.Action onDespawn;
    System.Boolean IsGoonSpawned { public get; public set; }
    GoonPool GoonPool { get; }
    public System.Void Spawn(GoonPool pool, UnityEngine.Vector3 spawnPoint);
    public System.Void Spawn_Client(NetworkConnection conn);                        // ObserversRpc/TargetRpc
    public System.Void ConfigureGoonSettings(NetworkConnection conn, CartelGoonAppearance appearance, System.Single moveSpeed);
    public System.Void Despawn();
    public System.Void Despawn_Client(NetworkConnection conn);
    public System.Void AttackEntity(Il2CppScheduleOne.Combat.ICombatTargetable target, System.Boolean includeGoonMates = true);
    public System.Void AddGoonMate(CartelGoon goonMate);  RemoveGoonMate(CartelGoon);  IsMatesWith(CartelGoon);
}
```

**This confirms two things a custom-NPC mod needs to know:**
1. **Pooling exists** (`goons` / `spawnedGoons` / `unspawnedGoons` / `ReturnToPool`) — but it is
   private to the cartel system, not a general NPC service. You build your own.
2. **Appearance is composed, not authored per character.** A base `AvatarSettings` + a clothing
   `AvatarSettings` + a skin tone + a hair colour is enough for a visually distinct NPC. That is
   precisely the biker/hippie/businessman/agent recipe.

### 14.4 The concrete recipe for a working custom NPC

There is no single "create NPC" call. The reliable sequence, in dependency order:

```csharp
// 1. Get a prefab. Cheapest reliable source: clone a shipped NPC of the archetype you want.
//    (There is no NPC prefab registry — NPCManager holds NPCPoIPrefab only.)
var template = Il2CppScheduleOne.NPCs.NPCManager.NPCRegistry
                 .ToArray().First(n => n.ID == "uncle_nelson");        // any civilian works
var go = UnityEngine.Object.Instantiate(template.gameObject,
                                        Il2CppScheduleOne.NPCs.NPCManager.Instance.NPCContainer);
go.SetActive(false);                                                    // set data BEFORE Awake
var npc = go.GetComponent<Il2CppScheduleOne.NPCs.NPC>();

// 2. Identity. Build a fresh NPCData (or deep-copy the template's) and swap the BasicInfo.
var data = npc.NPCData.GetDeepCopy();
data.BasicInfo.FirstName = "Marcus";
data.BasicInfo.LastName  = "Vance";
data.BasicInfo.HasLastName = true;
data.BasicInfo.ID        = "mymod_marcus_vance";                        // the key for NPCManager.GetNPC

// 3. Appearance. Either hand-build AvatarSettings, or (easier) build a BasicAvatarSettings
//    and convert. See §6.4/§6.6.
data.Appearance.AvatarSettings = myBasicAvatarSettings.GetAvatarSettings();
// data.Appearance.Mugshot can be generated: MugshotGenerator.Instance.GenerateMugshot(settings, false, cb);

// 4. Tuning blocks: data.Movement.WalkSpeed, data.Health.MaxHealth, data.Inventory.*,
//    data.Behaviour.CanCallPolice, data.Relationship.DefaultRelationshipValue,
//    data.Messaging.IsKnownByDefault, data.Dialogue.DialogueDatabase, data.Voice.VoiceDatabase …

// 5. Push it in, then activate so Awake sees the new data.
npc.ApplyNPCData(data);                     // also safe to call after activation
go.SetActive(true);
npc.SetGUID(Il2CppSystem.Guid.NewGuid());   // or a deterministic GUID so saves are stable

// 6. Register. Nothing else in the game will see the NPC until this happens.
if (!Il2CppScheduleOne.NPCs.NPCManager.NPCRegistry.Contains(npc))
    Il2CppScheduleOne.NPCs.NPCManager.NPCRegistry.Add(npc);

// 7. Network-spawn it (host/server only). NPC : NetworkBehaviour, so it needs a NetworkObject
//    spawn to exist for clients: InstanceFinder.ServerManager.Spawn(go)  — see FishNet.  [UNVERIFIED
//    exact call for this build; the NPC side is OnStartServer/OnSpawnServer, which fire from it.]

// 8. Place it.
npc.Movement.Warp(spawnPosition);           // or npc.Movement.WarpToNavMesh()

// 9. Optional roles:
//    - customer:  go.AddComponent<Il2CppScheduleOne.Economy.Customer>(); customer.NPC = npc;
//                 npc.RelationData.Unlock(EUnlockType.DirectApproach);
//    - phone contact: npc.CreateMessageConversation();
//    - schedule:  add NPCAction children, then npc.Behaviour.ScheduleManager.InitializeActions()
//                 followed by EnforceState(true);
//    - behaviours: they are components already on the cloned prefab; enable/disable by
//                 npc.Behaviour.GetBehaviour<T>() then .Enable_Server() / .Disable_Server().
```

**Why cloning beats building from scratch:** an `NPC` needs a rigged `Avatar` hierarchy, a
`NavMeshAgent` sized for the humanoid agent type, a `NetworkObject` with matching FishNet
codegen IDs, ~15 sibling components and a tree of `Behaviour` children. Reconstructing that by
hand in IL2CPP is a lost afternoon; cloning gives you all of it for free and everything you
actually want to change lives in `NPCData` + `AvatarSettings`.

---

## 15. Runtime flows worth memorising

**Spawn → ready:** `Awake` → `GetAndValidateReferences()` → `_npcData.GetRuntimeData()` →
`ApplyNPCData(data)` → `OnNPCDataReady` fires → `NPCInventory.Initialize(data)`,
`DialogueHandler.Initialize(data)`, avatar settings applied → `OnStartServer` /
`OnSpawnServer(conn)` → `Start` → `NPCScheduleManager.InitializeActions()` →
`EnforceState(initial: true)`.

**A minute passes:** `TimeManager` → `NPC.MinPass()` and `NPCScheduleManager.OnMinPass()` →
diff against `lastProcessedTime` → `ShouldStart()` on pending actions → `StartAction(action)` →
`OnStart`/`Started`/`LateStarted` → the action calls `SetDestination` → `NPCMovement` walks →
`WalkCallback(WalkResult)` → action does its thing until `GetEndTime()` → `End()`.

**Something interrupts:** a higher-priority `Behaviour` calls `Enable_Server()` →
`NPCBehaviour.SortBehaviourStack()` → current `activeBehaviour.Pause()` →
new one `Activate()` → when it `Disable_Server()`s, the previous one `Resume()`s. The
schedule keeps ticking underneath via `ScheduleBehaviour`.

**Player talks to an NPC:** `InteractableObject` → `DialogueController.Interacted()` →
`CanStartDialogue()` → `GetActiveGreeting(...)` → `GetActiveChoices()` (filtered by each
`DialogueChoice.ShouldShow()` / `IsValid(out reason)`) → player picks →
`DialogueHandler.ChoiceSelected(index)` → `ChoiceCallback(choiceLabel)` +
`DialogueChoice.onChoosen` → `StartDialogue(choice.Conversation)` → node walk via
`ShowNode`/`EvaluateBranch`/`GetLink` → `EndDialogue()` → `OnDialogueEnd`.

**A deal completes `[SC]`:** `Customer.TryGenerateContract(dealer)` →
`OfferContract(ContractInfo)` → `NotifyPlayerOfContract(...)` (phone) →
`ContractAccepted(window, trackContract, dealer)` → at deal time
`CustomerAttendDealBehaviour.SetContract(contract)` + `EnsureNPCHasEnoughCash()` → NPC walks to
`DeliveryLocation` → handover UI → `Customer.ProcessHandover(outcome, contract, items, byPlayer,
giveBonuses)` → `EvaluateDelivery(...)` → `AdjustAffinity(drugType, change)` +
`NPCRelationData.ChangeRelationship(delta)`.

---

## 16. UNVERIFIED / not present in the dumps

1. **Avatar layer & accessory asset-path catalogue.** Only 7 path literals exist anywhere in
   `literals-*.txt` and `strings-global-metadata-*.txt`
   (`Avatar/Layers/Bottom/MaleUnderwear`, `Avatar/Layers/Bottom/FemaleUnderwear`,
   `Avatar/Layers/Face/EyeShadow`, `Avatar/Layers/Top/Nipples`, `Avatar/Equippables/Hammer`,
   `Avatar/Equippables/Phone_Lowered`, `Avatar/Equippables/TrashBag`). Everything else lives in
   Unity assets. **Enumerate at runtime** (§6.6). Do not invent paths.
2. **How `Avatar` resolves an `assetPath` string into an `AvatarLayer`/`Accessory`.** No
   `Resources.Load` / registry method is visible on any AvatarFramework type in the dumps. Most
   likely a `Resources.Load<T>(assetPath)` inside `SetBodyLayer`/`ApplyAccessorySettings`, but I
   could not confirm it — **UNVERIFIED**.
3. **NPC prefab paths / a prefab registry.** `NPCManager` has `NPCPoIPrefab`,
   `PotentialCustomerPoIPrefab`, `PotentialDealerPoIPrefab` and nothing else. There is no
   `NPCPrefabs` list analogous to `VehicleManager.VehiclePrefabs`. Cloning a live NPC is the only
   verified route — **UNVERIFIED** that any other exists.
4. **Who adds an `NPC` to `NPCManager.NPCRegistry`.** No type in the dumps other than `NPCManager`
   itself names `NPCRegistry`, and `NPCManager` has no add/remove method. `NPC.Awake`/`Start` is
   the only plausible writer — **inferred, not verified**.
5. **The FishNet server-spawn call for this build** (`InstanceFinder.ServerManager.Spawn(...)` vs.
   a game wrapper). `NPC.OnStartServer`/`OnSpawnServer(conn)` are the receiving ends; the caller
   is FishNet internals — **UNVERIFIED** which overload/entry point the game uses.
6. **`AvatarSettings.Gender` / `Weight` ranges.** Treat as 0..1 shape-key blends (consistent with
   `ApplyShapeKeys(gender, weight)` and `BasicAvatarSettings.Gender` being an `Int32` selector
   scaled by `GenderScaleMultiplier`) — **inferred**, no clamp is visible.
7. **`NPCRelationData` tier thresholds.** `MinRelationship`/`MaxRelationship` are static fields
   whose *values* are not in the metadata; `RelationshipCategory.GetCategory(delta)` owns the
   `ERelationshipCategory` boundaries. Read both at runtime.
8. **`NPCAction.StartTime` units.** Documented here as the int game clock (`HHMM`) matching
   `TimeManager`; the dump only shows `System.Int32` — **inferred** from
   `GetActionsOccurringAt(Int32 time)` and `lastProcessedTime`.
9. **The last 4 of the 14 `DialogueController` subclasses** were cut off by output truncation in
   `03-subclasses.txt:1396`. Ten are listed in §10.3; re-grep that line for the full set.
10. **`Avatar` does not expose a clothing API.** `Clothing.ClothingDefinition` → avatar mapping in
    §6.5 is reconstructed from `ApplicationType` + the `Avatar.SetBodyLayer`/`SetFaceLayer`/
    `ApplyAccessorySettings` triple. No `ApplyClothing`/`Wear`/`Equip(ClothingInstance)` method
    exists on `Avatar` — **the mapping is inferred**, though `CharacterCreator.SetValue<T>(fieldName,
    value, ClothingDefinition definition)` and `CharacterCreator.lastSelectedClothingDefinitions`
    confirm the creator does exactly this translation.
11. **NPC despawn.** Only `CartelGoon.Despawn()` and `Employee.LeavePropertyAndDespawn()` exist.
    There is no generic `NPC.Despawn` — **you destroy the GameObject and remove it from
    `NPCManager.NPCRegistry` yourself.**

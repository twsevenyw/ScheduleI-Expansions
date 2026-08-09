# API — Economy / Customers / Dealers / Products / Money

> **Source of truth**: `research/raw/*` dumps of the *installed* MelonLoader `Il2CppAssemblies` for
> Schedule I v0.4.6f11 (Unity 2022.3.62f2, IL2CPP, MelonLoader 0.7.1, Il2CppInterop 1.5.0).
> Every type name, member name and signature below is copied verbatim from those dumps.
> Anything inferred is labelled `(inferred …)`; anything I could not confirm is labelled **UNVERIFIED**.
>
> **Naming**: interop namespaces are prefixed `Il2Cpp` (`Il2CppScheduleOne.Economy`). The *original*
> managed namespace is `ScheduleOne.Economy` — that unprefixed form is what appears in save files,
> FishNet RPC hash names (`ReadSyncVar___ScheduleOne_Economy_Customer`) and any Harmony-by-string
> `AccessTools.TypeByName("ScheduleOne.Economy.Customer")` lookup. Use the unprefixed name for
> strings, the `Il2Cpp`-prefixed name for compile-time references.
>
> **Il2CppInterop artefacts**: IL2CPP instance fields surface as C# *properties*. Both
> `_Foo_k__BackingField` and `Foo` exist — always prefer `Foo`. Members named
> `field_Private_Boolean_0`, `Method_Protected_Virtual_Void_0`, `Method_Private_Void_0` etc. are
> fallback names generated when the real IL2CPP name is not a legal C# identifier. **These indices
> are not stable across game versions or across regenerations of the interop assemblies — never
> Harmony-patch them by name without a runtime guard.**

---

## Assemblies & namespaces

| Assembly | Version | Types | Relevance |
|---|---|---|---|
| `Assembly-CSharp` | `0.0.0.0` | 3597 | **all** economy / customer / dealer / product / money code |
| `Assembly-CSharp-firstpass` | `0.0.0.0` | 625 | Curvy splines, EPOOutline — not economy |
| `Il2CppScheduleOne.Core` | `0.0.0.0` | 88 | `Il2CppScheduleOne.Core.Deliveries.DeliverySettings`, weather, settings framework |
| `Il2CppFishNet.Runtime` | `0.0.0.0` | — | `NetworkBehaviour`, `SyncVar<T>`, `PooledReader`, `NetworkConnection` |
| `S1API` | — | — | community modding wrapper (installed 3.1.4); wraps most of the below |

Namespaces in scope (public/total type counts from `research/raw/01-namespaces.txt`, all in
`Assembly-CSharp`):

| Namespace | Types | Notes |
|---|---|---|
| `Il2CppScheduleOne.Economy` | 20 | `Customer`, `CustomerData`, `Dealer`, `Supplier`, `DeadDrop`, `DeliveryLocation`, deal-window + standard enums |
| `Il2CppScheduleOne.Product` | 43 | `ProductDefinition`, `ProductItemInstance`, `ProductManager`, `ProductList`, `EDrugType`, `EProperty` |
| `Il2CppScheduleOne.Product.Packaging` | 2 | `PackagingDefinition`, `EStealthLevel` |
| `Il2CppScheduleOne.Packaging` | 7 | minigame/functional packaging only (no economy logic) |
| `Il2CppScheduleOne.Money` | 4 | `MoneyManager`, `Transaction`, `ATM`, `CashSlot` |
| `Il2CppScheduleOne.Messaging` | 8 | `MSGConversation`, `MessagingManager`, `Message`, `Response`, `SendableMessage` |
| `Il2CppScheduleOne.Delivery` | 7 | shop deliveries (van drops) — **not** customer deals |
| `Il2CppScheduleOne.Cartel` | 18 | `CartelDealer : Dealer`, `CartelDealManager`, `CartelInfluence`, `CartelActivity` |
| `Il2CppScheduleOne.Map` | 31 | `EMapRegion`, `Map`, `MapRegionData`, `NPCPoI`, `NPCEnterableBuilding` |
| `Il2CppScheduleOne.Quests` | 37 | `Contract : Quest`, `ContractInfo`, `QuestWindowConfig`, `QuestManager` |
| `Il2CppScheduleOne.UI.Handover` | 2 | `HandoverScreen`, `HandoverScreenDetailPanel` |
| `Il2CppScheduleOne.Persistence.Datas` | 131 | save DTOs (`CustomerData`, `DealerData`, `MoneyData`, `ProductManagerData`, `ContractData`) |
| `Il2CppScheduleOne.NPCs` | 19 | `NPC`, `NPCManager`, `NPCMovement`, `Billy` |
| `Il2CppScheduleOne.NPCs.Framework` | 32 | `NPCData`, `DealerNPCData`, `SupplierNPCData` (ScriptableObject-style config) |
| `Il2CppScheduleOne.NPCs.Relation` | 5 | `NPCRelationData`, `ERelationshipCategory` |
| `Il2CppScheduleOne.NPCs.Behaviour` | 55 | `CustomerAttendDealBehaviour`, `DealerAttendDealBehaviour` |

**FishNet codegen rule (verified against the metadata string table):** every `NetworkBehaviour`
subclass has its author-written `Awake` renamed to
`Awake_UserLogic_<FullTypeName>_Assembly-CSharp.dll` and a generated `Awake` put in its place.
Because that name contains `.` and `-`, Il2CppInterop renames it again to
`Method_Protected_Virtual_Void_0` / `Method_Protected_Virtual_New_Void_0`. Confirmed strings exist for
`ScheduleOne.Economy.Customer`, `.Dealer`, `.Supplier`, `ScheduleOne.NPCs.NPC`, `.NPCManager`,
`ScheduleOne.Money.MoneyManager`, `ScheduleOne.Product.ProductManager`,
`ScheduleOne.Quests.QuestManager`, `ScheduleOne.Messaging.MessagingManager`,
`ScheduleOne.Delivery.DeliveryManager`, `ScheduleOne.Levelling.LevelManager`,
`ScheduleOne.Cartel.CartelDealManager`, `ScheduleOne.Cartel.CartelInfluence`,
`ScheduleOne.NPCs.Behaviour.CustomerAttendDealBehaviour`, `…DealerAttendDealBehaviour`.
So: **the real per-frame/`Awake` body you want to patch is the fallback-named method, not `Awake`.**

Manager access pattern (from `Il2CppScheduleOne.DevUtilities.NetworkSingleton`1`):

```csharp
public class Il2CppScheduleOne.DevUtilities.NetworkSingleton`1 // base: Il2CppFishNet.Object.NetworkBehaviour
{
    static T instance { public get; public set; }
    System.Boolean Destroyed { public get; public set; }
    static System.Boolean InstanceExists { public get; }
    static T Instance { public get; public set; }
}
```

`Il2CppScheduleOne.DevUtilities.Singleton`1`, `PersistentSingleton`1`, `PlayerSingleton`1` follow the
same `Instance` / `InstanceExists` shape.

---

## 1. `Il2CppScheduleOne.Economy.Customer`

### What it is

**`Customer` is a separate `NetworkBehaviour` component, NOT an `NPC` subclass.**

```csharp
public class Il2CppScheduleOne.Economy.Customer
    : Il2CppFishNet.Object.NetworkBehaviour
```

Evidence, all verifiable in the dumps:

* `research/raw/02-types-index.txt` line 742:
  `Assembly-CSharp | public class | Il2CppScheduleOne.Economy.Customer | base: Il2CppFishNet.Object.NetworkBehaviour`
* `research/raw/03-subclasses.txt` has **no** `BASE Il2CppScheduleOne.Economy.Customer` entry →
  `Customer` has **zero** subclasses. (Contrast `BASE Il2CppScheduleOne.Economy.Dealer (7 direct subclasses)`.)
* `Customer` carries a back-reference `Il2CppScheduleOne.NPCs.NPC NPC { public get; public set; }`.
* `Il2CppScheduleOne.NPCs.Billy` (an `NPC` subclass) holds
  `Il2CppScheduleOne.Economy.Customer customerComp { public get; public set; }` — i.e. an NPC
  reaches its `Customer` through a component reference, sibling-style.
* It is *also* an `ISaveable`: it exposes `SaveFolderName`, `SaveFileName`, `Loader`,
  `ShouldSaveUnderFolder`, `LocalExtraFiles`, `LocalExtraFolders`, `HasChanged`,
  `InitializeSaveable()`, `GetSaveString()`, `WriteData(string)`.

**Hard runtime requirement** — the literal string
`"CustomerAttendDealBehaviour is required for Customer.cs to function properly. Disabling Customer component on "`
exists in `global-metadata.dat`. So a `Customer` component **must** sit on a GameObject that also
provides `Il2CppScheduleOne.NPCs.Behaviour.CustomerAttendDealBehaviour`, or `Customer` disables
itself. The field is `Il2CppScheduleOne.NPCs.Behaviour.CustomerAttendDealBehaviour _attendDealBehaviour`.

### Static constants and registries (all exposed as static properties)

```csharp
static Il2CppSystem.Action<Il2CppScheduleOne.Economy.Customer> onCustomerUnlocked { public get; public set; }
static Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Economy.Customer> LockedCustomers { public get; public set; }
static Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Economy.Customer> UnlockedCustomers { public get; public set; }
static System.Int32 QualityTierTolerance { public get; public set; }
static System.Int32 MaxOrderQuantityPerProduct { public get; public set; }
static System.Single AFFINITY_MAX_EFFECT { public get; public set; }
static System.Single PROPERTY_MAX_EFFECT { public get; public set; }
static System.Single QUALITY_MAX_EFFECT { public get; public set; }
static System.Single DEAL_REJECTED_RELATIONSHIP_CHANGE { public get; public set; }
static System.Int32 ATTACK_DEAL_COOLDOWN { public get; public set; }
static System.Single RELATIONSHIP_THRESHOLD_TO_GIVE_DEAL_TO_CARTEL { public get; public set; }
static System.Single CUSTOMER_UNLOCKED_CARTEL_INFLUENCE_CHANGE { public get; public set; }
static System.Single APPROACH_MIN_ADDICTION { public get; public set; }
static System.Single APPROACH_CHANCE_PER_DAY_MAX { public get; public set; }
static System.Single APPROACH_MIN_COOLDOWN { public get; public set; }
static System.Single APPROACH_MAX_COOLDOWN { public get; public set; }
static System.Int32 DEAL_COOLDOWN { public get; public set; }
static Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray PlayerAcceptMessages { public get; public set; }
static Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray PlayerRejectMessages { public get; public set; }
static System.Int32 DEAL_ATTENDANCE_TOLERANCE { public get; public set; }
static System.Int32 MIN_TRAVEL_TIME { public get; public set; }
static System.Int32 MAX_TRAVEL_TIME { public get; public set; }
static System.Int32 OFFER_EXPIRY_TIME_MINS { public get; public set; }
static System.Single MIN_ORDER_APPEAL { public get; public set; }
static System.Single ADDICTION_DRAIN_PER_DAY { public get; public set; }
static System.Boolean SAMPLE_REQUIRES_RECOMMENDATION { public get; public set; }
static System.Single MIN_NORMALIZED_RELATIONSHIP_FOR_RECOMMENDATION { public get; public set; }
static System.Single RELATIONSHIP_FOR_GUARANTEED_DEALER_RECOMMENDATION { public get; public set; }
static System.Single RELATIONSHIP_FOR_GUARANTEED_SUPPLIER_RECOMMENDATION { public get; public set; }
```

> These are `const`/`static readonly` in source but Il2CppInterop exposes them as writable static
> properties. **Numeric values are not in the metadata dumps** (they live in IL / initialiser code),
> so every value above is **UNVERIFIED** — read them at runtime.
>
> There is **no global customer registry object**: `LockedCustomers` + `UnlockedCustomers` *are* the
> registry. See §3.

### Instance state (properties; backing fields elided where a clean name exists)

```csharp
System.Boolean DEBUG { public get; public set; }

// --- addiction / dependence (SyncVar-backed) ---
System.Single CurrentAddiction { public get; public set; }
Il2CppFishNet.Object.Synchronizing.SyncVar<System.Single> syncVar____CurrentAddiction_k__BackingField { public get; public set; }
System.Single SyncAccessor_<CurrentAddiction>k__BackingField { public get; public set; }

// --- current offer / contract ---
Il2CppScheduleOne.Quests.ContractInfo offeredContractInfo { public get; public set; }   // raw field
Il2CppScheduleOne.Quests.ContractInfo OfferedContractInfo { public get; public set; }   // property
Il2CppScheduleOne.GameTime.GameDateTime OfferedContractTime { public get; public set; }
Il2CppScheduleOne.Quests.Contract CurrentContract { public get; public set; }
System.Boolean IsAwaitingDelivery { public get; public set; }
System.Boolean pendingInstantDeal { public get; public set; }

// --- cooldown counters, all in in-game minutes (inferred from names + MinPass driver) ---
System.Int32 TimeSinceLastDealCompleted { public get; public set; }
System.Int32 TimeSinceLastDealOffered { public get; public set; }
System.Int32 TimeSincePlayerApproached { public get; public set; }
System.Int32 TimeSinceInstantDealOffered { public get; public set; }
System.Int32 minsSinceUnlocked { public get; public set; }

// --- counters / history ---
System.Int32 OfferedDeals { public get; public set; }
System.Int32 CompletedDeliveries { public get; public set; }
Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Economy.Customer+ProductPurchaseRecord> WeeklyPurchaseRecord { public get; public set; }

// --- unlock / recommendation gating (HasBeenRecommended is a SyncVar) ---
System.Boolean HasBeenRecommended { public get; public set; }
Il2CppFishNet.Object.Synchronizing.SyncVar<System.Boolean> syncVar____HasBeenRecommended_k__BackingField { public get; public set; }
System.Boolean SyncAccessor_<HasBeenRecommended>k__BackingField { public get; public set; }

// --- links ---
Il2CppScheduleOne.NPCs.NPC NPC { public get; public set; }
Il2CppScheduleOne.Economy.Dealer AssignedDealer { public get; public set; }
Il2CppScheduleOne.Economy.CustomerData customerData { public get; public set; }   // serialized ScriptableObject ref
Il2CppScheduleOne.Economy.CustomerData CustomerData { public get; }               // read-only accessor
Il2CppScheduleOne.Economy.CustomerAffinityData currentAffinityData { public get; public set; }
Il2CppScheduleOne.Dialogue.DialogueDatabase dialogueDatabase { public get; }
Il2CppScheduleOne.NPCs.Behaviour.CustomerAttendDealBehaviour _attendDealBehaviour { public get; public set; }
Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.GameTime.EDay> _cachedOrderDays { public get; public set; }

// --- events ---
UnityEngine.Events.UnityEvent onUnlocked { public get; public set; }
UnityEngine.Events.UnityEvent onDealCompleted { public get; public set; }
UnityEngine.Events.UnityEvent<Il2CppScheduleOne.Quests.Contract> onContractAssigned { public get; public set; }

// --- sample flow ---
System.Boolean awaitingSample { public get; public set; }
System.Boolean sampleOfferedToday { public get; public set; }
Il2CppScheduleOne.Product.ProductItemInstance consumedSample { public get; public set; }

// --- dialogue hooks installed by SetUpDialogue() ---
Il2CppScheduleOne.Dialogue.DialogueController+DialogueChoice sampleChoice { public get; public set; }
Il2CppScheduleOne.Dialogue.DialogueController+DialogueChoice completeContractChoice { public get; public set; }
Il2CppScheduleOne.Dialogue.DialogueController+DialogueChoice offerDealChoice { public get; public set; }
Il2CppScheduleOne.Dialogue.DialogueController+GreetingOverride awaitingDealGreeting { public get; public set; }

// --- map marker ---
Il2CppScheduleOne.Map.NPCPoI potentialCustomerPoI { public get; public set; }

// --- ISaveable ---
System.String SaveFolderName { public get; }
System.String SaveFileName { public get; }
Il2CppScheduleOne.Persistence.Loaders.Loader Loader { public get; }
System.Boolean ShouldSaveUnderFolder { public get; }
Il2CppSystem.Collections.Generic.List<System.String> LocalExtraFiles { public get; public set; }
Il2CppSystem.Collections.Generic.List<System.String> LocalExtraFolders { public get; public set; }
System.Boolean HasChanged { public get; public set; }

// --- unstable fallback names ---
System.Boolean field_Private_Boolean_0 { public get; public set; }
System.Boolean field_Private_Boolean_1 { public get; public set; }
```

### Nested types

```csharp
public class Il2CppScheduleOne.Economy.Customer+CustomerPreference        // base: Il2CppSystem.Object
{
    Il2CppScheduleOne.Product.EDrugType DrugType { public get; public set; }
    Il2CppScheduleOne.Product.ProductDefinition Definition { public get; public set; }
    Il2CppScheduleOne.ItemFramework.EQuality MinimumQuality { public get; public set; }
}

public enum Il2CppScheduleOne.Economy.Customer+ESampleFeedback            // OriginalName "ESampleFeedback"
{
    WrongProduct = 0,
    WrongQuality = 1,
    Correct      = 2,
}

public class Il2CppScheduleOne.Economy.Customer+ProductPurchaseRecord     // base: Il2CppSystem.Object
{
    System.String ProductID { public get; public set; }
    System.Int32 Quantity { public get; public set; }
    System.Single TotalSpent { public get; public set; }
}

public class Il2CppScheduleOne.Economy.Customer+ScheduleGroupPair         // base: Il2CppSystem.Object
{
    UnityEngine.GameObject NormalScheduleGroup { public get; public set; }
    UnityEngine.GameObject CurfewScheduleGroup { public get; public set; }
}
```

> `CustomerPreference` and `ScheduleGroupPair` are declared but **no `Customer` member of either type
> appears in the dump** — they look vestigial in v0.4.6. **UNVERIFIED** whether anything still uses them.

### Full method list (177 methods, verbatim)

Order / contract:

```csharp
public Il2CppScheduleOne.Quests.ContractInfo TryGenerateContract(Il2CppScheduleOne.Economy.Dealer dealer)
public virtual System.Boolean ShouldTryGenerateDeal()
public System.Boolean IsDealTime()
public System.Void ForceDealOffer()
public virtual System.Void OfferContract(Il2CppScheduleOne.Quests.ContractInfo info)
public System.Void OfferContractToDealer(Il2CppScheduleOne.Quests.ContractInfo info, Il2CppScheduleOne.Economy.Dealer dealer)
public System.Void SetOfferedContract(Il2CppScheduleOne.Quests.ContractInfo info, Il2CppScheduleOne.GameTime.GameDateTime offerTime)
public virtual System.Void NotifyPlayerOfContract(Il2CppScheduleOne.Quests.ContractInfo contract, Il2CppScheduleOne.UI.Phone.Messages.MessageChain offerMessage, System.Boolean canAccept, System.Boolean canReject, System.Boolean canCounterOffer = True)
public System.Void SetUpResponseCallbacks()
public virtual System.Void AcceptContractClicked()
public virtual System.Void CounterOfferClicked()
public virtual System.Void PlayerAcceptedContract(Il2CppScheduleOne.Economy.EDealWindow window)
public System.Void SendContractAccepted(Il2CppScheduleOne.Economy.EDealWindow window, System.Boolean trackContract)
public System.Void ReceiveContractAccepted()
public Il2CppScheduleOne.Quests.Contract ContractAccepted(Il2CppScheduleOne.Economy.EDealWindow window, System.Boolean trackContract, Il2CppScheduleOne.Economy.Dealer dealer)
public virtual System.Void ContractRejected()
public System.Void ReceiveContractRejected()
public virtual System.Void AssignContract(Il2CppScheduleOne.Quests.Contract contract)
public virtual System.Void CurrentContractEnded(Il2CppScheduleOne.Quests.EQuestState outcome)
public virtual System.Void ExpireOffer()
public System.Void UpdateOfferExpiry()
public System.Void UpdateDealAttendance()
public System.Boolean IsAtDealLocation()
public virtual System.Void SetIsAwaitingDelivery(System.Boolean awaiting)
public Il2CppScheduleOne.Economy.DeliveryLocation GetDeliveryLocation()
public static System.Void GetContractTimings(Il2CppScheduleOne.Quests.QuestWindowConfig dealWindow, out System.Int32& softStartTime, out System.Int32& hardStartTime, out System.Int32& endTime)
public static System.Int32 MinsSinceLastDealOfferedAllCustomers()
public virtual System.Void PlayContractAcceptedReaction()
public virtual System.Void PlayContractRejectedReaction()
public System.Void SetContractIsCounterOffer()
```

Product selection / valuation:

```csharp
public Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Product.ProductDefinition> GetOrderableProducts(Il2CppScheduleOne.Economy.Dealer dealer = null)
public Il2CppSystem.Collections.Generic.List<Il2CppSystem.Tuple<Il2CppScheduleOne.Product.ProductDefinition, System.Int32>> GetOrderableProductsWithQuantities(Il2CppScheduleOne.Economy.Dealer dealer = null)
public Il2CppScheduleOne.Product.ProductDefinition GetWeightedRandomProduct(Il2CppScheduleOne.Economy.Dealer dealer, out System.Single& appeal, out System.Int32& orderableQuantity)
public System.Single GetProductEnjoyment(Il2CppScheduleOne.Product.ProductDefinition product, Il2CppScheduleOne.ItemFramework.EQuality quality)
public System.Single GetProductEnjoyment(Il2CppScheduleOne.Product.ProductDefinition product)
public Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Product.EDrugType> GetOrderedDrugTypes()
public static System.Single GetValueProposition(Il2CppScheduleOne.Product.ProductDefinition product, System.Single price)
```

Counter-offers, direct offers, samples:

```csharp
public virtual System.Boolean EvaluateCounteroffer(Il2CppScheduleOne.Product.ProductDefinition product, System.Int32 quantity, System.Single price)
public virtual System.Void SendCounteroffer(Il2CppScheduleOne.Product.ProductDefinition product, System.Int32 quantity, System.Single price)
public System.Void ProcessCounterOfferServerSide(System.String productID, System.Int32 quantity, System.Single price)
public virtual System.Void OfferDealItems(Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemInstance> items, System.Boolean offeredByPlayer, out System.Boolean& accepted)
public virtual System.Boolean OfferDealValid(out System.String& invalidReason)
public virtual System.Boolean ShowOfferDealOption(System.Boolean enabled)
public System.Single GetOfferSuccessChance(Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemInstance> items, System.Single askingPrice)
public virtual System.Void InstantDealOffered()
public virtual System.Void CustomerRejectedDeal(System.Boolean offeredByPlayer)
public System.Void RequestProduct()
public System.Void RequestProduct(Il2CppScheduleOne.PlayerScripts.Player target)
public System.Void PlayerRejectedProductRequest()
public System.Void RejectProductRequestOffer()
public System.Void RejectProductRequestOffer_Local()
public System.Void SampleOffered()
public virtual System.Single GetSampleRequestSuccessChance()
public virtual System.Void SampleAccepted()
public System.Single GetSampleSuccess(Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemInstance> items, System.Single price)
public System.Void ProcessSample(Il2CppScheduleOne.UI.Handover.HandoverScreen+EHandoverOutcome outcome, Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemInstance> items, System.Single price)
public System.Void ProcessSampleServerSide(Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemInstance> items)
public System.Void ProcessSampleClient()
public System.Void SampleConsumed()
public System.Void SampleWasSufficient()
public System.Void SampleWasInsufficient()
public virtual System.Boolean SampleOptionValid(out System.String& invalidReason)
public System.Void EndWait()
public virtual System.Void DirectApproachRejected()
public virtual System.Boolean ShowDirectApproachOption(System.Boolean enabled)
public virtual System.Boolean ShouldTryApproachPlayer()
```

Handover / delivery evaluation:

```csharp
public virtual System.Boolean IsReadyForHandover(System.Boolean enabled)
public virtual System.Boolean IsHandoverChoiceValid(out System.String& invalidReason)
public System.Void HandoverChosen()
public virtual System.Void ProcessHandover(Il2CppScheduleOne.UI.Handover.HandoverScreen+EHandoverOutcome outcome, Il2CppScheduleOne.Quests.Contract contract, Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemInstance> items, System.Boolean handoverByPlayer, System.Boolean giveBonuses = True)
public System.Void ProcessHandoverServerSide(Il2CppScheduleOne.UI.Handover.HandoverScreen+EHandoverOutcome outcome, Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemInstance> items, System.Boolean handoverByPlayer, System.Single totalPayment, Il2CppScheduleOne.Product.ProductList productList, System.Single satisfaction, Il2CppFishNet.Object.NetworkObject dealerObject)
public System.Void ProcessHandoverClient(System.Single satisfaction, System.Boolean handoverByPlayer, System.String npcToRecommend, Il2CppScheduleOne.UI.Handover.HandoverScreen+EHandoverOutcome outcome)
public virtual System.Single EvaluateDelivery(Il2CppScheduleOne.Quests.Contract contract, Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemInstance> providedItems, out System.Single& highestAddiction, out Il2CppScheduleOne.Product.EDrugType& mainTypeType, out System.Int32& matchedProductCount, out System.Single& qualityDifference)
public System.Void CalculateTopWeeklyPurchases(out Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.DevUtilities.StringIntPair>& mostPurchasedProducts, out System.Single& totalSpent)
public System.Void ContractWellReceived(System.String npcToRecommend)
```

Addiction / affinity / consumption:

```csharp
public System.Void ChangeAddiction(System.Single change)
public System.Void AdjustAffinity(Il2CppScheduleOne.Product.EDrugType drugType, System.Single change)
public System.Void ConsumeProduct(Il2CppScheduleOne.ItemFramework.ItemInstance item)
```

Unlock / recommendation / dealer assignment:

```csharp
public virtual System.Boolean IsUnlockable()
public System.Boolean KnownAndRecommended()
public virtual System.Void OnCustomerUnlocked(Il2CppScheduleOne.NPCs.Relation.NPCRelationData+EUnlockType unlockType, System.Boolean notify)
public System.Void SetHasBeenRecommended()
public System.Void RecommendCustomer(Il2CppScheduleOne.Economy.Customer friend)
public System.Void RecommendDealer(Il2CppScheduleOne.Economy.Dealer dealer)
public System.Void RecommendSupplier(Il2CppScheduleOne.Economy.Supplier supplier)
public System.Void AssignDealer(Il2CppScheduleOne.Economy.Dealer dealer)
public System.Void SetupPoI()
public System.Void UpdatePotentialCustomerPoI()
public System.Void SetPotentialCustomerPoIEnabled(System.Boolean enabled)
public System.Void SetUpDialogue()
public System.Void AutocreateCustomerSettings()
```

Lifecycle / persistence / interop-renamed:

```csharp
public virtual System.Void Awake()                      // FishNet-generated wrapper
public System.Void Start()
public System.Void OnDestroy()
public virtual System.Void OnStartClient()
public virtual System.Void OnSpawnServer(Il2CppFishNet.Connection.NetworkConnection connection)
public virtual System.Void OnTick()
public virtual System.Void OnMinPass()
public virtual System.Void OnSleepStart()
public virtual System.Void InitializeSaveable()
public virtual System.String GetSaveString()
public virtual Il2CppSystem.Collections.Generic.List<System.String> WriteData(System.String parentFolderPath)
public Il2CppScheduleOne.Persistence.Datas.CustomerData GetCustomerData()
public virtual System.Void Load(Il2CppScheduleOne.Persistence.Datas.CustomerData data)
public System.Void ReceiveCustomerData(Il2CppFishNet.Connection.NetworkConnection conn, Il2CppScheduleOne.Persistence.Datas.CustomerData data)
public virtual System.Void NetworkInitializeIfDisabled()
public virtual System.Void NetworkInitialize__Late()
public virtual System.Void NetworkInitialize___Early()
public virtual System.Boolean ReadSyncVar___ScheduleOne_Economy_Customer(Il2CppFishNet.Serializing.PooledReader PooledReader0, System.UInt32 UInt321, System.Boolean Boolean2)
public System.Single sync___get_value__CurrentAddiction_k__BackingField()
public System.Boolean sync___get_value__HasBeenRecommended_k__BackingField()
public System.Void sync___set_value__CurrentAddiction_k__BackingField(System.Single value, System.Boolean asServer)
public System.Void sync___set_value__HasBeenRecommended_k__BackingField(System.Boolean value, System.Boolean asServer)

// unstable fallback names — do NOT patch by name without a runtime guard
public virtual System.Void Method_Protected_Virtual_New_Void_0()   // = Awake_UserLogic_ScheduleOne.Economy.Customer_Assembly-CSharp.dll (verified: string present in Customer's metadata block)
public System.Void Method_Private_Void_0()                         // = <Start>g__RegisterLoadEvent|140_0 (inferred from string table order + void() signature)
public System.Void Method_Private_Void_EHandoverOutcome_List_1_ItemInstance_Single_PDM_0(Il2CppScheduleOne.UI.Handover.HandoverScreen+EHandoverOutcome outcome, Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemInstance> items, System.Single askingPrice)
                                                                   // = <InstantDealOffered>g__HandoverClosed|205_0 (inferred, same way)

// compiler-generated lambdas exposed publicly
public System.Void _Awake_b__139_0()
public System.Single _GetOrderedDrugTypes_b__240_0(Il2CppScheduleOne.Product.EDrugType x)
public System.Void _HandoverChosen_b__221_0(Il2CppScheduleOne.UI.Handover.HandoverScreen+EHandoverOutcome outcome, Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemInstance> items, System.Single price)
public System.Void _Start_b__140_1(Il2CppScheduleOne.NPCs.Relation.NPCRelationData+EUnlockType <p0>, System.Boolean <p1>)
```

### FishNet RPC triples on `Customer`

Public entry point → writer → logic body → reader. **Patch `RpcLogic___*` to change behaviour on the
authoritative side; patch the public method to intercept before the network hop.**

| Public method | RPC kind | Logic body |
|---|---|---|
| `SetOfferedContract(ContractInfo, GameDateTime)` | ObserversRpc | `RpcLogic___SetOfferedContract_4277245194` |
| `ExpireOffer()` | ServerRpc | `RpcLogic___ExpireOffer_2166136261` |
| `SetUpResponseCallbacks()` | ObserversRpc | `RpcLogic___SetUpResponseCallbacks_2166136261` |
| `ProcessCounterOfferServerSide(string, int, float)` | ServerRpc | `RpcLogic___ProcessCounterOfferServerSide_900355577` |
| `SetContractIsCounterOffer()` | ObserversRpc | `RpcLogic___SetContractIsCounterOffer_2166136261` |
| `SendContractAccepted(EDealWindow, bool)` | ServerRpc | `RpcLogic___SendContractAccepted_507093020` |
| `ReceiveContractAccepted()` | ObserversRpc | `RpcLogic___ReceiveContractAccepted_2166136261` |
| `ReceiveContractRejected()` | ObserversRpc | `RpcLogic___ReceiveContractRejected_2166136261` |
| `ProcessHandoverServerSide(...)` | ServerRpc | `RpcLogic___ProcessHandoverServerSide_3760244802` |
| `ProcessHandoverClient(...)` | ObserversRpc | `RpcLogic___ProcessHandoverClient_2441224929` |
| `ChangeAddiction(float)` | ServerRpc | `RpcLogic___ChangeAddiction_431000436` |
| `AdjustAffinity(EDrugType, float)` | ServerRpc | `RpcLogic___AdjustAffinity_3036964899` |
| `RejectProductRequestOffer()` | ServerRpc | `RpcLogic___RejectProductRequestOffer_2166136261` |
| `RejectProductRequestOffer_Local()` | ObserversRpc | `RpcLogic___RejectProductRequestOffer_Local_2166136261` |
| `ReceiveCustomerData(NetworkConnection, CustomerData)` | TargetRpc | `RpcLogic___ReceiveCustomerData_2280244125` |
| `ProcessSampleServerSide(List<ItemInstance>)` | ServerRpc | `RpcLogic___ProcessSampleServerSide_3704012609` |
| `ProcessSampleClient()` | ObserversRpc | `RpcLogic___ProcessSampleClient_2166136261` |
| `SampleWasSufficient()` | ObserversRpc | `RpcLogic___SampleWasSufficient_2166136261` |
| `SampleWasInsufficient()` | ObserversRpc | `RpcLogic___SampleWasInsufficient_2166136261` |

The `_2166136261` suffix is the FNV-1a offset basis, i.e. the hash of an empty parameter list — every
zero-arg RPC shares it. **The hash suffixes change when a signature changes, so treat them as
version-locked.**

---

## 2. `CustomerData`, affinity and preference types

### `Il2CppScheduleOne.Economy.CustomerData` — the authoring schema

**This is a `UnityEngine.ScriptableObject`.** It is the per-customer tuning asset a "special customer"
mod must author.

```csharp
public class Il2CppScheduleOne.Economy.CustomerData : UnityEngine.ScriptableObject
{
    Il2CppScheduleOne.Economy.CustomerAffinityData DefaultAffinityData { public get; public set; }
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Effects.Effect> PreferredProperties { public get; public set; }
    System.Single MinWeeklySpend { public get; public set; }
    System.Single MaxWeeklySpend { public get; public set; }
    System.Int32 MinOrdersPerWeek { public get; public set; }
    System.Int32 MaxOrdersPerWeek { public get; public set; }
    System.Int32 OrderTime { public get; public set; }
    Il2CppScheduleOne.GameTime.EDay PreferredOrderDay { public get; public set; }
    Il2CppScheduleOne.Economy.ECustomerStandard Standards { public get; public set; }
    System.Boolean CanBeDirectlyApproached { public get; public set; }
    System.Boolean GuaranteeFirstSampleSuccess { public get; public set; }
    System.Single MinMutualRelationRequirement { public get; public set; }
    System.Single MaxMutualRelationRequirement { public get; public set; }
    System.Single CallPoliceChance { public get; public set; }
    System.Single DependenceMultiplier { public get; public set; }
    System.Single BaseAddiction { public get; public set; }
    Il2CppSystem.Action onChanged { public get; public set; }

    public System.Single GetAdjustedWeeklySpend(System.Single normalizedRelationship)
    public System.Void GetOrderDays(System.Single dependence, System.Single normalizedRelationship, Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.GameTime.EDay> days)
    public static System.Single GetQualityScalar(Il2CppScheduleOne.ItemFramework.EQuality quality)
    public System.Void OnValidate()
    public System.Void RandomizeAffinities()
    public System.Void RandomizeFavouriteEffects()
    public System.Void RandomizeTiming()
}
```

That is **the complete field list — 16 data fields + 1 `Action`**. Nothing else is present; there is
no `Region`, no `Group`, no `PreferredPackaging`, no `MaxOrderQuantity` on `CustomerData`
(max quantity per product is the *global static* `Customer.MaxOrderQuantityPerProduct`).

Notes on individual fields:

* `OrderTime` — `System.Int32`, i.e. the game's 24h `HHMM` integer convention (`1430` = 14:30),
  consistent with `QuestWindowConfig.WindowStartTime` / `DealWindowInfo.StartTime`
  (*inferred from type + `S1API.Entities.Customer.CustomerDataBuilder.WithOrderTime(System.Int32 hhmm)`*).
* `PreferredProperties` is a list of `Il2CppScheduleOne.Effects.Effect` (the *effect* objects, not the
  `EProperty` enum). `Il2CppScheduleOne.Product.PropertyUtility` bridges the two:
  `PropertyUtility.Instance.GetProperties(List<System.String> ids)` and
  `PropertyUtility.Instance.PropertiesDict` (`Dictionary<String, Effect>`).
* `MinWeeklySpend` / `MaxWeeklySpend` are validated: the literal
  `"Min weekly spend cannot be greater than max weekly spend."` exists (thrown from `OnValidate`,
  *inferred*).
* `Min/MaxMutualRelationRequirement` gate unlockability against
  `NPCRelationData.GetAverageMutualRelationship()` (*inferred from names + `NPCRelationData` surface*).
* `GetQualityScalar(EQuality)` is the quality→value multiplier used in enjoyment/price maths.
  **Values UNVERIFIED** (IL only).
* `RandomizeAffinities` / `RandomizeFavouriteEffects` / `RandomizeTiming` are editor-time authoring
  helpers, callable at runtime — handy for generating a group's members.

### `Il2CppScheduleOne.Economy.CustomerAffinityData`

```csharp
public class Il2CppScheduleOne.Economy.CustomerAffinityData : Il2CppSystem.Object
{
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Economy.ProductTypeAffinity> ProductAffinities { public get; public set; }

    public System.Void CopyTo(Il2CppScheduleOne.Economy.CustomerAffinityData data)
    public System.Single GetAffinity(Il2CppScheduleOne.Product.EDrugType type)
}
```

### `Il2CppScheduleOne.Economy.ProductTypeAffinity`

```csharp
public class Il2CppScheduleOne.Economy.ProductTypeAffinity : Il2CppSystem.Object
{
    Il2CppScheduleOne.Product.EDrugType DrugType { public get; public set; }
    System.Single Affinity { public get; public set; }
}
```

> **There is no type named `DefaultAffinityData`.** `DefaultAffinityData` is a *property* on
> `CustomerData` of type `CustomerAffinityData`. Verified: `research/raw/02-types-index.txt` contains
> no `DefaultAffinityData` type; the metadata string table lists `DefaultAffinityData` in
> `CustomerData`'s field block.
>
> Live per-customer affinity lives in `Customer.currentAffinityData`, seeded from
> `CustomerData.DefaultAffinityData` via `CustomerAffinityData.CopyTo` (*inferred from `CopyTo`'s
> existence + the runtime literals `"Set affinity of "`, `"Removed affinity of "`,
> `"No affinity data found for product type "`, `"Product affinity is NaN"`,
> `"Product affinities array is too short"`*).

### `Il2CppScheduleOne.Economy.CustomerSatisfaction`

```csharp
public class Il2CppScheduleOne.Economy.CustomerSatisfaction : Il2CppSystem.Object
{
    public static System.Single GetRelationshipChange(System.Single satisfaction)
}
```

Single static utility mapping a 0..1 satisfaction to a relationship delta.

### `Il2CppScheduleOne.Economy.ECustomerStandard` + `StandardsMethod`

```csharp
public enum Il2CppScheduleOne.Economy.ECustomerStandard   // OriginalName("Assembly-CSharp.dll", "ScheduleOne.Economy", "ECustomerStandard")
{
    VeryLow  = 0,
    Low      = 1,
    Moderate = 2,
    High     = 3,
    VeryHigh = 4,
}

public static class Il2CppScheduleOne.Economy.StandardsMethod
{
    public static Il2CppScheduleOne.ItemFramework.EQuality GetCorrespondingQuality(Il2CppScheduleOne.Economy.ECustomerStandard property)
    public static System.String GetName(Il2CppScheduleOne.Economy.ECustomerStandard property)
}
```

### Quality, drug type, order day, deal window

```csharp
public enum Il2CppScheduleOne.ItemFramework.EQuality   // OriginalName(… "ScheduleOne.ItemFramework", "EQuality")
{
    Trash    = 0,
    Poor     = 1,
    Standard = 2,
    Premium  = 3,
    Heavenly = 4,
}

public enum Il2CppScheduleOne.Product.EDrugType        // OriginalName(… "ScheduleOne.Product", "EDrugType")
{
    Marijuana       = 0,
    Methamphetamine = 1,
    Cocaine         = 2,
    MDMA            = 3,
    Shrooms         = 4,
    Heroin          = 5,
}

public enum Il2CppScheduleOne.GameTime.EDay            // OriginalName(… "ScheduleOne.GameTime", "EDay")
{
    Monday = 0, Tuesday = 1, Wednesday = 2, Thursday = 3, Friday = 4, Saturday = 5, Sunday = 6,
}

public enum Il2CppScheduleOne.Economy.EDealWindow      // OriginalName(… "ScheduleOne.Economy", "EDealWindow")
{
    Morning = 0, Afternoon = 1, Night = 2, LateNight = 3,
}

public enum Il2CppScheduleOne.Economy.EContractParty   // OriginalName(… "ScheduleOne.Economy", "EContractParty")
{
    Player = 0, PlayerDealer = 1, Cartel = 2,
}

public enum Il2CppScheduleOne.Economy.EDealerType      // OriginalName(… "ScheduleOne.Economy", "EDealerType")
{
    PlayerDealer = 0, CartelDealer = 1,
}
```

`MDMA` and `Heroin` exist in `EDrugType` but there is no `MDMADefinition`/`HeroinDefinition` in
`Il2CppScheduleOne.Product` (only `WeedDefinition`, `MethDefinition`, `CocaineDefinition`,
`ShroomDefinition`) — those two types are declared but unproduced in v0.4.6.

### `Il2CppScheduleOne.Economy.DealWindowInfo` (struct)

```csharp
public struct Il2CppScheduleOne.Economy.DealWindowInfo : System.ValueType
{
    public System.Int32 StartTime;   // FieldOffset(0)
    public System.Int32 EndTime;     // FieldOffset(4)

    static System.Int32 WINDOW_DURATION_MINS { public get; public set; }
    static System.Int32 WINDOW_COUNT { public get; public set; }
    static Il2CppScheduleOne.Economy.DealWindowInfo Morning { public get; public set; }
    static Il2CppScheduleOne.Economy.DealWindowInfo Afternoon { public get; public set; }
    static Il2CppScheduleOne.Economy.DealWindowInfo Night { public get; public set; }
    static Il2CppScheduleOne.Economy.DealWindowInfo LateNight { public get; public set; }

    public .ctor(System.Int32 startTime, System.Int32 endTime)
    public static Il2CppScheduleOne.Economy.EDealWindow GetWindow(System.Int32 time)
    public static Il2CppScheduleOne.Economy.DealWindowInfo GetWindowInfo(Il2CppScheduleOne.Economy.EDealWindow window)
}
```

---

## 3. There is **no** `CustomerManager`

Verified: `Grep` for `CustomerManager` across all of `research/raw` returns **zero matches**, and
`research/raw/ns/ns-Il2CppScheduleOne.Economy.txt` lists exactly 20 top-level types, none of which is
a manager. The registry/unlock/scheduling responsibilities are split:

| Responsibility | Where it actually lives |
|---|---|
| Customer registry | `static List<Customer> Il2CppScheduleOne.Economy.Customer.LockedCustomers` and `…UnlockedCustomers` (populated in `Awake`/`OnCustomerUnlocked`, *inferred*) |
| "customer unlocked" broadcast | `static Il2CppSystem.Action<Customer> Customer.onCustomerUnlocked` |
| NPC registry & region lookup | `Il2CppScheduleOne.NPCs.NPCManager` |
| Order scheduling | **per-customer**, driven by `Customer.OnMinPass()` / `Customer.OnTick()` → `ShouldTryGenerateDeal()` → `TryGenerateContract(Dealer)` |
| Unlock gating | `Customer.IsUnlockable()`, `Customer.KnownAndRecommended()`, `NPC.RelationData` (`NPCRelationData`) |
| Region grouping | `Il2CppScheduleOne.NPCs.NPC.Region` (`EMapRegion`) + `NPCManager.GetNPCsInRegion(EMapRegion)` |

```csharp
public class Il2CppScheduleOne.NPCs.NPCManager
    : Il2CppScheduleOne.DevUtilities.NetworkSingleton<Il2CppScheduleOne.NPCs.NPCManager>
{
    static Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.NPCs.NPC> NPCRegistry { public get; public set; }
    Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<UnityEngine.Transform> NPCWarpPoints { public get; public set; }
    UnityEngine.Transform NPCContainer { public get; public set; }
    Il2CppScheduleOne.Map.NPCPoI NPCPoIPrefab { public get; public set; }
    Il2CppScheduleOne.Map.NPCPoI PotentialCustomerPoIPrefab { public get; public set; }
    Il2CppScheduleOne.Map.NPCPoI PotentialDealerPoIPrefab { public get; public set; }
    Il2CppScheduleOne.Persistence.Loaders.NPCsLoader loader { public get; public set; }
    System.String SaveFolderName { public get; }
    System.String SaveFileName { public get; }
    System.Int32 LoadOrder { public get; }

    public virtual System.Void Awake()
    public static Il2CppScheduleOne.NPCs.NPC GetNPC(System.String id)
    public static Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.NPCs.NPC> GetNPCsInRegion(Il2CppScheduleOne.Map.EMapRegion region)
    public Il2CppSystem.Collections.Generic.List<UnityEngine.Transform> GetOrderedDistanceWarpPoints(UnityEngine.Vector3 origin)
    public virtual System.String GetSaveString()
    public virtual System.Void InitializeSaveable()
    public virtual System.Void Method_Protected_Virtual_Void_0()   // = Awake_UserLogic_ScheduleOne.NPCs.NPCManager_Assembly-CSharp.dll
    public virtual System.Void OnDestroy()
    public virtual Il2CppSystem.Collections.Generic.List<System.String> WriteData(System.String parentFolderPath)
}
```

**How many customers exist:** not a constant anywhere. It is however many scene NPCs carry a
`Customer` component. Two related literals do exist — `"Reach 10 customers ("` and
`"Unlock 10 customers ("` (achievement text). `Dealer.MAX_CUSTOMERS` caps *assignment per dealer*,
not the world total. Actual world count is **UNVERIFIED** from static data (requires a scene scan or
counting `Customer.LockedCustomers.Count + Customer.UnlockedCustomers.Count` at runtime).

---

## 4. Order / contract generation pipeline

### `Il2CppScheduleOne.Quests.ContractInfo` — the *offer* DTO (plain object, not a Quest)

```csharp
public class Il2CppScheduleOne.Quests.ContractInfo : Il2CppSystem.Object
{
    System.Single Payment { public get; public set; }
    Il2CppScheduleOne.Product.ProductList Products { public get; public set; }
    System.String DeliveryLocationGUID { public get; public set; }
    Il2CppScheduleOne.Quests.QuestWindowConfig DeliveryWindow { public get; public set; }
    System.Boolean Expires { public get; public set; }
    System.Int32 ExpiresAfter { public get; public set; }
    System.Int32 PickupScheduleIndex { public get; public set; }
    System.Boolean IsCounterOffer { public get; public set; }
    Il2CppScheduleOne.Economy.DeliveryLocation DeliveryLocation { public get; public set; }

    public .ctor(System.Single payment, Il2CppScheduleOne.Product.ProductList products, System.String deliveryLocationGUID,
                 Il2CppScheduleOne.Quests.QuestWindowConfig deliveryWindow, System.Boolean expires, System.Int32 expiresAfter,
                 System.Int32 pickupScheduleIndex, System.Boolean isCounterOffer)
    public .ctor()

    public Il2CppScheduleOne.Dialogue.DialogueChain ProcessMessage(Il2CppScheduleOne.Dialogue.DialogueChain messageChain)
}
```

### `Il2CppScheduleOne.Quests.QuestWindowConfig` — the deal window

```csharp
public class Il2CppScheduleOne.Quests.QuestWindowConfig : Il2CppSystem.Object
{
    System.Boolean IsEnabled { public get; public set; }
    System.Int32 WindowStartTime { public get; public set; }
    System.Int32 WindowEndTime { public get; public set; }
}
```

### `Il2CppScheduleOne.Product.ProductList` — the order lines

```csharp
public class Il2CppScheduleOne.Product.ProductList : Il2CppSystem.Object
{
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Product.ProductList+Entry> entries { public get; public set; }

    public System.String GetCommaSeperatedString()   // sic — one 'e'
    public System.String GetLineSeperatedString()    // sic
    public System.String GetQualityString()
    public System.Int32 GetTotalQuantity()
}

public class Il2CppScheduleOne.Product.ProductList+Entry : Il2CppSystem.Object
{
    System.String ProductID { public get; public set; }
    Il2CppScheduleOne.ItemFramework.EQuality Quality { public get; public set; }
    System.Int32 Quantity { public get; public set; }

    public .ctor(System.String productID, Il2CppScheduleOne.ItemFramework.EQuality quality, System.Int32 quantity)
}
```

### `Il2CppScheduleOne.Quests.Contract` — the *accepted* contract (a live `Quest` MonoBehaviour)

```csharp
public class Il2CppScheduleOne.Quests.Contract : Il2CppScheduleOne.Quests.Quest   // Quest : UnityEngine.MonoBehaviour
{
    static System.Int32 DefaultExpiryTime { public get; public set; }
    static System.Single ExcessProductsMatchSumMultiplier { public get; public set; }
    static Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Quests.Contract> Contracts { public get; public set; }

    Il2CppFishNet.Object.NetworkObject Customer { public get; public set; }   // note: NetworkObject, not Customer
    Il2CppScheduleOne.Economy.Dealer Dealer { public get; public set; }
    System.Single Payment { public get; public set; }
    Il2CppScheduleOne.Product.ProductList ProductList { public get; public set; }
    Il2CppScheduleOne.Economy.DeliveryLocation DeliveryLocation { public get; public set; }
    Il2CppScheduleOne.Quests.QuestWindowConfig DeliveryWindow { public get; public set; }
    System.Int32 PickupScheduleIndex { public get; public set; }
    Il2CppScheduleOne.GameTime.GameDateTime AcceptTime { public get; public set; }
    System.Boolean completedContractsIncremented { public get; public set; }

    public virtual System.Void InitializeContract(System.String title, System.String description,
        Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppScheduleOne.Persistence.Datas.QuestEntryData> entries,
        System.String guid, Il2CppScheduleOne.Economy.Customer customer, System.Single payment,
        Il2CppScheduleOne.Product.ProductList products, System.String deliveryLocationGUID,
        Il2CppScheduleOne.Quests.QuestWindowConfig deliveryWindow, System.Int32 pickupScheduleIndex,
        Il2CppScheduleOne.GameTime.GameDateTime acceptTime)
    public virtual System.Void SilentlyInitializeContract(/* identical parameter list */)

    public virtual System.Boolean CanExpire()
    public virtual System.Void Complete(System.Boolean network = True)
    public virtual System.Void End()
    public virtual System.Void Expire(System.Boolean network = True)
    public virtual System.Void Fail(System.Boolean network = True)
    public System.Boolean DoesProductListMatchSpecified(Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemInstance> items, System.Boolean enforceQuality)
    public Il2CppSystem.Collections.Generic.Dictionary<Il2CppScheduleOne.Product.ProductItemInstance, System.Single> GetDescendingMatchRatings(Il2CppScheduleOne.Product.ProductList+Entry requestedItem, Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemInstance> providedItems)
    public System.Single GetProductListMatch(Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemInstance> items, out System.Int32& matchedProductCount)
    public virtual Il2CppScheduleOne.Persistence.Datas.SaveData GetSaveData()
    public System.Void SetDealer(Il2CppScheduleOne.Economy.Dealer dealer)
    public virtual System.Void SubmitPayment(System.Single bonusTotal)
    public System.Void UpdatePoI()
    public System.Void UpdateTiming()
    public virtual System.Void OnUncappedMinPass()
    public virtual System.Void SendExpiredNotification()
    public virtual System.Void SendExpiryReminder()
    public virtual System.Boolean ShouldQuestShowUI()
    public System.Boolean ShouldSave()
    public virtual System.Boolean ShouldShowJournalEntry()
    public virtual System.Void Start()
    public System.Void OnDestroy()
}

public class Il2CppScheduleOne.Quests.Contract+BonusPayment : Il2CppSystem.Object
{
    System.String Title { public get; public set; }
    System.Single Amount { public get; public set; }
    public .ctor(System.String title, System.Single amount)
}
```

### `Il2CppScheduleOne.Quests.QuestManager` — contract factory

```csharp
public class Il2CppScheduleOne.Quests.QuestManager
    : Il2CppScheduleOne.DevUtilities.NetworkSingleton<Il2CppScheduleOne.Quests.QuestManager>
{
    Il2CppScheduleOne.Quests.Contract ContractPrefab { public get; public set; }
    UnityEngine.Transform ContractContainer { public get; public set; }
    Il2CppScheduleOne.Quests.DeaddropQuest DeaddropCollectionPrefab { public get; public set; }
    // …

    public Il2CppScheduleOne.Quests.Contract ContractAccepted(Il2CppScheduleOne.Economy.Customer customer,
        Il2CppScheduleOne.Quests.ContractInfo contractData, System.Boolean track, System.String guid,
        Il2CppScheduleOne.Economy.Dealer dealer)

    public Il2CppScheduleOne.Quests.Contract CreateContract_Local(System.String title, System.String description,
        Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppScheduleOne.Persistence.Datas.QuestEntryData> entries,
        System.String guid, System.Boolean tracked, Il2CppScheduleOne.Economy.Customer customer, System.Single payment,
        Il2CppScheduleOne.Product.ProductList products, System.String deliveryLocationGUID,
        Il2CppScheduleOne.Quests.QuestWindowConfig deliveryWindow, System.Boolean expires,
        Il2CppScheduleOne.GameTime.GameDateTime expiry, System.Int32 pickupScheduleIndex,
        Il2CppScheduleOne.GameTime.GameDateTime acceptTime, Il2CppScheduleOne.Economy.Dealer dealer = null)

    public System.Void CreateContract_Networked(Il2CppFishNet.Connection.NetworkConnection conn, System.String title,
        System.String description, System.String guid, System.Boolean tracked, Il2CppFishNet.Object.NetworkObject customer,
        Il2CppScheduleOne.Quests.ContractInfo contractData, Il2CppScheduleOne.GameTime.GameDateTime expiry,
        Il2CppScheduleOne.GameTime.GameDateTime acceptTime, Il2CppFishNet.Object.NetworkObject dealerObj = null)
    // ObserversRpc — logic body: RpcLogic___CreateContract_Networked_2526053753
}
```

### The generation pipeline, in call order

1. **Tick** — `Customer.OnMinPass()` (and `Customer.OnTick()`) advance
   `TimeSinceLastDealOffered` / `TimeSinceLastDealCompleted` / `minsSinceUnlocked`, drain addiction
   (`ADDICTION_DRAIN_PER_DAY`), and call the gates. *(inferred: `OnMinPass`/`OnTick` are the only
   per-time hooks on `Customer`; the counters are all `Int32` minute counters.)*
2. **Gate** — `Customer.ShouldTryGenerateDeal()` → `bool`. Debug literal `"Should try generate deal: "`.
   Rejection literals seen in metadata:
   `"Customer already has a contract!"`, `"Customer already has a pending offer"`,
   `"Customer recently completed a deal"`, `" has not waited long enough since last deal"`,
   `" has not waited long enough since last offer"`, `"Already offered today"`,
   `"Already recently offered"`, `" has too low appeal for any products"`.
   Related: `Customer.IsDealTime()` (literal `"Is deal time: "`) and the static
   `Customer.MinsSinceLastDealOfferedAllCustomers()` (a *global* pacing throttle across all customers).
3. **Order days** — `CustomerData.GetOrderDays(float dependence, float normalizedRelationship, List<EDay> days)`
   fills the caller's list; `Customer._cachedOrderDays` caches the result. Randomisation between
   `MinOrdersPerWeek`..`MaxOrdersPerWeek` happens **inside** `GetOrderDays` (**UNVERIFIED** exact
   distribution — IL only).
4. **Budget** — `CustomerData.GetAdjustedWeeklySpend(float normalizedRelationship)` interpolates
   `MinWeeklySpend`..`MaxWeeklySpend`. Related literal: `"Spend under threshold: "`.
5. **Catalogue** — `Customer.GetOrderableProductsWithQuantities(Dealer dealer = null)` →
   `List<Tuple<ProductDefinition, int>>`. When `dealer == null` the pool is the player's
   `ProductManager.ListedProducts`; when a `Dealer` is supplied the pool is that dealer's inventory
   via `Dealer.GetOrderableProducts(EQuality minQuality)` / `Dealer.GetOrderableProductQuantity(string productID, EQuality minQuality, EQuality maxQuality)`
   (*inferred from the `dealer` parameter and the `Dealer` API shape*).
   `Customer.GetOrderableProducts(Dealer)` is the same thing projected to definitions only
   (lambda `_GetOrderableProducts_b__155_0`).
6. **Pick** — `Customer.GetWeightedRandomProduct(Dealer dealer, out float appeal, out int orderableQuantity)`.
   **This is where the randomisation lives.** The closure
   `Customer+__c__DisplayClass159_0` holds `Dictionary<ProductDefinition, float> productAppeal` and
   `_GetWeightedRandomProduct_b__0(Tuple<ProductDefinition,int> x)` supplies the weight — i.e. a
   weighted random pick over per-product appeal. Appeal is computed from
   `Customer.GetProductEnjoyment(ProductDefinition, EQuality)` /
   `GetProductEnjoyment(ProductDefinition)`, bounded by the statics
   `AFFINITY_MAX_EFFECT`, `PROPERTY_MAX_EFFECT`, `QUALITY_MAX_EFFECT`, and floored by
   `MIN_ORDER_APPEAL` (rejection literal `" has too low appeal for any products"`).
7. **Quantity clamp** — per-product quantity is clamped by `Customer.MaxOrderQuantityPerProduct`,
   quality tolerance by `Customer.QualityTierTolerance`, and (for the *global* order size) by
   `Il2CppScheduleOne.Levelling.LevelManager.GetOrderLimitMultiplier(FullRank rank)` /
   `GetRankOrderLimitMultiplier(ERank rank)` (*inferred from names — the only order-limit functions
   in the assembly*).
8. **Build** — `Customer.TryGenerateContract(Dealer dealer)` → `ContractInfo` (null on failure).
   It fills `Payment`, a `ProductList`, `DeliveryLocationGUID` (from
   `Customer.GetDeliveryLocation()` / `MapRegionData.GetRandomUnscheduledDeliveryLocation()`),
   a `QuestWindowConfig` and `ExpiresAfter` (default `Contract.DefaultExpiryTime`,
   offer expiry `Customer.OFFER_EXPIRY_TIME_MINS`).
9. **Offer** — `Customer.OfferContract(ContractInfo info)` (to the player) **or**
   `Customer.OfferContractToDealer(ContractInfo info, Dealer dealer)` (to an assigned dealer).
   Server-side. `OfferContract` → `SetOfferedContract(info, offerTime)` (ObserversRpc) →
   `NotifyPlayerOfContract(contract, offerMessage, canAccept, canReject, canCounterOffer = true)` →
   `SetUpResponseCallbacks()` (ObserversRpc). Literal `"Setting offer: "`.
10. **Window timings** — `Customer.GetContractTimings(QuestWindowConfig dealWindow, out int softStartTime, out int hardStartTime, out int endTime)`
    (static). Guard literal: `"Deal window is null in GetContractTimings"`.
    `DealWindowInfo.GetWindow(int time)` maps a clock time to `EDealWindow`.

### Dealer-side acceptance

```csharp
public virtual System.Boolean Il2CppScheduleOne.Economy.Dealer.ShouldAcceptContract(
    Il2CppScheduleOne.Quests.ContractInfo contractInfo, Il2CppScheduleOne.Economy.Customer customer)
public virtual System.Void Il2CppScheduleOne.Economy.Dealer.ContractedOffered(
    Il2CppScheduleOne.Quests.ContractInfo contractInfo, Il2CppScheduleOne.Economy.Customer customer)   // sic — "Contracted", not "Contract"
public System.Void Il2CppScheduleOne.Economy.Dealer.AddContract(Il2CppScheduleOne.Quests.Contract contract)
public Il2CppScheduleOne.Economy.EDealWindow Il2CppScheduleOne.Economy.Dealer.GetDealWindow()
public System.Int32 Il2CppScheduleOne.Economy.Dealer.GetContractCountInWindow(Il2CppScheduleOne.Economy.EDealWindow window)
public System.Void Il2CppScheduleOne.Economy.Dealer.SortContracts()
public System.Void Il2CppScheduleOne.Economy.Dealer.CustomerContractEnded(Il2CppScheduleOne.Quests.Contract contract)
```

`Dealer.GetDealWindow()` picks the least-loaded window using `GetContractCountInWindow`
(*inferred*); literals `"Contract accepted by dealer "`, `"Dealer contract expired! It was assigned to dealer: "`,
`"Dealer contract failed! It was assigned to dealer: "`, `"SortContracts"`.

**Note on the spec names in the brief:** `GetAvailableProducts` and `GetOrderableProductQuantity` are
methods on **`Dealer`**, not on `Customer`. `GetDealWindow` and `GetContractCountInWindow` are also
**`Dealer`** methods. `AddContract` is a **`Dealer`** method. `GetOrderDays` and
`GetAdjustedWeeklySpend` are **`CustomerData`** methods. There is no `Deal` type — the pair is
`ContractInfo` (offer) / `Contract` (accepted). `QuestWindowConfig` is the deal-window config type.

---

## 5. How a sale executes, end to end

Side annotations: **[S]** server-authoritative, **[C]** client/local, **[O]** replicated to observers,
**[T]** target-only. Derived from the RPC kind of each method (`RpcWriter___Server_*` = ServerRpc,
`…Observers_*` = ObserversRpc, `…Target_*` = TargetRpc) plus the literals
`"SendContractAccepted can only be called on the server!"` and `"Contract accepted called on client!"`.

### 5a. Offer → accept

| # | Call | Side |
|---|---|---|
| 1 | `Customer.ShouldTryGenerateDeal()` → `Customer.TryGenerateContract(Dealer)` | **[S]** |
| 2 | `Customer.OfferContract(ContractInfo)` | **[S]** |
| 3 | `Customer.SetOfferedContract(ContractInfo, GameDateTime)` → `RpcLogic___SetOfferedContract_4277245194` | **[O]** |
| 4 | `Customer.NotifyPlayerOfContract(ContractInfo, MessageChain, bool canAccept, bool canReject, bool canCounterOffer = true)` | **[C]** builds the phone message |
| 5 | `MSGConversation.SendMessageChain(MessageChain messages, float initialDelay = 0, bool notify = true, bool network = true)` via `MessagingManager.SendMessageChain(MessageChain m, string npcID, float initialDelay, bool notify)` | **[C]**→**[S]**→**[O]** |
| 6 | `Customer.SetUpResponseCallbacks()` → `RpcLogic___SetUpResponseCallbacks_2166136261` | **[O]** |
| 7 | Player taps a `Response` → `MSGConversation.ResponseChosen(Response r, bool network)` → `Customer.AcceptContractClicked()` / `Customer.CounterOfferClicked()` | **[C]** |
| 8 | `Il2CppScheduleOne.UI.Phone.Messages.DealWindowSelector.SetIsOpen(bool open, MSGConversation conversation, Action<EDealWindow> callback = null)` → `ButtonClicked(EDealWindow window)` | **[C]** |
| 9 | `Customer.PlayerAcceptedContract(EDealWindow window)` — literal `"Player accepted contract in window "` | **[C]** |
| 10 | `Customer.SendContractAccepted(EDealWindow window, bool trackContract)` → `RpcLogic___SendContractAccepted_507093020` | **[C]**→**[S]** |
| 11 | `Customer.ContractAccepted(EDealWindow window, bool trackContract, Dealer dealer)` → `Contract` | **[S]** |
| 12 | `QuestManager.ContractAccepted(Customer customer, ContractInfo contractData, bool track, string guid, Dealer dealer)` → `QuestManager.CreateContract_Local(...)` + `QuestManager.CreateContract_Networked(...)` | **[S]** then **[O]** |
| 13 | `Contract.InitializeContract(...)` / `Contract.SilentlyInitializeContract(...)`; `Customer.AssignContract(Contract)` fires `onContractAssigned` | **[S]**/**[O]** |
| 14 | `Customer.ReceiveContractAccepted()` → `RpcLogic___ReceiveContractAccepted_2166136261`, then `Customer.PlayContractAcceptedReaction()` | **[O]** |

Rejection path: `Customer.ContractRejected()` → `Customer.ReceiveContractRejected()` **[O]** →
`Customer.PlayContractRejectedReaction()`; relationship hit `Customer.DEAL_REJECTED_RELATIONSHIP_CHANGE`.
Expiry path: `Customer.UpdateOfferExpiry()` → `Customer.ExpireOffer()` **[S]** →
`RpcLogic___ExpireOffer_2166136261`; message keys `offer_expired`, `contract_expired`.

Counter-offer path: `Customer.CounterOfferClicked()` **[C]** →
`Customer.SendCounteroffer(ProductDefinition product, int quantity, float price)` →
`Customer.ProcessCounterOfferServerSide(string productID, int quantity, float price)` **[S]** →
`Customer.EvaluateCounteroffer(ProductDefinition, int, float)` (uses
`Customer.GetValueProposition(ProductDefinition product, float price)`) →
`Customer.SetContractIsCounterOffer()` **[O]**. Message keys `counteroffer_accepted`,
`counteroffer_rejected`; literal `"Counter offer already sent"`.

### 5b. Attendance → handover

| # | Call | Side |
|---|---|---|
| 1 | `Customer.UpdateDealAttendance()` (tolerance `Customer.DEAL_ATTENDANCE_TOLERANCE`, travel `MIN_TRAVEL_TIME`..`MAX_TRAVEL_TIME`) | **[S]** |
| 2 | `CustomerAttendDealBehaviour.SetContract(Contract contract)` → `Activate()` → `OnActiveTick()` → `IsAtDestination()` / `CheckWarp()` / `EnsureNPCHasEnoughCash()` | **[S]** |
| 3 | `Customer.IsAtDealLocation()`, `Customer.SetIsAwaitingDelivery(bool awaiting)` | **[S]** |
| 4 | `Customer.IsReadyForHandover(bool enabled)` gates the dialogue choice `completeContractChoice` | **[C]** |
| 5 | `Customer.IsHandoverChoiceValid(out string invalidReason)` → `Customer.HandoverChosen()` | **[C]** |
| 6 | `HandoverScreen.Open(Contract contract, Customer customer, HandoverScreen+EMode mode, Action<EHandoverOutcome, List<ItemInstance>, float> callback, Func<List<ItemInstance>, float, float> successChanceMethod, bool requireFullChanceOfSuccess = false)` | **[C]** |
| 7 | `HandoverScreen.CustomerItemsChanged()` / `UpdateSuccessChance()` / `GetError(out string err)` / `GetWarning(out string warning)` / `PriceChanged(float newPrice)` | **[C]** |
| 8 | `HandoverScreen.DonePressed()` → `HandoverScreen.Close(EHandoverOutcome outcome)` → invokes `_onHandoverCompleteCallback` = `Customer._HandoverChosen_b__221_0(EHandoverOutcome, List<ItemInstance>, float)` | **[C]** |
| 9 | `Customer.ProcessHandover(EHandoverOutcome outcome, Contract contract, List<ItemInstance> items, bool handoverByPlayer, bool giveBonuses = true)` | **[C]** |
| 10 | `Customer.EvaluateDelivery(Contract contract, List<ItemInstance> providedItems, out float highestAddiction, out EDrugType mainTypeType, out int matchedProductCount, out float qualityDifference)` → satisfaction | **[C]** computes, **[S]** trusts |
| 11 | `Customer.ProcessHandoverServerSide(EHandoverOutcome outcome, List<ItemInstance> items, bool handoverByPlayer, float totalPayment, ProductList productList, float satisfaction, NetworkObject dealerObject)` → `RpcLogic___ProcessHandoverServerSide_3760244802` | **[C]**→**[S]** |
| 12 | `Contract.SubmitPayment(float bonusTotal)` → `MoneyManager.ChangeCashBalance(...)` (player) or `Dealer.SubmitPayment(float payment)` (dealer route) | **[S]** |
| 13 | `Contract.Complete(bool network = true)` → `Customer.CurrentContractEnded(EQuestState outcome)` | **[S]** |
| 14 | `Customer.ChangeAddiction(float change)` **[S]**, `Customer.AdjustAffinity(EDrugType drugType, float change)` **[S]** | **[S]** |
| 15 | `CustomerSatisfaction.GetRelationshipChange(float satisfaction)` → `NPCRelationData.ChangeRelationship(float deltaChange, bool network = true)` | **[S]** |
| 16 | `ProductManager.RecordContractReceipt(NetworkConnection conn, ContractReceipt receipt)` (+ `onContractReceiptRecorded`) | **[S]**→**[O]** |
| 17 | `Customer.ProcessHandoverClient(float satisfaction, bool handoverByPlayer, string npcToRecommend, EHandoverOutcome outcome)` → `RpcLogic___ProcessHandoverClient_2441224929` | **[O]** |
| 18 | `Customer.ContractWellReceived(string npcToRecommend)` → `Customer.RecommendCustomer(Customer friend)` / `RecommendDealer(Dealer)` / `RecommendSupplier(Supplier)`; fires `onDealCompleted` | **[C]** |
| 19 | `Customer.CalculateTopWeeklyPurchases(out List<StringIntPair> mostPurchasedProducts, out float totalSpent)` maintains `WeeklyPurchaseRecord` | **[C]** |
| 20 | `Customer.ConsumeProduct(ItemInstance item)` → `ProductItemInstance.ApplyEffectsToNPC(NPC npc)` | **[S]** |

Popup/UX: `Il2CppScheduleOne.UI.NewCustomerPopup.PlayPopup(Customer customer)`;
literal `"Playing deal completion popup for {0} with satisfaction {1:P0} and base payment {2}"`,
`DealCompletedPopup`, `"New Customer Unlocked!"` / `"New Customers Unlocked!"`.

### `Il2CppScheduleOne.UI.Handover.HandoverScreen` (full)

```csharp
public class Il2CppScheduleOne.UI.Handover.HandoverScreen
    : Il2CppScheduleOne.DevUtilities.Singleton<Il2CppScheduleOne.UI.Handover.HandoverScreen>
{
    static System.Int32 CustomerSlotCount { public get; public set; }
    static System.Single VehicleMaxDistance { public get; public set; }
    System.Boolean IsOpen { public get; public set; }
    Il2CppScheduleOne.Quests.Contract CurrentContract { public get; public set; }
    Il2CppScheduleOne.Economy.Customer CurrentCustomer { public get; public set; }
    Il2CppSystem.Action<Il2CppScheduleOne.UI.Handover.HandoverScreen+EMode> OnHandoverScreenOpened { public get; public set; }
    Il2CppSystem.Action OnHandoverScreenClosed { public get; public set; }
    UnityEngine.Gradient SuccessColorMap { public get; public set; }
    Il2CppScheduleOne.UI.AmountSelector PriceSelector { public get; public set; }
    Il2CppScheduleOne.UI.Handover.HandoverScreenDetailPanel DetailPanel { public get; public set; }
    Il2CppScheduleOne.UI.Handover.HandoverScreen+EMode _mode { public get; public set; }
    Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppScheduleOne.ItemFramework.ItemSlot> _customerSlots { public get; public set; }
    System.Boolean _requireFullChanceOfSuccess { public get; public set; }
    Il2CppScheduleOne.UI.Handover.HandoverScreen+EHandoverOutcome _outcome { public get; public set; }
    Il2CppSystem.Action<Il2CppScheduleOne.UI.Handover.HandoverScreen+EHandoverOutcome, Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemInstance>, System.Single> _onHandoverCompleteCallback { public get; public set; }
    Il2CppSystem.Func<Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemInstance>, System.Single, System.Single> _successChanceMethod { public get; public set; }
    // + ~20 UI refs (labels, containers, buttons)

    public System.Void Open(Il2CppScheduleOne.Quests.Contract contract, Il2CppScheduleOne.Economy.Customer customer,
        Il2CppScheduleOne.UI.Handover.HandoverScreen+EMode mode,
        Il2CppSystem.Action<Il2CppScheduleOne.UI.Handover.HandoverScreen+EHandoverOutcome, Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemInstance>, System.Single> callback,
        Il2CppSystem.Func<Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemInstance>, System.Single, System.Single> successChanceMethod,
        System.Boolean requireFullChanceOfSuccess = False)
    public System.Void Close(Il2CppScheduleOne.UI.Handover.HandoverScreen+EHandoverOutcome outcome)
    public System.Void ClearCustomerSlots(System.Boolean returnToOriginals)
    public System.Void CustomerItemsChanged()
    public System.Void DonePressed()
    public System.Void Exit(Il2CppScheduleOne.ExitAction action)
    public Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemInstance> GetCustomerItems(System.Boolean onlyPackagedProduct = True)
    public System.Int32 GetCustomerItemsCount(System.Boolean onlyPackagedProduct = True)
    public System.Single GetCustomerItemsValue()
    public System.Boolean GetError(out System.String& err)
    public System.Boolean GetWarning(out System.String& warning)
    public System.Void OnClose()
    public System.Void PriceChanged(System.Single newPrice)
    public virtual System.Void Start()
    public System.Void TestOpen()
    public System.Void Update()
    public System.Void UpdateDoneButton()
    public System.Void UpdateSuccessChance()
}

public enum Il2CppScheduleOne.UI.Handover.HandoverScreen+EHandoverOutcome { Cancelled = 0, Finalize = 1 }
public enum Il2CppScheduleOne.UI.Handover.HandoverScreen+EMode          { Contract = 0, Sample = 1, Offer = 2 }

public class Il2CppScheduleOne.UI.Handover.HandoverScreenDetailPanel : UnityEngine.MonoBehaviour
{
    UnityEngine.RectTransform Container { public get; public set; }
    Il2CppTMPro.TextMeshProUGUI NameLabel { public get; public set; }
    UnityEngine.RectTransform RelationshipContainer { public get; public set; }
    UnityEngine.UI.Scrollbar RelationshipScrollbar { public get; public set; }
    UnityEngine.RectTransform AddictionContainer { public get; public set; }
    UnityEngine.UI.Scrollbar AdditionScrollbar { public get; public set; }   // sic — "Addition"
    UnityEngine.UI.Image StandardsStar { public get; public set; }
    Il2CppTMPro.TextMeshProUGUI StandardsLabel { public get; public set; }
    Il2CppTMPro.TextMeshProUGUI FavouriteDrugLabel { public get; public set; }
    Il2CppTMPro.TextMeshProUGUI EffectsLabel { public get; public set; }

    public System.Void Close()
    public System.Void Open(Il2CppScheduleOne.Economy.Customer customer)
}
```

`HandoverScreen` is used for **all three** flows (`EMode.Contract`, `.Sample`, `.Offer`); the
success-chance delegate is what differs — `Customer.GetSampleSuccess(List<ItemInstance>, float)`
for samples, `Customer.GetOfferSuccessChance(List<ItemInstance>, float askingPrice)` for offers.
Literal `"% chance of customer accepting"`, `"Insert product to offer to "`,
`"No items offered to customer "`, `"Customer expectations not met"`.

### Messaging / dialogue keys used by the deal pipeline

Verbatim string literals from `global-metadata.dat` (these are `DialogueContainer`/`MessageChain`
lookup keys, *inferred* from their snake_case form and the surrounding API):

`contract_request`, `first_contract_request`, `urgent_contract`, `contract`, `contract_accepted`,
`contract_rejected`, `contract_expired`, `contract_done`, `counteroffer_accepted`,
`counteroffer_rejected`, `offer_expired`, `offer_reject`, `deal_completed`, `deal_rejected`,
`customer_rejected_deal`, `awaiting_deal`, `late_deal`, `drugdeal`, `post_deal_recommend`,
`post_deal_recommend_dealer`, `sample_offer_rejected`, `sample_offer_rejected_police`,
`cartel_deal_request`, `cartel_deal_expired`, `cartel_deal_overdue`,
`dealer_rob_defended`, `dealer_rob_loss`, `dealer_rob_partially_defended`.

Dialogue *choice* labels: `ACCEPT_CONTRACT`, `REJECT_CONTRACT`, `COUNTEROFFER`, `ACCEPT_DEAL`,
`REFUSE_DEAL`, `OFFER`, `DIG_OFFER`. UI strings: `[Complete Deal]`, `[Counter-offer]`,
`[Make an offer]`, `[Schedule Deal]`, `Offer Deal`, `Complete Deal`.

---

## 6. `Il2CppScheduleOne.Economy.Dealer` and its 7 subclasses

```csharp
public class Il2CppScheduleOne.Economy.Dealer : Il2CppScheduleOne.NPCs.NPC
```

**Unlike `Customer`, `Dealer` IS an `NPC` subclass.** 7 direct subclasses
(`research/raw/03-subclasses.txt` line 1167):

```
BASE Il2CppScheduleOne.Economy.Dealer   (7 direct subclasses)
    Il2CppScheduleOne.Cartel.CartelDealer
    Il2CppScheduleOne.NPCs.CharacterClasses.Benji
    Il2CppScheduleOne.NPCs.CharacterClasses.Brad
    Il2CppScheduleOne.NPCs.CharacterClasses.Jane
    Il2CppScheduleOne.NPCs.CharacterClasses.Leo
    Il2CppScheduleOne.NPCs.CharacterClasses.Molly
    Il2CppScheduleOne.NPCs.CharacterClasses.Wei
```

### Constants

```csharp
static System.Int32 MAX_CUSTOMERS { public get; public set; }
static System.Int32 DEAL_ARRIVAL_DELAY { public get; public set; }
static System.Int32 MIN_TRAVEL_TIME { public get; public set; }
static System.Int32 MAX_TRAVEL_TIME { public get; public set; }
static System.Int32 OVERFLOW_SLOT_COUNT { public get; public set; }
static System.Single CASH_REMINDER_THRESHOLD { public get; public set; }
static System.Single RELATIONSHIP_CHANGE_PER_DEAL { public get; public set; }
static UnityEngine.Color32 DealerLabelColor { public get; public set; }
static System.Int32 NegativeQualityTolerance { public get; public set; }
static System.Int32 PositiveQualityTolerance { public get; public set; }
static Il2CppSystem.Action<Il2CppScheduleOne.Economy.Dealer> onDealerRecruited { public get; public set; }
static Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Economy.Dealer> AllPlayerDealers { public get; public set; }
```

(Numeric values **UNVERIFIED** — IL only. External research in `research-ext/DESIGN-INTENT.md` records
the shipped values as 20% cut / 10 customers as of v0.4.3; the 20% cut is
`DealerNPCData.SalesCutPercentage`, not a `Dealer` constant.)

### State

```csharp
System.Boolean IsRecruited { public get; public set; }
Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemSlot> ItemSlots { public get; public set; }
Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppScheduleOne.ItemFramework.ItemSlot> overflowSlots { public get; public set; }
Il2CppScheduleOne.Map.NPCPoI PotentialDealerPoI { public get; public set; }
Il2CppScheduleOne.Map.NPCPoI DealerPoI { public get; public set; }
System.Single Cash { public get; public set; }
Il2CppFishNet.Object.Synchronizing.SyncVar<System.Single> syncVar____Cash_k__BackingField { public get; public set; }
System.Single SyncAccessor_<Cash>k__BackingField { public get; public set; }
Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Economy.Customer> AssignedCustomers { public get; public set; }
Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Quests.Contract> ActiveContracts { public get; public set; }
Il2CppScheduleOne.Quests.Contract currentContract { public get; public set; }
System.Boolean HasBeenRecommended { public get; public set; }
Il2CppSystem.Action onContractAccepted { public get; public set; }
Il2CppSystem.Action OnRecommended { public get; public set; }
Il2CppSystem.Action OnCompleteDeal { public get; public set; }
Il2CppScheduleOne.Map.NPCEnterableBuilding Home { public get; public set; }
Il2CppScheduleOne.NPCs.Schedules.NPCEvent_StayInBuilding HomeEvent { public get; public set; }
Il2CppScheduleOne.Dialogue.DialogueController_Dealer DialogueController { public get; public set; }
Il2CppScheduleOne.Dialogue.DialogueController+DialogueChoice recruitChoice { public get; public set; }
Il2CppScheduleOne.Dialogue.DialogueController+DialogueChoice collectCashChoice { public get; public set; }
Il2CppScheduleOne.Dialogue.DialogueController+DialogueChoice assignCustomersChoice { public get; public set; }
System.Int32 itemCountOnTradeStart { public get; public set; }
Il2CppScheduleOne.NPCs.Behaviour.DealerAttendDealBehaviour _attendDealBehaviour { public get; public set; }
Il2CppScheduleOne.NPCs.Framework.DealerNPCData DealerData { public get; }
```

### Methods (143; the non-RPC-plumbing ones)

```csharp
// recruitment
public System.Boolean CanOfferRecruitment(out System.String& reason)
public virtual System.Void RecruitmentRequested()
public System.Void InitialRecruitment()                                   // ServerRpc → RpcLogic___InitialRecruitment_2166136261
public virtual System.Void SetIsRecruited(Il2CppFishNet.Connection.NetworkConnection conn)  // Observers+Target
public System.Void MarkAsRecommended()                                    // ServerRpc
public System.Void SetRecommended()                                       // ObserversRpc
public virtual System.Void OnDealerUnlocked(Il2CppScheduleOne.NPCs.Relation.NPCRelationData+EUnlockType unlockType, System.Boolean b)
public virtual System.Void UpdatePotentialDealerPoI()
public System.Void SetupPoI()
public System.Void SetUpDialogue()

// customer assignment
public virtual System.Void AddCustomer(Il2CppScheduleOne.Economy.Customer customer)
public System.Void AddCustomer_Server(System.String npcID)                // ServerRpc
public System.Void AddCustomer_Client(Il2CppFishNet.Connection.NetworkConnection conn, System.String npcID)  // Observers+Target
public virtual System.Void RemoveCustomer(Il2CppScheduleOne.Economy.Customer customer)
public System.Void RemoveCustomer(System.String npcID)                    // ObserversRpc
public System.Void SendRemoveCustomer(System.String npcID)                // ServerRpc

// contracts
public virtual System.Boolean ShouldAcceptContract(Il2CppScheduleOne.Quests.ContractInfo contractInfo, Il2CppScheduleOne.Economy.Customer customer)
public virtual System.Void ContractedOffered(Il2CppScheduleOne.Quests.ContractInfo contractInfo, Il2CppScheduleOne.Economy.Customer customer)
public System.Void AddContract(Il2CppScheduleOne.Quests.Contract contract)
public System.Void CustomerContractEnded(Il2CppScheduleOne.Quests.Contract contract)
public System.Void SortContracts()
public Il2CppScheduleOne.Economy.EDealWindow GetDealWindow()
public System.Int32 GetContractCountInWindow(Il2CppScheduleOne.Economy.EDealWindow window)
public System.Void CheckCurrentDealValidity()
public System.Void CheckAttendStart()
public virtual System.Void CheckNotifyPlayerOfDeal(Il2CppScheduleOne.Economy.Dealer cartelDealer, Il2CppScheduleOne.Quests.Contract contract)
public virtual System.Void CompletedDeal()                                // ServerRpc → RpcLogic___CompletedDeal_2166136261

// cash
public System.Boolean CanCollectCash(out System.String& reason)
public System.Void CollectCash()
public System.Void ChangeCash(System.Single change)
public System.Void SetCash(System.Single cash)                            // ServerRpc
public System.Void SubmitPayment(System.Single payment)                   // ServerRpc
public System.Void UpdateCollectCashChoice(System.Single oldCash, System.Single newCash, System.Boolean asServer)
public System.Void TryRobDealer()
public System.Void DealerUnconscious()

// inventory
public System.Void AddItemToInventory(Il2CppScheduleOne.ItemFramework.ItemInstance item)
public Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemSlot> GetAllSlots()
public Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemSlot> FilterAndSortSlots(Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemSlot> slots, System.String productID, Il2CppScheduleOne.ItemFramework.EQuality productQuality, Il2CppScheduleOne.Economy.Dealer+EAmountSortOrder amountSortOrder)
public Il2CppSystem.Collections.Generic.List<Il2CppSystem.Tuple<Il2CppScheduleOne.Product.ProductDefinition, Il2CppScheduleOne.ItemFramework.EQuality, System.Int32>> GetAvailableProducts()
public Il2CppSystem.Collections.Generic.List<Il2CppSystem.Tuple<Il2CppScheduleOne.Product.ProductDefinition, Il2CppScheduleOne.ItemFramework.EQuality, System.Int32>> GetOrderableProducts(Il2CppScheduleOne.ItemFramework.EQuality minQuality)
public System.Int32 GetOrderableProductQuantity(System.String productID, Il2CppScheduleOne.ItemFramework.EQuality minQuality, Il2CppScheduleOne.ItemFramework.EQuality maxQuality)
public System.Int32 GetPackagedProductAmount()
public System.Int32 GetTotalInventoryItemCount()
public Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Product.ProductItemInstance> RemoveAndReturnProductFromInventory(System.String productID, System.Int32 requiredQuantity, Il2CppScheduleOne.ItemFramework.EQuality targetQuality)
public System.Void RemoveContractItems(Il2CppScheduleOne.Quests.Contract contract, Il2CppScheduleOne.ItemFramework.EQuality targetQuality, out Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemInstance>& items)
public System.Void SplitItemSlot(Il2CppScheduleOne.ItemFramework.ItemSlot slot)
public System.Void TryMoveOverflowItems()
public System.Void TradeItems()
public System.Void TradeItemsDone()
public virtual System.Void SetItemSlotQuantity(System.Int32 itemSlotIndex, System.Int32 quantity)                 // ServerRpc
public System.Void SetItemSlotQuantity_Internal(System.Int32 itemSlotIndex, System.Int32 quantity)                 // ObserversRpc
public virtual System.Void SetSlotFilter(Il2CppFishNet.Connection.NetworkConnection conn, System.Int32 itemSlotIndex, Il2CppScheduleOne.ItemFramework.SlotFilter filter)
public System.Void SetSlotFilter_Internal(Il2CppFishNet.Connection.NetworkConnection conn, System.Int32 itemSlotIndex, Il2CppScheduleOne.ItemFramework.SlotFilter filter)
public virtual System.Void SetSlotLocked(Il2CppFishNet.Connection.NetworkConnection conn, System.Int32 itemSlotIndex, System.Boolean locked, Il2CppFishNet.Object.NetworkObject lockOwner, System.String lockReason)
public System.Void SetSlotLocked_Internal(Il2CppFishNet.Connection.NetworkConnection conn, System.Int32 itemSlotIndex, System.Boolean locked, Il2CppFishNet.Object.NetworkObject lockOwner, System.String lockReason)
public virtual System.Void SetStoredInstance(Il2CppFishNet.Connection.NetworkConnection conn, System.Int32 itemSlotIndex, Il2CppScheduleOne.ItemFramework.ItemInstance instance)
public System.Void SetStoredInstance_Internal(Il2CppFishNet.Connection.NetworkConnection conn, System.Int32 itemSlotIndex, Il2CppScheduleOne.ItemFramework.ItemInstance instance)

// lifecycle / persistence
public virtual System.Void Awake()
public virtual System.Void Start()
public virtual System.Void OnDestroy()
public virtual System.Void OnTick()
public virtual System.Void OnValidate()
public virtual System.Void OnSpawnServer(Il2CppFishNet.Connection.NetworkConnection connection)
public virtual Il2CppScheduleOne.Persistence.Datas.NPCData GetNPCData()
public virtual System.Void Load(Il2CppScheduleOne.Persistence.Datas.NPCData data, System.String containerPath)
public virtual System.Void Load(Il2CppScheduleOne.Persistence.Datas.DynamicSaveData dynamicData, Il2CppScheduleOne.Persistence.Datas.NPCData npcData)
public virtual System.Boolean ReadSyncVar___ScheduleOne_Economy_Dealer(Il2CppFishNet.Serializing.PooledReader PooledReader0, System.UInt32 UInt321, System.Boolean Boolean2)
public System.Single sync___get_value__Cash_k__BackingField()
public System.Void sync___set_value__Cash_k__BackingField(System.Single value, System.Boolean asServer)

// unstable fallback names
public virtual System.Void Method_Protected_Virtual_Void_0()   // = Awake_UserLogic_ScheduleOne.Economy.Dealer_Assembly-CSharp.dll (verified string)
public System.Void Method_Private_Void_List_1_ItemInstance_Single_0(Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemInstance> items, System.Single cash)
                                                               // = <TryRobDealer>g__SummariseLosses|98_0 (inferred)
public System.Void Method_Private_Void_List_1_ItemSlot_Boolean_Boolean_byref___c__DisplayClass109_0_0(Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemSlot> orderedSlots, System.Boolean split, System.Boolean onlyRemoveIdealQuality, ref Il2CppScheduleOne.Economy.Dealer+__c__DisplayClass109_0& A_4)
                                                               // = <RemoveAndReturnProductFromInventory>g__RemoveProduct|109_1 (verified: parameter names orderedSlots/split/onlyRemoveIdealQuality present in metadata)
public System.Void _Awake_b__63_0(Il2CppScheduleOne.NPCs.Relation.NPCRelationData+EUnlockType <p0>, System.Boolean <p1>)
```

```csharp
public enum Il2CppScheduleOne.Economy.Dealer+EAmountSortOrder { LowToHigh = 0, HighToLow = 1 }
```

### `Il2CppScheduleOne.NPCs.Framework.DealerNPCData` — the dealer config asset

```csharp
public class Il2CppScheduleOne.NPCs.Framework.DealerNPCData : Il2CppScheduleOne.NPCs.Framework.NPCData
{
    Il2CppScheduleOne.Economy.EDealerType DealerType { public get; public set; }
    System.String HomeName { public get; public set; }
    System.Single SigningFee { public get; public set; }
    System.Single SalesCutPercentage { public get; public set; }
    Il2CppScheduleOne.Dialogue.DialogueContainer RecruitDialogue { public get; public set; }
    Il2CppScheduleOne.Dialogue.DialogueContainer CollectCashDialogue { public get; public set; }
    Il2CppScheduleOne.Dialogue.DialogueContainer AssignCustomersDialogue { public get; public set; }

    public virtual Il2CppScheduleOne.NPCs.Framework.NPCData GetDeepCopy()
    public System.Void PopulateDealerData(Il2CppScheduleOne.NPCs.Framework.DealerNPCData data)
}
```

Wrapper `Il2CppScheduleOne.NPCs.Framework.DealerNPCDataObject : GenericNPCDataObject<DealerNPCData>`.
Base `NPCData` holds 12 `Il2CppScheduleOne.Core.ValueOrReference<TValue, TPreset>` sections:
`_basicInfo`, `_appearance`, `_health`, `_movement`, `_interaction`, `_relationship`, `_messaging`,
`_dialogue`, `_voice`, `_inventory`, `_behaviour`, `_weatherBehaviour`, each with a read-only
resolved accessor (`BasicInfo`, `Appearance`, …).

> **Naming trap:** `Il2CppScheduleOne.NPCs.Framework.DealerNPCData` (config) and
> `Il2CppScheduleOne.Persistence.Datas.DealerData` (save DTO) are different types. There is **no**
> type called `DealerData` in the `Economy` namespace.

### The 6 named player dealers

`Brad`, `Jane`, `Leo`, `Molly`, `Wei` add **nothing** beyond the FishNet plumbing:

```csharp
public class Il2CppScheduleOne.NPCs.CharacterClasses.Brad : Il2CppScheduleOne.Economy.Dealer
{
    System.Boolean field_Private_Boolean_0 { public get; public set; }
    System.Boolean field_Private_Boolean_1 { public get; public set; }
    public virtual System.Void Awake()
    public virtual System.Void NetworkInitializeIfDisabled()
    public virtual System.Void NetworkInitialize__Late()
    public virtual System.Void NetworkInitialize___Early()
}
// Jane, Leo, Molly, Wei: byte-for-byte the same shape.
```

`Benji` is the only one with real overrides (he is the tutorial/quest dealer):

```csharp
public class Il2CppScheduleOne.NPCs.CharacterClasses.Benji : Il2CppScheduleOne.Economy.Dealer
{
    System.String CompletedDealsVariable { public get; public set; }
    UnityEngine.Events.UnityEvent onRecruitmentRequested { public get; public set; }

    public virtual System.Void AddCustomer(Il2CppScheduleOne.Economy.Customer customer)
    public virtual System.Void Awake()
    public System.Void IncrementCompletedDeals()
    public virtual System.Void OnSpawnServer(Il2CppFishNet.Connection.NetworkConnection connection)
    public virtual System.Void OnTick()
    public virtual System.Void RecruitmentRequested()
    public virtual System.Void RemoveCustomer(Il2CppScheduleOne.Economy.Customer customer)
    public virtual System.Void UpdatePotentialDealerPoI()
    public virtual System.Void NetworkInitializeIfDisabled()
    public virtual System.Void NetworkInitialize__Late()
    public virtual System.Void NetworkInitialize___Early()
}
```

Related literals: `Benji_CustomerCount`, `"Assigned Customers ("`, `"How do I assign customers to you?"`,
`"This dealer is ready to be hired. Go to them and pay their signing free to recruit them."` (sic — "free"),
`"Unlock this dealer by reaching 'friendly' with one of their connections."`.

### `Il2CppScheduleOne.Cartel.CartelDealer`

```csharp
public class Il2CppScheduleOne.Cartel.CartelDealer : Il2CppScheduleOne.Economy.Dealer
{
    static System.Single DEALER_DEFEATED_INFLUENCE_CHANGE { public get; public set; }
    static System.Int32 PRODUCT_COUNT_MIN { public get; public set; }
    static System.Int32 PRODUCT_COUNT_MAX { public get; public set; }
    static System.Int32 PRODUCT_QUANTITY_MIN { public get; public set; }
    static System.Int32 PRODUCT_QUANTITY_MAX { public get; public set; }
    System.Boolean IsAcceptingDeals { public get; public set; }
    Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppScheduleOne.Product.ProductDefinition> RandomProducts { public get; public set; }
    Il2CppScheduleOne.ItemFramework.EQuality ProductQuality { public get; public set; }
    Il2CppScheduleOne.Product.Packaging.PackagingDefinition DefaultPackaging { public get; public set; }
    Il2CppScheduleOne.Cartel.CartelGoonAppearance appearance { public get; public set; }
    Il2CppScheduleOne.Cartel.GoonPool GoonPool { public get; }

    public virtual System.Void Awake()
    public System.Boolean CanCurrentlyAcceptDeal()
    public System.Void ConfigureGoonSettings(Il2CppFishNet.Connection.NetworkConnection conn, Il2CppScheduleOne.Cartel.CartelGoonAppearance appearance, System.Single moveSpeed)
    public System.Void DiedOrKnockedOut()
    public System.Void RandomizeAppearance()
    public System.Void RandomizeInventory()
    public System.Void SetIsAcceptingDeals(System.Boolean accepting)
    public virtual System.Void Start()
    public virtual System.Void OnSpawnServer(Il2CppFishNet.Connection.NetworkConnection connection)
    // + NetworkInitialize*/Rpc plumbing for ConfigureGoonSettings
}
```

`CartelDealer` is spawned/driven by the cartel activity system — see §10 for how that periodic
region-visit machinery works (it is the closest existing model for a "group visits town" event).

### Dealer management UI

```csharp
public class Il2CppScheduleOne.UI.Phone.Messages.DealerManagementApp
    : Il2CppScheduleOne.UI.App<Il2CppScheduleOne.UI.Phone.Messages.DealerManagementApp>
{
    Il2CppScheduleOne.Economy.Dealer SelectedDealer { public get; public set; }
    Il2CppScheduleOne.UI.Phone.CustomerSelector CustomerSelector { public get; public set; }
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Economy.Dealer> dealers { public get; public set; }
    UnityEngine.UI.Button AssignCustomerButton { public get; public set; }
    Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<UnityEngine.RectTransform> CustomerEntries { public get; public set; }
    Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<UnityEngine.RectTransform> InventoryEntries { public get; public set; }
    // + labels: NoDealersLabel, CashLabel, CutLabel, HomeLabel, CustomerTitleLabel, SelectorTitle …

    public System.Void AddCustomer(Il2CppScheduleOne.Economy.Customer customer)
    public System.Void AddDealer(Il2CppScheduleOne.Economy.Dealer dealer)
    public System.Void AssignCustomer()
    public System.Void BackPressed()
    public System.Void NextPressed()
    public System.Void OnDropdownOpen()
    public System.Void OnDropdownValueChanged(System.Int32 value)
    public System.Void Refresh()
    public System.Void RefreshDropdown()
    public System.Void RemoveCustomer(Il2CppScheduleOne.Economy.Customer customer)
    public System.Void SetDisplayedDealer(Il2CppScheduleOne.Economy.Dealer dealer)
    public virtual System.Void SetOpen(System.Boolean open)
}

public class Il2CppScheduleOne.UI.Phone.Messages.DealerManagementApp+InventoryItem : Il2CppSystem.Object
{
    System.String ID { public get; public set; }
    System.Int32 Quantity { public get; public set; }
    System.Int32 Quality { public get; public set; }        // Int32, not EQuality
    public .ctor(System.String id, System.Int32 quantity, System.Int32 quality)
}

public class Il2CppScheduleOne.UI.Phone.CustomerSelector : UnityEngine.MonoBehaviour
{
    UnityEngine.GameObject ButtonPrefab { public get; public set; }
    UnityEngine.RectTransform EntriesContainer { public get; public set; }
    Il2CppScheduleOne.UIPanel CustomersPanel { public get; public set; }
    UnityEngine.Events.UnityEvent<Il2CppScheduleOne.Economy.Customer> onCustomerSelected { public get; public set; }
    Il2CppSystem.Collections.Generic.List<UnityEngine.RectTransform> customerEntries { public get; public set; }
    Il2CppSystem.Collections.Generic.Dictionary<UnityEngine.RectTransform, Il2CppScheduleOne.Economy.Customer> entryToCustomer { public get; public set; }

    public System.Void CreateEntry(Il2CppScheduleOne.Economy.Customer customer)
    public System.Void CustomerSelected(Il2CppScheduleOne.Economy.Customer customer)
    public System.Void Open()
    public System.Void Close()
    public System.Void Exit(Il2CppScheduleOne.ExitAction action)
}
```

Literal `"Cannot set displayed dealer to null!"`. Any new customer will show up in
`CustomerSelector` automatically as long as it is in `Customer.UnlockedCustomers` (*inferred* —
`DealerManagementApp.Refresh()` / `CustomerSelector.Open()` are the only population paths).

### `Il2CppScheduleOne.Economy.Supplier` (for completeness — same family)

`Supplier : Il2CppScheduleOne.NPCs.NPC`, 4 subclasses (`Albert`, `Phil`, `Salvador`, `Shirley`).
Key surface: `ESupplierStatus { Idle = 0, PreppingDeadDrop = 1, Meeting = 2 }`, `Debt`,
`ChangeDebt(float)`, `DeaddropRequested()`, `DeaddropConfirmed(List<PhoneShopInterface+CartEntry> cart, float totalPrice)`,
`SetDeaddrop(Il2CppReferenceArray<StringIntPair> items, int minsUntilReady)`, `CompleteDeaddrop()`,
`MeetupRequested()`, `MeetAtLocation(NetworkConnection conn, int locationIndex, int expireIn)`,
`GetDeadDropLimit()`, statics `MeetupRelationshipRequirement`, `MeetupDuration`, `MeetupCooldown`,
`DeaddropWaitPerItem`, `DeaddropMaxWait`, `DeaddropItemLimit`, `MeetingEndDistance`,
`DeliveryRelationshipRequirement`.

---

## 7. `Il2CppScheduleOne.Economy.DeadDrop`

```csharp
public class Il2CppScheduleOne.Economy.DeadDrop : UnityEngine.MonoBehaviour
{
    static Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Economy.DeadDrop> DeadDrops { public get; public set; }
    System.String DeadDropName { public get; public set; }
    System.String DeadDropDescription { public get; public set; }
    Il2CppScheduleOne.Map.EMapRegion Region { public get; public set; }
    Il2CppScheduleOne.Storage.WorldStorageEntity Storage { public get; public set; }
    Il2CppScheduleOne.Map.POI PoI { public get; public set; }
    Il2CppScheduleOne.DevUtilities.OptimizedLight Light { public get; public set; }
    System.String ItemCountVariable { public get; public set; }
    System.String BakedGUID { public get; public set; }
    Il2CppSystem.Guid GUID { public get; public set; }

    public virtual System.Void Awake()
    public static Il2CppScheduleOne.Economy.DeadDrop GetRandomEmptyDrop(UnityEngine.Vector3 origin)
    public System.Void OnDestroy()
    public System.Void OnValidate()
    public System.Void RegenerateGUID()
    public virtual System.Void SetGUID(Il2CppSystem.Guid guid)
    public virtual System.Void Start()
    public System.Void UpdateDeadDrop()
}
```

Note the property is **`DeadDropDescription`**, not `Description`. `GetRandomEmptyDrop(Vector3 origin)`
filters on emptiness (`DeadDrop+__c._GetRandomEmptyDrop_b__19_0(DeadDrop drop)`) then orders by
distance from `origin` (`DeadDrop+__c__DisplayClass19_0._GetRandomEmptyDrop_b__1(DeadDrop drop)`) —
i.e. **nearest empty drop**, not uniformly random (*inferred from the two closures: a `Func<…,bool>`
predicate plus a `Func<…,float>` key*).

`ItemCountVariable` is a `Il2CppScheduleOne.Variables` variable name written whenever the drop's
contents change (*inferred from `UpdateDeadDrop()` + the `Variables` namespace*).

Storage is a `Il2CppScheduleOne.Storage.WorldStorageEntity`. Related quest type:

```csharp
public class Il2CppScheduleOne.Quests.DeaddropQuest : Il2CppScheduleOne.Quests.Quest
{
    static Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Quests.DeaddropQuest> DeaddropQuests { public get; public set; }
    Il2CppScheduleOne.Economy.DeadDrop Drop { public get; public set; }
    public System.Void SetDrop(Il2CppScheduleOne.Economy.DeadDrop drop)
}
// created via QuestManager.CreateDeaddropCollectionQuest(string dropGUID, string guidString = "")
//                     / CreateDeaddropCollectionQuest(NetworkConnection conn, string dropGUID, string guidString = "")
```

Cartel counterpart: `Il2CppScheduleOne.Cartel.StealDeadDrop : CartelActivity`.

---

## 8. `Product` / `ProductManager`

### `Il2CppScheduleOne.Product.ProductDefinition`

```csharp
public class Il2CppScheduleOne.Product.ProductDefinition
    : Il2CppScheduleOne.Product.PropertyItemDefinition   // → StorableItemDefinition → ItemDefinition
{
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Product.DrugTypeContainer> DrugTypes { public get; public set; }
    System.Single LawIntensityChange { public get; public set; }
    System.Single BasePrice { public get; public set; }
    System.Single MarketValue { public get; public set; }
    Il2CppScheduleOne.Product.FunctionalProduct FunctionalProduct { public get; public set; }
    System.Int32 NPCEffectDuration { public get; public set; }
    System.Int32 PlayerEffectDuration { public get; public set; }
    System.Single BaseAddictiveness { public get; public set; }
    Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppScheduleOne.Product.Packaging.PackagingDefinition> ValidPackaging { public get; public set; }
    Il2CppScheduleOne.Product.ProductConsumeAnimation ConsumeAnimation { public get; public set; }
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.StationFramework.StationRecipe> Recipes { public get; public set; }
    Il2CppScheduleOne.Product.EDrugType DrugType { public get; }     // read-only: first entry of DrugTypes (inferred)
    System.Single Price { public get; }                              // read-only

    public System.Void AddRecipe(Il2CppScheduleOne.StationFramework.StationRecipe recipe)
    public System.Void CleanRecipes()
    public virtual System.Void GenerateAppearanceSettings()
    public System.Single GetAddictiveness()
    public virtual Il2CppScheduleOne.ItemFramework.ItemInstance GetDefaultInstance(System.Int32 quantity = 1)
    public virtual Il2CppScheduleOne.Persistence.Datas.ProductData GetSaveData()
    public virtual System.String GetSaveString()
    public System.Void Initialize(Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Effects.Effect> properties, Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Product.EDrugType> drugTypes)
    public virtual System.Void InitializeSaveable()
    public System.Void OnValidate()
}
```

Concrete subclasses: `WeedDefinition`, `MethDefinition`, `CocaineDefinition`, `ShroomDefinition`
(each adds appearance settings + `GetAppearanceSettings(List<Effect> properties)` +
`Initialize(List<Effect>, List<EDrugType>, <X>AppearanceSettings)`).

### `Il2CppScheduleOne.Product.ProductItemInstance`

```csharp
public class Il2CppScheduleOne.Product.ProductItemInstance
    : Il2CppScheduleOne.ItemFramework.QualityItemInstance   // → StorableItemInstance → ItemInstance
{
    System.String PackagingID { public get; public set; }
    Il2CppScheduleOne.Product.Packaging.PackagingDefinition packaging { public get; public set; }
    Il2CppScheduleOne.Product.Packaging.PackagingDefinition AppliedPackaging { public get; }
    System.Int32 Amount { public get; }
    System.String Name { public get; }
    Il2CppScheduleOne.Equipping.Equippable Equippable { public get; }
    Il2CppScheduleOne.Storage.StoredItem StoredItem { public get; }
    UnityEngine.Sprite Icon { public get; }
    // inherited: Il2CppScheduleOne.ItemFramework.EQuality Quality { public get; public set; }

    public .ctor(Il2CppScheduleOne.ItemFramework.ItemDefinition definition, System.Int32 quantity,
                 Il2CppScheduleOne.ItemFramework.EQuality quality,
                 Il2CppScheduleOne.Product.Packaging.PackagingDefinition _packaging = null)

    public virtual System.Void ApplyEffectsToNPC(Il2CppScheduleOne.NPCs.NPC npc)
    public virtual System.Void ApplyEffectsToPlayer(Il2CppScheduleOne.PlayerScripts.Player player)
    public virtual System.Void ClearEffectsFromNPC(Il2CppScheduleOne.NPCs.NPC npc)
    public virtual System.Void ClearEffectsFromPlayer(Il2CppScheduleOne.PlayerScripts.Player Player)
    public virtual System.Boolean CanStackWith(Il2CppScheduleOne.ItemFramework.ItemInstance other, System.Boolean checkQuantities = True)
    public virtual System.Single GetAddictiveness()
    public virtual Il2CppScheduleOne.ItemFramework.ItemInstance GetCopy(System.Int32 overrideQuantity = -1)
    public virtual System.Single GetMonetaryValue()
    public System.Single GetSimilarity(Il2CppScheduleOne.Product.ProductDefinition other, Il2CppScheduleOne.ItemFramework.EQuality otherQuality)
    public virtual System.Int32 GetTotalAmount()
    public virtual Il2CppScheduleOne.Persistence.Datas.ItemData GetItemData()
    public virtual System.Void SetPackaging(Il2CppScheduleOne.Product.Packaging.PackagingDefinition def)
    public virtual System.Void Read(Il2CppFishNet.Serializing.Reader reader)
    public virtual System.Void Write(Il2CppFishNet.Serializing.Writer writer)
    public Il2CppScheduleOne.Equipping.Equippable GetEquippable()
    public UnityEngine.Sprite GetIcon()
    public Il2CppScheduleOne.Storage.StoredItem GetStoredItem()
}
```

### Packaging

```csharp
public class Il2CppScheduleOne.Product.Packaging.PackagingDefinition
    : Il2CppScheduleOne.ItemFramework.StorableItemDefinition
{
    System.Int32 Quantity { public get; public set; }
    Il2CppScheduleOne.Product.Packaging.EStealthLevel StealthLevel { public get; public set; }
    Il2CppScheduleOne.Packaging.FunctionalPackaging FunctionalPackaging { public get; public set; }
    Il2CppScheduleOne.Equipping.Equippable Equippable_Filled { public get; public set; }
    Il2CppScheduleOne.Storage.StoredItem StoredItem_Filled { public get; public set; }
}

public enum Il2CppScheduleOne.Product.Packaging.EStealthLevel { None = 0, Basic = 1, Advanced = 2 }

public static class Il2CppScheduleOne.Product.ProductQuantities
{
    static System.Int32 BagQuantity { public get; public set; }
    static System.Int32 JarQuantity { public get; public set; }
    static System.Int32 BrickQuantity { public get; public set; }
}
```

`Il2CppScheduleOne.Packaging.*` (7 types: `FunctionalPackaging`, `FunctionalBaggie`, `FunctionalJar`,
`FilledPackaging_Equippable`, `FilledPackaging_StoredItem`, `PackagingStationMk2`, `PackagingTool`)
is the **packing minigame**, not economy. `FunctionalPackaging.Definition` links back to the
`PackagingDefinition`; `PackagingDefinition.Quantity` is the units-per-package number the economy
actually cares about.

### `Il2CppScheduleOne.Product.ProductManager` — price authority

```csharp
public class Il2CppScheduleOne.Product.ProductManager
    : Il2CppScheduleOne.DevUtilities.NetworkSingleton<Il2CppScheduleOne.Product.ProductManager>
{
    static System.Int32 MIN_PRICE { public get; public set; }
    static System.Int32 MAX_PRICE { public get; public set; }
    static System.Int32 CONTRACT_RECEIPT_MAX_COUNT { public get; public set; }
    static System.Int32 STAGGERED_REPLICATIONS_PER_SECOND { public get; public set; }

    static Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Product.ProductDefinition> DiscoveredProducts { public get; public set; }
    static Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Product.ProductDefinition> ListedProducts { public get; public set; }
    static Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Product.ProductDefinition> FavouritedProducts { public get; public set; }
    static System.Boolean IsAcceptingOrders { public get; public set; }
    static System.Boolean MethDiscovered { public get; }
    static System.Boolean CocaineDiscovered { public get; }
    static System.Boolean ShroomsDiscovered { public get; }

    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Product.ProductDefinition> AllProducts { public get; public set; }
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Product.ProductDefinition> DefaultKnownProducts { public get; public set; }
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Product.PropertyItemDefinition> ValidMixIngredients { public get; public set; }
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Economy.ContractReceipt> ContractReceipts { public get; public set; }
    UnityEngine.AnimationCurve SampleSuccessCurve { public get; public set; }
    Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppScheduleOne.Product.ProductDefinition> ListForSaleOnStart { public get; public set; }
    Il2CppScheduleOne.Product.WeedDefinition DefaultWeed { public get; public set; }
    Il2CppScheduleOne.Product.CocaineDefinition DefaultCocaine { public get; public set; }
    Il2CppScheduleOne.Product.MethDefinition DefaultMeth { public get; public set; }
    Il2CppScheduleOne.Product.ShroomDefinition DefaultShroom { public get; public set; }
    Il2CppSystem.Collections.Generic.Dictionary<Il2CppScheduleOne.Product.ProductDefinition, System.Single> ProductPrices { public get; public set; }
    Il2CppScheduleOne.Product.ProductDefinition highestValueProduct { public get; public set; }
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Product.ProductDefinition> createdProducts { public get; public set; }
    Il2CppSystem.Collections.Generic.List<System.String> ProductNames { public get; public set; }
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.StationFramework.StationRecipe> mixRecipes { public get; public set; }
    Il2CppScheduleOne.Effects.MixMaps.MixerMap WeedMixMap / MethMixMap / CokeMixMap / ShroomMixMap { public get; public set; }
    Il2CppScheduleOne.Product.NewMixOperation CurrentMixOperation { public get; public set; }
    System.Boolean IsMixingInProgress { public get; }
    System.Boolean IsMixComplete { public get; public set; }
    System.Single TimeSinceProductListingChanged { public get; public set; }
    Il2CppScheduleOne.Persistence.Loaders.ProductManagerLoader loader { public get; public set; }
    System.String SaveFolderName { public get; }
    System.String SaveFileName { public get; }
    System.Int32 LoadOrder { public get; }

    // events
    Il2CppSystem.Action<Il2CppScheduleOne.Product.ProductDefinition> onProductDiscovered { public get; public set; }
    Il2CppSystem.Action<Il2CppScheduleOne.Product.ProductDefinition> onNewProductCreated { public get; public set; }
    Il2CppSystem.Action<Il2CppScheduleOne.Product.ProductDefinition> onProductListed { public get; public set; }
    Il2CppSystem.Action<Il2CppScheduleOne.Product.ProductDefinition> onProductDelisted { public get; public set; }
    Il2CppSystem.Action<Il2CppScheduleOne.Product.ProductDefinition> onProductFavourited { public get; public set; }
    Il2CppSystem.Action<Il2CppScheduleOne.Product.ProductDefinition> onProductUnfavourited { public get; public set; }
    Il2CppSystem.Action<Il2CppScheduleOne.Product.NewMixOperation> onMixCompleted { public get; public set; }
    Il2CppSystem.Action<Il2CppScheduleOne.StationFramework.StationRecipe> onMixRecipeAdded { public get; public set; }
    Il2CppSystem.Action<Il2CppScheduleOne.Economy.ContractReceipt> onContractReceiptRecorded { public get; public set; }
    Il2CppSystem.Action<Il2CppFishNet.Connection.NetworkConnection> onProductDataSentToConnection { public get; public set; }
    UnityEngine.Events.UnityEvent onFirstSampleRejection { public get; public set; }
    UnityEngine.Events.UnityEvent onSecondUniqueProductCreated { public get; public set; }

    // === price maths — these are the exact price methods ===
    public static System.Single CalculateProductValue(Il2CppScheduleOne.Product.ProductDefinition product, System.Single baseValue)
    public static System.Single CalculateProductValue(System.Single baseValue, Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Effects.Effect> properties)
    public System.Single GetPrice(Il2CppScheduleOne.Product.ProductDefinition product)
    public System.Void SetPrice(Il2CppFishNet.Connection.NetworkConnection conn, System.String productID, System.Single value)   // Observers+Target
    public System.Void SendPrice(System.String productID, System.Single value)                                                  // ServerRpc
    public System.Void RefreshHighestValueProduct()

    // discovery / listing / favouriting
    public static System.Void CheckDiscovery(Il2CppScheduleOne.ItemFramework.ItemInstance item)
    public System.Void DiscoverProduct(System.String productID)                                              // ServerRpc
    public System.Void SetProductDiscovered(Il2CppFishNet.Connection.NetworkConnection conn, System.String productID, System.Boolean autoList)
    public System.Void SetProductListed(System.String productID, System.Boolean listed)                       // ServerRpc
    public System.Void SetProductListed(Il2CppFishNet.Connection.NetworkConnection conn, System.String productID, System.Boolean listed)
    public System.Void SetProductFavourited(System.String productID, System.Boolean listed)                   // ServerRpc  (param misnamed "listed")
    public System.Void SetProductFavourited(Il2CppFishNet.Connection.NetworkConnection conn, System.String productID, System.Boolean fav)
    public System.Void SetIsAcceptingOrder(System.Boolean accepting)                                          // sic — singular "Order"
    public System.Void SetMethDiscovered()    // ServerRpc
    public System.Void SetCocaineDiscovered() // ServerRpc
    public System.Void SetShroomsDiscovered() // ServerRpc

    // lookup
    public Il2CppScheduleOne.Product.ProductDefinition GetKnownProduct(Il2CppScheduleOne.Product.EDrugType type, Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Effects.Effect> properties)
    public Il2CppScheduleOne.Effects.MixMaps.MixerMap GetMixerMap(Il2CppScheduleOne.Product.EDrugType type)
    public Il2CppScheduleOne.StationFramework.StationRecipe GetRecipe(System.String product, System.String mixer)
    public Il2CppScheduleOne.StationFramework.StationRecipe GetRecipe(Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Effects.Effect> productProperties, Il2CppScheduleOne.Effects.Effect mixerProperty)
    public static System.Boolean IsMixNameValid(System.String mixName)
    public static System.String MakeIDFileSafe(System.String id)

    // receipts (per-region, per-party analytics — see §10)
    public Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Economy.ContractReceipt> GetContractReceipts(Il2CppScheduleOne.Map.EMapRegion region, Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Economy.EContractParty> dealCompleterTypes, System.Int32 maxMinsAgo)
    public System.Void RecordContractReceipt(Il2CppFishNet.Connection.NetworkConnection conn, Il2CppScheduleOne.Economy.ContractReceipt receipt)  // Observers+Target

    // runtime product creation (per drug type)
    public System.Void CreateWeed_Server(System.String name, System.String id, Il2CppScheduleOne.Product.EDrugType type, Il2CppSystem.Collections.Generic.List<System.String> properties, Il2CppScheduleOne.Product.WeedAppearanceSettings appearance)
    public System.Void CreateWeed(Il2CppFishNet.Connection.NetworkConnection conn, System.String name, System.String id, Il2CppScheduleOne.Product.EDrugType type, Il2CppSystem.Collections.Generic.List<System.String> properties, Il2CppScheduleOne.Product.WeedAppearanceSettings appearance)
    public System.Void CreateMeth_Server(…MethAppearanceSettings…)      / CreateMeth(…)
    public System.Void CreateCocaine_Server(…CocaineAppearanceSettings…) / CreateCocaine(…)
    public System.Void CreateShroom_Server(…ShroomAppearanceSettings…)  / CreateShroom_Client(…)
    public System.String FinishAndNameMix(System.String productID, System.String ingredientID, System.String mixName)
    public System.Void FinishAndNameMix(System.String productID, System.String ingredientID, System.String mixName, System.String mixID)
    public System.Void CreateMixRecipe(Il2CppFishNet.Connection.NetworkConnection conn, System.String product, System.String mixer, System.String output)
    public System.Void SendMixRecipe(System.String product, System.String mixer, System.String output)
    public System.Void SetMixOperation(Il2CppScheduleOne.Product.NewMixOperation operation, System.Boolean complete)
    public System.Void SendMixOperation(Il2CppScheduleOne.Product.NewMixOperation operation, System.Boolean complete)

    public System.Void Clean()
    public System.Boolean HasSentProductDataToConnection(Il2CppFishNet.Connection.NetworkConnection conn)
    public System.Void OnMinPass()
    public System.Void OnNewDay()
    public virtual System.Void Method_Protected_Virtual_Void_0()   // = Awake_UserLogic_ScheduleOne.Product.ProductManager_Assembly-CSharp.dll
}
```

### How market price and quality are computed

* Static base: `ProductDefinition.BasePrice`, `ProductDefinition.MarketValue`,
  read-only `ProductDefinition.Price`.
* Effect-driven value: `ProductManager.CalculateProductValue(ProductDefinition product, float baseValue)`
  and the property-list overload `CalculateProductValue(float baseValue, List<Effect> properties)`.
  Clamped by `ProductManager.MIN_PRICE` / `MAX_PRICE`.
* Player-set listing price: `ProductManager.ProductPrices` dictionary, written by
  `SetPrice` / `SendPrice`, read by `GetPrice(ProductDefinition)`.
* Per-instance monetary value: `ProductItemInstance.GetMonetaryValue()` (override of
  `ItemInstance.GetMonetaryValue()`); packaging multiplies via `PackagingDefinition.Quantity` and
  `ProductItemInstance.GetTotalAmount()`.
* Quality scaling: `CustomerData.GetQualityScalar(EQuality quality)` on the demand side;
  `Il2CppScheduleOne.Product.ProductItemInstance.GetSimilarity(ProductDefinition other, EQuality otherQuality)`
  for match scoring.
* Customer-facing "is this a fair price": `Customer.GetValueProposition(ProductDefinition product, float price)`
  (static) + `HandoverScreen.FairPriceLabel` / `GetCustomerItemsValue()`.
* **All exact formulas and coefficients are UNVERIFIED** — they exist only in IL, not in the metadata
  string/type dumps.

`ProductManager.FavouritedProducts` + `onProductFavourited`/`onProductUnfavourited` +
`SetProductFavourited(...)` is the "favourited" mechanism the brief asks about; UI side is
`Il2CppScheduleOne.Product.ProductEntry.FavouriteClicked()` / `UpdateFavourited()` /
`ProductFavouritedOrUnFavourited(ProductDefinition def)` and `FavouritedColor`/`UnfavouritedColor`.

### `Il2CppScheduleOne.Economy.ContractReceipt`

```csharp
public class Il2CppScheduleOne.Economy.ContractReceipt : Il2CppSystem.Object
{
    System.Int32 ReceiptId { public get; public set; }
    Il2CppScheduleOne.Economy.EContractParty CompletedBy { public get; public set; }
    System.String CustomerId { public get; public set; }
    Il2CppScheduleOne.GameTime.GameDateTime CompletionTime { public get; public set; }
    Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppScheduleOne.DevUtilities.StringIntPair> Items { public get; public set; }
    System.Single AmountPaid { public get; public set; }

    public .ctor(System.Int32 receiptId, Il2CppScheduleOne.Economy.EContractParty completedBy, System.String customerID,
                 Il2CppScheduleOne.GameTime.GameDateTime completionTime,
                 Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppScheduleOne.DevUtilities.StringIntPair> items,
                 System.Single amountPaid)
}
```

Note `CustomerId` (lowercase `d`) on the property but `customerID` on the ctor parameter.
Supporting types: `Il2CppScheduleOne.DevUtilities.StringIntPair { System.String String; System.Int32 Int; }`,
`Il2CppScheduleOne.GameTime.GameDateTime { public System.Int32 elapsedDays; public System.Int32 time; }`
with `AddMins(int)`, `GetMinSum()`, `GetCopy()` and full comparison operators.

---

## 9. `Il2CppScheduleOne.Money.MoneyManager`

```csharp
public class Il2CppScheduleOne.Money.MoneyManager
    : Il2CppScheduleOne.DevUtilities.NetworkSingleton<Il2CppScheduleOne.Money.MoneyManager>
{
    static System.String MONEY_TEXT_COLOR { public get; public set; }
    static System.String MONEY_TEXT_COLOR_DARKER { public get; public set; }
    static System.String ONLINE_BALANCE_COLOR { public get; public set; }
    static Il2CppSystem.Globalization.CultureInfo cultureInfo { public get; public set; }

    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Money.Transaction> ledger { public get; public set; }
    System.Single onlineBalance { public get; public set; }
    System.Single lifetimeEarnings { public get; public set; }
    System.Single LifetimeEarnings { public get; }
    System.Single LastCalculatedNetworth { public get; public set; }
    System.Single cashBalance { public get; }                                          // read-only
    Il2CppScheduleOne.ItemFramework.CashInstance cashInstance { public get; }
    Il2CppFishNet.Object.Synchronizing.SyncVar<System.Single> syncVar___onlineBalance { public get; public set; }
    Il2CppFishNet.Object.Synchronizing.SyncVar<System.Single> syncVar___lifetimeEarnings { public get; public set; }
    System.Single SyncAccessor_onlineBalance { public get; public set; }
    System.Single SyncAccessor_lifetimeEarnings { public get; public set; }
    Il2CppSystem.Action<Il2CppScheduleOne.Money.MoneyManager+FloatContainer> onNetworthCalculation { public get; public set; }
    Il2CppScheduleOne.Audio.AudioSourceController CashSound { public get; public set; }
    UnityEngine.GameObject moneyChangePrefab { public get; public set; }
    UnityEngine.GameObject cashChangePrefab { public get; public set; }
    UnityEngine.Sprite LaunderingNotificationIcon { public get; public set; }
    Il2CppScheduleOne.Persistence.Loaders.MoneyLoader loader { public get; public set; }
    System.String SaveFolderName { public get; }
    System.String SaveFileName { public get; }
    System.Int32 LoadOrder { public get; }

    // === the money API ===
    public System.Void ChangeCashBalance(System.Single change, System.Boolean visualizeChange = True, System.Boolean playCashSound = False)
    public System.Void CreateOnlineTransaction(System.String _transaction_Name, System.Single _unit_Amount, System.Single _quantity, System.String _transaction_Note)   // ServerRpc → RpcLogic___CreateOnlineTransaction_1419830531
    public System.Void ReceiveOnlineTransaction(System.String _transaction_Name, System.Single _unit_Amount, System.Single _quantity, System.String _transaction_Note) // ObserversRpc → RpcLogic___ReceiveOnlineTransaction_1419830531
    public System.Void ChangeLifetimeEarnings(System.Single change)                     // ServerRpc → RpcLogic___ChangeLifetimeEarnings_431000436
    public System.Single GetNetWorth()
    public System.Void CheckNetworthAchievements()
    public Il2CppScheduleOne.ItemFramework.CashInstance GetCashInstance(System.Single amount)
    public System.Void PlayCashSound()
    public System.Void MinPass()
    public System.Void Load(Il2CppScheduleOne.Persistence.Datas.MoneyData data)
    public System.Void Loaded()

    // formatting helpers
    public static System.String FormatAmount(System.Single amount, System.Boolean showDecimals = False, System.Boolean includeColor = False)
    public static System.String ApplyMoneyTextColor(System.String text)
    public static System.String ApplyMoneyTextColorDarker(System.String text)
    public static System.String ApplyOnlineBalanceColor(System.String text)

    // UI coroutines
    public Il2CppSystem.Collections.IEnumerator ShowCashChange(UnityEngine.RectTransform changeDisplay)
    public Il2CppSystem.Collections.IEnumerator ShowOnlineBalanceChange(UnityEngine.RectTransform changeDisplay)

    public virtual System.Boolean ReadSyncVar___ScheduleOne_Money_MoneyManager(Il2CppFishNet.Serializing.PooledReader PooledReader0, System.UInt32 UInt321, System.Boolean Boolean2)
    public System.Single sync___get_value_lifetimeEarnings()
    public System.Single sync___get_value_onlineBalance()
    public System.Void sync___set_value_lifetimeEarnings(System.Single value, System.Boolean asServer)
    public System.Void sync___set_value_onlineBalance(System.Single value, System.Boolean asServer)
    public virtual System.Void Method_Protected_Virtual_Void_0()   // = Awake_UserLogic_ScheduleOne.Money.MoneyManager_Assembly-CSharp.dll
}

public class Il2CppScheduleOne.Money.MoneyManager+FloatContainer : Il2CppSystem.Object
{
    System.Single value { public get; public set; }
    public System.Void ChangeValue(System.Single value)
}

public class Il2CppScheduleOne.Money.Transaction : Il2CppSystem.Object
{
    System.String transaction_Name { public get; public set; }
    System.Single unit_Amount { public get; public set; }
    System.Single quantity { public get; public set; }
    System.String transaction_Note { public get; public set; }
    System.Single total_Amount { public get; }
    public .ctor(System.String _transaction_Name, System.Single _unit_Amount, System.Single _quantity, System.String _transaction_Note)
}
```

**Cash vs online**: cash is *physical inventory* — `cashBalance` is read-only and computed from
`CashInstance` items in the player's hotbar (`Il2CppScheduleOne.Money.CashSlot`, static
`MAX_CASH_PER_SLOT`); `ChangeCashBalance` mutates those items. Online balance is a `SyncVar<float>`
mutated only through the transaction ledger (`CreateOnlineTransaction` → `ReceiveOnlineTransaction`).

**Laundering is not on `MoneyManager`.** It lives in:
* `Il2CppScheduleOne.ObjectScripts.LaunderingStation : Il2CppScheduleOne.EntityFramework.GridItem`
  with `Il2CppScheduleOne.UI.LaunderingInterface Interface` and
  `Il2CppScheduleOne.ObjectScripts.CashCounter CashCounter`.
* `Il2CppScheduleOne.Persistence.Datas.LaunderOperationData` (save DTO).
* `Il2CppScheduleOne.Quests.Quest_CleanCash`.
* `MoneyManager.LaunderingNotificationIcon` is the only `MoneyManager` touch-point.

**ATM deposit cap** is on `Il2CppScheduleOne.Money.ATM`: statics `DepositLimitEnabled`,
`WeeklyDepositLimit`, `WeeklyDepositSum`, plus `BreakImpactThreshold`, `RepairTimeDays`,
`MinCashDrop`, `MaxCashDrop`, and `WeekPass()` / `DayPass()`.

**Events**: the only `MoneyManager` event is `onNetworthCalculation`
(`Action<MoneyManager+FloatContainer>`) — a *contributor* hook: subscribers add their own value to the
container during `GetNetWorth()`. There is **no** `onCashChanged` / `onBalanceChanged` event; to react
to money changes you must patch `ChangeCashBalance` / `ReceiveOnlineTransaction` or poll
`cashBalance` / `onlineBalance`.

---

## 10. Customer groups and regions — what exists

### Region concept: yes

```csharp
public enum Il2CppScheduleOne.Map.EMapRegion   // OriginalName(… "ScheduleOne.Map", "EMapRegion")
{
    Northtown = 0, Westville = 1, Downtown = 2, Docks = 3, Suburbia = 4, Uptown = 5,
}
```

```csharp
public class Il2CppScheduleOne.Map.Map : Il2CppScheduleOne.DevUtilities.Singleton<Il2CppScheduleOne.Map.Map>
{
    static Il2CppScheduleOne.Map.EMapRegion FINAL_REGION { public get; public set; }
    System.Boolean UNLOCK_ALL_REGIONS { public get; public set; }
    Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppScheduleOne.Map.MapRegionData> Regions { public get; public set; }
    Il2CppScheduleOne.Map.PoliceStation PoliceStation { public get; public set; }
    Il2CppScheduleOne.Map.MedicalCentre MedicalCentre { public get; public set; }
    UnityEngine.Transform TreeBounds { public get; public set; }

    public Il2CppScheduleOne.Map.MapRegionData GetRegionData(Il2CppScheduleOne.Map.EMapRegion region)
    public Il2CppScheduleOne.Map.EMapRegion GetRegionFromPosition(UnityEngine.Vector3 position)
    public Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Map.EMapRegion> GetUnlockedRegions()
    public System.Void OnRankUp(Il2CppScheduleOne.Levelling.FullRank old, Il2CppScheduleOne.Levelling.FullRank newRank)
}

public class Il2CppScheduleOne.Map.MapRegionData : Il2CppSystem.Object
{
    Il2CppScheduleOne.Map.EMapRegion Region { public get; public set; }
    System.String Name { public get; public set; }
    System.Boolean UnlockedByDefault { public get; public set; }
    Il2CppScheduleOne.Levelling.FullRank RankRequirement { public get; public set; }
    Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppScheduleOne.NPCs.NPC> StartingNPCs { public get; public set; }
    UnityEngine.Sprite RegionSprite { public get; public set; }
    Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppScheduleOne.Economy.DeliveryLocation> RegionDeliveryLocations { public get; public set; }
    Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppScheduleOne.Map.MapRegionData+RegionContainer> AdjacentRegions { public get; public set; }
    Il2CppScheduleOne.Audio.PolygonalZone RegionBounds { public get; public set; }
    System.Boolean IsUnlocked { public get; public set; }

    public Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Map.EMapRegion> GetAdjacentRegions()
    public Il2CppScheduleOne.Economy.DeliveryLocation GetRandomUnscheduledDeliveryLocation()
    public System.Void SetUnlocked()
}

public class Il2CppScheduleOne.Map.MapRegionData+RegionContainer : Il2CppSystem.Object
{
    Il2CppScheduleOne.Map.EMapRegion Region { public get; public set; }
}
```

Region unlocking: `MapRegionData.UnlockedByDefault` / `RankRequirement` (`Il2CppScheduleOne.Levelling.FullRank`,
a struct `{ ERank Rank; int Tier; }` with `ERank { Street_Rat=0 … Kingpin=10 }`), driven by
`Map.OnRankUp(FullRank old, FullRank newRank)` ← `LevelManager.onRankUp`, and replicated via
`LevelManager.SetUnlockedRegions(NetworkConnection conn, List<EMapRegion> unlockedRegions)`.
`Il2CppScheduleOne.Cartel.CartelInfluence.INFLUENCE_TO_UNLOCK_NEXT_REGION` gates it too.

Per-NPC region: `Il2CppScheduleOne.NPCs.NPC.Region { get; set; }` (type `EMapRegion`), plus
`NPCManager.GetNPCsInRegion(EMapRegion region)`.

Region-scoped analytics: `ProductManager.GetContractReceipts(EMapRegion region, List<EContractParty> dealCompleterTypes, int maxMinsAgo)`.

### `NPCRegion` type: **does not exist**

Verified — `Grep` for `NPCRegion` across all of `research/raw` returns zero matches. The closest
things are:
* `Il2CppScheduleOne.NPCs.NPC.Region` — a plain `EMapRegion` field on the NPC.
* `Il2CppScheduleOne.Map.NPCPresenceAccessZone : Il2CppScheduleOne.Map.AccessZone` (statics
  `CooldownTime`; fields `UnityEngine.Collider DetectionZone`, `Il2CppScheduleOne.NPCs.NPC TargetNPC`).
* `Il2CppScheduleOne.Map.NPCEnterableBuilding` (buildings, with `Occupants`, `GetSummonableNPCs()`).

### Customer group concept: **DOES NOT EXIST**

There is **no** `CustomerGroup`, no group id/tag/faction on `Customer`, and no group field on
`CustomerData`. Verified: `Grep` for `CustomerGroup` across all of `research/raw` returns zero
matches, and `CustomerData`'s complete 16-field list (§2) contains nothing group-like.
`Customer+ScheduleGroupPair` is about **schedule GameObjects** (`NormalScheduleGroup` /
`CurfewScheduleGroup`), not customer cohorts.

**The "Special Customers" mod must invent the group concept itself.** The three affordances the game
already gives you to build on:

1. **`EMapRegion` + `NPC.Region`** — the natural spatial cohort key. Free grouping and free
   "arrives in Westville tonight" semantics.
2. **`Il2CppScheduleOne.Cartel.CartelActivity` / `CartelRegionActivities` / `CartelActivities`** — an
   existing, working, save-persisted "periodic timed event visits a region" system. This is the
   *only* precedent in the codebase for what the mod needs and it is worth copying structurally:

```csharp
public class Il2CppScheduleOne.Cartel.CartelActivity : UnityEngine.MonoBehaviour
{
    System.Boolean IsActive { public get; public set; }
    System.Int32 MinsSinceActivation { public get; public set; }
    Il2CppScheduleOne.Map.EMapRegion Region { public get; public set; }
    System.Single InfluenceRequirement { public get; public set; }
    Il2CppSystem.Action onActivated { public get; public set; }
    Il2CppSystem.Action onDeactivated { public get; public set; }

    public virtual System.Void Activate(Il2CppScheduleOne.Map.EMapRegion region)
    public virtual System.Void Deactivate()
    public virtual System.Void HourPassed()
    public virtual System.Boolean IsRegionValidForActivity(Il2CppScheduleOne.Map.EMapRegion region)
    public virtual System.Void MinPassed()
}

public class Il2CppScheduleOne.Cartel.CartelRegionActivities : Il2CppFishNet.Object.NetworkBehaviour
{
    static System.Int32 MIN_COOLDOWN { public get; public set; }
    static System.Int32 MAX_COOLDOWN { public get; public set; }
    System.Boolean TEST_MODE { public get; public set; }
    Il2CppScheduleOne.Cartel.CartelActivity CurrentActivity { public get; public set; }
    System.Int32 HoursUntilNextActivity { public get; public set; }
    System.Boolean Active { public get; public set; }
    Il2CppScheduleOne.Map.EMapRegion Region { public get; public set; }
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Cartel.CartelActivity> Activities { public get; public set; }
    Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppScheduleOne.Cartel.CartelAmbushLocation> AmbushLocations { public get; public set; }
    Il2CppScheduleOne.Cartel.CartelDealer CartelDealer { public get; public set; }

    public System.Void ActivateDeal()
    public System.Void ActivityEnded()
    public System.Void HourPass()
    public static System.Int32 GetNewCooldown(Il2CppScheduleOne.Map.EMapRegion region)
    public System.Void StartActivity()
    public System.Void StartAcivity(System.Int32 activityIndex)                                        // sic — typo in the game
    public System.Void StartActivity(Il2CppFishNet.Connection.NetworkConnection conn, System.Int32 activityIndex)   // Observers+Target
    public System.Void TryStartActivity()
    public Il2CppScheduleOne.Persistence.CartelRegionalActivityData GetData()
    public System.Void Load(Il2CppScheduleOne.Persistence.CartelRegionalActivityData data)
}

public class Il2CppScheduleOne.Cartel.CartelActivities : Il2CppFishNet.Object.NetworkBehaviour
{
    static System.Int32 MAX_COOLDOWN_HOURS { public get; public set; }
    static System.Int32 MIN_COOLDOWN_HOURS { public get; public set; }
    Il2CppScheduleOne.Cartel.CartelActivity CurrentGlobalActivity { public get; public set; }
    System.Int32 HoursUntilNextGlobalActivity { public get; public set; }
    Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Cartel.CartelActivity> GlobalActivities { public get; public set; }
    Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppScheduleOne.Cartel.CartelRegionActivities> RegionalActivities { public get; public set; }

    public System.Void ActivityEnded()
    public System.Boolean CanNewActivityBegin()
    public Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Cartel.CartelActivity> GetActivitiesReadyToStart()
    public static System.Single GetInfluenceFraction()
    public static System.Int32 GetNewCooldown()
    public Il2CppScheduleOne.Cartel.CartelRegionActivities GetRegionalActivities(Il2CppScheduleOne.Map.EMapRegion region)
    public Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Map.EMapRegion> GetValidRegionsForActivity()
    public System.Void HourPass()
    public System.Void StartGlobalActivity(Il2CppFishNet.Connection.NetworkConnection conn, Il2CppScheduleOne.Map.EMapRegion region, System.Int32 activityIndex)
    public System.Void TryStartActivity()
}
```

Concrete `CartelActivity` subclasses (the pattern to imitate): `CartelCustomerDeal`
(static `TIMEOUT_MINUTES`, field `CartelDealer dealer`), `Ambush`, `RobDealer`, `SprayGraffiti`,
`StealDeadDrop`.

3. **`Il2CppScheduleOne.Cartel.GoonPool`** (`static float MALE_CHANCE`,
   `Il2CppReferenceArray<CartelGoon> goons`, `Il2CppReferenceArray<NPCEnterableBuilding> exitBuildings`)
   plus `CartelGoon.Spawn(GoonPool pool, Vector3 spawnPoint)` / `Despawn()` /
   `ConfigureGoonSettings(NetworkConnection conn, CartelGoonAppearance appearance, float moveSpeed)` —
   a working **pooled spawn/despawn of a group of NPCs that walk out of a building and leave again**.
   That is exactly the mechanic "bikers arrive in town" needs.

---

## 11. Save data

Save-file *names* come from each `ISaveable`'s `SaveFileName`. The relevant plain-string literals do
exist in `global-metadata.dat`: `Customer`, `Dealer`, `Supplier`, `NPCs`, `Products`, `Money`,
`Quests`, `Contracts`, `Business`, `Variables`, `Players`, `Metadata.json`, `Game.json`,
`Money.json`, `Configuration.json`, `Contents.json`, `Appearance.json`.
Mapping literal → file is *inferred* (`SaveFileName` + `SAVE_FILE_EXTENSION` on `SaveManager`).

```csharp
public class Il2CppScheduleOne.Persistence.ISaveable : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
{
    System.String SaveFolderName { public get; }
    System.String SaveFileName { public get; }
    Il2CppScheduleOne.Persistence.Loaders.Loader Loader { public get; }
    System.Boolean ShouldSaveUnderFolder { public get; }
    Il2CppSystem.Collections.Generic.List<System.String> LocalExtraFiles { public get; public set; }
    Il2CppSystem.Collections.Generic.List<System.String> LocalExtraFolders { public get; public set; }
    System.Boolean HasChanged { public get; public set; }
    public virtual System.Void CompleteSave(System.String parentFolderPath, System.Boolean writeDataFile)
    public virtual System.String GetContainerFolder(System.String parentFolderPath)
    public virtual System.String GetSaveString()
    public virtual System.Void InitializeSaveable()
    public virtual System.String Save(System.String parentFolderPath)
    public virtual System.Boolean TryLoadFile(System.String parentPath, System.String fileName, out System.String& contents)
    public virtual Il2CppSystem.Collections.Generic.List<System.String> WriteData(System.String parentFolderPath)
    // …
}
```

### The DTOs (all in `Il2CppScheduleOne.Persistence.Datas`)

```csharp
// --- customers → NPCs.json / per-NPC folder ---
public class CustomerData : Il2CppScheduleOne.Persistence.Datas.SaveData
{
    System.Single Dependence { public get; public set; }
    Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<System.Single> ProductAffinities { public get; public set; }
    System.Int32 TimeSinceLastDealCompleted { public get; public set; }
    System.Int32 TimeSinceLastDealOffered { public get; public set; }
    System.Int32 OfferedDeals { public get; public set; }
    System.Int32 CompletedDeals { public get; public set; }
    System.Boolean IsContractOffered { public get; public set; }
    Il2CppScheduleOne.Quests.ContractInfo OfferedContract { public get; public set; }
    Il2CppScheduleOne.GameTime.GameDateTime OfferedContractTime { public get; public set; }
    System.Int32 TimeSincePlayerApproached { public get; public set; }
    System.Int32 TimeSinceInstantDealOffered { public get; public set; }
    System.Boolean HasBeenRecommended { public get; public set; }

    public .ctor(System.Single dependence, Il2CppStructArray<System.Single> productAffinities,
                 System.Int32 timeSinceLastDealCompleted, System.Int32 timeSinceLastDealOffered,
                 System.Int32 offeredDeals, System.Int32 completedDeals, System.Boolean isContractOffered,
                 Il2CppScheduleOne.Quests.ContractInfo offeredContract, Il2CppScheduleOne.GameTime.GameDateTime offeredTime,
                 System.Int32 timeSincePlayerApproached, System.Int32 timeSinceInstantDealOffered,
                 System.Boolean hasBeenRecommended)
}
```

> **Naming collision to watch for:** `Il2CppScheduleOne.Economy.CustomerData` (ScriptableObject
> tuning asset) vs `Il2CppScheduleOne.Persistence.Datas.CustomerData` (save DTO). `Customer` has both:
> `Customer.CustomerData` (the `Economy` one) and `Customer.GetCustomerData()` /
> `Customer.Load(Persistence.Datas.CustomerData)` (the `Persistence` one).
>
> Note the DTO field is `CompletedDeals` but the live property is `Customer.CompletedDeliveries`, and
> `Dependence` ↔ `Customer.CurrentAddiction`. Also `ProductAffinities` is a *flat float array*
> indexed by `EDrugType` (not the `ProductTypeAffinity` list) — see the literals
> `"Product affinities array is too short"` and `"Product affinity is NaN"`.

```csharp
// --- dealers ---
public class NPCData : Il2CppScheduleOne.Persistence.Datas.SaveData
{
    System.String ID { public get; public set; }
    public .ctor(System.String id)
}

public class DealerData : Il2CppScheduleOne.Persistence.Datas.NPCData
{
    System.Boolean Recruited { public get; public set; }
    Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray AssignedCustomerIDs { public get; public set; }
    Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray ActiveContractGUIDs { public get; public set; }
    System.Single Cash { public get; public set; }
    Il2CppScheduleOne.Persistence.Datas.ItemSet OverflowItems { public get; public set; }
    System.Boolean HasBeenRecommended { public get; public set; }

    public .ctor(System.String id, System.Boolean recruited, Il2CppStringArray assignedCustomerIDs,
                 Il2CppStringArray activeContractGUIDs, System.Single cash,
                 Il2CppScheduleOne.Persistence.Datas.ItemSet overflowItems, System.Boolean hasBeenRecommended)
}

public class SupplierData  : Il2CppScheduleOne.Persistence.Datas.NPCData   { /* debt / deaddrop state */ }
public class EmployeeData  : Il2CppScheduleOne.Persistence.Datas.NPCData
{
    System.String AssignedProperty; System.String FirstName; System.String LastName; System.Boolean IsMale;
    System.Int32 AppearanceIndex; UnityEngine.Vector3 Position; UnityEngine.Quaternion Rotation;
    System.String GUID; System.Boolean PaidForToday;
}

// --- the NPC container: this is NPCs.json ---
public class NPCCollectionData : Il2CppScheduleOne.Persistence.Datas.SaveData
{
    Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppScheduleOne.Persistence.Datas.DynamicSaveData> NPCs { public get; public set; }
    public .ctor(Il2CppReferenceArray<DynamicSaveData> npcs)
}

public class DynamicSaveData : Il2CppScheduleOne.Persistence.Datas.SaveData
{
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
}
public class DynamicSaveData+AdditionalData { System.String Name; System.String Contents; }
```

**This `DynamicSaveData` envelope is the mod's insertion point for extra per-NPC state.** `NPC` and
`Dealer` both override `Load(DynamicSaveData dynamicData, NPCData npcData)`, and
`NPC.GetSaveData()` returns a `DynamicSaveData`. A mod can `AddData("SpecialCustomerGroup", json)`
and read it back with `TryGetData` without breaking vanilla parsing.

```csharp
// --- money → Money.json ---
public class MoneyData : Il2CppScheduleOne.Persistence.Datas.SaveData
{
    System.Single OnlineBalance { public get; public set; }
    System.Single Networth { public get; public set; }
    System.Single LifetimeEarnings { public get; public set; }
    System.Single WeeklyDepositSum { public get; public set; }
    public .ctor(System.Single onlineBalance, System.Single netWorth, System.Single lifetimeEarnings, System.Single weeklyDepositSum)
}

// --- products → Products.json ---
public class ProductManagerData : Il2CppScheduleOne.Persistence.Datas.SaveData
{
    Il2CppStringArray DiscoveredProducts { public get; public set; }
    Il2CppStringArray ListedProducts { public get; public set; }
    Il2CppScheduleOne.Product.NewMixOperation ActiveMixOperation { public get; public set; }
    System.Boolean IsMixComplete { public get; public set; }
    Il2CppReferenceArray<Il2CppScheduleOne.Product.MixRecipeData> MixRecipes { public get; public set; }
    Il2CppReferenceArray<Il2CppScheduleOne.DevUtilities.StringIntPair> ProductPrices { public get; public set; }
    Il2CppStringArray FavouritedProducts { public get; public set; }
    Il2CppReferenceArray<Il2CppScheduleOne.Persistence.Datas.WeedProductData> CreatedWeed { public get; public set; }
    Il2CppReferenceArray<Il2CppScheduleOne.Persistence.Datas.MethProductData> CreatedMeth { public get; public set; }
    Il2CppReferenceArray<Il2CppScheduleOne.Persistence.Datas.CocaineProductData> CreatedCocaine { public get; public set; }
    Il2CppReferenceArray<Il2CppScheduleOne.Persistence.Datas.ShroomProductData> CreatedShrooms { public get; public set; }
    Il2CppReferenceArray<Il2CppScheduleOne.Economy.ContractReceipt> ContractReceipts { public get; public set; }
}

public class ProductData : Il2CppScheduleOne.Persistence.Datas.SaveData
{
    System.String Name; System.String ID; Il2CppScheduleOne.Product.EDrugType DrugType; Il2CppStringArray Properties;
}
// subclasses: WeedProductData, MethProductData, CocaineProductData, ShroomProductData
public class ProductItemData : Il2CppScheduleOne.Persistence.Datas.QualityItemData
{
    System.String PackagingID { public get; public set; }
    public .ctor(System.String iD, System.Int32 quantity, System.String quality, System.String packagingID)
}

// --- contracts → under Quests ---
public class ContractData : Il2CppScheduleOne.Persistence.Datas.QuestData
{
    System.String CustomerGUID { public get; public set; }
    System.Single Payment { public get; public set; }
    Il2CppScheduleOne.Product.ProductList ProductList { public get; public set; }
    System.String DeliveryLocationGUID { public get; public set; }
    Il2CppScheduleOne.Quests.QuestWindowConfig DeliveryWindow { public get; public set; }
    System.Int32 PickupScheduleIndex { public get; public set; }
    Il2CppScheduleOne.Persistence.Datas.GameDateTimeData AcceptTime { public get; public set; }
}

// --- others in scope ---
public class RelationshipData : SaveData { /* NPC relationship + unlock */ }
public class CartelData      : SaveData { /* cartel status + regional activity + active deal */ }
public class DeliveriesData  : SaveData
{
    Il2CppReferenceArray<Il2CppScheduleOne.Delivery.DeliveryInstance> ActiveDeliveries;
    Il2CppReferenceArray<Il2CppScheduleOne.Persistence.Datas.VehicleData> DeliveryVehicles;
    Il2CppReferenceArray<Il2CppScheduleOne.Delivery.DeliveryReceipt> DeliveryHistory;
    Il2CppReferenceArray<Il2CppScheduleOne.Delivery.DeliveryReceipt> DisplayedDeliveryHistory;
}
public class LaunderOperationData : SaveData
public class MSGConversationData  : SaveData   // per-NPC message history
```

### Loaders

`Il2CppScheduleOne.Persistence.Loaders` has 68 loaders, including `NPCsLoader : Loader`
(`Load(string mainPath)`, property `NPCType`) and `NPCLoader : DynamicLoader`
(`Load(DynamicSaveData saveData)`, property `NPCType`), `MoneyLoader`, `ProductManagerLoader`,
`QuestsLoader`, `CartelLoader`, `DeliveriesLoader`, `RankLoader`, `VariablesLoader`, `PlayersLoader`.

**There is no `CustomerLoader` and no `DealerLoader`.** Dealer save data rides on
`NPCLoader`/`DealerData`. `Customer` restores through the load event registered in `Customer.Start()`
(`<Start>g__RegisterLoadEvent|140_0`, surfaced as `Customer.Method_Private_Void_0()`) plus
`Customer.Load(Persistence.Datas.CustomerData data)` and `ISaveable.TryLoadFile(...)`.
`Customer.Loader` is very likely `null` — **UNVERIFIED**, check at runtime.

Multiplayer late-join replication of customer state: `Customer.OnSpawnServer(NetworkConnection connection)`
→ `Customer.ReceiveCustomerData(NetworkConnection conn, Persistence.Datas.CustomerData data)` (TargetRpc).
Error literals: `" error reading customer data: "`, `"Customer data already exists"`,
`"Error loading contract data: "`, `"Failed to find customer with GUID: "`,
`"Failed to find customer NPC with ID "`, `"Invalid contract GUID: "`.

---

## Runtime flow — the complete customer lifecycle

Legend: **[S]** server-only, **[C]** local client, **[O]** observers, **[T]** target client,
**[Any]** either side.

> **Confidence note for this section:** every *method name and signature* is verbatim from the dumps.
> The side annotations are derived from each method's RPC kind (`RpcWriter___Server_*` ⇒ ServerRpc ⇒
> body runs on the server, `…Observers_*` ⇒ all clients, `…Target_*` ⇒ one client) plus the guard
> literals `"SendContractAccepted can only be called on the server!"` and
> `"Contract accepted called on client!"` — those are solid. The **call ordering and the
> "who calls whom" arrows for non-RPC methods are inferred** from names, parameter shapes, the
> compiler-generated closure names, and the debug literals. Treat the graph as a high-confidence map,
> not a decompilation.

```
                        ┌───────────────────────── SPAWN / LOAD ─────────────────────────┐
[Any]  Customer.Awake()  ──► Customer.Method_Protected_Virtual_New_Void_0()   (= Awake_UserLogic…)
                            ├─ resolve NPC back-ref + CustomerAttendDealBehaviour  (else self-disable)
                            ├─ register in Customer.LockedCustomers / UnlockedCustomers
                            └─ Customer.AutocreateCustomerSettings()  (if customerData missing — inferred)
[Any]  Customer.Start()  ──► Customer.SetUpDialogue()   (installs sampleChoice / offerDealChoice /
                            │                            completeContractChoice / awaitingDealGreeting
                            │                            onto NPC.DialogueHandler's DialogueController)
                            ├─ Customer.SetupPoI()      (NPCManager.PotentialCustomerPoIPrefab)
                            ├─ Customer.Method_Private_Void_0()  (= <Start>g__RegisterLoadEvent|140_0)
                            └─ NPC.RelationData.OnUnlocked += Customer._Start_b__140_1
[S]    Customer.OnSpawnServer(conn) ─► Customer.ReceiveCustomerData(conn, Persistence.Datas.CustomerData) [T]
[Any]  Customer.Load(Persistence.Datas.CustomerData data)

                        ┌────────────────────────── UNLOCK ──────────────────────────────┐
[C]    Customer.IsUnlockable()               ← CustomerData.Min/MaxMutualRelationRequirement
                                               vs NPCRelationData.GetAverageMutualRelationship()
[C]    Customer.ShowDirectApproachOption(bool)  ← CustomerData.CanBeDirectlyApproached
[C]    Customer.SampleOptionValid(out reason) / Customer.KnownAndRecommended()
                                               ← Customer.SAMPLE_REQUIRES_RECOMMENDATION,
                                                 Customer.HasBeenRecommended
[C]    Customer.SampleOffered()  ──► HandoverScreen.Open(…, EMode.Sample,
                                        successChanceMethod = Customer.GetSampleSuccess) 
[C]    Customer.GetSampleRequestSuccessChance()      ← ProductManager.SampleSuccessCurve
[C]→[S] Customer.ProcessSample(outcome, items, price) ─► Customer.ProcessSampleServerSide(items) [S]
[S]    Customer.SampleConsumed() / Customer.ConsumeProduct(ItemInstance)
[O]    Customer.ProcessSampleClient()
[O]    Customer.SampleWasSufficient()  ──► NPCRelationData.Unlock(EUnlockType.DirectApproach, notify)
       Customer.SampleWasInsufficient() (or Customer.DirectApproachRejected())
                                               ← CustomerData.GuaranteeFirstSampleSuccess forces success
[Any]  NPCRelationData.OnUnlocked ──► Customer.OnCustomerUnlocked(EUnlockType, bool notify)
                            ├─ move Locked→UnlockedCustomers; invoke static Customer.onCustomerUnlocked
                            ├─ invoke instance UnityEvent Customer.onUnlocked
                            ├─ Customer.SetPotentialCustomerPoIEnabled(false)
                            ├─ NewCustomerPopup.PlayPopup(Customer)                            [C]
                            └─ CartelInfluence.ChangeInfluence(region, CUSTOMER_UNLOCKED_CARTEL_INFLUENCE_CHANGE)
[S]    Customer.SetHasBeenRecommended()  (SyncVar HasBeenRecommended)
[C]    Customer.RecommendCustomer(Customer friend) / RecommendDealer(Dealer) / RecommendSupplier(Supplier)

                        ┌───────────── AFFINITY / ADDICTION (continuous) ────────────────┐
[Any]  Customer.currentAffinityData  ← CustomerData.DefaultAffinityData.CopyTo(...)
[S]    Customer.AdjustAffinity(EDrugType, float)   → CustomerAffinityData.GetAffinity(EDrugType)
[S]    Customer.ChangeAddiction(float)             → SyncVar CurrentAddiction
[S]    per-day drain by Customer.ADDICTION_DRAIN_PER_DAY (CustomerData.BaseAddiction seed,
                                                          CustomerData.DependenceMultiplier scale)

                        ┌──────────────── ORDER GENERATION (server) ─────────────────────┐
[S]    Customer.OnMinPass() / Customer.OnTick()
        ├─ advance TimeSinceLastDealOffered / TimeSinceLastDealCompleted / minsSinceUnlocked
        ├─ Customer.UpdateOfferExpiry()   → Customer.ExpireOffer()          (OFFER_EXPIRY_TIME_MINS)
        ├─ Customer.UpdateDealAttendance()                                  (DEAL_ATTENDANCE_TOLERANCE)
        ├─ Customer.ShouldTryApproachPlayer()  (APPROACH_MIN_ADDICTION, APPROACH_CHANCE_PER_DAY_MAX,
        │                                        APPROACH_MIN_COOLDOWN, APPROACH_MAX_COOLDOWN)
        │       └─► Customer.RequestProduct(Player target) → Customer.InstantDealOffered()
        └─ Customer.ShouldTryGenerateDeal()   (DEAL_COOLDOWN, Customer.IsDealTime(),
                                               Customer.MinsSinceLastDealOfferedAllCustomers(),
                                               ProductManager.IsAcceptingOrders)
                └─► CustomerData.GetOrderDays(dependence, normalizedRelationship, days) → _cachedOrderDays
                └─► CustomerData.GetAdjustedWeeklySpend(normalizedRelationship)
                └─► Customer.GetOrderableProductsWithQuantities(dealer)
                        └─ (dealer != null) Dealer.GetOrderableProducts(minQuality)
                                            Dealer.GetOrderableProductQuantity(id, min, max)
                        └─ (dealer == null) ProductManager.ListedProducts
                └─► Customer.GetWeightedRandomProduct(dealer, out appeal, out orderableQuantity)
                        └─ Customer.GetProductEnjoyment(def, quality)
                           (AFFINITY_MAX_EFFECT / PROPERTY_MAX_EFFECT / QUALITY_MAX_EFFECT,
                            CustomerData.PreferredProperties, CustomerData.Standards →
                            StandardsMethod.GetCorrespondingQuality, Customer.QualityTierTolerance,
                            floor Customer.MIN_ORDER_APPEAL)
                        └─ clamp Customer.MaxOrderQuantityPerProduct
                        └─ scale LevelManager.GetOrderLimitMultiplier(FullRank)
                └─► Customer.TryGenerateContract(Dealer) → ContractInfo
                        └─ Customer.GetDeliveryLocation() /
                           MapRegionData.GetRandomUnscheduledDeliveryLocation()
                        └─ Customer.GetContractTimings(QuestWindowConfig, out soft, out hard, out end)

                        ┌──────────────────────── OFFER MESSAGE ─────────────────────────┐
[S]    Customer.OfferContract(ContractInfo)                    (player route)
       Customer.OfferContractToDealer(ContractInfo, Dealer)    (dealer route)
[O]    Customer.SetOfferedContract(ContractInfo, GameDateTime)
[C]    Customer.NotifyPlayerOfContract(ContractInfo, MessageChain, canAccept, canReject, canCounterOffer)
[C]    ContractInfo.ProcessMessage(DialogueChain)  → key contract_request / first_contract_request / urgent_contract
[C]→[O] MSGConversation.SendMessageChain(...) via MessagingManager.SendMessageChain(...)
[O]    Customer.SetUpResponseCallbacks()  → MSGConversation.ShowResponses(List<Response>, delay, network)
[S]    Dealer.ShouldAcceptContract(ContractInfo, Customer) → Dealer.ContractedOffered(...)  (dealer route)
                                                            → Dealer.GetDealWindow()
                                                            → Dealer.AddContract(Contract)

                        ┌───────────────────── ACCEPT / COUNTER ─────────────────────────┐
[C]    MSGConversation.ResponseChosen(Response, network)
        ├─ Customer.AcceptContractClicked() → DealWindowSelector → Customer.PlayerAcceptedContract(EDealWindow)
        │      └─►[S] Customer.SendContractAccepted(EDealWindow, trackContract)
        │              └─ Customer.ContractAccepted(EDealWindow, trackContract, Dealer) → Contract
        │                  └─ QuestManager.ContractAccepted(Customer, ContractInfo, track, guid, Dealer)
        │                      ├─[S] QuestManager.CreateContract_Local(...)  → Contract.InitializeContract(...)
        │                      └─[O] QuestManager.CreateContract_Networked(...)
        │                  └─ Customer.AssignContract(Contract)  → UnityEvent onContractAssigned
        │              └─►[O] Customer.ReceiveContractAccepted() → Customer.PlayContractAcceptedReaction()
        ├─ Customer.CounterOfferClicked() → Customer.SendCounteroffer(def, qty, price)
        │      └─►[S] Customer.ProcessCounterOfferServerSide(productID, qty, price)
        │              └─ Customer.EvaluateCounteroffer(def, qty, price)
        │                     └─ Customer.GetValueProposition(def, price)
        │              └─►[O] Customer.SetContractIsCounterOffer()  (accept) / ContractRejected() (reject)
        └─ reject → Customer.ContractRejected() → [O] Customer.ReceiveContractRejected()
                       → Customer.PlayContractRejectedReaction()
                       → NPCRelationData.ChangeRelationship(Customer.DEAL_REJECTED_RELATIONSHIP_CHANGE)

                        ┌──────────────────── TRAVEL & HANDOVER ─────────────────────────┐
[S]    Customer.UpdateDealAttendance() → CustomerAttendDealBehaviour.SetContract(Contract) → Activate()
[S]    CustomerAttendDealBehaviour.OnActiveTick() / IsAtDestination() / CheckWarp() / EnsureNPCHasEnoughCash()
[S]    Customer.SetIsAwaitingDelivery(true); Customer.IsAtDealLocation()
[S]    (dealer route) DealerAttendDealBehaviour.AssignContract(Contract) → BeginHandover()
                       → IsCustomerReadyForHandover() → Dealer.RemoveContractItems(...)
[C]    Customer.IsReadyForHandover(bool) → Customer.IsHandoverChoiceValid(out reason) → Customer.HandoverChosen()
[C]    HandoverScreen.Open(Contract, Customer, EMode.Contract, callback, successChanceMethod, requireFull)
[C]    HandoverScreen.UpdateSuccessChance() / GetError / GetWarning / UpdateDoneButton
[C]    HandoverScreen.DonePressed() → Close(EHandoverOutcome.Finalize)
        → Customer._HandoverChosen_b__221_0(outcome, items, price)
[C]    Customer.ProcessHandover(outcome, Contract, items, handoverByPlayer, giveBonuses = true)
        └─ Customer.EvaluateDelivery(Contract, providedItems, out highestAddiction, out mainTypeType,
                                     out matchedProductCount, out qualityDifference)
               └─ Contract.GetProductListMatch(items, out matchedProductCount)
               └─ Contract.DoesProductListMatchSpecified(items, enforceQuality)
               └─ Contract.GetDescendingMatchRatings(ProductList+Entry, providedItems)
               └─ ProductItemInstance.GetSimilarity(ProductDefinition, EQuality)
[C]→[S] Customer.ProcessHandoverServerSide(outcome, items, handoverByPlayer, totalPayment,
                                           ProductList, satisfaction, dealerObject)

                        ┌────────────────── PAYMENT & CONSEQUENCES ──────────────────────┐
[S]    Contract.SubmitPayment(float bonusTotal)
        ├─ player route   → MoneyManager.ChangeCashBalance(change, visualizeChange, playCashSound)
        └─ dealer route   → Dealer.SubmitPayment(float payment) → Dealer.ChangeCash(float)
                            → Dealer.Cash (SyncVar) → Dealer.CollectCash() later
[S]    MoneyManager.ChangeLifetimeEarnings(float)
[S]    Contract.Complete(bool network = true) → Contract.End()
[S]    Customer.CurrentContractEnded(EQuestState outcome)   (Dealer.CustomerContractEnded(Contract))
[S]    Customer.ChangeAddiction(float) / Customer.AdjustAffinity(EDrugType, float)
[S]    CustomerSatisfaction.GetRelationshipChange(satisfaction)
        → NPCRelationData.ChangeRelationship(delta, network = true)
        (dealer route also applies Dealer.RELATIONSHIP_CHANGE_PER_DEAL)
[S]    ProductManager.RecordContractReceipt(conn, ContractReceipt)  → onContractReceiptRecorded
[S]    LevelManager.AddXP(int)   (Quest.CompletionXP)
[S]    Customer.ConsumeProduct(ItemInstance) → ProductItemInstance.ApplyEffectsToNPC(NPC)
[O]    Customer.ProcessHandoverClient(satisfaction, handoverByPlayer, npcToRecommend, outcome)
        ├─ Customer.ContractWellReceived(npcToRecommend)  → Recommend{Customer,Dealer,Supplier}
        ├─ UnityEvent Customer.onDealCompleted
        ├─ Customer.CalculateTopWeeklyPurchases(out mostPurchasedProducts, out totalSpent)
        └─ deal-completion popup ("Playing deal completion popup for {0} with satisfaction {1:P0} …")
[S]    RELATIONSHIP_THRESHOLD_TO_GIVE_DEAL_TO_CARTEL → CartelCustomerDeal / CartelDealer path
[S]    CustomerData.CallPoliceChance → sample_offer_rejected_police / Il2CppScheduleOne.Law.*
```

---

## Hooks & extension points

### A. Create a new customer at runtime

There is no factory. You must build the component graph yourself. The minimum viable set,
derived from `Customer`'s own field graph and the self-disable literal:

1. An `Il2CppScheduleOne.NPCs.NPC` (spawned via the game's NPC prefab pipeline, or S1API's NPC
   builder). It must be in `NPCManager.NPCRegistry` and have `NPC.Region`, `NPC.RelationData`,
   `NPC.DialogueHandler`, `NPC.MSGConversation`.
2. `AddComponent<Il2CppScheduleOne.NPCs.Behaviour.CustomerAttendDealBehaviour>()` on the NPC's
   behaviour container — **mandatory**, or `Customer` logs
   `"CustomerAttendDealBehaviour is required for Customer.cs to function properly. Disabling Customer component on "`
   and disables itself.
3. `AddComponent<Il2CppScheduleOne.Economy.Customer>()`.
4. Set `Customer.NPC`, `Customer._attendDealBehaviour`, `Customer.customerData`
   (a `ScriptableObject.CreateInstance<Il2CppScheduleOne.Economy.CustomerData>()`), and
   `Customer.currentAffinityData` (`new CustomerAffinityData()` then
   `CustomerData.DefaultAffinityData.CopyTo(currentAffinityData)`).
5. Because `Customer : NetworkBehaviour`, call `Customer.NetworkInitialize___Early()`,
   `NetworkInitialize__Late()`, and `NetworkInitializeIfDisabled()` — FishNet's generated
   `Awake` normally does this and will not run for a component added after `Awake`.
6. `Customer.SetUpDialogue()` and `Customer.SetupPoI()`.
7. `Customer.AutocreateCustomerSettings()` if you want the game to fill in defaults.
8. Unlock: `NPC.RelationData.Unlock(NPCRelationData+EUnlockType.DirectApproach, notify)` →
   `Customer.OnCustomerUnlocked(...)`, or call `Customer.OnCustomerUnlocked` directly.

**Precedent that this works:** `S1API.Entities.NPCCustomer` in the installed S1API does exactly this
and exposes it as `public System.Void EnsureCustomer()`. Its private helpers name the required steps
verbatim: `EnsureCustomerData(Customer)`, `EnsureCurrentAffinityDataInitialized(Customer)`,
`EnsureUnityEvents(Customer)`, `WireCoreReferences(Customer)`, `TryNetworkInitialize(Customer)`,
`InitializeRuntimeState(Customer)`, and the static
`EnsureDealAttendanceSupport(UnityEngine.GameObject prefabRoot, System.Type ownerType = null)`.
Also present: `S1API.Entities.Customer.CustomerDataBuilder` (21 fluent methods that map 1:1 onto
`Il2CppScheduleOne.Economy.CustomerData`'s fields) and
`S1API.Internal.Patches.NPCPatches+<DelayedCustomerAssignment>d__59`.

### B. Force / inject an order

Ranked from least to most invasive:

| Goal | Call / patch |
|---|---|
| Fire the game's own generator now, skipping cooldowns | `Customer.ForceDealOffer()` — **the single best entry point**. Public, no args, no RPC. |
| Supply a completely hand-built offer | build `Il2CppScheduleOne.Quests.ContractInfo` → `Customer.OfferContract(info)` **[S]** |
| Route the offer through a dealer | `Customer.OfferContractToDealer(info, dealer)` **[S]** |
| Skip the phone entirely and materialise an accepted contract | `Customer.ContractAccepted(EDealWindow, trackContract, Dealer)` **[S]**, or `QuestManager.Instance.ContractAccepted(Customer, ContractInfo, track, guid, Dealer)` |
| Fully manual contract object | `QuestManager.Instance.CreateContract_Local(title, description, entries, guid, tracked, customer, payment, products, deliveryLocationGUID, deliveryWindow, expires, expiry, pickupScheduleIndex, acceptTime, dealer = null)` then `QuestManager.Instance.CreateContract_Networked(...)` for observers |
| Force the *instant* street-deal path | `Customer.RequestProduct()` / `Customer.RequestProduct(Player target)` → `Customer.InstantDealOffered()` |
| Bypass the gate permanently | Harmony postfix `Customer.ShouldTryGenerateDeal()` → `__result = true`; and/or prefix `Customer.IsDealTime()` |
| Loosen the global throttle | Harmony postfix `Customer.MinsSinceLastDealOfferedAllCustomers()` → return a large number |

`ForceDealOffer()` is the honest answer to "can an order be forced": **yes, with one public
parameterless call, and it is server-side, so it also works in multiplayer as host.**

### C. Change what a customer wants and how much they pay

Data-level (no patching, fully save-compatible):

```csharp
var cd = customer.CustomerData;                        // Il2CppScheduleOne.Economy.CustomerData
cd.MinWeeklySpend = 2000f; cd.MaxWeeklySpend = 8000f;  // budget
cd.MinOrdersPerWeek = 4;   cd.MaxOrdersPerWeek = 7;    // frequency
cd.OrderTime = 2200;                                   // HHMM
cd.PreferredOrderDay = Il2CppScheduleOne.GameTime.EDay.Saturday;
cd.Standards = Il2CppScheduleOne.Economy.ECustomerStandard.High;
cd.PreferredProperties = /* List<Il2CppScheduleOne.Effects.Effect> from PropertyUtility */;
cd.BaseAddiction = 0.4f; cd.DependenceMultiplier = 1.5f;
cd.CanBeDirectlyApproached = true; cd.GuaranteeFirstSampleSuccess = true;
cd.CallPoliceChance = 0f;
cd.MinMutualRelationRequirement = 0f; cd.MaxMutualRelationRequirement = 0f;
cd.DefaultAffinityData.ProductAffinities /* List<ProductTypeAffinity> { DrugType, Affinity } */;
cd.onChanged?.Invoke();
// live affinity:
customer.AdjustAffinity(Il2CppScheduleOne.Product.EDrugType.Marijuana, +0.5f);   // [S]
customer.currentAffinityData /* CustomerAffinityData */;
```

Global tuning knobs (static properties — writable through interop, affects every customer):
`Customer.MaxOrderQuantityPerProduct`, `Customer.QualityTierTolerance`,
`Customer.AFFINITY_MAX_EFFECT`, `Customer.PROPERTY_MAX_EFFECT`, `Customer.QUALITY_MAX_EFFECT`,
`Customer.MIN_ORDER_APPEAL`, `Customer.DEAL_COOLDOWN`, `Customer.OFFER_EXPIRY_TIME_MINS`,
`Customer.ADDICTION_DRAIN_PER_DAY`, `Customer.SAMPLE_REQUIRES_RECOMMENDATION`,
`Customer.MIN_TRAVEL_TIME` / `MAX_TRAVEL_TIME`, `Customer.DEAL_ATTENDANCE_TOLERANCE`.

Harmony seams for shaping the generated order:

| Patch | Effect |
|---|---|
| postfix `Customer.TryGenerateContract(Dealer)` | rewrite the whole `ContractInfo` — payment, `ProductList`, window, expiry. **The one hook that controls everything about an order.** |
| postfix `Customer.GetOrderableProductsWithQuantities(Dealer)` | inject/remove candidate products + per-product quantities |
| postfix `Customer.GetWeightedRandomProduct(Dealer, out float, out int)` | force a specific product and quantity |
| postfix `Customer.GetProductEnjoyment(ProductDefinition, EQuality)` | reshape appeal without touching affinities |
| postfix `CustomerData.GetAdjustedWeeklySpend(float)` | multiply budgets (this is the "buys large quantities" lever) |
| postfix `CustomerData.GetOrderDays(float, float, List<EDay>)` | force order days / bump order count |
| postfix `Customer.GetValueProposition(ProductDefinition, float)` (static) | make the customer accept higher prices |
| postfix `Customer.EvaluateCounteroffer(ProductDefinition, int, float)` | always accept counter-offers |
| postfix `Customer.EvaluateDelivery(...)` | control satisfaction (drives payment bonuses + relationship) |
| postfix `LevelManager.GetOrderLimitMultiplier(FullRank)` (static) | lift the rank-based order size ceiling |
| postfix `Dealer.ShouldAcceptContract(ContractInfo, Customer)` | make dealers take the bulk orders |
| postfix `CustomerSatisfaction.GetRelationshipChange(float)` (static) | control relationship gain per deal |

### D. Add a periodic "group visits town" event

No dedicated API exists; you build a scheduler and attach it to an existing time source.

**Time sources** (pick one; all are real methods you can Harmony-postfix or subscribe to):

| Hook | Cadence |
|---|---|
| `Customer.OnMinPass()` | per in-game minute, per customer |
| `Il2CppScheduleOne.Cartel.CartelActivities.HourPass()` / `CartelRegionActivities.HourPass()` / `Cartel.HourPass()` | per in-game hour, global / per region |
| `Il2CppScheduleOne.Cartel.CartelActivity.HourPassed()` / `MinPassed()` | per activity |
| `Il2CppScheduleOne.Product.ProductManager.OnNewDay()` / `OnMinPass()` | per day / minute, global |
| `Il2CppScheduleOne.Money.MoneyManager.MinPass()` | per minute, global |
| `Il2CppScheduleOne.Delivery.DeliveryManager.OnTimePass(System.Int32 minutes)` | per tick, global |
| `Il2CppScheduleOne.Economy.Supplier.HourPass()` / `OnTimeSkip(System.Int32 minsSlept)` | per hour + sleep skip |
| `Customer.OnSleepStart()` | on sleep |
| `Il2CppScheduleOne.Quests.Quest.OnMinPass()` / `OnUncappedMinPass()` | per minute (uncapped variant survives time-skip) |

**Structural template to copy** — `CartelRegionActivities`:

```csharp
// per region: MIN_COOLDOWN / MAX_COOLDOWN hours, HoursUntilNextActivity countdown,
// CurrentActivity, TryStartActivity() on HourPass(), StartActivity(conn, index) as an
// Observers+Target RPC pair, and GetData()/Load(CartelRegionalActivityData) for persistence.
Il2CppScheduleOne.Cartel.CartelActivities.GetValidRegionsForActivity()   // → List<EMapRegion>
Il2CppScheduleOne.Cartel.CartelActivities.GetRegionalActivities(EMapRegion)
Il2CppScheduleOne.Cartel.CartelRegionActivities.GetNewCooldown(EMapRegion)   // static
Il2CppScheduleOne.Cartel.CartelActivity.Activate(EMapRegion) / Deactivate()
Il2CppScheduleOne.Cartel.CartelActivity.IsRegionValidForActivity(EMapRegion)
```

**Spawn/despawn template** — `GoonPool` + `CartelGoon`:

```csharp
Il2CppScheduleOne.Cartel.GoonPool                       // Il2CppReferenceArray<CartelGoon> goons,
                                                        // Il2CppReferenceArray<NPCEnterableBuilding> exitBuildings,
                                                        // static float MALE_CHANCE
Il2CppScheduleOne.Cartel.CartelGoon.Spawn(Il2CppScheduleOne.Cartel.GoonPool pool, UnityEngine.Vector3 spawnPoint)
Il2CppScheduleOne.Cartel.CartelGoon.Despawn()
Il2CppScheduleOne.Cartel.CartelGoon.Spawn_Client(Il2CppFishNet.Connection.NetworkConnection conn)
Il2CppScheduleOne.Cartel.CartelGoon.Despawn_Client(Il2CppFishNet.Connection.NetworkConnection conn)
Il2CppScheduleOne.Cartel.CartelGoon.ConfigureGoonSettings(Il2CppFishNet.Connection.NetworkConnection conn,
        Il2CppScheduleOne.Cartel.CartelGoonAppearance appearance, System.Single moveSpeed)
Il2CppScheduleOne.Cartel.CartelGoon.AddGoonMate(Il2CppScheduleOne.Cartel.CartelGoon goonMate)  // the "group" link
Il2CppScheduleOne.Cartel.CartelGoon.IsMatesWith(Il2CppScheduleOne.Cartel.CartelGoon otherGoon)
Il2CppScheduleOne.Cartel.CartelGoonAppearance                  // IsMale, BaseAppearanceIndex, SkinColor,
                                                               // HairColor, ClothingIndex, VoiceIndex
Il2CppScheduleOne.NPCs.NPCManager.NPCWarpPoints / GetOrderedDistanceWarpPoints(UnityEngine.Vector3 origin)
Il2CppScheduleOne.Map.NPCEnterableBuilding.GetSummonableNPCs() / GetClosestDoor(Vector3, bool)
```

**Announcement / notification surface**:

```csharp
Il2CppScheduleOne.Messaging.MessagingManager.Instance.SendMessageChain(
    Il2CppScheduleOne.UI.Phone.Messages.MessageChain m, System.String npcID,
    System.Single initialDelay, System.Boolean notify)                       // ServerRpc
Il2CppScheduleOne.Messaging.MSGConversation.SendMessageChain(MessageChain messages, System.Single initialDelay = 0,
    System.Boolean notify = True, System.Boolean network = True)
Il2CppScheduleOne.UI.Phone.Messages.MessageChain.Combine(MessageChain a, MessageChain b)
Il2CppScheduleOne.UI.NewCustomerPopup.Instance.PlayPopup(Il2CppScheduleOne.Economy.Customer customer)
Il2CppScheduleOne.NPCs.NPC.SendTextMessage(System.String message)
```

Precedent for a location announcement message: the literal
`"Hey boss, I've heard there's a Benzies deal happening in {0}, {1}. Might be worth checking out."`.

### E. Persist your group state

Use the `DynamicSaveData` envelope so vanilla keeps parsing the file:

```csharp
// write (patch NPC.GetSaveData() postfix, or NPC/Dealer WriteData):
dynamicSaveData.AddData("SpecialCustomerGroup", jsonString);
// read (patch NPC.Load(DynamicSaveData, NPCData) prefix):
if (dynamicSaveData.TryGetData("SpecialCustomerGroup", out string json)) { /* … */ }
```

### F. RPC patching rules

FishNet method triple:

```
public  X(args)                              // entry point — patch here to intercept before the hop
RpcWriter___Server_X_<hash>(args)            // serialiser (ServerRpc)
RpcWriter___Observers_X_<hash>(args)         // serialiser (ObserversRpc)
RpcWriter___Target_X_<hash>(conn, args)      // serialiser (TargetRpc)
RpcLogic___X_<hash>(args)                    // ← the real body; patch here to change behaviour
RpcReader___Server_X_<hash>(PooledReader, Channel, NetworkConnection)
RpcReader___Observers_X_<hash>(PooledReader, Channel)
RpcReader___Target_X_<hash>(PooledReader, Channel)
```

Practical rule: **patch the public method** to block/reshape a call before it goes over the wire;
**patch `RpcLogic___*`** to change what actually happens on the receiving side. The `<hash>` suffixes
are signature-derived — pin them per game version or resolve them with a name-prefix search at load.

---

## Building "special customers" — what already exists vs what must be written

### Already exists (use as-is)

| Need | Existing API |
|---|---|
| Customer behaviour engine | `Il2CppScheduleOne.Economy.Customer` — a self-contained `NetworkBehaviour` you can add to any NPC |
| Full preference schema | `Il2CppScheduleOne.Economy.CustomerData` (ScriptableObject, 16 fields), `CustomerAffinityData`, `ProductTypeAffinity`, `ECustomerStandard`, `StandardsMethod` |
| Force an order right now | `Customer.ForceDealOffer()` |
| Hand-build an order | `Il2CppScheduleOne.Quests.ContractInfo` + `Customer.OfferContract(...)` + `QuestManager.ContractAccepted(...)` |
| Bulk quantity levers | `Customer.MaxOrderQuantityPerProduct`, `CustomerData.Min/MaxWeeklySpend`, `LevelManager.GetOrderLimitMultiplier(FullRank)`, `Customer.QualityTierTolerance` |
| Travel to a deal location | `CustomerAttendDealBehaviour` + `Il2CppScheduleOne.Economy.DeliveryLocation` + `MapRegionData.GetRandomUnscheduledDeliveryLocation()` |
| Handover UI, all three modes | `Il2CppScheduleOne.UI.Handover.HandoverScreen.Open(...)` with a custom `successChanceMethod` delegate |
| Payment | `Contract.SubmitPayment(float)`, `MoneyManager.ChangeCashBalance(...)`, `Dealer.SubmitPayment(float)` |
| Relationship / addiction consequences | `CustomerSatisfaction.GetRelationshipChange(float)`, `NPCRelationData.ChangeRelationship(...)`, `Customer.ChangeAddiction(float)`, `Customer.AdjustAffinity(EDrugType, float)` |
| Phone offer messages + responses | `MessagingManager`, `MSGConversation`, `MessageChain`, `Response`, `SendableMessage`, `Customer.NotifyPlayerOfContract(...)`, `Customer.SetUpResponseCallbacks()` |
| In-world dialogue choices | `DialogueController+DialogueChoice` via `Customer.SetUpDialogue()` / `DialogueController.AddDialogueChoice(DialogueChoice, int priority = 0)` |
| Map markers | `Il2CppScheduleOne.Map.NPCPoI` + `NPCManager.PotentialCustomerPoIPrefab` + `Customer.SetupPoI()` / `SetPotentialCustomerPoIEnabled(bool)` |
| Region cohort key | `Il2CppScheduleOne.NPCs.NPC.Region` (`EMapRegion`), `NPCManager.GetNPCsInRegion(EMapRegion)`, `Map.GetUnlockedRegions()` |
| Periodic region event scheduler | `CartelActivities` / `CartelRegionActivities` / `CartelActivity` (copy structurally) |
| Group spawn / despawn of NPCs | `GoonPool` + `CartelGoon.Spawn/Despawn/AddGoonMate/IsMatesWith` |
| Dealer assignment of new customers | `Dealer.AddCustomer(Customer)` / `AddCustomer_Server(string npcID)`; UI auto-lists via `DealerManagementApp` + `CustomerSelector` |
| Extra per-NPC save state | `DynamicSaveData.AddData` / `TryGetData` |
| Existing high-level wrapper | `S1API.Entities.NPCCustomer.EnsureCustomer/ForceDealOffer/OfferContract/RequestProduct/Unlock/SetupDialog/SetAwaitingDelivery` + `S1API.Entities.Customer.CustomerDataBuilder` + `S1API.Economy.ContractInfoBuilder` |

### Must be written by the mod

1. **The group concept itself.** No `CustomerGroup`, no faction/tag/cohort field anywhere. You need
   your own `record SpecialCustomerGroup { string Id; EMapRegion Region; CustomerData Template;
   int MemberCount; int VisitDurationHours; … }` plus a `Dictionary<string /*npcId*/, string /*groupId*/>`
   side table, persisted through `DynamicSaveData`.
2. **The visit scheduler.** `CartelRegionActivities` is the shape to copy but it is `sealed` in
   practice (a scene `NetworkBehaviour` you cannot subclass without a prefab), so write your own
   `MonoBehaviour`/`NetworkBehaviour` driven off one of the `HourPass`/`OnMinPass` hooks in §D and
   persist `HoursUntilNextVisit` yourself.
3. **NPC spawning for group members.** Either reuse existing scene NPCs (cheapest, most stable —
   temporarily attach a `Customer` + your group's `CustomerData`, then detach) or spawn new NPCs.
   Spawning new networked NPCs from scratch is the highest-risk part; the `GoonPool` pattern
   (pre-placed pooled NPCs, activated/deactivated) is safer than instantiating prefabs, and S1API's
   NPC builder path is safer still.
4. **Bulk-order shaping.** `TryGenerateContract` will produce ordinary orders even with inflated
   `CustomerData` numbers because of `Customer.MaxOrderQuantityPerProduct` and the rank multiplier.
   For genuinely large orders, postfix `Customer.TryGenerateContract(Dealer)` and rewrite
   `ContractInfo.Products` / `ContractInfo.Payment` yourself.
5. **Group-scoped price/payment premium.** Nothing in the game scales payment per cohort. Postfix
   `Customer.GetValueProposition(ProductDefinition, float)` and/or rewrite `ContractInfo.Payment`.
6. **Multiplayer discipline.** Every mutation in the pipeline is server-authoritative
   (`"SendContractAccepted can only be called on the server!"`). Your scheduler must run only on the
   host and replicate via your own RPCs or by driving the existing public entry points, which already
   do the hop.
7. **Kill-switch + forward compatibility.** TVGS is shipping their own Special Customers as v0.5.0
   (see `CONTEXT.md`). Guard every `Customer` field write behind a null/`InstanceExists` check, never
   patch fallback-named members without a runtime resolve, and gate the whole feature behind a config
   flag.

### The concrete answer to "can a `Customer` component be added to a freshly spawned NPC and made to place large orders?"

**Yes.** Required sequence, all verbatim members:

```csharp
// 1. behaviour prerequisite (mandatory — else Customer self-disables)
var attend = npcGameObject.AddComponent<Il2CppScheduleOne.NPCs.Behaviour.CustomerAttendDealBehaviour>();

// 2. the component
var customer = npcGameObject.AddComponent<Il2CppScheduleOne.Economy.Customer>();

// 3. wire references
customer.NPC = npc;                      // Il2CppScheduleOne.NPCs.NPC
customer._attendDealBehaviour = attend;

// 4. tuning asset
var data = UnityEngine.ScriptableObject.CreateInstance<Il2CppScheduleOne.Economy.CustomerData>();
data.MinWeeklySpend = 5000f; data.MaxWeeklySpend = 15000f;
data.MinOrdersPerWeek = 5;   data.MaxOrdersPerWeek = 7;
data.Standards = Il2CppScheduleOne.Economy.ECustomerStandard.Moderate;
data.CanBeDirectlyApproached = true;
data.GuaranteeFirstSampleSuccess = true;
data.DefaultAffinityData = new Il2CppScheduleOne.Economy.CustomerAffinityData();
// … + PreferredProperties, BaseAddiction, DependenceMultiplier, OrderTime, PreferredOrderDay, CallPoliceChance
customer.customerData = data;
customer.currentAffinityData = new Il2CppScheduleOne.Economy.CustomerAffinityData();
data.DefaultAffinityData.CopyTo(customer.currentAffinityData);

// 5. FishNet init (generated Awake already ran / never ran for a late-added component)
customer.NetworkInitialize___Early();
customer.NetworkInitialize__Late();
customer.NetworkInitializeIfDisabled();

// 6. dialogue + map marker
customer.SetUpDialogue();
customer.SetupPoI();

// 7. unlock
npc.RelationData.Unlock(Il2CppScheduleOne.NPCs.Relation.NPCRelationData.EUnlockType.DirectApproach, true);
// (or: customer.OnCustomerUnlocked(EUnlockType.DirectApproach, notify: true);)

// 8. make it order — server side only
customer.ForceDealOffer();
```

For "large", combine `data.Min/MaxWeeklySpend` with a Harmony postfix on
`Il2CppScheduleOne.Economy.Customer.TryGenerateContract` that rewrites the returned
`ContractInfo.Products` (`ProductList.entries` of `ProductList+Entry(productID, quality, quantity)`)
and `ContractInfo.Payment`, plus a postfix on
`Il2CppScheduleOne.Levelling.LevelManager.GetOrderLimitMultiplier` if you need to break the rank
ceiling.

---

## Open questions / unverified

**Numeric values (all IL-only — the metadata dumps carry names and types, never initialiser values):**

* Every `Customer` static: `MaxOrderQuantityPerProduct`, `QualityTierTolerance`,
  `AFFINITY_MAX_EFFECT`, `PROPERTY_MAX_EFFECT`, `QUALITY_MAX_EFFECT`,
  `DEAL_REJECTED_RELATIONSHIP_CHANGE`, `ATTACK_DEAL_COOLDOWN`,
  `RELATIONSHIP_THRESHOLD_TO_GIVE_DEAL_TO_CARTEL`, `CUSTOMER_UNLOCKED_CARTEL_INFLUENCE_CHANGE`,
  `APPROACH_MIN_ADDICTION`, `APPROACH_CHANCE_PER_DAY_MAX`, `APPROACH_MIN_COOLDOWN`,
  `APPROACH_MAX_COOLDOWN`, `DEAL_COOLDOWN`, `DEAL_ATTENDANCE_TOLERANCE`, `MIN_TRAVEL_TIME`,
  `MAX_TRAVEL_TIME`, `OFFER_EXPIRY_TIME_MINS`, `MIN_ORDER_APPEAL`, `ADDICTION_DRAIN_PER_DAY`,
  `SAMPLE_REQUIRES_RECOMMENDATION`, `MIN_NORMALIZED_RELATIONSHIP_FOR_RECOMMENDATION`,
  `RELATIONSHIP_FOR_GUARANTEED_DEALER_RECOMMENDATION`, `RELATIONSHIP_FOR_GUARANTEED_SUPPLIER_RECOMMENDATION`,
  and the contents of `PlayerAcceptMessages` / `PlayerRejectMessages`.
* Every `Dealer` static: `MAX_CUSTOMERS`, `DEAL_ARRIVAL_DELAY`, `MIN_TRAVEL_TIME`, `MAX_TRAVEL_TIME`,
  `OVERFLOW_SLOT_COUNT`, `CASH_REMINDER_THRESHOLD`, `RELATIONSHIP_CHANGE_PER_DEAL`,
  `NegativeQualityTolerance`, `PositiveQualityTolerance`.
* `ProductManager.MIN_PRICE` / `MAX_PRICE` / `CONTRACT_RECEIPT_MAX_COUNT`,
  `Contract.DefaultExpiryTime`, `Contract.ExcessProductsMatchSumMultiplier`,
  `DealWindowInfo.WINDOW_DURATION_MINS` / `WINDOW_COUNT` and the four window `StartTime`/`EndTime`
  pairs, `ProductQuantities.BagQuantity` / `JarQuantity` / `BrickQuantity`,
  `ATM.WeeklyDepositLimit`, `CashSlot.MAX_CASH_PER_SLOT`, `CartelDealer.PRODUCT_*`,
  `CartelDealManager.DEAL_DUE_TIME_DAYS` / `PAYMENT_MULTIPLIER` / `DEAL_COOLDOWN_HOURS`,
  `CartelActivities.MIN_COOLDOWN_HOURS` / `MAX_COOLDOWN_HOURS`,
  `CartelInfluence.INFLUENCE_TO_UNLOCK_NEXT_REGION` / `WESTVILLE_MAX_INFLUENCE`,
  `LevelManager.TIERS_PER_RANK` / `XP_PER_TIER_MIN` / `XP_PER_TIER_MAX`.
  → **Read all of these at runtime.** Some are cross-referenced in `research-ext/DESIGN-INTENT.md`
  from patch notes / wiki, but that is secondary evidence, not the assembly.

**Formulas:**

* Exact maths inside `CustomerData.GetAdjustedWeeklySpend`, `CustomerData.GetOrderDays`,
  `CustomerData.GetQualityScalar`, `Customer.GetProductEnjoyment`, `Customer.GetValueProposition`,
  `Customer.GetOfferSuccessChance`, `Customer.GetSampleSuccess`, `Customer.EvaluateDelivery`,
  `Customer.EvaluateCounteroffer`, `CustomerSatisfaction.GetRelationshipChange`,
  `ProductManager.CalculateProductValue`, `Contract.GetProductListMatch`,
  `LevelManager.GetOrderLimitMultiplier`, `DeadDrop.GetRandomEmptyDrop`.
* Whether the weighted pick in `GetWeightedRandomProduct` is linear-weight or softmax over appeal.
* Where exactly `Customer.MaxOrderQuantityPerProduct` and `LevelManager.GetOrderLimitMultiplier` are
  applied (inside `TryGenerateContract` vs `GetOrderableProductsWithQuantities`).

**Structural things I could not confirm:**

* **How many `Customer` components exist in a fresh save.** No constant, no manifest. Needs a runtime
  count of `Customer.LockedCustomers.Count + Customer.UnlockedCustomers.Count`, or a scene scan.
  (Literals `"Reach 10 customers ("` / `"Unlock 10 customers ("` are achievement text, not a cap.)
* **`Customer.SaveFileName` / `SaveFolderName` / `ShouldSaveUnderFolder` actual values.** The literals
  `Customer`, `Dealer`, `Supplier`, `NPCs` all exist, and the mapping is the obvious one, but I did
  not verify which literal each property returns. `Customer.Loader` is probably `null` (no
  `CustomerLoader` type exists) — unconfirmed.
* **Where `Customer.LockedCustomers` / `UnlockedCustomers` are actually mutated.** Inferred from
  `Awake` + `OnCustomerUnlocked`; not directly observable in the dumps.
* **Whether `Customer+CustomerPreference` and `Customer+ScheduleGroupPair` are still used.** No
  `Customer` member of either type appears. They look dead in v0.4.6.
* **Whether `Customer` (as opposed to `NPC`) is registered with `SaveManager.RegisterSaveable`, and
  when.** `Customer.InitializeSaveable()` exists but the registration site is IL-only.
* **Whether adding a `Customer` component at runtime to an NPC whose `NetworkObject` has already
  spawned actually replicates correctly.** FishNet normally requires the `NetworkBehaviour` set to be
  fixed at spawn time. `S1API.Entities.NPCCustomer.TryNetworkInitialize(Customer)` exists, which
  suggests it works in practice, but **I have not verified it and cannot without running the game.**
  This is the single biggest technical risk for the mod, and it needs a live multiplayer test.
* **`CartelActivity` / `CartelRegionActivities` subclassability from a mod.** They are scene
  `MonoBehaviour`/`NetworkBehaviour` components on prefabs; whether an Il2CppInterop-injected subclass
  can be registered and serialised is untested.
* **`Method_Private_Void_List_1_ItemInstance_Single_0` on `Dealer`** = `<TryRobDealer>g__SummariseLosses|98_0`
  is an inference from string-table ordering, not a proof.
* **`Il2CppScheduleOne.Delivery.*` relevance.** I documented it, but it is the *shop van delivery*
  system (`DeliveryInstance`, `LoadingDock`, `DeliveryVehicle`, `EDeliveryStatus`,
  `DeliveryManager.OnTimePass(int minutes)`), driven by `PhoneShopInterface` — it has **no**
  connection to customer deals beyond sharing the word "delivery". Customer deal locations are
  `Il2CppScheduleOne.Economy.DeliveryLocation` (a `MonoBehaviour` with `LocationName`,
  `LocationDescription`, `UnityEngine.Transform CustomerStandPoint`,
  `UnityEngine.Transform TeleportPoint`, `System.String StaticGUID`,
  `List<Il2CppScheduleOne.Quests.Contract> ScheduledContracts`, `Il2CppSystem.Guid GUID`,
  `GetDescription()`, `SetGUID(Guid)`), which is a different thing entirely. Also relevant:
  `Il2CppScheduleOne.Economy.OverrideCustomerDealLocation : UnityEngine.MonoBehaviour` with a single
  field `Il2CppScheduleOne.Economy.DeliveryLocation Location` — attach it to an NPC to pin where that
  customer meets you, which is directly useful for staging a group's arrival point.

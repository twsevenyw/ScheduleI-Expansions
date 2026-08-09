# PLAN — Special Customers (`special_customers`)

> Implementation plan for the `Expansions.SpecialCustomers` module.
> Feature source (verbatim, Community Vote #3 ballot 2026-04-17):
> *"Special customer groups (bikers, hippies, businessmen, etc.) who periodically visit town and buy
> large quantities of product."*
>
> Read alongside `research/API-ECONOMY.md` §1–5 + §10 + "Hooks & extension points",
> `research/API-NPCS.md` §6 / §14, `research/API-S1API.md` §3 / §5 / §7 / §23,
> `research-ext/DESIGN-INTENT.md` §5 + §9 + §14.3, `research-ext/CUSTOM-CHARACTERS.md` §1.9 + §4.3–4.5.
>
> Every game symbol below is quoted from those docs. Anything I could not verify is tagged
> **[UNVERIFIED]** and carries a runtime verification step in §10.

---

## 0. The five decisions this plan makes

| # | Decision | Why |
|---|---|---|
| **D1** | **8 compile-time `S1API.Entities.NPC` subclasses form a reusable visitor pool.** Archetype identity is applied at runtime by swapping `AvatarSettings` and rewriting `CustomerData` — not by shipping one NPC type per archetype. | 5 archetypes × 6 members = 30 permanent networked NPCs on top of the game's ~80. 8 is a 10% increase; 30 is 37%, and every one of them streams through `ReplicationQueue` on every client join. It is also *exactly* how the game does it: `GoonPool` = base `AvatarSettings` + a second `AvatarSettings` as clothing overlay + skin/hair/voice (`API-NPCS.md` §14.3). |
| **D2** | **`builder.EnsureCustomer()` at prefab time — never `AddComponent<Customer>()` at runtime.** | This kills the mod's single biggest risk outright. `research/FEASIBILITY.md` calls out "whether a `Customer` added *after* `NetworkObject` spawn actually replicates" as the #1 unknown. If `Customer` is on the prefab before S1API's `TrySpawnNetworkInstance()`, FishNet's `NetworkBehaviour` set is fixed at spawn time exactly as FishNet expects. The unknown disappears. |
| **D3** | **Hand-build every offer with `S1API.Economy.ContractInfoBuilder` → `NPCCustomer.OfferContract(info)`. Do not patch `Customer.TryGenerateContract`.** | Total control of quantity / payment / window / expiry with **zero** Harmony patches in the order pipeline, and `API-S1API.md` §23.2 shows S1API already owns most of the surrounding surface. The handover, payment, quality match, relationship, addiction, XP and receipt still run through the game's own code (§5). |
| **D4** | **Detection-and-self-disable is a first-class subsystem with its own state machine, defaulting to OFF on ambiguity.** | A false positive costs the player a mod feature. A false negative gives them two competing bulk-buyer systems mutating the same `Customer` objects. Asymmetric risk ⇒ fail closed (§6). |
| **D5** | **Ship 4 archetypes in v1 (Bikers, Businessmen, Hippies, Rock Band). Defer Party Bus. Invent nothing.** | Party Bus needs a parked vehicle + passenger staging; `S1API.Vehicles.LandVehicle` covers spawn/park/colour but not seating, and it is the one group that reads as broken without the art. Inventing a 6th group maximises the chance of colliding head-on with TVGS's v0.5.0 content — see `DESIGN-INTENT.md` §14.3: *"Do not invent extra groups for v1."* |

---

## 1. Design — what the player experiences

### 1.1 The beat

Every **2–3 in-game days** (config `visit_interval_days_min` / `_max`, default 2/3), one group arrives in
one **unlocked** region and stays for a single day window. One group at a time, ever.

| Time | Event |
|---|---|
| **07:00** (day start, `TimeManager.WakeTime` **[UNVERIFIED value]**) | Members are warped in and revealed, staggered. A text message lands on the player's phone from the group's **leader** — a real `MSGConversation` via `S1API.Entities.NPC.SendTextMessage(string, Response[], float, bool)`. Map POIs light up. |
| **Archetype order time** (e.g. Businessmen 14:00, Bikers 21:00) | The leader sends the bulk offer. Same shape as a shipped contract offer: phone message, Accept / Reject / Counter-offer, deal-window picker. |
| **Order window** | 2 in-game hours from the order time — **the shipped rule** (`DESIGN-INTENT.md` §9.2). Offer expires after `Customer.OFFER_EXPIRY_TIME_MINS` if untouched. |
| **Deal window** | The leader walks to the group's congregation point (a real `Il2CppScheduleOne.Economy.DeliveryLocation`) and waits, exactly like a shipped customer, driven by `CustomerAttendDealBehaviour`. |
| **Rolling** | While the group is in town, *non-leader* members can be approached directly in the world for smaller walk-up deals — `Customer.CanBeDirectlyApproached = true` + `RequestProductBehaviour`. This is the "offload as much as you can carry" pressure valve. |
| **04:00** (`TimeManager.EndOfDay` **[UNVERIFIED value]**) | Departure. Members walk toward the nearest `NPCEnterableBuilding` / warp point, then are hidden and parked. Any unfulfilled contract `Fail()`s, with a goodbye text. |

Cadence is deliberately the shipped customer rhythm: shipped customers order 1–7 times/week, median ~3,
i.e. every ~2.3 days (`DESIGN-INTENT.md` §14.3). This is what "a lot more 'rhythm' to the gameplay" means.

### 1.2 Finding them

Three channels, all shipped machinery:

1. **Phone message** naming the region — the game already has precedent for this exact string shape:
   the literal `"Hey boss, I've heard there's a Benzies deal happening in {0}, {1}. Might be worth checking out."`
2. **Map POI** — `Customer.SetupPoI()` / `Customer.SetPotentialCustomerPoIEnabled(bool)` gives each unlocked
   member the standard customer marker for free, using `NPCManager.PotentialCustomerPoIPrefab`. A single
   group marker on top via `S1API.Map.MapPOIBuilder` / `MapPOIManager`.
3. **They are physically standing there**, in a cluster, in clothes nobody else in Hyland Point wears.

### 1.3 Quantities, pricing and the margin haircut

Anchored to `DESIGN-INTENT.md` §14.3, which is itself anchored to the decompiled shipped formulas in §9.4.

| Lever | Value | Anchor |
|---|---|---|
| Group budget | **5 × the highest per-deal budget available at the player's current rank** | Rides the shipped budget curve (base × up to 10× by rank), so it can never desync after a patch |
| Order quantity | **40–80 units**, rounded to nearest 5 | Shipped rule: `if (quantity >= 14) quantity = RoundToInt(quantity / 5) * 5` |
| Hard clamp | **1,000** | Shipped `Mathf.Clamp(quantity, 1, 1000)` |
| Price ceiling | **`MarketValue × (enjoyment + 0.95) × 0.85`** | The shipped appeal formula with the "slightly lower profit margin" multiplier applied. At the community-standard 1.4× market list price this puts the group ceiling near **1.19× market** — a clean ~15% haircut |
| Max-spend guard | Reject anything ≥ **3 × average daily spend** | Shipped `maxSpend = dailyAverage * 3` |
| XP per group contract | **60** | Parity with the largest shipped non-quest award |
| Addiction / relationship | **None persisted** | They travel. `CustomerData.BaseAddiction = 0`, `DependenceMultiplier = 0`, relationship reset on departure |
| `CallPoliceChance` | **0** | Matches shipped Cranky Frank (0%) and dealer police-immunity. A bulk buyer who snitches is pure frustration |
| Dealer assignment | **Blocked** | Preserves the intent: this is the *fast manual* dump route. −20% dealer cut on top of −15% margin makes it pointless |

The margin haircut is expressed **through the game's own maths**, not faked: we set
`ContractInfo.Payment` to 0.85 × what the shipped formula would produce, and (optionally, §7) tighten
`Customer.EvaluateCounteroffer` so haggling back up gets rejected. The player experiences it as
"they pay less per unit but they'll take everything."

### 1.4 The feel target

Bulk convenience with a cost, on a predictable-but-not-clockwork beat. If the player has 300 bricks of
meth rotting in a storage rack, a biker gang rolling into Northtown on Thursday should feel like relief.
If they have nothing to sell, it should feel like a missed bus.

---

## 2. Archetypes

Preferred drug types: **Bikers → Meth** and **Businessmen → Cocaine** are *sourced* (2025 Trello card,
quoted by PC Gamer). Hippies and Rock Band are inference and are therefore **config-exposed**.

Appearance recipes come from `research-ext/CUSTOM-CHARACTERS.md` §4.3–4.5 and the verified shipped asset
catalogue in §1.9. All paths are `Resources.Load` paths harvested from the **S1API constant tables**
(`BodyLayerFields.*`, `FaceLayerFields.*`, `AccessoryFields.*`, `CustomizationFields.HairStyle`) — use the
constants, never hand-typed strings (hair folders are lowercase; the prose docs are wrong).

### 2.1 Bikers — *"The Ashfall MC"*

| | |
|---|---|
| **Region weight** | Northtown 3 · Docks 3 · Westville 2 · Downtown 1 |
| **Order time** | 21:00 (they deal at night — the player pays for it in curfew risk) |
| **Preferred drugs** | `EDrugType.Methamphetamine` affinity **+0.9**; `Marijuana` +0.3; everything else −0.5 |
| **Standards** | `ECustomerStandard.VeryLow` — this is what makes them the inventory sink |
| **Price sensitivity** | Harshest. Group multiplier **0.80** (below the 0.85 baseline) |
| **Quantity band** | 60–90 units (largest of the four) |
| **Base body** | `Gender` 0.0 · `Height` 1.0–1.1 · `Weight` **0.75–0.9** (bulk is the read) |
| **Face** | `Face.SmugPout` black · `FacialHair.Goatee` charcoal |
| **Hair** | `HairStyle.LongSlicked` or `HairStyle.Balding`, charcoal `#262626` |
| **Body layers** | `Shirts.UpperBodyTattoos` deep-blue `#00008B` · `LeftArmTattoos.Web` · `RightArmTattoos.Alien` · `Shirts.TShirt` black (omit on the shirtless variant) · `Pants.Jeans` dark-grey |
| **Accessories** | `Chest.OpenVest` charcoal · `Feet.CombatBoots` black · `Head.LegendSunglasses` black · `Waist.Belt` brown · `FacialHairAccessory.Chevron` charcoal (use *either* Chevron *or* the Goatee layer, not both) · optional `Neck.GoldChain` |
| **Voice** | `redneck` (pitch 0.90–0.95) |
| **Equippable** | `Avatar/Equippables/Beer` |
| **Behaviour** | Loose cluster, no seats. `Aggressiveness` 0.7. Highest chance of a member wandering. |
| **Dialogue flavour** | Blunt, transactional, mildly hostile. Leader text: *"Rolled into {region}. We're only here till sunup. You got crank or not?"* |
| **Known art gap** | No leather jacket / bandana / fingerless-glove **mesh**. `Accessories.FingerlessGloves` is a texture layer — use it as body layer 6. |

### 2.2 Businessmen — *"the Wexler Group"*

| | |
|---|---|
| **Region weight** | Uptown 4 · Downtown 3 · Suburbia 1 |
| **Order time** | 14:00 |
| **Preferred drugs** | `Cocaine` **+0.9**; `Methamphetamine` −0.8 (they will not touch it) |
| **Standards** | `ECustomerStandard.High` — they reject anything below `Premium` |
| **Price sensitivity** | Softest. Group multiplier **0.92** (they pay near-normal, but demand quality) |
| **Quantity band** | 40–60 units |
| **Base body** | `Gender` 0.0–1.0 · `Height` 1.0 · `Weight` 0.45–0.65 (soft, not lean) |
| **Face** | `Face.SlightSmile` black · `OldPersonWrinkles` tan |
| **Hair** | `HairStyle.Receding` / `HairStyle.SidePartBob`, dark-grey (greying = seniority) |
| **Body layers** | `Shirts.Buttonup` sky-blue or white · `Pants.CargoPants` dark-grey |
| **Accessories** | `Chest.Blazer` **navy / brown / dark-grey — never charcoal** (charcoal is the Police mod's federal agent) · `Feet.DressShoes` brown · `Hands.Polex` gold `#FFFF00` · `Waist.Belt` brown · `Head.RectangleFrameGlasses` dark-grey · female variant `Bottom.MediumSkirt` navy **replacing** the trousers layer |
| **Voice** | `monotone` (pitch 1.0) |
| **Equippable** | `Avatar/Equippables/Coffee` or `Phone_Raised` |
| **Behaviour** | Tight cluster, stationary, faces the player. `Aggressiveness` 0.1. Uses `SitSpec` if the congregation point has seats. |
| **Dialogue flavour** | Clipped, euphemistic, never says the word. Leader text: *"In town through tonight. We're in the market for premium inventory. Quality is non-negotiable."* |
| **Art gap** | No necktie. Live with it. |

### 2.3 Hippies — *"the Longhaul Caravan"*

| | |
|---|---|
| **Region weight** | Westville 3 · Northtown 2 · Docks 2 · Suburbia 1 |
| **Order time** | 11:00 |
| **Preferred drugs** | `Marijuana` **+0.8**, `Shrooms` **+0.8** (the only two-drug group) — **[inference, config-exposed]** |
| **Standards** | `ECustomerStandard.Low` |
| **Price sensitivity** | 0.85 baseline |
| **Quantity band** | 50–80 units, but split across **two** `ProductList+Entry` lines (weed + shrooms) — the only group that asks for a mixed order |
| **Base body** | `Gender` mix freely · `Height` 0.95 · `Weight` 0.3–0.5 |
| **Face** | `Face.SlightSmile` black · `TiredEyes` brown (the stoned read, and it is a shipped layer) · `Freckles` tan · male `FacialHair.Swirl` brown |
| **Hair** | `HairStyle.LongCurly` / `HairStyle.ShoulderLength` / `HairStyle.Afro`, brown |
| **Body layers** | `Shirts.VNeck` in **lime / orange / purple** — pick one *per member*; the group reads by being the only saturated people on the street · `LeftArmTattoos.Peace` deep-purple · `RightArmTattoos.Weed` dark-green · `Pants.Jorts` beige |
| **Accessories** | `Feet.Sandals` brown · `Head.SmallRoundGlasses` orange · `Neck.GoldChain` tan (reads as beads at this scale) · optional `Head.BucketHat` dark-green · female `Bottom.LongSkirt` deep-purple **replacing** the Jorts layer |
| **Voice** | `hippie` (pitch 1.0–1.1) |
| **Equippable** | `Avatar/Equippables/Joint` |
| **Behaviour** | Widest scatter radius, slowest walk (`Movement.AddSpeedControl("sc_hippie", 5, 0.8f)`), most idle wandering. `Aggressiveness` 0.0. |
| **Dialogue flavour** | Warm, rambling, uses "man". Leader text: *"Hey — caravan's parked up in {region} for the day. We're looking for green and caps, whatever you've got, man."* |
| **⚠ Known issue** | The wiki documents skirt clipping: *"Clipping issues. All shirts tuck. Belt not visible."* Test the female variant before shipping. |
| **Art gap** | No tie-dye, headband or poncho. Tie-dye is the obvious custom-PNG body layer if the mod ever ships assets. |

### 2.4 Rock Band — *"Static Sermon"*

| | |
|---|---|
| **Region weight** | Downtown 3 · Uptown 2 · Docks 2 |
| **Order time** | 23:00 (latest of the four) |
| **Preferred drugs** | `Cocaine` **+0.8**, `Shrooms` **+0.6** — **[inference, config-exposed]** |
| **Standards** | `ECustomerStandard.Moderate` |
| **Price sensitivity** | 0.88 — they are careless with money |
| **Quantity band** | 45–70 units |
| **Group size** | Smallest: **4** (a band is a band) |
| **Base body** | `Gender` mix · `Height` 0.95–1.05 · `Weight` 0.25–0.45 (lean) |
| **Face** | `Face.Agitated` or `Face.SmugPout` black · `Eyes.EyeShadow` black · `FaceTattoos.Teardrop` on exactly one member |
| **Hair** | `HairStyle.Mohawk` / `HairStyle.Spiky` / `HairStyle.LongCurly` — a different one per member, in saturated colours (crimson, deep-purple, white) |
| **Body layers** | `Shirts.TShirt` black · `Accessories.FingerlessGloves` black · `Pants.Jeans` black · `ChestTattoos.Sword` or `LeftArmTattoos.Heart` |
| **Accessories** | `Head.Oakleys` black · `Feet.CombatBoots` black · `Neck.GoldChain` light-grey (reads as a chain) · `Waist.Belt` black |
| **Voice** | `cold` / `tyler`, pitch 1.05 |
| **Equippable** | `Avatar/Equippables/BrokenBottle` or `Beer` |
| **Behaviour** | Members drift apart and re-cluster. Enable `builder.EnsureSmokeBreak()` and `EnsureDrinking()` at prefab time so idle time reads as backstage loitering. |
| **Dialogue flavour** | Grandiose, half-asleep. Leader text: *"Two nights off in {region}. We need enough to get through both. Don't make it complicated."* |

### 2.5 Party Bus — **deferred to v1.1**

Needs a parked bus at the congregation point plus 6 passengers staged around it.
`S1API.Vehicles.LandVehicle` covers spawn / park / colour / visibility / ownership / storage — but
**not seating or passenger occupancy** (`API-S1API.md` §11: `NPC.IsInVehicle` and `NPC.CurrentVehicle`
are read-only; `S1API.Avatar.Seat` is scene-registry only). Without the bus this is just "hippies with a
different palette", which is worse than not shipping it. Revisit after `DriveToCarParkSpec` is proven by
the Hireable Drivers module.

### 2.6 Archetype data shape

Archetypes are **static, code-defined records** — not JSON, not a data pack. Rationale: co-op peers must
agree byte-for-byte on the roster, and a data pack turns that into a support problem. Config exposes
*tuning* (toggles, multipliers, affinity overrides), not *structure*.

```csharp
sealed record Archetype(
    string   Id,                  // "bikers" | "businessmen" | "hippies" | "rock_band"
    string   DisplayName,         // "The Ashfall MC"
    int      DefaultMemberCount,  // 5 / 5 / 6 / 4
    int      OrderTime,           // HHMM, matches CustomerData.OrderTime
    ECustomerStandard Standards,
    (EDrugType Type, float Affinity)[] Affinities,
    float    PriceMultiplier,     // 0.80 .. 0.92
    (int Min, int Max) QuantityBand,
    (EMapRegion Region, int Weight)[] RegionWeights,
    string   VoiceId, float VoicePitch,
    string   EquippablePath,
    Func<int /*slotIndex*/, AvatarSettings> BuildLook,   // §4.3
    ArchetypeDialogue Dialogue);
```

---

## 3. Architecture

### 3.1 Where it hangs off `Expansions.Core`

`SpecialCustomersModule : ExpansionModule` (already scaffolded) is the only entry point. It uses exactly
the Core API that exists today: `Log`, `Config`, `Harmony`, `Lifetime`, `OnRegistered`, `OnEnabled`,
`OnSceneLoaded`.

```
SpecialCustomersModule : ExpansionModule
├── OnRegistered()                  // Config.Bind(...) only — runs once, before first enable
├── OnEnabled()                     // everything below; all teardown via Lifetime.OnDispose(...)
│   ├── OfficialFeatureDetector.Evaluate()   → abort + explain if positive (§6)
│   ├── VisitorPool.Bind()                    // resolve the 8 game NPCs from NPCManager
│   ├── ArchetypeCatalog.Build()              // cache AvatarSettings clones (§4.3)
│   ├── VisitScheduler.Start()                // subscribes S1API.GameTime.TimeManager
│   └── ContractPatches.Apply(Harmony)        // §7 — two small postfixes, both optional
├── OnDisabled()                    // stays empty (Core disposes Lifetime + UnpatchSelf)
└── OnSceneLoaded(1, "Main")        // re-bind; TimeManager/NPCManager are per-load
```

`OnUpdate` is deliberately **not overridden** in steady state. The only per-frame work is the arrival
stagger, which is a self-terminating state machine active for a few seconds per visit (§4.6).

### 3.2 Classes

| Class | Responsibility |
|---|---|
| `SpecialCustomersModule` | Lifecycle, config binding, wiring. No game logic. |
| `SpecialVisitor01` … `SpecialVisitor08` : `S1API.Entities.NPC` | The pool. Each overrides `ConfigurePrefab(NPCPrefabBuilder)` (identity, base look, impostor, `EnsureCustomer()`, `WithCustomerDefaults(...)`) and `OnCreated()` (park + hide). Bodies are near-identical; a shared `VisitorPrefab.Configure(builder, slotIndex)` static does the work so each type is ~8 lines. **They cannot share a base class** other than `NPC` itself — see §3.5. |
| `VisitorPool` | Maps slot index → `S1API.Entities.NPC` (via `NPC.Get<T>()`) **and** → `Il2CppScheduleOne.NPCs.NPC` (via `NPCManager.GetNPC(id)`, the public static). Owns `Reserve(count)` / `Release(slot)` / `ParkAll()`. |
| `ArchetypeCatalog` | The four `Archetype` records + a cache of `AvatarSettings` clones keyed `(archetypeId, slotIndex)`. |
| `VisitScheduler` | Countdown, region pick, archetype pick, arrival/departure triggers. Subscribes `S1API.GameTime.TimeManager.OnHourPass` / `OnDayPass` / `OnSleepEnd(int)`. |
| `Visit` | The live visit: archetype, region, `DeliveryLocation`, member slots, leader slot, arrival/departure `GameDateTime`, per-member contract state. |
| `VisitorDresser` | Applies a cached `AvatarSettings` to a live NPC (`npc.Avatar.LoadAvatarSettings(...)`), sets voice pitch and equippable, restores the neutral look on departure. |
| `CustomerTuner` | Writes/restores the live `Il2CppScheduleOne.Economy.CustomerData` fields per visit; snapshots the prefab baseline so departure is exactly reversible. |
| `OfferFactory` | Builds `S1API.Economy.ContractInfo` via `ContractInfoBuilder` — quantity, product pick, payment, window, expiry. The whole of §5.2. |
| `Congregation` | Picks the `DeliveryLocation`, computes stand positions in a ring around `CustomerStandPoint`, warps members, attaches `OverrideCustomerDealLocation`. |
| `ArrivalStagger` | Frame-budgeted reveal/hide sequencer (§4.6). The only `OnUpdate` consumer. |
| `OfficialFeatureDetector` | §6. Pure, side-effect-free, cached per session. |
| `SpecialCustomerState` : `S1API.Internal.Abstraction.Saveable` | §3.3. |
| `HostGate` | `static bool IsAuthority => InstanceFinder.NetworkManager == null \|\| InstanceFinder.IsServer;` — every world mutation goes through it. |

### 3.3 Persisted state

**One** direct `Saveable` inheritor (S1API's `IsDirectSaveableInheritor` only discovers one-level
hierarchies — `API-S1API.md` §7). It writes to `<save>/Modded/Saveables/special_customers.json`, which
vanilla never reads, so an orphaned file after uninstall is inert.

```csharp
public sealed class SpecialCustomerState : Saveable
{
    [SaveableField("special_customers")]
    private VisitState _state = new();

    public override SaveableLoadOrder LoadOrder => SaveableLoadOrder.AfterBaseGame;

    protected override void OnLoaded() { /* hand to VisitScheduler.Restore(_state) */ }
}

sealed class VisitState
{
    public int    SchemaVersion;            // bump on shape change; unknown-future ⇒ ignore + start clean
    public int    DaysUntilNextVisit;
    public int    LastVisitElapsedDay;
    public string LastArchetypeId;          // avoid repeating the same group twice running
    public bool   SelfDisabledByDetection;  // sticky (§6.5)
    public string SelfDisableReason;
    public ActiveVisit ActiveVisit;         // null when nobody is in town
}

sealed class ActiveVisit
{
    public string ArchetypeId;
    public int    Region;                   // (int)EMapRegion
    public string DeliveryLocationGuid;     // DeliveryLocation.StaticGUID / GUID
    public int[]  MemberSlots;
    public int    LeaderSlot;
    public int    ArrivalElapsedDay, ArrivalTime;
    public int    DepartureElapsedDay, DepartureTime;
    public bool   OfferSent;
    public bool   ContractAccepted;
}
```

Notice what is **not** persisted: contracts, relationships, addiction, customer unlock state, NPC
positions. All of those are the game's own save data (`Customer : ISaveable`, `Contract : Quest`,
`NPCRelationData`), and S1API persists the NPC wrappers themselves. We persist only the *group layer*.
That is what makes self-disable safe (§6.6).

### 3.4 Data flow, one visit

```
TimeManager.OnDayPass ──► VisitScheduler.Tick()
                            │  DaysUntilNextVisit-- ; ==0 && HostGate.IsAuthority && !detector.Positive
                            ▼
                          VisitScheduler.BeginVisit()
                            ├─ pick archetype (weighted, != LastArchetypeId)
                            ├─ pick region: Map.Instance.GetUnlockedRegions() ∩ archetype.RegionWeights
                            ├─ Congregation.Pick(region)  → MapRegionData.GetRandomUnscheduledDeliveryLocation()
                            ├─ VisitorPool.Reserve(memberCount)
                            ├─ for each slot: VisitorDresser.Apply(slot, archetype)
                            │                 CustomerTuner.Apply(slot, archetype)
                            │                 npc.Customer.Unlock()
                            ├─ Congregation.Place(slots, location)   // Warp BEFORE reveal
                            └─ ArrivalStagger.Begin(slots)           // reveal 1 member / N frames
                                     │
TimeManager.OnHourPass ──► VisitScheduler.Tick()
                            ├─ hour == archetype.OrderTime/100  → OfferFactory.SendGroupOffer(leader)
                            └─ hour == departure                → VisitScheduler.EndVisit()
                                                                    ├─ Contract.Fail() any open contract
                                                                    ├─ CustomerTuner.Restore(slot)
                                                                    ├─ VisitorDresser.Restore(slot)
                                                                    ├─ relationship reset, re-lock customer
                                                                    └─ ArrivalStagger.BeginDeparture(slots)
```

### 3.5 Why 8 separate classes and not one generic type

S1API discovers NPC prefabs by **scanning loaded assemblies for subclasses of `S1API.Entities.NPC`** and
derives the prefab name from the **simple type name** (`"S1API_" + Type.Name`), reconstructing client
wrappers by matching simple names across assemblies (`MODDING-ECOSYSTEM.md` §3, quoting Personnel).
There is one instance per discovered type. So "8 pool members" literally means "8 types".

Hard contract, verbatim from Personnel: *"S1API calls `ConfigurePrefab`, `IsDealer` and `IsPhysical` on
an **UNINITIALIZED** instance, so none of them may depend on constructor-set fields."* Therefore:

- `ConfigurePrefab` reads only `const`s and `static` lookups. The slot index must be a **compile-time
  constant per type**, not a constructor argument.
- `IsPhysical` / `IsDealer` likewise.

Naming: `SpecialVisitor01` … `SpecialVisitor08` — zero-padded so ordinal sort is numeric, globally unique,
session-stable. Ids: `expansions_sc_visitor_01` … `_08`.

**Co-op ordering.** Personnel sorts its emitted types by id because *"FishNet spawnable registration is
order-sensitive and co-op peers must agree."* We do not control S1API's scan order for compile-time types,
but every peer runs the same DLL with the same metadata, so the order is the same on every peer.
**[UNVERIFIED]** — verification step in §10.

---

## 4. Spawning & lifecycle

### 4.1 Creation — S1API owns it, we own nothing

There is no manual prefab clone, no `NPCManager.NPCRegistry.Add`, no `InstanceFinder.ServerManager.Spawn`
call in this mod. `S1API.Internal.Patches.NPCPatches` (hooking
`Il2CppScheduleOne.Persistence.Loaders.NPCsLoader.Load`) does the whole sequence:

1. `NPC.PreRegisterAllNpcPrefabs()` — scans assemblies, pre-registers prefabs as FishNet spawnables
   ("*should be called on both server and client before any NPC instances are spawned*").
2. Per type: construct, call our `ConfigurePrefab(builder)`, materialise the prefab under
   `@S1API_PersistentPrefabs`.
3. `NPCPatches.RebuildPendingCustomNpcTypes(bool)` — instantiate in save order.
4. `NPCPatches.InstantiateRemainingCustomNpcs(string)` — **this is why adding the mod to an existing save
   works.**
5. `NPC.TrySpawnNetworkInstance()` — server-side FishNet spawn; clients get
   `NPC.CreateWrapperForNetworkSpawnedNPC(...)`.
6. `NPC.CheckAndSetCustomNpcsReady()` flips `NPC.CustomNpcsReady`.

**Gate every piece of cross-NPC wiring on `NPC.CustomNpcsReady`.** `VisitorPool.Bind()` polls it from
`S1API.Lifecycle.GameLifecycle.OnLoadComplete` and retries for a bounded number of frames before logging
a hard error.

### 4.2 `ConfigurePrefab` — the persistent half

```csharp
protected override void ConfigurePrefab(NPCPrefabBuilder builder) =>
    VisitorPrefab.Configure(builder, SlotIndex);   // SlotIndex is a const on the subclass

// VisitorPrefab.Configure (shared static):
builder
    .WithIdentity($"expansions_sc_visitor_{slot:00}", NeutralFirstNames[slot], NeutralLastNames[slot])
    .WithRegion(S1API.Map.Region.Downtown)          // reassigned per visit at runtime
    .WithSpawnPosition(ParkPosition, Quaternion.identity)
    .WithVoice(BaseVoices[slot], 1f)
    .WithAppearanceDefaults(a => a
        .Set<Gender>(BaseGender[slot]).Set<Height>(BaseHeight[slot]).Set<Weight>(BaseWeight[slot])
        .Set<SkinColor>(BaseSkin[slot]).Set<HairStyle>(BaseHair[slot]).Set<HairColor>(BaseHairColor[slot])
        .WithBodyLayer<Shirts>(Shirts.TShirt, NeutralGrey)
        .WithBodyLayer<Pants>(Pants.Jeans, NeutralGrey)
        .WithAccessoryLayer<Feet>(Feet.Sneakers, NeutralGrey)
        .WithImpostor(ImpostorNames[slot]))         // MANDATORY — see below
    .EnsureCustomer()                               // ← D2: Customer exists before network spawn
    .WithCustomerDefaults(cd => cd
        .WithSpending(0f, 0f)                       // zero until a visit tunes it up
        .WithOrdersPerWeek(0, 0)                    // never orders on its own
        .WithStandards(CustomerStandard.VeryLow)
        .AllowDirectApproach(false)
        .WithCallPoliceChance(0f)
        .WithDependence(0f, 0f)
        .WithMutualRelationRequirement(0f, 0f));
```

Three things that will bite if skipped:

- **`WithImpostor` is not optional.** Personnel, verbatim: *"Vanilla enables the >50m billboard impostor
  unconditionally, and runtime-built `AvatarSettings` carry no impostor texture — without one, distant
  custom NPCs render an empty billboard."* Bake a deterministic impostor per slot
  (`WithImpostor(name)` or `WithRandomImpostor(seed, names)`). It will not match the archetype clothing at
  distance; that is an accepted cosmetic cost of D1.
- **`Appearance.Build()` in `OnCreated`** — *"required — skip it and the appearance is incomplete + no
  mugshot."*
- **A role-less NPC left "mutually known but locked" hits a vanilla NRE**; Personnel routes around it by
  unlocking the relationship so `ContactsDetailPanel` renders the real name instead of `"???"`. Our pool
  members carry a `Customer` component, so they are not role-less — but if any pool member is ever
  configured without `EnsureCustomer()`, unlock its relationship.

### 4.3 Appearance swap — clone-and-replace, not append

**Do not** call `npc.Appearance.WithBodyLayer(...).Build()` repeatedly at runtime. It is **[UNVERIFIED]**
whether `NPCAppearance.Build()` replaces or appends the layer lists; if it appends, re-skinning a pool
member four visits running blows the hard caps (6 face / 8 body / 9 accessories).

Instead, build one complete `AvatarSettings` per `(archetype, slot)` **once** at enable time and apply it
wholesale — which is precisely what `CartelGoon.ConfigureGoonSettings(conn, CartelGoonAppearance, float)`
does:

```csharp
// once, at enable:
var donor = gameNpc.NPCData.Appearance.AvatarSettings;     // has a valid ImpostorTexture already
var look  = UnityEngine.Object.Instantiate(donor);         // AvatarSettings : ScriptableObject
look.BodyLayerSettings.Clear();  look.FaceLayerSettings.Clear();  look.AccessorySettings.Clear();
// … push the archetype's LayerSetting { layerPath, layerTint } / AccessorySetting { path, color } …
look.HairPath = HairStyle.LongSlicked;  look.HairColor = Charcoal;
look.Weight   = 0.85f;  look.SkinColor = skin;
cache[(archetypeId, slot)] = look;

// per visit:
gameNpc.Avatar.LoadAvatarSettings(cache[(archetypeId, slot)]);
```

`Il2CppScheduleOne.NPCs.NPC.Avatar` is a public property (`API-NPCS.md` §2.3) and
`Il2CppScheduleOne.AvatarFramework.Avatar.LoadAvatarSettings(AvatarSettings)` is the shipped entry point
(§6.1). Reach the game NPC with the public static `NPCManager.GetNPC("expansions_sc_visitor_01")` —
S1API's own `S1NPC` escape hatch is `internal`, so never reflect into it.

Track every `Instantiate`d `AvatarSettings` with `Lifetime.Track(look)` so disable destroys them.

### 4.4 Where they arrive and congregate

```csharp
var region   = PickRegion(archetype);                        // ∩ Map.Instance.GetUnlockedRegions()
var data     = Map.Instance.GetRegionData(region);           // MapRegionData
var location = data.GetRandomUnscheduledDeliveryLocation();  // DeliveryLocation, nobody else is using it
```

`Il2CppScheduleOne.Economy.DeliveryLocation` carries `LocationName`, `LocationDescription`,
`Transform CustomerStandPoint`, `Transform TeleportPoint`, `string StaticGUID`, `Guid GUID`,
`List<Contract> ScheduledContracts`.

- Members stand in a ring around `CustomerStandPoint` (radius 1.8 m for Businessmen, 3.5 m for Hippies).
- **Pin their deals there**: attach `Il2CppScheduleOne.Economy.OverrideCustomerDealLocation` (a
  `MonoBehaviour` with a single field `DeliveryLocation Location`) to each member's GameObject. That is
  the shipped mechanism for "this customer always meets you here" and it removes any need to patch
  `Customer.GetDeliveryLocation()`.
- Set `npc.Region = (S1API.Map.Region)region` so `NPCManager.GetNPCsInRegion(region)` and the map both
  agree.

Fallback if a region has no free delivery location: `NPCManager.Instance.GetOrderedDistanceWarpPoints(origin)`
gives a distance-ordered list of the game's own NPC warp points.

### 4.5 Movement and idling

- Arrival placement uses `npc.Movement.Warp(position)` (S1API `NPCMovement.Warp`), never `SetDestination`
   — a walk-in from off-map crosses unloaded navmesh.
- Departure uses `Movement.SetDestination(exitPoint)` toward
  `GoonPool.GetNearestExitBuilding`-equivalent geometry — for us,
  `NPCManager.Instance.GetOrderedDistanceWarpPoints(clusterCentre)[0]` — then hide on arrival or after a
  90-second timeout, whichever is first.
- Idle behaviour comes free from the prefab schedule. Give each archetype a `WithSchedule(...)` block at
  prefab time using `S1API.Entities.Schedule` specs: `WalkToSpec` between two nearby points for Bikers and
  Hippies, `SitSpec` for Businessmen (**`durationMinutes` must be > 0 or the action never triggers**).
  Enable with `npc.Schedule.Enable()` on arrival, `npc.Schedule.Disable()` on departure.
  `Schedule.InitializeActions()` appears in S1API's quick-start doc but **could not be found on
  `NPCSchedule`** — treat it as **[UNVERIFIED]**; `Enable()` alone is verified.
- Per-archetype walk speed: `Movement.AddSpeedControl("sc_visit", 5, speed)` on arrival,
  `RemoveSpeedControl("sc_visit")` on departure.

### 4.6 Performance budget

The constraint is real: mods like **Snitch** and **Siesta** exist because simultaneous NPCs cost frames,
and `ReplicationQueue.RATE_LIMIT_BYTES_PER_SECOND` / `MAX_REPLICATION_DURATION` mean every persistent
networked object slows client joins (`API-NETWORKING-CONSOLE.md` §1.2, §1.5 rule 6).

| Budget item | Value | Reasoning |
|---|---|---|
| Persistent networked NPCs added | **8**, always, whether or not a group is in town | ~10% on top of the game's ~80. 30 (one type per archetype member) would be 37% and is rejected. |
| Simultaneously **visible** visitors | **≤ 6** (`max_group_size`, hard cap 8) | Below the 10-customer dealer book, so a group reads as "a crowd" not "a second district". |
| Concurrent groups | **1**, always | Config cannot raise this. Two groups doubles every cost and halves the specialness. |
| Reveal stagger | **1 member per 6 frames** (~0.1 s at 60 fps; full group visible in ~0.6 s) | Avatar layer compositing + accessory instantiation is the spike. Warping happens first, while still hidden, so no navmesh churn coincides with mesh work. |
| Appearance swap cost | Paid **once per enable**, not per visit | `AvatarSettings` clones are cached per `(archetype, slot)`. Per visit it is one `LoadAvatarSettings` call per member. |
| Per-frame cost in steady state | **Zero.** `OnUpdate` not overridden | All scheduling is `OnHourPass` / `OnDayPass`. `ArrivalStagger` subscribes to the frame pump only while active and unsubscribes itself. |
| Parked-state cost | `SetVisible(false, networked: true)` + warped to a park point outside player streaming range; `Schedule.Disable()`; `CustomerData.MinOrdersPerWeek = 0` | A hidden NPC still ticks `Customer.OnMinPass()`, so the customer must be tuned to no-op, not merely invisible. |

`Il2CppScheduleOne.NPCs.NPC.SetVisible(bool visible, bool networked = false)` is the shipped visibility
call. **Pass `networked: true`** so clients hide/show too. Note S1API's `SupplierRuntimePatches` has a
**postfix** on `NPC.SetVisible(bool, bool)` — a postfix cannot cancel us, so this is safe to call.

**Escape hatch for low-end machines:** `max_group_size` down to 3 and `stagger_frames` up to 20 both
reduce the spike without changing the design.

### 4.7 Departure

1. Cancel outstanding work: if `Visit.ContractAccepted && contract still open` → `Contract.Fail(network: true)`
   (or let it `Expire()` naturally if `expires` was set — preferred, because the game sends the
   `contract_expired` message for us).
2. `CustomerTuner.Restore(slot)` — write back the snapshotted prefab-baseline `CustomerData` values.
3. Re-lock: reset the relationship via `npc.Relationship.Add(-delta)` back to baseline. Do **not** try to
   delete `NPCRelationData` — leave the game's own save shape untouched.
4. `VisitorDresser.Restore(slot)` — `LoadAvatarSettings(neutralLook[slot])`.
5. `Schedule.Disable()`, `RemoveSpeedControl`, destroy the `OverrideCustomerDealLocation` component,
   `SetPotentialCustomerPoIEnabled(false)`.
6. `Movement.Warp(ParkPosition)`, `SetVisible(false, true)`.
7. `VisitorPool.Release(slot)`, `state.ActiveVisit = null`, roll `DaysUntilNextVisit`.

Every one of those steps is also registered with `Lifetime.OnDispose(...)` while a visit is live, so
disabling the module mid-visit runs the same unwind (§9).

---

## 5. The sale — everything through the game's own economy

Nothing about money, product, quality or relationship is simulated by the mod. The only thing we author is
the **offer**.

### 5.1 Why the offer is hand-built

`Customer.TryGenerateContract(Dealer)` would work, but shaping it to bulk needs a postfix on it *plus*
postfixes on `CustomerData.GetAdjustedWeeklySpend`, `Customer.GetWeightedRandomProduct` and
`LevelManager.GetOrderLimitMultiplier` — four patches on hot paths shared with every shipped customer.
`ContractInfoBuilder` gets the same result with none. That is D3.

### 5.2 Building the offer

```csharp
using S1API.Economy;   // ContractInfoBuilder, ContractInfo
using S1API.Products;  // ProductDefinition, Quality, DrugType

// 1. Candidate products = what the player has listed, filtered to the archetype's drug types.
//    ProductManager.ListedProducts is the pool the vanilla generator uses when dealer == null.
var candidates = ListedProductsOfType(archetype.Affinities);

// 2. Budget. Read the shipped curve rather than inventing one:
//    LevelManager.GetOrderLimitMultiplier(FullRank) / GetRankOrderLimitMultiplier(ERank)
//    scaled by the archetype's group multiplier (§1.3).
var budget = ShippedTopTierPerDealBudget() * config.GroupBudgetMultiplier.Value;   // default 5.0

// 3. Quantity from the shipped formula, then the shipped rounding.
int qty = Mathf.RoundToInt(budget / unitPrice);
qty = Mathf.Clamp(qty, archetype.QuantityBand.Min, archetype.QuantityBand.Max);
qty = Mathf.Clamp(qty, 1, 1000);                       // shipped hard clamp
if (qty >= 14) qty = Mathf.RoundToInt(qty / 5f) * 5;   // shipped rounding rule

// 4. Payment with the margin haircut.
float payment = unitPrice * qty * archetype.PriceMultiplier;   // 0.80 .. 0.92
payment = Mathf.RoundToInt(payment / 5f) * 5;                  // shipped rounding rule

var info = new ContractInfoBuilder()
    .AddProduct(product, qty, minQuality)              // Quality from archetype.Standards
    .WithPayment(payment)
    .WithDeliveryLocationByGuid(visit.DeliveryLocationGuid)
    .WithDeliveryWindow(archetype.OrderTime, AddHours(archetype.OrderTime, 4))
    .WithExpiration(true)
    .ExpiresAfter(config.OfferExpiryMinutes.Value)
    .Build();

leader.Customer.OfferContract(info);                   // S1API NPCCustomer.OfferContract → bool
```

Hippies add a second `AddProduct(...)` line for the mixed weed+shrooms order.

`minQuality` comes from the archetype's `ECustomerStandard` through the shipped
`StandardsMethod.GetCorrespondingQuality(ECustomerStandard)` — never a hard-coded `EQuality`.

### 5.3 What the game does from there — untouched

Straight out of `API-ECONOMY.md` §5a/§5b. Every one of these is shipped code we do not modify:

| Stage | Game call | Side |
|---|---|---|
| Offer lands on the phone | `Customer.OfferContract` → `SetOfferedContract` (ObserversRpc) → `NotifyPlayerOfContract(...)` → `MSGConversation.SendMessageChain(...)` → `SetUpResponseCallbacks()` | S → O → C |
| Player accepts + picks a window | `Customer.AcceptContractClicked()` → `DealWindowSelector.SetIsOpen(...)` → `PlayerAcceptedContract(EDealWindow)` → `SendContractAccepted(window, track)` | C → S |
| Contract materialises | `Customer.ContractAccepted(...)` → `QuestManager.ContractAccepted(customer, info, track, guid, dealer)` → `CreateContract_Local` + `CreateContract_Networked` → `Contract.InitializeContract(...)` → `Customer.AssignContract(...)` fires `onContractAssigned` | S → O |
| Customer attends | `Customer.UpdateDealAttendance()` → `CustomerAttendDealBehaviour.SetContract(contract)` → `EnsureNPCHasEnoughCash()` → walks to the `DeliveryLocation` → `Customer.IsAtDealLocation()` / `SetIsAwaitingDelivery(true)` | S |
| Handover | `Customer.IsReadyForHandover(...)` → `HandoverChosen()` → `HandoverScreen.Open(contract, customer, EMode.Contract, callback, successChanceMethod)` → `DonePressed()` → `Close(EHandoverOutcome.Finalize)` | C |
| Evaluation | `Customer.ProcessHandover(...)` → `EvaluateDelivery(contract, items, out highestAddiction, out mainType, out matchedProductCount, out qualityDifference)` → `ProcessHandoverServerSide(...)` | C → S |
| **Money** | `Contract.SubmitPayment(float bonusTotal)` → `MoneyManager.ChangeCashBalance(...)` | S |
| Close-out | `Contract.Complete(network: true)` → `Customer.CurrentContractEnded(EQuestState)` | S |
| Consequences | `Customer.ChangeAddiction(f)`, `Customer.AdjustAffinity(EDrugType, f)`, `CustomerSatisfaction.GetRelationshipChange(satisfaction)` → `NPCRelationData.ChangeRelationship(delta, network)` | S |
| Analytics | `ProductManager.RecordContractReceipt(conn, ContractReceipt)` | S → O |
| Popup | `NewCustomerPopup.PlayPopup(customer)` / `DealCompletedPopup` | C |

Real money, real quality matching (`Contract.DoesProductListMatchSpecified`,
`Contract.GetProductListMatch`), real XP, real receipt in the analytics app, real satisfaction-driven
payment bonuses, real rain bonus, real exceeded-quality bonus. **Nothing is faked.**

We then *undo* the transient parts on departure (§4.7): affinity drift and relationship gain are rolled
back so a group that visits ten times does not slowly become a maxed-out permanent customer, which would
contradict "periodically visit town".

### 5.4 Walk-up sales to non-leader members

`CustomerData.CanBeDirectlyApproached = true` while a member is in town enables the shipped
`RequestProductBehaviour` path: the customer walks up, asks, follows, and opens the handover
(`EMode.Offer`, success chance from `Customer.GetOfferSuccessChance(items, askingPrice)`). Also gives the
player `Customer.RequestProduct()` / `InstantDealOffered()` for free. Zero mod code — it is one
`CustomerData` field.

### 5.5 Explicitly blocked

`Dealer.AddCustomer(Customer)` / `AddCustomer_Server(string npcID)` must not be able to book a visitor.
The clean way is data, not a patch: our members are only `Unlock()`ed for the duration of a visit, and
`DealerManagementApp`'s `CustomerSelector` lists unlocked customers. A visitor picked up mid-visit is
released on departure by `NPCDealer.RemoveCustomer(npc)` during unwind. If playtesting shows this is
reachable and confusing, add the postfix listed in §7 as optional patch **P3**.

---

## 6. Official-feature detection & self-disable

TVGS's own Special Customers is in Trello "In Progress", earmarked v0.5.0, described as *"not far off now"*
in the v0.4.6 notes (2026-08-01). This module must notice and get out of the way.

### 6.1 Principles

1. **Version is the primary signal.** It is the only one we can reason about today without inventing API
   names.
2. **Structural probes are confirmatory, never primary.** They exist to catch a surprise (feature ships in
   0.4.7; feature ships under a name we guessed).
3. **Cheap: one evaluation per session**, at first gameplay-scene load, cached in a static. No per-frame,
   no per-day re-probing.
4. **Fail closed.** Ambiguous ⇒ disable.
5. **Never crash on a probe.** Every probe is individually try/caught and contributes 0 on failure.
6. **Self-disable is inert, not destructive.**

### 6.2 Probe A — game version (primary, weight 100)

The game stamps `GameVersion` into every persisted object
(`Il2CppScheduleOne.Persistence.Datas.SaveData.GameVersion`, observed as `"0.4.6f11"` in real saves), and
`Il2CppScheduleOne.Persistence.SaveManager` exposes the game's own parser:

```csharp
public static float SaveManager.GetVersionNumber(string version);
```

Use **the game's own comparator**, not our own string parsing:

```csharp
float running = SaveManager.GetVersionNumber(RunningVersionString());
float ceiling = SaveManager.GetVersionNumber(config.DisableAtGameVersion.Value);  // default "0.5.0"
if (running >= ceiling) => POSITIVE (weight 100)
```

`RunningVersionString()` resolution order, first non-empty wins:

1. `UnityEngine.Application.version` — **[UNVERIFIED]** whether TVGS populates it. Verify in §10-V1.
2. The `GameVersion` field of any freshly-created `SaveData` subclass instance (the game writes the
   *current* build version there, which is exactly what we want).
3. `LoadManager` / `SaveManager` save-info metadata for the loaded slot — note this is the version the
   save was *last written with*, which is the running version once the player saves, but stale on first
   load of an old save. **Lowest priority for that reason.**

If all three fail → **UNRESOLVED**, which under rule 4 is treated as positive unless
`detection_mode = always_on`.

### 6.3 Probe B — economy namespace shape (weight 40)

A one-shot scan of the game assembly, cached. Three sub-probes:

| Sub-probe | Baseline (v0.4.6) | Positive when |
|---|---|---|
| Type count in `Il2CppScheduleOne.Economy` | **20** top-level types (`ns-Il2CppScheduleOne.Economy.txt`) | count ≠ 20 |
| `Il2CppScheduleOne.Economy.CustomerData` public instance property count | **17** (16 data fields + `onChanged`) | count > 17 |
| `Il2CppScheduleOne.Economy.ECustomerStandard` member count | **5** | count > 5 |

Also worth a look, weight 10 each: `Il2CppScheduleOne.Product.EDrugType` member count (**6**), and
`Il2CppScheduleOne.Map.EMapRegion` member count (**6**).

A shape change alone is weak evidence — TVGS could add a `CustomerData` field for anything. That is why
this is 40, not 100.

### 6.4 Probe C — name heuristic (weight 60)

**These strings are guesses, not API names.** The probe is a substring scan over types that genuinely
exist in the loaded assembly; a miss proves nothing.

```csharp
static readonly string[] GroupNameFragments =
{
    "CustomerGroup", "SpecialCustomer", "TravellingCustomer", "TravelingCustomer",
    "VisitingCustomer", "CustomerVisit", "CustomerCrew", "CustomerParty", "CustomerConvoy"
};
```

Scan `AccessTools.GetTypesFromAssembly(<Assembly-CSharp interop assembly>)` once, match
`t.Namespace?.StartsWith("Il2CppScheduleOne") == true && fragments.Any(f => t.Name.Contains(f))`.

Two robustness requirements:

- **Wrap the enumeration in a `SafeTypeLoadPatch`-style guard.** `hdlmrell/OTC-S1-Mod` prefixes
  `AccessTools.GetTypesFromAssembly` precisely because a broken third-party assembly in the load set
  throws `ReflectionTypeLoadException`. Catch it and use `ex.Types.Where(t => t != null)`.
- **The fragment list lives in config** (`detection_name_fragments`, comma-separated) so a future TVGS name
  can be added by a user editing a text file, with no rebuild.

### 6.5 Scoring and the decision

```
score = A(0|100) + B(0..60) + C(0|60)

score >= 100                       → DISABLED_OFFICIAL     (near-certain)
50 <= score < 100                  → DISABLED_AMBIGUOUS    (fail closed, rule 4)
score < 50 and version resolved    → ENABLED
version UNRESOLVED                 → DISABLED_AMBIGUOUS
```

`detection_mode` (string: `auto` | `always_on` | `always_off`, default `auto`) overrides the whole thing.
`always_on` is documented as *"only use this if you have verified the official feature is absent."*

**Stickiness.** Once a save records `SelfDisabledByDetection = true` with a reason, the module stays off
for that save even if a later session scores lower (e.g. the player rolled back a game version). Clearing
it requires `detection_mode = always_on` or deleting
`<save>/Modded/Saveables/special_customers.json`. This prevents flip-flopping, which is the worst outcome
because it means a group half-arrives.

### 6.6 Disabling mid-playthrough without corrupting the save

The state model in §3.3 was designed for this. On a positive detection **while a visit is live**:

1. Run the full §4.7 departure unwind immediately, silently (no goodbye text — the player is about to be
   told why).
2. Write `SelfDisabledByDetection = true` + `SelfDisableReason`, clear `ActiveVisit`, keep
   `SchemaVersion`.
3. `ExpansionRegistry.SetEnabled("special_customers", false, persist: false)` — session-off, persisted
   toggle untouched, exactly like the Core error path.
4. Everything is now inert:
   - `<save>/Modded/Saveables/special_customers.json` is a file vanilla never opens.
   - The 8 pool NPCs remain registered by S1API and simply never visit. They are parked, hidden, with
     `MinOrdersPerWeek = 0` and `MaxWeeklySpend = 0`, so they generate nothing.
   - No shipped `Customer`, `CustomerData`, `Contract` or `NPCRelationData` object carries mod-authored
     values, because every per-visit write is snapshotted and restored.

**The one genuine risk is uninstalling the DLL entirely**, which orphans 8 NPC records in the game's NPC
save tree. Whether `NPCsLoader` tolerates an NPC folder with no matching registered NPC is
**[UNVERIFIED]** — §10-V6. Until that is verified, ship the safer instruction: *disable the module, do not
delete the DLL.* If verification shows vanilla is intolerant, add a `sc_retire` console command
(`S1API.Console.BaseConsoleCommand`) that runs unwind, marks the state retired, and prints the exact
folders to delete.

### 6.7 Telling the player

Three channels, all cheap, none requiring UI work:

1. **MelonLoader console banner** at load — one block, unmissable:
   ```
   ══ Special Customers: DISABLED ══
   Reason: game version 0.5.0f2 >= 0.5.0 (official Special Customers feature expected).
   Score 160 (version 100, namespace shape 40, name match 20: Il2CppScheduleOne.Economy.CustomerGroup).
   Your save is untouched. Set detection_mode = always_on in
   UserData/Expansions.cfg [SpecialCustomers_01_Main] to override.
   ```
2. **A read-only config entry** `detection_status` written on every evaluation. Because the module's
   category follows the `<ModName>_01_Main` convention, **ModsApp and Prowiler's Mod Manager phone app
   surface it in-game for free** — the player sees the reason without opening a log.
3. **`DisplayName` suffix.** `SpecialCustomersModule.DisplayName` returns
   `"Special Customers (disabled — official feature detected)"` when the detector is positive, so the F7
   toggle menu explains itself.

Note channel 3 requires `DisplayName` to become a computed property. `ExpansionModule.DisplayName` is
already `abstract` (not a field), so this is legal — but `ModuleContext` captures it at construction time
for the `enabled` entry's description only, so the live menu reads the current value. Confirm during
implementation.

---

## 7. Harmony patches

**The core loop requires zero patches.** That is the payoff of D2 + D3, and it matters because
`API-S1API.md` §23.2 shows the neighbourhood is crowded:

| Game method | S1API patch | Kind |
|---|---|---|
| `Economy.Customer.Awake` | `NPCPatches` | Prefix(**800**) **and** Postfix(**0**) |
| `NPCs.NPCInventory.Awake` | `NPCPatches` | Prefix(**800**) **and** Postfix(**0**) |
| `NPCs.NPC.Awake` | `NPCPatches` | Prefix(800) |
| `NPCs.NPC.GetSaveData` | `NPCPatches` | Prefix **and** Postfix |
| `NPCs.NPC.ShouldSave` | `NPCPatches` | Prefix |
| `NPCs.NPC.SetVisible(bool, bool)` | `SupplierRuntimePatches` | Postfix |
| `NPCManager.GetNPC` | `NPCPatches` | Postfix |
| `NPCManager.GetSaveString` | `NPCPatches` | Prefix |
| `Persistence.Loaders.NPCsLoader.Load` | `GenericSaveablesPatches` + `NPCPatches` ×2 | Postfix + Prefix(400) + Prefix(800) |
| `Economy.Dealer.Awake` | `NPCPatches` | Prefix(800) |

### 7.1 Hard prohibition list

Do not patch, under any circumstances: **`Customer.Awake`**, **`NPCInventory.Awake`**, `NPC.Awake`,
`NPC.GetSaveData`, `NPC.ShouldSave`, `NPCManager.GetNPC`, `NPCManager.GetSaveString`, `NPCsLoader.Load`,
`NPCLoader.Load`, `SaveManager.Save`, `LoadManager.QueueLoadRequest`.

For save/load timing use `S1API.Lifecycle.GameLifecycle.OnLoadComplete` / `OnSaveComplete` — S1API's own
doc says so, because `SaveManager.Save` already carries three patches and `LoadManager.QueueLoadRequest`
two.

### 7.2 The patches this module actually applies

All applied through the injected per-module instance (`ExpansionModule.Harmony`, id
`com.evan.expansions.special_customers`), never via `[HarmonyPatch]` — the mod assembly carries
`[assembly: HarmonyDontPatchAll]`, and Core's `UnpatchSelf()` on disable only reverses this instance.

| # | Target | Kind | Priority | S1API collision | Why |
|---|---|---|---|---|---|
| **P1** | `Il2CppScheduleOne.Economy.Customer.ShouldTryGenerateDeal()` | **Postfix** | default (400) | **None** — not in S1API's patch map | Force `__result = false` when `__instance` is one of our 8 pool members and no visit is active. Belt-and-braces: prevents a parked visitor generating a vanilla order if the "locked customers never generate" assumption is wrong (§10-V4). Two-line body, `__instance`-filtered by a `HashSet<int>` of instance ids. |
| **P2** | `Il2CppScheduleOne.Economy.Customer.EvaluateCounteroffer(ProductDefinition, int, float)` | **Postfix** | default | **None** | Config-gated (`enforce_margin_on_counteroffers`, default **on**). For our members only, reject counter-offers whose implied unit price exceeds `MarketValue × (enjoyment + 0.95) × archetype.PriceMultiplier`. This is the enforcement half of "slightly lower profit margin"; without it a player can haggle the haircut away. Instance method, so `__instance` gives us the filter. |
| **P3** | `Il2CppScheduleOne.Economy.Dealer.ShouldAcceptContract(ContractInfo, Customer)` | **Postfix** | default | **None** | *Optional, off by default.* Force `__result = false` when `customer` is one of ours, so a dealer can never take a group contract. Only enable if §5.5's data-level block proves insufficient in playtesting. |
| **P4** | `Il2CppScheduleOne.Economy.Customer.GetDeliveryLocation()` | **Postfix** | default | **None** | *Optional fallback, off by default.* Force the congregation location if `OverrideCustomerDealLocation` turns out not to be honoured (§10-V5). |

Concrete application form (priority must be passed on the `HarmonyMethod`, since we do not use attributes):

```csharp
var target = AccessTools.Method(
    typeof(Il2CppScheduleOne.Economy.Customer),
    nameof(Il2CppScheduleOne.Economy.Customer.ShouldTryGenerateDeal));

Harmony.Patch(target, postfix: new HarmonyMethod(
    typeof(CustomerPatches), nameof(CustomerPatches.ShouldTryGenerateDeal_Post)));
```

Wrap the whole patch block in try/catch and log a warning rather than letting a failed patch take the
module down — Personnel's rule, verbatim: *"A failed patch costs the console bridge, not the library, so
the roster still loads either way."* If P1 fails to apply, fall back to the data-level guard
(`MinOrdersPerWeek = 0`) and log a warning.

### 7.3 If a future change forces a `Customer.Awake` patch

Only ever a **Postfix**, and it must run after S1API's Postfix(0). Harmony sorts postfixes by descending
priority, so pass an explicitly lower integer:

```csharp
new HarmonyMethod(m) { priority = -1 }   // below Priority.Last (0)
```

**[UNVERIFIED]** whether HarmonyX 2.10.2 accepts priorities below `Priority.Last`. The portable
alternative is `new HarmonyMethod(m) { after = new[] { s1apiHarmonyId } }`, resolving `s1apiHarmonyId` at
runtime from `Harmony.GetPatchInfo(target).Postfixes` rather than hard-coding it. Prefer the `after`
form — it is explicit and version-proof. §10-V8.

### 7.4 Patch timing

Apply patches on the **first gameplay scene**, never at mod load. Community-standard, and PropHunt
documents the failure: *"Patching the game's gameplay methods while the Side Hustle hub builds its menu UI
intermittently hard-crashes the game."* Concretely: `OnSceneLoaded(1, "Main")` → latch → patch inside
try/catch.

---

## 8. Config

Category `SpecialCustomers_01_Main` (from `ExpansionConfig.CategoryForId("special_customers")`), file
`UserData/Expansions.cfg`. All bindings declared in `OnRegistered()`, not `OnEnabled()` — `OnEnabled` runs
again on every toggle. Types are restricted to `bool` / `int` / `float` / `string` so MelonPreferences +
Tomlet round-trip cleanly; enum-shaped settings are strings that we parse.

| Key | Type | Default | Notes |
|---|---|---|---|
| `enabled` | bool | `true` | Auto-created by `ModuleContext` — do not re-declare |
| **Cadence** ||||
| `visit_interval_days_min` | int | `2` | Shipped customer median order gap ≈ 2.3 days |
| `visit_interval_days_max` | int | `3` | |
| `arrival_time` | int | `700` | HHMM. Day start |
| `departure_time` | int | `400` | HHMM. The 4:00 AM time pause — no new despawn logic needed |
| **Group size** ||||
| `max_group_size` | int | `6` | Clamped 1–8. Hard cap is the pool size |
| `group_size_jitter` | int | `1` | Actual size = archetype default ± jitter, clamped |
| **Economy** ||||
| `group_budget_multiplier` | float | `5.0` | × the top per-deal budget at the player's rank |
| `quantity_min` | int | `40` | Clamped 1–1000 (shipped clamp) |
| `quantity_max` | int | `80` | |
| `global_price_multiplier` | float | `1.0` | Multiplies each archetype's own 0.80–0.92 |
| `offer_expiry_minutes` | int | `120` | Matches the shipped 2-hour order window |
| `enforce_margin_on_counteroffers` | bool | `true` | Patch **P2** |
| `block_dealer_assignment` | bool | `false` | Patch **P3** |
| `force_delivery_location` | bool | `false` | Patch **P4** |
| **Archetypes** ||||
| `archetype_bikers_enabled` | bool | `true` | |
| `archetype_businessmen_enabled` | bool | `true` | |
| `archetype_hippies_enabled` | bool | `true` | |
| `archetype_rock_band_enabled` | bool | `true` | |
| `archetype_hippies_drugs` | string | `"Marijuana,Shrooms"` | Inferred mapping — exposed because it is not sourced |
| `archetype_rock_band_drugs` | string | `"Cocaine,Shrooms"` | Same |
| **Detection** ||||
| `detection_mode` | string | `"auto"` | `auto` / `always_on` / `always_off` |
| `disable_at_game_version` | string | `"0.5.0"` | Compared with `SaveManager.GetVersionNumber` |
| `detection_name_fragments` | string | `"CustomerGroup,SpecialCustomer,TravellingCustomer,TravelingCustomer,VisitingCustomer,CustomerVisit,CustomerCrew,CustomerParty,CustomerConvoy"` | Editable without a rebuild |
| `detection_status` | string | `""` | **Written by the mod**, read by the player / ModsApp |
| **Performance** ||||
| `stagger_frames` | int | `6` | Frames between member reveals |
| **Diagnostics** ||||
| `debug_force_visit_now` | bool | `false` | Self-resetting trigger: on change → true, start a visit immediately, write back false |
| `debug_archetype_override` | string | `""` | Force a specific archetype for the next visit |

Use `ConfigValue<T>.Changed` for the live-reacting ones (`max_group_size`, `detection_mode`,
`debug_force_visit_now`); it fires on external file edits too, which is what makes ModsApp hot-reload work.

---

## 9. Edge cases

| Case | Behaviour | Mechanism |
|---|---|---|
| **Save with a group in town** | Visit survives. On load, re-dress, re-warp, re-tune, re-attach `OverrideCustomerDealLocation`, re-enable POIs; contract state comes back from the game's own save (`Customer : ISaveable`, `Contract : Quest`) | `SpecialCustomerState.OnLoaded()` → `VisitScheduler.Restore()`, gated on `NPC.CustomNpcsReady` |
| **Load an old save made before the mod** | `ActiveVisit == null`, `DaysUntilNextVisit` seeded randomly in `[min, max]`. S1API's `InstantiateRemainingCustomNpcs` handles the 8 new NPCs | Nothing to do |
| **Load a save made with a newer `SchemaVersion`** | Ignore the payload, start clean, log a warning. Never partially deserialise | `SchemaVersion` check first thing in `OnLoaded` |
| **Sleep / time-skip across the departure time** | Departure runs immediately on wake, before anything else | `S1API.GameTime.TimeManager.OnSleepEnd(int minutesSkipped)` — S1API fuses the game's `onSleepEnd` with `onTimeSkip` so the skipped-minutes arg is real |
| **Sleep across an arrival** | Arrival is **deferred**, not skipped, and never fires while `TimeManager.IsSleepInProgress`. It runs on the first `OnHourPass` after wake, and if that is past the departure time the whole visit is cancelled and rescheduled | Explicit `IsSleepInProgress` guard in `VisitScheduler.Tick()` |
| **Player is far away at arrival** | Fine, and preferred. Reveal is unconditional but staggered | — |
| **Player is standing on the congregation point at arrival** | Defer reveal by 3 seconds and warp the ring 4 m further out, to avoid pop-in inside the player's face | `S1API.Entities.Player.All` positions vs. `CustomerStandPoint`, ≤ 8 m ⇒ defer |
| **Curfew (21:00 / hard 21:15)** | The group stays. `CallPoliceChance = 0` so they never snitch. Late-order archetypes (Bikers 21:00, Rock Band 23:00) deliberately push the player into curfew risk — that is the design tension | `S1API.Law.CurfewManager.IsCurrentlyActive` is read for *messaging* only ("we're here till sunup"), not behaviour |
| **Police interact with a member** | Normal NPC handling. No special casing. A visitor who witnesses a crime uses the shipped response chain minus `CallPoliceBehaviour`, because `CallPoliceChance = 0` | Shipped |
| **Member knocked out / killed** | Drop them from the visit, `Contract.Fail()` if they held one, exclude from the departure walk. Revive off-screen at departure via `npc.Revive()` (S1API `IHealth`) and return to pool | `npc.IsDead` / `!npc.IsConscious` polled at each `OnHourPass` and at departure |
| **Member arrested** | Same path as killed. The game removes them; on departure we `Revive()` and warp back to park | `npc.IsKnockedOut` / absence from `NPCManager.NPCRegistry` region query |
| **All members dead before the deal** | Cancel the visit, roll the next countdown at half interval ("they didn't stick around") | `VisitScheduler.EndVisit(reason: MembersLost)` |
| **Player accepts, then never shows** | Shipped expiry runs. `Contract.Expire()` → `contract_expired` message, `Customer.CurrentContractEnded(EQuestState)`. Our departure unwind sees no open contract | Shipped |
| **Two groups would overlap** | Impossible by construction — `VisitScheduler` refuses to begin a visit while `ActiveVisit != null`. Countdown is not consumed | Invariant assert + log |
| **Region locked mid-visit** | Cannot happen (regions only unlock). If `GetUnlockedRegions()` returns nothing valid for the archetype, skip that archetype; if none qualify, defer the visit one day | Pick-time filter |
| **Module disabled while a group is present** | Full §4.7 unwind, then teardown. Registered up-front with `Lifetime.OnDispose(...)` at `BeginVisit` time, so it runs even if disable comes from Core's error path | `ModuleLifetime` disposes in reverse order and one failing action never blocks the rest |
| **Module re-enabled later in the same session** | Fresh `Lifetime` and fresh `Harmony` from `ModuleContext.BeginCycle()`. Pool re-bound from `NPCManager`, state re-read, countdown resumes | Core |
| **Module auto-disabled after 10 frame-hook exceptions** | Same as manual disable; persisted toggle stays on so it retries next launch | `ModuleContext.ReportPhaseError` |
| **Multiplayer — host** | Normal operation. All world mutation behind `HostGate.IsAuthority` | `InstanceFinder.NetworkManager == null \|\| InstanceFinder.IsServer` |
| **Multiplayer — client** | Read-only. The 8 NPCs and every deal arrive replicated from the host. The client's scheduler never fires; its detector still runs (so it can warn) | Same gate |
| **Multiplayer — one peer lacks the mod** | Prefab registration diverges and the client will not resolve our spawnables. Detect on join by publishing a config+version hash to Steam lobby data and comparing; on mismatch, log a loud host-side warning and keep running host-only | `Lobby.SetLobbyData` / `GetLobbyData("expansions_sc_hash")`; `Lobby.IsInLobby`, `PlayerCount` |
| **Multiplayer — peers have different archetype config** | Same hash check. Config only affects host-side decisions, so a mismatch is cosmetic, but warn anyway | Same |
| **Client joins mid-visit** | The visitors are already network-spawned and visible; `ReplicationQueue` streams them like any NPC. No mod action | Shipped |
| **Official feature detected mid-session** | Cannot happen — detection is per-session and the game version cannot change mid-session. Detected *at load* ⇒ §6.6 | — |

---

## 10. Risks & unknowns — and exactly how to verify each

Ordered by how much it hurts to be wrong. Every verification is a single throwaway build plus a console
line; none needs a second machine except V7.

| # | Unknown | Verification |
|---|---|---|
| **V1** | **What returns the running game version string.** The whole detection design hangs off it. `UnityEngine.Application.version` is unconfirmed for this build; the `"0.4.6f11"` shape is only observed inside save JSON | Log all three candidates at first gameplay load: `Application.version`; the `GameVersion` field of a freshly constructed `SaveData` subclass; `SaveManager` / `LoadManager` save-info metadata. Then log `SaveManager.GetVersionNumber(x)` for each. Pick the first that yields a monotonic float matching `0.4.6f11`. **Do this before writing any other detection code.** |
| **V2** | **Does `NPCAppearance.Build()` replace or append the layer lists?** If append, repeated re-skins overflow 6/8/9 | Call `npc.Appearance.WithBodyLayer<Shirts>(Shirts.TShirt, Color.red).Build()` three times, then log `npc.NPCData.Appearance.AvatarSettings.BodyLayerSettings.Count`. 1 ⇒ replace, 3 ⇒ append. §4.3 already routes around this via `Avatar.LoadAvatarSettings`, so this only decides whether the S1API path is *also* available |
| **V3** | **Does `Avatar.LoadAvatarSettings(clonedSettings)` fully re-skin a live, already-spawned NPC** (layers, accessories, hair, shape keys) — and does it replicate to clients? | Clone a shipped NPC's `AvatarSettings` with `Object.Instantiate`, swap `BodyLayerSettings` for the biker recipe, call `LoadAvatarSettings` on a live NPC, and eyeball it. Then repeat with a second client attached. If it does not replicate, appearance becomes host-local cosmetic — acceptable degradation, but must be stated in the mod description |
| **V4** | **Does a *locked* `Customer` with `MinOrdersPerWeek = 0` ever generate a deal?** Determines whether patch **P1** is required or merely defensive | Park a pool member locked with zeroed data, run 3 in-game days at `settimescale 10`, and watch `Customer.OfferedDeals` and the phone. Also log `ShouldTryGenerateDeal()` each hour |
| **V5** | **Is `OverrideCustomerDealLocation` actually honoured** by `Customer.GetDeliveryLocation()` / `CustomerAttendDealBehaviour`? | Attach it to a shipped customer pointing at a known `DeliveryLocation`, force an offer with `Customer.ForceDealOffer()`, accept, and log `Customer.GetDeliveryLocation().LocationName` plus where the NPC actually walks. If it is ignored, enable patch **P4** |
| **V6** | **Does vanilla `NPCsLoader` tolerate orphaned NPC save folders** after the mod DLL is removed? Decides whether "uninstall" is a supported operation | Back up a save. Install, play, save (8 visitor folders written). Remove the DLL. Load. If the save loads clean, uninstall is supported; if it throws or drops NPCs, ship the `sc_retire` command and document "disable, don't delete" |
| **V7** | **Do compile-time `S1API.Entities.NPC` subclasses register in the same FishNet spawnable order on every peer?** Personnel sorts its *emitted* types for exactly this reason | Two-client test. Log the ordered list of `(prefabName, index)` S1API produces on host and client and diff. If it diverges, adopt Personnel's Pattern B: emit the 8 types into a `Reflection.Emit` assembly during `OnInitializeMelon` sorted by `string.CompareOrdinal(a.Id, b.Id)` |
| **V8** | **Does HarmonyX 2.10.2 accept `HarmonyMethod.priority` below `Priority.Last` (0)?** Only matters if §7.3 is ever needed | Patch any harmless method with `priority = -1` alongside a second postfix at 0 and log the execution order. Prefer the `after = new[]{ ownerId }` form regardless |
| **V9** | **Runtime values of every `Customer` static.** `MaxOrderQuantityPerProduct`, `QualityTierTolerance`, `MIN_ORDER_APPEAL`, `DEAL_COOLDOWN`, `OFFER_EXPIRY_TIME_MINS`, `DEAL_ATTENDANCE_TOLERANCE`, `MIN_TRAVEL_TIME`/`MAX_TRAVEL_TIME`, `AFFINITY_MAX_EFFECT`, `PROPERTY_MAX_EFFECT`, `QUALITY_MAX_EFFECT`, `ADDICTION_DRAIN_PER_DAY` — all IL-only, none in the metadata dumps | One log line each at first gameplay load. Also dump `TimeManager.EndOfDay`, `WakeTime`, `CycleDuration`, `TickDuration`, and `DealWindowInfo.WINDOW_DURATION_MINS` / `WINDOW_COUNT` and the four window `StartTime`/`EndTime` pairs. **Bake nothing; read them and derive.** Do this in the same milestone as V1 |
| **V10** | **Is a hand-built `ContractInfo` re-clamped downstream** by `Customer.MaxOrderQuantityPerProduct` or `LevelManager.GetOrderLimitMultiplier`? `API-ECONOMY.md` explicitly lists "where exactly they are applied" as unverified | Offer a 200-unit contract and log `Contract.ProductList.GetTotalQuantity()` after acceptance. If clamped, either split across multiple `ProductList+Entry` lines or raise the static for the duration of offer construction and restore in a `finally` |
| **V11** | **Does `S1API.Entities.NPCCustomer.OfferContract(ContractInfo)` reach the server path** (`Customer.OfferContract` is `[S]`), or does it need to be called from the host explicitly? | Log the return `bool` and whether the phone message arrives, on host and on a client. Guard the call with `HostGate.IsAuthority` regardless |
| **V12** | **How many `Customer` components exist in a fresh save.** Needed to size the "8 extra is ~10%" claim honestly | `Customer.LockedCustomers.Count + Customer.UnlockedCustomers.Count` and `NPCManager.NPCRegistry.Count` at load |
| **V13** | **`Schedule.InitializeActions()`** appears in the S1API quick-start but was not found on `NPCSchedule` | Try/catch it; fall back to `Schedule.Enable()` alone, which is verified |
| **V14** | Female skirt variants have a documented clipping bug (*"All shirts tuck. Belt not visible."*) | Visual check on the Hippie and Businessman female variants before shipping. If bad, drop the skirt accessory and use the trousers layer for everyone |
| **V15** | S1API 3.1.4 is installed; 3.1.7 restores civilian greetings/generic dialogue and residence-door summons for custom NPCs | Upgrade before writing NPC code, per the standing `CONTEXT.md` decision. The NuGet reference and the DLL in `Mods\` must move together |

---

## 11. Implementation order

Riskiest first, each milestone independently testable and independently revertable. **Nothing after M2 is
worth writing until M1 and M2 are green.**

### M0 — Runtime facts (half a day, no feature code)

A throwaway diagnostic path in `OnSceneLoaded(1, "Main")` that logs **V1**, **V9** and **V12**, plus the
appearance-asset corpus:

```csharp
foreach (var npc in NPCManager.NPCRegistry)
    Log.Msg(npc.ID + " => " + npc.NPCData.Appearance.AvatarSettings.GetJson(false));
```

That last line is the highest-value single move in the whole plan: it yields every asset path the game
actually uses, tagged by character, which is what turns the §2 recipes from "should work" into "verified
against shipped data".
**Exit criteria:** a version string that `SaveManager.GetVersionNumber` parses; every `Customer` static
logged; the asset corpus written to disk.

### M1 — One custom NPC, visible, standing in the world

`SpecialVisitor01` only. `ConfigurePrefab` with identity, base appearance, **impostor**, spawn position;
`OnCreated` with `Appearance.Build()`. No customer, no groups, no scheduler.
**Exit criteria:** the NPC is standing in Hyland Point, has a name and a mugshot, is *not* a blank
billboard at 60 m, survives save→quit→load, and appears in an existing save that predates the mod.
This is the "get one custom NPC visibly standing in the world" milestone and it is where every S1API
assumption gets tested at once.

### M2 — That NPC is a real customer you can sell to

Add `EnsureCustomer()` + `WithCustomerDefaults(...)` to the prefab. At runtime: `Customer.Unlock()`, then a
hand-built `ContractInfoBuilder` offer via `NPCCustomer.OfferContract(info)`, triggered by a temporary
console command. Verifies **V10** and **V11**.
**Exit criteria:** phone offer arrives → accept → the NPC walks to a delivery location → handover screen
opens → money lands via `MoneyManager` → the receipt shows in the analytics app. **D2 and D3 are now
proven or dead.** If `OfferContract` does not work, fall back to `Customer.ForceDealOffer()` plus a
`TryGenerateContract` postfix and re-plan §5.

### M3 — Archetype dressing

`ArchetypeCatalog` + `VisitorDresser`. Build the Biker `AvatarSettings` clone and apply it with
`Avatar.LoadAvatarSettings`. Verifies **V2** and **V3**.
**Exit criteria:** `SpecialVisitor01` looks like a biker, can be re-dressed as a businessman and back
without layer-count drift, and the neutral look restores exactly.

### M4 — The visit lifecycle, single member

`VisitScheduler` + `Congregation` + `CustomerTuner`, driven by `S1API.GameTime.TimeManager.OnHourPass` /
`OnDayPass`. One member arrives at 07:00, gets tuned, offers at the archetype order time, departs at
04:00. Verifies **V4** and **V5**; patch **P1** lands here if V4 says so.
**Exit criteria:** a full arrive → offer → sell → depart cycle at `settimescale 10`, with the customer
provably inert before arrival and after departure.

### M5 — Persistence

`SpecialCustomerState` + restore path. **Exit criteria:** save mid-visit, quit to menu, reload — the group
is still there, still dressed, still holding its contract; and saving with no visit active reloads clean.
Also test `SchemaVersion` forward-compat by hand-editing the JSON.

### M6 — The pool and the group

Slots 02–08. `VisitorPool`, `ArrivalStagger`, ring placement, the leader concept, walk-up sales on
non-leaders. Verifies the §4.6 performance budget.
**Exit criteria:** a 6-member group arrives with no visible frame hitch (measure with the stagger at 6
frames and at 1 frame to confirm the stagger is what is doing the work), all six are sellable, all six
leave.

### M7 — All four archetypes + region weighting + dialogue

The remaining three recipes, `RegionWeights` against `Map.Instance.GetUnlockedRegions()`, per-archetype
leader texts via `SendTextMessage`, map POIs. Verifies **V14**.
**Exit criteria:** four visually distinct groups, each in a plausible region, each with its own voice and
flavour text.

### M8 — Detection and self-disable

`OfficialFeatureDetector`, the scoring model, sticky state, the three player-facing channels. Test by
forcing `disable_at_game_version = "0.4.0"` so the version probe fires on the current build, and by adding
a real existing type name to `detection_name_fragments` so the name probe fires.
**Exit criteria:** with detection forced positive mid-visit, the group unwinds cleanly, the save reloads
with vanilla behaviour, the reason is visible in the log *and* in the config *and* in the toggle menu, and
re-enabling with `always_on` restores normal operation.

### M9 — Config surface and balance pass

Every key in §8 wired, live-reacting where §8 says so. Then play three in-game weeks and tune
`group_budget_multiplier`, the quantity band and the price multipliers against the §1.3 targets.

### M10 — Multiplayer

Two-client test. Verifies **V7** and the client-side read-only posture. Lobby hash check.
**Exit criteria:** host runs visits, client sees them, client's scheduler provably never fires, join
mid-visit works, and the peer-mismatch warning fires when the client's DLL is removed.

### M11 — Uninstall safety

Verifies **V6**. Ship either "uninstall is supported" or the `sc_retire` console command plus an explicit
README warning. **Do not release before this is answered** — it is the difference between a mod that can
be removed and one that traps a save.

---

## Appendix A — the console commands worth knowing during development

From `research/API-NETWORKING-CONSOLE.md` and `S1API.Console.ConsoleHelper`:

| Command | Use |
|---|---|
| `settimescale 10` | Run a full visit cycle in ~2 real minutes |
| `settime 2100` | Jump straight to a biker order window |
| `setdayduration 24` | Restore the default day length after testing |
| `forcesleep` | Exercise the sleep / time-skip edge cases (§9) |
| `teleport <npcid>` | Get to a visitor fast |
| `setrelationship` / `setunlocked` | Cross-check that departure really restored the baseline |

## Appendix B — the one-paragraph summary for the README

> Special customer groups — bikers, businessmen, hippies and a rock band — roll into Hyland Point every
> couple of days, park themselves in one district for a day, and buy in bulk. They pay a little under
> market for the convenience. Everything runs through the game's own contract, handover and payment
> systems, so the money, XP, quality matching and analytics are real. **If Schedule I v0.5.0 ships its own
> Special Customers feature, this mod detects it at launch and switches itself off without touching your
> save.**

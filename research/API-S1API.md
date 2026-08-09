# S1API — Public API Surface

> **Purpose**: what the S1API modding library already gives you, so the Hireable Drivers /
> Police Improvements / Special Customers teams know **what NOT to rewrite** — and where the wall is.
>
> Sources, in order of authority:
> 1. `C:\Users\fyfvg\.nuget\packages\s1api.forked\3.1.4\lib\netstandard2.1\S1API.xml` — real XML doc
>    comments shipped in the NuGet package.
> 2. `research/raw/ns/ns-S1API.*.txt` — full reflection member dumps of the **runtime IL2CPP build**.
> 3. `research/raw/01-namespaces.txt`, `02-types-index.txt`, `03-subclasses.txt`, `04-interface-implementors.txt`.
>
> Every signature is verbatim from those. Anything not confirmed is tagged **UNVERIFIED**.
> The game was **not** launched; nothing outside this file was modified.

**Merge note:** Part II of this file (§14 onward) is a parallel assembly-verified pass by another
agent — most valuably a **Harmony patch collision map** for `S1API.Internal.Patches`. It is
preserved verbatim. Where the two halves overlap, Part I is the more detailed one.

## Contents

**Part I — capability reference**
1. [Package & assembly facts](#1-package--assembly-facts)
2. [Namespace map](#2-namespace-map)
3. [Custom NPCs — the headline capability](#3-custom-npcs--the-headline-capability)
4. [Appearance](#4-appearance)
5. [Customers, dealers & economy](#5-customers-dealers--economy)
6. [Employees](#6-employees)
7. [Save data](#7-save-data)
8. [Phone apps](#8-phone-apps)
9. [Items, money, products, quests](#9-items-money-products-quests)
10. [Everything else](#10-everything-else)
11. [Gaps — what the team must build against the game assemblies directly](#11-gaps--what-the-team-must-build-against-the-game-assemblies-directly)
12. [Version stability](#12-version-stability)
13. [Open questions / unverified](#13-open-questions--unverified)

**Part II — parallel assembly-verified pass (preserved)** — §14 onward, including the Harmony
patch collision map.

---

## 1. Package & assembly facts

| Fact | Value |
|---|---|
| NuGet package id | `S1API.Forked` |
| Installed version | `3.1.4` (latest on nuget.org: **3.1.7**) |
| Author / description | `ifBars` — "A Schedule One Mono / Il2Cpp Cross Compatibility Layer (Forked from the original S1API by Kabooma)" |
| License | MIT |
| Upstream repo | `https://github.com/ifBars/S1API` @ commit `da37362ca3df49e2bac950c596b14c9567ef68a2` |
| Package dependency | `Newtonsoft.Json` `13.0.2` (`exclude="Build,Analyzers"`) |
| Compile-time lib | `lib/netstandard2.1/S1API.dll` (1 321 984 bytes) — **netstandard2.1**, one TFM only |
| XML docs | `lib/netstandard2.1/S1API.xml` (1 249 131 bytes) — real doc comments, mine them |
| Assembly identity | `S1API, Version=3.1.4.0, Culture=neutral, PublicKeyToken=null` |
| Runtime DLL | `<GameDir>\Mods\S1API.Il2Cpp.MelonLoader.dll` (1 358 336 bytes) |
| Loader plugin | `<GameDir>\Plugins\S1APILoader.MelonLoader.dll` (14 848 bytes) |
| Namespaces | 106 (`S1API` + 105 `S1API.*`) |

`<GameDir>` = `C:\Program Files (x86)\Steam\steamapps\common\Schedule I`.

### THE BIG ONE: the NuGet lib is the *Mono* build; the runtime DLL is the *IL2CPP* build

Both DLLs carry the assembly name `S1API`, but they are **not the same assembly**. Verified by
scanning both string heaps:

| | `Il2CppScheduleOne…` refs | bare `ScheduleOne…` refs |
|---|---|---|
| `…\.nuget\packages\s1api.forked\3.1.4\lib\netstandard2.1\S1API.dll` | **0** | **194** |
| `…\Schedule I\Mods\S1API.Il2Cpp.MelonLoader.dll` | **194** | **0** |

Assembly references differ to match: the runtime build pulls in `Il2Cppmscorlib`, `Il2CppSystem`,
`Il2CppFishNet`, `Il2CppTMPro`, `Il2CppInterop.Runtime`; the NuGet build pulls in plain
`netstandard` / `System.*` / `UnityEngine.*` / `Assembly-CSharp` / `MelonLoader` / `0Harmony` /
`Newtonsoft.Json`.

Consequences you must design around:

1. **S1API's public surface is deliberately game-type-free**, which is exactly what makes the
   Mono→IL2CPP swap work. `S1API.Entities.NPC`, `NPCPrefabBuilder`, `Saveable`, `PhoneApp`,
   `CustomerDataBuilder` etc. expose only `S1API.*`, `UnityEngine.*` and BCL types. Those bind fine.
2. **A minority of public members leak game types**, and those are effectively unusable from a mod
   compiled against the NuGet reference — the compiler emits a signature naming `ScheduleOne.X`,
   the runtime assembly declares `Il2CppScheduleOne.X`, so member resolution fails.
   *(High-confidence inference from the evidence above; **UNVERIFIED at runtime** — game not launched.)*
   The leaks that matter to this team:
   - `S1API.Law.LawManager.GetAssignedOfficers(...)` → `List<Il2CppScheduleOne.NPCs.NPC>` — **Police mod**
   - `S1API.Law.LawManager.OverrideActivitySettings(Il2CppScheduleOne.Law.LawActivitySettings)` — **Police mod**
   - `S1API.Entities.Relation.*.ApplyTo(Il2CppScheduleOne.NPCs.Relation.NPCRelationData, Il2CppScheduleOne.NPCs.NPC, bool)`
   - `S1API.Avatar.*.ResolveGameSeat()` / `ResolveSeatSet()`
   - `S1API.Building.*.CreateGridItem(...)` / `CreateSurfaceItem(...)`
   - `S1API.Dialogues.*.Register(Il2CppScheduleOne.Dialogue.DialogueHandler, string, System.Action)`
   - `S1API.GameTime.*` ctors and `ToS1()`
   - `S1API.Console.*.Submit(Il2CppSystem.Collections.Generic.IEnumerable<string>)`
   - `S1API.Property.*` ctors, `S1API.Stations.*` ctor, `S1API.Casino.*`,
     `S1API.PhoneCalls.*.S1PhoneCallData`, `S1API.AssetBundles.*` `Il2CppSystem.Type` overloads,
     `S1API.Utils.*.AddItemToArray<T>`
   For any of those, call the game assembly directly instead of going through S1API.
3. **The XML doc comments were generated from the Mono build.** Every `<see cref="T:ScheduleOne…"/>`
   in `S1API.xml` means `Il2CppScheduleOne…` at runtime. Read them with that substitution.

### The csproj gotcha — `PreventS1APICopy`

The loader supplies the runtime DLL, so the NuGet copy must never land in `Mods\`. If both are
present, two assemblies named `S1API` are in play and it breaks. `CreativeMode.csproj` handles this
with `<CopyLocalLockFileAssemblies>false</CopyLocalLockFileAssemblies>` plus an explicit scrub
target — reproduced verbatim:

```20:22:CreativeMode.csproj
  <ItemGroup>
    <PackageReference Include="S1API.Forked" Version="3.1.4" />
  </ItemGroup>
```

```79:84:CreativeMode.csproj
  <!-- Don't copy S1API NuGet into Mods — S1APILoader provides the runtime DLL -->
  <Target Name="PreventS1APICopy" AfterTargets="ResolvePackageAssets">
    <ItemGroup>
      <ReferenceCopyLocalPaths Remove="@(ReferenceCopyLocalPaths)" Condition="'%(Filename)' == 'S1API'" />
    </ItemGroup>
  </Target>
```

Rest of the project shape, for the three new mod csprojs to copy:

- `<TargetFramework>net6.0</TargetFramework>`, `LangVersion latest`, `Nullable enable`, `ImplicitUsings enable`
- `<AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>`
- `$(GameDir)` = `C:\Program Files (x86)\Steam\steamapps\common\Schedule I`;
  `$(Il2CppAssemblies)` = `$(GameDir)\MelonLoader\Il2CppAssemblies`; `$(MelonNet6)` = `$(GameDir)\MelonLoader\net6`
- Every `<Reference>` carries `<Private>false</Private>`: `MelonLoader`, `0Harmony`,
  `Il2CppInterop.Runtime` (from `$(MelonNet6)`); `UnityEngine.CoreModule`, `UnityEngine.IMGUIModule`,
  `UnityEngine.TextRenderingModule`, `UnityEngine.InputLegacyModule`, `UnityEngine`, `Il2Cppmscorlib`,
  `Il2CppSystem`, `Assembly-CSharp`, `Il2CppFishNet.Runtime`, `Il2CppScheduleOne.Core`
  (from `$(Il2CppAssemblies)`)
- `DeployToGame` target copies `$(TargetPath)` → `$(GameDir)\Mods` after build.

### How S1API is loaded

`S1APILoader.MelonLoader.dll` sits in `Plugins\`, so it runs before mods, and loads
`Mods\S1API.Il2Cpp.MelonLoader.dll`. S1API itself is a plain `MelonMod` (see §14) and installs
40 internal Harmony patch classes — see the collision map in §23.

## 2. Namespace map

106 namespaces. Counts are `public/total` copied verbatim from `research/raw/01-namespaces.txt`.
Two non-`S1API` namespaces also live in the assembly: `<global>` (1/3) and `Microsoft.CodeAnalysis` (0/1).

### Public namespaces

| Namespace | pub/total | Purpose |
|---|---|---|
| `S1API` | 1/1 | The `MelonMod` entry point itself (§14) |
| `S1API.AssetBundles` | 3/3 | `AssetLoader`, `WrappedAssetBundle(Request)` — load your own Unity bundles |
| `S1API.Avatar` | 4/4 | `Avatar`, `AvatarSettings`, `BasicAvatarSettings`, `Seat` (§4) |
| `S1API.Building` | 3/3 | `BuildManager`, `BuildEvents`, `BuildEventArgs` — placeable-object placement + events |
| `S1API.Cartel` | 5/5 | `Cartel`, `CartelGoon`, `CartelInfluence`, `CartelStatus`, `GoonManager` |
| `S1API.Casino` | 2/2 | `SlotMachineHelper`, `GamblingSessionMode` |
| `S1API.Conditions` | 1/1 | `SystemTriggerEntry` — condition entry for phone-call triggers |
| `S1API.Console` | 3/4 | `BaseConsoleCommand`, `ConsoleHelper` (27 cheats), `ConsoleItemAliases` (§10) |
| `S1API.Cutscenes` | 6/6 | `CutsceneBuilder`/`Camera`/`Frame`/`Handle`/`Manager`, `CutsceneEndReason` |
| `S1API.DeadDrops` | 4/4 | `DeadDropManager`, `DeadDropInstance`, `IDeadDropIdentifier`, `DeadDropGuidAttribute` (§5) |
| `S1API.DeadDrops.Native` | 25/25 | 25 marker types for the base-game dead drops (§5) |
| `S1API.Deliveries` | 5/5 | `DeliveryRegistry` + `Delivery`/`Item`/`Receipt`/`Status` — **read-only** (§5) |
| `S1API.Dialogues` | 5/5 | `DialogueInjector`, `DialogueInjection`, choice listener + paging |
| `S1API.Doors` | 3/3 | `DoorController`, `DoorAccess`, `DoorSide` |
| `S1API.Economy` | 6/6 | `Contract`, `ContractInfo(+Builder)`, `ContractReceipt`, `CustomerStandard`, `DealerType` (§5) |
| `S1API.Entities` | 15/16 | **`NPC`, `NPCPrefabBuilder`**, all the `NPCxxx` component wrappers, `Player` (§3) |
| `S1API.Entities.Actions` | 4/4 | Smoking / spray-painting / drinking / item-holding toggles (§3) |
| `S1API.Entities.Appearances.AccessoryFields` | 8/8 | 38 accessory asset-path constants (§4) |
| `S1API.Entities.Appearances.Base` | 4/4 | 4 marker base classes for the `<T>` category pattern (§4) |
| `S1API.Entities.Appearances.BodyLayerFields` | 6/6 | 36 body-layer asset-path constants (§4) |
| `S1API.Entities.Appearances.CustomizationFields` | 14/14 | 14 scalar categories + 24 hairstyles (§4) |
| `S1API.Entities.Appearances.FaceLayerFields` | 4/4 | 24 face-layer asset-path constants (§4) |
| `S1API.Entities.Behaviour` | 1/1 | `CombatBehaviour` (§3) |
| `S1API.Entities.Customer` | 1/1 | **`CustomerDataBuilder`** (§5) |
| `S1API.Entities.Dealer` | 2/2 | `DealerDataBuilder`, `DealerRecommendationBuilder` (§5) |
| `S1API.Entities.Dialogue` | 2/2 | `DialogueContainerBuilder`, `DialogueDatabaseBuilder` |
| `S1API.Entities.Employees` | 2/2 | `EmployeeManager` (appearance only), `EmployeeAppearance` (§6) |
| `S1API.Entities.Equippables` | 3/3 | `EquippablePath` (struct), `Misc`, `Weapon` path constants |
| `S1API.Entities.Impostors` | 2/2 | `NPCImpostorCatalog`, `AvatarImpostorDefinition` (§3) |
| `S1API.Entities.Interfaces` | 2/2 | `IEntity`, `IHealth` (§3) |
| `S1API.Entities.NPCs` | 6/6 | Wrappers: `DanSamwell`, `IgorRomanovich`, `MannyOakfield`, `OscarHolland`, `StanCarney`, `UncleNelson` |
| `S1API.Entities.NPCs.Docks` | 11/11 | Per-region base-game NPC wrappers (§3) |
| `S1API.Entities.NPCs.Downtown` | 11/11 | ″ |
| `S1API.Entities.NPCs.Northtown` | 16/16 | ″ |
| `S1API.Entities.NPCs.PoliceOfficers` | 9/9 | **The nine base-game officers** (§3) |
| `S1API.Entities.NPCs.Suburbia` | 11/11 | ″ |
| `S1API.Entities.NPCs.Uptown` | 10/10 | ″ |
| `S1API.Entities.NPCs.Westville` | 12/12 | ″ |
| `S1API.Entities.Relation` | 1/1 | `NPCRelationshipDataBuilder` |
| `S1API.Entities.Schedule` | 15/16 | `PrefabScheduleBuilder` + 12 `IScheduleActionSpec` implementations (§10) |
| `S1API.Entities.Supplier` | 1/1 | `SupplierDataBuilder` |
| `S1API.Entities.Voices` | 2/2 | `NPCVoiceCatalog`, `NPCVoiceDefinition` |
| `S1API.GameTime` | 3/3 | `TimeManager`, `GameDateTime` (struct), `Day` (enum) |
| `S1API.Graffiti` | 3/3 | `GraffitiManager`, `GraffitiEvents`, `SpraySurface` |
| `S1API.Growing` | 7/7 | Plants, seeds, mushroom beds, shroom colonies, grow-container additives |
| `S1API.Input` | 3/4 | `Controls`, `ButtonCode`, `InputDeviceType` |
| `S1API.Internal.Abstraction` | 3/7 | **`Saveable`, `Registerable`** + internal interfaces (§7) |
| `S1API.Items` | 32/32 | `ItemManager` + definition/instance/builder/creator triads for 6 item kinds (§9) |
| `S1API.Items.Additive` | 3/3 | Additive item authoring |
| `S1API.Items.Buildable` | 4/4 | Buildable item authoring |
| `S1API.Items.Clothing` | 7/7 | Clothing item authoring |
| `S1API.Items.Ingredient` | 3/3 | Mix-ingredient item authoring |
| `S1API.Items.Quality` | 4/4 | Quality-graded item authoring |
| `S1API.Items.Storable` | 4/5 | Storable item authoring |
| `S1API.Law` | 11/11 | **Wanted levels, pursuit, patrols, checkpoints, curfew, law intensity** (§10) |
| `S1API.Leveling` | 4/4 | `LevelManager`, `Rank`, `FullRank`, `Unlockable` |
| `S1API.Lifecycle` | 1/1 | `GameLifecycle` — managed load/save/scene events (§15) |
| `S1API.Logging` | 1/1 | `Log` (§14) |
| `S1API.Map` | 13/13 | `Region`, `Building`, `DeliveryLocation`, parking lots/spots, `MapPOI(+Builder,+Manager)` |
| `S1API.Map.Buildings` | 77/77 | 77 marker types, one per base-game building |
| `S1API.Map.DeliveryLocations` | 48/48 | 48 marker types, one per delivery location |
| `S1API.Map.ParkingLots` | 22/22 | 22 marker types, one per parking lot |
| `S1API.Messaging` | 1/1 | `Response` — text-message reply with callback |
| `S1API.Misc` | 1/1 | `ModularSwitch` |
| `S1API.Money` | 3/3 | `Money`, `CashDefinition`, `CashInstance` (§9) |
| `S1API.PhoneApp` | 2/3 | **`PhoneApp`**, `ExitAction` (§8) |
| `S1API.PhoneCalls` | 4/4 | `CallManager`, `PhoneCallDefinition`, `CallerDefinition`, `CallStageEntry` |
| `S1API.PhoneCalls.Constants` | 2/2 | `EvaluationType`, `SystemTriggerType` |
| `S1API.Products` | 51/56 | Full custom-drug framework — largest namespace (§9) |
| `S1API.Products.Packaging` | 1/2 | `StealthLevel` |
| `S1API.Properties` | 5/5 | `CustomEffect(+Builder)`, `EffectCreator`, `Property`, `ProductPropertyWrapper` |
| `S1API.Properties.Interfaces` | 1/1 | `PropertyBase` (abstract) |
| `S1API.Properties.Tokens` | 36/36 | 36 marker types for base-game product properties/effects |
| `S1API.Property` | 6/6 | Owned real estate: `PropertyManager`, `BusinessManager`, wrappers, `LaunderingOperation` |
| `S1API.Quests` | 5/5 | **`Quest`**, `QuestEntry`, `QuestManager`, `QuestWrapper`, `QuestData` (§9) |
| `S1API.Quests.Constants` | 2/2 | `QuestState`, `QuestAction` |
| `S1API.Quests.Identifiers` | 24/24 | 22 base-game quest markers + `IQuestIdentifier` + `QuestNameAttribute` |
| `S1API.Rendering` | 6/6 | Runtime asset creation: icons, materials, textures, avatar layers, accessories |
| `S1API.Saveables` | 2/3 | **`SaveableField`, `SaveableLoadOrder`** (§7) |
| `S1API.Shops` | 2/2 | `ShopManager`, `Shop` |
| `S1API.Stations` | 7/8 | Chemistry station recipe authoring |
| `S1API.Storage` | 4/4 | `StorageEntity`, `StorageEvents` + event args |
| `S1API.Storages` | 3/3 | `StorageManager`, `StorageInstance`, `StorageAccessSettings` |
| `S1API.Trash` | 1/1 | `TrashManager` |
| `S1API.TVApp` | 1/2 | `TVApp` (abstract) — same pattern as `PhoneApp`, for the in-game TV |
| `S1API.UI` | 3/3 | `UIFactory`, `CharacterCreatorManager`, `MainMenuRig` (§8, §11) |
| `S1API.Utils` | 9/9 | `EventHelper`, `ReflectionUtils`, colour/image/array/transform/button/toggle/random helpers |
| `S1API.Vehicles` | 4/4 | `VehicleRegistry`, `LandVehicle`, `VehicleColor`, `ParkingAlignment` (§10) |

### Internal-only namespaces (0 public types — listed so you know they exist and are off-limits)

| Namespace | total | What it does |
|---|---|---|
| `S1API.Internal` | 1 | `S1APIPreferences` |
| `S1API.Internal.Console` | 2 | Console alias registry |
| `S1API.Internal.Cutscenes` | 3 | Cutscene runtime |
| `S1API.Internal.Deliveries` | 5 | `DeliveryEventBridge` and friends |
| `S1API.Internal.Diagnostics` | 1 | Diagnostics |
| `S1API.Internal.Entities` | 9 | **`NPCPrefabIdentity`**, `NPCNetworkBootstrap`, `NPCDataAccess`, impostor/voice resolvers (§3) |
| `S1API.Internal.Entities.Suppliers` | 7 | Supplier runtime |
| `S1API.Internal.Items` | 6 | Item registration plumbing |
| `S1API.Internal.Lifecycle` | 2 | `SceneStateCleaner`, `TimeManagerShim` |
| `S1API.Internal.Map` | 4 | Map plumbing |
| `S1API.Internal.NPCWorkbench` | 19 | In-game NPC authoring/debug workbench |
| `S1API.Internal.Patches` | **40** | **All the Harmony patches — see the collision map in §23** |
| `S1API.Internal.Phone` | 1 | Phone plumbing |
| `S1API.Internal.Products` | 42 | Custom-product runtime |
| `S1API.Internal.Properties` | 2 | Effect plumbing |
| `S1API.Internal.Rendering` | 10 | Runtime texture/material generation |
| `S1API.Internal.Shops` | 1 | Shop plumbing |
| `S1API.Internal.Utils` | 13 | Misc helpers |

## 3. Custom NPCs — the headline capability

### Direct answer: yes. Here is the exact call sequence.

**You write one class. You never instantiate it, never register it, never spawn it.**

```csharp
using S1API.Entities;
using S1API.Map;

public class OfficerRookie : NPC                       // subclass S1API.Entities.NPC
{
    // 1. Declarative prefab configuration. Called ONCE during prefab creation,
    //    BEFORE the NPC exists in the world. Identity/customer/relationship/schedule
    //    MUST be set here or save/load and networking break.
    protected override void ConfigurePrefab(NPCPrefabBuilder builder)
    {
        builder
            .WithIdentity("officer_rookie", "Rookie", "Malone")
            .WithIcon(mySprite)                                     // 64x64 or 128x128
            .WithRegion(Region.Downtown)
            .WithSpawnPosition(new Vector3(x, y, z), Quaternion.identity)
            .WithVoice("male_01", 1.05f)
            .WithAppearanceDefaults(a => a
                .WithBodyLayer("Avatar/Layers/Bottom/Jeans", Color.white)
                .WithFaceLayer("Avatar/Layers/Face/Face_Stubble", Color.black)
                .WithAccessoryLayer("Avatar/Accessories/Head/Cap/Cap", Color.blue))
            .WithRelationshipDefaults(r => { /* NPCRelationshipDataBuilder */ })
            .WithInventoryDefaults(i => { /* RandomInventoryItemsBuilder */ })
            .WithSchedule(s => { /* PrefabScheduleBuilder */ });

        // opt-in component packs:
        // builder.EnsureCustomer();  builder.EnsureDealer();  builder.EnsureSupplier();
        // builder.EnsureSmokeBreak(); builder.EnsureDrinking(); builder.EnsureGraffiti();
        // builder.EnsureItemHolding();
    }

    // 2. Runtime init. Called after the NPC is instantiated and every component exists.
    protected override void OnCreated()
    {
        Aggressiveness = 0.8f;
        MaxHealth = 150f;
        Dialogue.…; Schedule.…; Inventory.…;
        OnDeath += HandleDeath;
    }
}
```

That's it. At load time `S1API.Internal.Patches.NPCPatches` (hooking
`Il2CppScheduleOne.Persistence.Loaders.NPCsLoader.Load`) does the rest:

1. `NPC.PreRegisterAllNpcPrefabs()` — *"Scans loaded assemblies for subclasses of S1API.Entities.NPC
   and pre-registers their prefabs"* into FishNet spawnables. Per XML doc on
   `PreRegisterPrefabForType`: *"Should be called on both server and client before any NPC instances
   are spawned."*
2. For each discovered type S1API constructs the instance, calls your `ConfigurePrefab(builder)`,
   and materialises the prefab GameObject under a persistent root named `@S1API_PersistentPrefabs`
   (`internal static class S1API.Entities.NPCPrefabContainer`, `private const string RootName =
   "@S1API_PersistentPrefabs"`).
3. `NPCPatches.RebuildPendingCustomNpcTypes(bool)` — *"Queue up custom NPC types so we can
   instantiate them in save order when using NPCs.json."*
4. `NPCPatches.InstantiateRemainingCustomNpcs(string)` — *"Instantiate any remaining custom NPC types
   that were not present in the save (newly added NPCs)."* This is why adding an NPC to an existing
   save works.
5. `NPC.TrySpawnNetworkInstance()` — *"Spawns this NPC's instance on the server using FishNet so it is
   networked. No-ops on clients."* Clients get `NPC.CreateWrapperForNetworkSpawnedNPC(...)`.
6. `NPC.CheckAndSetCustomNpcsReady()` flips the static `NPC.CustomNpcsReady` — *"Whether all custom
   NPCs have been instantiated and finalized… Mods can check this to ensure custom NPCs are ready
   before performing operations that depend on them."* **Gate all your cross-NPC wiring on this flag.**

Retrieval afterwards:

```csharp
var rookie = NPC.Get<OfficerRookie>();                                  // by wrapper type
var bailey = NPC.Get<S1API.Entities.NPCs.PoliceOfficers.OfficerBailey>();
var byId   = NPC.Get("officer_rookie");                                 // by S1 NPC ID, may return null
foreach (var n in NPC.All) { … }                                        // base game + modded
```

Persistence for free: `NPC : Saveable`, so `[SaveableField("…")]` on your subclass's fields is
persisted per save slot (see §7).

Prefab identity is carried into the world by an internal `MonoBehaviour`,
`S1API.Internal.Entities.NPCPrefabIdentity` (`[RegisterTypeInIl2Cpp]`), which stashes id / names /
icon / `Il2CppScheduleOne.AvatarFramework.AvatarSettings` / voice / relationship data in a static
registry and re-applies it via `ApplyCriticalIdentityBeforeAwake(Il2CppScheduleOne.NPCs.NPC)` and
`ApplyTo(Il2CppScheduleOne.NPCs.NPC)`. You never touch it, but that's where to look when an NPC
spawns nameless.

### `S1API.Entities.NPCPrefabBuilder` — complete public surface

`public sealed class NPCPrefabBuilder`, base `System.Object`. **The constructor is `internal`
(`internal .ctor(UnityEngine.GameObject prefabRoot, System.Type ownerType)`) — you only ever receive
one as the `ConfigurePrefab` parameter.** All methods return `NPCPrefabBuilder` for chaining.

```csharp
// --- identity & placement ---
public NPCPrefabBuilder WithIdentity(string id, string firstName, string lastName);
public NPCPrefabBuilder WithIcon(UnityEngine.Sprite icon);
public NPCPrefabBuilder WithRegion(S1API.Map.Region region);
public NPCPrefabBuilder WithSpawnPosition(UnityEngine.Vector3 position);
public NPCPrefabBuilder WithSpawnPosition(UnityEngine.Vector3 position, UnityEngine.Quaternion rotation);

// --- voice ---
public NPCPrefabBuilder WithVoice(string identifier);
public NPCPrefabBuilder WithVoice(string identifier, float pitch);
public NPCPrefabBuilder WithVoice(S1API.Entities.Voices.NPCVoiceDefinition voice);
public NPCPrefabBuilder WithVoice(S1API.Entities.Voices.NPCVoiceDefinition voice, float pitch);

// --- role component packs (add the game components) ---
public NPCPrefabBuilder EnsureCustomer();
public NPCPrefabBuilder EnsureDealer();
public NPCPrefabBuilder EnsureSupplier();

// --- idle-action component packs ---
public NPCPrefabBuilder EnsureSmokeBreak(string cigarettePrefabPath = null, System.Nullable<bool> debugMode = null);
public NPCPrefabBuilder EnsureDrinking(string drinkEquippablePath = null);
public NPCPrefabBuilder EnsureDrinking(S1API.Entities.Equippables.EquippablePath drinkEquippablePath);
public NPCPrefabBuilder EnsureGraffiti(string sprayPaintEquippablePath = null);
public NPCPrefabBuilder EnsureGraffiti(S1API.Entities.Equippables.EquippablePath sprayPaintEquippablePath);
public NPCPrefabBuilder EnsureItemHolding(string equippablePath = null);
public NPCPrefabBuilder EnsureItemHolding(S1API.Entities.Equippables.EquippablePath equippablePath);

// --- nested configuration blocks ---
public NPCPrefabBuilder WithAppearanceDefaults(System.Action<NPCPrefabBuilder.AvatarDefaultsBuilder> configure);
public NPCPrefabBuilder WithCustomerDefaults(System.Action<S1API.Entities.Customer.CustomerDataBuilder> configure);
public NPCPrefabBuilder WithDealerDefaults(System.Action<S1API.Entities.Dealer.DealerDataBuilder> configure);
public NPCPrefabBuilder WithSupplierDefaults(System.Action<S1API.Entities.Supplier.SupplierDataBuilder> configure);
public NPCPrefabBuilder WithRelationshipDefaults(System.Action<S1API.Entities.Relation.NPCRelationshipDataBuilder> configure);
public NPCPrefabBuilder WithInventoryDefaults(System.Action<S1API.Entities.RandomInventoryItemsBuilder> configure);

// --- schedule ---
public NPCPrefabBuilder WithSchedule(System.Action<S1API.Entities.Schedule.PrefabScheduleBuilder> configure);
public NPCPrefabBuilder WithSchedule(System.Collections.Generic.IEnumerable<S1API.Entities.Schedule.IScheduleActionSpec> specs);
public NPCPrefabBuilder WithSchedule(S1API.Entities.Schedule.IScheduleActionSpec[] specs);
```

XML doc on the type, verbatim: *"Builder for composing NPC prefab configuration before network spawn.
Use to declare networked components, spawn position, customer behavior, relationships, schedules, and
appearance defaults."* / *"Configuration must be done in `NPC.ConfigurePrefab` for proper save/load
behavior."*

`EnsureCustomer()` doc: *"Adds customer behavior component to the NPC. Required before configuring
customer defaults. Enables the NPC to act as a business customer that can buy products from the player."*

`WithIcon` doc: *"Should be 64x64 or 128x128 pixels. Uses default if not set."*

Game types it manipulates internally (private methods, listed so you know what it wires up):
`Il2CppScheduleOne.AvatarFramework.AvatarSettings`, `Il2CppScheduleOne.NPCs.NPCInventory`,
`Il2CppScheduleOne.NPCs.NPCScheduleManager`, `Il2CppScheduleOne.NPCs.Behaviour.Behaviour` /
`NPCBehaviour`, `Il2CppScheduleOne.NPCs.NPC`,
`Il2CppScheduleOne.Core.Equipping.Framework.EquippableData` / `TPEquippedItem`.

### `NPCPrefabBuilder.AvatarDefaultsBuilder` — complete public surface

`public sealed class S1API.Entities.NPCPrefabBuilder+AvatarDefaultsBuilder`, `public .ctor()`.
Settable properties (all `public get; public set;`), which map onto
`Il2CppScheduleOne.AvatarFramework.AvatarSettings`:

| Property | Type |
|---|---|
| `Gender` | `System.Single` |
| `Height` | `System.Single` |
| `Weight` | `System.Single` |
| `SkinColor` | `UnityEngine.Color32` |
| `LeftEyeLidColor` | `UnityEngine.Color` |
| `RightEyeLidColor` | `UnityEngine.Color` |
| `EyeBallTint` | `UnityEngine.Color` |
| `EyeballMaterialIdentifier` | `System.String` |
| `PupilDilation` | `System.Single` |
| `EyebrowScale` | `System.Single` |
| `EyebrowThickness` | `System.Single` |
| `EyebrowRestingHeight` | `System.Single` |
| `EyebrowRestingAngle` | `System.Single` |
| `LeftEye` | `System.ValueTuple<float, float>` — `(topLidOpen, bottomLidOpen)` |
| `RightEye` | `System.ValueTuple<float, float>` — `(topLidOpen, bottomLidOpen)` |
| `HairPath` | `System.String` |
| `HairColor` | `UnityEngine.Color` |
| `ImpostorSelection` | `S1API.Internal.Entities.AvatarImpostorSelection` — **`internal get; private set;`**, use the `WithImpostor*` methods |

Fluent methods (all return `AvatarDefaultsBuilder`):

```csharp
public AvatarDefaultsBuilder WithFaceLayer(string path, UnityEngine.Color color);
public AvatarDefaultsBuilder WithFaceLayer<T>(string path, UnityEngine.Color color);
public AvatarDefaultsBuilder WithBodyLayer(string path, UnityEngine.Color color);
public AvatarDefaultsBuilder WithBodyLayer<T>(string path, UnityEngine.Color color);
public AvatarDefaultsBuilder WithAccessoryLayer(string path, UnityEngine.Color color);
public AvatarDefaultsBuilder WithAccessoryLayer<T>(string path, UnityEngine.Color color);
public AvatarDefaultsBuilder WithImpostor(string name);
public AvatarDefaultsBuilder WithImpostor(S1API.Entities.Impostors.AvatarImpostorDefinition impostor);
public AvatarDefaultsBuilder WithImpostorTexture(UnityEngine.Texture2D texture);
public AvatarDefaultsBuilder WithRandomImpostor();
public AvatarDefaultsBuilder WithRandomImpostor(string[] names);
public AvatarDefaultsBuilder WithRandomImpostor(int seed, string[] names);
```

The generic overloads take the layer path as `string` too — the `<T>` is presumably a strongly-typed
path holder from `S1API.Entities.Appearances.*` (see §4). **UNVERIFIED:** the exact constraint on `T`
is not recoverable from the dump.

The three layer lists are stored as `internal readonly List<(string path, Color color)> FaceLayers /
BodyLayers / AccessoryLayers` — order of `With*Layer` calls is the layer order.

### `S1API.Entities.NPC` — complete public surface

`public abstract class NPC : S1API.Internal.Abstraction.Saveable`, implements
`S1API.Entities.Interfaces.IEntity`, `IHealth`, `S1API.Internal.Abstraction.IRegisterable`, `ISaveable`.
Wraps `Il2CppScheduleOne.NPCs.NPC`.

XML doc, verbatim:

> Abstract base class for creating custom NPCs with modular architecture supporting both physical and
> non-physical NPCs. Physical NPCs are visible in the game world with 3D models, movement, and direct
> interaction. Non-physical NPCs are invisible contacts primarily used for messaging and phone
> interactions.

```csharp
// --- construction ---
protected NPC();                                                    // the one you want; identity via ConfigurePrefab
protected NPC(string id, string firstName, string lastName, UnityEngine.Sprite icon = null);
        // "Backwards-compatible constructor for non-physical NPCs that provides identity directly."

// --- your override points ---
protected virtual void ConfigurePrefab(S1API.Entities.NPCPrefabBuilder builder);
protected virtual void OnCreated();
protected virtual void OnResponseLoaded(S1API.Messaging.Response response);
// inherited from Saveable/Registerable: OnLoaded(), OnSaved(), OnDestroyed(), LoadOrder

// --- statics ---
public static readonly List<NPC> All;                               // base game + modded
public static bool CustomNpcsReady { get; internal set; }
public static string NPCId { get; }                                 // per-wrapper-type constant
public static NPC Get<T>();
public static NPC Get(string npcId);
public static void PreRegisterAllNpcPrefabs();
public static void PreRegisterPrefabForType(System.Type npcType);

// --- identity ---
public string FirstName { get; set; }
public string LastName  { get; set; }
public string FullName  { get; }
public string ID        { get; protected set; }
public UnityEngine.Sprite Icon { get; set; }

// --- transform / world (IEntity) ---
public UnityEngine.GameObject gameObject { get; }                   // doc: "INTERNAL … Not intended for use by modders!"
public UnityEngine.Vector3 Position { get; set; }
public UnityEngine.Transform Transform { get; }                     // doc: "Please do not set the properties of this transform."
public float Scale { get; set; }
public void LerpScale(float scale, float lerpTime);
public void Goto(UnityEngine.Vector3 position);
public S1API.Map.Region Region { get; set; }
public bool RequiresRegionUnlocked { get; set; }

// --- state flags ---
public bool IsConscious  { get; }
public bool IsInBuilding { get; }
public bool IsInVehicle  { get; }
public bool IsPanicking  { get; }
public bool IsUnsettled  { get; }
public bool IsVisible    { get; }
public bool IsPhysical   { get; }
public bool IsDealer     { get; }
public bool IsSupplier   { get; }
public bool IsKnockedOut { get; }
public S1API.Vehicles.LandVehicle CurrentVehicle { get; }

// --- health (IHealth) ---
public float CurrentHealth { get; }
public float MaxHealth { get; set; }
public bool IsDead { get; }
public bool IsInvincible { get; set; }
public sealed override void Damage(int amount);
public sealed override void Heal(int amount);
public sealed override void Kill();
public sealed override void Revive();
public event System.Action OnDeath;

// --- behaviour ---
public float Aggressiveness { get; set; }
public void Panic();
public void StopPanicking();
public void Unsettle(float duration);
public void KnockOut();
public void SetEquippable(string assetPath);
public void SetEquippable(S1API.Entities.Equippables.EquippablePath equippablePath);

// --- component sub-objects (all read-only) ---
public S1API.Entities.NPCAppearance                 Appearance    { get; private set; }
public S1API.Entities.NPCMovement                   Movement      { get; }
public S1API.Entities.Behaviour.CombatBehaviour     CombatBehaviour { get; }
public S1API.Entities.Actions.NPCSmoking            Smoking       { get; }
public S1API.Entities.Actions.NPCSprayPainting      SprayPainting { get; }
public S1API.Entities.Actions.NPCDrinking           Drinking      { get; }
public S1API.Entities.Actions.NPCItemHolding        ItemHolding   { get; }
public S1API.Entities.NPCDialogue                   Dialogue      { get; }
public S1API.Entities.NPCSchedule                   Schedule      { get; }
public S1API.Entities.NPCInventory                  Inventory     { get; }
public S1API.Entities.NPCCustomer                   Customer      { get; }
public S1API.Entities.NPCDealer                     Dealer        { get; }
public S1API.Entities.NPCSupplier                   Supplier      { get; }
public S1API.Entities.NPCRelationship               Relationship  { get; }
public S1API.Entities.NPCMessaging                  Messaging     { get; }
public event System.Action OnInventoryChanged;

// --- messaging ---
protected readonly List<S1API.Messaging.Response> Responses;
public void SendTextMessage(string message, S1API.Messaging.Response[] responses = null,
                            float responseDelay = 1, bool network = true);
public bool ConversationCanBeHidden { get; set; }
public void ClearConversationCategories();
public void RefreshMessagingIcons();
```

`SendTextMessage` doc: *"Sends a text message from this NPC to the players. Supports responses with
callbacks… `message`: Unity rich text is allowed. `network`: Whether this should propagate to all
players or not."*

### `S1API.Entities.Interfaces` (2 types)

```csharp
public interface IEntity {
    UnityEngine.GameObject gameObject { get; }
    UnityEngine.Vector3 Position { get; set; }
    float Scale { get; set; }
}
public interface IHealth {
    float CurrentHealth { get; }
    float MaxHealth { get; set; }
    bool IsDead { get; }
    bool IsInvincible { get; set; }
    event System.Action OnDeath;
    void Damage(int amount); void Heal(int amount); void Kill(); void Revive();
}
```

These are **managed** S1API interfaces, so they behave normally. (The Il2CppInterop caveat — game
interfaces emitted as `class : Il2CppObjectBase` with no entries in the interface-implementor index —
does not apply to `S1API.*` types; it only bites when you go to the game assembly directly, e.g.
`Il2CppScheduleOne.Persistence.ISaveable`.)

### `S1API.Entities.NPCCustomer` (public sealed, wraps `Il2CppScheduleOne.Economy.Customer`)

```csharp
public bool IsCustomer { get; }
internal Il2CppScheduleOne.Economy.Customer Component { get; }      // INTERNAL — no escape hatch

public event System.Action OnUnlocked;
public event System.Action OnDealCompleted;
public event System.Action<float, int, int, int> OnContractAssigned;

public void EnsureCustomer();
public void Unlock();
public bool ForceDealOffer();
public bool OfferContract(S1API.Economy.ContractInfo info);
public void RecommendDealer(S1API.Entities.NPCDealer dealer);
public void RequestProduct(S1API.Entities.Player player = null);
public void SetAwaitingDelivery(bool awaiting);
public void SetupDialog();
```

`OnContractAssigned`'s four arguments are unnamed in the dump — **UNVERIFIED**, but by analogy with
`ContractInfo` they are most likely `(payment, productQuantity, …, …)`.

### `S1API.Entities.NPCDealer` (public sealed, wraps `Il2CppScheduleOne.Economy.Dealer`)

```csharp
public bool IsDealer { get; }
public S1API.Map.Building Home { get; set; }
internal Il2CppScheduleOne.Economy.Dealer Component { get; }

public event System.Action OnRecruited;
public event System.Action OnContractAccepted;
public event System.Action OnRecommended;

public void EnsureDealer();
public void RecruitDealer();
public bool IsRecruited();
public void AssignCustomer(S1API.Entities.NPC customer);
public void RemoveCustomer(S1API.Entities.NPC customer);
public List<S1API.Entities.NPC> GetAssignedCustomers();
public float GetCash();
public void ChangeCash(float amount);
public void CollectCash();
public bool HasBeenRecommended();
public void MarkAsRecommended();
```

**This is the closest thing S1API has to an "employee" API** — see §6 and §11. Dealers can be
recruited, assigned customers, and paid. Drivers/botanists/chemists/cleaners cannot.

### `S1API.Entities.Actions` (4 types) — idle-action toggles

Each is `public sealed`, constructed internally, reached via the `NPC.Smoking` / `.SprayPainting` /
`.Drinking` / `.ItemHolding` properties. Uniform shape:

```csharp
public bool IsActive { get; }
public void Begin();
public void End();
```

Plus, on `NPCSprayPainting` only:

```csharp
public void SetEffect(bool enabled, UnityEngine.Color color = null);
```

Game components wrapped: `Il2CppScheduleOne.NPCs.Other.SmokeCigarette`, `…Other.SprayPaint`,
`…Other.DrinkItem`, `…Other.HoldItem`. Requires the matching `builder.EnsureSmokeBreak()` /
`EnsureGraffiti()` / `EnsureDrinking()` / `EnsureItemHolding()` at prefab time.

### `S1API.Entities.Behaviour` (1 type)

```csharp
public class CombatBehaviour           // reached via NPC.CombatBehaviour
{
    public float  GiveUpRange { get; set; }
    public float  GiveUpTime  { get; set; }
    public string DefaultWeaponAssetPath { get; set; }

    public void SetAndAttackTarget(S1API.Entities.Interfaces.IEntity target);
    public void SetCurrentWeapon(string weaponPath);
    public void SetCurrentWeapon(S1API.Items.Equippable equippable);
    public void SetCurrentWeapon(S1API.Items.EquippableBuilder equippableBuilder);
    public void SetDefaultWeapon(S1API.Items.Equippable equippable);
    public void SetDefaultWeapon(S1API.Items.EquippableBuilder equippableBuilder);
}
```

Backed by `Il2CppScheduleOne.AvatarFramework.Equipping.AvatarWeapon`. **Relevant to the Police mod**:
`GiveUpRange` / `GiveUpTime` / weapon selection are exposed; pursuit AI, dispatch and wanted-level
are not (§11).

### `S1API.Entities.Impostors` (2 types) — cheap billboard avatars

```csharp
public sealed class AvatarImpostorDefinition
{
    public string Name { get; }
    public string ResourcePath { get; }
    internal UnityEngine.Texture2D Texture { get; }
    internal AvatarImpostorDefinition(string name, string resourcePath, UnityEngine.Texture2D texture);
    public override string ToString();
}

public static class NPCImpostorCatalog
{
    public static AvatarImpostorDefinition Find(string name);
    public static bool TryFind(string name, out AvatarImpostorDefinition impostor);
    public static IReadOnlyList<AvatarImpostorDefinition> GetAll();
    public static AvatarImpostorDefinition GetRandom(int seed);
}
```

Use `NPCImpostorCatalog.GetAll()` at runtime to discover valid impostor names, then feed them to
`AvatarDefaultsBuilder.WithImpostor(name)`.

### `S1API.Entities.NPCs.*` — 80 ready-made wrappers for every base-game NPC

Every one is `public class X : S1API.Entities.NPC` with exactly `public static string NPCId { get; }`
and an **`internal .ctor()`** — so you never `new` them; use `NPC.Get<X>()`. They exist so you can
reference a specific base-game NPC in a type-safe way. Complete roster:

| Namespace | Count | Types |
|---|---|---|
| `S1API.Entities.NPCs` | 6 | `DanSamwell`, `IgorRomanovich`, `MannyOakfield`, `OscarHolland`, `StanCarney`, `UncleNelson` |
| `…NPCs.Docks` | 11 | `AnnaChesterfield`, `BillyKramer`, `CrankyFrank`, `GenghisBarn`, `JaneLucero`, `JavierPerez`, `LisaGardener`, `MacCooper`, `MarcoBaron`, `MelissaWood`, `SalvadorMoreno` |
| `…NPCs.Downtown` | 11 | `BradCrosby`, `ElizabethHomley`, `EugeneBuckley`, `GregFliggle`, `JeffGilmore`, `JenniferRivera`, `KevinOakley`, `LouisFourier`, `LucyPennington`, `PhilipWentworth`, `RandyCaulfield` |
| `…NPCs.Northtown` | 16 | `AlbertHoover`, `AustinSteiner`, `BenjiColeman`, `BethPenn`, `ChloeBowers`, `DonnaMartin`, `GeraldinePoon`, `JessiWaters`, `KathyHenderson`, `KyleCooley`, `LudwigMeyer`, `MickLubbin`, `Ming`, `PeggyMyers`, `PeterFile`, `SamThompson` |
| **`…NPCs.PoliceOfficers`** | **9** | `OfficerBailey`, `OfficerCooper`, `OfficerGreen`, `OfficerHoward`, `OfficerJackson`, `OfficerLee`, `OfficerLopez`, `OfficerMurphy`, `OfficerOakley` |
| `…NPCs.Suburbia` | 11 | `AlisonKnight`, `CarlBundy`, `ChrisSullivan`, `DennisKennedy`, `HankStevenson`, `HaroldColt`, `JackKnight`, `JackieStevenson`, `JeremyWilkinson`, `KarenKennedy`, `WeiLong` |
| `…NPCs.Uptown` | 10 | `FionaHancock`, `HerbertBleuball`, `JenHeard`, `LeoRivers`, `LilyTurner`, `MichaelBoog`, `PearlMoore`, `RayHoffman`, `TobiasWentworth`, `WalterCussler` |
| `…NPCs.Westville` | 12 | `CharlesRowland`, `DeanWebster`, `DorisLubbin`, `GeorgeGreene`, `JerryMontero`, `JoyceBall`, `KeithWagner`, `KimDelaney`, `MegCooley`, `MollyPresley`, `ShirleyWatts`, `TrentSherman` |

**For the Police mod:** `NPCs.PoliceOfficers` gives you all nine existing officers as
`S1API.Entities.NPC` wrappers, so you get `CombatBehaviour`, `Aggressiveness`, `MaxHealth`, `Schedule`,
`Movement`, `Goto`, `Appearance` on each of them without writing a single Harmony patch. That is a
large chunk of "Police Improvements" for free.

## 4. Appearance

Three layers, in increasing order of convenience:

1. `S1API.Avatar` — thin wrappers over the game's avatar objects, usable on **any** avatar in the scene.
2. `S1API.Entities.NPCAppearance` — the fluent per-NPC builder, reached via `NPC.Appearance`.
3. `S1API.Entities.Appearances.*` — **80+ `public const string` asset paths**, so you never type a
   magic path. This is the part that saves the most time.

### `S1API.Avatar` (4 public types)

```csharp
public sealed class Avatar                       // wraps Il2CppScheduleOne.AvatarFramework.Avatar
{
    public UnityEngine.GameObject GameObject { get; }
    public bool IsActive { get; }
    public static Avatar[] FindInScene(bool includeInactive = false);
    public void LoadAvatarSettings(S1API.Avatar.AvatarSettings settings);
    // internal .ctor(Il2CppScheduleOne.AvatarFramework.Avatar avatar)
    // internal readonly Il2CppScheduleOne.AvatarFramework.Avatar S1Avatar
}
```

There is **no public `Avatar.ApplySettings(...)`** in 3.1.4 — the apply method is
`LoadAvatarSettings(AvatarSettings)`. On the NPC side the equivalent is
`NPCAppearance.Build()` (and the internal `NPCAppearance.ApplyToAvatar(Il2CppScheduleOne.AvatarFramework.Avatar)`).

```csharp
public sealed class AvatarSettings                // wraps Il2CppScheduleOne.AvatarFramework.AvatarSettings
{
    public static AvatarSettings Create();
    // internal .ctor(Il2CppScheduleOne.AvatarFramework.AvatarSettings settings)
    // internal readonly Il2CppScheduleOne.AvatarFramework.AvatarSettings S1AvatarSettings

    public float   Gender { get; set; }
    public float   Height { get; set; }
    public float   Weight { get; set; }
    public UnityEngine.Color32 SkinColor { get; set; }
    public string  HairPath { get; set; }
    public UnityEngine.Color HairColor { get; set; }
    public UnityEngine.Color32 LeftEyeLidColor  { get; set; }
    public UnityEngine.Color32 RightEyeLidColor { get; set; }
    public UnityEngine.Color EyeBallTint { get; set; }
    public float   PupilDilation { get; set; }
    public string  EyeballMaterialIdentifier { get; set; }
    public float   EyebrowScale { get; set; }
    public float   EyebrowThickness { get; set; }
    public float   EyebrowRestingHeight { get; set; }
    public float   EyebrowRestingAngle { get; set; }
    public AvatarSettings.EyeLidConfiguration LeftEyeRestingState  { get; set; }
    public AvatarSettings.EyeLidConfiguration RightEyeRestingState { get; set; }
    public int FaceLayerCount { get; }
    public int BodyLayerCount { get; }
    public int AccessoryCount { get; }

    public void AddFaceLayer(string layerPath, UnityEngine.Color layerTint);
    public void AddBodyLayer(string layerPath, UnityEngine.Color layerTint);
    public void AddAccessory(string path, UnityEngine.Color color);
    public List<AvatarSettings.LayerSetting>     GetFaceLayers();
    public List<AvatarSettings.LayerSetting>     GetBodyLayers();
    public List<AvatarSettings.AccessorySetting> GetAccessories();
    public void SetFaceLayers(List<AvatarSettings.LayerSetting> layers);
    public void SetBodyLayers(List<AvatarSettings.LayerSetting> layers);
    public void SetAccessories(List<AvatarSettings.AccessorySetting> accessories);
}

public sealed class AvatarSettings.LayerSetting     { public string LayerPath { get; set; } public UnityEngine.Color LayerTint { get; set; } public LayerSetting(); }
public sealed class AvatarSettings.AccessorySetting { public string Path { get; set; }      public UnityEngine.Color Color { get; set; }     public AccessorySetting(); }
public sealed class AvatarSettings.EyeLidConfiguration { public float TopLidOpen { get; set; } public float BottomLidOpen { get; set; } public EyeLidConfiguration(); }
```

```csharp
public sealed class BasicAvatarSettings            // wraps Il2CppScheduleOne.AvatarFramework.Customization.BasicAvatarSettings
{
    public static BasicAvatarSettings Create();
    // internal .ctor(Il2CppScheduleOne.AvatarFramework.Customization.BasicAvatarSettings settings)
    // internal readonly …Customization.BasicAvatarSettings S1BasicAvatarSettings

    public int    Gender { get; set; }             // NB: int here, float on AvatarSettings
    public float  Weight { get; set; }
    public UnityEngine.Color SkinColor { get; set; }
    public string HairStyle { get; set; }
    public UnityEngine.Color HairColor { get; set; }
    public string Mouth { get; set; }
    public string FacialHair { get; set; }
    public string FacialDetails { get; set; }
    public float  FacialDetailsIntensity { get; set; }
    public UnityEngine.Color EyeballColor { get; set; }
    public float  UpperEyeLidRestingPosition { get; set; }
    public float  LowerEyeLidRestingPosition { get; set; }
    public float  PupilDilation { get; set; }
    public float  EyebrowScale { get; set; }
    public float  EyebrowThickness { get; set; }
    public float  EyebrowRestingHeight { get; set; }
    public float  EyebrowRestingAngle { get; set; }
    public string Top { get; set; }        public UnityEngine.Color TopColor { get; set; }
    public string Bottom { get; set; }     public UnityEngine.Color BottomColor { get; set; }
    public string Shoes { get; set; }      public UnityEngine.Color ShoesColor { get; set; }
    public string Headwear { get; set; }   public UnityEngine.Color HeadwearColor { get; set; }
    public string Eyewear { get; set; }    public UnityEngine.Color EyewearColor { get; set; }

    public void AddTattoo(string tattooPath);
    public List<string> GetTattoos();
    public void SetTattoos(List<string> tattoos);
    public T    GetValue<T>(string fieldName);          // reflection escape hatch
    public void SetValue<T>(string fieldName, T value); // reflection escape hatch
    public S1API.Avatar.AvatarSettings ToAvatarSettings();
}
```

**Relation to the game type.** `Il2CppScheduleOne.AvatarFramework.Customization.BasicAvatarSettings`
is a `UnityEngine.ScriptableObject` whose `GetAvatarSettings()` returns
`Il2CppScheduleOne.AvatarFramework.AvatarSettings` — the runtime struct-of-settings the avatar renderer
consumes. S1API mirrors that split exactly: `S1API.Avatar.BasicAvatarSettings.ToAvatarSettings()` is
the wrapper-level equivalent of `GetAvatarSettings()`, and
`S1API.Avatar.Avatar.LoadAvatarSettings(AvatarSettings)` pushes the result onto a live avatar.
`GetValue<T>` / `SetValue<T>` exist because the game's `BasicAvatarSettings` has public fields S1API
doesn't mirror — use them for anything missing from the 27 properties above.

```csharp
public sealed class Seat                          // wraps Il2CppScheduleOne.AvatarFramework.Animation.AvatarSeat
{
    public string Label { get; }
    public string HierarchyPath { get; }
    public string SeatSetName { get; }
    public System.Nullable<int> IndexInSet { get; }
    public UnityEngine.Vector3    SittingPosition { get; }
    public UnityEngine.Quaternion SittingRotation { get; }
    public UnityEngine.Vector3    AccessPosition  { get; }
    public UnityEngine.Quaternion AccessRotation  { get; }
    public static int Count { get; }
    public static Seat[] GetAll();
    public static Seat[] GetBySeatSet(string setName);
    public static Seat FindByPathSuffix(string pathSuffix);
    public UnityEngine.GameObject ResolveSeatGameObject();
    public Il2CppScheduleOne.AvatarFramework.Animation.AvatarSeat    ResolveGameSeat();   // Il2Cpp leak, see §1
    public Il2CppScheduleOne.AvatarFramework.Animation.AvatarSeatSet ResolveSeatSet();    // Il2Cpp leak, see §1
}
```

`Seat` is a live registry — the constructors are private and instances are created by internal
`Register` / `Unregister` calls as the scene loads.

### `S1API.Entities.NPCAppearance` — the fluent per-NPC builder

Reached as `npc.Appearance`. Constructor is internal.

```csharp
public class NPCAppearance
{
    public NPCAppearance Set<T>(object appearanceValue);
    public NPCAppearance WithFaceLayer<T>(string path, UnityEngine.Color color);
    public NPCAppearance WithFaceLayer<T>(string path, uint hexColor);
    public NPCAppearance WithBodyLayer<T>(string path, UnityEngine.Color color);
    public NPCAppearance WithBodyLayer<T>(string path, uint hexColor);
    public NPCAppearance WithAccessoryLayer<T>(string path, UnityEngine.Color color);
    public NPCAppearance WithAccessoryLayer<T>(string path, uint hexColor);
    public NPCAppearance Build();                 // commits to the live avatar
    public void GenerateRandomAppearance();
    internal static bool MugshotsProcessingComplete { get; }
}
```

**`T` is the category marker class from `S1API.Entities.Appearances.*`** — the base classes all expose
`internal static List<string> GetConstPaths<T>()`, i.e. S1API reflects over `T`'s `const string` fields
to validate/enumerate the path. So the intended call shape is:

```csharp
using S1API.Entities.Appearances.AccessoryFields;
using S1API.Entities.Appearances.BodyLayerFields;
using S1API.Entities.Appearances.CustomizationFields;
using S1API.Entities.Appearances.FaceLayerFields;

npc.Appearance
   .Set<Gender>(0f)
   .Set<Height>(1.05f)
   .Set<HairStyle>(HairStyle.BuzzCut)
   .Set<SkinColor>(new Color32(210, 170, 140, 255))
   .WithBodyLayer<Shirts>(Shirts.Buttonup, Color.white)
   .WithBodyLayer<Pants>(Pants.CargoPants, Color.black)
   .WithFaceLayer<FacialHair>(FacialHair.Stubble, Color.black)
   .WithAccessoryLayer<Head>(Head.PoliceCap, Color.white)
   .WithAccessoryLayer<Waist>(Waist.PoliceBelt, Color.white)
   .WithAccessoryLayer<Chest>(Chest.BulletProofVestPolice, Color.white)
   .Build();
```

**UNVERIFIED:** the exact generic constraint on `T` and whether `Set<T>` validates the runtime type of
`appearanceValue` — the dump gives `Set<T>(System.Object)` with no constraint recorded. The 14
`<.cctor>b__33_*` closures in the compiler-generated cache class indicate a static
`Dictionary<Type, Action<NPCAppearance, object>>` with exactly **14 entries**, matching the 14 types in
`CustomizationFields` — so `Set<T>` accepts precisely those 14 `T`s.

`NPCAppearance` also drives mugshot generation (`internal void GenerateMugshot()`, a mugshot queue
coroutine, and `S1API.Internal.S1APIPreferences.EnableMugshotLoadingScreen`). Custom NPCs get contact
photos automatically.

### `S1API.Entities.Appearances.Base` (4 types)

Marker base classes, each `public class` with a `public .ctor()` and
`internal static List<string> GetConstPaths<T>()`:

- `BaseAppearance` — base for `CustomizationFields.*`
- `BaseFaceAppearance` — base for `FaceLayerFields.*`
- `BaseBodyAppearance` — base for `BodyLayerFields.*`
- `BaseAccessoryAppearance` — base for `AccessoryFields.*`

### `S1API.Entities.Appearances.CustomizationFields` (14 types)

Scalar/colour categories. Only `HairStyle` carries constants; the other 13 are pure type markers whose
value you pass to `Set<T>(object)`:

`EyeBallTint`, `EyeLidRestingStateLeft`, `EyeLidRestingStateRight`, `EyebrowRestingAngle`,
`EyebrowRestingHeight`, `EyebrowScale`, `EyebrowThickness`, `Gender`, `HairColor`, `HairStyle`,
`Height`, `PupilDilation`, `SkinColor`, `Weight`.

`HairStyle` — 24 `public const string`:

| Name | Path |
|---|---|
| `Afro` | `Avatar/Hair/afro/Afro` |
| `Balding` | `Avatar/Hair/balding/Balding` |
| `BowlCut` | `Avatar/Hair/bowlcut/BowlCut` |
| `Bun` | `Avatar/Hair/bun/Bun` |
| `BuzzCut` | `Avatar/Hair/buzzcut/BuzzCut` |
| `CloseBuzzCut` | `Avatar/Hair/closebuzzcut/CloseBuzzCut` |
| `DoubleTopKnot` | `Avatar/Hair/doubletopknot/DoubleTopKnot` |
| `Franklin` | `Avatar/Hair/franklin/Franklin` |
| `FringePonyTail` | `Avatar/Hair/fringeponytail/FringePonyTail` |
| `HighBun` | `Avatar/Hair/highbun/HighBun` |
| `Jesus` | `""` — `[Obsolete("The Jesus hairstyle was removed in game version 0.4.6 and now resolves to no hair.")]` |
| `LongCurly` | `Avatar/Hair/longcurly/LongCurly` |
| `LongSlicked` | `Avatar/Hair/longslicked/LongSlicked` |
| `LowBun` | `Avatar/Hair/lowbun/LowBun` |
| `MessyBob` | `Avatar/Hair/messybob/MessyBob` |
| `MidFringe` | `Avatar/Hair/midfringe/MidFringe` |
| `Mohawk` | `Avatar/Hair/mohawk/Mohawk` |
| `Monk` | `Avatar/Hair/monk/Monk` |
| `Peaked` | `Avatar/Hair/peaked/Peaked` |
| `Receding` | `Avatar/Hair/receding/Receding` |
| `ShoulderLength` | `Avatar/Hair/shoulderlength/ShoulderLength` |
| `SidePartBob` | `Avatar/Hair/sidepartbob/SidePartBob` |
| `Spiky` | `Avatar/Hair/spiky/Spiky` |
| `Tony` | `Avatar/Hair/tony/Tony` |

### `S1API.Entities.Appearances.FaceLayerFields` (4 types)

| Type | Constants |
|---|---|
| `Face` (13) | `Agape` `Avatar/Layers/Face/Face_Agape` · `Agitated` `…/Face_Agitated` · `FrownPout` `…/Face_FrownPout` · `Neutral` `…/Face_Neutral` · `NeutralPout` `…/Face_NeutralPout` · `OpenMouthSmile` `…/Face_OpenMouthSmile` · `Scared` `…/Face_Scared` · `SlightFrown` `…/Face_SlightFrown` · `SlightSmile` `…/Face_SlightSmile` · `Smile` `…/Face_Smile` · `SmugPout` `…/Face_SmugPout` · `Surprised` `…/Face_Surprised` · `FaceTattoos1` `Avatar/Layers/Face/FaceTattoos1` |
| `Eyes` (4) | `EyeShadow` `Avatar/Layers/Face/EyeShadow` · `Freckles` `…/Freckles` · `OldPersonWrinkles` `…/OldPersonWrinkles` · `TiredEyes` `…/TiredEyes` |
| `FacialHair` (3) | `Goatee` `Avatar/Layers/Face/FacialHair_Goatee` · `Stubble` `…/FacialHair_Stubble` · `Swirl` `…/FacialHair_Swirl` |
| `FaceTattoos` (4) | `ForeheadCross` `Avatar/Layers/Tattoos/face/Face_ForeheadCross` · `Sword` `…/Face_Sword` · `Teardrop` `…/Face_Teardrop` · `Tribal` `…/Face_Tribal` |

### `S1API.Entities.Appearances.BodyLayerFields` (6 types)

| Type | Constants |
|---|---|
| `Shirts` (13) | `Buttonup` `Avatar/Layers/Top/Buttonup` · `ChestHair` `…/ChestHair1` · `FastFoodTShirt` `…/FastFood T-Shirt` · `FlannelButtonUp` `…/FlannelButtonUp` · `GasStationTShirt` `…/GasStation T-Shirt` · `HazmatSuit` `…/HazmatSuit` · `Nipples` `…/Nipples` · `Overalls` `…/Overalls` · `RolledButtonUp` `…/RolledButtonUp` · `TShirt` `…/T-Shirt` · `TuckedTShirt` `…/Tucked T-Shirt` · `UpperBodyTattoos` `…/UpperBodyTattoos` · `VNeck` `…/V-Neck` |
| `Pants` (5) | `CargoPants` `Avatar/Layers/Bottom/CargoPants` · `FemaleUnderwear` `…/FemaleUnderwear` · `Jeans` `…/Jeans` · `Jorts` `…/Jorts` · `MaleUnderwear` `…/MaleUnderwear` |
| `Accessories` (2) | `FingerlessGloves` `Avatar/Layers/Accessories/FingerlessGloves` · `Gloves` `…/Gloves` |
| `ChestTattoos` (6) | `Bird` `Avatar/Layers/Tattoos/chest/Chest_Bird` · `DeadFace` `…/Chest_DeadFace` · `Egg` `…/Chest_Egg` · `LBC` `…/Chest_LBC` · `Sword` `…/Chest_Sword` · `UpperBody` `Avatar/Layers/Tattoos/UpperBodyTattoos` |
| `LeftArmTattoos` (5) | `Alien` `Avatar/Layers/Tattoos/leftarm/LeftArm_Alien` · `Heart` · `Peace` · `Web` · `Weed` |
| `RightArmTattoos` (5) | `Alien` `Avatar/Layers/Tattoos/rightarm/RightArm_Alien` · `Heart` · `Peace` · `Web` · `Weed` |

### `S1API.Entities.Appearances.AccessoryFields` (8 types)

| Type | Constants |
|---|---|
| `Head` (18) | `BucketHat` `Avatar/Accessories/Head/BucketHat/BucketHat` · `Cap` `…/Cap/Cap` · `CapFastFood` `…/Cap/Cap_FastFood` · `ChefHat` · `CowboyHat` · `FlatCap` · `LegendSunglasses` · `Oakleys` · **`PoliceCap` `Avatar/Accessories/Head/PoliceCap/PoliceCap`** · `PorkpieHat` · `RectangleFrameGlasses` · `Respirator` · `SaucePan` · `SmallRoundGlasses` · `Beanie` · `TrashCrown` · `SantaHat` · `MushroomHat` |
| `Chest` (5) | `Blazer` · `BulletProofVest` `Avatar/Accessories/Chest/BulletProofVest/BulletProofVest` · **`BulletProofVestPolice` `…/BulletProofVest/BulletProofVest_Police`** · `CollarJacket` · `OpenVest` |
| `Waist` (5) | `Apron` · `Belt` · `HazmatSuit` · **`PoliceBelt` `Avatar/Accessories/Waist/PoliceBelt/PoliceBelt`** · `PriestGown` |
| `Feet` (5) | `CombatBoots` · `DressShoes` · `Flats` · `Sandals` · `Sneakers` (all `Avatar/Accessories/Feet/<X>/<X>`) |
| `Bottom` (2) | `LongSkirt` `Avatar/Accessories/Bottom/LongSkirt/LongSkirt` · `MediumSkirt` `…/MediumSkirt/MediumSkirt` |
| `Neck` (1) | `GoldChain` `Avatar/Accessories/Neck/GoldChain/GoldChain` |
| `Hands` (1) | `Polex` `Avatar/Accessories/Hands/Polex/Polex` |
| `FacialHairAccessory` (1) | `Chevron` `Avatar/Accessories/FacialHair/Chevron/Chevron` |

**Police mod, free win:** `Head.PoliceCap` + `Chest.BulletProofVestPolice` + `Waist.PoliceBelt` +
`Feet.CombatBoots` is a complete officer look with zero asset authoring.

## 5. Customers, dealers & economy

### `S1API.Entities.Customer.CustomerDataBuilder` — the Special Customers workhorse

`public sealed class CustomerDataBuilder`, **`internal .ctor()`** — you only get one inside
`builder.WithCustomerDefaults(cd => …)` in `ConfigurePrefab`. It wraps a private
`Il2CppScheduleOne.Economy.CustomerData` (a `UnityEngine.ScriptableObject`) and `BuildInternal()`
returns it. Every fluent setter, with the `CustomerData` property it writes:

| Fluent setter | `Il2CppScheduleOne.Economy.CustomerData` field(s) |
|---|---|
| `WithSpending(float minWeekly, float maxWeekly)` | `MinWeeklySpend`, `MaxWeeklySpend` |
| `WithOrdersPerWeek(int min, int max)` | `MinOrdersPerWeek`, `MaxOrdersPerWeek` |
| `WithOrderTime(int hhmm)` | `OrderTime` |
| `WithPreferredOrderDay(S1API.GameTime.Day day)` | `PreferredOrderDay` (`Il2CppScheduleOne.GameTime.EDay`) |
| `WithPreferredOrderDay(string day)` | `PreferredOrderDay` |
| `WithStandards(S1API.Economy.CustomerStandard standards)` | `Standards` (`Il2CppScheduleOne.Economy.ECustomerStandard`) |
| `WithStandards(string standards)` | `Standards` |
| `AllowDirectApproach(bool allow)` | `CanBeDirectlyApproached` |
| `GuaranteeFirstSample(bool guarantee)` | `GuaranteeFirstSampleSuccess` |
| `WithMutualRelationRequirement(float minAt50, float maxAt100)` | `MinMutualRelationRequirement`, `MaxMutualRelationRequirement` |
| `WithCallPoliceChance(float chance)` | `CallPoliceChance` |
| `WithDependence(float baseAddiction, float dependenceMultiplier = 1)` | `BaseAddiction`, `DependenceMultiplier` |
| `WithAffinity(S1API.Products.DrugType drugType, float affinity)` | `DefaultAffinityData` (`Il2CppScheduleOne.Economy.CustomerAffinityData`) |
| `WithAffinities(IEnumerable<(S1API.Products.DrugType, float)> entries)` | `DefaultAffinityData` |
| `WithAffinities(IEnumerable<(string, float)> entries)` | `DefaultAffinityData` |
| `WithPreferredProperties(S1API.Properties.Interfaces.PropertyBase[] properties)` | `PreferredProperties` (`Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Effects.Effect>`) |
| `WithPreferredProperties(S1API.Properties.ProductPropertyWrapper[] wrappers)` | `PreferredProperties` |
| `WithPreferredPropertiesById(string[] propertyIds)` | `PreferredProperties` |
| `WithPreferredPropertiesByName(string[] propertyNames)` | `PreferredProperties` |

All 19 return `CustomerDataBuilder`. Verbatim signatures:

```csharp
public CustomerDataBuilder AllowDirectApproach(bool allow);
public CustomerDataBuilder GuaranteeFirstSample(bool guarantee);
public CustomerDataBuilder WithAffinities(IEnumerable<System.ValueTuple<string, float>> entries);
public CustomerDataBuilder WithAffinities(IEnumerable<System.ValueTuple<S1API.Products.DrugType, float>> entries);
public CustomerDataBuilder WithAffinity(S1API.Products.DrugType drugType, float affinity);
public CustomerDataBuilder WithCallPoliceChance(float chance);
public CustomerDataBuilder WithDependence(float baseAddiction, float dependenceMultiplier = 1);
public CustomerDataBuilder WithMutualRelationRequirement(float minAt50, float maxAt100);
public CustomerDataBuilder WithOrderTime(int hhmm);
public CustomerDataBuilder WithOrdersPerWeek(int min, int max);
public CustomerDataBuilder WithPreferredOrderDay(string day);
public CustomerDataBuilder WithPreferredOrderDay(S1API.GameTime.Day day);
public CustomerDataBuilder WithPreferredProperties(S1API.Properties.ProductPropertyWrapper[] wrappers);
public CustomerDataBuilder WithPreferredProperties(S1API.Properties.Interfaces.PropertyBase[] properties);
public CustomerDataBuilder WithPreferredPropertiesById(string[] propertyIds);
public CustomerDataBuilder WithPreferredPropertiesByName(string[] propertyNames);
public CustomerDataBuilder WithSpending(float minWeekly, float maxWeekly);
public CustomerDataBuilder WithStandards(string standards);
public CustomerDataBuilder WithStandards(S1API.Economy.CustomerStandard standards);
```

Not exposed (present on `CustomerData`, no fluent setter): `onChanged`, and the randomisers
`RandomizeAffinities()` / `RandomizeFavouriteEffects()` / `RandomizeTiming()`.

**Special Customers mod: this is your entire data model.** Combine
`builder.EnsureCustomer().WithCustomerDefaults(…)` in `ConfigurePrefab` with
`npc.Customer.OfferContract(ContractInfo)` / `.RequestProduct()` / `.Unlock()` at runtime.

### `S1API.Entities.Dealer` (2 public types)

```csharp
public sealed class DealerDataBuilder                          // internal .ctor(); via WithDealerDefaults(…)
{
    public DealerDataBuilder WithSigningFee(float fee);
    public DealerDataBuilder WithCut(float percentage);
    public DealerDataBuilder WithDealerType(S1API.Economy.DealerType type);
    public DealerDataBuilder WithHome(S1API.Map.Building building);
    public DealerDataBuilder WithHomeName(string name);
    public DealerDataBuilder AllowInsufficientQuality(bool allow);
    public DealerDataBuilder AllowExcessQuality(bool allow);
    public DealerDataBuilder WithCompletedDealsVariable(string varName);
    public DealerDataBuilder WithRecommendation(System.Action<DealerRecommendationBuilder> configure);
}

public sealed class DealerRecommendationBuilder                // internal .ctor()
{
    public DealerRecommendationBuilder FromCustomer(S1API.Entities.NPC customer);
    public DealerRecommendationBuilder FromCustomer(string customerId);
    public DealerRecommendationBuilder FromCustomer<TCustomer>();
    public DealerRecommendationBuilder OnDealCompleted();
}
```

The backing `DealerConfigData` is internal but its shape tells you the full set: `SigningFee`, `Cut`,
`DealerType`, `HomeName`, `Home`, `SellInsufficientQualityItems`, `SellExcessQualityItems`,
`CompletedDealsVariable`, `Recommendations`. The recommendation trigger enum
(`DealerRecommendationBuilder+Trigger`) is **internal** and has exactly one value, `DealCompleted = 0`
— hence the single `OnDealCompleted()` method.

Runtime dealer control is `S1API.Entities.NPCDealer` (§3).

### `S1API.Economy` (6 public types)

```csharp
public enum CustomerStandard : int { VeryLow = 0, Low = 1, Moderate = 2, High = 3, VeryHigh = 4 }
public enum DealerType       : int { PlayerDealer = 0, CartelDealer = 1 }

public sealed class Contract                        // wraps Il2CppScheduleOne.Quests.Contract
{
    public float Payment { get; }
    public int WindowStartTime { get; }
    public int WindowEndTime { get; }
    public int TotalQuantity { get; }
    // enumerable of (string, int, S1API.Products.Quality) — product id / quantity / quality
}

public sealed class ContractInfo
{
    public ContractInfo();
    public float Payment { get; set; }
    public List<ContractInfo.OrderLine> Orders { get; }
    public string DeliveryLocationGuid { get; set; }
    public System.Nullable<System.ValueTuple<int, int>> DeliveryWindow { get; set; }   // (startTime, endTime)
    public bool IsCounterOffer { get; set; }
    public bool Expires { get; set; }
    public int ExpiresAfterMinutes { get; set; }
    public int PickupScheduleIndex { get; set; }
    public ContractInfo AddProduct(S1API.Products.ProductDefinition definition, int quantity, S1API.Products.Quality minQuality);
    public ContractInfo AddProductById(string productId, int quantity, S1API.Products.Quality minQuality);
    public ContractInfo WithWindow(int startTime, int endTime);
    // internal Il2CppScheduleOne.Quests.ContractInfo ToInternal()
}
public sealed class ContractInfo.OrderLine { public string ProductId { get; set; } public int Quantity { get; set; } public S1API.Products.Quality MinQuality { get; set; } }

public sealed class ContractInfoBuilder
{
    public ContractInfoBuilder();
    public static ContractInfo QuickContract(S1API.Products.ProductDefinition definition, int quantity,
                                             float payment, S1API.Products.Quality minQuality = 2);
    public ContractInfoBuilder WithPayment(float payment);
    public ContractInfoBuilder AddProduct(S1API.Products.ProductDefinition definition, int quantity, S1API.Products.Quality minQuality);
    public ContractInfoBuilder AddProducts(IEnumerable<System.ValueTuple<S1API.Products.ProductDefinition, int, S1API.Products.Quality>> products);
    public ContractInfoBuilder WithDeliveryLocation(S1API.Map.DeliveryLocation location);
    public ContractInfoBuilder WithDeliveryLocationByGuid(string locationGuid);
    public ContractInfoBuilder WithDeliveryLocationByName(string locationName);
    public ContractInfoBuilder WithDeliveryWindow(int startTime, int endTime);
    public ContractInfoBuilder WithDeliveryWindow(System.TimeSpan startTime, System.TimeSpan endTime);
    public ContractInfoBuilder WithoutDeliveryWindow();
    public ContractInfoBuilder WithExpiration(bool expires);
    public ContractInfoBuilder ExpiresAfter(int minutes);
    public ContractInfoBuilder AsCounterOffer(bool isCounterOffer = true);
    public ContractInfoBuilder WithPickupScheduleIndex(int index);
    public ContractInfo Build();
}

public sealed class ContractReceipt
{
    public ContractReceipt();
    public int ReceiptId { get; set; }
    public string CustomerId { get; set; }
    public float AmountPaid { get; set; }
    public System.ValueTuple<int, int> CompletionTime { get; set; }        // (days, time)
    public System.ValueTuple<string, int>[] Items { get; set; }            // (id, quantity)
}
```

`ContractInfoBuilder` + `NPCCustomer.OfferContract(...)` is the complete "make a custom customer ask
for something" loop. Nothing here needs rewriting.

### `S1API.DeadDrops` (4 public types) + `S1API.DeadDrops.Native` (25)

```csharp
public static class DeadDropManager
{
    public static DeadDropInstance[] All   { get; }
    public static DeadDropInstance[] Empty { get; }
    public static DeadDropInstance Get<T>();
    public static string GetGuid<T>();
    public static DeadDropInstance GetByGUID(string guid);
    public static DeadDropInstance GetClosest(UnityEngine.Vector3 origin, bool mustBeEmpty = false);
    public static DeadDropInstance GetRandomEmptyNear(UnityEngine.Vector3 origin);
}

public class DeadDropInstance : S1API.Internal.Abstraction.IGUIDReference   // wraps Il2CppScheduleOne.Economy.DeadDrop
{
    public string GUID { get; }
    public string Name { get; }
    public string Description { get; }
    public S1API.Map.Region Region { get; }
    public S1API.Storages.StorageInstance Storage { get; }
    public int ItemCount { get; }
    public bool IsEmpty { get; }
    public UnityEngine.Vector3 Position { get; }
}

public interface IDeadDropIdentifier { }                        // pure marker, no members

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class DeadDropGuidAttribute : System.Attribute
{
    public DeadDropGuidAttribute(string guid);
    public string Guid { get; }
}
```

`S1API.DeadDrops.Native` is 25 `public sealed class` marker types, each presumably
`: IDeadDropIdentifier` with a `[DeadDropGuid("…")]`, used as the `T` in `DeadDropManager.Get<T>()`:
`BehindAutoShop`, `BehindBank`, `BehindCasino`, `BehindCrimsonCanary`, `BehindFireStation`,
`BehindGasMart`, `BehindGroceryStore`, `BehindLaundromat`, `BehindMedicalPractice`,
`BehindMotelOffice`, `BehindRandysBaitAndTackle`, `BehindSlopShop`, `BehindSupermarket`,
`BehindThompsonConstruction`, `BehindTopTattoo`, `BrownApartmentBlock`, `CentralCanal`, `Gazebo`,
`GreyDocksBuilding`, `NorthArcadeWall`, `PawnShopWestWall`, `SkatePark`,
`TacoTicklersExteriorWall`, `TownHallFountain`, `UnderWestBridge`.

### `S1API.Deliveries` (5 public types) — **read the Hireable Drivers note**

```csharp
public enum DeliveryStatus : int { InTransit = 0, Waiting = 1, Arrived = 2, Completed = 3 }

public static class DeliveryRegistry
{
    public static event System.Action<Delivery> Created;
    public static event System.Action<Delivery, DeliveryStatus, DeliveryStatus> StatusChanged;   // (delivery, previous, current)
    public static event System.Action<Delivery> Completed;

    public static IReadOnlyList<Delivery> GetAll();
    public static Delivery GetById(string id);
    public static IReadOnlyList<Delivery> GetForShop(S1API.Shops.Shop shop);
    public static IReadOnlyList<Delivery> GetForShop(string shopName);
    public static IReadOnlyList<DeliveryReceipt> GetHistory();
}

public sealed class Delivery                        // wraps Il2CppScheduleOne.Delivery.DeliveryInstance
{
    public string Id { get; }
    public string StoreName { get; }
    public string DestinationCode { get; }
    public int LoadingDockIndex { get; }
    public S1API.Property.PropertyWrapper Destination { get; }
    public DeliveryStatus Status { get; }
    public int MinutesUntilArrival { get; }
    public IReadOnlyList<DeliveryItem> Items { get; }
    public S1API.Shops.Shop Shop { get; }
    public S1API.Vehicles.LandVehicle ActiveVehicle { get; }
    public DeliveryReceipt ToReceipt();
    // internal Il2CppScheduleOne.Delivery.DeliveryInstance NativeDelivery { get; }
}

public sealed class DeliveryItem    { public string ItemId { get; } public int Quantity { get; } }
public sealed class DeliveryReceipt { public string Id { get; } public string StoreName { get; }
                                      public string DestinationCode { get; } public int LoadingDockIndex { get; }
                                      public IReadOnlyList<DeliveryItem> Items { get; } }
```

**This is entirely read-only observation.** You can watch deliveries and read `ActiveVehicle`, but
there is no `CreateDelivery`, no route assignment, no driver assignment, no vehicle control. The
internal `DeliveryRegistry.Notify*` methods and `S1API.Internal.Deliveries.DeliveryEventBridge`
("Translates native delivery lifecycle calls into the public delivery registry events") are how the
events fire — you cannot inject synthetic deliveries through them.

## 6. Employees

**Two types. Both cosmetic. There is no hire, fire, assign, pay, or behaviour API. At all.**

`S1API.Entities.Employees` in full — this is the complete namespace, nothing elided:

```csharp
public static class EmployeeManager
{
    public static EmployeeAppearance GetAppearance(bool male, int index);
    public static bool GetRandomAppearance(bool male, out int index, out S1API.Avatar.AvatarSettings settings);
    // private static readonly S1API.Logging.Log Logger
    // private .ctor()
}

public class EmployeeAppearance                     // wraps Il2CppScheduleOne.Employees.EmployeeManager+EmployeeAppearance
{
    public S1API.Avatar.AvatarSettings Settings { get; }
    public UnityEngine.Sprite Mugshot { get; }
    // internal .ctor(Il2CppScheduleOne.Employees.EmployeeManager+EmployeeAppearance s1EmployeeAppearance)
    // internal Il2CppScheduleOne.Employees.EmployeeManager+EmployeeAppearance S1EmployeeAppearance
}
```

What that buys you: the game's canned employee looks and their mugshot sprites, indexed by
`(male, index)`, plus a random picker. Useful for making a custom NPC *look* like a hired employee.

What is absent — verified by the type index containing no other `S1API.*Employee*` types:

- No wrapper for `Il2CppScheduleOne.Employees.Employee` or any of its subclasses
  (`Botanist`, `Chemist`, `Cleaner`, `Handler`, `Packager` — whichever exist in this build).
- No hire / fire / recruit / dismiss.
- No wage, payday, or paid-status.
- No assignment to a station, property, route, or vehicle.
- No employee behaviour, schedule, or task queue.
- No `EmployeeManager` roster access — the game's own `EmployeeManager` singleton is not wrapped.

**The nearest S1API equivalent to an employee is a Dealer** (`S1API.Entities.NPCDealer`,
`S1API.Entities.Dealer.DealerDataBuilder` — §5): recruitable, assignable customers, has a cut, holds
cash, has a home building. If "Hireable Drivers" can be modelled as a dealer-shaped relationship, you
get recruitment, payment and persistence free. If it needs the game's actual `Employee` hierarchy —
bed assignment, property binding, work loops — that is **entirely on you against `Assembly-CSharp`**
(§11).

## 7. Save data

**This is the single most valuable thing S1API gives you. Do not rewrite it.** Per-save mod
persistence, wired into the game's own save/load pipeline, in ~6 lines of code.

### The mechanism in one block

```csharp
using S1API.Internal.Abstraction;   // Saveable
using S1API.Saveables;              // SaveableField, SaveableLoadOrder

public class DriverRoster : Saveable        // subclass directly — see the "direct inheritor" rule
{
    [SaveableField("drivers")]              // → <saveslot>\Modded\Saveables\drivers.json
    private List<DriverRecord> _drivers = new();

    public override SaveableLoadOrder LoadOrder => SaveableLoadOrder.AfterBaseGame;  // optional

    protected override void OnCreated()  { }   // registration finished
    protected override void OnLoaded()   { }   // all [SaveableField]s deserialized
    protected override void OnSaved()    { }   // all [SaveableField]s written
    protected override void OnDestroyed(){ }   // about to unregister
}
```

You never instantiate or register it. `S1API.Saveables.SaveableAutoRegistry` scans loaded
assemblies at load time (`DiscoverSaveableTypes` → `IsDirectSaveableInheritor` →
`GetOrCreateInstances`) and constructs one singleton per discovered type.

**Constraint (from `IsDirectSaveableInheritor`):** discovery only picks up types that inherit
`Saveable` **directly**. A two-level hierarchy (`Saveable` → `MyBase` → `MyRoster`) will
**UNVERIFIED, but strongly implied by the method name** not be auto-registered. Keep mod
saveables one level deep. Types that already extend `Saveable` for other reasons — `S1API.Entities.NPC`,
`S1API.Quests.Quest` — get their `[SaveableField]`s handled through their own machinery.

### `S1API.Saveables` — complete surface (3 types, 2 public)

```csharp
[AttributeUsage(AttributeTargets.Field)]                 // attrs: [AttributeUsage(256)]
public class SaveableField : System.Attribute
{
    public SaveableField(string saveName);
    internal string SaveName { get; }                    // getter is INTERNAL — you can't read it back
}

public enum SaveableLoadOrder : int
{
    BeforeBaseGame = 0,
    AfterBaseGame  = 1,
}

internal static class SaveableAutoRegistry                // INTERNAL — cannot be called by mods
{
    public static IEnumerable<Saveable> GetRegisteredSaveables();
    internal static void ClearCache();
    private static void DiscoverSaveableTypes();
    private static IEnumerable<Saveable> GetOrCreateInstances();
    private static bool IsDirectSaveableInheritor(System.Type type);
}
```

XML doc on `SaveableField`, verbatim:

> Marks a field to be saved alongside the class instance. This attribute is intended to work across
> all custom game elements. (For example, custom NPCs, quests, etc.)
> **DO NOT NAME THE FIELD "QuestData" AS THIS WILL CONFLICT WITH THE API.**

XML doc on `LoadOrder`, verbatim:

> Default is `AfterBaseGame`, which loads after base game entities are loaded. Override this property
> to return `BeforeBaseGame` if your mod data needs to be available before the base game's ISaveables
> are loaded.
> **AfterBaseGame (default):** Your `OnLoaded` method is called after base game entities (NPCs,
> buildings, vehicles) have been loaded. This is the recommended setting for most mods.
> **BeforeBaseGame:** Your `OnLoaded` method is called before base game entities are loaded. Use this
> only if you need to set up hooks or state that the base game loading process depends on.
> **Note:** All saveables are saved at the same time (after base game save), regardless of load order.

### `S1API.Internal.Abstraction.Saveable` / `Registerable` — complete surface

```csharp
public abstract class Registerable : IRegisterable
{
    protected Registerable();
    internal virtual void CreateInternal();
    internal virtual void DestroyInternal();
    protected virtual void OnCreated();      // "Override hook for creation/registration completion."
    protected virtual void OnDestroyed();    // "Override hook for destruction/unregistration completion."
}

public abstract class Saveable : Registerable, IRegisterable, ISaveable
{
    protected Saveable();

    public SaveableLoadOrder LoadOrder { get; }          // virtual in practice: override it (see XML example)

    protected virtual void OnLoaded();                   // after all [SaveableField]s deserialized
    protected virtual void OnSaved();                    // after all [SaveableField]s written

    public static bool RequestGameSave();
    public static bool RequestGameSave(bool immediate);  // `immediate` ignored since v0.4.3

    internal virtual void LoadInternal(string folderPath);
    internal virtual void SaveInternal(string folderPath, ref Il2CppSystem.Collections.Generic.List<string> extraSaveables);
    internal void SaveToDynamic(Il2CppScheduleOne.Persistence.Datas.DynamicSaveData dynamicSaveData);
    internal void LoadFromDynamic(Il2CppScheduleOne.Persistence.Datas.DynamicSaveData dynamicSaveData);
}
```

The two supporting interfaces are **internal**, so you cannot implement them yourself — inheritance
from `Saveable` is the only route in:

```csharp
internal interface IRegisterable
{
    void CreateInternal(); void DestroyInternal(); void OnCreated(); void OnDestroyed();
}

internal interface ISaveable : IRegisterable
{
    internal static JsonSerializerSettings SerializerSettings { get; }
    void LoadInternal(string folderPath);
    void SaveInternal(string path, ref Il2CppSystem.Collections.Generic.List<string> extraSaveables);
    void OnLoaded(); void OnSaved();
}
```

`RequestGameSave()` XML doc: *"Requests the game to perform a save operation. If a game is not
currently loaded, the request is ignored and the method returns false."*

### Serialization details (from the XML docs)

- Serializer is **Newtonsoft.Json** (`13.0.2`), with an internal `GUIDReferenceConverter :
  JsonConverter` registered for `IGUIDReference` fields (both internal).
- One JSON file **per field**, named from the `SaveableField` string.
- `SaveInternal`: *"Null fields result in their corresponding save files being deleted. Non-null
  fields are added to the `extraSaveables` list to prevent the base game from deleting them during
  cleanup."* — so setting a field to `null` is how you delete its file.
- There is a second, newer path: `SaveToDynamic` / `LoadFromDynamic` write the same
  `[SaveableField]`s into an `Il2CppScheduleOne.Persistence.Datas.DynamicSaveData` blob "to support
  the base game's consolidated JSON save format". Both are `internal`; which path runs is decided by
  S1API, not by you. **UNVERIFIED** which one 3.1.4 actually uses at runtime.

### The Harmony hooks that drive it

`internal static class S1API.Internal.Patches.GenericSaveablesPatches`, `[HarmonyPatch]`:

| Method | Patch target | Kind |
|---|---|---|
| `BeforeBaseLoaders(Il2CppScheduleOne.Persistence.LoadRequest request)` | `Il2CppScheduleOne.Persistence.LoadManager.QueueLoadRequest` | `[HarmonyPrefix]` |
| `AfterBaseLoaders(string mainPath)` | `Il2CppScheduleOne.Persistence.Loaders.NPCsLoader.Load` | `[HarmonyPostfix]` |
| `SaveManager_Save_Postfix(string saveFolderPath)` | `Il2CppScheduleOne.Persistence.SaveManager.Save(string)` | `[HarmonyPostfix]` |

It also carries a `private static bool sameSession` flag and per-saveable deferred initializers
(`InitializeOnLoadComplete`, `ClearLockOnLoadComplete`) hung off `LoadManager`. Failures are logged
as `[Saveables] BeforeBaseLoaders failed:` / `AfterBaseLoaders failed:` / `SaveManager_Save_Postfix failed:`
— grep MelonLoader's log for those strings when save data goes missing.

`BeforeBaseLoaders` is what makes `SaveableLoadOrder.BeforeBaseGame` work; `AfterBaseLoaders` hangs
off the **NPC** loader, which is why "after base game" specifically means "after NPCs exist".

### On-disk layout (observed read-only, nothing modified)

Save root on this machine: `C:\Users\fyfvg\AppData\LocalLow\TVGS\Schedule I\Saves\76561199588797561\`

```
SaveGame_3\
  Modded\
    Quests\          (present, empty)
    Saveables\       (present, empty)
SaveGame_4\
  Modded\
    Quests\          (present, empty)
    Saveables\       (present, empty)
```

Both `Modded\` folders exist and both contain exactly `Quests\` and `Saveables\`, both currently
empty — S1API creates the skeleton on save even when no mod has registered a saveable.

So the full path for the example above is:

```
…\Saves\<steamid>\SaveGame_N\Modded\Saveables\drivers.json
```

`Modded\Quests\` is the parallel destination for `S1API.Quests.Quest` subclasses. A third literal in
the assembly, `Modded/CustomProducts.json`, shows custom products are persisted as a single file
directly under `Modded\`. **UNVERIFIED:** the exact per-file naming inside `Saveables\` (whether it
is `<SaveName>.json` flat, or nested per saveable type) — both folders were empty, so there was
nothing to observe. The `[SaveableField("…")]` string is the only name input, so flat
`<SaveName>.json` is the strong inference.

## 8. Phone apps

Same deal as NPCs and saveables: **subclass and forget.** `S1API.PhoneApp` has 3 types (2 public).

```csharp
public abstract class PhoneApp : S1API.Internal.Abstraction.Registerable
{
    protected PhoneApp();
    protected static readonly S1API.Logging.Log Logger;

    // --- declare your app (protected getters, override them) ---
    protected string AppName      { get; }
    protected string AppTitle     { get; }
    protected string IconLabel    { get; }
    protected string IconFileName { get; }
    protected UnityEngine.Sprite IconSprite { get; }
    protected PhoneApp.EOrientation Orientation { get; }

    // --- the one you MUST implement ---
    protected abstract void OnCreatedUI(UnityEngine.GameObject container);

    // --- optional hooks ---
    protected virtual void OnCreated();
    protected virtual void OnDestroyed();
    protected virtual void OnPhoneClosed();
    public    virtual void Exit(S1API.PhoneApp.ExitAction exit);

    // --- runtime control ---
    public void OpenApp();
    public void CloseApp();
    public bool IsOpen();
    public bool SetIconSprite(UnityEngine.Sprite sprite);
    public bool SetIconTexture(UnityEngine.Texture2D texture);
}

public enum PhoneApp.EOrientation : int { Horizontal = 0, Vertical = 1 }

public sealed class ExitAction               // internal .ctor
{
    public bool Used { get; set; }           // set true to swallow the back/escape press
}
```

### Registration story

There is **no `Register` call**. S1API Harmony-patches `Il2CppScheduleOne.UI.Phone.HomeScreen.Start`
and, for every discovered `PhoneApp` subclass, calls the internal
`SpawnIcon(Il2CppScheduleOne.UI.Phone.HomeScreen)` then `SpawnUI(Il2CppScheduleOne.UI.Phone.HomeScreen)`.
Icon click routing goes through `internal class S1API.PhoneApp.PhoneAppButtonHandler`, a
`[RegisterTypeInIl2Cpp] MonoBehaviour` that polls hover state in `Update()`. Escape/back handling is
bridged from `Il2CppScheduleOne.GameInput+ExitDelegate` via the private `HandleNativeExit` into your
`Exit(ExitAction)` override.

Because `PhoneApp : Registerable` (not `Saveable`), a phone app is **not** persisted. If it has state,
pair it with a separate `Saveable` (§7).

Icons: set either `IconFileName` (loaded from disk — **UNVERIFIED** which directory it resolves
against) or `IconSprite`, or call `SetIconSprite` / `SetIconTexture` at runtime.

### Building the UI inside `OnCreatedUI`

`S1API.UI.UIFactory` is a static helper that produces Unity uGUI objects styled roughly like the game's
phone. Complete surface (18 methods):

```csharp
public static UnityEngine.GameObject Panel(string name, UnityEngine.Transform parent, UnityEngine.Color bgColor,
                                           System.Nullable<UnityEngine.Vector2> anchorMin = null,
                                           System.Nullable<UnityEngine.Vector2> anchorMax = null,
                                           bool fullAnchor = false);
public static UnityEngine.UI.Text Text(string name, string content, UnityEngine.Transform parent,
                                       int fontSize = 14, UnityEngine.TextAnchor anchor = 0,
                                       UnityEngine.FontStyle style = 0);
public static UnityEngine.GameObject TopBar(string name, UnityEngine.Transform parent, string title,
                                            float topbarSize, int paddingLeft, int paddingRight,
                                            int paddingTop, int paddingBottom);
public static System.ValueTuple<UnityEngine.GameObject, UnityEngine.UI.Button, UnityEngine.UI.Text>
       ButtonWithLabel(string name, string label, UnityEngine.Transform parent, UnityEngine.Color bgColor, float Width, float Height);
public static System.ValueTuple<UnityEngine.GameObject, UnityEngine.UI.Button, UnityEngine.UI.Text>
       RoundedButtonWithLabel(string name, string label, UnityEngine.Transform parent, UnityEngine.Color bgColor,
                              float width, float height, int fontSize, UnityEngine.Color textColor);
public static UnityEngine.GameObject ButtonRow(string name, UnityEngine.Transform parent, float spacing = 12,
                                               UnityEngine.TextAnchor alignment = 4);
public static void BindAcceptButton(UnityEngine.UI.Button btn, UnityEngine.UI.Text label, string text,
                                    UnityEngine.Events.UnityAction callback);
public static UnityEngine.RectTransform ScrollableVerticalList(string name, UnityEngine.Transform parent,
                                                               out UnityEngine.UI.ScrollRect scrollRect);
public static UnityEngine.GameObject CreateQuestRow(string name, UnityEngine.Transform parent,
                                                    out UnityEngine.GameObject iconPanel,
                                                    out UnityEngine.GameObject textPanel);
public static void CreateRowButton(UnityEngine.GameObject go, UnityEngine.Events.UnityAction clickHandler, bool enabled);
public static void CreateTextBlock(UnityEngine.Transform parent, string title, string subtitle, bool isCompleted);
public static void SetIcon(UnityEngine.Sprite sprite, UnityEngine.Transform parent);
public static void HorizontalLayoutOnGO(UnityEngine.GameObject go, int spacing = 10, int padLeft = 0, int padRight = 0,
                                        int padTop = 0, int padBottom = 0, UnityEngine.TextAnchor alignment = 4);
public static void VerticalLayoutOnGO(UnityEngine.GameObject go, int spacing = 10, UnityEngine.RectOffset padding = null);
public static void SetLayoutGroupPadding(UnityEngine.UI.LayoutGroup layoutGroup, int left, int right, int top, int bottom);
public static void FitContentHeight(UnityEngine.RectTransform content);
public static void ClearChildren(UnityEngine.Transform parent);
```

Note it emits legacy `UnityEngine.UI.Text`, not TextMeshPro, so it will not match the game's own font
rendering exactly.

## 9. Items, money, products, quests

The real namespace names are `S1API.Items`, `S1API.Money`, `S1API.Products`, `S1API.Quests` — all four
exist, all four are large.

### `S1API.Money` (3 public types) — balance and cash

```csharp
public static class Money
{
    public static event System.Action OnBalanceChanged;

    public static float GetCashBalance();
    public static float GetOnlineBalance();
    public static float GetNetWorth();
    public static void  ChangeCashBalance(float amount, bool visualizeChange = true, bool playCashSound = false);
    public static void  CreateOnlineTransaction(string transactionName, float unitAmount, float quantity, string transactionNote);
    public static CashInstance CreateCashInstance(float amount);
    public static void AddNetworthCalculation(System.Action<object> callback);
    public static void RemoveNetworthCalculation(System.Action<object> callback);
    // private static Il2CppScheduleOne.Money.MoneyManager Internal { get; }
}

public class CashDefinition : S1API.Items.ItemDefinition, S1API.Internal.Abstraction.IGUIDReference
{
    public override S1API.Items.ItemInstance CreateInstance(int quantity = 1);
}

public class CashInstance : S1API.Items.ItemInstance
{
    public void AddQuantity(float amount);
    public void SetQuantity(float newQuantity);
}
```

Note there is **no `ChangeOnlineBalance`** — online money moves only through
`CreateOnlineTransaction(...)`, which is the correct game-shaped way (it shows up in the ledger).
`AddNetworthCalculation(Action<object>)` lets you contribute a custom net-worth term.

### `S1API.Items` (32 public types) — lookup, give, and item authoring

Lookup and registration:

```csharp
public static class ItemManager
{
    public static ItemDefinition GetDefinition(string itemID);
    [Obsolete("Use S1API.Items.ItemManager.GetDefinition instead.")]
    public static ItemDefinition GetItemDefinition(string itemID);
    public static List<ItemDefinition> GetAllItemDefinitions();
    public static bool IsItemRegistered(string itemID);
    public static void RegisterItem(ItemDefinition definition);
    public static bool EnsureItemRegistered(ItemDefinition definition);
    public static bool UnregisterItem(string itemID);
    public static bool PreserveRuntimeItem(ItemDefinition definition);
}
```

Giving items to the player is via `S1API.Console.ConsoleHelper.AddItemToInventory(string itemCode,
int? quantity = null)` (§10) or by constructing an `ItemInstance` from a definition and inserting it
into an inventory/`ItemSlotInstance`.

Full type roster of `S1API.Items`:

`ItemManager`, `ItemDefinition`, `ItemInstance`, `ItemSlotInstance`, `ItemCreator`, `ItemCategory`
(enum), `LegalStatus` (enum), `Equippable`, `EquippableBuilder`, `AvatarEquippablePaths`,
`AvatarEquippableRegistry`, `AvatarHand` (enum), `StorableItemDefinition`,
`StorableItemDefinitionBuilder`, `QualityItemDefinition`, `QualityItemDefinitionBuilder`,
`QualityItemInstance`, `QualityItemCreator`, `AdditiveDefinition`, `AdditiveDefinitionBuilder`,
`AdditiveItemCreator`, `BuildableItemDefinition`, `BuildableItemDefinitionBuilder`,
`BuildableItemCreator`, `BuildSoundType` (enum), `ClothingItemDefinition`,
`ClothingItemDefinitionBuilder`, `ClothingItemInstance`, `ClothingItemCreator`, `ClothingSlot` (enum),
`ClothingColor` (enum), `ClothingApplicationType` (enum).

Sub-namespaces mirror the same types by category, plus:

- `S1API.Items.Additive` (3): `AdditiveDefinition`, `AdditiveDefinitionBuilder`, `AdditiveItemCreator`
- `S1API.Items.Buildable` (4): `BuildableItemDefinition`, `BuildableItemDefinitionBuilder`, `BuildableItemCreator`, `BuildSoundType`
- `S1API.Items.Clothing` (7): `ClothingItemDefinition`, `ClothingItemDefinitionBuilder`, `ClothingItemInstance`, `ClothingItemCreator`, `ClothingSlot`, `ClothingColor`, `ClothingApplicationType`
- `S1API.Items.Ingredient` (3): `MixIngredientDefinition`, `MixIngredientDefinitionBuilder`, `MixIngredientItemCreator`
- `S1API.Items.Quality` (4): `QualityItemDefinition`, `QualityItemDefinitionBuilder`, `QualityItemInstance`, `QualityItemCreator`
- `S1API.Items.Storable` (5, 4 public): `StorableItemDefinition`, `StorableItemDefinitionBuilder`, `StorableItemDefinitionBuilderBase<T>` (abstract), `ItemCreator`, + internal `StorableItemDefinitionBuilderState`

Every category follows the same `XDefinitionBuilder` → `XDefinition` → `XItemCreator` triad. Custom
items are a solved problem; do not rewrite it.

### `S1API.Products` (56 types, 51 public) — the largest namespace in the library

This is a full custom-drug authoring framework, far beyond "look up a product":

```csharp
public static class ProductManager
{
    public const int MinPrice = 1;
    public const int MaxPrice = 999;

    public static ProductDefinition[] DiscoveredProducts  { get; }
    public static ProductDefinition[] ListedProducts      { get; }
    public static ProductDefinition[] FavouritedProducts  { get; }
    public static bool IsAcceptingOrders  { get; }
    public static bool MethDiscovered     { get; }
    public static bool CocaineDiscovered  { get; }
    public static bool ShroomsDiscovered  { get; }

    public static float GetPrice(ProductDefinition product);
    public static float CalculateProductValue(ProductDefinition product, float baseValue);

    // effect interception — player
    public static void SetEffectCallback(string effectId, System.Action<S1API.Entities.Player> callback, bool allowDefaultEffect = false);
    public static void SetEffectCallback(S1API.Properties.Interfaces.PropertyBase property, System.Action<S1API.Entities.Player> callback, bool allowDefaultEffect = false);
    public static void SetEffectClearCallback(string effectId, System.Action<S1API.Entities.Player> callback, bool allowDefaultEffect = false);
    public static void SetEffectClearCallback(S1API.Properties.Interfaces.PropertyBase property, System.Action<S1API.Entities.Player> callback, bool allowDefaultEffect = false);
    public static bool RemoveEffectCallback(string effectId);
    public static bool RemoveEffectCallback(S1API.Properties.Interfaces.PropertyBase property);
    public static bool RemoveEffectClearCallback(string effectId);
    public static bool RemoveEffectClearCallback(S1API.Properties.Interfaces.PropertyBase property);
    public static void ClearEffectCallbacks();
    public static void ResetEffectClearCallbacks();

    // effect interception — NPC (same 10-method shape with System.Action<S1API.Entities.NPC>)
    public static void SetNpcEffectCallback(…);        public static void SetNpcEffectClearCallback(…);
    public static bool RemoveNpcEffectCallback(…);     public static bool RemoveNpcEffectClearCallback(…);
    public static void ClearNpcEffectCallbacks();      public static void ResetNpcEffectClearCallbacks();
}
```

`SetNpcEffectCallback` is worth flagging for **Special Customers**: it lets you run arbitrary code
when a given product effect lands on an NPC, with `allowDefaultEffect` controlling whether the game's
own effect still applies.

Type roster (public): `ProductManager`, `ProductDefinition`, `ProductInstance`,
`ProductDefinitionWrapper`, `ProductPopulator`, `Quality` (enum), `DrugType` (enum),
`WeedDefinition`, `WeedDefinitionBuilder`, `WeedItemCreator`, `WeedAppearanceSettings`,
`MethDefinition`, `CocaineDefinition`, `ShroomDefinition`, `ShroomInstance`,
`ShroomAppearanceSettings`, `PackagingDefinition`, `CustomProductDefinition`,
`CustomProductDefinitionBuilder`, `CustomProductItemCreator`, `CustomProductMultiplayer`,
`CustomProductMultiplayerPolicy` (enum), `CustomProductSaveDescriptor`,
`CustomProductSaveProviderRegistry`, `ICustomProductSaveProvider`, `MixReactions`,
`ProductConsumptionContext`, `ProductConsumptionProfile`, `ProductConsumptionProfileBuilder`,
`ProductConsumptionProfileRegistry`, `ProductKind`, `ProductKindBuilder`, `ProductKindMetadata`,
`ProductKindMetadataBuilder`, `ProductKindMetadataRegistry`, `ProductKindRegistry`,
`ProductMixingMap` (enum), `ProductMixingOutput`, `ProductMixingOutputDefinition`,
`ProductMixingProfile`, `ProductMixingProfileBuilder`, `ProductMixingProfiles`,
`ProductPackagingContentProfile`, `ProductPackagingContentProfileBuilder`,
`ProductPackagingContentProfileRegistry`, `ProductPackagingVisualTemplate` (enum),
`ProductPresentationContext` (enum), `ProductPresentationProfile`,
`ProductPresentationProfileBuilder`, `ProductPresentationProfileRegistry`,
`ProductPresentationTransform`.

`S1API.Products.Packaging` (2, 1 public): `StealthLevel` (enum).

Custom products persist to `<saveslot>\Modded\CustomProducts.json` (string literal found in the
assembly) — a different path from `Modded\Saveables\` (§7).

### `S1API.Quests` (5 public types) + `.Constants` (2) + `.Identifiers` (24)

Same subclass-and-forget pattern as NPCs, and `Quest : Saveable` so quest state persists.

```csharp
public abstract class Quest : S1API.Internal.Abstraction.Saveable
{
    public Quest();                                   // public ctor, unlike NPC
    public readonly List<QuestEntry> QuestEntries;

    protected string Title { get; }                   // override these
    protected string Description { get; }
    protected bool   AutoBegin { get; }
    protected UnityEngine.Sprite QuestIcon { get; }
    protected S1API.Quests.Constants.QuestState QuestState { get; }
    // internal string SaveFolder { get; }            → <saveslot>\Modded\Quests\

    public event System.Action OnComplete;
    public event System.Action OnFail;

    protected QuestEntry AddEntry(string title, System.Nullable<UnityEngine.Vector3> poiPosition = null);
    protected QuestEntry AddEntry(string title, S1API.Entities.NPC npc);

    public void Begin();  public void Complete();  public void Fail();
    public void Cancel(); public void Expire();    public void End();
}

public class QuestEntry
{
    public S1API.Quests.Constants.QuestState State { get; }
    public string Title { get; set; }
    public UnityEngine.Vector3 POIPosition { get; set; }
    public event System.Action OnComplete;
    public void Begin(); public void Complete();
    public void SetState(S1API.Quests.Constants.QuestState questState);
    public bool SetPOIToNPC<T>();
    public bool SetPOIToNPC(S1API.Entities.NPC npc);
    public bool SetPOIToSpraySurface(S1API.Graffiti.SpraySurface spraySurface);
    public void ClearPOI();
}

public static class QuestManager
{
    public static Quest CreateQuest<T>(string guid = null);
    public static Quest CreateQuest(System.Type questType, string guid = null);
    public static Quest GetQuestByGuid(string guid);
    public static Quest GetQuestByName(string questName);
    public static QuestWrapper Get<T>();
}

public sealed class QuestWrapper       // read-only view, also used for base-game quests
{
    public string Title { get; }
    public List<QuestEntry> QuestEntries { get; }
    public event System.Action OnComplete;
    public event System.Action OnFail;
}

public class QuestData { public QuestData(string className); public readonly string ClassName; }
```

**Reminder from the `SaveableField` XML doc: never name a saveable field `"QuestData"` — it collides.**

`S1API.Quests.Constants`: `QuestState` (enum), `QuestAction` (enum).

`S1API.Quests.Identifiers` (24): `IQuestIdentifier` (interface), `QuestNameAttribute`, and 22 marker
classes for base-game quests — `Botanists`, `Chemists`, `CleanCash`, `Cleaners`, `DealForCartel`,
`DefeatCartel`, `DodgyDealing`, `GearingUp`, `GettingStarted`, `KeepingItFresh`, `MakingTheRounds`,
`MixingMania`, `MoneyManagement`, `MovingUp`, `NeedingTheGreen`, `OnTheGrind`, `Packagers`, `Packin`,
`UnfavourableAgreements`, `Warehouse`, `WeNeedToCook`, `WelcomeToHylandPoint`. Use them as the `T` in
`QuestManager.Get<T>()` to hook base-game quest completion.

## 10. Everything else

### `S1API.Law` (11 types) — **bury the lede no longer: this is most of the Police mod**

The task brief did not list this namespace, but it is the single most relevant one for Police
Improvements. Full surface:

```csharp
public enum PursuitLevel : int { None = 0, Investigating = 1, Arresting = 2, NonLethal = 3, Lethal = 4 }
public enum CheckpointLocation : int { Western = 0, Docks = 1, NorthResidential = 2, WestResidential = 3 }

public static class LawManager
{
    public static int   DispatchOfficerCount { get; }
    public static float DispatchVehicleUseThreshold { get; }
    public static float SearchTimeInvestigating { get; }
    public static float SearchTimeArresting { get; }
    public static float SearchTimeNonLethal { get; }
    public static float SearchTimeLethal { get; }
    public static float EscalationTimeArresting { get; }
    public static float EscalationTimeNonLethal { get; }
    public static int   ActiveOfficerCount { get; }

    public static void CallPolice(S1API.Entities.Player target);
    public static bool IsPlayerWanted(S1API.Entities.Player target);
    public static bool IsUnderInvestigation(S1API.Entities.Player target);
    public static bool IsLethalForceAuthorized(S1API.Entities.Player target);
    public static PursuitLevel GetWantedLevel(S1API.Entities.Player target);
    public static void SetWantedLevel(S1API.Entities.Player target, PursuitLevel level);
    public static void EscalateWantedLevel(S1API.Entities.Player target);
    public static void DeescalateWantedLevel(S1API.Entities.Player target);
    public static void ClearWantedLevel(S1API.Entities.Player target);

    public static FootPatrolRoute[]    GetAllFootPatrolRoutes();
    public static VehiclePatrolRoute[] GetAllVehiclePatrolRoutes();
    public static FootPatrolRoute    FindFootPatrolRoute(string routeName);
    public static VehiclePatrolRoute FindVehiclePatrolRoute(string routeName);
    public static PatrolGroup StartFootPatrol(FootPatrolRoute route, int requestedMembers = 2);
    public static bool StartVehiclePatrol(VehiclePatrolRoute route);
    public static List<Il2CppScheduleOne.NPCs.NPC> GetAssignedOfficers(…);   // ⚠ see §1 leak list
}

public static class LawController
{
    public const int   MinIntensity = 1;
    public const int   MaxIntensity = 10;
    public const float DailyIntensityDrain = 0.05f;
    public static int   Intensity { get; }
    public static float InternalIntensity { get; }
    public static bool  IsUsingOverrideSettings { get; }
    public static void SetIntensityLevel(int level);
    public static void SetInternalIntensity(float intensity);
    public static void ChangeIntensity(float change);
    public static void ClearActivitySettingsOverride();
    public static void OverrideActivitySettings(Il2CppScheduleOne.Law.LawActivitySettings settings);  // ⚠ Il2Cpp leak
}

public static class CheckpointManager
{
    public static void EnableAllCheckpoints(int officersPerCheckpoint = 2);
    public static void DisableAllCheckpoints();
    public static void SetCheckpointEnabled(CheckpointLocation location, bool enabled, int requestedOfficers = 2);
    public static bool IsCheckpointEnabled(CheckpointLocation location);
    public static CheckpointInfo GetCheckpointInfo(CheckpointLocation location);
    public static List<CheckpointInfo> GetAllCheckpointInfo();
    public static UnityEngine.Vector3 GetCheckpointPosition(CheckpointLocation location);
    public static int GetAssignedOfficerCount(CheckpointLocation location);
    public static bool IsGate1Open(CheckpointLocation location);
    public static bool IsGate2Open(CheckpointLocation location);
    public static List<Il2CppScheduleOne.NPCs.NPC> GetAssignedOfficers(CheckpointLocation location);  // ⚠ Il2Cpp leak
}

public static class CurfewManager
{
    public const int HourBeforeCurfew = 2000;
    public const int WarningTime = 2030;
    public const int CurfewStartTime = 2100;
    public const int HardCurfewStartTime = 2115;
    public const int CurfewEndTime = 500;
    public static bool IsEnabled { get; }
    public static bool IsCurrentlyActive { get; }
    public static bool IsHardCurfewActive { get; }
    public static void EnableCurfew(); public static void DisableCurfew();
    public static bool IsWithinCurfewHours(); public static bool IsWithinHardCurfewHours();
    public static int MinutesUntilCurfew(); public static int MinutesUntilCurfewEnds();
}

public sealed class CheckpointInfo   { public CheckpointInfo(); public CheckpointLocation Location { get; }
                                       public bool IsEnabled { get; } public UnityEngine.Vector3 Position { get; }
                                       public int AssignedOfficerCount { get; }
                                       public bool IsGate1Open { get; } public bool IsGate2Open { get; }
                                       public bool AreBothGatesClosed { get; } public bool IsAnyGateOpen { get; }
                                       public bool IsOperational { get; } }

public sealed class FootPatrolRoute    { public string RouteName { get; } public int WaypointCount { get; }
                                         public int StartWaypointIndex { get; } public UnityEngine.Vector3 Position { get; }
                                         public UnityEngine.Vector3 GetWaypointPosition(int index); }
public sealed class VehiclePatrolRoute { /* identical shape, wraps Il2CppScheduleOne.NPCs.Behaviour.VehiclePatrolRoute */ }

public sealed class PatrolGroup      { public int MemberCount { get; } public int CurrentWaypoint { get; }
                                       public FootPatrolRoute Route { get; }
                                       public void AdvanceGroup(); public void DisbandGroup();
                                       public bool IsGroupReadyToAdvance(); public bool IsPaused(); }

public sealed class PlayerCrimeData  { public PursuitLevel CurrentPursuitLevel { get; }
                                       public UnityEngine.Vector3 LastKnownPosition { get; }
                                       public float TimeSinceSighted { get; }
                                       public bool BodySearchPending { get; } public bool EvadedArrest { get; }
                                       public void SetPursuitLevel(PursuitLevel level);
                                       public void Escalate(); public void Deescalate(); public void ClearCrimes();
                                       public float GetSearchTime();
                                       public void RecordLastKnownPosition(bool resetTimeSinceSighted = true); }
```

**Do not rewrite any of this.** Wanted-level control, pursuit escalation, law intensity, patrol
route discovery and spawning, checkpoint control and curfew are all done. What is *not* here:
creating new patrol routes, changing dispatch counts (the `LawManager` dispatch properties are
get-only), and per-officer pursuit AI — see §11.

### `S1API.Vehicles` (4 types) — relevant to Hireable Drivers

```csharp
public static class VehicleRegistry
{
    public static LandVehicle CreateVehicle(string vehicleCode);
    public static LandVehicle[] GetAll();
    public static LandVehicle GetByGUID(string guid);
    public static LandVehicle GetByName(string gameObjectName);
    public static void RemoveVehicle(string guidString);
}

public class LandVehicle                                   // wraps Il2CppScheduleOne.Vehicles.LandVehicle
{
    public LandVehicle(string vehicleCode);                // public ctor
    public float VehiclePrice { get; set; }
    public float TopSpeed { get; set; }
    public bool  IsPlayerOwned { get; set; }
    public bool  IsOccupied { get; set; }
    public VehicleColor Color { get; set; }
    public string GUID { get; }
    public S1API.Storages.StorageInstance Storage { get; }

    public event System.Action OnVehicleStart;
    public event System.Action OnVehicleStop;
    public event System.Action OnHandbrakeApplied;
    public event System.Action<UnityEngine.Collision> OnCollision;

    public void Spawn(UnityEngine.Vector3 position, UnityEngine.Quaternion rotation);
    public void DestroyVehichle();                         // [sic] — typo is in the API
    public void SetVisible(bool vis);
    public void ApplyColor(VehicleColor col);
    public void AlignTo(UnityEngine.Transform target, ParkingAlignment type, bool network = false);
    public void Park(S1API.Map.ParkingData parkData, bool network);
    public void ExitPark(bool moveToExitPoint = true);
}

public enum ParkingAlignment : int { FrontToKerb = 0, RearToKerb = 1 }
public enum VehicleColor : int { Black = 0, DarkGrey = 1, LightGrey = 2, White = 3, Yellow = 4, Orange = 5,
                                 Red = 6, DullRed = 7, Pink = 8, Purple = 9, Navy = 10, DarkBlue = 11,
                                 LightBlue = 12, Cyan = 13, LightGreen = 14, DarkGreen = 15, Custom = 16 }
```

Spawn / park / colour / ownership / storage / collision events are covered. **Driving is not** — no
throttle, steering, waypoint following, or "send this vehicle to that location". See §11.

### `S1API.Console` (4 types, 3 public)

```csharp
public abstract class BaseConsoleCommand
{
    protected BaseConsoleCommand();
    public string CommandWord { get; }
    public string CommandDescription { get; }
    public string ExampleUsage { get; }
    public abstract void ExecuteCommand(List<string> args);
}

public static class ConsoleItemAliases { public static void Register(string alias, string canonicalItemId); }

internal static class CustomConsoleRegistry           // ⚠ INTERNAL
{
    internal static void Register(BaseConsoleCommand command);
    internal static IReadOnlyDictionary<string, BaseConsoleCommand> RegisteredCommands { get; }
    internal static bool TryExecute(Il2CppSystem.Collections.Generic.List<string> args);
    internal static bool TryExecuteManaged(List<string> args);
}
```

**Confirmed dead end:** `BaseConsoleCommand` is `public abstract`, but the only registration path,
`CustomConsoleRegistry.Register`, is `internal`. **A third-party mod cannot register a console command
through S1API 3.1.4.** You'd patch `Il2CppScheduleOne.Console` yourself (or ask upstream to make
`Register` public — it's a one-word fix in the fork).

`ConsoleHelper` is a static grab-bag of 27 cheat operations that *do* work from outside:

```csharp
public static void AddItemToInventory(string itemCode, System.Nullable<int> quantity = null);
public static void ClearInventory();  public static void ClearTrash();
public static void RunCashCommand(int amount);  public static void RunOnlineBalanceCommand(int amount);
public static void GiveXp(int amount);  public static void GrowPlants();  public static void SaveGame();
public static void DiscoverProduct(string productCode);
public static void SetQuality(S1API.Products.Quality quality);
public static void SpawnVehicle(string vehicleCode);
public static void SetTime(string hhmm);
public static void RaiseWanted();  public static void LowerWanted();  public static void ClearWanted();
public static void SetLawIntensity(float intensity);
public static void SetNpcRelationship(string npcId, float level);
public static void SetNpcRelationship(S1API.Entities.NPC npc, float level);
public static void UnlockNpc(S1API.Entities.NPC npc);
public static void SetQuestState(string questName, S1API.Quests.Constants.QuestState state);
public static void SetPlayerHealth(float amount);
public static void SetPlayerJumpMultiplier(float multiplier);
public static void SetPlayerMoveSpeedMultiplier(float multiplier);
[Obsolete("Player energy was removed from newer game builds. This method is retained as a compatibility no-op where unavailable and may be removed in a future S1API version.")]
public static void SetPlayerEnergyLevel(float amount);
public static void Submit(string command);
public static void Submit(Il2CppSystem.Collections.Generic.IEnumerable<string> arguments);   // ⚠ Il2Cpp leak
```

`Submit(string)` is the escape hatch — it runs any console command by text, including ones S1API has no
wrapper for.

### `S1API.UI` (3 types) — and why it cannot build a settings screen

```csharp
public static class UIFactory { /* 18 uGUI builder helpers — see §8 */ }

public static class CharacterCreatorManager
{
    public static bool IsOpen { get; }
    public static S1API.Avatar.BasicAvatarSettings ActiveSettings { get; }
    public static event System.Action OnOpened;
    public static event System.Action OnClosed;
    public static event System.Action<S1API.Avatar.BasicAvatarSettings> OnCompleted;
    public static void Open(S1API.Avatar.BasicAvatarSettings initialSettings = null, bool showUI = true);
    public static void Close();  public static void Complete();
    public static string[] GetAvailablePresets();
    public static void SelectPreset(string presetName);
    public static void SetRigRotation(float normalizedValue);
    public static void PreRegisterAsActiveUI();
}

public sealed class MainMenuRig                      // wraps Il2CppScheduleOne.UI.MainMenu.MainMenuRig
{
    public S1API.Avatar.Avatar Avatar { get; }
    public static MainMenuRig[] FindInScene(bool includeInactive = false);
}
```

That's the whole namespace. `MainMenuRig` exposes **only the avatar** on the main-menu rig — no
canvas, no tab list, no settings-panel injection point. `UIFactory` emits legacy `UnityEngine.UI.Text`
rather than TextMeshPro. **A native-looking main-menu settings screen is not buildable through
`S1API.UI`** — see §11.

### `S1API.Dialogues` (5 types)

`DialogueInjector`, `DialogueInjection`, `DialogueChoiceListener`, `DialogueChoicePaging`,
`DialogueChoicePagingOptions`. Headline: inject new dialogue nodes and choices into existing NPC
dialogue trees, with automatic paging when the choice list gets long. Note
`DialogueInjector`/`DialogueChoiceListener` `Register(Il2CppScheduleOne.Dialogue.DialogueHandler, string, Action)`
is an Il2Cpp leak (§1). Per-NPC authoring is `S1API.Entities.Dialogue.DialogueContainerBuilder` /
`DialogueDatabaseBuilder`, plus `S1API.Entities.NPCDialogue` on each NPC.

### `S1API.Cutscenes` (6 types)

`CutsceneManager`, `CutsceneBuilder`, `CutsceneFrame`, `CutsceneCamera`, `CutsceneHandle`,
`CutsceneEndReason`. Headline: script camera-framed cutscenes fluently and get a handle to await/cancel.

### `S1API.Doors` (3 types)

`DoorController`, `DoorAccess` (enum), `DoorSide` (enum). Headline: open/close/lock doors and query
which side you're on.

### `S1API.Building` (3 types)

`BuildManager`, `BuildEvents`, `BuildEventArgs`. Headline: place items on grids and surfaces, and
subscribe to build/destroy events. `CreateGridItem` / `CreateSurfaceItem` take
`Il2CppScheduleOne.Tiles.Grid` / `Il2CppScheduleOne.Building.Surface` — Il2Cpp leaks (§1).

### `S1API.Conditions` (1 type)

`SystemTriggerEntry` — a single condition record, used with `S1API.PhoneCalls.Constants.SystemTriggerType`
and `EvaluationType` to gate scripted phone calls.

### `S1API.AssetBundles` (3 types)

`AssetLoader`, `WrappedAssetBundle`, `WrappedAssetBundleRequest`. Headline: load Unity asset bundles
shipped with your mod. The `Il2CppSystem.Type` overloads (`Load`, `LoadAsset`, `LoadAllAssets`,
`LoadAssetWithSubAssets`, `LoadAssetAsync`) are Il2Cpp leaks (§1); prefer the generic/typed overloads
if they exist. **UNVERIFIED:** whether non-`Il2CppSystem.Type` overloads are available.

### `S1API.Cartel` (5 types)

`Cartel`, `CartelGoon`, `CartelInfluence`, `CartelStatus` (enum), `GoonManager`. Headline: read and
manipulate cartel influence/status and cartel goons. `CartelStatus` handling exposes
`Il2Cpp.ECartelStatus` in one signature (§1).

### `S1API.Casino` (2 types)

`SlotMachineHelper`, `GamblingSessionMode` (enum). Headline: find and drive slot machines.
`FindNearestSlotMachine` returns `Il2CppScheduleOne.Casino.SlotMachine` — Il2Cpp leak (§1).

### `S1API.Entities.Schedule` (16 types, 15 public) — NPC daily routines

`PrefabScheduleBuilder` plus 12 `IScheduleActionSpec` implementations, all set up inside
`builder.WithSchedule(...)` at prefab time: `WalkToSpec`, `SitSpec`, `StayInBuildingSpec`,
`DriveToCarParkSpec`, `LocationBasedActionSpec` (+ `LocationBasedActionSpecBuilder`),
`LocationDialogueSpec`, `UseATMSpec`, `UseSlotMachineSpec`, `UseVendingMachineSpec`,
`HandleDealSpec`, `EnsureDealSignalSpec`, plus `LocationArriveBehaviour` (enum) and internal
`NPCDestinationContainer`. Runtime access is `npc.Schedule` (`S1API.Entities.NPCSchedule`).

**`DriveToCarParkSpec` is the closest thing to a driver route in the whole library** — it makes an NPC
drive to a car park as a scheduled action. Worth prototyping against before writing vehicle AI (§11).

### Mod lifecycle, logging and events

```csharp
public class S1API.Logging.Log
{
    public Log(string sourceName);
    public void Msg(string message);  public void Warning(string message);
    public void Error(string message); public void BigError(string message);
}
```

`S1API.Lifecycle.GameLifecycle` is the managed event hub for load/save/scene transitions — see §15 for
its full member list. `S1API.Utils.EventHelper` (and the `[Obsolete]`
`S1API.Internal.Abstraction.EventHelper`, marked *"Use S1API.Utils.EventHelper instead"*) wrap Unity
`UnityEvent` / `EventTrigger` subscription with plain `System.Action`s so you can unsubscribe cleanly.

### Remaining namespaces in one line each

| Namespace | Headline capability |
|---|---|
| `S1API.GameTime` | `TimeManager` + `GameDateTime` struct + `Day` enum; read/set game clock. Ctors and `ToS1()` are Il2Cpp leaks (§1) |
| `S1API.Graffiti` | `GraffitiManager`, `GraffitiEvents`, `SpraySurface`; spray surfaces are valid quest POIs |
| `S1API.Growing` | Plants, `SeedDefinition`/`SeedInstance`/`SeedCreator`, mushroom beds, shroom colonies, grow-container additives |
| `S1API.Input` | `Controls`, `ButtonCode`, `InputDeviceType`; poll input device-agnostically |
| `S1API.Leveling` | `LevelManager`, `Rank` enum, `FullRank` struct, `Unlockable`; read/grant XP and rank unlocks |
| `S1API.Map` | `Region` enum, `Building`, `DeliveryLocation`, parking lots/spots, `MapPOI(+Builder/+Manager)`; add custom map markers |
| `S1API.Map.Buildings` / `.DeliveryLocations` / `.ParkingLots` | 77 / 48 / 22 marker types for type-safe lookup of every base-game location |
| `S1API.Messaging` | `Response` — a text-message reply option with a callback, used by `NPC.SendTextMessage` |
| `S1API.Misc` | `ModularSwitch` — a generic in-world switch |
| `S1API.PhoneCalls` | `CallManager`, `PhoneCallDefinition` (abstract), `CallerDefinition`, `CallStageEntry`; author scripted incoming calls. `S1PhoneCallData` is an Il2Cpp leak (§1) |
| `S1API.Properties` / `.Interfaces` / `.Tokens` | Custom product effects: `CustomEffectBuilder`, `EffectCreator`, `PropertyBase`, + 36 base-game effect marker types |
| `S1API.Property` | Owned real estate: `PropertyManager`, `BusinessManager`, `PropertyWrapper`, `BusinessWrapper`, `LaunderingOperation`, `BaseProperty`. Ctors are Il2Cpp leaks (§1) |
| `S1API.Rendering` | Runtime asset synthesis: `IconFactory`, `MaterialHelper`, `TextureUtils`, `AvatarLayerFactory`, `AccessoryFactory`, `RuntimeResourceRegistry` |
| `S1API.Shops` | `ShopManager`, `Shop`; enumerate shops and listings |
| `S1API.Stations` | Chemistry-station recipe authoring: `ChemistryStationRecipeBuilder`, ingredients, products, temperature, `QualityCalculationMethod` |
| `S1API.Storage` / `S1API.Storages` | Two namespaces: `Storage` is the entity + events (`StorageEntity`, `StorageEvents`), `Storages` is the manager + instance + `StorageAccessSettings` |
| `S1API.Trash` | `TrashManager` |
| `S1API.TVApp` | `TVApp` (abstract) — the `PhoneApp` pattern applied to the in-game TV |
| `S1API.Utils` | `ArrayExtensions`, `ButtonUtils`, `ColorUtils`, `EventHelper`, `ImageUtils`, `RandomUtils`, `ReflectionUtils`, `ToggleUtils`, `TransformUtils` |

## 11. Gaps — what the team must build against the game assemblies directly

Everything below is **absent from S1API 3.1.4's public surface**, verified by the namespace/type
index. Build it against `Assembly-CSharp` (`Il2CppScheduleOne.*`) + Harmony, and check §23 first so
you don't collide with S1API's own 40 patch classes.

### Hireable Drivers

| Gap | Why S1API can't do it | Where to go instead |
|---|---|---|
| **Custom employee types** | `S1API.Entities.Employees` is 2 types, appearance-only (§6). No wrapper for the game's `Employee` hierarchy at all. | `Il2CppScheduleOne.Employees.*` — subclass or patch `Employee` / `EmployeeManager` directly. Consider modelling a driver as an S1API `NPC` + `NPCDealer` instead, which gets you recruitment, cash, assignment and persistence free. |
| **Hire / fire / wage / payday** | No API. `NPCDealer.RecruitDealer()`, `.GetCash()`, `.ChangeCash()`, `.CollectCash()` and `DealerDataBuilder.WithSigningFee/WithCut` are the *only* employment-shaped primitives (§5). | `Il2CppScheduleOne.Employees.EmployeeManager` + the game's payday logic. |
| **Custom employee behaviour / task loops** | `S1API.Entities.Behaviour` has exactly one type, `CombatBehaviour`. There is no `Behaviour` authoring API. | `Il2CppScheduleOne.NPCs.Behaviour.*`. `NPCPrefabBuilder` privately touches `Behaviour` / `NPCBehaviour` but exposes nothing. |
| **Transit routes** | No route-authoring API anywhere. `S1API.Law.FootPatrolRoute` / `VehiclePatrolRoute` are **read-only wrappers over existing routes** — `RouteName`, `WaypointCount`, `GetWaypointPosition(i)`, no constructor, no waypoint mutation. | `Il2CppScheduleOne.NPCs.Behaviour.VehiclePatrolRoute` / `FootPatrolRoute` — build routes as GameObjects yourself. |
| **Vehicle AI / driving** | `S1API.Vehicles.LandVehicle` covers spawn, park, colour, visibility, ownership, storage, collision events — **not** throttle, steering, pathing, or "drive to X" (§10). | `Il2CppScheduleOne.Vehicles.*` + the game's NavMesh/driving components. **First try `S1API.Entities.Schedule.DriveToCarParkSpec`** — it is the one shipped primitive that makes an NPC drive somewhere. |
| **Passenger / seat occupancy control** | `NPC.IsInVehicle` and `NPC.CurrentVehicle` are read-only; `LandVehicle.IsOccupied` is settable but that's a flag, not a seating API. `S1API.Avatar.Seat` is scene-registry only. | `Il2CppScheduleOne.Vehicles.*` seat components. |

### Police Improvements

| Gap | Why S1API can't do it | Where to go instead |
|---|---|---|
| **Police intensity — partially covered** | `S1API.Law.LawController.SetIntensityLevel(1..10)`, `SetInternalIntensity(float)`, `ChangeIntensity(float)`, `ClearActivitySettingsOverride()` all work (§10). **`OverrideActivitySettings(Il2CppScheduleOne.Law.LawActivitySettings)` does not** — it's a Mono/IL2CPP signature leak (§1). | Call `Il2CppScheduleOne.Law.LawController` directly for the override, or build the `LawActivitySettings` and set it via reflection. |
| **Dispatch tuning** | `LawManager.DispatchOfficerCount`, `DispatchVehicleUseThreshold`, all four `SearchTime*` and both `EscalationTime*` are **get-only**. | Patch or reflect into `Il2CppScheduleOne.Law.LawManager`. |
| **Per-officer pursuit AI** | Only `CombatBehaviour` (`GiveUpRange`, `GiveUpTime`, weapon selection) and `NPC.Aggressiveness` are exposed. No pursuit state machine, no search patterns, no backup calls. | `Il2CppScheduleOne.NPCs.Behaviour.*` pursuit behaviours. |
| **New patrol routes / new checkpoints** | Both are read-only enumerations. `CheckpointLocation` is a fixed 4-value enum, so you cannot add a fifth checkpoint through S1API. | `Il2CppScheduleOne.Law.CheckpointManager` + scene GameObjects. |
| **Spawning brand-new officers** | Actually **not a gap** — subclass `S1API.Entities.NPC`, `builder.WithAppearanceDefaults(a => a.WithAccessoryLayer<Head>(Head.PoliceCap, …))`, give it a `CombatBehaviour` weapon, and S1API spawns and persists it (§3, §4). | — |

### Special Customers

| Gap | Why S1API can't do it | Where to go instead |
|---|---|---|
| **Custom customers** | **Not a gap.** `NPC` + `EnsureCustomer()` + `CustomerDataBuilder` (19 setters) + `NPCCustomer` runtime + `ContractInfoBuilder` is a complete pipeline (§3, §5). | — |
| **Per-save mod data** | **Not a gap.** `Saveable` + `[SaveableField]` (§7). | — |
| **New customer *archetypes* beyond `CustomerData`'s fields** | `CustomerData` is a fixed `ScriptableObject` shape; anything not in its 17 properties has no home. | Store it in your own `Saveable` keyed by NPC id, and apply it in `OnCreated()`. |

### All three mods

| Gap | Why S1API can't do it | Where to go instead |
|---|---|---|
| **Native-looking main-menu settings screen** | `S1API.UI` is 3 types. `MainMenuRig` exposes only `Avatar`; there is no canvas/tab/panel injection point, and `UIFactory` emits legacy `UnityEngine.UI.Text`, not the game's TextMeshPro (§10). | `Il2CppScheduleOne.UI.MainMenu.*` — find the settings canvas, clone an existing tab prefab so fonts/styling match, and Harmony-postfix its `Start`. Use `S1API.PhoneApp` (§8) if an in-game phone app is an acceptable substitute — that path *is* fully supported. |
| **Console commands** | `BaseConsoleCommand` is public but `CustomConsoleRegistry.Register` is internal (§10). | Patch `Il2CppScheduleOne.Console` yourself, or use `ConsoleHelper.Submit(string)` to *invoke* commands (which works fine). |
| **Mod settings persistence** | S1API has no preferences API for mods — `S1APIPreferences` is internal and S1API-only. | `MelonLoader.MelonPreferences` (already referenced by the csproj) for global settings; `Saveable` (§7) for per-save settings. |
| **Any S1API member taking/returning an `Il2Cpp*` type** | Mono-vs-IL2CPP signature mismatch (§1). | Call the game assembly directly for that specific member. |

## 12. Version stability

### Installed 3.1.4 vs available 3.1.7

`nuget.org` currently lists 80 versions of `S1API.Forked`, ending `3.1.4`, `3.1.5`, `3.1.6`, `3.1.7`.
You are **three patch releases behind**. The runtime DLL in `Mods\` and the NuGet reference must stay
in lockstep — both are named `S1API` and the loader picks the one in `Mods\`, so bumping the
`PackageReference` without updating the installed loader payload gives you a compile that binds
against members the runtime doesn't have. Treat the S1API version as **one number, changed in two
places at once**.

Release cadence is fast (80 releases, ~30 of them in the 2.x line alone), which cuts both ways: fixes
land quickly, and so do breaking changes.

### Thin wrappers — as fragile as the game API underneath

These hold no state and no logic; they are a `.` away from the game type. When the game updates, they
break exactly when the game type breaks, and S1API buys you nothing but nicer names.

- `S1API.Avatar.*` — `Avatar`, `AvatarSettings`, `BasicAvatarSettings`, `Seat` all hold an
  `internal readonly Il2CppScheduleOne…` field and forward every property.
- `S1API.Law.*` — every manager is `static … Internal { get; }` over the game's singleton plus
  pass-through methods. `CheckpointLocation`'s 4 values and `PursuitLevel`'s 5 are mirrors of game enums.
- `S1API.Vehicles.LandVehicle`, `S1API.Money.Money`, `S1API.GameTime.*`, `S1API.Property.*`,
  `S1API.Shops.*`, `S1API.Storages.*`, `S1API.Deliveries.*`, `S1API.Casino.*`, `S1API.Cartel.*`,
  `S1API.Economy.Contract`.
- `S1API.Entities.Actions.*` (4 types × `Begin`/`End`/`IsActive` over one game component each).
- `S1API.Entities.NPCCustomer` / `NPCDealer` / `NPCSupplier` — forwarders over
  `Il2CppScheduleOne.Economy.Customer` / `Dealer` / `Supplier`.
- All the marker-type namespaces (`Map.Buildings`, `Map.DeliveryLocations`, `Map.ParkingLots`,
  `Properties.Tokens`, `Quests.Identifiers`, `DeadDrops.Native`, `Entities.NPCs.*`) and all the
  `Appearances.*` `const string` path tables — these are **data**, and they rot silently when the game
  renames an asset. The `[Obsolete]` on `HairStyle.Jesus` (*"removed in game version 0.4.6 and now
  resolves to no hair"*) is the proof that this happens and that the fork does track it.

### Real encapsulation — worth depending on

These implement substantial logic S1API owns. They survive game updates better because S1API absorbs
the churn, and rewriting them would cost you real weeks.

- **`Saveable` / `SaveableField` / `SaveableAutoRegistry` / `GenericSaveablesPatches`** (§7) —
  assembly scanning, reflection-driven JSON round-trip, load ordering, file lifecycle, three Harmony
  hooks, and a second `DynamicSaveData` path. **The single highest-value component in the library.**
- **`NPC` + `NPCPrefabBuilder` + `NPCPrefabIdentity` + `NPCPatches`** (§3) — prefab synthesis,
  FishNet spawnable pre-registration, save-order-aware instantiation, late-added-NPC handling,
  client-side wrapper creation, identity re-application before `Awake`. Hundreds of lines of very
  fiddly IL2CPP + networking code.
- **`PhoneApp`** (§8) — icon cloning from the live `HomeScreen`, panel construction, exit-delegate
  bridging, hover handling via an injected `MonoBehaviour`.
- **`S1API.Products` internals** (42 internal types behind 51 public ones) — custom drug definitions,
  mixing profiles, packaging, presentation, multiplayer sync, its own save provider registry.
- **`NPCAppearance`** — the `Set<T>` dispatch table, mugshot generation and its queue.
- **`Quest`** (§9) — quest state persistence and base-game quest bridging.
- **`S1API.Entities.Schedule`** — 12 schedule action specs compiled into game schedule components at
  prefab time.

### Practical rule

Depend freely on the encapsulated parts; treat the thin wrappers as a convenience you can drop to the
game assembly for at any time. Since a chunk of the thin-wrapper surface leaks `Il2Cpp*` types anyway
(§1), you'll be going direct for some of them regardless.

## 13. Open questions / unverified

Ordered by how much it would hurt to be wrong.

1. **The Mono/IL2CPP signature mismatch is inferred, not observed.** The two-flavour evidence is
   solid (0 vs 194 `Il2CppScheduleOne` refs, §1), and the conclusion that a member typed
   `ScheduleOne.X` at compile time cannot bind to `Il2CppScheduleOne.X` at runtime follows from how
   .NET resolves members. But I did not launch the game, so I never saw the `MissingMethodException`.
   **Test this first with a one-line call to `S1API.Law.CheckpointManager.GetAssignedOfficers(...)`** —
   it decides whether the §1 leak list is a real constraint or a false alarm.
2. **Exact file naming inside `Modded\Saveables\`.** Both `Modded\Saveables\` folders on disk are
   empty, so nothing was observed. `<SaveName>.json` flat is the strong inference (the
   `[SaveableField("…")]` string is the only name input, and the sibling literal
   `Modded/CustomProducts.json` matches that shape), but it is not confirmed.
3. **Whether `SaveInternal`/`LoadInternal` or `SaveToDynamic`/`LoadFromDynamic` actually runs in 3.1.4.**
   Both paths exist and both are internal. `NPCPatches.RebuildPendingCustomNpcTypes` mentions
   "when using NPCs.json" and `IsLikelyCustomDynamicSaveData(DynamicSaveData)` exists, implying the
   game has both a per-file and a consolidated format and S1API branches on it. Which branch you get
   may depend on game version.
4. **The `IsDirectSaveableInheritor` constraint.** The method name says a `Saveable` subclass must
   inherit *directly* to be auto-registered, which would break `Saveable → MyBase → MyRoster`. I could
   not read the method body. If you want a shared base class for mod saveables, verify this first.
5. **`NPCAppearance.Set<T>` / `With*Layer<T>` generic constraints.** The dumps record
   `Set<T>(System.Object)` with no constraint. The 14 `<.cctor>b__33_*` closures strongly imply
   `T` ∈ the 14 `CustomizationFields` types, and `GetConstPaths<T>()` on the four `Base` classes
   implies `T` ∈ the path-constant classes for the layer methods — but the compiler-enforced
   constraint (if any) is unknown. Same for
   `NPCPrefabBuilder.AvatarDefaultsBuilder.With{Face,Body,Accessory}Layer<T>`.
6. **`OnContractAssigned`'s four arguments.** `event Action<float, int, int, int>` on `NPCCustomer`
   with no parameter names in the dump and no XML doc found. Log them at runtime before relying on them.
7. **`PhoneApp.IconFileName` resolution.** Which directory the filename is resolved against (mod
   folder? `UserData`? game root?) is not recorded anywhere I could read. Use `IconSprite` /
   `SetIconTexture` if you want certainty.
8. **`S1API.AssetBundles` non-`Il2CppSystem.Type` overloads.** I only enumerated the members that
   *match* an `Il2Cpp` filter, so I know the `Il2CppSystem.Type` overloads exist but did not confirm
   whether generic `LoadAsset<T>()` equivalents also exist.
9. **`S1API.DeadDrops.Native` marker shape.** I confirmed the 25 type names and that
   `IDeadDropIdentifier` + `DeadDropGuidAttribute` exist, but did not dump an individual `Native` type
   to confirm each one actually implements the interface and carries the attribute.
10. **`S1API.Entities.Schedule.DriveToCarParkSpec` capability.** Flagged in §10/§11 as the most
    promising shipped primitive for Hireable Drivers, but I only have its type name — no member dump,
    no doc comment. Worth 15 minutes with `type.ps1` before designing vehicle AI from scratch.
11. **Whether 3.1.5 / 3.1.6 / 3.1.7 change any of the above.** Everything here describes 3.1.4 only.
    In particular, `CustomConsoleRegistry.Register` being internal (§10) is exactly the kind of thing
    a patch release fixes.
12. **`S1API.Avatar.Avatar.ApplySettings(...)`** — named in the research brief but **not present** in
    3.1.4. The method is `LoadAvatarSettings(AvatarSettings)`. Either the brief was working from a
    different version, or from the pre-fork upstream S1API.
13. **XML doc coverage.** `S1API.xml` is 1.25 MB and I mined it targetedly (NPC, prefab builder,
    saveables, quests). Namespaces I documented purely from the reflection dumps — `S1API.Law`,
    `S1API.Vehicles`, `S1API.Products`, `S1API.Items`, most of §10 — very likely have doc comments I
    never read. **If you're about to use one of those in anger, grep `S1API.xml` for it first.**

---

# Part II — parallel assembly-verified pass (preserved)

> Written by a second agent directly from the 100 `research/raw/ns/ns-S1API.*.txt` namespace dumps
> and `research/raw/00-assemblies.txt`. Kept verbatim; only the section numbers were shifted
> (its §1 → §14, §12 → §25) so they don't collide with Part I. Its headline contribution is the
> **Harmony patch collision map** (§23), which Part I does not duplicate.

---

## 14. How S1API is wired in

```csharp
public class S1API.S1API : MelonLoader.MelonMod
{
    public override void OnInitializeMelon();
    public override void OnDeinitializeMelon();
    public override void OnUpdate();
    public override void OnGUI();
    public override void OnSceneWasLoaded(int buildIndex, string sceneName);
    public override void OnSceneWasInitialized(int buildIndex, string sceneName);
    public override void OnSceneWasUnloaded(int buildIndex, string sceneName);
}
```

It is a plain `MelonMod`, so MelonLoader load order applies. Internal state is
reset per scene by `S1API.Internal.Lifecycle.SceneStateCleaner.ResetForSceneChange`.
Its own preferences (`S1API.Internal.S1APIPreferences`) are three
`MelonPreferences_Entry<bool>`: `EnableMugshotLoadingScreen`,
`EnableUnityNullReferenceTraceLogging`, `EnableVerboseLogging`.

Logging:

```csharp
public class S1API.Logging.Log
{
    public Log(string sourceName);
    public void Msg(string message);
    public void Warning(string message);
    public void Error(string message);
    public void BigError(string message);
}
```

---

## 15. Lifecycle — managed events over the game's `UnityEvent`s

```csharp
public static class S1API.Lifecycle.GameLifecycle
{
    public static event System.Action OnPreLoad;
    public static event System.Action OnLoadComplete;
    public static event System.Action OnPreSceneChange;
    public static event System.Action OnSaveInfoLoaded;
    public static event System.Action OnSaveStart;
    public static event System.Action OnSaveComplete;
}
```

These are plain managed `System.Action` events that mirror
`LoadManager.onPreLoad / onLoadComplete / onPreSceneChange / onSaveInfoLoaded`
and `SaveManager.onSaveStart / onSaveComplete`. **Use these instead of wiring
`UnityAction`s onto the game's `UnityEvent`s yourself** — you get normal `+=` /
`-=`, no interop-delegate lifetime problem, and S1API already handles
re-binding across scene reloads.

Time events are the same story:

```csharp
public static class S1API.GameTime.TimeManager
{
    public static System.Action      OnHourPass;
    public static System.Action      OnDayPass;
    public static System.Action      OnWeekPass;
    public static System.Action      OnSleepStart;
    public static System.Action<int> OnSleepEnd;     // int = minutes skipped
    public static System.Action      OnTick;

    public static Day   CurrentDay      { get; }
    public static int   ElapsedDays     { get; }
    public static int   CurrentTime     { get; }     // 24h integer, e.g. 1530
    public static bool  IsNight         { get; }
    public static bool  IsEndOfDay      { get; }
    public static bool  SleepInProgress { get; }
    public static float NormalizedTime  { get; }
    public static float Playtime        { get; }

    public static void   SetTime(int time24h);
    public static int    Get24HourTimeFromMinutes(int minutes);
    public static int    GetMinutesFrom24HourTime(int time24h);
    public static string GetFormatted12HourTime();
    public static bool   IsCurrentTimeWithinRange(int startTime24h, int endTime24h);
}

public struct S1API.GameTime.GameDateTime
{
    public int ElapsedDays;
    public int Time;
    public GameDateTime(int elapsedDays, int time);
    public GameDateTime(int minSum);
    public GameDateTime(Il2CppScheduleOne.GameTime.GameDateTime gameDateTime);
    public GameDateTime AddMinutes(int minutes);
    public int    GetMinSum();
    public bool   IsNightTime();
    public bool   IsSameDay(GameDateTime other);
    public string GetFormattedTime();
    public Il2CppScheduleOne.GameTime.GameDateTime ToS1();
}

public enum S1API.GameTime.Day { Monday=0, ..., Sunday=6 }
```

Internally it uses `S1API.Internal.Lifecycle.TimeManagerShim` to add/remove
`Il2CppSystem.Action` delegates on the real `TimeManager` and to survive scene
reloads (`TryBindToCurrentInstance` / `ResetBindings`).

Also, the generic `UnityEvent` bridge, if you need one for a game event S1API
doesn't wrap:

```csharp
public static class S1API.Utils.EventHelper
{
    public static void AddListener(System.Action listener, UnityEngine.Events.UnityEvent unityEvent);
    public static void AddListener<T>(System.Action<T> listener, UnityEngine.Events.UnityEvent<T> unityEvent);
    public static void RemoveListener(System.Action listener, UnityEngine.Events.UnityEvent unityEvent);
    public static void RemoveListener<T>(System.Action<T> listener, UnityEngine.Events.UnityEvent<T> unityEvent);
    public static void AddEventTrigger(UnityEngine.EventSystems.EventTrigger trigger,
                                       UnityEngine.EventSystems.EventTriggerType eventType,
                                       System.Action listener);
    public static void RemoveEventTrigger(...);
}
```

(`S1API.Internal.Abstraction.EventHelper` is the same API but marked
`[Obsolete("Use S1API.Utils.EventHelper instead...")]` — use the `Utils` one.)

---

## 16. Save data (parallel pass — see also §7)

Covered in depth in `research/API-PERSISTENCE-TIME.md` §1.9. Summary of the
verified surface:

```csharp
public abstract class S1API.Internal.Abstraction.Saveable : Registerable, ISaveable
{
    public SaveableLoadOrder LoadOrder { get; }
    protected virtual void OnLoaded();
    protected virtual void OnSaved();
    public static bool RequestGameSave();
    public static bool RequestGameSave(bool immediate);

    internal virtual void LoadInternal(string folderPath);
    internal virtual void SaveInternal(string folderPath, ref Il2CppSystem.Collections.Generic.List<string> extraSaveables);
    internal void SaveToDynamic(Il2CppScheduleOne.Persistence.Datas.DynamicSaveData d);
    internal void LoadFromDynamic(Il2CppScheduleOne.Persistence.Datas.DynamicSaveData d);
}

[AttributeUsage(AttributeTargets.Field)]
public class S1API.Saveables.SaveableField : Attribute
{
    public SaveableField(string saveName);
    internal string SaveName { get; }
}

public enum S1API.Saveables.SaveableLoadOrder { BeforeBaseGame = 0, AfterBaseGame = 1 }

public abstract class S1API.Internal.Abstraction.Registerable : IRegisterable
{
    protected virtual void OnCreated();
    protected virtual void OnDestroyed();
}
```

Discovery is automatic — `S1API.Saveables.SaveableAutoRegistry` reflects over
loaded assemblies for direct `Saveable` inheritors
(`IsDirectSaveableInheritor(Type)`), instantiates one of each, and caches it.
You do not register anything.

Serialization is **Newtonsoft.Json** (`ISaveable.SerializerSettings` is a
`JsonSerializerSettings`; there is a `GUIDReferenceConverter : JsonConverter`
and an `IGUIDReference { string GUID { get; } }` marker), so real object graphs
work — unlike the game's `JsonUtility`-shaped `SaveData` classes.

---

## 17. Entities / NPCs (non-authoring surface)

```csharp
public abstract class S1API.Entities.NPC : Saveable, IEntity, IHealth
{
    public static readonly List<NPC> All;
    public static NPC  Get(string npcId);
    public static bool CustomNpcsReady { get; }

    public string ID        { get; protected set; }
    public string FirstName { get; set; }
    public string LastName  { get; set; }
    public string FullName  { get; }
    public UnityEngine.Sprite Icon { get; set; }
    public UnityEngine.GameObject gameObject { get; }
    public UnityEngine.Vector3 Position  { get; set; }
    public UnityEngine.Transform Transform { get; }
    public S1API.Map.Region Region { get; set; }
    public float Scale { get; set; }
    public float Aggressiveness { get; set; }
    public bool  RequiresRegionUnlocked { get; set; }
    public bool  ConversationCanBeHidden { get; set; }

    public bool IsConscious  { get; }
    public bool IsInBuilding { get; }
    public bool IsInVehicle  { get; }
    public bool IsPanicking  { get; }
    public bool IsUnsettled  { get; }
    public bool IsVisible    { get; }
    public bool IsPhysical   { get; }
    public bool IsDealer     { get; }
    public bool IsSupplier   { get; }
    public bool IsKnockedOut { get; }
    public bool IsDead       { get; }
    public bool IsInvincible { get; set; }
    public float CurrentHealth { get; }
    public float MaxHealth     { get; set; }
    public S1API.Vehicles.LandVehicle CurrentVehicle { get; }

    // sub-facades
    public NPCAppearance   Appearance     { get; }
    public NPCMovement     Movement       { get; }
    public NPCDialogue     Dialogue       { get; }
    public NPCSchedule     Schedule       { get; }
    public NPCInventory    Inventory      { get; }
    public NPCCustomer     Customer       { get; }
    public NPCDealer       Dealer         { get; }
    public NPCSupplier     Supplier       { get; }
    public NPCRelationship Relationship   { get; }
    public NPCMessaging    Messaging      { get; }
    public Behaviour.CombatBehaviour CombatBehaviour { get; }
    public Actions.NPCSmoking       Smoking       { get; }
    public Actions.NPCSprayPainting SprayPainting { get; }
    public Actions.NPCDrinking      Drinking      { get; }
    public Actions.NPCItemHolding   ItemHolding   { get; }

    public event System.Action OnDeath;
    public event System.Action OnInventoryChanged;

    public void KnockOut();
    public void StopPanicking();
    public void SendTextMessage(string message, S1API.Messaging.Response[] responses = null,
                                float responseDelay = 1, bool network = true);
    public void SetEquippable(string assetPath);
    public void SetEquippable(Equippables.EquippablePath equippablePath);

    internal readonly Il2CppScheduleOne.NPCs.NPC S1NPC;   // escape hatch (internal!)
}

public class S1API.Entities.NPCMovement
{
    public void SetDestination(UnityEngine.Vector3 position);
    public void Warp(UnityEngine.Vector3 position);
    public void FaceDirection(UnityEngine.Vector3 forward);
    public void FacePoint(UnityEngine.Vector3 position);
    public void AddSpeedControl(string id, int priority, float speed);
    public void RemoveSpeedControl(string id);
    public void Stop();
}

public sealed class S1API.Entities.NPCRelationship
{
    public void Add(float delta, bool network = true);
    public void SetUnlockType(NPCRelationship.UnlockType type);
}

public sealed class S1API.Entities.NPCDialogue
{
    public void PlayReaction(string key, float durationSeconds = -1, bool network = false);
    public void ShowWorldText(string text, float durationSeconds);
    public void StopOverride();
}

public sealed class S1API.Entities.NPCDealer
{
    public List<NPC> GetAssignedCustomers();
    public void RemoveCustomer(NPC customer);
}

public sealed class S1API.Entities.NPCCustomer
{
    public void SetAwaitingDelivery(bool awaiting);
    public void SetupDialog();
}

public sealed class S1API.Entities.NPCSchedule
{
    public IReadOnlyList<string> GetActionNames();
    public void SetCurfewMode(bool enabled);
}
```

Also in this family: `NPCAppearance`, `NPCInventory`, `NPCMessaging`,
`NPCSupplier`, `NPCPrefabBuilder` (the custom-NPC authoring entry point — see
the sibling doc), `RandomInventoryItemsBuilder`, `SupplierStatus` enum, and
sub-namespaces `Entities.Actions`, `.Appearances(.AccessoryFields /
.BodyLayerFields / .CustomizationFields / .FaceLayerFields / .Base)`,
`.Behaviour`, `.Customer`, `.Dealer`, `.Dialogue`, `.Employees`,
`.Equippables`, `.Impostors`, `.Interfaces`, `.Relation`, `.Schedule`,
`.Supplier`, `.Voices`, and pre-baked identifier namespaces
`.NPCs.{Docks,Downtown,Northtown,PoliceOfficers,Suburbia,Uptown,Westville}`.

```csharp
public class S1API.Entities.Player : IEntity, IHealth
{
    public static readonly List<Player> All;
    public static Player Local { get; }
    public bool IsLocal { get; }
    public string Name { get; }
    public UnityEngine.Vector3 Position { get; set; }
    public float Scale { get; set; }
    public S1API.Law.PlayerCrimeData CrimeData { get; }
    public float CurrentHealth { get; }
    public float MaxHealth { get; set; }
    public bool  IsDead, IsInvincible, IsInVehicle, Crouched, IsReadyToSleep,
                 IsSkating, IsSleeping, IsRagdolled, IsArrested, IsTased, IsUnconscious;
    public S1API.Vehicles.LandVehicle LastDrivenVehicle { get; }
    public float TimeSinceVehicleExit { get; }
    public S1API.Property.PropertyWrapper CurrentProperty { get; }
    public S1API.Property.PropertyWrapper LastVisitedProperty { get; }
    public S1API.Map.Region CurrentRegion { get; }

    public static event System.Action<Player> PlayerSpawned;
    public static event System.Action<Player> LocalPlayerSpawned;
    public static event System.Action<Player> PlayerDespawned;
    public event System.Action OnDeath;
    public event System.Action OnRevive;

    public void Damage(int amount);
    public void Heal(int amount);
    public void Kill();
    public void Revive();
    public S1API.Avatar.BasicAvatarSettings GetCurrentBasicAvatarSettings();
    public void SendAppearance(S1API.Avatar.BasicAvatarSettings settings);
    public S1API.Items.ClothingItemInstance EquipClothing(S1API.Items.ClothingItemDefinition definition);
    public void InsertClothing(S1API.Items.ClothingItemInstance clothing);
    public void RefreshClothingAppearance();
}
```

`Player.All` + `PlayerSpawned` / `PlayerDespawned` is the co-op-correct way to
enumerate players — better than `Il2CppScheduleOne.PlayerScripts.Player.PlayerList`
because you get spawn/despawn callbacks.

---

## 18. Items, money, economy (parallel pass — see also §9)

```csharp
public static class S1API.Items.ItemManager
{
    public static List<ItemDefinition> GetAllItemDefinitions();
    public static ItemDefinition GetDefinition(string itemID);
    public static bool IsItemRegistered(string itemID);
    public static void RegisterItem(ItemDefinition definition);
    public static bool EnsureItemRegistered(ItemDefinition definition);
    public static bool PreserveRuntimeItem(ItemDefinition definition);
    public static bool UnregisterItem(string itemID);
    [Obsolete] public static ItemDefinition GetItemDefinition(string itemID);
}
```

Types: `ItemDefinition`, `ItemInstance`, `ItemSlotInstance`,
`StorableItemDefinition(+Builder)`, `QualityItemDefinition(+Builder/Instance/Creator)`,
`ClothingItemDefinition(+Builder/Instance/Creator)`, `AdditiveDefinition(+Builder/Creator)`,
`BuildableItemDefinition(+Builder/Creator)`, `Equippable(+Builder)`,
`ItemCreator`, `AvatarEquippablePaths`, `AvatarEquippableRegistry`,
enums `ItemCategory`, `LegalStatus { Legal, Illegal }`, `ClothingSlot`,
`ClothingColor`, `ClothingApplicationType`, `AvatarHand`, `BuildSoundType`.

**`GetAllItemDefinitions()` is incomplete** — already recorded as a project
decision (it misses unloaded `ScriptableObject`s and update content), which is
why `ItemCatalog.cs` also scans `Resources` and `FindObjectsOfTypeAll`. Keep
doing that.

```csharp
public static class S1API.Money.Money
{
    public static event System.Action OnBalanceChanged;
    public static float GetCashBalance();
    public static float GetOnlineBalance();
    public static float GetNetWorth();
    public static void  ChangeCashBalance(float amount, bool visualizeChange = true, bool playCashSound = false);
    public static void  CreateOnlineTransaction(string transactionName, float unitAmount, float quantity, string transactionNote);
    public static CashInstance CreateCashInstance(float amount);
    public static void AddNetworthCalculation(System.Action<object> callback);
    public static void RemoveNetworthCalculation(System.Action<object> callback);
}
```

Economy: `Contract`, `ContractInfo`, `ContractInfoBuilder`, `ContractReceipt`,
enums `CustomerStandard`, `DealerType`.

Leveling:

```csharp
public static class S1API.Leveling.LevelManager
{
    public static bool  Exists      { get; }
    public static Rank  Rank        { get; }
    public static int   Tier        { get; }
    public static int   XP          { get; }
    public static int   TotalXP     { get; }
    public static float XPToNextTier{ get; }
    public static FullRank CurrentRank { get; }
    public static event System.Action<FullRank, FullRank> OnXPChanged;
    public static event System.Action<FullRank, FullRank> OnRankUp;
    public static void AddXP(int amount);
    public static void AddUnlockable(Unlockable unlockable);
    public static FullRank GetFullRankForXP(int totalXp);
    public static float GetOrderLimitMultiplier(FullRank rank);
}
```

---

## 19. Property, map, world

```csharp
public static class S1API.Property.PropertyManager
{
    public static PropertyWrapper FindPropertyByName(string name);
    public static List<PropertyWrapper> GetAllProperties();
    public static List<PropertyWrapper> GetOwnedProperties();
}

public static class S1API.Property.BusinessManager
{
    public static BusinessWrapper FindBusinessByName(string name);
    public static List<BusinessWrapper> GetAllBusinesses();
    public static List<BusinessWrapper> GetOwnedBusinesses();
}

public class S1API.Property.PropertyWrapper : BaseProperty
{
    public string PropertyName { get; }
    public string PropertyCode { get; }
    public float  Price        { get; set; }
    public bool   IsOwned      { get; }
    public int    EmployeeCapacity { get; set; }
    public int    EmployeeCount    { get; }
    public UnityEngine.Vector3 ExteriorSpawnPosition { get; }
    public UnityEngine.Vector3 InteriorSpawnPosition { get; }
    public int    LoadingDockCount { get; }
    public float  DefaultRotation  { get; }
    public bool   IsContentCulled  { get; set; }
}

public class S1API.Property.BusinessWrapper : PropertyWrapper
{
    public float LaunderCapacity { get; set; }
    public List<LaunderingOperation> LaunderingOperations { get; }
    public float CurrentLaunderTotal { get; }
    public float AppliedLaunderLimit { get; }
    public int   LaunderingOperationCount { get; }
    public bool  IsAtLaunderingCapacity { get; }
    public float LaunderingCapacityUsagePercent { get; }
}
```

Map: `Region` enum, `Building`, `DeliveryLocation`, `MapPOI` + `MapPOIBuilder`
+ `MapPOIManager` + `MapPOITextVisibility`, `ParkingLotWrapper`,
`ParkingSpotWrapper`, `ParkingLotRegistry`, `ParkingLotNameAttribute`,
`ParkingData`, `IParkingLotIdentifier`, plus pre-baked identifier namespaces
`Map.Buildings`, `Map.DeliveryLocations`, `Map.ParkingLots`.

Storage — two namespaces, both real:

```csharp
public static class S1API.Storages.StorageManager
{
    public static StorageInstance FindByName(string name);
    public static StorageInstance[] FindByPredicate(System.Func<StorageInstance, bool> predicate);
    public static StorageInstance[] GetAll();
}
// + StorageInstance, StorageAccessSettings enum

public static class S1API.Storage.StorageEvents { /* + StorageEntity, StorageEventArgs, StorageLoadingEventArgs */ }
```

Vehicles: `LandVehicle`, `VehicleRegistry`, enums `VehicleColor`, `ParkingAlignment`.
Doors: `DoorController`, enums `DoorAccess`, `DoorSide`.
Building: `BuildManager`, `BuildEvents`, `BuildEventArgs`.
Trash: `TrashManager` (`const int TrashItemLimit = 2000`, `CreateTrashItem`,
`RegisterTrashPrefab`, `GetTrashPrefab`, `GetRandomTrashPrefab`,
`DestroyAllTrash`).
Graffiti: `GraffitiManager`, `GraffitiEvents`, `SpraySurface`.
Growing: `PlantInstance`, `SeedDefinition/Instance/Creator`,
`MushroomBedInstance`, `ShroomColonyInstance`, `GrowContainerAdditives`.
Misc: `ModularSwitch`. Shops: `Shop`, `ShopManager`.
DeadDrops: `DeadDropManager`, `DeadDropInstance`, `DeadDropGuidAttribute`,
`IDeadDropIdentifier`. Deliveries: `Delivery`, `DeliveryItem`,
`DeliveryReceipt`, `DeliveryRegistry`, `DeliveryStatus`.
Cartel: `Cartel`, `CartelGoon`, `CartelInfluence`, `GoonManager`, `CartelStatus`.
Casino: `SlotMachineHelper`, `GamblingSessionMode`.
Stations: `ChemistryStationRecipe(+Builder/Ingredient/Product/Temperature)`,
`ChemistryStationRecipes`, `QualityCalculationMethod`.
Law: `LawManager`, `LawController`, `CurfewManager`, `CheckpointManager`,
`PlayerCrimeData`, `PatrolGroup`, `FootPatrolRoute`, `VehiclePatrolRoute`,
enums `PursuitLevel`, `CheckpointLocation` (documented in
`research/API-POLICE.md` and `research/API-PERSISTENCE-TIME.md` §2.6).

---

## 20. Quests

```csharp
public abstract class S1API.Quests.Quest : Saveable
{
    protected string Title       { get; }
    protected string Description { get; }
    protected bool   AutoBegin   { get; }
    protected Constants.QuestState QuestState { get; }
    protected UnityEngine.Sprite QuestIcon { get; }
    public readonly List<QuestEntry> QuestEntries;
    public event System.Action OnComplete;
    public event System.Action OnFail;

    protected QuestEntry AddEntry(string title, UnityEngine.Vector3? poiPosition = null);
    protected QuestEntry AddEntry(string title, S1API.Entities.NPC npc);
    public void Begin();
    public void Complete();
    public void Fail();
    public void Cancel();
    public void Expire();
    public void End();
}

public class S1API.Quests.QuestEntry
{
    public Constants.QuestState State { get; }
    public string Title { get; set; }
    public UnityEngine.Vector3 POIPosition { get; set; }
    public event System.Action OnComplete;
    public void Begin();
    public void Complete();
    public void SetState(Constants.QuestState questState);
    public bool SetPOIToNPC<T>();
    public bool SetPOIToNPC(S1API.Entities.NPC npc);
    public bool SetPOIToSpraySurface(S1API.Graffiti.SpraySurface spraySurface);
    public void ClearPOI();
}

public static class S1API.Quests.QuestManager
{
    public static Quest CreateQuest<T>(string guid = null);
    public static Quest CreateQuest(System.Type questType, string guid = null);
    public static QuestWrapper Get<T>();
    public static Quest GetQuestByGuid(string guid);
    public static Quest GetQuestByName(string questName);
}

public sealed class S1API.Quests.QuestWrapper   // wraps mod quests AND base-game quests
{
    public string Title { get; }
    public List<QuestEntry> QuestEntries { get; }
    public event System.Action OnComplete;
    public event System.Action OnFail;
}
```

Plus `S1API.Quests.Constants` (the `QuestState` enum) and
`S1API.Quests.Identifiers` (pre-baked base-game quest identifiers).

Note `Quest : Saveable` with a `[SaveableField("QuestData")]` field — mod
quests persist into the save's `Modded/Quests` folder automatically.

---

## 21. Console

Covered in detail in `research/API-NETWORKING-CONSOLE.md` §2.3. Surface:

```csharp
public abstract class S1API.Console.BaseConsoleCommand
{
    public string CommandWord        { get; }
    public string CommandDescription { get; }
    public string ExampleUsage       { get; }
    public abstract void ExecuteCommand(System.Collections.Generic.List<string> args);
}

public static class S1API.Console.ConsoleHelper
{
    public static void Submit(string command);
    public static void Submit(Il2CppSystem.Collections.Generic.IEnumerable<string> arguments);
    // + 25 typed wrappers: AddItemToInventory, ClearInventory, ClearTrash, ClearWanted,
    //   DiscoverProduct, GiveXp, GrowPlants, LowerWanted, RaiseWanted, RunCashCommand,
    //   RunOnlineBalanceCommand, SaveGame, SetLawIntensity, SetNpcRelationship (x2),
    //   SetPlayerHealth, SetPlayerJumpMultiplier, SetPlayerMoveSpeedMultiplier,
    //   SetQuality, SetQuestState, SetTime, SpawnVehicle, UnlockNpc,
    //   [Obsolete] SetPlayerEnergyLevel
}

public static class S1API.Console.ConsoleItemAliases
{
    public static void Register(string alias, string canonicalItemId);
}
```

Registration is via the internal `CustomConsoleRegistry`, driven by S1API's
patches on `Console.Awake` and `Console.SubmitCommand`. `ConsoleItemAliases`
works through a prefix on `Console+AddItemToInventoryCommand.Execute`.

---

## 22. Phone apps, TV apps, UI

```csharp
public abstract class S1API.PhoneApp.PhoneApp : Registerable
{
    protected string AppName      { get; }
    protected string AppTitle     { get; }
    protected string IconLabel    { get; }
    protected string IconFileName { get; }
    protected UnityEngine.Sprite IconSprite { get; }
    protected EOrientation Orientation { get; }   // Horizontal = 0, Vertical = 1

    protected abstract void OnCreatedUI(UnityEngine.GameObject container);
    protected virtual  void OnCreated();
    protected virtual  void OnDestroyed();
    protected virtual  void OnPhoneClosed();
    public    virtual  void Exit(ExitAction exit);

    public void OpenApp();
    public void CloseApp();
    public bool IsOpen();
    public bool SetIconSprite(UnityEngine.Sprite sprite);
    public bool SetIconTexture(UnityEngine.Texture2D texture);
}

public abstract class S1API.TVApp.TVApp { /* same shape, for the in-game TV */ }
```

Both are auto-registered (`Internal.Patches.PhoneAppRegistry.RegisteredApps`,
`Internal.Patches.TVAppRegistry.RegisteredApps`) and injected by S1API's
patches on `HomeScreen.Start` / `TVHomeScreen.Awake`.

```csharp
public static class S1API.UI.UIFactory
{
    public static UnityEngine.GameObject Panel(string name, Transform parent, Color bgColor,
                                               Vector2? anchorMin = null, Vector2? anchorMax = null,
                                               bool fullAnchor = false);
    public static UnityEngine.UI.Text Text(string name, string content, Transform parent,
                                           int fontSize = 14, TextAnchor anchor = 0, FontStyle style = 0);
    public static (GameObject, Button, Text) ButtonWithLabel(string name, string label, Transform parent,
                                                             Color bgColor, float Width, float Height);
    public static (GameObject, Button, Text) RoundedButtonWithLabel(string name, string label, Transform parent,
                                                                    Color bgColor, float width, float height,
                                                                    int fontSize, Color textColor);
    public static GameObject ButtonRow(string name, Transform parent, float spacing = 12, TextAnchor alignment = 4);
    public static GameObject TopBar(string name, Transform parent, string title, float topbarSize,
                                    int paddingLeft, int paddingRight, int paddingTop, int paddingBottom);
    public static RectTransform ScrollableVerticalList(string name, Transform parent, out ScrollRect scrollRect);
    public static GameObject CreateQuestRow(string name, Transform parent, out GameObject iconPanel, out GameObject textPanel);
    public static void CreateTextBlock(Transform parent, string title, string subtitle, bool isCompleted);
    public static void CreateRowButton(GameObject go, UnityAction clickHandler, bool enabled);
    public static void BindAcceptButton(Button btn, Text label, string text, UnityAction callback);
    public static void VerticalLayoutOnGO(GameObject go, int spacing = 10, RectOffset padding = null);
    public static void HorizontalLayoutOnGO(GameObject go, int spacing = 10, int padLeft = 0, int padRight = 0,
                                            int padTop = 0, int padBottom = 0, TextAnchor alignment = 4);
    public static void SetLayoutGroupPadding(LayoutGroup layoutGroup, int left, int right, int top, int bottom);
    public static void FitContentHeight(RectTransform content);
    public static void ClearChildren(Transform parent);
    public static void SetIcon(Sprite sprite, Transform parent);
}
```

**`UIFactory` builds legacy `UnityEngine.UI.Text`, not TMP.** For a
native-looking main-menu screen you still need to clone live game objects (as
already decided in `CONTEXT.md`). `UIFactory` is fine inside a phone app, where
the visual language is already boxy.

`S1API.UI` has exactly three public types: `UIFactory`,
`CharacterCreatorManager`, `MainMenuRig` — and `MainMenuRig` exposes only
`Avatar` and `FindInScene(bool includeInactive = false)`. There is **no**
main-menu screen API. Confirmed.

Other UI-adjacent helpers:
`S1API.Utils.ButtonUtils`, `ToggleUtils`, `ColorUtils`, `ImageUtils`
(`LoadImage`, `LoadImageFromResource(Assembly, string, float, FilterMode)`,
`LoadImageRaw(byte[])`, `TextureToSprite`), `TransformUtils`,
`ArrayExtensions`, `RandomUtils`, `ReflectionUtils`.
`S1API.AssetBundles.AssetLoader` + `WrappedAssetBundle` +
`WrappedAssetBundleRequest`.
`S1API.Rendering.{IconFactory, MaterialHelper, TextureUtils, AccessoryFactory, AvatarLayerFactory, RuntimeResourceRegistry}`.
`S1API.Input.{Controls, ButtonCode, InputDeviceType}`.

---

## 23. Harmony patches — the collision map

`S1API.Internal.Patches` contains **40 types** and **147 `[HarmonyPatch]`
attributes**. Every game method S1API already hooks is listed below, grouped by
the patch class. This is the authoritative collision list.

### 23.1 Persistence / lifecycle — **HIGH collision risk**

| Game method | Patch class(es) | Kind |
|---|---|---|
| `Persistence.LoadManager.StartGame` | `CustomProductManifestLoadPatches` | Prefix |
| `Persistence.LoadManager.LoadAsClient` | `CustomProductManifestLoadPatches` | Prefix |
| `Persistence.LoadManager.ExitToMenu` | `CustomProductManifestLoadPatches` | Prefix |
| `Persistence.LoadManager.Update` | `CustomProductManifestLoadPatches` | Postfix |
| `Persistence.LoadManager.QueueLoadRequest` | `CustomProductSavePatches`, `GenericSaveablesPatches` | Prefix ×2 |
| `Persistence.LoadManager.GetLoadStatusText` | `LoadingScreenPatches` | Postfix |
| `Persistence.SaveManager.Save(string)` | `CustomProductSavePatches`, `GenericSaveablesPatches`, `QuestPatches` | Postfix ×3 |
| `Persistence.Loaders.QuestsLoader.Load` | `QuestPatches` | Prefix + Postfix |
| `Persistence.Loaders.NPCsLoader.Load` | `GenericSaveablesPatches`, `NPCPatches` (×2) | Postfix + Prefix(400) + Prefix(800) |
| `Persistence.Loaders.NPCLoader.Load` | `NPCPatches`, `SupplierRuntimePatches` | Prefix(800) + Postfix ×2 |
| `Persistence.Loaders.StorageLoader.Load` | `SupplierStashPersistencePatches` | Prefix |
| `Persistence.Loaders.PlaceableStorageEntityLoader.Load(DynamicSaveData)` | `StoragePatches` | Prefix |
| `Persistence.Datas.ItemSet.LoadTo(List<ItemSlot>)` | `StoragePatches` | Prefix |
| `UI.LoadingScreen.Close` | `LoadingScreenPatches` | Prefix |

`LoadManager.QueueLoadRequest` and `SaveManager.Save(string)` each already carry
**two and three** S1API patches. If you add your own there, you are the fourth
in line and Harmony ordering between mods is not guaranteed. **Subscribe to
`GameLifecycle.OnSaveComplete` / `OnLoadComplete` instead.**

### 23.2 NPCs / entities — **HIGH collision risk**

| Game method | Patch class | Kind |
|---|---|---|
| `NPCs.NPC.Awake` | `NPCPatches` | Prefix (priority 800) |
| `NPCs.NPC.OnDestroy` | `SupplierRuntimePatches` | Postfix |
| `NPCs.NPC.GetSaveData` | `NPCPatches` | Prefix **and** Postfix |
| `NPCs.NPC.ShouldSave` | `NPCPatches` | Prefix |
| `NPCs.NPC.SetVisible(bool, bool)` | `SupplierRuntimePatches` | Postfix |
| `NPCs.NPCHealth.Awake` | `NPCPatches` | Prefix |
| `NPCs.NPCHealth.Load` | `NPCPatches` | Prefix |
| `NPCs.NPCHealth.Revive` | `NPCPatches` | Prefix |
| `NPCs.NPCInventory.Awake` | `NPCPatches` | Prefix(800) **and** Postfix(0) |
| `NPCs.NPCInventory.OnSleepStart` | `NPCPatches` | Prefix |
| `NPCs.NPCManager.GetNPC` | `NPCPatches` | Postfix |
| `NPCs.NPCManager.GetSaveString` | `NPCPatches` | Prefix |
| `Economy.Customer.Awake` | `NPCPatches` | Prefix(800) **and** Postfix(0) |
| `Economy.Dealer.Awake` | `NPCPatches` | Prefix(800) |
| `Economy.Dealer.Load(DynamicSaveData, NPCData)` | `NPCPatches` | Prefix |
| `Economy.Supplier.Awake` | `SupplierRuntimePatches` | Prefix(800) |
| `Economy.Supplier.Start` | `SupplierRuntimePatches` | Prefix(800) |
| `Economy.Supplier.Load(DynamicSaveData, NPCData)` | `SupplierRuntimePatches` | Prefix(800) |
| `Economy.SupplierStash.Start` | `SupplierRuntimePatches` | Prefix(800) |
| `Economy.DeadDrop.GetRandomEmptyDrop` | `SupplierRuntimePatches` | Prefix |
| `PlayerScripts.Player.Awake` | `PlayerPatches` | Postfix |
| `PlayerScripts.Player.OnDestroy` | `PlayerPatches` | Postfix |
| `PlayerScripts.Player.ReceivePlayerData` | `CustomProductManifestPlayerPatches` | Prefix |
| `PlayerScripts.Player.RequestPlayerData` | `CustomProductManifestPlayerPatches` | Prefix |
| `PlayerScripts.Health.PlayerHealth.TakeDamage` | `PlayerPatches` | Prefix |

Several of these prefixes **return `bool`**, meaning they can skip the original.
Verified skipping prefixes: `NPCHealth.Awake`, `NPCHealth.Load`,
`NPCHealth.Revive`, `NPCInventory.OnSleepStart`, `NPC.GetSaveData`,
`NPC.ShouldSave`, `NPCManager.GetSaveString`, `Dealer.Awake`, `Dealer.Load`,
`Supplier.Awake/Start/Load`, `SupplierStash.Start`, `DeadDrop.GetRandomEmptyDrop`,
`Player.ReceivePlayerData`, `Player.RequestPlayerData`,
`PlayerHealth.TakeDamage`, `NPCsLoader.Load`.

**This is the biggest hazard for the Special Customers and Police mods.** If
S1API's prefix on `Customer.Awake` or `NPCHealth.Awake` returns false for a
given NPC, a Postfix of yours on the same method still runs, but a *Prefix* of
yours may run before or after S1API's depending on priority, and the original
may never execute. Two survival rules:

1. **Prefer Postfix** unless you genuinely need to cancel.
2. **If you must Prefix, set an explicit `[HarmonyPriority]`** and assume
   S1API's 800-priority prefixes run first. Harmony's default priority is
   `Priority.Normal` (400), so S1API's `HarmonyPriority(800)` prefixes beat you
   by default and its `HarmonyPriority(0)` postfixes run last.

### 23.3 Products / mixing / icons — **HIGH collision risk if you touch products**

| Game method | Patch class | Kind |
|---|---|---|
| `Product.ProductManager.SendFinishAndNameMix(string,string,string,string)` | `CustomProductMixingClientRpcPatches` | class-level |
| `Product.ProductManager.FinishAndNameMix(string,string,string,string)` | `CustomProductMixingIdPatches` | class-level |
| `Product.ProductManager.RpcLogic___FinishAndNameMix_4237212381` | `CustomProductMixingPatches` | class-level |
| `Product.ProductIconManager.GenerateIcons` | `ProductPackagingContentPatches` | Prefix + **Finalizer** |
| `Product.ProductIconManager.GetIcon(string,string,bool)` | `ProductPackagingContentPatches` | Prefix |
| `Product.MultiTypeVisualsSetter.ApplyVisuals(ProductItemInstance)` | `ProductPackagingContentPatches` | Prefix |
| `DevUtilities.IconGenerator.GeneratePackagingIcon` | `ProductPackagingContentPatches` | Prefix + **Finalizer** |
| `Product.PropertyUtility.Awake` | `ProductManagerUiPatches` | Postfix |
| `Product.PropertyUtility.GetProperties` (resolved via `TargetMethod()`) | `PropertyUtilityPatches` | Postfix |
| `Product.ProductItemInstance.*` (resolved via `TargetMethods()`) | `ProductEffectPatches` | Prefix |
| `Effects.EffectMixCalculator.MixProperties` | `MixReactionPatches` | class-level |
| `StationFramework.StationRecipe.CalculateQuality` | `ChemistryStationPatches` | Prefix |
| `StationFramework.StationRecipe.get_RecipeID` | `ChemistryStationPatches` | Prefix |

Note the hardcoded RPC hash: **`RpcLogic___FinishAndNameMix_4237212381`**. That
name will change if TVGS touches `FinishAndNameMix`'s signature, so this is a
known S1API breakage point on game updates.

### 23.4 UI / phone / TV — moderate risk

| Game method | Patch class | Kind |
|---|---|---|
| `UI.Phone.HomeScreen.Start` | `HomeScreen_Start_Patch` **and** `HomeScreenScrollPatch` | Postfix ×2 |
| `UI.Phone.ContactsApp.ContactsApp.Start` | `ContactsAppPatches` | Postfix |
| `UI.Phone.Messages.DealerManagementApp.Refresh` | `DealerManagementAppPatches` | Postfix |
| `UI.Phone.Messages.DealerManagementApp.SetOpen` | `DealerManagementAppPatches` | Postfix |
| `UI.Phone.Delivery.DeliveryApp.CreateDeliveryStatusDisplay` | `DeliveryPatches` | Prefix(800) |
| `UI.Phone.Delivery.DeliveryApp.SetIsAvailable` | `DeliveryPatches` | Prefix(800) |
| `UI.Phone.ProductManagerApp.ProductManagerApp.Start` | `ProductManagerUiPatches` | Prefix |
| `UI.Phone.ProductManagerApp.ProductManagerApp.CreateEntry` | `ProductManagerUiPatches` | Prefix + Postfix |
| `UI.Phone.ProductManagerApp.ProductManagerApp.CreateFavouriteEntry` | `ProductManagerUiPatches` | Prefix |
| `UI.Phone.ProductManagerApp.ProductManagerApp.RemoveFavouriteEntry` | `ProductManagerUiPatches` | Prefix |
| `UI.Phone.ProductManagerApp.ProductManagerApp.SetOpen` | `ProductManagerUiPatches` | Postfix |
| `UI.Stations.ChemistryStationInterface.Awake` | `ChemistryStationPatches` | Prefix |
| `UI.Stations.ChemistryStationInterface.Open` | `ChemistryStationPatches` | Prefix |
| `UI.Phone.CallInterface.StartCall` | `CallManagerPatches` | Prefix |
| `UI.Phone.CallInterface.Close` | `CallManagerPatches` | Postfix |
| `Calling.CallManager.QueueCall` | `CallManagerPatches` | Prefix (returns bool) |
| `Calling.CallManager.CallCompleted` | `CallManagerPatches` | Postfix |
| `TV.TVHomeScreen.Awake` | `TVHomeScreen_Awake_Patch` | Postfix |
| `TV.TVHomeScreen.Open` | `TVHomeScreen_Open_Patch` | Postfix |
| `TV.TVHomeScreen.Close` (via `TargetMethod()`) | `TVHomeScreen_Close_Patch` | Prefix |
| `TV.TVInterface.Close` | `TVInterface_Close_Patch` | Prefix (returns bool) |

**`SettingsScreen.Awake` is NOT patched by S1API** — the injection point already
chosen for the Expansions settings tab is free. Good.

**No main-menu type is patched by S1API at all.** `MainMenuRig`, `MenuScreen`,
`MainMenuPopup`, `SettingsScreen` — all untouched.

### 23.5 Console — moderate risk

| Game method | Patch class | Kind |
|---|---|---|
| `Il2CppScheduleOne.Console.Awake` | `ConsolePatches` | Postfix |
| `Il2CppScheduleOne.Console.SubmitCommand(List<string>)` | `ConsolePatches` | **Prefix (returns bool)** |
| `Il2CppScheduleOne.Console+AddItemToInventoryCommand.Execute` | `ConsolePatches` | Prefix |
| `Il2CppScheduleOne.CommandListScreen.Start` | `ConsolePatches` | Postfix |

`SubmitCommand` has a **skipping prefix** (`RouteCustomCommandsIl2Cpp`) that
swallows the call when the first token matches a registered custom command.
Do not add your own prefix there; register a `BaseConsoleCommand` instead.

Also note this means **calling `Console.SubmitCommand(...)` from your mod goes
through S1API's router first** — harmless for vanilla words, but it is one more
moving part between you and the game.

### 23.6 World / building / vehicles / misc — low risk

| Game method | Patch class | Kind |
|---|---|---|
| `Building.BuildManager.CreateGridItem` | `BuildingPatches` | Postfix |
| `Building.BuildManager.CreateSurfaceItem` | `BuildingPatches` | Postfix |
| `EntityFramework.BuildableItem.InitializeBuildableItem` | `BuildingPatches` | Postfix |
| `EntityFramework.BuildableItem.GetSaveData` | `StoragePatches` | Postfix |
| `ObjectScripts.PlaceableStorageEntity.Start` | `StoragePatches` | Postfix |
| `ObjectScripts.PlaceableStorageEntity.InitializeGridItem` | `StoragePatches` | Postfix |
| `Storage.WorldStorageEntity.Load(WorldStorageEntityData)` | `SupplierStashPersistencePatches` | Postfix |
| `Growing.GrowContainer.InitializeGridItem(...)` | `GrowContainerPatches` | Prefix |
| `Graffiti.WorldSpraySurface.SetFinalized` | `GraffitiPatches` | Postfix |
| `Equipping.Equippable.Equip` | `EquippableAvatarPatches` | Postfix |
| `Equipping.Equippable.Unequip` | `EquippableAvatarPatches` | Prefix |
| `Vehicles.LandVehicle.OnDestroy` | `LandVehiclePatches` | Postfix |
| `Vehicles.LandVehicle.SetVisible` | `LandVehiclePatches` | **Prefix (returns bool)** |
| `Vehicles.VehicleManager.LoadVehicle` | `DeliveryPatches` | Prefix(800) |
| `Delivery.DeliveryInstance.SetStatus` | `DeliveryPatches` | Prefix(800) + Postfix |
| `Economy.DeliveryLocation.Awake` | `MapPatches` | Postfix |
| `Map.NPCEnterableBuilding.Awake` | `MapPatches` | Postfix |
| `Map.ParkingLot.Awake` | `MapPatches` | Postfix |
| `Quests.Quest.Start` | `QuestPatches` | Prefix |
| `Quests.QuestEntry.CreateCompassElement` | `QuestPatches` | Prefix (returns bool) |
| `AvatarFramework.Avatar.*` (via `TargetMethod()`) | `AvatarAccessoryPatches` | Postfix |

### 23.7 Direct collisions with our three planned mods

| Our mod | S1API patch it will meet | What to do |
|---|---|---|
| **Hireable Drivers** | `Vehicles.LandVehicle.SetVisible` (skipping prefix), `Vehicles.VehicleManager.LoadVehicle` (prefix 800), `Delivery.DeliveryInstance.SetStatus` (prefix 800 + postfix), `Map.ParkingLot.Awake` (postfix) | Don't patch `SetVisible` at all — it can be cancelled out from under you. Use `S1API.Vehicles.LandVehicle` / `VehicleRegistry` / `Map.ParkingLotRegistry` wrappers. For route status, don't reuse `DeliveryInstance`; build your own state. |
| **Special Customers** | `Economy.Customer.Awake` (prefix 800 + postfix 0), `NPCs.NPC.Awake` (prefix 800), `NPCs.NPC.GetSaveData` (pre+post), `NPCs.NPC.ShouldSave` (prefix), `NPCManager.GetNPC` (postfix), `NPCManager.GetSaveString` (prefix), `NPCsLoader.Load` (3 patches) | Almost everything you'd want to patch is already taken. **Use `S1API.Entities.NPCPrefabBuilder` and the `NPC` facade instead of patching.** If you patch `Customer.Awake`, use a Postfix with priority below 0 so S1API's postfix(0) has already run. |
| **Police Improvements** | `PlayerScripts.Health.PlayerHealth.TakeDamage` (**skipping prefix**), `PlayerScripts.Player.Awake`/`OnDestroy` (postfix), `NPCHealth.Awake/Load/Revive` (skipping prefixes) | `PlayerHealth.TakeDamage` is the dangerous one — S1API's invincibility feature can cancel it. If you need damage interception, patch as a Postfix, or read `S1API.Entities.Player.IsInvincible` and cooperate. Everything else you need (`LawManager`, `LawController`, `CurfewManager`, `CheckpointManager`) is **unpatched** and has a clean S1API facade. |

---

## 24. What NOT to reimplement (parallel pass)

Do not write your own version of any of these. S1API already ships them,
already handles IL2CPP interop lifetimes, and already survives scene reloads.

| Don't build | Use instead |
|---|---|
| `UnityAction` wiring onto `SaveManager.onSaveStart/onSaveComplete`, `LoadManager.onPreLoad/onLoadComplete/onPreSceneChange/onSaveInfoLoaded` | `S1API.Lifecycle.GameLifecycle` events |
| `Il2CppSystem.Action` juggling for `TimeManager.onHourPass/onDayPass/onWeekPass/onSleepStart/onSleepEnd/onTick` (incl. the `ActionList` cases) | `S1API.GameTime.TimeManager` static `System.Action`s |
| Your own `ISaveable` implementation + `ClassInjector` | `S1API.Internal.Abstraction.Saveable` + `[SaveableField]` |
| Your own per-save JSON file plumbing | Same — S1API writes into `Modded/Saveables` |
| A custom quest system / compass POI plumbing | `S1API.Quests.Quest` / `QuestEntry` / `QuestManager` |
| A custom `ConsoleCommand` IL2CPP subclass | `S1API.Console.BaseConsoleCommand` |
| String-building console invocations for common cheats | `S1API.Console.ConsoleHelper` typed methods |
| A phone app injected by patching `HomeScreen.Start` yourself | `S1API.PhoneApp.PhoneApp` (S1API owns that patch — **two** postfixes on it already) |
| A TV app | `S1API.TVApp.TVApp` |
| Player enumeration + spawn/despawn tracking | `S1API.Entities.Player.All` + `PlayerSpawned` / `PlayerDespawned` / `LocalPlayerSpawned` |
| NPC lookup, relationship math, movement wrappers | `S1API.Entities.NPC.All` / `.Get(id)` / `.Movement` / `.Relationship` |
| Curfew hour constants and window math | `S1API.Law.CurfewManager` (`const`s + `IsWithinCurfewHours()` / `MinutesUntilCurfew()`) |
| Wanted-level plumbing | `S1API.Law.LawManager` / `PlayerCrimeData` |
| Checkpoint enable/disable + officer counts | `S1API.Law.CheckpointManager` |
| Property/business lookup + laundering caps | `S1API.Property.PropertyManager` / `BusinessManager` / `BusinessWrapper` |
| XP / rank math and unlock gates | `S1API.Leveling.LevelManager` (+ `GetOrderLimitMultiplier`) |
| Money reads/writes and networth hooks | `S1API.Money.Money` |
| Sprite loading from disk or embedded resource | `S1API.Utils.ImageUtils` |
| Asset bundle loading (managed `AssetBundle.LoadFromMemory` is stripped) | `S1API.AssetBundles.AssetLoader` |
| Trash prefab registration / spawning | `S1API.Trash.TrashManager` |
| Map POI creation | `S1API.Map.MapPOIBuilder` / `MapPOIManager` |
| Dialogue choice injection | `S1API.Dialogues.DialogueInjector` / `DialogueChoiceListener` |
| Custom cutscenes | `S1API.Cutscenes.CutsceneBuilder` / `CutsceneManager` |
| Custom phone calls | `S1API.PhoneCalls.PhoneCallDefinition` / `CallManager` |
| Custom chemistry-station recipes | `S1API.Stations.ChemistryStationRecipeBuilder` |

Where S1API is **not** enough (already established, restated here so nobody
re-litigates it):

- **Employee hire/fire/assign/pay.** `S1API.Entities.Employees` contains only
  `EmployeeManager.GetAppearance/GetRandomAppearance` and `EmployeeAppearance`
  (1 KB namespace dump). Hireable Drivers must go direct to
  `Il2CppScheduleOne.Employees.*`.
- **A native main-menu settings screen.** `S1API.UI` has three public types and
  `MainMenuRig` exposes only `Avatar`. Clone game objects yourself.
- **Complete item enumeration.** `ItemManager.GetAllItemDefinitions()` misses
  unloaded assets; keep the `Resources` + `FindObjectsOfTypeAll` scan.
- **Anything requiring a new FishNet RPC.** Nothing in S1API changes the fact
  that the FishNet weaver never ran on mod assemblies.

---

## 25. Multiplayer notes specific to S1API

`S1API.Products.CustomProductMultiplayer` is the only place S1API takes an
explicit multiplayer stance:

```csharp
public static class S1API.Products.CustomProductMultiplayer
{
    public static CustomProductMultiplayerPolicy MissingContentPolicy { get; }
    public static string GetCompatibilityManifestHash();
}

public enum S1API.Products.CustomProductMultiplayerPolicy { Reject = 0 }   // only one value
```

The enum has exactly one member, `Reject`. Combined with the patches on
`Player.RequestPlayerData` / `Player.ReceivePlayerData` and the four
`LoadManager` prefixes in `CustomProductManifestLoadPatches`, S1API implements
a **manifest-hash handshake that rejects clients with mismatched custom
products**. There is no "tolerate" mode.

Two consequences for us:

1. **Mod version parity across the lobby is already enforced for custom
   products** — but for nothing else. Our own content still needs its own
   check (`Lobby.SetLobbyData` / `GetLobbyData` — see
   `research/API-NETWORKING-CONSOLE.md` §1.1).
2. If Special Customers registers custom products, an S1API version or content
   mismatch will hard-reject a joining player. Test that path before shipping.

Several S1API methods take an explicit `bool network` parameter —
`NPC.SendTextMessage(..., bool network = true)`,
`NPCRelationship.Add(float delta, bool network = true)`,
`NPCDialogue.PlayReaction(..., bool network = false)`. Those are your
replication switches; set `network: false` for anything cosmetic and local.

---

## 26. UNVERIFIED summary (parallel pass)

| Item | Status |
|---|---|
| `AvatarAccessoryPatches+AvatarApplyAccessorySettingsPatch` target method | Resolved at runtime by `TargetMethod()`. Postfix signature takes `Il2CppScheduleOne.AvatarFramework.Avatar __instance`; the class name implies `Avatar.ApplyAccessorySettings`, but the exact method name is **not** in the metadata. |
| `ProductEffectPatches` target methods | Resolved by `TargetMethods()` with a `MethodInfo` filter. Prefix takes `Il2CppScheduleOne.Product.ProductItemInstance __instance` — so one or more `ProductItemInstance` methods. Exact set **UNVERIFIED**. |
| `PropertyUtilityPatches` target method | Resolved by `TargetMethod()`. Postfix signature `(List<string> __0, List<Effect> __result)` strongly implies `Product.PropertyUtility.GetProperties`, but the name is **not** in the metadata. |
| `TVHomeScreen_Close_Patch` target method | Resolved by `TargetMethod()`. Class name implies `TV.TVHomeScreen.Close`. **UNVERIFIED**. |
| S1API save folder literals (`Modded/Quests`, `Modded/Saveables`) | Observed on disk in two real save slots; the string constants live in S1API's own string pool, which is not in these dumps. |
| Whether S1API 3.1.7 changes any of the above | These dumps are of the **installed 3.1.4**. `CONTEXT.md` records a decision to upgrade to 3.1.7 — **re-dump `S1API.Il2Cpp.MelonLoader.dll` after upgrading and re-check §10 before writing patch code.** |
| `Il2CppScheduleOne.Console+ConsoleCommand` subclassing via `ClassInjector` | Not attempted or verified; S1API does it internally via its `Console.Awake` postfix, which is why `BaseConsoleCommand` works. |

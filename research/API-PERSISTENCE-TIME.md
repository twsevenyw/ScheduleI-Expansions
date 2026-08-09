# Schedule I — Persistence & Time API Reference

Target: MelonLoader / IL2CPP mods against `Assembly-CSharp.dll`, game version **0.4.6f11**.
Managed namespace prefix in the interop assemblies is `Il2CppScheduleOne.*`.

Every type, member and signature below was read out of the metadata dumps in
`research/raw/ns/`. Anything I could **not** confirm is explicitly tagged
`UNVERIFIED`. Sources per section are listed inline.

---

## Part 1 — Persistence

Source dumps: `ns-Il2CppScheduleOne.Persistence.txt`,
`ns-Il2CppScheduleOne.Persistence.Loaders.txt`,
`ns-Il2CppScheduleOne.Persistence.ItemLoaders.txt`,
`ns-Il2CppScheduleOne.Persistence.Datas.txt`,
`ns-Il2CppScheduleOne.Persistence.Datas.Characters.txt`,
plus the real save slots in `research/raw/save-samples/`.

### 1.1 The three managers

| Type | Base | Lifetime |
|---|---|---|
| `Il2CppScheduleOne.Persistence.SaveManager` | `PersistentSingleton<SaveManager>` | survives scene change |
| `Il2CppScheduleOne.Persistence.LoadManager` | `PersistentSingleton<LoadManager>` | survives scene change |
| `Il2CppScheduleOne.Persistence.GenericSaveablesManager` | `Singleton<GenericSaveablesManager>` | per game scene |

`PersistentSingleton<T>` derives from `Singleton<T>` which derives from
`UnityEngine.MonoBehaviour`. Access pattern (verified in
`ns-Il2CppScheduleOne.DevUtilities.txt`):

```csharp
// Singleton<T> / PersistentSingleton<T>
static bool InstanceExists { get; }
static T    Instance       { get; set; }
```

Always gate on `InstanceExists` — `Instance` is a plain static field read and
will hand you a null/destroyed object outside the game scene.

### 1.2 `SaveManager` surface

```csharp
public class SaveManager : PersistentSingleton<SaveManager>
{
    // ---- static configuration ----
    static string MAIN_SCENE_NAME      { get; set; }
    static string MENU_SCENE_NAME      { get; set; }
    static string TUTORIAL_SCENE_NAME  { get; set; }
    static int    SAVES_PER_FRAME      { get; set; }
    static string SAVE_FILE_EXTENSION  { get; set; }
    static int    SAVE_SLOT_COUNT      { get; set; }
    static string SAVE_GAME_PREFIX     { get; set; }
    static bool   DEBUG                { get; set; }
    static bool   PRETTY_PRINT         { get; set; }
    static bool   SaveError            { get; set; }

    // ---- state ----
    bool   IsSaving                        { get; set; }
    float  SecondsSinceLastSave            { get; set; }
    string SaveName                        { get; set; }
    string PlayersSavePath                 { get; set; }
    string IndividualSavesContainerPath    { get; set; }
    string BackupFolderPath                { get; }
    bool   AccessPermissionIssueDetected   { get; set; }
    bool   saveFolderInitialized           { get; set; }

    // ---- registries ----
    Il2CppSystem.Collections.Generic.List<ISaveable>     Saveables              { get; set; }
    Il2CppSystem.Collections.Generic.List<IBaseSaveable> BaseSaveables          { get; set; }
    Il2CppSystem.Collections.Generic.List<string>        ApprovedBaseLevelPaths { get; set; }
    Il2CppSystem.Collections.Generic.List<ISaveable>     CompletedSaveables     { get; set; }
    Il2CppSystem.Collections.Generic.List<SaveRequest>   QueuedSaveRequests     { get; set; }

    // ---- events (UnityEvent, NOT Il2CppSystem.Action) ----
    UnityEngine.Events.UnityEvent onSaveStart    { get; set; }
    UnityEngine.Events.UnityEvent onSaveComplete { get; set; }

    // ---- methods ----
    void   Save();
    void   Save(string saveFolderPath);
    void   RegisterSaveable(ISaveable saveable);
    void   QueueSaveRequest(SaveRequest request);
    void   DequeueSaveRequest(SaveRequest request);
    void   CompleteSaveable(ISaveable saveable);
    void   ClearCompletedSaveable(ISaveable saveable);
    void   ClearBaseLevelOutdatedSaves(string saveFolderPath);
    void   CreateSaveBackup(SaveInfo saveInfo);
    void   CheckSaveFolderInitialized();
    void   DisablePlayTutorial(SaveInfo info);
    void   Clean();

    static float  GetVersionNumber(string version);
    static bool   HasWritePermissionOnDir(string path);
    static string MakeFileSafe(string fileName);
    static string SanitizeFileName(string fileName);
    static string StripExtensions(string filePath);
    static void   ReportSaveError();
}
```

### 1.3 `LoadManager` surface

```csharp
public class LoadManager : PersistentSingleton<LoadManager>
{
    static int    LOADS_PER_FRAME     { get; set; }
    static bool   DEBUG               { get; set; }
    static float  LOAD_ERROR_TIMEOUT  { get; set; }
    static float  NETWORK_TIMEOUT     { get; set; }
    static Il2CppSystem.Collections.Generic.List<string> LoadHistory { get; set; }
    static Il2CppReferenceArray<SaveInfo> SaveGames     { get; set; }
    static SaveInfo                       LastPlayedGame { get; set; }

    bool       IsGameLoaded         { get; set; }
    bool       IsLoading            { get; set; }
    bool       IsInGameScene        { get; }
    float      TimeSinceGameLoaded  { get; set; }
    bool       DebugMode            { get; set; }
    ELoadStatus LoadStatus          { get; set; }
    string     LoadedGameFolderPath { get; set; }
    SaveInfo   ActiveSaveInfo       { get; set; }
    SaveInfo   StoredSaveInfo       { get; set; }
    string     DefaultTutorialSaveFolder { get; }

    // ---- events ----
    UnityEngine.Events.UnityEvent onPreSceneChange  { get; set; }
    UnityEngine.Events.UnityEvent onSceneChangeDone { get; set; }
    UnityEngine.Events.UnityEvent onPreLoad         { get; set; }
    UnityEngine.Events.UnityEvent onLoadComplete    { get; set; }
    UnityEngine.Events.UnityEvent onSaveInfoLoaded  { get; set; }
    Il2CppSystem.Action<string>   OnLocalSaveLoadStart { get; set; }
    static Il2CppSystem.Action    onLoadConfigurations { get; set; }

    // ---- loader registries ----
    Il2CppSystem.Collections.Generic.List<ItemLoaders.ItemLoader>      ItemLoaders      { get; set; }
    Il2CppSystem.Collections.Generic.List<Loaders.BuildableItemLoader> ObjectLoaders    { get; set; }
    Il2CppSystem.Collections.Generic.List<Loaders.NPCLoader>           NPCLoaders       { get; set; }
    Il2CppSystem.Collections.Generic.List<Loaders.LegacyNPCLoader>     LegacyNPCLoaders { get; set; }
    Il2CppSystem.Collections.Generic.List<LoadRequest>                 loadRequests     { get; set; }

    void StartGame(SaveInfo info, bool allowLoadStacking = false, bool allowSaveBackup = true);
    void LoadLastSave();
    void LoadAsClient(string steamId64);
    void LoadTutorialAsClient();
    void ExitToMenu(SaveInfo autoLoadSave = null,
                    UI.MainMenu.MainMenuPopup.Data mainMenuPopup = null,
                    bool preventLeaveLobby = false);
    void QueueLoadRequest(LoadRequest request);
    void DequeueLoadRequest(LoadRequest request);
    void RefreshSaveInfo();
    void SetWaitingForHostLoad();
    void InitializeItemLoaders();
    void InitializeObjectLoaders();
    void InitializeNPCLoaders();
    void AddStaggeredReplicator(Networking.IStaggeredReplicator replicator);
    string GetLoadStatusText();

    ItemLoaders.ItemLoader      GetItemLoader(string itemType);
    Loaders.BuildableItemLoader GetObjectLoader(string objectType);
    Loaders.NPCLoader           GetNPCLoader(string npcType);
    Loaders.LegacyNPCLoader     GetLegacyNPCLoader(string npcType);

    static bool TryLoadSaveInfo(string saveFolderPath, int saveSlotIndex,
                                out SaveInfo saveInfo, bool requireGameFile = false);
    static void CleanUp();
}

public enum LoadManager.ELoadStatus
{
    None = 0, LoadingScene = 1, Initializing = 2,
    LoadingData = 3, SpawningPlayer = 4, WaitingForHost = 5
}
```

### 1.4 Call sequences

**Save** (from `SaveManager`, `SaveRequest`, `ISaveable`):

1. Something calls `SaveManager.Instance.Save()` or `Save(saveFolderPath)`.
   Triggers in-game: the `SavePoint` interactable (`SavePoint.Save()`, with a
   `static float SAVE_COOLDOWN` and `bool CanSave(out string reason)`), the
   `save` console command, sleeping, and quitting to menu.
2. `onSaveStart` (`UnityEvent`) fires.
3. `IsSaving` flips true; `Save` starts a coroutine
   (`<Save>g__SaveRoutine|0`) which walks `Saveables` and enqueues a
   `SaveRequest(ISaveable saveable, string parentFolderPath)` per saveable,
   throttled to `SAVES_PER_FRAME` per frame.
4. For each request the saveable's `GetSaveString()` is called, then
   `ISaveable.CompleteSave(parentFolderPath, writeDataFile)` →
   `WriteBaseData` / `WriteData` / `WriteSubfile` / `WriteFolder`.
5. `DeleteUnapprovedFiles(parentFolderPath)` prunes anything under the
   saveable's own folder that it did not write this pass. **This is the
   mechanism that eats stray mod files** — see §1.9.
6. `ClearBaseLevelOutdatedSaves(saveFolderPath)` prunes base-level files not in
   `ApprovedBaseLevelPaths`.
7. `onSaveComplete` fires; `IsSaving` back to false; `SecondsSinceLastSave` resets.

**Load** (from `LoadManager`):

1. `StartGame(SaveInfo info, ...)` → optional `SaveManager.CreateSaveBackup(info)`
   → `onPreSceneChange` → async scene load into `MAIN_SCENE_NAME` →
   `onSceneChangeDone`.
   `LoadStatus` walks `LoadingScene → Initializing → LoadingData → SpawningPlayer`.
   A joining client goes through `LoadAsClient(steamId64)` and sits in
   `WaitingForHost` until the host is ready.
2. `OnLocalSaveLoadStart(string)` fires with the save path (this is an
   `Il2CppSystem.Action<string>`, not a `UnityEvent`).
3. `InitializeItemLoaders()` / `InitializeObjectLoaders()` /
   `InitializeNPCLoaders()` populate the loader registries.
4. All registered `IBaseSaveable` are sorted by `LoadOrder`
   (`_StartGame_b__73_3(IBaseSaveable x) => x.LoadOrder`) and each one's
   `Loader.Load(mainPath)` is invoked, `LOADS_PER_FRAME` at a time via
   `LoadRequest(string filePath, Loader loader)`.
5. `onLoadComplete` (`UnityEvent`) fires. `IsGameLoaded` becomes true and
   `TimeSinceGameLoaded` starts counting.
   `LoadEventTransmitter` is a `MonoBehaviour` that re-broadcasts this as its
   own `onLoadComplete` UnityEvent for scene wiring.

**Hooks a mod should actually use:**

| Goal | Hook |
|---|---|
| "game finished loading, world is safe to touch" | `LoadManager.Instance.onLoadComplete` (UnityEvent) or poll `LoadManager.Instance.IsGameLoaded` |
| "about to write to disk — flush my data" | `SaveManager.Instance.onSaveStart` (UnityEvent) |
| "disk write done — safe to copy/backup" | `SaveManager.Instance.onSaveComplete` (UnityEvent) |
| "which save folder are we in?" | `LoadManager.Instance.LoadedGameFolderPath` |
| "leaving to menu — tear down" | `LoadManager.Instance.onPreSceneChange` |

`UnityEvent` subscription from IL2CPP is `AddListener(UnityEngine.Events.UnityAction)`:

```csharp
private static UnityAction _onLoadComplete;   // keep a static ref alive!

_onLoadComplete = (UnityAction)OnLoadComplete;
LoadManager.Instance.onLoadComplete.AddListener(_onLoadComplete);
// ...
LoadManager.Instance.onLoadComplete.RemoveListener(_onLoadComplete);
```

### 1.5 `ISaveable` — the contract

In the interop assembly `ISaveable` is emitted as a **class** deriving from
`Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase` (that is how Il2CppInterop
renders IL2CPP interfaces). Its members:

```csharp
public class ISaveable   // really an interface in the original C#
{
    string  SaveFolderName        { get; }   // e.g. "Properties", "Players"
    string  SaveFileName          { get; }   // e.g. "Time", "Money"
    Loaders.Loader Loader         { get; }   // the matching loader instance
    bool    ShouldSaveUnderFolder { get; }   // true => own directory, false => flat .json
    Il2CppSystem.Collections.Generic.List<string> LocalExtraFiles   { get; set; }
    Il2CppSystem.Collections.Generic.List<string> LocalExtraFolders { get; set; }
    bool    HasChanged            { get; set; }

    void   InitializeSaveable();
    string GetSaveString();
    string Save(string parentFolderPath);
    void   CompleteSave(string parentFolderPath, bool writeDataFile);
    void   WriteBaseData(string parentFolderPath, string saveString);
    Il2CppSystem.Collections.Generic.List<string> WriteData(string parentFolderPath);
    string WriteSubfile(string parentPath, string localPath_NoExtensions, string contents);
    string WriteFolder(string parentPath, string localPath_NoExtensions);
    string GetContainerFolder(string parentFolderPath);
    string GetLocalPath(out bool isFolder);
    void   DeleteUnapprovedFiles(string parentFolderPath);
    bool   TryLoadFile(string parentPath, string fileName, out string contents);
    bool   TryLoadFile(string path, out string contents, bool autoAddExtension = true);
}

public class IBaseSaveable   // interface; ISaveable-adjacent, ordering only
{
    int LoadOrder { get; }
}
```

`LocalExtraFiles` / `LocalExtraFolders` are the **whitelist** consulted by
`DeleteUnapprovedFiles`. A saveable that writes a subfile must add its relative
path to `LocalExtraFiles`, otherwise the next save deletes it.

`IGenericSaveable` is the lightweight cousin:

```csharp
public class IGenericSaveable
{
    Il2CppSystem.Guid GUID { get; }
    Datas.GenericSaveData GetSaveData();
    void InitializeSaveable();
    void Load(Datas.GenericSaveData data);
}
```

### 1.6 The `Loader` hierarchy

Base type (`Il2CppScheduleOne.Persistence.Loaders.Loader`, base
`Il2CppSystem.Object`):

```csharp
public class Loader
{
    virtual void Load(string mainPath);
    Il2CppSystem.Collections.Generic.List<Il2CppSystem.IO.DirectoryInfo> GetDirectories(string parentPath);
    Il2CppSystem.Collections.Generic.List<Il2CppSystem.IO.FileInfo>      GetFiles(string parenPath);   // sic, typo in game
    static bool TryDeserialize<T>(string json, out T data);
    bool TryLoadFile(string parentPath, string fileName, out string contents);
    bool TryLoadFile(string path, out string contents, bool autoAddExtension = true);
}
```

Three specialization families, all in `Persistence.Loaders`:

1. **Top-level file/folder loaders** — override `Load(string mainPath)` only:
   `GameDataLoader`, `MetadataLoader`, `TimeLoader`, `MoneyLoader`, `RankLoader`,
   `LawLoader`, `SewerLoader`, `CartelLoader`, `VariablesLoader`, `QuestsLoader`,
   `ProductManagerLoader`, `ShopManagerLoader`, `ShopLoader`, `TrashLoader`,
   `GraffitiLoader`, `DeliveriesLoader`, `GenericSaveablesLoader`,
   `PlayersLoader`, `PlayerLoader`, `PropertiesLoader`, `PropertyLoader`,
   `BusinessesLoader`, `BusinessLoader`, `VehiclesLoader`, `VehicleLoader`,
   `StorageLoader`, `PlaceableStorageEntityLoader`.
2. **Buildable/world-object loaders** — derive from `BuildableItemLoader`
   (which has `int LoadOrder { get; }`) and override both
   `Load(string mainPath)` and `Load(Datas.DynamicSaveData data)`:
   `GridItemLoader`, `SurfaceItemLoader`, `StorageSurfaceItemLoader`,
   `LabelledSurfaceItemLoader`, `ToggleableItemLoader`,
   `ToggleableSurfaceItemLoader`, `ProceduralGridItemLoader`,
   `ChemistryStationLoader`, `LabOvenLoader`, `CauldronLoader`,
   `MixingStationLoader`, `BrickPressLoader`, `DryingRackLoader`,
   `PackagingStationLoader` (`: GridItemLoader`), `PotLoader`,
   `MushroomBedLoader`, `SoilPourerLoader`, `SpawnStationLoader`,
   `TrashContainerLoader`, `AirConditionerLoader`, `JukeboxLoader`.
3. **NPC/employee loaders** — `NPCsLoader` (the whole-collection loader used by
   `NPCManager`), `NPCLoader`, `LegacyNPCLoader`, `DynamicLoader`,
   `EmployeeLoader`, `BotanistLoader`, `ChemistLoader`, `CleanerLoader`,
   `LegacyBotanistLoader`, `LegacyChemistLoader`, `LegacyCleanerLoader`,
   `LegacyEmployeeLoader`, `LegacyPackagerLoader`.
   Plus product loaders: `WeedProductLoader`, `MethProductLoader`,
   `CocaineProductLoader`, `ShroomProductLoader`.

Item strings are deserialized separately by
`Il2CppScheduleOne.Persistence.ItemDeserializer.LoadItem(string itemString)`,
which dispatches to `Persistence.ItemLoaders.ItemLoader` subclasses keyed by
`string ItemType { get; }`:
`CashLoader`, `ClothingLoader`, `CocaineLoader`, `IntegerItemLoader`,
`MethLoader`, `ProductItemLoader`, `QualityItemLoader`, `ShroomLoader`,
`TrashGrabberLoader`, `WateringCanLoader`, `WeedLoader`.

```csharp
public class ItemLoader
{
    string ItemType { get; }
    T LoadData<T>(string itemString);
    virtual ItemFramework.ItemInstance LoadItem(string itemString);
}
```

### 1.7 The `Datas` namespace

Root of everything (`Persistence.Datas.SaveData`):

```csharp
public class SaveData
{
    string DataType    { get; set; }   // written as the first JSON field
    int    DataVersion { get; set; }
    string GameVersion { get; set; }

    virtual int    GetDataVersion();
    virtual string GetJson(bool prettyPrint = true);
}
```

Every persisted object is a `SaveData` subclass and every JSON file on disk
starts with `DataType` / `DataVersion` / `GameVersion`. `SaveManager.PRETTY_PRINT`
controls indentation. Serialization is Unity `JsonUtility`-shaped (public
fields, `SerializableDictionary<TKey,TValue>` with parallel `keys`/`values`
lists for maps).

Top-level data classes and the file they land in:

| Data class | File written | Key fields |
|---|---|---|
| `MetaData` | `Metadata.json` | `CreationDate`, `LastPlayedDate` (both `DateTimeData`), `CreationVersion`, `LastSaveVersion`, `PlayTutorial` |
| `GameData` | `Game.json` | `OrganisationName`, `Seed`, `Settings` (`DevUtilities.GameSettings`) |
| `TimeData` | `Time.json` | `TimeOfDay`, `ElapsedDays`, `Playtime` |
| `MoneyData` | `Money.json` | `OnlineBalance`, `Networth`, `LifetimeEarnings`, `WeeklyDepositSum` |
| `Levelling.RankData`† | `Rank.json` | `Rank`, `Tier`, `XP`, `TotalXP`, `UnlockedRegions` |
| `LawData` | `Law.json` | — |
| `SewerData` | `Sewer.json` | `IsSewerUnlocked`, `IsRandomWorldKeyCollected`, `RandomSewerKeyLocationIndex`, `HasSewerKingBeenDefeated`, `HoursSinceLastSewerGoblinAppearance`, `RandomKeyPossessorIndex`, `RandomKeyPossessorSet`, `ActiveMushroomLocationIndices` |
| `CartelData` | `Cartel.json` | + `Persistence.CartelRegionalActivityData` (`Region`, `CurrentActivityIndex`, `HoursUntilNextActivity`) |
| `VariableCollectionData` | `Variables.json` | `Variables[]` of `VariableData { Name, Value }` |
| `QuestManagerData` | `Quests.json` | `QuestData` / `QuestEntryData` |
| `ProductManagerData` | `Products.json` | `ProductData`, `WeedProductData`, `MethProductData`, `CocaineProductData`, `ShroomProductData` |
| `ShopManagerData` | `Shops.json` | `ShopData { ShopCode, ... }` |
| `DeliveriesData` | `Deliveries.json` | `ContractData` |
| `TrashData` | `Trash.json` | `TrashItemData`, `TrashBagData`, `TrashGeneratorData`, `TrashContainerData` |
| `NPCCollectionData` | `NPCs.json` | `NPCs[]` of `NPCData` (each a nested `DynamicSaveData`) |
| `VehicleCollectionData` | `OwnedVehicles.json` | `VehicleData` |
| `WorldStorageEntitiesData` | `WorldStorageEntities.json` | `WorldStorageEntityData` |
| `GenericSaveablesData` | `GenericSaveables.json` | `Saveables[]` of `GenericSaveData` |
| `PlayerData` | `Players/Player_<id>/Player.json` | `PlayerCode`, `Position`, `Rotation`, `IntroCompleted` |
| `AvatarAppearanceData` | `Players/Player_<id>/Appearance.json` | — |
| `PropertyData` / `ManorData` | `Properties/<Name>.json` | `PropertyCode`, `IsOwned`, `SwitchStates[]`, `ToggleableStates[]`, `Employees[]`, `Objects[]` |
| `BusinessData` | `Businesses/<Name>.json` | `PropertyCode`, `IsOwned`, `SwitchStates[]`, `ToggleableStates[]`, `LaunderingOperations[]` (`LaunderOperationData`) |
| `GraffitiData` | `Graffiti.json` | `SpraySurfaceData`, `WorldSpraySurfaceData` |

† `RankData` is the one top-level data class that does **not** live in
`Persistence.Datas`. It is
`Il2CppScheduleOne.Levelling.RankData : Persistence.Datas.SaveData`
(verified in `02-types-index.txt`). Note the British spelling `Levelling`.

**The two extension mechanisms you actually care about:**

```csharp
// 1. DynamicSaveData — "a base blob plus named side-blobs"
public class DynamicSaveData : SaveData
{
    string BaseData { get; set; }                                  // JSON string
    Il2CppSystem.Collections.Generic.List<AdditionalData> AdditionalDatas { get; set; }

    void AddData(string name, string contents);
    void AddData(string name, SaveData data);
    string GetData(string name);
    T      GetData<T>(string name, bool warn = true);
    bool   TryGetData(string name, out string data);
    bool   TryGetData<T>(string name, out T data);
    T      ExtractBaseData<T>();
    bool   TryExtractBaseData<T>(out T data);

    public class AdditionalData { string Name { get; set; } string Contents { get; set; } }
}

// 2. GenericSaveData — "a GUID-keyed bag of primitives"
public class GenericSaveData : SaveData
{
    string GUID { get; set; }
    List<BoolValue>   boolValues   { get; set; }   // { key, value }
    List<FloatValue>  floatValues  { get; set; }
    List<IntValue>    intValues    { get; set; }
    List<StringValue> stringValues { get; set; }

    void  Add(string key, bool value);
    void  Add(string key, float value);
    void  Add(string key, int value);
    void  Add(string key, string value);
    bool   GetBool  (string key, bool   defaultValue = false);
    float  GetFloat (string key, float  defaultValue = 0);
    int    GetInt   (string key, int    defaultValue = 0);
    string GetString(string key, string defaultValue = "");
}
```

`GenericSaveablesManager` (a `Singleton`) owns these:

```csharp
Il2CppSystem.Collections.Generic.List<IGenericSaveable> Saveables { get; set; }
void RegisterSaveable(IGenericSaveable saveable);
void LoadSaveable(Datas.GenericSaveData data);
```

### 1.8 The on-disk save layout (verified from real saves)

Root, observed in `research/raw/save-samples/_LISTING.txt`:

```
%USERPROFILE%\AppData\LocalLow\TVGS\Schedule I\Saves\<steamID64>\
    SaveGame_1\  SaveGame_2\  SaveGame_3\  SaveGame_4\
    Backups\<orgname> (<version>, save slot N).zip
    steam_autocloud.vdf
    WriteTest.txt          <- SaveManager.HasWritePermissionOnDir probe
```

Current (0.4.x) slot layout:

```
SaveGame_1\
    Metadata.json                  680 B
    Game.json                      234 B
    Time.json                      152 B
    Money.json                     219 B
    Rank.json                      250 B
    Law.json                       115 B
    Sewer.json                     464 B
    Shops.json                     770 B
    Cartel.json                   1.7 KB
    Variables.json                9.6 KB
    Quests.json                    67 KB
    Products.json                  46 KB
    Deliveries.json                58 KB
    Graffiti.json                  98 KB
    Trash.json                    373 KB
    NPCs.json                     474 KB     <- ALL NPCs in one file now
    OwnedVehicles.json             739 B
    GenericSaveables.json          16 KB
    WorldStorageEntities.json     8.7 KB
    Businesses\
        Car Wash.json   Laundromat.json   Post Office.json   Taco Ticklers.json
    Properties\
        Barn.json  Bungalow.json  Docks Warehouse.json  Hyland Manor.json
        Motel Room.json  RV.json  Sewer Office.json  Storage Unit.json  Sweatshop.json
    Players\
        Player_0\                              <- host / local player
            Player.json  Appearance.json  Clothing.json  Inventory.json  Variables.json
        Player_76561198708930278\              <- co-op peer, folder = steamID64
            (same five files)
    Modded\                                    <- created by S1API, see §1.9
        Quests\
        Saveables\
```

The legacy (0.3.x) layout, still readable by the loaders, exploded everything
into folders — `Businesses\<Name>\Business.json` +
`Businesses\<Name>\Objects\<objectid_hash>\Data.json`, and
`NPCs\<NPC Name>\NPC.json` + `Relationship.json` + `CustomerData.json`, and
`WorldStorageEntities\Entity_<hash>.json`. That is why `ISaveable` has both
`ShouldSaveUnderFolder` and `GetLocalPath(out bool isFolder)`, and why
`LegacyNPCLoader` / `LegacyEmployeeLoader` exist.

Real file contents (abridged only where noted):

`Metadata.json` — complete:

```json
{
    "DataType": "MetaData",
    "DataVersion": 0,
    "GameVersion": "0.4.6f11",
    "CreationDate": {
        "DataType": "DateTimeData", "DataVersion": 0, "GameVersion": "0.4.6f11",
        "Year": 2025, "Month": 12, "Day": 17, "Hour": 17, "Minute": 40, "Second": 58
    },
    "LastPlayedDate": {
        "DataType": "DateTimeData", "DataVersion": 0, "GameVersion": "0.4.6f11",
        "Year": 2026, "Month": 8, "Day": 2, "Hour": 1, "Minute": 46, "Second": 2
    },
    "CreationVersion": "0.4.1f13",
    "LastSaveVersion": "0.4.6f11",
    "PlayTutorial": false
}
```

`Time.json` and `Game.json` — complete:

```json
{ "DataType": "TimeData", "DataVersion": 0, "GameVersion": "0.4.6f11",
  "TimeOfDay": 700, "ElapsedDays": 60, "Playtime": 101149 }
```
```json
{ "DataType": "GameData", "DataVersion": 0, "GameVersion": "0.4.6f11",
  "OrganisationName": "agies", "Seed": 709510097,
  "Settings": { "ConsoleEnabled": true, "UseRandomizedMixMaps": false } }
```

Note `TimeOfDay` is **24-hour clock as an integer**, not minutes: `700` = 07:00.

`Money.json` and `Rank.json` — complete:

```json
{ "DataType": "MoneyData", "DataVersion": 0, "GameVersion": "0.4.6f11",
  "OnlineBalance": 42701.7578125, "Networth": 1390598.75,
  "LifetimeEarnings": 1042278.4375, "WeeklyDepositSum": 10000.0 }
```
```json
{ "DataType": "RankData", "DataVersion": 1, "GameVersion": "0.4.6f11",
  "Rank": 10, "Tier": 30, "XP": 903, "TotalXP": 124603,
  "UnlockedRegions": [0, 1, 2, 3, 4, 5] }
```

`Players/Player_0/Player.json` — complete. The folder name is `Player_0` for
the local/host player and `Player_<steamID64>` for co-op peers, while
`PlayerCode` inside is always the steamID64:

```json
{ "DataType": "PlayerData", "DataVersion": 0, "GameVersion": "0.4.6f11",
  "PlayerCode": "76561199588797561",
  "Position": { "x": 194.976806640625, "y": 4.914999485015869, "z": -12.526748657226563 },
  "Rotation": 294.21136474609377,
  "IntroCompleted": true }
```

`Properties/Hyland Manor.json` — complete, shows the `ManorData` subclass of
`PropertyData`:

```json
{ "DataType": "ManorData", "DataVersion": 0, "GameVersion": "0.4.6f11",
  "PropertyCode": "manor", "IsOwned": false,
  "SwitchStates": [false,false,false,false,false,false,false,false],
  "ToggleableStates": [], "Employees": [], "Objects": [],
  "ManorState": 0, "DaysSinceStateChange": 1058, "TunnelDug": true }
```

`GenericSaveables.json` — abridged (16 KB of these):

```json
{
    "DataType": "GenericSaveablesData", "DataVersion": 0, "GameVersion": "0.4.6f11",
    "Saveables": [
        {
            "DataType": "GenericSaveData", "DataVersion": 0, "GameVersion": "0.4.6f11",
            "GUID": "73959fcd-042c-4df5-8f90-aa9503c3a3c5",
            "boolValues":   [ { "key": "broken",         "value": false } ],
            "floatValues":  [],
            "intValues":    [ { "key": "daysUntilRepair", "value": -1 } ],
            "stringValues": []
        }
        /* ... one entry per registered IGenericSaveable ... */
    ]
}
```

`NPCs.json` — abridged. This is the clearest illustration of `DynamicSaveData`:
a JSON-**string**-encoded `BaseData` plus named `AdditionalDatas` whose
`Contents` are themselves JSON strings (double-escaped on disk):

```json
{
    "DataType": "NPCCollectionData", "DataVersion": 0, "GameVersion": "0.4.6f11",
    "NPCs": [
        {
            "DataType": "NPCData", "DataVersion": 0, "GameVersion": "0.4.6f11",
            "BaseData": "{\"DataType\":\"NPCData\",\"DataVersion\":0,\"GameVersion\":\"0.4.6f11\",\"ID\":\"donna_martin\"}",
            "AdditionalDatas": [
                { "Name": "Relationship",
                  "Contents": "{\n \"DataType\": \"RelationshipData\", ... \"RelationDelta\": 5.0, \"Unlocked\": true, \"UnlockType\": 1\n}" },
                { "Name": "MessageConversation",
                  "Contents": "{\n \"DataType\": \"MSGConversationData\", ... \"ConversationIndex\": 64, \"Read\": true, \"MessageHistory\": [ ... ] }" }
            ]
        }
    ]
}
```

`Variables.json` — abridged; a flat name/value store where every value is a
string, including booleans (`"False"`) and numbers (`"1114"`):

```json
{ "DataType": "VariableCollectionData", "DataVersion": 0, "GameVersion": "0.4.6f11",
  "Variables": [
    { "DataType": "VariableData", "DataVersion": 0, "GameVersion": "0.4.6f11",
      "Name": "Completed_Contracts_Count", "Value": "1114" },
    { "DataType": "VariableData", "DataVersion": 0, "GameVersion": "0.4.6f11",
      "Name": "Loan_Sharks_Arrived", "Value": "False" }
  ] }
```

Legacy `Businesses/Car Wash/Objects/smallstoragerack_2cd9f9/Data.json` — the
per-object format, complete, showing the nested `ItemString` pattern used
everywhere for inventories:

```json
{ "DataType": "PlaceableStorageData", "DataVersion": 0, "GameVersion": "0.3.0",
  "GUID": "2cd9f956-ea18-4301-8051-abf546727be7",
  "ItemString": "{\n \"DataType\": \"ItemData\", ... \"ID\": \"smallstoragerack\", \"Quantity\": 1\n}",
  "LoadOrder": 0,
  "GridGUID": "a94f4d7e-de6e-4f6e-8c60-aa0b2c5ab190",
  "OriginCoordinate": { "x": 1.0, "y": 5.0 },
  "Rotation": 180,
  "Contents": { "Items": [
      "{\"DataType\":\"ItemData\",\"DataVersion\":0,\"GameVersion\":\"0.3.0\",\"ID\":\"\",\"Quantity\":0}",
      "{\"DataType\":\"ItemData\",\"DataVersion\":0,\"GameVersion\":\"0.3.0\",\"ID\":\"\",\"Quantity\":0}"
  ] } }
```

### 1.9 Recommended pattern for per-save mod data

**Rule zero: do not implement `ISaveable` yourself.**
Registering a hand-rolled `ISaveable` into `SaveManager.Saveables` means you
also inherit `DeleteUnapprovedFiles` and `ClearBaseLevelOutdatedSaves`, and an
IL2CPP interface implementation requires injecting a managed type into the
IL2CPP domain (`ClassInjector.RegisterTypeInIl2Cpp` + interface list). It works,
but it is the highest-risk option and any exception thrown from your
`GetSaveString()` runs *inside the vanilla save coroutine* — i.e. you can
abort the player's save.

**Preferred: `S1API.Internal.Abstraction.Saveable`.**
S1API is already installed and already solves this. Subclass `Saveable`, tag
fields with `[SaveableField("name")]`, and S1API's auto-registry
(`S1API.Saveables.SaveableAutoRegistry`) discovers and instantiates it:

```csharp
using S1API.Internal.Abstraction;
using S1API.Saveables;

public class MyModSave : Saveable
{
    [SaveableField("creative_settings")] public CreativeSettings Settings = new();

    protected override void OnLoaded() { /* apply to world */ }
    protected override void OnSaved()  { }
}
```

Relevant verified S1API surface (`ns-S1API.Internal.Abstraction.txt`,
`ns-S1API.Saveables.txt`):

```csharp
public abstract class Saveable : Registerable, ISaveable
{
    SaveableLoadOrder LoadOrder { get; }              // BeforeBaseGame = 0, AfterBaseGame = 1
    protected virtual void OnLoaded();
    protected virtual void OnSaved();
    internal virtual void LoadInternal(string folderPath);
    internal virtual void SaveInternal(string folderPath, ref Il2CppSystem.Collections.Generic.List<string> extraSaveables);
    internal void SaveToDynamic(Il2CppScheduleOne.Persistence.Datas.DynamicSaveData dynamicSaveData);
    internal void LoadFromDynamic(Il2CppScheduleOne.Persistence.Datas.DynamicSaveData dynamicSaveData);
    public static bool RequestGameSave();
    public static bool RequestGameSave(bool immediate);
}

[AttributeUsage(AttributeTargets.Field)]
public class SaveableField : Attribute { public SaveableField(string saveName); string SaveName { get; } }

public enum SaveableLoadOrder { BeforeBaseGame = 0, AfterBaseGame = 1 }
```

Serialization is Newtonsoft (`ISaveable.SerializerSettings` is a
`Newtonsoft.Json.JsonSerializerSettings`, and there is a
`GUIDReferenceConverter : JsonConverter`), so you get real object graphs, not
Unity's `JsonUtility` restrictions.

Where it lands: a `Modded\` subtree inside the save slot. Two of the four real
save slots on this machine contain `Modded\Quests\` and `Modded\Saveables\`.
Vanilla never writes there, and vanilla's pruning is scoped per-saveable
container / to `ApprovedBaseLevelPaths`, so those directories survive. I
verified the folders empirically from `_LISTING.txt`; the exact folder-name
constants live in S1API string literals which are not in these dumps, so treat
the literal names `Modded/Quests` and `Modded/Saveables` as
**observed-on-disk, not metadata-verified**.

**Fallback if you do not want an S1API dependency:** write your own JSON next to
the save, keyed off the load path, and drive it from the manager events:

```csharp
private static string _saveDir;

// on LoadManager.onLoadComplete
_saveDir = LoadManager.Instance.LoadedGameFolderPath;
var path = Path.Combine(_saveDir, "Mods", "CreativeMode.json");
if (File.Exists(path)) _state = JsonConvert.DeserializeObject<State>(File.ReadAllText(path));

// on SaveManager.onSaveComplete   (NOT onSaveStart — write after the game is done)
Directory.CreateDirectory(Path.GetDirectoryName(path));
File.WriteAllText(path, JsonConvert.SerializeObject(_state, Formatting.Indented));
```

Rules that keep this safe:

- **Write on `onSaveComplete`, not `onSaveStart`.** The vanilla save coroutine
  is still enumerating directories during the save; adding files mid-pass can
  race `DeleteUnapprovedFiles`.
- **Use your own top-level folder** (`Mods/` or `Modded/`), never a bare file at
  slot root — `ClearBaseLevelOutdatedSaves` prunes base-level entries against
  `ApprovedBaseLevelPaths`.
- **Never write inside `Properties/`, `Businesses/`, `Players/` or any other
  saveable's container folder** unless you also add your relative path to that
  saveable's `LocalExtraFiles`. Those are exactly the folders
  `DeleteUnapprovedFiles` sweeps.
- **Never mutate vanilla JSON files.** `SaveData.DataVersion` is how the game
  migrates formats; a hand-edited file with the wrong version is how saves die.
- **Do everything only on the host.** `LoadManager.LoadAsClient` never reads the
  local save folder — a client's `LoadedGameFolderPath` is not the authoritative
  save. See the networking doc.
- **Guard your load path.** A save written by a newer version of your mod will
  land in an older build; version your own blob and fail soft.

**When the mod is later removed:** with the additive pattern above, nothing
breaks. Your `Mods/CreativeMode.json` (or `Modded/Saveables/*`) becomes an inert
orphan file that vanilla ignores — vanilla only enumerates the folders its own
saveables own, and unknown top-level folders are not in any saveable's sweep
path. The save loads normally. The *world state* you changed (money, owned
properties, spawned objects) persists, because that went through the game's own
saveables — which is the correct outcome. The only way to break a vanilla load
is to corrupt a vanilla file, which the additive pattern never touches.

Counter-example of what NOT to do: appending your own keys to
`GenericSaveables.json` entries or adding a synthetic `GenericSaveData` with
your own GUID. `GenericSaveablesManager.LoadSaveable` looks up saveables by
GUID (`_LoadSaveable_b__0(IGenericSaveable x)`), so an orphan GUID after
uninstall is at best a warning and at worst a lookup failure inside the vanilla
load loop.

---

## Part 2 — Time

Source dumps: `ns-Il2CppScheduleOne.GameTime.txt`, `ns-Il2CppScheduleOne.Law.txt`,
`ns-S1API.GameTime.txt`, `ns-S1API.Law.txt`, `ns-Il2CppSystem.txt`, `ns-Il2Cpp.txt`.

### 2.1 Time representation

Three coexisting representations — do not mix them up:

1. **24-hour integer** (`CurrentTime`, `SetTime`, `Time.json` `TimeOfDay`):
   `700` = 07:00, `1530` = 15:30, `2100` = 21:00. This is the one the console
   and save files use.
2. **Minute sum** (`GetMinSum`, `GetTotalMinSum`, `DailyMinSum`): minutes since
   midnight (and, for `GetTotalMinSum`, since day 0).
3. **`GameDateTime` struct**: `{ int elapsedDays; int time; }`.

```csharp
public struct Il2CppScheduleOne.GameTime.GameDateTime   // System.ValueType
{
    public int elapsedDays;   // FieldOffset(0)
    public int time;          // FieldOffset(4)

    public GameDateTime(int _elapsedDays, int _time);
    public GameDateTime(int _minSum);
    public GameDateTime(Persistence.Datas.GameDateTimeData data);

    public GameDateTime AddMins(int mins);
    public GameDateTime GetCopy();
    public int GetMinSum();
    // op_Addition, op_Subtraction, op_LessThan, op_GreaterThan,
    // op_LessThanOrEqual, op_GreaterThanOrEqual
}

public enum Il2CppScheduleOne.GameTime.EDay
{ Monday = 0, Tuesday = 1, Wednesday = 2, Thursday = 3, Friday = 4, Saturday = 5, Sunday = 6 }
```

### 2.2 `TimeManager` — state

`Il2CppScheduleOne.GameTime.TimeManager : NetworkSingleton<TimeManager>`
(so it is a `FishNet.Object.NetworkBehaviour` — the host owns time).

```csharp
// ---- static constants (values are runtime statics; see note) ----
static float DefaultCycleDuration { get; set; }
static float TickDuration         { get; set; }
static float CycleDuration        { get; set; }
static float MinuteDuration       { get; }
static int   EndOfDay             { get; set; }
static int   WakeTime             { get; set; }

// ---- current state ----
int   DefaultTime          { get; set; }
int   CurrentTime          { get; set; }   // 24-hour integer, e.g. 1530
EDay  CurrentDay           { get; }
int   ElapsedDays          { get; set; }
int   DayIndex             { get; }
bool  IsEndOfDay           { get; }
bool  IsNight              { get; }
float NormalizedTimeOfDay  { get; }        // 0..1 through the day
bool  IsSleepInProgress    { get; set; }
bool  HostSleepDone        { get; set; }
float Playtime             { get; set; }
float TimeSpeedMultiplier  { get; set; }
int   DailyMinSum          { get; set; }

// ---- ISaveable side ----
Persistence.Loaders.TimeLoader loader { get; set; }
string SaveFolderName { get; }
string SaveFileName   { get; }
int    LoadOrder      { get; }
```

### 2.3 `TimeManager` — methods

```csharp
// conversions & queries (all static unless noted)
static int    AddMinutesTo24HourTime(int time, int minsToAdd);
static string Get12HourTime(float _time, bool appendDesignator = true);
static int    Get24HourTimeFromMinSum(int minSum);
static int    GetMinSumFrom24HourTime(int _time);
static string GetMinutesToDisplayTime(int minutes);
static bool   IsGivenTimeWithinRange(int givenTime, int min, int max);
static bool   IsValid24HourTime(string input);
static bool   IsValid24HourTime(int time);

GameDateTime GetDateTime();
int          GetTotalMinSum();
bool         IsCurrentTimeWithinRange(int min, int max);
bool         IsCurrentDateWithinRange(GameDateTime start, GameDateTime end);

// mutation  (server-authoritative — see networking doc)
void SetTime(int time);
void SetTimeAndSync(int time);
void SkipForwardToTime(int newTime);
void SetCycleDuration(float time);
void SetTimeSpeedMultiplier(float multiplier);
void SetHostSleepDone(bool done);
void StartSleep();
void CheckSleepStart();
void PassMinute();

// persistence
void   Load(Persistence.Datas.TimeData timeData);
string GetSaveString();
void   InitializeSaveable();

// RPC plumbing (do not call directly)
void SetTimeData_Client(FishNet.Connection.NetworkConnection conn, int elapsedDays, int time, uint serverTick);
void OnTimeSkip_Client(int oldTime, int newTime);
void PassMinute_Client(int oldTime);
// + RpcWriter___* / RpcReader___* / RpcLogic___* generated pairs
```

Internally time advances from two coroutines, `TimeLoop()` and `TickLoop()`,
gated by `ShouldMinutePass()` and `_secondsOnCurrentMinute` /
`_lastMinWaitExcess`.

### 2.4 The exact event fields

These are **fields exposed as properties**, not C# `event`s. Two different
delegate shapes:

```csharp
// --- Il2Cpp.ActionList (a game-specific multicast list) ---
Il2Cpp.ActionList onMinutePass          { get; set; }
Il2Cpp.ActionList onUncappedMinutePass  { get; set; }
Il2Cpp.ActionList onTick                { get; set; }

// --- Il2CppSystem.Action / Action<T> ---
Il2CppSystem.Action        onTimeChanged { get; set; }
Il2CppSystem.Action<int>   onTimeSkip    { get; set; }   // arg = skipped minutes
Il2CppSystem.Action        onTimeSet     { get; set; }
Il2CppSystem.Action        onHourPass    { get; set; }
Il2CppSystem.Action        onDayPass     { get; set; }
Il2CppSystem.Action        onWeekPass    { get; set; }
Il2CppSystem.Action        onUpdate      { get; set; }
Il2CppSystem.Action        onFixedUpdate { get; set; }
Il2CppSystem.Action        onSleepStart  { get; set; }
Il2CppSystem.Action        onSleepEnd    { get; set; }
```

Curfew events live on a different manager
(`Il2CppScheduleOne.Law.CurfewManager : NetworkSingleton<CurfewManager>`) and
are `UnityEvent`s, not `Action`s:

```csharp
UnityEngine.Events.UnityEvent onCurfewEnabled   { get; set; }
UnityEngine.Events.UnityEvent onCurfewDisabled  { get; set; }
UnityEngine.Events.UnityEvent onCurfewHint      { get; set; }
UnityEngine.Events.UnityEvent onCurfewWarning   { get; set; }
UnityEngine.Events.UnityEvent onCurfewStart     { get; set; }
UnityEngine.Events.UnityEvent onCurfewHardStart { get; set; }
UnityEngine.Events.UnityEvent onCurfewEnd       { get; set; }

bool IsEnabled         { get; set; }
bool IsCurrentlyActive { get; set; }
bool IsHardCurfewActive{ get; set; }
void Enable(FishNet.Connection.NetworkConnection conn);   // ObserversRpc/TargetRpc pair
void Disable();
void OnUncappedMinPass();
```

There is also `Il2CppScheduleOne.GameTime.TimeUnityEvents : MonoBehaviour`
which mirrors four of these as `UnityEvent`s for the Unity inspector
(`onHourPass`, `onDayPass`, `onSleepStart`, `onSleepEnd`) — useful if you'd
rather bind through a `UnityEvent` than an `Il2CppSystem.Action`, but you have
to find the component instance in the scene.

And a helper for "run this in N in-game minutes":

```csharp
public class Il2CppScheduleOne.GameTime.TimedCallback
{
    public TimedCallback(Il2CppSystem.Action callback, int durationMinutes,
                         bool tickAtEndOfDay = true, bool tickOnTimeSkip = true);
    void Cancel(); void Cleanup(); void Execute(); void Reset(); void Tick();
    void OnTimeSkip(int skippedMinutes);
    int _remainingMinutes { get; set; }
}
```

### 2.5 Subscribing from a MelonLoader IL2CPP mod — the exact form

`Il2CppSystem.Action` is a sealed class over `Il2CppSystem.MulticastDelegate`
with these operators (verified in `ns-Il2CppSystem.txt`):

```csharp
public static Il2CppSystem.Action op_Implicit(System.Action);
public static Il2CppSystem.Action op_Addition(Il2CppSystem.Action, Il2CppSystem.Action);
public static Il2CppSystem.Action op_Subtraction(Il2CppSystem.Action, Il2CppSystem.Action);
```

Because `op_Implicit` exists, `+=` with a managed `System.Action` compiles. But
**you must cache the converted `Il2CppSystem.Action`** — each conversion
allocates a *new* IL2CPP delegate object, so `-=` with a freshly-converted
delegate silently removes nothing and you leak a subscription per hot-reload.
Also keep the managed delegate rooted in a static field or the native→managed
trampoline can be collected and you get a hard crash inside the game's
`Invoke()`.

```csharp
using Il2CppScheduleOne.GameTime;

// keep BOTH alive for the lifetime of the subscription
private static System.Action        _managedDayPass;
private static Il2CppSystem.Action  _nativeDayPass;

private static void Subscribe()
{
    if (!TimeManager.InstanceExists) return;
    var tm = TimeManager.Instance;

    _managedDayPass = OnDayPass;
    _nativeDayPass  = (Il2CppSystem.Action)_managedDayPass;   // op_Implicit
    tm.onDayPass    = (Il2CppSystem.Action)(tm.onDayPass + _nativeDayPass);   // op_Addition
}

private static void Unsubscribe()
{
    if (!TimeManager.InstanceExists) return;
    var tm = TimeManager.Instance;
    tm.onDayPass = (Il2CppSystem.Action)(tm.onDayPass - _nativeDayPass);      // op_Subtraction
}

private static void OnDayPass() { /* ... */ }
```

`Action<int>` (only `onTimeSkip`) is identical with
`Il2CppSystem.Action<int>` and `System.Action<int>`.

`Il2Cpp.ActionList` (`onMinutePass`, `onUncappedMinutePass`, `onTick`) is a
different animal — a game class wrapping `List<Il2CppSystem.Action>`:

```csharp
public class Il2Cpp.ActionList
{
    Il2CppSystem.Collections.Generic.List<Il2CppSystem.Action> list { get; set; }
    void Add(Il2CppSystem.Action action);
    void Remove(Il2CppSystem.Action action);
    void Clear();
    void InvokeAll();
    void InvokeAllStaggered(float staggerTime);
    static ActionList op_Addition(ActionList list, Il2CppSystem.Action action);
    static ActionList op_Subtraction(ActionList list, Il2CppSystem.Action action);
}
```

Use `Add`/`Remove` directly — clearer than the operators and no reassignment:

```csharp
private static Il2CppSystem.Action _nativeMinute;

_nativeMinute = (Il2CppSystem.Action)(System.Action)OnMinutePass;
TimeManager.Instance.onMinutePass.Add(_nativeMinute);
// later
TimeManager.Instance.onMinutePass.Remove(_nativeMinute);
```

**Timing of subscription:** `TimeManager` is a `NetworkSingleton`, destroyed on
exit-to-menu and recreated per game load. Subscribe from
`LoadManager.Instance.onLoadComplete` (or after `LoadManager.Instance.IsGameLoaded`
turns true), and re-subscribe every load. Do not subscribe in
`OnApplicationStart` / `OnInitializeMelon`.

**If you would rather not deal with any of this**, S1API already wraps it in
plain managed `System.Action` statics (`ns-S1API.GameTime.txt`), including the
`ActionList` case via its private `AddToActionList`:

```csharp
public static class S1API.GameTime.TimeManager
{
    public static System.Action      OnHourPass;
    public static System.Action      OnDayPass;
    public static System.Action      OnWeekPass;
    public static System.Action      OnSleepStart;
    public static System.Action<int> OnSleepEnd;    // arg = minutes skipped
    public static System.Action      OnTick;

    static Day   CurrentDay      { get; }
    static int   ElapsedDays     { get; }
    static int   CurrentTime     { get; }
    static bool  IsNight         { get; }
    static bool  IsEndOfDay      { get; }
    static bool  SleepInProgress { get; }
    static float NormalizedTime  { get; }
    static float Playtime        { get; }

    static void   SetTime(int time24h);
    static int    Get24HourTimeFromMinutes(int minutes);
    static int    GetMinutesFrom24HourTime(int time24h);
    static string GetFormatted12HourTime();
    static bool   IsCurrentTimeWithinRange(int startTime24h, int endTime24h);
}
```

Note S1API's `OnSleepEnd` is `Action<int>` (it fuses the game's `onSleepEnd`
with `onTimeSkip` to report skipped minutes), while the game's own `onSleepEnd`
is parameterless. S1API also re-binds itself on scene reload
(`TryBindToCurrentInstance` / `ResetBindings`), which removes the
resubscribe-on-load chore.

### 2.6 Day/night and curfew constants

Day/night: `TimeManager.IsNight` is a computed property and the *thresholds*
are runtime static fields (`EndOfDay`, `WakeTime`, `CycleDuration`,
`DefaultCycleDuration`, `TickDuration`). **Numeric values for
`TimeManager.EndOfDay` / `WakeTime` are not present in the metadata dumps** —
IL2CPP static field initializers are code, not metadata. Read them at runtime:

```csharp
MelonLogger.Msg($"EndOfDay={TimeManager.EndOfDay} WakeTime={TimeManager.WakeTime} " +
                $"CycleDuration={TimeManager.CycleDuration} TickDuration={TimeManager.TickDuration}");
```

Mark those four values **UNVERIFIED** until dumped at runtime.

Curfew is different — S1API mirrors the game constants as real `const`
literals, so these **are** verified (`ns-S1API.Law.txt`):

```csharp
public static class S1API.Law.CurfewManager
{
    public const int HourBeforeCurfew     = 2000;   // 20:00 — hint window opens
    public const int WarningTime          = 2030;   // 20:30 — warning broadcast
    public const int CurfewStartTime      = 2100;   // 21:00 — curfew begins
    public const int HardCurfewStartTime  = 2115;   // 21:15 — hard curfew
    public const int CurfewEndTime         = 500;   // 05:00 — curfew ends

    static bool IsEnabled          { get; }
    static bool IsCurrentlyActive  { get; }
    static bool IsHardCurfewActive { get; }
    static void EnableCurfew();
    static void DisableCurfew();
    static bool IsWithinCurfewHours();
    static bool IsWithinHardCurfewHours();
    static int  MinutesUntilCurfew();
    static int  MinutesUntilCurfewEnds();
}
```

These mirror the game's own
`Il2CppScheduleOne.Law.CurfewManager.HOUR_BEFORE_CURFEW`, `WARNING_TIME`,
`CURFEW_START_TIME`, `HARD_CURFEW_START_TIME`, `CURFEW_END_TIME` static
properties. S1API's constants were authored against the same build, but they
are a *copy* — if you need certainty, read the game's statics at runtime.

Console-side equivalents that let you drive all of this without touching the
managers at all (see `research/API-NETWORKING-CONSOLE.md`):
`settime 1530`, `setdayduration 24`, `settimescale 1`, `forcesleep`.

---

## UNVERIFIED summary for this document

| Item | Status |
|---|---|
| S1API save folder literals `Modded/Quests`, `Modded/Saveables` | Observed on disk in two real save slots; the string constants are not in the metadata dumps. |
| `TimeManager.EndOfDay`, `WakeTime`, `CycleDuration`, `DefaultCycleDuration`, `TickDuration` numeric values | Static field values are not in IL2CPP metadata. Read at runtime. |
| `Il2CppScheduleOne.Law.CurfewManager` numeric constants | Values taken from S1API's `const` mirrors, not from the game assembly directly. |

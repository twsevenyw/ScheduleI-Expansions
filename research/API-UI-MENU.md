# Schedule I — UI / Main-Menu API reference (IL2CPP)

> Source of truth: `research/raw/*` (metadata dumps of the installed build) + read-only inspection of
> `C:\Program Files (x86)\Steam\steamapps\common\Schedule I\Schedule I_Data`.
> Every type/member signature below is copied verbatim from `research/raw/ns/ns-<Namespace>.txt`.
> Unity **2022.3.62f2** (confirmed literal, `strings-globalgamemanagers-ordered.txt:1715`).
> Nothing here was obtained by running the game.

## TL;DR — the recommended injection point

**Harmony `Postfix` on `Il2CppScheduleOne.UI.MainMenu.SettingsScreen.Awake()`**
(original name for Harmony-by-string: `ScheduleOne.UI.MainMenu.SettingsScreen`, `Awake`), then append one
`Il2CppScheduleOne.UI.MainMenu.SettingsScreen.SettingsCategory` to the public
`Categories` array, where `Toggle` is a clone of an existing category tab toggle and `Panel` is a clone of an
existing category panel.

Why this is the answer: the game's own `SettingsScreen.ShowCategory(System.Int32 index)` and its
`OnEnable()` toggle wiring iterate `Categories`, so a *data-only* append gives you a real category tab
— correct font, sprites, hover/scale animation, gamepad tab-cycling and radio-group behaviour — with **zero
shipped assets and no path strings**. The same `SettingsScreen` component type is reachable from the pause
menu too (see §5), so one patch covers both surfaces. Full code sketch in
[Recommended injection strategy](#recommended-injection-strategy).

---

## Assemblies & namespaces

| Assembly | Version | In-scope namespaces (public/total types) |
|---|---|---|
| `Assembly-CSharp` | 0.0.0.0 | `Il2CppScheduleOne` 48/48 · `Il2CppScheduleOne.UI` 101/101 · `.UI.MainMenu` 16/16 · `.UI.Settings` 28/28 · `.UI.Phone` 10/10 · `.UI.Phone.ContactsApp` 2/2 · `.UI.Phone.Delivery` 5/5 · `.UI.Phone.Map` 1/1 · `.UI.Phone.Messages` 8/8 · `.UI.Phone.ProductManagerApp` 3/3 · `.UI.Input` 17/17 · `.UI.Tooltips` 3/3 · `.UI.Items` 16/16 · `.UI.Management` 49/49 · `.UI.Multiplayer` 1/1 · `.CustomUI` 5/5 · `.DevUtilities` 52/52 · `.Configuration` 4/4 · `.State` 9/9 |
| `Il2CppScheduleOne.Core` | 0.0.0.0 | `Il2CppScheduleOne.Core.Settings` 2/2 · `Il2CppScheduleOne.Core.Settings.Framework` 8/8 |
| `Unity.TextMeshPro` | 0.0.0.0 | `Il2CppTMPro` 128/128 · `Il2CppTMPro.SpriteAssetUtilities` 2/2 |
| `UnityEngine.UI` | 1.0.0.0 | `UnityEngine.UI` 67/67 · `UnityEngine.EventSystems` 40/40 |
| `S1API` | 3.1.4.0 | `S1API.UI` 3 · `S1API.PhoneApp` 3 · `S1API.Internal.Phone` 1 · `S1API.Internal.Patches` (Harmony patch set) |

Namespace-naming rules for this build:

* Il2CppInterop prefixes every IL2CPP namespace with `Il2Cpp`. The **original** (unprefixed) name is what
  Harmony-by-string and `[ObfuscatedName]`/`[OriginalName]` attributes use. Confirmed pairs from
  `strings-global-metadata-ordered.txt`:
  * `Il2CppScheduleOne.UI.MainMenu.MainMenuController` ← `ScheduleOne.UI.MainMenu|MainMenuController` (:152616)
  * `Il2CppScheduleOne.UI.MainMenu.MenuScreen` ← `ScheduleOne.UI.MainMenu|MenuScreen` (:152620)
  * `Il2CppScheduleOne.UI.MainMenu.SettingsScreen` ← `ScheduleOne.UI.MainMenu|SettingsScreen` (:152766)
  * `Il2CppScheduleOne.UI.MainMenu.SettingsScreen+SettingsCategory` ← `ScheduleOne.UI.MainMenu.SettingsScreen|SettingsCategory` (:152767)
* Source paths are also in the metadata and are useful for guessing sibling types:
  `\Assets\Scripts\UI\MainMenu\MenuScreen.cs` (:154649), `\Assets\Scripts\UI\Settings\SettingsScreen.cs` (:154788).
  Note the mismatch: `SettingsScreen.cs` lives under `UI\Settings\` but declares namespace `ScheduleOne.UI.MainMenu`.
* TMPro is `Il2CppTMPro` (**not** `TMPro`). `UnityEngine.UI` / `UnityEngine.Events` / `UnityEngine.EventSystems`
  keep their real names (no `Il2Cpp` prefix) because Il2CppInterop only prefixes namespaces that collide.

Il2CppInterop artefacts you will see below, reported as-is:

* IL2CPP **instance fields are surfaced as C# properties.** Both `_Foo_k__BackingField` and the real `Foo`
  appear — always use `Foo`.
* `field_Private_Boolean_0`, `Method_Protected_Virtual_Void_0`,
  `Method_Private_IEnumerator_PDM_0`, `ObjectCompilerGeneratedNPrivateSealedIEnumerator1Object…Unique`
  are **fallback names for members whose real name was stripped**. They are position-dependent and
  **will change between game versions** — never bind to them from a shipped mod.
* `[interop-internal fields omitted: N]` in the dumps = Il2CppInterop's `NativeFieldInfoPtr_*` statics, not game data.

---

## 1. Main menu structure

### Scene layout (confirmed)

`strings-globalgamemanagers-ordered.txt:1712-1714` contains the build's scene list, in build order:

```
Assets/Scenes/Menu.unity
Assets/Scenes/Main.unity
Assets/Scenes/Tutorial.unity
```

so **the main-menu scene name is `Menu`** (build index 0). It maps to the level files as:

| Scene | Build index | Level file | Size | Shared assets |
|---|---|---|---|---|
| `Menu` | 0 | `level0` (no `.resS`) | 5,365,008 B | `sharedassets0.assets` (100,098,332 B) + `.resS` |
| `Main` | 1 | `level1` + `level1.resS` | 284,056,776 B | `sharedassets1.assets` |
| `Tutorial` | 2 | `level2` + `level2.resS` | 42,798,548 B | `sharedassets2.assets` |

The scene name is also reachable from code: `Il2CppScheduleOne.Persistence.SaveManager` exposes
`static System.String MENU_SCENE_NAME` / `MAIN_SCENE_NAME` / `TUTORIAL_SCENE_NAME` — prefer reading those
over hardcoding `"Menu"`. The bare literal `Menu` exists (`literals-sorted.txt:9450`), plus `Menu scene loaded`
(:9452) and `Menu PopUp` (:9451).

### Types in `Il2CppScheduleOne.UI.MainMenu` (16 types)

| Type | Base |
|---|---|
| `MenuScreen` | `UnityEngine.MonoBehaviour` |
| `MainMenuController` | `UnityEngine.MonoBehaviour` |
| `MainMenuPopup` | `Il2CppScheduleOne.DevUtilities.Singleton<MainMenuPopup>` |
| `MainMenuPopup+Data` | `Il2CppSystem.Object` |
| `MainMenuRig` | `UnityEngine.MonoBehaviour` |
| `SettingsScreen` | `MenuScreen` |
| `SettingsScreen+SettingsCategory` | `Il2CppSystem.Object` |
| `ContinueScreen` | `MenuScreen` |
| `NewGameScreen` | `MenuScreen` |
| `SetupScreen` | `MenuScreen` |
| `ConfirmOverwriteScreen` | `MenuScreen` |
| `ConfirmExitScreen` | `MenuScreen` |
| `ImportScreen` | `MenuScreen` |
| `Disclaimer` | `UnityEngine.MonoBehaviour` |
| `JoinLocal` | `UnityEngine.MonoBehaviour` |
| `SaveDisplay` | `UnityEngine.MonoBehaviour` |
| `SaveExportButton` | `UnityEngine.MonoBehaviour` |
| `SaveImportButton` | `UnityEngine.MonoBehaviour` |

Full reverse index of `MenuScreen` (`research/raw/03-subclasses.txt:2017`) — **8** direct subclasses, note the
one outside the namespace:

```
BASE Il2CppScheduleOne.UI.MainMenu.MenuScreen   (8 direct subclasses)
    Il2CppScheduleOne.CommandListScreen
    Il2CppScheduleOne.UI.MainMenu.ConfirmExitScreen
    Il2CppScheduleOne.UI.MainMenu.ConfirmOverwriteScreen
    Il2CppScheduleOne.UI.MainMenu.ContinueScreen
    Il2CppScheduleOne.UI.MainMenu.ImportScreen
    Il2CppScheduleOne.UI.MainMenu.NewGameScreen
    Il2CppScheduleOne.UI.MainMenu.SettingsScreen
    Il2CppScheduleOne.UI.MainMenu.SetupScreen
```

### `MenuScreen` — the screen-switching primitive

```csharp
// Il2CppScheduleOne.UI.MainMenu.MenuScreen : UnityEngine.MonoBehaviour
public class MenuScreen : UnityEngine.MonoBehaviour
{
    // --- Properties (16) ---
    static Il2CppScheduleOne.UI.MainMenu.MenuScreen _Current_k__BackingField { get; set; }
    static System.Single LerpTime { get; set; }
    static System.Single LerpScale { get; set; }
    System.Boolean _IsOpen_k__BackingField { get; set; }
    System.Int32 ExitInputPriority { get; set; }
    System.Boolean OpenOnStart { get; set; }
    System.Boolean AttachLobbyToScreen { get; set; }
    Il2CppScheduleOne.UI.MainMenu.MenuScreen PreviousScreen { get; set; }
    UnityEngine.CanvasGroup Group { get; set; }
    Il2CppScheduleOne.State.MonoState State { get; set; }
    Il2CppScheduleOne.UIScreen uiScreen { get; set; }
    Il2CppScheduleOne.UIPanel uiPanel { get; set; }
    UnityEngine.RectTransform rect { get; set; }
    UnityEngine.Coroutine lerpRoutine { get; set; }
    static Il2CppScheduleOne.UI.MainMenu.MenuScreen Current { get; set; }
    System.Boolean IsOpen { get; set; }

    // --- Methods (9) ---
    public virtual System.Void Awake();
    public System.Void Close();
    public virtual System.Void Exit(Il2CppScheduleOne.ExitAction action);
    public System.Void Lerp(System.Boolean open);
    public virtual System.Void OnClose();
    public virtual System.Void OnOpen();
    public System.Void Open(System.Boolean b);
    public System.Void Open();
    public System.Void Start();
}
```

Load-bearing facts:

* `static MenuScreen Current` is the single active screen — screens are switched by calling
  `Open()`/`Close()` on the target, not by a router class. `PreviousScreen` gives you the back-stack of one.
* `Lerp(bool open)` fades `Group` (CanvasGroup alpha) and scales `rect` over `LerpTime` with `LerpScale`
  (`_startAlpha_5__2` / `_startScale_5__3` / `_endAlpha_5__4` / `_endScale_5__5` locals in the generated
  `<<Lerp>g__Routine|0>d` iterator confirm alpha+scale tweening).
* `OpenOnStart` is the flag that identifies **the root main-menu screen**. This is the robust,
  string-free way to find the button column at runtime (see §Recommended injection strategy).
* `Open()` is `public System.Void Open()` with no arguments → it is a **valid `UnityEvent` persistent-call
  target**, which is exactly how the menu buttons are wired in the prefab.
* Every `MenuScreen` owns a `Il2CppScheduleOne.State.MonoState State`, so opening a screen pushes a state
  that controls cursor/movement/HUD (see §7).

### `MainMenuController`, `MainMenuPopup`, `MainMenuRig`

```csharp
// Il2CppScheduleOne.UI.MainMenu.MainMenuController : UnityEngine.MonoBehaviour
public class MainMenuController : UnityEngine.MonoBehaviour
{
    Il2CppScheduleOne.State.MonoState State { get; set; }

    public System.Void Awake();
    public System.Void OnDestroy();
    public System.Void Start();
}

// Il2CppScheduleOne.UI.MainMenu.MainMenuPopup : Il2CppScheduleOne.DevUtilities.Singleton<MainMenuPopup>
public class MainMenuPopup : Singleton<MainMenuPopup>
{
    Il2CppScheduleOne.UI.MainMenu.MenuScreen Screen { get; set; }
    Il2CppTMPro.TextMeshProUGUI Title { get; set; }
    Il2CppTMPro.TextMeshProUGUI Description { get; set; }

    public System.Void Open(Il2CppScheduleOne.UI.MainMenu.MainMenuPopup.Data data);
    public System.Void Open(System.String title, System.String description, System.Boolean isBad);
}

// Il2CppScheduleOne.UI.MainMenu.MainMenuPopup+Data : Il2CppSystem.Object
public class Data : Il2CppSystem.Object
{
    System.String Title { get; set; }
    System.String Description { get; set; }
    System.Boolean IsBad { get; set; }

    public .ctor(System.String title, System.String description, System.Boolean isBad);
}

// Il2CppScheduleOne.UI.MainMenu.MainMenuRig : UnityEngine.MonoBehaviour
public class MainMenuRig : UnityEngine.MonoBehaviour
{
    Il2CppScheduleOne.AvatarFramework.Avatar Avatar { get; set; }
    Il2CppScheduleOne.AvatarFramework.Customization.BasicAvatarSettings DefaultSettings { get; set; }
    Il2CppReferenceArray<Il2CppScheduleOne.Tools.CashPile> CashPiles { get; set; }

    public System.Void Awake();
    public System.Void LoadStuff();
}
```

`MainMenuController` is nearly empty — **it does not build the button list.** It only owns a `MonoState`.
That is the single most important structural finding of this section.

`MainMenuPopup.Instance.Open(title, description, isBad)` is a free, fully native modal for mod-side
messages on the menu (e.g. "Expansions failed to load"). `MainMenuPopup` is a `Singleton<T>`, so
`MainMenuPopup.Instance` works (see §7).

### How is the button list built? — **Hardcoded prefab children, not generated**

Evidence, in order of strength:

1. **No code path generates them.** There is no button-list field, no `List<Button>`, no `Instantiate`
   helper anywhere in `Il2CppScheduleOne.UI.MainMenu`. `MainMenuController` has only `Awake`/`Start`/`OnDestroy`.
   Contrast with places that *do* generate rows: `Il2CppScheduleOne.UI.Phone.HomeScreen.appIconPrefab` +
   `GenerateAppIcon<T>`, `Il2CppScheduleOne.CommandListScreen.CommandEntryPrefab`,
   `Il2CppScheduleOne.UI.GenericSelectionModule.ListOptionPrefab`. The main menu has no such prefab field.
2. **The labels are not string literals.** Grepping `research/raw/literals-*.txt` for exact-line matches:
   `New Game`, `Settings`, `Quit`, `Apply`, `Exit to Menu` → **0 hits**. `Continue` (:4966), `Options`
   (:10652) and `Back` (:3518) do exist as literals but are not provably the menu buttons (`Continue` is
   also used by `CallInterface.ContinuePrompt`; `Options`/`Back` look like input-prompt labels — see
   `inputdata/promptdata/back/promptdata_back`). Menu labels therefore live in the **serialized
   `TextMeshProUGUI.m_text` of prefab children inside `level0`**.
3. **Every click target is a parameterless public method**, i.e. shaped for inspector-wired `UnityEvent`s:
   * `MenuScreen.Open()` — navigate to a screen
   * `Il2CppScheduleOne.DevUtilities.ApplicationQuit.Quit()` — `public System.Void Quit()`, only member
   * `Il2CppScheduleOne.Tools.ExitToMenu.Exit()` — `public System.Void Exit()`, only member
   * `MenuScreen.Close()`, `ConfirmExitScreen.ConfirmExit()`, `ConfirmOverwriteScreen.Confirm()`
   * int-arg variants for slot buttons: `ContinueScreen.LoadGame(System.Int32 index)`,
     `NewGameScreen.SlotSelected(System.Int32 slotIndex)`, `SettingsScreen.ShowCategory(System.Int32 index)`

**Consequence for modding:** you cannot "register a menu entry". You must clone an existing button
GameObject and clear its inherited persistent `onClick` call (both are possible — see
§Recommended injection strategy).

### Save-slot screens (for reference / template mining)

```csharp
// ContinueScreen : MenuScreen
UnityEngine.RectTransform NotHostWarning { get; set; }
public System.Void LoadGame(System.Int32 index);
public System.Void Update();

// NewGameScreen : MenuScreen
Il2CppScheduleOne.UI.MainMenu.ConfirmOverwriteScreen ConfirmOverwriteScreen { get; set; }
Il2CppScheduleOne.UI.MainMenu.SetupScreen SetupScreen { get; set; }
public System.Void SlotSelected(System.Int32 slotIndex);

// SetupScreen : MenuScreen        <-- best in-menu example of a working text input
static System.String DEFAULT_SAVE_PATH { get; set; }
Il2CppTMPro.TMP_InputField InputField { get; set; }
UnityEngine.UI.Button StartButton { get; set; }
UnityEngine.RectTransform SkipIntroContainer { get; set; }
UnityEngine.UI.Toggle SkipIntroToggle { get; set; }
UnityEngine.RectTransform NotHostWarning { get; set; }
System.Int32 slotIndex { get; set; }
public System.Void ClearFolderContents(System.String folderPath);
public System.Void CopyDefaultSaveToFolder(System.String folderPath);
public static System.Void CopyFilesRecursively(System.String sourcePath, System.String targetPath);
public System.Void Initialize(System.Int32 index);
public System.Boolean IsInputValid();
public virtual System.Void Start();
public System.Void StartGame();
public System.Void Update();
public System.Void _Start_b__7_0(System.String <p0>);

// ConfirmExitScreen : MenuScreen
Il2CppTMPro.TextMeshProUGUI TimeSinceSaveLabel { get; set; }
public System.Void ConfirmExit();
public System.Void Update();

// ConfirmOverwriteScreen : MenuScreen
Il2CppScheduleOne.UI.MainMenu.SetupScreen SetupScreen { get; set; }
System.Int32 slotIndex { get; set; }
public System.Void Confirm();
public System.Void Initialize(System.Int32 index);

// ImportScreen : MenuScreen
UnityEngine.GameObject MainContainer { get; set; }
UnityEngine.GameObject FailContainer { get; set; }
UnityEngine.UI.Button ConfirmButton { get; set; }
Il2CppTMPro.TextMeshProUGUI OrganisationNameLabel { get; set; }
Il2CppTMPro.TextMeshProUGUI NetworthLabel { get; set; }
Il2CppTMPro.TextMeshProUGUI VersionLabel { get; set; }
Il2CppTMPro.TextMeshProUGUI WarningLabel { get; set; }
System.Int32 slotToOverwrite { get; set; }
Il2CppScheduleOne.Persistence.SaveInfo saveInfo { get; set; }
public System.Void Cancel();
public System.Void Confirm();
public static System.Void CopyFilesRecursively(System.String sourcePath, System.String targetPath);
public System.Void Initialize(System.Int32 _slotToOverwrite, Il2CppScheduleOne.UI.MainMenu.MenuScreen previousScreen);

// SaveDisplay : UnityEngine.MonoBehaviour
Il2CppReferenceArray<UnityEngine.RectTransform> Slots { get; set; }
public System.Void Awake();
public System.String GetTimeLabel(System.Int32 hours);
public System.Void Refresh();
public System.Single RoundToDecimalPlaces(System.Single value, System.Int32 decimalPlaces);
public System.Void SetDisplayedSave(System.Int32 index, Il2CppScheduleOne.Persistence.SaveInfo info);
public static System.Single ToSingle(System.Double value);

// Disclaimer : UnityEngine.MonoBehaviour   (the startup legal splash)
static System.Boolean Shown { get; set; }
UnityEngine.CanvasGroup Group { get; set; }
UnityEngine.CanvasGroup TextGroup { get; set; }
UnityEngine.Events.UnityEvent OnClose { get; set; }
System.Single Duration { get; set; }
public System.Void Awake();
public System.Void Fade();
public Il2CppSystem.Collections.IEnumerator Method_Private_IEnumerator_PDM_0();   // stripped name — unstable
```

`Disclaimer.Shown` being **static** means it survives scene reloads — that is why the disclaimer only shows
once per process.

### Leaving / entering the menu

```csharp
// Il2CppScheduleOne.Persistence.LoadManager : PersistentSingleton<LoadManager>
public System.Void ExitToMenu(Il2CppScheduleOne.Persistence.SaveInfo autoLoadSave = null,
                              Il2CppScheduleOne.UI.MainMenu.MainMenuPopup.Data mainMenuPopup = null,
                              System.Boolean preventLeaveLobby = False);
public System.Void StartGame(Il2CppScheduleOne.Persistence.SaveInfo info,
                             System.Boolean allowLoadStacking = False,
                             System.Boolean allowSaveBackup = True);
public System.Void LoadLastSave();
System.Boolean IsInGameScene { get; }
System.Boolean IsGameLoaded { get; set; }
UnityEngine.Events.UnityEvent onPreSceneChange { get; set; }
UnityEngine.Events.UnityEvent onSceneChangeDone { get; set; }
UnityEngine.Events.UnityEvent onLoadComplete { get; set; }
```

`ExitToMenu` takes a `MainMenuPopup.Data` — that is how the game shows "you were disconnected" style popups
after returning to the menu. S1API already prefixes it
(`S1API.Internal.Patches` → `[HarmonyPatch(typeof(Il2CppScheduleOne.Persistence.LoadManager), "ExitToMenu")]`),
so `LoadManager.ExitToMenu` / `StartGame` / `LoadAsClient` / `Update` are proven-patchable anchors for
"menu scene is now live" notifications.

---

## 2. Settings / options menu

### `SettingsScreen` — the category-tab host (**the injection surface**)

```csharp
// Il2CppScheduleOne.UI.MainMenu.SettingsScreen : Il2CppScheduleOne.UI.MainMenu.MenuScreen
public class SettingsScreen : MenuScreen
{
    // --- Properties (7) ---
    Il2CppReferenceArray<Il2CppScheduleOne.UI.MainMenu.SettingsScreen.SettingsCategory> Categories { get; set; }
    UnityEngine.UI.Button ApplyDisplayButton { get; set; }
    Il2CppScheduleOne.UI.Settings.ConfirmDisplaySettings ConfirmDisplaySettings { get; set; }
    UnityEngine.GameObject ActiveDisplaySelection { get; set; }
    Il2CppReferenceArray<UnityEngine.GameObject> HostOnlyGameObjects { get; set; }
    Il2CppScheduleOne.UITab Tab { get; set; }
    System.Boolean _initialized { get; set; }

    // --- Methods (5) ---
    public System.Void ApplyDisplaySettings(System.Boolean showRevertMenu);
    public virtual System.Void Awake();
    public System.Void OnEnable();
    public virtual System.Void OnOpen();
    public System.Void ShowCategory(System.Int32 index);
}

// Il2CppScheduleOne.UI.MainMenu.SettingsScreen+SettingsCategory : Il2CppSystem.Object
public class SettingsCategory : Il2CppSystem.Object
{
    UnityEngine.UI.Toggle Toggle { get; set; }   // the category tab button (radio)
    UnityEngine.GameObject Panel { get; set; }   // the page shown when that tab is active

    private .ctor();
    public  .ctor();                              // <-- parameterless ctor exists; you can construct one
    public  .ctor(System.IntPtr pointer);
}
```

Compiler-generated helpers that tell you what the code does (names are stripped/obfuscated — do not bind):

```csharp
// SettingsScreen+<>c__DisplayClass8_0   (closure of OnEnable)
System.Int32 index { get; set; }
Il2CppScheduleOne.UI.MainMenu.SettingsScreen __4__this { get; set; }
public System.Void _OnEnable_b__0(System.Boolean on);          // <-- per-category Toggle.onValueChanged(bool)

// SettingsScreen+<>c__DisplayClass12_0  (closure of ApplyDisplaySettings)
Il2CppScheduleOne.DevUtilities.DisplaySettings old { get; set; }
Il2CppScheduleOne.DevUtilities.DisplaySettings unapplied { get; set; }
// nested iterator ObfuscatedName: "<<ApplyDisplaySettings>g__Wait|0>d"
```

Reading those together, `OnEnable` captures a per-index `int` and registers a
`Toggle.onValueChanged(bool on)` closure per category, guarded by `_initialized`. **This is why appending to
`Categories` before `OnEnable` runs gives you working tab behaviour for free** (Unity order is
`Awake` → `OnEnable` → `Start`, so a `Postfix` on `Awake` is early enough).

`HostOnlyGameObjects` is strong evidence this same component is used **in-game**, not just in `Menu` — in the
main menu you are always host, so a host-only gating array would be pointless there. (Inferred, high confidence.)

### Setting-row widget types — verified real names

There is **no** `SettingsRow`/`OptionRow` type. A settings row is a plain GameObject carrying one of three
`MonoBehaviour` families, each of which delegates to a game-generic widget:

| Row kind | Component (base) | Delegates to |
|---|---|---|
| Toggle row | `Il2CppScheduleOne.UI.Settings.SettingsToggle` | `Il2CppScheduleOne.UIToggle uiToggle` |
| Slider row | `Il2CppScheduleOne.UI.Settings.SettingsSlider` | `UnityEngine.UI.Slider slider` + `Il2CppTMPro.TextMeshProUGUI valueLabel` |
| Dropdown row | `Il2CppScheduleOne.UI.Settings.SettingsDropdown` | `Il2CppScheduleOne.UIPopupSelector _popupSelector` + `Il2CppTMPro.TMP_Dropdown _dropdown` |
| Keybind row | `Il2CppScheduleOne.UI.Settings.Keybinder` | `Il2CppScheduleOne.DevUtilities.RebindActionUI rebindActionUI` |

```csharp
// Il2CppScheduleOne.UI.Settings.SettingsToggle : UnityEngine.MonoBehaviour
Il2CppScheduleOne.UIToggle uiToggle { get; set; }
public virtual System.Void Awake();
public System.Void GetReferences();
public virtual System.Void OnValueChanged(System.Boolean value);
public System.Void SetIsOnWithoutNotify(System.Boolean value);

// Il2CppScheduleOne.UI.Settings.SettingsSlider : UnityEngine.MonoBehaviour
System.Single ValueDisplayTime { get; set; }
System.Boolean DisplayValue { get; set; }
UnityEngine.UI.Slider slider { get; set; }
Il2CppTMPro.TextMeshProUGUI valueLabel { get; set; }
System.Single timeOnValueChange { get; set; }
public virtual System.Void Awake();
public virtual System.String GetDisplayValue(System.Single value);
public virtual System.Void OnDragEnd(System.Single value);
public virtual System.Void OnValueChanged(System.Single value);
public System.Void SetDisplayValue(System.Single value);
public System.Void SetValueWithoutNotify(System.Single value);
public virtual System.Void Update();
public System.Void _Awake_b__5_0(UnityEngine.EventSystems.BaseEventData data);

// Il2CppScheduleOne.UI.Settings.SettingsDropdown : UnityEngine.MonoBehaviour
Il2CppStringArray DefaultOptions { get; set; }
Il2CppScheduleOne.UIPopupSelector _popupSelector { get; set; }
Il2CppTMPro.TMP_Dropdown _dropdown { get; set; }
public System.Void AddOption(System.String option);
public System.Void AddOptions(Il2CppSystem.Collections.Generic.List<System.String> options);
public virtual System.Void Awake();
public System.Void ClearOptions();
public virtual System.Void OnValueChanged(System.Int32 value);
public System.Void SetValueWithoutNotify(System.Int32 value);
public virtual System.Void Start();
public System.Void _Start_b__5_0(Il2CppScheduleOne.UIPopupScreen_ContextMenu.ContextMenuOption v);
```

Concrete row subclasses you can use as **clone templates** (all 28 types in `Il2CppScheduleOne.UI.Settings`):

* `SettingsToggle` → `VSyncToggle`, `SSAOToggle`, `GodRaysToggle`, `InvertYToggle`,
  `FocusLostPauseToggle`, `AutoBackupSavesToggle`
* `SettingsSlider` → `AudioSlider`, `CameraBobSlider`, `FOVSLider` *(sic — capital `L`, do not "fix" it)*,
  `IntefaceScaleSlider` *(sic — misspelled in the game)*, `PointerSensitivitySlider`, `SensitivitySlider`,
  `TargetFPSSlider`
* `SettingsDropdown` → `AntiAliasingDropdown`, `DisplayModeDropdown`, `MonitorDropdown`, `QualityDropdown`,
  `ResolutionDropdown`, `SprintModeDropdown`, `UnitsModeDropdown`
* Standalone: `ConfirmDisplaySettings`, `GameSettingsWindow`, `Keybinder`, `PlayerLogExporterButton`,
  `RestoreDefaultBindingsButton` (+ `RestoreDefaultBindingsButton+EType { KeyboardMouse=0, Gamepad=1 }`),
  `SensitivitySlider+ESensitivityType { Mouse=0, Gamepad=1 }`

```csharp
// Il2CppScheduleOne.UI.Settings.ConfirmDisplaySettings : UnityEngine.MonoBehaviour
static System.Single RevertTime { get; set; }
Il2CppTMPro.TextMeshProUGUI SubtitleLabel { get; set; }
System.Single timeUntilRevert { get; set; }
Il2CppScheduleOne.DevUtilities.DisplaySettings oldSettings { get; set; }
Il2CppScheduleOne.DevUtilities.DisplaySettings newSettings { get; set; }
System.Boolean IsOpen { get; }
public System.Void Awake();
public System.Void Close(System.Boolean revert);
public System.Void Exit(Il2CppScheduleOne.ExitAction action);
public System.Void Open(Il2CppScheduleOne.DevUtilities.DisplaySettings _oldSettings,
                        Il2CppScheduleOne.DevUtilities.DisplaySettings _newSettings);
public System.Void Update();

// Il2CppScheduleOne.UI.Settings.GameSettingsWindow : UnityEngine.MonoBehaviour
//  NOT the options menu — this is the dev/console "Game Settings" window
Il2CppScheduleOne.UIToggle ConsoleToggle { get; set; }
Il2CppScheduleOne.UIToggle RandomMixMapsToggle { get; set; }
UnityEngine.GameObject Blocker { get; set; }
Il2CppScheduleOne.UIPanel uiPanel { get; set; }
public System.Void ApplySettings(Il2CppScheduleOne.DevUtilities.GameSettings settings);
public System.Void Awake();
public System.Void ConsoleToggled(System.Boolean value);
public System.Void RandomMixMapsToggled(System.Boolean value);
public System.Void Start();
```

### How a settings category is registered

**By data, not by API.** `Categories` is a serialized `SettingsCategory[]` on the `SettingsScreen`
component; `ShowCategory(int index)` indexes it; `OnEnable()` wires `Categories[i].Toggle` → `ShowCategory(i)`.
There is no `AddCategory` / `RegisterCategory` method anywhere in the assembly. Appending to the array *is*
the registration mechanism.

### How a setting persists

`Il2CppScheduleOne.DevUtilities.Settings` (a `PersistentSingleton<Settings>`) owns everything. Note that the
player-facing options are **not** the `Core.Settings.Framework` types (see below).

```csharp
// Il2CppScheduleOne.DevUtilities.Settings : PersistentSingleton<Settings>
static System.Single MinYPos { get; set; }
static System.String BETA_ARG { get; set; }
Il2CppSystem.Collections.Generic.List<System.String> LaunchArgs { get; set; }
static System.Boolean _ChristmasEventActive_k__BackingField { get; set; }
Il2CppScheduleOne.DevUtilities.Settings.EUnitType _UnitType_k__BackingField { get; set; }
Il2CppScheduleOne.DevUtilities.DisplaySettings DisplaySettings { get; set; }
Il2CppScheduleOne.DevUtilities.DisplaySettings UnappliedDisplaySettings { get; set; }
Il2CppScheduleOne.DevUtilities.GraphicsSettings GraphicsSettings { get; set; }
Il2CppScheduleOne.DevUtilities.AudioSettings AudioSettings { get; set; }
Il2CppScheduleOne.DevUtilities.InputSettings InputSettings { get; set; }
Il2CppScheduleOne.Gamepad.GamepadSettings GamepadSettings { get; set; }
Il2CppScheduleOne.DevUtilities.OtherSettings OtherSettings { get; set; }
UnityEngine.InputSystem.InputActionAsset InputActions { get; set; }
Il2CppScheduleOne.GameInput GameInput { get; set; }
UnityEngine.Rendering.Universal.ScriptableRendererFeature SSAO { get; set; }
UnityEngine.Rendering.Universal.ScriptableRendererFeature GodRays { get; set; }
Il2CppSystem.Action onInputsApplied { get; set; }
Il2CppSystem.Action onDisplaySettingsApplied { get; set; }
Il2CppSystem.Action onQualitySettingsChanged { get; set; }
Il2CppSystem.Action onUnappliedDisplayIndexChanged { get; set; }
System.Boolean PausingFreezesTime { get; }
Il2CppScheduleOne.DevUtilities.Settings.EUnitType UnitType { get; set; }
System.Single LookSensitivity { get; }

public System.Void ApplyAudioSettings(AudioSettings settings);
public System.Void ApplyDisplaySettings(DisplaySettings settings);
public System.Void ApplyGamepadSettings(Il2CppScheduleOne.Gamepad.GamepadSettings settings);
public System.Void ApplyGraphicsSettings(GraphicsSettings settings);
public System.Void ApplyInputSettings(InputSettings settings);
public System.Void ApplyOtherSettings(OtherSettings settings);
public AudioSettings    ReadAudioSettings();
public DisplaySettings  ReadDisplaySettings();
public GraphicsSettings ReadGraphicsSettings();
public InputSettings    ReadInputSettings();
public OtherSettings    ReadOtherSettings();
public Il2CppScheduleOne.Gamepad.GamepadSettings ReadGamepadSettings();
public System.Void WriteAudioSettings(AudioSettings settings);
public System.Void WriteDisplaySettings(DisplaySettings settings);
public System.Void WriteGraphicsSettings(GraphicsSettings settings);
public System.Void WriteInputSettings(InputSettings settings);
public System.Void WriteOtherSettings(OtherSettings settings);
public System.Void WriteGamepadSettings(Il2CppScheduleOne.Gamepad.GamepadSettings settings);
public System.Void ReloadAudioSettings();
public System.Void ReloadGamepadSettings();
public System.Void ReloadGraphicsSettings();
public System.Void ReloadInputSettings();
public System.Void ReloadOtherSettings();
public System.Void RestoreDefaultGamepadBindings();
public System.Void RestoreDefaultKeyboardBindings();
public System.String GetActionControlPath(System.String actionName);
public Il2CppScheduleOne.DevUtilities.Settings.EUnitType GetDefaultUnitTypeForPlayer();
public System.Void MoveMainWindowTo(UnityEngine.DisplayInfo displayInfo);
public virtual System.Void Awake();
public virtual System.Void Start();

// enum Settings+EUnitType { Metric = 0, Imperial = 1 }
```

The persistence shape is `Read*Settings()` / `Write*Settings(...)` / `Apply*Settings(...)` per group, with
plain POCO holders:

```csharp
// Il2CppScheduleOne.DevUtilities.DisplaySettings : System.ValueType   (a struct!)
public System.Int32 ResolutionIndex                                   [FieldOffset(0)]
public Il2CppScheduleOne.DevUtilities.DisplaySettings.EDisplayMode DisplayMode [FieldOffset(4)]
public System.Boolean VSync                                           [FieldOffset(8)]
public System.Int32 TargetFPS                                         [FieldOffset(12)]
public System.Single UIScale                                          [FieldOffset(16)]
public System.Single CameraBobbing                                    [FieldOffset(20)]
public System.Int32 ActiveDisplayIndex                                [FieldOffset(24)]
public Il2CppScheduleOne.DevUtilities.Settings.EUnitType UnitType     [FieldOffset(28)]
public System.Boolean PauseOnFocusLost                                [FieldOffset(32)]
public static System.Collections.Generic.List<UnityEngine.Resolution> GetResolutions();   // Il2CppSystem.Collections
// enum DisplaySettings+EDisplayMode { Windowed=0, FullscreenWindow=1, ExclusiveFullscreen=2 }

// Il2CppScheduleOne.DevUtilities.GraphicsSettings : Il2CppSystem.Object
GraphicsSettings.EGraphicsQuality GraphicsQuality { get; set; }   // { Low=0, Medium=1, High=2, Ultra=3 }
GraphicsSettings.EAntiAliasingMode AntiAliasingMode { get; set; } // { Off=0, FXAA=1, SMAA=2 }
System.Single FOV { get; set; }
System.Boolean SSAO { get; set; }
System.Boolean GodRays { get; set; }

// Il2CppScheduleOne.DevUtilities.AudioSettings : Il2CppSystem.Object
System.Single MasterVolume, AmbientVolume, MusicVolume, SFXVolume,
              UIVolume, DialogueVolume, FootstepsVolume, WeatherVolume { get; set; }

// Il2CppScheduleOne.DevUtilities.InputSettings : Il2CppSystem.Object
System.Single MouseSensitivity { get; set; }
System.Boolean InvertMouse { get; set; }
InputSettings.EActionMode SprintMode { get; set; }   // { Press=0, Hold=1 }
System.String BindingOverrides { get; set; }

// Il2CppScheduleOne.DevUtilities.OtherSettings : Il2CppSystem.Object
System.Boolean AutoBackupSaves { get; set; }

// Il2CppScheduleOne.DevUtilities.GameSettings : Il2CppSystem.Object   (per-save, on GameManager.Settings)
System.Boolean ConsoleEnabled { get; set; }
System.Boolean UseRandomizedMixMaps { get; set; }
```

**Where does the file live?** `Settings` has no `path` string exposed and no `Resources`/JSON literal that
maps to it, so the on-disk location is **UNVERIFIED**. There is no `PlayerPrefs`-style API surfaced. Do not
try to piggy-back on it — persist your mod's toggles with MelonLoader's `MelonPreferences` instead
(clean, versioned, and it survives game patches).

### `Il2CppScheduleOne.Core.Settings` + `.Core.Settings.Framework` — **not the options menu**

This is a **designer data-override framework** for `ScriptableObject` game-balance data, living in a different
assembly (`Il2CppScheduleOne.Core`). It has nothing to do with the player-facing options screen. Included
because it is in scope and because it *is* the right hook for balance-tuning parts of the three expansions.

```csharp
// Il2CppScheduleOne.Core.Settings.Framework.Settings : UnityEngine.ScriptableObject
System.Int32 Version { get; }
public System.Void CheckForDuplicateSettingsNames();
public System.Void Deserialize(System.String json);
public virtual Il2CppReferenceArray<SettingsObject> GetSettingsObjects();
public virtual System.Void OnValidate();
public System.String Serialize();
//   + Settings+SerializedObject { System.String Name; System.String Value; }
//   + Settings+SerializedObjectList { List<SerializedObject> Fields; }

// Il2CppScheduleOne.Core.Settings.Framework.SettingsObject : Il2CppSystem.Object
System.String Name { get; set; }
public virtual Il2CppSystem.Type GetObjectType();
public virtual System.Void TryOverwriteWith(SettingsObject other);

// SettingsField`1<T> : SettingsObject
System.Boolean Override { get; set; }
T Value { get; set; }
public .ctor(System.String name, T defaultValue);
public System.Void OverwriteWith(SettingsField<T> other);

// SerializableSettingsField`1<T> : SettingsField<T>
public virtual System.Void Deserialize(System.String value);
public virtual System.String Serialize();
//   + SerializableSettingsField`1+SerializedField { T Value; }

// SettingsList`1<T> : SettingsObject                 { List<T> Items; }
// ExtendibleSettingsList`1<T> : SettingsList<T>      { OverwriteWith(...); TryOverwriteWith(...); }
// ReplaceableSettingsList`1<T> : ExtendibleSettingsList<T>
//   + ReplaceableSettingsList`1+EMode { Replace = 0, Add = 1 }
// ISerializable  { Deserialize(System.String value); System.String Serialize(); }

// Concrete settings assets in Il2CppScheduleOne.Core.Settings:
//   EquipSettings : Settings
//   SFXSettings   : Settings  { ImpactSoundMaxRange, AudioSourcePoolSize, ImpactSounds, FootstepSounds }
```

Driven by `Il2CppScheduleOne.Configuration`:

```csharp
// Il2CppScheduleOne.Configuration.ConfigurationService : PersistentSingleton<ConfigurationService>
Il2CppReferenceArray<BaseConfiguration> _configurations { get; set; }
Il2CppReferenceArray<BaseConfiguration> Configurations { get; }
public virtual System.Void Awake();
public System.Void GetConfigurationAndListenForChanges<T>(Il2CppSystem.Action<BaseConfiguration> onConfigChanged);
public System.Void ResetConfigurations();
public System.Boolean TryGetConfiguration<T>(out T& configuration);
public System.Boolean TryGetConfiguration(System.String configurationName, out BaseConfiguration& configuration);
public System.Void UnsubscribeFromConfigurationChanges<T>(Il2CppSystem.Action<BaseConfiguration> onConfigChanged);

// Il2CppScheduleOne.Configuration.BaseConfiguration : UnityEngine.ScriptableObject
Il2CppSystem.Action<BaseConfiguration> OnConfigurationChanged { get; set; }
public virtual Il2CppScheduleOne.Core.Settings.Framework.Settings GetSettings();
public virtual System.Void ResetConfigurationToDefault();
public virtual System.Void ValidateConfiguration();

// Il2CppScheduleOne.Configuration.Configuration`1<T> : BaseConfiguration
T Settings { get; set; }
T DefaultSettings { get; set; }
public static System.Void ApplyOverwrites(T from, T to);
public System.Void ApplySettings(T newSettings);

// Il2CppScheduleOne.Configuration.ConfigurationServiceNetworker : NetworkBehaviour
public System.Void ApplySettingsJson(NetworkConnection conn, System.String configName, System.String settingsJson);
public System.Void OnConfigChanged(BaseConfiguration changedConfig);
```

Note `ConfigurationServiceNetworker.ApplySettingsJson` — configuration is **replicated host→clients over
FishNet**. If your expansion mods change balance values, this is the pattern to mirror so multiplayer stays
consistent.

---

## 3. The exact UI component stack

### Text

* **`Il2CppTMPro.TextMeshProUGUI`** is the primary label type across the whole UI (main menu, pause menu,
  settings, tooltips, HUD, phone). Its base `Il2CppTMPro.TMP_Text` exposes what you need:
  ```csharp
  System.String text { get; set; }
  Il2CppTMPro.TMP_FontAsset font { get; set; }
  UnityEngine.Color color { get; set; }
  System.Single fontSize { get; set; }
  System.Single fontSizeMin { get; set; }
  System.Single fontSizeMax { get; set; }
  ```
* **Legacy `UnityEngine.UI.Text` is still used in places** — the codebase is mixed. `UnityEngine.UI.Text`
  appears on `Il2CppScheduleOne.UI.TabItemUI._label` / `._indicatorLabel`,
  `Il2CppScheduleOne.UISelectable._label`, `Il2CppScheduleOne.UI.HUD.fpsLabel`,
  `Il2CppScheduleOne.UI.Phone.HomeScreen.timeText`, `Il2CppScheduleOne.UI.App<T>.notificationText`, and most
  of `Il2CppScheduleOne.UI.Phone.PhoneShopInterface`. **Any code that sets a label must handle both types.**
* `Il2CppTMPro.TMP_InputField` **works** (`ConsoleUI.InputField`, `SetupScreen.InputField`,
  `UIPopupScreen_ModifyAmountMenu.amountInputField`) — this is the fix for the legacy-IMGUI text-input problem
  documented in `CreativeModeMod.cs` (`GUILayout.TextField` / `GUI.DrawTexture` are stripped on this build).
  **uGUI/TMP has no equivalent stripping problem** — nothing in the dumps suggests any uGUI or TMP member is
  missing; `UnityEngine.UI` exports the full 67 public types and `Il2CppTMPro` all 128.
* `Il2CppTMPro.TMP_Dropdown` is used by `SettingsDropdown._dropdown`, but the game's *visible* dropdown is
  `Il2CppScheduleOne.UIPopupSelector` driving `Il2CppScheduleOne.UIPopupScreen_ContextMenu`.

### Fonts — **cannot be named from metadata; copy them at runtime**

Grepping all 23,869 literals for font asset names yields only engine/TMP internals: `DefaultFont`,
`Fonts & Materials/`, `.fonts`, `-unity-font`, `IconTitleFont`, `GdiVerticalFont`, `BakeSDF.*`,
`Hidden/TextCore/Distance Field`, `Hidden/TextCore/Distance Field SSD`. **No Schedule I font asset name exists
in `global-metadata.dat`**, because fonts are serialized object references inside `level0` /
`sharedassets0.assets`, not `Resources.Load` paths.

Therefore the only correct recipe is to **inherit the font from an existing label**:

```csharp
// Copy the exact game font + material + size ramp from a sibling label.
static void MatchStyle(Il2CppTMPro.TMP_Text target, Il2CppTMPro.TMP_Text source)
{
    target.font      = source.font;          // TMP_FontAsset (incl. its SDF material)
    target.fontSize  = source.fontSize;
    target.color     = source.color;
    target.alignment = source.alignment;
}
```

Or, if you must discover them, enumerate at runtime once and log:
`UnityEngine.Resources.FindObjectsOfTypeAll<Il2CppTMPro.TMP_FontAsset>()`.
**Cloning a whole label/row GameObject is strictly better than constructing one**, because it also carries the
material, the `RectTransform`, layout elements, outline/shadow settings and the `ColorFont` wiring.

### Sprites / atlases — same story

No `ui/` folder exists in the `Resources` path table (see §8), so UI sprites cannot be loaded by name.
The game does, however, ship two named-lookup `ScriptableObject`s you can query at runtime:

```csharp
// Il2CppScheduleOne.DevUtilities.SpriteFont : UnityEngine.ScriptableObject
Il2CppSystem.Collections.Generic.List<SpriteFont.SpriteFontItem> SpriteFontItems { get; set; }
public UnityEngine.Sprite GetSprite(System.String name);
//   SpriteFont+SpriteFontItem { System.String Name; UnityEngine.Sprite Sprite; }

// Il2CppScheduleOne.DevUtilities.ColorFont : UnityEngine.ScriptableObject
Il2CppSystem.Collections.Generic.List<ColorFont.ColorFontItem> ColorFontItems { get; set; }
public UnityEngine.Color GetColour(System.String name);
//   ColorFont+ColorFontItem { System.String Name; UnityEngine.Color Colour; }

// Il2CppScheduleOne.DevUtilities.FontSetter : UnityEngine.MonoBehaviour
Il2CppSystem.Collections.Generic.List<FontSetter.ImageItem> _imageItems { get; set; }
Il2CppScheduleOne.DevUtilities.ColorFont _colourFont { get; set; }
public System.Void SetColour(System.String componentName, System.String ColourName);
//   FontSetter+ImageItem { System.String Name; UnityEngine.UI.Image Image; }
```

`ColorFont` is the game's **named palette** — live instances are reachable via
`Il2CppScheduleOne.UI.Phone.Phone.Instance.GeneralColorFont`, `Il2CppScheduleOne.UI.TabController._tabColorFont`,
`Il2CppScheduleOne.UI.Phone.Delivery.DeliveryShop._shopTextColorFont`,
`Il2CppScheduleOne.UI.Phone.ContactsApp.ContactsDetailPanel._proudctColorFont` *(sic)*.
The palette **entry names are prefab/asset data and are UNVERIFIED** — enumerate `ColorFontItems` at runtime
and log `Name` to learn them.

### Colours — the confirmed hex values

These are real string literals from `literals-sorted.txt` (rich-text tags baked into game copy). They are the
best available evidence of the palette:

| Hex | Role (inferred from surrounding literal) | Literal line |
|---|---|---|
| `#54E717` | positive / money green (most-used) | 347, 2474-2477, 7647, 9356 |
| `#4CBFFF` | primary UI blue (also appears bare, not just in a tag) | 1088, 2473 |
| `#4CB0FF` | "packaged product" blue | 10976 |
| `#16F01C`, `#46CB4F`, `#8AEE8C`, `#93FF58`, `#2DB92D` | secondary greens (unlock / success) | 2470, 2472, 2480, 2481, 15096 |
| `#19BEF0`, `#76C9FF`, `#88CBFF` | secondary blues | 2471, 2478, 2479 |
| `#F7B119`, `#FFB43C`, `#FFC73D`, `#FFD755`, `#EEC58A`, `#EEEA8A` | warning / gold | 2485, 2491-2492, 8099, 11113, 2483, 2484 |
| `#FF5555`, `#FF6455`, `#FF6666`, `#F86266`, `#EE9A8A` | error / expired red | 2487-2490, 2486, 2482 |
| `#c0c0c0ff` | dimmed grey (with alpha) | 2493 |

Format templates in use: `<color=#{0}> ({1})</color>` (:2494), `<color=#{0}>{1}x</color> {2}` (:2495).
And `Il2CppScheduleOne.UI.Phone.CallInterface` exposes `UnityEngine.Color Highlight1Color` +
`System.String highlight1Hex`, i.e. the game itself converts a colour to a hex string for TMP rich text.

### Recipe: build a native-looking widget

1. Find a live example **by component type**, never by path:
   `screen.GetComponentsInChildren<Il2CppScheduleOne.UI.Settings.SettingsToggle>(true)`.
2. `UnityEngine.Object.Instantiate(template.gameObject, myParent, false)` — into a parent that is
   **inactive**, so the clone's `Awake`/`OnEnable` don't fire against real settings.
3. `UnityEngine.Object.Destroy(clone.GetComponent<Il2CppScheduleOne.UI.Settings.SettingsToggle>())` to detach
   the game's setting logic while keeping the visuals + `Il2CppScheduleOne.UIToggle`.
   (`Destroy` via the base-type reference removes the real subclass instance, whichever it is.)
4. Retarget the surviving generic widget:
   `clone.GetComponent<Il2CppScheduleOne.UIToggle>().OnChanged.AddListener(...)`,
   `UIToggle.SetStateWithoutNotify(bool)` for initial state.
5. Set the label: try `GetComponentInChildren<Il2CppTMPro.TextMeshProUGUI>(true)` first, fall back to
   `GetComponentInChildren<UnityEngine.UI.Text>(true)`.
6. `clone.SetActive(true)` / activate the parent.

You will get: correct font, correct SDF material, correct hover scale (`ButtonScaler`), correct ON/OFF text
(`UIToggle.ONTEXT` / `UIToggle.OFFTEXT`), correct gamepad navigation (`UISelectable` re-registers with its
parent `UIPanel` on `Awake`), correct tooltip hook (`Tooltip`), and zero shipped assets.

Generic widgets worth knowing (all in the root `Il2CppScheduleOne` namespace):

```csharp
// Il2CppScheduleOne.UIToggle : Il2CppScheduleOne.UIOption
Il2CppTMPro.TextMeshProUGUI buttonText { get; set; }
UnityEngine.UI.Image toggleImage { get; set; }
static System.String ONTEXT { get; set; }
static System.String OFFTEXT { get; set; }
UnityEngine.Events.UnityEvent<System.Boolean> OnChanged { get; set; }
System.Boolean state { get; set; }
public virtual System.Void Awake();
public virtual System.Void OnUpdate();
public System.Void SetButtonState(System.Boolean state);
public System.Void SetState(System.Boolean state);
public System.Void SetStateInternal(System.Boolean state);
public System.Void SetStateWithoutNotify(System.Boolean state);

// Il2CppScheduleOne.UISlider : Il2CppScheduleOne.UIOption
System.Boolean canUpdateValueText { get; set; }
UnityEngine.UI.Slider slider { get; set; }
System.Single stepSize { get; set; }
Il2CppTMPro.TextMeshProUGUI valueText { get; set; }
UnityEngine.Events.UnityEvent<System.Single> OnChanged { get; set; }
public virtual System.Void Awake();
public virtual System.Void MoveLeft();
public virtual System.Void MoveRight();
public System.Void UpdateSliderChanged();
public System.Void UpdateText();

// Il2CppScheduleOne.UIHorizontalSelector : Il2CppScheduleOne.UIOption
UnityEngine.UI.Button prevButton { get; set; }
UnityEngine.UI.Button nextButton { get; set; }
Il2CppTMPro.TextMeshProUGUI currentOptionNameText { get; set; }
UnityEngine.Events.UnityEvent<Il2CppScheduleOne.UIOption.OptionInfo> OnChanged { get; set; }
Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.UIOption.OptionInfo> options { get; set; }
System.Int32 currentIndex { get; set; }
public System.Void SetOptions(Il2CppSystem.Collections.Generic.List<UIOption.OptionInfo> newOptions,
                              System.Int32 defaultIndex = 0);
public System.Void UpdateCurrentOptionText();
//   UIOption+OptionInfo : Il2CppSystem.ValueType { System.String OptionName; System.Int32 OptionIndex; }

// Il2CppScheduleOne.UIPopupSelector : Il2CppScheduleOne.UIOption
Il2CppTMPro.TextMeshProUGUI currentOptionNameText { get; set; }
UnityEngine.Events.UnityEvent<UIPopupScreen_ContextMenu.ContextMenuOption> OnChanged { get; set; }
Il2CppReferenceArray<UIPopupScreen_ContextMenu.ContextMenuOption> options { get; set; }
System.Int32 currentIndex { get; set; }
public System.Void AddOption(UIPopupScreen_ContextMenu.ContextMenuOption option);
public System.Void AddOptions(Il2CppReferenceArray<UIPopupScreen_ContextMenu.ContextMenuOption> newOptions);
public System.Void ClampCurrentIndex();
public System.Void ClearOptions();
public System.Void ClosePopup(System.Int32 selectedIndex);
public System.Int32 GetOptionCount();
public System.Void OpenPopup();
public System.Void SetCurrentOptionWithoutNotify(System.Int32 index);
public System.Void SetOptions(Il2CppReferenceArray<UIPopupScreen_ContextMenu.ContextMenuOption> newOptions,
                              System.Int32 defaultIndex = 0);
public System.Void UpdateCurrentOptionText();

// Il2CppScheduleOne.UI.ButtonScaler : UnityEngine.MonoBehaviour   (the hover-grow effect)
UnityEngine.RectTransform ScaleTarget { get; set; }
System.Single HoverScale { get; set; }
System.Single ScaleTime { get; set; }
UnityEngine.UI.Button button { get; set; }
public System.Void Awake();
public System.Void HoverEnd();
public System.Void Hovered();
public System.Void SetScale(System.Single endScale);

// Il2CppScheduleOne.UI.CanvasGroupFader : UnityEngine.MonoBehaviour
System.Single _defaultFadeDuration { get; set; }
System.Boolean _scaleDurationWithFadeAmount { get; set; }
UnityEngine.CanvasGroup _canvasGroup { get; set; }
public System.Void Awake();
public System.Void FadeTo(System.Single targetAlpha);
public System.Void FadeTo(System.Single targetAlpha, System.Single duration);

// Il2CppScheduleOne.UI.CanvasScaler : UnityEngine.MonoBehaviour   (game wrapper, NOT UnityEngine.UI.CanvasScaler)
static System.Single _GlobalScaleFactor_k__BackingField { get; set; }
static Il2CppSystem.Action OnCanvasScaleFactorChanged { get; set; }
System.Single _scaleMultiplier { get; set; }
System.Single _globalScaleInfluence { get; set; }
UnityEngine.UI.CanvasScaler _canvasScaler { get; set; }
UnityEngine.Vector2 _defaultReferenceResolution { get; set; }
static System.Single GlobalScaleFactor { get; set; }
static System.Single NormalizedCanvasScaleFactor { get; }
public System.Void RefreshScale();
public static System.Void SetScaleFactor(System.Single scaleFactor);
```

`Il2CppScheduleOne.UI.CanvasScaler` shadows `UnityEngine.UI.CanvasScaler` — **always fully qualify** when
touching canvas scaling, and remember `IntefaceScaleSlider` (the UI-scale option) drives
`CanvasScaler.SetScaleFactor`, so a cloned widget inherits UI scaling for free.

---

## 4. Injection points — ranked candidates

### #1 — `SettingsScreen.Awake()` Postfix → append a `SettingsCategory`  ★ recommended

* **Patch:** `[HarmonyPatch(typeof(Il2CppScheduleOne.UI.MainMenu.SettingsScreen), "Awake")]` + `Postfix`
* **Template to clone:** `__instance.Categories[last].Toggle.gameObject` and `__instance.Categories[last].Panel`
* **Pros:** the game does the wiring for you (`OnEnable` → `ShowCategory(index)`); radio behaviour comes from
  the existing `UnityEngine.UI.ToggleGroup` that the cloned `Toggle` still references; gamepad tab-cycling via
  `SettingsScreen.Tab` (a `UITab : UIPanel`) picks up any cloned `UISelectable` automatically; works in the
  main menu **and** wherever else `SettingsScreen` lives (pause menu); no path strings; no shipped assets;
  visually indistinguishable from vanilla.
* **Cons:** lives *inside* Settings rather than as a first-class "Mods" entry on the root menu;
  `Il2CppReferenceArray` has no `Add`, so you must reallocate and reassign `Categories`.
* **Risk:** if a future patch changes `Categories` from an array to a `List<>`, the reallocation code breaks
  loudly at compile time against the regenerated assemblies (good — it fails visibly, not silently).

### #2 — `MenuScreen.Start()` Postfix, filtered on `OpenOnStart` → new root button + new `MenuScreen`

* **Patch:** `[HarmonyPatch(typeof(Il2CppScheduleOne.UI.MainMenu.MenuScreen), "Start")]` + `Postfix`, then
  `if (!__instance.OpenOnStart) return;` to select the root menu screen only. (Use `Start`, not `Awake`, so
  every sibling screen has finished `Awake`.)
* **Template to clone:** a `UnityEngine.UI.Button` from
  `__instance.GetComponentsInChildren<UnityEngine.UI.Button>(true)`; and a whole sibling `MenuScreen`
  (e.g. the `SettingsScreen`'s GameObject) as the panel.
* **Must do:** clear the inherited inspector-wired click, which *is* possible on this build:
  ```csharp
  clone.onClick.m_PersistentCalls.Clear();     // PersistentCallGroup.Clear() is public
  clone.onClick.DirtyPersistentCalls();        // UnityEventBase.DirtyPersistentCalls() is public
  clone.onClick.RemoveAllListeners();          // UnityEventBase.RemoveAllListeners() is public
  // or, non-destructively, per index:
  // clone.onClick.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.Off);
  ```
* **Pros:** a real top-level "Expansions" entry — maximum discoverability.
* **Cons:** more moving parts; the cloned button sits in a layout group whose ordering you don't control
  precisely (`transform.SetSiblingIndex` needed); cloning a whole `MenuScreen` clones its `MonoState`,
  `uiScreen`/`uiPanel` and all its children, which you then have to strip.
* **Risk:** medium. Highest chance of visual layout surprises.

### #3 — Pause-menu-only, or a phone app

* **Pause menu:** `[HarmonyPatch(typeof(Il2CppScheduleOne.UI.PauseMenu), "Awake")]` + `Postfix`, then work
  through `__instance.Screen` (a `MenuScreen`) and `__instance.Container` (`RectTransform`).
* **Phone app:** subclass `S1API.PhoneApp.PhoneApp` (S1API 3.1.4 is already installed) — the least code by far,
  but it is in-game only and not a menu screen.
* **Pros:** pause menu is in-game contextual; phone app is essentially free via S1API.
* **Cons:** neither satisfies "toggleable from a native-looking **main-menu** screen".
* **Risk:** low, but wrong surface for this requirement.

---

## 5. Pause menu

```csharp
// Il2CppScheduleOne.UI.PauseMenu : Il2CppScheduleOne.DevUtilities.Singleton<PauseMenu>
public class PauseMenu : Singleton<PauseMenu>
{
    // --- Properties (11) ---
    System.Boolean _IsPaused_k__BackingField { get; set; }
    UnityEngine.Canvas Canvas { get; set; }
    UnityEngine.RectTransform Container { get; set; }
    Il2CppScheduleOne.UI.MainMenu.MenuScreen Screen { get; set; }        // <-- same type as the main menu
    Il2CppScheduleOne.State.MonoStateMachine State { get; set; }
    UnityEngine.InputSystem.InputActionReference TogglePauseAction { get; set; }
    Il2CppTMPro.TextMeshProUGUI CartelNameLabel { get; set; }
    Il2CppScheduleOne.Tools.PreallocatedAction onPause { get; set; }
    Il2CppScheduleOne.Tools.PreallocatedAction onResume { get; set; }
    System.Boolean _togglePausePressedThisFrame { get; set; }
    System.Boolean IsPaused { get; set; }

    // --- Methods (15) ---
    public virtual System.Void Awake();
    public System.Boolean CanTogglePause();
    public System.Void CheckTogglePause();
    public System.Void CleanupScreenshot();
    public System.Void Exit(Il2CppScheduleOne.ExitAction action);
    public System.Void LateUpdate();
    public virtual System.Void OnDestroy();
    public System.Void OnGameLoseFocus();
    public System.Void Pause();
    public System.Void PrepForScreenshot();
    public System.Void Resume();
    public virtual System.Void Start();
    public System.Void StuckButtonClicked();
    public System.Void Update();
    public System.Void UpdateCartelName();
}
```

**Yes, the same injection technique works.** The decisive facts:

1. `PauseMenu.Screen` is typed `Il2CppScheduleOne.UI.MainMenu.MenuScreen` — the pause menu **is** a
   `MenuScreen`, so `Open()`/`Close()`/`Lerp()`/`Current` semantics are identical.
2. `SettingsScreen.HostOnlyGameObjects` only makes sense in-game, so the settings screen (and therefore your
   appended category) is reused there. *(Inferred, high confidence — verify at runtime by logging in the
   `SettingsScreen.Awake` Postfix which scene `__instance.gameObject.scene.name` reports.)*
3. `PauseMenu` opens via `TogglePauseAction` (`UnityEngine.InputSystem.InputActionReference`) checked in
   `CheckTogglePause()` from `Update()`, gated by `CanTogglePause()`; and it closes via
   `Exit(Il2CppScheduleOne.ExitAction action)` on the shared exit-listener bus (see §7).
   `onPause` / `onResume` are `Il2CppScheduleOne.Tools.PreallocatedAction` — clean subscribe points that avoid
   per-frame allocation.
4. `PrepForScreenshot()` / `CleanupScreenshot()` exist because pausing grabs a save thumbnail. Do not add
   heavy UI work to `Pause()` postfixes.
5. `StuckButtonClicked()` is the "I'm stuck" teleport button — a confirmed example of a plain
   parameterless `UnityEvent` target on this screen too.

Related in-game menu (the tab/character/phone overlay, distinct from pause):

```csharp
// Il2CppScheduleOne.UI.GameplayMenu : Singleton<GameplayMenu>
static System.Single OpenVerticalOffset, ClosedVerticalOffset, OpenTime, SlideTime { get; set; }
Il2CppScheduleOne.UI.GameplayMenu.EGameplayScreen _CurrentScreen_k__BackingField { get; set; }
UnityEngine.Camera OverlayCamera { get; set; }
UnityEngine.Light OverlayLight { get; set; }
Il2CppScheduleOne.State.MonoStateMachine State { get; set; }
System.Single ContainerOffset_PhoneScreen { get; set; }
UnityEngine.InputSystem.InputActionReference _toggleAction { get; set; }
UnityEngine.InputSystem.InputActionReference _mapShortcutAction { get; set; }
UnityEngine.InputSystem.InputActionReference _journalShortcutAction { get; set; }
UnityEngine.InputSystem.InputActionReference _messagesShortcutAction { get; set; }
System.Boolean IsOpen { get; set; }
System.Boolean CharacterScreenEnabled { get; }
Il2CppScheduleOne.UI.GameplayMenu.EGameplayScreen CurrentScreen { get; set; }
public System.Boolean AcceptInputFromCurrentState();
public System.Void Close();
public System.Void Exit(Il2CppScheduleOne.ExitAction exit);
public System.Void OnClose();
public System.Void OnOpen();
public System.Void Open();
public Il2CppSystem.Collections.IEnumerator SetIsOpenRoutine(System.Boolean open);
public System.Void SetScreen(Il2CppScheduleOne.UI.GameplayMenu.EGameplayScreen screen);
// enum GameplayMenu+EGameplayScreen { Phone = 0, Character = 1 }

// Il2CppScheduleOne.UI.GameplayMenuInterface : Singleton<GameplayMenuInterface>
UnityEngine.Canvas Canvas { get; set; }
UnityEngine.UI.Button PhoneButton { get; set; }
UnityEngine.UI.Button CharacterButton { get; set; }
UnityEngine.RectTransform SelectionIndicator { get; set; }
Il2CppScheduleOne.UI.CharacterInterface CharacterInterface { get; set; }
Il2CppScheduleOne.UI.GameplayMenuTab Tab { get; set; }
UnityEngine.CanvasGroup TabPromptsCanvasGroup { get; set; }
public System.Void CharacterClicked();
public System.Void PhoneClicked();
public System.Void SetSelected(Il2CppScheduleOne.UI.GameplayMenu.EGameplayScreen screen);

// Il2CppScheduleOne.UI.GameplayMenuTab : Il2CppScheduleOne.UITab
public virtual System.Boolean CanNavigate(System.Single navDir);
public System.Boolean CanRespondToInput();
```

`GameplayMenuInterface.PhoneButton` / `CharacterButton` + `SelectionIndicator` is a **second viable
"add a tab" surface** with the same clone-and-append shape (and only two entries, so a third is easy).

### HUD (for completeness)

```csharp
// Il2CppScheduleOne.UI.HUD : Singleton<HUD>
UnityEngine.Canvas canvas; UnityEngine.RectTransform canvasRect;
UnityEngine.UI.Image crosshair, blackOverlay, radialIndicator;
UnityEngine.UI.GraphicRaycaster raycaster;
Il2CppTMPro.TextMeshProUGUI topScreenText; UnityEngine.RectTransform topScreenText_Background;
UnityEngine.UI.Text fpsLabel;
UnityEngine.RectTransform cashSlotContainer, cashSlotUI, onlineBalanceContainer, onlineBalanceSlotUI,
                          managementSlotContainer, HotbarContainer, SlotContainer, QuestEntryContainer,
                          UnreadMessagesPrompt;
Il2CppScheduleOne.UI.ItemSlotUI managementSlotUI, discardSlot;
UnityEngine.UI.Image discardSlotFill;
Il2CppTMPro.TextMeshProUGUI selectedItemLabel, QuestEntryTitle, SleepPrompt, CurfewPrompt;
Il2CppScheduleOne.UI.CrimeStatusUI CrimeStatusUI;
Il2CppScheduleOne.UI.BalanceDisplay OnlineBalanceDisplay;
Il2CppScheduleOne.UI.CrosshairText CrosshairText;
UnityEngine.CanvasGroup NotificationsCanvasGroup, CashSlotHintAnimCanvasGroup;
UnityEngine.Animation CashSlotHintAnim;
Il2CppScheduleOne.Combat.ReticleController _reticleController;
Il2CppScheduleOne.UI.StackSplitTutorial StackSplitTutorial;
UnityEngine.Gradient RedGreenGradient; System.Int32 SampleSize;

public System.Void ShowTopScreenText(System.String t);      // cheap native "toast"
public System.Void HideTopScreenText();
public System.Void SetCrosshairVisible(System.Boolean vis);
public System.Void SetBlackOverlayVisible(System.Boolean vis, System.Single fadeTime);
public System.Void ShowRadialIndicator(System.Single fill);
public System.Void SetFirearmReticle(System.Single spreadAngle);
public System.Void ShowFirearmReticle();
public System.Void HideFirearmReticle();
public System.Single GetAverageFPS();
public System.Void RefreshFPS();
public System.Void UpdateQuestEntryTitle();
public Il2CppSystem.Collections.IEnumerator FadeBlackOverlay(System.Boolean visible, System.Single fadeTime);
```

`HUD.Instance.ShowTopScreenText("…")` is the one-liner native notification. Also
`Il2CppScheduleOne.UI.NotificationsManager` (a `Singleton`) for the queued notification feed.

---

## 6. Phone / app system

### Types

| Namespace | Type | Base |
|---|---|---|
| `Il2CppScheduleOne.UI` | ``App`1<T>`` | `Il2CppScheduleOne.DevUtilities.PlayerSingleton<T>` |
| `Il2CppScheduleOne.UI.Phone` | `Phone` | `PlayerSingleton<Phone>` |
| | `HomeScreen` | `PlayerSingleton<HomeScreen>` |
| | `AppsCanvas` | `PlayerSingleton<AppsCanvas>` |
| | `CallInterface` | `Singleton<CallInterface>` |
| | `CallNotification` | `Singleton<CallNotification>` |
| | `JournalApp` | `App<JournalApp>` |
| | `CounterofferInterface`, `CounterOfferProductSelector`, `CustomerSelector`, `PhoneShopInterface` | `MonoBehaviour` |
| `.Phone.ContactsApp` | `ContactsApp` | `App<ContactsApp>` |
| | `ContactsDetailPanel` | `MonoBehaviour` |
| `.Phone.Delivery` | `DeliveryApp` | `App<DeliveryApp>` |
| | `DeliveryReceiptDisplay`, `DeliveryShop`, `DeliveryStatusDisplay`, `ListingEntry` | `MonoBehaviour` |
| `.Phone.Map` | `MapApp` | `App<MapApp>` |
| `.Phone.Messages` | `MessagesApp` | `App<MessagesApp>` |
| | `DealerManagementApp` | `App<DealerManagementApp>` |
| | `ConfirmationPopup`, `DealWindowSelector`, `MessageBubble`, `MessageSenderInterface`, `WindowSelectorButton` | `MonoBehaviour` |
| | `MessageChain` | `Il2CppSystem.Object` |
| `.Phone.ProductManagerApp` | `ProductManagerApp` | `App<ProductManagerApp>` |
| | `ProductAppDetailPanel`, `ProductTypeContainer` | `MonoBehaviour` |

Confirmed reverse index (`research/raw/03-subclasses.txt:1974-1987`) — **7 apps**, each its own closed generic:

```
BASE Il2CppScheduleOne.UI.App<Il2CppScheduleOne.UI.Phone.ContactsApp.ContactsApp>              -> ContactsApp
BASE Il2CppScheduleOne.UI.App<Il2CppScheduleOne.UI.Phone.Delivery.DeliveryApp>                 -> DeliveryApp
BASE Il2CppScheduleOne.UI.App<Il2CppScheduleOne.UI.Phone.JournalApp>                           -> JournalApp
BASE Il2CppScheduleOne.UI.App<Il2CppScheduleOne.UI.Phone.Map.MapApp>                           -> MapApp
BASE Il2CppScheduleOne.UI.App<Il2CppScheduleOne.UI.Phone.Messages.DealerManagementApp>         -> DealerManagementApp
BASE Il2CppScheduleOne.UI.App<Il2CppScheduleOne.UI.Phone.Messages.MessagesApp>                 -> MessagesApp
BASE Il2CppScheduleOne.UI.App<Il2CppScheduleOne.UI.Phone.ProductManagerApp.ProductManagerApp>  -> ProductManagerApp
```

### The base app type — full signature

```csharp
// Il2CppScheduleOne.UI.App`1<T> : Il2CppScheduleOne.DevUtilities.PlayerSingleton<T>
public class App<T> : PlayerSingleton<T>
{
    // --- Properties (13) ---
    static Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.UI.App<T>> Apps { get; set; }   // <-- the registry
    System.String AppName { get; set; }
    System.String IconLabel { get; set; }
    UnityEngine.Sprite AppIcon { get; set; }
    Il2CppScheduleOne.UI.App<T> Orientation { get; set; }   // typed as App<T> by the dumper; real type is App`1+EOrientation
    System.Boolean AvailableInTutorial { get; set; }
    UnityEngine.RectTransform appContainer { get; set; }
    UnityEngine.RectTransform notificationContainer { get; set; }
    UnityEngine.UI.Text notificationText { get; set; }
    Il2CppScheduleOne.UIScreen _screen { get; set; }
    System.Boolean _isOpen_k__BackingField { get; set; }
    UnityEngine.UI.Button appIconButton { get; set; }
    System.Boolean isOpen { get; set; }

    // --- Methods (14) ---
    public System.Boolean AvailableInCurrentScene();
    public System.Void Close();
    public System.Void Exit(Il2CppScheduleOne.ExitAction action);
    public System.Void GenerateHomeScreenIcon();                          // <-- icon creation
    public static Il2CppScheduleOne.UI.App<T> GetApp(System.Int32 index);
    public System.Boolean IsHoveringButton();
    public virtual System.Void OnExit(Il2CppScheduleOne.ExitAction action);
    public virtual System.Void OnPhoneOpened();
    public virtual System.Void OnStartClient(System.Boolean IsOwner);
    public System.Void SetNotificationCount(System.Int32 amount);
    public virtual System.Void SetOpen(System.Boolean open);
    public System.Void ShortcutClicked();
    public virtual System.Void Start();
    public virtual System.Void Update();
}
// enum App`1+EOrientation { Horizontal = 0, Vertical = 1 }
```

### The registration path

```csharp
// Il2CppScheduleOne.UI.Phone.HomeScreen : PlayerSingleton<HomeScreen>
UnityEngine.Canvas canvas { get; set; }
UnityEngine.UI.Text timeText { get; set; }
UnityEngine.RectTransform appIconContainer { get; set; }     // <-- icon parent
UnityEngine.GameObject appIconPrefab { get; set; }           // <-- icon template (a real prefab field!)
Il2CppScheduleOne.UIScreen uiScreen { get; set; }
Il2CppScheduleOne.UIPanel uiPanel { get; set; }
Il2CppSystem.Collections.Generic.List<UnityEngine.UI.Button> appIcons { get; set; }
Il2CppScheduleOne.UISelectable lastSelectedSelectable { get; set; }
System.Boolean isOpen { get; set; }

public UnityEngine.UI.Button GenerateAppIcon<T>(Il2CppScheduleOne.UI.App<T> prog);   // <-- generic, per app
public Il2CppSystem.Collections.IEnumerator DelayedSetCanvasActive(System.Boolean active, System.Single delay);
public Il2CppSystem.Collections.IEnumerator SelectUIPanel();
public System.Void PhoneClosed();
public System.Void PhoneOpened();
public System.Void SetCanvasActive(System.Boolean a);
public System.Void SetIsOpen(System.Boolean o);
public virtual System.Void OnUncappedMinPass();
public virtual System.Void OnDestroy();
public virtual System.Void OnStartClient(System.Boolean IsOwner);
public virtual System.Void Start();
public virtual System.Void Update();

// Il2CppScheduleOne.UI.Phone.AppsCanvas : PlayerSingleton<AppsCanvas>
UnityEngine.Canvas canvas { get; set; }
System.Boolean isOpen { get; set; }
public Il2CppSystem.Collections.IEnumerator DelayedSetCanvasActive(System.Boolean active, System.Single delay);
public System.Void PhoneClosed();
public System.Void PhoneOpened();
public System.Void SetCanvasActive(System.Boolean a);
public System.Void SetIsOpen(System.Boolean o);

// Il2CppScheduleOne.UI.Phone.Phone : PlayerSingleton<Phone>
static UnityEngine.GameObject ActiveApp { get; set; }
UnityEngine.GameObject phoneModel { get; set; }
UnityEngine.Transform orientation_Vertical { get; set; }
UnityEngine.Transform orientation_Horizontal { get; set; }
UnityEngine.UI.GraphicRaycaster raycaster { get; set; }
Il2CppScheduleOne.State.MonoState state { get; set; }
Il2CppScheduleOne.DevUtilities.ColorFont _generalColorFont { get; set; }
Il2CppScheduleOne.DevUtilities.ColorFont _productColorFont { get; set; }
UnityEngine.InputSystem.InputActionReference _toggleFlashlightInputAction { get; set; }
Il2CppSystem.Action onPhoneOpened { get; set; }
Il2CppSystem.Action onPhoneClosed { get; set; }
Il2CppSystem.Action closeApps { get; set; }
UnityEngine.EventSystems.EventSystem eventSystem { get; set; }
System.Boolean IsOpen { get; set; }
System.Boolean isHorizontal { get; set; }
System.Boolean isOpenable { get; set; }
System.Boolean FlashlightOn { get; set; }
System.Boolean IsAnyAppOpen { get; }
Il2CppScheduleOne.DevUtilities.ColorFont GeneralColorFont { get; }
public System.Boolean MouseRaycast(out UnityEngine.EventSystems.RaycastResult& result);
public System.Void RequestCloseApp();
public System.Void SetIsActiveGameplayScreen(System.Boolean isActive);
public System.Void SetIsHorizontal(System.Boolean h);
public System.Void SetIsOpen(System.Boolean o);
public System.Void SetLookOffsetMultiplier(System.Single multiplier);
public System.Void ToggleFlashlight();
```

**Can a mod add a new app? Yes — and it is already solved.** S1API 3.1.4 (installed) does exactly this:

```csharp
// From research/raw/ns/ns-S1API.Internal.Patches.txt
internal static class S1API.Internal.Patches.HomeScreen_Start_Patch
    [HarmonyPatch(typeof(Il2CppScheduleOne.UI.Phone.HomeScreen), "Start")]
    private static System.Void Postfix(Il2CppScheduleOne.UI.Phone.HomeScreen __instance);

internal static class S1API.Internal.Patches.HomeScreenScrollPatch
    [HarmonyPatch(typeof(Il2CppScheduleOne.UI.Phone.HomeScreen), "Start")]
    private static System.Void Postfix(Il2CppScheduleOne.UI.Phone.HomeScreen __instance);
    private static System.Void SetupScrollableGrid(Il2CppScheduleOne.UI.Phone.HomeScreen homeScreen);
    private static UnityEngine.UI.Scrollbar CreateScrollbar(UnityEngine.Transform parent);
    private static System.Void ConfigureHiddenStub(UnityEngine.RectTransform stubRect);
```

and the public surface you would subclass:

```csharp
// S1API.PhoneApp.PhoneApp : S1API.Internal.Abstraction.Registerable, IRegisterable
protected System.String AppName    { get; }
protected System.String AppTitle   { get; }
protected System.String IconLabel  { get; }
protected System.String IconFileName { get; }
protected UnityEngine.Sprite IconSprite { get; }
protected S1API.PhoneApp.PhoneApp.EOrientation Orientation { get; }   // { Horizontal = 0, Vertical = 1 }

protected abstract System.Void OnCreatedUI(UnityEngine.GameObject container);   // <-- your UI goes here
protected virtual  System.Void OnCreated();
protected virtual  System.Void OnDestroyed();
protected virtual  System.Void OnPhoneClosed();
public    virtual  System.Void Exit(S1API.PhoneApp.ExitAction exit);
public System.Void OpenApp();
public System.Void CloseApp();
public System.Boolean IsOpen();
public System.Boolean SetIconSprite(UnityEngine.Sprite sprite);
public System.Boolean SetIconTexture(UnityEngine.Texture2D texture);

// Supporting: S1API.Internal.Phone.AppIconsRedirect [RegisterTypeInIl2Cpp] : MonoBehaviour
//   mirrors mod icons into a hidden stub grid so the native layout isn't disturbed
// Supporting: S1API.PhoneApp.PhoneAppButtonHandler [RegisterTypeInIl2Cpp] : MonoBehaviour
```

Do **not** try to add to `App<T>.Apps` yourself: `App<T>` is generic-per-app
(`PlayerSingleton<T>` + `static List<App<T>> Apps`), so each closed generic has its **own separate static
`Apps` list**. There is no shared registry to append to — which is precisely why S1API patches
`HomeScreen.Start` and builds the icon + panel by hand instead.

If you do build a phone app by hand rather than via S1API, use `HomeScreen.appIconPrefab` as the icon
template and `HomeScreen.appIconContainer` as the parent — those are verified serialized fields, no path
strings needed.

---

## 7. Shared UI infrastructure

### Singleton access pattern (`Il2CppScheduleOne.DevUtilities`) — needed to reach every UI manager

```csharp
// Il2CppScheduleOne.DevUtilities.Singleton`1<T> : UnityEngine.MonoBehaviour
static T instance { get; set; }
System.Boolean Destroyed { get; set; }
static System.Boolean InstanceExists { get; }     // <-- ALWAYS check this first
static T Instance { get; set; }
public virtual System.Void Awake();
public virtual System.Void OnDestroy();
public virtual System.Void Start();

// Il2CppScheduleOne.DevUtilities.PersistentSingleton`1<T> : Singleton<T>
public virtual System.Void Awake();               // adds DontDestroyOnLoad behaviour

// Il2CppScheduleOne.DevUtilities.NetworkSingleton`1<T> : Il2CppFishNet.Object.NetworkBehaviour
static T instance { get; set; }
System.Boolean Destroyed { get; set; }
System.Boolean field_Private_Boolean_0 { get; set; }   // stripped name — unstable
System.Boolean field_Private_Boolean_1 { get; set; }   // stripped name — unstable
static System.Boolean InstanceExists { get; }
static T Instance { get; set; }
public virtual System.Void Awake();
public virtual System.Void Method_Protected_Virtual_New_Void_0();   // stripped name — unstable
public virtual System.Void NetworkInitializeIfDisabled();
public virtual System.Void NetworkInitialize__Late();
public virtual System.Void NetworkInitialize___Early();
public virtual System.Void OnDestroy();
public virtual System.Void Start();

// Il2CppScheduleOne.DevUtilities.PlayerSingleton`1<T> : UnityEngine.MonoBehaviour
static T instance { get; set; }
static System.Boolean InstanceExists { get; }
static T Instance { get; set; }
public virtual System.Void Awake();
public virtual System.Void OnDestroy();
public virtual System.Void OnStartClient(System.Boolean IsOwner);
public virtual System.Void Start();
```

Canonical safe access (a null `Instance` on a scene-scoped singleton is the #1 crash source when the
menu scene is loaded):

```csharp
if (Il2CppScheduleOne.UI.PauseMenu.InstanceExists)
    Il2CppScheduleOne.UI.PauseMenu.Instance.Resume();
```

Who is which flavour (all confirmed from the dumps):

| Flavour | UI managers |
|---|---|
| `PersistentSingleton<T>` (survives scene loads — safe in `Menu` **and** `Main`) | `Il2CppScheduleOne.UIScreenManager`, `Il2CppScheduleOne.GameInput`, `Il2CppScheduleOne.DevUtilities.Settings`, `Il2CppScheduleOne.DevUtilities.CoroutineService`, `Il2CppScheduleOne.Configuration.ConfigurationService`, `Il2CppScheduleOne.Persistence.LoadManager`, `Il2CppScheduleOne.Persistence.SaveManager` |
| `Singleton<T>` (scene-scoped) | `Il2CppScheduleOne.UI.MainMenu.MainMenuPopup`, `Il2CppScheduleOne.UI.PauseMenu`, `Il2CppScheduleOne.UI.GameplayMenu`, `Il2CppScheduleOne.UI.GameplayMenuInterface`, `Il2CppScheduleOne.UI.HUD`, `Il2CppScheduleOne.UI.MouseTooltip`, `Il2CppScheduleOne.UI.NotificationsManager`, `Il2CppScheduleOne.UI.Tooltips.TooltipManager`, `Il2CppScheduleOne.UI.Input.InputPromptsManager`, `Il2CppScheduleOne.UI.Multiplayer.LobbyInterface`, `Il2CppScheduleOne.UI.GenericSelectionModule`, `Il2CppScheduleOne.FullscreenFade`, `Il2CppScheduleOne.Console`, `Il2CppScheduleOne.PlayerScripts.CursorManager` |
| `PlayerSingleton<T>` (per local player) | `Il2CppScheduleOne.UI.Phone.Phone`, `.Phone.HomeScreen`, `.Phone.AppsCanvas`, `Il2CppScheduleOne.UI.App<T>` |
| `NetworkSingleton<T>` | `Il2CppScheduleOne.DevUtilities.GameManager` |

### Screen / panel / selectable framework (root `Il2CppScheduleOne` namespace)

The navigation stack is `UIScreenManager` → `UIScreen` → `UIPanel` → `UISelectable` (→ `UITrigger`).

```csharp
// Il2CppScheduleOne.UIScreenManager : PersistentSingleton<UIScreenManager>
static System.Single NavigationRepeatDelay, NavigationRepeatRate, DefaultScrollSpeed,
                     ScrollbarScrollSpeed, NavigationThreshold { get; set; }
Il2CppReferenceArray<Il2CppScheduleOne.UIPopupScreen> popupScreenPrefabs { get; set; }
UnityEngine.InputSystem.InputActionReference submitInputAction { get; set; }
UnityEngine.InputSystem.InputActionReference backInputAction { get; set; }
UnityEngine.InputSystem.InputActionReference escapeInputAction { get; set; }
Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.UIPopupScreen> popupScreenInstances { get; set; }
Il2CppSystem.Collections.Generic.Stack<Il2CppScheduleOne.UIScreenManager.UIScreenInfo> screenStack { get; set; }
static UnityEngine.GameObject lastSelectedObject { get; set; }
static System.Boolean isBackTriggeredThisFrame { get; set; }
Il2CppScheduleOne.UIScreen TopScreen { get; }
System.Boolean HasActiveScreen { get; }
public System.Void AddScreen(Il2CppScheduleOne.UIScreen screen);
public System.Void AddScreen(Il2CppScheduleOne.UIScreen screen, Il2CppSystem.Action onCloseCallback);
public System.Void RemoveScreen(Il2CppScheduleOne.UIScreen screen);
public System.Void BackToCloseCurrentScreen();
public System.Void CheckInputDeviceMode();
public System.Void ClosePopupScreen(System.String popupID);
public Il2CppScheduleOne.UIPopupScreen FindPopupScreen(System.String popupID);
public System.Void HandleInputDeviceChanged(Il2CppScheduleOne.GameInput.InputDeviceType type);
public System.Boolean IsActiveScreenRegisteredForBack();
public System.Boolean IsAnyPopupScreenActive();
public System.Boolean IsAnyScreenActive();
public System.Boolean IsScreenInStack(Il2CppScheduleOne.UIScreen screen);
public System.Void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode);
public System.Void OpenPopupScreen(System.String popupID);
public System.Void OpenPopupScreen(System.String popupID, Il2CppReferenceArray<Il2CppSystem.Object> args);
//   UIScreenManager+UIScreenInfo : Il2CppSystem.ValueType { UIScreen screen; Il2CppSystem.Action onCloseCallback; }

// Il2CppScheduleOne.UIScreen : UnityEngine.MonoBehaviour
Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.UIPanel> panels { get; set; }
Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.InputDescriptor> inputDescriptors { get; set; }
UnityEngine.UI.ScrollRect activeScrollRect { get; set; }
System.Boolean addScreenOnStart, addScreenOnEnable, removeScreenOnDisable, autoAttachToInventory { get; set; }
Il2CppScheduleOne.UIPanel currentSelectedPanel { get; set; }
Il2CppScheduleOne.PanelChangeEvent _onPanelChange { get; set; }
System.Boolean IsSelected { get; set; }
Il2CppScheduleOne.UIPanel CurrentSelectedPanel { get; }
Il2CppSystem.Collections.Generic.IReadOnlyList<Il2CppScheduleOne.UIPanel> Panels { get; }
UnityEngine.Canvas Canvas { get; }
public System.Void AddPanel(Il2CppScheduleOne.UIPanel panel);
public System.Void RemovePanel(Il2CppScheduleOne.UIPanel panel);
public System.Void ClearPanels();
public System.Void InitScreen();
public System.Boolean NavigateToPanel(Il2CppScheduleOne.UIPanel panel);
public System.Void SetCurrentSelectedPanel(Il2CppScheduleOne.UIPanel panel,
                                           Il2CppScheduleOne.UISelectable overrideSelectable = null,
                                           System.Boolean scrollToChild = True,
                                           System.Boolean allowReselect = False);
public System.Void SubscribeToPanelChange(Il2CppScheduleOne.PanelChangeEvent callback);
public System.Void UnsubscribeFromPanelChange(Il2CppScheduleOne.PanelChangeEvent callback);
public virtual System.Void OnAwake();      // <-- override points if you inject your own UIScreen subclass
public virtual System.Void OnStarted();
public virtual System.Void OnDestroyed();

// Il2CppScheduleOne.UIPanel : UnityEngine.MonoBehaviour  (48 properties / 48 methods — key members only)
Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.UISelectable> selectables { get; set; }
Il2CppScheduleOne.UISelectable defaultSelectable { get; set; }
Il2CppScheduleOne.UIPanel.EPanelSelectionMode selectionModeOnEnter { get; set; }
UnityEngine.UI.ScrollRect scrollRect { get; set; }
UnityEngine.Vector2 scrollMargin { get; set; }
System.Int32 priority { get; set; }
System.Boolean selectPanelOnStart, selectPanelOnEnable, deselectPanelOnDisable { get; set; }
System.Boolean preventSideNavigation, allowDiagonalNavigation, useFullRectForNavigation { get; set; }
Il2CppScheduleOne.UIPanel.ENavigationOrigin navigationOrigin { get; set; }
Il2CppScheduleOne.UIPanel.EPanelExitMode panelExitMode { get; set; }
Il2CppScheduleOne.NavigationOverride<Il2CppScheduleOne.UIPanel> navigationOverride { get; set; }
UnityEngine.Events.UnityEvent OnPanelSelected { get; set; }
UnityEngine.Events.UnityEvent OnPanelDeselected { get; set; }
public System.Boolean AddSelectable(Il2CppScheduleOne.UISelectable selectable);
public System.Void RemoveSelectable(Il2CppScheduleOne.UISelectable selectable, System.Boolean autoFallback = True);
public Il2CppScheduleOne.UISelectable Select(Il2CppScheduleOne.UISelectable overrideSelectable = null,
                                             System.Boolean scrollToChild = True);
public System.Void ScrollToChild(UnityEngine.RectTransform child, System.Single duration = 0.15);
public System.Void SetParentScreen(Il2CppScheduleOne.UIScreen screen);
// enum UIPanel+ENavigationOrigin      { TopLeft=0, TopRight=1, BottomLeft=2, BottomRight=3, Center=4, Custom=5 }
// enum UIPanel+EPanelExitMode         { BestMatch=0, LastSelected=1 }
// enum UIPanel+EPanelSelectionMode    { KeepLastSelection=0, NearestToPreviousSelection=1, NearestToNavigationOrigin=2 }
// enum UIPanel+UINavigationType       { ImmediateDirection=0, NearestDirectionAndDistance=1 }

// Il2CppScheduleOne.UITab : Il2CppScheduleOne.UIPanel      (SettingsScreen.Tab is one of these)
System.Boolean allowLooping { get; set; }
Il2CppScheduleOne.UITab.CycleInputActionType cycleInputActionType { get; set; }
Il2CppScheduleOne.UITab.CycleDirection cycleDirection { get; set; }
System.Boolean reverseCycleDirection { get; set; }
Il2CppTMPro.TextMeshProUGUI cycleLeftVisual { get; set; }
Il2CppTMPro.TextMeshProUGUI cycleRightVisual { get; set; }
public virtual System.Boolean CanNavigate(System.Single navDir);
public System.Void CycleTab(System.Single navDir, System.Single delay, System.Single speed);
public System.Void CycleTabWithoutEvent(System.Single navDir);
public System.Single GetCycleTabInputValue();
public System.Boolean Navigate(System.Single navDir);
// enum UITab+CycleDirection        { Horizontal = 0, Vertical = 1 }
// enum UITab+CycleInputActionType  { Primary = 0, Secondary = 1, Tertiary = 2 }

// Il2CppScheduleOne.UISelectable : Il2CppScheduleOne.UITrigger  (key members)
UnityEngine.GameObject selectedImage { get; set; }
System.Boolean addToPanelOnAwake { get; set; }         // <-- why cloning gets you gamepad nav for free
System.Boolean findAnotherSelectableInPanelOnDisable { get; set; }
System.Boolean blockSelectionOnInteractableFalse { get; set; }
UnityEngine.UI.Text _label { get; set; }
Il2CppScheduleOne.UI.Input.InputPromptsData _inputPrompt { get; set; }
Il2CppScheduleOne.UI.Input.EmbeddedInputPromptUI _embeddedInputPrompt { get; set; }
UnityEngine.Events.UnityEvent OnSelected { get; set; }
UnityEngine.Events.UnityEvent OnDeselected { get; set; }
Il2CppScheduleOne.UIPanel ParentPanel { get; set; }
System.Boolean CanBeSelected { get; }
public System.Void SetParentPanel(Il2CppScheduleOne.UIPanel panel);
public System.Void SetSelectedImageVisible(System.Boolean visible);

// Il2CppScheduleOne.UITrigger : UnityEngine.MonoBehaviour   (press/hold behaviour under every button)
Il2CppScheduleOne.UITrigger.TriggerType triggerType { get; set; }
System.Boolean mouseAlwaysPress { get; set; }
System.Single holdDuration { get; set; }
UnityEngine.UI.Image holdImage { get; set; }
UnityEngine.UI.Selectable uGUISelectable { get; set; }
UnityEngine.Events.UnityEvent OnTrigger { get; set; }
UnityEngine.Events.UnityEvent OnRelease { get; set; }
System.Boolean Interactable { get; set; }
public System.Void UpdateHoldImage(System.Single amount);
// enum UITrigger+TriggerType { Press = 0, Hold = 1, PressAndRelease = 2 }

// Il2CppScheduleOne.UIPopupScreen_ConfirmationMenu : UIPopupScreen : UIScreen
Il2CppTMPro.TMP_Text titleText { get; set; }
Il2CppTMPro.TMP_Text messageText { get; set; }
Il2CppScheduleOne.UISelectable confirmButton { get; set; }
Il2CppScheduleOne.UISelectable cancelButton { get; set; }
public System.Void Open();
public Il2CppSystem.Collections.IEnumerator RegisterInput(Il2CppSystem.Action onConfirm, Il2CppSystem.Action onCancel);
public System.Void SelectPanel(Il2CppScheduleOne.UISelectable selectable);

// Il2CppScheduleOne.UIPopupScreen_ContextMenu : UIPopupScreen
Il2CppScheduleOne.UISelectable selectablePrefab { get; set; }
UnityEngine.Transform contentParent { get; set; }
UnityEngine.RectTransform anchorRectTransform { get; set; }
UnityEngine.GameObject screenBlocker { get; set; }
Il2CppScheduleOne.UIPopupScreen_ContextMenu.AnchorType anchor { get; set; }
public System.Void AddOption(System.Int32 id, System.String name, Il2CppSystem.Action action);
public System.Void Clear();
public System.Void SetPosition(UnityEngine.Vector2 pos);
// enum UIPopupScreen_ContextMenu+AnchorType { TopLeft=0, BottomLeft=1, Center=2 }
//   ContextMenuOption { System.Int32 optionID; System.String optionName; Il2CppSystem.Action optionAction;
//                       .ctor(System.Int32 id, System.String name, Il2CppSystem.Action action); }

// Il2CppScheduleOne.UI.TabController : UnityEngine.MonoBehaviour   (the tab-strip used by phone apps)
UnityEngine.RectTransform _tabIndicator { get; set; }
Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.UI.TabItemUI> _tabItems { get; set; }
System.Single _indicatorMoveTime { get; set; }
System.Boolean _allowLoopingNavigation { get; set; }
UnityEngine.AnimationCurve _indicatorMoveCurve { get; set; }
Il2CppScheduleOne.DevUtilities.ColorFont _tabColorFont { get; set; }
Il2CppScheduleOne.UIScreen _screen { get; set; }
Il2CppScheduleOne.UITab _uiTab { get; set; }
System.Int32 CurrentTabIndex { get; }
public System.Void SetTab(System.Int32 index);
public System.Void SetTab(System.Int32 index, System.Boolean instantIndicatorMove = False,
                          System.Boolean forceUpdateUI = False);
public System.Void SetTabIndicatorText(System.Int32 index, System.String text);
public System.Void SetToSelectedTab(System.Boolean instantIndicatorMove = False);
public System.Void HideTabIndicator(System.Int32 index);
public System.Void SubscribeToTabSelected(Il2CppScheduleOne.UI.TabSelectedEvent handler);
public System.Void UnsubscribeFromTabSelected(Il2CppScheduleOne.UI.TabSelectedEvent handler);

// Il2CppScheduleOne.UI.TabItemUI : UnityEngine.MonoBehaviour
Il2CppScheduleOne.UI.ButtonUI _button { get; set; }
UnityEngine.UI.Text _label { get; set; }
UnityEngine.GameObject _content { get; set; }
UnityEngine.GameObject _indicator { get; set; }
UnityEngine.UI.Text _indicatorLabel { get; set; }
Il2CppScheduleOne.UIPanel _contentPanel { get; set; }
public System.Void HideIndicator();
public System.Void SetIndicator(System.String text);

// Il2CppScheduleOne.UI.ButtonUI : UnityEngine.MonoBehaviour
UnityEngine.UI.Button _button { get; set; }
System.Int32 _id { get; set; }
Il2CppSystem.Action<System.Int32> OnSelect { get; set; }
UnityEngine.UI.Button Button { get; }
public System.Void Initialize(System.Int32 id);

// Il2CppScheduleOne.UI.TabSelectedEvent : Il2CppSystem.MulticastDelegate
public virtual System.Void Invoke(System.Int32 index);
public static Il2CppScheduleOne.UI.TabSelectedEvent op_Implicit(System.Action<System.Int32>  = null);
```

`Il2CppScheduleOne.UI.TabController` + `TabItemUI` is a **second, cleaner tab surface than `SettingsScreen`**
if you ever want a mod tab inside a phone app: `_tabItems` is a `List<TabItemUI>` (not an array), so appending
is trivial.

### Input blocking, cursor & the exit bus — do this instead of fighting `Cursor.lockState`

`CreativeModeMod.cs` currently forces `Cursor.lockState = CursorLockMode.None` every `OnUpdate` because "the
game re-locks mouse otherwise". The *reason* is `Il2CppScheduleOne.State.StatePropertiesTransitionHandler`,
and the correct fix is to participate in the state system rather than to fight it.

```csharp
// Il2CppScheduleOne.State.StateProperties : System.ValueType
public StateProperties.EMouseState        MouseState   [FieldOffset(0)]
public StateProperties.ECrosshairState    Crosshair    [FieldOffset(4)]
public StateProperties.EEquippingState    Equipping    [FieldOffset(8)]
public StateProperties.EInventoryState    Inventory    [FieldOffset(12)]
public StateProperties.EHUDState          HUD          [FieldOffset(16)]
public StateProperties.ECompassState      Compass      [FieldOffset(20)]
public StateProperties.EMovementState     Movement     [FieldOffset(24)]
public StateProperties.ELookState         CameraLook   [FieldOffset(28)]
public StateProperties.EBlurState         Blur         [FieldOffset(32)]
public Il2CppScheduleOne.PlayerScripts.PlayerCamera.ECameraMode CameraMode [FieldOffset(36)]

static StateProperties Unenforced   { get; set; }
static StateProperties UIDefault    { get; set; }
static StateProperties UIWithBlur   { get; set; }
static StateProperties Vehicle      { get; set; }
static StateProperties Skateboard   { get; set; }
static StateProperties Task         { get; set; }
static StateProperties UINoInventory{ get; set; }
static StateProperties Cutscene     { get; set; }

public static StateProperties GetPreset(StateProperties.EPreset preset);
public static System.Void Transition(StateProperties from, StateProperties to);

// enum StateProperties+EMouseState      { Unenforced=0, Free=1, Locked=2 }   <-- the cursor lock
// enum StateProperties+ELookState       { Unenforced=0, Free=1, Locked=2 }
// enum StateProperties+EMovementState   { Unenforced=0, Free=1, Locked=2 }
// enum StateProperties+ECrosshairState  { Unenforced=0, Visible=1, Hidden=2 }
// enum StateProperties+EHUDState        { Unenforced=0, Visible=1, Hidden=2 }
// enum StateProperties+ECompassState    { Unenforced=0, Visible=1, Hidden=2 }
// enum StateProperties+EEquippingState  { Unenforced=0, Enabled=1, Disabled=2 }
// enum StateProperties+EInventoryState  { Unenforced=0, Interactable=1, NonInteractable=2, Disabled=3 }
// enum StateProperties+EBlurState       { Unenforced=0, Enabled=1, Disabled=2 }
// enum StateProperties+EPreset          { Unenforced=0, UIDefault=1, Vehicle=2, UIWithBlur=3,
//                                         Task=4, UINoInventory=5, Cutscene=6 }

// Il2CppScheduleOne.State.StatePropertiesTransitionHandler : static
static StateProperties _currentProperties { get; set; }
public static System.Void Transition(StateProperties newProperties);

// Il2CppScheduleOne.State.MonoState : UnityEngine.MonoBehaviour   (attach one to your panel!)
Il2CppScheduleOne.State.MonoStateMachine _defaultParent { get; set; }
System.Boolean _customStateProperties { get; set; }
Il2CppScheduleOne.State.StateProperties.EPreset _preset { get; set; }
Il2CppScheduleOne.State.StateProperties _properties { get; set; }
System.Boolean _autoSetupExitListeners { get; set; }
System.Int32 _exitListenerPriority { get; set; }
System.Boolean _enableItemQuickMove { get; set; }
Il2CppStructArray<Il2CppScheduleOne.State.IState.EFlag> _flags { get; set; }
Il2CppScheduleOne.UI.Input.InputPromptsData _defaultInputPrompts { get; set; }
System.Boolean _subscribeToInputPromptModuleEvents { get; set; }
Il2CppSystem.Action OnAddedToStack, OnRemovedFromStack, OnStateActivate,
                    OnStateDeactivate, OnBecomeTopSibling, OnNoLongerTopSibling { get; set; }
System.Boolean IsActive { get; }
System.Boolean IsAcceptingInput { get; }
Il2CppScheduleOne.State.StateProperties Properties { get; set; }
public System.Void PushToDefaultParent();
public System.Void PopFromDefaultParent();
public System.Void RemoveFromDefaultParent();
public System.Void InitializeDefaultParent(Il2CppScheduleOne.State.MonoStateMachine parent);
public System.Void AssertDefaultParent();
public System.Void AddFlag(Il2CppScheduleOne.State.IState.EFlag flag);
public System.Void LoadModule(Il2CppScheduleOne.UI.Input.InputPromptsData module, System.String displayTextOverride = null);
public System.Void LoadModule(System.String moduleId,
                              Il2CppScheduleOne.UI.Input.EInputPromptPosition position = 0,
                              System.String displayTextOverride = null);
public System.Void UnloadModule(System.String moduleId);
public virtual System.Void OnActivate();
public virtual System.Void OnDeactivate();
public System.Void OnExit(Il2CppScheduleOne.ExitAction action);

// Il2CppScheduleOne.State.MonoStateMachine : MonoState
Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.State.IState> Stack { get; set; }
public System.Boolean IsAnyChildStateAcceptingInput();
public virtual Il2CppScheduleOne.State.IState Peek();
public virtual Il2CppScheduleOne.State.IState PeekRecursive();
public virtual System.Void Pop();
public virtual System.Void Push(Il2CppScheduleOne.State.IState state);
public virtual System.Void Remove(Il2CppScheduleOne.State.IState state);
// Il2CppScheduleOne.State.InGameStateMachine : MonoStateMachine { public System.Void PopUntilDefault(); }
// enum IState+EFlag { CanToggleClipboard=0, AllowPlayerMovement=1, CanDismissHint=2, HideWorldspaceDialogue=3 }
// Il2CppScheduleOne.State.State : Il2CppSystem.Object    (non-MonoBehaviour variant, same surface)
//   public .ctor(System.String name, IStateMachine defaultParent = null,
//                StateProperties.EPreset preset = 0, InputPromptsData _defaultInputPrompts = null);
```

**Cursor: two other legitimate paths.**

```csharp
// Il2CppScheduleOne.PlayerScripts.PlayerCamera (a PlayerSingleton)
public System.Void FreeMouse(System.Boolean hideCrosshair = True);
public System.Void LockMouse(System.Boolean showCrosshair = True);

// Il2CppScheduleOne.PlayerScripts.CursorManager : Singleton<CursorManager>
Il2CppSystem.Collections.Generic.List<CursorManager.CursorConfig> Cursors { get; set; }
public System.Void SetCursorAppearance(CursorManager.ECursorType type);
//   CursorManager+CursorConfig { CursorManager.ECursorType CursorType; ... }
```

`PlayerCamera.FreeMouse()` / `LockMouse()` is the game's own toggle. `CursorManager.SetCursorAppearance` swaps
the cursor sprite. **However:** in the `Menu` scene there is no `PlayerCamera`, and a `MenuScreen`'s
`MonoState` already sets `EMouseState.Free`, so a settings-category injection needs **no cursor code at all**.
That is another reason #1 beats a from-scratch overlay.

**The escape/back bus** — this is how you get "Esc closes my panel" working natively:

```csharp
// Il2CppScheduleOne.GameInput : PersistentSingleton<GameInput>
UnityEngine.InputSystem.InputActionReference PrimaryExitAction { get; set; }
UnityEngine.InputSystem.InputActionReference SecondaryExitAction { get; set; }
static Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.GameInput.ExitListener> exitListeners { get; set; }
static System.Boolean IsTyping { get; set; }
static Il2CppScheduleOne.GameInput.InputDeviceType CurrentInputDevice { get; set; }
static Il2CppScheduleOne.UI.Input.EPlatformType CurrentPlatformType { get; set; }
static Il2CppSystem.Action<Il2CppScheduleOne.GameInput.InputDeviceType> OnInputDeviceChanged { get; set; }

public static System.Void RegisterExitListener(Il2CppScheduleOne.GameInput.ExitDelegate listener,
                                               System.Int32 priority = 0);
public static System.Void RegisterExitListener(Il2CppSystem.Action exitMethod,
                                               Il2CppSystem.Func<System.Boolean> condition,
                                               System.Int32 priority = 0,
                                               System.Boolean primaryExitOnly = False);
public static System.Void DeregisterExitListener(Il2CppScheduleOne.GameInput.ExitDelegate listener);
public System.Void Exit(Il2CppScheduleOne.ExitType type);
public System.Void ExitAll();
public System.Void HandleExitInputs();
public static System.Boolean GetButton(Il2CppScheduleOne.GameInput.ButtonCode buttonCode);
public static System.Boolean GetButtonDown(Il2CppScheduleOne.GameInput.ButtonCode buttonCode);
public static System.Boolean GetButtonUp(Il2CppScheduleOne.GameInput.ButtonCode buttonCode);
public static Il2CppScheduleOne.GameInput.InputDeviceType GetCurrentInputDevice();
public static System.Boolean GetCurrentInputDeviceIsGamepad();
public static System.Boolean GetCurrentInputDeviceIsKeyboardMouse();
public static UnityEngine.Vector3 GetPointerPosition();
public System.Boolean TryGetAction(System.String actionName, out UnityEngine.InputSystem.InputAction& action);
// enum GameInput+InputDeviceType { KeyboardMouse = 0, Gamepad = 1 }
// GameInput+ExitListener { GameInput.ExitDelegate listenerFunction; System.Int32 priority; }
// GameInput+ExitDelegate : Il2CppSystem.MulticastDelegate
//   public virtual System.Void Invoke(Il2CppScheduleOne.ExitAction exitAction);
//   public static GameInput.ExitDelegate op_Implicit(System.Action<Il2CppScheduleOne.ExitAction>  = null);

// Il2CppScheduleOne.ExitAction : Il2CppSystem.Object
Il2CppScheduleOne.ExitType Type { get; set; }
System.Boolean Used { get; set; }
public .ctor(Il2CppScheduleOne.ExitType type);
public System.Void Use();                     // <-- consume the press so nothing else handles it
// enum Il2CppScheduleOne.ExitType { Primary = 0, Secondary = 1 }
```

`GameInput.RegisterExitListener(Il2CppSystem.Action exitMethod, Il2CppSystem.Func<bool> condition, int priority, bool primaryExitOnly)`
is the mod-friendly overload: no delegate type to construct beyond the standard `op_Implicit` conversions, and
`condition` lets you say "only when my panel is open". `MenuScreen.ExitInputPriority` shows the game's own
priority convention.

`GameInput.IsTyping` is what suppresses hotkeys while a `TMP_InputField` has focus — **respect it** in your
mod's keybinds.

### Tooltips

```csharp
// Il2CppScheduleOne.UI.Tooltips.TooltipManager : Singleton<TooltipManager>
UnityEngine.Canvas Canvas { get; set; }
UnityEngine.RectTransform anchor { get; set; }
Il2CppTMPro.TextMeshProUGUI tooltipLabel { get; set; }
Il2CppSystem.Collections.Generic.List<UnityEngine.Canvas> canvases { get; set; }
Il2CppSystem.Collections.Generic.List<UnityEngine.Canvas> sortedCanvases { get; set; }
Il2CppSystem.Collections.Generic.List<UnityEngine.UI.GraphicRaycaster> raycasters { get; set; }
UnityEngine.EventSystems.EventSystem eventSystem { get; set; }
System.Boolean tooltipShownThisFrame { get; set; }
System.Boolean _manuallyDeactivateTooltip { get; set; }
public System.Void AddCanvas(UnityEngine.Canvas canvas);       // <-- register a NEW canvas for hover detection
public System.Void CheckForTooltipHover();
public System.Void HideTooltip();
public System.Void ShowTooltip(System.String text, UnityEngine.Vector2 position,
                               System.Boolean worldspace, System.Boolean manuallyDeactivate = False);

// Il2CppScheduleOne.UI.Tooltips.Tooltip : UnityEngine.MonoBehaviour   (add this to any hoverable element)
System.String text { get; set; }
UnityEngine.Vector2 labelOffset { get; set; }
UnityEngine.RectTransform LabelOriginRect { get; set; }
UnityEngine.Canvas canvas { get; set; }
UnityEngine.Vector3 labelPosition { get; }
System.Boolean isWorldspace { get; set; }
public virtual System.Void Awake();

// Il2CppScheduleOne.UI.Tooltips.TooltipCanvasInitializer : UnityEngine.MonoBehaviour
public System.Void Start();

// Il2CppScheduleOne.UI.MouseTooltip : Singleton<MouseTooltip>   (the cursor-attached variant)
UnityEngine.RectTransform IconRect, TooltipRect { get; set; }
UnityEngine.UI.Image IconImg { get; set; }
Il2CppTMPro.TextMeshProUGUI TooltipLabel { get; set; }
UnityEngine.Vector3 TooltipOffset_NoIcon, TooltipOffset_WithIcon, IconOffset { get; set; }
UnityEngine.Color Color_Invalid { get; set; }
UnityEngine.Sprite Sprite_Cross { get; set; }
public System.Void ShowIcon(UnityEngine.Sprite sprite, UnityEngine.Color col);
public System.Void ShowTooltip(System.String text, UnityEngine.Color col);
```

If you build a panel on a **new** `Canvas`, you must call
`TooltipManager.Instance.AddCanvas(myCanvas)` or hover tooltips silently never fire — confirmed by the literal
`TooltipManager: AddCanvas called with null canvas` (`literals-sorted.txt:14342`). If you clone into an
existing canvas (the recommended approach) this is already handled.

### Input prompts (`Il2CppScheduleOne.UI.Input`, 17 types)

`InputPromptsManager` (a `Singleton`) drives the bottom-left / centre prompt strips. Full member list is in
`research/raw/ns/ns-Il2CppScheduleOne.UI.Input.txt`; the mod-relevant entry points are:

```csharp
public System.Void LoadModule(System.String id,
                              Il2CppScheduleOne.UI.Input.EInputPromptPosition position = 0,
                              System.String displayTextOverride = null);
public System.Void UnloadModule(System.String id);
public System.Boolean AddInputPrompt(System.String panelId,
                                     Il2CppScheduleOne.UI.Input.InputPromptsDescriptorData descriptor,
                                     System.Boolean isPulsing,
                                     System.String displayTextOverride = null);
public System.Boolean HasActivePrompt(System.String id);
public System.Void ShowHideActivePrompt(System.String panelId, System.Boolean show);
public System.Void UpdateDisplayTextOverride(System.String panelId, System.String displayTextOverride);
public System.Boolean TryGetActionBindingDisplayString(System.String actionName, out System.String& displayString);
public Il2CppScheduleOne.UI.Input.InputPromptsData GetInputPromptData(System.String id);
public System.String GetDisplayNameForControlPath(System.String controlPath);
// enum EInputPromptPosition { BottomLeftInGame=0, BottomLeftMenu=1, Center=2, Custom=3 }
// enum EPlatformType        { Steam=0, Xbox=1, PlayStation=2, Nintendo=3 }
```

Module IDs are `InputPromptsData.Id` strings loaded from `Resources` — see §8 for the verified path list
(e.g. `inputdata/promptdata/back/promptdata_back`).

---

## 8. Asset loading

**Verdict: UI is loaded with the scene. There is no `Resources.Load` path and no asset bundle for UI.**

Evidence:

1. `Schedule I_Data\StreamingAssets` contains only `DefaultSave`, `DefaultTutorialSave`,
   `UnityServicesProjectConfiguration.json` — **no `.bundle` / `.assetbundle` files anywhere.**
2. `Schedule I_Data\Resources` contains only `unity default resources` and `unity_builtin_extra` (engine
   built-ins).
3. The `Resources` path table in `globalgamemanagers` has these top-level folders **and no `ui/`**:
   ```
   avatar/  cash/  clothing/  decoration/  equippables/  furniture/  glassrefraction/  growing/
   h3/  hz3/  ingredients/  inputdata/  l/  lights/  materials/  meshes/  models/  packaging/
   plants/  pots/  prefabs/  product/  properties/  seeds/  shaders/  skateboards/  stations/
   storage/  stylesheets/  textures/  tools/  u/  utilities/  vehicles/  weapons/
   ```
4. `prefabs/` contains only two entries, neither UI: `Prefabs/FogSubVolume`, `Prefabs/FogVolume2D`
   (`literals-sorted.txt:11151-11152`).
5. Grepping the 23,869 literals for `^ui/`, `Canvas/`, `/Canvas`, `transform.Find`, `GameObject.Find`,
   `HUD/`, `GameplayMenu` path strings → **0 hits**. The game never resolves UI by path at runtime.

### Every verified `Resources.Load`-style path relating to UI

The **only** UI-adjacent `Resources` tree is the input-prompt data (from
`strings-globalgamemanagers-sorted.txt`). These are real, loadable paths:

```
inputdata/promptbindings/pc/bindingdata_button_a … _z, _0 … _9
inputdata/promptbindings/pc/bindingdata_button_{alt,shift,ctr,caps,tab,space,enter,escape,backspace,del,
                                                insert,home,end,pgup,pgdn,prtsc,scrlk,numlk,pause,
                                                contextmenu,tilde,backslash,forwardslash,
                                                arrow_up,arrow_down,arrow_left,arrow_right}
inputdata/promptbindings/pc/bindingdata_button_f1 … f12
inputdata/promptbindings/pc/bindingdata_button_num_{0..9,.,+,minus,star,enter,forwardslash}
inputdata/promptbindings/pc/bindingdata_button_{',-,,,.,;,[,],=}
inputdata/promptbindings/pc/bindingdata_click_{leftmouse,middlemouse,rightmouse}
inputdata/promptbindings/xbox/bindingdata_xbox_button_{a,b,x,y,l1,l2,r1,r2,dpad_up,dpad_down,dpad_left,dpad_right}
inputdata/promptbindings/xbox/bindingdata_xbox_{leftstick_x,rightstick_horizontal,rightstick_press,
                                                rightstick_anticlockwise}
inputdata/promptbindings/playstation/bindingdata_playstation_button_{cross,circle,square,triangle,l1,l2,r1,r2,
                                                                     dpad_up,dpad_down,dpad_left,dpad_right}
inputdata/promptbindings/playstation/bindingdata_playstation_{leftstick_x,rightstick_horizontal,
                                                              rightstick_press,rightstick_anticlockwise}
inputdata/promptbindings/steamdeck/bindingdata_steam_button_{a,b,x,y,l1,l2,r1,r2,dpad_*}
inputdata/promptbindings/steamdeck/bindingdata_steam_{leftstick_x,rightstick_*}

inputdata/promptdata/back/promptdata_back
inputdata/promptdata/genericinteract/promptdata_interact
inputdata/promptdata/generictask/promptdata_generictask
inputdata/promptdata/hint/promptdata_hint
inputdata/promptdata/call/promptdata_call
inputdata/promptdata/clipboard/promptdata_clipboard
inputdata/promptdata/clipboard/promptdata_clipboardheatmap
inputdata/promptdata/clipboard/promptdata_clipboardinteract
inputdata/promptdata/clipboard/promptdata_clipboardselector
inputdata/promptdata/clipboard/descriptor_clipboardselectordone
inputdata/promptdata/changeamount/{promptdata_changeamount,descriptor_changeamount}
inputdata/promptdata/changequantity/{promptdata_changequantity,descriptor_changequantity}
inputdata/promptdata/consume/{promptdata_consumeproduct,descriptor_consume}
inputdata/promptdata/building/promptdata_building
inputdata/promptdata/buildingairconditioner/promptdata_buildingairconditioner
inputdata/promptdata/buildingtemperaturetoggle/promptdata_buildingtemperaturetoggle
inputdata/promptdata/brickpress/{promptdata_rotatebrickpresshandle,descriptor_rotaterightstick}
inputdata/promptdata/controlbunsenburner/{promptdata_controlbunsenburner,descriptor_controlbunsenburner}
inputdata/promptdata/driver/promptdata_driver
inputdata/promptdata/fillcontainer/{promptdata_fillcontainer,descriptor_fillcontainer}
inputdata/promptdata/graffiti/{promptdata_graffiti,descriptor_spray,descriptor_undo,descriptor_restart}
inputdata/promptdata/gun/{promptdata_gun,descriptor_aim,descriptor_cockfire,descriptor_reload}
inputdata/promptdata/harvestplant/{promptdata_harvestplant,descriptor_rotatepot}
inputdata/promptdata/harvestshroom/promptdata_harvestshroom
inputdata/promptdata/applyshroomspawn/{promptdata_applyshroomspawn,descriptor_breakchunks}
inputdata/promptdata/heldskateboard/{promptdata_heldskateboard,descriptor_mount}
inputdata/promptdata/menuexit/promptdata_menuback
inputdata/promptdata/debuginputprompt        inputdata/promptdata/debugdescriptor
inputdata/debugpromptbinding1 … 4            inputdata/debugpromptdata1 … 3
```

(The list above is truncated at the ~200-entry grep cap; regenerate with
`rg -a -N -o "^inputdata/[^ ]*" research/raw/strings-globalgamemanagers-sorted.txt | Sort-Object -Unique`.)

Two helper APIs worth knowing:

```csharp
// Il2CppScheduleOne.DevUtilities.AssetPathUtility : static
public static System.String GetResourcesPath(UnityEngine.Object selectedObject);

// Il2CppScheduleOne.DevUtilities.TransformUtilities : static
public static System.String GetScenePath(UnityEngine.Transform transform);   // <-- use this to LOG hierarchy
public static UnityEngine.Vector2 XZ(UnityEngine.Vector3 vector);
```

`TransformUtilities.GetScenePath` is the single most useful call for this project: it turns any live transform
into a readable scene path, so a one-shot Postfix that dumps the settings screen resolves every "inferred"
name below into a confirmed one — without shipping any path dependency.

### Practical asset rules for the mods

* **Never** ship a copy of the game's font/sprites. Clone a live GameObject and inherit them.
* If you need a custom icon, load it from your mod folder as a `Texture2D` →
  `UnityEngine.Sprite.Create(...)`, or use `S1API.PhoneApp.PhoneApp.SetIconTexture(UnityEngine.Texture2D)` /
  `SetIconSprite(UnityEngine.Sprite)` for phone apps. `S1API.AssetBundles` (3 types) exists if you decide to
  ship a bundle later.
* `Il2CppScheduleOne.DevUtilities.IconGenerator` (a `Singleton`) can render item icons at runtime
  (`GeneratePackagingIcon(string packagingID, string productID)`, `GetTexture(UnityEngine.Transform model)`)
  — useful if an expansion adds items that need icons.

---

## Confirmed vs inferred GameObject names

### Confirmed — real string literals in `global-metadata.dat` / `globalgamemanagers`

| String | Source (file:line) | What it provably is | What it *might* be (unverified) |
|---|---|---|---|
| `Assets/Scenes/Menu.unity` | `strings-globalgamemanagers-ordered.txt:1712` | scene asset path, build index 0 | — |
| `Assets/Scenes/Main.unity` | same:1713 | scene asset path, build index 1 | — |
| `Assets/Scenes/Tutorial.unity` | same:1714 | scene asset path, build index 2 | — |
| `Menu` | `literals-sorted.txt:9450` | a string literal | almost certainly `SaveManager.MENU_SCENE_NAME`'s value |
| `Menu scene loaded` | `literals-sorted.txt:9452` | log message | — |
| `Menu PopUp` | `literals-sorted.txt:9451` | a string literal | possibly the `MainMenuPopup` GameObject name |
| `Main Menu` | `literals-sorted.txt:9300` | a string literal | UI label or log |
| `Continue` | `literals-sorted.txt:4966` | a string literal | menu button label **or** `CallInterface.ContinuePrompt` text |
| `Options` | `literals-sorted.txt:10652` | a string literal | menu button label **or** an input-prompt label |
| `Back` | `literals-sorted.txt:3518` | a string literal | pairs with `inputdata/promptdata/back/promptdata_back` |
| `Cancel`, `Confirm` | `literals-sorted.txt:4073`, `:4869` | string literals | popup button labels |
| `On`, `Off` | `literals-sorted.txt:10509`, `:10479` | string literals | likely `UIToggle.ONTEXT` / `OFFTEXT` values |
| `#54E717`, `#4CBFFF`, `#4CB0FF`, `#FFC73D`, `#FF5555`, `#c0c0c0ff`, … | `literals-sorted.txt:2469-2495` etc. | palette hex codes in TMP rich-text tags | — |
| `inputdata/promptdata/**`, `inputdata/promptbindings/**` | `strings-globalgamemanagers-sorted.txt` | real `Resources` paths | — |
| `. Please ensure this screen is a child of a Canvas.` | `literals-sorted.txt:1865` | `UIScreen` validation message | — |
| `No Canvas found for ` | `literals-sorted.txt:10004` | validation message | — |
| `TooltipManager: AddCanvas called with null canvas` | `literals-sorted.txt:14342` | validation message | — |
| `2022.3.62f2` | `strings-globalgamemanagers-ordered.txt:1715` | Unity editor version | — |

**Explicitly NOT found as literals** (searched exact-line, case-sensitive and case-insensitive):
`New Game`, `Settings`, `Quit`, `Apply`, `Exit to Menu`, any `ui/` Resources path, any `Canvas/…` or
`.../Button` style path string, any font asset name, any sprite atlas name. These live in serialized prefab
data inside `level0` / `sharedassets0.assets`.

### Inferred — derived from serialized inspector field names (NOT confirmed GameObject names)

Every entry below is a **field name**, which in Unity usually (but not always) matches the referenced child
GameObject's name. Treat as a naming hint only; resolve at runtime with `TransformUtilities.GetScenePath`.

| Owner component | Serialized field (verified) | Field type (verified) | Inferred child object |
|---|---|---|---|
| `MenuScreen` | `Group` | `UnityEngine.CanvasGroup` | screen root has a CanvasGroup |
| `MenuScreen` | `rect` | `UnityEngine.RectTransform` | screen root RectTransform |
| `MenuScreen` | `uiScreen` / `uiPanel` | `UIScreen` / `UIPanel` | nav components on the screen root |
| `MenuScreen` | `State` | `Il2CppScheduleOne.State.MonoState` | a `MonoState` sibling |
| `SettingsScreen` | `Categories[i].Toggle` | `UnityEngine.UI.Toggle` | one category **tab button** per category |
| `SettingsScreen` | `Categories[i].Panel` | `UnityEngine.GameObject` | one category **page** per category |
| `SettingsScreen` | `ApplyDisplayButton` | `UnityEngine.UI.Button` | an "Apply" button on the display page |
| `SettingsScreen` | `ActiveDisplaySelection` | `UnityEngine.GameObject` | monitor-selection widget |
| `SettingsScreen` | `HostOnlyGameObjects` | `GameObject[]` | rows hidden for non-host clients |
| `SettingsScreen` | `Tab` | `Il2CppScheduleOne.UITab` | gamepad tab-cycler on the category strip |
| `SettingsSlider` | `slider`, `valueLabel` | `UnityEngine.UI.Slider`, `TextMeshProUGUI` | slider + numeric readout in a row |
| `SettingsToggle` | `uiToggle` | `Il2CppScheduleOne.UIToggle` | the ON/OFF button in a row |
| `SettingsDropdown` | `_popupSelector`, `_dropdown` | `UIPopupSelector`, `TMP_Dropdown` | the selector widget in a row |
| `UIToggle` | `buttonText`, `toggleImage` | `TextMeshProUGUI`, `UnityEngine.UI.Image` | label + background of a toggle |
| `ButtonScaler` | `ScaleTarget` | `UnityEngine.RectTransform` | the thing that grows on hover |
| `PauseMenu` | `Canvas`, `Container`, `Screen`, `CartelNameLabel` | `Canvas`, `RectTransform`, `MenuScreen`, `TextMeshProUGUI` | pause menu root + button container |
| `GameplayMenuInterface` | `PhoneButton`, `CharacterButton`, `SelectionIndicator` | `Button`, `Button`, `RectTransform` | the two in-game tabs + moving underline |
| `HomeScreen` | `appIconContainer`, `appIconPrefab`, `timeText` | `RectTransform`, `GameObject`, `UnityEngine.UI.Text` | phone home grid + icon prefab |
| `App<T>` | `appContainer`, `notificationContainer`, `notificationText`, `appIconButton` | `RectTransform`, `RectTransform`, `UnityEngine.UI.Text`, `Button` | app page + unread badge |
| `TabItemUI` | `_button`, `_label`, `_content`, `_indicator`, `_indicatorLabel`, `_contentPanel` | `ButtonUI`, `Text`, `GameObject`, `GameObject`, `Text`, `UIPanel` | one tab + its page |
| `TabController` | `_tabIndicator`, `_tabItems` | `RectTransform`, `List<TabItemUI>` | moving tab underline |
| `SetupScreen` | `InputField`, `StartButton`, `SkipIntroContainer`, `SkipIntroToggle`, `NotHostWarning` | `TMP_InputField`, `Button`, `RectTransform`, `Toggle`, `RectTransform` | new-game setup form |
| `ImportScreen` | `MainContainer`, `FailContainer`, `ConfirmButton`, `OrganisationNameLabel`, `NetworthLabel`, `VersionLabel`, `WarningLabel` | — | save-import form |
| `SaveDisplay` | `Slots` | `RectTransform[]` | one row per save slot |
| `Disclaimer` | `Group`, `TextGroup` | `CanvasGroup`, `CanvasGroup` | splash fade groups |
| `MainMenuPopup` | `Screen`, `Title`, `Description` | `MenuScreen`, `TextMeshProUGUI`, `TextMeshProUGUI` | popup dialog |
| `MainMenuRig` | `Avatar`, `DefaultSettings`, `CashPiles` | `Avatar`, `BasicAvatarSettings`, `CashPile[]` | the menu-background diorama |
| `LobbyInterface` | `Container`, `LobbyTitle`, `PlayerSlots`, `InviteButton`, `LeaveButton`, `InviteHint`, `Panel` | — | multiplayer lobby strip |
| `TooltipManager` | `Canvas`, `anchor`, `tooltipLabel` | `Canvas`, `RectTransform`, `TextMeshProUGUI` | tooltip popup |
| `MouseTooltip` | `IconRect`, `IconImg`, `TooltipRect`, `TooltipLabel` | — | cursor tooltip |
| `InputPromptsUI` | `_promptsCenterContainer`, `_promptsBottomLeftInGameContainer`, `_promptsBottomLeftMenuContainer`, `_promptsCustomContainer` | `Transform` ×4 | prompt strip anchors |
| `InputPromptsManager` | `KeyPromptPrefab`, `WideKeyPromptPrefab`, `ExtraWideKeyPromptPrefab`, `LeftClickPromptPrefab`, `MiddleClickPromptPrefab`, `RightClickPromptPrefab` | `GameObject` ×6 | key-glyph prefabs |
| `HUD` | 38 fields, e.g. `crosshair`, `HotbarContainer`, `SlotContainer`, `discardSlot`, `topScreenText` | — | HUD layout |

---

## Recommended injection strategy

### The plan

1. **Anchor:** Harmony `Postfix` on `Il2CppScheduleOne.UI.MainMenu.SettingsScreen.Awake()`.
   Runs once per `SettingsScreen` instance, before `OnEnable` (Unity order `Awake → OnEnable → Start`), which
   is exactly when `Categories` is fully deserialized but not yet wired.
2. **Discover templates by component type, never by path.** Use the last existing
   `SettingsCategory` as tab+panel template, and `GetComponentsInChildren<SettingsToggle>(true)` /
   `<SettingsSlider>(true)` from the whole screen as row templates.
3. **Build an inactive panel, clone rows into it, then append.** Because Unity does not run
   `Awake`/`OnEnable` for children of an inactive GameObject, cloning into an inactive panel avoids the
   template row's own `Start`/`OnEnable` touching the real `Settings` singleton.
4. **Append one `SettingsCategory` to `Categories`** (reallocate the `Il2CppReferenceArray`).
   The game's `OnEnable` then wires your `Toggle` → `ShowCategory(yourIndex)`.
5. **Also wire the toggle yourself**, defensively. If the game already wired it, the duplicate listener just
   calls `ShowCategory` twice — idempotent and harmless. If it did not (e.g. `_initialized` was already true
   because the screen was re-enabled), yours is the one that works.
6. **Persist with `MelonPreferences`**, not the game's settings file (see §2).
7. **Log the real hierarchy once** with `TransformUtilities.GetScenePath` so the team can promote the
   "inferred" table above into confirmed names.

### Code sketch

```csharp
using System;
using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppTMPro;

using S1SettingsScreen   = Il2CppScheduleOne.UI.MainMenu.SettingsScreen;
using S1SettingsCategory = Il2CppScheduleOne.UI.MainMenu.SettingsScreen.SettingsCategory;
using S1SettingsToggle   = Il2CppScheduleOne.UI.Settings.SettingsToggle;
using S1SettingsSlider   = Il2CppScheduleOne.UI.Settings.SettingsSlider;
using S1UIToggle         = Il2CppScheduleOne.UIToggle;
using S1UISelectable     = Il2CppScheduleOne.UISelectable;
using TransformUtilities = Il2CppScheduleOne.DevUtilities.TransformUtilities;

internal static class ExpansionsCategory
{
    // Il2CppInterop trampolines are keyed off the managed delegate; keep every delegate rooted
    // for the lifetime of the process or the native side can call into collected memory.
    private static readonly List<object> KeepAlive = new List<object>();

    [HarmonyPatch(typeof(S1SettingsScreen), "Awake")]
    private static class Patch
    {
        private static void Postfix(S1SettingsScreen __instance)
        {
            try { Inject(__instance); }
            catch (Exception e) { MelonLogger.Error($"[Expansions] settings injection failed: {e}"); }
        }
    }

    private static void Inject(S1SettingsScreen screen)
    {
        var cats = screen.Categories;
        if (cats == null || cats.Length == 0) return;

        // One-shot recon: promotes every "inferred" name in the docs into a confirmed one.
        MelonLogger.Msg($"[Expansions] SettingsScreen at {TransformUtilities.GetScenePath(screen.transform)} " +
                        $"scene='{screen.gameObject.scene.name}' categories={cats.Length}");

        var template = cats[cats.Length - 1];
        if (template == null || template.Toggle == null || template.Panel == null) return;

        // Row templates must be captured BEFORE we clear the cloned panel.
        var toggleRowTemplate = screen.GetComponentsInChildren<S1SettingsToggle>(true);
        var sliderRowTemplate = screen.GetComponentsInChildren<S1SettingsSlider>(true);

        // ---- 1. the tab button -------------------------------------------------
        // Cloning keeps the ToggleGroup reference (the group lives on the parent, so the clone
        // still points at the original) => radio behaviour and the UISelectable gamepad
        // registration both come along for free.
        var tabGo = UnityEngine.Object.Instantiate(
            template.Toggle.gameObject, template.Toggle.transform.parent, false);
        tabGo.name = "Category_Expansions";
        var tabToggle = tabGo.GetComponent<Toggle>();
        tabToggle.isOn = false;
        // Drop any inspector-wired click that came with the clone.
        tabToggle.onValueChanged.m_PersistentCalls.Clear();
        tabToggle.onValueChanged.DirtyPersistentCalls();
        tabToggle.onValueChanged.RemoveAllListeners();
        SetLabel(tabGo, "EXPANSIONS");

        // ---- 2. the page ------------------------------------------------------
        var panel = UnityEngine.Object.Instantiate(
            template.Panel, template.Panel.transform.parent, false);
        panel.name = "Panel_Expansions";
        panel.SetActive(false);          // children get no Awake/OnEnable while inactive

        // Keep the page chrome (header/scroll view), drop the vanilla rows.
        // NOTE: which child index is the header is prefab data -> confirm from the log above
        //       before shipping; until then, wipe everything and add our own rows.
        var content = panel.transform;
        for (int i = content.childCount - 1; i >= 0; i--)
            UnityEngine.Object.DestroyImmediate(content.GetChild(i).gameObject);

        if (toggleRowTemplate != null && toggleRowTemplate.Length > 0)
        {
            AddToggleRow(content, toggleRowTemplate[0], "Hireable Drivers",     "Expansions", "drivers");
            AddToggleRow(content, toggleRowTemplate[0], "Police Improvements",  "Expansions", "police");
            AddToggleRow(content, toggleRowTemplate[0], "Special Customers",    "Expansions", "customers");
        }

        // ---- 3. register ------------------------------------------------------
        var cat = new S1SettingsCategory { Toggle = tabToggle, Panel = panel };

        var grown = new Il2CppReferenceArray<S1SettingsCategory>(cats.Length + 1);
        for (int i = 0; i < cats.Length; i++) grown[i] = cats[i];
        int myIndex = cats.Length;
        grown[myIndex] = cat;
        screen.Categories = grown;

        // Defensive wiring: harmless duplicate if SettingsScreen.OnEnable also wires it.
        Action<bool> onTab = on => { if (on) screen.ShowCategory(myIndex); };
        KeepAlive.Add(onTab);
        tabToggle.onValueChanged.AddListener(onTab);   // System.Action<bool> -> UnityAction<bool> via op_Implicit

        MelonLogger.Msg($"[Expansions] registered settings category at index {myIndex}");
    }

    private static void AddToggleRow(Transform parent, S1SettingsToggle template,
                                     string label, string prefCategory, string prefKey)
    {
        var row = UnityEngine.Object.Instantiate(template.gameObject, parent, false);
        row.name = "Row_" + prefKey;

        // Detach the game's setting logic but keep the visuals + the generic UIToggle.
        // Destroy through the base-type reference removes the real subclass instance
        // (VSyncToggle / SSAOToggle / ...), whichever it happens to be.
        var stock = row.GetComponent<S1SettingsToggle>();
        if (stock != null) UnityEngine.Object.DestroyImmediate(stock);

        SetLabel(row, label);

        var uiToggle = row.GetComponentInChildren<S1UIToggle>(true);
        if (uiToggle == null) return;

        var pref = MelonPreferences.GetCategory(prefCategory) ??
                   MelonPreferences.CreateCategory(prefCategory);
        var entry = pref.GetEntry<bool>(prefKey) ?? pref.CreateEntry(prefKey, true);

        uiToggle.SetStateWithoutNotify(entry.Value);

        Action<bool> onChanged = v => { entry.Value = v; MelonPreferences.Save(); };
        KeepAlive.Add(onChanged);
        uiToggle.OnChanged.AddListener(onChanged);   // UnityEvent<System.Boolean>
    }

    // The codebase mixes TMPro and legacy uGUI text — always handle both.
    private static void SetLabel(GameObject go, string text)
    {
        var tmp = go.GetComponentInChildren<TextMeshProUGUI>(true);
        if (tmp != null) { tmp.text = text; return; }
        var legacy = go.GetComponentInChildren<Text>(true);
        if (legacy != null) legacy.text = text;
    }
}
```

Register the patch from your `MelonMod`:

```csharp
public override void OnInitializeMelon()
{
    HarmonyInstance.PatchAll(typeof(ExpansionsCategory).Assembly);
}
```

Or, if you would rather not depend on `nameof`/type resolution at compile time, Harmony-by-string against the
**original** (unprefixed) IL2CPP names:

```csharp
// Il2CppInterop-generated managed type, resolved by its Il2Cpp-prefixed name:
var t = typeof(Il2CppScheduleOne.UI.MainMenu.SettingsScreen);
// Original name recorded in metadata (use for logging / cross-version diffing):
//   ScheduleOne.UI.MainMenu.SettingsScreen   (source: \Assets\Scripts\UI\Settings\SettingsScreen.cs)
HarmonyInstance.Patch(
    AccessTools.Method(t, "Awake"),
    postfix: new HarmonyMethod(typeof(ExpansionsCategory), nameof(Postfix)));
```

### Wiring `Button.onClick` from IL2CPP — the correct signature

The brief said `Button.onClick.AddListener` needs an `Il2CppSystem.Action`. On this build it actually takes
**`UnityEngine.Events.UnityAction`**:

```csharp
// UnityEngine.UI.Button : UnityEngine.UI.Selectable
UnityEngine.UI.Button.ButtonClickedEvent m_OnClick { get; set; }
UnityEngine.UI.Button.ButtonClickedEvent onClick   { get; set; }
public System.Void Press();
//   Button+ButtonClickedEvent : UnityEngine.Events.UnityEvent

// UnityEngine.Events.UnityEvent : UnityEventBase
public System.Void AddListener(UnityEngine.Events.UnityAction call);
public System.Void RemoveListener(UnityEngine.Events.UnityAction call);
public System.Void Invoke();

// UnityEngine.Events.UnityEvent`1<T0> : UnityEventBase
public System.Void AddListener(UnityEngine.Events.UnityAction<T0> call);

// UnityEngine.Events.UnityAction : Il2CppSystem.MulticastDelegate
public static UnityEngine.Events.UnityAction op_Implicit(System.Action  = null);
// UnityEngine.Events.UnityAction`1<T0> : Il2CppSystem.MulticastDelegate
public static UnityEngine.Events.UnityAction<T0> op_Implicit(System.Action<T0>  = null);
```

`Il2CppSystem.Action` also has `op_Implicit(System.Action)` and is what the *game's own* callback fields use
(`UIPopupScreen_ContextMenu.AddOption(int, string, Il2CppSystem.Action)`,
`GameInput.RegisterExitListener(Il2CppSystem.Action, Il2CppSystem.Func<bool>, int, bool)`,
`Settings.onDisplaySettingsApplied`, `App<T>` / `Phone.onPhoneOpened`).

So there are three shapes to know:

```csharp
// 1. uGUI events -> UnityAction (implicit from System.Action)
System.Action click = OnClicked;
myButton.onClick.AddListener(click);
// A method group will NOT convert: myButton.onClick.AddListener(OnClicked) does not compile.
// Cast or assign to System.Action first.

// 2. Game callback fields -> Il2CppSystem.Action (implicit from System.Action)
Il2CppScheduleOne.DevUtilities.Settings.Instance.onDisplaySettingsApplied += (System.Action)OnDisplayApplied;

// 3. Fully explicit, when overload resolution gets confused:
using Il2CppInterop.Runtime;
myButton.onClick.AddListener(
    DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction>(new System.Action(OnClicked)));
```

Stripping an inherited inspector-wired click on a cloned button — all four members are public on this build:

```csharp
clone.onClick.m_PersistentCalls.Clear();            // PersistentCallGroup.Clear()
clone.onClick.DirtyPersistentCalls();              // UnityEventBase.DirtyPersistentCalls()
clone.onClick.RemoveAllListeners();                // UnityEventBase.RemoveAllListeners()
// Non-destructive alternative, per persistent call:
for (int i = 0; i < clone.onClick.GetPersistentEventCount(); i++)
    clone.onClick.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.Off);
```

### Alternative #2 — new top-level main-menu button (code sketch)

```csharp
[HarmonyPatch(typeof(Il2CppScheduleOne.UI.MainMenu.MenuScreen), "Start")]
private static class RootMenuPatch
{
    private static void Postfix(Il2CppScheduleOne.UI.MainMenu.MenuScreen __instance)
    {
        // OpenOnStart identifies the root menu screen without any path string.
        if (!__instance.OpenOnStart) return;

        var buttons = __instance.GetComponentsInChildren<UnityEngine.UI.Button>(true);
        if (buttons == null || buttons.Length == 0) return;

        // Pick the button whose parent has the most Button siblings -> that's the button column.
        UnityEngine.UI.Button template = buttons[0];
        int best = -1;
        foreach (var b in buttons)
        {
            int siblings = b.transform.parent.GetComponentsInChildren<UnityEngine.UI.Button>(true).Length;
            if (siblings > best) { best = siblings; template = b; }
        }

        var go = UnityEngine.Object.Instantiate(template.gameObject, template.transform.parent, false);
        go.name = "Button_Expansions";
        go.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + 1);

        var btn = go.GetComponent<UnityEngine.UI.Button>();
        btn.onClick.m_PersistentCalls.Clear();
        btn.onClick.DirtyPersistentCalls();
        btn.onClick.RemoveAllListeners();

        System.Action open = () => MyExpansionsScreen.Open();
        KeepAlive.Add(open);
        btn.onClick.AddListener(open);
        SetLabel(go, "EXPANSIONS");
    }
}
```

For the panel itself, clone the whole `SettingsScreen` GameObject (it is a `MenuScreen`, so `Open()`/`Close()`
work unchanged), strip its `SettingsScreen` component and children, and set `PreviousScreen` to the root menu
so `Exit`/Esc goes back correctly.

### Risks

| Risk | Severity | Mitigation |
|---|---|---|
| `Categories` array shape changes in a patch (array → `List<>`) | medium | Fails at compile time against regenerated `Il2CppAssemblies`; keep the append in one small method. |
| Cloning the last category's `Panel` also clones its content, and the child index of the page "header" is prefab data | medium | The sketch wipes all children and rebuilds; log `GetScenePath` on first run, then keep the header deliberately. |
| Cloned row's `SettingsToggle` subclass `OnEnable`/`Start` writes to real settings before we destroy it | low | Clone into an **inactive** panel; children of an inactive GameObject never get `Awake`/`OnEnable`. |
| `_initialized` guard means `OnEnable` may not re-wire after our append | low | We wire the toggle ourselves too; duplicate `ShowCategory` calls are idempotent. |
| Managed delegate GC'd → native call into freed memory | **high if ignored** | Root every delegate in a static list (`KeepAlive` in the sketch). Classic Il2CppInterop footgun. |
| `SettingsScreen` exists in `Main` too, so the Postfix fires twice per session | low | Fine — inject per instance; do **not** use a global `bool _done` guard, or the second surface silently loses the tab. |
| `Il2CppScheduleOne.UI.CanvasScaler` vs `UnityEngine.UI.CanvasScaler` name clash | low | Always fully qualify. |
| `field_Private_*` / `Method_*_PDM_*` fallback names shift between versions | medium | Never referenced by the sketch. Keep it that way. |
| Multiplayer: expansion toggles must agree between host and clients | medium | Mirror `ConfigurationServiceNetworker.ApplySettingsJson` (host→client replication) for anything gameplay-affecting; a purely local cosmetic toggle is fine. |

---

## Open questions / unverified

Ordered by how much they affect the plan.

1. **The number and order of existing settings categories, and the label text of each.** `Categories` is an
   array of unknown length in prefab data. The sketch handles this (`cats.Length`) but you cannot know
   whether "Expansions" will land 5th or 7th until you log it.
2. **Which child of a category `Panel` is chrome (header / `ScrollRect` viewport) vs a settings row.** This is
   the one genuinely fragile step. Resolve with a single `GetScenePath` dump of
   `template.Panel.transform` children on first run, then hard-code the header index — or better, keep only
   children that have no `SettingsToggle`/`SettingsSlider`/`SettingsDropdown`/`Keybinder` component.
3. **Whether `SettingsScreen` really is instantiated in the `Main` (gameplay) scene.** Inferred with high
   confidence from `HostOnlyGameObjects`, but not proven. One log line of
   `screen.gameObject.scene.name` in the Postfix settles it.
4. **Whether `SettingsScreen.OnEnable` auto-wires appended categories.** Inferred from
   `SettingsScreen+<>c__DisplayClass8_0 { int index; SettingsScreen __4__this; void _OnEnable_b__0(bool on) }`
   plus the `_initialized` flag. The sketch does not depend on it.
5. **All main-menu GameObject names** — `Continue`/`New Game`/`Settings`/`Quit` button object names, the
   button column's container name, the root menu screen's name. Zero literal evidence; they are TMP `m_text`
   and object names inside `level0`.
6. **Font asset names and sprite/atlas names.** Not present in `global-metadata.dat` at all. Enumerate with
   `Resources.FindObjectsOfTypeAll<Il2CppTMPro.TMP_FontAsset>()` / `<UnityEngine.Sprite>()` at runtime if you
   ever need them by name. The clone-based recipe does not.
7. **`ColorFont` palette entry names** — `GetColour(System.String name)` takes a name, but the names are
   `ColorFontItem.Name` asset data. Enumerate `ColorFontItems` at runtime.
   Same for `SpriteFont.GetSprite(System.String name)`.
8. **`UIToggle.ONTEXT` / `OFFTEXT` values.** Static strings; the literals `On` (:10509) and `Off` (:10479)
   exist and are the obvious candidates, but the association is unproven. Read the statics at runtime.
9. **Where `Il2CppScheduleOne.DevUtilities.Settings` writes to disk.** No path literal, no `PlayerPrefs`
   surface. Do not depend on it; use `MelonPreferences`.
10. **``Il2CppScheduleOne.UI.App`1<T>.Orientation``** is reported by the dumper as type `App<T>` — that is a
    dumper artefact for a nested-enum-typed field. The real type is almost certainly
    ``App`1+EOrientation { Horizontal = 0, Vertical = 1 }``. Same pattern in
    ``ReplaceableSettingsList`1.Mode`` (reported as `ReplaceableSettingsList<T>`, real type
    ``ReplaceableSettingsList`1+EMode { Replace = 0, Add = 1 }``).
11. **Whether HarmonyX can reliably patch `virtual` overrides of Unity messages (`Awake`) on IL2CPP types in
    this MelonLoader 0.7.1 build.** S1API proves `Awake`/`Start` patching works in general
    (`HomeScreen.Start`, `ContactsApp.Start`, `Customer.Awake`, `NPCHealth.Awake`, `CommandListScreen.Start`),
    but every one of those is a *non-virtual* or *base* method. `SettingsScreen.Awake` is
    `public virtual` overriding `MenuScreen.Awake`. Expected to be fine (each override is its own native
    method), but it is the one thing in the plan I could not confirm from metadata. Fallback if it misbehaves:
    patch `SettingsScreen.OnEnable` (non-virtual, `public System.Void OnEnable()`) and guard with your own
    "already injected" check per instance.
12. **The `.resS`-less `level0`** (5.4 MB) means the menu scene's textures come from
    `sharedassets0.assets` (100 MB). Irrelevant if you clone, relevant if you ever try to extract assets.
13. **uGUI/TMP stripping.** I found **no** evidence of stripped uGUI/TMP members on this build (all 67
    `UnityEngine.UI` + 128 `Il2CppTMPro` public types are present, and the game itself uses
    `TMP_InputField`, `TMP_Dropdown`, `Slider`, `Toggle`, `ScrollRect`, `Image`). But absence of evidence in
    metadata is not proof the *native* implementation is intact — the legacy-IMGUI failures documented in
    `CreativeModeMod.cs` (`GUILayout.TextField`, `GUI.DrawTexture`) were runtime, not metadata, failures.
    Verify the first cloned `TMP_InputField` actually accepts keystrokes before designing around it.

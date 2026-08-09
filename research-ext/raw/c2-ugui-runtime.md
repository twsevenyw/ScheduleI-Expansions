## C2 — Runtime uGUI + TextMeshPro under MelonLoader / Il2CppInterop (Schedule I)

**Scope:** how to build a settings/toggle screen at runtime that looks native, from a MelonLoader IL2CPP mod, with zero shipped assets if possible.

**Bottom line up front:** *clone a real menu element, don't build from scratch.* Build-from-scratch is fully documented below and works, but cloning inherits the game's exact font asset, material preset, 9-slice sprites, hover/press colour blocks, layout metrics and (usually) its click/hover audio for free. Build from scratch only for containers the game has no analogue for.

---

### 0. Verification method + environment (read this before trusting anything below)

**[verified]** Everything in this file marked `[verified]` was checked against the actual install on this machine:

| Fact | Value | How verified |
|---|---|---|
| Game | `C:\Program Files (x86)\Steam\steamapps\common\Schedule I` | filesystem |
| Unity | 2022.3.62f2, IL2CPP | given + consistent with interop assemblies |
| MelonLoader | **0.7.1.0** (`MelonLoader\net6\MelonLoader.dll`) | file version |
| Il2CppInterop | **1.5.0.0** (`Il2CppInterop.Runtime.dll`, `.Common`, `.Generator`) | file version |
| HarmonyLib | **2.10.2.0** (`net6\0Harmony.dll`) | file version |
| ML AssetBundle shim | `net6\UnityEngine.Il2CppAssetBundleManager.dll` 0.7.1.0 | present |
| ML ImageConversion shim | `net6\UnityEngine.Il2CppImageConversionManager.dll` 0.7.1.0 | present |
| uGUI | `Il2CppAssemblies\UnityEngine.UI.dll` (800 KB) | present |
| TMP | `Il2CppAssemblies\Unity.TextMeshPro.dll` (965 KB), namespace **`Il2CppTMPro`** | present, `Il2CppTMPro` string in metadata |
| New Input System | `Unity.InputSystem.dll` (contains `InputSystemUIInputModule`) **and** `UnityEngine.InputLegacyModule.dll` | present |
| Canvas type lives in | `UnityEngine.UIModule.dll` (not CoreModule) | metadata probe |

**[verified] Compile validation.** I built a throw-away `net6.0` project referencing `MelonLoader\net6\*.dll` + every DLL in `MelonLoader\Il2CppAssemblies\` and compiled ~1000 lines covering every snippet in this document — including a second pass that compiles the *final* text of the longer snippets verbatim. Final state: **0 errors, 0 warnings** (warnings-as-visible, `NoWarn` cleared). Where a snippet initially failed to compile, the failure is documented inline as a gotcha (there were four; all are called out). Snippets below are copy-paste ready modulo variable names.

One boilerplate note: several snippets call `Object.Instantiate` / `Object.DestroyImmediate` / `Object.DontDestroyOnLoad`. Because `System.Object` is also in scope, either add `using Object = UnityEngine.Object;` at the top of the file or fully qualify as `UnityEngine.Object.…`. Both forms are used below and both compile.

`.csproj` used (mirror this in the real mod):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net6.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <GameDir>C:\Program Files (x86)\Steam\steamapps\common\Schedule I</GameDir>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="$(GameDir)\MelonLoader\net6\MelonLoader.dll" Private="false" />
    <Reference Include="$(GameDir)\MelonLoader\net6\Il2CppInterop.Runtime.dll" Private="false" />
    <Reference Include="$(GameDir)\MelonLoader\net6\Il2CppInterop.Common.dll" Private="false" />
    <Reference Include="$(GameDir)\MelonLoader\net6\0Harmony.dll" Private="false" />
    <Reference Include="$(GameDir)\MelonLoader\net6\UnityEngine.Il2CppAssetBundleManager.dll" Private="false" />
    <Reference Include="$(GameDir)\MelonLoader\net6\UnityEngine.Il2CppImageConversionManager.dll" Private="false" />
    <Reference Include="$(GameDir)\MelonLoader\Il2CppAssemblies\*.dll" Private="false" />
  </ItemGroup>
</Project>
```

The wildcard `Reference Include` works and pulls all ~130 interop assemblies. `Private="false"` keeps them out of your output folder.

---

### 1. Runtime uGUI construction under Il2CppInterop

#### 1.1 The interop cheat-sheet you actually need

**[verified]** Exact return types, established by forcing deliberate `CS0029` conversion errors and reading what the compiler said:

| Call | Actual return type |
|---|---|
| `Resources.FindObjectsOfTypeAll<T>()` | `Il2CppArrayBase<T>` |
| `Resources.FindObjectsOfTypeAll(Il2CppType.Of<T>())` | `Il2CppReferenceArray<UnityEngine.Object>` |
| `Resources.LoadAll<T>(path)` | `Il2CppArrayBase<T>` |
| `go.GetComponentsInChildren<T>(bool)` | `Il2CppArrayBase<T>` |
| `go.GetComponents<T>()` | `Il2CppArrayBase<T>` |
| `Object.FindObjectsOfType<T>()` | `Il2CppArrayBase<T>` |
| `go.AddComponent(Il2CppType.Of<T>())` | `UnityEngine.Component` (must `.Cast<T>()` / `.TryCast<T>()`) |
| `tex.GetPixels()` | `Il2CppStructArray<Color>` |
| `bundle.GetAllAssetNames()` | `Il2CppStringArray` |
| `bundle.LoadAllAssets<T>()` | `Il2CppReferenceArray<T>` |

**The trap:** `Il2CppReferenceArray<T>` *derives from* `Il2CppArrayBase<T>`, so `Il2CppArrayBase<T> x = ...ReferenceArray...` compiles but the reverse does not. Declare locals as `Il2CppArrayBase<T>` (or just `var`) and you will never hit `CS0266`. All of these are `foreach`-able and have `.Length` and an indexer.

Other essentials:

```csharp
using Il2CppInterop.Runtime;                              // Il2CppType, DelegateSupport
using Il2CppInterop.Runtime.Attributes;                   // HideFromIl2Cpp
using Il2CppInterop.Runtime.Injection;                    // ClassInjector, RegisterTypeOptions
using Il2CppInterop.Runtime.InteropTypes.Arrays;          // Il2CppArrayBase/ReferenceArray/StructArray/StringArray
using Il2CppTMPro;                                        // TMP is namespace-prefixed; UnityEngine.* is NOT
using MelonLoader;
using UnityEngine;
using UnityEngine.Events;                                 // UnityAction
using UnityEngine.EventSystems;
using UnityEngine.UI;
```

**[verified]** `UnityEngine`, `UnityEngine.UI`, `UnityEngine.Events`, `UnityEngine.EventSystems`, `UnityEngine.InputSystem` are **not** prefixed. Only non-Unity-shipped assemblies get the `Il2Cpp` prefix, and `Unity.TextMeshPro` is (inconsistently) treated as one — hence `Il2CppTMPro`. This is a known MelonLoader quirk, tracked as [LavaGang/MelonLoader#912](https://github.com/LavaGang/MelonLoader/issues/912). Every shipped Schedule I mod I inspected uses `using Il2CppTMPro;` (HonestMainMenu, MultiplayerPlus, SimpleLabels).

**[verified] `HarmonyLib.Harmony` must be fully qualified.** `new Harmony("id")` fails with `CS0118: 'Harmony' is a namespace but is used like a type`, because a `Harmony` namespace exists in the interop assembly set. Use `new HarmonyLib.Harmony("id")` or just `HarmonyInstance` from `MelonMod`.

#### 1.2 Canvas + CanvasScaler + GraphicRaycaster from scratch

```csharp
using UnityEngine;
using UnityEngine.UI;

public static Canvas BuildCanvas()
{
    GameObject root = new GameObject("MyMod_Canvas");       // [verified] plain `new GameObject(...)` works
    Object.DontDestroyOnLoad(root);
    root.hideFlags = HideFlags.HideAndDontSave;             // survives scene unload, not written to any scene
    root.layer = 5;                                         // LayerMask "UI"

    Canvas canvas = root.AddComponent<Canvas>();
    canvas.renderMode = RenderMode.ScreenSpaceOverlay;      // draws after everything, ignores cameras
    canvas.sortingOrder = 30000;                            // above every in-game canvas
    canvas.overrideSorting = true;
    canvas.referencePixelsPerUnit = 100f;
    canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1
                                    | AdditionalCanvasShaderChannels.Normal
                                    | AdditionalCanvasShaderChannels.Tangent;  // TMP needs these for SDF effects

    CanvasScaler scaler = root.AddComponent<CanvasScaler>();
    scaler.uiScaleMode          = CanvasScaler.ScaleMode.ScaleWithScreenSize;
    scaler.referenceResolution  = new Vector2(1920f, 1080f);
    scaler.screenMatchMode      = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
    scaler.matchWidthOrHeight   = 0.5f;
    scaler.referencePixelsPerUnit = 100f;

    GraphicRaycaster gr = root.AddComponent<GraphicRaycaster>();
    gr.ignoreReversedGraphics = true;
    gr.blockingObjects = GraphicRaycaster.BlockingObjects.None;
    return canvas;
}
```

**[verified] compiles.** Notes:

- **`sortingOrder`**: `ScreenSpaceOverlay` canvases sort by `sortingOrder`, then by hierarchy order. `30000` is what UnityExplorer/UniverseLib uses as `TOP_SORTORDER` ([UIBase.cs](https://github.com/sinai-dev/UniverseLib/blob/main/src/UI/UIBase.cs)) and is a safe "above everything" value. **[community]** Set `overrideSorting = true` as well; if the mod canvas is ever reparented under a game canvas, sortingOrder is otherwise ignored.
- **`additionalShaderChannels`**: **[inference, high confidence]** if you build a canvas from scratch and put TMP text on it, TMP's SDF shaders read `TEXCOORD1`/`NORMAL`/`TANGENT` for outline/underlay/bevel. Unity's own "Create > UI > Text - TextMeshPro" adds these channels to the canvas automatically; a hand-built canvas does not. Symptom if you forget: text renders but outlines/soft-shadow presets look wrong. Costs nothing to set.
- **`RenderMode`**: use `ScreenSpaceOverlay`. `ScreenSpaceCamera` requires you to find and hold the game's UI camera, and inherits its post-processing.

#### 1.3 Reading the game's own `CanvasScaler` so your UI scales identically

**[verified] compiles.** This is the "match the game's canvas" answer: don't hardcode 1920×1080, copy it.

```csharp
using UnityEngine;
using UnityEngine.UI;

/// Finds the game's primary screen-space canvas and mirrors its scaler onto ours.
public static void CopyScalerFromGame(CanvasScaler mine)
{
    Canvas best = null;
    int bestScore = -1;

    foreach (Canvas c in Resources.FindObjectsOfTypeAll<Canvas>())
    {
        if (c == null || !c.isRootCanvas) continue;
        if (c.renderMode != RenderMode.ScreenSpaceOverlay &&
            c.renderMode != RenderMode.ScreenSpaceCamera) continue;

        CanvasScaler cs = c.GetComponent<CanvasScaler>();
        if (cs == null) continue;

        // Prefer live, enabled, scene-resident canvases over prefabs/assets.
        int score = 0;
        if (c.gameObject.scene.IsValid())            score += 8;
        if (c.isActiveAndEnabled)                    score += 4;
        if (cs.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize) score += 2;
        score += Mathf.Min(c.transform.childCount, 1);   // has content

        if (score > bestScore) { bestScore = score; best = c; }
    }

    if (best == null) return;

    CanvasScaler src = best.GetComponent<CanvasScaler>();
    mine.uiScaleMode            = src.uiScaleMode;
    mine.referenceResolution    = src.referenceResolution;   // <-- the number you wanted
    mine.screenMatchMode        = src.screenMatchMode;
    mine.matchWidthOrHeight     = src.matchWidthOrHeight;
    mine.referencePixelsPerUnit = src.referencePixelsPerUnit;
    mine.scaleFactor            = src.scaleFactor;

    mine.GetComponent<Canvas>().sortingOrder = best.sortingOrder + 100;
    MelonLogger.Msg($"Matched game canvas '{best.name}': ref={src.referenceResolution} " +
                    $"mode={src.uiScaleMode} match={src.matchWidthOrHeight} ppu={src.referencePixelsPerUnit}");
}
```

Run this once after the main-menu or gameplay scene has initialised (`OnSceneWasInitialized`) and **log the result** — the printed `referenceResolution` is the single most useful number for authoring your card metrics. `Resources.FindObjectsOfTypeAll` returns inactive objects and assets too, hence the scoring. See [Resources.FindObjectsOfTypeAll](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Resources.FindObjectsOfTypeAll.html).

#### 1.4 GameObject creation, AddComponent, RectTransform

**[verified]** `new GameObject("x")` works under Il2CppInterop — the interop wrapper exposes the ctor and it allocates a real IL2CPP object.
**[verified]** Generic `AddComponent<T>()` works for any type that exists in IL2CPP (i.e. all Unity types and all game types). You only need `Il2CppType.Of<T>()` for the non-generic overload or reflection-ish paths.

```csharp
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.UI;

public static GameObject NewUIObject(string name, Transform parent)
{
    GameObject go = new GameObject(name);
    go.layer = 5;
    RectTransform rt = go.AddComponent<RectTransform>();
    rt.SetParent(parent, false);                    // false = keep local scale/pos, ALWAYS use false for UI
    rt.anchorMin        = new Vector2(0f, 0f);
    rt.anchorMax        = new Vector2(1f, 1f);
    rt.pivot            = new Vector2(0.5f, 0.5f);
    rt.offsetMin        = new Vector2(16f, 16f);    // (left, bottom) inset
    rt.offsetMax        = new Vector2(-16f, -16f);  // (-right, -top) inset
    rt.sizeDelta        = new Vector2(400f, 96f);   // only meaningful when anchorMin == anchorMax on that axis
    rt.anchoredPosition = new Vector2(0f, -12f);
    rt.localScale       = Vector3.one;
    return go;
}

// Non-generic fallbacks (identical behaviour, more typing):
Image a = go.AddComponent(Il2CppType.Of<Image>()).TryCast<Image>();     // null on failure
Image b = go.AddComponent(Il2CppType.From(typeof(Image))).Cast<Image>(); // throws on failure
```

**[verified] compiles.** `Il2CppType.Of<T>()` and `Il2CppType.From(Type)` both exist in Il2CppInterop 1.5.0. `Cast<T>()` throws `InvalidCastException`; `TryCast<T>()` returns `null`. Use `TryCast` everywhere you're not certain.

Anchor recipes for a settings screen:

```csharp
// Full-screen dimmer behind the panel
rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

// Centred fixed-size panel (960 x 720)
rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
rt.pivot = new Vector2(0.5f, 0.5f);
rt.sizeDelta = new Vector2(960f, 720f);
rt.anchoredPosition = Vector2.zero;

// Full-width card, fixed height, stacked from the top by a VerticalLayoutGroup
rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
rt.pivot = new Vector2(0.5f, 1f);
rt.sizeDelta = new Vector2(0f, 88f);   // width driven by anchors, height fixed
```

#### 1.5 `TextMeshProUGUI`

**[verified]** Namespace is `Il2CppTMPro`. Type is `Il2CppTMPro.TextMeshProUGUI`, deriving `TMP_Text` → `MaskableGraphic` → `Graphic`.

```csharp
using Il2CppTMPro;
using UnityEngine;

public static TextMeshProUGUI MakeText(Transform parent, TMP_FontAsset font)
{
    GameObject go = NewUIObject("Label", parent);
    TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();

    t.text        = "Auto-Restock  <color=#8BC34A><b>ON</b></color>";
    t.font        = font;                                  // TMP_FontAsset
    t.fontSize    = 22f;
    t.color       = new Color(1f, 1f, 1f, 1f);
    t.alignment   = TextAlignmentOptions.MidlineLeft;      // Il2CppTMPro.TextAlignmentOptions
    t.fontStyle   = FontStyles.Bold;                       // flags enum: FontStyles.Italic | FontStyles.UpperCase
    t.overflowMode = TextOverflowModes.Ellipsis;           // Il2CppTMPro.TextOverflowModes
    t.margin      = new Vector4(8f, 4f, 8f, 4f);           // (left, top, right, bottom)
    t.richText    = true;                                  // enables <b>, <i>, <color=#..>, <size=..>, <sprite=..>
    t.raycastTarget = false;                               // labels must not eat clicks
    t.enableAutoSizing = false;
    t.extraPadding = true;                                 // avoids SDF clipping on bold/outlined glyphs
    return t;
}
```

**[verified] compiles.** Word-wrap API — **both** exist in this build:

```csharp
t.enableWordWrapping = true;                        // classic TMP 3.0 API — works
t.textWrappingMode   = TextWrappingModes.Normal;    // newer API — ALSO present in this build
```

**[verified]** I probed `Unity.TextMeshPro.dll` metadata: `get_/set_enableWordWrapping` **and** `get_/set_textWrappingMode` are both present, and both compile with **zero obsolete warnings**. Use `enableWordWrapping` if you also want to keep a Mono-branch build compiling against older TMP; use `textWrappingMode` if you don't care. Caveat: the interop assemblies are Cpp2IL-generated and lose `[Obsolete]` attributes, so "no warning" does not prove the underlying TMP release doesn't deprecate it — behaviourally both setters exist and work.

Measuring text (needed for `ContentSizeFitter`-free hand layout):

```csharp
Vector2 size = t.GetPreferredValues(t.text, maxWidth, 0f);   // [verified] exists
float h = size.y;
// or, after the text has been assigned and the layout is valid:
t.ForceMeshUpdate(false, false);
float h2 = t.preferredHeight;
```

Rich-text markup you'll want for the "italic grey description" look — keep the description as one TMP object and style inline instead of creating two:

```csharp
title.text = "Hireable Drivers";
desc.text  = "<i><color=#9AA0A6>Employ drivers to move product between properties.</color></i>";
```

TMP rich tags reference: [TextMeshPro manual](https://docs.unity3d.com/Packages/com.unity.textmeshpro@3.0/manual/index.html), API: [TMP_Text](https://docs.unity3d.com/Packages/com.unity.textmeshpro@3.0/api/TMPro.TMP_Text.html).

#### 1.6 `Button` + `Image` + `Selectable` transitions

```csharp
using System;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public static Button MakeButton(Transform parent, Sprite nineSlice)
{
    GameObject go = NewUIObject("Btn", parent);

    Image img = go.AddComponent<Image>();
    img.sprite  = nineSlice;
    img.type    = Image.Type.Sliced;          // 9-slice; requires sprite.border != Vector4.zero
    img.fillCenter = true;
    img.pixelsPerUnitMultiplier = 1f;         // >1 shrinks the border; 1 = 1:1 px when sprite PPU == canvas refPPU
    img.color   = new Color(0.12f, 0.13f, 0.14f, 1f);

    Button b = go.AddComponent<Button>();
    b.targetGraphic = img;
    b.transition    = Selectable.Transition.ColorTint;   // or SpriteSwap

    ColorBlock cb = b.colors;                 // struct: read, mutate, write back
    cb.normalColor      = Color.white;
    cb.highlightedColor = new Color(0.85f, 1f, 0.85f, 1f);
    cb.pressedColor     = new Color(0.60f, 0.90f, 0.60f, 1f);
    cb.selectedColor    = cb.highlightedColor;
    cb.disabledColor    = new Color(0.40f, 0.40f, 0.40f, 0.50f);
    cb.colorMultiplier  = 1f;
    cb.fadeDuration     = 0.08f;
    b.colors = cb;                            // <-- MUST assign back

    SpriteState ss = b.spriteState;           // struct: same pattern
    ss.highlightedSprite = nineSlice;
    ss.pressedSprite     = nineSlice;
    ss.selectedSprite    = nineSlice;
    ss.disabledSprite    = nineSlice;
    b.spriteState = ss;

    Navigation nav = b.navigation;
    nav.mode = Navigation.Mode.None;          // stop gamepad/arrow focus stealing from the game
    b.navigation = nav;

    return b;
}
```

**[verified] compiles.** `ColorBlock`, `SpriteState` and `Navigation` are value types; mutating the property in place silently does nothing — always assign back.

**Listener marshalling — all three of these compile and work:**

```csharp
// 1. Cast-a-lambda. Works because C# 10 (net6 default) gives the lambda the natural type System.Action,
//    and interop's UnityAction declares a user-defined conversion from System.Action.
b.onClick.AddListener((UnityAction)(() => MelonLogger.Msg("clicked")));

// 2. Pass a System.Action directly — the conversion is implicit.
Action act = () => MelonLogger.Msg("clicked");
b.onClick.AddListener(act);

// 3. Explicit, version-proof, works on any C# LangVersion.
b.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>(act));

// Generic events work the same way:
toggle.onValueChanged.AddListener((UnityAction<bool>)((bool v) => MelonLogger.Msg($"{v}")));
scrollRect.onValueChanged.AddListener((UnityAction<Vector2>)((Vector2 v) => { }));
```

**[verified] all four lines compile.** Form 1 is what the Schedule I modding wiki documents ([s1modding: Il2Cpp Modding](https://s1modding.github.io/docs/moddevs/il2cpp/)). Form 2 is what UniverseLib exposes as an extension ([Il2CppProvider.cs](https://github.com/sinai-dev/UniverseLib/blob/main/src/Runtime/Il2Cpp/Il2CppProvider.cs)):

```csharp
// UniverseLib/src/Runtime/Il2Cpp/Il2CppProvider.cs
public static class Il2CppExtensions
{
    public static void AddListener(this UnityEvent action, Action listener) => action.AddListener(listener);
    public static void AddListener<T>(this UnityEvent<T> action, Action<T> listener) => action.AddListener(listener);
}
```

**Lifetime gotcha [community/inference]:** the native `UnityEvent` holds an IL2CPP delegate that wraps your managed closure. Il2CppInterop keeps that alive via a GC handle, but if the closure captures a `MonoBehaviour` you later destroy, invoking it throws. Keep listeners pointing at static/long-lived state (a plain C# settings singleton), which is what the recommended "no injection" architecture in §1.7 gives you anyway.

**Clearing inherited listeners (critical when cloning):**

```csharp
b.onClick = new Button.ButtonClickedEvent();   // nuke everything the prefab had
// or
b.onClick.RemoveAllListeners();                // only removes runtime AddListener calls, NOT serialised ones
```

**[verified]** `new Button.ButtonClickedEvent()` compiles and is exactly what the shipped Schedule I mod HonestMainMenu does after cloning a menu button:

```csharp
// HonestMainMenu (decompiled), Roachified — clones the LoadGame button to make a Continue button
GameObject val = Object.Instantiate<GameObject>(buttonObject, buttonObject.transform.parent);
((Object)val).name = "Continue";
val.SetText("Continue");
Button component = val.GetComponent<Button>();
if ((Object)(object)component == (Object)null) { Object.Destroy((Object)(object)val); return null; }
component.onClick = new ButtonClickedEvent();   // <-- wipe the original's serialised onClick
return component;
```
Source: [thunderstore.io/c/schedule-i/p/Roachified/HonestMainMenu/source/](https://thunderstore.io/c/schedule-i/p/Roachified/HonestMainMenu/source/).
`RemoveAllListeners()` alone is **not** enough on a clone — Unity's serialised persistent calls survive it. Replacing the whole event object is the reliable move.

#### 1.7 Custom `MonoBehaviour` injection — and why you probably shouldn't

**Recommended path: don't inject anything.** Drive everything from `MelonMod.OnUpdate()` plus plain C# state and `Button.onClick` lambdas. You get hot-reloadable, debuggable, exception-safe code with none of the injection failure modes.

```csharp
public class SettingsMod : MelonMod
{
    private static readonly Dictionary<string, bool> State = new();
    private SettingsScreen screen;        // plain C# class, NOT a MonoBehaviour
    private bool open;

    public override void OnUpdate()
    {
        if (Input.GetKeyDown(KeyCode.F7)) { open = !open; screen?.SetVisible(open); }
        if (open) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        screen?.Tick();                   // animations, hover polish, whatever
    }
}
```

If you *must* inject (custom `OnPointerEnter`, `IDragHandler`, per-frame work tied to object lifetime):

```csharp
using System;
using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.Injection;
using MelonLoader;
using UnityEngine;

[RegisterTypeInIl2Cpp]                                   // MelonLoader attribute; or call ClassInjector manually
public class MyBehaviour : MonoBehaviour
{
    public MyBehaviour(IntPtr ptr) : base(ptr) { }       // REQUIRED — IL2CPP calls this

    private Action onTick;                               // plain field, no attribute

    [HideFromIl2Cpp]                                     // valid on ctor/method/property/indexer/event ONLY
    public Action OnTick { get => onTick; set => onTick = value; }

    [HideFromIl2Cpp]
    public void ConfigureManaged(Dictionary<string, bool> state) { }

    private void Update() => onTick?.Invoke();
}

// Manual registration (must happen before first use):
ClassInjector.RegisterTypeInIl2Cpp<MyBehaviour>();
MyBehaviour mb = go.AddComponent<MyBehaviour>();          // [verified] generic AddComponent works on injected types
```

**[verified] GOTCHA #1 — `[HideFromIl2Cpp]` is illegal on fields.** Putting it on a field is a hard compile error:
`error CS0592: Attribute 'HideFromIl2Cpp' is not valid on this declaration type. It is only valid on 'constructor, method, property, indexer, event' declarations.`
Wrap the field in a property, as above. Every "just add `[HideFromIl2Cpp]` to your `Dictionary` field" answer on the internet is wrong for Il2CppInterop 1.5.0.

**[verified] GOTCHA #2 — you cannot implement Unity event interfaces the normal way.** Il2CppInterop emits `IPointerEnterHandler`, `IPointerExitHandler`, `IDragHandler` etc. as **classes**, not interfaces. This fails:

```csharp
public class Hover : MonoBehaviour, IPointerEnterHandler   // error CS1721: Class 'Hover' cannot have
{ }                                                        // multiple base classes: 'MonoBehaviour' and
                                                           // 'IPointerEnterHandler'
```

Two working fixes, both **[verified] compile**:

```csharp
// (a) Inject the interfaces explicitly via RegisterTypeOptions.
public class Hover : MonoBehaviour
{
    public Hover(IntPtr ptr) : base(ptr) { }
    public void OnPointerEnter(PointerEventData eventData) { }
    public void OnPointerExit (PointerEventData eventData) { }

    public static void Register() =>
        ClassInjector.RegisterTypeInIl2Cpp<Hover>(new RegisterTypeOptions
        {
            Interfaces = new[] { typeof(IPointerEnterHandler), typeof(IPointerExitHandler) }
        });
}
```

```csharp
// (b) BETTER for a settings screen: no injection at all — use Unity's own EventTrigger component.
using UnityEngine.EventSystems;

public static void AddHover(GameObject go, Action onEnter, Action onExit)
{
    EventTrigger trig = go.GetComponent<EventTrigger>() ?? go.AddComponent<EventTrigger>();
    if (trig.triggers == null)
        trig.triggers = new Il2CppSystem.Collections.Generic.List<EventTrigger.Entry>();

    EventTrigger.Entry enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
    enter.callback = new EventTrigger.TriggerEvent();
    enter.callback.AddListener((UnityAction<BaseEventData>)((BaseEventData _) => onEnter()));
    trig.triggers.Add(enter);

    EventTrigger.Entry exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
    exit.callback = new EventTrigger.TriggerEvent();
    exit.callback.AddListener((UnityAction<BaseEventData>)((BaseEventData _) => onExit()));
    trig.triggers.Add(exit);
}
```

Note `trig.triggers` is `Il2CppSystem.Collections.Generic.List<EventTrigger.Entry>`, not a `System.Collections.Generic.List<>`.

**[verified] GOTCHA #3 — no `IEnumerator` members on injected types.** The Schedule I modding wiki states it plainly: *"Don't put IEnumerators in types registered with `RegisterTypeInIl2Cpp`, as they will not work properly."* Use `MelonCoroutines.Start(MyCoroutine())` from a static/non-injected class instead ([verified] compiles).

Reference: [MelonWiki — IL2CPP differences](https://github.com/LavaGang/MelonWiki/blob/master/docs/modders/il2cppdifferences.md), [Il2CppInterop — Class Injection](https://github.com/BepInEx/Il2CppInterop/blob/master/Documentation/Class-Injection.md).

#### 1.8 Layout: use the layout system, with two guardrails

**Verdict:** use `VerticalLayoutGroup` + `LayoutElement` for the card list, and hand-compute *inside* each card. Auto-layout across N cards is where hand-computation gets miserable; inside a fixed-height card, anchors are simpler and faster than a nested layout group.

```csharp
using UnityEngine;
using UnityEngine.UI;

VerticalLayoutGroup v = listGo.AddComponent<VerticalLayoutGroup>();
v.spacing                 = 8f;
v.padding                 = new RectOffset(12, 12, 12, 12);
v.childAlignment          = TextAnchor.UpperLeft;
v.childControlHeight      = true;    // let children report preferredHeight
v.childControlWidth       = true;
v.childForceExpandHeight  = false;   // DO NOT force-expand height in a scroll list
v.childForceExpandWidth   = true;

ContentSizeFitter f = listGo.AddComponent<ContentSizeFitter>();
f.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
f.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

LayoutElement le = cardGo.AddComponent<LayoutElement>();
le.minHeight       = 64f;
le.preferredHeight = 72f;
le.flexibleWidth   = 1f;

LayoutRebuilder.ForceRebuildLayoutImmediate(listGo.GetComponent<RectTransform>());
Canvas.ForceUpdateCanvases();
```

**[verified] compiles**, including `RectOffset`, `TextAnchor`, `LayoutRebuilder.ForceRebuildLayoutImmediate`, `LayoutUtility.GetPreferredHeight(rt)`, `LayoutUtility.GetMinHeight(rt)`.

Gotchas:

1. **`ContentSizeFitter` + TMP preferred height is a one-frame liar.** TMP reports `preferredHeight` from its last generated mesh. On the frame you set `.text`, the mesh is stale, so the fitter sizes to the *previous* string. Fix: `t.ForceMeshUpdate(false, false);` then `LayoutRebuilder.ForceRebuildLayoutImmediate(parentRect);` — in that order, immediately after setting the text. **[community]** This is the single most common "my modded UI has overlapping rows" bug.
2. **`ContentSizeFitter` on a child of a layout group fights the group.** Put the fitter on the scroll `Content` object only, never on cards that are already driven by the parent group. UniverseLib does exactly this — fitter on `Content`, `LayoutElement` on children ([UIFactory.cs `CreateScrollView`](https://github.com/sinai-dev/UniverseLib/blob/main/src/UI/UIFactory.cs)).
3. **`ForceRebuildLayoutImmediate` is O(subtree).** Call it once after you've built/refreshed the whole list, not per card.
4. **`HorizontalOrVerticalLayoutGroup`** is the shared base if you want to write generic helpers: `v.TryCast<HorizontalOrVerticalLayoutGroup>()` **[verified]**.

Docs: [Auto Layout](https://docs.unity3d.com/2022.3/Documentation/Manual/UIAutoLayout.html), [ContentSizeFitter](https://docs.unity3d.com/Packages/com.unity.ugui@1.0/api/UnityEngine.UI.ContentSizeFitter.html), [LayoutRebuilder](https://docs.unity3d.com/Packages/com.unity.ugui@1.0/api/UnityEngine.UI.LayoutRebuilder.html).

#### 1.9 `ScrollRect` for the card list

```csharp
using UnityEngine;
using UnityEngine.UI;

public static ScrollRect BuildScroll(Transform parent, out RectTransform content)
{
    GameObject rootGo = NewUIObject("Scroll", parent);
    ScrollRect sr = rootGo.AddComponent<ScrollRect>();
    sr.horizontal        = false;
    sr.vertical          = true;
    sr.movementType      = ScrollRect.MovementType.Clamped;   // Elastic feels wrong for settings
    sr.scrollSensitivity = 30f;
    sr.inertia           = false;

    // Viewport: clips content. RectMask2D is cheaper than Mask (no stencil, no extra draw call).
    GameObject viewportGo = NewUIObject("Viewport", rootGo.transform);
    RectTransform viewportRt = viewportGo.GetComponent<RectTransform>();
    viewportRt.anchorMin = Vector2.zero;
    viewportRt.anchorMax = Vector2.one;
    viewportRt.offsetMin = Vector2.zero;
    viewportRt.offsetMax = new Vector2(-14f, 0f);             // leave room for a scrollbar
    viewportRt.pivot     = new Vector2(0f, 1f);
    RectMask2D mask = viewportGo.AddComponent<RectMask2D>();
    mask.padding  = new Vector4(0f, 0f, 0f, 0f);
    mask.softness = new Vector2Int(0, 0);                     // >0 gives a free fade-out edge

    // Content: top-anchored, height driven by VerticalLayoutGroup + ContentSizeFitter.
    GameObject contentGo = NewUIObject("Content", viewportGo.transform);
    content = contentGo.GetComponent<RectTransform>();
    content.anchorMin = new Vector2(0f, 1f);
    content.anchorMax = new Vector2(1f, 1f);
    content.pivot     = new Vector2(0.5f, 1f);
    content.offsetMin = Vector2.zero;
    content.offsetMax = Vector2.zero;
    content.sizeDelta = new Vector2(0f, 0f);

    VerticalLayoutGroup v = contentGo.AddComponent<VerticalLayoutGroup>();
    v.spacing = 8f;
    v.padding = new RectOffset(12, 12, 12, 12);
    v.childControlHeight = true;
    v.childForceExpandHeight = false;
    contentGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

    sr.viewport = viewportRt;
    sr.content  = content;
    return sr;
}
```

**[verified] compiles**, including `RectMask2D.padding` (`Vector4`) and `RectMask2D.softness` (`Vector2Int`).

`Mask` alternative if you need a non-rectangular / sprite-shaped clip:

```csharp
Image maskImg = viewportGo.AddComponent<Image>();   // Mask requires a Graphic
maskImg.sprite = roundedSprite;
Mask m = viewportGo.AddComponent<Mask>();
m.showMaskGraphic = false;
```
`Mask` costs 2 extra draw calls and breaks batching; prefer `RectMask2D` unless you genuinely need rounded clipping. **[community]** Note `RectMask2D` also does *not* clip TMP soft-shadow/underlay outside the rect cleanly — keep card content inside the padding.

#### 1.10 EventSystem, cursor lock, and giving input back to the game

This is the part that breaks mods. Four separate problems:

**(a) Is there an EventSystem at all, and is it the right one?**

```csharp
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;   // Unity.InputSystem.dll — present in this game

public static EventSystem EnsureEventSystem()
{
    EventSystem es = EventSystem.current;

    if (es == null)                                  // current can be null before the game's UI boots
    {
        foreach (UnityEngine.Object o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<EventSystem>()))
        {
            EventSystem cand = o.TryCast<EventSystem>();
            if (cand != null && cand.isActiveAndEnabled) { es = cand; break; }
        }
    }

    if (es == null)                                  // still nothing: make our own
    {
        GameObject go = new GameObject("MyMod_EventSystem");
        UnityEngine.Object.DontDestroyOnLoad(go);
        es = go.AddComponent<EventSystem>();

        // Pick the module that matches the game. Schedule I ships Unity.InputSystem AND the legacy module.
        go.AddComponent<InputSystemUIInputModule>();      // new Input System path
        // go.AddComponent<StandaloneInputModule>();      // legacy path — DO NOT add both
        EventSystem.current = es;
    }
    return es;
}
```

**[verified] compiles**, both module types resolve. **[verified]** `Unity.InputSystem.dll` in this game's interop set contains `InputSystemUIInputModule`; `UnityEngine.UI.dll` contains `StandaloneInputModule`. **[inference]** Because the game ships `Unity.InputSystem` + `Unity.InputSystem.ForUI`, its live EventSystem almost certainly uses `InputSystemUIInputModule`. **Strongly prefer reusing the game's existing EventSystem** over creating one — two active EventSystems produce duplicated/void pointer events, and Unity logs a warning.

The robust pattern: don't create anything, just read the module the game already uses.

```csharp
BaseInputModule existing = EventSystem.current?.currentInputModule;
bool usingNewInput = existing != null && existing.TryCast<InputSystemUIInputModule>() != null;
```

**(b) Unlocking the cursor when the game re-locks it every frame.**
Setting `Cursor.lockState` in `OnUpdate` works but flickers, because the game's own code sets it later in the frame. The clean fix is to Harmony-patch the setters, which is what UniverseLib does ([CursorUnlocker.cs](https://github.com/sinai-dev/UniverseLib/blob/main/src/Input/CursorUnlocker.cs)):

```csharp
using HarmonyLib;
using UnityEngine;

[HarmonyPatch(typeof(Cursor), nameof(Cursor.lockState), MethodType.Setter)]
public static class CursorLockPatch
{
    public static bool ForceUnlock;
    private static CursorLockMode lastGameValue = CursorLockMode.Locked;

    private static void Prefix(ref CursorLockMode value)
    {
        if (!ForceUnlock) { lastGameValue = value; return; }   // remember what the game wanted
        value = CursorLockMode.None;                           // ...and override it
    }

    public static CursorLockMode GameValue => lastGameValue;
}

[HarmonyPatch(typeof(Cursor), nameof(Cursor.visible), MethodType.Setter)]
public static class CursorVisiblePatch
{
    private static bool lastGameValue;
    private static void Prefix(ref bool value)
    {
        if (!CursorLockPatch.ForceUnlock) { lastGameValue = value; return; }
        value = true;
    }
    public static bool GameValue => lastGameValue;
}
```

**[verified] compiles** with the bundled HarmonyLib 2.10.2. Open the menu: `ForceUnlock = true; Cursor.lockState = CursorLockMode.None; Cursor.visible = true;`. Close it: `ForceUnlock = false; Cursor.lockState = CursorLockPatch.GameValue; Cursor.visible = CursorVisiblePatch.GameValue;` — restoring *the value the game last asked for* rather than a hardcoded `Locked` is what makes the hand-back seamless.

**(c) Stopping clicks/scroll from reaching the game underneath.**
A `GraphicRaycaster` + a full-screen `Image` (even at `alpha = 0.001`, `raycastTarget = true`) behind your panel blocks uGUI clicks. It does **not** block raw `Input.GetMouseButtonDown` polling in the game's own scripts. UniverseLib's mitigation is to zero the input axes when the pointer is over modded UI:

```csharp
if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
    UnityEngine.Input.ResetInputAxes();
```
**[community]** For Schedule I specifically, the more reliable lever is whatever pause/`InputManager` gate the game already uses when its own menus open — a sibling agent is decompiling that; wire your open/close into it if it exists, and keep the above as a fallback.

**(d) Keyboard hotkey without fighting the input backend.**

```csharp
// Legacy — works today (the existing CreativeMode mod uses Input.GetKeyDown / Input.inputString on this build).
if (UnityEngine.Input.GetKeyDown(KeyCode.F7)) Toggle();

// New Input System equivalent, immune to "Active Input Handling = Input System (New)".
using UnityEngine.InputSystem;
Keyboard kb = Keyboard.current;
if (kb != null && kb[Key.F7].wasPressedThisFrame) Toggle();
Vector2 mouse = Mouse.current != null ? Mouse.current.position.ReadValue()
                                      : (Vector2)UnityEngine.Input.mousePosition;
```
**[verified]** both compile. **[verified from project history]** legacy `Input` works on this build (CreativeMode v1.3.0 ships with it), so Active Input Handling is "Both". Writing the `Keyboard.current` path anyway is 3 lines of insurance against a future patch.

---

### 2. Reusing the game's fonts and sprites at RUNTIME (preferred over shipping assets)

#### 2.1 `Resources.FindObjectsOfTypeAll` — exact syntax that compiles

```csharp
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppTMPro;
using UnityEngine;

// Generic form -> Il2CppArrayBase<T>. Preferred: no casting needed.
Il2CppArrayBase<TMP_FontAsset> fonts   = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
Il2CppArrayBase<Sprite>        sprites = Resources.FindObjectsOfTypeAll<Sprite>();
Il2CppArrayBase<Font>          osFonts = Resources.FindObjectsOfTypeAll<Font>();
Il2CppArrayBase<Material>      mats    = Resources.FindObjectsOfTypeAll<Material>();
Il2CppArrayBase<Texture2D>     texs    = Resources.FindObjectsOfTypeAll<Texture2D>();
Il2CppArrayBase<TMP_SpriteAsset> sas   = Resources.FindObjectsOfTypeAll<TMP_SpriteAsset>();
Il2CppArrayBase<AudioClip>     clips   = Resources.FindObjectsOfTypeAll<AudioClip>();

foreach (Sprite s in sprites) { if (s != null && s.name == "RoundedPanel") { /* ... */ } }
for (int i = 0; i < fonts.Length; i++) { TMP_FontAsset f = fonts[i]; }

// Non-generic form -> Il2CppReferenceArray<UnityEngine.Object>; you must cast each element.
Il2CppReferenceArray<UnityEngine.Object> raw = Resources.FindObjectsOfTypeAll(Il2CppType.Of<TMP_FontAsset>());
for (int i = 0; i < raw.Length; i++)
{
    TMP_FontAsset f = raw[i].TryCast<TMP_FontAsset>();
    if (f != null) { /* ... */ }
}

// Resources.LoadAll
Il2CppArrayBase<Sprite> fromResources                 = Resources.LoadAll<Sprite>("");
Il2CppReferenceArray<UnityEngine.Object> fromResources2 = Resources.LoadAll("", Il2CppType.Of<Sprite>());
```

**[verified] all of the above compile.** The generic form works because Il2CppInterop generates the generic method properly; the MelonWiki's `Resources.FindObjectsOfTypeAll(Il2CppType.Of<Camera>())` advice is the older/safer form, but you don't need it here. This exact generic call is used in production by a shipped Schedule I mod (GDG Traditional Chinese Translation, decompiled: `Il2CppArrayBase<TMP_FontAsset> val = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();`).

**Caveats [verified/community]:**
- `FindObjectsOfTypeAll` returns inactive objects, prefabs, and **assets not attached to any scene** — including editor-leftover and fallback assets. Never take element `[0]`.
- It's slow (walks the entire IL2CPP object heap for that type). Call it **once**, cache the result in `static` fields, and never per-frame.
- Objects loaded lazily by the game (e.g. per-scene UI) won't exist before their scene loads. Harvest in `OnSceneWasInitialized` for the menu scene *and* the gameplay scene.

#### 2.2 The font lookup I'd actually bet on: vote among live UI text

Don't pick a font asset from the heap; pick the one the game is *using on screen right now*. Also grab its `fontSharedMaterial`, because that material carries the outline/underlay/face-dilate preset that makes the text look like the game's.

```csharp
using System.Collections.Generic;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;

public readonly struct GameTextStyle
{
    public readonly TMP_FontAsset Font;
    public readonly Material      SharedMaterial;   // the preset (outline/underlay), reuse it verbatim
    public readonly float         SampleFontSize;
    public readonly Color         SampleColor;
    public GameTextStyle(TMP_FontAsset f, Material m, float s, Color c)
        { Font = f; SharedMaterial = m; SampleFontSize = s; SampleColor = c; }
    public bool IsValid => Font != null;
}

public static class GameStyle
{
    private static GameTextStyle cached;

    /// Returns the TMP_FontAsset + material used by the majority of the game's on-screen UI text.
    public static GameTextStyle Resolve(bool force = false)
    {
        if (cached.IsValid && !force) return cached;

        Dictionary<int, int>           votes = new();
        Dictionary<int, TMP_FontAsset> fonts = new();
        Dictionary<int, Material>      mats  = new();
        Dictionary<int, float>         sizes = new();
        Dictionary<int, Color>         cols  = new();

        foreach (TextMeshProUGUI t in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())
        {
            if (t == null) continue;
            if (!t.gameObject.scene.IsValid()) continue;    // skip prefabs / imported assets
            if (t.canvas == null) continue;                 // must live under a real Canvas
            if (string.IsNullOrEmpty(t.text)) continue;     // skip empty placeholders

            TMP_FontAsset f = t.font;
            if (f == null) continue;

            int id = f.GetInstanceID();
            votes.TryGetValue(id, out int n);
            // Weight active, visible text more heavily than disabled UI.
            votes[id] = n + (t.isActiveAndEnabled ? 3 : 1);
            fonts[id] = f;
            if (!mats.ContainsKey(id)) { mats[id] = t.fontSharedMaterial; sizes[id] = t.fontSize; cols[id] = t.color; }
        }

        int bestId = 0, bestVotes = 0;
        foreach (KeyValuePair<int, int> kv in votes)
            if (kv.Value > bestVotes) { bestVotes = kv.Value; bestId = kv.Key; }

        if (bestVotes == 0)
        {
            // Fallback 1: any TMP_FontAsset on the heap that has a usable material.
            foreach (TMP_FontAsset f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                if (f != null && f.material != null)
                    return cached = new GameTextStyle(f, f.material, 22f, Color.white);

            // Fallback 2: build one from an OS font (last resort; won't match the game's look).
            Font os = Font.CreateDynamicFontFromOSFont("Arial", 24);
            TMP_FontAsset made = TMP_FontAsset.CreateFontAsset(os);
            return cached = new GameTextStyle(made, made != null ? made.material : null, 22f, Color.white);
        }

        cached = new GameTextStyle(fonts[bestId], mats[bestId], sizes[bestId], cols[bestId]);
        MelonLogger.Msg($"[style] font='{cached.Font.name}' mat='{cached.SharedMaterial?.name}' " +
                        $"size={cached.SampleFontSize} votes={bestVotes}");
        return cached;
    }

    public static void Apply(TMP_Text t, GameTextStyle s)
    {
        t.font = s.Font;
        if (s.SharedMaterial != null) t.fontSharedMaterial = s.SharedMaterial;  // shared = no material instance
    }
}
```

**[verified] compiles** (`t.canvas`, `t.fontSharedMaterial`, `t.fontMaterial`, `TMP_FontAsset.CreateFontAsset(Font)`, `Font.CreateDynamicFontFromOSFont`, `GetInstanceID`, `gameObject.scene.IsValid()` all resolve).

Use `fontSharedMaterial` (not `fontMaterial`) when assigning — `fontMaterial` *instantiates* a new material per text object, costing a draw call each and breaking batching. Only touch `fontMaterial` when you need per-object outline colour.

**If the game uses more than one font** (headers vs body, near certain), run the same vote scoped to a subtree:

```csharp
public static TMP_FontAsset FontUsedUnder(GameObject root)
{
    Dictionary<int,int> votes = new(); Dictionary<int,TMP_FontAsset> byId = new();
    foreach (TextMeshProUGUI t in root.GetComponentsInChildren<TextMeshProUGUI>(true))  // Il2CppArrayBase<T>
    {
        TMP_FontAsset f = t.font; if (f == null) continue;
        int id = f.GetInstanceID(); votes.TryGetValue(id, out int n); votes[id] = n + 1; byId[id] = f;
    }
    int best = 0, bn = 0;
    foreach (KeyValuePair<int,int> kv in votes) if (kv.Value > bn) { bn = kv.Value; best = kv.Key; }
    return bn > 0 ? byId[best] : null;
}
```

Point it at the pause/settings menu root and you get exactly the font that screen uses. **[verified] compiles** — note `GetComponentsInChildren<T>(true)` returns `Il2CppArrayBase<T>`, not `T[]` and not `Il2CppReferenceArray<T>`.

#### 2.3 Finding the game's own rounded 9-slice card sprite

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// Ranks every sliced Image in live UI and returns the sprite most likely to be
/// "the dark rounded panel background". Log the top 10 and eyeball the names.
public static void DumpCandidatePanelSprites()
{
    Dictionary<int, int>    uses    = new();
    Dictionary<int, Sprite> byId    = new();
    Dictionary<int, Color>  tint    = new();

    foreach (Image img in Resources.FindObjectsOfTypeAll<Image>())
    {
        if (img == null || img.sprite == null) continue;
        if (!img.gameObject.scene.IsValid()) continue;
        if (img.type != Image.Type.Sliced && img.type != Image.Type.Tiled) continue;

        Sprite s = img.sprite;
        if (s.border == Vector4.zero) continue;        // not a 9-slice

        int id = s.GetInstanceID();
        uses.TryGetValue(id, out int n);
        uses[id] = n + 1;
        byId[id] = s;
        if (!tint.ContainsKey(id)) tint[id] = img.color;
    }

    List<KeyValuePair<int,int>> ordered = new(uses);
    ordered.Sort((a, b) => b.Value.CompareTo(a.Value));
    for (int i = 0; i < ordered.Count && i < 10; i++)
    {
        Sprite s = byId[ordered[i].Key];
        MelonLogger.Msg($"[sprite] '{s.name}' uses={ordered[i].Value} border={s.border} " +
                        $"rect={s.rect} ppu={s.pixelsPerUnit} tint={tint[ordered[i].Key]}");
    }
}
```

**[verified] compiles.** Reuse a found sprite by assigning `img.sprite = found; img.type = Image.Type.Sliced; img.color = <your tint>;` — the game's own sprites are usually white/greyscale so you can recolour freely.

#### 2.4 Cloning an existing native UI element — the recommended path

**Why this wins.** A clone inherits, for free and without a single hardcoded style value: the exact `TMP_FontAsset` **and** its material preset, the exact 9-slice sprite and `pixelsPerUnitMultiplier`, the exact `ColorBlock` for hover/press/disabled, the exact `RectTransform` size and padding, any `LayoutElement` metrics, any `AudioSource`/hover animator the game attaches to its buttons, and any future restyling the developer ships in a patch. Build-from-scratch means re-deriving all of that by eye, and re-deriving it again after every game update.

**Why not always.** A clone also inherits game `MonoBehaviour`s that may reference game singletons and throw `NullReferenceException` in `OnEnable` when instantiated outside their expected parent, serialised `onClick` targets pointing at real game actions, and `Animator` controllers driving states you don't set. So: clone, then *sanitise*.

```csharp
using System;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public static class Cloner
{
    /// Clone a real menu button, wipe its behaviour, keep its look + hover + sound.
    public static Button CloneButton(GameObject template, Transform parent, string label, Action onClick)
    {
        GameObject clone = UnityEngine.Object.Instantiate(template, parent, false);
        clone.name = "MyMod_" + label.Replace(" ", "");
        clone.SetActive(true);

        // 1. Reset the click event completely (RemoveAllListeners does NOT clear serialised calls).
        Button b = clone.GetComponent<Button>();
        if (b != null)
        {
            b.onClick = new Button.ButtonClickedEvent();
            b.onClick.AddListener((UnityAction)(() => onClick()));
            Navigation nav = b.navigation; nav.mode = Navigation.Mode.None; b.navigation = nav;
        }

        // 2. Retarget the text.
        TextMeshProUGUI txt = clone.GetComponentInChildren<TextMeshProUGUI>(true);
        if (txt != null) { txt.text = label; txt.enabled = true; }

        // 3. Strip game logic while keeping Unity UI + audio.
        StripGameBehaviours(clone);
        return b;
    }

    /// Destroys every non-Unity MonoBehaviour on the clone. Whitelist anything you want to keep.
    public static void StripGameBehaviours(GameObject clone)
    {
        Il2CppArrayBase<MonoBehaviour> comps = clone.GetComponentsInChildren<MonoBehaviour>(true);
        foreach (MonoBehaviour mb in comps)
        {
            if (mb == null) continue;
            string full = mb.GetIl2CppType().FullName;                    // e.g. "Il2CppScheduleOne.UI.SomeThing"
            if (full.StartsWith("UnityEngine.")) continue;                // keep Button/Image/TMP/LayoutElement
            if (full.StartsWith("TMPro.") || full.StartsWith("Il2CppTMPro.")) continue;
            MelonLogger.Msg($"[clone] stripping {full}");
            UnityEngine.Object.DestroyImmediate(mb);
        }
    }
}
```

**[verified] compiles**, including `Object.Instantiate(go, parent, false)`, `mb.GetIl2CppType().FullName`, and `DestroyImmediate`.

Practical procedure:

1. **Find a template.** Dump the menu hierarchy once and log paths + component type names:
   ```csharp
   public static string PathOf(Transform t)
   { string s = t.name; while (t.parent != null) { t = t.parent; s = t.name + "/" + s; } return s; }

   foreach (Button b in Resources.FindObjectsOfTypeAll<Button>())
       if (b != null && b.gameObject.scene.IsValid())
           MelonLogger.Msg($"[btn] {PathOf(b.transform)}  tmp='{b.GetComponentInChildren<TextMeshProUGUI>(true)?.text}'");
   ```
   **[community]** The shipped HonestMainMenu mod hardcodes these Schedule I paths, which is a strong hint about the real hierarchy: root `GameObject.Find("MainMenu")`, buttons parent `"Home/Bank"`, children `"Continue"`, `"LoadGame"`, plus `"InputPrompt (Back)"` and `"Title"`. Treat as version-fragile; a sibling agent is verifying against the current build.
2. **Instantiate under your own canvas, not the game's**, so you own its lifetime and don't get destroyed on scene change.
3. **Strip, then verify no exceptions** in the MelonLoader console during `OnEnable`.
4. **Deactivate before instantiating** if the template's `Awake` is dangerous: `template.SetActive(false); clone = Instantiate(...); StripGameBehaviours(clone); clone.SetActive(true); template.SetActive(true);` — `Instantiate` of an inactive object does not run `Awake`, so you strip *before* anything executes. **[inference, high confidence]** This is standard Unity behaviour and the safest ordering.
5. **Keep the original alive.** Never `Destroy` the template; clone it and hide your clone instead.

**Hybrid — what I'd actually ship:** clone the game's *button* and *panel background* (style-heavy, hard to fake), build your *layout containers* from scratch (style-free, trivial). Cards = cloned panel image + scratch `VerticalLayoutGroup` + cloned-font TMP text + cloned toggle/button.

#### 2.5 UI click / hover sound

Three approaches, in order of fidelity:

**(a) It comes free with the clone.** If the game plays its click sound from an `AudioSource` on the button (or an `EventTrigger`/animator on it), a sanitised clone that keeps the `AudioSource` still plays it. Check before you strip:

```csharp
AudioSource s = clone.GetComponentInChildren<AudioSource>(true) ?? clone.GetComponentInParent<AudioSource>();
if (s != null) MelonLogger.Msg($"[clone] kept AudioSource clip='{s.clip?.name}' vol={s.volume}");
```
**[verified] compiles.** Add `AudioSource` to your strip whitelist.

**(b) The game has a UI/audio manager.** **[inference]** Most Unity games route UI SFX through a singleton (`AudioManager.PlayUISound(...)`, an `AudioMixerGroup`, or a `ScriptableObject` sound bank) rather than per-button sources. A sibling agent is decompiling `Il2CppScheduleOne.*`; if such a type exists, calling it is the highest-fidelity option because you also inherit volume sliders and mixer ducking. Runtime discovery without knowing the class name:

```csharp
// Log every AudioSource that lives under a Canvas — these are the UI sound emitters.
foreach (AudioSource a in Resources.FindObjectsOfTypeAll<AudioSource>())
{
    if (a == null || !a.gameObject.scene.IsValid()) continue;
    if (a.GetComponentInParent<Canvas>() == null) continue;
    MelonLogger.Msg($"[uiaudio] {PathOf(a.transform)} clip='{a.clip?.name}' " +
                    $"mixer='{a.outputAudioMixerGroup?.name}' spatial={a.spatialBlend}");
}
```

**(c) Generic fallback — find a clip by name and play it yourself.**

```csharp
using UnityEngine;

public static AudioClip FindClip(params string[] nameContains)
{
    foreach (AudioClip c in Resources.FindObjectsOfTypeAll<AudioClip>())
    {
        if (c == null) continue;
        string n = c.name.ToLowerInvariant();
        foreach (string want in nameContains)
            if (n.Contains(want)) return c;
    }
    return null;
}

// One long-lived 2D source on your canvas root; do NOT use PlayClipAtPoint for UI (it is 3D and
// spawns a temporary GameObject each call).
private static AudioSource uiSource;
public static void PlayUi(AudioClip clip, float volume = 1f)
{
    if (clip == null) return;
    if (uiSource == null)
    {
        GameObject go = new GameObject("MyMod_UIAudio");
        UnityEngine.Object.DontDestroyOnLoad(go);
        uiSource = go.AddComponent<AudioSource>();
        uiSource.playOnAwake        = false;
        uiSource.spatialBlend       = 0f;      // fully 2D
        uiSource.ignoreListenerPause = true;   // still audible while the game is paused
        uiSource.bypassEffects      = true;
    }
    uiSource.PlayOneShot(clip, volume);
}

// Usage: AudioClip click = FindClip("click", "ui_select", "button");
// Also: route through the game's mixer group if you can find one, so volume sliders apply:
// uiSource.outputAudioMixerGroup = someGameSource.outputAudioMixerGroup;
```
**[verified] compiles** (`PlayOneShot`, `PlayClipAtPoint`, `ignoreListenerPause`, `outputAudioMixerGroup` all resolve). Docs: [AudioSource.PlayOneShot](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/AudioSource.PlayOneShot.html).

#### 2.6 Rounded corners with no shipped art

**(a) Reuse a game sprite** — §2.3. Best fidelity, zero cost, but you depend on the game's art surviving patches. Cache by name **and** by a fallback search.

**(b) Generate the sprite in code — recommended, zero asset dependency.** Below is a complete, correct, anti-aliased rounded-rect + border generator using the standard signed-distance-field rounded-box function, with the right `border` vector for 9-slicing.

```csharp
using UnityEngine;

public static class RoundedSprite
{
    // Keep strong refs so Unity's GC / scene unload never takes them.
    private static readonly System.Collections.Generic.List<Object> keepAlive = new();

    /// <summary>
    /// Builds an anti-aliased rounded-rect sprite suitable for Image.Type.Sliced.
    /// The texture is only (2*radius + 3) px square: the 9-slice stretches the middle,
    /// so a tiny texture renders at any size with pixel-perfect corners.
    /// </summary>
    /// <param name="radius">Corner radius in pixels (== on-screen px when pixelsPerUnit matches the canvas).</param>
    /// <param name="border">Stroke thickness in px. 0 = fill only.</param>
    /// <param name="fill">Interior colour (use white and tint via Image.color for reuse).</param>
    /// <param name="stroke">Border colour. Ignored when border == 0.</param>
    /// <param name="pixelsPerUnit">Must equal Canvas.referencePixelsPerUnit (normally 100) for 1:1 px.</param>
    public static Sprite Create(int radius, int border, Color fill, Color stroke, float pixelsPerUnit = 100f)
    {
        radius = Mathf.Max(0, radius);
        border = Mathf.Max(0, border);

        // 2*radius covers both corner sets; +3 guarantees a >=1px stretchable middle strip.
        int size = radius * 2 + 3;
        float half = size * 0.5f;
        float r = radius;

        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false, false)
        {
            name       = $"MyMod_Rounded_{radius}_{border}",
            filterMode = FilterMode.Bilinear,
            wrapMode   = TextureWrapMode.Clamp,
            anisoLevel = 0,
            hideFlags  = HideFlags.HideAndDontSave
        };

        Color[] px = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // Signed distance to a rounded box (Inigo Quilez sdRoundBox), sampled at pixel centres.
                //   q = |p| - b + r ;  d = length(max(q,0)) + min(max(q.x,q.y),0) - r
                float qx = Mathf.Abs(x + 0.5f - half) - (half - r);
                float qy = Mathf.Abs(y + 0.5f - half) - (half - r);
                float d  = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude
                         + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;

                // 1px linear coverage -> anti-aliasing without supersampling.
                float aOuter = Mathf.Clamp01(0.5f - d);                       // 1 inside, 0 outside
                float aInner = border <= 0 ? 1f : Mathf.Clamp01(0.5f - (d + border));

                // Premultiply-safe blend: lerp RGB by inner coverage, then scale alpha by outer coverage.
                Color c;
                if (border <= 0) c = fill;
                else
                {
                    c   = Color.Lerp(stroke, fill, aInner);
                    c.a = Mathf.Lerp(stroke.a, fill.a, aInner);
                }
                c.a *= aOuter;
                px[y * size + x] = c;
            }
        }

        tex.SetPixels(px);          // Color[] converts implicitly to Il2CppStructArray<Color>
        tex.Apply(false, false);    // no mipmaps, keep readable

        // 9-slice border MUST be >= radius so the corners are never stretched.
        // Vector4 is (left, bottom, right, top) in PIXELS.
        float b = radius + 1f;
        Sprite sprite = Sprite.Create(
            tex,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            pixelsPerUnit,
            0u,
            SpriteMeshType.FullRect,     // FullRect is required for 9-slicing
            new Vector4(b, b, b, b),
            false);
        sprite.name = tex.name;
        sprite.hideFlags = HideFlags.HideAndDontSave;

        keepAlive.Add(tex);
        keepAlive.Add(sprite);
        return sprite;
    }

    /// Convenience: the two sprites a "settings card" needs.
    public static Sprite Card(int radius = 10)  => Create(radius, 0, Color.white, Color.clear);
    public static Sprite Outline(int radius = 10, int stroke = 2)
        => Create(radius, stroke, new Color(1f, 1f, 1f, 0f), Color.white);
}
```

Usage:

```csharp
Image bg = cardGo.AddComponent<Image>();
bg.sprite                  = RoundedSprite.Card(10);
bg.type                    = Image.Type.Sliced;
bg.fillCenter              = true;
bg.pixelsPerUnitMultiplier = 1f;
bg.color                   = new Color(0.11f, 0.12f, 0.13f, 0.96f);   // dark card

// Green "enabled" highlight ring on top of the same card
Image ring = ringGo.AddComponent<Image>();
ring.sprite   = RoundedSprite.Outline(10, 2);
ring.type     = Image.Type.Sliced;
ring.fillCenter = false;                     // draw only the 8 border quads
ring.color    = new Color(0.55f, 0.80f, 0.35f, 1f);
```

**[verified] compiles** — `Sprite.Create(Texture2D, Rect, Vector2, float, uint, SpriteMeshType, Vector4, bool)` exists in Unity 2022.3 ([Sprite.Create docs](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Sprite.Create.html)) and the generated interop signature matches.

Sizing maths that trips people up: on-screen slice size = `spriteBorderPx × (canvas.referencePixelsPerUnit / sprite.pixelsPerUnit) ÷ image.pixelsPerUnitMultiplier`. Keep `sprite.pixelsPerUnit == canvas.referencePixelsPerUnit` (both 100) and `pixelsPerUnitMultiplier = 1` and your radius is exactly the pixel value you asked for *in canvas reference space* (so it scales with the `CanvasScaler`, which is what you want). See [Image.pixelsPerUnitMultiplier](https://docs.unity3d.com/Packages/com.unity.ugui@1.0/api/UnityEngine.UI.Image.html) and [9-slicing](https://docs.unity3d.com/2022.3/Documentation/Manual/9SliceSprites.html).

IL2CPP-specific notes on the texture path:

```csharp
// tex.SetPixels(Color[]) works because Il2CppStructArray<T> defines an implicit conversion from T[].
// If you ever hit an ambiguity, be explicit:
using Il2CppInterop.Runtime.InteropTypes.Arrays;

Il2CppStructArray<Color> arr = new Il2CppStructArray<Color>(px.Length);
for (int i = 0; i < px.Length; i++) arr[i] = px[i];
tex.SetPixels(arr);
tex.Apply();

// Color32 variant with an explicit cast (also verified):
tex.SetPixels32((Il2CppStructArray<Color32>)px32);
```
**[verified] all three forms compile.** `Texture2D.GetPixels()` returns `Il2CppStructArray<Color>`.

**Lifetime warning [community/inference]:** a `Texture2D` you create from managed code is a native object. Without `HideFlags.HideAndDontSave` it is destroyed on scene unload and your cards go magenta/blank after a load screen. Also keep a managed reference (the `keepAlive` list) so Il2CppInterop doesn't release its GC handle.

**(c) Shader / TMP approaches** — for completeness:
- **TMP as a shape.** A `TextMeshProUGUI` containing a single glyph from a shape font, or a `TMP_SpriteAsset`, gets you SDF-crisp rounded shapes at any scale for free. Overkill and awkward for panels.
- **Custom UI shader.** Writing an SDF rounded-rect shader is the "correct" answer for arbitrary resolution independence, but shipping a shader means shipping an AssetBundle (§3) *and* fighting URP shader-variant stripping — this game uses URP (`Unity.RenderPipelines.Universal.Runtime.dll` present, **[verified]**). Not worth it.
- **`Shader.Find`.** `Shader.Find("UI/Default")` **[verified] compiles** and reliably returns the built-in uGUI shader at runtime, which is what a from-scratch `Image` gets anyway. Useful for repairing bundle-loaded materials (§3.3).

**Verdict:** (a) if you find a good game sprite, else (b). (b) is 60 lines, has no external dependency, and survives game patches — make it the default and treat (a) as an optional upgrade discovered at runtime.

---

### 3. AssetBundle path (fallback)

Only take this path if you need a hand-authored prefab, a custom shader, or a bundled font. Everything in §1–2 avoids it.

#### 3.1 Authoring for Unity 2022.3.62f2

**[verified]** The game is Unity **2022.3.62f2**. Install *exactly* that editor version from the [Unity release archive](https://unity.com/releases/editor/archive) (or `unityhub://2022.3.62f1`-style deep link for the matching build). Bundles carry a serialized-format version + a Unity version string; a bundle built by a different **minor** stream (2021.x, 2023.x, Unity 6) fails to load outright, and a different **patch** in the same 2022.3 stream *usually* loads but is not guaranteed. Match the patch version and you never have to think about it. MelonLoader's own AssetBundle fix PR was explicitly validated against `2022.3.62f2` and `6000.3.10f1` ([LavaGang/MelonLoader#1122](https://github.com/LavaGang/MelonLoader/pull/1122)).

Editor build script (`Assets/Editor/BuildBundles.cs` in a throwaway 2022.3.62f2 project):

```csharp
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

public static class BuildBundles
{
    [MenuItem("Tools/Build AssetBundles (Win64)")]
    public static void Build()
    {
        string outDir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "BuiltBundles");
        Directory.CreateDirectory(outDir);

        BuildPipeline.BuildAssetBundles(
            outDir,
            BuildAssetBundleOptions.ChunkBasedCompression   // LZ4: fast random access, good for LoadFromMemory
          | BuildAssetBundleOptions.StrictMode,             // fail the build on any error instead of shipping junk
            BuildTarget.StandaloneWindows64);               // MUST match the game's platform

        Debug.Log("Bundles -> " + outDir);
    }
}
#endif
```

Assign the bundle name on each asset via the Inspector's **AssetBundle** dropdown at the bottom, or in script with `AssetImporter.GetAtPath(path).assetBundleName = "myui"`.
Docs: [BuildPipeline.BuildAssetBundles](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/BuildPipeline.BuildAssetBundles.html), [BuildAssetBundleOptions](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/BuildAssetBundleOptions.html), [Building AssetBundles](https://docs.unity3d.com/2022.3/Documentation/Manual/AssetBundles-Building.html).

Use `ChunkBasedCompression` (LZ4), **not** the default LZMA — LZMA bundles must be fully decompressed before any asset can be read, which stalls the main thread on `LoadFromMemory`.

#### 3.2 Loading from a MelonLoader IL2CPP mod

**[verified] critical detail:** vanilla `AssetBundle.LoadFromMemory(byte[])` compiles under interop but is unreliable in IL2CPP — this is exactly why MelonLoader ships `UnityEngine.Il2CppAssetBundleManager.dll` ([LavaGang/UnityEngine.Il2CppAssetBundleManager](https://github.com/LavaGang/UnityEngine.Il2CppAssetBundleManager)), which resolves the internal icalls directly:

```csharp
// LavaGang/UnityEngine.Il2CppAssetBundleManager/Il2CppAssetBundleManager.cs
public static Il2CppAssetBundle LoadFromMemory(Il2CppStructArray<byte> binary) => LoadFromMemory(binary, 0u);

public static Il2CppAssetBundle LoadFromMemory(Il2CppStructArray<byte> binary, uint crc)
{
    if (binary == null) throw new System.ArgumentException("The binary cannot be null or empty.");
    if (LoadFromMemory_InternalDelegateField == null)
        throw new System.NullReferenceException("The LoadFromMemory_InternalDelegateField cannot be null.");
    System.IntPtr intPtr = LoadFromMemory_InternalDelegateField(
        IL2CPP.Il2CppObjectBaseToPtrNotNull(binary), crc);
    return ((intPtr != System.IntPtr.Zero) ? new Il2CppAssetBundle(intPtr) : null);
}
```

**Use `Il2CppAssetBundleManager`, not `AssetBundle`.** Both are already referenced if you add `MelonLoader\net6\UnityEngine.Il2CppAssetBundleManager.dll`.

Embedded resource → bundle → prefab:

```csharp
using System.IO;
using System.Reflection;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using UnityEngine;

public static byte[] ReadEmbedded(string resourceName)
{
    using Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
    if (s == null)
    {
        MelonLogger.Error("Missing embedded resource: " + resourceName);
        foreach (string n in Assembly.GetExecutingAssembly().GetManifestResourceNames())
            MelonLogger.Msg("  available: " + n);      // <-- always log this on failure
        return null;
    }
    using MemoryStream ms = new MemoryStream();
    s.CopyTo(ms);
    return ms.ToArray();
}

public static GameObject LoadPrefabFromMemory(byte[] data)
{
    // byte[] converts implicitly to Il2CppStructArray<byte>.
    Il2CppAssetBundle bundle = Il2CppAssetBundleManager.LoadFromMemory(data);
    if (bundle == null) { MelonLogger.Error("LoadFromMemory returned null"); return null; }

    Il2CppStringArray names = bundle.GetAllAssetNames();
    for (int i = 0; i < names.Length; i++) MelonLogger.Msg("  asset: " + names[i]);   // names are LOWERCASE paths

    GameObject prefab = bundle.LoadAsset<GameObject>("assets/ui/mypanel.prefab");
    // Non-generic form when T is awkward:
    // GameObject prefab = bundle.LoadAsset(names[0], Il2CppType.Of<GameObject>()).TryCast<GameObject>();
    return prefab;
}

// Fully explicit Il2CppStructArray construction (equivalent; use if the implicit conversion ever fails):
public static Il2CppAssetBundle LoadExplicit(byte[] data)
{
    Il2CppStructArray<byte> arr = new Il2CppStructArray<byte>(data.Length);
    for (int i = 0; i < data.Length; i++) arr[i] = data[i];
    return Il2CppAssetBundleManager.LoadFromMemory(arr, 0u);
}

// External file in UserData (easier iteration: swap the bundle without rebuilding the DLL)
public static Il2CppAssetBundle LoadFromUserData(string fileName)
{
    string path = Path.Combine(MelonEnvironment.UserDataDirectory, "MyMod", fileName);
    return File.Exists(path) ? Il2CppAssetBundleManager.LoadFromFile(path) : null;
}

// Load everything of a type
public static Sprite[] AllSprites(Il2CppAssetBundle bundle)
{
    Il2CppReferenceArray<Sprite> arr = bundle.LoadAllAssets<Sprite>();
    Sprite[] result = new Sprite[arr.Length];
    for (int i = 0; i < arr.Length; i++) result[i] = arr[i];
    return result;
}
```

**[verified] all of the above compile**, including `MelonEnvironment.UserDataDirectory` / `.ModsDirectory` / `.GameRootDirectory` (from `MelonLoader.Utils`).

Real-world proof of the pattern (Goose, a shipped MelonLoader IL2CPP mod):

```csharp
GameObject val = Il2CppAssetBundleManager
    .LoadFromMemory(Il2CppStructArray<byte>.op_Implicit(
        Utils.GetResource(Assembly.GetExecutingAssembly(), "Goose.goose.assets")))
    .LoadAsset<GameObject>("Goose.prefab");
```
Source: [thunderstore.io/c/hard-bullet/p/korbykob/Goose/source/](https://thunderstore.io/c/hard-bullet/p/korbykob/Goose/source/). Note `Il2CppStructArray<byte>.op_Implicit(...)` — that's the same implicit conversion, called explicitly.

`LoadFromFile` also has a real Schedule I precedent (GDG Traditional Chinese Translation, which sideloads TMP font bundles from disk):
```csharp
Il2CppAssetBundle il2CppAssetBundle = Il2CppAssetBundleManager.LoadFromFile(value);
Il2CppStringArray allAssetNames = il2CppAssetBundle.GetAllAssetNames();
TMP_FontAsset val = il2CppAssetBundle.LoadAsset<TMP_FontAsset>(text);
Il2CppReferenceArray<TMP_FontAsset> val2 = il2CppAssetBundle.LoadAllAssets<TMP_FontAsset>();
```
Source: [thunderstore.io/c/schedule-i/p/GrandDuchyOfGames/GDG_ScheduleI_Traditional_Chinese_Translation/source/](https://thunderstore.io/c/schedule-i/p/GrandDuchyOfGames/GDG_ScheduleI_Traditional_Chinese_Translation/source/).

Related shim: `UnityEngine.Il2CppImageConversionManager` for PNG loading, since `ImageConversion.LoadImage` has the same icall problem:
```csharp
Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
Il2CppImageConversionManager.LoadImage(tex, pngBytes, false);   // [verified] compiles
```

#### 3.3 Shader / material caveats (the pink-material problem)

A bundle built in the Editor references shaders **by asset**, not by name. Two failure modes:

1. **The shader isn't in the bundle** → material's shader resolves to null → Unity substitutes `Hidden/InternalErrorShader` → **magenta**.
2. **The shader IS in the bundle but was compiled with different keywords/variants than the game's build**, or the game is URP (**[verified]** — `Unity.RenderPipelines.Universal.Runtime.dll` is present) and your bundle project was built-in-RP → wrong/broken rendering.

Fix: after loading, walk the prefab and re-point every material at a shader/material that exists **in the game's build**.

```csharp
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.UI;

public static void RepairMaterials(GameObject prefab, Material knownGoodUiMaterial)
{
    Il2CppArrayBase<Graphic> graphics = prefab.GetComponentsInChildren<Graphic>(true);
    foreach (Graphic g in graphics)
    {
        Material m = g.material;
        if (m == null || m.shader == null || m.shader.name == "Hidden/InternalErrorShader")
        {
            g.material = knownGoodUiMaterial;      // harvested from a live game Graphic
            continue;
        }
        Shader replacement = Shader.Find(m.shader.name);   // resolves against the GAME's shader set
        if (replacement != null) m.shader = replacement;
    }
}
```
**[verified] compiles.** Get `knownGoodUiMaterial` from a live game `Image` (`someGameImage.material`) or `Shader.Find("UI/Default")`. Docs: [Shader.Find](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Shader.Find.html).

**TMP font assets in bundles are the worst case.** TMP keeps its default font, shaders and `TMP_Settings` in `Resources`, which never share identity across builds — so a bundled `TMP_FontAsset` arrives with a broken/duplicated material and TMP's `Shader.Find` inside `CreateFontAsset` doesn't see bundle shaders ([Unity Discussions: TMP + AssetBundles](https://discussions.unity.com/t/using-textmeshpro-with-assetbundles-and-runtime-tmp_fontasset-createfontasset/825012), [TMP + Addressables](https://discussions.unity.com/t/textmeshpro-addressables-asset-bundles/855482)). The fix that a shipped Schedule I mod actually uses — clone a *game* TMP material and re-point it at your atlas:

```csharp
// Pattern from GDG_ScheduleI_Traditional_Chinese_Translation (decompiled), cleaned up.
public static void RepairBundledFont(TMP_FontAsset font)
{
    if (font.material != null) { font.ReadFontAssetDefinition(); return; }

    Material donor = null;
    foreach (TMP_FontAsset f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
        if (f != null && f.material != null && f != font) { donor = f.material; break; }
    if (donor == null || font.atlasTexture == null) return;

    Material m = Object.Instantiate(donor);
    m.name        = font.name + " Material";
    m.mainTexture = font.atlasTexture;
    m.SetFloat(Shader.PropertyToID("_TextureWidth"),  font.atlasTexture.width);
    m.SetFloat(Shader.PropertyToID("_TextureHeight"), font.atlasTexture.height);
    m.SetFloat(Shader.PropertyToID("_GradientScale"), font.atlasPadding + 1);
    font.material = m;
    font.ReadFontAssetDefinition();
}
```
**[verified] compiles.** This is the single strongest argument for §2.2: **don't bundle a font, harvest the game's.**

#### 3.4 Embedding the bundle in the DLL

```xml
<ItemGroup>
  <EmbeddedResource Include="Resources\myui.bundle" LogicalName="MyMod.Resources.myui.bundle" />
</ItemGroup>
```

Without `LogicalName`, the generated name is `<RootNamespace>.<folder path with '/' replaced by '.'>.<filename>` — e.g. `MyMod.Resources.myui.bundle`. Setting `LogicalName` explicitly removes all guesswork. Always log `Assembly.GetExecutingAssembly().GetManifestResourceNames()` the first time.

**File vs embedded:** embed for release (single-DLL install, no path bugs, Thunderstore-friendly); load from `UserData\MyMod\` during development so you can rebuild the bundle without rebuilding the mod.

---

### 4. Reference material

**Primary docs**
- Unity 2022.3 Scripting API — [`Sprite.Create`](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Sprite.Create.html) (confirmed the 8-arg `border`/`SpriteMeshType` overload exists in 2022.3), [`AssetBundle.LoadFromMemory`](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/AssetBundle.LoadFromMemory.html), [`Texture2D.SetPixels`](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Texture2D.SetPixels.html), [`Resources.FindObjectsOfTypeAll`](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Resources.FindObjectsOfTypeAll.html), [`Shader.Find`](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Shader.Find.html), [`AudioSource.PlayOneShot`](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/AudioSource.PlayOneShot.html), [`BuildPipeline.BuildAssetBundles`](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/BuildPipeline.BuildAssetBundles.html), [`BuildAssetBundleOptions`](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/BuildAssetBundleOptions.html)
- uGUI package API — [`Image`](https://docs.unity3d.com/Packages/com.unity.ugui@1.0/api/UnityEngine.UI.Image.html) (incl. `pixelsPerUnitMultiplier`), [`Image.Type`](https://docs.unity3d.com/Packages/com.unity.ugui@1.0/api/UnityEngine.UI.Image.Type.html), [`CanvasScaler`](https://docs.unity3d.com/Packages/com.unity.ugui@1.0/api/UnityEngine.UI.CanvasScaler.html), [`ScrollRect`](https://docs.unity3d.com/Packages/com.unity.ugui@1.0/api/UnityEngine.UI.ScrollRect.html), [`RectMask2D`](https://docs.unity3d.com/Packages/com.unity.ugui@1.0/api/UnityEngine.UI.RectMask2D.html), [`ContentSizeFitter`](https://docs.unity3d.com/Packages/com.unity.ugui@1.0/api/UnityEngine.UI.ContentSizeFitter.html), [`LayoutRebuilder`](https://docs.unity3d.com/Packages/com.unity.ugui@1.0/api/UnityEngine.UI.LayoutRebuilder.html), [`EventSystem`](https://docs.unity3d.com/Packages/com.unity.ugui@1.0/api/UnityEngine.EventSystems.EventSystem.html)
- Unity manual — [Canvas Scaler](https://docs.unity3d.com/2022.3/Documentation/Manual/script-CanvasScaler.html), [Multi-resolution UI](https://docs.unity3d.com/2022.3/Documentation/Manual/HOWTO-UIMultiResolution.html), [9-slicing](https://docs.unity3d.com/2022.3/Documentation/Manual/9SliceSprites.html), [Auto Layout](https://docs.unity3d.com/2022.3/Documentation/Manual/UIAutoLayout.html), [AssetBundle fundamentals](https://docs.unity3d.com/2022.3/Documentation/Manual/AssetBundles-Native.html)
- TextMeshPro — [manual](https://docs.unity3d.com/Packages/com.unity.textmeshpro@3.0/manual/index.html), [`TMP_Text`](https://docs.unity3d.com/Packages/com.unity.textmeshpro@3.0/api/TMPro.TMP_Text.html), [`TMP_FontAsset`](https://docs.unity3d.com/Packages/com.unity.textmeshpro@3.0/api/TMPro.TMP_FontAsset.html)
- Input System UI — [UI support](https://docs.unity3d.com/Packages/com.unity.inputsystem@1.7/manual/UISupport.html)

**Modding-framework docs**
- [MelonLoader wiki](https://melonwiki.xyz/) and [IL2CPP differences page](https://github.com/LavaGang/MelonWiki/blob/master/docs/modders/il2cppdifferences.md) — canonical source for `RegisterTypeInIl2Cpp`, `IntPtr` ctor rules, `MelonCoroutines`, `Il2CppType.Of<T>()`
- [Il2CppInterop](https://github.com/BepInEx/Il2CppInterop) + [Documentation/](https://github.com/BepInEx/Il2CppInterop/tree/master/Documentation), especially [Class-Injection.md](https://github.com/BepInEx/Il2CppInterop/blob/master/Documentation/Class-Injection.md)
- [Schedule I Modding Wiki — Il2Cpp Modding](https://s1modding.github.io/docs/moddevs/il2cpp/) — game-specific; documents the `(UnityAction)(() => {})` cast, `Il2CppScheduleOne.*` namespaces, `._items` LINQ workaround, "no IEnumerators in injected types"
- [LavaGang/UnityEngine.Il2CppAssetBundleManager](https://github.com/LavaGang/UnityEngine.Il2CppAssetBundleManager) — the ML AssetBundle shim
- [MelonLoader PR #1122](https://github.com/LavaGang/MelonLoader/pull/1122) — AssetBundle fixes, validated on **Unity 2022.3.62f2**

**Real repos quoted in this doc**
1. **[sinai-dev/UniverseLib](https://github.com/sinai-dev/UniverseLib)** (the runtime-UI layer under [UnityExplorer](https://github.com/sinai-dev/UnityExplorer); the single best reference for building uGUI at runtime in IL2CPP) — [`UIFactory.cs`](https://github.com/sinai-dev/UniverseLib/blob/main/src/UI/UIFactory.cs), [`UIBase.cs`](https://github.com/sinai-dev/UniverseLib/blob/main/src/UI/UIBase.cs), [`CursorUnlocker.cs`](https://github.com/sinai-dev/UniverseLib/blob/main/src/Input/CursorUnlocker.cs), [`EventSystemHelper.cs`](https://github.com/sinai-dev/UniverseLib/blob/main/src/Input/EventSystemHelper.cs), [`Il2CppProvider.cs`](https://github.com/sinai-dev/UniverseLib/blob/main/src/Runtime/Il2Cpp/Il2CppProvider.cs). Quoted above: the `Il2CppExtensions.AddListener(UnityEvent, Action)` shim, and the canvas setup (`sortingOrder = 30000`, `overrideSorting = true`, `referenceResolution 1920×1080`, `ScreenMatchMode.Expand`).
2. **[LavaGang/UnityEngine.Il2CppAssetBundleManager](https://github.com/LavaGang/UnityEngine.Il2CppAssetBundleManager/blob/master/Il2CppAssetBundleManager.cs)** — quoted above: the real `LoadFromMemory(Il2CppStructArray<byte>, uint)` implementation showing why the shim exists.
3. **[tiagovitorino97/SimpleLabels](https://github.com/tiagovitorino97/SimpleLabels/blob/main/LabelMod.cs)** — a shipped Schedule I IL2CPP MelonLoader mod that builds runtime uGUI (`TMP_InputField`, `TextMeshProUGUI`, `RectTransform` anchors, `Il2CppTMPro`/`Il2CppScheduleOne.*` usings, `TextAlignmentOptions`, `enableWordWrapping`).
4. **[HonestMainMenu (Thunderstore, decompiled)](https://thunderstore.io/c/schedule-i/p/Roachified/HonestMainMenu/source/)** — the clone-a-real-menu-button reference implementation for this exact game; quoted above.
5. **[GDG ScheduleI Traditional Chinese Translation (Thunderstore, decompiled)](https://thunderstore.io/c/schedule-i/p/GrandDuchyOfGames/GDG_ScheduleI_Traditional_Chinese_Translation/source/)** — the TMP-font-from-bundle + donor-material repair reference for this exact game; quoted above.
6. **[NomadWithoutAHome gist — Schedule I menu mod](https://gist.github.com/NomadWithoutAHome/1fc4a18624eb42edff36e7551776ffd4)** — plain from-scratch `CreatePanel`/`CreateButton`/`CreateText` helpers for Schedule I; useful as a minimal skeleton, but hardcodes all styling (exactly what we're avoiding).

---

## Sources (C2)

- https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Sprite.Create.html — Unity official, 2022.3 branch, page dated 2026-07-02. Authoritative; confirmed the `border`/`SpriteMeshType` overload.
- https://docs.unity3d.com/2022.3/Documentation/ScriptReference/AssetBundle.LoadFromMemory.html — Unity official, 2022.3, 2026-07-02. Authoritative.
- https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Texture2D.SetPixels.html — Unity official, 2022.3. Authoritative.
- https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Resources.FindObjectsOfTypeAll.html — Unity official, 2022.3. Authoritative.
- https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Shader.Find.html — Unity official, 2022.3. Authoritative.
- https://docs.unity3d.com/2022.3/Documentation/ScriptReference/AudioSource.PlayOneShot.html — Unity official, 2022.3. Authoritative.
- https://docs.unity3d.com/2022.3/Documentation/ScriptReference/BuildPipeline.BuildAssetBundles.html — Unity official, 2022.3. Authoritative.
- https://docs.unity3d.com/2022.3/Documentation/ScriptReference/BuildAssetBundleOptions.html — Unity official, 2022.3. Authoritative.
- https://docs.unity3d.com/2022.3/Documentation/ScriptReference/BuildTarget.html — Unity official, 2022.3. Authoritative.
- https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Texture2D-ctor.html — Unity official, 2022.3. Authoritative.
- https://docs.unity3d.com/2022.3/Documentation/Manual/script-CanvasScaler.html — Unity official manual, 2022.3. Authoritative.
- https://docs.unity3d.com/2022.3/Documentation/Manual/HOWTO-UIMultiResolution.html — Unity official manual, 2022.3. Authoritative.
- https://docs.unity3d.com/2022.3/Documentation/Manual/9SliceSprites.html — Unity official manual, 2022.3. Authoritative.
- https://docs.unity3d.com/2022.3/Documentation/Manual/UIAutoLayout.html — Unity official manual, 2022.3. Authoritative.
- https://docs.unity3d.com/2022.3/Documentation/Manual/AssetBundles-Building.html — Unity official manual, 2022.3. Authoritative.
- https://docs.unity3d.com/2022.3/Documentation/Manual/AssetBundles-Native.html — Unity official manual, 2022.3. Authoritative.
- https://docs.unity3d.com/Packages/com.unity.ugui@1.0/api/UnityEngine.UI.Image.html — Unity package API. Authoritative; `pixelsPerUnitMultiplier` lives here (the 2022.3 ScriptReference URL 404s).
- https://docs.unity3d.com/Packages/com.unity.ugui@1.0/api/UnityEngine.UI.Image.Type.html — Unity package API. Authoritative.
- https://docs.unity3d.com/Packages/com.unity.ugui@1.0/api/UnityEngine.UI.CanvasScaler.html — Unity package API. Authoritative.
- https://docs.unity3d.com/Packages/com.unity.ugui@1.0/api/UnityEngine.UI.ScrollRect.html — Unity package API. Authoritative.
- https://docs.unity3d.com/Packages/com.unity.ugui@1.0/api/UnityEngine.UI.RectMask2D.html — Unity package API. Authoritative.
- https://docs.unity3d.com/Packages/com.unity.ugui@1.0/api/UnityEngine.UI.ContentSizeFitter.html — Unity package API. Authoritative.
- https://docs.unity3d.com/Packages/com.unity.ugui@1.0/api/UnityEngine.UI.LayoutRebuilder.html — Unity package API. Authoritative.
- https://docs.unity3d.com/Packages/com.unity.ugui@1.0/api/UnityEngine.EventSystems.EventSystem.html — Unity package API. Authoritative.
- https://docs.unity3d.com/Packages/com.unity.textmeshpro@3.0/manual/index.html — Unity TMP 3.0 manual (the TMP major used by Unity 2022.3). Authoritative.
- https://docs.unity3d.com/Packages/com.unity.textmeshpro@3.0/api/TMPro.TMP_Text.html — Unity TMP 3.0 API. Authoritative.
- https://docs.unity3d.com/Packages/com.unity.textmeshpro@3.0/api/TMPro.TMP_FontAsset.html — Unity TMP 3.0 API. Authoritative.
- https://docs.unity3d.com/Packages/com.unity.inputsystem@1.7/manual/UISupport.html — Unity Input System manual. Authoritative for `InputSystemUIInputModule`.
- https://unity.com/releases/editor/archive — Unity official; where to get exactly 2022.3.62f2. Current.
- https://melonwiki.xyz/ — MelonLoader official wiki. Authoritative for ML; live as of 2026-08.
- https://github.com/LavaGang/MelonWiki/blob/master/docs/modders/il2cppdifferences.md — MelonLoader official wiki source. Authoritative; the canonical `RegisterTypeInIl2Cpp` / `IntPtr` ctor / `DerivedConstructorPointer` reference.
- https://github.com/LavaGang/MelonLoader — MelonLoader source, 4k stars. Authoritative.
- https://github.com/LavaGang/MelonLoader/pull/1122 — merged 2026-03-26; AssetBundle icall fixes explicitly tested on Unity **2022.3.62f2**. Highly relevant, very recent.
- https://github.com/LavaGang/MelonLoader/issues/912 — explains why `TMPro` becomes `Il2CppTMPro`. Authoritative (maintainer repo).
- https://github.com/LavaGang/UnityEngine.Il2CppAssetBundleManager/blob/master/Il2CppAssetBundleManager.cs — MelonLoader-org source of the AssetBundle shim. Authoritative; code quoted verbatim.
- https://github.com/BepInEx/Il2CppInterop — the interop layer itself (v1.5.0 ships with ML 0.7.1). Authoritative.
- https://github.com/BepInEx/Il2CppInterop/tree/master/Documentation — official interop docs index. Authoritative.
- https://github.com/BepInEx/Il2CppInterop/blob/master/Documentation/Class-Injection.md — official; simple vs extended injection cases. Authoritative.
- https://s1modding.github.io/docs/moddevs/il2cpp/ — Schedule I Modding Wiki, last updated 2025-07-07. Community but game-specific and accurate; source of the `(UnityAction)(() => {})` pattern and the "no IEnumerators in injected types" rule.
- https://github.com/sinai-dev/UniverseLib — runtime-UI library used by UnityExplorer; the highest-quality open-source example of building uGUI under Il2CppInterop. Community, widely used, actively referenced.
- https://github.com/sinai-dev/UniverseLib/blob/main/src/UI/UIFactory.cs — scroll view / layout / button factory patterns. Community, quoted.
- https://github.com/sinai-dev/UniverseLib/blob/main/src/UI/UIBase.cs — canvas + sortingOrder 30000 pattern. Community, quoted.
- https://github.com/sinai-dev/UniverseLib/blob/main/src/Input/CursorUnlocker.cs — Harmony-patching `Cursor.lockState`/`visible` setters. Community, quoted.
- https://github.com/sinai-dev/UniverseLib/blob/main/src/Input/EventSystemHelper.cs — swap/restore `EventSystem.current` + input module. Community, quoted.
- https://github.com/sinai-dev/UniverseLib/blob/main/src/Runtime/Il2Cpp/Il2CppProvider.cs — `Il2CppExtensions.AddListener(UnityEvent, Action)`, `AddComponent(Il2CppType.From(type)).TryCast<T>()`. Community, quoted.
- https://github.com/sinai-dev/UnityExplorer — the consumer of UniverseLib; useful as a full working IL2CPP runtime-UI application. Community, widely trusted.
- https://github.com/tiagovitorino97/SimpleLabels — shipped Schedule I IL2CPP MelonLoader mod building runtime uGUI/TMP. Community, game-specific.
- https://github.com/tiagovitorino97/SimpleLabels/blob/main/LabelMod.cs — the actual UI construction code. Community, quoted.
- https://thunderstore.io/c/schedule-i/p/Roachified/HonestMainMenu/source/ — decompiled source of a shipped Schedule I mod; the clone-a-menu-button reference. Community, game-specific, decompiled (so formatting is IL-ish but semantics are real).
- https://thunderstore.io/c/schedule-i/p/GrandDuchyOfGames/GDG_ScheduleI_Traditional_Chinese_Translation/source/ — decompiled source; `Resources.FindObjectsOfTypeAll<TMP_FontAsset>()`, `Il2CppAssetBundleManager.LoadFromFile`, donor-material repair. Community, game-specific, decompiled.
- https://thunderstore.io/c/schedule-i/p/MedicalMess/MultiplayerPlus/source/ — decompiled source; runtime `TMP_InputField`/`TextMeshProUGUI` construction under Il2Cpp. Community, game-specific, decompiled.
- https://thunderstore.io/c/hard-bullet/p/korbykob/Goose/source/ — decompiled MelonLoader IL2CPP mod; `Il2CppAssetBundleManager.LoadFromMemory(Il2CppStructArray<byte>.op_Implicit(...))`. Community, not Schedule I but same loader/interop stack.
- https://gist.github.com/NomadWithoutAHome/1fc4a18624eb42edff36e7551776ffd4 — Schedule I MelonLoader menu mod gist; minimal from-scratch uGUI skeleton. Community, unversioned, treat as a sketch.
- https://discussions.unity.com/t/using-textmeshpro-with-assetbundles-and-runtime-tmp_fontasset-createfontasset/825012 — Unity forum; TMP `Shader.Find` fails for bundle shaders. Community, but the failure mode is well corroborated.
- https://discussions.unity.com/t/textmeshpro-addressables-asset-bundles/855482 — Unity forum; TMP's `Resources` coupling duplicates/breaks font assets in bundles. Community, corroborated.
- https://github.com/bbepis/XUnity.AutoTranslator/wiki/TextMeshPro-Font-Asset-Creation-&-Packaging-Guide — practical TMP-font-into-AssetBundle workflow. Community, mature project.
- https://stackoverflow.com/questions/79496143/how-to-use-methods-from-registertypeinil2cpp-in-melonloader — 2025-03; illustrates the injected-component instantiation footgun. Community, unanswered — low weight, listed for completeness.
- https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/proposals/csharp-10.0/lambda-improvements — Microsoft official; lambda *natural types* in C# 10, which is why `(UnityAction)(() => {})` compiles on net6. Authoritative.

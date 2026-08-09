# Schedule I — Custom Character / NPC Authoring

External research for three planned mods (driver employees, federal agents, special-customer groups: bikers / hippies / businessmen).
Date: **2026-08-03**. Game: Schedule I (TVGS), Unity **2022.3.62f2**, IL2CPP, MelonLoader 0.7.x, S1API.Forked 3.1.4, `net6`.

**Evidence labels used throughout**

| Label | Meaning |
| --- | --- |
| **[verified]** | Confirmed by me from the local game install (`Il2CppAssemblies`, `globalgamemanagers` string tables) or read directly in published source code. |
| **[community]** | From a wiki, mod description, or community documentation. Plausible but not independently confirmed. |
| **[inference]** | My reasoning from the above. Not directly stated by any source. |
| `⚠ unverified` | A name or API I could **not** confirm. Do not ship code against it without checking. |

Raw listings generated for this document live in `research-ext\raw\`:
`il2cpp-assemblies-listing.txt`, `shaders-globalgamemanagers.txt`, `avatarframework-types.txt`, `clothing-types.txt`, `s1api-appearance-path-catalog.txt`, `strings-avatar-tokens.txt`, plus downloaded source/docs prefixed `s1api_`, `s1apiexample_`, `bigwilly_`.

---

## 0. Executive summary

1. **The game is URP** (Universal Render Pipeline), forward+deferred hybrid, no HDRP anywhere. **[verified]**
2. **You do not need to author any 3D art.** Schedule I has a first-class, fully data-driven character system — `ScheduleOne.AvatarFramework` — where an NPC's entire look is a `ScriptableObject` called `AvatarSettings` holding paths + colors. **[verified]**
3. **Clothing is mostly texture layers painted onto one shared body mesh**, not geometry. Hats/glasses/shoes/vests/blazers *are* geometry (`Accessory` prefabs skinned to the shared rig). **[verified]**
4. **Recommendation: Option A** — build `AvatarSettings` from existing in-game layer/accessory paths, via S1API's `NPCPrefabBuilder.WithAppearanceDefaults(...)`. Zero art assets, native look, update-resilient.
5. **Multiple shipped mods already add NPCs this way**, including officially endorsed ones with public MIT source. This is a solved problem with copyable reference code.

---

## 1. How Schedule I characters are visually constructed

### 1.1 Render pipeline — Universal Render Pipeline (URP) **[verified]**

Determined from the local install by listing `...\Schedule I\MelonLoader\Il2CppAssemblies\` (full listing: `research-ext\raw\il2cpp-assemblies-listing.txt`):

```
Unity.RenderPipelines.Core.Runtime.dll            1,577,472
Unity.RenderPipelines.Universal.Runtime.dll       2,330,624
Unity.RenderPipeline.Universal.ShaderLibrary.dll     11,264
```

There is **no** `Unity.RenderPipelines.HighDefinition.*` and **no** `Unity.RenderPipelines.HighDefinition.Config.*`. Conclusion: **URP, not HDRP, not Built-in.** **[verified]**

Corroborating third-party packages in the same folder, all of which are URP-only or URP-first assets **[verified]**:

| Assembly | Package | Relevance |
| --- | --- | --- |
| `Il2CppAmplifyImpostors.Runtime.dll` | Amplify Impostors | Distance billboards — used for avatars (see §1.9) |
| `Il2Cppsc.stylizedwater2.runtime.dll` | Stylized Water 2 (Staggart) | URP-only |
| `Il2CppVisualDesignCafe.Nature.dll` | Nature Renderer | Vegetation |
| `Il2CppCorgiGodRays.dll` | Corgi God Rays | URP post FX |
| `Il2CppRootMotion.dll` | Final IK | Avatar IK (`AvatarIKController`, `BodyIK`) |
| `Il2CppHSVPicker.dll` | HSV Color Picker | The clothing / character-creator colour wheel |
| `Il2CppAstarPathfindingProject.dll` | A* Pathfinding Project | NPC navigation |
| `Il2CppFishNet.Runtime.dll` | FishNet | NPC networking / spawning |

### 1.2 Shader family **[verified]**

Extracted from `globalgamemanagers` (full list: `research-ext\raw\shaders-globalgamemanagers.txt`). The relevant entries:

```
Universal Render Pipeline/Lit
Universal Render Pipeline/Simple Lit
Universal Render Pipeline/Unlit
Shader Graphs/CombinedAvatar              <-- the avatar shader
Shader Graphs/TextureCombine_6x
Shader Graphs/TextureCombine_3x_Transparent
Custom/InstancedLitBase_Deferred
Custom/ScheduleOneFog
Hidden/Amplify Impostors/Octahedron Impostor URP
Hidden/Outline , Hidden/OutlineMask , Hidden/EPO/Fill/...
```

Key readings:

- **`Shader Graphs/CombinedAvatar` is the avatar shader.** Name matches `Avatar.UseCombinedLayer` / `Avatar.usingCombinedLayer` / `AvatarLayer.CombinedMaterial` / `AvatarSettings.CombinedLayer` in the assembly. **[verified]** The two `TextureCombine_*` graphs are almost certainly the layer-flattening step that bakes 3–6 clothing textures into one material. **[inference]**
- **Characters are LIT, not unlit and not cel-shaded.** There is no toon/cel/ramp shader in the project, and `Avatar` exposes `static float DEFAULT_SMOOTHNESS` — a PBR smoothness term. Anyone expecting a *Jet Set Radio* flat-shaded look is wrong; it's stylized-but-PBR. **[verified]**
- **The outline shaders are UI interaction highlights, not art style.** `Hidden/Outline`, `Hidden/OutlineMask` and the `Hidden/EPO/Fill/*` family are *Easy Performant Outline* (EPO) — the asset store package that draws the selection glow when you look at an interactable. Characters have **no** rim light or ink outline as a baseline. **[verified]** for the shader identity; **[inference]** that it is used only for interaction highlighting.
- Post FX stack is heavy and does a lot of the "look": `Hidden/Kronnect/Beautify` (Beautify), `Hidden/VolumetricFog2/*` (Volumetric Fog & Mist 2), `Custom/ScheduleOneFog`, Corgi God Rays, VLB volumetric light beams, URP Bloom/UberPost. The saturated, hazy, slightly-blown-out look is post-processing, not the character materials. **[verified]** shaders exist; **[inference]** on their contribution.

### 1.3 The architecture that actually matters: `ScheduleOne.AvatarFramework` **[verified]**

I confirmed this namespace by grepping the shipped interop assembly directly:

```powershell
rg -a -o "ScheduleOne\.[A-Za-z0-9_.]*Avatar[A-Za-z0-9_.]*" "Assembly-CSharp.dll"
```

```
ScheduleOne.AvatarFramework
ScheduleOne.AvatarFramework.Animation
ScheduleOne.AvatarFramework.Avatar
ScheduleOne.AvatarFramework.AvatarEffects
ScheduleOne.AvatarFramework.Customization
ScheduleOne.AvatarFramework.Customization.CharacterCreator
ScheduleOne.AvatarFramework.Emotions
ScheduleOne.AvatarFramework.Equipping
ScheduleOne.AvatarFramework.EyeController
ScheduleOne.AvatarFramework.Impostors
ScheduleOne.AvatarFramework.MugshotGenerator
```

Full type/member listing: `research-ext\raw\avatarframework-types.txt`. The load-bearing types:

#### `AvatarSettings : ScriptableObject` — the complete look of one character **[verified]**

```csharp
public class AvatarSettings : ScriptableObject
{
    Color  SkinColor;
    float  Height;                    // applied to transform.localScale
    float  Gender;                    // 0 = male, 1 = female  (blendshape)
    float  Weight;                    // 0..1                  (blendshape * 100)
    string HairPath;                  // Resources path
    Color  HairColor;
    float  EyebrowScale, EyebrowThickness, EyebrowRestingHeight, EyebrowRestingAngle;
    Color  LeftEyeLidColor, RightEyeLidColor;
    Eye.EyeLidConfiguration LeftEyeRestingState, RightEyeRestingState;   // {topLidOpen, bottomLidOpen}
    string EyeballMaterialIdentifier;
    Color  EyeBallTint;
    float  PupilDilation;

    List<LayerSetting>     FaceLayerSettings;   // struct { string layerPath; Color layerTint; }
    List<LayerSetting>     BodyLayerSettings;
    List<AccessorySetting> AccessorySettings;   // class  { string path;      Color color;    }

    bool        UseCombinedLayer;
    AvatarLayer CombinedLayer;
    Texture2D   ImpostorTexture;

    // convenience accessors: FaceLayer1..6 Path/Color, BodyLayer1..8 Path/Color, Accessory1..9 Path/Color
    string GetJson(bool prettyPrint = true);
}
```

Two things to notice. First, **the whole character is serializable data** — paths and colors, nothing else. Second, `GetJson()` exists, which means TVGS themselves treat avatar configs as portable data.

#### `Avatar : MonoBehaviour` — the applier **[verified]**

```csharp
public class Avatar : MonoBehaviour
{
    static int   MAX_ACCESSORIES;
    static bool  CombinedLayersEnabled;
    static float DEFAULT_SMOOTHNESS;
    static float maleShoulderScale, femaleShoulderScale;

    SkinnedMeshRenderer[] BodyMeshes;      // the shared body
    SkinnedMeshRenderer[] ShapeKeyMeshes;  // blendshape targets
    SkinnedMeshRenderer   FaceMesh;
    Transform Armature, HeadBone, HipBone, LeftShoulder, RightShoulder,
              LeftFootBone, RightFootBone, MiddleSpine, LowerSpine, LowestSpine;
    Rigidbody[] RagdollRBs;  Collider[] RagdollColliders;
    EyeController Eyes;  EyebrowController EyeBrows;
    Impostors.AvatarImpostor Impostor;
    Material DefaultAvatarMaterial;
    AvatarSettings CurrentSettings { get; set; }

    void LoadAvatarSettings(AvatarSettings settings);   // full apply
    void ApplyBodySettings(AvatarSettings s);
    void ApplyBodyLayerSettings(AvatarSettings s, int maxOrder = -1);
    void ApplyFaceLayerSettings(AvatarSettings s);
    void ApplyAccessorySettings(AvatarSettings s);
    void ApplyHairSettings(AvatarSettings s);
    void ApplyHairColorSettings(AvatarSettings s);
    void ApplyEyeBallSettings(AvatarSettings s);
    void ApplyEyeLidSettings(AvatarSettings s);
    void ApplyEyeLidColorSettings(AvatarSettings s);
    void ApplyEyebrowSettings(AvatarSettings s);
    void ApplyShapeKeys(float gender, float weight);
    void SetSkinColor(Color color);
    void SetBodyLayer(int index, string assetPath, Color color);
    void SetFaceLayer(int index, string assetPath, Color color);
    void SetFaceTexture(Texture2D tex, Color color);     // <-- runtime texture injection
    void SetEmission(Color color);
    void OverrideHairColor(Color color);  void ResetHairColor();
    void SetHairVisible(bool visible);    void SetVisible(bool vis);
    void GetMugshot(Action<Texture2D> callback);
    bool IsMale();  bool IsWhite();
}
```

### 1.4 The single most important fact: **layers are textures, accessories are meshes** **[verified]**

```csharp
public class AvatarLayer : ScriptableObject
{
    string    Name;
    string    AssetPath;
    Texture2D Texture;                 // albedo
    Texture2D Normal;                  // normal map
    Texture2D Normal_DefaultImportType;
    int       Order;                   // stacking order
    Material  CombinedMaterial;
}
public class FaceLayer : AvatarLayer { }
```

```csharp
public class Accessory : MonoBehaviour
{
    string Name;
    string AssetPath;
    bool   ReduceFootSize;   float FootSizeReduction;
    bool   ShouldBlockHair;                  // hats hide hair
    bool   ColorAllMeshes;
    MeshRenderer[]        meshesToColor;
    SkinnedMeshRenderer[] skinnedMeshesToColor;
    SkinnedMeshRenderer[] skinnedMeshesToBind;   // bound to the avatar's bones
    SkinnedMeshRenderer[] shapeKeyMeshRends;     // follows body blendshapes

    void ApplyColor(Color col);
    void ApplyShapeKeys(float gender, float weight);
    void BindBones(Transform[] bones);
}
public class Hair : Accessory { bool BlockedByHat; GameObject[] hairToHide; }
public class PoliceBelt : Accessory { GameObject BatonObject, TaserObject, GunObject; }
```

So:

| Category | Implementation | Examples |
| --- | --- | --- |
| Shirts, pants, tattoos, face expressions, freckles, chest hair, stubble | **Texture layer** stacked by `Order` on the shared body/face mesh, tinted per-layer | `Avatar/Layers/Top/T-Shirt`, `Avatar/Layers/Bottom/Jeans`, `Avatar/Layers/Face/Face_Smile` |
| Hats, glasses, shoes, blazers, vests, aprons, belts, skirts, gold chains, hair | **`Accessory` prefab** with `SkinnedMeshRenderer`s bound to the shared rig, tinted via `ApplyColor` | `Avatar/Accessories/Head/Cap/Cap`, `Avatar/Accessories/Chest/Blazer/Blazer` |
| Body shape | **Blendshapes** driven by `ApplyShapeKeys(gender, weight)` | — |
| Height | Direct `transform.localScale` | — |

This is why every NPC in the game reads as the same species: **there is exactly one body mesh and one rig.** Silhouette variation comes from accessories, blendshapes and scale only. **[inference, strongly supported]**

### 1.5 Polygon budget and texture resolution

- **Polygon budget: [inference].** Single shared skinned body + single face mesh + separately-skinned accessory meshes, with `LODGroup` support (`AvatarLODBoundsUpdater` collects `LODGroup`s per avatar) and Amplify Impostor billboards for distance. The visual style — chunky limbs, no fingers modelled separately, flat faces with billboard eyes — reads as roughly **1.5k–4k triangles for the body**, low hundreds for a hat. I did **not** measure this; treat as an estimate. To confirm, run AssetRipper (already downloaded at `tools\AssetRipper`) on `sharedassets0.assets` and read the mesh vertex counts.
- **Texture resolution: [inference].** `sharedassets0.assets` contains 1,397 `Texture2D` objects but the local AssetStudio info dump did not record dimensions. Given that 6 body layers get composited per character at runtime, per-layer albedo is very likely **512×512 or 1024×1024**, not 2K+. **Unconfirmed — measure before authoring.**
- **Confirmed structural facts** **[verified]**: each layer ships **albedo + normal map** (`AvatarLayer.Texture` / `.Normal`), so hand-painted flat colour alone will look wrong next to shipped clothing; you need at least a fabric-fold normal.

### 1.6 Eye and face treatment **[verified]**

Faces are **not** modelled. They are a flat `FaceMesh` with a stack of `FaceLayer` textures (mouth/expression, freckles, wrinkles, tired eyes, tattoos, facial hair), plus **separate 3D eye objects**:

```csharp
public class Eye : MonoBehaviour
{
    Transform Container, TopLidContainer, BottomLidContainer, PupilContainer;
    MeshRenderer TopLidRend, BottomLidRend, EyeBallRend;
    SkinnedMeshRenderer PupilRend;
    OptimizedLight EyeLight;
    struct EyeLidConfiguration { float topLidOpen; float bottomLidOpen; }
    void Blink(...); void LookAt(Vector3 position, bool instant = false);
    void SetDilation(float dil); void SetEyeballColor(Color col, float emission = 0.115f, ...);
    void SetLidColor(Color color); void SetSize(float size);
}
public class EyeController { float eyeSpacing, eyeHeight, eyeSize; bool BlinkingEnabled; float blinkInterval, blinkDuration; ... }
public class Eyebrow { void SetColor(); void SetScale(); void SetThickness(); void SetRestingAngle(); void SetRestingHeight(); }
```

Eyeballs are real geometry with **their own tiny light** (`EyeLight`, default emission `0.115`) — that's the characteristic glassy dot. Eyelids are separate meshes tinted to skin colour. Eyebrows are two quads with scale/thickness/angle/height parameters. Expression = swapping the mouth face-layer texture, driven by `AvatarEmotionManager` / `AvatarEmotionPreset` with `Lerp` between presets. **[verified]**

Practical consequence: **facial identity is authored almost entirely by picking a `Face_*` layer and tuning four eyebrow floats plus two eyelid floats.** That is very cheap to do well and very easy to get uncanny.

### 1.7 Colour palette **[verified] enum, [inference] hexes**

`ScheduleOne.Clothing.EClothingColor` is a fixed 27-value palette — this is the game's canonical clothing palette:

```
White, LightGrey, DarkGrey, Charcoal, Black,
LightRed, Red, Crimson, Orange, Tan, Brown, Coral, Beige,
Yellow, Lime, LightGreen, DarkGreen,
Cyan, SkyBlue, Blue, DeepBlue, Navy,
DeepPurple, Purple, Magenta, BrightPink, HotPink
```

Actual RGB values live in `ClothingUtility.ColorDataList` (a serialized `List<ColorData{ EClothingColor ColorType; Color ActualColor; Color LabelColor; }>`), which I could not read statically.

**However**, three exact colour literals in the officially-endorsed BigWillyMod source match CSS/X11 named colours precisely:

| BigWilly source | Hex | CSS name | `EClothingColor` member |
| --- | --- | --- | --- |
| `new Color(0.863f, 0.078f, 0.235f)` | `#DC143C` | crimson | `Crimson` |
| `new Color(0.824f, 0.706f, 0.549f)` | `#D2B48C` | tan | `Tan` |
| `new Color(0f, 0f, 0.502f)` | `#000080` | navy | `Navy` |

3 for 3. **[inference, high confidence]** the whole `EClothingColor` table is CSS/X11 named colours. Sticking to those hexes will make a custom NPC's clothing sit in exactly the same colour space as shipped clothing. Verify with the decompilation agent before relying on it (§5.4 Q6).

Skin tones from shipped/endorsed sources **[verified]**:

| Source | Value | Hex |
| --- | --- | --- |
| BigWillyMod `av.SkinColor` | `Color32(223, 189, 161, 255)` | `#DFBDA1` |
| S1API example NPC 1 | `Color32(150, 120, 95, 255)` | `#96785F` |
| S1API docs default | `Color32(150, 120, 95, 255)` | `#96785F` |

Note the pattern in BigWillyMod: **eyelid colour is set to the same value as skin colour.** Do that; mismatched eyelids are the #1 tell of a modded NPC.

### 1.8 Is NPC appearance data-driven? **Yes — [verified]**

Every category of character resolves to an `AvatarSettings` asset:

- **Named story/customer NPCs**: a per-NPC `AvatarSettings` asset. `NPC.Avatar.CurrentSettings` returns it at runtime.
- **Employees**: `EmployeeManager` holds `List<EmployeeAppearance> MaleAppearances` / `FemaleAppearances` where `EmployeeAppearance { AvatarSettings Settings; Sprite Mugshot; }`, selected by an integer `AppearanceIndex` and de-duplicated via `takenMaleAppearances` / `takenFemaleAppearances`. **[verified]**
- **Player**: `Customization.BasicAvatarSettings` (the character-creator subset), replicated over FishNet via `SetAvatarSettings` / `SendAvatarSettings` RPCs. **[verified]**
- **Distance LOD**: `AvatarSettings.ImpostorTexture` feeds `Impostors.AvatarImpostor` / Amplify's octahedral impostor shader. **[verified]**

### 1.9 Complete verified catalog of shipped appearance assets **[verified]**

These are `Resources.Load` paths (no file extension). Source of truth: the constant tables in the installed **S1API.Forked 3.1.4** assembly, dumped to `research-ext\raw\s1api-appearance-path-catalog.txt`. Independently corroborated by literal strings in the game's own metadata (e.g. `Avatar/Layers/Bottom/MaleUnderwear`, `Avatar/Layers/Bottom/FemaleUnderwear`) and by the community wiki example (`Avatar/Accessories/Feet/CombatBoots/CombatBoots`).

**Body layers — textures** (`Avatar/Layers/...`)

| Slot | Paths |
| --- | --- |
| Top | `Top/Buttonup`, `Top/RolledButtonUp`, `Top/FlannelButtonUp`, `Top/T-Shirt`, `Top/Tucked T-Shirt`, `Top/V-Neck`, `Top/Overalls`, `Top/HazmatSuit`, `Top/FastFood T-Shirt`, `Top/GasStation T-Shirt`, `Top/ChestHair1`, `Top/Nipples`, `Top/UpperBodyTattoos` |
| Bottom | `Bottom/Jeans`, `Bottom/Jorts`, `Bottom/CargoPants`, `Bottom/MaleUnderwear`, `Bottom/FemaleUnderwear` |
| Hands (layer) | `Accessories/Gloves`, `Accessories/FingerlessGloves` |
| Tattoos | `Tattoos/UpperBodyTattoos`, `Tattoos/chest/Chest_{Bird,DeadFace,Egg,LBC,Sword}`, `Tattoos/leftarm/LeftArm_{Alien,Heart,Peace,Web,Weed}`, `Tattoos/rightarm/RightArm_{Alien,Heart,Peace,Web,Weed}` |

**Face layers — textures** (`Avatar/Layers/Face/...`)

| Group | Paths |
| --- | --- |
| Expression | `Face_Agape`, `Face_Agitated`, `Face_FrownPout`, `Face_Neutral`, `Face_NeutralPout`, `Face_OpenMouthSmile`, `Face_Scared`, `Face_SlightFrown`, `Face_SlightSmile`, `Face_Smile`, `Face_SmugPout`, `Face_Surprised` |
| Detail | `EyeShadow`, `Freckles`, `OldPersonWrinkles`, `TiredEyes`, `FaceTattoos1` |
| Facial hair | `FacialHair_Goatee`, `FacialHair_Stubble`, `FacialHair_Swirl` |
| Face tattoos | `Tattoos/face/Face_{ForeheadCross,Sword,Teardrop,Tribal}` |

**Accessories — meshes** (`Avatar/Accessories/...`)

| Slot | Paths |
| --- | --- |
| Head (18) | `Head/BucketHat/BucketHat`, `Head/Cap/Cap`, `Head/Cap/Cap_FastFood`, `Head/ChefHat/ChefHat`, `Head/CowboyHat/CowboyHat`, `Head/FlatCap/FlatCap`, `Head/PorkpieHat/PorkpieHat`, `Head/Beanie/Beanie`, `Head/PoliceCap/PoliceCap`, `Head/SantaHat/SantaHat`, `Head/MushroomHat/MushroomHat`, `Head/TrashCrown/TrashCrown`, `Head/SaucePan/SaucePan`, `Head/LegendSunglasses/LegendSunglasses`, `Head/Oakleys/Oakleys`, `Head/RectangleFrameGlasses/RectangleFrameGlasses`, `Head/SmallRoundGlasses/SmallRoundGlasses`, `Head/Respirator/Respirator` |
| Chest (5) | `Chest/Blazer/Blazer`, `Chest/CollarJacket/CollarJacket`, `Chest/OpenVest/OpenVest`, `Chest/BulletProofVest/BulletProofVest`, `Chest/BulletProofVest/BulletProofVest_Police` |
| Waist (5) | `Waist/Apron/Apron`, `Waist/Belt/Belt`, `Waist/PoliceBelt/PoliceBelt`, `Waist/HazmatSuit/HazmatSuit`, `Waist/PriestGown/PriestGown` |
| Feet (5) | `Feet/CombatBoots/CombatBoots`, `Feet/DressShoes/DressShoes`, `Feet/Flats/Flats`, `Feet/Sandals/Sandals`, `Feet/Sneakers/Sneakers` |
| Bottom (2) | `Bottom/LongSkirt/LongSkirt`, `Bottom/MediumSkirt/MediumSkirt` |
| Neck (1) | `Neck/GoldChain/GoldChain` |
| Hands (1) | `Hands/Polex/Polex` (wristwatch) |
| Facial hair | `FacialHair/Chevron/Chevron` (moustache — mesh, not layer) |

**Hair — meshes** (`Avatar/Hair/...`, 24 constants, **23 usable**)

`afro/Afro`, `balding/Balding`, `bowlcut/BowlCut`, `bun/Bun`, `buzzcut/BuzzCut`, `closebuzzcut/CloseBuzzCut`, `doubletopknot/DoubleTopKnot`, `franklin/Franklin`, `fringeponytail/FringePonyTail`, `highbun/HighBun`, `longcurly/LongCurly`, `longslicked/LongSlicked`, `lowbun/LowBun`, `messybob/MessyBob`, `midfringe/MidFringe`, `mohawk/Mohawk`, `monk/Monk`, `peaked/Peaked`, `receding/Receding`, `shoulderlength/ShoulderLength`, `sidepartbob/SidePartBob`, `spiky/Spiky`, `tony/Tony`

The 24th constant, `HairStyle.Jesus`, is `""` and carries an explicit attribute **[verified]**:

```csharp
[Obsolete("The Jesus hairstyle was removed in game version 0.4.6 and now resolves to no hair.")]
public const string Jesus = "";
```

Useful signal for planning: **TVGS does delete avatar assets between versions.** Any appearance you author should degrade gracefully — a removed path resolves to *nothing*, not to an error, so a character can silently go bald or shoeless after an update. Budget for a post-patch visual check.

> **Two path traps.**
> 1. **Casing.** Hair folders are lowercase (`Avatar/Hair/bowlcut/BowlCut`) but the S1API *docs* show `Avatar/Hair/Spiky/Spiky` with a capital folder. The **constant tables** (and BigWillyMod's shipped code) use lowercase folders.
> 2. **The docs describe a class that does not exist.** `appearance-customization.md` shows `BodyLayerFields.Undergarments` with `"Avatar/Layers/Underwear/Boxers"`. There is **no** `Undergarments` class and **no** `Avatar/Layers/Underwear/` folder — underwear lives on `BodyLayerFields.Pants` as `MaleUnderwear` / `FemaleUnderwear` (`Avatar/Layers/Bottom/...`). **[verified]**
>
> Always use the constants (`Shirts.TShirt`, `Pants.Jeans`, `HairStyle.Spiky`, `Feet.Sneakers`, …), never a hand-typed string, and never a path copied out of the prose docs.

The real S1API constant classes are: `BodyLayerFields.{Shirts, Pants, Accessories, ChestTattoos, LeftArmTattoos, RightArmTattoos}`, `FaceLayerFields.{Face, Eyes, FacialHair, FaceTattoos}`, `AccessoryFields.{Head, Chest, Waist, Feet, Bottom, Neck, Hands, FacialHairAccessory}`, `CustomizationFields.{HairStyle, HairColor, SkinColor, Gender, Weight, Height, EyeBallTint, PupilDilation, EyebrowScale, EyebrowThickness, EyebrowRestingHeight, EyebrowRestingAngle, EyeLidRestingStateLeft, EyeLidRestingStateRight}`. **[verified]**

**Held items** (`Avatar/Equippables/...`) **[verified]**: `Baton`, `Beer`, `Coffee`, `Cuke`, `Hammer`, `Joint`, `Phone_Lowered`, `Phone_Raised`, `Pipe`, `TrashBag`, `BrokenBottle`, `Knife`, `M1911`, `PumpShotgun`, `Revolver`, `Taser`.

**Hard limits** **[community]**, from the S1API reference doc, consistent with `AvatarSettings`' 6/8/9 accessor properties: **6 face layers, 6 body layers, 9 accessories.** `Avatar.MAX_ACCESSORIES` is the authoritative runtime constant. **[verified]** that the constant exists; its value is a question for the decompilation agent (§5.4 Q1).

### 1.10 Clothing items → appearance mapping **[verified]**

```csharp
public enum EClothingSlot { Feet=0, Bottom=1, Waist=2, Top=3, Outerwear=4,
                            Hands=5, Neck=6, Eyes=7, Head=8, Wrist=9 }
public enum EClothingApplicationType { BodyLayer=0, FaceLayer=1, Accessory=2 }

public class ClothingDefinition : StorableItemDefinition
{
    EClothingSlot            Slot;
    EClothingApplicationType ApplicationType;   // decides layer vs mesh
    string                   ClothingAssetPath; // the same Resources path as above
    bool                     Colorable;
    EClothingColor           DefaultColor;
    List<EClothingSlot>      SlotsToBlock;
}
public class ClothingInstance : StorableItemInstance { EClothingColor Color; string Name; }
```

So a wearable shop item is just **an inventory wrapper around one appearance path**. `Avatar.ApplyClothing(AvatarSettings, ClothingInstance)` and `IsClothingApplied(...)` bridge the two. **[verified]** — this is why "give the NPC a hat" and "make a hat purchasable" are two independent problems.

In-game store: **Thrifty Threads**, run by Fiona Hancock, 6AM–6PM, downtown next to the Laundromat. Additional accessories at Bleuball's Boutique. **[community]**

### 1.11 Public dumps and decompiled sources

| Resource | What it is | Verdict |
| --- | --- | --- |
| [Skippeh/ScheduleOne_UnityProject](https://github.com/Skippeh/ScheduleOne_UnityProject) | **A Unity project with stripped Schedule I scripts + `.meta` files, explicitly requiring Unity 2022.3.62f2** — the exact version this install uses. Branches: `alternate`, `alternate-beta`. Prefabs and ScriptableObjects load at runtime under MelonLoader/BepInEx. Shader fixes credited to community. | **The single most valuable resource if you ever go the AssetBundle route.** It gives you correct GUIDs and the real rig. |
| [k073l/s1-codearchiver](https://github.com/k073l/s1-codearchiver) | Automation that DepotDownloads each Steam branch, decompiles the assemblies, and commits stripped code to per-branch git branches. Last push 2026-08-01. | Tooling, not a dump. Points at how the community keeps a current archive. |
| [Thunderstore "Decompiled source" pages](https://thunderstore.io/c/schedule-i/) | Thunderstore auto-decompiles **every uploaded mod DLL** and publishes it at `…/p/<owner>/<mod>/source/`. | Enormous. Full ILSpy output for mods whose authors never published a repo. Used heavily in §3. |
| [ifBars/S1API](https://github.com/ifBars/S1API) (MIT) | The modding API, with a `S1API/Entities/Appearances/**` constant catalog of every avatar path, plus `docs/appearance-customization.md`. | Best single source for verified path names. |
| FearAndDelight `Schedule-1-Modder-Documentation` GitHub wiki | Community notes with working `AvatarSettings` code. **The repo/wiki now 404s** — content survives only in search-engine caches (quoted in §3.5). | Dead link; treat quoted code as **[community]**. |

I did **not** find a public Il2CppDumper `dump.cs` for Schedule I. The community route is decompiling the *interop* assemblies MelonLoader generates locally (which is what `research\raw\ns\*.txt` is).

---

## 2. Authoring options — compare and recommend

### Option A — Compose from existing avatar parts (recommended)

Build an `AvatarSettings` from shipped `Avatar/...` paths + tints, and let the game's own prefab pipeline instantiate the character.

**What it requires**

1. Depend on S1API (already in the stack: S1API.Forked 3.1.4).
2. Subclass `S1API.Entities.NPC`, override `IsPhysical => true`.
3. In `ConfigurePrefab(NPCPrefabBuilder builder)`: `WithIdentity(id, first, last)`, `WithAppearanceDefaults(av => { … })`, `WithSpawnPosition(...)`, optionally `EnsureCustomer()/EnsureDealer()`, `WithSchedule(...)`.
4. In `OnCreated()`: `base.OnCreated(); Appearance.Build(); Schedule.Enable();`
5. Register the NPC type from your `MelonMod` entry point.

**Pros**

- **Native look by construction.** You are literally using the same assets, shader, rig and blendshapes as shipped NPCs. Impossible to look "modded".
- **Zero binary assets.** Mod is a single DLL. No AssetBundle version coupling, no shader stripping problems.
- **Update-resilient.** Paths are stable across updates because the shop items reference them too. If TVGS re-authors a texture, your NPC updates with the game.
- **Free extras:** mugshot generation, phone contact icon, impostor LOD, ragdoll, emotions, IK, FishNet replication, save/load — all handled.
- **Multiplayer-safe** — S1API does the networking.

**Cons**

- **You are bounded by 24 hairstyles, 18 hats, 5 chest items, 5 waist items, 5 shoes, 13 tops, 5 bottoms.** No leather jacket, no suit *jacket over shirt* (only `Blazer`), no motorcycle helmet, no tie, no backpack.
- Silhouette variety is limited: outside hats and the two skirts, everything is painted on. A biker and a businessman have **the same silhouette** unless you differentiate via hat/chest/feet.
- Layer budget is tight (6 body layers, 9 accessories).
- `EClothingColor`-adjacent tinting only takes you so far — a tint multiplies the shipped albedo, so a "leather" look from a cotton T-shirt texture is not achievable.

### Option B — Clone an NPC GameObject and retexture / recolour

Find an existing NPC prefab, `Instantiate`, then poke `SkinnedMeshRenderer` materials / generate textures at runtime.

**What it requires**

- Locate the prefab (`Resources.FindObjectsOfTypeAll<T>()`, or FishNet's `NetworkManager.SpawnablePrefabs`).
- `Object.Instantiate`, rename, then either copy an existing `AvatarSettings` or walk renderers and set `Material.color` / `_BaseMap`.
- `Avatar.SetFaceTexture(Texture2D, Color)` exists and is public — genuine runtime face texture injection. **[verified]**
- Runtime `Texture2D` generation (`new Texture2D(w,h); SetPixels32(); Apply();`) works fine under IL2CPP.

**Pros**

- Escapes the fixed catalog for *textures* — you can ship a PNG tattoo, logo tee or custom face and load it at runtime with no AssetBundle. **This is proven in the wild**: the *Inkorporated* tattoo-pack mod and *Personify*'s "custom PNG layers" both do exactly this. **[community]**
- Combines cleanly with Option A — use A for structure, B for one or two bespoke textures.

**Cons**

- Writing directly to `SkinnedMeshRenderer.material` fights the framework: `ApplyBodyLayerSettings` re-composites and will stomp your edits on the next appearance change, culling toggle, or `CombinedLayer` rebuild.
- Naive prefab cloning is the documented source of the **invisible-avatar bug**. Community guidance is explicit: *"If you are making an NPC from a prefab you NEED to make a new AvatarSettings instance… DO NOT USE `LoadAvatarSettings`! Rn, it bugs out and renders the avatar invisible."* **[community]**
- You inherit whatever else was on the source prefab (police behaviours, dialogue, schedules) and must strip it.
- Still cannot change geometry.

### Option C — Author new meshes/textures in an AssetBundle (Unity 2022.3.62f2)

**Pipeline**

1. Clone [Skippeh/ScheduleOne_UnityProject](https://github.com/Skippeh/ScheduleOne_UnityProject) at the branch matching your game branch, drag the game's `Managed` folder onto `DropManagedFolderHere.bat`, open in **Unity 2022.3.62f2 exactly**.
2. Install URP at the version the game ships (`Unity.RenderPipelines.Universal.Runtime.dll` — get the exact version from the decompilation agent, §5.4 Q7).
3. Model to the shared avatar rig; skin your mesh to the **same bone names/hierarchy** exposed by `Avatar.Armature` (`HeadBone`, `HipBone`, `LeftShoulder`, `RightShoulder`, `MiddleSpine`, `LowerSpine`, `LowestSpine`, `LeftFootBone`, `RightFootBone`, …).
4. Author it as an `Accessory` prefab: `SkinnedMeshRenderer` in `skinnedMeshesToBind`, plus `shapeKeyMeshRends` if it must follow the gender/weight blendshapes, plus `meshesToColor`/`skinnedMeshesToColor` for tinting, `ShouldBlockHair` for headwear.
5. Build the bundle, ship it, load at runtime, then **re-bind bones** with `Accessory.BindBones(Transform[])`.

**Rigging requirement.** `Accessory.skinnedMeshesToBind` + `BindBones(Transform[] bones)` is the framework's own re-binding path — a custom accessory must expose skinned meshes whose bone array can be remapped onto the target avatar's armature. This is the make-or-break detail. **[verified]** the API exists; **[inference]** on exactly what `bones` must contain (§5.4 Q3).

**Shader mismatch → pink materials.** Classic AssetBundle failure. Materials in your bundle reference shaders by name; if the shader isn't in the player build (or a *different copy* is), you get magenta. Two mitigations:
- Strip shaders from the bundle and at load time do `renderer.material.shader = Shader.Find("Shader Graphs/CombinedAvatar")` — or reuse `Avatar.DefaultAvatarMaterial` as the template. **[verified]** that both the shader name and the field exist.
- Or set the bundle's material to `Universal Render Pipeline/Lit` and re-resolve at runtime. **[inference]**

**Additional Option C hazards** **[inference]**
- Blendshape/shape-key names must match or `ApplyShapeKeys` silently no-ops.
- Bundles are compiled per Unity version. A TVGS engine bump breaks you.
- IL2CPP: `AssetBundle.LoadFromFile` works, but `LoadAllAssets<T>()` type resolution needs `Il2CppType.Of<T>()` care.
- Multiplayer: every peer needs the bundle, and mismatch produces desyncs or missing renderers.

**Pros:** the only way to get a genuinely new silhouette — a motorcycle helmet, a proper suit jacket, a tactical rig.
**Cons:** highest effort by a wide margin, most fragile, and the only option that can look *wrong* rather than merely *limited*.

### Decision table

| Criterion | A — recombine parts | B — clone + retexture | C — AssetBundle |
| --- | --- | --- | --- |
| Native visual fidelity | ★★★★★ | ★★★★☆ | ★★☆☆☆ (depends entirely on your artist) |
| New silhouettes possible | ✗ | ✗ | ✓ |
| New textures possible | ✗ (tint only) | ✓ (runtime PNG) | ✓ |
| Effort to first result | hours | days | weeks |
| Survives a game update | ★★★★★ | ★★★☆☆ | ★☆☆☆☆ |
| Survives a Unity/URP version bump | ★★★★★ | ★★★★☆ | ✗ rebuild required |
| Multiplayer safe | ★★★★★ (S1API) | ★★☆☆☆ | ★★☆☆☆ |
| Mod size | ~200 KB DLL | ~200 KB + PNGs | MBs |
| Known-good prior art in the wild | many mods | Inkorporated / Personify | none found |
| Pink-material risk | none | none | high |

### Recommendation

**Ship all three character sets with Option A. Reserve Option B for at most 2–3 bespoke PNG layers. Do not use Option C.**

Reasoning:

1. Every requirement in the brief is reachable with shipped assets. Federal agent = `Blazer` + `BulletProofVest` + `DressShoes` + `Oakleys` + `Belt`. Biker = `OpenVest` + `CombatBoots` + `LegendSunglasses` + arm tattoos + `Chevron` moustache. Businessman = `Blazer` + `Buttonup` + `DressShoes` + `Polex`. Hippie = `LongCurly` + `Sandals` + `V-Neck` + peace tattoos + `GoldChain`. There is no gap that justifies a modelling pipeline.
2. The brief's own success criterion is *"they must read as native Schedule I characters."* Option A is the only option where that is guaranteed rather than attempted.
3. The three mods span **at least 8 distinct character archetypes**. Option C multiplies by 8; Option A does not.
4. Update resilience matters for a game still shipping content updates. A DLL-only mod that references stable `Resources` paths survives patches that would invalidate a bundle.
5. If you later find one genuinely missing silhouette (a helmet), add *that one accessory* via Option C on top of an Option A character. Hybrid is cheap; all-in on C is not.

---

## 3. Case studies — mods that add NPCs

### 3.1 `ifBars/BigWillyMod` — the best single reference **[verified — read the source]**

- Thunderstore: <https://thunderstore.io/c/schedule-i/p/ifBars/BigWillyMod/>
- GitHub: <https://github.com/ifBars/BigWillyMod> — **MIT** per the GitHub API; the Thunderstore page text claims GPL v3. Check `LICENSE` in the repo before copying.
- Officially endorsed by the content creator it depicts; 2.2K downloads; last pushed 2026-06-20.

**How the character's appearance is built: pure Option A.** From `NPCs/BigWilly.cs`, verbatim:

```csharp
public sealed class BigWilly : NPC
{
    public override bool IsPhysical => true;

    protected override void ConfigurePrefab(NPCPrefabBuilder builder)
    {
        builder.WithIdentity("big_willy", "BigWilly", "")
        .WithAppearanceDefaults(av =>
        {
            av.Gender = 0f;
            av.Height = 1f;
            av.Weight = 1f;
            av.SkinColor        = new Color32(223, 189, 161, 255);
            av.LeftEyeLidColor  = new Color(0.875f, 0.741f, 0.631f);
            av.RightEyeLidColor = new Color(0.875f, 0.741f, 0.631f);
            av.EyeBallTint      = new Color(1f, 0.655f, 0.655f);
            av.HairColor        = new Color(0.122f, 0.075f, 0.043f);
            av.HairPath         = "Avatar/Hair/bowlcut/BowlCut";
            av.EyeballMaterialIdentifier = "Default";
            av.PupilDilation          = 1f;
            av.EyebrowScale           = 1.169f;
            av.EyebrowThickness       = 1.111f;
            av.EyebrowRestingHeight   = 0.32419f;
            av.EyebrowRestingAngle    = -10f;
            av.LeftEye  = (0.39435f, 0.26935f);
            av.RightEye = (0.39435f, 0.26935f);
            av.WithFaceLayer("Avatar/Layers/Face/Face_SlightSmile", new Color(0f, 0f, 0f));
            av.WithBodyLayer("Avatar/Layers/Top/Overalls",          new Color(0f, 0f, 0.502f));
            av.WithBodyLayer("Avatar/Layers/Top/FlannelButtonUp",   new Color(0.863f, 0.078f, 0.235f));
            av.WithBodyLayer("Avatar/Layers/Bottom/CargoPants",     new Color(0.149f, 0.149f, 0.149f));
            av.WithAccessoryLayer("Avatar/Accessories/Head/PorkpieHat/PorkpieHat",             new Color(0.824f, 0.706f, 0.549f));
            av.WithAccessoryLayer("Avatar/Accessories/Feet/CombatBoots/CombatBoots",           new Color(0.824f, 0.706f, 0.549f));
            av.WithAccessoryLayer("Avatar/Accessories/Head/SmallRoundGlasses/SmallRoundGlasses", new Color(0f, 0f, 0f));
        })
        .WithSpawnPosition(new Vector3(-35.7332f, -4.035f, 52.2295f))
        .EnsureCustomer()
        .WithCustomerDefaults(cd => { /* spending, affinities, preferred properties … */ })
        .WithRelationshipDefaults(r => { r.WithConnections<KyleCooley, LudwigMeyer, AustinSteiner>(); })
        .WithSchedule(plan => { /* EnsureDealSignal, StayInBuilding, WalkTo, UseSlotMachineUntilTime … */ })
        .WithInventoryDefaults(inv => { inv.WithStartupItems("donut","horsesemen","megabean").WithRandomCash(500, 5000); });
    }
}
```

Local copy: `research-ext\raw\bigwilly_BigWilly.cs`.

**Three lessons.**
1. **Two body layers stack on the Top slot** — `Overalls` *over* `FlannelButtonUp`. That's how you get layered clothing without geometry. `AvatarLayer.Order` decides who wins.
2. Eyelid colour = skin colour.
3. The file header says *"Schedule1ModdingTool generated NPC blueprint"* — this appearance block was **authored in a GUI and exported**, not hand-typed. Do the same (see §3.3).

The mod's custom hat is a *separate* concern, handled in `Items/StaySillyCapCreator.cs` via S1API's `ClothingItemCreator` with a texture replacement (`Resources/StaySillyCap/stay_silly_cap_texture.png`) — i.e. an existing cap mesh, retextured. That's Option B applied to a wearable item. Local copy: `research-ext\raw\bigwilly_StaySillyCapCreator.cs`.

### 3.2 `ifBars/S1APINPCExample` — the official example mod **[verified — read the source]**

- <https://github.com/ifBars/S1APINPCExample> — **no license file** (all rights reserved by default; safe to read and learn from, not to copy wholesale). Last pushed 2026-07-18.
- Files: `NPCs/ExamplePhysicalNPC1.cs`, `ExamplePhysicalNPC2.cs`, `ExamplePhysicalDealerNPC.cs`, `CharacterCustomizerNPC.cs`, `ExampleLocationActionsNPC.cs`, `UnknownClientNPC.cs`.

`ExamplePhysicalNPC1.cs` shows the **type-safe** form — constants instead of strings:

```csharp
using S1API.Entities.Appearances.AccessoryFields;
using S1API.Entities.Appearances.BodyLayerFields;
using S1API.Entities.Appearances.CustomizationFields;
using S1API.Entities.Appearances.FaceLayerFields;

builder.WithIdentity("example_physical_npc1", "Alex", "Test1")
    .WithAppearanceDefaults(av =>
    {
        av.Gender = 0.0f;
        av.Height = 1.0f;
        av.Weight = 0.36f;
        var skinColor = new Color32(150, 120, 95, 255);
        av.SkinColor        = skinColor;
        av.LeftEyeLidColor  = av.SkinColor;
        av.RightEyeLidColor = av.SkinColor;
        av.EyeBallTint      = Color.white;
        av.PupilDilation    = 0.66f;
        av.EyebrowScale     = 0.85f;
        av.EyebrowThickness = 0.6f;
        av.EyebrowRestingHeight = 0.1f;
        av.EyebrowRestingAngle  = 0.05f;
        av.LeftEye  = (0.5f, 0.5f);
        av.RightEye = (0.5f, 0.5f);
        av.HairColor = new Color(0.1f, 0.1f, 0.1f);
        av.HairPath  = HairStyle.Spiky;
        av.WithFaceLayer<Face>(Face.Agitated, Color.black);
        av.WithFaceLayer<Eyes>(Eyes.Freckles, Color.blue);
        av.WithBodyLayer<Shirts>(Shirts.TShirt, Color.red);
        av.WithBodyLayer<Pants>(Pants.Jeans, new Color(0.15f, 0.2f, 0.3f));
        av.WithAccessoryLayer<Feet>(Feet.Sneakers, Color.red);
        av.WithImpostor("Kyle");                 // <-- borrow a shipped NPC's LOD billboard
    })
    .WithSpawnPosition(spawnPos)
    .EnsureSmokeBreak(debugMode: true)
    .EnsureCustomer()
    …
```

**`av.WithImpostor("Kyle")` is the detail everyone misses.** Distance LOD uses a baked billboard texture; without one your NPC pops or vanishes at range. Borrow a shipped NPC's impostor whose silhouette matches, or use `WithImpostorTexture(...)`. **[verified]**

Local copies: `research-ext\raw\s1apiexample_*.cs`.

### 3.3 `DooDesch/Personnel` + `DooDesch/Personify` — no-code NPC packs and a GUI editor **[community]**

Both **MIT**, both updated **2026-08-01** (two days before this research). This is the most current tooling in the ecosystem.

- **Personnel** — <https://thunderstore.io/c/schedule-i/p/DooDesch/Personnel/> (v2.1.1)
  > "The NPC framework for Schedule I. NPC packs are plain folders — spawn points, daily schedules, customer/dealer economy, relationships, contacts — and Personnel spawns them as real S1API NPCs: networked, saved, walking their routines. Since 2.0, no mod code needed at all."
  >
  > "Deep appearance: body, skin, hair, face, eyes, eyebrows, clothing, accessories **and custom PNG layers (e.g. tattoos)**."

  Packs live in `UserData/Personnel/Packs/<author>/<pack>/` with a `manifest.json`. A mod can adopt a pack NPC with one subclass:
  ```csharp
  public sealed class PaleNpc : Personnel.PersonnelNpc
  {
      protected override string DefId => "examples_pale";
  }
  var npc = new PaleNpc();
  ```
  It also ships console helpers that solve the worst part of NPC authoring — coordinates: `personnel pos 07:30` emits a finished `walkTo` action for wherever you're standing, `personnel spawn` emits a spawn block, `personnel route` collects a whole day's route, all to clipboard.

- **Personify** — <https://thunderstore.io/c/schedule-i/p/DooDesch/Personify/> (v1.2.2)
  > "In-game NPC editor: design body, face, hair, clothing and tattoos live on the menu character, then export a ready-to-publish Personnel NPC pack."
  >
  > "Character mode mirrors the vanilla character creator; **Advanced mode opens the full avatar surface: stacked layers, custom PNG imports, per-layer visibility and tint.**"

  Runs from Main Menu → Side Hustle → Personify. Custom PNGs go in `UserData/Personify/Import/`, exports land in `UserData/Personify/Exports/`.

I could **not** find public GitHub repos for either (the Thunderstore pages link to GitHub but no matching repo exists under the `DooDesch` account). Support: `support.doodesch.de/personnel`, `support.doodesch.de/personify`.

**Recommendation for your workflow:** install Personify, dial in all 8 archetypes visually against the live menu character, export, then transcribe the resulting appearance blocks into your own `WithAppearanceDefaults(...)` calls. That is exactly what BigWillyMod's generated header implies ifBars did.

### 3.4 `UncleTyrone/EvenMoreFootPatrols` — the reference for Option B / prefab cloning **[verified — read the decompiled source]**

- Decompiled source: <https://old.thunderstore.io/c/schedule-i/p/UncleTyrone/EvenMoreFootPatrols_MONO/source/> (v1.0.3, Mono build; no license stated)
- Local copy of the page text is in the agent-tools cache; the relevant method verbatim:

```csharp
private static AvatarSettings getRandomOfficerAvatarSettings()
{
    PoliceOfficer val = EvenMoreFootPatrols._officers[Random.Range(0, EvenMoreFootPatrols._officers.Length)];
    return ((NPC)val).Avatar.CurrentSettings;
}

public static void spawnOfficer(NetworkManager manager, string name, PatrolGroup group,
                                float movementSpeedMult, bool warpToStart)
{
    PrefabObjects spawnablePrefabs = manager.SpawnablePrefabs;
    NetworkObject val = new NetworkObject();
    for (int i = 0; i < spawnablePrefabs.GetObjectCount(); i++)
    {
        NetworkObject @object = spawnablePrefabs.GetObject(true, i);
        if (!((Object)(object)((Component)@object).gameObject == (Object)null) &&
            ((Object)((Component)@object).gameObject).name == "PoliceNPC")
        {
            val = @object;
        }
    }
    NetworkObject val2 = Object.Instantiate<NetworkObject>(val);
    AvatarSettings randomOfficerAvatarSettings = getRandomOfficerAvatarSettings();
    ((Object)((Component)val2).gameObject).name = name;

    NPCMovement component = ((Component)val2).gameObject.GetComponent<NPCMovement>();
    component.MoveSpeedMultiplier = movementSpeedMult;

    PoliceOfficer component2 = ((Component)val2).gameObject.GetComponent<PoliceOfficer>();
    ((NPC)component2).Avatar.SetSkinColor(randomOfficerAvatarSettings.SkinColor);
    ((NPC)component2).Avatar.ApplyBodyLayerSettings(randomOfficerAvatarSettings, -1);
    ((NPC)component2).Avatar.ApplyBodySettings(randomOfficerAvatarSettings);
    ((NPC)component2).Avatar.ApplyEyeBallSettings(randomOfficerAvatarSettings);
    ((NPC)component2).Avatar.ApplyAccessorySettings(randomOfficerAvatarSettings);
    ((NPC)component2).Avatar.ApplyEyeLidColorSettings(randomOfficerAvatarSettings);
    ((NPC)component2).Avatar.ApplyEyeLidSettings(randomOfficerAvatarSettings);
    ((NPC)component2).Avatar.ApplyEyebrowSettings(randomOfficerAvatarSettings);
    ((NPC)component2).Avatar.ApplyFaceLayerSettings(randomOfficerAvatarSettings);
    ((NPC)component2).Avatar.ApplyHairColorSettings(randomOfficerAvatarSettings);
    ((NPC)component2).Avatar.ApplyHairSettings(randomOfficerAvatarSettings);
    ((NPC)component2).Avatar.ApplyShapeKeys(randomOfficerAvatarSettings.Gender,
                                            randomOfficerAvatarSettings.Weight, false);

    component2.StartFootPatrol(group, warpToStart);
    MelonCoroutines.Start(ActivateNetworkObjectAfterDelay(val2, 0.3f));
    MelonCoroutines.Start(SpawnNetworkObjectAfterDelay(manager, val2, 0.6f));
}
```

Everything you need to know about the low-level path is here:

- The **prefab is found by GameObject name `"PoliceNPC"`** inside FishNet's `NetworkManager.SpawnablePrefabs`, not via `Resources`. **[verified]**
- It calls **each `Apply*Settings` method individually** and never `LoadAvatarSettings` — matching the community warning about the invisibility bug.
- `ApplyBodyLayerSettings(settings, -1)` — the `-1` is `maxOrder`, meaning "apply all layers".
- **The activate/spawn ordering matters and needs delays**: `SetActive(true)` at +0.3 s, `ServerManager.Spawn(...)` at +0.6 s. Spawning immediately after `Instantiate` does not work.
- `ApplyShapeKeys(gender, weight, false)` takes a **third bool** here that the IL2CPP interop signature (`ApplyShapeKeys(float, float)`) does not show — the Mono and IL2CPP branches differ, or the third arg was added/removed. **Flag this** (§5.4 Q2).
- Note `Utils.FindObjectByName<T>` in the same mod uses `Resources.FindObjectsOfTypeAll<T>()` + name match — the pattern the brief asks about.

### 3.5 FearAndDelight — *Cute & Funny Framework* and the (now-dead) modder wiki **[community]**

- Thunderstore: <https://thunderstore.io/c/schedule-i/p/FearAndDelight/Cute_And_Funny_Framework/> (v0.2.0, ~11 months old, 508 downloads, Libraries/IL2CPP). Advertises *"(Experimental, will be introduced 0.3.0) Creation of custom NPCS, schedules, and application of cosmetics"* with this usage:
  ```csharp
  var goober = Cunny.Advanced.NPCHandler.SpawnCivPrefab();
  MelonCoroutines.Start(CoroutineUtils.Wait(5f,
      () => { Cunny.Advanced.NPCHandler.InitNPC(goober); }));
  var task = Advanced.NPCHandler.AddToNPCSchedule<NPCEvent_LocationBasedAction>(goober, comp => {
      comp.SetDestination(new Vector3(-22.43f, 0.7412f, 95.6903f));
      comp.SetStartTime(3); comp.Duration = 60;
      comp.ApplyDuration(); comp.ApplyEndTime();
  });
  ```
  Note `SpawnCivPrefab()` — there is a **civilian** NPC prefab, not just `PoliceNPC`. Useful for a neutral base. `⚠ unverified` prefab name.

- The author's `FearAndDelight/Schedule-1-Modder-Documentation` GitHub wiki **now 404s**. The NPCs page survives in search caches and contained the canonical raw-API recipe:

  > Cosmetics are all stored in a list corresponding to the cosmetic type in `AvatarSettings` within the `Avatar` component.
  > ```csharp
  > Avatar civAvatar = NPC.GetComponentInChildren<Avatar>();
  > // If you are making an NPC from a prefab you NEED to make a new avatarsettings instance.
  > AvatarSettings civAvatarSettings = ScriptableObject.CreateInstance<AvatarSettings>();
  >
  > civAvatarSettings.AccessorySettings.Add(new AvatarSettings.AccessorySetting
  > { path = "Avatar/Accessories/Feet/CombatBoots/CombatBoots", color = Color.black });
  > civAvatarSettings.AccessorySettings.Add(new AvatarSettings.AccessorySetting
  > { path = "Avatar/Accessories/Head/Saucepan/Saucepan", color = Color.white });
  >
  > civAvatarSettings.HairPath  = "Avatar/Hair/LongCurly/LongCurly";
  > civAvatarSettings.SkinColor = new Color32(144, 128, 115, 255);
  >
  > civAvatar.CurrentSettings = civAvatarSettings;
  > civAvatar.ApplyHairSettings(civAvatarSettings);
  > civAvatar.ApplyAccessorySettings(civAvatarSettings);
  > civAvatar.ApplyBodyLayerSettings(civAvatarSettings);
  > // DO NOT USE LoadAvatarSettings!!! Rn, it bugs out and renders the avatar invisible.
  > ```
  > "Do keep in mind, there is a limit to how many layers and accessories can be worn at one time!"

  The class/field names here (`AvatarSettings.AccessorySetting { path, color }`, `HairPath`, `SkinColor`, `CurrentSettings`, the `Apply*` methods) all match my local verification exactly, which raises confidence in the quote. The **path casing** in the quote (`Head/Saucepan/Saucepan`, `Hair/LongCurly/LongCurly`) does **not** match the shipped constants (`Head/SaucePan/SaucePan`, `Hair/longcurly/LongCurly`) — trust the constants.

### 3.6 Other NPC-adding mods (surveyed, not source-read)

| Mod | URL | Adds | Notes |
| --- | --- | --- | --- |
| **More NPCs** (Fannsonetti) | <https://www.nexusmods.com/schedule1/mods/1220> | 30+ custom customers + a dealer | The largest NPC pack. Changelog mentions per-NPC appearance work ("Gave Lester glasses to make him fit his inspiration better", "fixed Bobby Cooley's yellow hair") — i.e. Option A. IL2CPP + Mono. |
| **npc plus** | <https://www.nexusmods.com/schedule1/mods/1938> | 15 NPCs | Requires S1API. Explicitly *"Mod does not work with multiplayer."* |
| **GophxrMod** (HazDS) | <https://thunderstore.io/c/schedule-i/p/HazDS/GophxrMod/> | 1 NPC + 2 branded clothing items sold at Thrifty Threads | Same shape as BigWillyMod: Option A character, Option B retextured wearables. Cross-compat single DLL. |
| **ScheduleOneEnhanced** (KaBooMa) | <https://thunderstore.io/c/schedule-i/p/KaBooMa/ScheduleOneEnhanced/> | "Bicky Robby" bulk-order NPC | Claims *"As of initial release, I believe this is the first custom npc and quest mod."* Mono only. |
| **Empire2.0_S1API** (KaenSera01) | <https://github.com/KaenSera01/Empire2.0_S1API> | JSON-defined custom buyers/dealers | Roadmap explicitly lists *"Custom NPC avatars and appearances"* and *"NPCs physically spawning"* as **not yet done** — these are phone-contact NPCs only. |
| **s1-dynamicnpcs** (k073l) | <https://github.com/k073l/s1-dynamicnpcs> | data-driven NPCs via S1API | No license file. Last pushed 2026-01-25. |
| **NACops** / **Cartel Enforcer** (XO_WithSauce) | <https://thunderstore.io/c/schedule-i/p/XO_WithSauce/NACops_MONO/source/> · <https://old.thunderstore.io/c/schedule-i/p/XO_WithSauce/Cartel_Enforcer_MONO/source/> | more police / cartel enforcers | Both `using ScheduleOne.AvatarFramework;`. Same prefab-clone pattern as EvenMoreFootPatrols; JSON-configured. Decompiled source published on Thunderstore. |

---

## 4. Concrete art specs for the three character sets

### 4.0 Ground rules that apply to every character below

1. **Set `LeftEyeLidColor` and `RightEyeLidColor` equal to `SkinColor`.** Non-negotiable.
2. **Set an impostor** (`WithImpostor("<shipped NPC name>")`) or the character breaks at distance.
3. **Stay inside the `EClothingColor` palette hexes** (§1.7) unless you have a reason not to.
4. **Layer budget: 6 face / 6 body / 9 accessories.** Every spec below fits.
5. `Gender` 0 = male, 1 = female. `Weight` 0–1 (S1API default 0.4). `Height` safe range **0.8–1.2**, AvatarFramework default 0.98.
6. **Silhouette is 90% hat + chest accessory + shoes.** Spend your differentiation budget there, not on shirt colour.

---

### 4.1 Driver employee

**First, the hard constraint you need to know before designing anything.**

`EEmployeeType` is a **closed 4-value enum** — `Botanist=0, Handler=1, Chemist=2, Cleaner=3`. **[verified]** (`research-ext\raw\` → `research\raw\ns\ns-Il2CppScheduleOne.Employees.txt`). `EmployeeManager` exposes exactly four prefabs: `BotanistPrefab`, `PackagerPrefab`, `ChemistPrefab`, `CleanerPrefab` — note that the **`Handler` enum value maps to the `Packager` class**; "Packager" is the internal name for what the UI calls a Handler. **[verified]** There is no `Driver`. Adding a fifth type means Harmony-patching `EmployeeManager.GetEmployeePrefab`, `CreateEmployee_Server`, the Management Clipboard UI, and the save schema — a substantially bigger job than the art.

**Second: employees have no per-role uniform in vanilla.** **[verified]** — this is the finding that most changes the brief.

```csharp
public class EmployeeManager : NetworkSingleton<EmployeeManager>
{
    List<EmployeeAppearance> MaleAppearances;    // EmployeeAppearance { AvatarSettings Settings; Sprite Mugshot; }
    List<EmployeeAppearance> FemaleAppearances;
    List<int> takenMaleAppearances, takenFemaleAppearances;
    void GetRandomAppearance(bool male, out int index, out AvatarSettings settings);
    void RegisterAppearance(bool male, int index);
}
public class Employee : NPC
{
    int  AppearanceIndex { get; set; }
    bool IsMale { get; set; }
    virtual void InitializeAppearance(bool male, int index);   // NOT overridden by Botanist/Chemist/Cleaner/Packager
}
```

All four employee types draw from **one shared, gender-split pool of pre-authored `AvatarSettings`**, claimed one-per-employee. `InitializeAppearance` is declared on `Employee` and **overridden by none** of `Botanist`, `Chemist`, `Cleaner`, `Packager`. **[verified]** A Botanist and a Chemist are visually indistinguishable except for the tools they hold (`AvatarEquippable`) and the colour of their assigned bed. **[inference]**

**Design implication.** A driver "uniform" would be *more* uniform than any shipped employee — which reads as non-native. The right call is to match the shipped employee look (ordinary casual civilians) and signal the role through **one small consistent prop**, exactly the way the game signals Cleaner-ness with a trash grabber.

**Spec — Driver Employee**

| Field | Value |
| --- | --- |
| Silhouette | Baseline civilian. Single differentiator: **`Cap`** on head. No chest accessory. |
| Gender | randomise 0.0 / 1.0 (game uses `MALE_EMPLOYEE_CHANCE`) |
| Height / Weight | `0.95`–`1.05` / `0.35`–`0.6` — vary per instance, do **not** ship clones |
| Face layer | `Avatar/Layers/Face/Face_Neutral` (dark tint) + optional `Avatar/Layers/Face/FacialHair_Stubble` |
| Body layer 1 | `Avatar/Layers/Top/Tucked T-Shirt` — **DarkGrey `#A9A9A9`** or **Navy `#000080`** |
| Body layer 2 | `Avatar/Layers/Bottom/CargoPants` — **Charcoal `#262626`** |
| Accessory 1 | `Avatar/Accessories/Head/Cap/Cap` — **Navy `#000080`** *(the fleet colour; keep it identical across every driver — that's the uniform)* |
| Accessory 2 | `Avatar/Accessories/Feet/Sneakers/Sneakers` — **Charcoal `#262626`** |
| Accessory 3 | `Avatar/Accessories/Waist/Belt/Belt` — **Black `#000000`** |
| Accessory 4 *(optional)* | `Avatar/Accessories/Hands/Polex/Polex` — **DarkGrey** (a watch reads as "on the clock") |
| Equippable | `Avatar/Equippables/Phone_Lowered` while idle — reads as a driver checking a route **[inference]** |
| Impostor | borrow a generic male/female civilian |

**Why this and not a hi-vis vest:** the closest shipped chest items are `OpenVest` (a waistcoat) and `BulletProofVest` (tactical). Neither reads as hi-vis, and tinting `BulletProofVest` yellow reads as *SWAT cosplay*, not *delivery driver*. If you want hi-vis, that's the one place Option B earns its keep: retexture `OpenVest` with a runtime PNG.

**Reference:** shipped employee visuals — <https://scheduleonewiki.com/wiki/Employees> · <https://schedule-1.fandom.com/wiki/Employees>

---

### 4.2 Federal agent

**What the shipped police officer wears — [verified] from the accessory catalog:**

| Piece | Path |
| --- | --- |
| Cap | `Avatar/Accessories/Head/PoliceCap/PoliceCap` |
| Vest | `Avatar/Accessories/Chest/BulletProofVest/BulletProofVest_Police` |
| Duty belt | `Avatar/Accessories/Waist/PoliceBelt/PoliceBelt` — an `Accessory` subclass with `BatonObject`, `TaserObject`, `GunObject` children and `SetBatonVisible/SetTaserVisible/SetGunVisible` |
| Prefab | GameObject named **`"PoliceNPC"`** in `NetworkManager.SpawnablePrefabs` |

Note there is a **`BulletProofVest`** *and* a **`BulletProofVest_Police`** — two separate assets. The plain one is your federal agent's vest; the `_Police` variant carries the local-PD markings. **This is the single cleanest way to read "federal, not local."** **[verified]** that both exist; **[inference]** that `_Police` is the marked variant.

**Spec — Federal Agent (suited, plain-clothes lead)**

| Field | Value |
| --- | --- |
| Silhouette | **Blazer + dress shoes + sunglasses, bare head.** The absence of a cap next to a capped officer is the read. |
| Gender / Height / Weight | `0.0` / `1.05` / `0.35` (tall, lean, upright) |
| Face layer | `Avatar/Layers/Face/Face_Neutral` — **Black `#000000`** (flat, unamused) |
| Hair | `Avatar/Hair/closebuzzcut/CloseBuzzCut` or `Avatar/Hair/peaked/Peaked` — **Charcoal `#262626`** |
| Body layer 1 | `Avatar/Layers/Top/Buttonup` — **White `#FFFFFF`** |
| Body layer 2 | `Avatar/Layers/Bottom/CargoPants` — **Charcoal `#262626`** *(reads as suit trousers when tinted dark; `Jeans` does not)* |
| Accessory 1 | `Avatar/Accessories/Chest/Blazer/Blazer` — **Charcoal `#262626`** |
| Accessory 2 | `Avatar/Accessories/Head/Oakleys/Oakleys` — **Black `#000000`** |
| Accessory 3 | `Avatar/Accessories/Feet/DressShoes/DressShoes` — **Black `#000000`** |
| Accessory 4 | `Avatar/Accessories/Waist/Belt/Belt` — **Black `#000000`** |
| Accessory 5 | `Avatar/Accessories/Hands/Polex/Polex` — **LightGrey `#D3D3D3`** |
| Equippable | `Avatar/Equippables/M1911` when hostile |
| Impostor | borrow the shipped police officer impostor if the silhouette is close enough |

**Spec — Federal Agent (tactical / raid variant)**

Swap the blazer for the plain vest and add boots:

| Field | Value |
| --- | --- |
| Accessory 1 | `Avatar/Accessories/Chest/BulletProofVest/BulletProofVest` — **Navy `#000080`** *(not `_Police`)* |
| Accessory 2 | `Avatar/Accessories/Feet/CombatBoots/CombatBoots` — **Black `#000000`** |
| Accessory 3 | `Avatar/Accessories/Head/Cap/Cap` — **Navy `#000080`** *(not `PoliceCap`)* |
| Accessory 4 | `Avatar/Accessories/Waist/Belt/Belt` — **Black `#000000`** *(not `PoliceBelt` — the local-PD baton/taser rig is a tell)* |
| Body layers | `Top/Tucked T-Shirt` **Navy**, `Bottom/CargoPants` **Charcoal** |
| Accessory 5 | `Avatar/Accessories/Head/Oakleys/Oakleys` — **Black** |

**Palette discipline:** local PD reads *black + duty-belt clutter*. Federal reads **navy + charcoal + white shirt + clean belt.** Keep the two palettes disjoint and players will read the difference instantly without a single new asset.

If you want visible agency lettering on the vest back, that's the one legitimate Option B texture in this whole brief.

**References:** <https://schedule-1.fandom.com/wiki/Police> · <https://schedule-1.fandom.com/wiki/Clothing_and_Accessories>

---

### 4.3 Special customer group — **Biker**

| Field | Value |
| --- | --- |
| Silhouette | **Open vest over bare/tattooed torso + heavy boots + wraparound shades.** Widest, heaviest of the three groups. |
| Gender / Height / Weight | `0.0` / `1.0`–`1.1` / **`0.75`–`0.9`** (bulk is the read) |
| Face layer 1 | `Avatar/Layers/Face/Face_SmugPout` — **Black `#000000`** |
| Face layer 2 | `Avatar/Layers/Face/FacialHair_Goatee` — **DarkGrey `#A9A9A9`** or **Charcoal** |
| Hair | `Avatar/Hair/longslicked/LongSlicked` or `Avatar/Hair/balding/Balding` — **Charcoal `#262626`** / **LightGrey `#D3D3D3`** for an older member |
| Body layer 1 | `Avatar/Layers/Top/UpperBodyTattoos` — **DeepBlue `#00008B`** *(ink reads blue-black, not black)* |
| Body layer 2 | `Avatar/Layers/Tattoos/leftarm/LeftArm_Web` — **DeepBlue** |
| Body layer 3 | `Avatar/Layers/Tattoos/rightarm/RightArm_Alien` — **DeepBlue** |
| Body layer 4 | `Avatar/Layers/Top/T-Shirt` — **Black `#000000`** *(omit for the shirtless variant)* |
| Body layer 5 | `Avatar/Layers/Bottom/Jeans` — **DarkGrey `#A9A9A9`** |
| Accessory 1 | `Avatar/Accessories/Chest/OpenVest/OpenVest` — **Charcoal `#262626`** *(the cut)* |
| Accessory 2 | `Avatar/Accessories/Feet/CombatBoots/CombatBoots` — **Black `#000000`** |
| Accessory 3 | `Avatar/Accessories/Head/LegendSunglasses/LegendSunglasses` — **Black `#000000`** |
| Accessory 4 | `Avatar/Accessories/Waist/Belt/Belt` — **Brown `#A52A2A`** |
| Accessory 5 | `Avatar/Accessories/FacialHair/Chevron/Chevron` — **Charcoal** *(mesh moustache — stronger silhouette than the goatee layer; use one or the other)* |
| Accessory 6 *(optional)* | `Avatar/Accessories/Neck/GoldChain/GoldChain` — **Yellow `#FFFF00`** |
| Equippable | `Avatar/Equippables/Beer` or `Avatar/Equippables/BrokenBottle` |

**Gap:** there is no leather jacket, no bandana, no fingerless-glove *mesh* (only `Avatar/Layers/Accessories/FingerlessGloves`, a texture layer — use it, body layer 6). A patch/colours design on the vest back is the strongest Option B candidate in the whole brief.

---

### 4.4 Special customer group — **Hippie**

| Field | Value |
| --- | --- |
| Silhouette | **Long hair + open sandals + loose top.** Softest, narrowest of the three. |
| Gender / Height / Weight | `0.0`–`1.0` (mix freely) / `0.95` / `0.3`–`0.5` |
| Face layer 1 | `Avatar/Layers/Face/Face_SlightSmile` — **Black `#000000`** |
| Face layer 2 | `Avatar/Layers/Face/TiredEyes` — **Brown `#A52A2A`** *(the stoned read, and it's a shipped layer)* |
| Face layer 3 | `Avatar/Layers/Face/Freckles` — **Tan `#D2B48C`** |
| Face layer 4 *(male)* | `Avatar/Layers/Face/FacialHair_Swirl` — **Brown** |
| Hair | `Avatar/Hair/longcurly/LongCurly` (F) or `Avatar/Hair/shoulderlength/ShoulderLength` / `Avatar/Hair/afro/Afro` (M) — **Brown `#A52A2A`** |
| Body layer 1 | `Avatar/Layers/Top/V-Neck` — **Lime `#00FF00`**, **Orange `#FFA500`** or **Purple `#800080`** *(pick one per NPC; the group reads by being the only saturated people on the street)* |
| Body layer 2 | `Avatar/Layers/Tattoos/leftarm/LeftArm_Peace` — **DeepPurple `#4B0082`** |
| Body layer 3 | `Avatar/Layers/Tattoos/rightarm/RightArm_Weed` — **DarkGreen `#006400`** |
| Body layer 4 | `Avatar/Layers/Bottom/Jorts` — **Beige `#F5F5DC`** |
| Accessory 1 | `Avatar/Accessories/Feet/Sandals/Sandals` — **Brown `#A52A2A`** |
| Accessory 2 | `Avatar/Accessories/Head/SmallRoundGlasses/SmallRoundGlasses` — **Orange `#FFA500`** *(tinted round lenses)* |
| Accessory 3 | `Avatar/Accessories/Neck/GoldChain/GoldChain` — **Tan `#D2B48C`** *(reads as beads at this scale)* |
| Accessory 4 *(female variant)* | `Avatar/Accessories/Bottom/LongSkirt/LongSkirt` — **DeepPurple `#4B0082`** — **replaces** body layer 4 |
| Accessory 5 *(optional)* | `Avatar/Accessories/Head/BucketHat/BucketHat` — **DarkGreen `#006400`** |
| Equippable | `Avatar/Equippables/Joint` |

⚠ **Known wiki-documented issue:** skirts have *"Clipping issues. All shirts tuck. Belt not visible."* **[community]** Test the female variant before shipping.

**Gap:** no tie-dye, no headband, no poncho. Tie-dye is the obvious Option B custom PNG body layer — and *Personify* explicitly supports importing custom PNG layers, so you can prototype it without writing any code.

---

### 4.5 Special customer group — **Businessman**

| Field | Value |
| --- | --- |
| Silhouette | **Blazer + dress shoes + watch, hatless, upright.** Near-identical to the plain-clothes federal agent — differentiate by **palette and eyewear**, not by garments. |
| Gender / Height / Weight | `0.0`–`1.0` / `1.0` / `0.45`–`0.65` *(soft, not lean — the opposite of the agent)* |
| Face layer 1 | `Avatar/Layers/Face/Face_SlightSmile` — **Black `#000000`** *(vs. the agent's `Face_Neutral`)* |
| Face layer 2 | `Avatar/Layers/Face/OldPersonWrinkles` — **Tan `#D2B48C`** |
| Hair | `Avatar/Hair/receding/Receding` or `Avatar/Hair/sidepartbob/SidePartBob` (F) — **DarkGrey `#A9A9A9`** *(greying = seniority)* |
| Body layer 1 | `Avatar/Layers/Top/Buttonup` — **SkyBlue `#87CEEB`** or **White `#FFFFFF`** |
| Body layer 2 | `Avatar/Layers/Bottom/CargoPants` — **DarkGrey `#A9A9A9`** |
| Accessory 1 | `Avatar/Accessories/Chest/Blazer/Blazer` — **Navy `#000080`**, **Brown `#A52A2A`** or **DarkGrey** *(never charcoal — that's the agent)* |
| Accessory 2 | `Avatar/Accessories/Feet/DressShoes/DressShoes` — **Brown `#A52A2A`** *(agent wears black)* |
| Accessory 3 | `Avatar/Accessories/Hands/Polex/Polex` — **Yellow `#FFFF00`** *(gold watch)* |
| Accessory 4 | `Avatar/Accessories/Waist/Belt/Belt` — **Brown `#A52A2A`** |
| Accessory 5 | `Avatar/Accessories/Head/RectangleFrameGlasses/RectangleFrameGlasses` — **DarkGrey `#A9A9A9`** *(prescription, not shades — the strongest single differentiator from the agent)* |
| Accessory 6 *(female variant)* | `Avatar/Accessories/Bottom/MediumSkirt/MediumSkirt` — **Navy `#000080`** — **replaces** body layer 2 |
| Equippable | `Avatar/Equippables/Coffee` or `Avatar/Equippables/Phone_Raised` |

**Agent vs. businessman cheat sheet:**

| | Federal agent | Businessman |
| --- | --- | --- |
| Blazer | Charcoal `#262626` | Navy / Brown / DarkGrey |
| Shoes | Black | Brown |
| Eyewear | `Oakleys` (opaque) | `RectangleFrameGlasses` (clear) |
| Face | `Face_Neutral` | `Face_SlightSmile` + wrinkles |
| Weight | 0.35 | 0.45–0.65 |
| Watch | LightGrey (steel) | Yellow (gold) |

**Gap:** no necktie. This is the one item I'd genuinely miss, and a tie is a plausible tiny Option C accessory (single quad strip skinned to `MiddleSpine`) if you ever do a bundle.

---

## 5. Runtime appearance code — Il2CppInterop-safe approach

### 5.1 The recommended path: S1API, no raw interop **[verified against S1API 3.x source]**

```csharp
using MelonLoader;
using S1API.Entities;
using S1API.Entities.Appearances.AccessoryFields;
using S1API.Entities.Appearances.BodyLayerFields;
using S1API.Entities.Appearances.CustomizationFields;
using S1API.Entities.Appearances.FaceLayerFields;
using UnityEngine;

public sealed class FederalAgent : NPC
{
    public override bool IsPhysical => true;

    protected override void ConfigurePrefab(NPCPrefabBuilder builder)
    {
        builder.WithIdentity("fed_agent_carver", "Miles", "Carver")
            .WithAppearanceDefaults(av =>
            {
                av.Gender = 0.0f;
                av.Height = 1.05f;
                av.Weight = 0.35f;

                var skin = new Color32(198, 162, 132, 255);
                av.SkinColor        = skin;
                av.LeftEyeLidColor  = skin;      // ALWAYS mirror skin
                av.RightEyeLidColor = skin;

                av.EyeBallTint    = Color.white;
                av.PupilDilation  = 0.6f;
                av.LeftEye  = (0.5f, 0.5f);
                av.RightEye = (0.5f, 0.5f);

                av.EyebrowScale         = 0.9f;
                av.EyebrowThickness     = 0.7f;
                av.EyebrowRestingHeight = 0.05f;
                av.EyebrowRestingAngle  = -0.15f;   // slight scowl

                av.HairColor = new Color(0.15f, 0.15f, 0.15f);
                av.HairPath  = HairStyle.CloseBuzzCut;

                av.WithFaceLayer<Face>(Face.Neutral, Color.black);

                av.WithBodyLayer<Shirts>(Shirts.Buttonup,   Color.white);
                av.WithBodyLayer<Pants>(Pants.CargoPants,   new Color(0.149f, 0.149f, 0.149f));

                av.WithAccessoryLayer<Chest>(Chest.Blazer,     new Color(0.149f, 0.149f, 0.149f));
                av.WithAccessoryLayer<Head>(Head.Oakleys,      Color.black);
                av.WithAccessoryLayer<Feet>(Feet.DressShoes,   Color.black);
                av.WithAccessoryLayer<Waist>(Waist.Belt,       Color.black);
                av.WithAccessoryLayer<Hands>(Hands.Polex,      new Color(0.827f, 0.827f, 0.827f));

                av.WithImpostor("Kyle");   // ⚠ pick a shipped NPC whose silhouette matches
            })
            .WithSpawnPosition(new Vector3(-53.57f, 1.065f, 67.80f));
    }

    protected override void OnCreated()
    {
        base.OnCreated();
        try
        {
            Appearance.Build();       // mandatory: generates mugshot + applies
        }
        catch (System.Exception ex)
        {
            MelonLogger.Error($"[FederalAgent] appearance failed: {ex.Message}");
            Appearance.GenerateRandomAppearance();
            Appearance.Build();
        }
        Schedule.Enable();
    }
}
```

Rules that S1API's own docs state as mandatory **[community, from `S1API/docs/appearance-customization.md` and `skills/schedule-one-custom-npcs`]**:

- Appearance goes in `OnCreated()` (runtime), **never** in `ConfigurePrefab()`, which is for identity/schedule/static defaults only. Note the tension: `WithAppearanceDefaults(...)` *is* called from `ConfigurePrefab` — those are prefab **defaults**; the runtime `Appearance` builder is for live changes.
- **`Build()` is mandatory.** Without it the appearance never applies and there's no mugshot.
- Wrap in try/catch with `GenerateRandomAppearance()` as the fallback.
- Value ranges: `Gender` 0–1; `Weight` 0–1 (applied as `Weight * 100` blendshape); `Height` practical 0.8–1.2, no clamp in code so it *will* let you make a giant; `PupilDilation` 0–1; eyelids 0–1 each; `EyebrowRestingHeight` clamped **-1.1 to 1.5** at runtime.

### 5.2 The raw Il2CppInterop path (only if you must bypass S1API) `⚠ partially unverified`

```csharp
using Il2CppInterop.Runtime;
using Il2CppScheduleOne.AvatarFramework;
using Il2CppScheduleOne.NPCs;
using UnityEngine;

// 1) Find a template. Two options; the FishNet one is what shipped mods actually use.
//    (a) Resources scan — works, but returns prefabs AND live instances, so filter.
Avatar[] all = Resources.FindObjectsOfTypeAll<Avatar>();   // Il2CppReferenceArray<Avatar>

//    (b) FishNet spawnable prefabs — the pattern from EvenMoreFootPatrols. [verified in that source]
var nm = Il2CppFishNet.InstanceFinder.NetworkManager;
var prefabs = nm.SpawnablePrefabs;
Il2CppFishNet.Object.NetworkObject template = null;
for (int i = 0; i < prefabs.GetObjectCount(); i++)
{
    var obj = prefabs.GetObject(true, i);
    if (obj != null && obj.gameObject.name == "PoliceNPC")   // ⚠ civilian prefab name UNKNOWN — see Q4
        template = obj;
}

// 2) Instantiate + rename.
var clone = Object.Instantiate(template);
clone.gameObject.name = "FedAgent_Carver";

// 3) Build a FRESH AvatarSettings. Do NOT mutate the template's — it is a shared ScriptableObject
//    and every officer in the world references it.
var s = ScriptableObject.CreateInstance<AvatarSettings>();
s.SkinColor = new Color32(198, 162, 132, 255);
s.Gender = 0f; s.Weight = 0.35f; s.Height = 1.05f;
s.HairPath  = "Avatar/Hair/closebuzzcut/CloseBuzzCut";
s.HairColor = new Color(0.15f, 0.15f, 0.15f);

// Il2Cpp generic lists must be constructed, not collection-initialised.
s.BodyLayerSettings = new Il2CppSystem.Collections.Generic.List<AvatarSettings.LayerSetting>();
var top = new AvatarSettings.LayerSetting
{
    layerPath = "Avatar/Layers/Top/Buttonup",
    layerTint = Color.white
};
s.BodyLayerSettings.Add(top);

s.AccessorySettings = new Il2CppSystem.Collections.Generic.List<AvatarSettings.AccessorySetting>();
var blazer = new AvatarSettings.AccessorySetting
{
    path  = "Avatar/Accessories/Chest/Blazer/Blazer",
    color = new Color(0.149f, 0.149f, 0.149f)
};
s.AccessorySettings.Add(blazer);

// 4) Apply — individually. NEVER LoadAvatarSettings (documented invisibility bug). [community]
var npc = clone.gameObject.GetComponent<NPC>();
var av  = npc.Avatar;
av.CurrentSettings = s;
av.SetSkinColor(s.SkinColor);
av.ApplyBodySettings(s);
av.ApplyBodyLayerSettings(s, -1);      // -1 = apply all orders
av.ApplyFaceLayerSettings(s);
av.ApplyAccessorySettings(s);
av.ApplyHairSettings(s);
av.ApplyHairColorSettings(s);
av.ApplyEyeBallSettings(s);
av.ApplyEyeLidSettings(s);
av.ApplyEyeLidColorSettings(s);
av.ApplyEyebrowSettings(s);
av.ApplyShapeKeys(s.Gender, s.Weight);   // ⚠ Mono build takes a 3rd bool — see Q2

// 5) Place, activate, then network-spawn — WITH DELAYS. [verified pattern]
clone.transform.position = spawnPoint;
MelonCoroutines.Start(ActivateAfter(clone, 0.3f));            // gameObject.SetActive(true)
MelonCoroutines.Start(SpawnAfter(nm, clone, 0.6f));           // nm.ServerManager.Spawn(clone, null, default)
```

**IL2CPP-specific gotchas in the above** **[inference, from general Il2CppInterop practice]**:

- `Resources.FindObjectsOfTypeAll<T>()` returns `Il2CppReferenceArray<T>`; iterate with a plain `for`, and null-check with Unity's `== null` semantics (`UnityEngine.Object` overload), not `is null`.
- Use `Il2CppSystem.Collections.Generic.List<T>`, never `System.Collections.Generic.List<T>`, for anything crossing the boundary.
- `AvatarSettings.LayerSetting` is a **struct** (`Il2CppSystem.ValueType`) while `AccessorySetting` is a **class**. Assignment semantics differ. **[verified]**
- `ScriptableObject.CreateInstance<T>()` for an Il2Cpp type needs `Il2CppType.Of<AvatarSettings>()` in some interop versions; if the generic overload throws, use `ScriptableObject.CreateInstance(Il2CppType.Of<AvatarSettings>()).Cast<AvatarSettings>()`.
- Any `Action<Texture2D>` passed to `GetMugshot`/`GenerateMugshot` must be `Il2CppSystem.Action<Texture2D>`, constructed from a `DelegateSupport.ConvertDelegate` or the interop-generated ctor.
- Register your NPC MonoBehaviour subclasses with `ClassInjector.RegisterTypeInIl2Cpp<T>()` before use — or just don't subclass MonoBehaviour and let S1API do it.

**Registering with the NPC registry:** `⚠ unverified`. `ScheduleOne.NPCs.NPCManager` exists (namespace dumped at `research\raw\ns\ns-Il2CppScheduleOne.NPCs.txt`) but I did not confirm the registration entry point. **This is exactly what S1API's `NPCPrefabBuilder` / `NPCNetworkBootstrap` / `NPCPrefabIdentity` handle for you** — which is the strongest single argument for Option A. See Q5.

### 5.3 Employee-specific path (driver mod only) **[verified signature]**

```csharp
// EmployeeManager is a NetworkSingleton.
Il2CppScheduleOne.Employees.Employee CreateEmployee_Server(
    Property property, EEmployeeType type,
    string firstName, string lastName, string id,
    bool male, int appearanceIndex,
    Vector3 position, Quaternion rotation, string guid);
```

Because `EEmployeeType` has no `Driver` and appearance is chosen by `appearanceIndex` into `EmployeeManager.MaleAppearances`/`FemaleAppearances`, the two viable strategies are:

- **(i)** Append your driver `AvatarSettings` to `MaleAppearances`/`FemaleAppearances` at load, remember the index, and pass it — a small, low-risk patch. The role still has to reuse an existing `EEmployeeType`.
- **(ii)** Don't use the employee system at all. Ship drivers as S1API NPCs and implement hiring/wages yourself. More work, far fewer collisions with the Management Clipboard, save schema, and other employee mods.

I'd take (ii). **[inference]**

### 5.4 Exact questions for the decompilation agent

These are the specific things I could not resolve statically. Each one blocks or de-risks a concrete decision.

**Q1 — `Avatar.MAX_ACCESSORIES` value.**
What is the literal value of `static int Avatar.MAX_ACCESSORIES`? Community docs say 9 accessories / 6 body layers / 6 face layers. Are those the real runtime caps, and what happens on overflow — silent drop, exception, or last-wins? *(Blocks: whether the biker spec's 6 accessories + gloves layer is safe.)*

**Q2 — `ApplyShapeKeys` signature drift.**
IL2CPP interop shows `Avatar.ApplyShapeKeys(float gender, float weight)`. The decompiled **Mono** build of EvenMoreFootPatrols calls `ApplyShapeKeys(gender, weight, false)` with a **third bool**. Which is correct for the current IL2CPP build, and what is the third parameter? *(Blocks: any raw-interop apply sequence.)*

**Q3 — `Accessory.BindBones(Transform[] bones)` contract.**
What exactly must the `bones` array contain — order, count, which transforms? Is it `Avatar.Armature`'s full descendant list, or a fixed-order subset? And does `Accessory.skinnedMeshesToBind` need its `rootBone` set before or after? *(Blocks: Option C entirely. If we ever add a custom accessory, this is the make-or-break API.)*

**Q4 — The civilian NPC prefab name(s).**
EvenMoreFootPatrols finds `"PoliceNPC"` by GameObject name in `NetworkManager.SpawnablePrefabs`. Cute & Funny Framework has a `SpawnCivPrefab()`. **What is the exact GameObject name of the generic civilian NPC prefab** (and any other spawnable NPC prefabs) in `SpawnablePrefabs`? Please dump the full name list. *(Blocks: Option B; also useful as a sanity fallback for Option A.)*

**Q5 — NPC registration entry point.**
After instantiating and network-spawning a cloned NPC, what must be called so the game's own systems see it? Specifically: is there an `NPCManager.RegisterNPC` / `NPCManager.NPCRegistry` / `Registry` add, and what does `ScheduleOne.NPCs.NPCManager`'s public surface look like? Also: how does `NPC.ID`/`GUID` participate in save/load, and what happens on load if an NPC id from a previous session no longer exists? *(Blocks: whether raw interop is viable at all without S1API.)*

**Q6 — `EClothingColor` → RGB table.**
Dump the contents of `ClothingUtility.ColorDataList` — the `ActualColor` and `LabelColor` for all 27 `EClothingColor` values. I've inferred from three exact matches in shipped mod source (Crimson `#DC143C`, Tan `#D2B48C`, Navy `#000080`) that these are CSS/X11 named colours. **Confirm or refute.** *(Blocks: every palette hex in §4.)*

**Q7 — Exact URP package version.**
What is the assembly/package version of `Unity.RenderPipelines.Universal.Runtime`? *(Blocks: setting up a matching Unity 2022.3.62f2 project for Option C or for reading shipped materials in AssetRipper.)*

**Q8 — `Shader Graphs/CombinedAvatar` property names.**
What are the exposed shader properties (`_BaseMap`, `_BaseColor`, `_Smoothness`, layer slots, etc.)? And what is `Avatar.DEFAULT_SMOOTHNESS`? *(Blocks: any Option B runtime texture injection that needs to survive `ApplyBodyLayerSettings` re-compositing.)*

**Q9 — Employee appearance pool size and mutability.**
How many entries are in `EmployeeManager.MaleAppearances` / `FemaleAppearances`? Are they populated from a serialized prefab field (mutable at runtime) or built in `Awake()`? And confirm that none of `Botanist`/`Chemist`/`Cleaner`/`Packager` override `InitializeAppearance`. *(Blocks: driver-employee strategy (i) vs (ii) in §5.3.)*

**Q10 — `LoadAvatarSettings` invisibility bug.**
Community documentation says `Avatar.LoadAvatarSettings(AvatarSettings)` "bugs out and renders the avatar invisible" when used on a freshly cloned prefab. Read the method body: what does it do that the individual `Apply*` calls don't — does it call `SetVisible(false)` / `DestroyAccessories()` / depend on `onSettingsLoaded` subscribers or a coroutine that needs the object active? *(Blocks: knowing whether the workaround is still needed, or whether it's a stale 2025-era bug.)*

**Q11 — Layer texture resolution and mesh budget.**
From `sharedassets0.assets`: what are the pixel dimensions of a representative `AvatarLayer.Texture` (e.g. `Avatar/Layers/Top/T-Shirt`) and its `Normal`? And the triangle count of the shared avatar body `SkinnedMeshRenderer` and of one accessory (e.g. `Cap`)? *(Blocks: §1.5, which is currently inference. Only matters if we do Option B/C.)*

**Q12 — `AvatarSettings.EyeballMaterialIdentifier` valid values.**
BigWillyMod sets it to `"Default"`. What are the other legal identifiers, and where is the lookup table? *(Nice-to-have: could give the federal agent a distinct cold eye read.)*

---

## Sources (D)

- <https://www.nexusmods.com/schedule1/mods/1220> — "More NPCs" (Fannsonetti), Nexus Mods. 30+ custom NPCs, IL2CPP+Mono. Mod-author claims; **[community]**. Changelog was actively updated; version 1.2 is the latest referenced.
- <https://www.nexusmods.com/schedule1/mods/1938> — "npc plus", Nexus Mods. 15 NPCs, requires S1API, singleplayer-only. Author's first mod; **[community]**, treat quality claims loosely.
- <https://thunderstore.io/c/schedule-i/> — Thunderstore Schedule I community index. Primary mod distribution; **[community]** but authoritative for package metadata (I queried its `/api/v1/package/` endpoint directly on 2026-08-03).
- <https://thunderstore.io/c/schedule-i/p/ifBars/BigWillyMod/> — BigWillyMod v1.0.3 description page. Officially endorsed NPC mod. **[community]** for the description, **[verified]** for the source behind it.
- <https://github.com/ifBars/BigWillyMod> — BigWillyMod source. **License: MIT per GitHub API** (Thunderstore page text says GPL v3 — reconcile before copying). Last pushed 2026-06-20. Highest-trust NPC appearance reference; I read `NPCs/BigWilly.cs` directly. **[verified]**
- <https://github.com/ifBars/S1API> — S1API, MIT, 22 stars, last pushed **2026-08-03** (same day as this research). The modding API in use here (S1API.Forked 3.1.4). Highest trust; I read its docs and constant tables. **[verified]**
- <https://github.com/ifBars/S1APINPCExample> — official S1API NPC example project. **No license file** (default all-rights-reserved — read, don't copy verbatim). Last pushed 2026-07-18. **[verified]** — read `NPCs/ExamplePhysicalNPC1.cs` etc.
- <https://raw.githubusercontent.com/ifBars/S1API/HEAD/S1API/docs/appearance-customization.md> — S1API appearance docs. Authoritative on API shape; **some example paths in it use wrong folder casing** vs. the shipped constants. Current as of 2026-08-03. **[community]** for paths, **[verified]** for API.
- <https://raw.githubusercontent.com/ifBars/S1API/HEAD/skills/schedule-one-custom-npcs/references/s1api-custom-npc-reference.md> — S1API's own condensed custom-NPC reference incl. value clamps and layer limits. Current. **[community]**, but explicitly derived from AvatarFramework source so higher trust than typical docs.
- <https://thunderstore.io/c/schedule-i/p/DooDesch/Personnel/> — Personnel v2.1.1, MIT, updated **2026-08-01**. No-code NPC packs. Most current NPC framework in the ecosystem. **[community]**; no public source repo found.
- <https://thunderstore.io/c/schedule-i/p/DooDesch/Personify/> — Personify v1.2.2, MIT, updated **2026-08-01**. In-game avatar editor + Personnel pack export. Author self-describes it as young/lightly tested. **[community]**; recommended as an authoring tool, not a dependency.
- <https://old.thunderstore.io/c/schedule-i/p/UncleTyrone/EvenMoreFootPatrols_MONO/source/> — full ILSpy decompilation of EvenMoreFootPatrols v1.0.3 (Mono). ~10 months old, so **pre-dates several game updates**; API drift likely (see Q2). Still the clearest prefab-clone reference. **[verified]** as a faithful decompile.
- <https://thunderstore.io/c/schedule-i/p/XO_WithSauce/NACops_MONO/source/> — decompiled NACops source. `using ScheduleOne.AvatarFramework;`, JSON-configured police additions. Mono branch. **[verified]** decompile, **[community]** for technique.
- <https://old.thunderstore.io/c/schedule-i/p/XO_WithSauce/Cartel_Enforcer_MONO/source/> — decompiled Cartel Enforcer source. Shows `AvatarWeapon`/`AvatarEquippable` manipulation on spawned NPCs. Mono branch. **[verified]** decompile.
- <https://thunderstore.io/c/schedule-i/p/HazDS/GophxrMod/> — GophxrMod, 1 NPC + 2 branded clothing items at Thrifty Threads, requires S1API 3.0.0+, targets game v0.4.4f10. Cross-compat single DLL. **[community]**.
- <https://thunderstore.io/c/schedule-i/p/KaBooMa/ScheduleOneEnhanced/> — ScheduleOneEnhanced, adds the "Bicky Robby" NPC + bulk-order quest. Claims to be the first custom NPC+quest mod. Mono only. **[community]**, self-reported primacy unverified.
- <https://thunderstore.io/c/schedule-i/p/FearAndDelight/Cute_And_Funny_Framework/> — Cute & Funny Framework v0.2.0, ~11 months old, 508 downloads. NPC creation was "experimental, 0.3.0". **Likely abandoned** given the author's wiki now 404s. **[community]**, low currency.
- <https://github-wiki-see.page/m/FearAndDelight/Schedule-1-Modder-Documentation/wiki/NPCs> — community NPC/cosmetics documentation. **This URL now returns 404**; content quoted in §3.5 is from a search-engine cache. Class and field names in it independently match my local verification, which is why I trust the code shape despite the dead link. **[community]**, stale (~2025).
- <https://github.com/KaenSera01/Empire2.0_S1API> — JSON-driven custom buyers/dealers. Its own roadmap lists "Custom NPC avatars and appearances" and physically-spawning NPCs as **future work** — so it is *not* an appearance reference. **[community]**.
- <https://github.com/k073l/s1-dynamicnpcs> — data-driven NPCs via S1API. No license file. Last pushed 2026-01-25 (somewhat stale). **[community]**.
- <https://github.com/k073l/s1-codearchiver> — automated decompile-and-archive tooling for Schedule I Steam branches. Last pushed 2026-08-01, actively maintained. Best route to a *current* full decompile. **[community]**, high trust as tooling.
- <https://github.com/Skippeh/ScheduleOne_UnityProject> — Unity project with stripped Schedule I scripts + meta files. **Requires Unity 2022.3.62f2 exactly** (matches this install). Branches `alternate`/`alternate-beta`. Last pushed 2026-04-01, 12 stars. **The prerequisite for Option C.** **[community]**, high trust.
- <https://scheduleonewiki.com/wiki/Employees> — Schedule I Wiki, employees page. Confirms the four player-facing employee types. Small independent wiki, low edit volume. **[community]**.
- <https://schedule-1.fandom.com/wiki/Employees> — Fandom employees page with hire/wage costs and duties. Larger and better maintained than the above. **[community]**.
- <https://schedule-1.fandom.com/wiki/Clothing_and_Accessories> — Fandom clothing table with slot, price, game ID, and known issues ("Clipping issues with some hairstyles", skirt tucking bugs). **The best community cross-check for the accessory catalog**; item names line up with the verified `Avatar/Accessories/...` paths. **[community]**, current.
- <https://schedule-1.fandom.com/wiki/Thrifty_Threads> — clothing store, run by Fiona Hancock, 6AM–6PM. **[community]**.
- <https://gameranx.com/features/id/533820/article/schedule-1-how-to-change-clothes/> — full Thrifty Threads price list (cross-checks the Fandom table; both agree). Games-media, moderate trust, undated. **[community]**.
- <https://selphie1999gaming.com/game-guides/schedule-i/how-to-get-more-clothes-in-schedule-1/> — third independent clothing list. Agrees with the above two. **[community]**.
- <https://github.com/vfsfitvnm/frida-il2cpp-bridge/discussions/650> — maintainer confirmation that IL2CPP discards the original `Assembly-CSharp.dll` permanently; dumps recover names/signatures, never method bodies. Context for why no public Schedule I "source dump" exists. **[community]**, authoritative on the general point, May 2025.
- **Local install, `C:\Program Files (x86)\Steam\steamapps\common\Schedule I\MelonLoader\Il2CppAssemblies\`** — assembly listing establishing URP and third-party packages. **[verified]**, read 2026-08-03.
- **Local install, `Schedule I_Data\globalgamemanagers`** — shader name table establishing `Shader Graphs/CombinedAvatar`, `Universal Render Pipeline/Lit|Simple Lit|Unlit`, Amplify Impostors, EPO outlines. **[verified]**, read 2026-08-03.
- **Local interop dumps, `research\raw\ns\ns-Il2CppScheduleOne.*.txt` and `ns-S1API.*.txt`** (produced by the sibling decompilation agent) — full type/member listings for `AvatarFramework`, `Clothing`, `Employees`, `Police`, and the S1API appearance constant tables. I independently spot-checked these against `Assembly-CSharp.dll` with ripgrep and the names match. **[verified]**, generated 2026-08-03.

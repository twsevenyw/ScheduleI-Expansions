# UI-STYLE.md — Schedule I mod UI style guide

**Target:** a settings screen for MelonLoader / IL2CPP mods that is visually indistinguishable from shipped Schedule I UI.
**Game build:** Unity **2022.3.62f2** (`raw/assetstudio-info-globalgamemanagers.assets.txt:235`).
**Canvas space:** all pixel values in this document are **1920×1080 canvas reference pixels** unless stated otherwise. See §6.1.

Legend: **[V]** = verified against an extracted asset or a measured pixel. **[D]** = derived by calculation from verified data. **[I]** = inferred / judgement call, not directly verified.

---

## 0. Executive summary

| Thing | Answer |
|---|---|
| Typeface | **Open Sans**, entire weight+italic matrix shipped in-game |
| Card title | `OpenSans-SemiBold SDF` @ **18**, `#FFFFFF` |
| Card description | `OpenSans-SemiBoldItalic SDF` @ **12**, `#FFFFFF` (reads grey purely because it is 12 px) |
| Unselected card fill | `#454749` |
| Selected card fill | `#359A38` |
| Selected card border | `#FFFFFF`, 2 px, rounded |
| Card corner radius | **8.5 px** |
| Card background sprite | `Rectangle_RoundedEdges`, `Image.Type.Sliced`, `pixelsPerUnitMultiplier = 30` |
| Trophy | game sprite `trophy`, tinted `#FFC04B` — **exact match to the reference** |
| Canvas | `ScaleWithScreenSize`, **1920×1080**, `MatchWidthOrHeight`, match = **0**, refPPU **100** |
| Hover / press | `Selectable.Transition.ColorTint`, `fadeDuration = 0.1` |

---

## 1. Palette

### 1.1 Verified values

All "reference" values come from `raw/refscreenshot-measurements.txt` plus direct `System.Drawing.GetPixel` probing of the user's reference PNG (327×285, native 1:1 — proven native because the selected card's 2 px white border is two rows of exact `#FFFFFF` with **zero** blended edge pixels; any downscale would have blurred it). **[V]**

| Role | Hex | RGBA float | Source | Status |
|---|---|---|---|---|
| Page / backdrop | `#010101` | `0.004, 0.004, 0.004, 1` | ref PNG, 864 px of `#010101` + 655 px of `#020202` outside the cards | **[V]** |
| Backdrop pattern highlight | `#0A0B0A` → `#0F0F0F` | — | faint criss-cross line texture over the backdrop, 117 px at `#0A0B0A` | **[V]** (source art unidentified — see §7.2) |
| Unselected card fill | `#454749` | `0.2706, 0.2784, 0.2863, 1` | ref PNG, 29 500 px = **31.65 %** of the whole image | **[V]** |
| Unselected card border | *none* | — | at `y=60`, `x=9` is `#030403` and `x=10` is already `#454749` — hard edge, no stroke | **[V]** |
| Selected card fill | `#359A38` | `0.2078, 0.6039, 0.2196, 1` | ref PNG, 16 350 px = **17.54 %** | **[V]** |
| Selected card border | `#FFFFFF` | `1, 1, 1, 1` | ref PNG, exactly 2 px on all four sides (`x=10,11` and `x=318,319`; `y=185,186` and `y=271,272`) | **[V]** |
| Card title text | `#FFFFFF` | `1, 1, 1, 1` | 201 / 227 / 174 exact-white pixels per title; top-5 % ink luma = **255.0** | **[V]** |
| Card description text | `#FFFFFF` | `1, 1, 1, 1` | top-5 % ink luma = **248.4 / 247.6 / 242.9**; ink histogram tails smoothly to 255 with 45 pixels ≥ 250 scattered across every glyph row | **[V]** — see note below |
| Trophy icon | `#FFC04B` | `1, 0.7529, 0.2941, 1` | 13 696 px in `raw/ref-trophy-zoom.png`; 214 px in the source screenshot | **[V]** |

> **The description is white, not grey.** This is the single most important palette finding and it contradicts the naive reading of the screenshot. Proof: a truncated ramp (i.e. real grey text) can produce **no** pixel brighter than the fill colour, yet the description band contains 45 pixels ≥ `#FAFAFA` including 4 at exact `#FFFFFF`, spatially scattered across both lines from `x=25` to `x=304`. The apparent greyness is a rasterisation artefact: at 12 px an Open Sans SemiBold Italic stem is ≈1.4 px wide, so SDF antialiasing means almost no pixel ever reaches full coverage. **If you set the description to an actual grey it will look washed out next to the reference.** Use `Color.white`; if it still reads too hot on your monitor, `#F0F0F0` is the furthest I would go. **[V]** / **[I]** on the fallback.

### 1.2 State tints — taken from the game's own `Button.ColorBlock`s

Aggregated over all **545** `Button` MonoBehaviours in `raw/dump-l0-mb/Button *.txt`. **[V]**

| ColorBlock (Normal / Highlighted / Pressed / Disabled) | Count | What it is |
|---|---|---|
| `#FFFFFF a0` / `#FFFFFF a100` / `#FFFFFF a150` / `#FFFFFF a128` | 80 | white overlay flash |
| `#000000 a0` / `#000000 a47` / `#000000 a110` / `#C8C8C8 a128` | 64 | **black darkening overlay** |
| `#707070` / `#4D4D4D` / `#3F3F3F` / `#C8C8C8 a128` | 38 | direct tint on the graphic |
| `#FFFFFF` / `#F5F5F5` / `#C8C8C8` / `#C8C8C8 a128` | 27 | direct tint, subtle |
| `#F5F5F5` / `#DCDCDC` / `#C8C8C8` / `#C8C8C8 a128` | 24 | direct tint, subtle |

`m_Transition = 1` (**ColorTint**) on all 473 buttons that declare one. `m_FadeDuration = 0.1` on 406, `0.05` on 63. **[V]**

**Recommendation for the card:** the black-overlay block. The white-overlay block is what the game uses on small icon buttons; at full card width a 39 % white flash is overpowering. **[I]**

Resulting composited colours **[D]**:

| State | Tint on the overlay | Unselected card | Selected card |
|---|---|---|---|
| Normal | `#000000` α 0 | `#454749` | `#359A38` |
| Hover | `#000000` α 0.184 (47/255) | `#383A3C` | `#2B7E2E` |
| Pressed | `#000000` α 0.431 (110/255) | `#27282A` | `#1E5820` |
| Disabled | `#C8C8C8` α 0.502 (128/255) | `#878888` | `#7FB180` |

> **Critical implementation detail.** `Selectable` tinting is *multiplicative*: `Graphic.CrossFadeColor` writes the tint into the `CanvasRenderer`, and the rendered colour is `Image.color × tintColor`. So the overlay `Image` must be **opaque white** (`Color.white`) and the `ColorBlock` alphas do all the work. If you set the overlay's own colour to transparent black, every state multiplies to alpha 0 and nothing ever shows. This is the same reason the game's blocks are written as `#000000 a0 → #000000 a47`: the graphic underneath them is opaque. **[D]**

---

## 2. Typography

### 2.1 The typeface

`raw/extract-fonts/` contains the real TTFs pulled from the game: the full **Open Sans** matrix (Light, Regular, Medium, SemiBold, Bold, ExtraBold + every italic), plus `Caveat-Regular.ttf`, `PerfectDOSVGA437.ttf`, `fs-sevegment.ttf`, `LiberationSans.ttf`. **The game's UI typeface is Open Sans.** **[V]**

### 2.2 TMP_FontAsset names as they exist at runtime

Exactly these 15 strings, from the `m_Name` field of each dump in `raw/dump-tmpfonts/`. **[V]**

```
Caveat-Regular SDF          OpenSans-Bold Lit SDF       OpenSans-Regular SDF
ComicNeue-Bold SDF          OpenSans-Bold SDF           OpenSans-SemiBold SDF
ComicNeue-BoldItalic SDF    OpenSans-BoldItalic SDF     OpenSans-SemiBoldItalic SDF
fs-sevegment SDF            OpenSans-Light SDF
LiberationSans SDF          OpenSans-Medium SDF
LiberationSans SDF - Fallback   OpenSans-MediumItalic SDF
```

> There is **no** `OpenSans-Italic SDF` and no `OpenSans-ExtraBold SDF`. The only italics available at runtime are BoldItalic, MediumItalic, SemiBoldItalic and ComicNeue-BoldItalic. **[V]**

### 2.3 Which font the game actually uses

Aggregated `m_fontAsset` across all **1037** `TextMeshProUGUI` components in `raw/dump-l0-mb/`, with pathIDs resolved against the embedded font assets in the same dump (e.g. `OpenSans-SemiBold SDF @79218.txt`). **[V]**

| pathID | Font asset | Components |
|---|---|---|
| 79218 | **`OpenSans-SemiBold SDF`** | **513** |
| 79209 | `ComicNeue-Bold SDF` | 255 |
| 79212 | `OpenSans-Bold SDF` | 98 |
| 79219 | **`OpenSans-SemiBoldItalic SDF`** | **85** |
| 79215 | `OpenSans-Medium SDF` | 28 |
| 79120 | `LiberationSans SDF` | 27 |
| 79213 | `OpenSans-BoldItalic SDF` | 13 |
| 79217 | `OpenSans-Regular SDF` | 10 |
| 79210 | `ComicNeue-BoldItalic SDF` | 8 |

Font sizes actually used, most common first: **14** (225), **20** (172), **24** (157), **18** (124), **12** (117), 28 (66), 16 (39), 10 (31). `m_fontStyle = 0` on 1034 of 1037 — **the game never uses TMP's synthetic bold/italic; it swaps the font asset.** Do the same. `m_characterSpacing = 0` and `m_wordSpacing = 0` on all 1037. `m_lineSpacing = 0` on 1025. **[V]**

### 2.4 The reference screen's exact roles — measured, not guessed

Method: every TTF in `raw/extract-fonts/` was loaded individually into its own `PrivateFontCollection`, each string rendered with GDI+ unhinted antialiasing at `GenericTypographic` metrics, and the **ink bounding box** measured — apples-to-apples with the ink extents measured off the reference PNG. Total absolute error summed across strings; the global minimum over 50 (weight × size) combinations is reported.

**Titles** — reference ink widths `Hireable Drivers` = 139, `Police Improvements` = 181, `Special Customers` = 157: **[V]**

| Candidate | Rendered widths | Total error |
|---|---|---|
| **`OpenSans-SemiBold` @ 18** | **138 / 181 / 158** | **2 px** ← |
| `OpenSans-Medium` @ 18.5 | 139 / 182 / 159 | 3 px |
| `OpenSans-Bold` @ 17.5 | 140 / 183 / 158 | 4 px |
| `OpenSans-Regular` @ 18.5 | 136 / 179 / 157 | 5 px |
| `OpenSans-ExtraBold` @ 17 | 141 / 184 / 159 | 7 px |

**Descriptions** — reference ink widths for the seven wrapped lines = 281 / 224 / 262 / 259 / 223 / 274 / 172: **[V]**

| Candidate | Rendered widths | Total error |
|---|---|---|
| **`OpenSans-SemiBoldItalic` @ 12** | **281 / 223 / 263 / 260 / 222 / 274 / 172** | **4 px** ← |
| `OpenSans-BoldItalic` @ 11.5 | 279 / 222 / 262 / 258 / 220 / 273 / 170 | 11 px |
| `OpenSans-ExtraBoldItalic` @ 11 | 279 / 221 / 262 / 257 / 220 / 273 / 171 | 12 px |
| `OpenSans-MediumItalic` @ 12 | 276 / 219 / 257 / 255 / 218 / 269 / 168 | 33 px |

Both winners land on **integer sizes**, both are **sizes the game itself uses** (18 → 124 components, 12 → 117 components), and both are the game's **two most-used Open Sans assets**. `OpenSans-SemiBoldItalic SDF` at exactly size 12 appears on **30** real game components. Three independent lines of evidence converge. **[V]**

### 2.5 Text role table

| Role | Font asset | Size | Colour | Alignment | Line spacing | Char spacing |
|---|---|---|---|---|---|---|
| Card title | `OpenSans-SemiBold SDF` | 18 | `#FFFFFF` | Center | 0 (default) | 0 |
| Card description | `OpenSans-SemiBoldItalic SDF` | 12 | `#FFFFFF` | Center, word-wrap on | 0 (default) | 0 |
| Screen heading | `OpenSans-SemiBold SDF` | 24 | `#FFFFFF` | Center | 0 | 0 |
| Section label | `OpenSans-SemiBold SDF` | 14 | `#FFFFFF` | Left | 0 | 0 |
| Button label | `OpenSans-SemiBold SDF` | 14 | `#FFFFFF` | Center | 0 | 0 |
| Small / hint | `OpenSans-Medium SDF` | 12 | `#FFFFFF` | Left | 0 | 0 |

Heading/label/button rows are **[I]** — extrapolated from the game's size histogram, not measured off the reference (which contains only cards).

**Line pitch, measured:** description baseline-to-baseline = **16 px** in card 1 (strong-ink rows 62–68 then 78–84) and 16/17 alternating in card 3. Open Sans `lineSpacing / em` = 2789 / 2048 = **1.3618**, so TMP's default at size 12 is 12 × 1.3618 = **16.34 px**. Measured 16. **Leave `lineSpacing` at 0.** **[V] + [D]**

### 2.6 Runtime font lookup

Never ship a font. Grab the loaded `TMP_FontAsset` and — critically — its `fontSharedMaterial`, which carries the SDF face-dilate / outline preset that makes text look like the game's.

```csharp
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;

public static class S1Fonts
{
    // Verified runtime names, in fallback order per role.
    public const string Title = "OpenSans-SemiBold SDF";
    public const string Desc  = "OpenSans-SemiBoldItalic SDF";

    static readonly string[] TitleChain = { "OpenSans-SemiBold SDF", "OpenSans-Bold SDF",
                                            "OpenSans-Medium SDF",   "OpenSans-Regular SDF",
                                            "LiberationSans SDF" };
    static readonly string[] DescChain  = { "OpenSans-SemiBoldItalic SDF", "OpenSans-BoldItalic SDF",
                                            "OpenSans-MediumItalic SDF",   "OpenSans-SemiBold SDF",
                                            "LiberationSans SDF" };

    static Dictionary<string, TMP_FontAsset> _cache;
    static Material _sharedMat;

    /// Call once from OnSceneWasInitialized. FindObjectsOfTypeAll walks the whole
    /// IL2CPP heap for the type - never call it per frame.
    public static void Harvest()
    {
        _cache = new Dictionary<string, TMP_FontAsset>();
        Il2CppArrayBase<TMP_FontAsset> all = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        foreach (TMP_FontAsset f in all)
        {
            if (f == null || string.IsNullOrEmpty(f.name)) continue;
            if (!_cache.ContainsKey(f.name)) _cache[f.name] = f;
        }

        // Steal the material preset from live on-screen text using our primary font.
        foreach (TextMeshProUGUI t in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())
        {
            if (t == null || t.font == null) continue;
            if (!t.gameObject.scene.IsValid()) continue;     // skip prefabs and imported assets
            if (t.font.name != Title) continue;
            if (t.fontSharedMaterial == null) continue;
            _sharedMat = t.fontSharedMaterial;
            break;
        }
        MelonLogger.Msg($"[S1Fonts] cached {_cache.Count} font assets, material='{_sharedMat?.name ?? "none"}'");
    }

    public static TMP_FontAsset Get(string preferred)
    {
        if (_cache == null) Harvest();

        if (_cache.TryGetValue(preferred, out TMP_FontAsset exact) && exact != null) return exact;

        string[] chain = preferred == Desc ? DescChain : TitleChain;
        foreach (string n in chain)
            if (_cache.TryGetValue(n, out TMP_FontAsset f) && f != null) return f;

        // Loose contains-match before giving up (guards against a patch renaming assets).
        foreach (KeyValuePair<string, TMP_FontAsset> kv in _cache)
            if (kv.Key.StartsWith("OpenSans") && kv.Value != null) return kv.Value;

        foreach (KeyValuePair<string, TMP_FontAsset> kv in _cache)
            if (kv.Value != null) return kv.Value;

        MelonLogger.Warning("[S1Fonts] no TMP_FontAsset found at all");
        return null;
    }

    /// Applies the game's SDF material preset. Skip if you want flat text.
    public static void ApplyMaterial(TMP_Text t)
    {
        if (_sharedMat != null && t.font != null && t.font.name == Title)
            t.fontSharedMaterial = _sharedMat;
    }
}
```

> Each `TMP_FontAsset` carries exactly one atlas + material. Assigning a material from font A onto text using font B renders garbage. `ApplyMaterial` therefore guards on the name. **[I]**

---

## 3. Component anatomy — the toggle card

All numbers measured with `GetPixel` on the reference PNG at native scale. Card origin = the card's top-left outer corner.

### 3.1 Measured geometry **[V]**

| Metric | Value | Evidence |
|---|---|---|
| Card width | **310 px** | left edge `x=10`, right edge `x=319` |
| Unselected card height | **71 px** | card 1 `y=23..93`; card 2 `y=104..174` |
| Selected card height (3 desc lines) | **88 px** | `y=185..272` incl. 2 px border top and bottom |
| Gap between cards | **10 px** | `y=94..103` and `y=175..184` |
| Corner radius | **≈ 8.5 px** | corner inset falls 8 → 0 over 7 rows on card 2's top-left |
| Selected border thickness | **2 px**, all four sides | pure `#FFFFFF`, no antialiasing |
| Title cap top | card top **+ 11 px** | card 1 top `y=23`, `H` top `y=34` |
| Title baseline | card top **+ 24 px** | `y=47` |
| Title cap height | **13 px** | matches Open Sans cap 1462/2048 × 18 = 12.85 |
| Desc line 1 baseline | card top **+ 45 px** | `y=68` |
| Desc line 2 baseline | card top **+ 61 px** | `y=84` |
| Desc line pitch | **16 px** | see §2.5 |
| Title → desc baseline gap | **21 px** | 68 − 47 |
| Last baseline → card bottom | **9 px** | 93 − 84 |
| Title horizontal centre | card centre (164.5) | card 1 title ink `x=96..234`, centre 165 |
| Trophy ink box | **19 × 19 px** | gold bbox `x=19..37`, `y=194..212` |
| Trophy offset from card inner corner | **+7, +7** | inner edge starts at `x=12, y=187` |
| Trophy vertical centre | 203 | title vertical centre ≈ 202.5 → **trophy is centred on the title row** |

### 3.2 Text wrap width — independently confirms horizontal padding **[D]**

The longest rendered description line is 281 px; the shortest line that *would* have overflowed is 299 px (line 2 of card 3, 274 px, plus a 4 px space plus "buy" at ≈21 px). Therefore the TMP wrap width lies in **[281, 298]**, i.e. horizontal padding is between 6 and 14.5 px. **12 px reproduces every line break in the reference exactly.**

### 3.3 Layout recipe

Solved against Open Sans metrics (ascender 2189/2048 = 1.0688 em, line height 1.3618 em):

```
Card RectTransform
  width            310   (or stretch to the list container)
  minHeight        71    (LayoutElement)
  height           driven by ContentSizeFitter.verticalFit = PreferredSize

VerticalLayoutGroup on Card
  padding          left 12, right 12, top 5, bottom 5
  spacing          3
  childAlignment   UpperCenter
  childForceExpandWidth   true
  childForceExpandHeight  false
  childControlWidth       true
  childControlHeight      true

Title  TextMeshProUGUI   size 18, Center,  LayoutElement.preferredHeight = 25
Desc   TextMeshProUGUI   size 12, Center, word-wrap on, ContentSizeFitter-free
                          (the layout group sizes it)

List container
  VerticalLayoutGroup.spacing = 10
  padding left/right 10
```

Verification of the recipe against the measurement **[D]**:
`5 + 24.5 (title line box) + 3 + 32.7 (2 desc lines) + 5 = 70.2` → measured **71**.
Three-line card: `5 + 24.5 + 3 + 49.0 + 5 = 86.5` → measured **88**.
Title baseline = `card top + 5 + (18 × 1.0688) = card top + 24.2` → measured **+24**.

### 3.4 The 2 px border does not change the box

Card 3 (3 description lines, with border) is 88 px; the same content without a border computes to 86.5 px. The border therefore **overlays the card rect** rather than wrapping it — implement it as a fill inset inside the card graphic, not as an outer container. See §5.4. **[D]**

### 3.5 Trophy

24 × 24 px `Image`, anchored top-left, `anchoredPosition = (+5, −5)` from the card's inner top-left, `ignoreLayout = true`. The gold ink measures 19 × 19; the game sprite carries a soft black halo, so give it ≈24 px of box. **[D]**

---

## 4. Sprite reuse plan

### 4.1 9-slice source data

From `raw/sprite-9slice.tsv` and `raw/sprite-inventory.tsv`, both **[V]**:

| Sprite | Size | Border (L,B,R,T) | PPU | pathID | Shape (probed from the PNG) |
|---|---|---|---|---|---|
| `Rectangle_RoundedEdges` | 512×512 | 255,255,255,255 | 100 | 5077 | solid; corner arc is a full quarter-circle of r=255 |
| `Rectangle_RoundedEdges_Top` | 512×512 | 255,255,255,255 | 100 | 4955 | two corners rounded, two square — **see naming warning below** |
| `Rectangle_RoundedEdges_Bottom` | 512×512 | 255,255,255,255 | 100 | 4788 | two corners rounded, two square — **see naming warning below** |
| `Outline_RoundedEdges` | 512×512 | 255,255,255,255 | 100 | 4888 | ring, **100 px** thick |
| `Outline_RoundedEdges 1` | 512×512 | 255,255,255,255 | 100 | 4758 | ring, **40 px** thick |
| `WhiteFrame_4px` | 64×64 | 8,8,8,8 | 100 | 4811 | **square-cornered** frame, 4 px stroke, `#EFF0EF` |
| `Rect shade` | 256×256 | 122,122,122,122 | 100 | 4960 | soft black drop shadow / vignette |
| `UISprite` | 32×32 | 10,10,10,10 | 200 | 4727 | Unity default rounded box, `#F5F5F5` with `#323232` 1 px edge |
| `Background` | 32×32 | 10,10,10,10 | 200 | 4728 | Unity default |
| `InputFieldBackground` | 32×32 | 10,10,10,10 | 200 | 4729 | white rounded box with dark edge |
| `UIMask` | 32×32 | 10,10,10,10 | 200 | 4731 | Unity default mask |
| `property_outline` | 32×32 | 8,8,8,8 | 150 | 5041 | square-cornered frame, 8 px stroke |
| `CustomUI_Spritesheet_64px_0` | 120×120 | 20,20,20,20 | 500 | 5149 | **square-cornered** hollow frame, 16 px stroke |
| `CustomUI_Spritesheet_64px_2` | 120×120 | 48,48,48,48 | 500 | 5150 | rounded frame, r≈27 px, 16 px stroke |
| `CustomUI_Spritesheet_64px_3` | 248×248 | 92,92,92,92 | 500 | 5151 | rounded frame, r≈88 px, 16 px stroke |

> **Naming warning on the half-rounded pair.** Probing the exported PNGs (which AssetStudio writes in correct visual orientation — `trophy.png` exports upright, so row 0 is the visual top): `Rectangle_RoundedEdges_Top` has its **square** corners on the top row and its **rounded** corners on the bottom row, and `Rectangle_RoundedEdges_Bottom` is the reverse. That is the opposite of what the names suggest. Assume nothing — put one on screen and look at it before committing. **[V]** on the pixels, **[I]** on which way it lands after Unity's texture-space flip.

### 4.2 The sizing formula that decides everything

```
on-screen slice size (canvas px) = spriteBorderPx × (canvas.referencePixelsPerUnit / sprite.pixelsPerUnit)
                                                   ÷ image.pixelsPerUnitMultiplier
```

With `canvas.referencePixelsPerUnit = 100` (the game's value, §6.1) and `Rectangle_RoundedEdges` at PPU 100, this collapses to:

> **corner radius (px) = 255 ÷ `pixelsPerUnitMultiplier`**

| `pixelsPerUnitMultiplier` | Radius | Times the game uses it on this sprite |
|---|---|---|
| 15 | 17.0 px | 20 |
| 20 | 12.75 px | 50 |
| 25 | 10.2 px | **104** |
| **30** | **8.5 px** | **63** ← the card |
| 33.3 | 7.66 px | 21 |
| 40 | 6.38 px | 70 |
| 50 | 5.1 px | 57 |
| 80 | 3.19 px | 20 |
| 100 | 2.55 px | 21 |

`pixelsPerUnitMultiplier = 30` gives 8.5 px, matches the measured card radius, **and is a value the game itself uses 63 times.** **[V] + [D]**

### 4.3 What the game actually uses these sprites for

Aggregated across all **3745** `Image` components in level 0. Every `Rectangle_RoundedEdges` usage is `Image.Type.Sliced`. **[V]**

| Sprite | `Image` refs in level 0 | Verdict |
|---|---|---|
| `Circle` | 566 | icon/avatar masks, `Type.Simple` |
| `Rectangle_RoundedEdges` | **477** | **the game's canonical rounded panel — use this** |
| `CustomUI_Spritesheet_64px_0` | 231 | square outline frame |
| `Rect shade` | 84 | drop shadows, `ppuMult` 5–9 |
| `UISprite` | 81 | Unity leftovers |
| `WhiteFrame_4px` | 75 | square outlines, `ppuMult` 1 and 1.5 |
| `Background` | 55 | Unity leftovers |
| `Tick` | 32 | checkmarks |
| `Outline_RoundedEdges 1` | 5 | rare |
| `trophy`, `gear_icon`, `Tick circle`, `MenuTitle`, `Outline_RoundedEdges` | 1 each | **thin — see §4.6** |

### 4.4 Element-by-element plan

| Element | Sprite | `Image.type` | `pixelsPerUnitMultiplier` | Result | Status |
|---|---|---|---|---|---|
| Card background / border layer | `Rectangle_RoundedEdges` | Sliced | **30** | r = 8.5 px | **[V]** sprite, **[D]** multiplier |
| Card fill (inset 2 px inside the border) | `Rectangle_RoundedEdges` | Sliced | **40** | r = 6.38 px — nests correctly inside an 8.5 px outer radius | **[D]** |
| Hover / press overlay | `Rectangle_RoundedEdges` | Sliced | **30** | matches the card silhouette exactly | **[D]** |
| Selected badge / trophy | **`trophy`** | Simple | 1 | tint `#FFC04B`; 24×24 box | **[V]** — shape-matched, see §4.5 |
| Panel background | `Rectangle_RoundedEdges` | Sliced | **25** | r = 10.2 px, the game's most common panel radius | **[V]** |
| Panel drop shadow | `Rect shade` | Sliced | **9** | 13.6 px spread; the game's own value (36 uses) | **[V]** |
| Scrollbar track | `Rectangle_RoundedEdges` | Sliced | **50** | r = 5.1 px, colour `#000000` α 0.25 | **[I]** |
| Scrollbar handle | `Rectangle_RoundedEdges` | Sliced | **50** | r = 5.1 px, colour `#707070` (the game's Button normal grey) | **[I]** |
| Toggle knob / check | `Tick` or `Tick circle` | Simple | 1 | white, tint per state | **[V]** sprite, **[I]** usage |
| Input field | `InputFieldBackground` | Sliced | 1 | Unity default, matches the game's own use | **[V]** |
| Square outline (alt style) | `WhiteFrame_4px` | Sliced | **2** | exactly 2 px stroke, **square corners** | **[D]** |
| Rounded 2 px outline, single Image (alt) | `Outline_RoundedEdges 1` | Sliced | **20** | 2.0 px ring but outer r = 12.75 px, not 8.5 | **[D]** |

### 4.5 The trophy is the game's own sprite — confirmed

The reference trophy and `raw/extract-uisprites/trophy.png` downscaled to 19 px are **structurally identical**: same rim bar, same two open handles, same tapered stem, same base plinth.

```
REF  y=194 |   ############    |     GAME y=3  |  ..############.. |
REF  y=197 |## .########### .##|     GAME y=6  |  .#..########..#. |
REF  y=200 |##. ##########. ##.|     GAME y=9  |   .##.######.##.  |
REF  y=203 | .##.########.###  |     GAME y=12 |      ..####..     |
REF  y=206 |      .####..      |     GAME y=15 |     .########.    |
```

`trophy.png` is a **white** silhouette with black interior shading and a soft black halo. Tinting the `Image` `#FFC04B` multiplies white → gold and leaves the black shading and halo black — which is exactly the gold-with-dark-outline look in the reference (the reference's outline pixels sample as `#1D5D1F` / `#103E11`, i.e. black blended against `#359A38`). **No custom art required.** **[V]**

### 4.6 Scene availability caveat

`Resources.FindObjectsOfTypeAll<Sprite>()` only returns sprites **resident in memory**. A sprite is resident if something loaded references it.

- **Safe anywhere** (hundreds of references in level 0): `Rectangle_RoundedEdges`, `Circle`, `Rect shade`, `WhiteFrame_4px`, `UISprite`, `Background`, `UIMask`, `CustomUI_Spritesheet_64px_0`.
- **Thin — verify before relying on it**: `trophy`, `gear_icon`, `Tick circle`, `MenuTitle`, `Outline_RoundedEdges` each have exactly **one** referencing `Image` in level 0. If that one object's prefab is not instantiated, the sprite will not be found. Harvest in **both** `OnSceneWasInitialized` for the menu scene *and* the gameplay scene, and keep the procedural fallback in §4.7. **[V]** count, **[I]** risk assessment.

### 4.7 Sprite lookup helper

```csharp
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using UnityEngine;

public static class S1Sprites
{
    public const string Card    = "Rectangle_RoundedEdges";
    public const string Ring    = "Outline_RoundedEdges 1";
    public const string Frame   = "WhiteFrame_4px";
    public const string Shadow  = "Rect shade";
    public const string Trophy  = "trophy";
    public const string Gear    = "gear_icon";
    public const string Tick    = "Tick";

    static readonly Dictionary<string, Sprite> _cache = new();
    static Sprite _fallbackRounded;

    /// Call from OnSceneWasInitialized for BOTH the menu scene and the gameplay scene.
    /// Later scenes add sprites; existing entries are kept.
    public static void Harvest()
    {
        Il2CppArrayBase<Sprite> all = Resources.FindObjectsOfTypeAll<Sprite>();
        int added = 0;
        foreach (Sprite s in all)
        {
            if (s == null || string.IsNullOrEmpty(s.name)) continue;
            if (_cache.ContainsKey(s.name)) continue;
            _cache[s.name] = s;
            added++;
        }
        MelonLogger.Msg($"[S1Sprites] +{added} sprites (total {_cache.Count})");
    }

    public static Sprite Get(string name, params string[] fallbacks)
    {
        if (_cache.Count == 0) Harvest();

        if (_cache.TryGetValue(name, out Sprite s) && s != null) return s;
        foreach (string f in fallbacks)
            if (_cache.TryGetValue(f, out Sprite g) && g != null) return g;

        MelonLogger.Warning($"[S1Sprites] '{name}' not resident; using procedural fallback");
        return RoundedFallback();
    }

    /// Last resort: a 64x64 white rounded square with border 16, PPU 100.
    /// Same sizing maths as Rectangle_RoundedEdges, just radius 16 instead of 255,
    /// so use pixelsPerUnitMultiplier = 16 / desiredRadius with this one.
    public static Sprite RoundedFallback()
    {
        if (_fallbackRounded != null) return _fallbackRounded;

        const int N = 64, R = 16;
        Texture2D tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode   = TextureWrapMode.Clamp;

        Color[] px = new Color[N * N];
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            float dx = Mathf.Max(R - x - 0.5f, 0f, x + 0.5f - (N - R));
            float dy = Mathf.Max(R - y - 0.5f, 0f, y + 0.5f - (N - R));
            float d  = Mathf.Sqrt(dx * dx + dy * dy);
            float a  = Mathf.Clamp01(R - d + 0.5f);
            px[y * N + x] = new Color(1f, 1f, 1f, a);
        }
        tex.SetPixels(px);
        tex.Apply(false, false);

        _fallbackRounded = Sprite.Create(
            tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f),
            100f, 0u, SpriteMeshType.FullRect, new Vector4(R, R, R, R), false);
        _fallbackRounded.name = "S1Mod_RoundedFallback";
        Object.DontDestroyOnLoad(tex);
        return _fallbackRounded;
    }
}
```

`Sprite.Create(Texture2D, Rect, Vector2, float, uint, SpriteMeshType, Vector4, bool)` — the 8-argument overload carrying `border` — exists in Unity 2022.3 and the generated interop signature matches (`raw/c2-ugui-runtime.md:1191`). **[V]**

---

## 5. Runtime uGUI construction

### 5.1 IL2CPP quirks that matter here

All **[V]** from `raw/c2-ugui-runtime.md` §1.1–1.5 unless noted.

- **Namespaces:** `UnityEngine`, `UnityEngine.UI`, `UnityEngine.Events`, `UnityEngine.EventSystems` are **not** prefixed. TextMeshPro **is**: `using Il2CppTMPro;`. This is MelonLoader quirk [#912](https://github.com/LavaGang/MelonLoader/issues/912).
- **`AddComponent<T>()`** — the generic form works for every Unity type and every game type. `Il2CppType.Of<T>()` is only needed for the non-generic overload, which returns `Component` and must be `.Cast<T>()` (throws) or `.TryCast<T>()` (returns null).
- **`Resources.FindObjectsOfTypeAll<T>()`** returns `Il2CppArrayBase<T>`. Declare locals as `Il2CppArrayBase<T>` or `var` — `Il2CppReferenceArray<T>` derives from it, so the reverse assignment is a `CS0266`.
- **`Button.onClick.AddListener`** needs a real `UnityAction`: `b.onClick.AddListener((UnityAction)(() => Foo()));`. A bare lambda or `System.Action` will not bind. `DelegateSupport.ConvertDelegate<UnityAction>(action)` is the explicit form.
- **`RemoveAllListeners()` does not clear serialised calls.** If you ever clone a game button, do `b.onClick = new Button.ButtonClickedEvent();`.
- **`HarmonyLib.Harmony` must be fully qualified** — a `Harmony` namespace exists in the interop set, so `new Harmony("id")` is `CS0118`.
- **`rt.SetParent(parent, false)`** — always `false` for UI, or you inherit world scale.
- **IMGUI is stripped in this build** (`GUILayout.TextField`, `GUI.DrawTexture`). This does not affect uGUI at all. The one adjacent thing to avoid: do **not** fall back to `OnGUI` for a debug overlay — it will not render. Use a uGUI `TextMeshProUGUI` instead. **[I]** on the fallback advice.
- **Do not inject `MonoBehaviour` subclasses** unless you need to. Drive state from `MelonMod.OnUpdate()` plus `Button.onClick` lambdas — no `ClassInjector`, no `OnEnable` failure modes. The hover handling in §5.6 is written this way deliberately.

### 5.2 Canvas

```csharp
using UnityEngine;
using UnityEngine.UI;

public static Canvas BuildCanvas()
{
    GameObject root = new GameObject("S1CreativeMode_Canvas");
    Object.DontDestroyOnLoad(root);
    root.hideFlags = HideFlags.HideAndDontSave;
    root.layer     = 5;                                    // "UI"

    Canvas canvas = root.AddComponent<Canvas>();
    canvas.renderMode            = RenderMode.ScreenSpaceOverlay;
    canvas.sortingOrder          = 30000;
    canvas.overrideSorting       = true;
    canvas.referencePixelsPerUnit = 100f;                  // matches the game - see 6.1
    canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1
                                    | AdditionalCanvasShaderChannels.Normal
                                    | AdditionalCanvasShaderChannels.Tangent;  // TMP SDF effects

    CanvasScaler scaler = root.AddComponent<CanvasScaler>();
    scaler.uiScaleMode            = CanvasScaler.ScaleMode.ScaleWithScreenSize;
    scaler.referenceResolution    = new Vector2(1920f, 1080f);
    scaler.screenMatchMode        = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
    scaler.matchWidthOrHeight     = 0f;                    // the game's value - see 6.1
    scaler.referencePixelsPerUnit = 100f;

    GraphicRaycaster gr = root.AddComponent<GraphicRaycaster>();
    gr.ignoreReversedGraphics = true;
    gr.blockingObjects        = GraphicRaycaster.BlockingObjects.None;
    return canvas;
}
```

### 5.3 Primitives

```csharp
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

public static class UI
{
    public static readonly Color PageBg      = new Color32(0x01, 0x01, 0x01, 255);
    public static readonly Color CardIdle    = new Color32(0x45, 0x47, 0x49, 255);
    public static readonly Color CardOn      = new Color32(0x35, 0x9A, 0x38, 255);
    public static readonly Color BorderOn    = Color.white;
    public static readonly Color TextMain    = Color.white;
    public static readonly Color TrophyGold  = new Color32(0xFF, 0xC0, 0x4B, 255);

    public static GameObject New(string name, Transform parent)
    {
        GameObject go = new GameObject(name);
        go.layer = 5;
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.localScale = Vector3.one;
        return go;
    }

    public static void Stretch(RectTransform rt, float inset = 0f)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(inset, inset);
        rt.offsetMax = new Vector2(-inset, -inset);
    }

    /// 9-sliced rounded rect. radiusPx is honoured exactly when the sprite is
    /// Rectangle_RoundedEdges (border 255, PPU 100) and canvas refPPU is 100.
    public static Image Rounded(GameObject go, Color color, float radiusPx)
    {
        Image img = go.AddComponent<Image>();
        img.sprite                  = S1Sprites.Get(S1Sprites.Card);
        img.type                    = Image.Type.Sliced;
        img.fillCenter              = true;
        img.pixelsPerUnitMultiplier = 255f / Mathf.Max(radiusPx, 0.5f);
        img.color                   = color;
        return img;
    }

    public static TextMeshProUGUI Text(GameObject go, string fontName, float size,
                                       Color color, TextAlignmentOptions align, bool wrap)
    {
        TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
        t.font              = S1Fonts.Get(fontName);
        S1Fonts.ApplyMaterial(t);
        t.fontSize          = size;
        t.color             = color;
        t.alignment         = align;
        t.enableWordWrapping = wrap;
        t.richText          = true;
        t.raycastTarget     = false;          // labels must never eat clicks
        t.characterSpacing  = 0f;
        t.wordSpacing       = 0f;
        t.lineSpacing       = 0f;             // Open Sans default 1.3618 em is correct
        t.margin            = Vector4.zero;
        return t;
    }
}
```

> `img.pixelsPerUnitMultiplier = 255f / radiusPx` only holds for `Rectangle_RoundedEdges`. If `S1Sprites.Get` fell back to `RoundedFallback()` (border 16), the constant is 16, not 255. Read `img.sprite.border.x` instead of hardcoding if you want this bulletproof. **[I]**

### 5.4 The toggle card

```csharp
using System;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public sealed class ToggleCard
{
    public GameObject Root;
    public Image      Border;      // outer graphic - white when selected
    public Image      Fill;        // inset 2px  - grey when off, green when on
    public Image      Hover;       // Button.targetGraphic
    public Image      Trophy;
    public bool       IsOn;

    const float Radius      = 8.5f;   // 255 / 30
    const float FillRadius  = 6.375f; // 255 / 40, nests inside 8.5 with a 2px gap
    const float BorderPx    = 2f;

    public static ToggleCard Build(Transform parent, string title, string desc,
                                   bool startOn, bool showTrophy, Action<bool> onToggle)
    {
        ToggleCard c = new ToggleCard();

        // ---- root: the border graphic doubles as the card silhouette ----
        c.Root = UI.New("Card_" + title, parent);
        RectTransform rt = c.Root.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot     = new Vector2(0.5f, 1f);

        c.Border = UI.Rounded(c.Root, UI.CardIdle, Radius);
        c.Border.raycastTarget = true;

        VerticalLayoutGroup v = c.Root.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(12, 12, 5, 5);
        v.spacing = 3f;
        v.childAlignment          = TextAnchor.UpperCenter;
        v.childControlWidth       = true;
        v.childControlHeight      = true;
        v.childForceExpandWidth   = true;
        v.childForceExpandHeight  = false;

        ContentSizeFitter fit = c.Root.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fit.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

        LayoutElement rootLE = c.Root.AddComponent<LayoutElement>();
        rootLE.minHeight = 71f;

        // ---- [0] fill, inset by the border thickness ----
        GameObject fillGo = UI.New("Fill", c.Root.transform);
        c.Fill = UI.Rounded(fillGo, UI.CardIdle, FillRadius);
        c.Fill.raycastTarget = false;
        UI.Stretch(fillGo.GetComponent<RectTransform>(), BorderPx);
        Ignore(fillGo);

        // ---- [1] hover overlay: the Button tints THIS, never the fill ----
        // MUST be opaque white - ColorTint multiplies Image.color by the tint,
        // so a transparent graphic would stay invisible in every state.
        GameObject hoverGo = UI.New("Hover", c.Root.transform);
        c.Hover = UI.Rounded(hoverGo, Color.white, Radius);
        c.Hover.raycastTarget = false;
        UI.Stretch(hoverGo.GetComponent<RectTransform>());
        Ignore(hoverGo);

        // ---- [2] trophy ----
        if (showTrophy)
        {
            GameObject tro = UI.New("Trophy", c.Root.transform);
            c.Trophy = tro.AddComponent<Image>();
            c.Trophy.sprite        = S1Sprites.Get(S1Sprites.Trophy);
            c.Trophy.type          = Image.Type.Simple;
            c.Trophy.color         = UI.TrophyGold;
            c.Trophy.preserveAspect = true;
            c.Trophy.raycastTarget = false;
            RectTransform trt = tro.GetComponent<RectTransform>();
            trt.anchorMin = trt.anchorMax = new Vector2(0f, 1f);
            trt.pivot     = new Vector2(0f, 1f);
            trt.sizeDelta = new Vector2(24f, 24f);
            trt.anchoredPosition = new Vector2(5f, -5f);
            Ignore(tro);
            if (c.Trophy.sprite == null) c.Trophy.enabled = false;
        }

        // ---- [3] title ----
        GameObject titleGo = UI.New("Title", c.Root.transform);
        UI.Text(titleGo, S1Fonts.Title, 18f, UI.TextMain, TextAlignmentOptions.Center, false)
          .text = title;
        titleGo.AddComponent<LayoutElement>().preferredHeight = 25f;

        // ---- [4] description ----
        GameObject descGo = UI.New("Desc", c.Root.transform);
        UI.Text(descGo, S1Fonts.Desc, 12f, UI.TextMain, TextAlignmentOptions.Center, true)
          .text = desc;

        // ---- button ----
        Button b = c.Root.AddComponent<Button>();
        b.targetGraphic = c.Hover;
        b.transition    = Selectable.Transition.ColorTint;

        ColorBlock cb = b.colors;
        cb.normalColor      = new Color(0f, 0f, 0f, 0f);
        cb.highlightedColor = new Color(0f, 0f, 0f, 47f  / 255f);   // the game's value
        cb.pressedColor     = new Color(0f, 0f, 0f, 110f / 255f);   // the game's value
        cb.selectedColor    = new Color(0f, 0f, 0f, 47f  / 255f);
        cb.disabledColor    = new Color(200f / 255f, 200f / 255f, 200f / 255f, 128f / 255f);
        cb.colorMultiplier  = 1f;
        cb.fadeDuration     = 0.1f;                                 // the game's value
        b.colors = cb;

        // Assigning `colors` kicks off a 0.1s fade from the DEFAULT block (opaque white),
        // which flashes a white card for one fade. Snap the overlay to normal immediately.
        c.Hover.canvasRenderer.SetColor(cb.normalColor);

        Navigation nav = b.navigation;
        nav.mode = Navigation.Mode.None;
        b.navigation = nav;

        ToggleCard self = c;
        b.onClick.AddListener((UnityAction)(() =>
        {
            self.SetOn(!self.IsOn);
            S1Audio.PlayClick();
            onToggle?.Invoke(self.IsOn);
        }));

        c.SetOn(startOn);
        return c;
    }

    static void Ignore(GameObject go)
    {
        LayoutElement le = go.AddComponent<LayoutElement>();
        le.ignoreLayout = true;
    }

    public void SetOn(bool on)
    {
        IsOn = on;
        Border.color = on ? UI.BorderOn : UI.CardIdle;
        Fill.color   = on ? UI.CardOn   : UI.CardIdle;
        Fill.enabled = true;               // keep it enabled: off-state fill == border colour,
                                           // so the 2px ring is invisible without a layout change
        if (Trophy != null) Trophy.gameObject.SetActive(on);
    }
}
```

**Why `Fill` stays enabled when off:** §3.4 proved the border overlays the card box rather than growing it. Keeping the same two-layer structure in both states means the card's outer rect — and therefore the `VerticalLayoutGroup` result — is identical whether selected or not. Only two `Color` assignments change. **[D]**

### 5.5 The card list

```csharp
public static GameObject BuildList(Transform parent)
{
    GameObject list = UI.New("CardList", parent);
    RectTransform rt = list.GetComponent<RectTransform>();
    rt.anchorMin = new Vector2(0f, 0f);
    rt.anchorMax = new Vector2(1f, 1f);
    rt.offsetMin = new Vector2(10f, 10f);
    rt.offsetMax = new Vector2(-10f, -10f);

    VerticalLayoutGroup v = list.AddComponent<VerticalLayoutGroup>();
    v.padding = new RectOffset(0, 0, 0, 0);
    v.spacing = 10f;                       // measured gap between cards
    v.childAlignment         = TextAnchor.UpperCenter;
    v.childControlWidth      = true;
    v.childControlHeight     = true;
    v.childForceExpandWidth  = true;
    v.childForceExpandHeight = false;

    ContentSizeFitter fit = list.AddComponent<ContentSizeFitter>();
    fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
    return list;
}

// After adding or removing cards, force one rebuild - TMP preferred heights are
// not valid until the text has been laid out once.
LayoutRebuilder.ForceRebuildLayoutImmediate(list.GetComponent<RectTransform>());
```

### 5.6 Hover without injecting a MonoBehaviour

`Selectable.Transition.ColorTint` handles the visual state for free. The only thing it does not do is fire a **hover sound**. Rather than injecting an `EventTrigger` subclass, poll the `Selectable` state from `OnUpdate`:

```csharp
// In your MelonMod:
readonly List<ToggleCard> _cards = new();
readonly HashSet<int>     _hovered = new();

public override void OnUpdate()
{
    if (_root == null || !_root.activeInHierarchy) return;

    foreach (ToggleCard c in _cards)
    {
        if (c.Root == null) continue;
        int id = c.Root.GetInstanceID();

        // Hover == the tint has moved off normalColor. Cheap and injection-free.
        bool over = c.Hover != null && c.Hover.canvasRenderer.GetColor().a > 0.02f;

        if (over && _hovered.Add(id))  S1Audio.PlayHover();
        else if (!over)                _hovered.Remove(id);
    }
}
```

**[I]** — this reads the tint that `ColorTint` is already driving, so it needs no `IPointerEnterHandler` and therefore no `ClassInjector`. If you would rather use `EventTrigger`, note that it *does* require an injected callback type under IL2CPP.

---

## 6. Integration + polish

### 6.1 Canvas scaling — verified from the game's own scalers

All **21** `CanvasScaler` components in level 0 were read (`raw/dump-l0-mb/CanvasScaler *.txt`). **[V]**

| `m_UiScaleMode` | Reference resolution | `m_ScreenMatchMode` | `m_MatchWidthOrHeight` | Count |
|---|---|---|---|---|
| 1 = ScaleWithScreenSize | **1920 × 1080** | 0 = MatchWidthOrHeight | **0** (match Width) | **8** |
| 0 = ConstantPixelSize | 1920 × 1080 (ignored in this mode) | 0 | 0 | 1 |
| 0 = ConstantPixelSize | 800 × 600 (Unity's unused default) | 0 | 0 | 6 |
| — | dump truncated (276–280 bytes, no typetree) | — | — | 6 |

`m_ReferencePixelsPerUnit = 100` on all 15 that parsed. **Every scaler in the game that scales with screen size uses 1920 × 1080 — there is no second reference resolution anywhere in level 0.**

> **Use `ScaleWithScreenSize`, `1920 × 1080`, `MatchWidthOrHeight`, `matchWidthOrHeight = 0`, `referencePixelsPerUnit = 100`.**
> `match = 0` means the game scales purely off **width**. On a 21:9 monitor the game's UI gets proportionally taller, and yours must do the same or it will drift. This is the one setting most mods get wrong (they use `0.5`).

`raw/c2-ugui-runtime.md:147` also has a `CopyScalerFromGame()` helper that reads these live rather than hardcoding — worth calling once and logging, so a patch that changes the reference resolution shows up immediately.

### 6.2 UI sound

The game attaches a component literally named **`ButtonSound`** to its buttons — **61** instances in level 0. Its serialised fields (`raw/dump-l0-mb/ButtonSound *.txt`): **[V]**

```
bool  _playSoundOnClickStart = False
PPtr<AudioClip> _hoverClip     -> pathID 4434     _hoverVolume = 0.4  (or 1.0)
PPtr<AudioClip> _clickClip     -> pathID 3787     _clickVolume = 0.3  (or 0.7 / 1.0)
```

Aggregated across all 61: `_hoverClip` is pathID **4434** on 61/61. `_clickClip` is pathID **3787** on 55 and **3927** on 6. So there is exactly **one canonical hover clip and one canonical click clip**. Their asset *names* were not extracted — but you do not need them, because you can read the clips straight off a live `ButtonSound`: **[V]** on the data, **[I]** on the approach.

```csharp
using Il2CppInterop.Runtime;
using MelonLoader;
using UnityEngine;

public static class S1Audio
{
    static AudioClip  _hover, _click;
    static float      _hoverVol = 0.4f, _clickVol = 0.3f;
    static AudioSource _src;

    /// Reads the game's own ButtonSound component via reflection over the interop
    /// type, so it keeps working if the namespace moves between patches.
    public static void Harvest()
    {
        System.Type bs = null;
        foreach (System.Reflection.Assembly a in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            bs = a.GetType("Il2CppScheduleOne.UI.ButtonSound", false)
              ?? a.GetType("Il2CppScheduleOne.ButtonSound", false);
            if (bs != null) break;
        }
        if (bs == null) { MelonLogger.Warning("[S1Audio] ButtonSound type not found"); return; }

        Il2CppSystem.Type il2 = Il2CppType.From(bs);
        foreach (UnityEngine.Object o in Resources.FindObjectsOfTypeAll(il2))
        {
            _hover = Member(bs, o, "_hoverClip") as AudioClip;
            _click = Member(bs, o, "_clickClip") as AudioClip;
            if (Member(bs, o, "_hoverVolume") is float f1) _hoverVol = f1;
            if (Member(bs, o, "_clickVolume") is float f2) _clickVol = f2;
            if (_hover != null && _click != null) break;
        }
        MelonLogger.Msg($"[S1Audio] hover='{_hover?.name}' click='{_click?.name}'");

        GameObject go = new GameObject("S1Mod_Audio");
        Object.DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        _src = go.AddComponent<AudioSource>();
        _src.playOnAwake = false;
        _src.spatialBlend = 0f;                 // 2D
    }

    /// Il2CppInterop may surface an IL2CPP instance field as either a C# property
    /// or a C# field depending on the generator version - try both.
    static object Member(System.Type t, object inst, string name)
    {
        const System.Reflection.BindingFlags F =
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public   |
            System.Reflection.BindingFlags.NonPublic;

        System.Reflection.PropertyInfo pi = t.GetProperty(name, F);
        if (pi != null && pi.CanRead) return pi.GetValue(inst);

        System.Reflection.FieldInfo fi = t.GetField(name, F);
        return fi?.GetValue(inst);
    }

    public static void PlayHover() { if (_src != null && _hover != null) _src.PlayOneShot(_hover, _hoverVol); }
    public static void PlayClick() { if (_src != null && _click != null) _src.PlayOneShot(_click, _clickVol); }
}
```

Volumes: **hover 0.4, click 0.3** — the values on the buttons that also set `_playSoundOnClickStart = False`, i.e. the standard menu buttons. **[V]**

Related audio infrastructure present in level 0, if you need to route through the game's mixer instead of your own `AudioSource`: `AudioManager` (1), `SFXManager` (1), `MusicManager` (1), `SFXConfiguration` (1), `DefaultSFXSettings` (1), `AudioSourceController` (730), `RandomizedAudioSourceController` (228). A representative `AudioSourceController` uses `_audioType = 4`, `_defaultBaseVolume = 0.15`, `_randomizePitch = False`. **[V]**

### 6.3 Transition timing

- `fadeDuration = **0.1** s` — 406 of 473 game buttons. Use this. **[V]**
- `0.05 s` is the secondary value (63 buttons), used on tighter controls like keybind rows. **[V]**
- Unity's `ColorTint` fade is **linear** (`CanvasRenderer.SetColor` over `fadeDuration`); the game does not override it with animation curves — `m_Transition = 1` (ColorTint) on all 473, **zero** buttons use `m_Transition = 3` (Animation). Do not add easing the game does not have. **[V]**
- For panel open/close the game gives no measurable reference here. **[I]** suggestion: 0.12 s linear alpha on a `CanvasGroup`, which is consistent with the 0.1 s button fade.

### 6.4 Navigation and gamepad

The game uses a custom `UISelectable` component (**348** instances) that registers with a `UIScreen`/`uiPanel` to drive keyboard and gamepad navigation. If you inject cards into an existing game menu, you must add them to that panel's selectable list or navigation will skip them — this is what S1API does when adding phone app icons (`raw/a1-melonloader-s1api.md:1155`). For a standalone mod canvas, set `Navigation.Mode.None` (as in §5.4) and handle input yourself. **[V]**

### 6.5 Blocking input to the game underneath

A `GraphicRaycaster` plus a full-screen `Image` with `raycastTarget = true` blocks uGUI clicks, but **does not** block the game's own raw `Input.GetMouseButtonDown` polling. The standard mitigation is to zero the input axes while the pointer is over modded UI (`raw/c2-ugui-runtime.md:703`). **[V]**

---

## 7. Sources

### 7.1 Every claim, traced

| Claim | Source | Status |
|---|---|---|
| Unity 2022.3.62f2 | `raw/assetstudio-info-globalgamemanagers.assets.txt:235` | V |
| Card fill / green / border / backdrop hex | `raw/refscreenshot-measurements.txt:64-68`, re-verified by GetPixel | V |
| Card geometry (310×71, 88, gap 10, radius 8, border 2 px) | `raw/refscreenshot-measurements.txt:32-57` | V |
| Reference is native 1:1 (no downscale) | 2 px border is exact `#FFFFFF` with no blended edge pixel | V |
| Text ink bands, baselines, trophy bbox | GetPixel row-profile of the reference PNG | V |
| Title = OpenSans-SemiBold @ 18 | ink-width fit vs `raw/extract-fonts/*.ttf`, err 2 px over 3 strings | V |
| Description = OpenSans-SemiBoldItalic @ 12 | ink-width fit, err 4 px over 7 strings | V |
| Description colour is white | 45 px ≥ `#FAFAFA` scattered across both lines; top-5 % luma 248 vs 255 for the title | V |
| TMP_FontAsset runtime names (15) | `raw/dump-tmpfonts/*.txt` (`m_Name` field) | V |
| Font-asset usage counts + pathID→name map | 1037 `TextMeshProUGUI` dumps in `raw/dump-l0-mb/`, resolved against `OpenSans-SemiBold SDF @79218.txt` etc. | V |
| Game font sizes 14/20/24/18/12 | same aggregation | V |
| No synthetic bold/italic (`m_fontStyle = 0` × 1034) | same aggregation | V |
| Open Sans lineSpacing/em = 1.3618 | `FontFamily.GetLineSpacing / GetEmHeight` on `raw/extract-fonts/OpenSans-*.ttf` | V |
| 9-slice borders and PPU | `raw/sprite-9slice.tsv`, `raw/sprite-inventory.tsv` | V |
| Sprite shapes (ring thicknesses, square vs rounded) | alpha probing of `raw/extract-uisprites/*.png`, `raw/extract-customui/*.png` | V |
| `Rectangle_RoundedEdges` = 477 `Image` refs; ppuMult histogram | 3745 `Image` dumps in `raw/dump-l0-mb/` | V |
| Trophy = the game's `trophy` sprite tinted | 19 px shape comparison vs `raw/extract-uisprites/trophy.png`; gold `#FFC04B` from `raw/ref-trophy-zoom.png` | V |
| Button ColorBlocks, fadeDuration 0.1, ColorTint | 545 `Button` dumps in `raw/dump-l0-mb/` | V |
| CanvasScaler 1920×1080, match 0, refPPU 100 | 21 `CanvasScaler` dumps in `raw/dump-l0-mb/` | V |
| `ButtonSound` hover/click clips and volumes | 61 `ButtonSound` dumps in `raw/dump-l0-mb/` | V |
| `UISelectable` × 348, audio manager inventory | filename census of `raw/dump-l0-mb/` | V |
| IL2CPP interop patterns, canvas recipe, `Sprite.Create` overload | `raw/c2-ugui-runtime.md` §1.1–1.5, §2.1–2.2, line 1191 | V |
| S1API preserves navigation via `UISelectable` | `raw/a1-melonloader-s1api.md:1155` | V |
| Extraction methodology | `ASSETS-TOOLCHAIN.md` | V |
| Open Sans is the UI typeface | `ASSETS-TOOLCHAIN.md:332`, `raw/extract-fonts/` | V |

External references: [Unity 2022.3 `Sprite.Create`](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Sprite.Create.html) · [`Resources.FindObjectsOfTypeAll`](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Resources.FindObjectsOfTypeAll.html) · [uGUI `Image` (incl. `pixelsPerUnitMultiplier`)](https://docs.unity3d.com/Packages/com.unity.ugui@1.0/api/UnityEngine.UI.Image.html) · [9-slicing](https://docs.unity3d.com/2022.3/Documentation/Manual/9SliceSprites.html) · [Canvas Scaler](https://docs.unity3d.com/2022.3/Documentation/Manual/script-CanvasScaler.html) · [MelonLoader #912 (TMP namespace prefix)](https://github.com/LavaGang/MelonLoader/issues/912) · [UniverseLib `UIFactory.cs`](https://github.com/sinai-dev/UniverseLib/blob/main/src/UI/UIFactory.cs)

### 7.2 Things in the reference I could NOT match to a real game asset

Handle these deliberately.

1. **The backdrop's criss-cross line pattern.** The reference backdrop is `#010101`/`#020202` overlaid with a faint diagonal X-lattice peaking at `#0A0B0A`–`#0F0F0F`. I checked every sprite in `raw/extract-uisprites/`, `raw/extract-customui/` and `raw/extract-palette/` and none of them is this texture. It is not in `sprite-9slice.tsv` either. **Options:** (a) omit it and use flat `#010101`; (b) it may simply be the blurred game world behind a dark scrim, in which case a full-screen `Image` at `#000000` α ≈ 0.94 over the live scene reproduces it; (c) `Vertical gradient` / `Horizontal gradient` (both real, pathIDs 4774 / 4860) are the nearest shipped backdrop art but are gradients, not lattices. **[I]**

2. **A rounded 2 px outline as a single sprite.** No shipped sprite gives 2 px stroke *and* 8.5 px radius simultaneously — the two ring sprites lock thickness to radius. `Outline_RoundedEdges 1` at `ppuMult = 20` gives exactly 2.0 px but a 12.75 px radius; `Outline_RoundedEdges` at `ppuMult = 50` gives 2.0 px at a 5.1 px radius; `WhiteFrame_4px` at `ppuMult = 2` gives exactly 2 px but square corners. The two-layer approach in §5.4 is exact and is what the spec above uses. **[D]**

3. **Scrollbar art.** The game has 76 `Scrollbar` components but I did not trace their target graphics to specific sprites. §4.4's scrollbar rows are **[I]**. If scroll fidelity matters, clone a real game `Scrollbar` and sanitise it (`raw/c2-ugui-runtime.md` §2.4) rather than building one.

4. **`ButtonSound` clip asset names.** Verified as pathIDs 4434 (hover) and 3787 (click) with unanimous agreement across 61 components, but the `AudioClip` names were never extracted. §6.2 sidesteps this by reading the clips off a live component instead of looking them up by name. **[V]** on the pathIDs, **[I]** on the namespace string in the reflection lookup.

5. **Screen heading / section-label / button-label type scale** (§2.5, rows 3–6). The reference screenshot contains only cards. Those rows are extrapolated from the game's size histogram, not measured. **[I]**

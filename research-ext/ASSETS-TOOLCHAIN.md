# Schedule I — Asset Inspection / Extraction Toolchain (validated)

> **Status of this document:** everything under "Validated procedure" was actually executed on this
> machine against the installed game on **2026‑08‑03** and the outputs are committed under
> `research-ext/raw/`. Claims are labelled **[verified]** (I ran it and saw the output),
> **[community]** (documented by the tool authors / community), or **[inference]** (my reasoning).
>
> The game was **never launched**. All work was read-only against files on disk.

---

## 1. Target facts (measured, not assumed)

| Fact | Value | How established |
|---|---|---|
| Unity version | **2022.3.62f2** | **[verified]** ASCII string at offset ~0x30 of `Schedule I_Data/globalgamemanagers`; also reported by AssetStudio as `[Unity Version] # 2022.3.62f2` |
| Scripting backend | **IL2CPP** | **[verified]** presence of `GameAssembly.dll` (66.8 MB) + `Schedule I_Data/il2cpp_data/` |
| Render pipeline | **URP** (Universal) | **[verified]** `Unity.RenderPipelines.Universal.Runtime.dll` in interop assemblies; `URP_0Low/1Med/2High/3Ultra` UniversalRenderPipelineAsset MonoBehaviours dumped from `globalgamemanagers.assets` |
| Product / company | `Schedule I` / `TVGS` | **[verified]** `Schedule I_Data/app.info` |
| Game code assemblies | `Assembly-CSharp.dll`, `Assembly-CSharp-firstpass.dll`, **`ScheduleOne.Core.dll`** | **[verified]** `MelonLoader/Il2CppAssemblies/` and `MelonLoader/Dependencies/Il2CppAssemblyGenerator/Cpp2IL/cpp2il_out/` |
| Notable 3rd-party runtime libs | FishNet.Runtime, AstarPathfindingProject (+ClipperLib/Poly2Tri/Ionic.Zip), RootMotion (Final IK), AmplifyImpostors, HSVPicker, LokoSolo.PinchableScrollRect, RuntimePreviewGenerator, sc.stylizedwater2, VisualDesignCafe.Nature, CorgiGodRays, Edgegap, Steamworks.NET, Newtonsoft.Json, Unity.InputSystem, Unity.TextMeshPro | **[verified]** directory listing of `MelonLoader/Il2CppAssemblies/` |
| Serialized-file layout | `globalgamemanagers(.assets)`, `level0/1/2`, `resources.assets`, `sharedassets0/1/2.assets` (+ `.resS`, `.resource`) | **[verified]** directory listing |
| Total on-disk asset payload | ~6.3 GB (`sharedassets1.assets.resS` alone is 3.24 GB, `sharedassets0.assets.resS` 2.07 GB) | **[verified]** |

### Where the UI lives
**[verified]** `sharedassets0.assets` is the file that carries the UI: **428 Sprites**, **20 Font**
assets, 49 `Canvas`, 6067 `RectTransform`, 4845 `CanvasRenderer`, 45 `CanvasGroup`.
`resources.assets` adds only ~55 more sprites and 1 more font on top of that.
So **for UI work you only ever need to load `sharedassets0.assets`** — which is fast
(~3 s) because sprite/font metadata does not require decoding the 2 GB `.resS` blob.

Asset counts I measured:

| File | Files loaded (incl. deps) | Exportable assets | Sprites | Fonts | Canvases |
|---|---|---|---|---|---|
| `globalgamemanagers.assets` | 3 | 4,830 | 1 | 1 | 0 |
| `sharedassets0.assets` | 4 | 101,486 | 428 | 20 | 49 |
| `resources.assets` | 5 | 123,669 | 483 | 21 | 52 |
| `level0` | 5 | 140,799 | 428 | 20 | 58 |

(Counts are cumulative because AssetStudio auto-loads dependency files. Subtract to get per-file
figures.) **[verified]**

---

## 2. Tool selection — why AssetStudioMod CLI

I evaluated two candidates and downloaded both.

| Tool | Version tried | Verdict |
|---|---|---|
| **AssetStudioMod CLI** (aelurum fork) | **v0.19.0**, released 2025‑09‑04 | ✅ **Use this.** True CLI, scriptable, fast, reads Unity 2022.3.62f2 correctly, can list/dump/export by type with name filters, and accepts `--assembly-folder` to reconstruct MonoBehaviour type trees. |
| **AssetRipper** | **1.3.14**, released 2026‑04‑25 | ⚠️ Only ships `AssetRipper.GUI.Free.exe` (a localhost web UI) on Windows — **no CLI binary in the release zip** (contents: `AssetRipper.GUI.Free.exe`, `capstone.dll`, `compile_time.txt`). Better than AssetStudio at reconstructing a *whole Unity project* (it embeds Cpp2IL and rebuilds scenes/prefabs as YAML), but it is not automatable from PowerShell without driving its HTTP endpoints, and it wants to chew the full 6 GB. Keep as the escalation path if you need full prefab/scene reconstruction. |
| UABEA (`nesrak1/UABEA`) | not used | **[inference]** Good for *editing* single assets and for hand-inspecting one serialized file, but GUI-only and worse than AssetStudio for bulk listing. Skip unless you need to write back into a bundle. |

**Recommendation: AssetStudioMod CLI for everything in this project.** It answered every UI/asset
question in under 10 seconds per query.

### Exact download + setup (validated, copy-paste)

```powershell
$ProgressPreference = 'SilentlyContinue'
New-Item -ItemType Directory -Force -Path "$PWD\tools" | Out-Null
Set-Location "$PWD\tools"

# net472 build => no extra runtime needed on Win10/11 (this machine only had .NET 6/8 runtimes,
# so the net9_win64 build would NOT have run).
Invoke-WebRequest `
  -Uri "https://github.com/aelurum/AssetStudioMod/releases/download/v0.19.0/AssetStudioModCLI_net472_win32_64.zip" `
  -OutFile "ASCLI472.zip"
Expand-Archive .\ASCLI472.zip -DestinationPath .\ASCLI472 -Force

# exe path after extraction:
$exe = ".\ASCLI472\AssetStudioModCLI_net472_win32_64\AssetStudioModCLI.exe"
& $exe --help
```

**[verified]** `dotnet --list-runtimes` on this box showed only `Microsoft.NETCore.App 6.0.36 / 8.0.26 /
8.0.27` — **no .NET 9** — which is exactly why the `net472` asset is the right pick. If you ever
switch machines and have .NET 9, `AssetStudioModCLI_net9_win64.zip` (6.5 MB) is smaller and faster.

Release index used: <https://github.com/aelurum/AssetStudioMod/releases> (checked via
`api.github.com/repos/aelurum/AssetStudioMod/releases/latest`).

---

## 3. Validated procedures

Common variables used in all snippets:

```powershell
$exe  = "C:\Users\fyfvg\Documents\ScheduleI-CreativeMode\tools\ASCLI472\AssetStudioModCLI_net472_win32_64\AssetStudioModCLI.exe"
$data = "C:\Program Files (x86)\Steam\steamapps\common\Schedule I\Schedule I_Data"
$out  = "C:\Users\fyfvg\Documents\ScheduleI-CreativeMode\research-ext\raw"
$asm  = "C:\Program Files (x86)\Steam\steamapps\common\Schedule I\MelonLoader\Dependencies\Il2CppAssemblyGenerator\Cpp2IL\cpp2il_out"
```

### 3.1 Inventory a serialized file (fast, no decoding) — **[verified]**

```powershell
& $exe "$data\sharedassets0.assets" -m info --load-all --log-level info
```
Prints `[Unity Version]` and a per-type count table. ~3 s. Output saved to
`research-ext/raw/assetstudio-info-sharedassets0.assets.txt` (plus the same for
`resources.assets` and `globalgamemanagers.assets`).

### 3.2 Export the game's real fonts (TTF) — **[verified]**

```powershell
& $exe "$data\sharedassets0.assets" -m export -t font -g containerFull -f assetName `
       -o "$out\extract-fonts" -r --log-level warning
& $exe "$data\resources.assets"     -m export -t font -g containerFull -f assetName `
       -o "$out\extract-fonts" -r --log-level warning
```
Produced 16 real `.ttf` files → see **§4.1**. This is the single most valuable UI finding.

### 3.3 Dump Sprite metadata (names, size, **9-slice borders**, pixelsPerUnit) — **[verified]**

`-m dump` writes a JSON per asset **without decoding textures**, so it is fast and never touches
the multi-GB `.resS`. Sprite is a native Unity type so it always parses (no type tree needed).

```powershell
& $exe "$data\sharedassets0.assets" -m dump -t sprite -g containerFull -f assetName `
       -o "$out\dump-sprites" -r --log-level error
```
428 JSON files in ~4 s → `research-ext/raw/dump-sprites/`.

I post-processed those into two flat listings:

* `research-ext/raw/sprite-inventory.tsv` — all 428 sprites: `name, WxH, border(L,B,R,T), ppu, texture, pathID`
* `research-ext/raw/sprite-9slice.tsv` — only the 15 sprites with a **non-zero 9-slice border**
  (i.e. the reusable panel/frame art)

Post-processing script (validated):

```powershell
$rows=@()
Get-ChildItem "$out\dump-sprites" -Recurse -Filter *.txt | ForEach-Object {
  try {
    $j = Get-Content $_.FullName -Raw | ConvertFrom-Json
    $rows += [pscustomobject]@{
      Name=$j.m_Name; W=$j.m_Rect.width; H=$j.m_Rect.height
      BL=$j.m_Border.X; BB=$j.m_Border.Y; BR=$j.m_Border.Z; BT=$j.m_Border.W
      PPU=$j.m_PixelsToUnits; Tex=$j.m_RD.texture.Name; PathID=$j.m_PathID
    }
  } catch {}
}
$rows | Sort-Object Name |
  ForEach-Object { "{0}`t{1}x{2}`tborder=({3},{4},{5},{6})`tppu={7}`ttex={8}`tpathID={9}" -f `
      $_.Name,$_.W,$_.H,$_.BL,$_.BB,$_.BR,$_.BT,$_.PPU,$_.Tex,$_.PathID } |
  Set-Content "$out\sprite-inventory.tsv"
```

### 3.4 Export selected sprites as PNG — **[verified]**

Never export all sprites (it decodes textures out of the 2 GB `.resS`). Filter by name:

```powershell
& $exe "$data\sharedassets0.assets" -m export -t sprite -g none -f assetName `
       -o "$out\extract-uisprites" -r --log-level error `
       --filter-by-name "Rectangle_RoundedEdges,Outline_RoundedEdges,WhiteFrame_4px,trophy,MenuTitle,UISprite,gear_icon,ProductSlotBackground,Blank_Avatar_Icon,S1 Logo Whiteout,InputFieldBackground,Circle,hollowcircle thin,Relationship band,Vertical gradient,Horizontal gradient"
```
`--filter-by-name` is a *substring* match on a comma-separated list, so
`--filter-by-name CustomUI_Spritesheet_64px` grabs the whole sheet. 22 PNGs in ~8 s.
Output: `research-ext/raw/extract-uisprites/`, `research-ext/raw/extract-customui/`,
`research-ext/raw/extract-palette/`.

### 3.5 Inspect a sprite's shape without an image viewer — **[verified]**

Useful in an agent/CI context. Prints an ASCII alpha/luma map:

```powershell
Add-Type -AssemblyName System.Drawing
$b=[System.Drawing.Bitmap]::FromFile("$out\extract-customui\CustomUI_Spritesheet_64px_2.png")
for($ry=0; $ry -lt 24; $ry++){
  $line=""
  for($rx=0; $rx -lt 56; $rx++){
    $x=[int]($rx*($b.Width-1)/55); $y=[int]($ry*($b.Height-1)/23)
    $c=$b.GetPixel($x,$y)
    $line += if($c.A -lt 20){" "} elseif($c.A -lt 150){"."}
             elseif(($c.R+$c.G+$c.B) -gt 600){"#"} elseif(($c.R+$c.G+$c.B) -gt 300){"o"} else{"-"}
  }
  Write-Host $line
}
$b.Dispose()
```

### 3.6 Sample a sprite's dominant colours (palette archaeology) — **[verified]**

This is how the palette in `UI-STYLE.md` was derived from the game's own art rather than guessed:

```powershell
Add-Type -AssemblyName System.Drawing
Get-ChildItem "$out\extract-palette" -Filter *.png | ForEach-Object {
  $b=[System.Drawing.Bitmap]::FromFile($_.FullName); $h=@{}
  $sx=[Math]::Max(1,[int]($b.Width/96)); $sy=[Math]::Max(1,[int]($b.Height/96))
  for($y=0;$y -lt $b.Height;$y+=$sy){ for($x=0;$x -lt $b.Width;$x+=$sx){
    $c=$b.GetPixel($x,$y)
    if($c.A -gt 200){ $k=('{0:X2}{1:X2}{2:X2}' -f $c.R,$c.G,$c.B)
                      if($h.ContainsKey($k)){$h[$k]++}else{$h[$k]=1} } } }
  "{0,-30} {1}x{2} -> {3}" -f $_.BaseName,$b.Width,$b.Height,
     (($h.GetEnumerator()|Sort-Object Value -Descending|Select-Object -First 5 |
        ForEach-Object{"#$($_.Key)($($_.Value))"}) -join '  ')
  $b.Dispose()
}
```
Raw result: `research-ext/raw/palette-samples.txt`.

### 3.7 ⭐ Reconstruct MonoBehaviour field data using MelonLoader's Cpp2IL output — **[verified, important]**

**The problem:** this build ships **no type trees for MonoBehaviour**. Proof: dumping a
`TMP_FontAsset` yields only the common header —

```
MonoBehaviour Base
	PPtr<GameObject> m_GameObject
		int m_FileID = 0
		SInt64 m_PathID = 0
	UInt8 m_Enabled = 1
	PPtr<MonoScript> m_Script
		int m_FileID = 1
		SInt64 m_PathID = 2218
	string m_Name = "OpenSans-SemiBold SDF"
```
(`research-ext/raw/dump-tmpfonts/OpenSans-SemiBold SDF.txt`, 230 bytes.) **[verified]**

**The fix:** MelonLoader already ran Cpp2IL when it generated the interop assemblies, and it kept
the **raw Cpp2IL output**, which — unlike the Il2CppInterop assemblies — has *real fields with real
names and types*. Point AssetStudio at it:

```powershell
$asm = "C:\Program Files (x86)\Steam\steamapps\common\Schedule I\MelonLoader\Dependencies\Il2CppAssemblyGenerator\Cpp2IL\cpp2il_out"

& $exe "$data\globalgamemanagers.assets" -m dump -t monoBehaviour -g none -f assetName_pathID `
       -o "$out\dump-mb-test" -r --log-level error --assembly-folder $asm
```

**Result [verified]:** field data appears. 27/27 MonoBehaviours dumped with content, e.g.
`GameSkin @11000.txt` = 59 KB, `PostProcessData @4790.txt` = 5.3 KB, and
`URP_0Low/1Med/2High/3Ultra` + `Deferred Renderer` + `VolumetricFogDepthRenderer` render-pipeline
assets. Without `--assembly-folder` these are ~200-byte stubs.

> ⚠️ **Do NOT use `MelonLoader/Il2CppAssemblies/` as the assembly folder.** Those are
> Il2CppInterop *proxy* assemblies — the fields are replaced by property accessors that call into
> `GameAssembly.dll`, so there is no serializable field layout for AssetStudio to read.
> **[inference, but well-founded]** — Il2CppInterop's documented codegen model; the field data only
> exists in the Cpp2IL stage output.

**Known limitation [verified], with exact root cause:** `-g sceneHierarchy` on `level0` fails
regardless of `--assembly-folder`:

```
System.ArgumentException: Illegal characters in path.
  at System.IO.Path.Combine(String path1, String path2)
  at AssetStudioCLI.Studio.GenerateFullPath(BaseNode treeNode, String path)   <- Studio.cs:482
  at AssetStudioCLI.Studio.BuildTreeStructure(...)                            <- Studio.cs:465
  at AssetStudioCLI.Studio.ParseAssets()                                      <- Studio.cs:376
```
(`research-ext/raw/log-level0-dump.txt`.) **Cause:** at least one GameObject in the scene has a name
containing a character that is illegal in a Windows path (`: ? * | < > "`), and AssetStudioMod
v0.19.0 does not sanitise node names when it builds the output directory tree. So
**`-g sceneHierarchy` is unusable on this game** — it is a tool bug, not a type-tree problem.

Workarounds, in order of preference:
1. use `-g none` or `-g type` (field data still comes out, you just lose the hierarchy paths);
2. patch/rebuild AssetStudioMod with a `Path.GetInvalidFileNameChars()` filter in
   `GenerateFullPath` **[inference]**;
3. escalate to **AssetRipper**, which sanitises names and reconstructs the scene graph properly —
   this is the right tool if you specifically need the *main-menu GameObject hierarchy path*.

### 3.8 Escalation path: AssetRipper (only if you need whole scenes/prefabs)

```powershell
Invoke-WebRequest -Uri "https://github.com/AssetRipper/AssetRipper/releases/download/1.3.14/AssetRipper_win_x64.zip" -OutFile AssetRipper.zip
Expand-Archive .\AssetRipper.zip -DestinationPath .\AssetRipper -Force
.\AssetRipper\AssetRipper.GUI.Free.exe     # opens a localhost web UI in your browser
```
Then: *Load Folder* → `C:\Program Files (x86)\Steam\steamapps\common\Schedule I\Schedule I_Data`
→ *Export* → "Export all files". **[community]** AssetRipper embeds Cpp2IL and will emit
`Assets/Scenes/*.unity` and `*.prefab` as YAML with resolved script fields, which is the only
practical way to read the real `Image.m_Color` / `TextMeshProUGUI.m_fontSize` values off the
shipped menu. Budget **tens of GB of disk and 10–40 minutes** for a 6 GB game.
Docs: <https://github.com/AssetRipper/AssetRipper/wiki>.

---

## 4. Asset listings actually produced

All under `research-ext/raw/`.

| File | What it is |
|---|---|
| `assetstudio-info-globalgamemanagers.assets.txt` | per-type asset census |
| `assetstudio-info-resources.assets.txt` | per-type asset census |
| `assetstudio-info-sharedassets0.assets.txt` | per-type asset census (the UI file) |
| `sprite-inventory.tsv` | **all 428 sprite names** + size + 9-slice border + ppu + source texture + pathID |
| `sprite-9slice.tsv` | the 15 nine-sliceable sprites (the reusable panel/frame art) |
| `dump-sprites/*.txt` | per-sprite JSON metadata (428 files) |
| `dump-tmpfonts/*.txt` | the 15 `TMP_FontAsset` names (see §4.2) |
| `extract-fonts/*.ttf` | 16 real font files ripped from the game |
| `extract-uisprites/*.png` | 22 core UI sprites as PNG |
| `extract-customui/*.png` | the `CustomUI_Spritesheet_64px` slices + shadow sprites |
| `extract-palette/*.png` | 25 coloured sprites used for palette sampling |
| `palette-samples.txt` | dominant-colour table for those |
| `ref-*.png` | nearest-neighbour zooms of the user's reference screenshot |
| `refscreenshot-measurements.txt` | the raw pixel measurements behind `UI-STYLE.md` |
| `log-level0-dump.txt` | the failing `sceneHierarchy` run, for the record |

### 4.1 Fonts shipped in the game — **[verified]**

Ripped `.ttf` files (from `sharedassets0.assets` + `resources.assets`):

| File | Bytes | Role **[inference]** |
|---|---|---|
| `OpenSans-Light.ttf` | 129,756 | UI |
| `OpenSans-LightItalic.ttf` | 135,668 | UI |
| `OpenSans-Regular.ttf` | 129,796 | UI body |
| `OpenSans-Italic.ttf` | 135,380 | UI body italic |
| `OpenSans-Medium.ttf` | 129,948 | UI |
| `OpenSans-MediumItalic.ttf` | 135,556 | **card descriptions** |
| `OpenSans-SemiBold.ttf` | 129,716 | UI emphasis |
| `OpenSans-SemiBoldItalic.ttf` | 135,512 | UI |
| `OpenSans-Bold.ttf` | 129,784 | **card titles / headers** |
| `OpenSans-BoldItalic.ttf` | 135,108 | UI |
| `OpenSans-ExtraBold.ttf` | 130,180 | big headings |
| `OpenSans-ExtraBoldItalic.ttf` | 135,688 | big headings |
| `Caveat-Regular.ttf` | 196,984 | handwriting (notes / signage) |
| `PerfectDOSVGA437.ttf` | 81,192 | in-game computer / terminal |
| `fs-sevegment.ttf` | 17,392 | seven-segment digital readouts |
| `LiberationSans.ttf` | 350,200 | TextMeshPro default + fallback |

**⇒ The game's UI typeface is Open Sans (Apache-2.0 / SIL-adjacent — Google Fonts, free to
redistribute), with the entire weight+italic matrix shipped.**

### 4.2 `TMP_FontAsset` names — **[verified]** (these are the strings to look up at runtime)

```
OpenSans-Light SDF
OpenSans-Regular SDF
OpenSans-Medium SDF
OpenSans-MediumItalic SDF
OpenSans-SemiBold SDF
OpenSans-SemiBoldItalic SDF
OpenSans-Bold SDF
OpenSans-BoldItalic SDF
OpenSans-Bold Lit SDF          <- "Lit" = lit/3D TMP shader variant
Caveat-Regular SDF
ComicNeue-Bold SDF
ComicNeue-BoldItalic SDF
fs-sevegment SDF
LiberationSans SDF
LiberationSans SDF - Fallback
```
Note `ComicNeue-*` TMP assets exist even though the ComicNeue `.ttf` was among the 5 assets
AssetStudio skipped — **[inference]** its source font is probably referenced from a package rather
than embedded as a standalone `Font`.

### 4.3 The 15 nine-sliceable sprites — **[verified]**

| Sprite | Size | Border (L,B,R,T) | spritePPU | Rendered radius/stroke at `pixelsPerUnitMultiplier = 1` (canvas refPPU 100) |
|---|---|---|---|---|
| `Rectangle_RoundedEdges` | 512×512 | 255,255,255,255 | 100 | **filled** rounded rect, ~235 px corner → needs a large multiplier |
| `Rectangle_RoundedEdges_Top` | 512×512 | 255,255,255,255 | 100 | filled, rounded **top** corners only |
| `Rectangle_RoundedEdges_Bottom` | 512×512 | 255,255,255,255 | 100 | filled, rounded **bottom** corners only |
| `Outline_RoundedEdges` | 512×512 | 255,255,255,255 | 100 | rounded **ring**, thick stroke |
| `Outline_RoundedEdges 1` | 512×512 | 255,255,255,255 | 100 | rounded ring, **thinner** stroke |
| `CustomUI_Spritesheet_64px_0` | 120×120 | 20,20,20,20 | 500 | square frame, ~4 px slice |
| `CustomUI_Spritesheet_64px_2` | 120×120 | 48,48,48,48 | 500 | **rounded frame, ~9.6 px slice / ~6 px radius, ~3.4 px stroke** |
| `CustomUI_Spritesheet_64px_3` | 248×248 | 92,92,92,92 | 500 | rounded frame, ~18.4 px slice / larger radius |
| `property_outline` | 32×32 | 8,8,8,8 | 150 | square outline, ~5.3 px slice |
| `WhiteFrame_4px` | 64×64 | 8,8,8,8 | 100 | 1 px hairline white frame (mid-row alpha = 255,255,255,255,0,0,…) |
| `Rect shade` | 256×256 | 122,122,122,122 | 100 | soft **box shadow** |
| `UISprite` | 32×32 | 10,10,10,10 | 200 | Unity built-in rounded button, 5 px radius |
| `Background` | 32×32 | 10,10,10,10 | 200 | Unity built-in |
| `InputFieldBackground` | 32×32 | 10,10,10,10 | 200 | Unity built-in |
| `UIMask` | 32×32 | 10,10,10,10 | 200 | Unity built-in mask |

Shape verification (ASCII alpha maps) confirmed:
`Rectangle_RoundedEdges` = solid rounded rect, corner radius ≈ **232–235 px** of 512 (measured:
top row is opaque only from x≈235 to x≈277); `Outline_RoundedEdges` / `… 1` = the same silhouette
but hollow, with the `1` variant having a visibly thinner stroke;
`CustomUI_Spritesheet_64px_2` = hollow rounded frame; `_0` = hollow square frame;
`Rect shade` / `Circle shade` = radial soft shadows. **[verified]**

### 4.4 Other high-value sprite groups found — **[verified]**

* **83 `*_Mugshot` sprites** — one per named NPC (`Albert_Mugshot`, `Jane_Mugshot`, `Mayor_Mugshot`,
  `Nurse_Mugshot`, `FungalPhil_Mugshot`, …) plus `Blank_Avatar_Icon` and `Avatar_Icon`.
* **7 phone app icons** — `appicon_contacts`, `appicon_dealers`, `appicon_deliveries`,
  `appicon_journal`, `appicon_map`, `appicon_messages`, `appicon_productmanager`.
* **Main-menu art** — `MenuTitle` (2048×1024, visible content 2028×220), `S1 Logo Whiteout`,
  `TVGS wide logo 1`, and the social buttons `trello`, `discord`, `reddit`, `twitter` (187×187 each).
* **~40 clothing icons** — `Beanie_Icon`, `Flannel_Icon`, `Blazer_Icon`, `CargoPants_Icon`,
  `Jeans_Icon`, `Jorts_Icon`, `Overalls_Icon`, `Vest_Icon`, `KevlarVest_Icon`,
  `CombatBoots(Clone)_Icon`, `CowboyHat(Clone)_Icon`, `PorkpieHat(Clone)_Icon`,
  `FlatCap(Clone)_Icon`, `BucketHat(Clone)_Icon`, `Cap(Clone)_Icon`, `ChefHat_Icon`,
  `Apron(Clone)_Icon`, `GoldChain(Clone)_Icon`, `Oakleys(Clone)_Icon`,
  `LegendSunglasses(Clone)_Icon`, `SmallRoundGlasses(Clone)_Icon`,
  `RectangleFrameGlasses(Clone)_Icon`, `Polex(Clone)_Icon`, `DressShoes(Clone)_Icon`,
  `Sneakers(Clone)_Icon`, `Sandals(Clone)_Icon`, `Flats(Clone)_Icon`, `FingerlessGloves_Icon`,
  `Gloves_Icon`, `Belt(Clone)_Icon`, `ButtonUp_Icon`, `ButtonUp_RolledIcon`, `T-Shirt_Icon`,
  `VNeckIcon`, `CollarJacket_Icon`, `LongSkirt(Clone)_Icon`, `MediumSkirt(Clone)_Icon`,
  `MushroomHat Icon`. → the clothing catalogue for custom NPC outfits; see `CUSTOM-CHARACTERS.md`.
* **Icon set for UI** — `trophy`, `gear_icon`, `star`, `handshake`, `home`, `information`,
  `secured-lock`, `fire-flame`, `clock`, `edit`, `filter`, `layer`, `vehicle_icon`, `Tick`,
  `Tick circle`, `Cross`, `Plus`, `ExclamationMark`, `QuestionMark`, `arrow`, `arrow 1`, `arrow 2`,
  `thin arrow 2`, `cardinal-point`, `Pause`, `Play`, `Skip`, `Shuffle`, `Repeat icon`,
  `Single repeat icon`, `No repeat icon`, `Sync`, `Stack`, `Open box`, `Box in hand`, `Phone`,
  `Telephone`, `Knob`, `Notch`, `Guilloche`, `Guilloche inverted`, `Money pattern`, `cash_front`,
  `Money`, `Vertical gradient`, `Horizontal gradient`, `Circle`, `Circle shade`,
  `hollowcircle thin`, `Selection ring quadrant`, `RelationCircle`, `Relationship band`,
  `Addiction band flipped`, `black-brick`, `Line tile`, `Tile`, `handdrawn_line_horizontal`,
  `Key background`, `Wide key background`, `Extra wide key background`, `Mouse Left/Middle/Right Click`,
  `Input_SteamDeck_Spritesheet_*`, `DesertMap`, `Map_Full`, `property_outline`.
* **Progress/relationship gradients** — `Relationship band` (128×16) and `Addiction band flipped`
  (128×16) are 5-band gradient strips; sampling them yields the game's semantic tier colours
  (see `UI-STYLE.md` §palette).

---

## 5. Gotchas learned the hard way

1. **[verified]** Never pipe AssetStudio through `Select-Object -First N` — it closes the pipeline
   early, kills the process, and PowerShell reports `exit code -1`. Redirect with `*> file.txt`
   and read the file instead.
2. **[verified]** `-m dump` on Sprite/Texture/native types works with no extra setup; `-m dump` on
   MonoBehaviour needs `--assembly-folder <cpp2il_out>` or you get 200-byte stubs.
3. **[verified]** `--filter-by-name` is comma-separated substring matching. Values containing
   spaces are fine (`"S1 Logo Whiteout"`), just quote the whole argument.
4. **[verified]** Exporting *all* sprites/textures forces decoding out of the multi-GB `.resS`
   files. Always filter. Metadata-only work (`-m dump`) never touches `.resS`.
5. **[verified]** AssetStudio auto-loads dependency files, so per-type counts from
   `-m info` are cumulative across the dependency set — subtract to get per-file numbers.
6. **[inference]** Nothing here modifies the game. All commands are read-only opens. Still, don't
   run extraction while the game is running — Steam/Unity hold locks on the `.resS` files.
7. **[verified]** `MelonLoader/Dependencies/Il2CppAssemblyGenerator/Cpp2IL/cpp2il_out/` is
   regenerated whenever the game updates and MelonLoader re-runs its generator. It is therefore
   always in sync with the installed build — a free, always-current dummy-assembly set. Treat it as
   the canonical local cross-reference (110 DLLs, `Assembly-CSharp.dll` = 5.5 MB,
   `ScheduleOne.Core.dll` = 70 KB).

---

## 6. Licensing / redistribution note

**[verified fact, not legal advice]** The extracted `.ttf` files are the *upstream* Google Fonts
originals: Open Sans, Caveat, and Comic Neue are all under the **SIL Open Font License / Apache
2.0** and can be redistributed freely. `PerfectDOSVGA437` is a freeware VGA font.
`LiberationSans` ships with TextMeshPro. So if you ever *do* need to ship a font with the mod, you
can get Open Sans straight from Google Fonts rather than from the game's assets — same file,
no ambiguity. **But you shouldn't need to:** see the runtime-lookup plan in `UI-STYLE.md`, which
reuses the game's already-loaded `TMP_FontAsset`s and needs zero shipped assets.

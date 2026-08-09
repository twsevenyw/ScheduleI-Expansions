# Sources — Schedule I Roadmap Expansions research

Every URL consulted across the whole external research effort (tracks A1, A2, B/design-intent, C2/uGUI,
D/characters, assets toolchain), consolidated and grouped by topic. Compiled 2026-08-03 from the source lists in
`DESIGN-INTENT.md`, `CUSTOM-CHARACTERS.md`, `ASSETS-TOOLCHAIN.md`, `raw/a1-melonloader-s1api.md`,
`raw/a2-mods-survey.md` and `raw/c2-ugui-runtime.md`. **362 unique URLs** were extracted; near-duplicates
(alternate paths into the same repo) are folded into one row.

**Trust key**
- 🟢 **Primary** — read directly by a research agent (file, API response, or the canonical publisher).
- 🔵 **Authoritative third party** — official vendor documentation (Unity, Microsoft, Steam, FishNet).
- 🟡 **Community** — mod page, README, wiki, Discord-relayed. Plausible; not proof.
- 🟠 **Stale** — content predates the current game build by enough to be misleading. Verify before use.
- 🔴 **Dead / contradicted** — do not rely on it.

**Recency baseline for "stale":** the game shipped 2025-03-24 and is now **v0.4.6f11 (2026-08-01)**. Anything
written in 2025 predates at least five minor versions (0.4.1 → 0.4.6) and is flagged accordingly, even though it is
not literally "pre-2025".

---

## 1. Official developer / publisher sources

| URL | Note |
|---|---|
| <https://store.steampowered.com/news/app/3164500/view/1839676055889605> | 🟢 **v0.4.6 patch notes, 2026-08-01 — the current version.** Contains the Special Customers status update and the "1-2 officers instead of 2" police change. Highest trust. |
| <https://store.steampowered.com/news/app/3164500/view/684135019607754378> | 🟢 v0.4.6 "Gamepad Support + Optimization". Confirms Special Customers is announced but **unreleased**. Same release, alternate news id. |
| <https://store.steampowered.com/news/app/3164500/view/1830163047259398> | 🟢 **"Community vote #3 is now open!", 2026-04-17 — the verbatim source of all three feature descriptions.** The canonical design brief for our mods. |
| <https://store.steampowered.com/news/app/3164500/view/1830797770239647> | 🟢 "'Special customers' wins community vote #3", 2026-04-26. Vote counts, the five group names, the "slightly lower profit margin" design statement. |
| <https://store.steampowered.com/news/app/3164500/view/1809235871707524> | 🟢 **"Community vote #2 is now open!", 2025-09-01 — the detailed Hireable Drivers spec** ("designate automatic transit routes… specify the required conditions"). 11 months old but the feature is still unshipped, so the wording stands. |
| <https://store.steampowered.com/news/app/3164500/view/1809869180154298> | 🟢 "'Shrooms' wins community vote #2", 2025-09-08. Drivers placed 2nd. |
| <https://store.steampowered.com/news/app/3164500/view/1801617199549230> | 🟢 "Community vote #1 is now open!", 2025-06-10. Lists "Police Expansion Update", no description. |
| <https://store.steampowered.com/news/app/3164500/view/1802354289676065> | 🟢 "'Rival Cartel' wins community vote #1", 2025-06-16. The "non-winning choices will be available again" promise. |
| <https://store.steampowered.com/news/app/3164500/view/1836506165582327> | 🟢 v0.4.6 Open Beta, 2026-07-09. Full change list. |
| <https://store.steampowered.com/news/app/3164500/view/1830163047259391> | 🟢 Patch v0.4.5f2, 2026-04-17. |
| <https://store.steampowered.com/news/app/3164500/view/1828441623108889> | 🟢 v0.4.5 Anniversary Update, 2026-03-30. |
| <https://store.steampowered.com/news/app/3164500/view/1827626365752067> | 🟢 v0.4.4 Weather Update, 2026-03-19. Police sentry additions; warehouse worker limit 10→12. |
| <https://store.steampowered.com/news/app/3164500/view/1823191198612472> | 🟢 v0.4.3 Storage Closets, 2026-02-02. Dealer 10-customer cap, dealer app redesign, `setdayduration`. |
| <https://store.steampowered.com/news/app/3164500/view/1819386365108685> | 🟢 v0.4.2 Shrooms, 2025-12-26. |
| <https://store.steampowered.com/news/app/3164500/view/1815034432980586> | 🟢 v0.4.1 Halloween, 2025-11-02. Sewers as a police-free layer. |
| <https://store.steampowered.com/news/app/3164500/view/1811772772305948> | 🟢 Patch v0.4.0f8, 2025-09-27. Checkpoint/vision/pursuit tweaks. |
| <https://store.steampowered.com/news/app/3164500/view/1809235871567996> | 🟢 v0.4.0 Rival Cartel, 2025-08-27. |
| <https://store.steampowered.com/news/app/3164500/view/1801617199481467> | 🟢 v0.3.6, 2025-06-08. Beds → lockers; employee property transfers. 🟠 Balance figures superseded. |
| <https://api.steampowered.com/ISteamNews/GetNewsForApp/v2/?appid=3164500&count=500&maxlength=0> | 🟢 Steam news API. **80 items, complete archive back to 2024-09-04.** Captured to `raw/steam-news-*`. |
| <https://steamcommunity.com/app/3164500> | 🟢 Corroborates the v0.4.6 notes. |
| <https://trello.com/b/VQQpru3F/schedule-i-roadmap> | 🟢 **Official Trello roadmap**, sole member Tyler (tyler_tvgs), board last activity 2026-07-13. Fetched as JSON → `raw/trello-board.json`. ⚠️ Highest trust for card *names/lists*; **card descriptions are stripped/encrypted in the public export**, so it is *not* a design-brief source. |
| `https://trello.com/c/{1eSfUIFM, 2V3qA9i0, 4XqwnMXg, 5i4t7UQX, 6dDYjf1z, CBq3237G, ddUsIjkF, hxaS1kcq, IwgSiGMh, JB9PFrNB, O7P75IXw, oRWcm1dz, QmmlCTSJ, QzRY2aMp, tPNHHTvP, vk7gcscu, ZcVeqsi7}` | 🟢 Individual Trello card permalinks harvested from the board export (17 cards, incl. the two live Police cards and the Drivers card). Titles only. |
| <https://patchbot.io/games/schedule-i> | 🟡 Third-party changelog index. Corroborates the version ladder v0.4.6 ← 0.4.5f2 ← 0.4.5 ← 0.4.4 ← 0.4.3 ← 0.4.2. Current. |
| <https://discord.gg/qKMRFzgSmg> | 🔴 Official Schedule I Discord. **Not scrapable, not joined.** No dev commentary on drivers / federal agents / outlaw status exists outside the Steam announcements above. Documented coverage gap. |

---

## 2. S1API (our dependency) and its immediate ecosystem

| URL | Note |
|---|---|
| <https://github.com/ifBars/S1API> | 🟢 **THE dependency.** Cloned `stable` @ `db421c99`, 2026-08-03. **MIT.** 22★, 27 forks, 984 files. Its ~90 Harmony targets are the best hook catalogue in the ecosystem. |
| <https://api.github.com/repos/ifBars/S1API> | 🟢 Metadata: `fork: true`, `parent: KaBooMa/S1API`, licence MIT, pushed 2026-08-03T21:43Z. |
| <https://github.com/ifBars/S1API/releases> | 🟢 Release notes v3.1.0 → v3.1.7, read via API. **Effectively a per-patch log of what Schedule I updates break.** Most valuable single artefact for update-resilience reasoning. |
| <https://github.com/ifBars/S1API/blob/stable/S1API/S1API.cs> | 🟢 Reference `MelonMod` lifecycle; `MelonPriority(Int32.MinValue)`; scene gating on `"Main"`. |
| <https://github.com/ifBars/S1API/blob/stable/S1API/Quests/Quest.cs> | 🟢 `Quest : Saveable` surface + the namespace-alias pattern for `#if IL2CPPMELON`. |
| <https://github.com/ifBars/S1API/blob/stable/S1API/docs/getting-started.md> | 🟢 The "do not reference `Assembly-CSharp.dll`" warning, verbatim. |
| <https://github.com/ifBars/S1API/blob/stable/S1API/docs/custom-npcs.md> | 🟢 Minimal custom-NPC example, messaging, voices. ⚠️ Contains two errors: `protected override bool IsPhysical` (source says `public`) and `Schedule.InitializeActions()` (not found in source). |
| <https://github.com/ifBars/S1API/blob/stable/S1API/docs/phone-app.md> | 🟢 Complete phone-app example + the 3.0.6→3.1 `ExitAction` migration worked example. |
| <https://github.com/ifBars/S1API/blob/stable/S1API/docs/tv-app.md> | 🟢 Source of the exact `MelonGame("TVGS", "Schedule I")` strings. |
| <https://raw.githubusercontent.com/ifBars/S1API/HEAD/S1API/docs/appearance-customization.md> | 🟢 Appearance API shape. ⚠️ Some example asset paths use **wrong folder casing** vs the shipped constants — prefer the typed constant classes. Captured to `raw/`. |
| <https://github.com/ifBars/S1API/tree/stable/skills/schedule-one-custom-npcs> | 🟢 S1API's own AI-agent skill (3 files). Officially recommended for coding agents. Captured to `raw/`. |
| <https://raw.githubusercontent.com/ifBars/S1API/HEAD/skills/schedule-one-custom-npcs/references/s1api-custom-npc-reference.md> | 🟢 Condensed custom-NPC reference **derived from AvatarFramework source** — value clamps, layer limits (face 6 / body 6 / accessory 9), defaults, the dialogue recursion rule. Higher trust than typical docs. |
| <https://ifbars.github.io/S1API/> | 🟢 docfx docs site, built from `S1API/docs/`. Content read from repo source rather than the rendered site. |
| <https://www.nuget.org/packages/S1API.Forked> | 🟢 81 versions, 1.6.7 → **3.1.7**, author `ifBars`. **This is the package we use.** |
| <https://api.nuget.org/v3-flatcontainer/s1api.forked/index.json> | 🟢 Authoritative version index. |
| <https://www.nuget.org/packages/S1API> | 🟠 The **dead upstream** package (KaBooMa), 13 versions, stalled at 1.6.2. Do not use. |
| <https://github.com/KaBooMa/S1API> | 🟠🔴 Upstream original. **Abandoned** (last push 2025-05-23), **no LICENSE file** despite the nuspec declaring MIT — a genuine licensing contradiction. Prefer the fork. |
| <https://github.com/ifBars/S1APITemplate> | 🟢 Canonical csproj + 15 agent-skill reference files, last commit 2026-08-02. ⚠️ **No LICENSE — read for technique only.** Source of the `MONO`/`IL2CPP` symbol convention and the failure checklist. |
| <https://github.com/ifBars/S1APITemplate/blob/main/S1APITemplate.csproj> | 🟢 The authoritative project shape (quoted verbatim in `MODDING-ECOSYSTEM.md` §1.3). |
| <https://github.com/ifBars/S1APITemplate/blob/main/.agents/skills/schedule-one-modding/references/build-config.md> | 🟢 The Steam branch ↔ backend mapping: `none/beta = IL2CPP`, `alternate/alternate-beta = Mono`. |
| <https://github.com/ifBars/S1APINPCExample> | 🟢 Four complete example NPCs; the four `.cs` files are downloaded to `raw/s1apiexample_*`. Last push 2026-07-18. ⚠️ **No LICENSE — read, don't paste.** |
| <https://github.com/ifBars/S1APINPCExample/blob/master/NPCs/ExamplePhysicalNPC1.cs> · `ExamplePhysicalNPC2.cs` · `ExamplePhysicalDealerNPC.cs` · `CharacterCustomizerNPC.cs` | 🟢 The specific files quoted in `MODDING-ECOSYSTEM.md` §4.3–4.6. |
| <https://github.com/ifBars/S1Interop> | 🟡 Dual-runtime toolchain, **GPL-3.0**, alpha, 2★, created 2026-06-30. Its `analyze`/`lint` diagnostics look useful but the licence and maturity rule out a dependency. |
| <https://deepwiki.com/ifBars/S1API> · <http://context7.com/ifbars/s1api/llms.txt> | 🟡 Alternate S1API doc surfaces linked from the README. **Not fetched** — listed for completeness. |
| <https://thunderstore.io/c/schedule-i/p/ifBars/S1API_Forked/> · <https://www.nexusmods.com/schedule1/mods/1194> | 🟡 Distribution channels (85K downloads on Thunderstore). Pages not fetched. |

---

## 3. Community modding documentation and wikis

| URL | Note |
|---|---|
| <https://s1modding.github.io/> · source <https://github.com/s1modding/s1modding.github.io> | 🟢 **The community wiki, MIT, actively maintained** (last push 2026-08-03). Read in full: `moddevs/il2cpp.md`, `patching.md`, `environment_setup.md`, `melonloader_utilities.md`. ⚠️ Page front-matter dates are all 2025-06, so individual pages may not have been revised for 0.4.6. |
| <https://s1modding.github.io/docs/moddevs/il2cpp/> | 🟢 Canonical source for `#if IL2CPP`/`#if MONO`, `RegisterTypeInIl2Cpp`, the `._items` LINQ workaround, `Il2CppType.Of<T>()`, the `(UnityAction)(() => {})` cast, and "no `IEnumerator`s in injected types". |
| <https://github.com/s1modding/s1modding.github.io/blob/main/content/docs/moddevs/patching.md> | 🟢 The "no transpilers on IL2CPP" rule, verbatim; manual `harmony.Patch` example; `[HarmonyDontPatchAll]`. |
| <https://github.com/s1modding/s1modding.github.io/blob/main/content/docs/moddevs/melonloader_utilities.md> | 🟢 `MelonEvents`, `MelonCoroutines`, `MelonPreferences` file locations and `SetFilePath`. |
| <https://github.com/s1modding/s1modding.github.io/blob/main/content/docs/moddevs/il2cpp.md> | 🟢 Repo source for the above. |
| <https://s1modding.github.io/docs/modusers/troubleshooting/> | 🟢 Branch-mismatch signatures (`Version=6.0.0` vs `Version=0.0.0`) and the `cpp2il_out` cleanup fix. |
| <https://s1modding.github.io/docs/> | 🟢 Docs index. |
| <https://doodesch-mods.github.io/ScheduleOne-Cookbook/> · repo <https://github.com/DooDesch-Mods/ScheduleOne-Cookbook> | 🟢 **The highest-value single document in the ecosystem.** Curated FishNet / IL2CPP / AssetBundle / preferences / update-tracking knowledge base, every claim carrying a Discord permalink. NOASSERTION licence, last push 2026-08-01. Second-hand by nature — treat contents as 🟡. |
| <https://discord.gg/9Z5RKEYSzq> | 🔴 Unofficial Schedule One Modding Server. **The primary knowledge source in this ecosystem; not joined.** Every Cookbook claim links into it. **The biggest remaining coverage gap.** |
| <https://discord.gg/UD4K4chKak> | 🔴 Schedule I Modding Discord (linked from the wiki's `environment_setup.md`). Not joined. |
| <https://github-wiki-see.page/m/FearAndDelight/Schedule-1-Modder-Documentation/wiki> · `/wiki/NPCs` | 🟠🔴 **The GitHub repo 404s;** only this mirror survives. Unmaintained (~2025). Useful only for the "switch to Alternate beta for Mono, decompile with dnSpy" trick and the UnityExplorer recommendation. Its NPC class/field names did independently match local verification. |

---

## 4. MelonLoader / Il2CppInterop / Harmony

| URL | Note |
|---|---|
| <https://github.com/LavaGang/MelonLoader> | 🟢 Loader source, 4k★. Authoritative. |
| **`https://api.github.com/repos/LavaGang/MelonLoader/releases`** | 🟢 **Verified live 2026-08-03 for this document.** Latest stable **v0.7.3 (2026-05-14)** — Il2CppInterop → `1.5.1-ci.845`, fixes `OnPreferencesSaved`/`OnPreferencesLoaded` sometimes not firing, fixes automatic Harmony patching scanning unannotated types. v0.7.2 (2026-03-03) "Reimplemented Il2CppInteropFixes". **Our install is v0.7.1 (2025-06-21) — a year old.** |
| <https://melonwiki.xyz/> | 🔵 MelonLoader official wiki. Live as of 2026-08. |
| <https://melonwiki.xyz/#/modders/quickstart> | 🔵 `OnInitializeMelon` vs `OnLateInitializeMelon` ordering. 🟡 relayed, not read first-hand by A1. |
| <https://melonwiki.xyz/#/modders/il2cppdifferences> | 🔵 The deeper IL2CPP reference; canonical `RegisterTypeInIl2Cpp` / `IntPtr` ctor / `DerivedConstructorPointer` documentation. |
| <https://melonwiki.xyz/#/modders/preferences> | 🔵 MelonPreferences reference. |
| <https://github.com/LavaGang/MelonWiki/blob/master/docs/modders/il2cppdifferences.md> | 🟢 Repo source of the above. |
| <https://github.com/LavaGang/MelonLoader/pull/1122> | 🟢 Merged 2026-03-26; AssetBundle icall fixes **explicitly tested on Unity 2022.3.62f2** — our exact version. Highly relevant. |
| <https://github.com/LavaGang/MelonLoader/issues/912> | 🟢 Explains why `TMPro` becomes `Il2CppTMPro`. |
| <https://github.com/LavaGang/MelonLoader/issues/1142> · `/issues/1159` | 🟡 Unity 6000.x breakage (duplicate `<>O` in `UnityEngine.CoreModule`). Not our version yet, but the class of failure to expect. |
| <https://github.com/V1ndicate1/FixCoreModule> | 🟡 MIT workaround for the above. |
| <https://github.com/LavaGang/UnityEngine.Il2CppAssetBundleManager> · [`Il2CppAssetBundleManager.cs`](https://github.com/LavaGang/UnityEngine.Il2CppAssetBundleManager/blob/master/Il2CppAssetBundleManager.cs) | 🟢 **Required on IL2CPP** — the managed `AssetBundle.LoadFromMemory` is stripped from this build. Code quoted verbatim. |
| <https://github.com/xmusjackson/UnityEngine.BE.Il2CppAssetBundleManager> | 🟡 BepInEx port of the same shim. |
| <https://github.com/BepInEx/Il2CppInterop> · [`/tree/master/Documentation`](https://github.com/BepInEx/Il2CppInterop/tree/master/Documentation) | 🔵 The interop layer itself (v1.5.0 ships with ML 0.7.1). Authoritative. |
| <https://github.com/BepInEx/Il2CppInterop/blob/master/Documentation/Class-Injection.md> | 🔵 Official simple-vs-extended class injection reference. |
| <https://harmony.pardeike.net/> | 🔵 HarmonyX/Harmony docs. Live. ⚠️ Documents current Harmony; **we run HarmonyX 2.10.2**. |
| <https://github.com/TrevTV/MelonLoader.VSWizard/releases> | 🟡 Official MelonLoader VS template, wiki-linked. |
| <https://stackoverflow.com/questions/79496143/how-to-use-methods-from-registertypeinil2cpp-in-melonloader> | 🟠 March 2025, **unanswered**. Illustrates the injected-component instantiation footgun. Low weight. |
| <https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/proposals/csharp-10.0/lambda-improvements> | 🔵 Microsoft official — why `(UnityAction)(() => {})` compiles on net6. |

---

## 5. Decompiled game source, datamining and version tracking

| URL | Note |
|---|---|
| <https://github.com/k073l/s1-codearchiver> | 🟢 ⭐ **The best cross-reference, and it's current.** Branch `alternate` @ `3973cfc7` — *"Auto-update for version 0.4.6f11 Alternate"*, 2026-08-01. **2,026 stripped `.cs` files.** High trust for signatures, **zero for bodies** (stripped). ⚠️ No LICENSE; content © TVGS. Decompiles the **Mono** branch, which is strong evidence for IL2CPP but not proof. |
| <https://raw.githubusercontent.com/k073l/s1-codearchiver/alternate/ScheduleOne-stripped/Law/LawManager.cs> | 🟢 Working raw-URL pattern. Files read: `DevUtilities/{Singleton,NetworkSingleton,PlayerSingleton}.cs`, `Employees/{EEmployeeType,Employee}.cs`, `Economy/EDealerType.cs`, `Law/LawManager.cs`, `NPCs/NPCManager.cs`. |
| <https://github.com/Skippeh/ScheduleOne_UnityProject> | 🟢 Unity project with stripped scripts + `.meta` files. **Requires Unity 2022.3.62f2 exactly — matches our install.** Last push 2026-04-01, 12★. ⚠️ Mono branches only; no LICENSE. Prerequisite for prefab/ScriptableObject inspection. |
| <https://github.com/GuysWeForgotDre/S1DataMining/blob/main/diff.json> | 🟡 Machine-readable 0.3.6f6 → 0.4.0 class/method diff. ⚠️ **The `Added` and `Removed` labels are reversed** (author's own warning). 🟠 Historical only. Not fetched by us. |
| <https://github.com/k073l/RefGen> | 🟡 MIT, 2026-03-31. Per-version reference-assembly NuGet packages — turns "building against an older game version" into a version bump. |
| <https://github.com/perfare/Il2CppDumper> | 🟡 ⚠️ README states Unity **5.3–2022.2** support; Schedule I is **2022.3** — outside the stated range. You almost certainly don't need it; MelonLoader's own generator produces better assemblies. |
| <https://github.com/AssetRipper/AssetRipper/releases> · [`/wiki`](https://github.com/AssetRipper/AssetRipper/wiki) · [`1.3.14 win-x64`](https://github.com/AssetRipper/AssetRipper/releases/download/1.3.14/AssetRipper_win_x64.zip) | 🟢 Full Unity project export. The workflow is documented in the wiki's `ripping_the_project.md`. |
| <https://github.com/icsharpcode/ILSpy> (`dotnet tool install --global ilspycmd`) | 🔵 The template's prescribed one-type-at-a-time decompiler. |
| <https://github.com/aelurum/AssetStudioMod/releases> · [`v0.19.0 net472`](https://github.com/aelurum/AssetStudioMod/releases/download/v0.19.0/AssetStudioModCLI_net472_win32_64.zip) | 🟢 **The tool actually used** for our sprite/font/palette extraction — the net472 build, because this machine has only .NET 6/8 runtimes. See `ASSETS-TOOLCHAIN.md`. |
| <https://github.com/vfsfitvnm/frida-il2cpp-bridge/discussions/650> | 🟡 May 2025. Maintainer confirmation that IL2CPP permanently discards the original `Assembly-CSharp.dll` — dumps recover names/signatures, never bodies. Context for why no true Schedule I source dump exists. |
| <https://github.com/thecatontheceiling/scheduleone> | 🔴 **404 — repository deleted.** Verified twice: API 404 *and* absent from that user's 24 public repos. |
| <https://deepwiki.com/thecatontheceiling/scheduleone> | 🔴🟠 Surviving snapshot of the above; documents **v0.3.5**, five minor versions stale. **Do not use — use `s1-codearchiver` instead.** |

---

## 6. Published mods — source available, ranked by relevance

### 6a. Custom NPCs / customers / dealers

| URL | Note |
|---|---|
| <https://github.com/DooDesch-Mods/ScheduleOne-Personnel> | 🟢 **MIT, 2026-08-02, v2.1.1. The #1 reference for S1API custom NPCs.** Cloned and read. Source of the uninitialized-instance contract, deterministic type ordering for co-op, impostor fallback, `AddMapMarker`, and the `<ModName>_01_Main` preferences convention. |
| <https://thunderstore.io/c/schedule-i/p/DooDesch/Personnel/> | 🟡 Mod page — v2.1.1, pack format, *"In co-op, everyone needs the same packs installed."* |
| <https://github.com/hdlmrell/OTC-S1-Mod> · page <https://thunderstore.io/c/schedule-i/p/hdlmrell/OverTheCounter/> | 🟢 source read / 🟡 page. **NOASSERTION licence — treat as all-rights-reserved.** 2026-04-14, 9.6K dl. Primary source for `NetworkHelper.IsHost` and the `Customer`/`Contract`/phone patch catalogue. |
| <https://github.com/ifBars/BigWillyMod> · page <https://thunderstore.io/c/schedule-i/p/ifBars/BigWillyMod/> | 🟢 Last push 2026-06-20. ⚠️ **Licence contradiction: MIT per the GitHub API, GPL v3 per the Thunderstore page text.** Reconcile before copying. `NPCs/BigWilly.cs` downloaded to `raw/bigwilly_*`. Highest-trust NPC appearance reference. |
| <https://github.com/k073l/s1-dynamicnpcs> | 🟠 Data-driven NPCs on S1API. **No licence.** Last push 2026-01-25 — somewhat stale. |
| <https://github.com/KaenSera01/Empire2.0_S1API> | 🟡 JSON-driven custom buyers/dealers. Its roadmap lists custom avatars as *future* work — **not** an appearance reference. |
| <https://github.com/manzune/AdvancedDealing> · fork <https://github.com/UrbanSide/AdvancedDealing> | 🟡 **MIT.** Dealers deliver cash, message via the Messages app, negotiate cut. The FPZone fork (2026-07-16) is a **save-migration case study** for surviving 0.4.5f2. |
| <https://github.com/EndureBlackout/so-better-fiends> | 🟠 Customers become fiends, demand product, call cops. 2025-04-26 — stale, patterns only. |
| <https://github.com/ReservedKeyword/Schedule-I-Twitch-Customers> | 🟠 **MIT**, 2025-04-06. Injects externally-sourced customers. "MelonLoader (formerly BepInEx)". Stale. |
| <https://github.com/Joive/Schedule-I-Customer-Relations> | 🟡 Customer/NPC relationship manipulation via an in-game menu. Licence unchecked. |
| <https://github.com/XOWithSauce/schedule-feeningnpcs> | 🟡 Customer-behaviour modification precedent. |
| <https://github.com/Papacester/nightmare-monkey> (Narcopelago) | 🟡 *"Randomize Dealers, Customers, Cartel Influence"* — deep economy hooks. 2026-08-02. |

### 6b. Police / law

| URL | Note |
|---|---|
| <https://github.com/XOWithSauce/schedule-nacops> · page <https://thunderstore.io/c/schedule-i/p/XO_WithSauce/NACops_IL2CPP/> | 🟢 source read / 🟡 page. **NO LICENCE — learn from, do not copy.** 2026-08-03, v2.1.0, page says `Game version: 0.4.5f2 default`. **The police bible:** exact patch targets, runtime networked-officer cloning, `LawActivitySettings` replacement, teardown discipline. |
| <https://thunderstore.io/c/schedule-i/p/XO_WithSauce/NACops_MONO/source/> | 🟡 Decompiled Mono source of the same mod, via Thunderstore's source viewer. |
| <https://github.com/XOWithSauce/schedule-cartelenforcer> · <https://old.thunderstore.io/c/schedule-i/p/XO_WithSauce/Cartel_Enforcer_MONO/source/> | 🟢 **MIT**, 2026-08-03, 27K dl. Same author, same init helper as NACops — **read this one when you need to copy code.** Hostile-faction events, `[RegisterTypeInIl2Cpp]` game-`Quest` subclassing, `AvatarWeapon`/`AvatarEquippable` manipulation. |
| <https://github.com/DooDesch-Mods/ScheduleOne-LooseEnds> | 🟢 **MIT**, 2026-08-02. Smallest clean police-reaction source (`NPCHealth.*`, `Draggable.*`). Good first read. |
| <https://github.com/surrealnirvana/LawEnforcementEnhancementMod> · page <https://thunderstore.io/c/schedule-i/p/Surrealnirvana/Enhanced_Law_Enforcement/> | 🟠 **No licence**, 2025-04-21 — **stale**. `[RegisterTypeInIl2Cpp]` MonoBehaviour + `ServerManager.Spawn` + JSON config patterns only. |
| <https://github.com/Babyhamsta/HardcorePoliceMod-Schedule1> · page <https://thunderstore.io/c/schedule-i/p/JD/HardcoreAI/> | 🟠 **Apache-2.0**, 2025-04-15 — **stale**. Manual `harmony.Patch(method, postfix:)` style. |
| <https://github.com/SadPoty/PickPocketPolice> · page <https://thunderstore.io/c/schedule-i/p/SadPoty/PickPocket_Police/> | 🟠 2025-05-13. Police interaction flags. Licence unchecked. |
| <https://github.com/Foxcapades/schedule-1-mods> · page <https://thunderstore.io/c/schedule-i/p/Foxcapades/CopHealthModifierIl2Cpp/> | 🟡 Minimal example of touching officer stats. |
| <https://github.com/AssailentDev/Section97> · page <https://thunderstore.io/c/schedule-i/p/Assailent/Section97/> | 🟠 2025-04-16, 13.1K dl. Store robbery / mugging → crime + police response. |
| <https://thunderstore.io/c/schedule-i/p/HazDS/NoPolice/> | 🟡 No `.cs`, but its description **enumerates the police subsystems**: *"patrols, pursuits, body searches, checkpoints, and curfew enforcement… each individually configurable."* |
| <https://thunderstore.io/c/schedule-i/p/UncleTyrone/EvenMoreFootPatrols_IL2CPP/> · <https://old.thunderstore.io/c/schedule-i/p/UncleTyrone/EvenMoreFootPatrols_MONO/source/> · <https://www.nexusmods.com/schedule1/mods/1202> | 🟠 2025-09-27 / decompile ~10 months old. Confirms patrol routes are data-driven and injectable; clearest prefab-clone reference, but **pre-dates several updates — API drift likely.** |

### 6c. Employees / logistics / drivers

| URL | Note |
|---|---|
| <https://github.com/k073l/s1-employeetweaks> | 🟡 **MIT**, 2026-08-02. ⭐ **Closest MIT prior art for Hireable Drivers.** Metadata verified; **source not yet read — highest-value remaining read.** |
| <https://github.com/k073l/BusinessEmployment> | 🟡 **MIT**, 2026-08-01, 12K dl. Proves mod-side employee creation works. Source not yet read. |
| <https://github.com/k073l/s1-multidelivery> | 🟡 **MIT**, 2026-08-02. Multiple deliveries from one store — delivery-system patching. |
| <https://thunderstore.io/c/schedule-i/p/k0Mods/> | 🟡 k073l's full package list (SewerEmployees, InputDock, FurnitureDelivery, …). |
| <https://github.com/DevKaiE/DealerTransportMod> · page <https://thunderstore.io/c/schedule-i/p/KaikiNoodles/DealerSelfSupplySystem/> | 🟠 **No licence**, 2025-04-23, 16.7K dl. Smallest source-available **NPC-drives-logistics** mod. Stale; patch targets only. |
| <https://github.com/archenovalis/NoLazyWorkers> · page <https://thunderstore.io/c/schedule-i/p/ArchieN/NoLazyWorkers_Il2Cpp/> | 🟠 **No licence**, 2025-08-06, 22K dl combined. Employee task-graph reference. |
| <https://github.com/archenovalis/KLINE> · page <https://thunderstore.io/c/schedule-i/p/ArchieN/KLINE_Il2Cpp/> | 🟠 2025-04-29. *"Multiple vehicles will deliver it"* — multi-van spawning. |
| <https://github.com/GuysWeForgotDre/Improved-Packagers> · page <https://thunderstore.io/c/schedule-i/p/Dre/ImprovedPackagers/> | 🟠 2025-08-11. *"Load vehicles in Loading Bays"* — **the exact vanilla subsystem our drivers must drive.** |
| <https://thunderstore.io/c/schedule-i/p/bwyan/HireableDeliveryDriver_IL2CPP_port/> | 🟡 **No source.** 2026-01-05, 2.5K dl. **The closest existing thing to Hireable Drivers.** Source of the host-only MP quote. Author claim, medium trust. |
| <https://thunderstore.io/c/schedule-i/p/notsurewhattoputhere/HireableDeliveryDriver/> | 🟡 **No source.** The Mono original, 2026-01-04, 3.3K dl. |
| <https://thunderstore.io/c/schedule-i/p/OmniCorp/Smarter_Employees/> · <https://thunderstore.io/c/schedule-i/p/NanobotZ/CrossProperty_Transportation_IL2CPP/> · <https://thunderstore.io/c/schedule-i/p/Daudr/VehicleDeliverySlots/> · <https://thunderstore.io/c/schedule-i/p/Jumble/Joyrider/> | 🟡 No source. Feature-shape precedents: filtered item movement, inter-property routing, configurable trunk slots, carjacking + police vehicles. |

### 6d. UI / main menu / phone / frameworks

| URL | Note |
|---|---|
| <https://github.com/DooDesch-Mods/ScheduleOne-SideHustle> | 🟢 **MIT**, 2026-08-03. **The main-menu-injection reference implementation.** `MenuScreen.OpenOnStart` discovery, 20-frame warmup, label-matched button clone, `NeutralizeClick`, no Harmony, no AssetBundle. |
| <https://sidehustle.doodesch.de> · <https://support.doodesch.de/sidehustle> | 🟡 Author-operated lobby browser and support site. Not independently verified. |
| <https://github.com/RoachxD/ScheduleOne.HonestMainMenu> · page <https://thunderstore.io/c/schedule-i/p/Roachified/HonestMainMenu/> · [source view](https://thunderstore.io/c/schedule-i/p/Roachified/HonestMainMenu/source/) | 🟢 **MIT**, 2026-01-10. **Source of the verbatim menu scene/object names** (`MainMenu`, `Home/Bank`, `Title`, `MainMenuPopup.Instance.Open`) and the `MissingMethodException` retry pattern. 🟠 Slightly stale (Jan 2026); its hard-coded paths are a liability vs SideHustle's discovery. |
| <https://github.com/k073l/s1-modsapp> · page <https://thunderstore.io/c/schedule-i/p/k0Mods/ModsApp/> | 🟢 **MIT**, 2026-08-02, 16.9K dl. **The de-facto mod-settings framework** + its `PREFERENCES.md`, the authoritative MelonPreferences hot-reload guide. Requires S1API ≥ 3.0.1. |
| <https://www.nexusmods.com/schedule1/mods/397> | 🟡 Prowiler's "Mod Manager & Phone App" — **the other settings-UI standard**, named by Hotline/Personnel/WarehousePlus. **No public source.** 🟠 Page cached 2025-04-27. |
| <https://github.com/DooDesch-Mods/ScheduleOne-Hotline> · page <https://thunderstore.io/c/schedule-i/p/DooDesch/Hotline/> | 🟢 **MIT**, 2026-08-03. Unified in-game overlay + one master key; drop-in `HotlineApi.cs` shim that no-ops when absent. Does **not** use S1API. |
| <https://github.com/DooDesch-Mods/ScheduleOne-Personify> · page | 🟢 **MIT**, 2026-08-02. In-game NPC editor as a **menu-scene overlay**; exports Personnel packs. Second menu-surface reference. |
| <https://github.com/DooDesch-Mods/ScheduleOne-Inkubator> · page | 🟢 **MIT**, 2026-08-02. 3D tattoo editor, also a menu-scene overlay launched from SideHustle. |
| <https://github.com/DooDesch-Mods/ScheduleOne-Sideload> · page | 🟢 **MIT**, 2026-08-02. HTML/CSS/JS → real Unity UI on the in-game phone. Also the source of the `Il2CppAssetBundleManager` stripping note. |
| <https://github.com/ifBars/bGUI> | 🟡 **MIT**, 2026-06-26. Builder-styled runtime uGUI. |
| <https://github.com/ifBars/S1MAPI> · page <https://thunderstore.io/c/schedule-i/p/ifBars/S1MAPI/> | 🟡 **GPL-3.0 ⚠** — procedural meshes + glTF. **Do not link into our mods.** |
| <https://thunderstore.io/c/schedule-i/p/SirTidez/SaveExpansion/> | 🟡 *"Expands the native save menu from 5 to 20 slots"* — a third menu-surface data point. **Source repo not locatable** (the `website_url` is a bare profile). |
| <https://thunderstore.io/c/schedule-i/p/SanicDev/Disclaimer_Skip_IL2CPP/> | 🟡 No source. Documents that the menu scene has a **pre-menu disclaimer step**. |
| <https://www.nexusmods.com/schedule1/mods/58> | 🟠 LethalLizard's in-game Mod Manager. Cached 2025-04-02. |
| <https://github.com/zampxdev/SewerMenu> · <https://github.com/firebirdjsb/NugzzMenu> · <https://thunderstore.io/c/schedule-i/p/zampx/SewerMenu/> | 🟡 Cheat-menu peers to our own Creative Mode. NugzzMenu is *"IL2CPP mod menu … using S1API"*. |
| <https://github.com/tiagovitorino97/SimpleLabels> · [`LabelMod.cs`](https://github.com/tiagovitorino97/SimpleLabels/blob/main/LabelMod.cs) | 🟡 A shipped Schedule I IL2CPP mod building runtime uGUI/TMP. Game-specific, code quoted. |
| <https://gist.github.com/NomadWithoutAHome/1fc4a18624eb42edff36e7551776ffd4> | 🟠 Unversioned gist — minimal from-scratch uGUI skeleton. **Treat as a sketch.** |
| <https://thunderstore.io/c/schedule-i/p/GrandDuchyOfGames/GDG_ScheduleI_Traditional_Chinese_Translation/source/> | 🟡 Decompiled; `Resources.FindObjectsOfTypeAll<TMP_FontAsset>()`, `Il2CppAssetBundleManager.LoadFromFile`, donor-material repair. |
| <https://thunderstore.io/c/schedule-i/p/MedicalMess/MultiplayerPlus/source/> | 🟡 Decompiled; runtime `TMP_InputField` / `TextMeshProUGUI` construction under Il2Cpp. |
| <https://thunderstore.io/c/hard-bullet/p/korbykob/Goose/source/> | 🟡 Not Schedule I, same loader/interop stack. `Il2CppAssetBundleManager.LoadFromMemory` usage. |
| <https://github.com/sinai-dev/UniverseLib> · [`UIFactory.cs`](https://github.com/sinai-dev/UniverseLib/blob/main/src/UI/UIFactory.cs) · [`UIBase.cs`](https://github.com/sinai-dev/UniverseLib/blob/main/src/UI/UIBase.cs) · [`CursorUnlocker.cs`](https://github.com/sinai-dev/UniverseLib/blob/main/src/Input/CursorUnlocker.cs) · [`EventSystemHelper.cs`](https://github.com/sinai-dev/UniverseLib/blob/main/src/Input/EventSystemHelper.cs) · [`Il2CppProvider.cs`](https://github.com/sinai-dev/UniverseLib/blob/main/src/Runtime/Il2Cpp/Il2CppProvider.cs) | 🟡 **The highest-quality open-source example of building uGUI under Il2CppInterop.** Widely used, actively referenced. |
| <https://github.com/sinai-dev/UnityExplorer> | 🟡 The consumer of UniverseLib; a full working IL2CPP runtime-UI application + in-game scene inspector. |

### 6e. Multiplayer / lobby

| URL | Note |
|---|---|
| <https://github.com/DooDesch-Mods/ScheduleOne-PropHunt> | 🟢 **MIT**, 2026-08-03. Best MP-gamemode source; the **patch-late-not-at-load** crash lesson; embedded-resource icon convention; `il2cppassetbundle` requirement. |
| <https://github.com/ifBars/SteamNetworkLib> · page <https://thunderstore.io/c/schedule-i/p/ifBars/SteamNetworkLib_Mono/> | 🟢 **MIT**, 2026-07-28, 23K dl. **The recommended IL2CPP multiplayer sync path** (Steam lobby data / P2P). |
| <https://github.com/DooDesch-Mods/ScheduleOne-FullHouse> · <https://github.com/ifBars/BiggerLobbies> · <https://github.com/Nyxis-Studio/SO_MultiplayerEnhanced> · <https://github.com/jasonlearst/Schedule1-BiggerCrew> · <https://thunderstore.io/c/schedule-i/p/ifBars/BiggerLobbies/> | 🟢🟡 Lobby-cap engines (MIT / GPL-3.0 mixed). Confirm the vanilla cap is **4** and that MP mods require all-players-install. |
| <https://github.com/ifBars/S1DS-SCOPE> · <https://github.com/ZackaryH8/S1DS-TextChat> · <https://github.com/ifBars/S1DS-PlayerList> | 🟡 **No licence.** A dedicated-server ecosystem with a paired client/server addon API exists (2026-06/07). Relevant only if we ever want authoritative server logic. |

### 6f. Performance, tooling and misc mods

| URL | Note |
|---|---|
| <https://github.com/DooDesch-Mods/ScheduleOne-Snitch> · <https://github.com/DooDesch/ScheduleOne-Siesta> | 🟢 **MIT**, 2026-08-02. Snitch profiles NPCs/trash/quests **and your own mods**; Siesta is NPC distance/visibility LOD. Both are the mitigation if dozens of custom NPCs cost FPS. |
| <https://github.com/HazDS/S1Mods> · pages <https://thunderstore.io/c/schedule-i/p/HazDS/GophxrMod/> · `/RenameNPCs/` | 🟡 **No licence**, releases only (no `.cs`). GophxrMod proves a single custom NPC + custom clothing ships and works. RenameNPCs enumerates every NPC-name surface. |
| <https://github.com/JCherryhomes/Schedule-1-Small-Corner-Map> · <https://github.com/youseemenot/schedule1-Skoofidons-Minimap> · <https://thunderstore.io/c/schedule-i/p/CherryMods/SmallCornerMap/> | 🟡 The latter does *"real-time tracking of NPCs, police, and co-op players"* — useful for reading the police/NPC registries. |
| <https://thunderstore.io/c/schedule-i/p/GreenCarrot/MoreDeals/> | 🟡 No real source. *"Customers will request from you multiple times per day"* — **the closest existing analogue to bulk-buying special customers.** |
| <https://thunderstore.io/c/schedule-i/p/DerTomDer/Lithium/> · <https://www.nexusmods.com/schedule1/mods/1138> | 🟡 No source. *"Modular balancing framework… Each feature is optional and fully toggleable"* — **the design pattern our toggle screen has to beat.** |
| <https://github.com/ScheduleLua/ScheduleLua-Framework> · page <https://thunderstore.io/c/schedule-i/p/ScheduleLua/ScheduleLua/> | 🟠 **GPL-3.0 ⚠**, 2025-10-07. Lua scripting, Mono-leaning. Not useful to us. |
| <https://thunderstore.io/c/schedule-i/p/FearAndDelight/Cute_And_Funny_Framework/> | 🟠 v0.2.0, ~11 months old, 508 dl. NPC creation was "experimental". **Likely abandoned** (the author's wiki 404s). |
| <https://github.com/k073l/S1MelonModTemplate> · <https://github.com/weedeej/S1MONO_IL2CPP_Template> · <https://github.com/chloelcdev/schedule-1-first-mod> · <https://github.com/rfvgyhn/schedule-one-mods> | 🟡 Project templates. k073l's is **MIT**, 2026-04-11, wiki-recommended. weedeej's is 🟠 2025-05-26 and stale. `rfvgyhn` documents the minimal IL2CPP reference set. |
| <https://thunderstore.io/c/schedule-i/p/KaBooMa/ScheduleOneEnhanced/> | 🟡 Adds the "Bicky Robby" NPC + bulk-order quest; claims first custom NPC+quest mod. Mono only. Primacy unverified. |

---

## 7. Mod distribution, managers and security

| URL | Note |
|---|---|
| <https://thunderstore.io/c/schedule-i/api/v1/package/> | 🟢 **Complete Schedule I package index — 494 entries incl. deprecated, parsed in full 2026-08-03.** Authoritative and current. |
| <https://thunderstore.io/c/schedule-i/> | 🟢 Community landing page; category taxonomy (`Audio / IL2CPP / Libraries / Misc / Modpacks / Mods / Mono / Tools`), "213 results". |
| <https://api.github.com/orgs/DooDesch-Mods/repos> | 🟢 41 repos with licences and push dates. |
| `https://api.github.com/repos/{owner}/{name}` | 🟢 Query template — used for licence + last-push on all 26 cited repos. |
| `https://api.github.com/search/repositories?q=topic:schedule-1` (and `schedule-i`, `scheduleone`, `schedule1`, `"Schedule I" MelonLoader mod`) | 🟢 18/20/4/12/21 results. **Topic coverage is sparse — a supplement, not a census.** |
| <https://thunderstore.io/c/schedule-i/p/ifBars/MLVScan/> | 🟢 Security scanner plugin, 20K dl, rating 9. *"Detects and disables potentially malicious mods."* **Our DLLs will be scanned on users' installs.** |
| <https://thunderstore.io/c/schedule-i/p/hdlmrell/OTCLoader/> · repo <https://github.com/hdlmrell/OTC-Loader> | 🟡 18K dl. *"Auto-detects your game branch and disables incompatible mod DLLs before they crash."* |
| <https://thunderstore.io/c/schedule-i/p/the_croods/SwapperPlugin/> | 🟡 No source, 80.9K dl. Seamless IL2CPP↔Mono backend swapping — shows how much branch pain exists. |
| <https://thunderstore.io/c/schedule-i/p/ebkr/r2modman/> · <https://thunderstore.io/c/schedule-i/p/Kesomannen/GaleModManager/> | 🟢 The two dominant Thunderstore managers. **Our `manifest.json` must declare dependencies correctly for these.** |
| <https://www.nexusmods.com/schedule1/mods/1750> | 🟡 SIMM — branch-aware external mod manager, recommended by ModsApp's README. |
| <https://www.nexusmods.com/schedule1/mods/top> · <https://next.nexusmods.com/games/schedule1> · <https://www.nexusmods.com/schedule1/about/stats> | 🟠🔴 **Nexus is login-walled and serves heavily cached snapshots.** The "Top files" page retrieved was an **April 2025** snapshot; Nexus' own counters disagreed across pages (535 / ~993 / "over 1,150"). **Unreliable for totals; names and IDs only.** Nexus is a known coverage gap in this survey. |
| <https://www.nexusmods.com/schedule1/mods/1220> (More NPCs) · `/1938` (npc plus) · `/1584` (WarehousePlus) | 🟡 Mod-author claims only; page content cached. |
| <https://www.nuget.org/packages/FishNetV3.CodeGenerator.MSBuild> | 🟡 `1.0.0-beta.11`. **Mono only** — does not help us on IL2CPP. |

---

## 8. FishNet / networking documentation

| URL | Note |
|---|---|
| <https://fish-networking.gitbook.io/docs> | 🔵 Live FishNet docs. ⚠️ **These are v4; the game runs v3.** Concepts and attribute names carry over, specifics may not. |
| <https://fish-networking.gitbook.io/docs/guides/features/network-communication/remote-procedure-calls.md> | 🔵 `ServerRpc` / `ObserversRpc` / `TargetRpc` / `Channel` / `RunLocally` / `DataLength`, read verbatim. Same v3/v4 caveat. |
| <https://fish-networking.gitbook.io/docs/guides/features/network-communication/synchronizing.md> | 🔵 SyncTypes. Same caveat. |
| <https://fish-networking.gitbook.io/docs/guides/features/network-communication/broadcasts.md> | 🔵 Broadcasts — the escape hatch used by the hand-rolled IL2CPP route. Same caveat. |
| <https://web.archive.org/web/20240324100202/https://fish-networking.gitbook.io/docs/> | 🟡 **Archived FishNet v3 docs — the version the game actually uses.** Recommended by the Cookbook. **Not fetched by us; cited on the Cookbook's authority.** Use this over the live docs when precision matters. |
| <https://partner.steamgames.com/doc/api> | 🔵 Steamworks lobby-data limits (~8 KB per player of member data; only the host may write lobby data). |

---

## 9. Unity official documentation (2022.3 branch — matches our 2022.3.62f2)

All 🔵 **Authoritative**, all on the 2022.3 branch or the matching package version, pages dated 2026-07-02 where checked.

| Area | URLs |
|---|---|
| Sprites / textures | [`Sprite.Create`](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Sprite.Create.html) · [`Texture2D.SetPixels`](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Texture2D.SetPixels.html) · [`Texture2D` ctor](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Texture2D-ctor.html) · [9-slice manual](https://docs.unity3d.com/2022.3/Documentation/Manual/9SliceSprites.html) |
| AssetBundles | [`AssetBundle.LoadFromMemory`](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/AssetBundle.LoadFromMemory.html) · [`BuildPipeline.BuildAssetBundles`](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/BuildPipeline.BuildAssetBundles.html) · [`BuildAssetBundleOptions`](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/BuildAssetBundleOptions.html) · [`BuildTarget`](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/BuildTarget.html) · [Building](https://docs.unity3d.com/2022.3/Documentation/Manual/AssetBundles-Building.html) · [Native](https://docs.unity3d.com/2022.3/Documentation/Manual/AssetBundles-Native.html) |
| Runtime lookup | [`Resources.FindObjectsOfTypeAll`](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Resources.FindObjectsOfTypeAll.html) · [`Shader.Find`](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Shader.Find.html) · [`AudioSource.PlayOneShot`](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/AudioSource.PlayOneShot.html) |
| uGUI | [`Image`](https://docs.unity3d.com/Packages/com.unity.ugui@1.0/api/UnityEngine.UI.Image.html) (`pixelsPerUnitMultiplier` lives here; the 2022.3 ScriptReference URL 404s) · [`Image.Type`](https://docs.unity3d.com/Packages/com.unity.ugui@1.0/api/UnityEngine.UI.Image.Type.html) · [`CanvasScaler`](https://docs.unity3d.com/Packages/com.unity.ugui@1.0/api/UnityEngine.UI.CanvasScaler.html) · [`ScrollRect`](https://docs.unity3d.com/Packages/com.unity.ugui@1.0/api/UnityEngine.UI.ScrollRect.html) · [`RectMask2D`](https://docs.unity3d.com/Packages/com.unity.ugui@1.0/api/UnityEngine.UI.RectMask2D.html) · [`ContentSizeFitter`](https://docs.unity3d.com/Packages/com.unity.ugui@1.0/api/UnityEngine.UI.ContentSizeFitter.html) · [`LayoutRebuilder`](https://docs.unity3d.com/Packages/com.unity.ugui@1.0/api/UnityEngine.UI.LayoutRebuilder.html) · [`EventSystem`](https://docs.unity3d.com/Packages/com.unity.ugui@1.0/api/UnityEngine.EventSystems.EventSystem.html) · [CanvasScaler manual](https://docs.unity3d.com/2022.3/Documentation/Manual/script-CanvasScaler.html) · [Multi-resolution](https://docs.unity3d.com/2022.3/Documentation/Manual/HOWTO-UIMultiResolution.html) · [Auto-layout](https://docs.unity3d.com/2022.3/Documentation/Manual/UIAutoLayout.html) |
| TextMeshPro 3.0 (the TMP major for Unity 2022.3) | [Manual](https://docs.unity3d.com/Packages/com.unity.textmeshpro@3.0/manual/index.html) · [`TMP_Text`](https://docs.unity3d.com/Packages/com.unity.textmeshpro@3.0/api/TMPro.TMP_Text.html) · [`TMP_FontAsset`](https://docs.unity3d.com/Packages/com.unity.textmeshpro@3.0/api/TMPro.TMP_FontAsset.html) |
| Input System | [`InputSystemUIInputModule`](https://docs.unity3d.com/Packages/com.unity.inputsystem@1.7/manual/UISupport.html) |
| Editor archive | <https://unity.com/releases/editor/archive> — where to get exactly **2022.3.62f2**. |
| TMP-in-bundles gotchas | 🟡 [Shader.Find fails for bundle shaders](https://discussions.unity.com/t/using-textmeshpro-with-assetbundles-and-runtime-tmp_fontasset-createfontasset/825012) · [TMP's Resources coupling duplicates/breaks font assets](https://discussions.unity.com/t/textmeshpro-addressables-asset-bundles/855482) — Unity forum, but the failure modes are well corroborated. |
| TMP font packaging workflow | 🟡 [XUnity.AutoTranslator wiki](https://github.com/bbepis/XUnity.AutoTranslator/wiki/TextMeshPro-Font-Asset-Creation-&-Packaging-Guide) — mature project, practical guide. |

---

## 10. Game wikis (balance data, mechanics)

All 🟡 **Community**. Per-page edit dates matter more than the domain — they are given where known.

| URL | Note |
|---|---|
| <https://schedule-1.fandom.com/wiki/Handlers> | 🟡 Edited **2026-04-08**. **The single best source on the shipped route system.** High trust, recent. |
| <https://schedule-1.fandom.com/wiki/Customers> | 🟡 Full per-customer data table (budget, affinities, standards, orders/week, order time, call-police chance) + demand simulation. Excellent; edited 2026. |
| <https://schedule-1.fandom.com/wiki/Police> | 🟡 Edited **2026-05-24**. Wanted levels, triggers, checkpoints, body searches, evasion, equipment. Recent. |
| <https://schedule-1.fandom.com/wiki/Game_mechanics> | 🟡 Edited **2026-07-10**. Day schedule and time. Very recent. |
| <https://schedule-1.fandom.com/wiki/Dealers> | 🟡 Buy-ins, 20% cut, 10-customer cap with explicit v0.4.3/v0.4.2 footnotes. Recent. |
| <https://schedule-1.fandom.com/wiki/Employees> | 🟡 Costs, wages, assignable stations, per-property limits. Current (post-locker). |
| <https://schedule-1.fandom.com/wiki/Penalties> | 🟠 **Last edited 2025-08-09 — the oldest data used in DESIGN-INTENT.md. The fine table may have drifted; re-verify in game.** Our Police heat formula is derived from it. |
| <https://schedule-1.fandom.com/wiki/Vehicles> | 🟠 Prices/cargo slots current, but **speed data was tested at v0.3.4f4 (2025) and is stale.** |
| <https://schedule-1.fandom.com/wiki/Updates> | 🟡 Version/date index; **already lists "v0.5.0: Special Customers Update — 2026.08.??"**. Recent. |
| <https://schedule-1.fandom.com/wiki/Money> · `/Businesses` · `/Properties` · `/Ranks` · `/Suppliers` · `/Quality` · `/Items` · `/Storage_rack` · `/Locker` · `/Console` · `/Player` | 🟡 Deposit/launder caps, business prices, property sizes and loading bays, XP thresholds, dead drops, quality bands, container slots, console commands. Captured to `raw/fandom-*.json`. |
| <https://schedule-1.fandom.com/wiki/Clothing_and_Accessories> · `/Thrifty_Threads` | 🟡 **The best community cross-check for the accessory catalog** — names line up with the verified `Avatar/Accessories/...` paths. Includes known clipping issues. |
| <https://scheduleonewiki.com/wiki/Employees> · `/Dealers` | 🟠 Smaller independent wiki. Corroborates the 20% cut and wage model, but its dealer page **still says 8 customers (pre-v0.4.3) — contradicted by Fandom and the patch notes.** |
| <https://schedulelua.github.io/ScheduleLua-Docs/api/world/game-time.html> | 🟡 Modding-API docs: day indexing 1 = Monday, night 20:00–06:00. |

---

## 11. Decompiled game *logic* (formulas)

| URL | Note |
|---|---|
| <https://github.com/zocke1r/Deal-Optimizer-Mod/blob/b20a63a7/src/IL2CPP/Core.cs> | 🟢 **Calls the game's own methods** — source of the deal-acceptance, appeal, quantity, payment and spending-limit formulas in `DESIGN-INTENT.md` §9.4. Highest trust for *structure*; constants may have shifted since. |
| <https://deepwiki.com/zocke1r/Deal-Optimizer-Mod> | 🟡 Architecture summary of the same mod. |
| <https://github.com/xyrilyn/Deal-Optimizer-Mod/blob/main/README.md> | 🟡 Fork README; confirms max-daily-spend and counteroffer behaviour. |

---

## 12. Press, guides and aggregators

| URL | Note |
|---|---|
| <https://www.pcgamer.com/games/sim/schedule-1-roadmap-future-plans-for-the-drug-dealing-sim-include-a-classic-fishing-minigame-plus-parkour-and-heroin/> | 🟡 **2025-04-01. The most valuable secondary source in the design research** — direct verbatim quotes from the *original* Trello card descriptions for travelling customers, drivers and police, back when they were public. **Now irreplaceable** (the descriptions are gone from the export and unrecoverable from Wayback). 🟠 15 months old. |
| <https://www.pcgamesn.com/schedule-1/roadmap> | 🟠 2025 roadmap card-title list. Titles trustworthy, no dates. |
| <https://mein-mmo.de/en/schedule-1-and-its-roadmap-all-content-you-can-expect-in-the-future,1245998/> | 🟠 Independent 2025 read of the same board; corroborates bribery and the police evidence locker. |
| <https://mein-mmo.de/en/developer-shows-his-work-on-schedule-1-on-twitch-reveals-release-window,1253317/> | 🟠 2025-04-24 dev Twitch stream, **summarised from Reddit — the VOD is deleted.** Second-hand; directional only. |
| <https://gamescout.co.uk/2026/04/schedule-1-special-customers-update/> | 🟡 2026-04. Accurate restatement of the vote #3 result and group list. Recent. |
| <https://beefsuplex.com/news/schedule-one-community-vote-three/> | 🟡 2026-04-26. Vote share (36.9%) and window (Apr 17–24, 2026). Small outlet. |
| <https://consolepcgaming.com/schedule-1s-latest-beta-is-built-for-controller-play/> | 🟡 2026 v0.4.6 beta coverage; independently quotes the "1-2 officers" police line. |
| <https://dotesports.com/indies/news/schedule-1-roadmap> | 🟠 2025 roadmap coverage. |
| <https://steamcommunity.com/sharedfiles/filedetails/?id=3454740900> | 🟡 "The Ultimate Guide For Schedule 1 (UPDATED 2026)". Addiction rates (+20%/sale, −7%/day), the 5-vs-6-deals-to-max finding, top-4-products cap. Player-tested, not datamined. |
| <https://steamcommunity.com/sharedfiles/filedetails/?id=3467994992> | 🟡 "Pricing Guide". Full rank-multiplier ladder (×1.00 → ×3.90). Community-derived. |
| <https://steamcommunity.com/sharedfiles/filedetails/?id=3649586524> | 🟡 Shrooms-era dealer guide. Confirms 20% cut, v0.4.3 10-customer cap, dealer police immunity. |
| <https://techsngames.com/schedule-1-best-dealer-setup/> | 🟡 2026 dealer guide. Moderate trust, SEO-flavoured. |
| <https://www.dexerto.com/gaming/how-to-hire-assign-workers-in-schedule-1-3171655/> | 🔴🟠 **2025 and contradicted** — lists Cleaner sign-on at $1,500 and Handlers at 3 stations, both superseded. Cited only to document the disagreement. |
| <https://selphie1999gaming.com/game-guides/schedule-i/schedule-1-delivery-guide-how-to-order-items/> · <https://www.destructoid.com/how-to-get-a-loading-dock-in-schedule-1/> · <https://scalacube.com/blog/schedule-1/how-to-get-loading-docks-in-schedule-1> | 🟡 Three independent confirmations of the **$200 flat delivery fee** and the loading-bay requirement. |
| <https://gameriv.com/schedule-1-all-businesses-operating-hours/> · <https://primagames.com/tips/operating-hours-of-all-businesses-in-schedule-1> | 🟡 Store opening hours. |
| <https://gameranx.com/features/id/533820/article/schedule-1-how-to-change-clothes/> · <https://selphie1999gaming.com/game-guides/schedule-i/how-to-get-more-clothes-in-schedule-1/> | 🟡 Two independent Thrifty Threads price lists; both agree with the Fandom table. Undated. |
| <https://www.destructoid.com/schedule-1-trello-and-discord-links/> | 🟡 Confirms Trello and Discord are the two official community channels. |
| <https://www.switchbladegaming.com/schedule-i/best-mods/> | 🟡🟠 SEO aggregator, **low-to-medium trust** — but its user-side update advice (check Posts/Bugs after a patch; delete the old DLL rather than overwrite; remove broken mods entirely) matches the official wiki. |

---

## 13. Local, non-URL verification (recorded for provenance)

| Source | What it established |
|---|---|
| `C:\Program Files (x86)\Steam\steamapps\common\Schedule I\MelonLoader\net6\*.dll` (`FileVersionInfo`) | 🟢 MelonLoader **0.7.1**, 0Harmony **2.10.2**, Il2CppInterop **1.5.0**, Mono.Cecil 0.11.6, MonoMod 22.07.31.01, Newtonsoft.Json 13.0.3. |
| `Schedule I.exe` (`FileVersionInfo`) | 🟢 Unity **2022.3.62f2** (`7670c08855a9`). |
| `MelonLoader\Il2CppAssemblies\` listing | 🟢 URP + third-party package inventory → `raw/il2cpp-assemblies-listing.txt`. |
| `Schedule I_Data\globalgamemanagers` | 🟢 Shader table: `Shader Graphs/CombinedAvatar`, URP Lit/Simple Lit/Unlit, Amplify Impostors, EPO outlines → `raw/shaders-globalgamemanagers.txt`. |
| `s1api.forked.{3.1.4,3.1.7}.nupkg`, `s1api.1.6.2.nupkg` | 🟢 Downloaded from api.nuget.org; nuspec + `S1API.xml` (**3,806 members / 760 types**) parsed for the namespace census. |
| Sibling agent interop dumps (`research/raw/ns/ns-Il2CppScheduleOne.*.txt`, `ns-S1API.*.txt`) | 🟢 Full type/member listings for `AvatarFramework`, `Clothing`, `Employees`, `Police` + S1API appearance constants; spot-checked against `Assembly-CSharp.dll`. |
| AssetStudioMod CLI extraction | 🟢 → `raw/sprite-inventory.tsv`, `raw/sprite-9slice.tsv`, `raw/palette-samples.txt`, `raw/dump-sprites/`, `raw/dump-tmpfonts/`, `raw/assetstudio-info-*.txt`. |
| Repo files | 🟢 `CreativeMode.csproj`, `CreativeModeMod.cs`, `src/Directory.Build.props`, `src/Expansions.Core/**` — read directly for the csproj and config review in `MODDING-ECOSYSTEM.md` §1.3 and §7.3. |

---

## 14. Explicitly unavailable / do-not-use

| Item | Why |
|---|---|
| <https://github.com/thecatontheceiling/scheduleone> | 🔴 **Deleted.** Not renamed — absent from that user's 24 public repos. Its DeepWiki snapshot is v0.3.5. |
| <https://github.com/FearAndDelight/Schedule-1-Modder-Documentation> | 🔴 **404.** Only the `github-wiki-see.page` mirror survives; unmaintained. |
| Original Trello card descriptions | 🔴 Stripped/encrypted in the public export and unrecoverable from Wayback (client-rendered SPA). The April 2025 PC Gamer / PCGamesN / mein-mmo quotes are the only surviving record. |
| 2025-04-24 dev Twitch VOD | 🔴 Deleted; only a Reddit summary survives. |
| Official Discord (<https://discord.gg/qKMRFzgSmg>) and the modding Discord (<https://discord.gg/9Z5RKEYSzq>) | 🔴 Not scrapable, not joined. **The single biggest coverage gap** — every Cookbook claim links into the latter. |
| Reddit (r/ScheduleI, r/ScheduleOneGame) | 🔴 Searches surfaced Nexus/Thunderstore/blog aggregators rather than threads. **Documented coverage gap** in the mod survey. |
| Any numeric spec for the three features | 🔴 **None was ever published by the developer.** Every number in `DESIGN-INTENT.md` §14 is derived from shipped values, not quoted. |
| `GrahamKracker/UnityExplorer` (IL2CPP build) | 🟡 Recommended by the (dead) FearAndDelight wiki as a runtime scene inspector + C# REPL. **Release link never verified.** Prefer `sinai-dev/UnityExplorer`. |

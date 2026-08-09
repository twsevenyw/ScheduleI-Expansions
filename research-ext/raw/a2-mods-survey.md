## A2 — Published Schedule I mod survey, multiplayer, and menu-injection patterns

**Scope of this document.** External research only (public web + public git repos) performed 2026-08-03. No local mod source was read; the game was not launched. Every claim is labelled **[verified]** (I read the primary source file / code / API response myself), **[community]** (a mod page, README, wiki, or Discord-sourced claim relayed by a third party), or **[inference]** (my own reasoning from the above).

**Method note / honesty about coverage.** Thunderstore's package API returned a complete, current index and I parsed all of it. Nexus Mods, by contrast, serves login walls and heavily cached snapshots to non-authenticated fetches — the "Top files" page I retrieved was an **April 2025** snapshot, and Nexus' own counters disagreed across snapshots (one stats page reported 535 mods / 2,716 files; the games hub reported ~993; a search synthesis claimed "over 1,150"). I therefore treat Nexus as **incomplete** in this survey and flag it. GitHub was queried via the unauthenticated REST API (repo metadata, license, last-push are all first-hand). For deep dives I cloned 21 repositories and read the actual `.cs` files; every code block below is quoted from a file I opened.

---

## 1. Survey of existing published Schedule I mods

### 1.1 The landscape in one paragraph

Schedule I modding is **MelonLoader-first, not BepInEx-first**. Thunderstore's Schedule I community lists **213 non-deprecated packages (494 including deprecated)** and its category taxonomy is `Audio / IL2CPP / Libraries / Misc / Modpacks / Mods / Mono / Tools` — note that **`IL2CPP` and `Mono` are first-class categories**, because the game ships two backends on two Steam branches and mods are not interchangeable between them **[verified]** (Thunderstore API `https://thunderstore.io/c/schedule-i/api/v1/package/`; category list from `https://thunderstore.io/c/schedule-i/`). BepInEx exists in the ecosystem only as a minority path — the Cookbook mentions a BepInEx port of `Il2CppAssetBundleManager`, and one mod (`Schedule-I-Twitch-Customers`) describes itself as "MelonLoader (formerly BepInEx)" — but **no BepInEx pack ranks anywhere near the top**, and MelonLoader itself is the #2 most-downloaded Thunderstore package for the game at 111,083 downloads **[verified]**.

The single most important structural discovery for our project: **there is a mature, MIT-licensed framework stack that already solves most of what we need**, authored largely by two people — **DooDesch** (`Personnel`, `SideHustle`, `Hotline`, `Sideload`, `Inkorporated`, plus the community `Cookbook`) and **ifBars** (`S1API` fork — the exact library we already depend on — plus `S1MAPI`, `SteamNetworkLib`, `bGUI`). Our three features do not need to be built from raw Harmony patches.

### 1.2 Master table

Ordered roughly by relevance to our three features, then by download count. "dl" = Thunderstore downloads at time of query. Licenses are from the GitHub API (`NONE` = no license file → *all rights reserved by default*; `NOASSERTION` = a license file GitHub could not classify).

| Name | Author | Platform | URL | Loader | Source? | License | Last updated | Relevance to us |
|---|---|---|---|---|---|---|---|---|
| **SideHustle** | DooDesch | GitHub (+TS via consumers) | https://github.com/DooDesch-Mods/ScheduleOne-SideHustle | MelonLoader (IL2CPP only) | Yes | **MIT** | 2026-08-03 | **Main-menu hub framework.** Injects a native-styled button into the title screen and hosts registered "gamemodes". The single best main-menu-injection reference that exists. |
| **Personnel** | DooDesch | https://thunderstore.io/c/schedule-i/p/DooDesch/Personnel/ | GitHub: https://github.com/DooDesch-Mods/ScheduleOne-Personnel | MelonLoader (IL2CPP) | Yes | **MIT** | 2026-08-02 | **Custom-NPC framework.** NPC packs as folders → real networked, save-persisted S1API NPCs with schedules + customer/dealer economy. Directly serves all three of our features. |
| **S1API (ifBars fork)** | ifBars (orig. KaBooMa) | https://thunderstore.io/c/schedule-i/p/ifBars/S1API_Forked/ | GitHub: https://github.com/ifBars/S1API | Both | Yes | **MIT** | 2026-08-03 | Our existing dependency (85K dl, ★22). `NPCPrefabBuilder`, `PhoneApp`, `Saveable`, quests, dialogue. |
| **NACops** | XOWithSauce | https://thunderstore.io/c/schedule-i/p/XO_WithSauce/NACops_IL2CPP/ | GitHub: https://github.com/XOWithSauce/schedule-nacops | MelonLoader (both, `#if MONO`) | Yes | **NONE** ⚠ | 2026-08-03 | **The closest existing thing to our Police Improvements.** Property raids, property heat, disguised private investigator, buy-busts, corrupt/lethal cops, progression-scaled frequency, runtime-cloned officers. 5.9K dl IL2CPP + 3.5K Mono. |
| **Hireable Delivery Driver (IL2CPP port)** | bwyan (orig. notsurewhattoputhere) | https://thunderstore.io/c/schedule-i/p/bwyan/HireableDeliveryDriver_IL2CPP_port/ | **No source** | MelonLoader (IL2CPP) | No | n/a | 2026-01-05 | **The closest existing thing to our Hireable Drivers.** Hire drivers, routes between loading docks, vans spawn/load/travel/unload. Explicitly "host-only management". 2.5K dl. |
| HireableDeliveryDriver (Mono original) | notsurewhattoputhere | https://thunderstore.io/c/schedule-i/p/notsurewhattoputhere/HireableDeliveryDriver/ | No source | MelonLoader (Mono) | No | n/a | 2026-01-04 | Same feature, Mono branch. 3.3K dl, rating 4. |
| **OverTheCounter (OTC)** | hdlmrell | https://thunderstore.io/c/schedule-i/p/hdlmrell/OverTheCounter/ | GitHub: https://github.com/hdlmrell/OTC-S1-Mod | MelonLoader (both) | Yes | **NOASSERTION** ⚠ | 2026-04-14 | **Live customer NPCs walking in and buying**, hireable budtenders, a full custom phone app, buildable storefronts, Steam-P2P MP sync. The most feature-complete gameplay mod with source. 9.6K dl, rating 7. |
| **ModsApp** | k073l | https://thunderstore.io/c/schedule-i/p/k0Mods/ModsApp/ | GitHub: https://github.com/k073l/s1-modsapp | MelonLoader (both, via S1API) | Yes | **MIT** | 2026-08-02 | **The de-facto mod-settings framework.** Phone app that auto-discovers every mod's `MelonPreferences` and JSON configs. 16.9K dl, rating 6. Requires S1API ≥ 3.0.1 (our fork). |
| **Mod Manager & Phone App** | Prowiler | https://www.nexusmods.com/schedule1/mods/397 | No source found | MelonLoader | No | n/a | 2025-04-27 (cached) | The *other* settings-UI standard. `Hotline`'s README names it as the recommended in-game settings UI. Multiple mods say "configurable in-game through the Mod Manager Phone App". |
| **Hotline** | DooDesch | https://thunderstore.io/c/schedule-i/p/DooDesch/Hotline/ | GitHub: https://github.com/DooDesch-Mods/ScheduleOne-Hotline | MelonLoader | Yes | **MIT** | 2026-08-03 | Unified in-game debug/HUD overlay + one master key for all mods. Alternative surface to a main-menu screen. Does **not** use S1API. |
| **HonestMainMenu** | RoachxD | https://thunderstore.io/c/schedule-i/p/Roachified/HonestMainMenu/ | GitHub: https://github.com/RoachxD/ScheduleOne.HonestMainMenu | MelonLoader (both) | Yes | **MIT** | 2026-01-10 | Rewrites the title-screen buttons. Gives us the **hard-coded scene/object names** for the menu hierarchy. 1.1K dl. |
| **Personify** | DooDesch | https://thunderstore.io/c/schedule-i/p/DooDesch/Personify/ | GitHub: https://github.com/DooDesch-Mods/ScheduleOne-Personify | MelonLoader (IL2CPP) | Yes | **MIT** | 2026-08-02 | In-game NPC editor running **as an overlay on the menu scene**, exports Personnel packs. Second main-menu-surface reference. |
| **Inkubator** | DooDesch | https://thunderstore.io/c/schedule-i/p/DooDesch/Inkubator/ | GitHub: https://github.com/DooDesch-Mods/ScheduleOne-Inkubator | MelonLoader (IL2CPP) | Yes | **MIT** | 2026-08-02 | 3D tattoo editor, also a menu-scene overlay launched from Side Hustle. |
| **Cartel Enforcer** | XO_WithSauce | https://thunderstore.io/c/schedule-i/p/XO_WithSauce/Cartel_Enforcer_MONO/ | GitHub: https://github.com/XOWithSauce/schedule-cartelenforcer | Both | Yes | **MIT** | 2026-08-03 | Ambushes, drive-bys, dealer robbery, custom quests via `[RegisterTypeInIl2Cpp]`, runtime `NetworkObject` init. 27K dl. Best MIT-licensed source for hostile-faction events. |
| **LooseEnds** | DooDesch | https://thunderstore.io/c/schedule-i/p/DooDesch/LooseEnds/ | GitHub: https://github.com/DooDesch-Mods/ScheduleOne-LooseEnds | MelonLoader | Yes | **MIT** | 2026-08-02 | Civilians who see a corpse call police; police investigate. Small, clean, MIT source for police-reaction logic. |
| **Law Enforcement Enhancement Mod** | surrealnirvana | https://thunderstore.io/c/schedule-i/p/Surrealnirvana/Enhanced_Law_Enforcement/ | GitHub: https://github.com/surrealnirvana/LawEnforcementEnhancementMod | MelonLoader (IL2CPP) | Yes | **NONE** ⚠ | 2025-04-21 | Officer spawning + patrol behaviour; uses `[RegisterTypeInIl2Cpp]` on a MonoBehaviour and `ServerManager.Spawn`. 5.0K dl. Stale (Apr 2025) — patterns only. |
| HardcorePoliceMod | Babyhamsta (pkg: JD) | https://thunderstore.io/c/schedule-i/p/JD/HardcoreAI/ | GitHub: https://github.com/Babyhamsta/HardcorePoliceMod-Schedule1 | MelonLoader | Yes | **Apache-2.0** | 2025-04-15 | Harder police AI. Uses manual `harmony.Patch(method, postfix:)` rather than attributes. Stale. |
| NoPolice | HazDS | https://thunderstore.io/c/schedule-i/p/HazDS/NoPolice/ | GitHub (releases only): https://github.com/HazDS/S1Mods | Both | Releases only, no `.cs` | **NONE** | 2026-03-20 | Useful because its description **enumerates the police subsystems**: "patrols, pursuits, body searches, checkpoints, and curfew enforcement… each individually configurable". |
| EvenMoreFootPatrols | UncleTyrone | https://thunderstore.io/c/schedule-i/p/UncleTyrone/EvenMoreFootPatrols_IL2CPP/ | Nexus: https://www.nexusmods.com/schedule1/mods/1202 | Both | No | n/a | 2025-09-27 | "Comes pre-loaded with additional routes" → confirms patrol routes are data-driven and injectable. |
| CopHealthModifier | Foxcapades | https://thunderstore.io/c/schedule-i/p/Foxcapades/CopHealthModifierIl2Cpp/ | GitHub: https://github.com/Foxcapades/schedule-1-mods | Both | Yes | (unchecked) | 2026-04-09 | Minimal example of touching officer stats. |
| PickPocketPolice | SadPoty | https://thunderstore.io/c/schedule-i/p/SadPoty/PickPocket_Police/ | GitHub: https://github.com/SadPoty/PickPocketPolice | Both | Yes | (unchecked) | 2025-05-13 | Police interaction flags. |
| Joyrider | Jumble | https://thunderstore.io/c/schedule-i/p/Jumble/Joyrider/ | No source | Both | No | n/a | 2026-03-17 | Carjacking NPC + police vehicles — vehicle-ownership handling. |
| **DealerSelfSupplySystem** | KaikiNoodles / DevKaiE | https://thunderstore.io/c/schedule-i/p/KaikiNoodles/DealerSelfSupplySystem/ | GitHub: https://github.com/DevKaiE/DealerTransportMod | MelonLoader (IL2CPP) | Yes | **NONE** ⚠ | 2025-04-23 | "Dealers track stock, collect items from storage, travel realistically." 16.7K dl. Closest source-available **NPC-drives-logistics** mod. |
| AdvancedDealing | ManZune | https://thunderstore.io/c/schedule-i/p/ManZune/AdvancedDealing/ | GitHub: https://github.com/manzune/AdvancedDealing | Both | Yes | **MIT** | 2026-02-09 | Dealers deliver cash, message via Messages app, negotiate cut. 9.4K dl, rating 6. |
| AdvancedDealingCommunity (fork) | FPZone / UrbanSide | https://thunderstore.io/c/schedule-i/p/FPZone/AdvancedDealingCommunity/ | GitHub: https://github.com/UrbanSide/AdvancedDealing | IL2CPP | Yes | **MIT** | 2026-07-16 | Community-maintained fork "for Schedule I 0.4.5f2" with **save migration** — an update-survival case study. |
| NoLazyWorkers | ArchieN / archenovalis | https://thunderstore.io/c/schedule-i/p/ArchieN/NoLazyWorkers_Il2Cpp/ | GitHub: https://github.com/archenovalis/NoLazyWorkers | Both | Yes | **NONE** ⚠ | 2025-08-06 | Employees fetch supplies; multi-recipe loops. 18.1K+4.5K dl. Employee-task-graph reference. |
| KLINE | ArchieN | https://thunderstore.io/c/schedule-i/p/ArchieN/KLINE_Il2Cpp/ | GitHub: https://github.com/archenovalis/KLINE | Both | Yes | (repo, unchecked) | 2025-04-29 | "Multiple vehicles will deliver it" — multi-van delivery spawning. |
| BusinessEmployment / SewerEmployees / InputDock / MultiDelivery / FurnitureDelivery | k073l | https://thunderstore.io/c/schedule-i/p/k0Mods/ | e.g. https://github.com/k073l/s1-multidelivery | Both | Partly | (per-repo) | 2026-08-02 | Adds employees to Businesses; employees loading vehicles at loading docks; multi-vehicle orders. 12K / 5.6K / 2.2K dl. Very close to Hireable Drivers' plumbing. |
| Smarter_Employees | OmniCorp | https://thunderstore.io/c/schedule-i/p/OmniCorp/Smarter_Employees/ | No source | Both | No | n/a | 2025-06-02 | "Automates employee logistics by moving items around according to filters." |
| ImprovedPackagers / PackagersLoadVehicles | Dre | https://thunderstore.io/c/schedule-i/p/Dre/ImprovedPackagers/ | GitHub: https://github.com/GuysWeForgotDre/Improved-Packagers | Both | Yes | (unchecked) | 2025-08-11 | "load vehicles in Loading Bays" — the exact vanilla subsystem our drivers must drive. |
| CrossProperty_Transportation | NanobotZ | https://thunderstore.io/c/schedule-i/p/NanobotZ/CrossProperty_Transportation_IL2CPP/ | No source | Both | No | n/a | 2025-04-25 | Station output → storage in *another* property. Inter-property routing precedent. |
| VehicleDeliverySlots | Daudr | https://thunderstore.io/c/schedule-i/p/Daudr/VehicleDeliverySlots/ | No source | IL2CPP | No | n/a | 2026-07-09 | Configurable delivery-vehicle trunk slots / adaptive trunk grids. |
| **GophxrMod** | HazDS | https://thunderstore.io/c/schedule-i/p/HazDS/GophxrMod/ | GitHub (releases): https://github.com/HazDS/S1Mods | Both | Releases only | **NONE** | 2026-03-19 | "adds Gophxr as a **custom NPC with a full daily schedule, dialogue, and customer behavior**, plus his branded cap and t-shirt available at Thrifty Threads." Proof a single custom NPC + custom clothing ships and works. |
| RenameNPCs | HazDS | https://thunderstore.io/c/schedule-i/p/HazDS/RenameNPCs/ | (releases) | Both | No `.cs` | **NONE** | 2026-03-07 | "names persist across sessions and appear everywhere: name tags, dialogue, phone contacts, and map markers" → enumerates every NPC-name surface. |
| BetterFiends | EndureBlackout | https://thunderstore.io/c/schedule-i/p/EndureBlackout/BetterFiends/ | GitHub: https://github.com/EndureBlackout/so-better-fiends | Both | Yes | (unchecked) | 2025-04-26 | Customers become fiends, demand product, "attack or even call the cops" — customer→police bridge. |
| FeeningNPCs / CashDrops / SnitchSamples | XOWithSauce | https://thunderstore.io/c/schedule-i/p/XO_WithSauce/FeeningNPCs_IL2CPP/ | https://github.com/XOWithSauce/schedule-feeningnpcs | Both | Yes | (unchecked) | 2025-08-17 | Customer-behaviour modification precedents. |
| MoreDeals | GreenCarrot | https://thunderstore.io/c/schedule-i/p/GreenCarrot/MoreDeals/ | No real source | Both | No | n/a | 2025-04-21 | "customers will request from you multiple times per day" — closest existing analogue to bulk-buying special customers. |
| Joive_Customer_Relations | Joive | https://thunderstore.io/c/schedule-i/p/Joive/Joive_Customer_Relations/ | GitHub: https://github.com/Joive/Schedule-I-Customer-Relations | MelonLoader | Yes | (unchecked) | 2026-05-04 | Customer/NPC relationship manipulation via in-game menu. |
| TwitchCustomers | ReservedKeyword | https://thunderstore.io/c/schedule-i/p/JD/TwitchCustomers/ | https://github.com/ReservedKeyword/Schedule-I-Twitch-Customers | MelonLoader (was BepInEx) | Yes | **MIT** | 2025-04-06 | Injects externally-sourced customers — customer-generation precedent. |
| Lithium (+ EVB fork) | DerTomDer / EVB | https://thunderstore.io/c/schedule-i/p/DerTomDer/Lithium/ | Nexus: https://www.nexusmods.com/schedule1/mods/1138 | Both | No | n/a | 2026-05-12 | **"Modular balancing framework… Each feature is optional and fully toggleable."** The design pattern our toggle screen has to beat. 5.3K dl. |
| **SaveExpansion** | SirTidez | https://thunderstore.io/c/schedule-i/p/SirTidez/SaveExpansion/ | https://github.com/SirTidez (repo not located) | Both | Not found | n/a | 2026-08-01 | "expands Schedule I's **native save menu** from 5 to up to 20 slots" — a second main-menu-surface data point. |
| Disclaimer_Skip | SanicDev | https://thunderstore.io/c/schedule-i/p/SanicDev/Disclaimer_Skip_IL2CPP/ | No source | Both | No | n/a | 2025-04-19 | "skips the disclaimer displayed when you hit the menu" — the menu scene has a pre-menu disclaimer step to be aware of. |
| PropHunt | DooDesch | https://thunderstore.io/c/schedule-i/p/DooDesch/PropHunt/ | https://github.com/DooDesch-Mods/ScheduleOne-PropHunt | MelonLoader (IL2CPP) | Yes | **MIT** | 2026-08-03 | Full MP gamemode "**Hosted from the Side Hustle menu**". Best example of a mod that owns a lobby + syncs state on IL2CPP. |
| Sideload | DooDesch | https://thunderstore.io/c/schedule-i/p/DooDesch/Sideload/ | https://github.com/DooDesch-Mods/ScheduleOne-Sideload | MelonLoader (IL2CPP) | Yes | **MIT** | 2026-08-02 | "Write a mod interface as HTML/CSS/JS… renders into real Unity UI and puts it on the in-game phone." Radical alternative UI path. |
| Snitch | DooDesch | https://thunderstore.io/c/schedule-i/p/DooDesch/Snitch/ | https://github.com/DooDesch-Mods/ScheduleOne-Snitch | MelonLoader (IL2CPP) | Yes | **MIT** | 2026-08-02 | Perf profiler for **NPCs, trash, quests and your own mods** — directly useful when we add dozens of custom NPCs. |
| Siesta | DooDesch | https://thunderstore.io/c/schedule-i/p/DooDesch/Siesta/ | https://github.com/DooDesch/ScheduleOne-Siesta | MelonLoader | Yes | **MIT** | 2026-08-02 | NPC distance/visibility LOD — the mitigation if our NPCs cost FPS. |
| S1MAPI | ifBars | https://thunderstore.io/c/schedule-i/p/ifBars/S1MAPI/ | https://github.com/ifBars/S1MAPI | Both | Yes | **GPL-3.0** ⚠ | 2026-07 | Procedural meshes + glTF loading "without depending on game assemblies". **GPL-3.0 — do not link into our MIT-ish mods casually.** |
| SteamNetworkLib | ifBars | https://thunderstore.io/c/schedule-i/p/ifBars/SteamNetworkLib_Mono/ | https://github.com/ifBars/SteamNetworkLib | Both | Yes | **MIT** | 2026-07-28 | Object-oriented Steamworks lobby/P2P wrapper — **the recommended IL2CPP multiplayer path**. 23K dl. |
| bGUI | ifBars | (via Cookbook) | https://github.com/ifBars/bGUI | Both | Yes | **MIT** | 2026-06-26 | Builder-styled uGUI for runtime interfaces — relevant to building our screen without AssetBundles. |
| Il2CppAssetBundleManager | LavaGang | (bundled in MelonLoader) | https://github.com/LavaGang/UnityEngine.Il2CppAssetBundleManager | MelonLoader IL2CPP | Yes | (repo) | — | **Required** on IL2CPP; `UnityEngine.AssetBundle.LoadFromMemory` is stripped from this build. |
| MLVScan | ifBars | https://thunderstore.io/c/schedule-i/p/ifBars/MLVScan/ | (site links to attestations) | Both | — | — | 2026-06 | Security scanner plugin that disables suspicious mods. 20K dl, rating 9. **Our DLLs will be scanned by users' installs.** |
| OTCLoader | hdlmrell | https://thunderstore.io/c/schedule-i/p/hdlmrell/OTCLoader/ | https://github.com/hdlmrell/OTC-Loader | Both | Yes | (unchecked) | 2026-03-31 | "auto-detects your game branch (IL2CPP or Mono) and **disables incompatible mod DLLs before they crash**". 18K dl. |
| SwapperPlugin | the_croods | https://thunderstore.io/c/schedule-i/p/the_croods/SwapperPlugin/ | No source | Both | No | n/a | 2025-05-20 | "Allows seamless swapping between the il2cpp and mono backends." 80.9K dl — shows how much branch pain exists. |
| S1MelonModTemplate | k073l | — | https://github.com/k073l/S1MelonModTemplate | Both | Yes | **MIT** | 2026-04-11 | Dual IL2CPP/Mono MelonLoader project template. ★3. |
| CodeArchiver | k073l | — | https://github.com/k073l/s1-codearchiver | tool | Yes | **NONE** | 2026-08-01 | Automated cross-version stripped-code diffing. ★8. **The update-survival tool.** |
| RefGen | k073l | — | https://github.com/k073l/RefGen | tool | Yes | **MIT** | 2026-03-31 | Builds reference-assembly NuGet packages per game version. |
| Mod Manager (in-game) | LethalLizard | https://www.nexusmods.com/schedule1/mods/58 | No source | MelonLoader | No | n/a | 2025-04-02 (cached) | Enable/disable mods in-game. |
| Schedule I Mod Manager (SIMM) | (Nexus) | https://www.nexusmods.com/schedule1/mods/1750 | No source | external | No | n/a | — | Branch-aware external mod manager, recommended by ModsApp's README. |
| r2modman / Gale | ebkr / Kesomannen | https://thunderstore.io/c/schedule-i/p/ebkr/r2modman/ · https://thunderstore.io/c/schedule-i/p/Kesomannen/GaleModManager/ | GitHub | tools | Yes | (repo) | 2026-06/07 | The two dominant TS managers (158K / rating 1352 and rating 206). Our Thunderstore manifest must declare dependencies correctly for these. |
| ScheduleLua | ScheduleLua team (ifBars) | https://thunderstore.io/c/schedule-i/p/ScheduleLua/ScheduleLua/ | https://github.com/ScheduleLua/ScheduleLua-Framework | Mono (IL2CPP unverified) | Yes | **GPL-3.0** ⚠ | 2025-10-07 | Lua scripting framework. Mono-leaning; not useful to us. |
| SmallCornerMap / Skoofidon's Minimap | CherryMods / youseemenot | https://thunderstore.io/c/schedule-i/p/CherryMods/SmallCornerMap/ | https://github.com/JCherryhomes/Schedule-1-Small-Corner-Map · https://github.com/youseemenot/schedule1-Skoofidons-Minimap | Both | Yes | (unchecked) | 2025-12 / 2026-07 | The latter does "real-time tracking of NPCs, **police**, and co-op players" — useful for reading police/NPC registries. |
| FullHouse / BiggerLobbies / MultiplayerPlus / MultiplayerEnhanced / BiggerCrew | DooDesch / ifBars / MedicalMess / NyxisStudio / jasonlearst | https://thunderstore.io/c/schedule-i/p/ifBars/BiggerLobbies/ etc. | https://github.com/DooDesch-Mods/ScheduleOne-FullHouse · https://github.com/ifBars/BiggerLobbies · https://github.com/Nyxis-Studio/SO_MultiplayerEnhanced · https://github.com/jasonlearst/Schedule1-BiggerCrew | Both | Mostly yes | MIT / GPL-3.0 | 2026-08-03 (FullHouse) | Lobby-cap engines. Confirms the vanilla cap is **4 players** and that MP mods require all-players-install. |
| S1DS (dedicated server + addon API) | ifBars, ZackaryH8 | — | https://github.com/ifBars/S1DS-SCOPE · https://github.com/ZackaryH8/S1DS-TextChat · https://github.com/ifBars/S1DS-PlayerList | server/client addons | Yes | **NONE** | 2026-06/07 | A **dedicated-server** ecosystem exists with a paired client/server addon API. Relevant if we ever want authoritative server logic. |
| Narcopelago | Papacester | https://thunderstore.io/c/schedule-i/p/Narcopelago/Narcopelago/ | https://github.com/Papacester/nightmare-monkey | Both | Yes | (unchecked) | 2026-08-02 | "Randomize Dealers, Customers, Cartel Influence" — deep economy hooks. |
| Section97 | Assailent | https://thunderstore.io/c/schedule-i/p/Assailent/Section97/ | https://github.com/AssailentDev/Section97 | Both | Yes | (unchecked) | 2025-04-16 | Store robbery / mugging → crime + police response. 13.1K dl. |
| SewerMenu / NugzzMenu / TeleportMenu / Item_Giver | zampx / firebirdjsb / MrTibbz / Joive | https://thunderstore.io/c/schedule-i/p/zampx/SewerMenu/ | https://github.com/zampxdev/SewerMenu · https://github.com/firebirdjsb/NugzzMenu | IL2CPP | Yes | (unchecked) | 2026-01 / 2026-07 | Cheat-menu peers to our own Creative Mode. NugzzMenu is "IL2CPP mod menu … using S1API". |
| Cookbook (community wiki) | DooDesch | https://doodesch-mods.github.io/ScheduleOne-Cookbook/ (repo) | https://github.com/DooDesch-Mods/ScheduleOne-Cookbook | docs | Yes | **NOASSERTION** | 2026-08-01 | **Curated FishNet/IL2CPP/AssetBundle/preferences knowledge base, sourced from the modding Discord.** Highest-value single document in the ecosystem. |
| Schedule I Modding Wiki | s1modding | https://s1modding.github.io/docs/ | — | docs | — | — | live | The other official-ish docs site (`/docs/moddevs/`, `/docs/modusers/troubleshooting/`). |

⚠ = **no license file → assume all rights reserved. Learn from, do not copy.**

**Not found / negative results.** I could not find: (a) any mod that adds *federal agents* specifically — the nearest is NACops' "investigators" and raid officers **[verified]**; (b) any mod implementing "special customer groups (bikers/hippies/businessmen) visiting town" — nothing in 494 Thunderstore packages or the GitHub searches matches; (c) source for `HireableDeliveryDriver` (neither the Mono original nor the IL2CPP port links a repository, and neither author appears in the GitHub searches) **[verified]**; (d) an r/ScheduleI or r/ScheduleOneGame "recommended mods" thread — my searches surfaced Nexus/Thunderstore/blog aggregators (e.g. `switchbladegaming.com/schedule-i/best-mods/`) rather than reddit threads, so reddit is a **coverage gap** in this report; (e) any BepInEx-based Schedule I mod of consequence.

---

### 1.3 Deep dive — SideHustle (main-menu hub) · **MIT · safe to copy**

Repo: https://github.com/DooDesch-Mods/ScheduleOne-SideHustle — MIT, last push 2026-08-03 **[verified]**.

**File/class layout** (files I listed):

```
Core.cs  UIConstants-equivalents live in Menu/*
Api/          API.cs  GamemodeDescriptor.cs  GamemodeTypes.cs  LaunchContext.cs
              ModPolicy.cs  Registry.cs  SettingDescriptor.cs
Boot/         Plugin.cs  (a separate MelonLoader *plugin* assembly, SideHustle.Boot.csproj)
Config/       Preferences.cs
Menu/         MenuInjector.cs  Hub.cs (54KB)  HubVanilla.cs  HubProfiles.cs
              HostConfigView.cs  JoinBrowserView.cs  ContinueInterstitial.cs
              DownloadLink.cs  InstallProgressView.cs  PackageBrowserView.cs
              ProfilesViews.cs  SyncConsentView.cs  SyncManualInstallView.cs  VanillaHostView.cs
Mods/         AltBase.cs  ModInventory.cs  ModPolicyResolver.cs  ModSwitcher.cs
Multiplayer/  LobbyCoordinator.cs  MultiplayerCoordinator.cs  ServerBrowser.cs  WorldBoot.cs
              ClientExitGuard.cs  GamemodeHygiene.cs  HostControls.cs  LobbyCaps.cs
              NetworkTuning.cs  PlayerAlias.cs  PublicLobbyAccess.cs  ConfigCodec.cs
Profiles/, Shared/, Sync/  (Thunderstore client, profile engine, hash-verified installs)
```

**Harmony patch targets: none for the menu.** This is the most important finding. SideHustle deliberately does **not** patch the menu:

```csharp
// ScheduleOne-SideHustle/Menu/MenuInjector.cs:12-19
/// Injects the "Side Hustle" entry into the main-menu home screen. We do not Harmony-patch MenuScreen.Awake
/// (it is the base of many screens); instead we run from the MelonMod scene lifecycle once the "Menu" scene is up,
/// find the home screen (the MenuScreen with OpenOnStart), clone one of its nav buttons for styling and rewire
/// its click to open the hub panel. The first frame after a scene load may not have the UI laid out yet, so Core
/// retries us for a short window.
```

**Finding the home screen without hard-coded paths** — resilient across updates:

```csharp
// ScheduleOne-SideHustle/Menu/MenuInjector.cs:134-150
private static MenuScreen FindHomeScreen()
{
    Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<MenuScreen> screens =
        UnityEngine.Object.FindObjectsOfType<MenuScreen>(true);
    if (screens == null || screens.Length == 0) return null;

    MenuScreen openOnStart = null;
    for (int i = 0; i < screens.Length; i++)
    {
        MenuScreen s = screens[i];
        if (s == null) continue;
        if (s.OpenOnStart) { openOnStart = s; break; }
    }
    if (openOnStart == null)
        Core.Log?.Warning($"[menu] no MenuScreen with OpenOnStart among {screens.Length}; falling back to first.");
    return openOnStart ?? screens[0];
}
```

Game symbols used: `Il2CppScheduleOne.UI.MainMenu.MenuScreen`, and its `OpenOnStart` property **[verified]**.

**The 20-frame warm-up — a real crash the community hit:**

```csharp
// ScheduleOne-SideHustle/Menu/MenuInjector.cs:36-39
// Let the menu's own UIScreen/UISelectable navigation finish initializing before we clone + reparent a button
// into it. Touching the nav while the game is still iterating its selectables can corrupt it and hard-crash
// (more likely when another heavy mod loads alongside us and shifts the timing).
private const int WarmupFrames = 20;
```

**Cloning a nav button so it looks native, and choosing the safest template:**

```csharp
// ScheduleOne-SideHustle/Menu/MenuInjector.cs:45-47
// Nav-button labels we prefer to clone (Settings has the most side-effect-free click).
private static readonly string[] PreferredLabels =
    { "settings", "options", "load", "continue", "new game", "quit", "exit" };
```

```csharp
// ScheduleOne-SideHustle/Menu/MenuInjector.cs:187-205
private static GameObject CloneNavButton(Button template, string name, string label, UnityAction onClick, int siblingIndex)
{
    Transform parent = template.transform.parent;
    GameObject clone = UnityEngine.Object.Instantiate(template.gameObject, parent, false).Cast<GameObject>();
    clone.transform.localScale = Vector3.one;
    clone.name = name;
    clone.transform.SetSiblingIndex(siblingIndex);

    SetLabel(clone, label);

    Button btn = clone.GetComponent<Button>();
    if (btn == null) { UnityEngine.Object.Destroy(clone); return null; }
    NeutralizeClick(btn);
    btn.onClick.AddListener(onClick);
    btn.interactable = true;

    if (!clone.activeSelf) clone.SetActive(true);
    return clone;
}
```

**The non-obvious gotcha that will bite us** — `RemoveAllListeners()` alone is not enough, because Unity *persistent* (inspector-serialised) listeners survive it:

```csharp
// ScheduleOne-SideHustle/Menu/MenuInjector.cs:335-345
private static void NeutralizeClick(Button btn)
{
    try
    {
        btn.onClick.RemoveAllListeners();
        int n = btn.onClick.GetPersistentEventCount();
        for (int i = 0; i < n; i++)
            btn.onClick.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.Off);
    }
    catch (Exception e) { Core.Log?.Warning("[menu] neutralize click failed: " + e.Message); }
}
```

**Label handling covers both TMP and legacy uGUI Text, and forces single-line overflow:**

```csharp
// ScheduleOne-SideHustle/Menu/MenuInjector.cs:358-371
private static void SetLabel(GameObject go, string label)
{
    var tmp = go.GetComponentInChildren<Il2CppTMPro.TextMeshProUGUI>(true);
    if (tmp != null)
    {
        tmp.text = label;
        // Our labels ("Mod Profiles", "Restore my mods") can be wider than the cloned nav button; keep them on
        // one line (extend, not wrap) so a longer entry does not break onto a second row like the vanilla ones.
        try { tmp.enableWordWrapping = false; tmp.overflowMode = Il2CppTMPro.TextOverflowModes.Overflow; } catch { }
        return;
    }
    var txt = go.GetComponentInChildren<Text>(true);
    if (txt != null) { txt.text = label; try { txt.horizontalOverflow = HorizontalWrapMode.Overflow; } catch { } }
}
```

**Idempotency for scene re-init** — the "Menu" scene can initialise more than once per menu load:

```csharp
// ScheduleOne-SideHustle/Menu/MenuInjector.cs:79-81
// Idempotency guard: the "Menu" scene can re-initialise during a single menu load (the game fires
// OnSceneWasInitialized -> Reset more than once), so injection may run again after our buttons
// already exist. Adopt what is there and only add what is missing - never duplicate an entry.
```

**Config persistence** — `MelonPreferences`, with a category id deliberately prefixed for settings-UI auto-detection (see §3.1):

```
Settings live in `UserData/MelonPreferences.cfg` under `SideHustle_01_Main`.
| `Enabled` | `true` | Show the Side Hustle menu entry. Off hides it without uninstalling (return to the main menu to apply). |
```
— `ScheduleOne-SideHustle/README.md:119-125` **[verified]**

**Assets:** SideHustle loads no AssetBundle for its UI at all. It clones live game GameObjects and reads embedded resources only for its boot plugin (`Profiles/BootInstaller.cs:66  using var s = typeof(BootInstaller).Assembly.GetManifestResourceStream(ResourceName);`) **[verified]**. **[inference]** This is the strongest argument for building our toggle screen the same way: no bundle, no shipped fonts, no version-locked sprites.

**Public API we can register against** (`Api/API.cs`, `Api/GamemodeTypes.cs`, `Api/SettingDescriptor.cs`) **[verified]**:

```csharp
[assembly: MelonOptionalDependencies("SideHustle")]

SideHustle.API.Register(new GamemodeDescriptor
{
    Id = "you.yourmode",                      // stable, unique
    DisplayName = "Your Mode",
    Description = "What your gamemode does.",
    Author = "You",
    Support = GamemodeSupport.Singleplayer,   // or Multiplayer / Hybrid
    Surface = GamemodeSurface.MenuSpace,      // overlay on the menu (no save), or World
    OnLaunchSingleplayer = ctx => { /* start your gamemode */ },
    OnExitToHub = ctx => { /* clean up when the player backs out */ }
});
```
— `README.md:133-147`. `GamemodeSurface.MenuSpace` = "Runs as an overlay in the menu scene; no save is loaded"; `World` = "Side Hustle boots a throwaway save first" (`Api/GamemodeTypes.cs:22-28`).

`SettingDescriptor` gives `SettingType { Slider, Toggle, Segmented, Text, Dropdown }` with `Key/Label/Hint/Category/Default/Unit/Min/Max/Step/WholeNumbers/Options/Values`, plus `SettingPreset` bundles. **[verified]** `Api/SettingDescriptor.cs`. **[inference]** This is a *host-config* form for gamemodes, not a general mod-settings surface — it is the wrong tool for "toggle my three mods", but the exactly right shape to copy for our own screen.

---

### 1.4 Deep dive — HonestMainMenu (the hard-coded menu hierarchy) · **MIT · safe to copy**

Repo: https://github.com/RoachxD/ScheduleOne.HonestMainMenu — MIT, last push 2026-01-10 **[verified]**.

Files: `Main.cs`, `UIConstants.cs`, `UIHelper.cs`, `Models/{MenuButtons,SerializableClothingFile,SerializableClothingSaveData,SerializableColor,SerializableColorData}.cs`, `Patches/{MainMenuRigLoadStuffPatch,SceneManagerLoadScenePatch}.cs`, `Services/{BackButtonPromptSetup,ClothingApplicator,ClothingDataService,ClothingDefinitionResolver,ClothingUtilitySeeder,ColorDataRepository,ContinueScreenSetup,MenuReactivity,MenuSetup}.cs`, `Resources/{ColorData.json,Icon.png}` **[verified]**.

**The scene and object names, quoted verbatim — this is the gold:**

```csharp
// ScheduleOne.HonestMainMenu/UIConstants.cs
public static class UIConstants
{
    public const string MenuSceneName = "Menu";
    public const string MainMenuObjectName = "MainMenu";
    public const string MenuButtonsParentPath = "Home/Bank";
    public const string ContinueObjectNameAndLabel = "Continue";
    public const string LoadGameObjectName = "LoadGame";
    public const string InputPromptObjectName = "InputPrompt (Back)";
    public const string LoadGameLabel = "Load Game";
    public const string RmbPromptBindingKey = "rightButton";
    public const string TitleObjectName = "Title";
}
```

So the hierarchy is: scene **`Menu`** → root GameObject **`MainMenu`** → nav buttons under **`MainMenu/Home/Bank`** → screens are *siblings* under `MainMenu` (`MainMenu/Continue` is the screen panel, `MainMenu/Home/Bank/Continue` is the button), and each screen has a **`Title`** child carrying a TMP label. There is also a **`MainMenu/InputPrompt (Back)`** object with a `ScheduleOne.UI.Input.InputPrompt` component **[verified]**.

**Scene lifecycle, not a menu patch:**

```csharp
// ScheduleOne.HonestMainMenu/Main.cs:45-55, 80-98
public override void OnSceneWasLoaded(int buildIndex, string sceneName)
{
    if (!sceneName.Equals(UIConstants.MenuSceneName, System.StringComparison.OrdinalIgnoreCase))
    { HandleNonMenuScene(sceneName); return; }
    MainMenuRigLoadStuffPatch.ResetLoadedRigs();
    HandleMenuScene();
}
...
GameObject menuRootObject = GameObject.Find(UIConstants.MainMenuObjectName);
...
var menuRoot = menuRootObject.transform;
BackButtonPromptSetup.Apply(menuRoot);
var menuButtons = MenuSetup.Build(menuRoot);
```

**Cloning + repositioning a button and pushing siblings down:**

```csharp
// ScheduleOne.HonestMainMenu/Services/MenuSetup.cs:77-95
private static Button CreateAndConfigureNewContinueButton(GameObject buttonObject)
{
    GameObject newContinueButtonObject = Object.Instantiate(
        buttonObject,
        buttonObject.transform.parent
    );
    newContinueButtonObject.name = UIConstants.ContinueObjectNameAndLabel;
    newContinueButtonObject.SetText(UIConstants.ContinueObjectNameAndLabel);

    var newContinueButton = newContinueButtonObject.GetComponent<Button>();
    if (newContinueButton == null) { Object.Destroy(newContinueButtonObject); return null; }

    newContinueButton.onClick = new Button.ButtonClickedEvent(); // Clear any cloned listeners
    return newContinueButton;
}
```

```csharp
// ScheduleOne.HonestMainMenu/Services/MenuSetup.cs:164-177
private static void OffsetRemainingSiblings(Transform parentTransform, int startIndex, float offset)
{
    for (int i = startIndex; i < parentTransform.childCount; i++)
    {
        Transform sibling = parentTransform.GetChild(i);
        if (sibling.GetComponent<RectTransform>() is RectTransform siblingRect)
            siblingRect.anchoredPosition = new Vector2(
                siblingRect.anchoredPosition.x,
                siblingRect.anchoredPosition.y - offset);
    }
}
```

**Setting text through TMP** (this is the whole "look native" trick — reuse the game's own `TextMeshProUGUI`, never ship a font):

```csharp
// ScheduleOne.HonestMainMenu/UIHelper.cs:13-23
public static void SetText(this GameObject gameObject, string text)
{
    if (gameObject == null) return;
    TextMeshProUGUI tmpText = gameObject.GetComponentInChildren<TextMeshProUGUI>(true);
    if (tmpText == null) return;
    tmpText.text = text;
}
```
Note `using Il2CppTMPro;` under `IL2CPP_BUILD` vs `using TMPro;` under `MONO_BUILD` (`UIHelper.cs:3-7`).

**Harmony patch targets** (the only two, and neither is required for button injection):

```csharp
[HarmonyPatch(typeof(MainMenuRig), "LoadStuff")]                              // Patches/MainMenuRigLoadStuffPatch.cs:21
[HarmonyPatch(typeof(Il2CppScheduleOne.Clothing.ClothingUtility), "Awake")]   // Patches/MainMenuRigLoadStuffPatch.cs:137
[HarmonyPatch(typeof(SceneManager), nameof(SceneManager.LoadScene), new[] { typeof(string) })] // Patches/SceneManagerLoadScenePatch.cs:7
```

**Other game symbols confirmed here** (all `Il2CppScheduleOne.*` on IL2CPP) **[verified]**:
`UI.MainMenu.MainMenuRig` (`.Avatar`, `.CashPiles[i].SetDisplayedAmount(float)`), `UI.MainMenu.MainMenuPopup.Instance.Open(title, message, bool)`, `Persistence.LoadManager` (`.Instance`, `.LastPlayedGame` → `.SavePath`/`.Networth`, `.onSaveInfoLoaded` UnityEvent, `.StartGame(SaveInfo, bool, bool)`), `Networking.Lobby.Instance.IsHost`, `AvatarFramework.Customization.BasicAvatarSettings` (+ `JsonUtility.FromJsonOverwrite`), `UI.Input.InputPrompt.Actions`. Scene paths take the form `Assets/Scenes/{sceneName}.unity` (`SceneManagerLoadScenePatch.cs:24`).

**Version-drift defence worth stealing** — it catches a signature change at runtime and falls back:

```csharp
// ScheduleOne.HonestMainMenu/Services/MenuReactivity.cs:100-116
LoadManager.Instance.StartGame(LoadManager.LastPlayedGame, false, true);
...
catch (MissingMethodException mmEx) when (
    mmEx.Message.Contains("StartGame") ||
    (mmEx.StackTrace?.Contains("LoadManager.StartGame") ?? false))
{
    try { LoadManager.Instance.StartGame(LoadManager.LastPlayedGame, false); ... }
```

---

### 1.5 Deep dive — Personnel (custom NPC framework) · **MIT · safe to copy**

Repo: https://github.com/DooDesch-Mods/ScheduleOne-Personnel — MIT, last push 2026-08-02, v2.1.1 **[verified]**.

Files: `Core.cs`, `API.cs`, `Personnel.csproj`, `Appearance/AvatarSettingsFactory.cs`, `Config/Preferences.cs`, `Content/{ExamplePack,NpcPackManifest,PackLoader}.cs`, `Model/{NpcDef,NpcRoleData}.cs`, `Registration/{CustomLayerRegistry,NpcRegistry}.cs`, `Spawn/{DynamicNpcTypeFactory,PersonnelNpc,ScheduleSpecFactory}.cs`, `Tools/PersonnelConsole.cs`, `Util/{ColorParse,Ids,Parse}.cs` **[verified]**.

**Entry point + the exact MelonGame attribute values:**

```csharp
// ScheduleOne-Personnel/Core.cs:6-7
[assembly: MelonInfo(typeof(Personnel.Core), "Personnel", "2.1.1", "DooDesch", "https://github.com/DooDesch-Mods/ScheduleOne-Personnel")]
[assembly: MelonGame("TVGS", "Schedule I")]
```

**Custom NPCs are S1API NPCs, not hand-rolled prefabs.** `PersonnelNpc : S1API.Entities.NPC` and everything flows through `ConfigurePrefab`:

```csharp
// ScheduleOne-Personnel/Spawn/PersonnelNpc.cs:81-134 (abridged)
protected override void ConfigurePrefab(NPCPrefabBuilder builder)
{
    if (API.TryGet(DefId, out NpcDef def) && def != null) API.ConfigureFromDef(builder, def);
    else Core.Log?.Warning($"PersonnelNpc: no definition '{DefId}' found - is the pack installed?");
}

protected override void OnCreated()
{
    base.OnCreated();
    ...
    // AvatarSettings (applied in ConfigurePrefab -> WithAppearanceDefaults) can't express bone
    // distortion, so it's applied here as a separate pass once a real Avatar exists.
    var avatar = gameObject.GetComponentInChildren<Avatar>(true);
    if (avatar != null) API.ApplyDistortion(avatar, def);

    if (!string.IsNullOrWhiteSpace(def.Spawn?.Region))
        if (Parse.TryParseEnum(def.Spawn.Region, out S1API.Map.Region region)) Region = region;

    if (def.Behavior != null) { Aggressiveness = def.Behavior.Aggression; MaxHealth = def.Behavior.MaxHealth;
                                if (def.Behavior.Scale > 0f) Scale = def.Behavior.Scale; }

    if (def.Schedule != null && def.Schedule.Count > 0) Schedule.Enable();

    if (def.Contact?.MapMarker != false) API.AddMapMarker(gameObject);
}
```

**Two brutal S1API contract details that will save us hours** (quoted comments):

```csharp
// ScheduleOne-Personnel/Spawn/PersonnelNpc.cs:29-31
/// <see cref="DefId"/> must return a compile-time constant: S1API calls <see cref="ConfigurePrefab"/>,
/// <see cref="IsDealer"/> and <see cref="IsPhysical"/> on an UNINITIALIZED instance, so none of them may
/// depend on constructor-set fields.
```

```csharp
// ScheduleOne-Personnel/Spawn/DynamicNpcTypeFactory.cs:16-22
/// - S1API derives the prefab name from the SIMPLE type name ("S1API_" + Type.Name) and reconstructs
///   client wrappers by matching simple names across all assemblies - names must be deterministic,
///   session-stable and globally unique.
/// - The assembly name must not match S1API's discovery skip-list (System/Unity/Il2Cpp/Mono./__Generated/...).
/// - Emission must happen before the main scene loads (S1API scans at scene init); Personnel emits
///   during OnInitializeMelon.
```

**And the multiplayer-critical ordering constraint:**

```csharp
// ScheduleOne-Personnel/Spawn/DynamicNpcTypeFactory.cs:49-51
// Deterministic emission order across machines/sessions: FishNet spawnable registration is
// order-sensitive and co-op peers must agree.
wanted.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
```

**The S1API `NPCPrefabBuilder` surface** (read from `ifBars/S1API`, `S1API/Entities/NPCPrefabBuilder.cs`) **[verified]** — this is what our three mods should build on rather than cloning prefabs by hand:

`WithIdentity(id, firstName, lastName)`, `WithIcon(Sprite)`, `WithAppearanceDefaults(Action<AvatarDefaultsBuilder>)`, `WithSchedule(...)` (builder / `IEnumerable<IScheduleActionSpec>` / varargs), `WithVoice(...)`, `WithRegion(Region)`, `WithSpawnPosition(Vector3[, Quaternion])`, `WithRelationshipDefaults(...)`, `WithInventoryDefaults(...)`, `EnsureCustomer()` + `WithCustomerDefaults(...)`, `EnsureDealer()` + `WithDealerDefaults(...)`, `EnsureSupplier()` + `WithSupplierDefaults(...)`, `EnsureSmokeBreak()`, `EnsureDrinking()`, `EnsureItemHolding()`, `EnsureGraffiti()`. `AvatarDefaultsBuilder` adds `WithFaceLayer/WithBodyLayer/WithAccessoryLayer<T>(path, Color)` and `WithImpostor/WithImpostorTexture/WithRandomImpostor(...)`.

**Customer knobs — directly usable for Special Customers** (`ScheduleOne-Personnel/API.cs:158-183`):

```csharp
builder.WithCustomerDefaults(c =>
{
    if (cu.Spending != null) c.WithSpending(cu.Spending.Min, cu.Spending.Max);
    if (cu.OrdersPerWeek != null) c.WithOrdersPerWeek((int)cu.OrdersPerWeek.Min, (int)cu.OrdersPerWeek.Max);
    if (!string.IsNullOrWhiteSpace(cu.PreferredOrderDay)) c.WithPreferredOrderDay(cu.PreferredOrderDay);
    if (cu.OrderTime.HasValue) c.WithOrderTime(cu.OrderTime.Value);
    if (!string.IsNullOrWhiteSpace(cu.Standards)) c.WithStandards(cu.Standards);
    if (cu.AllowDirectApproach.HasValue) c.AllowDirectApproach(cu.AllowDirectApproach.Value);
    if (cu.GuaranteeFirstSample.HasValue) c.GuaranteeFirstSample(cu.GuaranteeFirstSample.Value);
    if (cu.MutualRelationRequirement != null)
        c.WithMutualRelationRequirement(cu.MutualRelationRequirement.Min, cu.MutualRelationRequirement.Max);
    if (cu.CallPoliceChance.HasValue) c.WithCallPoliceChance(cu.CallPoliceChance.Value);
    if (cu.DependenceBase.HasValue) c.WithDependence(cu.DependenceBase.Value, cu.DependenceMultiplier ?? 1f);
    ...
    if (cu.PreferredProperties != null && cu.PreferredProperties.Count > 0)
        c.WithPreferredPropertiesById(cu.PreferredProperties.ToArray());
});
```

**`WithSpending(min,max)` + `WithOrdersPerWeek(min,max)` + `WithPreferredOrderDay` is literally "special customer groups who periodically visit and buy large quantities"** **[inference]**.

**A real vanilla NRE they had to route around, worth knowing:**

```csharp
// ScheduleOne-Personnel/API.cs:91-95
// Phone-contact presentation. Unlocking the relationship makes ContactsDetailPanel render the
// real name instead of "???" (respected against save data by S1API); it also keeps clear of the
// vanilla NRE that triggers when a role-less NPC is left "mutually known but locked".
```

**Distant custom NPCs render a blank billboard unless you set an impostor:**

```csharp
// ScheduleOne-Personnel/API.cs:218-221
// Vanilla enables the >50m billboard impostor unconditionally, and runtime-built AvatarSettings
// carry no impostor texture - without one, distant custom NPCs render an empty billboard. The
// impostor builder API only exists in newer S1API builds, so this is best-effort via reflection
```

**Adding a phone-map marker to a custom NPC:**

```csharp
// ScheduleOne-Personnel/API.cs:264-275
public static void AddMapMarker(GameObject npcRoot)
{
    if (npcRoot == null || !S1NPCs.NPCManager.InstanceExists) return;
    var mgr = S1NPCs.NPCManager.Instance;
    if (mgr == null || mgr.NPCPoIPrefab == null) return;
    var npc = npcRoot.GetComponent<S1NPCs.NPC>();
    if (npc == null) return;
    var poi = UnityEngine.Object.Instantiate(mgr.NPCPoIPrefab, npcRoot.transform);
    poi.SetNPC(npc);
    poi.enabled = true;
}
```

**Config persistence — `MelonPreferences`, with the category-naming convention spelled out:**

```csharp
// ScheduleOne-Personnel/Config/Preferences.cs:5-32
/// MelonPreferences wrapper. Category id is prefixed with the mod name so the "Mod Manager &amp; Phone App"
/// settings UI auto-detects it. ...
private const string CategoryId = "Personnel_01_Main";
...
_category = MelonPreferences.CreateCategory(CategoryId, "Personnel (Custom NPCs)");
_loadExamplePack = _category.CreateEntry("LoadExamplePack", false, "Load example NPC pack",
    "OFF by default. ... Requires a game restart.");
_enableAutoRegister = _category.CreateEntry("EnableAutoRegister", true, "Auto-register pack NPCs",
    "ON by default. ... Turn OFF as a kill switch if a pack misbehaves. Requires a game restart.");
```

**Content lives outside the DLL** — packs are folders at `<Schedule I>/UserData/Personnel/Packs/<PackName>/` containing `manifest.json` + PNGs; `"schemaVersion": 2`, `"autoRegister": true`, `"npcs": [...]` **[verified]** (`README.md`). **MP rule stated plainly:** *"In co-op, everyone needs the same packs installed - the same rule as for mods."* **[community]** (README).

**Il2CppInterop registration:** Personnel does **not** use `ClassInjector` at all — it emits `PersonnelNpc` subclasses via `System.Reflection.Emit` into a non-collectible `AssemblyBuilder` named `Personnel.Generated`, because S1API's own assembly scan does the discovery **[verified]** (`Spawn/DynamicNpcTypeFactory.cs:95-122`).

---

### 1.6 Deep dive — NACops (police) · source available, **NO LICENSE — learn from, do not copy**

Repo: https://github.com/XOWithSauce/schedule-nacops — **no license file** (GitHub API `license: null`), last push 2026-08-03, mod v2.1.0, Thunderstore "Game version: 0.4.5f2 default" **[verified]**.

Files: `Source/NACops.cs`, `Source/Config/ConfigLoader.cs` (37KB), `Source/Officer/{CopInitHelper,OfficerOverrides}.cs`, `Source/Raid/RaidPropertyEvent.cs` (69KB), `Source/PrivateInvestigator/PrivateInvestigator.cs` (46KB), `Source/MassSurveillance/{MassSurveillance,HylandFlock}.cs`, `Source/BuyBust/BuyBust.cs`, `Source/DrugApprehender/Apprehender.cs`, `Source/SnitchSamples/SnitchSamples.cs`, `Source/Debug/{ConsoleModule,DebugModule}.cs`, `Source/Utils/{AvatarUtility,InvestigatorPatches,Police_ProcessVision_Patch,PropertyDoorController_Patch}.cs` **[verified]**.

**Assembly attributes — the full set a police mod needs:**

```csharp
// schedule-nacops/Source/NACops.cs:50-61
[assembly: MelonInfo(typeof(NACops.NACops), NACops.BuildInfo.Name, NACops.BuildInfo.Version, ...)]
[assembly: MelonColor()]
[assembly: MelonOptionalDependencies("FishNet.Runtime")]
[assembly: MelonGame("TVGS", "Schedule I")]
#if MONO
[assembly: MelonPlatformDomain(MelonPlatformDomainAttribute.CompatibleDomains.MONO)]
[assembly: MelonLoader.VerifyLoaderVersion("0.7.2", true)]
#else
[assembly: MelonPlatformDomain(MelonPlatformDomainAttribute.CompatibleDomains.IL2CPP)]
[assembly: MelonLoader.VerifyLoaderVersion("0.7.2", true)]
#endif
```

**Exact Harmony patch targets** (class + method, in the game's assembly) **[verified]**:

| Target | File | Purpose |
|---|---|---|
| `PursuitBehaviour.UpdateArrest` | `Officer/OfficerOverrides.cs:32` | reimplement arrest range/time from config |
| `PursuitBehaviour.UpdateLethalBehaviour` | `Officer/OfficerOverrides.cs:72` | swap the officer's weapon prefab |
| `PursuitBehaviour.OnCurrentWeaponChanged` | `Utils/InvestigatorPatches.cs:35` | keep the investigator unarmed |
| `PoliceOfficer.GetNameAddress` | `Utils/InvestigatorPatches.cs:20` | rename officers |
| `PenaltyHandler.ProcessCrimeList` | `MassSurveillance/MassSurveillance.cs:670` | override fines / charges |
| `VisionCone.SetSightableStateEnabled` | `Utils/Police_ProcessVision_Patch.cs:26` | control what cops can notice |
| `PropertyDoorController.CanPlayerAccess` | `Utils/PropertyDoorController_Patch.cs:15` | let raid officers through doors |
| `Player.ConsumeProduct` | `DrugApprehender/Apprehender.cs:22` | apprehend players smoking |
| `Customer.ProcessHandover` | `BuyBust/BuyBust.cs:36` | buy-bust on handover |
| `Customer.SampleOffered` | `SnitchSamples/SnitchSamples.cs:24` | snitching customers |
| `SaveManager.Save(string)` / `SaveManager.Save()` | `NACops.cs:452,472` | persist property-heat data |
| `LoadManager.ExitToMenu` | `NACops.cs:481` | teardown |
| `DeathScreen.LoadSaveClicked` | `NACops.cs:491` | teardown |
| `Console.SubmitCommand(List<string>)` and `(Il2CppSystem.Collections.Generic.List<string>)` and `(string)` | `Debug/DebugModule.cs:476-510` | dev-console commands — **note all three overloads are patched** |

Cartel Enforcer (same author, **MIT**) adds these, which matter for hostile-NPC events: `CartelGoon.Spawn` / `.Despawn`, `Dealer.DealerUnconscious`, `Dealer.CheckNotifyPlayerOfDeal`, `Dealer.TryRobDealer` (with the comment `// This is only reached by FishNet Instance IsServer`), `CartelDealer.RandomizeInventory`, `CartelDealManager.CompleteDeal`/`.ExpireDeal`, `CartelActivities.TryStartActivity`, `CartelRegionActivities.TryStartActivity`, `Ambush.SpawnAmbush`/`.ContractReceiptRecorded`, `CombatBehaviour.SetTarget_Client`, `CallPoliceBehaviour.IsTargetValid`, `NPC.ProcessImpactForce`, `Customer.SampleConsumed`/`.GetSampleSuccess`/`.SampleWasSufficient`/`.OnCustomerUnlocked`, `WorldSpraySurface.Reward`, `ContactsApp.SetSelectedRegion`, `Quest.ActiveEntryCount` (getter), `Quest_DefeatCartel.OnSleepEnd`, `CartelInfluenceChangePopup.Show`, `DialogueController_Dealer.CheckChoice`/`.ChoiceCallback` **[verified]**.

**Runtime networked NPC creation — the single most valuable code block in this whole survey.** This is how you get a *new, working, networked* police officer at runtime on IL2CPP without a registered network prefab:

```csharp
// schedule-nacops/Source/Officer/CopInitHelper.cs:53-116 (abridged)
public static IEnumerator ReplicateCopNPC()
{
    PoliceOfficer officer = UnityEngine.Object.FindObjectOfType<PoliceOfficer>();
    AvatarSettings copySettings = officer.Avatar.CurrentSettings;
    GameObject obj = officer.gameObject;
    obj.SetActive(false);

    GameObject clone = UnityEngine.Object.Instantiate(obj);
    copBaseClone = clone;
    clone.transform.position = Vector3.zero;

    NPC npc = clone.GetComponent<NPC>();
    NetworkObject newNob = clone.GetComponent<NetworkObject>();
    PoliceOfficer offc = clone.GetComponent<PoliceOfficer>();
    offc.AutoDeactivate = false; // Prevent from returning to station and from being added to officer pool

    clone.name = "RuntimeOfficer";
    yield return MelonCoroutines.Start(InitiateClone(newNob, networkManager));

    npc.NPCData.BasicInfo.ID = "officerPrefab";
    if (!NPCManager.NPCRegistry.Contains(npc)) NPCManager.NPCRegistry.Add(npc);
    npc.Avatar.LoadAvatarSettings(copySettings);
    offc.PursuitBehaviour.arrestingEnabled = false;

    try { networkManager.ServerManager.Spawn(newNob); }
    catch (Exception ex) { Log($"Failed to spawn officer {ex}"); }

    offc.Behaviour.ScheduleManager.DisableSchedule();
    offc.Movement.PauseMovement();
    if (PoliceOfficer.Officers.Contains(offc)) PoliceOfficer.Officers.Remove(offc);
    obj.SetActive(true);
}
```

```csharp
// schedule-nacops/Source/Officer/CopInitHelper.cs:118-228 (abridged, IL2CPP branch)
public static IEnumerator InitiateClone(NetworkObject newNob, NetworkManager netManager, NPCData dataPreset = null)
{
    NPC npc = newNob.GetComponent<NPC>();

    // Populate unassigned fields  -- activate for a frame, then deactivate
    newNob.transform.Find("Avatar").gameObject.SetActive(true);
    newNob.transform.Find("Avatar/BodyContainer").gameObject.SetActive(true);
    newNob.GetComponent<NavMeshAgent>().enabled = true;
    newNob.gameObject.SetActive(true);
    yield return Wait01;            // WaitForSeconds(0.1f)
    yield return frameEnd;          // WaitForEndOfFrame
    ... (deactivate the same three) ...

    // Remove CustomerAttendDealBehaviour since calling disable on it gives nullreference exceptions
    Behaviour temp = npc.Behaviour.GetBehaviour("Customer attend deal");
    if (temp) { UnityEngine.Object.Destroy(temp.gameObject); npc.Behaviour.OnValidate(); }

    byte componentIndex = 0;
    newNob.UpdateNetworkBehaviours(newNob, ref componentIndex);
    if (dataPreset != null) npc.ApplyNPCData(dataPreset);
    newNob.Preinitialize_Internal(netManager, 150, null, true);
    newNob.Initialize(true, true);
    newNob.SetIsNetworked(false);
    npc.Awareness.SetAwarenessActive(false);
}
```

On Mono the same three internal calls go through `AccessTools.Method(typeof(NetworkObject), "UpdateNetworkBehaviours" | "Preinitialize_Internal" | "Initialize", ...)` because they are non-public there **[verified]**. Cartel Enforcer's `Source/Misc/InitHelper.cs:68-135` is byte-for-byte the same technique.

**Police/law game symbols confirmed** (all under `Il2CppScheduleOne.{Police,Law,Vision,NPCs,PlayerScripts}`) **[verified]**:

- `PoliceOfficer` — static `.Officers`; `.AutoDeactivate`, `.ChatterEnabled`, `.BodySearchChance`, `.BodySearchDuration`, `.GunPrefab`, `.TaserPrefab`, `.belt.GunObject`, `.onExitVehicle`, `.PursuitBehaviour`, `.Awareness.VisionCone`, `.Movement`, `.Behaviour`, `.Health`, `.DialogueHandler`, `.NPCData`
- `PoliceStation` — static `.PoliceStations[0]`; `.SpawnPoint`, `.Doors[0]`, `.NPCEnteredBuilding(npc, door)`
- `PursuitBehaviour` — `.TargetPlayer`, `.arrestingEnabled`, `.Npc.CenterPoint`, `.timeWithinArrestRange`, `.wasInArrestCircleLastFrame`, `.leaveArrestCircleCount`, `.IsTargetRecentlyVisible`, `.SetMovementSpeed(f, "combat", 5)`, `.currentWeapon`, `.ClearWeapon()`, `.VirtualPunchWeapon.onSuccessfulHit`, `.officer`, `.SucessfulHit` *(sic — vanilla typo)*, `.OnCurrentWeaponChanged(w)`
- `VisionCone` — `.WorldspaceIconsEnabled`, `.RangeMultiplier`, `.stateSettings[player][EVisualState].NoticeTimeMultiplier`; enum `EVisualState` (`.Visible`, …)
- `Singleton<LawController>.Instance` — `.MondaySettings` … `.SundaySettings` of type `LawActivitySettings { Curfews, Checkpoints (CheckpointInstance[]), Patrols, VehiclePatrols, Sentries }` — **replaceable wholesale, which is how NACops adds patrols/sentries**
- `Player.Local.CrimeData` (`PlayerCrimeData`) — `.AddCrime(Crime)`, `.SetPursuitLevel(EPursuitLevel)`, `.CurrentPursuitLevel`, `.CurrentPursuitLevelDuration`, `.TimeSincePursuitStart`, `.CurrentArrestProgress`, `.SetArrestProgress(f)`; enum `PlayerCrimeData.EPursuitLevel { None, Investigating, … }`
- Crime classes instantiated directly: `ViolatingCurfew`, `Vandalism`, `AttemptingToSell`, `DrugTrafficking`, `TransportingIllicitItems`, `Evading`, `FailureToComply`, `Theft`, `BrandishingWeapon`, `DischargeFirearm`
- `PenaltyHandler` — consts `ASSAULT_FINE`, `ATTEMPT_TO_SELL_FINE`, `BRANDISHING_FINE`, `DEADLY_ASSAULT_FINE`, `DISCHARGE_FIREARM_FINE`, … + `ProcessCrimeList`
- `Player.Local.onArrested`; `PlayerInventory.instance.onEquippedSlotChanged`
- `NetworkSingleton<TimeManager>.Instance.onHourPass` / `.onSleepEnd`
- `NPCData.BasicInfo.{ID,FirstName,LastName}`, `.Inventory.CanBePickpocketed`, `.WeatherBehaviour.UseUmbrellaChance`, `.Health.MaxHealth`; `npc.Actions._canUseUmbrella`
- `officer.Movement.Agent.areaMask = 57; // identical to employee` — the NavMesh mask that lets an officer enter buildings
- `officer.Behaviour.CombatBehaviour.{GiveUpRange, DefaultSearchTime, DefaultMovementSpeed, GiveUpAfterSuccessfulHits}`
- `DialogueController_Police`, `DialogueController.Choices`

**Asset loading:** no AssetBundles. Weapons come from the game's own `Resources`:

```csharp
// schedule-nacops/Source/Officer/OfficerOverrides.cs:196-231 (paths verbatim)
case "goldenm1911": resourcePath = "Avatar/Equippables/M1911_Gold"; break;
case "revolver":    resourcePath = "Avatar/Equippables/Revolver";   break;
case "shotgun":     resourcePath = "Avatar/Equippables/PumpShotgun"; break;
...
UnityEngine.Object obj = Resources.Load(resourcePath);
GameObject gameObject = obj.TryCast<GameObject>();
rangedWeaponPrefab = UnityEngine.Object.Instantiate<GameObject>(gameObject, new Vector3(0f,-5f,0f), Quaternion.identity, null)
                        .GetComponent<AvatarEquippable>();
```

**Config persistence: JSON under `Mods/NACops/`, plus a `MelonPreferences` mirror.** `raid.json`, `officer.json`, `HeatData/(organisation).json` — the last written from a `SaveManager.Save` prefix **[verified]** (`NACops.cs:452-470`; file paths from the Thunderstore page **[community]**). A `SyncConfig()` reflects `MelonPreferences` entry values back onto the JSON config object field-by-field (`NACops.cs:117-143`).

**Il2CppInterop registration** (`MassSurveillance/HylandFlock.cs:45-53, 636-644`) — the canonical shape:

```csharp
[RegisterTypeInIl2Cpp]
public class HylandFlockInstance : MonoBehaviour
{
    public HylandFlockInstance(IntPtr ptr) : base(ptr) { }
    public HylandFlockInstance() : base(ClassInjector.DerivedConstructorPointer<HylandFlockInstance>())
        => ClassInjector.DerivedConstructorBody(this);
}
```

Cartel Enforcer uses the same pattern to subclass the game's own `Quest`: `[RegisterTypeInIl2Cpp] public class Quest_TrucedRecruits : … { public Quest_TrucedRecruits() : base(ClassInjector.DerivedConstructorPointer<Quest_TrucedRecruits>()) => ClassInjector.DerivedConstructorBody(this); }` **[verified]** (`Source/Allied/Quests/AlliedIntroQuest.cs:38-53`).

**Teardown discipline.** `ExitPreTask()` (`NACops.cs:371-449`) stops every coroutine, clears every static collection, `Destroy`s created `Texture2D`s and `DestroyImmediate`s instanced `ScriptableObject` `AvatarSettings`. **[inference]** Our mods must do the same or we leak across save loads — the mod is re-initialised per session via `LoadManager.Instance.onLoadComplete` and `OnSceneWasInitialized(buildIndex == 1)`.

---

### 1.7 Deep dive — OverTheCounter (customer NPCs + phone app + MP) · source available, **NOASSERTION license — treat as all-rights-reserved**

Repo: https://github.com/hdlmrell/OTC-S1-Mod — GitHub reports `NOASSERTION`, last push 2026-04-14 **[verified]**. Largest gameplay mod with source (dozens of files under `OverTheCounter/{Apps,Logic,Patches,UI,Utilities}`).

**Its multiplayer gate is a two-line helper used in ~120 places** — the pattern I'd adopt verbatim:

```csharp
// OTC-S1-Mod/OverTheCounter/Utilities/NetworkHelper.cs
#if IL2CPP
using Il2CppFishNet;
#else
using FishNet;
#endif
/// Multiplayer authority helper. All shared-state mutations must be gated
/// behind <see cref="IsHost"/> so only the server/host executes them.
/// Returns true in single-player (no NetworkManager present).
public static class NetworkHelper
{
    public static bool IsHost =>
        InstanceFinder.NetworkManager == null || InstanceFinder.IsServer;
}
```

**Notable Harmony targets** (`OverTheCounter/Patches/*`, `Apps/*`, `Utilities/*`) **[verified]** — heavy on `Customer` and the phone:

`Customer.OfferContract`, `Customer.OfferContractToDealer`, `Customer.NotifyPlayerOfContract`, `Customer.AcceptContractClicked`, `Customer.ContractRejected`, `Customer.ProcessHandover`, `Customer.SetUpResponseCallbacks`, `Contract.UpdateTiming`, `Contract.SubmitPayment`, `HandoverScreen.Open`/`.Close`, `PhoneApp.OpenApp`, `MapApp.SetOpen`, `CompassManager.AddElement`, `Quest.InitializeQuest`, `StaticDoor.NPCSelected`, `DialogueController_Fixer.{ModifyChoiceList,ChoiceCallback,CheckChoice,ModifyDialogueText}`, `Supplier.{EndMeeting,GetAppropriateLocation,MeetAtLocation}`, `FleeBehaviour.Activate`, `SaveManager.Save(string)`, plus a profiler set (`NPCMovement.Update`, `NPCBehaviour.Update`, `PlayerMovement.Update`, `PlayerCamera.Update`, `TimeManager.Update`, `LandVehicle.Update`).

Two patches are architecturally interesting: `Patches/NpcTypeDiscoveryPatch.cs` **transpiles** S1API's own `NPC.CreateWrapperForNetworkSpawnedNPC` and `NPC.PreRegisterAllNpcPrefabs`, and `Patches/SafeTypeLoadPatch.cs` prefixes `HarmonyLib.AccessTools.GetTypesFromAssembly` to survive broken assemblies in the load set **[verified]**.

**Il2CppInterop MonoBehaviours** — OTC registers overlays both ways:

```csharp
// OTC-S1-Mod/OverTheCounter/UI/HUDOverlay.cs:28, 98
[RegisterTypeInIl2Cpp]
...
ClassInjector.RegisterTypeInIl2Cpp<HUDOverlay>();
```
(same for `MinimapOverlay`, `RecipeOverlay`, `StoreAlertOverlay`, `DebugHelpers`, `ImmediateQuestWindowConfig`) **[verified]**.

**Assets:** embedded resources read with `Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)` for PNGs/icons, and 3-D content as **GLB via S1MAPI** rather than AssetBundles (`Core.cs:262 S1MAPI.Utils.EmbeddedResourceLoader.LoadBytes(...)`, `Logic/Placement/CheckoutCounterInstance.cs:352 byte[] glbData = EmbeddedResourceLoader.LoadBytes(...)`) **[verified]**. **Caution:** S1MAPI is **GPL-3.0**, so OTC's approach carries licence implications ours may not want.

**MP mechanism:** OTC does *not* use custom FishNet RPCs. It gates on `NetworkHelper.IsHost`, keeps a `ConfigSyncData`/`ConfigReplicatorPatch` layer that prefixes existing vanilla `RpcLogic___*` methods, and moves checkout state over **Steam P2P** (`Logic/CheckoutProcess.cs` has `_p2pSubscribed` guards on both host and client branches) **[verified]**.

---

### 1.8 Shorter notes on the remaining source-available mods

**LooseEnds** (MIT) — smallest useful police-reaction source. Patches `NPCHealth.NotifyAttackedByPlayer`, `NPCHealth.Die`, `NPCHealth.KnockOut`, `Draggable.StartDragging`, `Draggable.StopDragging` **[verified]**.

**PropHunt** (MIT) — best MP-gamemode source. Registers with Side Hustle as `Support = GamemodeSupport.Multiplayer, Surface = GamemodeSurface.World`, and its `Core.cs` comments contain a crash lesson we should heed: *"Gameplay patches are applied lazily on the first gameplay scene (see OnSceneWasInitialized \"Main\"), NOT here at load. Patching the game's gameplay methods while the Side Hustle hub builds its menu UI intermittently hard-crashes the game"* **[verified]** (`ScheduleOne-PropHunt/Core.cs:33-34, 171`). Its patch set is a useful catalogue of gameplay hooks: `PauseMenu.StuckButtonClicked`, `PlayerCamera.LateUpdate`/`ViewAvatar`, `PlayerMovement.TryToggleCrouch`, `InteractionManager.CheckInteraction`/`CheckRightClick`, `InteractableObject.ShowMessage`, `Equippable_RangedWeapon.Fire`/`.GetMagazine`, `Equippable_MeleeWeapon.ExecuteHit`, `FXManager.CreateImpactFX`, `VanillaPhone.ToggleFlashlight`, `Il2CppScheduleOne.Map.POI.Update`, `PhysicsDamageable.ReceiveImpact`, `TrashItem.SetPhysicsActive` **[verified]**.

**DealerTransportMod** (no licence) — smallest NPC-logistics source. Patches `NPC.Update`, `StorageMenu.Open(StorageEntity)`, `StorageMenu.CloseMenu`, `StorageEntity.OnClosed`, `SaveManager.Save(string)`, `LoadManager.StartGame` **[verified]**.

**Law Enforcement Enhancement Mod** (no licence, stale) — `[RegisterTypeInIl2Cpp]` on `OfficerSpawnSystem` (a MonoBehaviour, 137KB), `GameObject.Find("NetworkManager")` in `OnSceneWasLoaded` when `sceneName.Contains("Main")`, `_networkManager.ServerManager.Spawn(newOfficerGO)`, and JSON config at `Path.Combine(MelonEnvironment.UserDataDirectory, "law_enforcement_settings.json")` via `Newtonsoft.Json` **[verified]** (`Core.cs:41-70`).

**S1API itself** is worth reading as a hook catalogue — it patches, among ~90 targets: `NPCsLoader.Load`, `NPCLoader.Load`, `NPC.Awake`/`.Start`/`.OnDestroy`/`.WriteData`/`.GetSaveData`/`.ShouldSave`, `NPCManager.GetNPC`/`.GetSaveString`, `NPCInventory.Awake`/`.OnSleepStart`, `NPCHealth.Awake`/`.Load`/`.Revive`, `NPCMovement.SetGravityMultiplier`, `NPCScheduleManager.InitializeActions`/`.GetActionsTotallyOccurringWithinRange`, `Customer.Awake`, `Dealer.Awake`/`.Load`, `Supplier.Awake`/`.Start`, `SupplierStash.Start`, `Player.Awake`/`.OnDestroy`, `PlayerHealth.TakeDamage`, `LoadManager.StartGame`/`.LoadAsClient`/`.ExitToMenu`/`.Update`/`.QueueLoadRequest`/`.GetLoadStatusText`, `SaveManager.Save(string)`, `HomeScreen.Start`, `ContactsApp.Start`, `DealerManagementApp.SetOpen`/`.Refresh`, `DeliveryApp.SetIsAvailable`/`.CreateDeliveryStatusDisplay`, `DeliveryInstance.SetStatus`, `VehicleManager.LoadVehicle`, `LandVehicle.OnDestroy`/`.SetVisible`, `BuildManager.CreateGridItem`/`.CreateSurfaceItem`, `CallManager.QueueCall`/`.CallCompleted`, `CallInterface.StartCall`/`.Close`, `Console.Awake`/`.SubmitCommand`, `LoadingScreen.Close`, `NPCEnterableBuilding.Awake`, `DeliveryLocation.Awake`, `ParkingLot.Awake`, `QuestsLoader.Load`, `Quest.Start`, `QuestEntry.CreateCompassElement`, `TVHomeScreen.Awake`/`.Open`, `WorldSpraySurface.SetFinalized` **[verified]**.

---

## 2. Multiplayer / co-op (FishNet) compatibility

### 2.1 The one fact that changes our plan

**Schedule I ships FishNet v3, and on the IL2CPP branch FishNet's code generator never runs against a mod assembly, so custom RPCs are effectively unavailable.** **[community]** — stated in the community Cookbook, sourced to Discord messages from **Skippy** and **j0ckinjz**:

> Schedule I uses **FishNet v3**. This matters because the current live FishNet documentation is for **v4**, which has diverged.
> — `ScheduleOne-Cookbook/.../networking/fishnet-overview.md:14-21`, citing `discord.com/channels/1349221936470687764/1359978396708245675/1359978396708245675`

> On IL2CPP the FishNet code generator does not run against your mod assembly, so the clean custom-RPC path is effectively unavailable.
> — same file, lines 48-53, citing **j0ckinjz** `.../1359340799548199102/1383148447602835506`

The Cookbook names exactly two remaining routes: **(1) sync through the Steam lobby**, **(2) hand-roll FishNet interop** — with an explicit caution on (2):

> The IL2CPP FishNet approach below was reported to work in real multiplayer but not consistently (serializers sometimes never fire on load, and some paths needed Harmony patches to stabilize). If you just need to move small amounts of data, the Steam lobby route is far less painful.
> — `networking/custom-rpcs-and-codegen.md:122-126` **[community]**

Two further IL2CPP-specific gotchas from the same page **[community]** (all sourced to **XmusJackson (cheger32)**):

- Internal `NetworkBehaviour` dictionaries come up as **garbage rather than empty** under IL2CPP, so a derived behaviour must hand-initialise `_rpcLinks`, `_syncVars`, `_syncObjects`, `_bufferedRpcs`, `_serverRpcDelegates`, `_observersRpcDelegates`, `_targetRpcDelegates`, `_reconcileRpcDelegates` in `OnStartNetwork()`.
- *"if the server spawns a `NetworkObject` before a client has finished connecting, that client can hang on an infinite load. A crude mitigation was to delay the spawn 10-15 seconds so pending clients get in first; new invites after the spawn still could not join."*

Also relevant even on Mono: FishNet registers generated serializers via `RuntimeInitializeOnLoadMethod`, **which does not fire for mod-loader-loaded assemblies**, so you must reflect over `FishNet.Serializing.Generated` types and invoke their `InitializeOnce` yourself **[community]** (same page, lines 38-74).

### 2.2 FishNet concepts a Schedule I mod must respect

Names below are verified against the live FishNet docs (v4 wording, but the concepts and attribute names carry over from v3) at https://fish-networking.gitbook.io/docs — **[verified]** for the doc text, **[community]** for "the game runs v3":

- **`NetworkBehaviour`** — RPCs are object-bound and *"must be called on scripts which inherit from NetworkBehaviour"* (https://fish-networking.gitbook.io/docs/guides/features/network-communication/remote-procedure-calls.md).
- **`[ServerRpc]`** — client → server. Owner-only by default; `[ServerRpc(RequireOwnership = false)]` opens it to any client, and a trailing `NetworkConnection conn = null` parameter is auto-populated with the caller.
- **`[ObserversRpc]`** — server → observing clients. `ExcludeOwner`, `BufferLast` (re-sends latest value to late joiners — the vanilla answer to mid-game joins).
- **`[TargetRpc]`** — server → one client; first parameter must be the `NetworkConnection`.
- **`Channel.Reliable` / `Channel.Unreliable`**, `RunLocally`, `DataLength` — same doc.
- **SyncTypes / `SyncVar`** — https://fish-networking.gitbook.io/docs/guides/features/network-communication/synchronizing.md
- **Broadcasts** (not object-bound; the escape hatch used by the hand-rolled IL2CPP route) — https://fish-networking.gitbook.io/docs/guides/features/network-communication/broadcasts.md
- **`InstanceFinder`** — confirmed present under `Il2CppFishNet` and used as `InstanceFinder.NetworkManager` / `InstanceFinder.IsServer` **[verified]** (OTC `NetworkHelper.cs`). `InstanceFinder.IsHost` / `.IsClient` exist in FishNet's API but I did **not** see them used in any Schedule I mod I read — every mod used `IsServer` or the game's own `Lobby.IsHost`. Treat `IsHost`/`IsClient` as **[inference]** until confirmed against our interop assemblies.
- **`NetworkObject`** — verified: `Il2CppFishNet.Object.NetworkObject`, with the runtime-init trio `UpdateNetworkBehaviours(NetworkObject, ref byte)`, `Preinitialize_Internal(NetworkManager, int, NetworkConnection, bool)`, `Initialize(bool, bool)`, plus `SetIsNetworked(bool)` **[verified]** (NACops `CopInitHelper.cs`).
- **Spawning** — `networkManager.ServerManager.Spawn(NetworkObject | GameObject)`; the `NetworkManager` is found at runtime with `UnityEngine.Object.FindObjectOfType<NetworkManager>(true)` (NACops) or `GameObject.Find("NetworkManager")` (LawEnforcement) **[verified]**.
- **Runtime-registered network prefabs are hard** because the prefab collection is keyed by a **16-bit AssetBundle hash** and populated during `Registry.Awake`: `networkManager.GetPrefabObjects<SinglePrefabObjects>(assetBundleHash, createIfMissing: true)` then `netPrefabs.AddObject(networkObject, checkForDuplicates: true)` **[community]** (Cookbook `core-concepts/known-hook-points.md:17-76`, sourced to **Skippy**). Registration order must match across peers — which is exactly why Personnel sorts its emitted types ordinally: *"FishNet spawnable registration is order-sensitive and co-op peers must agree"* **[verified]**.

### 2.3 Schedule I's own singleton/lobby layer (verify against our interop assemblies)

`ScheduleOne.DevUtilities` supplies `Singleton<T>`, `NetworkSingleton<T>`, `PlayerSingleton<T>` **[community]** (Cookbook `core-concepts/game-structure-and-namespaces.md:39, 62-79`), and I saw all three shapes used first-hand **[verified]**:

- `Singleton<LawController>.Instance`, `Singleton<Lobby>.Instance` (`.IsHost`, `.LobbySteamID`, `.Players`)
- `NetworkSingleton<TimeManager>.Instance`, `NetworkSingleton<MoneyManager>.Instance` (`.onlineBalance`), `NetworkSingleton<NPCManager>.Instance`, `NetworkSingleton<Cartel>.Instance` (`.Status`, `.Influence.GetInfluence(region)`), `NetworkSingleton<EmployeeManager>.Instance`, plus `NetworkSingleton<T>.InstanceExists`
- `PlayerSingleton<PlayerInventory>.Instance`; also the static `PlayerInventory.instance`
- `Il2CppScheduleOne.Networking.Lobby.Instance.IsHost` (HonestMainMenu) — note there are **two** `Lobby` references in the wild: `Il2CppScheduleOne.Networking.Lobby` (HonestMainMenu, verified) and `Singleton<Lobby>` (Cookbook, community). Confirm which our build exposes.

**Vanilla lobby cap is 4 players**, and at least six separate mods exist purely to raise it (FullHouse → 32, BiggerLobbies → 20, MultiplayerPlus → 20, BiggerCrew → 16, CyrilZ0817's → 16) **[verified]** (Thunderstore + GitHub descriptions).

### 2.4 Real MP claims from real mod pages

| Mod | Stated MP behaviour | Label |
|---|---|---|
| **HireableDeliveryDriver (IL2CPP)** | *"## Multiplayer — Route management is host-only. Host controls hiring, wages, and route changes. Clients can still see and use the vans once spawned."* Requirements: *"MelonLoader 0.7.1+"* | **[community]** https://thunderstore.io/c/schedule-i/p/bwyan/HireableDeliveryDriver_IL2CPP_port/ |
| bwyan-HireableDeliveryDriver (TS short desc) | *"Hire delivery drivers to move items between loading docks on your properties **(host-only management)**."* | **[community]** Thunderstore API |
| **Personnel** | *"In co-op, everyone needs the same packs installed - the same rule as for mods."* | **[community]** README |
| **Hotline** | *"The master key and any synthetic press it injects stay **client-local, so nothing affects multiplayer**."* | **[community]** README |
| **SideHustle** | *"Schedule I normally **kicks joiners who aren't on the host's Steam friends list**. While you host a Side Hustle gamemode that kick is lifted."* + host/client roles handed to the gamemode via `LaunchContext` | **[community]** README |
| **OverTheCounter** | Host-authoritative by construction — ~120 `NetworkHelper.IsHost` gates, Steam-P2P for checkout state | **[verified]** source |
| **NACops** | No MP statement on the page; declares `[assembly: MelonOptionalDependencies("FishNet.Runtime")]` and spawns via `ServerManager.Spawn` — i.e. server-side only by construction | **[verified]** source; **[inference]** on the MP posture |
| UpgradedTrashCans, SprayPaintableVeeper, OG_Backpack | advertise "multiplayer compatible" / "proper multiplayer support" / "saving of its contents for all players, not just the host" | **[community]** Thunderstore descriptions |
| SortDealerCustomers, NACops, SideHustle, Personnel, Hotline, PropHunt | all state **IL2CPP-only** or ship separate MONO/IL2CPP packages | **[community]** |

**Pattern**: host-only management with client-visible results is the norm; nobody in this ecosystem is doing bidirectional custom RPCs on IL2CPP.

### 2.5 Recommendation for our three mods

**[inference]**, but well-grounded:

1. **Be server-authoritative, not host-gated-out.** Wrap the exact OTC helper and gate every state mutation:
   `public static bool IsHost => InstanceFinder.NetworkManager == null || InstanceFinder.IsServer;`
   This returns `true` in singleplayer, so the same code path works for solo play with no special-casing.
2. **All three features are naturally host-authoritative.** Driver routes/vans, police intensity/outlaw state, and special-customer visit scheduling are all world simulation. Run them only on the host and let FishNet replicate the *vanilla* objects (NPCs spawned via `ServerManager.Spawn`, vanilla `PoliceOfficer`/`Customer`/`LandVehicle` components) — those already have working generated RPCs and SyncVars because they were compiled in the Unity editor. **Do not create our own `NetworkBehaviour`.**
3. **Require all players to install, and say so on the mod page.** Because the S1API prefab pipeline registers spawnable NPC prefabs by simple type name in a load-order-sensitive way, a client without our DLLs will not be able to reconstruct wrappers for our NPCs. Personnel states the same rule for packs.
4. **Keep our shared-config sync out of FishNet.** If the host's toggles need to reach clients (e.g. "Police Improvements is ON"), push a tiny string through **Steam lobby data** (host-writes / everyone-reads) via `SteamMatchmaking.SetLobbyData(Singleton<Lobby>.Instance.LobbySteamID, key, value)`, or use **SteamNetworkLib** (MIT, 23K downloads) instead of raw Steamworks. Limits: ~8 KB per player of member data; only the host may write lobby data **[community]** Cookbook `networking/steam-lobby-data-sync.md`.
5. **Version-stamp the handshake.** Publish `<mod>_ver` in lobby member data so a mismatched client can be warned rather than desynced. **[inference]**
6. **Delay any runtime `ServerManager.Spawn` until clients are connected** — the reported infinite-client-load hazard. Spawn from `LoadManager.Instance.onLoadComplete` + a `WaitUntil(LoadManager.Instance.IsGameLoaded)`, as NACops does, and consider gating further on lobby membership being stable.
7. **Detect co-op cheaply for the UI:** `InstanceFinder.NetworkManager != null && !InstanceFinder.IsServer` ⇒ we are a client; grey out management controls and show "Host only", exactly as HireableDeliveryDriver does.

---

## 3. Settings UI / main-menu injection patterns

### 3.1 Is there an established mod-settings framework? **Yes — two, and they are complementary, not competing.**

| Framework | URL | What it provides | Integrate or ignore? |
|---|---|---|---|
| **ModsApp** (k073l) | https://thunderstore.io/c/schedule-i/p/k0Mods/ModsApp/ · https://github.com/k073l/s1-modsapp · **MIT** | A "Mods" **phone app** that auto-discovers every loaded mod, shows name/version/author/compat/enabled, and renders **all** its `MelonPreferences` entries with type-appropriate controls (bool, int, float, string, enum, Color, KeyCode, Vector3, List, Dictionary), plus arbitrary **JSON config** files under `UserData`. Search with fuzzy matching, categories, themes, per-mod changelog/readme viewer, dependency tracking, in-app log explorer. 16.9K downloads, rating 6. **Requires S1API ≥ 3.0.1 — i.e. exactly the fork we already ship.** | **Integrate passively.** Do nothing but name our `MelonPreferences` categories well and it works. |
| **Mod Manager & Phone App** (Prowiler) | https://www.nexusmods.com/schedule1/mods/397 | *"Adds a 'Mod Settings' app to the phone home screen. Lets you browse installed mods & change their settings in-game. Modify configurable options using toggles/input fields/keybinds & save."* No public source found. | **Integrate passively.** Multiple mods (`Hotline`, `Personnel`, `WarehousePlus`, `SeaDoge-StrongerGrip`, `DiumStream-LevelUpHUD`) name it as *the* in-game settings UI. |
| **Hotline** (DooDesch) | https://github.com/DooDesch-Mods/ScheduleOne-Hotline · **MIT** | One in-game overlay + one master key for every mod's HUD. `Hotline.Api.Hud.RegisterPanel(id, title)`, `RegisterAction`, `RegisterToggle(label, get, set)`, `RegisterSlider(label, min, max, get, set, step, unit)`, `RegisterText(provider)`, `BindPanelLog`, `Log`. Drop-in single-file shim (`HotlineApi.cs`) so it is a **no-op when absent**. Does **not** use S1API. Also *"auto-catches mods that grab function keys"*. | **Optionally integrate** for a debug/live-tuning panel. **Not** a substitute for a main-menu screen. |
| **SideHustle** `SettingDescriptor` | https://github.com/DooDesch-Mods/ScheduleOne-SideHustle · **MIT** | Host-config form on the main menu for a registered *gamemode*: `SettingType { Slider, Toggle, Segmented, Text, Dropdown }`, `SettingPreset` bundles, values shipped to host+clients in the launch config blob. | **Ignore as a settings framework** (wrong semantics — it configures a match, not a mod), but **copy its shape** for our own screen. |
| Mod Manager (LethalLizard) | https://www.nexusmods.com/schedule1/mods/58 | Enable/disable mods in-game + open the Mods folder. | Ignore. |
| SIMM / r2modman / Gale / Vortex | https://www.nexusmods.com/schedule1/mods/1750 · https://thunderstore.io/c/schedule-i/p/ebkr/r2modman/ · https://thunderstore.io/c/schedule-i/p/Kesomannen/GaleModManager/ | External, branch-aware install/profile managers. ModsApp's README explicitly recommends these over its own enable/disable. | Ignore for UI; **do** target them with a correct `manifest.json`. |
| BepInEx `ConfigurationManager` equivalent | — | **Not found.** No BepInEx config-manager analogue exists in this ecosystem — consistent with the ecosystem being MelonLoader-first. | n/a |

**The auto-detection convention we must follow** — this is a concrete, cheap win, quoted from Personnel:

```csharp
/// MelonPreferences wrapper. Category id is prefixed with the mod name so the "Mod Manager &amp; Phone App"
/// settings UI auto-detects it.
private const string CategoryId = "Personnel_01_Main";
```
and SideHustle uses `SideHustle_01_Main`, Hotline uses `Hotline_01_Main` **[verified]**. So: **`<ModName>_01_Main`**, created with `MelonPreferences.CreateCategory("<ModName>_01_Main", "<Friendly Name>")`, and every entry given a `display_name` **and** a `description` (`CreateEntry(id, default, displayName, description)`), because both settings UIs render the description as help text **[verified]** ModsApp `PREFERENCES.md`).

ModsApp's `PREFERENCES.md` is also the authoritative hot-reload guide: `entry.OnEntryValueChanged.Subscribe((old,new) => …)`, `entry.OnEntryValueChangedUntyped.Subscribe(...)` for a multi-entry tracker, `MelonPreferences.Save()`, and `public override void OnPreferencesSaved()` on the `MelonMod`; and *"the app will remind you when a restart might be necessary… This depends on how individual mods handle preference updates"* **[verified]**. **[inference]** Our three toggles should be genuinely hot-appliable (subscribe to `OnEntryValueChanged` and enable/disable the feature's coroutines + Harmony patches at runtime) so that we work from *either* settings app in addition to our own screen.

### 3.2 Mods that add a main-menu (title screen) entry — and exactly how

**Three exist, and I read all three.**

**(a) SideHustle** — the reference implementation. Full code in §1.3. Summary of the technique:

| Step | How |
|---|---|
| Trigger | `MelonMod` scene lifecycle only. `OnSceneWasInitialized(_, "Menu")` → reset; then a per-frame `TickRetry()` |
| Timing | wait **20 frames** (`WarmupFrames`) before touching UI, then retry for ~120 more frames, then give up |
| Find the menu | `UnityEngine.Object.FindObjectsOfType<MenuScreen>(true)`, pick the one with `.OpenOnStart == true`, fall back to `screens[0]` |
| Find a template | `home.GetComponentsInChildren<Button>(true)`, prefer label match against `{"settings","options","load","continue","new game","quit","exit"}` — *"Settings has the most side-effect-free click"* |
| Create | `Object.Instantiate(template.gameObject, template.transform.parent, false)`, `localScale = Vector3.one`, rename, `SetSiblingIndex(idx)` |
| Style | inherited from the clone; label set through `TextMeshProUGUI` with `enableWordWrapping = false; overflowMode = Overflow` |
| Rewire | `onClick.RemoveAllListeners()` **plus** `SetPersistentListenerState(i, UnityEventCallState.Off)` for all `GetPersistentEventCount()` |
| Harmony | **none** |
| Idempotency | scan for our own button names first and adopt them |

**(b) HonestMainMenu** — same clone-a-button idea, but with **hard-coded paths** (`GameObject.Find("MainMenu")` → `Find("Home/Bank")` → `Find("Continue")`), and it does Harmony-patch `MainMenuRig.LoadStuff` (for the avatar/cash-pile display, not for buttons). It also renames the *screen* panel and its `Title` TMP child. Full code in §1.4. **[inference]** The hard-coded `Home/Bank` path is a liability across updates; SideHustle's `OpenOnStart` discovery is strictly more robust. But HonestMainMenu is how we *know* the names, and its `MainMenuPopup.Instance.Open(title, body, bool)` call gives us a native dialog for free.

**(c) Inkubator / Personify** — these don't add their own button; they register with SideHustle and then draw a **full-screen overlay on top of the live menu scene**:

```
/// The in-game NPC editor overlay. Opened from the Side Hustle hub, it runs in the menu scene ON TOP of the live
///   — ScheduleOne-Personify/Editor/EditorUI.cs:15
/// Side Hustle. Launching it (from the main-menu hub) opens the tattoo editor overlay on top of the live
///   — ScheduleOne-Inkubator/Core.cs:13
// Centered "picker" card (consistent with the Side Hustle hub's centered window).
///   — ScheduleOne-Inkubator/Editor/EditorUI.cs:166
```
Both guard on `sceneName == "Menu"` in `OnSceneWasInitialized` / `OnSceneWasUnloaded`, and both reach the live menu character via `using S1MenuRig = Il2CppScheduleOne.UI.MainMenu.MainMenuRig;` **[verified]**. Personify's self-test even asserts `SceneManager.GetActiveScene().name != "Menu"` before running.

**(d) SaveExpansion** (SirTidez) — *"expands Schedule I's **native save menu** from 5 to up to 20 slots"* **[community]**; I could not locate the source repo (the Thunderstore `website_url` is the bare user profile `https://github.com/SirTidez`), so its mechanism is unverified.

**Confirmed scene / object names** — answering the question directly **[verified]**:

- The menu scene is called **`Menu`** (not `MainMenu`). Confirmed independently in three repos: `UIConstants.MenuSceneName = "Menu"`, `MenuInjector`'s doc comment, and `if (sceneName == "Menu")` in Inkubator/Personify.
- The gameplay scene is **`Main`**, and it is **`buildIndex == 1`** (`NACops.OnSceneWasInitialized: if (buildIndex == 1)`; `Snitch: _inWorld = sceneName == "Main"`; `Personnel: if (sceneName == "Main")`; `LawEnforcement: if (!sceneName.Contains("Main")) return;`).
- Scene load paths are `Assets/Scenes/{sceneName}.unity`.
- Menu root GameObject: **`MainMenu`**; nav buttons under **`MainMenu/Home/Bank`**; screens are siblings of `Home` under `MainMenu` (e.g. `MainMenu/Continue`), each with a `Title` child; `MainMenu/InputPrompt (Back)` carries `ScheduleOne.UI.Input.InputPrompt`.
- Relevant types: `Il2CppScheduleOne.UI.MainMenu.{MenuScreen (.OpenOnStart), MainMenuRig, MainMenuPopup}`.
- The menu is **not** a persistent scene — it is loaded and unloaded (`OnSceneWasUnloaded(_, "Menu")` is used by Inkubator/Personify to tear down), and it can **re-initialise more than once per menu load**.

### 3.3 In-game pause menu and phone as alternative surfaces

**Pause menu:** the type is `PauseMenu`, and PropHunt patches `PauseMenu.StuckButtonClicked` **[verified]**. SideHustle also puts a friend-invite panel on the pause menu (*"every lobby member — not just the host — can invite Steam friends from the **pause-menu panel**"*) **[community]**. I found **no** mod that adds a *top-level pause-menu entry* the way SideHustle adds a main-menu entry — so that path is under-explored. `Save Game - Button via Pause-Menu - IL2CPP` exists on Nexus (title only, from the cached top-files snapshot) **[community]**, which suggests injecting a pause-menu button is feasible but I have no source for it.

**Phone:** heavily trodden. S1API exposes a first-class `PhoneApp` base (`S1API/PhoneApp/PhoneApp.cs`, which itself carries `[RegisterTypeInIl2Cpp]`) and patches `HomeScreen.Start` + a `HomeScreenScrollPatch` to make room for extra icons **[verified]**. Real examples: ModsApp, Prowiler's Mod Settings, OTC's `CustomersApp`/`GreenTabApp` (which patches `PhoneApp.OpenApp`), Wages App, BankApp, Employee Manager App, PropHunt's `PropHuntPhoneApp` (icon from `GetManifestResourceStream("PropHunt.Assets.phone_icon.png")`), and Sideload (HTML/CSS/JS → Unity UI on the phone).

**Comparison for our use case:**

| | Main-menu screen | Pause / options menu | Phone app |
|---|---|---|---|
| Looks native | **Best.** Cloning a real nav button inherits the exact font, sprite, hover state and layout — nothing to fake. | Good, same cloning technique available. | Good, but a phone app reads as a *tool*, not as a game option. |
| Robust across updates | **Good, if done SideHustle-style** (`MenuScreen.OpenOnStart` discovery + label-matched template + no Harmony). Fragile if done HonestMainMenu-style (hard-coded `Home/Bank`). | Unknown — no proven precedent found. | **Most churn-exposed.** `HomeScreen`, `PhoneApp`, and app-icon layout all move; S1API absorbs it for us but adds a hard dependency on S1API's own release cadence. |
| Easiest | **Easiest**, surprisingly: ~150 lines with no Harmony, no assets, no networking, and it runs before any save exists. | Medium (unproven). | Medium — S1API does the heavy lifting, but you're building panel content by hand. |
| Available when it matters | **Before a save loads** — which is exactly right for "which of these three mods is enabled", since our features are world simulation that must be decided at load time. | Only in-session. | Only in-session. |
| Verdict | **Primary surface.** | Skip. | **Secondary**, for free: name preferences `<Mod>_01_Main` and ModsApp / Prowiler's app render them without us writing anything. |

**[inference]** Concrete recommendation: build the toggle screen the SideHustle way — clone the Settings nav button, add one entry (e.g. "Roadmap Expansions"), open a cloned screen panel whose `Title` we relabel and whose rows are cloned from the vanilla settings rows. Back the toggles with `MelonPreferences` in three categories named `HireableDrivers_01_Main`, `PoliceImprovements_01_Main`, `SpecialCustomers_01_Main`, so the community settings apps pick them up for free and our screen is a *nicer front end on the same data* rather than a parallel system. **Do not ship an AssetBundle for this UI** — SideHustle, HonestMainMenu, Inkubator and Personify all build native-looking menu UI with zero bundles, and on IL2CPP `UnityEngine.AssetBundle.LoadFromMemory` is stripped anyway (§4.3). Consider also registering with SideHustle as an *optional* dependency so we appear in its hub when installed, since it costs ~10 lines and is a no-op when absent.

---

## 4. How mods survive updates in practice (community evidence)

### 4.1 Which updates broke what

- **0.3.6f6 → 0.4.0** was disruptive enough that a class/method diff was published as a machine-readable artefact: https://github.com/GuysWeForgotDre/S1DataMining/blob/main/diff.json — with a warning that *"the arguments were entered backwards, so the `Added` and `Removed` labels are reversed"* **[community]** (Cookbook `core-concepts/tracking-game-updates.md:12-24`, sourced to **OnlyMurdersSometimes**).
- **v0.4.1f6** removed `LandVehicle.onPlayerEnter` / `onPlayerExit`; the replacement is per-player `Player.onEnterVehicle` / `Player.onExitVehicle`, subscribed via `Player.onPlayerSpawned` so late joiners are caught. The community published a drop-in `VehicleEvents : Singleton<VehicleEvents>` replacement **[community]** (Cookbook `known-hook-points.md:80-130`, sourced to **Skippy**). **This one is directly on our path** — Hireable Drivers has to know when a driver enters a van.
- **0.4.6** *"dropped twelve assemblies: the HBAO, RadiantGI, Cinemachine, StylizedWaterForURP, StylizedGrass, Postprocessing, EasyFeedback and Boxophobic.Utils packages"*, which broke MelonLoader's interop generation for anyone **updating** from 0.4.5 or earlier — because Cpp2IL never cleans `MelonLoader/Dependencies/Il2CppAssemblyGenerator/Cpp2IL/cpp2il_out/`, so stale assemblies get merged with the fresh dump and `Pass11ComputeTypeSpecifics.ComputeSpecifics` throws an `NullReferenceException`. **Fix: delete `cpp2il_out` and relaunch.** *"This only hits you when updating from an older version. A fresh install has nothing stale to leave behind, which is why the same game version loads fine for one person and refuses for another."* **[community]** (Cookbook `tracking-game-updates.md:69-104`; the same `cpp2il_out` fix appears independently on the official wiki: https://s1modding.github.io/docs/modusers/troubleshooting/).
- To check whether your interop assemblies even match the installed game: compare `GameAssemblyHash` in `MelonLoader/Dependencies/Il2CppAssemblyGenerator/Config.cfg` against the SHA512 of `GameAssembly.dll` **[community]** (same page).
- **MelonLoader 0.7.1 is a known-bad build** for Schedule I; the Cookbook ships a `VersionChecker` helper that reflects the loaded MelonLoader assembly version and prints a loud console banner, hard-coding `0.7.1.0` as problematic and recommending `0.7.0` / `0.7.2-nightly` **[community]** (`best-practices/version-checks.md`, sourced to **Estonia**). **That recommendation has since moved on**: the 2026-08 DooDesch mods and Hotline all require **MelonLoader `0.7.3+`**, and NACops declares `[assembly: VerifyLoaderVersion("0.7.2", true)]` **[verified]**. Our project targets 0.7.x — worth confirming which point release.
- Newer **Unity 6000.x** builds are currently broken for MelonLoader generally (duplicate `<>O` type in `UnityEngine.CoreModule`), with a community tool `FixCoreModule` as workaround **[community]** https://github.com/LavaGang/MelonLoader/issues/1142 and /issues/1159. Not our problem today (we're on 2022.3.62f2) but it's the class of failure to expect if TVGS ever upgrades Unity.

### 4.2 Do mod pages state game-version compatibility? Yes — Thunderstore has a field for it.

**[verified]** Thunderstore Schedule I mod pages render an explicit `Game version:` line — NACops' page reads `Game version: 0.4.5f2 default` (and `default` is the branch, i.e. IL2CPP). README badges are the other common signal: SideHustle, Personnel and Hotline all carry `![Game](…Schedule%20I)` + `![MelonLoader](…0.7.3+)` + `![Status](…working)` badges and a Requirements table pinning `Schedule I | IL2CPP (current Steam public build)`. Nexus authors instead bake the version into the **title** — e.g. `(v1.3.8) Instant Operations`, `HUB - SmartEmployees v2.0.8` — and HazDS names the target in every description: *"A MelonLoader mod for Schedule 1 (v0.4.4f10)…"*, *"(v0.4.3f3)"*, *"(v0.4.5f1)"* **[community]**. Thunderstore package descriptions also carry compatibility notes like `"v0.4.0 (Cartel) and 0.3.6f6 compatible"` (Dre-DealersSendTexts) and `"Community-maintained AdvancedDealing fork for Schedule I 0.4.5f2: … save migration"` (FPZone) **[verified]** via API.

### 4.3 What modders actually changed / what the community advises

**Declare your backend so MelonLoader refuses cleanly instead of crashing.** The single highest-leverage habit:

```csharp
#if MONO
[assembly: MelonPlatformDomain(MelonPlatformDomainAttribute.CompatibleDomains.MONO)]
#else
[assembly: MelonPlatformDomain(MelonPlatformDomainAttribute.CompatibleDomains.IL2CPP)]
#endif
```
Without it, the user sees `Could not resolve type with token 0100001c from typeref (expected class 'Il2CppScheduleOne....'`; with it, MelonLoader prints a readable *"is incompatible: … only compatible with the following Domain: IL2CPP"* **[community]** (Cookbook `best-practices/branch-compatibility.md`, sourced to **k073l**). The official wiki gives the field diagnostic: *"Il2Cpp mods will usually error with `... Version=6.0.0` on Mono, while Mono mods will error with `... Version=0.0.0` on IL2CPP"* **[community]** https://s1modding.github.io/docs/modusers/troubleshooting/. The ecosystem also grew tooling for this: **OTCLoader** (*"auto-detects your game branch and disables incompatible mod DLLs before they crash"*, 18K downloads) and **SwapperPlugin** (80.9K downloads) **[verified]**.

**Guard against signature drift at the call site.** HonestMainMenu catches `MissingMethodException` on `LoadManager.StartGame` and retries the older 2-arg overload (§1.4). Personnel probes for a newer S1API method by reflection and silently degrades: *"The impostor builder API only exists in newer S1API builds, so this is best-effort via reflection: present -> pick a deterministic impostor, absent -> silently skip"* **[verified]**. Cartel Enforcer and NACops patch **all three** `Console.SubmitCommand` overloads because which one exists varies **[verified]**.

**Never let a failed patch take the mod down.** *"Authoring commands… A failed patch costs the console bridge, not the library, so the roster still loads either way. `try { HarmonyInstance.PatchAll(); } catch (Exception e) { Log.Warning("Console commands unavailable: " + e.Message); }`"* **[verified]** Personnel `Core.cs:36-38`. SideHustle's injector does the same: `catch { _injectedThisScene = true; /* don't spam every frame on a hard failure */ }`.

**Patch late, not at load.** *"Gameplay patches are applied lazily on the first gameplay scene… NOT here at load. Patching the game's gameplay methods while the Side Hustle hub builds its menu UI intermittently hard-crashes the game"* **[verified]** PropHunt `Core.cs:33-34`.

**Build against a pinned reference assembly, not the live game.** `RefGen` produces per-version reference-assembly NuGet packages so *"building against an older game version is just a version bump"* — e.g. `<PackageReference Include="RefGen.Schedule-I.Il2Cpp" Version="0.4.4-f8" />` alongside `HarmonyX 2.10.2` and `LavaGang.MelonLoader 0.7.2` **[community]** (Cookbook, sourced to **k073l**). `CodeArchiver` captures **stripped** game code per branch so an update reads as a clean Git diff **[community]** — ★8, last push 2026-08-01, so it is actively maintained.

**Don't reference the game directly if a library can absorb the change.** *"Shielding from game-version churn. When an update renames or moves the game's internals, the library absorbs the change so your mod keeps working with little or no edit."* **[community]** Cookbook `frameworks/index.md:19-21`. Corollary: reference the library with `<Private>false</Private>` (or NuGet) and **never bundle your own copy** — *"Bundling your own copy risks loading a second, mismatched version of the assembly"* **[community]** same page.

**IL2CPP-specific stripping is a live hazard, and it bites exactly where you'd expect.** Two independent mods documented that the *managed* AssetBundle wrappers are stripped from this build:

```csharp
// ScheduleOne-Sideload/Paint/BoxMaterial.cs:37-39
// NOT UnityEngine.AssetBundle.LoadFromMemory: that method is stripped from this IL2CPP build and
// throws "Method unstripping failed". MelonLoader's manager goes straight to the native icall.
Il2CppAssetBundle bundle = Il2CppAssetBundleManager.LoadFromMemory(bytes);
```
```csharp
// ScheduleOne-PropHunt/Disguise/OutlineShader.cs:14-19
/// IL2CPP loading note: the managed AssetBundle.LoadFromMemory AND LoadAllAssets
/// wrappers are stripped from the game binary ("Method unstripping failed"), so we go through
/// S1API's AssetLoader, whose Il2CppAssetBundle reaches both the load and the asset
/// extraction via native ICalls - the only path that actually works here.
```
Also from PropHunt: *"IL2CPP cannot compile ShaderLab at runtime, so a precompiled bundle is the only path"*, and the bundle must be *"built once in a 2022.3 editor"* (matching our Unity 2022.3.62f2) and as the `il2cppassetbundle` type **[verified]** / **[community]**. Embedded-resource naming convention: `"<RootNamespace>.<folder path with dots>.<file>"`, e.g. `"PropHunt.Assets.Bundles.propoutline"`.
**[inference]** This corroborates our own decision log ("Never call GUI.DrawTexture on this build — stripped") and generalises it: on this IL2CPP build, assume any *managed convenience wrapper* over a native Unity API may be stripped, and prefer either the game's own live objects or MelonLoader's native-ICall managers.

**Community advice to users, which shapes how we should publish.** From an aggregator that summarises the current consensus **[community]** https://www.switchbladegaming.com/schedule-i/best-mods/: check each mod's Posts/Bugs tab after a patch (*"Community members typically flag broken mods within hours"*); **delete** the old `.dll` before dropping the new one rather than overwriting; remove a broken mod entirely rather than running it (*"A broken mod loading at startup can cause crashes or, in rare cases, save corruption"*); and treat any patch note mentioning *"scripting changes"* or *"backend changes"* as a red flag. **[inference]** For us: ship a `CHANGELOG.md` and state the exact tested game version + MelonLoader version on both the Thunderstore page and the README, the way every actively-maintained mod in this survey does.

**Security scanning is now normal.** `MLVScan` (ifBars) is a MelonLoader plugin with 20K downloads and rating 9 that *"detect[s] and disable[s] potentially malicious mods before they can harm your system"*, and ModsApp's README displays an `MLVScan` attestation badge with a CI workflow (`.github/workflows/mlvscan.yml`) **[verified]**. **[inference]** If our mods do anything that looks like file or network I/O, expect to need an attestation or we'll be auto-disabled on some users' installs.

---

## Sources (A2)

**Indexes / APIs (first-hand, current as of 2026-08-03)**
- https://thunderstore.io/c/schedule-i/api/v1/package/ — complete Schedule I package index, 494 entries incl. deprecated. **Authoritative, current.**
- https://thunderstore.io/c/schedule-i/ — community landing page; category taxonomy, "213 results". **Authoritative, current.**
- https://api.github.com/orgs/DooDesch-Mods/repos — 41 repos with licences and push dates. **Authoritative, current.**
- https://api.github.com/repos/{owner}/{name} — licence + last-push for all 26 repos cited. **Authoritative, current.**
- https://api.github.com/search/repositories?q=topic:schedule-1 / topic:schedule-i / topic:scheduleone / topic:schedule1 / "Schedule I" MelonLoader mod — 18/20/4/12/21 results. **Authoritative, current; topic coverage is sparse so treat as a supplement, not a census.**
- https://www.nexusmods.com/schedule1/mods/top — **cached April-2025 snapshot**, login-walled. Low recency; used only for mod names/IDs.
- https://next.nexusmods.com/games/schedule1 · https://www.nexusmods.com/schedule1/about/stats — inconsistent counters across snapshots. **Unreliable for totals.**

**Cloned repos read first-hand (21)** — all read at `--depth 1` HEAD on 2026-08-03
- https://github.com/DooDesch-Mods/ScheduleOne-SideHustle — MIT, pushed 2026-08-03. **Primary source for main-menu injection.** Highest trust.
- https://github.com/DooDesch-Mods/ScheduleOne-Personnel — MIT, 2026-08-02. **Primary source for custom NPCs.** Highest trust.
- https://github.com/DooDesch-Mods/ScheduleOne-Cookbook — NOASSERTION, 2026-08-01. Curated Discord knowledge; every claim carries a Discord permalink. High trust, second-hand by nature.
- https://github.com/DooDesch-Mods/ScheduleOne-Hotline — MIT, 2026-08-03. Overlay framework + `Hotline.Api` shim.
- https://github.com/DooDesch-Mods/ScheduleOne-PropHunt — MIT, 2026-08-03. MP gamemode + late-patching lesson.
- https://github.com/DooDesch-Mods/ScheduleOne-Personify — MIT, 2026-08-02. Menu-scene overlay + NPC authoring.
- https://github.com/DooDesch-Mods/ScheduleOne-Inkubator — MIT, 2026-08-02. Menu-scene overlay.
- https://github.com/DooDesch-Mods/ScheduleOne-LooseEnds — MIT, 2026-08-02. `NPCHealth` patch targets.
- https://github.com/DooDesch-Mods/ScheduleOne-Snitch — MIT, 2026-08-02. `Hotline.Api` consumption + profiling.
- https://github.com/DooDesch-Mods/ScheduleOne-Sideload — MIT, 2026-08-02. `Il2CppAssetBundleManager` stripping note.
- https://github.com/DooDesch-Mods/ScheduleOne-FullHouse — MIT, 2026-08-03. Lobby cap.
- https://github.com/RoachxD/ScheduleOne.HonestMainMenu — MIT, 2026-01-10. **Source of the verbatim menu scene/object names.** High trust, slightly stale (Jan 2026).
- https://github.com/k073l/s1-modsapp — MIT, 2026-08-02. **The settings framework + its `PREFERENCES.md` dev guide.** Highest trust.
- https://github.com/XOWithSauce/schedule-nacops — **no licence**, 2026-08-03. **Primary source for police internals + runtime networked NPC spawn.** Highest trust technically; *legally read-only.*
- https://github.com/XOWithSauce/schedule-cartelenforcer — MIT, 2026-08-03. Hostile-faction events, `[RegisterTypeInIl2Cpp]` quest subclassing.
- https://github.com/hdlmrell/OTC-S1-Mod — NOASSERTION, 2026-04-14. **Primary source for `NetworkHelper.IsHost` + customer/phone patch targets.** *Treat as all-rights-reserved.*
- https://github.com/ifBars/S1API — MIT, 2026-08-03, ★22. **Our dependency.** `NPCPrefabBuilder` API + ~90 patch targets. Highest trust.
- https://github.com/surrealnirvana/LawEnforcementEnhancementMod — **no licence**, 2025-04-21. Stale; `[RegisterTypeInIl2Cpp]` + `ServerManager.Spawn` + JSON-config patterns only.
- https://github.com/DevKaiE/DealerTransportMod — **no licence**, 2025-04-23. Stale; storage/NPC patch targets.
- https://github.com/Babyhamsta/HardcorePoliceMod-Schedule1 — Apache-2.0, 2025-04-15, ★8. Stale; manual `harmony.Patch` style.
- https://github.com/HazDS/S1Mods — **no licence**, 2026-03-27. Releases only, no `.cs`; used for descriptions.

**Mod pages fetched first-hand**
- https://thunderstore.io/c/schedule-i/p/bwyan/HireableDeliveryDriver_IL2CPP_port/ — **the host-only MP quote.** Current, but no source repo. Medium trust (author claim).
- https://thunderstore.io/c/schedule-i/p/XO_WithSauce/NACops_IL2CPP/ — `Game version: 0.4.5f2 default`, full feature + config docs. Current, corroborated by source.

**Documentation**
- https://fish-networking.gitbook.io/docs/guides/features/network-communication/remote-procedure-calls.md — `ServerRpc` / `ObserversRpc` / `TargetRpc` / `Channel` / `RunLocally` / `DataLength`, verbatim. Current, but **v4 docs while the game runs v3**.
- https://fish-networking.gitbook.io/docs/guides/features/network-communication/synchronizing.md — SyncTypes. Same v3/v4 caveat.
- https://fish-networking.gitbook.io/docs/guides/features/network-communication/broadcasts.md — Broadcasts. Same caveat.
- https://web.archive.org/web/20240324100202/https://fish-networking.gitbook.io/docs/ — **archived FishNet v3 docs**, i.e. the version the game actually uses. Recommended by the Cookbook. Not fetched by me; cited on the Cookbook's authority.
- https://s1modding.github.io/docs/ — Schedule I Modding Wiki. Live. `/docs/modusers/troubleshooting/` (branch-mismatch signatures, `cpp2il_out` fix), `/docs/moddevs/melonloader_utilities/`, `/docs/moddevs/reading_game_code/`, `/docs/moddevs/il2cpp/#supporting-both-branches`.
- https://melonwiki.xyz/#/modders/preferences — MelonPreferences reference. Live.
- https://harmony.pardeike.net/ — HarmonyX docs. Live.
- https://partner.steamgames.com/doc/api — Steamworks lobby data limits. Live.
- https://discord.gg/9Z5RKEYSzq — Unofficial Schedule One Modding Server. **The primary knowledge source in this ecosystem; I did not join it.** Every Cookbook claim carries a permalink into it — the biggest remaining coverage gap in this report.

**Tooling / infrastructure**
- https://github.com/k073l/s1-codearchiver — cross-version stripped-code diffing, ★8, 2026-08-01, no licence.
- https://github.com/k073l/RefGen — per-version reference-assembly NuGet packages, MIT, 2026-03-31.
- https://github.com/k073l/S1MelonModTemplate — dual IL2CPP/Mono template, MIT, ★3, 2026-04-11.
- https://github.com/GuysWeForgotDre/S1DataMining/blob/main/diff.json — 0.3.6f6→0.4.0 diff. **Added/Removed labels are reversed.** Not fetched by me.
- https://github.com/ifBars/SteamNetworkLib — MIT, 2026-07-28. The recommended IL2CPP sync path.
- https://github.com/ifBars/bGUI — MIT, 2026-06-26. Runtime uGUI builder.
- https://github.com/ifBars/S1MAPI — **GPL-3.0**. Procedural mesh / glTF. Licence-encumbered for us.
- https://github.com/LavaGang/UnityEngine.Il2CppAssetBundleManager — bundled with MelonLoader; required on IL2CPP.
- https://github.com/xmusjackson/UnityEngine.BE.Il2CppAssetBundleManager — BepInEx port.
- https://www.nuget.org/packages/FishNetV3.CodeGenerator.MSBuild — `1.0.0-beta.11`. **Mono only.**
- https://github.com/LavaGang/MelonLoader/issues/1142 · /issues/1159 — Unity 6000.x breakage. Current, not our version yet.
- https://github.com/V1ndicate1/FixCoreModule — duplicate-`<>O` workaround, MIT.
- https://thunderstore.io/c/schedule-i/p/ifBars/MLVScan/ · https://thunderstore.io/c/schedule-i/p/hdlmrell/OTCLoader/ · https://thunderstore.io/c/schedule-i/p/ebkr/r2modman/ · https://thunderstore.io/c/schedule-i/p/Kesomannen/GaleModManager/ — security scanner, branch guard, and the two dominant mod managers.
- https://www.nexusmods.com/schedule1/mods/1750 (SIMM) · /397 (Prowiler Mod Manager & Phone App) · /58 (LethalLizard Mod Manager) · /1194 (S1API on Nexus) · /1138 (Lithium) · /1202 (EvenMoreFootPatrols) · /1584 (WarehousePlus) — Nexus IDs referenced above. **Login-walled / cached; names and IDs only, low recency.**
- https://www.switchbladegaming.com/schedule-i/best-mods/ — third-party aggregator summarising current user-side update advice. **Low-to-medium trust (SEO blog), but its advice matches the wiki.**
- https://sidehustle.doodesch.de · https://support.doodesch.de/sidehustle — SideHustle live lobby browser + support. Author-operated; not independently verified.

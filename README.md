# Schedule I Expansions

Four mods for **Schedule I** (IL2CPP / Steam) that rebuild three features the
game voted on but never shipped, plus the shared library they run on. The goal
is that they feel like part of the game rather than something bolted on: native
menu screens built from the game's own UI prefabs, the game's own dialogue and
clipboard systems for interaction, and the game's own economy for payouts.

Built on [MelonLoader](https://melonwiki.xyz/) 0.7.3 and
[S1API (Forked)](https://github.com/ifBars/S1API) 3.1.7.

> **Status: playable, not proven.** Everything below compiles clean and is
> statically verified against the game's real IL2CPP metadata, but most of it
> has had one playtest round or none. See [Testing status](#testing-status)
> before you install this over a save you care about.

---

## The suite

| Mod | Installs to | Version | What it does |
|---|---|---|---|
| **Expansions Core** | `UserLibs/` | 0.1.0 | Shared library. Module registry and lifecycle, reversible enable/disable, config, the native Expansions menu, the actions surface, the tutorial director and the probe harness. Not a mod on its own — the other three need it. |
| **Hireable Drivers** | `Mods/` | 0.4.0 | Hire a driver from the employee fixer's own dialogue, one per property. They move items between properties, businesses and dealers on the shipped Packager clipboard's five route rows. Drives real cars on the game's A\* road graph where the vehicle supports it, and falls back to a timed relay where it doesn't. |
| **Police Improvements** | `Mods/` | 0.2.0 | A persistent 0–100 heat scale that survives sleep and reload, mapped onto five intensity tiers that are *added* to the game's own baseline, so heat 0 is exactly vanilla. Tier-scaled fines, product seizure, a latching outlaw status, and federal agents. |
| **Special Customers** | `Mods/` | 1.0.0 | Customer groups — bikers, businessmen, a caravan, a cult — arrive in town every few in-game days, text you a bulk contract, and pay out through the game's real handover pipeline. Four archetypes off an eight-NPC reusable pool. |

They share one **Expansions** screen, reachable from the main-menu nav column or
**F7** in-game. Each mod can be toggled on and off live, in both directions,
without restarting.

### What that buys you, concretely

- 46+ menu actions, 38+ runtime probes, 83+ config keys, a 6-chapter in-game
  tutorial.
- Config at `UserData/Expansions.cfg`, in the `<ModName>_01_Main` convention
  that third-party mod-settings apps auto-detect.
- Disabling a module genuinely unwinds it — per-module Harmony instances are
  unpatched and tracked objects disposed in reverse order.

---

## Install

Grab the zip from the [latest release](https://github.com/twsevenyw/ScheduleI-Expansions/releases/latest)
and merge it over your Schedule I folder.

**`INSTALL.txt` inside the zip is the real install guide.** It is written to be
pasted into an AI assistant and walked through step by step, so it works if
you have never installed a mod before.

The zip mirrors the game folder layout:

```
INSTALL.txt
FEATURES.txt
LICENSE-S1API.txt
Mods/Expansions.HireableDrivers.dll
Mods/Expansions.PoliceOverhaul.dll
Mods/Expansions.SpecialCustomers.dll
Mods/S1API.Il2Cpp.MelonLoader.dll
Plugins/S1APILoader.MelonLoader.dll
UserLibs/Expansions.Core.dll
```

MelonLoader is **not** bundled — it patches the game and you should install it
yourself from the official source. S1API is bundled, unmodified, under MIT; see
`LICENSE-S1API.txt`.

### One rule that matters

**Disable, don't delete.** Special Customers creates NPCs that get written into
your save. Deleting the DLL orphans their folders inside the save; disabling the
module stops everything it does and leaves the save coherent. To disable a mod
outside the game, rename `Foo.dll` to `Foo.dll.disabled`.

---

## Testing status

Being honest about this, because the mods touch saves.

| Area | State |
|---|---|
| Compilation | Both solutions build at **0 errors, 0 warnings**. Every game and S1API symbol is metadata-checked against the real IL2CPP assemblies. |
| Load safety | `Expansions.Core` holds **zero** compile-time references to `Assembly-CSharp` or FishNet. Everything game-side is late-bound, so a renamed symbol after a game patch degrades to a logged warning instead of a load-time crash that would take all four mods down. |
| Special Customers | The custom NPC (Marcus Vale) is confirmed live in-game: registered with the game's own `NPCManager`, GUID, mugshot, correct region. The full arrive → offer → sell → depart loop is **not** yet confirmed end to end. |
| Tutorial | Verified in-game end to end — all chapters completed as real quests with persisted progress. |
| Hireable Drivers | Fully implemented, source-verified, **never run in a live game** since the rework to native clipboard hiring. |
| Police Improvements | Fully implemented, source-verified, **never run in a live game**. |
| Playtest round 1 fixes | Six fixes plus the entire Drivers rework are source-verified only. Nothing has executed. |

Known open issues are tracked in `CONTEXT.md` under "Open items". The
short version: Marcus Vale wanders off his post, and cross-property route
save round-tripping is unconfirmed.

Game version: built and checked against **0.4.6f11**. The Il2Cpp interop
assemblies are a projection of `GameAssembly.dll`, so a game patch means a
rebuild — see [docs/RELEASING.md](docs/RELEASING.md).

---

## Building from source

You need Schedule I installed, MelonLoader 0.7.3+, and the game launched at
least once so MelonLoader has generated
`<game>\MelonLoader\Il2CppAssemblies\`. The projects reference those assemblies
directly; without them nothing compiles. That is also why this repo has no CI
build — the reasoning is in [docs/RELEASING.md](docs/RELEASING.md).

```powershell
dotnet build src\Expansions.sln -c Release
```

Output deploys straight into the game (`Expansions.Core.dll` → `UserLibs\`, the
three mods → `Mods\`). Close the game first or the copy is skipped.

If your Steam library is not at
`C:\Program Files (x86)\Steam\steamapps\common\Schedule I`, edit `GameDir` in
`src\Directory.Build.props`.

### Layout

| Path | What |
|---|---|
| `src/Expansions.sln` | The suite. Four projects. |
| `src/Expansions.Core/` | Shared library → `UserLibs`. |
| `src/Expansions.HireableDrivers/` | → `Mods`, module id `hireable_drivers`. |
| `src/Expansions.PoliceOverhaul/` | → `Mods`, module id `police_overhaul`. |
| `src/Expansions.SpecialCustomers/` | → `Mods`, module id `special_customers`. |
| `scripts/publish.ps1` | Build, package, tag and publish a release. |
| `docs/` | Release runbook and the update-manifest contract. |
| `research/` | Hand-written API references for the game's IL2CPP surface. |
| `research-ext/` | External research: design intent, modding ecosystem, UI style spec. |
| `CreativeModeMod.cs` etc. | Creative Mode — see below. |
| `FEATURES.txt` | Full plain-text feature reference for the whole suite. |

The `research/` and `research-ext/` markdown is the most reusable thing here for
other Schedule I modders — several thousand lines of verified API notes covering
NPCs, police, economy, employees, property, persistence, vehicles, the UI and
main menu, plus a measured spec for matching the game's UI style. The bulk
IL2CPP dumps those were written from are not committed; they are regenerable
with the tooling described in `research-ext/ASSETS-TOOLCHAIN.md` and
`research/probe/`.

---

## Creative Mode

A separate, older mod that lives in this repo: a Minecraft-style creative
toolbox for single-player. It is **not** part of the distributable and is never
included in a release — it is a personal cheat mod, kept here as source.

Open with **F8**; Esc closes. The mouse unlocks while it is open.

| Tab | What |
|---|---|
| Items | Filter chips, paged item list, quantity buttons, clear inventory |
| Money | Cash and bank quick amounts, +$1k to +$1M |
| Player | Health, wanted level, XP, move/jump multipliers |
| Unlock | Individual properties and businesses, or unlock everything |
| NPCs | Unlock suppliers and NPCs, set relationships 0–5, bulk unlock and max |
| Quests | Complete individual story quests, or all listed |
| World | Save, grow plants, clear trash, time-scale presets (pause to 10×), time of day, vehicles, freecam |

Load a save before spawning items — the item registry is empty on the main menu.
It uses the same underlying systems as the built-in console (`give`,
`changecash`) via S1API's `ConsoleHelper`.

Build it with `dotnet build -c Release` from the repo root; it auto-copies
`CreativeMode.dll` into `Mods\`.

> Schedule I's IL2CPP build strips `GUILayout.TextField` and breaks
> `GUI.DrawTexture`, so this mod uses absolute-rect buttons instead of text
> boxes. That is a workaround, not a style choice.

---

## Releasing

Releases are cut from the maintainer's machine with `scripts/publish.ps1`,
because a GitHub runner cannot compile against the game's interop assemblies.
CI verifies published releases rather than building them.

Full runbook: [docs/RELEASING.md](docs/RELEASING.md).
Update-manifest contract for the in-game updater:
[docs/UPDATE-MANIFEST.md](docs/UPDATE-MANIFEST.md).

---

## Credits and licensing

- [MelonLoader](https://melonwiki.xyz/) — the mod loader. Not bundled.
- [S1API (Forked)](https://github.com/ifBars/S1API) by ifBars — MIT, bundled
  unmodified with its notice in `LICENSE-S1API.txt`.
- Schedule I is by TVGS. This project is an unofficial fan mod, not affiliated
  with or endorsed by them. No game assets are redistributed.

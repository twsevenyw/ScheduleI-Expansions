# PLAN-POLICE-IMPROVEMENTS.md — buildable engineering plan

Target: `src/Expansions.PoliceOverhaul/` (module id `police_overhaul`, displays as **Police Improvements**).
Feature brief (Community Vote #3, verbatim): *"Dynamic police intensity, federal agents, outlaw status, and
heavier consequences when caught."*

**Sources of truth.** Every game symbol cited here is copied from `research/API-POLICE.md`,
`research/API-NPCS.md`, `research/API-ECONOMY.md`, `research/API-PERSISTENCE-TIME.md`,
`research/API-NETWORKING-CONSOLE.md`, `research/API-VEHICLES.md`, `research/API-S1API.md`,
`research-ext/DESIGN-INTENT.md`, `research-ext/MODDING-ECOSYSTEM.md`, `research-ext/CUSTOM-CHARACTERS.md`.
Anything not in those docs is marked **UNVERIFIED** with a runtime verification recipe in §11.
Nothing here was tested against a running game.

**Notation.** `[V]` = verified in a dump. `[I]` = inferred. `[U]` = unverified, needs a runtime probe.
Il2Cpp-prefixed names are what Harmony and C# see. Fallback names (`Method_Protected_Virtual_Void_0`,
`field_Private_Boolean_0`) are **never** bound in shipping code — they renumber between game versions.

---

## 0. The one-paragraph design

The game has no heat system, no persistent law reputation, and no jail. What it *does* have is a
complete, designer-authored, minute-ticked law scheduler (`LawController` → 7 × `LawActivitySettings` →
`PatrolInstance`/`CheckpointInstance`/`SentryInstance`/`VehiclePatrolInstance`/`CurfewInstance`), every
entry of which gates itself on a single integer, `LawController.LE_Intensity`, compared against its own
`IntensityRequirement`. **So we do not build a parallel police spawner. We build a persistent per-player
`Heat` scalar, map it onto `LE_Intensity` plus a small set of global tuning statics, and let the game's own
scheduler put more cops on the street.** Federal agents are the one thing the scheduler can't express, so
they are a mod-owned, tagged, pooled-off-the-books `PoliceOfficer` instance. Outlaw status is a latched
mod-owned state that re-skins the *rules* (fines, confiscation scope, search outcomes, service access)
rather than the world. Consequences hook the arrest pipeline where the game already removes items
(`ArrestNoticeScreen.ConfiscateItems`) and add the one thing the shipped economy actually fears: a hit to
the **online balance**, which is capped at $10,000/week of deposits and $20,000/24 h of laundering, versus
cash, which is abundant.

---

## 1. Ground rules

### 1.1 Authority

Every world mutation is gated behind:

```csharp
internal static bool IsServerOrSolo =>
    Il2CppFishNet.InstanceFinder.NetworkManager == null || Il2CppFishNet.InstanceFinder.IsServer;
```

`[V]` — `research/API-NETWORKING-CONSOLE.md:378`. The null check is the important half: `IsServer` is false
before the network object exists. Do **not** use `InstanceFinder.IsHost` (false for a dedicated server and
before the network starts) or `Lobby.Instance.IsHost` (false for a solo game with no lobby).

Reinforcing evidence that the police pipeline is host-only from step 5 onward: the game's own log literals
`Attempted to dispatch officers from a client, this is not allowed.` and
`Attempted to deactivate an officer on the client` `[V]`. Clients run **only** the HUD read-out and the
config UI. Multiplayer risk for this feature is **medium** — it mostly tunes existing host state — but the
federal-agent spawn (§3) is the part that can actually desync, so it is last in the build order.

### 1.2 Patching discipline

- Patch **only** through the injected `ExpansionModule.Harmony` instance. `[assembly: HarmonyDontPatchAll]`
  is already on the mod entry class, so `[HarmonyPatch]` attributes do nothing; that is deliberate, because
  MelonLoader's own `PatchAll` runs under *its* Harmony id and would survive `UnpatchSelf()`.
- Patch classes are plain `static` holders with plain `static Prefix`/`Postfix` methods, resolved with
  `AccessTools.Method(...)` and applied explicitly in one `ApplyPatches()` call.
- **Apply patches on first gameplay scene, not at mod load.** `ExpansionModule.OnEnabled` can run before
  `Assembly-CSharp` types are ready. Latch on `OnSceneLoaded(buildIndex: 1, "Main")`, guard with a
  `_patched` bool, reset that bool from `Lifetime.OnDispose`.
- Wrap `ApplyPatches()` in try/catch. A single missing method must not take the whole module down —
  `ModuleContext.ReportPhaseError` already auto-disables after 10 throws, which is a worse outcome than one
  degraded feature.
- Prefer no-patch subscription points wherever one exists (see §6.3). Fewer patches = fewer collisions =
  cleaner unwind.

### 1.3 Never subclass a game `NetworkBehaviour`

FishNet's code generator does not run against mod assemblies on IL2CPP, so a derived `PoliceOfficer` has
no RPC links, no serializers, and garbage (not empty) internal dictionaries `[C]`. Every officer we create
is an **instance of the game's own type**, obtained by instantiating the registered prefab (§3.3).

### 1.4 What we never touch

- `Il2CppScheduleOne.PlayerScripts.Health.PlayerHealth.TakeDamage` — S1API has a **skipping Prefix** there
  (`PlayerPatches`) `[V]`. If we want a federal firefight to hurt more, we tune the *weapon*, not the damage
  handler.
- `Il2CppScheduleOne.NPCs.NPCHealth.Awake` / `.Load` / `.Revive` — S1API has **verified skipping prefixes**
  on all three `[V]`. Officer health is set by direct field write on our own spawned instance, post-`Awake`,
  with no patch at all.
- `Il2CppScheduleOne.Persistence.Datas.LawData` / `LawController.GetSaveString()` — `Law.json` holds exactly
  one float (`InternalLawIntensity`) `[V]`. We never write `internalLawIntensity` and never extend `LawData`.
  Disabling the module must leave the vanilla save byte-identical in that file.

---

## 2. Pillar 1 — Dynamic police intensity

### 2.1 The state

`Heat` — a **float 0–100, per player, persisted across days and across saves.** It has to survive sleep or
it is just a rename of the shipped wanted level, which is wiped by sleeping (`PlayerCrimeData.OnSleepStart`,
plus the v0.1.x patch note "Wanted level is now cleared when you sleep") `[V]`.

`Heat` is **not** `LawController.internalLawIntensity`. That float is the game's own slow rank-shaped curve
and lives in `Law.json`; we read it, we never write it. Keeping them separate is what makes the module
cleanly removable.

### 2.2 What drives Heat

| Input | Source | Amount | Anchor |
|---|---|---|---|
| Crime recorded | Postfix `PlayerCrimeData.AddCrime(Crime, Int32)` `[V]` | `fine ÷ 5 × quantity` | Uses the shipped penalty table as the game's own severity ranking. Fines are read at runtime from `PenaltyHandler.*_FINE` statics (§2.6), so we self-calibrate to whatever the current build charges. |
| Arrest | `Player.onArrested` (`Il2CppSystem.Action`) `[V]` | `+15` | ≈ the `ASSAULT_FINE` tier. |
| Deal completed | Postfix `Il2CppScheduleOne.Quests.Contract.SubmitPayment(System.Single)` `[V]` | `+payment / 2000`, capped `+15 / in-game day` | $2,000 is the smallest shipped business launder cap; 15/day ≈ one Assault-tier event of "you are moving volume". |
| Curfew multiplier | `CurfewManager.IsCurrentlyActive` `[V]` | crime heat `× 1.5` | Curfew is the one shipped player-only law; being caught during it is already the second-highest fine ($100). |
| Region multiplier | `Il2CppScheduleOne.Map.Map.GetRegionFromPosition(Vector3)` → `EMapRegion` `[V]` | Uptown/Suburbia `× 1.25`, Downtown/Westville `× 1.0`, Northtown/Docks `× 0.85` | Mirrors the shipped per-region customer budget tiers (DESIGN-INTENT §9.3) — richer districts complain louder. |
| Sleep decay | `TimeManager.onSleepEnd` (`Il2CppSystem.Action`) `[V]` | `−(10 − minutesCleanToday × 0.004)`, floored at 0 | The anchored −10/slept day (DESIGN-INTENT §14.2), *minus* whatever already drained live. |
| Live idle drain | Postfix `LawController.OnUncappedMinPass()` `[V]` | `−0.004/min` while `CurrentPursuitLevel == None` and `Crimes.Count == 0` | ≈ −5 across a clean 21-hour day, so the HUD visibly rewards lying low. Combined clean-day total is still exactly −10. |

All heat writes go through one method so the master scalar and clamping happen in one place.

### 2.3 Heat → tier → world

Five tiers, taken from DESIGN-INTENT §14.2 and pinned to the one dynamic primitive TVGS actually shipped in
v0.4.6: *"Police patrols, sentries, checkpoints now assign 1-2 officers instead of 2 all the time."* Our job
is to **widen that shipped 1–2 band to 1–4**, not to invent a parallel spawner.

| Tier | Heat | `LE_Intensity` contribution | Officer band per post | Checkpoint windows | Detection | Body-search × |
|---|---|---|---|---|---|---|
| T0 Calm | 0–19 | +0 … +1 | Min 1 / Max 1 | shipped | 1.00 / 1.00 | 1.00 |
| T1 Alert | 20–39 | +2 … +3 | Min 1 / Max 2 *(vanilla)* | shipped | 1.10 / 1.10 | 1.25 |
| T2 Crackdown | 40–59 | +3 … +4 | Min 2 / Max 3 | widened ±2 h | 1.25 / 1.25 | 1.50 |
| T3 Task Force | 60–79 | +5 … +6 | Min 2 / Max 4 | all daylight hours | 1.50 / 1.50 | 2.00 |
| T4 Federal | 80–100 | +7 … +8 | Min 3 / Max 4 | all hours | 1.75 / 2.00 | 2.50 |

Detection column is `VisionCone.UniversalAttentivenessScale` / `UniversalMemoryScale`.
4 is a **hard runtime cap** the game enforces itself — log literal
`Attempted to dispatch more than 4 officers, this is not allowed.` `[V]`. Never request more.

The intensity write, every game minute:

```csharp
// Postfix on Il2CppScheduleOne.Law.LawController.OnUncappedMinPass()
int contribution = Mathf.RoundToInt(heat / 12.5f * masterScalar);      // 0..8
int target       = Mathf.Clamp(_dayBaseline + contribution, 1, 10);    // S1API: MinIntensity 1, MaxIntensity 10
if (lc.LE_Intensity != target)
{
    lc.LE_Intensity = target;
    lc.CurrentSettings.Evaluate();   // take effect this minute, not next day
}
```

`_dayBaseline` is captured in a Postfix on `LawController.DayPass()` — i.e. **after** the game has run its own
`IntensityIncreasePerDay` / `DAILY_INTENSITY_DRAIN` arithmetic — plus once on enable. That way the vanilla
rank-shaped curve stays the floor and Heat only ever adds. We own `LE_Intensity` while enabled and restore
the snapshot on disable; we never write `internalLawIntensity`, so `Law.json` is untouched.

### 2.4 The systems it actually modulates (real members)

**A. Patrol / sentry / checkpoint / curfew density — via the game's own scheduler.**
Raising `LE_Intensity` makes more `*Instance` objects clear their `IntensityRequirement` and start
themselves on the next `MinPass()`. Zero extra code. This alone covers "more cops at higher heat".

**B. Officers per post — via the schedule data.** These are plain writable ints on the instances hanging off
`LawController.{Monday..Sunday}Settings` and `.CurrentSettings` `[V]`:

| Object | Members we write | Note |
|---|---|---|
| `Il2CppScheduleOne.Law.PatrolInstance` | `MinMembers`, `MaxMembers` | `LawManager.StartFootpatrol(FootPatrolRoute, Int32 requestedMembers)` consumes them |
| `Il2CppScheduleOne.Law.SentryInstance` | `MinMembers`, `MaxMembers` | |
| `Il2CppScheduleOne.Law.CheckpointInstance` | `MinMembers`, `MaxMembers`, `StartTime`, `EndTime` | `SetCheckpointEnabled(loc, true, requestedOfficers)` |
| `Il2CppScheduleOne.Law.VehiclePatrolInstance` | (read only — has no member count) | eligibility comes from `IntensityRequirement` alone |
| `Il2CppScheduleOne.Law.CurfewInstance` | (untouched) | let intensity turn curfew on for nights it would otherwise skip |

**Do not write `IntensityRequirement`.** That is the designer's difficulty ladder; driving `LE_Intensity`
against it is the intended mechanism. Writing both makes the system unreadable and un-restorable.

**Snapshot discipline (load-bearing).** These instances are shared designer objects reachable from all seven
day settings. Before the first write, deep-snapshot every `(instance, field) → original` pair into a
`Dictionary`; restore it in `Lifetime.OnDispose` and then call `LawController.Instance.CurrentSettings.Evaluate()`
so the game re-derives the correct world state. Snapshot keyed on the object reference, taken once per
scene load, discarded on scene unload.

**C. Detection sensitivity — two global scalars, no patch:**
`Il2CppScheduleOne.Vision.VisionCone.UniversalAttentivenessScale` and `.UniversalMemoryScale` `[V]`.
By naming style (PascalCase, not SCREAMING_CASE) these are the most likely of the tuning statics to be real
mutable fields rather than inlined consts `[I]` — but see risk R1.

**D. Response size and mode — three statics on `LawManager` `[V]`:**
`OfficerDispatchMin`, `OfficerDispatchMax`, `DISPATCH_VEHICLE_USE_THRESHOLD`. Lowering the vehicle threshold
at T3+ is how cruisers appear on the road, which matches the shipped rank curve ("you'll begin to see cars
and more patrol officers"). Belt-and-braces: a Prefix on
`Il2CppScheduleOne.Map.PoliceStation.Dispatch(Int32, Player, EDispatchType, Boolean)` that clamps
`__0` (requested count) into the tier band — this works even if the statics turn out to be inlined consts.

**E. Search persistence and body-search rate:**
`PoliceOfficer.BODY_SEARCH_CHANCE_DEFAULT` (global) plus a per-instance sweep over
`PoliceOfficer.Officers` writing `officer.BodySearchChance` `[V]`, and
`Il2CppScheduleOne.NPCs.Behaviour.SentryBehaviour.BodySearchChance`. The per-instance sweep is the reliable
path — it is a plain instance property and cannot have been const-inlined.
`PlayerCrimeData.SEARCH_TIME_*` and `ESCALATION_TIME_*` get a tier multiplier for how long cops keep looking
after losing you.

**F. Checkpoint frequency:** handled entirely by (B) — widening `CheckpointInstance.StartTime`/`EndTime` and
raising `LE_Intensity`. The game's own `CheckpointInstance.MinPass()` / `Evaluate()` /
`DistanceRequirementsMet()` then opens and closes them, including the shipped
`MIN_ACTIVATION_DISTANCE` courtesy check that stops a checkpoint materialising on top of you. **Never call
`CheckpointManager.SetCheckpointEnabled` directly on a tick** — you would be fighting `CheckpointInstance`
every minute and the state would not unwind on disable.

### 2.5 Why "drive the schedule data, not the runtime objects"

This is the central architectural call. Three reasons:

1. **Teardown is free.** Restore the snapshot, call `Evaluate()`, done. Nothing to hunt down.
2. **It survives the game updating its AI.** We are writing designer numbers, not reimplementing dispatch.
3. **It composes with the game's own courtesy checks** — distance gating, curfew gating, unmanned-checkpoint
   lowering (v0.4.0f8), officer-pool exhaustion. Reimplementing dispatch would lose all of them.

### 2.6 Reading the fine table at runtime

`PenaltyHandler`'s 14 fine constants have **no values in the metadata dump** `[V]`. On the first gameplay
scene, read them once and cache:

```csharp
static readonly (string Field, string Crime)[] FineMap = { ("CONTROLLED_SUBSTANCE_FINE", nameof(PossessingControlledSubstances)), … };
// value = (float)AccessTools.Property(typeof(PenaltyHandler), field).GetValue(null);
```

If any read returns 0 or throws, fall back to the DESIGN-INTENT §10.3 table
($5/$10/$20/$30/$50/$50/$50/$50/$50/$50/$75/$100/$150/$150). Log which path was used.
`DrugTrafficking`, `TransportingIllicitItems` and `VehicularAssault` have **no** dedicated constant `[V]` —
map them to `ATTEMPT_TO_SELL_FINE`, `LOW_SEVERITY_DRUG_FINE` and `ASSAULT_FINE` respectively `[I]`.

---

## 3. Pillar 2 — Federal agents

### 3.1 What they are, mechanically

A federal agent is **an instance of `Il2CppScheduleOne.Police.PoliceOfficer` that our mod owns**: spawned by
us, never added to any `PoliceStation.OfficerPool`, never saved, tagged in a mod-side
`HashSet<int>` keyed on `GetInstanceID()`, and configured to behave differently. They are not a new type,
not a `ClassInjector` subclass, and not a new prefab. `PoliceOfficer` has **no subclasses in the shipped
game** `[V]`, and `ClassInjector` over a `NetworkBehaviour` with a SyncVar is explicitly not recommended `[V]`.

How they differ from a beat cop:

| Axis | Local PD | Federal agent |
|---|---|---|
| Pooling | `AutoDeactivate = true`, returns to `PoliceStation.OfficerPool` | `AutoDeactivate = false`; Prefix on `CheckDeactivation()` returns false while our despawn timer is live |
| Persistence | `ShouldSave()` true, saved with the NPC set | Prefix on `PoliceOfficer.ShouldSave()` returns false — they must never enter save data |
| Radio | `ChatterEnabled = true` | `false` — silence reads as "not on the PD net" |
| Search | `BodySearchChance` from the tier | `1.0f` — they always search |
| Vision | prefab defaults | `Awareness.VisionCone.RangeMultiplier ×1.5`, `.Attentiveness ×1.5`, `.Memory ×2.0` |
| Give-up | `CombatBehaviour.GiveUpRange` / `GiveUpTime` defaults | both raised; they do not drop pursuit on brief LOS loss |
| Force | escalates through the shipped ladder | starts at `EPursuitLevel.NonLethal`, escalates to `Lethal` on any resist |
| Investigation | reactive | proactive: they stake out your owned property (§3.6) |

Escaping one awards **60 XP**, exact parity with the shipped "escape Wanted Dead or Alive" award — the
game's largest non-quest XP grant (DESIGN-INTENT §13.4).

### 3.2 Triggers

Evaluated once per in-game hour (`TimeManager.onHourPass`), server-side, per player. Any one fires:

1. `Heat ≥ 80` held continuously for **1 full in-game day** (1,440 tracked minutes), **or**
2. cumulative confiscated value `≥ $10,000` — the shipped weekly ATM deposit cap, i.e. the game's own
   definition of "more money than a normal person moves", **or**
3. `≥ 3 arrests within 7 in-game days`, matching the shipped weekly order cycle.

Cooldown: no new federal event within **2 in-game days** of the last one ending, so it stays an event and
not a permanent tax. Never fires while `Player.IsArrested`, `IsUnconscious`, or `TimeManager.IsSleepInProgress`.

### 3.3 Construction — how you actually get one

**Primary path (preferred):** the officer prefab is registered with FishNet under the GameObject name
`"PoliceNPC"` in `NetworkManager.SpawnablePrefabs` `[V]` — read out of the decompiled
`UncleTyrone/EvenMoreFootPatrols` source and corroborated by `research-ext/CUSTOM-CHARACTERS.md:843`. This
beats cloning a live officer because the prefab is already a registered network prefab.

```csharp
var manager  = Il2CppFishNet.InstanceFinder.NetworkManager;
var prefabs  = manager.SpawnablePrefabs;
NetworkObject template = null;
for (int i = 0; i < prefabs.GetObjectCount(); i++)
{
    var obj = prefabs.GetObject(true, i);
    if (obj != null && obj.gameObject.name == "PoliceNPC") { template = obj; break; }
}

var clone   = UnityEngine.Object.Instantiate(template);
var officer = clone.gameObject.GetComponent<Il2CppScheduleOne.Police.PoliceOfficer>();
clone.gameObject.name = "FederalAgent_" + Guid.NewGuid().ToString("N")[..8];
// … appearance (§3.4) and tuning (§3.1) …
// EvenMoreFootPatrols found the activate/spawn ordering needs delays:
//   SetActive(true) at +0.3 s, ServerManager.Spawn(clone) at +0.6 s. Immediate spawn does not work. [V]
```

**Fallback path (NACops Pattern A)** if `"PoliceNPC"` is not in `SpawnablePrefabs` on this build: clone a
live `PoliceOfficer` while inactive and re-run the FishNet init trio `[V]`:

```csharp
var donor = Il2CppScheduleOne.Police.PoliceOfficer.Officers[0];
donor.gameObject.SetActive(false);
var clone = UnityEngine.Object.Instantiate(donor.gameObject);
var nob   = clone.GetComponent<NetworkObject>();

byte componentIndex = 0;
nob.UpdateNetworkBehaviours(nob, ref componentIndex);
nob.Preinitialize_Internal(manager, 150, null, true);
nob.Initialize(true, true);
manager.ServerManager.Spawn(nob);
donor.gameObject.SetActive(true);
```

Two non-obvious details from that source, both worth copying: the clone's `Avatar`, `Avatar/BodyContainer`
and `NavMeshAgent` must be **activated for one frame then deactivated** so unassigned fields populate; and
NACops sets `officer.Movement.Agent.areaMask = 57` — the NavMesh mask that lets an officer path into
buildings `[V]`. Read the implementation from the **MIT** sibling `XOWithSauce/schedule-cartelenforcer`
(`InitHelper.cs`), not from NACops, which has no licence.

### 3.4 The look — re-tint plus existing parts away

No new assets. The shipped officer is `PoliceCap` + `BulletProofVest_Police` + `PoliceBelt` +
`CombatBoots` `[V]`. The federal read is achieved by *removing* the PD markers and shifting the palette to
navy/charcoal. Two variants, both from `research-ext/CUSTOM-CHARACTERS.md` §4.2:

**Suited (investigator / stakeout):**

| Slot | Path | Tint |
|---|---|---|
| Body layer | `Avatar/Layers/Top/Buttonup` | `#FFFFFF` |
| Body layer | `Avatar/Layers/Bottom/CargoPants` | `#262626` *(reads as suit trousers when dark; `Jeans` does not)* |
| Accessory | `Avatar/Accessories/Chest/Blazer/Blazer` | `#262626` |
| Accessory | `Avatar/Accessories/Head/Oakleys/Oakleys` | `#000000` |
| Accessory | `Avatar/Accessories/Feet/DressShoes/DressShoes` | `#000000` |
| Accessory | `Avatar/Accessories/Waist/Belt/Belt` | `#000000` — **plain `Belt`, not `PoliceBelt`** |
| Head | *nothing* | the absence of a cap next to a capped officer is the read |

**Tactical (raid):** swap the blazer for `Avatar/Accessories/Chest/BulletProofVest/BulletProofVest` in navy
`#000080` — note there is a plain `BulletProofVest` **and** a `BulletProofVest_Police` `[V]`; the unmarked
one is the whole trick. Add `Avatar/Accessories/Feet/CombatBoots/CombatBoots` and
`Avatar/Accessories/Head/Cap/Cap` in navy.

**The `PoliceBelt` trick.** `Il2CppScheduleOne.AvatarFramework.PoliceBelt : Accessory` exposes
`SetBatonVisible(bool)`, `SetTaserVisible(bool)`, `SetGunVisible(bool)` `[V]`. If you keep the belt on the
tactical variant, call `SetBatonVisible(false); SetTaserVisible(false); SetGunVisible(true);` — the
silhouette says "no less-lethal option" before the agent has done anything. Reachable as
`officer.belt` `[V]`.

**Application.** Copy a live officer's `Avatar.CurrentSettings` as the base, override the accessory and
body-layer entries, then apply with the **per-aspect** calls rather than `LoadAvatarSettings` — the
community-documented invisibility bug on a fresh clone `[V]`:

```csharp
var a = officer.Avatar;
a.SetSkinColor(baseSettings.SkinColor);
a.ApplyBodySettings(s); a.ApplyBodyLayerSettings(s, -1); a.ApplyFaceLayerSettings(s);
a.ApplyAccessorySettings(s); a.ApplyHairSettings(s); a.ApplyHairColorSettings(s);
a.ApplyEyeBallSettings(s); a.ApplyEyeLidSettings(s); a.ApplyEyeLidColorSettings(s); a.ApplyEyebrowSettings(s);
a.ApplyShapeKeys(s.Gender, s.Weight);
```

Note the IL2CPP signature is `ApplyShapeKeys(Single, Single)`; the Mono decompile shows a third `bool`.
Bind by the IL2CPP shape and flag it (R5).

Author the override settings via `Il2CppScheduleOne.AvatarFramework.Customization.BasicAvatarSettings` —
31 semantic slots (`Top`/`Bottom`/`Shoes`/`Headwear`/`Eyewear` + colours) — and convert with
`GetAvatarSettings()` `[V]`. `BasicAvatarSettings.GetValue<T>(string)` / `SetValue<T>(string, T)` +
`GetJson()` mean the whole agent look can be a JSON file in `UserData/` that a player can re-skin. Do that
from day one; it costs nothing and kills a whole class of "can I change the outfit" requests.

**Asset paths are not in the metadata** — only 7 literals exist `[V]`. The paths above come from S1API's
accessory constant catalogue (`Head.PoliceCap`, `Chest.BulletProofVestPolice`, `Waist.PoliceBelt`, …) which
carries the full path strings `[V]`. Still: enumerate `Resources.FindObjectsOfTypeAll<Accessory>()` at
startup and log the set once, so a bad path degrades to "wrong hat" instead of an exception (R4).

### 3.5 Spawning, registration, despawn

**Spawn:** at `PoliceStation.GetClosestPoliceStation(player.transform.position).SpawnPoint` `[V]`, 2 agents
per event (DESIGN-INTENT §14.2). Register them:

- add to our own `List<PoliceOfficer> _agents` (the authoritative list),
- add to `Il2CppScheduleOne.NPCs.NPCManager.NPCRegistry` **only if** the game did not (NACops does this
  defensively; `NPC.Awake` is the presumed writer but that is inferred `[I]`),
- set `npc.NPCData.BasicInfo.ID` to a namespaced value like `"expansions_fed_agent"` so it can never
  collide with a real NPC id,
- **do not** add to `PoliceStation.OfficerPool`. If you do, `PullOfficer()` will dispatch a federal agent
  for a jaywalking call and they will never leave.

**Direct control:** `officer.BeginFootPursuit_Networked(player.PlayerCode, includeColleagues: false)` `[V]`
when the player is already wanted, otherwise `officer.StartFootPatrol(group, warpToStartPoint: true)` on a
`PatrolGroup` built from a `FootPatrolRoute` near the player's last known position
(`LawManager.StartFootpatrol(route, requestedMembers)` returns a usable `PatrolGroup` `[V]`).
`includeColleagues: false` matters — we do not want a fed dragging the local PD into every call.

**Despawn** — a single `DespawnAgent(PoliceOfficer)` used by both the timer and teardown:

```
1. officer.PursuitBehaviour.EndCombat();  officer.PursuitBehaviour.Disable();
2. officer.Deactivate();                                // server-only; log literal proves client is rejected
3. untag  -> the CheckDeactivation / ShouldSave prefixes stop firing for it
4. PoliceOfficer.Officers.Remove(officer);
   NPCManager.NPCRegistry.Remove(npc);
5. if (nob.IsSpawned) manager.ServerManager.Despawn(nob);
6. UnityEngine.Object.Destroy(officer.gameObject);
```

There is **no generic `NPC.Despawn`** in the game — only `CartelGoon.Despawn()` and
`Employee.LeavePropertyAndDespawn()` `[V]`. Destroying the GameObject and pulling it out of the registries
yourself is the documented route.

**Duration:** an event lasts `federal_event_hours` (default 6 in-game hours) or until the player is
arrested, whichever is first. On expiry, agents walk to the nearest
`PoliceStation.GetClosestPoliceStation(...)` and despawn out of sight — use
`PoliceOfficer.OutOfSightTimeToDeactivate` semantics rather than popping them in front of the player.

### 3.6 Investigation and property raids

Two behaviours beyond "chase you":

**Stakeout (T4 + federal active).** Agents are assigned to the player's most-recently-visited owned
`Property` (`S1API.Entities.Player.CurrentProperty` / `LastVisitedProperty` `[V]`) and driven with
`StartFootPatrol` on a synthesised two-waypoint route at the property entrance. Result: the player is
pinned indoors, which is a real, legible consequence with zero new AI.

**Raid (Outlaw + federal active).** If the player is *not* present at the property when the event fires,
after `raid_delay_minutes` (default 30 in-game minutes) every `StorageEntity` on that property has its
contraband drained at `EStealthLevel.Advanced`:

```csharp
foreach (var slot in storage.ItemSlots) { /* match contraband, then */ }
storage.SetStoredInstance(conn: null, itemSlotIndex: i, instance: null);
storage.ContentsChanged();
```

`StorageEntity` members verified: `ItemSlots`, `ItemCount`, `GetAllItems()`, `GetContentsDictionary()`,
`ClearContents()`, `SetStoredInstance(NetworkConnection, Int32, ItemInstance)`, `SetItemSlotQuantity(Int32, Int32)`,
`GetNetworth(MoneyManager+FloatContainer)` `[V]`. Use `GetNetworth` to add the raid haul to the confiscated-value
counter that feeds trigger #2. **Never `ClearContents()`** — that takes legitimate items too and will read as a
bug. Confiscate contraband only, cap the fraction at `raid_confiscation_fraction` (default 0.5), and post a
phone message naming what was taken so it is never silent.

This is the closest buildable reading of the original Trello card's *"raids on your properties"* and
*"an evidence room where your confiscated items go … you can break in to try and recover your stuff"* — the
evidence room itself is out of scope for v1 (there is no such location in the game and building one means
authoring a new interior).

---

## 4. Pillar 3 — Outlaw status

### 4.1 State model

Outlaw is **latched and tiered**, not a slider. Heat is the pressure; Outlaw is the phase change.

```
                 heat >= 90  OR  survived a federal encounter
   Clean  ──────────────────────────────────────────────────►  Marked
     ▲                                                            │
     │  3 consecutive clean in-game days                          │  2nd federal encounter
     │  OR pay $25,000 legal fee (cash)                           │  OR 2 arrests while Outlaw
     │                                                            ▼
     └──────────────  (from Hunted: clear to Marked first)  ───  Hunted
```

- **Entry**: `Heat ≥ 90`, **or** a federal event ends with the player un-arrested ("survived").
- **Marked → Hunted**: a second federal encounter, or a second arrest while Outlaw.
- **Exit**: `Hunted → Marked` and `Marked → Clean` each require **3 consecutive in-game days with zero
  crimes recorded**, or a one-off cash payment of **$25,000** (the shipped Barn price — a genuine mid-game
  milestone, so the "buy your way out" lane exists but stings).
- A "clean day" = a day rollover during which `PlayerCrimeData.AddCrime` never fired and no arrest occurred.
  Tracked in the persisted record; reset to 0 by any crime.
- Entering Outlaw does **not** reset Heat. Leaving Outlaw clamps Heat to 79 so you do not instantly re-latch.

### 4.2 What it changes in the world

| Effect | Mechanism | Verified members |
|---|---|---|
| Every cop knows you on sight | `player.VisibilityComponent.ApplyState("Expansions.Police.Outlaw", EVisualState.Wanted, 0f)` — label-scoped, cannot collide with the game's own states | `EntityVisibility.ApplyState(String, EVisualState, Single)` / `.RemoveState(String, Single)` `[V]` |
| Body searches always find something | Postfix `BodySearchBehaviour.DoesPlayerContainItemsOfInterest()` → true | `[V]`, `virtual` |
| Vehicle searches always find something | Postfix `CheckpointBehaviour.DoesVehicleContainIllicitItems()` → true | `[V]` |
| Cops always eligible to search you | Postfix `PoliceOfficer.CanInvestigatePlayer(Player)` → true | `[V]` |
| Pursuit does not evaporate | Prefix `PlayerCrimeData.TimeoutPursuit()` returns false while `TimeSinceSighted < GetSearchTime() × outlaw_search_multiplier` | `[V]` |
| Heat floor | Prefix `PlayerCrimeData.Deescalate()` refuses to drop below `EPursuitLevel.Investigating` while Outlaw | `[V]` |
| Rap sheet survives arrest | Postfix `PlayerCrimeData.OnPlayerFreed()` re-adds the retained crimes. **Postfix, not a skipping Prefix** — the method also does `SetPursuitLevel(None)` and resets `MinsSinceLastArrested`, both of which we want | `[V]` |
| Dealer cut 20% → 25% | `NPCDealer` cut field on each recruited dealer, restored on exit | via `S1API.Entities.NPCDealer` / `DealerDataBuilder.WithCut` `[V]` |
| Customer snitch chance +10 pp | `CustomerData.CallPoliceChance` +0.10, clamped to 1.0, snapshotted and restored | `[V]` |
| Card-only vendors refuse | Ray's Realty and Hyland Auto only. Uses the shipped cash-only/card-only split — you are an outlaw, not bankrupt | shop gating via `S1API.Shops.ShopManager` `[V]` |

Everything in that table is snapshot-and-restore. On disable or on leaving Outlaw, the reverse runs from a
single `OutlawState.Revert()`.

### 4.3 How the player sees it

- **HUD.** Postfix `Il2CppScheduleOne.UI.CrimeStatusUI.UpdateStatus()` `[V]`. That class already owns
  `InvestigatingMask` / `UnderArrestMask` / `WantedMask` / `WantedDeadMask` and a `CrimeStatusGroup`
  `CanvasGroup`. Add one TMP child under `CrimeStatusContainer` cloned from an existing label so it
  inherits the game's font asset and SDF material — never ship a font. Show `HEAT 62 · CRACKDOWN`, and
  `OUTLAW` in the `WantedDeadMask` palette when latched.
- **Phone message** on every tier change and on Outlaw entry/exit, via S1API messaging. Silent state
  changes read as bugs.
- **Curfew boards.** `CurfewManager.VMSBoards` is an array of `VMSBoard` and the manager owns
  `NORMAL_MESSAGE` / `CURFEW_MESSAGE` / `WARNING_MESSAGE` `[V]`. At T3+ swap the normal message for a
  wanted-notice string. Cheap, extremely native-feeling, fully reversible. `[U]` on whether `VMSBoard`
  exposes a settable text member — probe it (R6); skip if not.

### 4.4 Persistence

One S1API saveable, **one level deep** — `SaveableAutoRegistry.IsDirectSaveableInheritor` only discovers
direct inheritors `[V]`:

```csharp
using S1API.Internal.Abstraction;
using S1API.Saveables;

public sealed class PoliceSaveState : Saveable
{
    [SaveableField("expansions_police")]                       // -> <save>\Modded\Saveables\expansions_police.json
    private List<PlayerHeatRecord> _players = new();

    public override SaveableLoadOrder LoadOrder => SaveableLoadOrder.AfterBaseGame;

    protected override void OnLoaded() { /* hand records to HeatDirector */ }
    protected override void OnSaved()  { }
}

public sealed class PlayerHeatRecord
{
    public string PlayerKey;              // see R3
    public float  Heat;
    public int    OutlawTier;             // 0 Clean, 1 Marked, 2 Hunted
    public int    CleanDayStreak;
    public int    ArrestDayStamps;        // packed ElapsedDays values, last 7 days
    public float  ConfiscatedValueTotal;
    public int    MinutesAtHeat80;
    public int    LastFederalEventDay;
    public int    MinutesCleanToday;
}
```

Never name a saveable field `"QuestData"` — it collides `[V]`. Do not instantiate or register the class;
S1API scans assemblies and constructs one singleton per discovered type.

---

## 5. Pillar 4 — Heavier consequences

The shipped arrest is nearly free: fines are $5–$150 per charge, **cash only, with no debt if you cannot
pay** — carry no cash and arrest costs you nothing but your stash. Vehicle cargo is untouched. That is the
hole to close.

### 5.1 The arrest pipeline as it exists

```
Player.Arrest_Server()          [C→S ServerRpc]
  -> Player.Arrest_Client()     [S→C ObserversRpc]  sets IsArrested, fires Player.onArrested
  -> ArrestScreen.Open() -> Continue()
  -> ArrestNoticeScreen.Open()
       -> RecordCrimes()                      fills recordedCrimes from PlayerCrimeData.Crimes
       -> RecordPossession(EStealthLevel)     builds the "what you had" list
       -> PenaltyHandler.ProcessCrimeList(recordedCrimes) -> List<string>
       -> ConfiscateItems(EStealthLevel)      <-- ITEMS ACTUALLY REMOVED HERE
       -> OnClose() / Exit()                  fine applied to MoneyManager here — UNVERIFIED
Player.Free_Server() -> Free_Client() -> PlayerCrimeData.OnPlayerFreed() -> ClearCrimes()
```

All `[V]` except the marked line.

### 5.2 What we add

| Consequence | Implementation | Members |
|---|---|---|
| **Fine multiplier** | Postfix `PenaltyHandler.ProcessCrimeList(Dictionary<Crime,Int32>)`, rewrite the `$… fine` string in `__result` so the notice shows the real number; charge it ourselves in §5.3 | `[V]`, static |
| **Confiscation scope** | Prefix `ArrestNoticeScreen.ConfiscateItems(EStealthLevel)` mutating `__0` → `EStealthLevel.Advanced` while Outlaw. The argument is the *max* stealth tier that gets taken, so Advanced packaging stops being a get-out-of-jail card | `[V]` |
| **Notice honesty** | Same mutation on `RecordPossession(EStealthLevel)` so the notice lists what will actually be taken | `[V]` |
| **Vehicle cargo** | Postfix `ConfiscateItems`: drain contraband from `ArrestNoticeScreen.vehicle.Storage` (the screen already holds the `LandVehicle`), using `SetStoredInstance(null, i, null)` + `ContentsChanged()` | `ArrestNoticeScreen.vehicle` `[V]`; `LandVehicle.Storage` `[V]` |
| **Equipment loss** | Same postfix, Outlaw only: confiscate weapons/tools from the player's slots | `[U]` — needs the `PlayerInventory` type name confirmed at runtime (R7) |
| **Cash + debt** | §5.3 | |
| **Jail day** | §5.4 | |
| **Relationship damage** | On arrest, −0.25 relationship to the NPC who called it in (from `CallPoliceBehaviour.Target`/`ReportedCrime` context) | `NPC.RelationData` (`NPCRelationData`) `[V]` |

### 5.3 Cash, then debt — the actual teeth

The shipped economy makes **cash abundant and online balance scarce**: $10,000/week ATM deposit cap,
$20,000/24 h laundering ceiling (DESIGN-INTENT §13.3). A cash-only fine is therefore a rounding error, and
that is precisely why the ballot called out "heavier consequences". So:

```csharp
// Postfix on Il2CppScheduleOne.UI.ArrestNoticeScreen.OnClose()
float owed = ComputeFine(recordedCrimes) * fineMultiplier;     // ×2 while Outlaw
float cash = MoneyManager.Instance.cashBalance;                // read-only property [V]
float paid = Mathf.Min(cash, owed);
MoneyManager.Instance.ChangeCashBalance(-paid, visualizeChange: true, playCashSound: true);   // [V]

float shortfall = owed - paid;
if (shortfall > 0f && enableDebt)
    MoneyManager.Instance.CreateOnlineTransaction("Court fine", -shortfall, 1f, "Unpaid penalties");  // [V], ServerRpc
```

`CreateOnlineTransaction` is the ledger path that moves `onlineBalance` (a `SyncVar<float>`) `[V]`. This is
the single highest-impact line in the whole mod: it converts "arrest is free if I'm broke" into "arrest eats
the resource I actually can't farm". Default it **on** but keep `police_enable_debt` in config, because it
is also the most likely change to annoy someone.

**Ordering hazard.** Whether the *game* also deducts a fine at `OnClose`/`Exit` is **UNVERIFIED** (§11 R2).
Verify before shipping the multiplier, or you double-charge. The probe is in §11.

### 5.4 Jail time (Outlaw only)

There is no jail system in the game — no holding cell, no `jail`/`detain`/`custody` literal anywhere in the
metadata `[V]`. Building one means authoring an interior. Instead, reuse two shipped systems:

1. **Lose the rest of the day.** Postfix `PlayerCrimeData.OnPlayerFreed()` (server side): call
   `TimeManager.Instance.StartSleep()` `[V, signature]` so the game's own sleep transition runs — employees
   paid, plants advanced, day incremented. `[U]` whether `StartSleep()` is callable without a bed (R8).
   Fallback: `TimeManager.Instance.SkipForwardToTime(TimeManager.WakeTime)` `[V]`.
2. **Processing fee.** Charge one day of total employee wages in cash, computed from the live employee
   roster (shipped rates: Cleaner $100, Botanist $200, Handler $200, Chemist $300). Same economic effect as
   "forfeit a day of wages" with none of the risk of reaching into the payday system.

Announce both on the arrest notice. An unexplained day-skip is indistinguishable from a crash.

---

## 6. Architecture

### 6.1 Files

```
src/Expansions.PoliceOverhaul/
  PoliceOverhaulMod.cs              (exists) MelonMod entry, [HarmonyDontPatchAll]
  PoliceOverhaulModule.cs           (exists) ExpansionModule — wires everything, owns the scene latch
  PoliceConfig.cs                   every ConfigValue<T> in one place (§9)

  State/
    HeatModel.cs                    PURE MATH. No game types, no Unity types. Heat curve, tier mapping,
                                    decay, outlaw transitions. Directly unit-testable off-game.
    PlayerHeatRecord.cs             serialisable DTO
    PoliceSaveState.cs              : S1API Saveable, one [SaveableField]

  Runtime/
    GameBridge.cs                   IsServerOrSolo, singleton accessors, null-safe lookups, Player→key
    RuntimeConstants.cs             reads + caches PenaltyHandler fines and the §12 tuning statics;
                                    records which writes actually took effect (R1)
    HeatDirector.cs                 owns Heat per player; per-minute tick; writes LE_Intensity
    LawScheduleTuner.cs             snapshot/restore + tier-driven writes to *Instance member bands
                                    and CheckpointInstance windows
    DetectionTuner.cs               VisionCone universal scalars, per-officer BodySearchChance sweep,
                                    LawManager dispatch statics
    OutlawState.cs                  state machine + Apply()/Revert() of every world effect in §4.2
    ConsequenceService.cs           fine maths, confiscation widening, vehicle cargo, debt, jail day
    FederalAgentFactory.cs          prefab lookup / clone, avatar build, per-instance tuning
    FederalAgentDirector.cs         trigger evaluation, event lifecycle, stakeout/raid, despawn
    AgentAppearance.cs              BasicAvatarSettings authoring + UserData JSON round-trip
    PoliceHud.cs                    CrimeStatusUI augmentation + phone notifications

  Patches/
    LawPatches.cs        P1–P6
    OfficerPatches.cs    P7–P11
    PursuitPatches.cs    P12–P14, P21
    ArrestPatches.cs     P15–P20
```

### 6.2 How it hangs off `ExpansionModule`

```csharp
public sealed class PoliceOverhaulModule : ExpansionModule
{
    private bool _wired;

    protected override void OnRegistered() => _config = new PoliceConfig(Config);   // declare settings once

    public override void OnEnabled() { /* nothing world-touching; wait for the scene */ }

    public override void OnSceneLoaded(int buildIndex, string sceneName)
    {
        if (_wired || sceneName != "Main" || !GameBridge.IsServerOrSolo) return;
        try
        {
            RuntimeConstants.Probe();                      // read + verify-write every tuning static
            _director   = new HeatDirector(_config, Log);
            _tuner      = new LawScheduleTuner(Log);       // snapshots on construction
            _outlaw     = new OutlawState(_config, Log);
            _feds       = new FederalAgentDirector(_config, Log);
            ApplyPatches();
            SubscribeEvents();
            _wired = true;
            Lifetime.OnDispose(() => { _feds.DespawnAll(); _outlaw.Revert(); _tuner.Restore(); _wired = false; });
        }
        catch (Exception ex) { Log.Error("Wiring failed; module inert this session.", ex); }
    }

    public override void OnDisabled() { }   // stays empty — Lifetime + Harmony.UnpatchSelf() do the work
}
```

`Config.Bind<T>(id, default, name, description)` returns a live `ConfigValue<T>`; subscribe to
`ConfigValue<T>.Changed` for hot-reload of the master scalar and unsubscribe via `Lifetime.OnDispose`.

Frame work: none. Everything is event- or minute-driven. `OnUpdate` stays unimplemented — a police mod that
costs a frame budget is a police mod people uninstall.

### 6.3 No-patch subscription points (prefer these)

| Event | Type | Use |
|---|---|---|
| `PlayerCrimeData.onPursuitLevelChange` | `Il2CppSystem.Action<EPursuitLevel, EPursuitLevel>` `[V]` | HUD, outlaw escalation, federal trigger |
| `Player.onArrested` / `Player.onFreed` | `Il2CppSystem.Action` `[V]` | heat +15, consequence pipeline |
| `PoliceOfficer.OnPoliceVisionEvent` | `static Il2CppSystem.Action<VisionEventReceipt>` `[V]` | "a cop noticed something" without patching |
| `TimeManager.onDayPass` / `onSleepStart` / `onSleepEnd` / `onHourPass` | `Il2CppSystem.Action` `[V]` | decay, clean-day streak, federal trigger poll |
| `CurfewManager.onCurfewStart` / `onCurfewEnd` | `UnityEngine.Events.UnityEvent` `[V]` | curfew heat multiplier |
| `BodySearchBehaviour.onSearchComplete_Clear` / `_ItemsFound` | `UnityEvent` `[V]` | search outcome telemetry |
| `MoneyManager.onNetworthCalculation` | `Action<FloatContainer>` `[V]` | *contributor* hook only — do not use for detecting money changes |

Subscribe/unsubscribe on `Il2CppSystem.Action` fields with `op_Addition`/`op_Subtraction` and **cache the
delegate in a managed field** so the interop wrapper is not collected:

```csharp
_onDay = (Il2CppSystem.Action)(System.Action)HandleDayPass;
tm.onDayPass = (Il2CppSystem.Action)(tm.onDayPass + _onDay);
Lifetime.OnDispose(() => { if (TimeManager.InstanceExists) tm.onDayPass = (Il2CppSystem.Action)(tm.onDayPass - _onDay); });
```

`Il2Cpp.ActionList` (used by `onMinutePass` / `onUncappedMinutePass` / `onTick`) is a different shape —
`.Add(...)` / `.Remove(...)` `[V]`.

---

## 7. Harmony patches — complete target list

All applied through `Context.Harmony`. Default priority (`Priority.Normal` = 400) unless stated. Every
target below is a real, verified member.

| # | Target | Kind | Pri | Why | S1API collision |
|---|---|---|---|---|---|
| P1 | `Law.LawController.OnUncappedMinPass()` | Postfix | 400 | heat tick, idle drain, write `LE_Intensity`, `CurrentSettings.Evaluate()` | **none** |
| P2 | `Law.LawController.DayPass()` | Postfix | 400 | capture `_dayBaseline` after the game's own daily arithmetic; clean-day streak | **none** |
| P3 | `Console+SetLawIntensity.Execute(List<String>)` | Postfix | 400 | re-derive `_dayBaseline` when a player uses `setlawintensity` so we stop fighting them | **none** — S1API patches `Console.Awake`, `Console.SubmitCommand` and `Console+AddItemToInventoryCommand.Execute`, not this class |
| P4 | `Law.LawManager.PoliceCalled(Player, Crime)` | Postfix | 400 | heat for the **target player** (never `Player.Local` — this is the per-player entry point) | **none** |
| P5 | `PlayerScripts.PlayerCrimeData.AddCrime(Crime, Int32)` | Postfix | 400 | heat per crime even when no dispatch occurs; resets clean-day streak | **none** |
| P6 | `Map.PoliceStation.Dispatch(Int32, Player, EDispatchType, Boolean)` | Prefix | 400 | clamp `__0` into the tier officer band (works even if the `LawManager` statics are const-inlined); optionally add a fed | **none** |
| P7 | `Police.PoliceOfficer.CheckDeactivation()` | Prefix → `false` | 400 | keep tagged federal agents on the map | **none** |
| P8 | `Police.PoliceOfficer.ShouldSave()` | Prefix → `__result = false` | 400 | tagged agents must never enter NPC save data | ⚠ **adjacent** — S1API prefixes `NPCs.NPC.ShouldSave`. `PoliceOfficer.ShouldSave` is a separate, more-derived native method under IL2CPP, so patching the derived declaration does not touch S1API's. **Confirm with `AccessTools.GetDeclaredMethods(typeof(PoliceOfficer))` before shipping** |
| P9 | `Police.PoliceOfficer.CanInvestigatePlayer(Player)` | Postfix | 400 | force search eligibility while Outlaw | **none** |
| P10 | `NPCs.Behaviour.BodySearchBehaviour.DoesPlayerContainItemsOfInterest()` | Postfix | 400 | Outlaw: nothing is clean enough | **none** |
| P11 | `NPCs.Behaviour.CheckpointBehaviour.DoesVehicleContainIllicitItems()` | Postfix | 400 | Outlaw: vehicle search always finds | **none** |
| P12 | `PlayerScripts.PlayerCrimeData.TimeoutPursuit()` | Prefix → `false` conditionally | 400 | pursuits persist longer while Outlaw | **none** |
| P13 | `PlayerScripts.PlayerCrimeData.Deescalate()` | Prefix → `false` conditionally | 400 | floor the pursuit level while Outlaw | **none** |
| P14 | `PlayerScripts.PlayerCrimeData.OnPlayerFreed()` | **Postfix** | 400 | re-add retained crimes (rap sheet). Deliberately *not* a skipping prefix — the method also does `SetPursuitLevel(None)` and resets `MinsSinceLastArrested`, both of which we want to keep | **none** |
| P15 | `UI.ArrestNoticeScreen.RecordPossession(EStealthLevel)` | Prefix, mutate `__0` | 400 | notice lists what will really be taken | **none** |
| P16 | `UI.ArrestNoticeScreen.ConfiscateItems(EStealthLevel)` | Prefix (mutate `__0`) + Postfix | 400 | widen confiscation tier; drain vehicle cargo and equipment; tally confiscated value | **none** |
| P17 | `Law.PenaltyHandler.ProcessCrimeList(Dictionary<Crime, Int32>)` | Postfix, mutate `__result` in place | 400 | show the real (multiplied) fine on the notice | ⚠ **not S1API, but NACops patches this method too.** Postfix + in-place mutation composes with another postfix; a *replacing* patch would not. Keep it a postfix |
| P18 | `UI.ArrestNoticeScreen.OnClose()` | Postfix | 400 | charge cash, create debt, apply the jail day and processing fee | **none** |
| P19 | `UI.CrimeStatusUI.UpdateStatus()` | Postfix | 400 | heat + outlaw HUD | **none** |
| P20 | `PlayerScripts.Player` → `RpcLogic___Free_Server_2166136261` | Postfix | 400 | server-authoritative post-release effects. Resolve by **name prefix at runtime**, never by hardcoded hash | **none** |
| P21 | `Police.NPCResponses_Police.NoticedSuspiciousPlayer/NoticedPettyCrime/NoticedDrugDeal/NoticedViolatingCurfew` | Postfix | 400 | Outlaw: notice → immediate `Arresting` instead of `Investigating`. All 19 methods on this class are `virtual` `[V]` — the cleanest behaviour surface in the system | **none** |

Resolve FishNet-generated bodies by prefix, never by literal hash — hashes are stable within a version and
change with the signature:

```csharp
var m = typeof(Il2CppScheduleOne.PlayerScripts.Player)
    .GetMethods(BindingFlags.Public | BindingFlags.Instance)
    .First(x => x.Name.StartsWith("RpcLogic___Free_Server_"));
```

### 7.1 S1API collision audit — the full check

S1API 3.1.4 declares 147 `[HarmonyPatch]` attributes across 40 types. Its footprint is:
`Persistence.*` (LoadManager, SaveManager, all loaders), `NPCs.NPC.{Awake, OnDestroy, GetSaveData, ShouldSave,
SetVisible}`, `NPCs.NPCHealth.{Awake, Load, Revive}`, `NPCs.NPCInventory.{Awake, OnSleepStart}`,
`NPCs.NPCManager.{GetNPC, GetSaveString}`, `Economy.{Customer, Dealer, Supplier, SupplierStash, DeadDrop}`,
`PlayerScripts.Player.{Awake, OnDestroy, ReceivePlayerData, RequestPlayerData}`,
`PlayerScripts.Health.PlayerHealth.TakeDamage`, `Product.*`, `UI.Phone.HomeScreen.Start`,
`UI.LoadingScreen.Close`, `Console.{Awake, SubmitCommand}`, `Console+AddItemToInventoryCommand.Execute` `[V]`.

**Zero overlap** with `Il2CppScheduleOne.Law.*`, `Il2CppScheduleOne.Police.*`,
`Il2CppScheduleOne.PlayerScripts.PlayerCrimeData`, `Il2CppScheduleOne.NPCs.Behaviour.*`,
`Il2CppScheduleOne.UI.ArrestNoticeScreen`, `Il2CppScheduleOne.UI.CrimeStatusUI`,
`Il2CppScheduleOne.Vision.*`, `Il2CppScheduleOne.Map.PoliceStation`. `LawManager`, `LawController`,
`CurfewManager` and `CheckpointManager` are completely unpatched by S1API — clear runway.

Three adjacencies, all handled:

1. `NPC.ShouldSave` (S1API Prefix) vs our `PoliceOfficer.ShouldSave` (P8) — separate native methods under
   IL2CPP; confirm at runtime.
2. `PlayerHealth.TakeDamage` — S1API has a **skipping** Prefix. We never patch it.
3. `NPCHealth.Awake/Load/Revive` — S1API has **verified skipping** Prefixes. We never patch them; officer
   health is a post-`Awake` field write on our own instances.

General rule from the S1API doc, adopted verbatim: **prefer Postfix unless you genuinely need to cancel**;
if you must Prefix, set an explicit `[HarmonyPriority]` and assume S1API's 800-priority prefixes run first.
None of our prefixes contend with an S1API prefix, so default priority is correct throughout.

---

## 8. Balance

Every number below is anchored to a value the shipped game already uses. Where I invent, I say so and I say
what it is calibrated against. The goal is that a player who has never read a patch note cannot tell which
of these numbers came from TVGS and which came from us.

### 8.1 The shipped anchors this is built on

| Shipped fact | Value | Source |
|---|---|---|
| Fine table | $5 / $10 / $20 / $30 (drug tiers) · $50 (Failure to Comply, Evading, Vandalism, Theft, Brandishing, Discharge) · $75 Assault · $100 Curfew · $150 Attempting to Sell, Deadly Assault | DESIGN-INTENT §10.3 |
| Fines are cash-only, **no debt if you can't pay** | — | DESIGN-INTENT §10.3 |
| Officers per post (v0.4.6) | **1–2**, previously flat 2 | v0.4.6 patch notes |
| Law intensity dial | integer **1–10** | `Console+SetLawIntensity`, S1API `MinIntensity`/`MaxIntensity` |
| Daily intensity drain | **0.05** | S1API `DailyIntensityDrain` |
| Curfew window | 20:00 hint · 20:30 warn · 21:00 soft · 21:15 hard · 05:00 end, +15 min police tolerance | verified constants |
| Wanted-level escape XP | 20 / 40 / **60** | DESIGN-INTENT §13.4 |
| Dealer cut | flat **20 %**, never changes | DESIGN-INTENT §8 |
| Customer snitch chance range | **0 % – 67.1 %** | DESIGN-INTENT §9.2 |
| ATM deposit cap | **$10,000 / week / player** | DESIGN-INTENT §13.3 |
| Laundering cap | **$20,000 / 24 h** with all four businesses | DESIGN-INTENT §13.3 |
| Barn price | **$25,000** | DESIGN-INTENT §13.4 |
| Employee wages | Cleaner $100 · Botanist $200 · Handler $200 · Chemist $300 per day | DESIGN-INTENT §7.1 |
| Time | 1 in-game hour ≈ 1 real minute; playable window 07:00 → 04:00 ≈ 21 in-game hours | DESIGN-INTENT §13.2 |
| Logistics quantum | **30 in-game minutes** per supplier item | DESIGN-INTENT §11.3 |

### 8.2 Heat gain — derived from the game's own severity ranking

`heat = fine ÷ 5`, read at runtime from `PenaltyHandler`. No new opinion about which crime is worse than
which; the dev already ranked them and we reuse that ranking wholesale.

| Crime | Fine | Heat | Crime | Fine | Heat |
|---|---|---|---|---|---|
| Controlled Substance | $5 | **+1** | Assault | $75 | **+15** |
| Low-severity drug | $10 | **+2** | Violating Curfew | $100 | **+20** |
| Moderate-severity drug | $20 | **+4** | Attempting to Sell | $150 | **+30** |
| High-severity drug | $30 | **+6** | Deadly Assault | $150 | **+30** |
| Evading / Vandalism / Theft / Brandishing / Discharge / Failure to Comply | $50 | **+10** | | | |

Modifiers: `× 1.5` during active curfew · `× 1.25` Uptown/Suburbia, `× 0.85` Northtown/Docks · `× quantity`
for stacked charges · `× heat_gain_scalar`.
Non-crime gains: arrest **+15** *(invented; calibrated to the Assault tier — an arrest should cost more than
one misdemeanour)*; deals **+1 per $2,000 of contract payment**, capped **+15/in-game day** *(invented;
$2,000 is the smallest shipped business launder cap, so one heat point ≈ "one small business's worth of
laundering", and the daily cap keeps a legitimate grind from soft-locking you into Federal)*.

Decay: **−10 per slept day** (anchored between the shipped −7 %/day addiction decay and the shipped total
wipe of the wanted level on sleep), of which up to **−5** is delivered live at −0.004/clean-minute so the
HUD moves. Full 100 → 0 in **10 clean days**.

**Sanity check against a real playthrough.** A mid-game player selling ~8 direct deals a day at ~$800 each
gains ≈ +3 heat/day from volume and loses 10 on sleep — so clean dealing never accumulates heat, which is
correct: the ballot said *police* improvements, not *anti-dealing* improvements. One caught curfew violation
(+20 × 1.5 = +30) plus the arrest (+15) is +45 in a night, i.e. **one bad night takes you from Calm to
Crackdown and costs four and a half clean days to shed.** That is the intended shape.

### 8.3 Tier effects

| Tier | Heat | Officers/post | `LE_Intensity` add | Checkpoints | Attentiveness / Memory | Body-search × | Grounding |
|---|---|---|---|---|---|---|---|
| T0 Calm | 0–19 | 1 / 1 | +0…1 | shipped windows | 1.00 / 1.00 | 1.00 | Exactly the lower bound v0.4.6 shipped |
| T1 Alert | 20–39 | 1 / 2 | +2…3 | shipped windows | 1.10 / 1.10 | 1.25 | Exactly the shipped upper bound — **T1 is vanilla** |
| T2 Crackdown | 40–59 | 2 / 3 | +3…4 | ±2 h widened | 1.25 / 1.25 | 1.50 | Extends the shipped rank curve ("you'll begin to see cars and more patrol officers") |
| T3 Task Force | 60–79 | 2 / 4 | +5…6 | all daylight | 1.50 / 1.50 | 2.00 | Pulls the shipped top-of-rank-curve (SUVs, checkpoint fences) forward |
| T4 Federal | 80–100 | 3 / 4 | +7…8 | all hours | 1.75 / 2.00 | 2.50 | Federal agents become eligible |

4 officers is the game's own hard cap. `LE_Intensity` is clamped 1–10 and is **added to** the vanilla
baseline, never replacing it — so at Heat 0 the world is exactly vanilla, which is the property that makes
the mod feel shipped rather than bolted on.

### 8.4 Federal agents

| Parameter | Value | Grounding |
|---|---|---|
| Trigger A | Heat ≥ 80 held **1 full in-game day** | A day is the game's own pressure unit (wage draw, deal cycle, curfew) |
| Trigger B | cumulative confiscated value ≥ **$10,000** | The shipped weekly ATM deposit cap — the game's own definition of "more money than a normal person moves" |
| Trigger C | ≥ **3 arrests in 7 in-game days** | 7 days is the shipped weekly order cycle |
| Agents per event | **2** | Two is a team; four is the dispatch cap and would leave no room for local PD |
| Event duration | **6 in-game hours** | ≈ 6 real minutes — long enough to be a chapter, short enough not to be a tax |
| Cooldown | **2 in-game days** | Keeps it an event |
| Behaviour | `NonLethal` from first contact, `Lethal` on any resist; no pursuit drop on brief LOS loss | Reuses the shipped Wanted / Wanted-Dead-or-Alive ladder rather than inventing AI |
| Escape reward | **60 XP** | Exact parity with the shipped "escape Wanted Dead or Alive", the largest non-quest award |
| Raid delay | **30 in-game minutes** after arrival | The shipped logistics quantum |
| Raid confiscation | **50 %** of contraband at `EStealthLevel.Advanced` | Never 100 % — a total wipe reads as a bug, and a partial loss is what makes you move the rest |

### 8.5 Outlaw

| Parameter | Value | Grounding |
|---|---|---|
| Entry | Heat ≥ **90**, or surviving a federal encounter | Top of the heat band; a state, not a slider — matching the ballot's "outlaw **status**" |
| Fine multiplier | **× 2** | Doubles the $150 top charge to $300. Deliberately conservative: the fine table is 2025-era data and may have drifted |
| Confiscation tier | `Basic` → **`Advanced`** | Advanced packaging stops being a get-out-of-jail card |
| Vehicle cargo | confiscated | Closes the shipped hole that vehicles and their cargo are "effectively invulnerable" |
| Dealer cut | 20 % → **25 %** | +5 pp hazard premium on a flat, never-changing shipped number — felt immediately, doesn't break the mental model |
| Customer snitch | **+10 pp** | Shipped chances already span 0–67.1 %, so +10 pp stays inside the existing distribution |
| Services lost | Ray's Realty, Hyland Auto | Uses the shipped cash-only / card-only vendor split. Cash trade still works — you're an outlaw, not bankrupt |
| Clear | **3 consecutive clean in-game days**, or **$25,000** cash | 3 clean days ≈ the 30 heat you'd shed anyway; $25,000 is the shipped Barn price, a genuine mid-game milestone |
| Jail day | force the day forward + a processing fee = one day of total employee wages, cash | The game has no jail. Reuses the shipped sleep transition and the shipped wage table instead of building a jail scene |

### 8.6 The debt lever, and why it is the real difficulty knob

Everything above is texture. The number that actually changes how the game plays is in §5.3: an unpaid fine
becomes an **online-balance** debit. The shipped economy makes cash abundant (employees are paid in it,
suppliers and the casino take it) and online balance scarce (capped at $10,000/week of deposits and
$20,000/24 h of laundering, i.e. roughly $140,000/week ceiling on convertible income at full build-out).
A $300 cash fine against a $50,000 cash pile is nothing. A $300 hit to the online balance is 1.5 % of a
day's laundering ceiling, and it is the only currency in the game the player cannot simply go and earn
faster. Default it on; expose it as `enable_debt`; expect it to be the setting people argue about.

---

## 9. Config

`MelonPreferences` category `PoliceImprovements_01_Main` (already set via
`ExpansionConfig.CategoryFor("PoliceImprovements")`), written to `UserData/Expansions.cfg`. The `_01_Main`
suffix is what makes ModsApp and Prowiler's phone app pick it up for free.

| Key | Type | Default | Effect |
|---|---|---|---|
| `intensity_scalar` | `float` | `1.0` | **Master scalar.** Multiplies every heat→world effect: `LE_Intensity` contribution, officer bands, detection scalars, body-search multiplier. `0` = heat tracks but changes nothing (a pure telemetry mode). `2.0` = brutal. |
| `heat_gain_scalar` | `float` | `1.0` | Multiplies all heat *inputs* |
| `heat_decay_per_day` | `float` | `10.0` | Anchored to DESIGN-INTENT §14.2 |
| `heat_decay_per_clean_minute` | `float` | `0.004` | Live drain; subtracted from the daily amount so the clean-day total stays `heat_decay_per_day` |
| `heat_from_deals` | `bool` | `true` | Product volume feeds heat |
| `max_officers_per_post` | `int` | `4` | Clamped to 4 by the game regardless |
| `enable_federal_agents` | `bool` | `true` | |
| `federal_heat_threshold` | `int` | `80` | |
| `federal_agents_per_event` | `int` | `2` | |
| `federal_event_hours` | `int` | `6` | In-game hours before agents withdraw |
| `federal_cooldown_days` | `int` | `2` | |
| `enable_property_raids` | `bool` | `true` | |
| `raid_delay_minutes` | `int` | `30` | Matches the shipped 30-minute logistics quantum |
| `raid_confiscation_fraction` | `float` | `0.5` | Never 1.0 — a total wipe reads as a bug |
| `enable_outlaw` | `bool` | `true` | |
| `outlaw_heat_threshold` | `int` | `90` | |
| `outlaw_clear_days` | `int` | `3` | Consecutive clean in-game days |
| `outlaw_legal_fee` | `int` | `25000` | Shipped Barn price |
| `outlaw_fine_multiplier` | `float` | `2.0` | |
| `outlaw_dealer_cut` | `float` | `0.25` | Shipped flat cut is 0.20 |
| `outlaw_snitch_bonus` | `float` | `0.10` | Shipped chances already span 0–67.1 % |
| `outlaw_blocks_card_vendors` | `bool` | `true` | Ray's Realty + Hyland Auto |
| `enable_debt` | `bool` | `true` | Unpaid fines hit the online balance |
| `confiscate_vehicle_cargo` | `bool` | `true` | Closes the shipped "vehicle cargo is invulnerable" hole |
| `enable_jail_day` | `bool` | `true` | Outlaw arrests cost the rest of the day |
| `show_heat_hud` | `bool` | `true` | |
| `agent_appearance_file` | `string` | `""` | Optional `UserData/` JSON overriding the federal look |
| `debug_probe` | `bool` | `false` | Dump every tuning static and its post-write read-back (§11) |

`intensity_scalar` and `heat_gain_scalar` are hot-reloadable through `ConfigValue<T>.Changed`. Everything
structural (`enable_federal_agents`, `enable_outlaw`) is read at wiring time; changing it mid-session logs
"takes effect on next enable" rather than half-applying.

---

## 10. Edge cases

| Case | Handling |
|---|---|
| **Save / load** | Heat and Outlaw live in `PoliceSaveState` (`AfterBaseGame`, so NPCs exist by `OnLoaded`). `LE_Intensity` is runtime-only in the game and is re-derived on the first `OnUncappedMinPass` after load. `internalLawIntensity` / `Law.json` untouched. On load, re-apply Outlaw world effects from the persisted tier (they are runtime state, not saved by the game). |
| **New save with an existing config** | `PoliceSaveState` starts empty; `HeatDirector` creates a record lazily on first crime. Never assume a record exists. |
| **Sleep / time skip** | `TimeManager.onSleepEnd` applies daily decay **scaled by minutes skipped**, not by one flat day — `onTimeSkip` carries the skipped-minute count `[V]`. Clamp so a 3-day skip cannot drive heat below 0. The game already clears the wanted level on sleep; do not fight it. Federal event timers are in in-game minutes, so a skip correctly expires them. |
| **Curfew interaction** | Heat raises `LE_Intensity`, which can make a `CurfewInstance` clear its `IntensityRequirement` on a night it would otherwise skip — that is intended and native. Never write `CurfewManager.CURFEW_START_TIME` et al.; the verified window (2000 / 2030 / 2100 / 2115 / 500) is player muscle memory and moving it is user-hostile. Crimes during `IsCurrentlyActive` get the ×1.5 multiplier. |
| **Arrested while Outlaw** | Full pipeline: widened confiscation, ×2 fine, debt, vehicle cargo, jail day, processing fee, `OutlawTier` may advance Marked→Hunted. Heat is **not** cleared — that is the whole point of a persistent scalar. |
| **Death while Outlaw** | `PlayerCrimeData.OnDie()` `[V]` runs the game's own reset. We keep Heat and Outlaw, apply no extra penalty (hospital bills are already a shipped cost), and re-apply the `EVisualState.Wanted` label on respawn — visibility state is runtime-only and will have been dropped. |
| **Federal agent killed** | Treat as an arrest-equivalent escalation: heat `+15`, Marked→Hunted. Despawn via the normal path; never leave a corpse tagged, or `CheckDeactivation` keeps firing on a dead object. |
| **Officer pool exhausted** | The game logs `Attempted to dispatch officers, but there are no officers in the pool.` and no-ops `[V]`. Our tier bands are *requests*; never assert they were honoured. Read back `PlayerCrimeData.Pursuers.Count` if you need the truth. |
| **Multiplayer** | Heat is per-player, keyed as in R3. Only the host runs `HeatDirector`, `LawScheduleTuner`, `FederalAgentDirector`, `OutlawState` and `ConsequenceService`. Clients run `PoliceHud` only, reading the replicated `PlayerCrimeData.CurrentPursuitLevel` SyncVar; heat is shown as the last value the host broadcast, or hidden if the mod is host-only in that session. `LawManager.SetWantedLevel` takes a **target player** — never collapse per-player logic to `Player.Local`. |
| **Client without the mod** | Everything still works: we only tune host state and spawn game-native NPCs through FishNet. A vanilla client sees more cops and a federal agent that looks like an unusually well-dressed officer. |
| **Disabling mid-chase** | `Lifetime.OnDispose` runs, in reverse registration order: (1) despawn every tagged agent through `DespawnAgent`, but **only clear the player's pursuit if no vanilla officer remains in `PlayerCrimeData.Pursuers`** — otherwise leave the vanilla chase running; (2) `OutlawState.Revert()` — remove the `"Expansions.Police.Outlaw"` visibility state from every player, restore dealer cuts and `CallPoliceChance`, unblock vendors; (3) `LawScheduleTuner.Restore()` — write back every snapshotted member band and checkpoint window, restore `LE_Intensity`, `UniversalAttentivenessScale`, `UniversalMemoryScale`, `OfficerDispatchMin/Max`, `DISPATCH_VEHICLE_USE_THRESHOLD`, `BODY_SEARCH_CHANCE_DEFAULT` and every per-officer `BodySearchChance`, then `LawController.Instance.CurrentSettings.Evaluate()`; (4) unsubscribe every cached `Il2CppSystem.Action`; (5) `Harmony.UnpatchSelf()` (automatic in `ModuleContext.EndCycle`). |
| **Re-enabling** | `_wired` is false, `OnSceneLoaded` has already fired for `Main`, so nothing re-wires until the next scene load. Fix: `OnEnabled` checks whether the gameplay scene is already active and wires immediately if so. Snapshots are re-taken from the (restored) live values. |
| **Scene unload → main menu** | Drop every snapshot and agent reference; the objects are destroyed. Do not attempt restore-on-unload — the target objects are already gone. Guard every singleton access with `InstanceExists`. |
| **Module throws 10× in a frame hook** | `ModuleContext.ReportPhaseError` auto-disables for the session, which runs the full unwind above. Persisted toggle stays on, so it retries next launch. This is why teardown must be correct even from a half-initialised state — every `Restore`/`Revert` must be null-tolerant and idempotent. |
| **NACops or another police mod installed** | We only postfix `ProcessCrimeList` and never replace it, so fine strings compose. `LawActivitySettings` wholesale replacement (NACops' technique) will clobber our member-band snapshot — detect `schedule-nacops` in `MelonBase.RegisteredMelons` at wiring time and log a compatibility warning; do not attempt to arbitrate. |

---

## 11. Risks and unknowns — with runtime verification

Ordered by how much it hurts to be wrong.

**R1 — `const` inlining of the tuning statics.** *(API-POLICE.md open question #2.)* If any of
`UniversalAttentivenessScale`, `UniversalMemoryScale`, `OfficerDispatchMin/Max`,
`DISPATCH_VEHICLE_USE_THRESHOLD`, `BODY_SEARCH_CHANCE_DEFAULT`, `SEARCH_TIME_*`, `ESCALATION_TIME_*` was a
C# `const`, IL2CPP inlined it at every call site and our write silently does nothing. This is the single
biggest "it compiled, it ran, nothing happened" risk.
**Verify:** ship `RuntimeConstants.Probe()` behind `debug_probe`. For each field: read → write a sentinel
(e.g. `× 7.3`) → read back → restore. A field that reads back the sentinel is *writable*; that still does
not prove the *game reads it*, so pair it with one behavioural check per field —
`UniversalAttentivenessScale = 50f` and stand in plain sight of a cop across the street (they should notice
almost instantly); `OfficerDispatchMax = 4`, run `raisewanted`, and count `PlayerCrimeData.Pursuers`.
**Mitigation already in the design:** every global-static lever has an instance-level or patch-level twin —
per-officer `BodySearchChance` and `VisionCone.RangeMultiplier`/`Attentiveness`/`Memory` instead of the
universal scalars, and the `PoliceStation.Dispatch` prefix (P6) instead of `OfficerDispatchMax`. If the
probe says a static is inlined, flip that lever to its twin at wiring time.

**R2 — Does the game deduct the fine, and where?** *(API-POLICE.md open question #5.)*
`PenaltyHandler.ProcessCrimeList` returns strings only; no `MoneyManager` call site was found in metadata.
If the game *also* charges at `ArrestNoticeScreen.OnClose`/`Exit`, our §5.3 charge double-bills.
**Verify:** `changecash 500`, violate curfew, get arrested. Log `MoneyManager.Instance.cashBalance` from a
Prefix and a Postfix on both `OnClose()` and `Exit()`, and from `Player.onFreed`. Whichever boundary shows
the drop is the game's charge point; subtract it from ours. Also settles open question #6 (whether cash is
confiscated separately) in the same run.

**R3 — Per-player persistence key stability.** `Player.PlayerCode` is a SyncVar string `[V]` and is the id
`BeginFootPursuit(playerCode)` takes, but **nothing proves it is stable across sessions** for the same
human. If it is not, heat resets every load, or worse, transfers to the wrong player in co-op.
**Verify:** log `Player.Local.PlayerCode`, `S1API.Entities.Player.Local.Name` and the Steam id on two
consecutive loads of the same save, then again after a co-op join in a different order.
**Fallback:** single-player stores exactly one record under the key `"local"`; co-op keys on Steam id if
reachable, otherwise degrades to host-only heat with a logged warning.

**R4 — Federal agent spawn viability.** Two unknowns stacked: (a) is `"PoliceNPC"` actually present in
`InstanceFinder.NetworkManager.SpawnablePrefabs` on this IL2CPP build (the name comes from a **Mono**
decompile `[V]`, and `research/API-POLICE.md` concluded there is *no officer prefab path*, which is true of
`Resources` but says nothing about the FishNet registry); (b) does `ServerManager.Spawn` on an
`Instantiate`d copy replicate to a second client.
**Verify:** iterate `prefabs.GetObjectCount()` and log every `GetObject(true, i).gameObject.name` — a
one-shot dump that also answers (a) for every other prefab we might ever want. Then, behind `debug_probe`,
spawn one agent and check it appears for a second client and does not appear in the save after a
save/reload cycle.
**Fallback ladder:** SpawnablePrefabs → NACops live-clone (§3.3) → host-only agents (`SetIsNetworked(false)`,
visible to the host only) → federal agents ship disabled by default with a logged reason.

**R5 — `ApplyShapeKeys` arity.** The IL2CPP interop signature is `ApplyShapeKeys(Single, Single)` `[V]`; the
Mono decompile of EvenMoreFootPatrols calls a three-argument form `[V]`. One of the two branches differs.
**Verify:** `AccessTools.GetDeclaredMethods(typeof(Avatar)).Where(m => m.Name == "ApplyShapeKeys")` and log
the parameter list. Bind reflectively if there is more than one.

**R6 — VMS board text.** `CurfewManager.VMSBoards` is `Il2CppReferenceArray<ObjectScripts.VMSBoard>` `[V]`
but the members of `VMSBoard` were not dumped in the police doc.
**Verify:** reflect over `typeof(VMSBoard)` and log its public members. If there is no settable text member,
drop the feature — it is decoration.

**R7 — Player inventory type for equipment confiscation.** `ArrestNoticeScreen.ConfiscateItems` already
removes contraband, so we only need the inventory type to take *equipment*. The type name is not confirmed
in the research docs.
**Verify:** `typeof(Player).GetProperties()` and log anything inventory-shaped; or
`Resources.FindObjectsOfTypeAll<MonoBehaviour>()` filtered on names containing `Inventory`. If it does not
resolve cleanly, ship without equipment loss — it is the least load-bearing consequence in §5.

**R8 — `TimeManager.StartSleep()` callable without a bed.** Signature is verified `[V]`; the precondition is
not.
**Verify:** call it from a debug key while standing in the street and watch for a sleep transition or an
exception. **Fallback:** `SkipForwardToTime(TimeManager.WakeTime)`, which is a plain time write.

**R9 — `LawActivitySettings` object identity across days.** Our snapshot assumes the `*Instance` objects
reachable from `MondaySettings…SundaySettings` are stable references for the session.
**Verify:** log `GetHashCode()` for a handful of `PatrolInstance` objects on day 1 and day 3. If they are
rebuilt, key the snapshot on `(day, arrayIndex)` instead of the object reference and re-apply on
`onDayPass`.

**R10 — `PoliceOfficer.ShouldSave` is a distinct method from `NPC.ShouldSave`.** P8 depends on it.
**Verify:** `AccessTools.GetDeclaredMethods(typeof(PoliceOfficer)).Any(m => m.Name == "ShouldSave")`. If it
is not declared on `PoliceOfficer`, drop P8 and instead remove the agent from `NPCManager.NPCRegistry`
before any save (hook `S1API`'s save event) — messier but sufficient.

**R11 — `Crime.CrimeName` virtualness** *(open question #7)* and **which field `setlawintensity` writes**
*(open question #3)*. Neither is load-bearing: we never rely on `CrimeName` for logic (we key on
`Il2CppSystem.Type` via `Il2CppType.Of<T>()`, as `IsCrimeOnRecord` does `[V]`), and P3 re-derives the
baseline from whatever the console left behind rather than assuming which field it touched.

---

## 12. Implementation order

Sequenced smallest-risky-part-first. Every milestone is independently testable and independently shippable;
each one leaves the module in a state where disabling it fully unwinds.

**M0 — Runtime probe. No gameplay change.**
`RuntimeConstants.Probe()` behind `debug_probe`: read every static in API-POLICE.md §12, sentinel-write and
read back, log the writable set. Dump every `SpawnablePrefabs` GameObject name. Dump
`typeof(VMSBoard)` / `typeof(Avatar).ApplyShapeKeys` / `AccessTools.GetDeclaredMethods(typeof(PoliceOfficer))`.
Log `PenaltyHandler`'s 14 fine values. Log `Player.Local.PlayerCode` twice across a reload.
**This retires R1, R4a, R5, R6, R10 and half of R3 in one session and costs a day.** Do not skip it —
every later milestone's fallback choice depends on its output.

**M1 — Heat model + persistence + HUD read-out.**
`HeatModel`, `PlayerHeatRecord`, `PoliceSaveState`, `HeatDirector`, patches P1/P2/P4/P5, the arrest and
day/sleep subscriptions, and `PoliceHud` showing `HEAT nn · TIER`. **No world effect at all** —
`intensity_scalar` is ignored at this stage. Confirms the save file lands in
`…\Modded\Saveables\expansions_police.json`, that the key is stable, and that heat moves the way the balance
table says. Retires R3.

**M2 — Intensity → the world, statics only.**
Write `LE_Intensity` + `CurrentSettings.Evaluate()`, detection scalars, dispatch statics, per-officer
`BodySearchChance` sweep, P6. No schedule-data mutation yet, so teardown is trivial. First playable
difficulty change. Verifies that `LE_Intensity` actually moves patrol counts.

**M3 — `LawScheduleTuner`.**
Snapshot/restore + tier-driven `MinMembers`/`MaxMembers` and `CheckpointInstance` window widening. Highest
teardown-correctness burden of any milestone — test enable → play a day → disable → verify vanilla numbers
are back, with a logged before/after diff. Retires R9.

**M4 — Heavier consequences.**
P15–P18, `ConsequenceService`: fine multiplier, confiscation tier widening, vehicle cargo, cash-then-debt.
Retires R2 (which must be settled *before* the multiplier ships). Jail day gated behind R8; ship it disabled
if the probe says `StartSleep()` is unsafe.

**M5 — Outlaw.**
`OutlawState` + P9–P14, P21, P19 HUD banner, phone notifications, the $25,000 clear path. Test the full
latch/clear cycle and, critically, that `Revert()` from every tier leaves dealer cuts and
`CallPoliceChance` at their original values.

**M6 — Federal agents.**
`FederalAgentFactory`, `AgentAppearance`, `FederalAgentDirector`, P7/P8. Spawn → look → tune → dispatch →
despawn. Test the save-pollution case explicitly: spawn an agent, save, reload, confirm it is gone and the
NPC set is unchanged. Retires R4.

**M7 — Stakeouts and raids.**
Property targeting, the raid confiscation pass, the phone message. Last because it is the only feature that
mutates player-owned storage, and because everything else must already be trustworthy before you let the
mod take things out of a safe.

---

## Appendix — verified symbol quick-reference

Everything this plan binds to, with its declaring type. All `[V]` unless marked.

**Law** — `Law.LawController.{LE_Intensity, internalLawIntensity, IntensityIncreasePerDay,
DAILY_INTENSITY_DRAIN, MondaySettings…SundaySettings, CurrentSettings, OverrideSetings(sic), EndOverride,
GetSettings(), GetSettings(EDay), SetInternalIntensity, ChangeInternalIntensity, DayPass, OnUncappedMinPass}` ·
`Law.LawManager.{OfficerDispatchMin, OfficerDispatchMax, DISPATCH_VEHICLE_USE_THRESHOLD, PoliceCalled,
StartFootpatrol, StartVehiclePatrol}` ·
`Law.LawActivitySettings.{Patrols, Checkpoints, Curfews, VehiclePatrols, Sentries, Evaluate, OnLoaded, End}` ·
`Law.PatrolInstance.{Route, MinMembers, MaxMembers, StartTime, EndTime, IntensityRequirement,
OnlyIfCurfewEnabled, ActiveGroup}` ·
`Law.CheckpointInstance.{Location, MinMembers, MaxMembers, StartTime, EndTime, IntensityRequirement,
MIN_ACTIVATION_DISTANCE, EnableCheckpoint, DisableCheckpoint}` ·
`Law.SentryInstance.{MinMembers, MaxMembers, StartTime, EndTime, IntensityRequirement, StartEntry(sic), EndSentry}` ·
`Law.VehiclePatrolInstance.{Route, StartTime, IntensityRequirement}` ·
`Law.CurfewInstance.{ActiveInstance, IntensityRequirement, Enabled}` ·
`Law.CurfewManager.{HOUR_BEFORE_CURFEW 2000, WARNING_TIME 2030, CURFEW_START_TIME 2100,
HARD_CURFEW_START_TIME 2115, CURFEW_END_TIME 500, IsEnabled, IsCurrentlyActive, IsHardCurfewActive,
VMSBoards, onCurfewStart, onCurfewEnd}` ·
`Law.CheckpointManager.{WesternCheckpoint, DocksCheckpoint, NorthResidentialCheckpoint,
WestResidentialCheckpoint, GetCheckpoint, SetCheckpointEnabled}` + `ECheckpointLocation{Western, Docks,
NorthResidential, WestResidential}` ·
`Law.PenaltyHandler.{14 × *_FINE, ProcessCrimeList}` ·
`Law.Crime` + 17 subclasses.

**Police** — `Police.PoliceOfficer.{Officers, OnPoliceVisionEvent, GetNearestOfficer,
OutOfSightTimeToDeactivate, INVESTIGATION_COOLDOWN, INVESTIGATION_MAX_DISTANCE,
INVESTIGATION_MIN_VISIBILITY, INVESTIGATION_CHECK_INTERVAL, BODY_SEARCH_CHANCE_DEFAULT,
MIN_CHATTER_INTERVAL, MAX_CHATTER_INTERVAL, PursuitBehaviour, VehiclePursuitBehaviour, BodySearchBehaviour,
CheckpointBehaviour, FootPatrolBehaviour, VehiclePatrolBehaviour, SentryBehaviour, belt, BatonPrefab,
TaserPrefab, GunPrefab, AutoDeactivate, ChatterEnabled, BodySearchChance, BodySearchDuration, IgnorePlayers,
AssignedVehicle, BeginFootPursuit_Networked, BeginVehiclePursuit_Networked, BeginBodySearch_Networked,
AssignToCheckpoint, AssignToSentryLocation, StartFootPatrol, StartVehiclePatrol, Activate, Deactivate,
CheckDeactivation, ShouldSave, CanInvestigate, CanInvestigatePlayer, ProcessVisionEvent, GetNameAddress}` ·
`Police.NPCResponses_Police.{19 virtual Noticed*/Notice*/RespondTo*/SawPickpocketing/GunshotHeard/HitByCar/ImpactReceived}` ·
`Police.RoadCheckpoint.{MAX_TIME_OPEN, ActivationState, Gate1Open, Gate2Open, AssignedNPCs,
MaxStealthLevel, onPlayerWalkThrough}` · `Police.Investigation.{CurrentProgress, Target, ChangeProgress}`.

**Player / crime** — `PlayerScripts.PlayerCrimeData.{SEARCH_TIME_*, ESCALATION_TIME_*, SHOT_COOLDOWN_*,
VEHICLE_COLLISION_*, CurrentPursuitLevel, LastKnownPosition, Pursuers, NearestOfficer, Crimes,
CurrentArrestProgress, CurrentBodySearchProgress, MinsSinceLastArrested, TimeSincePursuitStart,
TimeSinceSighted, EvadedArrest, onPursuitLevelChange, SetPursuitLevel, SetPursuitLevel_Server, Escalate,
Deescalate, UpdateEscalation, UpdateTimeout, TimeoutPursuit, SetEvaded, AddCrime, ClearCrimes,
IsCrimeOnRecord, GetSearchTime, OnPlayerFreed, OnDie, OnSleepStart, MinPass}` +
`EPursuitLevel{None, Investigating, Arresting, NonLethal, Lethal}` ·
`PlayerScripts.Player.{IsArrested, IsTased, IsRagdolled, IsUnconscious, PlayerCode, onArrested, onFreed,
Arrest_Server, Arrest_Client, Free_Server, Free_Client, GetRandomPlayer, PlayerList}`.

**Behaviours** — `NPCs.Behaviour.PursuitBehaviour.{ARREST_RANGE, ARREST_TIME, EXTRA_VISIBILITY_TIME,
MOVE_SPEED_INVESTIGATING/ARRESTING/CHASE, CHASE_SPEED_DISTANCE_THRESHOLD, ARREST_MAX_DISTANCE,
LEAVE_ARREST_CIRCLE_LIMIT, TargetPlayer, Weapon_Baton/Taser/Gun, arrestingEnabled,
UpdateInvestigatingBehaviour, UpdateArrestBehaviour, UpdateNonLethalBehaviour, UpdateLethalBehaviour,
EndCombat, Disable, SetTarget}` ·
`NPCs.Behaviour.BodySearchBehaviour.{MAX_STEALTH_LEVEL, BODY_SEARCH_RANGE, MAX_SEARCH_TIME,
BODY_SEARCH_COOLDOWN, MaxStealthLevel, DoesPlayerContainItemsOfInterest, ConcludeSearch, Escalate,
onSearchComplete_Clear, onSearchComplete_ItemsFound}` ·
`NPCs.Behaviour.CheckpointBehaviour.{AssignedCheckpoint, IsSearching, DoesVehicleContainIllicitItems,
PlayerWalkedThroughCheckPoint}` ·
`NPCs.Behaviour.{FootPatrolBehaviour, VehiclePatrolBehaviour, SentryBehaviour, CallPoliceBehaviour,
PatrolGroup, FootPatrolRoute, VehiclePatrolRoute, NPCBehaviour, Behaviour}`.

**Vision** — `Vision.VisionCone.{UniversalAttentivenessScale, UniversalMemoryScale, RangeMultiplier,
Attentiveness, Memory, effectiveRange, HorizontalFOV, VerticalFOV, Range, MinVisionDelta, stateSettings,
SetSightableStateEnabled, SetNoticePlayerCrimes}` ·
`Vision.EntityVisibility.{ApplyState(String, EVisualState, Single), RemoveState(String, Single),
ClearStates, CurrentVisibility, Suspiciousness}` · `Vision.EVisualState` · `Vision.VisionEventReceipt`.

**Dispatch / map** — `Map.PoliceStation.{PoliceStations, SpawnPoint, OfficerPool, PoliceVehicles,
Dispatch, PullOfficer, DeployVehicle, ReturnVehicle, GetClosestPoliceStation}` + `EDispatchType` ·
`Map.Map.{GetRegionFromPosition, GetRegionData, GetUnlockedRegions, PoliceStation}` ·
`Map.EMapRegion{Northtown, Westville, Downtown, Docks, Suburbia, Uptown}` ·
`NPCs.NPCManager.{NPCRegistry, GetNPC, GetNPCsInRegion}`.

**Arrest UI / economy** — `UI.ArrestNoticeScreen.{VEHICLE_POSSESSION_TIMEOUT, recordedCrimes, vehicle,
Open, OnClose, Exit, RecordCrimes, RecordPossession, ConfiscateItems}` ·
`UI.ArrestScreen.{Open, Continue, Close}` ·
`UI.CrimeStatusUI.{CrimeStatusContainer, CrimeStatusGroup, BodysearchLabel, InvestigatingMask,
UnderArrestMask, WantedMask, WantedDeadMask, ArrestProgressContainer, UpdateStatus}` ·
`Product.Packaging.EStealthLevel{None, Basic, Advanced}` ·
`Money.MoneyManager.{cashBalance, onlineBalance, ChangeCashBalance, CreateOnlineTransaction, GetNetWorth,
FormatAmount, onNetworthCalculation}` ·
`Quests.Contract.SubmitPayment(Single)` ·
`Storage.StorageEntity.{MAX_SLOTS, SlotCount, ItemSlots, ItemCount, GetAllItems, GetContentsDictionary,
ClearContents, SetStoredInstance, SetItemSlotQuantity, GetNetworth, ContentsChanged}` ·
`Vehicles.LandVehicle.{Storage, Trunk, GetContents}`.

**Time** — `GameTime.TimeManager.{CurrentTime, CurrentDay, ElapsedDays, IsNight, IsSleepInProgress,
WakeTime, EndOfDay, onMinutePass, onUncappedMinutePass, onHourPass, onDayPass, onWeekPass, onTimeSkip,
onSleepStart, onSleepEnd, SetTime, SkipForwardToTime, StartSleep, IsCurrentTimeWithinRange,
GetMinSumFrom24HourTime}` · `GameTime.EDay` · `GameTime.TimedCallback`.

**Avatar** — `AvatarFramework.Avatar.{CurrentSettings, LoadAvatarSettings, ApplyBodySettings,
ApplyBodyLayerSettings, ApplyFaceLayerSettings, ApplyAccessorySettings, ApplyHairSettings,
ApplyHairColorSettings, ApplyEyebrowSettings, ApplyEyeBallSettings, ApplyEyeLidSettings,
ApplyEyeLidColorSettings, ApplyShapeKeys, SetBodyLayer, SetFaceLayer, SetSkinColor}` ·
`AvatarFramework.PoliceBelt.{BatonObject, TaserObject, GunObject, SetBatonVisible, SetTaserVisible,
SetGunVisible}` ·
`AvatarFramework.Customization.BasicAvatarSettings.{Top, Bottom, Shoes, Headwear, Eyewear + colours,
GetAvatarSettings, GetValue<T>, SetValue<T>, GetJson}`.

**S1API** — `S1API.Law.{LawManager, LawController, CheckpointManager, CurfewManager, PursuitLevel,
CheckpointLocation, CheckpointInfo, PatrolGroup}` (note: `LawManager.GetAssignedOfficers` and
`LawController.OverrideActivitySettings` are Mono/IL2CPP signature leaks — call the game type directly) ·
`S1API.Internal.Abstraction.Saveable` · `S1API.Saveables.{SaveableField, SaveableLoadOrder}` ·
`S1API.Entities.Player.{All, Local, CrimeData, CurrentRegion, CurrentProperty, LastVisitedProperty}` ·
`S1API.Entities.NPCDealer` · `S1API.Shops.ShopManager`.

**Console** — `Console+SetLawIntensity` (`setlawintensity`), `Console+RaisedWanted` (sic, `raisewanted`),
`Console+LowerWanted`, `Console+ClearWanted`, `Console+SetPoliceIgnorePlayers`.

**Network** — `Il2CppFishNet.InstanceFinder.{NetworkManager, IsServer, IsHost, IsClientOnly}` ·
`NetworkManager.{ServerManager.Spawn/Despawn, SpawnablePrefabs}` ·
`NetworkObject.{UpdateNetworkBehaviours, Preinitialize_Internal, Initialize, SetIsNetworked, IsSpawned}`.

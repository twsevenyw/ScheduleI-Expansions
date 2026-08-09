# Schedule I — MelonLoader / IL2CPP Modding Ecosystem

> **What this is.** The implementation-facing reference for the Roadmap Expansions workstream: how to build,
> patch, inject, persist and survive updates on this exact stack. Synthesised 2026-08-03 from the raw research
> under `raw/` (`a1-melonloader-s1api.md`, `a2-mods-survey.md`, `c2-ugui-runtime.md`, the S1API docs and the
> downloaded example NPC sources), plus targeted live verification noted inline.
>
> **Evidence labels are used on every non-trivial claim:**
> - **[V]** verified — a primary source was read directly (file, API response, assembly metadata).
> - **[C]** community — a mod page, README, wiki, or Discord-relayed claim. Plausible, not proven.
> - **[I]** inference — my reasoning from the above. Not observed.
> - **[U]** unverified — stated somewhere but contradicted, unlocatable, or untested. **Do not build on it without checking.**
>
> **Deliberately not covered here** (see siblings, do not duplicate):
> - Visual character construction, avatar layers, clothing art specs → `CUSTOM-CHARACTERS.md`
> - Asset extraction / AssetStudio / sprite + font harvesting → `ASSETS-TOOLCHAIN.md`
> - Feature design intent and balance numbers → `DESIGN-INTENT.md`
> - Runtime uGUI/TMP construction detail → `raw/c2-ugui-runtime.md` (and `UI-STYLE.md` when it lands)

---

## 0. Executive summary — the decisions this document supports

| Question | Answer | Confidence |
|---|---|---|
| Custom NPCs | Subclass `S1API.Entities.NPC`, configure in `ConfigurePrefab(NPCPrefabBuilder)`, wire in `OnCreated()`. Never clone prefabs by hand. | **[V]** |
| Reversible game patching | One `HarmonyLib.Harmony` instance per module, id `com.evan.expansions.<id>`, `UnpatchSelf()` on disable — **plus `[HarmonyDontPatchAll]` on each `MelonMod`**, which is currently missing. | **[V]** mechanism, **[I]** the gap |
| Config persistence | `MelonPreferences` for global/mod settings; `S1API.Saveables.Saveable` for per-save-slot state. Two different things, use both. | **[V]** |
| Settings UI | Main-menu screen cloned SideHustle-style (primary) + free pickup by ModsApp / Prowiler's phone app if categories are named right (secondary). | **[V]** technique, **[I]** the ranking |
| Multiplayer | Host-authoritative. `InstanceFinder.NetworkManager == null \|\| InstanceFinder.IsServer`. Never author a custom `NetworkBehaviour`. | **[V]** |
| Loader target | We are on **0.7.1** (2025-06-21). Current stable is **0.7.3** (2026-05-14) and the active ecosystem requires 0.7.2+/0.7.3+. **This is a live gap.** | **[V]** — verified today |

**Top 3 external mods to read, in order:** `DooDesch-Mods/ScheduleOne-Personnel` (MIT) → `XOWithSauce/schedule-nacops` (no licence, read-only) → `DooDesch-Mods/ScheduleOne-SideHustle` (MIT). Rationale in §5.

---

# 1. MelonLoader 0.7.x IL2CPP modding for Schedule I (2026)

## 1.1 Verified environment baseline

Measured from the installed files on this machine, not assumed **[V]**:

| Component | Version | Notes |
|---|---|---|
| MelonLoader | **0.7.1** (`0.7.1+0a690474…`) | `MelonLoader\net6\MelonLoader.dll` |
| `0Harmony.dll` (HarmonyX) | **2.10.2** | Not 2.15. The official S1API template's NuGet `HarmonyX 2.15.0` is compile-time only. |
| Il2CppInterop.{Runtime,Common,Generator,HarmonySupport} | **1.5.0** (`1.5.0-ci.625`) | |
| Mono.Cecil / MonoMod.RuntimeDetour / Newtonsoft.Json | 0.11.6 / 22.07.31.01 / 13.0.3 | |
| Unity | **2022.3.62f2** (`7670c08855a9`) | From `Schedule I.exe` ProductVersion |
| Game | **v0.4.6 / build 0.4.6f11** | Steam news + `s1-codearchiver` auto-commit 2026-08-01 |
| S1API installed / latest | **3.1.4** / **3.1.7** | `S1API.Forked` NuGet, 81 versions |

Consequences that actually bite:

1. **Compile against `MelonLoader\net6\0Harmony.dll`, not a NuGet HarmonyX.** Otherwise you can bind to an API that
   only exists in 2.15 and fail at runtime on 2.10.2. Our `CreativeMode.csproj` already does this correctly **[V]**.
2. **Schedule I is still 0.4.x** and TVGS has publicly announced Special Customers as the next major release.
   Our Special Customers mod is building on ground that is scheduled to move **[V]**.

## 1.2 Loader version — the one thing that changed since the raw research

**Verified live today via the GitHub releases API [V]:**

| Tag | Released | Relevance |
|---|---|---|
| **v0.7.3** | **2026-05-14** | Il2CppInterop → `1.5.1-ci.845`. *"Adjusted `OnPreferencesSaved` and `OnPreferencesLoaded` callbacks to fix an issue with them sometimes not being triggered."* *"Fixed an issue with Automatic Melon Harmony Patching looking for unannotated types."* *"Added `0Harmony.dll` to the list of assemblies to be force-resolved."* |
| v0.7.2 | 2026-03-03 | *"Reimplemented Il2CppInteropFixes"* — the exact defect S1APILoader hand-patches on 0.7.1. Cpp2IL → `2022.1.0-pre-release.21`. |
| v0.7.1 | **2025-06-21** | **What we have.** Over a year old. |

Three things follow, all **[I]** but well-grounded:

- The Cookbook's *"MelonLoader 0.7.1 is a known-bad build for Schedule I"* claim **[C]** is corroborated by the fact
  that S1APILoader ships a 0.7.1-specific workaround that is **live on our machine** — it registers an
  `AssemblyResolve` fallback stripping the `Il2Cpp` prefix, invokes the private
  `MelonLoader.Fixes.Il2CppInteropFixes.Install()`, and forces `LoaderConfig.Current.UnityEngine.ForceRegeneration = true`
  once, guarded by `UserData\S1API.InteropFix.marker` **[V]**.
- The active ecosystem has moved: NACops declares `[assembly: VerifyLoaderVersion("0.7.2", true)]`; the DooDesch
  suite and Hotline require `0.7.3+` **[V]**.
- `OnPreferencesSaved` not always firing on ≤0.7.2 directly undermines the config hot-reload contract that ModsApp
  documents. If our settings screen relies on that callback, **test it or move to 0.7.3.**

**Recommendation:** upgrade the local install to 0.7.3, regenerate `Il2CppAssemblies`, rebuild, and declare
`[assembly: VerifyLoaderVersion("0.7.2", true)]` so a user on an older loader gets a readable refusal instead of a
`TypeLoadException`. Cost is one regeneration cycle.

## 1.3 The `.csproj` shape — and a review of ours

`CreativeMode.csproj` is a working, shipping IL2CPP-only project and is the right template to copy **[V]**. It
already does everything that matters:

- `net6.0`, `CopyLocalLockFileAssemblies=false`, all `<Reference>`s marked `<Private>false</Private>`.
- References the *runtime* MelonLoader/Harmony/Il2CppInterop DLLs from `MelonLoader\net6`, not NuGet.
- Unity + game assemblies from `MelonLoader\Il2CppAssemblies`.
- `PreventS1APICopy` target strips the S1API NuGet DLL out of `ReferenceCopyLocalPaths` so the S1APILoader-managed
  runtime build wins. **This matters** — the Cookbook's rule is *"never bundle your own copy … risks loading a
  second, mismatched version"* **[C]**.
- `DeployToGame` copy with `ContinueOnError`.

`src/Directory.Build.props` replicates the same reference set for the three expansion mods **[V]**. Good.

Deltas worth making, all **[I]**:

| Gap | Why | Fix |
|---|---|---|
| `S1API.Forked` pinned to **3.1.4** in both files | 3.1.7 *"Restored civilian greetings and generic dialogue for custom NPCs created through the current `BaseEmployee` fallback"* and *"Restored residence-door summons for S1API custom NPCs"* **[V]** — both directly on our NPC path | Bump to `3.1.7` before writing NPC code |
| `GameDir` is hard-coded in tracked files | Breaks on any other machine | Move to an untracked `local.build.props` imported with `Condition="Exists(...)"`, as the official template does **[V]** |
| No `<Version>` on the expansion projects other than 0.1.0 | Thunderstore/ModsApp read assembly version | Keep it in sync with `MelonInfo` |
| No `RestorePackagesWithLockFile` / pinned reference assemblies | A game patch silently changes what you compiled against | Optional: `k073l/RefGen` publishes per-version reference-assembly NuGet packages so *"building against an older game version is just a version bump"* **[C]** |

**On referencing `Assembly-CSharp.dll` directly.** S1API's docs say don't: *"Do not add the game's `Assembly-CSharp.dll`
as a reference when using S1API.Forked unless you know what you are doing. Referencing the game's assembly directly
loses the cross compatability of the API."* **[V]**. We already do reference it, deliberately, and that is the right
call here **[I]**: we are IL2CPP-only, and a game rename becomes a *compile* error instead of a runtime
`TypeLoadException`. The discipline that makes it safe is: **use S1API for everything it covers; confine raw
`Il2CppScheduleOne.*` access to isolated adapter files; wrap every adapter call in try/catch + feature detect** (§8).

## 1.4 Assembly attributes every mod must carry

```csharp
[assembly: MelonInfo(typeof(Expansions.HireableDrivers.HireableDriversMod), "Hireable Drivers", "0.1.0", "Evan")]
[assembly: MelonGame("TVGS", "Schedule I")]                 // exact strings [V]
[assembly: MelonColor(255, 90, 170, 240)]                   // cosmetic, console banner
[assembly: MelonPlatformDomain(MelonPlatformDomainAttribute.CompatibleDomains.IL2CPP)]
[assembly: MelonLoader.VerifyLoaderVersion("0.7.2", true)]  // ← add this
```

- `MelonGame("TVGS", "Schedule I")` — verbatim from S1API's own docs and from Personnel's `Core.cs` **[V]**.
- **`MelonPlatformDomain` is the single highest-leverage habit in this ecosystem.** Without it a Mono user sees
  `Could not resolve type with token 0100001c from typeref (expected class 'Il2CppScheduleOne....')`; with it,
  MelonLoader prints *"is incompatible: … only compatible with the following Domain: IL2CPP"* **[C]**, Cookbook,
  sourced to k073l. Our scaffolding already has it **[V]**. Keep it.
- Field diagnostic if you ever see it in a bug report: *"Il2Cpp mods will usually error with `... Version=6.0.0` on
  Mono, while Mono mods will error with `... Version=0.0.0` on IL2CPP"* **[C]**.
- **Do not set `MelonPriority(Int32.MinValue)`** — that is S1API's, and claiming it races you ahead of the API you
  depend on **[V]**.

## 1.5 Lifecycle — when it is safe to touch what

S1API's own root mod is the reference implementation **[V]**:

```csharp
public class S1API : MelonMod
{
    public override void OnInitializeMelon() { ... }
    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        if (sceneName == "Main") GameLifecycle.Initialize();
    }
    public override void OnSceneWasInitialized(int buildIndex, string sceneName) { /* prefab warmup */ }
    public override void OnSceneWasUnloaded(int buildIndex, string sceneName) { ... }
}
```

| Phase | Safe to do | Not safe |
|---|---|---|
| `OnInitializeMelon` | Create `MelonPreferences` categories; register modules; emit dynamic types (Personnel emits its NPC subclasses here, because *"S1API scans at scene init"* **[V]**) | Touch any game object or singleton — no scene exists yet |
| `OnLateInitializeMelon` | Detect other mods (SideHustle, Hotline, ModsApp) | — |
| `OnSceneWasLoaded(_, "Menu")` | Reset menu-injection state | Assume the menu hierarchy exists yet |
| `OnSceneWasInitialized(_, "Menu")` | Begin menu injection **after a ~20-frame warmup** (SideHustle's `WarmupFrames`) **[V]** | Apply gameplay Harmony patches — PropHunt: *"Patching the game's gameplay methods while the Side Hustle hub builds its menu UI intermittently hard-crashes the game"* **[V]** |
| `OnSceneWasInitialized(1, "Main")` | Apply gameplay patches, hook managers | Spawn networked objects immediately |
| `LoadManager.Instance.onLoadComplete` / `S1API.Lifecycle.GameLifecycle.OnLoadComplete` | Read save-dependent state; spawn NPCs/vehicles | — |

**Scene facts, confirmed independently in three repos [V]:** menu scene = `"Menu"`, gameplay scene = `"Main"` at
`buildIndex == 1`. The menu is loaded/unloaded (not persistent) and **can re-initialise more than once per menu
visit** — injection must be idempotent.

**Coroutines:** `MelonCoroutines.Start(IEnumerator)` / `Stop(object)`. Hard rule from the community wiki: *"Don't put
`IEnumerator`s in types registered with `RegisterTypeInIl2Cpp`, as they will not work properly."* **[V]**

## 1.6 File layout — where each DLL goes

| Folder | Contents | Rule |
|---|---|---|
| `Mods\` | `MelonMod` DLLs — our three expansion mods, `CreativeMode.dll`, `S1API.Il2Cpp.MelonLoader.dll` | Scanned for melons |
| `Plugins\` | `MelonPlugin` DLLs — `S1APILoader.MelonLoader.dll`. Runs at `OnApplicationEarlyStart`, before mods | |
| `UserLibs\` | Shared managed libraries — **`Expansions.Core.dll`** | Verified in ML 0.7.1 IL: `MelonFolderHandler.ScanForFolders` scans UserLibs before Plugins/Mods and registers it with `MelonAssemblyResolver.AddSearchDirectory` **[V]**, per `CONTEXT.md` |
| `UserData\` | `MelonPreferences.cfg`, our `Expansions.cfg`, JSON configs, S1APILoader's interop marker | |

**S1APILoader's guarantee, read from source [V]:** it renames `Mods\S1API.{Mono,Il2Cpp}.MelonLoader.dll` ⇄ `.disabled`
*before* MelonLoader scans, so exactly one build ever loads; if duplicates exist it picks the higher assembly version,
falling back to last-write time. Load order is therefore: plugin → S1API (`MelonPriority(Int32.MinValue)`) → us.

## 1.7 IL2CPP language-level rules (these cause most crashes)

| Rule | Wrong | Right | Src |
|---|---|---|---|
| No LINQ over `Il2CppSystem.Collections.Generic.List<T>` | `list.FirstOrDefault()` | index-based `for` loop (S1API's own style), or `list._items.FirstOrDefault()` | **[V]** |
| Casting | `(Foo)obj` | `obj.Cast<Foo>()` (throws) / `obj.TryCast<Foo>()` (null) | **[V]** |
| Runtime type tokens | `typeof(Camera)` | `Il2CppType.Of<Camera>()` | **[V]** |
| `UnityAction` from a lambda | `btn.onClick.AddListener(() => …)` | `btn.onClick.AddListener((UnityAction)(() => …))` — or better, `S1API.Utils.ButtonUtils.AddListener(button, action)` | **[V]** |
| Game-defined delegates | `new S1GameInput.ExitDelegate(H)` | `DelegateSupport.ConvertDelegate<S1GameInput.ExitDelegate>(new Action<S1ExitAction>(H))` | **[V]** |
| Converted delegate lifetime | local variable | **cache in a field for the object's lifetime** — otherwise it is GC'd and native code calls freed memory. S1API's `PhoneApp.cs` comments say so explicitly | **[V]** |
| `string[]` params | `string[]` | `Il2CppStringArray` (`Il2CppInterop.Runtime.InteropTypes.Arrays`) | **[V]** import exists; **[U]** no concrete Schedule I call site verified |
| Managed convenience wrappers | `AssetBundle.LoadFromMemory`, `GUI.DrawTexture`, `GUI.TextField` | stripped on this build. Use `Il2CppAssetBundleManager` / absolute-`Rect` IMGUI | **[V]** for AssetBundle (two independent mods document *"Method unstripping failed"*); **[C]**+local for the IMGUI members |

**Generalise it:** on this IL2CPP build, assume *any* managed convenience wrapper over a native Unity API may be
stripped. Prefer the game's own live objects or MelonLoader's native-ICall managers **[I]**.

---

# 2. Harmony patching under Il2CppInterop

## 2.1 What works and what doesn't

- **HarmonyX 2.10.2**, with `Il2CppInterop.HarmonySupport.dll` 1.5.0 teaching it to detour IL2CPP native methods **[V]**.
- **No transpilers, ever.** *"Transpilers modify the IL code, and thus, will not work on IL2CPP builds… Transpilers
  are only available for Mono builds."* **[V]**, community wiki; the S1API template repeats it as a hard rule.
  (Note: OTC *does* transpile — but it transpiles **S1API's own managed assembly**, not the game **[V]**. That's legal
  because S1API is a normal .NET assembly. Do not read it as permission to transpile `Il2CppScheduleOne.*`.)
- Prefix / postfix / finalizer only.

## 2.2 Attribute patching — the 80% case

```csharp
[HarmonyPatch(typeof(Il2CppScheduleOne.Law.LawManager), nameof(Il2CppScheduleOne.Law.LawManager.PoliceCalled))]
internal static class LawManager_PoliceCalled_Patch
{
    // __instance types as the INTEROP wrapper type, never the Mono type.
    static bool Prefix(Il2CppScheduleOne.Law.LawManager __instance,
                       Il2CppScheduleOne.PlayerScripts.Player target,
                       Il2CppScheduleOne.Law.Crime crime)
    {
        // return false to skip the original
        return true;
    }
}
```

`__instance` is the `Il2CppScheduleOne.*` wrapper **[I]** from the namespace-prefix rule, corroborated by every S1API
patch compiling against `Il2CppScheduleOne.*` under `IL2CPPMELON` **[V]**. `ref TReturn __result` works normally **[V]**.

## 2.3 Manual patching — when attributes aren't enough

Use for: overloads, generics, runtime-resolved names, and anything you want to be able to *not* apply.

```csharp
MethodInfo target = typeof(TargetClass).GetMethod("TargetMethod", new[] { typeof(int) });
harmony.Patch(target,
              prefix:  new HarmonyMethod(typeof(MyPatch), nameof(MyPatch.Pre)),
              postfix: null,
              finalizer: null);
```
**[V]**, community wiki. `AccessTools.Method(type, name, argTypes)` is the more robust resolver and is what NACops uses
on Mono for non-public members **[V]**.

**Patch every overload when you don't know which exists.** Cartel Enforcer and NACops both patch **all three**
`Console.SubmitCommand` overloads — `(List<string>)`, `(Il2CppSystem.Collections.Generic.List<string>)` and
`(string)` — precisely because which one exists varies by version **[V]**.

## 2.4 Virtual, generic and interface targets

| Target kind | Guidance | Confidence |
|---|---|---|
| **Virtual / overridden** | Patch the **most-derived declaring type** you actually care about. Patching a base declaration does *not* intercept a subclass override under IL2CPP, because each override is a separate native method. Verify with `AccessTools.GetDeclaredMethods(type)`. | **[I]** — standard Harmony semantics; not verified against a Schedule I case |
| **Interface methods** | Not patchable directly — there is no body. Patch each implementing type's method. | **[I]** |
| **Generic methods / generic type members** | Under IL2CPP a generic is only present for instantiations the AOT compiler emitted. `harmony.Patch` on an open generic silently does nothing useful; you must resolve the closed instantiation (`method.MakeGenericMethod(...)`) and even then it may not exist. **Avoid generic targets.** | **[I]**, and treat as **[U]** until you test it |
| **Property getters/setters** | `AccessTools.PropertyGetter(type, "Name")`. Cartel Enforcer patches `Quest.ActiveEntryCount` (getter) this way **[V]**. | **[V]** |
| **Coroutines / `IEnumerator`** | Patching the coroutine method intercepts *enumerator creation*, not execution. `AccessTools.EnumeratorMoveNext` exists for this, but under IL2CPP the nested `<Name>d__NN` state machines may be mangled or inlined. **Don't.** The template's rule: *"Patch the method that owns the state transition."* | **[V]** for the rule, **[U]** for whether `MoveNext` patching works here |
| **Constructors** | `AccessTools.Constructor(type, argTypes)`. In practice everyone patches `Awake`/`Start` instead — S1API patches `NPC.Awake`, `Customer.Awake`, `Dealer.Awake`, `Player.Awake`, `ParkingLot.Awake`, … **[V]**. Copy that. | **[V]** |

## 2.5 FishNet compiler-generated methods

The decompiled `Employee.cs` contains members like `RpcWriter___Observers_Initialize_2260823878`,
`RpcLogic___Initialize_2260823878`, `RpcReader___Target_Initialize_2260823878`, and
`SyncAccessor__003CPaidForToday_003Ek__BackingField` **[V]**. **The numeric suffixes are hash-derived and change
between game versions.** The template's rule: *"Resolve generated RPC/wrapper methods by stable prefix/signature, not
suffixes."* **[V]**

```csharp
static MethodInfo? FindGenerated(Type owner, string stablePrefix, int argCount)
{
    foreach (var m in owner.GetMethods(BindingFlags.Instance | BindingFlags.Static |
                                       BindingFlags.Public   | BindingFlags.NonPublic))
        if (m.Name.StartsWith(stablePrefix, StringComparison.Ordinal) &&
            m.GetParameters().Length == argCount)
            return m;
    return null;
}
```
**[I]** — assembled from the verified rule; not run.

This is directly on the Hireable Drivers path: `Employee.Initialize` is `[ObserversRpc]`/`[TargetRpc]`,
`SendFire`/`SendTransfer` are `[ServerRpc]`, and `PaidForToday` is a SyncVar **[V]**. **Any driver mod is a networked
mod whether you want it or not.**

## 2.6 Reversible patching — and the bug in our current scaffolding

`HarmonyLib.Harmony.UnpatchSelf()` removes every patch owned by that instance's **id**. Our `ModuleContext` already
does the right thing **[V]**:

```csharp
HarmonyId = $"com.evan.expansions.{module.Id}";
Harmony   = new HarmonyLib.Harmony(HarmonyId);       // recreated on every enable
...
try { Harmony.UnpatchSelf(); }
catch (Exception ex) { Log.Error("Harmony.UnpatchSelf() failed; some patches may still be live.", ex); }
```

Because the id is stable and the instance is recreated per enable-cycle, `UnpatchSelf()` on a fresh instance still
removes the previous cycle's patches — that is correct, not a bug.

**The gap:** MelonLoader automatically runs `PatchAll` over each mod assembly using the **`MelonMod`'s own**
`HarmonyInstance`, whose id is the melon's, not `com.evan.expansions.<id>`. Any `[HarmonyPatch]`-annotated class in
`Expansions.HireableDrivers.dll` will therefore be applied by MelonLoader at load and **will not be removed** by the
module's `UnpatchSelf()`. The 0.7.3 changelog line *"Fixed an issue with Automatic Melon Harmony Patching looking for
unannotated types"* confirms the auto-patch pass exists and scans the assembly **[V]**. None of our three mod entry
classes currently carries `[HarmonyDontPatchAll]` **[V]**.

**Fix — pick one and apply it to all three mods:**

```csharp
[HarmonyDontPatchAll]                                  // MelonLoader attribute
public sealed class HireableDriversMod : MelonMod { ... }
```
…**or** keep every patch class free of `[HarmonyPatch]` attributes and apply them manually from
`OnEnabled` via the module's own `Harmony` instance. The second is stricter and I'd prefer it **[I]**, because it also
gives you per-feature granularity (enable "federal agents" without enabling "outlaw status").

```csharp
// Inside a module's OnEnabled — everything here is undone by UnpatchSelf().
var target = AccessTools.Method(typeof(Il2CppScheduleOne.Law.PenaltyHandler), "ProcessCrimeList");
if (target != null)
    Harmony.Patch(target, prefix: new HarmonyMethod(typeof(PenaltyPatches), nameof(PenaltyPatches.Pre)));
else
    Log.Warning("PenaltyHandler.ProcessCrimeList not found — fine overrides disabled this session.");
```
**[I]** — pattern assembled from verified APIs.

## 2.7 Two hard-won timing/robustness rules

**Patch late.** PropHunt, verbatim: *"Gameplay patches are applied lazily on the first gameplay scene (see
`OnSceneWasInitialized "Main"`), NOT here at load. Patching the game's gameplay methods while the Side Hustle hub
builds its menu UI intermittently hard-crashes the game."* **[V]**

**Never let a failed patch take the mod down.** Personnel, verbatim:
`try { HarmonyInstance.PatchAll(); } catch (Exception e) { Log.Warning("Console commands unavailable: " + e.Message); }`
— *"A failed patch costs the console bridge, not the library, so the roster still loads either way."* **[V]**
SideHustle's injector does the same and sets a latch so it doesn't retry every frame.

## 2.8 Verified patch-target catalogue

Every symbol below was read out of a real mod's source **[V]**. Use these as your starting map rather than guessing.

**Police / law** (NACops): `PursuitBehaviour.UpdateArrest`, `PursuitBehaviour.UpdateLethalBehaviour`,
`PursuitBehaviour.OnCurrentWeaponChanged`, `PoliceOfficer.GetNameAddress`, `PenaltyHandler.ProcessCrimeList`,
`VisionCone.SetSightableStateEnabled`, `PropertyDoorController.CanPlayerAccess`, `Player.ConsumeProduct`,
`Customer.ProcessHandover`, `Customer.SampleOffered`, `SaveManager.Save(string)`/`Save()`, `LoadManager.ExitToMenu`,
`DeathScreen.LoadSaveClicked`, all three `Console.SubmitCommand` overloads.

**Hostile-faction events** (Cartel Enforcer, MIT): `CartelGoon.Spawn`/`.Despawn`, `Dealer.DealerUnconscious`,
`Dealer.TryRobDealer` (`// This is only reached by FishNet Instance IsServer`), `CartelDealManager.CompleteDeal`/`.ExpireDeal`,
`Ambush.SpawnAmbush`, `CombatBehaviour.SetTarget_Client`, `CallPoliceBehaviour.IsTargetValid`, `NPC.ProcessImpactForce`,
`Customer.SampleConsumed`/`.GetSampleSuccess`/`.OnCustomerUnlocked`, `DialogueController_Dealer.CheckChoice`/`.ChoiceCallback`.

**Customers / contracts / phone** (OTC): `Customer.OfferContract`, `.OfferContractToDealer`, `.NotifyPlayerOfContract`,
`.AcceptContractClicked`, `.ContractRejected`, `.ProcessHandover`, `.SetUpResponseCallbacks`, `Contract.UpdateTiming`,
`Contract.SubmitPayment`, `HandoverScreen.Open`/`.Close`, `PhoneApp.OpenApp`, `MapApp.SetOpen`,
`CompassManager.AddElement`, `StaticDoor.NPCSelected`, `Supplier.EndMeeting`/`.GetAppropriateLocation`/`.MeetAtLocation`,
`FleeBehaviour.Activate`.

**NPC health / civilians** (LooseEnds, MIT): `NPCHealth.NotifyAttackedByPlayer`, `NPCHealth.Die`, `NPCHealth.KnockOut`,
`Draggable.StartDragging`/`.StopDragging`.

**NPC logistics / storage** (DealerTransportMod): `NPC.Update`, `StorageMenu.Open(StorageEntity)`, `StorageMenu.CloseMenu`,
`StorageEntity.OnClosed`, `SaveManager.Save(string)`, `LoadManager.StartGame`.

**S1API's own ~90 targets** are the single best hook catalogue in the ecosystem **[V]** — including `NPCsLoader.Load`,
`NPC.Awake`/`.Start`/`.OnDestroy`/`.WriteData`/`.GetSaveData`, `NPCManager.GetNPC`, `NPCScheduleManager.InitializeActions`,
`Customer.Awake`, `Dealer.Awake`/`.Load`, `LoadManager.StartGame`/`.LoadAsClient`/`.ExitToMenu`, `HomeScreen.Start`,
`DealerManagementApp.SetOpen`/`.Refresh`, `DeliveryApp.SetIsAvailable`, `VehicleManager.LoadVehicle`,
`LandVehicle.OnDestroy`/`.SetVisible`, `Console.Awake`/`.SubmitCommand`, `ParkingLot.Awake`, `DeliveryLocation.Awake`.
**Check this list before you patch anything — if S1API already patches it, patching it again risks ordering conflicts.**

---

# 3. Custom class injection (`ClassInjector`)

This matters because the three mods need custom NPC/employee/customer behaviour.

## 3.1 The two constructor forms

Canonical shape, quoted from the community wiki **[V]**:

```csharp
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;

[RegisterTypeInIl2Cpp]
public class MyCommand : ConsoleCommand
{
    // Required always: IL2CPP hands you a native pointer when the runtime creates the object.
    public MyCommand(IntPtr ptr) : base(ptr) { }

    // Required ONLY if you `new MyCommand()` from managed code.
    // Do NOT add this for MonoBehaviours — you AddComponent them instead.
    public MyCommand() : base(ClassInjector.DerivedConstructorPointer<MyCommand>())
        => ClassInjector.DerivedConstructorBody(this);
}
```

The alternative to the attribute is an explicit call at init: `ClassInjector.RegisterTypeInIl2Cpp<T>()` **[V]**.
OTC does **both** on the same types (`[RegisterTypeInIl2Cpp]` *and* `ClassInjector.RegisterTypeInIl2Cpp<HUDOverlay>()`)
**[V]** — belt and braces, harmless.

NACops' `MonoBehaviour` form, which is the one to copy **[V]**:

```csharp
[RegisterTypeInIl2Cpp]
public class HylandFlockInstance : MonoBehaviour
{
    public HylandFlockInstance(IntPtr ptr) : base(ptr) { }
    public HylandFlockInstance() : base(ClassInjector.DerivedConstructorPointer<HylandFlockInstance>())
        => ClassInjector.DerivedConstructorBody(this);
}
```

Note S1API's own injected `MonoBehaviour` (`PhoneAppButtonHandler`) carries **neither** constructor **[V]** — consistent
with the wiki's note that the derived constructor is only needed when you `new` the type. Both styles ship and work.

## 3.2 Rules for injected types

| Rule | Detail | Src |
|---|---|---|
| No `IEnumerator` members | Coroutines on injected types misbehave. Move the iterator to a plain managed class, drive it with `MelonCoroutines.Start`. | **[V]** |
| Register before use | `[RegisterTypeInIl2Cpp]` is processed by MelonLoader at melon load; explicit `RegisterTypeInIl2Cpp<T>()` must run before the first `AddComponent`. | **[V]** |
| Managed-only members | The template says they *"may need `[HideFromIl2Cpp]`"* — but there is **zero actual usage** of that attribute anywhere in S1API's source. | **[U]** — unverified in practice |
| Fields | Keep them simple. Interop-visible fields must be interop-representable types. | **[I]** |
| Type identity | Injected types are matched by **simple type name** in some pipelines (see below) — keep names globally unique. | **[V]** |

## 3.3 Subclassing *game* types — what's actually proven

This is the crux for Hireable Drivers, and the honest answer is nuanced.

| Approach | Proven? | Evidence |
|---|---|---|
| Inject a fresh `MonoBehaviour` and attach it to a game object | **Yes, routine.** NACops (`HylandFlockInstance`), OTC (5 overlay types), LawEnforcementEnhancementMod (`OfficerSpawnSystem`), S1API (`PhoneAppButtonHandler`) | **[V]** |
| Subclass a game **abstract/plain** type | **Yes.** Cartel Enforcer subclasses the game's `Quest`: `[RegisterTypeInIl2Cpp] public class Quest_TrucedRecruits : … { … DerivedConstructorPointer … }`. The wiki's own example subclasses `ConsoleCommand`. | **[V]** |
| Subclass a game **`NetworkBehaviour`** (`NPC`, `Employee`, `PoliceOfficer`, `Customer`) | **No mod in the survey does this.** On IL2CPP the FishNet codegen never runs against a mod assembly, so a derived `NetworkBehaviour` has no generated serializers, no RPC links, and its internal dictionaries come up as **garbage rather than empty** — you'd have to hand-initialise `_rpcLinks`, `_syncVars`, `_syncObjects`, `_bufferedRpcs`, `_serverRpcDelegates`, `_observersRpcDelegates`, `_targetRpcDelegates`, `_reconcileRpcDelegates` in `OnStartNetwork()`. | **[C]** Cookbook, sourced to XmusJackson |

**Conclusion [I], and it is load-bearing for all three mods: do not subclass `Employee`, `NPC`, `PoliceOfficer` or
`Customer`.** Use one of the three patterns that are actually proven instead:

### Pattern A — clone a live game object and re-initialise it (NACops)

The single most valuable code block in the whole survey. This is how you get a **new, working, networked** NPC at
runtime on IL2CPP without a registered network prefab **[V]**:

```csharp
// abridged from schedule-nacops/Source/Officer/CopInitHelper.cs:53-116 — READ-ONLY, no licence
PoliceOfficer officer = UnityEngine.Object.FindObjectOfType<PoliceOfficer>();
AvatarSettings copySettings = officer.Avatar.CurrentSettings;
GameObject obj = officer.gameObject;
obj.SetActive(false);                                   // clone while inactive

GameObject clone = UnityEngine.Object.Instantiate(obj);
NPC           npc    = clone.GetComponent<NPC>();
NetworkObject newNob = clone.GetComponent<NetworkObject>();
PoliceOfficer offc   = clone.GetComponent<PoliceOfficer>();
offc.AutoDeactivate = false;                            // don't return to station / join the pool

yield return MelonCoroutines.Start(InitiateClone(newNob, networkManager));

npc.NPCData.BasicInfo.ID = "officerPrefab";
if (!NPCManager.NPCRegistry.Contains(npc)) NPCManager.NPCRegistry.Add(npc);
npc.Avatar.LoadAvatarSettings(copySettings);

try { networkManager.ServerManager.Spawn(newNob); } catch (Exception ex) { Log(ex); }
obj.SetActive(true);                                    // restore the donor
```

with the network re-init trio:

```csharp
// abridged from CopInitHelper.cs:118-228
byte componentIndex = 0;
newNob.UpdateNetworkBehaviours(newNob, ref componentIndex);
newNob.Preinitialize_Internal(netManager, 150, null, true);
newNob.Initialize(true, true);
newNob.SetIsNetworked(false);
```

Two non-obvious details in that file, both worth stealing conceptually: the clone's `Avatar`, `Avatar/BodyContainer`
and `NavMeshAgent` must be **activated for one frame then deactivated** so unassigned fields populate, and the
`"Customer attend deal"` behaviour must be **destroyed** rather than disabled because disabling it NREs **[V]**.
Cartel Enforcer's `InitHelper.cs` is the same technique, and Cartel Enforcer is **MIT** — prefer reading that copy.

### Pattern B — emit subclasses at runtime with `Reflection.Emit` (Personnel)

Personnel does **not** use `ClassInjector` at all. It emits `PersonnelNpc` subclasses into a non-collectible
`AssemblyBuilder` named `Personnel.Generated`, because S1API's assembly scan does the discovery **[V]**. Three
constraints it documents in comments, all of which apply to us if we ever go dynamic:

```
- S1API derives the prefab name from the SIMPLE type name ("S1API_" + Type.Name) and reconstructs
  client wrappers by matching simple names across all assemblies — names must be deterministic,
  session-stable and globally unique.
- The assembly name must not match S1API's discovery skip-list (System/Unity/Il2Cpp/Mono./__Generated/...).
- Emission must happen before the main scene loads (S1API scans at scene init); Personnel emits
  during OnInitializeMelon.
```
plus, critically for co-op:
```csharp
// FishNet spawnable registration is order-sensitive and co-op peers must agree.
wanted.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
```

**For our three mods we don't need dynamic emission** — the customer groups are a fixed, authored set, so plain
compile-time `NPC` subclasses are simpler and safer **[I]**. Keep Pattern B in reserve if Special Customers ever
becomes data-driven.

### Pattern C — let S1API own the prefab (default choice)

Just subclass `S1API.Entities.NPC` (§4.3). S1API handles prefab creation, network registration, save persistence and
client wrapper reconstruction. This is Pattern B's foundation and it is what Personnel, the S1API examples and
BigWillyMod all do **[V]**.

### One more S1API contract detail that will cost you an afternoon

```csharp
/// DefId must return a compile-time constant: S1API calls ConfigurePrefab, IsDealer and IsPhysical on an
/// UNINITIALIZED instance, so none of them may depend on constructor-set fields.
```
**[V]**, Personnel. `IsPhysical`/`IsDealer`/`IsSupplier` and everything read inside `ConfigurePrefab` must be constants
or static lookups.

---

# 4. S1API — full public surface, and what NOT to reimplement

## 4.1 Identity, licence, version

| Fact | Value |
|---|---|
| Repo | **<https://github.com/ifBars/S1API>**, branch `stable`, **MIT** **[V]** |
| It's a fork of | `KaBooMa/S1API` — abandoned (last push 2025-05-23), **no LICENSE file** despite its nuspec claiming MIT **[V]** |
| NuGet package | **`S1API.Forked`** (author `ifBars`) — 81 versions, 1.6.7 → **3.1.7**. Plain `S1API` is the dead upstream. **[V]** |
| Ships | one `lib/netstandard2.1/S1API.dll` (the **Mono** build) for compile; the IL2CPP runtime build (`S1API.Il2Cpp.MelonLoader.dll`) comes from the release ZIP/Thunderstore **[V]** |
| Only runtime dep | Newtonsoft.Json 13.0.2 (ML ships 13.0.3 — resolves fine) **[V]** |
| Self-reported coverage | **32.0%** of the game's classes as of 2026-08-01 **[V]** |
| Surface size | 3,806 documented members across 760 types (parsed from `S1API.xml` 3.1.4) **[V]** |
| Test asymmetry | *"434 Mono contract tests, focused IL2CPP contract coverage"* **[V]** — **IL2CPP is the less-tested path, and it's ours** |

Two internal conventions to know: S1API uses `#if IL2CPPMELON` / `#elif MONOMELON` **internally**; the convention for
*your* mod is `#if IL2CPP` / `#if MONO` **[V]**. Don't mix them. And **`ModSaveableRegistry` is obsolete — *"do NOT use
it"*** **[V]**.

## 4.2 Do NOT reimplement these

Anything in this table is already wrapped, cross-runtime, and maintained by someone else's release cadence. Every
S1API release is a free compatibility patch you didn't write **[I]**, strongly supported by §8.1.

| Need | Use | Src |
|---|---|---|
| Create a custom NPC (physical or contact) | `S1API.Entities.NPC` + `NPCPrefabBuilder` | **[V]** |
| NPC appearance / layers / impostors | `NPCAppearance` + `AvatarDefaultsBuilder` | **[V]** |
| NPC dialogue trees + choice callbacks | `NPCDialogue` | **[V]** |
| Customer economics (spend, orders/week, standards, affinities, police-call chance) | `CustomerDataBuilder` | **[V]** |
| Dealer economics (signing fee, cut, home, recommendations) | `DealerDataBuilder` | **[V]** |
| NPC schedules (walk / stay / vending / dialogue / drive-to-carpark / smoke break) | `PrefabScheduleBuilder` | **[V]** |
| Wanted level, pursuit escalation, patrol routes, checkpoints, curfew | `S1API.Law.LawManager` + 10 sibling types | **[V]** |
| Storage read/write and slot management | `S1API.Storages.StorageManager` / `StorageInstance`, `S1API.Storage.StorageEntity` | **[V]** |
| Vehicles: create, spawn, park, colour, storage | `S1API.Vehicles.VehicleRegistry` / `LandVehicle` | **[V]** |
| Money, net worth, transactions | `S1API.Money.Money` | **[V]** |
| Game time + hour/day/week/sleep events | `S1API.GameTime.TimeManager` | **[V]** |
| Player state (health, arrest, vehicle, region, crime data) | `S1API.Entities.Player` | **[V]** |
| Quests with map POIs and entries | `S1API.Quests.Quest` / `QuestManager` | **[V]** |
| Per-save-slot JSON persistence | `S1API.Internal.Abstraction.Saveable` + `[SaveableField]` | **[V]** |
| Console commands (yours) and built-ins (theirs) | `BaseConsoleCommand`, `ConsoleHelper` | **[V]** |
| Phone apps | `S1API.PhoneApp.PhoneApp` | **[V]** |
| Contracts / deal offers | `S1API.Economy.ContractInfoBuilder`, `NPCCustomer.OfferContract` | **[V]** |
| IL2CPP-safe button/event listeners | `S1API.Utils.ButtonUtils` / `EventHelper` | **[V]** |

**And the gaps you must build yourself:**

| ⛔ Gap | Impact |
|---|---|
| **No employee hire/fire/assign/pay API.** `S1API.Entities.Employees` is exactly two types — `EmployeeManager.GetAppearance/GetRandomAppearance` and `EmployeeAppearance` — confirmed by exhaustive grep of the 3.1.4 XML **[V]** | **Hireable Drivers cannot be pure-S1API.** Go direct to `Il2CppScheduleOne.Employees.*` |
| **No main-menu screen API.** `S1API.UI` has 3 public types; `MainMenuRig` exposes only `Avatar` and `FindInScene` **[V]** | Settings screen is entirely ours (see §7.3) |
| **`UIFactory` renders in Arial + built-in Unity UI skin, not the game's TMP fonts** — verbatim: `txt.font = Resources.GetBuiltinResource<Font>("Arial.ttf")` **[V]** | Anything built with it looks like a mod. Its docs' *"match the game's aesthetic"* claim is generous **[I]** |
| `S1API.Internal.*` is public-namespaced but mostly `internal` — `ReflectionUtils`, `CrossType`, `NPCTypeUtils` are all tempting and all inaccessible **[V]** | Rewrite the ~8 reflection helpers yourself (§8.1) |
| Two-thirds of the game is unwrapped **[V]** | Expect raw interop for anything ambitious |

## 4.3 Custom NPCs — the central capability

### The two-phase model (the most important rule in the system)

> - `ConfigurePrefab(...)`: saved defaults (identity, relationships, schedules, customer/dealer defaults)
> - `OnCreated()`: runtime wiring (build avatar, dialogue callbacks, event subscriptions)

**[V]**, S1API docs. Registration is by **auto-discovery** — you never call `Register()`. S1API scans loaded assemblies
for `NPC` subclasses. Your type must be `public`, non-abstract, with a parameterless constructor **[V]**.

⚠️ **Doc discrepancy, resolved:** `custom-npcs.md` declares `protected override bool IsPhysical`;
`basic-npc-creation.md` and the source (`NPC.cs:2303 public virtual bool IsPhysical => false;`) say **`public
override`** **[V]**. The source wins; every real example uses `public override`.

### `NPCPrefabBuilder` — complete fluent surface

Extracted from `S1API/Entities/NPCPrefabBuilder.cs`; line numbers are real **[V]**.

| Method | Line |
|---|---|
| `WithIdentity(string id, string firstName, string lastName)` | 104 |
| `WithIcon(Sprite icon)` | 135 |
| `WithAppearanceDefaults(Action<AvatarDefaultsBuilder>)` | 157 |
| `WithSchedule(Action<PrefabScheduleBuilder>)` / `(IEnumerable<IScheduleActionSpec>)` / `(params IScheduleActionSpec[])` | 267 / 289 / 316 |
| `WithSpawnPosition(Vector3, Quaternion)` / `(Vector3)` | 545 / 556 |
| `WithRegion(Region)` | 594 |
| `EnsureCustomer()` / `WithCustomerDefaults(Action<CustomerDataBuilder>)` | 87 / 479 |
| `EnsureDealer()` / `WithDealerDefaults(Action<DealerDataBuilder>)` | 335 / 570 |
| `EnsureSupplier()` / `WithSupplierDefaults(Action<SupplierDataBuilder>)` | 463 / 616 |
| `WithRelationshipDefaults(Action<NPCRelationshipDataBuilder>)` | 517 |
| `WithInventoryDefaults(Action<RandomInventoryItemsBuilder>)` | 1039 |
| `WithVoice(NPCVoiceDefinition)` / `(…, float pitch)` / `(string)` / `(string, float)` | 383 / 395 / 405 / 417 |
| `EnsureSmokeBreak(string? cigarettePrefabPath = null, bool? debugMode = null)` | 638 |
| `EnsureGraffiti(string?)` / `(EquippablePath)` | 817 / 821 |
| `EnsureDrinking(string?)` / `(EquippablePath)` | 934 / 976 |
| `EnsureItemHolding(string?)` / `(EquippablePath)` | 987 / 1028 |
| nested `AvatarDefaultsBuilder`: `WithFaceLayer`, `WithBodyLayer`, `WithAccessoryLayer`, `WithImpostor(string\|AvatarImpostorDefinition)`, `WithImpostorTexture(Texture2D)`, `WithRandomImpostor(…)` | 1347–1450 |

### `NPC` runtime surface

```csharp
// Overridables                                                  // NPC.cs line
protected virtual void ConfigurePrefab(NPCPrefabBuilder builder) //  2113
protected virtual void OnResponseLoaded(Response response)       //  2120
protected override void OnCreated()                              //  2129
public   virtual bool IsPhysical => false;                       //  2303
public   virtual bool IsDealer   => false;                       //  2313
public   virtual bool IsSupplier => false;                       //  2322

// Identity / transform / state
public GameObject gameObject;  public Transform Transform;  public string FullName;
public bool IsConscious / IsInBuilding / IsInVehicle / IsPanicking / IsUnsettled / IsVisible;
public bool IsKnockedOut / IsDead;  public float CurrentHealth;  public LandVehicle? CurrentVehicle;

// Actions
public void Revive() / Damage(int) / Heal(int) / Kill() / KnockOut();
public void Unsettle(float duration) / Panic() / StopPanicking();
public void LerpScale(float scale, float lerpTime);  public void Goto(Vector3 position);
public void SetEquippable(string assetPath);  public void SetEquippable(Equippables.EquippablePath path);

// Subsystem components (lazily created wrappers)
public NPCAppearance Appearance;  public NPCMovement Movement;  public CombatBehaviour CombatBehaviour;
public NPCSmoking Smoking;  public NPCSprayPainting SprayPainting;  public NPCDrinking Drinking;
public NPCItemHolding ItemHolding;  public NPCDialogue Dialogue;  public NPCSchedule Schedule;
public NPCInventory Inventory;  public NPCCustomer Customer;  public NPCDealer Dealer;
public NPCSupplier Supplier;  public NPCRelationship Relationship;  public NPCMessaging Messaging;

// Messaging + lookup
public void SendTextMessage(string message, Response[]? responses = null,
                            float responseDelay = 1f, bool network = true);
public static NPC? Get(string npcId);
public static void PreRegisterPrefabForType(System.Type npcType);
public static void PreRegisterAllNpcPrefabs();
```
All **[V]** from source.

### A real, complete customer NPC — Special Customers in miniature

Verbatim from the downloaded `S1APINPCExample/NPCs/ExamplePhysicalNPC1.cs` **[V]** (⚠️ that repo has **no LICENSE** —
read for technique, do not paste):

```csharp
public sealed class ExamplePhysicalNPC1 : NPC
{
    public override bool IsPhysical => true;

    protected override void ConfigurePrefab(NPCPrefabBuilder builder)
    {
        var parkingGarage    = ParkingLotRegistry.Get<ParkingGarage>();
        var northApartments  = Building.Get<NorthApartments>();
        Vector3 posA     = new Vector3(-28.060f, 1.065f, 62.070f);
        Vector3 spawnPos = new Vector3(-53.5701f, 1.065f, 67.7955f);

        builder.WithIdentity("example_physical_npc1", "Alex", "Test1")
            .WithAppearanceDefaults(av =>
            {
                av.Gender = 0.0f; av.Height = 1.0f; av.Weight = 0.36f;
                av.SkinColor = new Color32(150, 120, 95, 255);
                av.LeftEyeLidColor = av.SkinColor; av.RightEyeLidColor = av.SkinColor;
                av.HairColor = new Color(0.1f, 0.1f, 0.1f);
                av.HairPath  = HairStyle.Spiky;
                av.WithFaceLayer<Face>(Face.Agitated, Color.black);
                av.WithBodyLayer<Shirts>(Shirts.TShirt, Color.red);
                av.WithBodyLayer<Pants>(Pants.Jeans, new Color(0.15f, 0.2f, 0.3f));
                av.WithAccessoryLayer<Feet>(Feet.Sneakers, Color.red);
                av.WithImpostor("Kyle");                       // ← see the impostor warning below
            })
            .WithSpawnPosition(spawnPos)
            .EnsureSmokeBreak(debugMode: true)
            .EnsureCustomer()
            .WithCustomerDefaults(cd =>
            {
                cd.WithSpending(minWeekly: 400f, maxWeekly: 1000f)
                  .WithOrdersPerWeek(1, 4)
                  .WithPreferredOrderDay(Day.Sunday)
                  .WithOrderTime(900)
                  .WithStandards(CustomerStandard.VeryLow)
                  .AllowDirectApproach(true)
                  .GuaranteeFirstSample(true)
                  .WithMutualRelationRequirement(minAt50: 2.5f, maxAt100: 4.0f)
                  .WithCallPoliceChance(0.15f)
                  .WithDependence(baseAddiction: 0.1f, dependenceMultiplier: 1.1f)
                  .WithAffinities(new[] { (DrugType.Marijuana, 0.45f), (DrugType.Cocaine, -0.2f) })
                  .WithPreferredProperties(Property.Munchies, Property.Energizing, Property.Cyclopean);
            })
            .WithRelationshipDefaults(r =>
            {
                r.WithDelta(1.5f)
                 .SetUnlocked(false)
                 .SetUnlockType(NPCRelationship.UnlockType.DirectApproach)
                 .WithConnections<KyleCooley, LudwigMeyer, AustinSteiner>();
            })
            .WithSchedule(plan =>
            {
                plan.EnsureDealSignal()
                    .UseVendingMachine(900)
                    .WalkTo(posA, 925, faceDestinationDir: true)
                    .StayInBuilding(northApartments, 1100)
                    .LocationBased(spawnPos, 1200, 60).Within(1.5f).OnArriveSmokeBreak()
                    .UseVendingMachine(1400)
                    .StayInBuilding(northApartments, 1425, 60)
                    .DriveToCarParkWithCreateVehicle(parkingGarage.GameObjectName, "shitbox", 1550,
                        new Vector3(-66.189f, -3.025f, 124.795f), Quaternion.Euler(0f, 90f, 0f),
                        ParkingAlignment.FrontToKerb);
            })
            .WithInventoryDefaults(inv =>
            {
                inv.WithStartupItems("banana", "baseballbat", "cuke")
                   .WithRandomCash(min: 50, max: 500)
                   .WithClearInventoryEachNight(false);
            });
    }

    protected override void OnCreated()
    {
        try
        {
            base.OnCreated();
            Appearance.Build();                                 // mandatory — generates the mugshot
            SendTextMessage("Hello from physical NPC 1!");
            /* dialogue — see §4.5 */
            Aggressiveness = 3f;
            Region = Region.Northtown;
            Schedule.Enable();
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"ExamplePhysicalNPC OnCreated failed: {ex.Message}");
            MelonLogger.Error($"StackTrace: {ex.StackTrace}");
        }
    }
}
```

Read that `WithCustomerDefaults` block again: **`WithSpending(min,max)` + `WithOrdersPerWeek(min,max)` +
`WithPreferredOrderDay` + `WithStandards` + `WithAffinities` is literally the Special Customers spec** — periodic,
group-flavoured, bulk-buying customers — with zero game patching **[I]**, and it is exactly how Personnel exposes
the same knobs to its JSON packs **[V]**.

A non-physical contact NPC is three lines by comparison **[V]**:

```csharp
public sealed class ContactNpc : NPC
{
    public override bool IsPhysical => false;
    protected override void ConfigurePrefab(NPCPrefabBuilder builder)
        => builder.WithIdentity("contact_npc", "Unknown", "Contact").WithIcon(null);
}
```
No `WithSpawnPosition`, no `WithSchedule`, no `Schedule.Enable()`.

### Standard `OnCreated` order, and the three things that go wrong

```csharp
protected override void OnCreated()
{
    base.OnCreated();
    Appearance.Build();      // required — skip it and the appearance is incomplete + no mugshot [V]
    Schedule.Enable();
}
```

1. **Distant custom NPCs render a blank billboard** unless you set an impostor. Personnel, verbatim: *"Vanilla enables
   the >50m billboard impostor unconditionally, and runtime-built AvatarSettings carry no impostor texture — without
   one, distant custom NPCs render an empty billboard."* **[V]** Use `av.WithImpostor("Kyle")` or
   `WithRandomImpostor(...)`.
2. **`Schedule.InitializeActions()` is in the quick-start doc but could not be found on `NPCSchedule` in the source.**
   Verified members are: `IsEnabled`, `CurfewModeEnabled`, `Enable()`, `Disable()`, `EnforceState()`,
   `SetCurfewMode(bool)`, `GetActiveActionName()`, `EnsureDealSignal()`, `ClearActions(...)`, `GetActionNames()` **[V]**.
   Treat `InitializeActions()` as **[U]**; `Schedule.Enable()` alone is verified to work.
3. **A role-less NPC left "mutually known but locked" hits a vanilla NRE.** Personnel routes around it by unlocking the
   relationship so `ContactsDetailPanel` renders the real name instead of `"???"` **[V]**.
4. `SitAtSeatSet(...)` / `SitSpec` need `durationMinutes > 0` or the action never triggers **[V]**.

### Appearance and clothing — API level only

The full art pipeline is in `CUSTOM-CHARACTERS.md`. The API-level facts you need here, all **[V]** from S1API's own
AvatarFramework-derived reference:

- Asset paths go through `Resources.Load(...)` — **resource-style, no extension**:
  `Avatar/Hair/Spiky/Spiky`, `Avatar/Layers/Face/Face_Agitated`, `Avatar/Layers/Top/T-Shirt`,
  `Avatar/Layers/Bottom/Jeans`, `Avatar/Accessories/Feet/Sneakers/Sneakers`.
- Layer limits: **face 6, body 6, accessory 9**.
- Value ranges: `Gender` 0–1 (`IsMale()` is `< 0.5f`), `Weight` 0–1 (applied as `Weight * 100f` blendshape),
  `Height` practical 0.8–1.2 (applied straight to `localScale`, **no clamp**), `PupilDilation` 0–1, eyelids 0–1,
  `EyebrowRestingHeight` clamped −1.1 → 1.5.
- Defaults: `Height 0.98`, `Gender 0.0`, `Weight 0.4`, `PupilDilation 1.0`, eyelids 0.5.
- `SkinColor` is conventionally `Color32` (0–255); `HairColor`/`EyeBallTint` are `Color` (0–1).
- Typed constant classes exist for layers (`HairStyle`, `Face`, `Eyes`, `Shirts`, `Pants`, `Feet`, …) — **use them
  instead of string paths**; the docs' example paths have known casing errors **[C]**.

### Dialogue and responses — the real pattern

Verbatim from `ExamplePhysicalNPC1.OnCreated` **[V]**:

```csharp
Dialogue.BuildAndSetDatabase(db => {
    db.WithModuleEntry("Reactions", "GREETING", "Welcome.");
});

Dialogue.BuildAndRegisterContainer("AlexShop", c => {
    c.AddNode("ENTRY", "Want some info for $100?", ch => {
        ch.Add("PAY_FOR_INFO", "Pay $100", "INFO_NODE")
          .Add("NO_THANKS", "No thanks", "EXIT");
    });
    c.AddNode("INFO_NODE",  "Get scammed nerd.",            ch => ch.Add("BYE", "Thanks", "EXIT"));
    c.AddNode("NOT_ENOUGH", "You don't have enough cash.",  ch => ch.Add("BACK", "I'll come back.", "ENTRY"));
    c.AddNode("EXIT", "See you.");
});

Dialogue.OnChoiceSelected("PAY_FOR_INFO", () =>
{
    const float price = 100f;
    if (Money.GetCashBalance() >= price)
    {
        Money.ChangeCashBalance(-price, visualizeChange: true, playCashSound: true);
        Dialogue.JumpTo("AlexShop", "INFO_NODE");
    }
    else Dialogue.JumpTo("AlexShop", "NOT_ENOUGH");
});

Dialogue.OnNodeDisplayed("INFO_NODE", () => { /* fires when the node is shown */ });

Dialogue.OnChoiceSelected("BYE", () => { Dialogue.StopOverride(); SendTextMessage("You got scammed"); });

Dialogue.UseContainerOnInteract("AlexShop");
```

**Hard rule [V]:** `Dialogue.StopOverride()` is safe from `OnChoiceSelected(...)`. **Do not call it from
`OnNodeDisplayed(...)`** — you risk recursion and a stack overflow.

Also available: `ClearConversationCategories()` for contacts that shouldn't show Customer/Supplier/Dealer badges **[V]**,
and `S1API.Dialogues.DialogueInjector` for injecting into *existing* NPCs' dialogue **[V]** (5 types in that namespace).

### Text messages and save-safe callbacks

```csharp
protected override void OnCreated()
{
    base.OnCreated();
    Messaging.OnConversationOpened += HandleConversationOpened;
    Messaging.SendTextMessage("Open this conversation to continue.");
}
protected override void OnDestroyed()
{
    Messaging.OnConversationOpened -= HandleConversationOpened;
    base.OnDestroyed();
}
```
**[V]**. State: `Messaging.IsRead`, `.HasUnreadMessages`, `.IsOpen`, `.OnConversationOpened`.
`SendTextMessage(msg, Response[]?, responseDelay, network)` attaches player reply options; **reattach their callbacks
in `OnResponseLoaded(Response)` after a load** or saved responses go dead **[V]**.

### Customer and dealer runtime wiring

`NPCCustomer` **[V]**: `IsCustomer`, `EnsureCustomer()`, `Unlock()`, `ForceDealOffer()`, `OfferContract(ContractInfo)`,
`RequestProduct(Player? = null)`, `SetAwaitingDelivery(bool)`, `SetupDialog()`, `RecommendDealer(NPCDealer)`,
`event OnUnlocked`, `event OnDealCompleted`, `event Action<float,int,int,int> OnContractAssigned`.

`NPCRelationship` **[V]**: `Delta`, `Normalized`, `Add(float, bool network = true)`, `IsUnlocked`, `Type`,
`Unlock(UnlockType = DirectApproach, bool notify = true)`, `SetUnlockType`, `UnlockConnections()`, `IsKnown`,
`IsMutuallyKnown`, `ConnectionIDs`, `event Action<float> OnChanged`, `event Action<UnlockType,bool> OnUnlocked`.

The dealer example's **event-cleanup discipline is the pattern to copy verbatim** **[V]** — cache the delegate in a
field, `-=` before `+=`, unsubscribe in `OnDestroyed`:

```csharp
private Action _dealerRecruitedHandler;

private void WireDealerEvents()
{
    if (Dealer == null) { MelonLogger.Warning($"Dealer component missing for {ID}"); return; }
    _dealerRecruitedHandler ??= HandleDealerRecruited;
    Dealer.OnRecruited -= _dealerRecruitedHandler;
    Dealer.OnRecruited += _dealerRecruitedHandler;
}
protected override void OnDestroyed() { base.OnDestroyed(); UnwireDealerEvents(); }
```

Dealer defaults (from `ExamplePhysicalDealerNPC`) **[V]**: `WithSigningFee(1000f)`, `WithCut(0.15f)`,
`WithDealerType(DealerType.PlayerDealer)`, `WithHome(building)` / `WithHomeName("North Apartments")`,
`AllowInsufficientQuality(false)`, `AllowExcessQuality(true)`, `WithCompletedDealsVariable("…")`, and
`WithRecommendation(r => r.FromCustomer<ExamplePhysicalNPC2>().OnDealCompleted())`. Dealers need
`plan.EnsureDealSignal()` in the schedule or contract handling won't work **[V]**.

**Named base-game NPCs** get typed identifiers: ~76 across `S1API.Entities.NPCs.{Northtown ×16, Westville ×12,
Docks ×11, Downtown ×11, Suburbia ×11, Uptown ×10, PoliceOfficers ×9}` plus 6 unclassified **[V]**. Use
`WithConnections<KyleCooley, LudwigMeyer, AustinSteiner>()` rather than string ids.

**Voices** **[V]**: `builder.WithVoice(NPCVoiceCatalog.Tyler, pitch: 0.92f)`; ids `cold`, `crackhead`, `female-1`,
`female-2`, `goblin`, `hippie`, `joel`, `monotone`, `redneck`, `timid`, `tyler`; pitch 0.1–4.0. *(`hippie` and
`redneck` are on the nose for two of the three Special Customer groups.)*

**Map markers.** Custom NPCs don't get a phone-map POI automatically. Personnel adds one **[V]**:

```csharp
var mgr = S1NPCs.NPCManager.Instance;                       // guard InstanceExists first
var poi = UnityEngine.Object.Instantiate(mgr.NPCPoIPrefab, npcRoot.transform);
poi.SetNPC(npcRoot.GetComponent<S1NPCs.NPC>());
poi.enabled = true;
```

## 4.4 Law — the best-supported of our three mods

`S1API.Law.LawManager`, complete public surface **[V]**:

```csharp
public static int   DispatchOfficerCount        => 2;
public static float DispatchVehicleUseThreshold => 25f;
public static float SearchTimeInvestigating     => 60f;
public static float SearchTimeArresting         => 25f;
public static float SearchTimeNonLethal         => 30f;
public static float SearchTimeLethal            => 40f;
public static float EscalationTimeArresting     => 25f;
public static float EscalationTimeNonLethal     => 120f;

public static void CallPolice(Player target);
public static void SetWantedLevel(Player target, PursuitLevel level);
public static void ClearWantedLevel(Player target);
public static PursuitLevel GetWantedLevel(Player target);
public static void EscalateWantedLevel(Player target);
public static void DeescalateWantedLevel(Player target);
public static int  ActiveOfficerCount { get; }
public static bool IsPlayerWanted(Player target);
public static bool IsUnderInvestigation(Player target);
public static bool IsLethalForceAuthorized(Player target);

public static PatrolGroup? StartFootPatrol(FootPatrolRoute route, int requestedMembers = 2);
public static bool StartVehiclePatrol(VehiclePatrolRoute route);
public static FootPatrolRoute?    FindFootPatrolRoute(string routeName);
public static VehiclePatrolRoute? FindVehiclePatrolRoute(string routeName);
public static FootPatrolRoute[]    GetAllFootPatrolRoutes();
public static VehiclePatrolRoute[] GetAllVehiclePatrolRoutes();
```

Escalation ladder: `None → Investigating → Arresting → NonLethal → Lethal` **[V]**. Plus `CheckpointManager`,
`CheckpointInfo`, `CurfewManager`, `LawController`, `PlayerCrimeData`, `PatrolGroup`, `FootPatrolRoute`,
`VehiclePatrolRoute`, `PursuitLevel` — 11 types **[V]** — and `ConsoleHelper.SetLawIntensity(float)` (0–10).

For anything beyond that (federal agents, outlaw status, custom penalties) drop to `Il2CppScheduleOne.Law` /
`.Police`, whose surface is catalogued in §2.8 and §4.9.

## 4.5 Save data vs settings — two different systems

`Saveable` + `[SaveableField("name")]` **[V]**:

- Reflects **all** instance fields, public and non-public, including inherited.
- File name = `SaveName + ".json"`; serialised with `JsonConvert.SerializeObject(value, Indented, ISaveable.SerializerSettings)`.
- **A `null` field deletes its save file** (`File.Delete`).
- Non-null fields are added to `extraSaveables` so the game's cleanup doesn't delete them.
- `OnSaved()` / `OnLoaded()` fire after the field loop.
- `public virtual SaveableLoadOrder LoadOrder => SaveableLoadOrder.AfterBaseGame;` — override to `BeforeBaseGame` for
  config that must apply before entities load. *All* saveables save at the same time regardless of load order.
- `Saveable.RequestGameSave()` → `bool`, static, swallows failures.
- Files land under the active save slot (`…\AppData\LocalLow\TVGS\Schedule I\Saves\<steam_id>\…`), so per-slot
  namespacing is free **[V]** for the mechanism, **[I]** for the exact folder wiring.

**Split for our mods [I]:** which drivers are hired, which routes exist, which customer groups have already visited,
current outlaw status → `Saveable`. Global toggles, hotkeys, difficulty multipliers → `MelonPreferences` (§7).

## 4.6 Console

Your own commands: subclass `BaseConsoleCommand` (`CommandWord`, `CommandDescription`, `ExampleUsage`,
`ExecuteCommand(List<string>)`) **[V]**. It deliberately does not inherit the game's IL2CPP `ConsoleCommand`:
*"Avoids inheriting from Il2Cpp abstract types; safe for both Mono and Il2Cpp."* **[V]**. Lookup is case-insensitive and
`args` arrives with the command word already stripped.

⚠️ `CustomConsoleRegistry.Register` is `internal` and **no public registration entry point was located** — discovery is
*presumed* to be by auto-discovery like the rest of S1API. **[U]** — check `ConsolePatches.cs` before relying on it.

Built-ins via `ConsoleHelper` (flat statics, all **[V]**): `Submit(string)`, `RunCashCommand(int)`,
`RunOnlineBalanceCommand(int)`, `AddItemToInventory(code, qty?)`, `ClearInventory()`, `ClearTrash()`, `ClearWanted()`,
`GiveXp(int)`, `GrowPlants()`, `LowerWanted()`, `RaiseWanted()`, `SaveGame()`, `DiscoverProduct(code)`,
`SetPlayerHealth(float)`, `SetPlayerJumpMultiplier(float)`, **`SetLawIntensity(float 0–10)`**,
`SetPlayerMoveSpeedMultiplier(float)`, `SetQuality(Quality)`, `SetQuestState(name, state)`,
`SetNpcRelationship(id|NPC, 0–5)`, `UnlockNpc(NPC)`, `SetTime("1530")`, `SpawnVehicle(code)`.
These construct the game's own command objects and call `.Execute(args)` — they don't go through the text parser **[V]**.

## 4.7 Phone apps — the free settings surface

`public abstract class PhoneApp : Registerable` **[V]**. Implement `AppName`, `AppTitle`, `IconLabel`, `IconFileName`,
`OnCreatedUI(GameObject container)`; optionally override `IconSprite`, `Orientation`, `Exit(ExitAction)`,
`OnPhoneClosed()`. Public: `IsOpen()`, `OpenApp()`, `CloseApp()`, `SetIconSprite(Sprite)`, `SetIconTexture(Texture2D)`.
Auto-discovered when the phone `HomeScreen` starts — **your type must be `public`**, and *"Do not manually register"* **[V]**.

Why it's robust: S1API clones the native `HomeScreen.appIconPrefab`, adds the `Button` to `HomeScreen.appIcons` and the
`UISelectable` to `HomeScreen.uiPanel.AddSelectable(...)` — **which is what preserves keyboard/gamepad navigation**,
newly relevant post-v0.4.6 **[V]**. Icon PNG is loaded from `MelonEnvironment.ModsDirectory`.

## 4.8 Quests, money, time, player — one-liners

- `Quest : Saveable` **[V]**: `Title`, `Description`, `AutoBegin`, `QuestIcon`, `AddEntry(title, Vector3?)` /
  `AddEntry(title, NPC)` (POI follows the NPC), `OnComplete`/`OnFail`, `Begin/Cancel/Expire/Fail/Complete/End`.
  `QuestManager.CreateQuest<T>(guid?)` uses `Activator.CreateInstance` → needs a public parameterless ctor.
  24 typed identifiers exist for base-game quests, including `Warehouse`.
- `TimeManager` **[V]**: `OnHourPass`, `OnDayPass`, `OnWeekPass`, `OnSleepStart`, `OnSleepEnd(int)`, `OnTick`;
  `CurrentDay`, `ElapsedDays`, `CurrentTime`, `IsNight`, `IsEndOfDay`, `NormalizedTime`, `Playtime`;
  `SetTime(int)`, `IsCurrentTimeWithinRange(int,int)`, `GetMinutesFrom24HourTime(int)`.
- `Money` **[V]**: `OnBalanceChanged`, `ChangeCashBalance(float, bool visualize, bool sound)`,
  `CreateOnlineTransaction(...)`, `GetNetWorth()`, `GetCashBalance()`, `GetOnlineBalance()`,
  `AddNetworthCalculation(Action<object>)`.
- `Player` **[V]**: `static Local`, `static All`, spawn/despawn events, `Position`, **`PlayerCrimeData CrimeData`**,
  health, `IsArrested`/`IsTased`/`IsUnconscious`, `LastDrivenVehicle`, `CurrentProperty`, `CurrentRegion`, clothing.

## 4.9 Where you must leave S1API — the raw game surface

Verified against v0.4.6f11's stripped decompile **[V]**:

```csharp
namespace ScheduleOne.Employees;
public enum EEmployeeType { Botanist, Handler, Chemist, Cleaner }   // FOUR types, none is a driver
```
*(Note the enum value is `Handler` while the class file is `Packager.cs` — string matching will bite you.)*

```csharp
public class Employee : NPC
{
    [SerializeField] protected EEmployeeType Type;
    public FloatStack WorkSpeedController;
    [Header("Payment")] public float SigningFee;
    [Header("Payment")] public float DailyWage;
    public MoveItemBehaviour MoveItemBehaviour;

    public ScheduleOne.Property.Property AssignedProperty { get; protected set; }
    public int  EmployeeIndex { get; protected set; }
    public bool PaidForToday  { get; private set; }        // FishNet SyncVar
    public bool Fired         { get; private set; }
    public bool IsWaitingOutside => WaitOutside.Active;
    public EEmployeeType EmployeeType => Type;

    [ObserversRpc(RunLocally = true)][TargetRpc]
    public virtual void Initialize(NetworkConnection conn, string firstName, string lastName,
                                   string id, string guid, string propertyID, bool male, int appearanceIndex);
    protected virtual void AssignProperty(Property prop, bool warp);
    protected virtual void UnassignProperty();
    [ServerRpc(RequireOwnership = false)] public void SendTransfer(string propertyCode);
    [ServerRpc(RequireOwnership = false)] public void SendFire();
    public void SetIsPaid();  public bool IsPayAvailable();  public void RemoveDailyWage();
    public virtual EmployeeHome GetHome();
    protected void SetDestination(ITransitEntity transitEntity, bool teleportIfFail = true);
    protected void SetDestination(Vector3 position, bool teleportIfFail = true);
}
```

**`ScheduleOne.Management` already contains the routing infrastructure** — `ITransitEntity`, `TransitRoute`,
`AdvancedTransitRoute`, `RouteListField`, `ObjectField`, `NPCField`, `ItemField`, `ManagementItemFilter`,
`EntityConfiguration`, `IConfigurable`, `ConfigurationReplicator`, per-employee `BotanistConfiguration` /
`ChemistConfiguration` / `CleanerConfiguration` / `PackagerConfiguration`, and `Management/UI/ConfigPanel` **[V]**.
**Modelling a driver as an `EntityConfiguration` with a `RouteListField` is the way to make it feel native and reuse
the existing management UI** **[I]** — and it matches the dev's own "designate automatic transit routes" wording.

**Singletons.** Three base classes in `ScheduleOne.DevUtilities` — `Singleton<T>`, `NetworkSingleton<T>`,
`PlayerSingleton<T>` (plus `PersistentSingleton<T>`), all exposing `static bool InstanceExists` and `static T Instance`
**[V]**. ⚠️ **`NetworkSingleton<T>` derives from FishNet's `NetworkBehaviour`** — those managers are network objects and
touching them before the session is up fails **[V]** for the inheritance, **[I]** for the consequence.

**Two types the brief assumed exist, but don't [V]:**
- **`CustomerManager` does not exist.** Customers are `ScheduleOne.Economy.Customer` components on NPCs; enumerate via
  `NPCManager.NPCRegistry`.
- **`PoliceManager` does not exist.** Police live in `ScheduleOne.Police` (`PoliceOfficer`, `Investigation`, `Offense`,
  `RoadCheckpoint`, `NPCResponses_Police`) and are orchestrated by `ScheduleOne.Law.LawManager` (a plain
  `Singleton<LawManager>`). `PoliceOfficer.Officers` is a static list.

**Law/police fields confirmed in the wild** (NACops) **[V]**: `PoliceOfficer.{AutoDeactivate, ChatterEnabled,
BodySearchChance, BodySearchDuration, GunPrefab, TaserPrefab, PursuitBehaviour, Awareness.VisionCone, Movement,
Behaviour, Health, DialogueHandler, NPCData}`; `PoliceStation.PoliceStations[0].{SpawnPoint, Doors}`;
`PursuitBehaviour.{TargetPlayer, arrestingEnabled, timeWithinArrestRange, IsTargetRecentlyVisible, currentWeapon,
SucessfulHit /* sic */}`; `VisionCone.{WorldspaceIconsEnabled, RangeMultiplier, stateSettings[...].NoticeTimeMultiplier}`;
**`Singleton<LawController>.Instance.{MondaySettings … SundaySettings}` of type `LawActivitySettings { Curfews,
Checkpoints, Patrols, VehiclePatrols, Sentries }` — replaceable wholesale, which is exactly how NACops adds patrols and
sentries**; `Player.Local.CrimeData.{AddCrime(Crime), SetPursuitLevel(EPursuitLevel), CurrentPursuitLevel,
CurrentArrestProgress, SetArrestProgress(f)}`; 14+ concrete `Crime` subclasses; `PenaltyHandler` fine constants
(`ASSAULT_FINE`, `ATTEMPT_TO_SELL_FINE`, …) + `ProcessCrimeList`. Also
`officer.Movement.Agent.areaMask = 57; // identical to employee` — **the NavMesh mask that lets an officer enter
buildings**.

**Economy types for Special Customers** **[V]**: `ScheduleOne.Economy.{Customer, CustomerData, CustomerAffinityData,
CustomerSatisfaction, ECustomerStandard, ProductTypeAffinity, StandardsMethod, OverrideCustomerDealLocation,
DealWindowInfo, EDealWindow, Dealer, EDealerType, Supplier, SupplierStash, DeadDrop, DeliveryLocation}`.

---

# 5. Existing mods worth studying, ranked

Full 60-row inventory is in `raw/a2-mods-survey.md` §1.2. This is the shortlist that matters, with the specific thing
to take from each.

## 5.1 Ranked by usefulness to our three mods

| # | Mod | Licence | Last push | Take this |
|---|---|---|---|---|
| **1** | [`DooDesch-Mods/ScheduleOne-Personnel`](https://github.com/DooDesch-Mods/ScheduleOne-Personnel) | **MIT ✅** | 2026-08-02 | The complete, current, *legally copyable* reference for S1API custom NPCs: `ConfigureFromDef` → `NPCPrefabBuilder`, the uninitialized-instance contract, deterministic type ordering for co-op, impostor fallback via reflection, `AddMapMarker`, distortion applied post-`OnCreated`, and the `MelonPreferences` category convention. Serves **all three** features. |
| **2** | [`XOWithSauce/schedule-nacops`](https://github.com/XOWithSauce/schedule-nacops) | **NONE ⚠ read-only** | 2026-08-03 | The police bible: exact patch targets (§2.8), the runtime networked-officer clone (§3.3 Pattern A), `LawActivitySettings` wholesale replacement for patrols/sentries/checkpoints, weapon swap via `Resources.Load("Avatar/Equippables/…")`, and disciplined `ExitPreTask()` teardown. **Directly serves Police Improvements.** Its MIT sibling [`schedule-cartelenforcer`](https://github.com/XOWithSauce/schedule-cartelenforcer) uses the *same* init helper — **read that one when you need to copy code.** |
| **3** | [`DooDesch-Mods/ScheduleOne-SideHustle`](https://github.com/DooDesch-Mods/ScheduleOne-SideHustle) | **MIT ✅** | 2026-08-03 | Main-menu injection with **no Harmony and no AssetBundle**: find `MenuScreen` with `OpenOnStart`, wait 20 frames, clone a label-matched nav button (Settings has *"the most side-effect-free click"*), `RemoveAllListeners()` **plus** `SetPersistentListenerState(i, Off)`, relabel via `TextMeshProUGUI`, adopt your own button if it already exists. Also `SettingDescriptor { Slider, Toggle, Segmented, Text, Dropdown }` as a shape for our screen. |
| 4 | [`k073l/s1-modsapp`](https://github.com/k073l/s1-modsapp) | **MIT ✅** | 2026-08-02 | Its `PREFERENCES.md` is the authoritative MelonPreferences hot-reload guide (§7). Integrate *passively* — cost is zero. |
| 5 | [`hdlmrell/OTC-S1-Mod`](https://github.com/hdlmrell/OTC-S1-Mod) | **NOASSERTION ⚠** | 2026-04-14 | The `NetworkHelper.IsHost` two-liner used in ~120 places; the biggest catalogue of `Customer`/`Contract`/phone patch targets; `SafeTypeLoadPatch` prefixing `AccessTools.GetTypesFromAssembly` to survive broken assemblies in the load set. |
| 6 | [`k073l/s1-employeetweaks`](https://github.com/k073l/s1-employeetweaks) · [`k073l/BusinessEmployment`](https://github.com/k073l/BusinessEmployment) | **MIT ✅** | 2026-08-02 / 08-01 | Closest MIT prior art for **Hireable Drivers**: BusinessEmployment proves mod-side employee creation works. Metadata verified; **source not yet read — this is the highest-value remaining read.** |
| 7 | [`bwyan/HireableDeliveryDriver_IL2CPP_port`](https://thunderstore.io/c/schedule-i/p/bwyan/HireableDeliveryDriver_IL2CPP_port/) | n/a — **no source** | 2026-01-05 | Feature-shape precedent only: hire drivers, routes between loading docks, vans spawn/load/travel/unload, *"Route management is host-only. Host controls hiring, wages, and route changes. Clients can still see and use the vans once spawned."* That MP posture is the one to match. |
| 8 | [`DooDesch-Mods/ScheduleOne-PropHunt`](https://github.com/DooDesch-Mods/ScheduleOne-PropHunt) | **MIT ✅** | 2026-08-03 | The patch-late-not-at-load crash lesson; best MP-gamemode source; embedded-resource icon convention `"<RootNamespace>.<folders>.<file>"`. |
| 9 | [`DooDesch-Mods/ScheduleOne-LooseEnds`](https://github.com/DooDesch-Mods/ScheduleOne-LooseEnds) | **MIT ✅** | 2026-08-02 | Smallest clean MIT police-reaction source (`NPCHealth.*`, `Draggable.*`). Good starter read before NACops. |
| 10 | [`RoachxD/ScheduleOne.HonestMainMenu`](https://github.com/RoachxD/ScheduleOne.HonestMainMenu) | **MIT ✅** | 2026-01-10 | Where the hard-coded menu names come from (`MainMenu`, `Home/Bank`, `Title` child, `MainMenuPopup.Instance.Open`). Also: `MissingMethodException`-catch-and-retry-older-overload. Slightly stale (Jan 2026). |
| 11 | [`DevKaiE/DealerTransportMod`](https://github.com/DevKaiE/DealerTransportMod) | **NONE ⚠** | 2025-04-23 | Smallest NPC-drives-logistics precedent. Stale, patterns only. |
| 12 | [`DooDesch-Mods/ScheduleOne-Cookbook`](https://github.com/DooDesch-Mods/ScheduleOne-Cookbook) | NOASSERTION | 2026-08-01 | Not code — the curated FishNet/IL2CPP/AssetBundle/preferences knowledge base, with a Discord permalink on every claim. **The highest-value single document in the ecosystem.** |
| 13 | [`DooDesch-Mods/ScheduleOne-Snitch`](https://github.com/DooDesch-Mods/ScheduleOne-Snitch) · [`Siesta`](https://github.com/DooDesch/ScheduleOne-Siesta) | **MIT ✅** | 2026-08-02 | Snitch profiles NPCs/trash/quests *and your own mods*; Siesta is NPC distance LOD. Both are the mitigation if dozens of custom NPCs cost FPS. |

**Nothing exists for Special Customers.** Across 494 Thunderstore packages and the GitHub topic searches, no mod
implements customer groups visiting town **[V]**. Closest analogues: `GreenCarrot/MoreDeals` (*"customers will request
from you multiple times per day"*), `hdlmrell/OverTheCounter`, `EndureBlackout/BetterFiends`. **Greenfield —
and the feature TVGS is actively shipping.**

Also **no mod adds federal agents** — the nearest is NACops' disguised private investigator and raid officers **[V]**.

## 5.2 Licence rules for this project

| Verdict | Repos |
|---|---|
| ✅ **Copy with attribution** | `ifBars/S1API`, all `DooDesch-Mods/*` (Personnel, SideHustle, Hotline, PropHunt, LooseEnds, Snitch, Sideload, FullHouse, Personify, Inkubator), `XOWithSauce/schedule-cartelenforcer`, `k073l/*` (modsapp, employeetweaks, BusinessEmployment, multidelivery, S1MelonModTemplate, RefGen), `RoachxD/ScheduleOne.HonestMainMenu`, `manzune/AdvancedDealing`, `s1modding/s1modding.github.io`, `ifBars/SteamNetworkLib`, `ifBars/bGUI` — all **MIT** **[V]** |
| ⚠️ **Read only, never paste** | `XOWithSauce/schedule-nacops`, `hdlmrell/OTC-S1-Mod` (NOASSERTION), `ifBars/S1APITemplate`, `ifBars/S1APINPCExample`, `k073l/s1-codearchiver`, `k073l/s1-dynamicnpcs`, `Skippeh/ScheduleOne_UnityProject`, `DevKaiE/DealerTransportMod`, `surrealnirvana/LawEnforcementEnhancementMod`, `archenovalis/NoLazyWorkers`, `HazDS/S1Mods`, `KaBooMa/S1API` — **no LICENSE file = all rights reserved** **[V]** |
| ⚠️ **Licence-encumbered** | `ifBars/S1MAPI` and `ScheduleLua` are **GPL-3.0** — linking makes our mods GPL **[V]** |
| ⚠️ Contradiction to resolve | `ifBars/BigWillyMod` is **MIT per the GitHub API** but its Thunderstore page text says GPL v3. Reconcile before copying **[V]** |

## 5.3 One ecosystem fact that affects publishing

`MLVScan` (ifBars) is a MelonLoader plugin with 20K downloads and rating 9 that *"detect[s] and disable[s] potentially
malicious mods"*, and ModsApp ships an MLVScan attestation badge with a CI workflow **[V]**. **[I]** If our mods do
anything resembling file or network I/O (JSON config writes, Steam lobby data), expect to need an attestation or some
users' installs will auto-disable us. Also relevant: `OTCLoader` (18K dl) *"auto-detects your game branch and disables
incompatible mod DLLs before they crash"* — one more reason `MelonPlatformDomain` must be correct.

---

# 6. Multiplayer / FishNet

## 6.1 The constraint that settles the design

**Schedule I runs FishNet v3, and on IL2CPP FishNet's code generator never runs against a mod assembly, so custom
RPCs are effectively unavailable.** **[C]** — Cookbook, sourced to Discord messages from Skippy and j0ckinjz. Two
corroborating details from the same source:

- Internal `NetworkBehaviour` dictionaries come up as **garbage rather than empty** under IL2CPP; a derived behaviour
  must hand-initialise `_rpcLinks`, `_syncVars`, `_syncObjects`, `_bufferedRpcs`, `_serverRpcDelegates`,
  `_observersRpcDelegates`, `_targetRpcDelegates`, `_reconcileRpcDelegates` in `OnStartNetwork()`.
- *"if the server spawns a `NetworkObject` before a client has finished connecting, that client can hang on an infinite
  load. A crude mitigation was to delay the spawn 10-15 seconds."*

Even on Mono, FishNet registers generated serializers via `RuntimeInitializeOnLoadMethod`, **which does not fire for
mod-loader-loaded assemblies** — you'd have to reflect over `FishNet.Serializing.Generated` and call `InitializeOnce`
yourself **[C]**.

The Cookbook's own summary of the hand-rolled route: *"reported to work in real multiplayer but not consistently
(serializers sometimes never fire on load, and some paths needed Harmony patches to stabilize). If you just need to
move small amounts of data, the Steam lobby route is far less painful."* **[C]**

## 6.2 The rule of thumb for our three mods

**Copy OTC's two-liner and gate every state mutation behind it** **[V]**:

```csharp
#if IL2CPP
using Il2CppFishNet;
#else
using FishNet;
#endif

/// Multiplayer authority helper. All shared-state mutations must be gated behind IsHost so only the
/// server/host executes them. Returns true in single-player (no NetworkManager present).
public static class NetworkHelper
{
    public static bool IsHost => InstanceFinder.NetworkManager == null || InstanceFinder.IsServer;
}
```

It returns `true` in singleplayer, so **the same code path works solo with zero special-casing** — that's the whole
trick.

Seven concrete rules, all **[I]** but well-grounded:

1. **All three features are naturally host-authoritative.** Driver routes and vans, police intensity and outlaw state,
   special-customer visit scheduling are all world simulation. Run them only on the host and let FishNet replicate the
   *vanilla* objects (NPCs spawned via `ServerManager.Spawn`, vanilla `PoliceOfficer`/`Customer`/`LandVehicle`) — those
   already have working generated RPCs and SyncVars because they were compiled in the Unity editor.
2. **Never author a custom `NetworkBehaviour`.** (See §3.3.)
3. **Require all players to install, and say so on the mod page.** S1API registers spawnable NPC prefabs by simple type
   name in a load-order-sensitive way; a client without our DLLs can't reconstruct wrappers for our NPCs. Personnel
   states the same rule for packs: *"In co-op, everyone needs the same packs installed — the same rule as for mods."*
4. **Keep config sync out of FishNet.** Push a small string through **Steam lobby data** (host-writes / everyone-reads),
   `SteamMatchmaking.SetLobbyData(Singleton<Lobby>.Instance.LobbySteamID, key, value)`, or use **SteamNetworkLib**
   (MIT, 23K dl) instead of raw Steamworks. Limits: ~8 KB per player of member data; only the host may write.
5. **Version-stamp the handshake** — publish `<mod>_ver` in lobby member data so a mismatched client is warned, not
   desynced.
6. **Delay any runtime `ServerManager.Spawn` until clients are connected.** Spawn from `LoadManager.Instance.onLoadComplete`
   plus a `WaitUntil(LoadManager.Instance.IsGameLoaded)`, as NACops does.
7. **Detect co-op cheaply for the UI:** `InstanceFinder.NetworkManager != null && !InstanceFinder.IsServer` ⇒ we are a
   client; grey out management controls and show "Host only", exactly as HireableDeliveryDriver does.

**Ecosystem norm confirmed:** host-only management with client-visible results. Nobody in this ecosystem is doing
bidirectional custom RPCs on IL2CPP **[V]** across the mod pages surveyed. Vanilla lobby cap is 4; six separate mods
exist purely to raise it **[V]**.

**One name to verify locally:** there are two `Lobby` references in the wild — `Il2CppScheduleOne.Networking.Lobby`
(HonestMainMenu, **[V]**) and `Singleton<Lobby>` (Cookbook, **[C]**). Confirm which our interop assemblies expose before
using `.IsHost` / `.LobbySteamID` / `.Players`. Likewise `InstanceFinder.IsHost` / `.IsClient` exist in FishNet's API
but **no Schedule I mod in the survey used them** — treat as **[U]**; every real mod used `IsServer`.

---

# 7. Config and settings-UI conventions

## 7.1 MelonPreferences norms

| Norm | Detail | Src |
|---|---|---|
| Default location | `UserData\MelonPreferences.cfg`; per-category override with `category.SetFilePath("…"); category.SaveToFile();` | **[V]** |
| Save timing | ML auto-saves on quit; `MelonPreferences.Save()` forces it | **[V]** |
| Change notification | `entry.OnEntryValueChanged.Subscribe((old, @new) => …)`, `entry.OnEntryValueChangedUntyped.Subscribe(...)` for a multi-entry tracker, and `public override void OnPreferencesSaved()` on the `MelonMod` | **[V]** ModsApp `PREFERENCES.md` |
| ⚠️ 0.7.1 trap | `MelonPreferences_Entry<T>.OnValueChanged` is **obsolete-as-error** — the build fails. Use the `OnEntryValueChanged` MelonEvent field. | **[V]** — recorded in our own `CONTEXT.md` |
| ⚠️ ≤0.7.2 trap | `OnPreferencesSaved` / `OnPreferencesLoaded` *"sometimes not being triggered"* — fixed in **0.7.3** | **[V]** — verified today from the release notes |
| Description text | Always pass **both** `displayName` and `description` to `CreateEntry(id, default, displayName, description)`; both settings apps render the description as help text | **[V]** |
| Shared `.cfg` across categories | Safe — `SetFilePath` reuses the file handle and `SaveToFile` mutates only its own TOML section | **[V]** per our `CONTEXT.md` |

## 7.2 The two community settings frameworks

| Framework | What it gives you | Verdict |
|---|---|---|
| **ModsApp** (k073l, **MIT**, 16.9K dl) | A "Mods" **phone app** that auto-discovers every loaded mod and renders **all** its `MelonPreferences` entries with type-appropriate controls (bool/int/float/string/enum/Color/KeyCode/Vector3/List/Dictionary), plus arbitrary JSON configs under `UserData`. Fuzzy search, categories, themes, per-mod changelog/readme, dependency tracking, in-app log explorer. **Requires S1API ≥ 3.0.1 — exactly the fork we already ship.** | **Integrate passively.** Zero code. |
| **Mod Manager & Phone App** (Prowiler, Nexus 397, no source) | *"Adds a 'Mod Settings' app to the phone home screen… Modify configurable options using toggles/input fields/keybinds & save."* Named as *the* in-game settings UI by Hotline, Personnel, WarehousePlus and others. | **Integrate passively.** |
| **Hotline** (DooDesch, **MIT**) | One in-game overlay + one master key for every mod's HUD: `Hotline.Api.Hud.RegisterPanel/RegisterAction/RegisterToggle/RegisterSlider/RegisterText/BindPanelLog`. Ships a drop-in single-file shim (`HotlineApi.cs`) that is a **no-op when absent**. Also *"auto-catches mods that grab function keys"* — relevant, since we claim F7. | **Optional**, for a dev/live-tuning panel. Not a settings surface. |
| **SideHustle `SettingDescriptor`** | `SettingType { Slider, Toggle, Segmented, Text, Dropdown }` + `SettingPreset` bundles, shipped to host+clients in a launch blob. | **Ignore as a framework** (it configures a *match*, not a mod) — but **copy its shape** for our screen. |
| BepInEx `ConfigurationManager` analogue | **Does not exist** in this ecosystem. | n/a |

**The naming convention.** Personnel's comment, verbatim **[V]**:

```csharp
/// MelonPreferences wrapper. Category id is prefixed with the mod name so the "Mod Manager & Phone App"
/// settings UI auto-detects it.
private const string CategoryId = "Personnel_01_Main";
```
SideHustle uses `SideHustle_01_Main`; Hotline uses `Hotline_01_Main` **[V]**. So the convention is
**`<ModName>_01_Main`**, created via `MelonPreferences.CreateCategory("<ModName>_01_Main", "<Friendly Name>")`.

## 7.3 Verdict on our current scaffolding — mostly right, two changes

**What holds up [V] from reading the code:**

- `MelonPreferences` (not `Saveable`) for module toggles is **correct**, and `ExpansionConfig`'s own comment states the
  right reason: *"module toggles are driven from the main menu, before any save exists, so they are machine-global."*
  A `Saveable` cannot be read before a save loads.
- One category per module, one shared `UserData\Expansions.cfg` via `SetFilePath(..., autoload: true)` — verified safe.
- `ModuleConfig.Bind` reusing an existing entry (`HasEntry` → `GetEntry` → else `CreateEntry`) so a re-registered
  module keeps the user's value — good, and necessary given enable/disable cycling.
- Every entry gets a `displayName` **and** a `description`. That's exactly what both settings apps want.
- `ConfigValue<T>` caching parsed values (`_verboseCache`, `_menuHotkeyCache`) with a `Changed` subscription instead of
  round-tripping through MelonPreferences every frame — correct and cheap.

**Change 1 — the category naming misses the community convention.** We currently produce:

```
[Expansions]                      ← global
[Expansions.hireable_drivers]
[Expansions.police_overhaul]
[Expansions.special_customers]
```

Two problems:

- **It doesn't match `<ModName>_01_Main`.** Prowiler's app keys off the *mod name* prefix; our prefix is `Expansions`,
  but the three MelonMods are named `"Hireable Drivers"`, `"Police Improvements"` and `"Special Customers"` **[V]**.
  We are three separate DLLs presenting as one config namespace, which is exactly the case the convention doesn't
  handle. **[I]**
- **The dot in the identifier is a real risk.** MelonPreferences serialises categories as TOML tables. An identifier
  containing `.` will be written as `[Expansions.hireable_drivers]`, which TOML defines as a *sub-table* of
  `[Expansions]` — and we also have a literal `[Expansions]` category. Whether Tomlet round-trips that correctly is
  **[U]** — I could not verify it without running the game, and the scaffolding is compile-verified only. **Test this
  before writing feature config**, or sidestep it entirely.

  Recommended identifiers (sidesteps both problems, keeps one shared file):
  ```
  HireableDrivers_01_Main
  PoliceImprovements_01_Main
  SpecialCustomers_01_Main
  Expansions_00_Shared          // the cross-module category
  ```
  This is a rename, so do it **now** — `IExpansionModule`'s own doc comment warns that renaming the id *"orphans the
  user's saved settings"* **[V]**.

**Change 2 — make the toggles genuinely hot-appliable.** ModsApp's docs note *"the app will remind you when a restart
might be necessary… This depends on how individual mods handle preference updates."* **[V]** Our architecture already
supports hot toggling (per-module Harmony + `ModuleLifetime` teardown), so subscribe the `enabled` entry's
`OnEntryValueChanged` to the registry's enable/disable path. Then we work correctly from *three* front ends — our
screen, ModsApp, and Prowiler's app — for near-zero extra code **[I]**.

**Non-change:** keep the shared `Expansions.cfg`. One file for a three-mod suite is better UX than three, and it's
verified safe.

**Main-menu surface — the recommendation stands.** Cloning a real nav button inherits the exact font, sprite, hover
state and layout, requires no Harmony and no AssetBundle, and — critically — **is available before a save loads**,
which is when "is Police Improvements on" must be decided. Comparison table in `raw/a2-mods-survey.md` §3.3;
construction detail in `raw/c2-ugui-runtime.md`. Do **not** ship an AssetBundle for it: four separate mods build
native-looking menu UI with zero bundles, and `AssetBundle.LoadFromMemory` is stripped on this build anyway **[V]**.
Optionally register with SideHustle as an *optional* dependency (~10 lines, no-op when absent) **[I]**.

---

# 8. Update resilience

## 8.1 Defensive techniques, ranked by how much to lean on them

**1. Prefer S1API for anything it covers.** Every S1API release is a compatibility patch you didn't write. The
evidence is its release notes, which read as a changelog of what Schedule I patches break **[V]**:

- **0.4.6f11 moved `ScheduleOne.DevUtilities.ExitAction`** and made the old signature impossible to retain. S1API
  absorbed it by introducing `S1API.PhoneApp.ExitAction`; downstream mods changed **one signature** instead of breaking.
- **v3.1.2 (2026-08-01):** *"custom phone apps now receive an independent icon created from the native phone-app prefab
  instead of renaming and rewiring the built-in Delivery icon"* — 0.4.6f11 changed the home screen enough to break every
  custom app icon.
- **Player energy was removed from the game.** S1API kept the method as a documented no-op rather than deleting or
  throwing.
- **`RequestGameSave(bool immediate)`** kept its signature and now ignores the argument.

**Learn the *shape* of those fixes: keep the signature, degrade the behaviour, log once, never throw.**

**2. Wrap native calls, return a sentinel.** S1API's own `RequestGameSave` **[V]**:

```csharp
try
{
    var loadManager = S1Persistence.LoadManager.Instance;
    if (loadManager == null || !loadManager.IsGameLoaded) return false;
    var saveManager = S1Persistence.SaveManager.Instance;
    if (saveManager == null) return false;
    saveManager.Save();
    return true;
}
catch { return false; }
```

**3. Check singleton existence first.** Every game manager derives from `Singleton<T>` / `NetworkSingleton<T>` /
`PlayerSingleton<T>`, all of which expose `static bool InstanceExists` **[V]**. S1API calls
`EmployeeManager.InstanceExists` before `.Instance` **[V]**. Do the same, every time.

**4. Reflective member access with a graceful miss.** S1API has an internal toolkit for exactly this —
`GetTypeByName(string)`, `GetMethod(Type?, string, BindingFlags)`, `TryGetFieldOrProperty(object, string)`,
`TrySetFieldOrProperty`, `TryGetStaticFieldOrProperty`, `TrySetStaticFieldOrProperty`, `GetAllFields`,
`GetDerivedClasses<TBase>()` **[V]**. **They are `internal`, so you cannot call them** — but that list is a good spec
for the helper we should write. `CreativeModeMod.cs` already ships the same idea (console-command + reflection +
native fallbacks for quest state and NPC relationships) **[V]** per `CONTEXT.md`; lift it into `Expansions.Core`.

Personnel's version of the same discipline, verbatim: *"The impostor builder API only exists in newer S1API builds, so
this is best-effort via reflection: present -> pick a deterministic impostor, absent -> silently skip"* **[V]**.

**5. Signature-drift guard at the call site.** HonestMainMenu catches `MissingMethodException` on
`LoadManager.StartGame` and retries the older 2-arg overload **[V]**. Cartel Enforcer and NACops patch all three
`Console.SubmitCommand` overloads because which one exists varies **[V]**. Generic shape:

```csharp
static bool TryInvokeAny(object? target, Type owner, string name, params object?[] args)
{
    foreach (var m in owner.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.Instance | BindingFlags.Static))
    {
        if (m.Name != name) continue;
        if (m.GetParameters().Length != args.Length) continue;
        try { m.Invoke(target, args); return true; } catch { /* try the next overload */ }
    }
    return false;
}
```
**[I]** — assembled from verified practice, not run.

**6. Type-level feature detection.** `Type.GetType("Il2CppScheduleOne.…")` is the natural IL2CPP form **[I]** — but note
`Type.GetType` with a bare name only searches the calling assembly and mscorlib, so use the assembly-qualified name or
scan `AppDomain.CurrentDomain.GetAssemblies()`. **No Schedule I mod in the survey was observed doing this**, so it's
reasoning, not observed practice.

**7. ⚠️ Do NOT gate on `Application.version` until you've printed it once.** Unity returns the Player Settings value;
the `.exe` file version is Unity's own `2022.3.62.7762112`, not the game version. **[U]** — format unconfirmed.

**8. Never let a failed patch take the mod down** (§2.7), and **auto-disable a misbehaving module**. Our scaffolding
already disables a module that throws 10× in a frame hook for the session while leaving the persisted toggle on **[V]**
per `CONTEXT.md` — that is exactly right; keep it.

**9. Teardown discipline.** NACops' `ExitPreTask()` stops every coroutine, clears every static collection, `Destroy`s
created `Texture2D`s and `DestroyImmediate`s instanced `ScriptableObject` `AvatarSettings` **[V]**. Mods are
re-initialised per session via `LoadManager.Instance.onLoadComplete` and `OnSceneWasInitialized(buildIndex == 1)`, so
**without this you leak across save loads** **[I]**. Our `ModuleLifetime` (disposes tracked objects and subscriptions
in reverse order) is the right vehicle **[V]**.

## 8.2 The version-tracking toolchain

| Tool | Use | Trust |
|---|---|---|
| ⭐ [`k073l/s1-codearchiver`](https://github.com/k073l/s1-codearchiver) branch `alternate` | Auto-tracked **method-stripped** decompile of the current build (2,026 `.cs` files; latest commit `3973cfc7`, *"Auto-update for version 0.4.6f11 Alternate"*, 2026-08-01). Raw URLs work: `https://raw.githubusercontent.com/k073l/s1-codearchiver/alternate/ScheduleOne-stripped/Law/LawManager.cs`. **Diff two commits to see exactly what a patch changed — that is the whole point of the repo.** | **[V]**. High for *signatures*, zero for *bodies*. ⚠️ no LICENSE, content © TVGS. It decompiles **Mono** — signatures are strong evidence for IL2CPP but not proof |
| `k073l/RefGen` | Per-version reference-assembly NuGet packages, so *"building against an older game version is just a version bump"* | **[C]**, MIT |
| `Skippeh/ScheduleOne_UnityProject` | Stripped scripts + `.meta` files in a real Unity project — needed for prefab/ScriptableObject/UI-hierarchy inspection. **Requires Unity 2022.3.62f2, which exactly matches our install.** | **[V]** metadata. ⚠️ Mono branches only, no LICENSE |
| Interop-assembly staleness check | Compare `GameAssemblyHash` in `MelonLoader/Dependencies/Il2CppAssemblyGenerator/Config.cfg` against the SHA512 of `GameAssembly.dll` | **[C]** Cookbook |
| `cpp2il_out` cleanup | Cpp2IL never cleans `MelonLoader/Dependencies/Il2CppAssemblyGenerator/Cpp2IL/cpp2il_out/`, so after a game update stale assemblies merge with the fresh dump and `Pass11ComputeTypeSpecifics.ComputeSpecifics` throws an NRE. **Delete `cpp2il_out` and relaunch.** *"This only hits you when updating from an older version"* — which is why the same game version loads for one person and not another. | **[C]** Cookbook + official wiki, independently |

**Explicitly do not use:** `thecatontheceiling/scheduleone` — **404, deleted** (verified: API 404 *and* absent from that
user's 24 public repos). Its DeepWiki snapshot documents **v0.3.5**, five minor versions stale **[V]**.
`FearAndDelight/Schedule-1-Modder-Documentation` also 404s; only a `github-wiki-see.page` mirror survives **[V]**.

## 8.3 Known breakages worth internalising

- **v0.4.1f6 removed `LandVehicle.onPlayerEnter` / `onPlayerExit`**; the replacement is per-player
  `Player.onEnterVehicle` / `Player.onExitVehicle`, subscribed via `Player.onPlayerSpawned` so late joiners are caught.
  The community published a drop-in `VehicleEvents : Singleton<VehicleEvents>` replacement **[C]**. **This one is
  directly on our path** — Hireable Drivers must know when a driver enters a van.
- **0.3.6f6 → 0.4.0** was disruptive enough that a machine-readable class/method diff was published
  ([`S1DataMining/diff.json`](https://github.com/GuysWeForgotDre/S1DataMining/blob/main/diff.json)) — ⚠️ *"the arguments
  were entered backwards, so the `Added` and `Removed` labels are reversed"* **[C]**.
- **0.4.6 dropped twelve assemblies** (HBAO, RadiantGI, Cinemachine, StylizedWaterForURP, StylizedGrass,
  Postprocessing, EasyFeedback, Boxophobic.Utils), breaking interop generation for anyone *updating* **[C]**.
- **Unity 6000.x is currently broken for MelonLoader generally** (duplicate `<>O` type in `UnityEngine.CoreModule`),
  workaround `V1ndicate1/FixCoreModule` **[C]**. Not our problem on 2022.3.62f2, but it's the class of failure to
  expect if TVGS ever upgrades Unity.

## 8.4 Publishing checklist (from what actively-maintained mods do)

**[V]** for the practices, **[I]** for it being our checklist:

- State the exact tested **game version** and **MelonLoader version** in the README and on the Thunderstore page.
  Thunderstore renders a `Game version:` field; README badges (`Game | MelonLoader | Status`) are the other norm.
- Ship a `CHANGELOG.md`.
- Correct `MelonPlatformDomain` + `VerifyLoaderVersion`.
- Correct `manifest.json` dependencies so r2modman and Gale resolve S1API.
- Tell users to **delete** the old `.dll` rather than overwrite, and to remove a broken mod entirely rather than run it
  (*"A broken mod loading at startup can cause crashes or, in rare cases, save corruption"*) **[C]**.
- Treat any patch note mentioning *"scripting changes"* or *"backend changes"* as a red flag.

---

# 9. Open questions — verify before building on these

| # | Question | Status | How to settle it |
|---|---|---|---|
| 1 | Does a MelonPreferences category id containing `.` round-trip correctly through Tomlet alongside a same-named parent category? | **[U]** | Launch once, inspect `UserData\Expansions.cfg`, change a value, relaunch. Or just rename (§7.3) |
| 2 | Is there a public registration entry point for `BaseConsoleCommand`? | **[U]** | Read `S1API/Internal/Patches/ConsolePatches.cs` |
| 3 | Does `NPCSchedule.InitializeActions()` exist? | **[U]** — in the docs, not found in source | Grep S1API 3.1.7 source; `Schedule.Enable()` alone is verified |
| 4 | Are `InstanceFinder.IsHost` / `.IsClient` present in our interop assemblies? | **[U]** | Reflect over `Il2CppFishNet.InstanceFinder`. Meanwhile use `IsServer` |
| 5 | `Il2CppScheduleOne.Networking.Lobby` vs `Singleton<Lobby>` — which does our build expose? | **[U]** | Grep `il2cpp-assemblies-listing.txt` / the interop assemblies |
| 6 | Does patching a nested coroutine `MoveNext` work under IL2CPP here? | **[U]** | Only test if you truly need it; prefer patching the state-transition owner |
| 7 | Does `[HideFromIl2Cpp]` matter for our injected types? | **[U]** — zero usage found anywhere in S1API | Add only if injection fails |
| 8 | What does `Application.version` actually return? | **[U]** | Print it once |
| 9 | Are `GUI.TextField` / `GUI.DrawTexture` genuinely stripped, or was that a different failure? | **[C]** — our own local finding, no external corroboration found | Only matters if we revisit IMGUI |
| 10 | Do `k073l/s1-employeetweaks` and `BusinessEmployment` show a working employee-creation path? | Not yet read | **Highest-value remaining read for Hireable Drivers** |

---

## Appendix — the ten rules, compressed

1. Compile against the runtime DLLs in `MelonLoader\net6`; never bundle S1API.
2. `MelonGame("TVGS","Schedule I")` + `MelonPlatformDomain(IL2CPP)` + `VerifyLoaderVersion("0.7.2", true)`. Never `MelonPriority(Int32.MinValue)`.
3. Prefix/postfix only. No transpilers on game code. Match generated FishNet methods by prefix, not by hash suffix.
4. One Harmony instance per module + `[HarmonyDontPatchAll]` on the `MelonMod`, or reversibility is a lie.
5. Patch on the first gameplay scene, not at load. Wrap `PatchAll` in try/catch.
6. Custom NPCs = `S1API.Entities.NPC` + `NPCPrefabBuilder`. Never subclass a game `NetworkBehaviour`.
7. `ConfigurePrefab` is called on an **uninitialized** instance — constants only.
8. `Appearance.Build()` and an impostor, or your NPC is a blank billboard at 50m.
9. Gate every state mutation on `InstanceFinder.NetworkManager == null || InstanceFinder.IsServer`.
10. `MelonPreferences` for settings, `Saveable` for save state, `InstanceExists` before `.Instance`, try/catch around every raw interop call.

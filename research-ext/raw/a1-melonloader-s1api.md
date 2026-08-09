> **Research track A1** — external research only (web + public repos). No local mod source was read; no game launch.
> Research date: **2026-08-03**. Claims are tagged **[verified]** (primary source read directly), **[community]** (wiki/forum/third-party claim), or **[inference]** (my reasoning from verified facts).

## 1. MelonLoader IL2CPP modding of Schedule I — current best practices (2026)

### 1.1 Verified baseline for *this* machine

I read the installed MelonLoader assemblies directly (file version metadata), because "MelonLoader 0.7.x" is not precise enough to reason about Harmony or Il2CppInterop behaviour.

| Component | Version | How verified |
|---|---|---|
| MelonLoader | **0.7.1** (`0.7.1+0a690474…`) | **[verified]** `FileVersionInfo` of `MelonLoader\net6\MelonLoader.dll` |
| `0Harmony.dll` (HarmonyX) | **2.10.2** | **[verified]** `FileVersionInfo` of `MelonLoader\net6\0Harmony.dll` |
| Il2CppInterop.{Runtime,Common,Generator,HarmonySupport} | **1.5.0** (`1.5.0-ci.625`) | **[verified]** same method |
| Mono.Cecil | 0.11.6 | **[verified]** |
| MonoMod.RuntimeDetour | 22.07.31.01 | **[verified]** |
| Newtonsoft.Json (ML-shipped) | 13.0.3 | **[verified]** |
| Unity engine | **2022.3.62f2** (`7670c08855a9`) | **[verified]** `FileVersionInfo` of `Schedule I.exe` ProductVersion |
| Game version (current public) | **v0.4.6** / build `0.4.6f11` | **[verified]** Steam news + the `s1-codearchiver` auto-commit "Auto-update for version 0.4.6f11 Alternate" dated 2026-08-01 |

Two important consequences:

1. **Harmony is 2.10.2, not 2.15.** The official S1API template pins `HarmonyX 2.15.0` as a compile-time `PackageReference` (`PrivateAssets="all"`), but at runtime MelonLoader loads its own 2.10.2. **[verified]** for both halves; **[inference]** the practical guidance: compile against the `0Harmony.dll` that ships in `MelonLoader\net6` rather than a NuGet HarmonyX, so you can never bind to an API added after 2.10.2.
2. **Schedule I is *still* in 0.4.x.** The "Warehouses & Logistics" era content is behind us; v0.4.6 (2026) is "Gamepad Support + Optimization", and the **Special Customers** update was announced as in-development and *not yet shipped* as of the v0.4.6 announcement. **[verified]** — [Steam news v0.4.6](https://store.steampowered.com/news/app/3164500/view/684135019607754378), [patchbot changelog](https://patchbot.io/games/schedule-i). This matters directly: a "Special Customers" mod is building a feature the developer has publicly said is coming, so expect a hard collision on a future patch. **[inference]**

### 1.2 Correct `.csproj` shape for a Schedule I MelonLoader mod (2026)

The authoritative shape is the official S1API template, `ifBars/S1APITemplate/S1APITemplate.csproj` (last commit 2026-08-02). **[verified]** — I cloned the repo and read the file. Key structural decisions it makes:

- Two target frameworks selected by `$(Configuration)`: `netstandard2.1` for Mono, `net6.0` for IL2CPP.
- Three configurations: `CrossCompat`, `Mono`, `Il2cpp`, each with its own `AssemblyName` so the two backend DLLs can coexist in one `Mods` folder.
- `DefineConstants` adds `MONO` / `IL2CPP` / `CROSS_COMPAT`.
- All local paths are pushed into an untracked `local.build.props`.

Quoted verbatim from `S1APITemplate.csproj` (<https://github.com/ifBars/S1APITemplate/blob/main/S1APITemplate.csproj>):

```xml
  <PropertyGroup>
    <TargetFrameworks>netstandard2.1;net6.0</TargetFrameworks>
    <TargetFramework Condition="'$(Configuration)' != 'Il2cpp'">netstandard2.1</TargetFramework>
    <TargetFramework Condition="'$(Configuration)' == 'Il2cpp'">net6.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>S1APITemplate</RootNamespace>
    <LangVersion>latest</LangVersion>
    <NeutralLanguage>en-US</NeutralLanguage>
    <Configurations>CrossCompat;Mono;Il2cpp</Configurations>
    <Platforms>AnyCPU</Platforms>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <CopyLocalLockFileAssemblies>false</CopyLocalLockFileAssemblies>
    ...
  </PropertyGroup>

  <PropertyGroup Condition="'$(Configuration)' == 'CrossCompat'">
    <DefineConstants>$(DefineConstants);CROSS_COMPAT;MONO</DefineConstants>
    <AssemblyName>S1APITemplate</AssemblyName>
  </PropertyGroup>

  <PropertyGroup Condition="'$(Configuration)' == 'Mono'">
    <DefineConstants>$(DefineConstants);MONO</DefineConstants>
    <AssemblyName>S1APITemplate_Mono</AssemblyName>
  </PropertyGroup>

  <PropertyGroup Condition="'$(Configuration)' == 'Il2cpp'">
    <DefineConstants>$(DefineConstants);IL2CPP</DefineConstants>
    <AssemblyName>S1APITemplate_Il2cpp</AssemblyName>
  </PropertyGroup>
```

Its IL2CPP reference block, quoted verbatim (same file):

```xml
  <ItemGroup Label="Il2CppGameReferences" Condition="'$(Configuration)' == 'Il2cpp' and '$(Il2CppAssembliesPath)' != ''">
    <Reference Include="Assembly-CSharp">
      <HintPath>$(Il2CppAssembliesPath)\Assembly-CSharp.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="Assembly-CSharp-firstpass">
      <HintPath>$(Il2CppAssembliesPath)\Assembly-CSharp-firstpass.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="Il2CppInterop.Runtime">
      <HintPath>$(MelonLoaderAssembliesPath)\Il2CppInterop.Runtime.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
```

Two naming facts worth internalising, both **[verified]** from that file and from `S1API/S1API.csproj`:

- In `MelonLoader\Il2CppAssemblies`, the game assembly file is still named **`Assembly-CSharp.dll`** (not `Il2CppAssembly-CSharp.dll`) — it's the *namespaces inside* that get the `Il2Cpp` prefix. Some game-adjacent assemblies *do* get prefixed filenames (`Il2CppScheduleOne.Core.dll`, `Il2CppFishNet.Runtime.dll`, `Il2Cppmscorlib.dll`, `Il2CppNewtonsoft.Json.dll`). **[verified]** — S1API's own csproj references `$(Il2CppAssembliesPath)\Assembly-CSharp.dll` alongside `$(Il2CppAssembliesPath)\Il2CppScheduleOne.Core.dll` and `$(Il2CppAssembliesPath)\Il2Cppmscorlib.dll`.
- MelonLoader's own managed assemblies live in `MelonLoader\net6` for IL2CPP and `MelonLoader\net35` for Mono. **[verified]** — `example.build.props` in the S1API repo.

**Complete working csproj for an IL2CPP-only Schedule I mod that uses S1API.Forked.** This is my synthesis of the verified template + S1API's own csproj + the verified local paths; treat the structure as verified and the exact combination as **[inference]**:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <!-- Keep machine-specific paths out of source control. -->
  <Import Project="local.build.props" Condition="Exists('local.build.props')" />

  <PropertyGroup>
    <TargetFramework>net6.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <!-- Leave default compile items ON unless you deliberately hand-pick sources.
         EnableDefaultCompileItems=false forces you to list every .cs, which is a
         common source of "my new file isn't being built" confusion. -->
    <EnableDefaultCompileItems>true</EnableDefaultCompileItems>
    <CopyLocalLockFileAssemblies>false</CopyLocalLockFileAssemblies>
    <AssemblyName>MyScheduleMod</AssemblyName>
    <RootNamespace>MyScheduleMod</RootNamespace>
    <Version>1.0.0</Version>
    <DefineConstants>$(DefineConstants);IL2CPP</DefineConstants>

    <GameDir Condition="'$(GameDir)' == ''">C:\Program Files (x86)\Steam\steamapps\common\Schedule I</GameDir>
    <Il2CppAssembliesPath>$(GameDir)\MelonLoader\Il2CppAssemblies</Il2CppAssembliesPath>
    <MelonLoaderAssembliesPath>$(GameDir)\MelonLoader\net6</MelonLoaderAssembliesPath>
  </PropertyGroup>

  <!-- S1API: the ONLY package reference you need for cross-runtime game access. -->
  <ItemGroup>
    <PackageReference Include="S1API.Forked" Version="3.1.7" />
  </ItemGroup>

  <!-- MelonLoader + Harmony: reference the DLLs the game actually loads,
       NOT the NuGet packages, so compile-time and runtime versions match. -->
  <ItemGroup>
    <Reference Include="MelonLoader">
      <HintPath>$(MelonLoaderAssembliesPath)\MelonLoader.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="0Harmony">
      <HintPath>$(MelonLoaderAssembliesPath)\0Harmony.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="Il2CppInterop.Runtime">
      <HintPath>$(MelonLoaderAssembliesPath)\Il2CppInterop.Runtime.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="Il2CppInterop.Common">
      <HintPath>$(MelonLoaderAssembliesPath)\Il2CppInterop.Common.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>

  <!-- Unity + game assemblies from the MelonLoader-generated interop folder. -->
  <ItemGroup>
    <Reference Include="Il2Cppmscorlib">
      <HintPath>$(Il2CppAssembliesPath)\Il2Cppmscorlib.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine">
      <HintPath>$(Il2CppAssembliesPath)\UnityEngine.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine.CoreModule">
      <HintPath>$(Il2CppAssembliesPath)\UnityEngine.CoreModule.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine.IMGUIModule">
      <HintPath>$(Il2CppAssembliesPath)\UnityEngine.IMGUIModule.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine.InputLegacyModule">
      <HintPath>$(Il2CppAssembliesPath)\UnityEngine.InputLegacyModule.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine.UI">
      <HintPath>$(Il2CppAssembliesPath)\UnityEngine.UI.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine.UIModule">
      <HintPath>$(Il2CppAssembliesPath)\UnityEngine.UIModule.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine.TextRenderingModule">
      <HintPath>$(Il2CppAssembliesPath)\UnityEngine.TextRenderingModule.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine.ImageConversionModule">
      <HintPath>$(Il2CppAssembliesPath)\UnityEngine.ImageConversionModule.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine.PhysicsModule">
      <HintPath>$(Il2CppAssembliesPath)\UnityEngine.PhysicsModule.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine.AIModule">
      <HintPath>$(Il2CppAssembliesPath)\UnityEngine.AIModule.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="Unity.TextMeshPro">
      <HintPath>$(Il2CppAssembliesPath)\Unity.TextMeshPro.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>

  <!-- OPTIONAL: only add these if you accept losing Mono cross-compat. -->
  <ItemGroup Condition="'$(ReferenceGameAssembly)' == 'true'">
    <Reference Include="Assembly-CSharp">
      <HintPath>$(Il2CppAssembliesPath)\Assembly-CSharp.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="Il2CppFishNet.Runtime">
      <HintPath>$(Il2CppAssembliesPath)\Il2CppFishNet.Runtime.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>

  <Target Name="DeployToGame" AfterTargets="Build" Condition="'$(AutomateLocalDeployment)' == 'true'">
    <MakeDir Directories="$(GameDir)\Mods" />
    <Copy SourceFiles="$(TargetPath)" DestinationFolder="$(GameDir)\Mods" SkipUnchangedFiles="true" />
  </Target>
</Project>
```

**Should you reference `Assembly-CSharp.dll` directly, or go through reflection?** The S1API documentation gives an unambiguous answer, quoted verbatim from `S1API/docs/getting-started.md`:

> **Important Warning:** Do not add the game's `Assembly-CSharp.dll` as a reference when using S1API.Forked unless you know what you are doing. Referencing the game's assembly directly loses the cross compatability of the API.

**[verified]** — <https://github.com/ifBars/S1API/blob/stable/S1API/docs/getting-started.md>.

My read of the trade-off, **[inference]**: hard-referencing `Assembly-CSharp.dll` gives you compile-time checking against the *exact* build you generated interop assemblies from, and that is genuinely valuable for reverse-engineering work. It also means a rename in a game patch is a *compile* error rather than a `TypeLoadException` at runtime — which is arguably better, since you find out at build time. The cost is that your DLL is now hard-bound: it will `TypeLoadException` on any game version where a referenced member moved, and it can't run on Mono. The pragmatic pattern used by mature Schedule I mods is: **use S1API for everything it covers, hard-reference `Assembly-CSharp` only in an isolated adapter file for the gaps, and wrap every adapter call in try/catch with a feature-detect.**

### 1.3 Mono vs IL2CPP, and the community's conditional-compilation convention

**The branch mapping** (quoted verbatim from the S1API template's `references/build-config.md`, and independently stated in the community wiki's `common_terms`):

```text
none/beta = IL2CPP
alternate/alternate-beta = Mono
```

**[verified]** — <https://github.com/ifBars/S1APITemplate/blob/main/.agents/skills/schedule-one-modding/references/build-config.md>. i.e. the *default* Steam branch is IL2CPP; the `alternate` Steam beta branch is a Mono build of the same game. That Mono branch is what everyone actually decompiles, because Mono IL decompiles cleanly.

**There are two different symbol conventions in this ecosystem, and mixing them up will silently compile the wrong branch.** This is the single most important detail in this section:

| Convention | Symbols | Who uses it |
|---|---|---|
| Community / mod-author convention | `MONO` / `IL2CPP` | Community wiki, `S1APITemplate`, `S1MelonModTemplate`, `S1MONO_IL2CPP_Template` — i.e. **what you should use in your own mod** |
| S1API-internal convention | `MONOMELON` / `IL2CPPMELON` | Only inside the S1API repo itself (its `Configurations` are literally named `MonoMelon` / `Il2CppMelon`) |

**[verified]** for both: the community wiki page `moddevs/il2cpp.md` uses `#if IL2CPP`; the S1API template's csproj emits `IL2CPP`/`MONO`; every S1API source file I read (e.g. `S1API/PhoneApp/PhoneApp.cs`, `S1API/Quests/Quest.cs`, `S1API/Console/ConsoleHelper.cs`) uses `#if IL2CPPMELON` / `#elif MONOMELON`.

The community wiki's canonical example, quoted verbatim from `content/docs/moddevs/il2cpp.md`:

```csharp
#if IL2CPP
using Il2CppScheduleOne;
#else
using ScheduleOne;
#endif
public class MyMod : MelonMod
{
    public override void OnInitializeMelon()
    {
#if IL2CPP
        // Il2Cpp specific code
        MelonLogger.Msg("Running on Il2Cpp");
#else
        // Mono specific code
        MelonLogger.Msg("Running on Mono");
#endif
    }
}
```

**[verified]** — <https://github.com/s1modding/s1modding.github.io/blob/main/content/docs/moddevs/il2cpp.md>.

S1API's own internal pattern is more disciplined and worth copying: it aliases the namespace once at the top of the file and then writes runtime-neutral code below. Quoted verbatim from `S1API/Quests/Quest.cs` lines 1–13:

```csharp
#if (IL2CPPMELON)
using S1Quests = Il2CppScheduleOne.Quests;
using S1Dev = Il2CppScheduleOne.DevUtilities;
using S1Map = Il2CppScheduleOne.Map;
using S1Data = Il2CppScheduleOne.Persistence.Datas;
using S1Contacts = Il2CppScheduleOne.UI.Phone.ContactsApp;
#elif MONOMELON
using S1Quests = ScheduleOne.Quests;
using S1Dev = ScheduleOne.DevUtilities;
using S1Map = ScheduleOne.Map;
using S1Data = ScheduleOne.Persistence.Datas;
using S1Contacts = ScheduleOne.UI.Phone.ContactsApp;
#endif
```

**[verified]** — <https://github.com/ifBars/S1API/blob/stable/S1API/Quests/Quest.cs>. The guidance in the template's `il2cpp-modding.md` reference states this as a rule: *"Keep `#if MONO` / `#if IL2CPP` near imports, aliases, delegates, casts, injected types, and adapters. Keep business logic runtime-neutral behind small helpers."* **[verified]**.

**Shipping two DLLs.** The convention is one DLL per backend, distinguished by `AssemblyName` (`Foo_Mono.dll`, `Foo_Il2cpp.dll`) — from `S1APITemplate.csproj` **[verified]**. S1API itself ships `S1API.Il2Cpp.MelonLoader.dll` and `S1API.Mono.MelonLoader.dll` into `Mods\` and lets a *plugin* enable exactly one at startup (see §2.3).

**Runtime detection**, if you want a single DLL that branches at runtime rather than compile time: `MelonUtils.IsGameIl2Cpp()`. **[verified]** — used in `S1APILoader/S1APILoader.cs` line 31: `string activeBuild = MelonUtils.IsGameIl2Cpp() ? "Il2Cpp" : "Mono";`.

**For this project specifically** — the workspace is IL2CPP-only and `CONTEXT.md` already records the decision to target IL2CPP only. **[inference]** I'd keep that. Dual-targeting costs real complexity and buys nothing for a single-machine install; and if you *do* want cross-compat later, S1API already gives it to you for free as long as you stay on S1API abstractions.

### 1.4 `MelonMod` lifecycle in 0.7.1

The best in-ecosystem reference implementation is S1API's own root mod. Quoted verbatim from `S1API/S1API.cs` (<https://github.com/ifBars/S1API/blob/stable/S1API/S1API.cs>) **[verified]**:

```csharp
[assembly: MelonInfo(typeof(S1API.S1API), "S1API (Forked by Bars)", "3.1.7", "KaBooMa")]
[assembly: MelonPriority(Int32.MinValue)]
namespace S1API
{
    public class S1API : MelonMod
    {
        public override void OnInitializeMelon() { ... }
        public override void OnDeinitializeMelon() { ... }
        public override void OnUpdate() { ... }
        public override void OnGUI() { ... }
        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            if (sceneName == "Main")
            {
                GameLifecycle.Initialize();
            }
        }
        public override void OnSceneWasUnloaded(int buildIndex, string sceneName) { ... }
        public override void OnSceneWasInitialized(int buildIndex, string sceneName) { ... }
    }
}
```

Notable, all **[verified]** from that file:

- `[assembly: MelonPriority(Int32.MinValue)]` — S1API forces itself to load first. Your mod should **not** use `Int32.MinValue`; leave priority default or use a modest positive value so you load after S1API.
- Scene names that matter: **`"Main"`** (the gameplay scene) and **`"Tutorial"`**. S1API triggers `GameLifecycle.Initialize()` on `OnSceneWasLoaded("Main")` and resets on `OnSceneWasUnloaded("Main")`.
- `OnSceneWasInitialized` is where S1API does IL2CPP prefab warmup (`NPCNetworkBootstrap.EnsurePrefabsWarmup()`), i.e. it's *later* than `OnSceneWasLoaded` and safer for touching scene objects.

**`OnInitializeMelon` vs `OnLateInitializeMelon`.** `OnInitializeMelon` runs while other melons may still be loading; `OnLateInitializeMelon` runs after all melons have initialized. **[community]** — MelonLoader's own documented behaviour (<https://melonwiki.xyz/#/modders/quickstart>); I did not read MelonLoader source. **[inference]** Practical rule for this project: register `MelonPreferences` and Harmony patches in `OnInitializeMelon`; do anything that depends on *another mod* (including asking whether S1API is present) in `OnLateInitializeMelon`.

**The `MelonGame` attribute for Schedule I.** The exact strings, quoted verbatim from `S1API/docs/tv-app.md` lines 173–174:

```csharp
[assembly: MelonInfo(typeof(HelloWorldTVApp.Core), "HelloWorldTVApp", "1.0.0", "YourName")]
[assembly: MelonGame("TVGS", "Schedule I")]
```

**[verified]** — <https://github.com/ifBars/S1API/blob/stable/S1API/docs/tv-app.md>. So: developer = `"TVGS"`, game = `"Schedule I"` (with the space, matching the Steam folder name). Note S1API and S1APILoader themselves deliberately omit `MelonGame` — a melon with no `MelonGame` attribute is treated as universal. **[verified]** (I grepped the whole repo; the only `MelonGame` occurrence is in that doc file). **[inference]** For your mods, *do* include `[assembly: MelonGame("TVGS", "Schedule I")]`; it makes MelonLoader refuse to load your DLL into the wrong game, which is a cheap safety net.

**`MelonEvents`** — subscribe rather than override when you want a priority-ordered callback or you're outside your mod class. Quoted verbatim from the community wiki `moddevs/melonloader_utilities.md`:

```csharp
MelonEvents.OnUpdate.Subscribe(() =>
{
    // This code will be executed every frame
    Melon<MyMod>.LoggerInstance.Msg("Updating...");
}, 100); // The higher the number, the lower the priority.
```

**[verified]** — <https://github.com/s1modding/s1modding.github.io/blob/main/content/docs/moddevs/melonloader_utilities.md>.

**Coroutines.** `MelonCoroutines.Start(IEnumerator)` / `MelonCoroutines.Stop(object)`. Critically, from the same wiki: *"Don't put **IEnumerator**s in types registered with `RegisterTypeInIl2Cpp`, as they will not work properly."* **[verified]**.

**Logging.** `LoggerInstance` in instance context; `Melon<MyMod>.LoggerInstance` in static context. **[verified]** same wiki page.

### 1.5 Harmony patching under Il2CppInterop

**Version.** HarmonyX **2.10.2** ships in `MelonLoader\net6\0Harmony.dll` on this machine. **[verified]**. `Il2CppInterop.HarmonySupport.dll` 1.5.0 is what teaches Harmony how to detour IL2CPP native methods. **[verified]**.

**The hardest rule: no transpilers on IL2CPP.** Quoted verbatim from the community wiki `moddevs/patching.md`:

> **Important:** Transpilers modify the IL code, and thus, will not work on IL2CPP builds, as they are compiled to C++ and then to machine code. Transpilers are only available for Mono builds.

**[verified]** — <https://github.com/s1modding/s1modding.github.io/blob/main/content/docs/moddevs/patching.md>. The template's `il2cpp-modding.md` reference repeats it as a hard rule: *"Prefer prefix/postfix patches. Do not use IL transpilers for IL2CPP builds."* **[verified]**.

**Attribute patching works normally** for the simple case. Quoted verbatim from the same wiki page:

```csharp
[HarmonyPatch(typeof(TargetClass), "TargetMethod")]
public class MyPatch
{
    public static bool Prefix(TargetClass __instance, ref int __result)
    {
        __result = 42;   // Change the return value to 42
        return false;    // Return false to skip the original method
    }
}
```

**[verified]**. `__instance` types as the **interop wrapper type**, i.e. `Il2CppScheduleOne.NPCs.NPC __instance`, not the Mono `ScheduleOne.NPCs.NPC`. **[inference]** from the namespace-prefix rule, corroborated by every S1API patch file being compiled against `Il2CppScheduleOne.*` under `IL2CPPMELON`.

**When `[HarmonyPatch(typeof(X), nameof(X.Y))]` isn't enough** — overloads, generics, methods whose name you only know at runtime, or methods that don't exist in the generated assemblies — use manual patching with a resolved `MethodInfo`. Quoted verbatim from the wiki:

```csharp
MethodInfo privateMethod = typeof(TargetClass).GetMethod("TargetMethod", new Type [] { typeof(int) });

// We pass in the method we want to patch, the prefix method, the postfix method, and the finalizer method
harmony.Patch(privateMethod, new HarmonyMethod(typeof(MyPatch), "PatchMethodName"), null, null);
```

**[verified]**. Combine with `[HarmonyDontPatchAll]` on your `MelonMod` class if you want full control over when patching happens — also **[verified]** from the same page:

```csharp
[HarmonyDontPatchAll] // From MelonLoader - prevents automatic patching of all methods in this class
public class MyMod : MelonMod
{
    HarmonyInstance harmony;
    public override void OnInitializeMelon()
    {
        harmony = this.HarmonyInstance;
        harmony.PatchAll(); // but we do it anyway, manually
    }
}
```

**Generated FishNet RPC/wrapper methods.** Schedule I is a FishNet networked game, and the decompiled source is full of compiler-generated members like `RpcWriter___Observers_Initialize_2260823878`, `RpcLogic___Initialize_2260823878`, `RpcReader___Target_Initialize_2260823878`, and `SyncAccessor__003CPaidForToday_003Ek__BackingField`. **[verified]** — I read `ScheduleOne/Employees/Employee.cs` from the decompiled archive; those exact names appear. The numeric suffixes are hash-derived and **will change between game versions**. The template's guidance is explicit: *"Resolve generated RPC/wrapper methods by stable prefix/signature, not suffixes."* **[verified]**. Practically that means enumerating `type.GetMethods()` and matching `m.Name.StartsWith("RpcLogic___Initialize")` rather than hardcoding the full name. **[inference]**

**Patching coroutines / `IEnumerator`.** A C# iterator compiles to a state-machine class with a `MoveNext()` method; patching the coroutine method itself only intercepts the *creation* of the enumerator, not its execution. To intercept execution you patch the nested `<MethodName>d__NN::MoveNext`. **[community]** — this is standard Harmony practice (`AccessTools.EnumeratorMoveNext` exists for exactly this), but I did not verify a Schedule I-specific example, and under IL2CPP the nested state-machine types may be name-mangled or inlined. **[inference]** For Schedule I I'd avoid patching coroutines entirely and instead patch the method that *owns the state transition* — which is exactly what the template's `decompilation-workflow.md` recommends: *"Patch the method that owns the state transition."* **[verified]**.

**Custom `MonoBehaviour`s and injected types.** Quoted verbatim from the community wiki `moddevs/il2cpp.md`:

```csharp
using Il2CppInterop.Runtime;
using Il2CppScheduleOne;

[RegisterTypeInIl2Cpp]
public class MyCommand : ConsoleCommand
{
    public MyCommand(IntPtr ptr) : base(ptr)
    {
    }

    // This constructor is required if we're instatiating from Mono side like
    // new MyCommand();
    // Not needed if not instantiating - don't use this constructor for MonoBehaviours
    public MyCommand() : base(ClassInjector.DerivedConstructorPointer<MyCommand>())
    {
        ClassInjector.DerivedConstructorBody(this);
    }
}
```

**[verified]**. The alternative is `ClassInjector.RegisterTypeInIl2Cpp<T>()` called manually at init. **[verified]** same page.

S1API's real-world usage, quoted verbatim from `S1API/PhoneApp/PhoneApp.cs` lines 717–731:

```csharp
#if IL2CPPMELON
    [RegisterTypeInIl2Cpp]
#endif
    internal class PhoneAppButtonHandler : MonoBehaviour
    {
        internal PhoneApp? phoneApp;

        private void Update()
        {
            // Replicate the native App<T> Update logic for physical button detection
            if (phoneApp != null && phoneApp.IsOpen() && Phone.InstanceExists && Phone.Instance.IsOpen && IsHoveringButton() && GameInput.GetButtonDown(GameInput.ButtonCode.PrimaryClick))
            {
                phoneApp.CloseApp();
            }
        }
        ...
    }
```

**[verified]**. Note that this injected `MonoBehaviour` has **no** `IntPtr` constructor and no `DerivedConstructorPointer` constructor — consistent with the wiki's note that the derived constructor is only needed if you `new` the type from managed code, which you never do for a `MonoBehaviour` (you `AddComponent` it).

**`[HideFromIl2Cpp]`** — apply to managed-only members on an injected type so the injector doesn't try to expose them to IL2CPP. **[community]** — stated in the template's `il2cpp-modding.md` (*"Managed-only helper methods on injected types may need `[HideFromIl2Cpp]`"*) **[verified as a claim in that file]**, but I found **no** actual usage of `[HideFromIl2Cpp]` anywhere in the S1API source (I grepped), so I can't show you a real Schedule I example. Treat as unverified-in-practice.

**Delegate marshalling.** This is where IL2CPP bites hardest. Three verified patterns:

1. **Inline cast for `UnityAction`** — quoted verbatim from the community wiki:
   ```csharp
   addButton.onClick.AddListener((UnityAction)(() => {
    // Do something
   }));
   ```
   **[verified]**.

2. **`DelegateSupport.ConvertDelegate<T>`** for game-defined delegate types — quoted verbatim from `S1API/PhoneApp/PhoneApp.cs` lines 293–298:
   ```csharp
   // Create IL2CPP-safe delegate instance
   #if IL2CPPMELON
                   _exitDelegate = DelegateSupport.ConvertDelegate<S1GameInput.ExitDelegate>(new System.Action<S1ExitAction>(HandleNativeExit));
   #else
                   _exitDelegate = new S1GameInput.ExitDelegate(HandleNativeExit);
   #endif
                   GameInput.RegisterExitListener(_exitDelegate, 1);
   ```
   **[verified]**. Note also the surrounding code caches `_exitDelegate` in a field — **you must keep a managed reference to a converted delegate or it will be garbage-collected and the native side will call into freed memory.** The field comments in that file say so explicitly: *"Cached action delegate for closeApps event subscription (IL2CPP compatibility)"*. **[verified]**

3. **Use S1API's wrappers** so you never write the above: `S1API.Utils.EventHelper.AddListener(Action, UnityEvent)` / `AddListener<T>(Action<T>, UnityEvent<T>)` / `RemoveListener(...)`, and `S1API.Utils.ButtonUtils.AddListener(Button, Action)`. **[verified]** — I read `S1API/Utils/EventHelper.cs` and `S1API/Utils/ButtonUtils.cs` and confirmed those exact signatures.

**Il2Cpp collections vs CLR collections.** Quoted verbatim from the community wiki:

```csharp
Il2CppSystem.Collections.Generic.List<int> list;
list.FirstOrDefault(); // This won't work
list._items.FirstOrDefault(); // This will work
```

**[verified]**. The template reference states the safer rule: *"Il2Cpp collections are not normal CLR collections; prefer explicit loops."* **[verified]**. S1API follows the explicit-loop rule — quoted verbatim from `S1API/Quests/QuestManager.cs`:

```csharp
#elif IL2CPPMELON
                var gameQuests = S1Quests.Quest.Quests;
                if (gameQuests != null)
                {
                    for (int i = 0; i < gameQuests.Count; i++)
                    {
                        var gameQuest = gameQuests[i];
                        if (gameQuest != null && gameQuest.Title == questTitle)
                        {
                            return gameQuest;
                        }
                    }
                }
#endif
```

**[verified]**. Note the `#if MONOMELON` branch of the same method uses a plain `foreach` — that asymmetry is deliberate.

Even S1API's own UI helper carries the warning — quoted verbatim from `S1API/UI/UIFactory.cs` `ClearChildren`:

```csharp
            // Use index-based iteration for IL2CPP compatibility to avoid invalid cast during enumeration
            for (int i = parent.childCount - 1; i >= 0; i--)
```

**[verified]**.

**Casting.** `Cast<T>()` throws, `TryCast<T>()` returns null. Quoted verbatim from the wiki: *"For types with no explicit cast, use `.Cast<T>()` or `.TryCast<T>()` method"* with `Type result = il2CppObj.Cast<Type>();`. **[verified]**. S1API's internal helper wraps both — quoted verbatim from `S1API/Internal/Utils/CrossType.cs`:

```csharp
        internal static bool Is<T>(object obj, out T result)
#if IL2CPPMELON
            where T : Il2CppObjectBase
#elif MONOMELON
            where T : class
#endif
        {
#if IL2CPPMELON
            if (obj is Object il2CppObj)
            {
                Type il2CppType = Il2CppType.Of<T>();
                if (il2CppType.IsAssignableFrom(il2CppObj.GetIl2CppType()))
                {
                    result = il2CppObj.TryCast<T>()!;
                    return true;
                }
            }
#elif MONOMELON
            if (obj is T t)
            {
                result = t;
                return true;
            }
#endif
            result = null!;
            return false;
        }
```

**[verified]** — this is `internal`, so you can't call it, but it's the exact pattern to copy.

**`Il2CppType.Of<T>()`** for runtime type tokens — quoted verbatim from the wiki:

```csharp
using Il2CppInterop.Runtime;
Resources.FindObjectsOfTypeAll(Il2CppType.Of<Camera>());
```

**[verified]**.

**`Il2CppStringArray`** — IL2CPP `string[]` parameters surface as `Il2CppStringArray` (in `Il2CppInterop.Runtime.InteropTypes.Arrays`). S1API imports that namespace in `UIFactory.cs` under `#if IL2CPPMELON`. **[verified]** that the import exists; I did **not** verify a concrete Schedule I call site that requires it, so treat "you will need it for X" as **[inference]**.

### 1.6 Surviving game updates

**The evidence base.** S1API's release notes are effectively a changelog of what Schedule I patches break, and they are unusually specific. The single best worked example, quoted verbatim from `S1API/docs/phone-app.md`:

> ### Migrating from S1API 3.0.6
>
> S1API 3.0.6 exposed `ScheduleOne.DevUtilities.ExitAction` directly. Schedule I
> 0.4.6f11 moved that native type and made the old signature impossible to retain.
> Change phone-app overrides to use the S1API-owned wrapper:
>
> ```csharp
> public override void Exit(S1API.PhoneApp.ExitAction exit)
> {
>     if (!exit.Used)
>     {
>         exit.Used = true;
>         // Close or reset custom UI state here.
>     }
> }
> ```

**[verified]** — <https://github.com/ifBars/S1API/blob/stable/S1API/docs/phone-app.md>. This is the pattern in miniature: a game patch *moved a namespace*, S1API absorbed it by introducing its own wrapper type, and downstream mods changed one signature instead of breaking entirely.

Other concrete breakages I can evidence:

- **v3.1.2 (2026-08-01):** *"A hotfix for custom phone apps on Schedule I 0.4.6f11. Custom phone apps now receive an independent icon created from the native phone-app prefab instead of renaming and rewiring the built-in Delivery icon."* **[verified]** — release notes. So 0.4.6f11 changed the phone home-screen enough to break every custom app icon.
- **Player energy was removed from the game.** Quoted verbatim from `S1API/Console/ConsoleHelper.cs`:
  ```csharp
  [System.Obsolete("Player energy was removed from newer game builds. This method is retained as a compatibility no-op where unavailable and may be removed in a future S1API version.")]
  public static void SetPlayerEnergyLevel(float amount)
  ```
  **[verified]**. Note the *shape* of the fix: keep the method, make it a logged no-op, don't throw.
- **`Saveable.RequestGameSave(bool immediate)`** — quoted verbatim from `S1API/Internal/Abstraction/Saveable.cs`: *"`<param name="immediate">`This parameter is ignored in v0.4.3+ (kept for backwards compatibility).`</param>`"*. **[verified]**. Same shape: keep the signature, ignore the argument.

**Defensive techniques, in rough order of how much I'd lean on them:**

1. **Prefer S1API for anything it covers.** Every S1API release is a free compatibility patch you didn't have to write. **[inference]**, but strongly supported by the above.
2. **Wrap native calls in try/catch and return a sentinel, don't throw.** S1API does this everywhere. Quoted verbatim from `Saveable.RequestGameSave()`:
   ```csharp
   try
   {
       var loadManager = S1Persistence.LoadManager.Instance;
       if (loadManager == null || !loadManager.IsGameLoaded)
           return false;
       var saveManager = S1Persistence.SaveManager.Instance;
       if (saveManager == null)
           return false;
       saveManager.Save();
       return true;
   }
   catch
   {
       return false;
   }
   ```
   **[verified]**.
3. **Check singleton existence before use.** Every game manager derives from `Singleton<T>` / `NetworkSingleton<T>` / `PlayerSingleton<T>`, all of which expose a static `InstanceExists` property. **[verified]** — I read `ScheduleOne/DevUtilities/Singleton.cs`, `NetworkSingleton.cs` and `PlayerSingleton.cs` from the decompiled archive; all three declare `public static bool InstanceExists => (Object)(object)instance != (Object)null;`. S1API calls `S1Employees.EmployeeManager.InstanceExists` before `.Instance` **[verified]** in `EmployeeManager.cs`.
4. **Reflective member access with a graceful miss.** S1API ships an internal toolkit for exactly this: `ReflectionUtils.GetTypeByName(string)`, `GetMethod(Type?, string, BindingFlags)`, `TryGetFieldOrProperty(object, string)`, `TrySetFieldOrProperty(object?, string, object?)`, `TryGetStaticFieldOrProperty(Type, string)`, `TrySetStaticFieldOrProperty(Type, string, object?)`, `GetAllFields(Type?, BindingFlags)`, `GetDerivedClasses<TBase>()`. **[verified]** — I read the signatures out of `S1API/Internal/Utils/ReflectionUtils.cs`. **These are `internal`, so you cannot call them** — but the list is a good spec for the equivalent helper you should write. **[inference]**
5. **`Type.GetType("Il2CppScheduleOne....")` feature detection.** **[inference]** — this is the natural IL2CPP form of a defensive lookup and follows from the verified namespace-prefix rule, but I did not find a Schedule I mod doing exactly this in the sources I read, so I'm flagging it as reasoning rather than observed practice. Caveat worth knowing: `Type.GetType` with a bare name only searches the calling assembly and mscorlib, so you generally need the assembly-qualified name or an `AppDomain.CurrentDomain.GetAssemblies()` scan.
6. **`Application.version`.** **[inference]** — Unity's `Application.version` returns the value set in Player Settings, which for Schedule I should track the `0.4.6` line. I did **not** verify what Schedule I actually puts there, and the `.exe` file version is Unity's own `2022.3.62.7762112`, not the game version. Do not gate behaviour on `Application.version` until you've printed it once and confirmed the format. Marked **unverified**.

**What the community says actually breaks each patch**, from the template's `il2cpp-modding.md` "Failure checklist", quoted verbatim **[verified]**:

```text
- Wrong backend DLL loaded.
- Wrong target framework or assembly references.
- Missing generated assemblies.
- Missing delegate conversion.
- Missing injected constructors.
- CLR cast or LINQ over Il2Cpp collection.
- Patch target changed after game update.
- Scene/network object accessed too early.
```

### 1.7 Pitfalls, symptoms, and fixes

| Pitfall | Symptom | Fix | Confidence |
|---|---|---|---|
| Stale `Il2CppAssemblies` after a game patch | `TypeLoadException` / `MissingMethodException` at melon load; members that exist in your IDE don't exist at runtime | Delete `MelonLoader\Il2CppAssemblies` and `MelonLoader\Dependencies\Il2CppAssemblyGenerator`, launch once to regenerate, rebuild | **[community]** — wiki troubleshooting page; also implied by S1APILoader's `ForceRegeneration` workaround **[verified]** |
| MelonLoader **0.7.1** specifically has an Il2CppInterop defect | Interop assemblies resolve wrongly; `Assembly.Load("Il2CppAssembly-CSharp")` fails | S1APILoader auto-patches it: it registers an `AssemblyResolve` fallback that strips the `Il2Cpp` prefix, invokes the private `MelonLoader.Fixes.Il2CppInteropFixes.Install()`, and forces `LoaderConfig.Current.UnityEngine.ForceRegeneration = true` once, guarded by a marker file in `UserData\S1API.InteropFix.marker` | **[verified]** — `S1APILoader/S1APILoader.cs` lines 53–72, 101–116, 260–308. **You are on 0.7.1, so this code path is live on your machine.** |
| `cpp2il_out\Assembly-CSharp.dll` is locked | Assembly generation hangs or fails | Check for a duplicate game launch or stale lock before blaming mod code | **[verified]** — template `local-game-introspection.md` |
| IMGUI members stripped from this IL2CPP build | `DrawWindow` aborts mid-frame → blank/partial window, often with no exception surfaced | Avoid the stripped member; use absolute-`Rect` `GUI.Button`/`GUI.Label`. Your `CONTEXT.md` already records `GUI.TextField` and `GUI.DrawTexture` as confirmed-stripped on this build | **[community]** — I could not reproduce or find external confirmation that these specific members are stripped in Schedule I; this is your own prior local finding, which I'm carrying forward unverified from outside |
| `MelonPreferences` file location | Config doesn't appear where expected | Default is `UserData\MelonPreferences.cfg`; per-category override via `category.SetFilePath("Foo/Bar.cfg"); category.SaveToFile();`. ML auto-saves on quit; `MelonPreferences.Save()` to force | **[verified]** — wiki `melonloader_utilities.md` |
| Extra managed/native dependencies | `FileNotFoundException` on your own dependency | Put extra managed DLLs in `UserLibs\`; MelonLoader adds it to the resolve path | **[community]** — standard MelonLoader layout; not verified against a Schedule I source in this research |
| Load-order vs S1API | `NullReferenceException` in S1API-dependent init | S1API is `MelonPriority(Int32.MinValue)` and its loader is a `MelonPlugin` running at `OnApplicationEarlyStart`, so it is always up before your mod — do **not** set your own priority to `Int32.MinValue` | **[verified]** — `S1API/S1API.cs` line 15, `S1APILoader/S1APILoader.cs` line 20 |
| Touching game state in `OnInitializeMelon` | NRE — no scene loaded yet | Gate on `OnSceneWasLoaded(_, "Main")`, or better, `S1API.Lifecycle.GameLifecycle.OnLoadComplete` | **[verified]** — S1API's own `OnSceneWasLoaded` gates on `"Main"`; `GameLifecycle` documented in `S1API/Lifecycle/GameLifecycle.cs` |
| Converted delegate GC'd | Native callback crashes or silently stops firing | Cache the `DelegateSupport.ConvertDelegate<T>` result in a field for the object's lifetime, and unsubscribe in teardown | **[verified]** — pattern + comments in `S1API/PhoneApp/PhoneApp.cs` |
| LINQ over an `Il2CppSystem.Collections.Generic.List<T>` | `InvalidCastException` during enumeration, or silently wrong results | Index-based `for` loop, or `list._items` | **[verified]** — wiki `il2cpp.md`; S1API `QuestManager.cs`, `UIFactory.ClearChildren` |
| `IEnumerator` inside a `[RegisterTypeInIl2Cpp]` type | Coroutine never advances / misbehaves | Move the iterator to a plain managed class and drive it with `MelonCoroutines.Start` | **[verified]** — wiki `il2cpp.md` |
| Transpiler on IL2CPP | Patch silently no-ops or throws at patch time | Prefix/postfix only | **[verified]** |
| Hardcoded FishNet RPC method names | Patch stops applying after a game update | Match by stable prefix (`RpcLogic___Initialize`), not the numeric suffix | **[verified]** — template `il2cpp-modding.md`; suffix instability visible in the decompiled `Employee.cs` |

---

## 2. S1API — full public-surface documentation

### 2.1 Identity, lineage, and license — **read this first, it changes what you should reference**

| Fact | Value | Source |
|---|---|---|
| Repo you should read | **<https://github.com/ifBars/S1API>** | **[verified]** cloned at commit `db421c99` (2026-08-03 15:38 -0600, "Merge pull request #195 from ifBars/releases/3.1.7") |
| Default branch | `stable` | **[verified]** GitHub API |
| **It is a fork.** Upstream = | **<https://github.com/KaBooMa/S1API>** | **[verified]** GitHub API: `"fork": true`, `"parent": {"full_name": "KaBooMa/S1API"}` |
| Upstream status | **Effectively abandoned** — last push `2025-05-23`, no license file (`"license": null`), 12 open issues | **[verified]** GitHub API |
| Fork status | Very much alive — last push **2026-08-03** (today), 22 ★, 27 forks, 4 open issues | **[verified]** GitHub API |
| **License (the fork)** | **MIT** — `LICENSE`: "MIT License / Copyright (c) 2025 S1API Contributors" | **[verified]** read the file; GitHub API also reports `spdx_id: MIT` |
| **License (upstream KaBooMa)** | **No license file in the repo.** The NuGet package `S1API` 1.6.2 nuspec *declares* `<license type="expression">MIT</license>` | **[verified]** — GitHub API `"license": null`; nuspec read from the downloaded `.nupkg`. ⚠️ This is a genuine inconsistency: the repo has no LICENSE, the package claims MIT. |
| Docs site | <https://ifbars.github.io/S1API/> (docfx, built from `S1API/docs/`) | **[verified]** — `homepage` field + `S1API/docfx.json` + `docs.yml` workflow |
| Other doc surfaces | DeepWiki <https://deepwiki.com/ifBars/S1API>, Context7 llms.txt <http://context7.com/ifbars/s1api/llms.txt> | **[verified]** — badges in README |
| Distribution | [Nexus 1194](https://www.nexusmods.com/schedule1/mods/1194), [Thunderstore ifBars/S1API_Forked](https://thunderstore.io/c/schedule-i/p/ifBars/S1API_Forked/), [GitHub Releases](https://github.com/ifBars/S1API/releases) | **[verified]** README |

**NuGet package identity — which one is `S1API.Forked`?**

| Package | Versions | Author | Repo URL in nuspec |
|---|---|---|---|
| `S1API` | 1.1.1 … **1.6.2** (dead since the upstream repo went quiet) | `KaBooMa` | `https://github.com/KaBooMa/S1API` @ `d9665e9b` |
| **`S1API.Forked`** | 1.6.7 … **3.1.7** (81 versions) | **`ifBars`** | **`https://github.com/ifBars/S1API`** @ `da37362c` (for 3.1.4) |

**[verified]** — I downloaded `s1api.forked.3.1.4.nupkg`, `s1api.forked.3.1.7.nupkg` and `s1api.1.6.2.nupkg` from the NuGet flat container and read the `.nuspec` out of each. So: **`S1API.Forked` is unambiguously `ifBars/S1API`**, and it is what you have installed.

**Your installed version is 3.1.4; latest is 3.1.7.** You are three patch releases behind, all shipped 2026-08-02/03. **[verified]** — flat-container version index + release notes. What's in the gap:

- **3.1.5** — custom-product mixing fixes; `ProductMixingProfileBuilder.WithPropertyColorMixing()`.
- **3.1.6** — one fix: show custom visuals in mix discovery.
- **3.1.7** — opt-in supplier persistence IDs; **"Restored civilian greetings and generic dialogue for custom NPCs created through the current `BaseEmployee` fallback"**; **"Restored residence-door summons for S1API custom NPCs"**.

**[verified]** — release notes via GitHub API. **[inference]** The 3.1.7 NPC fixes are directly relevant to a custom-NPC-heavy project. I'd upgrade to 3.1.7 before writing NPC code, and note that the "`BaseEmployee` fallback" phrasing hints S1API creates custom NPCs off an employee-derived prefab — worth checking if you're building driver NPCs.

**Assembly identity.** Both packages ship a single `lib/netstandard2.1/S1API.dll` — i.e. the **Mono** build is what you compile against, regardless of which runtime you ship to. The IL2CPP build is only distributed in the release ZIP / Thunderstore package as `Mods\S1API.Il2Cpp.MelonLoader.dll`. **[verified]** — nupkg contents: `lib\netstandard2.1\S1API.dll` (1,321,984 bytes for 3.1.4) + `lib\netstandard2.1\S1API.xml` (1,249,131 bytes of XML docs). Assembly name is `S1API`; root namespace `S1API`. **[verified]** — `S1API/S1API.csproj` `<RootNamespace>S1API</RootNamespace>`.

**Only runtime dependency: `Newtonsoft.Json` 13.0.2.** **[verified]** — nuspec dependency group. Note MelonLoader itself ships Newtonsoft.Json 13.0.3, so this resolves fine. **[inference]**

**Coverage.** The README self-reports **32.0% class coverage** of Schedule One's game types as of 2026-08-01, up from 25% in 2025-12. **[verified]** — README coverage badge and the embedded QuickChart data series. Read that as: *two thirds of the game is still not wrapped*, and you will be dropping to raw `Il2CppScheduleOne.*` for a meaningful fraction of anything ambitious.

### 2.2 How to reference it

Quoted verbatim from `S1API/docs/getting-started.md` **[verified]**:

> 2. **Add S1API.Forked as a Reference**
>    - Install the S1API.Forked NuGet package in your mod project:
>      - Using NuGet Package Manager: Search for "S1API.Forked" and install the latest version
>      - Using Package Manager Console: `Install-Package S1API.Forked`
>      - Using .NET CLI: `dotnet add package S1API.Forked`
>      - Using PackageReference: Add `<PackageReference Include="S1API.Forked" />` to your project file
>    - The NuGet package automatically handles the correct references for both IL2CPP and Mono builds

### 2.3 `S1APILoader` — the plugin, and what load order it actually guarantees

`S1APILoader.dll` is a **`MelonPlugin`** (not a `MelonMod`), installed to `Plugins\`. Plugins run before mods. Its entire job is *build selection*.

Quoted verbatim from `S1APILoader/S1APILoader.cs` **[verified]**:

```csharp
[assembly: MelonInfo(typeof(S1APILoader.S1APILoader), "S1APILoader", "2.5.0", "KaBooMa & Bars")]

namespace S1APILoader
{
    public class S1APILoader : MelonPlugin
    {
        public override void OnApplicationEarlyStart()
        {
            TryApplyEarlyIl2CppInteropFix();
            ...
            string activeBuild = MelonUtils.IsGameIl2Cpp() ? "Il2Cpp" : "Mono";
            string inactiveBuild = !MelonUtils.IsGameIl2Cpp() ? "Il2Cpp" : "Mono";

            MelonLogger.Msg($"Loading S1API for {activeBuild}...");

            // Normalize both builds in Mods folder: keep exactly one file per build
            NormalizeBuild(modsFolder, activeBuild, shouldBeEnabled: true,
                fileNamePattern: "S1API.{0}.MelonLoader.dll");
            NormalizeBuild(modsFolder, inactiveBuild, shouldBeEnabled: false,
                fileNamePattern: "S1API.{0}.MelonLoader.dll");
            ...
        }
```

**What this means concretely** — all **[verified]** from that file:

- It renames `Mods\S1API.Mono.MelonLoader.dll` ⇄ `Mods\S1API.Mono.MelonLoader.dll.disabled` (and the Il2Cpp equivalent) *before MelonLoader scans the Mods folder*, so exactly one S1API build ever loads.
- It does the same in `Plugins\S1API\` with the pattern `S1API.{0}.dll`.
- If two copies of the same build exist (enabled + disabled), it picks the one with the higher **assembly version**, falling back to last-write time (`ChooseNewest` → `TryGetAssemblyVersion` → `GetSafeWriteTimeUtc`).
- On MelonLoader **0.7.1** only, it additionally applies the Il2CppInterop workaround described in §1.7.

**Load-order guarantee.** Plugin (`OnApplicationEarlyStart`) → S1API mod (`MelonPriority(Int32.MinValue)`, so first among mods) → your mod. **[verified]** for the two S1API halves; **[inference]** for "therefore your mod is safe" — which holds as long as you don't also claim `Int32.MinValue`.

### 2.4 The two base classes everything inherits from: `Registerable` and `Saveable`

`S1API.Internal.Abstraction.Registerable` (public, despite the `Internal` namespace) is the root. `Saveable : Registerable, ISaveable` adds persistence. `Quest`, `NPC`, and `PhoneApp` all derive from one of these. **[verified]** — `S1API/Internal/Abstraction/Saveable.cs` line 30: `public abstract class Saveable : Registerable, ISaveable`; `S1API/PhoneApp/PhoneApp.cs` line 39: `public abstract class PhoneApp : Registerable`; `S1API/Quests/Quest.cs` line 46: `public abstract class Quest : Saveable`.

**Auto-discovery is the registration mechanism.** You never call a `Register()`. S1API scans loaded assemblies for subclasses (`ReflectionUtils.GetDerivedClasses<TBase>()` **[verified]** exists) and instantiates them. Requirements, quoted verbatim from `S1API/docs/save-system.md`:

> - Your saveable class must directly inherit `Saveable` (classes that inherit from `Saveable`, like `NPC`, are handled internally by the API).
> - It must be non-abstract and have a parameterless constructor.
> - S1API will create one instance per `Saveable` type and call its lifecycle methods during save/load.

**[verified]**. And for phone apps, from `S1API/docs/phone-app.md`: *"Do not manually register; S1API auto-discovers `PhoneApp` subclasses when the phone `HomeScreen` starts"* and *"Ensure your app type is `public`."* **[verified]**.

⚠️ **`ModSaveableRegistry` is obsolete.** Quoted verbatim from `save-system.md`: *"`ModSaveableRegistry` is obsolete, do NOT use it."* **[verified]**. If you find it in an older tutorial, ignore that tutorial.

### 2.5 NPCs — `S1API.Entities`

This is the biggest subsystem in S1API by a wide margin: `S1API/Entities/NPC.cs` alone is **4,064 lines / 201 KB**, and `S1API/Internal/Patches/NPCPatches.cs` is **128 KB**. **[verified]** — file sizes from the clone.

**The two-phase model.** Quoted verbatim from `S1API/docs/basic-npc-creation.md`:

> The two phases to remember:
>
> - `ConfigurePrefab(...)`: saved defaults (identity, relationships, schedules, customer/dealer defaults)
> - `OnCreated()`: runtime wiring (build avatar, dialogue callbacks, event subscriptions)

**[verified]**.

**Minimal custom NPC**, quoted verbatim from `S1API/docs/custom-npcs.md`:

```csharp
public sealed class MyFirstNPC : NPC
{
    protected override bool IsPhysical => true;
    
    protected override void ConfigurePrefab(NPCPrefabBuilder builder)
    {
        builder.WithIdentity(
                id: "my_first_npc",
                firstName: "John",
                lastName: "Doe")
                .WithSpawnPosition(new Vector3(0, 0, 0))
                .EnsureCustomer()
                .WithCustomerDefaults(cd => {
                    cd.WithSpending(100f, 500f)
                      .WithOrdersPerWeek(1, 3);
                });
    }
    
    public MyFirstNPC() : base()
    {
    }
    
    protected override void OnCreated()
    {
        base.OnCreated();
        
        // Set up appearance
        Appearance
            .Set<CustomizationFields.Gender>(0.5f)
            .Set<CustomizationFields.Height>(1.0f)
            .Build();
        
        // Enable systems
        Schedule.Enable();
        Schedule.InitializeActions();
    }
}
```

**[verified]** — <https://github.com/ifBars/S1API/blob/stable/S1API/docs/custom-npcs.md>. ⚠️ Note a discrepancy between the two docs: `custom-npcs.md` declares `protected override bool IsPhysical`, while `basic-npc-creation.md` declares `public override bool IsPhysical`. The **source** is authoritative: `S1API/Entities/NPC.cs` line 2303 declares `public virtual bool IsPhysical => false;` **[verified]**, so it must be `public override`. `custom-npcs.md` is wrong.

A fuller `ConfigurePrefab`, quoted verbatim from `basic-npc-creation.md` **[verified]**:

```csharp
    protected override void ConfigurePrefab(NPCPrefabBuilder builder)
    {
        var spawnPos = new Vector3(-50f, 1.06f, 70f);
        var hangoutPos = new Vector3(-28f, 1.06f, 62f);

        builder.WithIdentity("my_first_npc", "Alex", "Example")
            .WithSpawnPosition(spawnPos)
            .WithAppearanceDefaults(av =>
            {
                av.Gender = 0.5f;
                av.Height = 1.0f;
                av.Weight = 0.5f;
                av.HairPath = "Avatar/Hair/Spiky/Spiky";
            })
            .WithSchedule(plan =>
            {
                plan.WalkTo(hangoutPos, 900, faceDestinationDir: true);
            });
    }
```

**`NPCPrefabBuilder` — complete public fluent surface**, extracted directly from `S1API/Entities/NPCPrefabBuilder.cs` **[verified]** (every entry below is a real line number in that file):

| Method | Line |
|---|---|
| `NPCPrefabBuilder WithIdentity(string id, string firstName, string lastName)` | 104 |
| `NPCPrefabBuilder WithIcon(Sprite icon)` | 135 |
| `NPCPrefabBuilder WithAppearanceDefaults(Action<AvatarDefaultsBuilder> configure)` | 157 |
| `NPCPrefabBuilder WithSchedule(Action<PrefabScheduleBuilder> configure)` | 267 |
| `NPCPrefabBuilder WithSchedule(IEnumerable<IScheduleActionSpec> specs)` | 289 |
| `NPCPrefabBuilder WithSchedule(params IScheduleActionSpec[] specs)` | 316 |
| `NPCPrefabBuilder WithSpawnPosition(Vector3 position, Quaternion rotation)` | 545 |
| `NPCPrefabBuilder WithSpawnPosition(Vector3 position)` | 556 |
| `NPCPrefabBuilder WithRegion(Region region)` | 594 |
| `NPCPrefabBuilder EnsureCustomer()` / `WithCustomerDefaults(Action<CustomerDataBuilder>)` | 87 / 479 |
| `NPCPrefabBuilder EnsureDealer()` / `WithDealerDefaults(Action<DealerDataBuilder>)` | 335 / 570 |
| `NPCPrefabBuilder EnsureSupplier()` / `WithSupplierDefaults(Action<SupplierDataBuilder>)` | 463 / 616 |
| `NPCPrefabBuilder WithRelationshipDefaults(Action<NPCRelationshipDataBuilder>)` | 517 |
| `NPCPrefabBuilder WithInventoryDefaults(Action<RandomInventoryItemsBuilder>)` | 1039 |
| `NPCPrefabBuilder WithVoice(NPCVoiceDefinition)` / `(…, float pitch)` / `(string)` / `(string, float)` | 383 / 395 / 405 / 417 |
| `NPCPrefabBuilder EnsureSmokeBreak(string? cigarettePrefabPath = null, bool? debugMode = null)` | 638 |
| `NPCPrefabBuilder EnsureGraffiti(string?)` / `(EquippablePath)` | 817 / 821 |
| `NPCPrefabBuilder EnsureDrinking(string?)` / `(EquippablePath)` | 934 / 976 |
| `NPCPrefabBuilder EnsureItemHolding(string?)` / `(EquippablePath)` | 987 / 1028 |
| nested `AvatarDefaultsBuilder`: `WithFaceLayer`, `WithBodyLayer`, `WithAccessoryLayer`, `WithImpostor(string\|AvatarImpostorDefinition)`, `WithImpostorTexture(Texture2D)`, `WithRandomImpostor(…)` | 1347–1450 |

**`NPC` runtime surface** — the composition properties are the useful part. Extracted from `S1API/Entities/NPC.cs` **[verified]**, line numbers real:

```csharp
// Overridables
protected virtual void ConfigurePrefab(NPCPrefabBuilder builder) { }   // 2113
protected virtual void OnResponseLoaded(Response response) { }         // 2120
protected override void OnCreated()                                    // 2129
public virtual bool IsPhysical => false;                               // 2303
public virtual bool IsDealer   => false;                               // 2313
public virtual bool IsSupplier => false;                               // 2322

// Identity / transform
public GameObject gameObject { get; }        // 2156
public Transform Transform => …              // 2171
public string FullName => …                  // 2212

// State
public bool IsConscious / IsInBuilding / IsInVehicle / IsPanicking / IsUnsettled / IsVisible   // 2262–2293
public bool IsKnockedOut / IsDead            // 2663 / 2711
public float CurrentHealth                   // 2696
public LandVehicle? CurrentVehicle           // 2687

// Actions
public void Revive() / Damage(int) / Heal(int) / Kill() / KnockOut()   // 2728–2803
public void Unsettle(float duration) / Panic() / StopPanicking()      // 2767 / 2783 / 2796
public void LerpScale(float scale, float lerpTime)                    // 2775
public void Goto(Vector3 position)                                    // 2810
public void SetEquippable(string assetPath)                           // 3042
public void SetEquippable(Equippables.EquippablePath equippablePath)   // 3048

// Subsystem components (lazily created wrappers)
public NPCAppearance    Appearance      { get; private set; }   // 2892
public NPCMovement      Movement        // 2897
public CombatBehaviour  CombatBehaviour // 2902
public NPCSmoking       Smoking         // 2907
public NPCSprayPainting SprayPainting   // 2912
public NPCDrinking      Drinking        // 2917
public NPCItemHolding   ItemHolding     // 2922
public NPCDialogue      Dialogue        // 2927
public NPCSchedule      Schedule        // 2932
public NPCInventory     Inventory       // 2937
public NPCCustomer      Customer        // 2942
public NPCDealer        Dealer          // 2947
public NPCSupplier      Supplier        // 2952
public NPCRelationship  Relationship    // 2957
public NPCMessaging     Messaging       // 2962

// Messaging + lookup
public void SendTextMessage(string message, Response[]? responses = null,
                            float responseDelay = 1f, bool network = true);   // 2972
public static NPC? Get(string npcId);                                          // 3066
public void RefreshMessagingIcons();                                           // 1396
public static void PreRegisterPrefabForType(System.Type npcType);              // 1467
public static void PreRegisterAllNpcPrefabs();                                 // 1488
```

**`NPCSchedule`** (`S1API/Entities/NPCSchedule.cs`) **[verified]**: `bool IsEnabled`, `bool CurfewModeEnabled`, `void Enable()`, `void Disable()`, `void EnforceState()`, `void SetCurfewMode(bool)`, `string GetActiveActionName()`, `void EnsureDealSignal()`, `void ClearActions(bool includeSignals = true, bool includeEvents = true)`, `IReadOnlyList<string> GetActionNames()`.
⚠️ The `custom-npcs.md` quick-start calls `Schedule.InitializeActions()`. **I could not find an `InitializeActions` method on `NPCSchedule`** in the source I read. Treat that doc line as **unverified / possibly stale**; `basic-npc-creation.md` only calls `Schedule.Enable()`, which *is* verified.

**`NPCRelationship`** (`S1API/Entities/NPCRelationship.cs`) **[verified]**: `enum UnlockType`, `float Delta`, `float Normalized`, `void Add(float delta, bool network = true)`, `bool IsUnlocked`, `UnlockType Type`, `void Unlock(UnlockType type = UnlockType.DirectApproach, bool notify = true)`, `void SetUnlockType(UnlockType)`, `void UnlockConnections()`, `bool IsKnown`, `bool IsMutuallyKnown`, `List<string> ConnectionIDs`, `event Action<float> OnChanged`, `event Action<UnlockType, bool> OnUnlocked`.

**`NPCCustomer`** (`S1API/Entities/NPCCustomer.cs`) **[verified]** — directly relevant to a Special Customers mod: `bool IsCustomer`, `void EnsureCustomer()`, `void Unlock()`, `bool ForceDealOffer()`, `bool OfferContract(ContractInfo info)`, `void RequestProduct(Player? player = null)`, `void SetAwaitingDelivery(bool)`, `void SetupDialog()`, `void RecommendDealer(NPCDealer dealer)`, `event Action OnUnlocked`, `event Action OnDealCompleted`, `event Action<float,int,int,int> OnContractAssigned`.

**Messaging + read receipts**, quoted verbatim from `custom-npcs.md` **[verified]**:

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

With state: `Messaging.IsRead`, `Messaging.HasUnreadMessages`, `Messaging.IsOpen`, `Messaging.OnConversationOpened`. **[verified]**.

**Voices** — `builder.WithVoice(NPCVoiceCatalog.Tyler, pitch: 0.92f)`, identifiers `cold`, `crackhead`, `female-1`, `female-2`, `goblin`, `hippie`, `joel`, `monotone`, `redneck`, `timid`, `tyler`; pitch range 0.1–4.0. **[verified]** — `custom-npcs.md`.

**Named-NPC identifiers.** S1API ships typed identifier classes for **~76 named base-game NPCs** organised by district (`S1API.Entities.NPCs.Northtown` ×16, `.Westville` ×12, `.Docks` ×11, `.Downtown` ×11, `.Suburbia` ×11, `.Uptown` ×10, `.PoliceOfficers` ×9, plus 6 unclassified: `DanSamwell`, `IgorRomanovich`, `MannyOakfield`, `OscarHolland`, `StanCarney`, `UncleNelson`). **[verified]** — namespace census from `S1API.xml` (3.1.4) + file listing from the clone.

**Reference implementations.** Four complete example NPCs at **<https://github.com/ifBars/S1APINPCExample>** (last push 2026-07-18, **no license file** ⚠️): `ExamplePhysicalNPC1` (customer + dialogue + inventory + scheduling), `ExamplePhysicalNPC2` (customer events + dealer recommendation), `ExamplePhysicalDealerNPC` (full dealer), `CharacterCustomizerNPC` (opens the character creator from dialogue). **[verified]** — repo exists per GitHub API; file paths per `custom-npcs.md`. ⚠️ **No license** means you should read it for technique, not copy-paste it.

**AI-agent skill.** The S1API repo ships `skills/schedule-one-custom-npcs/SKILL.md` + two reference files (`s1api-custom-npc-reference.md`, `example-project-patterns.md`), and the docs explicitly recommend feeding it to a coding agent. **[verified]** — <https://github.com/ifBars/S1API/tree/stable/skills/schedule-one-custom-npcs>. Same skill is vendored into `S1APITemplate/.agents/skills/`.

### 2.6 Quests — `S1API.Quests`

`Quest : Saveable`. Quoted verbatim from `S1API/Quests/Quest.cs` **[verified]**:

```csharp
    public abstract class Quest : Saveable
    {
        protected abstract string Title { get; }
        protected abstract string Description { get; }
        protected virtual bool AutoBegin => true;
        protected QuestState QuestState => (QuestState)S1Quest.State;
        public readonly System.Collections.Generic.List<QuestEntry> QuestEntries = new System.Collections.Generic.List<QuestEntry>();

        [SaveableField("QuestData")]
        private readonly QuestData _questData;

        protected virtual Sprite? QuestIcon => null;

        protected QuestEntry AddEntry(string title, Vector3? poiPosition = null);
        protected QuestEntry AddEntry(string title, NPC npc);   // POI follows the NPC

        public event Action OnComplete;
        public event Action? OnFail;

        public void Begin();     public void Cancel();    public void Expire();
        public void Fail();      public void Complete();  public void End();
    }
```

**`QuestManager`** (static) — quoted verbatim from `S1API/Quests/QuestManager.cs` **[verified]**:

```csharp
        public static Quest CreateQuest<T>(string? guid = null) where T : Quest;
        public static Quest CreateQuest(Type questType, string? guid = null);
        public static Quest? GetQuestByGuid(string guid);
        public static Quest? GetQuestByName(string questName);
        public static QuestWrapper? Get<T>() where T : IQuestIdentifier;
```

Note `CreateQuest` is `Activator.CreateInstance(questType)` — so your quest class needs a public parameterless constructor. **[verified]** from the body.

**Full example:**

```csharp
using S1API.Quests;
using UnityEngine;

public class DriverRouteQuest : Quest
{
    protected override string Title       => "Establish a Delivery Route";
    protected override string Description => "Hire a driver and complete one delivery.";
    protected override bool   AutoBegin   => false;

    private QuestEntry _hireEntry;
    private QuestEntry _deliverEntry;

    public DriverRouteQuest()
    {
        _hireEntry    = AddEntry("Hire a driver");
        _deliverEntry = AddEntry("Complete a delivery", new Vector3(-28f, 1.06f, 62f));
    }

    protected override void OnLoaded()
    {
        OnComplete += () => MelonLoader.MelonLogger.Msg("Route established.");
    }
}

// elsewhere, once the game has loaded:
var quest = QuestManager.CreateQuest<DriverRouteQuest>("mymod.driver-route");
quest.Begin();
```

**[inference]** — assembled from verified signatures; I have not run it.

**Base-game quests** get typed identifiers via `S1API.Quests.Identifiers` (24 types, verified from the XML census): `GettingStarted`, `WelcomeToHylandPoint`, `WeNeedToCook`, `MovingUp`, `GearingUp`, `OnTheGrind`, `MakingTheRounds`, `MixingMania`, `KeepingItFresh`, `DodgyDealing`, `MoneyManagement`, `CleanCash`, `NeedingTheGreen`, `Packin`, `Packagers`, `Botanists`, `Chemists`, `Cleaners`, `DealForCartel`, `DefeatCartel`, `UnfavourableAgreements`, **`Warehouse`**, `IQuestIdentifier`, `QuestNameAttribute`. **[verified]**. Use `QuestManager.Get<Warehouse>()` → `QuestWrapper?`.

Quest state is persisted through `[SaveableField("QuestData")]` on the base class, and saves into a per-quest subfolder — quoted verbatim from `Quest.cs`:

```csharp
        internal override void SaveInternal(string folderPath, ref List<string> extraSaveables)
        {
            string questDataPath = Path.Combine(folderPath, S1Quest.SaveFolderName);
            if (!Directory.Exists(questDataPath))
                Directory.CreateDirectory(questDataPath);

            base.SaveInternal(questDataPath, ref extraSaveables);
        }
```
**[verified]**.

There is a dedicated docs page `S1API/docs/quests-complete-example.md` I did not read in full — flagging so you know it exists.

### 2.7 Phone apps — `S1API.PhoneApp` ⭐ strongest candidate for the settings UI

`public abstract class PhoneApp : Registerable`. **[verified]** — `S1API/PhoneApp/PhoneApp.cs` line 39.

**Abstract members you must implement** (verbatim from the source):

```csharp
        protected abstract string AppName { get; }        // unique internal id
        protected abstract string AppTitle { get; }       // display title
        protected abstract string IconLabel { get; }      // text under the icon
        protected abstract string IconFileName { get; }   // e.g. "my_icon.png"
        protected abstract void OnCreatedUI(GameObject container);
```

**Virtual members you may override:**

```csharp
        protected virtual Sprite? IconSprite => null;                     // takes precedence over IconFileName
        protected virtual EOrientation Orientation => EOrientation.Horizontal;
        public enum EOrientation { Horizontal = 0, Vertical = 1 }
        public virtual void Exit(ExitAction exit);
        protected virtual void OnPhoneClosed() { }
        protected override void OnCreated();      // base impl: PhoneAppRegistry.Register(this)
        protected override void OnDestroyed();
```

**Public methods:**

```csharp
        public bool IsOpen();
        public void OpenApp();
        public void CloseApp();
        public bool SetIconSprite(Sprite sprite);
        public bool SetIconTexture(Texture2D texture);
```

All **[verified]** from `PhoneApp.cs`.

**Complete working example**, quoted verbatim from `S1API/docs/phone-app.md` **[verified]**:

```csharp
using UnityEngine;
using UnityEngine.UI;
using S1API.PhoneApp;
using S1API.UI;
using S1API.Utils;

public class HelloWorldApp : PhoneApp
{
    // Define app metadata. These properties are used by S1API to register and display your app.
    protected override string AppName => "HelloWorld";
    protected override string AppTitle => "Hello World";
    protected override string IconLabel => "Hello";
    protected override string IconFileName => "hello_icon.png"; // Icon file in your Mods/Plugins folder

    // OnCreated is called once when the app is initialized.
    protected override void OnCreated()
    {
        base.OnCreated();
        // Any one-time setup or initialization logic for your app.
    }

    // OnCreatedUI is called when the app's UI panel is created and needs content.
    // S1API provides a full-size container configured for the app's Orientation.
    // An internal PhoneAppButtonHandler component is automatically added to the app panel to manage button interactions.
    protected override void OnCreatedUI(GameObject container)
    {
        // Use UIFactory to create and layout UI elements within the provided container.
        var panel = UIFactory.Panel("MainPanel", container.transform, new Color(0.1f, 0.1f, 0.1f), fullAnchor: true);
        UIFactory.Text("Title", "📱 Hello, S1API!", panel.transform, 22, TextAnchor.MiddleCenter);
        
        // Example: Add a button using RoundedButtonWithLabel
        var (maskGO, button, label) = UIFactory.RoundedButtonWithLabel(
            "MyButton", 
            "Click Me", 
            panel.transform, 
            new Color(0.2f, 0.5f, 0.3f), 
            140, 40, 18, 
            Color.white
        );
        
        // Use ButtonUtils.AddListener for IL2CPP/Mono compatibility
        ButtonUtils.AddListener(button, () => 
        {
            Logger.Msg("Button Clicked!");
            // Your button logic here
        });
    }
}
```

**How it wires into the native phone** — worth knowing because it tells you what breaks. From `PhoneApp.SpawnUI`/`SpawnIcon` **[verified]**:

- Finds `AppsCanvas` as a sibling of `HomeScreen`; creates a full-stretch `GameObject` named `AppName` under it.
- Clones the native `HomeScreen.appIconPrefab`, adds the resulting `Button` to `HomeScreen.appIcons` and the `UISelectable` to `HomeScreen.uiPanel.AddSelectable(...)` — this is what preserves keyboard/**gamepad** navigation (relevant post-v0.4.6).
- Icon image is `Mask/Image`, label is `Label` (a `UnityEngine.UI.Text`).
- Icon file is loaded from `MelonEnvironment.ModsDirectory` — `Path.Combine(MelonEnvironment.ModsDirectory, filename)`.
- Subscribes `Phone.Instance.closeApps`, `Phone.Instance.onPhoneClosed`, and registers a `GameInput` exit listener at priority `1`.
- Open/close manipulates `AppsCanvas.Instance.SetIsOpen`, `HomeScreen.Instance.SetIsOpen`, `Phone.Instance.SetIsHorizontal`, `Phone.Instance.SetLookOffsetMultiplier`, and `Phone.ActiveApp`.

**[inference]** For a settings UI, this is a much better fit than IMGUI: it's the game's own canvas, it's controller-navigable, and S1API absorbs the wiring. The catch is §2.9 — `UIFactory` does **not** give you the game's fonts.

### 2.8 Save data — `S1API.Saveables` + `S1API.Internal.Abstraction.Saveable`

**The attribute** (verbatim, `S1API/Saveables/SaveableField.cs` — the whole public surface is 2 members):

```csharp
    public class SaveableField : Attribute
    {
        public SaveableField(string saveName);
    }
```
**[verified]**.

**Mechanism**, from `Saveable.SaveInternal` / `LoadInternal` **[verified]**:

- Reflects **all** instance fields, public and non-public (`BindingFlags.Instance | Public | NonPublic`), including inherited ones via `ReflectionUtils.GetAllFields`.
- File name = `SaveName` + `.json` (appended only if not already present).
- Serialisation is `JsonConvert.SerializeObject(value, Formatting.Indented, ISaveable.SerializerSettings)`.
- **A `null` field deletes its save file** — `if (value == null) File.Delete(saveDataPath);`.
- Non-null fields are added to `extraSaveables`, with this comment verbatim: *"We add this to the extra saveables to prevent the game from deleting it / Otherwise, it'll delete it after it finishes saving and does clean up"*.
- After the loop: `OnSaved()` / `OnLoaded()`.

**Namespacing per save.** S1API is handed a `folderPath` by the game's save pipeline, so files land under the active save slot. The game's save root is `C:\Users\<user>\AppData\LocalLow\TVGS\Schedule I\Saves\<steam_id>` **[verified]** — community wiki `common_terms`. **[inference]** therefore your JSON ends up per-save-slot automatically; I did not read the exact patch that supplies `folderPath`.

**Load order.** Quoted verbatim from `Saveable.cs`:

```csharp
        public virtual SaveableLoadOrder LoadOrder => SaveableLoadOrder.AfterBaseGame;
```

with the doc-comment example, verbatim:

```csharp
        /// public class EarlyConfigSaveable : Saveable
        /// {
        ///     public override SaveableLoadOrder LoadOrder => SaveableLoadOrder.BeforeBaseGame;
        ///     
        ///     [SaveableField("config")]
        ///     private ModConfig _config = new ModConfig();
        ///     
        ///     protected override void OnLoaded()
        ///     {
        ///         // Base game entities are NOT loaded yet
        ///         ApplyGlobalSettings(_config);
        ///     }
        /// }
```
**[verified]**. And: *"All saveables are saved at the same time (after base game save), regardless of load order."* **[verified]**.

**Forcing a save:** `Saveable.RequestGameSave()` → `bool` (static). **[verified]**.

**Dynamic/consolidated format.** `SaveToDynamic(DynamicSaveData)` / `LoadFromDynamic(DynamicSaveData)` write each `SaveableField` as a key in the game's `DynamicSaveData` blob rather than a separate file — used for NPCs. These are `internal`. **[verified]** — `Saveable.cs` lines 204–255; the corresponding game type is `ScheduleOne.Persistence.Datas.DynamicSaveData` **[verified]** in the decompiled tree.

**Full example** (verbatim from `S1API/docs/save-system.md`):

```csharp
using S1API.Internal.Abstraction;
using S1API.Saveables;

public class NotesSave : Saveable
{
    [SaveableField("notes")] private List<Note> _notes = new();

    protected override void OnLoaded()
    {
        // Apply loaded data to runtime managers, UI, etc.
    }

    protected override void OnSaved()
    {
        // Optional: clear transient caches
    }
}
```

**[inference]** For mod *settings* (as opposed to per-save state), `MelonPreferences` is the better home — it lives in `UserData\MelonPreferences.cfg`, survives save deletion, and doesn't need the game loaded. Use `Saveable` for per-save-slot state (which drivers are hired, which customer groups have visited) and `MelonPreferences` for global toggles.

### 2.9 UI helpers — `S1API.UI`

Only **3 public types**: `UIFactory`, `MainMenuRig`, `CharacterCreatorManager`. **[verified]** — XML namespace census (`S1API.UI` = 3 types).

**`UIFactory` — complete public signature list**, extracted from `S1API/UI/UIFactory.cs` **[verified]**:

```csharp
public static GameObject Panel(string name, Transform parent, Color bgColor,
                               Vector2? anchorMin = null, Vector2? anchorMax = null, bool fullAnchor = false);
public static Text       Text(string name, string content, Transform parent, int fontSize = 14,
                               TextAnchor anchor = TextAnchor.UpperLeft, FontStyle style = FontStyle.Normal);
public static RectTransform ScrollableVerticalList(string name, Transform parent, out ScrollRect scrollRect);
public static void       FitContentHeight(RectTransform content);
public static (GameObject, Button, Text) RoundedButtonWithLabel(string name, string label, Transform parent,
                               Color bgColor, float width, float height, int fontSize, Color textColor);
public static GameObject ButtonRow(string name, Transform parent, float spacing = 12f,
                               TextAnchor alignment = TextAnchor.MiddleCenter);
public static (GameObject, Button, Text) ButtonWithLabel(string name, string label, Transform parent,
                               Color bgColor, float Width, float Height);
public static void       SetIcon(Sprite sprite, Transform parent);
public static void       CreateTextBlock(Transform parent, string title, string subtitle, bool isCompleted);
public static void       CreateRowButton(GameObject go, UnityAction clickHandler, bool enabled);
public static void       ClearChildren(Transform parent);
public static void       VerticalLayoutOnGO(GameObject go, int spacing = 10, RectOffset? padding = null);
public static GameObject CreateQuestRow(string name, Transform parent, out GameObject iconPanel, out GameObject textPanel);
public static GameObject TopBar(string name, Transform parent, string title, float topbarSize,
                               int paddingLeft, int paddingRight, int paddingTop, int paddingBottom);
public static void       HorizontalLayoutOnGO(GameObject go, int spacing = 10, int padLeft = 0, int padRight = 0,
                               int padTop = 0, int padBottom = 0, TextAnchor alignment = TextAnchor.MiddleCenter);
public static void       SetLayoutGroupPadding(LayoutGroup layoutGroup, int left, int right, int top, int bottom);
public static void       BindAcceptButton(Button btn, Text label, string text, UnityAction callback);
```

⚠️ **`UIFactory` does NOT expose the game's fonts or sprites.** This matters a lot for the "must not feel third-party" requirement in `CONTEXT.md`. Verbatim from `UIFactory.Text`:

```csharp
            txt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
```

and from `ButtonWithLabel`:

```csharp
            img.sprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");
```

and `RoundedButtonWithLabel` generates its own 32×32 rounded sprite procedurally in `GetRoundedSprite()`. **[verified]** — all three read directly from the file. So every `UIFactory` control renders in **built-in Unity Arial with the default Unity UI skin**, using legacy `UnityEngine.UI.Text`, **not TextMeshPro**. The docs page claims these helpers *"match the game's aesthetic"* (`docs/ui.md`, verbatim: *"a set of common UI primitives to rapidly build elements that match the game's aesthetic"*) — **[inference]** that claim is generous; Arial is not the game's font.

**[inference]** To get native look you'll need to harvest the game's `TMP_FontAsset` and sprites at runtime yourself (e.g. `Resources.FindObjectsOfTypeAll(Il2CppType.Of<TMPro.TMP_FontAsset>())`, or pull the font off an existing native `TextMeshProUGUI` in the phone/main-menu hierarchy) and then post-process what `UIFactory` builds — or skip `UIFactory` and clone a native prefab. `PhoneApp.CreateAppIcon` already demonstrates the clone-a-native-prefab technique (`Object.Instantiate(homeScreenInstance.appIconPrefab, parent)`) **[verified]**, which is the pattern I'd copy.

**`MainMenuRig`** (`S1API/UI/MainMenuRig.cs`) — thin and probably not what you want for a settings screen. Full public surface **[verified]**:

```csharp
    public sealed class MainMenuRig
    {
        public Avatar.Avatar? Avatar { get; }
        public static MainMenuRig[] FindInScene(bool includeInactive = false);
    }
```

That's it — it only exposes the menu **avatar**. **There is no S1API API for adding a screen/tab to the main menu.** **[verified]** by exhaustion: `S1API.UI` contains exactly 3 types and this is the only main-menu one. If `CONTEXT.md`'s "native uGUI/TMP main-menu screen" decision stands, that work is entirely on you against raw `Il2CppScheduleOne.UI.MainMenu.*` / `UIScreen` / `UIScreenManager` / `UITab` / `UIToggle` / `UISlider` (all confirmed to exist in the game — see §3).

**`CharacterCreatorManager`** — `PreRegisterAsActiveUI()` then `Open()`. Quoted verbatim from `docs/ui.md` **[verified]**:

```csharp
using S1API.UI;
// If you open this from dialogue, pre-register before closing dialogue to avoid camera conflicts.
CharacterCreatorManager.PreRegisterAsActiveUI();

// Then close dialogue (if applicable) and open the creator.
// Dialogue.End();
CharacterCreatorManager.Open();
```

**Cross-runtime UI helpers** in `S1API.Utils` (9 public types **[verified]**): `ButtonUtils` (`AddListener(Button, Action)`, `RemoveListener`, `ClearListeners`, `Enable(Button, Text?, string?)`, `Disable(...)`, `SetLabel(Text, string)`, `SetStyle(Button, Text, string, Color)`), `EventHelper` (`AddListener(Action, UnityEvent)`, `AddListener<T>(Action<T>, UnityEvent<T>)`, `RemoveListener` ×2, `AddEventTrigger` ×2, `RemoveEventTrigger`), plus `ToggleUtils`, `ImageUtils`, `ColorUtils`, `TransformUtils`, `ArrayExtensions`, `RandomUtils`, `ReflectionUtils`. **[verified]** — signatures read from source. **Always use `ButtonUtils.AddListener` / `EventHelper.AddListener` instead of raw `onClick.AddListener`** — they hide the `(UnityAction)` cast and the delegate lifetime problem.

### 2.10 Console — `S1API.Console`

Four public types: `ConsoleHelper`, `BaseConsoleCommand`, `ConsoleItemAliases`, and (internal) `CustomConsoleRegistry`. **[verified]**.

**Registering your own command.** Subclass `BaseConsoleCommand` — verbatim from `S1API/Console/BaseConsoleCommand.cs`:

```csharp
    public abstract class BaseConsoleCommand
    {
        public abstract string CommandWord { get; }
        public abstract string CommandDescription { get; }
        public abstract string ExampleUsage { get; }
        public abstract void ExecuteCommand(List<string> args);
    }
```

**[verified]**. Note this deliberately does **not** inherit the game's IL2CPP `ConsoleCommand` — verbatim comment from `CustomConsoleRegistry.cs`: *"Holds managed custom console commands and executes them when routed by patches. Avoids inheriting from Il2Cpp abstract types; safe for both Mono and Il2Cpp."* **[verified]**. Routing happens through `S1API/Internal/Patches/ConsolePatches.cs`; lookup is case-insensitive and the game lowercases tokens before dispatch, and `args` passed to you has the command word already stripped. **[verified]** from `CustomConsoleRegistry.TryExecute*`.

⚠️ `CustomConsoleRegistry.Register` is `internal`. I did **not** find a public registration entry point in the source I read — registration appears to be by auto-discovery of `BaseConsoleCommand` subclasses, consistent with the rest of S1API, but **I could not verify the discovery call site**. Flagging as **unverified**; check `ConsolePatches.cs` before relying on it.

**Calling built-in commands.** `ConsoleHelper` is a flat static wrapper. Complete list **[verified]** from `S1API/Console/ConsoleHelper.cs`:

```csharp
public static void Submit(string command);                      // raw, e.g. "settime 1530"
public static void Submit(IEnumerable<string> arguments);
public static void RunCashCommand(int amount);
public static void RunOnlineBalanceCommand(int amount);
public static void AddItemToInventory(string itemCode, int? quantity = null);
public static void ClearInventory();
public static void ClearTrash();
public static void ClearWanted();
public static void GiveXp(int amount);
public static void GrowPlants();
public static void LowerWanted();
public static void RaiseWanted();
public static void SaveGame();
public static void DiscoverProduct(string productCode);
public static void SetPlayerHealth(float amount);
public static void SetPlayerJumpMultiplier(float multiplier);
public static void SetLawIntensity(float intensity);            // 0–10
public static void SetPlayerMoveSpeedMultiplier(float multiplier);
public static void SetQuality(Quality quality);
public static void SetQuestState(string questName, QuestState state);
public static void SetNpcRelationship(string npcId, float level);   // 0–5
public static void SetNpcRelationship(NPC npc, float level);
public static void UnlockNpc(NPC npc);
public static void SetTime(string hhmm);                        // "1530"
public static void SpawnVehicle(string vehicleCode);
[Obsolete] public static void SetPlayerEnergyLevel(float amount);   // no-op, energy removed from the game
```

Implementation detail worth knowing: these construct the game's own command object (`new ChangeCashCommand()`, `new SetLawIntensity()`, …) and call `.Execute(args)` — they don't go through the console text parser. **[verified]**.

### 2.11 Complete public namespace catalogue (S1API.Forked **3.1.4**, the version you have)

Derived by parsing `lib/netstandard2.1/S1API.xml` from the 3.1.4 `.nupkg`: **3,806 documented members across 760 types**. **[verified]**. Type counts per namespace:

| Namespace | Types | One-line description |
|---|---:|---|
| `S1API` | 1 | Root `MelonMod`. |
| `S1API.AssetBundles` | 3 | `AssetLoader`, `WrappedAssetBundle(Request)` — cross-runtime bundle loading. |
| `S1API.Avatar` | 4 (+3) | `Avatar`, `AvatarSettings`, `BasicAvatarSettings`, `Seat`. |
| `S1API.Building` | 3 | `BuildManager`, `BuildEvents`, `BuildEventArgs` — construction events. |
| `S1API.Cartel` | 5 | `Cartel`, `CartelGoon`, `CartelInfluence`, `CartelStatus`, `GoonManager`. |
| `S1API.Casino` | 2 (+1) | `SlotMachineHelper`, `GamblingSessionMode`. |
| `S1API.Console` | 4 | See §2.10. |
| `S1API.Cutscenes` | 6 | `CutsceneManager`, `CutsceneBuilder`, `CutsceneCamera`, `CutsceneFrame`, `CutsceneHandle`, `CutsceneEndReason`. |
| `S1API.DeadDrops` | 4 | `DeadDropManager`, `DeadDropInstance`, `NativeDeadDrops`, identifiers. |
| `S1API.Deliveries` | 5 | `DeliveryRegistry`, `Delivery`, `DeliveryItem`, `DeliveryReceipt`, `DeliveryStatus`. |
| `S1API.Dialogues` | 5 | `DialogueInjector`, `DialogueInjection`, choice listeners + paging. |
| `S1API.Doors` | 3 | `DoorController`, `DoorAccess`, `DoorSide`. |
| `S1API.Economy` | 6 (+1) | `Contract`, `ContractInfo(Builder)`, `ContractReceipt`, `CustomerStandard`, `DealerType`. |
| `S1API.Entities` | 16 | `NPC` + the 15 subsystem wrappers. |
| `S1API.Entities.*` (all sub-ns) | ~180 | Appearances (36 field types), Schedule (16), Employees (2), NPCs (76 named identifiers), builders. |
| `S1API.GameTime` | 3 | `TimeManager`, `Day`, time helpers. |
| `S1API.Graffiti` | 3 | Graffiti placement. |
| `S1API.Growing` | 7 | Pots/soil/additives/plants. |
| `S1API.Input` | 4 | Input abstraction. |
| `S1API.Items` (+6 sub-ns) | 58 | Item definitions/instances/builders: `Additive`, `Buildable`, `Clothing`, `Ingredient`, `Quality`, `Storable`. |
| `S1API.Law` | 11 | See §2.12. |
| `S1API.Leveling` | 4 | XP / rank. |
| `S1API.Lifecycle` | 1 | `GameLifecycle` — `OnPreLoad`, `OnLoadComplete`. |
| `S1API.Logging` | 1 | `Log`. |
| `S1API.Map` (+3 sub-ns) | 114 | `MapPOIManager`, `MapPOI(Builder)`, `Region`, `Parking`, **77 typed `Buildings`**, 22 `ParkingLots`, 30+ `DeliveryLocations`. |
| `S1API.Messaging` | 1 | `Response`. |
| `S1API.Misc` | 1 | `ModularSwitch`. |
| `S1API.Money` | 3 | `Money`, `CashDefinition`, `CashInstance`. |
| `S1API.PhoneApp` | 3 (+1) | See §2.7. |
| `S1API.PhoneCalls` (+Constants) | 6 | `CallManager`, `PhoneCallDefinition`, `CallStageEntry`, `CallerDefinition`, trigger enums. |
| `S1API.Products` (+2 sub-ns) | 58 | Full custom-product framework (new in 3.1.0). |
| `S1API.Properties` (+Tokens) | 42 | Product effect properties; 36 named `Tokens` (`Euphoric`, `Zombifying`, …). |
| `S1API.Property` | 6 | `PropertyManager`, `PropertyWrapper`, `BusinessManager`, `BusinessWrapper`, `BaseProperty`, `LaunderingOperation`. |
| `S1API.Quests` (+2 sub-ns) | 31 | See §2.6. |
| `S1API.Rendering` | 6 (+2) | `IconFactory`, `MaterialHelper`, `TextureUtils`, `AccessoryFactory`, `AvatarLayerFactory`, `RuntimeResourceRegistry`. |
| `S1API.Saveables` | 3 | `SaveableField`, `SaveableLoadOrder`, `SaveableAutoRegistry`. |
| `S1API.Shops` | 2 | `ShopManager`, `Shop`. |
| `S1API.Stations` | 7 | Chemistry-station recipes. |
| `S1API.Storage` | 4 | `StorageEntity`, `StorageEvents`. |
| `S1API.Storages` | 3 | `StorageManager`, `StorageInstance`, `StorageAccessSettings`. |
| `S1API.Trash` | 1 | `TrashManager`. |
| `S1API.TVApp` | 2 | Custom TV apps. |
| `S1API.UI` | 3 | See §2.9. |
| `S1API.Utils` | 9 | See §2.9. |
| `S1API.Vehicles` | 4 | `VehicleRegistry`, `LandVehicle`, `VehicleColor`, `ParkingAlignment`. |
| `S1API.Internal.*` | ~250 | Implementation. `Internal.Utils`, `Internal.Patches` (37 patch classes) etc. **Mostly `internal` despite the public namespace — do not depend on it.** |

### 2.12 Task-oriented API map for *this* project's three mods

**Hiring / controlling employees → ⛔ NOT COVERED.** This is the biggest gap I found. The entire `S1API.Entities.Employees` namespace is **two types**, and they only do appearance lookup. Full source of the manager, quoted verbatim from `S1API/Entities/Employees/EmployeeManager.cs` **[verified]**:

```csharp
    public static class EmployeeManager
    {
        public static EmployeeAppearance? GetAppearance(bool male, int index);
        public static bool GetRandomAppearance(bool male, out int index, out AvatarSettings? settings);
    }

    public class EmployeeAppearance
    {
        public AvatarSettings Settings { get; }
        public Sprite Mugshot { get; }
    }
```

I confirmed by exhaustion: grepping the 3.1.4 XML docs for any type name containing `Employee`/`Botanist`/`Chemist`/`Handler`/`Cleaner`/`Packager` returns only `Entities.Employees.EmployeeAppearance`, `Entities.Employees.EmployeeManager`, four `Quests.Identifiers.*` classes, `ChemistryStation*`, and two unrelated `*Handler` types. **[verified]**. **There is no S1API API to hire, fire, assign, pay, or configure an employee.** Hireable Drivers must go direct to `Il2CppScheduleOne.Employees.*` — see §3 for the exact surface.

**Moving items between storage entities → ✅ covered.** `S1API.Storages.StorageManager` **[verified]**: `StorageInstance[] GetAll()`, `StorageInstance? FindByName(string)`, `StorageInstance[] FindByPredicate(Func<StorageInstance,bool>)`. `StorageInstance` **[verified]**: `FromGameObject`, `FromGameObjectInChildren`, `Name`, `Subtitle`, `SlotCount`, `ItemCount`, `AccessSettings`, `IsOpened`, `ItemSlotInstance[] Slots`, `ItemInstance[] GetItems()`, `Dictionary<ItemInstance,int> GetContentsDictionary()`, `bool CanItemFit(ItemInstance, int)`, `void AddItem(ItemInstance)`, `int RemoveItem(ItemInstance)`, `int TryRemoveQuantity(string itemDefinitionId, int quantity)`, `int RemoveAllOfDefinition(string)`, `event Action OnOpened/OnClosed/OnContentsChanged`. Separately `S1API.Storage.StorageEntity` **[verified]** handles capacity: `AddSlots(int)`, `RemoveSlots(int)`, `SetSlotCount(int)`, `MaxSlots` (default 20), `GetEmptySlotCount()`, `GetOccupiedSlotCount()`.

**Spawning vehicles → ✅ covered.** `S1API.Vehicles.VehicleRegistry` **[verified]**: `LandVehicle[] GetAll()`, `GetByGUID(string)`, `GetByName(string)`, `LandVehicle? CreateVehicle(string vehicleCode)`, `RemoveVehicle(string guidString)`. `LandVehicle` **[verified]**: `ctor(string vehicleCode)`, `Spawn(Vector3, Quaternion)`, `AlignTo(Transform, ParkingAlignment, bool network)`, `Park(Map.ParkingData, bool)`, `ExitPark(bool)`, `SetVisible(bool)`, `DestroyVehichle()` *(sic — typo in the API)*, `ApplyColor(VehicleColor)`, `VehiclePrice`, `TopSpeed`, `IsPlayerOwned`, `IsOccupied`, `GUID`, `StorageInstance? Storage`, `event OnVehicleStart/OnVehicleStop/OnHandbrakeApplied/OnCollision`. Also `ConsoleHelper.SpawnVehicle(string)`.

**Spawning NPCs → ✅ covered.** See §2.5.

**Triggering police / wanted state → ✅ well covered.** `S1API.Law.LawManager`, full public surface **[verified]** from source:

```csharp
    public static class LawManager
    {
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
    }
```

Escalation ladder, verbatim from the doc comments: `None → Investigating → Arresting → NonLethal → Lethal`. Plus `S1API.Law` also has `CheckpointManager`, `CheckpointInfo`, `CurfewManager`, `LawController`, `PlayerCrimeData`, `PatrolGroup`, `FootPatrolRoute`, `VehiclePatrolRoute`, `PursuitLevel` (11 types total **[verified]**). And `ConsoleHelper.SetLawIntensity(float)` (0–10). **This is more than enough for a Police Improvements mod.**

**Creating customers / orders → ✅ mostly covered.** `NPCCustomer.EnsureCustomer()`, `OfferContract(ContractInfo)`, `ForceDealOffer()`, `RequestProduct(Player?)`, `SetAwaitingDelivery(bool)`, `RecommendDealer(NPCDealer)` + events. Plus `S1API.Economy.ContractInfoBuilder`, `Contract` (`Payment`, `WindowStartTime`, `WindowEndTime`, `TotalQuantity`, `GetOrders()` → `IEnumerable<(string productId, int quantity, Quality minQuality)>`), `CustomerStandard`, `DealerType`, and `S1API.Entities.Customer.CustomerDataBuilder`. All **[verified]**.

**Time / money / player** — `S1API.GameTime.TimeManager` **[verified]**: static events `OnHourPass`, `OnDayPass`, `OnWeekPass`, `OnSleepStart`, `OnSleepEnd(int)`, `OnTick`; properties `CurrentDay`, `ElapsedDays`, `CurrentTime`, `IsNight`, `IsEndOfDay`, `SleepInProgress`, `NormalizedTime`, `Playtime`; methods `SetTime(int time24h)`, `GetFormatted12HourTime()`, `IsCurrentTimeWithinRange(int,int)`, `GetMinutesFrom24HourTime(int)`, `Get24HourTimeFromMinutes(int)`. `S1API.Money.Money` **[verified]**: `event OnBalanceChanged`, `ChangeCashBalance(float, bool visualizeChange, bool playCashSound)`, `CreateOnlineTransaction(...)`, `GetNetWorth()`, `GetCashBalance()`, `GetOnlineBalance()`, `AddNetworthCalculation(Action<object>)`, `RemoveNetworthCalculation(...)`, `CreateCashInstance(float)`. `S1API.Entities.Player` **[verified]**: `static Player Local`, `static List<Player> All`, `static event PlayerSpawned/LocalPlayerSpawned/PlayerDespawned`, `Position`, `Transform`, `Scale`, `PlayerCrimeData CrimeData`, `CurrentHealth`/`MaxHealth`/`IsDead`/`IsInvincible`, `LandVehicle? LastDrivenVehicle`, `IsInVehicle`, `Crouched`, `IsSkating`, `IsSleeping`, `IsRagdolled`, `IsArrested`, `IsTased`, `IsUnconscious`, `PropertyWrapper? CurrentProperty/LastVisitedProperty`, `Region CurrentRegion`, clothing methods, `Revive/Damage/Heal/Kill`, `event OnDeath/OnRevive`.

### 2.13 Compatibility story, and the gaps

**How it insulates you.** Every S1API type is a thin wrapper holding an `internal` reference to the native object (`internal readonly S1Quests.Quest S1Quest;`, `internal S1Employees.EmployeeManager.EmployeeAppearance S1EmployeeAppearance;`). The `#if IL2CPPMELON`/`#elif MONOMELON` aliasing lives entirely inside S1API, so your mod compiles against one managed surface. When a game patch moves a type, S1API changes one alias and republishes; you rebuild. **[verified]** pattern; **[inference]** the conclusion.

**Multi-targeting.** Yes — `<Configurations>MonoMelon;Il2CppMelon</Configurations>`, `netstandard2.1` for Mono and `net6.0` for IL2CPP, deployed as `S1API.Mono.MelonLoader.dll` / `S1API.Il2Cpp.MelonLoader.dll`. **[verified]** — `S1API/S1API.csproj`.

**Validation.** The 3.1.7 notes claim *"Validated the merged release with 434 Mono contract tests, focused IL2CPP contract coverage, and successful Mono/IL2CPP builds"*, and there's an `S1API.Tests` project (63 files) plus an `il2cpp-build-check.yml` CI workflow. **[verified]**. Note the asymmetry: **Mono has 434 tests, IL2CPP has "focused coverage"** — so IL2CPP is the less-tested path, which is the one you're on.

**Known gaps, ranked by impact on this project:**

1. ⛔ **No employee hiring/management API.** (§2.12) Blocks Hireable Drivers from being pure-S1API.
2. ⛔ **No main-menu screen API.** `MainMenuRig` exposes only the avatar. Blocks the "native main-menu settings screen" plan from being pure-S1API.
3. ⚠️ **`UIFactory` uses Arial + built-in Unity sprites**, not the game's TMP fonts. Anything built with it looks like a mod.
4. ⚠️ **32% class coverage.** Two-thirds of the game is unwrapped.
5. ⚠️ **`S1API.Internal.*` is public-namespaced but mostly `internal`** — `ReflectionUtils`, `CrossType`, `NPCTypeUtils` are all tempting and all inaccessible.
6. ⚠️ **Upstream `KaBooMa/S1API` has no LICENSE file** despite the NuGet package declaring MIT. The fork *does* have MIT.
7. ℹ️ Self-declared limits, quoted verbatim from the README: *"S1API will NOT be the be-all and end-all. It's just not possible."* / *"Cover all modding needs. It will never be as flexible as writing your own mod referencing game assemblies."* **[verified]**.

**License summary for anything you'd copy:**

| Source | License | Safe to copy code from? |
|---|---|---|
| `ifBars/S1API` | **MIT** | ✅ Yes, with attribution |
| `ifBars/S1APITemplate` | ⚠️ No LICENSE file (GitHub API) | ❌ Read for technique only |
| `ifBars/S1APINPCExample` | ⚠️ No LICENSE file | ❌ Read for technique only |
| `ifBars/S1Interop` | **GPL-3.0** | ⚠️ Copying makes your mod GPL |
| `KaBooMa/S1API` (upstream) | ⚠️ No LICENSE file; nuspec says MIT | ❌ Ambiguous — prefer the fork |
| `s1modding/s1modding.github.io` | **MIT** | ✅ Yes |
| `k073l/*` mods (modsapp, employeetweaks, BusinessEmployment, S1MelonModTemplate) | **MIT** | ✅ Yes |
| `k073l/s1-codearchiver` | ⚠️ No LICENSE; and the code it hosts is **TVGS's** | ❌ Reference only, never redistribute |
| `Skippeh/ScheduleOne_UnityProject` | ⚠️ No LICENSE | ❌ Reference only |

All **[verified]** via the GitHub API `license` field.

---

## 3. Community IL2CPP dumps / API references / decompiled source

### 3.1 The game's root namespace — confirmed

**Yes, it is `ScheduleOne.*`** on Mono, and **`Il2CppScheduleOne.*`** in the MelonLoader-generated interop assemblies. **[verified]** three independent ways: (a) the decompiled archive's files all declare `namespace ScheduleOne.<Sub>;`, (b) S1API's `#if IL2CPPMELON` aliases map `Il2CppScheduleOne.X` ⇄ `ScheduleOne.X`, (c) the community wiki states the `Il2Cpp` prefix rule explicitly.

Verified top-level sub-namespaces (from the v0.4.6f11 tree, 2,026 `.cs` files): `Audio`, `AvatarFramework`, `Building`, `Calling`, `Cartel`, `Casino`, `Clothing`, `Combat`, `Configuration`, `Core`, `CustomUI`, `Cutscenes`, `Decoration`, `Delivery`, `DevUtilities`, `Development`, `Dialogue`, `Doors`, `Dragging`, `Economy`, `Effects`, **`Employees`**, `EntityFramework`, `Equipping`, `Events`, `Experimental`, `FX`, `Gamepad`, `GamepadInput`, `GamePhysics`, `GameTime`, `Graffiti`, `Growing`, `Heatmap`, `Input`, `Instancing`, `Interaction`, `ItemFramework`, **`Law`**, `Levelling`, `Lighting`, **`Management`**, `Map`, `Materials`, `Math`, `Messaging`, `Misc`, `Money`, **`NPCs`**, `Networking`, `Noise`, `ObjectScripts`, `Packaging`, `Persistence`, `Platform`, `PlayerScripts`, `PlayerTasks`, **`Police`**, `Polling`, `Product`, `Property`, `Quests`, `Reporting`, `ScriptableObjects`, `Skating`, `State`, `StationFramework`, `Storage`, `TV`, `Temperature`, `Tiles`, `Tools`, `Trash`, `UI`, `Variables`, `Vehicles`, `Vision`, `VoiceOver`, `Weather`. **[verified]**.

Note `Levelling` is **double-L** (British spelling) while S1API's wrapper namespace is `S1API.Leveling` (single L). Easy typo. **[verified]**.

### 3.2 Sources, ranked

#### ⭐ 1. `k073l/s1-codearchiver` — the best cross-reference, and it's current

- **URL:** <https://github.com/k073l/s1-codearchiver>
- **What it is:** a Python automation that watches Steam branches, runs DepotDownloader + a decompiler, and commits **method-stripped** C# to per-branch git branches.
- **Contents:** branch `alternate` → `ScheduleOne-stripped/` with **2,026 `.cs` files** covering the whole of `Assembly-CSharp`. Branch `alternate-beta` tracks the beta channel. Branch `main` holds only the tooling.
- **Version it reflects:** latest commit on `alternate` is **`3973cfc7`, 2026-08-01, "Auto-update for version 0.4.6f11 Alternate"** — i.e. **the current live game build**. History goes back through 0.4.5f2, 0.4.5f1, 0.4.4f10, 0.4.3f3, 0.4.2f9, 0.4.1f13, 0.4.1f12…
- **How to use it:** raw URLs like `https://raw.githubusercontent.com/k073l/s1-codearchiver/alternate/ScheduleOne-stripped/Law/LawManager.cs`. **Diff two commits to see exactly what a patch changed** — that's the whole point of the repo.
- **Trustworthiness:** high for *signatures*, zero for *bodies* — method bodies are stripped, you get declarations only. Author `k073l` is a prolific, active Schedule I mod author (20+ Schedule I repos). Its own README, quoted verbatim: *"ScheduleOne-stripped contains code stubs from Schedule I, game by TVGS. These stubs are provided for archival and modding purposes only. All rights to the original code belong to TVGS."*
- **Caveats:** it decompiles the **Mono** (`alternate`) branch. Mono signatures are strong evidence for IL2CPP but not proof — per the template's `decompilation-workflow.md`, verbatim: *"Mono and IL2CPP are separate evidence streams. Mono source helps with intent, but it does not prove IL2CPP wrapper names, casts, generated methods, or Harmony patchability."* **[verified]**. No LICENSE file. 8★.
- **Status:** **[verified]** — I read the tree, the branch list, the commit log, and several individual files through the GitHub API.

#### 2. `Skippeh/ScheduleOne_UnityProject` — for prefabs/scenes/assets

- **URL:** <https://github.com/Skippeh/ScheduleOne_UnityProject>
- **What it is:** a Unity project with stripped Schedule I scripts + `.meta` files, so ScriptableObjects and prefabs resolve correctly in the editor.
- **Unity version required: 2022.3.62f2** — **exactly matches your install** (verified from `Schedule I.exe`). That's a strong signal it's current.
- **Version/date:** last push **2026-04-01**; default branch `alternate`; 12★, 3 forks. Branch must match your Steam branch.
- **Use:** authoring or inspecting prefabs, UI hierarchies, and ScriptableObjects — i.e. exactly what you'd need to build a *native-looking* settings screen. README says the stripped scripts *"still work fine at runtime if loaded with a mod loader like BepInEx or MelonLoader."*
- **Caveats:** **Mono branches only** (`alternate`/`alternate-beta`), explicitly *"only tested and verified to work with the mono branches"*. No LICENSE. You must supply the game DLLs yourself via `DropManagedFolderHere.bat`.
- **Status:** **[verified]** repo metadata via GitHub API; README content **[community]** (via search result, not fetched directly).

#### 3. `s1modding.github.io` — the community wiki, and it's actively maintained

- **URL:** <https://s1modding.github.io/> · source <https://github.com/s1modding/s1modding.github.io>
- **Last push: 2026-08-03** (today). **MIT licensed.** Hugo site; source markdown under `content/docs/`.
- **Pages I read in full [verified]:** `moddevs/il2cpp.md`, `moddevs/patching.md`, `moddevs/environment_setup.md`, `moddevs/melonloader_utilities.md`. Also present: `creating_your_first_mod.md`, `reading_game_code.md`, `ripping_the_project.md`, `publishing.md`, `resources.md`, `modusers/common_terms/`, `modusers/troubleshooting/`, `modusers/install_melonloader.md`, `modusers/installing_mods.md`.
- ⚠️ Note the page front-matter dates are all `2025-06-2x` even though the repo was pushed today, so individual pages may not have been revised for 0.4.6.
- Discord: <https://discord.gg/UD4K4chKak> **[verified]** — linked from `environment_setup.md`. I did not join or read it.

#### 4. `ifBars/S1APITemplate` `.agents/skills/` — condensed, high-signal reference

- **URL:** <https://github.com/ifBars/S1APITemplate> (last commit **2026-08-02**)
- Ships two vendored agent skills: `schedule-one-modding` (13 reference files: `il2cpp-modding.md`, `build-config.md`, `decompilation-workflow.md`, `community-wiki.md`, `local-game-introspection.md`, `npc-creation.md`, `s1api-reference.md`, `ui-patterns.md`, `vanilla-modding.md`, `mapi-reference.md`, `assetripper-workflow.md`, `dedicated-server-reference.md`, `steamnetworklib-reference.md`, plus `scripts/Invoke-S1LocalProbe.ps1`) and `schedule-one-custom-npcs`.
- These are terse, current, and correct on everything I could cross-check.
- ⚠️ **No LICENSE file.**
- **Status:** **[verified]** — cloned and read `il2cpp-modding.md`, `build-config.md`, `decompilation-workflow.md`, `community-wiki.md`, `local-game-introspection.md`, plus the csproj.

#### 5. `ifBars/S1Interop` — dual-runtime toolchain (alpha)

- **URL:** <https://github.com/ifBars/S1Interop> · docs <https://ifbars.github.io/S1Interop/>
- Created 2026-06-30, last push 2026-07-31, **GPL-3.0**, 2★. CLI: `s1interop new`, `sdkgen`, `migrate --dual-runtime`, `analyze`, `lint`, `verify-migration`. Generates backend-neutral `S1Interop.ScheduleOne.*` facades and reports unsafe IL2CPP boundary cases at build time.
- **[inference]** The `analyze`/`lint` diagnostics could be genuinely useful even if you don't adopt the SDK — but it's alpha, 2★, and GPL-3.0, so I would not take a dependency on it.
- **Status:** **[verified]** repo metadata; **[community]** for the feature list (README via search).

#### 6. Generic IL2CPP tooling

- **`Perfare/Il2CppDumper`** — <https://github.com/perfare/Il2CppDumper>. Extracts `global-metadata.dat` + the il2cpp binary into `DummyDll` stubs, `dump.cs`, and IDA/Ghidra scripts. **Supports Unity 5.3 – 2022.2.** ⚠️ Schedule I is **2022.3**, which is *outside* the stated support range — you may need `ForceIl2CppVersion`. **[verified]** from the tool's README. **[inference]** you almost certainly don't need this: MelonLoader's own `Il2CppAssemblyGenerator` (Cpp2IL + Il2CppInterop) already produces better assemblies in `MelonLoader\Il2CppAssemblies`.
- **`AssetRipper`** — <https://github.com/AssetRipper/AssetRipper/releases>. For a full Unity project export. Workflow documented verbatim in `s1modding.github.io/content/docs/moddevs/ripping_the_project.md`. **[verified]**.
- **`ilspycmd`** — `dotnet tool install --global ilspycmd`, then decompile one type at a time. This is what the S1API template recommends. **[verified]**.
- **`GrahamKracker/UnityExplorer`** (IL2CPP build) — runtime scene-hierarchy inspector + C# REPL. **[community]** — recommended by the FearAndDelight wiki; I did not verify the release link.

#### 7. Reference mods worth reading (all MIT, all current)

These are by `k073l` and are *directly* aligned with your three roadmap mods. All **[verified]** via GitHub API for license + last-push:

| Repo | License | Last push | Why it matters here |
|---|---|---|---|
| [`k073l/s1-employeetweaks`](https://github.com/k073l/s1-employeetweaks) | MIT | 2026-08-02 | Employee-related tweaks — **the closest existing prior art for Hireable Drivers** |
| [`k073l/BusinessEmployment`](https://github.com/k073l/BusinessEmployment) | MIT | 2026-08-01 | Adds the ability to employ in Businesses — proves employee creation is achievable from a mod |
| [`k073l/s1-modsapp`](https://github.com/k073l/s1-modsapp) | MIT | 2026-08-02 | **A phone app for managing mods** — the closest prior art for your settings UI |
| [`k073l/s1-dynamicnpcs`](https://github.com/k073l/s1-dynamicnpcs) | (none) | 2026-01-25 | Data-driven NPCs built on S1API |
| [`k073l/s1-multidelivery`](https://github.com/k073l/s1-multidelivery) | MIT | 2026-08-02 | Multiple deliveries from one store — delivery-system patching |
| [`k073l/S1MelonModTemplate`](https://github.com/k073l/S1MelonModTemplate) | MIT | 2026-04-11 | Cross-backend template; recommended by the community wiki |
| [`k073l/RefGen`](https://github.com/k073l/RefGen) | MIT | 2026-03-31 | Automated Steam tracker + reference-assembly generator |
| [`rfvgyhn/schedule-one-mods`](https://github.com/rfvgyhn/schedule-one-mods) | — | — | Documents the minimal IL2CPP reference set: `Assembly-CSharp`, `Il2Cppmscorlib`, `UnityEngine.CoreModule`, `UnityEngine.TextRenderingModule`, `UnityEngine.UI` **[community]** |

**[inference]** I did not read the source of these mods (out of scope for this pass), but `s1-employeetweaks`, `BusinessEmployment` and `s1-modsapp` are the three highest-value reads for the next research pass, in that order.

#### ❌ Dead / unavailable — reported honestly

- **`thecatontheceiling/scheduleone`** — **404, repository is gone.** DeepWiki still hosts a snapshot at <https://deepwiki.com/thecatontheceiling/scheduleone> describing a decompilation of **v0.3.5**, but I confirmed the GitHub repo no longer exists: the API returns 404 *and* I enumerated all 24 of that user's public repos and `scheduleone` is not among them (so it wasn't renamed — it was deleted or made private). Even if you find a mirror, **v0.3.5 is ~5 minor versions stale**. Use `s1-codearchiver` instead. **[verified]**
- **`FearAndDelight/Schedule-1-Modder-Documentation`** — **404 on the GitHub API.** Content survives only via the `github-wiki-see.page` mirror at <https://github-wiki-see.page/m/FearAndDelight/Schedule-1-Modder-Documentation/wiki>. It's a short, useful beginner page (the "switch to the Alternate beta to get Mono, then decompile with dnSpy" trick, and the UnityExplorer recommendation) but it is unmaintained and not authoritative. **[verified]** the 404; **[community]** the content.

### 3.3 Manager singletons and key types — verified against v0.4.6f11

**The singleton pattern.** Three base classes in `ScheduleOne.DevUtilities`, all with identical `Instance` / `InstanceExists` shape. Quoted verbatim from the decompiled `DevUtilities/Singleton.cs`:

```csharp
namespace ScheduleOne.DevUtilities;
public abstract class Singleton<T> : MonoBehaviour where T : Singleton<T>
{
    private static T instance;
    protected bool Destroyed;
    public static bool InstanceExists => (Object)(object)instance != (Object)null;
    public static T Instance { get; protected set; }
    ...
}
```

and `NetworkSingleton.cs`:

```csharp
namespace ScheduleOne.DevUtilities;
public abstract class NetworkSingleton<T> : NetworkBehaviour where T : NetworkSingleton<T>
{
    public static bool InstanceExists => (Object)(object)instance != (Object)null;
    public static T Instance { get; protected set; }
    ...
}
```

plus `PlayerSingleton<T> : MonoBehaviour` (adds `public virtual void OnStartClient(bool IsOwner);`) and `PersistentSingleton<T>`. All **[verified]** — read from the archive.

**⚠️ `NetworkSingleton<T>` derives from FishNet's `NetworkBehaviour`.** That means those managers are **network objects**, and touching them before the network session is up will fail. This is the "Scene/network object accessed too early" item on the failure checklist. **[verified]** for the inheritance; **[inference]** for the consequence.

**Complete manager census** (files matching `*Manager|*Registry|*Singleton|*Controller` in the v0.4.6f11 tree) — **[verified]**. The ones that matter for this project:

| Type | Namespace | Base | Notes |
|---|---|---|---|
| **`NPCManager`** | `ScheduleOne.NPCs` | `NetworkSingleton<NPCManager>`, `IBaseSaveable`, `ISaveable` | `public static List<NPC> NPCRegistry;` · `public static NPC GetNPC(string id);` · `public static List<NPC> GetNPCsInRegion(EMapRegion region);` · `NPCWarpPoints`, `NPCContainer`, `NPCPoIPrefab`, `PotentialCustomerPoIPrefab`, `PotentialDealerPoIPrefab` · `SaveFolderName => "NPCs"` |
| **`TimeManager`** | `ScheduleOne.GameTime` | — | |
| **`LawManager`** | `ScheduleOne.Law` | **`Singleton<LawManager>`** | `const int OfficerDispatchMin/Max` · `static float DISPATCH_VEHICLE_USE_THRESHOLD` · `void PoliceCalled(Player target, Crime crime)` · `PatrolGroup StartFootpatrol(FootPatrolRoute, int)` · `PoliceOfficer StartVehiclePatrol(VehiclePatrolRoute)` |
| **`EmployeeManager`** | `ScheduleOne.Employees` | — | |
| **`MoneyManager`** | `ScheduleOne.Money` | — | |
| **`PlayerManager`** | `ScheduleOne.PlayerScripts` | — | |
| **`QuestManager`** | `ScheduleOne.Quests` | — | |
| **`PropertyManager`** / **`BusinessManager`** | `ScheduleOne.Property` | — | |
| **`StorageManager`** | `ScheduleOne.Storage` | — | |
| **`VehicleManager`** | `ScheduleOne.Vehicles` | — | |
| **`DeliveryManager`** | `ScheduleOne.Delivery` | — | |
| **`MessagingManager`** | `ScheduleOne.Messaging` | — | |
| **`SaveManager`** / **`LoadManager`** / **`GenericSaveablesManager`** | `ScheduleOne.Persistence` | — | `LoadManager.Instance.IsGameLoaded`, `SaveManager.Instance.Save()` **[verified]** via S1API usage |
| **`GameManager`** / **`MetadataManager`** | `ScheduleOne.DevUtilities` | — | |
| **`LevelManager`** | `ScheduleOne.Levelling` | — | (double-L) |
| **`ShopManager`** | `ScheduleOne.UI.Shop` | — | note: under `UI`, not a top-level namespace |
| **`CheckpointManager`** / **`CurfewManager`** / **`LawController`** | `ScheduleOne.Law` | — | |
| **`NPCScheduleManager`** / **`NPCSpeedController`** | `ScheduleOne.NPCs` | — | |
| Also present | | | `AchievementManager`, `AudioManager`, `MusicManager`, `SFXManager`, `BuildManager`, `CallManager`, `CartelDealManager`, `CombatManager`, `CustomizationManager`, `CutsceneManager`, `IntroManager`, `DialogueManager`, `DragManager`, `EnvironmentManager`, `FXManager`, `PostProcessingManager`, `GraffitiManager`, `HeatmapManager`, `InstancingManager`, `InteractionManager`, `PhysicsManager`, `PollManager`, `ProductManager`, `ProductIconManager`, `ReportManager`, `SewerManager`, `TaskManager`, `TrashManager`, `CompassManager`, `InputPromptsManager`, `ItemUIManager`, `NotificationsManager`, `TooltipManager`, `UIScreenManager`, `CursorManager`, `HapticsManager`, `OcclusionManager`, `EquippableDataRegistry`, `Registry` |

**⚠️ Types the brief asked about that DO NOT EXIST:**

- **`CustomerManager`** — there is no such type. Customers are `ScheduleOne.Economy.Customer` components attached to NPCs; you enumerate them via `NPCManager.NPCRegistry`. **[verified]** by exhaustive tree listing.
- **`PoliceManager`** — there is no such type. Police live in `ScheduleOne.Police` (`PoliceOfficer`, `Investigation`, `Offense`, `RoadCheckpoint`, `NPCResponses_Police`, `NPCResponses_CartelGoon`) and are orchestrated by `ScheduleOne.Law.LawManager`. `PoliceOfficer.Officers` is a static list (S1API reads `S1Police.PoliceOfficer.Officers.Count` **[verified]**).

### 3.4 Findings that directly constrain the three roadmap mods

**Employees (Hireable Drivers).** Quoted verbatim from `ScheduleOne/Employees/EEmployeeType.cs`:

```csharp
namespace ScheduleOne.Employees;
public enum EEmployeeType
{
    Botanist,
    Handler,
    Chemist,
    Cleaner
}
```

**[verified]**. **There are exactly four employee types and none of them is a driver.** Note also the enum value is `Handler` while the class file is `Packager.cs` — the class/enum naming diverges, which will bite you if you match on strings.

Key members of `ScheduleOne.Employees.Employee : NPC` **[verified]** from the archive:

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
    public bool PaidForToday  { get; private set; }
    public bool Fired         { get; private set; }
    public bool IsWaitingOutside => WaitOutside.Active;
    public bool IsMale        { get; private set; }
    public EEmployeeType EmployeeType => Type;
    public float CurrentWorkSpeed => WorkSpeedController.Value;
    public int TicksSinceLastWork { get; private set; }

    [ObserversRpc(RunLocally = true)][TargetRpc]
    public virtual void Initialize(NetworkConnection conn, string firstName, string lastName,
                                   string id, string guid, string propertyID, bool male, int appearanceIndex);
    protected virtual void AssignProperty(ScheduleOne.Property.Property prop, bool warp);
    protected virtual void UnassignProperty();
    [ServerRpc(RequireOwnership = false)] public void SendTransfer(string propertyCode);
    [ServerRpc(RequireOwnership = false)] public void SendFire();
    protected virtual void Fire();
    protected bool CanWork();
    public void SetIsPaid();
    public bool IsPayAvailable();
    public void RemoveDailyWage();
    public virtual EmployeeHome GetHome();
    public virtual bool GetWorkIssue(out DialogueContainer notWorkingReason);
    public virtual void SetIdle(bool idle);
    protected void SetDestination(ITransitEntity transitEntity, bool teleportIfFail = true);
    protected void SetDestination(Vector3 position, bool teleportIfFail = true);
    public override NPCData GetNPCData();
}
```

**[inference]** — `Employee : NPC` with a `MoveItemBehaviour` and `SetDestination(ITransitEntity)` is a *very* good foundation for a driver. But note: `Initialize` is `[ObserversRpc]`/`[TargetRpc]`, `SendFire`/`SendTransfer` are `[ServerRpc]`, and `PaidForToday` is a FishNet `SyncVar`. **Any driver mod is a multiplayer-networked mod whether you want it to be or not.** The RPC wrappers (`RpcWriter___Observers_Initialize_2260823878`) carry version-unstable numeric suffixes — match by prefix.

**Item routing (also Hireable Drivers).** `ScheduleOne.Management` already contains the infrastructure: `ITransitEntity`, `TransitRoute`, `AdvancedTransitRoute`, `RouteListField`, `ObjectField`, `NPCField`, `ItemField`, `ManagementItemFilter`, `EntityConfiguration`, `IConfigurable`, `ConfigurationReplicator`, plus per-employee `BotanistConfiguration`/`ChemistConfiguration`/`CleanerConfiguration`/`PackagerConfiguration` and a `Management/UI/ConfigPanel`. **[verified]** file listing. **[inference]** Modelling a driver as an `EntityConfiguration` with a `RouteListField` would make it feel native and reuse the existing management UI.

**Police (Police Improvements).** `ScheduleOne.Law` has 30 files including 14 concrete `Crime` subclasses (`Assault`, `AttemptingToSell`, `BrandishingWeapon`, `DeadlyAssault`, `DischargeFirearm`, `DrugTrafficking`, `Evading`, `FailureToComply`, `PossessingControlledSubstances`, `PossessingHigh/Moderate/LowSeverityDrug`, `Theft`, `TransportingIllicitItems`, `Vandalism`, `VehicularAssault`, `ViolatingCurfew`), plus `PenaltyHandler`, `LawActivitySettings`, `CheckpointInstance`, `CurfewInstance`, `PatrolInstance`, `VehiclePatrolInstance`, `SentryInstance`, `SentryLocation`. **[verified]**. Combined with S1API's `LawManager` wrapper (§2.12), this is the best-supported of the three mods.

**Customers (Special Customers).** `ScheduleOne.Economy` has `Customer`, `CustomerData`, `CustomerAffinityData`, `CustomerSatisfaction`, `ECustomerStandard`, `ProductTypeAffinity`, `StandardsMethod`, `OverrideCustomerDealLocation`, `DealWindowInfo`, `EDealWindow`, `Dealer`, `EDealerType`, `Supplier`, `SupplierStash`, `DeadDrop`, `DeliveryLocation`. **[verified]**. ⚠️ **TVGS has publicly announced a Special Customers update as the next major release** — a mod of the same name is building on ground that is about to move. **[verified]** from the v0.4.6 announcement.

---

## Sources (A1)

**Primary — S1API and its ecosystem**
- <https://github.com/ifBars/S1API> — **[verified]** cloned `stable` @ `db421c99`, 2026-08-03. MIT. THE S1API you use. 22★, 27 forks, 984 tracked files.
- <https://api.github.com/repos/ifBars/S1API> — **[verified]** metadata: `fork: true`, `parent: KaBooMa/S1API`, license MIT, pushed 2026-08-03T21:43Z.
- <https://github.com/KaBooMa/S1API> — **[verified]** upstream original. **Abandoned:** last push 2025-05-23, **no LICENSE file**, 12 open issues.
- <https://ifbars.github.io/S1API/> — docfx docs site, built from `S1API/docs/`. **[verified]** exists as `homepage`; content read from repo source, not the rendered site.
- <https://www.nuget.org/packages/S1API.Forked> — **[verified]** 81 versions, 1.6.7→3.1.7. Authors `ifBars`. Repo URL in nuspec = `github.com/ifBars/S1API`. Ships `lib/netstandard2.1/S1API.dll`.
- <https://www.nuget.org/packages/S1API> — **[verified]** original, 13 versions, dead at 1.6.2. Author `KaBooMa`.
- <https://api.nuget.org/v3-flatcontainer/s1api.forked/index.json> — **[verified]** authoritative version index.
- <https://github.com/ifBars/S1API/releases> — **[verified]** release notes for v3.1.0 → v3.1.7 read via API; excellent per-patch breakage log.
- <https://github.com/ifBars/S1APITemplate> — **[verified]** cloned, last commit 2026-08-02. Canonical csproj + 15 agent-skill reference files. ⚠️ **no LICENSE**.
- <https://github.com/ifBars/S1APINPCExample> — **[verified]** exists, last push 2026-07-18, 4 example NPCs. ⚠️ **no LICENSE**. Not read in full.
- <https://github.com/ifBars/S1Interop> — **[verified]** metadata. GPL-3.0, alpha, created 2026-06-30, last push 2026-07-31. Dual-runtime toolchain.
- <https://github.com/ifBars/S1API/tree/stable/skills/schedule-one-custom-npcs> — **[verified]** exists (3 files). Officially recommended for AI agents.
- <https://thunderstore.io/c/schedule-i/p/ifBars/S1API_Forked/> · <https://www.nexusmods.com/schedule1/mods/1194> — **[verified]** as linked distribution channels; pages not fetched.
- <https://deepwiki.com/ifBars/S1API> · <http://context7.com/ifbars/s1api/llms.txt> — **[verified]** linked from README; not fetched.

**Community wiki / guides**
- <https://s1modding.github.io/> · source <https://github.com/s1modding/s1modding.github.io> — **[verified]** cloned, last commit 2026-08-03. **MIT.** Read in full: `moddevs/il2cpp.md`, `moddevs/patching.md`, `moddevs/environment_setup.md`, `moddevs/melonloader_utilities.md`. ⚠️ page front-matter dates are 2025-06.
- <https://s1modding.github.io/docs/moddevs/il2cpp/> — **[verified]** canonical source for the `#if IL2CPP`/`#if MONO` convention, `RegisterTypeInIl2Cpp`, `._items` LINQ workaround, `Il2CppType.Of<T>()`.
- <https://discord.gg/UD4K4chKak> — Schedule I Modding Discord. **[verified]** as a link in `environment_setup.md`; **not joined, not read**.
- <https://github-wiki-see.page/m/FearAndDelight/Schedule-1-Modder-Documentation/wiki> — **[community]**, mirror only. ⚠️ **The GitHub repo itself 404s.** Unmaintained; useful only for the Alternate-branch/dnSpy trick.
- <https://melonwiki.xyz/#/modders/il2cppdifferences> · <https://melonwiki.xyz/#/modders/quickstart> — **[community]**, referenced by the wiki as the deeper IL2CPP reference. Not fetched directly.

**Decompiled source / dumps**
- <https://github.com/k073l/s1-codearchiver> — **[verified]** ⭐ best cross-reference. Branch `alternate` @ `3973cfc7`, **"Auto-update for version 0.4.6f11 Alternate"**, 2026-08-01. **2,026 stripped `.cs` files.** Method bodies removed. ⚠️ no LICENSE; content © TVGS. Mono branch — signatures only, not IL2CPP proof.
- Raw file pattern: `https://raw.githubusercontent.com/k073l/s1-codearchiver/alternate/ScheduleOne-stripped/<Namespace>/<Type>.cs` — **[verified]** working; I read `DevUtilities/{Singleton,NetworkSingleton,PlayerSingleton}.cs`, `Employees/{EEmployeeType,Employee}.cs`, `Economy/EDealerType.cs`, `Law/LawManager.cs`, `NPCs/NPCManager.cs`.
- <https://github.com/Skippeh/ScheduleOne_UnityProject> — **[verified]** metadata. Last push 2026-04-01, default branch `alternate`, **Unity 2022.3.62f2** (matches your install). ⚠️ Mono branches only; no LICENSE.
- <https://github.com/thecatontheceiling/scheduleone> — ❌ **404 / DELETED.** **[verified]** — API 404 *and* absent from that user's 24 public repos. Do not rely on it.
- <https://deepwiki.com/thecatontheceiling/scheduleone> — **[community]** surviving snapshot of the above; documents **v0.3.5**, i.e. badly stale.
- <https://github.com/perfare/Il2CppDumper> — **[verified]** README. ⚠️ states Unity **5.3–2022.2** support; Schedule I is 2022.3.
- <https://github.com/AssetRipper/AssetRipper/releases> — **[verified]** as the tool the wiki's `ripping_the_project.md` prescribes.
- <https://github.com/icsharpcode/ILSpy> / `dotnet tool install --global ilspycmd` — **[verified]** as the template's prescribed decompiler.

**Game version / patch history**
- <https://store.steampowered.com/news/app/3164500/view/684135019607754378> — **[verified]** v0.4.6 "Gamepad Support + Optimization". Confirms **Special Customers is announced but unreleased**.
- <https://patchbot.io/games/schedule-i> — **[verified]** changelog index: v0.4.6 ← v0.4.5f2 ← v0.4.5 Anniversary ← v0.4.4 Weather ← v0.4.3 Storage Closets ← v0.4.2 Shrooms. Developer/publisher **TVGS**, released 2025-03-24.
- <https://steamcommunity.com/app/3164500> — **[verified]** corroborating v0.4.6 notes.

**Local verification (environment facts only — no mod source read)**
- `MelonLoader\net6\*.dll` `FileVersionInfo` — **[verified]** MelonLoader **0.7.1**, 0Harmony **2.10.2**, Il2CppInterop **1.5.0**, Mono.Cecil 0.11.6, MonoMod 22.07.31.01, Newtonsoft.Json 13.0.3.
- `Schedule I.exe` `FileVersionInfo` — **[verified]** Unity **2022.3.62f2** (`7670c08855a9`).
- `s1api.forked.{3.1.4,3.1.7}.nupkg`, `s1api.1.6.2.nupkg` — **[verified]** downloaded from api.nuget.org, extracted, nuspec + `S1API.xml` (3,806 members / 760 types) parsed for the namespace census.

**Reference mods (metadata verified via GitHub API; source not read)**
- <https://github.com/k073l/s1-employeetweaks> — MIT, 2026-08-02. ⭐ closest prior art for Hireable Drivers.
- <https://github.com/k073l/BusinessEmployment> — MIT, 2026-08-01. ⭐ proves mod-side employee creation.
- <https://github.com/k073l/s1-modsapp> — MIT, 2026-08-02. ⭐ closest prior art for a mod-settings phone app.
- <https://github.com/k073l/s1-dynamicnpcs> — no license, 2026-01-25. Data-driven NPCs on S1API.
- <https://github.com/k073l/s1-multidelivery> — MIT, 2026-08-02.
- <https://github.com/k073l/S1MelonModTemplate> — MIT, 2026-04-11. Cross-backend template, wiki-recommended.
- <https://github.com/k073l/RefGen> — MIT, 2026-03-31.
- <https://github.com/weedeej/S1MONO_IL2CPP_Template> — no license, 2025-05-26. Wiki-recommended; now fairly stale.
- <https://github.com/rfvgyhn/schedule-one-mods> — **[community]**, documents the minimal IL2CPP reference set.
- <https://github.com/chloelcdev/schedule-1-first-mod> — **[community]**, beginner IL2CPP template; confirms the `Il2CppScheduleOne` namespace rule.
- <https://github.com/TrevTV/MelonLoader.VSWizard/releases> — **[verified]** as wiki-linked official MelonLoader VS template.

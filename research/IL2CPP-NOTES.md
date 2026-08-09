# IL2CPP / MelonLoader practicalities for Schedule I

Everything here was verified against the assemblies actually installed on this machine. Where a claim
is empirical (observed by the shipped `CreativeMode` mod) rather than provable from metadata, it says so.

## 1. Verified environment

| Thing | Value | How verified |
|---|---|---|
| Game | Schedule I, IL2CPP backend | `GameAssembly.dll` present, `Schedule I_Data\il2cpp_data\` present |
| Unity | **2022.3.62f2** (`FileVersion 2022.3.62.7762112`) | `Schedule I.exe` version info |
| IL2CPP metadata version | **31** (`sanity 0xFAB11BAF`) | parsed header of `global-metadata.dat` |
| MelonLoader | **0.7.1.0** | `MelonLoader\net6\MelonLoader.dll` file version |
| Il2CppInterop.Runtime | **1.5.0.0** | `MelonLoader\net6\Il2CppInterop.Runtime.dll` file version |
| 0Harmony | **2.10.2.0** (namespace `HarmonyLib`) | `MelonLoader\net6\0Harmony.dll` file version; `MelonLoader.MelonBase.HarmonyInstance` is typed `HarmonyLib.Harmony` |
| Interop assemblies | `MelonLoader\Il2CppAssemblies\` (137 DLLs) | directory listing |
| Save root | `%USERPROFILE%\AppData\LocalLow\TVGS\Schedule I\Saves\<steamid64>\SaveGame_1..4` | directory listing |

`MelonLoader\net6\Il2CppInterop.HarmonySupport.dll` is present. MelonLoader loads it itself and installs the
IL2CPP method patcher into HarmonyLib's pipeline; **a mod never references it directly**.

## 2. Assembly reference set

This is the exact set the working `CreativeMode.csproj` in this repo uses, and it is the set to copy. All
references must be `<Private>false</Private>` so nothing gets copied into `Mods\`.

```xml
<PropertyGroup>
  <GameDir>C:\Program Files (x86)\Steam\steamapps\common\Schedule I</GameDir>
  <Il2CppAssemblies>$(GameDir)\MelonLoader\Il2CppAssemblies</Il2CppAssemblies>
  <MelonNet6>$(GameDir)\MelonLoader\net6</MelonNet6>
</PropertyGroup>
```

| Reference | From | Needed for |
|---|---|---|
| `MelonLoader` | `$(MelonNet6)` | `MelonMod`, `MelonPreferences`, `MelonCoroutines`, logging |
| `0Harmony` | `$(MelonNet6)` | `HarmonyLib.Harmony`, `[HarmonyPatch]` |
| `Il2CppInterop.Runtime` | `$(MelonNet6)` | `ClassInjector`, `Il2CppType`, `TryCast`, `Il2CppReferenceArray` |
| `Assembly-CSharp` | `$(Il2CppAssemblies)` | all `Il2CppScheduleOne.*` game types |
| `Assembly-CSharp-firstpass` | `$(Il2CppAssemblies)` | third-party/plugin code compiled into firstpass |
| `Il2CppScheduleOne.Core` | `$(Il2CppAssemblies)` | `Il2CppScheduleOne.Core.*` (weather, settings framework, items framework) |
| `Il2CppFishNet.Runtime` | `$(Il2CppAssemblies)` | `NetworkBehaviour`, `NetworkObject`, `InstanceFinder` |
| `Il2Cppmscorlib` | `$(Il2CppAssemblies)` | `Il2CppSystem.Collections.Generic.List<T>` etc. |
| `Il2CppSystem` | `$(Il2CppAssemblies)` | `Il2CppSystem.Action`, `Il2CppSystem.Object`, `Il2CppSystem.Guid` |
| `UnityEngine.CoreModule` | `$(Il2CppAssemblies)` | `GameObject`, `Transform`, `MonoBehaviour`, `Resources` |
| `UnityEngine` | `$(Il2CppAssemblies)` | type-forwarder facade |
| `UnityEngine.UI` | `$(Il2CppAssemblies)` | `Button`, `Image`, `Slider`, `Toggle` (native-looking UI) |
| `Unity.TextMeshPro` | `$(Il2CppAssemblies)` | `TextMeshProUGUI` |
| `UnityEngine.IMGUIModule` | `$(Il2CppAssemblies)` | legacy `GUI`/`GUILayout` debug UI only |
| `UnityEngine.InputLegacyModule` | `$(Il2CppAssemblies)` | `UnityEngine.Input` |
| `UnityEngine.AIModule` | `$(Il2CppAssemblies)` | `NavMesh`, `NavMeshAgent` if you touch navigation |

Required assembly-level attributes (from the working mod):

```csharp
[assembly: MelonInfo(typeof(MyMod), "My Mod", "1.0.0", "Author")]
[assembly: MelonGame("TVGS", "Schedule I")]
[assembly: MelonPlatform(MelonPlatformAttribute.CompatiblePlatforms.WINDOWS_X64)]
[assembly: MelonPlatformDomain(MelonPlatformDomainAttribute.CompatibleDomains.IL2CPP)]
```

### S1API csproj gotcha

`S1API.Forked` is consumed as a `PackageReference`, but the **runtime** DLL is supplied by
`Plugins\S1APILoader.MelonLoader.dll`. If the NuGet copy lands in `Mods\` you get duplicate assemblies.
The working project suppresses the copy:

```xml
<ItemGroup>
  <PackageReference Include="S1API.Forked" Version="3.1.4" />
</ItemGroup>

<Target Name="PreventS1APICopy" AfterTargets="ResolvePackageAssets">
  <ItemGroup>
    <ReferenceCopyLocalPaths Remove="@(ReferenceCopyLocalPaths)" Condition="'%(Filename)' == 'S1API'" />
  </ItemGroup>
</Target>
```

## 3. Name mangling: how interop names map to real names

**The rule:** interop full name = `Il2Cpp` + original full name. `ScheduleOne.NPCs.NPC` → `Il2CppScheduleOne.NPCs.NPC`.
Unity and BCL types are *not* prefixed (`UnityEngine.GameObject`, `UnityEngine.Vector3`), but the IL2CPP
BCL is (`System.Collections.Generic.List<T>` → `Il2CppSystem.Collections.Generic.List<T>`).

`System.String`, `System.Int32`, `System.Single`, `System.Boolean` and the other primitives stay as the real
managed types — Il2CppInterop marshals them. So a game method that returns `string` is `System.String` in the
interop assembly and you use it like a normal C# string.

**The original name matters** for anything string-keyed: save-file `DataType` discriminators, Harmony
patching by string, `IL2CPP.GetIl2CppClass("Assembly-CSharp", "ScheduleOne.NPCs", "NPC")`. Two attributes
record the original identity when the mapping is not 1:1 — both are verified present in these assemblies:

```csharp
// on Il2CppScheduleOne.Employees.EEmployeeType
[OriginalName("Assembly-CSharp.dll", "ScheduleOne.Employees", "EEmployeeType")]

// on compiler-generated types whose real name contains characters invalid in C# identifiers
[ObfuscatedName("ScheduleOne.NPCs.NPC+<>c__DisplayClass219_0")]
```

### Fallback names — the version-stability hazard

Some members appear as `field_Private_Boolean_0`, `Method_Protected_Virtual_Void_0`,
`Method_Private_IEnumerator_PDM_0`. These are Il2CppInterop placeholders for members whose real name was not
in the metadata. Two consequences:

1. They carry no semantic information. `Il2CppScheduleOne.NPCs.NPC` has both `field_Private_Boolean_0` and
   `field_Private_Boolean_1` and nothing distinguishes them.
2. **The numeric suffix is assignment-order dependent**, so it can shift when the game updates and the
   interop assemblies are regenerated. Never build load-bearing logic on one. If you must, guard it with a
   reflection lookup and a graceful failure path.

Compiler-generated names are also mangled: `<>c__DisplayClass219_0` becomes `__c__DisplayClass219_0`, and
`<Movement>k__BackingField` becomes `_Movement_k__BackingField`.

## 4. Interop type shapes (the five that actually bite)

### 4.1 IL2CPP fields become C# properties

Il2CppInterop cannot emit real fields backed by native memory, so **every IL2CPP instance field is a
property** with a getter and setter that read/write through the object pointer. In the dumps this means
fields you expect to see under `--- Fields ---` are under `--- Properties ---` instead.

Auto-properties in the game produce *two* entries: the compiler backing field and the real property.
Always use the real one.

```csharp
// Il2CppScheduleOne.NPCs.NPC exposes BOTH of these:
Il2CppScheduleOne.NPCs.NPCMovement _Movement_k__BackingField { get; set; }  // backing field - avoid
Il2CppScheduleOne.NPCs.NPCMovement Movement { get; set; }                   // real property - use this
```

Only use a `_X_k__BackingField` when there is no public property (it happens for private auto-properties).

### 4.2 IL2CPP interfaces become classes — this is the big one

**Il2CppInterop emits IL2CPP interfaces from this game's code as `class`es deriving from
`Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase`.** Verified:

```
=== public class Il2CppScheduleOne.Management.ITransitEntity ===
  base: Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
```

Scope of this: **`Assembly-CSharp` contains zero `public interface` types.** Counting `| public interface |`
across every dumped assembly gives `Il2CppInterop.Runtime` 26, `MelonLoader` 13, `S1API` 9,
`UnityEngine.AnimationModule` 6, `UnityEngine.CoreModule` 2 — and nothing else. So the rule is *not*
universal across interop assemblies (a couple of Unity modules do keep real interfaces), but for every
`ScheduleOne` interface it holds without exception. Treat `Il2CppScheduleOne.*.IFoo` as a class, always.

The practical fallout is largest for `ISaveable`: a managed type **cannot implement the game's save
interface**, so `SaveManager.RegisterSaveable(...)` is unreachable from a mod. See `API-PERSISTENCE-TIME.md`
for the S1API-based alternative.

Consequences, all of which have burned people:

- `x is ITransitEntity` and `(ITransitEntity)x` **do not work** — there is no C# inheritance relationship
  between the implementing type and the "interface" class.
- Implementing types do **not** list the interface under `implements:` in the dumps, and they do **not**
  appear under it in `research/raw/04-interface-implementors.txt`. That index only covers real managed
  interfaces. To find implementors of an IL2CPP interface, grep the namespace dumps for a distinctive
  member name (e.g. `AccessPoints` for `ITransitEntity`).
- The correct way to cast is `TryCast<T>()`, which performs a real `il2cpp_object_isinst` against the
  native interface and therefore *does* succeed:

```csharp
// Il2CppObjectBase.TryCast<T>() returns null on failure; Cast<T>() throws.
var transit = storageEntity.TryCast<Il2CppScheduleOne.Management.ITransitEntity>();
if (transit != null)
    transit.InsertItemIntoInput(item, npc);
```

`Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase` verified members:

```csharp
public T Cast<T>();          // throws on mismatch
public T TryCast<T>();       // returns null on mismatch  <-- use this
public T Unbox<T>();         // value types
public System.IntPtr Pointer { get; }
public System.IntPtr ObjectClass { get; }
public System.Boolean WasCollected { get; }
```

`WasCollected` is worth knowing: an interop wrapper can outlive the native object. Check it before
touching a cached reference across scene loads.

### 4.3 Arrays

C# arrays do not survive. Il2CppInterop uses:

| IL2CPP array of | Interop type |
|---|---|
| reference type | `Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<T>` |
| value type | `Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<T>` |
| string | `Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray` |

All derive from `Il2CppArrayBase<T>` / `Il2CppArrayBase`, support `Length` and `this[int]`, and implement
`IEnumerable<T>`. They construct from a managed array or a length:

```csharp
using Il2CppInterop.Runtime.InteropTypes.Arrays;

var arr = new Il2CppStringArray(new[] { "a", "b" });
var refs = new Il2CppReferenceArray<UnityEngine.Transform>(4);
```

Il2CppInterop also generates convenience overloads that take a plain `T[]`, so many game methods can be
called with a managed array directly — check the dump; both forms appear side by side, e.g.
`UnityEngine.GUILayout.TextField(string, Il2CppReferenceArray<GUILayoutOption>)` **and**
`TextField(string, GUILayoutOption[])`.

### 4.4 Collections

`Il2CppSystem.Collections.Generic.List<T>` has `Count` and `this[int]`. **Prefer index loops over
`foreach`** — the interop enumerator allocates a wrapper per step and is the usual suspect when iteration
throws mid-loop. The shipped mod uses index loops throughout:

```csharp
var reg = Il2CppScheduleOne.NPCs.NPCManager.NPCRegistry;
for (int i = 0; i < reg.Count; i++)
{
    var npc = reg[i];
    if (npc == null) continue;   // registry can contain destroyed entries
}
```

### 4.5 Nested types

`Outer+Inner` in the dumps is `Outer.Inner` in C#. Verified examples you will actually use:
`Il2CppScheduleOne.Management.ITransitEntity.ESlotType` (`Input=0, Output=1, Both=2`),
`Il2CppScheduleOne.Employees.EmployeeManager.EmployeeAppearance`,
`Il2CppScheduleOne.Console.ConsoleCommand`.

## 5. Delegates and events

### 5.1 Il2CppSystem.Action

Verified arities present: non-generic `Il2CppSystem.Action`, plus generic `Action` of 1 through 8 type
parameters (written `Il2CppSystem.Action'1` .. `'8` in the dumps, where `'` stands for the metadata
backtick). Each is
`sealed class : Il2CppSystem.MulticastDelegate` and — critically — each has an implicit conversion from the
managed delegate plus combine/remove operators:

```csharp
public static Il2CppSystem.Action op_Implicit(System.Action);
public static Il2CppSystem.Action op_Addition(Il2CppSystem.Action, Il2CppSystem.Action);
public static Il2CppSystem.Action op_Subtraction(Il2CppSystem.Action, Il2CppSystem.Action);
```

So subscribing to a game "event" works with ordinary C# syntax, as long as you **give the compiler a
constructed delegate rather than a method group** (implicit user-defined conversions don't apply to method
groups):

```csharp
// Il2CppScheduleOne.NPCs.NPC.onEnterVehicle is Il2CppSystem.Action<LandVehicle>
System.Action<Il2CppScheduleOne.Vehicles.LandVehicle> handler = veh => { /* ... */ };
npc.onEnterVehicle += handler;    // works: op_Implicit then op_Addition
npc.onEnterVehicle -= handler;    // ONLY works if you pass the same managed delegate instance
```

Note that most of these are **public fields/properties, not C# events**, so `+=` is really
`x = op_Addition(x, y)`. Two practical consequences:

1. **Keep the managed delegate in a field** if you ever want to unsubscribe. Each `op_Implicit` call
   creates a *new* native wrapper, so `-= veh => {...}` silently does nothing.
2. Assigning instead of adding (`npc.onEnterVehicle = handler;`) **wipes every other subscriber**,
   including the game's own. Always use `+=`.

### 5.2 UnityEvent / Button.onClick

`UnityEngine.Events.UnityEvent.AddListener` takes `UnityEngine.Events.UnityAction`, **not**
`Il2CppSystem.Action`. `UnityAction` is `sealed class : Il2CppSystem.MulticastDelegate` and also has
`op_Implicit(System.Action)`:

```csharp
// verified: UnityEngine.UI.Button.onClick is Button.ButtonClickedEvent : UnityEvent
//           UnityEvent.AddListener(UnityEngine.Events.UnityAction call)
button.onClick.AddListener((System.Action)OnMyButtonClicked);
```

`UnityEvent<T0>` through `UnityEvent<T0,T1,T2,T3>` take `UnityAction<...>` correspondingly.

There is also `UnityEventBase.AddListener(Il2CppSystem.Object targetObj, Il2CppSystem.Reflection.MethodInfo method)`
for persistent listeners — ignore it, it is the serialized-inspector path.

### 5.3 Arbitrary delegate conversion

For any other delegate type, `Il2CppInterop.Runtime.DelegateSupport` is the general escape hatch:

```csharp
public static TIl2Cpp ConvertDelegate<TIl2Cpp>(System.Delegate delegate);
```

`op_Implicit` on the generated delegate types calls into this internally. The returned wrapper is rooted by
`DelegateSupport`'s internal trampoline cache, so it will not be collected out from under native code —
but the *managed* target still needs to be kept alive by you if it is a closure over a short-lived object.

## 6. Harmony patching under MelonLoader 0.7.1

### 6.1 Automatic PatchAll

`MelonLoader.MelonBase` exposes `HarmonyLib.Harmony HarmonyInstance { get; }` and MelonLoader calls
`PatchAll` on your assembly automatically during registration, unless the assembly opts out via
`MelonAssembly.HarmonyDontPatchAll`. So attribute patches just work with no explicit call.

If you want manual control, patch in `OnInitializeMelon` using `HarmonyInstance`.

### 6.2 Attribute patching

Patching IL2CPP methods is transparent because `Il2CppInterop.HarmonySupport` installs a method patcher
that detours the *native* function behind the interop stub. Reference the interop types directly:

```csharp
using HarmonyLib;
using Il2CppScheduleOne.Law;

[HarmonyPatch(typeof(LawController), nameof(LawController.OnUncappedMinPass))]
static class Patch_LawController_Tick
{
    static void Postfix(LawController __instance) { /* ... */ }
}
```

Rules that differ from managed Harmony:

- **`__instance` must be typed as the interop type**, not `object`.
- **Never `Prefix` with `ref` on an interop reference parameter** unless you have checked the dump for a
  genuine `ref`/`out`. Interop reference params are pointers already.
- `__result` works for value types and interop reference types.
- **Transpilers are unavailable.** There is no IL to rewrite — the interop stub is a thin native call and
  the real body is compiled C++. Prefix/Postfix/Finalizer only.
- A `Prefix` returning `false` skips the native call, which is the reliable way to *replace* behaviour.

### 6.3 Overload disambiguation

Many game methods are overloaded (`NPC.EnterBuilding` has two, `NPC.SetScale` has two,
`NPC.Load` has two). Always pass an explicit argument-type array:

```csharp
[HarmonyPatch(typeof(Il2CppScheduleOne.NPCs.NPC), nameof(Il2CppScheduleOne.NPCs.NPC.EnterBuilding),
    new[] { typeof(string), typeof(int) })]
```

### 6.4 Virtual, interface and generic methods

- **Virtual methods:** patching the declaring type patches that type's native implementation only.
  Because IL2CPP compiles each override separately, **you must patch each override you care about.**
  `Il2CppScheduleOne.NPCs.NPC.Awake` is `virtual` and there are 81 direct subclasses; patching `NPC.Awake`
  does *not* intercept `PoliceOfficer.Awake` if `PoliceOfficer` overrides it. Use
  `research/raw/03-subclasses.txt` to enumerate overrides, and patch a loop of `MethodInfo`s manually:

```csharp
foreach (var t in new[] { typeof(Botanist), typeof(Chemist), typeof(Cleaner), typeof(Packager) })
{
    var m = AccessTools.DeclaredMethod(t, "UpdateBehaviour");
    if (m != null)
        HarmonyInstance.Patch(m, prefix: new HarmonyMethod(typeof(MyPatch), nameof(MyPatch.Pre)));
}
```

  Note `AccessTools.DeclaredMethod` (not `Method`) — `Method` walks up the hierarchy and will silently
  hand you the base implementation.

- **"Interface" methods:** since interop interfaces are classes with `virtual` stubs
  (`ITransitEntity.InsertItemIntoInput` is `public virtual`), patching the interface class patches the
  interface's own stub, not each implementor's native code. **Patch the concrete implementors instead.**

- **Generic methods:** Il2CppInterop generates a generic stub plus a `MethodInfoStoreGeneric_*` nested
  helper per instantiation. Patching the open generic does not reliably hit instantiations. Prefer patching
  a non-generic caller. Treat generic-method patching as a last resort. **UNVERIFIED** on this build — no
  game system in the mapped areas requires it.

### 6.5 FishNet RPCs — patch the right member

FishNet's codegen turns one `[ServerRpc]`/`[ObserversRpc]` method into a family. For
`Il2CppScheduleOne.Employees.EmployeeManager.CreateEmployee` the real, verified members are:

```csharp
// the entry point you call, and the call site to intercept
public void CreateEmployee(Property property, EEmployeeType type, string firstName, string lastName,
                           string id, bool male, int appearanceIndex, Vector3 position,
                           Quaternion rotation, string guid = "");

// serialises + sends to the server
public void RpcWriter___Server_CreateEmployee_311954683(...same args...);

// THE REAL BODY - runs on the authoritative peer
public void RpcLogic___CreateEmployee_311954683(...same args...);

// deserialises an incoming call
public void RpcReader___Server_CreateEmployee_311954683(PooledReader r, Channel c, NetworkConnection conn);
```

Choose deliberately:

| Goal | Patch |
|---|---|
| Veto or rewrite arguments before they leave the caller | the public method `X` |
| Observe/alter the actual effect wherever it lands | `RpcLogic___X_<hash>` |
| Inspect what arrived off the wire | `RpcReader___...` |

**The `<hash>` suffix is a codegen hash and can change between game versions.** Never hardcode it in a
`[HarmonyPatch]` attribute. Resolve it at runtime:

```csharp
static MethodInfo FindRpcLogic(System.Type declaring, string methodName)
    => AccessTools.GetDeclaredMethods(declaring)
        .FirstOrDefault(m => m.Name.StartsWith("RpcLogic___" + methodName + "_", StringComparison.Ordinal));
```

SyncVars follow the same pattern: `syncVar___<Foo>k__BackingField`,
`SyncAccessor_<Foo>k__BackingField`, `sync___get_value_<Foo>k__BackingField`,
`sync___set_value_<Foo>k__BackingField(value, asServer)`, and a per-type
`ReadSyncVar___ScheduleOne_X`. Write a SyncVar through the public setter **on the server only**; writing on
a client desynchronises until the next authoritative update and may be overwritten silently.

## 7. Custom class injection

### 7.1 The verified API surface

`Il2CppInterop.Runtime.Injection.ClassInjector` — exact public members in the installed 1.5.0:

```csharp
public static void RegisterTypeInIl2Cpp<T>();
public static void RegisterTypeInIl2Cpp(System.Type type);
public static void RegisterTypeInIl2Cpp<T>(RegisterTypeOptions options);
public static void RegisterTypeInIl2Cpp(System.Type type, RegisterTypeOptions options);

public static bool IsTypeRegisteredInIl2Cpp<T>();
public static bool IsTypeRegisteredInIl2Cpp(System.Type type);

public static System.IntPtr DerivedConstructorPointer<T>();
public static void DerivedConstructorBody(Il2CppObjectBase objectBase);

public static void Dump<T>();                 // diagnostics: prints the injected class layout
public static void AssignGcHandle(System.IntPtr pointer, System.Runtime.InteropServices.GCHandle gcHandle);
public static void ProcessNewObject(Il2CppObjectBase obj);
public static void Finalize(System.IntPtr ptr);
```

`Il2CppInterop.Runtime.Injection.RegisterTypeOptions`:

```csharp
public static readonly RegisterTypeOptions Default;
public bool LogSuccess { get; set; }
public System.Func<System.Type, System.Type[]> InterfacesResolver { get; set; }
public Il2CppInterfaceCollection Interfaces { get; set; }
```

`Il2CppInterop.Runtime.Injection.Il2CppInterfaceCollection : List<INativeClassStruct>` has
`op_Implicit(System.Type[])`, so you can assign a plain type array to `Interfaces`.

Supporting attributes, both verified present:

```csharp
Il2CppInterop.Runtime.Attributes.HideFromIl2CppAttribute      // AttributeUsage(736) = method|property|field|event
Il2CppInterop.Runtime.Attributes.Il2CppImplementsAttribute    // AttributeUsage(4) = class; ctor(System.Type[] interfaces)
```

Also present: `Il2CppInterop.Runtime.Injection.EnumInjector` (injects a **new** enum type — it cannot add
members to an existing game enum) and `Il2CppInterop.Runtime.Runtime.ClassInjectorBase`
(`GetMonoObjectFromIl2CppPointer`, `GetGcHandlePtrFromIl2CppObject`).

### 7.2 The canonical injected MonoBehaviour

This is the pattern to use for all three planned mods. Register **once**, before any instance is created.

```csharp
using System;
using Il2CppInterop.Runtime.Injection;
using MelonLoader;
using UnityEngine;

public class DriverController : MonoBehaviour
{
    // REQUIRED: the IntPtr ctor. Il2CppInterop calls this when native code hands the object back to
    // managed code (e.g. from GetComponent). Without it you get a MissingMethodException at first use.
    public DriverController(IntPtr ptr) : base(ptr) { }

    // REQUIRED if you ever do AddComponent<DriverController>() / new DriverController().
    // ClassInjector.DerivedConstructorPointer<T>() allocates the native object for the injected class,
    // and DerivedConstructorBody wires up the GC handle.
    public DriverController() : base(ClassInjector.DerivedConstructorPointer<DriverController>())
        => ClassInjector.DerivedConstructorBody(this);

    // Unity messages: must be public or the IL2CPP-side vtable won't see them.
    public void Awake() { }
    public void OnDestroy() { }
    public void Update() { }

    // Anything IL2CPP must not see (unsupported signatures, generics, managed-only types).
    [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
    internal System.Collections.Generic.Dictionary<string, object> ManagedOnlyState = new();
}

public class MyMod : MelonMod
{
    public override void OnInitializeMelon()
    {
        if (!ClassInjector.IsTypeRegisteredInIl2Cpp<DriverController>())
            ClassInjector.RegisterTypeInIl2Cpp<DriverController>();
    }
}
```

With interfaces:

```csharp
ClassInjector.RegisterTypeInIl2Cpp<MyThing>(new RegisterTypeOptions
{
    LogSuccess = true,
    Interfaces = new[] { typeof(Il2CppSomeNamespace.ISomeInterface) }   // op_Implicit(Type[])
});
```

### 7.3 Rules for members of an injected type

- Every member IL2CPP should see must be **public**, and its signature must use only interop-representable
  types (primitives, `Il2CppSystem.*`, interop wrappers, interop arrays).
- Anything else needs `[HideFromIl2Cpp]`. Managed generics, `System.Collections.Generic.*`,
  `System.Action`, tuples and `Span<T>` all need it.
- Fields on injected types are real managed fields; they are **not** Unity-serialized and never appear in
  the inspector or in a prefab. There is no `[SerializeField]` equivalent.
- Injected types are **not** available to `Resources.Load`, asset bundles, or prefab references. You can
  only ever attach them with `AddComponent`.
- Registration must happen before the first use and is process-global; guard with
  `IsTypeRegisteredInIl2Cpp<T>()` so a reload does not double-register.
- Use `ClassInjector.Dump<T>()` when injection "succeeds" but methods never fire — it prints the resulting
  native class layout.

## 8. Can you subclass a *game* IL2CPP class? — verdict: don't

The mechanical answer is "sometimes". The engineering answer for this game is **no, and there is a
better path**. Reasons, in order of how badly they bite:

1. **FishNet codegen is build-time, not runtime.** `Il2CppScheduleOne.NPCs.NPC`,
   `Il2CppScheduleOne.Employees.Employee`, `Il2CppScheduleOne.Economy.Customer` and
   `Il2CppScheduleOne.Economy.Dealer` all derive from `Il2CppFishNet.Object.NetworkBehaviour`. FishNet's
   weaver generated, per concrete type, `NetworkInitialize___Early`, `NetworkInitialize__Late`,
   the `RpcWriter___`/`RpcReader___`/`RpcLogic___` triples, `ReadSyncVar___ScheduleOne_X` and the
   `syncVar___*` accessors. An injected subclass has **none** of these. It will not register RPCs and will
   not participate in SyncVar serialisation. This alone rules out injected `NPC`/`Employee`/`Customer`
   subclasses for anything networked.
2. **You cannot get an injected type onto a prefab.** FishNet only spawns prefabs whose `NetworkObject`
   carries a valid prefabId — the game itself emits
   `"Spawned object has an invalid prefabId. Make sure all objects which are being spawned over the network are within SpawnableObjects on the NetworkManager."`
   and `"PrefabId for {0} is null. Object will not spawn."`. So even a working injected subclass has no
   spawnable asset to live on.
3. **Only `virtual` members can be overridden**, and IL2CPP compiles each override separately, so
   `base.Something()` calls and non-virtual "overrides" silently do the wrong thing.

### The pattern that does work

**Reuse an already-registered game prefab, then bolt an injected `MonoBehaviour` onto the instance and
Harmony-patch the vanilla logic you need to change.** The game itself demonstrates both halves:

- `Il2CppScheduleOne.Employees.EmployeeManager.CreateEmployee_Server(Property, EEmployeeType, string firstName, string lastName, string id, bool male, int appearanceIndex, Vector3 position, Quaternion rotation, string guid)`
  returns a fully-formed, network-spawned `Employee` built from `BotanistPrefab` / `PackagerPrefab` /
  `ChemistPrefab` / `CleanerPrefab` via `GetEmployeePrefab(EEmployeeType)`. That is a supported runtime
  NPC-instantiation path.
- `Il2CppScheduleOne.Cartel.GoonPool` shows the alternative: a **fixed pool** of pre-placed
  `CartelGoon` instances (`goons`, `spawnedGoons`, `unspawnedGoons`) recycled by
  `SpawnGoon(Vector3)` / `ReturnToPool(CartelGoon)`, with identity supplied purely by re-skinning —
  `GetRandomAppearance()` returns a `CartelGoonAppearance { bool IsMale; int BaseAppearanceIndex;
  Color SkinColor; Color HairColor; int ClothingIndex; int VoiceIndex; }` indexing into
  `MaleBaseAppearances` / `FemaleBaseAppearances` / `MaleClothing` / `FemaleClothing` / `SkinTones` /
  `HairColors` / `MaleVoices` / `FemaleVoices`.

So: **appearance is data, not a type.** Both settings classes derive from `UnityEngine.ScriptableObject`, so
a mod can author a brand-new look at runtime with `ScriptableObject.CreateInstance<T>()`:

```csharp
// Il2CppScheduleOne.AvatarFramework.Customization.BasicAvatarSettings : UnityEngine.ScriptableObject
//   ~31 semantic fields - author against this one
public Il2CppScheduleOne.AvatarFramework.AvatarSettings GetAvatarSettings();

// Il2CppScheduleOne.AvatarFramework.AvatarSettings : UnityEngine.ScriptableObject   (~72 raw fields)
// Il2CppScheduleOne.AvatarFramework.Avatar : UnityEngine.MonoBehaviour
public void LoadAvatarSettings(Il2CppScheduleOne.AvatarFramework.AvatarSettings settings);
public void ApplySettings(Il2CppScheduleOne.AvatarFramework.AvatarSettings settings);
```

Mind the sub-namespace: `BasicAvatarSettings` is in `...AvatarFramework.Customization`, while
`AvatarSettings` and `Avatar` are in `...AvatarFramework`. There is **no** `ApplyBasicSettings` method.
This is how you get a "custom NPC that looks native" without a single injected game-type subclass.

### Adding a new employee "type"

`Il2CppScheduleOne.Employees.EEmployeeType` is a closed 4-value enum
(`Botanist=0, Handler=1, Chemist=2, Cleaner=3`; `Handler` is the enum name for the `Packager` class).
IL2CPP enums are plain ints so `(EEmployeeType)4` compiles and passes, but
`EmployeeManager.GetEmployeePrefab` will return null for it. The workable approach is a Harmony postfix on
`GetEmployeePrefab` returning `PackagerPrefab` for your synthetic value, plus your own marker component to
distinguish the instance. Do **not** expect vanilla `switch`es on the enum to behave for an unknown value.

## 9. Coroutines

`MelonLoader.MelonCoroutines` — exact verified signatures:

```csharp
public static System.Object Start(System.Collections.IEnumerator routine);
public static void Stop(System.Object coroutineToken);
```

Note the parameter is the **managed** `System.Collections.IEnumerator`, so a normal C# iterator method
works. `Start` returns an opaque token — keep it if you need to stop the routine.

```csharp
private object _token;

public override void OnInitializeMelon()
    => _token = MelonCoroutines.Start(Tick());

private System.Collections.IEnumerator Tick()
{
    while (true)
    {
        yield return new UnityEngine.WaitForSeconds(1f);   // Unity yield instructions are fine
        // ...
    }
}

public override void OnDeinitializeMelon()
{
    if (_token != null) MelonCoroutines.Stop(_token);
}
```

Caveats:

- A `MelonCoroutines` routine is hosted by MelonLoader, not by a `GameObject`, so it **survives scene
  loads**. If it touches scene objects, re-validate them after every load or you will dereference
  destroyed wrappers (check `WasCollected`).
- Do not `yield return` an `Il2CppSystem.Collections.IEnumerator` returned by a game method — the types are
  not interchangeable. To run a *game* coroutine, call the game's own
  `MonoBehaviour.StartCoroutine(Il2CppSystem.Collections.IEnumerator)` on a game component.
- Exceptions inside the routine kill it silently. Wrap the body in try/catch and log.

## 10. Mod settings persistence: MelonPreferences

For the "toggleable from a native-looking main-menu screen" requirement, `MelonPreferences` is the right
backing store (it is global/per-install, not per-save; per-save data belongs in the save folder — see
`API-PERSISTENCE-TIME.md`). Verified API:

```csharp
public static MelonPreferences_Category CreateCategory(string identifier, string display_name = null,
                                                       bool is_hidden = false, bool should_save = true);
public static MelonPreferences_Entry<T> CreateEntry<T>(string category_identifier, string entry_identifier,
        T default_value, string display_name = null, string description = null, bool is_hidden = false,
        bool dont_save_default = false, MelonLoader.Preferences.ValueValidator validator = null);

public static MelonPreferences_Category GetCategory(string identifier);
public static MelonPreferences_Entry<T> GetEntry<T>(string category_identifier, string entry_identifier);
public static T GetEntryValue<T>(string category_identifier, string entry_identifier);
public static void SetEntryValue<T>(string category_identifier, string entry_identifier, T value);
public static bool HasEntry(string category_identifier, string entry_identifier);
public static void Load();
public static void Save();

public static readonly MelonEvent<string> OnPreferencesLoaded;
public static readonly MelonEvent<string> OnPreferencesSaved;
```

`MelonLoader.MelonPreferences_Category` (what `CreateCategory` returns) adds the per-category form used in
the snippet below:

```csharp
public MelonPreferences_Entry<T> CreateEntry<T>(string identifier, T default_value, string display_name = null,
        string description = null, bool is_hidden = false, bool dont_save_default = false,
        MelonLoader.Preferences.ValueValidator validator = null, string oldIdentifier = null);
public MelonPreferences_Entry GetEntry(string identifier);
public MelonPreferences_Entry<T> GetEntry<T>(string identifier);
public void SaveToFile(bool printmsg = true);
```

`MelonBase` also gives you overridable `OnPreferencesLoaded()` / `OnPreferencesSaved()` callbacks (and
`(string filepath)` variants). Values land in `UserData\MelonPreferences.cfg` (TOML — `MelonPreferences.Mapper`
is a `TomlMapper`).

```csharp
private static MelonPreferences_Entry<bool> _enableDrivers;

public override void OnInitializeMelon()
{
    var cat = MelonPreferences.CreateCategory("ScheduleOneExpansions", "Expansions");
    _enableDrivers = cat.CreateEntry("HireableDrivers", true, "Hireable Drivers",
                                     "Adds driver employees that move goods between properties.");
}
// read: _enableDrivers.Value        write: _enableDrivers.Value = false; MelonPreferences.Save();
```

## 11. MelonMod lifecycle — verified overridables

From `MelonLoader.MelonBase` and `MelonLoader.MelonMod`. Anything marked obsolete in the assembly is
omitted; the ones below are the live set.

```csharp
// MelonBase
public virtual void OnEarlyInitializeMelon();   // before the support module; almost nothing exists yet
public virtual void OnInitializeMelon();        // main entry point - register injected types, prefs, patches
public virtual void OnLateInitializeMelon();    // after all mods initialised
public virtual void OnUpdate();
public virtual void OnLateUpdate();
public virtual void OnFixedUpdate();
public virtual void OnGUI();                    // legacy IMGUI - see section 12
public virtual void OnApplicationQuit();
public virtual void OnDeinitializeMelon();
public virtual void OnPreferencesLoaded();      // + (string filepath) overload
public virtual void OnPreferencesSaved();       // + (string filepath) overload
public virtual void OnPreSupportModule();

// MelonMod
public virtual void OnSceneWasLoaded(int buildIndex, string sceneName);
public virtual void OnSceneWasInitialized(int buildIndex, string sceneName);
public virtual void OnSceneWasUnloaded(int buildIndex, string sceneName);
```

Do **not** touch game managers in `OnInitializeMelon` — nothing is loaded yet. Use
`OnSceneWasInitialized` for scene-scoped setup, and for save-scoped setup hook the game's own load-complete
event (see `API-PERSISTENCE-TIME.md`).

## 12. Which Unity APIs are actually unsafe on this build

The existing `CreativeMode` mod's comments say `GUILayout.TextField` and `GUI.DrawTexture` are "stripped".
**That description is wrong, and the distinction matters.** Both are verified present in the interop
metadata — `UnityEngine.GUI` declares exactly **10** `DrawTexture` overloads and **4** `TextField`
overloads, and `UnityEngine.GUILayout` declares `TextField`/`TextArea` in both array forms:

```csharp
// UnityEngine.GUI - 10 DrawTexture overloads present, e.g.
public static void DrawTexture(UnityEngine.Rect position, UnityEngine.Texture image);
public static void DrawTexture(Rect position, Texture image, ScaleMode scaleMode, bool alphaBlend, float imageAspect);
// UnityEngine.GUI  - 4 TextField overloads present
public static string TextField(UnityEngine.Rect position, System.String text);
// UnityEngine.GUILayout - TextField/TextArea present in both Il2CppReferenceArray and params-array forms
public static string TextField(System.String text, UnityEngine.GUILayoutOption[] options);
```

So they **compile and resolve**. What the mod actually observed is a *runtime* failure: the call throws
mid-`OnGUI`, and because one exception aborts the rest of that IMGUI pass, the remainder of the window
silently vanishes — which is exactly the reported "blank Items tab" symptom.

The most likely mechanism is a missing native ICall / null internal state behind these paths
(`GUI.Internal_DrawTexture(ref Internal_DrawTextureArguments)` and the `UnityEngine.TextEditor` machinery
that `DoTextField` drives). **I could not confirm the precise cause from metadata alone — marked
UNVERIFIED.** What is certain is the operational consequence, so treat this as the rule:

1. **Do not build shipping UI on legacy IMGUI.** Use uGUI + TextMeshPro like the game does. IMGUI is fine
   for a developer overlay, nothing more.
2. **If you do use IMGUI, wrap each widget group in its own try/catch**, never one try/catch around the
   whole window — otherwise a single bad call blanks everything after it.
3. **Assume nothing about which IMGUI calls work; verify empirically.** Metadata presence is not evidence
   of a working call path on IL2CPP. The verified-good set from the shipped mod is `GUI.Box`, `GUI.Label`,
   `GUI.Button`, `GUI.BeginGroup`/`EndGroup`, `GUI.Window`. Text input is done by capturing
   `UnityEngine.Input.inputString` manually instead of using a text field.

Other things worth knowing:

- **`UnityEngine.Resources.FindObjectsOfTypeAll<T>()`** works and is the shipped mod's main discovery
  mechanism for objects not reachable from a manager. It is expensive — cache the result.
- **`UnityEngine.Object` null checks:** `== null` on an interop wrapper compares the *wrapper*, and Unity's
  destroyed-object semantics do not carry across cleanly. For components fetched from the game, prefer an
  explicit `WasCollected` check plus a `!= null` test, and re-fetch after scene loads.
- **`Il2CppSystem.Guid` is not `System.Guid`.** Convert via `ToString()`/parse; they are distinct types.

## 13. Networked spawning — the hard constraint

Verified from the game's own error literals and FishNet's API:

- FishNet spawns only prefabs registered in the `NetworkManager`'s spawnable-prefab collection.
  Verified types: `Il2CppFishNet.Managing.Object.PrefabObjects : UnityEngine.ScriptableObject`,
  `SinglePrefabObjects : PrefabObjects`, `DefaultPrefabObjects : SinglePrefabObjects`,
  `DualPrefabObjects : PrefabObjects`.
- Registration at runtime **is** exposed:

```csharp
public virtual void AddObject(Il2CppFishNet.Object.NetworkObject networkObject, bool checkForDuplicates = false);
public virtual void AddObjects(Il2CppSystem.Collections.Generic.List<NetworkObject> networkObjects, bool checkForDuplicates = false);
public void AddUniqueNetworkObject(Il2CppFishNet.Object.NetworkObject nob);   // SinglePrefabObjects
public virtual void InitializePrefabRange(int startIndex);
public virtual NetworkObject GetObject(bool asServer, int id);
```

- **But prefabIds are positional.** Every peer must register the same prefabs in the same order, or the
  server's id will resolve to a different prefab on a client and FishNet kicks the connection
  (`"... Connection {1} will be kicked immediately."`). In practice this means: if you register custom
  prefabs, **every** player in the session needs the mod, at the same version, registering at a
  deterministic point in startup.

**Recommendation:** avoid custom prefabs entirely. Spawn through
`EmployeeManager.CreateEmployee_Server(...)` (reuses an already-registered employee prefab) or recycle
pooled instances the way `GoonPool` does, then re-skin with `AvatarSettings` and attach an injected
`MonoBehaviour`. This sidesteps prefab registration, version-lockstep and FishNet codegen in one move.

## 14. Regenerating the research dumps

The probe used to produce everything in `research/raw/` lives in `research/probe/`. It uses
`System.Reflection.MetadataLoadContext`, so it never executes game code and does not need the native
runtime.

```powershell
# full metadata dump -> research/raw/{00..04}*.txt and research/raw/ns/ns-<Namespace>.txt
dotnet run --project research\probe\Probe.csproj -c Release

# string literals + identifier tables -> research/raw/literals-*.txt, strings-*.txt
dotnet run --project research\probe\Probe.csproj -c Release -- strings

# pull one type's full member block
powershell -NoProfile -File research\probe\type.ps1 Il2CppScheduleOne.NPCs.NPCManager
powershell -NoProfile -File research\probe\type.ps1 --grep TransitEntity
```

Re-run both after every game update; the interop assemblies are regenerated by MelonLoader on first launch
after a patch, and fallback member names (§3) can shift.


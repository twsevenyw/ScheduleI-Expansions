using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Probe;

// Metadata-only reflection dumper for the Schedule I Il2CppInterop assemblies.
// Nothing is executed from the target assemblies; MetadataLoadContext reads metadata only.
internal static class Program
{
    const string GameDir = @"C:\Program Files (x86)\Steam\steamapps\common\Schedule I";
    static string Il2CppDir => Path.Combine(GameDir, "MelonLoader", "Il2CppAssemblies");
    static string Net6Dir => Path.Combine(GameDir, "MelonLoader", "net6");
    static string ModsDir => Path.Combine(GameDir, "Mods");
    const string OutDir = @"C:\Users\fyfvg\Documents\ScheduleI-CreativeMode\research\raw";

    static MetadataLoadContext _mlc;

    static int Main(string[] args)
    {
        Directory.CreateDirectory(OutDir);

        if (args.Length > 0 && args[0] == "strings") { Strings.Run(OutDir); return 0; }

        var paths = new List<string>();
        paths.AddRange(Directory.GetFiles(Il2CppDir, "*.dll"));
        paths.AddRange(Directory.GetFiles(Net6Dir, "*.dll"));
        foreach (var f in Directory.GetFiles(ModsDir, "*.dll")) paths.Add(f);
        // .NET 6 runtime assemblies so the core assembly + BCL references resolve.
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location);
        paths.AddRange(Directory.GetFiles(runtimeDir, "*.dll"));

        // de-dup by simple name, prefer the first occurrence (game dirs win over runtime)
        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in paths)
        {
            var n = Path.GetFileNameWithoutExtension(p);
            if (!byName.ContainsKey(n)) byName[n] = p;
        }

        _mlc = new MetadataLoadContext(new PathAssemblyResolver(byName.Values), "System.Private.CoreLib");

        var targets = new[]
        {
            "Assembly-CSharp",
            "Assembly-CSharp-firstpass",
            "Il2CppScheduleOne.Core",
            "Il2Cpp__Generated",
            "Il2CppFishNet.Runtime",
            "Il2CppAstarPathfindingProject",
            "Unity.TextMeshPro",
            "UnityEngine.UI",
            "Il2CppGameKit.Utilities",
            "Il2CppHSVPicker",
            "S1API.Il2Cpp.MelonLoader",
            "Il2CppInterop.Runtime",
            "MelonLoader",
            // Unity modules: needed to prove which Unity APIs survived IL2CPP stripping in this build.
            "UnityEngine.CoreModule",
            "UnityEngine.IMGUIModule",
            "UnityEngine.InputLegacyModule",
            "UnityEngine.AIModule",
            "UnityEngine.AnimationModule",
            "UnityEngine.PhysicsModule",
            "UnityEngine.TextRenderingModule",
            "UnityEngine.UIModule",
            "UnityEngine.ImageConversionModule",
            "UnityEngine.AssetBundleModule",
            // Il2CppSystem: Action/Func shapes a mod must use for IL2CPP delegates and events.
            "Il2CppSystem",
            "Il2Cppmscorlib",
        };

        var loaded = new List<Assembly>();
        foreach (var t in targets)
        {
            if (!byName.TryGetValue(t, out var p)) { Console.WriteLine($"!! missing {t}"); continue; }
            try { loaded.Add(_mlc.LoadFromAssemblyPath(p)); }
            catch (Exception ex) { Console.WriteLine($"!! load fail {t}: {ex.Message}"); }
        }

        DumpAssemblyIndex(loaded, byName);
        DumpNamespaceIndex(loaded);
        DumpTypeIndex(loaded);
        DumpHierarchy(loaded);
        DumpPerNamespace(loaded);
        Console.WriteLine("done");
        return 0;
    }

    static IEnumerable<Type> SafeTypes(Assembly a)
    {
        Type[] ts;
        try { ts = a.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { ts = ex.Types.Where(t => t != null).ToArray(); }
        catch (Exception) { yield break; }
        // A few assemblies in this set (notably Il2Cppmscorlib) contain type rows whose declaring-type
        // handle is out of range, which makes MetadataLoadContext throw from FullName/Namespace/IsNested.
        // Probe each type once and skip the broken rows so one bad row can't kill the whole dump.
        foreach (var t in ts)
        {
            if (t == null) continue;
            try { _ = t.FullName; _ = t.Namespace; _ = t.IsNested; }
            catch { continue; }
            yield return t;
        }
    }

    // ---------------------------------------------------------------- indexes

    static void DumpAssemblyIndex(List<Assembly> loaded, Dictionary<string, string> byName)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Assembly index (name, version, path, referenced assemblies)");
        sb.AppendLine();
        foreach (var a in loaded)
        {
            var n = a.GetName();
            sb.AppendLine($"=== {n.Name} v{n.Version} ===");
            sb.AppendLine($"  path: {a.Location}");
            sb.AppendLine($"  types: {SafeTypes(a).Count()}");
            sb.AppendLine("  references:");
            foreach (var r in a.GetReferencedAssemblies().OrderBy(x => x.Name))
                sb.AppendLine($"    {r.Name} v{r.Version}");
            sb.AppendLine();
        }
        sb.AppendLine("=== ALL DLLs available on the resolver path ===");
        foreach (var kv in byName.OrderBy(k => k.Key)) sb.AppendLine($"  {kv.Key}  <-  {kv.Value}");
        File.WriteAllText(Path.Combine(OutDir, "00-assemblies.txt"), sb.ToString());
    }

    static void DumpNamespaceIndex(List<Assembly> loaded)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Namespace index: <assembly>  <namespace>  <public type count>/<total type count>");
        sb.AppendLine();
        foreach (var a in loaded)
        {
            var groups = SafeTypes(a).Where(t => !SafeIsNested(t))
                .GroupBy(t => SafeNamespace(t) ?? "<global>")
                .OrderBy(g => g.Key, StringComparer.Ordinal);
            sb.AppendLine($"=== {a.GetName().Name} ===");
            foreach (var g in groups)
                sb.AppendLine($"  {g.Key}   {g.Count(SafeIsPublic)}/{g.Count()}");
            sb.AppendLine();
        }
        File.WriteAllText(Path.Combine(OutDir, "01-namespaces.txt"), sb.ToString());
    }

    static void DumpTypeIndex(List<Assembly> loaded)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Type index: <assembly> | <kind> | <full type name> | base: <base type>");
        foreach (var a in loaded)
        {
            var an = a.GetName().Name;
            foreach (var t in SafeTypes(a).OrderBy(t => t.FullName ?? "", StringComparer.Ordinal))
                sb.AppendLine($"{an} | {Kind(t)} | {t.FullName} | base: {SafeName(BaseOf(t))}");
        }
        File.WriteAllText(Path.Combine(OutDir, "02-types-index.txt"), sb.ToString());
    }

    // Two reverse indexes that plain reflection can't answer: who derives from X, and who
    // implements interface I. Both are needed constantly when mapping a game's class tree.
    static void DumpHierarchy(List<Assembly> loaded)
    {
        var children = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var implementors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var all = new List<Type>();
        foreach (var a in loaded) all.AddRange(SafeTypes(a));

        foreach (var t in all)
        {
            if (SafeName(t) == "?") continue;
            var b = BaseOf(t);
            if (b != null)
            {
                var bn = SafeName(b);
                if (!children.TryGetValue(bn, out var l)) children[bn] = l = new List<string>();
                l.Add(t.FullName ?? t.Name);
            }
            foreach (var i in Safe(() => t.GetInterfaces()))
            {
                var inn = SafeName(i);
                if (!implementors.TryGetValue(inn, out var l)) implementors[inn] = l = new List<string>();
                l.Add(t.FullName ?? t.Name);
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("# Direct-subclass index: '<base type>' followed by indented direct subclasses");
        sb.AppendLine();
        foreach (var kv in children.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            sb.AppendLine($"BASE {kv.Key}   ({kv.Value.Count} direct subclasses)");
            foreach (var c in kv.Value.OrderBy(x => x, StringComparer.Ordinal)) sb.AppendLine("    " + c);
        }
        File.WriteAllText(Path.Combine(OutDir, "03-subclasses.txt"), sb.ToString());

        sb.Clear();
        sb.AppendLine("# Interface implementor index (includes inherited interface implementations)");
        sb.AppendLine();
        foreach (var kv in implementors.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            sb.AppendLine($"IFACE {kv.Key}   ({kv.Value.Count} implementors)");
            foreach (var c in kv.Value.OrderBy(x => x, StringComparer.Ordinal)) sb.AppendLine("    " + c);
        }
        File.WriteAllText(Path.Combine(OutDir, "04-interface-implementors.txt"), sb.ToString());
    }

    // ------------------------------------------------------------ member dump

    static void DumpPerNamespace(List<Assembly> loaded)
    {
        // group all types (including nested, emitted under their declaring type) by root namespace bucket
        var buckets = new Dictionary<string, List<Type>>(StringComparer.Ordinal);
        foreach (var a in loaded)
            foreach (var t in SafeTypes(a))
            {
                if (SafeIsNested(t)) continue;
                var ns = SafeNamespace(t) ?? "<global>";
                if (!buckets.TryGetValue(ns, out var l)) buckets[ns] = l = new List<Type>();
                l.Add(t);
            }

        Directory.CreateDirectory(Path.Combine(OutDir, "ns"));
        foreach (var kv in buckets)
        {
            var file = "ns-" + Sanitize(kv.Key) + ".txt";
            var sb = new StringBuilder();
            sb.AppendLine($"################ NAMESPACE {kv.Key} ################");
            sb.AppendLine($"# types: {kv.Value.Count}");
            sb.AppendLine();
            foreach (var t in kv.Value.OrderBy(t => t.Name, StringComparer.Ordinal))
            {
                try { DumpType(sb, t, 0); }
                catch (Exception ex) { sb.AppendLine($"=== {t.FullName} === !! DUMP FAILED: {ex.GetType().Name}: {ex.Message}"); }
            }
            File.WriteAllText(Path.Combine(OutDir, "ns", file), sb.ToString());
        }
    }

    static string Sanitize(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Replace('<', '_').Replace('>', '_');
    }

    static void DumpType(StringBuilder sb, Type t, int depth)
    {
        string pad = new string(' ', depth * 2);
        sb.AppendLine($"{pad}=== {Kind(t)} {t.FullName} ===");

        var attrs = AttrList(SafeAttrs(t));
        if (attrs.Count > 0) sb.AppendLine($"{pad}  attrs: {string.Join(" ", attrs)}");

        var bt = BaseOf(t);
        if (bt != null) sb.AppendLine($"{pad}  base: {SafeName(bt)}");
        Type[] ifaces;
        try { ifaces = t.GetInterfaces(); } catch { ifaces = Array.Empty<Type>(); }
        if (ifaces.Length > 0) sb.AppendLine($"{pad}  implements: {string.Join(", ", ifaces.Select(SafeName).OrderBy(x => x, StringComparer.Ordinal))}");
        if (t.IsGenericTypeDefinition) sb.AppendLine($"{pad}  generic params: {string.Join(", ", t.GetGenericArguments().Select(g => g.Name))}");

        const BindingFlags BF = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                BindingFlags.Static | BindingFlags.DeclaredOnly;

        if (t.IsEnum)
        {
            sb.AppendLine($"{pad}  ENUM (underlying {SafeName(SafeEnumUnderlying(t))}):");
            foreach (var f in Safe(() => t.GetFields(BF)).Where(f => f.IsLiteral))
            {
                object v = null;
                try { v = f.GetRawConstantValue(); } catch { }
                sb.AppendLine($"{pad}    {f.Name} = {v}");
            }
            sb.AppendLine();
            foreach (var nt in Safe(() => t.GetNestedTypes(BF))) DumpType(sb, nt, depth + 1);
            return;
        }

        // ---- fields
        var fields = Safe(() => t.GetFields(BF)).ToList();
        var realFields = fields.Where(f => !IsInteropNoise(f.Name)).ToList();
        var noiseFields = fields.Where(f => IsInteropNoise(f.Name)).ToList();
        if (realFields.Count > 0)
        {
            sb.AppendLine($"{pad}  --- Fields ({realFields.Count}) ---");
            foreach (var f in realFields)
            {
                var fa = AttrList(SafeAttrs(f));
                string cv = "";
                if (f.IsLiteral) { try { cv = " = " + Fmt(f.GetRawConstantValue()); } catch { } }
                sb.AppendLine($"{pad}    {FieldMods(f)}{SafeName(SafeFieldType(f))} {f.Name}{cv}{(fa.Count > 0 ? "   " + string.Join(" ", fa) : "")}");
            }
        }

        // ---- properties (Il2CppInterop exposes IL2CPP instance fields as properties)
        var props = Safe(() => t.GetProperties(BF)).ToList();
        if (props.Count > 0)
        {
            sb.AppendLine($"{pad}  --- Properties ({props.Count}) ---");
            foreach (var p in props)
            {
                var g = Safe2(() => p.GetGetMethod(true));
                var s = Safe2(() => p.GetSetMethod(true));
                string acc = (g != null ? Vis(g) + "get; " : "") + (s != null ? Vis(s) + "set; " : "");
                string idx = "";
                var ip = Safe(() => p.GetIndexParameters()).ToArray();
                if (ip.Length > 0) idx = "[" + string.Join(", ", ip.Select(x => SafeName(SafeParamType(x)) + " " + x.Name)) + "]";
                string stat = (g != null && g.IsStatic) || (s != null && s.IsStatic) ? "static " : "";
                var pa = AttrList(SafeAttrs(p));
                sb.AppendLine($"{pad}    {stat}{SafeName(SafePropType(p))} {p.Name}{idx} {{ {acc}}}{(pa.Count > 0 ? "   " + string.Join(" ", pa) : "")}");
            }
        }

        // ---- events
        var evts = Safe(() => t.GetEvents(BF)).ToList();
        if (evts.Count > 0)
        {
            sb.AppendLine($"{pad}  --- Events ({evts.Count}) ---");
            foreach (var e in evts)
                sb.AppendLine($"{pad}    event {SafeName(SafeEvtType(e))} {e.Name}");
        }

        // ---- constructors
        var ctors = Safe(() => t.GetConstructors(BF)).ToList();
        if (ctors.Count > 0)
        {
            sb.AppendLine($"{pad}  --- Constructors ({ctors.Count}) ---");
            foreach (var c in ctors) sb.AppendLine($"{pad}    {Vis(c)}.ctor({Params(c)})");
        }

        // ---- methods
        var methods = Safe(() => t.GetMethods(BF))
            .Where(m => !IsAccessor(m, props, evts))
            .ToList();
        if (methods.Count > 0)
        {
            sb.AppendLine($"{pad}  --- Methods ({methods.Count}) ---");
            foreach (var m in methods.OrderBy(m => m.Name, StringComparer.Ordinal))
            {
                var ma = AttrList(SafeAttrs(m));
                sb.AppendLine($"{pad}    {MethodMods(m)}{SafeName(SafeRet(m))} {m.Name}{GenArgs(m)}({Params(m)}){(ma.Count > 0 ? "   " + string.Join(" ", ma) : "")}");
            }
        }

        if (noiseFields.Count > 0)
            sb.AppendLine($"{pad}  [interop-internal fields omitted: {noiseFields.Count}]");

        sb.AppendLine();

        foreach (var nt in Safe(() => t.GetNestedTypes(BF)).OrderBy(x => x.Name, StringComparer.Ordinal))
            DumpType(sb, nt, depth + 1);
    }

    static bool IsAccessor(MethodInfo m, List<PropertyInfo> props, List<EventInfo> evts)
    {
        if (!m.IsSpecialName) return false;
        var n = m.Name;
        return n.StartsWith("get_") || n.StartsWith("set_") || n.StartsWith("add_") ||
               n.StartsWith("remove_") || n.StartsWith("raise_");
    }

    static bool IsInteropNoise(string n) =>
        n.StartsWith("NativeMethodInfoPtr_", StringComparison.Ordinal) ||
        n.StartsWith("NativeFieldInfoPtr_", StringComparison.Ordinal) ||
        n.StartsWith("<>", StringComparison.Ordinal);

    // ------------------------------------------------------------- formatting

    static string Kind(Type t)
    {
        var sb = new StringBuilder();
        try { sb.Append(t.IsPublic || t.IsNestedPublic ? "public " : t.IsNestedPrivate ? "private " : t.IsNestedFamily ? "protected " : "internal "); }
        catch { sb.Append("? "); }
        if (t.IsEnum) sb.Append("enum");
        else if (t.IsInterface) sb.Append("interface");
        else if (SafeIsValueType(t)) sb.Append("struct");
        else
        {
            if (t.IsAbstract && t.IsSealed) sb.Append("static ");
            else if (t.IsAbstract) sb.Append("abstract ");
            else if (t.IsSealed) sb.Append("sealed ");
            sb.Append("class");
        }
        return sb.ToString();
    }

    static string Vis(MethodBase m) =>
        m.IsPublic ? "public " : m.IsFamily ? "protected " : m.IsAssembly ? "internal " :
        m.IsFamilyOrAssembly ? "protected internal " : "private ";

    static string MethodMods(MethodInfo m)
    {
        var sb = new StringBuilder(Vis(m));
        if (m.IsStatic) sb.Append("static ");
        if (m.IsAbstract) sb.Append("abstract ");
        else if (m.IsVirtual && !m.IsFinal) sb.Append("virtual ");
        else if (m.IsVirtual && m.IsFinal) sb.Append("sealed-override ");
        return sb.ToString();
    }

    static string FieldMods(FieldInfo f)
    {
        var sb = new StringBuilder();
        sb.Append(f.IsPublic ? "public " : f.IsFamily ? "protected " : f.IsAssembly ? "internal " : "private ");
        if (f.IsLiteral) sb.Append("const ");
        else if (f.IsStatic) sb.Append("static ");
        if (f.IsInitOnly) sb.Append("readonly ");
        return sb.ToString();
    }

    static string GenArgs(MethodInfo m)
    {
        if (!m.IsGenericMethodDefinition) return "";
        try { return "<" + string.Join(", ", m.GetGenericArguments().Select(a => a.Name)) + ">"; }
        catch { return "<?>"; }
    }

    static string Params(MethodBase m)
    {
        ParameterInfo[] ps;
        try { ps = m.GetParameters(); } catch { return "?"; }
        return string.Join(", ", ps.Select(p =>
        {
            var t = SafeParamType(p);
            string mod = "";
            if (t != null && t.IsByRef) mod = p.IsOut ? "out " : p.IsIn ? "in " : "ref ";
            string def = "";
            if (p.HasDefaultValue) { try { def = " = " + Fmt(p.RawDefaultValue); } catch { } }
            return $"{mod}{SafeName(t)} {p.Name}{def}";
        }));
    }

    static string Fmt(object o) => o == null ? "null" : o is string s ? "\"" + s + "\"" : o.ToString();

    static List<string> AttrList(IList<CustomAttributeData> data)
    {
        var res = new List<string>();
        if (data == null) return res;
        foreach (var d in data)
        {
            string n;
            try { n = d.AttributeType.Name; } catch { continue; }
            if (n.EndsWith("Attribute")) n = n.Substring(0, n.Length - 9);
            // keep only attributes that carry real information
            if (n is "CompilerGenerated" or "DebuggerHidden" or "DebuggerBrowsable" or "Obfuscation" or
                "IsReadOnly" or "Nullable" or "NullableContext" or "Extension" or "DefaultMember" or
                "CallerCount" or "CachedScanResults") continue;
            var args = new List<string>();
            try
            {
                foreach (var a in d.ConstructorArguments) args.Add(FmtAttrArg(a));
                foreach (var a in d.NamedArguments) args.Add(a.MemberName + "=" + FmtAttrArg(a.TypedValue));
            }
            catch { }
            res.Add(args.Count > 0 ? $"[{n}({string.Join(", ", args)})]" : $"[{n}]");
        }
        return res;
    }

    static string FmtAttrArg(CustomAttributeTypedArgument a)
    {
        try
        {
            if (a.Value is IList<CustomAttributeTypedArgument> l)
                return "{" + string.Join(", ", l.Select(FmtAttrArg)) + "}";
            if (a.Value is Type t) return "typeof(" + SafeName(t) + ")";
            return Fmt(a.Value);
        }
        catch { return "?"; }
    }

    static string SafeName(Type t)
    {
        if (t == null) return "?";
        try
        {
            if (t.IsByRef) return SafeName(t.GetElementType()) + "&";
            if (t.IsPointer) return SafeName(t.GetElementType()) + "*";
            if (t.IsArray) return SafeName(t.GetElementType()) + "[" + new string(',', t.GetArrayRank() - 1) + "]";
            if (t.IsGenericType)
            {
                var name = t.GetGenericTypeDefinition().FullName ?? t.Name;
                int i = name.IndexOf('`');
                if (i > 0) name = name.Substring(0, i);
                return name + "<" + string.Join(", ", t.GetGenericArguments().Select(SafeName)) + ">";
            }
            return t.FullName ?? t.Name;
        }
        catch { return t.Name; }
    }

    // Some assemblies in this set (notably Il2Cppmscorlib) have metadata that makes
    // MetadataLoadContext throw while resolving declaring types, so even IsNested needs a guard.
    static bool SafeIsNested(Type t) { try { return t.IsNested; } catch { return false; } }
    static bool SafeIsPublic(Type t) { try { return t.IsPublic; } catch { return false; } }
    static string SafeNamespace(Type t) { try { return t.Namespace; } catch { return null; } }
    static Type BaseOf(Type t) { try { return t.BaseType; } catch { return null; } }
    static bool SafeIsValueType(Type t) { try { return t.IsValueType; } catch { return false; } }
    static Type SafeEnumUnderlying(Type t) { try { return t.GetEnumUnderlyingType(); } catch { return null; } }
    static Type SafeFieldType(FieldInfo f) { try { return f.FieldType; } catch { return null; } }
    static Type SafePropType(PropertyInfo p) { try { return p.PropertyType; } catch { return null; } }
    static Type SafeEvtType(EventInfo e) { try { return e.EventHandlerType; } catch { return null; } }
    static Type SafeParamType(ParameterInfo p) { try { return p.ParameterType; } catch { return null; } }
    static Type SafeRet(MethodInfo m) { try { return m.ReturnType; } catch { return null; } }
    static IList<CustomAttributeData> SafeAttrs(MemberInfo m) { try { return m.GetCustomAttributesData(); } catch { return null; } }
    static IList<CustomAttributeData> SafeAttrs(ParameterInfo m) { try { return m.GetCustomAttributesData(); } catch { return null; } }

    static IEnumerable<T> Safe<T>(Func<T[]> f) { try { return f() ?? Array.Empty<T>(); } catch { return Array.Empty<T>(); } }
    static T Safe2<T>(Func<T> f) where T : class { try { return f(); } catch { return null; } }
}

using System.Runtime.CompilerServices;
using Il2CppInterop.Runtime;

namespace Expansions.Core.Diagnostics;

/// <summary>
/// Answers "is this static writable, or did IL2CPP inline it as a const?" by reading the field's
/// metadata attributes out of the running IL2CPP runtime. No write is attempted, and none is needed.
/// <para>
/// Il2CppInterop projects every IL2CPP field — including <c>const</c> ones — as a CLR static property
/// whose setter is a bare <c>il2cpp_field_static_set_value</c>. That native call has no guard: unlike
/// Mono's <c>mono_field_static_set_value</c>, which refuses a literal field outright, IL2CPP computes
/// <c>klass-&gt;static_fields + field-&gt;offset</c> and memcpys into it. A <c>const</c> contributes no
/// storage, so on a class whose statics are <em>all</em> const that base pointer is null and the write
/// lands at a near-null address: an access violation that kills the process outright, with no managed
/// exception to catch. Reading the same field is safe — IL2CPP returns the value from metadata.
/// </para>
/// <para>
/// So the flags are both the answer and the safety gate: <c>LITERAL</c> means const-inlined, which
/// means the mod's write would be a silent no-op at best and a crash at worst.
/// </para>
/// </summary>
public sealed class Il2CppFieldFacts
{
    // ECMA-335 II.23.1.5 field attributes, which is what il2cpp_field_get_flags returns verbatim.
    private const int AttributeStatic = 0x0010;
    private const int AttributeInitOnly = 0x0020;
    private const int AttributeLiteral = 0x0040;
    private const int AttributeHasDefault = 0x8000;

    /// <summary>Stops a corrupt field iterator turning into an infinite loop.</summary>
    private const int FieldScanCap = 4096;

    private static readonly Dictionary<string, int> MutableStaticCounts = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Il2CppFieldFacts> Cache = new(StringComparer.Ordinal);
    private static readonly object Gate = new();

    private Il2CppFieldFacts(
        Il2CppFieldVerdict verdict,
        string failure,
        int flags,
        uint offset,
        int declaringMutableStatics)
    {
        Verdict = verdict;
        Failure = failure;
        Flags = flags;
        Offset = offset;
        DeclaringMutableStatics = declaringMutableStatics;
    }

    public Il2CppFieldVerdict Verdict { get; }

    /// <summary>Empty unless <see cref="Verdict"/> is <see cref="Il2CppFieldVerdict.Unknown"/>.</summary>
    public string Failure { get; }

    public int Flags { get; }

    public uint Offset { get; }

    /// <summary>
    /// How many non-const statics the declaring class has. Zero means IL2CPP allocated it no static
    /// storage at all, so <em>any</em> static write to that class dereferences null. Negative means
    /// the field table could not be walked, which is treated the same way as zero.
    /// </summary>
    public int DeclaringMutableStatics { get; }

    /// <summary>False when the declaring class has no static-field block, or we could not tell.</summary>
    public bool DeclaringClassHasStaticStorage => DeclaringMutableStatics > 0;

    public bool IsStatic => (Flags & AttributeStatic) != 0;

    public bool IsLiteral => (Flags & AttributeLiteral) != 0;

    public bool IsInitOnly => (Flags & AttributeInitOnly) != 0;

    public bool HasDefault => (Flags & AttributeHasDefault) != 0;

    /// <summary>
    /// True only for a genuine mutable static on a class that owns static storage. Everything else —
    /// a const, an unresolved field, an instance member — must never be written.
    /// </summary>
    public bool IsSafeToWrite => Verdict == Il2CppFieldVerdict.MutableStatic && DeclaringClassHasStaticStorage;

    /// <summary>Flag names, for the report. Empty string when nothing resolved.</summary>
    public string FlagNames
    {
        get
        {
            if (Verdict == Il2CppFieldVerdict.Unknown)
                return "-";

            var names = new List<string>(4);
            if (IsStatic) names.Add("STATIC");
            if (IsLiteral) names.Add("LITERAL");
            if (IsInitOnly) names.Add("INIT_ONLY");
            if (HasDefault) names.Add("HAS_DEFAULT");

            return names.Count == 0 ? "none" : string.Join("+", names);
        }
    }

    /// <summary>
    /// Reads <paramref name="memberName"/>'s native attributes off <paramref name="interopType"/>.
    /// Never throws; an unresolvable field comes back as <see cref="Il2CppFieldVerdict.Unknown"/>
    /// with the reason in <see cref="Failure"/>.
    /// </summary>
    public static Il2CppFieldFacts Inspect(Type interopType, string memberName)
    {
        if (interopType is null)
            return Unknown("no type");

        var key = (interopType.FullName ?? interopType.Name) + "|" + memberName;

        lock (Gate)
        {
            if (Cache.TryGetValue(key, out var cached))
                return cached;
        }

        var facts = Resolve(interopType, memberName);

        lock (Gate)
            Cache[key] = facts;

        return facts;
    }

    private static Il2CppFieldFacts Resolve(Type interopType, string memberName)
    {
        try
        {
            // The native class pointer is published by the interop type's static constructor, which
            // has not necessarily run just because we hold a Type handle.
            RuntimeHelpers.RunClassConstructor(interopType.TypeHandle);

            var classPointer = Il2CppClassPointerStore.GetNativeClassPointer(interopType);
            if (classPointer == IntPtr.Zero)
                return Unknown($"'{interopType.Name}' has no native class pointer");

            var fieldPointer = IL2CPP.GetIl2CppField(classPointer, memberName);
            if (fieldPointer == IntPtr.Zero)
                return Unknown($"'{interopType.Name}.{memberName}' is not a native field on this build");

            var flags = IL2CPP.il2cpp_field_get_flags(fieldPointer);
            var offset = IL2CPP.il2cpp_field_get_offset(fieldPointer);
            var mutableStatics = CountMutableStatics(interopType, classPointer);

            var verdict = (flags & AttributeStatic) == 0
                ? Il2CppFieldVerdict.InstanceField
                : (flags & AttributeLiteral) != 0
                    ? Il2CppFieldVerdict.ConstInlined
                    : Il2CppFieldVerdict.MutableStatic;

            return new Il2CppFieldFacts(verdict, string.Empty, flags, offset, mutableStatics);
        }
        catch (Exception ex)
        {
            return Unknown(GameReflection.Unwrap(ex));
        }
    }

    /// <summary>One line saying what the verdict means for a mod that wants to write this value.</summary>
    public string Explain(string label) => Verdict switch
    {
        Il2CppFieldVerdict.MutableStatic when DeclaringClassHasStaticStorage =>
            "real mutable static — a write sticks",
        Il2CppFieldVerdict.MutableStatic when DeclaringMutableStatics == 0 =>
            "static, but its class owns no static-field block; treat as unwritable",
        Il2CppFieldVerdict.MutableStatic =>
            "static, but its class's field table could not be walked, so a write is not provably safe",
        Il2CppFieldVerdict.ConstInlined =>
            $"C# `const` — IL2CPP baked it into every call site, so `{label}` cannot be tuned at runtime" +
            (DeclaringMutableStatics == 0
                ? "; writing it would fault, because its class has no static-field block to write into"
                : "; writing it lands on a neighbouring static instead"),
        Il2CppFieldVerdict.InstanceField => "instance field, not a static",
        _ => Failure.Length > 0 ? Failure : "unresolved",
    };

    private static Il2CppFieldFacts Unknown(string failure) =>
        new(Il2CppFieldVerdict.Unknown, failure, 0, 0, -1);

    /// <summary>
    /// Counts the class's non-const statics. IL2CPP only allocates a static-field block when that
    /// count is greater than zero, which is what makes a write to an all-const class fatal.
    /// </summary>
    private static int CountMutableStatics(Type interopType, IntPtr classPointer)
    {
        var key = interopType.FullName ?? interopType.Name;

        lock (Gate)
        {
            if (MutableStaticCounts.TryGetValue(key, out var cached))
                return cached;
        }

        var count = 0;

        try
        {
            var iterator = IntPtr.Zero;
            for (var scanned = 0; scanned < FieldScanCap; scanned++)
            {
                var field = IL2CPP.il2cpp_class_get_fields(classPointer, ref iterator);
                if (field == IntPtr.Zero)
                    break;

                var flags = IL2CPP.il2cpp_field_get_flags(field);
                if ((flags & AttributeStatic) != 0 && (flags & AttributeLiteral) == 0)
                    count++;
            }
        }
        catch
        {
            count = -1;
        }

        lock (Gate)
            MutableStaticCounts[key] = count;

        return count;
    }
}

public enum Il2CppFieldVerdict
{
    /// <summary>The field could not be resolved in the running IL2CPP metadata.</summary>
    Unknown = 0,

    /// <summary>A real static field with storage. Writable.</summary>
    MutableStatic = 1,

    /// <summary>A C# <c>const</c>. IL2CPP inlined it; there is nothing to write.</summary>
    ConstInlined = 2,

    /// <summary>Resolved, but it is an instance field.</summary>
    InstanceField = 3,
}

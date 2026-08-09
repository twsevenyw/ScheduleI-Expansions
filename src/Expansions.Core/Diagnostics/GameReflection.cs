using System.Collections;
using System.Globalization;
using System.Reflection;

namespace Expansions.Core.Diagnostics;

/// <summary>
/// Late-bound access to game types.
/// <para>
/// Nothing under <c>Diagnostics</c> holds a compile-time reference to <c>Assembly-CSharp</c>,
/// FishNet or Steamworks. A probe whose target was renamed, moved or removed by a game patch has to
/// be able to report <see cref="ProbeStatus.NotFound"/> — it must never stop
/// <c>Expansions.Core.dll</c> from loading, because that would take all three mods down with it.
/// </para>
/// <para>
/// Il2CppInterop projects IL2CPP statics as static CLR <em>properties</em> and IL2CPP fields as CLR
/// properties too, so every lookup here checks properties and fields, and walks the base chain by
/// hand: <c>Singleton&lt;T&gt;.Instance</c> lives on the open generic base, where
/// <c>FlattenHierarchy</c> is not dependable.
/// </para>
/// </summary>
public static class GameReflection
{
    private const BindingFlags Declared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly |
        BindingFlags.Static | BindingFlags.Instance;

    /// <summary>Interop assemblies load lazily, so a namespace root may need an explicit load.</summary>
    private static readonly (string Prefix, string Assembly)[] AssemblyHints =
    {
        ("Il2CppScheduleOne.", "Assembly-CSharp"),
        ("Il2CppFishNet.", "Il2CppFishNet.Runtime"),
        ("Il2CppSteamworks.", "Il2Cppcom.rlabrecque.steamworks.net"),
        ("Il2CppPathfinding.", "Il2CppAstarPathfindingProject"),
        ("Il2CppTMPro.", "Unity.TextMeshPro"),
        ("Il2CppSystem.", "Il2Cppmscorlib"),
    };

    private static readonly Dictionary<string, Type?> TypeCache = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, MemberInfo?> MemberCache = new(StringComparer.Ordinal);
    private static readonly HashSet<string> AmbiguityWarnings = new(StringComparer.Ordinal);
    private static readonly object Gate = new();

    /// <summary>Resolves a type by full name across every loaded assembly. Null means "not on this build".</summary>
    public static Type? FindType(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return null;

        lock (Gate)
        {
            if (TypeCache.TryGetValue(fullName, out var cached))
                return cached;
        }

        var found = Resolve(fullName);

        lock (Gate)
            TypeCache[fullName] = found;

        return found;
    }

    /// <summary>True when <paramref name="fullName"/> resolves. Handy for a one-line presence check.</summary>
    public static bool TypeExists(string fullName) => FindType(fullName) is not null;

    public static bool TryReadStatic(string typeName, string memberName, out object? value, out string failure)
    {
        var type = FindType(typeName);
        if (type is null)
        {
            value = null;
            failure = $"type '{typeName}' not found";
            return false;
        }

        return TryReadStatic(type, memberName, out value, out failure);
    }

    public static bool TryReadStatic(Type type, string memberName, out object? value, out string failure)
    {
        value = null;

        var member = FindMember(type, memberName);
        if (member is null)
        {
            failure = $"'{type.Name}.{memberName}' not found";
            return false;
        }

        if (!IsStatic(member))
        {
            failure = $"'{type.Name}.{memberName}' is an instance member";
            return false;
        }

        try
        {
            value = ReadMember(member, null);
            failure = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            failure = Unwrap(ex);
            return false;
        }
    }

    /// <summary>
    /// Writes a static, refusing anything that would corrupt memory rather than merely fail.
    /// <para>
    /// The load-bearing guard is the <c>LITERAL</c> check. Il2CppInterop projects an IL2CPP
    /// <c>const</c> as an ordinary read/write CLR property whose setter is a bare
    /// <c>il2cpp_field_static_set_value</c>, and unlike Mono's equivalent that native call does not
    /// refuse a literal field — it writes to <c>static_fields + offset</c> regardless, which on a
    /// class whose statics are all const is a null base pointer and an instant access violation. No
    /// managed <c>catch</c> can save the process from that, so it has to be refused before the call.
    /// </para>
    /// </summary>
    public static bool TryWriteStatic(Type type, string memberName, object? value, out string failure)
    {
        var member = FindMember(type, memberName);
        if (member is null)
        {
            failure = $"'{type.Name}.{memberName}' not found";
            return false;
        }

        if (!IsStatic(member))
        {
            failure = $"'{type.Name}.{memberName}' is an instance member";
            return false;
        }

        var facts = Il2CppFieldFacts.Inspect(type, memberName);
        if (facts.Verdict == Il2CppFieldVerdict.ConstInlined)
        {
            failure = $"'{type.Name}.{memberName}' is a C# const that IL2CPP inlined; writing it would fault, not fail";
            return false;
        }

        try
        {
            switch (member)
            {
                case PropertyInfo property when property.GetSetMethod(nonPublic: true) is not null:
                    property.SetValue(null, value);
                    break;
                case PropertyInfo:
                    failure = $"'{type.Name}.{memberName}' has no setter";
                    return false;
                case FieldInfo field when field.IsLiteral || field.IsInitOnly:
                    failure = $"'{type.Name}.{memberName}' is a compile-time constant field";
                    return false;
                case FieldInfo field:
                    field.SetValue(null, value);
                    break;
                default:
                    failure = $"'{type.Name}.{memberName}' is not settable";
                    return false;
            }

            failure = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            failure = Unwrap(ex);
            return false;
        }
    }

    /// <summary>
    /// Writes an instance member.
    /// <para>
    /// Unlike <see cref="TryWriteStatic"/> this needs no <c>LITERAL</c> guard: the const-inlining
    /// hazard is specific to static storage, and an instance field always has a real offset inside the
    /// object. It still refuses read-only members rather than letting the setter throw.
    /// </para>
    /// </summary>
    public static bool TryWrite(object? instance, string memberName, object? value, out string failure)
    {
        if (instance is null)
        {
            failure = "instance is null";
            return false;
        }

        var type = instance.GetType();
        var member = FindMember(type, memberName);
        if (member is null)
        {
            failure = $"'{type.Name}.{memberName}' not found";
            return false;
        }

        if (IsStatic(member))
            return TryWriteStatic(type, memberName, value, out failure);

        try
        {
            switch (member)
            {
                case PropertyInfo property when property.GetSetMethod(nonPublic: true) is not null:
                    property.SetValue(instance, value);
                    break;
                case PropertyInfo:
                    failure = $"'{type.Name}.{memberName}' has no setter";
                    return false;
                case FieldInfo { IsLiteral: false, IsInitOnly: false } field:
                    field.SetValue(instance, value);
                    break;
                default:
                    failure = $"'{type.Name}.{memberName}' is not settable";
                    return false;
            }

            failure = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            failure = Unwrap(ex);
            return false;
        }
    }

    /// <summary>
    /// Reads an instance member (falling back to a static one of the same name).
    /// <para>
    /// ⚠ False comfort: the <c>bool</c> return only covers managed exceptions. Invoking an IL2CPP
    /// property getter on a dead or half-constructed native object can raise
    /// <c>AccessViolationException</c>, which is <em>not</em> catchable and kills the process.
    /// Only call this when the object's liveness is already proven (or the call is not on a
    /// save/load / Awake / ShouldSave hot path). Never use it to decide mod ownership of a game
    /// object — track ownership in a managed set keyed on the native pointer instead.
    /// </para>
    /// </summary>
    public static bool TryRead(object? instance, string memberName, out object? value, out string failure)
    {
        value = null;

        if (instance is null)
        {
            failure = "instance is null";
            return false;
        }

        var member = FindMember(instance.GetType(), memberName);
        if (member is null)
        {
            failure = $"'{instance.GetType().Name}.{memberName}' not found";
            return false;
        }

        try
        {
            value = ReadMember(member, IsStatic(member) ? null : instance);
            failure = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            failure = Unwrap(ex);
            return false;
        }
    }

    /// <summary>
    /// Convenience for a dotted chain, e.g. <c>"NPCData.Appearance.AvatarSettings"</c>.
    /// Same AV risk as <see cref="TryRead"/> — each hop can hard-fault on a bad native.
    /// </summary>
    public static bool TryReadPath(object? root, string path, out object? value, out string failure)
    {
        value = root;
        failure = string.Empty;

        foreach (var step in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!TryRead(value, step, out value, out failure))
                return false;

            if (value is null)
            {
                failure = $"'{step}' is null";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Invokes a method, choosing the overload that actually fits <paramref name="arguments"/>.
    /// <para>
    /// Arity alone is not enough to identify a method here. <c>NPCMovement.Warp</c> takes either a
    /// <c>Transform</c> or a <c>Vector3</c>, and <c>Console.SubmitCommand</c> takes either a
    /// <c>string</c> or a <c>List&lt;string&gt;</c> — one argument each. Picking by arity lands on
    /// whichever the runtime happened to list first and then throws inside <c>Invoke</c>, so the
    /// runtime types of the arguments are used to disambiguate.
    /// </para>
    /// </summary>
    public static bool TryInvoke(
        Type type,
        object? instance,
        string methodName,
        object?[] arguments,
        out object? result,
        out string failure)
    {
        result = null;

        var method = FindMethodForArguments(type, methodName, arguments);
        if (method is null)
        {
            failure = $"'{type.Name}.{methodName}' with {arguments.Length} argument(s) not found";
            return false;
        }

        try
        {
            result = method.Invoke(method.IsStatic ? null : instance, arguments);
            failure = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            failure = Unwrap(ex);
            return false;
        }
    }

    /// <summary>
    /// Invokes the overload with exactly <paramref name="parameterTypes"/>. Use this whenever the
    /// signature is known and the arguments could be null or of a convertible-but-different type,
    /// which is where <see cref="TryInvoke"/>'s inference has nothing to work with.
    /// </summary>
    public static bool TryInvokeExact(
        Type type,
        object? instance,
        string methodName,
        Type[] parameterTypes,
        object?[] arguments,
        out object? result,
        out string failure)
    {
        result = null;

        var method = FindMethod(type, methodName, parameterTypes);
        if (method is null)
        {
            failure =
                $"'{type.Name}.{methodName}({string.Join(", ", parameterTypes.Select(static t => t.Name))})' not found";
            return false;
        }

        try
        {
            result = method.Invoke(method.IsStatic ? null : instance, arguments);
            failure = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            failure = Unwrap(ex);
            return false;
        }
    }

    /// <summary>
    /// A method by name and arity, for presence checks. Ambiguity is resolved by ordinal signature so
    /// two runs agree, and reported once per site — but a caller that needs a specific overload should
    /// be using <see cref="FindMethod(Type,string,Type[])"/>.
    /// </summary>
    public static MethodInfo? FindMethod(Type type, string methodName, int argumentCount)
    {
        var candidates = Candidates(type, methodName, argumentCount);
        if (candidates.Count == 0)
            return null;

        if (candidates.Count > 1)
            WarnAmbiguous(type, methodName, candidates);

        return candidates[0];
    }

    /// <summary>The overload whose parameters are exactly <paramref name="parameterTypes"/>.</summary>
    public static MethodInfo? FindMethod(Type type, string methodName, Type[] parameterTypes)
    {
        foreach (var candidate in Candidates(type, methodName, parameterTypes.Length))
        {
            var parameters = candidate.GetParameters();
            var matches = true;

            for (var i = 0; i < parameters.Length && matches; i++)
                matches = parameters[i].ParameterType == parameterTypes[i];

            if (matches)
                return candidate;
        }

        // Second pass: a base-class or interface parameter still accepts the argument type, which is
        // what makes `Log(Il2CppSystem.Object)` reachable when the caller only knows it has a string.
        foreach (var candidate in Candidates(type, methodName, parameterTypes.Length))
        {
            var parameters = candidate.GetParameters();
            var matches = true;

            for (var i = 0; i < parameters.Length && matches; i++)
                matches = parameters[i].ParameterType.IsAssignableFrom(parameterTypes[i]);

            if (matches)
                return candidate;
        }

        return null;
    }

    /// <summary>
    /// Picks the overload that fits the argument values. Falls back to the arity match so this can
    /// never resolve fewer methods than matching on arity alone did.
    /// </summary>
    private static MethodInfo? FindMethodForArguments(Type type, string methodName, object?[] arguments)
    {
        var candidates = Candidates(type, methodName, arguments.Length);
        if (candidates.Count <= 1)
            return candidates.Count == 1 ? candidates[0] : null;

        MethodInfo? best = null;
        var bestScore = -1;

        // Candidates arrive most-derived first and signature-sorted within a type, so a strict >
        // keeps the first of any tie and the choice is the same on every run.
        foreach (var candidate in candidates)
        {
            var score = ScoreArguments(candidate, arguments);
            if (score > bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }

        if (bestScore >= 0)
            return best;

        // No overload can hold these arguments. Fall back to the arity pick rather than refusing: that
        // is what this did before, and Invoke's own exception is a better error than "not found".
        WarnAmbiguous(type, methodName, candidates);
        return candidates[0];
    }

    /// <summary>
    /// How well <paramref name="arguments"/> fit <paramref name="candidate"/>: 2 per exact type
    /// match, 1 per assignable or null argument, and -1 overall for an argument the parameter cannot
    /// hold at all.
    /// </summary>
    private static int ScoreArguments(MethodInfo candidate, object?[] arguments)
    {
        var parameters = candidate.GetParameters();
        var score = 0;

        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i].ParameterType;
            var argument = arguments[i];

            if (argument is null)
            {
                // A null cannot be handed to a value-type parameter, which is often the whole
                // difference between two overloads.
                if (parameter.IsValueType && Nullable.GetUnderlyingType(parameter) is null)
                    return -1;

                score += 1;
                continue;
            }

            var actual = argument.GetType();
            if (parameter == actual)
                score += 2;
            else if (parameter.IsAssignableFrom(actual))
                score += 1;
            else
                return -1;
        }

        return score;
    }

    /// <summary>
    /// Every method of this name and arity on the type and its bases, most-derived first and sorted by
    /// signature within each type. That ordering is what makes the choice reproducible:
    /// <c>Type.GetMethods</c> makes no promise about order, so "the first match" was previously whatever
    /// the runtime felt like that session.
    /// <para>
    /// An override and the declaration it overrides have the same signature, so the base copy is dropped
    /// — otherwise every virtual method would look ambiguous.
    /// </para>
    /// </summary>
    private static List<MethodInfo> Candidates(Type type, string methodName, int argumentCount)
    {
        var candidates = new List<MethodInfo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var level = new List<MethodInfo>();

        for (var current = type; current is not null; current = SafeBaseType(current))
        {
            MethodInfo[] methods;
            try
            {
                methods = current.GetMethods(Declared);
            }
            catch
            {
                // An unresolvable parameter or return type on one link of the chain must not stop the
                // search: the method we want may well be further up it.
                continue;
            }

            level.Clear();

            foreach (var candidate in methods)
            {
                if (candidate.Name != methodName)
                    continue;

                try
                {
                    if (candidate.GetParameters().Length != argumentCount)
                        continue;
                }
                catch
                {
                    continue;
                }

                level.Add(candidate);
            }

            level.Sort(static (a, b) => string.CompareOrdinal(Signature(a), Signature(b)));

            foreach (var candidate in level)
            {
                if (seen.Add(Signature(candidate)))
                    candidates.Add(candidate);
            }
        }

        return candidates;
    }

    /// <summary>
    /// Says once, at debug level, that an arity-only lookup had a real choice to make. It is a note for
    /// whoever wrote the call site, not a problem for the owner — hence debug rather than warn.
    /// </summary>
    private static void WarnAmbiguous(Type type, string methodName, List<MethodInfo> candidates)
    {
        var key = "ambiguous|" + type.FullName + "|" + methodName + "|" + candidates.Count;

        lock (Gate)
        {
            if (!AmbiguityWarnings.Add(key))
                return;
        }

        ExpansionHost.Log.Debug(
            $"'{type.Name}.{methodName}' has {candidates.Count} overloads of the same arity " +
            $"({string.Join(" / ", candidates.Select(Signature))}); using {Signature(candidates[0])}. " +
            "Name the parameter types if a different one was meant.");
    }

    private static string Signature(MethodInfo method)
    {
        try
        {
            return $"{method.Name}({string.Join(",", method.GetParameters().Select(static p => p.ParameterType.FullName ?? p.ParameterType.Name))})";
        }
        catch
        {
            return method.Name + "(?)";
        }
    }

    private static Type? SafeBaseType(Type type)
    {
        try
        {
            return type.BaseType;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Returns the live instance of a <c>Singleton&lt;T&gt;</c> / <c>NetworkSingleton&lt;T&gt;</c> /
    /// <c>PersistentSingleton&lt;T&gt;</c>. <c>InstanceExists</c> is consulted first: reading
    /// <c>Instance</c> when there isn't one logs a game-side error we would rather not cause.
    /// </summary>
    public static bool TryGetSingleton(string typeName, out object? instance, out string failure)
    {
        instance = null;

        var type = FindType(typeName);
        if (type is null)
        {
            failure = $"type '{typeName}' not found";
            return false;
        }

        if (TryReadStatic(type, "InstanceExists", out var exists, out _) && exists is bool present && !present)
        {
            failure = $"{type.Name}.InstanceExists is false (nothing has spawned it yet)";
            return false;
        }

        if (!TryReadStatic(type, "Instance", out instance, out failure))
            return false;

        if (IsPresent(instance))
            return true;

        instance = null;
        failure = $"{type.Name}.Instance is null";
        return false;
    }

    /// <summary>
    /// Materialises an IL2CPP or managed sequence.
    /// <para>
    /// <c>Il2CppSystem.Collections.Generic.List&lt;T&gt;</c> does <em>not</em> implement managed
    /// <c>IEnumerable</c> — its interface implementations are dotted-name IL2CPP members — so it has
    /// to be walked through <c>Count</c> plus the <c>Item</c> indexer.
    /// </para>
    /// </summary>
    public static IReadOnlyList<object?> Enumerate(object? collection, int cap = 8192)
    {
        var items = new List<object?>();
        if (collection is null || collection is string)
            return items;

        try
        {
            if (collection is IEnumerable managed)
            {
                foreach (var item in managed)
                {
                    items.Add(item);
                    if (items.Count >= cap)
                        break;
                }

                return items;
            }

            var type = collection.GetType();
            var countMember = FindMember(type, "Count") ?? FindMember(type, "Length");
            var indexer = type.GetMethod("get_Item", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(int) }, null);

            if (countMember is null || indexer is null)
                return items;

            if (ReadMember(countMember, collection) is not int count)
                return items;

            var limit = Math.Min(count, cap);
            for (var i = 0; i < limit; i++)
                items.Add(indexer.Invoke(collection, new object[] { i }));
        }
        catch
        {
            // A half-materialised list is still worth reporting; the caller says how many it got.
        }

        return items;
    }

    /// <summary>
    /// Null-safe for both worlds. Il2CppInterop hands back a managed <c>null</c> for a null native
    /// pointer, but a <em>destroyed</em> <c>UnityEngine.Object</c> arrives non-null with a dead
    /// native side, which only Unity's own equality operator catches.
    /// </summary>
    public static bool IsPresent(object? value)
    {
        if (value is null)
            return false;

        if (value is not UnityEngine.Object unityObject)
            return true;

        try
        {
            return unityObject != null;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>Short, culture-invariant, exception-proof rendering for report cells.</summary>
    public static string Format(object? value)
    {
        if (value is null)
            return "null";

        try
        {
            switch (value)
            {
                case string text:
                    return text.Length == 0 ? "\"\"" : text;
                case bool flag:
                    return flag ? "true" : "false";
                case float single:
                    return single.ToString("0.######", CultureInfo.InvariantCulture);
                case double dbl:
                    return dbl.ToString("0.######", CultureInfo.InvariantCulture);
                case IFormattable formattable:
                    return formattable.ToString(null, CultureInfo.InvariantCulture);
                case UnityEngine.Object unityObject:
                    return unityObject == null ? "null (destroyed)" : unityObject.name;
                default:
                    return value.ToString() ?? value.GetType().Name;
            }
        }
        catch (Exception ex)
        {
            return $"<threw {ex.GetType().Name}>";
        }
    }

    /// <summary>CLR type name of a value, or <c>-</c>. Used to record what a static actually is.</summary>
    public static string TypeNameOf(object? value) => value?.GetType().Name ?? "-";

    /// <summary>Unity's active scene name, or empty if the scene manager is unavailable.</summary>
    public static string ActiveSceneName()
    {
        try
        {
            return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    internal static string Unwrap(Exception exception)
    {
        var inner = exception is TargetInvocationException { InnerException: { } target } ? target : exception;
        return $"{inner.GetType().Name}: {inner.Message}";
    }

    private static Type? Resolve(string fullName)
    {
        var direct = SafeGetType(fullName);
        if (direct is not null)
            return direct;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var found = SafeGetType(assembly, fullName);
            if (found is not null)
                return found;
        }

        foreach (var (prefix, assemblyName) in AssemblyHints)
        {
            if (!fullName.StartsWith(prefix, StringComparison.Ordinal))
                continue;

            try
            {
                var found = SafeGetType(Assembly.Load(assemblyName), fullName);
                if (found is not null)
                    return found;
            }
            catch
            {
                // The interop assembly is not installed on this build; the caller reports NOT_FOUND.
            }
        }

        return null;
    }

    private static Type? SafeGetType(string fullName)
    {
        try
        {
            return Type.GetType(fullName, throwOnError: false);
        }
        catch
        {
            return null;
        }
    }

    private static Type? SafeGetType(Assembly assembly, string fullName)
    {
        try
        {
            return assembly.GetType(fullName, throwOnError: false);
        }
        catch
        {
            return null;
        }
    }

    private static MemberInfo? FindMember(Type type, string memberName)
    {
        var key = type.AssemblyQualifiedName + "|" + memberName;

        lock (Gate)
        {
            if (MemberCache.TryGetValue(key, out var cached))
                return cached;
        }

        MemberInfo? found = null;
        for (var current = type; current is not null && found is null; current = SafeBaseType(current))
        {
            try
            {
                found = current.GetProperty(memberName, Declared) ?? (MemberInfo?)current.GetField(memberName, Declared);
            }
            catch (AmbiguousMatchException)
            {
                found = current.GetProperties(Declared).FirstOrDefault(p => p.Name == memberName);
            }
            catch
            {
                // An unresolvable member type on this link of the chain; keep walking.
            }
        }

        lock (Gate)
            MemberCache[key] = found;

        return found;
    }

    private static bool IsStatic(MemberInfo member) => member switch
    {
        PropertyInfo property => (property.GetGetMethod(true) ?? property.GetSetMethod(true))?.IsStatic ?? false,
        FieldInfo field => field.IsStatic,
        _ => false,
    };

    private static object? ReadMember(MemberInfo member, object? instance) => member switch
    {
        PropertyInfo property => property.GetValue(instance),
        FieldInfo field => field.GetValue(instance),
        _ => null,
    };
}

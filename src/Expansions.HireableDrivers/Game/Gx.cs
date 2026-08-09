using System.Reflection;
using Expansions.Core.Diagnostics;
using Il2CppInterop.Runtime.InteropTypes;

namespace Expansions.HireableDrivers.Game;

/// <summary>
/// Every touch this mod makes on the game goes through here.
/// <para>
/// The assembly holds <em>no</em> compile-time reference to <c>Assembly-CSharp</c> or FishNet: a type
/// or member the next game patch renames has to degrade to a logged warning and a disabled feature,
/// never to a <c>TypeLoadException</c> at mod load — which would take the whole melon down and, via
/// <c>Expansions.Core</c>, risk the sibling mods with it.
/// </para>
/// <para>
/// Il2CppInterop projects IL2CPP fields <em>and</em> statics as CLR properties, emits interfaces as
/// classes (so <c>is</c>/<c>as</c> never match — <see cref="Cast"/> is mandatory), and gives
/// <c>Il2CppSystem.Collections.Generic.List&lt;T&gt;</c> no managed <c>IEnumerable</c>. All three are
/// handled here so callers can read like ordinary code.
/// </para>
/// </summary>
internal static class Gx
{
    private const BindingFlags Declared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly |
        BindingFlags.Static | BindingFlags.Instance;

    /// <summary>Any parameter type is acceptable at this position.</summary>
    internal const string Any = "*";

    private static readonly Dictionary<string, MethodInfo?> MethodCache = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, MemberInfo?> MemberCache = new(StringComparer.Ordinal);
    private static readonly Dictionary<Type, MethodInfo> CastCache = new();
    private static readonly HashSet<string> ReportedFailures = new(StringComparer.Ordinal);
    private static readonly object Gate = new();

    internal static Type? Type(string fullName) => GameReflection.FindType(fullName);

    /// <summary>Resolved type or a one-time warning. Callers treat null as "not on this build".</summary>
    internal static Type? RequireType(string fullName)
    {
        var type = GameReflection.FindType(fullName);
        if (type is null)
            ReportOnce($"type:{fullName}", $"Game type '{fullName}' is not on this build; the feature that needs it is off.");

        return type;
    }

    internal static bool Alive(object? value) => GameReflection.IsPresent(value);

    // ── Reads ───────────────────────────────────────────────────────────────────────────────────

    internal static object? Get(object? instance, string member)
    {
        if (instance is null)
            return null;

        return GameReflection.TryRead(instance, member, out var value, out var failure)
            ? value
            : Miss(instance.GetType().Name + "." + member, failure);
    }

    internal static T Get<T>(object? instance, string member, T fallback = default!)
    {
        var value = Get(instance, member);
        return value is T typed ? typed : fallback;
    }

    /// <summary>Unity-null-aware read: a destroyed object comes back as null rather than a corpse.</summary>
    internal static object? GetAlive(object? instance, string member)
    {
        var value = Get(instance, member);
        return Alive(value) ? value : null;
    }

    internal static object? GetStatic(string typeName, string member)
    {
        var type = Type(typeName);
        if (type is null)
            return null;

        return GameReflection.TryReadStatic(type, member, out var value, out var failure)
            ? value
            : Miss($"{typeName}.{member}", failure);
    }

    internal static T GetStatic<T>(string typeName, string member, T fallback = default!)
    {
        var value = GetStatic(typeName, member);
        return value is T typed ? typed : fallback;
    }

    internal static bool Set(object? instance, string member, object? value)
    {
        if (instance is null)
            return false;

        var accessor = FindMember(instance.GetType(), member);
        if (accessor is null)
        {
            Miss(instance.GetType().Name + "." + member, "not found");
            return false;
        }

        try
        {
            switch (accessor)
            {
                case PropertyInfo property when property.GetSetMethod(nonPublic: true) is not null:
                    property.SetValue(instance, value);
                    return true;
                case FieldInfo { IsLiteral: false, IsInitOnly: false } field:
                    field.SetValue(instance, value);
                    return true;
                default:
                    Miss(instance.GetType().Name + "." + member, "not writable");
                    return false;
            }
        }
        catch (Exception ex)
        {
            Miss(instance.GetType().Name + "." + member, Explain(ex));
            return false;
        }
    }

    // ── Calls ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Instance call resolved against the value's runtime type. Overloads are disambiguated by
    /// parameter type <em>simple</em> names (<see cref="Any"/> for a wildcard), because arity alone
    /// is ambiguous for things like <c>NPCMovement.SetDestination</c>.
    /// </summary>
    internal static object? Call(object? instance, string method, string[] signature, params object?[] args)
    {
        if (instance is null)
            return null;

        return CallOn(instance.GetType(), instance, method, signature, args);
    }

    internal static object? CallStatic(string typeName, string method, string[] signature, params object?[] args)
    {
        var type = Type(typeName);
        return type is null ? null : CallOn(type, null, method, signature, args);
    }

    internal static object? CallOn(Type? type, object? instance, string method, string[] signature, params object?[] args)
    {
        if (type is null)
            return null;

        var target = Method(type, method, signature);
        if (target is null)
        {
            Miss($"{type.Name}.{method}({string.Join(",", signature)})", "not found");
            return null;
        }

        try
        {
            return target.Invoke(target.IsStatic ? null : instance, args);
        }
        catch (Exception ex)
        {
            Miss($"{type.Name}.{method}", Explain(ex));
            return null;
        }
    }

    /// <summary>True when the call resolved and ran. Use for void calls whose success matters.</summary>
    internal static bool TryCall(object? instance, string method, string[] signature, params object?[] args)
    {
        if (instance is null)
            return false;

        var target = Method(instance.GetType(), method, signature);
        if (target is null)
        {
            Miss($"{instance.GetType().Name}.{method}({string.Join(",", signature)})", "not found");
            return false;
        }

        try
        {
            target.Invoke(target.IsStatic ? null : instance, args);
            return true;
        }
        catch (Exception ex)
        {
            Miss($"{instance.GetType().Name}.{method}", Explain(ex));
            return false;
        }
    }

    internal static MethodInfo? Method(Type? type, string name, params string[] signature)
    {
        if (type is null)
            return null;

        var key = type.AssemblyQualifiedName + "|" + name + "|" + string.Join(",", signature);

        lock (Gate)
        {
            if (MethodCache.TryGetValue(key, out var cached))
                return cached;
        }

        MethodInfo? found = null;
        for (var current = type; current is not null && found is null; current = current.BaseType)
        {
            foreach (var candidate in current.GetMethods(Declared))
            {
                if (!string.Equals(candidate.Name, name, StringComparison.Ordinal))
                    continue;

                if (Matches(candidate, signature))
                {
                    found = candidate;
                    break;
                }
            }
        }

        lock (Gate)
            MethodCache[key] = found;

        return found;
    }

    private static bool Matches(MethodInfo candidate, string[] signature)
    {
        var parameters = candidate.GetParameters();
        if (parameters.Length != signature.Length)
            return false;

        for (var i = 0; i < parameters.Length; i++)
        {
            if (signature[i] == Any)
                continue;

            var name = parameters[i].ParameterType.Name;
            if (name.EndsWith("&", StringComparison.Ordinal))
                name = name[..^1];

            if (!string.Equals(name, signature[i], StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    // ── Interop shapes ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The IL2CPP equivalent of <c>as</c>. Il2CppInterop emits IL2CPP interfaces as CLR classes that
    /// concrete wrappers do not implement, so a managed cast to <c>ITransitEntity</c> always fails;
    /// <c>Il2CppObjectBase.TryCast&lt;T&gt;</c> asks the runtime instead and returns a wrapper whose
    /// virtual calls dispatch to the real implementation.
    /// </summary>
    internal static object? Cast(object? value, Type? target)
    {
        if (value is not Il2CppObjectBase interop || target is null)
            return null;

        try
        {
            MethodInfo closed;
            lock (Gate)
            {
                if (!CastCache.TryGetValue(target, out closed!))
                {
                    var open = typeof(Il2CppObjectBase).GetMethod("TryCast", BindingFlags.Public | BindingFlags.Instance);
                    if (open is null)
                        return null;

                    closed = open.MakeGenericMethod(target);
                    CastCache[target] = closed;
                }
            }

            var result = closed.Invoke(interop, null);
            return Alive(result) ? result : null;
        }
        catch
        {
            return null;
        }
    }

    internal static object? Cast(object? value, string targetTypeName) => Cast(value, Type(targetTypeName));

    /// <summary>Materialises an IL2CPP list, an <c>Il2CppReferenceArray</c> or a managed sequence.</summary>
    internal static IReadOnlyList<object?> List(object? collection) => GameReflection.Enumerate(collection);

    /// <summary>
    /// <c>Il2CppSystem.Guid</c> overrides <c>ToString()</c>, so the boxed struct stringifies to the
    /// canonical dashed form. Persisted keys go through here.
    /// </summary>
    internal static string GuidString(object? guid)
    {
        if (guid is null)
            return string.Empty;

        try
        {
            return guid.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    internal static string GuidOf(object? instance, string member = "GUID") => GuidString(Get(instance, member));

    /// <summary>Boxed enum value by name, e.g. <c>EnumValue("…EEmployeeType", "Handler")</c>.</summary>
    internal static object? EnumValue(string typeName, string name)
    {
        var type = Type(typeName);
        if (type is null || !type.IsEnum)
            return null;

        try
        {
            return Enum.Parse(type, name, ignoreCase: false);
        }
        catch
        {
            Miss($"{typeName}.{name}", "enum member not found");
            return null;
        }
    }

    /// <summary>
    /// An empty <c>Il2CppSystem.Collections.Generic.List&lt;T&gt;</c> for a game type named at runtime.
    /// The game's own setters take these lists, and a managed <c>List&lt;object&gt;</c> will not bind.
    /// </summary>
    internal static object? NewList(string elementTypeName)
    {
        var element = Type(elementTypeName);
        if (element is null)
            return null;

        try
        {
            return Activator.CreateInstance(typeof(Il2CppSystem.Collections.Generic.List<>).MakeGenericType(element));
        }
        catch (Exception ex)
        {
            Miss($"new List<{element.Name}>", Explain(ex));
            return null;
        }
    }

    internal static object? New(string typeName, params object?[] args)
    {
        var type = Type(typeName);
        if (type is null)
            return null;

        try
        {
            return Activator.CreateInstance(type, args);
        }
        catch (Exception ex)
        {
            Miss($"new {type.Name}({args.Length})", Explain(ex));
            return null;
        }
    }

    /// <summary>Live <c>Singleton&lt;T&gt;</c> / <c>NetworkSingleton&lt;T&gt;</c>, or null before it spawns.</summary>
    internal static object? Singleton(string typeName) =>
        GameReflection.TryGetSingleton(typeName, out var instance, out _) ? instance : null;

    // ── Failure bookkeeping ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Unwraps the <c>TargetInvocationException</c> every reflective call wraps a real failure in, so
    /// the log says what actually went wrong. Core's equivalent is internal to Core.
    /// </summary>
    internal static string Explain(Exception exception)
    {
        var inner = exception is TargetInvocationException { InnerException: { } target } ? target : exception;
        return $"{inner.GetType().Name}: {inner.Message}";
    }

    /// <summary>
    /// Walks the base chain by hand. <c>FlattenHierarchy</c> is not dependable here: Il2CppInterop puts
    /// singleton accessors on the open generic base, and an override and its base declaration are two
    /// separate members that would otherwise come back ambiguous.
    /// </summary>
    private static MemberInfo? FindMember(Type type, string memberName)
    {
        var key = type.AssemblyQualifiedName + "|#|" + memberName;

        lock (Gate)
        {
            if (MemberCache.TryGetValue(key, out var cached))
                return cached;
        }

        MemberInfo? found = null;
        for (var current = type; current is not null && found is null; current = current.BaseType)
        {
            try
            {
                found = current.GetProperty(memberName, Declared) ?? (MemberInfo?)current.GetField(memberName, Declared);
            }
            catch (AmbiguousMatchException)
            {
                found = current.GetProperties(Declared).FirstOrDefault(p => p.Name == memberName);
            }
        }

        lock (Gate)
            MemberCache[key] = found;

        return found;
    }

    private static object? Miss(string what, string failure)
    {
        ReportOnce("member:" + what, $"Could not reach '{what}' ({failure}). That part of the driver loop is disabled.");
        return null;
    }

    /// <summary>
    /// One warning per distinct failure per process. A tick loop that runs once a second must not
    /// turn a renamed member into ten thousand log lines.
    /// </summary>
    private static void ReportOnce(string key, string message)
    {
        lock (Gate)
        {
            if (!ReportedFailures.Add(key))
                return;
        }

        DriverLog.Warn(message);
    }

    /// <summary>Distinct binding failures seen so far, for the diagnostics probe.</summary>
    internal static IReadOnlyCollection<string> Failures
    {
        get
        {
            lock (Gate)
                return ReportedFailures.ToArray();
        }
    }
}

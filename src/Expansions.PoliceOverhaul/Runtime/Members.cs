using System.Reflection;
using Expansions.Core.Diagnostics;

namespace Expansions.PoliceOverhaul.Runtime;

/// <summary>
/// Instance-member writes, which <c>GameReflection</c> deliberately does not offer — it only writes
/// statics, because those are the ones that can fault the process.
/// <para>
/// Everything here is best-effort and silent on failure by design: a renamed game member has to
/// degrade one lever, not throw out of a per-minute tick.
/// </para>
/// <para>
/// ⚠ <see cref="Read"/> / <see cref="ReadPath"/> call <c>GameReflection.TryRead</c>, which looks
/// safe (bool return) but can still hard-fault the process with an uncatchable
/// <c>AccessViolationException</c> when the native object is dead or half-built. Never use these
/// from a Harmony prefix/postfix to answer "is this ours?" — use managed pointer-set membership
/// instead (<see cref="FederalAgents.IsAgent"/>).
/// </para>
/// </summary>
internal static class Members
{
    private const BindingFlags Declared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly |
        BindingFlags.Static | BindingFlags.Instance;

    private static readonly Dictionary<string, MemberInfo?> Cache = new(StringComparer.Ordinal);
    private static readonly object Gate = new();

    internal static bool TryWrite(object? instance, string memberName, object? value)
    {
        if (instance is null)
            return false;

        var member = Find(instance.GetType(), memberName);

        try
        {
            switch (member)
            {
                case PropertyInfo property when property.GetSetMethod(nonPublic: true) is not null:
                    property.SetValue(instance, value);
                    return true;
                case FieldInfo { IsLiteral: false, IsInitOnly: false } field:
                    field.SetValue(instance, value);
                    return true;
                default:
                    return false;
            }
        }
        catch (Exception ex)
        {
            PoliceLog.Detail($"Writing {instance.GetType().Name}.{memberName} failed: {PoliceLog.Describe(ex)}");
            return false;
        }
    }

    internal static T Read<T>(object? instance, string memberName, T fallback)
    {
        if (!GameReflection.TryRead(instance, memberName, out var value, out _))
            return fallback;

        return value is T typed ? typed : fallback;
    }

    /// <summary>Reads a dotted chain, returning <paramref name="fallback"/> if any hop is missing or null.</summary>
    internal static object? ReadPath(object? root, string path) =>
        GameReflection.TryReadPath(root, path, out var value, out _) ? value : null;

    internal static bool Invoke(object? instance, string methodName, params object?[] arguments) =>
        InvokeCore(instance, methodName, arguments, out _);

    internal static object? InvokeFor(object? instance, string methodName, params object?[] arguments) =>
        InvokeCore(instance, methodName, arguments, out var result) ? result : null;

    private static bool InvokeCore(object? instance, string methodName, object?[] arguments, out object? result)
    {
        result = null;
        if (instance is null)
            return false;

        if (GameReflection.TryInvoke(instance.GetType(), instance, methodName, arguments, out result, out var failure))
            return true;

        PoliceLog.Detail($"Calling {instance.GetType().Name}.{methodName} failed: {failure}");
        return false;
    }

    private static MemberInfo? Find(Type type, string memberName)
    {
        var key = type.AssemblyQualifiedName + "|" + memberName;

        lock (Gate)
        {
            if (Cache.TryGetValue(key, out var cached))
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
            Cache[key] = found;

        return found;
    }
}

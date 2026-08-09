using System.Globalization;
using Expansions.Core.Diagnostics;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Expansions.Tweaks.Runtime;

/// <summary>
/// Thin typed wrappers over <see cref="GameReflection"/>, plus the two IL2CPP-specific lookups this
/// module needs. Everything here answers with a fallback rather than throwing: a member that moved
/// between game versions must cost one feature, not the mod.
/// </summary>
internal static class Members
{
    /// <summary>Reads an instance member, converting numerics, and falls back on any failure.</summary>
    internal static T Read<T>(object? instance, string name, T fallback)
    {
        if (!GameReflection.TryRead(instance, name, out var value, out _))
            return fallback;

        return Coerce(value, fallback);
    }

    internal static object? ReadObject(object? instance, string name) =>
        GameReflection.TryRead(instance, name, out var value, out _) ? value : null;

    internal static bool Write(object? instance, string name, object? value)
    {
        if (GameReflection.TryWrite(instance, name, value, out var failure))
            return true;

        TweakLog.Detail($"Could not write '{name}': {failure}");
        return false;
    }

    internal static T ReadStatic<T>(Type? type, string name, T fallback)
    {
        if (type is null || !GameReflection.TryReadStatic(type, name, out var value, out _))
            return fallback;

        return Coerce(value, fallback);
    }

    /// <summary>
    /// Every live and asset-resident instance of an IL2CPP <see cref="Object"/> subclass, each
    /// re-wrapped as <paramref name="type"/>.
    /// <para>
    /// <c>Resources.FindObjectsOfTypeAll</c> rather than <c>FindObjectsOfType</c>: it also returns
    /// inactive objects and ScriptableObject assets, which is where the delivery settings live and
    /// where a mixing station sits while its property is unloaded. The explicit re-wrap exists because
    /// the array element type is <c>UnityEngine.Object</c>, and a member lookup against that wrapper
    /// would find nothing.
    /// </para>
    /// </summary>
    internal static IReadOnlyList<(Object Unity, object Typed)> FindAll(Type type)
    {
        var found = new List<(Object, object)>();

        try
        {
            var objects = Resources.FindObjectsOfTypeAll(Il2CppType.From(type));
            if (objects is null)
                return found;

            foreach (var candidate in objects)
            {
                if (candidate is null || !GameReflection.IsPresent(candidate))
                    continue;

                var typed = ReWrap(candidate, type);
                if (typed is not null)
                    found.Add((candidate, typed));
            }
        }
        catch (Exception ex)
        {
            TweakLog.Detail($"Could not enumerate '{type.Name}': {TweakLog.Describe(ex)}");
        }

        return found;
    }

    /// <summary>Same native object, seen through the wrapper class that actually declares its members.</summary>
    private static object? ReWrap(object candidate, Type type)
    {
        if (type.IsInstanceOfType(candidate))
            return candidate;

        try
        {
            return candidate is Il2CppObjectBase native
                ? Activator.CreateInstance(type, native.Pointer)
                : null;
        }
        catch (Exception ex)
        {
            TweakLog.Detail($"Could not re-wrap an instance as '{type.Name}': {TweakLog.Describe(ex)}");
            return null;
        }
    }

    /// <summary>
    /// Boxed IL2CPP numerics do not unbox to a different numeric type, and an IL2CPP enum arrives as
    /// its own CLR enum rather than an <c>int</c>. Both are routine here, so conversion is the normal
    /// path rather than an error case.
    /// </summary>
    private static T Coerce<T>(object? value, T fallback)
    {
        if (value is T typed)
            return typed;

        if (value is null)
            return fallback;

        try
        {
            return (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
        }
        catch
        {
            return fallback;
        }
    }
}

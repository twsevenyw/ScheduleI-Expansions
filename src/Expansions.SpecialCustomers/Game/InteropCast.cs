using System.Reflection;
using Expansions.Core.Diagnostics;
using Il2CppInterop.Runtime.InteropTypes;

namespace Expansions.SpecialCustomers.Game;

/// <summary>
/// Re-wraps Il2CppInterop <c>UnityEngine.Object</c>/<c>Component</c> shells as their concrete type.
/// <para>
/// <c>FindObjectsOfType</c>, <c>GetComponent(Il2CppType)</c>, and <c>Object.Instantiate</c> hand back
/// base wrappers. Reflecting a member off those looks on <c>Object</c>/<c>Component</c>, finds
/// nothing, and fails silently — the same trap Hireable Drivers hit with
/// <c>AddDialogueChoice</c>. Always cast before resolving a member.
/// </para>
/// </summary>
internal static class InteropCast
{
    private static readonly Dictionary<Type, MethodInfo> CastCache = new();
    private static readonly object Gate = new();

    /// <summary>
    /// Concrete projection of <paramref name="value"/> as <paramref name="typeName"/>, or null when
    /// the native object is not that type / is destroyed.
    /// </summary>
    internal static object? As(object? value, string typeName)
    {
        var type = GameReflection.FindType(typeName);
        return type is null ? null : As(value, type);
    }

    internal static object? As(object? value, Type? target)
    {
        if (value is null || target is null || !GameReflection.IsPresent(value))
            return null;

        if (target.IsInstanceOfType(value))
            return value;

        if (value is not Il2CppObjectBase interop)
            return null;

        try
        {
            MethodInfo closed;
            lock (Gate)
            {
                if (!CastCache.TryGetValue(target, out closed!))
                {
                    var open = typeof(Il2CppObjectBase).GetMethod(
                        "TryCast", BindingFlags.Public | BindingFlags.Instance);
                    if (open is null)
                        return Reinterpret(interop, target);

                    closed = open.MakeGenericMethod(target);
                    CastCache[target] = closed;
                }
            }

            var result = closed.Invoke(interop, null);
            if (GameReflection.IsPresent(result))
                return result;
        }
        catch
        {
            // Fall through to the IntPtr constructor — same path VisitorIntegrity already uses.
        }

        return Reinterpret(interop, target);
    }

    /// <summary>
    /// True when <paramref name="value"/> is already the concrete type, or can be re-wrapped as it.
    /// Surfaced in failure strings so an untyped Object never looks like "member missing".
    /// </summary>
    internal static bool Is(object? value, string typeName) => As(value, typeName) is not null;

    private static object? Reinterpret(Il2CppObjectBase instance, Type wrapper)
    {
        try
        {
            return Activator.CreateInstance(wrapper, instance.Pointer);
        }
        catch
        {
            return null;
        }
    }
}

using Expansions.Core.Diagnostics;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace Expansions.PoliceOverhaul.Runtime;

/// <summary>
/// Component lookups across the interop boundary.
/// <para>
/// <c>GetComponent</c> hands back a wrapper typed as <see cref="Component"/> whatever was asked for,
/// so the derived members are invisible to reflection until the wrapper is rebuilt through the
/// concrete interop type's <c>IntPtr</c> constructor. That rebuild is the whole reason this class
/// exists, and getting it wrong is silent: every later member lookup simply finds nothing.
/// </para>
/// </summary>
internal static class Components
{
    /// <summary>Native pointer of an interop object, or <see cref="IntPtr.Zero"/>. The identity key.</summary>
    internal static IntPtr PointerOf(object? value) =>
        value is Il2CppObjectBase native ? native.Pointer : IntPtr.Zero;

    internal static object? Get(GameObject? gameObject, Type componentType)
    {
        if (gameObject is null)
            return null;

        try
        {
            var component = gameObject.GetComponent(Il2CppType.From(componentType));
            return GameReflection.IsPresent(component) ? Rebuild(component!, componentType) : null;
        }
        catch (Exception ex)
        {
            PoliceLog.Detail($"GetComponent<{componentType.Name}> failed: {PoliceLog.Describe(ex)}");
            return null;
        }
    }

    /// <summary>
    /// Every component of <paramref name="componentType"/> under <paramref name="root"/>, inactive
    /// ones included. Inactive matters more than it sounds: property content culling deactivates a
    /// property's objects once the player walks away, which is exactly when a raid happens.
    /// </summary>
    internal static IReadOnlyList<object> InChildren(Transform? root, Type componentType)
    {
        var found = new List<object>();
        if (root is null)
            return found;

        try
        {
            var components = root.GetComponentsInChildren(Il2CppType.From(componentType), true);
            if (components is null)
                return found;

            foreach (var component in components)
            {
                if (!GameReflection.IsPresent(component))
                    continue;

                if (Rebuild(component, componentType) is { } typed)
                    found.Add(typed);
            }
        }
        catch (Exception ex)
        {
            PoliceLog.Detail($"GetComponentsInChildren<{componentType.Name}> failed: {PoliceLog.Describe(ex)}");
        }

        return found;
    }

    internal static Transform? TransformOf(object? instance) => Members.ReadPath(instance, "transform") as Transform;

    internal static GameObject? GameObjectOf(object? instance) => Members.ReadPath(instance, "gameObject") as GameObject;

    private static object? Rebuild(Component component, Type componentType)
    {
        try
        {
            return Activator.CreateInstance(componentType, component.Pointer);
        }
        catch (Exception ex)
        {
            PoliceLog.Detail($"Rebuilding a {componentType.Name} wrapper failed: {PoliceLog.Describe(ex)}");
            return null;
        }
    }
}

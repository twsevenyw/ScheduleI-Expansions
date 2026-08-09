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

    /// <summary>
    /// Re-reads an interop object as a more-derived type.
    /// <para>
    /// Il2CppInterop builds a wrapper of whatever type the <em>property</em> declared, not of whatever
    /// the native object actually is, and reflection then follows the wrapper. So reading
    /// <c>NPC.NPCData</c> hands back something typed <c>NPCData</c> even when the native object is a
    /// <c>DealerNPCData</c>, and every member the subclass adds is invisible — silently, with no
    /// exception and no log line.
    /// </para>
    /// <para>
    /// The native class name is checked first, so a genuinely different subclass returns null rather
    /// than a wrapper that reads whatever happens to sit at that offset.
    /// </para>
    /// </summary>
    internal static object? Reinterpret(object? instance, string typeName)
    {
        if (instance is null)
            return null;

        var type = GameReflection.FindType(typeName);
        if (type is null)
            return null;

        if (type.IsInstanceOfType(instance))
            return instance;

        var simpleName = typeName[(typeName.LastIndexOf('.') + 1)..];
        if (!string.Equals(GameBridge.NativeClassName(instance), simpleName, StringComparison.Ordinal))
            return null;

        var pointer = PointerOf(instance);
        if (pointer == IntPtr.Zero)
            return null;

        try
        {
            return Activator.CreateInstance(type, pointer);
        }
        catch (Exception ex)
        {
            PoliceLog.Detail($"Re-reading a {simpleName} wrapper failed: {PoliceLog.Describe(ex)}");
            return null;
        }
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

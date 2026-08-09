using System.Reflection;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace Expansions.Core.UI.Native;

/// <summary>
/// Managed reflection over Il2CppInterop wrappers, for the game types Core deliberately does not
/// reference at compile time.
/// <para>
/// Core is loaded from <c>UserLibs</c> by all three mods, so a hard reference to a game type that a
/// patch renames would surface as a <c>TypeLoadException</c> and take every mod down with it. The UI
/// therefore reaches <c>Assembly-CSharp</c> the same way <see cref="Diagnostics.GameReflection"/>
/// does: by name, with a graceful "not on this build" answer.
/// </para>
/// </summary>
internal static class InteropObjects
{
    private const BindingFlags Instance =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    /// <summary>
    /// Live scene instances of a late-bound <c>UnityEngine.Object</c> subclass.
    /// <para>
    /// <c>FindObjectsOfTypeAll</c> also returns objects loaded from prefabs and asset bundles, which
    /// have no scene and would answer questions like "is this the open screen" with prefab data;
    /// <c>scene.IsValid()</c> is the filter that keeps only what is really instantiated.
    /// </para>
    /// </summary>
    public static List<UnityEngine.Object> FindInScene(Type wrapper)
    {
        var found = new List<UnityEngine.Object>();

        try
        {
            var il2CppType = Il2CppType.From(wrapper, throwOnFailure: false);
            if (il2CppType is null)
                return found;

            var all = Resources.FindObjectsOfTypeAll(il2CppType);
            if (all is null)
                return found;

            for (var i = 0; i < all.Length; i++)
            {
                var candidate = all[i];
                if (candidate == null)
                    continue;

                var component = candidate.TryCast<Component>();
                if (component == null || !component.gameObject.scene.IsValid())
                    continue;

                found.Add(candidate);
            }
        }
        catch
        {
            // A missing interop type or a heap walk during a scene swap: the caller reports it as
            // "main menu not found" and degrades.
        }

        return found;
    }

    /// <summary>
    /// Re-wraps <paramref name="instance"/> as <paramref name="wrapper"/>. Reflection cannot read an
    /// Il2CppInterop generated property through a base-class wrapper, and every generated type
    /// carries a public <c>.ctor(IntPtr)</c> for precisely this.
    /// </summary>
    public static object? Reinterpret(Il2CppObjectBase? instance, Type wrapper)
    {
        if (instance is null)
            return null;

        if (wrapper.IsInstanceOfType(instance))
            return instance;

        try
        {
            return Activator.CreateInstance(wrapper, instance.Pointer);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Reads a property or field off a late-bound wrapper. Null means "absent or threw".</summary>
    public static object? Read(Il2CppObjectBase? instance, Type wrapper, string memberName)
    {
        var typed = Reinterpret(instance, wrapper);
        if (typed is null)
            return null;

        try
        {
            var property = wrapper.GetProperty(memberName, Instance);
            if (property is not null && property.CanRead)
                return property.GetValue(typed);

            return wrapper.GetField(memberName, Instance)?.GetValue(typed);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Null-safe for destroyed natives: a freed <c>UnityEngine.Object</c> arrives non-null.</summary>
    public static bool Alive(UnityEngine.Object? value)
    {
        if (value is null)
            return false;

        try
        {
            return value != null;
        }
        catch
        {
            return false;
        }
    }
}

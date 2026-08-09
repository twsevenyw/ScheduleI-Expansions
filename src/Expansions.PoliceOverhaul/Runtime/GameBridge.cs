using Expansions.Core.Diagnostics;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;

namespace Expansions.PoliceOverhaul.Runtime;

/// <summary>
/// The seam between the mod's own logic and the running game. Everything crossing it is late-bound,
/// null-tolerant and cheap enough to sit on a per-minute tick.
/// </summary>
internal static class GameBridge
{
    /// <summary>
    /// The IL2CPP class name of a live object, which is not the same as its managed wrapper type.
    /// <para>
    /// Il2CppInterop hands a Harmony patch whatever type the method signature declared, so a
    /// <c>Crime</c> argument arrives as <c>Crime</c> even when the object is really a
    /// <c>ViolatingCurfew</c>. The native class is the only reliable discriminator, and the crime
    /// subtype is exactly what the fine table is keyed on.
    /// </para>
    /// </summary>
    internal static string NativeClassName(object? value)
    {
        if (value is not Il2CppObjectBase native)
            return value?.GetType().Name ?? string.Empty;

        try
        {
            var pointer = native.Pointer;
            if (pointer == IntPtr.Zero)
                return string.Empty;

            return IL2CPP.il2cpp_class_get_name_(IL2CPP.il2cpp_object_get_class(pointer)) ?? string.Empty;
        }
        catch
        {
            return value.GetType().Name;
        }
    }

    /// <summary>Boxes an integer as a game enum so it can be passed through reflection.</summary>
    internal static object? BoxEnum(string enumTypeName, int value)
    {
        var type = GameReflection.FindType(enumTypeName);
        if (type is null || !type.IsEnum)
            return null;

        try
        {
            return Enum.ToObject(type, value);
        }
        catch
        {
            return null;
        }
    }

    internal static object? Singleton(string typeName) =>
        GameReflection.TryGetSingleton(typeName, out var instance, out _) ? instance : null;

    /// <summary>Every live player object, host and clients. Empty before the world exists.</summary>
    internal static IReadOnlyList<object?> Players()
    {
        var type = GameReflection.FindType(GameTypes.Player);
        if (type is null)
            return Array.Empty<object?>();

        return GameReflection.TryReadStatic(type, "PlayerList", out var list, out _)
            ? GameReflection.Enumerate(list, 64)
            : Array.Empty<object?>();
    }

    internal static object? LocalPlayer()
    {
        var type = GameReflection.FindType(GameTypes.Player);
        if (type is null)
            return null;

        return GameReflection.TryReadStatic(type, "Local", out var player, out _) && GameReflection.IsPresent(player)
            ? player
            : null;
    }

    /// <summary>
    /// Stable-enough per-player key.
    /// <para>
    /// <c>PlayerCode</c> is a SyncVar and is what the game's own pursuit calls take, but nothing
    /// proves it survives a reload for the same human. Solo play therefore collapses to the constant
    /// <c>"local"</c>, which is provably stable, and only co-op pays the risk.
    /// </para>
    /// </summary>
    internal static string KeyFor(object? player)
    {
        if (player is null)
            return "local";

        if (Players().Count <= 1)
            return "local";

        var code = Members.Read(player, "PlayerCode", string.Empty);
        return string.IsNullOrWhiteSpace(code) ? "local" : code;
    }

    internal static string NameOf(object? player) => Members.Read(player, "PlayerName", string.Empty);

    internal static bool IsArrested(object? player) => Members.Read(player, "IsArrested", false);

    /// <summary>Region name, or empty. Used only as a heat multiplier, so empty is a safe answer.</summary>
    internal static string RegionOf(object? player)
    {
        if (!GameReflection.TryRead(player, "CurrentRegion", out var region, out _) || region is null)
            return string.Empty;

        return region.ToString() ?? string.Empty;
    }

    internal static bool CurfewActive()
    {
        var manager = Singleton(GameTypes.CurfewManager);
        return manager is not null && Members.Read(manager, "IsCurrentlyActive", false);
    }

    /// <summary>
    /// A toast in the game's own notification style. Silent if the manager is not up yet, which is
    /// the normal state in the menu scene.
    /// </summary>
    internal static void Notify(string title, string body, float seconds = 5f)
    {
        var manager = Singleton(GameTypes.NotificationsManager);
        if (manager is null)
            return;

        Members.Invoke(manager, "SendNotification", title, body, null, seconds, true);
    }

    /// <summary>Appends to an <c>Il2CppSystem.Collections.Generic.List&lt;string&gt;</c>.</summary>
    internal static void AddToIl2CppList(object? list, string value) => Members.Invoke(list, "Add", value);

    /// <summary>
    /// A new, empty IL2CPP list of the same closed generic type as <paramref name="template"/>.
    /// Cloning the type off a live instance sidesteps having to name the element type at all.
    /// </summary>
    internal static object? NewListLike(object? template)
    {
        if (template is null)
            return null;

        try
        {
            return Activator.CreateInstance(template.GetType());
        }
        catch (Exception ex)
        {
            PoliceLog.Detail($"Could not create a list like {template.GetType().Name}: {PoliceLog.Describe(ex)}");
            return null;
        }
    }
}

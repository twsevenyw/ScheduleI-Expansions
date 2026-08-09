using Expansions.Core.Diagnostics;
using UnityEngine;

namespace Expansions.PoliceOverhaul.Runtime;

/// <summary>
/// The player's owned properties, and what contraband is sitting in them.
/// <para>
/// Everything here reads the game's own <c>Property.OwnedProperties</c> list and its own bounds test,
/// so "is this mine" and "am I standing in it" are answered by the same code the game uses to decide
/// whether you can build in a room.
/// </para>
/// </summary>
internal static class Estate
{
    /// <summary>
    /// Property ownership changes at most a handful of times a save, but the raid actions ask about it
    /// on every frame the menu is open, and each answer is a reflection walk of the game's list. Half a
    /// second of staleness is invisible to the player and turns sixty walks a second into two.
    /// </summary>
    private const float OwnedTtlSeconds = 0.5f;

    private static IReadOnlyList<object> _owned = Array.Empty<object>();
    private static float _ownedStamp = float.NegativeInfinity;

    /// <summary>Every property the player owns. Empty before a save is loaded.</summary>
    internal static IReadOnlyList<object> Owned()
    {
        var now = UnscaledTime();
        if (now - _ownedStamp < OwnedTtlSeconds)
            return _owned;

        _ownedStamp = now;
        _owned = ReadOwned();
        return _owned;
    }

    /// <summary>Drops the cache. Called on scene unload, where every entry is about to be destroyed.</summary>
    internal static void Forget()
    {
        _owned = Array.Empty<object>();
        _ownedStamp = float.NegativeInfinity;
    }

    private static IReadOnlyList<object> ReadOwned()
    {
        var type = GameReflection.FindType(GameTypes.Property);
        if (type is null)
            return Array.Empty<object>();

        if (!GameReflection.TryReadStatic(type, "OwnedProperties", out var list, out _))
            return Array.Empty<object>();

        var owned = new List<object>();
        foreach (var property in GameReflection.Enumerate(list, 64))
        {
            if (GameReflection.IsPresent(property) && property is not null)
                owned.Add(property);
        }

        return owned;
    }

    private static float UnscaledTime()
    {
        try
        {
            return Time.unscaledTime;
        }
        catch
        {
            // Off the Unity main thread there is no clock; recompute rather than serve a stale list.
            return float.PositiveInfinity;
        }
    }

    internal static string NameOf(object? property)
    {
        var name = Members.Read(property, "PropertyName", string.Empty);
        if (name.Length > 0)
            return name;

        var code = Members.Read(property, "PropertyCode", string.Empty);
        return code.Length > 0 ? code : "one of your properties";
    }

    internal static string CodeOf(object? property) => Members.Read(property, "PropertyCode", string.Empty);

    /// <summary>
    /// Whether a point is inside the property, using the game's own collider test. A property whose
    /// bounds cannot be read answers <c>false</c>, which is the safe direction: a raid on a property
    /// the player might be standing in would read as a bug.
    /// </summary>
    internal static bool Contains(object? property, Vector3 point)
    {
        if (property is null)
            return false;

        return Members.InvokeFor(property, "DoBoundsContainPoint", point) is true;
    }

    internal static Vector3 SpawnPointOf(object? property)
    {
        if (Members.ReadPath(property, "SpawnPoint") is Transform spawn)
            return spawn.position;

        return Components.TransformOf(property)?.position ?? Vector3.zero;
    }

    /// <summary>
    /// Every storage container that belongs to this property.
    /// <para>
    /// Scanned from the property's own transform and from each buildable item it owns, because a
    /// placed rack can be parented under either depending on which grid it was built on. Results are
    /// de-duplicated by native pointer, so a container reachable both ways is only raided once.
    /// </para>
    /// </summary>
    internal static IReadOnlyList<object> StoragesIn(object? property)
    {
        var storageType = GameReflection.FindType(GameTypes.StorageEntity);
        if (property is null || storageType is null)
            return Array.Empty<object>();

        var found = new List<object>();
        var seen = new HashSet<IntPtr>();

        Collect(Components.TransformOf(property));

        foreach (var item in GameReflection.Enumerate(Members.ReadPath(property, "BuildableItems"), 512))
            Collect(Components.TransformOf(item));

        return found;

        void Collect(Transform? root)
        {
            foreach (var storage in Components.InChildren(root, storageType))
            {
                if (seen.Add(Components.PointerOf(storage)))
                    found.Add(storage);
            }
        }
    }

    /// <summary>
    /// The contraband stacks in one container, newest-worth-first so a partial seizure takes the
    /// valuable product rather than whatever happened to be in slot zero.
    /// </summary>
    internal static List<Contraband> ContrabandIn(object? storage)
    {
        var haul = new List<Contraband>();

        if (!GameReflection.TryRead(storage, "ItemSlots", out var slots, out _) || slots is null)
            return haul;

        var entries = GameReflection.Enumerate(slots, 64);
        for (var i = 0; i < entries.Count; i++)
        {
            var instance = Members.ReadPath(entries[i], "ItemInstance");
            if (instance is null || !Items.IsContraband(instance))
                continue;

            haul.Add(new Contraband(i, instance, Items.NameOf(instance), Items.QuantityOf(instance), Items.ValueOf(instance)));
        }

        haul.Sort(static (a, b) => b.Value.CompareTo(a.Value));
        return haul;
    }

    /// <summary>One stack of seizable product in a container slot.</summary>
    internal readonly struct Contraband
    {
        internal Contraband(int slotIndex, object instance, string name, int quantity, float value)
        {
            SlotIndex = slotIndex;
            Instance = instance;
            Name = name;
            Quantity = quantity;
            Value = value;
        }

        internal int SlotIndex { get; }

        internal object Instance { get; }

        internal string Name { get; }

        internal int Quantity { get; }

        /// <summary>Total worth of the stack, for the seizure total the federal triggers read.</summary>
        internal float Value { get; }
    }
}

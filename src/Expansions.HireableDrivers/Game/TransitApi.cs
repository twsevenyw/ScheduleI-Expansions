using UnityEngine;

namespace Expansions.HireableDrivers.Game;

/// <summary>
/// Storage-side primitives: reading an <c>ITransitEntity</c>, and moving items in and out of it
/// without leaving a slot locked or a duplicated stack behind.
/// </summary>
internal static class TransitApi
{
    private const int OutputSlots = 1;

    /// <summary>
    /// Casts anything to <c>ITransitEntity</c> and rejects the partial implementors.
    /// <para>
    /// Some types satisfy the cast but leave <c>InputSlots</c>, <c>OutputSlots</c> or
    /// <c>AccessPoints</c> null, which makes them selectable in a route picker and useless to the
    /// loop. Filtering here is what stops a route the player can build but the driver cannot run.
    /// </para>
    /// </summary>
    internal static object? AsUsableTransit(object? candidate)
    {
        var transit = Gx.Cast(candidate, GameTypes.TransitEntity);
        if (transit is null)
            return null;

        if (Gx.Get(transit, "AccessPoints") is null)
            return null;

        if (Gx.Get(transit, "InputSlots") is null && Gx.Get(transit, "OutputSlots") is null)
            return null;

        return transit;
    }

    internal static string TransitGuid(object? transit) => Gx.GuidOf(transit);

    internal static string TransitName(object? transit)
    {
        var name = Gx.Get<string>(transit, "Name", string.Empty);
        return string.IsNullOrWhiteSpace(name) ? "storage" : name;
    }

    internal static bool IsDestroyed(object? transit) => !Gx.Alive(transit) || Gx.Get(transit, "IsDestroyed") is true;

    internal static bool IsAcceptingItems(object? transit) => Gx.Get(transit, "IsAcceptingItems") is true;

    internal static Vector3 TransitPosition(object? transit)
    {
        if (Gx.GetAlive(transit, "LinkOrigin") is Transform origin)
            return origin.position;

        foreach (var point in Gx.List(Gx.Get(transit, "AccessPoints")))
        {
            if (point is Transform { } access && Gx.Alive(access))
                return access.position;
        }

        return Vector3.zero;
    }

    /// <summary>Items sitting in the entity's output slots, as (definition id, display name, count).</summary>
    internal static IReadOnlyList<(string Id, string Name, int Quantity)> Available(object? transit)
    {
        var totals = new Dictionary<string, (string Name, int Quantity)>(StringComparer.OrdinalIgnoreCase);

        foreach (var slot in Gx.List(Gx.Get(transit, "OutputSlots")))
        {
            var instance = Gx.Get(slot, "ItemInstance");
            if (instance is null)
                continue;

            var id = Gx.Get<string>(instance, "ID", string.Empty);
            if (id.Length == 0)
                continue;

            var quantity = Gx.Get(slot, "Quantity") as int? ?? 0;
            if (quantity <= 0)
                continue;

            var name = Gx.Get<string>(instance, "Name", id);
            totals[id] = totals.TryGetValue(id, out var existing)
                ? (existing.Name, existing.Quantity + quantity)
                : (name, quantity);
        }

        return totals.Select(pair => (pair.Key, pair.Value.Name, pair.Value.Quantity)).ToArray();
    }

    /// <summary>First non-empty, unlocked output slot whose item matches <paramref name="itemId"/> (empty = any).</summary>
    internal static object? FindOutputSlot(object? transit, string itemId)
    {
        foreach (var slot in Gx.List(Gx.Get(transit, "OutputSlots")))
        {
            var instance = Gx.Get(slot, "ItemInstance");
            if (instance is null)
                continue;

            if ((Gx.Get(slot, "Quantity") as int? ?? 0) <= 0)
                continue;

            if (Gx.Get(slot, "IsLocked") is true || Gx.Get(slot, "IsRemovalLocked") is true)
                continue;

            if (itemId.Length > 0 &&
                !string.Equals(Gx.Get<string>(instance, "ID", string.Empty), itemId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return slot;
        }

        return null;
    }

    // ── Source -> trunk ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Moves one batch from a source entity into a storage container. Returns the number of units
    /// actually moved, which is zero for every "not now" case — full trunk, filtered slot, locked
    /// slot, player has the container open.
    /// </summary>
    internal static int SourceToStorage(object? source, object? storage, object? npc, string itemId, int maxUnits)
    {
        if (source is null || storage is null || maxUnits <= 0)
            return 0;

        if (Gx.Get(storage, "IsOpened") is true || Gx.GetAlive(storage, "CurrentPlayerAccessor") is not null)
            return 0;

        var slot = FindOutputSlot(source, itemId);
        if (slot is null)
            return 0;

        var instance = Gx.Get(slot, "ItemInstance");
        if (instance is null)
            return 0;

        var available = Gx.Call(source, "GetOutputCapacityForItem", new[] { "ItemInstance", "NPC" }, instance, npc) as int? ?? 0;
        var space = Gx.Call(storage, "HowManyCanFit", new[] { "ItemInstance" }, instance) as int? ?? 0;
        var held = Gx.Get(slot, "Quantity") as int? ?? 0;

        var amount = Math.Min(Math.Min(available, space), Math.Min(held, maxUnits));
        if (amount <= 0)
            return 0;

        // Never alias the source instance: quality-bearing items, cash and water only stack when the
        // extra data matches, and a shared reference would mutate both ends at once.
        var payload = Gx.Call(instance, "GetCopy", new[] { "Int32" }, amount);
        if (payload is null)
            return 0;

        Gx.Call(slot, "ChangeQuantity", new[] { "Int32", "Boolean" }, -amount, false);
        Gx.Call(storage, "InsertItem", new[] { "ItemInstance", "Boolean" }, payload, true);
        Gx.Call(storage, "ContentsChanged", Array.Empty<string>());
        return amount;
    }

    // ── Trunk -> destination ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Empties as much of a storage container into a destination entity as will fit.
    /// <para>
    /// The input slots are reserved for the duration and the lock is always released in a
    /// <c>finally</c>: a leaked slot lock jams the destination for the rest of the save, and no
    /// vanilla code path clears one it did not create.
    /// </para>
    /// </summary>
    internal static int StorageToDestination(object? storage, object? destination, object? npc, object? locker)
    {
        if (storage is null || destination is null)
            return 0;

        if (Gx.Get(storage, "IsOpened") is true || Gx.GetAlive(storage, "CurrentPlayerAccessor") is not null)
            return 0;

        if (!IsAcceptingItems(destination))
            return 0;

        var moved = 0;
        var reserved = false;

        try
        {
            foreach (var slot in Gx.List(Gx.Get(storage, "ItemSlots")))
            {
                var instance = Gx.Get(slot, "ItemInstance");
                if (instance is null)
                    continue;

                var held = Gx.Get(slot, "Quantity") as int? ?? 0;
                if (held <= 0)
                    continue;

                if (!reserved && locker is not null)
                {
                    Gx.Call(destination, "ReserveInputSlotsForItem", new[] { "ItemInstance", "NetworkObject" }, instance, locker);
                    reserved = true;
                }

                // checkPlayerFilters: true, or a player-filtered slot happily swallows the wrong item.
                var space = Gx.Call(destination, "GetInputCapacityForItem", new[] { "ItemInstance", "NPC", "Boolean" },
                    instance, npc, true) as int? ?? 0;

                var amount = Math.Min(space, held);
                if (amount <= 0)
                    continue;

                var chunk = Gx.Call(instance, "GetCopy", new[] { "Int32" }, amount);
                if (chunk is null)
                    continue;

                Gx.Call(slot, "ChangeQuantity", new[] { "Int32", "Boolean" }, -amount, false);
                Gx.Call(destination, "InsertItemIntoInput", new[] { "ItemInstance", "NPC" }, chunk, npc);
                moved += amount;
            }

            if (moved > 0)
                Gx.Call(storage, "ContentsChanged", Array.Empty<string>());
        }
        finally
        {
            if (reserved && locker is not null)
                Gx.Call(destination, "RemoveSlotLocks", new[] { "NetworkObject" }, locker);
        }

        return moved;
    }

    /// <summary>Drops any lock this NPC still holds on an entity. Safe to call when there is none.</summary>
    internal static void ReleaseLocks(object? destination, object? locker)
    {
        if (destination is null || locker is null)
            return;

        Gx.Call(destination, "RemoveSlotLocks", new[] { "NetworkObject" }, locker);
    }

    // ── Storage ─────────────────────────────────────────────────────────────────────────────────

    internal static int UnitsInStorage(object? storage)
    {
        var total = 0;
        foreach (var slot in Gx.List(Gx.Get(storage, "ItemSlots")))
            total += Gx.Get(slot, "Quantity") as int? ?? 0;

        return total;
    }

    internal static string DescribeStorage(object? storage)
    {
        var parts = new List<string>();
        foreach (var slot in Gx.List(Gx.Get(storage, "ItemSlots")))
        {
            var instance = Gx.Get(slot, "ItemInstance");
            if (instance is null)
                continue;

            var quantity = Gx.Get(slot, "Quantity") as int? ?? 0;
            if (quantity <= 0)
                continue;

            parts.Add($"{quantity}x {Gx.Get<string>(instance, "Name", "item")}");
        }

        return parts.Count == 0 ? "empty" : string.Join(", ", parts);
    }

    /// <summary>Rough unit capacity of a container, used for the default departure threshold.</summary>
    internal static int UnitCapacity(object? storage, string itemId)
    {
        var slots = Gx.Get(storage, "SlotCount") as int? ?? 0;
        if (slots <= 0)
            return 0;

        var stack = 10;
        foreach (var slot in Gx.List(Gx.Get(storage, "ItemSlots")))
        {
            var instance = Gx.Get(slot, "ItemInstance");
            if (instance is null)
                continue;

            if (itemId.Length > 0 &&
                !string.Equals(Gx.Get<string>(instance, "ID", string.Empty), itemId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (Gx.Get(instance, "StackLimit") is int limit && limit > 0)
            {
                stack = limit;
                break;
            }
        }

        return slots * stack;
    }

    internal static object? SlotTypeOutput() => Gx.EnumValue(GameTypes.SlotType, "Output") ?? OutputSlots;
}

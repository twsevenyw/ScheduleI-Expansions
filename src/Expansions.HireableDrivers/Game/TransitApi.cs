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

    internal static int InputCapacity(object? transit, object? item, object? npc) =>
        Gx.Call(
            transit,
            "GetInputCapacityForItem",
            new[] { "ItemInstance", "NPC", "Boolean" },
            item,
            npc,
            true) as int? ?? 0;

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
    internal static object? FindOutputSlot(object? transit, string itemId, object? nativeRoute = null)
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

            // A blacklist or multi-item whitelist cannot be represented by DriverRoute.ItemId. Ask
            // the live AdvancedTransitRoute's own filter when it is available, so the clipboard means
            // exactly what it says rather than degrading those shapes to "anything".
            var filter = Gx.Get(nativeRoute, "Filter");
            if (filter is not null &&
                Gx.Call(filter, "DoesItemMeetFilter", new[] { "ItemInstance" }, instance) is false)
            {
                continue;
            }

            return slot;
        }

        return null;
    }

    internal static string ItemIdInSlot(object? slot) =>
        Gx.Get<string>(Gx.Get(slot, "ItemInstance"), "ID", string.Empty);

    internal static object? ItemInSlot(object? slot) => Gx.Get(slot, "ItemInstance");

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

        var id = Gx.Get<string>(instance, "ID", string.Empty);
        var before = UnitsInStorage(storage, id);

        if (!Gx.TryCall(slot, "ChangeQuantity", new[] { "Int32", "Boolean" }, -amount, false))
            return 0;

        Gx.TryCall(storage, "InsertItem", new[] { "ItemInstance", "Boolean" }, payload, true);
        var actual = Math.Clamp(UnitsInStorage(storage, id) - before, 0, amount);

        if (actual == 0)
        {
            // The source mutation already happened. Put it back before reporting a failed transfer;
            // silently losing product is worse than declining this tick.
            RestoreToSource(source, slot, instance, npc, amount);
            return 0;
        }

        if (actual < amount)
            RestoreToSource(source, slot, instance, npc, amount - actual);

        if (actual > 0)
            Gx.TryCall(storage, "ContentsChanged", Array.Empty<string>());

        return actual;
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
    internal static int StorageToDestination(
        object? storage,
        object? destination,
        object? npc,
        object? locker,
        string itemId,
        int maxUnits,
        IReadOnlyDictionary<IntPtr, int>? protectedQuantities)
    {
        if (storage is null || destination is null || maxUnits <= 0)
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
                if (moved >= maxUnits)
                    break;

                var instance = Gx.Get(slot, "ItemInstance");
                if (instance is null)
                    continue;

                var id = Gx.Get<string>(instance, "ID", string.Empty);
                if (itemId.Length > 0 && !string.Equals(id, itemId, StringComparison.OrdinalIgnoreCase))
                    continue;

                var held = UnprotectedQuantity(
                    slot,
                    Gx.Get(slot, "Quantity") as int? ?? 0,
                    protectedQuantities);
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

                var amount = Math.Min(Math.Min(space, held), maxUnits - moved);
                if (amount <= 0)
                    continue;

                var chunk = Gx.Call(instance, "GetCopy", new[] { "Int32" }, amount);
                if (chunk is null)
                    continue;

                var before = UnitsInTransitSlots(destination, "InputSlots", id);
                if (!Gx.TryCall(slot, "ChangeQuantity", new[] { "Int32", "Boolean" }, -amount, false))
                    continue;

                Gx.TryCall(
                    destination,
                    "InsertItemIntoInput",
                    new[] { "ItemInstance", "NPC" },
                    chunk,
                    npc);

                // The insertion API returns void and can refuse internally. Only the input-slot
                // delta proves receipt; restore every unit that did not appear.
                var actual = Math.Clamp(
                    UnitsInTransitSlots(destination, "InputSlots", id) - before,
                    0,
                    amount);

                if (actual < amount)
                    RestoreToStorage(storage, instance, amount - actual);

                moved += actual;
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

    internal static int UnitsInStorage(object? storage, string itemId)
    {
        if (itemId.Length == 0)
            return UnitsInStorage(storage);

        var total = 0;
        foreach (var slot in Gx.List(Gx.Get(storage, "ItemSlots")))
        {
            var instance = Gx.Get(slot, "ItemInstance");
            if (instance is null ||
                !string.Equals(Gx.Get<string>(instance, "ID", string.Empty), itemId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            total += Gx.Get(slot, "Quantity") as int? ?? 0;
        }

        return total;
    }

    /// <summary>
    /// Per-slot quantities that were already in a vehicle before this trip. Protecting a total count
    /// is insufficient: two stacks with the same item id can carry different quality/additive data,
    /// and unloading the first matching slot would swap the player's original stack for the driver's.
    /// </summary>
    internal static Dictionary<IntPtr, int> CaptureProtectedQuantities(object? storage, string itemId)
    {
        return RestoreProtectedQuantities(storage, CaptureProtectedSlotQuantities(storage, itemId));
    }

    internal static List<int> CaptureProtectedSlotQuantities(object? storage, string itemId)
    {
        var captured = new List<int>();

        foreach (var slot in Gx.List(Gx.Get(storage, "ItemSlots")))
        {
            var instance = Gx.Get(slot, "ItemInstance");
            if (instance is null ||
                !string.Equals(Gx.Get<string>(instance, "ID", string.Empty), itemId, StringComparison.OrdinalIgnoreCase))
            {
                captured.Add(0);
                continue;
            }

            var quantity = Gx.Get(slot, "Quantity") as int? ?? 0;
            captured.Add(Math.Max(0, quantity));
        }

        return captured;
    }

    internal static Dictionary<IntPtr, int> RestoreProtectedQuantities(
        object? storage,
        IReadOnlyList<int>? quantitiesBySlot)
    {
        var restored = new Dictionary<IntPtr, int>();
        if (quantitiesBySlot is null)
            return restored;

        var slots = Gx.List(Gx.Get(storage, "ItemSlots"));
        for (var i = 0; i < slots.Count && i < quantitiesBySlot.Count; i++)
        {
            var pointer = Gx.PointerOf(slots[i]);
            var quantity = Math.Max(0, quantitiesBySlot[i]);
            if (pointer != IntPtr.Zero && quantity > 0)
                restored[pointer] = quantity;
        }

        return restored;
    }

    internal static int UnprotectedUnits(
        object? storage,
        string itemId,
        IReadOnlyDictionary<IntPtr, int>? protectedQuantities)
    {
        var total = 0;
        foreach (var slot in Gx.List(Gx.Get(storage, "ItemSlots")))
        {
            var instance = Gx.Get(slot, "ItemInstance");
            if (instance is null ||
                !string.Equals(Gx.Get<string>(instance, "ID", string.Empty), itemId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            total += UnprotectedQuantity(
                slot,
                Gx.Get(slot, "Quantity") as int? ?? 0,
                protectedQuantities);
        }

        return total;
    }

    internal static int UnprotectedQuantity(
        object? slot,
        int held,
        IReadOnlyDictionary<IntPtr, int>? protectedQuantities)
    {
        if (held <= 0 || protectedQuantities is null)
            return Math.Max(0, held);

        var pointer = Gx.PointerOf(slot);
        var protectedAmount = pointer != IntPtr.Zero && protectedQuantities.TryGetValue(pointer, out var quantity)
            ? quantity
            : 0;

        return Math.Max(0, held - protectedAmount);
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

    private static int UnitsInTransitSlots(object? transit, string member, string itemId)
    {
        var total = 0;
        foreach (var slot in Gx.List(Gx.Get(transit, member)))
        {
            var instance = Gx.Get(slot, "ItemInstance");
            if (instance is null ||
                !string.Equals(Gx.Get<string>(instance, "ID", string.Empty), itemId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            total += Gx.Get(slot, "Quantity") as int? ?? 0;
        }

        return total;
    }

    private static void RestoreToStorage(object? storage, object? template, int amount)
    {
        if (amount <= 0)
            return;

        var itemId = Gx.Get<string>(template, "ID", string.Empty);
        var before = UnitsInStorage(storage, itemId);
        var rollback = Gx.Call(template, "GetCopy", new[] { "Int32" }, amount);
        if (rollback is null)
        {
            DriverLog.Error($"A failed destination transfer could not restore {amount} item(s) to the vehicle.");
            return;
        }

        Gx.TryCall(storage, "InsertItem", new[] { "ItemInstance", "Boolean" }, rollback, true);
        var restored = Math.Clamp(UnitsInStorage(storage, itemId) - before, 0, amount);
        if (restored < amount)
        {
            DriverLog.Error(
                $"A failed destination transfer restored only {restored}/{amount} item(s) to the vehicle.");
        }

        Gx.TryCall(storage, "ContentsChanged", Array.Empty<string>());
    }

    private static void RestoreToSource(object? source, object? slot, object? template, object? npc, int amount)
    {
        if (amount <= 0)
            return;

        // ChangeQuantity may have cleared ItemInstance when it hit zero. Reusing that now-empty slot
        // would not restore the item definition, so only take the cheap path while the template still
        // matches; otherwise use the transit entity's own output insertion routine.
        var current = Gx.Get(slot, "ItemInstance");
        var expectedId = Gx.Get<string>(template, "ID", string.Empty);
        if (current is not null &&
            string.Equals(Gx.Get<string>(current, "ID", string.Empty), expectedId, StringComparison.OrdinalIgnoreCase) &&
            Gx.TryCall(slot, "ChangeQuantity", new[] { "Int32", "Boolean" }, amount, false))
        {
            return;
        }

        var rollback = Gx.Call(template, "GetCopy", new[] { "Int32" }, amount);
        if (rollback is null ||
            !Gx.TryCall(source, "InsertItemIntoOutput", new[] { "ItemInstance", "NPC" }, rollback, npc))
        {
            DriverLog.Error($"A failed vehicle load could not restore {amount} item(s) to the pickup.");
        }
    }
}

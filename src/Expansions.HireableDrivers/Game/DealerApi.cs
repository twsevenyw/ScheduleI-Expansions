using UnityEngine;

namespace Expansions.HireableDrivers.Game;

/// <summary>
/// Dealers as delivery destinations.
/// <para>
/// A <c>Dealer</c> is an <c>NPC</c>, not an <c>ITransitEntity</c>: it has no access points, no
/// input/output slot split and no slot-reservation API, and it walks around. Every difference from a
/// storage destination is handled here.
/// </para>
/// </summary>
internal static class DealerApi
{
    internal static IReadOnlyList<object?> Recruited()
    {
        var dealers = new List<object?>();
        foreach (var dealer in Gx.List(Gx.GetStatic(GameTypes.Dealer, "AllPlayerDealers")))
        {
            if (Gx.Alive(dealer) && Gx.Get(dealer, "IsRecruited") is true)
                dealers.Add(dealer);
        }

        return dealers;
    }

    internal static object? FindById(string id)
    {
        if (string.IsNullOrEmpty(id))
            return null;

        foreach (var dealer in Gx.List(Gx.GetStatic(GameTypes.Dealer, "AllPlayerDealers")))
        {
            if (Gx.Alive(dealer) && string.Equals(Gx.Get<string>(dealer, "ID", string.Empty), id, StringComparison.Ordinal))
                return dealer;
        }

        return null;
    }

    internal static string Id(object? dealer) => Gx.Get<string>(dealer, "ID", string.Empty);

    internal static string Name(object? dealer)
    {
        var full = Gx.Get<string>(dealer, "FullName", string.Empty);
        return string.IsNullOrWhiteSpace(full) ? Id(dealer) : full;
    }

    internal static bool IsRecruited(object? dealer) => Gx.Get(dealer, "IsRecruited") is true;

    internal static int HeldItems(object? dealer)
    {
        var total = 0;
        var seen = new HashSet<IntPtr>();

        foreach (var slot in Gx.List(Gx.Get(dealer, "ItemSlots"))
                     .Concat(Gx.List(Gx.Get(dealer, "overflowSlots"))))
        {
            var pointer = Gx.PointerOf(slot);
            if (pointer != IntPtr.Zero && !seen.Add(pointer))
                continue;

            total += Math.Max(0, Gx.Get(slot, "Quantity") as int? ?? 0);
        }

        // A renamed slot member should not turn every dealer into "empty"; retain the shipped
        // aggregate as the fallback while Gx records the binding failure for diagnostics.
        return seen.Count > 0
            ? total
            : Gx.Call(dealer, "GetTotalInventoryItemCount", Array.Empty<string>()) as int? ?? 0;
    }

    internal static Vector3 Position(object? dealer) =>
        Gx.GetAlive(dealer, "transform") is Transform transform ? transform.position : Vector3.zero;

    /// <summary>
    /// Where to aim the vehicle. Dealers move, so the parking target is their home building rather
    /// than wherever they happen to be standing when the trip is planned.
    /// </summary>
    internal static Vector3 HomePosition(object? dealer)
    {
        var home = Gx.GetAlive(dealer, "Home");
        if (Gx.GetAlive(home, "transform") is Transform transform)
            return transform.position;

        return Position(dealer);
    }

    /// <summary>
    /// Hands cargo over in one shot. There is no reservation API on a dealer, so the transfer has to
    /// be atomic within a single tick rather than spread over several like a storage deposit.
    /// </summary>
    internal static int StorageToDealer(
        object? storage,
        object? dealer,
        int cap,
        string itemId,
        int maxUnits,
        IReadOnlyDictionary<IntPtr, int>? protectedQuantities)
    {
        if (storage is null || dealer is null || maxUnits <= 0)
            return 0;

        if (Gx.Get(storage, "IsOpened") is true || Gx.GetAlive(storage, "CurrentPlayerAccessor") is not null)
            return 0;

        var room = cap - HeldItems(dealer);
        if (room <= 0)
            return 0;

        var moved = 0;
        var storageTouched = false;

        foreach (var slot in Gx.List(Gx.Get(storage, "ItemSlots")))
        {
            if (room <= 0 || moved >= maxUnits)
                break;

            var instance = Gx.Get(slot, "ItemInstance");
            if (instance is null)
                continue;

            var id = Gx.Get<string>(instance, "ID", string.Empty);
            if (itemId.Length > 0 && !string.Equals(id, itemId, StringComparison.OrdinalIgnoreCase))
                continue;

            var held = TransitApi.UnprotectedQuantity(
                slot,
                Gx.Get(slot, "Quantity") as int? ?? 0,
                protectedQuantities);
            if (held <= 0)
                continue;

            var amount = Math.Min(Math.Min(held, room), maxUnits - moved);
            var chunk = Gx.Call(instance, "GetCopy", new[] { "Int32" }, amount);
            if (chunk is null)
                continue;

            var before = HeldItems(dealer);
            if (!Gx.TryCall(slot, "ChangeQuantity", new[] { "Int32", "Boolean" }, -amount, false))
                continue;

            storageTouched = true;
            Gx.TryCall(dealer, "AddItemToInventory", new[] { "ItemInstance" }, chunk);

            // AddItemToInventory returns void and may silently reject. Invocation success proves
            // nothing; only the inventory delta is authoritative.
            var actual = Math.Clamp(HeldItems(dealer) - before, 0, amount);

            if (actual < amount)
            {
                var storageBefore = TransitApi.UnitsInStorage(storage, id);
                var rollback = Gx.Call(instance, "GetCopy", new[] { "Int32" }, amount - actual);
                if (rollback is null)
                {
                    DriverLog.Error($"A failed dealer transfer could not restore {amount - actual} item(s) to the vehicle.");
                }
                else
                {
                    Gx.TryCall(storage, "InsertItem", new[] { "ItemInstance", "Boolean" }, rollback, true);
                    var restored = Math.Clamp(
                        TransitApi.UnitsInStorage(storage, id) - storageBefore,
                        0,
                        amount - actual);
                    if (restored < amount - actual)
                    {
                        DriverLog.Error(
                            $"A failed dealer transfer restored only {restored}/{amount - actual} item(s) to the vehicle.");
                    }
                }
            }

            moved += actual;
            room -= actual;
        }

        if (storageTouched)
            Gx.TryCall(storage, "ContentsChanged", Array.Empty<string>());

        if (moved > 0)
        {
            // Anything that spilled into the hidden overflow slots is folded back in by the game's own
            // routine, which is also what makes the dealer start selling it.
            Gx.Call(dealer, "TryMoveOverflowItems", Array.Empty<string>());
        }

        return moved;
    }
}

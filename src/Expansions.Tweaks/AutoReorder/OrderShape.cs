using System.Globalization;
using System.Text;
using Expansions.Core.Diagnostics;
using Expansions.Tweaks.Runtime;

namespace Expansions.Tweaks.AutoReorder;

/// <summary>
/// What makes two deliveries "the same order": the store, where it is going, which bay, and the
/// exact basket.
/// <para>
/// A <c>DeliveryID</c> is per-order and changes on every repeat, so it cannot be the identity of a
/// standing order. Content can, and it is also what the player means when they tick the box: repeat
/// <em>this</em>, not "whatever the last order from that shop was".
/// </para>
/// </summary>
internal static class OrderShape
{
    /// <summary>
    /// Reads the four identity members off either a <c>DeliveryReceipt</c> or a
    /// <c>DeliveryInstance</c> — they declare the same four, which is why one reader serves both.
    /// Returns false when the object is not a delivery-shaped thing at all.
    /// </summary>
    internal static bool TryRead(object? source, out OrderContents contents)
    {
        contents = OrderContents.Empty;

        if (source is null)
            return false;

        var store = Members.Read(source, "StoreName", string.Empty);
        var destination = Members.Read(source, "DestinationCode", string.Empty);

        if (store.Length == 0)
            return false;

        var dock = Members.Read(source, "LoadingDockIndex", 0);
        var items = ReadItems(source);

        contents = new OrderContents(store, destination, dock, items);
        return true;
    }

    /// <summary>
    /// The basket, as the game stores it: an <c>Il2CppReferenceArray&lt;StringIntPair&gt;</c> of item
    /// id and quantity. Zero and negative quantities are dropped — they would make two identical
    /// baskets hash differently depending on how the order was built.
    /// </summary>
    internal static IReadOnlyList<OrderItem> ReadItems(object? source)
    {
        var items = new List<OrderItem>();
        var raw = Members.ReadObject(source, "Items");

        foreach (var entry in GameReflection.Enumerate(raw, cap: 512))
        {
            var id = Members.Read(entry, "String", string.Empty);
            var quantity = Members.Read(entry, "Int", 0);

            if (id.Length == 0 || quantity <= 0)
                continue;

            items.Add(new OrderItem(id, quantity));
        }

        return items;
    }

    /// <summary>
    /// Stable, case-insensitive, order-insensitive key. Two baskets holding the same items in a
    /// different sequence are the same standing order, so the items are sorted before hashing.
    /// </summary>
    internal static string KeyFor(string store, string destination, int dock, IReadOnlyList<OrderItem> items)
    {
        var sorted = items
            .Where(static i => i.Quantity > 0 && i.Id.Length > 0)
            .GroupBy(static i => i.Id, StringComparer.OrdinalIgnoreCase)
            .Select(static g => new OrderItem(g.Key.ToLowerInvariant(), g.Sum(static i => i.Quantity)))
            .OrderBy(static i => i.Id, StringComparer.Ordinal)
            .ToArray();

        var builder = new StringBuilder(64);
        builder.Append(store.Trim().ToLowerInvariant()).Append('|');
        builder.Append(destination.Trim().ToLowerInvariant()).Append('|');
        builder.Append(dock.ToString(CultureInfo.InvariantCulture)).Append('|');

        for (var i = 0; i < sorted.Length; i++)
        {
            if (i > 0)
                builder.Append(',');

            builder.Append(sorted[i].Id).Append(':').Append(sorted[i].Quantity.ToString(CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    /// <summary>"3 x Baggie, 20 x Soil" — short enough for a notification and a probe cell.</summary>
    internal static string Describe(IReadOnlyList<OrderItem> items, int maxShown = 4)
    {
        if (items.Count == 0)
            return "nothing";

        var shown = items
            .Take(maxShown)
            .Select(static i => $"{i.Quantity} x {i.Id}");

        var text = string.Join(", ", shown);
        return items.Count > maxShown ? $"{text} (+{items.Count - maxShown} more)" : text;
    }
}

/// <summary>One line of a basket: the shop listing's item id and how many were ordered.</summary>
internal readonly struct OrderItem
{
    internal OrderItem(string id, int quantity)
    {
        Id = id;
        Quantity = quantity;
    }

    internal string Id { get; }

    internal int Quantity { get; }
}

/// <summary>A delivery's identity, read off a live instance or a past receipt.</summary>
internal readonly struct OrderContents
{
    internal static readonly OrderContents Empty =
        new(string.Empty, string.Empty, 0, Array.Empty<OrderItem>());

    internal OrderContents(string store, string destination, int dock, IReadOnlyList<OrderItem> items)
    {
        Store = store;
        Destination = destination;
        Dock = dock;
        Items = items;
    }

    internal string Store { get; }

    internal string Destination { get; }

    internal int Dock { get; }

    internal IReadOnlyList<OrderItem> Items { get; }

    internal bool IsEmpty => Store.Length == 0;

    internal string Key => OrderShape.KeyFor(Store, Destination, Dock, Items);
}

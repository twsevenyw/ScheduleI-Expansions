namespace Expansions.Tweaks.AutoReorder;

/// <summary>
/// One ticked delivery: the order to keep placing, plus everything the engine knows about why it
/// is or is not placing it right now.
/// <para>
/// The persisted half is <see cref="Data"/>; the rest is per-session and is rebuilt from the live
/// game on load. A standing order is never cancelled by the engine — only by the player unticking
/// it — so there is deliberately no "give up" state here, only a reason and a retry time.
/// </para>
/// </summary>
internal sealed class StandingOrder
{
    internal StandingOrder(StandingOrderData data)
    {
        Data = data;
    }

    internal StandingOrderData Data { get; }

    internal string Key => Data.Key;

    internal string Store => Data.StoreName;

    internal string Destination => Data.DestinationCode;

    internal int Dock => Data.LoadingDockIndex;

    internal IReadOnlyList<OrderItem> Items =>
        Data.Items.Select(static i => new OrderItem(i.Id, i.Quantity)).ToArray();

    /// <summary>The delivery this order is currently waiting on, or empty when nothing is in flight.</summary>
    internal string PendingDeliveryId
    {
        get => Data.PendingDeliveryId;
        set => Data.PendingDeliveryId = value ?? string.Empty;
    }

    /// <summary>Why nothing is happening, in the player's words. Empty means it is about to fire.</summary>
    internal string WaitReason { get; set; } = "starting up";

    /// <summary>Set while an order has been submitted but no new delivery has appeared yet.</summary>
    internal float ConfirmDeadline { get; set; }

    /// <summary><c>Time.realtimeSinceStartup</c> before which no further attempt is made.</summary>
    internal float RetryAfter { get; set; }

    /// <summary>Consecutive submissions that produced no delivery. Drives the retry back-off.</summary>
    internal int Failures { get; set; }

    internal bool AwaitingConfirmation => ConfirmDeadline > 0f;

    internal string Summary =>
        $"{OrderShape.Describe(Items)} from {Store} to {DestinationLabel} (bay {Dock + 1})";

    internal string DestinationLabel => Destination.Length == 0 ? "your property" : Destination;

    /// <summary>Recomputes the content key, so a hand-edited save can never carry a stale one.</summary>
    internal void RefreshKey() => Data.Key = OrderShape.KeyFor(Store, Destination, Dock, Items);
}

/// <summary>The persisted half of a standing order. Plain data — Newtonsoft writes it verbatim.</summary>
public sealed class StandingOrderData
{
    public string Key { get; set; } = string.Empty;

    public string StoreName { get; set; } = string.Empty;

    public string DestinationCode { get; set; } = string.Empty;

    public int LoadingDockIndex { get; set; }

    public List<OrderItemData> Items { get; set; } = new();

    /// <summary>Survives a reload so a delivery in flight when the game closed is still waited on.</summary>
    public string PendingDeliveryId { get; set; } = string.Empty;

    /// <summary>How many times this standing order has placed itself. Shown in the probe.</summary>
    public int PlacedCount { get; set; }
}

public sealed class OrderItemData
{
    public string Id { get; set; } = string.Empty;

    public int Quantity { get; set; }
}

/// <summary>The whole per-save blob.</summary>
public sealed class AutoReorderSaveData
{
    /// <summary>Bumped only when the shape changes in a way an older build would misread.</summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Which save these orders belong to, as the save folder path.
    /// <para>
    /// S1API builds one blob object for the whole process and reuses it across save loads, and a save
    /// that has no file of ours leaves the previous save's fields exactly where they were. Without a
    /// stamp to compare, loading a second save would inherit the first save's ticks and start
    /// spending that player's money on orders they never placed.
    /// </para>
    /// </summary>
    public string SaveId { get; set; } = string.Empty;

    public List<StandingOrderData> Orders { get; set; } = new();
}

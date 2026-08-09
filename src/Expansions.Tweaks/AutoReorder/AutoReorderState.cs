using Expansions.Core.Diagnostics;
using Expansions.Tweaks.Runtime;
using S1API.Internal.Abstraction;
using S1API.Saveables;

namespace Expansions.Tweaks.AutoReorder;

/// <summary>
/// Per-save storage for the ticked deliveries.
/// <para>
/// Subclasses S1API's <c>Saveable</c> rather than implementing <c>ISaveable</c> directly. S1API
/// writes into the save slot's <c>Modded\</c> subtree, which vanilla's <c>DeleteUnapprovedFiles</c>
/// never sweeps, and it owns the postfixes on the save path already. A throw out of a hand-rolled
/// <c>GetSaveString()</c> would run inside the vanilla save coroutine and could abort the player's
/// save, so nothing here is allowed to escape.
/// </para>
/// <para>
/// S1API discovers direct <c>Saveable</c> inheritors by reflection and constructs exactly one of
/// each, whether or not the module is enabled. That makes the public parameterless constructor load
/// bearing, and it means this type publishes itself through <see cref="Current"/> instead of being
/// newed up by the feature.
/// </para>
/// </summary>
public sealed class AutoReorderState : Saveable
{
    /// <summary>Refuses to read a blob written by a newer build rather than misinterpret it.</summary>
    private const int SupportedVersion = 1;

    /// <summary>
    /// Stamp for a blob that was genuinely read for the loaded save but could not name it, because
    /// the save path was unreadable at that moment. Adopted rather than discarded: the data is known
    /// to be this save's, only its label is missing.
    /// </summary>
    private const string Unstamped = "*";

    [SaveableField("tweaks_auto_reorder")]
    public AutoReorderSaveData Data = new();

    public AutoReorderState() => Current = this;

    /// <summary>Fires once the loaded save's standing orders have been read and sanitised.</summary>
    internal static event Action? Loaded;

    internal static AutoReorderState? Current { get; private set; }

    /// <summary>After the base game, so the delivery manager and the shops already exist.</summary>
    public override SaveableLoadOrder LoadOrder => SaveableLoadOrder.AfterBaseGame;

    /// <summary>The loaded save's orders, or an empty list when no save has been read yet.</summary>
    internal static List<StandingOrderData> Orders => Current?.Data?.Orders ?? new List<StandingOrderData>();

    /// <summary>Adds or replaces an order by content key. Returns false if it was already there.</summary>
    internal static bool Arm(StandingOrderData order)
    {
        var current = Current;
        if (current is null)
            return false;

        current.Data ??= new AutoReorderSaveData();
        current.Data.Orders ??= new List<StandingOrderData>();

        foreach (var existing in current.Data.Orders)
        {
            if (string.Equals(existing.Key, order.Key, StringComparison.Ordinal))
                return false;
        }

        current.Data.Orders.Add(order);
        return true;
    }

    internal static bool Disarm(string key)
    {
        var orders = Current?.Data?.Orders;
        if (orders is null)
            return false;

        for (var i = orders.Count - 1; i >= 0; i--)
        {
            if (string.Equals(orders[i].Key, key, StringComparison.Ordinal))
            {
                orders.RemoveAt(i);
                return true;
            }
        }

        return false;
    }

    internal static int DisarmAll()
    {
        var orders = Current?.Data?.Orders;
        if (orders is null || orders.Count == 0)
            return 0;

        var count = orders.Count;
        orders.Clear();
        return count;
    }

    /// <summary>
    /// Makes sure the blob in memory belongs to the save that is actually loaded, and says why not
    /// when it cannot tell.
    /// <para>
    /// Load-bearing, and the reason <see cref="AutoReorderSaveData.SaveId"/> exists. A save with no
    /// file of ours never calls <see cref="OnLoaded"/>, so its blob is whatever the last save left
    /// behind. Comparing the stamp catches that without a race against the load itself, and without
    /// ever destroying data that might still be the loaded save's.
    /// </para>
    /// </summary>
    internal static bool BindToLoadedSave(out string reason)
    {
        var current = Current;
        if (current is null)
        {
            reason = "S1API has not built the auto-reorder save blob yet";
            return false;
        }

        var id = CurrentSaveId();
        if (id.Length == 0)
        {
            reason = "no save is loaded";
            return false;
        }

        current.Data ??= new AutoReorderSaveData();
        reason = string.Empty;

        if (string.Equals(current.Data.SaveId, id, StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.Equals(current.Data.SaveId, Unstamped, StringComparison.Ordinal))
        {
            current.Data.SaveId = id;
            return true;
        }

        var carried = current.Data.Orders?.Count ?? 0;
        current.Data = new AutoReorderSaveData { SaveId = id };

        if (carried > 0)
        {
            TweakLog.Detail(
                $"Dropped {carried} standing auto-reorder(s): they were stamped for a different save than the one loaded.");
        }

        return true;
    }

    /// <summary>The loaded save's folder, or empty when no save is loaded.</summary>
    internal static string CurrentSaveId()
    {
        if (!GameReflection.TryGetSingleton(AutoReorderTypes.SaveManager, out var manager, out _))
            return string.Empty;

        return Members.Read(manager, "PlayersSavePath", string.Empty).Trim();
    }

    internal static bool IsArmed(string key)
    {
        var orders = Current?.Data?.Orders;
        if (orders is null)
            return false;

        foreach (var order in orders)
        {
            if (string.Equals(order.Key, key, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    protected override void OnLoaded()
    {
        try
        {
            Data ??= new AutoReorderSaveData();

            if (Data.Version > SupportedVersion)
            {
                TweakLog.Warn(
                    $"This save's auto-reorder data is version {Data.Version} but this build reads {SupportedVersion}. " +
                    "Ignoring it rather than half-reading it — no delivery will repeat itself until you tick it again.");

                Data = new AutoReorderSaveData();
            }

            Data.Version = SupportedVersion;
            Data.Orders ??= new List<StandingOrderData>();
            Sanitise(Data.Orders);

            // Stamped here rather than trusted from the file: this data was just read for the save
            // being loaded, whatever the file happened to claim.
            var id = CurrentSaveId();
            Data.SaveId = id.Length == 0 ? Unstamped : id;

            TweakLog.Detail($"Loaded {Data.Orders.Count} standing auto-reorder(s).");
        }
        catch (Exception ex)
        {
            // Never let this reach the vanilla load path.
            TweakLog.Error("Reading the auto-reorder save data failed; this save starts with no standing orders.", ex);
            Data = new AutoReorderSaveData();
        }

        try
        {
            Loaded?.Invoke();
        }
        catch (Exception ex)
        {
            TweakLog.Error("Rebinding standing auto-reorders after load failed.", ex);
        }
    }

    protected override void OnSaved() =>
        TweakLog.Detail($"Saved {Data?.Orders?.Count ?? 0} standing auto-reorder(s).");

    /// <summary>
    /// Drops anything a hand-edited or half-written blob could leave behind, and recomputes every
    /// content key so a stale one can never make a tick box and its standing order disagree.
    /// </summary>
    private static void Sanitise(List<StandingOrderData> orders)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var i = orders.Count - 1; i >= 0; i--)
        {
            var order = orders[i];

            if (order is null || string.IsNullOrWhiteSpace(order.StoreName))
            {
                orders.RemoveAt(i);
                continue;
            }

            order.DestinationCode ??= string.Empty;
            order.PendingDeliveryId ??= string.Empty;
            order.Items ??= new List<OrderItemData>();

            for (var item = order.Items.Count - 1; item >= 0; item--)
            {
                var line = order.Items[item];
                if (line is null || string.IsNullOrWhiteSpace(line.Id) || line.Quantity <= 0)
                    order.Items.RemoveAt(item);
            }

            if (order.Items.Count == 0)
            {
                orders.RemoveAt(i);
                continue;
            }

            order.Key = OrderShape.KeyFor(
                order.StoreName,
                order.DestinationCode,
                order.LoadingDockIndex,
                order.Items.Select(static l => new OrderItem(l.Id, l.Quantity)).ToArray());

            if (!seen.Add(order.Key))
                orders.RemoveAt(i);
        }
    }
}

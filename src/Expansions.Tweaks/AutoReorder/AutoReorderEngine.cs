using System.Globalization;
using Expansions.Core.Actions;
using Expansions.Tweaks.Runtime;
using UnityEngine;

namespace Expansions.Tweaks.AutoReorder;

/// <summary>
/// Keeps every ticked delivery standing.
/// <para>
/// <b>"As soon as it can" means exactly this:</b> the moment the game itself would let the player
/// press Reorder on that order, with two extra conditions of our own — the deliveries app must be
/// closed (so a repeat can never overwrite a cart the player is filling) and paying must not take
/// the balance below <c>auto_reorder_min_balance</c>. Everything else is asked of the game:
/// <c>DeliveryApp.CanReorder</c> covers stock, unlocks, loading bays, affordability and whether the
/// receipt still describes something orderable, and the store having no delivery in flight covers
/// "the previous one has actually arrived", because a delivery only leaves the live list once it has
/// been unloaded.
/// </para>
/// <para>
/// A failed condition never cancels the standing order. It sets a reason, waits, and tries again;
/// the reason is what the probe and the Actions pane report. The only thing that stops a standing
/// order is the player unticking it.
/// </para>
/// </summary>
internal sealed class AutoReorderEngine
{
    /// <summary>Deliveries take in-game hours. Once a second is responsive and costs nothing.</summary>
    private const float TickSeconds = 1f;

    /// <summary>
    /// How long a submitted order has to turn into a delivery before it counts as a failure.
    /// <c>SendDelivery</c> is a server RPC, so on the host it lands the same frame — this is slack
    /// for a busy frame, not an expected wait.
    /// </summary>
    private const float ConfirmWindowSeconds = 8f;

    private const float BaseRetrySeconds = 5f;

    private const float MaxRetrySeconds = 60f;

    /// <summary><c>EDeliveryStatus.Completed</c>. InTransit/Waiting/Arrived are 0/1/2.</summary>
    private const int Completed = 3;

    private readonly AutoReorderSettings _settings;
    private readonly Dictionary<string, StandingOrder> _orders = new(StringComparer.Ordinal);

    /// <summary>
    /// When each store last had a repeat submitted to it.
    /// <para>
    /// Two standing orders from the same store would otherwise both fire in the same pass: the
    /// second one's <c>HasActiveDelivery</c> check is only reliable once the server RPC behind
    /// <c>SubmitOrder</c> has actually registered the delivery, and "probably synchronous" is not
    /// good enough when being wrong means charging the player twice.
    /// </para>
    /// </summary>
    private readonly Dictionary<string, float> _submittedAt = new(StringComparer.OrdinalIgnoreCase);

    private float _nextTick;

    internal AutoReorderEngine(AutoReorderSettings settings)
    {
        _settings = settings;
    }

    /// <summary>Why nothing at all is being attempted, when that is true for every order at once.</summary>
    internal string Blocker { get; private set; } = "starting up";

    /// <summary>Live view of the standing orders, ordered so two probe runs agree.</summary>
    internal IReadOnlyList<StandingOrder> Orders =>
        _orders.Values.OrderBy(static o => o.Store, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static o => o.Key, StringComparer.Ordinal)
            .ToArray();

    /// <summary>Drops all per-session state. The persisted ticks are deliberately left alone.</summary>
    internal void Stop()
    {
        _orders.Clear();
        _submittedAt.Clear();
        Blocker = "the module is switched off";
    }

    /// <summary>Forgets the in-flight bookkeeping after a save load, keeping the ticks.</summary>
    internal void Rebind()
    {
        _orders.Clear();
        _submittedAt.Clear();
        _nextTick = 0f;
    }

    internal void Tick()
    {
        var now = Time.realtimeSinceStartup;
        if (now < _nextTick)
            return;

        _nextTick = now + TickSeconds;

        // Before anything is read out of the blob: it is one object reused for the whole process, so
        // it has to be proved to belong to the save that is loaded.
        if (!AutoReorderState.BindToLoadedSave(out var binding))
        {
            _orders.Clear();
            Blocker = binding;
            return;
        }

        // Synced even when the tweak is off, so the probe can still list what is armed and say that
        // the switch is the only thing holding it.
        Sync();

        if (!_settings.Enabled.Value)
        {
            SetAll("the auto-reorder tweak is switched off");
            return;
        }

        if (_orders.Count == 0)
        {
            Blocker = string.Empty;
            return;
        }

        if (!AutoReorderHostGate.Evaluate(out var authority))
        {
            SetAll(authority);
            return;
        }

        var manager = DeliveryApi.Manager();
        if (manager is null)
        {
            SetAll("no loaded game yet");
            return;
        }

        var app = DeliveryApi.App();
        if (app is null)
        {
            SetAll("the phone's deliveries app has not started yet");
            return;
        }

        if (DeliveryApi.IsAppOpen(app))
        {
            SetAll("waiting until you close the deliveries app, so a repeat cannot overwrite your cart");
            return;
        }

        Blocker = string.Empty;

        var live = ReadLiveDeliveries(manager);

        // A snapshot, because following a moved loading bay re-keys the dictionary mid-loop.
        foreach (var order in _orders.Values.ToArray())
            Evaluate(order, manager, app, live, now);
    }

    /// <summary>
    /// Arms a standing order from anything delivery-shaped — a live <c>DeliveryInstance</c> off an
    /// active row, or a <c>DeliveryReceipt</c> off a past row. Returns the key, or empty on refusal.
    /// </summary>
    internal string Arm(object? source, out string message)
    {
        if (!OrderShape.TryRead(source, out var contents))
        {
            message = "that is not a delivery this build can read.";
            return string.Empty;
        }

        return Arm(contents, Members.Read(source, "DeliveryID", string.Empty), out message);
    }

    /// <summary>
    /// Arms a standing order from contents already read off a row, so the phone's tick box and the
    /// menu actions go through one path.
    /// </summary>
    internal string Arm(OrderContents contents, string pendingDeliveryId, out string message)
    {
        if (contents.IsEmpty || contents.Items.Count == 0)
        {
            message = "that delivery has nothing orderable in it.";
            return string.Empty;
        }

        if (!AutoReorderState.BindToLoadedSave(out var binding))
        {
            message = $"that cannot be remembered right now - {binding}.";
            return string.Empty;
        }

        var data = new StandingOrderData
        {
            Key = contents.Key,
            StoreName = contents.Store,
            DestinationCode = contents.Destination,
            LoadingDockIndex = contents.Dock,
            Items = contents.Items.Select(static i => new OrderItemData { Id = i.Id, Quantity = i.Quantity }).ToList(),
            PendingDeliveryId = pendingDeliveryId ?? string.Empty,
        };

        if (!AutoReorderState.Arm(data))
        {
            message = "that order is already set to repeat.";
            return data.Key;
        }

        Sync();
        message =
            $"Repeating {OrderShape.Describe(contents.Items)} from {contents.Store} " +
            "as soon as the last one has arrived and been unloaded.";

        return data.Key;
    }

    internal bool Disarm(string key)
    {
        var removed = AutoReorderState.Disarm(key);
        if (removed)
            Sync();

        return removed;
    }

    internal int DisarmAll()
    {
        var count = AutoReorderState.DisarmAll();
        if (count > 0)
            Sync();

        return count;
    }

    /// <summary>Mirrors the persisted list into the runtime one, preserving per-session state by key.</summary>
    private void Sync()
    {
        var persisted = AutoReorderState.Orders;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var data in persisted)
        {
            if (data is null || string.IsNullOrEmpty(data.Key))
                continue;

            seen.Add(data.Key);

            if (_orders.TryGetValue(data.Key, out var existing) && ReferenceEquals(existing.Data, data))
                continue;

            _orders[data.Key] = new StandingOrder(data);
        }

        // Always swept, never short-circuited on count: one order removed and another added between
        // two ticks leaves the counts equal and a stale entry behind.
        foreach (var key in _orders.Keys.Where(k => !seen.Contains(k)).ToArray())
            _orders.Remove(key);
    }

    private void SetAll(string reason)
    {
        Blocker = reason;

        foreach (var order in _orders.Values)
            order.WaitReason = reason;
    }

    private void Evaluate(StandingOrder order, object manager, object app, IReadOnlyList<Flight> live, float now)
    {
        // Anything from this store that has not been unloaded yet blocks a repeat - that is the
        // game's own one-delivery-per-store rule, and it is also what makes "the previous one has
        // actually arrived" true rather than merely "has been dispatched".
        var flight = live.FirstOrDefault(f => f.MatchesStore(order.Store));

        if (flight is not null)
        {
            if (order.AwaitingConfirmation)
                Confirm(order, flight);

            order.PendingDeliveryId = flight.Id;
            order.WaitReason = flight.SameShapeAs(order.Key)
                ? $"the current one is {flight.StatusText}"
                : $"{order.Store} is already delivering a different order ({flight.StatusText})";
            return;
        }

        order.PendingDeliveryId = string.Empty;

        if (order.AwaitingConfirmation)
        {
            if (now < order.ConfirmDeadline)
            {
                order.WaitReason = "the repeat has just been submitted; waiting for the van";
                return;
            }

            order.ConfirmDeadline = 0f;
            order.Failures++;
            order.RetryAfter = now + Backoff(order.Failures);

            ActionLog.Fail(
                $"Auto-reorder: submitted {order.Summary} but no delivery appeared. " +
                $"Trying again in {Backoff(order.Failures):0}s (attempt {order.Failures}).");
        }

        if (now < order.RetryAfter)
        {
            order.WaitReason =
                $"the last attempt produced no delivery; retrying in {Math.Ceiling(order.RetryAfter - now):0}s";
            return;
        }

        if (_submittedAt.TryGetValue(order.Store, out var submitted) && now - submitted < ConfirmWindowSeconds)
        {
            order.WaitReason = $"another repeat from {order.Store} was just submitted; waiting for it to land";
            return;
        }

        var shop = DeliveryApi.Shop(app, order.Store);
        if (shop is null)
        {
            order.WaitReason = $"'{order.Store}' is not one of the stores in the deliveries app on this save";
            return;
        }

        // Belt and braces: the live-delivery sweep above is name-matched, this is the game's own
        // answer for the same question.
        if (DeliveryApi.HasActiveDelivery(shop))
        {
            order.WaitReason = $"{order.Store} still has a delivery in progress";
            return;
        }

        var receipt = ResolveReceipt(manager, order);
        if (receipt is null)
        {
            order.WaitReason = "the delivery history has no receipt for this order yet";
            return;
        }

        if (!DeliveryApi.IsValidReceipt(app, receipt))
        {
            order.WaitReason = "the game no longer accepts that receipt - an item in it may have stopped being sold";
            return;
        }

        if (!DeliveryApi.CanReorder(app, receipt, out var refusal))
        {
            order.WaitReason = refusal;
            return;
        }

        var cost = DeliveryApi.DeliveryCost(app, receipt);
        var payment = DeliveryApi.PaymentFor(shop, cost);
        var floor = _settings.ResolvedFloor;

        if (payment.Balance - cost < floor)
        {
            order.WaitReason =
                $"paying {Money(cost)} from your {payment.Name} would leave {Money(payment.Balance - cost)}, " +
                $"under your {Money(floor)} floor";
            return;
        }

        Place(order, app, shop, receipt, cost, payment, now);
    }

    private void Place(
        StandingOrder order,
        object app,
        object shop,
        object receipt,
        float cost,
        PaymentSource payment,
        float now)
    {
        // The shop's cart is the staging area SubmitOrder reads. It is only ever non-empty here if
        // the player left items in it and closed the app, so clearing it costs nothing and stops a
        // stale line being bundled into a repeat.
        var stale = DeliveryApi.CartCount(shop);
        if (stale > 0)
        {
            TweakLog.Detail($"Clearing {stale} item(s) left in {order.Store}'s cart before repeating the order.");
            DeliveryApi.ResetCart(shop);
        }

        // Recorded before the call, not after: if Reorder throws half-way the order may still have
        // reached the server, and a second attempt in the same second is the one thing that must not
        // happen.
        _submittedAt[order.Store] = now;

        if (!DeliveryApi.Reorder(app, receipt, out var failure))
        {
            order.Failures++;
            order.RetryAfter = now + Backoff(order.Failures);
            order.WaitReason = $"the game's reorder call did not run ({failure}); retrying";
            ActionLog.Fail($"Auto-reorder: could not repeat {order.Summary} - {failure}");
            return;
        }

        if (DeliveryApi.CartCount(shop) > 0)
            DeliveryApi.ResetCart(shop);

        order.ConfirmDeadline = now + ConfirmWindowSeconds;
        order.WaitReason = "the repeat has just been submitted; waiting for the van";

        ActionLog.Ok($"Auto-reorder: repeated {order.Summary} for {Money(cost)} from your {payment.Name}.");

        if (_settings.Notify.Value)
        {
            DeliveryApi.Notify(
                "Order repeated",
                $"{OrderShape.Describe(order.Items, 3)} from {order.Store} - {Money(cost)}");
        }
    }

    private static void Confirm(StandingOrder order, Flight flight)
    {
        order.ConfirmDeadline = 0f;
        order.Failures = 0;
        order.RetryAfter = 0f;
        order.Data.PlacedCount++;

        TweakLog.Detail($"Auto-reorder confirmed: delivery '{flight.Id}' for {order.Summary}.");
    }

    /// <summary>
    /// The receipt to hand to <c>Reorder</c>, taken from the game's own history so cost, contents and
    /// destination are literally the ones the player ordered before.
    /// <para>
    /// Exact content match first. The relaxed pass ignores only the loading-bay index, which is a
    /// choice of dock rather than part of what was ordered, and never the destination — reordering a
    /// receipt delivers to wherever <em>that receipt</em> says, so matching loosely on destination
    /// could quietly send the goods to a different property.
    /// </para>
    /// </summary>
    private object? ResolveReceipt(object manager, StandingOrder order)
    {
        var history = DeliveryApi.History(manager);
        object? relaxed = null;

        // Newest last, and a reorder moves the receipt it used to the end, so walking backwards finds
        // the copy the game itself considers current.
        for (var i = history.Count - 1; i >= 0; i--)
        {
            var receipt = history[i];
            if (!OrderShape.TryRead(receipt, out var contents))
                continue;

            if (string.Equals(contents.Key, order.Key, StringComparison.Ordinal))
                return receipt;

            if (relaxed is null && SameOrderDifferentBay(contents, order))
                relaxed = receipt;
        }

        if (relaxed is null || !OrderShape.TryRead(relaxed, out var moved))
            return relaxed;

        // Adopt the bay the game actually used, so this stops being the slow path next time. Skipped
        // if some other standing order already owns that key.
        if (!AutoReorderState.IsArmed(moved.Key))
        {
            TweakLog.Detail(
                $"Auto-reorder: the matching '{order.Store}' order moved from bay {order.Dock + 1} to bay " +
                $"{moved.Dock + 1}; following it.");

            var previous = order.Key;
            order.Data.LoadingDockIndex = moved.Dock;
            order.RefreshKey();

            // Re-keyed in place rather than through Sync(), which would replace the StandingOrder and
            // lose the confirmation deadline the caller is about to set on this one.
            _orders.Remove(previous);
            _orders[order.Key] = order;
        }

        return relaxed;
    }

    private static bool SameOrderDifferentBay(OrderContents contents, StandingOrder order) =>
        string.Equals(
            OrderShape.KeyFor(contents.Store, contents.Destination, order.Dock, contents.Items),
            order.Key,
            StringComparison.Ordinal);

    private IReadOnlyList<Flight> ReadLiveDeliveries(object manager)
    {
        var flights = new List<Flight>();

        foreach (var delivery in DeliveryApi.LiveDeliveries(manager))
        {
            var status = Members.Read(delivery, "Status", -1);
            if (status == Completed)
                continue;

            if (!OrderShape.TryRead(delivery, out var contents))
                continue;

            flights.Add(new Flight(
                Members.Read(delivery, "DeliveryID", string.Empty),
                contents,
                status,
                Members.Read(delivery, "TimeUntilArrival", -1)));
        }

        return flights;
    }

    private static float Backoff(int failures) =>
        Math.Min(MaxRetrySeconds, BaseRetrySeconds * (float)Math.Pow(2, Math.Max(0, failures - 1)));

    internal static string Money(float amount) =>
        "$" + Math.Round(amount).ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>A delivery currently on the road, reduced to what the engine reasons about.</summary>
    private sealed class Flight
    {
        internal Flight(string id, OrderContents contents, int status, int minutesToArrival)
        {
            Id = id;
            Contents = contents;
            Status = status;
            MinutesToArrival = minutesToArrival;
        }

        internal string Id { get; }

        internal OrderContents Contents { get; }

        internal int Status { get; }

        internal int MinutesToArrival { get; }

        internal string StatusText => Status switch
        {
            0 => MinutesToArrival > 0
                ? $"on its way, about {MinutesToArrival} in-game minute(s) out"
                : "on its way",
            1 => "waiting for its loading bay to free up",
            2 => "parked and waiting to be unloaded",
            _ => "in progress",
        };

        internal bool MatchesStore(string store) =>
            string.Equals(Contents.Store, store, StringComparison.OrdinalIgnoreCase);

        internal bool SameShapeAs(string key) => string.Equals(Contents.Key, key, StringComparison.Ordinal);
    }
}

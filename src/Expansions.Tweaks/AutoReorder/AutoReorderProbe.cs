using Expansions.Core.Diagnostics;
using Expansions.Tweaks.Runtime;

namespace Expansions.Tweaks.AutoReorder;

/// <summary>
/// The read-only answer to "what is standing, and why has none of it fired?".
/// <para>
/// Every waiting reason in the table is the same string the engine is acting on, not a
/// reconstruction of it — including the game's own wording when <c>CanReorder</c> is the thing
/// refusing. That is the point of the probe: a standing order that never fires has to be
/// self-explaining, because nothing else in the game will explain it.
/// </para>
/// </summary>
internal static class AutoReorderProbe
{
    /// <summary>Matches the string the module's other probes use, so they share one report section.</summary>
    internal const string Area = "Quality of Life";

    internal const string Id = "tweaks.autoreorder.standing_orders";

    internal static IProbe Create(AutoReorderSettings settings, Func<AutoReorderEngine?> engine, Func<AutoReorderUi?> ui) =>
        new DelegateProbe(
            Id,
            "Which deliveries are set to repeat, and what is each one waiting for?",
            Area,
            (_, result) => Run(result, settings, engine(), ui()));

    private static void Run(ProbeResult result, AutoReorderSettings settings, AutoReorderEngine? engine, AutoReorderUi? ui)
    {
        var manager = DeliveryApi.Manager();
        var app = DeliveryApi.App();

        AutoReorderHostGate.Evaluate(out var authority);

        result.Heading("Setup");
        result.Fact("auto_reorder_enabled", settings.Enabled.Value ? "on" : "off");
        result.Fact("auto_reorder_min_balance", AutoReorderEngine.Money(settings.ResolvedFloor));
        result.Fact("auto_reorder_notify", settings.Notify.Value ? "on" : "off");
        result.Fact("This peer", authority);
        result.Fact("DeliveryManager", manager is null ? "not found" : "live");
        result.Fact("DeliveryApp", app is null ? "not found" : DeliveryApi.IsAppOpen(app) ? "live, open" : "live, closed");
        result.Fact("Per-save blob",
            AutoReorderState.BindToLoadedSave(out var binding) ? "bound to the loaded save" : binding);
        result.Fact("Cash / online", $"{AutoReorderEngine.Money(DeliveryApi.CashBalance())} / {AutoReorderEngine.Money(DeliveryApi.OnlineBalance())}");

        result.Heading("Tick boxes");

        if (ui is null)
        {
            result.Bullet("The feature is not attached, so no tick boxes exist.");
        }
        else
        {
            result.Fact("Cloned from", ui.DonorName);
            result.Fact("Rows carrying a box", ui.AttachedCount.ToString());
            result.Fact("Blocked by", ui.Failure.Length == 0 ? "nothing" : ui.Failure);
        }

        if (engine is null)
        {
            result.Inconclusive(
                "Auto-reorder is not running - the Quality of Life module is off, or it never attached. " +
                "Nothing is repeating and nothing is being spent.");
            return;
        }

        var orders = engine.Orders;

        result.Heading("Standing orders");

        if (orders.Count == 0)
        {
            result.Bullet("Nothing is set to repeat.");
        }
        else
        {
            result.Table(
                new[] { "Store", "To", "Bay", "Order", "In flight", "Placed", "Waiting on" },
                orders.Select(order => (IReadOnlyList<string>)new[]
                {
                    order.Store,
                    order.DestinationLabel,
                    (order.Dock + 1).ToString(),
                    OrderShape.Describe(order.Items, 6),
                    order.PendingDeliveryId.Length == 0 ? "-" : order.PendingDeliveryId,
                    order.Data.PlacedCount.ToString(),
                    order.WaitReason.Length == 0 ? "nothing - it fires next tick" : order.WaitReason,
                }).ToList());
        }

        result.Heading("Deliveries the game is tracking");

        var live = manager is null ? Array.Empty<object?>() : DeliveryApi.LiveDeliveries(manager);

        if (live.Count == 0)
        {
            result.Bullet("None.");
        }
        else
        {
            result.Table(
                new[] { "Id", "Store", "Status", "Minutes out" },
                live.Select(delivery => (IReadOnlyList<string>)new[]
                {
                    Members.Read(delivery, "DeliveryID", "?"),
                    Members.Read(delivery, "StoreName", "?"),
                    StatusName(Members.Read(delivery, "Status", -1)),
                    Members.Read(delivery, "TimeUntilArrival", -1).ToString(),
                }).ToList());
        }

        if (manager is null)
        {
            result.NotFound(
                $"'{AutoReorderTypes.DeliveryManager}' is not on this build, so nothing can repeat. " +
                "The game has probably renamed the delivery stack.");
            return;
        }

        if (orders.Count == 0)
        {
            result.Ok(
                "Auto-reorder is running with nothing armed. Open the phone's Deliveries app and tick the " +
                "box on a row to start one.");
            return;
        }

        if (engine.Blocker.Length > 0)
        {
            result.Inconclusive(
                $"{orders.Count} standing order(s), all held up by the same thing: {engine.Blocker}. " +
                "Nothing has been cancelled - they resume the moment that clears.");
            return;
        }

        var ready = orders.Count(static o => o.WaitReason.Length == 0);
        result.Ok(
            $"{orders.Count} standing order(s), {ready} ready to fire. The rest are waiting for the reason " +
            "in their row; none of them has been cancelled.");
    }

    /// <summary><c>EDeliveryStatus</c>, spelled out. Kept local so the probe owns no shared helper.</summary>
    private static string StatusName(int status) => status switch
    {
        0 => "InTransit",
        1 => "Waiting",
        2 => "Arrived",
        3 => "Completed",
        _ => "unknown",
    };
}

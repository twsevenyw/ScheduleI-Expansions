using Expansions.Core.Diagnostics;
using Expansions.Tweaks.Runtime;

namespace Expansions.Tweaks.AutoReorder;

/// <summary>
/// Late-bound facade over the shipped delivery stack.
/// <para>
/// Every order this feature places goes through <c>DeliveryApp.Reorder(DeliveryReceipt)</c> — the
/// exact method behind the Reorder button on a past-orders row — and every decision about whether it
/// <em>may</em> place one goes through <c>DeliveryApp.CanReorder(receipt, out reason)</c>. That is
/// deliberate: cost, contents, destination, loading bay, stock, affordability and the one-delivery-
/// per-store rule are then the game's own answers rather than a second implementation of them that
/// could drift at the next patch. When the game refuses, the reason string it hands back is the
/// reason the player is shown.
/// </para>
/// </summary>
internal static class DeliveryApi
{
    /// <summary>
    /// The player's delivery app.
    /// <para>
    /// <c>DeliveryApp : App&lt;DeliveryApp&gt; : PlayerSingleton&lt;DeliveryApp&gt;</c>, so the
    /// singleton path is the right one. The scene sweep behind it covers the window before
    /// <c>Awake</c> has run and the case where a future build stops using the singleton base.
    /// </para>
    /// </summary>
    internal static object? App()
    {
        if (GameReflection.TryGetSingleton(AutoReorderTypes.DeliveryApp, out var instance, out _) && instance is not null)
            return instance;

        var type = GameReflection.FindType(AutoReorderTypes.DeliveryApp);
        if (type is null)
            return null;

        foreach (var (unity, typed) in Members.FindAll(type))
        {
            if (GameReflection.IsPresent(unity))
                return typed;
        }

        return null;
    }

    internal static object? Manager() =>
        GameReflection.TryGetSingleton(AutoReorderTypes.DeliveryManager, out var manager, out _) ? manager : null;

    /// <summary>True while the player is looking at the app. Nothing is ordered in that window.</summary>
    internal static bool IsAppOpen(object? app) => Members.Read(app, "isOpen", false);

    /// <summary>Every delivery the manager is tracking, in-transit through arrived.</summary>
    internal static IReadOnlyList<object?> LiveDeliveries(object? manager) =>
        GameReflection.Enumerate(Members.ReadObject(manager, "Deliveries"), cap: 256);

    /// <summary>
    /// The receipt history the past-orders tab renders. A reorder <em>moves</em> the original receipt
    /// to the end of this list rather than adding a duplicate, so the same object keeps serving a
    /// standing order across repeats.
    /// </summary>
    internal static IReadOnlyList<object?> History(object? manager) =>
        GameReflection.Enumerate(Members.ReadObject(manager, "DisplayedDeliveryHistory"), cap: 256);

    internal static IReadOnlyList<object?> Shops(object? app) =>
        GameReflection.Enumerate(Members.ReadObject(app, "deliveryShops"), cap: 64);

    internal static IReadOnlyList<object?> StatusDisplays(object? app) =>
        GameReflection.Enumerate(Members.ReadObject(app, "statusDisplays"), cap: 64);

    internal static IReadOnlyList<object?> PastDisplays(object? app) =>
        GameReflection.Enumerate(Members.ReadObject(app, "_pastDeliveries"), cap: 64);

    /// <summary>The shop element for a store name, or null when that store is not in the app.</summary>
    internal static object? Shop(object? app, string storeName)
    {
        if (app is null || storeName.Length == 0)
            return null;

        if (GameReflection.TryInvoke(app.GetType(), app, "GetShop", new object?[] { storeName }, out var shop, out _) &&
            shop is not null)
        {
            return shop;
        }

        // GetShop matches on the shop's own name; a store renamed between builds still resolves here
        // through the matching ShopInterface, which is what the receipt's StoreName came from.
        foreach (var candidate in Shops(app))
        {
            var matching = Members.ReadObject(candidate, "MatchingShop");
            var name = Members.Read(matching, "ShopName", string.Empty);

            if (name.Length > 0 && string.Equals(name, storeName, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        return null;
    }

    /// <summary>The game's own one-order-per-store rule. True means a repeat has to wait.</summary>
    internal static bool HasActiveDelivery(object? shop) =>
        shop is not null &&
        GameReflection.TryInvoke(shop.GetType(), shop, "HasActiveDelivery", Array.Empty<object?>(), out var busy, out _) &&
        busy is true;

    /// <summary>
    /// The game's whole answer to "can this be ordered again right now", with its own wording for a
    /// refusal. Missing on an unexpected build, which is reported rather than assumed to mean yes.
    /// </summary>
    internal static bool CanReorder(object? app, object? receipt, out string reason)
    {
        reason = string.Empty;

        if (app is null || receipt is null)
        {
            reason = "the deliveries app is not up yet";
            return false;
        }

        return Guard(app, "CanReorder", receipt, out reason);
    }

    internal static bool IsValidReceipt(object? app, object? receipt) =>
        app is not null && receipt is not null &&
        GameReflection.TryInvoke(app.GetType(), app, "IsValidReceipt", new[] { receipt }, out var valid, out _) &&
        valid is true;

    /// <summary>Total the order will cost, items plus the delivery fee, as the app quotes it.</summary>
    internal static float DeliveryCost(object? app, object? receipt)
    {
        if (app is null || receipt is null)
            return 0f;

        if (GameReflection.TryInvoke(app.GetType(), app, "GetDeliveryCost", new[] { receipt }, out var cost, out _) &&
            cost is float amount)
        {
            return amount;
        }

        return 0f;
    }

    /// <summary>Places the order exactly as the Reorder button does. False means the call did not run.</summary>
    internal static bool Reorder(object? app, object? receipt, out string failure)
    {
        failure = string.Empty;

        if (app is null || receipt is null)
        {
            failure = "the deliveries app is not up yet";
            return false;
        }

        return GameReflection.TryInvoke(app.GetType(), app, "Reorder", new[] { receipt }, out _, out failure);
    }

    internal static int CartCount(object? shop) =>
        shop is not null &&
        GameReflection.TryInvoke(shop.GetType(), shop, "GetOrderItemCount", Array.Empty<object?>(), out var count, out _) &&
        count is int items
            ? items
            : 0;

    internal static void ResetCart(object? shop)
    {
        if (shop is null)
            return;

        GameReflection.TryInvoke(shop.GetType(), shop, "ResetCart", Array.Empty<object?>(), out _, out _);
    }

    /// <summary>
    /// Which balance a manual order from this shop would draw on.
    /// <para>
    /// <c>ShopInterface.EPaymentType</c> is <c>Cash</c>/<c>Online</c>/<c>PreferCash</c>/
    /// <c>PreferOnline</c>. The floor has to be checked against whichever one the game will actually
    /// charge, or a "keep $1,000" rule would be measured against money the order never touches.
    /// </para>
    /// </summary>
    internal static PaymentSource PaymentFor(object? shop, float cost)
    {
        var cash = CashBalance();
        var online = OnlineBalance();

        var matching = Members.ReadObject(shop, "MatchingShop");
        var payment = Members.Read(matching, "PaymentType", -1);

        return payment switch
        {
            0 => new PaymentSource("cash", cash),
            1 => new PaymentSource("online balance", online),
            2 => cash >= cost ? new PaymentSource("cash", cash) : new PaymentSource("online balance", online),
            3 => online >= cost ? new PaymentSource("online balance", online) : new PaymentSource("cash", cash),

            // Unreadable payment type: assume the tighter of the two, so an unknown build errs
            // towards not spending rather than towards spending.
            _ => cash <= online ? new PaymentSource("cash", cash) : new PaymentSource("online balance", online),
        };
    }

    internal static float CashBalance() =>
        GameReflection.TryGetSingleton(AutoReorderTypes.MoneyManager, out var money, out _)
            ? Members.Read(money, "cashBalance", 0f)
            : 0f;

    internal static float OnlineBalance() =>
        GameReflection.TryGetSingleton(AutoReorderTypes.MoneyManager, out var money, out _)
            ? Members.Read(money, "onlineBalance", 0f)
            : 0f;

    /// <summary>The game's own toast. Silent failure is fine — it is an announcement, not a step.</summary>
    internal static void Notify(string title, string subtitle)
    {
        if (!GameReflection.TryGetSingleton(AutoReorderTypes.NotificationsManager, out var manager, out _) ||
            manager is null)
        {
            return;
        }

        var method = GameReflection.FindMethod(manager.GetType(), "SendNotification", 5);
        if (method is null)
            return;

        try
        {
            method.Invoke(manager, new object?[] { title, subtitle, null, 5f, true });
        }
        catch (Exception ex)
        {
            TweakLog.Detail($"Could not show the auto-reorder notification: {TweakLog.Describe(ex)}");
        }
    }

    /// <summary>
    /// Calls a <c>bool Method(arg, out string reason)</c> and hands back both halves. Reflection
    /// writes a by-ref parameter into the argument array, which is the only way to read the game's
    /// refusal text without a compile-time reference to its types.
    /// </summary>
    private static bool Guard(object target, string methodName, object? argument, out string reason)
    {
        var type = target.GetType();
        var method = GameReflection.FindMethod(type, methodName, 2);

        if (method is null)
        {
            reason = $"'{type.Name}.{methodName}' is not on this build";
            return false;
        }

        var arguments = new[] { argument, null };

        try
        {
            var allowed = method.Invoke(method.IsStatic ? null : target, arguments) is true;
            var given = arguments[1] as string;
            reason = allowed ? string.Empty : Sentence(given, "the game refused the repeat without saying why");
            return allowed;
        }
        catch (Exception ex)
        {
            reason = $"'{type.Name}.{methodName}' threw ({TweakLog.Describe(ex)})";
            return false;
        }
    }

    private static string Sentence(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value!.Trim();
}

/// <summary>Which pot an order will be paid from, and how much is in it.</summary>
internal readonly struct PaymentSource
{
    internal PaymentSource(string name, float balance)
    {
        Name = name;
        Balance = balance;
    }

    /// <summary>"cash" or "online balance", for the waiting text.</summary>
    internal string Name { get; }

    internal float Balance { get; }
}

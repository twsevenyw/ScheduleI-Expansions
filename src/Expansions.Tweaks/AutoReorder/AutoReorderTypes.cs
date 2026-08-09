namespace Expansions.Tweaks.AutoReorder;

/// <summary>
/// Every game type the auto-reorder feature touches, by name only.
/// <para>
/// Same rule as <see cref="Runtime.GameTypes"/>, restated here rather than added to it so the two
/// features never contend for the same file. Nothing in this folder holds a compile-time reference
/// to <c>Assembly-CSharp</c> or FishNet: a type renamed by a game patch has to degrade to a logged
/// warning and a probe that says NOT_FOUND, never a <c>TypeLoadException</c> that stops the melon
/// loading.
/// </para>
/// </summary>
internal static class AutoReorderTypes
{
    internal const string DeliveryManager = "Il2CppScheduleOne.Delivery.DeliveryManager";

    /// <summary>The phone app. <c>App&lt;DeliveryApp&gt;</c> → <c>PlayerSingleton&lt;DeliveryApp&gt;</c>.</summary>
    internal const string DeliveryApp = "Il2CppScheduleOne.UI.Phone.Delivery.DeliveryApp";

    /// <summary>One per store inside the app; owns the cart, the destination and the order button.</summary>
    internal const string DeliveryShop = "Il2CppScheduleOne.UI.Phone.Delivery.DeliveryShop";

    /// <summary>A row in the "active orders" list.</summary>
    internal const string DeliveryStatusDisplay = "Il2CppScheduleOne.UI.Phone.Delivery.DeliveryStatusDisplay";

    /// <summary>A row in the "past orders" list — the one that already carries a Reorder button.</summary>
    internal const string DeliveryReceiptDisplay = "Il2CppScheduleOne.UI.Phone.Delivery.DeliveryReceiptDisplay";

    /// <summary>
    /// Holds the phone's own "Listed for sale" / "Favourite" tick boxes, which is where the
    /// checkmark graphic is cloned from. Nothing is shipped as art.
    /// </summary>
    internal const string ProductAppDetailPanel =
        "Il2CppScheduleOne.UI.Phone.ProductManagerApp.ProductAppDetailPanel";

    internal const string MoneyManager = "Il2CppScheduleOne.Money.MoneyManager";

    /// <summary>Read only for <c>PlayersSavePath</c>, which is what stamps a blob to its save slot.</summary>
    internal const string SaveManager = "Il2CppScheduleOne.Persistence.SaveManager";

    internal const string NotificationsManager = "Il2CppScheduleOne.UI.NotificationsManager";

    internal const string InstanceFinder = "Il2CppFishNet.InstanceFinder";
}

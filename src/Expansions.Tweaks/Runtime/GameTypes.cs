namespace Expansions.Tweaks.Runtime;

/// <summary>
/// Every game type this module touches, by name only.
/// <para>
/// Nothing in this assembly holds a compile-time reference to <c>Assembly-CSharp</c>. A type renamed
/// by a game patch has to degrade to a logged warning and a probe that says NOT_FOUND, never a
/// <c>TypeLoadException</c> at mod load — which would take the whole melon down before
/// <see cref="Expansions.Core.ExpansionRegistry"/> ever saw it.
/// </para>
/// </summary>
internal static class GameTypes
{
    internal const string MixingStation = "Il2CppScheduleOne.ObjectScripts.MixingStation";

    /// <summary>The Bikers-update station. Derives from <see cref="MixingStation"/> and overrides Awake.</summary>
    internal const string MixingStationMk2 = "Il2CppScheduleOne.ObjectScripts.MixingStationMk2";

    internal const string Atm = "Il2CppScheduleOne.Money.ATM";

    internal const string AtmInterface = "Il2CppScheduleOne.UI.ATMInterface";

    internal const string MoneyManager = "Il2CppScheduleOne.Money.MoneyManager";

    internal const string DeliveryManager = "Il2CppScheduleOne.Delivery.DeliveryManager";

    /// <summary>
    /// Owns <c>GetDeliveryTime(itemCount)</c>, the single function deciding how long an order takes:
    /// the order screen prints its result and <c>SubmitOrder</c> bakes it into the new delivery.
    /// Scaling that one return value is the whole of the delivery-speed feature.
    /// <para>
    /// The <c>DeliverySettings</c> asset behind it is deliberately not named here. Writing to it is
    /// what produced the 6731407h regression; see <see cref="DeliverySpeed"/>.
    /// </para>
    /// </summary>
    internal const string DeliveryShop = "Il2CppScheduleOne.UI.Phone.Delivery.DeliveryShop";

    /// <summary>One row of the phone's deliveries app. Read only, to report what the player is shown.</summary>
    internal const string DeliveryStatusDisplay = "Il2CppScheduleOne.UI.Phone.Delivery.DeliveryStatusDisplay";
}

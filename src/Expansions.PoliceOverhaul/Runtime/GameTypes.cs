namespace Expansions.PoliceOverhaul.Runtime;

/// <summary>
/// Every game type this module touches, in one list, as strings.
/// <para>
/// Nothing in this assembly holds a compile-time reference to <c>Assembly-CSharp</c> or FishNet. That
/// is not fastidiousness: a mod DLL that fails to load takes its <c>MelonMod</c> with it, and S1API's
/// prefab scan swallows <c>Assembly.GetTypes()</c> failures — so one renamed game type in a signature
/// here could silently delete another mod's NPCs. Late binding turns the same rename into one logged
/// warning and one dead lever.
/// </para>
/// </summary>
internal static class GameTypes
{
    internal const string LawController = "Il2CppScheduleOne.Law.LawController";
    internal const string LawManager = "Il2CppScheduleOne.Law.LawManager";
    internal const string PenaltyHandler = "Il2CppScheduleOne.Law.PenaltyHandler";
    internal const string CurfewManager = "Il2CppScheduleOne.Law.CurfewManager";

    internal const string PoliceOfficer = "Il2CppScheduleOne.Police.PoliceOfficer";
    internal const string PoliceStation = "Il2CppScheduleOne.Map.PoliceStation";
    internal const string EDispatchType = "Il2CppScheduleOne.Map.PoliceStation+EDispatchType";
    internal const string NpcHealth = "Il2CppScheduleOne.NPCs.NPCHealth";
    internal const string NpcManager = "Il2CppScheduleOne.NPCs.NPCManager";

    internal const string Player = "Il2CppScheduleOne.PlayerScripts.Player";
    internal const string PlayerCrimeData = "Il2CppScheduleOne.PlayerScripts.PlayerCrimeData";
    internal const string PlayerInventory = "Il2CppScheduleOne.PlayerScripts.PlayerInventory";

    internal const string BodySearchBehaviour = "Il2CppScheduleOne.NPCs.Behaviour.BodySearchBehaviour";
    internal const string CheckpointBehaviour = "Il2CppScheduleOne.NPCs.Behaviour.CheckpointBehaviour";
    internal const string CallPoliceBehaviour = "Il2CppScheduleOne.NPCs.Behaviour.CallPoliceBehaviour";

    internal const string Property = "Il2CppScheduleOne.Property.Property";
    internal const string StorageEntity = "Il2CppScheduleOne.Storage.StorageEntity";
    internal const string TimeManager = "Il2CppScheduleOne.GameTime.TimeManager";

    internal const string Dealer = "Il2CppScheduleOne.Economy.Dealer";
    internal const string DealerNPCData = "Il2CppScheduleOne.NPCs.Framework.DealerNPCData";
    internal const string Customer = "Il2CppScheduleOne.Economy.Customer";
    internal const string ShopInterface = "Il2CppScheduleOne.UI.Shop.ShopInterface";

    internal const string VisionCone = "Il2CppScheduleOne.Vision.VisionCone";
    internal const string EVisualState = "Il2CppScheduleOne.Vision.EVisualState";

    internal const string ArrestNoticeScreen = "Il2CppScheduleOne.UI.ArrestNoticeScreen";
    internal const string NotificationsManager = "Il2CppScheduleOne.UI.NotificationsManager";

    internal const string EStealthLevel = "Il2CppScheduleOne.Product.Packaging.EStealthLevel";
    internal const string Contract = "Il2CppScheduleOne.Quests.Contract";

    internal const string AvatarSettings = "Il2CppScheduleOne.AvatarFramework.AvatarSettings";
    internal const string AccessorySetting = "Il2CppScheduleOne.AvatarFramework.AvatarSettings+AccessorySetting";

    internal const string NetworkObject = "Il2CppFishNet.Object.NetworkObject";
    internal const string InstanceFinder = "Il2CppFishNet.InstanceFinder";
}

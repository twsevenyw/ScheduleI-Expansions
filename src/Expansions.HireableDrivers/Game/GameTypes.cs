namespace Expansions.HireableDrivers.Game;

/// <summary>
/// Every game type this mod binds, in one place.
/// <para>
/// These strings are the mod's entire contract with <c>Assembly-CSharp</c>. They were transcribed
/// from the IL2CPP metadata dumps under <c>research/raw/ns/</c> and are re-checked against
/// <c>&lt;GameDir&gt;\MelonLoader\Il2CppAssemblies\</c> by the verification harness, so a game patch
/// that renames one shows up as a build-time report rather than a runtime surprise.
/// </para>
/// </summary>
internal static class GameTypes
{
    internal const string EmployeeManager = "Il2CppScheduleOne.Employees.EmployeeManager";
    internal const string Employee = "Il2CppScheduleOne.Employees.Employee";
    internal const string Packager = "Il2CppScheduleOne.Employees.Packager";
    internal const string EmployeeHome = "Il2CppScheduleOne.Employees.EmployeeHome";
    internal const string EEmployeeType = "Il2CppScheduleOne.Employees.EEmployeeType";

    internal const string Property = "Il2CppScheduleOne.Property.Property";
    internal const string Business = "Il2CppScheduleOne.Property.Business";
    internal const string LoadingDock = "Il2CppScheduleOne.Delivery.LoadingDock";

    internal const string PackagerConfiguration = "Il2CppScheduleOne.Management.PackagerConfiguration";
    internal const string RouteListField = "Il2CppScheduleOne.Management.RouteListField";
    internal const string AdvancedTransitRoute = "Il2CppScheduleOne.Management.AdvancedTransitRoute";
    internal const string ManagementItemFilter = "Il2CppScheduleOne.Management.ManagementItemFilter";
    internal const string ItemFilterMode = "Il2CppScheduleOne.Management.ManagementItemFilter+EMode";
    internal const string RouteEntryUi = "Il2CppScheduleOne.UI.Management.RouteEntryUI";
    internal const string ManagementInterface = "Il2CppScheduleOne.Management.ManagementInterface";

    internal const string DialogueControllerFixer = "Il2CppScheduleOne.Dialogue.DialogueController_Fixer";
    internal const string DialogueChoice = "Il2CppScheduleOne.Dialogue.DialogueController+DialogueChoice";
    internal const string ShouldShowCheck = "Il2CppScheduleOne.Dialogue.DialogueController+DialogueChoice+ShouldShowCheck";

    internal const string VehicleManager = "Il2CppScheduleOne.Vehicles.VehicleManager";
    internal const string LandVehicle = "Il2CppScheduleOne.Vehicles.LandVehicle";
    internal const string VehicleAgent = "Il2CppScheduleOne.Vehicles.AI.VehicleAgent";
    internal const string NavigationSettings = "Il2CppScheduleOne.Vehicles.AI.NavigationSettings";
    internal const string NavigationUtility = "Il2CppScheduleOne.Vehicles.AI.NavigationUtility";
    internal const string ParkData = "Il2CppScheduleOne.Vehicles.ParkData";
    internal const string ParkingLot = "Il2CppScheduleOne.Map.ParkingLot";
    internal const string ParkingSpot = "Il2CppScheduleOne.Map.ParkingSpot";

    internal const string TransitEntity = "Il2CppScheduleOne.Management.ITransitEntity";
    internal const string SlotType = "Il2CppScheduleOne.Management.ITransitEntity+ESlotType";
    internal const string StorageEntity = "Il2CppScheduleOne.Storage.StorageEntity";
    internal const string ItemSlot = "Il2CppScheduleOne.ItemFramework.ItemSlot";
    internal const string ItemInstance = "Il2CppScheduleOne.ItemFramework.ItemInstance";
    internal const string BuildableItem = "Il2CppScheduleOne.EntityFramework.BuildableItem";

    internal const string Dealer = "Il2CppScheduleOne.Economy.Dealer";
    internal const string Npc = "Il2CppScheduleOne.NPCs.NPC";
    internal const string NpcMovement = "Il2CppScheduleOne.NPCs.NPCMovement";
    internal const string NavMeshUtility = "Il2CppScheduleOne.DevUtilities.NavMeshUtility";

    internal const string TimeManager = "Il2CppScheduleOne.GameTime.TimeManager";
    internal const string CurfewManager = "Il2CppScheduleOne.Law.CurfewManager";
    internal const string MoneyManager = "Il2CppScheduleOne.Money.MoneyManager";
}

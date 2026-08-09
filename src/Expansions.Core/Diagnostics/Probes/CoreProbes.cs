namespace Expansions.Core.Diagnostics.Probes;

/// <summary>
/// The probes Core ships. They are cross-cutting — every one of them answers a question that at
/// least two of the three feature plans depend on — so they live here rather than in a feature mod,
/// and they work with none of the feature mods installed.
/// </summary>
public static class CoreProbes
{
    private static bool _registered;

    /// <summary>Idempotent. Called from <see cref="ExpansionHost.EnsureInitialized"/>.</summary>
    public static void RegisterAll()
    {
        if (_registered)
            return;

        _registered = true;

        var probes = new List<IProbe>();

        VehicleProbes.Register(probes);
        PoliceProbes.Register(probes);
        NpcProbes.Register(probes);
        VersionProbes.Register(probes);
        IdentityProbes.Register(probes);
        ConstantProbes.Register(probes);
        EnvironmentProbes.Register(probes);

        foreach (var probe in probes)
            ProbeRegistry.Register(probe);

        foreach (var note in MutatingTests)
            MutatingTestCatalogue.Register(note);
    }

    /// <summary>
    /// Unknowns the plans list that no read-only probe can settle. They are printed with their
    /// recipes at the bottom of every report so they are visibly outstanding rather than forgotten.
    /// </summary>
    private static readonly MutatingTestNote[] MutatingTests =
    {
        new("HD-P2", "Hireable Drivers",
            "Is `NPC.EnterVehicle(null, veh)` a legal null-connection call?",
            "On a listen server, take a hired Handler and call `EnterVehicle(null, veh)`. Assert `emp.IsInVehicle`, `emp.CurrentVehicle == veh` and that `veh.OccupantNPCs.Length` went up. If null fails, retry with `InstanceFinder.ClientManager.Connection`."),

        new("HD-P3", "Hireable Drivers",
            "Does `VehicleAgent.Navigate` actually drive a civilian car with no player nearby?",
            "Pick a parked player-owned car, `ExitPark`, set `DriveFlags`, `Navigate(lot.EntryPoint.position, …)`. Log at 1 Hz: `Agent.AutoDriving`, `Agent.KinematicMode`, `veh.Speed_Kmh`, distance to `Agent.TargetLocation`, `Agent.GetIsStuck()`, and the callback's `ENavigationResult`."),

        new("HD-P4", "Hireable Drivers",
            "Does `NavMeshUtility.GetReachableAccessPoint` return non-null across properties?",
            "For each pair of owned properties, take an `ITransitEntity` from each and log `GetReachableAccessPoint(e, driverNpc) != null` plus `driverNpc.Movement.CanGetTo(e, 1f)`. Needs a live hired employee, so it changes payroll state."),

        new("HD-P7", "Hireable Drivers",
            "Does an NPC-driven car trigger `CheckpointBehaviour` or a police stop?",
            "Drive a loaded van through an active `RoadCheckpoint` and watch for `CheckpointBehaviour` activating and for a `Crime` on `PlayerCrimeData`."),

        new("HD-P8", "Hireable Drivers",
            "Does `Dealer.CollectCash()` credit the local player directly?",
            "Read `dealer.Cash` and the `MoneyManager` cash/online balances before and after `CanCollectCash` + `CollectCash` on the host. Keep the cash-collection leg disabled until this is answered."),

        new("HD-P12", "Hireable Drivers",
            "Does a cross-property `AdvancedTransitRoute` survive a save/load round trip?",
            "Build a cross-property route into `packager.configuration.Routes`, `RequestGameSave(immediate: true)`, reload, re-read `Routes.Routes`. If it blanks, mirror routes into the mod's own sidecar and restore on load."),

        new("PI-R1b", "Police Improvements",
            "Does the game *read* the tuning statics it lets you write?",
            "`police.tuning_writability` says which statics are real fields rather than inlined consts, read-only. It cannot say whether the game consults them. Set `VisionCone.UniversalAttentivenessScale = 50` and stand in plain sight of an officer across the street; lower `LawManager.DISPATCH_VEHICLE_USE_THRESHOLD` and watch for cruisers on a pursuit."),

        new("PI-R2", "Police Improvements",
            "Does the game deduct the fine, and at which boundary?",
            "`changecash 500`, break curfew, get arrested. Log `MoneyManager.Instance.cashBalance` from a prefix and postfix on `ArrestNoticeScreen.OnClose()` and `Exit()`, and from `Player.onFreed`. Whichever boundary shows the drop is the game's charge point — subtract it from the mod's own."),

        new("PI-R4b", "Police Improvements",
            "Does `ServerManager.Spawn` on a cloned officer replicate to a second client?",
            "`net.spawnable_prefabs` answers whether the prefab is registered. Spawning one and checking it appears for a second client, and does not survive a save/reload, requires two clients and a throwaway save."),

        new("PI-R8", "Police Improvements",
            "Is `TimeManager.StartSleep()` callable without a bed?",
            "Call it from a debug key while standing in the street and watch for a sleep transition or an exception. Fallback is `SkipForwardToTime(TimeManager.WakeTime)`, which is a plain time write."),

        new("PI-R9", "Police Improvements",
            "Are the `LawActivitySettings` instance objects stable across days?",
            "Log `GetHashCode()` for a handful of `PatrolInstance` objects on day 1 and again on day 3. If they are rebuilt, key the snapshot on (day, arrayIndex) instead of the object reference and re-apply on `onDayPass`."),

        new("SC-V2", "Special Customers",
            "Does `NPCAppearance.Build()` replace or append the layer lists?",
            "Call `WithBodyLayer(...).Build()` three times on one NPC and log `AvatarSettings.BodyLayerSettings.Count`. 1 means replace, 3 means append and repeated re-skins would overflow the 6/8/9 slot budget."),

        new("SC-V3", "Special Customers",
            "Does `Avatar.LoadAvatarSettings` fully re-skin a live NPC, and does it replicate?",
            "Clone a shipped NPC's `AvatarSettings`, swap the body layers for an archetype recipe, call `LoadAvatarSettings` on a live NPC and look at it. Repeat with a second client attached."),

        new("SC-V4", "Special Customers",
            "Does a locked `Customer` with `MinOrdersPerWeek = 0` ever generate a deal?",
            "Park a pool member locked with zeroed data, run three in-game days at `settimescale 10`, and watch `Customer.OfferedDeals`, the phone, and `ShouldTryGenerateDeal()` hourly."),

        new("SC-V5", "Special Customers",
            "Is `OverrideCustomerDealLocation` honoured by `Customer.GetDeliveryLocation()`?",
            "Attach it to a shipped customer pointing at a known `DeliveryLocation`, force an offer, accept, and log `GetDeliveryLocation().LocationName` plus where the NPC actually walks."),

        new("SC-V6", "Special Customers",
            "Does vanilla `NPCsLoader` tolerate orphaned NPC save folders after the DLL is removed?",
            "Back up a save. Install, play, save so the visitor folders are written. Remove the DLL and load. This decides whether \"uninstall\" is supported or whether the mod ships a retire command plus a \"disable, don't delete\" warning."),

        new("SC-V7", "Special Customers",
            "Do S1API NPC subclasses register in the same FishNet spawnable order on every peer?",
            "Two clients. Log the ordered list of (prefabName, index) S1API produces on host and client and diff it. If it diverges, emit the types sorted by ordinal id instead."),

        new("SC-V10", "Special Customers",
            "Is a hand-built `ContractInfo` re-clamped downstream?",
            "Offer a 200-unit contract and log `Contract.ProductList.GetTotalQuantity()` after acceptance. If clamped, split across multiple entries or raise the static for the duration of construction and restore in a `finally`."),

        new("SC-V11", "Special Customers",
            "Does `NPCCustomer.OfferContract(ContractInfo)` reach the server path?",
            "Log the return value and whether the phone message arrives, on host and on a client. Guard the call with the authority check regardless."),

        new("XM-R3b", "Cross-mod",
            "Is `Player.PlayerCode` stable for the same human across sessions?",
            "`identity.player_code` records it. Run the suite twice against the same save in two separate game sessions and diff the PlayerCode line, then repeat with a co-op client joining in the opposite order."),
    };
}

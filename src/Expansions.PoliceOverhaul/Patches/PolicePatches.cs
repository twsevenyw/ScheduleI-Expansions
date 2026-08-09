using System.Reflection;
using Expansions.Core.Diagnostics;
using Expansions.PoliceOverhaul.Runtime;
using Expansions.PoliceOverhaul.State;
using HarmonyLib;

namespace Expansions.PoliceOverhaul.Patches;

/// <summary>
/// Every Harmony patch the module owns, applied explicitly through the per-module instance.
/// <para>
/// Attribute auto-patching is switched off assembly-wide, deliberately: MelonLoader runs
/// <c>PatchAll</c> under the melon's own Harmony id, so an <c>[HarmonyPatch]</c> class would survive
/// <c>UnpatchSelf()</c> and quietly break the reversible-toggle guarantee the whole suite is built
/// on. Patches exist only because <see cref="Apply"/> put them there.
/// </para>
/// <para>
/// No patch here signature-references a game type. Arguments arrive boxed through Harmony's
/// <c>__args</c> and instances as <c>object</c>, so a renamed game type costs one logged patch rather
/// than a <c>TypeLoadException</c> at mod load. Prefixes are used only where the original genuinely
/// has to be cancelled, and none of them contends with an S1API prefix.
/// </para>
/// </summary>
internal static class PolicePatches
{
    private static readonly List<string> Applied = new();
    private static readonly List<string> Missing = new();

    internal static IReadOnlyList<string> AppliedTargets => Applied;

    internal static IReadOnlyList<string> MissingTargets => Missing;

    internal static void Apply(HarmonyLib.Harmony harmony)
    {
        Applied.Clear();
        Missing.Clear();

        Patch(harmony, GameTypes.LawController, "OnUncappedMinPass", 0, postfix: nameof(LawMinutePassPostfix));
        Patch(harmony, GameTypes.PlayerCrimeData, "AddCrime", 2, postfix: nameof(AddCrimePostfix));
        Patch(harmony, GameTypes.PlayerCrimeData, "OnPlayerFreed", 0, postfix: nameof(PlayerFreedPostfix));
        Patch(harmony, GameTypes.PlayerCrimeData, "TimeoutPursuit", 0, prefix: nameof(TimeoutPursuitPrefix));
        Patch(harmony, GameTypes.PlayerCrimeData, "Deescalate", 0, prefix: nameof(DeescalatePrefix));
        // The hand-written arrest body is the FishNet-generated RpcLogic method; Arrest_Client itself
        // is only the writer wrapper the server calls.
        Patch(harmony, GameTypes.Player, "RpcLogic___Arrest_Client_*", 0, postfix: nameof(ArrestedPostfix));

        Patch(harmony, GameTypes.PoliceStation, "Dispatch", 4, prefix: nameof(DispatchPrefix));
        Patch(harmony, GameTypes.PoliceOfficer, "CheckDeactivation", 0, prefix: nameof(CheckDeactivationPrefix));
        Patch(harmony, GameTypes.PoliceOfficer, "ShouldSave", 0, prefix: nameof(ShouldSavePrefix));
        Patch(harmony, GameTypes.PoliceOfficer, "CanInvestigatePlayer", 1, postfix: nameof(CanInvestigatePostfix));

        Patch(harmony, GameTypes.BodySearchBehaviour, "DoesPlayerContainItemsOfInterest", 0, postfix: nameof(BodySearchPostfix));
        Patch(harmony, GameTypes.CheckpointBehaviour, "DoesVehicleContainIllicitItems", 0, postfix: nameof(VehicleSearchPostfix));
        // The hand-written body of the observers RPC, so the caller is recorded on the host as well
        // as on every client rather than only where the writer happened to run.
        Patch(harmony, GameTypes.CallPoliceBehaviour, "RpcLogic___FinalizeCall_*", 0, postfix: nameof(PoliceCalledPostfix));

        Patch(harmony, GameTypes.ShopInterface, "Open", 0, prefix: nameof(ShopOpenPrefix));

        Patch(harmony, GameTypes.ArrestNoticeScreen, "Open", 0, postfix: nameof(NoticeOpenPostfix));
        Patch(harmony, GameTypes.ArrestNoticeScreen, "OnClose", 0, postfix: nameof(NoticeClosePostfix));
        Patch(harmony, GameTypes.ArrestNoticeScreen, "RecordPossession", 1, prefix: nameof(WidenStealthPrefix));
        Patch(harmony, GameTypes.ArrestNoticeScreen, "ConfiscateItems", 1, prefix: nameof(WidenStealthPrefix), postfix: nameof(ConfiscatePostfix));
        Patch(harmony, GameTypes.PenaltyHandler, "ProcessCrimeList", 1, postfix: nameof(PenaltyListPostfix));

        Patch(harmony, GameTypes.Contract, "SubmitPayment", 1, postfix: nameof(ContractPaidPostfix));

        PoliceLog.Msg($"Patched {Applied.Count} law-enforcement target(s)." +
                      (Missing.Count > 0 ? $" {Missing.Count} not present on this build: {string.Join(", ", Missing)}." : string.Empty));
    }

    // ── Heat inputs ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The one per-minute hook the whole module runs on. Everything time-driven hangs off it in a
    /// fixed order — heat first, because the raid and stakeout checks read the outlaw status it has
    /// just refreshed.
    /// </summary>
    private static void LawMinutePassPostfix(object __instance)
    {
        if (!PoliceRuntime.IsLive || !HostGate.IsAuthority)
            return;

        Guard("minute tick", () => PoliceRuntime.Heat!.MinutePass(__instance));
        Guard("raid countdown", () => PoliceRuntime.Raids?.MinutePass());
        Guard("stakeout upkeep", () => PoliceRuntime.Federal?.MinutePass());
    }

    private static void AddCrimePostfix(object __instance, object[] __args)
    {
        if (!PoliceRuntime.IsLive || !HostGate.IsAuthority || __args.Length < 2)
            return;

        Guard("crime recorded", () =>
        {
            var crime = GameBridge.NativeClassName(__args[0]);
            var quantity = __args[1] is int number ? number : 1;
            var player = Members.ReadPath(__instance, "Player");

            PoliceRuntime.Heat!.AddCrimeHeat(player, crime, quantity);
            PoliceRuntime.Consequences!.Track(player, crime, quantity);
        });
    }

    private static void ArrestedPostfix(object __instance)
    {
        if (!PoliceRuntime.IsLive || !HostGate.IsAuthority)
            return;

        Guard("arrest", () => PoliceRuntime.Heat!.AddArrestHeat(__instance));
    }

    /// <summary>
    /// Volume attracts attention. The argument is only the quality bonus, so the deal's actual worth
    /// comes off the contract itself and the bonus is added on top.
    /// </summary>
    private static void ContractPaidPostfix(object __instance, object[] __args)
    {
        if (!PoliceRuntime.IsLive || !HostGate.IsAuthority)
            return;

        Guard("contract payment", () =>
        {
            var bonus = __args.Length > 0 && __args[0] is float extra ? extra : 0f;
            PoliceRuntime.Heat!.AddDealHeat(Members.Read(__instance, "Payment", 0f) + bonus);
        });
    }

    /// <summary>
    /// Deliberately a postfix rather than a skipping prefix: the original also clears the pursuit
    /// level and resets the since-arrested counter, both of which we want to keep.
    /// <para>
    /// This is where custody lands, because it is the first moment the game agrees the arrest is
    /// finished — running the clock skip any earlier fights the arrest screen's own transition.
    /// </para>
    /// </summary>
    private static void PlayerFreedPostfix(object __instance)
    {
        if (!PoliceRuntime.IsLive)
            return;

        Guard("release", () =>
        {
            var player = Members.ReadPath(__instance, "Player");

            // Custody moves the world clock, so only the authoritative peer runs it, and only for the
            // player sitting at this machine. A client watching a co-op partner get booked must not
            // skip everyone's day.
            var isLocal = player is not null && GameBridge.IsLocal(player);
            if (HostGate.IsAuthority && isLocal)
                PoliceRuntime.Consequences!.Release(player);
            else
                PoliceRuntime.Consequences!.ClearCharges(player);
        });
    }

    /// <summary>Records who dialled, so the arrest can cost the player that relationship.</summary>
    private static void PoliceCalledPostfix(object __instance)
    {
        if (!PoliceRuntime.IsLive || !HostGate.IsAuthority)
            return;

        Guard("a police call", () => PoliceRuntime.Consequences!.Informants.Record(__instance));
    }

    /// <summary>
    /// Shuts card-only vendors to an outlaw.
    /// <para>
    /// A skipping prefix, which is the one place in this module where cancelling the original is the
    /// right call: there is no "refuse" return value to postfix, and letting the shop open and then
    /// closing it would flash the whole interface. The refusal always says which shop and why, so it
    /// can never be mistaken for the menu failing to open.
    /// </para>
    /// </summary>
    private static bool ShopOpenPrefix(object __instance)
    {
        if (!PoliceRuntime.IsLive || PoliceRuntime.Outlaw is not { } outlaw)
            return true;

        try
        {
            if (!outlaw.Economy.ShouldRefuseShop(__instance))
                return true;

            var reason = outlaw.Economy.RefusalFor(__instance);
            // Toast: this is click-time UI feedback; a phone text would land after they already walked off.
            PoliceMessages.Toast("They will not serve you", reason, 8f);
            PoliceLog.Msg($"Refused a card-only shop to an outlawed player: {reason}");
            return false;
        }
        catch (Exception ex)
        {
            // Never leave a shop unopenable because our check threw.
            PoliceLog.Error("Police Improvements threw while checking a shop; letting it open.", ex);
            return true;
        }
    }

    // ── Outlaw rule changes ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Pursuits do not simply evaporate once you are flagged — but they still end.
    /// <para>
    /// The original is only called when the game has already decided the pursuit has timed out, so
    /// cancelling it outright would mean a chase you can never escape. Instead the deadline is
    /// doubled: the skip only holds until time-since-sighted passes twice the search time, after
    /// which the vanilla path runs and the pursuit drops as normal.
    /// </para>
    /// </summary>
    private static bool TimeoutPursuitPrefix(object __instance)
    {
        if (!PoliceRuntime.IsLive || PoliceRuntime.Outlaw is not { AnyoneOutlawed: true })
            return true;

        if (!PoliceRuntime.Outlaw.IsOutlawed(Members.ReadPath(__instance, "Player")))
            return true;

        var sighted = Members.Read(__instance, "TimeSinceSighted", 0f);
        var searchTime = Members.InvokeFor(__instance, "GetSearchTime") is float time ? time : 0f;

        return searchTime <= 0f || sighted >= searchTime * 2f;
    }

    /// <summary>
    /// Floors the pursuit level at Investigating while outlawed — police stop chasing, but they never
    /// go back to not caring. Only the last step down is blocked, so the shipped de-escalation ladder
    /// still runs.
    /// </summary>
    private static bool DeescalatePrefix(object __instance)
    {
        if (!PoliceRuntime.IsLive || PoliceRuntime.Outlaw is not { AnyoneOutlawed: true })
            return true;

        var level = Members.Read<object?>(__instance, "CurrentPursuitLevel", null);
        return level is null || Convert.ToInt32(level) != 1;
    }

    private static void CanInvestigatePostfix(object[] __args, ref bool __result)
    {
        if (__result || !PoliceRuntime.IsLive || __args.Length < 1)
            return;

        if (PoliceRuntime.Outlaw!.IsOutlawed(__args[0]))
            __result = true;
    }

    private static void BodySearchPostfix(object __instance, ref bool __result)
    {
        if (__result || !PoliceRuntime.IsLive)
            return;

        var target = Members.ReadPath(__instance, "TargetPlayer") ?? GameBridge.LocalPlayer();
        if (PoliceRuntime.Outlaw!.IsOutlawed(target))
            __result = true;
    }

    private static void VehicleSearchPostfix(object __instance, ref bool __result)
    {
        if (__result || !PoliceRuntime.IsLive)
            return;

        var target = Members.ReadPath(__instance, "Initiator") ?? GameBridge.LocalPlayer();
        if (PoliceRuntime.Outlaw!.IsOutlawed(target))
            __result = true;
    }

    // ── Dispatch and federal agents ───────────────────────────────────────────────────────────

    /// <summary>
    /// Raises the requested officer count to the current heat tier's floor.
    /// <para>
    /// Raise-only, deliberately: the dispatcher already asks for what the situation warrants, and
    /// lowering that would make the mod reduce police response at calm heat. This is also the
    /// belt-and-braces half of the officers-per-post work — it holds when the schedule-data path is
    /// switched off, and it is the lever that survives if <c>LawManager</c>'s dispatch statics turn
    /// out to be const-inlined on a future build. Four is the game's own hard cap; asking for more
    /// makes it log an error and refuse outright.
    /// </para>
    /// </summary>
    private static void DispatchPrefix(object[] __args)
    {
        if (!PoliceRuntime.IsLive || !HostGate.IsAuthority || __args.Length < 1 || __args[0] is not int requested)
            return;

        var config = PoliceRuntime.Config!;
        if (!config.EnableIntensity.Value)
            return;

        var (min, _) = HeatModel.OfficerBand(
            PoliceRuntime.Heat!.PeakTier,
            Math.Max(0f, config.IntensityScalar.Value),
            config.MaxOfficersPerPost.Value,
            config.PoliceDensity.Value);

        __args[0] = Math.Min(HeatModel.HardOfficerCap, Math.Max(requested, min));
    }

    /// <summary>Tagged agents belong to us, not to the station pool, so they never auto-retire.</summary>
    private static bool CheckDeactivationPrefix(object __instance) => !FederalAgents.IsAgent(__instance);

    /// <summary>
    /// Keeps agents out of the save entirely. This is a distinct native method from the
    /// <c>NPC.ShouldSave</c> that S1API prefixes — confirmed against the interop metadata — so the two
    /// patches never see each other.
    /// </summary>
    private static bool ShouldSavePrefix(object __instance, ref bool __result)
    {
        if (!FederalAgents.IsAgent(__instance))
            return true;

        __result = false;
        return false;
    }

    // ── Arrest consequences ───────────────────────────────────────────────────────────────────

    private static void NoticeOpenPostfix()
    {
        if (PoliceRuntime.IsLive)
            Guard("arrest notice opening", () => PoliceRuntime.Consequences!.NoticeOpening());
    }

    private static void NoticeClosePostfix()
    {
        if (PoliceRuntime.IsLive)
            Guard("arrest settlement", () => PoliceRuntime.Consequences!.ChargeAtNoticeClose());
    }

    /// <summary>
    /// Widens the confiscation tier while outlawed by rewriting the argument in place, so both the
    /// listing pass and the removal pass agree about what is being taken.
    /// </summary>
    private static void WidenStealthPrefix(object[] __args)
    {
        if (PoliceRuntime.IsLive)
            Guard("confiscation scope", () => PoliceRuntime.Consequences!.WidenStealthArgument(__args));
    }

    private static void ConfiscatePostfix(object __instance)
    {
        if (!PoliceRuntime.IsLive || !HostGate.IsAuthority)
            return;

        Guard("vehicle cargo", () => PoliceRuntime.Consequences!.ConfiscateVehicleCargo(__instance));
        Guard("equipment seizure", () => PoliceRuntime.Consequences!.ConfiscateEquipment());
    }

    /// <summary>
    /// Appends our surcharge to the notice rather than rewriting the game's lines. A postfix that only
    /// adds composes with another mod's postfix; a replacing patch would not, and at least one other
    /// police mod patches this same method.
    /// </summary>
    private static void PenaltyListPostfix(object __result)
    {
        if (PoliceRuntime.IsLive)
            Guard("penalty list", () => PoliceRuntime.Consequences!.AnnotatePenaltyList(__result));
    }

    // ── Plumbing ──────────────────────────────────────────────────────────────────────────────

    private static void Patch(
        HarmonyLib.Harmony harmony,
        string typeName,
        string methodName,
        int argumentCount,
        string? prefix = null,
        string? postfix = null)
    {
        var label = Short(typeName, methodName);

        var target = Resolve(typeName, methodName, argumentCount);
        if (target is null)
        {
            Missing.Add(label);
            return;
        }

        try
        {
            harmony.Patch(
                target,
                prefix is null ? null : new HarmonyMethod(AccessTools.Method(typeof(PolicePatches), prefix)),
                postfix is null ? null : new HarmonyMethod(AccessTools.Method(typeof(PolicePatches), postfix)));

            Applied.Add(label);
        }
        catch (Exception ex)
        {
            Missing.Add(label);
            PoliceLog.Warn($"Could not patch {label}: {PoliceLog.Describe(ex)}");
        }
    }

    /// <summary>
    /// Resolves by declaring type first, so a patch aimed at an override lands on the override rather
    /// than on the base method another mod may already own.
    /// <para>
    /// A trailing <c>*</c> matches by name prefix, which is how FishNet-generated bodies have to be
    /// reached: <c>RpcLogic___Arrest_Client_2166136261</c> carries a signature hash that is stable
    /// within a version and changes with the signature, so hard-coding it would silently stop working
    /// on the next patch that touches the method.
    /// </para>
    /// </summary>
    private static MethodInfo? Resolve(string typeName, string methodName, int argumentCount)
    {
        var type = GameReflection.FindType(typeName);
        if (type is null)
            return null;

        if (!methodName.EndsWith("*", StringComparison.Ordinal))
            return GameReflection.FindMethod(type, methodName, argumentCount);

        var prefix = methodName[..^1];
        foreach (var candidate in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            if (candidate.Name.StartsWith(prefix, StringComparison.Ordinal) &&
                candidate.GetParameters().Length == argumentCount)
            {
                return candidate;
            }
        }

        return null;
    }

    private static string Short(string typeName)
    {
        var dot = typeName.LastIndexOf('.');
        return dot < 0 ? typeName : typeName[(dot + 1)..];
    }

    private static string Short(string typeName, string methodName) =>
        $"{Short(typeName)}.{methodName.TrimEnd('*')}";

    /// <summary>
    /// A throw inside a patch body propagates into game code. Ten of those in a frame auto-disables
    /// the whole module, which is a far worse outcome than one skipped tick.
    /// </summary>
    private static void Guard(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            PoliceLog.Error($"Police Improvements threw while handling {what}; the game carries on.", ex);
        }
    }
}

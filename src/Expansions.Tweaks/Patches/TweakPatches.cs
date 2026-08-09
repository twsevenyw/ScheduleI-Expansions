using System.Reflection;
using Expansions.Core.Diagnostics;
using Expansions.Tweaks.Runtime;
using HarmonyLib;

namespace Expansions.Tweaks.Patches;

/// <summary>
/// Every Harmony patch this module owns, applied explicitly through the per-module instance.
/// <para>
/// Attribute auto-patching is switched off assembly-wide, deliberately: MelonLoader runs
/// <c>PatchAll</c> under the melon's own Harmony id, so an <c>[HarmonyPatch]</c> class would survive
/// <c>UnpatchSelf()</c> and quietly break the reversible-toggle guarantee the suite is built on.
/// </para>
/// <para>
/// No patch here signature-references a game type: instances arrive as <c>object</c> and results
/// through <c>ref</c> primitives, so a renamed game type costs one logged patch rather than a
/// <c>TypeLoadException</c> at mod load. Nothing here is a skipping prefix, so none of them can
/// cancel another mod's work — and none of them contends with an S1API patch (S1API owns
/// <c>DeliveryInstance.SetStatus</c>, which this module deliberately leaves alone).
/// </para>
/// </summary>
internal static class TweakPatches
{
    private static readonly List<string> Applied = new();
    private static readonly List<string> Missing = new();

    internal static IReadOnlyList<string> AppliedTargets => Applied;

    internal static IReadOnlyList<string> MissingTargets => Missing;

    /// <summary>Counts calls that reached the real function, which is how the probe spots inlining.</summary>
    internal static int MixTimeCalls { get; private set; }

    /// <summary>Frames the ATM caption repair has had a chance to run, i.e. the UI was open.</summary>
    internal static int AtmUpdateCalls { get; private set; }

    /// <summary>Delivery quotes that reached the postfix; zero here means the speed-up is a no-op.</summary>
    internal static int DeliveryQuoteCalls { get; private set; }

    internal static bool IsApplied(string label) => Applied.Contains(label);

    internal static void Apply(HarmonyLib.Harmony harmony, TweaksConfig config)
    {
        Applied.Clear();
        Missing.Clear();
        MixTimeCalls = 0;
        AtmUpdateCalls = 0;
        DeliveryQuoteCalls = 0;

        if (config.EnableFasterMixing.Value)
            ApplyMixing(harmony, config);

        if (config.EnableDepositLimit.Value)
            ApplyDeposit(harmony);

        if (config.EnableFasterDeliveries.Value)
            Patch(harmony, GameTypes.DeliveryShop, "GetDeliveryTime", 1, postfix: nameof(DeliveryTimePostfix));

        TweakLog.Msg($"Patched {Applied.Count} target(s)." +
                     (Missing.Count > 0
                         ? $" {Missing.Count} not present on this build: {string.Join(", ", Missing)}."
                         : string.Empty));
    }

    private static void ApplyMixing(HarmonyLib.Harmony harmony, TweaksConfig config)
    {
        // A station built after the sweep, or one whose property streamed in later, is scaled here.
        // Mk2 declares its own Awake and MixingStart, so both types are patched by declaration
        // rather than letting the base lookup hand back the same MethodInfo twice.
        Patch(harmony, GameTypes.MixingStation, "Awake", 0, postfix: nameof(StationAwakePostfix));
        Patch(harmony, GameTypes.MixingStationMk2, "Awake", 0, postfix: nameof(StationAwakePostfix));

        // The belt to Awake's braces: whatever happened earlier, the station is scaled before the
        // one moment its mix duration is read and committed.
        Patch(harmony, GameTypes.MixingStation, "MixingStart", 0, prefix: nameof(MixingStartPrefix));
        Patch(harmony, GameTypes.MixingStationMk2, "MixingStart", 0, prefix: nameof(MixingStartPrefix));

        Patch(harmony, GameTypes.MixingStation, "MixingDone", 0, postfix: nameof(MixingDonePostfix));

        // Only installed when the owner asked to exclude chemists. It is the one lever that can
        // distinguish who started a mix, and it is best-effort by nature — see the postfix.
        if (!config.MixSpeedAppliesToEmployees.Value)
            Patch(harmony, GameTypes.MixingStation, "GetMixTimeForCurrentOperation", 0, postfix: nameof(MixTimePostfix));
    }

    private static void ApplyDeposit(HarmonyLib.Harmony harmony)
    {
        // The weekly reset writes a true value over the shifted counter, so the shift goes back on.
        Patch(harmony, GameTypes.Atm, "WeekPass", 0, postfix: nameof(WeekPassPostfix));

        // The counter is serialised into Money.json. The shift is lifted for the duration of the
        // write so the save records the player's real week-to-date total, never ours.
        Patch(harmony, GameTypes.MoneyManager, "GetSaveString", 0,
            prefix: nameof(SavePrefix), postfix: nameof(SavePostfix));

        Patch(harmony, GameTypes.MoneyManager, "Load", 1, postfix: nameof(MoneyLoadPostfix));

        Patch(harmony, GameTypes.AtmInterface, "Update", 0, postfix: nameof(AtmUpdatePostfix));
    }

    // ── Mixing ────────────────────────────────────────────────────────────────────────────────

    private static void StationAwakePostfix(object __instance) =>
        Guard("a station waking up", () => TweaksRuntime.Mixing?.Track(__instance));

    private static void MixingStartPrefix(object __instance) =>
        Guard("a mix starting", () => TweaksRuntime.Mixing?.Track(__instance));

    private static void MixingDonePostfix() =>
        Guard("a mix finishing", TweaksRuntime.RecordMixCompleted);

    /// <summary>
    /// Takes the speed-up back off for a mix a chemist is running, when the owner asked for that.
    /// <para>
    /// Best-effort, and honestly so. Attribution comes from <c>NPCUserObject</c>, which the game sets
    /// when an NPC takes the station — if a chemist starts a mix without that having been recorded
    /// first, the mix is simply fast. And because this is a postfix on a small getter, IL2CPP may
    /// have inlined the getter into its callers, in which case the exclusion does nothing at all.
    /// Failing towards "faster" rather than towards "broken" is the right way for this one to break,
    /// which is why the station field carries the feature and this only trims it.
    /// </para>
    /// </summary>
    private static void MixTimePostfix(object __instance, ref int __result)
    {
        MixTimeCalls++;

        var runtime = TweaksRuntime.Mixing;
        if (runtime is null || __result <= 0)
            return;

        if (!GameReflection.IsPresent(Members.ReadObject(__instance, "NPCUserObject")))
            return;

        var ratio = runtime.VanillaRatio(__instance);
        if (ratio > 1.0001f)
            __result = Math.Max(1, (int)Math.Round(__result * ratio, MidpointRounding.AwayFromZero));
    }

    // ── ATM deposit limit ─────────────────────────────────────────────────────────────────────

    private static void WeekPassPostfix() =>
        Guard("the weekly deposit reset", () => TweaksRuntime.Deposit?.Reconcile("the weekly reset"));

    private static void SavePrefix() =>
        Guard("a money save", () => TweaksRuntime.Deposit?.LiftForSave());

    private static void SavePostfix() =>
        Guard("a money save", () => TweaksRuntime.Deposit?.RestoreAfterSave());

    private static void MoneyLoadPostfix() =>
        Guard("a money load", () => TweaksRuntime.Deposit?.Reconcile("a save load"));

    private static void AtmUpdatePostfix(object __instance)
    {
        var deposit = TweaksRuntime.Deposit;
        if (deposit is not { IsActive: true } || !Members.Read(__instance, "IsOpen", false))
            return;

        AtmUpdateCalls++;
        Guard("the ATM caption", () => deposit.RepairCaption(__instance));
    }

    // ── Deliveries ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The whole of the delivery speed-up. <c>GetDeliveryTime</c> is the one function that decides
    /// how long an order takes, so scaling its result moves the ETA on the order screen and the
    /// <c>TimeUntilArrival</c> baked into the delivery in the same stroke, and there is nothing
    /// written to the world that would need undoing.
    /// <para>
    /// <c>ref int</c> and <c>object</c> only: no game type appears in this signature, so a rename
    /// costs a logged miss rather than a load-time failure.
    /// </para>
    /// </summary>
    private static void DeliveryTimePostfix(ref int __result)
    {
        DeliveryQuoteCalls++;

        var scaled = DeliverySpeed.ScaleQuote(__result);
        if (scaled > 0)
            __result = scaled;
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
        var label = Short(typeName) + "." + methodName;

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
                prefix is null ? null : new HarmonyMethod(AccessTools.Method(typeof(TweakPatches), prefix)),
                postfix is null ? null : new HarmonyMethod(AccessTools.Method(typeof(TweakPatches), postfix)));

            Applied.Add(label);
        }
        catch (Exception ex)
        {
            Missing.Add(label);
            TweakLog.Warn($"Could not patch {label}: {TweakLog.Describe(ex)}");
        }
    }

    /// <summary>
    /// Declared methods only. <c>MixingStationMk2</c> overrides <c>Awake</c> and <c>MixingStart</c>,
    /// and a lookup that walked to the base would hand back the very same <c>MethodInfo</c> for both
    /// types — patching it twice, and applying the postfix twice per call.
    /// </summary>
    private static MethodInfo? Resolve(string typeName, string methodName, int argumentCount)
    {
        var type = GameReflection.FindType(typeName);
        if (type is null)
            return null;

        const BindingFlags Declared =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
            BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (var candidate in type.GetMethods(Declared))
        {
            if (candidate.Name != methodName)
                continue;

            try
            {
                if (candidate.GetParameters().Length == argumentCount)
                    return candidate;
            }
            catch
            {
                // An unresolvable parameter type on this overload; keep looking at the others.
            }
        }

        return null;
    }

    private static string Short(string typeName)
    {
        var dot = typeName.LastIndexOf('.');
        return dot < 0 ? typeName : typeName[(dot + 1)..];
    }

    /// <summary>
    /// A throw inside a patch body propagates into game code, and ten of those in a frame auto-disable
    /// the melon. One skipped station is a far better outcome.
    /// </summary>
    private static void Guard(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            TweakLog.Error($"Quality of Life threw while handling {what}; the game carries on.", ex);
        }
    }
}

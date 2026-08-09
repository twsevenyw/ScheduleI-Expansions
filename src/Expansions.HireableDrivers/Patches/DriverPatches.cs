using System.Reflection;
using Expansions.Core;
using Expansions.HireableDrivers.Game;
using Expansions.HireableDrivers.Runtime;
using HarmonyLib;

namespace Expansions.HireableDrivers.Patches;

/// <summary>
/// The four patches the transport loop needs, applied only through the module's own Harmony instance.
/// <para>
/// No <c>[HarmonyPatch]</c> attributes anywhere: the mod assembly carries
/// <c>[assembly: HarmonyDontPatchAll]</c> precisely so that disabling the module genuinely unpatches,
/// and an attribute class would survive <c>UnpatchSelf</c> under MelonLoader's own Harmony id.
/// </para>
/// <para>
/// Every body checks the driver registry first, so a vanilla Handler is never affected. Each patch is
/// applied independently and a failure is survivable: with no stations and no vanilla routes assigned,
/// an unsuppressed driver simply idles rather than fighting us for its own legs.
/// </para>
/// </summary>
internal static class DriverPatches
{
    private static readonly List<string> Applied = new();
    private static readonly List<string> Skipped = new();

    internal static IReadOnlyList<string> AppliedPatches => Applied.ToArray();

    internal static IReadOnlyList<string> SkippedPatches => Skipped.ToArray();

    /// <summary>
    /// Both are load-bearing. Without UpdateBehaviour suppression and the ready-route veto, the
    /// vanilla Handler brain can move the same route while the driver state machine is using it.
    /// </summary>
    internal static bool TransportSafe =>
        IsApplied("UpdateBehaviour") && IsApplied("GetTransitRouteReady");

    /// <summary>
    /// Whether a named patch landed, without materialising the list. Read from per-frame availability
    /// predicates, so it must not allocate.
    /// </summary>
    internal static bool IsApplied(string methodName)
    {
        // Index-based rather than an enumerator: the list is only written during Apply, but a foreach
        // that overlapped it would throw where a stale read is harmless.
        for (var i = 0; i < Applied.Count; i++)
        {
            if (Applied[i].EndsWith("." + methodName, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    internal static void Apply(HarmonyLib.Harmony harmony, ModuleLifetime lifetime)
    {
        Applied.Clear();
        Skipped.Clear();

        var packager = Gx.RequireType(GameTypes.Packager);
        var configuration = Gx.Type(GameTypes.PackagerConfiguration);
        var routeEntry = Gx.Type(GameTypes.RouteEntryUi);
        var configPanel = Gx.Type(GameTypes.PackagerConfigPanel);

        // The one place a skipping prefix is genuinely required: UpdateBehaviour is the per-tick work
        // dispatcher and there is no other way to stop the packaging brain issuing its own movement.
        Patch(harmony, packager, "UpdateBehaviour", nameof(SuppressUpdateBehaviour), PatchKind.Prefix);

        Patch(harmony, packager, "ShouldIdle", nameof(NeverIdleMidTrip), PatchKind.Postfix);
        Patch(harmony, packager, "IsAnyWorkInProgress", nameof(BusyMidTrip), PatchKind.Postfix);
        Patch(harmony, packager, "Fire", nameof(OnFired), PatchKind.Postfix);
        Patch(harmony, packager, "GetTransitRouteReady", nameof(DriverRoutesAreOurs), PatchKind.Postfix);
        Patch(harmony, configuration, "IsStationValid", nameof(NoStationsForDrivers), PatchKind.Postfix);
        Patch(harmony, routeEntry, "ObjectValid", nameof(ConstrainRouteEndpoints), PatchKind.Postfix);
        Patch(harmony, routeEntry, "DestinationClicked", nameof(PickDestinationFromList), PatchKind.Prefix);
        Patch(harmony, routeEntry, "RefreshUI", nameof(ShowDealerDestination), PatchKind.Postfix);
        Patch(harmony, configPanel, "BindInternal", nameof(DressDriverPanel), PatchKind.Postfix);

        lifetime.OnDispose(() =>
        {
            Applied.Clear();
            Skipped.Clear();
        });

        if (Applied.Count > 0)
            DriverLog.Debug($"Patched: {string.Join(", ", Applied)}.");

        if (Skipped.Count > 0)
            DriverLog.Warn(
                $"Could not patch {string.Join(", ", Skipped)}. " +
                (TransportSafe
                    ? "The affected native UI/cleanup path has a fallback."
                    : "Trips are disabled so the vanilla Handler brain cannot duplicate a route."));
    }

    private enum PatchKind
    {
        Prefix,
        Postfix,
    }

    private static void Patch(HarmonyLib.Harmony harmony, Type? target, string methodName, string patchName, PatchKind kind)
    {
        var label = $"{target?.Name ?? "?"}.{methodName}";

        try
        {
            var original = target is null ? null : Gx.Method(target, methodName, AnySignature(target, methodName));
            if (original is null)
            {
                Skipped.Add(label);
                return;
            }

            var replacement = typeof(DriverPatches).GetMethod(patchName, BindingFlags.NonPublic | BindingFlags.Static);
            if (replacement is null)
            {
                Skipped.Add(label);
                return;
            }

            var harmonyMethod = new HarmonyMethod(replacement);
            harmony.Patch(
                original,
                prefix: kind == PatchKind.Prefix ? harmonyMethod : null,
                postfix: kind == PatchKind.Postfix ? harmonyMethod : null);

            Applied.Add(label);
        }
        catch (Exception ex)
        {
            Skipped.Add($"{label} ({ex.GetType().Name})");
        }
    }

    /// <summary>
    /// Finds the declared overload's real arity so the patch binds to the exact override slot rather
    /// than to whatever the base class happens to expose.
    /// </summary>
    private static string[] AnySignature(Type target, string methodName)
    {
        for (var current = target; current is not null; current = current.BaseType)
        {
            foreach (var candidate in current.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (string.Equals(candidate.Name, methodName, StringComparison.Ordinal))
                    return Enumerable.Repeat(Gx.Any, candidate.GetParameters().Length).ToArray();
            }
        }

        return Array.Empty<string>();
    }

    /// <summary>
    /// Silences the packaging brain for the duration of a trip only.
    /// <para>
    /// Suppressing it permanently would be a mistake: the vanilla per-tick dispatcher is also where the
    /// bed-and-wage bookkeeping lives, so a driver whose <c>UpdateBehaviour</c> never runs would never be
    /// marked paid and <c>CanWork()</c> would stay false forever. Between trips a driver is an ordinary
    /// Handler with no stations and no vanilla routes, which finds nothing to do and idles — so leaving
    /// the brain alone then costs nothing and buys the whole employee contract.
    /// </para>
    /// </summary>
    private static bool SuppressUpdateBehaviour(object __instance) =>
        !DriverRegistry.TryGet(__instance, out var brain) || !brain.IsOnTrip;

    private static void NeverIdleMidTrip(object __instance, ref bool __result)
    {
        if (DriverRegistry.TryGet(__instance, out var brain) && brain.IsOnTrip)
            __result = false;
    }

    private static void BusyMidTrip(object __instance, ref bool __result)
    {
        if (DriverRegistry.TryGet(__instance, out var brain) && brain.IsOnTrip)
            __result = true;
    }

    private static void OnFired(object __instance)
    {
        if (!DriverRegistry.TryGet(__instance, out var brain))
            return;

        try
        {
            DriverHiring.Release(brain);
            DriverRegistry.Unregister(brain.Record.EmployeeId);
            DriverLog.Msg($"{brain.Name} was fired; the route assignments went with them.");
        }
        catch (Exception ex)
        {
            DriverLog.Error("Firing a driver did not tear down cleanly.", ex);
        }
    }

    /// <summary>
    /// Stops the management clipboard offering a driver the Stations row it inherits from the Handler.
    /// Cosmetic, but a station assigned to a driver would silently never be worked.
    /// </summary>
    private static void NoStationsForDrivers(object __instance, ref bool __result, ref string reason)
    {
        if (!__result)
            return;

        var packager = Gx.GetAlive(__instance, "packager");
        if (packager is null || !DriverRegistry.IsDriver(packager))
            return;

        __result = false;
        reason = "Drivers run transit routes, not stations.";
    }

    /// <summary>
    /// A driver's routes are shown and edited on the vanilla clipboard but executed by this mod, so the
    /// packaging brain must never see one ready. Without this the Handler would walk a same-property
    /// route on foot while the driver was planning the same trip by road.
    /// <para>
    /// A postfix rather than a skipping prefix because the original still has to assign its <c>out</c>
    /// item parameter; a skipped call would leave the caller's local holding whatever was there.
    /// </para>
    /// </summary>
    private static void DriverRoutesAreOurs(object __instance, ref object __result)
    {
        if (__result is not null && DriverRegistry.IsDriver(__instance))
            __result = null!;
    }

    /// <summary>
    /// The endpoint rule, applied inside the game's own worldspace picker so hovering an invalid
    /// entity shows the reason and clicking it does nothing — no separate mod validation UI.
    /// <para>
    /// Sources are pinned to the property the driver was hired at. Destinations are widened instead:
    /// a Handler may only route within its property, and the whole point of a driver is that it can
    /// cross town.
    /// </para>
    /// </summary>
    private static void ConstrainRouteEndpoints(object __instance, object obj, ref string reason, ref bool __result)
    {
        var brain = DriverRegistry.ConfiguredDriver();
        if (brain is null)
            return;

        // The clipboard's selection can outlive the panel it was made on, so confirm this row really is
        // one of that driver's before overruling the game about it.
        if (ClipboardApi.RowIndex(brain.Employee, Gx.GetAlive(__instance, "AssignedRoute")) < 0)
            return;

        if (Gx.Get(__instance, "settingSource") is true)
        {
            if (__result && !ClipboardRoutes.IsSourceAllowed(brain, obj, out var refusal))
            {
                __result = false;
                reason = refusal;
            }

            return;
        }

        if (__result || !Gx.Alive(obj) || Gx.Get(obj, "IsAcceptingItems") is not true)
            return;

        // Only overturn a refusal that is about reach. A destination that is already this route's own
        // pickup is still nonsense, and the vanilla message for it is the right one.
        var source = Gx.GetAlive(Gx.Get(__instance, "AssignedRoute"), "Source");
        if (source is not null && string.Equals(Gx.GuidOf(source), Gx.GuidOf(obj), StringComparison.OrdinalIgnoreCase))
            return;

        __result = true;
        reason = string.Empty;
    }

    /// <summary>
    /// Sends a driver's drop-off button to the clipboard's own option-list screen, which can name a
    /// warehouse across town or a dealer — neither of which the worldspace picker can reach. Returning
    /// true anywhere here means the shipped picker runs exactly as it always did.
    /// </summary>
    private static bool PickDestinationFromList(object __instance)
    {
        try
        {
            return RoutePicker.OnDestinationClicked(__instance);
        }
        catch (Exception ex)
        {
            DriverLog.Error("The driver drop-off list threw; falling back to the game's own picker.", ex);
            return true;
        }
    }

    /// <summary>
    /// Writes the dealer's name onto a row the game left blank. A <c>Dealer</c> is an NPC, not an
    /// <c>ITransitEntity</c>, so it cannot live in the vanilla route object and the row would otherwise
    /// read "None" for a drop-off that is really set.
    /// </summary>
    private static void ShowDealerDestination(object __instance)
    {
        try
        {
            RoutePicker.LabelRow(__instance);
        }
        catch (Exception ex)
        {
            DriverLog.Debug($"Could not label a dealer drop-off ({Gx.Explain(ex)}).");
        }
    }

    /// <summary>
    /// Makes the shipped Packager panel read as a driver's panel: no Stations row, and a Routes heading
    /// that names the property the driver is allowed to collect from.
    /// <para>
    /// The panel is instantiated fresh from its prefab for every selection, so this only ever edits a
    /// throwaway instance and non-drivers are explicitly restored rather than left to luck.
    /// </para>
    /// </summary>
    private static void DressDriverPanel(object __instance, object configs)
    {
        try
        {
            var drivers = new List<DriverBrain>();
            var total = 0;

            foreach (var config in Gx.List(configs))
            {
                total++;

                // Enumerating a List<EntityConfiguration> hands back base-typed wrappers, so the
                // derived member is only reachable after an IL2CPP cast.
                var packagerConfig = Gx.Cast(config, GameTypes.PackagerConfiguration) ?? config;
                if (DriverRegistry.TryGet(Gx.GetAlive(packagerConfig, "packager"), out var brain))
                    drivers.Add(brain);
            }

            var isDriverPanel = total > 0 && drivers.Count == total;

            if (Gx.GetAlive(Gx.GetAlive(__instance, "StationsUI"), "gameObject") is UnityEngine.GameObject stations)
                stations.SetActive(!isDriverPanel);

            // The heading is only ever written for a driver. Writing a guess at the vanilla wording back
            // for a Handler would be the mod editing a panel it has no business editing.
            if (!isDriverPanel)
                return;

            var routes = Gx.GetAlive(__instance, "RoutesUI");
            if (routes is null)
                return;

            var heading = drivers.Count == 1
                ? $"Routes - collects at {ClipboardRoutes.HomeName(drivers[0])}"
                : "Routes - each driver collects at its own property";

            // FieldText is what RouteListFieldUI.Start writes into the label, and Start runs after Bind
            // on a freshly instantiated panel, so both have to be set or the heading is overwritten.
            Gx.Set(routes, "FieldText", heading);

            if (Gx.GetAlive(routes, "FieldLabel") is { } label)
                Gx.Set(label, "text", heading);
        }
        catch (Exception ex)
        {
            DriverLog.Debug($"Could not dress the driver clipboard panel ({Gx.Explain(ex)}).");
        }
    }
}

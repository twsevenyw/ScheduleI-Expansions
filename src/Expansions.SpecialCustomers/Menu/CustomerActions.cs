using Expansions.Core.Actions;
using Expansions.SpecialCustomers.Archetypes;
using Expansions.SpecialCustomers.Detection;
using Expansions.SpecialCustomers.Game;
using Expansions.SpecialCustomers.Visitors;
using Expansions.SpecialCustomers.Visits;
using S1API.Entities;
using UnityEngine;

namespace Expansions.SpecialCustomers.Menu;

/// <summary>
/// Everything the module can do from the Expansions screen, with no console involved.
/// <para>
/// Every action returns an <see cref="ActionResult"/> rather than logging, because the owner's
/// in-game console does not work: a button whose answer only reaches the MelonLoader window has, for
/// their purposes, done nothing visible.
/// </para>
/// </summary>
internal static class CustomerActions
{
    /// <summary>The id Core's registry reserves for this module's teleport.</summary>
    internal const string TeleportVisitorId = "special_customers.tp_visitor";

    /// <summary>Stand this far in front of somebody rather than inside them.</summary>
    private const float TeleportStandoff = 1.6f;

    internal static IEnumerable<ExpansionAction> Build(VisitDirector director)
    {
        yield return new ExpansionAction(
            id: TeleportVisitorId,
            label: $"Teleport to {VisitorSlot.Primary.FullName}",
            description: "Puts you a step in front of the group's advance scout, who stays on the Northtown motel forecourt between visits. He is also the leader when a group is in town.",
            isAvailable: () => VisitorRuntime.StatusOf(VisitorSlot.Primary).WrapperResolved
                ? ActionAvailability.Ready
                : ActionAvailability.Unavailable("he is not in the world yet - load a save first"),
            invoke: () => TeleportTo(VisitorSlot.Primary),
            order: 10);

        yield return new ExpansionAction(
            id: "special_customers.tp_group",
            label: "Teleport to the visiting group",
            description: "Puts you at the meeting point of whichever group is in town.",
            isAvailable: () => director.Current is not null
                ? ActionAvailability.Ready
                : ActionAvailability.Unavailable("no group is in town"),
            invoke: () => TeleportToGroup(director),
            order: 20);

        yield return new ExpansionAction(
            id: "special_customers.park_visitor",
            label: "Send the scout back to his post",
            description: "Warps the scout back onto the Northtown forecourt if he has wandered or got stuck.",
            isAvailable: HostOnly(() => VisitorRuntime.Resolve(VisitorSlot.Primary) is not null, "he is not in the world"),
            invoke: ParkScout,
            order: 30);

        yield return new ExpansionAction(
            id: "special_customers.force_visit",
            label: "Bring a group into town now",
            description: "Skips the countdown and starts a visit immediately. Pick which group from the list.",
            isAvailable: CanStartVisit(director),
            choices: ArchetypeChoices,
            invokeChoice: choice => StartVisit(director, choice.Id),
            order: 40);

        yield return new ExpansionAction(
            id: "special_customers.force_offer",
            label: "Make the group offer now",
            description: "Sends the visiting group's bulk contract to your phone without waiting for their order time.",
            isAvailable: HostOnly(() => director.Current is not null, "no group is in town"),
            invoke: () => director.ForceOffer(out var message) ? ActionResult.Ok(message) : ActionResult.Failed(message),
            order: 50);

        yield return new ExpansionAction(
            id: "special_customers.clear_offer",
            label: "Clear the group's stuck order",
            description: "Drops every offer and contract the visitor pool is holding. Use this when the leader keeps saying they already have an order pending. An accepted contract you were still working on will be expired too.",
            isAvailable: HostOnly(() => VisitorRuntime.ResolvedCount() > 0, "no visitor is in the world yet"),
            invoke: () => director.ClearOfferState(out var message) ? ActionResult.Ok(message) : ActionResult.Failed(message),
            order: 55);

        yield return new ExpansionAction(
            id: "special_customers.sale_loop",
            label: "Why can't I sell to them?",
            description: "Walks the sale from the leader's contract state to the phone and back, and says in one sentence what to do next.",
            isAvailable: () => ActionAvailability.Ready,
            invoke: () => ActionResult.Ok(director.DescribeSaleLoop()),
            order: 56);

        yield return new ExpansionAction(
            id: "special_customers.send_home",
            label: "Send the group home",
            description: "Ends the visit early and runs the full departure unwind: undressed, re-parked, hidden, every tuned value restored.",
            isAvailable: HostOnly(() => director.Current is not null, "no group is in town"),
            invoke: () => director.SendHome(out var message) ? ActionResult.Ok(message) : ActionResult.Failed(message),
            order: 60);

        yield return new ExpansionAction(
            id: "special_customers.status",
            label: "Show group status",
            description: "Who is in town, where, when they order, when they leave, and when the next group is due.",
            isAvailable: () => ActionAvailability.Ready,
            invoke: () => ActionResult.Ok(string.Join("\n", director.DescribeStatus())),
            order: 70);

        yield return new ExpansionAction(
            id: "special_customers.detection",
            label: "Show detection score",
            description: "Whether the module has stood itself down because the game looks like it ships Special Customers natively, and the evidence for it.",
            isAvailable: () => ActionAvailability.Ready,
            invoke: DetectionReport,
            order: 80);
    }

    /// <summary>
    /// Anything that moves an NPC is server-authoritative. The refusal names the reason so a co-op
    /// guest sees "host only" rather than a button that silently does nothing.
    /// </summary>
    private static Func<ActionAvailability> HostOnly(Func<bool> ready, string notReadyReason) => () =>
    {
        if (!HostGate.Evaluate(out var authority))
            return ActionAvailability.Unavailable($"host only - this peer is a {authority}");

        return ready() ? ActionAvailability.Ready : ActionAvailability.Unavailable(notReadyReason);
    };

    private static Func<ActionAvailability> CanStartVisit(VisitDirector director) => () =>
    {
        if (!OfficialFeatureDetector.Verdict.IsEnabled)
            return ActionAvailability.Unavailable("the module has stood itself down - see Show detection score");

        if (!HostGate.Evaluate(out var authority))
            return ActionAvailability.Unavailable($"host only - this peer is a {authority}");

        if (director.Current is { } visit)
            return ActionAvailability.Unavailable($"{visit.Archetype.DisplayName} are already in town");

        return VisitorRuntime.CustomNpcsReady()
            ? ActionAvailability.Ready
            : ActionAvailability.Unavailable("the visitor NPCs are not in the world yet");
    };

    private static IReadOnlyList<ActionChoice> ArchetypeChoices()
    {
        var choices = new List<ActionChoice>(ArchetypeCatalog.All.Count + 1)
        {
            new("", "Whoever is due next", "Weighted pick, never the same group twice running"),
        };

        foreach (var archetype in ArchetypeCatalog.All)
        {
            var drugs = string.Join(" / ", archetype.EffectiveDrugs());
            var detail = archetype.IsEnabled
                ? $"{archetype.ShortName} - {drugs}, {archetype.Standards} standards, orders at {GameClock.Format(archetype.OrderTime)}"
                : $"{archetype.ShortName} - switched off in the config";

            choices.Add(new ActionChoice(archetype.Id, archetype.DisplayName, detail));
        }

        return choices;
    }

    private static ActionResult StartVisit(VisitDirector director, string archetypeId)
    {
        var archetype = ArchetypeCatalog.Find(archetypeId);
        if (archetype is not null && !archetype.IsEnabled)
        {
            return ActionResult.Failed(
                $"{archetype.DisplayName} are switched off. Turn archetype_{archetype.Id}_enabled back on in " +
                "UserData/Expansions.cfg under [SpecialCustomers_01_Main].");
        }

        return director.ForceVisit(archetypeId, out var message)
            ? ActionResult.Ok(message)
            : ActionResult.Failed(message);
    }

    private static ActionResult TeleportTo(VisitorSlot slot)
    {
        var status = VisitorRuntime.StatusOf(slot);
        if (!status.WrapperResolved)
            return ActionResult.Failed($"{slot.FullName} is not in the world - {status.Failure}. Run the diagnostics probes for the full picture.");

        return MovePlayerTo(status.Position, out var failure)
            ? ActionResult.Ok($"Teleported to {status.FullName} at {Describe.Of(status.Position)}.")
            : ActionResult.Failed($"Could not teleport: {failure}");
    }

    private static ActionResult TeleportToGroup(VisitDirector director)
    {
        var visit = director.Current;
        if (visit is null)
            return ActionResult.Failed("No group is in town right now.");

        return MovePlayerTo(visit.StandPoint, out var failure)
            ? ActionResult.Ok($"Teleported to {visit.Archetype.DisplayName} at {visit.DeliveryLocationName} in {WorldGeography.NameOf(visit.Region)}.")
            : ActionResult.Failed($"Could not teleport: {failure}");
    }

    private static ActionResult ParkScout()
    {
        var slot = VisitorSlot.Primary;
        var npc = VisitorRuntime.Resolve(slot);
        if (npc is null)
            return ActionResult.Failed($"{slot.FullName} is not in the world, so there is nothing to park.");

        try
        {
            npc.Movement.Warp(slot.SpawnPosition);
            return ActionResult.Ok($"Warped {slot.FullName} back to {Describe.Of(slot.SpawnPosition)}.");
        }
        catch (Exception ex)
        {
            return ActionResult.Error($"Parking {slot.FullName} failed", ex);
        }
    }

    private static ActionResult DetectionReport()
    {
        var verdict = OfficialFeatureDetector.Verdict;
        var lines = new List<string>(verdict.Evidence.Count + 4)
        {
            $"Detection: {verdict.Headline}.",
            verdict.Reason,
        };

        lines.AddRange(verdict.Evidence);

        if (!verdict.IsEnabled)
            lines.Add("Override with detection_mode = always_on in UserData/Expansions.cfg under [SpecialCustomers_01_Main].");

        var message = string.Join("\n", lines);
        return verdict.IsEnabled ? ActionResult.Ok(message) : ActionResult.NoChange(message);
    }

    private static bool MovePlayerTo(Vector3 target, out string failure)
    {
        try
        {
            var player = Player.Local;
            if (player is null)
            {
                failure = "there is no local player yet - load a save first";
                return false;
            }

            // Moving your own camera is a client-side operation, so no authority check: a co-op
            // guest is allowed to walk over and look.
            var away = player.Position - target;
            away.y = 0f;
            var offset = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward;

            player.Position = target + (offset * TeleportStandoff);
            failure = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            failure = Describe.Of(ex);
            return false;
        }
    }
}

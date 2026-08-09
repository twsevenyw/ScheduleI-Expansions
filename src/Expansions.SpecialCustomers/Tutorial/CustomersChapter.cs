using Expansions.Core;
using Expansions.Core.Tutorial;
using Expansions.SpecialCustomers.Detection;
using Expansions.SpecialCustomers.Visitors;
using Expansions.SpecialCustomers.Visits;
using S1API.Entities;
using UnityEngine;

namespace Expansions.SpecialCustomers.Tutorial;

/// <summary>
/// Walks the player from "a group arrived" to "I got paid".
/// <para>
/// The id matches the module id, which is how it replaces Core's placeholder chapter — and how
/// disabling the module puts the placeholder back rather than leaving a hole in the quest line.
/// </para>
/// </summary>
internal sealed class CustomersChapter : ITutorialChapter
{
    /// <summary>Close enough to be standing with the group rather than looking at them from a roof.</summary>
    private const float ArrivalRadius = 12f;

    private readonly VisitDirector _director;

    internal CustomersChapter(VisitDirector director) => _director = director;

    public string Id => SpecialCustomersModule.ModuleId;

    public string Title => "Special Customers";

    public string Description =>
        "Bikers, businessmen, hippies and a rock band roll into Hyland Point every couple of days, park " +
        "themselves in one district for a day, and buy in bulk. They pay a little under market for the " +
        "convenience. Everything runs through the game's own contract, handover and payment systems.";

    public int Order => 700;

    public TutorialAvailability GetAvailability()
    {
        if (!ExpansionRegistry.IsEnabled(SpecialCustomersModule.ModuleId))
            return TutorialAvailability.ComingSoon("Special Customers is switched off");

        var verdict = OfficialFeatureDetector.Verdict;
        if (!verdict.IsEnabled)
            return TutorialAvailability.ComingSoon($"the module has stood itself down: {verdict.Reason}");

        return VisitorRuntime.ResolvedCount() > 0
            ? TutorialAvailability.Available
            : TutorialAvailability.ComingSoon("the visitor NPCs are not in the world yet — load a save first");
    }

    public void BuildSteps(ITutorialChapterBuilder builder)
    {
        // Sampled at build time so only what the player does from here counts. If a group happens to
        // be in town already, the first objective is satisfied immediately, which is correct.
        var deliveriesAtStart = CompletedDeliveries();

        builder
            .AddStep("arrive", "Wait for a group to arrive in town")
            .Describe(
                "A group visits every two or three in-game days and stays until four in the morning. " +
                "You get a text from their leader when they turn up. Impatient? Open the Expansions " +
                "screen and use 'Bring a group into town now'.")
            .CompletesWhen(() => _director.Current is not null);

        builder
            .AddStep("find", "Go and find them")
            .Describe(
                "Their meeting point is marked on your map, and every member carries the standard " +
                "customer pin. 'Teleport to the visiting group' on the Expansions screen takes you " +
                "straight there.")
            .CompletesWhen(IsStandingWithTheGroup);

        builder
            .AddStep("offer", "Receive their bulk offer")
            .Describe(
                "The leader texts an offer at the group's own order time — 2 PM for the businessmen, " +
                "11 AM for the hippies, 9 PM for the bikers, 11 PM for the band. " +
                "'Make the group offer now' sends it early.")
            .CompletesWhen(() => _director.Current is { OfferSent: true });

        builder
            .AddStep("accept", "Accept it and pick a deal window")
            .Describe(
                "This is the shipped contract flow, unchanged: accept on the phone, choose a window, " +
                "and the leader walks to the meeting point and waits for you.")
            .CompletesWhen(LeaderHasContract);

        builder
            .AddStep("deliver", "Hand the product over and get paid")
            .Describe(
                "Meet them in the window with the quantity and quality they asked for. The money, the XP, " +
                "the quality matching and the receipt in your analytics app are all the game's own — the mod " +
                "only wrote the offer. They pay a little under market, which is the price of dumping " +
                "everything at once.")
            .CompletesWhen(() => CompletedDeliveries() > deliveriesAtStart);
    }

    /// <summary>
    /// Handovers the pool has completed, summed. The game raises this only after it has evaluated a
    /// delivery and paid for it, so it is the honest "you got paid" signal.
    /// </summary>
    private static int CompletedDeliveries()
    {
        var total = 0;
        foreach (var slot in VisitorSlot.All)
            total += Game.GameNpc.Resolve(slot.Id, out _)?.CompletedDeliveries ?? 0;

        return total;
    }

    /// <summary>
    /// Reads the leader's live contract rather than patching the acceptance path. The shipped
    /// pipeline already records the answer; a Harmony patch would only be a second copy of it.
    /// </summary>
    private bool LeaderHasContract()
    {
        var visit = _director.Current;
        if (visit is null)
            return false;

        return Game.GameNpc.Resolve(visit.Leader.Id, out _)?.HasLiveContract == true;
    }

    private bool IsStandingWithTheGroup()
    {
        var visit = _director.Current;
        if (visit is null)
            return false;

        try
        {
            var player = Player.Local;
            return player is not null && Vector3.Distance(player.Position, visit.StandPoint) <= ArrivalRadius;
        }
        catch
        {
            return false;
        }
    }
}

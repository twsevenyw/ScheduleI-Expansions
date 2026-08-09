using Expansions.Core.Tutorial;
using Expansions.SpecialCustomers.Configuration;
using Expansions.SpecialCustomers.Visitors;
using Expansions.SpecialCustomers.Visits;
using S1API.Entities;
using UnityEngine;

namespace Expansions.SpecialCustomers.Tutorial;

/// <summary>
/// Walks the player from "a group arrived" to "I got paid", in six objectives that each watch real
/// game state.
/// <para>
/// No step completes itself and none of them is a note in place of a feature. Every condition reads
/// something the shipped pipeline owns — the director's live visit, the player's own position, the
/// leader's <c>OfferedContractInfo</c> and <c>CurrentContract</c>, and the pool's
/// <c>CompletedDeliveries</c> — so ticking one off means the thing actually happened.
/// </para>
/// <para>
/// The chapter reports itself available unconditionally. Core turns an unavailable chapter into a
/// single objective that ticks itself off three seconds later, which would be a placeholder in the
/// middle of the quest line; when this module genuinely cannot run, it withdraws the whole chapter
/// instead so the line has one fewer real chapter rather than one fake one.
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

    public TutorialAvailability GetAvailability() => TutorialAvailability.Available;

    public void BuildSteps(ITutorialChapterBuilder builder)
    {
        // Sampled at build time so only what the player does from here counts. If a group happens to
        // be in town already, the first objective is satisfied immediately, which is correct.
        var deliveriesAtStart = CompletedDeliveries();

        builder
            .AddStep("products", "Decide what they are allowed to buy")
            .Describe(
                "Special customers only order from a list you control. Open the Expansions screen and use " +
                "'Show the product allow-list' to see which names resolved to real products. Edit " +
                $"{ProductAllowList.ConfigKey} in UserData/Expansions.cfg under [SpecialCustomers_01_Main] to " +
                "change it — a custom mix-in from another mod works as long as you spell its name the way the " +
                "Products app does.")
            .CompletesWhen(AllowListResolves);

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
                "straight there. Look at them properly while you are there — every member is dressed " +
                "individually, and they will be the same people if you reload.")
            .CompletesWhen(IsStandingWithTheGroup);

        builder
            .AddStep("offer", "Receive their bulk offer")
            .Describe(
                "The leader texts an offer at the group's own order time — 2 PM for the businessmen, " +
                "11 AM for the hippies, 9 PM for the bikers, 11 PM for the band. It will only ever name " +
                "something from your allow-list. 'Make the group offer now' sends it early.")
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
    /// True once at least one configured product name resolves to a real definition — or once the
    /// list has deliberately been cleared, which is the "let them buy anything" setting.
    /// </summary>
    private static bool AllowListResolves()
    {
        var resolution = ProductAllowList.Current();
        return !resolution.IsRestricted || resolution.Matched.Count > 0;
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

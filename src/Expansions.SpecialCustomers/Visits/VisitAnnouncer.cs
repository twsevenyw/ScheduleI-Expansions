using Expansions.SpecialCustomers.Configuration;
using Expansions.SpecialCustomers.Game;
using Expansions.SpecialCustomers.Visitors;
using S1API.Map;

namespace Expansions.SpecialCustomers.Visits;

/// <summary>
/// The three ways a player finds out a group is in town: a text from the leader, a marker on the
/// map, and the standard customer pins on each member.
/// <para>
/// All three are shipped machinery. Nothing here draws its own UI.
/// </para>
/// </summary>
internal static class VisitAnnouncer
{
    private const string GroupMarkerId = "special_customers.group";

    internal static string LastMessage { get; private set; } = string.Empty;

    internal static void AnnounceArrival(Visit visit)
    {
        var text = string.Format(visit.Archetype.ArrivalMessage, WorldGeography.NameOf(visit.Region));
        Send(visit, text);
        ShowMarker(visit);
        SetMemberPins(visit, true);
    }

    internal static void AnnounceDeparture(Visit visit, bool silent)
    {
        if (!silent)
            Send(visit, visit.Archetype.DepartureMessage);

        HideMarker();
        SetMemberPins(visit, false);
    }

    internal static void AnnounceOffer(Visit visit, int quantity, string productName, float payment)
    {
        var text = string.Format(
            visit.Archetype.OfferMessage,
            quantity,
            productName,
            payment.ToString("0"));

        Send(visit, text);
    }

    /// <summary>Re-attaches the map marker after a load without re-sending the arrival text.</summary>
    internal static void RestoreMarker(Visit visit)
    {
        ShowMarker(visit);
        SetMemberPins(visit, true);
    }

    internal static void HideMarker()
    {
        try
        {
            MapPOIManager.Remove(GroupMarkerId);
        }
        catch (Exception ex)
        {
            VisitorLog.Instance.Debug($"Removing the group map marker threw ({Describe.Of(ex)}).");
        }
    }

    private static void Send(Visit visit, string text)
    {
        LastMessage = text;

        if (!CustomerSettings.AnnounceArrivals)
            return;

        // The message is networked, so only the host sends it; a guest receives the host's copy and
        // sending its own would put the same text on every phone twice.
        if (!HostGate.IsAuthority)
            return;

        try
        {
            var leader = VisitorRuntime.Resolve(visit.Leader);
            if (leader is null)
            {
                VisitorLog.Instance.Warn("The group leader is not in the world, so their message could not be sent.");
                return;
            }

            leader.SendTextMessage(text);
        }
        catch (Exception ex)
        {
            VisitorLog.Instance.Warn($"Sending the group's message failed ({Describe.Of(ex)}); the map marker still shows where they are.");
        }
    }

    private static void ShowMarker(Visit visit)
    {
        if (!CustomerSettings.AnnounceArrivals)
            return;

        try
        {
            MapPOIManager.Remove(GroupMarkerId);

            new MapPOIBuilder(GroupMarkerId)
                .WithLabel(visit.Archetype.DisplayName)
                .WithPosition(visit.StandPoint)
                .WithTextVisibility(MapPOITextVisibility.Always)
                .WithVisibility(true)
                .Build();
        }
        catch (Exception ex)
        {
            VisitorLog.Instance.Warn($"The group map marker could not be created ({Describe.Of(ex)}).");
        }
    }

    /// <summary>The shipped "potential customer" pin, one per member, for free.</summary>
    private static void SetMemberPins(Visit visit, bool enabled)
    {
        foreach (var slot in visit.Members)
        {
            var npc = GameNpc.Resolve(slot.Id, out _);
            npc?.SetPotentialCustomerPoiEnabled(enabled, out _);
        }
    }
}

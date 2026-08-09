using Expansions.SpecialCustomers.Archetypes;
using Expansions.SpecialCustomers.Persistence;
using Expansions.SpecialCustomers.Visitors;
using S1API.Map;
using UnityEngine;

namespace Expansions.SpecialCustomers.Visits;

/// <summary>One group's stay in town: who, where, when, and how far through it they are.</summary>
internal sealed class Visit
{
    private Visit(Archetype archetype, Region region, string locationGuid, string locationName, Vector3 standPoint, IReadOnlyList<int> memberSlots, int leaderSlot)
    {
        Archetype = archetype;
        Region = region;
        DeliveryLocationGuid = locationGuid;
        DeliveryLocationName = locationName;
        StandPoint = standPoint;
        MemberSlots = memberSlots;
        LeaderSlot = leaderSlot;
    }

    internal Archetype Archetype { get; }

    internal Region Region { get; }

    internal string DeliveryLocationGuid { get; }

    internal string DeliveryLocationName { get; }

    internal Vector3 StandPoint { get; }

    internal IReadOnlyList<int> MemberSlots { get; }

    internal int LeaderSlot { get; }

    internal int ArrivalDay { get; private set; }

    internal int ArrivalTime { get; private set; }

    internal int DepartureDay { get; private set; }

    internal int DepartureTime { get; private set; }

    internal bool OfferSent { get; set; }

    /// <summary>Bounded so a leader the game will never accept an offer from is not retried hourly.</summary>
    internal int OfferAttempts { get; set; }

    /// <summary>
    /// Seeds every member's appearance. Fixed for the whole visit and written to the save, so the
    /// group is the same six faces across a reload and a different six next time they come.
    /// </summary>
    internal int AppearanceSeed { get; private set; }

    internal long DepartureStamp => GameClock.Stamp(DepartureDay, DepartureTime);

    internal long OrderStamp => GameClock.Stamp(ArrivalDay, OrderTime);

    /// <summary>
    /// The archetype's own order time, unless it would fall before the group has even arrived — in
    /// which case they order an hour after arriving rather than never.
    /// </summary>
    internal int OrderTime =>
        GameClock.MinutesOfDay(Archetype.OrderTime) > GameClock.MinutesOfDay(ArrivalTime)
            ? Archetype.OrderTime
            : GameClock.AddHours(ArrivalTime, 1);

    internal VisitorSlot Leader => VisitorSlot.Find(LeaderSlot) ?? VisitorSlot.Primary;

    internal IEnumerable<VisitorSlot> Members
    {
        get
        {
            foreach (var index in MemberSlots)
            {
                var slot = VisitorSlot.Find(index);
                if (slot is not null)
                    yield return slot;
            }
        }
    }

    internal static Visit Begin(
        Archetype archetype,
        Region region,
        string locationGuid,
        string locationName,
        Vector3 standPoint,
        IReadOnlyList<int> memberSlots,
        int leaderSlot,
        int arrivalDay,
        int arrivalTime,
        int departureTime,
        int appearanceSeed)
    {
        var visit = new Visit(archetype, region, locationGuid, locationName, standPoint, memberSlots, leaderSlot)
        {
            ArrivalDay = arrivalDay,
            ArrivalTime = arrivalTime,
            DepartureTime = departureTime,
            AppearanceSeed = NonZero(appearanceSeed, arrivalDay),
        };

        // A departure time later in the day than the arrival is the same day; anything earlier —
        // which the 04:00 default always is — is the following morning.
        visit.DepartureDay = GameClock.MinutesOfDay(departureTime) > GameClock.MinutesOfDay(arrivalTime)
            ? arrivalDay
            : arrivalDay + 1;

        return visit;
    }

    internal static Visit? FromSave(ActiveVisitData data)
    {
        var archetype = ArchetypeCatalog.Find(data.ArchetypeId);
        if (archetype is null)
            return null;

        var slots = data.MemberSlots?.Where(index => VisitorSlot.Find(index) is not null).ToArray() ?? Array.Empty<int>();
        if (slots.Length == 0)
            return null;

        var location = ResolveLocation(data.DeliveryLocationGuid);
        var standPoint = location is null ? Vector3.zero : WorldGeographyStandPoint(location);

        return new Visit(
            archetype,
            (Region)Math.Clamp(data.Region, 0, 5),
            data.DeliveryLocationGuid ?? string.Empty,
            location?.Name ?? "an agreed spot",
            standPoint,
            slots,
            slots.Contains(data.LeaderSlot) ? data.LeaderSlot : slots[0])
        {
            ArrivalDay = data.ArrivalDay,
            ArrivalTime = data.ArrivalTime,
            DepartureDay = data.DepartureDay,
            DepartureTime = data.DepartureTime,
            OfferSent = data.OfferSent,
            OfferAttempts = data.OfferAttempts,
            AppearanceSeed = NonZero(data.AppearanceSeed, data.ArrivalDay),
        };
    }

    /// <summary>
    /// A save written before appearance randomisation carries no seed. Deriving one from the arrival
    /// day keeps that group stable for the rest of its stay rather than re-rolling every reload.
    /// </summary>
    private static int NonZero(int seed, int arrivalDay) =>
        seed != 0 ? seed : unchecked(((arrivalDay * -1640531527) ^ 0x5f37) | 1);

    internal ActiveVisitData ToSave() => new()
    {
        ArchetypeId = Archetype.Id,
        Region = (int)Region,
        DeliveryLocationGuid = DeliveryLocationGuid,
        MemberSlots = MemberSlots.ToArray(),
        LeaderSlot = LeaderSlot,
        ArrivalDay = ArrivalDay,
        ArrivalTime = ArrivalTime,
        DepartureDay = DepartureDay,
        DepartureTime = DepartureTime,
        OfferSent = OfferSent,
        OfferAttempts = OfferAttempts,
        AppearanceSeed = AppearanceSeed,
    };

    private static DeliveryLocation? ResolveLocation(string? guid)
    {
        if (string.IsNullOrEmpty(guid))
            return null;

        try
        {
            return DeliveryLocation.GetByGuid(guid);
        }
        catch
        {
            return null;
        }
    }

    private static Vector3 WorldGeographyStandPoint(DeliveryLocation location) =>
        Game.WorldGeography.StandPointOf(location) ?? Vector3.zero;
}

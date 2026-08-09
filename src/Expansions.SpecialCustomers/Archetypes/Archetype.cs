using Expansions.SpecialCustomers.Configuration;
using S1API.Economy;
using S1API.Map;
using S1API.Products;

namespace Expansions.SpecialCustomers.Archetypes;

/// <summary>
/// One visiting group's identity, taste and economics.
/// <para>
/// Code-defined rather than data-driven on purpose: co-op peers have to agree on the roster
/// byte-for-byte, and a data pack turns that into a support problem. Config exposes tuning —
/// toggles, multipliers, the two inferred drug preferences — never structure.
/// </para>
/// </summary>
internal sealed class Archetype
{
    internal Archetype(
        string id,
        string displayName,
        string shortName,
        int defaultMemberCount,
        int orderTime,
        CustomerStandard standards,
        DrugType[] preferredDrugs,
        float priceMultiplier,
        int quantityMin,
        int quantityMax,
        (Region Region, int Weight)[] regionWeights,
        string voiceId,
        float voicePitch,
        float walkSpeed,
        float clusterRadius,
        float aggressiveness,
        string arrivalMessage,
        string departureMessage,
        string offerMessage,
        Wardrobe wardrobe)
    {
        Id = id;
        DisplayName = displayName;
        ShortName = shortName;
        DefaultMemberCount = defaultMemberCount;
        OrderTime = orderTime;
        Standards = standards;
        PreferredDrugs = preferredDrugs;
        PriceMultiplier = priceMultiplier;
        QuantityMin = quantityMin;
        QuantityMax = quantityMax;
        RegionWeights = regionWeights;
        VoiceId = voiceId;
        VoicePitch = voicePitch;
        WalkSpeed = walkSpeed;
        ClusterRadius = clusterRadius;
        Aggressiveness = aggressiveness;
        ArrivalMessage = arrivalMessage;
        DepartureMessage = departureMessage;
        OfferMessage = offerMessage;
        Wardrobe = wardrobe;
    }

    internal string Id { get; }

    /// <summary>The name the player sees on the phone and the map marker.</summary>
    internal string DisplayName { get; }

    /// <summary>Two words for the menu and the status line.</summary>
    internal string ShortName { get; }

    internal int DefaultMemberCount { get; }

    /// <summary>HHMM. When the leader sends the bulk offer.</summary>
    internal int OrderTime { get; }

    internal CustomerStandard Standards { get; }

    internal DrugType[] PreferredDrugs { get; }

    /// <summary>0.80-0.92. The below-market rate the group pays for the convenience of bulk.</summary>
    internal float PriceMultiplier { get; }

    internal int QuantityMin { get; }

    internal int QuantityMax { get; }

    internal (Region Region, int Weight)[] RegionWeights { get; }

    internal string VoiceId { get; }

    internal float VoicePitch { get; }

    internal float WalkSpeed { get; }

    /// <summary>Metres. How tightly the members stand around the delivery location.</summary>
    internal float ClusterRadius { get; }

    internal float Aggressiveness { get; }

    /// <summary><c>{0}</c> is the region name.</summary>
    internal string ArrivalMessage { get; }

    internal string DepartureMessage { get; }

    /// <summary><c>{0}</c> quantity, <c>{1}</c> product, <c>{2}</c> payment.</summary>
    internal string OfferMessage { get; }

    /// <summary>The parts pool every member of this group is randomised out of.</summary>
    internal Wardrobe Wardrobe { get; }

    internal bool IsEnabled => CustomerSettings.IsArchetypeEnabled(Id);

    /// <summary>
    /// One member's appearance. <paramref name="visitSeed"/> is persisted with the visit, so the
    /// same slot rebuilds the same person across a save/load and a different one next visit.
    /// </summary>
    internal ArchetypeLook BuildLook(int slotIndex, int visitSeed) =>
        VisitorLookFactory.Build(this, slotIndex, visitSeed);

    /// <summary>
    /// The archetype's drugs, with the config override applied for the two whose preference is
    /// inferred rather than sourced. An override that parses to nothing is ignored, not obeyed —
    /// a group that wants no drug at all can never place an order.
    /// </summary>
    internal DrugType[] EffectiveDrugs()
    {
        var raw = CustomerSettings.DrugOverrideFor(Id);
        if (raw.Length == 0)
            return PreferredDrugs;

        var parsed = new List<DrugType>();
        foreach (var token in raw.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (Enum.TryParse<DrugType>(token.Trim(), ignoreCase: true, out var drug) && !parsed.Contains(drug))
                parsed.Add(drug);
        }

        return parsed.Count > 0 ? parsed.ToArray() : PreferredDrugs;
    }

    /// <summary>Lowest quality this group will accept, from its standards.</summary>
    internal Quality MinimumQuality => Standards switch
    {
        CustomerStandard.VeryLow => Quality.Poor,
        CustomerStandard.Low => Quality.Standard,
        CustomerStandard.Moderate => Quality.Standard,
        CustomerStandard.High => Quality.Premium,
        _ => Quality.Heavenly,
    };
}

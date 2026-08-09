namespace Expansions.SpecialCustomers.Persistence;

/// <summary>
/// Everything the module needs to survive a save/load, and nothing else.
/// <para>
/// Contracts, relationships, addiction, customer unlock state and NPC positions are deliberately
/// absent: all of those are the game's own save data, written by <c>Customer</c>, <c>Contract</c>
/// and <c>NPCRelationData</c>. Only the group layer is ours. That is what makes a self-disable
/// harmless — the file stops being read and nothing else in the save carries mod-authored values.
/// </para>
/// <para>
/// Public with plain fields, because this is Newtonsoft-serialised by S1API into
/// <c>&lt;save&gt;/Modded/Saveables/</c>, a subtree vanilla never opens — and a serializer that
/// cannot see the type writes an empty object without complaining.
/// </para>
/// </summary>
public sealed class VisitStateData
{
    /// <summary>Bump on any shape change. A payload from the future is ignored, never half-read.</summary>
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion = CurrentSchemaVersion;

    /// <summary>Absolute elapsed day, not a countdown — a time skip must not be able to lose it.</summary>
    public int NextVisitDay;

    /// <summary>So the same group never turns up twice running.</summary>
    public string LastArchetypeId = string.Empty;

    public bool SelfDisabledByDetection;

    public string SelfDisableReason = string.Empty;

    public ActiveVisitData? Active;
}

/// <summary>The group currently in town, or null.</summary>
public sealed class ActiveVisitData
{
    public string ArchetypeId = string.Empty;

    /// <summary>Ordinal of <c>S1API.Map.Region</c>, stored as an int so the enum can move.</summary>
    public int Region;

    public string DeliveryLocationGuid = string.Empty;

    public int[] MemberSlots = Array.Empty<int>();

    public int LeaderSlot;

    public int ArrivalDay;

    public int ArrivalTime;

    public int DepartureDay;

    public int DepartureTime;

    public bool OfferSent;

    /// <summary>
    /// How many times the bulk offer has been attempted this visit. Persisted so a save reloaded
    /// mid-visit does not restart a retry loop the player already watched fail.
    /// </summary>
    public int OfferAttempts;
}

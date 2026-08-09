using Expansions.Core.Diagnostics;
using Expansions.SpecialCustomers.Archetypes;
using Expansions.SpecialCustomers.Configuration;
using Expansions.SpecialCustomers.Game;
using Expansions.SpecialCustomers.Visitors;

namespace Expansions.SpecialCustomers.Visits;

/// <summary>
/// Writes a visiting group's taste and appetite into the live <c>CustomerData</c>, and puts the
/// prefab baseline back when they leave.
/// <para>
/// The snapshot is what makes a self-disable safe: after departure no shipped economy object carries
/// a mod-authored value, so a save written with the mod and loaded without it behaves like a save
/// that never had it.
/// </para>
/// </summary>
internal static class CustomerTuner
{
    /// <summary>Every field the tuner is allowed to touch. Snapshotting reads exactly this list.</summary>
    private static readonly string[] TunedFields =
    {
        "MinWeeklySpend",
        "MaxWeeklySpend",
        "MinOrdersPerWeek",
        "MaxOrdersPerWeek",
        "OrderTime",
        "Standards",
        "CanBeDirectlyApproached",
        "GuaranteeFirstSampleSuccess",
        "CallPoliceChance",
        "BaseAddiction",
        "DependenceMultiplier",
        "MinMutualRelationRequirement",
        "MaxMutualRelationRequirement",
    };

    private static readonly Dictionary<int, Dictionary<string, object?>> Baselines = new();
    private static readonly Dictionary<int, float> RelationshipBaselines = new();

    /// <summary>Session-scoped, not visit-scoped: the shipped dialogue setup appends, so it runs once.</summary>
    private static readonly HashSet<int> DialogueRefreshed = new();

    private static readonly object Gate = new();

    internal static int SnapshotCount
    {
        get
        {
            lock (Gate)
                return Baselines.Count;
        }
    }

    internal static bool Apply(VisitorSlot slot, Archetype archetype, bool isLeader, float budget, out string failure)
    {
        var npc = GameNpc.Resolve(slot.Id, out failure);
        if (npc is null)
            return false;

        var data = npc.CustomerData(out failure);
        if (data is null)
            return false;

        Snapshot(slot, data);
        SnapshotRelationship(slot);

        // Generous but finite: the shipped counter-offer maths reads the weekly spend, and a group
        // whose budget is zero would reject its own contract.
        var spend = Math.Max(budget * 2f, 1000f);

        Write(data, "MinWeeklySpend", spend);
        Write(data, "MaxWeeklySpend", spend);

        // Zero either way. The mod authors every offer by hand; a visitor that also generated its
        // own orders would put deals on the phone that nobody is in town to fulfil.
        Write(data, "MinOrdersPerWeek", 0);
        Write(data, "MaxOrdersPerWeek", 0);

        Write(data, "OrderTime", archetype.OrderTime);
        WriteStandards(data, archetype);

        // Every member is approachable, leader included: the leader's phone contract is the headline
        // sale, but a group you cannot do anything with face to face reads as broken, and the
        // shipped street-deal path costs nothing to leave open.
        Write(data, "CanBeDirectlyApproached", CustomerSettings.AllowWalkUpSales);
        Write(data, "GuaranteeFirstSampleSuccess", false);

        // A bulk buyer who calls the police is pure frustration, and it matches shipped Cranky Frank.
        Write(data, "CallPoliceChance", 0f);

        // They travel. Nothing about a visit should leave a permanent addicted customer behind.
        Write(data, "BaseAddiction", 0f);
        Write(data, "DependenceMultiplier", 0f);
        Write(data, "MinMutualRelationRequirement", 0f);
        Write(data, "MaxMutualRelationRequirement", 0f);

        Unlock(slot);
        RefreshDialogue(slot);

        failure = string.Empty;
        return true;
    }

    /// <summary>Puts every snapshotted field back. Missing snapshot means it was never tuned.</summary>
    internal static bool Restore(VisitorSlot slot, out string failure)
    {
        Dictionary<string, object?>? baseline;
        lock (Gate)
        {
            if (!Baselines.TryGetValue(slot.Index, out baseline))
            {
                failure = string.Empty;
                return true;
            }
        }

        var npc = GameNpc.Resolve(slot.Id, out failure);
        if (npc is null)
            return false;

        var data = npc.CustomerData(out failure);
        if (data is null)
            return false;

        foreach (var pair in baseline!)
            Write(data, pair.Key, pair.Value);

        RestoreRelationship(slot);

        lock (Gate)
        {
            Baselines.Remove(slot.Index);
            RelationshipBaselines.Remove(slot.Index);
        }

        failure = string.Empty;
        return true;
    }

    internal static void Forget()
    {
        lock (Gate)
        {
            Baselines.Clear();
            RelationshipBaselines.Clear();
        }
    }

    /// <summary>
    /// A new scene means new <c>Customer</c> components, so the once-per-session dialogue rebuild has
    /// to be armed again. Kept out of <see cref="Forget"/>, which runs on every unwind and would
    /// otherwise let a disable/enable cycle duplicate the customer's choices.
    /// </summary>
    internal static void ForgetDialogueSetup()
    {
        lock (Gate)
            DialogueRefreshed.Clear();
    }

    private static void Snapshot(VisitorSlot slot, object data)
    {
        lock (Gate)
        {
            if (Baselines.ContainsKey(slot.Index))
                return;
        }

        var baseline = new Dictionary<string, object?>(TunedFields.Length, StringComparer.Ordinal);
        foreach (var field in TunedFields)
        {
            if (GameReflection.TryRead(data, field, out var value, out _))
                baseline[field] = value;
        }

        lock (Gate)
            Baselines[slot.Index] = baseline;
    }

    /// <summary>
    /// A group that visits ten times must not slowly become a maxed-out permanent customer — that
    /// would contradict "periodically visit town" — so whatever the deals move is rolled back.
    /// </summary>
    private static void SnapshotRelationship(VisitorSlot slot)
    {
        lock (Gate)
        {
            if (RelationshipBaselines.ContainsKey(slot.Index))
                return;
        }

        try
        {
            var npc = VisitorRuntime.Resolve(slot);
            if (npc is null)
                return;

            lock (Gate)
                RelationshipBaselines[slot.Index] = npc.Relationship.Delta;
        }
        catch (Exception ex)
        {
            VisitorLog.Instance.Debug($"Could not read the relationship for slot {slot.Index:00} ({Describe.Of(ex)}).");
        }
    }

    private static void RestoreRelationship(VisitorSlot slot)
    {
        float baseline;
        lock (Gate)
        {
            if (!RelationshipBaselines.TryGetValue(slot.Index, out baseline))
                return;
        }

        try
        {
            var npc = VisitorRuntime.Resolve(slot);
            if (npc is null)
                return;

            var drift = npc.Relationship.Delta - baseline;
            if (Math.Abs(drift) > 0.01f)
                npc.Relationship.Add(-drift);
        }
        catch (Exception ex)
        {
            VisitorLog.Instance.Debug($"Could not reset the relationship for slot {slot.Index:00} ({Describe.Of(ex)}).");
        }
    }

    /// <summary>
    /// A locked customer cannot be offered a contract, so every member is unlocked on arrival. There
    /// is no shipped inverse, so they stay in the player's contacts afterwards — which reads as the
    /// group being remembered rather than as a leak.
    /// </summary>
    private static void Unlock(VisitorSlot slot)
    {
        try
        {
            VisitorRuntime.Resolve(slot)?.Customer.Unlock();
        }
        catch (Exception ex)
        {
            VisitorLog.Instance.Debug($"Unlocking slot {slot.Index:00} as a customer threw ({Describe.Of(ex)}).");
        }
    }

    /// <summary>
    /// Re-runs the shipped <c>Customer.SetUpDialogue</c>, which builds the sample / deal / complete-
    /// contract choices from <c>CustomerData</c>.
    /// <para>
    /// It normally runs once during <c>Customer.Start</c>, long before a visit writes the values it
    /// reads, so a visitor that was zeroed at prefab time gets a customer menu with nothing in it.
    /// Once per visitor per session: the shipped call appends rather than replaces, so a second run
    /// would show every choice twice.
    /// </para>
    /// </summary>
    private static void RefreshDialogue(VisitorSlot slot)
    {
        lock (Gate)
        {
            if (!DialogueRefreshed.Add(slot.Index))
                return;
        }

        var link = CustomerLink.Resolve(slot.Id, out var failure);
        if (link is null)
        {
            VisitorLog.Instance.Debug($"Could not refresh customer dialogue for slot {slot.Index:00}: {failure}");
            return;
        }

        if (!link.SetUpDialogue(out var setupFailure))
            VisitorLog.Instance.Debug($"Customer.SetUpDialogue failed for slot {slot.Index:00}: {setupFailure}");
    }

    private static void WriteStandards(object data, Archetype archetype)
    {
        var value = GameTypes.ToEnum(GameTypes.CustomerStandard, (int)archetype.Standards);
        if (value is null)
        {
            VisitorLog.Instance.Debug($"'{GameTypes.CustomerStandard}' not found; standards left at the prefab default.");
            return;
        }

        Write(data, "Standards", value);
    }

    private static void Write(object data, string member, object? value)
    {
        try
        {
            var property = data.GetType().GetProperty(member);
            if (property is null || !property.CanWrite)
            {
                VisitorLog.Instance.Debug($"CustomerData.{member} is not writable on this build; skipping it.");
                return;
            }

            property.SetValue(data, value);
        }
        catch (Exception ex)
        {
            VisitorLog.Instance.Debug($"Writing CustomerData.{member} failed ({Describe.Of(ex)}).");
        }
    }
}

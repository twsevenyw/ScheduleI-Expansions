using Expansions.SpecialCustomers.Game;
using Expansions.SpecialCustomers.Visitors;
using UnityObject = UnityEngine.Object;

namespace Expansions.SpecialCustomers.Archetypes;

/// <summary>
/// Turns a neutral pool member into a biker and back again.
/// <para>
/// Both looks are cached <c>AvatarSettings</c> assets applied wholesale, so re-dressing the same
/// slot for a fifth visit costs one call and cannot accumulate layers. The neutral snapshot is taken
/// the first time a slot is ever dressed, which is the only moment the mod is certain the avatar is
/// still exactly what the prefab described.
/// </para>
/// </summary>
internal static class VisitorDresser
{
    private static readonly Dictionary<string, UnityObject> Looks = new(StringComparer.Ordinal);
    private static readonly Dictionary<int, UnityObject> Neutral = new();
    private static readonly object Gate = new();

    /// <summary>Slots dressed at least once this session, for the probe.</summary>
    internal static IReadOnlyCollection<int> DressedSlots
    {
        get
        {
            lock (Gate)
                return Neutral.Keys.ToArray();
        }
    }

    internal static int CachedLookCount
    {
        get
        {
            lock (Gate)
                return Looks.Count;
        }
    }

    internal static bool Apply(VisitorSlot slot, Archetype archetype, int memberIndex, out string failure)
    {
        var npc = GameNpc.Resolve(slot.Id, out failure);
        if (npc is null)
            return false;

        var donor = npc.CurrentAvatarSettings;
        if (donor is null)
        {
            failure = "the visitor has no live avatar settings to clone from";
            return false;
        }

        RememberNeutral(slot, donor);

        var look = Resolve(slot, archetype, memberIndex, donor, out failure);
        if (look is null)
            return false;

        if (!npc.LoadAvatarSettings(look, out failure))
            return false;

        ApplyProp(slot, archetype);
        failure = string.Empty;
        return true;
    }

    /// <summary>Puts the neutral civilian look back. Silent when the slot was never dressed.</summary>
    internal static bool Restore(VisitorSlot slot, out string failure)
    {
        UnityObject? neutral;
        lock (Gate)
        {
            if (!Neutral.TryGetValue(slot.Index, out neutral) || neutral == null)
            {
                failure = string.Empty;
                return true;
            }
        }

        var npc = GameNpc.Resolve(slot.Id, out failure);
        if (npc is null)
            return false;

        ClearProp(slot);
        return npc.LoadAvatarSettings(neutral, out failure);
    }

    /// <summary>Destroys every cached settings asset. Registered with the module's lifetime.</summary>
    internal static void Clear()
    {
        List<UnityObject> assets;

        lock (Gate)
        {
            assets = Looks.Values.Concat(Neutral.Values).ToList();
            Looks.Clear();
            Neutral.Clear();
        }

        foreach (var asset in assets)
        {
            try
            {
                if (asset != null)
                    UnityObject.Destroy(asset);
            }
            catch
            {
                // A dead native side is exactly what we wanted anyway.
            }
        }
    }

    private static UnityObject? Resolve(VisitorSlot slot, Archetype archetype, int memberIndex, object donor, out string failure)
    {
        var key = $"{archetype.Id}:{slot.Index}";

        lock (Gate)
        {
            if (Looks.TryGetValue(key, out var cached) && cached != null)
            {
                failure = string.Empty;
                return cached;
            }
        }

        var clone = AvatarSettingsFactory.Clone(donor, out failure);
        if (clone is null)
            return null;

        clone.name = $"SC_{archetype.Id}_{slot.Index:00}";

        var look = archetype.BuildLook(memberIndex);
        var dropped = look.Trim();
        if (dropped > 0)
        {
            VisitorLog.Instance.Warn(
                $"The {archetype.ShortName} recipe for member {memberIndex} overflowed the avatar slot budget; " +
                $"{dropped} layer(s) were dropped.");
        }

        if (!AvatarSettingsFactory.Apply(clone, look, out var applyFailure))
        {
            // Partial application still beats leaving them in neutral grey, so this is a warning and
            // the clone is kept: the group is the point, and a missing belt is not worth losing it.
            VisitorLog.Instance.Warn(
                $"Parts of the {archetype.ShortName} look could not be written for slot {slot.Index:00} ({applyFailure}).");
        }

        lock (Gate)
            Looks[key] = clone;

        failure = string.Empty;
        return clone;
    }

    private static void RememberNeutral(VisitorSlot slot, object donor)
    {
        lock (Gate)
        {
            if (Neutral.TryGetValue(slot.Index, out var existing) && existing != null)
                return;
        }

        var snapshot = AvatarSettingsFactory.Clone(donor, out var failure);
        if (snapshot is null)
        {
            VisitorLog.Instance.Warn(
                $"Could not snapshot the neutral look for slot {slot.Index:00} ({failure}); it will stay dressed after the group leaves.");
            return;
        }

        snapshot.name = $"SC_neutral_{slot.Index:00}";

        lock (Gate)
            Neutral[slot.Index] = snapshot;
    }

    private static void ApplyProp(VisitorSlot slot, Archetype archetype)
    {
        if (string.IsNullOrEmpty(archetype.EquippablePath))
            return;

        try
        {
            VisitorRuntime.Resolve(slot)?.SetEquippable(archetype.EquippablePath);
        }
        catch (Exception ex)
        {
            VisitorLog.Instance.Debug($"Equippable '{archetype.EquippablePath}' was rejected ({Describe.Of(ex)}).");
        }
    }

    private static void ClearProp(VisitorSlot slot)
    {
        try
        {
            VisitorRuntime.Resolve(slot)?.SetEquippable(string.Empty);
        }
        catch (Exception ex)
        {
            VisitorLog.Instance.Debug($"Could not clear the held prop for slot {slot.Index:00} ({Describe.Of(ex)}).");
        }
    }
}

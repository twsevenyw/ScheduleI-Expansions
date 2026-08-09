using Expansions.SpecialCustomers.Game;
using Expansions.SpecialCustomers.Visitors;
using UnityObject = UnityEngine.Object;

namespace Expansions.SpecialCustomers.Archetypes;

/// <summary>
/// Turns a neutral pool member into one specific biker, and back again.
/// <para>
/// Both looks are cached <c>AvatarSettings</c> assets applied wholesale, so re-dressing the same
/// slot for a fifth visit costs one call and cannot accumulate layers. The neutral snapshot is taken
/// the first time a slot is ever dressed, which is the only moment the mod is certain the avatar is
/// still exactly what the prefab described.
/// </para>
/// <para>
/// The cache is keyed on the visit as well as the slot, because the point of the visit seed is that
/// the same slot is a different person next time. Looks built for an older visit are destroyed as
/// soon as a new seed shows up: each one is a live <c>ScriptableObject</c> flagged
/// <c>HideAndDontSave</c>, so nothing else would ever collect them.
/// </para>
/// </summary>
internal static class VisitorDresser
{
    private static readonly Dictionary<int, Dressed> Current = new();
    private static readonly Dictionary<int, UnityObject> Neutral = new();
    private static readonly object Gate = new();

    private static int _cachedSeed;

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
                return Current.Count;
        }
    }

    /// <summary>Per-slot look fingerprints for the current visit, so the probe can prove they differ.</summary>
    internal static IReadOnlyDictionary<int, string> CurrentSignatures
    {
        get
        {
            lock (Gate)
                return Current.ToDictionary(pair => pair.Key, pair => pair.Value.Signature);
        }
    }

    internal static bool Apply(VisitorSlot slot, Archetype archetype, int visitSeed, out string failure)
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

        var dressed = Resolve(slot, archetype, visitSeed, donor, out failure);
        if (dressed is null)
            return false;

        if (!npc.LoadAvatarSettings(dressed.Value.Settings, out failure))
            return false;

        SetProp(slot, dressed.Value.Prop);
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

        SetProp(slot, string.Empty);
        return npc.LoadAvatarSettings(neutral, out failure);
    }

    /// <summary>Destroys every cached settings asset. Registered with the module's lifetime.</summary>
    internal static void Clear()
    {
        List<UnityObject> assets;

        lock (Gate)
        {
            assets = Current.Values.Select(dressed => dressed.Settings).Concat(Neutral.Values).ToList();
            Current.Clear();
            Neutral.Clear();
            _cachedSeed = 0;
        }

        Destroy(assets);
    }

    private static Dressed? Resolve(VisitorSlot slot, Archetype archetype, int visitSeed, object donor, out string failure)
    {
        DropStaleLooks(visitSeed);

        lock (Gate)
        {
            if (Current.TryGetValue(slot.Index, out var cached) &&
                cached.Settings != null &&
                string.Equals(cached.ArchetypeId, archetype.Id, StringComparison.Ordinal))
            {
                failure = string.Empty;
                return cached;
            }
        }

        var clone = AvatarSettingsFactory.Clone(donor, out failure);
        if (clone is null)
            return null;

        clone.name = $"SC_{archetype.Id}_{slot.Index:00}_{visitSeed:x8}";

        var look = archetype.BuildLook(slot.Index, visitSeed);
        var dropped = look.Trim();
        if (dropped > 0)
        {
            VisitorLog.Instance.Warn(
                $"The {archetype.ShortName} look for slot {slot.Index:00} overflowed the avatar slot budget; " +
                $"{dropped} layer(s) were dropped.");
        }

        if (!AvatarSettingsFactory.Apply(clone, look, out var applyFailure))
        {
            // Partial application still beats leaving them in neutral grey, so this is a warning and
            // the clone is kept: the group is the point, and a missing belt is not worth losing it.
            VisitorLog.Instance.Warn(
                $"Parts of the {archetype.ShortName} look could not be written for slot {slot.Index:00} ({applyFailure}).");
        }

        var dressed = new Dressed(archetype.Id, clone, look.EquippablePath, look.Signature());

        lock (Gate)
            Current[slot.Index] = dressed;

        failure = string.Empty;
        return dressed;
    }

    private static void DropStaleLooks(int visitSeed)
    {
        List<UnityObject> stale;

        lock (Gate)
        {
            if (_cachedSeed == visitSeed)
                return;

            _cachedSeed = visitSeed;
            stale = Current.Values.Select(dressed => dressed.Settings).ToList();
            Current.Clear();
        }

        Destroy(stale);
    }

    private static void Destroy(IEnumerable<UnityObject> assets)
    {
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

    private static void SetProp(VisitorSlot slot, string path)
    {
        try
        {
            VisitorRuntime.Resolve(slot)?.SetEquippable(path);
        }
        catch (Exception ex)
        {
            var what = path.Length > 0 ? $"Equippable '{path}' was rejected" : "The held prop could not be cleared";
            VisitorLog.Instance.Debug($"{what} for slot {slot.Index:00} ({Describe.Of(ex)}).");
        }
    }

    private readonly struct Dressed
    {
        internal Dressed(string archetypeId, UnityObject settings, string prop, string signature)
        {
            ArchetypeId = archetypeId;
            Settings = settings;
            Prop = prop;
            Signature = signature;
        }

        internal string ArchetypeId { get; }

        internal UnityObject Settings { get; }

        internal string Prop { get; }

        internal string Signature { get; }
    }
}

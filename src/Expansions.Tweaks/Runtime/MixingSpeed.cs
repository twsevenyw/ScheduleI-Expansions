using Expansions.Core.Diagnostics;
using Object = UnityEngine.Object;

namespace Expansions.Tweaks.Runtime;

/// <summary>
/// Faster mixing, done by scaling the one number the whole mixing pipeline is derived from.
/// <para>
/// <c>MixingStation.MixTimePerItem</c> is a plain <c>int</c> instance field — real storage on every
/// station, not a <c>const</c> — and <c>GetMixTimeForCurrentOperation()</c> is
/// <c>MixTimePerItem × quantity</c>. Scaling the field therefore moves the mix duration, the
/// worldspace progress readout and the management app's estimate together, with no chance of the UI
/// and the timer disagreeing, and it works identically for a mix the player starts and one a chemist
/// starts because both read the same station.
/// </para>
/// <para>
/// The alternative — a postfix on <c>GetMixTimeForCurrentOperation()</c> — was rejected: IL2CPP
/// inlines a small getter into its callers, and a detour on a function nobody calls is a silent
/// no-op. A field write cannot be inlined away.
/// </para>
/// <para>
/// The field is a serialized prefab setting, not save data, so nothing here reaches a save file. It
/// still has to be restored on disable, because the value lives for the lifetime of the process.
/// </para>
/// </summary>
internal sealed class MixingSpeed
{
    private readonly Dictionary<int, Entry> _tracked = new();

    private float _multiplier = 1f;

    internal float Multiplier => _multiplier;

    internal int StationsScaled => _tracked.Count;

    internal bool TypeResolved => GameReflection.TypeExists(GameTypes.MixingStation);

    /// <summary>Scales every station that exists now. Idempotent: an already-tracked station is skipped.</summary>
    internal void Apply(float multiplier)
    {
        if (Math.Abs(multiplier - _multiplier) > 0.0001f)
        {
            Restore();
            _multiplier = multiplier;
        }

        if (IsVanilla)
            return;

        var type = GameReflection.FindType(GameTypes.MixingStation);
        if (type is null)
        {
            TweakLog.Warn($"'{GameTypes.MixingStation}' is not on this build; mixing is left vanilla.");
            return;
        }

        foreach (var (unity, typed) in Members.FindAll(type))
            Track(unity, typed);
    }

    /// <summary>Scales one station, from the Awake postfix or immediately before a mix starts.</summary>
    internal void Track(object? station)
    {
        if (IsVanilla || station is not Object unity)
            return;

        Track(unity, station);
    }

    /// <summary>
    /// How much to multiply a mix time by to get back to the vanilla figure for this station. Used by
    /// the chemist exclusion, which has to undo the scaling for one call rather than for the station.
    /// </summary>
    internal float VanillaRatio(object? station)
    {
        if (station is not Object unity)
            return 1f;

        return _tracked.TryGetValue(InstanceId(unity), out var entry) && entry.Scaled > 0
            ? entry.Original / (float)entry.Scaled
            : 1f;
    }

    internal void Restore()
    {
        foreach (var entry in _tracked.Values)
        {
            if (GameReflection.IsPresent(entry.Target))
                Members.Write(entry.Typed, "MixTimePerItem", entry.Original);
        }

        _tracked.Clear();
    }

    /// <summary>
    /// Drops the tracking without writing, for a scene teardown where the stations are already gone.
    /// Writing into a destroyed native object is the one way this could crash rather than fail.
    /// </summary>
    internal void Forget() => _tracked.Clear();

    /// <summary>One line per tracked station, for the probe. Capped: a big save has a lot of these.</summary>
    internal IReadOnlyList<IReadOnlyList<string>> Rows(int cap = 8)
    {
        var rows = new List<IReadOnlyList<string>>();

        foreach (var entry in _tracked.Values)
        {
            if (rows.Count >= cap)
                break;

            var live = GameReflection.IsPresent(entry.Target)
                ? Members.Read(entry.Typed, "MixTimePerItem", -1).ToString()
                : "destroyed";

            rows.Add(new[] { entry.Name, entry.Original.ToString(), entry.Scaled.ToString(), live });
        }

        return rows;
    }

    private bool IsVanilla => Math.Abs(_multiplier - 1f) < 0.0001f;

    private void Track(Object unity, object typed)
    {
        var id = InstanceId(unity);
        if (id == 0 || _tracked.ContainsKey(id))
            return;

        var original = Members.Read(typed, "MixTimePerItem", -1);
        if (original <= 0)
        {
            TweakLog.Detail($"Station '{Name(unity)}' reports MixTimePerItem {original}; leaving it alone.");
            return;
        }

        var scaled = Scale(original, _multiplier);
        if (scaled == original)
            return;

        if (!Members.Write(typed, "MixTimePerItem", scaled))
            return;

        _tracked[id] = new Entry(unity, typed, Name(unity), original, scaled);
        TweakLog.Detail($"Station '{Name(unity)}': mix time per item {original} -> {scaled}.");
    }

    /// <summary>At least one minute per item, so a large multiplier cannot round a mix down to instant.</summary>
    internal static int Scale(int vanilla, float multiplier) =>
        Math.Max(1, (int)Math.Round(vanilla / multiplier, MidpointRounding.AwayFromZero));

    private static int InstanceId(Object unity)
    {
        try
        {
            return unity.GetInstanceID();
        }
        catch
        {
            return 0;
        }
    }

    private static string Name(Object unity)
    {
        try
        {
            return unity.name;
        }
        catch
        {
            return "<station>";
        }
    }

    private readonly struct Entry
    {
        internal Entry(Object target, object typed, string name, int original, int scaled)
        {
            Target = target;
            Typed = typed;
            Name = name;
            Original = original;
            Scaled = scaled;
        }

        internal Object Target { get; }

        /// <summary>The same object seen through the wrapper that declares <c>MixTimePerItem</c>.</summary>
        internal object Typed { get; }

        internal string Name { get; }

        internal int Original { get; }

        internal int Scaled { get; }
    }
}

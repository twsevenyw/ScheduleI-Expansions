using Expansions.Core.Diagnostics;
using Expansions.PoliceOverhaul.State;
using Il2CppInterop.Runtime.InteropTypes;

namespace Expansions.PoliceOverhaul.Runtime;

/// <summary>
/// Makes officers notice you sooner and search you more often as heat rises.
/// <para>
/// Two paths, because only one of them is guaranteed. The universal vision scalars are real mutable
/// statics on this build and cover every NPC at once; the per-officer sweep writes plain instance
/// floats and therefore cannot have been const-inlined, so it is the path that always works. Both
/// snapshot before writing.
/// </para>
/// <para>
/// Writes are idempotent and keyed on the native pointer. Re-applying the same BodySearchChance /
/// RangeMultiplier every minute (or worse, every frame) fights the game's pursuit state machine —
/// officers stutter-walk and flip between investigating (?) and alerted (!). Apply once when an
/// officer enters management, again only when the target multipliers actually change, and never
/// while that officer is already engaged in pursuit/combat at high heat.
/// </para>
/// </summary>
internal sealed class DetectionTuner
{
    private readonly LawLevers _levers;
    private readonly Dictionary<IntPtr, ManagedOfficer> _officers = new();

    private HeatTier _appliedTier = (HeatTier)(-1);
    private float _appliedScalar = float.NaN;
    private float _appliedSearchMul = float.NaN;
    private float _appliedRangeMul = float.NaN;
    private float _appliedAttentiveness = float.NaN;
    private float _appliedMemory = float.NaN;

    internal DetectionTuner(LawLevers levers) => _levers = levers;

    internal int TrackedOfficers => _officers.Count;

    /// <summary>Session total of per-officer instance writes. Climbing every tick means the bug is back.</summary>
    internal int TotalOfficerWrites { get; private set; }

    /// <summary>Per-officer write counts this session, for the probe. Pointer → count.</summary>
    internal IReadOnlyDictionary<IntPtr, int> OfficerWriteCounts
    {
        get
        {
            var map = new Dictionary<IntPtr, int>(_officers.Count);
            foreach (var (pointer, managed) in _officers)
                map[pointer] = managed.WriteCount;
            return map;
        }
    }

    /// <summary>
    /// Applies the tier to every officer currently in the world. Called from the minute tick so it
    /// also picks up officers the game pooled in since last time. Cheap when nothing changed:
    /// already-managed officers with the same target values are skipped entirely.
    /// </summary>
    internal void Apply(HeatTier tier, float masterScalar, Func<object, bool>? skip = null)
    {
        // At TaskForce/Federal the pursuit SM is already aggressive. Cap attentiveness so we do not
        // keep re-lighting vision cones harder than memory can hold — that is the ?/! flicker.
        var attentiveness = BackedOffDetection(tier, masterScalar);
        var memory = HeatModel.MemoryMultiplier(tier, masterScalar);
        var searchMultiplier = HeatModel.BodySearchMultiplier(tier, masterScalar);
        var rangeMultiplier = HeatModel.DetectionMultiplier(tier, masterScalar);

        var globalsChanged =
            tier != _appliedTier ||
            Math.Abs(masterScalar - _appliedScalar) > 0.0001f ||
            Math.Abs(attentiveness - _appliedAttentiveness) > 0.0001f ||
            Math.Abs(memory - _appliedMemory) > 0.0001f;

        if (globalsChanged)
        {
            _levers.Set(GameTypes.VisionCone, "UniversalAttentivenessScale", attentiveness);
            _levers.Set(GameTypes.VisionCone, "UniversalMemoryScale", memory);

            _appliedTier = tier;
            _appliedScalar = masterScalar;
            _appliedAttentiveness = attentiveness;
            _appliedMemory = memory;
        }

        var targetsChanged =
            Math.Abs(searchMultiplier - _appliedSearchMul) > 0.0001f ||
            Math.Abs(rangeMultiplier - _appliedRangeMul) > 0.0001f;

        if (targetsChanged)
        {
            _appliedSearchMul = searchMultiplier;
            _appliedRangeMul = rangeMultiplier;
        }

        var highHeat = tier >= HeatTier.Crackdown;

        foreach (var officer in Officers())
        {
            if (officer is not Il2CppObjectBase native || native.Pointer == IntPtr.Zero)
                continue;

            if (skip is not null && skip(officer))
                continue;

            if (!_officers.TryGetValue(native.Pointer, out var managed))
            {
                managed = new ManagedOfficer(OfficerSnapshot.Take(officer));
                _officers[native.Pointer] = managed;
            }

            var wantSearch = managed.Baseline.BodySearchChance is { } search
                ? Math.Clamp(search * searchMultiplier, 0f, 1f)
                : (float?)null;
            var wantRange = managed.Baseline.RangeMultiplier is { } range
                ? range * rangeMultiplier
                : (float?)null;

            // Already holding the values we want — no write.
            if (managed.HasApplied &&
                NearlyEqual(managed.AppliedSearch, wantSearch) &&
                NearlyEqual(managed.AppliedRange, wantRange))
            {
                continue;
            }

            // High heat + already chasing: leave the pursuit SM alone. First-time apply still lands
            // so a brand-new officer is not left at vanilla while everyone else is tuned.
            if (highHeat && managed.HasApplied && IsEngaged(officer))
                continue;

            var wrote = false;

            if (wantSearch is { } searchValue &&
                Members.TryWrite(officer, "BodySearchChance", searchValue))
            {
                managed.AppliedSearch = searchValue;
                wrote = true;
            }

            if (wantRange is { } rangeValue &&
                managed.Baseline.VisionCone is not null &&
                Members.TryWrite(managed.Baseline.VisionCone, "RangeMultiplier", rangeValue))
            {
                managed.AppliedRange = rangeValue;
                wrote = true;
            }

            if (!wrote)
                continue;

            managed.HasApplied = true;
            managed.WriteCount++;
            TotalOfficerWrites++;
        }
    }

    internal void Restore()
    {
        foreach (var managed in _officers.Values)
            managed.Baseline.Write();

        if (_officers.Count > 0)
            PoliceLog.Msg($"Restored vanilla detection settings on {_officers.Count} officer(s).");

        Forget();
        _levers.Restore();
    }

    internal void Forget()
    {
        _officers.Clear();
        _appliedTier = (HeatTier)(-1);
        _appliedScalar = float.NaN;
        _appliedSearchMul = float.NaN;
        _appliedRangeMul = float.NaN;
        _appliedAttentiveness = float.NaN;
        _appliedMemory = float.NaN;
        TotalOfficerWrites = 0;
    }

    /// <summary>
    /// Probe line: one row per managed officer with how many times we wrote its instance fields.
    /// A climbing number between probes while standing still is the oscillation bug returning.
    /// </summary>
    internal IReadOnlyList<string> ProbeLines(int limit = 24)
    {
        var lines = new List<string>(Math.Min(limit, _officers.Count));
        var n = 0;
        foreach (var (pointer, managed) in _officers)
        {
            if (n >= limit)
            {
                lines.Add("…");
                break;
            }

            lines.Add(
                $"ptr=0x{pointer.ToInt64():X} writes={managed.WriteCount} " +
                $"applied={(managed.HasApplied ? "yes" : "no")} " +
                $"search={managed.AppliedSearch?.ToString("0.###") ?? "-"} " +
                $"range={managed.AppliedRange?.ToString("0.###") ?? "-"}");
            n++;
        }

        return lines;
    }

    /// <summary>Every live officer, including pooled ones sitting inside the station.</summary>
    internal static IReadOnlyList<object?> Officers()
    {
        var type = GameReflection.FindType(GameTypes.PoliceOfficer);
        if (type is null)
            return Array.Empty<object?>();

        return GameReflection.TryReadStatic(type, "Officers", out var list, out _)
            ? GameReflection.Enumerate(list, 128)
            : Array.Empty<object?>();
    }

    /// <summary>
    /// Attentiveness at Federal used to outrun memory and bounce vision between suspicious and
    /// confirmed. Cap it at the TaskForce curve once heat is in arrest-on-sight territory.
    /// </summary>
    private static float BackedOffDetection(HeatTier tier, float masterScalar)
    {
        var effective = tier >= HeatTier.TaskForce ? HeatTier.TaskForce : tier;
        return HeatModel.DetectionMultiplier(effective, masterScalar);
    }

    /// <summary>
    /// Best-effort: is this officer already in a pursuit/combat behaviour? Safe outside Harmony —
    /// never call from a patch body. Missing members simply mean "not engaged".
    /// </summary>
    private static bool IsEngaged(object officer)
    {
        foreach (var path in new[] { "PursuitBehaviour", "CombatBehaviour", "VehiclePursuitBehaviour" })
        {
            var behaviour = Members.ReadPath(officer, path);
            if (behaviour is null || !GameReflection.IsPresent(behaviour))
                continue;

            // Prefer explicit activity flags. Do not treat MonoBehaviour.enabled as engaged —
            // idle behaviours stay enabled and that would freeze tuning forever.
            if (Members.Read(behaviour, "IsActive", false) || Members.Read(behaviour, "Active", false))
                return true;
        }

        return false;
    }

    private static bool NearlyEqual(float? a, float? b)
    {
        if (a is null && b is null)
            return true;
        if (a is null || b is null)
            return false;
        return Math.Abs(a.Value - b.Value) <= 0.0001f;
    }

    private sealed class ManagedOfficer
    {
        internal ManagedOfficer(OfficerSnapshot baseline) => Baseline = baseline;

        internal OfficerSnapshot Baseline { get; }

        internal float? AppliedSearch { get; set; }

        internal float? AppliedRange { get; set; }

        internal bool HasApplied { get; set; }

        internal int WriteCount { get; set; }
    }

    private readonly struct OfficerSnapshot
    {
        private OfficerSnapshot(object officer, object? visionCone, float? bodySearchChance, float? rangeMultiplier)
        {
            Officer = officer;
            VisionCone = visionCone;
            BodySearchChance = bodySearchChance;
            RangeMultiplier = rangeMultiplier;
        }

        internal object Officer { get; }

        internal object? VisionCone { get; }

        internal float? BodySearchChance { get; }

        internal float? RangeMultiplier { get; }

        internal static OfficerSnapshot Take(object officer)
        {
            var cone = Members.ReadPath(officer, "Awareness.VisionCone");
            return new OfficerSnapshot(
                officer,
                cone,
                ReadFloat(officer, "BodySearchChance"),
                cone is null ? null : ReadFloat(cone, "RangeMultiplier"));
        }

        internal void Write()
        {
            if (BodySearchChance.HasValue)
                Members.TryWrite(Officer, "BodySearchChance", BodySearchChance.Value);

            if (RangeMultiplier.HasValue && VisionCone is not null)
                Members.TryWrite(VisionCone, "RangeMultiplier", RangeMultiplier.Value);
        }

        private static float? ReadFloat(object instance, string member) =>
            GameReflection.TryRead(instance, member, out var value, out _) && value is float number ? number : null;
    }
}

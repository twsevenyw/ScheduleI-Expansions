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
/// </summary>
internal sealed class DetectionTuner
{
    private readonly LawLevers _levers;
    private readonly Dictionary<IntPtr, OfficerSnapshot> _officers = new();

    private HeatTier _appliedTier = (HeatTier)(-1);
    private float _appliedScalar = float.NaN;

    internal DetectionTuner(LawLevers levers) => _levers = levers;

    internal int TrackedOfficers => _officers.Count;

    /// <summary>
    /// Applies the tier to every officer currently in the world. Called from the minute tick, so it
    /// also picks up officers the game pooled in since last time. Cheap: the list is a handful of
    /// entries and the writes are skipped when nothing changed.
    /// </summary>
    internal void Apply(HeatTier tier, float masterScalar, Func<object, bool>? skip = null)
    {
        var tierChanged = tier != _appliedTier || Math.Abs(masterScalar - _appliedScalar) > 0.0001f;

        if (tierChanged)
        {
            var attentiveness = HeatModel.DetectionMultiplier(tier, masterScalar);
            var memory = HeatModel.MemoryMultiplier(tier, masterScalar);

            _levers.Set(GameTypes.VisionCone, "UniversalAttentivenessScale", attentiveness);
            _levers.Set(GameTypes.VisionCone, "UniversalMemoryScale", memory);

            _appliedTier = tier;
            _appliedScalar = masterScalar;
        }

        var searchMultiplier = HeatModel.BodySearchMultiplier(tier, masterScalar);
        var rangeMultiplier = HeatModel.DetectionMultiplier(tier, masterScalar);

        foreach (var officer in Officers())
        {
            if (officer is not Il2CppObjectBase native || native.Pointer == IntPtr.Zero)
                continue;

            if (skip is not null && skip(officer))
                continue;

            if (!_officers.TryGetValue(native.Pointer, out var snapshot))
            {
                snapshot = OfficerSnapshot.Take(officer);
                _officers[native.Pointer] = snapshot;
            }

            if (snapshot.BodySearchChance.HasValue)
                Members.TryWrite(officer, "BodySearchChance", Math.Clamp(snapshot.BodySearchChance.Value * searchMultiplier, 0f, 1f));

            if (snapshot.RangeMultiplier.HasValue && snapshot.VisionCone is not null)
                Members.TryWrite(snapshot.VisionCone, "RangeMultiplier", snapshot.RangeMultiplier.Value * rangeMultiplier);
        }
    }

    internal void Restore()
    {
        foreach (var snapshot in _officers.Values)
            snapshot.Write();

        if (_officers.Count > 0)
            PoliceLog.Msg($"Restored vanilla detection settings on {_officers.Count} officer(s).");

        _officers.Clear();
        _appliedTier = (HeatTier)(-1);
        _appliedScalar = float.NaN;

        _levers.Restore();
    }

    internal void Forget()
    {
        _officers.Clear();
        _appliedTier = (HeatTier)(-1);
        _appliedScalar = float.NaN;
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

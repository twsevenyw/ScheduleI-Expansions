using Expansions.Core.Diagnostics;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace Expansions.PoliceOverhaul.Runtime;

/// <summary>
/// Federal team: temporary designation of the game's own shipped officers.
/// <para>
/// Hard rule: this class never creates, clones, re-identifies, or destroys an NPC. It relocates
/// living officers, tags their native pointers in a managed set, applies reversible instance buffs,
/// and restores those buffs when the event ends. Ownership answers are pointer-set lookups only.
/// </para>
/// </summary>
internal static class FederalAgents
{
    private static readonly HashSet<IntPtr> Tagged = new();
    private static readonly Dictionary<IntPtr, Vector3> Posts = new();
    private static readonly Dictionary<IntPtr, AgentSnapshot> Snapshots = new();

    internal static Viability Status { get; private set; } = Viability.Unknown;

    internal static int LiveCount => Tagged.Count;

    /// <summary>Managed designation set size. Zero means every ownership query is a no-op.</summary>
    internal static int OwnedCount => Tagged.Count;

    /// <summary>Raised once per officer the moment it is designated.</summary>
    internal static event Action? AgentActivated;

    /// <summary>
    /// True while this officer is on federal assignment. Pure managed pointer-set lookup —
    /// never reflects game members.
    /// </summary>
    internal static bool IsAgent(object? officer)
    {
        try
        {
            if (Tagged.Count == 0 || officer is not Il2CppObjectBase native)
                return false;

            var pointer = native.Pointer;
            return pointer != IntPtr.Zero && Tagged.Contains(pointer);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>True when at least one living shipped officer can be designated.</summary>
    internal static Viability Survey()
    {
        var officerType = GameReflection.FindType(GameTypes.PoliceOfficer);
        if (officerType is null)
        {
            Status = new Viability(false, "PoliceOfficer type is not on this build");
            return Status;
        }

        var living = 0;
        foreach (var officer in DetectionTuner.Officers())
        {
            if (IsLivingShipped(officer))
                living++;
        }

        if (living == 0)
        {
            Status = new Viability(false,
                "no living shipped officers to designate (normal outside a loaded save)");
            return Status;
        }

        Status = new Viability(true,
            $"{living} living shipped officer(s) available to designate — no cloning");
        return Status;
    }

    /// <summary>
    /// Relocate up to <paramref name="count"/> shipped officers, tag them as federal, and apply
    /// reversible buffs. Returns how many were designated. Never invents bodies.
    /// </summary>
    internal static int Designate(int count, Vector3 position, string targetPlayerCode, Vector3? postPosition = null)
    {
        if (!HostGate.IsAuthority || count <= 0)
            return 0;

        if (!Status.CanDesignate && !Survey().CanDesignate)
        {
            PoliceLog.Warn($"Federal designation unavailable: {Status.Reason}.");
            return 0;
        }

        var pursue = postPosition is null;
        var fielded = OfficerDeployment.Deploy(
            position,
            count,
            targetPlayer: null,
            pursue: false,
            holdPost: false,
            reason: postPosition is null ? "federal-designate-pursuit" : "federal-designate-stakeout");

        var designated = 0;
        foreach (var candidate in DetectionTuner.Officers())
        {
            if (designated >= count)
                break;

            if (candidate is null || !IsLivingShipped(candidate) || IsAgent(candidate))
                continue;

            var officer = candidate;
            var here = Components.TransformOf(officer)?.position;
            if (here is null)
                continue;

            if (Vector3.Distance(here.Value, position) > OfficerDeployment.SceneRadiusMetres)
                continue;

            if (!TryTag(officer, postPosition, out var pointer))
                continue;

            if (pursue && !string.IsNullOrEmpty(targetPlayerCode))
                Members.Invoke(officer, "BeginFootPursuit_Networked", targetPlayerCode, false);

            if (postPosition is { } post)
            {
                var offset = Quaternion.Euler(0f, designated * 48f, 0f) * Vector3.forward * (5f + designated * 0.75f);
                Posts[pointer] = post + offset;
                OfficerDeployment.Relocate(officer, post + offset, "federal-post");
            }

            designated++;
            RaiseActivated();
        }

        // Deploy may have left fewer in radius than wanted — pull more from farther candidates.
        if (designated < count)
        {
            foreach (var candidate in DetectionTuner.Officers())
            {
                if (designated >= count)
                    break;

                if (candidate is null || !IsLivingShipped(candidate) || IsAgent(candidate))
                    continue;

                var officer = candidate;
                var offset = Quaternion.Euler(0f, designated * 48f, 0f) * Vector3.forward * (5f + designated * 0.75f);
                var dest = (postPosition ?? position) + offset;
                if (!OfficerDeployment.Relocate(officer, dest, "federal-designate"))
                    continue;

                if (!TryTag(officer, postPosition is null ? null : dest, out _))
                    continue;

                if (pursue && !string.IsNullOrEmpty(targetPlayerCode))
                    Members.Invoke(officer, "BeginFootPursuit_Networked", targetPlayerCode, false);

                designated++;
                RaiseActivated();
            }
        }

        if (designated > 0)
        {
            PoliceLog.Msg(
                $"Designated {designated} shipped officer(s) as federal team" +
                (postPosition is null ? " (pursuit)." : " (stakeout).") +
                $" Deploy said: {OfficerDeployment.LastReport}");
        }
        else if (fielded <= 0)
        {
            PoliceLog.Warn(
                $"Federal designation fielded nobody. Deploy: {OfficerDeployment.LastReport}. Survey: {Status.Reason}");
        }

        return designated;
    }

    /// <summary>Walk posted agents back when they drift.</summary>
    internal static void HoldPosts()
    {
        if (Posts.Count == 0)
            return;

        foreach (var (pointer, post) in Posts)
        {
            var officer = FindByPointer(pointer);
            if (officer is null)
                continue;

            var here = Components.TransformOf(officer)?.position;
            if (here is null || (here.Value - post).sqrMagnitude < 9f)
                continue;

            var movement = Members.ReadPath(officer, "Movement");
            if (movement is not null)
                Members.Invoke(movement, "SetDestination", post, true, 1f);
        }
    }

    /// <summary>
    /// End of assignment: restore buffs and clear tags. Never destroys GameObjects.
    /// </summary>
    internal static void ReleaseAll()
    {
        foreach (var snapshot in Snapshots.Values)
            snapshot.Restore();

        var released = Tagged.Count;
        Tagged.Clear();
        Snapshots.Clear();
        Posts.Clear();

        if (released > 0)
            PoliceLog.Msg($"Released {released} federal designation(s) — officers restored, not destroyed.");
    }

    /// <summary>
    /// Scene teardown. Natives are already gone — drop managed tags without writing into them.
    /// </summary>
    internal static void Forget()
    {
        Tagged.Clear();
        Snapshots.Clear();
        Posts.Clear();
        Status = Viability.Unknown;
    }

    private static bool TryTag(object officer, Vector3? post, out IntPtr pointer)
    {
        pointer = IntPtr.Zero;
        if (officer is not Il2CppObjectBase native)
            return false;

        try
        {
            pointer = native.Pointer;
        }
        catch
        {
            return false;
        }

        if (pointer == IntPtr.Zero || Tagged.Contains(pointer))
            return false;

        var snapshot = AgentSnapshot.Take(officer);
        ApplyStrength(officer, snapshot);
        Snapshots[pointer] = snapshot;
        Tagged.Add(pointer);

        if (post is { } p)
            Posts[pointer] = p;

        return true;
    }

    private static void ApplyStrength(object officer, AgentSnapshot baseline)
    {
        var config = PoliceRuntime.Config;
        var healthMul = Math.Max(1f, config?.FederalHealthMultiplier.Value ?? 2f);
        var visionMul = Math.Max(1f, config?.FederalVisionMultiplier.Value ?? 1.75f);
        var search = Math.Clamp(config?.FederalSearchChance.Value ?? 1f, 0f, 1f);
        var speedMul = Math.Max(1f, config?.FederalMovementSpeedMultiplier.Value ?? 1.25f);
        var giveUpMul = Math.Max(1f, config?.FederalGiveUpRangeMultiplier.Value ?? 1.5f);
        var searchTimeMul = Math.Max(1f, config?.FederalSearchTimeMultiplier.Value ?? 1.5f);

        Members.TryWrite(officer, "BodySearchChance", search);

        if (baseline.VisionCone is not null)
        {
            if (baseline.RangeMultiplier is { } range)
                Members.TryWrite(baseline.VisionCone, "RangeMultiplier", range * visionMul);
            if (baseline.Attentiveness is { } attentiveness)
                Members.TryWrite(baseline.VisionCone, "Attentiveness", attentiveness * visionMul);
            if (baseline.Memory is { } memory)
                Members.TryWrite(baseline.VisionCone, "Memory", memory * (visionMul + 0.25f));
        }

        if (baseline.Health is not null && baseline.HealthValue is { } health)
        {
            var boosted = health * healthMul;
            Members.TryWrite(baseline.Health, "Health", boosted);
            Members.TryWrite(baseline.Health, "MaxHealth", boosted);
        }

        if (baseline.Combat is not null)
        {
            if (baseline.MovementSpeed is { } speed)
                Members.TryWrite(baseline.Combat, "DefaultMovementSpeed", speed * speedMul);
            if (baseline.GiveUpRange is { } giveUp)
                Members.TryWrite(baseline.Combat, "GiveUpRange", giveUp * giveUpMul);
            if (baseline.SearchTime is { } searchTime)
                Members.TryWrite(baseline.Combat, "DefaultSearchTime", searchTime * searchTimeMul);
        }

        if (baseline.VehiclePursuit is not null)
            Members.Invoke(baseline.VehiclePursuit, "SetAggressiveDriving", true);
    }

    private static bool IsLivingShipped(object? officer)
    {
        if (officer is null || !GameReflection.IsPresent(officer))
            return false;

        var health = Members.ReadPath(officer, "Health");
        return !Members.Read(health, "IsDead", false) && !Members.Read(health, "IsKnockedOut", false);
    }

    private static object? FindByPointer(IntPtr pointer)
    {
        foreach (var officer in DetectionTuner.Officers())
        {
            if (officer is Il2CppObjectBase native && native.Pointer == pointer && GameReflection.IsPresent(officer))
                return officer;
        }

        return null;
    }

    private static void RaiseActivated()
    {
        try
        {
            AgentActivated?.Invoke();
        }
        catch (Exception ex)
        {
            PoliceLog.Warn($"An agent-activated handler threw: {PoliceLog.Describe(ex)}");
        }
    }

    internal readonly struct Viability
    {
        internal static readonly Viability Unknown = new(false, "not surveyed yet");

        internal Viability(bool canDesignate, string reason)
        {
            CanDesignate = canDesignate;
            Reason = reason;
        }

        internal bool CanDesignate { get; }
        internal string Reason { get; }

        /// <summary>Legacy name kept so probe/menu copy compiles without a wider rename.</summary>
        internal bool CanSpawn => CanDesignate;
    }

    private readonly struct AgentSnapshot
    {
        private AgentSnapshot(
            object officer,
            float? bodySearchChance,
            object? visionCone,
            float? rangeMultiplier,
            float? attentiveness,
            float? memory,
            object? health,
            float? healthValue,
            float? maxHealth,
            object? combat,
            float? movementSpeed,
            float? giveUpRange,
            float? searchTime,
            object? vehiclePursuit)
        {
            Officer = officer;
            BodySearchChance = bodySearchChance;
            VisionCone = visionCone;
            RangeMultiplier = rangeMultiplier;
            Attentiveness = attentiveness;
            Memory = memory;
            Health = health;
            HealthValue = healthValue;
            MaxHealth = maxHealth;
            Combat = combat;
            MovementSpeed = movementSpeed;
            GiveUpRange = giveUpRange;
            SearchTime = searchTime;
            VehiclePursuit = vehiclePursuit;
        }

        internal object Officer { get; }
        internal float? BodySearchChance { get; }
        internal object? VisionCone { get; }
        internal float? RangeMultiplier { get; }
        internal float? Attentiveness { get; }
        internal float? Memory { get; }
        internal object? Health { get; }
        internal float? HealthValue { get; }
        internal float? MaxHealth { get; }
        internal object? Combat { get; }
        internal float? MovementSpeed { get; }
        internal float? GiveUpRange { get; }
        internal float? SearchTime { get; }
        internal object? VehiclePursuit { get; }

        internal static AgentSnapshot Take(object officer)
        {
            var cone = Members.ReadPath(officer, "Awareness.VisionCone");
            var health = Members.ReadPath(officer, "Health");
            var combat = Members.ReadPath(officer, "CombatBehaviour")
                         ?? Members.ReadPath(officer, "PursuitBehaviour");
            var vehicle = Members.ReadPath(officer, "VehiclePursuitBehaviour");

            return new AgentSnapshot(
                officer,
                ReadFloat(officer, "BodySearchChance"),
                cone,
                cone is null ? null : ReadFloat(cone, "RangeMultiplier"),
                cone is null ? null : ReadFloat(cone, "Attentiveness"),
                cone is null ? null : ReadFloat(cone, "Memory"),
                health,
                health is null ? null : ReadFloat(health, "Health"),
                health is null ? null : ReadFloat(health, "MaxHealth"),
                combat,
                combat is null ? null : ReadFloat(combat, "DefaultMovementSpeed"),
                combat is null ? null : ReadFloat(combat, "GiveUpRange"),
                combat is null ? null : ReadFloat(combat, "DefaultSearchTime"),
                vehicle);
        }

        private static float? ReadFloat(object instance, string member) =>
            GameReflection.TryRead(instance, member, out var value, out _) && value is float number
                ? number
                : null;

        internal void Restore()
        {
            if (!GameReflection.IsPresent(Officer))
                return;

            try
            {
                if (BodySearchChance is { } search)
                    Members.TryWrite(Officer, "BodySearchChance", search);

                if (VisionCone is not null && GameReflection.IsPresent(VisionCone))
                {
                    if (RangeMultiplier is { } range)
                        Members.TryWrite(VisionCone, "RangeMultiplier", range);
                    if (Attentiveness is { } attentiveness)
                        Members.TryWrite(VisionCone, "Attentiveness", attentiveness);
                    if (Memory is { } memory)
                        Members.TryWrite(VisionCone, "Memory", memory);
                }

                if (Health is not null && GameReflection.IsPresent(Health))
                {
                    if (MaxHealth is { } max)
                        Members.TryWrite(Health, "MaxHealth", max);
                    if (HealthValue is { } health)
                        Members.TryWrite(Health, "Health", health);
                }

                if (Combat is not null && GameReflection.IsPresent(Combat))
                {
                    if (MovementSpeed is { } speed)
                        Members.TryWrite(Combat, "DefaultMovementSpeed", speed);
                    if (GiveUpRange is { } giveUp)
                        Members.TryWrite(Combat, "GiveUpRange", giveUp);
                    if (SearchTime is { } searchTime)
                        Members.TryWrite(Combat, "DefaultSearchTime", searchTime);
                }

                if (VehiclePursuit is not null && GameReflection.IsPresent(VehiclePursuit))
                    Members.Invoke(VehiclePursuit, "SetAggressiveDriving", false);
            }
            catch (Exception ex)
            {
                PoliceLog.Detail($"Federal snapshot restore: {PoliceLog.Describe(ex)}");
            }
        }
    }
}

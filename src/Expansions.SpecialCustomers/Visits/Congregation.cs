using Expansions.Core.Diagnostics;
using Expansions.SpecialCustomers.Archetypes;
using Expansions.SpecialCustomers.Game;
using Expansions.SpecialCustomers.Visitors;
using S1API.Entities;
using UnityEngine;

namespace Expansions.SpecialCustomers.Visits;

/// <summary>
/// Puts the group somewhere findable and keeps them there.
/// <para>
/// Placement is a warp, never a walk-in: an NPC pathing in from off-map crosses navmesh the player
/// has not streamed, and the shipped answer to that is the same warp points the game uses for its
/// own arrivals. The relocate path mirrors Police's proven officer move: Stop → Warp →
/// WarpToNavMesh → verify distance, with a hard retry. "Arrived" means bodies within a few metres
/// of the meeting point, not a state flag.
/// </para>
/// </summary>
internal static class Congregation
{
    /// <summary>How close the player has to be for the group to give them room.</summary>
    private const float PlayerClearance = 8f;

    private const float PlayerPushback = 4f;

    /// <summary>Same acceptance radius Police uses when proving an officer actually moved.</summary>
    private const float ArrivalAcceptMetres = 6f;

    private const string SpeedControlId = "sc_visit";
    private const string NpcMovementType = "Il2CppScheduleOne.NPCs.NPCMovement";

    /// <summary>Warps every member into a ring around the delivery point. Members stay hidden.</summary>
    internal static bool Place(Visit visit, out string failure)
    {
        var centre = visit.StandPoint;
        var radius = visit.Archetype.ClusterRadius;

        // Pop-in three feet from the player's face reads as a bug, so the ring backs off when they
        // are already standing on the spot.
        if (PlayerIsStandingOnIt(centre))
            radius += PlayerPushback;

        PostWatch.ExemptVisit(visit.MemberSlots);

        var index = 0;
        var count = Math.Max(1, visit.MemberSlots.Count);
        var placed = 0;
        var problems = new List<string>();

        foreach (var slot in visit.Members)
        {
            var npc = VisitorRuntime.Resolve(slot);
            if (npc is null)
            {
                problems.Add($"slot {slot.Index:00} not in world");
                index++;
                continue;
            }

            // Schedule off for the visit so the civilian routine cannot walk them away mid-stay.
            // The watchdog itself is suppressed via ExemptVisit above.
            if (!PostWatch.Pin(slot, out var pinFailure))
                VisitorLog.Instance.Debug($"Could not pin slot {slot.Index:00} for the visit ({pinFailure}).");

            var position = RingPosition(centre, radius, index, count);
            if (!Relocate(npc, slot, position, centre, visit.Archetype, out var landed, out var relocateFailure))
            {
                problems.Add($"{slot.FullName}: {relocateFailure}");
                VisitorLog.Instance.Error(
                    $"FAILED to place {slot.FullName} at the meeting point: {relocateFailure}. " +
                    $"Still at {Describe.Of(npc.Position)}; meeting point is {Describe.Of(centre)}.");
            }
            else
            {
                placed++;
                var dist = Horizontal(landed, centre);
                VisitorLog.Instance.Msg(
                    $"{slot.FullName} placed at {Describe.Of(landed)} " +
                    $"({WorldGeography.NameOf(visit.Region)}), {dist:0.#} m from meeting point {Describe.Of(centre)}.");
            }

            var gameNpc = GameNpc.Resolve(slot.Id, out _);
            gameNpc?.SetRegion((int)visit.Region, out _);

            try
            {
                npc.Region = visit.Region;
            }
            catch (Exception ex)
            {
                VisitorLog.Instance.Debug($"Could not set the region on slot {slot.Index:00} ({Describe.Of(ex)}).");
            }

            index++;
        }

        if (placed == 0)
        {
            failure = problems.Count > 0
                ? string.Join("; ", problems)
                : "no group members could be relocated";
            return false;
        }

        failure = problems.Count == 0 ? string.Empty : string.Join("; ", problems);
        return problems.Count == 0;
    }

    /// <summary>Sends the group home: back to the parked positions, speed controls removed.</summary>
    internal static void Park(VisitorSlot slot)
    {
        var npc = VisitorRuntime.Resolve(slot);
        if (npc is null)
            return;

        // Re-applied on every park, because the whole point of parking someone is that they stay
        // there and the generic NPC schedule is what walks them away again.
        if (!PostWatch.Pin(slot, out var pinFailure))
            VisitorLog.Instance.Debug($"Could not pin slot {slot.Index:00} to its post ({pinFailure}).");

        try
        {
            npc.Movement.RemoveSpeedControl(SpeedControlId);
        }
        catch (Exception ex)
        {
            VisitorLog.Instance.Debug($"Could not clear the speed control on slot {slot.Index:00} ({Describe.Of(ex)}).");
        }

        try
        {
            npc.Aggressiveness = 0f;
            Relocate(npc, slot, slot.SpawnPosition, slot.SpawnPosition, archetype: null, out _, out _);
            npc.Region = slot.Region;
        }
        catch (Exception ex)
        {
            VisitorLog.Instance.Debug($"Parking slot {slot.Index:00} threw ({Describe.Of(ex)}).");
        }

        GameNpc.Resolve(slot.Id, out _)?.SetRegion((int)slot.Region, out _);
    }

    /// <summary>
    /// Evenly spaced around the delivery point, facing inward. The offset is deterministic per
    /// member so two peers place the same person in the same spot.
    /// </summary>
    internal static Vector3 RingPosition(Vector3 centre, float radius, int index, int count)
    {
        var angle = (index / (float)count) * Mathf.PI * 2f;
        return centre + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
    }

    /// <summary>Horizontal metres from each live member to the meeting point, for the arrival audit.</summary>
    internal static void LogPositions(Visit visit, string phase)
    {
        foreach (var slot in visit.Members)
        {
            var npc = VisitorRuntime.Resolve(slot);
            if (npc is null)
            {
                VisitorLog.Instance.Error($"{phase}: {slot.FullName} is not in the world.");
                continue;
            }

            var here = npc.Position;
            var dist = Horizontal(here, visit.StandPoint);
            var ok = dist <= ArrivalAcceptMetres;
            var line =
                $"{phase}: {slot.FullName} at {Describe.Of(here)} region={npc.Region}, " +
                $"{dist:0.#} m from meeting point (accept ≤ {ArrivalAcceptMetres:0.#} m) → {(ok ? "AT POINT" : "NOT AT POINT")}";

            if (ok)
                VisitorLog.Instance.Msg(line);
            else
                VisitorLog.Instance.Error(line);
        }
    }

    private static bool Relocate(
        NPC npc,
        VisitorSlot slot,
        Vector3 destination,
        Vector3 facePoint,
        Archetype? archetype,
        out Vector3 landed,
        out string failure)
    {
        landed = npc.Position;
        failure = string.Empty;

        try
        {
            var go = npc.gameObject;
            if (go is not null && !go.activeSelf)
                go.SetActive(true);

            npc.Movement.Stop();
            npc.Movement.Warp(destination);
            NativeWarpToNavMesh(npc, destination);

            landed = npc.Position;
            if (Horizontal(landed, destination) > ArrivalAcceptMetres)
            {
                npc.Movement.Warp(destination);
                NativeWarpToNavMesh(npc, destination);
                landed = npc.Position;
            }

            npc.Movement.FacePoint(facePoint);

            if (archetype is not null)
            {
                npc.Aggressiveness = archetype.Aggressiveness;
                if (Math.Abs(archetype.WalkSpeed - 1f) > 0.01f)
                    npc.Movement.AddSpeedControl(SpeedControlId, 5, archetype.WalkSpeed);
            }

            var dist = Horizontal(landed, destination);
            if (dist > ArrivalAcceptMetres)
            {
                failure =
                    $"after Warp+WarpToNavMesh still {dist:0.#} m from target {Describe.Of(destination)} " +
                    $"(was parked at {Describe.Of(slot.SpawnPosition)})";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            failure = Describe.Of(ex);
            return false;
        }
    }

    private static void NativeWarpToNavMesh(NPC npc, Vector3 destination)
    {
        try
        {
            var native = VisitorIntegrity.NativeOf(npc);
            if (native is null)
                return;

            if (!GameReflection.TryRead(native, "Movement", out var movement, out _) ||
                !GameReflection.IsPresent(movement))
            {
                return;
            }

            var typed = InteropCast.As(movement, NpcMovementType) ?? movement;
            if (typed is null)
                return;

            var type = GameReflection.FindType(NpcMovementType) ?? typed.GetType();

            GameReflection.TryInvoke(type, typed, "Warp", new object?[] { destination }, out _, out _);
            GameReflection.TryInvoke(type, typed, "WarpToNavMesh", Array.Empty<object?>(), out _, out _);
            GameReflection.TryInvoke(type, typed, "SetDestination", new object?[] { destination }, out _, out _);
        }
        catch (Exception ex)
        {
            VisitorLog.Instance.Debug($"Native WarpToNavMesh threw ({Describe.Of(ex)}).");
        }
    }

    private static float Horizontal(Vector3 a, Vector3 b) =>
        new Vector2(a.x - b.x, a.z - b.z).magnitude;

    private static bool PlayerIsStandingOnIt(Vector3 centre)
    {
        try
        {
            foreach (var player in Player.All)
            {
                if (player is not null && Vector3.Distance(player.Position, centre) <= PlayerClearance)
                    return true;
            }
        }
        catch
        {
            // Not knowing where the player is only costs the group its politeness.
        }

        return false;
    }
}

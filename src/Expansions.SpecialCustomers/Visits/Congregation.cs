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
/// own arrivals.
/// </para>
/// </summary>
internal static class Congregation
{
    /// <summary>How close the player has to be for the group to give them room.</summary>
    private const float PlayerClearance = 8f;

    private const float PlayerPushback = 4f;

    private const string SpeedControlId = "sc_visit";

    /// <summary>Warps every member into a ring around the delivery point. Members stay hidden.</summary>
    internal static void Place(Visit visit)
    {
        var centre = visit.StandPoint;
        var radius = visit.Archetype.ClusterRadius;

        // Pop-in three feet from the player's face reads as a bug, so the ring backs off when they
        // are already standing on the spot.
        if (PlayerIsStandingOnIt(centre))
            radius += PlayerPushback;

        var index = 0;
        var count = Math.Max(1, visit.MemberSlots.Count);

        foreach (var slot in visit.Members)
        {
            var npc = VisitorRuntime.Resolve(slot);
            if (npc is null)
            {
                VisitorLog.Instance.Warn($"Visitor slot {slot.Index:00} is not in the world, so it cannot join the group.");
                index++;
                continue;
            }

            var position = RingPosition(centre, radius, index, count);
            Place(npc, position, centre, visit.Archetype);

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
    }

    /// <summary>Sends the group home: back to the parked positions, speed controls removed.</summary>
    internal static void Park(VisitorSlot slot)
    {
        var npc = VisitorRuntime.Resolve(slot);
        if (npc is null)
            return;

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
            npc.Movement.Warp(slot.SpawnPosition);
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

    private static void Place(NPC npc, Vector3 position, Vector3 centre, Archetype archetype)
    {
        try
        {
            npc.Movement.Warp(position);
            npc.Movement.FacePoint(centre);
            npc.Aggressiveness = archetype.Aggressiveness;

            if (Math.Abs(archetype.WalkSpeed - 1f) > 0.01f)
                npc.Movement.AddSpeedControl(SpeedControlId, 5, archetype.WalkSpeed);
        }
        catch (Exception ex)
        {
            VisitorLog.Instance.Warn($"Placing a group member failed ({Describe.Of(ex)}); they stay where they were.");
        }
    }

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

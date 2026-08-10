using Expansions.Core.Diagnostics;
using Expansions.PoliceOverhaul.State;
using S1API.Law;
using UnityEngine;

namespace Expansions.PoliceOverhaul.Runtime;

/// <summary>
/// Killing a cop is not a normal crime on the heat curve — it is a dedicated escalation that spikes
/// heat, raises wanted, and floods the kill site with officers.
/// </summary>
internal sealed class OfficerKillResponse
{
    private readonly PoliceConfig _config;
    private readonly HeatDirector _heat;

    /// <summary>Last officer→player pair from <c>NotifyAttackedByPlayer</c>, used to attribute a Die.</summary>
    private IntPtr _lastAttackedOfficer;
    private object? _lastAttacker;
    private float _lastAttackRealtime;

    internal OfficerKillResponse(PoliceConfig config, HeatDirector heat)
    {
        _config = config;
        _heat = heat;
    }

    internal int KillsThisSession { get; private set; }

    internal string LastResponse { get; private set; } = "none yet";

    internal void NoteAttack(object? health, object? player)
    {
        if (health is null || player is null)
            return;

        var officer = Members.ReadPath(health, "npc") ?? Members.ReadPath(health, "Npc");
        if (!IsPoliceForceMember(officer))
            return;

        _lastAttackedOfficer = Components.PointerOf(officer);
        _lastAttacker = player;
        _lastAttackRealtime = Time.realtimeSinceStartup;
    }

    internal void OnOfficerDied(object? health)
    {
        if (!HostGate.IsAuthority || !_config.EnableOfficerKillResponse.Value)
            return;

        var officer = Members.ReadPath(health, "npc") ?? Members.ReadPath(health, "Npc");
        if (!IsPoliceForceMember(officer))
            return;

        var pointer = Components.PointerOf(officer);
        object? player = null;

        if (pointer == _lastAttackedOfficer &&
            _lastAttacker is not null &&
            Time.realtimeSinceStartup - _lastAttackRealtime < 12f)
        {
            player = _lastAttacker;
        }

        player ??= GameBridge.LocalPlayer();
        if (player is null)
            return;

        // Federal agents dying in a chase are not "you killed a city cop".
        if (FederalAgents.IsAgent(officer))
            return;

        Respond(player, officer);
    }

    private void Respond(object player, object? officer)
    {
        KillsThisSession++;

        var heatSpike = Math.Max(0f, _config.OfficerKillHeat.Value);
        if (heatSpike > 0f)
        {
            var record = _heat.RecordFor(player);
            _heat.SetHeat(record, record.Heat + heatSpike);
        }

        var position = Components.TransformOf(officer)?.position
                       ?? Components.TransformOf(player)?.position
                       ?? Vector3.zero;

        var dispatch = Math.Clamp(_config.OfficerKillDispatchCount.Value, 1, HeatModel.HardOfficerCap);
        var available = PoliceForce.EnsureAt(position, dispatch, player, beginAsSighted: true);

        RaiseWanted(player, _config.OfficerKillWantedLevel.Value);

        var message =
            $"Officer down. Heat +{heatSpike:0}, wanted escalated, and {available} unit(s) are rolling on your position " +
            $"(asked for {dispatch}). Killing cops is how the whole town learns your face.";

        LastResponse = message;
        PoliceLog.Msg($"Officer kill response: {message}");

        if (_config.ShowHud.Value)
            PoliceMessages.Announce("Officer down", message, toastSeconds: 10f, urgent: true, forPlayer: player);
    }

    private static void RaiseWanted(object player, int level)
    {
        try
        {
            var apiPlayer = S1API.Entities.Player.Local;
            if (apiPlayer is null || !GameBridge.IsLocal(player))
                return;

            var clamped = Math.Clamp(level, (int)PursuitLevel.Investigating, (int)PursuitLevel.Lethal);
            LawManager.SetWantedLevel(apiPlayer, (PursuitLevel)clamped);
        }
        catch (Exception ex)
        {
            PoliceLog.Detail($"Officer-kill wanted escalate failed: {PoliceLog.Describe(ex)}");
        }
    }

    private static bool IsPoliceForceMember(object? npc)
    {
        if (npc is null || !GameReflection.IsPresent(npc))
            return false;

        var name = GameBridge.NativeClassName(npc);
        return name.IndexOf("PoliceOfficer", StringComparison.Ordinal) >= 0;
    }
}

using Expansions.Core.Diagnostics;
using S1API.Law;
using UnityEngine;

namespace Expansions.PoliceOverhaul.Runtime;

/// <summary>
/// Makes cops show up promptly after a crime or a call, without writing IL2CPP consts.
/// <para>
/// The engine's phone-call duration and search-time statics are const-inlined on this build, so the
/// levers that actually move are: dispatch with <c>beginAsSighted</c>, the one writable vehicle
/// threshold, an immediate (config-delayed) <see cref="PoliceForce.Dispatch"/>, and a bump of the
/// S1API wanted level so the chase AI engages instead of milling.
/// </para>
/// </summary>
internal sealed class ResponseTuner
{
    private readonly PoliceConfig _config;
    private readonly LawLevers _levers;

    private float _appliedAggressiveness = float.NaN;
    private float _vanillaVehicleThreshold = float.NaN;
    private bool _queued;

    internal ResponseTuner(PoliceConfig config, LawLevers levers)
    {
        _config = config;
        _levers = levers;
    }

    /// <summary>0..2, clamped. Read by dispatch and the pursuit-timeout patch.</summary>
    internal float Aggressiveness => Math.Clamp(_config.PursuitAggressiveness.Value, 0f, 2f);

    /// <summary>True when responding units should already know the player's position.</summary>
    internal bool BeginAsSighted => Aggressiveness >= 0.5f;

    /// <summary>
    /// How many times the vanilla search window an outlaw pursuit must outlast before timing out.
    /// Vanilla is 1; the previous outlaw patch hard-coded 2. Aggressiveness stretches that further.
    /// </summary>
    internal float OutlawPursuitHold => 1f + Aggressiveness;

    /// <summary>Apply the writable vehicle-threshold lever for the current aggressiveness.</summary>
    internal void Apply()
    {
        var aggression = Aggressiveness;
        if (Math.Abs(aggression - _appliedAggressiveness) < 0.001f)
            return;

        if (!_levers.IsWritable(GameTypes.LawManager, "DISPATCH_VEHICLE_USE_THRESHOLD"))
        {
            _appliedAggressiveness = aggression;
            return;
        }

        var type = GameReflection.FindType(GameTypes.LawManager);
        if (type is null)
            return;

        if (float.IsNaN(_vanillaVehicleThreshold))
        {
            if (!GameReflection.TryReadStatic(type, "DISPATCH_VEHICLE_USE_THRESHOLD", out var raw, out _) ||
                raw is not float vanilla)
            {
                return;
            }

            _vanillaVehicleThreshold = vanilla;
        }

        // Lower threshold → more cruiser responses → faster cross-town arrival. Floor at 25% of vanilla
        // so we never write nonsense if aggressiveness is maxed.
        var factor = Math.Clamp(1f - (aggression * 0.35f), 0.25f, 1f);
        _levers.Set(GameTypes.LawManager, "DISPATCH_VEHICLE_USE_THRESHOLD", _vanillaVehicleThreshold * factor);
        _appliedAggressiveness = aggression;
        PoliceLog.Detail(
            $"Response tuner: aggressiveness {aggression:0.##}, vehicle threshold " +
            $"{_vanillaVehicleThreshold * factor:0.##} (vanilla {_vanillaVehicleThreshold:0.##}).");
    }

    internal void Restore()
    {
        _appliedAggressiveness = float.NaN;
        _vanillaVehicleThreshold = float.NaN;
        _queued = false;
    }

    /// <summary>
    /// Queue a real dispatch to the player's position after the configured delay. Coalesces bursts of
    /// crimes in the same window into one response so we do not spam the hard cap of 4.
    /// </summary>
    internal void QueueResponse(object? player, string reason)
    {
        if (!HostGate.IsAuthority || player is null)
            return;

        if (_queued)
        {
            PoliceLog.Detail($"Response already queued; ignoring duplicate ({reason}).");
            return;
        }

        var delaySeconds = Math.Clamp(_config.ResponseDelaySeconds.Value, 0f, 30f);
        var frames = Math.Max(1, (int)Math.Round(delaySeconds * 60f));
        var wanted = Math.Max(1, _config.EventOfficerCount.Value);
        var sighted = BeginAsSighted;
        var aggression = Aggressiveness;

        _queued = true;
        Deferred.After(frames, "police response", () =>
        {
            _queued = false;

            try
            {
                Apply();

                var position = Components.TransformOf(player)?.position ?? Vector3.zero;
                var available = PoliceForce.EnsureAt(position, wanted, player, sighted);

                EscalateWanted(player, aggression);

                PoliceLog.Msg(
                    $"Police response ({reason}): {available} officer(s) near the scene after {delaySeconds:0.#}s, " +
                    $"beginAsSighted={sighted}, aggressiveness={aggression:0.##}." +
                    (PoliceForce.LastShortfall.Length > 0 ? $" {PoliceForce.LastShortfall}" : string.Empty));
            }
            catch (Exception ex)
            {
                PoliceLog.Error("Police Improvements threw while dispatching a response; the game carries on.", ex);
            }
        });
    }

    /// <summary>
    /// Immediate response path for the menu/event "call the police" button — same levers, no queue.
    /// </summary>
    internal string RespondNow(object? player, string reason)
    {
        if (player is null)
            return "There is no local player to report.";

        Apply();

        var position = Components.TransformOf(player)?.position ?? Vector3.zero;
        var wanted = Math.Max(1, _config.EventOfficerCount.Value);
        var available = PoliceForce.EnsureAt(position, wanted, player, BeginAsSighted);
        EscalateWanted(player, Aggressiveness);

        var shortfall = PoliceForce.LastShortfall.Length > 0 ? $" Note: {PoliceForce.LastShortfall}" : string.Empty;
        return $"Dispatch has your position ({reason}): {available} officer(s) live nearby, " +
               $"aggressiveness {Aggressiveness:0.##}, beginAsSighted={BeginAsSighted}.{shortfall}";
    }

    private static void EscalateWanted(object player, float aggression)
    {
        try
        {
            var apiPlayer = S1API.Entities.Player.Local;
            if (apiPlayer is null || !GameBridge.IsLocal(player))
                return;

            // Investigating is enough for units to move; Arresting at high aggression makes the
            // response feel like a real call rather than a look-around.
            var level = aggression >= 1.5f ? PursuitLevel.Arresting : PursuitLevel.Investigating;
            var current = LawManager.GetWantedLevel(apiPlayer);
            if ((int)current < (int)level)
                LawManager.SetWantedLevel(apiPlayer, level);
            else if (current == PursuitLevel.None)
                LawManager.EscalateWantedLevel(apiPlayer);
        }
        catch (Exception ex)
        {
            PoliceLog.Detail($"Could not raise wanted level for response: {PoliceLog.Describe(ex)}");
        }
    }
}

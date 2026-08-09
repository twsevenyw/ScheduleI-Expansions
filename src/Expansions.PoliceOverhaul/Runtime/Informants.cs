using Expansions.Core.Diagnostics;

namespace Expansions.PoliceOverhaul.Runtime;

/// <summary>
/// Remembers who called the police on you, and makes the arrest cost you that relationship.
/// <para>
/// The game already tells us exactly who dialled: <c>CallPoliceBehaviour</c> finishes a call by
/// naming its <c>Target</c> player, and the behaviour hangs off the NPC that made it. So this is not
/// a guess about who was nearby — it is the caller, by name, which is what makes the notification
/// worth reading.
/// </para>
/// <para>
/// Unlike the tuning levers elsewhere in this module, a relationship change is <em>not</em> snapshotted
/// and restored on disable. It is an outcome of play written into the game's own save, in the same
/// class as the fine that was charged and the product that was seized — rolling those back on a toggle
/// would be rewriting history, not unwinding a patch.
/// </para>
/// </summary>
internal sealed class Informants
{
    /// <summary>A call older than this is not what got you arrested. Two in-game hours.</summary>
    private const int MemoryMinutes = 120;

    private readonly PoliceConfig _config;
    private readonly List<Call> _calls = new();

    internal Informants(PoliceConfig config) => _config = config;

    internal int Remembered => _calls.Count;

    /// <summary>Name of whoever last called it in, for the probe and the arrest line. Empty if nobody has.</summary>
    internal string LastCaller { get; private set; } = string.Empty;

    internal float RelationshipLost { get; private set; }

    /// <summary>Records a completed police call. Called from the behaviour's own finish hook.</summary>
    internal void Record(object? behaviour)
    {
        if (!_config.EnableRelationshipDamage.Value)
            return;

        var npc = Members.ReadPath(behaviour, "Npc");
        if (!GameReflection.IsPresent(npc) || npc is null)
            return;

        var target = Members.ReadPath(behaviour, "Target");
        var key = GameBridge.KeyFor(GameReflection.IsPresent(target) ? target : GameBridge.LocalPlayer());
        var pointer = Components.PointerOf(npc);

        Forget(pointer);
        _calls.Add(new Call(pointer, npc, key, GameClock.Minutes()));

        LastCaller = Members.Read(npc, "FullName", string.Empty);
        PoliceLog.Detail($"'{LastCaller}' called the police on '{key}'.");
    }

    /// <summary>
    /// Applies the fallout after an arrest and returns the names, or empty when nobody informed.
    /// Callers older than the memory window are dropped rather than punished: a grudge should attach
    /// to the person who made the call that got you taken in, not to everyone who ever has.
    /// </summary>
    internal string SettleAfterArrest(object? player)
    {
        if (!_config.EnableRelationshipDamage.Value || _calls.Count == 0)
            return string.Empty;

        var key = GameBridge.KeyFor(player);
        var now = GameClock.Minutes();
        var delta = Math.Abs(_config.SnitchRelationshipDamage.Value);
        var punished = new List<string>();

        for (var i = _calls.Count - 1; i >= 0; i--)
        {
            var call = _calls[i];

            if (now - call.AtMinute > MemoryMinutes)
            {
                _calls.RemoveAt(i);
                continue;
            }

            if (!string.Equals(call.PlayerKey, key, StringComparison.Ordinal))
                continue;

            _calls.RemoveAt(i);

            if (delta <= 0f || !GameReflection.IsPresent(call.Npc))
                continue;

            var relation = Members.ReadPath(call.Npc, "RelationData");
            if (relation is null)
                continue;

            if (!Members.Invoke(relation, "ChangeRelationship", -delta, true))
                continue;

            RelationshipLost += delta;

            var name = Members.Read(call.Npc, "FullName", string.Empty);
            punished.Add(name.Length > 0 ? name : "someone you know");
        }

        if (punished.Count == 0)
            return string.Empty;

        var names = string.Join(" and ", punished.Distinct(StringComparer.Ordinal).Take(3));
        PoliceLog.Msg($"Relationship dropped by {delta:0.00} with {names} for calling the police.");

        if (_config.ShowHud.Value)
            PoliceMessages.InformantFallout(names);

        return names;
    }

    internal void Clear()
    {
        _calls.Clear();
        LastCaller = string.Empty;
    }

    private void Forget(IntPtr pointer)
    {
        for (var i = _calls.Count - 1; i >= 0; i--)
        {
            if (_calls[i].Pointer == pointer)
                _calls.RemoveAt(i);
        }
    }

    private readonly struct Call
    {
        internal Call(IntPtr pointer, object npc, string playerKey, int atMinute)
        {
            Pointer = pointer;
            Npc = npc;
            PlayerKey = playerKey;
            AtMinute = atMinute;
        }

        internal IntPtr Pointer { get; }

        internal object Npc { get; }

        internal string PlayerKey { get; }

        internal int AtMinute { get; }
    }
}

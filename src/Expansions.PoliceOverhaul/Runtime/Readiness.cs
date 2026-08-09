using Expansions.Core.Actions;
using Expansions.PoliceOverhaul.State;

namespace Expansions.PoliceOverhaul.Runtime;

/// <summary>
/// Why a control is greyed out, in words that tell the owner what to do about it.
/// <para>
/// Shared by the menu actions and the triggerable events so the two can never disagree. Every string
/// here names the specific thing that is missing — a save, the host, a setting, a property — because
/// a refusal that says "not available right now" is indistinguishable from a bug and sends the owner
/// to the log for something the button already knew.
/// </para>
/// </summary>
internal static class Readiness
{
    /// <summary>
    /// The precondition every police control shares: a loaded game, on the authoritative peer.
    /// <para>
    /// The host gate is tested first, and that order is the whole point. A co-op client never wires the
    /// module at all, so asking "is it live" first would tell them to load a save they already have
    /// loaded — the single most misleading refusal this module could produce.
    /// </para>
    /// </summary>
    internal static ActionAvailability Live()
    {
        if (!HostGate.Evaluate(out var reason))
            return ActionAvailability.Unavailable($"the host owns police state and this peer is a {reason}");

        if (PoliceRuntime.IsLive)
            return ActionAvailability.Ready;

        var status = PoliceRuntime.WireStatus;
        return ActionAvailability.Unavailable(
            string.IsNullOrEmpty(status) || status.StartsWith("module ", StringComparison.Ordinal)
                ? "Police Improvements has not wired into a loaded game yet - load a save and come back"
                : status);
    }

    internal static ActionAvailability Federal()
    {
        var live = Live();
        if (!live.IsAvailable)
            return live;

        if (PoliceRuntime.Config is { EnableFederalAgents.Value: false })
            return ActionAvailability.Unavailable("federal agents are switched off (enable_federal_agents)");

        if (PoliceRuntime.Federal is { IsActive: true } federal)
            return ActionAvailability.Unavailable($"a federal event is already running, {federal.HoursRemaining}h left");

        if (FederalAgents.Status.CanDesignate || FederalAgents.Survey().CanDesignate)
            return ActionAvailability.Ready;

        return ActionAvailability.Unavailable(FederalAgents.Status.Reason);
    }

    internal static ActionAvailability Raid(bool immediate)
    {
        var live = Live();
        if (!live.IsAvailable)
            return live;

        if (PoliceRuntime.Config is { EnablePropertyRaids.Value: false })
            return ActionAvailability.Unavailable("property raids are switched off (enable_property_raids)");

        if (Estate.Owned().Count == 0)
            return ActionAvailability.Unavailable("you do not own a property yet, so there is nothing to raid");

        if (!immediate && PoliceRuntime.Raids is { IsPending: true } pending)
            return ActionAvailability.Unavailable($"a raid on {pending.PendingProperty} is already inbound");

        return ActionAvailability.Ready;
    }

    internal static ActionAvailability Outlaw()
    {
        var live = Live();
        if (!live.IsAvailable)
            return live;

        return PoliceRuntime.Config is { EnableOutlaw.Value: false }
            ? ActionAvailability.Unavailable("outlaw status is switched off (enable_outlaw)")
            : ActionAvailability.Ready;
    }

    internal static ActionAvailability Escalate()
    {
        var outlaw = Outlaw();
        if (!outlaw.IsAvailable)
            return outlaw;

        return PoliceRuntime.LocalRecord?.Outlaw == OutlawTier.Hunted
            ? ActionAvailability.Unavailable("you are already HUNTED, which is as far as this goes")
            : ActionAvailability.Ready;
    }

    /// <summary>
    /// Menu repair path for the legal fee. Hidden while the station-door hook is attached — pay at
    /// the door instead. Same pattern as Hireable Drivers' native hiring surface.
    /// </summary>
    internal static ActionAvailability LegalFee()
    {
        if (LegalFeeDesk.IsAttached)
        {
            return ActionAvailability.Unavailable(
                "knock on the police station door — Pay legal fee is on the door prompt " +
                $"(Marked ${PoliceRuntime.Config?.LegalFeeMarked.Value:N0}, Hunted ${PoliceRuntime.Config?.LegalFeeHunted.Value:N0})");
        }

        var outlaw = Outlaw();
        if (!outlaw.IsAvailable)
            return outlaw;

        var record = PoliceRuntime.LocalRecord;
        if (record is null || record.Outlaw == OutlawTier.Clean)
            return ActionAvailability.Unavailable("your record is already clean");

        var fee = PoliceRuntime.Config?.FeeFor(record.Outlaw) ?? 0;
        var funds = Wallet.Total();

        var doorNote = LegalFeeDesk.LastFailure.Length > 0
            ? $" Door hook unavailable ({LegalFeeDesk.LastFailure}), so this menu path is the repair."
            : " Door hook is not attached yet — this menu path is the repair.";

        return funds >= fee
            ? ActionAvailability.Ready
            : ActionAvailability.Unavailable($"clearing {OutlawState.Describe(record.Outlaw)} costs ${fee:N0} and you have ${funds:N0}.{doorNote}");
    }
}

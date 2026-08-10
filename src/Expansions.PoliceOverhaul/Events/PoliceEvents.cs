using Expansions.Core;
using Expansions.Core.Actions;
using Expansions.Core.Events;
using Expansions.PoliceOverhaul.Runtime;
using Expansions.PoliceOverhaul.State;
using S1API.Law;

namespace Expansions.PoliceOverhaul.Events;

/// <summary>
/// The five things worth making happen on demand, bound to Core's event hotkey.
/// <para>
/// These are not the menu actions with a different name. An action is the owner's console
/// replacement — read from a long list, usually to inspect or reset something. An event is a thing
/// that happens to the player while they are playing, and every one of these has a visible
/// consequence in the world within a few seconds of firing.
/// </para>
/// <para>
/// <see cref="EventRegistry"/> is called directly rather than by reflection, deliberately: this
/// module already shipped eight menu actions that silently registered zero because they were bound
/// through <c>Activator.CreateInstance</c> against a constructor signature that did not exist. Every
/// mod holds a project reference to Core, so the typed call is checked by the compiler.
/// </para>
/// </summary>
internal static class PoliceEvents
{
    internal static int RegisteredCount { get; private set; }

    internal static void Register(ModuleLifetime lifetime)
    {
        RegisteredCount = 0;

        Add(lifetime, new ExpansionEvent(
            id: "police_overhaul.federal_team",
            label: "Send a federal team",
            description: "Two plain-clothes agents arrive from the nearest station. If you are inside a property you own they set up outside it instead of chasing you.",
            isAvailable: Readiness.Federal,
            invoke: SendFederalTeam,
            order: 10));

        Add(lifetime, new ExpansionEvent(
            id: "police_overhaul.raid_warning",
            label: "Put a raid on the clock",
            description: "Targets the property you are currently inside and warns that a team is inbound. If you are not inside one, falls back to another owned property.",
            isAvailable: () => Readiness.Raid(immediate: false),
            invoke: () => Raid(immediate: false),
            order: 20));

        Add(lifetime, new ExpansionEvent(
            id: "police_overhaul.raid_now",
            label: "Raid a property now",
            description: "Immediately targets the property you are currently inside. Your presence makes the team drive on, so nothing is seized.",
            isAvailable: () => Readiness.Raid(immediate: true),
            invoke: () => Raid(immediate: true),
            order: 30));

        Add(lifetime, new ExpansionEvent(
            id: "police_overhaul.escalate_outlaw",
            label: "Escalate outlaw status",
            description: "Pushes you Clean to MARKED, or MARKED to HUNTED, and applies everything that comes with it: searches always find, dealers charge more, card vendors close.",
            isAvailable: Readiness.Escalate,
            invoke: EscalateOutlaw,
            order: 40));

        Add(lifetime, new ExpansionEvent(
            id: "police_overhaul.call_police",
            label: "Have someone call the police",
            description: "Reports you to dispatch through the game's own call path, so real officers are sent to your last known position.",
            isAvailable: PursuitAvailability,
            invoke: CallPolice,
            order: 50));

        PoliceLog.Msg($"Registered {RegisteredCount}/5 triggerable event(s) with Core.");
    }

    private static ActionAvailability PursuitAvailability()
    {
        var live = Readiness.Live();
        if (!live.IsAvailable)
            return live;

        return S1API.Entities.Player.Local is null
            ? ActionAvailability.Unavailable("there is no local player to report")
            : ActionAvailability.Ready;
    }

    // ── Bodies ────────────────────────────────────────────────────────────────────────────────

    private static ActionResult SendFederalTeam()
    {
        if (PoliceRuntime.Federal is not { } federal)
            return ActionResult.Failed("Police Improvements is not wired into a loaded game.");

        var started = federal.ForceBegin(out var message);
        if (!started)
            PoliceMessages.Announce("Federal agents", message);
        return started ? ActionResult.Ok(message) : ActionResult.Failed(message);
    }

    private static ActionResult Raid(bool immediate)
    {
        if (PoliceRuntime.Raids is not { } raids)
            return ActionResult.Failed("Police Improvements is not wired into a loaded game.");

        var started = raids.Force(immediate, out var message);
        if (!started || immediate)
            PoliceMessages.Announce(immediate ? "Raid" : "Raid", message);
        return started ? ActionResult.Ok(message) : ActionResult.Failed(message);
    }

    private static ActionResult EscalateOutlaw()
    {
        if (PoliceRuntime.Heat is not { } heat || PoliceRuntime.Outlaw is not { } outlaw)
            return ActionResult.Failed("Police Improvements is not wired into a loaded game.");

        var record = heat.LocalRecord;
        var next = record.Outlaw == OutlawTier.Clean ? OutlawTier.Marked : OutlawTier.Hunted;

        outlaw.Promote(record, next);
        outlaw.Sync();

        var message =
            $"You are now {OutlawState.Describe(next)}. Body searches will find something, pursuits will not " +
            "let go, fines are multiplied, and card-only vendors have closed to you.";
        PoliceMessages.Announce($"Outlaw: {OutlawState.Describe(next)}", message);
        return ActionResult.Ok(message);
    }

    private static ActionResult CallPolice()
    {
        var player = S1API.Entities.Player.Local;
        if (player is null)
            return ActionResult.Failed("There is no local player to report.");

        // S1API path still runs so vanilla listeners fire; ResponseTuner then makes sure officers
        // actually leave the station under the configured delay/aggressiveness.
        LawManager.CallPolice(player);

        var native = GameBridge.LocalPlayer();
        var message = PoliceRuntime.Response is { } response
            ? response.RespondNow(native, "event hotkey")
            : FallbackCall(native);

        PoliceMessages.Announce("Dispatch", message);
        return PoliceForce.LastShortfall.Length > 0 && message.Contains("0 officer", StringComparison.Ordinal)
            ? ActionResult.Failed(message)
            : ActionResult.Ok(message);
    }

    private static string FallbackCall(object? native)
    {
        var wanted = PoliceRuntime.Config?.EventOfficerCount.Value ?? 3;
        var here = Components.TransformOf(native)?.position ?? UnityEngine.Vector3.zero;
        var available = PoliceForce.EnsureAt(here, wanted, native, beginAsSighted: true);
        return $"Dispatch has your position: {available} officer(s) live nearby." +
               (PoliceForce.LastShortfall.Length > 0 ? $" Note: {PoliceForce.LastShortfall}" : string.Empty);
    }

    private static void Add(ModuleLifetime lifetime, ExpansionEvent expansionEvent)
    {
        try
        {
            lifetime.Add(EventRegistry.Register(expansionEvent));
            RegisteredCount++;
        }
        catch (Exception ex)
        {
            PoliceLog.Warn($"Could not register event '{expansionEvent.Id}': {PoliceLog.Describe(ex)}");
        }
    }
}

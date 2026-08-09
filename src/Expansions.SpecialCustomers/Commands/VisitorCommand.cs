using Expansions.Core;
using Expansions.SpecialCustomers.Archetypes;
using Expansions.SpecialCustomers.Detection;
using Expansions.SpecialCustomers.Visitors;
using S1API.Console;
using S1API.Entities;
using UnityEngine;

namespace Expansions.SpecialCustomers.Commands;

/// <summary>
/// In-game console entry point: <c>scvisitor</c>.
/// <para>
/// S1API discovers this by reflecting over <c>BaseConsoleCommand</c> subclasses when the game's
/// console wakes up and instantiating each one through its <b>public parameterless constructor</b>.
/// There is no registration API to call, so the type must stay public and the constructor must stay
/// implicit or public and trivial.
/// </para>
/// <para>
/// Every operation here also exists as a menu action on the Expansions screen, which is the
/// supported route — this is the fallback for players whose console works.
/// </para>
/// </summary>
public sealed class VisitorCommand : BaseConsoleCommand
{
    /// <summary>Stand this far in front of the visitor rather than inside them.</summary>
    private const float TeleportStandoff = 1.6f;

    public override string CommandWord => "scvisitor";

    public override string CommandDescription =>
        "Reports on the Special Customers visitor pool and the group currently in town, and moves you to them.";

    public override string ExampleUsage =>
        "scvisitor  |  scvisitor tp  |  scvisitor group  |  scvisitor visit bikers  |  scvisitor offer  |  scvisitor detect";

    /// <summary>Arrives without the command word; S1API strips it before dispatch.</summary>
    public override void ExecuteCommand(List<string> args)
    {
        try
        {
            Execute(
                args is { Count: > 0 } ? args[0] : string.Empty,
                args is { Count: > 1 } ? args[1] : string.Empty);
        }
        catch (Exception ex)
        {
            Fail($"'{CommandWord}' failed: {Describe.Of(ex)}");
            VisitorLog.Instance.Error($"'{CommandWord}' failed.", ex);
        }
    }

    private void Execute(string subcommand, string argument)
    {
        switch (subcommand.ToLowerInvariant())
        {
            case "":
            case "where":
            case "status":
                Report();
                return;

            case "tp":
            case "goto":
                Teleport();
                return;

            case "park":
                Park();
                return;

            case "group":
                Group();
                return;

            case "visit":
                Visit(argument);
                return;

            case "offer":
                Offer();
                return;

            case "home":
                Home();
                return;

            case "detect":
                Detect();
                return;

            default:
                Usage();
                return;
        }
    }

    private void Report()
    {
        if (!ExpansionRegistry.IsEnabled(SpecialCustomersModule.ModuleId))
        {
            Say("Special Customers is disabled, so nothing is tracking the visitors. The NPCs are built by S1API and may " +
                "still be in the world; the reading below is best-effort.");
        }

        var resolved = 0;
        foreach (var slot in VisitorSlot.All)
        {
            var status = VisitorRuntime.StatusOf(slot);
            if (!status.WrapperResolved)
            {
                Say($"  {slot.Index:00} {slot.FullName,-20} missing — {status.Failure}");
                continue;
            }

            resolved++;
            Say($"  {slot.Index:00} {status.FullName,-20} {Describe.Of(status.Position)}  [{status.Region}]  " +
                $"visible={Describe.YesNo(status.IsVisible)} mugshot={Describe.YesNo(status.HasMugshot)}");
        }

        Say($"{resolved} of {VisitorSlot.Count} visitors are in the world.");
        Group();
    }

    private void Group()
    {
        var director = SpecialCustomersModule.Director;
        if (director is null)
        {
            Fail("The module is not running, so there is no group state to report.");
            return;
        }

        foreach (var line in director.DescribeStatus())
            Say(line);
    }

    private void Teleport()
    {
        var director = SpecialCustomersModule.Director;
        var visit = director?.Current;

        var target = visit is not null ? visit.StandPoint : (Vector3?)null;
        var label = visit is not null ? visit.Archetype.DisplayName : VisitorSlot.Primary.FullName;

        if (target is null)
        {
            var status = VisitorRuntime.StatusOf(VisitorSlot.Primary);
            if (!status.WrapperResolved)
            {
                Fail($"{status.Id} is not in the world, so there is nowhere to teleport to. Run 'expprobe sc'.");
                return;
            }

            target = status.Position;
        }

        var player = LocalPlayer();
        if (player is null)
        {
            Fail("No local player yet. Load a save first.");
            return;
        }

        // Moving the local player is a client-side operation, so no authority check: a co-op guest
        // is allowed to walk over and look at the group the host spawned.
        var away = player.Position - target.Value;
        away.y = 0f;
        var offset = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward;

        player.Position = target.Value + (offset * TeleportStandoff);
        Say($"Teleported to {label} at {Describe.Of(target.Value)}.");
    }

    private void Park()
    {
        var slot = VisitorSlot.Primary;

        if (!HostGate.Evaluate(out var authority))
        {
            Fail($"Only the host may move an NPC ({authority}). Ask the host to run this.");
            return;
        }

        var npc = VisitorRuntime.Resolve(slot);
        if (npc is null)
        {
            Fail($"{slot.Id} is not in the world, so there is nothing to park. Run 'expprobe sc'.");
            return;
        }

        npc.Movement.Warp(slot.SpawnPosition);
        Say($"Warped {slot.FullName} back to {Describe.Of(slot.SpawnPosition)}.");
    }

    private void Visit(string archetypeId)
    {
        var director = SpecialCustomersModule.Director;
        if (director is null)
        {
            Fail("The module is not running.");
            return;
        }

        if (archetypeId.Length > 0 && ArchetypeCatalog.Find(archetypeId) is null)
        {
            Fail($"'{archetypeId}' is not an archetype. Try: {string.Join(", ", ArchetypeCatalog.All.Select(a => a.Id))}.");
            return;
        }

        if (director.ForceVisit(archetypeId, out var message))
            Say(message);
        else
            Fail(message);
    }

    private void Offer()
    {
        var director = SpecialCustomersModule.Director;
        if (director is null)
        {
            Fail("The module is not running.");
            return;
        }

        if (director.ForceOffer(out var message))
            Say(message);
        else
            Fail(message);
    }

    private void Home()
    {
        var director = SpecialCustomersModule.Director;
        if (director is null)
        {
            Fail("The module is not running.");
            return;
        }

        if (director.SendHome(out var message))
            Say(message);
        else
            Fail(message);
    }

    private void Detect()
    {
        var verdict = OfficialFeatureDetector.Verdict;

        Say($"Detection: {verdict.Headline}");
        Say($"  {verdict.Reason}");

        foreach (var line in verdict.Evidence)
            Say($"  {line}");
    }

    private void Usage()
    {
        Say($"{CommandWord}              pool status plus whichever group is in town");
        Say($"{CommandWord} tp           teleport to the group, or to the scout when nobody is visiting");
        Say($"{CommandWord} park         host only: warp the scout back to his post");
        Say($"{CommandWord} group        just the group state");
        Say($"{CommandWord} visit [id]   host only: start a visit now ({string.Join(", ", ArchetypeCatalog.All.Select(a => a.Id))})");
        Say($"{CommandWord} offer        host only: send the group's bulk offer now");
        Say($"{CommandWord} home         host only: end the visit and unwind");
        Say($"{CommandWord} detect       why the module is or is not running");
        Say("expprobe sc         full prefab / impostor / appearance / group diagnosis, written to UserData");
    }

    private static void Say(string message)
    {
        VisitorLog.Instance.Msg(message);
        GameConsoleEcho.Write(message);
    }

    private static void Fail(string message)
    {
        VisitorLog.Instance.Warn(message);
        GameConsoleEcho.Write(message);
    }

    private static Player? LocalPlayer()
    {
        try
        {
            return Player.Local;
        }
        catch
        {
            return null;
        }
    }
}

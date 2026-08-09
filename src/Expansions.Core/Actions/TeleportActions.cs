using Expansions.Core.Game;
using UnityEngine;

namespace Expansions.Core.Actions;

/// <summary>
/// Teleporting to an NPC. Generic, driven off <c>NPCManager.NPCRegistry</c>, so it reaches every NPC
/// on the map including the ones our own mods add.
/// </summary>
internal static class TeleportActions
{
    /// <summary>
    /// The Special Customers visitor. Ships here rather than only in that mod because the owner has to
    /// be able to reach Marcus Vale whether or not Special Customers registers an action of its own —
    /// and this route needs nothing from that mod but the id.
    /// </summary>
    private const string MarcusValeId = "expansions_sc_visitor_01";

    internal static ExpansionAction ToNpc() => new(
        id: "core.teleport.npc",
        label: "Teleport to an NPC",
        description: "Search the live NPC registry by name or save id, then jump to whoever you pick.",
        isAvailable: Populated,
        choices: Choices,
        invokeChoice: Go,
        order: 10);

    internal static ExpansionAction ToMarcusVale() => new(
        id: "core.teleport.marcus_vale",
        label: "Teleport to Marcus Vale",
        description: $"The Special Customers visitor on the Northtown motel forecourt ({MarcusValeId}).",
        isAvailable: () =>
        {
            var loaded = CoreActions.RequiresLoadedSave();
            if (!loaded.IsAvailable)
                return loaded;

            return GameNpcs.IsKnown(MarcusValeId)
                ? ActionAvailability.Ready
                : ActionAvailability.Unavailable(
                    "he is not in the NPC registry - check the Special Customers toggle, or run its diagnostics area");
        },
        invoke: () =>
        {
            var record = GameNpcs.Find(MarcusValeId);
            if (record is null)
            {
                return ActionResult.Failed(
                    $"No NPC with id '{MarcusValeId}' is in the registry. Special Customers builds him through S1API " +
                    "at load time, so run the diagnostics for the prefab and spawnable-ordinal detail.");
            }

            var (ok, message) = GameNpcs.TeleportTo(record);
            return ok ? ActionResult.Ok(message) : ActionResult.Failed(message);
        },
        order: 11);

    private static ActionAvailability Populated()
    {
        var loaded = CoreActions.RequiresLoadedSave();
        if (!loaded.IsAvailable)
            return loaded;

        return GameNpcs.RegistryCount() == 0
            ? ActionAvailability.Unavailable("the NPC registry is empty - the save may still be loading")
            : ActionAvailability.Ready;
    }

    private static IReadOnlyList<ActionChoice> Choices()
    {
        // The picker is the one place the positions on screen have to be current, so it pays for a fresh
        // walk of the registry rather than reusing whatever the availability checks last cached.
        GameNpcs.Refresh();

        var records = GameNpcs.All();
        var player = GameNpcs.PlayerPosition();
        var choices = new List<ActionChoice>(records.Count);

        foreach (var record in records)
        {
            var detail = player is not null && record.HasPosition
                ? $"{record.Region} - {Vector3.Distance(player.Value, record.Position):0} m away - {record.Id}"
                : $"{record.Region} - {record.Id}";

            choices.Add(new ActionChoice(record.Id, record.Name, detail));
        }

        return choices;
    }

    private static ActionResult Go(ActionChoice choice)
    {
        // Re-resolved rather than captured: the picker list can be a few seconds old and an NPC walks.
        var record = GameNpcs.Find(choice.Id);
        if (record is null)
            return ActionResult.Failed($"'{choice.Label}' ({choice.Id}) is no longer in the registry.");

        var (ok, message) = GameNpcs.TeleportTo(record);
        return ok ? ActionResult.Ok(message) : ActionResult.Failed(message);
    }
}

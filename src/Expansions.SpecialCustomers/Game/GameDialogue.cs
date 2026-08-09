using Expansions.Core.Diagnostics;
using UnityEngine;
using UnityEngine.Events;

namespace Expansions.SpecialCustomers.Game;

/// <summary>
/// The one part of the game's dialogue system S1API does not wrap: appending a top-level choice to
/// an NPC's interaction menu.
/// <para>
/// Appending rather than overriding is deliberate. <c>DialogueController.OverrideContainer</c> would
/// replace the whole menu, and with it the shipped "complete contract" choice that is how a delivery
/// is actually handed over — so a group that could be talked to would be a group that could not be
/// paid. <c>AddDialogueChoice</c> is the game's own extension point and is what
/// <c>Customer.SetUpDialogue</c> itself uses.
/// </para>
/// </summary>
internal static class GameDialogue
{
    private const string ControllerType = "Il2CppScheduleOne.Dialogue.DialogueController";
    private const string ChoiceType = "Il2CppScheduleOne.Dialogue.DialogueController+DialogueChoice";

    /// <summary>The dialogue controller on the NPC, or null with a reason on a build that moved it.</summary>
    internal static object? FindController(GameNpc npc, out string failure)
    {
        var controllerType = GameReflection.FindType(ControllerType);
        if (controllerType is null)
        {
            failure = $"'{ControllerType}' not found";
            return null;
        }

        var il2cppType = Il2CppInterop.Runtime.Il2CppType.From(controllerType);

        // The controller sits beside the handler on prefabs the game ships, but S1API can add the
        // handler itself, so the NPC root is searched as a fallback.
        if (npc.DialogueHandler is Component handler && handler != null)
        {
            var beside = handler.gameObject.GetComponent(il2cppType);
            if (GameReflection.IsPresent(beside))
            {
                failure = string.Empty;
                return beside;
            }
        }

        var root = npc.GameObject;
        if (root is null)
        {
            failure = "the NPC has no GameObject";
            return null;
        }

        var inChildren = root.GetComponentInChildren(il2cppType, true);
        if (GameReflection.IsPresent(inChildren))
        {
            failure = string.Empty;
            return inChildren;
        }

        failure = "the NPC has no DialogueController, so it has no interaction menu to add to";
        return null;
    }

    /// <summary>A container S1API registered on this NPC, looked up by the name it was built with.</summary>
    internal static object? FindContainer(GameNpc npc, string containerName, out string failure)
    {
        var handler = npc.DialogueHandler;
        if (handler is null)
        {
            failure = "the NPC has no DialogueHandler";
            return null;
        }

        if (!GameReflection.TryRead(handler, "dialogueContainers", out var containers, out failure) || containers is null)
        {
            failure = failure.Length > 0 ? failure : "DialogueHandler.dialogueContainers is null";
            return null;
        }

        foreach (var candidate in GameReflection.Enumerate(containers))
        {
            if (candidate is UnityEngine.Object asset && asset != null && string.Equals(asset.name, containerName, StringComparison.Ordinal))
            {
                failure = string.Empty;
                return candidate;
            }
        }

        failure = $"no dialogue container named '{containerName}' is registered on this NPC";
        return null;
    }

    /// <summary>
    /// Adds a choice that opens <paramref name="container"/>. Returns the choice so it can be taken
    /// back out again when the group leaves.
    /// </summary>
    internal static object? AddChoice(GameNpc npc, string choiceText, object container, int priority, out string failure)
    {
        var controller = FindController(npc, out failure);
        if (controller is null)
            return null;

        var choiceType = GameReflection.FindType(ChoiceType);
        if (choiceType is null)
        {
            failure = $"'{ChoiceType}' not found";
            return null;
        }

        object choice;
        try
        {
            choice = Activator.CreateInstance(choiceType)!;
        }
        catch (Exception ex)
        {
            failure = Describe.Of(ex);
            return null;
        }

        // A controller with dialogue switched off shows the greeting and swallows every choice,
        // which is exactly the dead end this is here to remove.
        GameReflection.TryInvoke(
            controller.GetType(), controller, "SetDialogueEnabled", new object?[] { true }, out _, out _);

        GameReflection.TryWrite(choice, "ChoiceText", choiceText, out _);
        GameReflection.TryWrite(choice, "Enabled", true, out _);
        GameReflection.TryWrite(choice, "ShowWorldspaceDialogue", false, out _);
        GameReflection.TryWrite(choice, "Priority", priority, out _);
        GameReflection.TryWrite(choice, "Conversation", container, out _);

        // The game invokes this unconditionally when the choice is picked; the inspector always
        // supplies one, so a hand-built choice has to as well.
        GameReflection.TryWrite(choice, "onChoosen", new UnityEvent(), out _);

        if (!GameReflection.TryInvoke(
                controller.GetType(), controller, "AddDialogueChoice", new object?[] { choice, priority }, out _, out failure))
        {
            return null;
        }

        failure = string.Empty;
        return choice;
    }

    /// <summary>Takes a previously added choice back out. Disabling first, in case the list refuses.</summary>
    internal static bool RemoveChoice(GameNpc npc, object choice, out string failure)
    {
        GameReflection.TryWrite(choice, "Enabled", false, out _);

        var controller = FindController(npc, out failure);
        if (controller is null)
            return false;

        if (!GameReflection.TryRead(controller, "Choices", out var choices, out failure) || choices is null)
        {
            failure = failure.Length > 0 ? failure : "DialogueController.Choices is null";
            return false;
        }

        return GameReflection.TryInvoke(
            choices.GetType(), choices, "Remove", new[] { choice }, out _, out failure);
    }
}

using System.Reflection;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.Events;

namespace Expansions.HireableDrivers.Game;

/// <summary>
/// The NPC dialogue seam the mod hires through.
/// <para>
/// <c>DialogueController.AddDialogueChoice</c> is the game's own public extension point for the list of
/// options you get when you interact with an NPC — it is how the shipped controllers add their own
/// verbs. Using it means no Harmony patch, no cloned prefab and no guessing at the dialogue graph's
/// node links, which is the part of the hiring flow that cannot be read from the metadata dumps.
/// </para>
/// </summary>
internal static class DialogueApi
{
    /// <summary>
    /// Live <c>DialogueController_Fixer</c> components. This is the controller that carries
    /// <c>selectedEmployeeType</c> and <c>selectedProperty</c>, i.e. the employee-hiring conversation.
    /// </summary>
    internal static IReadOnlyList<object?> HiringControllers()
    {
        var type = Gx.Type(GameTypes.DialogueControllerFixer);
        if (type is null)
            return Array.Empty<object?>();

        try
        {
            var found = UnityEngine.Object.FindObjectsOfType(Il2CppType.From(type));
            var live = new List<object?>(found.Length);
            foreach (var controller in found)
            {
                if (Gx.Alive(controller))
                    live.Add(controller);
            }

            return live;
        }
        catch (Exception ex)
        {
            DriverLog.Warn($"Could not find the hiring NPC ({ex.GetType().Name}); driver hiring falls back to the Expansions menu.");
            return Array.Empty<object?>();
        }
    }

    /// <summary>
    /// The <c>DialogueController</c> on an NPC's own GameObject.
    /// <para>
    /// This is the component the shipped <c>Employee</c> adds "Fire" and "Why aren't you working?" to,
    /// through the same <c>AddDialogueChoice</c> call the mod uses — so a driver's own options sit in
    /// the game's list, in the game's order, with the game's input handling.
    /// </para>
    /// </summary>
    internal static object? ControllerOn(object? npc)
    {
        var type = Gx.Type(GameTypes.DialogueController);
        if (type is null || npc is not Component component || !Gx.Alive(component))
            return null;

        try
        {
            var found = component.GetComponentInChildren(Il2CppType.From(type), true);
            return Gx.Alive(found) ? found : null;
        }
        catch (Exception ex)
        {
            DriverLog.Debug($"Could not reach a DialogueController on an NPC ({Gx.Explain(ex)}).");
            return null;
        }
    }

    /// <summary>The hiring NPC's name, for the "hire them over there" line the mod tells the player.</summary>
    internal static string ControllerName(object? controller)
    {
        var npc = Gx.GetAlive(controller, "npc");

        var full = Gx.Get<string>(npc, "FullName", string.Empty);
        if (!string.IsNullOrWhiteSpace(full))
            return full;

        var first = Gx.Get<string>(npc, "FirstName", string.Empty);
        if (!string.IsNullOrWhiteSpace(first))
            return first;

        return Gx.Get<string>(Gx.GetAlive(npc, "gameObject"), "name", "the employee fixer");
    }

    /// <summary>
    /// Builds a choice and hands it to the controller. <paramref name="shouldShow"/> is re-evaluated by
    /// the game every time the interaction list is built, which is what keeps a per-property entry's
    /// fee and slot count current without a polling loop.
    /// </summary>
    internal static object? AddChoice(
        object? controller,
        string text,
        System.Action onChosen,
        Func<bool, bool> shouldShow,
        int priority)
    {
        if (!Gx.Alive(controller))
            return null;

        var choice = Gx.New(GameTypes.DialogueChoice);
        if (choice is null)
            return null;

        Gx.Set(choice, "ChoiceText", text);
        Gx.Set(choice, "Enabled", true);
        Gx.Set(choice, "Priority", priority);

        // A property name and a price do not belong in a speech bubble over the player's head, and the
        // choice text is already shown in the option list.
        Gx.Set(choice, "ShowWorldspaceDialogue", false);

        // Conversation stays null: this option performs an action rather than opening a dialogue graph,
        // and the mod cannot author the ScriptableObject node links a new branch would need.

        if (!Bind(choice, onChosen) || !Gate(choice, shouldShow))
            return null;

        // AddDialogueChoice is virtual and the Fixer may override it, so resolve against the instance.
        // It returns the new index; a null comes back only when the method could not be resolved.
        var index = Gx.Call(controller, "AddDialogueChoice", new[] { "DialogueChoice", "Int32" }, choice, priority);
        return index is null ? null : choice;
    }

    internal static void RemoveChoice(object? controller, object? choice)
    {
        if (!Gx.Alive(controller) || choice is null)
            return;

        var choices = Gx.Get(controller, "Choices");
        if (choices is null)
            return;

        Gx.Call(choices, "Remove", new[] { Gx.Any }, choice);
    }

    /// <summary>
    /// <c>UnityAction</c> is not a CLR delegate under Il2CppInterop — it is an
    /// <c>Il2CppSystem.MulticastDelegate</c> reached through <c>op_Implicit(System.Action)</c>. The
    /// managed <see cref="System.Action"/> must stay rooted or the interop wrapper is collected and the
    /// click silently stops firing, so callers keep it alive; see <c>HiringDesk</c>.
    /// </summary>
    private static bool Bind(object? choice, System.Action onChosen)
    {
        // The field is Unity-serialized on the shipped prefabs, so an instance built from the bare
        // constructor has no event object to subscribe to until one is put there.
        if (Gx.Get(choice, "onChoosen") is not UnityEvent chosen)
        {
            chosen = new UnityEvent();
            if (!Gx.Set(choice, "onChoosen", chosen))
            {
                DriverLog.Warn("A hiring choice would not take a click handler; driver hiring falls back to the Expansions menu.");
                return false;
            }
        }

        UnityAction listener = onChosen;
        chosen.AddListener(listener);
        return true;
    }

    /// <summary>
    /// <c>ShouldShowCheck</c> lives in <c>Assembly-CSharp</c>, so the conversion has to go through the
    /// generated <c>op_Implicit(Func&lt;bool, bool&gt;)</c> by reflection rather than a cast.
    /// </summary>
    private static bool Gate(object? choice, Func<bool, bool> shouldShow)
    {
        var type = Gx.Type(GameTypes.ShouldShowCheck);
        if (type is null)
            return false;

        var conversion = type
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m =>
                string.Equals(m.Name, "op_Implicit", StringComparison.Ordinal) &&
                m.GetParameters() is [{ ParameterType.IsGenericType: true }]);

        if (conversion is null)
            return false;

        try
        {
            var converted = conversion.Invoke(null, new object?[] { shouldShow });
            return converted is not null && Gx.Set(choice, "shouldShowCheck", converted);
        }
        catch (Exception ex)
        {
            DriverLog.Warn($"Could not attach the availability check to a hiring choice ({Gx.Explain(ex)}).");
            return false;
        }
    }
}

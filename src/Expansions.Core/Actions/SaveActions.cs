using Expansions.Core.Game;

namespace Expansions.Core.Actions;

/// <summary>Deliberate save from the Actions tab — same <c>SaveManager.Save()</c> path as autosave-on-quit.</summary>
internal static class SaveActions
{
    internal static ExpansionAction SaveNow() => new(
        id: "core.save.now",
        label: "Save the game now",
        description: "Writes the loaded save through the game's own SaveManager. Reports the slot and the time.",
        isAvailable: GameSave.Availability,
        invoke: GameSave.SaveNow,
        order: 35);
}

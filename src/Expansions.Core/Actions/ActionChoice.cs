namespace Expansions.Core.Actions;

/// <summary>
/// One entry in an action's picker: what the owner clicks when the action needs a target it cannot
/// guess, like which NPC to teleport to or which probe area to run.
/// </summary>
public sealed class ActionChoice
{
    public ActionChoice(string id, string label, string detail = "", string searchText = "")
    {
        Id = id ?? string.Empty;
        Label = string.IsNullOrWhiteSpace(label) ? Id : label;
        Detail = detail ?? string.Empty;
        // Matching on label plus detail plus id is what lets the owner find Marcus Vale by typing
        // "marcus", "vale", "northtown" or the raw save id.
        SearchText = (string.IsNullOrWhiteSpace(searchText)
            ? $"{Label} {Detail} {Id}"
            : searchText).ToLowerInvariant();
    }

    public string Id { get; }

    public string Label { get; }

    /// <summary>Secondary line, e.g. "Northtown - 84 m away".</summary>
    public string Detail { get; }

    /// <summary>Pre-lowered haystack the picker's search filters against.</summary>
    public string SearchText { get; }
}

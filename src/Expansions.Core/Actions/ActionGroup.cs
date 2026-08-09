namespace Expansions.Core.Actions;

/// <summary>One module's actions under one heading, as the screen lays them out.</summary>
public sealed class ActionGroup
{
    internal ActionGroup(string title, IReadOnlyList<ExpansionAction> actions)
    {
        Title = title;
        Actions = actions;
    }

    public string Title { get; }

    public IReadOnlyList<ExpansionAction> Actions { get; }
}

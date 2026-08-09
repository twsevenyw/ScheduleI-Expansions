namespace Expansions.Core.Events;

/// <summary>One module's triggerable events under one heading, as the chooser lays them out.</summary>
public sealed class EventGroup
{
    internal EventGroup(string title, IReadOnlyList<ExpansionEvent> events)
    {
        Title = title;
        Events = events;
    }

    public string Title { get; }

    public IReadOnlyList<ExpansionEvent> Events { get; }
}

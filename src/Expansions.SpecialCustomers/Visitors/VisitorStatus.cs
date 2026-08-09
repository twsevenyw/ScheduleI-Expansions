using UnityEngine;

namespace Expansions.SpecialCustomers.Visitors;

/// <summary>What the mod knows about one visitor right now. Snapshot, never a live handle.</summary>
internal sealed class VisitorStatus
{
    internal int SlotIndex { get; init; }

    internal string Id { get; init; } = string.Empty;

    internal string FullName { get; init; } = string.Empty;

    /// <summary><c>ConfigurePrefab</c> completed for this slot at least once this session.</summary>
    internal bool PrefabConfigured { get; init; }

    /// <summary><c>OnCreated</c> completed, which is also the proof that <c>Appearance.Build()</c> ran.</summary>
    internal bool Created { get; init; }

    internal bool WrapperResolved { get; init; }

    internal Vector3 Position { get; init; }

    internal Vector3 ConfiguredSpawn { get; init; }

    internal bool IsVisible { get; init; }

    /// <summary>A generated mugshot is what the contacts and messages screens draw.</summary>
    internal bool HasMugshot { get; init; }

    internal string Region { get; init; } = string.Empty;

    internal int FramesWaited { get; init; }

    /// <summary>S1API marked this type finalized (FinalizeNetworkSpawn completed without early exit).</summary>
    internal bool Finalized { get; init; }

    /// <summary>NPCActions graph is safe to tick (umbrella wired or explicitly disabled).</summary>
    internal bool ActionListValid { get; init; }

    internal string IntegritySummary { get; init; } = string.Empty;

    /// <summary>Empty when everything the mod can check came back clean.</summary>
    internal string Failure { get; init; } = string.Empty;

    internal bool IsHealthy =>
        PrefabConfigured && Created && WrapperResolved && ActionListValid && Failure.Length == 0;

    internal string Summary => WrapperResolved
        ? $"{FullName} ({Id}) at {Describe.Of(Position)} in {Region}, " +
          $"finalized={Describe.YesNo(Finalized)}, actions={Describe.YesNo(ActionListValid)}, " +
          $"visible={Describe.YesNo(IsVisible)}, mugshot={Describe.YesNo(HasMugshot)}"
        : $"{Id} is not in the world yet{(Failure.Length > 0 ? " — " + Failure : string.Empty)}";
}

using UnityEngine;

namespace Expansions.Core.Tutorial;

/// <summary>
/// One objective. Becomes a single entry in the chapter's quest, ticked off when
/// <see cref="Condition"/> returns true or when <see cref="TutorialSignals.Raise(string)"/> names it.
/// </summary>
public sealed class TutorialStep
{
    internal TutorialStep(
        string id,
        string title,
        string hint,
        Vector3? poi,
        Func<bool>? condition,
        bool completesOnSignal,
        string rewardDescription,
        Action? reward)
    {
        Id = id;
        Title = title;
        Hint = hint;
        Poi = poi;
        Condition = condition;
        CompletesOnSignal = completesOnSignal;
        RewardDescription = rewardDescription;
        Reward = reward;
    }

    /// <summary>Chapter-qualified and unique: the builder prefixes the chapter id.</summary>
    public string Id { get; }

    /// <summary>The quest entry title. Keep it short and imperative.</summary>
    public string Title { get; }

    /// <summary>Longer explanation for the menu status line and the log. May be empty.</summary>
    public string Hint { get; }

    /// <summary>Optional compass/map marker.</summary>
    public Vector3? Poi { get; }

    public string RewardDescription { get; }

    /// <summary>True when only <see cref="TutorialSignals"/> can complete this step.</summary>
    public bool CompletesOnSignal { get; }

    /// <summary>Polled every frame while the step is live. Null means signal-only.</summary>
    internal Func<bool>? Condition { get; }

    internal Action? Reward { get; }
}

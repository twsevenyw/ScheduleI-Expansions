namespace Expansions.Core.Tutorial;

/// <summary>Where one chapter stands in the line, as the Tutorial tab presents it.</summary>
public enum TutorialChapterState
{
    /// <summary>Waiting its turn.</summary>
    Pending,

    /// <summary>Its quest is live and the player is working through it.</summary>
    Playing,

    /// <summary>Recorded as done on the loaded save.</summary>
    Complete,

    /// <summary>Switched off, so the director steps over it without recording anything.</summary>
    Skipped,
}

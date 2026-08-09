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

    /// <summary>
    /// On, but the thing it teaches is not reachable yet — the mod is off, the module has stood
    /// itself down, or the world is not in the right state. The line plays on past it and comes back
    /// when the chapter says it is ready; it is never ticked off on the player's behalf.
    /// </summary>
    Unavailable,
}

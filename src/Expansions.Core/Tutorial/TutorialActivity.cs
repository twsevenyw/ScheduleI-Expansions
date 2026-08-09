namespace Expansions.Core.Tutorial;

/// <summary>
/// Process-wide counters for the few Core surfaces whose use an objective wants to observe but nothing
/// else can see: a tab being looked at, a button being pressed.
/// <para>
/// Deliberately counters rather than <see cref="TutorialSignals"/>. Signals latch, which is right for a mod's
/// Harmony patch firing at a moment the player is definitely in the chapter, but wrong for a button that
/// sits on the Actions tab from the first frame of the game. A player who pressed 'Check for updates' an
/// hour before the guide chapter started would watch its objective tick itself off on appearance — exactly
/// the fake completion this design is meant to be rid of. A chapter baselines these when it builds, so only
/// presses made while its objective is open count.
/// </para>
/// <para>
/// Only ever touched from the Unity thread (UI clicks and console commands), so no locking.
/// </para>
/// </summary>
internal static class TutorialActivity
{
    internal static int TutorialTabViews { get; private set; }

    internal static int UserDataReveals { get; private set; }

    internal static int UpdateStatusViews { get; private set; }

    internal static void TutorialTabViewed() => TutorialTabViews++;

    internal static void UserDataRevealed() => UserDataReveals++;

    internal static void UpdateStatusViewed() => UpdateStatusViews++;
}

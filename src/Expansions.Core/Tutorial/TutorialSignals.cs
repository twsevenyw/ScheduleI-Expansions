namespace Expansions.Core.Tutorial;

/// <summary>
/// Push-based objective completion, for the case where a Harmony patch already knows the exact moment
/// something happened and polling for it would be wasteful or impossible.
/// <para>
/// Signals latch: raising one for a step that has not started yet is remembered until the step is
/// live, which means a patch does not have to care where the player is in the line.
/// </para>
/// </summary>
public static class TutorialSignals
{
    /// <summary>Stops a misbehaving patch turning a latch into an unbounded leak.</summary>
    private const int MaxLatched = 256;

    private static readonly HashSet<string> Latched = new(StringComparer.Ordinal);
    private static readonly object Gate = new();

    /// <summary>Marks the chapter-qualified <paramref name="stepId"/> complete on the next tick.</summary>
    public static void Raise(string stepId)
    {
        if (string.IsNullOrWhiteSpace(stepId))
            return;

        lock (Gate)
        {
            if (Latched.Count >= MaxLatched && !Latched.Contains(stepId))
            {
                ExpansionHost.Log.Warn(
                    $"Tutorial signal '{stepId}' dropped: {MaxLatched} signals are already waiting, which " +
                    $"means something is raising ids no chapter declares.");
                return;
            }

            Latched.Add(stepId);
        }
    }

    /// <summary>Convenience overload matching <c>ITutorialChapterBuilder.AddStep</c>'s id scoping.</summary>
    public static void Raise(string chapterId, string stepId) => Raise($"{chapterId}.{stepId}");

    internal static bool Consume(string stepId)
    {
        lock (Gate)
            return Latched.Remove(stepId);
    }

    internal static void Clear()
    {
        lock (Gate)
            Latched.Clear();
    }
}

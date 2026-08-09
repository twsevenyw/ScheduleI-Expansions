using Expansions.Core.Logging;

namespace Expansions.Core.Actions;

/// <summary>
/// The on-screen output channel.
/// <para>
/// This exists because the in-game console is not reachable on every save, so anything an action
/// only wrote to a log has, from the owner's side, vanished. Probe summaries, report paths, action
/// results and errors all land here and are rendered in the Actions pane; each line is mirrored to
/// the MelonLoader log so the two never diverge.
/// </para>
/// </summary>
public static class ActionLog
{
    /// <summary>Roughly three screenfuls of the output pane. Older lines fall off the top.</summary>
    private const int Capacity = 60;

    private static readonly ModuleLogger Log = new("Actions");
    private static readonly List<ActionLogLine> Lines = new(Capacity);
    private static readonly object Gate = new();

    private static ActionLogLine[] _snapshot = Array.Empty<ActionLogLine>();
    private static int _revision;

    /// <summary>Fires on every new line, so an open pane repaints.</summary>
    public static event Action? Changed;

    /// <summary>Bumped per line. Cheaper for the UI to compare than the list itself.</summary>
    public static int Revision => Volatile.Read(ref _revision);

    /// <summary>Oldest first. A snapshot, so it is safe to hold while more lines arrive.</summary>
    public static IReadOnlyList<ActionLogLine> Snapshot => _snapshot;

    public static void Ok(string message) => Write(ActionOutcome.Ok, message);

    public static void Note(string message) => Write(ActionOutcome.NoChange, message);

    public static void Fail(string message) => Write(ActionOutcome.Failed, message);

    public static void Write(ActionOutcome outcome, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        var line = new ActionLogLine(outcome, message.Trim(), DateTime.Now);

        lock (Gate)
        {
            if (Lines.Count >= Capacity)
                Lines.RemoveAt(0);

            Lines.Add(line);
            _snapshot = Lines.ToArray();
            Interlocked.Increment(ref _revision);
        }

        switch (outcome)
        {
            case ActionOutcome.Failed:
                Log.Warn(line.Message);
                break;
            default:
                Log.Msg(line.Message);
                break;
        }

        try
        {
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Debug($"An output listener threw ({ex.GetType().Name}: {ex.Message}).");
        }
    }

    public static void Clear()
    {
        lock (Gate)
        {
            Lines.Clear();
            _snapshot = Array.Empty<ActionLogLine>();
            Interlocked.Increment(ref _revision);
        }

        try
        {
            Changed?.Invoke();
        }
        catch
        {
            // Clearing must not be able to fail.
        }
    }
}

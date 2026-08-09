using Expansions.Core.Configuration;

namespace Expansions.Core.Tutorial;

/// <summary>
/// Which tutorial chapters the owner wants played.
/// <para>
/// Chapters are registered at runtime by whichever mods are installed, so the switch cannot be an
/// entry per chapter — a disabled id belonging to a mod that is not loaded this session has to survive
/// the round trip rather than be silently dropped. One comma-separated list in
/// <c>Expansions.cfg</c> does that, and stays hand-editable.
/// </para>
/// <para>
/// Disabled is not the same as complete. The director skips a disabled chapter without recording it,
/// so switching one back on replays it rather than leaving a hole in the line.
/// </para>
/// </summary>
public static class TutorialSettings
{
    private static readonly HashSet<string> Disabled = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    private static bool _attached;
    private static bool _writing;

    /// <summary>Fires when any chapter is switched on or off, including from a file edit.</summary>
    public static event Action? Changed;

    /// <summary>Chapter ids currently switched off, including ids we have no chapter for.</summary>
    public static IReadOnlyCollection<string> DisabledIds
    {
        get
        {
            EnsureLoaded();
            lock (Gate)
                return Disabled.ToArray();
        }
    }

    public static bool IsEnabled(string chapterId)
    {
        if (string.IsNullOrEmpty(chapterId))
            return true;

        EnsureLoaded();

        lock (Gate)
            return !Disabled.Contains(chapterId);
    }

    /// <summary>Registered chapters the line will actually play.</summary>
    public static int EnabledCount()
    {
        var count = 0;
        foreach (var chapter in TutorialRegistry.Chapters)
        {
            if (IsEnabled(chapter.Id))
                count++;
        }

        return count;
    }

    /// <summary>Returns true when the switch actually moved.</summary>
    public static bool SetEnabled(string chapterId, bool enabled)
    {
        if (string.IsNullOrWhiteSpace(chapterId))
            return false;

        EnsureLoaded();

        lock (Gate)
        {
            var moved = enabled ? Disabled.Remove(chapterId) : Disabled.Add(chapterId);
            if (!moved)
                return false;
        }

        Persist();
        return true;
    }

    /// <summary>Switches every registered chapter at once. Ids from absent mods are left alone.</summary>
    public static int SetAll(bool enabled)
    {
        EnsureLoaded();

        var moved = 0;

        lock (Gate)
        {
            foreach (var chapter in TutorialRegistry.Chapters)
            {
                if (enabled ? Disabled.Remove(chapter.Id) : Disabled.Add(chapter.Id))
                    moved++;
            }
        }

        if (moved > 0)
            Persist();

        return moved;
    }

    private static void EnsureLoaded()
    {
        if (_attached)
            return;

        lock (Gate)
        {
            if (_attached)
                return;

            _attached = true;
            Parse(ExpansionConfig.DisabledTutorialChapters);
        }

        // Picks up a hand edit or a third-party settings app writing the same key.
        ExpansionConfig.DisabledTutorialChaptersChanged += OnConfigChanged;
    }

    private static void OnConfigChanged()
    {
        if (_writing)
            return;

        lock (Gate)
            Parse(ExpansionConfig.DisabledTutorialChapters);

        Announce();
    }

    /// <summary>Caller holds <see cref="Gate"/>.</summary>
    private static void Parse(string? value)
    {
        Disabled.Clear();

        if (string.IsNullOrWhiteSpace(value))
            return;

        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var id = part.Trim();
            if (id.Length > 0)
                Disabled.Add(id);
        }
    }

    private static void Persist()
    {
        string text;
        lock (Gate)
        {
            var ids = Disabled.ToArray();
            Array.Sort(ids, StringComparer.OrdinalIgnoreCase);
            text = string.Join(",", ids);
        }

        try
        {
            // The entry raises its changed event synchronously on this stack; the guard stops that
            // re-parsing the value we are in the middle of writing.
            _writing = true;
            ExpansionConfig.DisabledTutorialChapters = text;
        }
        catch (Exception ex)
        {
            ExpansionHost.Log.Warn(
                $"Could not persist the tutorial chapter switches ({ex.GetType().Name}: {ex.Message}); " +
                $"they apply for this session only.");
        }
        finally
        {
            _writing = false;
        }

        Announce();
    }

    private static void Announce()
    {
        try
        {
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            ExpansionHost.Log.Debug($"A tutorial-settings listener threw ({ex.GetType().Name}: {ex.Message}).");
        }
    }
}

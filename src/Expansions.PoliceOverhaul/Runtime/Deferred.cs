using Expansions.Core.Diagnostics;

namespace Expansions.PoliceOverhaul.Runtime;

/// <summary>
/// Work that must not run on the stack that asked for it.
/// <para>
/// The case this exists for is custody. Releasing a player finishes inside a FishNet RPC body, and
/// the day skip has to move the world clock, fire the game's own time-skip events and let the arrest
/// screen finish tearing itself down. Doing that re-entrantly from inside the release call is the
/// kind of thing that works in single-player on a good day and corrupts a co-op session on a bad one.
/// A handful of frames of daylight costs nothing and removes the whole class of problem.
/// </para>
/// </summary>
internal static class Deferred
{
    private static readonly List<Item> Queue = new();

    internal static int Pending => Queue.Count;

    internal static void After(int frames, string what, Action work) =>
        Queue.Add(new Item(Math.Max(1, frames), what, work));

    internal static void Pump()
    {
        if (Queue.Count == 0)
            return;

        for (var i = Queue.Count - 1; i >= 0; i--)
        {
            var item = Queue[i];
            if (--item.Frames > 0)
                continue;

            Queue.RemoveAt(i);

            try
            {
                item.Work();
            }
            catch (Exception ex)
            {
                PoliceLog.Error($"Police Improvements threw while {item.What}; the game carries on.", ex);
            }
        }
    }

    /// <summary>Drops anything still waiting. Called on teardown and on scene unload.</summary>
    internal static void Clear() => Queue.Clear();

    private sealed class Item
    {
        internal Item(int frames, string what, Action work)
        {
            Frames = frames;
            What = what;
            Work = work;
        }

        internal int Frames { get; set; }

        internal string What { get; }

        internal Action Work { get; }
    }
}

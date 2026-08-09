using Expansions.SpecialCustomers.Configuration;
using Expansions.SpecialCustomers.Game;
using Expansions.SpecialCustomers.Visitors;

namespace Expansions.SpecialCustomers.Visits;

/// <summary>
/// Reveals or hides a group one member at a time.
/// <para>
/// Compositing an avatar's layers and instantiating its accessories is the spike, and six of them on
/// one frame is a visible hitch. The warp happens first, while everyone is still hidden, so navmesh
/// work never coincides with mesh work; then one member appears every few frames until the group is
/// whole — about six tenths of a second at the default setting.
/// </para>
/// <para>
/// This is the only thing in the module that consumes the frame pump, and it unsubscribes itself by
/// going idle the moment its queue is empty.
/// </para>
/// </summary>
internal sealed class ArrivalStagger
{
    private readonly Queue<Step> _queue = new();
    private int _framesSinceLast;

    internal bool IsActive => _queue.Count > 0;

    internal int Pending => _queue.Count;

    internal void Reveal(IEnumerable<VisitorSlot> slots) => Enqueue(slots, visible: true);

    internal void Hide(IEnumerable<VisitorSlot> slots) => Enqueue(slots, visible: false);

    /// <summary>Applies a visibility change now, outside the queue. Used by park and unwind.</summary>
    internal static bool SetVisible(VisitorSlot slot, bool visible, out string failure)
    {
        var npc = GameNpc.Resolve(slot.Id, out failure);
        if (npc is null)
            return false;

        // networked: true is not optional — hiding host-side only leaves the group standing in the
        // street on every other client.
        return npc.SetVisible(visible, out failure);
    }

    internal void Pump()
    {
        if (_queue.Count == 0)
            return;

        if (++_framesSinceLast < CustomerSettings.StaggerFrames)
            return;

        _framesSinceLast = 0;

        var step = _queue.Dequeue();
        if (!SetVisible(step.Slot, step.Visible, out var failure))
        {
            VisitorLog.Instance.Warn(
                $"Could not {(step.Visible ? "reveal" : "hide")} visitor slot {step.Slot.Index:00} ({failure}).");
        }
    }

    /// <summary>Applies everything still queued immediately. Used when a visit ends mid-reveal.</summary>
    internal void Drain()
    {
        while (_queue.Count > 0)
        {
            var step = _queue.Dequeue();
            SetVisible(step.Slot, step.Visible, out _);
        }

        _framesSinceLast = 0;
    }

    internal void Clear()
    {
        _queue.Clear();
        _framesSinceLast = 0;
    }

    private void Enqueue(IEnumerable<VisitorSlot> slots, bool visible)
    {
        foreach (var slot in slots)
            _queue.Enqueue(new Step(slot, visible));
    }

    private readonly struct Step
    {
        internal Step(VisitorSlot slot, bool visible)
        {
            Slot = slot;
            Visible = visible;
        }

        internal VisitorSlot Slot { get; }

        internal bool Visible { get; }
    }
}

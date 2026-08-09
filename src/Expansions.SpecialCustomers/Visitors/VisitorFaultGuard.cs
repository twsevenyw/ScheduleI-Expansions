using UnityEngine;

namespace Expansions.SpecialCustomers.Visitors;

/// <summary>
/// Session-level kill switch. Trips only on repeated <b>actual</b> faults (exceptions inside the
/// crash guards, or last-resort destroys). Successful <c>UpdateUmbrellaUse</c> skips are the guard
/// working — they must never count toward a trip.
/// </summary>
internal static class VisitorFaultGuard
{
    private const int TickTripThreshold = 24;
    private const int DestroyTripThreshold = 8;
    private const float WindowSeconds = 2f;

    private static readonly Queue<float> FaultTimes = new();
    private static int _destroys;
    private static bool _tripped;
    private static string _lastTrip = "not tripped";
    private static string _lastDestroy = "none";

    internal static bool IsTripped => _tripped;

    internal static string LastTrip => _lastTrip;

    internal static string LastDestroy => _lastDestroy;

    internal static int Destroys => _destroys;

    internal static void NoteTickFault(string detail)
    {
        if (_tripped)
            return;

        var now = Time.realtimeSinceStartup;
        FaultTimes.Enqueue(now);
        while (FaultTimes.Count > 0 && now - FaultTimes.Peek() > WindowSeconds)
            FaultTimes.Dequeue();

        if (FaultTimes.Count < TickTripThreshold)
            return;

        Trip($"repeated visitor tick faults ({FaultTimes.Count} in {WindowSeconds:0.#}s): {detail}");
    }

    internal static void NoteDestroy(string id, string reason)
    {
        _destroys++;
        _lastDestroy = $"{id}: {reason}";

        if (_tripped)
            return;

        if (_destroys < DestroyTripThreshold)
            return;

        Trip($"destroyed {_destroys} visitors this session — last: {_lastDestroy}");
    }

    internal static void Trip(string reason)
    {
        if (_tripped)
            return;

        _tripped = true;
        _lastTrip = reason;

        VisitorLog.Instance.Error(
            "Special Customers SAFETY TRIP: withdrawing every visitor and disabling visits for this " +
            "session — " + reason);

        try
        {
            VisitorLifecycle.WithdrawAll("safety trip");
        }
        catch (Exception ex)
        {
            VisitorLog.Instance.Warn($"Safety trip withdraw threw: {Describe.Of(ex)}");
        }

        try
        {
            SpecialCustomersModule.Director?.Unwind(silent: true);
        }
        catch (Exception ex)
        {
            VisitorLog.Instance.Warn($"Safety trip director unwind threw: {Describe.Of(ex)}");
        }
    }

    internal static void ResetSession()
    {
        FaultTimes.Clear();
        _destroys = 0;
        _tripped = false;
        _lastTrip = "not tripped";
        _lastDestroy = "none";
    }
}

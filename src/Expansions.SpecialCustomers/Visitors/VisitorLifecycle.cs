using S1API.Entities;

namespace Expansions.SpecialCustomers.Visitors;

/// <summary>
/// Shared post-spawn path for every <c>SpecialVisitorNN</c>. Runs from <c>OnCreated</c> (S1API's
/// schedule — including while the module toggle is off). Keeps the visitor alive; the umbrella /
/// SetVisible Harmony guards neuter the crash. Destroy is only when the fault breaker has already
/// withdrawn the feature for the session.
/// </summary>
internal static class VisitorLifecycle
{
    internal static void FinishCreate(NPC npc, int slotIndex)
    {
        try
        {
            if (VisitorFaultGuard.IsTripped)
            {
                VisitorIntegrity.DestroyCompletely(npc, "fault guard already tripped");
                VisitorRuntime.NoteSpawnRejected(slotIndex, VisitorFaultGuard.LastTrip);
                return;
            }

            // Required, not cosmetic: without it the avatar is left half-applied and no mugshot is
            // ever generated, so the contacts and messages screens fall back to a blank portrait.
            try
            {
                npc.Appearance.Build();
            }
            catch (Exception ex)
            {
                VisitorLog.Instance.Warn(
                    $"Appearance.Build failed for slot {slotIndex:00} ({Describe.Of(ex)}); continuing.");
            }

            SpawnGraphFix.EnsureSpawnHierarchyActive(npc.gameObject);
            var report = VisitorIntegrity.HealAndValidate(npc);
            VisitorRuntime.NoteIntegrity(slotIndex, report);

            if (!report.ActionListValid)
            {
                VisitorLog.Instance.Warn(
                    $"Visitor slot {slotIndex:00} tick-unsafe after OnCreated ({report.Summary}); " +
                    "keeping alive — UpdateUmbrellaUse guard absorbs until healed.");
            }

            // Always register the live wrapper. Soft gaps / failed finalize SetVisible must not
            // erase a spawn that previously worked (slot 01).
            VisitorRuntime.NoteCreated(slotIndex, npc);

            if (VisitorSlot.Find(slotIndex) is { IsResidentScout: true } &&
                VisitorIntegrity.CanSafelySetVisible(npc))
            {
                try
                {
                    var native = VisitorIntegrity.NativeOf(npc);
                    if (native is not null)
                    {
                        Expansions.Core.Diagnostics.GameReflection.TryInvoke(
                            native.GetType(), native, "SetVisible", new object?[] { true, true }, out _, out _);
                    }
                }
                catch (Exception ex)
                {
                    VisitorLog.Instance.Debug($"Scout SetVisible: {Describe.Of(ex)}");
                }
            }
        }
        catch (Exception ex)
        {
            VisitorRuntime.NoteCreationFailure(slotIndex, ex);
            // Keep the NPC. The always-on umbrella/SetVisible guards are the crash neuter; destroying
            // here is what wiped the pool after the over-strict integrity check.
            try
            {
                VisitorRuntime.NoteCreated(slotIndex, npc);
            }
            catch
            {
                // Best-effort bookkeeping.
            }
        }
    }

    /// <summary>Destroy every live visitor wrapper we still hold. Used by the fault guard.</summary>
    internal static void WithdrawAll(string reason)
    {
        foreach (var slot in VisitorSlot.All)
        {
            var npc = VisitorRuntime.Resolve(slot);
            if (npc is null)
                continue;

            try
            {
                VisitorIntegrity.DestroyCompletely(npc, reason);
                VisitorRuntime.NoteSpawnRejected(slot.Index, reason);
            }
            catch (Exception ex)
            {
                VisitorLog.Instance.Warn(
                    $"WithdrawAll could not destroy slot {slot.Index:00}: {Describe.Of(ex)}");
            }
        }

        VisitorRuntime.ForgetLiveWrappers();
    }
}

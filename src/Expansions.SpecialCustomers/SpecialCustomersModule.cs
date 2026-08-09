using Expansions.Core;
using Expansions.Core.Actions;
using Expansions.Core.Diagnostics;
using Expansions.Core.Tutorial;
using Expansions.SpecialCustomers.Configuration;
using Expansions.SpecialCustomers.Detection;
using Expansions.SpecialCustomers.Diagnostics;
using Expansions.SpecialCustomers.Menu;
using Expansions.SpecialCustomers.Persistence;
using Expansions.SpecialCustomers.Tutorial;
using Expansions.SpecialCustomers.Visitors;
using Expansions.SpecialCustomers.Visits;

namespace Expansions.SpecialCustomers;

/// <summary>
/// Special customer groups — bikers, businessmen, hippies and a rock band — who roll into Hyland
/// Point every couple of days, park themselves in one district for a day, and buy in bulk.
/// <para>
/// The mod authors exactly one thing: the offer. Everything after it is the game's own contract,
/// handover and payment pipeline, which is why the money, XP, quality matching and analytics
/// receipts are real and why the core loop needs no Harmony patches.
/// </para>
/// </summary>
public sealed class SpecialCustomersModule : ExpansionModule
{
    public const string ModuleId = "special_customers";

    private readonly VisitDirector _director = new();

    private IDisposable? _chapter;
    private Action? _poolSettledHandler;
    private bool _detectionEvaluated;

    public override string Id => ModuleId;

    /// <summary>
    /// Computed rather than constant so the Expansions screen explains a self-disable without the
    /// player having to open a log.
    /// </summary>
    public override string DisplayName =>
        OfficialFeatureDetector.Verdict.IsEnabled
            ? "Special Customers"
            : "Special Customers (standing down — see Show detection score)";

    public override string Description =>
        "Special customer groups (bikers, hippies, businessmen, etc.) who periodically visit town and buy large quantities of product.";

    public override string Version => "1.0.0";

    /// <summary>The live director, for the console command. Null while the module is disabled.</summary>
    internal static VisitDirector? Director { get; private set; }

    protected override void OnRegistered()
    {
        // Runs once, before the first enable and before any save loads. The prefab pass reads these
        // through VisitorSettings, which is why they cannot wait for OnEnabled.
        VisitorSettings.Bind(Config);
        CustomerSettings.Bind(Config);

        // Crash guards must land even when the module toggle is off — S1API still constructs every
        // SpecialVisitorNN from this assembly, and a half-built NPC kills the process either way.
        Try("patching spawn-graph integrity (always-on)", () => SpawnGraphFix.Apply());

        Log.Debug("Registered.");
    }

    public override void OnEnabled()
    {
        // A failure anywhere below must cost this module and nothing else: Core and the other two
        // mods share this process, and the visitor NPCs are S1API's to build either way.
        Try("hooking prefab pre-registration", () =>
        {
            Lifetime.OnDispose(VisitorPreRegistration.Disarm);
            VisitorPreRegistration.EnsurePatched(Harmony);
        });

        // Idempotent re-apply (and reset the session fault guard for a fresh enable).
        Try("patching spawn-graph integrity", () =>
        {
            Lifetime.OnDispose(VisitorFaultGuard.ResetSession);
            VisitorFaultGuard.ResetSession();
            SpawnGraphFix.Apply();
        });

        Try("patching the member allow-list gate", () =>
        {
            Lifetime.OnDispose(VisitProductGate.Disarm);
            VisitProductGate.Apply(Harmony);
        });

        Try("attaching the visitor runtime", () => Lifetime.OnDispose(VisitorRuntime.Attach()));

        Try("attaching the visit director", () =>
        {
            Director = _director;
            Lifetime.OnDispose(() => Director = null);
            Lifetime.OnDispose(_director.Attach());
        });

        Try("registering probes", () =>
        {
            foreach (var probe in VisitorProbes.All(_director))
                Lifetime.Add(ProbeRegistry.Register(probe));
        });

        Try("registering the tutorial chapter", () =>
        {
            Lifetime.OnDispose(WithdrawChapter);

            // Re-enabling after a stand-down must not put an uncompletable chapter back into the
            // quest line: the detector's verdict is per-session, so it is still the right answer.
            var verdict = OfficialFeatureDetector.Verdict;
            if (!verdict.IsEnabled)
            {
                Log.Msg($"The Special Customers tutorial chapter is not registered: {verdict.Reason}");
                return;
            }

            _chapter = TutorialRegistry.Register(new CustomersChapter(_director));
        });

        Try("registering menu actions", () =>
            Lifetime.Add(ActionRegistry.RegisterAll(CustomerActions.Build(_director).ToArray())));

        Try("reporting the product allow-list", () =>
        {
            _poolSettledHandler = ReportAllowList;
            VisitorRuntime.PoolSettled += _poolSettledHandler;
            Lifetime.OnDispose(() =>
            {
                if (_poolSettledHandler is null)
                    return;

                VisitorRuntime.PoolSettled -= _poolSettledHandler;
                _poolSettledHandler = null;
            });
        });

        Log.Debug($"Enabled with {VisitorSlot.Count} visitor slot(s): {string.Join(", ", VisitorRoster.PrefabNames)}.");
    }

    /// <summary>
    /// Everything reversible is registered with <see cref="ExpansionModule.Lifetime"/>, so there is
    /// nothing to do here. The visitor NPCs are the deliberate exception: S1API builds them from the
    /// types in this assembly during save load, before any toggle is read, so disabling the module
    /// stops what the mod <i>does</i> without removing what S1API already created. Deleting the DLL
    /// is what removes them, and that orphans their folders in saves that stored them — disable, do
    /// not delete.
    /// </summary>
    public override void OnDisabled()
    {
    }

    public override void OnUpdate()
    {
        VisitorRuntime.Pump();
        _director.Pump();
    }

    public override void OnSceneLoaded(int buildIndex, string sceneName)
    {
        // Second and last attempt at the hook. Enabling happens at melon init, which is early enough
        // that the interop assembly may not have been resolvable yet; by the first scene it is.
        Try("hooking prefab pre-registration", () => VisitorPreRegistration.EnsurePatched(Harmony));
        Try("patching spawn-graph integrity", () => SpawnGraphFix.Apply(Harmony));

        VisitorRuntime.OnSceneChanged(sceneName);

        // A new scene means new Customer and DialogueController components, so both the once-per-
        // session dialogue rebuild and the choice bindings have to be armed again.
        CustomerTuner.ForgetDialogueSetup();
        VisitorDialogue.OnSceneChanged();

        // The schedules and the product registry both belong to the scene being left.
        PostWatch.OnSceneChanged();
        ProductAllowList.Invalidate();

        if (buildIndex == 1 || string.Equals(sceneName, "Main", StringComparison.Ordinal))
            Try("evaluating official-feature detection", EvaluateDetection);
    }

    /// <summary>
    /// One evaluation per session, on the first gameplay scene — the game version cannot change
    /// mid-session, and re-probing per day would risk a group half-arriving on a flip-flop.
    /// <para>
    /// A positive verdict does not touch the persisted module toggle. It makes the module inert: the
    /// director refuses to start visits, any group already in town is unwound silently, and the
    /// reason is published to the log, to the config and to the module's own display name.
    /// </para>
    /// </summary>
    private void EvaluateDetection()
    {
        if (_detectionEvaluated)
            return;

        _detectionEvaluated = true;

        var saved = SpecialCustomerState.Snapshot();
        var verdict = OfficialFeatureDetector.Evaluate(saved.SelfDisabledByDetection, saved.SelfDisableReason);
        OfficialFeatureDetector.Report(verdict);

        if (verdict.IsEnabled)
            return;

        // Sticky for this save, so a rolled-back game version cannot make a group half-arrive.
        if (verdict.Outcome != DetectionOutcome.DisabledByUser)
            _director.RecordSelfDisable(verdict.Reason);

        _director.Unwind(silent: true);

        // No group will ever arrive, so the chapter's objectives are unreachable. It is withdrawn
        // rather than left in the quest line as a note the player cannot act on; the reason is on
        // the module card, in the config, in the log and behind "Show detection score".
        WithdrawChapter();
        Log.Msg($"The Special Customers tutorial chapter was withdrawn: {verdict.Reason}");
    }

    private void WithdrawChapter()
    {
        var chapter = Interlocked.Exchange(ref _chapter, null);
        chapter?.Dispose();
    }

    /// <summary>
    /// Prints the resolved product allow-list once per save load, so it is obvious which names
    /// matched a real definition — including definitions other mods created.
    /// </summary>
    private void ReportAllowList()
    {
        try
        {
            ProductAllowList.Report(ProductAllowList.Current(forceRefresh: true));
        }
        catch (Exception ex)
        {
            Log.Error("Reporting the product allow-list failed; groups will still resolve it when they order.", ex);
        }
    }

    private void Try(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Error($"Special Customers failed while {what}; the rest of the module carries on.", ex);
        }
    }
}

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
            Lifetime.Add(TutorialRegistry.Register(new CustomersChapter(_director))));

        Try("registering menu actions", () =>
            Lifetime.Add(ActionRegistry.RegisterAll(CustomerActions.Build(_director).ToArray())));

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

        VisitorRuntime.OnSceneChanged(sceneName);

        // A new scene means new Customer and DialogueController components, so both the once-per-
        // session dialogue rebuild and the choice bindings have to be armed again.
        CustomerTuner.ForgetDialogueSetup();
        VisitorDialogue.OnSceneChanged();

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

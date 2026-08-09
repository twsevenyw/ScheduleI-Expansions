using S1API.Internal.Abstraction;
using S1API.Saveables;

namespace Expansions.SpecialCustomers.Persistence;

/// <summary>
/// The module's per-save blob.
/// <para>
/// S1API discovers direct <see cref="Saveable"/> inheritors by reflection and constructs exactly one
/// of each, so this type is never instantiated by the module — it publishes itself through
/// <see cref="Current"/> instead. That also means the public parameterless constructor is load
/// bearing and must not be tidied away.
/// </para>
/// <para>
/// It writes to <c>&lt;save&gt;/Modded/Saveables/</c>, which vanilla never reads, so an orphaned file
/// after an uninstall is inert.
/// </para>
/// </summary>
public sealed class SpecialCustomerState : Saveable
{
    [SaveableField("special_customers")]
    private VisitStateData _state = new();

    public SpecialCustomerState() => Current = this;

    /// <summary>Raised after S1API has finished reading the blob for the loaded save.</summary>
    internal static event Action<VisitStateData>? Loaded;

    internal static SpecialCustomerState? Current { get; private set; }

    /// <summary>Loads after the base game so the NPCs, map and economy are already up.</summary>
    public override SaveableLoadOrder LoadOrder => SaveableLoadOrder.AfterBaseGame;

    internal VisitStateData State => _state;

    /// <summary>The blob for the loaded save, or a fresh one when no save has been read yet.</summary>
    internal static VisitStateData Snapshot() => Current?._state ?? new VisitStateData();

    protected override void OnLoaded()
    {
        base.OnLoaded();

        try
        {
            _state ??= new VisitStateData();

            // A payload written by a newer build is discarded rather than half-read. Partially
            // deserialising a group that no longer means what it used to would leave visitors
            // dressed, tuned and unreachable.
            if (_state.SchemaVersion > VisitStateData.CurrentSchemaVersion)
            {
                VisitorLog.Instance.Warn(
                    $"This save carries Special Customers data written by a newer version " +
                    $"(schema {_state.SchemaVersion} > {VisitStateData.CurrentSchemaVersion}); starting clean rather than guessing at it.");
                _state = new VisitStateData();
            }

            _state.SchemaVersion = VisitStateData.CurrentSchemaVersion;
            _state.LastArchetypeId ??= string.Empty;
            _state.SelfDisableReason ??= string.Empty;

            Loaded?.Invoke(_state);
        }
        catch (Exception ex)
        {
            VisitorLog.Instance.Error("Reading the Special Customers save blob failed; this save starts with no group.", ex);
            _state = new VisitStateData();
        }
    }
}

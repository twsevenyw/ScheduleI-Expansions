using S1API.Entities;

namespace Expansions.SpecialCustomers.Visitors;

/// <summary>
/// The first member of the special-customer visitor pool, and the one who stays in town.
/// <para>
/// S1API discovers this by scanning loaded assemblies for <see cref="NPC"/> subclasses and derives
/// the FishNet spawnable prefab name from the <b>simple type name</b> (<c>S1API_SpecialVisitor01</c>),
/// so the name is save data and co-op wire data at once and must not be renamed. Zero-padded so the
/// ordinal sort S1API applies to type full names is also numeric across all eight slots.
/// </para>
/// <para>
/// Once this assembly is present the NPC exists whether or not the module is enabled — S1API builds
/// it during save load, before any module toggle is consulted. Disabling the module therefore stops
/// everything this mod <i>does</i>, not the visitor's existence; that needs the DLL removed, and
/// removing it orphans the visitor's folder in saves that already stored it.
/// </para>
/// </summary>
public sealed class SpecialVisitor01 : NPC
{
    /// <summary>Must be a constant: <see cref="ConfigurePrefab"/> runs before any field is set.</summary>
    private const int SlotIndex = 1;

    public override bool IsPhysical => true;

    protected override void ConfigurePrefab(NPCPrefabBuilder builder) =>
        VisitorPrefab.Configure(builder, SlotIndex);

    protected override void OnCreated()
    {
        base.OnCreated();

        try
        {
            // Required, not cosmetic: without it the avatar is left half-applied and no mugshot is
            // ever generated, so the contacts and messages screens fall back to a blank portrait.
            Appearance.Build();

            VisitorRuntime.NoteCreated(SlotIndex, this);
        }
        catch (Exception ex)
        {
            VisitorRuntime.NoteCreationFailure(SlotIndex, ex);
        }
    }
}

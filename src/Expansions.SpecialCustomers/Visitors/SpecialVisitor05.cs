using S1API.Entities;

namespace Expansions.SpecialCustomers.Visitors;

/// <summary>
/// Pool member 05. See <see cref="SpecialVisitor01"/> for why each slot is its own type and why the
/// simple type name is save and wire data that must not be renamed.
/// </summary>
public sealed class SpecialVisitor05 : NPC
{
    private const int SlotIndex = 5;

    public override bool IsPhysical => true;

    protected override void ConfigurePrefab(NPCPrefabBuilder builder) =>
        VisitorPrefab.Configure(builder, SlotIndex);

    protected override void OnCreated()
    {
        base.OnCreated();

        try
        {
            Appearance.Build();
            VisitorRuntime.NoteCreated(SlotIndex, this);
        }
        catch (Exception ex)
        {
            VisitorRuntime.NoteCreationFailure(SlotIndex, ex);
        }
    }
}

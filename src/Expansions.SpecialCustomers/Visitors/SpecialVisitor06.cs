using S1API.Entities;

namespace Expansions.SpecialCustomers.Visitors;

/// <summary>
/// Pool member 06. See <see cref="SpecialVisitor01"/> for why each slot is its own type and why the
/// simple type name is save and wire data that must not be renamed.
/// </summary>
public sealed class SpecialVisitor06 : NPC
{
    private const int SlotIndex = 6;

    public override bool IsPhysical => true;

    protected override void ConfigurePrefab(NPCPrefabBuilder builder) =>
        VisitorPrefab.Configure(builder, SlotIndex);

    protected override void OnCreated()
    {
        base.OnCreated();
        VisitorLifecycle.FinishCreate(this, SlotIndex);
    }
}

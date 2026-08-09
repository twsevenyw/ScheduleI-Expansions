using System.Globalization;
using Expansions.SpecialCustomers.Archetypes;
using S1API.Map;
using UnityEngine;

namespace Expansions.SpecialCustomers.Visitors;

/// <summary>
/// The fixed, per-slot half of a visitor's identity and neutral look.
/// <para>
/// Everything here is static data. S1API calls <c>ConfigurePrefab</c> on an <b>uninitialized</b>
/// instance, so the slot's values can never come from a constructor or an instance field — the slot
/// index has to be a compile-time constant on the subclass and everything else has to hang off it
/// through a static lookup.
/// </para>
/// <para>
/// Eight slots, re-dressed per archetype, rather than one NPC type per archetype member. Five
/// archetypes of six would be thirty permanently networked NPCs on top of the game's eighty; eight
/// is a tenth of that, and re-dressing is how the game builds its own cartel goons.
/// </para>
/// </summary>
internal sealed class VisitorSlot
{
    /// <summary>
    /// Save-data key prefix. Treated as immutable: changing it renames the NPC as far as the save is
    /// concerned and orphans the folder written under the old id.
    /// </summary>
    private const string IdPrefix = "expansions_sc_visitor_";

    private static readonly VisitorSlot[] Slots =
    {
        // Slot 01 is the group's advance scout and the only member who stays in town between visits.
        // He predates the pool, he is already in the owner's save, and the menu's teleport action
        // points at him — so his identity, impostor and post are fixed.
        new(1, "Marcus", "Vale", "Austin", "monotone", 1f, 0f, 1f, 0.45f, new Color32(150, 120, 95, 255),
            AvatarAssets.Hair.Peaked, new Color(0.14f, 0.12f, 0.11f), Vector3.zero, residentScout: true),
        new(2, "Dana", "Roscoe", string.Empty, "female1", 1f, 1f, 0.97f, 0.5f, new Color32(206, 168, 136, 255),
            AvatarAssets.Hair.MessyBob, new Color(0.28f, 0.18f, 0.11f), new Vector3(1.4f, 0f, 0.6f)),
        new(3, "Wes", "Kohler", string.Empty, "redneck", 0.95f, 0f, 1.05f, 0.62f, new Color32(178, 137, 104, 255),
            AvatarAssets.Hair.BuzzCut, new Color(0.11f, 0.1f, 0.09f), new Vector3(2.8f, 0f, 0.2f)),
        new(4, "Priya", "Nandal", string.Empty, "female2", 1.03f, 1f, 0.95f, 0.42f, new Color32(148, 108, 80, 255),
            AvatarAssets.Hair.ShoulderLength, new Color(0.09f, 0.08f, 0.08f), new Vector3(4.2f, 0f, 0.8f)),
        new(5, "Otto", "Brandt", string.Empty, "cold", 0.98f, 0f, 1.02f, 0.55f, new Color32(232, 197, 168, 255),
            AvatarAssets.Hair.Spiky, new Color(0.35f, 0.24f, 0.12f), new Vector3(5.6f, 0f, 0.3f)),
        new(6, "Camille", "Oduya", string.Empty, "female1", 1.05f, 1f, 0.99f, 0.47f, new Color32(110, 78, 56, 255),
            AvatarAssets.Hair.Afro, new Color(0.08f, 0.07f, 0.07f), new Vector3(7f, 0f, 0.9f)),
        new(7, "Silas", "Boone", string.Empty, "tyler", 1f, 0f, 1.08f, 0.68f, new Color32(76, 53, 39, 255),
            AvatarAssets.Hair.LongCurly, new Color(0.07f, 0.06f, 0.06f), new Vector3(8.4f, 0f, 0.4f)),
        new(8, "Nora", "Vasilenko", string.Empty, "timid", 1.02f, 1f, 0.96f, 0.4f, new Color32(207, 168, 134, 255),
            AvatarAssets.Hair.MidFringe, new Color(0.52f, 0.4f, 0.2f), new Vector3(9.8f, 0f, 0.7f)),
    };

    private VisitorSlot(
        int index,
        string firstName,
        string lastName,
        string impostorName,
        string voiceId,
        float voicePitch,
        float gender,
        float height,
        float weight,
        Color32 skinColor,
        string hairPath,
        Color hairColor,
        Vector3 spawnOffset,
        bool residentScout = false)
    {
        Index = index;
        Id = IdPrefix + index.ToString("00", CultureInfo.InvariantCulture);
        FirstName = firstName;
        LastName = lastName;
        ImpostorName = impostorName;
        VoiceId = voiceId;
        VoicePitch = voicePitch;
        Gender = gender;
        Height = height;
        Weight = weight;
        SkinColor = skinColor;
        HairPath = hairPath;
        HairColor = hairColor;
        SpawnOffset = spawnOffset;
        IsResidentScout = residentScout;
    }

    internal int Index { get; }

    internal string Id { get; }

    internal string FirstName { get; }

    internal string LastName { get; }

    internal string FullName => FirstName + " " + LastName;

    /// <summary>
    /// Name of a shipped impostor billboard, resolved by S1API from <c>charactersettings/&lt;name&gt;</c>.
    /// Empty means "pick one deterministically from the catalogue by slot index", which cannot be a
    /// typo. Mandatory either way: vanilla swaps every NPC to its billboard past ~50 m, and a
    /// runtime-built avatar carries no impostor of its own, so without one the visitor is a blank
    /// card at range.
    /// </summary>
    internal string ImpostorName { get; }

    internal string VoiceId { get; }

    internal float VoicePitch { get; }

    internal float Gender { get; }

    internal float Height { get; }

    internal float Weight { get; }

    internal Color32 SkinColor { get; }

    internal string HairPath { get; }

    internal Color HairColor { get; }

    /// <summary>Keeps parked pool members from stacking on one point.</summary>
    internal Vector3 SpawnOffset { get; }

    /// <summary>
    /// True for the one member who stays in town, visible, when no group is visiting. Everyone else
    /// is hidden and parked, because eight idle networked strangers is a cost with no payoff.
    /// </summary>
    internal bool IsResidentScout { get; }

    internal Region Region => Region.Northtown;

    internal static int Count => Slots.Length;

    internal static IReadOnlyList<VisitorSlot> All => Slots;

    internal Vector3 SpawnPosition => VisitorSettings.SpawnOrigin + SpawnOffset;

    /// <summary>Null for an index with no slot, so a bad constant surfaces as a logged skip.</summary>
    internal static VisitorSlot? Find(int index)
    {
        foreach (var slot in Slots)
        {
            if (slot.Index == index)
                return slot;
        }

        return null;
    }

    internal static VisitorSlot Primary => Slots[0];

    /// <summary>Neutral grey civilian clothing, worn whenever no group is in town.</summary>
    internal static Color NeutralGrey { get; } = new(0.42f, 0.43f, 0.45f);

    internal static Color NeutralDenim { get; } = new(0.19f, 0.23f, 0.31f);

    internal static string ShirtPath => AvatarAssets.Body.TShirt;

    internal static string PantsPath => AvatarAssets.Body.Jeans;

    internal static string ShoesPath => AvatarAssets.Accessory.Sneakers;

    internal static string FacePath => AvatarAssets.Face.Neutral;
}

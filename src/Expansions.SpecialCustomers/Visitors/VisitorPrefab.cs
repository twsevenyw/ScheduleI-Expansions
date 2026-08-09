using S1API.Economy;
using S1API.Entities;
using UnityEngine;

namespace Expansions.SpecialCustomers.Visitors;

/// <summary>
/// The persistent half of a visitor, shared by every <c>SpecialVisitorNN</c> so the subclasses stay
/// declarations rather than code.
/// <para>
/// This runs on an <b>uninitialized</b> instance during S1API's prefab pass. It may read constants
/// and statics and nothing else, and it must not throw: S1API swallows the exception and the type
/// silently loses its prefab, which shows up as an NPC that simply never appears.
/// </para>
/// </summary>
internal static class VisitorPrefab
{
    internal static void Configure(NPCPrefabBuilder builder, int slotIndex)
    {
        try
        {
            var slot = VisitorSlot.Find(slotIndex);
            if (slot is null)
            {
                VisitorLog.Instance.Error(
                    $"No visitor slot {slotIndex} is defined; its prefab will be left unconfigured and the NPC will not appear.");
                return;
            }

            Apply(builder, slot);
            VisitorRuntime.NoteConfigured(slot);
        }
        catch (Exception ex)
        {
            // Never let this reach S1API: a throw here costs the prefab, and a missing prefab costs
            // the NPC for the rest of the session.
            VisitorLog.Instance.Error(
                $"Configuring the visitor prefab for slot {slotIndex} failed; that visitor will not appear this session.",
                ex);
        }
    }

    private static void Apply(NPCPrefabBuilder builder, VisitorSlot slot)
    {
        ApplyVoice(builder, slot);

        builder
            .WithIdentity(slot.Id, slot.FirstName, slot.LastName)
            .WithRegion(slot.Region)
            .WithSpawnPosition(slot.SpawnPosition, Quaternion.identity)
            .WithAppearanceDefaults(avatar =>
            {
                avatar.Gender = slot.Gender;
                avatar.Height = slot.Height;
                avatar.Weight = slot.Weight;
                avatar.SkinColor = slot.SkinColor;
                avatar.LeftEyeLidColor = slot.SkinColor;
                avatar.RightEyeLidColor = slot.SkinColor;
                avatar.EyeBallTint = Color.white;
                avatar.PupilDilation = 0.65f;
                avatar.EyebrowScale = 0.85f;
                avatar.EyebrowThickness = 0.6f;
                avatar.HairPath = slot.HairPath;
                avatar.HairColor = slot.HairColor;

                avatar.WithFaceLayer(VisitorSlot.FacePath, Color.black);
                avatar.WithBodyLayer(VisitorSlot.ShirtPath, VisitorSlot.NeutralGrey);
                avatar.WithBodyLayer(VisitorSlot.PantsPath, VisitorSlot.NeutralDenim);
                avatar.WithAccessoryLayer(VisitorSlot.ShoesPath, VisitorSlot.NeutralGrey);

                ApplyImpostor(avatar, slot);
            })
            // Deliberate: adding the Customer before S1API network-spawns the prefab sidesteps the
            // open question of whether a Customer attached after the spawn replicates at all.
            .EnsureCustomer()
            .WithCustomerDefaults(customer => customer
                // Zeroed until a visit tunes them up. A parked visitor must be provably inert, not
                // merely invisible: a hidden customer still ticks, and a hidden customer that placed
                // an order would put an unreachable deal on the player's phone.
                .WithSpending(0f, 0f)
                .WithOrdersPerWeek(0, 0)
                .WithStandards(CustomerStandard.VeryLow)
                .AllowDirectApproach(false)
                .GuaranteeFirstSample(false)
                .WithCallPoliceChance(0f)
                .WithDependence(0f, 0f)
                .WithMutualRelationRequirement(0f, 0f));
    }

    /// <summary>
    /// Non-negotiable. Vanilla swaps to the billboard impostor past ~50 m whether or not one was
    /// baked, so an NPC without this reads as a blank white card at range.
    /// <para>
    /// Always a <b>named</b> catalogue entry from <see cref="VisitorSlot.ImpostorName"/>. Prefab-time
    /// <c>GetRandom</c>/<c>WithRandomImpostor</c> left Avatar inactive and S1API refused the spawn
    /// with <c>Avatar(active)</c> for every slot that used them; named <c>WithImpostor</c> (as slot 01
    /// always did with Austin) does not.
    /// </para>
    /// </summary>
    private static void ApplyImpostor(NPCPrefabBuilder.AvatarDefaultsBuilder avatar, VisitorSlot slot)
    {
        if (slot.ImpostorName.Length == 0)
        {
            VisitorLog.Instance.Error(
                $"Slot {slot.Index:00} ({slot.Id}) has no ImpostorName; refusing to call WithRandomImpostor " +
                "because that path leaves Avatar inactive and S1API drops the spawn.");
            return;
        }

        avatar.WithImpostor(slot.ImpostorName);
    }

    /// <summary>
    /// Isolated because it is the one builder call documented to throw: an unrecognised voice id or
    /// an out-of-range pitch is a configuration error, and losing the whole prefab — and with it the
    /// NPC — over the wrong greeting samples would be a bad trade.
    /// </summary>
    private static void ApplyVoice(NPCPrefabBuilder builder, VisitorSlot slot)
    {
        try
        {
            builder.WithVoice(slot.VoiceId, slot.VoicePitch);
        }
        catch (Exception ex)
        {
            VisitorLog.Instance.Warn(
                $"Voice '{slot.VoiceId}' was rejected for {slot.Id} ({Describe.Of(ex)}); it keeps the base prefab's voice.");
        }
    }
}

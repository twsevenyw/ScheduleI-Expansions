using Expansions.Core.Diagnostics;
using Expansions.SpecialCustomers.Archetypes;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace Expansions.SpecialCustomers.Game;

/// <summary>
/// Builds a complete <c>AvatarSettings</c> for one archetype member by cloning the visitor's own
/// settings and replacing the layer lists wholesale.
/// <para>
/// Cloning rather than editing in place is what keeps the baked distance impostor: the texture is a
/// field on the asset, and <c>Instantiate</c> carries it across. Replacing rather than appending is
/// what keeps a pool member that has been re-dressed four times from overflowing the game's 6 face /
/// 8 body / 9 accessory caps.
/// </para>
/// <para>
/// Morph fields are clamped to the observed ranges of the 117 readable shipped NPCs in the avatar
/// dump. Layer rows are written by get→modify→set on the donor's existing
/// <c>LayerSetting</c> structs — they are IL2CPP value types, and <c>Activator.CreateInstance</c>
/// plus <c>List.Add</c> was handing the avatar garbage rows (writes of Gender/Hair "succeeded"
/// while the mesh stretched across the screen).
/// </para>
/// </summary>
internal static class AvatarSettingsFactory
{
    private const int FaceBodySlots = 6;

    /// <summary>
    /// Duplicates a live settings asset. Marked <c>HideAndDontSave</c> so Unity does not collect it
    /// on the next scene load — an archetype look outlives the scene it was built in.
    /// </summary>
    internal static UnityObject? Clone(object donor, out string failure)
    {
        var typedDonor = InteropCast.As(donor, GameTypes.AvatarSettings);
        if (typedDonor is not UnityObject unityDonor || unityDonor == null)
        {
            failure =
                "the donor settings asset is null, destroyed, or an untyped UnityEngine.Object wrapper " +
                "(cast to AvatarSettings failed)";
            return null;
        }

        try
        {
            // Instantiate(Object) returns a bare Object wrapper — members live on AvatarSettings.
            var raw = UnityObject.Instantiate(unityDonor);
            var typed = InteropCast.As(raw, GameTypes.AvatarSettings);
            if (typed is not UnityObject clone || clone == null)
            {
                if (raw != null)
                    UnityObject.Destroy(raw);

                failure =
                    "Object.Instantiate returned an untyped UnityEngine.Object that would not cast to AvatarSettings";
                return null;
            }

            clone.hideFlags = HideFlags.HideAndDontSave;
            failure = string.Empty;
            return clone;
        }
        catch (Exception ex)
        {
            failure = Describe.Of(ex);
            return null;
        }
    }

    /// <summary>Writes a recipe into a cloned settings asset. Morph outliers keep the donor value.</summary>
    internal static bool Apply(object settings, ArchetypeLook look, out string failure)
    {
        var typed = InteropCast.As(settings, GameTypes.AvatarSettings);
        if (typed is null)
        {
            failure =
                "settings is not an AvatarSettings instance (untyped UnityEngine.Object wrapper — " +
                "cast before writing fields)";
            return false;
        }

        var settingsType = GameReflection.FindType(GameTypes.AvatarSettings) ?? typed.GetType();
        var problems = new List<string>();

        // Donor morphs are known-good for this rig. Proposed wardrobe values are used only when they
        // sit inside the shipped dump range; otherwise we keep the donor and log it.
        var gender = GuardMorph(typed, settingsType, "Gender", look.Gender, AvatarMorphRanges.Gender);
        var height = GuardMorph(typed, settingsType, "Height", look.Height, AvatarMorphRanges.Height);
        var weight = GuardMorph(typed, settingsType, "Weight", look.Weight, AvatarMorphRanges.Weight);
        var browScale = GuardMorph(typed, settingsType, "EyebrowScale", look.EyebrowScale, AvatarMorphRanges.EyebrowScale);
        var browThick = GuardMorph(typed, settingsType, "EyebrowThickness", look.EyebrowThickness, AvatarMorphRanges.EyebrowThickness);
        var pupils = GuardMorph(typed, settingsType, "PupilDilation", look.PupilDilation, AvatarMorphRanges.PupilDilation);

        look.Gender = gender;
        look.Height = height;
        look.Weight = weight;
        look.EyebrowScale = browScale;
        look.EyebrowThickness = browThick;
        look.PupilDilation = pupils;

        Write(typed, settingsType, "Gender", gender, problems);
        Write(typed, settingsType, "Height", height, problems);
        Write(typed, settingsType, "Weight", weight, problems);
        Write(typed, settingsType, "SkinColor", (Color)look.SkinColor, problems);
        Write(typed, settingsType, "HairPath", look.HairPath, problems);
        Write(typed, settingsType, "HairColor", look.HairColor, problems);
        Write(typed, settingsType, "EyebrowScale", browScale, problems);
        Write(typed, settingsType, "EyebrowThickness", browThick, problems);
        Write(typed, settingsType, "PupilDilation", pupils, problems);

        // Eyelids follow skin or the face reads as a mask at close range.
        Write(typed, settingsType, "LeftEyeLidColor", (Color)look.SkinColor, problems);
        Write(typed, settingsType, "RightEyeLidColor", (Color)look.SkinColor, problems);

        // Runtime layer composition. Every shipped NPC except our own scout uses a baked combined
        // layer; we author discrete lists, so the flag must be off and the baked ref cleared.
        Write(typed, settingsType, "UseCombinedLayer", false, problems);
        Write(typed, settingsType, "CombinedLayer", null!, problems);

        ReplaceLayers(typed, settingsType, "FaceLayerSettings", GameTypes.LayerSetting, "layerPath", "layerTint",
            look.FaceLayers, FaceBodySlots, padEmpty: true, problems);
        ReplaceLayers(typed, settingsType, "BodyLayerSettings", GameTypes.LayerSetting, "layerPath", "layerTint",
            look.BodyLayers, FaceBodySlots, padEmpty: true, problems);
        ReplaceLayers(typed, settingsType, "AccessorySettings", GameTypes.AccessorySetting, "path", "color",
            look.AccessoryLayers, targetCount: look.AccessoryLayers.Count, padEmpty: false, problems);

        if (problems.Count > 0)
        {
            failure = string.Join("; ", problems);
            return false;
        }

        if (!Verify(typed, settingsType, look, out failure))
            return false;

        failure = string.Empty;
        return true;
    }

    /// <summary>Re-reads a few load-bearing fields so a silent no-op cannot look like a dress.</summary>
    internal static bool Verify(object settings, ArchetypeLook look, out string failure)
    {
        var typed = InteropCast.As(settings, GameTypes.AvatarSettings);
        if (typed is null)
        {
            failure = "verify failed: settings is not AvatarSettings";
            return false;
        }

        var settingsType = GameReflection.FindType(GameTypes.AvatarSettings) ?? typed.GetType();
        return Verify(typed, settingsType, look, out failure);
    }

    private static bool Verify(object settings, Type settingsType, ArchetypeLook look, out string failure)
    {
        var problems = new List<string>();

        if (!TryRead(settings, settingsType, "Gender", out var gender) ||
            gender is not float genderValue ||
            Math.Abs(genderValue - look.Gender) > 0.001f)
        {
            problems.Add($"Gender did not stick (wanted {look.Gender}, got {GameReflection.Format(gender)})");
        }

        if (!TryRead(settings, settingsType, "HairPath", out var hair) ||
            hair is not string hairPath ||
            !string.Equals(hairPath, look.HairPath, StringComparison.Ordinal))
        {
            problems.Add($"HairPath did not stick (wanted '{look.HairPath}', got {GameReflection.Format(hair)})");
        }

        if (!TryRead(settings, settingsType, "UseCombinedLayer", out var combined) || combined is not false)
            problems.Add($"UseCombinedLayer did not stick (got {GameReflection.Format(combined)})");

        if (!TryRead(settings, settingsType, "Height", out var height) ||
            height is not float heightValue ||
            !AvatarMorphRanges.Height.Contains(heightValue))
        {
            problems.Add($"Height out of shipped range after write (got {GameReflection.Format(height)})");
        }

        if (!TryRead(settings, settingsType, "Weight", out var weight) ||
            weight is not float weightValue ||
            !AvatarMorphRanges.Weight.Contains(weightValue))
        {
            problems.Add($"Weight out of shipped range after write (got {GameReflection.Format(weight)})");
        }

        failure = problems.Count == 0 ? string.Empty : string.Join("; ", problems);
        return problems.Count == 0;
    }

    private static float GuardMorph(
        object settings,
        Type settingsType,
        string member,
        float proposed,
        AvatarMorphRanges.Range hard)
    {
        var donor = TryRead(settings, settingsType, member, out var raw) && raw is float value
            ? value
            : hard.Clamp(proposed);

        var chosen = AvatarMorphRanges.Guard(member, proposed, donor, hard, out var rejected);
        if (rejected is not null)
            VisitorLog.Instance.Warn(rejected);

        return chosen;
    }

    private static void Write(object settings, Type settingsType, string member, object? value, List<string> problems)
    {
        try
        {
            var property = settingsType.GetProperty(member);
            if (property is null || !property.CanWrite)
            {
                // CombinedLayer may be absent or set-only via field on some builds — non-fatal.
                if (string.Equals(member, "CombinedLayer", StringComparison.Ordinal))
                    return;

                problems.Add(
                    $"{member} is not writable on {settingsType.Name} " +
                    $"(runtime wrapper was {settings.GetType().Name})");
                return;
            }

            property.SetValue(settings, value);
        }
        catch (Exception ex)
        {
            if (string.Equals(member, "CombinedLayer", StringComparison.Ordinal))
                return;

            problems.Add($"{member}: {Describe.Of(ex)}");
        }
    }

    private static bool TryRead(object settings, Type settingsType, string member, out object? value)
    {
        value = null;
        try
        {
            var property = settingsType.GetProperty(member);
            if (property is null || !property.CanRead)
                return false;

            value = property.GetValue(settings);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void ReplaceLayers(
        object settings,
        Type settingsType,
        string listMember,
        string elementTypeName,
        string pathMember,
        string colorMember,
        IReadOnlyList<ArchetypeLook.Layer> layers,
        int targetCount,
        bool padEmpty,
        List<string> problems)
    {
        var elementType = GameReflection.FindType(elementTypeName);
        if (elementType is null)
        {
            problems.Add($"'{elementTypeName}' not found");
            return;
        }

        var property = settingsType.GetProperty(listMember);
        if (property is null)
        {
            problems.Add($"{listMember} not found on {settingsType.Name}");
            return;
        }

        object? list;
        try
        {
            list = property.GetValue(settings);
        }
        catch (Exception ex)
        {
            problems.Add($"{listMember}: {Describe.Of(ex)}");
            return;
        }

        if (!GameReflection.IsPresent(list))
        {
            list = CreateList(property.PropertyType, problems, listMember);
            if (list is null)
                return;

            try
            {
                property.SetValue(settings, list);
            }
            catch (Exception ex)
            {
                problems.Add($"{listMember} assign: {Describe.Of(ex)}");
                return;
            }
        }

        var desired = padEmpty ? Math.Max(targetCount, layers.Count) : layers.Count;
        if (!EnsureCount(list!, elementType, pathMember, colorMember, desired, problems, listMember))
            return;

        for (var i = 0; i < desired; i++)
        {
            var path = i < layers.Count ? layers[i].Path : string.Empty;
            var color = i < layers.Count ? layers[i].Color : new Color(0f, 0f, 0f, 0f);

            if (!TryGetItem(list!, i, out var row) || row is null)
            {
                problems.Add($"{listMember}[{i}] unreadable");
                return;
            }

            Write(row, elementType, pathMember, path, problems);
            Write(row, elementType, colorMember, color, problems);

            // Struct rows must be written back — SetValue on a boxed copy alone is a no-op to the list.
            if (!TrySetItem(list!, i, row, out var setFailure))
            {
                problems.Add($"{listMember}[{i}] set_Item: {setFailure}");
                return;
            }

            object? written = null;
            if (!TryGetItem(list!, i, out var check) || check is null ||
                !TryRead(check, elementType, pathMember, out written) ||
                written is not string writtenPath ||
                !string.Equals(writtenPath, path, StringComparison.Ordinal))
            {
                problems.Add(
                    $"{listMember}[{i}].{pathMember} did not stick after set_Item " +
                    $"(wanted '{path}', got {GameReflection.Format(written)}) — " +
                    "LayerSetting is an IL2CPP struct; corrupt rows stretch the mesh");
                return;
            }
        }

        // Drop anything past the recipe so a longer donor list cannot reappear as a ghost garment.
        while (TryCount(list!, out var count) && count > desired)
        {
            if (!GameReflection.TryInvoke(list!.GetType(), list, "RemoveAt", new object?[] { count - 1 }, out _, out var removeFailure))
            {
                problems.Add($"{listMember}.RemoveAt: {removeFailure}");
                return;
            }
        }
    }

    private static bool EnsureCount(
        object list,
        Type elementType,
        string pathMember,
        string colorMember,
        int desired,
        List<string> problems,
        string listMember)
    {
        if (!TryCount(list, out var count))
        {
            problems.Add($"{listMember}.Count unreadable");
            return false;
        }

        while (count < desired)
        {
            object element;
            try
            {
                // Prefer cloning an existing valid native struct row over Activator on a zeroed one.
                if (count > 0 && TryGetItem(list, 0, out var template) && template is not null)
                {
                    element = template;
                }
                else
                {
                    element = Activator.CreateInstance(elementType)!;
                }
            }
            catch (Exception ex)
            {
                problems.Add($"{elementType.Name}: {Describe.Of(ex)}");
                return false;
            }

            Write(element, elementType, pathMember, string.Empty, problems);
            Write(element, elementType, colorMember, new Color(0f, 0f, 0f, 0f), problems);

            if (!GameReflection.TryInvoke(list.GetType(), list, "Add", new[] { element }, out _, out var addFailure))
            {
                problems.Add($"{listMember}.Add: {addFailure}");
                return false;
            }

            count++;
        }

        return true;
    }

    private static bool TryCount(object list, out int count)
    {
        count = 0;
        if (!GameReflection.TryRead(list, "Count", out var value, out _))
            return false;

        if (value is int i)
        {
            count = i;
            return true;
        }

        return false;
    }

    private static bool TryGetItem(object list, int index, out object? item)
    {
        item = null;
        return GameReflection.TryInvoke(list.GetType(), list, "get_Item", new object?[] { index }, out item, out _) &&
               item is not null;
    }

    private static bool TrySetItem(object list, int index, object item, out string failure) =>
        GameReflection.TryInvoke(list.GetType(), list, "set_Item", new object?[] { index, item }, out _, out failure);

    private static object? CreateList(Type listType, List<string> problems, string listMember)
    {
        try
        {
            return Activator.CreateInstance(listType);
        }
        catch (Exception ex)
        {
            problems.Add($"{listMember} could not be created: {Describe.Of(ex)}");
            return null;
        }
    }
}

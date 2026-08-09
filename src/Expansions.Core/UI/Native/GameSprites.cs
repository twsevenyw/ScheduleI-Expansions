using Expansions.Core.Logging;
using UnityEngine;

namespace Expansions.Core.UI.Native;

/// <summary>
/// Binds the game's own UI sprites by name, with a procedurally generated rounded rect as the last
/// resort.
/// <para>
/// <c>Resources.FindObjectsOfTypeAll&lt;Sprite&gt;()</c> only returns sprites resident in memory, and
/// residency depends on something loaded referencing them. <c>Rectangle_RoundedEdges</c> has 477
/// referencing <c>Image</c>s in the menu scene and is safe anywhere; <c>trophy</c> and
/// <c>Rect shade</c> are thin, hence the fallbacks and the re-harvest on every scene change.
/// </para>
/// </summary>
internal static class GameSprites
{
    /// <summary>The game's canonical rounded panel: 512x512, 9-slice border 255, PPU 100.</summary>
    public const string Rounded = "Rectangle_RoundedEdges";

    public const string Trophy = "trophy";

    /// <summary>Soft black drop shadow, 256x256, border 122, PPU 100.</summary>
    public const string Shade = "Rect shade";

    private static readonly string[] RoundedFallbacks = { "UISprite", "Background", "InputFieldBackground" };
    private static readonly string[] TrophyFallbacks = { "Tick circle", "Tick" };

    private static readonly Dictionary<string, Sprite> Cache = new(StringComparer.Ordinal);
    private static readonly ModuleLogger Log = new("UI");

    private static Sprite? _generatedRounded;
    private static bool _harvested;

    public static int Count => Cache.Count;

    /// <summary>Call once per screen build, after the scene it will be shown in is up.</summary>
    public static void Harvest()
    {
        _harvested = true;

        try
        {
            var all = Resources.FindObjectsOfTypeAll<Sprite>();
            for (var i = 0; i < all.Length; i++)
            {
                var sprite = all[i];
                if (sprite == null || string.IsNullOrEmpty(sprite.name))
                    continue;

                // Replaces an entry whose native side has since been unloaded, rather than skipping
                // on name alone and keeping a dead handle.
                if (Cache.TryGetValue(sprite.name, out var existing) && existing != null)
                    continue;

                Cache[sprite.name] = sprite;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Sprite harvest failed ({ex.GetType().Name}: {ex.Message}); the generated fallback will be used.");
        }
    }

    /// <summary>The panel/card sprite, or a generated 9-sliceable rounded square if it is not resident.</summary>
    public static Sprite? RoundedRect()
    {
        var sprite = Find(Rounded, RoundedFallbacks);
        return sprite != null ? sprite : Generated();
    }

    /// <summary>Null when neither the trophy nor a tick is resident; the caller hides the icon.</summary>
    public static Sprite? TrophyIcon() => Find(Trophy, TrophyFallbacks);

    /// <summary>Null when the shade sprite is not resident; the caller drops the drop shadow.</summary>
    public static Sprite? ShadeRect() => Find(Shade, Array.Empty<string>());

    /// <summary>
    /// The <c>Image.pixelsPerUnitMultiplier</c> that renders <paramref name="sprite"/>'s 9-slice
    /// corner at <paramref name="radiusPx"/> canvas pixels.
    /// <para>
    /// On-screen slice = <c>border × (canvasRefPPU / spritePPU) ÷ multiplier</c>. Reading the sprite's
    /// real border instead of hardcoding 255 keeps the geometry right when a fallback sprite with a
    /// different border is in play.
    /// </para>
    /// </summary>
    public static float MultiplierFor(Sprite? sprite, float radiusPx)
    {
        var radius = Mathf.Max(radiusPx, 0.5f);

        if (sprite == null)
            return 255f / radius;

        var border = sprite.border.x;
        var pixelsPerUnit = sprite.pixelsPerUnit;

        if (border <= 0f || pixelsPerUnit <= 0f)
            return 1f;

        return border * (MenuStyle.ReferencePixelsPerUnit / pixelsPerUnit) / radius;
    }

    internal static void Forget()
    {
        Cache.Clear();
        _harvested = false;
    }

    private static Sprite? Find(string name, string[] fallbacks)
    {
        if (!_harvested)
            Harvest();

        if (Cache.TryGetValue(name, out var sprite) && sprite != null)
            return sprite;

        foreach (var fallback in fallbacks)
        {
            if (Cache.TryGetValue(fallback, out var alternate) && alternate != null)
            {
                Log.Debug($"Sprite '{name}' is not resident; using '{fallback}'.");
                return alternate;
            }
        }

        return null;
    }

    /// <summary>
    /// 64x64 white rounded square, 9-slice border 16, PPU 100 — same sizing maths as the game sprite,
    /// just a different border, which <see cref="MultiplierFor"/> accounts for.
    /// </summary>
    private static Sprite? Generated()
    {
        if (_generatedRounded != null)
            return _generatedRounded;

        const int size = 64;
        const int radius = 16;

        try
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };

            var pixels = new Color[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = Mathf.Max(radius - x - 0.5f, 0f, x + 0.5f - (size - radius));
                    var dy = Mathf.Max(radius - y - 0.5f, 0f, y + 0.5f - (size - radius));
                    var alpha = Mathf.Clamp01(radius - Mathf.Sqrt((dx * dx) + (dy * dy)) + 0.5f);
                    pixels[(y * size) + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, false);
            UnityEngine.Object.DontDestroyOnLoad(texture);

            var sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                MenuStyle.ReferencePixelsPerUnit,
                0u,
                SpriteMeshType.FullRect,
                new Vector4(radius, radius, radius, radius),
                false);

            sprite.name = "Expansions_RoundedFallback";
            UnityEngine.Object.DontDestroyOnLoad(sprite);

            Log.Warn($"'{Rounded}' is not resident in this scene; drawing with a generated rounded rect instead.");
            _generatedRounded = sprite;
            return sprite;
        }
        catch (Exception ex)
        {
            Log.Error("Could not generate the fallback rounded sprite; panels will draw as plain rectangles.", ex);
            return null;
        }
    }
}

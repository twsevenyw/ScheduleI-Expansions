using Expansions.Core.Configuration;

namespace Expansions.Tweaks.AutoReorder;

/// <summary>
/// The auto-reorder feature's own settings.
/// <para>
/// Deliberately a separate type from <see cref="TweaksConfig"/> rather than three more properties on
/// it. Both bind into the same <see cref="ModuleConfig"/>, so the owner still sees one
/// <c>[Tweaks_01_Main]</c> section in <c>Expansions.cfg</c> — the split is a source-file boundary,
/// not a user-visible one.
/// </para>
/// </summary>
internal sealed class AutoReorderSettings
{
    /// <summary>Below zero a "floor" stops meaning anything; the game has no negative balances.</summary>
    internal const float MinFloor = 0f;

    internal const float MaxFloor = 100_000_000f;

    internal AutoReorderSettings(ModuleConfig config)
    {
        Enabled = config.Bind("auto_reorder_enabled", true, "Auto-reorder deliveries",
            "Adds a tick box to each row in the phone's Deliveries app. Tick it and that exact order " +
            "is placed again by itself as soon as the game would let you press Reorder. Off removes " +
            "the tick boxes and stops every standing order; the ticks you set are remembered and " +
            "resume when you switch it back on.");

        MinBalance = config.Bind("auto_reorder_min_balance", 1_000f, "Auto-reorder balance floor",
            "A standing order will not fire if paying for it would leave you below this. Checked " +
            "against whichever balance the shop actually charges - cash or online - so it matches " +
            "what ordering by hand would take. Set 0 to spend down to nothing.");

        Notify = config.Bind("auto_reorder_notify", true, "Announce automatic orders",
            "Show the game's own notification whenever a standing order places itself, so money " +
            "never leaves your account silently. The Expansions output pane records it either way.");
    }

    internal ConfigValue<bool> Enabled { get; }

    internal ConfigValue<float> MinBalance { get; }

    internal ConfigValue<bool> Notify { get; }

    /// <summary>
    /// The floor actually in force. Clamped on read rather than rewritten, matching how
    /// <see cref="TweaksConfig"/> treats its own out-of-range values: an odd number is the owner
    /// experimenting, not a mistake to correct behind their back.
    /// </summary>
    internal float ResolvedFloor
    {
        get
        {
            var raw = MinBalance.Value;
            if (float.IsNaN(raw))
                return MinBalance.DefaultValue;

            return Math.Clamp(raw, MinFloor, MaxFloor);
        }
    }
}

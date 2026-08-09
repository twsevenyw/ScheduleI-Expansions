namespace Expansions.Core.Diagnostics;

/// <summary>
/// One named, independently fallible runtime question. A probe answers exactly one thing, never
/// throws on a missing target (report <see cref="ProbeStatus.NotFound"/> instead), and — unless it
/// declares <see cref="Mutates"/> — leaves the game exactly as it found it.
/// </summary>
public interface IProbe
{
    /// <summary>Stable, lowercase, dot-separated. Appears verbatim in <c>RESULT:&lt;id&gt;:&lt;status&gt;</c>.</summary>
    string Id { get; }

    /// <summary>Short question this probe answers.</summary>
    string Name { get; }

    /// <summary>Report section this probe is grouped under.</summary>
    string Area { get; }

    /// <summary>
    /// True if the probe writes game state. Mutating probes must snapshot before and restore in a
    /// <c>finally</c>, and are listed separately in the report header.
    /// </summary>
    bool Mutates { get; }

    /// <summary>
    /// True if the probe is meaningless outside a loaded save. The runner short-circuits those to
    /// <see cref="ProbeStatus.Inconclusive"/> rather than letting them reach for a null singleton.
    /// </summary>
    bool RequiresLoadedSave { get; }

    /// <summary>
    /// True if the probe must never run by accident. These are skipped by <c>expprobe</c> with no
    /// arguments, by the menu's Diagnostics button and by any area or prefix selector; the only way
    /// in is naming the id exactly <em>and</em> turning on
    /// <see cref="Configuration.ExpansionConfig.AllowMutatingProbes"/>. Reserved for probes that can
    /// take the process down rather than merely fail.
    /// </summary>
    bool ExplicitOnly { get; }

    void Run(ProbeContext context, ProbeResult result);
}

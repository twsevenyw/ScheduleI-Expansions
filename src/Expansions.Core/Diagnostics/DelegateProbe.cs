namespace Expansions.Core.Diagnostics;

/// <summary>
/// Probe built from a lambda, so a feature module can contribute one without declaring a type:
/// <code>
/// Lifetime.Add(ProbeRegistry.Register(new DelegateProbe(
///     "drivers.route_roundtrip", "Do cross-property routes survive a save?", "Hireable Drivers",
///     (ctx, r) => { … })));
/// </code>
/// </summary>
public sealed class DelegateProbe : IProbe
{
    private readonly Action<ProbeContext, ProbeResult> _run;

    public DelegateProbe(
        string id,
        string name,
        string area,
        Action<ProbeContext, ProbeResult> run,
        bool requiresLoadedSave = true,
        bool mutates = false,
        bool explicitOnly = false)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Probe id must be a non-empty, stable string.", nameof(id));

        Id = id;
        Name = string.IsNullOrWhiteSpace(name) ? id : name;
        Area = string.IsNullOrWhiteSpace(area) ? "Uncategorised" : area;
        RequiresLoadedSave = requiresLoadedSave;
        Mutates = mutates;
        ExplicitOnly = explicitOnly;
        _run = run ?? throw new ArgumentNullException(nameof(run));
    }

    public string Id { get; }

    public string Name { get; }

    public string Area { get; }

    public bool Mutates { get; }

    public bool RequiresLoadedSave { get; }

    public bool ExplicitOnly { get; }

    public void Run(ProbeContext context, ProbeResult result) => _run(context, result);
}

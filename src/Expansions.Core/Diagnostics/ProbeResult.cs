using System.Text;

namespace Expansions.Core.Diagnostics;

/// <summary>
/// One probe's findings. The probe fills this in; the runner owns <see cref="Error"/> and
/// <see cref="ElapsedMs"/> and renders the whole thing to Markdown.
/// </summary>
public sealed class ProbeResult
{
    private readonly List<string> _body = new();
    private readonly List<string> _artifacts = new();

    internal ProbeResult(IProbe probe)
    {
        Id = probe.Id;
        Name = probe.Name;
        Area = probe.Area;
        Mutates = probe.Mutates;
    }

    public string Id { get; }

    public string Name { get; }

    public string Area { get; }

    /// <summary>True if this probe writes game state. Mutating probes must restore in a <c>finally</c>.</summary>
    public bool Mutates { get; }

    public ProbeStatus Status { get; set; } = ProbeStatus.Inconclusive;

    /// <summary>True when the runner refused to run the probe at all, rather than it deciding nothing.</summary>
    public bool Skipped { get; internal set; }

    /// <summary>One line saying what the finding means for the implementation plans.</summary>
    public string Interpretation { get; set; } = string.Empty;

    public Exception? Error { get; internal set; }

    public long ElapsedMs { get; internal set; }

    /// <summary>Extra files this probe wrote, as absolute paths.</summary>
    public IReadOnlyList<string> Artifacts => _artifacts;

    public IReadOnlyList<string> Body => _body;

    public void Line(string text = "") => _body.Add(text);

    public void Bullet(string text) => _body.Add("- " + text);

    public void Fact(string label, string value) => _body.Add($"- **{label}**: {value}");

    public void Heading(string text)
    {
        if (_body.Count > 0)
            _body.Add(string.Empty);

        _body.Add("**" + text + "**");
        _body.Add(string.Empty);
    }

    /// <summary>Pipe table with per-cell escaping. Skipped entirely when <paramref name="rows"/> is empty.</summary>
    public void Table(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        if (headers.Count == 0 || rows.Count == 0)
            return;

        SeparateFromPrevious();
        _body.Add("| " + string.Join(" | ", headers.Select(Cell)) + " |");
        _body.Add("|" + string.Concat(Enumerable.Repeat("---|", headers.Count)));

        foreach (var row in rows)
        {
            var cells = new string[headers.Count];
            for (var i = 0; i < headers.Count; i++)
                cells[i] = i < row.Count ? Cell(row[i]) : string.Empty;

            _body.Add("| " + string.Join(" | ", cells) + " |");
        }

        _body.Add(string.Empty);
    }

    /// <summary>Fenced block. Cheaper than a table for long flat lists.</summary>
    public void Code(IEnumerable<string> lines, string language = "text")
    {
        SeparateFromPrevious();
        _body.Add("```" + language);
        _body.AddRange(lines);
        _body.Add("```");
        _body.Add(string.Empty);
    }

    public void Artifact(string path)
    {
        if (!string.IsNullOrWhiteSpace(path))
            _artifacts.Add(path);
    }

    public void Ok(string interpretation)
    {
        Status = ProbeStatus.Ok;
        Interpretation = interpretation;
    }

    public void NotFound(string interpretation)
    {
        Status = ProbeStatus.NotFound;
        Interpretation = interpretation;
    }

    public void Inconclusive(string interpretation)
    {
        Status = ProbeStatus.Inconclusive;
        Interpretation = interpretation;
    }

    public void Fail(string interpretation)
    {
        Status = ProbeStatus.Failed;
        Interpretation = interpretation;
    }

    /// <summary>A table or fence glued to the line above it does not render as a block.</summary>
    private void SeparateFromPrevious()
    {
        if (_body.Count > 0 && _body[^1].Length != 0)
            _body.Add(string.Empty);
    }

    /// <summary>Markdown table cells cannot contain a raw pipe or a newline.</summary>
    private static string Cell(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var builder = new StringBuilder(value!.Length + 8);
        foreach (var c in value)
        {
            switch (c)
            {
                case '|':
                    builder.Append("\\|");
                    break;
                case '\r':
                    break;
                case '\n':
                    builder.Append(' ');
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }

        return builder.ToString();
    }
}

using System.Text.RegularExpressions;
using SIL.Motif.Host.Gdr;
using SIL.Motif.Contract.Responses;
using SIL.TextEditing;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.App.Preview;

/// <summary>A changed field in a Grammar Design Record Markdown document.</summary>
public sealed record GdrMarkdownChange(string Field, string? WritingSystem, IReadOnlyList<Paragraph> Paragraphs,
    IReadOnlyList<TextRun>? Runs);

/// <summary>The fields and findings read from a Grammar Design Record Markdown document.</summary>
public sealed record GdrMarkdownReading(IReadOnlyList<GdrMarkdownChange> Changes, IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings)
{
    /// <summary>Whether the document cannot be saved.</summary>
    public bool HasErrors => Errors.Count != 0;
}

/// <summary>Builds and reads the whole-record Markdown view using the field Markdown reader and writer.</summary>
public sealed class GdrMarkdownDocument
{
    private sealed record Field(string Name, string? Ws, string? AlternativeWs, bool Inline, DiscussionSourceForm Form,
        GdrDiscussion Source, IReadOnlyList<Paragraph> Original, string Body);

    private readonly string _header;
    private readonly Dictionary<(string Name, string? Ws), Field> _fields = new();
    private static readonly Regex Opening = new(@"^::: \{field=(title|description|discussion)(?: ws=([^ }]+))?\}$",
        RegexOptions.CultureInvariant);

    /// <summary>Builds a document from the record as shown, using the project's Markdown options.</summary>
    public GdrMarkdownDocument(GdrShowResponse shown, string? projectName, IReadOnlyList<Paragraph>? discussion = null)
    {
        var source = GdrDiscussion.From(shown);
        var view = new GdrMarkdownView(shown, projectName, discussion?.Select(source.AsProject).ToArray());
        _header = view.Header;
        Text = view.Text;
        foreach (var field in view.Fields)
        {
            var fieldSource = field.Inline ? GdrDiscussion.From(field.Response) : source;
            var copy = (field.Inline || discussion is null ? fieldSource.Document.Paragraphs : discussion)
                .Select(paragraph => paragraph.Clone()).ToArray();
            var form = new DiscussionSourceForm(fieldSource, projectName, richString: field.Inline,
                inlineShape: field.Inline ? copy[0] : null);
            _fields.Add((field.Name, field.WritingSystem), new Field(field.Name, field.WritingSystem,
                field.AlternativeWritingSystem, field.Inline, form, fieldSource, copy, field.Body));
        }
    }

    /// <summary>The document initially shown to the person.</summary>
    public string Text { get; }

    /// <summary>The Discussion's paragraphs as the document was built, with any pending edit; copies the caller may change.</summary>
    public IReadOnlyList<Paragraph> DiscussionParagraphs => _fields[("discussion", null)].Original.Select(paragraph => paragraph.Clone()).ToArray();

    /// <summary>Reads Discussion paragraphs a caller edited as paragraphs, giving the change Markdown would, or none when they match.</summary>
    public GdrMarkdownReading ReadDiscussion(IReadOnlyList<Paragraph> paragraphs)
    {
        ArgumentNullException.ThrowIfNull(paragraphs);
        var field = _fields[("discussion", null)];
        if (field.Original.Count == paragraphs.Count && field.Original.Zip(paragraphs).All(pair => field.Form.AreEquivalent(pair.First, pair.Second)))
            return new([], [], []);
        return new([new GdrMarkdownChange("discussion", null, paragraphs, null)], [], []);
    }

    /// <summary>Reads the document, returning findings and changed fields without staging anything.</summary>
    public GdrMarkdownReading Read(string text)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var changes = new List<GdrMarkdownChange>();
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (!text.StartsWith(_header, StringComparison.Ordinal))
            return new([], ["The record identity, parent and position are read-only."], []);
        var lines = text[_header.Length..].Split('\n');
        var seen = new HashSet<(string, string?)>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var match = Opening.Match(lines[i]);
            if (!match.Success) { errors.Add("Expected a field div."); break; }
            var key = (match.Groups[1].Value, match.Groups[2].Success ? match.Groups[2].Value : null);
            if (!_fields.TryGetValue(key, out var field) || !seen.Add(key))
            { errors.Add($"Unknown or repeated field: {key.Item1} {key.Item2}."); break; }
            var body = new List<string>();
            var depth = 0;
            var closed = false;
            while (++i < lines.Length)
            {
                var line = lines[i];
                if (line == ":::" && depth == 0) { closed = true; break; }
                if (line.StartsWith("::: ", StringComparison.Ordinal)) depth++;
                else if (line == ":::" && depth > 0) depth--;
                body.Add(line);
            }
            if (!closed) { errors.Add($"The {field.Name} div is not closed."); break; }
            var value = string.Join("\n", body);
            if (value == field.Body) continue;
            var read = field.Form.Read(value, field.Inline);
            errors.AddRange(read.Diagnostics.Where(item => item.Severity == DiagnosticSeverity.Error)
                .Select(item => $"{field.Name}: {item.Message}"));
            warnings.AddRange(read.Diagnostics.Where(item => item.Severity == DiagnosticSeverity.Warning)
                .Select(item => $"{field.Name}: {item.Message}"));
            if (read.HasErrors) continue;
            if (field.Original.Count != read.Paragraphs.Count ||
                !field.Original.Zip(read.Paragraphs).All(pair => field.Form.AreEquivalent(pair.First, pair.Second)))
            {
                var runs = field.Inline ? field.Source.ProjectRuns(read.Paragraphs[0]) : null;
                if (field.Name == "title" && runs!.Any(run => run.CharacterStyle is not null || run.ObjData is not null ||
                    !string.Equals(run.Ws, field.AlternativeWs, StringComparison.OrdinalIgnoreCase) ||
                    run.Props is { } props && (props.Ints.Count != 0 || props.Strings.Count != 0)))
                    errors.Add("title: only plain text in its writing system is allowed.");
                else changes.Add(new GdrMarkdownChange(field.Name, field.AlternativeWs, read.Paragraphs, runs));
            }
        }
        if (seen.Count != _fields.Count) errors.Add("Every field must be present once.");
        return new(changes, errors, warnings);
    }
}

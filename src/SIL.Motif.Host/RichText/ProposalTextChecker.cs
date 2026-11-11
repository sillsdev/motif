using System.Text;
using System.Text.RegularExpressions;
using Block = Markdig.Syntax.Block;
using ListBlock = Markdig.Syntax.ListBlock;
using ParagraphBlock = Markdig.Syntax.ParagraphBlock;
using SIL.LCModel;
using SIL.Motif.Contract.Responses;
using SIL.TextEditing;
using SIL.TextEditing.Markdown;

namespace SIL.Motif.Host.RichText;

/// <summary>A Rationale or Comment as it is to be stored, and what a reader would see as wrong in it.</summary>
/// <param name="Text">The text as written, with only the tidy-ups that keep its meaning when it is clean.</param>
/// <param name="Findings">Only what a reader would see as wrong (ADR 0099 decision 4); empty when the text is clean.</param>
public sealed record CheckedProposalText(string Text, IReadOnlyList<TextFinding> Findings)
{
    /// <summary>Whether the text has no findings.</summary>
    public bool IsClean => Findings.Count == 0;

    /// <summary>
    /// Whether the text needed the project and its Baseline could not be read, so only the markup was checked.
    /// </summary>
    public bool ProjectUnavailable { get; init; }
}

/// <summary>
/// Checks a Proposal's Rationale or Comment as FieldWorks Markdown (ADR 0099). Unmarked text is the project's default
/// analysis writing system. Every error the parser reports is a finding unless it is only a spelling note, and so are
/// a link to a deleted object and what a CommonMark reader would show differently from FieldWorks: a list that starts
/// inside a paragraph, or a numbered list FieldWorks would renumber. The text is stored as written; when it is clean,
/// line endings become LF, trailing whitespace at its end goes and a path link gains its <c>guid=</c>, and only if
/// reading the tidied text gives the same paragraphs.
/// </summary>
public static class ProposalTextChecker
{
    /// <summary>A list that starts on the line after a paragraph line, which FieldWorks reads as part of the paragraph.</summary>
    public const string ListNeedsBlankLine = "motif.list.needs-blank-line";

    /// <summary>A numbered list that starts at a number other than 1, which FieldWorks numbers from 1.</summary>
    public const string ListRenumbered = "motif.list.renumbered";

    // Spelling notes the stored text keeps or the next export rewrites; every other error is a finding.
    private static readonly HashSet<string> TidyUps = new(StringComparer.Ordinal)
    {
        MarkdownDiagnosticIds.LinkStalePath,
        MarkdownDiagnosticIds.LinkRebound,
        MarkdownDiagnosticIds.LinkUrlRebuilt,
        MarkdownDiagnosticIds.WsUnmarkedVernacular,
        MarkdownDiagnosticIds.Unnormalizable,
        MarkdownDiagnosticIds.PropUnknownNumber,
    };

    // Not errors to the parser, but a reader sees them.
    private static readonly HashSet<string> AlsoVisible = new(StringComparer.Ordinal) { MarkdownDiagnosticIds.LinkBroken };

    private static readonly Regex ProjectAttribute = new(@"\{[^}]*(?<![\w-])(?:ws|custom-style|style)=", RegexOptions.CultureInvariant);

    /// <summary>
    /// Whether checking <paramref name="text"/> needs the project: it links to an object of the project or names a
    /// writing system or style, and its markup reads without an error. Other text checks the same without one.
    /// </summary>
    public static bool NeedsProject(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var parsed = MarkdownConverter.Parse(text, Options(null, null));
        var errors = parsed.Diagnostics.Where(diagnostic => diagnostic.Severity == MarkdownSeverity.Error).ToArray();
        if (errors.Any(diagnostic => !diagnostic.Id.StartsWith("fwmd.link.", StringComparison.Ordinal))) return false;
        return errors.Any(diagnostic => diagnostic.Id == MarkdownDiagnosticIds.LinkNotFound) ||
            parsed.Objects.Any(item => item.Kind != MarkdownObjectKind.ExternalLink) ||
            ProjectAttribute.IsMatch(text);
    }

    /// <summary>
    /// Checks <paramref name="text"/> against the project open in <paramref name="cache"/>, whose name is
    /// <paramref name="projectName"/>; with no cache only the markup is checked and a link must carry its GUID.
    /// </summary>
    public static CheckedProposalText Check(string text, LcmCache? cache, string? projectName)
    {
        ArgumentNullException.ThrowIfNull(text);
        var options = Options(cache, projectName);
        var parsed = MarkdownConverter.Parse(text, options);
        var findings = parsed.Diagnostics
            .Where(IsFinding)
            .Concat(Structure(parsed))
            .OrderBy(diagnostic => diagnostic.Start)
            .Select(diagnostic => new TextFinding(diagnostic.Id, diagnostic.Line, diagnostic.Column, diagnostic.Message)
            {
                Observed = diagnostic.Observed,
                Replacement = diagnostic.Replacement,
            }).ToArray();
        if (findings.Length > 0) return new CheckedProposalText(text, findings);
        return new CheckedProposalText(Tidied(text, parsed, options), []);
    }

    /// <summary>
    /// Whether a reader would see what the parser reported as wrong: any error but a spelling note, so an id this
    /// checker does not know blocks, and a link to a deleted object.
    /// </summary>
    public static bool IsFinding(MarkdownDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        return diagnostic.Severity == MarkdownSeverity.Error ? !TidyUps.Contains(diagnostic.Id) : AlsoVisible.Contains(diagnostic.Id);
    }

    // CommonMark starts a list right after a paragraph line and keeps its first number; FieldWorks does neither.
    private static IEnumerable<MarkdownDiagnostic> Structure(MarkdownParseResult parsed)
    {
        var document = Markdig.Markdown.Parse(parsed.Source);
        Block? previous = null;
        foreach (var block in document)
        {
            if (block is ListBlock list)
            {
                if (previous is ParagraphBlock paragraph && LastLine(parsed.Source, paragraph) + 1 == list.Line)
                    yield return parsed.CreateDiagnostic(ListNeedsBlankLine, MarkdownSeverity.Error,
                        "This list starts on the line after a paragraph, so FieldWorks reads it as part of the paragraph. " +
                        "Put a blank line before it.", list.Span.Start, list.Span.Start + 1);
                else if (list.IsOrdered && list.OrderedStart is { } start && start.TrimStart('0') != "1")
                    yield return parsed.CreateDiagnostic(ListRenumbered, MarkdownSeverity.Error,
                        $"This numbered list starts at {start}, and FieldWorks numbers every list from 1. Start it at 1, " +
                        "or write the numbers as text.", list.Span.Start, list.Span.Start + start.Length);
            }
            previous = block;
        }
    }

    private static int LastLine(string source, ParagraphBlock paragraph) =>
        paragraph.Line + source.AsSpan(paragraph.Span.Start, paragraph.Span.Length).Count('\n');

    // A tidy-up that reads back as other paragraphs would change the meaning, so the text is then kept as written.
    private static string Tidied(string text, MarkdownParseResult parsed, MarkdownOptions options)
    {
        var tidied = new StringBuilder(text);
        foreach (var link in parsed.Objects.Where(NeedsGuid).OrderByDescending(item => item.Source!.Start))
        {
            var end = link.Source!.End;
            if (text[end - 1] == '}') tidied.Insert(end - 1, " guid=" + link.Guid!.Value.ToString("D"));
            else tidied.Insert(end, "{guid=" + link.Guid!.Value.ToString("D") + "}");
        }
        var result = tidied.ToString().Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimEnd();
        if (result.Length == 0 || result == text) return text;
        var reread = MarkdownConverter.Parse(result, options);
        return reread.Diagnostics.Count == parsed.Diagnostics.Count && Same(parsed.Document, reread.Document) ? result : text;

        bool NeedsGuid(MarkdownObject link)
        {
            if (link.Kind != MarkdownObjectKind.InternalLink || link.Guid is null || link.Source is null) return false;
            var spelled = text[link.Source.Start..link.Source.End];
            var opens = spelled.IndexOf("](", StringComparison.Ordinal);
            if (opens < 0 || spelled.Contains("guid=", StringComparison.Ordinal)) return false;
            var destination = spelled[(opens + 2)..];
            return !destination.StartsWith("silfw:", StringComparison.OrdinalIgnoreCase) &&
                !destination.StartsWith("motif:", StringComparison.OrdinalIgnoreCase) &&
                !destination.StartsWith("/objects/", StringComparison.Ordinal);
        }
    }

    private static bool Same(Document before, Document after) =>
        before.Paragraphs.Count == after.Paragraphs.Count &&
        before.Paragraphs.Zip(after.Paragraphs).All(pair => pair.First.Style == pair.Second.Style &&
            pair.First.Runs.Count == pair.Second.Runs.Count &&
            pair.First.Runs.Zip(pair.Second.Runs).All(runs => runs.First.Text == runs.Second.Text &&
                runs.First.Ws == runs.Second.Ws && runs.First.ObjData == runs.Second.ObjData));

    private static MarkdownOptions Options(LcmCache? cache, string? projectName) => cache is null
        ? MotifMarkdownOptions.Create(new FixedWritingSystems("en", "en"))
        : MotifMarkdownOptions.For(cache, projectName);
}

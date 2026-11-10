using SIL.LCModel;
using SIL.Motif.Contract.Responses;
using SIL.TextEditing.Markdown;
using SIL.TextEditing.Project;

namespace SIL.Motif.Host.RichText;

/// <summary>A Rationale or Comment as it is to be stored, and what a reader would see as wrong in it.</summary>
/// <param name="Text">The canonical FieldWorks Markdown when the text is clean; otherwise the text exactly as written.</param>
/// <param name="Findings">Only what a reader would see as wrong (ADR 0099 decision 4); empty when the text is clean.</param>
public sealed record CheckedProposalText(string Text, IReadOnlyList<TextFinding> Findings)
{
    /// <summary>Whether the text has no findings.</summary>
    public bool IsClean => Findings.Count == 0;
}

/// <summary>
/// Checks a Proposal's Rationale or Comment as FieldWorks Markdown (ADR 0099). Unmarked text is the project's default
/// analysis writing system. Findings are only what a reader would see as wrong: unreadable markup, a link to an object
/// the project does not have, an unknown writing system or style. A clean text is stored in its canonical spelling,
/// without front matter; a text with findings is kept exactly as written, so nothing is lost and the findings' lines
/// and columns still point into it.
/// </summary>
public static class ProposalTextChecker
{
    // Left out: spelling notes (stale path, rebuilt URL), suggestions, noncharacters and unknown property numbers.
    private static readonly HashSet<string> ReaderVisible = new(StringComparer.Ordinal)
    {
        MarkdownDiagnosticIds.UnclosedMark,
        MarkdownDiagnosticIds.UnknownAttribute,
        MarkdownDiagnosticIds.DuplicateAttribute,
        MarkdownDiagnosticIds.UnsupportedMarkup,
        MarkdownDiagnosticIds.MalformedAttribute,
        MarkdownDiagnosticIds.TooDeep,
        MarkdownDiagnosticIds.VersionUnsupported,
        MarkdownDiagnosticIds.RecordFile,
        MarkdownDiagnosticIds.FieldBlockSyntax,
        MarkdownDiagnosticIds.HeadingTooDeep,
        MarkdownDiagnosticIds.TableRagged,
        MarkdownDiagnosticIds.TableTabInCell,
        MarkdownDiagnosticIds.TableAlignment,
        MarkdownDiagnosticIds.WsUnknown,
        MarkdownDiagnosticIds.StyleUnknown,
        MarkdownDiagnosticIds.LinkNotFound,
        MarkdownDiagnosticIds.LinkAmbiguous,
        MarkdownDiagnosticIds.LinkBroken,
        MarkdownDiagnosticIds.LinkGuidMismatch,
        MarkdownDiagnosticIds.LinkRelativeWithoutBase,
        MarkdownDiagnosticIds.PictureUnknown,
    };

    /// <summary>
    /// Whether <paramref name="text"/> says anything only a project can check: a link, or an attribute naming a writing
    /// system or style, or anything read without a project finds wrong. Other text reads and checks the same with or
    /// without one.
    /// </summary>
    public static bool NeedsProject(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Contains('{')) return true;
        var parsed = MarkdownConverter.Parse(text, Options(null, null));
        return parsed.Objects.Count > 0 || parsed.Diagnostics.Count > 0;
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
        var findings = parsed.Diagnostics.Where(diagnostic => ReaderVisible.Contains(diagnostic.Id))
            .Select(diagnostic => new TextFinding(diagnostic.Id, diagnostic.Line, diagnostic.Column, diagnostic.Message)
            {
                Observed = diagnostic.Observed,
                Replacement = diagnostic.Replacement,
            }).ToArray();
        if (findings.Length > 0) return new CheckedProposalText(text, findings);
        return new CheckedProposalText(Canonical(text, parsed.Document, options), []);
    }

    // The writer checks each paragraph by reading it back; a text it cannot write is kept as written.
    private static string Canonical(string text, SIL.TextEditing.Document document, MarkdownOptions options)
    {
        try
        {
            var canonical = MarkdownConverter.ToMarkdown(document, options).TrimEnd('\n');
            return canonical.Length == 0 ? text : canonical;
        }
        catch (MarkdownWriteException)
        {
            return text;
        }
    }

    private static MarkdownOptions Options(LcmCache? cache, string? projectName)
    {
        var options = new MarkdownOptions { WriteFrontMatter = false, UnknownGuids = UnknownGuidPolicy.MarkBroken };
        if (cache is null) return options;
        options.WritingSystems = new ProjectWritingSystems(cache);
        options.Styles = new ProjectStyles(cache);
        options.Paths = new ProjectPathResolver(cache);
        options.ProjectName = projectName;
        return options;
    }
}

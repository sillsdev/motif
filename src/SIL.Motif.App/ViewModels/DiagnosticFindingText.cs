using System.Globalization;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>One run of a finding's sentence: plain text, or the text of a named FieldWorks object that is a link.</summary>
/// <param name="Text">The words shown.</param>
/// <param name="Link">The object the words name when they are a link; null for plain text.</param>
public sealed record FindingSegment(string Text, GrammarWarningPart? Link = null);

/// <summary>What one finding says, written once as a sentence with the FieldWorks objects it names as links in place.</summary>
public static class FindingSentence
{
    /// <summary>
    /// Splits the finding's sentence around each linked object it names, in the order they appear. An object the
    /// sentence does not mention is added after it, comma-separated, so a link is never dropped.
    /// </summary>
    internal static IReadOnlyList<FindingSegment> Segments(GrammarWarning display)
    {
        var text = display.Description.Length > 0 ? display.Description : display.Text;
        var links = display.Subject.Where(IsLink).DistinctBy(part => part.FieldWorksLink, StringComparer.Ordinal).ToArray();
        var segments = new List<FindingSegment>();
        var unplaced = new List<GrammarWarningPart>();
        var position = 0;
        foreach (var part in links)
        {
            var name = part.Text;
            var index = name.Length == 0 ? -1 : text.IndexOf($"'{name}'", position, StringComparison.Ordinal) is var quoted and >= 0
                ? quoted + 1 : text.IndexOf(name, position, StringComparison.Ordinal);
            if (index < 0) { unplaced.Add(part); continue; }
            if (index > position) segments.Add(new FindingSegment(text[position..index]));
            segments.Add(new FindingSegment(name, part));
            position = index + name.Length;
        }
        if (position < text.Length) segments.Add(new FindingSegment(text[position..]));
        for (var i = 0; i < unplaced.Count; i++)
        {
            segments.Add(new FindingSegment(i == 0 ? " " : MotifAppStrings.WarningsListSeparator + " "));
            segments.Add(new FindingSegment(unplaced[i].Text, unplaced[i]));
        }
        return segments;
    }

    private static bool IsLink(GrammarWarningPart part) =>
        part.Status == GrammarSubjectStatus.Object && part.Role == GrammarWarningPartRole.Object &&
        Uri.TryCreate(part.FieldWorksLink, UriKind.Absolute, out var link) &&
        string.Equals(link.Scheme, "silfw", StringComparison.OrdinalIgnoreCase);
}

/// <summary>The one wording of finding counts by level, shared by the Diagnostics summary and the status bar.</summary>
public static class DiagnosticFindingText
{
    /// <summary>"136 errors, 11 warnings, 4 information findings", with no closing period.</summary>
    public static string Breakdown(int errors, int warnings, int information) => string.Format(CultureInfo.CurrentCulture,
        MotifAppStrings.HealthBreakdownFormat,
        PluralText.Format(MotifAppStrings.WarningsErrorsPattern, errors),
        PluralText.Format(MotifAppStrings.WarningsWarningsPattern, warnings),
        PluralText.Format(MotifAppStrings.WarningsInformationPattern, information));

    /// <summary>"136 errors, 11 warnings": the two levels that ask something of a person, for the status bar.</summary>
    public static string Attention(int errors, int warnings) => string.Format(CultureInfo.CurrentCulture,
        MotifAppStrings.HealthBarFormat,
        PluralText.Format(MotifAppStrings.WarningsErrorsPattern, errors),
        PluralText.Format(MotifAppStrings.WarningsWarningsPattern, warnings));
}

using SIL.Motif.Host.PanGloss;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

internal sealed record PanGlossWarningText(
    string Title,
    string Description,
    string Explanation,
    string Guidance,
    string FieldWorksPlaces,
    string HelpBody,
    Uri? HelpUrl)
{
    public static PanGlossWarningText From(GrammarWarning warning)
    {
        var places = warning.FieldWorksPlaces
            .Select(place => place.Describe(FieldWorksLinks.ToolName))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var helpUrl = warning.HelpUrl is { } address ? new Uri(address) : null;
        return new(warning.Title ?? warning.Group ?? warning.CodeLabel, warning.Description,
            warning.Explanation ?? string.Empty, warning.Guidance ?? string.Empty,
            string.Join("; ", places), warning.HelpBody ?? string.Empty, helpUrl);
    }
}

/// <summary>Projects captured warning identities to readable display text while retaining the stored report.</summary>
internal static class WarningDisplayProjection
{
    private static readonly System.Text.RegularExpressions.Regex RawIdentifier = new(
        "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static string CapturedName(GrammarWarningPart part)
    {
        // A phoneme is often written as punctuation, so its name is quoted to stay readable as a name.
        if (IsPhoneme(part) && part.Title is { Length: > 0 } phoneme && !Guid.TryParse(phoneme, out _))
            return part.Subtitle is { Length: > 0 } set ? $"'{phoneme}' ({set})" : $"'{phoneme}'";
        if (!string.IsNullOrWhiteSpace(part.Text) && !Guid.TryParse(part.Text, out _)) return part.Text;
        if (string.IsNullOrWhiteSpace(part.Title) || Guid.TryParse(part.Title, out _)) return part.Text;
        return part.Subtitle is { Length: > 0 } subtitle ? $"{part.Title} ({subtitle})" : part.Title;
    }

    private static bool IsPhoneme(GrammarWarningPart part) =>
        string.Equals(part.FieldWorksKind, "PhPhoneme", StringComparison.Ordinal);

    // PanGloss writes phoneme names bare into its sentence, where a punctuation name reads as nothing.
    private static string QuotePhonemeList(string value, IReadOnlyList<string> names)
    {
        if (value.Length == 0 || names.Count == 0) return value;
        var bare = string.Join(", ", names);
        var index = value.IndexOf(bare, StringComparison.Ordinal);
        return index < 0 || index > 0 && value[index - 1] == '\'' ? value
            : value.Remove(index, bare.Length).Insert(index, string.Join(", ", names.Select(name => $"'{name}'")));
    }

    /// <summary>Returns a display copy using only the finding's captured subject names and identity aliases.</summary>
    internal static GrammarWarning For(GrammarWarning warning)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in warning.Subject.Concat(warning.Problem))
        {
            var name = CapturedName(part);
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (part.ObjectId is { Length: > 0 } objectId) names.TryAdd(objectId, name);
            if (part.SubjectGuid is { Length: > 0 } subjectGuid) names.TryAdd(subjectGuid, name);
        }

        var phonemes = warning.Subject.Concat(warning.Problem).Where(IsPhoneme)
            .Select(part => part.Title).OfType<string>().Where(title => title.Length > 0).ToArray();
        string Display(string value, bool unnamed = true) => QuotePhonemeList(RawIdentifier.Replace(value,
            match => names.TryGetValue(match.Value, out var name) ? name :
                unnamed ? MotifAppStrings.WarningsUnnamedItem : match.Value), phonemes);
        GrammarWarningPart Part(GrammarWarningPart part) => part with
        {
            Text = Display(CapturedName(part), part.Role is GrammarWarningPartRole.Text or GrammarWarningPartRole.Value),
        };
        return warning with
        {
            Description = Display(warning.Description),
            Explanation = warning.Explanation is { } explanation ? Display(explanation) : null,
            Guidance = warning.Guidance is { } guidance ? Display(guidance) : null,
            Text = Display(warning.Text),
            Subject = warning.Subject.Select(Part).ToArray(),
            Problem = warning.Problem.Select(Part).ToArray(),
        };
    }
}

using SIL.Motif.Contract.Responses;
using SIL.TextEditing.Markdown;

namespace SIL.Motif.Host.RichText;

/// <summary>Motif's FieldWorks Markdown options over the styles and writing systems in a GDR read response.</summary>
public static class GdrMarkdownOptions
{
    /// <summary>Uses the response's defaults and known names; object links name <paramref name="projectName"/>.</summary>
    public static MarkdownOptions For(GdrShowResponse response, string? projectName)
    {
        ArgumentNullException.ThrowIfNull(response);
        var catalog = new ResponseCatalog(response);
        return MotifMarkdownOptions.Create(catalog, catalog, projectName: projectName);
    }

    private sealed class ResponseCatalog(GdrShowResponse response) : IWritingSystemMapper, IKnownWritingSystems, IStyleCatalog, IKnownStyles
    {
        public string DefaultAnalysis { get; } = Default(response.DefaultAnalysis, response);
        public string DefaultVernacular { get; } = Default(response.DefaultVernacular, response);
        public IReadOnlyList<string> KnownTags { get; } = response.WritingSystems.Select(system => system.Tag).ToArray();
        public IReadOnlyList<string> ParagraphStyleNames { get; } = Names(response, GdrStyleKind.Paragraph);
        public IReadOnlyList<string> CharacterStyleNames { get; } = Names(response, GdrStyleKind.Character);
        public bool IsKnown(string tag) => KnownTags.Contains(tag, StringComparer.Ordinal);
        public bool IsParagraphStyle(string name) => ParagraphStyleNames.Contains(name, StringComparer.Ordinal);
        public bool IsCharacterStyle(string name) => CharacterStyleNames.Contains(name, StringComparer.Ordinal);

        // A response without defaults, such as one Definition alternative's, types in its first writing system.
        private static string Default(string tag, GdrShowResponse response) =>
            tag.Length > 0 ? tag : response.WritingSystems.FirstOrDefault()?.Tag ?? "en";

        private static string[] Names(GdrShowResponse response, GdrStyleKind kind) =>
            response.Styles.Where(style => style.Kind == kind).Select(style => style.Name).ToArray();
    }
}

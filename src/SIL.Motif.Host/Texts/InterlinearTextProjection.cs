namespace SIL.Motif.Host.Texts;

/// <summary>
/// Motif's own reading of one FieldWorks Text — the immutable spine both <see cref="FlexTextJsonWriter"/>
/// and <see cref="FlexTextXmlWriter"/> serialize, and both parse back into, so the two formats can never
/// drift from each other.
/// </summary>
/// <param name="Guid">The Text's own identity, carried through unchanged from LibLCM.</param>
/// <param name="Title">
/// The Text's name in every populated writing system, each becoming one <c>item type="title"</c>.
/// </param>
/// <param name="Paragraphs">
/// The Text's <see cref="SIL.LCModel.IText.ContentsOA"/>, in <see cref="SIL.LCModel.IStText.ParagraphsOS"/>
/// order — never reordered.
/// </param>
public sealed record InterlinearTextProjection(
    Guid Guid,
    IReadOnlyList<FlexItem> Title,
    IReadOnlyList<InterlinearParagraph> Paragraphs);

/// <param name="Guid">The paragraph's own identity.</param>
/// <param name="Phrases">The paragraph's segments, in <c>SegmentsOS</c> order.</param>
public sealed record InterlinearParagraph(Guid Guid, IReadOnlyList<InterlinearPhrase> Phrases);

/// <param name="Guid">The segment's own identity.</param>
/// <param name="Items">
/// Segment-level items — free and literal translation, when present — never the words themselves.
/// </param>
/// <param name="Words">The segment's analyses, in <c>AnalysesRS</c> order.</param>
public sealed record InterlinearPhrase(
    Guid Guid, IReadOnlyList<FlexItem> Items, IReadOnlyList<InterlinearWord> Words);

/// <param name="WordformGuid">
/// The linked <c>WfiWordform</c>'s identity, or <see langword="null"/> for a punctuation occurrence,
/// which LibLCM represents with no wordform at all.
/// </param>
/// <param name="AnalysisStatus">
/// One of <see cref="InterlinearAnalysisStatus"/>'s three values. Carried on the word here because that
/// is where this projection's callers read it; the FLExText schema itself places the corresponding
/// <c>analysisStatus</c> attribute on <c>morphemes</c>, present only when <paramref name="Morphemes"/> is
/// non-empty — see the writers.
/// </param>
/// <param name="Items">Word-level items — surface form, and category when an analysis chose one.</param>
/// <param name="Morphemes">
/// The chosen analysis's morph bundles in <c>MorphBundlesOS</c> order, empty for an unanalysed word or
/// punctuation.
/// </param>
public sealed record InterlinearWord(
    Guid? WordformGuid,
    string AnalysisStatus,
    IReadOnlyList<FlexItem> Items,
    IReadOnlyList<InterlinearMorpheme> Morphemes);

/// <param name="MorphGuid">The chosen allomorph's identity, or <see langword="null"/> when none is set.</param>
/// <param name="Items">Morpheme-level items — form, citation form, gloss, and category abbreviation.</param>
public sealed record InterlinearMorpheme(Guid? MorphGuid, IReadOnlyList<FlexItem> Items);

/// <summary>
/// One FLExText <c>item</c> element, collapsed to its three attributes: which kind of content it carries,
/// which writing system it is written in, and the text itself.
/// </summary>
public sealed record FlexItem(string Type, string Lang, string Value);

/// <summary>
/// The three states <see cref="InterlinearWord.AnalysisStatus"/> can hold. FLExText's own
/// <c>analysisStatusTypes</c> enumeration is finer (it also distinguishes how a guess was produced); this
/// projection keeps only the distinction Motif's writers and reader agree on, and the XML writer maps
/// <see cref="Approved"/> to <c>humanApproved</c> and <see cref="Unapproved"/> to <c>guess</c>.
/// </summary>
public static class InterlinearAnalysisStatus
{
    /// <summary>No analysis is chosen for this occurrence — a bare wordform or a punctuation form.</summary>
    public const string Unanalysed = "unanalysed";

    /// <summary>An analysis is chosen and the project's human agent has approved it.</summary>
    public const string Approved = "approved";

    /// <summary>An analysis is chosen but carries no human approval evaluation.</summary>
    public const string Unapproved = "unapproved";
}

/// <summary>
/// Derives a Handoff filename for one Text that cannot collide with another, even when two Texts share a
/// title or a title contains characters unsafe for a filename.
/// </summary>
public static class InterlinearTextFileNaming
{
    /// <param name="projection">The Text to name a file for.</param>
    /// <param name="extension">The suffix after the guid, e.g. <c>"flextext.json"</c>.</param>
    public static string BuildFileName(InterlinearTextProjection projection, string extension)
    {
        ArgumentNullException.ThrowIfNull(projection);
        if (string.IsNullOrWhiteSpace(extension)) throw new ArgumentException("Required.", nameof(extension));

        var title = projection.Title.Count > 0 ? projection.Title[0].Value : string.Empty;
        return $"{Sanitize(title)}-{projection.Guid:N}.{extension}";
    }

    private static string Sanitize(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "untitled";

        var invalid = Path.GetInvalidFileNameChars();
        var chars = title.Select(ch => invalid.Contains(ch) || char.IsWhiteSpace(ch) ? '_' : ch).ToArray();
        var sanitized = new string(chars).Trim('_', '.');
        return sanitized.Length == 0 ? "untitled" : sanitized;
    }
}

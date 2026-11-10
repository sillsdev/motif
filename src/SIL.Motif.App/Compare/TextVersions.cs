using System.Text;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Operations;
using SIL.TextEditing;

namespace SIL.Motif.App.Compare;

/// <summary>
/// One paragraph as one side holds it: its key (null for a paragraph that side made), paragraph style, the paragraph
/// properties other than its style, and its runs, each in the shape a <c>text/setParagraphs</c> payload carries.
/// </summary>
public sealed record ParagraphVersion(string? Key, string Style, TextProps Props, IReadOnlyList<TextRun> Runs)
{
    /// <summary>The paragraph's text.</summary>
    public string Text => string.Concat(Runs.Select(run => run.Text));

    /// <summary>Whether <paramref name="other"/> holds the same text, style, paragraph properties and runs, ignoring the key and normalisation.</summary>
    public bool SameContent(ParagraphVersion other) =>
        Style == other.Style && TextVersions.Signature(Props) == TextVersions.Signature(other.Props) &&
        TextVersions.Digest(Runs) == TextVersions.Digest(other.Runs);
}

/// <summary>
/// What a paragraph held when the person's edit began: its key, text and style, and its runs when they are known.
/// An edit remembers only a fingerprint of the starting runs, so the runs are known when one side still matches it.
/// </summary>
public sealed record StartingParagraph(string Key, string Text, string Style, ParagraphVersion? Exact);

/// <summary>
/// The three versions the sort compares: what the field held when the edit began, the person's version and the
/// FieldWorks version. A field made of one formatted string is one paragraph whose key is its writing system.
/// </summary>
public sealed record TextVersions(
    IReadOnlyList<StartingParagraph> Starting,
    IReadOnlyList<ParagraphVersion> Mine,
    IReadOnlyList<ParagraphVersion> FieldWorks)
{
    private const int NamedStyle = TextPropertyNumbers.NamedStyle;
    private const int RightToLeft = 128;

    /// <summary>The <see cref="TextRunsDigest"/> of <paramref name="runs"/>, as the project computes it for the same runs.</summary>
    public static string Digest(IEnumerable<TextRun> runs) => TextRunsDigest.Of(runs.Select(run =>
    {
        var strings = run.Props?.Strings.Where(pair => pair.Key != NamedStyle).ToDictionary() ?? [];
        if (run.CharacterStyle is { } style) strings[NamedStyle] = style;
        return new TextRunsDigest.RunFacts(run.Text, run.Ws,
            run.Props?.Ints.Select(pair => KeyValuePair.Create(pair.Key, pair.Value)) ?? [], strings, run.ObjData);
    }));

    /// <summary>A comparable form of <paramref name="props"/>; a right-to-left value of 0 reads as no value.</summary>
    public static string Signature(TextProps? props)
    {
        if (props is null) return "";
        var builder = new StringBuilder();
        foreach (var (number, (variation, value)) in props.Ints.OrderBy(pair => pair.Key))
            if (!(number == RightToLeft && value == 0)) builder.Append(number).Append('=').Append(variation).Append('/').Append(value).Append(';');
        foreach (var (number, value) in props.Strings.Where(pair => pair.Key != NamedStyle).OrderBy(pair => pair.Key))
            builder.Append(number).Append(':').Append(value).Append(';');
        return builder.ToString();
    }

    /// <summary>A run as a payload carries it: the character style apart and the other properties in a bag.</summary>
    public static TextRun Run(GdrRun run) => new(run.Text, run.WritingSystem, run.Style, new TextProps(
        run.Properties.Ints.ToDictionary(pair => pair.Key, pair => (pair.Value.Variation, pair.Value.Value)),
        run.Properties.Strings.Where(pair => pair.Key != NamedStyle).ToDictionary(pair => pair.Key, pair => pair.Value)), run.ObjData);

    /// <summary>A paragraph as <c>gdr show</c> returns it, in the payload's shape.</summary>
    public static ParagraphVersion Paragraph(GdrParagraph paragraph)
    {
        var ints = paragraph.Properties.Ints.ToDictionary(pair => pair.Key, pair => (pair.Value.Variation, pair.Value.Value));
        if (paragraph.RightToLeft) ints[RightToLeft] = (0, 1);
        else ints.Remove(RightToLeft);
        var strings = paragraph.Properties.Strings.Where(pair => pair.Key != NamedStyle).ToDictionary(pair => pair.Key, pair => pair.Value);
        return new(paragraph.Key, paragraph.Style ?? "", new TextProps(ints, strings), paragraph.Runs.Select(Run).ToArray());
    }

    /// <summary>
    /// The three versions of a StText field: the person's <paramref name="edit"/> (a <c>text/setParagraphs</c> payload)
    /// and the field as FieldWorks now holds it, <paramref name="live"/>. A paragraph the edit did not touch is the
    /// FieldWorks one on every side, since the edit does not remember it; one FieldWorks has since removed is left out.
    /// </summary>
    public static TextVersions FromParagraphs(TextParagraphsPayload edit, IReadOnlyList<GdrParagraph> live)
    {
        ArgumentNullException.ThrowIfNull(edit);
        ArgumentNullException.ThrowIfNull(live);
        var fieldWorks = live.Select(Paragraph).ToArray();
        var byKey = fieldWorks.Where(paragraph => paragraph.Key is not null).ToDictionary(paragraph => paragraph.Key!, StringComparer.Ordinal);
        var changed = edit.Changed.ToDictionary(change => change.Index);
        var mine = new List<ParagraphVersion>();
        var starting = new List<StartingParagraph>();
        for (var index = 0; index < edit.Order.Count; index++)
        {
            var key = edit.Order[index];
            if (changed.TryGetValue(index, out var change))
            {
                mine.Add(new ParagraphVersion(change.Key, change.Style ?? "", change.Props ?? TextProps.None, change.Runs));
                continue;
            }
            if (key is null || !byKey.TryGetValue(key, out var untouched)) continue;
            mine.Add(untouched);
            starting.Add(new StartingParagraph(key, untouched.Text, untouched.Style, untouched));
        }
        foreach (var (key, expected) in edit.Expected)
        {
            var ownVersion = mine.FirstOrDefault(paragraph => paragraph.Key == key);
            starting.Add(new StartingParagraph(key, expected.Text, expected.Style,
                Exact(expected.Text, expected.Style, expected.Runs, byKey.GetValueOrDefault(key), ownVersion)));
        }
        return new TextVersions(starting, mine, fieldWorks);
    }

    /// <summary>
    /// The three versions of one formatted string, or one writing system's alternative of a MultiString: the person's
    /// <paramref name="edit"/> and the runs FieldWorks now holds, <paramref name="live"/>, as one paragraph keyed <paramref name="key"/>.
    /// </summary>
    public static TextVersions FromRichString(TextRichStringPayload edit, IReadOnlyList<GdrRun> live, string key)
    {
        ArgumentNullException.ThrowIfNull(edit);
        ArgumentNullException.ThrowIfNull(live);
        var fieldWorks = new ParagraphVersion(key, "", TextProps.None, live.Select(Run).ToArray());
        var mine = new ParagraphVersion(key, "", TextProps.None, edit.Runs);
        var starting = new StartingParagraph(key, edit.Expected.Text, "",
            Exact(edit.Expected.Text, "", edit.Expected.Runs, fieldWorks, mine));
        return new TextVersions([starting], [mine], [fieldWorks]);
    }

    // The starting runs are whichever side's runs still match the remembered fingerprint, if either does.
    private static ParagraphVersion? Exact(string text, string style, string digest, ParagraphVersion? fieldWorks, ParagraphVersion? mine)
    {
        foreach (var side in new[] { fieldWorks, mine })
            if (side is not null && side.Style == style && Same(side.Text, text) && Digest(side.Runs) == digest) return side;
        return null;
    }

    internal static bool Same(string left, string right) =>
        string.Equals(left.Normalize(NormalizationForm.FormD), right.Normalize(NormalizationForm.FormD), StringComparison.Ordinal);
}

using System.Globalization;
using System.Text;
using System.Text.Json;
using SIL.Motif.Contract.Gdr;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.RichText;
using SIL.TextEditing;
using SIL.TextEditing.Markdown;

namespace SIL.Motif.Host.Gdr;

/// <summary>A field's Markdown and the read response it represents.</summary>
public sealed record GdrMarkdownField(string Name, string? WritingSystem, string? AlternativeWritingSystem,
    bool Inline, GdrShowResponse Response, string Body);

/// <summary>The whole-record FieldWorks Markdown view shared by the CLI and the window.</summary>
public sealed class GdrMarkdownView
{
    /// <summary>
    /// Builds the record from its read response. Optional Discussion paragraphs are project text, without view-only
    /// link decoration, so the window can include unsaved edits. No project is opened or changed.
    /// </summary>
    public GdrMarkdownView(GdrShowResponse shown, string? projectName, IReadOnlyList<Paragraph>? discussion = null)
    {
        ArgumentNullException.ThrowIfNull(shown);
        var id = CanonicalId.Parse(shown.Record.Id).ToGuid();
        Header = "---\nfwmd: 1\nrecord: " + JsonSerializer.Serialize("/lists/GDR/" + shown.Record.Abbreviation) +
            "\nguid: " + id.ToString("D") + "\nparent: " + JsonSerializer.Serialize(shown.Record.ParentId ?? "") +
            "\nposition: " + shown.Position.ToString(CultureInfo.InvariantCulture) + "\n---\n";
        var fields = new List<GdrMarkdownField>();
        AddAlternatives("title", shown.TitleAlternatives);
        AddAlternatives("description", shown.DescriptionAlternatives);
        Add("discussion", null, null, false, shown, discussion ?? ProjectParagraphs(shown));
        Fields = fields;
        var output = new StringBuilder(Header);
        foreach (var field in fields)
        {
            output.Append('\n').Append("::: {field=").Append(field.Name);
            if (field.WritingSystem is not null) output.Append(" ws=").Append(field.WritingSystem);
            output.Append("}\n").Append(field.Body).Append("\n:::\n");
        }
        Text = output.ToString();
        return;

        void AddAlternatives(string name, IReadOnlyList<RichStringAlternative> alternatives)
        {
            foreach (var alternative in alternatives.OrderBy(item => item.WritingSystem == shown.DefaultAnalysis ? 0 : 1))
            {
                var response = shown with { Discussion = [new GdrParagraph(null, false, alternative.WritingSystem,
                    new GdrTextProperties(new Dictionary<int, GdrIntProperty>(), new Dictionary<int, string>()), alternative.Runs)] };
                Add(name, alternative.WritingSystem == shown.DefaultAnalysis ? null : alternative.WritingSystem,
                    alternative.WritingSystem, true, response, ProjectParagraphs(response));
            }
        }

        void Add(string name, string? ws, string? alternativeWs, bool inline, GdrShowResponse response,
            IReadOnlyList<Paragraph> paragraphs)
        {
            var form = new FieldWorksMarkdownSourceForm(GdrMarkdownOptions.For(response, projectName));
            var copy = paragraphs.Select(paragraph => ShowDirection(paragraph.Clone())).ToArray();
            var (standIns, blocks) = inline ? (copy, []) : GdrQueryBlockMarkdown.LiftParagraphs(copy, shown.DefaultAnalysis);
            var body = form.Write(standIns, inline) ?? throw new InvalidOperationException($"The {name} cannot be shown as Markdown.");
            body = GuideInterlinear.Fold(GdrQueryBlockMarkdown.RestoreFences(body, blocks));
            fields.Add(new(name, ws, alternativeWs, inline, response, body));
        }
    }

    /// <summary>The read-only record identity, parent and position, with the FieldWorks Markdown version.</summary>
    public string Header { get; }

    /// <summary>The fields in document order, with default analysis alternatives first.</summary>
    public IReadOnlyList<GdrMarkdownField> Fields { get; }

    /// <summary>The document, using LF line endings and ending in a newline.</summary>
    public string Text { get; }

    /// <summary>The Discussion of <paramref name="response"/> as paragraphs with the properties and keys the project holds.</summary>
    public static Paragraph[] ProjectParagraphs(GdrShowResponse response) => response.Discussion.Select(source =>
    {
        var paragraph = new Paragraph
        {
            Props = Bag(source.Properties), Style = source.Style ?? string.Empty, Rtl = source.RightToLeft,
            DefaultWs = source.DefaultWritingSystem, Key = source.Key,
        };
        foreach (var run in source.Runs)
            paragraph.Runs.Add(new Run { Ws = run.WritingSystem, Text = run.Text, Props = Bag(run.Properties), ObjData = run.ObjData });
        return ShowDirection(paragraph);
    }).ToArray();

    private const int RightToLeftProperty = 128;
    private const int EnumVariation = 3;

    /// <summary>
    /// Spells a stored direction the way the notation writes it, as <c>rightToLeft=N</c>: FieldWorks stores ktptRightToLeft with
    /// the enum variation, which the notation would otherwise show as <c>p128=N:enum</c>. A paragraph with no stored direction
    /// stays without one, so a round trip changes nothing.
    /// </summary>
    internal static Paragraph ShowDirection(Paragraph paragraph)
    {
        if (paragraph.Rtl || paragraph.Props.IntProps.ContainsKey(RightToLeftProperty))
            paragraph.Props.IntProps[RightToLeftProperty] = new IntPropValue(0, paragraph.Rtl ? 1 : 0);
        return paragraph;
    }

    /// <summary>The direction as the project stores it: the enum variation FieldWorks writes, for left to right and right to left.</summary>
    public static int StoredDirectionVariation => EnumVariation;

    private static PropertyBag Bag(GdrTextProperties properties)
    {
        var bag = new PropertyBag();
        foreach (var (number, value) in properties.Ints) bag.SetInt(number, value.Value, value.Variation);
        foreach (var (number, value) in properties.Strings) bag.SetStr(number, value);
        return bag;
    }
}

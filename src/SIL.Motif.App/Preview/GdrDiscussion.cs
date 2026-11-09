using SIL.Motif.Contract.Responses;
using SIL.TextEditing;
using SIL.TextEditing.Avalonia;
using TextDocument = SIL.TextEditing.Document;

namespace SIL.Motif.App.Preview;

/// <summary>
/// What a <c>gdr show</c> response becomes for a text field: the Discussion as a document, the project's styles as a
/// resolver, and the project's writing systems as a registry. Nothing here reads a project.
/// </summary>
public sealed class GdrDiscussion
{
    private const float WritingSystemSizePoints = 12;

    /// <summary>The text that follows a link whose target is missing.</summary>
    public const string BrokenMarker = " (broken link)";

    // FieldWorks colours are 0x00BBGGRR.
    private const int BrokenColor = 0x0000C0, LinkColor = 0xC00000;

    private GdrDiscussion(TextDocument document, IStyleResolver styles, WritingSystemRegistry writingSystems,
        IReadOnlyDictionary<(int Paragraph, int Offset), GdrLinkTarget> targets)
    {
        Document = document;
        Styles = styles;
        WritingSystems = writingSystems;
        Targets = targets;
    }

    /// <summary>The Discussion's paragraphs, runs and their properties.</summary>
    public TextDocument Document { get; }

    /// <summary>Answers each style the Discussion uses, per writing system, with the project's resolved properties.</summary>
    public IStyleResolver Styles { get; }

    /// <summary>The writing systems the Discussion uses, with the project's direction and default font.</summary>
    public WritingSystemRegistry WritingSystems { get; }

    /// <summary>What each link run points to, keyed by the run's paragraph and the offset where it starts in the document.</summary>
    public IReadOnlyDictionary<(int Paragraph, int Offset), GdrLinkTarget> Targets { get; }

    /// <summary>What a person reads when a link is activated: the target's kind, name and description, or that it is broken.</summary>
    public static string Describe(string linkText, GdrLinkTarget target) => target.State switch
    {
        GdrLinkState.Resolved => $"{target.Kind}: {target.Name}" + (target.Description.Length > 0 ? $" ({target.Description})" : ""),
        GdrLinkState.External => $"{target.Name}",
        _ => $"'{linkText}' is a broken link: its target is not in this project.",
    };

    /// <summary>Whether <paramref name="run"/> is the marker this view adds after a broken link, which the project does not hold.</summary>
    public static bool IsMarker(Run run) =>
        run.Text == BrokenMarker && !run.Props.TryGetStr(TextPropertyNumbers.NamedStyle, out _) &&
        run.Props.TryGetInt(TextPropertyNumbers.ForeColor, out var color) && color == BrokenColor;

    /// <summary>The text of <paramref name="paragraph"/> as the project holds it, without the markers this view adds.</summary>
    public static string ProjectText(Paragraph paragraph) =>
        string.Concat(paragraph.Runs.Where(run => !IsMarker(run)).Select(run => run.Text));

    /// <summary>Builds the document, style resolver and writing-system registry for <paramref name="response"/>.</summary>
    public static GdrDiscussion From(GdrShowResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        var document = new TextDocument();
        var targets = new Dictionary<(int Paragraph, int Offset), GdrLinkTarget>();
        foreach (var source in response.Discussion)
        {
            var paragraph = new Paragraph
            {
                Props = Bag(source.Properties),
                Style = source.Style ?? string.Empty,
                Rtl = source.RightToLeft,
                DefaultWs = source.DefaultWritingSystem,
                Key = source.Key,
            };
            var offset = 0;
            foreach (var run in source.Runs)
            {
                var props = Bag(run.Properties);
                if (run.Target is { } target)
                {
                    targets[(document.Paragraphs.Count, offset)] = target;
                    props.SetInt(TextPropertyNumbers.Underline, TextPropertyNumbers.UnderlineSingle, 0);
                    props.SetInt(TextPropertyNumbers.ForeColor, target.State == GdrLinkState.Broken ? BrokenColor : LinkColor, 0);
                }
                paragraph.Runs.Add(new Run { Ws = run.WritingSystem, Text = run.Text, Props = props });
                offset += run.Text.Length;
                if (run.Target is { State: GdrLinkState.Broken })
                {
                    // The marker is text so a screen reader, which reads the field's text, says the link is broken too.
                    var marker = new Run { Ws = run.WritingSystem, Text = BrokenMarker, Props = new PropertyBag() };
                    marker.Props.SetInt(TextPropertyNumbers.Italic, 1, 0);
                    marker.Props.SetInt(TextPropertyNumbers.ForeColor, BrokenColor, 0);
                    paragraph.Runs.Add(marker);
                    offset += BrokenMarker.Length;
                }
            }
            document.Paragraphs.Add(paragraph);
        }

        var writingSystems = new WritingSystemRegistry();
        foreach (var system in response.WritingSystems)
            writingSystems.Add(WritingSystemRegistry.Define(system.Tag, system.DefaultFont, WritingSystemSizePoints,
                rtl: system.Direction == "rtl"));
        return new GdrDiscussion(document, new ResponseStyles(response.Styles), writingSystems, targets);
    }

    private static PropertyBag Bag(GdrTextProperties properties)
    {
        var bag = new PropertyBag();
        foreach (var (number, value) in properties.Ints) bag.SetInt(number, value.Value, value.Variation);
        foreach (var (number, value) in properties.Strings) bag.SetStr(number, value);
        return bag;
    }

    private sealed class ResponseStyles(IReadOnlyList<GdrStyle> styles) : IStyleResolver
    {
        public PropertyBag? Resolve(string styleName, string ws)
        {
            var style = styles.FirstOrDefault(item => item.Name == styleName);
            if (style is null) return null;
            // An unresolved writing system gets the style's first set, as the project does for an unknown tag.
            return style.ByWritingSystem.TryGetValue(ws, out var properties) ? Bag(properties)
                : style.ByWritingSystem.Values.Select(Bag).FirstOrDefault();
        }
    }
}

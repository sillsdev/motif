using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.RichText;
using SIL.Motif.Runner.Operations;
using SIL.TextEditing;
using SIL.TextEditing.Avalonia;
using SIL.TextEditing.Markdown;
using TextDocument = SIL.TextEditing.Document;

namespace SIL.Motif.App.Preview;

/// <summary>
/// What a <c>gdr show</c> response becomes for a text field: the Discussion as a document, the project's styles as a
/// resolver and as the choices an edit offers, and the project's writing systems as a registry. Nothing here reads a
/// project.
/// </summary>
public sealed class GdrDiscussion
{
    private const float WritingSystemSizePoints = 12;

    /// <summary>The text that follows a link whose target is missing.</summary>
    public const string BrokenMarker = " (broken link)";

    // FieldWorks colours are 0x00BBGGRR.
    private const int BrokenColor = 0x0000C0, LinkColor = 0xC00000;

    private const int RightToLeft = 128;

    // What a link's underline and colour were before this view showed it as a link, by link target.
    private readonly Dictionary<string, LinkLook> _linkLooks;

    // The targets of links the project could not resolve, which this view marks as broken.
    private readonly HashSet<string> _brokenLinks;

    private readonly ResponseCatalog _catalog;

    private GdrDiscussion(TextDocument document, IStyleResolver styles, WritingSystemRegistry writingSystems,
        IReadOnlyDictionary<(int Paragraph, int Offset), GdrLinkTarget> targets,
        IReadOnlyDictionary<string, TextParagraphExpected> expected, Dictionary<string, LinkLook> linkLooks,
        HashSet<string> brokenLinks, GdrShowResponse response)
    {
        _brokenLinks = brokenLinks;
        _catalog = new ResponseCatalog(response);
        Document = document;
        Styles = styles;
        WritingSystems = writingSystems;
        Targets = targets;
        Expected = expected;
        _linkLooks = linkLooks;
        ParagraphStyles = response.Styles.Where(style => style.Kind == GdrStyleKind.Paragraph).Select(style => style.Name).ToArray();
        CharacterStyles = response.Styles.Where(style => style.Kind == GdrStyleKind.Character).Select(style => style.Name).ToArray();
        WritingSystemChoices = response.WritingSystems;
        Source = response.Discussion;
    }

    /// <summary>The paragraphs as the project returned them, before this view laid them out.</summary>
    public IReadOnlyList<GdrParagraph> Source { get; }

    /// <summary>The project's paragraph styles, which an edit may give a paragraph.</summary>
    public IReadOnlyList<string> ParagraphStyles { get; }

    /// <summary>The project's character styles, which an edit may give a run.</summary>
    public IReadOnlyList<string> CharacterStyles { get; }

    /// <summary>The project's writing systems, which an edit may give a run or type in.</summary>
    public IReadOnlyList<GdrWritingSystem> WritingSystemChoices { get; }

    /// <summary>The character style FieldWorks gives a link, which a new link carries when the project has it.</summary>
    public const string HyperlinkStyle = "Hyperlink";

    /// <summary>
    /// A run that links to a project object, shown as this view shows links; the project receives it with its writing
    /// system, <paramref name="objData"/> and, when the project has it, the <see cref="HyperlinkStyle"/> character style.
    /// </summary>
    public Run LinkRun(string text, string ws, string objData)
    {
        var props = new PropertyBag();
        if (CharacterStyles.Contains(HyperlinkStyle, StringComparer.Ordinal)) props.SetStr(TextPropertyNumbers.NamedStyle, HyperlinkStyle);
        _linkLooks.TryAdd(objData, LinkLook.Of(props));
        props.SetInt(TextPropertyNumbers.Underline, TextPropertyNumbers.UnderlineSingle, 0);
        props.SetInt(TextPropertyNumbers.ForeColor, LinkColor, 0);
        return new Run { Ws = ws, Text = text, Props = props, ObjData = objData };
    }

    /// <summary>What each stored paragraph held as the project returned it, by key: what an edit of it began from.</summary>
    public IReadOnlyDictionary<string, TextParagraphExpected> Expected { get; }

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

    /// <summary>
    /// The runs of <paramref name="paragraph"/> as the project is to hold them: without broken-link markers, and with a
    /// link run's own underline and colour in place of those this view shows links with, unless the person changed them.
    /// Every other property of a link run, its style included, is the run's as edited.
    /// </summary>
    public IReadOnlyList<TextRun> ProjectRuns(Paragraph paragraph) => paragraph.Runs.Where(run => !IsMarker(run)).Select(run =>
    {
        var props = run.ObjData is { } objData && run.Text != ObjectReplacement
            ? (_linkLooks.TryGetValue(objData, out var look) ? look : default).Restore(run.Props) : run.Props;
        var style = props.TryGetStr(TextPropertyNumbers.NamedStyle, out var named) && named.Length > 0 ? named : null;
        return new TextRun(run.Text, run.Ws, style, Props(props), run.ObjData);
    }).ToArray();

    /// <summary>The paragraph properties of <paramref name="paragraph"/> other than its style, with its direction.</summary>
    public static TextProps ParagraphProps(Paragraph paragraph)
    {
        var props = paragraph.Props.Clone();
        if (paragraph.Rtl || props.IntProps.ContainsKey(RightToLeft)) props.IntProps[RightToLeft] = new IntPropValue(0, paragraph.Rtl ? 1 : 0);
        return Props(props);
    }

    private static TextProps Props(PropertyBag bag) => new(
        bag.IntProps.ToDictionary(pair => pair.Key, pair => (pair.Value.Variation, pair.Value.Value)),
        bag.StrProps.Where(pair => pair.Key != TextPropertyNumbers.NamedStyle).ToDictionary(pair => pair.Key, pair => pair.Value));

    /// <summary>Builds the document, style resolver and writing-system registry for <paramref name="response"/>.</summary>
    public static GdrDiscussion From(GdrShowResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        var document = new TextDocument();
        var targets = new Dictionary<(int Paragraph, int Offset), GdrLinkTarget>();
        var expected = new Dictionary<string, TextParagraphExpected>(StringComparer.Ordinal);
        var linkLooks = new Dictionary<string, LinkLook>(StringComparer.Ordinal);
        var brokenLinks = new HashSet<string>(StringComparer.Ordinal);
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
                    if (run.ObjData is { } objData) linkLooks.TryAdd(objData, LinkLook.Of(props));
                    targets[(document.Paragraphs.Count, offset)] = target;
                    props.SetInt(TextPropertyNumbers.Underline, TextPropertyNumbers.UnderlineSingle, 0);
                    props.SetInt(TextPropertyNumbers.ForeColor, target.State == GdrLinkState.Broken ? BrokenColor : LinkColor, 0);
                }
                paragraph.Runs.Add(new Run { Ws = run.WritingSystem, Text = run.Text, Props = props, ObjData = run.ObjData });
                offset += run.Text.Length;
                if (run.Target is { State: GdrLinkState.Broken })
                {
                    if (run.ObjData is { } broken) brokenLinks.Add(broken);
                    paragraph.Runs.Add(Marker(run.WritingSystem));
                    offset += BrokenMarker.Length;
                }
            }
            document.Paragraphs.Add(paragraph);
            if (source.Key is { } key)
                expected[key] = new TextParagraphExpected(string.Concat(source.Runs.Select(run => run.Text)), source.Style ?? "",
                    TextRunsDigest.Of(source));
        }

        var writingSystems = new WritingSystemRegistry();
        foreach (var system in response.WritingSystems)
            writingSystems.Add(WritingSystemRegistry.Define(system.Tag, system.DefaultFont, WritingSystemSizePoints,
                rtl: system.Direction == "rtl"));
        return new GdrDiscussion(document, new ResponseStyles(response.Styles), writingSystems, targets, expected, linkLooks, brokenLinks, response);
    }

    /// <summary>
    /// The FieldWorks Markdown options for this text: unmarked text is the response's default analysis writing system,
    /// <c>.v</c> its default vernacular, and the response's writing systems and styles are the ones that exist. A link
    /// to an object keeps its GUID, since nothing here reads the project's paths. <paramref name="projectName"/> is the
    /// project a link names.
    /// </summary>
    public MarkdownOptions MarkdownOptions(string? projectName) => MotifMarkdownOptions.Create(_catalog, _catalog, projectName: projectName);

    /// <summary>
    /// <paramref name="paragraph"/> as the project holds it: without the broken-link markers this view adds, and with each
    /// link run's own underline and colour in place of those this view shows links with.
    /// </summary>
    public Paragraph AsProject(Paragraph paragraph)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        var copy = paragraph.Clone();
        for (var i = copy.Runs.Count - 1; i >= 0; i--)
            if (IsMarker(copy.Runs[i])) copy.Runs.RemoveAt(i);
        // A link this view has no look on record for had none of its own: it came from Markdown, which AsShown leaves alone.
        foreach (var run in copy.Runs)
            if (run.ObjData is { } objData && run.Text != ObjectReplacement)
                run.Props = (_linkLooks.TryGetValue(objData, out var look) ? look : default).Restore(run.Props);
        return copy;
    }

    /// <summary>
    /// <paramref name="paragraph"/>, as the project would hold it, shown as this view shows text: a link the project could
    /// not resolve followed by its marker, and a link with no underline or colour of its own underlined and coloured as
    /// this view shows links. A link that has its own, as Markdown can give it, keeps them, so what was typed is what the
    /// project receives; an underline or colour that is exactly this view's link look reads as that look. The inverse of
    /// <see cref="AsProject"/>; it records nothing, so reading half-typed Markdown changes no later reading.
    /// </summary>
    public Paragraph AsShown(Paragraph paragraph)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        var copy = paragraph.Clone();
        for (var i = 0; i < copy.Runs.Count; i++)
        {
            var run = copy.Runs[i];
            if (run.ObjData is not { } objData || run.Text == ObjectReplacement) continue;
            var broken = _brokenLinks.Contains(objData);
            var own = run.Props.IntProps.ContainsKey(TextPropertyNumbers.Underline) || run.Props.IntProps.ContainsKey(TextPropertyNumbers.ForeColor);
            if (!own)
            {
                run.Props.SetInt(TextPropertyNumbers.Underline, TextPropertyNumbers.UnderlineSingle, 0);
                run.Props.SetInt(TextPropertyNumbers.ForeColor, broken ? BrokenColor : LinkColor, 0);
            }
            if (broken) copy.Runs.Insert(++i, Marker(run.Ws));
        }
        return copy;
    }

    private const string ObjectReplacement = "\uFFFC";

    // The marker is text so a screen reader, which reads the field's text, says the link is broken too.
    private static Run Marker(string ws)
    {
        var marker = new Run { Ws = ws, Text = BrokenMarker, Props = new PropertyBag() };
        marker.Props.SetInt(TextPropertyNumbers.Italic, 1, 0);
        marker.Props.SetInt(TextPropertyNumbers.ForeColor, BrokenColor, 0);
        return marker;
    }

    private static PropertyBag Bag(GdrTextProperties properties)
    {
        var bag = new PropertyBag();
        foreach (var (number, value) in properties.Ints) bag.SetInt(number, value.Value, value.Variation);
        foreach (var (number, value) in properties.Strings) bag.SetStr(number, value);
        return bag;
    }

    // Two links to one target keep their own other properties; only these two are shared, and only as shown.
    private readonly record struct LinkLook(IntPropValue? Underline, IntPropValue? ForeColor)
    {
        public static LinkLook Of(PropertyBag props) => new(Int(props, TextPropertyNumbers.Underline), Int(props, TextPropertyNumbers.ForeColor));

        public PropertyBag Restore(PropertyBag shown)
        {
            var props = shown.Clone();
            Put(props, TextPropertyNumbers.Underline, Underline, value => value == TextPropertyNumbers.UnderlineSingle);
            Put(props, TextPropertyNumbers.ForeColor, ForeColor, value => value is LinkColor or BrokenColor);
            return props;
        }

        private static IntPropValue? Int(PropertyBag props, int number) =>
            props.IntProps.TryGetValue(number, out var value) ? value : null;

        // The view shows links with variation 0, so a value set any other way, as the toolbar sets it, is the person's.
        private static void Put(PropertyBag props, int number, IntPropValue? original, Func<int, bool> isShown)
        {
            if (!props.IntProps.TryGetValue(number, out var value) || value.Variation != 0 || !isShown(value.Value)) return;
            if (original is { } kept) props.IntProps[number] = kept;
            else props.IntProps.Remove(number);
        }
    }

    // What the response says exists, as the Markdown reader asks it: an unknown tag or style name is a finding.
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

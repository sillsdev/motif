using SIL.Motif.Runner.Operations;
using SIL.TextEditing;

namespace SIL.Motif.App.Compare;

/// <summary>Moves a <see cref="ParagraphVersion"/> into a text field's document and back.</summary>
public static class CompareDocuments
{
    private const int RightToLeft = 128;

    /// <summary>The field paragraph for <paramref name="version"/>, its key kept.</summary>
    public static Paragraph ToParagraph(ParagraphVersion version, string defaultWs = "en")
    {
        ArgumentNullException.ThrowIfNull(version);
        var paragraph = new Paragraph { Key = version.Key, Style = version.Style, Props = Bag(version.Props) };
        if (version.Style.Length > 0) paragraph.Props.SetStr(TextPropertyNumbers.NamedStyle, version.Style);
        paragraph.Rtl = paragraph.Props.TryGetInt(RightToLeft, out var rtl) && rtl != 0;
        foreach (var run in version.Runs)
        {
            var props = Bag(run.Props);
            if (run.CharacterStyle is { } style) props.SetStr(TextPropertyNumbers.NamedStyle, style);
            paragraph.Runs.Add(new Run { Text = run.Text, Ws = run.Ws, Props = props, ObjData = run.ObjData });
        }
        paragraph.DefaultWs = version.Runs.Count > 0 ? version.Runs[0].Ws : defaultWs;
        return paragraph;
    }

    /// <summary>The version a field paragraph holds, its key kept when it is a string.</summary>
    public static ParagraphVersion FromParagraph(Paragraph paragraph)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        var runs = paragraph.Runs.Where(run => run.Text.Length > 0).Select(run =>
        {
            var style = run.Props.TryGetStr(TextPropertyNumbers.NamedStyle, out var named) && named.Length > 0 ? named : null;
            return new TextRun(run.Text, run.Ws, style, Props(run.Props), run.ObjData);
        }).ToArray();
        var props = paragraph.Props.Clone();
        if (paragraph.Rtl) props.SetInt(RightToLeft, 1);
        else props.IntProps.Remove(RightToLeft);
        return new ParagraphVersion(paragraph.Key as string, paragraph.Style, Props(props), runs);
    }

    private static PropertyBag Bag(TextProps? props)
    {
        var bag = new PropertyBag();
        if (props is null) return bag;
        foreach (var (number, (variation, value)) in props.Ints) bag.SetInt(number, value, variation);
        foreach (var (number, value) in props.Strings) bag.SetStr(number, value);
        return bag;
    }

    private static TextProps Props(PropertyBag bag) => new(
        bag.IntProps.ToDictionary(pair => pair.Key, pair => (pair.Value.Variation, pair.Value.Value)),
        bag.StrProps.Where(pair => pair.Key != TextPropertyNumbers.NamedStyle).ToDictionary(pair => pair.Key, pair => pair.Value));
}

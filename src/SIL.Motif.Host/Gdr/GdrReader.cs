using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.TextEditing;
using SIL.TextEditing.LCModel;
using SIL.TextEditing.Markdown;
using SIL.TextEditing.Project;

namespace SIL.Motif.Host.Gdr;

/// <summary>
/// Reads Grammar Design Records from an open project copy: the possibility list named
/// <see cref="ListName"/>, each record's title, number and abstract, and a Discussion as paragraphs of runs with the
/// project's own styles and writing systems resolved beside them. It never opens a unit of work.
/// </summary>
public static class GdrReader
{
    /// <summary>The name of the custom possibility list that holds the records.</summary>
    public const string ListName = "Grammar Design Records";

    /// <summary>The records in list order; empty when the project has no such list.</summary>
    public static IReadOnlyList<GdrSummary> List(LcmCache cache) =>
        Records(cache).Select(Summary).ToArray();

    /// <summary>The record whose number or canonical id is <paramref name="id"/>, or null.</summary>
    public static GdrShowResponse? Show(LcmCache cache, string id)
    {
        var record = Records(cache).FirstOrDefault(item => Matches(item, id));
        if (record is null) return null;

        var writingSystems = new ProjectWritingSystems(cache);
        var styles = new ProjectStyles(cache);
        var paragraphs = Paragraphs(record);
        var document = TsStringConversion.ToDocument(paragraphs, writingSystems);

        var wsTags = new SortedSet<string>(StringComparer.Ordinal) { writingSystems.DefaultVernacular, writingSystems.DefaultAnalysis };
        var styleKinds = new SortedDictionary<string, GdrStyleKind>(StringComparer.Ordinal);
        var converted = document.Paragraphs.Select(paragraph =>
        {
            wsTags.Add(paragraph.DefaultWs);
            if (paragraph.Style.Length > 0) NoteStyle(styles, styleKinds, paragraph.Style);
            return new GdrParagraph(paragraph.Style.Length > 0 ? paragraph.Style : null, paragraph.Rtl, paragraph.DefaultWs,
                Properties(paragraph.Props), paragraph.Runs.Select(run =>
                {
                    wsTags.Add(run.Ws);
                    var style = run.Props.TryGetStr(TextPropertyNumbers.NamedStyle, out var named) && named.Length > 0 ? named : null;
                    if (style is not null) NoteStyle(styles, styleKinds, style);
                    return new GdrRun(run.Text, run.Ws, style, Properties(run.Props));
                }).ToArray());
        }).ToArray();

        return new GdrShowResponse(Summary(record), converted)
        {
            DefaultVernacular = writingSystems.DefaultVernacular,
            DefaultAnalysis = writingSystems.DefaultAnalysis,
            Styles = styleKinds.Select(pair => new GdrStyle(pair.Key, pair.Value, wsTags.ToDictionary(tag => tag,
                tag => Properties(styles.Resolve(pair.Key, tag)!), StringComparer.Ordinal))).ToArray(),
            WritingSystems = wsTags.Select(tag =>
            {
                var info = writingSystems.DescribeWritingSystem(tag) ?? WritingSystemInfo.Default(tag);
                return new GdrWritingSystem(tag, info.Direction, info.Font);
            }).ToArray(),
        };
    }

    private static IEnumerable<ICmPossibility> Records(LcmCache cache)
    {
        var list = cache.ServiceLocator.GetInstance<ICmPossibilityListRepository>().AllInstances()
            .FirstOrDefault(candidate => Texts(candidate.Name).Any(name => name == ListName));
        return list?.PossibilitiesOS ?? Enumerable.Empty<ICmPossibility>();
    }

    private static IEnumerable<string> Texts(IMultiUnicode text) =>
        text.AvailableWritingSystemIds.Select(ws => text.get_String(ws).Text ?? "");

    private static string Best(IMultiUnicode text) => text.BestAnalysisVernacularAlternative.Text ?? "";

    private static GdrSummary Summary(ICmPossibility record) => new(
        CanonicalId.FromGuid(record.Guid).Value, record.Abbreviation.BestAnalysisAlternative.Text ?? "",
        Best(record.Name), record.Description.BestAnalysisAlternative.Text ?? "");

    private static bool Matches(ICmPossibility record, string id) =>
        string.Equals(record.Abbreviation.BestAnalysisAlternative.Text, id, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(CanonicalId.FromGuid(record.Guid).Value, id, StringComparison.Ordinal);

    private static IEnumerable<TextParagraph> Paragraphs(ICmPossibility record) =>
        record.DiscussionOA?.ParagraphsOS.OfType<IStTxtPara>()
            .Select(paragraph => new TextParagraph(paragraph.Guid, paragraph.Contents, paragraph.StyleRules))
        ?? Enumerable.Empty<TextParagraph>();

    private static void NoteStyle(ProjectStyles styles, SortedDictionary<string, GdrStyleKind> kinds, string name)
    {
        if (kinds.ContainsKey(name)) return;
        if (styles.IsParagraphStyle(name)) kinds[name] = GdrStyleKind.Paragraph;
        else if (styles.IsCharacterStyle(name)) kinds[name] = GdrStyleKind.Character;
    }

    private static GdrTextProperties Properties(PropertyBag bag) => new(
        bag.IntProps.ToDictionary(pair => pair.Key, pair => new GdrIntProperty(pair.Value.Variation, pair.Value.Value)),
        bag.StrProps.ToDictionary(pair => pair.Key, pair => pair.Value));
}

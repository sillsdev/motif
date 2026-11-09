using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.Motif.Contract.Commands;
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
/// project's own styles and writing systems resolved beside them. Sub-items are part of the outline and are listed
/// after their parent. It never opens a unit of work. An ambiguous list or abbreviation, or a style cycle, is thrown
/// as <see cref="GdrRefusalException"/>.
/// </summary>
public static class GdrReader
{
    /// <summary>The name of the custom possibility list that holds the records.</summary>
    public const string ListName = "Grammar Design Records";

    /// <summary>The records in list order; empty when the project has no such list.</summary>
    public static IReadOnlyList<GdrSummary> List(LcmCache cache) =>
        Records(cache).Select(entry => Summary(cache, entry)).ToArray();

    /// <summary>The record whose number or canonical id is <paramref name="id"/>, or null.</summary>
    public static GdrShowResponse? Show(LcmCache cache, string id)
    {
        var matches = Records(cache).Where(item => Matches(cache, item.Record, id)).ToArray();
        if (matches.Length == 0) return null;
        if (matches.Length > 1)
            throw new GdrRefusalException(new Refusal(RefusalCodes.GdrAmbiguous, FailureReason.Refused,
                $"More than one Grammar Design Record is named '{id}'. Name one by its canonical id."));
        var entry = matches[0];
        var record = entry.Record;

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
                    return new GdrRun(run.Text, run.Ws, style, Properties(run.Props), run.ObjData);
                }).ToArray());
        }).ToArray();

        return new GdrShowResponse(Summary(cache, entry), converted)
        {
            DefaultVernacular = writingSystems.DefaultVernacular,
            DefaultAnalysis = writingSystems.DefaultAnalysis,
            Styles = styleKinds.Select(pair => new GdrStyle(pair.Key, pair.Value, wsTags.ToDictionary(tag => tag,
                tag => Properties(Resolve(styles, pair.Key, tag)), StringComparer.Ordinal))).ToArray(),
            WritingSystems = wsTags.Select(tag =>
            {
                var info = writingSystems.DescribeWritingSystem(tag) ?? WritingSystemInfo.Default(tag);
                return new GdrWritingSystem(tag, info.Direction, info.Font);
            }).ToArray(),
        };
    }

    private readonly record struct Entry(ICmPossibility Record, ICmPossibility? Parent);

    private static IEnumerable<Entry> Records(LcmCache cache)
    {
        var lists = cache.ServiceLocator.GetInstance<ICmPossibilityListRepository>().AllInstances()
            .Where(candidate => Texts(candidate.Name).Any(name => name == ListName)).ToArray();
        if (lists.Length > 1)
            throw new GdrRefusalException(new Refusal(RefusalCodes.GdrAmbiguous, FailureReason.Refused,
                $"{lists.Length} possibility lists are named '{ListName}'. Rename or remove all but one in FieldWorks."));
        return lists.Length == 0 ? [] : Outline(lists[0].PossibilitiesOS, null);
    }

    private static IEnumerable<Entry> Outline(IEnumerable<ICmPossibility> items, ICmPossibility? parent)
    {
        foreach (var item in items)
        {
            yield return new Entry(item, parent);
            foreach (var child in Outline(item.SubPossibilitiesOS, item)) yield return child;
        }
    }

    private static IEnumerable<string> Texts(IMultiAccessorBase text) =>
        text.AvailableWritingSystemIds.Select(ws => text.get_String(ws).Text ?? "");

    // BestAnalysisAlternative answers "***" for an empty field, which is not content a record has.
    private static string Real(LcmCache cache, IMultiAccessorBase text)
    {
        foreach (var ws in new[] { cache.DefaultAnalWs, cache.DefaultVernWs }.Concat(text.AvailableWritingSystemIds))
        {
            var value = text.get_String(ws).Text;
            if (!string.IsNullOrEmpty(value)) return value;
        }
        return "";
    }

    private static GdrSummary Summary(LcmCache cache, Entry entry) => new(
        CanonicalId.FromGuid(entry.Record.Guid).Value, Real(cache, entry.Record.Abbreviation),
        Real(cache, entry.Record.Name), Real(cache, entry.Record.Description),
        entry.Parent is null ? null : CanonicalId.FromGuid(entry.Parent.Guid).Value);

    private static bool Matches(LcmCache cache, ICmPossibility record, string id)
    {
        var abbreviation = Real(cache, record.Abbreviation);
        return (abbreviation.Length > 0 && string.Equals(abbreviation, id, StringComparison.OrdinalIgnoreCase)) ||
               string.Equals(CanonicalId.FromGuid(record.Guid).Value, id, StringComparison.Ordinal);
    }

    private static PropertyBag Resolve(ProjectStyles styles, string name, string tag)
    {
        try
        {
            return styles.Resolve(name, tag)!;
        }
        catch (InvalidOperationException problem)
        {
            throw new GdrRefusalException(new Refusal(RefusalCodes.GdrStyleCycle, FailureReason.StoreInconsistent,
                $"The project's styles cannot be resolved. {problem.Message}"));
        }
    }

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

/// <summary>A Grammar Design Record read that the project's contents make impossible, carrying the refusal to return.</summary>
public sealed class GdrRefusalException(Refusal refusal) : Exception(refusal.Message)
{
    /// <summary>The refusal the command answers with.</summary>
    public Refusal Refusal { get; } = refusal;
}

using System.Text;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.PanGloss;
using SIL.TextEditing;
using SIL.TextEditing.LCModel;
using SIL.TextEditing.Markdown;
using SIL.TextEditing.Project;

namespace SIL.Motif.Host.Gdr;

/// <summary>
/// Reads Grammar Design Records from an open project copy: the possibility list named <see cref="ListName"/>, each
/// record's title, number and abstract, and a Discussion as paragraphs of runs. Every style and writing system of the
/// project, and any other the Discussion uses, is resolved beside them. Sub-items are part of the outline and are listed
/// after their parent. It never opens a unit of work. An ambiguous list or abbreviation, or a style cycle, is thrown as
/// <see cref="GdrRefusalException"/>.
/// </summary>
public static class GdrReader
{
    /// <summary>The name of the custom possibility list that holds the records.</summary>
    public const string ListName = "Grammar Design Records";

    /// <summary>The records in list order; empty when the project has no such list.</summary>
    public static IReadOnlyList<GdrSummary> List(LcmCache cache) =>
        Records(cache).Select(entry => Summary(cache, entry)).ToArray();

    /// <summary>
    /// The records with their live Discussion paragraphs, for the sync check (ADR 0119), which reads styles, runs and
    /// links straight from the project and needs no wire conversion.
    /// </summary>
    internal static IReadOnlyList<GdrStructure> ReadStructure(LcmCache cache) => Records(cache).Select(entry =>
    {
        var abbreviation = Real(cache, entry.Record.Abbreviation);
        var title = Real(cache, entry.Record.Name);
        return new GdrStructure(entry.Record.Guid, abbreviation, title, abbreviation.Length > 0 ? abbreviation : title,
            entry.Record.DiscussionOA?.ParagraphsOS.OfType<IStTxtPara>().ToArray() ?? []);
    }).ToArray();

    /// <summary>
    /// The record whose number or canonical id is <paramref name="id"/>, or null. A link naming the project
    /// <paramref name="projectName"/>, or the open copy's own name, is a link into this project.
    /// </summary>
    public static GdrShowResponse? Show(LcmCache cache, string id, string? projectName = null)
    {
        var wanted = id.Normalize(NormalizationForm.FormD);
        var matches = Records(cache).Where(item => Matches(cache, item.Record, wanted)).ToArray();
        if (matches.Length == 0) return null;
        if (matches.Length > 1)
            throw new GdrRefusalException(new Refusal(RefusalCodes.GdrAmbiguous, FailureReason.Refused,
                $"More than one Grammar Design Record is named '{id}'. Name one by its canonical id."));
        var entry = matches[0];
        var record = entry.Record;

        var writingSystems = new ProjectWritingSystems(cache);
        var resolver = new ProjectPathResolver(cache);
        var styles = new ProjectStyles(cache);
        var paragraphs = Paragraphs(record);
        var document = TsStringConversion.ToDocument(paragraphs, writingSystems);

        // The project's own styles and writing systems are what an edit may choose from; the Discussion may use others.
        var wsTags = new SortedSet<string>(writingSystems.KnownTags, StringComparer.Ordinal)
            { writingSystems.DefaultVernacular, writingSystems.DefaultAnalysis };
        var styleKinds = new SortedDictionary<string, GdrStyleKind>(StringComparer.Ordinal);
        foreach (var name in styles.ParagraphStyleNames) styleKinds.TryAdd(name, GdrStyleKind.Paragraph);
        foreach (var name in styles.CharacterStyleNames) styleKinds.TryAdd(name, GdrStyleKind.Character);
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
                    return new GdrRun(run.Text, run.Ws, style, Properties(run.Props), run.ObjData,
                        Target(resolver, run.ObjData, cache.ProjectId.Name, projectName));
                }).ToArray(), paragraph.Key is Guid key ? CanonicalId.FromGuid(key).Value : null);
        }).ToArray();

        var resolved = ResolveAll(styles, styleKinds.Keys, wsTags);
        return new GdrShowResponse(Summary(cache, entry), converted)
        {
            DefaultVernacular = writingSystems.DefaultVernacular,
            DefaultAnalysis = writingSystems.DefaultAnalysis,
            Styles = styleKinds.Select(pair => new GdrStyle(pair.Key, pair.Value, wsTags.ToDictionary(tag => tag,
                tag => Properties(resolved[pair.Key][tag]), StringComparer.Ordinal))).ToArray(),
            WritingSystems = wsTags.Select(tag =>
            {
                var info = writingSystems.DescribeWritingSystem(tag) ?? WritingSystemInfo.Default(tag);
                return new GdrWritingSystem(tag, info.Direction, info.Font);
            }).ToArray(),
        };
    }

    /// <summary>
    /// The FieldWorks tool that lists the records: FieldWorks names a custom list's tool by the list's name without white
    /// space, followed by <c>Edit</c>.
    /// </summary>
    public const string ListTool = "GrammarDesignRecordsEdit";

    /// <summary>
    /// The object data FieldWorks stores in a run that links to the record <paramref name="canonicalId"/> of the project
    /// <paramref name="projectName"/>: a <c>silfw://</c> link naming the project and <see cref="ListTool"/>, as
    /// <see cref="FieldWorksLinks"/> builds them, which <c>gdr show</c> resolves back to that record.
    /// </summary>
    public static string LinkData(string projectName, string canonicalId)
    {
        ArgumentNullException.ThrowIfNull(projectName);
        return LinkData(projectName, new FieldWorksLinkTarget(ListTool, CanonicalId.Parse(canonicalId).ToGuid()));
    }

    /// <summary>
    /// A link to the object at the FieldWorks Markdown project path <paramref name="path"/> (ADR 0031), such as
    /// <c>/lexicon/entry-1(2)</c> or <c>/lists/list-1/item-1</c>, as a run's object data naming the project
    /// <paramref name="projectName"/> and the FieldWorks tool that opens the object. A sense opens at its entry, as
    /// FieldWorks' own links do. A path that is malformed, names nothing, names more than one object, or names an object
    /// no FieldWorks tool opens is thrown as <see cref="GdrRefusalException"/>.
    /// </summary>
    public static GdrPathLink LinkToPath(LcmCache cache, string projectName, string path)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(projectName);
        ArgumentNullException.ThrowIfNull(path);
        var written = path.Trim();
        ProjectPath? parsed;
        try
        {
            parsed = ProjectPath.Parse(written);
        }
        catch (ArgumentException)
        {
            parsed = null;
        }
        if (parsed is null || parsed.Segments.Count == 0 || parsed.Field is not null)
            throw LinkRefused(RefusalCodes.GdrLinkPathInvalid, FailureReason.InvalidArgument,
                "Write a project path that starts with /, such as /lexicon/entry or /lists/list/item.");
        var resolver = new ProjectPathResolver(cache);
        if (resolver.Resolve(parsed, out var candidates) is not { } guid)
            throw candidates.Count > 1
                ? LinkRefused(RefusalCodes.GdrLinkAmbiguous, FailureReason.Refused,
                    $"{candidates.Count} objects have the path {written}. Add a homograph number or the start of the object's id.")
                : LinkRefused(RefusalCodes.GdrLinkNotFound, FailureReason.NotFound, $"Nothing in the project has the path {written}.");
        var item = cache.ServiceLocator.ObjectRepository.GetObject(guid);
        var target = FieldWorksLinks.TargetFor(cache, item) ?? CustomListTarget(item) ??
            throw LinkRefused(RefusalCodes.GdrLinkNoTool, FailureReason.Refused,
                $"Motif knows no FieldWorks tool that opens {written}, so a link to it could not be followed.");
        var described = resolver.Describe(target.ObjectId)!;
        return new GdrPathLink(CanonicalId.FromGuid(target.ObjectId).Value, described.Kind, described.Name, described.Description,
            LinkData(projectName, target));
    }

    // FieldWorks lists a custom (unowned) list's items in a tool named for the list, as ListTool is.
    private static FieldWorksLinkTarget? CustomListTarget(ICmObject item)
    {
        if (item is not ICmPossibility possibility) return null;
        ICmObject owner = possibility;
        while (owner is ICmPossibility) owner = owner.Owner;
        return owner is ICmPossibilityList { Owner: null } list && Texts(list.Name).FirstOrDefault(name => name.Length > 0) is { } name
            ? new FieldWorksLinkTarget(string.Concat(name.Where(character => !char.IsWhiteSpace(character))) + "Edit", possibility.Guid)
            : null;
    }

    private static GdrRefusalException LinkRefused(string code, FailureReason reason, string message) =>
        new(new Refusal(code, reason, message));

    private static string LinkData(string projectName, FieldWorksLinkTarget target)
    {
        var url = FieldWorksLinks.ForTarget(projectName, target);
        var link = new MarkdownObject { Kind = MarkdownObjectKind.InternalLink, Guid = target.ObjectId, Url = url };
        DefaultObjectMapper.Instance.TryCreateObjData(link, out var objData, out _);
        return objData;
    }

    private static GdrLinkTarget? Target(ProjectPathResolver resolver, string? objData, params string?[] projectNames)
    {
        var link = objData is null ? null : DefaultObjectMapper.Instance.Describe(objData);
        if (link is null || link.Kind == MarkdownObjectKind.Picture) return null;
        if (link.Kind == MarkdownObjectKind.ExternalLink ||
            (link.Project is { } named && !projectNames.Contains(named, StringComparer.OrdinalIgnoreCase)))
            return new GdrLinkTarget(GdrLinkState.External, null, link.Url, "");
        var described = link.Guid is { } guid ? resolver.Describe(guid) : null;
        return described is null
            ? new GdrLinkTarget(GdrLinkState.Broken, null, null, "")
            : new GdrLinkTarget(GdrLinkState.Resolved, described.Kind, described.Name, described.Description);
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

    // The id arrives in NFD; the stored abbreviation may be composed either way.
    private static bool Matches(LcmCache cache, ICmPossibility record, string id)
    {
        var abbreviation = Real(cache, record.Abbreviation).Normalize(NormalizationForm.FormD);
        return (abbreviation.Length > 0 && string.Equals(abbreviation, id, StringComparison.OrdinalIgnoreCase)) ||
               string.Equals(CanonicalId.FromGuid(record.Guid).Value, id, StringComparison.Ordinal);
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, PropertyBag>> ResolveAll(ProjectStyles styles,
        IEnumerable<string> names, IEnumerable<string> tags)
    {
        try
        {
            return styles.ResolveAll(names, tags);
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

/// <summary>One record as the sync check reads it: its identity and its Discussion's paragraphs as stored.</summary>
/// <param name="Label">The abbreviation, or the title when the record has none; how a finding names it.</param>
internal sealed record GdrStructure(Guid Guid, string Abbreviation, string Title, string Label, IReadOnlyList<IStTxtPara> Paragraphs);

/// <summary>A link to a project object found by its path.</summary>
/// <param name="Id">The canonical id of the object the link opens.</param>
/// <param name="Kind">What the object is, in a person's words.</param>
/// <param name="Name">The object's name, which names the link.</param>
/// <param name="Description">A short description of the object; empty when it has none.</param>
/// <param name="ObjData">The run's object data, as FieldWorks stores a link.</param>
public sealed record GdrPathLink(string Id, string Kind, string Name, string Description, string ObjData)
{
    /// <summary>The Baseline the path was found in.</summary>
    public BaselineToken? Baseline { get; init; }

    /// <summary>Whether the live file has been saved since that Baseline, so the object may since have moved or gone.</summary>
    public bool IsStale { get; init; }
}

/// <summary>A Grammar Design Record read that the project's contents make impossible, carrying the refusal to return.</summary>
public sealed class GdrRefusalException(Refusal refusal) : Exception(refusal.Message)
{
    /// <summary>The refusal the command answers with.</summary>
    public Refusal Refusal { get; } = refusal;
}

using System.Collections.ObjectModel;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Gdr;
using SIL.Motif.Host.PanGloss;
using SIL.TextEditing.Markdown;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// One entry of the Grammar Guide's outline: a Grammar Design Record, with the records filed under it. A record at the
/// top of the list is a part, and one filed under another is a section of that part, as the project's own hierarchy has it.
/// </summary>
public sealed class GrammarGuideSection
{
    private GrammarGuideSection(GdrSummary record) => Record = record;

    /// <summary>The record as the project lists it.</summary>
    public GdrSummary Record { get; }

    /// <summary>The record's id.</summary>
    public string Id => Record.Id;

    /// <summary>What the outline calls the entry: the record's number and title, whichever it has.</summary>
    public string Label => string.Join("  ", new[] { Record.Abbreviation, Record.Title }.Where(text => !string.IsNullOrWhiteSpace(text)))
        is { Length: > 0 } label ? label : Record.Id;

    /// <summary>The part this section is filed under, or null for a part.</summary>
    public GrammarGuideSection? Parent { get; private set; }

    /// <summary>Whether the entry is a part: it is at the top of the project's list.</summary>
    public bool IsPart => Parent is null;

    /// <summary>The sections filed under this entry, in the project's order.</summary>
    public ObservableCollection<GrammarGuideSection> Children { get; } = [];

    /// <summary>
    /// The outline of <paramref name="records"/>, which the project lists depth first. A record whose parent is not in the
    /// list, or that names itself or one of its own sections as parent, is shown as a part rather than dropped.
    /// </summary>
    public static IReadOnlyList<GrammarGuideSection> Outline(IEnumerable<GdrSummary> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var nodes = new List<GrammarGuideSection>();
        var byId = new Dictionary<string, GrammarGuideSection>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            var node = new GrammarGuideSection(record);
            nodes.Add(node);
            byId.TryAdd(record.Id, node);
        }
        var parts = new List<GrammarGuideSection>();
        foreach (var node in nodes)
        {
            var parent = node.Record.ParentId is { } id && byId.TryGetValue(id, out var found) ? found : null;
            if (parent is null || parent == node || IsBelow(parent, node, byId))
            {
                parts.Add(node);
                continue;
            }
            node.Parent = parent;
            parent.Children.Add(node);
        }
        return parts;
    }

    // Whether following parents from candidate reaches node, which would make the two a loop.
    private static bool IsBelow(GrammarGuideSection candidate, GrammarGuideSection node, Dictionary<string, GrammarGuideSection> byId)
    {
        var steps = 0;
        for (var current = candidate; current is not null && steps <= byId.Count; steps++)
        {
            if (current == node) return true;
            current = current.Record.ParentId is { } id && byId.TryGetValue(id, out var next) ? next : null;
        }
        return false;
    }

    /// <summary>The entry, and every entry under it, depth first.</summary>
    public IEnumerable<GrammarGuideSection> Flatten()
    {
        yield return this;
        foreach (var child in Children)
        foreach (var below in child.Flatten())
            yield return below;
    }

    /// <summary>The headings of <paramref name="shown"/>'s Discussion: its paragraphs in a Heading style, with their level.</summary>
    public static IReadOnlyList<GuideHeading> HeadingsOf(GdrShowResponse? shown)
    {
        if (shown is null) return [];
        var headings = new List<GuideHeading>();
        foreach (var paragraph in shown.Discussion)
        {
            if (paragraph.Style is not { } style || !style.StartsWith("Heading", StringComparison.OrdinalIgnoreCase)) continue;
            var text = string.Concat(paragraph.Runs.Select(run => run.Text)).Trim();
            if (text.Length == 0) continue;
            var digits = new string(style.Where(char.IsDigit).ToArray());
            headings.Add(new GuideHeading(int.TryParse(digits, out var level) && level > 0 ? level : 1, text));
        }
        return headings;
    }
}

/// <summary>A heading in a section's text.</summary>
/// <param name="Level">1 for the top heading style, 2 for the next, and so on.</param>
/// <param name="Text">The heading's text.</param>
public sealed record GuideHeading(int Level, string Text)
{
    /// <summary>Whether the heading sits under a top heading.</summary>
    public bool IsSubheading => Level > 1;
}

/// <summary>
/// A grammar object a section's text links to, as the project resolved the link: its kind and current name, or that the
/// target is missing or deleted, or lies outside this project. The grammar itself is read and edited in FieldWorks.
/// </summary>
/// <param name="Text">What the link says in the section's text.</param>
/// <param name="Target">What the project resolved the link to.</param>
/// <param name="FieldWorksLink">The <c>silfw:</c> link that opens the object in FieldWorks, or null when the link names none.</param>
public sealed record GuideGrammarName(string Text, GdrLinkTarget Target, Uri? FieldWorksLink)
{
    /// <summary>Whether the target is an object of the project.</summary>
    public bool IsResolved => Target.State == GdrLinkState.Resolved;

    /// <summary>Whether the target is not in the project: it was deleted or never existed.</summary>
    public bool IsMissing => Target.State == GdrLinkState.Broken;

    /// <summary>The object's name, or the link's own text when the project could not name it.</summary>
    public string Name => Target.Name is { Length: > 0 } name ? name : Text;

    /// <summary>The kind of object, or empty when the project names none.</summary>
    public string Kind => Target.Kind ?? "";

    /// <summary>What the project says about the target: its kind, or that it is missing or outside the project.</summary>
    public string StateText => Target.State switch
    {
        GdrLinkState.Broken => MotifAppStrings.GuideLinkMissing,
        GdrLinkState.External => MotifAppStrings.GuideLinkOutside,
        _ => Kind,
    };

    /// <summary>Whether FieldWorks can open the object: it is in the project and the link names where.</summary>
    public bool CanOpen => IsResolved && FieldWorksLink is not null;

    /// <summary>The name the Open in FieldWorks button gives assistive technology.</summary>
    public string OpenName => string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.GuideOpenNameFormat, Name);

    /// <summary>
    /// The grammar objects <paramref name="shown"/>'s text links to, each once, in the order they first appear. Links to
    /// other Grammar Design Records are the Guide's own cross-references, not grammar, and are left out.
    /// </summary>
    public static IReadOnlyList<GuideGrammarName> From(GdrShowResponse? shown)
    {
        if (shown is null) return [];
        var names = new List<GuideGrammarName>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var run in shown.Discussion.SelectMany(paragraph => paragraph.Runs))
        {
            if (run.Target is not { } target || run.ObjData is not { } objData) continue;
            var link = DefaultObjectMapper.Instance.Describe(objData);
            if (link?.Tool == GdrReader.ListTool) continue;
            var uri = link?.Kind == MarkdownObjectKind.InternalLink && Uri.TryCreate(objData[1..], UriKind.Absolute, out var parsed) ? parsed : null;
            if (!seen.Add(objData)) continue;
            names.Add(new GuideGrammarName(run.Text, target, uri));
        }
        return names;
    }
}

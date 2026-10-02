using System.Xml;
using System.Web;
using SIL.LCModel;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Host.PanGloss;

/// <summary>
/// A query-owned inventory of the saved live project's identity and object GUIDs. Baseline facts remain separate:
/// a link is available only when this saved file belongs to the expected project and contains its destination.
/// No LibLCM cache or project lock is opened. Availability describes the saved file at the time of this read.
/// </summary>
public sealed class SavedProjectNavigation
{
    private readonly string _projectName;
    private readonly Dictionary<Guid, string> _objects;

    private SavedProjectNavigation(string path, string status, Dictionary<Guid, string> objects) =>
        (_projectName, ProjectIdentityStatus, _objects) = (Path.GetFileNameWithoutExtension(path), status, objects);

    /// <summary><c>match</c>, <c>mismatch</c>, or <c>unknown</c> when the complete saved identity cannot be read.</summary>
    public string ProjectIdentityStatus { get; }

    /// <summary>Whether the complete saved project identity matches the expected authored GUID.</summary>
    public bool ProjectMatches => ProjectIdentityStatus == "match";

    /// <summary>
    /// Reads one saved-file inventory. An unreadable or malformed file, duplicate object GUID, or missing or
    /// duplicate project identity leaves every destination unavailable; labels from a Baseline remain usable.
    /// </summary>
    public static SavedProjectNavigation Read(string path, string? expectedProjectIdentity)
    {
        var objects = new Dictionary<Guid, string>();
        if (!Guid.TryParse(expectedProjectIdentity, out var expected)) return new(path, "unknown", objects);
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
                { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            Guid? project = null;
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "rt") continue;
                if (!Guid.TryParse(reader.GetAttribute("guid"), out var id) ||
                    reader.GetAttribute("class") is not { Length: > 0 } kind || !objects.TryAdd(id, kind))
                    return new(path, "unknown", []);
                if (reader.GetAttribute("class") != "LangProject") continue;
                if (project is not null) return new(path, "unknown", []);
                project = id;
            }
            return new(path, project is null ? "unknown" : project == expected ? "match" : "mismatch", objects);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or XmlException)
        {
            return new(path, "unknown", []);
        }
    }

    /// <summary>A live link to a recorded destination, or null when this saved project cannot establish it.</summary>
    public string? LinkFor(FieldWorksLinkTarget? target) =>
        ProjectMatches && target is not null && _objects.TryGetValue(target.ObjectId, out var kind) &&
        Accepts(target.Tool, kind)
            ? FieldWorksLinks.ForTarget(_projectName, target) : null;

    private static bool Accepts(string tool, string kind) => tool.ToLowerInvariant() switch
    {
        "lexiconedit" => kind == "LexEntry",
        "posedit" => kind == "PartOfSpeech",
        "phonemeedit" => kind == "PhPhoneme",
        "naturalclassedit" => kind is "PhNCSegments" or "PhNCFeatures",
        "environmentedit" => kind == "PhEnvironment",
        "phonologicalruleedit" => kind is "PhRegularRule" or "PhMetathesisRule",
        "compoundruleadvancededit" => kind is "MoEndoCompound" or "MoExoCompound",
        "adhoccoprohibedit" => kind is "MoAlloAdhocProhib" or "MoMorphAdhocProhib",
        "analyses" => kind == "WfiWordform",
        "phonologicalfeaturesadvancededit" or "featuresadvancededit" =>
            kind is "FsClosedFeature" or "FsComplexFeature" or "FsOpenFeature",
        "variantentrytypeedit" => kind is "LexEntryType" or "LexEntryInflType",
        "prodrestrictedit" => kind == "CmPossibility",
        _ => false,
    };

    /// <summary>Rechecks a cached link against this saved project, without trusting its recorded filename.</summary>
    public string? VerifyLink(string? link)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) || uri.Scheme != "silfw") return null;
        var query = HttpUtility.ParseQueryString(HttpUtility.UrlDecode(uri.Query.TrimStart('?')));
        return Guid.TryParse(query["guid"], out var id) && query["tool"] is { Length: > 0 } tool
            ? LinkFor(new(tool, id)) : null;
    }

    /// <summary>Preserves captured morphology and labels while rechecking any cached entry links.</summary>
    public ParserReading Verify(ParserReading reading) => reading with
    {
        Morphs = reading.Morphs.Select(morph => morph with { FieldWorksLink = VerifyLink(morph.FieldWorksLink) }).ToArray(),
    };

    /// <summary>The verified live destination for a Baseline object, independent of its readable Baseline facts.</summary>
    public TraceFieldWorksTarget? TargetFor(LcmCache cache, ICmObject found)
    {
        var target = FieldWorksLinks.TargetFor(cache, found);
        return target is not null && LinkFor(target) is { } link
            ? new(target.Tool, FieldWorksLinks.ToolName(target.Tool), target.ObjectId.ToString("D"), link) : null;
    }

    /// <summary>Retains producer advice and labels while checking every reported warning destination.</summary>
    public GrammarCheckResponse Verify(GrammarCheckResponse check) => check with
    {
        Findings = check.Findings.Select(finding => finding with
        {
            Subject = finding.Subject.Select(Verify).ToArray(),
            Problem = finding.Problem.Select(Verify).ToArray(),
        }).ToArray(),
    };

    /// <summary>A reported warning part with its link limited to an available saved-file destination.</summary>
    public GrammarWarningPart Verify(GrammarWarningPart part) => part with
    {
        FieldWorksLink = part.FieldWorksLink is not null && Guid.TryParse(part.FieldWorksGuid, out var id) &&
            (part.FieldWorksTool ?? FieldWorksLinks.ToolOf(part.FieldWorksLink)) is { } tool
                ? LinkFor(new(tool, id)) : null,
    };
}

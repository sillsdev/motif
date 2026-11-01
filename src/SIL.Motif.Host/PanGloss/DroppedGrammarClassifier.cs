using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Host.PanGloss;

/// <summary>
/// Separates the grammar findings that say PanGloss dropped something it was given from every other finding, and
/// the ones a Proposal introduced from the ones the Baseline already had.
/// </summary>
public static class DroppedGrammarClassifier
{
    /// <summary>
    /// PanGloss's warning codes for grammar it continued past after leaving the object out: an environment, an
    /// allomorph, an entry, a rule, a natural class, a template slot, a compound rule, a stem name, an ad hoc
    /// prohibition, a null affix, a stratum, an entry or analysis with nothing loadable, a circumfix half or an
    /// unresolved reference. A closed list for specific wording only: any other finding at Warning or Error level
    /// is still reported, generically, and a pinned test fails when a repin renames a listed code or the
    /// recorded code list belongs to another release. Findings at Information level drop nothing and are ignored;
    /// one that the Baseline also has counts as pre-existing, so project-wide notes do not make a Trial unclean.
    /// </summary>
    public static IReadOnlySet<string> Codes { get; } = LoadCodes();

    // One code per line in an embedded resource: they are PanGloss warning codes, not Motif refusal codes.
    private static HashSet<string> LoadCodes()
    {
        using var stream = typeof(DroppedGrammarClassifier).Assembly.GetManifestResourceStream("DroppedGrammarCodes.txt")
            ?? throw new InvalidOperationException("The dropped-grammar code list is not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Classifies the candidate's dropped-grammar findings against the Baseline's.</summary>
    /// <param name="candidate">The grammar check of the candidate the Trial built.</param>
    /// <param name="baseline">The Baseline's own grammar check.</param>
    /// <param name="proposalObjects">The GUIDs of every object the Proposal creates or edits.</param>
    public static DroppedGrammar Classify(GrammarCheckResponse candidate, GrammarCheckResponse baseline,
        IReadOnlySet<Guid> proposalObjects)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(proposalObjects);
        var known = Reported(baseline).Select(Key).ToHashSet(StringComparer.Ordinal);
        var introduced = new List<DroppedGrammarFinding>();
        var preExisting = 0;
        foreach (var finding in Reported(candidate))
        {
            var objects = Objects(finding);
            var listed = Codes.Contains(finding.Code!);
            // Only a listed drop on an object the Proposal edits is the Proposal's even when the Baseline had it.
            var touched = listed && finding.Subject.Select(SubjectGuid).OfType<Guid>().Any(proposalObjects.Contains);
            if (touched || !known.Contains(Key(finding)))
            {
                introduced.Add(new DroppedGrammarFinding(finding.Code!, objects, Bound(listed ? finding.Description
                    : $"PanGloss reported {finding.Code} on {Names(objects)}: {finding.Description}", MaxMessageLength)!,
                    listed));
            }
            else
                preExisting++;
        }
        return new DroppedGrammar(introduced.Take(DroppedGrammar.MaxFindings).ToArray(), preExisting)
            { OmittedFindings = Math.Max(0, introduced.Count - DroppedGrammar.MaxFindings) };
    }

    // Listed codes plus unlisted import findings at Warning or above; unlisted check findings drop nothing.
    private static IEnumerable<GrammarWarning> Reported(GrammarCheckResponse response) =>
        response.Findings.Where(finding => finding.Code is { } code && (Codes.Contains(code) ||
            finding.Origin != GrammarFindingOrigin.Check && finding.Severity != GrammarDiagnosticLevel.Information &&
            !AboutWords(code)));

    // These warnings count stored analyses that stopped parsing, which is about words, not grammar objects.
    private static bool AboutWords(string code) =>
        code is PanGlossFindingCodes.StoredAnalysisIncomplete or PanGlossFindingCodes.StoredAnalysisBudgetReached;

    private static string Names(IReadOnlyList<DroppedGrammarObject> objects) => objects.Count == 0
        ? "an object PanGloss did not identify"
        : string.Join(", ", objects.Select(item => $"{item.Kind ?? "object"} {item.Name} ({item.MotifId})"));

    // Code and objects; with no object, listed codes use their text and others the code, as text names the file.
    private static string Key(GrammarWarning finding)
    {
        var ids = finding.Subject.Select(SubjectGuid).OfType<Guid>().Select(id => id.ToString("N"))
            .Order(StringComparer.Ordinal).ToArray();
        var rest = ids.Length > 0 ? string.Join(",", ids) : Codes.Contains(finding.Code!) ? finding.Description : "";
        return finding.Code + "|" + rest;
    }

    private static Guid? SubjectGuid(GrammarWarningPart part) =>
        Guid.TryParse(part.SubjectGuid ?? part.FieldWorksGuid, out var guid) ? guid : null;

    private static IReadOnlyList<DroppedGrammarObject> Objects(GrammarWarning finding) =>
        finding.Subject.Select(part => (part, guid: SubjectGuid(part))).Where(item => item.guid is not null)
            .Select(item => new DroppedGrammarObject(CanonicalId.FromGuid(item.guid!.Value).Value,
                item.part.SourceClass ?? item.part.FieldWorksKind, Bound(item.part.Title ?? item.part.Text, MaxNameLength),
                item.part.Field))
            .GroupBy(item => item.MotifId, StringComparer.Ordinal).Select(group => group.First())
            .Take(MaxObjectsPerFinding).ToArray();

    // A finding that names hundreds of objects would swell the stored result; the first few identify it.
    private const int MaxObjectsPerFinding = 10;

    // Long text is cut so a stored result stays a few kilobytes whatever PanGloss reports.
    private const int MaxMessageLength = 400;
    private const int MaxNameLength = 120;

    private static string? Bound(string? text, int length) =>
        text is null || text.Length <= length ? text : text[..(length - 1)] + "…";

    /// <summary>Cuts an exception's text to the length a stored finding message may have.</summary>
    public static string BoundMessage(string text) => Bound(text, MaxMessageLength)!;
}

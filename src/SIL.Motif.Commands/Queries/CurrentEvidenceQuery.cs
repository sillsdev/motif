using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Store;
using SIL.Motif.Host.Texts;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands.Queries;

/// <summary>The saved project and its most recent stored Selection and matching Assessment.</summary>
public sealed record CurrentEvidenceSnapshot(
    string ProjectName,
    DateTimeOffset MotifStoreCreatedUtc,
    DateTimeOffset? LastFieldWorksSaveUtc,
    EvidenceFreshness Freshness,
    BaselineRecord? Baseline,
    ProjectSummarySnapshot? ProjectSummary,
    NamedSelectionRecord? DefaultSelection,
    ResolvedSelectionSnapshot? Selection,
    AssessmentRecord? MatchingAssessment)
{
    /// <summary>The later subset runs applied to words of the current default Selection.</summary>
    public IReadOnlyList<AssessmentRecord> RerunAssessments { get; init; } = [];

    /// <summary>Readable parser results for the stored Assessment's morph identifiers, keyed by word.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<ParserReading>> ResolvedReadingsByWord { get; init; } =
        new Dictionary<string, IReadOnlyList<ParserReading>>(StringComparer.Ordinal);

    /// <summary>The Selection's stored Baseline analyses, including added words, keyed by word form.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<ParserReading>> StoredAnalysesByWord { get; init; } =
        new Dictionary<string, IReadOnlyList<ParserReading>>(StringComparer.Ordinal);

    /// <summary>
    /// Each word form's <c>silfw:</c> link to its wordform in Word Analyses, from the captured Text projection,
    /// keyed by word form.
    /// </summary>
    public IReadOnlyDictionary<string, string> WordAnalysesLinksByWord { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>The correctness measurement recorded by the same invocation as the matching ParseTime run.</summary>
    public string? MatchingCorrectnessAssessmentId { get; init; }

    /// <summary>The per-rule timing measurement recorded by the same invocation as the matching ParseTime run.</summary>
    public string? MatchingObjectTimingAssessmentId { get; init; }

    /// <summary>The current word outcomes after later subset runs replace their earlier answers.</summary>
    public IReadOnlyList<AssessedWord> EffectiveWords => AssessmentWordOverlay.Apply(
        MatchingAssessment?.Words ?? [], RerunAssessments);

    /// <summary>The object times recorded for <see cref="EffectiveWords"/>, each from the run that timed its word.</summary>
    public IReadOnlyList<AssessmentObjectTiming> EffectiveObjectTimings => MatchingAssessment is { } assessment
        ? AssessmentWordOverlay.ApplyObjectTimings(assessment, RerunAssessments) : [];

    /// <summary>
    /// The matching Assessment as an <c>assess</c> run returns it: its words after later subset runs, worded by
    /// the same row builder a run uses, with its completion summary and its measurements by kind.
    /// <see langword="null"/> without a Baseline or a matching Assessment. It is built afresh on each read.
    /// </summary>
    public AssessCommandResponse? Assessment => AssessmentWordRows.FromStored(this);

    /// <summary>When the matching Assessment was recorded, or <see langword="null"/> without one.</summary>
    public DateTimeOffset? AssessedUtc => MatchingAssessment is { } assessment
        ? DateTimeOffset.Parse(assessment.SavedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
        : null;
}

/// <summary>Whether the current project file matches its recorded Baseline.</summary>
public enum EvidenceFreshness { NoBaseline, Current, Stale }

/// <summary>The exact Selection identities and Text occurrence counts resolved for stored evidence.</summary>
public sealed record ResolvedSelectionSnapshot(
    Selection Selection,
    IReadOnlyDictionary<string, int> OccurrencesByWord,
    int TextCount,
    int AddedWordCount)
{
    /// <summary>The total occurrences of selected words in the chosen Texts.</summary>
    public int TotalOccurrences => Selection.Words.Sum(word => OccurrencesByWord.GetValueOrDefault(word));
}

/// <summary>Reads current project evidence from the paired store and live file timestamp.</summary>
public static class CurrentEvidenceQuery
{
    /// <summary>Reads a project's saved summary, default Selection, matching Assessment, and freshness.</summary>
    public static CommandOutcome<CurrentEvidenceSnapshot> ReadCurrentEvidence(string projectPath) =>
        ProjectStoreCommand.Run(projectPath, MotifProductVersion.CurrentText,
            (database, project) => ReadCurrentEvidence(database, project));

    internal static CommandOutcome<CurrentEvidenceSnapshot> ReadCurrentEvidence(
        MotifDatabase database, ProjectLocator project, bool includeDefaultSelection = true,
        bool includeResolvedReadings = true)
    {
        var storeCreated = ReadStoreCreatedUtc(database);
        DateTimeOffset? lastSave = File.Exists(project.FullFwDataPath)
            ? new DateTimeOffset(File.GetLastWriteTimeUtc(project.FullFwDataPath), TimeSpan.Zero) : null;
        var current = new BaselineRepository(database)
            .GetCurrentEvidence(ProjectWorkspaceKey.Compute(project));
        var saved = includeDefaultSelection ? new NamedSelectionRepository(database).GetDefault() : null;
        // The matching Assessment measures the current Baseline, so its numbers were measured against its save.
        var baselineSave = current?.Baseline.SourceLastWriteUtc;
        var freshness = EvidenceFreshnessRule.Of(baselineSave, baselineSave,
            EvidenceFreshnessRule.LatestSave(lastSave, baselineSave));
        ResolvedSelectionSnapshot? selection = null;
        AssessmentRecord? assessment = null;
        IReadOnlyList<AssessmentRecord> reruns = [];
        string? correctnessAssessmentId = null;
        string? objectTimingAssessmentId = null;
        if (current is not null && saved is not null)
        {
            var resolved = ResolveSelection(current.Summary, saved);
            if (!resolved.Succeeded)
                return CommandOutcome<CurrentEvidenceSnapshot>.Refused(resolved.Refusal!);
            var resolvedSelection = resolved.Value!;
            var tokenJson = JsonSerializer.Serialize(current.Baseline.Token, MotifJson.CreateOptions());
            assessment = new AssessmentRepository(database).FindLatestBaselineAssessment(
                AssessmentKind.ParseTime.ToStoredKind(), tokenJson, resolvedSelection.Selection.Sha256,
                resolvedSelection.Selection.Words);
            if (assessment is not null)
            {
                if (assessment.Invocation?.InvocationId is { } invocationId)
                {
                    string? SameInvocation(AssessmentKind kind) => new AssessmentRepository(database)
                        .ListBaselineAssessments(kind.ToStoredKind())
                        .LastOrDefault(candidate => candidate.BaselineToken == tokenJson &&
                            candidate.Invocation?.InvocationId == invocationId)?.AssessmentId;
                    correctnessAssessmentId = SameInvocation(AssessmentKind.Correctness);
                    objectTimingAssessmentId = SameInvocation(AssessmentKind.ObjectTiming);
                }
                var original = assessment.Selection.Words.ToHashSet(StringComparer.Ordinal);
                reruns = new AssessmentRepository(database).ListBaselineAssessments(AssessmentKind.ParseTime.ToStoredKind())
                    .Where(candidate => candidate.BaselineToken == tokenJson &&
                        string.CompareOrdinal(candidate.SavedUtc, assessment.SavedUtc) > 0 &&
                        candidate.Selection.Words.Count < original.Count &&
                        candidate.Selection.Words.All(original.Contains))
                    .OrderBy(candidate => candidate.SavedUtc, StringComparer.Ordinal)
                    .ThenBy(candidate => candidate.AssessmentId, StringComparer.Ordinal).ToArray();
                resolvedSelection = resolvedSelection with
                {
                    Selection = resolvedSelection.Selection with { Provenance = assessment.Selection.Provenance },
                    OccurrencesByWord = MergeRecordedOccurrences(
                        resolvedSelection.OccurrencesByWord,
                        AssessmentWordOverlay.Apply(assessment.Words ?? [], reruns)),
                };
            }
            selection = resolvedSelection;
        }

        var effectiveWords = assessment is null ? [] : AssessmentWordOverlay.Apply(assessment.Words ?? [], reruns);
        var resolvedReadings = includeResolvedReadings && freshness == EvidenceFreshness.Current && current is not null
            ? ResolveReadings(current.Baseline.FwDataPath,
                Path.GetFileNameWithoutExtension(project.FullFwDataPath), effectiveWords)
            : new Dictionary<string, IReadOnlyList<ParserReading>>(StringComparer.Ordinal);
        var storedAnalyses = new Dictionary<string, IReadOnlyList<ParserReading>>(StringComparer.Ordinal);
        var wordLinks = new Dictionary<string, string>(StringComparer.Ordinal);
        if (assessment is not null && saved is { TextIds.Count: > 0 })
        {
            var projected = TextWordsQuery.Query(new TextWordsRequest(project.FullFwDataPath, saved.TextIds));
            if (!projected.Succeeded)
                return CommandOutcome<CurrentEvidenceSnapshot>.Refused(projected.Refusal!);
            if (!projected.Value!.HasBaseline)
                return CommandOutcome<CurrentEvidenceSnapshot>.Refused(new Refusal(
                    "current-evidence.text-words-unavailable", FailureReason.Refused,
                    "The stored Text analyses are unavailable for this Assessment."));
            foreach (var word in projected.Value.Words)
                storedAnalyses[word.Form] = word.Analyses.Select(analysis =>
                    new ParserReading(analysis.Morphs)
                        {
                        StoredAnalysisId = analysis.StoredAnalysisId,
                        StoredAnalysisOpinion = analysis.StoredAnalysisOpinion,
                        Identity = analysis.Identity,
                    }).ToArray();
            foreach (var token in projected.Value.Texts.SelectMany(text => text.Lines).SelectMany(line => line.Tokens))
                if (token.Form is { } form && token.WordLink is { } link) wordLinks.TryAdd(form, link);
        }
        var missingWords = effectiveWords.Select(word => word.Word).Where(word => !storedAnalyses.ContainsKey(word))
            .ToHashSet(StringComparer.Ordinal);
        if (current is not null && missingWords.Count > 0 && File.Exists(current.Baseline.FwDataPath))
        {
            using var cache = new FwDataProjectLoader().LoadScratchCache(current.Baseline.FwDataPath);
            var projectName = Path.GetFileNameWithoutExtension(project.FullFwDataPath);
            foreach (var wordform in cache.ServiceLocator.GetInstance<IWfiWordformRepository>().AllInstances())
            {
                var forms = wordform.Form.AvailableWritingSystemIds.Select(ws => wordform.Form.get_String(ws)?.Text)
                    .OfType<string>().Select(form => form.Trim().Normalize(System.Text.NormalizationForm.FormD))
                    .Where(missingWords.Contains).Distinct(StringComparer.Ordinal).ToArray();
                if (forms.Length == 0) continue;
                var analyses = TextWordsProjectionBuilder.ReadWordform(cache, wordform).Analyses
                    .Select(analysis => TextWordsQuery.ReadAnalysis(analysis, projectName))
                    .Select(analysis => new ParserReading(analysis.Morphs)
                    {
                        StoredAnalysisId = analysis.StoredAnalysisId,
                        StoredAnalysisOpinion = analysis.StoredAnalysisOpinion,
                        Identity = analysis.Identity,
                    }).ToArray();
                foreach (var form in forms)
                {
                    storedAnalyses[form] = analyses;
                    if (FieldWorksLinks.ForTarget(projectName, FieldWorksLinks.TargetFor(cache, wordform)) is { } link)
                        wordLinks.TryAdd(form, link);
                }
            }
        }
        return CommandOutcome<CurrentEvidenceSnapshot>.Success(new CurrentEvidenceSnapshot(
            Path.GetFileNameWithoutExtension(project.FullFwDataPath), storeCreated, lastSave, freshness,
            current?.Baseline, current?.Summary, saved, selection, assessment)
        {
            RerunAssessments = reruns,
            MatchingCorrectnessAssessmentId = correctnessAssessmentId,
            MatchingObjectTimingAssessmentId = objectTimingAssessmentId,
            ResolvedReadingsByWord = resolvedReadings,
            StoredAnalysesByWord = storedAnalyses,
            WordAnalysesLinksByWord = wordLinks,
        });
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<ParserReading>> ResolveReadings(
        string baselinePath, string projectName, IReadOnlyList<AssessedWord> words)
    {
        var resolvable = words.Where(word => word.Morphology is { Analyses.Count: > 0 }).ToArray();
        if (resolvable.Length == 0 || !File.Exists(baselinePath))
            return new Dictionary<string, IReadOnlyList<ParserReading>>(StringComparer.Ordinal);

        using var cache = new FwDataProjectLoader().LoadScratchCache(baselinePath);
        return resolvable.ToDictionary(word => word.Word,
            word => (IReadOnlyList<ParserReading>)ParserReadingReader.Read(cache, projectName, word.Morphology!),
            StringComparer.Ordinal);
    }

    /// <summary>Resolves one saved Selection from the project inventory captured with its Baseline.</summary>
    public static CommandOutcome<ResolvedSelectionSnapshot> ResolveSelection(
        ProjectSummarySnapshot summary, NamedSelectionRecord saved)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(saved);
        var texts = summary.Texts.ToDictionary(text => text.TextId);
        var forms = new List<string>();
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var textId in saved.TextIds)
        {
            if (!texts.TryGetValue(textId, out var text))
                return CommandOutcome<ResolvedSelectionSnapshot>.Refused(new Refusal(
                    "selection.text-not-found", FailureReason.InvalidArgument,
                    $"No Text with GUID '{textId:D}' exists in this Baseline.",
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["textId"] = textId.ToString("D") }));
            foreach (var (word, count) in text.OccurrencesByWord)
            {
                forms.Add(word);
                occurrences[word] = occurrences.GetValueOrDefault(word) + count;
            }
        }
        forms.AddRange(saved.AddedWords.Select(word => word.Trim())
            .Where(word => word.Length > 0).Select(word => word.Normalize(System.Text.NormalizationForm.FormD)));
        var selection = Selection.Create(saved.Name, forms);
        if (selection.Words.Count == 0)
            return CommandOutcome<ResolvedSelectionSnapshot>.Refused(new Refusal(
                "selection.empty", FailureReason.Refused,
                "No requested source contributed any words, so there is nothing to compose a Selection from."));
        var resolvedOccurrences = selection.Words.ToDictionary(word => word,
            word => occurrences.GetValueOrDefault(word), StringComparer.Ordinal);
        return CommandOutcome<ResolvedSelectionSnapshot>.Success(new ResolvedSelectionSnapshot(
            selection, resolvedOccurrences, saved.TextIds.Count, saved.AddedWords.Count));
    }

    private static IReadOnlyDictionary<string, int> MergeRecordedOccurrences(
        IReadOnlyDictionary<string, int> resolved, IReadOnlyList<AssessedWord>? words)
    {
        if (words is null) return resolved;
        var merged = resolved.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
        foreach (var word in words)
            if (word.OccurrenceCount is { } count) merged[word.Word] = count;
        return merged;
    }

    private static DateTimeOffset ReadStoreCreatedUtc(MotifDatabase database)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT CreatedUtc FROM MotifMetadata WHERE Id = 1;";
        return DateTimeOffset.Parse((string)command.ExecuteScalar()!, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
    }
}

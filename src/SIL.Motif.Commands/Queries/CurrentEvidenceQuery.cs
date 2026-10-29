using SIL.LCModel;
using SIL.Motif.Host.Baselines;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Commands.Store;
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
    /// <summary>The latest parser refusal for this exact Baseline, retained until a run succeeds.</summary>
    public Refusal? LastParserRefusal { get; init; }

    /// <summary>The query's saved-file navigation check; absent when word context was not requested.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public SavedProjectNavigation? Navigation { get; init; }

    /// <summary>The later subset runs applied to words of the current default Selection.</summary>
    public IReadOnlyList<AssessmentRecord> RerunAssessments { get; init; } = [];

    /// <summary>Readable parser results for the stored Assessment's morph identifiers, keyed by word.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<ParserReading>> ResolvedReadingsByWord { get; init; } =
        new Dictionary<string, IReadOnlyList<ParserReading>>(StringComparer.Ordinal);

    /// <summary>Whether the selected Baseline word context was successfully read, including an empty result.</summary>
    public bool WordContextAvailable { get; init; }

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

    /// <summary>The selected component runs and their effective measurements, projected once.</summary>
    public AssessmentEvidenceSet? EvidenceSet { get; init; }

    /// <summary>Effective word results when one view overlays a measured subset on today's values.</summary>
    public IReadOnlyList<AssessedWord>? EffectiveWordsOverride { get; init; }

    /// <summary>The named set from which this snapshot's stored measurements were selected.</summary>
    public EvidenceSet? Set { get; init; }

    /// <summary>The current word outcomes after explicit reparses replace their earlier answers.</summary>
    public IReadOnlyList<AssessedWord> EffectiveWords => EffectiveWordsOverride ?? EvidenceSet?.Words ??
        (MatchingAssessment is { } assessment ? AssessmentEvidenceSet.Create(assessment, RerunAssessments).Words : []);

    /// <summary>The object times recorded for <see cref="EffectiveWords"/>, each from the run that timed its word.</summary>
    public IReadOnlyList<AssessmentObjectTiming> EffectiveObjectTimings => EvidenceSet?.ObjectTimings ??
        (MatchingAssessment is { } assessment ? AssessmentWordOverlay.ApplyObjectTimings(assessment, RerunAssessments) : []);

    /// <summary>Whether the effective object timing detail covers its contributing words.</summary>
    public ObjectTimingDetail TimingDetail => EvidenceSet?.TimingDetail ??
        (MatchingAssessment is { } assessment ? AssessmentEvidenceSet.Create(assessment, RerunAssessments).TimingDetail
            : new ObjectTimingDetail(false, "Object timing is unavailable."));

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
        bool includeResolvedReadings = true, bool includeWordContext = true)
    {
        var storeCreated = ReadStoreCreatedUtc(database);
        DateTimeOffset? lastSave = File.Exists(project.FullFwDataPath)
            ? new DateTimeOffset(File.GetLastWriteTimeUtc(project.FullFwDataPath), TimeSpan.Zero) : null;
        var resolved = EvidenceSets.ResolveCurrent(database, project, includeDefaultSelection);
        if (!resolved.Succeeded)
            return CommandOutcome<CurrentEvidenceSnapshot>.Refused(resolved.Refusal!);
        var current = resolved.Value!.Current;
        var saved = resolved.Value.DefaultSelection;
        // The matching Assessment measures the current Baseline, so its numbers were measured against its save.
        var baselineSave = current?.Baseline.SourceLastWriteUtc;
        var freshness = EvidenceFreshnessRule.Of(baselineSave, baselineSave,
            EvidenceFreshnessRule.LatestSave(lastSave, baselineSave));
        var selection = resolved.Value.Selection;
        var assessment = resolved.Value.MatchingAssessment;
        var reruns = resolved.Value.Reruns;
        var navigation = includeWordContext && current is not null
            ? SavedProjectNavigation.Read(project.FullFwDataPath, current.Baseline.Token.ProjectIdentity) : null;
        var snapshot = new CurrentEvidenceSnapshot(
            Path.GetFileNameWithoutExtension(project.FullFwDataPath), storeCreated, lastSave, freshness,
            current?.Baseline, current?.Summary, saved, selection, assessment)
        {
            LastParserRefusal = current is null ? null : new ParserRefusalRepository(database).Get(current.Baseline.Token),
            Navigation = navigation,
            RerunAssessments = reruns,
            EvidenceSet = assessment is null ? null : AssessmentEvidenceSet.Create(assessment, reruns),
            Set = resolved.Value.Set,
            MatchingCorrectnessAssessmentId = resolved.Value.Set?.MeasurementIds
                .GetValueOrDefault(AssessmentKinds.Correctness),
            MatchingObjectTimingAssessmentId = resolved.Value.Set?.MeasurementIds
                .GetValueOrDefault(AssessmentKinds.ObjectTiming),
        };
        return includeWordContext ? ReadWordContext(snapshot, project.FullFwDataPath, includeResolvedReadings) :
            CommandOutcome<CurrentEvidenceSnapshot>.Success(snapshot);
    }

    /// <summary>
    /// Hydrates the exact evidence already selected, without consulting current run pointers again.
    /// A missing Baseline is a refusal, allowing numeric-only callers to retain the original snapshot.
    /// </summary>
    internal static CommandOutcome<CurrentEvidenceSnapshot> ReadWordContext(CurrentEvidenceSnapshot snapshot,
        string projectPath, bool includeResolvedReadings = true)
    {
        if (snapshot.MatchingAssessment is null || snapshot.Baseline is not { } baseline)
            return CommandOutcome<CurrentEvidenceSnapshot>.Success(snapshot);
        if (!File.Exists(baseline.FwDataPath))
            return CommandOutcome<CurrentEvidenceSnapshot>.Refused(new Refusal(
                "current-evidence.baseline-unavailable", FailureReason.StoreInconsistent,
                "The exact Baseline file for this Assessment is unavailable. Capture a new Baseline and assess it."));
        try
        {
            using var reader = BaselineReadCache.Open(baseline.FwDataPath);
            var navigation = snapshot.Navigation ?? SavedProjectNavigation.Read(projectPath, baseline.Token.ProjectIdentity);
            var context = BaselineWordContext.Read(reader.Cache, navigation,
                snapshot.EffectiveWords.Select(word => word.Word).ToArray());
            var resolvedReadings = includeResolvedReadings
                ? snapshot.EffectiveWords.Where(word => word.Morphology is not null).ToDictionary(
                    word => word.Word,
                    word => (IReadOnlyList<ParserReading>)ParserReadingReader.Read(reader.Cache, string.Empty, word.Morphology!, navigation.LinkFor),
                    StringComparer.Ordinal)
                : new Dictionary<string, IReadOnlyList<ParserReading>>(StringComparer.Ordinal);
            return CommandOutcome<CurrentEvidenceSnapshot>.Success(snapshot with
            {
                Navigation = navigation,
                WordContextAvailable = true,
                StoredAnalysesByWord = context.Analyses,
                WordAnalysesLinksByWord = context.WordLinks,
                ResolvedReadingsByWord = resolvedReadings,
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or LcmInitializationException)
        {
            return CommandOutcome<CurrentEvidenceSnapshot>.Refused(new Refusal(
                "current-evidence.baseline-unavailable", FailureReason.StoreInconsistent,
                "The exact Baseline word context is unavailable: " + exception.Message));
        }
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

    private static DateTimeOffset ReadStoreCreatedUtc(MotifDatabase database)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT CreatedUtc FROM MotifMetadata WHERE Id = 1;";
        return DateTimeOffset.Parse((string)command.ExecuteScalar()!, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
    }
}

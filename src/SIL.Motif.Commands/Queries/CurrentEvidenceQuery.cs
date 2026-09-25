using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
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

    /// <summary>The current word outcomes after later subset runs replace their earlier answers.</summary>
    public IReadOnlyList<AssessedWord> EffectiveWords => AssessmentWordOverlay.Apply(
        MatchingAssessment?.Words ?? [], RerunAssessments);
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
        MotifDatabase database, ProjectLocator project, bool includeDefaultSelection = true)
    {
        var storeCreated = ReadStoreCreatedUtc(database);
        DateTimeOffset? lastSave = File.Exists(project.FullFwDataPath)
            ? new DateTimeOffset(File.GetLastWriteTimeUtc(project.FullFwDataPath), TimeSpan.Zero) : null;
        var current = new BaselineRepository(database)
            .GetCurrentEvidence(ProjectWorkspaceKey.Compute(project));
        var saved = includeDefaultSelection ? new NamedSelectionRepository(database).GetDefault() : null;
        var freshness = current is null
            ? EvidenceFreshness.NoBaseline
            : lastSave is { } savedUtc && savedUtc > current.Baseline.SourceLastWriteUtc
                ? EvidenceFreshness.Stale
                : EvidenceFreshness.Current;
        ResolvedSelectionSnapshot? selection = null;
        AssessmentRecord? assessment = null;
        IReadOnlyList<AssessmentRecord> reruns = [];
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

        return CommandOutcome<CurrentEvidenceSnapshot>.Success(new CurrentEvidenceSnapshot(
            Path.GetFileNameWithoutExtension(project.FullFwDataPath), storeCreated, lastSave, freshness,
            current?.Baseline, current?.Summary, saved, selection, assessment)
        {
            RerunAssessments = reruns,
        });
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

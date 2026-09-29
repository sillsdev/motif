using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Store;
using SIL.Motif.Host.Texts;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands;

/// <summary>Reads and changes per-occurrence Read state in the project's Motif store.</summary>
public static class ReadStateCommands
{
    private static readonly JsonSerializerOptions JsonOptions = MotifJson.CreateOptions();

    /// <summary>Returns Read occurrences whose sentence, FieldWorks, and PanGloss evidence still matches.</summary>
    public static CommandOutcome<WordReadStateResponse> Execute(WordReadStateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.TextId == Guid.Empty)
            return Invalid("A Text identity is required.");
        if (request.Occurrences is { } occurrences &&
            (occurrences.Count == 0 || occurrences.Any(occurrence => occurrence.TextId != request.TextId) ||
             occurrences.Distinct().Count() != occurrences.Count))
            return Invalid("Read-state occurrences must be unique and belong to the requested Text.");

        return ProjectStoreCommand.Run(request.ProjectPath, MotifProductVersion.CurrentText,
            (database, project) => ReadOrChange(database, project, request));
    }

    private static CommandOutcome<WordReadStateResponse> ReadOrChange(
        MotifDatabase database, ProjectLocator project, WordReadStateRequest request)
    {
        var repository = new ReadStateRepository(database);
        var workspaceKey = ProjectWorkspaceKey.Compute(project);
        var current = new BaselineRepository(database).GetCurrentTextWords(workspaceKey, [request.TextId]);
        if (current is null)
        {
            repository.DeleteForText(request.TextId);
            return Success([], hasBaseline: false);
        }

        var text = current.Projection.Texts.SingleOrDefault(candidate => candidate.TextId == request.TextId);
        if (text is null)
        {
            repository.DeleteForText(request.TextId);
            return request.IsRead == true
                ? Invalid("The requested Text is not present in the current Baseline.")
                : Success([], hasBaseline: true);
        }

        var assessmentDigests = CurrentAssessmentDigests(database);
        Revalidate(repository, current.Projection, text, assessmentDigests);

        if (request.IsRead is null)
            return Success(repository.GetForText(request.TextId).Select(record => record.Occurrence).ToArray(),
                hasBaseline: true);

        var targets = request.Occurrences ?? AllOccurrences(text);
        if (request.IsRead == false)
        {
            foreach (var target in targets) repository.Delete(target);
            return Success(repository.GetForText(request.TextId).Select(record => record.Occurrence).ToArray(),
                hasBaseline: true);
        }

        var fingerprints = new List<(OccurrenceAnchor Occurrence, ReadOccurrenceFingerprint Fingerprint)>();
        foreach (var target in targets)
        {
            if (!TryFingerprint(current.Projection, text, target, assessmentDigests, out var fingerprint))
                return Invalid("The requested occurrence does not have current, unambiguous word evidence.");
            fingerprints.Add((target, fingerprint));
        }
        foreach (var (occurrence, fingerprint) in fingerprints)
            repository.Upsert(new ReadOccurrenceRecord(occurrence, JsonSerializer.Serialize(fingerprint, JsonOptions)));

        return Success(repository.GetForText(request.TextId).Select(record => record.Occurrence).ToArray(),
            hasBaseline: true);
    }

    private static void Revalidate(ReadStateRepository repository, TextWordsProjection projection,
        TextWordsProjectedText text, IReadOnlyDictionary<string, string> assessmentDigests)
    {
        foreach (var record in repository.GetForText(text.TextId))
        {
            try
            {
                var expected = JsonSerializer.Deserialize<ReadOccurrenceFingerprint>(record.FingerprintJson,
                    JsonOptions);
                if (expected is null || expected.Occurrence is null || expected.Assessments is null ||
                    !TryFingerprint(projection, text, record.Occurrence, assessmentDigests,
                        out var current) ||
                    OccurrenceFitEvidenceResolver.Compare(expected.Occurrence, projection) is not null ||
                    expected.WordformDigest != current.WordformDigest ||
                    !expected.Assessments.SequenceEqual(current.Assessments))
                    repository.Delete(record.Occurrence);
            }
            catch (JsonException)
            {
                repository.Delete(record.Occurrence);
            }
        }
    }

    private static bool TryFingerprint(TextWordsProjection projection, TextWordsProjectedText text,
        OccurrenceAnchor occurrence, IReadOnlyDictionary<string, string> assessmentDigests,
        out ReadOccurrenceFingerprint fingerprint)
    {
        fingerprint = null!;
        if (occurrence.TextId != text.TextId ||
            !OccurrenceFitEvidenceResolver.TryCapture(projection, occurrence, out var evidence, out _) ||
            evidence is null || !evidence.ParseIsCurrent)
            return false;

        var line = text.Lines.SingleOrDefault(candidate => candidate.ParagraphId == occurrence.ParagraphId &&
            candidate.SegmentId == occurrence.SegmentId);
        var token = line?.Tokens.SingleOrDefault(candidate => candidate.OccurrenceIndex == occurrence.Index);
        if (token?.WordformId is not { } wordformId ||
            projection.Wordforms.SingleOrDefault(wordform => wordform.WordformId == wordformId) is not { } wordform)
            return false;

        var wordformDigest = Digest(new
        {
            wordform = CanonicalId.FromGuid(wordform.WordformId).Value,
            wordform.IncorrectSpelling,
            wordform.CandidateCount,
            analyses = wordform.Analyses.OrderBy(analysis => analysis.AnalysisId)
                .Select(analysis => new
                {
                    id = CanonicalId.FromGuid(analysis.AnalysisId).Value,
                    analysis.Key,
                    analysis.Opinion,
                    analysis.Identity,
                    analysis.Morphs,
                }).ToArray(),
        });
        var assessments = token.Forms.Select(Canonicalize)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(form => form, StringComparer.Ordinal)
            .Select(form => new ReadAssessmentEvidence(form,
                assessmentDigests.TryGetValue(form, out var digest) ? digest : null)).ToArray();
        fingerprint = new ReadOccurrenceFingerprint(evidence, wordformDigest, assessments);
        return true;
    }

    private static Dictionary<string, string> CurrentAssessmentDigests(MotifDatabase database)
    {
        var latest = new Dictionary<string, string>(StringComparer.Ordinal);
        var assessments = new AssessmentRepository(database)
            .ListBaselineAssessments(AssessmentKind.ParseTime.ToStoredKind())
            .OrderBy(assessment => assessment.SavedUtc, StringComparer.Ordinal)
            .ThenBy(assessment => assessment.AssessmentId, StringComparer.Ordinal);
        foreach (var assessment in assessments)
        foreach (var word in assessment.Words ?? [])
        {
            var form = Canonicalize(word.Word);
            if (form.Length > 0) latest[form] = Digest(SemanticResult(word));
        }
        return latest;
    }

    private static object SemanticResult(AssessedWord word) => new
    {
        word.Outcome,
        analyses = word.Analyses.Select(analysis => new
        {
            analysis.CategoryGuid,
            analysis.MorphemeGuids,
            analysis.RootIndex,
            analysis.IdentityDigest,
        }).ToArray(),
        morphology = word.Morphology is { } morphology ? new
        {
            morphology.Schema,
            morphology.Word,
            morphology.Capped,
            morphology.TimedOut,
            morphology.InvalidShape,
            morphology.Analyses,
            morphology.Unavailable,
        } : null,
        correctness = word.Correctness is { } correctness ? new
        {
            correctness.Expected,
            correctness.Matched,
            correctness.Status,
            correctness.Expectations,
            correctness.Unmatched,
            correctness.Unavailable,
        } : null,
        word.ProjectStanding,
        word.OccurrenceCount,
        word.ReadingGrades,
        word.MissedApprovedCount,
        word.MissedApproved,
        word.IsIncomplete,
    };

    private static OccurrenceAnchor[] AllOccurrences(TextWordsProjectedText text) => text.Lines
        .SelectMany(line => line.Tokens.Where(token => token.WordformId is not null)
            .Select(token => new OccurrenceAnchor(text.TextId, line.ParagraphId, line.SegmentId,
                token.OccurrenceIndex)))
        .Distinct()
        .ToArray();

    private static string Canonicalize(string form) => form.Trim().Normalize(NormalizationForm.FormD);

    private static string Digest(object value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        var canonical = CanonicalJson.CanonicalizeToUtf8(json);
        return "sha256:" + Convert.ToHexString(SHA256.HashData(canonical)).ToLowerInvariant();
    }

    private static CommandOutcome<WordReadStateResponse> Success(
        IReadOnlyList<OccurrenceAnchor> occurrences, bool hasBaseline) =>
        CommandOutcome<WordReadStateResponse>.Success(new WordReadStateResponse(occurrences, hasBaseline));

    private static CommandOutcome<WordReadStateResponse> Invalid(string message) =>
        CommandOutcome<WordReadStateResponse>.Refused(new Refusal(
            "word.read-state-invalid", FailureReason.InvalidArgument, message));

    private sealed record ReadOccurrenceFingerprint(
        OccurrenceFitEvidence Occurrence,
        string WordformDigest,
        IReadOnlyList<ReadAssessmentEvidence> Assessments);

    private sealed record ReadAssessmentEvidence(string Form, string? Digest);
}

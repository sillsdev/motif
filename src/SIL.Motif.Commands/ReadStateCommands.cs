using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
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
    public static CommandOutcome<WordReadStateResponse> Execute(
        WordReadStateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (cancellationToken.IsCancellationRequested)
            return CommandOutcome<WordReadStateResponse>.Refused(new Refusal(
                "word.read-state-cancelled", FailureReason.Cancelled, "Reading word state was cancelled."));
        if (request.TextId == Guid.Empty)
            return Invalid("A Text identity is required.");
        if (request.Occurrences is { } occurrences &&
            (occurrences.Count == 0 || occurrences.Any(occurrence => occurrence.TextId != request.TextId) ||
             occurrences.Distinct().Count() != occurrences.Count))
            return Invalid("Read-state occurrences must be unique and belong to the requested Text.");

        return ProjectStoreCommand.Run(request.ProjectPath, MotifProductVersion.CurrentText,
            (database, project) => ReadOrChange(database, project, request));
    }

    internal static CommandOutcome<WordReadStateResponse> ReadValid(
        WordReadStateRequest request, CancellationToken cancellationToken = default,
        Action<TextWordsProjection>? onProjectionRead = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (cancellationToken.IsCancellationRequested)
            return CommandOutcome<WordReadStateResponse>.Refused(new Refusal(
                "word.read-state-cancelled", FailureReason.Cancelled, "Reading word state was cancelled."));
        if (request.TextId == Guid.Empty || request.IsRead is not null || request.Occurrences is not null)
            return Invalid("A Text identity and read-only request are required.");
        return ProjectStoreCommand.Run(request.ProjectPath, MotifProductVersion.CurrentText,
            (database, project) => ReadOrChange(database, project, request, pruneInvalidReads: false,
                onProjectionRead));
    }

    private static CommandOutcome<WordReadStateResponse> ReadOrChange(
        MotifDatabase database, ProjectLocator project, WordReadStateRequest request,
        bool pruneInvalidReads = true, Action<TextWordsProjection>? onProjectionRead = null)
    {
        var repository = new ReadStateRepository(database);
        using var write = request.ExpectedContext is null ? null : repository.BeginTransaction();
        CommandOutcome<WordReadStateResponse> Complete(CommandOutcome<WordReadStateResponse> outcome)
        {
            if (outcome.Succeeded) write?.Commit();
            return outcome;
        }

        var workspaceKey = ProjectWorkspaceKey.Compute(project);
        if (request.ExpectedContext is { } expectedContext &&
            !repository.IsCurrentContext(workspaceKey, expectedContext))
            return ContextChanged();
        var current = new BaselineRepository(database).GetCurrentTextWords(workspaceKey, [request.TextId]);
        if (current is not null) onProjectionRead?.Invoke(current.Projection);
        if (request.ExpectedContext is { } expectedCurrent &&
            (current is null || current.Baseline.Token != expectedCurrent.Baseline))
            return ContextChanged();
        if (current is null)
        {
            if (pruneInvalidReads) repository.DeleteForText(request.TextId);
            if (request.IsRead == true)
                return Invalid("A current Baseline is required before marking a word Read.");
            return Complete(Success([], hasBaseline: false));
        }

        var text = current.Projection.Texts.SingleOrDefault(candidate => candidate.TextId == request.TextId);
        if (text is null)
        {
            if (pruneInvalidReads) repository.DeleteForText(request.TextId);
            return request.IsRead == true
                ? Invalid("The requested Text is not present in the current Baseline.")
                : Complete(Success([], hasBaseline: true));
        }

        var saved = repository.GetForText(request.TextId);
        // No saved Read marks means there are no fingerprints to validate against parser evidence.
        if (request.IsRead is null && saved.Count == 0) return Complete(Success([], hasBaseline: true));

        var assessmentDigests = CurrentAssessmentDigests(database, current.Baseline.Token, text,
            request.AssessmentIds);
        if (request.IsRead is null)
        {
            var valid = ValidateReadRecords(current.Projection, text, assessmentDigests, saved);
            if (pruneInvalidReads)
            {
                Revalidate(repository, saved, valid);
                return Complete(Success(repository.GetForText(request.TextId)
                    .Select(record => record.Occurrence).ToArray(), hasBaseline: true));
            }
            return Complete(Success(valid.Select(item => item.Occurrence).Distinct().ToArray(),
                hasBaseline: true));
        }

        Revalidate(repository, saved,
            ValidateReadRecords(current.Projection, text, assessmentDigests, saved));

        var targets = request.Occurrences ?? AllOccurrences(text);
        if (request.IsRead == false)
        {
            foreach (var target in targets) repository.Delete(target);
            return Complete(Success(repository.GetForText(request.TextId)
                .Select(record => record.Occurrence).ToArray(), hasBaseline: true));
        }

        var fingerprints = new List<(OccurrenceAnchor Occurrence, ReadOccurrenceFingerprint Fingerprint)>();
        var skipped = new List<OccurrenceAnchor>();
        foreach (var target in targets)
        {
            var status = CaptureFingerprint(current.Projection, text, target, assessmentDigests, out var fingerprint);
            if (status == FingerprintStatus.ParagraphNotParsed && request.Occurrences is null)
            {
                skipped.Add(target);
                continue;
            }
            if (status != FingerprintStatus.Current)
                return Invalid("The requested occurrence does not have current, unambiguous word evidence.");
            fingerprints.Add((target, fingerprint));
        }
        foreach (var (occurrence, fingerprint) in fingerprints)
            repository.Upsert(new ReadOccurrenceRecord(occurrence, JsonSerializer.Serialize(fingerprint, JsonOptions)));

        return Complete(Success(repository.GetForText(request.TextId)
            .Select(record => record.Occurrence).ToArray(), hasBaseline: true, skipped));
    }

    private static void Revalidate(ReadStateRepository repository, IReadOnlyList<ReadOccurrenceRecord> saved,
        IReadOnlyList<ReadOccurrenceValidation> valid)
    {
        var byOriginal = valid.ToDictionary(item => item.Original);
        foreach (var record in saved)
        {
            if (!byOriginal.TryGetValue(record.Occurrence, out var current))
            {
                repository.Delete(record.Occurrence);
                continue;
            }
            if (current.Occurrence != record.Occurrence)
            {
                repository.Delete(record.Occurrence);
                repository.Upsert(new ReadOccurrenceRecord(current.Occurrence,
                    JsonSerializer.Serialize(current.Fingerprint, JsonOptions)));
            }
        }
    }

    private static IReadOnlyList<ReadOccurrenceValidation> ValidateReadRecords(TextWordsProjection projection,
        TextWordsProjectedText text, IReadOnlyDictionary<string, string> assessmentDigests,
        IReadOnlyList<ReadOccurrenceRecord> saved)
    {
        var valid = new List<ReadOccurrenceValidation>(saved.Count);
        foreach (var record in saved)
        {
            try
            {
                var expected = JsonSerializer.Deserialize<ReadOccurrenceFingerprint>(record.FingerprintJson,
                    JsonOptions);
                if (expected is null || expected.Occurrence is null || expected.Assessments is null ||
                    !OccurrenceFitEvidenceResolver.TryReanchor(expected.Occurrence, projection, out var anchor) ||
                    CaptureFingerprint(projection, text, anchor, assessmentDigests,
                        out var current) != FingerprintStatus.Current ||
                    expected.WordformDigest != current.WordformDigest ||
                    !expected.Assessments.SequenceEqual(current.Assessments))
                {
                    continue;
                }
                valid.Add(new ReadOccurrenceValidation(record.Occurrence, anchor, current));
            }
            catch (JsonException)
            {
            }
        }
        return valid;
    }

    private static FingerprintStatus CaptureFingerprint(TextWordsProjection projection, TextWordsProjectedText text,
        OccurrenceAnchor occurrence, IReadOnlyDictionary<string, string> assessmentDigests,
        out ReadOccurrenceFingerprint fingerprint)
    {
        fingerprint = null!;
        if (occurrence.TextId != text.TextId ||
            !OccurrenceFitEvidenceResolver.TryCapture(projection, occurrence, out var evidence, out _) ||
            evidence is null)
            return FingerprintStatus.Invalid;
        if (!evidence.ParseIsCurrent) return FingerprintStatus.ParagraphNotParsed;

        var line = text.Lines.SingleOrDefault(candidate => candidate.ParagraphId == occurrence.ParagraphId &&
            candidate.SegmentId == occurrence.SegmentId);
        var token = line?.Tokens.SingleOrDefault(candidate => candidate.OccurrenceIndex == occurrence.Index);
        if (token?.WordformId is not { } wordformId ||
            projection.Wordforms.SingleOrDefault(wordform => wordform.WordformId == wordformId) is not { } wordform)
            return FingerprintStatus.Invalid;

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
        var assessments = token.Forms.Select(form => Canonicalize(form.Text))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(form => form, StringComparer.Ordinal)
            .Select(form => new ReadAssessmentEvidence(form,
                assessmentDigests.TryGetValue(form, out var digest) ? digest : null)).ToArray();
        fingerprint = new ReadOccurrenceFingerprint(evidence, wordformDigest, assessments);
        return FingerprintStatus.Current;
    }

    private static Dictionary<string, string> CurrentAssessmentDigests(MotifDatabase database,
        BaselineToken currentToken, TextWordsProjectedText text, IReadOnlyList<string>? assessmentIds)
    {
        var forms = text.Lines.SelectMany(line => line.Tokens).Where(token => token.WordformId is not null)
            .SelectMany(token => token.Forms).Select(form => Canonicalize(form.Text)).Where(form => form.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        var tokenJson = JsonSerializer.Serialize(currentToken, JsonOptions);
        var repository = new AssessmentRepository(database);
        var assessments = repository.ReadBaselineAssessmentWords(AssessmentKind.ParseTime.ToStoredKind(),
            tokenJson, assessmentIds, forms);
        var current = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var word in assessments.SelectMany(assessment => assessment.Words ?? []))
        {
            var form = Canonicalize(word.Word);
            current[form] = Digest(SemanticResult(word));
        }
        return current;
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
        IReadOnlyList<OccurrenceAnchor> occurrences, bool hasBaseline,
        IReadOnlyList<OccurrenceAnchor>? skipped = null) =>
        CommandOutcome<WordReadStateResponse>.Success(new WordReadStateResponse(occurrences, hasBaseline)
        {
            SkippedOccurrences = skipped ?? [],
        });

    private static CommandOutcome<WordReadStateResponse> Invalid(string message) =>
        CommandOutcome<WordReadStateResponse>.Refused(new Refusal(
            "word.read-state-invalid", FailureReason.InvalidArgument, message));

    private static CommandOutcome<WordReadStateResponse> ContextChanged() =>
        CommandOutcome<WordReadStateResponse>.Refused(new Refusal(
            "texts.evidence-changed", FailureReason.Refused,
            "The saved Selection evidence changed. Reload it and try again."));

    private sealed record ReadOccurrenceFingerprint(
        OccurrenceFitEvidence Occurrence,
        string WordformDigest,
        IReadOnlyList<ReadAssessmentEvidence> Assessments);

    private sealed record ReadAssessmentEvidence(string Form, string? Digest);

    private sealed record ReadOccurrenceValidation(
        OccurrenceAnchor Original, OccurrenceAnchor Occurrence, ReadOccurrenceFingerprint Fingerprint);

    private enum FingerprintStatus
    {
        Current,
        ParagraphNotParsed,
        Invalid,
    }
}

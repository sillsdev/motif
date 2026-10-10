using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Analysis;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.PanGloss;

namespace SIL.Motif.Host.Parsimony;

/// <summary>Projects explicitly named ParseTime Assessments into immutable parser evidence.</summary>
public static class AssessmentEvidenceProjector
{
    /// <summary>Builds case and reading evidence bound to one exact source and parser executable.</summary>
    public static ParserOverlayProjection Build(IReadOnlyList<ParserAssessmentSource> assessments,
        IReadOnlyList<EvidenceWordform> wordforms, string sourceSha256, string parserSha256,
        string? proposalId = null, string? proposalIntentDigest = null,
        IReadOnlyList<ReviewedNegativeExpectation>? reviewedNegatives = null)
    {
        ArgumentNullException.ThrowIfNull(assessments);
        ArgumentNullException.ThrowIfNull(wordforms);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceSha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(parserSha256);
        if (assessments.Count == 0) return ParserOverlayProjection.NotRequested;
        if (assessments.Select(item => item.AssessmentId).Distinct(StringComparer.Ordinal).Count() != assessments.Count)
            throw new InvalidDataException("Parser Assessment references must be unique.");

        var runs = new List<ParserRunProjection>();
        var cases = new List<ParserCaseProjection>();
        var analyses = new List<ParserAnalysisProjection>();
        var morphs = new List<ParserAnalysisMorphProjection>();
        var rejected = new List<ParserDisapprovedMorphologyProjection>();
        var matches = new List<ParserDisapprovedMatchProjection>();
        var reviewedNegativeCases = new List<ParserReviewedNegativeCaseProjection>();
        reviewedNegatives ??= [];
        var forms = wordforms.SelectMany(wordform => wordform.Forms
                .Select(form => new FormIdentity(form.TextNfd, form.WritingSystem, wordform.Guid)))
            .GroupBy(item => item.Nfd, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Distinct().ToArray(), StringComparer.Ordinal);

        foreach (var assessment in assessments.OrderBy(item => item.AssessmentId, StringComparer.Ordinal))
        {
            ValidateBinding(assessment, sourceSha256, parserSha256, proposalId, proposalIntentDigest);
            var runStart = cases.Count;
            if (!TryReadCases(assessment, out var parserCases, out var unavailableReason))
            {
                reviewedNegativeCases.AddRange(reviewedNegatives.Select(item =>
                    new ParserReviewedNegativeCaseProjection(assessment.AssessmentId, item.CaseId,
                        item.RevisionId, item.ContentDigest, item.WritingSystem,
                        item.Form.Normalize(NormalizationForm.FormD), item.Status, null, "unavailable",
                        "unavailable", false, "[]", unavailableReason)));
                runs.Add(new ParserRunProjection(assessment.AssessmentId, assessment.InvocationId,
            assessment.SourceSha256, assessment.ParserSha256, assessment.ModelFingerprint,
                    "unavailable", assessment.TimeoutMs, assessment.StepLimit, assessment.Threads,
                    assessment.StoredCases.Count, 0, unavailableReason));
                continue;
            }

            for (var caseIndex = 0; caseIndex < parserCases.Count; caseIndex++)
            {
                var parserCase = parserCases[caseIndex];
                var surfaceNfd = parserCase.Word.Normalize(NormalizationForm.FormD);
                var caseKey = CaseKey(assessment.AssessmentId, caseIndex, surfaceNfd);
                var identities = forms.GetValueOrDefault(surfaceNfd, []);
                var wordformIdentity = identities.Length == 1 ? identities[0] : null;
                var wordform = wordformIdentity is null ? null : wordforms.Single(item => item.Guid == wordformIdentity.Guid);
                var complete = !parserCase.Capped && !parserCase.TimedOut && !parserCase.InvalidShape;
                var identityStatus = wordformIdentity is not null ? "complete" : "unavailable";
                var reason = identityStatus == "complete" ? null : identities.Length == 0
                    ? "No exact Baseline wordform matches this parser surface."
                    : "The parser surface maps to more than one writing-system wordform.";
                cases.Add(new ParserCaseProjection(assessment.AssessmentId, caseKey, parserCase.Word,
                    surfaceNfd, wordformIdentity?.WritingSystem, wordformIdentity?.Guid.ToString("D").ToLowerInvariant(),
                    complete ? "complete" : "incomplete", identityStatus, reason,
                    parserCase.Capped, parserCase.TimedOut, parserCase.InvalidShape, parserCase.Attempts, null,
                    parserCase.Analyses.Count));

                var rejectedSignatures = wordform?.Analyses.Where(item => item.Opinion == "disapproved")
                    .Select(item => (Analysis: item, Signature: Signature(item.Morphs)))
                    .GroupBy(item => item.Signature.Value, StringComparer.Ordinal)
                    .Select(group => new RejectedSignature(group.Key,
                        group.Select(item => item.Analysis).ToArray(), group.All(item => item.Signature.Available)))
                    .ToArray() ?? [];
                foreach (var signature in rejectedSignatures)
                {
                    var guids = signature.Analyses.Select(item => item.Guid.ToString("D").ToLowerInvariant())
                        .Order(StringComparer.Ordinal).ToArray();
                    rejected.Add(new ParserDisapprovedMorphologyProjection(assessment.AssessmentId, caseKey,
                        signature.Value, JsonSerializer.Serialize(guids), signature.IdentityAvailable ? "complete" : "unavailable",
                        signature.IdentityAvailable ? null : "A Disapproved reading has an unresolved Form or MSA identity."));
                }

                for (var analysisIndex = 0; analysisIndex < parserCase.Analyses.Count; analysisIndex++)
                {
                    var parserAnalysis = parserCase.Analyses[analysisIndex];
                    var signature = Signature(parserAnalysis.Morphs.Select(item =>
                        new SignatureMorph(item.Form, item.Msa, item.InflType)).ToArray());
                    analyses.Add(new ParserAnalysisProjection(assessment.AssessmentId, caseKey, analysisIndex,
                        signature.Value, signature.Available ? "complete" : "unavailable"));
                    for (var morphIndex = 0; morphIndex < parserAnalysis.Morphs.Count; morphIndex++)
                    {
                        var morph = parserAnalysis.Morphs[morphIndex];
                        morphs.Add(new ParserAnalysisMorphProjection(assessment.AssessmentId, caseKey,
                            analysisIndex, morphIndex, morph.Form, morph.Msa, morph.InflType,
                            morph.GuessedString?.Normalize(NormalizationForm.FormD)));
                    }

                    if (wordform is null) continue;
                    foreach (var target in wordform.Analyses.Where(item => item.Opinion == "disapproved"))
                    {
                        var targetSignature = Signature(target.Morphs);
                        if (!targetSignature.Available) continue;
                        var expected = new ApprovedMorphology(target.Morphs.Select(item => new ApprovedMorph(
                            GuidText(item.MorphGuid), GuidText(item.MsaGuid), GuidText(item.InflTypeGuid),
                            item.Forms.Select(form => form.Text).ToArray())).ToArray());
                        if (MorphologyCorrectness.Matches(parserAnalysis, expected))
                            matches.Add(new ParserDisapprovedMatchProjection(assessment.AssessmentId, caseKey,
                                analysisIndex, targetSignature.Value,
                                target.Guid.ToString("D").ToLowerInvariant()));
                    }
                }
            }

            ProjectReviewedNegativeCases(assessment.AssessmentId, parserCases, wordforms,
                reviewedNegatives, reviewedNegativeCases);

            var completedCount = cases.Skip(runStart).Count(item => item.Completion == "complete");
            runs.Add(new ParserRunProjection(assessment.AssessmentId, assessment.InvocationId,
                assessment.SourceSha256, assessment.ParserSha256, assessment.ModelFingerprint,
                "complete", assessment.TimeoutMs, assessment.StepLimit, assessment.Threads,
                parserCases.Count, completedCount, null));
        }

        return new ParserOverlayProjection(true, runs, cases, analyses, morphs, rejected, matches,
            reviewedNegativeCases);
    }

    private static void ProjectReviewedNegativeCases(string assessmentId,
        IReadOnlyList<ParseWordEvidence> parserCases, IReadOnlyList<EvidenceWordform> wordforms,
        IReadOnlyList<ReviewedNegativeExpectation> reviewedNegatives,
        ICollection<ParserReviewedNegativeCaseProjection> projected)
    {
        var identities = wordforms.SelectMany(wordform => wordform.Forms.Select(form =>
                new FormIdentity(form.TextNfd, form.WritingSystem, wordform.Guid)))
            .GroupBy(item => item.Nfd, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Distinct().ToArray(), StringComparer.Ordinal);
        foreach (var negative in reviewedNegatives)
        {
            var formNfd = negative.Form.Normalize(NormalizationForm.FormD);
            var candidates = parserCases.Select((parserCase, index) => (parserCase, index))
                .Where(item => item.parserCase.Word.Normalize(NormalizationForm.FormD) == formNfd)
                .ToArray();
            var exact = candidates.Where(item =>
            {
                var forms = identities.GetValueOrDefault(formNfd, []);
                if (forms.Length != 1 || forms[0].WritingSystem != negative.WritingSystem) return false;
                if (negative.WordformId is null) return true;
                return CanonicalId.Parse(negative.WordformId).ToGuid() == forms[0].Guid;
            }).ToArray();
            if (exact.Length == 0)
            {
                projected.Add(new ParserReviewedNegativeCaseProjection(assessmentId, negative.CaseId,
                    negative.RevisionId, negative.ContentDigest, negative.WritingSystem, formNfd,
                    negative.Status, null, "unavailable", "unavailable", false, "[]",
                    candidates.Length == 0
                        ? "The Assessment does not include this reviewed-negative surface."
                        : "The parser surface does not resolve uniquely to the reviewed writing system."));
                continue;
            }

            foreach (var item in exact)
            {
                var parserCase = item.parserCase;
                var acceptedOrdinals = parserCase.Analyses.Select((analysis, ordinal) => (analysis, ordinal))
                    .Where(candidate => negative.Status == "eligible" &&
                        MatchesReviewedNegative(candidate.analysis, negative.Target))
                    .Select(candidate => candidate.ordinal).ToArray();
                var complete = !parserCase.Capped && !parserCase.TimedOut && !parserCase.InvalidShape;
                projected.Add(new ParserReviewedNegativeCaseProjection(assessmentId, negative.CaseId,
                    negative.RevisionId, negative.ContentDigest, negative.WritingSystem, formNfd,
                    negative.Status, CaseKey(assessmentId, item.index, formNfd),
                    complete ? "complete" : "incomplete", "complete", acceptedOrdinals.Length > 0,
                    JsonSerializer.Serialize(acceptedOrdinals), complete ? null :
                    "The parser search was capped, timed out or returned an invalid shape."));
            }
        }
    }

    private static bool MatchesReviewedNegative(ParseAnalysis actual, NegativeJudgmentTarget target)
    {
        if (target is SurfaceNegativeTarget) return true;
        if (target is not ReadingNegativeTarget reading || actual.Morphs.Count != reading.Morphs.Count)
            return false;
        for (var index = 0; index < actual.Morphs.Count; index++)
        {
            var produced = actual.Morphs[index];
            var expected = reading.Morphs[index].Identity;
            if (CanonicalGuid(produced.Form) != CanonicalGuid(expected.Form) ||
                CanonicalGuid(produced.Msa) != CanonicalGuid(expected.Msa) ||
                CanonicalGuid(produced.InflType) != CanonicalGuid(expected.InflType) ||
                (expected.GuessedString is not null &&
                 produced.GuessedString?.Normalize(NormalizationForm.FormD) !=
                 expected.GuessedString.Normalize(NormalizationForm.FormD)))
                return false;
        }
        return true;
    }

    private static void ValidateBinding(ParserAssessmentSource assessment, string sourceSha256,
        string parserSha256, string? proposalId, string? proposalIntentDigest)
    {
        if (assessment.Assessor != "pangloss" || assessment.Kind != AssessmentKinds.ParseTime ||
            assessment.SourceSha256 != sourceSha256 || assessment.ParserSha256 != "sha256:" + parserSha256 ||
            assessment.ProposalId != proposalId || assessment.ProposalIntentDigest != proposalIntentDigest ||
            assessment.Threads < 1 || string.IsNullOrWhiteSpace(assessment.InvocationId))
            throw new InvalidDataException(
                $"Assessment '{assessment.AssessmentId}' does not match the exact source and parser for this Report.");
        if (assessment.SourcePath is null || !File.Exists(assessment.SourcePath) ||
            BatchInvocationEvidence.DigestFile(assessment.SourcePath) != sourceSha256)
            throw new InvalidDataException(
                $"Assessment '{assessment.AssessmentId}' has no retained source matching this Report.");
    }

    private static bool TryReadCases(ParserAssessmentSource assessment,
        out IReadOnlyList<ParseWordEvidence> cases, out string? reason)
    {
        cases = [];
        if (assessment.SidecarPath is null || assessment.SidecarSha256 is null ||
            !File.Exists(assessment.SidecarPath))
        {
            reason = "The retained morphology sidecar is unavailable.";
            return false;
        }
        try
        {
            if (BatchInvocationEvidence.DigestFile(assessment.SidecarPath) != assessment.SidecarSha256)
            {
                reason = "The retained morphology sidecar digest does not match its Assessment.";
                return false;
            }
            var stored = assessment.StoredCases;
            if (stored.Count == 0 || stored.Any(item => item is null))
            {
                reason = "The Assessment has no complete stored parser cases.";
                return false;
            }
            var parsed = ParseMorphEvidence.Read(File.ReadAllText(assessment.SidecarPath),
                stored.Select(item => item!.Word).ToArray());
            for (var index = 0; index < parsed.Count; index++)
            {
                var parserCase = parsed[index];
                var storedCase = stored[index]!;
                var addedUnavailable = storedCase.Unavailable.Count - parserCase.Unavailable.Count;
                var addedWarnings = storedCase.Unavailable.Skip(parserCase.Unavailable.Count).ToArray();
                var mayAddAttempts = parserCase.Attempts is null && storedCase.Attempts is >= 0;
                var mayAddGrammarWarnings = parserCase.Analyses.Count == 0 && parserCase.Unavailable.Count == 0 &&
                    !parserCase.Capped && !parserCase.TimedOut && !parserCase.InvalidShape &&
                    addedWarnings.All(IsAssessmentGrammarWarning);
                if (addedUnavailable < 0 || (addedUnavailable > 0 && !mayAddGrammarWarnings) ||
                    (storedCase.Attempts != parserCase.Attempts && !mayAddAttempts) ||
                    storedCase.Unavailable.Any(string.IsNullOrWhiteSpace) ||
                    !storedCase.Unavailable.Take(parserCase.Unavailable.Count)
                        .SequenceEqual(parserCase.Unavailable, StringComparer.Ordinal) ||
                    JsonSerializer.Serialize(parserCase with
                    {
                        Attempts = storedCase.Attempts,
                        Unavailable = storedCase.Unavailable,
                    },
                        ParseMorphEvidence.JsonOptions) !=
                    JsonSerializer.Serialize(storedCase, ParseMorphEvidence.JsonOptions))
                {
                    reason = "The retained sidecar and stored Assessment cases disagree.";
                    return false;
                }
            }
            cases = parsed;
            reason = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            reason = "The retained morphology sidecar could not be validated.";
            return false;
        }
    }

    private static bool IsAssessmentGrammarWarning(string warning) =>
        warning.StartsWith("capability:", StringComparison.OrdinalIgnoreCase) ||
        warning.Contains("cannot be loaded as an affix rule", StringComparison.OrdinalIgnoreCase) ||
        warning.Contains("cannot be checked against the phoneme inventory", StringComparison.OrdinalIgnoreCase);

    private static (string Value, bool Available) Signature(IReadOnlyList<EvidenceMorph> morphs) =>
        Signature(morphs.Select(item => new SignatureMorph(GuidText(item.MorphGuid), GuidText(item.MsaGuid),
            GuidText(item.InflTypeGuid))).ToArray());

    private static (string Value, bool Available) Signature(IReadOnlyList<SignatureMorph> morphs)
    {
        var available = morphs.Count > 0 && morphs.All(item => item.Form is not null && item.Msa is not null);
        var canonical = JsonSerializer.Serialize(morphs.Select(item => new
        {
            form = CanonicalGuid(item.Form),
            msa = CanonicalGuid(item.Msa),
            inflType = CanonicalGuid(item.InflType),
        }));
        return (Digest(canonical), available);
    }

    private static string? GuidText(Guid? value) => value?.ToString("D").ToLowerInvariant();

    private static string? CanonicalGuid(string? value) => Guid.TryParseExact(value, "D", out var guid)
        ? guid.ToString("D").ToLowerInvariant() : value;

    private static string CaseKey(string assessmentId, int index, string surfaceNfd) =>
        "case/" + Digest(assessmentId + "\n" + index + "\n" + surfaceNfd);

    private static string Digest(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed record FormIdentity(string Nfd, string? WritingSystem, Guid Guid);
    private sealed record SignatureMorph(string? Form, string? Msa, string? InflType);
    private sealed record RejectedSignature(string Value, IReadOnlyList<EvidenceAnalysis> Analyses,
        bool IdentityAvailable);
}

/// <summary>The validated Assessment material passed from the durable repository.</summary>
public sealed record ParserAssessmentSource(string AssessmentId, string Assessor, string Kind,
    string? ProposalId, string? ProposalIntentDigest, string SourceSha256, string ParserSha256,
    string? ModelFingerprint, string InvocationId,
    string? SourcePath, string? SidecarPath, string? SidecarSha256, int? TimeoutMs, string StepLimit,
    int Threads, IReadOnlyList<ParseWordEvidence?> StoredCases);

/// <summary>One parser overlay and its immutable source material.</summary>
public sealed record ParserOverlayProjection(bool Requested,
    IReadOnlyList<ParserRunProjection> Runs, IReadOnlyList<ParserCaseProjection> Cases,
    IReadOnlyList<ParserAnalysisProjection> Analyses, IReadOnlyList<ParserAnalysisMorphProjection> Morphs,
    IReadOnlyList<ParserDisapprovedMorphologyProjection> DisapprovedMorphologies,
    IReadOnlyList<ParserDisapprovedMatchProjection> DisapprovedMatches,
    IReadOnlyList<ParserReviewedNegativeCaseProjection> ReviewedNegativeCases)
{
    /// <summary>The overlay state when a Report names no Assessment.</summary>
    public static ParserOverlayProjection NotRequested { get; } = new(false, [], [], [], [], [], [], []);
}

/// <summary>One parser invocation's exact input binding and aggregate case completion.</summary>
public sealed record ParserRunProjection(string AssessmentId, string InvocationId, string SourceSha256,
    string ParserSha256, string? ModelFingerprint, string Status, int? TimeoutMs, string StepLimit,
    int Threads, int RequestedCaseCount, int CompletedCaseCount, string? Reason);

/// <summary>One exact surface case from a retained parser invocation.</summary>
public sealed record ParserCaseProjection(string AssessmentId, string CaseKey, string Surface,
    string SurfaceNfd, string? WritingSystem, string? WordformGuid, string Completion,
    string IdentityStatus, string? Reason, bool Capped, bool TimedOut, bool InvalidShape,
    int? Attempts, int? Passes, int AnalysisCount);

/// <summary>One ordered parser reading and its canonical ordered-triple signature.</summary>
public sealed record ParserAnalysisProjection(string AssessmentId, string CaseKey, int Ordinal,
    string Signature, string IdentityStatus);

/// <summary>One morph identity from an ordered parser reading.</summary>
public sealed record ParserAnalysisMorphProjection(string AssessmentId, string CaseKey,
    int AnalysisOrdinal, int MorphOrdinal, string? FormGuid, string? MsaGuid, string? InflTypeGuid,
    string? GuessedStringNfd);

/// <summary>One distinct Disapproved morphology and the source analyses that share it.</summary>
public sealed record ParserDisapprovedMorphologyProjection(string AssessmentId, string CaseKey,
    string Signature, string AnalysisGuidsJson, string IdentityStatus, string? Reason);

/// <summary>An exact parser reading match to a Disapproved morphology signature.</summary>
public sealed record ParserDisapprovedMatchProjection(string AssessmentId, string CaseKey,
    int ParserAnalysisOrdinal, string DisapprovedSignature, string DisapprovedAnalysisGuid);

/// <summary>One Assessment's exact case evidence for a human-confirmed negative.</summary>
public sealed record ParserReviewedNegativeCaseProjection(string AssessmentId, string CaseId,
    string RevisionId, string ContentDigest, string WritingSystem, string FormNfd, string ExpectationStatus,
    string? CaseKey, string Completion, string IdentityStatus, bool Accepted,
    string AcceptedAnalysisOrdinalsJson, string? Reason);

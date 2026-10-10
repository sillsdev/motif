using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SIL.Motif.Commands.Parsimony;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Retirement;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Store;
using SIL.Motif.Host.Baselines;
using SIL.Motif.Projection.Retirement;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Parsimony;
using SIL.Motif.Worker.Store;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Commands.Retirement;

internal static class RetirementReviewProjectionAssembler
{
    private const string ParseTime = "ParseTime";

    internal static RetirementProposalReviewProjection? Build(MotifDatabase database, DraftDocument draft,
        Proposal proposal, DryRunProjection dryRun, JobRecord dryRunJob, JobRecord candidateJob,
        ReplaceListedAllomorphsWithRuleIntent retirement, AllomorphReferenceFootprint footprint,
        ParsimonyCandidateEvidenceResponse evidence, ICollection<string> unavailable)
    {
        var input = ParsimonyCandidateJobInput.Parse(candidateJob.InputJson);
        var translation = input.RetirementExpectationTranslation;
        if (translation is null)
        {
            unavailable.Add("Frozen affected-reading expectations are not available for this candidate yet.");
            return null;
        }

        var boundFinding = FindBoundFinding(database, draft);
        if (boundFinding is null)
        {
            unavailable.Add("This Draft is not linked to the issue the rule is meant to address yet.");
            return null;
        }
        if (boundFinding.Report.Inputs.BaselineToken != evidence.Candidate.BaselineToken)
            throw new InvalidDataException("The bound finding comes from a different Baseline than the candidate Reports.");
        if (boundFinding.Finding.MeasureId != evidence.MeasureId)
            throw new InvalidDataException("The candidate measure does not match the bound finding's measure.");

        ValidateStoredReport(database, evidence.Before);
        ValidateStoredReport(database, evidence.After);
        ValidateSourceFootprint(dryRunJob, retirement, footprint);
        ValidateBeforeFinding(boundFinding.Finding, evidence.Before);
        var afterFinding = evidence.After.Findings.SingleOrDefault(item =>
            item.FindingId == boundFinding.Finding.FindingId);
        if (afterFinding is not null && afterFinding.MeasureId != boundFinding.Finding.MeasureId)
            throw new InvalidDataException("The after Report reuses the bound finding id for another measure.");

        var assessments = ReadAssessmentPair(database, evidence, unavailable);
        if (assessments is null) return null;
        var (beforeAssessment, afterAssessment) = assessments.Value;
        if (translation.Original.Baseline != evidence.Candidate.BaselineToken ||
            translation.ProposalIntentDigest != IntentDigest.Compute(proposal) ||
            translation.DryRunEffectDigest != dryRun.EffectDigest ||
            translation.DryRunFootprintDigest != dryRun.FootprintDigest)
            throw new InvalidDataException("Frozen expectations do not match this Draft and Dry Run.");

        var verification = RecipeVerification.Compare(translation,
            RecipeVerificationTrialProjection.From(beforeAssessment),
            RecipeVerificationTrialProjection.From(afterAssessment), RecipeVerificationCriteria.Tightening);
        var statistics = Statistics(footprint, translation, dryRun, boundFinding.Finding, evidence, afterFinding);
        var attributions = translation.Original.Cases.SelectMany(@case => @case.Readings
            .Where(reading => reading.Opinion == "approved")
            .Select(reading => new RetirementRuleAttribution(@case.CaseId, reading.ReadingId,
                "unavailable", null, null, "PanGloss did not retain a rule trace for this reading."))).ToArray();
        var afterSurfaces = afterAssessment.Words!.GroupBy(word =>
                (word.Morphology?.Word ?? word.Word).Normalize(NormalizationForm.FormD), StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single().Morphology?.Word ?? group.Single().Word,
                StringComparer.Ordinal);
        var observedAfter = translation.Original.Cases.Where(@case => afterSurfaces.ContainsKey(@case.Surface))
            .ToDictionary(@case => @case.CaseId, @case => afterSurfaces[@case.Surface], StringComparer.Ordinal);
        var diagnostics = CombineDiagnostics(footprint, retirement);
        return RetirementProposalReviewProjectionBuilder.Build(proposal, dryRun, statistics, translation,
            verification, evidence, footprint, boundFinding.Finding.FindingId, attributions, observedAfter, diagnostics,
            boundFinding.OperationIds);
    }

    private static BoundFinding? FindBoundFinding(MotifDatabase database, DraftDocument draft)
    {
        var entries = draft.ComposerProvenance.Where(item => item.ValueKind == JsonValueKind.Object &&
            item.TryGetProperty("composer", out var composer) && composer.ValueKind == JsonValueKind.String &&
            composer.GetString() == "RecordParsimonyDisposition" && item.TryGetProperty("input", out _)).ToArray();
        if (entries.Length == 0) return null;
        if (entries.Length != 1)
            throw new InvalidDataException("The Draft has several issue bindings and the retirement review is ambiguous.");
        var operationIds = ReadBindingOperationIds(entries[0]);
        if (operationIds.Count == 0 || operationIds.Any(id => draft.Operations.All(operation => operation.OperationId != id)))
            throw new InvalidDataException("The stored finding binding does not name its complete Draft operation group.");
        var authored = entries[0].GetProperty("input");
        var intent = RecordParsimonyDispositionIntentParser.Parse(authored);
        if (intent.ReportId is null) return null;
        var record = new ReportRepository(database).Get(intent.ReportId.Value.Value);
        if (record is null) return null;
        var report = JsonSerializer.Deserialize<ParsimonyReportResponse>(record.ReportJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("The bound Parsimony Report is empty.");
        if (report.ReportId != record.ReportId || report.ReportId != intent.ReportId.Value.Value)
            throw new InvalidDataException("The bound Parsimony Report identity does not match its stored row.");
        var matches = report.Findings.Where(finding => finding.MeasureId == intent.MeasureId &&
            CanonicalDigest(finding.EvidenceDigest) == intent.EvidenceDigest && SubjectMatches(intent.Subject, finding))
            .ToArray();
        if (matches.Length != 1)
            throw new InvalidDataException("The bound issue does not identify one exact finding in its source Report.");
        return new BoundFinding(report, matches[0], operationIds);
    }

    private static IReadOnlyList<string> ReadBindingOperationIds(JsonElement provenance)
    {
        if (!provenance.TryGetProperty("operationIds", out var value) || value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The stored finding binding has no operation identities.");
        var ids = value.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String
            ? CanonicalId.Parse(item.GetString()!).Value
            : throw new InvalidDataException("A stored finding-binding operation identity is not a string.")).ToArray();
        if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
            throw new InvalidDataException("The stored finding binding repeats an operation identity.");
        return Array.AsReadOnly(ids);
    }

    private static bool SubjectMatches(HumanJudgmentSubject subject, ParsimonyFinding finding) => subject switch
    {
        ParsimonyFindingJudgmentSubject direct => direct.MeasureId == finding.MeasureId &&
            direct.FindingId == finding.FindingId,
        GroupJudgmentSubject group => GroupMatches(group, finding),
        _ => false,
    };

    private static bool GroupMatches(GroupJudgmentSubject subject, ParsimonyFinding finding)
    {
        var groupKind = subject.Role switch
        {
            JudgmentGroupRole.DuplicatePair => ParsimonyGroupKind.AllomorphDuplicateForms,
            JudgmentGroupRole.AlternationFamily => ParsimonyGroupKind.AlternationFamily,
            JudgmentGroupRole.AdhocSlotOrder => ParsimonyGroupKind.AdhocSlotOrder,
            _ => (ParsimonyGroupKind?)null,
        };
        if (groupKind is null || subject.MeasureId != finding.MeasureId ||
            finding.AttachesTo.Kind != ParsimonyAttachmentKind.Group || finding.AttachesTo.GroupKind != groupKind)
            return false;
        var view = subject.Role == JudgmentGroupRole.AdhocSlotOrder ? "adhoc-context" : "allomorph-context";
        var members = new HashSet<Guid>(subject.Members.Select(item => CanonicalId.Parse(item.Id).ToGuid()));
        var reported = new HashSet<Guid>();
        foreach (var reference in finding.EvidenceRefs.Where(item => item.View == view))
        {
            if (reference.Arguments.ValueKind != JsonValueKind.Object ||
                !reference.Arguments.TryGetProperty("objectGuid", out var identity) ||
                identity.ValueKind != JsonValueKind.String) return false;
            reported.Add(ParseGuid(identity.GetString()!));
        }
        return members.Count == subject.Members.Count && members.SetEquals(reported);
    }

    private static void ValidateBeforeFinding(ParsimonyFinding bound, ParsimonyReportResponse before)
    {
        var matching = before.Findings.SingleOrDefault(item => item.FindingId == bound.FindingId);
        if (matching is null || matching.MeasureId != bound.MeasureId ||
            CanonicalDigest(matching.EvidenceDigest) != CanonicalDigest(bound.EvidenceDigest))
            throw new InvalidDataException("The candidate Before Report does not reproduce the bound finding and digest.");
    }

    private static void ValidateStoredReport(MotifDatabase database, ParsimonyReportResponse report)
    {
        var stored = new ReportRepository(database).Get(report.ReportId)
            ?? throw new InvalidDataException($"Candidate Report '{report.ReportId}' is missing from the project store.");
        var recorded = JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (!string.Equals(CanonicalJson.Canonicalize(stored.ReportJson), CanonicalJson.Canonicalize(recorded),
                StringComparison.Ordinal))
            throw new InvalidDataException($"Candidate Report '{report.ReportId}' differs from its stored record.");
    }

    private static void ValidateSourceFootprint(JobRecord dryRunJob, ReplaceListedAllomorphsWithRuleIntent retirement,
        AllomorphReferenceFootprint liveFootprint)
    {
        if (dryRunJob.ResultJson is null)
            throw new InvalidDataException("The completed Dry Run has no source Baseline binding.");
        var source = DryRunJobCompletion.Parse(dryRunJob.ResultJson).SourceBaseline;
        using var reader = BaselineReadCache.Open(source.FwDataPath);
        var sourceFootprint = AllomorphReferenceFootprintReader.Read(reader.Cache,
            retirement.Retirements.SelectMany(item => item.RetiredForms)
                .Select(item => CanonicalId.Parse(item.Id).ToGuid()));
        if (sourceFootprint.Digest != liveFootprint.Digest)
            throw new InvalidDataException("The live project's retirement references differ from the Dry Run source.");
    }

    private static (AssessmentRecord Before, AssessmentRecord After)? ReadAssessmentPair(
        MotifDatabase database, ParsimonyCandidateEvidenceResponse evidence, ICollection<string> unavailable)
    {
        if (evidence.After.AssessmentIds.Count != 1 || evidence.After.Inputs.AssessmentIds.Count != 1 ||
            evidence.After.AssessmentIds[0] != evidence.After.Inputs.AssessmentIds[0])
        {
            unavailable.Add("One exact after parser result is not available for the affected readings yet.");
            return null;
        }
        var repository = new AssessmentRepository(database);
        AssessmentRecord after;
        try
        {
            after = repository.Get(evidence.After.AssessmentIds[0]);
        }
        catch (KeyNotFoundException)
        {
            unavailable.Add("The after parser result is not available in the project store yet.");
            return null;
        }
        var recordedBaseline = JsonSerializer.Deserialize<BaselineToken>(after.BaselineToken, MotifJson.CreateOptions());
        if (recordedBaseline != evidence.Candidate.BaselineToken || after.Assessor != "pangloss" ||
            after.Kind != ParseTime || after.ProposalId?.Value != evidence.Candidate.ProposalId ||
            after.ProposalIntentDigest != evidence.Candidate.ProposalIntentDigest || after.Invocation is null ||
            after.Invocation.SourceBytesSha256 != evidence.Candidate.CandidateSourceSha256 ||
            after.GrammarSourceSha256 != after.Invocation.SourceBytesSha256)
            throw new InvalidDataException("The after parser Assessment does not match this candidate Report.");

        var matchingBefore = repository.ListBaselineAssessments(ParseTime).Where(candidate =>
                candidate.BaselineToken == after.BaselineToken && candidate.Assessor == after.Assessor &&
                candidate.Selection.Sha256 == after.Selection.Sha256 &&
                candidate.Selection.Words.SequenceEqual(after.Selection.Words, StringComparer.Ordinal) &&
                candidate.TokeniserName == after.TokeniserName && candidate.TokeniserVersion == after.TokeniserVersion &&
                candidate.Invocation is { } beforeInvocation &&
                beforeInvocation.ExecutableBytesSha256 == after.Invocation.ExecutableBytesSha256 &&
                beforeInvocation.PerWordTimeoutMs == after.Invocation.PerWordTimeoutMs &&
                beforeInvocation.PerWordStepLimit == after.Invocation.PerWordStepLimit &&
                beforeInvocation.Threads == after.Invocation.Threads &&
                beforeInvocation.CollectStatistics == after.Invocation.CollectStatistics &&
                beforeInvocation.SourceBytesSha256 == evidence.Candidate.BaselineSourceSha256 &&
                candidate.GrammarSourceSha256 == beforeInvocation.SourceBytesSha256)
            .ToArray();
        if (matchingBefore.Length != 1)
        {
            unavailable.Add(matchingBefore.Length == 0
                ? "A matching before parser result is not available yet."
                : "More than one before parser result matches this candidate, so the pair is ambiguous.");
            return null;
        }
        return (matchingBefore[0], after);
    }

    private static RetirementReviewStatistics Statistics(AllomorphReferenceFootprint footprint,
        RetirementExpectationTranslation translation, DryRunProjection dryRun, ParsimonyFinding beforeFinding,
        ParsimonyCandidateEvidenceResponse evidence, ParsimonyFinding? afterFinding)
    {
        var bundleRefs = footprint.References.Where(item => item.Kind == "bundle-morph").ToArray();
        var adhocRefs = footprint.References.Where(item => item.Kind.StartsWith("adhoc-", StringComparison.Ordinal))
            .ToArray();
        var formChanges = dryRun.Effects.Where(effect => effect.Field == "analysis/wfiMorphBundle/form")
            .SelectMany(effect => effect.Changes).ToArray();
        var deletedForms = dryRun.Operations.Where(operation =>
            operation.Kind == "lexical/lexEntry/deleteAlternateForm")
            .SelectMany(operation => operation.AfterJson is { } json ? DeletedFormIds(json) : [])
            .Distinct(StringComparer.Ordinal).Count();
        var detector = new RetirementDetectorCounts(beforeFinding.FindingId,
            checked((int)beforeFinding.Number.Numerator), checked((int)beforeFinding.Number.Denominator),
            checked((int)(afterFinding?.Number.Numerator ?? 0)), checked((int)(afterFinding?.Number.Denominator ?? 0)),
            CanonicalDigest(beforeFinding.EvidenceDigest),
            CanonicalDigest(afterFinding?.EvidenceDigest ?? evidence.After.Inputs.Evidence.Sha256),
            afterFinding is null);
        var adhocCounts = new List<RetirementAdhocCounts>();
        foreach (var grouped in new[] { false, true })
        foreach (var enabled in new[] { false, true })
        {
            var rows = adhocRefs.Where(item => item.Grouped == grouped && item.Enabled == enabled).ToArray();
            adhocCounts.Add(new RetirementAdhocCounts(grouped, enabled,
                rows.Select(item => item.Rule).OfType<Guid>().Distinct().Count(), rows.Length));
        }
        return new RetirementReviewStatistics(
            OpinionCounts(bundleRefs.Select(item => (Required(item.Bundle, "bundle"), Required(item.Opinion, "opinion")))),
            OpinionCounts(bundleRefs.Select(item => (Required(item.Analysis, "analysis"), Required(item.Opinion, "opinion")))),
            OpinionCounts(footprint.AffectedWordforms.SelectMany(wordform => wordform.Analyses.Select(analysis =>
                (CanonicalId.FromGuid(wordform.Wordform).Value, analysis.Opinion)))),
            translation.Original.Cases.Count, Array.AsReadOnly(adhocCounts.ToArray()),
            footprint.References.Count(item => item.Kind != "bundle-morph" &&
                !item.Kind.StartsWith("adhoc-", StringComparison.Ordinal) && item.Kind != "entry-alternate-form"),
            deletedForms + footprint.OwnedDependents.Count,
            formChanges.Length, formChanges.Count(item => item.After is null), detector, footprint.Digest);
    }

    private static RetirementOpinionCounts OpinionCounts(IEnumerable<(string Id, string Opinion)> values)
    {
        var counts = new int[4];
        foreach (var group in values.GroupBy(item => item.Id, StringComparer.Ordinal))
        {
            var opinions = group.Select(item => item.Opinion).Distinct(StringComparer.Ordinal).ToArray();
            var slot = opinions.Length != 1 ? 3 : opinions[0] switch
            {
                "approved" => 0,
                "disapproved" => 1,
                "unknown" => 2,
                _ => throw new InvalidDataException("A reference Opinion is outside the review contract."),
            };
            counts[slot]++;
        }
        return new RetirementOpinionCounts(counts[0], counts[1], counts[2], counts[3]);
    }

    private static AllomorphReferenceDestinationDiagnostics CombineDiagnostics(
        AllomorphReferenceFootprint footprint, ReplaceListedAllomorphsWithRuleIntent retirement)
    {
        var parts = retirement.Retirements.Select(item => AllomorphReferenceFootprintReader.Diagnose(footprint, item))
            .ToArray();
        var rows = parts.SelectMany(item => item.UnresolvedReferences).Distinct().ToArray();
        return new AllomorphReferenceDestinationDiagnostics(rows,
            rows.Where(item => item.Kind == "bundle-morph" && item.Opinion == "approved")
                .Select(item => item.Analysis).OfType<Guid>().Distinct().Count(),
            rows.Where(item => item.Kind.StartsWith("adhoc-", StringComparison.Ordinal))
                .Select(item => item.SourceObject).Distinct().Count(),
            rows.Count(item => item.IsCustom), rows.Count(item => !item.IsCustom && item.Kind == "other-native"),
            string.Join(" ", parts.Select(item => item.Message).Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.Ordinal)))
        {
            Unavailable = Array.AsReadOnly(parts.SelectMany(item => item.Unavailable)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()),
        };
    }

    private static IEnumerable<string> DeletedFormIds(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.String && root.GetString() is { } inner)
        {
            using var nested = JsonDocument.Parse(inner);
            root = nested.RootElement.Clone();
        }
        var property = root.EnumerateObject().SingleOrDefault(item => item.Name is "members" or "forms");
        if (property.Value.ValueKind != JsonValueKind.Array) yield break;
        foreach (var member in property.Value.EnumerateArray())
        {
            var id = member.ValueKind == JsonValueKind.String ? member.GetString() :
                member.TryGetProperty("id", out var idValue) ? idValue.GetString() : null;
            if (id is not null) yield return CanonicalId.Parse(id).Value;
        }
    }

    private static string Required(Guid? id, string kind) => id is { } value
        ? CanonicalId.FromGuid(value).Value
        : throw new InvalidDataException($"A footprint row has no {kind} identity.");

    private static string Required(string? value, string kind) => string.IsNullOrWhiteSpace(value)
        ? throw new InvalidDataException($"A footprint row has no {kind}.")
        : value;

    private static Guid ParseGuid(string value) => Guid.TryParse(value, out var guid)
        ? guid
        : CanonicalId.Parse(value).ToGuid();

    private static string CanonicalDigest(string digest) => digest.StartsWith("sha256:", StringComparison.Ordinal)
        ? digest
        : "sha256:" + digest;

    private sealed record BoundFinding(ParsimonyReportResponse Report, ParsimonyFinding Finding,
        IReadOnlyList<string> OperationIds);
}

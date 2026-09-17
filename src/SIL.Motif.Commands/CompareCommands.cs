using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.Parser;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands;

/// <summary>
/// The <c>compare</c> verb. ADR 0042's amendment: comparison is a join on the word, and <c>compare</c> is
/// sugar over producing an Assessment of the <c>Difference</c> kind, stored and citable — never a separate
/// transient mechanism. This computes the join, records the result through
/// <see cref="AssessmentRepository"/> exactly like any other Assessment, and renders it through the same
/// <see cref="DifferenceReportProducer"/> that <c>report --kind difference</c> would use to read it back
/// later, so the preview returned here and a later reading of the stored row never disagree.
/// </summary>
public static class CompareCommands
{
    /// <summary>
    /// Loads the two named Assessments, joins them on the word, stores the result as a new Assessment of the
    /// <c>Difference</c> kind, and returns it.
    /// </summary>
    /// <remarks>
    /// The stored Difference inherits the Assessor its two inputs share: a difference between two
    /// measurements made by one Assessor is itself a measurement of that Assessor's making, and requiring
    /// the two inputs to agree is what makes the shared-Assessor rule (ADR 0042 decision 1) enforce itself —
    /// a difference cannot be produced from inputs that disagree, because there would be no single Assessor
    /// left to attribute it to.
    /// </remarks>
    public static CommandOutcome<CompareResponse> Produce(ProduceComparisonRequest request)
    {
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, _) =>
        {
            var repository = new AssessmentRepository(database);
            AssessmentRecord from;
            AssessmentRecord to;
            try
            {
                from = repository.Get(request.FromAssessmentId);
                to = repository.Get(request.ToAssessmentId);
            }
            catch (KeyNotFoundException exception)
            {
                return CommandOutcome<CompareResponse>.Refused(new Refusal(
                    "comparison.assessment-not-found", FailureReason.NotFound, exception.Message,
                    Fact(("fromAssessmentId", request.FromAssessmentId), ("toAssessmentId", request.ToAssessmentId))));
            }
            catch (ArgumentException exception)
            {
                return CommandOutcome<CompareResponse>.Refused(new Refusal(
                    "comparison.assessment-not-found", FailureReason.InvalidArgument, exception.Message,
                    Fact(("fromAssessmentId", request.FromAssessmentId), ("toAssessmentId", request.ToAssessmentId))));
            }

            AssessmentComparison comparison;
            try
            {
                comparison = AssessmentComparer.Compare(from.ToComparable(), to.ToComparable());
            }
            catch (ComparisonRefusalException exception)
            {
                return CommandOutcome<CompareResponse>.Refused(new Refusal(
                    "comparison.refused", FailureReason.Refused, exception.Message,
                    Fact(("fromAssessmentId", request.FromAssessmentId), ("toAssessmentId", request.ToAssessmentId))));
            }

            var assessmentId = CanonicalId.Mint("assessment/").Value;
            var scopeJson = ScopeCodec.Write(new StoredScope.Difference(
                request.FromAssessmentId, request.ToAssessmentId, comparison.FromWordCount, comparison.ToWordCount,
                comparison.SharedWords.Count, from.GrammarSourceSha256, to.GrammarSourceSha256,
                comparison.TokeniserMismatch, comparison.TokeniserWarning));
            var words = comparison.Changes
                .Select(change => new AssessedWord(
                    change.Word, $"{change.Kind}:{change.FromOutcome}->{change.ToOutcome}",
                    Array.Empty<ParsedAnalysis>()))
                .ToArray();
            var selection = Selection.Create(
                $"difference:{request.FromAssessmentId}..{request.ToAssessmentId}", comparison.SharedWords);
            var (tokeniserName, tokeniserVersion) = comparison.TokeniserMismatch
                ? ("mixed", "mixed")
                : (from.TokeniserName, from.TokeniserVersion);

            repository.Record(new NewAssessmentRecord(
                AssessmentId: assessmentId,
                ProposalId: null,
                ProposalIntentDigest: null,
                Assessor: from.Assessor,
                Kind: AssessmentKind.Difference.ToStoredKind(),
                ScopeJson: scopeJson,
                ScopeDigest: Digest(scopeJson),
                TokeniserName: tokeniserName,
                TokeniserVersion: tokeniserVersion,
                BaselineToken: "{\"from\":" + from.BaselineToken + ",\"to\":" + to.BaselineToken + "}",
                Selection: selection,
                OutcomeDigest: Digest(scopeJson),
                SemanticDigest: Digest(string.Join('\n', words.Select(w => w.Word + "|" + w.Outcome))),
                GrammarSourceSha256: string.Empty,
                ModelFingerprint: "motif-comparison",
                Pipeline: "compare",
                DiagnosticCount: comparison.TokeniserMismatch ? 1 : 0,
                Words: words));

            // Read back the row just recorded, so this can't disagree with a later report of the same row.
            var stored = repository.Get(assessmentId);
            var rendered = ReportCommands.Catalog.Resolve(DifferenceReportProducer.KindName)
                .Produce(stored.ToReportable(), new ReportQuery(), AssessorCatalog.Empty);

            return CommandOutcome<CompareResponse>.Success(new CompareResponse(
                assessmentId, request.FromAssessmentId, request.ToAssessmentId, from.Assessor,
                comparison.FromWordCount, comparison.ToWordCount, comparison.SharedWords.Count,
                comparison.TokeniserMismatch, comparison.TokeniserWarning, rendered.Text));
        });
    }

    private static string Digest(string json)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static Dictionary<string, string> Fact(params (string Key, string? Value)[] entries)
    {
        var facts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in entries)
        {
            if (value is not null) facts[key] = value;
        }
        return facts;
    }
}

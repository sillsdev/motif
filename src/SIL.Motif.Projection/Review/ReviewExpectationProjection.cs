using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Review;

namespace SIL.Motif.Projection.Review;

/// <summary>A frozen Notebook surface assertion and the exact case it describes.</summary>
public sealed record ReviewNegativeExpectation(string CaseId, string Word, string WritingSystem,
    string Context, string? WordformId, ReviewAssertion Assertion);

/// <summary>One agent decision retained from an Applied Proposal revision.</summary>
public sealed record ReviewSourceDecision(Guid AnalysisId, Guid AgentId, bool Approved,
    string ProposalId, string RevisionId, string OperationId);

/// <summary>Captures native human-approved readings from a caller-owned project.</summary>
public static class ReviewExpectationProjection
{
    /// <summary>Projects applied agent decisions against the loaded Baseline's native analyses.</summary>
    public static IReadOnlyDictionary<Guid, IReadOnlyList<ReviewAssertion>> ReadSourceValidated(
        LcmCache cache, IReadOnlyList<ReviewSourceDecision> decisions)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(decisions);
        var byAnalysis = decisions.GroupBy(item => item.AnalysisId).ToDictionary(group => group.Key, group => group.ToArray());
        var result = new Dictionary<Guid, IReadOnlyList<ReviewAssertion>>();
        foreach (var word in cache.ServiceLocator.GetInstance<IWfiWordformRepository>().AllInstances())
        {
            var assertions = new List<ReviewAssertion>();
            foreach (var analysis in word.AnalysesOC)
            {
                if (!byAnalysis.TryGetValue(analysis.Guid, out var selected)) continue;
                var morphology = Morphology(word, analysis);
                var conflict = selected.Select(item => item.Approved).Distinct().Count() > 1;
                foreach (var decision in selected)
                {
                    var eligible = decision.Approved && !conflict && Exact(morphology);
                    assertions.Add(new(decision.OperationId, Identity(morphology), morphology,
                        ReferenceAuthority.SourceValidated, CanonicalId.FromGuid(decision.AgentId).Value,
                        decision.RevisionId, false, false, eligible,
                        Array.AsReadOnly(new[] { decision.ProposalId, decision.OperationId })));
                }
            }
            if (assertions.Count > 0) result.Add(word.Guid, Array.AsReadOnly(assertions.ToArray()));
        }
        return new System.Collections.ObjectModel.ReadOnlyDictionary<Guid, IReadOnlyList<ReviewAssertion>>(result);
    }

    /// <summary>Retains reviewed surface negatives from the existing frozen expectation projection.</summary>
    public static IReadOnlyList<ReviewNegativeExpectation> ReadReviewedNegatives(ParsimonyExpectationProjection source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var result = new List<ReviewNegativeExpectation>();
        foreach (var negative in source.ReviewedNegatives.Where(item =>
                     item.Target is SurfaceNegativeTarget && item.Actor.Kind == JudgmentActorKind.Human))
        {
            var eligible = negative.Status == "eligible";
            var assertion = new ReviewAssertion(negative.JudgmentId, string.Empty, null, ReferenceAuthority.Human,
                negative.Actor.Id ?? string.Empty, negative.RevisionId, false, true, eligible,
                Array.AsReadOnly(new[] { negative.NotebookRecordId, negative.ContentDigest }));
            result.Add(new(negative.CaseId, negative.Form.Normalize(NormalizationForm.FormD),
                negative.WritingSystem, negative.Context, negative.WordformId, assertion));
        }
        return Array.AsReadOnly(result.ToArray());
    }

    /// <summary>Returns immutable assertions keyed by their source wordform identity.</summary>
    public static IReadOnlyDictionary<Guid, IReadOnlyList<ReviewAssertion>> ReadApproved(LcmCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        var actor = CanonicalId.FromGuid(cache.LangProject.DefaultUserAgent.Guid).Value;
        var result = new Dictionary<Guid, IReadOnlyList<ReviewAssertion>>();
        foreach (var word in cache.ServiceLocator.GetInstance<IWfiWordformRepository>().AllInstances())
        {
            var assertions = new List<ReviewAssertion>();
            foreach (var analysis in word.HumanApprovedAnalyses.OrderBy(item => item.Guid))
            {
                var analysisId = CanonicalId.FromGuid(analysis.Guid).Value;
                var morphology = Morphology(word, analysis);
                var identity = Identity(morphology);
                var evaluation = cache.LangProject.DefaultUserAgent.ApprovesOA;
                var eligible = Exact(morphology) && evaluation is not null;
                var revision = evaluation is null ? string.Empty : CanonicalId.FromGuid(evaluation.Guid).Value;
                assertions.Add(new ReviewAssertion(analysisId, identity, morphology, ReferenceAuthority.Human,
                    actor, revision, true, false, eligible, Array.AsReadOnly(new[] { analysisId })));
            }
            if (assertions.Count > 0) result.Add(word.Guid, Array.AsReadOnly(assertions.ToArray()));
        }
        return new System.Collections.ObjectModel.ReadOnlyDictionary<Guid, IReadOnlyList<ReviewAssertion>>(result);
    }

    private static ApprovedMorphology Morphology(IWfiWordform word, IWfiAnalysis analysis)
    {
        var morphs = analysis.MorphBundlesOS.Select(bundle => new ApprovedMorph(
            bundle.MorphRA?.Guid.ToString("D"), bundle.MsaRA?.Guid.ToString("D"),
            bundle.InflTypeRA?.Guid.ToString("D"), Array.AsReadOnly(bundle.Form.AvailableWritingSystemIds
                .Select(ws => bundle.Form.get_String(ws)?.Text).OfType<string>()
                .Select(text => text.Normalize(NormalizationForm.FormD))
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()))).ToArray();
        return new ApprovedMorphology(Array.AsReadOnly(morphs))
        {
            SourceAnalysisId = CanonicalId.FromGuid(analysis.Guid).Value,
            SourceWordformGuid = word.Guid.ToString("D"),
        };
    }

    private static bool Exact(ApprovedMorphology morphology) => morphology.Morphs.Count > 0 &&
        morphology.Morphs.All(morph => morph.Form is not null && morph.Msa is not null);

    private static string Identity(ApprovedMorphology morphology) => "sha256:" + Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            morphology.Morphs.Select(morph => new { morph.Form, morph.Msa, morph.InflType })))));
}

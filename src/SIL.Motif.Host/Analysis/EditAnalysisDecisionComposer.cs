using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Resolution;

namespace SIL.Motif.Host.Analysis;

/// <summary>
/// Lowers one human decision about an existing analysis or wordform through <see cref="AnalysisChangeComposer"/>,
/// the same lowering a collected word change uses, so the two never diverge.
/// </summary>
public static class EditAnalysisDecisionComposer
{
    /// <summary>Resolves the identities against the project and returns the operations that record the decision.</summary>
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, EditAnalysisDecisionIntent intent)
    {
        if (intent.Decision == AnalysisDecisions.Misspelled)
        {
            var wordform = intent.Wordform!.Value;
            if (CanonicalIdResolver.Resolve(cache, wordform) is not IWfiWordform)
                throw new InvalidOperationException(
                    $"'EditAnalysisDecision': '{wordform.Value}' is not an existing wordform.");
            return AnalysisChangeComposer.Build(cache,
                new AnalysisChangeIntent(AnalysisChangeKinds.IncorrectSpelling, wordform, null));
        }

        var analysisId = intent.Analysis!.Value;
        if (CanonicalIdResolver.Resolve(cache, analysisId) is not IWfiAnalysis analysis ||
            analysis.Owner is not IWfiWordform owner)
            throw new InvalidOperationException(
                $"'EditAnalysisDecision': '{analysisId.Value}' is not a stored analysis of a wordform.");
        var kind = intent.Decision switch
        {
            AnalysisDecisions.Approve => AnalysisChangeKinds.Approve,
            AnalysisDecisions.Disapprove => AnalysisChangeKinds.Reject,
            _ => AnalysisChangeKinds.Candidate,
        };
        var operations = AnalysisChangeComposer.Build(cache, new AnalysisChangeIntent(
            kind, CanonicalId.FromGuid(owner.Guid), null, analysisId));
        return operations.Count > 0 ? operations : throw new InvalidOperationException(
            $"'EditAnalysisDecision': analysis '{analysisId.Value}' has no decision to return to unknown.");
    }
}

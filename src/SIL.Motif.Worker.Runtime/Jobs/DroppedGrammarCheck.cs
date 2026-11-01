using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Worker.Jobs;

/// <summary>
/// The dropped-grammar check shared by a Trial and a Dry Run: runs <c>grammar-health</c> on a candidate grammar,
/// compares it with the Baseline's own findings, and reports a result that is never clean when the check did not run.
/// </summary>
internal static class DroppedGrammarCheck
{
    /// <summary>
    /// Runs the grammar check on a candidate grammar, then compares it with the Baseline's.
    /// </summary>
    /// <returns>The candidate's check when it ran, and the dropped-grammar result either way.</returns>
    internal static async Task<(GrammarCheckResponse? Candidate, DroppedGrammar Result)> CheckAsync(
        Func<string, string, Action<bool>?, CancellationToken, Task<GrammarCheckResponse?>>? grammarHealth,
        MotifDatabase database, BaselineToken baselineToken, string baselineFwDataPath, string candidatePath, string projectName,
        Contract.Model.Proposal proposal, Action<bool>? onWait, TimeSpan bound, CancellationToken cancellationToken)
    {
        if (grammarHealth is null)
            return (null, DroppedGrammar.NotChecked("No parser is available to check the candidate grammar."));
        GrammarCheckResponse? candidate;
        try
        {
            string? timedOut;
            (candidate, timedOut) = await StageBound.RunAsync("grammar check", bound,
                token => grammarHealth(candidatePath, projectName, onWait, token), cancellationToken)
                .ConfigureAwait(false);
            if (timedOut is not null) return (null, DroppedGrammar.NotChecked(timedOut));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return (null, DroppedGrammar.NotChecked(DroppedGrammarClassifier.BoundMessage(exception.Message)));
        }
        if (candidate is null)
            return (null, DroppedGrammar.NotChecked("The grammar check did not return a report."));
        try
        {
            return (candidate, await CompareAsync(candidate, grammarHealth, database, baselineToken, baselineFwDataPath,
                projectName, proposal, onWait, bound, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A stored Baseline check that cannot be read leaves the comparison unmade, which is never clean.
            return (candidate, DroppedGrammar.NotChecked("The Baseline's grammar check could not be read: " +
                DroppedGrammarClassifier.BoundMessage(exception.Message)));
        }
    }

    /// <summary>Classifies a candidate's findings against the Baseline's stored or freshly run check.</summary>
    internal static async Task<DroppedGrammar> CompareAsync(GrammarCheckResponse candidate,
        Func<string, string, Action<bool>?, CancellationToken, Task<GrammarCheckResponse?>> grammarHealth,
        MotifDatabase database, BaselineToken baselineToken, string baselineFwDataPath, string projectName, Contract.Model.Proposal proposal,
        Action<bool>? onWait, TimeSpan bound, CancellationToken cancellationToken)
    {
        var baselineCheck = new TrialGrammarCheckRepository(database).ReadBaselineCheck(baselineToken);
        string? why = null;
        if (baselineCheck is null)
        {
            try
            {
                (baselineCheck, why) = await StageBound.RunAsync("Baseline grammar check", bound,
                    token => grammarHealth(baselineFwDataPath, projectName, onWait, token), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                why = exception.Message;
            }
        }
        // Without the Baseline's findings nothing separates the Proposal's from the project's own, so say so.
        if (baselineCheck is null)
            return DroppedGrammar.NotChecked("The Baseline's grammar check was not available" +
                (why is null ? "" : ": " + DroppedGrammarClassifier.BoundMessage(why).TrimEnd('.')) + ".");
        return DroppedGrammarClassifier.Classify(candidate, baselineCheck, ProposalObjects(proposal));
    }

    // Objects the Proposal creates or edits: a finding on one counts as the Proposal's even if it predates it.
    private static IReadOnlySet<Guid> ProposalObjects(Contract.Model.Proposal proposal) =>
        proposal.Operations.SelectMany(operation => new[] { operation.EntityId, operation.Target })
            .OfType<CanonicalId>().Select(id => id.ToGuid()).ToHashSet();
}

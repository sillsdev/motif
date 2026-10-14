using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.PanGloss;

namespace SIL.Motif.Worker.Jobs;

/// <summary>Runs the whole-grammar check a Trial stores beside its Assessment.</summary>
internal static class TrialGrammarCheck
{
    /// <summary>Runs <c>pangloss grammar-health</c> on the Trial's scratch grammar and reads its report.</summary>
    /// <param name="invoker">Runs PanGloss.</param>
    /// <param name="label">Names the run in the invoker's diagnostics.</param>
    /// <param name="storedAnalyses">How much of the stored-analysis comparison the check runs.</param>
    /// <param name="grammarPath">The scratch project the check reads.</param>
    /// <param name="projectName">The FieldWorks project name PanGloss puts in its deep links.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <param name="onAdmissionWait">Reports actual waits for machine parser capacity.</param>
    /// <returns>The grammar-health response, or null when the parser reports no response.</returns>
    internal static async Task<GrammarCheckResponse?> RunAsync(IPanGlossInvoker invoker, string label,
        StoredAnalysisComparison storedAnalyses, string grammarPath, string projectName,
        CancellationToken cancellationToken, Action<bool>? onAdmissionWait = null)
    {
        var outcome = await invoker.RunAsync(
            new PanGlossRequest.GrammarHealth(grammarPath, projectName, storedAnalyses)
            {
                OnAdmissionWait = onAdmissionWait,
            }, label, cancellationToken)
            .ConfigureAwait(false);
        var output = outcome switch
        {
            PanGlossOutcome.Completed completed => completed.Output,
            PanGlossOutcome.Refused refused when !string.IsNullOrWhiteSpace(refused.StandardOutput) =>
                refused.StandardOutput,
            _ => throw new InvalidOperationException(outcome.Message),
        };
        return GrammarHealthReportReader.ReadResponse(output);
    }
}

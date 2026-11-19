using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

internal static class ProposalReadinessWords
{
    internal static string For(ReadinessProjection readiness)
    {
        var sentences = new List<string>();
        if (readiness.MissingBoundDryRun) sentences.Add(MotifAppStrings.ProposalDryRunNeeded);
        if (readiness.RefusalCode == RefusalCodes.ApplyPendingChanges)
            sentences.Add(MotifAppStrings.RefusalApplyPendingChanges);
        if (readiness.RefusalCode == RefusalCodes.ApplyRegression)
            sentences.Add(MotifAppStrings.RefusalApplyRegression);
        if (readiness.DroppedGrammar is { } dropped) sentences.Add(DroppedGrammarWords.Blocker(dropped));
        if (sentences.Count == 0) sentences.Add(MotifAppStrings.RefusalApplyNotReady);
        return string.Join(" ", sentences);
    }
}

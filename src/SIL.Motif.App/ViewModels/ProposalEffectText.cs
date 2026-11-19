using System.Globalization;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Model.Snapshot;

namespace SIL.Motif.App.ViewModels;

internal static class ProposalEffectText
{
    public static string Transition(EffectView effect, EffectChange change)
    {
        var field = effect.Field == SnapshotFields.LexSenseGloss ? MotifAppStrings.ProposalChangeGloss : effect.Field;
        var label = string.IsNullOrWhiteSpace(change.Ws) ? field : string.Format(CultureInfo.CurrentCulture,
            MotifAppStrings.ProposalChangeFieldWithWritingSystem, field, change.Ws);
        return string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalChangeTransition, label,
            change.Before ?? MotifAppStrings.ProposalChangeEmpty, change.After ?? MotifAppStrings.ProposalChangeEmpty);
    }

    public static string Shared(EffectView effect) => string.Join(Environment.NewLine,
        effect.Changes.Select(change => Transition(effect, change)).Prepend(string.Format(CultureInfo.CurrentCulture,
            MotifAppStrings.ProposalSharedEffect, effect.CanonicalId)));
}

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

    /// <summary>What a Dry Run found, as one sentence: how many fields it will change and the first few changes.</summary>
    public static string WillChange(IReadOnlyList<EffectView> effects)
    {
        var transitions = effects.SelectMany(effect => effect.Changes.Select(change => Transition(effect, change))).ToArray();
        if (transitions.Length == 0) return MotifAppStrings.ProposalDryRunWillChangeNothing;
        var shown = string.Join("; ", transitions.Take(ShownChanges));
        if (transitions.Length > ShownChanges)
            shown += string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalDryRunWillChangeMore,
                transitions.Length - ShownChanges);
        return string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalDryRunWillChangeFormat,
            PluralText.Format(MotifAppStrings.ProposalDryRunFieldCountPattern,
                effects.Where(effect => effect.Changes.Count > 0).Select(effect => (effect.CanonicalId, effect.Field)).Distinct().Count()),
            shown);
    }

    private const int ShownChanges = 3;

    public static string Shared(EffectView effect) => string.Join(Environment.NewLine,
        effect.Changes.Select(change => Transition(effect, change)).Prepend(string.Format(CultureInfo.CurrentCulture,
            MotifAppStrings.ProposalSharedEffect, effect.CanonicalId)));
}

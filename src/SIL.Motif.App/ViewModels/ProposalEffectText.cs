using System.Globalization;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Model.Snapshot;

namespace SIL.Motif.App.ViewModels;

internal static class ProposalEffectText
{
    /// <summary>The change with the sense it changes named first, as "palit sense 2: Gloss (en): buy → sell".</summary>
    public static string Transition(EffectView effect, EffectChange change, ProposalEntryNames names) =>
        names.Name(effect.CanonicalId) is { } name
            ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalEntryNamedTransition, name,
                Transition(effect, change))
            : Transition(effect, change);

    public static string Transition(EffectView effect, EffectChange change) =>
        string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalChangeTransition,
            FieldLabel(effect.Field, change.Ws),
            change.Before ?? MotifAppStrings.ProposalChangeEmpty, change.After ?? MotifAppStrings.ProposalChangeEmpty);

    /// <summary>The field's name as the window says it, with its writing system when it has one.</summary>
    public static string FieldLabel(string canonicalField, string? ws)
    {
        var field = canonicalField == SnapshotFields.LexSenseGloss ? MotifAppStrings.ProposalChangeGloss : canonicalField;
        return string.IsNullOrWhiteSpace(ws) ? field : string.Format(CultureInfo.CurrentCulture,
            MotifAppStrings.ProposalChangeFieldWithWritingSystem, field, ws);
    }

    /// <summary>What a Dry Run found, as one sentence: how many fields it will change and the first few changes.</summary>
    public static string WillChange(IReadOnlyList<EffectView> effects, ProposalEntryNames? names = null)
    {
        names ??= ProposalEntryNames.None;
        var transitions = effects.SelectMany(effect => effect.Changes.Select(change => Transition(effect, change, names)))
            .ToArray();
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

    public static string Shared(EffectView effect, ProposalEntryNames? names = null) => string.Join(Environment.NewLine,
        effect.Changes.Select(change => Transition(effect, change)).Prepend(string.Format(CultureInfo.CurrentCulture,
            MotifAppStrings.ProposalSharedEffect, (names ?? ProposalEntryNames.None).Name(effect.CanonicalId) ?? effect.CanonicalId)));
}

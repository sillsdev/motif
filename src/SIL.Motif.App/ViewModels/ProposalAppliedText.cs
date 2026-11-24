using System.Globalization;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>What an Applied Proposal's frozen record says, in the window's words.</summary>
internal static class ProposalAppliedText
{
    public static string Heading(AppliedRecordView record)
    {
        var time = ProposalDocumentViewModel.LocalRecordedTime(record.AppliedUtc);
        return string.Format(CultureInfo.CurrentCulture, record.Mode == SIL.Motif.Contract.Requests.ApplyModes.Person
            ? MotifAppStrings.ProposalAppliedByYou : MotifAppStrings.ProposalAppliedByOther, time, record.User);
    }

    /// <summary>One line for each field the Apply changed, the sense named when the Baseline holds it.</summary>
    public static IReadOnlyList<string> Effects(AppliedRecordView record, ProposalEntryNames names) =>
        [.. record.Effects.SelectMany(effect => Alternatives(effect).Select(ws =>
        {
            var transition = string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalChangeTransition,
                ProposalEffectText.FieldLabel(effect.Field, ws),
                Held(effect.Before, ws) ?? MotifAppStrings.ProposalChangeEmpty,
                Held(effect.After, ws) ?? MotifAppStrings.ProposalChangeEmpty);
            return string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalEntryNamedTransition,
                names.Name(effect.Target) ?? effect.Target, transition);
        }))];

    /// <summary>One line for each Difference saved before the Apply, as its counts.</summary>
    public static IReadOnlyList<string> Evidence(AppliedRecordView record) =>
        [.. record.Evidence.Select(evidence => string.Format(CultureInfo.CurrentCulture,
            MotifAppStrings.ProposalAppliedEvidenceFormat, evidence.TextTitle, Count(evidence.Words),
            Count(evidence.Occurrences), Count(evidence.ImprovedWords), Count(evidence.CouldBeBadWords)))];

    private static string Count(int value) => value.ToString("N0", CultureInfo.CurrentCulture);

    // Every writing system either side held, so a gloss added or removed shows as a change from or to empty.
    private static IEnumerable<string> Alternatives(AppliedEffectView effect) =>
        effect.Before.Keys.Concat(effect.After.Keys).Distinct(StringComparer.Ordinal)
            .Where(ws => Held(effect.Before, ws) != Held(effect.After, ws));

    private static string? Held(IReadOnlyDictionary<string, string> value, string ws) =>
        value.TryGetValue(ws, out var text) && text.Length > 0 ? text : null;
}

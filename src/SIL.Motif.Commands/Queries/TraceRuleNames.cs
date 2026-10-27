using System;
using System.Collections.Generic;
using System.Linq;
using SIL.LCModel;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Commands.Queries;

/// <summary>Names the rules a word trace mentions the way FieldWorks shows them, with the parser's id kept as a secondary detail.</summary>
public static class TraceRuleNames
{
    // An affix rule is known by its entry, as FieldWorks shows it: headword, then the sense gloss if any.
    public static string? NameOf(ICmObject rule)
    {
        var msa = rule as IMoMorphSynAnalysis;
        for (var owner = rule; owner is not null; owner = owner.Owner)
        {
            if (owner is ILexEntry entry)
            {
                var headword = entry.HeadWord?.Text;
                var gloss = (msa is null ? entry.SensesOS.FirstOrDefault()
                        : entry.AllSenses.FirstOrDefault(sense => sense.MorphoSyntaxAnalysisRA == msa))
                    ?.Gloss.BestAnalysisAlternative.Text;
                return gloss is { Length: > 0 } && gloss != "***" ? $"{headword} ‘{gloss}’" : headword;
            }
        }
        // A rule's ShortName is its class description ("A PhRegularRule"), not the name the project gives it.
        var named = rule switch
        {
            IPhSegmentRule segment => segment.Name.BestAnalysisAlternative?.Text,
            IMoCompoundRule compound => compound.Name.BestAnalysisAlternative?.Text,
            _ => rule.ShortName,
        };
        return named is { Length: > 0 } name && name != "***" ? name : null;
    }

    /// <summary>The project object ids behind a trace's authored refs, for a caller that holds the project open.</summary>
    public static IReadOnlyList<Guid> AuthoredIdentities(WordTraceResponse response) =>
        response.Reading.Refs.Where(reference => reference.IdentityQuality == "authored")
            .Select(reference => Guid.TryParse(reference.Identity, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty).Distinct().ToArray();

    /// <summary>
    /// Returns the response with each authored ref named and the no-parse reasons rewritten as
    /// "name (parser id) ...", so a rule reads as FieldWorks shows it. Refs the project cannot name keep the parser's label.
    /// </summary>
    public static WordTraceResponse Apply(WordTraceResponse response, Func<Guid, string?> nameOf)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(nameOf);
        if (response.Parsed || response.InvalidShape) return response;
        var refs = response.Reading.Refs.Select(reference =>
            reference.IdentityQuality == "authored" && Guid.TryParse(reference.Identity, out var id) && nameOf(id) is { Length: > 0 } name
                ? reference with { CapturedFieldWorksLabel = name } : reference).ToArray();
        var reading = response.Reading with { Refs = refs };
        if (response.Reading.NoParseReasons.Count > 0 && response.Reading.Attempts.Count == 0)
            reading = reading with { NoParseReasons = TraceReadingBuilder.RecordedReasonsFor(reading.Root, new TraceDisplayLabels(refs, showSource: true)) };
        return response with { Reading = reading };
    }
}

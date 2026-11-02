using System;
using System.Collections.Generic;
using System.Linq;
using SIL.LCModel;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Projection;
using SIL.Motif.Projection.Grammar;

namespace SIL.Motif.Commands.Queries;

/// <summary>
/// Warns about each phoneme spelling that two or more other phonemes' spellings add up to, as ng beside n and g.
/// HermitCrab reads a word's letters as the longest spelling at each point and keeps one reading, so the letters
/// n and g are always read as ng and the phoneme sequence n then g is never found.
/// </summary>
internal static class PhonemeSpellingWarnings
{
    internal static GrammarCheckResponse AddTo(GrammarCheckResponse check, LcmCache cache)
    {
        var phonemes = cache.LangProject.PhonologicalDataOA?.PhonemeSetsOS.SelectMany(set => set.PhonemesOC)
            .Select(phoneme => (Phoneme: phoneme, Spellings: Spellings(phoneme))).ToArray() ?? [];
        var warnings = new List<GrammarWarning>();
        foreach (var (phoneme, spellings) in phonemes)
        {
            var others = phonemes.Where(item => item.Phoneme != phoneme).SelectMany(item => item.Spellings)
                .Distinct(StringComparer.Ordinal).ToArray();
            foreach (var spelling in spellings)
            {
                var parts = Split(spelling, others);
                if (parts is not null) warnings.Add(Warning(cache, phoneme, spelling, parts));
            }
        }
        if (warnings.Count == 0) return check;

        var ordered = warnings.OrderBy(warning => warning.Description, StringComparer.Ordinal).ToArray();
        var summary = check.Summary.ToList();
        summary.Add(new GrammarWarningSummary(GrammarFindingCodes.PhonemeSpellingAmbiguous,
            "Phoneme spelling made of other phonemes", GrammarDiagnosticLevel.Warning, ordered.Length));
        return check with { Findings = check.Findings.Concat(ordered).ToArray(), Summary = summary };
    }

    private const string DottedCircle = "\u25CC";

    private static string[] Spellings(IPhPhoneme phoneme) => phoneme.CodesOS
        .SelectMany(code => code.Representation.AvailableWritingSystemIds
            .Select(ws => code.Representation.get_String(ws)?.Text).OfType<string>())
        .Select(text => text.Replace(DottedCircle, string.Empty))
        .Where(text => text.Length > 0).Distinct(StringComparer.Ordinal).ToArray();

    // The pieces of a spelling that is wholly a run of two or more other spellings, or null when it is not.
    internal static IReadOnlyList<string>? Split(string spelling, IReadOnlyCollection<string> others)
    {
        var best = new List<string>?[spelling.Length + 1];
        best[0] = [];
        for (var end = 1; end <= spelling.Length; end++)
            foreach (var piece in others)
            {
                var start = end - piece.Length;
                if (start < 0 || best[start] is null || best[end] is not null) continue;
                if (string.CompareOrdinal(spelling, start, piece, 0, piece.Length) != 0) continue;
                best[end] = [.. best[start]!, piece];
            }
        return best[spelling.Length] is { Count: >= 2 } found ? found : null;
    }

    private static GrammarWarning Warning(LcmCache cache, IPhPhoneme phoneme, string spelling, IReadOnlyList<string> parts)
    {
        var name = Name(cache, phoneme.Name, spelling);
        var joined = string.Join(" + ", parts.Select(part => $"'{part}'"));
        var id = CanonicalId.FromGuid(phoneme.Guid).Value;
        var subject = new GrammarWarningPart($"Phoneme {name}", GrammarWarningPartRole.Object, id, phoneme.ClassName)
        {
            Title = name,
            Subtitle = $"Spelled '{spelling}'",
            SubjectGuid = phoneme.Guid.ToString("D"),
            Status = GrammarSubjectStatus.Object,
        };
        var description = $"Phoneme '{name}' is spelled '{spelling}', which is also {joined}, each the spelling of another phoneme. " +
            "If that is intended, as with 'aa' beside 'a', nothing needs changing.";
        return new GrammarWarning(GrammarDiagnosticLevel.Warning, "Phoneme spelling made of other phonemes",
            [subject], [], description)
        {
            Code = GrammarFindingCodes.PhonemeSpellingAmbiguous,
            Group = "Sound inventory",
            Title = "Phoneme spelling made of other phonemes",
            Description = description,
            Explanation = "The parser reads a word's letters as the longest spelling at each point and keeps one reading, " +
                $"so the letters '{spelling}' are always read as the one phoneme, and a word where {joined} meet " +
                "(across a morpheme boundary, say) is never read as those phonemes.",
            Guidance = "Where the letters meet across a morpheme boundary, write that form of the affix or stem as an " +
                $"allomorph spelled with '{spelling}'. The only other fix is an orthography that removes the ambiguity " +
                "(Black, section 6.3.3). Removing the phoneme would break every rule that uses it.",
            Origin = GrammarFindingOrigin.Check,
            Scope = GrammarSubjectStatus.Object,
        };
    }

    private static string Name(LcmCache cache, IMultiAccessorBase? name, string fallback)
    {
        if (name is null) return fallback;
        var text = WritingSystemTextReader.BestAnalysis(cache, name).Text;
        if (text.Length == 0) text = WritingSystemTextReader.First(cache, name).Text;
        return text.Length == 0 ? fallback : text;
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using SIL.LCModel;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Projection;

namespace SIL.Motif.Commands.Queries;

/// <summary>
/// Reports lexicon entries that look like data errors and let wrong words parse: a Noun, Verb or Adjective stem that is
/// one letter or whose gloss says it is a cross-reference stub, a letter or a function word; entries repeated with the
/// same form and gloss; and an affix form whose hyphen sits on the wrong side. It only reports. Nothing is changed.
/// </summary>
internal static class EntryHygieneWarnings
{
    private const int ListedLimit = 20;
    private const int SampleSize = 5;

    private static readonly HashSet<string> ContentCategories = new(StringComparer.OrdinalIgnoreCase)
        { "noun", "verb", "adjective", "n", "v", "adj" };

    private static readonly Regex FunctionWordGloss = new(
        @"\b(interjection|conjunction|preposition|determiner|linker|pause word)\b|^\s*(\S+\s+){0,2}(marker|particle)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex LetterGloss = new(@"^\s*(the\s+)?(letter|alphabet)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex StubGloss = new(@"^\s*(=|see\s*:|see\s+also\b|see\s+under\b)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static GrammarCheckResponse AddTo(GrammarCheckResponse check, LcmCache cache)
    {
        var entries = cache.ServiceLocator.GetInstance<ILexEntryRepository>().AllInstances()
            .ToArray();
        check = Add(check, GrammarFindingCodes.StemTypedAsContentWord, "Stem typed as a content word",
            StemFindings(cache, entries));
        check = Add(check, GrammarFindingCodes.DuplicateEntry, "Duplicate entry", DuplicateFindings(cache, entries));
        return Add(check, GrammarFindingCodes.AffixHyphenOnWrongSide, "Affix hyphen on the wrong side",
            HyphenFindings(cache, entries));
    }

    private static GrammarCheckResponse Add(GrammarCheckResponse check, string code, string label, List<Item> items)
    {
        if (items.Count == 0) return check;
        var findings = items.Count > ListedLimit
            ? new[] { Build(code, label, items.Take(SampleSize).ToList(), items.Count) }
            : items.Select(item => Build(code, label, [item], 1)).ToArray();
        var summary = check.Summary.ToList();
        summary.Add(new GrammarWarningSummary(code, label, GrammarDiagnosticLevel.Warning, items.Count));
        return check with { Findings = check.Findings.Concat(findings).ToArray(), Summary = summary };
    }

    private sealed record Item(string Sort, string Text, IReadOnlyList<(ICmObject Object, string Name)> Subjects);

    private static List<Item> StemFindings(LcmCache cache, IEnumerable<ILexEntry> entries)
    {
        var items = new List<Item>();
        foreach (var entry in entries)
        {
            if (entry.LexemeFormOA is not IMoStemAllomorph) continue;
            var typed = entry.MorphoSyntaxAnalysesOC.OfType<IMoStemMsa>().Any(msa => IsContentCategory(cache, msa.PartOfSpeechRA));
            if (!typed) continue;
            var name = Name(cache, entry.LexemeFormOA.Form, "unnamed entry");
            var glosses = entry.SensesOS.Select(sense => Name(cache, sense.Gloss, "")).Where(g => g.Length > 0).ToArray();
            var reason = Reason(name, glosses);
            if (reason is null) continue;
            items.Add(new Item(name, $"Stem '{name}' is typed as a content word but {reason}.", [(entry, name)]));
        }
        return items.OrderBy(item => item.Sort, StringComparer.Ordinal).ToList();
    }

    private static string? Reason(string form, string[] glosses)
    {
        if (glosses.FirstOrDefault(StubGloss.IsMatch) is { } stub)
            return $"its gloss '{stub}' marks it as a cross-reference stub";
        if (glosses.FirstOrDefault(LetterGloss.IsMatch) is { } letter)
            return $"its gloss '{letter}' marks it as a letter";
        if (glosses.FirstOrDefault(FunctionWordGloss.IsMatch) is { } function)
            return $"its gloss '{function}' names a function word";
        var elements = new StringInfo(form.Normalize()).LengthInTextElements;
        return elements == 1 && form.Any(char.IsLetter) ? "its form is a single letter" : null;
    }

    private static bool IsContentCategory(LcmCache cache, IPartOfSpeech? category)
    {
        for (var level = 0; category is not null && level < 20; level++)
        {
            if (ContentCategories.Contains(category.CatalogSourceId ?? "") ||
                ContentCategories.Contains(Name(cache, category.Name, "")) ||
                ContentCategories.Contains(Name(cache, category.Abbreviation, "")))
                return true;
            category = category.Owner as IPartOfSpeech;
        }
        return false;
    }

    private static List<Item> DuplicateFindings(LcmCache cache, IEnumerable<ILexEntry> entries) => entries
        .Select(entry => (entry, form: Name(cache, entry.LexemeFormOA?.Form, ""), kind: Kind(entry),
            gloss: entry.SensesOS.Select(sense => Name(cache, sense.Gloss, "")).FirstOrDefault(g => g.Length > 0) ?? ""))
        .Where(item => item.form.Length > 0 && item.gloss.Length > 0)
        .GroupBy(item => (Form: item.form.Normalize(), Gloss: item.gloss.Normalize().Trim(), item.kind))
        .Where(group => group.Count() > 1)
        .Select(group => new Item(group.Key.Form + "\u0001" + group.Key.Gloss,
            $"{group.Count()} entries have the form '{group.Key.Form}' and the gloss '{group.Key.Gloss}'.",
            group.Take(SampleSize).Select(item => ((ICmObject)item.entry, item.form)).ToArray()))
        .OrderBy(item => item.Sort, StringComparer.Ordinal).ToList();

    /// <summary>The morph type and category an entry is filed under; entries repeat only when both match.</summary>
    private static string Kind(ILexEntry entry)
    {
        var category = entry.MorphoSyntaxAnalysesOC.Select(msa => msa switch
        {
            IMoStemMsa stem => stem.PartOfSpeechRA,
            IMoInflAffMsa infl => infl.PartOfSpeechRA,
            IMoDerivAffMsa deriv => deriv.FromPartOfSpeechRA,
            IMoUnclassifiedAffixMsa other => other.PartOfSpeechRA,
            _ => null,
        }).FirstOrDefault(part => part is not null);
        return $"{entry.LexemeFormOA?.MorphTypeRA?.Guid}|{category?.Guid}";
    }

    private static List<Item> HyphenFindings(LcmCache cache, IEnumerable<ILexEntry> entries)
    {
        var items = new List<Item>();
        foreach (var entry in entries)
            foreach (var form in entry.AllAllomorphs.OfType<IMoForm>())
            {
                var text = Name(cache, form.Form, "");
                var side = Side(form.MorphTypeRA?.Guid);
                var wrong = side switch
                {
                    "prefix" => text.StartsWith('-') && !text.EndsWith('-'),
                    "suffix" => text.EndsWith('-') && !text.StartsWith('-'),
                    _ => false,
                };
                if (!wrong) continue;
                var expected = side == "prefix" ? text.TrimStart('-') + "-" : "-" + text.TrimEnd('-');
                var entryName = Name(cache, entry.LexemeFormOA?.Form, text);
                items.Add(new Item(entryName + "\u0001" + text,
                    $"A {side} form of '{entryName}' is written '{text}' with the hyphen on the wrong side; a {side} is written '{expected}'.",
                    [(form, text)]));
            }
        return items.OrderBy(item => item.Sort, StringComparer.Ordinal).ToList();
    }

    private static string? Side(Guid? morphType) =>
        morphType is { } id && (id == MoMorphTypeTags.kguidMorphPrefix || id == MoMorphTypeTags.kguidMorphProclitic ||
            id == MoMorphTypeTags.kguidMorphPrefixingInterfix) ? "prefix"
        : morphType is { } other && (other == MoMorphTypeTags.kguidMorphSuffix || other == MoMorphTypeTags.kguidMorphEnclitic ||
            other == MoMorphTypeTags.kguidMorphSuffixingInterfix) ? "suffix"
        : null;

    private static GrammarWarning Build(string code, string label, IReadOnlyList<Item> items, int total)
    {
        var description = items.Count == 1 && total == 1
            ? items[0].Text
            : $"{total} findings of this kind, for example: {string.Join(" ", items.Select(item => item.Text))}";
        var parts = items.SelectMany(item => item.Subjects).Take(SampleSize)
            .Select(subject => new GrammarWarningPart($"Entry {subject.Name}", GrammarWarningPartRole.Object,
                CanonicalId.FromGuid(subject.Object.Guid).Value, subject.Object.ClassName)
            {
                Title = subject.Name,
                Subtitle = label,
                SubjectGuid = subject.Object.Guid.ToString("D"),
                Status = GrammarSubjectStatus.Object,
            }).ToArray();
        return new GrammarWarning(GrammarDiagnosticLevel.Warning, label, parts, [], description)
        {
            Code = code,
            Group = "Lexicon",
            Title = label,
            Description = description,
            Explanation = Explanation(code),
            Guidance = Guidance(code),
            Origin = GrammarFindingOrigin.Check,
            Scope = GrammarSubjectStatus.Object,
        };
    }

    private static string Explanation(string code) => code switch
    {
        GrammarFindingCodes.StemTypedAsContentWord =>
            "A stem typed Noun, Verb or Adjective takes the inflection and derivation of that category, so a letter, a " +
            "cross-reference stub or a function word typed that way lets short unrelated readings parse.",
        GrammarFindingCodes.DuplicateEntry =>
            "Entries with the same form and gloss parse the same words twice and double the readings of those words.",
        _ => "Motif and FieldWorks write an affix with its hyphen on the side that touches the stem: a prefix 'i-', a suffix '-i'. " +
            "A hyphen on the other side makes the form match no word.",
    };

    private static string Guidance(string code) => code switch
    {
        GrammarFindingCodes.StemTypedAsContentWord =>
            "Move it to the category it belongs to with compose-set-stem-part-of-speech, or delete the entry; " +
            "compose-batch stages many in one Proposal. This report changes nothing.",
        GrammarFindingCodes.DuplicateEntry =>
            "Delete one entry, or merge them in FieldWorks. This report changes nothing.",
        _ => "Rewrite the form with the hyphen on the stem side. This report changes nothing.",
    };

    private static string Name(LcmCache cache, IMultiAccessorBase? name, string fallback)
    {
        if (name is null) return fallback;
        var text = WritingSystemTextReader.BestAnalysis(cache, name).Text;
        if (text.Length == 0) text = WritingSystemTextReader.First(cache, name).Text;
        return text.Length == 0 ? fallback : text;
    }
}

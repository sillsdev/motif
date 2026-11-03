using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SIL.LCModel;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Projection;

namespace SIL.Motif.Commands.Queries;

/// <summary>
/// Reports each phoneme that occurs nowhere: no lexical form or wordform spells it, no rule, natural class,
/// environment or affix-process insertion names it. An archiphoneme is an ordinary phoneme, so one that no form
/// carries and no rule names does nothing; the finding is information, because a sound inventory authored ahead
/// of the lexicon is legitimate.
/// </summary>
internal static class PhonemeNeverUsedWarnings
{
    private const int ListedLimit = 20;
    private const int SampleSize = 5;
    private const string Label = "Phoneme that occurs nowhere";

    internal static GrammarCheckResponse AddTo(GrammarCheckResponse check, LcmCache cache)
    {
        var phonemes = cache.LangProject.PhonologicalDataOA?.PhonemeSetsOS.SelectMany(set => set.PhonemesOC).ToArray() ?? [];
        if (phonemes.Length == 0) return check;
        var named = NamedPhonemes(cache);
        var text = TextOfProject(cache);
        var unused = phonemes.Where(phoneme => !named.Contains(phoneme.Guid) && !AppearsIn(phoneme, text))
            .OrderBy(phoneme => Name(cache, phoneme), StringComparer.Ordinal).ToArray();
        if (unused.Length == 0) return check;

        var findings = unused.Length > ListedLimit
            ? new[] { Collapsed(cache, unused) }
            : unused.Select(phoneme => Build([Subject(cache, phoneme)],
                $"Phoneme '{Name(cache, phoneme)}' occurs in no form, wordform, rule, natural class or environment.")).ToArray();
        var summary = check.Summary.ToList();
        summary.Add(new GrammarWarningSummary(GrammarFindingCodes.PhonemeNeverUsed, Label,
            GrammarDiagnosticLevel.Information, unused.Length));
        return check with { Findings = check.Findings.Concat(findings).ToArray(), Summary = summary };
    }

    private static HashSet<Guid> NamedPhonemes(LcmCache cache)
    {
        var named = new HashSet<Guid>();
        var data = cache.LangProject.PhonologicalDataOA;
        foreach (var naturalClass in data.NaturalClassesOS.OfType<IPhNCSegments>())
            foreach (var segment in naturalClass.SegmentsRC) named.Add(segment.Guid);
        foreach (var rule in data.PhonRulesOS.OfType<IPhSegmentRule>())
            foreach (var context in RuleContexts(rule)) Collect(context, named);
        foreach (var insertion in cache.ServiceLocator.GetInstance<ICmObjectRepository>().AllInstances().OfType<IMoInsertPhones>())
            foreach (var phone in insertion.ContentRS) named.Add(phone.Guid);
        return named;
    }

    private static IEnumerable<IPhPhonContext> RuleContexts(IPhSegmentRule rule)
    {
        foreach (var context in rule.StrucDescOS) yield return context;
        if (rule is not IPhRegularRule regular) yield break;
        foreach (var rhs in regular.RightHandSidesOS)
        {
            foreach (var context in rhs.StrucChangeOS) yield return context;
            if (rhs.LeftContextOA is { } left) yield return left;
            if (rhs.RightContextOA is { } right) yield return right;
        }
    }

    private static void Collect(IPhPhonContext context, HashSet<Guid> named)
    {
        switch (context)
        {
            case IPhSimpleContextSeg { FeatureStructureRA: { } segment }:
                named.Add(segment.Guid);
                break;
            case IPhSequenceContext sequence:
                foreach (var member in sequence.MembersRS) Collect(member, named);
                break;
            case IPhIterationContext { MemberRA: { } member }:
                Collect(member, named);
                break;
        }
    }

    // One string of every form, wordform and environment, so each phoneme is one search.
    private static string TextOfProject(LcmCache cache)
    {
        var all = new StringBuilder();
        void Add(IMultiAccessorBase? accessor)
        {
            if (accessor is null) return;
            foreach (var ws in accessor.AvailableWritingSystemIds)
                if (accessor.get_String(ws)?.Text is { Length: > 0 } text) all.Append(text).Append('\n');
        }
        foreach (var entry in cache.ServiceLocator.GetInstance<ILexEntryRepository>().AllInstances())
            foreach (var form in entry.AllAllomorphs) Add(form.Form);
        foreach (var wordform in cache.ServiceLocator.GetInstance<IWfiWordformRepository>().AllInstances()) Add(wordform.Form);
        foreach (var environment in cache.LangProject.PhonologicalDataOA.EnvironmentsOS)
            if (environment.StringRepresentation?.Text is { Length: > 0 } pattern) all.Append(pattern).Append('\n');
        return all.ToString().Normalize(NormalizationForm.FormD);
    }

    private static bool AppearsIn(IPhPhoneme phoneme, string text) => Spellings(phoneme)
        .Any(spelling => text.Contains(spelling, StringComparison.Ordinal));

    private static IEnumerable<string> Spellings(IPhPhoneme phoneme) => phoneme.CodesOS
        .SelectMany(code => code.Representation.AvailableWritingSystemIds
            .Select(ws => code.Representation.get_String(ws)?.Text).OfType<string>())
        .Select(spelling => spelling.Replace("◌", string.Empty).Normalize(NormalizationForm.FormD))
        .Where(spelling => spelling.Length > 0);

    private static GrammarWarning Collapsed(LcmCache cache, IPhPhoneme[] phonemes)
    {
        var sample = phonemes.Take(SampleSize).ToArray();
        var names = string.Join(", ", sample.Select(phoneme => $"'{Name(cache, phoneme)}'"));
        return Build(sample.Select(phoneme => Subject(cache, phoneme)).ToArray(),
            $"{phonemes.Length} phonemes occur in no form, wordform, rule, natural class or environment, for example {names}.");
    }

    private static GrammarWarningPart Subject(LcmCache cache, IPhPhoneme phoneme)
    {
        var name = Name(cache, phoneme);
        return new($"Phoneme {name}", GrammarWarningPartRole.Object, CanonicalId.FromGuid(phoneme.Guid).Value, phoneme.ClassName)
        {
            Title = name,
            Subtitle = "Occurs nowhere",
            SubjectGuid = phoneme.Guid.ToString("D"),
            Status = GrammarSubjectStatus.Object,
        };
    }

    private static GrammarWarning Build(GrammarWarningPart[] subjects, string description) =>
        new(GrammarDiagnosticLevel.Information, Label, subjects, [], description)
        {
            Code = GrammarFindingCodes.PhonemeNeverUsed,
            Group = "Sound inventory",
            Title = Label,
            Description = description,
            Explanation = "No lexical form, wordform, rule, natural class or environment uses the phoneme, so the parser " +
                "never reads it. An archiphoneme, a phoneme that only an underlying form carries, is an ordinary phoneme " +
                "and is reported the same way until a form or rule uses it.",
            Guidance = "If the phoneme is an archiphoneme, write an underlying form with its letter and add the rule " +
                "that changes it. If the inventory was authored ahead of the lexicon, nothing needs changing. " +
                "Do not remove a phoneme a later rule will need.",
            Origin = GrammarFindingOrigin.Check,
            Scope = GrammarSubjectStatus.Object,
        };

    private static string Name(LcmCache cache, IPhPhoneme phoneme)
    {
        var text = WritingSystemTextReader.BestAnalysis(cache, phoneme.Name).Text;
        if (text.Length == 0) text = WritingSystemTextReader.First(cache, phoneme.Name).Text;
        return text.Length == 0 ? CanonicalId.FromGuid(phoneme.Guid).Value : text;
    }
}

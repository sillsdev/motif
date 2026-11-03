using System;
using System.Collections.Generic;
using System.Linq;
using SIL.LCModel;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Projection;

namespace SIL.Motif.Commands.Queries;

/// <summary>
/// Reports each entry marked do-not-parse. PanGloss leaves such an entry out of every parse, but neither FieldWorks'
/// HermitCrab loader nor its XAmple transform reads the flag, so FieldWorks still parses the entry after Apply and a
/// Trial reads the grammar differently from FieldWorks.
/// </summary>
internal static class DoNotParseWarnings
{
    private const int ListedLimit = 20;
    private const int SampleSize = 5;
    private const string Label = "Entry that FieldWorks still parses";

    internal static GrammarCheckResponse AddTo(GrammarCheckResponse check, LcmCache cache)
    {
        var entries = cache.ServiceLocator.GetInstance<ILexEntryRepository>().AllInstances()
            .Where(entry => entry.DoNotUseForParsing)
            .ToArray();
        if (entries.Length == 0) return check;

        var findings = entries.Length > ListedLimit
            ? new[] { Collapsed(cache, entries) }
            : entries.Select(entry => Finding(cache, entry))
                .OrderBy(finding => finding.Description, StringComparer.Ordinal)
                .ToArray();
        var summary = check.Summary.ToList();
        summary.Add(new GrammarWarningSummary(GrammarFindingCodes.DoNotParseIgnoredByFieldWorks,
            Label, GrammarDiagnosticLevel.Information, entries.Length));
        return check with { Findings = check.Findings.Concat(findings).ToArray(), Summary = summary };
    }

    private static GrammarWarning Collapsed(LcmCache cache, ILexEntry[] entries)
    {
        var sample = entries.Select(entry => (entry, name: Name(cache, entry.LexemeFormOA?.Form, "unnamed entry")))
            .OrderBy(item => item.name, StringComparer.Ordinal).Take(SampleSize).ToArray();
        var subjects = sample.Select(item => Subject(item.entry, item.name)).ToArray();
        var names = string.Join(", ", sample.Select(item => $"'{item.name}'"));
        var description = $"{entries.Length} entries are marked do-not-parse, for example {names}. " +
            "Motif's parser leaves them out, but FieldWorks still parses them after Apply.";
        return Build(subjects, description);
    }

    private static GrammarWarning Finding(LcmCache cache, ILexEntry entry)
    {
        var name = Name(cache, entry.LexemeFormOA?.Form, "unnamed entry");
        var homograph = entry.HomographNumber > 1 ? $" (homograph {entry.HomographNumber})" : "";
        var description = $"Entry '{name}'{homograph} is marked do-not-parse. Motif's parser leaves it out, but FieldWorks still parses it after Apply.";
        return Build([Subject(entry, name)], description);
    }

    private static GrammarWarningPart Subject(ILexEntry entry, string name) =>
        new($"Entry {name}", GrammarWarningPartRole.Object, CanonicalId.FromGuid(entry.Guid).Value, entry.ClassName)
        {
            Title = name,
            Subtitle = entry.HomographNumber > 1 ? $"Marked do-not-parse, homograph {entry.HomographNumber}" : "Marked do-not-parse",
            SubjectGuid = entry.Guid.ToString("D"),
            Status = GrammarSubjectStatus.Object,
        };

    private static GrammarWarning Build(GrammarWarningPart[] subjects, string description) =>
        new(GrammarDiagnosticLevel.Information, Label, subjects, [], description)
        {
            Code = GrammarFindingCodes.DoNotParseIgnoredByFieldWorks,
            Group = "Lexicon",
            Title = Label,
            Description = description,
            Explanation = "Motif's parser leaves a do-not-parse entry out, so a Trial never uses its forms or meanings. " +
                "FieldWorks' parsers do not read the flag, so after Apply they parse words with this entry, " +
                "and the Trial result differs from the parse in FieldWorks.",
            Guidance = "To exclude the entry in FieldWorks too, delete it or retire it, or disable the rule or template " +
                "that lets it parse. If the flag is only a note for people, leave it, and read a Trial knowing that " +
                "FieldWorks still parses the entry.",
            Origin = GrammarFindingOrigin.Check,
            Scope = GrammarSubjectStatus.Object,
        };

    private static string Name(LcmCache cache, IMultiAccessorBase? name, string fallback)
    {
        if (name is null) return fallback;
        var text = WritingSystemTextReader.BestAnalysis(cache, name).Text;
        if (text.Length == 0) text = WritingSystemTextReader.First(cache, name).Text;
        return text.Length == 0 ? fallback : text;
    }
}

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
/// Reports each entry marked do-not-parse. Neither FieldWorks' HermitCrab loader nor its XAmple transform reads the
/// flag, so FieldWorks parses the entry after Apply. The PanGloss Motif pins today (0.14.0) leaves it out, so a Trial
/// reads the grammar differently from FieldWorks until the next PanGloss release parses it too.
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
            "FieldWorks parses them after Apply; the pinned PanGloss leaves them out until its next release.";
        return Build(subjects, description);
    }

    private static GrammarWarning Finding(LcmCache cache, ILexEntry entry)
    {
        var name = Name(cache, entry.LexemeFormOA?.Form, "unnamed entry");
        var homograph = entry.HomographNumber > 1 ? $" (homograph {entry.HomographNumber})" : "";
        var description = $"Entry '{name}'{homograph} is marked do-not-parse. FieldWorks parses it after Apply; the pinned PanGloss leaves it out until its next release.";
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
            Explanation = "FieldWorks' parsers do not read the flag, so after Apply they parse words with this entry. " +
                "The PanGloss Motif pins today leaves the entry out, so a Trial differs from FieldWorks until the next " +
                "PanGloss release parses it too.",
            Guidance = "To stop the entry parsing, retype it with compose-set-stem-part-of-speech or delete it. " +
                "Flagging it does not stop FieldWorks parsing it.",
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

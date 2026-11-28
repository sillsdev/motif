using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Commands.Queries;

/// <summary>
/// Runs the compose advisories over every object of a grammar, read-only, with the codes compose-batch uses, and adds
/// the orphan-environment advisory. They advise and never refuse, so they never change the check's outcome.
/// </summary>
internal static class GrammarAdvisorySweep
{
    internal static IReadOnlyList<GrammarCheckAdvisory> Run(LcmCache cache)
    {
        var advisories = new List<GrammarCheckAdvisory>();
        var environments = cache.LangProject.PhonologicalDataOA?.EnvironmentsOS.ToArray() ?? [];
        foreach (var environment in environments.OrderBy(item => item.Guid))
        {
            var pattern = environment.StringRepresentation?.Text ?? string.Empty;
            var objects = new[] { new GrammarCheckAdvisoryObject(CanonicalId.FromGuid(environment.Guid).Value, pattern) };
            if (Intent(pattern) is { } intent && ProposalCommands.SpellsWholeWord(intent))
                advisories.Add(new GrammarCheckAdvisory(ComposeAdvisoryCodes.WholeWordEnvironment,
                    ProposalCommands.WholeWordEnvironmentMessage(pattern)) { Objects = objects });
            if (UnusedObjects.Referrers(environment).Count == 0)
                advisories.Add(new GrammarCheckAdvisory(GrammarCheckAdvisoryCodes.OrphanEnvironment,
                    $"Environment '{pattern}' is referenced by no allomorph. Delete it, or give it " +
                    "to the forms it was written for.") { Objects = objects });
        }
        foreach (var inflectionClass in cache.ServiceLocator.GetInstance<IMoInflClassRepository>().AllInstances()
                     .OrderBy(item => item.Guid))
        {
            if (ProposalCommands.StemCount(cache, inflectionClass) != 1) continue;
            var name = inflectionClass.Name.BestAnalysisAlternative.Text;
            advisories.Add(new GrammarCheckAdvisory(ComposeAdvisoryCodes.OneStemInflectionClass,
                ProposalCommands.OneStemInflectionClassMessage(name, "in this grammar"))
            {
                Objects = [new GrammarCheckAdvisoryObject(CanonicalId.FromGuid(inflectionClass.Guid).Value, name)],
            });
        }
        return advisories;
    }

    // "/ left _ right" as compose-batch's intent: # is a word boundary, letters are sounds.
    private static JsonElement? Intent(string pattern)
    {
        var text = pattern.Trim();
        if (!text.StartsWith('/')) return null;
        var sides = text[1..].Split('_');
        if (sides.Length != 2) return null;
        static List<Dictionary<string, string>> Items(string side) => side.Where(c => !char.IsWhiteSpace(c)).Select(c => c switch
        {
            '#' => new Dictionary<string, string> { ["boundary"] = "word" },
            _ when char.IsLetterOrDigit(c) || char.GetUnicodeCategory(c) is
                System.Globalization.UnicodeCategory.NonSpacingMark or System.Globalization.UnicodeCategory.SpacingCombiningMark
                or System.Globalization.UnicodeCategory.ModifierLetter or System.Globalization.UnicodeCategory.ModifierSymbol
                => new Dictionary<string, string> { ["phoneme"] = c.ToString() },
            _ => new Dictionary<string, string> { ["other"] = c.ToString() },
        }).ToList();
        return JsonSerializer.SerializeToElement(new { left = Items(sides[0]), right = Items(sides[1]) });
    }
}

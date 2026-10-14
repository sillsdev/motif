using SIL.LCModel;

namespace SIL.Motif.Host.Texts;

/// <summary>Counts lexical entries, stem lexemes, and grammar rules in one saved project model.</summary>
/// <param name="LexemeCount">The number of lexical entries.</param>
/// <param name="StemLexemeCount">The number of entries whose lexeme form is a stem allomorph.</param>
/// <param name="RuleCount">The number of affix, compound, and phonological rules.</param>
public sealed record ProjectGrammarInventory(int LexemeCount, int StemLexemeCount, int RuleCount);

/// <summary>Reads the grammar inventory shared by project summaries and grammar checks.</summary>
public static class ProjectGrammarInventoryReader
{
    /// <summary>Counts lexical entries, stem lexemes, and grammar rules in the supplied cache.</summary>
    public static ProjectGrammarInventory Read(LcmCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        var entryRepository = cache.ServiceLocator.GetInstance<ILexEntryRepository>();
        var entries = entryRepository.AllInstances().ToArray();
        var affixRules = entries
            .SelectMany(entry => entry.AlternateFormsOS.OfType<IMoAffixAllomorph>()
                .Concat(entry.LexemeFormOA is IMoAffixAllomorph allomorph ? [allomorph] : []))
            .Select(form => form.Guid).Distinct().Count();
        var ruleCount = affixRules + cache.LangProject.MorphologicalDataOA.CompoundRulesOS.Count +
            cache.LangProject.PhonologicalDataOA.PhonRulesOS.Count;
        return new ProjectGrammarInventory(entryRepository.Count,
            entries.Count(entry => entry.LexemeFormOA is IMoStemAllomorph), ruleCount);
    }
}

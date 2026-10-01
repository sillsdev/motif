using System.Text.RegularExpressions;
using SIL.LCModel;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Projection.Grammar;

/// <summary>
/// Reads, from a FieldWorks project, what a grammar finding's subject leads to: the allomorphs and grammatical infos
/// whose stored uses count, the rule keys whose stored per-word times count, or the letters a word is spelled with.
/// Every link followed is a FieldWorks reference, except a natural class's place in an environment, which FieldWorks
/// itself writes as the class's abbreviation in brackets. Reads only; the project is never changed.
/// </summary>
public static class WarningReachReader
{
    /// <summary>
    /// What <paramref name="part"/> reaches, or <see langword="null"/> for a part that names no object or letter.
    /// <paramref name="cache"/> is opened only for a subject that must be followed in the project.
    /// </summary>
    public static WarningReach? Reach(GrammarWarningPart part, Func<LcmCache> cache)
    {
        ArgumentNullException.ThrowIfNull(part);
        ArgumentNullException.ThrowIfNull(cache);
        var kind = part.FieldWorksKind ?? string.Empty;
        // A letter PanGloss names without a GUID is one not yet listed as a phoneme; its title is the letter.
        if (part.ObjectId is null && kind != "PhPhoneme") return null;
        Guid.TryParse(part.SubjectGuid, out var guid);
        var id = guid == Guid.Empty ? null : guid.ToString("D");

        if (kind == "PhPhoneme") return Spelling(part, id is null ? null : cache());
        if (id is null) return CantTell(WarningCantTell.NothingNamed);
        return kind switch
        {
            "MoForm" or "MoStemAllomorph" or "MoAffixAllomorph" or "MoAffixProcess" =>
                new WarningReach(WarningWordsPath.Uses) { AllomorphIds = [id] },
            "MoStemMsa" or "MoInflAffMsa" or "MoDerivAffMsa" or "MoUnclassifiedAffixMsa" =>
                new WarningReach(WarningWordsPath.Uses) { GrammaticalInfoIds = [id] },
            "PhRegularRule" or "PhMetathesisRule" =>
                new WarningReach(WarningWordsPath.RuleTimes) { TimingKeys = [new TraceTimingKey(PhonRule, id)] },
            "MoCompoundRule" or "MoEndoCompound" or "MoExoCompound" =>
                new WarningReach(WarningWordsPath.RuleTimes) { TimingKeys = [new TraceTimingKey(MorphRule, id)] },
            "LexEntry" => Follow<ILexEntry>(cache(), guid, entry => new WarningReach(WarningWordsPath.ThroughAllomorphs)
            {
                AllomorphIds = entry.AllAllomorphs.Select(Id).ToArray(),
            }),
            "LexSense" => Follow<ILexSense>(cache(), guid, sense =>
                new WarningReach(WarningWordsPath.ThroughGrammaticalInfo)
                {
                    GrammaticalInfoIds = sense.MorphoSyntaxAnalysisRA is { } msa ? [Id(msa)] : [],
                }),
            "PhEnvironment" => Follow<IPhEnvironment>(cache(), guid, environment =>
                new WarningReach(WarningWordsPath.ThroughAllomorphs)
                {
                    AllomorphIds = AllomorphsConditionedBy(environment.Cache, [environment]).Select(Id).ToArray(),
                }),
            "PhNaturalClass" => Follow<IPhNaturalClass>(cache(), guid, NaturalClass),
            _ => CantTell(WarningCantTell.KindNotFollowed),
        };
    }

    /// <summary>The kind PanGloss's statistics time a phonological rule under.</summary>
    public const string PhonRule = "phon_rule";

    /// <summary>The kind PanGloss's statistics time a morphological rule, such as a compound rule, under.</summary>
    public const string MorphRule = "morph_rule";

    private static WarningReach NaturalClass(IPhNaturalClass naturalClass)
    {
        var cache = naturalClass.Cache;
        var environments = new List<IPhEnvironment>();
        var allomorphs = new List<IMoForm>();
        var rules = new List<IPhSegmentRule>();
        var token = Abbreviation(naturalClass) is { } abbreviation
            ? new Regex(@"\[\s*" + Regex.Escape(abbreviation) + @"\s*\]", RegexOptions.CultureInvariant)
            : null;
        foreach (var environment in cache.LanguageProject.PhonologicalDataOA?.EnvironmentsOS ?? Enumerable.Empty<IPhEnvironment>())
            if (token is not null && token.IsMatch(environment.StringRepresentation?.Text ?? string.Empty))
                environments.Add(environment);
        foreach (var context in cache.ServiceLocator.GetInstance<IPhSimpleContextNCRepository>().AllInstances()
                     .Where(context => context.FeatureStructureRA == naturalClass))
        {
            switch (NearestOwner(context))
            {
                case IPhEnvironment environment: environments.Add(environment); break;
                case IPhSegmentRule rule: rules.Add(rule); break;
                case IMoForm allomorph: allomorphs.Add(allomorph); break;
            }
        }
        allomorphs.AddRange(AllomorphsConditionedBy(cache, environments.Distinct().ToArray()));
        return new WarningReach(WarningWordsPath.ThroughEnvironmentsAndRules)
        {
            AllomorphIds = allomorphs.Distinct().Select(Id).ToArray(),
            TimingKeys = rules.Distinct().Select(rule => new TraceTimingKey(PhonRule, Id(rule))).ToArray(),
        };
    }

    private static ICmObject? NearestOwner(ICmObject start)
    {
        for (var owner = start.Owner; owner is not null; owner = owner.Owner)
            if (owner is IPhEnvironment or IPhSegmentRule or IMoForm) return owner;
        return null;
    }

    // FieldWorks resolves a class in an environment string by this same abbreviation (PhEnvironment's recognizer).
    private static string? Abbreviation(IPhNaturalClass naturalClass)
    {
        var abbreviation = naturalClass.Abbreviation.AnalysisDefaultWritingSystem?.Text;
        if (string.IsNullOrEmpty(abbreviation)) abbreviation = naturalClass.Abbreviation.BestAnalysisVernacularAlternative?.Text;
        if (string.IsNullOrEmpty(abbreviation) || abbreviation == "***")
            abbreviation = naturalClass.Name.BestAnalysisVernacularAlternative?.Text;
        return string.IsNullOrEmpty(abbreviation) || abbreviation == "***" ? null : abbreviation;
    }

    private static IEnumerable<IMoForm> AllomorphsConditionedBy(LcmCache cache, IReadOnlyCollection<IPhEnvironment> environments)
    {
        if (environments.Count == 0) return [];
        return cache.ServiceLocator.GetInstance<IMoFormRepository>().AllInstances().Where(form => form switch
        {
            IMoStemAllomorph stem => stem.PhoneEnvRC.Any(environments.Contains),
            IMoAffixAllomorph affix => affix.PhoneEnvRC.Any(environments.Contains) ||
                affix.PositionRS.Any(environments.Contains),
            _ => false,
        });
    }

    private static WarningReach Spelling(GrammarWarningPart part, LcmCache? cache)
    {
        var codes = cache is not null && Guid.TryParse(part.SubjectGuid, out var guid) &&
            cache.ServiceLocator.ObjectRepository.TryGetObject(guid, out var found) && found is IPhPhoneme phoneme
            ? phoneme.CodesOS.Select(code => code.Representation.VernacularDefaultWritingSystem?.Text)
                .Where(text => !string.IsNullOrEmpty(text) && text != "***").Select(text => text!).ToArray()
            : [];
        var spellings = codes.Length > 0 ? codes : part.Title is { Length: > 0 } title ? [title] : [];
        return spellings.Length == 0
            ? CantTell(WarningCantTell.NothingNamed)
            : new WarningReach(WarningWordsPath.Spelling) { Spellings = spellings };
    }

    private static WarningReach Follow<T>(LcmCache cache, Guid guid, Func<T, WarningReach> follow) where T : class, ICmObject =>
        cache.ServiceLocator.ObjectRepository.TryGetObject(guid, out var found) && found is T typed
            ? follow(typed)
            : CantTell(WarningCantTell.NotInProject);

    private static WarningReach CantTell(WarningCantTell reason) =>
        new(WarningWordsPath.CantTell) { CantTell = reason };

    private static string Id(ICmObject item) => item.Guid.ToString("D");
}

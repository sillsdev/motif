using System.Text.RegularExpressions;
using SIL.LCModel;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Projection.Grammar;

/// <summary>
/// Reads, from a FieldWorks project, what a grammar finding's subject leads to: the allomorphs and grammatical infos
/// whose stored uses count, the rule keys whose stored per-word times count, or the letters a word is spelled with.
/// Exact routes follow FieldWorks references. Natural-class names in environment notation do not establish
/// which object was selected; that unresolved reach is kept separately. Reads only; the project is never changed.
/// </summary>
public static partial class WarningReachReader
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
        if (part.ObjectId is null && kind.Length == 0) return null;
        if (!Guid.TryParse(part.SubjectGuid, out var guid) || guid == Guid.Empty)
            return kind == "PhPhoneme" ? Spelling(part, null) :
                Unavailable(WarningWordsPath.UnresolvedIdentity, WarningAttributionReason.NamedWithoutProjectGuid);
        var project = cache();
        if (!project.ServiceLocator.ObjectRepository.TryGetObject(guid, out var found))
            return Unavailable(WarningWordsPath.MissingObject, WarningAttributionReason.StaleGuid);
        if (!IsKind(found, kind))
            return Unavailable(WarningWordsPath.MissingObject, WarningAttributionReason.WrongClass);
        var id = guid.ToString("D");
        if (kind == "PhPhoneme") return Spelling(part, project);
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
            _ => ReverseReferences(found),
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
        var labels = naturalClass.Abbreviation.AvailableWritingSystemIds.Select(ws => naturalClass.Abbreviation.get_String(ws)?.Text)
            .Concat(naturalClass.Name.AvailableWritingSystemIds.Select(ws => naturalClass.Name.get_String(ws)?.Text)).Where(text => !string.IsNullOrEmpty(text) && text != "***")
            .Distinct(StringComparer.Ordinal).ToArray();
        var unresolvedNotation = (cache.LanguageProject.PhonologicalDataOA?.EnvironmentsOS ?? Enumerable.Empty<IPhEnvironment>())
            .Any(environment => labels.Any(label => Regex.IsMatch(environment.StringRepresentation?.Text ?? string.Empty,
                @"\[\s*" + Regex.Escape(label!) + @"\s*\]", RegexOptions.CultureInvariant)));
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
        return new WarningReach(allomorphs.Count == 0 && rules.Count == 0 && unresolvedNotation
            ? WarningWordsPath.UnresolvedIdentity : WarningWordsPath.ThroughEnvironmentsAndRules)
        {
            Reason = allomorphs.Count == 0 && rules.Count == 0 && unresolvedNotation
                ? WarningAttributionReason.UnresolvedEnvironmentNotation : null,
            AttributionLimits = unresolvedNotation ? [WarningAttributionReason.UnresolvedEnvironmentNotation] : [],
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
        if (spellings.Length > 0) return new WarningReach(WarningWordsPath.Spelling) { Spellings = spellings };
        return cache is null
            ? Unavailable(WarningWordsPath.UnresolvedIdentity, WarningAttributionReason.NamedWithoutProjectGuid)
            : Unavailable(WarningWordsPath.ProjectWide, WarningAttributionReason.NoWordAttribution);
    }

    private static WarningReach Follow<T>(LcmCache cache, Guid guid, Func<T, WarningReach> follow) where T : class, ICmObject =>
        cache.ServiceLocator.ObjectRepository.TryGetObject(guid, out var found) && found is T typed
            ? follow(typed)
            : Unavailable(WarningWordsPath.MissingObject, WarningAttributionReason.StaleGuid);

    private static bool IsKind(ICmObject found, string kind) => found.ClassName == kind || kind switch
    {
        "MoForm" => found is IMoForm,
        "MoMorphSynAnalysis" => found is IMoMorphSynAnalysis,
        "MoCompoundRule" => found is IMoCompoundRule,
        "PhSegmentRule" => found is IPhSegmentRule,
        "PhNaturalClass" => found is IPhNaturalClass,
        "FsFeatDefn" => found is IFsFeatDefn,
        "FsFeatureSpecification" => found is IFsFeatureSpecification,
        "FsAbstractStructure" => found is IFsAbstractStructure,
        "MoAdhocProhib" => found is IMoAdhocProhib,
        _ => false,
    };

    private static WarningReach Unavailable(WarningWordsPath path, WarningAttributionReason reason) =>
        new(path) { Reason = reason };

    private static string Id(ICmObject item) => item.Guid.ToString("D");
}

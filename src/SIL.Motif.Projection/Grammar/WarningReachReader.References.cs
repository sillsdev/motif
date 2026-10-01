using SIL.LCModel;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Projection.Grammar;

public static partial class WarningReachReader
{
    private static WarningReach ReverseReferences(ICmObject item) => item switch
    {
        IMoInflAffixTemplate template => Slots(template.Cache,
            template.PrefixSlotsRS.Concat(template.SuffixSlotsRS).Concat(template.SlotsRS)
                .Concat(template.ProcliticSlotsRS).Concat(template.EncliticSlotsRS).Distinct().ToArray()),
        IMoInflAffixSlot slot => Slots(slot.Cache, [slot]),
        IFsFeatDefn or IFsSymFeatVal or IFsFeatureSpecification or IFsAbstractStructure => Features(item),
        IMoStemName stemName => StemName(stemName),
        IMoInflClass inflectionClass => InflectionClass(inflectionClass),
        ILexEntryInflType type => EntryType(type),
        IMoAlloAdhocProhib prohibition => new(WarningWordsPath.Membership)
        {
            AllomorphIds = prohibition.AllomorphsRS.Concat(prohibition.RestOfAllosRS)
                .Concat(prohibition.FirstAllomorphRA is { } first ? [first] : []).Distinct().Select(Id).ToArray(),
        },
        IMoMorphAdhocProhib prohibition => new(WarningWordsPath.Membership)
        {
            GrammaticalInfoIds = prohibition.MorphemesRS.Concat(prohibition.RestOfMorphsRS)
                .Concat(prohibition.FirstMorphemeRA is { } first ? [first] : []).Distinct().Select(Id).ToArray(),
        },
        IPhBdryMarker boundary => Boundary(boundary),
        IPhPhonemeSet => Unavailable(WarningWordsPath.ProjectWide, WarningAttributionReason.NoWordAttribution),
        IFsFeatureSystem => Unavailable(WarningWordsPath.ProjectWide, WarningAttributionReason.NoWordAttribution),
        _ => Unavailable(WarningWordsPath.UnresolvedIdentity, WarningAttributionReason.UnsupportedKind),
    };

    private static WarningReach Slots(LcmCache cache, IReadOnlyCollection<IMoInflAffixSlot> slots)
    {
        var msas = cache.ServiceLocator.GetInstance<IMoInflAffMsaRepository>().AllInstances()
            .Where(msa => msa.SlotsRC.Any(slots.Contains)).ToArray();
        return new(WarningWordsPath.Membership)
        {
            GrammaticalInfoIds = msas.Select(Id).ToArray(),
            AllomorphIds = msas.Select(msa => msa.Owner).OfType<ILexEntry>()
                .SelectMany(entry => entry.AllAllomorphs).Distinct().Select(Id).ToArray(),
        };
    }

    private static WarningReach StemName(IMoStemName name) => new(WarningWordsPath.ThroughAllomorphs)
    {
        AllomorphIds = name.Cache.ServiceLocator.GetInstance<IMoStemAllomorphRepository>().AllInstances()
            .Where(form => form.StemNameRA == name).Select(Id).ToArray(),
        GrammaticalInfoIds = name.Cache.ServiceLocator.GetInstance<IMoDerivAffMsaRepository>().AllInstances()
            .Where(msa => msa.FromStemNameRA == name).Select(Id).ToArray(),
    };

    private static WarningReach InflectionClass(IMoInflClass cls)
    {
        var services = cls.Cache.ServiceLocator;
        return new(WarningWordsPath.ThroughGrammaticalInfo)
        {
            GrammaticalInfoIds = services.GetInstance<IMoStemMsaRepository>().AllInstances()
                .Where(msa => msa.InflectionClassRA == cls).Cast<IMoMorphSynAnalysis>()
                .Concat(services.GetInstance<IMoDerivAffMsaRepository>().AllInstances()
                    .Where(msa => msa.FromInflectionClassRA == cls || msa.ToInflectionClassRA == cls))
                .Select(Id).ToArray(),
            AllomorphIds = services.GetInstance<IMoAffixFormRepository>().AllInstances()
                .Where(form => form.InflectionClassesRC.Contains(cls)).Select(Id).ToArray(),
        };
    }

    private static WarningReach EntryType(ILexEntryInflType type) => new(WarningWordsPath.Membership)
    {
        AllomorphIds = type.Cache.ServiceLocator.GetInstance<ILexEntryRefRepository>().AllInstances()
            .Where(reference => reference.VariantEntryTypesRS.Contains(type)).Select(reference => reference.Owner)
            .OfType<ILexEntry>().SelectMany(entry => entry.AllAllomorphs).Distinct().Select(Id).ToArray(),
    };

    private static WarningReach Boundary(IPhBdryMarker boundary)
    {
        var owners = boundary.Cache.ServiceLocator.GetInstance<IPhSimpleContextBdryRepository>().AllInstances()
            .Where(context => context.FeatureStructureRA == boundary).Select(NearestOwner).OfType<ICmObject>().ToArray();
        return owners.Length == 0
            ? Unavailable(WarningWordsPath.ProjectWide, WarningAttributionReason.NoWordAttribution)
            : Merge(WarningWordsPath.ThroughEnvironmentsAndRules, owners.Select(OwnerReach));
    }

    private static WarningReach Features(ICmObject subject)
    {
        var services = subject.Cache.ServiceLocator;
        var specifications = services.GetInstance<IFsFeatureSpecificationRepository>().AllInstances().ToArray();
        var matched = specifications.Where(spec => spec == subject || spec.FeatureRA == subject || spec switch
        {
            IFsClosedValue value => value.ValueRA == subject,
            IFsNegatedValue value => value.ValueRA == subject,
            IFsDisjunctiveValue value => value.ValueRC.Any(item => item == subject),
            _ => false,
        }).ToHashSet();
        bool added;
        do
        {
            added = false;
            foreach (var shared in specifications.OfType<IFsSharedValue>())
                if (shared.ValueRA is { } target && matched.Contains(target)) added |= matched.Add(shared);
        } while (added);
        var owners = matched.Select(FeatureOwner).OfType<ICmObject>().ToList();
        if (subject is IFsAbstractStructure && FeatureOwner(subject) is { } owner) owners.Add(owner);
        foreach (var context in services.GetInstance<IPhSimpleContextNCRepository>().AllInstances())
            if (context.PlusConstrRS.Concat(context.MinusConstrRS).Any(constraint => constraint.FeatureRA == subject) &&
                NearestOwner(context) is { } ruleOwner) owners.Add(ruleOwner);
        return Merge(WarningWordsPath.ThroughFeatureOwners, owners.Distinct().Select(OwnerReach));
    }

    private static ICmObject? FeatureOwner(ICmObject item)
    {
        for (var owner = item.Owner; owner is not null; owner = owner.Owner)
            if (owner is IMoMorphSynAnalysis or IMoForm or IPhPhoneme or IPhNaturalClass or IPhSegmentRule or
                IMoStemName or IMoInflClass or IMoInflAffixTemplate or ILexEntryInflType or IPartOfSpeech) return owner;
        return null;
    }

    private static WarningReach OwnerReach(ICmObject owner) => owner switch
    {
        IMoForm form => new(WarningWordsPath.ThroughAllomorphs) { AllomorphIds = [Id(form)] },
        IMoMorphSynAnalysis msa => new(WarningWordsPath.ThroughGrammaticalInfo) { GrammaticalInfoIds = [Id(msa)] },
        IPhNaturalClass naturalClass => NaturalClass(naturalClass),
        IPhSegmentRule rule => new(WarningWordsPath.RuleTimes) { TimingKeys = [new(PhonRule, Id(rule))] },
        IPhEnvironment environment => new(WarningWordsPath.ThroughAllomorphs)
        {
            AllomorphIds = AllomorphsConditionedBy(environment.Cache, [environment]).Select(Id).ToArray(),
        },
        IPhPhoneme phoneme => Spelling(new("", GrammarWarningPartRole.Object, Id(phoneme), "PhPhoneme")
            { SubjectGuid = Id(phoneme) }, phoneme.Cache),
        _ => ReverseReferences(owner),
    };

    private static WarningReach Merge(WarningWordsPath path, IEnumerable<WarningReach> routes)
    {
        var items = routes.ToArray();
        var exact = items.Where(item => item.Path != WarningWordsPath.Membership).ToArray();
        var members = items.Where(item => item.Path == WarningWordsPath.Membership).ToArray();
        return new(path)
        {
            AllomorphIds = exact.SelectMany(item => item.AllomorphIds).Distinct().ToArray(),
            GrammaticalInfoIds = exact.SelectMany(item => item.GrammaticalInfoIds).Distinct().ToArray(),
            TimingKeys = exact.SelectMany(item => item.TimingKeys).Distinct().ToArray(),
            Spellings = items.SelectMany(item => item.Spellings).Distinct().ToArray(),
            MembershipAllomorphIds = members.SelectMany(item => item.AllomorphIds)
                .Concat(items.SelectMany(item => item.MembershipAllomorphIds)).Distinct().ToArray(),
            MembershipGrammaticalInfoIds = members.SelectMany(item => item.GrammaticalInfoIds)
                .Concat(items.SelectMany(item => item.MembershipGrammaticalInfoIds)).Distinct().ToArray(),
            MembershipTimingKeys = members.SelectMany(item => item.TimingKeys)
                .Concat(items.SelectMany(item => item.MembershipTimingKeys)).Distinct().ToArray(),
        };
    }
}

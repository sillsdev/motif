using SIL.LCModel;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Projection;

/// <summary>
/// Reads what FieldWorks says about the object an <see cref="ObjectUseRef"/> names, from an already-open project:
/// a morpheme's entry, senses, grammatical info and allomorphs, or a rule's kind, and the key PanGloss times it
/// under. It only reads, so it never opens a unit of work and never changes the project.
/// </summary>
public static class ObjectFactsReader
{
    /// <summary>
    /// The facts for <paramref name="reference"/>, each FieldWorks destination from <paramref name="fieldWorks"/>;
    /// <see langword="null"/> when the project holds no entry, grammatical info or rule the ref names.
    /// </summary>
    /// <remarks>
    /// An allomorph names its entry and a grammatical info its own; a timing key names an entry, a grammatical info
    /// or a rule. A ref naming only an allomorph gets its entry's grammatical info when the entry has exactly one.
    /// </remarks>
    public static ObjectFacts? Read(LcmCache cache, ObjectUseRef reference,
        Func<ICmObject, TraceFieldWorksTarget?> fieldWorks)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(fieldWorks);
        if (InspectorSubjectResolver.Resolve(cache, reference).Ref is null) return null;
        var allomorph = Find<IMoForm>(cache, reference.AllomorphId);
        var timed = reference.TimingKind is null ? null : Find<ICmObject>(cache, reference.TimingKey);
        var msa = Find<IMoMorphSynAnalysis>(cache, reference.GrammaticalInfoId) ?? timed as IMoMorphSynAnalysis;
        var entry = allomorph?.Owner as ILexEntry ?? msa?.Owner as ILexEntry ?? timed as ILexEntry;
        var rule = timed is IPhSegmentRule or IMoCompoundRule || timed is IMoMorphSynAnalysis and not IMoStemMsa
            ? timed : null;
        if (entry is null && rule is null) return null;
        if (msa is null && entry?.MorphoSyntaxAnalysesOC.Count == 1) msa = entry.MorphoSyntaxAnalysesOC.First();

        return new ObjectFacts
        {
            Entry = entry is null ? null : new ObjectFactsEntry(Id(entry), entry.HeadWord?.Text ?? string.Empty)
            {
                HeadwordWritingSystem = WritingSystemTextReader.SingleId(cache, entry.HeadWord),
                MorphTypeWritingSystem = WritingSystemTextReader.BestAnalysis(cache, entry.LexemeFormOA?.MorphTypeRA?.Name).WritingSystem,
                MorphType = Text(entry.LexemeFormOA?.MorphTypeRA?.Name),
                FieldWorks = fieldWorks(entry),
            },
            Senses = entry is null ? [] : entry.AllSenses
                .Where(sense => msa is null || sense.MorphoSyntaxAnalysisRA == msa)
                .Select(sense => new ObjectFactsSense(Id(sense), sense.LexSenseOutline?.Text ?? string.Empty)
                {
                    GlossWritingSystem = WritingSystemTextReader.BestAnalysis(cache, sense.Gloss).WritingSystem,
                    DefinitionWritingSystem = WritingSystemTextReader.BestAnalysis(cache, sense.Definition).WritingSystem,
                    Gloss = Text(sense.Gloss),
                    Definition = Text(sense.Definition),
                    FieldWorks = fieldWorks(sense),
                }).ToArray(),
            GrammaticalInfo = msa is null ? null : GrammaticalInfo(cache, msa, fieldWorks),
            Allomorphs = entry is null ? [] : new[] { entry.LexemeFormOA }.Concat(entry.AlternateFormsOS)
                .OfType<IMoForm>().Select(form => Allomorph(cache, form, form == allomorph, fieldWorks)).ToArray(),
            Rule = rule is null ? null : Rule(cache, rule, entry, fieldWorks),
            TimingKey = TimingKey(reference, timed, msa, entry),
        };
    }

    // PanGloss keys an affix by its grammatical info and a stem by its entry; a timed object keeps its own kind.
    private static TraceTimingKey? TimingKey(ObjectUseRef reference, ICmObject? timed, IMoMorphSynAnalysis? msa,
        ILexEntry? entry)
    {
        if (timed is not null) return new TraceTimingKey(reference.TimingKind!, Id(timed));
        var stems = entry?.MorphoSyntaxAnalysesOC;
        if (entry is not null && (msa is IMoStemMsa || msa is null && stems!.Count > 0 && stems.All(each => each is IMoStemMsa)))
            return new TraceTimingKey("lex_entry", Id(entry));
        return msa is null ? null : new TraceTimingKey("morph_rule", Id(msa));
    }

    private static ObjectFactsGrammaticalInfo GrammaticalInfo(LcmCache cache, IMoMorphSynAnalysis msa,
        Func<ICmObject, TraceFieldWorksTarget?> fieldWorks)
    {
        ObjectFactsNamed? Named(ICmPossibility? category) => category is null ? null
            : new ObjectFactsNamed(Id(category), Text(category.Name) ?? string.Empty)
            {
                NameWritingSystem = WritingSystemTextReader.BestAnalysis(cache, category.Name).WritingSystem,
                FieldWorks = fieldWorks(category),
            };
        IReadOnlyList<ObjectFactsSlot> Slots(IEnumerable<IMoInflAffixSlot> slots) => slots
            .Select(slot => new ObjectFactsSlot(Id(slot), Text(slot.Name) ?? string.Empty)
            {
                NameWritingSystem = WritingSystemTextReader.BestAnalysis(cache, slot.Name).WritingSystem,
                Optional = slot.Optional,
                Templates = Templates(cache).Where(template =>
                        template.PrefixSlotsRS.Contains(slot) || template.SuffixSlotsRS.Contains(slot))
                    .Select(template => new ObjectFactsNamed(Id(template), Text(template.Name) ?? string.Empty)
                    {
                        NameWritingSystem = WritingSystemTextReader.BestAnalysis(cache, template.Name).WritingSystem,
                        FieldWorks = fieldWorks(template),
                    }).ToArray(),
                FieldWorks = fieldWorks(slot),
            }).ToArray();
        ObjectFactsFeatures? Features(IFsFeatStruc? structure) => ObjectFactsReader.Features(cache, structure, fieldWorks);

        return msa switch
        {
            IMoStemMsa stem => new ObjectFactsGrammaticalInfo(Id(msa), "stem")
            {
                Category = Named(stem.PartOfSpeechRA),
                Slots = Slots(stem.SlotsRC),
                AddedFeatures = Features(stem.MsFeaturesOA),
            },
            IMoInflAffMsa inflectional => new ObjectFactsGrammaticalInfo(Id(msa), "inflectionalAffix")
            {
                Category = Named(inflectional.PartOfSpeechRA),
                Slots = Slots(inflectional.SlotsRC),
                AddedFeatures = Features(inflectional.InflFeatsOA),
            },
            IMoDerivAffMsa derivational => new ObjectFactsGrammaticalInfo(Id(msa), "derivationalAffix")
            {
                Category = Named(derivational.FromPartOfSpeechRA),
                ResultCategory = Named(derivational.ToPartOfSpeechRA),
                RequiredFeatures = Features(derivational.FromMsFeaturesOA),
                AddedFeatures = Features(derivational.ToMsFeaturesOA),
            },
            IMoDerivStepMsa step => new ObjectFactsGrammaticalInfo(Id(msa), "derivationalStep")
            {
                Category = Named(step.PartOfSpeechRA),
                AddedFeatures = Features(step.MsFeaturesOA),
            },
            IMoUnclassifiedAffixMsa unclassified => new ObjectFactsGrammaticalInfo(Id(msa), "unclassifiedAffix")
            {
                Category = Named(unclassified.PartOfSpeechRA),
            },
            _ => new ObjectFactsGrammaticalInfo(Id(msa), "unknown"),
        };
    }

    private static ObjectFactsAllomorph Allomorph(LcmCache cache, IMoForm form, bool asked,
        Func<ICmObject, TraceFieldWorksTarget?> fieldWorks)
    {
        var type = form.MorphTypeRA;
        IEnumerable<IPhEnvironment> environments = form switch
        {
            IMoStemAllomorph stem => stem.PhoneEnvRC,
            IMoAffixAllomorph affix => affix.PhoneEnvRC.Concat(affix.PositionRS),
            _ => [],
        };
        return new ObjectFactsAllomorph(Id(form),
            (type?.Prefix ?? string.Empty) + form.Form.BestVernacularAlternative?.Text + (type?.Postfix ?? string.Empty))
        {
            FormWritingSystem = WritingSystemTextReader.SingleId(cache, form.Form.BestVernacularAlternative),
            MorphTypeWritingSystem = WritingSystemTextReader.BestAnalysis(cache, type?.Name).WritingSystem,
            MorphType = Text(type?.Name),
            IsAsked = asked,
            Environments = environments.Distinct().Select(environment => new ObjectFactsEnvironment(Id(environment),
                environment.StringRepresentation?.Text ?? string.Empty)
            {
                NotationWritingSystem = WritingSystemTextReader.SingleId(cache, environment.StringRepresentation),
                FieldWorks = fieldWorks(environment),
            })
                .ToArray(),
            RequiredFeatures = Features(cache, (form as IMoAffixAllomorph)?.MsEnvFeaturesOA, fieldWorks),
        };
    }

    private static ObjectFactsRule Rule(LcmCache cache, ICmObject rule, ILexEntry? entry,
        Func<ICmObject, TraceFieldWorksTarget?> fieldWorks)
    {
        var (kind, name) = rule switch
        {
            IPhSegmentRule segment => ("phonologicalRule", Text(segment.Name)),
            IMoCompoundRule compound => ("compoundRule", Text(compound.Name)),
            _ => ("affixRule", AffixName(entry, (IMoMorphSynAnalysis)rule)),
        };
        return new ObjectFactsRule(Id(rule), kind, name ?? string.Empty)
        {
            NameWritingSystem = rule switch
            {
                IPhSegmentRule segment => WritingSystemTextReader.BestAnalysis(cache, segment.Name).WritingSystem,
                IMoCompoundRule compound => WritingSystemTextReader.BestAnalysis(cache, compound.Name).WritingSystem,
                _ => null,
            },
            FieldWorks = fieldWorks(rule),
        };
    }

    // An affix is known by its entry, as FieldWorks shows it: headword, then its sense's gloss if any.
    private static string? AffixName(ILexEntry? entry, IMoMorphSynAnalysis msa)
    {
        var headword = entry?.HeadWord?.Text;
        var gloss = Text(entry?.AllSenses.FirstOrDefault(sense => sense.MorphoSyntaxAnalysisRA == msa)?.Gloss);
        return gloss is null ? headword : $"{headword} ‘{gloss}’";
    }

    private static ObjectFactsFeatures? Features(LcmCache cache, IFsFeatStruc? structure,
        Func<ICmObject, TraceFieldWorksTarget?> fieldWorks)
    {
        if (structure is null) return null;
        var values = ClosedValues(structure).Select(value => new ObjectFactsFeatureValue(
            Text(value.FeatureRA?.Name) ?? string.Empty, Text(value.ValueRA?.Name) ?? string.Empty)
        {
            FeatureWritingSystem = WritingSystemTextReader.BestAnalysis(cache, value.FeatureRA?.Name).WritingSystem,
            ValueWritingSystem = WritingSystemTextReader.BestAnalysis(cache, value.ValueRA?.Name).WritingSystem,
            ValueAbbreviationWritingSystem = WritingSystemTextReader.BestAnalysis(cache, value.ValueRA?.Abbreviation).WritingSystem,
            ValueAbbreviation = Text(value.ValueRA?.Abbreviation),
            FieldWorks = value.FeatureRA is { } feature ? fieldWorks(feature) : null,
        }).ToArray();
        return values.Length == 0 ? null : new ObjectFactsFeatures(structure.LongName, values);
    }

    private static IEnumerable<IFsClosedValue> ClosedValues(IFsFeatStruc structure) => structure.FeatureSpecsOC
        .SelectMany(spec => spec switch
        {
            IFsClosedValue closed => [closed],
            IFsComplexValue { ValueOA: IFsFeatStruc inner } => ClosedValues(inner),
            _ => Enumerable.Empty<IFsClosedValue>(),
        });

    // Every template in category order, depth first, as Category Edit lists them.
    private static IEnumerable<IMoInflAffixTemplate> Templates(LcmCache cache)
    {
        IEnumerable<IPartOfSpeech> Walk(IEnumerable<ICmPossibility> categories) => categories.OfType<IPartOfSpeech>()
            .SelectMany(category => new[] { category }.Concat(Walk(category.SubPossibilitiesOS)));
        return Walk(cache.LangProject.PartsOfSpeechOA?.PossibilitiesOS ?? Enumerable.Empty<ICmPossibility>())
            .SelectMany(category => category.AffixTemplatesOS);
    }

    private static T? Find<T>(LcmCache cache, string? id) where T : class, ICmObject =>
        Guid.TryParse(id, out var guid) &&
        cache.ServiceLocator.GetInstance<ICmObjectRepository>().TryGetObject(guid, out var found)
            ? found as T : null;

    private static string Id(ICmObject found) => found.Guid.ToString("D");

    // LibLCM shows a missing alternative as "***", which is no text at all.
    private static string? Text(IMultiAccessorBase? text) =>
        text?.BestAnalysisAlternative?.Text is { Length: > 0 } value && value != "***" ? value : null;
}

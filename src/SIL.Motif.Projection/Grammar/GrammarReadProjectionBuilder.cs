using System.Text;
using System.Text.Json.Nodes;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Projection.Grammar;

/// <summary>Projects a bounded page of authored grammar objects from an open project.</summary>
public static class GrammarReadProjectionBuilder
{
    /// <summary>
    /// Reads a phonological rule's kind and disabled flag. Detailed reads include the authoring intent for an edit
    /// guard, or null when the rule has a shape the composer cannot express, and the number of right-hand sides.
    /// </summary>
    public static JsonObject ReadPhonologicalRule(IPhSegmentRule rule, bool detailed)
    {
        ArgumentNullException.ThrowIfNull(rule);
        var facts = new JsonObject
        {
            ["ruleKind"] = rule.ClassName,
            ["disabled"] = rule.Disabled,
        };
        if (detailed)
        {
            facts["rightHandSideCount"] = (rule as IPhRegularRule)?.RightHandSidesOS.Count;
            facts["intent"] = PhonologicalRuleReader.Read(rule) is { } intent
                ? PhonologicalRuleReader.ToJson(intent) : null;
        }
        return facts;
    }

    /// <summary>Builds the matching object page for <paramref name="request"/>.</summary>
    public static GrammarReadResponse Build(LcmCache cache, GrammarReadRequest request)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(request);

        var filtered = Objects(cache, request.Kind, withMsas: request.Id is not null)
            .Where(item => Matches(item, request.Query))
            .Where(item => MatchesExact(item, request.Exact))
            .Where(item => MatchesMorphType(item, request.MorphType))
            .Where(item => MatchesId(item, request.Id));
        // Phonological rules apply in project order, so that order, not the name, is the page order.
        var ordered = request.Kind == GrammarReadKind.phonological_rule
            ? filtered.OrderBy(item => ((GrammarPhonologicalRuleRead)item).Position)
            : filtered.OrderBy(SortKey, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Id, StringComparer.Ordinal);
        var matches = ordered.ToArray();
        var items = matches.Skip(request.Offset).Take(request.Limit)
            .Select(item => item is GrammarAffixTemplateRead template ? FillTemplate(cache, template) : item).ToArray();
        return new GrammarReadResponse(request.Kind, matches.Length, request.Offset, request.Limit, items)
        {
            ExactNote = string.IsNullOrWhiteSpace(request.MorphType) ? ExactNote(request.Exact, matches) : null,
        };
    }

    private static string? ExactNote(string? exact, GrammarReadItem[] matches) =>
        ExactNote(exact, matches.OfType<GrammarLexicalEntryRead>().Select(entry => (entry.Headword, entry.Id)));

    /// <summary>
    /// The note for a bare exact form that matched marked headwords only by stripping the marker, or null.
    /// Callers that filter by morph type skip it, since they already narrowed the kind of form.
    /// </summary>
    /// <param name="exact">The exact form asked for.</param>
    /// <param name="matched">The headword and id of every entry the exact form matched.</param>
    public static string? ExactNote(string? exact, IEnumerable<(string? Headword, string Id)> matched)
    {
        if (string.IsNullOrWhiteSpace(exact)) return null;
        var marked = matched.Where(entry => FormMatch.MatchesOnlyByMarker(exact, [entry.Headword])).ToArray();
        if (marked.Length == 0) return null;
        var names = marked.Select(entry => entry.Headword ?? entry.Id).ToArray();
        return $"'{exact.Trim()}' also matched {marked.Length} marked headword(s): {string.Join(", ", names)}. " +
            "Give the marker (for example " + string.Join(" or ", names.Take(3).Select(name => $"'{name.Trim()}'")) +
            ") to match only that form.";
    }

    /// <summary>The morph type names a morph type filter accepts, in the project's order.</summary>
    public static IReadOnlyList<string> MorphTypeNames(LcmCache cache) =>
        cache.LangProject.LexDbOA.MorphTypesOA.PossibilitiesOS.Select(type => Text(cache, type.Name))
            .OfType<string>().ToArray();

    /// <summary>The names in the comma-separated <paramref name="morphTypes"/> that are not a morph type name or id.</summary>
    public static IReadOnlyList<string> UnknownMorphTypes(LcmCache cache, string morphTypes)
    {
        var known = cache.LangProject.LexDbOA.MorphTypesOA.PossibilitiesOS
            .SelectMany(type => new[] { Text(cache, type.Name), Id(type) }).OfType<string>().ToArray();
        return morphTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(wanted => !known.Any(name => string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    private static IEnumerable<GrammarReadItem> Objects(LcmCache cache, GrammarReadKind kind, bool withMsas) => kind switch
    {
        GrammarReadKind.phoneme => cache.LangProject.PhonologicalDataOA.PhonemeSetsOS
            .SelectMany(set => set.PhonemesOC).Select(phoneme => ReadPhoneme(cache, phoneme)),
        GrammarReadKind.natural_class => cache.LangProject.PhonologicalDataOA.NaturalClassesOS
            .Select(naturalClass => ReadNaturalClass(cache, naturalClass)),
        GrammarReadKind.environment => cache.LangProject.PhonologicalDataOA.EnvironmentsOS
            .Select(environment => ReadEnvironment(cache, environment)),
        GrammarReadKind.part_of_speech => Categories(cache.LangProject.PartsOfSpeechOA.PossibilitiesOS)
            .Select(category => ReadPartOfSpeech(cache, category)),
        GrammarReadKind.inflection_feature => cache.LangProject.MsFeatureSystemOA.FeaturesOC
            .Select(feature => ReadFeature(cache, feature)),
        GrammarReadKind.lexical_entry => cache.ServiceLocator.GetInstance<ILexEntryRepository>().AllInstances()
            .Select(entry => ReadEntry(cache, entry, withMsas)),
        GrammarReadKind.phonological_rule => cache.LangProject.PhonologicalDataOA.PhonRulesOS
            .Select((rule, index) => ReadRule(cache, rule, index + 1)),
        GrammarReadKind.affix_template => ReadTemplates(cache),
        _ => [],
    };

    private static GrammarPhonologicalRuleRead ReadRule(LcmCache cache, IPhSegmentRule rule, int position)
    {
        var facts = ReadPhonologicalRule(rule, detailed: true);
        return new GrammarPhonologicalRuleRead(Id(rule), Text(cache, rule.Name), position, rule.ClassName, rule.Disabled,
            facts["rightHandSideCount"]?.GetValue<int>(), facts["intent"]?.DeepClone());
    }

    private static GrammarPhonemeRead ReadPhoneme(LcmCache cache, IPhPhoneme phoneme) => new(
        Id(phoneme), Text(cache, phoneme.Name), phoneme.CodesOS.SelectMany(code =>
            code.Representation.AvailableWritingSystemIds.Order().Select(ws =>
                WritingSystemTextReader.Read(cache, code.Representation.get_String(ws))))
            .Where(value => value.Text.Length > 0)
            .Select(value => new GrammarRepresentationRead(value.Text, value.WritingSystem)).ToArray());

    private static GrammarNaturalClassRead ReadNaturalClass(LcmCache cache, IPhNaturalClass naturalClass)
    {
        var members = naturalClass is IPhNCSegments segments
            ? segments.SegmentsRC.Select(member => new GrammarNamedObjectRead(Id(member), Text(cache, member.Name))).ToArray()
            : [];
        var features = naturalClass is IPhNCFeatures featureClass && featureClass.FeaturesOA is { } structure
            ? structure.FeatureSpecsOC.Where(spec => spec.FeatureRA is not null)
                .Select(spec => new GrammarFeatureSpecificationRead(
                    Named(cache, spec.FeatureRA!),
                    spec is IFsClosedValue closed && closed.ValueRA is { } value ? Named(cache, value) : null))
                .ToArray()
            : [];
        return new GrammarNaturalClassRead(Id(naturalClass), Text(cache, naturalClass.Name),
            Text(cache, naturalClass.Abbreviation), naturalClass.ClassName, members, features);
    }

    private static GrammarEnvironmentRead ReadEnvironment(LcmCache cache, IPhEnvironment environment) => new(
        Id(environment), Text(cache, environment.Name), environment.StringRepresentation?.Text);

    private static GrammarPartOfSpeechRead ReadPartOfSpeech(LcmCache cache, IPartOfSpeech category) => new(
        Id(category), Text(cache, category.Name), Text(cache, category.Abbreviation),
        category.Owner is ICmPossibility parent ? Named(cache, parent) : null);

    private static GrammarInflectionFeatureRead ReadFeature(LcmCache cache, IFsFeatDefn feature) => new(
        Id(feature), Text(cache, feature.Name), Text(cache, feature.Abbreviation), feature.ClassName,
        feature is IFsClosedFeature closed
            ? closed.ValuesOC.Select(value => Named(cache, value)).ToArray()
            : []);

    private static GrammarLexicalEntryRead ReadEntry(LcmCache cache, ILexEntry entry, bool withMsas)
    {
        var lexeme = entry.LexemeFormOA is { } form ? ReadForm(cache, form) : null;
        return new GrammarLexicalEntryRead(Id(entry), entry.HeadWord?.Text,
            Text(cache, entry.LexemeFormOA?.MorphTypeRA?.Name),
            entry.LexemeFormOA?.MorphTypeRA is { } morphType ? Id(morphType) : null,
            lexeme,
            entry.AllSenses.Select(sense => ReadSense(cache, sense)).ToArray(),
            entry.AlternateFormsOS.Select(alternate => ReadForm(cache, alternate)).ToArray())
        {
            Msas = withMsas ? entry.MorphoSyntaxAnalysesOC.Select(msa => ReadMsa(cache, msa)).ToArray() : null,
        };
    }

    // Templates are listed without affixes; only the page that is returned has its slots filled.
    private static IEnumerable<GrammarAffixTemplateRead> ReadTemplates(LcmCache cache) =>
        Categories(cache.LangProject.PartsOfSpeechOA.PossibilitiesOS).SelectMany(category =>
            category.AffixTemplatesOS.Select(template => new GrammarAffixTemplateRead(Id(template),
                Text(cache, template.Name), Named(cache, category), template.Final,
                template.PrefixSlotsRS.Select(slot => ReadSlot(cache, slot, withAffixes: false)).ToArray(),
                template.SuffixSlotsRS.Select(slot => ReadSlot(cache, slot, withAffixes: false)).ToArray())));

    private static GrammarAffixTemplateRead FillTemplate(LcmCache cache, GrammarAffixTemplateRead shell)
    {
        var template = (IMoInflAffixTemplate)cache.ServiceLocator.ObjectRepository.GetObject(CanonicalId.Parse(shell.Id).ToGuid());
        return shell with
        {
            PrefixSlots = template.PrefixSlotsRS.Select(slot => ReadSlot(cache, slot, withAffixes: true)).ToArray(),
            SuffixSlots = template.SuffixSlotsRS.Select(slot => ReadSlot(cache, slot, withAffixes: true)).ToArray(),
        };
    }

    // The analyses that name the slot come from its referrers, so no lexicon scan is needed.
    private static GrammarAffixSlotRead ReadSlot(LcmCache cache, IMoInflAffixSlot slot, bool withAffixes) =>
        new(Id(slot), Text(cache, slot.Name), slot.Optional, !withAffixes ? [] :
            slot.ReferringObjects.OfType<IMoInflAffMsa>().Where(msa => msa.Owner is ILexEntry)
                .Select(msa => (Entry: (ILexEntry)msa.Owner, Msa: msa))
                .OrderBy(row => row.Entry.HeadWord?.Text, StringComparer.Ordinal).ThenBy(row => Id(row.Msa), StringComparer.Ordinal)
                .Select(row => new GrammarSlotAffixRead(Id(row.Entry), row.Entry.HeadWord?.Text,
                    Text(cache, row.Entry.LexemeFormOA?.MorphTypeRA?.Name), Id(row.Msa),
                    row.Msa.PartOfSpeechRA is { } pos ? Named(cache, pos) : null)).ToArray());

    private static GrammarGrammaticalInfoRead ReadMsa(LcmCache cache, IMoMorphSynAnalysis msa)
    {
        var (partOfSpeech, resultPartOfSpeech) = msa switch
        {
            IMoStemMsa stem => (stem.PartOfSpeechRA, (IPartOfSpeech?)null),
            IMoInflAffMsa inflectional => (inflectional.PartOfSpeechRA, (IPartOfSpeech?)null),
            IMoDerivAffMsa derivational => (derivational.FromPartOfSpeechRA, derivational.ToPartOfSpeechRA),
            IMoDerivStepMsa step => (step.PartOfSpeechRA, (IPartOfSpeech?)null),
            IMoUnclassifiedAffixMsa unclassified => (unclassified.PartOfSpeechRA, (IPartOfSpeech?)null),
            _ => ((IPartOfSpeech?)null, (IPartOfSpeech?)null),
        };
        return new GrammarGrammaticalInfoRead(Id(msa), msa.ClassName,
            partOfSpeech is null ? null : Named(cache, partOfSpeech),
            resultPartOfSpeech is null ? null : Named(cache, resultPartOfSpeech))
        {
            InflectionClass = msa is IMoStemMsa { InflectionClassRA: { } inflectionClass }
                ? new GrammarNamedObjectRead(Id(inflectionClass), Text(cache, inflectionClass.Name)) : null,
            Slots = msa is IMoInflAffMsa { SlotsRC.Count: > 0 } inflAff
                ? inflAff.SlotsRC.Select(slot => new GrammarNamedObjectRead(Id(slot), Text(cache, slot.Name))).ToArray() : null,
        };
    }

    private static GrammarFormRead ReadForm(LcmCache cache, IMoForm form)
    {
        var text = WritingSystemTextReader.First(cache, form.Form);
        IEnumerable<IPhEnvironment> environments = form switch
        {
            IMoStemAllomorph stem => stem.PhoneEnvRC,
            IMoAffixAllomorph affix => affix.PhoneEnvRC.Concat(affix.PositionRS),
            _ => [],
        };
        return new GrammarFormRead(Id(form), text.Text, text.WritingSystem, form.ClassName,
            Text(cache, form.MorphTypeRA?.Name), form.MorphTypeRA is { } morphType ? Id(morphType) : null,
            environments.Distinct().Select(environment => new GrammarEnvironmentReferenceRead(
                Id(environment), Text(cache, environment.Name), environment.StringRepresentation?.Text)).ToArray());
    }

    private static GrammarSenseRead ReadSense(LcmCache cache, ILexSense sense)
    {
        var gloss = WritingSystemTextReader.BestAnalysis(cache, sense.Gloss);
        var definition = WritingSystemTextReader.BestAnalysis(cache, sense.Definition);
        var grammaticalInfo = sense.MorphoSyntaxAnalysisRA is { } msa ? ReadMsa(cache, msa) : null;
        return new GrammarSenseRead(Id(sense), EmptyToNull(gloss.Text), gloss.WritingSystem,
            EmptyToNull(definition.Text), definition.WritingSystem, grammaticalInfo);
    }

    private static GrammarNamedObjectRead Named(LcmCache cache, ICmPossibility possibility) =>
        new(Id(possibility), Text(cache, possibility.Name));

    private static GrammarNamedObjectRead Named(LcmCache cache, IFsFeatDefn feature) =>
        new(Id(feature), Text(cache, feature.Name));

    private static GrammarNamedObjectRead Named(LcmCache cache, IFsSymFeatVal value) =>
        new(Id(value), Text(cache, value.Name));

    private static IEnumerable<IPartOfSpeech> Categories(IEnumerable<ICmPossibility> possibilities) =>
        possibilities.OfType<IPartOfSpeech>().SelectMany(category => new[] { category }
            .Concat(Categories(category.SubPossibilitiesOS)));

    private static bool Matches(GrammarReadItem item, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        var text = query.Trim().Normalize(NormalizationForm.FormD);
        return SearchTerms(item).Any(term =>
            term?.Normalize(NormalizationForm.FormD).Contains(text, StringComparison.OrdinalIgnoreCase) == true);
    }

    private static bool MatchesExact(GrammarReadItem item, string? exact) =>
        string.IsNullOrWhiteSpace(exact) || FormMatch.Any(exact, ExactTerms(item));

    private static IEnumerable<string?> ExactTerms(GrammarReadItem item) => item is GrammarLexicalEntryRead entry
        ? [entry.Headword, .. Forms(entry).Select(form => form.Form)]
        : SearchTerms(item);

    private static bool MatchesMorphType(GrammarReadItem item, string? morphTypes)
    {
        if (string.IsNullOrWhiteSpace(morphTypes)) return true;
        if (item is not GrammarLexicalEntryRead entry) return false;
        return morphTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(wanted =>
            string.Equals(wanted, entry.MorphTypeId, StringComparison.Ordinal) ||
            string.Equals(wanted, entry.MorphType, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string?> SearchTerms(GrammarReadItem item) => item switch
    {
        GrammarPhonemeRead phoneme => [phoneme.Name, .. phoneme.Representations.Select(value => value.Text)],
        GrammarNaturalClassRead naturalClass => [naturalClass.Name, naturalClass.Abbreviation],
        GrammarEnvironmentRead environment => [environment.Name, environment.Notation],
        GrammarPartOfSpeechRead category => [category.Name, category.Abbreviation],
        GrammarInflectionFeatureRead feature => [feature.Name, feature.Abbreviation,
            .. feature.Values.Select(value => value.Name)],
        GrammarPhonologicalRuleRead rule => [rule.Name],
        GrammarAffixTemplateRead template => [template.Name],
        GrammarLexicalEntryRead entry => [entry.Headword, entry.MorphType,
            .. entry.Senses.Select(sense => sense.Gloss),
            .. Forms(entry).Select(form => form.Form)],
        _ => [],
    };

    private static string SortKey(GrammarReadItem item) => item switch
    {
        GrammarPhonemeRead phoneme => phoneme.Name ?? phoneme.Representations.FirstOrDefault()?.Text ?? string.Empty,
        GrammarNaturalClassRead naturalClass => naturalClass.Name ?? naturalClass.Abbreviation ?? string.Empty,
        GrammarEnvironmentRead environment => environment.Name ?? environment.Notation ?? string.Empty,
        GrammarPartOfSpeechRead category => category.Name ?? category.Abbreviation ?? string.Empty,
        GrammarInflectionFeatureRead feature => feature.Name ?? feature.Abbreviation ?? string.Empty,
        GrammarLexicalEntryRead entry => entry.Headword ?? string.Empty,
        GrammarPhonologicalRuleRead rule => rule.Name ?? string.Empty,
        GrammarAffixTemplateRead template => template.Name ?? string.Empty,
        _ => string.Empty,
    };

    private static IEnumerable<GrammarFormRead> Forms(GrammarLexicalEntryRead entry) =>
        entry.LexemeForm is { } lexeme ? entry.AlternateForms.Prepend(lexeme) : entry.AlternateForms;

    private static bool MatchesId(GrammarReadItem item, string? id)
    {
        if (id is null) return true;
        var target = CanonicalId.Parse(id).ToGuid();
        return CanonicalId.Parse(item.Id).ToGuid() == target;
    }

    private static string Id(ICmObject target) => CanonicalId.FromGuid(target.Guid).Value;

    private static string? Text(LcmCache cache, IMultiAccessorBase? accessor)
    {
        if (accessor is null) return null;
        var text = WritingSystemTextReader.BestAnalysis(cache, accessor).Text;
        if (text.Length == 0) text = WritingSystemTextReader.First(cache, accessor).Text;
        return EmptyToNull(text);
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrEmpty(value) ? null : value;
}

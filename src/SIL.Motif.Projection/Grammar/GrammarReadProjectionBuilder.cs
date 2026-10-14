using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Projection.Grammar;

/// <summary>Projects a bounded page of authored grammar objects from an open project.</summary>
public static class GrammarReadProjectionBuilder
{
    /// <summary>Builds the matching object page for <paramref name="request"/>.</summary>
    public static GrammarReadResponse Build(LcmCache cache, GrammarReadRequest request)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(request);

        var matches = Objects(cache, request.Kind)
            .Where(item => Matches(item, request.Query))
            .Where(item => MatchesId(item, request.Id))
            .OrderBy(SortKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
        var items = matches.Skip(request.Offset).Take(request.Limit).ToArray();
        return new GrammarReadResponse(request.Kind, matches.Length, request.Offset, request.Limit, items);
    }

    private static IEnumerable<GrammarReadItem> Objects(LcmCache cache, GrammarReadKind kind) => kind switch
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
            .Select(entry => ReadEntry(cache, entry)),
        _ => [],
    };

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

    private static GrammarLexicalEntryRead ReadEntry(LcmCache cache, ILexEntry entry)
    {
        var lexeme = entry.LexemeFormOA is { } form ? ReadForm(cache, form) : null;
        return new GrammarLexicalEntryRead(Id(entry), entry.HeadWord?.Text,
            Text(cache, entry.LexemeFormOA?.MorphTypeRA?.Name),
            entry.LexemeFormOA?.MorphTypeRA is { } morphType ? Id(morphType) : null,
            lexeme,
            entry.AllSenses.Select(sense => ReadSense(cache, sense)).ToArray(),
            entry.AlternateFormsOS.Select(alternate => ReadForm(cache, alternate)).ToArray());
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
        var msa = sense.MorphoSyntaxAnalysisRA;
        var (partOfSpeech, resultPartOfSpeech) = msa switch
        {
            IMoStemMsa stem => (stem.PartOfSpeechRA, (IPartOfSpeech?)null),
            IMoInflAffMsa inflectional => (inflectional.PartOfSpeechRA, (IPartOfSpeech?)null),
            IMoDerivAffMsa derivational => (derivational.FromPartOfSpeechRA, derivational.ToPartOfSpeechRA),
            IMoDerivStepMsa step => (step.PartOfSpeechRA, (IPartOfSpeech?)null),
            IMoUnclassifiedAffixMsa unclassified => (unclassified.PartOfSpeechRA, (IPartOfSpeech?)null),
            _ => ((IPartOfSpeech?)null, (IPartOfSpeech?)null),
        };
        var grammaticalInfo = msa is null ? null : new GrammarGrammaticalInfoRead(
            Id(msa), msa.ClassName, partOfSpeech is null ? null : Named(cache, partOfSpeech),
            resultPartOfSpeech is null ? null : Named(cache, resultPartOfSpeech));
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
        var text = query.Trim();
        return SearchTerms(item).Any(term => term?.Contains(text, StringComparison.OrdinalIgnoreCase) == true);
    }

    private static IEnumerable<string?> SearchTerms(GrammarReadItem item) => item switch
    {
        GrammarPhonemeRead phoneme => [phoneme.Name, .. phoneme.Representations.Select(value => value.Text)],
        GrammarNaturalClassRead naturalClass => [naturalClass.Name, naturalClass.Abbreviation],
        GrammarEnvironmentRead environment => [environment.Name, environment.Notation],
        GrammarPartOfSpeechRead category => [category.Name, category.Abbreviation],
        GrammarInflectionFeatureRead feature => [feature.Name, feature.Abbreviation,
            .. feature.Values.Select(value => value.Name)],
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

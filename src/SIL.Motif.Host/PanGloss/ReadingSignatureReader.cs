using SIL.LCModel;
using SIL.Motif.Contract.HandAnalysis;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Host.PanGloss;

/// <summary>Projects parser morphemes against the exact FieldWorks project that produced them.</summary>
public sealed class ReadingSignatureReader(LcmCache? cache)
{
    private readonly ICmObjectRepository? _objects = cache?.ServiceLocator.ObjectRepository;
    private readonly Dictionary<(string? Form, string? Msa, string? Type), ResolvedMorpheme> _morphs = new();

    /// <summary>Reads the ordered morphemes and their canonical identities from a single parser Reading.</summary>
    public ReadingSignature Read(ParseAnalysis reading) => Capture(reading).Signature!;

    /// <summary>Freezes the canonical identity and lexicon evidence while the measured project is available.</summary>
    public ParseAnalysis Capture(ParseAnalysis reading)
    {
        var resolved = reading.Morphs.Select(Resolve).ToArray();
        return reading with
        {
            Signature = new([.. resolved.Select(item => item.Morph)]),
            MorphemeAcceptance = resolved.Select((item, index) => reading.Morphs[index].GuessedString is not null
                ? ReadingAcceptance.Guessed : item.LexiconBacked ? ReadingAcceptance.LexiconBacked : ReadingAcceptance.Unresolved).ToArray(),
        };
    }

    private ResolvedMorpheme Resolve(ParseMorph morph)
    {
        var key = (morph.Form, morph.Msa, morph.InflType);
        if (_morphs.TryGetValue(key, out var existing)) return existing;
        var form = Find<IMoForm>(morph.Form);
        var msa = Find<IMoMorphSynAnalysis>(morph.Msa);
        var entry = form?.Owner as ILexEntry ?? msa?.Owner as ILexEntry;
        var senseOwner = msa?.Owner as ILexEntry;
        var senses = senseOwner?.AllSenses
            .Where(sense => sense.MorphoSyntaxAnalysisRA?.Guid == msa!.Guid)
            .Select(sense => CanonicalId.FromGuid(sense.Guid).Value).Order(StringComparer.Ordinal).ToArray() ?? [];
        IEnumerable<ReadingFeatureValue> features = msa switch
        {
            IMoStemMsa stem => Features(stem.MsFeaturesOA, ReadingFeatureRole.Stem, []),
            IMoInflAffMsa inflection => Features(inflection.InflFeatsOA, ReadingFeatureRole.Inflection, []),
            IMoDerivAffMsa derivation => Features(derivation.FromMsFeaturesOA, ReadingFeatureRole.DerivationInput, [])
                .Concat(Features(derivation.ToMsFeaturesOA, ReadingFeatureRole.DerivationOutput, [])),
            IMoDerivStepMsa step => Features(step.MsFeaturesOA, ReadingFeatureRole.DerivationalStep, []),
            _ => [],
        };
        var direction = msa is IMoDerivAffMsa affix ? new ReadingDerivation(Id(affix.FromPartOfSpeechRA), Id(affix.ToPartOfSpeechRA)) : null;
        var senseStatus = senses.Length switch
        {
            0 => ReadingSenseStatus.Unresolved,
            1 => ReadingSenseStatus.Resolved,
            _ => ReadingSenseStatus.Ambiguous,
        };
        var result = new ResolvedMorpheme(new ReadingMorpheme(Id(entry), senses.Length == 1 ? senses[0] : null,
            senses.Length > 1 ? [.. senses] : [], senseStatus, Id(morph.Msa), Id(morph.Form), Id(morph.InflType),
            msa is IMoStemMsa, direction, [.. features]),
            entry is not null && form is not null && msa is not null && senseStatus == ReadingSenseStatus.Resolved &&
            (morph.InflType is null || Find<ILexEntryInflType>(morph.InflType) is not null));
        _morphs.Add(key, result);
        return result;
    }

    private T? Find<T>(string? id) where T : class, ICmObject =>
        _objects is not null && Guid.TryParse(id, out var guid) && _objects.TryGetObject(guid, out var obj) ? obj as T : null;

    private static string? Id(string? id) => Guid.TryParse(id, out var guid) && guid != Guid.Empty
        ? CanonicalId.FromGuid(guid).Value : null;

    private static string? Id(ICmObject? value) => value is null ? null : CanonicalId.FromGuid(value.Guid).Value;

    private static IEnumerable<ReadingFeatureValue> Features(IFsFeatStruc? structure, ReadingFeatureRole role,
        IReadOnlyList<string> path) =>
        structure?.FeatureSpecsOC.SelectMany(spec => spec switch
        {
            IFsClosedValue { FeatureRA: { } feature, ValueRA: { } value } =>
                [new ReadingFeatureValue(role, [.. path, Id(feature)!], [Id(value)!])],
            IFsComplexValue { FeatureRA: { } component, ValueOA: IFsFeatStruc inner } =>
                Features(inner, role, [.. path, Id(component)!]),
            _ => Enumerable.Empty<ReadingFeatureValue>(),
        }) ?? [];

    private sealed record ResolvedMorpheme(ReadingMorpheme Morph, bool LexiconBacked);
}

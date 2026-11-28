using System.Collections.Immutable;
using SIL.LCModel;
using SIL.Motif.Contract.HandAnalysis;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Host.PanGloss;

/// <summary>Resolves Reading identity against the FieldWorks project that produced the observed morphology.</summary>
/// <remarks>One resolver serves parser observations and stored analyses, so equal readings get equal identities.</remarks>
public sealed class ReadingSignatureReader(LcmCache? cache)
{
    private readonly ICmObjectRepository? _objects = cache?.ServiceLocator.ObjectRepository;
    private readonly Dictionary<(string? Form, string? Msa, string? Type), Resolved> _morphs = new();

    /// <summary>Reads the ordered morphemes and their canonical identities from a single parser Reading.</summary>
    public ReadingSignature Read(ParseAnalysis reading) => Capture(reading).Signature!;

    /// <summary>
    /// Reads a parser Reading only when every morpheme is verified against the project. Returns null for a guessed
    /// or unverifiable morpheme, or when there are no morphemes, so no identity is invented.
    /// </summary>
    public ReadingSignature? ReadVerified(ParseAnalysis reading) => CaptureVerified(reading).Signature;

    /// <summary>
    /// Freezes lexicon evidence for every morpheme but keeps a Signature only when the whole Reading is verified.
    /// An unverifiable Reading keeps a null Signature and a non-null <see cref="ParseAnalysis.MorphemeAcceptance"/>,
    /// so later capture does not guess an identity for it. An identity the reading already carries is kept.
    /// </summary>
    public ParseAnalysis CaptureVerified(ParseAnalysis reading)
    {
        var captured = Capture(reading);
        var verified = reading.Morphs.Count > 0 && reading.Morphs.All(morph => morph.GuessedString is null &&
            Resolve(morph).Verified);
        return verified ? captured : captured with { Signature = reading.Signature };
    }

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

    /// <summary>Reads a stored analysis in native bundle order, including the sense selected on each bundle.</summary>
    public static ReadingSignature? Read(IWfiAnalysis analysis)
    {
        if (analysis.MorphBundlesOS.Count == 0) return null;
        var morphemes = ImmutableArray.CreateBuilder<ReadingMorpheme>(analysis.MorphBundlesOS.Count);
        foreach (var bundle in analysis.MorphBundlesOS)
        {
            var resolved = ReadMorpheme(bundle.MorphRA, bundle.MsaRA, bundle.InflTypeRA, bundle.SenseRA,
                Id(bundle.MorphRA), Id(bundle.MsaRA), Id(bundle.InflTypeRA), bundle.InflTypeRA is not null);
            if (!resolved.Verified) return null;
            morphemes.Add(resolved.Morph);
        }
        return new ReadingSignature(morphemes.MoveToImmutable());
    }

    private Resolved Resolve(ParseMorph morph)
    {
        var key = (morph.Form, morph.Msa, morph.InflType);
        if (_morphs.TryGetValue(key, out var existing)) return existing;
        var result = ReadMorpheme(Find<IMoForm>(morph.Form), Find<IMoMorphSynAnalysis>(morph.Msa),
            Find<ILexEntryInflType>(morph.InflType), null, Id(morph.Form), Id(morph.Msa), Id(morph.InflType),
            morph.InflType is not null);
        _morphs.Add(key, result);
        return result;
    }

    private T? Find<T>(string? id) where T : class, ICmObject =>
        _objects is not null && Guid.TryParse(id, out var guid) && guid != Guid.Empty && _objects.TryGetObject(guid, out var obj)
            ? obj as T : null;

    private static Resolved ReadMorpheme(IMoForm? form, IMoMorphSynAnalysis? msa, ILexEntryInflType? inflType,
        ILexSense? explicitSense, string? formId, string? msaId, string? inflTypeId, bool inflTypeRequested)
    {
        var entry = form?.Owner as ILexEntry;
        var lexicalEntry = msa?.Owner as ILexEntry;
        var verified = entry is not null && lexicalEntry is not null && (!inflTypeRequested || inflType is not null);

        // A variant's allomorph and grammatical info may have different owners.
        ICmObject[] components = [];
        if (verified)
        {
            components = entry == lexicalEntry
                ? [lexicalEntry!]
                : [.. entry!.EntryRefsOS.Where(reference => reference.RefType == LexEntryRefTags.krtVariant &&
                        (inflType is null || reference.VariantEntryTypesRS.Contains(inflType)))
                    .SelectMany(reference => reference.ComponentLexemesRS)
                    .Where(component => component == lexicalEntry ||
                        component is ILexSense sense && lexicalEntry!.AllSenses.Contains(sense))];
            verified = components.Length > 0;
        }

        var senses = components.SelectMany<ICmObject, ILexSense>(component => component switch
            {
                ILexEntry target => target.AllSenses,
                ILexSense target => [target],
                _ => Enumerable.Empty<ILexSense>(),
            })
            .Where(sense => sense.MorphoSyntaxAnalysisRA == msa)
            .DistinctBy(sense => sense.Guid).ToArray();
        if (explicitSense is not null && !senses.Any(sense => sense.Guid == explicitSense.Guid)) verified = false;
        var senseId = verified ? Id(explicitSense ?? (senses.Length == 1 ? senses[0] : null)) : null;
        var candidates = verified && senseId is null && senses.Length > 1
            ? senses.Select(sense => CanonicalId.FromGuid(sense.Guid).Value).Order(StringComparer.Ordinal).ToImmutableArray() : [];

        var features = ImmutableArray.CreateBuilder<ReadingFeatureValue>();
        if (verified && msa is not null && !ReadFeatures(msa, features)) verified = false;
        var derivation = verified && msa is IMoDerivAffMsa deriv &&
            (deriv.FromPartOfSpeechRA is not null || deriv.ToPartOfSpeechRA is not null)
            ? new ReadingDerivation(Id(deriv.FromPartOfSpeechRA), Id(deriv.ToPartOfSpeechRA)) : null;
        var status = senseId is not null ? ReadingSenseStatus.Resolved :
            candidates.Length > 0 ? ReadingSenseStatus.Ambiguous : ReadingSenseStatus.Unresolved;
        var morph = new ReadingMorpheme(Id(entry ?? lexicalEntry), senseId, candidates, status,
            msaId, formId, inflTypeId, msa is IMoStemMsa, derivation, verified ? features.ToImmutable() : []);
        // Sense ambiguity does not stop a morpheme being lexicon-backed.
        return new Resolved(morph, verified, verified && form is not null && msa is not null &&
            status != ReadingSenseStatus.Unresolved);
    }

    private static bool ReadFeatures(IMoMorphSynAnalysis msa, ImmutableArray<ReadingFeatureValue>.Builder features) =>
        msa switch
        {
            IMoStemMsa stem => ReadStructure(stem.MsFeaturesOA, ReadingFeatureRole.Stem, [], features),
            IMoInflAffMsa infl => ReadStructure(infl.InflFeatsOA, ReadingFeatureRole.Inflection, [], features),
            IMoDerivAffMsa deriv => ReadStructure(deriv.FromMsFeaturesOA, ReadingFeatureRole.DerivationInput, [], features) &&
                ReadStructure(deriv.ToMsFeaturesOA, ReadingFeatureRole.DerivationOutput, [], features),
            IMoDerivStepMsa step => ReadStructure(step.MsFeaturesOA, ReadingFeatureRole.DerivationalStep, [], features),
            _ => true,
        };

    private static bool ReadStructure(IFsFeatStruc? structure, ReadingFeatureRole role,
        ImmutableArray<string> prefix, ImmutableArray<ReadingFeatureValue>.Builder features)
    {
        if (structure is null) return true;
        if (structure.FeatureDisjunctionsOC.Count > 0) return false;
        foreach (var specification in structure.FeatureSpecsOC)
        {
            if (Id(specification.FeatureRA) is not { } feature) return false;
            var path = prefix.Add(feature);
            switch (specification)
            {
                case IFsClosedValue { ValueRA: { } symbolic } when
                    symbolic.Owner == specification.FeatureRA && Id(symbolic) is { } value:
                    features.Add(new ReadingFeatureValue(role, path, [value]));
                    break;
                case IFsComplexValue { ValueOA: IFsFeatStruc nested } when ReadStructure(nested, role, path, features):
                    break;
                default:
                    return false;
            }
        }
        return true;
    }

    private static string? Id(string? id) => Guid.TryParse(id, out var guid) && guid != Guid.Empty
        ? CanonicalId.FromGuid(guid).Value : null;

    private static string? Id(ICmObject? item) =>
        item is not null && item.Guid != Guid.Empty ? CanonicalId.FromGuid(item.Guid).Value : null;

    private sealed record Resolved(ReadingMorpheme Morph, bool Verified, bool LexiconBacked);
}

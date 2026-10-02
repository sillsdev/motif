using SIL.LCModel;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Projection;

/// <summary>
/// Checks an inspector subject against an already-open project before anything is read about it: every id the
/// subject gives must name an object of the right kind, and together they must name one object. Only then does it
/// become the ref the facts reader takes, so the reader never fills a supplied id that was not found and never
/// joins one entry's allomorph to another entry's grammatical info. It only reads.
/// </summary>
public static class InspectorSubjectResolver
{
    /// <summary>
    /// The resolution of <paramref name="subject"/> in <paramref name="cache"/>, and the ref that names exactly the
    /// ids it gave when it resolves; the ref is <see langword="null"/> for any other resolution.
    /// </summary>
    public static (InspectorResolution Resolution, ObjectUseRef? Ref) Resolve(LcmCache cache, InspectorSubject subject)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(subject);
        return subject.Kind switch
        {
            InspectorSubjectKind.Morpheme => Morpheme(cache, subject),
            InspectorSubjectKind.Rule => Rule(cache, subject),
            _ => (InspectorResolution.Unsupported, null),
        };
    }

    /// <summary>
    /// Validates every supplied ref constraint together. Missing supplied objects are unresolved; omitted parts
    /// may be inferred by the facts reader only after this validation. Labels never participate.
    /// </summary>
    public static (InspectorResolution Resolution, ObjectUseRef? Ref) Resolve(LcmCache cache, ObjectUseRef reference)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(reference);
        if (!Given(reference.AllomorphId) || !Given(reference.GrammaticalInfoId))
            return (InspectorResolution.NotAuthored, null);
        var form = Find(cache, reference.AllomorphId);
        var msa = Find(cache, reference.GrammaticalInfoId);
        if (reference.AllomorphId is not null && form is not IMoForm ||
            reference.GrammaticalInfoId is not null && msa is not IMoMorphSynAnalysis)
            return (InspectorResolution.NotInBaseline, null);
        var hasTiming = reference.TimingKind is not null || reference.TimingKey is not null;
        var timed = hasTiming ? Find(cache, reference.TimingKey) : null;
        if (hasTiming && (reference.TimingIdentityQuality != "authored" || !Given(reference.TimingKey)))
            return (InspectorResolution.NotAuthored, null);
        if (hasTiming && (timed is null || !Fits(reference.TimingKind, timed)))
            return (InspectorResolution.NotInBaseline, null);
        var owners = new[] { form?.Owner as ILexEntry, msa?.Owner as ILexEntry,
            timed as ILexEntry ?? timed?.Owner as ILexEntry }.OfType<ILexEntry>().Distinct().ToArray();
        if (owners.Length > 1 || msa is not null && timed is IMoMorphSynAnalysis && msa != timed ||
            (form is not null || msa is not null) && hasTiming && timed is not (ILexEntry or IMoMorphSynAnalysis))
            return (InspectorResolution.Contradictory, null);
        return form is null && msa is null && timed is null ? (InspectorResolution.NotInBaseline, null)
            : (InspectorResolution.Resolved, reference);
    }

    private static bool Fits(string? kind, ICmObject found) => kind switch
    {
        "phon_rule" => found is IPhSegmentRule,
        "morph_rule" => found is IMoCompoundRule || found is IMoMorphSynAnalysis and not IMoStemMsa,
        "lex_entry" => found is ILexEntry,
        _ => false,
    };

    private static (InspectorResolution, ObjectUseRef?) Morpheme(LcmCache cache, InspectorSubject subject)
    {
        if (subject.AllomorphId is null && subject.GrammaticalInfoId is null) return (InspectorResolution.NotInBaseline, null);
        if (!Given(subject.AllomorphId) || !Given(subject.GrammaticalInfoId)) return (InspectorResolution.NotAuthored, null);
        return Resolve(cache, new ObjectUseRef
        {
            AllomorphId = subject.AllomorphId,
            GrammaticalInfoId = subject.GrammaticalInfoId,
            Label = subject.Label,
            Gloss = subject.Gloss,
        });
    }

    private static (InspectorResolution, ObjectUseRef?) Rule(LcmCache cache, InspectorSubject subject)
    {
        if (subject.TimingKey is not { } key) return (InspectorResolution.NotInBaseline, null);
        if (subject.IdentityQuality != "authored" || !Guid.TryParse(key.Key, out _)) return (InspectorResolution.NotAuthored, null);
        return Resolve(cache, ObjectUseRef.ForTimingKey(key with { IdentityQuality = subject.IdentityQuality }, subject.Label));
    }

    // A null id is one the subject left out; any other must at least be a GUID to be looked up.
    private static bool Given(string? id) => id is null || Guid.TryParse(id, out _);

    private static ICmObject? Find(LcmCache cache, string? id) =>
        Guid.TryParse(id, out var guid) &&
        cache.ServiceLocator.GetInstance<ICmObjectRepository>().TryGetObject(guid, out var found)
            ? found : null;
}

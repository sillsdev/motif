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

    private static (InspectorResolution, ObjectUseRef?) Morpheme(LcmCache cache, InspectorSubject subject)
    {
        if (subject.AllomorphId is null && subject.GrammaticalInfoId is null) return (InspectorResolution.NotInBaseline, null);
        if (!Given(subject.AllomorphId) || !Given(subject.GrammaticalInfoId)) return (InspectorResolution.NotAuthored, null);
        var form = Find(cache, subject.AllomorphId);
        var msa = Find(cache, subject.GrammaticalInfoId);
        if (subject.AllomorphId is not null && form is not IMoForm ||
            subject.GrammaticalInfoId is not null && msa is not IMoMorphSynAnalysis)
            return (InspectorResolution.NotInBaseline, null);
        if (form is not null && msa is not null && form.Owner != msa.Owner) return (InspectorResolution.Contradictory, null);
        return (InspectorResolution.Resolved, new ObjectUseRef
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
        var found = Find(cache, key.Key);
        if (found is null) return (InspectorResolution.NotInBaseline, null);
        var fits = key.Kind switch
        {
            "phon_rule" => found is IPhSegmentRule,
            "morph_rule" => found is IMoCompoundRule || found is IMoMorphSynAnalysis and not IMoStemMsa,
            "lex_entry" => found is ILexEntry,
            _ => false,
        };
        return fits
            ? (InspectorResolution.Resolved, new ObjectUseRef { TimingKind = key.Kind, TimingKey = key.Key, Label = subject.Label })
            : (InspectorResolution.NotInBaseline, null);
    }

    // A null id is one the subject left out; any other must at least be a GUID to be looked up.
    private static bool Given(string? id) => id is null || Guid.TryParse(id, out _);

    private static ICmObject? Find(LcmCache cache, string? id) =>
        Guid.TryParse(id, out var guid) &&
        cache.ServiceLocator.GetInstance<ICmObjectRepository>().TryGetObject(guid, out var found)
            ? found : null;
}

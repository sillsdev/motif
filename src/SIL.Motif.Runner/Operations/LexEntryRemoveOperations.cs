using System.Runtime.CompilerServices;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Snapshot;

namespace SIL.Motif.Runner.Operations;

/// <summary>The operation kind that deletes one whole dictionary entry that nothing outside it refers to.</summary>
public static class LexEntryRemoveOperationKinds
{
    /// <summary>Deletes the targeted entry with its forms, senses, grammatical info and entry references.</summary>
    public const string Remove = "lexical/lexEntry/remove";

#pragma warning disable CA2255 // Load-time registration makes this kind available before parsing and dispatch.
    [ModuleInitializer]
    internal static void Register()
    {
        OperationKindRegistry.Register(Remove);
        OperationHandlerRegistry.Register(Remove, LexEntryRemoveHandler.Instance);
        OperationSlotRegistry.RegisterMany(Remove, operation =>
        {
            var entry = OperationSlotRegistry.Target(operation);
            var payload = LexEntryRemovePayload.Parse(OperationSlotRegistry.After(operation));
            return
            [
                new OperationSlot(entry, null),
                new OperationSlot(payload.Lexicon, SnapshotFields.LexDbEntries, OperationSlotRegistry.MemberKey(entry)),
            ];
        });
    }
#pragma warning restore CA2255
}

/// <summary>The closed payload of an entry deletion.</summary>
/// <param name="Lexicon">The project's lexicon, which lists the entry; a different one makes the Draft stale.</param>
public sealed record LexEntryRemovePayload(CanonicalId Lexicon)
{
    /// <summary>Parses the closed payload, refusing any other property.</summary>
    public static LexEntryRemovePayload Parse(JsonElement after)
    {
        const string kind = LexEntryRemoveOperationKinds.Remove;
        ClosedPayloadParsing.RequireObject(after, kind);
        ClosedPayloadParsing.RejectUnknownProperties(after, ["lexicon"], kind);
        return new LexEntryRemovePayload(
            CanonicalId.FromGuid(ClosedPayloadParsing.GetRequiredCanonicalId(after, "lexicon", kind).ToGuid()));
    }
}

/// <summary>
/// Decides whether an entry may be deleted. LibLCM deletes everything the entry owns and quietly unlinks every
/// reference to it, so the entry is refused while any object outside it refers to the entry or to anything it owns:
/// a variant or complex-form entry reference, a lexical relation, a morph bundle of a parser analysis, an ad hoc
/// prohibition, a reversal entry, or a pending-sense link.
/// </summary>
internal static class LexEntryRemoval
{
    private const string Kind = LexEntryRemoveOperationKinds.Remove;
    private const int NamedReferrers = 5;

    /// <summary>The id of the project's lexicon, which lists every entry.</summary>
    internal static CanonicalId LexiconId(LcmCache cache) => CanonicalId.FromGuid(cache.LangProject.LexDbOA.Guid);

    /// <summary>Throws, naming the referrers, when something outside the entry refers to it or to an object it owns.</summary>
    internal static void Inspect(ILexEntry entry)
    {
        var inside = new HashSet<Guid>(entry.AllOwnedObjects.Select(item => item.Guid)) { entry.Guid };
        var outside = new List<(ICmObject Owned, ICmObject Referrer)>();
        foreach (var owned in entry.AllOwnedObjects.Prepend(entry))
        foreach (var referrer in owned.ReferringObjects)
            if (!inside.Contains(referrer.Guid) && !IsInside(referrer, inside))
                outside.Add((owned, referrer));
        if (outside.Count == 0) return;
        var named = string.Join("; ", outside.Take(NamedReferrers).Select(pair =>
            $"{SenseRemoval.Describe(pair.Referrer)} refers to {SenseRemoval.Describe(pair.Owned)}"));
        var more = outside.Count > NamedReferrers ? $"; and {outside.Count - NamedReferrers} more" : "";
        throw new InvalidOperationException(
            $"'{Kind}' refuses the entry '{entry.HeadWord?.Text}' ({CanonicalId.FromGuid(entry.Guid).Value}) because " +
            $"{named}{more}. Retarget or remove each reference first.");
    }

    private static bool IsInside(ICmObject item, HashSet<Guid> inside)
    {
        for (var owner = item.Owner; owner is not null; owner = owner.Owner)
            if (inside.Contains(owner.Guid))
                return true;
        return false;
    }

    /// <summary>The recorded before-state: what the entry held, so a reviewer sees exactly what goes.</summary>
    internal static JsonElement Preview(ILexEntry entry) => JsonSerializer.SerializeToElement(new
    {
        deletedEntry = new
        {
            id = CanonicalId.FromGuid(entry.Guid).Value,
            headword = entry.HeadWord?.Text,
            lexemeForm = entry.LexemeFormOA is { } lexeme ? Form(lexeme) : null,
            alternateForms = entry.AlternateFormsOS.Select(Form).ToArray(),
            senses = entry.AllSenses.Select(sense => new
            {
                id = CanonicalId.FromGuid(sense.Guid).Value,
                gloss = RemovalPreview.AllWritingSystems(sense.Cache, sense.Gloss),
                definition = RemovalPreview.AllWritingSystems(sense.Cache, sense.Definition),
                analysis = sense.MorphoSyntaxAnalysisRA is { } msa ? CanonicalId.FromGuid(msa.Guid).Value : null,
                examples = sense.ExamplesOS.Count,
            }).ToArray(),
            analyses = entry.MorphoSyntaxAnalysesOC.Select(msa => new
            {
                id = CanonicalId.FromGuid(msa.Guid).Value,
                @class = msa.ClassName,
                partOfSpeech = VariantSenseAnalysisSafety.PartOfSpeech(msa) is { } pos
                    ? pos.Name.BestAnalysisAlternative?.Text : null,
            }).ToArray(),
            entryReferences = entry.EntryRefsOS.Count,
        },
        reversal = "createEntries with the same id, then recreate the lexeme form, alternate forms, senses and analyses with " +
            "the ids and content listed here",
    });

    private static object Form(IMoForm form) => new
    {
        id = CanonicalId.FromGuid(form.Guid).Value,
        @class = form.ClassName,
        forms = RemovalPreview.AllWritingSystems(form.Cache, form.Form),
        morphType = form.MorphTypeRA?.Name.BestAnalysisAlternative?.Text,
    };
}

/// <summary>Deletes an entry that nothing outside it refers to, recording what goes in the Dry Run preview.</summary>
internal sealed class LexEntryRemoveHandler : IOperationHandler
{
    internal static readonly LexEntryRemoveHandler Instance = new();
    private const string Kind = LexEntryRemoveOperationKinds.Remove;

    private LexEntryRemoveHandler()
    {
    }

    public ExpectedEffect ApplyAndCaptureEffect(
        LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        var (id, entry) = Resolve(cache, operation);
        LexEntryRemoval.Inspect(entry);
        var lexicon = LexEntryRemoval.LexiconId(cache);
        var before = Membership(cache, id);
        var preview = LexEntryRemoval.Preview(entry);
        touchedTargets.Add(id);
        touchedTargets.AddRange(entry.AllOwnedObjects.Select(item => CanonicalId.FromGuid(item.Guid)));
        entry.Delete();
        return new ExpectedEffect(lexicon, SnapshotFields.LexDbEntries, before, Membership(cache, id), preview);
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation)
    {
        var (id, entry) = Resolve(cache, operation);
        var current = Membership(cache, id);
        return new ExpectedEffect(LexEntryRemoval.LexiconId(cache), SnapshotFields.LexDbEntries, current, current,
            LexEntryRemoval.Preview(entry));
    }

    private static (CanonicalId Id, ILexEntry Entry) Resolve(LcmCache cache, OperationEnvelope operation)
    {
        var payload = LexEntryRemovePayload.Parse(
            operation.After ?? throw new ContractParseException($"'{Kind}' operation requires 'after'."));
        if (payload.Lexicon != LexEntryRemoval.LexiconId(cache))
            throw new InvalidOperationException($"'{Kind}' names a lexicon this project does not have; recompute the Draft.");
        return TargetResolution.Resolve<ILexEntry>(cache, operation, Kind);
    }

    private static Dictionary<string, string> Membership(LcmCache cache, CanonicalId id) =>
        cache.LangProject.LexDbOA.Entries.Any(entry => entry.Guid == id.ToGuid())
            ? new(StringComparer.Ordinal) { ["ref"] = id.Value } : new(StringComparer.Ordinal);
}

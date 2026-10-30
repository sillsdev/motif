using System.Runtime.CompilerServices;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Execution;

namespace SIL.Motif.Runner.Operations;

/// <summary>Names the creation of an ad hoc prohibition owned by the project's morphology data.</summary>
public static class AdhocProhibitionCreateOperationKinds
{
    public const string Create = "grammar/moMorphData/createAdhocProhibition";

#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void Register()
    {
        OperationKindRegistry.Register(Create);
        OperationHandlerRegistry.Register(Create, AdhocProhibitionCreateHandler.Instance);
        OperationSlotRegistry.RegisterOwningCreate(Create, SnapshotFields.MoMorphDataAdhocCoProhibitions, false);
    }
#pragma warning restore CA2255
}

/// <summary>The closed <c>after</c> payload: the kind, the first morpheme or allomorph, the rest in order and the adjacency.</summary>
public sealed record AdhocProhibitionPayload(string Kind, CanonicalId First, IReadOnlyList<CanonicalId> Rest, string Adjacency)
{
    public const string Morpheme = "morpheme";
    public const string Allomorph = "allomorph";

    /// <summary>The adjacency names in FieldWorks' Adjacency list order; the position is the stored integer.</summary>
    public static readonly IReadOnlyList<string> Adjacencies =
        ["anywhere", "somewhere-to-right", "somewhere-to-left", "adjacent-to-right", "adjacent-to-left"];

    public static AdhocProhibitionPayload Parse(JsonElement after, string kind)
    {
        ClosedPayloadParsing.RequireObject(after, kind);
        ClosedPayloadParsing.RejectUnknownProperties(after, ["kind", "first", "rest", "adjacency"], kind);
        var prohibitionKind = ClosedPayloadParsing.GetRequiredString(after, "kind", kind);
        var adjacency = ClosedPayloadParsing.GetRequiredString(after, "adjacency", kind);
        if (prohibitionKind is not (Morpheme or Allomorph)) throw new InvalidOperationException($"'{kind}': kind must be morpheme or allomorph.");
        if (!Adjacencies.Contains(adjacency)) throw new InvalidOperationException($"'{kind}': adjacency must be one of {string.Join(", ", Adjacencies)}.");
        if (!after.TryGetProperty("rest", out var rest) || rest.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"'{kind}': rest must be an array of ids.");
        var members = rest.EnumerateArray().Select(item =>
            item.ValueKind == JsonValueKind.String && CanonicalId.TryParse(item.GetString(), out var id)
                ? id : throw new InvalidOperationException($"'{kind}': rest must contain canonical id strings.")).ToArray();
        return new(prohibitionKind, ClosedPayloadParsing.GetRequiredCanonicalId(after, "first", kind), members, adjacency);
    }
}

/// <summary>Checks, describes and builds an ad hoc prohibition the way FieldWorks' two prohibition classes hold it.</summary>
public static class AdhocProhibition
{
    /// <summary>Resolves the named objects, refusing unknown ones, an empty rest and a duplicate.</summary>
    public static (ICmObject First, IReadOnlyList<ICmObject> Others) Check(LcmCache cache, AdhocProhibitionPayload payload)
    {
        var morphData = cache.LangProject.MorphologicalDataOA ?? throw new AdhocRefusal(
            RefusalCodes.AdhocProhibitionNoMorphologyData, "The project has no morphology data to own an ad hoc prohibition.");
        if (payload.Rest.Count == 0)
            throw new InvalidOperationException("An ad hoc prohibition needs at least one morpheme or allomorph in the rest, as FieldWorks requires.");
        if (payload.Rest.Contains(payload.First))
            throw new AdhocRefusal(RefusalCodes.AdhocProhibitionSelf,
                "The first morpheme or allomorph must not also be in the rest: the parser would ban it from every word, or ignore the rule.");
        var first = Resolve(cache, payload, payload.First);
        var rest = payload.Rest.Select(id => Resolve(cache, payload, id)).ToArray();
        if (morphData.AdhocCoProhibitionsOC.Any(existing => Same(existing, payload, first, rest)))
            throw new AdhocRefusal(RefusalCodes.AdhocProhibitionDuplicate,
                $"An ad hoc prohibition already exists: {Describe(payload, first, rest)}.");
        return (first, rest);
    }

    /// <summary>Creates the prohibition with the authored identity, enabled, inside the caller's unit of work.</summary>
    public static void Create(LcmCache cache, MoMorphDataHolder owner, Guid guid, AdhocProhibitionPayload payload,
        ICmObject first, IReadOnlyList<ICmObject> rest)
    {
        var adjacency = AdhocProhibitionPayload.Adjacencies.ToList().IndexOf(payload.Adjacency);
        if (payload.Kind == AdhocProhibitionPayload.Morpheme)
        {
            var created = cache.ServiceLocator.GetInstance<IMoMorphAdhocProhibFactory>().Create(guid);
            owner.Data.AdhocCoProhibitionsOC.Add(created);
            created.FirstMorphemeRA = (IMoMorphSynAnalysis)first;
            foreach (var item in rest) created.RestOfMorphsRS.Add((IMoMorphSynAnalysis)item);
            created.Adjacency = adjacency;
            created.Disabled = false;
        }
        else
        {
            var created = cache.ServiceLocator.GetInstance<IMoAlloAdhocProhibFactory>().Create(guid);
            owner.Data.AdhocCoProhibitionsOC.Add(created);
            created.FirstAllomorphRA = (IMoForm)first;
            foreach (var item in rest) created.RestOfAllosRS.Add((IMoForm)item);
            created.Adjacency = adjacency;
            created.Disabled = false;
        }
    }

    /// <summary>The plain statement of what is prohibited next to what, for example
    /// "morpheme 'pa-' cannot occur in the same word as 'bago'".</summary>
    public static string Describe(AdhocProhibitionPayload payload, ICmObject first, IReadOnlyList<ICmObject> rest)
    {
        var noun = payload.Kind == AdhocProhibitionPayload.Morpheme ? "morpheme" : "allomorph";
        var relation = payload.Adjacency switch
        {
            "anywhere" => "in the same word as",
            "somewhere-to-right" => "somewhere to the right of",
            "somewhere-to-left" => "somewhere to the left of",
            "adjacent-to-right" => "immediately to the right of",
            _ => "immediately to the left of",
        };
        var others = string.Join(" and ", rest.Select(Label));
        var side = payload.Adjacency switch
        {
            "somewhere-to-right" or "adjacent-to-right" => " (the first comes after the rest in the word)",
            "somewhere-to-left" or "adjacent-to-left" => " (the first comes before the rest in the word)",
            _ => "",
        };
        return $"{noun} '{Label(first)}' cannot occur {relation} {others}{side}";
    }

    /// <summary>
    /// A sentence saying the rule can never match, or null. The adjacency says where the first sits relative to the
    /// rest, so a prefix as the first with an adjacency to its right, or a suffix with one to its left, points away
    /// from every stem and suffix it is paired with. Nothing is warned when a rest member could lie on that side
    /// (another affix of the same side) or when a morph type is not clearly a prefix, a suffix or a stem.
    /// </summary>
    public static string? DirectionWarning(AdhocProhibitionPayload payload, ICmObject first, IReadOnlyList<ICmObject> rest)
    {
        var toRight = payload.Adjacency is "somewhere-to-right" or "adjacent-to-right";
        var toLeft = payload.Adjacency is "somewhere-to-left" or "adjacent-to-left";
        var firstSide = SideOf(first);
        var impossible = firstSide switch
        {
            MorphSide.Prefix when toRight => rest.All(item => SideOf(item) is MorphSide.Stem or MorphSide.Suffix),
            MorphSide.Suffix when toLeft => rest.All(item => SideOf(item) is MorphSide.Stem or MorphSide.Prefix),
            _ => false,
        };
        if (!impossible) return null;
        var correct = payload.Adjacency.Replace(toRight ? "right" : "left", toRight ? "left" : "right", StringComparison.Ordinal);
        return $"The first is a {(firstSide == MorphSide.Prefix ? "prefix" : "suffix")}, which never comes " +
               $"{(toRight ? "after" : "before")} the rest in a word, so this rule can never match. " +
               $"Adjacency names where the first sits relative to the rest; for this pair use anywhere or {correct}.";
    }

    private enum MorphSide { Other, Prefix, Suffix, Stem }

    private static MorphSide SideOf(ICmObject item)
    {
        var type = item is IMoForm form ? form.MorphTypeRA : item.OwnerOfClass<ILexEntry>()?.LexemeFormOA?.MorphTypeRA;
        if (type is null) return MorphSide.Other;
        var guid = type.Guid;
        if (guid == MoMorphTypeTags.kguidMorphPrefix || guid == MoMorphTypeTags.kguidMorphProclitic
            || guid == MoMorphTypeTags.kguidMorphPrefixingInterfix) return MorphSide.Prefix;
        if (guid == MoMorphTypeTags.kguidMorphSuffix || guid == MoMorphTypeTags.kguidMorphEnclitic
            || guid == MoMorphTypeTags.kguidMorphSuffixingInterfix) return MorphSide.Suffix;
        if (guid == MoMorphTypeTags.kguidMorphStem || guid == MoMorphTypeTags.kguidMorphRoot
            || guid == MoMorphTypeTags.kguidMorphBoundStem || guid == MoMorphTypeTags.kguidMorphBoundRoot) return MorphSide.Stem;
        return MorphSide.Other;
    }

    private static string Label(ICmObject item)
    {
        string? form = item is IMoForm moForm
            ? moForm.Form.BestVernacularAlternative?.Text
            : item.OwnerOfClass<ILexEntry>()?.HeadWord?.Text;
        return string.IsNullOrEmpty(form) ? CanonicalId.FromGuid(item.Guid).Value : form;
    }

    private static ICmObject Resolve(LcmCache cache, AdhocProhibitionPayload payload, CanonicalId id)
    {
        const string kind = AdhocProhibitionCreateOperationKinds.Create;
        try
        {
            return payload.Kind == AdhocProhibitionPayload.Morpheme
                ? ReferenceFieldLowering.Resolve<IMoMorphSynAnalysis>(cache, id, kind)
                : ReferenceFieldLowering.Resolve<IMoForm>(cache, id, kind);
        }
        catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException or ArgumentException)
        {
            throw payload.Kind == AdhocProhibitionPayload.Morpheme
                ? new AdhocRefusal(RefusalCodes.AdhocProhibitionInvalidMorpheme,
                    $"'{id.Value}' is not a morpheme (a grammatical analysis of an entry).")
                : new AdhocRefusal(RefusalCodes.AdhocProhibitionInvalidAllomorph,
                    $"'{id.Value}' is not an allomorph (a form of an entry).");
        }
    }

    private static bool Same(IMoAdhocProhib existing, AdhocProhibitionPayload payload, ICmObject first, IReadOnlyList<ICmObject> rest)
    {
        ICmObject? existingFirst;
        IReadOnlyList<ICmObject> existingRest;
        switch (existing)
        {
            case IMoMorphAdhocProhib morph when payload.Kind == AdhocProhibitionPayload.Morpheme:
                existingFirst = morph.FirstMorphemeRA;
                existingRest = morph.RestOfMorphsRS.Cast<ICmObject>().ToArray();
                break;
            case IMoAlloAdhocProhib allo when payload.Kind == AdhocProhibitionPayload.Allomorph:
                existingFirst = allo.FirstAllomorphRA;
                existingRest = allo.RestOfAllosRS.Cast<ICmObject>().ToArray();
                break;
            default:
                return false;
        }
        if (existing.Adjacency != AdhocProhibitionPayload.Adjacencies.ToList().IndexOf(payload.Adjacency) ||
            existingFirst?.Guid != first.Guid || existingRest.Count != rest.Count) return false;
        // Only a non-Anywhere prohibition reads its rest as a left-to-right order.
        var left = existingRest.Select(item => item.Guid);
        var right = rest.Select(item => item.Guid);
        return payload.Adjacency == "anywhere" ? left.Order().SequenceEqual(right.Order()) : left.SequenceEqual(right);
    }
}

/// <summary>Holds the morphology data so the create helper takes one typed owner.</summary>
public readonly record struct MoMorphDataHolder(IMoMorphData Data);

internal sealed class AdhocRefusal : ProposalRefusalException
{
    internal AdhocRefusal(string code, string message)
        : base(code, FailureReason.Refused, message, new Dictionary<string, string>()) { }
}

internal sealed class AdhocProhibitionCreateHandler : IOperationHandler
{
    internal static readonly AdhocProhibitionCreateHandler Instance = new();
    private const string Kind = AdhocProhibitionCreateOperationKinds.Create;

    public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        if (operation.StorageIdOverride is not null)
            throw new InvalidOperationException($"'{Kind}': storage GUID overrides are not supported for this create.");
        var entityId = operation.EntityId ?? throw new InvalidOperationException($"'{Kind}' requires entityId.");
        var payload = AdhocProhibitionPayload.Parse(OperationSlotRegistry.After(operation), Kind);
        var (id, data) = TargetResolution.Resolve<IMoMorphData>(cache, operation, Kind);
        CreateIdentity.RequireFresh(cache, entityId, Kind,
            payload.Kind == AdhocProhibitionPayload.Morpheme ? "MoMorphAdhocProhib" : "MoAlloAdhocProhib");
        var (first, rest) = AdhocProhibition.Check(cache, payload);
        var before = Read(data, entityId);
        touchedTargets.Add(id);
        AdhocProhibition.Create(cache, new(data), entityId.ToGuid(), payload, first, rest);
        return new ExpectedEffect(id, SnapshotFields.MoMorphDataAdhocCoProhibitions, before, Read(data, entityId),
            Preview(payload, first, rest));
    }

    /// <summary>Reads only whether the prohibition's identity exists: the named morphemes and allomorphs may be created
    /// by earlier operations of the same Proposal, so they cannot be resolved or labelled before it runs.</summary>
    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation)
    {
        var entityId = operation.EntityId ?? throw new InvalidOperationException($"'{Kind}' requires entityId.");
        AdhocProhibitionPayload.Parse(OperationSlotRegistry.After(operation), Kind);
        var (id, data) = TargetResolution.Resolve<IMoMorphData>(cache, operation, Kind);
        var current = Read(data, entityId);
        if (!data.AdhocCoProhibitionsOC.Any(item => item.Guid == entityId.ToGuid()))
            CreateIdentity.AddCollision(cache, entityId, null, current);
        return new ExpectedEffect(id, SnapshotFields.MoMorphDataAdhocCoProhibitions, current, current);
    }

    private static JsonElement Preview(AdhocProhibitionPayload payload, ICmObject first, IReadOnlyList<ICmObject> rest) =>
        JsonSerializer.SerializeToElement(new
        {
            prohibits = AdhocProhibition.Describe(payload, first, rest),
            warning = AdhocProhibition.DirectionWarning(payload, first, rest),
            kind = payload.Kind,
            adjacency = payload.Adjacency,
            first = payload.First.Value,
            rest = payload.Rest.Select(id => id.Value).ToArray(),
        }, OmitNull);

    private static readonly JsonSerializerOptions OmitNull = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    private static Dictionary<string, string> Read(IMoMorphData data, CanonicalId entityId) =>
        data.AdhocCoProhibitionsOC.Any(item => item.Guid == entityId.ToGuid())
            ? new(StringComparer.Ordinal) { ["ref"] = entityId.Value } : new(StringComparer.Ordinal);
}

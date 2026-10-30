using System.Runtime.CompilerServices;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Execution;
using SIL.Motif.Runner.Snapshotting;

namespace SIL.Motif.Runner.Operations;

/// <summary>Names the guarded exact-set edit of a clitic stem MSA's "Attaches to Categories".</summary>
public static class CliticAttachmentOperationKinds
{
    public const string Set = "grammar/moStemMsa/editFromPartsOfSpeech";
#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void Register()
    {
        OperationKindRegistry.Register(Set);
        OperationHandlerRegistry.Register(Set, CliticAttachmentHandler.Instance);
        OperationSlotRegistry.RegisterMany(Set, CliticAttachmentEdit.Slots);
    }
#pragma warning restore CA2255
}

/// <summary>The checks shared by composing and applying; each refusal is catalogued.</summary>
internal static class CliticAttachmentEdit
{
    internal static IReadOnlyList<OperationSlot> Slots(OperationEnvelope operation)
    {
        var (expected, desired) = ParseSets(OperationSlotRegistry.After(operation));
        return expected.Except(desired).Concat(desired.Except(expected))
            .Select(member => new OperationSlot(OperationSlotRegistry.Target(operation),
                SnapshotFields.MoStemMsaFromPartsOfSpeech, OperationSlotRegistry.MemberKey(member))).ToArray();
    }

    internal static (CanonicalId[] Expected, CanonicalId[] Desired) ParseSets(JsonElement after)
    {
        ClosedPayloadParsing.RequireObject(after, CliticAttachmentOperationKinds.Set);
        ClosedPayloadParsing.RejectUnknownProperties(after, ["expectedCategories", "categories"], CliticAttachmentOperationKinds.Set);
        return (Set(after, "expectedCategories"), Set(after, "categories"));
    }

    private static CanonicalId[] Set(JsonElement after, string name)
    {
        if (!after.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            throw new ContractParseException($"'{CliticAttachmentOperationKinds.Set}' operation 'after.{name}' is required and must be an array.");
        var ids = value.EnumerateArray().Select(item =>
            item.ValueKind == JsonValueKind.String && CanonicalId.TryParse(item.GetString(), out var id)
                ? id
                : throw new ContractParseException($"'{name}' must contain canonical id strings.")).ToArray();
        return EditCliticAttachmentIntentParser.NormalizeSet(ids);
    }

    /// <summary>Returns the current set after the clitic, category and expected-set checks, each with its own catalogued code.</summary>
    internal static CanonicalId[] Check(LcmCache cache, IMoStemMsa msa, IReadOnlyList<CanonicalId> expected,
        IReadOnlyList<CanonicalId> desired)
    {
        var id = CanonicalId.FromGuid(msa.Guid);
        var morphType = (msa.Owner as ILexEntry)?.LexemeFormOA?.MorphTypeRA?.Guid;
        if (morphType != MoMorphTypeTags.kguidMorphClitic && morphType != MoMorphTypeTags.kguidMorphProclitic &&
            morphType != MoMorphTypeTags.kguidMorphEnclitic)
            throw new CliticAttachmentRefusal(RefusalCodes.CliticAttachmentNotClitic,
                "The target must be a stem analysis of an entry whose form is a proclitic, enclitic or clitic.",
                ("target", id.Value), ("morphType", morphType is { } type ? CanonicalId.FromGuid(type).Value : ""));
        var repository = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
        foreach (var category in expected.Concat(desired).Distinct())
            if (!repository.IsValidObjectId(category.ToGuid()) || repository.GetObject(category.ToGuid()) is not IPartOfSpeech)
                throw new CliticAttachmentRefusal(RefusalCodes.CliticAttachmentInvalidCategory,
                    "Every category must be a part of speech in this project.", ("category", category.Value));
        var current = EditCliticAttachmentIntentParser.NormalizeSet(
            msa.FromPartsOfSpeechRC.Select(item => CanonicalId.FromGuid(item.Guid)).ToArray());
        if (!current.SequenceEqual(expected))
            throw new CliticAttachmentRefusal(RefusalCodes.CliticAttachmentStaleSource,
                "The clitic's Attaches to Categories changed; compose the edit again.", ("target", id.Value),
                ("expectedCategories", string.Join(",", expected.Select(item => item.Value))),
                ("actualCategories", string.Join(",", current.Select(item => item.Value))));
        return current;
    }

    internal static string[] Names(LcmCache cache, IEnumerable<CanonicalId> categories) =>
        categories.Select(category => Name(cache, category)).Order(StringComparer.CurrentCulture).ToArray();

    private static string Name(LcmCache cache, CanonicalId category) =>
        (cache.ServiceLocator.GetInstance<ICmObjectRepository>().GetObject(category.ToGuid()) as IPartOfSpeech)
            ?.Name.BestAnalysisAlternative?.Text is { Length: > 0 } name ? name : category.Value;
}

internal sealed class CliticAttachmentRefusal : ProposalRefusalException
{
    internal CliticAttachmentRefusal(string code, string message, params (string Key, string Value)[] facts)
        : base(code, FailureReason.Refused, message, facts.ToDictionary(item => item.Key, item => item.Value)) { }
}

internal sealed class CliticAttachmentHandler : IOperationHandler
{
    internal static readonly CliticAttachmentHandler Instance = new();

    public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        var (id, msa) = TargetResolution.Resolve<IMoStemMsa>(cache, operation, CliticAttachmentOperationKinds.Set);
        var (expected, desired) = CliticAttachmentEdit.ParseSets(OperationSlotRegistry.After(operation));
        var current = CliticAttachmentEdit.Check(cache, msa, expected, desired);
        var before = ReferenceCollectionFieldSnapshotting.ReadAlternatives(msa.FromPartsOfSpeechRC);
        var preview = JsonSerializer.SerializeToElement(new
        {
            beforeCategoryNames = CliticAttachmentEdit.Names(cache, current),
            afterCategoryNames = CliticAttachmentEdit.Names(cache, desired),
        });
        touchedTargets.Add(id);
        foreach (var member in current.Except(desired))
            MoStemMsaFromPartsOfSpeechRemoveRefLowering.Apply(cache, msa, member);
        foreach (var member in desired.Except(current))
            MoStemMsaFromPartsOfSpeechAddRefLowering.Apply(cache, msa, member);
        return new(id, SnapshotFields.MoStemMsaFromPartsOfSpeech, before,
            ReferenceCollectionFieldSnapshotting.ReadAlternatives(msa.FromPartsOfSpeechRC), preview);
    }

    /// <summary>
    /// Reads only the target's current set. The clitic and category checks run when the operation applies,
    /// because an earlier step in the same Draft may be what makes the form a clitic.
    /// </summary>
    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation)
    {
        var (id, msa) = TargetResolution.Resolve<IMoStemMsa>(cache, operation, CliticAttachmentOperationKinds.Set);
        var current = ReferenceCollectionFieldSnapshotting.ReadAlternatives(msa.FromPartsOfSpeechRC);
        return new(id, SnapshotFields.MoStemMsaFromPartsOfSpeech, current, current);
    }
}

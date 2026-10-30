using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Names a clitic stem MSA, its current "Attaches to Categories" set, and the exact set it should have.</summary>
public sealed record EditCliticAttachmentIntent(CanonicalId Target,
    IReadOnlyList<CanonicalId> ExpectedCategories, IReadOnlyList<CanonicalId> Categories);

/// <summary>Reads a closed clitic-attachment edit; both sets are required and may be empty.</summary>
public static class EditCliticAttachmentIntentParser
{
    public static EditCliticAttachmentIntent Parse(JsonElement input)
    {
        SoundSystemIntentParsing.Object(input, "EditCliticAttachment", "target", "expectedCategories", "categories");
        return new(LexicalFormAuthoring.Normalize(SoundSystemIntentParsing.Id(input, "target")),
            NormalizeSet(SoundSystemIntentParsing.Ids(input, "expectedCategories", false)),
            NormalizeSet(SoundSystemIntentParsing.Ids(input, "categories", false)));
    }

    internal static CanonicalId[] NormalizeSet(IReadOnlyList<CanonicalId> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var normalized = ids.Select(LexicalFormAuthoring.Normalize).ToArray();
        if (normalized.Distinct().Count() != normalized.Length)
            throw new ContractParseException("A category set must not repeat a decoded identity.");
        return normalized.OrderBy(id => id.Value, StringComparer.Ordinal).ToArray();
    }
}

/// <summary>Stages an exact replacement of a clitic stem MSA's "Attaches to Categories".</summary>
public static class EditCliticAttachmentComposer
{
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, EditCliticAttachmentIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var expected = EditCliticAttachmentIntentParser.NormalizeSet(intent.ExpectedCategories);
        var desired = EditCliticAttachmentIntentParser.NormalizeSet(intent.Categories);
        var target = LexicalFormAuthoring.Normalize(intent.Target);
        var msa = LexicalFormAuthoring.Resolve<IMoStemMsa>(cache, target, "EditCliticAttachment");
        var current = CliticAttachmentEdit.Check(cache, msa, expected, desired);
        if (current.SequenceEqual(desired)) return [];
        return [new(CanonicalId.Mint(), CliticAttachmentOperationKinds.Set, target: target,
            after: JsonSerializer.SerializeToElement(new
            {
                expectedCategories = expected.Select(id => id.Value).ToArray(),
                categories = desired.Select(id => id.Value).ToArray(),
            }),
            rationale: "Set the clitic's Attaches to Categories to an exact set.")];
    }
}

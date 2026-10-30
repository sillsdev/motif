using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Names an existing entry, the entry or sense it is a variant of, and the variant type.</summary>
public sealed record MakeVariantOfIntent(CanonicalId Entry, CanonicalId Component, CanonicalId VariantType);

/// <summary>Reads a closed make-variant-of intent.</summary>
public static class MakeVariantOfIntentParser
{
    public static MakeVariantOfIntent Parse(JsonElement input)
    {
        SoundSystemIntentParsing.Object(input, "MakeVariantOf", "entry", "component", "variantType");
        return new(SoundSystemIntentParsing.Id(input, "entry"), SoundSystemIntentParsing.Id(input, "component"),
            SoundSystemIntentParsing.Id(input, "variantType"));
    }
}

/// <summary>Stages the creation of a variant reference that makes one entry a variant of another entry or sense.</summary>
public static class MakeVariantOfComposer
{
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, MakeVariantOfIntent intent, Func<CanonicalId>? mintId = null)
    {
        var entry = VariantLinkSafety.ResolveObject<ILexEntry>(cache, intent.Entry, RefusalCodes.VariantInvalidEntry,
            "The entry to make a variant must be an entry.");
        var (component, _) = VariantLinkSafety.CheckLink(cache, entry, intent.Component, intent.VariantType);
        VariantLinkSafety.RequireNotLinked(entry, component);
        var mint = mintId ?? (() => CanonicalId.Mint());
        return [new(mint(), VariantRefCreateOperationKinds.Create, target: intent.Entry, entityId: mint(),
            after: JsonSerializer.SerializeToElement(new
            {
                component = intent.Component.Value,
                variantType = intent.VariantType.Value,
            }), rationale: "Make the entry a variant of its main entry or sense, with the variant type.")];
    }
}

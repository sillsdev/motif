using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Names a variant reference, its main entry or sense, and its variant type.</summary>
public sealed record LinkVariantIntent(CanonicalId Target, CanonicalId Component, CanonicalId VariantType);

/// <summary>Reads a closed variant-link intent.</summary>
public static class LinkVariantIntentParser
{
    public static LinkVariantIntent Parse(JsonElement input)
    {
        SoundSystemIntentParsing.Object(input, "LinkVariant", "target", "component", "variantType");
        return new(SoundSystemIntentParsing.Id(input, "target"), SoundSystemIntentParsing.Id(input, "component"),
            SoundSystemIntentParsing.Id(input, "variantType"));
    }
}

/// <summary>Stages a checked replacement of the component and type of an existing variant reference.</summary>
public static class LinkVariantComposer
{
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, LinkVariantIntent intent)
    {
        var (reference, _, _) = VariantLinkSafety.Resolve(cache, intent.Target, intent.Component, intent.VariantType);
        return [new(CanonicalId.Mint(), VariantLinkOperationKinds.Set, target: intent.Target,
            after: JsonSerializer.SerializeToElement(new
            {
                component = intent.Component.Value,
                variantType = intent.VariantType.Value,
                expectedComponents = reference.ComponentLexemesRS.Select(x => CanonicalId.FromGuid(x.Guid).Value).ToArray(),
                expectedTypes = reference.VariantEntryTypesRS.Select(x => CanonicalId.FromGuid(x.Guid).Value).ToArray(),
            }), rationale: "Link the variant reference to its main entry or sense and variant type.")];
    }
}

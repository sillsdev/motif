using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Names an existing entry, the entry or sense it is a variant of, and the variant type.</summary>
/// <param name="Entry">The entry that becomes a variant.</param>
/// <param name="Component">The main entry or sense.</param>
/// <param name="VariantType">A type in the project's variant types list.</param>
/// <param name="HideMinorEntry">Hides the variant's minor entry in dictionary views; false shows it.</param>
public sealed record MakeVariantOfIntent(
    CanonicalId Entry, CanonicalId Component, CanonicalId VariantType, bool HideMinorEntry = false);

/// <summary>Reads a closed make-variant-of intent.</summary>
public static class MakeVariantOfIntentParser
{
    public static MakeVariantOfIntent Parse(JsonElement input)
    {
        SoundSystemIntentParsing.Object(input, "MakeVariantOf", "entry", "component", "variantType", "hideMinorEntry");
        return new(SoundSystemIntentParsing.Id(input, "entry"), SoundSystemIntentParsing.Id(input, "component"),
            SoundSystemIntentParsing.Id(input, "variantType"), VariantIntentFlags.Optional(input, "hideMinorEntry"));
    }
}

/// <summary>Reads an optional true-or-false property; absent means false.</summary>
internal static class VariantIntentFlags
{
    /// <summary>The closed payload of a variant-reference create; hideMinorEntry is written only when true.</summary>
    internal static JsonElement RefPayload(CanonicalId component, CanonicalId variantType, bool hideMinorEntry) =>
        JsonSerializer.SerializeToElement(hideMinorEntry
            ? new { component = component.Value, variantType = variantType.Value, hideMinorEntry = (bool?)true }
            : (object)new { component = component.Value, variantType = variantType.Value });

    internal static bool Optional(JsonElement input, string name) =>
        !input.TryGetProperty(name, out var flag) ? false
            : flag.ValueKind is JsonValueKind.True or JsonValueKind.False ? flag.GetBoolean()
            : throw new Contract.Parsing.ContractParseException($"'{name}' must be true or false.");
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
            after: VariantIntentFlags.RefPayload(intent.Component, intent.VariantType, intent.HideMinorEntry),
            rationale: "Make the entry a variant of its main entry or sense, with the variant type.")];
    }
}

using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>One entry and the main entry or sense it becomes a variant of.</summary>
public sealed record VariantLinkItem(CanonicalId Entry, CanonicalId Component);

/// <summary>Names many entries, the main entry or sense of each, and the one variant type they all take.</summary>
/// <param name="VariantType">A type in the project's variant types list.</param>
/// <param name="Links">Each entry that becomes a variant, with its main entry or sense.</param>
/// <param name="OnlyStubs">
/// When set, an entry any of whose senses has content is refused, so a bulk conversion cannot turn a real
/// entry into a variant by mistake.
/// </param>
public sealed record MakeVariantsOfIntent(CanonicalId VariantType, IReadOnlyList<VariantLinkItem> Links, bool OnlyStubs);

/// <summary>Reads a closed bulk make-variants-of intent.</summary>
public static class MakeVariantsOfIntentParser
{
    public static MakeVariantsOfIntent Parse(JsonElement input)
    {
        SoundSystemIntentParsing.Object(input, "MakeVariantsOf", "variantType", "links", "onlyStubs");
        var links = SoundSystemIntentParsing.Array(input, "links");
        if (links.Length == 0) throw new Contract.Parsing.ContractParseException("'links' needs at least one entry.");
        var items = links.Select(link =>
        {
            SoundSystemIntentParsing.Object(link, "MakeVariantsOf link", "entry", "component");
            return new VariantLinkItem(SoundSystemIntentParsing.Id(link, "entry"), SoundSystemIntentParsing.Id(link, "component"));
        }).ToArray();
        var onlyStubs = false;
        if (input.TryGetProperty("onlyStubs", out var flag))
            onlyStubs = flag.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? flag.GetBoolean()
                : throw new Contract.Parsing.ContractParseException("'onlyStubs' must be true or false.");
        return new(SoundSystemIntentParsing.Id(input, "variantType"), items, onlyStubs);
    }
}

/// <summary>
/// Stages a variant reference for each listed entry, checking each link against the project and against the
/// links staged before it, so the whole list is accepted or refused with the offending link named.
/// </summary>
public static class MakeVariantsOfComposer
{
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, MakeVariantsOfIntent intent, Func<CanonicalId>? mintId = null)
    {
        var mint = mintId ?? (() => CanonicalId.Mint());
        var staged = new Dictionary<Guid, List<Guid>>();
        var seen = new HashSet<(Guid, Guid)>();
        var operations = new List<OperationEnvelope>();
        for (var index = 0; index < intent.Links.Count; index++)
        {
            var link = intent.Links[index];
            try
            {
                var entry = VariantLinkSafety.ResolveObject<ILexEntry>(cache, link.Entry, RefusalCodes.VariantInvalidEntry,
                    "The entry to make a variant must be an entry.");
                var (component, _) = VariantLinkSafety.CheckLink(cache, entry, link.Component, intent.VariantType, staged);
                VariantLinkSafety.RequireNotLinked(entry, component);
                if (!seen.Add((entry.Guid, component.Guid)))
                    throw VariantLinkSafety.Refuse(RefusalCodes.VariantAlreadyLinked,
                        $"{SenseRemoval.Describe(entry)} is linked to {SenseRemoval.Describe(component)} twice in this list.");
                if (intent.OnlyStubs)
                    foreach (var sense in entry.AllSenses)
                        if (LexSenseEmptiness.FirstPopulatedField(cache, sense) is { } field)
                            throw VariantLinkSafety.Refuse(RefusalCodes.VariantInvalidEntry,
                                $"{SenseRemoval.Describe(entry)} is not a stub: {SenseRemoval.Describe(sense)} has content in " +
                                $"'{field}'. Leave 'onlyStubs' out to convert it anyway.");
                var destination = component is ILexEntry main ? main : (ICmObject)component.OwnerOfClass<ILexEntry>();
                if (!staged.TryGetValue(entry.Guid, out var targets)) staged[entry.Guid] = targets = [];
                targets.Add(destination.Guid);
                operations.Add(new(mint(), VariantRefCreateOperationKinds.Create, target: link.Entry, entityId: mint(),
                    after: JsonSerializer.SerializeToElement(new
                    {
                        component = link.Component.Value,
                        variantType = intent.VariantType.Value,
                    }), rationale: "Make the entry a variant of its main entry or sense, with the variant type."));
            }
            catch (VariantLinkRefusalException exception)
            {
                throw VariantLinkSafety.Refuse(exception.Code,
                    $"Link {index} (entry {link.Entry.Value}, component {link.Component.Value}): {exception.Message}");
            }
        }
        return operations;
    }
}

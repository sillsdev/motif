using System;
using System.Collections.Generic;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>How many times one affix rule may apply to a word, and the count the project must hold now.</summary>
/// <param name="Msa">The id of the affix's morphosyntactic analysis (inflectional, derivational or unclassified).</param>
/// <param name="ExpectedMaxApps">The count as the project holds it when the Draft is composed; 1 when none is set.</param>
/// <param name="MaxApps">The count to set, 1 to <see cref="AffixMaxApps.Cap"/>; 1 removes the setting.</param>
public sealed record SetAffixMaxAppsIntent(CanonicalId Msa, int ExpectedMaxApps, int MaxApps);

/// <summary>Parses the closed JSON intent for setting an affix repeat count.</summary>
public static class SetAffixMaxAppsIntentParser
{
    private const string ConstructName = "SetAffixMaxApps";

    /// <summary>Reads the three required properties; any other property is refused.</summary>
    public static SetAffixMaxAppsIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, ConstructName, "msa", "expectedMaxApps", "maxApps");
        if (!authored.TryGetProperty("msa", out var msa) || msa.ValueKind != JsonValueKind.String ||
            !CanonicalId.TryParse(msa.GetString(), out var id))
            throw new ContractParseException($"'{ConstructName}': 'msa' is required and must be a canonical id string.");
        return new SetAffixMaxAppsIntent(id, Integer(authored, "expectedMaxApps"), Integer(authored, "maxApps"));
    }

    private static int Integer(JsonElement authored, string propertyName)
    {
        if (!authored.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt32(out var number))
            throw new ContractParseException($"'{ConstructName}': '{propertyName}' is required and must be an integer.");
        return number;
    }
}

/// <summary>Builds one operation that sets an affix repeat count in the project's ParserParameters without changing the cache.</summary>
public static class SetAffixMaxAppsComposer
{
    private const string ConstructName = "SetAffixMaxApps";
    private const string Rationale =
        "Authored by the SetAffixMaxApps composer: the parser applies an affix rule once per word unless the project gives " +
        "that affix a higher repeat count, so an affix the language repeats (a stacked extension, a doubled converb) needs one.";

    /// <summary>Builds the one write for the project's morphology data.</summary>
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, SetAffixMaxAppsIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);

        AffixMaxApps.RequireCount(intent.MaxApps);
        var msa = AffixMaxApps.ResolveAffixMsa(cache, intent.Msa);
        var morphData = cache.LangProject.MorphologicalDataOA;
        int current;
        try
        {
            AffixMaxAppsParserParameters.RequireRoot(morphData.ParserParameters);
            current = AffixMaxAppsParserParameters.Read(morphData.ParserParameters, msa.Guid);
        }
        catch (System.Xml.XmlException error)
        {
            throw new InvalidOperationException(
                $"'{ConstructName}': the project's ParserParameters is not well-formed XML ({error.Message}); fix it in FieldWorks first.");
        }
        catch (FormatException error)
        {
            throw new InvalidOperationException(
                $"'{ConstructName}': the project's maxApps for this affix is not a whole number, which the parser cannot read either ({error.Message}); fix it in FieldWorks first.");
        }
        if (current != intent.ExpectedMaxApps)
            throw new AffixMaxAppsRefusal(RefusalCodes.AffixMaxAppsStale,
                $"'{ConstructName}': the project has maxApps={current} for '{intent.Msa.Value}'; the intent expected maxApps={intent.ExpectedMaxApps}.");

        return [new OperationEnvelope(
            operationId: CanonicalId.Mint(),
            kind: MoMorphDataAffixMaxAppsOperationKinds.Set,
            target: CanonicalId.FromGuid(morphData.Guid),
            after: JsonSerializer.SerializeToElement(new { msa = intent.Msa.Value, maxApps = intent.MaxApps }),
            rationale: Rationale)];
    }
}

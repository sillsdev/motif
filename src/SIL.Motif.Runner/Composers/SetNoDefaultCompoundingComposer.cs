using System;
using System.Collections.Generic;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Whether the parser's built-in default compounding is to be off, and the value the project must hold now.</summary>
/// <param name="ExpectedNoDefaultCompounding">The flag as the project holds it when the Draft is composed.</param>
/// <param name="NoDefaultCompounding">True to turn the built-in default compounding off, false to turn it on.</param>
public sealed record SetNoDefaultCompoundingIntent(bool ExpectedNoDefaultCompounding, bool NoDefaultCompounding);

/// <summary>Parses the closed JSON intent for turning default compounding off or on.</summary>
public static class SetNoDefaultCompoundingIntentParser
{
    private const string ConstructName = "SetNoDefaultCompounding";

    /// <summary>Reads both required Boolean values; any other property is refused.</summary>
    public static SetNoDefaultCompoundingIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, ConstructName, "expectedNoDefaultCompounding", "noDefaultCompounding");
        return new SetNoDefaultCompoundingIntent(
            RequiredBoolean(authored, "expectedNoDefaultCompounding"),
            RequiredBoolean(authored, "noDefaultCompounding"));
    }

    private static bool RequiredBoolean(JsonElement authored, string propertyName)
    {
        if (!authored.TryGetProperty(propertyName, out var value) ||
            value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new ContractParseException(
                $"'{ConstructName}': '{propertyName}' is required and must be a Boolean.");
        return value.GetBoolean();
    }
}

/// <summary>Builds one operation that sets NoDefaultCompounding in the project's ParserParameters without changing the cache.</summary>
public static class SetNoDefaultCompoundingComposer
{
    private const string ConstructName = "SetNoDefaultCompounding";
    private const string Rationale =
        "Authored by the SetNoDefaultCompounding composer: the parser's built-in default compounding joins any two stems, " +
        "so a project with no compound rules of its own turns it off to stop over-accepting words.";

    /// <summary>Builds the one write for the project's morphology data.</summary>
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, SetNoDefaultCompoundingIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);

        var morphData = cache.LangProject.MorphologicalDataOA;
        bool current;
        try { current = NoDefaultCompoundingParserParameters.Read(morphData.ParserParameters); }
        catch (System.Xml.XmlException error)
        {
            throw new InvalidOperationException(
                $"'{ConstructName}': the project's ParserParameters is not well-formed XML ({error.Message}); fix it in FieldWorks first.");
        }
        if (current != intent.ExpectedNoDefaultCompounding)
            throw new InvalidOperationException(
                $"'{ConstructName}': the project has NoDefaultCompounding={current}; " +
                $"the intent expected NoDefaultCompounding={intent.ExpectedNoDefaultCompounding}.");

        return [new OperationEnvelope(
            operationId: CanonicalId.Mint(),
            kind: MoMorphDataNoDefaultCompoundingOperationKinds.Set,
            target: CanonicalId.FromGuid(morphData.Guid),
            after: JsonSerializer.SerializeToElement(new { value = intent.NoDefaultCompounding }),
            rationale: Rationale)];
    }
}

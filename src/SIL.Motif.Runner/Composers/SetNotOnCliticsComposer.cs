using System;
using System.Collections.Generic;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Whether the parser's phonological rules are to stay off the clitic stratum, and the value the project must hold now.</summary>
/// <param name="ExpectedNotOnClitics">The flag as the project holds it when the Draft is composed.</param>
/// <param name="NotOnClitics">True to run phonological rules on the word before a clitic attaches, false to run them on the clitic stratum too.</param>
public sealed record SetNotOnCliticsIntent(bool ExpectedNotOnClitics, bool NotOnClitics);

/// <summary>Parses the closed JSON intent for setting where phonological rules run relative to clitics.</summary>
public static class SetNotOnCliticsIntentParser
{
    private const string ConstructName = "SetNotOnClitics";

    /// <summary>Reads both required Boolean values; any other property is refused.</summary>
    public static SetNotOnCliticsIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, ConstructName, "expectedNotOnClitics", "notOnClitics");
        return new SetNotOnCliticsIntent(
            RequiredBoolean(authored, "expectedNotOnClitics"),
            RequiredBoolean(authored, "notOnClitics"));
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

/// <summary>Builds one operation that sets NotOnClitics in the project's ParserParameters without changing the cache.</summary>
public static class SetNotOnCliticsComposer
{
    private const string ConstructName = "SetNotOnClitics";
    private const string Rationale =
        "Authored by the SetNotOnClitics composer: the parser runs phonological rules on the word before a clitic attaches " +
        "unless this is false, in which case they also run on the word with its clitic attached.";

    /// <summary>Builds the one write for the project's morphology data.</summary>
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, SetNotOnCliticsIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);

        var morphData = cache.LangProject.MorphologicalDataOA;
        bool current;
        try { current = NotOnCliticsParserParameters.Read(morphData.ParserParameters); }
        catch (System.Xml.XmlException error)
        {
            throw new InvalidOperationException(
                $"'{ConstructName}': the project's ParserParameters is not well-formed XML ({error.Message}); fix it in FieldWorks first.");
        }
        catch (FormatException error)
        {
            throw new InvalidOperationException(
                $"'{ConstructName}': the project's NotOnClitics value is not 0, 1, true or false, which FieldWorks cannot read either ({error.Message}); fix it in FieldWorks first.");
        }
        if (current != intent.ExpectedNotOnClitics)
            throw new InvalidOperationException(
                $"'{ConstructName}': the project has NotOnClitics={current}; " +
                $"the intent expected NotOnClitics={intent.ExpectedNotOnClitics}.");

        return [new OperationEnvelope(
            operationId: CanonicalId.Mint(),
            kind: MoMorphDataNotOnCliticsOperationKinds.Set,
            target: CanonicalId.FromGuid(morphData.Guid),
            after: JsonSerializer.SerializeToElement(new { value = intent.NotOnClitics }),
            rationale: Rationale)];
    }
}

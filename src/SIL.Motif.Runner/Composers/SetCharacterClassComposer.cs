using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Which characters of which writing system are to be word-forming or punctuation.</summary>
/// <param name="WritingSystem">The tag of one of the project's writing systems, such as <c>ceb</c>.</param>
/// <param name="Class">The class every named character is to have.</param>
/// <param name="Characters">The characters to class, each one visible character, such as the two curly quotes.</param>
public sealed record SetCharacterClassIntent(string WritingSystem, WritingSystemCharacterClass Class, string Characters);

/// <summary>Parses the closed JSON intent for classing characters of a writing system.</summary>
public static class SetCharacterClassIntentParser
{
    private const string ConstructName = "SetCharacterClass";

    /// <summary>Reads the three required strings; any other property or class name is refused.</summary>
    public static SetCharacterClassIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, ConstructName, "ws", "class", "characters");
        var className = Required(authored, "class");
        var characterClass = className switch
        {
            "wordForming" => WritingSystemCharacterClass.WordForming,
            "punctuation" => WritingSystemCharacterClass.Punctuation,
            _ => throw new ContractParseException(
                $"'{ConstructName}': 'class' must be 'wordForming' or 'punctuation', not '{className}'."),
        };
        return new SetCharacterClassIntent(Required(authored, "ws"), characterClass, Required(authored, "characters"));
    }

    private static string Required(JsonElement authored, string propertyName)
    {
        if (!authored.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
            throw new ContractParseException($"'{ConstructName}': '{propertyName}' is required and must be a non-empty string.");
        return value.GetString()!;
    }
}

/// <summary>Builds one operation that classes characters of a writing system without changing the cache.</summary>
public static class SetCharacterClassComposer
{
    private const string ConstructName = "SetCharacterClass";
    private const string Rationale =
        "Authored by the SetCharacterClass composer: FieldWorks treats a character as part of a word when the writing " +
        "system's main character set lists it or Unicode calls it a letter, mark or modifier; punctuation is what remains.";

    /// <summary>Builds the one write for the project's root, refusing a writing system or character that cannot be classed.</summary>
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, SetCharacterClassIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);

        var characters = intent.Characters.EnumerateRunes().Select(rune => rune.ToString()).Distinct(StringComparer.Ordinal).ToArray();
        var after = new { ws = intent.WritingSystem, @class = WritingSystemCharacterClassPayload.Name(intent.Class), characters };
        var payload = WritingSystemCharacterClassPayload.Parse(JsonSerializer.SerializeToElement(after));
        WritingSystemCharacterClassHandler.Find(cache, payload.WritingSystem);
        WritingSystemCharacterClassHandler.RequireChangeable(payload);

        return [new OperationEnvelope(
            operationId: CanonicalId.Mint(),
            kind: LangProjectWritingSystemOperationKinds.SetCharacterClass,
            target: CanonicalId.FromGuid(cache.LangProject.Guid),
            after: JsonSerializer.SerializeToElement(after),
            rationale: Rationale)];
    }
}

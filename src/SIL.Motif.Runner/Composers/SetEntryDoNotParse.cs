using System;
using System.Collections.Generic;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Names an entry and the expected and requested values of its do-not-parse flag.</summary>
public sealed record SetEntryDoNotParseIntent(CanonicalId Target, bool ExpectedDoNotParse, bool DoNotParse);

/// <summary>Reads a closed intent for an entry's do-not-parse flag.</summary>
public static class SetEntryDoNotParseIntentParser
{
    /// <summary>Requires the target and both Boolean values.</summary>
    public static SetEntryDoNotParseIntent Parse(JsonElement input)
    {
        SoundSystemIntentParsing.Object(input, "SetEntryDoNotParse", "target", "expectedDoNotParse", "doNotParse");
        return new(SoundSystemIntentParsing.Id(input, "target"), Boolean(input, "expectedDoNotParse"),
            Boolean(input, "doNotParse"));
    }

    private static bool Boolean(JsonElement input, string name)
    {
        if (!input.TryGetProperty(name, out var value) || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new ContractParseException($"'SetEntryDoNotParse': '{name}' is required and must be a Boolean.");
        return value.GetBoolean();
    }
}

/// <summary>Builds a write to the entry's FieldWorks do-not-parse flag without changing the cache.</summary>
public static class SetEntryDoNotParseComposer
{
    /// <summary>Checks the current flag and returns one semantic operation for the requested value.</summary>
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, SetEntryDoNotParseIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var target = LexicalFormAuthoring.Resolve<ICmObject>(cache, intent.Target, "SetEntryDoNotParse");
        if (target is not ILexEntry entry)
            throw new InvalidOperationException("The target must be a lexical entry.");
        if (entry.DoNotUseForParsing != intent.ExpectedDoNotParse)
            throw new InvalidOperationException(
                $"The entry has DoNotUseForParsing={entry.DoNotUseForParsing}; expected {intent.ExpectedDoNotParse}.");
        return [new(CanonicalId.Mint(), LexEntryDoNotUseForParsingOperationKinds.SetDoNotUseForParsing,
            target: intent.Target, after: JsonSerializer.SerializeToElement(new { value = intent.DoNotParse }),
            rationale: "Set the lexical entry's FieldWorks do-not-parse flag.")];
    }
}

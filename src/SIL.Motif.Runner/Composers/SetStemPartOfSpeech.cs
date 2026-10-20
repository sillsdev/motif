using System;
using System.Collections.Generic;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Sets the part of speech of one stem MSA; null for the expected value denotes none today.</summary>
public sealed record SetStemPartOfSpeechIntent(CanonicalId Target, CanonicalId? ExpectedPartOfSpeech,
    CanonicalId PartOfSpeech);

/// <summary>Parses a closed stem part-of-speech edit with a required nullable expected value and a required new value.</summary>
public static class SetStemPartOfSpeechIntentParser
{
    public static SetStemPartOfSpeechIntent Parse(JsonElement input)
    {
        SoundSystemIntentParsing.Object(input, "SetStemPartOfSpeech",
            "target", "expectedPartOfSpeech", "partOfSpeech");
        if (!input.TryGetProperty("expectedPartOfSpeech", out var expected))
            throw new ContractParseException("'expectedPartOfSpeech' is required and must be an identity or null.");
        return new(LexicalFormAuthoring.Normalize(SoundSystemIntentParsing.Id(input, "target")),
            expected.ValueKind == JsonValueKind.Null ? null
                : LexicalFormAuthoring.Normalize(SoundSystemIntentParsing.Id(input, "expectedPartOfSpeech")),
            LexicalFormAuthoring.Normalize(SoundSystemIntentParsing.Id(input, "partOfSpeech")));
    }
}

/// <summary>Sets a stem MSA's part of speech after checking the expected current value.</summary>
public static class SetStemPartOfSpeechComposer
{
    /// <summary>The rationale each operation carries; a Draft still needs its own Rationale to finalize.</summary>
    public const string OperationRationale =
        "Authored by the SetStemPartOfSpeech composer: the stem analysis takes the named part of speech.";

    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, SetStemPartOfSpeechIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        const string construct = "SetStemPartOfSpeech";
        var msa = LexicalFormAuthoring.Resolve<ICmObject>(cache, intent.Target, construct) as IMoStemMsa
            ?? throw new InvalidOperationException("The target must be a stem analysis.");
        var category = LexicalFormAuthoring.Resolve<IPartOfSpeech>(cache, intent.PartOfSpeech, construct);
        if (msa.PartOfSpeechRA?.Guid != intent.ExpectedPartOfSpeech?.ToGuid())
            throw new InvalidOperationException(
                "The stem analysis's part of speech differs from the expected part of speech.");
        if (msa.InflectionClassRA is { } cls &&
            !LexicalFormAuthoring.InCategoryOrAncestor(InflectionAuthoring.OwningCategory(cls), category))
            throw new InvalidOperationException(
                "The stem analysis's inflection class does not belong to the new part of speech or one of its ancestors.");
        if (msa.PartOfSpeechRA?.Guid == category.Guid) return [];
        return [new(CanonicalId.Mint(), MoStemMsaPartOfSpeechOperationKinds.SetPartOfSpeech,
            target: LexicalFormAuthoring.Normalize(intent.Target),
            after: JsonSerializer.SerializeToElement(new { @ref = LexicalFormAuthoring.Normalize(intent.PartOfSpeech).Value }),
            rationale: OperationRationale)];
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
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
        if (msa.PartOfSpeechRA?.Guid == category.Guid) return [];
        return [new(CanonicalId.Mint(), MoStemMsaPartOfSpeechOperationKinds.SetPartOfSpeech,
            target: LexicalFormAuthoring.Normalize(intent.Target),
            after: JsonSerializer.SerializeToElement(new { @ref = LexicalFormAuthoring.Normalize(intent.PartOfSpeech).Value }),
            rationale: OperationRationale + Losses(msa, category))];
    }

    /// <summary>Names the class and features LibLCM drops on the move.</summary>
    private static string Losses(IMoStemMsa msa, IPartOfSpeech category)
    {
        var lost = new List<string>();
        if (msa.PartOfSpeechRA is null) return "";
        if (msa.InflectionClassRA is { } cls && !category.AllInflectionClasses.Contains(cls))
            lost.Add("inflection class " + cls.ShortName);
        if (msa.MsFeaturesOA is { } structure)
            foreach (var spec in structure.FeatureSpecsOC.Where(spec => !Bears(category, spec.FeatureRA)))
                lost.Add("feature " + (spec.FeatureRA?.ShortName ?? "(none)"));
        return lost.Count == 0 ? "" : " Warning: the move drops " + string.Join(", ", lost) +
            ", which neither the new category nor its ancestors hold; moving the stem back does not restore them; make the feature bearable by the category or an ancestor first to keep it.";
    }

    private static bool Bears(IPartOfSpeech category, IFsFeatDefn? feature)
    {
        if (feature is null) return false;
        for (var current = category; current is not null; current = current.Owner as IPartOfSpeech)
            if (current.InflectableFeatsRC.Contains(feature)) return true;
        return false;
    }
}

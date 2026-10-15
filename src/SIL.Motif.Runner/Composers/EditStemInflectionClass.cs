using System;
using System.Collections.Generic;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Changes one stem MSA's inflection class; null denotes no class.</summary>
public sealed record EditStemInflectionClassIntent(CanonicalId Target, CanonicalId? ExpectedInflectionClass,
    CanonicalId? InflectionClass);

/// <summary>Parses a closed stem class edit with required nullable expected and desired identities.</summary>
public static class EditStemInflectionClassIntentParser
{
    public static EditStemInflectionClassIntent Parse(JsonElement input)
    {
        SoundSystemIntentParsing.Object(input, "EditStemInflectionClass",
            "target", "expectedInflectionClass", "inflectionClass");
        return new(LexicalFormAuthoring.Normalize(SoundSystemIntentParsing.Id(input, "target")),
            NullableId(input, "expectedInflectionClass"), NullableId(input, "inflectionClass"));
    }

    private static CanonicalId? NullableId(JsonElement input, string field)
    {
        if (!input.TryGetProperty(field, out var value))
            throw new ContractParseException($"'{field}' is required and must be an identity or null.");
        return value.ValueKind == JsonValueKind.Null ? null
            : LexicalFormAuthoring.Normalize(SoundSystemIntentParsing.Id(input, field));
    }
}

/// <summary>Sets or clears a stem MSA's class using classes owned by its part of speech or an ancestor.</summary>
public static class EditStemInflectionClassComposer
{
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, EditStemInflectionClassIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var msa = LexicalFormAuthoring.Resolve<ICmObject>(cache, intent.Target, "EditStemInflectionClass")
            as IMoStemMsa ?? throw new InvalidOperationException("The target must be a stem analysis.");
        var category = msa.PartOfSpeechRA
            ?? throw new InvalidOperationException("The stem analysis has no part of speech.");
        if (msa.InflectionClassRA?.Guid != intent.ExpectedInflectionClass?.ToGuid())
            throw new InvalidOperationException("The stem analysis's class differs from the expected inflection class.");
        if (intent.InflectionClass is { } desired)
        {
            var cls = LexicalFormAuthoring.Resolve<IMoInflClass>(cache, desired, "EditStemInflectionClass");
            if (!LexicalFormAuthoring.InCategoryOrAncestor(InflectionAuthoring.OwningCategory(cls), category))
                throw new InvalidOperationException("The class must belong to the part of speech or one of its ancestors.");
        }
        if (intent.ExpectedInflectionClass?.ToGuid() == intent.InflectionClass?.ToGuid()) return [];
        var kind = intent.InflectionClass is null
            ? MoStemMsaInflectionClassOperationKinds.ClearInflectionClass
            : MoStemMsaInflectionClassOperationKinds.SetInflectionClass;
        var after = intent.InflectionClass is { } clsId
            ? JsonSerializer.SerializeToElement(new { @ref = LexicalFormAuthoring.Normalize(clsId).Value })
            : JsonSerializer.SerializeToElement(new { });
        return [new(CanonicalId.Mint(), kind, target: LexicalFormAuthoring.Normalize(intent.Target), after: after,
            rationale: "Authored by the EditStemInflectionClass composer.")];
    }
}

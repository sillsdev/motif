using System;
using System.Collections.Generic;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Changes one category's explicit default; null denotes no explicit default, rather than an inherited class.</summary>
public sealed record EditPartOfSpeechInflectionClassDefaultIntent(CanonicalId Target,
    CanonicalId? ExpectedInflectionClass, CanonicalId? InflectionClass);

/// <summary>Parses a closed default edit with required nullable expected and desired identities.</summary>
public static class EditPartOfSpeechInflectionClassDefaultIntentParser
{
    public static EditPartOfSpeechInflectionClassDefaultIntent Parse(JsonElement input)
    {
        SoundSystemIntentParsing.Object(input, "EditPartOfSpeechInflectionClassDefault",
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

/// <summary>Sets or clears an explicit default using classes owned by the category or an ancestor.</summary>
public static class EditPartOfSpeechInflectionClassDefaultComposer
{
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, EditPartOfSpeechInflectionClassDefaultIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var category = InflectionAuthoring.Category(cache, intent.Target);
        if (category.DefaultInflectionClassRA?.Guid != intent.ExpectedInflectionClass?.ToGuid())
            throw new InvalidOperationException("The category's explicit default differs from the expected inflection class.");
        if (intent.InflectionClass is { } desired)
        {
            var cls = LexicalFormAuthoring.Resolve<IMoInflClass>(cache, desired, "EditPartOfSpeechInflectionClassDefault");
            if (!LexicalFormAuthoring.InCategoryOrAncestor(InflectionAuthoring.OwningCategory(cls), category))
                throw new InvalidOperationException("The default class must belong to the category or one of its ancestors.");
        }
        if (intent.ExpectedInflectionClass?.ToGuid() == intent.InflectionClass?.ToGuid()) return [];
        var kind = intent.InflectionClass is null
            ? PartOfSpeechDefaultInflectionClassOperationKinds.ClearDefaultInflectionClass
            : PartOfSpeechDefaultInflectionClassOperationKinds.SetDefaultInflectionClass;
        var after = intent.InflectionClass is { } clsId
            ? JsonSerializer.SerializeToElement(new { @ref = LexicalFormAuthoring.Normalize(clsId).Value })
            : JsonSerializer.SerializeToElement(new { });
        return [new(CanonicalId.Mint(), kind, target: LexicalFormAuthoring.Normalize(intent.Target), after: after,
            rationale: "Authored by the EditPartOfSpeechInflectionClassDefault composer.")];
    }
}

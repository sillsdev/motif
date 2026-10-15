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

/// <summary>Changes one stem allomorph's stem name; null denotes no stem name.</summary>
public sealed record EditStemAllomorphStemNameIntent(CanonicalId Target, CanonicalId? ExpectedStemName,
    CanonicalId? StemName);

/// <summary>Parses a closed stem-name edit with required nullable expected and desired identities.</summary>
public static class EditStemAllomorphStemNameIntentParser
{
    public static EditStemAllomorphStemNameIntent Parse(JsonElement input)
    {
        SoundSystemIntentParsing.Object(input, "EditStemAllomorphStemName", "target", "expectedStemName", "stemName");
        return new(LexicalFormAuthoring.Normalize(SoundSystemIntentParsing.Id(input, "target")),
            NullableId(input, "expectedStemName"), NullableId(input, "stemName"));
    }

    private static CanonicalId? NullableId(JsonElement input, string field)
    {
        if (!input.TryGetProperty(field, out var value))
            throw new ContractParseException($"'{field}' is required and must be an identity or null.");
        return value.ValueKind == JsonValueKind.Null ? null
            : LexicalFormAuthoring.Normalize(SoundSystemIntentParsing.Id(input, field));
    }
}

/// <summary>Sets or clears a stem allomorph's stem name using names of its entry's stem categories or ancestors.</summary>
public static class EditStemAllomorphStemNameComposer
{
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, EditStemAllomorphStemNameIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var allomorph = LexicalFormAuthoring.Resolve<ICmObject>(cache, intent.Target, "EditStemAllomorphStemName")
            as IMoStemAllomorph ?? throw new InvalidOperationException("The target must be a stem allomorph.");
        if (allomorph.StemNameRA?.Guid != intent.ExpectedStemName?.ToGuid())
            throw new InvalidOperationException("The allomorph's stem name differs from the expected stem name.");
        if (intent.StemName is { } desired)
        {
            var name = LexicalFormAuthoring.Resolve<ICmObject>(cache, desired, "EditStemAllomorphStemName")
                as IMoStemName ?? throw new InvalidOperationException("The desired stem name must be a stem name.");
            var categories = (allomorph.Owner as ILexEntry)?.MorphoSyntaxAnalysesOC.OfType<IMoStemMsa>()
                .Select(msa => msa.PartOfSpeechRA).OfType<IPartOfSpeech>().ToArray() ?? [];
            if (categories.Length == 0)
                throw new InvalidOperationException("The allomorph's entry has no stem analysis with a part of speech.");
            if (!categories.Any(category => LexicalFormAuthoring.InCategoryOrAncestor(name.Owner as IPartOfSpeech, category)))
                throw new InvalidOperationException(
                    "The stem name must belong to a stem category of the entry or one of its ancestors.");
        }
        if (intent.ExpectedStemName?.ToGuid() == intent.StemName?.ToGuid()) return [];
        var kind = intent.StemName is null
            ? MoStemAllomorphStemNameOperationKinds.ClearStemName
            : MoStemAllomorphStemNameOperationKinds.SetStemName;
        var after = intent.StemName is { } nameId
            ? JsonSerializer.SerializeToElement(new { @ref = LexicalFormAuthoring.Normalize(nameId).Value })
            : JsonSerializer.SerializeToElement(new { });
        return [new(CanonicalId.Mint(), kind, target: LexicalFormAuthoring.Normalize(intent.Target), after: after,
            rationale: "Authored by the EditStemAllomorphStemName composer.")];
    }
}

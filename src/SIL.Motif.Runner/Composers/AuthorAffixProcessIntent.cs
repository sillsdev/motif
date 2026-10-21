using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsing;

namespace SIL.Motif.Runner.Composers;

/// <summary>A named top-level match: one phoneme, one natural class, or the remaining stem sequence.</summary>
public sealed record AffixProcessInputPart(string Name, CanonicalId? Phoneme = null,
    CanonicalId? NaturalClass = null, bool Remaining = false);

/// <summary>One realization step: copy a named input part or insert a nonempty phoneme sequence.</summary>
public sealed record AffixProcessOutputStep(string? Copy = null, IReadOnlyList<CanonicalId>? Insert = null);

/// <summary>One new affix entry with an ordered flat stem pattern and a realization that may omit (delete) leading input parts.</summary>
public sealed record AuthorAffixProcessIntent(CanonicalId MorphType, string Gloss, string AnalysisWritingSystem,
    LexicalEntryMsa Msa, IReadOnlyList<AffixProcessInputPart> Input, IReadOnlyList<AffixProcessOutputStep> Output,
    string? Definition = null);

/// <summary>Parses the closed named-part affix-process intent, including the typed authoring limits.</summary>
public static class AuthorAffixProcessIntentParser
{
    public static AuthorAffixProcessIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, "AuthorAffixProcess",
            "morphType", "gloss", "analysisWs", "msa", "definition", "input", "output");
        var input = ParseInput(authored);
        var output = ParseOutput(authored);
        var result = new AuthorAffixProcessIntent(SoundSystemIntentParsing.Id(authored, "morphType"),
            SoundSystemIntentParsing.Text(authored, "gloss"), SoundSystemIntentParsing.Text(authored, "analysisWs"),
            AuthorLexicalEntryIntentParser.ParseMsa(authored, "AuthorAffixProcess"), input, output,
            authored.TryGetProperty("definition", out _) ? SoundSystemIntentParsing.Text(authored, "definition") : null);
        try { AffixProcessIntentShape.Validate(result); }
        catch (InvalidOperationException exception) { throw new ContractParseException(exception.Message); }
        return result;
    }

    internal static AffixProcessInputPart[] ParseInput(JsonElement authored)
    {
        return SoundSystemIntentParsing.Array(authored, "input").Select(part =>
        {
            SoundSystemIntentParsing.Object(part, "affix input", "name", "phoneme", "naturalClass", "remaining");
            if (part.TryGetProperty("remaining", out var remaining) && remaining.ValueKind != JsonValueKind.True)
                throw new ContractParseException("An input's remaining value must be true.");
            return new AffixProcessInputPart(SoundSystemIntentParsing.Text(part, "name"),
                part.TryGetProperty("phoneme", out _) ? SoundSystemIntentParsing.Id(part, "phoneme") : null,
                part.TryGetProperty("naturalClass", out _) ? SoundSystemIntentParsing.Id(part, "naturalClass") : null,
                part.TryGetProperty("remaining", out _));
        }).ToArray();
    }

    internal static AffixProcessOutputStep[] ParseOutput(JsonElement authored)
    {
        return SoundSystemIntentParsing.Array(authored, "output").Select(step =>
        {
            SoundSystemIntentParsing.Object(step, "affix output", "copy", "insert");
            return new AffixProcessOutputStep(
                step.TryGetProperty("copy", out _) ? SoundSystemIntentParsing.Text(step, "copy") : null,
                step.TryGetProperty("insert", out _) ? SoundSystemIntentParsing.Array(step, "insert").Select(phone =>
                    phone.ValueKind == JsonValueKind.String && CanonicalId.TryParse(phone.GetString(), out var id)
                        ? id : throw new ContractParseException("Inserted phonemes must be canonical identity strings.")).ToArray() : null);
        }).ToArray();
    }
}

internal static class AffixProcessIntentShape
{
    internal static void Validate(AuthorAffixProcessIntent intent)
    {
        if (string.IsNullOrWhiteSpace(intent.Gloss))
            throw new InvalidOperationException("An affix process needs a nonblank gloss.");
        if (string.IsNullOrWhiteSpace(intent.AnalysisWritingSystem))
            throw new InvalidOperationException("An affix process needs a nonblank analysis writing system.");
        if (intent.Definition is not null && string.IsNullOrWhiteSpace(intent.Definition))
            throw new InvalidOperationException("An affix process definition must be nonblank when present.");
        if (intent.Msa is null || intent.Msa.Kind == LexicalEntryMsaKind.Stem || !Enum.IsDefined(intent.Msa.Kind))
            throw new InvalidOperationException("An affix process needs affix grammatical info.");
        ValidatePattern(intent.Input, intent.Output);
    }

    /// <summary>Checks the stem pattern and realization alone, as an entry-less allomorph states them.</summary>
    internal static void ValidatePattern(IReadOnlyList<AffixProcessInputPart> input, IReadOnlyList<AffixProcessOutputStep> output)
    {
        if (input is null || input.Count is < 1 or > 16)
            throw new InvalidOperationException("A process needs between 1 and 16 input parts.");
        if (output is null || output.Count is < 1 or > 64)
            throw new InvalidOperationException("A process needs between 1 and 64 logical output steps.");
        var copies = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < input.Count; index++)
        {
            var part = input[index];
            if (part is null || string.IsNullOrWhiteSpace(part.Name) || !copies.TryAdd(part.Name, 0))
                throw new InvalidOperationException("Input part names must be nonblank and unique.");
            if ((part.Phoneme is null ? 0 : 1) + (part.NaturalClass is null ? 0 : 1) + (part.Remaining ? 1 : 0) != 1)
                throw new InvalidOperationException("An input part needs exactly one phoneme, natural class or remaining stem.");
            if (part.Remaining && index != input.Count - 1)
                throw new InvalidOperationException("The remaining-stem part must be unique and last.");
        }
        var expanded = 0;
        var inserts = false;
        foreach (var step in output)
        {
            if (step is null || (step.Copy is null) == (step.Insert is null))
                throw new InvalidOperationException("An output step needs exactly one copy or insertion.");
            if (step.Copy is { } name)
            {
                if (!copies.ContainsKey(name)) throw new InvalidOperationException($"Unknown input part '{name}'.");
                copies[name]++;
                expanded++;
            }
            else
            {
                if (step.Insert!.Count is < 1 or > 64)
                    throw new InvalidOperationException("An insertion needs between 1 and 64 phonemes.");
                expanded += step.Insert.Count;
                inserts = true;
            }
        }
        if (expanded > 256) throw new InvalidOperationException("A process supports at most 256 expanded output mappings.");
        var remaining = input.FirstOrDefault(part => part.Remaining);
        if (remaining is not null && copies[remaining.Name] == 0)
            throw new InvalidOperationException("The remaining-stem part must be copied at least once; omitting it deletes the stem.");
        if (copies.Values.All(count => count == 0))
            throw new InvalidOperationException("An output that copies no input part deletes the whole stem.");
        if (!inserts && copies.Values.All(count => count == 1))
            throw new InvalidOperationException("An identity-only realization is not an affix process.");
    }
}

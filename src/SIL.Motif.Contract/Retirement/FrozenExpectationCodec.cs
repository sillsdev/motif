using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SIL.Motif.Contract.Canonicalization;

namespace SIL.Motif.Contract.Retirement;

/// <summary>
/// Pure closed input validation for the independent before oracle. Capturing expectations, translating
/// identities and comparing parser results belong to producers/verification, not this value codec.
/// </summary>
public static class FrozenExpectationCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        MaxDepth = 32
    };

    /// <summary>Reads frozen original evidence, including explicit reasons that make it incomplete.</summary>
    public static FrozenExpectationSet Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            AllomorphRetirementCodec.CheckObject(document.RootElement);
            AllomorphRetirementValidation.Require(document.RootElement.TryGetProperty("contract", out _), "Missing expectation contract.");
            if (document.RootElement.TryGetProperty("baseline", out var baseline) && baseline.ValueKind == JsonValueKind.Object)
                Require(baseline.EnumerateObject().All(p => BaselineFields.Contains(p.Name, StringComparer.Ordinal)),
                    "Unknown frozen Baseline property.");
            var value = JsonSerializer.Deserialize<FrozenExpectationSet>(json, Options)
                ?? throw new FormatException("Null frozen expectation input.");
            Validate(value);
            return value;
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException)
        {
            throw new FormatException("Invalid closed frozen expectations: " + error.Message, error);
        }
    }

    /// <summary>Serializes a frozen original record; no expected Form is substituted or inferred.</summary>
    public static string ToJson(FrozenExpectationSet value)
    {
        var json = JsonSerializer.Serialize(value, Options);
        Parse(json);
        return CanonicalJson.Canonicalize(json);
    }

    /// <summary>
    /// Rejects unavailable original input; pinned by `IncompleteInputIsRetainedAndCannotClaimCompleteVerification`.
    /// </summary>
    public static void RequireComplete(FrozenExpectationSet value)
    {
        Validate(value);
        AllomorphRetirementValidation.Require(value.Unavailable.Count == 0, "Frozen expectations contain unavailable capabilities.");
    }

    private static void Validate(FrozenExpectationSet value)
    {
        Require(value.Contract == AllomorphRetirementCapabilities.FrozenExpectationContract, "Unsupported expectation contract.");
        Require(value.Baseline is not null, "Missing source Baseline.");
        Digest(value.HumanInputDigest); Digest(value.ManifestDigest); Text(value.ExpectationRevision);
        List(value.Cases); List(value.ReviewedNegatives); List(value.Unavailable);
        foreach (var reason in value.Unavailable) Text(reason);
        var cases = new HashSet<string>();
        foreach (var item in value.Cases)
        {
            Id(item.CaseId); OptionalId(item.Wordform); Text(item.WritingSystem); Surface(item.Surface);
            Require(cases.Add(Key(item.CaseId)), "Duplicate frozen case id.");
            List(item.Readings);
            var readings = new HashSet<string>();
            foreach (var reading in item.Readings)
            {
                Id(reading.ReadingId); OptionalId(reading.Analysis); Opinion(reading.Opinion); Digest(reading.ProvenanceDigest);
                Require(readings.Add(Key(reading.ReadingId)), "Duplicate reading id within a case.");
                Require(reading.Analysis is null || item.Wordform is not null, "Stored analysis needs its original wordform.");
                List(reading.Evaluations);
                var evaluations = new HashSet<string>();
                foreach (var evaluation in reading.Evaluations)
                {
                    Id(evaluation.Evaluation); Id(evaluation.Agent);
                    Require(evaluation.Opinion is "approved" or "disapproved", "Human evaluation must record its actual Opinion.");
                    Require(evaluations.Add(Key(evaluation.Evaluation)), "Duplicate evaluation membership.");
                }
                Morphs(reading.Morphs, true);
            }
        }
        var negatives = new HashSet<string>();
        foreach (var negative in value.ReviewedNegatives)
        {
            Id(negative.CaseId); Id(negative.Revision); Digest(negative.ContentDigest);
            Text(negative.WritingSystem); Surface(negative.Surface); Text(negative.Context);
            Require(negatives.Add(Key(negative.CaseId)), "Duplicate reviewed negative case.");
            Require(negative.Target is "surface" or "reading", "Unknown negative target.");
            Morphs(negative.Morphs, negative.Target == "reading");
            Require(negative.Target != "surface" || negative.Morphs.Count == 0, "Surface negatives have no morphology target.");
        }
    }

    private static void Morphs(IReadOnlyList<FrozenExpectationMorph> morphs, bool required)
    {
        List(morphs);
        Require(!required || morphs.Count > 0, "An expected reading needs original ordered morphology.");
        foreach (var morph in morphs)
        {
            OptionalId(morph.Bundle); OptionalId(morph.Form); Id(morph.Msa); OptionalId(morph.InflType);
            Require(morph.ExpansionRole is "whole" or "prefix" or "suffix", "Unknown frozen expansion role.");
            if (morph.GuessedString is null)
            {
                Require(morph.Form is not null, "An ordinary morphology requires authoritative Form identity.");
                Require(morph.GuessedWritingSystem is null, "Guessed WS requires guessed text.");
            }
            else { Surface(morph.GuessedString); Text(morph.GuessedWritingSystem); }
            List(morph.BundleText);
            var writingSystems = new HashSet<string>(StringComparer.Ordinal);
            foreach (var text in morph.BundleText)
            {
                Text(text.WritingSystem); Require(text.Text is not null, "Missing original bundle text."); Digest(text.RichContentDigest);
                Require(writingSystems.Add(text.WritingSystem), "Duplicate bundle text writing system.");
            }
        }
    }

    private static void List<T>(IReadOnlyList<T>? value) => Require(value is not null && value.All(v => v is not null), "Missing frozen list or null element.");
    private static readonly string[] BaselineFields =
    ["projectIdentity", "semanticSnapshotDigest", "projectionVersion", "capturedUtc", "bundleDigest", "capturedHostSessionId", "capturedEditGeneration"];
    private static void Surface(string value)
    {
        Text(value);
        Require(value.Normalize(NormalizationForm.FormD) == value, "Plain Unicode expectations must use NFD.");
    }
    private static void Opinion(string value) => Require(value is "approved" or "disapproved" or "unknown" or "mixed", "Unknown human Opinion.");
    private static void OptionalId(string? value) { if (value is not null) Id(value); }
    private static string Key(string value) => AllomorphRetirementValidation.Key(value);
    private static void Id(string? value) => AllomorphRetirementValidation.Id(value);
    private static void Digest(string value) => AllomorphRetirementValidation.Digest(value);
    private static void Text(string? value) => AllomorphRetirementValidation.Text(value);
    private static void Require(bool condition, string message) => AllomorphRetirementValidation.Require(condition, message);
}

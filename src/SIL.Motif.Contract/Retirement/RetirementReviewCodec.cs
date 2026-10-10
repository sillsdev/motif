using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using SIL.Motif.Contract.Canonicalization;

namespace SIL.Motif.Contract.Retirement;

/// <summary>Closed numeric review values; the producer must reconcile them with the bound detailed manifest.</summary>
public static class RetirementReviewCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true
    };

    public static RetirementReviewStatistics Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            AllomorphRetirementCodec.CheckObject(document.RootElement);
            var value = JsonSerializer.Deserialize<RetirementReviewStatistics>(json, Options)
                ?? throw new FormatException("Null retirement review statistics.");
            Validate(value);
            return value;
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException)
        {
            throw new FormatException("Invalid closed retirement review statistics: " + error.Message, error);
        }
    }

    public static string ToJson(RetirementReviewStatistics value)
    {
        var json = JsonSerializer.Serialize(value, Options);
        Parse(json);
        return CanonicalJson.Canonicalize(json);
    }

    private static void Validate(RetirementReviewStatistics value)
    {
        foreach (var counts in new[] { value.BundlesRepointed, value.AnalysesRepointed, value.WordformsRepointed })
            Require(counts is not null && counts.Approved >= 0 && counts.Disapproved >= 0 && counts.Unknown >= 0 && counts.Mixed >= 0,
                "Opinion counts must be disjoint nonnegative counts.");
        Require(value.WordformsRepointed.Total() <= value.AnalysesRepointed.Total() && value.AnalysesRepointed.Total() <= value.BundlesRepointed.Total(),
            "Distinct wordforms/analyses cannot exceed the bundles being repointed.");
        Require(value.Adhoc is { Count: 4 } && value.Adhoc.All(c => c is not null), "All four ad hoc partitions must be explicit.");
        Require(value.Adhoc.Select(c => (c.Grouped, c.Enabled)).Distinct().Count() == 4, "Duplicate ad hoc partition.");
        foreach (var counts in value.Adhoc)
            Require(counts.Rules >= 0 && counts.TargetOccurrences >= counts.Rules, "Invalid distinct ad hoc rules/occurrences.");
        Require(value.AssessedFormCases >= 0 && value.OtherReferences >= 0 && value.OwnedObjectsDeleted >= 0 &&
            value.BundleTextAlternativesChanged >= 0 && value.BundleTextAlternativesCleared >= 0, "Negative review count.");
        Require(value.Detector is not null, "Missing detector result.");
        var detector = value.Detector!;
        AllomorphRetirementValidation.Text(detector.FindingId);
        Require(detector.BeforeNumerator >= 0 && detector.BeforeDenominator >= detector.BeforeNumerator &&
            detector.AfterNumerator >= 0 && detector.AfterDenominator >= detector.AfterNumerator, "Invalid detector n/N.");
        AllomorphRetirementValidation.Digest(detector.BeforeEvidenceDigest);
        AllomorphRetirementValidation.Digest(detector.AfterEvidenceDigest);
        AllomorphRetirementValidation.Digest(value.DetailManifestDigest);
    }

    private static void Require(bool condition, string message) => AllomorphRetirementValidation.Require(condition, message);
}

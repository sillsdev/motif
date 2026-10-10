using System.Text.Json;
using System.Text.Json.Serialization;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Retirement;

namespace SIL.Motif.Worker.Parsimony;

/// <summary>The frozen measure request for one completed Dry Run candidate.</summary>
public sealed record ParsimonyCandidateJobInput(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("dryRunJobId")] string DryRunJobId,
    [property: JsonPropertyName("measureId")] string MeasureId,
    [property: JsonPropertyName("scopeBinding")] ParsimonyScopeBinding? ScopeBinding,
    [property: JsonPropertyName("assessmentIds")] IReadOnlyList<string>? AssessmentIds = null,
    [property: JsonPropertyName("retirementExpectationTranslation")]
    RetirementExpectationTranslation? RetirementExpectationTranslation = null)
{
    public const int CurrentSchemaVersion = 3;

    public static ParsimonyCandidateJobInput Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object || document.RootElement.EnumerateObject()
                .Any(property => property.Name is not
                    ("schemaVersion" or "dryRunJobId" or "measureId" or "scopeBinding" or "assessmentIds" or
                     "retirementExpectationTranslation")))
            throw new InvalidDataException("The candidate Parsimony job input contains unsupported fields.");
        var input = JsonSerializer.Deserialize<ParsimonyCandidateJobInput>(json, MotifJson.CreateOptions())
            ?? throw new InvalidDataException("The candidate Parsimony job has no input.");
        if (input.SchemaVersion != CurrentSchemaVersion || string.IsNullOrWhiteSpace(input.DryRunJobId) ||
            string.IsNullOrWhiteSpace(input.MeasureId) || input.ScopeBinding is null ||
            !Enum.IsDefined(input.ScopeBinding.Kind))
            throw new InvalidDataException("The candidate Parsimony job input is incomplete.");
        if (input.AssessmentIds is { } assessmentIds &&
            (assessmentIds.Any(string.IsNullOrWhiteSpace) ||
             assessmentIds.Distinct(StringComparer.Ordinal).Count() != assessmentIds.Count))
            throw new InvalidDataException("Candidate parser Assessment references must be nonblank and unique.");
        if (input.RetirementExpectationTranslation is { } translation)
            _ = RetirementExpectationTranslationCodec.Parse(
                RetirementExpectationTranslationCodec.ToJson(translation));
        return input;
    }
}

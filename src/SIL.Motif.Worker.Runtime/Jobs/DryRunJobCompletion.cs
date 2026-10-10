using System.Text.Json;
using System.Text.Json.Serialization;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;

namespace SIL.Motif.Worker.Jobs;

/// <summary>The exact source binding and Baseline freshness recorded when a Dry Run finishes.</summary>
public sealed record DryRunJobCompletion(
    [property: JsonPropertyName("baselineToken")] BaselineToken BaselineToken,
    [property: JsonPropertyName("capturedUtc")] string CapturedUtc,
    [property: JsonPropertyName("freshness")] string Freshness,
    [property: JsonPropertyName("sourceBaseline")] DryRunSourceBinding SourceBaseline)
{
    public static DryRunJobCompletion Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            document.RootElement.EnumerateObject().Any(property => property.Name is not
                ("baselineToken" or "capturedUtc" or "freshness" or "sourceBaseline")))
            throw new InvalidDataException("The completed Dry Run has an unsupported result shape.");
        var completion = JsonSerializer.Deserialize<DryRunJobCompletion>(json, MotifJson.CreateOptions())
            ?? throw new InvalidDataException("The completed Dry Run has no source binding.");
        if (completion.SourceBaseline is null || completion.BaselineToken is null ||
            completion.SourceBaseline.Token != completion.BaselineToken ||
            string.IsNullOrWhiteSpace(completion.CapturedUtc) ||
            completion.Freshness is not ("current" or "known-old" or "currentness-not-checked"))
            throw new InvalidDataException("The completed Dry Run has an incomplete source binding.");
        completion.SourceBaseline.Validate();
        return completion;
    }
}

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
    [property: JsonPropertyName("sourceBaseline")] DryRunSourceBinding SourceBaseline,
    [property: JsonPropertyName("contentDigest")] string ContentDigest)
{
    public static DryRunJobCompletion Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            document.RootElement.EnumerateObject().Any(property => property.Name is not
                ("baselineToken" or "capturedUtc" or "freshness" or "sourceBaseline" or "contentDigest")))
            throw new InvalidDataException("The completed Dry Run has an unsupported result shape.");
        var completion = JsonSerializer.Deserialize<DryRunJobCompletion>(json, MotifJson.CreateOptions())
            ?? throw new InvalidDataException("The completed Dry Run has no source binding.");
        if (string.IsNullOrWhiteSpace(completion.ContentDigest) || completion.SourceBaseline is null || completion.BaselineToken is null ||
            completion.SourceBaseline.Token != completion.BaselineToken ||
            string.IsNullOrWhiteSpace(completion.CapturedUtc) ||
            completion.Freshness is not ("current" or "known-old" or "currentness-not-checked"))
            throw new InvalidDataException("This stored Dry Run lacks content and Baseline evidence. Delete the Motif store and let Motif recreate it.");
        completion.SourceBaseline.Validate();
        return completion;
    }
}

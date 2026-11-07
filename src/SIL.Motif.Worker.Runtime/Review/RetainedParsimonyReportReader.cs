using System.Text.Json;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Worker.Runtime.Review;

/// <summary>Reads a retained paired Parsimony response without starting a detector.</summary>
public static class RetainedParsimonyReportReader
{
    /// <summary>Returns null for absent or malformed retained evidence.</summary>
    public static ParsimonyCandidateEvidenceResponse? Read(string? resultJson)
    {
        if (resultJson is null) return null;
        try { return JsonSerializer.Deserialize<ParsimonyCandidateEvidenceResponse>(resultJson, MotifJson.CreateOptions()); }
        catch (JsonException) { return null; }
    }
}

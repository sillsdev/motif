using System.Text.Json;
using System.Text.Json.Nodes;

namespace SIL.Motif.Worker.Jobs;

/// <summary>Execution cleanup metadata carried beside the job's existing public progress.</summary>
internal static class JobCleanupProgress
{
    private const string StatusProgress = "StatusProgress";
    private const string CleanupDeadline = "TerminalCleanupDeadline";
    private const string CleanupUnconfirmed = "TerminalCleanupUnconfirmed";

    internal static long? Deadline(string? progress) =>
        Envelope(progress)?[CleanupDeadline]?.GetValue<long>();

    internal static bool Unconfirmed(string? progress) =>
        Envelope(progress)?[CleanupUnconfirmed]?.GetValue<bool>() == true;

    internal static string? PublicProgress(string? progress) =>
        Envelope(progress) is { } envelope ? envelope[StatusProgress]?.ToJsonString() : progress;

    internal static string WithDeadline(string? progress, long deadline)
    {
        var envelope = Wrap(progress);
        envelope[CleanupDeadline] = Math.Min(deadline, envelope[CleanupDeadline]?.GetValue<long>() ?? deadline);
        return envelope.ToJsonString();
    }

    internal static string WithUnconfirmedCleanup(string? progress)
    {
        var envelope = Wrap(progress);
        envelope[CleanupUnconfirmed] = true;
        return envelope.ToJsonString();
    }

    private static JsonObject Wrap(string? progress) =>
        Envelope(progress) ?? new JsonObject { [StatusProgress] = JsonNode.Parse(progress ?? "null") };

    private static JsonObject? Envelope(string? progress)
    {
        if (progress is null) return null;
        try
        {
            using var document = JsonDocument.Parse(progress);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                (!root.TryGetProperty(CleanupDeadline, out _) && !root.TryGetProperty(CleanupUnconfirmed, out _)))
                return null;

            var envelope = new JsonObject();
            foreach (var property in root.EnumerateObject())
            {
                if (envelope.ContainsKey(property.Name) || !IsCleanupField(property))
                    throw new InvalidDataException("Stored job cleanup progress has an invalid field.");
                envelope.Add(property.Name, JsonNode.Parse(property.Value.GetRawText()));
            }
            if (!envelope.ContainsKey(StatusProgress))
                throw new InvalidDataException("Stored job cleanup progress is missing its public progress field.");
            return envelope;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Stored job progress JSON is malformed.", exception);
        }
    }

    private static bool IsCleanupField(JsonProperty property) => property.Name switch
    {
        StatusProgress => property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Null,
        CleanupDeadline => property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt64(out _),
        CleanupUnconfirmed => property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        _ => false,
    };
}

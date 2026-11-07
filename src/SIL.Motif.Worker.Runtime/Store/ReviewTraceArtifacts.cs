using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SIL.Motif.Contract.Review;
using SIL.Motif.Host.Store;

namespace SIL.Motif.Worker.Store;

/// <summary>Captured trace nodes and their exact report binding.</summary>
public sealed record RetainedReviewTrace(string TraceId, string ProjectKey, string ReportId, string CaseKey, Side Side,
    string ParserDigest, string GrammarDigest, string ScopeDigest, string Completion, bool CaptureComplete,
    IReadOnlyList<TraceNode> Nodes, ReadingAgreement ReadingAgreement = ReadingAgreement.Unknown);

/// <summary>Stores sanitized trace nodes alongside the report's releasable row artifacts.</summary>
public static class ReviewTraceArtifacts
{
    /// <summary>Writes a trace only while the report still retains its details.</summary>
    public static void Store(MotifDatabase database, RetainedReviewTrace trace)
    {
        using var lease = ReviewRowArtifacts.Acquire(database, trace.ReportId);
        RequireDetail(database, trace.ProjectKey, trace.ReportId);
        var path = PathFor(database, trace.ReportId, trace.TraceId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(trace, ReviewJson.Options);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            try { File.Move(temporary, path, overwrite: false); }
            catch (IOException) when (File.Exists(path)) { }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        var lookup = LookupPath(database, trace.ReportId, trace.CaseKey, trace.Side);
        if (!File.Exists(lookup)) File.WriteAllText(lookup, trace.TraceId);
    }

    /// <summary>Finds a saved diagnostic for one report case and side without opening its inputs.</summary>
    public static RetainedReviewTrace? Find(MotifDatabase database, string project, string report, string caseKey, Side side)
    {
        using var lease = ReviewRowArtifacts.Acquire(database, report);
        RequireDetail(database, project, report);
        var lookup = LookupPath(database, report, caseKey, side);
        if (!File.Exists(lookup)) return null;
        var trace = ReadCore(database, project, report, File.ReadAllText(lookup));
        if (trace is null || trace.CaseKey != caseKey || trace.Side != side)
            throw new InvalidDataException("The retained trace lookup does not match its case.");
        return trace;
    }

    /// <summary>Reads a saved trace without starting a parser.</summary>
    public static RetainedReviewTrace? Read(MotifDatabase database, string project, string report, string traceId)
    {
        using var lease = ReviewRowArtifacts.Acquire(database, report);
        RequireDetail(database, project, report);
        return ReadCore(database, project, report, traceId);
    }

    private static RetainedReviewTrace? ReadCore(MotifDatabase database, string project, string report, string traceId)
    {
        var path = PathFor(database, report, traceId);
        if (!File.Exists(path)) return null;
        var trace = JsonSerializer.Deserialize<RetainedReviewTrace>(File.ReadAllBytes(path), ReviewJson.Options)
            ?? throw new InvalidDataException("Invalid retained trace.");
        if (trace.TraceId != traceId || trace.ProjectKey != project || trace.ReportId != report || trace.Nodes.Count > 10_000)
            throw new InvalidDataException("The retained trace does not match its report.");
        return trace;
    }

    private static void RequireDetail(MotifDatabase database, string project, string report)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT DetailAvailable FROM ReviewReports WHERE ProjectKey=$project AND ReportId=$report;";
        command.Parameters.AddWithValue("$project", project);
        command.Parameters.AddWithValue("$report", report);
        if (command.ExecuteScalar() is not long available || available == 0) throw new ReviewDetailUnavailableException();
    }

    private static string LookupPath(MotifDatabase database, string report, string caseKey, Side side)
    {
        var key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(caseKey + "\n" + side)));
        var reportHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(report)));
        return Path.Combine(database.FullPath + ".review-readings", reportHash, "case-" + key + ".trace");
    }

    private static string PathFor(MotifDatabase database, string report, string traceId)
    {
        if (traceId.Length != 64 || traceId.Any(character => !Uri.IsHexDigit(character))) throw new ArgumentException("Invalid trace id.");
        var reportHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(report)));
        return Path.Combine(database.FullPath + ".review-readings", reportHash, "trace-" + traceId + ".json");
    }
}

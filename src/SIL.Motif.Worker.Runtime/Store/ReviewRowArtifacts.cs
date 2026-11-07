using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SIL.Motif.Contract.Review;
using SIL.Motif.Host.Store;
using SIL.Motif.Host.PanGloss;

namespace SIL.Motif.Worker.Store;

internal static class ReviewRowArtifacts
{
    internal static string Write(MotifDatabase database, ReviewReportId reportId, ReviewRow reading)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes<ReviewRow>(reading, ReviewJson.Options);
        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var directory = DirectoryFor(database, reportId.Value);
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, digest + ".json");
        if (!File.Exists(destination))
        {
            var temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllBytes(temporary, bytes);
                try { File.Move(temporary, destination, overwrite: false); }
                catch (IOException) when (File.Exists(destination)) { }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        if (!StringComparer.Ordinal.Equals(digest, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(destination)))))
            throw new InvalidDataException("A retained review reading no longer matches its digest.");
        return digest;
    }

    internal static ReviewRow Read(MotifDatabase database, ReviewReportId reportId, string digest)
    {
        if (digest.Length != 64 || digest.Any(c => !Uri.IsHexDigit(c))) throw new InvalidDataException("Invalid review reading digest.");
        var path = Path.Combine(DirectoryFor(database, reportId.Value), digest + ".json");
        byte[] bytes;
        try { bytes = File.ReadAllBytes(path); }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new ReviewDetailUnavailableException("artifact-missing");
        }
        if (!StringComparer.Ordinal.Equals(digest, Convert.ToHexStringLower(SHA256.HashData(bytes))))
            throw new InvalidDataException("A retained review reading no longer matches its digest.");
        return JsonSerializer.Deserialize<ReviewRow>(bytes, ReviewJson.Options) ?? throw new InvalidDataException("Invalid retained review reading.");
    }

    internal static void DeleteReleased(MotifDatabase database, string proposalId)
    {
        var reports = new List<string>();
        using (var connection = database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT ReportId FROM ReviewReports WHERE ProposalId=$proposal AND DetailAvailable=0;";
            command.Parameters.AddWithValue("$proposal", proposalId);
            using var reader = command.ExecuteReader();
            while (reader.Read()) reports.Add(reader.GetString(0));
        }
        foreach (var report in reports) Delete(database, report);
    }

    internal static WorkerMutexOwner Lock(MotifDatabase database, string reportId)
    {
        var path = Path.GetFullPath(database.FullPath);
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()) path = path.ToUpperInvariant();
        var key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(path + "\n" + reportId)));
        return WorkerMutexOwner.ForRoot("Global\\Motif.ReviewReading." + key);
    }

    internal static WorkerMutexOwner Acquire(MotifDatabase database, string reportId)
    {
        var lease = Lock(database, reportId);
        try
        {
            while (!lease.TryAcquire()) Thread.Sleep(20);
            return lease;
        }
        catch { lease.Dispose(); throw; }
    }

    internal static void Delete(MotifDatabase database, string reportId)
    {
        using var lease = Acquire(database, reportId);
        try
        {
            var directory = DirectoryFor(database, reportId);
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string DirectoryFor(MotifDatabase database, string reportId) => Path.Combine(
        database.FullPath + ".review-readings", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(reportId))));
}

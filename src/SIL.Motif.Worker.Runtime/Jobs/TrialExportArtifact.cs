using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Worker.Jobs;

/// <summary>Publishes and opens one durable JSON file per measured Trial.</summary>
public static class TrialExportArtifact
{
    private const int Version = 1;

    /// <summary>The stored Report kind for an export that carries its evidence by value.</summary>
    internal const string BindingKind = "trial-export-binding";

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>Reads the receipt and, when requested, a numbered page from the same immutable evidence file.</summary>
    public static TrialExportReceipt? Read(MotifDatabase database, string jobId, string? at = null, int? page = null)
    {
        if (string.IsNullOrWhiteSpace(jobId) || JsonEncodedText.Encode(jobId).EncodedUtf8Bytes.Length > 256 || page is < 1)
            return null;
        var key = JobKey(jobId);
        if (LoadBinding(database, jobId) is not { } binding) return null;

        var suffix = FileSuffix(key, binding.Sha256);
        var path = database.FullPath + suffix;
        if (!File.Exists(path)) return null;
        RequireRegular(Path.GetDirectoryName(path)!);
        RequireRegular(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (stream.Length != binding.Bytes || !MatchesHeader(stream, binding))
            throw new InvalidDataException("The retained Trial export does not match its store binding.");
        var (reportId, reportDigest) = new ReviewReportRepository(database)
            .FindTrialReportBinding(jobId, binding.ProjectKey, binding.ProposalId);
        if (!binding.Matches(at) && at != reportDigest) return null;
        var receipt = new TrialExportReceipt(Version, jobId, path, binding.Bytes, binding.Sha256, reportId, reportDigest,
            binding.Denominators, binding.ProposalRevision, binding.BaselineBundle, binding.PublishMilliseconds,
            binding.PageOffsets.Length, binding.ComparisonComplete, PathIsStoreSuffix: false);
        receipt = WithinBound(receipt, suffix);

        return page is { } number ? receipt with { Page = ReadPage(stream, binding, number) } : receipt;
    }

    /// <summary>Keeps the serialized receipt under its bound by swapping an over-long, JSON-escaped path for the store suffix.</summary>
    internal static TrialExportReceipt WithinBound(TrialExportReceipt receipt, string suffix) =>
        ProjectionJson.ToUtf8Bytes(receipt).Length > 1950 ? receipt with { Path = suffix, PathIsStoreSuffix = true } : receipt;

    /// <summary>The semantic-snapshot digest of the Baseline this Trial was measured on, or null when it has no export.</summary>
    internal static string? ReadBaselineSemantic(MotifDatabase database, string jobId) =>
        string.IsNullOrWhiteSpace(jobId) ? null : LoadBinding(database, jobId)?.BaselineSemantic;

    /// <summary>
    /// The PanGloss versions the Trial recorded for its before and after sides, or null when the export is not retained.
    /// Reads only the provenance header of the evidence file, never a word page.
    /// </summary>
    internal static (string? Before, string? After)? ReadParserVersions(MotifDatabase database, string jobId)
    {
        if (LoadBinding(database, jobId) is not { } binding) return null;
        var path = database.FullPath + FileSuffix(JobKey(jobId), binding.Sha256);
        if (!File.Exists(path)) return null;
        RequireRegular(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        var header = binding.PageOffsets.Length > 0 ? binding.PageOffsets[0] : stream.Length;
        var bytes = new byte[header + (binding.PageOffsets.Length > 0 ? 2 : 0)];
        stream.ReadExactly(bytes, 0, (int)header);
        if (binding.PageOffsets.Length > 0) { bytes[header] = (byte)']'; bytes[header + 1] = (byte)'}'; }
        using var document = JsonDocument.Parse(bytes);
        if (!document.RootElement.TryGetProperty("provenance", out var provenance) ||
            !provenance.TryGetProperty("panGlossVersion", out var version) || version.ValueKind != JsonValueKind.Object)
            return (null, null);
        string? Side(string name) => version.TryGetProperty(name, out var side) && side.ValueKind == JsonValueKind.String
            ? side.GetString() : null;
        return (Side("before"), Side("after"));
    }

    private static ArtifactBinding? LoadBinding(MotifDatabase database, string jobId)
    {
        var record = new ReportRepository(database).Get(BindingId(JobKey(jobId)));
        if (record is not { Kind: BindingKind }) return null;
        var binding = JsonSerializer.Deserialize<ArtifactBinding>(record.ReportJson, Json);
        return binding is null || binding.JobId != jobId || binding.ProjectKey != ProjectWorkspaceKey.Compute(database.Project) ||
            !Sha256Value.IsCanonical(binding.Sha256) ? null : binding;
    }

    private static bool MatchesHeader(Stream stream, ArtifactBinding binding)
    {
        Span<byte> prefix = stackalloc byte[4096];
        var length = stream.ReadAtLeast(prefix, prefix.Length, throwOnEndOfStream: false);
        var reader = new Utf8JsonReader(prefix[..length], isFinalBlock: length == stream.Length, state: default);
        return reader.Read() && reader.TokenType == JsonTokenType.StartObject &&
            ReadProperty(ref reader, "schemaVersion") && reader.TokenType == JsonTokenType.Number &&
            reader.TryGetInt32(out var version) && version == Version &&
            ReadProperty(ref reader, "jobId") && reader.TokenType == JsonTokenType.String && reader.ValueTextEquals(binding.JobId) &&
            ReadProperty(ref reader, "denominators") && reader.TrySkip() &&
            ReadProperty(ref reader, "provenance") && reader.TokenType == JsonTokenType.StartObject &&
            ReadProperty(ref reader, "projectKey") && reader.TokenType == JsonTokenType.String && reader.ValueTextEquals(binding.ProjectKey);
    }

    private static bool ReadProperty(ref Utf8JsonReader reader, string name) =>
        reader.Read() && reader.TokenType == JsonTokenType.PropertyName && reader.ValueTextEquals(name) && reader.Read();

    private static TrialExportPage ReadPage(Stream stream, ArtifactBinding binding, int number)
    {
        if (number > binding.PageOffsets.Length) return new(number, null, []);
        stream.Position = binding.PageOffsets[number - 1];
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var count = Math.Min(TrialExportPage.Size, binding.Denominators.Submitted - (number - 1) * TrialExportPage.Size);
        var words = new JsonElement[count];
        for (var index = 0; index < count; index++)
        {
            var line = reader.ReadLine() ?? throw new InvalidDataException("A retained Trial export page ended early.");
            var start = line.StartsWith(',') ? 1 : 0;
            words[index] = JsonSerializer.Deserialize<JsonElement>(line.AsSpan(start), Json);
        }
        return new(number, number < binding.PageOffsets.Length ? number + 1 : null, words);
    }

    internal static void Publish(MotifDatabase database, string jobId, TrialExportDenominators denominators,
        TrialExportProvenance provenance, IReadOnlyList<TrialExportInterruption> interruptions,
        IEnumerable<TrialExportWord> words, string? claimToken = null)
    {
        var elapsed = Stopwatch.StartNew();
        var projectKey = ProjectWorkspaceKey.Compute(database.Project);
        var directory = MotifDatabase.TrialExportDirectory(database.FullPath);
        Directory.CreateDirectory(directory);
        RequireRegular(directory);
        var temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            var offsets = new List<long>();
            string digest;
            long bytes;
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                WriteEvidence(stream, jobId, denominators, provenance with { ProjectKey = projectKey }, interruptions, words, offsets);
                stream.Flush(flushToDisk: true);
                bytes = stream.Length;
                stream.Position = 0;
                digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(stream));
            }

            var key = JobKey(jobId);
            var path = database.FullPath + FileSuffix(key, digest);
            var binding = new ArtifactBinding(jobId, projectKey, provenance.ProposalId,
                provenance.ProposalRevision, provenance.Baseline.BundleDigest, provenance.Baseline.SemanticSnapshotDigest,
                denominators, bytes, digest, elapsed.ElapsedMilliseconds, offsets.ToArray(),
                interruptions.Count == 0 && denominators.Before.Finished == denominators.Expanded &&
                denominators.After.Finished == denominators.Expanded);
            var report = new ReportRecord(BindingId(key), null, null, JsonSerializer.Serialize(binding, Json),
                null, BindingKind);
            void Retain(SqliteConnection connection, SqliteTransaction transaction)
            {
                try { File.Move(temporary, path); }
                catch (IOException) when (File.Exists(path)) { RequireRegular(path); }
                ReportRepository.Save(connection, transaction, report);
            }

            if (claimToken is not null)
            {
                TrialReviewEvidence.WithClaim(database, jobId, claimToken, Retain);
            }
            else
            {
                using var connection = database.OpenConnection();
                using var transaction = connection.BeginTransaction();
                Retain(connection, transaction);
                transaction.Commit();
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void WriteEvidence(FileStream stream, string jobId, TrialExportDenominators denominators,
        TrialExportProvenance provenance, IReadOnlyList<TrialExportInterruption> interruptions,
        IEnumerable<TrialExportWord> words, List<long> offsets)
    {
        using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", Version);
        writer.WriteString("jobId", jobId);
        writer.WritePropertyName("denominators");
        JsonSerializer.Serialize(writer, denominators, Json);
        writer.WritePropertyName("provenance");
        JsonSerializer.Serialize(writer, provenance, Json);
        writer.WritePropertyName("interruptions");
        JsonSerializer.Serialize(writer, interruptions, Json);
        writer.WriteStartArray("words");
        writer.Flush();

        var count = 0;
        foreach (var word in words)
        {
            if (count % TrialExportPage.Size == 0) offsets.Add(stream.Position);
            JsonSerializer.Serialize(writer, word, Json);
            writer.Flush();
            stream.WriteByte((byte)'\n');
            count++;
        }
        if (count != denominators.Submitted) throw new InvalidDataException("The Trial export does not match its submitted count.");
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();
    }

    private static string JobKey(string jobId) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(jobId)));
    private static string BindingId(string jobKey) => "trial-export/" + jobKey;
    private static string FileSuffix(string jobKey, string digest) =>
        Path.Combine(MotifDatabase.TrialExportDirectory(""), jobKey + "." + digest["sha256:".Length..] + ".json");

    private static void RequireRegular(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The Trial evidence export path is redirected.");
    }

    private sealed record ArtifactBinding(string JobId, string ProjectKey, string ProposalId, string ProposalRevision,
        string BaselineBundle, string BaselineSemantic, TrialExportDenominators Denominators, long Bytes,
        string Sha256, long PublishMilliseconds, long[] PageOffsets, bool ComparisonComplete)
    {
        internal bool Matches(string? at) => at is null || at == ProposalRevision || at == BaselineBundle || at == BaselineSemantic;
    }
}

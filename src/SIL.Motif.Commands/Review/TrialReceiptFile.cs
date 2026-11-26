using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Review;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands.Review;

/// <summary>
/// Writes the receipt a green Trial earns and decides whether Apply may rely on one: the receipt must be intact, passing,
/// and bound to this Proposal's current revision, the current Baseline, a Trial of this Proposal and the gate code that is
/// running now. The lane gate binds its receipt to the live revision and the gate script's sha the same way.
/// </summary>
internal static class TrialReceiptFile
{
    /// <summary>
    /// The build of the expectation rules, so a receipt issued by other rules is refused. It is the module id of this
    /// assembly: any rebuild of the gate code changes it, as a changed script changes the lane gate's sha.
    /// </summary>
    internal static string GateCodeDigest => Hex(typeof(TrialReceiptFile).Assembly.ManifestModule.ModuleVersionId.ToByteArray());

    /// <summary>The digest of the manifests and caps a receipt was held to.</summary>
    internal static string ExpectationsDigest(TrialExpectations expectations)
    {
        var text = new StringBuilder();
        foreach (var rows in new[] { expectations.RetiredRows, expectations.AddedRows })
        {
            text.Append(rows is null ? "-" : string.Join('\n', rows.Where(line => !string.IsNullOrWhiteSpace(line)))).Append('\u001f');
        }
        text.Append(expectations.MaxDoubled?.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('\u001f');
        text.Append(expectations.MaxDoubledFrac?.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        return Hex(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    /// <summary>Builds the receipt of a green Trial, with its digest set.</summary>
    internal static TrialReceipt Build(string proposalId, string intentDigest, string baselineDigest, string jobId,
        TrialExpectations expectations, ReadingDeltaCounts counts)
    {
        var receipt = new TrialReceipt(TrialReceipt.Version, proposalId, intentDigest, baselineDigest, jobId, GateCodeDigest,
            ExpectationsDigest(expectations), ReadingExpectationStates.Green, counts.Measured, counts.Doubled, "");
        return Seal(receipt);
    }

    /// <summary>Writes the receipt, replacing any earlier file; null on success, else the refusal.</summary>
    internal static Refusal? Write(string path, TrialReceipt receipt)
    {
        try
        {
            var full = Path.GetFullPath(path);
            if (Path.GetDirectoryName(full) is { Length: > 0 } directory) Directory.CreateDirectory(directory);
            File.WriteAllText(full, JsonSerializer.Serialize(receipt, Options) + "\n", new UTF8Encoding(false));
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new Refusal(RefusalCodes.TrialReceiptUnwritable, FailureReason.InvalidArgument,
                $"The receipt could not be written to '{path}': {exception.Message}");
        }
    }

    /// <summary>The refusal that stops Apply, or null when the receipt at <paramref name="path"/> binds this Apply.</summary>
    internal static Refusal? Verify(MotifDatabase database, ProjectLocator project, ProposalRecord record, string path)
    {
        Refusal Invalid(string reason) => new(RefusalCodes.ApplyReceiptInvalid, FailureReason.InvalidArgument,
            $"The Trial receipt '{path}' cannot authorize Apply: {reason} Run 'proposal trial delta --write-receipt' on a current Trial.",
            new Dictionary<string, string> { ["proposalId"] = record.ProposalId.Value });
        Refusal Stale(string reason) => new(RefusalCodes.ApplyReceiptStale, FailureReason.Refused,
            $"The Trial receipt '{path}' no longer binds this Apply: {reason} Run a new Trial and write a new receipt.",
            new Dictionary<string, string> { ["proposalId"] = record.ProposalId.Value });
        TrialReceipt? receipt;
        try
        {
            if (!File.Exists(path)) return Invalid("the file does not exist.");
            receipt = JsonSerializer.Deserialize<TrialReceipt>(File.ReadAllText(path), Options);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return Invalid("it could not be read: " + exception.Message);
        }
        if (receipt is null || receipt.SchemaVersion != TrialReceipt.Version) return Invalid("it is not a receipt of the current version.");
        if (!string.Equals(receipt.ReceiptDigest, DigestOf(receipt), StringComparison.Ordinal)) return Invalid("it was edited after it was written.");
        if (receipt.State != ReadingExpectationStates.Green) return Invalid($"its state is '{receipt.State}', not green.");

        try
        {
            if (!string.Equals(receipt.ProposalId, record.ProposalId.Value, StringComparison.Ordinal))
                return Stale($"it is for Proposal {receipt.ProposalId}, not {record.ProposalId.Value}.");
            if (!string.Equals(receipt.GateCodeDigest, GateCodeDigest, StringComparison.Ordinal))
                return Stale($"it was issued by gate code {receipt.GateCodeDigest}, and this build is {GateCodeDigest}.");
            var current = Trials.ReadCurrentIntentDigest(record);
            if (!string.Equals(receipt.IntentDigest, current, StringComparison.Ordinal))
                return Stale($"it is for Proposal revision {receipt.IntentDigest}, and the Proposal is now {current ?? "unreadable"}.");
            var baseline = new BaselineRepository(database).GetCurrent(ProjectWorkspaceKey.Compute(project))?.Token.SemanticSnapshotDigest;
            if (!string.Equals(receipt.BaselineSemanticDigest, baseline, StringComparison.Ordinal))
                return Stale($"it is for Baseline {receipt.BaselineSemanticDigest}, and the current Baseline is {baseline ?? "absent"}.");
            var job = new JobRepository(database).Get(receipt.TrialJobId);
            if (job is null || !string.Equals(Trials.ProposalOf(job), record.ProposalId.Value, StringComparison.Ordinal))
                return Stale($"its Trial {receipt.TrialJobId} is gone or belongs to another Proposal.");
            return null;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or KeyNotFoundException or
            ArgumentException or InvalidOperationException)
        {
            return Stale("the current revision or Baseline could not be read: " + exception.Message);
        }
    }

    /// <summary>
    /// The refusal when the loaded live project differs from the Baseline the receipt names, as when FieldWorks saved an
    /// edit after the Baseline; null when it still matches. Run after <see cref="Verify"/> has accepted the receipt.
    /// </summary>
    internal static Refusal? VerifyLive(MotifDatabase database, ProjectLocator project, ProposalRecord record,
        SIL.LCModel.LcmCache live)
    {
        var token = new BaselineRepository(database).GetCurrent(ProjectWorkspaceKey.Compute(project))?.Token;
        if (token is not null && AutonomousTrialGate.LiveMatchesBaseline(live, token)) return null;
        return new Refusal(RefusalCodes.ApplyReceiptStale, FailureReason.Refused,
            "The Trial receipt no longer binds this Apply: the FieldWorks data changed since the Baseline the Trial measured. " +
            "Capture a new Baseline, run a new Trial and write a new receipt.",
            new Dictionary<string, string> { ["proposalId"] = record.ProposalId.Value });
    }

    private static readonly JsonSerializerOptions Options = MotifJson.CreateOptions();

    /// <summary>The receipt with its digest recomputed over every other member.</summary>
    internal static TrialReceipt Seal(TrialReceipt receipt) => receipt with { ReceiptDigest = DigestOf(receipt) };

    private static string DigestOf(TrialReceipt receipt) =>
        Hex(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(receipt with { ReceiptDigest = "" }, Options)));

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
}

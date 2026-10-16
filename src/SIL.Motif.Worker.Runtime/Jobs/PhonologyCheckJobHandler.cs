using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Phonology;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Worker.Jobs;

/// <summary>Checks one saved phonology Example Set revision against the current Baseline's grammar.</summary>
public sealed class PhonologyCheckJobHandler(MotifDatabase database, BaselineRepository baselines,
    string projectKey, IPanGlossInvoker invoker)
{
    /// <summary>The job kind that records a phonology check as one advisory Assessment.</summary>
    public const string JobKind = "phonology-check";

    private static readonly JsonSerializerOptions JsonOptions = MotifJson.CreateOptions();

    /// <summary>Runs the check and records its Assessment, or fails the job with a categorized reason.</summary>
    /// <param name="claimed">The claimed job whose input names the revision, Baseline and limits.</param>
    /// <param name="cancellationToken">Cancels the invocation.</param>
    public async Task<JobOutcome?> RunAsync(ClaimedJob claimed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claimed);
        var input = JsonSerializer.Deserialize<PhonologyCheckJobInput>(claimed.Job.InputJson, JsonOptions)
            ?? throw new InvalidDataException("The phonology check job has no input.");

        PhonologyExampleSetHeader header;
        IReadOnlyList<PhonologyExample> examples;
        try
        {
            header = new PhonologyExampleSetRepository(database).Read(input.RevisionId, 1, null).ExampleSet;
            using var connection = database.OpenConnection();
            using var transaction = connection.BeginTransaction();
            examples = PhonologyExampleSetRepository.ReadFrozen(connection, transaction, header.Revision).Examples;
        }
        catch (KeyNotFoundException)
        {
            return Failure(JobFailureCategory.Semantic, "The saved Example Set revision no longer exists.");
        }

        var current = baselines.GetCurrentEvidence(projectKey);
        if (current is null)
            return Failure(JobFailureCategory.Semantic,
                "No Baseline is recorded. Capture a Baseline before checking phonology examples.");
        var baseline = current.Baseline;
        if (input.BaselineToken is not null && baseline.Token != input.BaselineToken)
            return Failure(JobFailureCategory.Semantic,
                "The Baseline changed after the check was requested; request the check again.");
        if (!File.Exists(baseline.FwDataPath))
            return Failure(JobFailureCategory.Infrastructure,
                "The recorded Baseline bundle is missing; refresh the Baseline before checking phonology examples.");

        var sourceDigest = BatchInvocationEvidence.DigestFile(baseline.FwDataPath);
        PhonologyAssessmentOutcome outcome;
        try
        {
            var invocation = await invoker.RunAsync(
                new PanGlossRequest.PhonologyExamples(baseline.FwDataPath, examples, input.Limits),
                "phonology-check", cancellationToken).ConfigureAwait(false);
            if (invocation is PanGlossOutcome.Cancelled)
                return Failure(JobFailureCategory.Cancellation, "The phonology check was cancelled.");
            outcome = PhonologyAssessmentOutcome.From(invocation);
        }
        catch (PhonologyInfrastructureException exception)
        {
            return Failure(JobFailureCategory.Infrastructure, exception.Message);
        }
        catch (InvalidDataException exception)
        {
            return Failure(JobFailureCategory.Semantic, exception.Message);
        }

        // The invocation hashes the grammar it actually read; a different file means the Baseline moved under it.
        if (outcome is PhonologyAssessmentOutcome.Produced produced &&
            "sha256:" + produced.Measurement.GrammarSha256 != sourceDigest)
            return Failure(JobFailureCategory.Semantic,
                "The Baseline grammar changed while the check ran; request the check again.");

        var assessmentId = CanonicalId.Mint("assessment/").Value;
        var scopeJson = ScopeCodec.Write(new StoredScope.PhonologyExamples(header.Revision, input.Limits));
        var scopeDigest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(scopeJson)));
        var record = new NewAssessmentRecord(assessmentId, null, null, "pangloss",
            AssessmentKind.PhonologyExamples.ToString(), scopeJson, scopeDigest,
            new AssessmentSubject.PhonologyExamples(header.Revision),
            JsonSerializer.Serialize(baseline.Token, JsonOptions), null, null, sourceDigest, null, null, null,
            new AssessmentRecordMaterial.PhonologyExamples(new PhonologyBaselineSource(baseline.Token),
                input.Limits, outcome));
        new AssessmentRepository(database).Record(record);
        return new JobOutcome(JobStatus.Completed, ResultJson: JsonSerializer.Serialize(
            new { assessmentIds = new[] { assessmentId }, assessmentId, revisionId = header.Revision.RevisionId },
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private static JobOutcome Failure(JobFailureCategory category, string detail) =>
        new(JobStatus.Failed, category, JsonSerializer.Serialize(new { detail }));
}

/// <summary>The stored input of a phonology check job.</summary>
/// <param name="RevisionId">The exact saved Example Set revision to check.</param>
/// <param name="BaselineToken">The Baseline the requester named, or null for the current one when the job runs.</param>
/// <param name="Limits">The request-wide step, work and time ceilings.</param>
public sealed record PhonologyCheckJobInput(
    [property: JsonPropertyName("revisionId")] string RevisionId,
    [property: JsonPropertyName("baselineToken")] BaselineToken? BaselineToken,
    [property: JsonPropertyName("limits")] PhonologyLimits Limits);

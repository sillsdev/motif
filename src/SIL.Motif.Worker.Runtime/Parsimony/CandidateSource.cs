using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Worker.Parsimony;

/// <summary>
/// The completed job whose published Dry Run a candidate Parsimony job measures: a Dry Run job, or a Trial job
/// that committed, which publishes the same Dry Run beside its Assessments.
/// </summary>
/// <param name="Job">The Dry Run or Trial job.</param>
/// <param name="DryRunInput">The frozen Proposal, prerequisites and Baseline source the Dry Run used.</param>
/// <param name="PublishedDryRunJson">The Dry Run effects the job published.</param>
/// <param name="Source">The exact published Baseline files the Dry Run ran against.</param>
/// <param name="AssessmentIds">The Assessments a Trial recorded, or none for a Dry Run job.</param>
public sealed record ParsimonyCandidateSource(JobRecord Job, DryRunJobInput DryRunInput,
    string PublishedDryRunJson, DryRunSourceBinding Source, IReadOnlyList<string> AssessmentIds)
{
    /// <summary>The job kind of a plain Dry Run.</summary>
    public const string DryRunKind = "dry-run";

    /// <summary>
    /// Reads the job a candidate names. A Trial is a source only once it committed with a published Dry Run, and
    /// only while the Baseline it measured is still the current one, because its Baseline files are not named in
    /// its result.
    /// </summary>
    /// <param name="database">The project's store.</param>
    /// <param name="jobId">The Dry Run or Trial job id.</param>
    /// <returns>The source, or null when the job is absent, of another kind, unfinished or unpublished.</returns>
    /// <exception cref="InvalidDataException">The job lacks frozen source or Proposal evidence.</exception>
    public static ParsimonyCandidateSource? Read(MotifDatabase database, string jobId)
    {
        ArgumentNullException.ThrowIfNull(database);
        var job = new JobRepository(database).Get(jobId);
        if (job is null || !job.DryRunPublished || job.DryRunJson is null) return null;
        return job.Kind switch
        {
            DryRunKind when job.Status == JobStatus.CompletedDryRunOnly => FromDryRun(job),
            TrialJobHandler.TrialKind when job.Status == JobStatus.Completed => FromTrial(database, job),
            _ => null,
        };
    }

    private static ParsimonyCandidateSource FromDryRun(JobRecord job)
    {
        var input = DryRunJobInput.Parse(job.InputJson);
        if (job.ResultJson is null) throw new InvalidDataException("The completed Dry Run has no source binding.");
        var source = DryRunJobCompletion.Parse(job.ResultJson).SourceBaseline;
        if (input.SourceBaseline is not null && input.SourceBaseline != source)
            throw new InvalidDataException("The Dry Run input and result name different Baseline sources.");
        source.Validate();
        return new ParsimonyCandidateSource(job, input, job.DryRunJson!, source, []);
    }

    private static ParsimonyCandidateSource FromTrial(MotifDatabase database, JobRecord job)
    {
        var trial = TrialJobInput.Parse(job.InputJson);
        var completion = job.ResultJson is null ? null
            : JsonSerializer.Deserialize<TrialResult>(job.ResultJson, MotifJson.CreateOptions());
        if (completion?.BaselineToken is null)
            throw new InvalidDataException("The completed Trial has no Baseline binding.");
        var current = new BaselineRepository(database).GetCurrentPublished(job.ProjectKey);
        if (current is null || current.Token != completion.BaselineToken)
            throw new InvalidDataException("The Baseline changed after this Trial. Run the Trial again.");
        var source = new DryRunSourceBinding(current.Token, current.RootDirectory, current.FwDataPath,
            "sha256:" + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(current.FwDataPath))));
        source.Validate();
        var input = new DryRunJobInput(DryRunJobInput.CurrentSchemaVersion,
            FrozenProposalRevision.Create(trial.ProposalJson), trial.Prerequisites ?? [], source);
        input.Validate();
        return new ParsimonyCandidateSource(job, input, job.DryRunJson!, source, completion.AssessmentIds ?? []);
    }

    // Only the two fields of a Trial's stored completion a candidate needs; the rest belongs to the Trial.
    private sealed record TrialResult(
        [property: JsonPropertyName("baselineToken")] BaselineToken? BaselineToken,
        [property: JsonPropertyName("assessmentIds")] IReadOnlyList<string>? AssessmentIds);
}

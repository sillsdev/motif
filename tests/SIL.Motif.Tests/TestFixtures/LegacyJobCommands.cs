using System;
using SIL.Motif.Cli.Rendering;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Projection.Usage;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>
/// Renders <see cref="JobCommands"/>' typed outcomes back into the pre-typed-outcome
/// <see cref="CommandResult"/> shape, through the same <see cref="CommandTextRenderer"/> the CLI itself
/// uses. See <see cref="LegacyProposalCommands"/> for why: tests written against the old rendered-text
/// surface call through here rather than asserting on <see cref="Contract.Commands.CommandOutcome{T}"/>
/// directly, so they keep exercising the same end-to-end call sequence a real invocation makes.
/// </summary>
internal static class LegacyJobCommands
{
    public static CommandResult EnqueueBaselineRefresh(string fwDataPath, string productVersion) =>
        CommandTextRenderer.Render(
            JobCommands.EnqueueBaselineRefresh(new EnqueueBaselineRefreshRequest(fwDataPath, productVersion)),
            asJson: false, successAsJson: false);

    public static CommandResult EnqueueDryRun(
        string fwDataPath, string productVersion, string proposalId, UsageLog? usage = null) =>
        CommandTextRenderer.Render(
            JobCommands.EnqueueDryRun(new EnqueueDryRunRequest(fwDataPath, productVersion, proposalId), usage),
            asJson: false, successAsJson: false);

    public static CommandResult EnqueueTrial(
        string fwDataPath, string productVersion, string proposalId, string? scope = null, UsageLog? usage = null) =>
        CommandTextRenderer.Render(
            JobCommands.EnqueueTrial(
                new EnqueueTrialRequest(fwDataPath, productVersion, proposalId, scope), usage),
            asJson: false, successAsJson: false);

    public static CommandResult WaitForDryRun(
        string fwDataPath, string productVersion, string proposalId, string jobId, bool asJson, TimeSpan timeout) =>
        CommandTextRenderer.Render(
            JobCommands.WaitForDryRun(
                new WaitForDryRunRequest(fwDataPath, productVersion, proposalId, jobId, timeout)),
            asJson);

    public static CommandResult WaitForJob(
        string fwDataPath, string jobId, string productVersion, bool asJson, TimeSpan timeout) =>
        CommandTextRenderer.Render(
            JobCommands.WaitForJob(new WaitForJobRequest(fwDataPath, jobId, productVersion, timeout)), asJson);

    public static CommandResult Show(string fwDataPath, string jobId, string productVersion, bool asJson) =>
        CommandTextRenderer.Render(JobCommands.Show(new ShowJobRequest(fwDataPath, jobId, productVersion)), asJson);

    public static CommandResult Assessments(
        string fwDataPath, string jobId, string productVersion, bool asJson) =>
        CommandTextRenderer.Render(
            JobCommands.Assessments(new JobAssessmentsRequest(fwDataPath, jobId, productVersion)), asJson);

    public static CommandResult Cancel(string fwDataPath, string jobId, string productVersion, bool asJson) =>
        CommandTextRenderer.Render(
            JobCommands.Cancel(new CancelJobRequest(fwDataPath, jobId, productVersion)), asJson);

    public static CommandResult Requeue(string fwDataPath, string jobId, string productVersion, bool asJson) =>
        CommandTextRenderer.Render(
            JobCommands.Requeue(new RequeueJobRequest(fwDataPath, jobId, productVersion)), asJson);

    public static CommandResult ListAll(string productVersion, bool asJson) =>
        CommandTextRenderer.Render(JobCommands.ListAll(new ListActiveJobsRequest(productVersion)), asJson);

    public static CommandResult Move(
        string fwDataPath, string jobId, string productVersion, JobMoveTarget target, bool asJson) =>
        CommandTextRenderer.Render(
            JobCommands.Move(new MoveJobRequest(fwDataPath, jobId, productVersion, target)), asJson);
}

using System;
using System.Diagnostics;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Cli.Rendering;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>
/// A Draft is a <c>Proposals</c> row (ADR 0041 decision 3); <c>discard-draft</c> is the only verb that
/// removes one. A never-finalized Draft has no committed revision behind it, so its row is deleted
/// outright. A Draft <c>reopen</c> produced carries its source Proposal's committed <c>ProposalRevisions</c>
/// and <c>Decisions</c> forward under the same id instead, so discarding it clears only the two columns
/// <c>reopen</c> set — the exact inverse — leaving the Proposal exactly at its prior committed revision.
/// </summary>
public sealed class DiscardDraftTests : IDisposable
{
    private const string ProductVersion = "1.0";
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "motif-discard-draft-" + Guid.NewGuid().ToString("N"));

    public DiscardDraftTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Project, string.Empty);
    }

    private string Project => Path.Combine(_root, "project.fwdata");

    private CommandOutcome<DraftCreatedResponse> NewDraft(
        string fwDataPath, string productVersion, string draftName, string? label) =>
        ProposalCommands.New(new NewDraftRequest(fwDataPath, productVersion, draftName, label));

    private CommandOutcome<SetGlossAddedResponse> AddSetGloss(
        string fwDataPath, string productVersion, string draftName, string target, string ws, string text) =>
        ProposalCommands.AddSetGloss(new AddSetGlossRequest(
            fwDataPath, productVersion, draftName, target, ws, text));

    private CommandOutcome<ProposalFinalizedResponse> FinalizeDraft(
        string fwDataPath, string productVersion, string draftName) =>
        ProposalCommands.Finalize(new FinalizeRequest(fwDataPath, productVersion, draftName));

    private CommandOutcome<ReopenedResponse> ReopenDraft(
        string fwDataPath, string productVersion, string draftName, string proposalId) =>
        ProposalCommands.Reopen(new ReopenRequest(fwDataPath, productVersion, draftName, proposalId));

    private CommandOutcome<DraftDiscardedResponse> DiscardDraft(
        string fwDataPath, string productVersion, string draftName) =>
        ProposalCommands.DiscardDraft(new DiscardDraftRequest(fwDataPath, productVersion, draftName));

    private CommandOutcome<ProposalDetailProjection> ShowProposal(
        string fwDataPath, string productVersion, string proposalId) =>
        ProposalCommands.Show(new ShowProposalRequest(fwDataPath, productVersion, proposalId));
    [Fact]
    public void DiscardingANeverFinalizedDraftRemovesItAndFreesTheNameImmediately()
    {
        Assert.True(NewDraft(Project, ProductVersion, "d", "a label").Succeeded);

        var discarded = DiscardDraft(Project, ProductVersion, "d");

        Assert.True(discarded.Succeeded);
        Assert.Equal("d", discarded.Value!.DraftName);
        Assert.False(discarded.Value.WasReopened);
        Assert.False(OpenRepository().DraftNameExists("d"));

        // Reusable immediately, not merely absent from a listing: a fresh 'new' under the same name succeeds.
        var recreated = NewDraft(Project, ProductVersion, "d", "second label");
        Assert.True(recreated.Succeeded);
    }

    [Fact]
    public void DiscardingAnUnknownNameRefusesNotFound()
    {
        var result = DiscardDraft(Project, ProductVersion, "nope");

        Assert.False(result.Succeeded);
        Assert.Equal(FailureReason.NotFound, result.Refusal!.Reason);
        Assert.Contains("not found", result.Refusal.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DiscardingAFinalizedProposalsIdRefusesNotFoundJustLikeAnAbsentDraft()
    {
        // A finalized Proposal has no DraftName row, so this takes the same NotFound path as an unknown name.
        Assert.True(NewDraft(Project, ProductVersion, "d", null).Succeeded);
        Assert.True(AddSetGloss(Project, ProductVersion, "d", CanonicalId.Mint().Value, "en", "hello").Succeeded);
        DraftRationale.Author(Project, "d", "a label", "a comment");
        var finalize = FinalizeDraft(Project, ProductVersion, "d");
        Assert.True(finalize.Succeeded);
        var proposalId = finalize.Value!.ProposalId;

        var result = DiscardDraft(Project, ProductVersion, proposalId);

        Assert.False(result.Succeeded);
        Assert.Equal(FailureReason.NotFound, result.Refusal!.Reason);
    }

    [Fact]
    public void DiscardingADraftReopenedFromAFinalizedProposalRevertsToItsPriorCommittedRevision()
    {
        Assert.True(NewDraft(Project, ProductVersion, "d", null).Succeeded);
        Assert.True(AddSetGloss(Project, ProductVersion, "d", CanonicalId.Mint().Value, "en", "hello").Succeeded);
        DraftRationale.Author(Project, "d", "a label", "a comment");
        var finalize = FinalizeDraft(Project, ProductVersion, "d");
        Assert.True(finalize.Succeeded);
        var proposalId = finalize.Value!.ProposalId;
        var before = OpenRepository().Get(CanonicalId.Parse(proposalId));
        Assert.True(ReopenDraft(Project, ProductVersion, "reopened", proposalId).Succeeded);

        var result = DiscardDraft(Project, ProductVersion, "reopened");

        Assert.True(result.Succeeded);
        Assert.Equal("reopened", result.Value!.DraftName);
        Assert.True(result.Value.WasReopened);

        // The draft name is gone, and the Proposal is listed as committed again, not as a draft.
        var repository = OpenRepository();
        Assert.False(repository.DraftNameExists("reopened"));
        var after = repository.Get(CanonicalId.Parse(proposalId));
        Assert.Null(after.DraftName);
        // Every committed revision behind it is exactly as it was before the reopen.
        Assert.Equal(before.IntentDigest, after.IntentDigest);
        Assert.Equal(before.ProposalJson, after.ProposalJson);
        Assert.Equal(before.Status, after.Status);

        // The name is free again, not merely absent from a listing: a fresh 'new' under it succeeds.
        Assert.True(NewDraft(Project, ProductVersion, "reopened", null).Succeeded);
    }

    [Fact]
    public void ShowRendersTheSameProposalAfterReopenThenDiscardAsItDidBeforeTheReopen()
    {
        Assert.True(NewDraft(Project, ProductVersion, "d", null).Succeeded);
        Assert.True(AddSetGloss(Project, ProductVersion, "d", CanonicalId.Mint().Value, "en", "hello").Succeeded);
        DraftRationale.Author(Project, "d", "a label", "a comment");
        var finalize = FinalizeDraft(Project, ProductVersion, "d");
        Assert.True(finalize.Succeeded);
        var proposalId = finalize.Value!.ProposalId;
        var shownBefore = ShowProposal(Project, ProductVersion, proposalId);
        Assert.True(shownBefore.Succeeded);

        Assert.True(ReopenDraft(Project, ProductVersion, "reopened", proposalId).Succeeded);
        Assert.True(DiscardDraft(Project, ProductVersion, "reopened").Succeeded);

        var shownAfter = ShowProposal(Project, ProductVersion, proposalId);
        Assert.True(shownAfter.Succeeded);
        Assert.Equal(
            ProposalCommandRenderer.Render(shownBefore, asJson: false).Output,
            ProposalCommandRenderer.Render(shownAfter, asJson: false).Output);
    }

    /// <summary>Drives the real executable, since this is a new verb on the published argv surface.</summary>
    [Fact]
    public void ArgvDiscardDraftDispatchesAndFreesTheNameForANewDraftOfTheSameName()
    {
        Assert.Equal(0, Run($"new --project \"{Project}\" --draft argv-d").ExitCode);

        var discarded = Run($"discard-draft --project \"{Project}\" --draft argv-d");
        Assert.Equal(0, discarded.ExitCode);
        Assert.Equal(string.Empty, discarded.Error);
        Assert.Contains("Discarded draft 'argv-d'", discarded.Output);

        var recreated = Run($"new --project \"{Project}\" --draft argv-d");
        Assert.Equal(0, recreated.ExitCode);
    }

    [Fact]
    public void ArgvDiscardDraftOfAnUnknownNameRefusesWithNotFoundExitCode()
    {
        var result = Run($"discard-draft --project \"{Project}\" --draft nope --json");

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(FailureReason.NotFound, Envelope(result.Error).Reason);
    }

    [Fact]
    public void ArgvOmittingTheDraftFlagIsAUsageFailureNamingTheVerb()
    {
        var result = Run($"discard-draft --project \"{Project}\"");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Usage: motif discard-draft --project <fwdata> --draft <name>", result.Error, StringComparison.Ordinal);
    }

    private IProposalRepository OpenRepository() => new ProposalRepository(ProjectMotifDatabase.Open(Project));

    private static FailureEnvelope Envelope(string stderr) =>
        ProjectionJson.Deserialize<FailureEnvelope>(stderr)!;

    private static CliRun Run(string arguments)
    {
        var executable = BuildOutput.Cli;
        var start = new ProcessStartInfo(executable)
        {
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = Process.Start(start)!;
        // Both pipes drain concurrently: a sequential read deadlocks past the pipe buffer.
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        var output = outputTask.GetAwaiter().GetResult();
        var error = errorTask.GetAwaiter().GetResult();
        Assert.True(process.WaitForExit(60000), "The CLI did not exit within its bound.");
        return new CliRun(process.ExitCode, output, error);
    }

    private sealed record CliRun(int ExitCode, string Output, string Error);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
    }
}

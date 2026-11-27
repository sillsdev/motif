using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using SIL.LCModel;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group0)]
public sealed class ApplyReconciliationTests(PristineProjectFixture pristine)
{
    [Theory]
    [InlineData("declined", "Declined")]
    [InlineData("withdrawn", "Withdrawn")]
    [InlineData("superseded", "Superseded")]
    public void ApplyRefusesAClosedProposalWithoutChangingTheProject(string status, string statusWord)
    {
        var seed = pristine.Seed;
        string projectPath;
        using (var scratch = pristine.NewScratch())
            projectPath = scratch.ProjectId.Path;

        var proposalId = CreateMeasuredProposal(projectPath, "closed-" + status,
            CanonicalId.FromGuid(seed.FirstSenseId).Value, SeededProject.FirstGloss + " proposed");
        string? replacementId = null;
        switch (status)
        {
            case "declined":
                Assert.True(ProposalCommands.Reject(new RejectRequest(projectPath, "1.0", proposalId)).Succeeded);
                break;
            case "withdrawn":
                Assert.True(ProposalCommands.Withdraw(new RejectRequest(projectPath, "1.0", proposalId)).Succeeded);
                break;
            case "superseded":
                replacementId = CreateMeasuredProposal(projectPath, "replacement",
                    CanonicalId.FromGuid(seed.FirstSenseId).Value, SeededProject.FirstGloss + " replacement");
                Assert.True(ProposalCommands.Supersede(new SupersedeRequest(
                    projectPath, "1.0", proposalId, replacementId)).Succeeded);
                break;
        }

        var outcome = ProposalCommands.Apply(new ApplyRequest(projectPath, "1.0", proposalId));

        Assert.False(outcome.Succeeded);
        Assert.Equal("proposal.invalid-status", outcome.Refusal?.Code);
        Assert.Equal(FailureReason.Refused, outcome.Refusal?.Reason);
        Assert.Contains(statusWord, outcome.Refusal!.Message, StringComparison.Ordinal);
        if (replacementId is null)
            Assert.Contains("Reopen", outcome.Refusal.Message, StringComparison.Ordinal);
        else
        {
            Assert.Contains(replacementId, outcome.Refusal.Message, StringComparison.Ordinal);
            Assert.Contains("Open", outcome.Refusal.Message, StringComparison.Ordinal);
        }
        AssertGlossOnDisk(projectPath, seed.FirstSenseId, NewLangProjFixture.AnalysisTag, SeededProject.FirstGloss);
    }

    [Fact]
    public void ADeferredProposalCanStillBeApplied()
    {
        var seed = pristine.Seed;
        string projectPath;
        using (var scratch = pristine.NewScratch())
            projectPath = scratch.ProjectId.Path;

        var gloss = SeededProject.FirstGloss + " deferred";
        var proposalId = CreateMeasuredProposal(projectPath, "deferred-apply",
            CanonicalId.FromGuid(seed.FirstSenseId).Value, gloss);
        Assert.True(ProposalCommands.Defer(new DeferRequest(projectPath, "1.0", proposalId)).Succeeded);

        var outcome = ProposalCommands.Apply(new ApplyRequest(projectPath, "1.0", proposalId, Force: true));

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        Assert.Equal(proposalId, outcome.Value!.ProposalId);
        AssertGlossOnDisk(projectPath, seed.FirstSenseId, NewLangProjFixture.AnalysisTag, gloss);
    }

    [Fact]
    public void PendingDraftCannotBeStagedBetweenAuthorizationAndMutation()
    {
        string path;
        using (var scratch = pristine.NewScratch()) path = scratch.ProjectId.Path;
        var proposalId = CreateMeasuredProposal(path, "race-target",
            CanonicalId.FromGuid(pristine.Seed.FirstSenseId).Value, SeededProject.FirstGloss + " applied");
        Assert.True(ProposalCommands.New(new NewDraftRequest(path, "1.0", PendingChanges.DraftName, "Pending")).Succeeded);
        CommandOutcome<SetGlossAddedResponse>? staged = null;
        var reachedSave = false;
        try
        {
            ProposalCommands.BeforeDraftSave.Value = _ => reachedSave = true;
            ProposalCommands.ApplyLoaderOverride.Value = () =>
            {
                staged = ProposalCommands.AddSetGloss(new AddSetGlossRequest(path, "1.0", PendingChanges.DraftName,
                    CanonicalId.FromGuid(pristine.Seed.FirstSenseId).Value, NewLangProjFixture.AnalysisTag, "pending"));
                return new FwDataProjectLoader();
            };
            var applied = ProposalCommands.Apply(new ApplyRequest(path, "1.0", proposalId, Force: true));
            Assert.True(reachedSave);
            Assert.NotNull(staged);
            Assert.True(applied.Succeeded, applied.Refusal?.Message);
            Assert.Equal("draft.revision-conflict", staged!.Refusal?.Code);
        }
        finally
        {
            ProposalCommands.BeforeDraftSave.Value = null;
            ProposalCommands.ApplyLoaderOverride.Value = null;
        }
    }

    [Fact]
    public void ReopeningThePendingProposalCannotRaceAnApply()
    {
        string path;
        using (var scratch = pristine.NewScratch()) path = scratch.ProjectId.Path;
        using (var database = ProjectMotifDatabase.Open(path))
            PendingChanges.EnsurePendingDraft(database, new ProposalRepository(database));
        Assert.True(ProposalCommands.AddSetGloss(new AddSetGlossRequest(path, "1.0", PendingChanges.DraftName,
            CanonicalId.FromGuid(pristine.Seed.FirstSenseId).Value, NewLangProjFixture.AnalysisTag,
            SeededProject.FirstGloss + " pending")).Succeeded);
        DraftRationale.Author(path, PendingChanges.DraftName, "Clarify a gloss", "Record the intended meaning.");
        var finalized = ProposalCommands.Finalize(new FinalizeRequest(path, "1.0", PendingChanges.DraftName));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var proposalId = finalized.Value!.ProposalId;
        var request = new ReopenRequest(path, "1.0", PendingChanges.DraftName, proposalId);
        using (var lease = ProjectApplyLease.TryAcquire(path))
        {
            Assert.NotNull(lease);
            var refused = ProposalCommands.Reopen(request);
            Assert.Equal(RefusalCodes.ApplyInProgress, refused.Refusal?.Code);
        }
        var reopened = ProposalCommands.Reopen(request);
        Assert.True(reopened.Succeeded, reopened.Refusal?.Message);
    }

    [Fact]
    public void CoreApplyRechecksThePendingDraftBeforeMutation()
    {
        string path;
        using (var scratch = pristine.NewScratch()) path = scratch.ProjectId.Path;
        var target = CanonicalId.FromGuid(pristine.Seed.FirstSenseId).Value;
        var proposalId = CreateMeasuredProposal(path, "recheck-target", target, SeededProject.FirstGloss + " applied");
        Assert.True(ProposalCommands.New(new NewDraftRequest(path, "1.0", PendingChanges.DraftName, "Pending")).Succeeded);
        string emptyJson;
        using (var database = ProjectMotifDatabase.Open(path))
            emptyJson = new ProposalRepository(database).GetDraft(PendingChanges.DraftName).ProposalJson!;
        Assert.True(ProposalCommands.AddSetGloss(new AddSetGlossRequest(path, "1.0", PendingChanges.DraftName,
            target, NewLangProjFixture.AnalysisTag, "pending")).Succeeded);
        string stagedJson;
        using (var database = ProjectMotifDatabase.Open(path))
        {
            var repository = new ProposalRepository(database);
            stagedJson = repository.GetDraft(PendingChanges.DraftName).ProposalJson!;
            Assert.True(repository.TrySaveDraft(PendingChanges.DraftName, stagedJson, emptyJson));
        }
        try
        {
            ProposalCommands.ApplyLoaderOverride.Value = () =>
            {
                using var database = ProjectMotifDatabase.Open(path);
                Assert.True(new ProposalRepository(database).TrySaveDraft(PendingChanges.DraftName, emptyJson, stagedJson));
                return new FwDataProjectLoader();
            };
            var applied = ProposalCommands.Apply(new ApplyRequest(path, "1.0", proposalId, Force: true));
            Assert.Equal(RefusalCodes.ApplyPendingChanges, applied.Refusal?.Code);
            AssertGlossOnDisk(path, pristine.Seed.FirstSenseId, NewLangProjFixture.AnalysisTag, SeededProject.FirstGloss);
        }
        finally { ProposalCommands.ApplyLoaderOverride.Value = null; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CoreApplyRefusesAFinalizedProposalWhilePendingChangesExist(bool force)
    {
        string path;
        using (var scratch = pristine.NewScratch()) path = scratch.ProjectId.Path;
        var target = CanonicalId.FromGuid(pristine.Seed.FirstSenseId).Value;
        var proposalId = CreateMeasuredProposal(path, "other-proposal", target, SeededProject.FirstGloss + " applied");
        Assert.True(ProposalCommands.New(new NewDraftRequest(path, "1.0", PendingChanges.DraftName, "Pending")).Succeeded);
        Assert.True(ProposalCommands.AddSetGloss(new AddSetGlossRequest(path, "1.0", PendingChanges.DraftName,
            target, NewLangProjFixture.AnalysisTag, "pending")).Succeeded);
        var outcome = ProposalCommands.Apply(new ApplyRequest(path, "1.0", proposalId, force));
        Assert.Equal(RefusalCodes.ApplyPendingChanges, outcome.Refusal?.Code);
        AssertGlossOnDisk(path, pristine.Seed.FirstSenseId, NewLangProjFixture.AnalysisTag, SeededProject.FirstGloss);
    }

    [Fact]
    public void CoreApplyAllowsAFinalizedProposalWhenThePendingDraftIsEmpty()
    {
        string path;
        using (var scratch = pristine.NewScratch()) path = scratch.ProjectId.Path;
        var gloss = SeededProject.FirstGloss + " after empty";
        var proposalId = CreateMeasuredProposal(path, "empty-pending-target",
            CanonicalId.FromGuid(pristine.Seed.FirstSenseId).Value, gloss);
        Assert.True(ProposalCommands.New(new NewDraftRequest(path, "1.0", PendingChanges.DraftName, "Pending")).Succeeded);
        var outcome = ProposalCommands.Apply(new ApplyRequest(path, "1.0", proposalId, Force: true));
        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        AssertGlossOnDisk(path, pristine.Seed.FirstSenseId, NewLangProjFixture.AnalysisTag, gloss);
    }

    [Fact]
    public void ReceiptWriteFailureAfterSavedApplyIsStoreInconsistent()
    {
        var seed = pristine.Seed;
        var gloss = SeededProject.FirstGloss + " receipt boundary";
        string projectPath;
        using (var scratch = pristine.NewScratch())
            projectPath = scratch.ProjectId.Path;

        const string draftName = "receipt-boundary-demo";
        var proposalId = CreateMeasuredProposal(projectPath, draftName,
            CanonicalId.FromGuid(seed.FirstSenseId).Value, gloss);
        var previousFailurePath = Environment.GetEnvironmentVariable("MOTIF_TEST_FAIL_RECEIPT_WRITE_FOR");
        try
        {
            Environment.SetEnvironmentVariable("MOTIF_TEST_FAIL_RECEIPT_WRITE_FOR", projectPath);
            var outcome = ProposalCommands.Apply(new ApplyRequest(
                projectPath, "1.0", proposalId, Force: true));

            Assert.False(outcome.Succeeded);
            Assert.Equal("apply.reconciliation-needed", outcome.Refusal!.Code);
            Assert.Equal(FailureReason.StoreInconsistent, outcome.Refusal.Reason);
            Assert.Equal(4, FailureEnvelope.ExitCodeFor(outcome.Refusal.Reason));
        }
        finally
        {
            Environment.SetEnvironmentVariable("MOTIF_TEST_FAIL_RECEIPT_WRITE_FOR", previousFailurePath);
        }

        AssertGlossOnDisk(projectPath, seed.FirstSenseId, NewLangProjFixture.AnalysisTag, gloss);

        using var failedStore = ProjectMotifDatabase.Open(projectPath);
        using var connection = failedStore.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Receipts WHERE ProposalId = $proposal;";
        command.Parameters.AddWithValue("$proposal", proposalId);
        Assert.Equal(0L, (long)command.ExecuteScalar()!);
    }

    // A save that returns without reaching the file is not logged as applied.
    [Fact]
    public void ASaveThatDoesNotLandIsRefusedAndNothingIsRecordedAsApplied()
    {
        var seed = pristine.Seed;
        var gloss = SeededProject.FirstGloss + " unsaved";
        string projectPath;
        using (var scratch = pristine.NewScratch())
            projectPath = scratch.ProjectId.Path;

        var proposalId = CreateMeasuredProposal(projectPath, "unsaved-demo",
            CanonicalId.FromGuid(seed.FirstSenseId).Value, gloss);
        var savedPause = ApplySaveCheck.Pause;
        try
        {
            ApplySaveCheck.Pause = TimeSpan.FromMilliseconds(10);
            ProposalCommands.ApplyLoaderOverride.Value = static () => new SaveThatDoesNotLand();
            var outcome = ProposalCommands.Apply(new ApplyRequest(projectPath, "1.0", proposalId, Force: true));

            Assert.False(outcome.Succeeded);
            Assert.Equal("apply.not-saved", outcome.Refusal!.Code);
            Assert.Contains("Nothing was recorded as applied", outcome.Refusal.Message, StringComparison.Ordinal);
            Assert.Contains("applying again is safe", outcome.Refusal.Message, StringComparison.Ordinal);
        }
        finally
        {
            ProposalCommands.ApplyLoaderOverride.Value = null;
            ApplySaveCheck.Pause = savedPause;
        }

        AssertGlossOnDisk(projectPath, seed.FirstSenseId, NewLangProjFixture.AnalysisTag, SeededProject.FirstGloss);
        using var store = ProjectMotifDatabase.Open(projectPath);
        using var connection = store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Receipts WHERE ProposalId = $proposal;";
        command.Parameters.AddWithValue("$proposal", proposalId);
        Assert.Equal(0L, (long)command.ExecuteScalar()!);
    }

    // The save lands during the third pause; a fake sleeper keeps the test independent of the clock.
    [Fact]
    public void ASaveThatLandsLateIsFoundByTheBoundedReRead()
    {
        var path = Path.Combine(Path.GetTempPath(), "apply-late-" + Guid.NewGuid().ToString("N") + ".fwdata");
        var id = Guid.NewGuid();
        var savedSleep = ApplySaveCheck.Sleep;
        var pauses = 0;
        try
        {
            File.WriteAllText(path, "<x/>");
            ApplySaveCheck.Sleep = _ =>
            {
                if (++pauses == 3)
                    File.WriteAllText(path, "<x><Version val=\"" + id.ToString("D") + "\"/></x>");
            };
            ApplySaveCheck.RequireOnDisk(path, id);
            Assert.True(pauses >= 1);
        }
        finally
        {
            ApplySaveCheck.Sleep = savedSleep;
            File.Delete(path);
        }
    }

    private sealed class SaveThatDoesNotLand : FwDataProjectLoader
    {
        public override void Save(LcmCache cache) { }
    }

    // After the project is saved, a busy store is an unknown outcome to reconcile, never a Busy to retry.
    [Fact]
    public void ABusyStoreAfterApplySavedTheProjectNeedsReconciliationNotARetry()
    {
        var seed = pristine.Seed;
        var gloss = SeededProject.FirstGloss + " busy receipt";
        string projectPath;
        using (var scratch = pristine.NewScratch())
            projectPath = scratch.ProjectId.Path;

        var proposalId = CreateMeasuredProposal(projectPath, "busy-receipt-demo",
            CanonicalId.FromGuid(seed.FirstSenseId).Value, gloss);
        var previous = Environment.GetEnvironmentVariable("MOTIF_TEST_BUSY_RECEIPT_WRITE_FOR");
        try
        {
            Environment.SetEnvironmentVariable("MOTIF_TEST_BUSY_RECEIPT_WRITE_FOR", projectPath);
            var outcome = ProposalCommands.Apply(new ApplyRequest(projectPath, "1.0", proposalId, Force: true));

            Assert.False(outcome.Succeeded);
            Assert.Equal("apply.reconciliation-needed", outcome.Refusal!.Code);
            Assert.Equal(FailureReason.StoreInconsistent, outcome.Refusal.Reason);
            Assert.Equal("5", outcome.Refusal.Facts["causeSqliteCode"]);
            Assert.Contains("SqliteException", outcome.Refusal.Facts["cause"], StringComparison.Ordinal);
            Assert.Contains("motif apply " + proposalId, outcome.Refusal.Message, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MOTIF_TEST_BUSY_RECEIPT_WRITE_FOR", previous);
        }
        AssertGlossOnDisk(projectPath, seed.FirstSenseId, NewLangProjFixture.AnalysisTag, gloss);
        Assert.True(ProjectReconciliationMarker.Exists(projectPath));
        Assert.Equal("proposed", StoredStatus(projectPath, proposalId));

        var recovered = ProposalCommands.Apply(new ApplyRequest(projectPath, "1.0", proposalId, Force: true));

        Assert.True(recovered.Succeeded, recovered.Refusal?.Message);
        Assert.True(recovered.Value!.AlreadyApplied);
        Assert.Contains("recorded", recovered.Value.ResultNote, StringComparison.Ordinal);
        Assert.False(ProjectReconciliationMarker.Exists(projectPath));
        Assert.Equal("applied", StoredStatus(projectPath, proposalId));
        AssertGlossOnDisk(projectPath, seed.FirstSenseId, NewLangProjFixture.AnalysisTag, gloss);
    }

    // A marker that could not be written leaves only the store saying `proposed`; applying again still repairs it.
    [Fact]
    public void ApplyingAgainRecordsAnApplyTheStoreNeverHeardOfEvenWithoutAMarker()
    {
        var seed = pristine.Seed;
        var gloss = SeededProject.FirstGloss + " unrecorded no marker";
        string projectPath;
        using (var scratch = pristine.NewScratch())
            projectPath = scratch.ProjectId.Path;
        var proposalId = CreateMeasuredProposal(projectPath, "unrecorded-no-marker",
            CanonicalId.FromGuid(seed.FirstSenseId).Value, gloss);
        Assert.True(ProposalCommands.Apply(new ApplyRequest(projectPath, "1.0", proposalId, Force: true)).Succeeded);
        using (var database = ProjectMotifDatabase.Open(projectPath))
        using (var connection = database.OpenConnection())
        {
            foreach (var sql in new[] { "DELETE FROM Receipts;", "UPDATE Proposals SET Status = 'proposed';" })
            {
                using var command = connection.CreateCommand();
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
        }
        Assert.False(ProjectReconciliationMarker.Exists(projectPath));

        var recovered = ProposalCommands.Apply(new ApplyRequest(projectPath, "1.0", proposalId, Force: true));

        Assert.True(recovered.Succeeded, recovered.Refusal?.Message);
        Assert.True(recovered.Value!.AlreadyApplied);
        Assert.Equal("applied", StoredStatus(projectPath, proposalId));
    }

    // While the warning stands, an Apply the log does not hold is refused and the warning kept.
    [Fact]
    public void AProposalTheLogDoesNotHoldIsRefusedWhileTheWarningStands()
    {
        var seed = pristine.Seed;
        string projectPath;
        using (var scratch = pristine.NewScratch())
            projectPath = scratch.ProjectId.Path;
        var proposalId = CreateMeasuredProposal(projectPath, "not-in-log",
            CanonicalId.FromGuid(seed.FirstSenseId).Value, SeededProject.FirstGloss + " not in log");
        ProjectReconciliationMarker.Mark(projectPath, ReconciliationBoundary.Save, proposalId);

        var outcome = ProposalCommands.Apply(new ApplyRequest(projectPath, "1.0", proposalId, Force: true));

        Assert.False(outcome.Succeeded);
        Assert.Equal("apply.reconciliation-needed", outcome.Refusal!.Code);
        Assert.Contains("Refresh", outcome.Refusal.Message, StringComparison.Ordinal);
        Assert.True(ProjectReconciliationMarker.Exists(projectPath));
        AssertGlossOnDisk(projectPath, seed.FirstSenseId, NewLangProjFixture.AnalysisTag, SeededProject.FirstGloss);
    }

    [Fact]
    public void RecoveringOneProposalAlsoRecordsTheOtherSavedApplyAndThenClearsTheWarning()
    {
        string projectPath;
        using (var scratch = pristine.NewScratch())
            projectPath = scratch.ProjectId.Path;
        var (first, second) = TwoAppliedButUnrecorded(projectPath);
        ProjectReconciliationMarker.Mark(projectPath, ReconciliationBoundary.ReceiptRecording, first);
        ProjectReconciliationMarker.Mark(projectPath, ReconciliationBoundary.ReceiptRecording, second);

        Assert.True(ProposalCommands.Apply(new ApplyRequest(projectPath, "1.0", first, Force: true)).Succeeded);

        Assert.Equal("applied", StoredStatus(projectPath, second));
        Assert.False(ProjectReconciliationMarker.Exists(projectPath));
    }

    [Fact]
    public void RecoveringOneProposalKeepsTheWarningWhileAnotherSavedApplyCannotBeRecorded()
    {
        string projectPath;
        using (var scratch = pristine.NewScratch())
            projectPath = scratch.ProjectId.Path;
        var (first, second) = TwoAppliedButUnrecorded(projectPath);
        var digest = LoggedDigest(projectPath, second);
        File.WriteAllText(projectPath, File.ReadAllText(projectPath).Replace(digest, new string('0', digest.Length)));
        ProjectReconciliationMarker.Mark(projectPath, ReconciliationBoundary.ReceiptRecording, first);
        ProjectReconciliationMarker.Mark(projectPath, ReconciliationBoundary.ReceiptRecording, second);

        Assert.True(ProposalCommands.Apply(new ApplyRequest(projectPath, "1.0", first, Force: true)).Succeeded);

        Assert.Equal("proposed", StoredStatus(projectPath, second));
        Assert.True(ProjectReconciliationMarker.Exists(projectPath));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ACommandNeverClearsAWarningThatNeedsAPersonToLook(bool oldTextOnlyMarker)
    {
        string projectPath;
        using (var scratch = pristine.NewScratch())
            projectPath = scratch.ProjectId.Path;
        var (first, _) = TwoAppliedButUnrecorded(projectPath);
        if (oldTextOnlyMarker) ProjectReconciliationMarker.Mark(projectPath);
        else ProjectReconciliationMarker.Mark(projectPath, ReconciliationBoundary.Save, first);

        var outcome = ProposalCommands.Apply(new ApplyRequest(projectPath, "1.0", first, Force: true));

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        Assert.Equal("applied", StoredStatus(projectPath, first));
        Assert.True(ProjectReconciliationMarker.Exists(projectPath));
    }

    [Fact]
    public void RecordUnrecordedAppliesRecordsMatchesAndKeepsTheWarningForAMismatch()
    {
        string projectPath;
        using (var scratch = pristine.NewScratch())
            projectPath = scratch.ProjectId.Path;
        var (first, second) = TwoAppliedButUnrecorded(projectPath);
        var digest = LoggedDigest(projectPath, second);
        File.WriteAllText(projectPath, File.ReadAllText(projectPath).Replace(digest, new string('0', digest.Length)));
        ProjectReconciliationMarker.Mark(projectPath);

        var outcome = ProposalCommands.RecordUnrecordedApplies(projectPath, "1.0");

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        Assert.Equal(1, outcome.Value!.Recorded);
        Assert.Equal(1, outcome.Value.Unresolved);
        Assert.Equal("applied", StoredStatus(projectPath, first));
        Assert.Equal("proposed", StoredStatus(projectPath, second));
        Assert.True(ProjectReconciliationMarker.Exists(projectPath));
        var refused = ProposalCommands.Apply(new ApplyRequest(projectPath, "1.0", second, Force: true));
        Assert.Equal("apply.applied-content-mismatch", refused.Refusal?.Code);
        Assert.Contains("Refresh", refused.Refusal!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RecordUnrecordedAppliesClearsTheWarningOnceNothingIsOutstanding()
    {
        string projectPath;
        using (var scratch = pristine.NewScratch())
            projectPath = scratch.ProjectId.Path;
        var (first, second) = TwoAppliedButUnrecorded(projectPath);
        ProjectReconciliationMarker.Mark(projectPath, ReconciliationBoundary.Save, first);

        var outcome = ProposalCommands.RecordUnrecordedApplies(projectPath, "1.0");

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        Assert.Equal(2, outcome.Value!.Recorded);
        Assert.Equal(0, outcome.Value.Unresolved);
        Assert.Equal("applied", StoredStatus(projectPath, first));
        Assert.Equal("applied", StoredStatus(projectPath, second));
        Assert.False(ProjectReconciliationMarker.Exists(projectPath));
    }

    private (string First, string Second) TwoAppliedButUnrecorded(string projectPath)
    {
        var first = CreateMeasuredProposal(projectPath, "unrecorded-one",
            CanonicalId.FromGuid(pristine.Seed.FirstSenseId).Value, SeededProject.FirstGloss + " one");
        var second = CreateMeasuredProposal(projectPath, "unrecorded-two",
            CanonicalId.FromGuid(pristine.Seed.SecondSenseId).Value, SeededProject.FirstGloss + " two");
        string secondAnchor;
        using (var database = ProjectMotifDatabase.Open(projectPath))
            secondAnchor = new ProposalRepository(database).Get(CanonicalId.Parse(second)).AnchorJson!;
        Assert.True(ProposalCommands.Apply(new ApplyRequest(projectPath, "1.0", first, Force: true)).Succeeded);
        // Applying the first drifts every other anchor; the second's footprint is untouched, so its anchor still holds.
        using (var database = ProjectMotifDatabase.Open(projectPath))
            new ProposalRepository(database).SetAnchor(CanonicalId.Parse(second), secondAnchor);
        Assert.True(ProposalCommands.Apply(new ApplyRequest(projectPath, "1.0", second, Force: true)).Succeeded);
        var ids = new List<string> { first, second };
        using (var database = ProjectMotifDatabase.Open(projectPath))
        using (var connection = database.OpenConnection())
            foreach (var sql in new[] { "DELETE FROM Receipts;", "UPDATE Proposals SET Status = 'proposed';" })
            {
                using var command = connection.CreateCommand();
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
        return (ids[0], ids[1]);
    }

    private static string LoggedDigest(string projectPath, string proposalId)
    {
        using var database = ProjectMotifDatabase.Open(projectPath);
        var digest = new ProposalRepository(database).Get(CanonicalId.Parse(proposalId)).IntentDigest!;
        return digest["sha256:".Length..];
    }

    private static string StoredStatus(string projectPath, string proposalId)
    {
        using var database = ProjectMotifDatabase.Open(projectPath);
        return new ProposalRepository(database).Get(CanonicalId.Parse(proposalId)).Status;
    }

    [Fact]
    public void FailureAfterReceiptRecordingStillReturnsAppliedSuccess()
    {
        var seed = pristine.Seed;
        var gloss = SeededProject.FirstGloss + " post-receipt failure";
        string projectPath;
        using (var scratch = pristine.NewScratch())
            projectPath = scratch.ProjectId.Path;

        var proposalId = CreateMeasuredProposal(projectPath, "post-receipt-demo",
            CanonicalId.FromGuid(seed.FirstSenseId).Value, gloss);
        var previousFailurePath = Environment.GetEnvironmentVariable("MOTIF_TEST_FAIL_AFTER_RECEIPT_FOR");
        try
        {
            Environment.SetEnvironmentVariable("MOTIF_TEST_FAIL_AFTER_RECEIPT_FOR", projectPath);
            var outcome = ProposalCommands.Apply(new ApplyRequest(
                projectPath, "1.0", proposalId, Force: true));

            Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
            Assert.Equal(proposalId, outcome.Value!.ProposalId);
            Assert.Contains("post-receipt housekeeping could not be completed", outcome.Value.ResultNote,
                StringComparison.Ordinal);
            var log = ProjectDiagnosticLog.PathFor(projectPath);
            Assert.Contains(log, outcome.Value.ResultNote, StringComparison.Ordinal);
            Assert.Contains("Injected post-receipt failure.", File.ReadAllText(log), StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MOTIF_TEST_FAIL_AFTER_RECEIPT_FOR", previousFailurePath);
        }

        AssertGlossOnDisk(projectPath, seed.FirstSenseId, NewLangProjFixture.AnalysisTag, gloss);
        using var store = ProjectMotifDatabase.Open(projectPath);
        using var connection = store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Receipts WHERE ProposalId = $proposal;";
        command.Parameters.AddWithValue("$proposal", proposalId);
        Assert.Equal(1L, (long)command.ExecuteScalar()!);
    }

    [Fact]
    public void ACacheThatFailsToCloseAfterApplyStillReturnsAppliedSuccessWithANote()
    {
        var seed = pristine.Seed;
        var gloss = SeededProject.FirstGloss + " dispose failure";
        string projectPath;
        using (var scratch = pristine.NewScratch())
            projectPath = scratch.ProjectId.Path;

        var proposalId = CreateMeasuredProposal(projectPath, "dispose-failure-demo",
            CanonicalId.FromGuid(seed.FirstSenseId).Value, gloss);
        var previousFailurePath = Environment.GetEnvironmentVariable("MOTIF_TEST_FAIL_CACHE_DISPOSE_FOR");
        try
        {
            Environment.SetEnvironmentVariable("MOTIF_TEST_FAIL_CACHE_DISPOSE_FOR", projectPath);
            var outcome = ProposalCommands.Apply(new ApplyRequest(projectPath, "1.0", proposalId, Force: true));

            Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
            Assert.Contains("could not close the project cleanly", outcome.Value!.ResultNote, StringComparison.Ordinal);
            Assert.Contains("Injected cache disposal failure.",
                File.ReadAllText(ProjectDiagnosticLog.PathFor(projectPath)), StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MOTIF_TEST_FAIL_CACHE_DISPOSE_FOR", previousFailurePath);
        }

        AssertGlossOnDisk(projectPath, seed.FirstSenseId, NewLangProjFixture.AnalysisTag, gloss);
    }

    [Fact]
    public void ApplysUnitOfWorkCannotBeCancelledByCtrlC()
    {
        // The CLI's Ctrl+C token reaches the Dry Run wait only; a cancelled Apply would be half a unit of work.
        var applies = typeof(ProposalCommands).GetMethods().Where(method => method.Name == "Apply").ToArray();

        Assert.NotEmpty(applies);
        Assert.All(applies, method => Assert.DoesNotContain(method.GetParameters(),
            parameter => parameter.ParameterType == typeof(System.Threading.CancellationToken)));
    }

    internal static string CreateMeasuredProposal(string projectPath, string draftName, string target, string gloss)
    {
        Assert.True(ProposalCommands.New(new NewDraftRequest(
            projectPath, "1.0", draftName, null)).Succeeded);
        Assert.True(ProposalCommands.AddSetGloss(new AddSetGlossRequest(
            projectPath, "1.0", draftName, target, NewLangProjFixture.AnalysisTag,
            gloss)).Succeeded);
        DraftRationale.Author(projectPath, draftName, "Clarify a gloss",
            "Record the intended lexical meaning before applying the Proposal.");
        var finalized = ProposalCommands.Finalize(new FinalizeRequest(projectPath, "1.0", draftName));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var dry = DryRunJobRunner.Run(projectPath, "1.0", finalized.Value!.ProposalId);
        Assert.True(dry.Succeeded, dry.Refusal?.Message);
        return finalized.Value.ProposalId;
    }

    private static void AssertGlossOnDisk(string projectPath, Guid senseGuid, string writingSystem, string expected)
    {
        using var cache = new FwDataProjectLoader().LoadScratchCache(projectPath);
        var wsHandle = cache.WritingSystemFactory.GetWsFromStr(writingSystem);
        var sense = cache.ServiceLocator.GetInstance<ILexSenseRepository>().GetObject(senseGuid);
        Assert.Equal(expected, sense.Gloss.get_String(wsHandle).Text);
    }

}

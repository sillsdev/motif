using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Host;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

[Collection(LcmCacheTestCollection.Name)]
public sealed class ProposalApplyWalkthroughTests(PristineProjectFixture pristine)
{
    [Fact]
    public void AProposalCanReachTheBackupConfirmationThroughTheWindow()
    {
        using var project = new WalkthroughProject(pristine);
        const string name = "clarify-gloss";
        const string label = "Clarify the gloss";
        var version = MotifProductVersion.CurrentText;
        var created = ProposalCommands.New(new NewDraftRequest(project.FwDataPath, version, name, label));
        Assert.True(created.Succeeded, created.Refusal?.Message);
        var gloss = ProposalCommands.AddSetGloss(new AddSetGlossRequest(project.FwDataPath, version, name,
            CanonicalId.FromGuid(project.Seed.FirstSenseId).Value, "en", "greeting"));
        Assert.True(gloss.Succeeded, gloss.Refusal?.Message);
        DraftRationale.Author(project.FwDataPath, name, name, "Use a more precise gloss.");
        var finalized = ProposalCommands.Finalize(new FinalizeRequest(project.FwDataPath, version, name));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var proposalId = finalized.Value!.ProposalId;
        var deadline = WalkthroughSteps.DeadlineIn(180);

        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            using var walkthrough = new WalkthroughWindow(project.ManagedRoot, project.FwDataPath,
                parserPath: FakeParser.ExecutablePath);
            var captureFolder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable);
            if (!string.IsNullOrEmpty(captureFolder)) walkthrough.Window.Height = 1150;
            walkthrough.Show();
            walkthrough.ChooseNewProject();
            if (!walkthrough.Workspace.Baseline.HasBaseline)
                walkthrough.ClickAutomationId(AutomationIds.RefreshProject);
            SetupWalkthroughActions.CaptureBaselineAndWaitForSetup(walkthrough, 1, WalkthroughSteps.Remaining(deadline));
            WalkthroughSteps.EnsureAssessmentForAnalyze(walkthrough, WalkthroughSteps.Remaining(deadline));

            walkthrough.ShowPage(WorkspacePage.Proposals);
            var rowId = AutomationIds.ForProposalRow(proposalId);
            walkthrough.ScrollIntoView(rowId);
            var row = Assert.IsType<ListBoxItem>(walkthrough.FindByAutomationId(rowId).FindAncestorOfType<ListBoxItem>());
            HeadlessClick.Click(walkthrough.Window, row, label);
            walkthrough.WaitUntil(() => walkthrough.FindOptionalByAutomationId(AutomationIds.ProposalStatus) is not null,
                WalkthroughSteps.Remaining(deadline), "the Proposal document did not open");
            var status = walkthrough.FindByAutomationId(AutomationIds.ProposalStatus);
            var document = Assert.IsType<ProposalDocumentViewModel>(status.DataContext);
            walkthrough.WaitUntil(() => document.Detail is not null && !document.IsLoading,
                WalkthroughSteps.Remaining(deadline), "the Proposal document did not load");
            Assert.False(document.CanPrepareApply);
            Assert.Contains("greeting", Assert.Single(document.OperationRows).Values);
            Capture("01-untried");

            walkthrough.ClickAutomationId(document.TryAutomationId);
            walkthrough.WaitUntil(() => document.Difference.OffersMake && !document.IsLoading,
                WalkthroughSteps.Remaining(deadline), "Try It did not offer to make a Difference");
            Assert.False(document.CanPrepareApply);
            var values = Assert.Single(document.OperationRows).Values;
            Assert.Contains("Gloss (en)", values);
            Assert.Contains($"{SeededProject.FirstGloss} → greeting", values);
            Capture("02-tried");

            walkthrough.ClickAutomationId(AutomationIds.ProposalDifferenceMake);
            walkthrough.WaitUntil(() => document.Difference.IsShown && !document.Difference.Maker.IsActive,
                WalkthroughSteps.Remaining(deadline), "Make Difference did not show the result");
            Assert.False(document.CanPrepareApply);
            using (var database = ProjectMotifDatabase.Open(project.FwDataPath))
                Assert.Null(new ProposalRepository(database).Get(CanonicalId.Parse(proposalId)).AnchorJson);
            Capture("03-difference-made");

            walkthrough.ClickAutomationId(AutomationIds.ProposalRunDryRun);
            walkthrough.WaitUntil(() => document.CanPrepareApply, WalkthroughSteps.Remaining(deadline),
                "Run Dry Run did not make Apply reachable",
                () => document.PrepareBlockReason ?? string.Empty);
            using (var database = ProjectMotifDatabase.Open(project.FwDataPath))
                Assert.NotNull(new ProposalRepository(database).Get(CanonicalId.Parse(proposalId)).AnchorJson);
            Capture("04-ready-to-apply");

            walkthrough.ClickAutomationId(AutomationIds.ProposalPrepareApply);
            walkthrough.WaitUntil(() => document.IsConfirmingApply, WalkthroughSteps.Remaining(deadline),
                "Apply did not open the confirmation");
            var confirmation = walkthrough.FindByAutomationId(AutomationIds.ProposalConfirmApply);
            Assert.True(confirmation.IsEffectivelyVisible);
            Assert.True(confirmation.IsEffectivelyEnabled);
            var backup = Assert.Single(walkthrough.Window.GetVisualDescendants().OfType<CopyableTextBlock>(),
                text => text.Name == "ApplyBackupNotice" && text.IsEffectivelyVisible);
            Assert.Equal(MotifAppStrings.ApplyBackupNotice, backup.Text);
            Capture("05-confirmation");
            Assert.Null(document.Receipt);
            Assert.Equal(project.SourceSha256, WalkthroughStoreAssertions.Sha256(project.FwDataPath));
            return Task.CompletedTask;

            void Capture(string state)
            {
                if (!string.IsNullOrEmpty(captureFolder))
                    PageScreenshots.Save(walkthrough.Window, Path.Combine(captureFolder, $"proposal-{state}.png"));
            }
        }, WalkthroughSteps.Remaining(deadline));
    }
}

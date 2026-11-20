using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.LCModel;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.App.Views.Words;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.App.Walkthrough;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.RealClient;

/// <summary>
/// The journey the screenshots showed broken in pieces, end to end through the real command client over a
/// disposable copy of the fixture project: approve a word in the word panel, see it become the pending Proposal,
/// check it (the Trial), apply it by pressing the buttons, and find both the receipt on screen and the approved
/// analysis in the FieldWorks copy.
/// </summary>
[Collection(LcmCacheTestCollection.Name)]
[Trait("MotifTestLevel", "Integration")]
public sealed class ApproveToApplyJourneyTests(PristineProjectFixture pristine)
{
    [Fact]
    public void ApprovingAWordBecomesAProposalThatIsCheckedAndAppliedToTheProject()
    {
        // FieldWorks has parsed the Text's paragraphs, which a decision about a place in them needs.
        using var project = new WalkthroughProject(pristine, configure: (cache, _, text) =>
            SIL.LCModel.Infrastructure.NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var seeded = cache.ServiceLocator.GetInstance<ITextRepository>().GetObject(text.TextId);
                foreach (var paragraph in seeded.ContentsOA.ParagraphsOS.OfType<IStTxtPara>())
                    paragraph.ParseIsCurrent = true;
            }));
        var parser = FakeParser.CopyRecordingInvocations(project.ManagedRoot);
        var deadline = WalkthroughSteps.DeadlineIn(240);
        var shown = new List<string>();
        var word = SeededProject.UnanalysedWordForm;
        var reading = new
        {
            morphs = new[]
            {
                new
                {
                    form = pristine.Seed.FirstLexemeFormId.ToString("D"),
                    msa = project.FirstMsaId.ToString("D"),
                    inflType = (string?)null,
                    guessedString = (string?)null,
                },
            },
        };

        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var walkthrough = new WalkthroughWindow(project.ManagedRoot, project.FwDataPath, parserPath: parser);
            walkthrough.Show();
            walkthrough.ChooseNewProject();
            walkthrough.WaitUntil(() => walkthrough.Workspace.Baseline.HasBaseline &&
                walkthrough.Workspace.Selection.Texts.Count == 1 && walkthrough.Workspace.Context.Setup?.IsOpen == true,
                WalkthroughSteps.Remaining(deadline), "the fixture project did not open");
            SetupWalkthroughActions.FinishFirstRun(walkthrough, SeededProject.TextTitle,
                SIL.Motif.Contract.Assess.ParserWorkLimit.DefaultWork.ToString(), WalkthroughSteps.Remaining(deadline));
            walkthrough.SetFakeParserBehavior(new
            {
                words = new object[]
                {
                    new { word, outcome = "complete", signature = "journey", analyses = new[] { reading } },
                },
            });
            walkthrough.TypePastedWords(word);
            var workspace = walkthrough.Workspace;
            workspace.Assess.RunCommand.Execute(null);
            walkthrough.WaitUntil(() => workspace.Assess.State == RunState.Completed &&
                workspace.Context.EvidencePublication.IsCompleted,
                WalkthroughSteps.Remaining(deadline), "the parse did not finish");

            // Words page: open the word, which shows the shared word panel.
            workspace.CurrentPage = WorkspacePage.Words;
            var words = workspace.PageModel<WordsPageModel>();
            words.Words.SearchText = word;
            walkthrough.WaitUntil(() => !words.Words.IsLoading && words.Words.Rows.Any(row => row.Form == word),
                WalkthroughSteps.Remaining(deadline), "the Words page did not list the word");
            PageScreenshots.Settle(walkthrough.Window);
            var row = words.Words.Rows.First(candidate => candidate.Form == word);
            row.Listed.IsOpen = true;
            await words.Words.CardPending;
            await words.PanelPending;
            PageScreenshots.Settle(walkthrough.Window);
            var panel = Assert.IsType<WordOccurrencesViewModel>(words.OccurrencePanel);
            var view = Assert.Single(walkthrough.Window.GetVisualDescendants().OfType<WordOccurrencesPanel>());
            if (panel.ChosenReading is null && panel.Readings.Count > 0) panel.ChosenReading = panel.Readings[0];
            panel.CheckShownGaps();
            Assert.True(panel.SelectedCount > 0, "the word has a place to approve");

            // What the person sees on the button, not the view model's label.
            var approve = Assert.Single(view.GetVisualDescendants().OfType<Button>(), button =>
                AutomationProperties.GetAutomationId(button) == AutomationIds.WordOccurrencesApprove);
            var buttonText = string.Join(" ", RenderedPageText.Visible(approve).Select(block => block.Text));
            // A wrong label does not stop the journey, so the rest of it is still exercised; it fails the test at the end.
            if (buttonText != panel.ApproveLabel)
                shown.Add($"the Approve button reads '{buttonText}', not '{panel.ApproveLabel}'");

            var staged = panel.SelectedCount;
            HeadlessClick.Click(walkthrough.Window, approve, panel.ApproveLabel);
            walkthrough.WaitUntil(() => workspace.Context.Changes.Items.Count >= staged,
                TimeSpan.FromSeconds(30), "Approve did not stage the places",
                () => $"status='{panel.StatusSentence}' selected={panel.SelectedCount} chosen='{panel.ChosenReading?.Label}' " +
                    $"canApprove={panel.ApproveCommand.CanExecute(null)} busy={panel.IsBusy} " +
                    $"refusal='{workspace.Context.Changes.LastRefusal?.Message}'");
            var approvals = workspace.Context.Changes.Items.Where(change => change.Kind == ChangeKinds.Approve).ToArray();
            Assert.Equal(staged, approvals.Length);

            // What the store records: the same approvals, as pending changes in the project's Motif file.
            var client = RealCommandClient.Create(project.ManagedRoot, parser);
            var stored = await client.LoadPendingChangesAsync(new PendingChangesRequest(project.FwDataPath, "1.0"),
                CancellationToken.None);
            Assert.True(stored.Succeeded, stored.Refusal?.Message);
            Assert.Equal(staged, stored.Value!.Changes.Count(change => change.Kind == ChangeKinds.Approve));
            Assert.NotNull(stored.Value.DraftId);

            // The approvals are one pending Proposal in the list, and it opens as a document.
            walkthrough.ShowPage(WorkspacePage.Proposals);
            var proposals = workspace.PageModel<ProposalsPageModel>();
            var listed = false;
            try
            {
                walkthrough.WaitUntil(() => proposals.Groups.SelectMany(group => group.Cards).Any(card => card.Card.IsPendingDraft),
                    TimeSpan.FromSeconds(15), "the pending Proposal was not listed");
                listed = true;
            }
            catch (Exception) { /* reported with the other faults below, after the rest of the journey ran */ }
            if (listed)
            {
                var pending = proposals.Groups.SelectMany(group => group.Cards).Single(card => card.Card.IsPendingDraft);
                shown.AddRange(pending.Label.Contains(pending.Card.ProposalId)
                    ? [$"the pending Proposal is titled by its id '{pending.Label}'"] : []);
                await proposals.OpenCommand.ExecuteAsync(pending);
            }
            else
            {
                shown.Add($"the Proposals page ({proposals.State}) does not list the {staged} approved place(s) as a pending Proposal");
                await workspace.Context.OpenProposalAsync(stored.Value.DraftId!, CancellationToken.None);
            }
            walkthrough.WaitUntil(() => proposals.SelectedDocument is { IsLoading: false },
                WalkthroughSteps.Remaining(deadline), "the pending Proposal did not open");
            PageScreenshots.Settle(walkthrough.Window);
            var document = proposals.SelectedDocument!;
            Assert.True(document.IsPending);

            // Check it (the Trial), prepare the Apply and confirm it, all by pressing buttons.
            walkthrough.SetFakeParserBehavior(new
            {
                words = new object[] { new { word, outcome = "complete", signature = "journey-after", analyses = new[] { reading } } },
            });
            var check = ButtonShowing(walkthrough, MotifAppStrings.ApplyCheckChanges);
            HeadlessClick.Click(walkthrough.Window, check, MotifAppStrings.ApplyCheckChanges);
            walkthrough.WaitUntil(() => !document.IsCheckingChanges &&
                string.IsNullOrWhiteSpace(document.ApplyBlockReason), TimeSpan.FromSeconds(60),
                "checking the changes did not clear what blocked Apply",
                () => $"blocked: '{document.ApplyBlockReason}' refusal='{document.PendingMeasurement.Refusal?.Sentence}' " +
                    $"measuring={document.PendingMeasurement.IsMeasuring} parser calls=[{string.Join(',', FakeParser.Invocations(parser))}]");

            var prepare = ButtonById(walkthrough, AutomationIds.ProposalPrepareApply);
            walkthrough.WaitUntil(() => prepare.IsEffectivelyEnabled, TimeSpan.FromSeconds(20),
                "the Apply button never became available after checking the changes",
                () => $"canPrepare={document.CanPrepareApply} loading={document.IsLoading} applying={workspace.Context.ProposalApply.IsApplying} " +
                    $"canExecute={document.PrepareApplyCommand.CanExecute(null)} block='{document.ApplyBlockReason}'");
            // Tells a press that never asked from a confirmation that a later read closed.
            var confirmationsOpened = 0;
            document.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ProposalDocumentViewModel.IsConfirmingApply) && document.IsConfirmingApply)
                    confirmationsOpened++;
            };
            HeadlessClick.Click(walkthrough.Window, prepare, MotifAppStrings.ProposalApplyPrepare);
            walkthrough.WaitUntil(() => document.IsConfirmingApply, WalkthroughSteps.Remaining(deadline),
                "Apply did not ask for confirmation",
                () => $"press ran={document.PrepareApplyCommand.ExecutionTask?.Status.ToString() ?? "never"} " +
                    $"running={document.PrepareApplyCommand.IsRunning} confirmations opened={confirmationsOpened} " +
                    $"apply refusal='{document.ApplyRefusal?.Sentence}' checking={document.IsCheckingChanges} " +
                    $"pending={document.IsPending} loading={document.IsLoading} outdated={document.IsOutdated} " +
                    $"canApply={document.CanApply} canPrepare={document.CanPrepareApply} block='{document.ApplyBlockReason}' " +
                    $"target='{workspace.Context.Changes.TargetProposalId}' applying={workspace.Context.ProposalApply.IsApplying} " +
                    $"hasReview={document.HasReview} stale={workspace.Context.Evidence.IsStale} lens='{workspace.Context.Lens?.ProposalId}' " +
                    $"reading={document.Reading.Status} measuring={document.PendingMeasurement.IsMeasuring} refusal='{document.PendingMeasurement.Refusal?.Sentence}'");
            Assert.Empty(StoredApproved(project.FwDataPath, pristine, word));
            var confirm = ButtonById(walkthrough, AutomationIds.ProposalConfirmApply);
            Assert.True(confirm.IsEffectivelyEnabled, document.ApplyBlockReason);
            HeadlessClick.Click(walkthrough.Window, confirm, MotifAppStrings.BoardApply);
            walkthrough.WaitUntil(() => document.HasReceipt, WalkthroughSteps.Remaining(deadline),
                "applying did not show a receipt", () => document.ApplyRefusal?.Sentence ?? "no refusal");

            // What the window says, and what the project holds.
            var receipt = walkthrough.Window.GetVisualDescendants().OfType<CopyableTextBlock>().Single(text =>
                AutomationProperties.GetAutomationId(text) == AutomationIds.ProposalReceipt);
            Assert.True(receipt.IsEffectivelyVisible);
            Assert.False(string.IsNullOrWhiteSpace(receipt.Text));
            Assert.Empty(workspace.Context.Changes.Items);
            Assert.Single(StoredApproved(project.FwDataPath, pristine, word));
        }, WalkthroughSteps.Remaining(deadline));

        Assert.True(shown.Count == 0, string.Join(Environment.NewLine, shown));
    }

    private static Button ButtonById(WalkthroughWindow walkthrough, string automationId) =>
        walkthrough.Window.GetVisualDescendants().OfType<Button>().Single(button =>
            AutomationProperties.GetAutomationId(button) == automationId && button.IsEffectivelyVisible);

    private static Button ButtonShowing(WalkthroughWindow walkthrough, string label) =>
        walkthrough.Window.GetVisualDescendants().OfType<Button>().Single(button =>
            button.IsEffectivelyVisible && Equals(button.Content, label));

    private static IReadOnlyList<Guid> StoredApproved(string fwDataPath, PristineProjectFixture pristine, string form)
    {
        using var cache = new FwDataProjectLoader().LoadScratchCache(fwDataPath);
        var wordform = cache.ServiceLocator.GetInstance<IWfiWordformRepository>().AllInstances()
            .Single(candidate => candidate.Form.get_String(cache.DefaultVernWs).Text == form);
        return wordform.AnalysesOC
            .Where(analysis => analysis.GetAgentOpinion(cache.LangProject.DefaultUserAgent) == Opinions.approves)
            .Select(analysis => analysis.Guid).ToArray();
    }
}

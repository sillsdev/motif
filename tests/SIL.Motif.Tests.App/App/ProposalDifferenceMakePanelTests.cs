using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.Controls;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.App.Views.Shell;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

// The Proposals inspector's Difference section, driven through the window the way a person would.
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ProposalDifferenceMakePanelTests
{
    private const string Project = @"C:\projects\one.fwdata";
    private static readonly Guid GenesisId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static ProposalCard Card(ProposalGroup group = ProposalGroup.WaitingForYou,
        TrialStanding? standing = null) =>
        new("p/one", "Open", "proposed", group, false, [new("rule", 1, "1 rule")],
            standing ?? TrialStanding.Current, null, []);

    private static async Task<(ProposalDifferencePanel Panel, MotifTestWindow Window)> OpenAsync(
        FakeCommandClient fake, ProposalCard card, bool withTexts = true)
    {
        fake.ProposalStandingsCompleteWith(new ProposalStandings([card], 0));
        fake.ListTextsCompletesWith(new TextInventoryResponse(
            withTexts ? [new TextChoiceSummary(GenesisId, "Genesis 1")] : [], HasBaseline: true));
        var context = WorkspaceContextTests.NewContext(fake);
        var model = new ProposalsPageModel(context);
        await context.OpenProjectAsync(Project);
        await model.DifferenceReading;
        var host = WorkspaceLayoutHost.ForPage(new ProposalsPage(model));
        var window = new MotifTestWindow { Width = 1600, Height = 1000, Content = host };
        window.Show();
        PageScreenshots.Settle(window);
        var checks = Assert.Single(host.GetVisualDescendants().OfType<Expander>(),
            expander => expander.Header is TextBlock { Text: var text } && text == MotifAppStrings.ProposalOptionalChecks);
        checks.IsExpanded = true;
        PageScreenshots.Settle(window);
        return (Assert.Single(host.GetVisualDescendants().OfType<ProposalDifferencePanel>()), window);
    }

    private static Control Named(ProposalDifferencePanel panel, string name) => panel.FindControl<Control>(name)!;

    private static string? Label(Button button) => Assert.IsType<TextBlock>(button.Content).Text;

    private static void Click(ProposalDifferencePanel panel, string name, MotifTestWindow window)
    {
        var button = Assert.IsAssignableFrom<Button>(Named(panel, name));
        Assert.True(button.IsEffectivelyEnabled, $"{name} is disabled");
        button.Command!.Execute(button.CommandParameter);
        PageScreenshots.Settle(window);
    }

    [Fact]
    public void AProposalWithNoDifferenceOffersMakingOneAndTheDifferenceAppearsWhenItIsReady()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            var fixture = new ProposalDifferenceFixture { TextId = GenesisId.ToString("D") };
            var made = false;
            fake.OnProposalDifference((request, token) => made
                ? fixture.Read(request, token)
                : Task.FromResult(CommandOutcome<ProposalDifferenceResponse>.Refused(new Refusal(
                    RefusalCodes.DifferenceNone, FailureReason.NotFound, "None."))));
            fake.OnProduceProposalDifference((request, _, token) =>
            {
                made = true;
                return fixture.Read(new ProposalDifferenceShowRequest(request.ProjectPath, request.ProposalId), token);
            });
            var (panel, window) = await OpenAsync(fake, Card());
            try
            {
                Assert.True(Named(panel, "DifferenceMakeControls").IsEffectivelyVisible);
                Assert.False(Named(panel, "DifferenceHeadline").IsEffectivelyVisible);
                Assert.False(Named(panel, "DifferenceMaking").IsEffectivelyVisible);
                var text = Assert.IsType<ComboBox>(Named(panel, "DifferenceText"));
                Assert.Equal("Genesis 1", Assert.IsType<DifferenceTextChoice>(text.SelectedItem).Title);
                var make = Assert.IsType<Button>(Named(panel, "DifferenceMake"));
                Assert.Equal("Make Difference", Label(make));

                Click(panel, "DifferenceMake", window);

                var request = Assert.Single(fake.ProduceProposalDifferenceRequests);
                Assert.Equal(new ProposalDifferenceProduceRequest(Project, "p/one", GenesisId.ToString("D")), request);
                Assert.True(Named(panel, "DifferenceHeadline").IsEffectivelyVisible);
                Assert.Equal("15 words to check, 4 words improved",
                    panel.FindControl<CopyableTextBlock>("DifferenceHeadline")!.Text);
                Assert.Equal("Make Difference Again", Label(make));
                Assert.False(Named(panel, "DifferenceMaking").IsEffectivelyVisible);
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void ASourceValidatedDifferenceKeepsItsNotesBesideTheMakeControlsAndOffersAHumanOneBesideIt()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            var evidence = new ProposalDifferenceWordEvidence[]
            {
                new("analysis/1", "analysis", "source-approved", "Source validation", "approves"),
            };
            var fixture = new ProposalDifferenceFixture
            {
                TextId = GenesisId.ToString("D"), Reference = ProposalDifferenceReferences.SourceValidated,
                FirstWordEvidence = evidence,
                Warnings = [ProposalDifferenceWarnings.EvidenceUnreadable + ": 'Stage sita' (proposal/9)."],
                Unreadable = [new("proposal/9", "Stage sita", "The stored revision could not be read.")],
            };
            fake.OnProposalDifference(fixture.Read);
            var started = new TaskCompletionSource();
            var release = new TaskCompletionSource();
            fake.OnProduceProposalDifference(async (request, _, token) =>
            {
                started.SetResult();
                await release.Task;
                return await fixture.Read(new ProposalDifferenceShowRequest(request.ProjectPath, request.ProposalId), token);
            });
            var (panel, window) = await OpenAsync(fake, Card());
            try
            {
                var model = Assert.IsType<ProposalDifferenceViewModel>(panel.DataContext);
                model.Words[0].ChooseCommand.Execute(null);
                await Wait.UntilAsync(() => model.HasSelectedWordEvidence, "The chosen word showed no evidence.");
                PageScreenshots.Settle(window);

                void AssertNotesShow(bool wordChosen = true)
                {
                    Assert.Equal(MotifAppStrings.ProposalDifferenceNoteSourceValidated,
                        panel.FindControl<CopyableTextBlock>("DifferenceReferenceNote")!.Text);
                    Assert.True(Named(panel, "DifferenceReferenceNote").IsEffectivelyVisible);
                    Assert.True(Named(panel, "DifferenceEvidenceUnreadable").IsEffectivelyVisible);
                    Assert.Equal(MotifAppStrings.ProposalDifferenceAdviceSourceValidated, model.AdviceText);
                    Assert.Equal(wordChosen, Named(panel, "DifferenceWordEvidence").IsEffectivelyVisible);
                    if (!wordChosen) return;
                    Assert.Contains("Source validation", panel.FindControl<CopyableTextBlock>("DifferenceWordEvidence")!.Text);
                }

                AssertNotesShow();
                Assert.True(Named(panel, "DifferenceMakeControls").IsEffectivelyVisible);
                // Only a saved Difference against people's decisions is made again; this Text has none.
                Assert.Equal("Make Difference", Label(Assert.IsType<Button>(Named(panel, "DifferenceMake"))));

                Click(panel, "DifferenceMake", window);
                await started.Task;
                PageScreenshots.Settle(window);

                Assert.True(Named(panel, "DifferenceMaking").IsEffectivelyVisible);
                Assert.False(Named(panel, "DifferenceReferenceNote").IsEffectivelyVisible);
                Assert.False(model.HasAdviceText);
                Assert.True(Named(panel, "DifferenceEvidenceUnreadable").IsEffectivelyVisible);
                Assert.True(Named(panel, "DifferenceWordEvidence").IsEffectivelyVisible);
                release.SetResult();
                await Wait.UntilAsync(() => !model.Maker.IsActive, "The Difference run never ended.");
                PageScreenshots.Settle(window);

                Assert.False(Named(panel, "DifferenceMaking").IsEffectivelyVisible);
                AssertNotesShow(wordChosen: false);

                model.Words[0].ChooseCommand.Execute(null);
                await Wait.UntilAsync(() => model.HasSelectedWordEvidence, "The chosen word showed no evidence.");
                PageScreenshots.Settle(window);
                AssertNotesShow();
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void ALongRunShowsProgressInWordsAndCancellingItSavesNothing()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.OnProposalDifference((_, _) => Task.FromResult(CommandOutcome<ProposalDifferenceResponse>.Refused(
                new Refusal(RefusalCodes.DifferenceNone, FailureReason.NotFound, "None."))));
            var reported = new TaskCompletionSource();
            var token = default(CancellationToken);
            fake.OnProduceProposalDifference(async (_, progress, cancellationToken) =>
            {
                token = cancellationToken;
                progress!.Report(new AssessmentProgress(AssessmentStage.Finalizing, 5, 20, "Comparing words."));
                reported.SetResult();
                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                }
                return CommandOutcome<ProposalDifferenceResponse>.Refused(new Refusal(
                    RefusalCodes.DifferenceCancelled, FailureReason.Cancelled, "Cancelled."));
            });
            var (panel, window) = await OpenAsync(fake, Card());
            try
            {
                var make = Assert.IsType<Button>(Named(panel, "DifferenceMake"));
                make.Command!.Execute(null);
                await reported.Task;
                PageScreenshots.Settle(window);

                Assert.True(Named(panel, "DifferenceMaking").IsEffectivelyVisible);
                Assert.False(make.Command.CanExecute(null));
                Assert.Equal("Compared 5 of 20 words.", panel.FindControl<CopyableTextBlock>("DifferenceProgressText")!.Text);
                Assert.Equal(0.25, Assert.IsType<ProgressBar>(Named(panel, "DifferenceProgress")).Value, 3);

                Click(panel, "DifferenceCancel", window);
                await Task.Delay(50);
                PageScreenshots.Settle(window);

                Assert.True(token.IsCancellationRequested);
                Assert.False(Named(panel, "DifferenceMaking").IsEffectivelyVisible);
                Assert.True(Named(panel, "DifferenceCancelled").IsEffectivelyVisible);
                Assert.False(Named(panel, "DifferenceHeadline").IsEffectivelyVisible);
                Assert.Equal("Make Difference", Label(make));
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void ARefusedRunSaysWhyInTheWindowsWords()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.OnProposalDifference((_, _) => Task.FromResult(CommandOutcome<ProposalDifferenceResponse>.Refused(
                new Refusal(RefusalCodes.DifferenceNone, FailureReason.NotFound, "None."))));
            fake.OnProduceProposalDifference((_, _, _) => Task.FromResult(
                CommandOutcome<ProposalDifferenceResponse>.Refused(new Refusal(
                    RefusalCodes.DifferenceTrialIncomplete, FailureReason.Refused, "Run: motif proposal trial"))));
            var (panel, window) = await OpenAsync(fake, Card());
            try
            {
                Click(panel, "DifferenceMake", window);

                var refusal = Named(panel, "DifferenceMakeRefusal");
                Assert.True(refusal.IsEffectivelyVisible);
                Assert.Equal(MotifAppStrings.RefusalDifferenceTrialIncomplete,
                    Assert.IsType<WindowRefusal>(refusal.DataContext).Sentence);
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void ANeverTriedOrDecidedProposalOrAProjectWithNoTextsShowsANoteInsteadOfTheAction()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            foreach (var (card, texts, note) in new (ProposalCard, bool, string)[]
            {
                (Card(standing: TrialStanding.NotTried), true, "DifferenceNotTried"),
                (Card(ProposalGroup.SetAside), true, "DifferenceDecidedNote"),
                (Card(), false, "DifferenceNoTexts"),
            })
            {
                var fake = new FakeCommandClient();
                fake.OnProposalDifference((_, _) => Task.FromResult(CommandOutcome<ProposalDifferenceResponse>.Refused(
                    new Refusal(RefusalCodes.DifferenceNone, FailureReason.NotFound, "None."))));
                var (panel, window) = await OpenAsync(fake, card, texts);
                try
                {
                    Assert.True(Named(panel, note).IsEffectivelyVisible, note);
                    Assert.False(Named(panel, "DifferenceMakeControls").IsEffectivelyVisible, note);
                    Assert.Empty(fake.ProduceProposalDifferenceRequests);
                }
                finally
                {
                    window.Close();
                }
            }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void TheButtonShowsItsLabelInATextBlockAndTheTextListTrimsLongTitles()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.OnProposalDifference((_, _) => Task.FromResult(CommandOutcome<ProposalDifferenceResponse>.Refused(
                new Refusal(RefusalCodes.DifferenceNone, FailureReason.NotFound, "None."))));
            var (panel, window) = await OpenAsync(fake, Card());
            try
            {
                var make = Assert.IsType<Button>(Named(panel, "DifferenceMake"));
                Assert.Equal("Make Difference", Assert.IsType<TextBlock>(make.Content).Text);
                var text = Assert.IsType<ComboBox>(Named(panel, "DifferenceText"));
                text.IsDropDownOpen = true;
                PageScreenshots.Settle(window);
                var titles = text.GetVisualDescendants().OfType<TextBlock>()
                    .Where(block => block.Text == "Genesis 1").ToList();
                Assert.NotEmpty(titles);
                Assert.All(titles, block => Assert.Equal(TextTrimming.CharacterEllipsis, block.TextTrimming));
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void FocusMovesToCancelWhileARunGoesAndBackToMakeWhenItEnds()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.OnProposalDifference((_, _) => Task.FromResult(CommandOutcome<ProposalDifferenceResponse>.Refused(
                new Refusal(RefusalCodes.DifferenceNone, FailureReason.NotFound, "None."))));
            var release = new TaskCompletionSource();
            fake.OnProduceProposalDifference(async (_, _, token) =>
            {
                await release.Task;
                return CommandOutcome<ProposalDifferenceResponse>.Refused(new Refusal(
                    RefusalCodes.DifferenceNoTrial, FailureReason.NotFound, "No Trial."));
            });
            var (panel, window) = await OpenAsync(fake, Card());
            try
            {
                var make = Assert.IsType<Button>(Named(panel, "DifferenceMake"));
                var cancel = Assert.IsType<Button>(Named(panel, "DifferenceCancel"));
                make.Focus();
                PageScreenshots.Settle(window);
                Assert.True(make.IsFocused);

                make.Command!.Execute(null);
                PageScreenshots.Settle(window);
                Assert.True(cancel.IsFocused, "focus should follow the run to Cancel");

                release.SetResult();
                await Task.Delay(50);
                PageScreenshots.Settle(window);
                Assert.True(make.IsFocused, "focus should come back to Make when the run ends");
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }
}

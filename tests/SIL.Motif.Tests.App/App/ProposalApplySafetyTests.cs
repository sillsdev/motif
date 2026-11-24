using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

// fix1-proposal: what a person sees on a Proposal document when its evidence is unavailable, and before Apply.
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ProposalApplySafetyTests
{
    private sealed class RecordingLauncher : IUriLauncher
    {
        public List<Uri> Launched { get; } = [];
        public Task<bool> LaunchAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            Launched.Add(uri);
            return Task.FromResult(true);
        }
    }

    private static TrialRecord CurrentTrial() => new("job/one", "one", "intent/one",
        WorkspaceContextTests.Assessment().Baseline.Token, null, new Dictionary<string, string>(), [], false, null,
        JobStatus.Completed, DateTimeOffset.UtcNow, TrialFreshness.Current);

    private static string[] VisibleText(Control root) => [.. root.GetVisualDescendants().OfType<TextBlock>()
        .Where(block => block.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(block.Text)).Select(block => block.Text!)];

    private static Button PrepareButton(Control root) => Assert.Single(root.GetVisualDescendants().OfType<Button>(),
        button => AutomationProperties.GetAutomationId(button) == AutomationIds.ProposalPrepareApply);

    private static async Task<(FakeCommandClient Fake, WorkspaceContext Context, OpenProposalDocuments Documents,
        ProposalDocumentViewModel Document)> OpenAsync(bool withTrial, RecordingLauncher? launcher = null,
        ProposalCard? card = null, bool withTexts = false, bool withDifference = false)
    {
        var fake = new FakeCommandClient();
        if (withTexts)
            fake.ListTextsCompletesWith(new TextInventoryResponse([new TextChoiceSummary(Guid.NewGuid(), "Genesis 1", 12)],
                HasBaseline: true));
        var trial = CurrentTrial();
        if (withTrial)
        {
            fake.ReadTrialCompletesWith(trial);
            fake.Review = new ProposalReviewResponse("one", "intent/one", "revision/one",
                [new GrammarChangeEntry("entry", 0, "A change", ["op/one"], [], "content", false,
                    "composer", "A change", [], [], [])], trial, new(true, false, false, null, [], false), [], 0);
            if (withDifference) ProposalStatusFakes.GiveItASavedDifference(fake);
        }
        fake.PendingChangesIs(new PendingChangesSnapshot("one", "revision/one", [], []));
        fake.ShowProposalHandler = (request, _) => Task.FromResult(CommandOutcome<ProposalDetailProjection>.Success(
            new(request.ProposalId, "proposed", "Clarify the gloss", null, "intent/one", [])));
        var context = WorkspaceContextTests.NewContext(fake, uriLauncher: launcher);
        await context.OpenProjectAsync("project.fwdata");
        var documents = new OpenProposalDocuments(context);
        var document = documents.Open("one");
        document.Card = new ProposalCardViewModel(card ?? new("one", "Clarify the gloss", "proposed",
            ProposalGroup.NotAppliedYet, false, [], withTrial ? TrialStanding.Current : TrialStanding.NotTried, null, []));
        await document.RefreshAsync();
        return (fake, context, documents, document);
    }

    [Fact]
    public void AnUntriedProposalShowsTheNextActionAndKeepsApplyOff()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (_, context, documents, document) = await OpenAsync(withTrial: false);
            var page = new ProposalDocument { DataContext = document };
            var window = new MotifTestWindow { Width = 1000, Height = 900, Content = new ScrollViewer { Content = page } };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                var text = VisibleText(page);
                Assert.Single(text, line => line.Contains("Not tried yet", StringComparison.Ordinal));
                Assert.False(PrepareButton(page).IsEffectivelyEnabled);
                Assert.DoesNotContain(MotifAppStrings.ProposalEvidenceUnavailable, text);
                Assert.True(document.HasStatusAction);
                Assert.Equal(MotifAppStrings.TryIt, document.StatusActionText);
                Assert.False(document.CanPrepareApply);
                Assert.False(document.CanApply);
            }
            finally { window.Close(); await documents.StopAsync(); await context.StopProjectWorkAsync(); }
        }, TimeSpan.FromMinutes(2));
    }

    // fix1-round1c: a Proposal with a current Trial but no Difference made yet is not "unavailable".
    [Fact]
    public void AProposalWithACurrentTrialAndNoDifferenceYetSaysSoAndNamesTheStep()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (_, context, documents, document) = await OpenAsync(withTrial: true, withTexts: true);
            var page = new ProposalDocument { DataContext = document };
            var window = new MotifTestWindow { Width = 1000, Height = 900, Content = new ScrollViewer { Content = page } };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                var text = VisibleText(page);
                Assert.DoesNotContain(text, line => line.Contains("Evidence Is Unavailable", StringComparison.Ordinal));
                Assert.Single(text, line => line == MotifAppStrings.ProposalEvidenceNotMadeYet);
            }
            finally { window.Close(); await documents.StopAsync(); await context.StopProjectWorkAsync(); }
        }, TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void AProposalWithACurrentReadyTrialLetsApplyStartAndTheConfirmationNamesTheBackup()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var launcher = new RecordingLauncher();
            var (_, context, documents, document) = await OpenAsync(withTrial: true, launcher, withDifference: true);
            var page = new ProposalDocument { DataContext = document };
            var window = new MotifTestWindow { Width = 1000, Height = 900, Content = new ScrollViewer { Content = page } };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                Assert.True(PrepareButton(page).IsEffectivelyEnabled);
                Assert.DoesNotContain(VisibleText(page), line => line.Contains(MotifAppStrings.ApplyBackupNotice, StringComparison.Ordinal));
                await document.PrepareApplyCommand.ExecuteAsync(null);
                PageScreenshots.Settle(window);
                Assert.True(document.IsConfirmingApply);
                Assert.Contains(VisibleText(page), line => line == MotifAppStrings.ApplyBackupNotice);
                var link = Assert.Single(page.GetVisualDescendants().OfType<HyperlinkButton>(),
                    button => button.Name == "ApplyBackupLink");
                Assert.True(link.IsEffectivelyVisible);
                Assert.Equal(MotifAppStrings.ApplyBackupLink, link.Content);
                await document.OpenBackupHelpCommand.ExecuteAsync(null);
                Assert.Equal(new Uri(AppLinks.FieldWorksBackupHelp), Assert.Single(launcher.Launched));
            }
            finally { window.Close(); await documents.StopAsync(); await context.StopProjectWorkAsync(); }
        }, TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void TheChecklistReflectsTheBoundDryRunAndAShortFailureCanBeRetried()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (fake, context, documents, document) = await OpenAsync(withTrial: true, withDifference: true);
            var ready = fake.Review!;
            fake.Review = ready with
            {
                Readiness = ready.Readiness! with
                {
                    IsReady = false,
                    MissingBoundDryRun = true,
                    Reasons = ["RAW back-end reason"],
                },
            };
            var page = new ProposalDocument { DataContext = document };
            var window = new MotifTestWindow { Width = 1050, Height = 920, Content = new ScrollViewer { Content = page } };
            var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                await document.RefreshAsync();
                window.Show();
                PageScreenshots.Settle(window);
                var run = Assert.Single(page.GetVisualDescendants().OfType<Button>(),
                    button => AutomationProperties.GetAutomationId(button) == AutomationIds.ProposalRunDryRun);
                Assert.True(document.TriedChecked);
                Assert.True(document.DifferenceChecked);
                Assert.False(document.DryRunChecked);
                Assert.True(run.IsEffectivelyEnabled);
                Assert.False(PrepareButton(page).IsEffectivelyEnabled);
                Assert.DoesNotContain(VisibleText(page), line => line.Contains("RAW", StringComparison.Ordinal));

                fake.WaitForDryRunHandler = async (request, progress, token) =>
                {
                    Assert.Equal(document.ProposalId, request.ProposalId);
                    Assert.Equal(document.ProjectPath, request.FwDataPath);
                    Assert.Equal("job/dry-run", request.JobId);
                    progress.Report(new(request.JobId, "project/test", true, "dry-run",
                        JobStatus.Running, 1, null, false, null, 0));
                    await finish.Task.WaitAsync(token);
                    return CommandOutcome<DryRunProjection>.Refused(
                        new Refusal("job.dry-run-incomplete", FailureReason.Refused, "The job stopped."));
                };
                var running = document.RunDryRunCommand.ExecuteAsync(null);
                PageScreenshots.Settle(window);
                Assert.Contains(MotifAppStrings.ProposalDryRunRunning, VisibleText(page));
                Assert.False(run.IsEffectivelyEnabled);
                Capture("dry-run-running");
                finish.SetResult();
                await running;
                PageScreenshots.Settle(window);
                Assert.Contains(MotifAppStrings.ProposalDryRunFailed, VisibleText(page));
                Assert.False(PrepareButton(page).IsEffectivelyEnabled);
                Assert.True(run.IsEffectivelyEnabled);
                Capture("dry-run-failed");

                fake.WaitForDryRunHandler = (request, _, _) =>
                {
                    fake.Review = ready;
                    return Task.FromResult(CommandOutcome<DryRunProjection>.Success(
                        new(request.ProposalId, "intent/one", "", [], "effect", "footprint") { EvidenceCurrent = true }));
                };
                await document.RunDryRunCommand.ExecuteAsync(null);
                PageScreenshots.Settle(window);
                Assert.True(document.DryRunChecked);
                Assert.False(run.IsEffectivelyVisible);
                Assert.True(PrepareButton(page).IsEffectivelyEnabled);

                fake.ShowProposalHandler = (request, _) => Task.FromResult(CommandOutcome<ProposalDetailProjection>.Success(
                    new(request.ProposalId, "deferred", "Clarify the gloss", null, "intent/one", [])));
                fake.Review = ready with { Readiness = null };
                await document.RefreshAsync();
                PageScreenshots.Settle(window);
                Assert.Equal(MotifAppStrings.ProposalDeferred, document.StatusText);
                Assert.False(PrepareButton(page).IsEffectivelyEnabled);
                Assert.DoesNotContain("Choose Apply... to check this Proposal's readiness.", VisibleText(page));
                Capture("deferred");
            }
            finally
            {
                finish.TrySetResult();
                window.Close();
                await documents.StopAsync();
                await context.StopProjectWorkAsync();
            }

            void Capture(string state)
            {
                if (Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable) is { Length: > 0 } folder)
                    PageScreenshots.Save(window, Path.Combine(folder, $"proposal-{state}.png"));
            }
        }, TimeSpan.FromMinutes(2));
    }

    [UiThreadFact]
    public async Task TheDifferenceChooserStartsWithTheLargestSelectedTextAndSkipsEmptyTexts()
    {
        var fake = new FakeCommandClient();
        var empty = Guid.NewGuid();
        var small = Guid.NewGuid();
        var largest = Guid.NewGuid();
        var unselected = Guid.NewGuid();
        fake.ListTextsCompletesWith(new TextInventoryResponse(
            [new(empty, "Empty"), new(small, "Small", 4), new(largest, "Large", 20),
                new(unselected, "Outside the Selection", 200)], HasBaseline: true));
        var context = WorkspaceContextTests.NewContext(fake);
        await context.OpenProjectAsync("project.fwdata");
        foreach (var text in context.Selection.Texts) text.IsChecked = text.Id != unselected;
        var documents = new OpenProposalDocuments(context);
        try
        {
            var document = documents.Open("one");
            await document.RefreshAsync();
            Assert.DoesNotContain(document.Difference.TextChoices, text => text.Id == empty);
            Assert.Equal(largest, document.Difference.SelectedText?.Id);
        }
        finally
        {
            await documents.StopAsync();
            await context.StopProjectWorkAsync();
        }
    }

    [Fact]
    public void UnloadedEvidenceDoesNotSayItIsUnavailable()
    {
        var difference = new ProposalDifferenceViewModel(
            (_, _) => throw new InvalidOperationException(), _ => { });
        var evidence = new ProposalEvidenceColumnsViewModel(difference);

        Assert.False(evidence.HasAvailability);
    }

    [Fact]
    public void LostApprovedWordsAreCountedBeforeApply()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var summary = new TrialSummary("job/one", DateTimeOffset.UtcNow, TrialFreshness.Current, TrialWordScope.AllWords,
                new Paired<double?>(60, 64.5, true, true, true, null), new Paired<int>(10, 9, true, true, true, null),
                new Paired<int>(12, 12, true, true, false, null), new Paired<double?>(2500, 2600, true, true, false, null),
                new Paired<int?>(3, 7, true, true, true, null), 2, 0) { LostApprovedKnown = true };
            var card = new ProposalCard("one", "Clarify the gloss", "proposed", ProposalGroup.NotAppliedYet, false, [],
                TrialStanding.Current, summary, []);
            var (_, context, documents, document) = await OpenAsync(withTrial: true, card: card);
            var page = new ProposalDocument { DataContext = document };
            var window = new MotifTestWindow { Width = 1000, Height = 900, Content = new ScrollViewer { Content = page } };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                Assert.Contains(VisibleText(page), line => line.StartsWith("Applying would lose the approved analysis of 2 words", StringComparison.Ordinal));
            }
            finally { window.Close(); await documents.StopAsync(); await context.StopProjectWorkAsync(); }
        }, TimeSpan.FromMinutes(2));
    }

    [Theory]
    [InlineData("draft-AaHkr4ld58bKaFnPiDnHiw", true, "Untitled Draft")]
    [InlineData("", true, "Untitled Draft")]
    [InlineData("abc", false, "Untitled Proposal")]
    [InlineData("Clarify the gloss", true, "Clarify the gloss")]
    public void AnUnnamedDraftIsNeverTitledByItsRawId(string label, bool draft, string expected)
    {
        var id = label.StartsWith("draft-", StringComparison.Ordinal) ? label : "abc";
        var card = new ProposalCardViewModel(new(id, label, "proposed", ProposalGroup.NotAppliedYet, draft, [],
            TrialStanding.NotTried, null, []) { IsDraft = draft });
        Assert.Equal(expected, card.Label);
        Assert.DoesNotContain(id, card.AccessibleRowName.Replace(expected, string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void ADraftDocumentIsTitledByItsFirstChangeWhenItHasNoName()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.ShowProposalHandler = (request, _) => Task.FromResult(CommandOutcome<ProposalDetailProjection>.Success(
                new(request.ProposalId, "proposed", request.ProposalId, null, "intent/one", [])));
            var context = WorkspaceContextTests.NewContext(fake);
            await context.OpenProjectAsync("project.fwdata");
            var documents = new OpenProposalDocuments(context);
            var document = documents.Open("AaHkr4ld58bKaFnPiDnHiw");
            document.Card = new ProposalCardViewModel(new("AaHkr4ld58bKaFnPiDnHiw", "AaHkr4ld58bKaFnPiDnHiw", "proposed",
                ProposalGroup.NotAppliedYet, false, [], TrialStanding.NotTried, null, []) { IsDraft = true });
            await document.RefreshAsync();
            Assert.Equal("Untitled Draft", document.Label);
            document.OperationDescriptions = ["Change the gloss of palit sense 1"];
            Assert.Equal("Change the gloss of palit sense 1", document.Label);
            await documents.StopAsync();
            await context.StopProjectWorkAsync();
        }, TimeSpan.FromMinutes(2));
    }
}

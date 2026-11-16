using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
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
        ProposalCard? card = null)
    {
        var fake = new FakeCommandClient();
        var trial = CurrentTrial();
        if (withTrial)
        {
            fake.ReadTrialCompletesWith(trial);
            fake.Review = new ProposalReviewResponse("one", "intent/one", "revision/one",
                [new GrammarChangeEntry("entry", 0, "A change", ["op/one"], [], "content", false,
                    "composer", "A change", [], [], [])], trial, new(true, false, false, null, [], false), [], 0);
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
    public void AProposalWithoutACurrentTrialSaysEvidenceIsUnavailableOnceAndKeepsApplyOffWithItsReason()
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
                Assert.Single(text, line => line.Contains("Evidence Is Unavailable", StringComparison.Ordinal));
                Assert.False(PrepareButton(page).IsEffectivelyEnabled);
                Assert.Contains(text, line => line.StartsWith("Apply is unavailable:", StringComparison.Ordinal) &&
                    line.Contains("Try It", StringComparison.Ordinal));
                Assert.False(document.CanPrepareApply);
                Assert.False(document.CanApply);
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
            var (_, context, documents, document) = await OpenAsync(withTrial: true, launcher);
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

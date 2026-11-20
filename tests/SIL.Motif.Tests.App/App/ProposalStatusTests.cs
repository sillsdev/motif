using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Requests;
using Xunit;

namespace SIL.Motif.Tests.App;

// round2-proposal: the Proposal page's status block and Apply rules.
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ProposalStatusTests
{
    private static TrialRecord CurrentTrial() => new("job/one", "one", "intent/one",
        WorkspaceContextTests.Assessment().Baseline.Token, null, new Dictionary<string, string>(), [], false, null,
        JobStatus.Completed, DateTimeOffset.UtcNow, TrialFreshness.Current);

    private static string[] VisibleText(Control root) => [.. root.GetVisualDescendants().OfType<TextBlock>()
        .Where(block => block.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(block.Text)).Select(block => block.Text!)];

    private static Button ApplyButton(Control root) => Assert.Single(root.GetVisualDescendants().OfType<Button>(),
        button => AutomationProperties.GetAutomationId(button) == AutomationIds.ProposalPrepareApply);

    private static Control[] StatusBlocks(Control root) => [.. root.GetVisualDescendants().OfType<Control>().Where(
        control => AutomationProperties.GetAutomationId(control) == AutomationIds.ProposalStatus)];

    public enum State { Draft, ReadyNoTrial, TrialNoDifference, TrialAndDifference }

    private static async Task<(WorkspaceContext Context, OpenProposalDocuments Documents, ProposalDocumentViewModel Document)>
        OpenAsync(State state, string? builder = null, string? model = null, string? question = null,
        Action<FakeCommandClient>? configure = null)
    {
        var fake = new FakeCommandClient();
        fake.ListTextsCompletesWith(new TextInventoryResponse([new TextChoiceSummary(Guid.NewGuid(), "Genesis 1", 12)],
            HasBaseline: true));
        var id = state == State.Draft ? "draft/one" : "one";
        var trial = CurrentTrial();
        if (state is State.TrialNoDifference or State.TrialAndDifference)
        {
            fake.ReadTrialCompletesWith(trial);
            fake.Review = new ProposalReviewResponse("one", "intent/one", "revision/one",
                [new GrammarChangeEntry("entry", 0, "A change", ["op/one"], [], "content", false,
                    "composer", "A change", [], [], [])], trial, new(true, false, false, null, [], false), [], 0);
        }
        if (state == State.TrialAndDifference)
        {
            ProposalStatusFakes.GiveItASavedDifference(fake);
        }
        var change = new PendingChange("one", "wordform/one", "one", "approve", "assessment/one", "reading", ["op"]);
        fake.PendingChangesIs(state == State.Draft
            ? new("draft/one", "revision/one", [change], [new ChangeFit("one", true, [])])
            : new PendingChangesSnapshot("one", "revision/one", [], []));
        configure?.Invoke(fake);
        fake.ShowProposalHandler = (request, _) => Task.FromResult(CommandOutcome<ProposalDetailProjection>.Success(
            new(request.ProposalId, "proposed", "Clarify the gloss", null, "intent/one", [])));
        var context = WorkspaceContextTests.NewContext(fake);
        await context.OpenProjectAsync("project.fwdata");
        var documents = new OpenProposalDocuments(context);
        var document = documents.Open(id);
        document.Card = new ProposalCardViewModel(new(id, "Clarify the gloss", "proposed",
            ProposalGroup.NotAppliedYet, state == State.Draft, [],
            state is State.TrialNoDifference or State.TrialAndDifference ? TrialStanding.Current : TrialStanding.NotTried,
            null, [])
        { IsDraft = state == State.Draft, Builder = builder, Model = model, Question = question });
        await document.RefreshAsync();
        return (context, documents, document);
    }

    private static async Task<T> RenderAsync<T>(ProposalDocumentViewModel document, Func<ProposalDocument, Window, T> look)
    {
        var page = new ProposalDocument { DataContext = document };
        var window = new MotifTestWindow { Width = 1000, Height = 1200, Content = new ScrollViewer { Content = page } };
        try
        {
            window.Show();
            PageScreenshots.Settle(window);
            return look(page, window);
        }
        finally { window.Close(); await Task.CompletedTask; }
    }

    [Theory]
    [InlineData(State.Draft, "has not been checked yet", false)]
    [InlineData(State.ReadyNoTrial, "Not tried yet", false)]
    [InlineData(State.TrialNoDifference, "No Difference has been made", false)]
    [InlineData(State.TrialAndDifference, "You can apply it", true)]
    public void EachStateShowsExactlyOneStatusBlockAndApplyIsEnabledOnlyWithTrialAndDifference(
        State state, string says, bool applyEnabled)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (context, documents, document) = await OpenAsync(state);
            try
            {
                await RenderAsync(document, (page, _) =>
                {
                    Assert.Single(StatusBlocks(page));
                    var text = VisibleText(page);
                    Assert.Single(text, line => line.Contains(says, StringComparison.Ordinal));
                    if (!applyEnabled)
                        Assert.DoesNotContain(text, line => line.Contains("Evidence is unavailable", StringComparison.OrdinalIgnoreCase));
                    Assert.DoesNotContain(text, line => line.Contains("Try this Proposal first", StringComparison.Ordinal));
                    Assert.DoesNotContain(text, line => line.Contains("Approved places are unavailable", StringComparison.OrdinalIgnoreCase));
                    Assert.Equal(applyEnabled, ApplyButton(page).IsEffectivelyEnabled);
                    Assert.Equal(applyEnabled, document.CanPrepareApply);
                    return 0;
                });
            }
            finally { await documents.StopAsync(); await context.StopProjectWorkAsync(); }
        }, TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void ACurrentTrialWithoutADifferenceKeepsApplyOffAndNamesTheButtonOnThePage()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (context, documents, document) = await OpenAsync(State.TrialNoDifference);
            try
            {
                await RenderAsync(document, (page, _) =>
                {
                    Assert.False(ApplyButton(page).IsEffectivelyEnabled);
                    Assert.Equal(MotifAppStrings.ProposalApplyNeedsDifference, document.ApplyBlockReason);
                    Assert.Contains(MotifAppStrings.ProposalDifferenceMake, document.ApplyBlockReason, StringComparison.Ordinal);
                    var make = Assert.Single(page.GetVisualDescendants().OfType<Button>(), button =>
                        AutomationProperties.GetAutomationId(button) == AutomationIds.ProposalDifferenceMake);
                    Assert.True(make.IsEffectivelyVisible);
                    Assert.Contains(StatusBlocks(page).Single().GetVisualDescendants(), control => ReferenceEquals(control, make));
                    return 0;
                });
            }
            finally { await documents.StopAsync(); await context.StopProjectWorkAsync(); }
        }, TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void ADraftWithAChangeThatNoLongerFitsOffersToRemoveItAndThenHasNoSuchBlocker()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var gone = new PendingChange("gone", "wordform/gone", "gone", "approve", "assessment/one", "reading", ["op"])
            { StagingIntent = new ChangeIntent("gone", "approve", "wordform/gone", "gone", "assessment/one", null, DisplayReading: "reading") };
            var gone2 = new PendingChange("gone2", "wordform/gone2", "gone2", "approve", "assessment/one", "reading", ["op"])
            { StagingIntent = new ChangeIntent("gone2", "approve", "wordform/gone2", "gone2", "assessment/one", null, DisplayReading: "reading") };
            var kept = new PendingChange("kept", "wordform/kept", "kept", "approve", "assessment/one", "reading", ["op"])
            { StagingIntent = new ChangeIntent("kept", "approve", "wordform/kept", "kept", "assessment/one", null, DisplayReading: "reading") };
            var (context, documents, document) = await OpenAsync(State.Draft);
            try
            {
                var fake = (FakeCommandClient)context.Commands;
                fake.PendingChangesIs(new("draft/one", "revision/two", [gone, gone2, kept],
                    [new ChangeFit("gone", ChangeFitStatus.NoLongerFits, ["The text changed."]),
                        new ChangeFit("gone2", ChangeFitStatus.NoLongerFits, ["The text changed."]), new ChangeFit("kept", true, [])]));
                await context.Changes.ReloadAsync();
                Assert.True(document.OffersRemoveNonFitting);
                Assert.Contains(context.ProposalApply.Blockers(null, document.RemoveNonFittingCommand, null, null),
                    blocker => blocker.Kind == ApplyBlockerKind.NoLongerFits && blocker.Action is not null);
                await RenderAsync(document, (page, _) =>
                {
                    var remove = Assert.Single(page.GetVisualDescendants().OfType<Button>(), button =>
                        AutomationProperties.GetAutomationId(button) == AutomationIds.ProposalRemoveNonFitting);
                    Assert.True(remove.IsEffectivelyVisible);
                    return 0;
                });
                await ((IAsyncRelayCommand)document.RemoveNonFittingCommand).ExecuteAsync(null);
                Assert.False(document.OffersRemoveNonFitting);
                Assert.Equal(["kept"], context.Changes.Items.Select(item => item.ChangeId));
                // Both removals are one staging action: a single Undo puts them back.
                await context.Changes.UndoStagingActionAsync();
                Assert.Equal(["gone", "gone2", "kept"], context.Changes.Items.Select(item => item.ChangeId).Order());
                Assert.DoesNotContain(context.ProposalApply.Blockers(null, null, null, null),
                    blocker => blocker.Kind == ApplyBlockerKind.NoLongerFits);
            }
            finally { await documents.StopAsync(); await context.StopProjectWorkAsync(); }
        }, TimeSpan.FromMinutes(2));
    }

    // round2-landrev: Apply follows the Difference as it is made and as it is forgotten.
    [Fact]
    public void MakingADifferenceEnablesApplyOnThePageAndForgettingItDisablesApplyAgain()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (context, documents, document) = await OpenAsync(State.TrialNoDifference, configure: fake =>
                fake.OnProduceProposalDifference(async (_, _, token) =>
                {
                    var response = (await new ProposalDifferenceFixture(3).Read(
                        new ProposalDifferenceShowRequest("project.fwdata", "one"), token)).Value!;
                    return CommandOutcome<ProposalDifferenceResponse>.Success(response with
                    {
                        Summary = response.Summary with { DifferenceId = "one/newest", TrialJobId = "job/one" },
                    });
                }));
            try
            {
                var page = new ProposalDocument { DataContext = document };
                var window = new MotifTestWindow { Width = 1000, Height = 1200, Content = new ScrollViewer { Content = page } };
                try
                {
                    window.Show();
                    PageScreenshots.Settle(window);
                    Assert.False(ApplyButton(page).IsEffectivelyEnabled);
                    Assert.Contains(MotifAppStrings.ProposalEvidenceNotMadeYet, VisibleText(page));

                    await document.Difference.Maker.RunCommand.ExecuteAsync(null);
                    PageScreenshots.Settle(window);
                    Assert.True(document.Difference.IsShown);
                    Assert.True(ApplyButton(page).IsEffectivelyEnabled);
                    Assert.DoesNotContain(MotifAppStrings.ProposalEvidenceNotMadeYet, VisibleText(page));

                    document.Difference.Clear();
                    PageScreenshots.Settle(window);
                    Assert.False(ApplyButton(page).IsEffectivelyEnabled);
                    Assert.False(document.CanPrepareApply);
                }
                finally { window.Close(); }
            }
            finally { await documents.StopAsync(); await context.StopProjectWorkAsync(); }
        }, TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void AnUntriedProposalOffersTryItInTheStatusBlockAndItsApplyReasonNamesThatButton()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (context, documents, document) = await OpenAsync(State.ReadyNoTrial);
            try
            {
                await RenderAsync(document, (page, _) =>
                {
                    var tryIt = Assert.Single(StatusBlocks(page).Single().GetVisualDescendants().OfType<Button>(),
                        button => button.IsEffectivelyVisible && button.IsEffectivelyEnabled);
                    Assert.Equal(MotifAppStrings.TryIt, tryIt.Content);
                    Assert.Same(document.TryCommand, tryIt.Command);
                    Assert.DoesNotContain("Proposals list", document.ApplyBlockReason, StringComparison.OrdinalIgnoreCase);
                    Assert.Contains("this page", document.ApplyBlockReason, StringComparison.Ordinal);
                    Assert.Contains(MotifAppStrings.TryIt, document.ApplyBlockReason, StringComparison.Ordinal);
                    return 0;
                });
            }
            finally { await documents.StopAsync(); await context.StopProjectWorkAsync(); }
        }, TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void LinesWhoseValueIsUnknownAreNotShownAndRecordedOnesAre()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (context, documents, document) = await OpenAsync(State.ReadyNoTrial);
            var (context2, documents2, recorded) = await OpenAsync(State.ReadyNoTrial, "Claude", "model-x", "Why?");
            try
            {
                await RenderAsync(document, (page, _) =>
                {
                    var text = VisibleText(page);
                    Assert.DoesNotContain(text, line => line.Contains("Unknown", StringComparison.Ordinal));
                    Assert.DoesNotContain(text, line => line.Contains("not recorded", StringComparison.Ordinal));
                    return 0;
                });
                await RenderAsync(recorded, (page, _) =>
                {
                    var text = VisibleText(page);
                    Assert.Contains("Built by: Claude", text);
                    Assert.Contains("Model: model-x", text);
                    Assert.Contains("Question: Why?", text);
                    return 0;
                });
            }
            finally
            {
                await documents.StopAsync(); await context.StopProjectWorkAsync();
                await documents2.StopAsync(); await context2.StopProjectWorkAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void OneOpenProposalNeedsNoSecondButtonBesideCloseProposal()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (context, documents, _) = await OpenAsync(State.ReadyNoTrial);
            try
            {
                Assert.False(documents.HasSeveral);
                documents.Open("two");
                Assert.True(documents.HasSeveral);
            }
            finally { await documents.StopAsync(); await context.StopProjectWorkAsync(); }
        }, TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void ARationaleReadsAtBodySizeAndTheApplyConfirmationMarksApplyAsTheWrite()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (context, documents, document) = await OpenAsync(State.TrialAndDifference);
            try
            {
                var size = document.RationaleEditor!.Styles.Resolve("Normal", "en")!.IntProps[TextPropertyNumbersFontSize];
                Assert.Equal(15000, size.Value);
                await document.PrepareApplyCommand.ExecuteAsync(null);
                await RenderAsync(document, (page, _) =>
                {
                    var apply = Assert.Single(page.GetVisualDescendants().OfType<Button>(), button =>
                        AutomationProperties.GetAutomationId(button) == AutomationIds.ProposalConfirmApply);
                    Assert.Contains("primary", apply.Classes);
                    Assert.Contains("writes-fieldworks", apply.Classes);
                    var cancel = Assert.Single(page.GetVisualDescendants().OfType<Button>(), button =>
                        Equals(button.Content, MotifAppStrings.Cancel) && ReferenceEquals(button.Command, document.CancelApplyCommand));
                    Assert.Contains("secondary", cancel.Classes);
                    var text = VisibleText(page);
                    Assert.True(Array.IndexOf([.. text], MotifAppStrings.ApplyBackupNotice) >= 0);
                    return 0;
                });
            }
            finally { await documents.StopAsync(); await context.StopProjectWorkAsync(); }
        }, TimeSpan.FromMinutes(2));
    }

    private const int TextPropertyNumbersFontSize = SIL.TextEditing.TextPropertyNumbers.FontSize;
}

internal static class ProposalStatusFakes
{
    // round2-proposal: a fake whose Difference reads as shown, so Apply can be offered.
    public static void GiveItASavedDifference(FakeCommandClient fake)
    {
        var fixture = new ProposalDifferenceFixture(3);
        fake.OnProposalDifference(async (request, token) =>
        {
            var response = (await fixture.Read(request, token)).Value!;
            var saved = new[] { new ProposalDifferenceSaved("one/newest", "text/one", "Text",
                ProposalDifferenceReferences.Human, "2026-10-31T00:00:00Z", true) };
            return CommandOutcome<ProposalDifferenceResponse>.Success(response with
            {
                Summary = response.Summary with { DifferenceId = "one/newest", TrialJobId = "job/one" },
                Saved = saved, SavedTotal = 1,
            });
        });
    }
}

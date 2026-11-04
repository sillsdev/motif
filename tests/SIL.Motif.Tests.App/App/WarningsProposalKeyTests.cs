using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins what the Warnings page holds of the open Proposal: it is keyed by Proposal and Trial, shows a reading state
/// and never an older Proposal's rows while a read runs, reads only while the page is shown, and a flip of the bar's
/// view keeps the selected finding, the sort and the filters.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class WarningsProposalKeyTests
{
    internal const string ProjectPath = "/tmp/warnings-proposal-key.fwdata";
    private const string SubjectGuid = "66666666-6666-6666-6666-666666666666";

    private static GrammarWarning Finding(string code, GrammarDiagnosticLevel level, params string[] words)
    {
        var reach = new WarningReach(WarningWordsPath.Uses);
        var subject = new GrammarWarningPart(code + " form", GrammarWarningPartRole.Object, SubjectGuid,
            "MoForm", "silfw://localhost/link?tool=LexEntryEdit") { SubjectGuid = SubjectGuid, Reach = reach };
        return new GrammarWarning(level, code, [subject], [], code)
        {
            Code = code,
            Group = code,
            YourWords = new WarningWords(WarningWordsMatch.Identity,
                [.. words.Select(word => new ObjectUseWord(new WordRow(word, WordRowOutcome.NoParse, "Lost", WordRowTone.Problem)))],
                []) { Paths = [reach.Path] },
        };
    }

    private static GrammarCheckResponse Today() => new(
    [
        Finding("a", GrammarDiagnosticLevel.Warning, "w1", "w2"), Finding("b", GrammarDiagnosticLevel.Error, "w3"),
        Finding("c", GrammarDiagnosticLevel.Information),
    ], true);

    private static GrammarCheckResponse ProposalOne() => new(
    [
        Finding("b", GrammarDiagnosticLevel.Error, "w3"), Finding("d", GrammarDiagnosticLevel.Warning, "w4", "w5", "w6"),
        Finding("e", GrammarDiagnosticLevel.Warning),
    ], true);

    private static GrammarCheckResponse ProposalTwo() => new(
    [
        Finding("e", GrammarDiagnosticLevel.Warning), Finding("f", GrammarDiagnosticLevel.Error, "w7"),
    ], true);

    private static TrialRecord TrialOf(string proposalId) =>
        OverviewProposalViewTests.Trial() with { ProposalId = proposalId, JobId = "job/" + proposalId };

    internal static (FakeCommandClient Fake, WorkspaceContext Context, WarningsPageModel Page) Build(
        Func<GrammarCheckRequest, Task<CommandOutcome<StoredGrammarCheckResponse>>> read)
    {
        var fake = new FakeCommandClient();
        fake.StoredGrammarCheckHandler = (request, _) => read(request);
        fake.OnReadTrial((request, _) => Task.FromResult(CommandOutcome<TrialRecord?>.Success(TrialOf(request.ProposalId))));
        var context = WorkspaceContextTests.NewContext(fake);
        return (fake, context, new WarningsPageModel(context));
    }

    internal static Task<CommandOutcome<StoredGrammarCheckResponse>> Answer(GrammarCheckResponse check) =>
        Task.FromResult(CommandOutcome<StoredGrammarCheckResponse>.Success(new StoredGrammarCheckResponse(check)));

    internal static GrammarCheckResponse Of(string? proposalId) => proposalId switch
    {
        null => Today(),
        "proposal/two" => ProposalTwo(),
        _ => ProposalOne(),
    };

    private static async Task<(FakeCommandClient Fake, WorkspaceContext Context, WarningsPageModel Page)> OpenedAsync()
    {
        var (fake, context, page) = Build(request => Answer(Of(request.ProposalId)));
        await context.OpenProjectAsync(ProjectPath);
        context.OpenPage(WorkspacePage.Warnings);
        await context.OpenProposalAsync("proposal/one", CancellationToken.None);
        return (fake, context, page);
    }

    internal static string[] Codes(WarningsPageModel page) => [.. page.Grammar.Warnings.Rows
        .Cast<GrammarWarningRowViewModel>().Select(row => row.GroupCode!).Order(StringComparer.Ordinal)];

    private static string TouchedText(int words) => string.Format(MotifAppStrings.WarningsTouchYourWordsFormat,
        PluralText.Format(MotifAppStrings.WordCountPattern, words));

    [Fact]
    public async Task TouchYourWordsCountsTheWordsOfTheRowsShownInThatView()
    {
        var (_, context, page) = await OpenedAsync();
        var warnings = page.Grammar.Warnings;

        context.ShowProposalView(MarkupView.Today);
        Assert.Equal(TouchedText(3), warnings.TouchYourWordsText);
        context.ShowProposalView(MarkupView.Proposal);
        Assert.Equal(TouchedText(4), warnings.TouchYourWordsText);
        context.ShowProposalView(MarkupView.TodayToProposal);
        Assert.Equal(TouchedText(6), warnings.TouchYourWordsText);
    }

    [Fact]
    public async Task OpeningAnotherProposalNeverShowsTheOldProposalsRowsWhileTheNewOneReads()
    {
        var gate = new TaskCompletionSource<CommandOutcome<StoredGrammarCheckResponse>>();
        var (_, context, page) = Build(request => request.ProposalId == "proposal/two" ? gate.Task : Answer(Of(request.ProposalId)));
        await context.OpenProjectAsync(ProjectPath);
        context.OpenPage(WorkspacePage.Warnings);
        await context.OpenProposalAsync("proposal/one", CancellationToken.None);
        context.ShowProposalView(MarkupView.Proposal);
        Assert.Equal(["b", "d", "e"], Codes(page));

        var opening = context.OpenProposalAsync("proposal/two", CancellationToken.None);
        context.ShowProposalView(MarkupView.Proposal);

        Assert.True(page.ShowProposalNone);
        Assert.Empty(Codes(page));
        Assert.Equal(MotifAppStrings.OverviewProposalReading, page.ProposalNoneText);
        context.ShowProposalView(MarkupView.TodayToProposal);
        Assert.Equal(["a", "b", "c"], Codes(page));
        Assert.False(page.Grammar.Warnings.Pairs);

        gate.SetResult(CommandOutcome<StoredGrammarCheckResponse>.Success(new StoredGrammarCheckResponse(ProposalTwo())));
        await opening;
        context.ShowProposalView(MarkupView.Proposal);
        Assert.Equal(["e", "f"], Codes(page));
    }

    // The UI thread pumps every continuation; the pump stands in for it.
    [Fact]
    public Task AReadForTheOldProposalThatLandsAfterTheNewOneOpenedIsDropped() =>
        SingleThreadPump.RunAsync(async () =>
        {
            var (page, touchedBy) = await OldReadLandsAfterTheNewOneOpenedAsync();

            Assert.Equal(["e", "f"], Codes(page));
            Assert.Single(touchedBy);
        });

    // A read answers from a worker in the App; a thread of its own is never the one the test is already on.
    private static void OnAnotherThread(Action answer)
    {
        var worker = new Thread(() => answer());
        worker.Start();
        worker.Join();
    }

    // Opens Proposal one then two, lands one's read last, and returns the threads that edited the rows.
    private static async Task<(WarningsPageModel Page, HashSet<int> TouchedBy)> OldReadLandsAfterTheNewOneOpenedAsync()
    {
        var oneGate = new TaskCompletionSource<CommandOutcome<StoredGrammarCheckResponse>>();
        var todayGate = new TaskCompletionSource<CommandOutcome<StoredGrammarCheckResponse>>();
        var holdToday = false;
        var (_, context, page) = Build(request => request.ProposalId switch
        {
            "proposal/one" => oneGate.Task,
            null when holdToday => todayGate.Task,
            _ => Answer(Of(request.ProposalId)),
        });
        var touchedBy = new HashSet<int>();
        page.Grammar.Warnings.TableEntries.CollectionChanged += (_, _) =>
        {
            lock (touchedBy) touchedBy.Add(Environment.CurrentManagedThreadId);
        };
        await context.OpenProjectAsync(ProjectPath);
        context.OpenPage(WorkspacePage.Warnings);
        var openingOne = context.OpenProposalAsync("proposal/one", CancellationToken.None);
        await Task.Yield();
        holdToday = true;
        var openingTwo = context.OpenProposalAsync("proposal/two", CancellationToken.None);
        await Task.Yield();

        OnAnotherThread(() => oneGate.SetResult(
            CommandOutcome<StoredGrammarCheckResponse>.Success(new StoredGrammarCheckResponse(ProposalOne()))));
        await Task.Yield();
        context.ShowProposalView(MarkupView.Proposal);
        Assert.DoesNotContain("d", Codes(page));

        OnAnotherThread(() => todayGate.SetResult(
            CommandOutcome<StoredGrammarCheckResponse>.Success(new StoredGrammarCheckResponse(Today()))));
        await openingOne;
        await openingTwo;
        return (page, touchedBy);
    }

    [Fact]
    public async Task TheProposalsCheckIsReadOnlyWhileTheWarningsPageIsShownAndOncePerKey()
    {
        var (fake, context, page) = Build(request => Answer(Of(request.ProposalId)));
        await context.OpenProjectAsync(ProjectPath);
        await context.OpenProposalAsync("proposal/one", CancellationToken.None);
        Assert.DoesNotContain(fake.StoredGrammarCheckRequests, request => request.ProposalId is not null);

        context.OpenPage(WorkspacePage.Warnings);
        await Task.Delay(50);
        context.ShowProposalView(MarkupView.Proposal);
        Assert.Equal(["b", "d", "e"], Codes(page));
        Assert.Equal(1, fake.StoredGrammarCheckRequests.Count(request => request.ProposalId == "proposal/one"));

        await context.OpenProposalAsync("proposal/one", CancellationToken.None);
        context.ShowProposalView(MarkupView.TodayToProposal);
        context.ShowProposalView(MarkupView.Proposal);
        Assert.Equal(1, fake.StoredGrammarCheckRequests.Count(request => request.ProposalId == "proposal/one"));
    }

    [Fact]
    public async Task AFlipKeepsTheSelectedFindingTheSortTheFiltersAndRebuildsNoRow()
    {
        var (_, context, page) = await OpenedAsync();
        var warnings = page.Grammar.Warnings;
        context.ShowProposalView(MarkupView.Proposal);
        warnings.MostYourWordsFirst = false;
        warnings.TouchYourWords = true;
        warnings.Bucket = GrammarFindingBucket.Errors;
        var selected = Assert.IsType<GrammarWarningRowViewModel>(Assert.Single(warnings.Rows.Cast<object>()));
        warnings.SelectedTableEntry = selected;
        var changes = new List<NotifyCollectionChangedEventArgs>();
        warnings.TableEntries.CollectionChanged += (_, e) => changes.Add(e);
        var changed = new List<string?>();
        warnings.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        context.ShowProposalView(MarkupView.TodayToProposal);

        Assert.Same(selected, warnings.SelectedTableEntry);
        Assert.DoesNotContain(changes, e => e.Action == NotifyCollectionChangedAction.Reset);
        Assert.DoesNotContain(changes, e => e.OldItems?.Contains(selected) == true);
        Assert.False(warnings.MostYourWordsFirst);
        Assert.True(warnings.TouchYourWords);
        Assert.Equal(GrammarFindingBucket.Errors, warnings.Bucket);
        Assert.DoesNotContain(nameof(GrammarWarningsViewModel.Findings), changed);

        context.ShowProposalView(MarkupView.Today);
        var again = Assert.IsType<GrammarWarningRowViewModel>(warnings.SelectedTableEntry);
        Assert.Equal("b", again.GroupCode);
        Assert.False(warnings.MostYourWordsFirst);
        Assert.Equal(GrammarFindingBucket.Errors, warnings.Bucket);
    }

    [Fact]
    public async Task ReopeningTheProjectDropsTheProposalsSideFromTheRows()
    {
        var (_, context, page) = await OpenedAsync();
        context.ShowProposalView(MarkupView.Proposal);
        Assert.True(page.Grammar.Warnings.ShowsProposalCheck);

        await context.OpenProjectAsync(ProjectPath);

        Assert.False(page.Grammar.Warnings.ShowsProposalCheck);
        Assert.False(page.Grammar.Warnings.Pairs);
    }

    [Fact]
    public void GoneAndNewRowsCarryAVisibleMarkAndAnAccessibleNameAndTheFiltersAreTitleCase()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.StoredGrammarCheckHandler = (request, _) => Answer(Of(request.ProposalId));
            fake.ReadTrialCompletesWith(OverviewProposalViewTests.Trial());
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            try
            {
                await workspace.Context.OpenProposalAsync("proposal/one", CancellationToken.None);
                workspace.Context.OpenPage(WorkspacePage.Warnings);
                workspace.Context.ShowProposalView(MarkupView.TodayToProposal);
                PageScreenshots.Settle(window);

                var texts = window.GetVisualDescendants().OfType<TextBlock>().Where(block => block.IsEffectivelyVisible)
                    .Select(block => block.Text).ToArray();
                Assert.Contains(MotifAppStrings.WarningsGoneMark, texts);
                Assert.Contains(MotifAppStrings.WarningsNewMark, texts);
                var names = window.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible)
                    .Select(button => Avalonia.Automation.AutomationProperties.GetName(button)).ToArray();
                Assert.Contains(names, name => name?.StartsWith("Details for ") == true && name.Contains("Gone"));
                var chips = window.GetVisualDescendants().OfType<FilterChip>().Select(chip => chip.Label).ToArray();
                Assert.Contains("Gone with This Proposal", chips);
                Assert.Contains("New with This Proposal", chips);
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }
}

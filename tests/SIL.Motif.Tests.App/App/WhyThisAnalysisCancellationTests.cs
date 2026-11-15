using Avalonia.Threading;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>Pins that a fold's old work is cancelled or ignored when it folds, when Diagnostics turns off, or when the project goes.</summary>
[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "Unit")]
public sealed class WhyThisAnalysisCancellationTests
{
    private const string ProjectPath = "/tmp/motif-why/project.fwdata";

    private static async Task<(WhyThisAnalysisTests.Rig Rig, WhyThisAnalysisViewModel Fold,
        TaskCompletionSource<CommandOutcome<WordTraceResponse>> Gate, CancellationToken[] Tokens)> StartAsync()
    {
        var rig = new WhyThisAnalysisTests.Rig();
        await rig.OpenAsync();
        var response = WhyThisAnalysisTests.Trace();
        var gate = new TaskCompletionSource<CommandOutcome<WordTraceResponse>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tokens = new CancellationToken[1];
        rig.Fake.OnTraceWord((_, cancellation) =>
        {
            tokens[0] = cancellation;
            return gate.Task;
        });
        var fold = new WhyThisAnalysisViewModel(rig.Context, response.Word);
        fold.IsExpanded = true;
        await Wait.UntilAsync(() => rig.Fake.TraceWordRequests.Count == 1, "The trace was not requested.",
            TimeSpan.FromSeconds(10), between: () => Dispatcher.UIThread.RunJobs());
        return (rig, fold, gate, tokens);
    }

    [Fact]
    public void FoldingWhileLoadingCancelsTheRequestAndIgnoresALateAnswer()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (rig, fold, gate, tokens) = await StartAsync();
            using (rig)
            using (fold)
            {
                Assert.Equal(WhyThisAnalysisState.Loading, fold.State);
                fold.IsExpanded = false;
                Assert.True(tokens[0].IsCancellationRequested);
                gate.TrySetResult(CommandOutcome<WordTraceResponse>.Success(WhyThisAnalysisTests.Trace()));
                await fold.StopAsync();
                Assert.Equal(WhyThisAnalysisState.Collapsed, fold.State);
                Assert.Empty(fold.TraceRows);
                Assert.Equal(0, rig.Fake.TryWordSessionsFake.ListenerCount);
            }
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void TurningDiagnosticsOffFoldsTheOpenFoldAndReleasesItsSession()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (rig, fold, gate, tokens) = await StartAsync();
            using (rig)
            using (fold)
            {
                rig.Context.Diagnostics = false;
                Assert.False(fold.IsExpanded);
                Assert.True(tokens[0].IsCancellationRequested);
                gate.TrySetResult(CommandOutcome<WordTraceResponse>.Success(WhyThisAnalysisTests.Trace()));
                await fold.StopAsync();
                Assert.Equal(WhyThisAnalysisState.Collapsed, fold.State);
                Assert.Equal(0, rig.Fake.TryWordSessionsFake.ListenerCount);
            }
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void DisposingTheFoldCancelsItsRequestAndReleasesItsSession()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (rig, fold, gate, tokens) = await StartAsync();
            using (rig)
            {
                fold.Dispose();
                Assert.True(tokens[0].IsCancellationRequested);
                gate.TrySetResult(CommandOutcome<WordTraceResponse>.Success(WhyThisAnalysisTests.Trace()));
                await Task.Yield();
                Assert.Empty(fold.TraceRows);
                Assert.Equal(0, rig.Fake.TryWordSessionsFake.ListenerCount);
            }
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void ReopeningAFoldedFoldAsksAgainOnlyBecauseItWasAskedToExpandAgain()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var rig = new WhyThisAnalysisTests.Rig();
            await rig.OpenAsync();
            var response = WhyThisAnalysisTests.Trace();
            rig.Fake.TraceWordCompletesWith(response);
            using var fold = new WhyThisAnalysisViewModel(rig.Context, response.Word);
            fold.IsExpanded = true;
            await Wait.UntilAsync(() => fold.State == WhyThisAnalysisState.Ready, "Not ready.",
                TimeSpan.FromSeconds(10), between: () => Dispatcher.UIThread.RunJobs());
            fold.IsExpanded = false;
            await Task.Yield();
            Assert.Single(rig.Fake.TraceWordRequests);
            fold.IsExpanded = true;
            await Wait.UntilAsync(() => fold.State == WhyThisAnalysisState.Ready, "Not ready again.",
                TimeSpan.FromSeconds(10), between: () => Dispatcher.UIThread.RunJobs());
            Assert.Equal(2, rig.Fake.TraceWordRequests.Count);
        }, TimeSpan.FromSeconds(60));
    }

    private static (Func<WordTraceRequest, CancellationToken, Task<CommandOutcome<WordTraceResponse>>> Handler,
        List<(string Word, CancellationToken Token, TaskCompletionSource<CommandOutcome<WordTraceResponse>> Gate)> Calls) Pending()
    {
        var calls = new List<(string, CancellationToken, TaskCompletionSource<CommandOutcome<WordTraceResponse>>)>();
        return ((request, cancellation) =>
        {
            var gate = new TaskCompletionSource<CommandOutcome<WordTraceResponse>>(TaskCreationOptions.RunContinuationsAsynchronously);
            cancellation.Register(() => gate.TrySetCanceled(cancellation));
            lock (calls) calls.Add((request.Word, cancellation, gate));
            return gate.Task;
        }, calls);
    }

    [Fact]
    public void ReExpandingWhileTheFirstRequestUnwindsGetsItsOwnTrace()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var rig = new WhyThisAnalysisTests.Rig();
            await rig.OpenAsync();
            var response = WhyThisAnalysisTests.Trace();
            var (handler, calls) = Pending();
            rig.Fake.OnTraceWord(handler);
            using var fold = new WhyThisAnalysisViewModel(rig.Context, response.Word);
            fold.IsExpanded = true;
            await Wait.UntilAsync(() => calls.Count == 1, "The first trace was not requested.",
                TimeSpan.FromSeconds(10), between: () => Dispatcher.UIThread.RunJobs());
            fold.IsExpanded = false;
            fold.IsExpanded = true;
            await Wait.UntilAsync(() => calls.Count == 2, "The second expand never asked for its own trace.",
                TimeSpan.FromSeconds(10), between: () => Dispatcher.UIThread.RunJobs());
            Assert.True(calls[0].Token.IsCancellationRequested);
            Assert.False(calls[1].Token.IsCancellationRequested);
            calls[1].Gate.SetResult(CommandOutcome<WordTraceResponse>.Success(response));
            await Wait.UntilAsync(() => fold.State == WhyThisAnalysisState.Ready,
                "The second request did not show its trace.", TimeSpan.FromSeconds(10),
                between: () => Dispatcher.UIThread.RunJobs());
            Assert.NotEmpty(fold.TraceRows);
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void ABaselineChangeWhileLoadingCancelsAndShowsNoStaleTrace()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var rig = new WhyThisAnalysisTests.Rig();
            await rig.OpenAsync();
            var response = WhyThisAnalysisTests.Trace();
            var (handler, calls) = Pending();
            rig.Fake.OnTraceWord(handler);
            using var fold = new WhyThisAnalysisViewModel(rig.Context, response.Word);
            fold.IsExpanded = true;
            await Wait.UntilAsync(() => calls.Count == 1, "The trace was not requested.",
                TimeSpan.FromSeconds(10), between: () => Dispatcher.UIThread.RunJobs());
            rig.Context.Baseline = (rig.Context.Baseline ?? new WorkspaceBaseline(true, "", "", "", "", null))
                with { SavedText = "another save" };
            Assert.True(calls[0].Token.IsCancellationRequested);
            Assert.False(fold.IsExpanded);
            Assert.Equal(WhyThisAnalysisState.Collapsed, fold.State);
            calls[0].Gate.TrySetResult(CommandOutcome<WordTraceResponse>.Success(response));
            await fold.StopAsync();
            Assert.Empty(fold.TraceRows);
            Assert.Empty(fold.Analyses);
            Assert.Equal(0, rig.Fake.TryWordSessionsFake.ListenerCount);
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void AProjectChangeWhileLoadingCancelsAndShowsNoStaleTrace()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var rig = new WhyThisAnalysisTests.Rig();
            await rig.OpenAsync();
            var response = WhyThisAnalysisTests.Trace();
            var (handler, calls) = Pending();
            rig.Fake.OnTraceWord(handler);
            using var fold = new WhyThisAnalysisViewModel(rig.Context, response.Word);
            fold.IsExpanded = true;
            await Wait.UntilAsync(() => calls.Count == 1, "The trace was not requested.",
                TimeSpan.FromSeconds(10), between: () => Dispatcher.UIThread.RunJobs());
            await rig.Context.OpenProjectAsync("/tmp/motif-why/other.fwdata");
            Assert.True(calls[0].Token.IsCancellationRequested);
            Assert.False(fold.IsExpanded);
            calls[0].Gate.TrySetResult(CommandOutcome<WordTraceResponse>.Success(response));
            await fold.StopAsync();
            Assert.Equal(WhyThisAnalysisState.Collapsed, fold.State);
            Assert.Empty(fold.TraceRows);
            Assert.Single(calls);
            Assert.Equal(0, rig.Fake.TryWordSessionsFake.ListenerCount);
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void SwitchingTheWordWhileLoadingCancelsTheOldFoldAndTheNewOneStartsFolded()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (handler, calls) = Pending();
            var (workspace, window) = await AnalyzeTextsLayoutTests.OpenAnalyzeTexts(
                configure: (fake, _) => fake.OnTraceWord(handler));
            try
            {
                workspace.Context.Diagnostics = true;
                var page = workspace.PageModel<TextsPageModel>();
                var inText = page.ResultsInText;
                inText.CloseTokenCard();
                await AnalyzeTextsLayoutTests.SettleReaderAsync(workspace, window);
                async Task OpenAsync(string form)
                {
                    var token = AnalyzeTextsLayoutTests.RealizedLines(inText).SelectMany(line => line.Tokens)
                        .Single(item => item.Form == form);
                    await inText.OpenTokenCardAsync(token);
                    await AnalyzeTextsLayoutTests.SettleReaderAsync(workspace, window);
                }
                await OpenAsync("mwalimu");
                var first = Assert.IsType<WhyThisAnalysisViewModel>(page.WhyThisAnalysis.Fold);
                Assert.Equal("mwalimu", first.Form);
                first.IsExpanded = true;
                await Wait.UntilAsync(() => calls.Count == 1, "The trace was not requested.",
                    TimeSpan.FromSeconds(10), between: () => Dispatcher.UIThread.RunJobs());

                await OpenAsync("alikula");
                var second = Assert.IsType<WhyThisAnalysisViewModel>(page.WhyThisAnalysis.Fold);
                Assert.NotSame(first, second);
                Assert.Equal("alikula", second.Form);
                Assert.False(second.IsExpanded);
                Assert.True(calls[0].Token.IsCancellationRequested);
                Assert.Single(calls);
                calls[0].Gate.TrySetResult(CommandOutcome<WordTraceResponse>.Success(WhyThisAnalysisTests.Trace()));
                await Task.Yield();
                Assert.Equal(WhyThisAnalysisState.Collapsed, second.State);
                Assert.Empty(second.TraceRows);
            }
            finally
            {
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromSeconds(60));
    }
}

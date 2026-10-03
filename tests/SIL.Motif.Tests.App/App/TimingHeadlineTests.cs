using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins that Timing leads with PanGloss's own measured speed, and that the page is never blank: while it reads,
/// when nothing was measured, and when the read is refused, it says so where the numbers would be.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class TimingHeadlineTests
{
    private const string ProjectPath = @"C:\projects\sample.fwdata";

    private static readonly (string Word, int Ms, string Completion)[] NineWords =
    [
        ("mwalimu", 700, "Step limit"), ("hawajafika", 48, "Finished"), ("walikula", 12, "Finished"),
        ("alikula", 9, "Finished"), ("ninakula", 9, "Finished"), ("tunakula", 8, "Finished"),
        ("wanakula", 7, "Finished"), ("unakula", 6, "Finished"), ("kula", 1, "Finished"),
    ];

    [Fact]
    public async Task TheHeadlineGivesTotalTimeMedianAndStoppedWordsForAllWords()
    {
        var (fake, context) = NewContext();
        var timing = new TimingPageModel(context);
        fake.OnTiming((request, _) => Task.FromResult(CommandOutcome<TimingResponse>.Success(Response(request))));
        await context.OpenProjectAsync(ProjectPath);

        context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.Now, WasRerun: false));

        Assert.True(timing.HasHeadline);
        Assert.Equal("800 ms", timing.HeadlineTotal);
        Assert.Equal("for all 9 words", timing.HeadlineTotalCaption);
        Assert.Equal("9 ms", timing.HeadlineMedian);
        Assert.Equal("1", timing.HeadlineStopped);
        Assert.Equal("stopped at the step limit", timing.HeadlineStoppedCaption);
        Assert.True(timing.IsAllSelected);
        Assert.Equal(1_000_000m, timing.RerunSteps);
        Assert.True(timing.HasStoppedWords);
        Assert.Equal("Stopped at the step limit · Raise it under More.",
            timing.StoppedWordsAdviceText);
    }

    [Fact]
    public async Task TimedOutWordsPointToThePerWordTimeControl()
    {
        var (fake, context) = NewContext();
        var timing = new TimingPageModel(context);
        fake.OnTiming((request, _) => Task.FromResult(CommandOutcome<TimingResponse>.Success(
            Response(request) with
            {
                Words = NineWords.Select(word => new TimingWordRow(word.Word, word.Ms,
                    word.Word == "mwalimu" ? "Time limit" : word.Completion)).ToArray(),
            })));
        await context.OpenProjectAsync(ProjectPath);

        context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.Now, WasRerun: false));
        await context.EvidencePublication;

        Assert.True(timing.HasStoppedWords);
        Assert.Equal("Ran out of time · Increase Seconds per word.", timing.StoppedWordsAdviceText);
    }

    [Fact]
    public async Task TheHeadlineNamesAChosenGroupAsTheseWords()
    {
        var (fake, context) = NewContext();
        var timing = new TimingPageModel(context);
        fake.OnTiming((request, _) => Task.FromResult(CommandOutcome<TimingResponse>.Success(Response(request))));
        await context.OpenProjectAsync(ProjectPath);
        context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.Now, WasRerun: false));

        await timing.SelectWordSetCommand.ExecuteAsync("slowest");

        Assert.Equal("for these 9 words", timing.HeadlineTotalCaption);
    }

    [Fact]
    public async Task NanosecondOnlyWordsCountAsMeasuredForTheHeadline()
    {
        var (fake, context) = NewContext();
        var timing = new TimingPageModel(context);
        fake.OnTiming((request, _) => Task.FromResult(CommandOutcome<TimingResponse>.Success(
            Response(request) with
            {
                Words =
                [
                    new TimingWordRow("quick", null, TimingCompletion.Finished) { ElapsedNs = 800_000 },
                    new TimingWordRow("quicker", null, TimingCompletion.Finished) { ElapsedNs = 200_000 },
                    new TimingWordRow("missing", null, TimingCompletion.Finished),
                ],
                Attribution = new WordTimeAttribution(2, 1, 1, 0, 0, 0, false),
            })));
        await context.OpenProjectAsync(ProjectPath);

        context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.Now, WasRerun: false));

        Assert.True(timing.HasHeadline);
        Assert.Equal("for 2 of the 3 words", timing.HeadlineTotalCaption);
    }

    [Fact]
    public async Task StoredTimingSummaryNamesPercentilesThatWereNotRecorded()
    {
        var (fake, context) = NewContext();
        var timing = new TimingPageModel(context);
        fake.OnTiming((request, _) => Task.FromResult(CommandOutcome<TimingResponse>.Success(
            Response(request) with { MedianMs = null, Percentile95Ms = null })));
        await context.OpenProjectAsync(ProjectPath);

        context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.Now, WasRerun: false));

        Assert.Equal("9 words · median not recorded · 95th percentile not recorded", timing.StoredTimingSummary);
    }

    [Fact]
    public async Task NoMeasuredParseTimeOffersParseAllWordsInsteadOfABlankPage()
    {
        var (fake, context) = NewContext();
        var timing = new TimingPageModel(context);
        await context.OpenProjectAsync(ProjectPath);

        context.PublishEvidence(new WorkspaceEvidence(Assessment() with { Measurements = [] }, DateTimeOffset.Now,
            WasRerun: false));

        Assert.False(timing.HasHeadline);
        Assert.True(timing.ShowNoTimingRecorded);
        Assert.Equal("No parse times were recorded for these words.", TimingPageModel.NoTimingRecordedText);
        Assert.Empty(fake.TimingRequests);
    }

    [Fact]
    public async Task WhileTheTimesAreReadThePageSaysSo()
    {
        var (fake, context) = NewContext();
        var timing = new TimingPageModel(context);
        var completion = new TaskCompletionSource<CommandOutcome<TimingResponse>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fake.OnTiming((_, _) => completion.Task);
        await context.OpenProjectAsync(ProjectPath);

        context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.Now, WasRerun: false));

        Assert.True(timing.IsLoadingTiming);
        Assert.False(timing.ShowNoTimingRecorded);
        completion.SetResult(CommandOutcome<TimingResponse>.Success(Response(new TimingRequest(ProjectPath))));
        await context.EvidencePublication;

        Assert.False(timing.IsLoadingTiming);
        Assert.True(timing.HasHeadline);
    }

    [Fact]
    public async Task ARefusedReadIsNotReportedAsNothingRecorded()
    {
        var (fake, context) = NewContext();
        var timing = new TimingPageModel(context);
        fake.OnTiming((_, _) => Task.FromResult(CommandOutcome<TimingResponse>.Refused(
            new Refusal("timing.unexpected", FailureReason.Refused, "The store could not be read."))));
        await context.OpenProjectAsync(ProjectPath);

        context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.Now, WasRerun: false));

        Assert.True(timing.HasTimingRefusal);
        Assert.False(timing.ShowNoTimingRecorded);
        Assert.False(timing.IsLoadingTiming);
    }

    [Fact]
    public void TheTimingPageIsNeverBlank()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (_, context) = NewContext();
            var timing = new TimingPageModel(context);
            await context.OpenProjectAsync(ProjectPath);
            context.PublishEvidence(new WorkspaceEvidence(Assessment() with { Measurements = [] },
                DateTimeOffset.Now, WasRerun: false));

            var window = new Window { Width = 1240, Height = 900, Content = new TimingPage(timing) };
            try
            {
                window.Show();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                var shown = window.GetLogicalDescendants().OfType<TextBlock>()
                    .Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToArray();
                Assert.Contains(TimingPageModel.NoTimingRecordedText, shown);
                // The only action offered is one the page can run now; a disabled one would be a dead control.
                var parseAgain = window.GetLogicalDescendants().OfType<Button>()
                    .Single(button => Equals(button.Content, "Parse again"));
                Assert.Same(context.Assess.RunCommand, parseAgain.Command);
                Assert.Equal(context.Assess.RunCommand.CanExecute(null), parseAgain.IsEffectivelyVisible);
                Assert.DoesNotContain(window.GetLogicalDescendants().OfType<Button>(), button =>
                    Equals(button.Content, "Parse all words"));
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void EmptyStepLimitFilterNamesTheFilterAndOffersAllWords()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (fake, context) = NewContext();
            var timing = new TimingPageModel(context);
            fake.OnTiming((request, _) => Task.FromResult(CommandOutcome<TimingResponse>.Success(
                request.WordSet == "step-limit"
                    ? Response(request) with { WordCount = 0, Words = [], SlowestWords = [] }
                    : Response(request))));
            await context.OpenProjectAsync(ProjectPath);
            context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.Now, WasRerun: false));
            await context.EvidencePublication;
            await timing.SelectWordSetCommand.ExecuteAsync("step-limit");

            var window = new Window { Width = 900, Height = 700, Content = new TimingPage(timing) };
            try
            {
                window.Show();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                var text = string.Join(" ", window.GetLogicalDescendants().OfType<TextBlock>()
                    .Where(item => item.IsEffectivelyVisible).Select(item => item.Text));
                Assert.Contains("No words stopped at the step limit.", text);
                var allWords = Assert.Single(window.GetLogicalDescendants().OfType<Button>(),
                    button => Equals(button.Content, "Show all words"));
                Assert.True(allWords.IsEffectivelyVisible);
                Assert.True(allWords.IsTabStop);
                allWords.Command!.Execute(allWords.CommandParameter);
                await timing.SelectWordSetCommand.ExecutionTask!;
                Assert.True(timing.IsAllSelected);
                Assert.True(timing.HasHeadline);
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void TheHeadlineSitsAboveTheControls()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (fake, context) = NewContext();
            var timing = new TimingPageModel(context);
            fake.OnTiming((request, _) => Task.FromResult(CommandOutcome<TimingResponse>.Success(Response(request))));
            await context.OpenProjectAsync(ProjectPath);
            context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.Now, WasRerun: false));

            var window = new Window { Width = 1240, Height = 900, Content = new TimingPage(timing) };
            try
            {
                window.Show();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                var texts = window.GetLogicalDescendants().OfType<TextBlock>()
                    .Where(text => text.IsEffectivelyVisible).ToArray();
                var headline = texts.First(text => text.Text == "800 ms");
                var lookAt = texts.First(text => text.Text == "Words");
                Assert.True(headline.TranslatePoint(default, window)!.Value.Y <
                    lookAt.TranslatePoint(default, window)!.Value.Y);
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void LongRuleTitlesShowAnEllipsisAndKeepTheFullTooltip()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (fake, context) = NewContext();
            var timing = new TimingPageModel(context);
            const string titleText = "Default Right Head Compound with a Long Grammar Rule Name";
            fake.OnTiming((request, _) =>
            {
                var response = Response(request);
                if (request.By == "rule")
                    response = response with
                    {
                        Aggregates = [new TimingAggregateRow("mrule#0:long", titleText, 700, 0.875, 9)
                            { Kind = "morph_rule", IdentityQuality = "structural" }],
                    };
                return Task.FromResult(CommandOutcome<TimingResponse>.Success(response));
            });
            await context.OpenProjectAsync(ProjectPath);
            context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.Now, WasRerun: false));
            await context.EvidencePublication;

            var window = new Window { Width = 900, Height = 700, Content = new TimingPage(timing) };
            try
            {
                window.Show();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                var title = window.GetLogicalDescendants().OfType<TextBlock>()
                    .Single(text => text.Text == titleText && text.Classes.Contains("section-title"));
                Assert.Equal(TextTrimming.CharacterEllipsis, title.TextTrimming);
                Assert.Equal(titleText, ToolTip.GetTip(title));
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromSeconds(10));
    }

    private static TimingResponse Response(TimingRequest request) =>
        new(request.AssessmentId ?? "assessment-parse", request.WordSet, request.By, NineWords.Length, 9, 700,
            [new SlowWordTiming("mwalimu", 700), new SlowWordTiming("hawajafika", 48)],
            request.By == "kind" ? [new TimingAggregateRow("Morphological rules", "Morphological rules", 800, 1, 9)] : [], [])
        {
            Words = NineWords.Select(word => new TimingWordRow(word.Word, word.Ms, word.Completion)).ToArray(),
            Attribution = new WordTimeAttribution(9, 800, 800, 0, 0, 0, false),
        };

    private static AssessCommandResponse Assessment() => new(
        new BaselineCaptureResponse(
            new BaselineToken("project-1", "sha256:" + new string('a', 64), "1", "2026-09-05T11:02:00Z",
                "sha256:" + new string('b', 64)),
            ProjectPath, DateTimeOffset.UtcNow, false, false),
        new SelectionProjection([], []), [], "summary")
    {
        InvocationId = "invocation/one",
        Measurements = [new ProducedAssessmentReference("assessment-parse", "ParseTime", "invocation/one")],
    };

    private static (FakeCommandClient Fake, WorkspaceContext Context) NewContext()
    {
        var fake = new FakeCommandClient();
        return (fake, WorkspaceContextTests.NewContext(fake));
    }
}

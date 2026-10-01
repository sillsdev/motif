using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
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
        Assert.Equal("0.8 s", timing.HeadlineTotal);
        Assert.Equal("for all 9 words", timing.HeadlineTotalCaption);
        Assert.Equal("9 ms", timing.HeadlineMedian);
        Assert.Equal("1", timing.HeadlineStopped);
        Assert.Equal("stopped at the step limit", timing.HeadlineStoppedCaption);
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
                var headline = texts.First(text => text.Text == "0.8 s");
                var lookAt = texts.First(text => text.Text == "Look at");
                Assert.True(headline.TranslatePoint(default, window)!.Value.Y <
                    lookAt.TranslatePoint(default, window)!.Value.Y);
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

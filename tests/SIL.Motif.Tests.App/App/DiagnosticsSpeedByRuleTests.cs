using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App.Views;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class DiagnosticsSpeedByRuleTests
{
    [Fact]
    public void TheRuleRowUsesItsLiveMeasuredWordDenominator()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            var context = WorkspaceContextTests.NewContext(fake);
            context.Diagnostics = true;
            var model = new DiagnosticsPageModel(context) { SelectedTabIndex = 1 };
            model.SpeedByRule.Rows = [new("rule", "Recorded rule", "phon_rule", 12, false)];
            model.SpeedByRule.MeasuredWordCount = 2;
            var page = new DiagnosticsPage(model);
            var window = new MotifTestWindow { Width = 640, Height = 600, Content = page };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                Assert.Contains(page.GetVisualDescendants().OfType<CopyableTextBlock>(),
                    text => text.IsEffectivelyVisible && text.Text == "6.00");
                model.SpeedByRule.MeasuredWordCount = 0;
                PageScreenshots.Settle(window);
                Assert.Contains(page.GetVisualDescendants().OfType<CopyableTextBlock>(),
                    text => text.IsEffectivelyVisible && text.Text == MotifAppStrings.TimingNotRecorded);
                Assert.Empty(fake.AssessRequests);
            }
            finally { window.Close(); await context.StopProjectWorkAsync(); }
        }, TimeSpan.FromSeconds(30));
    }

    [UiThreadTheory]
    [InlineData(2, "6.00")]
    [InlineData(0, null)]
    public async Task DividesRecordedStepsByMeasuredWordsOnly(int measured, string? expected)
    {
        var fake = new FakeCommandClient();
        fake.TimingCompletesWith(new TimingResponse("assessment-parse", "all", "rule", 5, 999, 999, [], [], [])
        {
            Work = new WordWorkSummary(measured, 20, null, null),
            ParserWorkAggregates = [new("rule", "Recorded rule", "phon_rule", 12, false),
                new("remainder", "remainder", "parser", 8, true)],
            ObjectTimingDetail = new(false),
        });
        var context = WorkspaceContextTests.NewContext(fake);
        await context.OpenProjectAsync("project.fwdata");
        context.PublishEvidence(new WorkspaceEvidence(WorkspaceContextTests.Assessment(), DateTimeOffset.UtcNow, false));
        var model = new SpeedByRuleViewModel(context);
        await model.ReadAsync("project.fwdata", CancellationToken.None);
        Assert.Equal(expected ?? MotifAppStrings.TimingNotRecorded, model.DisplayRows[0].StepsPerWord);
        Assert.Equal(MotifAppStrings.TimingNotAttributedLabel, model.DisplayRows[1].Name);
        Assert.Contains($"{measured} of 5", model.DenominatorText);
        Assert.Equal(MotifAppStrings.DiagnosticsPartial, model.DetailText);
        Assert.Empty(fake.AssessRequests);
        await context.StopProjectWorkAsync();
    }

    [UiThreadFact]
    public async Task BeginningAnotherReadHidesOldTotalsBeforeItCompletes()
    {
        var fake = new FakeCommandClient();
        var context = WorkspaceContextTests.NewContext(fake);
        await context.OpenProjectAsync("project.fwdata");
        context.PublishEvidence(new WorkspaceEvidence(WorkspaceContextTests.Assessment(), DateTimeOffset.UtcNow, false));
        var model = new SpeedByRuleViewModel(context);
        fake.TimingCompletesWith(new TimingResponse("assessment-parse", "all", "rule", 1, null, null, [], [], [])
        {
            Work = new(1, 8, 8, 8), ParserWorkAggregates = [new("rule", "Recorded", "phon_rule", 8, false)],
        });
        await model.ReadAsync("project.fwdata", CancellationToken.None);
        Assert.Single(model.DisplayRows);
        var pending = new TaskCompletionSource<CommandOutcome<TimingResponse>>();
        fake.OnTiming((_, _) => pending.Task);
        using var stop = new CancellationTokenSource();
        var read = model.ReadAsync("project.fwdata", stop.Token);
        Assert.Empty(model.DisplayRows);
        stop.Cancel();
        pending.SetCanceled(stop.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        await context.StopProjectWorkAsync();
    }
    [UiThreadFact]
    public async Task ASelectedRuleResolvesOnlyItsVerifiedFieldWorksLink()
    {
        var fake = new FakeCommandClient();
        var work = new TimingWorkAggregate("rule-id", "Exact rule", "phon_rule", 12, false) { IdentityQuality = "authored" };
        fake.OnInspect((request, _) => Task.FromResult(CommandOutcome<InspectResponse>.Success(
            new InspectResponse(request.Subject, InspectorResolution.Resolved)
            {
                Facts = InspectorSection<ObjectFacts>.Of(new ObjectFacts
                {
                    Rule = new ObjectFactsRule("rule-id", "phon_rule", "Exact rule")
                    {
                        FieldWorks = new("PhonologicalRuleEdit", "Phonological Rules", "rule-id", "silfw://localhost/link?guid=rule-id"),
                    },
                }),
            })));
        var context = WorkspaceContextTests.NewContext(fake);
        context.ProjectPath = "project.fwdata";
        context.Diagnostics = true;
        var model = new SpeedByRuleViewModel(context);
        model.SelectedRow = new DiagnosticRuleRow(work, 2);
        await model.LinkRead;
        Assert.Equal("silfw://localhost/link?guid=rule-id", model.FieldWorksAddress?.AbsoluteUri);
        Assert.Equal(work.TimingKey, Assert.Single(fake.InspectRequests).Subject.TimingKey);
        model.Clear();
        Assert.Null(model.FieldWorksAddress);
        await context.StopProjectWorkAsync();
    }
}

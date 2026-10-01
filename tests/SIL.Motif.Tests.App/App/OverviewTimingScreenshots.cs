using Avalonia;
using Avalonia.Styling;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Saves the Overview and Timing pages over a read Overview and stored parse times, and Try a Word beside its
/// earlier parse's timing, the states the every-page capture cannot reach because its sample data configures
/// neither read.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class OverviewTimingScreenshots
{
    private static readonly (string Word, int Ms, string Completion)[] Words =
    [
        ("mwalimu", 700, "Step limit"), ("hawajafika", 48, "Finished"), ("walikula", 12, "Finished"),
        ("alikula", 9, "Finished"), ("ninakula", 9, "Finished"), ("tunakula", 8, "Finished"),
        ("wanakula", 7, "Finished"), ("unakula", 6, "Finished"), ("kula", 1, "Finished"),
    ];

    [ScreenshotFact]
    public void CapturePopulatedOverviewAndTiming()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);

        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(configure: ReadOverviewAndTiming);
            try
            {
                workspace.Context.TryWord("matinlu");
                await workspace.Assess.Trace.TryCommand.ExecutionTask!;
                foreach (var (theme, variant) in new[] { ("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark) })
                {
                    Application.Current!.RequestedThemeVariant = variant;
                    foreach (var width in new[] { 1040, 1240 })
                    {
                        window.Width = width;
                        window.Height = 780;
                        foreach (var (name, page) in new[]
                                 {
                                     ("14-overview-populated", WorkspacePage.Overview),
                                     ("16-timing-filled", WorkspacePage.Timing),
                                     ("17-try-a-word-earlier-timing", WorkspacePage.TryAWord),
                                 })
                        {
                            workspace.CurrentPage = page;
                            PageScreenshots.Save(window, Path.Combine(folder, $"{name}-{width}-{theme}.png"));
                        }
                    }
                }
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                window.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }

    /// <summary>Gives the sample data a read Overview and stored parse times.</summary>
    internal static void ReadOverviewAndTiming(FakeCommandClient fake, AssessCommandResponse assessment)
    {
        fake.OverviewCompletesWith(OverviewPageWordsTests.Populated());
        fake.OnTiming((request, _) => Task.FromResult(CommandOutcome<TimingResponse>.Success(
            request.ExplicitWords is ["matinlu"] ? MatinluTiming() : Timing(request.By))));
        fake.AssessCompletesWith(assessment with
        {
            Measurements = [new ProducedAssessmentReference("assessment/one", "ParseTime", "assessment/one")],
        });
    }

    // The stored parse kept matinlu's time to the whole millisecond, more than its two rules recorded.
    private static TimingResponse MatinluTiming() =>
        new("assessment/one", "all", "rule", 1, 1, 1, [new SlowWordTiming("matinlu", 1)],
            [
                new TimingAggregateRow("lu", "lu", 0.4, 0.4, 1) { Kind = "morph_rule" },
                new TimingAggregateRow("ma", "ma", 0.3, 0.3, 1) { Kind = "morph_rule" },
            ], [])
        {
            Words = [new TimingWordRow("matinlu", 1, TimingCompletion.Finished)],
            Attribution = new WordTimeAttribution(1, 1, 0.7, 0.3, 0.3, 0, false),
        };

    private static TimingResponse Timing(string by) =>
        new("assessment/one", "all", by, Words.Length, 9, 700,
            [new SlowWordTiming("mwalimu", 700), new SlowWordTiming("hawajafika", 48), new SlowWordTiming("walikula", 12)],
            // The kinds record 680 ms of the words' 800 ms, so the page has other time to show.
            by == "kind"
                ?
                [
                    new TimingAggregateRow("morph_rule", "morph_rule", 448, 448d / 800, 9) { Kind = "morph_rule" },
                    new TimingAggregateRow("phon_rule", "phon_rule", 160, 160d / 800, 7) { Kind = "phon_rule" },
                    new TimingAggregateRow("lex_entry", "lex_entry", 40, 40d / 800, 9) { Kind = "lex_entry" },
                    new TimingAggregateRow("root_index", "root_index", 32, 32d / 800, 9) { Kind = "root_index" },
                ]
                :
                [
                    new TimingAggregateRow("Subject agreement", "Subject agreement", 288, 0.36, 6) { Kind = "morph_rule" },
                    new TimingAggregateRow("Past tense li-", "Past tense li-", 152, 0.19, 3) { Kind = "morph_rule" },
                    new TimingAggregateRow("Vowel harmony", "Vowel harmony", 120, 0.15, 7) { Kind = "phon_rule" },
                ],
            by == "kind" ? [] : [new WordRuleTiming("mwalimu", 180, 900), new WordRuleTiming("hawajafika", 34, 200)])
        {
            Words = Words.Select(word => new TimingWordRow(word.Word, word.Ms, word.Completion)).ToArray(),
            Attribution = new WordTimeAttribution(Words.Length, 800, 680, 120, 0.15, 0, false),
        };
}

using Avalonia;
using Avalonia.Styling;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Saves the Overview and Timing pages over a read Overview and stored parse times, the states the every-page
/// capture cannot reach because its sample data configures neither read.
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
        fake.OnTiming((request, _) => Task.FromResult(CommandOutcome<TimingResponse>.Success(Timing(request.By))));
        fake.AssessCompletesWith(assessment with
        {
            Measurements = [new ProducedAssessmentReference("assessment/one", "ParseTime", "assessment/one")],
        });
    }

    private static TimingResponse Timing(string by) =>
        new("assessment/one", "all", by, Words.Length, 9, 700,
            [new SlowWordTiming("mwalimu", 700), new SlowWordTiming("hawajafika", 48), new SlowWordTiming("walikula", 12)],
            by == "kind"
                ?
                [
                    new TimingAggregateRow("Morphological rules", 512, 0.64, 3500, 9),
                    new TimingAggregateRow("Phonological rules", 176, 0.22, 1200, 7),
                    new TimingAggregateRow("Lexical entries", 72, 0.09, 400, 9),
                    new TimingAggregateRow("Root lookup", 40, 0.05, 200, 9),
                ]
                :
                [
                    new TimingAggregateRow("Subject agreement", 288, 0.36, 2100, 6) { Kind = "Morphological rules" },
                    new TimingAggregateRow("Past tense li-", 152, 0.19, 900, 3) { Kind = "Morphological rules" },
                    new TimingAggregateRow("Vowel harmony", 120, 0.15, 800, 7) { Kind = "Phonological rules" },
                ],
            by == "kind" ? [] : [new WordRuleTiming("mwalimu", 180, 900), new WordRuleTiming("hawajafika", 34, 200)])
        {
            Words = Words.Select(word => new TimingWordRow(word.Word, word.Ms, word.Ms * 7, word.Completion)).ToArray(),
        };
}

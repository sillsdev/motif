using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Saves an Analyze texts word card inside a Proposal, with the block that says what the Proposal's Trial built for
/// the word and the Fix menu entries that approve it there.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "System")]
public sealed class ProposalWordCardScreenshots
{
    private const string ProposalId = "proposal/sound";
    private const string Word = "hawajafika";

    private static readonly Guid MorphGuid = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly ParseAnalysis Built = new([new ParseMorph(MorphGuid.ToString("D"), "msa-2", null, null)]);

    [ScreenshotFact]
    public void CaptureWordCardInAProposal()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);

        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await AnalyzeTextsLayoutTests.OpenAnalyzeTexts(configure: (fake, _) =>
                fake.ReadTrialCompletesWith(Trial()));
            try
            {
                await workspace.Context.OpenProposalAsync(ProposalId, CancellationToken.None);
                window.Height = 1500;
                await AnalyzeTextsLayoutTests.SettleReaderAsync(workspace, window);
                var inText = workspace.PageModel<TextsPageModel>().ResultsInText;
                var token = AnalyzeTextsLayoutTests.RealizedLines(inText).SelectMany(line => line.Tokens)
                    .Single(item => item.Form == Word);
                await inText.OpenTokenCardAsync(token);
                AnalyzeTextsLayoutTests.Settle(window);
                Assert.True(token.ShowsProposalBlock);
                Assert.True(token.ApproveInProposal is not null || token.AddExpectedParse is not null);
                var card = AnalyzeTextsLayoutTests.OpenCard(window);
                var fix = card.GetVisualDescendants().OfType<Button>().First(button =>
                    button.Flyout is Flyout && button.IsEffectivelyVisible && button.DataContext is ResultsTokenViewModel);
                var flyout = (Flyout)fix.Flyout!;
                SaveAll(window, folder, "proposal-word-card");
                flyout.ShowAt(fix);
                AnalyzeTextsLayoutTests.Settle(window);
                Assert.True(flyout.IsOpen);
                SaveAll(window, folder, "proposal-word-card-fix-menu");
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                window.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }

    private static TrialRecord Trial() => new("job/trial", ProposalId, "sha256:intent",
        PageScreenshots.Token(), null,
        new Dictionary<string, string> { [AssessmentKinds.ParseTime] = "trial-root" }, [Word], false, null,
        JobStatus.Completed, new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero), TrialFreshness.Current)
    {
        Label = "Sound change",
        WordResults =
        [
            new AssessmentWordResult(Word, "analysed", false, "Complete", 3, null)
            {
                Morphology = new ParseWordEvidence("v1", 0, Word, 3, false, false, false, [Built], []),
                Readings =
                [
                    new ParserReading([new ParserReadingMorph("ha-", "NEG", "pref", null, false, "silfw://entry")]),
                ],
            },
        ],
    };

    private static void SaveAll(MainWindow window, string folder, string name)
    {
        foreach (var (theme, variant) in new[] { ("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark) })
        {
            Application.Current!.RequestedThemeVariant = variant;
            foreach (var width in new[] { 1040, 1240 })
                Save(window, width, Path.Combine(folder, $"{name}-{width}-{theme}.png"));
        }
    }

    private static void Save(MainWindow window, int width, string path)
    {
        window.Width = width;
        AnalyzeTextsLayoutTests.Settle(window);
        LayoutAssertions.BeforeCapture(window);
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException($"No frame rendered for {path}.");
        frame.Save(path, PngBitmapEncoderOptions.Default);
    }
}

using Avalonia;
using Avalonia.Styling;
using Avalonia.LogicalTree;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Ids;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class CompareComparisonScreenshots
{
    [ScreenshotFact]
    public void CaptureMixedOpinionMatrixAndTexts()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var candidateForm = Guid.NewGuid().ToString("D");
            var rejectedForm = Guid.NewGuid().ToString("D");
            var msa = Guid.NewGuid().ToString("D");
            var candidate = new ParserReading([new("a-", "3SG", "v", null, false, null)])
            {
                StoredAnalysisId = CanonicalId.FromGuid(Guid.NewGuid()).Value, StoredAnalysisOpinion = ReadingGrade.Candidate,
                Identity = new ApprovedMorphology([new(candidateForm, msa, null, [])]),
            };
            var disapproved = new ParserReading([new("-a", "FV", "v", null, false, null)])
            {
                StoredAnalysisId = CanonicalId.FromGuid(Guid.NewGuid()).Value, StoredAnalysisOpinion = ReadingGrade.Disapproved,
                Identity = new ApprovedMorphology([new(rejectedForm, msa, null, [])]),
            };
            var mixed = new AssessmentWordResult("alikula", "analysed", false, "Search completed", 1, null)
            {
                ProjectStanding = ProjectStanding.Candidate, StoredAnalyses = [candidate, disapproved],
                ReadingGrades = [ReadingGrade.Disapproved], Readings = [disapproved with { StoredAnalysisId = null }],
                Morphology = new ParseWordEvidence("v1", 0, "alikula", 1, false, false, false,
                    [new ParseAnalysis([new(rejectedForm, msa, null, null)])], []),
            };
            ProjectAnalysis Stored(ParserReading reading) => new("", reading.Morphs)
            {
                StoredAnalysisId = reading.StoredAnalysisId, StoredAnalysisOpinion = reading.StoredAnalysisOpinion,
                Identity = reading.Identity,
            };
            var textId = Guid.NewGuid();
            var undecided = Stored(candidate);
            var rejected = Stored(disapproved);
            var token = new TextToken(mixed.Word, mixed.Word, null, null)
            { Analysis = undecided, StoredAnalyses = [undecided, rejected] };
            var captured = new TextWordsResponse(
                [new(mixed.Word, null, [new(textId, "Mixed opinions", 1, mixed.Word, "unapproved", undecided)],
                    [], [rejected], CandidateCount: 1) { Analyses = [undecided, rejected] }],
                [new(textId, "Mixed opinions", [new(1, [new("Word: ", null, null, null), token])])],
                HasBaseline: true, OccurrenceCount: 1);
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(capturedWords: captured,
                capturedAssessment: PageScreenshots.Assessment() with { Words = [mixed] });
            try
            {
                workspace.CurrentPage = WorkspacePage.Texts;
                workspace.PageModel<TextsPageModel>().Tab = TextsTab.Matrix;
                PageScreenshots.Settle(window);
                var matrix = workspace.Assess.Compare;
                matrix.SelectCells([new(WordProjectStatus.Candidate, CompareColumnKind.NoMatch)]);
                var meaning = matrix.Words.Single(word => word.Word == mixed.Word).Meaning;
                Assert.Equal("Have a look", meaning);
                Assert.False(matrix.ShowsMeaning);
                foreach (var (theme, variant) in new[] { ("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark) })
                {
                    Application.Current!.RequestedThemeVariant = variant;
                    foreach (var width in new[] { 1040, 1240 })
                    {
                        window.Width = width;
                        window.Height = 780;
                        workspace.PageModel<TextsPageModel>().Tab = TextsTab.Matrix;
                        PageScreenshots.Save(window, Path.Combine(folder, $"compare-mixed-{width}-{theme}.png"));
                        workspace.PageModel<TextsPageModel>().Tab = TextsTab.AnalyzeTexts;
                        await AnalyzeTextsLayoutTests.SettleReaderAsync(workspace, window);
                        var inText = workspace.PageModel<TextsPageModel>().ResultsInText;
                        var anchor = Assert.Single(workspace.Context.SelectionReads.Summary!.SourcePositions).Location.Anchor;
                        var panel = window.GetLogicalDescendants().OfType<ResultsInTextPanel>().Single();
                        var word = await inText.ReadOccurrenceAsync(anchor);
                        Assert.NotNull(word);
                        inText.SelectToken(word);
                        Assert.True(await panel.FocusOccurrenceAsync(anchor));
                        await AnalyzeTextsLayoutTests.SettleReaderAsync(workspace, window);
                        Assert.Equal(meaning, word.PanGlossSummary);
                        Assert.Contains("Your undecided analysis wasn't built", word.ComparisonDetail);
                        Assert.Contains("PanGloss matched an analysis FieldWorks marked Disapproved.", word.ComparisonDetail);
                        PageScreenshots.Save(window, Path.Combine(folder, $"compare-mixed-texts-{width}-{theme}.png"));
                    }
                }
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                window.Close();
            }
        }, TimeSpan.FromMinutes(2));
    }
}

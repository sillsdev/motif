using Avalonia;
using Avalonia.Styling;
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
            var candidate = new ParserReading([new("a-", "3SG", "v", null, false, null)])
            {
                StoredAnalysisId = "undecided", StoredAnalysisOpinion = ReadingGrade.Candidate,
                Identity = new ApprovedMorphology([new("candidate", "msa", null, [])]),
            };
            var disapproved = new ParserReading([new("-a", "FV", "v", null, false, null)])
            {
                StoredAnalysisId = "disapproved", StoredAnalysisOpinion = ReadingGrade.Disapproved,
                Identity = new ApprovedMorphology([new("disapproved", "msa", null, [])]),
            };
            var mixed = new AssessmentWordResult("alikula", "analysed", false, "Search completed", 1, null)
            {
                ProjectStanding = ProjectStanding.Candidate, StoredAnalyses = [candidate, disapproved],
                ReadingGrades = [ReadingGrade.Disapproved], Readings = [disapproved with { StoredAnalysisId = null }],
                Morphology = new ParseWordEvidence("v1", 0, "alikula", 1, false, false, false,
                    [new ParseAnalysis([new("disapproved", "msa", null, null)])], []),
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
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(configure: (fake, assessment) =>
            {
                fake.ListTextsCompletesWith(new([new(textId, "Mixed opinions")], HasBaseline: true));
                fake.ListTextWordsCompletesWith(new(
                    [new(mixed.Word, null, [new(textId, "Mixed opinions", 1, mixed.Word, "unapproved", undecided)],
                        [], [rejected], CandidateCount: 1) { Analyses = [undecided, rejected] }],
                    [new(textId, "Mixed opinions", [new(1, [new("Word: ", null, null, null), token])])],
                    HasBaseline: true, OccurrenceCount: 1));
                fake.AssessCompletesWith(assessment with { Words = assessment.Words.Select(word =>
                    word.Word == mixed.Word ? mixed : word).ToArray() });
            });
            try
            {
                workspace.CurrentPage = WorkspacePage.Texts;
                workspace.PageModel<TextsPageModel>().Tab = TextsTab.Matrix;
                var matrix = workspace.Assess.Compare;
                matrix.SelectCells([new(WordProjectStatus.Candidate, CompareColumnKind.NoMatch)]);
                Assert.Equal("Differs: have a look", matrix.Words.Single(word => word.Word == mixed.Word).Meaning);
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
                        var inText = workspace.PageModel<TextsPageModel>().ResultsInText;
                        var word = Assert.Single(inText.Texts.SelectMany(text => text.Lines).SelectMany(line => line.Tokens),
                            token => token.IsWord);
                        inText.SelectToken(word);
                        Assert.Equal(matrix.Words.Single(row => row.Word == mixed.Word).Meaning, word.PanGlossSummary);
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

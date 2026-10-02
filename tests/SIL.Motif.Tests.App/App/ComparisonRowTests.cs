using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using Xunit;
using RowView = SIL.Motif.App.Views.WordRow;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ComparisonRowTests
{
    private static readonly ParserReadingMorph HomographA =
        new("x", "thing", "n", null, false, null)
        {
            AllomorphId = "aaaaaaaa-0000-0000-0000-000000000001",
            GrammaticalInfoId = "cccccccc-0000-0000-0000-000000000001",
        };

    private static readonly ParserReadingMorph HomographB = HomographA with
    {
        AllomorphId = "bbbbbbbb-0000-0000-0000-000000000001",
    };

    private static SIL.Motif.Contract.Responses.WordRow Project(
        IReadOnlyList<ParserReadingMorph> stored, IReadOnlyList<ParserReadingMorph> parsed)
    {
        var expected = new ParserReading(stored)
        {
            StoredAnalysisId = "stored-analysis", StoredAnalysisOpinion = ReadingGrade.Approved,
            Identity = new ApprovedMorphology(stored.Select(morph =>
                new ApprovedMorph(morph.AllomorphId!, morph.GrammaticalInfoId!, null, [])).ToArray()),
        };
        return WordRowProjection.Of(new AssessmentWordResult("xx", "analysed", false, "Finished", 1, null)
        {
            ProjectStanding = ProjectStanding.Approved,
            ExpectedAnalysis = expected, StoredAnalyses = [expected], Readings = [new ParserReading(parsed)],
            Morphology = new ParseWordEvidence("v1", 0, "xx", 1, false, false, false,
                [new ParseAnalysis(parsed.Select(morph =>
                    new ParseMorph(morph.AllomorphId ?? "", morph.GrammaticalInfoId, null, null)).ToArray())], []),
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedHomographsExplainTheSequenceWithoutInventingAnIdentityDifference(bool alternateSpelling)
    {
        var second = alternateSpelling ? HomographB with
        {
            AllomorphId = Guid.Parse(HomographB.AllomorphId!).ToString("B").ToUpperInvariant(),
            GrammaticalInfoId = Guid.Parse(HomographB.GrammaticalInfoId!).ToString("N").ToUpperInvariant(),
        } : HomographB;
        var row = Project([HomographA, HomographB], [HomographB, second]);
        Assert.Equal([2], row.DifferingPositions);
        var model = new WordRowViewModel(row);
        Assert.DoesNotContain("different morpheme identity", model.IdentityDetail);
        Assert.Contains("morpheme sequence differs", model.IdentityDetail);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void MissingIdentityIsExplainedAsMissingEvidence(string? missingId)
    {
        var model = new WordRowViewModel(Project([HomographA], [HomographA with { AllomorphId = missingId }]));
        Assert.Contains("identity not recorded", model.IdentityDetail);
        Assert.DoesNotContain("different morpheme identity", model.IdentityDetail);
    }

    [Fact]
    public void CanonicalGuidSpellingDoesNotCreateAnIdentityDifference()
    {
        var same = HomographA with { AllomorphId = Guid.Parse(HomographA.AllomorphId!).ToString("B").ToUpperInvariant() };
        var row = Project([HomographA], [same]);
        Assert.Equal(WordRowOutcome.Same, row.Outcome);
        Assert.False(new WordRowViewModel(row).HasIdentityDetail);
    }

    [Fact]
    public void IdenticalFormAndGlossStillExplainTheIdentityDifference()
    {
        var sameText = new ParserReadingMorph("tabu", "book", "n", null, false, null)
        { AllomorphId = "stored", GrammaticalInfoId = "noun" };
        var model = new WordRowViewModel(Project([sameText], [sameText with { AllomorphId = "parser" }]));
        Assert.Contains("Same form and gloss", model.IdentityDetail);
        Assert.Contains("different morpheme identity", model.IdentityDetail);
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var row = new RowView { Row = model };
            var window = new Window { Content = row, Width = 1240, Height = 400 };
            window.Show();
            try
            {
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var detail = row.GetVisualDescendants().OfType<CopyableTextBlock>()
                    .Single(block => block.Text == model.IdentityDetail);
                Assert.True(detail.Bounds.Width > row.Bounds.Width / 2,
                    "The identity explanation needs the row's width rather than a narrow morpheme column.");
            }
            finally { window.Close(); }
            return Task.CompletedTask;
        }, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void ReadTextKeepsTheWordsSharedComparison()
    {
        var comparison = new WordComparison(ProjectStanding.Approved, WordRowOutcome.Different,
            "approved-different", "Built something else", WordRowTone.Problem);
        var result = new AssessmentWordResult("word", "analysed", false, "Finished", 1, null)
        { Comparison = comparison, ProjectStanding = ProjectStanding.Approved };
        var token = new SIL.Motif.Commands.Queries.TextToken("word", "word", null, "unanalysed");
        var shown = new ResultsTokenViewModel("Text", 1, token, result);
        Assert.Same(comparison, shown.Comparison);
    }

    [Fact]
    public void CompactRowsShowBuiltAnywayInTheMatrixsProblemTone()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var row = new RowView
            {
                Columns = WordRowColumnSets.Timing,
                Row = new WordRowViewModel(new("walikula", WordRowOutcome.Same, "Built anyway", WordRowTone.Problem)
                { Opinion = ProjectStanding.Rejected, MeaningCode = "disapproved-built-anyway" }),
            };
            var window = new Window { Content = row, Width = 800, Height = 300 };
            window.Show();
            try
            {
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var chip = row.FindControl<MarkChip>("OutcomeAlone")!;
                Assert.Equal("Built anyway", chip.Text);
                Assert.Equal(Mark.Of(MeaningTone.Problem), chip.Mark);
            }
            finally { window.Close(); }
            return Task.CompletedTask;
        }, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void MixedOpinionHeadlineAndQualificationFitTheirMeaningColumn()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var comparison = new WordComparison(ProjectStanding.Candidate, WordRowOutcome.Different,
                "disapproved-rebuilt", "Rebuilt an analysis you Disapproved", WordRowTone.Problem)
            {
                Detail = "Your undecided analysis wasn't built",
            };
            var model = new WordRowViewModel(new("mixed", comparison.Outcome, comparison.Headline, comparison.Tone)
            {
                MeaningCode = comparison.MeaningCode, MeaningDetail = comparison.Detail, Comparison = comparison,
                Opinion = ProjectStanding.Candidate,
            });
            var row = new RowView { Row = model };
            var host = new StackPanel { Children = { row } };
            Grid.SetIsSharedSizeScope(host, true);
            var window = new Window { Content = host, Width = 1240, Height = 400, RequestedThemeVariant = ThemeVariant.Light };
            window.Show();
            try
            {
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var meaning = row.GetVisualDescendants().OfType<Control>()
                    .First(control => control.Classes.Contains("wordRowMeaning"));
                var headline = meaning.GetVisualDescendants().OfType<TextBlock>()
                    .Single(text => text.Text == comparison.Headline && text.IsEffectivelyVisible);
                var detail = meaning.GetVisualDescendants().OfType<TextBlock>()
                    .Single(text => text.Text == comparison.Detail);
                Assert.True(headline.IsEffectivelyVisible);
                Assert.True(detail.IsEffectivelyVisible);
                Assert.True(headline.Bounds.Width <= meaning.Bounds.Width,
                    $"Headline width {headline.Bounds.Width} exceeds column width {meaning.Bounds.Width}.");
                Assert.True(detail.Bounds.Width <= meaning.Bounds.Width);
            }
            finally { window.Close(); }
            return Task.CompletedTask;
        }, TimeSpan.FromMinutes(1));
    }
}

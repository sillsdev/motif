using Avalonia.Input;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class ReviewNumbersWordingTests
{
    private const string ProjectPath = @"C:\projects\one.fwdata";

    public static TheoryData<ReviewNumbersResponse, string> Cases => new()
    {
        { new(ReviewComparability.NoEarlierAssessment, 0, 0, 0, 2, 1, true),
            "1 of 2 words you changed kept their approved analyses. Nothing was checked before to compare with." },
        { new(ReviewComparability.NoEarlierAssessment, 0, 0, 0, 1, 1, true),
            "1 of 1 word you changed kept their approved analyses. Nothing was checked before to compare with." },
        { new(ReviewComparability.DifferentAssessor, 0, 0, 0, 1, 1, true),
            "1 of 1 word you changed kept their approved analyses. " +
            "The earlier numbers came from a different parser, so they cannot be compared with these." },
        { new(ReviewComparability.NoSharedWords, 0, 0, 0, 1, 0, true),
            "0 of 1 word you changed kept their approved analyses. " +
            "None of these words were checked before, so there is nothing to compare them with." },
        { new(ReviewComparability.Compared, 12, 9, 11, 3, 2, true),
            "Among 12 words also checked before, 9 → 11 kept their approved analyses." },
        { new(ReviewComparability.Compared, 1, 0, 1, 1, 1, false),
            "Among 1 word also checked before, 0 → 1 kept their approved analyses." },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task TheWindowWordsEachComparisonForALinguist(ReviewNumbersResponse numbers, string sentence)
    {
        var page = await MeasuredPage(numbers);

        Assert.Equal(sentence, page.NumbersText);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task TheReviewPageNeverNamesAssessmentsAssessorsOrRegressions(ReviewNumbersResponse numbers,
        string sentence)
    {
        Assert.NotNull(sentence);
        var page = await MeasuredPage(numbers);

        foreach (var text in new[] { page.NumbersText, page.MeasurementError, page.ApplyBlockReason })
        {
            Assert.DoesNotContain("Assessment", text ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Assessor", text ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("regression", text ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static async Task<ReviewPageModel> MeasuredPage(ReviewNumbersResponse numbers)
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one",
            [new PendingChange("change", "wordform", "first", "incorrect-spelling", null, "first", ["change"])],
            [new ChangeFit("change", true, [])]));
        fake.MeasurePendingCompletesWith(new MeasurePendingResult("job/one", "revision/one", numbers));
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(null, null, false));
        var selection = new SelectionViewModel(fake);
        var context = new WorkspaceContext(selection, new AssessViewModel(fake, selection),
            new ChangesViewModel(fake), fake, new FolderPicker(), new DragSource(), new BaselineViewModel(fake));
        var page = new ReviewPageModel(context);
        await context.OpenProjectAsync(ProjectPath);

        await page.MeasureCommand.ExecuteAsync(null);

        return page;
    }

    private sealed class FolderPicker : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class DragSource : IFileDragSource
    {
        public Task<DragDropEffects> StartDragAsync(PointerPressedEventArgs trigger, IReadOnlyList<string> filePaths,
            DragDropEffects allowedEffects) => Task.FromResult(allowedEffects);
    }
}

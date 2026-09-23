using System.Linq;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins <see cref="SelectionViewModel"/>: loading a project's current Baseline Texts, toggling Texts by
/// GUID, searching, pasted-word line splitting and whitespace handling, All wordforms, Retry failed,
/// Retry-slower-than validation, source counts in the summary, <see cref="SelectionViewModel.CanAssess"/>,
/// and that changing project resets every field.
/// </summary>
public sealed class SelectionViewModelTests
{
    private const string ProjectPath = @"C:\projects\one.fwdata";
    private const string OtherProjectPath = @"C:\projects\two.fwdata";

    private static readonly Guid AlphaId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid BetaId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    // A project switch or Refresh while a load is in flight must not let the older answer land last.
    [Fact]
    public async Task AnOlderTextLoadCompletingAfterANewerOneIsDiscarded()
    {
        var fake = new FakeCommandClient();
        var older = new TaskCompletionSource<CommandOutcome<TextInventoryResponse>>();
        var newer = new TaskCompletionSource<CommandOutcome<TextInventoryResponse>>();
        var calls = 0;
        fake.OnListTexts((_, _) => ++calls == 1 ? older.Task : newer.Task);
        var selection = new SelectionViewModel(fake);

        var first = selection.SetProjectAsync(ProjectPath);
        var second = selection.LoadTextsAsync(ProjectPath);
        newer.SetResult(CommandOutcome<TextInventoryResponse>.Success(
            new TextInventoryResponse([new TextChoiceSummary(AlphaId, "Alpha")], HasBaseline: true)));
        await second;
        older.SetResult(CommandOutcome<TextInventoryResponse>.Success(
            new TextInventoryResponse([], HasBaseline: false)));
        await first;

        Assert.Equal("Alpha", Assert.Single(selection.Texts).Title);
        Assert.Null(selection.TextsEmptyMessage);
    }

    // An empty box with no explanation is what stopped a real user choosing Texts at all.
    [Fact]
    public async Task WithNoBaselineTheEmptyTextListSaysToCaptureOneRatherThanShowingNothing()
    {
        var fake = new FakeCommandClient();
        fake.ListTextsCompletesWith(new TextInventoryResponse([], HasBaseline: false));

        var selection = new SelectionViewModel(fake);

        await selection.SetProjectAsync(ProjectPath);

        Assert.Empty(selection.Texts);
        Assert.Equal(
            "Capture a Baseline to choose Texts.", selection.TextsEmptyMessage);
    }

    [Fact]
    public async Task WithABaselineThatHasNoTextsTheMessageSaysThatInstead()
    {
        var fake = new FakeCommandClient();
        fake.ListTextsCompletesWith(new TextInventoryResponse([], HasBaseline: true));

        var selection = new SelectionViewModel(fake);

        await selection.SetProjectAsync(ProjectPath);

        Assert.Equal("This Baseline has no Texts.", selection.TextsEmptyMessage);
    }

    [Fact]
    public async Task WhenTextsLoadThereIsNoEmptyMessageAtAll()
    {
        var fake = new FakeCommandClient();
        fake.ListTextsCompletesWith(new TextInventoryResponse(
            [new TextChoiceSummary(Guid.NewGuid(), "Genesis")], HasBaseline: true));

        var selection = new SelectionViewModel(fake);

        await selection.SetProjectAsync(ProjectPath);

        Assert.Single(selection.Texts);
        Assert.Null(selection.TextsEmptyMessage);
    }

    [Fact]
    public void BeforeAnyProjectIsSetNothingIsSelectedAndAssessIsDisabled()
    {
        var viewModel = new SelectionViewModel(new FakeCommandClient());

        Assert.Empty(viewModel.Texts);
        Assert.False(viewModel.CanAssess);
        Assert.Equal("Nothing selected yet.", viewModel.SummaryText);
    }

    [Fact]
    public async Task SettingAProjectLoadsItsCurrentBaselineTextsByGuidAndTitle()
    {
        var fake = new FakeCommandClient();
        fake.ListTextsCompletesWith(new TextInventoryResponse(
            [new TextChoiceSummary(AlphaId, "Alpha"), new TextChoiceSummary(BetaId, "Beta")], HasBaseline: true));
        var viewModel = new SelectionViewModel(fake);

        await viewModel.SetProjectAsync(ProjectPath);

        Assert.Equal(2, viewModel.Texts.Count);
        Assert.Equal(AlphaId, viewModel.Texts[0].Id);
        Assert.Equal("Alpha", viewModel.Texts[0].Title);
        Assert.Equal(BetaId, viewModel.Texts[1].Id);
        Assert.Equal(ProjectPath, Assert.Single(fake.ListTextsRequests).ProjectPath);
    }

    [Fact]
    public async Task ARefusalWhileLoadingTextsShowsItsMessageAndLeavesTheListEmpty()
    {
        var fake = new FakeCommandClient();
        var refusal = new Refusal("project.busy", FailureReason.Busy, "The project is held by FieldWorks.");
        fake.ListTextsRefusesWith(refusal);
        var viewModel = new SelectionViewModel(fake);

        await viewModel.SetProjectAsync(ProjectPath);

        Assert.Empty(viewModel.Texts);
        Assert.Equal(refusal.Message, viewModel.RefusalMessage);
    }

    [Fact]
    public async Task CheckingATextAddsItsGuidToTheComposedRequestByIdentityNotTitle()
    {
        var fake = new FakeCommandClient();
        fake.ListTextsCompletesWith(new TextInventoryResponse(
            [new TextChoiceSummary(AlphaId, "Alpha"), new TextChoiceSummary(BetaId, "Beta")], HasBaseline: true));
        var viewModel = new SelectionViewModel(fake);
        await viewModel.SetProjectAsync(ProjectPath);

        viewModel.Texts.Single(text => text.Id == BetaId).IsChecked = true;

        Assert.Equal([BetaId], viewModel.ChosenTextIds);
        Assert.Equal([BetaId], viewModel.BuildRequest().TextIds);
    }

    [Fact]
    public async Task CheckingOrUncheckingATextRaisesChangeNotificationForChosenTextIds()
    {
        var fake = new FakeCommandClient();
        fake.ListTextsCompletesWith(new TextInventoryResponse([new TextChoiceSummary(AlphaId, "Alpha")], HasBaseline: true));
        var viewModel = new SelectionViewModel(fake);
        await viewModel.SetProjectAsync(ProjectPath);
        var raised = 0;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SelectionViewModel.ChosenTextIds)) raised++;
        };

        viewModel.Texts[0].IsChecked = true;
        viewModel.Texts[0].IsChecked = false;

        Assert.Equal(2, raised);
    }

    [Fact]
    public async Task SetTextCountsUpdatesTheMatchingTextAndClearCountsResetsEveryone()
    {
        var fake = new FakeCommandClient();
        fake.ListTextsCompletesWith(new TextInventoryResponse(
            [new TextChoiceSummary(AlphaId, "Alpha"), new TextChoiceSummary(BetaId, "Beta")], HasBaseline: true));
        var viewModel = new SelectionViewModel(fake);
        await viewModel.SetProjectAsync(ProjectPath);

        viewModel.SetTextCounts(AlphaId, occurrenceCount: 12, distinctWordCount: 5);

        Assert.Equal("12 words · 5 distinct", viewModel.Texts.Single(text => text.Id == AlphaId).CountsText);
        Assert.Equal(string.Empty, viewModel.Texts.Single(text => text.Id == BetaId).CountsText);

        viewModel.ClearTextCounts();

        Assert.Equal(string.Empty, viewModel.Texts.Single(text => text.Id == AlphaId).CountsText);
    }

    [Fact]
    public async Task SearchingFiltersTheDisplayedListButNeverUnchecksAHiddenText()
    {
        var fake = new FakeCommandClient();
        fake.ListTextsCompletesWith(new TextInventoryResponse(
            [new TextChoiceSummary(AlphaId, "Alpha"), new TextChoiceSummary(BetaId, "Beta")], HasBaseline: true));
        var viewModel = new SelectionViewModel(fake);
        await viewModel.SetProjectAsync(ProjectPath);
        viewModel.Texts.Single(text => text.Id == BetaId).IsChecked = true;

        viewModel.SearchText = "Alp";

        var visible = Assert.Single(viewModel.Texts);
        Assert.Equal(AlphaId, visible.Id);
        Assert.Equal([BetaId], viewModel.ChosenTextIds);
    }

    [Theory]
    [InlineData("one\ntwo\nthree", new[] { "one", "two", "three" })]
    [InlineData("one\r\ntwo\r\nthree", new[] { "one", "two", "three" })]
    [InlineData("  one  \n\n   \nfour", new[] { "one", "four" })]
    [InlineData("", new string[0])]
    [InlineData("   \n  \n", new string[0])]
    public void PastedWordsSplitsOnLinesTrimsAndDropsBlankLines(string pasted, string[] expected)
    {
        var viewModel = new SelectionViewModel(new FakeCommandClient()) { PastedWords = pasted };

        Assert.Equal(expected, viewModel.PastedWordEntries);
        Assert.Equal(expected, viewModel.BuildRequest().Words);
    }

    [Fact]
    public void AllWordformsAloneMakesTheSelectionNonemptyAndComposesTrue()
    {
        var viewModel = new SelectionViewModel(new FakeCommandClient()) { AllWordforms = true };

        Assert.True(viewModel.CanAssess);
        Assert.True(viewModel.BuildRequest().AllWordforms);
        Assert.Equal("all wordforms", viewModel.SummaryText);
    }

    [Fact]
    public void RetryFailedAloneMakesTheSelectionNonemptyAndComposesTrue()
    {
        var viewModel = new SelectionViewModel(new FakeCommandClient()) { RetryFailed = true };

        Assert.True(viewModel.CanAssess);
        Assert.True(viewModel.BuildRequest().RetryFailed);
        Assert.Equal("retry failed", viewModel.SummaryText);
    }

    [Fact]
    public void RetrySlowerThanMillisecondsComposesAStrictlyGreaterThanThreshold()
    {
        var viewModel = new SelectionViewModel(new FakeCommandClient()) { RetrySlowerThanMilliseconds = 500 };

        Assert.True(viewModel.CanAssess);
        Assert.Null(viewModel.ThresholdValidationMessage);
        Assert.Equal(TimeSpan.FromMilliseconds(500), viewModel.BuildRequest().RetrySlowerThan);
        Assert.Equal("retry slower than 500 ms", viewModel.SummaryText);
    }

    [Fact]
    public void ANegativeThresholdIsInvalidAndDisablesAssessEvenWithOtherSourcesChosen()
    {
        var viewModel = new SelectionViewModel(new FakeCommandClient())
        {
            AllWordforms = true,
            RetrySlowerThanMilliseconds = -1,
        };

        Assert.False(viewModel.CanAssess);
        Assert.NotNull(viewModel.ThresholdValidationMessage);
        Assert.Null(viewModel.BuildRequest().RetrySlowerThan);
    }

    [Fact]
    public void ZeroIsAValidThreshold()
    {
        var viewModel = new SelectionViewModel(new FakeCommandClient()) { RetrySlowerThanMilliseconds = 0 };

        Assert.True(viewModel.CanAssess);
        Assert.Null(viewModel.ThresholdValidationMessage);
        Assert.Equal(TimeSpan.Zero, viewModel.BuildRequest().RetrySlowerThan);
    }

    [Fact]
    public void CombiningMultipleSourcesCountsEachInTheSummary()
    {
        var viewModel = new SelectionViewModel(new FakeCommandClient())
        {
            AllWordforms = true,
            RetryFailed = true,
            PastedWords = "one\ntwo",
        };

        Assert.Equal("all wordforms, 2 pasted words, retry failed", viewModel.SummaryText);
    }

    [Fact]
    public async Task ChangingProjectResetsEveryFieldBeforeLoadingTheNewProjectsTexts()
    {
        var fake = new FakeCommandClient();
        fake.ListTextsCompletesWith(new TextInventoryResponse([new TextChoiceSummary(AlphaId, "Alpha")], HasBaseline: true));
        var viewModel = new SelectionViewModel(fake);
        await viewModel.SetProjectAsync(ProjectPath);
        viewModel.Texts[0].IsChecked = true;
        viewModel.PastedWords = "leftover";
        viewModel.AllWordforms = true;
        viewModel.RetryFailed = true;
        viewModel.RetrySlowerThanMilliseconds = 250;
        viewModel.SearchText = "Al";

        fake.ListTextsCompletesWith(new TextInventoryResponse([new TextChoiceSummary(BetaId, "Beta")], HasBaseline: true));
        await viewModel.SetProjectAsync(OtherProjectPath);

        Assert.Equal(string.Empty, viewModel.SearchText);
        Assert.Equal(string.Empty, viewModel.PastedWords);
        Assert.False(viewModel.AllWordforms);
        Assert.False(viewModel.RetryFailed);
        Assert.Null(viewModel.RetrySlowerThanMilliseconds);
        var text = Assert.Single(viewModel.Texts);
        Assert.Equal(BetaId, text.Id);
        Assert.False(text.IsChecked);
        Assert.Empty(viewModel.ChosenTextIds);
        Assert.Equal(OtherProjectPath, fake.ListTextsRequests[^1].ProjectPath);
    }
}

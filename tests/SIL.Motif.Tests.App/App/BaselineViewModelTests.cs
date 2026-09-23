using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins <see cref="BaselineViewModel"/>: the absent-Baseline state, the exact pinned freshness sentence,
/// the held/free notice, Refresh enablement, state updated only on success, refusal display, and
/// <see cref="BaselineViewModel.OfferRerun"/> firing only when an Assessment already covered the Baseline
/// a successful Refresh just replaced.
/// </summary>
public sealed class BaselineViewModelTests
{
    private const string Digest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string BundleDigest = "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string ProjectPath = @"C:\projects\one.fwdata";

    private static BaselineToken NewToken(string capturedUtc = "2026-09-05T00:00:00Z") =>
        new("project-1", Digest, "1", capturedUtc, BundleDigest);

    [Fact]
    public void BeforeAnyProjectIsSetThereIsNoBaselineAndRefreshIsDisabled()
    {
        var viewModel = new BaselineViewModel(new FakeCommandClient());

        Assert.False(viewModel.HasBaseline);
        Assert.Equal("No Baseline captured yet", viewModel.CapturedTimeText);
        Assert.False(viewModel.RefreshCommand.CanExecute(null));
    }

    [Fact]
    public async Task SettingAProjectWithNoBaselineYetShowsTheAbsentStateAndEnablesRefresh()
    {
        var fake = new FakeCommandClient();
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(null, null, false));
        var viewModel = new BaselineViewModel(fake);

        await viewModel.SetProjectAsync(ProjectPath);

        Assert.False(viewModel.HasBaseline);
        Assert.Equal("No Baseline captured yet", viewModel.CapturedTimeText);
        Assert.True(viewModel.RefreshCommand.CanExecute(null));
        Assert.Equal(ProjectPath, Assert.Single(fake.CurrentBaselineRequests).ProjectPath);
    }

    [Fact]
    public async Task SettingAProjectWithACapturedBaselineShowsItsTimeAndTheExactFreshnessSentence()
    {
        var fake = new FakeCommandClient();
        var savedUtc = new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero);
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(NewToken(), savedUtc, false));
        var viewModel = new BaselineViewModel(fake);

        await viewModel.SetProjectAsync(ProjectPath);

        Assert.True(viewModel.HasBaseline);
        Assert.Equal(savedUtc.ToLocalTime().ToString("f"), viewModel.CapturedTimeText);
        Assert.Equal("as of FieldWorks' last save", viewModel.FreshnessText);
        Assert.Equal("as of FieldWorks' last save", BaselineViewModel.FreshnessSentence);
    }

    [Theory]
    [InlineData(true, "FieldWorks holds this project open right now.")]
    [InlineData(false, "FieldWorks does not currently hold this project.")]
    public async Task TheHeldNoticeNamesWhetherFieldWorksHoldsTheProjectNow(bool held, string expected)
    {
        var fake = new FakeCommandClient();
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(null, null, held));
        var viewModel = new BaselineViewModel(fake);

        await viewModel.SetProjectAsync(ProjectPath);

        Assert.Equal(expected, viewModel.HeldStatusText);
    }

    [Fact]
    public async Task RefreshCommandUpdatesStateOnSuccess()
    {
        var fake = new FakeCommandClient();
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(null, null, false));
        var viewModel = new BaselineViewModel(fake);
        await viewModel.SetProjectAsync(ProjectPath);

        var capturedUtc = DateTimeOffset.UtcNow;
        var token = NewToken();
        fake.CaptureBaselineCompletesWith(new BaselineCaptureResponse(token, ProjectPath, capturedUtc, true, false));

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasBaseline);
        Assert.Same(token, viewModel.Token);
        Assert.Equal(capturedUtc, viewModel.SourceLastWriteUtc);
        Assert.True(viewModel.FieldWorksHeldProject);
        Assert.Null(viewModel.RefusalMessage);
        Assert.Equal(ProjectPath, Assert.Single(fake.CaptureBaselineRequests).ProjectPath);
    }

    [Fact]
    public async Task RefreshCommandLeavesStateUntouchedAndShowsTheRefusalOnRefusal()
    {
        var fake = new FakeCommandClient();
        var token = NewToken();
        var savedUtc = DateTimeOffset.UtcNow;
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(token, savedUtc, false));
        var viewModel = new BaselineViewModel(fake);
        await viewModel.SetProjectAsync(ProjectPath);

        var refusal = new Refusal("baseline.busy", FailureReason.Busy, "The project is held by FieldWorks.");
        fake.CaptureBaselineRefusesWith(refusal);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Same(token, viewModel.Token);
        Assert.Equal(savedUtc, viewModel.SourceLastWriteUtc);
        Assert.Equal(refusal.Message, viewModel.RefusalMessage);
    }

    [Fact]
    public async Task RefreshOffersARerunOnlyWhenAnAssessmentAlreadyCoveredTheReplacedBaseline()
    {
        var fake = new FakeCommandClient();
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(NewToken(), DateTimeOffset.UtcNow, false));
        var viewModel = new BaselineViewModel(fake);
        await viewModel.SetProjectAsync(ProjectPath);
        viewModel.HasAssessment = true;

        fake.CaptureBaselineCompletesWith(
            new BaselineCaptureResponse(NewToken("2026-09-06T00:00:00Z"), ProjectPath, DateTimeOffset.UtcNow, false, false));

        var offered = false;
        viewModel.OfferRerun += (_, _) => offered = true;
        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.True(offered);
        Assert.False(viewModel.HasAssessment);
    }

    [Fact]
    public async Task RefreshDoesNotOfferARerunWhenNoAssessmentCoveredTheProject()
    {
        var fake = new FakeCommandClient();
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(null, null, false));
        var viewModel = new BaselineViewModel(fake);
        await viewModel.SetProjectAsync(ProjectPath);

        fake.CaptureBaselineCompletesWith(
            new BaselineCaptureResponse(NewToken(), ProjectPath, DateTimeOffset.UtcNow, false, false));

        var offered = false;
        viewModel.OfferRerun += (_, _) => offered = true;
        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.False(offered);
    }
}

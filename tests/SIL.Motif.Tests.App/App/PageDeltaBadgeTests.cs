using SIL.Motif.App.ViewModels;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class PageDeltaBadgeTests
{
    [Fact]
    public void APageWithoutARecordedCountHasNoBadge()
    {
        var context = WorkspaceContextTests.NewContext(new FakeCommandClient());
        var model = new ProgressPageModel(context);
        var page = new PageViewModel(WorkspacePage.Progress, "Progress", "icon", model);

        Assert.False(page.HasBadge);
        Assert.Equal(string.Empty, page.Badge);
        Assert.Equal("Progress page", page.AutomationName);
    }

    [Fact]
    public void TheRailReflectsARealCountAndClearsItWhenUnavailable()
    {
        var context = WorkspaceContextTests.NewContext(new FakeCommandClient());
        var model = new ProposalsPageModel(context);
        var page = new PageViewModel(WorkspacePage.Proposals, "Proposals", "icon", model);

        model.BadgeCount = 2;
        Assert.True(page.HasBadge);
        Assert.Equal("2", page.Badge);
        Assert.Equal("Proposals page", page.AutomationName);

        model.BadgeCount = null;
        Assert.False(page.HasBadge);
        Assert.Equal(string.Empty, page.Badge);
    }
}

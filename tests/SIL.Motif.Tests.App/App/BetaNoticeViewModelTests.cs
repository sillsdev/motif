using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class BetaNoticeViewModelTests
{
    [Fact]
    public void GotItRemembersTheNoticeForTheNextWindow()
    {
        var preferences = new MemoryBetaNoticePreferences();
        var firstWindow = new BetaNoticeViewModel(preferences, new RecordingUriLauncher());

        Assert.True(firstWindow.IsVisible);

        firstWindow.GotItCommand.Execute(null);

        Assert.False(firstWindow.IsVisible);
        Assert.True(preferences.HasSeenBetaNotice);
        Assert.False(new BetaNoticeViewModel(preferences, new RecordingUriLauncher()).IsVisible);
    }

    private sealed class MemoryBetaNoticePreferences : IBetaNoticePreferences
    {
        public bool HasSeenBetaNotice { get; private set; }

        public void MarkBetaNoticeSeen() => HasSeenBetaNotice = true;
    }

    private sealed class RecordingUriLauncher : IUriLauncher
    {
        public Task<bool> LaunchAsync(Uri uri, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}

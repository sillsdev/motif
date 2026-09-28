using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class TechDemoNoticeViewModelTests
{
    [Fact]
    public void GotItRemembersTheNoticeForTheNextWindow()
    {
        var preferences = new MemoryTechDemoNoticePreferences();
        var firstWindow = new TechDemoNoticeViewModel(preferences, new RecordingUriLauncher());

        Assert.Equal(
            "Motif is a tech demo. It reads your project and writes nothing until you press Apply. Keep a FieldWorks backup, and tell us what goes wrong.",
            TechDemoNoticeViewModel.NoticeText);
        Assert.True(firstWindow.IsVisible);

        firstWindow.GotItCommand.Execute(null);

        Assert.False(firstWindow.IsVisible);
        Assert.True(preferences.HasSeenTechDemoNotice);
        Assert.False(new TechDemoNoticeViewModel(preferences, new RecordingUriLauncher()).IsVisible);
    }

    private sealed class MemoryTechDemoNoticePreferences : ITechDemoNoticePreferences
    {
        public bool HasSeenTechDemoNotice { get; private set; }

        public void MarkTechDemoNoticeSeen() => HasSeenTechDemoNotice = true;
    }

    private sealed class RecordingUriLauncher : IUriLauncher
    {
        public Task<bool> LaunchAsync(Uri uri, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}

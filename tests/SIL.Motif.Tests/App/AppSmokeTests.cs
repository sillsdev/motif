using Avalonia;
using SIL.Motif.App;
using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Constructs the application and its main window under Avalonia's headless platform, with no
/// dispatcher loop ever started (<see cref="AvaloniaHeadlessFixture"/> uses <c>SetupWithoutStarting</c>).
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class AppSmokeTests
{
    public AppSmokeTests(AvaloniaHeadlessFixture fixture)
    {
        _ = fixture;
    }

    [Fact]
    public void MainWindowCanBeConstructedHeadlessly()
    {
        Assert.IsType<SIL.Motif.App.App>(Application.Current);

        var window = new MainWindow();

        Assert.NotNull(window);
        Assert.Equal("Motif", window.Title);
    }
}

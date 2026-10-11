using Avalonia.Controls;
using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>The Setup dialog is sized to the window before it is ever shown, so it never opens wider than the window.</summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class SetupDialogFitTests(AvaloniaHeadlessFixture avalonia)
{
    [Fact]
    public void FittingADialogThatWasNeverMeasuredNarrowsItsSurface()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var dialog = new SetupDialog();

            dialog.FitToAvailableArea(661, 480);

            var surface = dialog.FindControl<Border>("DialogSurface");
            Assert.NotNull(surface);
            Assert.Equal(661, surface.MaxWidth);
            Assert.Equal(480, surface.MaxHeight);
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(30));
    }
}

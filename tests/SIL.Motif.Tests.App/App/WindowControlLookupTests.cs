using Avalonia.Automation;
using Avalonia.Controls;
using SIL.Motif.App;
using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>Positive control for the lookup that several tests use to claim "no Choose what to parse button outside the card".</summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class WindowControlLookupTests(AvaloniaHeadlessFixture avalonia)
{
    private static Button ChooseButton(bool visible = true)
    {
        var button = new Button { IsVisible = visible };
        AutomationProperties.SetName(button, MotifAppStrings.ChooseWhatToParseAccessibleName);
        return button;
    }

    [Fact]
    public void TheChooseWhatToParseLookupReturnsTheVisibleButtonOutsideTheCardAndSkipsTheOthers()
    {
        avalonia.Invoke(() =>
        {
            var outside = ChooseButton();
            var hidden = ChooseButton(visible: false);
            var unrelated = new Button();
            AutomationProperties.SetName(unrelated, "Refresh the project");
            var inCard = new ProjectCardView { Content = ChooseButton() };
            var panel = new StackPanel();
            panel.Children.Add(outside);
            panel.Children.Add(hidden);
            panel.Children.Add(unrelated);
            panel.Children.Add(inCard);
            var window = new MotifTestWindow { Content = panel, Width = 600, Height = 400 };
            try
            {
                window.Show();
                window.UpdateLayout();
                Assert.Same(outside, Assert.Single(window.VisibleChooseWhatToParseButtonsOutsideProjectCard()));
            }
            finally { window.Close(); }
        });
    }
}

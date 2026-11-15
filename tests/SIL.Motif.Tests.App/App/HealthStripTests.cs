using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views.Shell;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class HealthStripTests(AvaloniaHeadlessFixture avalonia)
{
    [Fact]
    public void HoverAndKeyboardFocusOpenTheSameFlyout()
    {
        avalonia.Invoke(() =>
        {
            var context = WorkspaceContextTests.NewContext(new FakeCommandClient());
            var preferences = new MemoryUserPreferencesStore();
            context.UseDiagnosticsPreferences(preferences);
            var strip = new HealthStrip(new HealthStripViewModel(context));
            var other = new Button { Content = "Other" };
            var window = new MotifTestWindow
            {
                Content = new StackPanel { Children = { other, strip } }, Width = 400, Height = 200,
            };
            try
            {
                window.Show();
                window.UpdateLayout();
                var button = strip.FindControl<Button>("HealthButton")!;
                var flyout = button.Flyout!;
                var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
                window.MouseMove(point);
                Dispatcher.UIThread.RunJobs();
                Assert.True(flyout.IsOpen);
                window.MouseMove(new Point(390, 190));
                Dispatcher.UIThread.RunJobs();
                Assert.False(flyout.IsOpen);
                Assert.True(other.Focus(NavigationMethod.Tab));
                Assert.True(button.Focus(NavigationMethod.Tab));
                Dispatcher.UIThread.RunJobs();
                Assert.Same(flyout, button.Flyout);
                Assert.True(flyout.IsOpen);
                var content = Assert.IsType<StackPanel>(Assert.IsType<Flyout>(flyout).Content);
                var action = Assert.Single(content.Children.OfType<Button>());
                Assert.True(action.Focus(NavigationMethod.Tab));
                action.Command!.Execute(null);
                Assert.True(preferences.Current.Diagnostics);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void MissingMeasurementsStayMissingAndTurningOnDiagnosticsPersists()
    {
        var fake = new FakeCommandClient();
        var context = WorkspaceContextTests.NewContext(fake);
        var preferences = new MemoryUserPreferencesStore();
        context.UseDiagnosticsPreferences(preferences);
        var health = new HealthStripViewModel(context);
        var overview = SampleOverview.Populated();
        health.Overview = overview with
        {
            Warnings = null,
            Timing = overview.Timing with { Work = WordWorkSummary.None },
        };

        Assert.False(health.HasWarnings);
        Assert.False(health.HasMedianWork);
        Assert.Equal(string.Empty, health.WarningText);
        Assert.Equal(string.Empty, health.MedianWorkText);
        health.TurnOnDiagnosticsCommand.Execute(null);
        Assert.True(preferences.Current.Diagnostics);
        Assert.False(health.CanTurnOnDiagnostics);
        Assert.Empty(fake.TimingRequests);
        Assert.Empty(fake.AssessRequests);
        Assert.Empty(fake.CheckGrammarRequests);
    }

    [Fact]
    public void RecordedMedianIsOnlyInTheDetailsText()
    {
        var context = WorkspaceContextTests.NewContext(new FakeCommandClient());
        var health = new HealthStripViewModel(context) { Overview = SampleOverview.Populated() };

        Assert.True(health.HasMedianWork);
        Assert.Contains("64", health.MedianWorkText);
        Assert.DoesNotContain("64", health.AccessibleName);
        Assert.DoesNotContain("parser", health.AccessibleName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OutdatedMeasurementsAreNotPresentedAsCurrent()
    {
        var context = WorkspaceContextTests.NewContext(new FakeCommandClient());
        var health = new HealthStripViewModel(context)
        {
            Overview = SampleOverview.Populated() with { IsStale = true },
        };

        Assert.False(health.HasWarnings);
        Assert.False(health.HasMedianWork);
    }

    [Fact]
    public void ADelayedOverviewCannotRestoreThePreviousProjectsWritingSystems()
    {
        avalonia.Invoke(() =>
        {
            var context = WorkspaceContextTests.NewContext(new FakeCommandClient());
            var health = new HealthStripViewModel(context);
            var sample = SampleOverview.Populated();
            WritingSystemDisplay System(string id) => new(id, id, id, WritingSystemKind.Vernacular, 0, true,
                "Charis SIL", string.Empty, false, new Dictionary<string, double>());
            var previous = sample with { WritingSystems = [System("old")] };
            var current = sample with { WritingSystems = [System("new")] };

            Task.Run(() => health.Overview = previous).GetAwaiter().GetResult();
            health.Overview = current;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("new", context.TextStyles.DefaultVernacularId);
        });
    }
}

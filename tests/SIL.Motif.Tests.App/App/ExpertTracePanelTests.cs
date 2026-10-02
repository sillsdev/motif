using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ExpertTracePanelTests
{
    [Theory]
    [InlineData("{\"left\":\"word_edge\",\"right\":\"#\"}", "{\"left\":\"word_edge\",\"right\":\"#\"}")]
    [InlineData("\"[raw environment]\"", "[raw environment]")]
    public void UnqualifiedEnvironmentOperandsHaveNoAuthoredMeaningsInThePanel(string operandJson, string expectedRaw)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var trace = TraceWordViewModel.FromDiagnosticJson(ExpertTraceReadingTests.EnvironmentOperandDiagnostic(operandJson));
            trace.SelectedStep = trace.Root!;
            var panel = new ExpertTracePanel { DataContext = trace };
            var window = new Window { Content = panel, Width = 1040, Height = 1500 };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                var section = panel.GetVisualDescendants().OfType<Expander>().Single(expander =>
                    AutomationProperties.GetName(expander) == "Expert environment notation");
                section.IsExpanded = true;
                PageScreenshots.Settle(window);
                var tokens = panel.GetVisualDescendants().OfType<TraceNotationToken>().Where(control =>
                    control.IsEffectivelyVisible && control.DataContext is EnvironmentToken).ToArray();
                var token = Assert.Single(tokens);
                Assert.Equal(expectedRaw, Assert.IsType<EnvironmentToken>(token.DataContext).Raw);
                Assert.Equal("Authored environment notation unavailable", ToolTip.GetTip(token));
                var text = panel.GetVisualDescendants().OfType<TextBlock>().Where(block => block.IsEffectivelyVisible)
                    .Select(block => block.Text).ToArray();
                Assert.Contains(expectedRaw, text);
                Assert.Contains("Authored environment notation unavailable", text);
                Assert.DoesNotContain("Target position", text);
                Assert.DoesNotContain("Word edge", text);
                Assert.DoesNotContain("Natural class raw environment; membership is project-defined", text);
            }
            finally { window.Close(); }
            return Task.CompletedTask;
        }, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void TheModeSwitchKeepsTheSelectedOccurrenceAndOpenInspector()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(configure: (fake, _) =>
                fake.TraceWordCompletesWith(PageScreenshots.TraceWithIdentities()));
            try
            {
                window.Width = 1240;
                window.Height = 1600;
                workspace.CurrentPage = WorkspacePage.TryAWord;
                workspace.Context.TryWord("matinlu");
                await workspace.Assess.Trace.TryCommand.ExecutionTask!;
                PageScreenshots.Settle(window);
                var trace = workspace.Assess.Trace;
                trace.Result = TraceWordViewModel.FromDiagnosticJson(ExpertTraceReadingTests.NotationDiagnostic).Result;
                trace.SelectedCandidate = trace.Candidates[0];
                var attempt = trace.SelectedCandidate;
                var selected = FindInspectable(trace.RecordedRoots[0]);
                trace.SelectedStep = selected;
                workspace.Context.OpenInspector(selected.InspectSubject!, trace: trace.InspectorTrace, captured: selected.Captured);
                await workspace.Inspector.Loading;
                var subject = workspace.Inspector.Title;
                var expert = Assert.Single(window.GetVisualDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "Expert trace view");
                expert.Command!.Execute(expert.CommandParameter);
                PageScreenshots.Settle(window);
                var picker = Assert.Single(window.GetVisualDescendants().OfType<ComboBox>(), box =>
                    AutomationProperties.GetName(box) == "Expert attempt");
                Assert.Equal(trace.Candidates.Count, picker.Items.Count);
                Assert.Same(attempt, picker.SelectedItem);
                Assert.Same(attempt, trace.SelectedCandidate);
                Assert.Equal(selected.RecordedStep.StepId, trace.SelectedStep!.RecordedStep.StepId);
                Assert.True(workspace.Inspector.IsOpen);
                Assert.Equal(subject, workspace.Inspector.Title);
                Assert.Contains(window.GetVisualDescendants().OfType<Control>(), control =>
                    AutomationProperties.GetName(control) == "Expert trace events" && control.IsEffectivelyVisible);
                var plain = Assert.Single(window.GetVisualDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "Plain trace view");
                plain.Command!.Execute(plain.CommandParameter);
                PageScreenshots.Settle(window);
                Assert.Equal(selected.RecordedStep.StepId, trace.SelectedStep!.RecordedStep.StepId);
                Assert.True(workspace.Inspector.IsOpen);
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(1));
    }
    [Fact]
    public void OpeningUnrecordedExpertSectionsShowsTheirAvailability()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var trace = TraceWordViewModel.FromDiagnosticJson(TraceEnvelope.Of("", """
                {"type":"WordAnalysis","children":[{"type":"Failed","children":[]}]}
                """));
            trace.SelectedStep = trace.Root!.Children[0];
            var panel = new ExpertTracePanel { DataContext = trace };
            var window = new Window { Content = panel, Width = 1040, Height = 1500 };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                foreach (var section in panel.GetVisualDescendants().OfType<Expander>()) section.IsExpanded = true;
                PageScreenshots.Settle(window);
                var text = panel.GetVisualDescendants().OfType<TextBlock>().Where(block => block.IsEffectivelyVisible)
                    .Select(block => block.Text).ToArray();
                Assert.Contains("No phonological events recorded in this view", text);
                Assert.Contains("Environment not recorded", text);
            }
            finally { window.Close(); }
            return Task.CompletedTask;
        }, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void TheParserEventTokenExplainsItselfOnKeyboardFocus()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var trace = TraceWordViewModel.FromDiagnosticJson(ExpertTraceReadingTests.NotationDiagnostic);
            var panel = new ExpertTracePanel { DataContext = trace };
            var window = new Window { Content = panel, Width = 1040, Height = 1500 };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                var token = panel.GetVisualDescendants().OfType<Button>().Single(button =>
                    AutomationProperties.GetName(button) == "Recorded event: 0.0.0.0");
                Assert.True(token.Focus(NavigationMethod.Tab));
                PageScreenshots.Settle(window);
                Assert.True(ToolTip.GetIsOpen(token));
                Assert.NotNull(ToolTip.GetTip(token));
                var tip = window.GetVisualDescendants().OfType<ToolTip>().Single(control => control.IsEffectivelyVisible);
                var tipArea = new Rect(tip.TranslatePoint(default, window)!.Value, tip.Bounds.Size);
                var tokenArea = new Rect(token.TranslatePoint(default, window)!.Value, token.Bounds.Size);
                Assert.True(tipArea.Bottom >= tokenArea.Top - 64 && tipArea.Top <= tokenArea.Bottom + 64,
                    $"The explanation at {tipArea} must stay beside the focused event at {tokenArea}.");
            }
            finally { window.Close(); }
            return Task.CompletedTask;
        }, TimeSpan.FromMinutes(1));
    }

    private static TraceStepViewModel FindInspectable(TraceStepViewModel root) => root.CanInspect ? root
        : root.Children.Select(FindInspectableOrNull).First(step => step is not null)!;

    private static TraceStepViewModel? FindInspectableOrNull(TraceStepViewModel root) => root.CanInspect ? root
        : root.Children.Select(FindInspectableOrNull).FirstOrDefault(step => step is not null);
}

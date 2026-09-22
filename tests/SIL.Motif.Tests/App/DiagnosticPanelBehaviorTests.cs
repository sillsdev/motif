using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class DiagnosticPanelBehaviorTests
{
    [Fact]
    public void SharedWritingSystemAndUnavailableMsaRemainDisplayable()
    {
        var morph = new TraceMorph("id", "word", null, null, null, null, null, null, null, null)
        {
            RawJson = "{\"msa\":null}", FormWritingSystem = "ar",
        };
        var model = new TraceWordViewModel
        {
            Result = new WordTraceResponse("word", true, true, null, 1, null, 1, [],
                new TraceStep("WordAnalysis", null, null, null, null, []))
            {
                Analyses = [new TraceAnalysis("analysis-0", 0, "word", "recorded", [morph])],
                HostCapture = new TraceHostCapture(null, null, null, null, null, null,
                    [new TraceWritingSystem("ar", "Arabic", true, true, "rtl", null),
                     new TraceWritingSystem("ar", "Arabic", false, false, "rtl", null)]),
            },
        };
        var display = Assert.Single(Assert.Single(model.Analyses).Morphs);
        Assert.Equal(Avalonia.Media.FlowDirection.RightToLeft, display.FormDirection);
        Assert.Contains("not recorded", display.MsaDetails);
    }
    private readonly AvaloniaHeadlessFixture _avalonia;
    public DiagnosticPanelBehaviorTests(AvaloniaHeadlessFixture avalonia) => _avalonia = avalonia;

    [Fact]
    public void MorphSearchOpensActualAncestorContainersAndRetainsFailureOperands()
    {
        _avalonia.Invoke(() =>
        {
            var leaf = new TraceStep("Failed", "rule", "surface", null, "RequiredSyntacticFeatureStruct", [])
            {
                OutcomeStatus = "failed", FailureRequired = "past", FailureActual = "present",
                AttemptedMorphs = [new TraceMorph("form-1", "needle", "headword", "gloss", "verb", null, null, null, null, null)],
            };
            var branch = new TraceStep("MorphologicalRuleSynthesis", "rule", "surface", null, null, [leaf]);
            var root = new TraceStep("WordAnalysis", null, null, null, null, [branch]);
            var model = new TraceWordViewModel
            {
                Result = new WordTraceResponse("word", false, true, null, 3, null, 1, [], root),
                MorphFilter = "needle",
            };
            var panel = new DiagnosticPanel(model);
            var window = new Window { Content = panel, Width = 1000, Height = 800 };
            try
            {
                window.Show();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.Single(model.FilteredRoots);
                var tree = panel.FindControl<TreeView>("TreeHost")!;
                var container = Assert.IsType<TreeViewItem>(tree.ContainerFromIndex(0));
                Assert.True(container.IsExpanded);
                var branchContainer = Assert.IsType<TreeViewItem>(container.ContainerFromIndex(0));
                Assert.True(branchContainer.IsExpanded);
                var selected = model.FilteredRoots[0].Children[0].Children[0];
                model.SelectedStep = selected;
                Assert.Contains("past", selected.ContextText);
                Assert.Contains("present", selected.ContextText);
                Assert.Equal("needle", Assert.Single(selected.AttemptedMorphs).Form);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void NarrowPanelStacksSelectedDetailsBelowTheTree()
    {
        _avalonia.Invoke(() =>
        {
            var model = new TraceWordViewModel
            {
                Result = new WordTraceResponse("word", false, true, null, 1, null, 1, [],
                    new TraceStep("WordAnalysis", null, null, null, null, [])),
            };
            model.SelectedStep = model.FilteredRoots[0];
            var panel = new DiagnosticPanel(model);
            var window = new Window { Content = panel, Width = 600, Height = 800 };
            try
            {
                window.Show();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var details = panel.FindControl<Border>("DetailHost")!;
                Assert.Equal(0, Grid.GetColumn(details));
                Assert.Equal(2, Grid.GetRow(details));
            }
            finally { window.Close(); }
        });
    }
}

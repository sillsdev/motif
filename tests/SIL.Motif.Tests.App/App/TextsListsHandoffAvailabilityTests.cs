using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class TextsListsHandoffAvailabilityTests
{
    [Fact]
    public void EmptyAndUntickedListsShowOnlyTheirRelevantHandoffReason()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var (workspace, window) = FakeComposedWindow.Create();
            try
            {
                window.Show();
                window.ApplyTemplate();
                window.UpdateLayout();

                var texts = window.GetLogicalDescendants().OfType<ListBoxItem>().Single(item =>
                    AutomationProperties.GetName(item) == "Texts page");
                HeadlessClick.Click(window, texts, "Texts page");
                Click(window, "Lists tab");

                var lists = workspace.PageModel<TextsPageModel>().TextsLists;
                var emptyListReason = "No words in this list to send to AI Handoff.";
                var wholeListButton = FindButton(window, "AI Handoff for the whole selected list");
                var checkedWordsButton = FindButton(window, "AI Handoff for ticked words in the selected list");
                Assert.False(wholeListButton.IsEffectivelyEnabled);
                Assert.False(checkedWordsButton.IsEffectivelyEnabled);
                Assert.Equal(emptyListReason, lists.HandOffListDisabledReason);
                Assert.Equal("This word list has no words to tick.",
                    AutomationProperties.GetHelpText(checkedWordsButton));
                Assert.Equal([emptyListReason], VisibleReasons(window));

                var words = workspace.Context.Assess.Words;
                words.Load([new AssessmentWordResult(
                    "approved-empty", "no-analysis", false, "Search completed", 10, null)
                {
                    ProjectStanding = ProjectStanding.Approved,
                    OccurrenceCount = 1,
                }]);
                workspace.Context.Assess.Compare.Load(words.AllRows);
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                const string untickedWordsReason = "Tick words in this list before starting an AI Handoff.";
                Assert.True(wholeListButton.IsEffectivelyEnabled);
                Assert.False(checkedWordsButton.IsEffectivelyEnabled);
                Assert.Empty(lists.HandOffListDisabledReason);
                Assert.Equal(untickedWordsReason, lists.HandOffCheckedWordsDisabledReason);
                Assert.Equal(untickedWordsReason, AutomationProperties.GetHelpText(checkedWordsButton));
                Assert.Equal([untickedWordsReason], VisibleReasons(window));
            }
            finally
            {
                window.Close();
                workspace.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }

            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(10));
    }

    private static IReadOnlyList<string?> VisibleReasons(MainWindow window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var reasons = new HashSet<string>
        {
            "Choose a word list first.",
            "No words in this list to send to AI Handoff.",
            "Tick words in this list before starting an AI Handoff.",
        };
        return window.GetVisualDescendants().OfType<TextBlock>()
            .Where(text => text.IsEffectivelyVisible && text.Text is { } value && reasons.Contains(value))
            .Select(text => text.Text).ToArray();
    }

    private static void Click(MainWindow window, string name)
    {
        var button = window.GetLogicalDescendants().OfType<Button>().Single(control =>
            string.Equals(AutomationProperties.GetName(control), name, StringComparison.Ordinal));
        HeadlessClick.Click(window, button, name);
    }

    private static Button FindButton(MainWindow window, string name) =>
        window.GetLogicalDescendants().OfType<Button>().Single(control =>
            string.Equals(AutomationProperties.GetName(control), name, StringComparison.Ordinal));

}

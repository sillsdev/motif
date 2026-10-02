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
    public void EmptyAndUntickedListsGiveTheirHandoffReasonOnTheButton_NotAsANoteBesideIt()
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
                FakeComposedWindow.Click(window, "Lists tab");

                var lists = workspace.PageModel<TextsPageModel>().TextsLists;
                var emptyListReason = "No words in this list to send to AI Handoff.";
                var wholeListButton = window.GetVisualDescendants().OfType<Button>()
                    .Single(button => ReferenceEquals(button.Command, lists.HandOffListCommand));
                var checkedWordsButton = window.GetVisualDescendants().OfType<Button>()
                    .Single(button => ReferenceEquals(button.Command, lists.HandOffCheckedWordsCommand));
                Assert.False(wholeListButton.IsEffectivelyEnabled);
                Assert.False(checkedWordsButton.IsEffectivelyEnabled);
                Assert.Equal(emptyListReason, lists.HandOffListDisabledReason);
                Assert.Equal("This word list has no words to tick.",
                    AutomationProperties.GetHelpText(checkedWordsButton));
                Assert.Equal(emptyListReason, ToolTip.GetTip(wholeListButton));
                Assert.Equal(emptyListReason, AutomationProperties.GetHelpText(wholeListButton));
                Assert.Empty(VisibleReasons(window));

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

                const string untickedWordsReason = "Tick words first.";
                Assert.True(wholeListButton.IsEffectivelyEnabled);
                Assert.False(checkedWordsButton.IsEffectivelyEnabled);
                Assert.Empty(lists.HandOffListDisabledReason);
                Assert.Equal(untickedWordsReason, lists.HandOffCheckedWordsDisabledReason);
                Assert.Equal(untickedWordsReason, AutomationProperties.GetHelpText(checkedWordsButton));
                Assert.Equal(untickedWordsReason, ToolTip.GetTip(checkedWordsButton));
                Assert.Empty(VisibleReasons(window));
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
            "Tick words first.",
        };
        return window.GetVisualDescendants().OfType<TextBlock>()
            .Where(text => text.IsEffectivelyVisible && text.Text is { } value && reasons.Contains(value))
            .Select(text => text.Text).ToArray();
    }

}

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

/// <summary>
/// Drives the composed window through its scripted clipboard and diagnostic file dialogs: opening a saved
/// diagnostic from Try a Word, saving and copying its JSON, and copying the AI Handoff's starter prompt.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class DesktopSeamWalkthroughTests
{
    [Fact]
    public void TheStarterPromptAndAQuestionAreCopiedExactly()
    {
        var clipboard = new RecordingClipboard();
        const string starterPrompt = "Read handoff.md first, then answer from the files alone.";
        const string question = "Which words did not parse, and what do they have in common?";

        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            using var walkthrough = new WalkthroughWindow(Path.GetTempPath(), "unused.fwdata", clipboard: clipboard);
            var handoff = walkthrough.Workspace.PageModel<SettingsPageModel>().Handoff.Handoff;
            handoff.Files.Add(new HandoffFileViewModel("handoff.md", @"C:\handoff\handoff.md"));
            handoff.OutputDirectory = @"C:\handoff";
            handoff.PastedHeader = starterPrompt;
            handoff.State = RunState.Completed;
            walkthrough.Show();
            walkthrough.SkipSetup();
            walkthrough.ShowPage(WorkspacePage.Settings);
            InteractiveControlSweep.AssertScene(walkthrough, "completed Handoff",
                InteractiveControlFamily.Action,
                InteractiveControlFamily.Link,
                InteractiveControlFamily.Disclosure,
                InteractiveControlFamily.List,
                InteractiveControlFamily.SelectableText,
                InteractiveControlFamily.ContentSurface,
                InteractiveControlFamily.Collection,
                InteractiveControlFamily.Container);

            walkthrough.Click("Copy the starter prompt");
            walkthrough.Click(question);
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(30));

        Assert.Equal([starterPrompt, question], clipboard.Copied);
    }

}

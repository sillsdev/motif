using Avalonia.Automation;
using Avalonia.Controls;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

internal static class WalkthroughWindowReopen
{
    public static void OpenRecentProjectByClick(this WalkthroughWindow walkthrough, string projectPath)
    {
        ArgumentNullException.ThrowIfNull(walkthrough);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);

        walkthrough.LoadKnownProjects();
        var project = walkthrough.Workspace.RecentProjects.Single(known =>
            string.Equals(known.FullFwDataPath, projectPath, StringComparison.OrdinalIgnoreCase));
        walkthrough.OpenProjectMenu();
        var openRecent = walkthrough.FindProjectMenuEntry<Button>("Open a recent project");
        Assert.True(openRecent.IsEffectivelyEnabled, "'Open a recent project' is not effectively enabled.");
        HeadlessClick.Click(walkthrough.Window, openRecent, "Open a recent project");
        var item = walkthrough.Window.RecentProjectItems.Single(candidate =>
            string.Equals(AutomationProperties.GetName(candidate), project.AutomationName, StringComparison.Ordinal));
        Assert.True(item.IsEffectivelyEnabled, $"'{project.AutomationName}' is not effectively enabled.");
        HeadlessClick.Click(walkthrough.Window, item, project.AutomationName);
        walkthrough.WaitUntil(
            () => walkthrough.Workspace.OpenRecentProjectCommand.ExecutionTask is { IsCompleted: true },
            TimeSpan.FromSeconds(60), $"opening '{project.AutomationName}' did not finish");
    }
}

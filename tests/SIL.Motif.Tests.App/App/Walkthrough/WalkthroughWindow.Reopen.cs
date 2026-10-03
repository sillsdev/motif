using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

internal static class WalkthroughWindowReopen
{
    public static void OpenRecentProjectByClick(this WalkthroughWindow walkthrough, string projectPath)
    {
        ArgumentNullException.ThrowIfNull(walkthrough);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);

        walkthrough.OpenProjectMenu();
        var automationName = $"Open {Path.GetFileNameWithoutExtension(projectPath)}";
        walkthrough.WaitUntil(() => walkthrough.Workspace.RecentProjects.Any(candidate =>
                string.Equals(candidate.AutomationName, automationName, StringComparison.Ordinal)),
            TimeSpan.FromSeconds(10), $"'{automationName}' did not appear under Open recent");
        var project = walkthrough.Workspace.RecentProjects.Single(known =>
            string.Equals(known.FullFwDataPath, projectPath, StringComparison.OrdinalIgnoreCase));
        var openRecent = walkthrough.FindProjectMenuEntry<Button>("Open a recent project");
        var recentMenu = Assert.IsType<MenuFlyout>(openRecent.Flyout);
        HeadlessClick.Click(TopLevel.GetTopLevel(openRecent)!, openRecent, "Open a recent project");
        Assert.True(recentMenu.IsOpen, "Clicking 'Open a recent project' did not open its list.");
        while (!walkthrough.Window.RecentProjectItems.Any(candidate => ReferenceEquals(candidate.CommandParameter, project)))
        {
            var next = Assert.Single(recentMenu.Items.OfType<MenuItem>(), candidate =>
                candidate.Header is string header && header.StartsWith("Show ", StringComparison.Ordinal));
            next.BringIntoView();
            Dispatcher.UIThread.RunJobs();
            HeadlessClick.Click(TopLevel.GetTopLevel(next)!, next, next.Header!.ToString()!);
            Dispatcher.UIThread.RunJobs();
        }
        var item = walkthrough.Window.RecentProjectItems.Single(candidate =>
            string.Equals(AutomationProperties.GetName(candidate), project.AutomationName, StringComparison.Ordinal));
        var command = walkthrough.Workspace.OpenRecentProjectCommand;
        Assert.Same(command, item.Command);
        Assert.Same(project, item.CommandParameter);
        var before = command.ExecutionTask;
        HeadlessClick.Click(TopLevel.GetTopLevel(item)!, item, project.AutomationName);
        walkthrough.WaitUntil(() => !recentMenu.IsOpen &&
                !ReferenceEquals(before, command.ExecutionTask) && command.ExecutionTask is { IsCompleted: true },
            TimeSpan.FromSeconds(60), $"opening '{project.AutomationName}' did not finish");
        Assert.NotSame(before, command.ExecutionTask);
        Assert.Equal(projectPath, walkthrough.Workspace.Context.ProjectPath);
    }
}

using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App;

public static class AutomationIds
{
    public const string ProjectMenu = "motif-project-menu";
    public const string SelectNewProject = "motif-select-new-project";
    public const string RefreshProject = "motif-refresh-project";
    public const string SkipSetup = "motif-skip-setup";
    public const string OverviewSelectionWordCount = "motif-overview-selection-word-count";
    public const string Pages = "motif-pages";

    public static string ForPage(WorkspacePage page) => page switch
    {
        WorkspacePage.Overview => "motif-page-overview",
        WorkspacePage.Texts => "motif-page-texts",
        WorkspacePage.TryAWord => "motif-page-try-a-word",
        WorkspacePage.Timing => "motif-page-timing",
        WorkspacePage.Warnings => "motif-page-warnings",
        WorkspacePage.Review => "motif-page-review",
        WorkspacePage.AiHandoff => "motif-page-ai-handoff",
        _ => throw new ArgumentOutOfRangeException(nameof(page), page, "Unknown workspace page."),
    };
}

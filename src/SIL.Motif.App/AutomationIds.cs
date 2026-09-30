using System.Text;
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

    /// <summary>Creates a repeatable identifier for one part of a word's compact result strip.</summary>
    /// <param name="form">The displayed form, normalized so canonically equivalent Unicode has one identifier.</param>
    /// <param name="occurrenceIndex">The word's position in the selected Text.</param>
    /// <param name="part">The strip part: word, opinion, fieldworks, pangloss, action, fix, staged, or unread.</param>
    /// <returns>An ASCII identifier that names this part of this word occurrence.</returns>
    internal static string ForWordPart(string form, int occurrenceIndex, string part)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(form);
        ArgumentOutOfRangeException.ThrowIfNegative(occurrenceIndex);
        if (part is not ("word" or "opinion" or "fieldworks" or "pangloss" or "action" or "fix" or "staged" or "unread"))
            throw new ArgumentException("Unknown word strip part.", nameof(part));

        var formCode = Convert.ToHexString(Encoding.UTF8.GetBytes(form.Normalize(NormalizationForm.FormC)))
            .ToLowerInvariant();
        return $"motif-word-{occurrenceIndex}-{formCode}-{part}";
    }
}

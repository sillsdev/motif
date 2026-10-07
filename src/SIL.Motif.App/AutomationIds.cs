using System.Text;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App;

public static class AutomationIds
{
    public const string ProjectMenu = "motif-project-menu";
    public const string SelectNewProject = "motif-select-new-project";
    public const string RefreshProject = "motif-refresh-project";
    public const string AnalyzeTextsTab = "motif-analyze-texts-tab";
    public const string AnalyzeTextReaderView = "motif-analyze-text-reader-view";
    public const string AnalyzeWordListView = "motif-analyze-word-list-view";
    public const string RunAssessment = "motif-run-assessment";
    public const string EarlierResultsNote = "motif-earlier-results-note";
    public const string SkipSetup = "motif-skip-setup";
    public const string OverviewSelectionWordCount = "motif-overview-selection-word-count";
    public const string OverviewSpeed = "motif-overview-speed";
    public const string OverviewTextCoverage = "motif-overview-text-coverage";
    public const string SetupAddWords = "motif-setup-add-words";
    public const string SetupStepLimit = "motif-setup-step-limit";
    public const string SetupSelectAllTexts = "motif-setup-select-all-texts";
    public const string SetupClearTexts = "motif-setup-clear-texts";
    public const string SelectAllTexts = "motif-select-all-texts";
    public const string ClearTexts = "motif-clear-texts";
    public const string SetupBack = "motif-setup-back";
    public const string SetupNext = "motif-setup-next";
    public const string SetupFinish = "motif-setup-finish";
    public const string ParseAllWords = "motif-parse-all-words";
    public const string ParseProgressDetails = "motif-parse-progress-details";
    public const string ParseStoppedWords = "motif-parse-stopped-words";
    public const string ParseNoProgress = "motif-parse-no-progress";
    public const string ParseReportProblem = "motif-parse-report-problem";
    public const string ParseAllWordsProgress = "motif-parse-all-words-progress";
    public const string CancelAssessment = "motif-cancel-assessment";
    public const string ReviewChanges = "motif-review-changes";
    public const string MeasureChanges = "motif-measure-changes";
    public const string ApplyChanges = "motif-apply-changes";
    public const string WriteHandoff = "motif-write-handoff";
    public const string CancelHandoff = "motif-cancel-handoff";
    public const string HandoffProgress = "motif-handoff-progress";
    public const string HandoffFiles = "motif-handoff-files";
    public const string FreshnessLabel = "motif-freshness-label";
    public const string FreshnessDetail = "motif-freshness-detail";
    public const string ParsePromptText = "motif-parse-prompt-text";
    public const string HandOffList = "motif-handoff-list";
    public const string HandOffCheckedWords = "motif-handoff-checked-words";
    public const string TryWordHandOff = "motif-try-word-handoff";
    public const string DragAllHandoffFiles = "motif-drag-all-handoff-files";
    public const string ApplyReceipt = "motif-apply-receipt";
    public const string TryWordInput = "motif-try-word-input";
    public const string TryWordRun = "motif-try-word-run";
    public const string TryWordResult = "motif-try-word-result";
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
    /// <param name="textId">The identity of the selected Text containing the occurrence.</param>
    /// <param name="occurrenceIndex">The word's position in the selected Text.</param>
    /// <param name="part">One of strip, word, opinion, disapproved, fieldworks, pangloss, action, fix, staged, or unread.</param>
    /// <returns>An ASCII identifier that names this part of this word occurrence.</returns>
    internal static string ForWordPart(Guid textId, string form, int occurrenceIndex, string part)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(textId, Guid.Empty);
        return ForWordPartIdentity(textId.ToString("N"), form, occurrenceIndex, part);
    }

    /// <summary>Creates a repeatable identifier for a pasted word occurrence that is not anchored to a Text.</summary>
    /// <param name="form">The displayed form, normalized so canonically equivalent Unicode has one identifier.</param>
    /// <param name="occurrenceIndex">The word's position in the pasted input.</param>
    /// <param name="part">One of the word strip's named parts.</param>
    /// <returns>An ASCII identifier that names this part of this pasted word occurrence.</returns>
    internal static string ForPastedWordPart(string form, int occurrenceIndex, string part) =>
        ForWordPartIdentity("input", form, occurrenceIndex, part);

    private static string ForWordPartIdentity(string identity, string form, int occurrenceIndex, string part)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(form);
        ArgumentOutOfRangeException.ThrowIfNegative(occurrenceIndex);
        if (part is not ("strip" or "word" or "opinion" or "disapproved" or "fieldworks" or "pangloss" or
            "action" or "fix" or "staged" or "unread"))
            throw new ArgumentException("Unknown word strip part.", nameof(part));

        var formCode = Convert.ToHexString(Encoding.UTF8.GetBytes(form.Normalize(NormalizationForm.FormC)))
            .ToLowerInvariant();
        return $"motif-word-{identity}-{occurrenceIndex}-{formCode}-{part}";
    }

    /// <summary>Creates a repeatable identifier for one part of a word row in one of the window's word lists.</summary>
    /// <param name="list">The list the row sits in, such as <c>matrix</c>, <c>fix-first</c> or <c>lists</c>.</param>
    /// <param name="form">The word, normalized so canonically equivalent Unicode has one identifier.</param>
    /// <param name="part">One of row, word, tick, open-in-text, try-a-word, word-analyses or card.</param>
    /// <returns>
    /// An ASCII identifier such as <c>motif-word-row-matrix-kitabu-row</c>. A form of lowercase ASCII letters keeps
    /// its spelling; any other form is written as the hexadecimal of its UTF-8 bytes, prefixed <c>x</c>.
    /// </returns>
    public static string ForWordRowPart(string list, string form, string part)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(list);
        ArgumentException.ThrowIfNullOrWhiteSpace(form);
        if (part is not ("row" or "word" or "tick" or "open-in-text" or "try-a-word" or "word-analyses" or "card"))
            throw new ArgumentException("Unknown word row part.", nameof(part));

        var normal = form.Normalize(NormalizationForm.FormC);
        var formCode = normal.All(letter => letter is >= 'a' and <= 'z') ? normal
            : "x" + Convert.ToHexString(Encoding.UTF8.GetBytes(normal)).ToLowerInvariant();
        return $"motif-word-row-{list}-{formCode}-{part}";
    }
}

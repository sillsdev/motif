using System.Text;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App;

public static class AutomationIds
{
    public const string MainMenu = "motif-main-menu";
    /// <summary>The ≡ button that opens the menus in a narrow window.</summary>
    public const string MenuButton = "motif-menu-button";
    /// <summary>The top bar's project chip, which opens the project card.</summary>
    public const string ProjectChip = "motif-project-chip";
    /// <summary>The project card that opens from the chip.</summary>
    public const string ProjectCard = "motif-project-card";
    public const string ProjectCardRefresh = "motif-project-card-refresh";
    public const string ProjectCardSeeWhatChanged = "motif-project-card-see-what-changed";
    public const string ProjectCardChoose = "motif-project-card-choose";
    public const string ProjectCardSettings = "motif-project-card-settings";
    public const string ProjectCardOpenAnother = "motif-project-card-open-another";
    public const string TechDemoGotIt = "motif-tech-demo-got-it";
    /// <summary>The File menu, which holds the project commands the project menu button used to hold.</summary>
    public const string ProjectMenu = "motif-project-menu";
    public const string HelpButton = "motif-help-button";
    public const string SettingsButton = "motif-settings-button";
    /// <summary>The automation identifier of one finding's row on the Parsimony page.</summary>
    public static string ForParsimonyFinding(string findingId) => "motif-parsimony-finding-" + findingId;

    /// <summary>The Parsimony page's axis counts, shown once its findings are listed.</summary>
    public const string ParsimonySummary = "motif-parsimony-summary";
    /// <summary>The evidence column, shown while a finding is selected.</summary>
    public const string ParsimonyEvidence = "motif-parsimony-evidence";
    /// <summary>The automation identifier of one kind-of-finding filter on the Parsimony page.</summary>
    public static string ParsimonyKindFilter(string measureId) => "motif-parsimony-kind-" + measureId;

    /// <summary>The automation identifier of one decision filter on the Parsimony page.</summary>
    public static string ParsimonyDecisionFilter(string key) => "motif-parsimony-decision-" + key;

    /// <summary>The Parsimony page's Active and Suppressed tabs.</summary>
    public const string ParsimonyActiveTab = "motif-parsimony-tab-active";
    public const string ParsimonySuppressedTab = "motif-parsimony-tab-suppressed";
    /// <summary>The record type a decision is recorded under, and the reason and question fields.</summary>
    public const string ParsimonyRecordType = "motif-parsimony-record-type";
    public const string ParsimonyReason = "motif-parsimony-reason";
    public const string ParsimonyQuestion = "motif-parsimony-question";
    /// <summary>The decision actions and the line that says what the last one did.</summary>
    public const string ParsimonyKeep = "motif-parsimony-keep";
    public const string ParsimonyDefer = "motif-parsimony-defer";
    public const string ParsimonyAsk = "motif-parsimony-ask";
    public const string ParsimonyReturn = "motif-parsimony-return";
    public const string ParsimonyActionMessage = "motif-parsimony-action-message";
    /// <summary>The staged-decision link to Review changes.</summary>
    public const string ParsimonyReviewChanges = "motif-parsimony-review-changes";
    /// <summary>A finding's decision, shown as the window's chip beside its text.</summary>
    public const string ParsimonyDecision = "motif-parsimony-decision";
    /// <summary>The Suppressed decision of one revision.</summary>
    public static string ForParsimonySuppressed(string revisionId) => "motif-parsimony-suppressed-" + revisionId;
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
    public const string SetupCancel = "motif-setup-cancel";
    public const string ParseAllWords = "motif-parse-all-words";
    public const string ParseProgressDetails = "motif-parse-progress-details";
    public const string ParseStoppedWords = "motif-parse-stopped-words";
    public const string ParseNoProgress = "motif-parse-no-progress";
    public const string ParseReportProblem = "motif-parse-report-problem";
    public const string ParseAllWordsProgress = "motif-parse-all-words-progress";
    public const string CancelAssessment = "motif-cancel-assessment";
    public const string ReviewChanges = "motif-review-changes";
    public const string MeasureChanges = "motif-measure-changes";
    public const string ReviewPrevious = "motif-review-previous";
    public const string ReviewNext = "motif-review-next";
    public const string ReviewKeep = "motif-review-keep";
    public const string ReviewUndo = "motif-review-undo";
    public const string ReviewLookedAt = "motif-review-looked-at";
    public const string ReviewUndoPrompt = "motif-review-undo-prompt";
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
    public const string ProposalsSetAside = "motif-proposals-set-aside";
    public const string ProposalsClearFilter = "motif-proposals-clear-filter";
    /// <summary>The Proposals page's grouped table.</summary>
    public const string ProposalsTable = "motif-proposals-table";

    /// <summary>The "words in Selection" caption under the Overview's first count.</summary>
    public const string OverviewSelectionLabel = "motif-overview-selection-label";

    /// <summary>The Analyze texts reader's Read menu button.</summary>
    public const string ReaderReadMenu = "motif-reader-read-menu";

    /// <summary>The sidebar's page-name label, one per page, and its Selection summary line.</summary>
    public const string SidebarPageLabel = "motif-sidebar-page-label";
    public const string SidebarSelectionSummary = "motif-sidebar-selection-summary";

    /// <summary>The text the AI assistants page shows when it finds no supported assistant.</summary>
    public const string AssistantsEmptyText = "motif-assistants-empty-text";

    /// <summary>The word list and its content grid on the Texts matrix, Lists and Review pages and Timing.</summary>
    public const string MatrixWordsGrid = "motif-matrix-words-grid";
    public const string ListsWordsSlot = "motif-lists-words-slot";
    public const string ListsWordsGrid = "motif-lists-words-grid";
    public const string TimingWordsGrid = "motif-timing-words-grid";
    public const string ReviewWordsSlot = "motif-review-words-slot";
    public const string ReviewWordsGrid = "motif-review-words-grid";

    /// <summary>The Review changes page's count heading, and the Apply card's heading and count.</summary>
    public const string ReviewCountTitle = "motif-review-count-title";
    public const string ReviewApplyTitle = "motif-review-apply-title";
    public const string ReviewApplyCount = "motif-review-apply-count";

    /// <summary>The parts of the Texts matrix: a row's label and count, a cell's three lines, the list heading.</summary>
    public const string MatrixRowOpinionLabel = "motif-matrix-row-opinion-label";
    public const string MatrixRowCount = "motif-matrix-row-count";
    public const string MatrixCellCount = "motif-matrix-cell-count";
    public const string MatrixCellPlaces = "motif-matrix-cell-places";
    public const string MatrixCellLabel = "motif-matrix-cell-label";
    public const string MatrixListHeading = "motif-matrix-list-heading";
    public const string MatrixListSummary = "motif-matrix-list-summary";
    public const string MatrixListExplanation = "motif-matrix-list-explanation";
    public const string MatrixSortChoice = "motif-matrix-sort-choice";
    public const string MatrixParseAgain = "motif-matrix-parse-again";

    /// <summary>A word row's Open in text and Try a Word buttons, and the Keep and Undo buttons on a row in the review list.</summary>
    public const string WordOpenInText = "motif-word-open-in-text";
    public const string WordUndoChange = "motif-word-undo-change";
    public const string WordTryAWord = "motif-word-try-a-word";
    public const string WordKeepChange = "motif-word-keep-change";

    /// <summary>The sentence a pending-change row gives about its word.</summary>
    public const string WordNote = "motif-word-note";

    /// <summary>The Review page's Back button and the group header's Remove button.</summary>
    public const string ReviewBack = "motif-review-back";

    /// <summary>The identifier of one Review group's Undo all button, distinct for each group title.</summary>
    /// <param name="title">The group's title, which is unique within the page.</param>
    /// <returns>An identifier that no other group's button shares.</returns>
    public static string ForReviewGroupUndoAll(string title) =>
        "motif-review-group-undo-all-" + Convert.ToHexString(
            Encoding.UTF8.GetBytes(title.Normalize(NormalizationForm.FormC))).ToLowerInvariant();

    /// <summary>The letter drawn inside an opinion mark.</summary>
    public const string OpinionMarkLetter = "motif-opinion-mark-letter";

    /// <summary>The words shown beside a mark, a morpheme panel's form and a mark's own sign, one id per role.</summary>
    public const string NamedMarkText = "motif-named-mark-text";
    public const string MorphemePanelForm = "motif-morpheme-panel-form";
    public const string MarkGlyph = "motif-mark-glyph";

    /// <summary>Creates the identifier of one column's heading in a word list.</summary>
    /// <param name="column">The column's name, such as <c>FieldWorks</c>.</param>
    /// <returns>An identifier shared by that column's heading in every word list.</returns>
    public static string ForWordHeading(string column) => "motif-word-heading-" + column.ToLowerInvariant();

    /// <summary>The Review changes action that declines the open Proposal.</summary>
    public const string DeclineProposal = "motif-review-decline";

    /// <summary>Creates the identifier of the Open button on one Proposal's card.</summary>
    /// <param name="proposalId">The Proposal's canonical id.</param>
    /// <returns>An ASCII identifier that stays with the Proposal whichever group it is in.</returns>
    public static string ForProposalOpen(string proposalId) => "motif-proposals-open-" + ProposalIdPart(proposalId);

    /// <summary>Creates the identifier of one Proposal row's action menu.</summary>
    /// <param name="proposalId">The Proposal's canonical id.</param>
    /// <returns>An ASCII identifier that stays with the Proposal whichever group it is in.</returns>
    public static string ForProposalActions(string proposalId) => "motif-proposals-actions-" + ProposalIdPart(proposalId);

    /// <summary>Creates the identifier of the Try again button on one Proposal's card.</summary>
    /// <param name="proposalId">The Proposal's canonical id.</param>
    /// <returns>An ASCII identifier that stays with the Proposal whichever group it is in.</returns>
    public static string ForProposalTryAgain(string proposalId) =>
        "motif-proposals-try-again-" + ProposalIdPart(proposalId);

    /// <summary>Creates the identifier of the Delete button on one closed Proposal's card.</summary>
    /// <param name="proposalId">The Proposal's canonical id.</param>
    /// <returns>An ASCII identifier that stays with the Proposal whichever group it is in.</returns>
    public static string ForProposalDelete(string proposalId) =>
        "motif-proposals-delete-" + ProposalIdPart(proposalId);

    /// <summary>Creates the identifier of the Reopen button on one declined or withdrawn Proposal's card.</summary>
    /// <param name="proposalId">The Proposal's canonical id.</param>
    /// <returns>An ASCII identifier that stays with the Proposal whichever group it is in.</returns>
    public static string ForProposalReopen(string proposalId) =>
        "motif-proposals-reopen-" + ProposalIdPart(proposalId);

    // Script ids are lower-case ASCII, so a Proposal's id becomes a fixed-length hash of itself.
    private static string ProposalIdPart(string proposalId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proposalId);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes(proposalId)), 0, 8).ToLowerInvariant();
    }

    /// <summary>Creates the identifier of one Proposal's row on the Proposals page.</summary>
    /// <param name="proposalId">The Proposal's canonical id.</param>
    /// <returns>An ASCII identifier that names that row.</returns>
    public static string ForProposalRow(string proposalId)
    {
        return "motif-proposal-row-" + ProposalIdPart(proposalId);
    }

    public static string ForPage(WorkspacePage page) => page switch
    {
        WorkspacePage.Overview => "motif-page-overview",
        WorkspacePage.Proposals => "motif-page-proposals",
        WorkspacePage.Texts => "motif-page-texts",
        WorkspacePage.TryAWord => "motif-page-try-a-word",
        WorkspacePage.Timing => "motif-page-timing",
        WorkspacePage.Warnings => "motif-page-warnings",
        WorkspacePage.Parsimony => "motif-page-parsimony",
        WorkspacePage.Review => "motif-page-review",
        WorkspacePage.AiHandoff => "motif-page-ai-handoff",
        WorkspacePage.AiAssistants => "motif-page-ai-assistants",
        WorkspacePage.Settings => "motif-page-settings",
        _ => throw new ArgumentOutOfRangeException(nameof(page), page, "Unknown workspace page."),
    };

    /// <summary>Creates a repeatable identifier for one part of a word's compact result strip.</summary>
    /// <param name="form">The displayed form, normalized so canonically equivalent Unicode has one identifier.</param>
    /// <param name="textId">The identity of the selected Text containing the occurrence.</param>
    /// <param name="occurrenceIndex">The word's position in the selected Text.</param>
    /// <param name="part">One of strip, word, opinion, disapproved, fieldworks, pangloss, action, fix, add, staged, or unread.</param>
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
            "action" or "fix" or "add" or "staged" or "unread"))
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

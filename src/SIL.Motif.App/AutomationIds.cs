using System.Text;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App;

public static class AutomationIds
{
    public const string HealthStrip = "motif-health-strip";
    public const string HealthDetails = "motif-health-details";
    public const string HealthEnableDiagnostics = "motif-health-enable-diagnostics";
    public const string ProgressNext = "motif-progress-next";
    public const string ProgressHeading = "motif-progress-heading";
    public const string Sources = "motif-sources";
    public const string Passage = "motif-passage";
    public const string WordOccurrences = "motif-word-occurrences";
    public const string GrammarGuide = "motif-grammar-guide";
    public const string ProposalDocuments = "motif-proposal-documents";
    public const string ProposalCompare = "motif-proposal-compare";
    public const string ClaudePanel = "motif-claude-panel";
    public const string DiagnosticsWarnings = "motif-diagnostics-warnings";
    public const string DiagnosticsSpeedByRule = "motif-diagnostics-speed-by-rule";
    public const string WordTrace = "motif-word-trace";
    public const string SourcesAdd = "motif-sources-add";
    public const string SourcesList = "motif-sources-list";
    public const string PassageReader = "motif-passage-reader";
    public const string PassageKeepAnalysis = "motif-passage-keep-analysis";
    public const string PassageNextGap = "motif-passage-next-gap";
    public const string PassageKeepAllLikeThis = "motif-passage-keep-all-like-this";
    public const string WordDetail = "motif-word-detail";
    public const string WordAskClaude = "motif-word-ask-claude";
    public const string WordNotAWord = "motif-word-not-a-word";
    public const string ProposalsList = "motif-proposals-list";
    public const string ProposalComments = "motif-proposal-comments";
    public const string ProposalApply = "motif-proposal-apply";
    public const string ProposalDecline = "motif-proposal-decline";
    public const string ProposalCompareFirst = "motif-proposal-compare-first";
    public const string ProposalCompareSecond = "motif-proposal-compare-second";
    public const string GrammarGuideSections = "motif-grammar-guide-sections";
    public const string GrammarGuideDiscussion = "motif-grammar-guide-discussion";
    public const string ClaudeRequests = "motif-claude-requests";
    public const string DiagnosticsTabs = "motif-diagnostics-tabs";
    public const string DiagnosticsWarningDetails = "motif-diagnostics-warning-details";
    public const string DiagnosticsRuleRows = "motif-diagnostics-rule-rows";
    public const string ProgressChapters = "motif-progress-chapters";
    public const string MainMenu = "motif-main-menu";
    /// <summary>The ≡ button that opens the menus in a narrow window.</summary>
    public const string MenuButton = "motif-menu-button";
    /// <summary>The top bar's project chip, which opens the project card.</summary>
    public const string ProjectChip = "motif-project-chip";
    /// <summary>The project card that opens from the chip.</summary>
    public const string ProjectCard = "motif-project-card";
    public const string ProjectCardChoose = "motif-project-card-choose";
    public const string ProjectCardSettings = "motif-project-card-settings";
    public const string ProjectCardOpenAnother = "motif-project-card-open-another";
    public const string TechDemoGotIt = "motif-tech-demo-got-it";
    /// <summary>The File menu, which holds the project commands the project menu button used to hold.</summary>
    public const string ProjectMenu = "motif-project-menu";
    public const string HelpButton = "motif-help-button";
    public const string SettingsButton = "motif-settings-button";
    /// <summary>The setting that makes the file Handoff section available.</summary>
    public const string SettingsShowHandoff = "motif-settings-show-handoff";
    /// <summary>The button that leaves Settings.</summary>
    public const string SettingsClose = "motif-settings-close";
    public const string SelectNewProject = "motif-select-new-project";
    public const string RefreshProject = "motif-refresh-project";
    public const string SkipSetup = "motif-skip-setup";
    public const string SetupAddWords = "motif-setup-add-words";
    public const string SetupParserWorkLimit = "motif-setup-parser-work-limit";
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
    public const string WriteHandoff = "motif-write-handoff";
    public const string CancelHandoff = "motif-cancel-handoff";
    public const string HandoffProgress = "motif-handoff-progress";
    public const string HandoffFiles = "motif-handoff-files";
    public const string FreshnessLabel = "motif-freshness-label";
    public const string FreshnessDetail = "motif-freshness-detail";
    public const string ParsePromptText = "motif-parse-prompt-text";
    public const string DragAllHandoffFiles = "motif-drag-all-handoff-files";
    public const string Pages = "motif-pages";
    public const string ProposalsSetAside = "motif-proposals-set-aside";
    /// <summary>The button that confirms deleting the selected Proposal.</summary>
    public const string ProposalsConfirmDelete = "motif-proposals-confirm-delete";
    /// <summary>The button that keeps the selected Proposal instead of deleting it.</summary>
    public const string ProposalsKeep = "motif-proposals-keep";
    /// <summary>The saved Difference section of the Proposal inspector.</summary>
    public const string ProposalDifferencePanel = "motif-proposal-difference";
    /// <summary>The automatic Parsimony advice section of the Proposal inspector.</summary>
    public const string ProposalParsimonyPanel = "motif-proposal-parsimony";
    /// <summary>The pictures section of the Proposal inspector.</summary>
    public const string ProposalPicturesPanel = "motif-proposal-pictures";
    /// <summary>The button that chooses a picture to add to the selected Proposal.</summary>
    public const string ProposalPictureAdd = "motif-proposal-picture-add";
    public const string ProposalParsimonyState = "motif-proposal-parsimony-state";
    public const string ProposalParsimonyGroups = "motif-proposal-parsimony-groups";
    /// <summary>The short read of the Parsimony section: which measures found new Findings.</summary>
    public const string ProposalParsimonyRead = "motif-proposal-parsimony-read";
    /// <summary>The Proposal's parser work against the Baseline, as a percent change.</summary>
    public const string ProposalParsimonyParserWork = "motif-proposal-parsimony-parser-work";
    /// <summary>How long Parsimony took, in seconds or minutes.</summary>
    public const string ProposalParsimonyWallTime = "motif-proposal-parsimony-wall-time";
    /// <summary>The automation identifier of one Finding of the Parsimony section.</summary>
    public static string ForProposalParsimonyFinding(string findingId) => "motif-proposal-parsimony-finding-" + findingId;
    /// <summary>The automation identifier of one Finding's Show Evidence button.</summary>
    public static string ForProposalParsimonyEvidence(string findingId) => "motif-proposal-parsimony-evidence-" + findingId;

    /// <summary>The automation identifier of a Finding's evidence text.</summary>
    public static string ForProposalParsimonyEvidenceText(string findingId) => "motif-proposal-parsimony-evidence-text-" + findingId;

    /// <summary>The automation identifier of a Parsimony group's Findings fold; <paramref name="group"/> is grammar or text.</summary>
    public static string ForProposalParsimonyFindings(string group) => "motif-proposal-parsimony-findings-" + group;

    /// <summary>The automation identifier of a Parsimony group's list of Findings.</summary>
    public static string ForProposalParsimonyFindingsList(string group) => "motif-proposal-parsimony-findings-list-" + group;

    /// <summary>The automation identifier of a Parsimony group's note about its Findings.</summary>
    public static string ForProposalParsimonyFindingsNote(string group) => "motif-proposal-parsimony-findings-note-" + group;

    /// <summary>The automation identifier of the lines saying which Findings a group does not list.</summary>
    public static string ForProposalParsimonyFindingsMore(string group) => "motif-proposal-parsimony-findings-more-" + group;

    /// <summary>The automation identifier of a Parsimony group's decision lines.</summary>
    public static string ForProposalParsimonyJudgments(string group) => "motif-proposal-parsimony-judgments-" + group;
    public const string ProposalDifferenceClear = "motif-proposal-difference-clear";
    public const string ProposalDifferenceText = "motif-proposal-difference-text";
    public const string ProposalDifferenceSaved = "motif-proposal-difference-saved";
    public const string ProposalDifferenceMake = "motif-proposal-difference-make";
    public const string ProposalDifferenceCancel = "motif-proposal-difference-cancel";
    public const string ProposalDifferenceProgress = "motif-proposal-difference-progress";
    public const string ProposalDifferenceMoreWords = "motif-proposal-difference-more-words";
    public const string ProposalDifferenceMorePlaces = "motif-proposal-difference-more-places";
    public const string ProposalDifferenceOpenWord = "motif-proposal-difference-open-word";


    /// <summary>The Analyze texts reader's Read menu button.</summary>
    public const string ReaderReadMenu = "motif-reader-read-menu";

    /// <summary>The sidebar's page-name label, one per page, and its Selection summary line.</summary>
    public const string SidebarPageLabel = "motif-sidebar-page-label";
    public const string SidebarSelectionSummary = "motif-sidebar-selection-summary";

    /// <summary>The text the AI assistants page shows when it finds no supported assistant.</summary>
    public const string AssistantsEmptyText = "motif-assistants-empty-text";

    /// <summary>A word row's Open in Text, Keep and Undo buttons.</summary>
    public const string WordOpenInText = "motif-word-open-in-text";
    public const string WordUndoChange = "motif-word-undo-change";
    public const string WordKeepChange = "motif-word-keep-change";

    /// <summary>The sentence a pending-change row gives about its word.</summary>
    public const string WordNote = "motif-word-note";

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

    /// <summary>Creates the identifier of Ask Again on one declined Proposal's card.</summary>
    /// <param name="proposalId">The Proposal's canonical id.</param>
    /// <returns>An ASCII identifier that stays with the Proposal.</returns>
    public static string ForProposalAskAgain(string proposalId) =>
        "motif-proposals-ask-again-" + ProposalIdPart(proposalId);

    /// <summary>Creates the identifier of Withdraw Request on one open Request.</summary>
    /// <param name="requestId">The Request's canonical id.</param>
    /// <returns>An ASCII identifier that stays with the Request.</returns>
    public static string ForRequestWithdraw(string requestId) =>
        "motif-requests-withdraw-" + ProposalIdPart(requestId);

    /// <summary>The Ask an Assistant to Fix This button and its note box.</summary>
    public const string AskAssistantButton = "motif-ask-assistant";

    /// <summary>The note box in the Ask an Assistant to Fix This flyout.</summary>
    public const string AskAssistantNote = "motif-ask-assistant-note";

    /// <summary>The Ask button in the Ask an Assistant to Fix This flyout.</summary>
    public const string AskAssistantSend = "motif-ask-assistant-send";

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
        WorkspacePage.Progress => "motif-page-progress",
        WorkspacePage.Words => "motif-page-words",
        WorkspacePage.GrammarGuide => "motif-page-grammar-guide",
        WorkspacePage.Diagnostics => "motif-page-diagnostics",
        WorkspacePage.Proposals => "motif-page-proposals",
        WorkspacePage.Texts => "motif-page-texts",
        WorkspacePage.Settings => "motif-page-settings",
        _ => throw new ArgumentOutOfRangeException(nameof(page), page, "Unknown workspace page."),
    };

    /// <summary>Creates a repeatable identifier for one part of a word's compact result strip.</summary>
    /// <param name="form">The displayed form, normalized so canonically equivalent Unicode has one identifier.</param>
    /// <param name="textId">The identity of the selected Text containing the occurrence.</param>
    /// <param name="occurrenceIndex">The word's position in the selected Text.</param>
    /// <param name="part">One of strip, word, opinion, disapproved, fieldworks, pangloss, action, fix, add, readings, staged, or unread.</param>
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
            "action" or "fix" or "add" or "readings" or "staged" or "unread"))
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
    #region W2
    public const string SourcesFieldWorks = "motif-sources-fieldworks";
    public const string SourcesCurrent = "motif-sources-current";
    public const string SourcesContext = "motif-sources-context";
    public const string SourcesFolder = "motif-sources-folder";
    public const string SourcesStageContext = "motif-sources-stage-context";
    #endregion
    // W7_shared
    public const string ClaudeToggle = "motif-claude-toggle";
    public const string ClaudeClose = "motif-claude-close";
    public const string ClaudeSetup = "motif-claude-setup";
    // w8 proposal diagnostic rows.
    public static string ForProposalDiagnostics(string proposalId) =>
        "motif-proposal-diagnostics-" + Uri.EscapeDataString(proposalId);
    // W9-automation
    public const string ProgressNextProposals = "motif-progress-next-proposals";
    public const string ProgressNextRequests = "motif-progress-next-requests";
    public const string ProgressNextRetry = "motif-progress-next-retry";
    #region W3
    public const string WordOccurrencesPanel = "motif-word-occurrences-panel";
    public const string WordOccurrencesWord = "motif-word-occurrences-word";
    public const string WordOccurrencesSelected = "motif-word-occurrences-selected";
    public const string WordOccurrencesCheckGaps = "motif-word-occurrences-check-gaps";
    public const string WordOccurrencesPlaces = "motif-word-occurrences-places";
    public const string WordOccurrencesPrevious = "motif-word-occurrences-previous";
    public const string WordOccurrencesNext = "motif-word-occurrences-next";
    public const string WordOccurrencesReading = "motif-word-occurrences-reading";
    // fix1-word
    public const string WordOccurrencesReadingSummary = "motif-word-occurrences-reading-summary";
    public const string WordOccurrencesApprove = "motif-word-occurrences-approve";
    public const string WordOccurrencesAsk = "motif-word-occurrences-ask";
    public const string WordOccurrencesNotAWord = "motif-word-occurrences-not-a-word";
    public const string WordOccurrencesStatus = "motif-word-occurrences-status";
    public const string WordOccurrencesDiagnostics = "motif-word-occurrences-diagnostics";

    /// <summary>The check box of one place of the open word, from its segment position.</summary>
    public static string ForOccurrencePlace(SIL.Motif.Contract.Requests.OccurrenceAnchor anchor) =>
        $"motif-word-occurrence-place-{anchor.SegmentId:N}-{anchor.Index}";
    #endregion

    #region W5
    public const string ProposalComparePair = "motif-proposal-compare-pair";
    public const string ProposalPrepareApply = "motif-proposal-prepare-apply";
    public const string ProposalConfirmApply = "motif-proposal-confirm-apply";
    public const string ProposalReceipt = "motif-proposal-receipt";
    #endregion
    #region W6
    public const string ProposalComparePanel = "motif-proposal-compare-panel";
    public const string ProposalCompareBack = "motif-proposal-compare-back";
    public const string ProposalCompareRemeasure = "motif-proposal-compare-remeasure";
    public const string ProposalCompareDifferences = "motif-proposal-compare-differences";
    #endregion
    #region w10
    public const string WhyThisAnalysisFold = "motif-why-this-analysis";
    public const string WhyThisAnalysisStatus = "motif-why-this-analysis-status";
    public const string WhyThisAnalysisAnalyses = "motif-why-this-analysis-analyses";
    public const string WhyThisAnalysisRules = "motif-why-this-analysis-rules";
    public const string WhyThisAnalysisFailed = "motif-why-this-analysis-failed";
    public const string WhyThisAnalysisFullTrace = "motif-why-this-analysis-full-trace";
    public const string WhyThisAnalysisFullTraceToggle = "motif-why-this-analysis-full-trace-toggle";
    public const string WhyThisAnalysisCopy = "motif-why-this-analysis-copy";
    #endregion
    #region w11a
    public const string GuideSectionEditor = "motif-guide-section-editor";
    public const string GuideEditorToolbar = "motif-guide-editor-toolbar";
    public const string GuideEditorStale = "motif-guide-editor-stale";
    public const string GuideEditorApply = "motif-guide-editor-apply";
    public const string GuideEditorStatus = "motif-guide-editor-status";
    public const string GuideExamples = "motif-guide-examples";
    public const string GuideGrammarNames = "motif-guide-grammar-names";
    public const string GuideHeadings = "motif-guide-headings";
    public const string GuideEdit = "motif-guide-edit";
    public const string GuideNotice = "motif-guide-notice";
    // fix1-progress
    public const string ProgressNothingParsed = "motif-progress-nothing-parsed";
    public const string ProgressChooseTexts = "motif-progress-choose-texts";
    public const string ProgressParsed = "motif-progress-parsed";
    public const string ProgressReviewWords = "motif-progress-review-words";
    public const string ProgressReadTexts = "motif-progress-read-texts";
    public const string ProgressUnavailable = "motif-progress-unavailable";
    public const string ProgressReadingParse = "motif-progress-reading-parse";
    // round2-proposal
    public const string ProposalStatus = "motif-proposal-status";
    #endregion
}

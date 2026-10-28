namespace SIL.Motif.App;

/// <summary>
/// The window's localized text. The neutral MotifAppStrings.resx holds the English source and translations
/// would ship as satellite assemblies, as FwAvaloniaStrings does in FieldWorks. A language change takes effect on
/// restart because <c>{x:Static}</c> reads once. Automation ids stay constants in <see cref="AutomationIds"/>.
/// </summary>
public static class MotifAppStrings
{
    private static readonly System.Resources.ResourceManager Resources =
        new("SIL.Motif.App.MotifAppStrings", typeof(MotifAppStrings).Assembly);

    // Falls back to the id so a missing entry shows in the window; MotifAppStringsTests pins every accessor.
    private static string Text(string stringId) => Resources.GetString(stringId) ?? stringId;

    public static string SetAnalysis => Text("MotifApp.SetAnalysis");

    public static string SetAnalysisApproved => Text("MotifApp.SetAnalysisApproved");

    public static string SetAnalysisUnknown => Text("MotifApp.SetAnalysisUnknown");

    public static string SetAnalysisDisapproved => Text("MotifApp.SetAnalysisDisapproved");

    public static string SetAnalysisRemove => Text("MotifApp.SetAnalysisRemove");

    public static string SetAnalysisChangedTipFormat => Text("MotifApp.SetAnalysisChangedTipFormat");

    public static string SetAnalysisCurrentTipFormat => Text("MotifApp.SetAnalysisCurrentTipFormat");

    public static string AddAnalysisFromPanGloss => Text("MotifApp.AddAnalysisFromPanGloss");

    public static string ReviewCardEvidenceChanged => Text("MotifApp.ReviewCardEvidenceChanged");

    public static string ReviewCardDetailsUnavailable => Text("MotifApp.ReviewCardDetailsUnavailable");

    public static string ShortcutPagesRail => Text("MotifApp.ShortcutPagesRail");

    public static string ShortcutMenus => Text("MotifApp.ShortcutMenus");

    public static string ShortcutActivateMenuItem => Text("MotifApp.ShortcutActivateMenuItem");

    public static string ShortcutOpenMenu => Text("MotifApp.ShortcutOpenMenu");

    public static string ShortcutToggleSidePanel => Text("MotifApp.ShortcutToggleSidePanel");

    public static string ShortcutReviewChanges => Text("MotifApp.ShortcutReviewChanges");

    public static string ShortcutDiagnosticTree => Text("MotifApp.ShortcutDiagnosticTree");

    public static string ShortcutMorpheme => Text("MotifApp.ShortcutMorpheme");

    public static string ShortcutWindow => Text("MotifApp.ShortcutWindow");

    public static string ShortcutEditing => Text("MotifApp.ShortcutEditing");

    public static string ShortcutMatrixPreview => Text("MotifApp.ShortcutMatrixPreview");

    public static string ShortcutMoveBetweenWords => Text("MotifApp.ShortcutMoveBetweenWords");

    public static string ShortcutPreviousLine => Text("MotifApp.ShortcutPreviousLine");

    public static string ShortcutNextLine => Text("MotifApp.ShortcutNextLine");

    public static string ShortcutOpenWordCard => Text("MotifApp.ShortcutOpenWordCard");

    public static string ShortcutOpenOrCloseWordCard => Text("MotifApp.ShortcutOpenOrCloseWordCard");

    public static string ShortcutCloseWordCard => Text("MotifApp.ShortcutCloseWordCard");

    public static string ShortcutReaderPreviousScreen => Text("MotifApp.ShortcutReaderPreviousScreen");

    public static string ShortcutReaderNextScreen => Text("MotifApp.ShortcutReaderNextScreen");

    public static string ShortcutReaderFirstLine => Text("MotifApp.ShortcutReaderFirstLine");

    public static string ShortcutReaderLastLine => Text("MotifApp.ShortcutReaderLastLine");

    public static string ShortcutStageApprove => Text("MotifApp.ShortcutStageApprove");

    public static string ShortcutStageDisapprove => Text("MotifApp.ShortcutStageDisapprove");

    public static string ShortcutStageUnknown => Text("MotifApp.ShortcutStageUnknown");

    public static string ShortcutStageRemoveAnalysis => Text("MotifApp.ShortcutStageRemoveAnalysis");

    public static string ShortcutStageIncorrectSpelling => Text("MotifApp.ShortcutStageIncorrectSpelling");

    public static string ShortcutOpenAddList => Text("MotifApp.ShortcutOpenAddList");

    public static string ShortcutOpenDispositionMenu => Text("MotifApp.ShortcutOpenDispositionMenu");

    public static string ShortcutPreviousChange => Text("MotifApp.ShortcutPreviousChange");

    public static string ShortcutNextChange => Text("MotifApp.ShortcutNextChange");

    public static string ShortcutKeepChange => Text("MotifApp.ShortcutKeepChange");

    public static string ShortcutUndoChange => Text("MotifApp.ShortcutUndoChange");

    public static string ShortcutPreviousRow => Text("MotifApp.ShortcutPreviousRow");

    public static string ShortcutNextRow => Text("MotifApp.ShortcutNextRow");

    public static string ShortcutWordPreviousScreen => Text("MotifApp.ShortcutWordPreviousScreen");

    public static string ShortcutWordNextScreen => Text("MotifApp.ShortcutWordNextScreen");

    public static string ShortcutFirstRow => Text("MotifApp.ShortcutFirstRow");

    public static string ShortcutLastRow => Text("MotifApp.ShortcutLastRow");

    public static string ShortcutPreviousDiagnosticItem => Text("MotifApp.ShortcutPreviousDiagnosticItem");

    public static string ShortcutNextDiagnosticItem => Text("MotifApp.ShortcutNextDiagnosticItem");

    public static string ShortcutFocusWordSearch => Text("MotifApp.ShortcutFocusWordSearch");

    public static string ShortcutChooseCell => Text("MotifApp.ShortcutChooseCell");

    public static string ShortcutOpenInspector => Text("MotifApp.ShortcutOpenInspector");

    public static string ShortcutOpenHelp => Text("MotifApp.ShortcutOpenHelp");

    public static string ShortcutStepBack => Text("MotifApp.ShortcutStepBack");

    public static string ShortcutShowShortcuts => Text("MotifApp.ShortcutShowShortcuts");

    public static string ShortcutOpenSettings => Text("MotifApp.ShortcutOpenSettings");

    public static string ShortcutZoomIn => Text("MotifApp.ShortcutZoomIn");

    public static string ShortcutZoomOut => Text("MotifApp.ShortcutZoomOut");

    public static string ShortcutResetZoom => Text("MotifApp.ShortcutResetZoom");

    public static string ShortcutCopy => Text("MotifApp.ShortcutCopy");

    public static string ShortcutRedo => Text("MotifApp.ShortcutRedo");

    public static string ShortcutCloseSettings => Text("MotifApp.ShortcutCloseSettings");

    public static string ShortcutSaveParsingLimits => Text("MotifApp.ShortcutSaveParsingLimits");

    public static string ShortcutCancelParsingLimitEdit => Text("MotifApp.ShortcutCancelParsingLimitEdit");

    public static string ShortcutMoveBetweenThemeChoices => Text("MotifApp.ShortcutMoveBetweenThemeChoices");

    public static string ShortcutFocusCloseSettings => Text("MotifApp.ShortcutFocusCloseSettings");

    public static string ShortcutFocusSettingsGroups => Text("MotifApp.ShortcutFocusSettingsGroups");

    public static string ShortcutTables => Text("MotifApp.ShortcutTables");

    public static string ShortcutTableNextAction => Text("MotifApp.ShortcutTableNextAction");

    public static string ShortcutTablePreviousAction => Text("MotifApp.ShortcutTablePreviousAction");

    public static string ShortcutTableOpenActions => Text("MotifApp.ShortcutTableOpenActions");

    public static string ShortcutTableOpenDetails => Text("MotifApp.ShortcutTableOpenDetails");

    public static string MenuFile => Text("MotifApp.MenuFile");

    public static string MenuSelectNew => Text("MotifApp.MenuSelectNew");

    public static string MenuOpenRecent => Text("MotifApp.MenuOpenRecent");

    public static string MenuConfigure => Text("MotifApp.MenuConfigure");

    public static string MenuExit => Text("MotifApp.MenuExit");

    public static string MenuEdit => Text("MotifApp.MenuEdit");

    public static string MenuUndo => Text("MotifApp.MenuUndo");

    public static string MenuRedo => Text("MotifApp.MenuRedo");

    public static string MenuCopy => Text("MotifApp.MenuCopy");

    public static string MenuView => Text("MotifApp.MenuView");

    public static string MenuZoomIn => Text("MotifApp.MenuZoomIn");

    public static string MenuZoomOut => Text("MotifApp.MenuZoomOut");

    public static string MenuResetZoom => Text("MotifApp.MenuResetZoom");

    public static string MenuTools => Text("MotifApp.MenuTools");

    public static string MenuOptions => Text("MotifApp.MenuOptions");

    public static string MenuWritingSystems => Text("MotifApp.MenuWritingSystems");

    public static string MenuHelp => Text("MotifApp.MenuHelp");

    public static string MenuHelpTopics => Text("MotifApp.MenuHelpTopics");

    public static string MenuKeyboardShortcuts => Text("MotifApp.MenuKeyboardShortcuts");

    public static string MenuReportProblem => Text("MotifApp.MenuReportProblem");

    public static string MenuRecentPreviousPattern => Text("MotifApp.MenuRecentPreviousPattern");

    public static string MenuRecentMorePattern => Text("MotifApp.MenuRecentMorePattern");

    public static string MainMenuAccessibleName => Text("MotifApp.MainMenuAccessibleName");

    public static string SeeWhatChanged => Text("MotifApp.SeeWhatChanged");

    public static string SeeWhatChangedAccessibleName => Text("MotifApp.SeeWhatChangedAccessibleName");

    public static string ParseAllWords => Text("MotifApp.ParseAllWords");

    public static string ParseAllWordsAccessibleName => Text("MotifApp.ParseAllWordsAccessibleName");

    public static string ParseAllWordsTip => Text("MotifApp.ParseAllWordsTip");

    public static string ChooseWhatToParse => Text("MotifApp.ChooseWhatToParse");

    public static string ChooseWhatToParseAccessibleName => Text("MotifApp.ChooseWhatToParseAccessibleName");

    public static string Refresh => Text("MotifApp.Refresh");

    public static string RefreshAccessibleName => Text("MotifApp.RefreshAccessibleName");

    public static string RefreshTip => Text("MotifApp.RefreshTip");

    public static string TechDemoStatus => Text("MotifApp.TechDemoStatus");

    public static string TechDemoStatusTip => Text("MotifApp.TechDemoStatusTip");

    public static string TechDemoCompact => Text("MotifApp.TechDemoCompact");

    public static string TechDemoCompactTip => Text("MotifApp.TechDemoCompactTip");

    public static string ReportProblem => Text("MotifApp.ReportProblem");

    public static string ReportProblemAccessibleName => Text("MotifApp.ReportProblemAccessibleName");

    public static string ReviewChangesAccessibleName => Text("MotifApp.ReviewChangesAccessibleName");

    public static string OpenWarningsAccessibleName => Text("MotifApp.OpenWarningsAccessibleName");

    public static string StatusChangesPattern => Text("MotifApp.StatusChangesPattern");
    public static string StatusFilterHidesPattern => Text("MotifApp.StatusFilterHidesPattern");

    public static string StatusWarningsPattern => Text("MotifApp.StatusWarningsPattern");

    public static string SeeParseProgressAccessibleName => Text("MotifApp.SeeParseProgressAccessibleName");

    public static string Parsing => Text("MotifApp.Parsing");

    public static string ParseProgressDetailsAccessibleName => Text("MotifApp.ParseProgressDetailsAccessibleName");

    public static string ParseAllWordsProgressAccessibleName => Text("MotifApp.ParseAllWordsProgressAccessibleName");

    public static string Cancel => Text("MotifApp.Cancel");

    public static string CancelParsingTip => Text("MotifApp.CancelParsingTip");

    public static string CancelParsingAccessibleName => Text("MotifApp.CancelParsingAccessibleName");

    public static string ReportParseProblemAccessibleName => Text("MotifApp.ReportParseProblemAccessibleName");

    public static string SeeStoppedWords => Text("MotifApp.SeeStoppedWords");

    public static string SeeStoppedWordsAccessibleName => Text("MotifApp.SeeStoppedWordsAccessibleName");

    public static string KeyboardShortcutStatusAccessibleName => Text("MotifApp.KeyboardShortcutStatusAccessibleName");

    public static string TrialBaselineNotice => Text("MotifApp.TrialBaselineNotice");

    public static string TrialBaselineNoticeAccessibleName => Text("MotifApp.TrialBaselineNoticeAccessibleName");

    public static string ProjectSummaryAccessibleName => Text("MotifApp.ProjectSummaryAccessibleName");

    public static string Overview => Text("MotifApp.Overview");

    public static string TheOverviewCouldNotBeReadAccessibleName => Text("MotifApp.TheOverviewCouldNotBeReadAccessibleName");

    public static string TryAgain => Text("MotifApp.TryAgain");

    public static string ReadTheOverviewAgainAccessibleName => Text("MotifApp.ReadTheOverviewAgainAccessibleName");

    public static string TheLatestParseWasRefusedAccessibleName => Text("MotifApp.TheLatestParseWasRefusedAccessibleName");

    public static string ParseFailureDetails => Text("MotifApp.ParseFailureDetails");

    public static string LatestParseFailureDetailsAccessibleName => Text("MotifApp.LatestParseFailureDetailsAccessibleName");

    public static string WordsInSelection => Text("MotifApp.WordsInSelection");

    public static string Places => Text("MotifApp.Places");

    public static string Wordforms => Text("MotifApp.Wordforms");

    public static string Rules => Text("MotifApp.Rules");

    public static string Lexemes => Text("MotifApp.Lexemes");

    public static string ChooseAProjectAccessibleName => Text("MotifApp.ChooseAProjectAccessibleName");

    public static string OpenARecentProject => Text("MotifApp.OpenARecentProject");

    public static string RecentProjectsAccessibleName => Text("MotifApp.RecentProjectsAccessibleName");

    public static string OpenFolder => Text("MotifApp.OpenFolder");

    public static string OpenProjectFolderAccessibleName => Text("MotifApp.OpenProjectFolderAccessibleName");

    public static string SelectANewProject => Text("MotifApp.SelectANewProject");

    public static string SelectANewProjectAccessibleName => Text("MotifApp.SelectANewProjectAccessibleName");

    public static string TheDefaultSelectionCouldNotBe => Text("MotifApp.TheDefaultSelectionCouldNotBe");

    public static string LookFirstAccessibleName => Text("MotifApp.LookFirstAccessibleName");

    public static string LookFirst => Text("MotifApp.LookFirst");

    public static string StoredResultsToLookAtFirstAccessibleName => Text("MotifApp.StoredResultsToLookAtFirstAccessibleName");

    public static string TextCoverageAccessibleName => Text("MotifApp.TextCoverageAccessibleName");

    public static string TextCoverage => Text("MotifApp.TextCoverage");

    public static string OverviewTextsLink => Text("MotifApp.OverviewTextsLink");

    public static string OpenTextCoverageInTextsAccessibleName => Text("MotifApp.OpenTextCoverageInTextsAccessibleName");

    public static string TextCoverageOutcomesAccessibleName => Text("MotifApp.TextCoverageOutcomesAccessibleName");

    public static string ApprovedAnalysesKeptAccessibleName => Text("MotifApp.ApprovedAnalysesKeptAccessibleName");

    public static string ApprovedAnalysesKept => Text("MotifApp.ApprovedAnalysesKept");

    public static string OverviewMatrixLink => Text("MotifApp.OverviewMatrixLink");

    public static string OpenMatrixFromApprovedKeptAccessibleName => Text("MotifApp.OpenMatrixFromApprovedKeptAccessibleName");

    public static string ApprovedAnalysesKeptOutcomesAccessibleName => Text("MotifApp.ApprovedAnalysesKeptOutcomesAccessibleName");

    public static string SpeedAccessibleName => Text("MotifApp.SpeedAccessibleName");

    public static string OverviewSlowestWordsLink => Text("MotifApp.OverviewSlowestWordsLink");

    public static string OpenSlowestWordsInTimingAccessibleName => Text("MotifApp.OpenSlowestWordsInTimingAccessibleName");

    public static string Speed => Text("MotifApp.Speed");

    public static string OverviewTimeByKindAccessibleName => Text("MotifApp.OverviewTimeByKindAccessibleName");

    public static string SlowestWordsAccessibleName => Text("MotifApp.SlowestWordsAccessibleName");

    public static string SlowestWords => Text("MotifApp.SlowestWords");

    public static string SlowestWordsAndRecordedTimesAccessibleName => Text("MotifApp.SlowestWordsAndRecordedTimesAccessibleName");

    public static string GrammarWarningsAccessibleName => Text("MotifApp.GrammarWarningsAccessibleName");

    public static string GrammarWarnings => Text("MotifApp.GrammarWarnings");

    public static string OverviewWarningsLink => Text("MotifApp.OverviewWarningsLink");

    public static string OpenGrammarWarningsInWarningsAccessibleName => Text("MotifApp.OpenGrammarWarningsInWarningsAccessibleName");

    public static string WarningKindsAndWordsAccessibleName => Text("MotifApp.WarningKindsAndWordsAccessibleName");

    public static string AskAChatModelAboutAnything => Text("MotifApp.AskAChatModelAboutAnything");

    public static string StartAnAIHandoff => Text("MotifApp.StartAnAIHandoff");

    public static string StartAnAIHandoffAccessibleName => Text("MotifApp.StartAnAIHandoffAccessibleName");

    public static string ProjectHistory => Text("MotifApp.ProjectHistory");

    public static string ProjectHistoryAccessibleName => Text("MotifApp.ProjectHistoryAccessibleName");

    public static string LoadingHistory => Text("MotifApp.LoadingHistory");

    public static string NothingRecordedForThisProjectYet => Text("MotifApp.NothingRecordedForThisProjectYet");

    public static string ProjectHistoryEntriesAccessibleName => Text("MotifApp.ProjectHistoryEntriesAccessibleName");

    public static string LostWords => Text("MotifApp.LostWords");

    public static string RecordedApprovedLostWordsAccessibleName => Text("MotifApp.RecordedApprovedLostWordsAccessibleName");

    public static string SharedMorphemes => Text("MotifApp.SharedMorphemes");

    public static string Words => Text("MotifApp.Words");

    public static string NamedByAGrammarWarning => Text("MotifApp.NamedByAGrammarWarning");

    public static string Matrix => Text("MotifApp.Matrix");

    public static string MatrixTabAccessibleName => Text("MotifApp.MatrixTabAccessibleName");

    public static string AnalyzeTexts => Text("MotifApp.AnalyzeTexts");

    public static string AnalyzeTextsTabAccessibleName => Text("MotifApp.AnalyzeTextsTabAccessibleName");

    public static string Lists => Text("MotifApp.Lists");

    public static string ListsTabAccessibleName => Text("MotifApp.ListsTabAccessibleName");

    public static string WhatChanged => Text("MotifApp.WhatChanged");

    public static string WhatChangedTabAccessibleName => Text("MotifApp.WhatChangedTabAccessibleName");

    public static string ReviewTheChangesNotAppliedYetAccessibleName => Text("MotifApp.ReviewTheChangesNotAppliedYetAccessibleName");

    public static string ReviewChanges => Text("MotifApp.ReviewChanges");

    public static string ParseAllWordsInTheSelectionAccessibleName => Text("MotifApp.ParseAllWordsInTheSelectionAccessibleName");

    public static string ParseStatusAccessibleName => Text("MotifApp.ParseStatusAccessibleName");

    public static string NoResultsYetChooseTheWords => Text("MotifApp.NoResultsYetChooseTheWords");

    public static string ReadText => Text("MotifApp.ReadText");

    public static string ShowTheTextReaderAccessibleName => Text("MotifApp.ShowTheTextReaderAccessibleName");

    public static string WordList => Text("MotifApp.WordList");

    public static string ShowTheWordListAccessibleName => Text("MotifApp.ShowTheWordListAccessibleName");

    public static string TextToReadAccessibleName => Text("MotifApp.TextToReadAccessibleName");

    public static string SearchWordsInAnalyzeTextsAccessibleName => Text("MotifApp.SearchWordsInAnalyzeTextsAccessibleName");

    public static string SelectWordsForActionsAccessibleName => Text("MotifApp.SelectWordsForActionsAccessibleName");

    public static string ChooseWordsWithCheckboxesAccessibleName => Text("MotifApp.ChooseWordsWithCheckboxesAccessibleName");

    public static string SelectAll => Text("MotifApp.SelectAll");

    public static string SelectAllChosenWordOccurrencesAccessibleName => Text("MotifApp.SelectAllChosenWordOccurrencesAccessibleName");

    public static string ClearSelection => Text("MotifApp.ClearSelection");

    public static string ClearSelectedWordOccurrencesAccessibleName => Text("MotifApp.ClearSelectedWordOccurrencesAccessibleName");

    public static string ForTheSelectedWords => Text("MotifApp.ForTheSelectedWords");

    public static string ApproveOneAnalysisAtATime => Text("MotifApp.ApproveOneAnalysisAtATime");

    public static string AddReadingsAsUnknown => Text("MotifApp.AddReadingsAsUnknown");

    public static string AcceptTheNewSet => Text("MotifApp.AcceptTheNewSet");

    public static string MarkIncorrect => Text("MotifApp.MarkIncorrect");

    public static string RemoveAnalyses => Text("MotifApp.RemoveAnalyses");

    public static string UndoChanges => Text("MotifApp.UndoChanges");

    public static string CheckAgain => Text("MotifApp.CheckAgain");

    public static string InThisText => Text("MotifApp.InThisText");

    public static string AddReadingsAsUnknownInThis => Text("MotifApp.AddReadingsAsUnknownInThis");

    public static string AddParserReadingsAsUnknownInThisTextAccessibleName => Text("MotifApp.AddParserReadingsAsUnknownInThisTextAccessibleName");

    public static string AcceptNewSetInThisText => Text("MotifApp.AcceptNewSetInThisText");

    public static string AcceptNewSetForTheSelectedAccessibleName => Text("MotifApp.AcceptNewSetForTheSelectedAccessibleName");

    public static string MarkSpellingsIncorrectInThisText => Text("MotifApp.MarkSpellingsIncorrectInThisText");

    public static string MarkSpellingsIncorrectInTheSelectedAccessibleName => Text("MotifApp.MarkSpellingsIncorrectInTheSelectedAccessibleName");

    public static string UndoChangesInThisText => Text("MotifApp.UndoChangesInThisText");

    public static string UndoChangesInTheSelectedTextAccessibleName => Text("MotifApp.UndoChangesInTheSelectedTextAccessibleName");

    public static string StageRemovalFromThisText => Text("MotifApp.StageRemovalFromThisText");

    public static string StageRemovalOfTheAnalysesUsedInThisTextAccessibleName => Text("MotifApp.StageRemovalOfTheAnalysesUsedInThisTextAccessibleName");

    public static string InAllChosenTexts => Text("MotifApp.InAllChosenTexts");

    public static string AcceptNewSetInSelection => Text("MotifApp.AcceptNewSetInSelection");

    public static string AcceptNewSetForTheSelectionAccessibleName => Text("MotifApp.AcceptNewSetForTheSelectionAccessibleName");

    public static string AddReadingsAsUnknownInAll => Text("MotifApp.AddReadingsAsUnknownInAll");

    public static string AddParserReadingsAsUnknownInAllChosenTextsAccessibleName => Text("MotifApp.AddParserReadingsAsUnknownInAllChosenTextsAccessibleName");

    public static string MarkSpellingsIncorrectInAllChosen => Text("MotifApp.MarkSpellingsIncorrectInAllChosen");

    public static string MarkSpellingsIncorrectInAllChosenAccessibleName => Text("MotifApp.MarkSpellingsIncorrectInAllChosenAccessibleName");

    public static string UndoChangesInAllChosenTexts => Text("MotifApp.UndoChangesInAllChosenTexts");

    public static string UndoChangesInAllChosenTextsAccessibleName => Text("MotifApp.UndoChangesInAllChosenTextsAccessibleName");

    public static string CheckUncertainChangesAgainAccessibleName => Text("MotifApp.CheckUncertainChangesAgainAccessibleName");

    public static string StageRemovalFromAllChosenTexts => Text("MotifApp.StageRemovalFromAllChosenTexts");

    public static string StageRemovalOfTheAnalysesUsedInAllChosenTextsAccessibleName => Text("MotifApp.StageRemovalOfTheAnalysesUsedInAllChosenTextsAccessibleName");

    public static string Read => Text("MotifApp.Read");

    public static string MarkReadOrUnreadAccessibleName => Text("MotifApp.MarkReadOrUnreadAccessibleName");

    public static string MarkThisTextAsRead => Text("MotifApp.MarkThisTextAsRead");

    public static string MarkTheSelectedTextAsReadAccessibleName => Text("MotifApp.MarkTheSelectedTextAsReadAccessibleName");

    public static string MarkThisTextAsUnread => Text("MotifApp.MarkThisTextAsUnread");

    public static string MarkTheSelectedTextAsUnreadAccessibleName => Text("MotifApp.MarkTheSelectedTextAsUnreadAccessibleName");

    public static string SelectedOccurrenceCountAccessibleName => Text("MotifApp.SelectedOccurrenceCountAccessibleName");

    public static string MarkSelectedAsRead => Text("MotifApp.MarkSelectedAsRead");

    public static string MarkSelectedOccurrencesAsReadAccessibleName => Text("MotifApp.MarkSelectedOccurrencesAsReadAccessibleName");

    public static string MarkSelectedAsUnread => Text("MotifApp.MarkSelectedAsUnread");

    public static string MarkSelectedOccurrencesAsUnreadAccessibleName => Text("MotifApp.MarkSelectedOccurrencesAsUnreadAccessibleName");

    public static string ChooseTexts => Text("MotifApp.ChooseTexts");

    public static string ChooseTextsToAnalyzeAccessibleName => Text("MotifApp.ChooseTextsToAnalyzeAccessibleName");

    public static string TextLinesAndParserResultsAccessibleName => Text("MotifApp.TextLinesAndParserResultsAccessibleName");

    public static string Word => Text("MotifApp.Word");

    public static string FieldWorks => Text("MotifApp.FieldWorks");

    public static string PanGloss => Text("MotifApp.PanGloss");

    public static string InspectorKeyHint => Text("MotifApp.InspectorKeyHint");

    public static string CloseTheWordCardAccessibleName => Text("MotifApp.CloseTheWordCardAccessibleName");

    public static string WordListsAccessibleName => Text("MotifApp.WordListsAccessibleName");

    public static string OrderTheWordsInTheSelectedAccessibleName => Text("MotifApp.OrderTheWordsInTheSelectedAccessibleName");

    public static string SearchWordsInListsAccessibleName => Text("MotifApp.SearchWordsInListsAccessibleName");

    public static string ParseTheSelectedListSStoppedAccessibleName => Text("MotifApp.ParseTheSelectedListSStoppedAccessibleName");

    public static string WordsInTheSelectedListAccessibleName => Text("MotifApp.WordsInTheSelectedListAccessibleName");

    public static string ChooseAListAboveToSee => Text("MotifApp.ChooseAListAboveToSee");

    public static string SearchWordsAccessibleName => Text("MotifApp.SearchWordsAccessibleName");

    public static string WordsToTestAccessibleName => Text("MotifApp.WordsToTestAccessibleName");

    public static string Texts => Text("MotifApp.Texts");

    public static string SelectAllTexts => Text("MotifApp.SelectAllTexts");

    public static string SelectAllTextsAccessibleName => Text("MotifApp.SelectAllTextsAccessibleName");

    public static string Clear => Text("MotifApp.Clear");

    public static string ClearAccessibleName => Text("MotifApp.ClearAccessibleName");

    public static string SearchTextsAccessibleName => Text("MotifApp.SearchTextsAccessibleName");

    public static string TextsToAnalyzeAccessibleName => Text("MotifApp.TextsToAnalyzeAccessibleName");

    public static string AddWords => Text("MotifApp.AddWords");

    public static string WordsToAnalyzeOnePerLineAccessibleName => Text("MotifApp.WordsToAnalyzeOnePerLineAccessibleName");

    public static string AnalysisOptions => Text("MotifApp.AnalysisOptions");

    public static string AllWordforms => Text("MotifApp.AllWordforms");

    public static string RetryFailed => Text("MotifApp.RetryFailed");

    public static string DefaultSelectionParsingLimits => Text("MotifApp.DefaultSelectionParsingLimits");

    public static string StepLimitPerWord => Text("MotifApp.StepLimitPerWord");

    public static string SelectionStepLimitPerWordAccessibleName => Text("MotifApp.SelectionStepLimitPerWordAccessibleName");

    public static string NoStepLimit => Text("MotifApp.NoStepLimit");

    public static string SelectionHasNoStepLimitAccessibleName => Text("MotifApp.SelectionHasNoStepLimitAccessibleName");

    public static string TimeLimitPerWordSeconds => Text("MotifApp.TimeLimitPerWordSeconds");

    public static string SelectionTimeLimitPerWordInAccessibleName => Text("MotifApp.SelectionTimeLimitPerWordInAccessibleName");

    public static string UseMotifSEstimate => Text("MotifApp.UseMotifSEstimate");

    public static string UseMotifSEstimatedTimeLimitAccessibleName => Text("MotifApp.UseMotifSEstimatedTimeLimitAccessibleName");

    public static string SaveADefaultSelectionToEdit => Text("MotifApp.SaveADefaultSelectionToEdit");

    public static string RetrySlowerThanMs => Text("MotifApp.RetrySlowerThanMs");

    public static string RetrySlowerThanInMillisecondsAccessibleName => Text("MotifApp.RetrySlowerThanInMillisecondsAccessibleName");

    public static string WhyApplyIsUnavailableAccessibleName => Text("MotifApp.WhyApplyIsUnavailableAccessibleName");

    public static string AppliedToTheFieldWorksProject => Text("MotifApp.AppliedToTheFieldWorksProject");

    public static string WhatApplyChecksAccessibleName => Text("MotifApp.WhatApplyChecksAccessibleName");

    public static string ShowThem => Text("MotifApp.ShowThem");

    public static string ShowChangesThatNoLongerFitAccessibleName => Text("MotifApp.ShowChangesThatNoLongerFitAccessibleName");

    public static string FieldWorksMustBeClosedOrIt => Text("MotifApp.FieldWorksMustBeClosedOrIt");

    public static string WhatBlocksApplyAccessibleName => Text("MotifApp.WhatBlocksApplyAccessibleName");

    public static string ApplyToFieldWorksProjectAccessibleName => Text("MotifApp.ApplyToFieldWorksProjectAccessibleName");

    public static string Decline => Text("MotifApp.Decline");

    public static string Show => Text("MotifApp.Show");

    public static string ShowChangesAccessibleName => Text("MotifApp.ShowChangesAccessibleName");

    public static string AllChanges => Text("MotifApp.AllChanges");

    public static string NotLookedAtYet => Text("MotifApp.NotLookedAtYet");

    public static string StillFit => Text("MotifApp.StillFit");

    public static string NeedADecision => Text("MotifApp.NeedADecision");

    public static string Previous => Text("MotifApp.Previous");

    public static string Next => Text("MotifApp.Next");

    public static string Keep => Text("MotifApp.Keep");

    public static string Undo => Text("MotifApp.Undo");

    public static string ReviewAllChangesAccessibleName => Text("MotifApp.ReviewAllChangesAccessibleName");

    public static string ReviewAllChangesTip => Text("MotifApp.ReviewAllChangesTip");

    public static string ChooseAWordInAnalyzeTexts => Text("MotifApp.ChooseAWordInAnalyzeTexts");

    public static string GoToAnalyzeTexts => Text("MotifApp.GoToAnalyzeTexts");

    public static string WordChange => Text("MotifApp.WordChange");

    public static string OpinionChange => Text("MotifApp.OpinionChange");

    public static string Analysis => Text("MotifApp.Analysis");

    public static string PanGlossEffects => Text("MotifApp.PanGlossEffects");

    public static string Where => Text("MotifApp.Where");

    public static string Fits => Text("MotifApp.Fits");

    public static string KeepUndo => Text("MotifApp.KeepUndo");

    public static string ChangesGroupedForReviewAccessibleName => Text("MotifApp.ChangesGroupedForReviewAccessibleName");

    public static string CheckAllChangesAgainAgainstTheAccessibleName => Text("MotifApp.CheckAllChangesAgainAgainstTheAccessibleName");

    public static string UndoAll => Text("MotifApp.UndoAll");

    public static string LookedAtAccessibleName => Text("MotifApp.LookedAtAccessibleName");

    public static string LookedAtTip => Text("MotifApp.LookedAtTip");

    public static string Project => Text("MotifApp.Project");

    public static string Details => Text("MotifApp.Details");

    public static string DetailsForThisChangeAccessibleName => Text("MotifApp.DetailsForThisChangeAccessibleName");

    public static string SelectAChangeToSeeIts => Text("MotifApp.SelectAChangeToSeeIts");

    public static string NowReads => Text("MotifApp.NowReads");

    public static string RefreshedSentenceAccessibleName => Text("MotifApp.RefreshedSentenceAccessibleName");

    public static string SoundRuleReplacementReviewAccessibleName => Text("MotifApp.SoundRuleReplacementReviewAccessibleName");

    public static string ReviewSoundChanges => Text("MotifApp.ReviewSoundChanges");

    public static string SoundChangesAndAffectedFormsAccessibleName => Text("MotifApp.SoundChangesAndAffectedFormsAccessibleName");

    public static string TestedReadings => Text("MotifApp.TestedReadings");

    public static string BeforeAndAfterEvidenceForEachAccessibleName => Text("MotifApp.BeforeAndAfterEvidenceForEachAccessibleName");


    public static string OtherChanges => Text("MotifApp.OtherChanges");

    public static string SomeObservedChangesCouldNotBe => Text("MotifApp.SomeObservedChangesCouldNotBe");

    public static string SomeChangesNeedFurtherReview => Text("MotifApp.SomeChangesNeedFurtherReview");

    public static string DestinationNeeded => Text("MotifApp.DestinationNeeded");

    public static string SomeReviewDetailsCouldNotBe => Text("MotifApp.SomeReviewDetailsCouldNotBe");

    public static string ReadingProposals => Text("MotifApp.ReadingProposals");

    public static string NoProposalsYet => Text("MotifApp.NoProposalsYet");

    public static string ShowAllProposals => Text("MotifApp.ShowAllProposals");

    public static string NoProposalsToShow => Text("MotifApp.NoProposalsToShow");

    public static string SelectAProposal => Text("MotifApp.SelectAProposal");

    public static string Status => Text("MotifApp.Status");

    public static string BuiltBy => Text("MotifApp.BuiltBy");

    public static string ItsChanges => Text("MotifApp.ItsChanges");

    public static string ProposalNumbersHeading => Text("MotifApp.ProposalNumbersHeading");

    public static string OpenInReviewChanges => Text("MotifApp.OpenInReviewChanges");

    public static string Reopen => Text("MotifApp.Reopen");

    public static string Delete => Text("MotifApp.Delete");

    public static string Proposals => Text("MotifApp.Proposals");

    public static string EachOneIsTriedOnA => Text("MotifApp.EachOneIsTriedOnA");

    public static string NoProposalsYetChangesYouCollect => Text("MotifApp.NoProposalsYetChangesYouCollect");

    public static string Proposal => Text("MotifApp.Proposal");

    public static string ApprovedKept => Text("MotifApp.ApprovedKept");

    public static string Coverage => Text("MotifApp.Coverage");

    public static string Warnings => Text("MotifApp.Warnings");

    public static string WordTime => Text("MotifApp.WordTime");

    public static string ProposalsAccessibleName => Text("MotifApp.ProposalsAccessibleName");

    public static string Open => Text("MotifApp.Open");

    public static string InspectorAccessibleName => Text("MotifApp.InspectorAccessibleName");

    public static string TryAWord => Text("MotifApp.TryAWord");

    public static string CloseTheInspectorAccessibleName => Text("MotifApp.CloseTheInspectorAccessibleName");

    public static string CloseEscStepsBackTip => Text("MotifApp.CloseEscStepsBackTip");

    public static string BreadcrumbAccessibleName => Text("MotifApp.BreadcrumbAccessibleName");

    public static string Reading => Text("MotifApp.Reading");

    public static string InThisTraceAccessibleName => Text("MotifApp.InThisTraceAccessibleName");

    public static string FromThisTrace => Text("MotifApp.FromThisTrace");

    public static string WhatItIsAccessibleName => Text("MotifApp.WhatItIsAccessibleName");

    public static string WhatItIs => Text("MotifApp.WhatItIs");

    public static string YourWordsThatUseItAccessibleName => Text("MotifApp.YourWordsThatUseItAccessibleName");

    public static string YourWordsThatUseIt => Text("MotifApp.YourWordsThatUseIt");

    public static string FromTheLastParseAllWords => Text("MotifApp.FromTheLastParseAllWords");

    public static string GrammarWarningsAboutItAccessibleName => Text("MotifApp.GrammarWarningsAboutItAccessibleName");

    public static string GrammarWarningsAboutIt => Text("MotifApp.GrammarWarningsAboutIt");

    public static string FromTheLastGrammarCheck => Text("MotifApp.FromTheLastGrammarCheck");

    public static string OpenWarnings => Text("MotifApp.OpenWarnings");

    public static string InFieldWorksAccessibleName => Text("MotifApp.InFieldWorksAccessibleName");

    public static string InFieldWorks => Text("MotifApp.InFieldWorks");

    public static string FromTheBaseline => Text("MotifApp.FromTheBaseline");

    public static string CollapseSideBarAccessibleName => Text("MotifApp.CollapseSideBarAccessibleName");

    public static string CollapseSideBarTip => Text("MotifApp.CollapseSideBarTip");

    public static string ShowSideBarAccessibleName => Text("MotifApp.ShowSideBarAccessibleName");

    public static string DetailsAccessibleName => Text("MotifApp.DetailsAccessibleName");

    public static string HideInspectorAccessibleName => Text("MotifApp.HideInspectorAccessibleName");

    public static string HideInspectorTip => Text("MotifApp.HideInspectorTip");

    public static string InspectorHeading => Text("MotifApp.InspectorHeading");

    public static string SelectAnItemToInspect => Text("MotifApp.SelectAnItemToInspect");

    public static string ShowInspectorAccessibleName => Text("MotifApp.ShowInspectorAccessibleName");

    public static string ShowInspectorTip => Text("MotifApp.ShowInspectorTip");

    public static string ChangesMayAlreadyBeAppliedPattern => Text("MotifApp.ChangesMayAlreadyBeAppliedPattern");

    public static string ChangesMayAlreadyBeApplied => Text("MotifApp.ChangesMayAlreadyBeApplied");

    public static string NoChangesYet => Text("MotifApp.NoChangesYet");

    public static string NoChanges => Text("MotifApp.NoChanges");

    public static string ChangeCountPattern => Text("MotifApp.ChangeCountPattern");

    public static string WordCountPattern => Text("MotifApp.WordCountPattern");

    public static string ProposalCountPattern => Text("MotifApp.ProposalCountPattern");

    public static string ProblemFindingFormat => Text("MotifApp.ProblemFindingFormat");

    public static string WordGroupCountPattern => Text("MotifApp.WordGroupCountPattern");

    public static string ChangeGroupCountPattern => Text("MotifApp.ChangeGroupCountPattern");

    public static string ProposalChangeCountFormat => Text("MotifApp.ProposalChangeCountFormat");

    public static string TryWordLoadingGrammar => Text("MotifApp.TryWordLoadingGrammar");

    public static string Settings => Text("MotifApp.Settings");

    public static string Back => Text("MotifApp.Back");

    public static string LeaveSettingsAccessibleName => Text("MotifApp.LeaveSettingsAccessibleName");

    public static string Display => Text("MotifApp.Display");

    public static string ResetSavedPreferences => Text("MotifApp.ResetSavedPreferences");

    public static string ResetSavedPreferencesAccessibleName => Text("MotifApp.ResetSavedPreferencesAccessibleName");

    public static string Zoom => Text("MotifApp.Zoom");

    public static string ZoomOutAccessibleName => Text("MotifApp.ZoomOutAccessibleName");

    public static string ZoomInAccessibleName => Text("MotifApp.ZoomInAccessibleName");

    public static string ResetTo100 => Text("MotifApp.ResetTo100");

    public static string ResetZoomTo100AccessibleName => Text("MotifApp.ResetZoomTo100AccessibleName");

    public static string Theme => Text("MotifApp.Theme");

    public static string ThemeRememberedCaption => Text("MotifApp.ThemeRememberedCaption");

    public static string Light => Text("MotifApp.Light");

    public static string LightThemeAccessibleName => Text("MotifApp.LightThemeAccessibleName");

    public static string Dark => Text("MotifApp.Dark");

    public static string DarkThemeAccessibleName => Text("MotifApp.DarkThemeAccessibleName");

    public static string MatchOperatingSystemThemeAccessibleName => Text("MotifApp.MatchOperatingSystemThemeAccessibleName");

    public static string Reset => Text("MotifApp.Reset");

    public static string ResetThemeToMatchOperatingAccessibleName => Text("MotifApp.ResetThemeToMatchOperatingAccessibleName");

    public static string KeyboardShortcuts => Text("MotifApp.KeyboardShortcuts");

    public static string ShortcutsWorkWithoutTextFocusCaption => Text("MotifApp.ShortcutsWorkWithoutTextFocusCaption");

    public static string AllShortcuts => Text("MotifApp.AllShortcuts");

    public static string SearchShortcutsAccessibleName => Text("MotifApp.SearchShortcutsAccessibleName");

    public static string ClearShortcutSearchAccessibleName => Text("MotifApp.ClearShortcutSearchAccessibleName");

    public static string OpenProjectForParsingLimitsPlaceholder => Text("MotifApp.OpenProjectForParsingLimitsPlaceholder");

    public static string ChooseWhatToParseForLimitsPlaceholder => Text("MotifApp.ChooseWhatToParseForLimitsPlaceholder");

    public static string StepLimitPerWordLabel => Text("MotifApp.StepLimitPerWordLabel");

    public static string StepLimitDefaultCaption => Text("MotifApp.StepLimitDefaultCaption");

    public static string ResetStepLimit => Text("MotifApp.ResetStepLimit");

    public static string ResetStepLimitTo200000AccessibleName => Text("MotifApp.ResetStepLimitTo200000AccessibleName");

    public static string NoStepLimitAccessibleName => Text("MotifApp.NoStepLimitAccessibleName");

    public static string TimeLimitPerWord => Text("MotifApp.TimeLimitPerWord");

    public static string TimeLimitFollowsStepLimitCaption => Text("MotifApp.TimeLimitFollowsStepLimitCaption");

    public static string TimeLimitPerWordInAccessibleName => Text("MotifApp.TimeLimitPerWordInAccessibleName");

    public static string UseMotifEstimateAccessibleName => Text("MotifApp.UseMotifEstimateAccessibleName");

    public static string DefaultProposalChecks => Text("MotifApp.DefaultProposalChecks");

    public static string CompareStoredAnalyses => Text("MotifApp.CompareStoredAnalyses");

    public static string CompareStoredAnalysesCaption => Text("MotifApp.CompareStoredAnalysesCaption");

    public static string StoredAnalysisWorkBudgetChoiceAccessibleName => Text("MotifApp.StoredAnalysisWorkBudgetChoiceAccessibleName");

    public static string StoredAnalysisWorkBudgetChoice => Text("MotifApp.StoredAnalysisWorkBudgetChoice");

    public static string StoredAnalysisWorkBudgetCaption => Text("MotifApp.StoredAnalysisWorkBudgetCaption");

    public static string StoredAnalysisEveryWordChoiceAccessibleName => Text("MotifApp.StoredAnalysisEveryWordChoiceAccessibleName");

    public static string StoredAnalysisEveryWordChoice => Text("MotifApp.StoredAnalysisEveryWordChoice");

    public static string StoredAnalysisEveryWordCaption => Text("MotifApp.StoredAnalysisEveryWordCaption");

    public static string StoredAnalysisOffChoiceAccessibleName => Text("MotifApp.StoredAnalysisOffChoiceAccessibleName");

    public static string StoredAnalysisOffCaption => Text("MotifApp.StoredAnalysisOffCaption");

    public static string ResetComparison => Text("MotifApp.ResetComparison");

    public static string ResetStoredAnalysisComparisonToAccessibleName => Text("MotifApp.ResetStoredAnalysisComparisonToAccessibleName");

    public static string ProposalChecksAppliesNextCaption => Text("MotifApp.ProposalChecksAppliesNextCaption");

    public static string OpenProjectForWritingSystemsPlaceholder => Text("MotifApp.OpenProjectForWritingSystemsPlaceholder");

    public static string Default => Text("MotifApp.Default");

    public static string WritingSystemFontPreviewCaption => Text("MotifApp.WritingSystemFontPreviewCaption");

    public static string Font => Text("MotifApp.Font");

    public static string FontIsMissingAccessibleName => Text("MotifApp.FontIsMissingAccessibleName");

    public static string Features => Text("MotifApp.Features");

    public static string Direction => Text("MotifApp.Direction");

    public static string Size => Text("MotifApp.Size");

    public static string WritingSystemLimitsFootnote => Text("MotifApp.WritingSystemLimitsFootnote");

    public static string ChangeInFieldWorksHeading => Text("MotifApp.ChangeInFieldWorksHeading");

    public static string WritingSystemSetupFieldWorksPath => Text("MotifApp.WritingSystemSetupFieldWorksPath");

    public static string WritingSystemSizeFieldWorksPath => Text("MotifApp.WritingSystemSizeFieldWorksPath");

    public static string WritingSystemRefreshHint => Text("MotifApp.WritingSystemRefreshHint");

    public static string TextsToParseLabel => Text("MotifApp.TextsToParseLabel");

    public static string UseAllTexts => Text("MotifApp.UseAllTexts");

    public static string UseAllTextsAccessibleName => Text("MotifApp.UseAllTextsAccessibleName");

    public static string OpenProjectForTextsPlaceholder => Text("MotifApp.OpenProjectForTextsPlaceholder");

    public static string HelpAndProblemReports => Text("MotifApp.HelpAndProblemReports");

    public static string Help => Text("MotifApp.Help");

    public static string OpenHelp => Text("MotifApp.OpenHelp");

    public static string GuideAndCommandPagesOnline => Text("MotifApp.GuideAndCommandPagesOnline");

    public static string OpenMotifSite => Text("MotifApp.OpenMotifSite");

    public static string OpenMotifSiteAccessibleName => Text("MotifApp.OpenMotifSiteAccessibleName");

    public static string WhatReportIncludes => Text("MotifApp.WhatReportIncludes");

    public static string WhatReportIncludesAccessibleName => Text("MotifApp.WhatReportIncludesAccessibleName");

    public static string ReportIncludesList => Text("MotifApp.ReportIncludesList");

    public static string AboutMotif => Text("MotifApp.AboutMotif");

    public static string Motif => Text("MotifApp.Motif");

    public static string PanGlossMissingCaption => Text("MotifApp.PanGlossMissingCaption");

    public static string OperatingSystem => Text("MotifApp.OperatingSystem");

    public static string WhereMotifKeepsItsData => Text("MotifApp.WhereMotifKeepsItsData");

    public static string OpenMotifDataFolderAccessibleName => Text("MotifApp.OpenMotifDataFolderAccessibleName");

    public static string MotifDataFolderCaption => Text("MotifApp.MotifDataFolderCaption");

    public static string CopyVersionsAccessibleName => Text("MotifApp.CopyVersionsAccessibleName");

    public static string MotifWritesNothingUntilApplyCaption => Text("MotifApp.MotifWritesNothingUntilApplyCaption");

    public static string SettingsGroupsAccessibleName => Text("MotifApp.SettingsGroupsAccessibleName");

    public static string SetupFoundProjectTitle => Text("MotifApp.SetupFoundProjectTitle");

    public static string SetupSelectionTitle => Text("MotifApp.SetupSelectionTitle");

    public static string SetupLimitsTitle => Text("MotifApp.SetupLimitsTitle");

    public static string SetupFoundProjectDescription => Text("MotifApp.SetupFoundProjectDescription");

    public static string SetupSelectionDescription => Text("MotifApp.SetupSelectionDescription");

    public static string SetupLimitsDescription => Text("MotifApp.SetupLimitsDescription");

    public static string SetupChoicesKeptCaption => Text("MotifApp.SetupChoicesKeptCaption");

    public static string SetupTextsOrderCaption => Text("MotifApp.SetupTextsOrderCaption");

    public static string SetupAddWordsLabel => Text("MotifApp.SetupAddWordsLabel");

    public static string OneWordPerLinePlaceholder => Text("MotifApp.OneWordPerLinePlaceholder");

    public static string WordsToAddAccessibleName => Text("MotifApp.WordsToAddAccessibleName");

    public static string SetupAttemptLimitPerWord => Text("MotifApp.SetupAttemptLimitPerWord");

    public static string SetupAttemptLimitDefaultCaption => Text("MotifApp.SetupAttemptLimitDefaultCaption");

    public static string NoAttemptLimit => Text("MotifApp.NoAttemptLimit");

    public static string SkipForNow => Text("MotifApp.SkipForNow");

    public static string SkipSetupForNowAccessibleName => Text("MotifApp.SkipSetupForNowAccessibleName");

    public static string MotifHasStoppedWindowTitle => Text("MotifApp.MotifHasStoppedWindowTitle");

    public static string ReviewProblemReportAccessibleName => Text("MotifApp.ReviewProblemReportAccessibleName");

    public static string Close => Text("MotifApp.Close");

    public static string CrashHeadlineAccessibleName => Text("MotifApp.CrashHeadlineAccessibleName");

    public static string CrashSummaryAccessibleName => Text("MotifApp.CrashSummaryAccessibleName");

    public static string CrashMessageAccessibleName => Text("MotifApp.CrashMessageAccessibleName");

    public static string CrashDetailsAccessibleName => Text("MotifApp.CrashDetailsAccessibleName");

    public static string CrashDetailsTextAccessibleName => Text("MotifApp.CrashDetailsTextAccessibleName");

    public static string ReviewProblemReportWindowTitle => Text("MotifApp.ReviewProblemReportWindowTitle");

    public static string ProblemReportStatusAccessibleName => Text("MotifApp.ProblemReportStatusAccessibleName");

    public static string CopyReport => Text("MotifApp.CopyReport");

    public static string CopyReviewedProblemReportAccessibleName => Text("MotifApp.CopyReviewedProblemReportAccessibleName");

    public static string OpenIssueForm => Text("MotifApp.OpenIssueForm");

    public static string OpenIssueFormWithReviewedAccessibleName => Text("MotifApp.OpenIssueFormWithReviewedAccessibleName");

    public static string CloseProblemReportPreviewAccessibleName => Text("MotifApp.CloseProblemReportPreviewAccessibleName");

    public static string ProblemReportPreviewIntro => Text("MotifApp.ProblemReportPreviewIntro");

    public static string IncludeLocalDetails => Text("MotifApp.IncludeLocalDetails");

    public static string IncludeLocalDetailsAccessibleName => Text("MotifApp.IncludeLocalDetailsAccessibleName");

    public static string ProblemReportPreviewAccessibleName => Text("MotifApp.ProblemReportPreviewAccessibleName");

    public static string OpenOnline => Text("MotifApp.OpenOnline");

    public static string OpenThisHelpPageOnlineAccessibleName => Text("MotifApp.OpenThisHelpPageOnlineAccessibleName");

    public static string FontSourceFallback => Text("MotifApp.FontSourceFallback");

    public static string FontSourceBundled => Text("MotifApp.FontSourceBundled");

    public static string FontSourceInstalled => Text("MotifApp.FontSourceInstalled");

    public static string AlsoInVernacular => Text("MotifApp.AlsoInVernacular");

    public static string FontNoneSet => Text("MotifApp.FontNoneSet");

    public static string FontFeaturesNone => Text("MotifApp.FontFeaturesNone");

    public static string DirectionRightToLeft => Text("MotifApp.DirectionRightToLeft");

    public static string DirectionLeftToRight => Text("MotifApp.DirectionLeftToRight");

    public static string StyleSizeFormat => Text("MotifApp.StyleSizeFormat");

    public static string MissingFontCaptionFormat => Text("MotifApp.MissingFontCaptionFormat");

    public static string WritingSystems => Text("MotifApp.WritingSystems");

    public static string MatchWindows => Text("MotifApp.MatchWindows");

    public static string MatchMacOS => Text("MotifApp.MatchMacOS");

    public static string MatchSystem => Text("MotifApp.MatchSystem");

    public static string HelpCaption => Text("MotifApp.HelpCaption");

    public static string ReportCaption => Text("MotifApp.ReportCaption");

    public static string ShowInExplorer => Text("MotifApp.ShowInExplorer");

    public static string ShowInFinder => Text("MotifApp.ShowInFinder");

    public static string ParsingLeadFormat => Text("MotifApp.ParsingLeadFormat");

    public static string TextSelectionSummaryFormat => Text("MotifApp.TextSelectionSummaryFormat");

    public static string TextsSelectedPattern => Text("MotifApp.TextsSelectedPattern");

    public static string NoShortcutMatchesFormat => Text("MotifApp.NoShortcutMatchesFormat");

    public static string ProposalChecksLead => Text("MotifApp.ProposalChecksLead");

    public static string ParsingForProjectFormat => Text("MotifApp.ParsingForProjectFormat");

    public static string WritingSystemsInProjectFormat => Text("MotifApp.WritingSystemsInProjectFormat");

    public static string WritingSystemsLeadFormat => Text("MotifApp.WritingSystemsLeadFormat");

    public static string WritingSystemsEmptyRefresh => Text("MotifApp.WritingSystemsEmptyRefresh");

    public static string WritingSystemsEmptyBaseline => Text("MotifApp.WritingSystemsEmptyBaseline");

    public static string StepLimitEstimateDefault => Text("MotifApp.StepLimitEstimateDefault");

    public static string TimeLimitPositiveRefusal => Text("MotifApp.TimeLimitPositiveRefusal");

    public static string ParsingFooterRunning => Text("MotifApp.ParsingFooterRunning");

    public static string ParsingFooterIdle => Text("MotifApp.ParsingFooterIdle");

    public static string StoredAnalysesSaveRefusalFormat => Text("MotifApp.StoredAnalysesSaveRefusalFormat");

    public static string StepLimitWholeNumberRefusal => Text("MotifApp.StepLimitWholeNumberRefusal");

    public static string TimeLimitRangeRefusal => Text("MotifApp.TimeLimitRangeRefusal");

    public static string Copied => Text("MotifApp.Copied");

    public static string CopyVersions => Text("MotifApp.CopyVersions");

    public static string EnterPositiveWholeNumberOfAttempts => Text("MotifApp.EnterPositiveWholeNumberOfAttempts");

    public static string RunningNoStepLimit => Text("MotifApp.RunningNoStepLimit");

    public static string RunningStepLimitFormat => Text("MotifApp.RunningStepLimitFormat");

    public static string SetupNextLimits => Text("MotifApp.SetupNextLimits");

    public static string SetupNextFirstRun => Text("MotifApp.SetupNextFirstRun");

    public static string SetupNextTexts => Text("MotifApp.SetupNextTexts");

    public static string SetupStepCaptionFormat => Text("MotifApp.SetupStepCaptionFormat");

    public static string SetupNoAttemptLimitEstimate => Text("MotifApp.SetupNoAttemptLimitEstimate");

    public static string EstimateUnderOneMillisecond => Text("MotifApp.EstimateUnderOneMillisecond");

    public static string EstimateMillisecondsFormat => Text("MotifApp.EstimateMillisecondsFormat");

    public static string EstimateSecondsFormat => Text("MotifApp.EstimateSecondsFormat");

    public static string NoAddedWords => Text("MotifApp.NoAddedWords");

    public static string UseThisSelection => Text("MotifApp.UseThisSelection");

    public static string StartFirstRun => Text("MotifApp.StartFirstRun");

    public static string SetupUseSelectionTitle => Text("MotifApp.SetupUseSelectionTitle");

    public static string SetupFirstRunTitle => Text("MotifApp.SetupFirstRunTitle");

    public static string SetupUseSelectionDescription => Text("MotifApp.SetupUseSelectionDescription");

    public static string SetupFirstRunDescription => Text("MotifApp.SetupFirstRunDescription");

    public static string SetupChooseTextOrWord => Text("MotifApp.SetupChooseTextOrWord");

    public static string SetupEstimateUnlimitedTypicalFormat => Text("MotifApp.SetupEstimateUnlimitedTypicalFormat");

    public static string SetupEstimateUnlimitedMeasuredFormat => Text("MotifApp.SetupEstimateUnlimitedMeasuredFormat");

    public static string SetupEstimateLimitedTypicalFormat => Text("MotifApp.SetupEstimateLimitedTypicalFormat");

    public static string SetupEstimateLimitedMeasuredFormat => Text("MotifApp.SetupEstimateLimitedMeasuredFormat");

    public static string CrashHeadline => Text("MotifApp.CrashHeadline");

    public static string CrashSummary => Text("MotifApp.CrashSummary");

    public static string CrashThankYou => Text("MotifApp.CrashThankYou");

    public static string ProblemReportCopied => Text("MotifApp.ProblemReportCopied");

    public static string ProblemReportCopyFailed => Text("MotifApp.ProblemReportCopyFailed");

    public static string ProblemReportIssueFormOpened => Text("MotifApp.ProblemReportIssueFormOpened");

    public static string CancelSetupAccessibleName => Text("MotifApp.CancelSetupAccessibleName");

    public static string WritingSystemDetailsAccessibleNameFormat => Text("MotifApp.WritingSystemDetailsAccessibleNameFormat");

    public static string VernacularSectionLabel => Text("MotifApp.VernacularSectionLabel");

    public static string AnalysisSectionLabel => Text("MotifApp.AnalysisSectionLabel");

    public static string SetupAttemptLimitPerWordAccessibleName => Text("MotifApp.SetupAttemptLimitPerWordAccessibleName");

    public static string SetupCancel => Text("MotifApp.SetupCancel");

    public static string AiHandoffHeading => Text("MotifApp.AiHandoffHeading");

    public static string WriteTheAIHandoffFolderAccessibleName => Text("MotifApp.WriteTheAIHandoffFolderAccessibleName");

    public static string KindOfFinding => Text("MotifApp.KindOfFinding");

    public static string ParsimonyDecisionLabel => Text("MotifApp.ParsimonyDecisionLabel");

    public static string NotebookRecordTypeForThisDecision => Text("MotifApp.NotebookRecordTypeForThisDecision");

    public static string ParsimonyReportEarlierBaselineNotice => Text("MotifApp.ParsimonyReportEarlierBaselineNotice");

    public static string ParsimonyFindingLabel => Text("MotifApp.ParsimonyFindingLabel");

    public static string ParsimonyCountedLabel => Text("MotifApp.ParsimonyCountedLabel");

    public static string StrengthOfEvidence => Text("MotifApp.StrengthOfEvidence");

    public static string PairedParserCheck => Text("MotifApp.PairedParserCheck");

    public static string ParsimonyNothingSuppressed => Text("MotifApp.ParsimonyNothingSuppressed");

    public static string Judgment => Text("MotifApp.Judgment");

    public static string ParsimonyReasonLabel => Text("MotifApp.ParsimonyReasonLabel");

    public static string ParsimonyStateLabel => Text("MotifApp.ParsimonyStateLabel");

    public static string ParsimonyActionLabel => Text("MotifApp.ParsimonyActionLabel");

    public static string Defer => Text("MotifApp.Defer");

    public static string Ask => Text("MotifApp.Ask");

    public static string ReturnToActive => Text("MotifApp.ReturnToActive");

    public static string ParsimonyReviewChanges => Text("MotifApp.ParsimonyReviewChanges");

    public static string ParsimonyReturnLabel => Text("MotifApp.ParsimonyReturnLabel");

    public static string FindingsNotAvailableAccessibleName => Text("MotifApp.FindingsNotAvailableAccessibleName");

    public static string FilterByKindAccessibleName => Text("MotifApp.FilterByKindAccessibleName");

    public static string FilterByDecisionAccessibleName => Text("MotifApp.FilterByDecisionAccessibleName");

    public static string FindingsStillLoadingAccessibleName => Text("MotifApp.FindingsStillLoadingAccessibleName");

    public static string FindingEvidenceAccessibleName => Text("MotifApp.FindingEvidenceAccessibleName");

    public static string DecideThisFindingAccessibleName => Text("MotifApp.DecideThisFindingAccessibleName");

    public static string NotebookRecordTypeAccessibleName => Text("MotifApp.NotebookRecordTypeAccessibleName");

    public static string ReasonAccessibleName => Text("MotifApp.ReasonAccessibleName");

    public static string QuestionAccessibleName => Text("MotifApp.QuestionAccessibleName");

    public static string SavedDecisionAccessibleName => Text("MotifApp.SavedDecisionAccessibleName");

    public static string ParsimonyStateAccessibleName => Text("MotifApp.ParsimonyStateAccessibleName");

    public static string MeasuredFromAnEarlierBaselineAccessibleName => Text("MotifApp.MeasuredFromAnEarlierBaselineAccessibleName");

    public static string EvidenceNotStoredAccessibleName => Text("MotifApp.EvidenceNotStoredAccessibleName");

    public static string JudgmentsNeedingALookAccessibleName => Text("MotifApp.JudgmentsNeedingALookAccessibleName");

    public static string IncompleteReportAccessibleName => Text("MotifApp.IncompleteReportAccessibleName");

    public static string ActiveFindingsAccessibleName => Text("MotifApp.ActiveFindingsAccessibleName");

    public static string SuppressedDecisionsAccessibleName => Text("MotifApp.SuppressedDecisionsAccessibleName");

    public static string TotalWordTime => Text("MotifApp.TotalWordTime");

    public static string MedianWordTime => Text("MotifApp.MedianWordTime");

    public static string TimingMatrixCellHeading => Text("MotifApp.TimingMatrixCellHeading");

    public static string ListFromTexts => Text("MotifApp.ListFromTexts");

    public static string SecondsPerWord => Text("MotifApp.SecondsPerWord");

    public static string AnalysisAttemptLimit => Text("MotifApp.AnalysisAttemptLimit");

    public static string ReadingTheParseTimes => Text("MotifApp.ReadingTheParseTimes");

    public static string TimingBaselineStaleNotice => Text("MotifApp.TimingBaselineStaleNotice");

    public static string TimingRuleColumnHeading => Text("MotifApp.TimingRuleColumnHeading");

    public static string TimingKindColumnHeading => Text("MotifApp.TimingKindColumnHeading");

    public static string TimingTimeColumnHeading => Text("MotifApp.TimingTimeColumnHeading");

    public static string TimingWordsColumnHeading => Text("MotifApp.TimingWordsColumnHeading");

    public static string Inspect => Text("MotifApp.Inspect");

    public static string HowLongTheWordsTook => Text("MotifApp.HowLongTheWordsTook");

    public static string Slowest => Text("MotifApp.Slowest");

    public static string TimingSourceMatrixCellOption => Text("MotifApp.TimingSourceMatrixCellOption");

    public static string UseCell => Text("MotifApp.UseCell");

    public static string AList => Text("MotifApp.AList");

    public static string UseList => Text("MotifApp.UseList");

    public static string AIHandoffForTheseWords => Text("MotifApp.AIHandoffForTheseWords");

    public static string More => Text("MotifApp.More");

    public static string ChosenInTexts => Text("MotifApp.ChosenInTexts");

    public static string PickWords => Text("MotifApp.PickWords");

    public static string ReRun => Text("MotifApp.ReRun");

    public static string ReRunChosenWords => Text("MotifApp.ReRunChosenWords");

    public static string ParseAgain => Text("MotifApp.ParseAgain");

    public static string ShowAllWords => Text("MotifApp.ShowAllWords");

    public static string AIHandoffForThisRule => Text("MotifApp.AIHandoffForThisRule");

    public static string TimingRuleWordsColumnTip => Text("MotifApp.TimingRuleWordsColumnTip");

    public static string TotalWordTimeAccessibleName => Text("MotifApp.TotalWordTimeAccessibleName");

    public static string TimingControlsAccessibleName => Text("MotifApp.TimingControlsAccessibleName");

    public static string AllTimedWordsAccessibleName => Text("MotifApp.AllTimedWordsAccessibleName");

    public static string NumberOfSlowestWordsAccessibleName => Text("MotifApp.NumberOfSlowestWordsAccessibleName");

    public static string WordsFromAMatrixCellAccessibleName => Text("MotifApp.WordsFromAMatrixCellAccessibleName");

    public static string MatrixCellFromTextsAccessibleName => Text("MotifApp.MatrixCellFromTextsAccessibleName");

    public static string UseCellAccessibleName => Text("MotifApp.UseCellAccessibleName");

    public static string WordsFromAListAccessibleName => Text("MotifApp.WordsFromAListAccessibleName");

    public static string ListFromTextsAccessibleName => Text("MotifApp.ListFromTextsAccessibleName");

    public static string UseListAccessibleName => Text("MotifApp.UseListAccessibleName");

    public static string AIHandoffForTheseWordsAccessibleName => Text("MotifApp.AIHandoffForTheseWordsAccessibleName");

    public static string MoreWaysToChooseWordsAccessibleName => Text("MotifApp.MoreWaysToChooseWordsAccessibleName");

    public static string ChosenInTextsAccessibleName => Text("MotifApp.ChosenInTextsAccessibleName");

    public static string WordsPickedByHandAccessibleName => Text("MotifApp.WordsPickedByHandAccessibleName");

    public static string PickWordsAccessibleName => Text("MotifApp.PickWordsAccessibleName");

    public static string ReRunWordsWithNewLimitsAccessibleName => Text("MotifApp.ReRunWordsWithNewLimitsAccessibleName");

    public static string SecondsPerWordAccessibleName => Text("MotifApp.SecondsPerWordAccessibleName");

    public static string PerWordAnalysisAttemptLimitAccessibleName => Text("MotifApp.PerWordAnalysisAttemptLimitAccessibleName");

    public static string ReRunChosenWordsAccessibleName => Text("MotifApp.ReRunChosenWordsAccessibleName");

    public static string ParseTheseWordsAgainToMeasureThemAccessibleName => Text("MotifApp.ParseTheseWordsAgainToMeasureThemAccessibleName");

    public static string ShowAllWordsInTimingAccessibleName => Text("MotifApp.ShowAllWordsInTimingAccessibleName");

    public static string WhatEveryShareIsOfAccessibleName => Text("MotifApp.WhatEveryShareIsOfAccessibleName");

    public static string ShareOfTotalWordTimeByKindAccessibleName => Text("MotifApp.ShareOfTotalWordTimeByKindAccessibleName");

    public static string TimingByKindAccessibleName => Text("MotifApp.TimingByKindAccessibleName");

    public static string CallsByKindAccessibleName => Text("MotifApp.CallsByKindAccessibleName");

    public static string TimingOverrunAccessibleName => Text("MotifApp.TimingOverrunAccessibleName");

    public static string TimingByRuleAccessibleName => Text("MotifApp.TimingByRuleAccessibleName");

    public static string OpenTheGrammarWarningAccessibleName => Text("MotifApp.OpenTheGrammarWarningAccessibleName");

    public static string WordsUnderThisRuleAccessibleName => Text("MotifApp.WordsUnderThisRuleAccessibleName");

    public static string NotCheckedYet => Text("MotifApp.NotCheckedYet");

    public static string WarningsCheckExplanation => Text("MotifApp.WarningsCheckExplanation");

    public static string CheckTheGrammar => Text("MotifApp.CheckTheGrammar");

    public static string GrammarNotCheckedYetAccessibleName => Text("MotifApp.GrammarNotCheckedYetAccessibleName");

    public static string CheckTheGrammarAccessibleName => Text("MotifApp.CheckTheGrammarAccessibleName");

    public static string CompareRowsNotSortedNotice => Text("MotifApp.CompareRowsNotSortedNotice");

    public static string CompareMatrixAxisLabel => Text("MotifApp.CompareMatrixAxisLabel");

    public static string FixTheseFirst => Text("MotifApp.FixTheseFirst");

    public static string WhatTheseWordsShare => Text("MotifApp.WhatTheseWordsShare");

    public static string AddAsUnknown => Text("MotifApp.AddAsUnknown");

    public static string IncorrectSpelling => Text("MotifApp.IncorrectSpelling");

    public static string CompareParseAgainTip => Text("MotifApp.CompareParseAgainTip");

    public static string CompareMatrixAccessibleName => Text("MotifApp.CompareMatrixAccessibleName");

    public static string FixTheseFirstCountAccessibleName => Text("MotifApp.FixTheseFirstCountAccessibleName");

    public static string WordsToCheckFirstAccessibleName => Text("MotifApp.WordsToCheckFirstAccessibleName");

    public static string WhatTheseWordsShareAccessibleName => Text("MotifApp.WhatTheseWordsShareAccessibleName");

    public static string OrderTheListedWordsAccessibleName => Text("MotifApp.OrderTheListedWordsAccessibleName");

    public static string SearchTheListedWordsAccessibleName => Text("MotifApp.SearchTheListedWordsAccessibleName");

    public static string CompareParseAgainAccessibleName => Text("MotifApp.CompareParseAgainAccessibleName");

    public static string SecondsEachWordGetsWhenRunAgainAccessibleName => Text("MotifApp.SecondsEachWordGetsWhenRunAgainAccessibleName");

    public static string AIHandoffForTheListedWordsAccessibleName => Text("MotifApp.AIHandoffForTheListedWordsAccessibleName");

    public static string AddCheckedWordsAsUnknownAccessibleName => Text("MotifApp.AddCheckedWordsAsUnknownAccessibleName");

    public static string MarkCheckedWordsAsIncorrectSpellingAccessibleName => Text("MotifApp.MarkCheckedWordsAsIncorrectSpellingAccessibleName");

    public static string WordsInTheChosenCellsAccessibleName => Text("MotifApp.WordsInTheChosenCellsAccessibleName");

    public static string HandoffIntro => Text("MotifApp.HandoffIntro");

    public static string WhatGoesIn => Text("MotifApp.WhatGoesIn");

    public static string GetAChatModelsHelp => Text("MotifApp.GetAChatModelsHelp");

    public static string CopyTheStarterPrompt => Text("MotifApp.CopyTheStarterPrompt");

    public static string HandoffStarterPromptDetail => Text("MotifApp.HandoffStarterPromptDetail");

    public static string OpenAChat => Text("MotifApp.OpenAChat");

    public static string ClaudeChatGPTOrGemini => Text("MotifApp.ClaudeChatGPTOrGemini");

    public static string AddTheFilesThenPaste => Text("MotifApp.AddTheFilesThenPaste");

    public static string HandoffAddFilesDetail => Text("MotifApp.HandoffAddFilesDetail");

    public static string HandoffTryAskingHeading => Text("MotifApp.HandoffTryAskingHeading");

    public static string HandoffTryAskingDetail => Text("MotifApp.HandoffTryAskingDetail");

    public static string HandOffEveryParsedWordInstead => Text("MotifApp.HandOffEveryParsedWordInstead");

    public static string CopyPath => Text("MotifApp.CopyPath");

    public static string CopyStarterPrompt => Text("MotifApp.CopyStarterPrompt");

    public static string HandoffFolderTip => Text("MotifApp.HandoffFolderTip");

    public static string CopyThisQuestionTip => Text("MotifApp.CopyThisQuestionTip");

    public static string WordsChosenInCompareAccessibleName => Text("MotifApp.WordsChosenInCompareAccessibleName");

    public static string TheAIHandoffIsOutOfDateAccessibleName => Text("MotifApp.TheAIHandoffIsOutOfDateAccessibleName");

    public static string AIHandoffProgressAccessibleName => Text("MotifApp.AIHandoffProgressAccessibleName");

    public static string CancelTheRunningAIHandoffAccessibleName => Text("MotifApp.CancelTheRunningAIHandoffAccessibleName");

    public static string WhatTheAIHandoffWillContainAccessibleName => Text("MotifApp.WhatTheAIHandoffWillContainAccessibleName");

    public static string AIHandoffFilesAccessibleName => Text("MotifApp.AIHandoffFilesAccessibleName");

    public static string CopyTheStarterPromptAccessibleName => Text("MotifApp.CopyTheStarterPromptAccessibleName");

    public static string DifferenceEmptyState => Text("MotifApp.DifferenceEmptyState");

    public static string MovesBetweenCellsWorstFirstAccessibleName => Text("MotifApp.MovesBetweenCellsWorstFirstAccessibleName");

    public static string WordsInTheChosenMoveAccessibleName => Text("MotifApp.WordsInTheChosenMoveAccessibleName");

    public static string StatisticsStaleNotice => Text("MotifApp.StatisticsStaleNotice");

    public static string StatisticsGroupAccessibleName => Text("MotifApp.StatisticsGroupAccessibleName");

    public static string FilterStatisticsRowsAccessibleName => Text("MotifApp.FilterStatisticsRowsAccessibleName");

    public static string StatisticsWordScopeAccessibleName => Text("MotifApp.StatisticsWordScopeAccessibleName");

    public static string StatisticsRowsAccessibleName => Text("MotifApp.StatisticsRowsAccessibleName");

    public static string WarningsAreaLabel => Text("MotifApp.WarningsAreaLabel");

    public static string Severity => Text("MotifApp.Severity");

    public static string NamesYourWords => Text("MotifApp.NamesYourWords");

    public static string ShowFindingsWithExactUsesInYourWordsAccessibleName => Text("MotifApp.ShowFindingsWithExactUsesInYourWordsAccessibleName");

    public static string SelectedFinding => Text("MotifApp.SelectedFinding");

    public static string NamedWords => Text("MotifApp.NamedWords");

    public static string WhatIsWrong => Text("MotifApp.WhatIsWrong");

    public static string WhyPanGlossReportedIt => Text("MotifApp.WhyPanGlossReportedIt");

    public static string WordformsNotCompared => Text("MotifApp.WordformsNotCompared");

    public static string WhatToDoInFieldWorks => Text("MotifApp.WhatToDoInFieldWorks");

    public static string WarningNoAdviceYet => Text("MotifApp.WarningNoAdviceYet");

    public static string WarningWordsUsingNamedResource => Text("MotifApp.WarningWordsUsingNamedResource");

    public static string AfterYouFixIt => Text("MotifApp.AfterYouFixIt");

    public static string WarningAfterFixSteps => Text("MotifApp.WarningAfterFixSteps");

    public static string WarningsInspectorEmpty => Text("MotifApp.WarningsInspectorEmpty");

    public static string LearnMore => Text("MotifApp.LearnMore");

    public static string AIHandoffForThisWarning => Text("MotifApp.AIHandoffForThisWarning");

    public static string ParseTheseWordsAgain => Text("MotifApp.ParseTheseWordsAgain");

    public static string SelectedWarningDetailsAccessibleName => Text("MotifApp.SelectedWarningDetailsAccessibleName");

    public static string RecentWords => Text("MotifApp.RecentWords");

    public static string TryWordNoTraceToSave => Text("MotifApp.TryWordNoTraceToSave");

    public static string TryWordEmptyPrompt => Text("MotifApp.TryWordEmptyPrompt");

    public static string Tracing => Text("MotifApp.Tracing");

    public static string TryWordWhyItDidNotParseHeading => Text("MotifApp.TryWordWhyItDidNotParseHeading");

    public static string NoStepRecorded => Text("MotifApp.NoStepRecorded");

    public static string ParserLabel => Text("MotifApp.ParserLabel");

    public static string TryWordAttemptLabel => Text("MotifApp.TryWordAttemptLabel");

    public static string WhyItStopped => Text("MotifApp.WhyItStopped");

    public static string NoMorphemesRecorded => Text("MotifApp.NoMorphemesRecorded");

    public static string RecordedTrace => Text("MotifApp.RecordedTrace");

    public static string OtherRecordedEvent => Text("MotifApp.OtherRecordedEvent");

    public static string MeasuredObject => Text("MotifApp.MeasuredObject");

    public static string TimeLabel => Text("MotifApp.TimeLabel");

    public static string OpenASavedTrace => Text("MotifApp.OpenASavedTrace");

    public static string CopyForAChatModel => Text("MotifApp.CopyForAChatModel");

    public static string SaveTheTrace => Text("MotifApp.SaveTheTrace");

    public static string CopyTheTrace => Text("MotifApp.CopyTheTrace");

    public static string TryWordPlainTab => Text("MotifApp.TryWordPlainTab");

    public static string TryWordExpertTab => Text("MotifApp.TryWordExpertTab");

    public static string TryIt => Text("MotifApp.TryIt");

    public static string TryWordTimingLabel => Text("MotifApp.TryWordTimingLabel");

    public static string WordAnalysesInFieldWorks => Text("MotifApp.WordAnalysesInFieldWorks");

    public static string TryAWordToolsAccessibleName => Text("MotifApp.TryAWordToolsAccessibleName");

    public static string RecentWordsAccessibleName => Text("MotifApp.RecentWordsAccessibleName");

    public static string OpenASavedTraceAccessibleName => Text("MotifApp.OpenASavedTraceAccessibleName");

    public static string CopyAIDiagnosticInstructionsAccessibleName => Text("MotifApp.CopyAIDiagnosticInstructionsAccessibleName");

    public static string SaveFullDiagnosticJSONAccessibleName => Text("MotifApp.SaveFullDiagnosticJSONAccessibleName");

    public static string CopyFullDiagnosticJSONAccessibleName => Text("MotifApp.CopyFullDiagnosticJSONAccessibleName");

    public static string DiagnosticCaptureDetailsAccessibleName => Text("MotifApp.DiagnosticCaptureDetailsAccessibleName");

    public static string WritingSystemDirectionAndFontAccessibleName => Text("MotifApp.WritingSystemDirectionAndFontAccessibleName");

    public static string PlainTraceViewAccessibleName => Text("MotifApp.PlainTraceViewAccessibleName");

    public static string ExpertTraceViewAccessibleName => Text("MotifApp.ExpertTraceViewAccessibleName");

    public static string TryTheWordAccessibleName => Text("MotifApp.TryTheWordAccessibleName");

    public static string WordToTryAccessibleName => Text("MotifApp.WordToTryAccessibleName");

    public static string CancelTheTraceAccessibleName => Text("MotifApp.CancelTheTraceAccessibleName");

    public static string TryTheProposalAgainAccessibleName => Text("MotifApp.TryTheProposalAgainAccessibleName");

    public static string TryAWordResultAccessibleName => Text("MotifApp.TryAWordResultAccessibleName");

    public static string TryWordOpenInAnalyzeTextsAccessibleName => Text("MotifApp.TryWordOpenInAnalyzeTextsAccessibleName");

    public static string TimingForThisWordAccessibleName => Text("MotifApp.TimingForThisWordAccessibleName");

    public static string SearchCompletenessAccessibleName => Text("MotifApp.SearchCompletenessAccessibleName");

    public static string TryAWordSummaryAccessibleName => Text("MotifApp.TryAWordSummaryAccessibleName");

    public static string TryWordWhyItDidNotParseAccessibleName => Text("MotifApp.TryWordWhyItDidNotParseAccessibleName");

    public static string FieldWorksBesideTheParserAccessibleName => Text("MotifApp.FieldWorksBesideTheParserAccessibleName");

    public static string EveryStoredFieldWorksAnalysisAccessibleName => Text("MotifApp.EveryStoredFieldWorksAnalysisAccessibleName");

    public static string RecordedParserAnalysesAccessibleName => Text("MotifApp.RecordedParserAnalysesAccessibleName");

    public static string PathsTheParserDroppedAccessibleName => Text("MotifApp.PathsTheParserDroppedAccessibleName");

    public static string AttemptsThatGotFurthestAccessibleName => Text("MotifApp.AttemptsThatGotFurthestAccessibleName");

    public static string WhatStoppedTheAttemptAccessibleName => Text("MotifApp.WhatStoppedTheAttemptAccessibleName");

    public static string ShowEveryAttemptAccessibleName => Text("MotifApp.ShowEveryAttemptAccessibleName");

    public static string WhyItDidNotParseAccessibleName => Text("MotifApp.WhyItDidNotParseAccessibleName");

    public static string RecordedStoppedAttemptsAccessibleName => Text("MotifApp.RecordedStoppedAttemptsAccessibleName");

    public static string FullParserAnalysisDetailsAccessibleName => Text("MotifApp.FullParserAnalysisDetailsAccessibleName");

    public static string RecordedTraceContextAccessibleName => Text("MotifApp.RecordedTraceContextAccessibleName");

    public static string PlainTraceStepsAccessibleName => Text("MotifApp.PlainTraceStepsAccessibleName");

    public static string TimeFromAnEarlierParseAccessibleName => Text("MotifApp.TimeFromAnEarlierParseAccessibleName");

    public static string EarlierRuleTimesAccessibleName => Text("MotifApp.EarlierRuleTimesAccessibleName");

    public static string SelectedAttempt => Text("MotifApp.SelectedAttempt");

    public static string TryWordAttemptInspectorEmpty => Text("MotifApp.TryWordAttemptInspectorEmpty");

    public static string TryWordMembershipNotRecorded => Text("MotifApp.TryWordMembershipNotRecorded");

    public static string RecordedSearchWordAccessibleName => Text("MotifApp.RecordedSearchWordAccessibleName");

    public static string StepsOnTheSelectedAttemptAccessibleName => Text("MotifApp.StepsOnTheSelectedAttemptAccessibleName");

    public static string SelectedRecordedStepDetailsAccessibleName => Text("MotifApp.SelectedRecordedStepDetailsAccessibleName");

    public static string TheParsersCodeAccessibleName => Text("MotifApp.TheParsersCodeAccessibleName");

    public static string TraceLiveNavigationUnavailable => Text("MotifApp.TraceLiveNavigationUnavailable");

    public static string TraceRepeatedResultTip => Text("MotifApp.TraceRepeatedResultTip");

    public static string AnalysesAccessibleName => Text("MotifApp.AnalysesAccessibleName");

    public static string RecordedAnalysesAccessibleName => Text("MotifApp.RecordedAnalysesAccessibleName");

    public static string RecordedSourceAnalysesAccessibleName => Text("MotifApp.RecordedSourceAnalysesAccessibleName");

    public static string EventSource => Text("MotifApp.EventSource");

    public static string InputOutput => Text("MotifApp.InputOutput");

    public static string Outcome => Text("MotifApp.Outcome");

    public static string Notation => Text("MotifApp.Notation");

    public static string PhonologicalRulesApplied => Text("MotifApp.PhonologicalRulesApplied");

    public static string ExpertPhonologicalEventsNote => Text("MotifApp.ExpertPhonologicalEventsNote");

    public static string ExpertNoPhonologicalEvents => Text("MotifApp.ExpertNoPhonologicalEvents");

    public static string SelectedEvent => Text("MotifApp.SelectedEvent");

    public static string Readable => Text("MotifApp.Readable");

    public static string RecordedRejectionDetails => Text("MotifApp.RecordedRejectionDetails");

    public static string ParserRecord => Text("MotifApp.ParserRecord");

    public static string EnvironmentNotation => Text("MotifApp.EnvironmentNotation");

    public static string EnvironmentNotRecorded => Text("MotifApp.EnvironmentNotRecorded");

    public static string EveryStep => Text("MotifApp.EveryStep");

    public static string ExpertEveryStepTip => Text("MotifApp.ExpertEveryStepTip");

    public static string ExpertSubruleIndexTip => Text("MotifApp.ExpertSubruleIndexTip");

    public static string ExpertOccurrenceTip => Text("MotifApp.ExpertOccurrenceTip");

    public static string ExpertAttemptAccessibleName => Text("MotifApp.ExpertAttemptAccessibleName");

    public static string ExpertWholeTreeAccessibleName => Text("MotifApp.ExpertWholeTreeAccessibleName");

    public static string ExpertTraceEventsAccessibleName => Text("MotifApp.ExpertTraceEventsAccessibleName");

    public static string ExpertPhonologicalRulesAccessibleName => Text("MotifApp.ExpertPhonologicalRulesAccessibleName");

    public static string ExpertEnvironmentNotationAccessibleName => Text("MotifApp.ExpertEnvironmentNotationAccessibleName");

    public static string DeleteMotifsStoreAndReopen => Text("MotifApp.DeleteMotifsStoreAndReopen");

    public static string FatalGrammarIssuesAccessibleName => Text("MotifApp.FatalGrammarIssuesAccessibleName");

    public static string ReviewAProblemReportAccessibleName => Text("MotifApp.ReviewAProblemReportAccessibleName");

    public static string RefusalDeleteStoreAccessibleName => Text("MotifApp.RefusalDeleteStoreAccessibleName");

    public static string RefusalDetailsAccessibleName => Text("MotifApp.RefusalDetailsAccessibleName");

    public static string OriginalRefusalDetailsAccessibleName => Text("MotifApp.OriginalRefusalDetailsAccessibleName");

    public static string DeleteMotifsStore => Text("MotifApp.DeleteMotifsStore");

    public static string KeepMotifsStore => Text("MotifApp.KeepMotifsStore");

    public static string RefreshRefusalAccessibleName => Text("MotifApp.RefreshRefusalAccessibleName");

    public static string StoreRefusalConfirmDeleteAccessibleName => Text("MotifApp.StoreRefusalConfirmDeleteAccessibleName");

    public static string KeepMotifsStoreAccessibleName => Text("MotifApp.KeepMotifsStoreAccessibleName");

    public static string FieldWorksHoldsNoAnalysisForThisWord => Text("MotifApp.FieldWorksHoldsNoAnalysisForThisWord");

    public static string KeepFieldWorksAsItIs => Text("MotifApp.KeepFieldWorksAsItIs");

    public static string AddToFieldWorks => Text("MotifApp.AddToFieldWorks");

    public static string AddThemAllAsUnknown => Text("MotifApp.AddThemAllAsUnknown");

    public static string DispositionIncorrectSpellingTip => Text("MotifApp.DispositionIncorrectSpellingTip");

    public static string WhatFieldWorksHoldsForThisWordAccessibleName => Text("MotifApp.WhatFieldWorksHoldsForThisWordAccessibleName");

    public static string MarkThisSpellingIncorrectAccessibleName => Text("MotifApp.MarkThisSpellingIncorrectAccessibleName");

    public static string KeepFieldWorksAccessibleName => Text("MotifApp.KeepFieldWorksAccessibleName");

    public static string PanGlossAnalysesToAddToFieldWorksAccessibleName => Text("MotifApp.PanGlossAnalysesToAddToFieldWorksAccessibleName");

    public static string AddEveryPanGlossAnalysisAsUnknownAccessibleName => Text("MotifApp.AddEveryPanGlossAnalysisAsUnknownAccessibleName");

    public static string GrammarFindingsIntro => Text("MotifApp.GrammarFindingsIntro");

    public static string CheckingTheGrammar => Text("MotifApp.CheckingTheGrammar");

    public static string GrammarLoadingExplanation => Text("MotifApp.GrammarLoadingExplanation");

    public static string GrammarBaselineRequired => Text("MotifApp.GrammarBaselineRequired");

    public static string NoGrammarFindingsWereReported => Text("MotifApp.NoGrammarFindingsWereReported");

    public static string WarningsItemColumnHeading => Text("MotifApp.WarningsItemColumnHeading");

    public static string CheckTheGrammarAgain => Text("MotifApp.CheckTheGrammarAgain");

    public static string MostOfYourWordsFirst => Text("MotifApp.MostOfYourWordsFirst");

    public static string ReportOrder => Text("MotifApp.ReportOrder");

    public static string OpenDetails => Text("MotifApp.OpenDetails");

    public static string GrammarLoadingAccessibleName => Text("MotifApp.GrammarLoadingAccessibleName");

    public static string GrammarCheckRefusalAccessibleName => Text("MotifApp.GrammarCheckRefusalAccessibleName");

    public static string GrammarCheckFailedFormat => Text("MotifApp.GrammarCheckFailedFormat");

    public static string TryCheckingTheGrammarAgainAccessibleName => Text("MotifApp.TryCheckingTheGrammarAgainAccessibleName");

    public static string CheckTheGrammarAgainAccessibleName => Text("MotifApp.CheckTheGrammarAgainAccessibleName");

    public static string ChooseWarningSortOrderAccessibleName => Text("MotifApp.ChooseWarningSortOrderAccessibleName");

    public static string SortFindingsByExactWordUsesAccessibleName => Text("MotifApp.SortFindingsByExactWordUsesAccessibleName");

    public static string SortFindingsInReportOrderAccessibleName => Text("MotifApp.SortFindingsInReportOrderAccessibleName");

    public static string GrammarFindingsAccessibleName => Text("MotifApp.GrammarFindingsAccessibleName");

    public static string WordCardThisChangeLabel => Text("MotifApp.WordCardThisChangeLabel");

    public static string NothingInFieldWorks => Text("MotifApp.NothingInFieldWorks");

    public static string WordCardChangeSentence => Text("MotifApp.WordCardChangeSentence");

    public static string InFieldWorksNow => Text("MotifApp.InFieldWorksNow");

    public static string WordCardNothingInFieldWorksForWord => Text("MotifApp.WordCardNothingInFieldWorksForWord");

    public static string InPanGlossNow => Text("MotifApp.InPanGlossNow");

    public static string InPanGlossWithThisProposal => Text("MotifApp.InPanGlossWithThisProposal");

    public static string WordCardProposalBuildsNothing => Text("MotifApp.WordCardProposalBuildsNothing");

    public static string NotInFieldWorks => Text("MotifApp.NotInFieldWorks");

    public static string WordCardNoStoredWarning => Text("MotifApp.WordCardNoStoredWarning");

    public static string TimeByRule => Text("MotifApp.TimeByRule");

    public static string WhatToDo => Text("MotifApp.WhatToDo");

    public static string WordCardFieldWorksHoldsNothing => Text("MotifApp.WordCardFieldWorksHoldsNothing");

    public static string WordCardNotBuiltLabel => Text("MotifApp.WordCardNotBuiltLabel");

    public static string WordCardApprovedNotBuilt => Text("MotifApp.WordCardApprovedNotBuilt");

    public static string PanGlossHelp => Text("MotifApp.PanGlossHelp");

    public static string OpenTheWarning => Text("MotifApp.OpenTheWarning");

    public static string MarkAsUnread => Text("MotifApp.MarkAsUnread");

    public static string WordCardChangeSentenceAccessibleName => Text("MotifApp.WordCardChangeSentenceAccessibleName");

    public static string SentenceContextAccessibleName => Text("MotifApp.SentenceContextAccessibleName");

    public static string AboutPanGlossAccessibleName => Text("MotifApp.AboutPanGlossAccessibleName");

    public static string MarkThisOccurrenceAsUnreadAccessibleName => Text("MotifApp.MarkThisOccurrenceAsUnreadAccessibleName");

    public static string CheckThisUncertainChangeAgainAccessibleName => Text("MotifApp.CheckThisUncertainChangeAgainAccessibleName");

    public static string StripTokenNothingInFieldWorks => Text("MotifApp.StripTokenNothingInFieldWorks");

    public static string Staged => Text("MotifApp.Staged");

    public static string TimingNoTimesRecorded => Text("MotifApp.TimingNoTimesRecorded");

    public static string TimingFocusAllWords => Text("MotifApp.TimingFocusAllWords");

    public static string TimingFocusSelectedWordsPattern => Text("MotifApp.TimingFocusSelectedWordsPattern");

    public static string TimingFocusAllWordsUnderRuleFormat => Text("MotifApp.TimingFocusAllWordsUnderRuleFormat");

    public static string TimingFocusSelectedWordsUnderRuleFormat => Text("MotifApp.TimingFocusSelectedWordsUnderRuleFormat");

    public static string TimingRequestedRuleLower => Text("MotifApp.TimingRequestedRuleLower");

    public static string TimingRequestedRule => Text("MotifApp.TimingRequestedRule");

    public static string TimingStoredSummaryFormat => Text("MotifApp.TimingStoredSummaryFormat");

    public static string TimingNoTimesRecordedSelected => Text("MotifApp.TimingNoTimesRecordedSelected");

    public static string TimingNoWordsStopped => Text("MotifApp.TimingNoWordsStopped");

    public static string TimingNoWordsInGroup => Text("MotifApp.TimingNoWordsInGroup");

    public static string TimingNoWordsInSelection => Text("MotifApp.TimingNoWordsInSelection");

    public static string TimingCaptionSomeOfAllFormat => Text("MotifApp.TimingCaptionSomeOfAllFormat");

    public static string TimingCaptionSomeOfTheseFormat => Text("MotifApp.TimingCaptionSomeOfTheseFormat");

    public static string TimingCaptionOneWord => Text("MotifApp.TimingCaptionOneWord");

    public static string TimingCaptionAllWordsFormat => Text("MotifApp.TimingCaptionAllWordsFormat");

    public static string TimingCaptionTheseWordsFormat => Text("MotifApp.TimingCaptionTheseWordsFormat");

    public static string TimingStoppedCaption => Text("MotifApp.TimingStoppedCaption");

    public static string TimingAllWordsButtonFormat => Text("MotifApp.TimingAllWordsButtonFormat");

    public static string TimingAdviceBothLimits => Text("MotifApp.TimingAdviceBothLimits");

    public static string TimingAdviceSearchLimit => Text("MotifApp.TimingAdviceSearchLimit");

    public static string TimingAdviceTimeLimit => Text("MotifApp.TimingAdviceTimeLimit");

    public static string TimingStoppedButtonFormat => Text("MotifApp.TimingStoppedButtonFormat");

    public static string TimingRerunMeasurementsPattern => Text("MotifApp.TimingRerunMeasurementsPattern");

    public static string TimingPhraseSomeOfAllFormat => Text("MotifApp.TimingPhraseSomeOfAllFormat");

    public static string TimingPhraseSomeOfTheseFormat => Text("MotifApp.TimingPhraseSomeOfTheseFormat");

    public static string TimingPhraseThisWord => Text("MotifApp.TimingPhraseThisWord");

    public static string TimingPhraseAllWordsFormat => Text("MotifApp.TimingPhraseAllWordsFormat");

    public static string TimingPhraseTheseWordsFormat => Text("MotifApp.TimingPhraseTheseWordsFormat");

    public static string TimingNotAttributedLabel => Text("MotifApp.TimingNotAttributedLabel");

    public static string TimingShareHeaderFormat => Text("MotifApp.TimingShareHeaderFormat");

    public static string TimingShareHeader => Text("MotifApp.TimingShareHeader");

    public static string TimingNoSharesShown => Text("MotifApp.TimingNoSharesShown");

    public static string TimingShareOfFormat => Text("MotifApp.TimingShareOfFormat");

    public static string TimingShareOfOneMissingFormat => Text("MotifApp.TimingShareOfOneMissingFormat");

    public static string TimingShareOfManyMissingFormat => Text("MotifApp.TimingShareOfManyMissingFormat");

    public static string TimingNotAttributedNotRecorded => Text("MotifApp.TimingNotAttributedNotRecorded");

    public static string TimingNotAttributedWithShareFormat => Text("MotifApp.TimingNotAttributedWithShareFormat");

    public static string TimingNotAttributedFormat => Text("MotifApp.TimingNotAttributedFormat");

    public static string TimingOverrunFormat => Text("MotifApp.TimingOverrunFormat");

    public static string TimingNoStoredTimingForWords => Text("MotifApp.TimingNoStoredTimingForWords");

    public static string TimingScopeFormat => Text("MotifApp.TimingScopeFormat");

    public static string TimingPercentileSummaryFormat => Text("MotifApp.TimingPercentileSummaryFormat");

    public static string TimingNotRecordedLower => Text("MotifApp.TimingNotRecordedLower");

    public static string TimingRuleTimeOfFormat => Text("MotifApp.TimingRuleTimeOfFormat");

    public static string TimingRuleWordsTitleFormat => Text("MotifApp.TimingRuleWordsTitleFormat");

    public static string TimingStoppedAtSearchLimit => Text("MotifApp.TimingStoppedAtSearchLimit");

    public static string TimingStoppedAtTimeLimit => Text("MotifApp.TimingStoppedAtTimeLimit");

    public static string TimingNoStoredTimingForRule => Text("MotifApp.TimingNoStoredTimingForRule");

    public static string TimingRuleSummaryShareFormat => Text("MotifApp.TimingRuleSummaryShareFormat");

    public static string TimingRuleSummaryFormat => Text("MotifApp.TimingRuleSummaryFormat");

    public static string TimingRecordedInWordsPattern => Text("MotifApp.TimingRecordedInWordsPattern");

    public static string TimingOpenProjectFirst => Text("MotifApp.TimingOpenProjectFirst");

    public static string TimingChooseMatrixCellFirst => Text("MotifApp.TimingChooseMatrixCellFirst");

    public static string TimingChooseWordListFirst => Text("MotifApp.TimingChooseWordListFirst");

    public static string TimingNoWordsInList => Text("MotifApp.TimingNoWordsInList");

    public static string TimingEnterWords => Text("MotifApp.TimingEnterWords");

    public static string TimingTickWordsInTexts => Text("MotifApp.TimingTickWordsInTexts");

    public static string TimingHideCalls => Text("MotifApp.TimingHideCalls");

    public static string TimingShowCalls => Text("MotifApp.TimingShowCalls");

    public static string TimingHideCallsAccessibleName => Text("MotifApp.TimingHideCallsAccessibleName");

    public static string TimingShowCallsAccessibleName => Text("MotifApp.TimingShowCallsAccessibleName");

    public static string TimingRerunProgressFormat => Text("MotifApp.TimingRerunProgressFormat");

    public static string TimingRerunProgressParsingFormat => Text("MotifApp.TimingRerunProgressParsingFormat");

    public static string TimingRerunInvalidLimits => Text("MotifApp.TimingRerunInvalidLimits");

    public static string TimingRerunComplete => Text("MotifApp.TimingRerunComplete");

    public static string TimingRerunCancelled => Text("MotifApp.TimingRerunCancelled");

    public static string TimingNotRecorded => Text("MotifApp.TimingNotRecorded");

    public static string TimingCallsPartialFormat => Text("MotifApp.TimingCallsPartialFormat");

    public static string TimingCallsPattern => Text("MotifApp.TimingCallsPattern");

    public static string TimingNotCounted => Text("MotifApp.TimingNotCounted");

    public static string TimingKindMorphologicalRules => Text("MotifApp.TimingKindMorphologicalRules");

    public static string TimingKindPhonologicalRules => Text("MotifApp.TimingKindPhonologicalRules");

    public static string TimingKindRootLookup => Text("MotifApp.TimingKindRootLookup");

    public static string TimingKindLexicalEntries => Text("MotifApp.TimingKindLexicalEntries");

    public static string TimingGrammarWarningTip => Text("MotifApp.TimingGrammarWarningTip");

    public static string TimingUnknownQuality => Text("MotifApp.TimingUnknownQuality");

    public static string ParsimonyFindingCountPattern => Text("MotifApp.ParsimonyFindingCountPattern");

    public static string ParsimonyFilterChoiceLabelFormat => Text("MotifApp.ParsimonyFilterChoiceLabelFormat");

    public static string ParsimonyJobFailed => Text("MotifApp.ParsimonyJobFailed");

    public static string ParsimonyBaselineChanged => Text("MotifApp.ParsimonyBaselineChanged");

    public static string ParsimonyReportUnavailable => Text("MotifApp.ParsimonyReportUnavailable");

    public static string ParsimonyNoReport => Text("MotifApp.ParsimonyNoReport");

    public static string ParsimonyNoReportNextStep => Text("MotifApp.ParsimonyNoReportNextStep");

    public static string ParsimonyNoRecordTypes => Text("MotifApp.ParsimonyNoRecordTypes");

    public static string ParsimonyStagedNote => Text("MotifApp.ParsimonyStagedNote");

    public static string ParsimonyReturnStagedLabel => Text("MotifApp.ParsimonyReturnStagedLabel");

    public static string ParsimonyEvidenceNote => Text("MotifApp.ParsimonyEvidenceNote");

    public static string ParsimonyNotMeasuredFormat => Text("MotifApp.ParsimonyNotMeasuredFormat");

    public static string ParsimonyActiveTabFormat => Text("MotifApp.ParsimonyActiveTabFormat");

    public static string ParsimonySuppressedTabFormat => Text("MotifApp.ParsimonySuppressedTabFormat");

    public static string ParsimonyFindingsNotAvailable => Text("MotifApp.ParsimonyFindingsNotAvailable");

    public static string ParsimonyFindingWrongProject => Text("MotifApp.ParsimonyFindingWrongProject");

    public static string ParsimonyFindingNoLongerActive => Text("MotifApp.ParsimonyFindingNoLongerActive");

    public static string ParsimonyDecisionGone => Text("MotifApp.ParsimonyDecisionGone");

    public static string ParsimonyLoadingNoteFormat => Text("MotifApp.ParsimonyLoadingNoteFormat");

    public static string ParsimonyLoadFailedFormat => Text("MotifApp.ParsimonyLoadFailedFormat");

    public static string ParsimonyIncompleteNote => Text("MotifApp.ParsimonyIncompleteNote");

    public static string ParsimonyNoFindingsToReview => Text("MotifApp.ParsimonyNoFindingsToReview");

    public static string ParsimonyToReviewFormat => Text("MotifApp.ParsimonyToReviewFormat");

    public static string ParsimonyAllFindings => Text("MotifApp.ParsimonyAllFindings");

    public static string ParsimonyNotDecided => Text("MotifApp.ParsimonyNotDecided");

    public static string ParsimonyKept => Text("MotifApp.ParsimonyKept");

    public static string ParsimonyDeferred => Text("MotifApp.ParsimonyDeferred");

    public static string ParsimonyAsked => Text("MotifApp.ParsimonyAsked");

    public static string ParsimonyNoReasonGiven => Text("MotifApp.ParsimonyNoReasonGiven");

    public static string ParsimonyReasonFormat => Text("MotifApp.ParsimonyReasonFormat");

    public static string ParsimonyConflictFormat => Text("MotifApp.ParsimonyConflictFormat");

    public static string ParsimonyConflictDefault => Text("MotifApp.ParsimonyConflictDefault");

    public static string ParsimonyStagedStatusFormat => Text("MotifApp.ParsimonyStagedStatusFormat");

    public static string ParsimonyResurfacedFormat => Text("MotifApp.ParsimonyResurfacedFormat");

    public static string ParsimonyDetailCounted => Text("MotifApp.ParsimonyDetailCounted");

    public static string ParsimonyDetailItems => Text("MotifApp.ParsimonyDetailItems");

    public static string ParsimonyDetailWhyReported => Text("MotifApp.ParsimonyDetailWhyReported");

    public static string ParsimonyDetailStrength => Text("MotifApp.ParsimonyDetailStrength");

    public static string ParsimonyDetailPairedCheck => Text("MotifApp.ParsimonyDetailPairedCheck");

    public static string ParsimonyDetailEarlierReason => Text("MotifApp.ParsimonyDetailEarlierReason");

    public static string ParsimonyDetailDecision => Text("MotifApp.ParsimonyDetailDecision");

    public static string ParsimonyDetailSubject => Text("MotifApp.ParsimonyDetailSubject");

    public static string ParsimonyDetailJudgmentId => Text("MotifApp.ParsimonyDetailJudgmentId");

    public static string ParsimonyDetailNotebookRecord => Text("MotifApp.ParsimonyDetailNotebookRecord");

    public static string ParsimonyDetailReason => Text("MotifApp.ParsimonyDetailReason");

    public static string ParsimonyDetailCurrentState => Text("MotifApp.ParsimonyDetailCurrentState");

    public static string ParsimonyReportedBecauseFormat => Text("MotifApp.ParsimonyReportedBecauseFormat");

    public static string ParsimonyWriteQuestionFirst => Text("MotifApp.ParsimonyWriteQuestionFirst");

    public static string ParsimonyReturnStagedMessageFormat => Text("MotifApp.ParsimonyReturnStagedMessageFormat");

    public static string ParsimonyFixNoteFormat => Text("MotifApp.ParsimonyFixNoteFormat");

    public static string ParsimonyFixNoteNone => Text("MotifApp.ParsimonyFixNoteNone");

    public static string ParsimonyItemsNotNamed => Text("MotifApp.ParsimonyItemsNotNamed");

    public static string ParsimonyItemsAndMorePattern => Text("MotifApp.ParsimonyItemsAndMorePattern");

    public static string ParsimonyJoinTwoFormat => Text("MotifApp.ParsimonyJoinTwoFormat");

    public static string ParsimonyJoinManyFormat => Text("MotifApp.ParsimonyJoinManyFormat");

    public static string ParsimonyDescribeFormat => Text("MotifApp.ParsimonyDescribeFormat");

    public static string ParsimonyCheckTitle => Text("MotifApp.ParsimonyCheckTitle");

    public static string ParsimonyCheckShortTitle => Text("MotifApp.ParsimonyCheckShortTitle");

    public static string ParsimonyAxisParsimony => Text("MotifApp.ParsimonyAxisParsimony");

    public static string ParsimonyAxisRestrictiveness => Text("MotifApp.ParsimonyAxisRestrictiveness");

    public static string ParsimonyAxisBoth => Text("MotifApp.ParsimonyAxisBoth");

    public static string ParsimonyAxisParsimonyMeaning => Text("MotifApp.ParsimonyAxisParsimonyMeaning");

    public static string ParsimonyAxisRestrictivenessMeaning => Text("MotifApp.ParsimonyAxisRestrictivenessMeaning");

    public static string ParsimonyAxisBothMeaning => Text("MotifApp.ParsimonyAxisBothMeaning");

    public static string ParsimonyQuestionOpen => Text("MotifApp.ParsimonyQuestionOpen");

    public static string ParsimonyFixPlanned => Text("MotifApp.ParsimonyFixPlanned");

    public static string ParsimonyStateSuppressed => Text("MotifApp.ParsimonyStateSuppressed");

    public static string ParsimonyStateStale => Text("MotifApp.ParsimonyStateStale");

    public static string ParsimonyStateEvidenceUnavailable => Text("MotifApp.ParsimonyStateEvidenceUnavailable");

    public static string ParsimonyStateConflict => Text("MotifApp.ParsimonyStateConflict");

    public static string ParsimonyStateDecisionUnavailable => Text("MotifApp.ParsimonyStateDecisionUnavailable");

    public static string ParsimonyTierStatic => Text("MotifApp.ParsimonyTierStatic");

    public static string ParsimonyTierText => Text("MotifApp.ParsimonyTierText");

    public static string ParsimonyTierOutcomes => Text("MotifApp.ParsimonyTierOutcomes");

    public static string ParsimonyVerificationPassed => Text("MotifApp.ParsimonyVerificationPassed");

    public static string ParsimonyVerificationFailed => Text("MotifApp.ParsimonyVerificationFailed");

    public static string ParsimonyVerificationInconclusive => Text("MotifApp.ParsimonyVerificationInconclusive");

    public static string ParsimonyVerificationNotRun => Text("MotifApp.ParsimonyVerificationNotRun");

    public static string HandoffWriteAgain => Text("MotifApp.HandoffWriteAgain");

    public static string HandoffWrite => Text("MotifApp.HandoffWrite");

    public static string HandoffNoParseResultsFormat => Text("MotifApp.HandoffNoParseResultsFormat");

    public static string HandoffParseAllWordsFirst => Text("MotifApp.HandoffParseAllWordsFirst");

    public static string HandoffCoverageTextsPattern => Text("MotifApp.HandoffCoverageTextsPattern");

    public static string HandoffCoveragePastedWordsPattern => Text("MotifApp.HandoffCoveragePastedWordsPattern");

    public static string HandoffCoverageJoinFormat => Text("MotifApp.HandoffCoverageJoinFormat");

    public static string HandoffCoverageOnFormat => Text("MotifApp.HandoffCoverageOnFormat");

    public static string HandoffCoverageOnFromFormat => Text("MotifApp.HandoffCoverageOnFromFormat");

    public static string HandoffCoverageLatestFormat => Text("MotifApp.HandoffCoverageLatestFormat");

    public static string HandoffCoverageLatestFromFormat => Text("MotifApp.HandoffCoverageLatestFromFormat");

    public static string HandoffDataSensitivity => Text("MotifApp.HandoffDataSensitivity");

    public static string HandoffLastWrittenFormat => Text("MotifApp.HandoffLastWrittenFormat");

    public static string HandoffNotWrittenYet => Text("MotifApp.HandoffNotWrittenYet");

    public static string HandoffOutOfDateFormat => Text("MotifApp.HandoffOutOfDateFormat");

    public static string HandoffKeepsTraceFormat => Text("MotifApp.HandoffKeepsTraceFormat");

    public static string HandoffWarningWordsPattern => Text("MotifApp.HandoffWarningWordsPattern");

    public static string HandoffWarningNoWords => Text("MotifApp.HandoffWarningNoWords");

    public static string HandoffChosenWordsPattern => Text("MotifApp.HandoffChosenWordsPattern");

    public static string HandoffFileHandoffMd => Text("MotifApp.HandoffFileHandoffMd");

    public static string HandoffFileParseResults => Text("MotifApp.HandoffFileParseResults");

    public static string HandoffFileTexts => Text("MotifApp.HandoffFileTexts");

    public static string HandoffFileGrammar => Text("MotifApp.HandoffFileGrammar");

    public static string HandoffFileReader => Text("MotifApp.HandoffFileReader");

    public static string HandoffDragAccessibleNameFormat => Text("MotifApp.HandoffDragAccessibleNameFormat");

    public static string HandoffCopyPathAccessibleNameFormat => Text("MotifApp.HandoffCopyPathAccessibleNameFormat");

    public static string StatsIncompleteBoth => Text("MotifApp.StatsIncompleteBoth");

    public static string StatsIncompleteSearch => Text("MotifApp.StatsIncompleteSearch");

    public static string StatsIncompleteTime => Text("MotifApp.StatsIncompleteTime");

    public static string StatsSearchCompleted => Text("MotifApp.StatsSearchCompleted");

    public static string StatsCompletionUnavailable => Text("MotifApp.StatsCompletionUnavailable");

    public static string StatsCompleted => Text("MotifApp.StatsCompleted");

    public static string StatsNoResult => Text("MotifApp.StatsNoResult");

    public static string StatsSearchAndTimeLimits => Text("MotifApp.StatsSearchAndTimeLimits");

    public static string StatsSearchLimit => Text("MotifApp.StatsSearchLimit");

    public static string StatsTimeLimit => Text("MotifApp.StatsTimeLimit");

    public static string StatsGroupByWord => Text("MotifApp.StatsGroupByWord");

    public static string StatsGroupByObject => Text("MotifApp.StatsGroupByObject");

    public static string StatsGroupByAllomorph => Text("MotifApp.StatsGroupByAllomorph");

    public static string StatsGroupByMorpheme => Text("MotifApp.StatsGroupByMorpheme");

    public static string StatsGroupByObjectGroup => Text("MotifApp.StatsGroupByObjectGroup");

    public static string StatsGroupNeverFired => Text("MotifApp.StatsGroupNeverFired");

    public static string StatsHeaderAnalysisAttempts => Text("MotifApp.StatsHeaderAnalysisAttempts");

    public static string StatsHeaderAttempts => Text("MotifApp.StatsHeaderAttempts");

    public static string StatsHeaderWordTime => Text("MotifApp.StatsHeaderWordTime");

    public static string StatsHeaderOwnTime => Text("MotifApp.StatsHeaderOwnTime");

    public static string StatsShadingWord => Text("MotifApp.StatsShadingWord");

    public static string StatsShadingObject => Text("MotifApp.StatsShadingObject");

    public static string StatsScopeObjectFormat => Text("MotifApp.StatsScopeObjectFormat");

    public static string StatsScopeNoWords => Text("MotifApp.StatsScopeNoWords");

    public static string StatsScopeSelectedWordsPattern => Text("MotifApp.StatsScopeSelectedWordsPattern");

    public static string StatsScopeEveryWord => Text("MotifApp.StatsScopeEveryWord");

    public static string StatsScopeOverridesFormat => Text("MotifApp.StatsScopeOverridesFormat");

    public static string StatsStoppedHeadlineFormat => Text("MotifApp.StatsStoppedHeadlineFormat");

    public static string StatsNoWordStopped => Text("MotifApp.StatsNoWordStopped");

    public static string StatsEachSearchStopped => Text("MotifApp.StatsEachSearchStopped");

    public static string StatsNoWordSeveral => Text("MotifApp.StatsNoWordSeveral");

    public static string StatsSeveralWordsPattern => Text("MotifApp.StatsSeveralWordsPattern");

    public static string StatsNoSearchSeveral => Text("MotifApp.StatsNoSearchSeveral");

    public static string StatsSortByAnalyses => Text("MotifApp.StatsSortByAnalyses");

    public static string DifferenceNoneMovedFormat => Text("MotifApp.DifferenceNoneMovedFormat");

    public static string DifferenceMovedWorseFormat => Text("MotifApp.DifferenceMovedWorseFormat");

    public static string DifferenceMovedNoneWorseFormat => Text("MotifApp.DifferenceMovedNoneWorseFormat");

    public static string DifferenceOnlyInOnePattern => Text("MotifApp.DifferenceOnlyInOnePattern");

    public static string DifferenceRunComparisonFormat => Text("MotifApp.DifferenceRunComparisonFormat");

    public static string DifferenceMoveRegressed => Text("MotifApp.DifferenceMoveRegressed");

    public static string DifferenceMoveNowUnknown => Text("MotifApp.DifferenceMoveNowUnknown");

    public static string DifferenceMoveChanged => Text("MotifApp.DifferenceMoveChanged");

    public static string DifferenceMoveNewCoverage => Text("MotifApp.DifferenceMoveNewCoverage");

    public static string DifferenceMoveFixed => Text("MotifApp.DifferenceMoveFixed");

    public static string DifferenceMoveDecided => Text("MotifApp.DifferenceMoveDecided");

    public static string DifferenceMoveSettled => Text("MotifApp.DifferenceMoveSettled");

    public static string DifferenceMoveImproved => Text("MotifApp.DifferenceMoveImproved");

    public static string DifferenceMoveUnchanged => Text("MotifApp.DifferenceMoveUnchanged");

    public static string DifferenceStillProblemFormat => Text("MotifApp.DifferenceStillProblemFormat");

    public static string DifferenceTransitionFormat => Text("MotifApp.DifferenceTransitionFormat");

    public static string DifferenceMoveAccessibleNameFormat => Text("MotifApp.DifferenceMoveAccessibleNameFormat");

    public static string DifferenceBeforeFormat => Text("MotifApp.DifferenceBeforeFormat");

    public static string CompareListLost => Text("MotifApp.CompareListLost");

    public static string CompareListBuiltSomethingElse => Text("MotifApp.CompareListBuiltSomethingElse");

    public static string CompareListHaveALook => Text("MotifApp.CompareListHaveALook");

    public static string CompareListBuiltAnyway => Text("MotifApp.CompareListBuiltAnyway");

    public static string CompareListNew => Text("MotifApp.CompareListNew");

    public static string CompareListNobodyCanAnalyze => Text("MotifApp.CompareListNobodyCanAnalyze");

    public static string CompareListStopped => Text("MotifApp.CompareListStopped");

    public static string CompareEveryWordFinished => Text("MotifApp.CompareEveryWordFinished");

    public static string CompareRerunWordsPattern => Text("MotifApp.CompareRerunWordsPattern");

    public static string CompareNoCellsChosen => Text("MotifApp.CompareNoCellsChosen");

    public static string CompareCellSelectionFormat => Text("MotifApp.CompareCellSelectionFormat");

    public static string CompareCellsChosenPattern => Text("MotifApp.CompareCellsChosenPattern");

    public static string CompareAllWords => Text("MotifApp.CompareAllWords");

    public static string CompareCellHeadingFormat => Text("MotifApp.CompareCellHeadingFormat");

    public static string CompareChooseCellHint => Text("MotifApp.CompareChooseCellHint");

    public static string CompareCtrlClickHint => Text("MotifApp.CompareCtrlClickHint");

    public static string CompareWordsOfFormat => Text("MotifApp.CompareWordsOfFormat");

    public static string CompareWordsAndPlacesFormat => Text("MotifApp.CompareWordsAndPlacesFormat");

    public static string CompareNoWordsTicked => Text("MotifApp.CompareNoWordsTicked");

    public static string CompareWordsTickedPattern => Text("MotifApp.CompareWordsTickedPattern");

    public static string CompareSharedMoreFormat => Text("MotifApp.CompareSharedMoreFormat");

    public static string CompareColumnSame => Text("MotifApp.CompareColumnSame");

    public static string CompareColumnDifferent => Text("MotifApp.CompareColumnDifferent");

    public static string CompareColumnNoParse => Text("MotifApp.CompareColumnNoParse");

    public static string CompareColumnStopped => Text("MotifApp.CompareColumnStopped");

    public static string CompareColumnNotParsed => Text("MotifApp.CompareColumnNotParsed");

    public static string CompareHeldInFieldWorksFormat => Text("MotifApp.CompareHeldInFieldWorksFormat");

    public static string CompareDifferentAndMore => Text("MotifApp.CompareDifferentAndMore");

    public static string CompareChooseNotInFieldWorksAccessibleName => Text("MotifApp.CompareChooseNotInFieldWorksAccessibleName");

    public static string CompareChooseRowAccessibleNameFormat => Text("MotifApp.CompareChooseRowAccessibleNameFormat");

    public static string CompareWordsPattern => Text("MotifApp.CompareWordsPattern");

    public static string CompareChooseColumnAccessibleNameFormat => Text("MotifApp.CompareChooseColumnAccessibleNameFormat");

    public static string CompareRefusalBadgeFormat => Text("MotifApp.CompareRefusalBadgeFormat");

    public static string ComparePlacesPattern => Text("MotifApp.ComparePlacesPattern");



    public static string CompareCellAccessibleNameFormat => Text("MotifApp.CompareCellAccessibleNameFormat");

    public static string CompareCellAccessibleNamePlacesFormat => Text("MotifApp.CompareCellAccessibleNamePlacesFormat");

    public static string CompareCellAccessibleNameStatusFormat => Text("MotifApp.CompareCellAccessibleNameStatusFormat");

    public static string CompareMorphemeCountFormat => Text("MotifApp.CompareMorphemeCountFormat");

    public static string CompareMorphemeUsedPattern => Text("MotifApp.CompareMorphemeUsedPattern");

    public static string CompareWordAccessibleNameFormat => Text("MotifApp.CompareWordAccessibleNameFormat");

    public static string CompareReadingChoiceFormat => Text("MotifApp.CompareReadingChoiceFormat");

    public static string CompareReadingUnresolved => Text("MotifApp.CompareReadingUnresolved");

    public static string CompareAnalysesPattern => Text("MotifApp.CompareAnalysesPattern");

    public static string CompareOutcomeAnalysesFormat => Text("MotifApp.CompareOutcomeAnalysesFormat");

    public static string CompareFixApprovedNoParse => Text("MotifApp.CompareFixApprovedNoParse");

    public static string CompareFixApprovedNoMatch => Text("MotifApp.CompareFixApprovedNoMatch");

    public static string CompareFixRejectedRebuilt => Text("MotifApp.CompareFixRejectedRebuilt");

    public static string CompareFixUnknownNoBuild => Text("MotifApp.CompareFixUnknownNoBuild");

    public static string CompareFixRejectedRebuiltExplanation => Text("MotifApp.CompareFixRejectedRebuiltExplanation");

    public static string CompareFixUnknownNoBuildExplanation => Text("MotifApp.CompareFixUnknownNoBuildExplanation");

    public static string TryWordContextUnavailable => Text("MotifApp.TryWordContextUnavailable");

    public static string TryWordMembershipUnrecorded => Text("MotifApp.TryWordMembershipUnrecorded");

    public static string TryWordNoStoredAnalyses => Text("MotifApp.TryWordNoStoredAnalyses");

    public static string TryWordStoredAnalysesForFormat => Text("MotifApp.TryWordStoredAnalysesForFormat");

    public static string TryWordRefusalInLastParseFormat => Text("MotifApp.TryWordRefusalInLastParseFormat");

    public static string OutcomeSame => Text("MotifApp.OutcomeSame");

    public static string OutcomeDifferent => Text("MotifApp.OutcomeDifferent");

    public static string TryWordParsedFormat => Text("MotifApp.TryWordParsedFormat");

    public static string TryWordAnalysesPattern => Text("MotifApp.TryWordAnalysesPattern");

    public static string TryWordTracedWithBaselineFormat => Text("MotifApp.TryWordTracedWithBaselineFormat");

    public static string TryWordTracedSavedGrammar => Text("MotifApp.TryWordTracedSavedGrammar");

    public static string TryWordGrammarNotRecorded => Text("MotifApp.TryWordGrammarNotRecorded");

    public static string TryWordDifferentBaselines => Text("MotifApp.TryWordDifferentBaselines");

    public static string TryWordOtherRulesPattern => Text("MotifApp.TryWordOtherRulesPattern");

    public static string TryWordEarlierTimingTitleFormat => Text("MotifApp.TryWordEarlierTimingTitleFormat");

    public static string TryWordEarlierSourceWithWordFormat => Text("MotifApp.TryWordEarlierSourceWithWordFormat");

    public static string TryWordEarlierSourceFormat => Text("MotifApp.TryWordEarlierSourceFormat");

    public static string TryWordShareOfFormat => Text("MotifApp.TryWordShareOfFormat");

    public static string TryWordWordShare => Text("MotifApp.TryWordWordShare");

    public static string TryWordOverrunFormat => Text("MotifApp.TryWordOverrunFormat");

    public static string TryWordOpinionCountFormat => Text("MotifApp.TryWordOpinionCountFormat");

    public static string TryWordPieceBuiltRefused => Text("MotifApp.TryWordPieceBuiltRefused");

    public static string TraceStepBuilt => Text("MotifApp.TraceStepBuilt");

    public static string TraceStepRefused => Text("MotifApp.TraceStepRefused");

    public static string TryWordPieceTried => Text("MotifApp.TryWordPieceTried");

    public static string TryWordPieceDiffer => Text("MotifApp.TryWordPieceDiffer");

    public static string WarningsAllAreas => Text("MotifApp.WarningsAllAreas");

    public static string WarningsFindingsPattern => Text("MotifApp.WarningsFindingsPattern");

    public static string WarningsOpenEntryFormat => Text("MotifApp.WarningsOpenEntryFormat");

    public static string WarningsErrorsPattern => Text("MotifApp.WarningsErrorsPattern");

    public static string WarningsWarningsPattern => Text("MotifApp.WarningsWarningsPattern");

    public static string WarningsInformationPattern => Text("MotifApp.WarningsInformationPattern");

    public static string WarningsBreakdownFormat => Text("MotifApp.WarningsBreakdownFormat");

    public static string WarningsCountMatchFormat => Text("MotifApp.WarningsCountMatchFormat");

    public static string WarningsTouchYourWordsFormat => Text("MotifApp.WarningsTouchYourWordsFormat");

    public static string WarningsTouchYourWords => Text("MotifApp.WarningsTouchYourWords");

    public static string WarningsSortMostWords => Text("MotifApp.WarningsSortMostWords");

    public static string WarningsSortReportOrder => Text("MotifApp.WarningsSortReportOrder");

    public static string WarningsAreaEnvironments => Text("MotifApp.WarningsAreaEnvironments");

    public static string WarningsAreaAffixes => Text("MotifApp.WarningsAreaAffixes");

    public static string WarningsAreaPhonologicalRules => Text("MotifApp.WarningsAreaPhonologicalRules");

    public static string WarningsAreaOther => Text("MotifApp.WarningsAreaOther");

    public static string WarningsYourWordsThatUseFormat => Text("MotifApp.WarningsYourWordsThatUseFormat");

    public static string WarningsYourWords => Text("MotifApp.WarningsYourWords");

    public static string WarningsNoneUseFormat => Text("MotifApp.WarningsNoneUseFormat");

    public static string WarningsNoneUseNamed => Text("MotifApp.WarningsNoneUseNamed");

    public static string WarningsRowTitleFormat => Text("MotifApp.WarningsRowTitleFormat");

    public static string WarningsSeverityError => Text("MotifApp.WarningsSeverityError");

    public static string WarningsSeverityWarning => Text("MotifApp.WarningsSeverityWarning");

    public static string WarningsSeverityNote => Text("MotifApp.WarningsSeverityNote");

    public static string WarningsTableItemRepeatedFormat => Text("MotifApp.WarningsTableItemRepeatedFormat");

    public static string WarningsUnnamedItem => Text("MotifApp.WarningsUnnamedItem");

    public static string WarningsNone => Text("MotifApp.WarningsNone");

    public static string WarningsSpellingMatchesPattern => Text("MotifApp.WarningsSpellingMatchesPattern");

    public static string WarningsSpellingMatchesAtLeastPattern => Text("MotifApp.WarningsSpellingMatchesAtLeastPattern");

    public static string WarningsReachSomeExact => Text("MotifApp.WarningsReachSomeExact");

    public static string WarningsReachExact => Text("MotifApp.WarningsReachExact");

    public static string WarningsReachSpellingPartial => Text("MotifApp.WarningsReachSpellingPartial");

    public static string WarningsReachSpelling => Text("MotifApp.WarningsReachSpelling");

    public static string WarningsReachMembersPartial => Text("MotifApp.WarningsReachMembersPartial");

    public static string WarningsReachMembers => Text("MotifApp.WarningsReachMembers");

    public static string WarningsReachNone => Text("MotifApp.WarningsReachNone");

    public static string WarningsReachNoRoute => Text("MotifApp.WarningsReachNoRoute");

    public static string WarningsReachNoSubject => Text("MotifApp.WarningsReachNoSubject");

    public static string WarningsReachUnsupported => Text("MotifApp.WarningsReachUnsupported");

    public static string WarningsReachProjectWide => Text("MotifApp.WarningsReachProjectWide");

    public static string WarningsReachMissing => Text("MotifApp.WarningsReachMissing");

    public static string WarningsReachUnresolvedNamed => Text("MotifApp.WarningsReachUnresolvedNamed");

    public static string WarningsReachUnresolved => Text("MotifApp.WarningsReachUnresolved");

    public static string WarningsReachEvidencePartial => Text("MotifApp.WarningsReachEvidencePartial");

    public static string WarningsReachUnavailable => Text("MotifApp.WarningsReachUnavailable");

    public static string WarningsOneStoredAnalysis => Text("MotifApp.WarningsOneStoredAnalysis");

    public static string WarningsStoredAnalysesFormat => Text("MotifApp.WarningsStoredAnalysesFormat");

    public static string WarningsCantTell => Text("MotifApp.WarningsCantTell");

    public static string WarningsNoneOfYourWords => Text("MotifApp.WarningsNoneOfYourWords");

    public static string WarningsWordsOfYourWordsFormat => Text("MotifApp.WarningsWordsOfYourWordsFormat");

    public static string WarningsSeenTimesFormat => Text("MotifApp.WarningsSeenTimesFormat");

    public static string WarningsGoneAfterRefresh => Text("MotifApp.WarningsGoneAfterRefresh");

    public static string WarningsNotMatchedAfterRefresh => Text("MotifApp.WarningsNotMatchedAfterRefresh");

    public static string WindowNothingParsedSinceRefresh => Text("MotifApp.WindowNothingParsedSinceRefresh");

    public static string WindowParsingSeeTopRow => Text("MotifApp.WindowParsingSeeTopRow");

    public static string WindowAiHandoffForThisList => Text("MotifApp.WindowAiHandoffForThisList");

    public static string WindowAiHandoffForTickedWordsAccessibleName => Text("MotifApp.WindowAiHandoffForTickedWordsAccessibleName");

    public static string WindowAiHandoffForThisWord => Text("MotifApp.WindowAiHandoffForThisWord");

    public static string WindowAiHandoff => Text("MotifApp.WindowAiHandoff");

    public static string WindowChooseAWordListFirst => Text("MotifApp.WindowChooseAWordListFirst");

    public static string WindowNoWordsInListForHandoff => Text("MotifApp.WindowNoWordsInListForHandoff");

    public static string WindowNoWordsInListToTick => Text("MotifApp.WindowNoWordsInListToTick");

    public static string WindowTickWordsFirst => Text("MotifApp.WindowTickWordsFirst");

    public static string WindowFieldWorksSavedSince => Text("MotifApp.WindowFieldWorksSavedSince");

    public static string WindowDragAllAiHandoffFilesAccessibleName => Text("MotifApp.WindowDragAllAiHandoffFilesAccessibleName");

    public static string WindowAiHandoffForWordsPattern => Text("MotifApp.WindowAiHandoffForWordsPattern");

    public static string ProgressSlowestFormat => Text("MotifApp.ProgressSlowestFormat");

    public static string ProgressFinishing => Text("MotifApp.ProgressFinishing");

    public static string ProgressReadingStatistics => Text("MotifApp.ProgressReadingStatistics");

    public static string ProgressPreparing => Text("MotifApp.ProgressPreparing");

    public static string ProgressWordsDoneFormat => Text("MotifApp.ProgressWordsDoneFormat");

    public static string ProgressWordsDoneParsingFormat => Text("MotifApp.ProgressWordsDoneParsingFormat");

    public static string ProgressStatusParsingFormat => Text("MotifApp.ProgressStatusParsingFormat");

    public static string ProgressComplete => Text("MotifApp.ProgressComplete");

    public static string ProgressTimeFinishing => Text("MotifApp.ProgressTimeFinishing");

    public static string ProgressTimeReadingStatistics => Text("MotifApp.ProgressTimeReadingStatistics");

    public static string ProgressTimeComplete => Text("MotifApp.ProgressTimeComplete");

    public static string ProgressTimeEstimating => Text("MotifApp.ProgressTimeEstimating");

    public static string ProgressElapsedFormat => Text("MotifApp.ProgressElapsedFormat");

    public static string ProgressStoppedWordsPattern => Text("MotifApp.ProgressStoppedWordsPattern");

    public static string ProgressStoppedTimedOutAfterFormat => Text("MotifApp.ProgressStoppedTimedOutAfterFormat");

    public static string ProgressStoppedTimedOutFormat => Text("MotifApp.ProgressStoppedTimedOutFormat");

    public static string ProgressStoppedSearchLimitFormat => Text("MotifApp.ProgressStoppedSearchLimitFormat");

    public static string ProgressStalledNoLimit => Text("MotifApp.ProgressStalledNoLimit");

    public static string ProgressStalledWithLimit => Text("MotifApp.ProgressStalledWithLimit");

    public static string ProgressCancellation => Text("MotifApp.ProgressCancellation");

    public static string ProgressEstimatingTime => Text("MotifApp.ProgressEstimatingTime");

    public static string ProgressLessThanSecondLeft => Text("MotifApp.ProgressLessThanSecondLeft");

    public static string ProgressAboutLeftFormat => Text("MotifApp.ProgressAboutLeftFormat");

    public static string ProgressDurationHoursFormat => Text("MotifApp.ProgressDurationHoursFormat");

    public static string ProgressDurationMinutesFormat => Text("MotifApp.ProgressDurationMinutesFormat");

    public static string ProgressDurationLessThanSecond => Text("MotifApp.ProgressDurationLessThanSecond");

    public static string ProgressDurationSecondsFormat => Text("MotifApp.ProgressDurationSecondsFormat");

    public static string ProgressStoppedWordFormat => Text("MotifApp.ProgressStoppedWordFormat");

    public static string ProgressTimedOut => Text("MotifApp.ProgressTimedOut");

    public static string ProgressStoppedAtSearchLimit => Text("MotifApp.ProgressStoppedAtSearchLimit");

    public static string OutcomeNoParse => Text("MotifApp.OutcomeNoParse");

    public static string OutcomeStopped => Text("MotifApp.OutcomeStopped");

    public static string OutcomeNotParsed => Text("MotifApp.OutcomeNotParsed");

    public static string OpinionApproved => Text("MotifApp.OpinionApproved");

    public static string OpinionDisapproved => Text("MotifApp.OpinionDisapproved");

    public static string OpinionUnknown => Text("MotifApp.OpinionUnknown");

    public static string MeaningFine => Text("MotifApp.MeaningFine");

    public static string MeaningHaveALook => Text("MotifApp.MeaningHaveALook");

    public static string MeaningProblem => Text("MotifApp.MeaningProblem");

    public static string MeaningNoResult => Text("MotifApp.MeaningNoResult");

    public static string SeverityInformation => Text("MotifApp.SeverityInformation");

    public static string TraceStepTried => Text("MotifApp.TraceStepTried");

    public static string IncorrectSpellingLabel => Text("MotifApp.IncorrectSpellingLabel");

    public static string MeaningParsesNothingInFieldWorks => Text("MotifApp.MeaningParsesNothingInFieldWorks");

    public static string WordRowOpenInText => Text("MotifApp.WordRowOpenInText");

    public static string WordRowOpenWord => Text("MotifApp.WordRowOpenWord");

    public static string WordRowOpenInTextAccessibleNameFormat => Text("MotifApp.WordRowOpenInTextAccessibleNameFormat");

    public static string WordRowOpenWordAccessibleNameFormat => Text("MotifApp.WordRowOpenWordAccessibleNameFormat");

    public static string WordRowAnalysesFormat => Text("MotifApp.WordRowAnalysesFormat");

    public static string WordRowAnalysesShowingOneFormat => Text("MotifApp.WordRowAnalysesShowingOneFormat");

    public static string WordRowMatchedDisapproved => Text("MotifApp.WordRowMatchedDisapproved");

    public static string WordRowIdentityNotRecordedFormat => Text("MotifApp.WordRowIdentityNotRecordedFormat");

    public static string WordRowDifferentIdentityFormat => Text("MotifApp.WordRowDifferentIdentityFormat");

    public static string WordRowSequenceDiffersFormat => Text("MotifApp.WordRowSequenceDiffersFormat");

    public static string WordRowIdentityListFormat => Text("MotifApp.WordRowIdentityListFormat");

    public static string WordRowSameFormGlossFormat => Text("MotifApp.WordRowSameFormGlossFormat");

    public static string WordRowDifferentEntryFormat => Text("MotifApp.WordRowDifferentEntryFormat");

    public static string WordRowLessThanMs => Text("MotifApp.WordRowLessThanMs");

    public static string WordRowMsFormat => Text("MotifApp.WordRowMsFormat");

    public static string WordRowMeasuredFormat => Text("MotifApp.WordRowMeasuredFormat");

    public static string WordRowUnread => Text("MotifApp.WordRowUnread");

    public static string WordRowSummaryPanGlossFormat => Text("MotifApp.WordRowSummaryPanGlossFormat");

    public static string WordRowOpenInToolFormat => Text("MotifApp.WordRowOpenInToolFormat");

    public static string WordRowToolLinkFormat => Text("MotifApp.WordRowToolLinkFormat");

    public static string WordRowParseToLinkFormat => Text("MotifApp.WordRowParseToLinkFormat");

    public static string WordRowNoWordformFormat => Text("MotifApp.WordRowNoWordformFormat");

    public static string TraceKindMorphologicalRule => Text("MotifApp.TraceKindMorphologicalRule");

    public static string TraceKindPhonologicalRule => Text("MotifApp.TraceKindPhonologicalRule");

    public static string TraceKindAffixTemplate => Text("MotifApp.TraceKindAffixTemplate");

    public static string TraceKindLexicalLookup => Text("MotifApp.TraceKindLexicalLookup");

    public static string TraceKindRuleLevel => Text("MotifApp.TraceKindRuleLevel");

    public static string TraceKindCompoundRule => Text("MotifApp.TraceKindCompoundRule");

    public static string TraceKindBuiltTheWord => Text("MotifApp.TraceKindBuiltTheWord");

    public static string TraceKindNoAnalysisFound => Text("MotifApp.TraceKindNoAnalysisFound");

    public static string TraceKindBlocked => Text("MotifApp.TraceKindBlocked");

    public static string TraceKindStep => Text("MotifApp.TraceKindStep");

    public static string StoreDeletionWarning => Text("MotifApp.StoreDeletionWarning");

    public static string TrialNotTriedSentence => Text("MotifApp.TrialNotTriedSentence");

    public static string TrialNotTriedStatus => Text("MotifApp.TrialNotTriedStatus");

    public static string TrialTrying => Text("MotifApp.TrialTrying");

    public static string TrialDidNotFinishStatus => Text("MotifApp.TrialDidNotFinishStatus");

    public static string TrialDidNotFinishSentence => Text("MotifApp.TrialDidNotFinishSentence");

    public static string TrialCurrentStatus => Text("MotifApp.TrialCurrentStatus");

    public static string TrialNeedsAnotherTryStatus => Text("MotifApp.TrialNeedsAnotherTryStatus");

    public static string TrialBaselineMovedSentence => Text("MotifApp.TrialBaselineMovedSentence");

    public static string TrialBaselineMovedOldNumbers => Text("MotifApp.TrialBaselineMovedOldNumbers");

    public static string TrialProposalChangedSentence => Text("MotifApp.TrialProposalChangedSentence");

    public static string TrialProposalChangedOldNumbers => Text("MotifApp.TrialProposalChangedOldNumbers");

    public static string RefusalParserMissing => Text("MotifApp.RefusalParserMissing");

    public static string RefusalParserUnusable => Text("MotifApp.RefusalParserUnusable");

    public static string RefusalParserWrong => Text("MotifApp.RefusalParserWrong");

    public static string RefusalStoreFromOtherVersion => Text("MotifApp.RefusalStoreFromOtherVersion");

    public static string RefusalStoreDamaged => Text("MotifApp.RefusalStoreDamaged");

    public static string RefusalGeneric => Text("MotifApp.RefusalGeneric");

    public static string RefusalApplyAppliedContentMismatch => Text("MotifApp.RefusalApplyAppliedContentMismatch");

    public static string RefusalApplyChangeNoLongerFits => Text("MotifApp.RefusalApplyChangeNoLongerFits");

    public static string RefusalApplyChangeUncertain => Text("MotifApp.RefusalApplyChangeUncertain");

    public static string RefusalApplyChangesChanged => Text("MotifApp.RefusalApplyChangesChanged");

    public static string RefusalApplyDrift => Text("MotifApp.RefusalApplyDrift");

    public static string RefusalApplyDryRunMissing => Text("MotifApp.RefusalApplyDryRunMissing");

    public static string RefusalProposalClosed => Text("MotifApp.RefusalProposalClosed");

    public static string RefusalProposalNotFound => Text("MotifApp.RefusalProposalNotFound");

    public static string RefusalProposalRationaleEmpty => Text("MotifApp.RefusalProposalRationaleEmpty");

    public static string RefusalApplyPreconditionFailed => Text("MotifApp.RefusalApplyPreconditionFailed");

    public static string RefusalApplyNotReady => Text("MotifApp.RefusalApplyNotReady");

    public static string RefusalApplyProjectInUse => Text("MotifApp.RefusalApplyProjectInUse");

    public static string RefusalApplyReconciliationNeeded => Text("MotifApp.RefusalApplyReconciliationNeeded");

    public static string RefusalOperationCascadingDelete => Text("MotifApp.RefusalOperationCascadingDelete");

    public static string RefusalOperationInvalidDependency => Text("MotifApp.RefusalOperationInvalidDependency");

    public static string RefusalApplyRegression => Text("MotifApp.RefusalApplyRegression");

    public static string RefusalApplyReopenFailed => Text("MotifApp.RefusalApplyReopenFailed");

    public static string RefusalParseAlreadyRunningHere => Text("MotifApp.RefusalParseAlreadyRunningHere");

    public static string RefusalParseAlreadyRunningElsewhere => Text("MotifApp.RefusalParseAlreadyRunningElsewhere");

    public static string RefusalAssessBaselineChanged => Text("MotifApp.RefusalAssessBaselineChanged");

    public static string RefusalAssessInvalidLimit => Text("MotifApp.RefusalAssessInvalidLimit");

    public static string RefusalAssessInvocationInconsistent => Text("MotifApp.RefusalAssessInvocationInconsistent");

    public static string RefusalAssessMeasurementsIncomplete => Text("MotifApp.RefusalAssessMeasurementsIncomplete");

    public static string RefusalAssessUnsupportedKind => Text("MotifApp.RefusalAssessUnsupportedKind");

    public static string RefusalAssessWordsAmbiguous => Text("MotifApp.RefusalAssessWordsAmbiguous");

    public static string RefusalAssessWordsFileEmpty => Text("MotifApp.RefusalAssessWordsFileEmpty");

    public static string RefusalAssessWordsFileUnreadable => Text("MotifApp.RefusalAssessWordsFileUnreadable");

    public static string RefusalAssessmentCancelled => Text("MotifApp.RefusalAssessmentCancelled");

    public static string RefusalBaselineBusy => Text("MotifApp.RefusalBaselineBusy");

    public static string RefusalBaselineCopyUnloadable => Text("MotifApp.RefusalBaselineCopyUnloadable");

    public static string RefusalBaselineOwnedRootViolation => Text("MotifApp.RefusalBaselineOwnedRootViolation");

    public static string RefusalBaselineSourceIncomplete => Text("MotifApp.RefusalBaselineSourceIncomplete");

    public static string RefusalBaselineTextWordsUnreadable => Text("MotifApp.RefusalBaselineTextWordsUnreadable");

    public static string RefusalChangeAnalysisIdentityRequired => Text("MotifApp.RefusalChangeAnalysisIdentityRequired");

    public static string RefusalChangeAnalysisOwnerInvalid => Text("MotifApp.RefusalChangeAnalysisOwnerInvalid");

    public static string RefusalChangeAssessmentIncomplete => Text("MotifApp.RefusalChangeAssessmentIncomplete");

    public static string RefusalChangeAssessmentKind => Text("MotifApp.RefusalChangeAssessmentKind");

    public static string RefusalChangeAssessmentMissing => Text("MotifApp.RefusalChangeAssessmentMissing");

    public static string RefusalChangeAssessmentRequired => Text("MotifApp.RefusalChangeAssessmentRequired");

    public static string RefusalChangeAssessmentStale => Text("MotifApp.RefusalChangeAssessmentStale");

    public static string RefusalChangeAssessmentWordMissing => Text("MotifApp.RefusalChangeAssessmentWordMissing");

    public static string RefusalChangeBaselineMissing => Text("MotifApp.RefusalChangeBaselineMissing");

    public static string RefusalChangeCannotCompose => Text("MotifApp.RefusalChangeCannotCompose");

    public static string RefusalChangeInvalidIdentity => Text("MotifApp.RefusalChangeInvalidIdentity");

    public static string RefusalChangeNoEffect => Text("MotifApp.RefusalChangeNoEffect");

    public static string RefusalChangeNotFound => Text("MotifApp.RefusalChangeNotFound");

    public static string RefusalChangeOccurrenceUnavailable => Text("MotifApp.RefusalChangeOccurrenceUnavailable");

    public static string RefusalChangeOccurrenceWordformMismatch => Text("MotifApp.RefusalChangeOccurrenceWordformMismatch");

    public static string RefusalChangeProjectSaving => Text("MotifApp.RefusalChangeProjectSaving");

    public static string RefusalChangeReadingMissing => Text("MotifApp.RefusalChangeReadingMissing");

    public static string RefusalChangeReconfirmNotAllowed => Text("MotifApp.RefusalChangeReconfirmNotAllowed");

    public static string RefusalChangeReconfirmUnneeded => Text("MotifApp.RefusalChangeReconfirmUnneeded");

    public static string RefusalChangeRefreshRequired => Text("MotifApp.RefusalChangeRefreshRequired");

    public static string RefusalChangeRevisionConflict => Text("MotifApp.RefusalChangeRevisionConflict");

    public static string RefusalChangeScopeInvalid => Text("MotifApp.RefusalChangeScopeInvalid");

    public static string RefusalChangeSlotOccupied => Text("MotifApp.RefusalChangeSlotOccupied");

    public static string RefusalChangeStoredAnalysisMissing => Text("MotifApp.RefusalChangeStoredAnalysisMissing");

    public static string RefusalChangeTextUnavailable => Text("MotifApp.RefusalChangeTextUnavailable");

    public static string RefusalChangeWordformAmbiguous => Text("MotifApp.RefusalChangeWordformAmbiguous");

    public static string RefusalChangeWordformChanged => Text("MotifApp.RefusalChangeWordformChanged");

    public static string RefusalChangeWordformMissing => Text("MotifApp.RefusalChangeWordformMissing");

    public static string RefusalConfigInvalid => Text("MotifApp.RefusalConfigInvalid");

    public static string RefusalDraftRevisionConflict => Text("MotifApp.RefusalDraftRevisionConflict");

    public static string RefusalGrammarCheckCancelled => Text("MotifApp.RefusalGrammarCheckCancelled");

    public static string RefusalGrammarCheckParserRefused => Text("MotifApp.RefusalGrammarCheckParserRefused");

    public static string RefusalGrammarCheckTimedOut => Text("MotifApp.RefusalGrammarCheckTimedOut");

    public static string RefusalHandoffCancelled => Text("MotifApp.RefusalHandoffCancelled");

    public static string RefusalHandoffDestinationExists => Text("MotifApp.RefusalHandoffDestinationExists");

    public static string RefusalHandoffInvocationMismatch => Text("MotifApp.RefusalHandoffInvocationMismatch");

    public static string RefusalHandoffInvocationNotFound => Text("MotifApp.RefusalHandoffInvocationNotFound");

    public static string RefusalHandoffInvocationRequired => Text("MotifApp.RefusalHandoffInvocationRequired");

    public static string RefusalHandoffSourceUnavailable => Text("MotifApp.RefusalHandoffSourceUnavailable");

    public static string RefusalHandoffStatisticsUnavailable => Text("MotifApp.RefusalHandoffStatisticsUnavailable");

    public static string RefusalHandoffTextNotFound => Text("MotifApp.RefusalHandoffTextNotFound");

    public static string RefusalJobParserUnavailable => Text("MotifApp.RefusalJobParserUnavailable");

    public static string RefusalJobWaitCancelled => Text("MotifApp.RefusalJobWaitCancelled");

    public static string RefusalJobWaitTimeout => Text("MotifApp.RefusalJobWaitTimeout");

    public static string RefusalPreflightUnavailable => Text("MotifApp.RefusalPreflightUnavailable");

    public static string RefusalProjectBackupUnreadable => Text("MotifApp.RefusalProjectBackupUnreadable");

    public static string RefusalProjectBusy => Text("MotifApp.RefusalProjectBusy");

    public static string RefusalProjectInUse => Text("MotifApp.RefusalProjectInUse");

    public static string RefusalProjectInvalid => Text("MotifApp.RefusalProjectInvalid");

    public static string RefusalProjectNotFound => Text("MotifApp.RefusalProjectNotFound");

    public static string RefusalProjectOperationIo => Text("MotifApp.RefusalProjectOperationIo");

    public static string RefusalProjectStoreIo => Text("MotifApp.RefusalProjectStoreIo");

    public static string RefusalProjectUnloadable => Text("MotifApp.RefusalProjectUnloadable");

    public static string RefusalProjectWaitCancelled => Text("MotifApp.RefusalProjectWaitCancelled");

    public static string RefusalReviewAssessmentNotFound => Text("MotifApp.RefusalReviewAssessmentNotFound");

    public static string RefusalReviewNumbersUnavailable => Text("MotifApp.RefusalReviewNumbersUnavailable");

    public static string RefusalReviewWrongAssessmentKind => Text("MotifApp.RefusalReviewWrongAssessmentKind");

    public static string RefusalSelectionDefaultMissing => Text("MotifApp.RefusalSelectionDefaultMissing");

    public static string RefusalSelectionEmpty => Text("MotifApp.RefusalSelectionEmpty");

    public static string RefusalSelectionInvalid => Text("MotifApp.RefusalSelectionInvalid");

    public static string RefusalSelectionTextNotFound => Text("MotifApp.RefusalSelectionTextNotFound");

    public static string RefusalSelectionReaderBusy => Text("MotifApp.RefusalSelectionReaderBusy");

    public static string RefusalSelectionReaderCancelled => Text("MotifApp.RefusalSelectionReaderCancelled");

    public static string RefusalSelectionReaderDisposed => Text("MotifApp.RefusalSelectionReaderDisposed");

    public static string RefusalSelectionReaderInvalid => Text("MotifApp.RefusalSelectionReaderInvalid");

    public static string RefusalTrialScratchGone => Text("MotifApp.RefusalTrialScratchGone");

    public static string RefusalTrialNone => Text("MotifApp.RefusalTrialNone");

    public static string RefusalSelectionReaderNoBaseline => Text("MotifApp.RefusalSelectionReaderNoBaseline");

    public static string RefusalSelectionReaderRangeInvalid => Text("MotifApp.RefusalSelectionReaderRangeInvalid");

    public static string RefusalSelectionReaderOwnershipLimit => Text("MotifApp.RefusalSelectionReaderOwnershipLimit");

    public static string RefusalSelectionReaderProjectPathRequired => Text("MotifApp.RefusalSelectionReaderProjectPathRequired");

    public static string RefusalTextsEvidenceChanged => Text("MotifApp.RefusalTextsEvidenceChanged");

    public static string RefusalStatsCancelled => Text("MotifApp.RefusalStatsCancelled");

    public static string RefusalStatsInvalidEvidence => Text("MotifApp.RefusalStatsInvalidEvidence");

    public static string RefusalStatsNoAssessment => Text("MotifApp.RefusalStatsNoAssessment");

    public static string RefusalStatsNoCache => Text("MotifApp.RefusalStatsNoCache");

    public static string RefusalStatsNoEvidence => Text("MotifApp.RefusalStatsNoEvidence");

    public static string RefusalStatsParserRefused => Text("MotifApp.RefusalStatsParserRefused");

    public static string RefusalStatsTimedOut => Text("MotifApp.RefusalStatsTimedOut");

    public static string RefusalStoreInconsistent => Text("MotifApp.RefusalStoreInconsistent");

    public static string RefusalStoreUnsupported => Text("MotifApp.RefusalStoreUnsupported");

    public static string RefusalTextsWordsCancelled => Text("MotifApp.RefusalTextsWordsCancelled");

    public static string RefusalTimingInvalidOverride => Text("MotifApp.RefusalTimingInvalidOverride");

    public static string RefusalTimingNoAssessment => Text("MotifApp.RefusalTimingNoAssessment");

    public static string RefusalTimingNoBaseline => Text("MotifApp.RefusalTimingNoBaseline");

    public static string RefusalTimingOverrideNotFound => Text("MotifApp.RefusalTimingOverrideNotFound");

    public static string RefusalTimingWordSetNotFound => Text("MotifApp.RefusalTimingWordSetNotFound");

    public static string RefusalTimingWrongKind => Text("MotifApp.RefusalTimingWrongKind");

    public static string RefusalTrialChangesChanged => Text("MotifApp.RefusalTrialChangesChanged");

    public static string RefusalTrialMeasurementIncomplete => Text("MotifApp.RefusalTrialMeasurementIncomplete");

    public static string RefusalNoProjectCode => Text("MotifApp.RefusalNoProjectCode");

    public static string RefusalTrialNothingPending => Text("MotifApp.RefusalTrialNothingPending");

    public static string RefusalTrialUpdateInProgress => Text("MotifApp.RefusalTrialUpdateInProgress");

    public static string RefusalWordTraceCancelled => Text("MotifApp.RefusalWordTraceCancelled");

    public static string RefusalWordTraceMalformedDiagnostic => Text("MotifApp.RefusalWordTraceMalformedDiagnostic");

    public static string RefusalWordTraceNoBaseline => Text("MotifApp.RefusalWordTraceNoBaseline");

    public static string RefusalWordTraceParserRefused => Text("MotifApp.RefusalWordTraceParserRefused");

    public static string RefusalDetailsGrammarIssuesTitle => Text("MotifApp.RefusalDetailsGrammarIssuesTitle");

    public static string RefusalStoreOtherVersionHeading => Text("MotifApp.RefusalStoreOtherVersionHeading");

    public static string StatisticsColumnKind => Text("MotifApp.StatisticsColumnKind");

    public static string StatisticsColumnObject => Text("MotifApp.StatisticsColumnObject");

    public static string StatisticsColumnCompletion => Text("MotifApp.StatisticsColumnCompletion");

    public static string StatisticsColumnAnalyses => Text("MotifApp.StatisticsColumnAnalyses");

    public static string StatisticsColumnElapsedMs => Text("MotifApp.StatisticsColumnElapsedMs");

    public static string TimingDetailedStatisticsHeading => Text("MotifApp.TimingDetailedStatisticsHeading");

    public static string TryWordParserDetailsHeading => Text("MotifApp.TryWordParserDetailsHeading");

    public static string TryWordRecordedTreeContextHeading => Text("MotifApp.TryWordRecordedTreeContextHeading");

    public static string TraceFullMorphDetailsHeading => Text("MotifApp.TraceFullMorphDetailsHeading");

    public static string TraceRawMorphRecordHeading => Text("MotifApp.TraceRawMorphRecordHeading");

    public static string TryWordFullAnalysisDetailsHeading => Text("MotifApp.TryWordFullAnalysisDetailsHeading");

    public static string TraceKindWord => Text("MotifApp.TraceKindWord");

    public static string ExpertAnalysisPathPlaceholder => Text("MotifApp.ExpertAnalysisPathPlaceholder");

    public static string StatisticsFilterPlaceholder => Text("MotifApp.StatisticsFilterPlaceholder");

    public static string ParsimonyReasonPlaceholder => Text("MotifApp.ParsimonyReasonPlaceholder");

    public static string ParsimonyQuestionPlaceholder => Text("MotifApp.ParsimonyQuestionPlaceholder");

    public static string TryWordInputPlaceholder => Text("MotifApp.TryWordInputPlaceholder");

    public static string TimingChooseCellPlaceholder => Text("MotifApp.TimingChooseCellPlaceholder");

    public static string TimingChooseListPlaceholder => Text("MotifApp.TimingChooseListPlaceholder");

    public static string TimingPasteWordsPlaceholder => Text("MotifApp.TimingPasteWordsPlaceholder");

    public static string CompareSearchWordsPlaceholder => Text("MotifApp.CompareSearchWordsPlaceholder");

    public static string ExpertMoreRuleStepsFormat => Text("MotifApp.ExpertMoreRuleStepsFormat");

    public static string ExpertShowingEveryStep => Text("MotifApp.ExpertShowingEveryStep");

    public static string ExpertShowingAttemptPathFormat => Text("MotifApp.ExpertShowingAttemptPathFormat");

    public static string ExpertChooseRecordedEvent => Text("MotifApp.ExpertChooseRecordedEvent");

    public static string RefusalIssueObjectFormat => Text("MotifApp.RefusalIssueObjectFormat");

    public static string RefusalIssueIdentityFormat => Text("MotifApp.RefusalIssueIdentityFormat");

    public static string RefusalIssueFieldFormat => Text("MotifApp.RefusalIssueFieldFormat");

    public static string CompareWordUnitPattern => Text("MotifApp.CompareWordUnitPattern");

    public static string MenuRefresh => Text("MotifApp.MenuRefresh");

    public static string RefreshIconGlyph => Text("MotifApp.RefreshIconGlyph");

    public static string ProjectChipCaret => Text("MotifApp.ProjectChipCaret");

    public static string MainMenuButtonGlyph => Text("MotifApp.MainMenuButtonGlyph");

    public static string MainMenuButtonAccessibleName => Text("MotifApp.MainMenuButtonAccessibleName");

    public static string ProjectChipNameFormat => Text("MotifApp.ProjectChipNameFormat");

    public static string ProjectChipParsedFormat => Text("MotifApp.ProjectChipParsedFormat");

    public static string ProjectChipDetailFormat => Text("MotifApp.ProjectChipDetailFormat");

    public static string ProjectChipJoinFormat => Text("MotifApp.ProjectChipJoinFormat");

    public static string ProjectCardAccessibleName => Text("MotifApp.ProjectCardAccessibleName");

    public static string ProjectCardOpenedFormat => Text("MotifApp.ProjectCardOpenedFormat");

    public static string ProjectCardParsedFormat => Text("MotifApp.ProjectCardParsedFormat");

    public static string ProjectCardNotParsed => Text("MotifApp.ProjectCardNotParsed");

    public static string ProjectCardButtonRefresh => Text("MotifApp.ProjectCardButtonRefresh");

    public static string ProjectCardButtonParseAllWords => Text("MotifApp.ProjectCardButtonParseAllWords");

    public static string ProjectCardButtonSeeWhatChanged => Text("MotifApp.ProjectCardButtonSeeWhatChanged");

    public static string ProjectCardTextsHeading => Text("MotifApp.ProjectCardTextsHeading");

    public static string ProjectCardTextsSummaryFormat => Text("MotifApp.ProjectCardTextsSummaryFormat");

    public static string ProjectCardTextsCountPattern => Text("MotifApp.ProjectCardTextsCountPattern");

    public static string ProjectCardNoTextsChosen => Text("MotifApp.ProjectCardNoTextsChosen");

    public static string ProjectCardButtonChoose => Text("MotifApp.ProjectCardButtonChoose");

    public static string ProjectCardButtonProjectSettings => Text("MotifApp.ProjectCardButtonProjectSettings");

    public static string ProjectCardProjectSettingsAccessibleName => Text("MotifApp.ProjectCardProjectSettingsAccessibleName");

    public static string ProjectCardButtonOpenAnother => Text("MotifApp.ProjectCardButtonOpenAnother");

    public static string ProjectCardOpenAnotherAccessibleName => Text("MotifApp.ProjectCardOpenAnotherAccessibleName");

    public static string TechDemoNoticeGotIt => Text("MotifApp.TechDemoNoticeGotIt");

    public static string TechDemoNoticeGotItAccessibleName => Text("MotifApp.TechDemoNoticeGotItAccessibleName");

    public static string ProjectCardTextsCountFormat => Text("MotifApp.ProjectCardTextsCountFormat");

    public static string OverviewSpeedWordsPattern => Text("MotifApp.OverviewSpeedWordsPattern");

    public static string OverviewSpeedMainFormat => Text("MotifApp.OverviewSpeedMainFormat");

    public static string OverviewSpeedNoTimes => Text("MotifApp.OverviewSpeedNoTimes");

    public static string OverviewSpeedMedianFormat => Text("MotifApp.OverviewSpeedMedianFormat");

    public static string OverviewSpeedMedianWithPercentileFormat => Text("MotifApp.OverviewSpeedMedianWithPercentileFormat");

    public static string OverviewSpeedStoppedPattern => Text("MotifApp.OverviewSpeedStoppedPattern");

    public static string OverviewSlowestSummaryFormat => Text("MotifApp.OverviewSlowestSummaryFormat");

    public static string OverviewSlowestWordFormat => Text("MotifApp.OverviewSlowestWordFormat");

    public static string OverviewSlowestStoppedWordFormat => Text("MotifApp.OverviewSlowestStoppedWordFormat");

    public static string OverviewTimingOverrunFormat => Text("MotifApp.OverviewTimingOverrunFormat");

    public static string TraceParsed => Text("MotifApp.TraceParsed");

    public static string TraceSearchIncomplete => Text("MotifApp.TraceSearchIncomplete");

    public static string TraceAnalysesPattern => Text("MotifApp.TraceAnalysesPattern");

    public static string TraceSourceRecordsPattern => Text("MotifApp.TraceSourceRecordsPattern");

    public static string TraceAnalysesWithRecordsFormat => Text("MotifApp.TraceAnalysesWithRecordsFormat");

    public static string TraceParsedAnalysesFormat => Text("MotifApp.TraceParsedAnalysesFormat");

    public static string TraceAnalysisAttemptsPattern => Text("MotifApp.TraceAnalysisAttemptsPattern");

    public static string TraceGuessed => Text("MotifApp.TraceGuessed");

    public static string TraceOverallFormat => Text("MotifApp.TraceOverallFormat");

    public static string TraceOverallNotRecorded => Text("MotifApp.TraceOverallNotRecorded");

    public static string TraceInParserFormat => Text("MotifApp.TraceInParserFormat");

    public static string TraceParserTimeFormat => Text("MotifApp.TraceParserTimeFormat");

    public static string TraceDroppedPathsPattern => Text("MotifApp.TraceDroppedPathsPattern");

    public static string TraceAttemptsFailedPattern => Text("MotifApp.TraceAttemptsFailedPattern");

    public static string TraceAttemptsPattern => Text("MotifApp.TraceAttemptsPattern");

    public static string TraceClosestAttemptsHeadingOne => Text("MotifApp.TraceClosestAttemptsHeadingOne");

    public static string TraceClosestAttemptsHeadingMany => Text("MotifApp.TraceClosestAttemptsHeadingMany");

    public static string TraceMoreAttemptsCountPattern => Text("MotifApp.TraceMoreAttemptsCountPattern");

    public static string TraceShowOtherFormat => Text("MotifApp.TraceShowOtherFormat");

    public static string TraceShowOtherRefusedByFormat => Text("MotifApp.TraceShowOtherRefusedByFormat");

    public static string TraceSearchIncompleteFormat => Text("MotifApp.TraceSearchIncompleteFormat");

    public static string TraceReasonNotRecorded => Text("MotifApp.TraceReasonNotRecorded");

    public static string TraceSearchComplete => Text("MotifApp.TraceSearchComplete");

    public static string TraceSearchCompletionNotRecorded => Text("MotifApp.TraceSearchCompletionNotRecorded");

    public static string TraceStatusHiddenFormat => Text("MotifApp.TraceStatusHiddenFormat");

    public static string TraceHiddenByFiltersPattern => Text("MotifApp.TraceHiddenByFiltersPattern");

    public static string TraceFilterFailed => Text("MotifApp.TraceFilterFailed");

    public static string TraceFilterSucceeded => Text("MotifApp.TraceFilterSucceeded");

    public static string TraceFilterRuleFormat => Text("MotifApp.TraceFilterRuleFormat");

    public static string TraceFilterMorphFormat => Text("MotifApp.TraceFilterMorphFormat");

    public static string TraceFilterAllSteps => Text("MotifApp.TraceFilterAllSteps");

    public static string TraceStepsHiddenPattern => Text("MotifApp.TraceStepsHiddenPattern");

    public static string TraceFilterHiddenSuffixPattern => Text("MotifApp.TraceFilterHiddenSuffixPattern");

    public static string TraceRecordedTimesPattern => Text("MotifApp.TraceRecordedTimesPattern");

    public static string OverviewSeeWordsPattern => Text("MotifApp.OverviewSeeWordsPattern");

    public static string OverviewStoppedWordsRowPattern => Text("MotifApp.OverviewStoppedWordsRowPattern");

    public static string OverviewStoppedWordsDetailFormat => Text("MotifApp.OverviewStoppedWordsDetailFormat");

    public static string OverviewApprovedDifferentEntryPattern => Text("MotifApp.OverviewApprovedDifferentEntryPattern");

    public static string OverviewSeeLostWordsPattern => Text("MotifApp.OverviewSeeLostWordsPattern");

    public static string OverviewApprovedLostPattern => Text("MotifApp.OverviewApprovedLostPattern");

    public static string OverviewSharedMorphemeUseFormat => Text("MotifApp.OverviewSharedMorphemeUseFormat");

    public static string OverviewSharedMorphemeNamedByWarning => Text("MotifApp.OverviewSharedMorphemeNamedByWarning");

    public static string OverviewSharedMorphemesUnavailable => Text("MotifApp.OverviewSharedMorphemesUnavailable");

    public static string OverviewSharedMorphemesNone => Text("MotifApp.OverviewSharedMorphemesNone");

    public static string OverviewUnknownDifferentPattern => Text("MotifApp.OverviewUnknownDifferentPattern");

    public static string OverviewLostWordsParseFirst => Text("MotifApp.OverviewLostWordsParseFirst");

    public static string OverviewLostWordsNone => Text("MotifApp.OverviewLostWordsNone");

    public static string OverviewLostWordsSome => Text("MotifApp.OverviewLostWordsSome");

    public static string OverviewNoKnownProjects => Text("MotifApp.OverviewNoKnownProjects");

    public static string OverviewSelectionNotResolved => Text("MotifApp.OverviewSelectionNotResolved");

    public static string OverviewSelectionNotSetUp => Text("MotifApp.OverviewSelectionNotSetUp");

    public static string OverviewNotMeasured => Text("MotifApp.OverviewNotMeasured");

    public static string OverviewNotAttributed => Text("MotifApp.OverviewNotAttributed");

    public static string OverviewNotParsedYet => Text("MotifApp.OverviewNotParsedYet");

    public static string OverviewNotRecorded => Text("MotifApp.OverviewNotRecorded");

    public static string OverviewCoverageMainFormat => Text("MotifApp.OverviewCoverageMainFormat");

    public static string OverviewCoverageWordsFormat => Text("MotifApp.OverviewCoverageWordsFormat");

    public static string OverviewCoverageNoPlaces => Text("MotifApp.OverviewCoverageNoPlaces");

    public static string OverviewCoveragePlacesFormat => Text("MotifApp.OverviewCoveragePlacesFormat");

    public static string OverviewLabelSame => Text("MotifApp.OverviewLabelSame");

    public static string OverviewLabelDifferent => Text("MotifApp.OverviewLabelDifferent");

    public static string OverviewLabelNoParse => Text("MotifApp.OverviewLabelNoParse");

    public static string OverviewLabelStopped => Text("MotifApp.OverviewLabelStopped");

    public static string OverviewLabelCantRead => Text("MotifApp.OverviewLabelCantRead");

    public static string OverviewOutcomeSame => Text("MotifApp.OverviewOutcomeSame");

    public static string OverviewOutcomeDifferent => Text("MotifApp.OverviewOutcomeDifferent");

    public static string OverviewOutcomeNoParse => Text("MotifApp.OverviewOutcomeNoParse");

    public static string OverviewOutcomeStopped => Text("MotifApp.OverviewOutcomeStopped");

    public static string OverviewOpenOutcomeWordsFormat => Text("MotifApp.OverviewOpenOutcomeWordsFormat");

    public static string OverviewOpenApprovedWordsFormat => Text("MotifApp.OverviewOpenApprovedWordsFormat");

    public static string OverviewAccuracyNothingApproved => Text("MotifApp.OverviewAccuracyNothingApproved");

    public static string OverviewAccuracyMainFormat => Text("MotifApp.OverviewAccuracyMainFormat");

    public static string OverviewAccuracyBuiltElseUnavailableFormat => Text("MotifApp.OverviewAccuracyBuiltElseUnavailableFormat");

    public static string OverviewAccuracySameFormsFormat => Text("MotifApp.OverviewAccuracySameFormsFormat");

    public static string OverviewAccuracyBuiltDifferentlyFormat => Text("MotifApp.OverviewAccuracyBuiltDifferentlyFormat");

    public static string OverviewAccuracyLostFormat => Text("MotifApp.OverviewAccuracyLostFormat");

    public static string OverviewAccuracyStoppedFormat => Text("MotifApp.OverviewAccuracyStoppedFormat");

    public static string OverviewAccuracyNotParsedFormat => Text("MotifApp.OverviewAccuracyNotParsedFormat");

    public static string OverviewDisapprovedStillBuiltPattern => Text("MotifApp.OverviewDisapprovedStillBuiltPattern");

    public static string OverviewCandidatesConfirmedFormat => Text("MotifApp.OverviewCandidatesConfirmedFormat");

    public static string OverviewLabelKept => Text("MotifApp.OverviewLabelKept");

    public static string OverviewLabelBuiltSomethingElse => Text("MotifApp.OverviewLabelBuiltSomethingElse");

    public static string OverviewLabelLost => Text("MotifApp.OverviewLabelLost");

    public static string OverviewOutcomeKept => Text("MotifApp.OverviewOutcomeKept");

    public static string OverviewOutcomeBuiltSomethingElse => Text("MotifApp.OverviewOutcomeBuiltSomethingElse");

    public static string OverviewOutcomeLost => Text("MotifApp.OverviewOutcomeLost");

    public static string OverviewOutcomeNoResult => Text("MotifApp.OverviewOutcomeNoResult");

    public static string OverviewWarningsParseToSee => Text("MotifApp.OverviewWarningsParseToSee");

    public static string OverviewWarningsNoConfirmedUse => Text("MotifApp.OverviewWarningsNoConfirmedUse");

    public static string OverviewWarningsNoWordUse => Text("MotifApp.OverviewWarningsNoWordUse");

    public static string OverviewWarningsWordsUseFormat => Text("MotifApp.OverviewWarningsWordsUseFormat");

    public static string OverviewSpellingOnlyMatchesPattern => Text("MotifApp.OverviewSpellingOnlyMatchesPattern");

    public static string OverviewWarningErrorsPattern => Text("MotifApp.OverviewWarningErrorsPattern");

    public static string OverviewWarningWarningsPattern => Text("MotifApp.OverviewWarningWarningsPattern");

    public static string OverviewWarningInformationPattern => Text("MotifApp.OverviewWarningInformationPattern");

    public static string OverviewWarningsNotCheckedYet => Text("MotifApp.OverviewWarningsNotCheckedYet");

    public static string OverviewWarningsHeadlineOnly => Text("MotifApp.OverviewWarningsHeadlineOnly");

    public static string OverviewWarningsWithErrorsFormat => Text("MotifApp.OverviewWarningsWithErrorsFormat");

    public static string OverviewReadRefusalNumbers => Text("MotifApp.OverviewReadRefusalNumbers");

    public static string TraceGrammarSourceNotRecorded => Text("MotifApp.TraceGrammarSourceNotRecorded");

    public static string TraceGrammarSourceBaselineFormat => Text("MotifApp.TraceGrammarSourceBaselineFormat");

    public static string TraceGrammarSourceFormat => Text("MotifApp.TraceGrammarSourceFormat");

    public static string TraceProvenanceNotRecorded => Text("MotifApp.TraceProvenanceNotRecorded");

    public static string TraceNoTerminalAttemptStopped => Text("MotifApp.TraceNoTerminalAttemptStopped");

    public static string TraceNoTerminalAttempt => Text("MotifApp.TraceNoTerminalAttempt");

    public static string TraceWhyOtherAttemptsFailed => Text("MotifApp.TraceWhyOtherAttemptsFailed");

    public static string TraceWhyNotParsed => Text("MotifApp.TraceWhyNotParsed");

    public static string TraceWritingSystemsNotRecorded => Text("MotifApp.TraceWritingSystemsNotRecorded");

    public static string TraceWritingSystemFormat => Text("MotifApp.TraceWritingSystemFormat");

    public static string TraceDirectionNotRecorded => Text("MotifApp.TraceDirectionNotRecorded");

    public static string TraceFontUiFallback => Text("MotifApp.TraceFontUiFallback");

    public static string TraceWritingSystemsSummaryFormat => Text("MotifApp.TraceWritingSystemsSummaryFormat");

    public static string TraceLabelValueFormat => Text("MotifApp.TraceLabelValueFormat");

    public static string TraceCaptureFormatLabel => Text("MotifApp.TraceCaptureFormatLabel");

    public static string TraceCaptureParserLabel => Text("MotifApp.TraceCaptureParserLabel");

    public static string TraceCaptureParserVersionLabel => Text("MotifApp.TraceCaptureParserVersionLabel");

    public static string TraceCaptureProfileLabel => Text("MotifApp.TraceCaptureProfileLabel");

    public static string TraceCaptureGrammarHashLabel => Text("MotifApp.TraceCaptureGrammarHashLabel");

    public static string TraceCaptureGrammarHashSemanticsLabel => Text("MotifApp.TraceCaptureGrammarHashSemanticsLabel");

    public static string TraceCaptureProjectIdentityLabel => Text("MotifApp.TraceCaptureProjectIdentityLabel");

    public static string TraceCaptureBundleDigestLabel => Text("MotifApp.TraceCaptureBundleDigestLabel");

    public static string TraceBaselineCapturedFormat => Text("MotifApp.TraceBaselineCapturedFormat");

    public static string TraceSourceSavedFormat => Text("MotifApp.TraceSourceSavedFormat");

    public static string TraceBaselineSourceNotRecorded => Text("MotifApp.TraceBaselineSourceNotRecorded");

    public static string TraceCapturedAtFormat => Text("MotifApp.TraceCapturedAtFormat");

    public static string TraceHostElapsedFormat => Text("MotifApp.TraceHostElapsedFormat");

    public static string TraceCaptureDetailsNotRecorded => Text("MotifApp.TraceCaptureDetailsNotRecorded");

    public static string TraceShowAnalyses => Text("MotifApp.TraceShowAnalyses");

    public static string TraceFocusThisWord => Text("MotifApp.TraceFocusThisWord");

    public static string TraceSurfaceNotRecorded => Text("MotifApp.TraceSurfaceNotRecorded");

    public static string TraceAvailabilityNotRecorded => Text("MotifApp.TraceAvailabilityNotRecorded");

    public static string TraceMorphemesFormat => Text("MotifApp.TraceMorphemesFormat");

    public static string TraceParserMorphemesTipFormat => Text("MotifApp.TraceParserMorphemesTipFormat");

    public static string TraceAnalysisNumberFormat => Text("MotifApp.TraceAnalysisNumberFormat");

    public static string TraceRecordedAnalysis => Text("MotifApp.TraceRecordedAnalysis");

    public static string TraceProjectionErrorText => Text("MotifApp.TraceProjectionErrorText");

    public static string TraceRawMorphNotRecorded => Text("MotifApp.TraceRawMorphNotRecorded");

    public static string TraceValueNotRecorded => Text("MotifApp.TraceValueNotRecorded");

    public static string TraceFieldNotRecordedFormat => Text("MotifApp.TraceFieldNotRecordedFormat");

    public static string TraceMorphFormLabel => Text("MotifApp.TraceMorphFormLabel");

    public static string TraceMorphHeadwordLabel => Text("MotifApp.TraceMorphHeadwordLabel");

    public static string TraceMorphGlossLabel => Text("MotifApp.TraceMorphGlossLabel");

    public static string TraceMorphCategoryLabel => Text("MotifApp.TraceMorphCategoryLabel");

    public static string TraceMorphSlotLabel => Text("MotifApp.TraceMorphSlotLabel");

    public static string TraceMorphInflectionClassLabel => Text("MotifApp.TraceMorphInflectionClassLabel");

    public static string TraceMorphFeaturesLabel => Text("MotifApp.TraceMorphFeaturesLabel");

    public static string TraceMorphGuessedStringLabel => Text("MotifApp.TraceMorphGuessedStringLabel");

    public static string TraceMorphGuessedLabel => Text("MotifApp.TraceMorphGuessedLabel");

    public static string TraceMorphIdentityLabel => Text("MotifApp.TraceMorphIdentityLabel");

    public static string TraceMorphIdentityQualityLabel => Text("MotifApp.TraceMorphIdentityQualityLabel");

    public static string TraceMorphFormIdLabel => Text("MotifApp.TraceMorphFormIdLabel");

    public static string TraceMorphEntryIdLabel => Text("MotifApp.TraceMorphEntryIdLabel");

    public static string TraceMorphMsaIdLabel => Text("MotifApp.TraceMorphMsaIdLabel");

    public static string TraceMorphInflTypeIdLabel => Text("MotifApp.TraceMorphInflTypeIdLabel");

    public static string TraceMorphWritingSystemsLabel => Text("MotifApp.TraceMorphWritingSystemsLabel");

    public static string TraceWsRoleForm => Text("MotifApp.TraceWsRoleForm");

    public static string TraceWsRoleHeadword => Text("MotifApp.TraceWsRoleHeadword");

    public static string TraceWsRoleGloss => Text("MotifApp.TraceWsRoleGloss");

    public static string TraceWsRoleFormat => Text("MotifApp.TraceWsRoleFormat");

    public static string TraceIdentityQualityUnknown => Text("MotifApp.TraceIdentityQualityUnknown");

    public static string TraceOpenEntryFormat => Text("MotifApp.TraceOpenEntryFormat");

    public static string TraceOpenHeadwordFormat => Text("MotifApp.TraceOpenHeadwordFormat");

    public static string TraceMsaNotRecorded => Text("MotifApp.TraceMsaNotRecorded");

    public static string TraceMsaUnavailable => Text("MotifApp.TraceMsaUnavailable");

    public static string TraceMsaKindLabel => Text("MotifApp.TraceMsaKindLabel");

    public static string TraceMsaFromCategoryLabel => Text("MotifApp.TraceMsaFromCategoryLabel");

    public static string TraceMsaToCategoryLabel => Text("MotifApp.TraceMsaToCategoryLabel");

    public static string TraceMsaFromInflectionClassLabel => Text("MotifApp.TraceMsaFromInflectionClassLabel");

    public static string TraceMsaToInflectionClassLabel => Text("MotifApp.TraceMsaToInflectionClassLabel");

    public static string TraceMsaFromFeaturesLabel => Text("MotifApp.TraceMsaFromFeaturesLabel");

    public static string TraceMsaToFeaturesLabel => Text("MotifApp.TraceMsaToFeaturesLabel");

    public static string TraceMsaAttachesToLabel => Text("MotifApp.TraceMsaAttachesToLabel");

    public static string TraceMsaSlotsLabel => Text("MotifApp.TraceMsaSlotsLabel");

    public static string TraceMsaOptionalFormat => Text("MotifApp.TraceMsaOptionalFormat");

    public static string TraceRecordedRefusal => Text("MotifApp.TraceRecordedRefusal");

    public static string TraceDidNotRecordWhy => Text("MotifApp.TraceDidNotRecordWhy");

    public static string TraceSourceKindLower => Text("MotifApp.TraceSourceKindLower");

    public static string TraceSourceKindTitle => Text("MotifApp.TraceSourceKindTitle");

    public static string TraceQualityNotRecorded => Text("MotifApp.TraceQualityNotRecorded");

    public static string TraceSourceIdentityFormat => Text("MotifApp.TraceSourceIdentityFormat");

    public static string TraceSourceIdentityNotRecorded => Text("MotifApp.TraceSourceIdentityNotRecorded");

    public static string TraceRecordedAttempt => Text("MotifApp.TraceRecordedAttempt");

    public static string TraceRefusedByFormat => Text("MotifApp.TraceRefusedByFormat");

    public static string TraceFailureContextNotRecorded => Text("MotifApp.TraceFailureContextNotRecorded");

    public static string TraceStatusBuiltTheWord => Text("MotifApp.TraceStatusBuiltTheWord");

    public static string TraceEffortDidNotApplyFormat => Text("MotifApp.TraceEffortDidNotApplyFormat");

    public static string TraceEffortNoRootFormat => Text("MotifApp.TraceEffortNoRootFormat");

    public static string TraceEffortSurfaceMismatchFormat => Text("MotifApp.TraceEffortSurfaceMismatchFormat");

    public static string TraceNotTimed => Text("MotifApp.TraceNotTimed");

    public static string TraceBuildingStemFormFormat => Text("MotifApp.TraceBuildingStemFormFormat");

    public static string TraceBuildingStemUsedFormat => Text("MotifApp.TraceBuildingStemUsedFormat");

    public static string TraceBuildingStemFoundFormat => Text("MotifApp.TraceBuildingStemFoundFormat");

    public static string TraceBuildingAffix => Text("MotifApp.TraceBuildingAffix");

    public static string TraceBuildingSoundRule => Text("MotifApp.TraceBuildingSoundRule");

    public static string TraceProducerFormat => Text("MotifApp.TraceProducerFormat");

    public static string TraceCapturedFieldWorksFormat => Text("MotifApp.TraceCapturedFieldWorksFormat");

    public static string TraceCapturedKindLabel => Text("MotifApp.TraceCapturedKindLabel");

    public static string TraceCapturedProducerLabel => Text("MotifApp.TraceCapturedProducerLabel");

    public static string TraceCapturedFieldWorksLabel => Text("MotifApp.TraceCapturedFieldWorksLabel");

    public static string TraceCapturedProducerEventIdLabel => Text("MotifApp.TraceCapturedProducerEventIdLabel");

    public static string TraceCapturedTreeAddressLabel => Text("MotifApp.TraceCapturedTreeAddressLabel");

    public static string TraceCapturedSubruleLabel => Text("MotifApp.TraceCapturedSubruleLabel");

    public static string TraceCapturedInputLabel => Text("MotifApp.TraceCapturedInputLabel");

    public static string TraceCapturedOutputLabel => Text("MotifApp.TraceCapturedOutputLabel");

    public static string TraceCapturedOutcomeLabel => Text("MotifApp.TraceCapturedOutcomeLabel");

    public static string TraceCapturedEventLabel => Text("MotifApp.TraceCapturedEventLabel");

    public static string TraceCapturedReasonLabel => Text("MotifApp.TraceCapturedReasonLabel");

    public static string TraceCapturedExplanationLabel => Text("MotifApp.TraceCapturedExplanationLabel");

    public static string TraceCapturedEvaluatorDetailsLabel => Text("MotifApp.TraceCapturedEvaluatorDetailsLabel");

    public static string TraceOutcomeRefused => Text("MotifApp.TraceOutcomeRefused");

    public static string TraceOutcomeTried => Text("MotifApp.TraceOutcomeTried");

    public static string TraceOutcomeApplied => Text("MotifApp.TraceOutcomeApplied");

    public static string TraceOutcomeNotRecorded => Text("MotifApp.TraceOutcomeNotRecorded");

    public static string TraceInputNotRecorded => Text("MotifApp.TraceInputNotRecorded");

    public static string TraceInputShapeTip => Text("MotifApp.TraceInputShapeTip");

    public static string TraceOutputNotRecorded => Text("MotifApp.TraceOutputNotRecorded");

    public static string TraceOutputShapeTip => Text("MotifApp.TraceOutputShapeTip");

    public static string TraceRecordedEventFormat => Text("MotifApp.TraceRecordedEventFormat");

    public static string TraceEventAddressNotRecorded => Text("MotifApp.TraceEventAddressNotRecorded");

    public static string TraceProducerEventWithTreeFormat => Text("MotifApp.TraceProducerEventWithTreeFormat");

    public static string TraceProducerEventNoTreeFormat => Text("MotifApp.TraceProducerEventNoTreeFormat");

    public static string TraceTreeAddressOnlyFormat => Text("MotifApp.TraceTreeAddressOnlyFormat");

    public static string TraceProducerEventNotRecorded => Text("MotifApp.TraceProducerEventNotRecorded");

    public static string TraceRejectionDetailsNotRecorded => Text("MotifApp.TraceRejectionDetailsNotRecorded");

    public static string TraceRejectionDetailsRecorded => Text("MotifApp.TraceRejectionDetailsRecorded");

    public static string TraceRequiredFormat => Text("MotifApp.TraceRequiredFormat");

    public static string TraceActualFormat => Text("MotifApp.TraceActualFormat");

    public static string TraceEnvironmentFormat => Text("MotifApp.TraceEnvironmentFormat");

    public static string TraceEvidenceStatusFormat => Text("MotifApp.TraceEvidenceStatusFormat");

    public static string TraceEvidenceKindFormat => Text("MotifApp.TraceEvidenceKindFormat");

    public static string TraceEvidenceSourceFormat => Text("MotifApp.TraceEvidenceSourceFormat");

    public static string TraceEvidenceReasonCodeFormat => Text("MotifApp.TraceEvidenceReasonCodeFormat");

    public static string TraceEvidenceUnavailableReasonFormat => Text("MotifApp.TraceEvidenceUnavailableReasonFormat");

    public static string TraceEvidenceReasonFormat => Text("MotifApp.TraceEvidenceReasonFormat");

    public static string TraceEvidenceRequiredFormat => Text("MotifApp.TraceEvidenceRequiredFormat");

    public static string TraceEvidenceActualFormat => Text("MotifApp.TraceEvidenceActualFormat");

    public static string TraceEvidenceEnvironmentFormat => Text("MotifApp.TraceEvidenceEnvironmentFormat");

    public static string TraceSubruleFormat => Text("MotifApp.TraceSubruleFormat");

    public static string TraceContextualFailureNotRecorded => Text("MotifApp.TraceContextualFailureNotRecorded");

    public static string TraceStepApplied => Text("MotifApp.TraceStepApplied");

    public static string ReviewApplyLosesAnalysis => Text("MotifApp.ReviewApplyLosesAnalysis");
    public static string ReviewApplyNotReady => Text("MotifApp.ReviewApplyNotReady");
    public static string ReviewApplyDoesNotFit => Text("MotifApp.ReviewApplyDoesNotFit");
    public static string ReviewApplyReconciliationNeeded => Text("MotifApp.ReviewApplyReconciliationNeeded");
    public static string ReviewApplyUncertain => Text("MotifApp.ReviewApplyUncertain");
    public static string ReviewApplyFieldWorksHoldsProject => Text("MotifApp.ReviewApplyFieldWorksHoldsProject");
    public static string ReviewApplyFieldWorksSavedSince => Text("MotifApp.ReviewApplyFieldWorksSavedSince");
    public static string ReviewApplyNotMeasured => Text("MotifApp.ReviewApplyNotMeasured");
    public static string ReviewApplyTrialNeeded => Text("MotifApp.ReviewApplyTrialNeeded");
    public static string ReviewApplyNothingToApply => Text("MotifApp.ReviewApplyNothingToApply");
    public static string ReviewApplyFitNotChecked => Text("MotifApp.ReviewApplyFitNotChecked");
    public static string ReviewApplyApplying => Text("MotifApp.ReviewApplyApplying");
    public static string ReviewApplyMeasuring => Text("MotifApp.ReviewApplyMeasuring");
    public static string ReviewApplyChooseChange => Text("MotifApp.ReviewApplyChooseChange");
    public static string ReviewApply => Text("MotifApp.ReviewApply");
}

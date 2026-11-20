namespace SIL.Motif.App;

/// <summary>
/// The window's localized text. The neutral MotifAppStrings.resx holds the English source and translations
/// would ship as satellite assemblies, as FwAvaloniaStrings does in FieldWorks. A language change takes effect on
/// restart because <c>{x:Static}</c> reads once. Automation ids stay constants in <see cref="AutomationIds"/>.
/// </summary>
public static class MotifAppStrings
{
    // round3-assistant-access
    public static string AssistantAccessHeading => Text("MotifApp.AssistantAccessHeading");
    public static string AssistantAccessExplanation => Text("MotifApp.AssistantAccessExplanation");
    public static string AssistantAccessSwitch => Text("MotifApp.AssistantAccessSwitch");
    public static string MeasureCheckingWords => Text("MotifApp.MeasureCheckingWords");
    public static string MeasurePreparingWords => Text("MotifApp.MeasurePreparingWords");
    public static string MeasureWordsCheckedFormat => Text("MotifApp.MeasureWordsCheckedFormat");
    public static string HelpCurrentPageAccessibleName => Text("MotifApp.HelpCurrentPageAccessibleName");
    public static string HelpCurrentPageTip => Text("MotifApp.HelpCurrentPageTip");
    public static string GrammarGuide => Text("MotifApp.GrammarGuide");
    public static string BoardSources => Text("MotifApp.BoardSources");
    public static string BoardPassage => Text("MotifApp.BoardPassage");
    public static string BoardApproved => Text("MotifApp.BoardApproved");
    public static string BoardGuessed => Text("MotifApp.BoardGuessed");
    public static string BoardNoAnalysis => Text("MotifApp.BoardNoAnalysis");
    public static string BoardOpenProposals => Text("MotifApp.BoardOpenProposals");
    public static string ProposalsWaitingForYou => Text("MotifApp.ProposalsWaitingForYou");
    public static string ProposalsNeedsAnotherTry => Text("MotifApp.ProposalsNeedsAnotherTry");
    public static string ProposalsNotTried => Text("MotifApp.ProposalsNotTried");
    public static string ProposalsPendingChanges => Text("MotifApp.ProposalsPendingChanges");
    public static string ProposalsClosed => Text("MotifApp.ProposalsClosed");
    public static string ProposalsHideClosed => Text("MotifApp.ProposalsHideClosed");
    public static string ProposalsShowClosedFormat => Text("MotifApp.ProposalsShowClosedFormat");
    public static string BoardCompareProposals => Text("MotifApp.BoardCompareProposals");
    public static string BoardWillFillIn => Text("MotifApp.BoardWillFillIn");
    public static string BoardWillChange => Text("MotifApp.BoardWillChange");
    public static string BoardWillStopFillingIn => Text("MotifApp.BoardWillStopFillingIn");
    public static string BoardComments => Text("MotifApp.BoardComments");
    public static string BoardDiagnostics => Text("MotifApp.BoardDiagnostics");
    public static string BoardSpeedByRule => Text("MotifApp.BoardSpeedByRule");
    public static string BoardWhyThisAnalysis => Text("MotifApp.BoardWhyThisAnalysis");
    public static string BoardOpenInFieldWorks => Text("MotifApp.BoardOpenInFieldWorks");
    public static string BoardNotAWord => Text("MotifApp.BoardNotAWord");
    public static string BoardApply => Text("MotifApp.BoardApply");
    public static string BoardDecline => Text("MotifApp.BoardDecline");
    public static string BoardFullTrace => Text("MotifApp.BoardFullTrace");
    public static string BoardKeepAnalysis => Text("MotifApp.BoardKeepAnalysis");
    public static string BoardNextGap => Text("MotifApp.BoardNextGap");
    public static string BoardKeepAllLikeThis => Text("MotifApp.BoardKeepAllLikeThis");
    public static string HealthMedianWorkFormat => Text("MotifApp.HealthMedianWorkFormat");
    public static string HealthTurnOnDiagnostics => Text("MotifApp.HealthTurnOnDiagnostics");
    public static string Health => Text("MotifApp.Health");
    public static string Progress => Text("MotifApp.Progress");
    public static string ProgressNext => Text("MotifApp.ProgressNext");
    public static string ProgressWaitingProposalsPattern => Text("MotifApp.ProgressWaitingProposalsPattern");
    public static string ProgressSelectionFormat => Text("MotifApp.ProgressSelectionFormat");
    public static string SettingsShortcutTip => Text("MotifApp.SettingsShortcutTip");
    public static string ApplyNoLongerFitsPattern => Text("MotifApp.ApplyNoLongerFitsPattern");
    public static string ApplyUncertainPattern => Text("MotifApp.ApplyUncertainPattern");
    public static string ApplyFitNotCheckedSentence => Text("MotifApp.ApplyFitNotCheckedSentence");
    public static string ApplyRemoveNonFitting => Text("MotifApp.ApplyRemoveNonFitting");
    public static string ApplyReconciliationSentence => Text("MotifApp.ApplyReconciliationSentence");
    public static string ApplyNothingSentence => Text("MotifApp.ApplyNothingSentence");
    public static string ApplyFieldWorksHeldSentence => Text("MotifApp.ApplyFieldWorksHeldSentence");
    public static string ApplyStaleSentence => Text("MotifApp.ApplyStaleSentence");
    public static string ApplyLostCountPattern => Text("MotifApp.ApplyLostCountPattern");
    public static string ApplyLostSentenceFormat => Text("MotifApp.ApplyLostSentenceFormat");
    public static string ApplyNotMeasuredSentence => Text("MotifApp.ApplyNotMeasuredSentence");
    public static string ApplyCheckChanges => Text("MotifApp.ApplyCheckChanges");
    public static string ApplyIncompleteMeasurement => Text("MotifApp.ApplyIncompleteMeasurement");
    public static string ApplyReadinessUnavailable => Text("MotifApp.ApplyReadinessUnavailable");
    public static string PageAccessibleNameFormat => Text("MotifApp.PageAccessibleNameFormat");
    public static string BoardProgress => Text("MotifApp.BoardProgress");
    public static string BoardTexts => Text("MotifApp.BoardTexts");
    public static string BoardWords => Text("MotifApp.BoardWords");
    public static string BoardGrammarGuide => Text("MotifApp.BoardGrammarGuide");
    public static string BoardProposals => Text("MotifApp.BoardProposals");
    public static string BoardSettings => Text("MotifApp.BoardSettings");
    public static string BoardWarnings => Text("MotifApp.BoardWarnings");
    public static string BoardUnused => Text("MotifApp.BoardUnused");
    public static string BoardAmbiguity => Text("MotifApp.BoardAmbiguity");
    public static string BoardTemplate => Text("MotifApp.BoardTemplate");
    public static string BoardSpeed => Text("MotifApp.BoardSpeed");
    public static string BoardOther => Text("MotifApp.BoardOther");
    public static string BoardRule => Text("MotifApp.BoardRule");
    public static string BoardStepsPerWord => Text("MotifApp.BoardStepsPerWord");
    public static string BoardShowWords => Text("MotifApp.BoardShowWords");
    public static string BoardHideThisWarning => Text("MotifApp.BoardHideThisWarning");
    public static string BoardDraft => Text("MotifApp.BoardDraft");
    public static string BoardReady => Text("MotifApp.BoardReady");
    public static string BoardProposal => Text("MotifApp.BoardProposal");
    public static string BoardYourTexts => Text("MotifApp.BoardYourTexts");
    public static string BoardNext => Text("MotifApp.BoardNext");
    public static string BoardThisWeek => Text("MotifApp.BoardThisWeek");
    public static string BoardDecisions => Text("MotifApp.BoardDecisions");
    public static string BoardExamplesFromYourTexts => Text("MotifApp.BoardExamplesFromYourTexts");
    public static string BoardInTheGrammar => Text("MotifApp.BoardInTheGrammar");
    public static string BoardHistory => Text("MotifApp.BoardHistory");
    public static string BoardEdit => Text("MotifApp.BoardEdit");
    public static string BoardWhereTheyDiffer => Text("MotifApp.BoardWhereTheyDiffer");
    public static string BoardShowInTexts => Text("MotifApp.BoardShowInTexts");
    public static string BoardChangesToTheGrammar => Text("MotifApp.BoardChangesToTheGrammar");
    public static string BoardApplyTheFirst => Text("MotifApp.BoardApplyTheFirst");
    public static string BoardApplyTheSecond => Text("MotifApp.BoardApplyTheSecond");
    public static string BoardApproveHere => Text("MotifApp.BoardApproveHere");
    public static string BoardApprovePlaces => Text("MotifApp.BoardApprovePlaces");
    public static string BoardAnalysis => Text("MotifApp.BoardAnalysis");
    public static string BoardEveryPlace => Text("MotifApp.BoardEveryPlace");
    public static string BoardTriedAndFailed => Text("MotifApp.BoardTriedAndFailed");
    public static string BoardCompare => Text("MotifApp.BoardCompare");
    public static string BoardFieldWorksClosed => Text("MotifApp.BoardFieldWorksClosed");
    public static string BoardParserUpdated => Text("MotifApp.BoardParserUpdated");
    public static string BoardNoAnalysisYet => Text("MotifApp.BoardNoAnalysisYet");
    public static string BoardAddSource => Text("MotifApp.BoardAddSource");
    public static string BoardFieldWorksTexts => Text("MotifApp.BoardFieldWorksTexts");
    public static string BoardParatext => Text("MotifApp.BoardParatext");
    public static string BoardFile => Text("MotifApp.BoardFile");
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


    public static string ReviewCardEvidenceChanged => Text("MotifApp.ReviewCardEvidenceChanged");

    public static string ShortcutPagesRail => Text("MotifApp.ShortcutPagesRail");

    public static string ShortcutMenus => Text("MotifApp.ShortcutMenus");

    public static string ShortcutActivateMenuItem => Text("MotifApp.ShortcutActivateMenuItem");

    public static string ShortcutOpenMenu => Text("MotifApp.ShortcutOpenMenu");

    public static string ShortcutToggleSidePanel => Text("MotifApp.ShortcutToggleSidePanel");

    public static string ShortcutDiagnosticTree => Text("MotifApp.ShortcutDiagnosticTree");

    public static string ShortcutMorpheme => Text("MotifApp.ShortcutMorpheme");

    public static string ShortcutWindow => Text("MotifApp.ShortcutWindow");

    public static string ShortcutEditing => Text("MotifApp.ShortcutEditing");

    public static string SelectedWords => Text("MotifApp.SelectedWords");

    public static string MenuDiagnostics => Text("MotifApp.MenuDiagnostics");

    public static string DiagnosticsGroup => Text("MotifApp.DiagnosticsGroup");


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




    public static string ShortcutOpenDispositionMenu => Text("MotifApp.ShortcutOpenDispositionMenu");

    public static string ShortcutPreviousRow => Text("MotifApp.ShortcutPreviousRow");

    public static string ShortcutNextRow => Text("MotifApp.ShortcutNextRow");

    public static string ShortcutWordPreviousScreen => Text("MotifApp.ShortcutWordPreviousScreen");

    public static string ShortcutWordNextScreen => Text("MotifApp.ShortcutWordNextScreen");

    public static string ShortcutFirstRow => Text("MotifApp.ShortcutFirstRow");

    public static string ShortcutLastRow => Text("MotifApp.ShortcutLastRow");

    public static string ShortcutPreviousDiagnosticItem => Text("MotifApp.ShortcutPreviousDiagnosticItem");

    public static string ShortcutNextDiagnosticItem => Text("MotifApp.ShortcutNextDiagnosticItem");

    public static string ShortcutFocusWordSearch => Text("MotifApp.ShortcutFocusWordSearch");


    public static string ShortcutCompareVersions => Text("MotifApp.ShortcutCompareVersions");

    public static string ShortcutCompareSideBySide => Text("MotifApp.ShortcutCompareSideBySide");

    public static string ShortcutCompareUseMine => Text("MotifApp.ShortcutCompareUseMine");

    public static string ShortcutCompareUseFieldWorks => Text("MotifApp.ShortcutCompareUseFieldWorks");

    public static string ShortcutCompareUseBoth => Text("MotifApp.ShortcutCompareUseBoth");

    public static string ShortcutCompareEditSideBySide => Text("MotifApp.ShortcutCompareEditSideBySide");

    public static string ShortcutCompareNextChoice => Text("MotifApp.ShortcutCompareNextChoice");

    public static string ShortcutComparePreviousChoice => Text("MotifApp.ShortcutComparePreviousChoice");

    public static string ShortcutCompareOpenSideBySide => Text("MotifApp.ShortcutCompareOpenSideBySide");

    public static string ShortcutCompareDecideLater => Text("MotifApp.ShortcutCompareDecideLater");

    public static string ShortcutCompareUseThis => Text("MotifApp.ShortcutCompareUseThis");

    public static string ShortcutCompareSwitchSide => Text("MotifApp.ShortcutCompareSwitchSide");

    public static string ShortcutCompareBackToCombined => Text("MotifApp.ShortcutCompareBackToCombined");

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

    public static string MenuCorpus => Text("MotifApp.MenuCorpus");

    public static string CorpusTitle => Text("MotifApp.CorpusTitle");

    public static string CorpusCorpora => Text("MotifApp.CorpusCorpora");

    public static string CorpusDocuments => Text("MotifApp.CorpusDocuments");

    public static string CorpusSource => Text("MotifApp.CorpusSource");

    public static string CorpusEditorPhrase0 => Text("MotifApp.CorpusEditorPhrase0");

    public static string CorpusEditorPhrase1 => Text("MotifApp.CorpusEditorPhrase1");

    public static string CorpusEditorPhrase2 => Text("MotifApp.CorpusEditorPhrase2");

    public static string CorpusEditorPhrase3 => Text("MotifApp.CorpusEditorPhrase3");

    public static string CorpusEditorPhrase4 => Text("MotifApp.CorpusEditorPhrase4");

    public static string CorpusEditorPhrase5 => Text("MotifApp.CorpusEditorPhrase5");

    public static string CorpusEditorPhrase6 => Text("MotifApp.CorpusEditorPhrase6");

    public static string CorpusEditorPhrase7 => Text("MotifApp.CorpusEditorPhrase7");

    public static string CorpusEditorPhrase8 => Text("MotifApp.CorpusEditorPhrase8");

    public static string CorpusEditorPhrase9 => Text("MotifApp.CorpusEditorPhrase9");

    public static string CorpusEditorPhrase10 => Text("MotifApp.CorpusEditorPhrase10");

    public static string CorpusEditorPhrase11 => Text("MotifApp.CorpusEditorPhrase11");

    public static string CorpusEditorPhrase12 => Text("MotifApp.CorpusEditorPhrase12");

    public static string CorpusEditorPhrase13 => Text("MotifApp.CorpusEditorPhrase13");

    public static string CorpusEditorPhrase14 => Text("MotifApp.CorpusEditorPhrase14");

    public static string CorpusEditorPhrase15 => Text("MotifApp.CorpusEditorPhrase15");

    public static string CorpusEditorPhrase16 => Text("MotifApp.CorpusEditorPhrase16");

    public static string CorpusEditorPhrase17 => Text("MotifApp.CorpusEditorPhrase17");

    public static string CorpusEditorPhrase18 => Text("MotifApp.CorpusEditorPhrase18");

    public static string CorpusEditorPhrase19 => Text("MotifApp.CorpusEditorPhrase19");

    public static string CorpusEditorPhrase20 => Text("MotifApp.CorpusEditorPhrase20");

    public static string CorpusEditorPhrase21 => Text("MotifApp.CorpusEditorPhrase21");

    public static string CorpusEditorPhrase22 => Text("MotifApp.CorpusEditorPhrase22");

    public static string CorpusEditorPhrase23 => Text("MotifApp.CorpusEditorPhrase23");

    public static string CorpusEditorPhrase24 => Text("MotifApp.CorpusEditorPhrase24");

    public static string CorpusEditorPhrase25 => Text("MotifApp.CorpusEditorPhrase25");

    public static string CorpusEditorPhrase26 => Text("MotifApp.CorpusEditorPhrase26");

    public static string CorpusEditorPhrase27 => Text("MotifApp.CorpusEditorPhrase27");

    public static string CorpusEditorPhrase28 => Text("MotifApp.CorpusEditorPhrase28");

    internal static IReadOnlyDictionary<string, string> CorpusEditorPhrases => new Dictionary<string, string>
    {
        ["Find"] = CorpusEditorPhrase0,
        ["Replace"] = CorpusEditorPhrase1,
        ["next"] = CorpusEditorPhrase2,
        ["previous"] = CorpusEditorPhrase3,
        ["all"] = CorpusEditorPhrase4,
        ["match case"] = CorpusEditorPhrase5,
        ["regexp"] = CorpusEditorPhrase6,
        ["by word"] = CorpusEditorPhrase7,
        ["replace"] = CorpusEditorPhrase8,
        ["replace all"] = CorpusEditorPhrase9,
        ["close"] = CorpusEditorPhrase10,
        ["Go to line"] = CorpusEditorPhrase11,
        ["go"] = CorpusEditorPhrase12,
        ["current match"] = CorpusEditorPhrase13,
        ["on line"] = CorpusEditorPhrase14,
        ["replaced match on line $"] = CorpusEditorPhrase15,
        ["replaced $ matches"] = CorpusEditorPhrase16,
        ["Control character"] = CorpusEditorPhrase17,
        ["Selection deleted"] = CorpusEditorPhrase18,
        ["folded code"] = CorpusEditorPhrase19,
        ["to"] = CorpusEditorPhrase20,
        ["unfold"] = CorpusEditorPhrase21,
        ["Folded lines"] = CorpusEditorPhrase22,
        ["Unfolded lines"] = CorpusEditorPhrase23,
        ["Fold line"] = CorpusEditorPhrase24,
        ["Unfold line"] = CorpusEditorPhrase25,
        ["Completions"] = CorpusEditorPhrase26,
        ["Diagnostics"] = CorpusEditorPhrase27,
        ["No diagnostics"] = CorpusEditorPhrase28,
    };

    public static string CorpusContexts => Text("MotifApp.CorpusContexts");

    public static string CorpusWordforms => Text("MotifApp.CorpusWordforms");

    public static string CorpusBoundariesNotice => Text("MotifApp.CorpusBoundariesNotice");

    public static string CorpusNoProject => Text("MotifApp.CorpusNoProject");

    public static string CorpusUnavailable => Text("MotifApp.CorpusUnavailable");

    public static string MenuWritingSystems => Text("MotifApp.MenuWritingSystems");

    public static string MenuHelp => Text("MotifApp.MenuHelp");

    public static string MenuHelpTopics => Text("MotifApp.MenuHelpTopics");

    public static string MenuKeyboardShortcuts => Text("MotifApp.MenuKeyboardShortcuts");

    public static string MenuReportProblem => Text("MotifApp.MenuReportProblem");

    public static string MenuRecentPreviousPattern => Text("MotifApp.MenuRecentPreviousPattern");

    public static string MenuRecentMorePattern => Text("MotifApp.MenuRecentMorePattern");

    public static string MainMenuAccessibleName => Text("MotifApp.MainMenuAccessibleName");

    public static string SeeWhatChanged => Text("MotifApp.SeeWhatChanged");

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

    public static string TextsWordChangesPattern => Text("MotifApp.TextsWordChangesPattern");

    public static string EditMorphemeInFieldWorksFormat => Text("MotifApp.EditMorphemeInFieldWorksFormat");

    public static string StatusFilterHidesPattern => Text("MotifApp.StatusFilterHidesPattern");

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

    public static string TrialWorkNotice => Text("MotifApp.TrialWorkNotice");

    public static string PairedProposalOnly => Text("MotifApp.PairedProposalOnly");

    public static string PairedProposalNotMeasuredNote => Text("MotifApp.PairedProposalNotMeasuredNote");

    public static string PairedTodayNotMeasuredNote => Text("MotifApp.PairedTodayNotMeasuredNote");

    public static string ProposalWorkNotComparable => Text("MotifApp.ProposalWorkNotComparable");
    public static string ParserWorkComparisonUnavailable => Text("MotifApp.ParserWorkComparisonUnavailable");

    public static string TrialWorkNoticeAccessibleName => Text("MotifApp.TrialWorkNoticeAccessibleName");

    public static string Overview => Text("MotifApp.Overview");

    public static string TryAgain => Text("MotifApp.TryAgain");

    public static string Places => Text("MotifApp.Places");

    public static string Wordforms => Text("MotifApp.Wordforms");

    public static string Rules => Text("MotifApp.Rules");

    public static string OpenFolder => Text("MotifApp.OpenFolder");

    public static string LookFirst => Text("MotifApp.LookFirst");

    public static string TextCoverage => Text("MotifApp.TextCoverage");

    public static string Speed => Text("MotifApp.Speed");

    public static string SlowestWords => Text("MotifApp.SlowestWords");

    public static string GrammarWarnings => Text("MotifApp.GrammarWarnings");

    public static string LostWords => Text("MotifApp.LostWords");

    public static string Words => Text("MotifApp.Words");


    public static string WhatChanged => Text("MotifApp.WhatChanged");

    public static string ReadText => Text("MotifApp.ReadText");

    public static string WordList => Text("MotifApp.WordList");

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

    public static string CloseTheWordCardAccessibleName => Text("MotifApp.CloseTheWordCardAccessibleName");

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

    public static string ParserWorkLimitPerWord => Text("MotifApp.ParserWorkLimitPerWord");

    public static string SelectionParserWorkLimitPerWordAccessibleName => Text("MotifApp.SelectionParserWorkLimitPerWordAccessibleName");

    public static string NoParserWorkLimit => Text("MotifApp.NoParserWorkLimit");

    public static string SelectionHasNoParserWorkLimitAccessibleName => Text("MotifApp.SelectionHasNoParserWorkLimitAccessibleName");

    public static string TimeLimitPerWordSeconds => Text("MotifApp.TimeLimitPerWordSeconds");

    public static string SelectionTimeLimitPerWordInAccessibleName => Text("MotifApp.SelectionTimeLimitPerWordInAccessibleName");

    public static string UseMotifSEstimate => Text("MotifApp.UseMotifSEstimate");

    public static string UseMotifSEstimatedTimeLimitAccessibleName => Text("MotifApp.UseMotifSEstimatedTimeLimitAccessibleName");

    public static string SaveADefaultSelectionToEdit => Text("MotifApp.SaveADefaultSelectionToEdit");

    public static string RetryWorkOverLabel => Text("MotifApp.RetryWorkOverLabel");

    public static string RetryWorkOverAccessibleName => Text("MotifApp.RetryWorkOverAccessibleName");

    public static string Decline => Text("MotifApp.Decline");

    public static string Show => Text("MotifApp.Show");

    public static string Previous => Text("MotifApp.Previous");

    public static string Next => Text("MotifApp.Next");

    public static string Keep => Text("MotifApp.Keep");

    public static string Undo => Text("MotifApp.Undo");

    public static string WordChange => Text("MotifApp.WordChange");

    public static string Analysis => Text("MotifApp.Analysis");

    public static string Where => Text("MotifApp.Where");

    public static string Fits => Text("MotifApp.Fits");

    public static string Project => Text("MotifApp.Project");

    public static string Details => Text("MotifApp.Details");

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

    public static string PicturesTitle => Text("MotifApp.PicturesTitle");
    public static string AddPicture => Text("MotifApp.AddPicture");
    public static string RemovePicture => Text("MotifApp.RemovePicture");

    public static string ConfirmRemovePicture => Text("MotifApp.ConfirmRemovePicture");

    public static string ParseSelectionSummaryFormat => Text("MotifApp.ParseSelectionSummaryFormat");

    public static string ProposalParsimonyFindingsHeader => Text("MotifApp.ProposalParsimonyFindingsHeader");

    public static string ProposalReadyExplanation => Text("MotifApp.ProposalReadyExplanation");

    public static string ProposalParsimonyNotAvailableYet => Text("MotifApp.ProposalParsimonyNotAvailableYet");
    public static string NoPicturesYet => Text("MotifApp.NoPicturesYet");
    public static string PictureNotRead => Text("MotifApp.PictureNotRead");
    public static string PictureNotSupported => Text("MotifApp.PictureNotSupported");
    public static string PictureTooBig => Text("MotifApp.PictureTooBig");
    public static string PictureDescription => Text("MotifApp.PictureDescription");
    public static string PictureCountFormat => Text("MotifApp.PictureCountFormat");
    public static string PictureLimitReached => Text("MotifApp.PictureLimitReached");
    public static string PictureCaptionWatermark => Text("MotifApp.PictureCaptionWatermark");
    public static string PicturesAccessibleName => Text("MotifApp.PicturesAccessibleName");
    public static string ReadingProposals => Text("MotifApp.ReadingProposals");

    public static string NoProposalsYet => Text("MotifApp.NoProposalsYet");

    public static string Status => Text("MotifApp.Status");

    public static string BuiltBy => Text("MotifApp.BuiltBy");

    /// <summary>The row action that enters the selected Proposal's lens.</summary>
    /// <summary>The heading for a Proposal's explanation.</summary>
    public static string ProposalRationale => Text("MotifApp.ProposalRationale");

    /// <summary>The heading for a Proposal's operations.</summary>
    /// <summary>Warns that a Proposal's Rationale describes older content.</summary>
    /// <summary>The button that makes the Rationale editable.</summary>
    public static string RationaleEdit => Text("MotifApp.RationaleEdit");

    /// <summary>Opens a comparison with the Rationale changed elsewhere.</summary>
    public static string RationaleCompare => Text("MotifApp.RationaleCompare");

    /// <summary>Explains how the comparison is shown and when its result is saved.</summary>
    public static string RationaleCompareExplanation => Text("MotifApp.RationaleCompareExplanation");

    /// <summary>Explains how to keep the text when the current Rationale cannot be identified for comparison.</summary>
    public static string RationaleCompareReload => Text("MotifApp.RationaleCompareReload");

    /// <summary>Instructions appended when a side-by-side comparison still needs a choice.</summary>
    public static string SideBySideWaiting => Text("MotifApp.SideBySideWaiting");

    /// <summary>Names a local unsaved edit kept for Restore when starting again from another Rationale.</summary>
    public static string RationaleUnsavedRestoreFormat => Text("MotifApp.RationaleUnsavedRestoreFormat");

    /// <summary>Says the Rationale changed elsewhere during the edit.</summary>
    public static string RationaleChangedElsewhere => Text("MotifApp.RationaleChangedElsewhere");

    /// <summary>The Comment or its Proposal changed while editing.</summary>
    public static string CommentChangedElsewhere => Text("MotifApp.CommentChangedElsewhere");

    /// <summary>The group filter is temporarily paused during a Proposal edit.</summary>
    /// <summary>Asks to save or cancel the Rationale edit before switching.</summary>
    public static string RationaleSaveOrCancelFirst => Text("MotifApp.RationaleSaveOrCancelFirst");

    /// <summary>The button that saves the edited Rationale.</summary>
    public static string RationaleSave => Text("MotifApp.RationaleSave");

    /// <summary>Heading over the findings of a Rationale.</summary>
    public static string RationaleFindingsHeading => Text("MotifApp.RationaleFindingsHeading");

    /// <summary>Heading over the earlier versions of a Rationale.</summary>
    public static string RationaleRestoreHeading => Text("MotifApp.RationaleRestoreHeading");

    /// <summary>The button that puts an earlier Rationale into the field.</summary>
    public static string RationaleRestore => Text("MotifApp.RationaleRestore");

    /// <summary>Formats when an earlier Rationale was saved and by whom.</summary>
    public static string RationaleRevisionFormat => Text("MotifApp.RationaleRevisionFormat");

    /// <summary>Names a person who wrote an earlier Rationale without a name.</summary>
    public static string RationaleRevisionAPerson => Text("MotifApp.RationaleRevisionAPerson");

    /// <summary>Notes that a Rationale is shown as plain text.</summary>
    public static string RationalePlainText => Text("MotifApp.RationalePlainText");

    /// <summary>The menu item that opens a link in the Rationale.</summary>
    public static string RationaleOpenLink => Text("MotifApp.RationaleOpenLink");

    /// <summary>Formats an operation description and its affected word count phrase.</summary>
    public static string ProposalOperationSentenceFormat => Text("MotifApp.ProposalOperationSentenceFormat");

    /// <summary>Formats an operation description when no affected word count is known.</summary>
    public static string ProposalOperationSentenceNoCountFormat => Text("MotifApp.ProposalOperationSentenceNoCountFormat");

    /// <summary>Formats an operation that makes one named thing.</summary>
    public static string ProposalOperationNamedFormat => Text("MotifApp.ProposalOperationNamedFormat");

    /// <summary>The plural pattern for how many words an operation affects.</summary>
    public static string ProposalOperationAffectsPattern => Text("MotifApp.ProposalOperationAffectsPattern");

    /// <summary>The heading for optional Proposal checks.</summary>
    /// <summary>Opens the confirmation for declining a Proposal.</summary>
    public static string DeclineProposalPrompt => Text("MotifApp.DeclineProposalPrompt");

    /// <summary>Leaves a Proposal for a later decision.</summary>
    public static string DecideLater => Text("MotifApp.DecideLater");

    /// <summary>Labels an optional reason for declining.</summary>
    public static string ProposalDeclineReason => Text("MotifApp.ProposalDeclineReason");

    /// <summary>Confirms that a Proposal was declined.</summary>
    public static string ProposalDeclinedNotice => Text("MotifApp.ProposalDeclinedNotice");

    /// <summary>Names the stages of Proposal review.</summary>
    /// <summary>Formats the Trial's approved readings kept and lost.</summary>
    public static string ProposalVerdictFormat => Text("MotifApp.ProposalVerdictFormat");

    /// <summary>Says no approved readings were lost.</summary>
    public static string ProposalLosesNone => Text("MotifApp.ProposalLosesNone");

    /// <summary>Names the person as the builder of their own pending changes.</summary>
    public static string ProposalBuilderYou => Text("MotifApp.ProposalBuilderYou");

    /// <summary>Used when a Proposal's builder was not recorded.</summary>
    public static string ProposalBuilderUnknown => Text("MotifApp.ProposalBuilderUnknown");

    /// <summary>Formats the builder in a Proposal header.</summary>
    public static string ProposalBuiltByFormat => Text("MotifApp.ProposalBuiltByFormat");

    /// <summary>Used when a Proposal's age is unknown.</summary>
    public static string ProposalAgeUnknown => Text("MotifApp.ProposalAgeUnknown");

    /// <summary>Used for a Proposal finalized today.</summary>
    public static string ProposalAgeToday => Text("MotifApp.ProposalAgeToday");

    /// <summary>Plural pattern for days since a Proposal was finalized.</summary>
    public static string ProposalAgeDaysPattern => Text("MotifApp.ProposalAgeDaysPattern");

    /// <summary>Formats the date of a Proposal's earliest finalized revision.</summary>
    public static string ProposalFinalizedFormat => Text("MotifApp.ProposalFinalizedFormat");

    /// <summary>Formats the facts on a Proposal row.</summary>
    public static string ProposalDecisionFactsFormat => Text("MotifApp.ProposalDecisionFactsFormat");

    /// <summary>Shown when no model was recorded for a Proposal.</summary>
    public static string ProposalModelUnknown => Text("MotifApp.ProposalModelUnknown");

    /// <summary>Formats a Proposal's recorded model.</summary>
    public static string ProposalModelFormat => Text("MotifApp.ProposalModelFormat");

    /// <summary>Shown when no question was recorded for a Proposal.</summary>
    public static string ProposalQuestionUnknown => Text("MotifApp.ProposalQuestionUnknown");

    /// <summary>Formats a Proposal's recorded question.</summary>
    public static string ProposalQuestionFormat => Text("MotifApp.ProposalQuestionFormat");

    /// <summary>The state of a missing Rationale.</summary>
    public static string ProposalRationaleMissing => Text("MotifApp.ProposalRationaleMissing");

    /// <summary>The state of a Rationale not yet read.</summary>
    public static string ProposalUnread => Text("MotifApp.ProposalUnread");

    /// <summary>The state of a Rationale already read.</summary>
    public static string ProposalReadState => Text("MotifApp.ProposalReadState");

    /// <summary>Says a Proposal was applied.</summary>
    public static string ProposalApplied => Text("MotifApp.ProposalApplied");

    /// <summary>Says a Proposal was not applied.</summary>
    public static string ProposalNotApplied => Text("MotifApp.ProposalNotApplied");

    /// <summary>Formats how many review entries were looked at.</summary>
    public static string ProposalReviewProgressFormat => Text("MotifApp.ProposalReviewProgressFormat");

    /// <summary>Formats the current states shown below the Proposal workflow stages.</summary>
    public static string Reopen => Text("MotifApp.Reopen");

    public static string Delete => Text("MotifApp.Delete");

    public static string DeleteProposalQuestionFormat => Text("MotifApp.DeleteProposalQuestionFormat");

    public static string DeleteProposalWarning => Text("MotifApp.DeleteProposalWarning");

    public static string DeleteProposal => Text("MotifApp.DeleteProposal");

    public static string KeepProposal => Text("MotifApp.KeepProposal");

    public static string Proposals => Text("MotifApp.Proposals");

    public static string Proposal => Text("MotifApp.Proposal");

    public static string ApprovedKept => Text("MotifApp.ApprovedKept");

    public static string Coverage => Text("MotifApp.Coverage");

    public static string Warnings => Text("MotifApp.Warnings");

    public static string ProposalWork => Text("MotifApp.ProposalWork");

    public static string ProposalsAccessibleName => Text("MotifApp.ProposalsAccessibleName");

    public static string Open => Text("MotifApp.Open");


    public static string Reading => Text("MotifApp.Reading");

    public static string OpenWarnings => Text("MotifApp.OpenWarnings");

    public static string InFieldWorks => Text("MotifApp.InFieldWorks");

    public static string CollapseSideBarAccessibleName => Text("MotifApp.CollapseSideBarAccessibleName");

    public static string CollapseSideBarTip => Text("MotifApp.CollapseSideBarTip");

    public static string ShowSideBarAccessibleName => Text("MotifApp.ShowSideBarAccessibleName");

    public static string DetailsAccessibleName => Text("MotifApp.DetailsAccessibleName");

    public static string HideInspectorAccessibleName => Text("MotifApp.HideInspectorAccessibleName");

    public static string BackToPage => Text("MotifApp.BackToPage");
    public static string FilterAll => Text("MotifApp.FilterAll");
    public static string FilterUnread => Text("MotifApp.FilterUnread");
    public static string FilterDifferent => Text("MotifApp.FilterDifferent");
    public static string FilterNotInFieldWorks => Text("MotifApp.FilterNotInFieldWorks");
    public static string FilterNoParse => Text("MotifApp.FilterNoParse");
    public static string FilterStopped => Text("MotifApp.FilterStopped");
    public static string FilterNotParsed => Text("MotifApp.FilterNotParsed");
    public static string FilterNamedInWarning => Text("MotifApp.FilterNamedInWarning");
    public static string FilterApproved => Text("MotifApp.FilterApproved");
    public static string FilterUnknown => Text("MotifApp.FilterUnknown");
    public static string FilterDisapproved => Text("MotifApp.FilterDisapproved");
    public static string FilterIncorrectSpelling => Text("MotifApp.FilterIncorrectSpelling");
    public static string FilterSeveralAnalyses => Text("MotifApp.FilterSeveralAnalyses");
    public static string SearchWordsPlaceholder => Text("MotifApp.SearchWordsPlaceholder");
    public static string SearchTextsPlaceholder => Text("MotifApp.SearchTextsPlaceholder");


    public static string HideInspectorTip => Text("MotifApp.HideInspectorTip");

    public static string InspectorHeading => Text("MotifApp.InspectorHeading");

    public static string ShowInspectorAccessibleName => Text("MotifApp.ShowInspectorAccessibleName");

    public static string ShowInspectorTip => Text("MotifApp.ShowInspectorTip");

    public static string ChangeCountPattern => Text("MotifApp.ChangeCountPattern");

    public static string WordCountPattern => Text("MotifApp.WordCountPattern");

    public static string ProposalCountPattern => Text("MotifApp.ProposalCountPattern");
    public static string UnreadableProposalsPattern => Text("MotifApp.UnreadableProposalsPattern");
    public static string UnreadableProposalsNamesFormat => Text("MotifApp.UnreadableProposalsNamesFormat");
    public static string ProposalDifferenceTitle => Text("MotifApp.ProposalDifferenceTitle");
    public static string ProposalDifferenceAdvice => Text("MotifApp.ProposalDifferenceAdvice");
    public static string ProposalDifferenceReading => Text("MotifApp.ProposalDifferenceReading");
    public static string ProposalDifferenceUnreadable => Text("MotifApp.ProposalDifferenceUnreadable");
    public static string ProposalDifferenceWordCountPattern => Text("MotifApp.ProposalDifferenceWordCountPattern");
    public static string ProposalDifferenceOccurrenceCountPattern => Text("MotifApp.ProposalDifferenceOccurrenceCountPattern");
    public static string ProposalDifferenceHeadlineFormat => Text("MotifApp.ProposalDifferenceHeadlineFormat");
    public static string ProposalDifferenceComparedFormat => Text("MotifApp.ProposalDifferenceComparedFormat");
    public static string ProposalDifferenceUnboundFormat => Text("MotifApp.ProposalDifferenceUnboundFormat");
    public static string ProposalDifferenceOutsideFormat => Text("MotifApp.ProposalDifferenceOutsideFormat");
    public static string ProposalDifferenceDetailRemoved => Text("MotifApp.ProposalDifferenceDetailRemoved");
    public static string ProposalDifferenceRemakeHint => Text("MotifApp.ProposalDifferenceRemakeHint");
    public static string ProposalDifferenceBasisFormat => Text("MotifApp.ProposalDifferenceBasisFormat");
    public static string ProposalDifferenceBasisHuman => Text("MotifApp.ProposalDifferenceBasisHuman");
    public static string ProposalDifferenceBasisSourceValidated => Text("MotifApp.ProposalDifferenceBasisSourceValidated");
    public static string ProposalDifferenceSavedTitle => Text("MotifApp.ProposalDifferenceSavedTitle");
    public static string ProposalDifferenceSavedAccessibleName => Text("MotifApp.ProposalDifferenceSavedAccessibleName");
    public static string ProposalDifferenceSavedItemFormat => Text("MotifApp.ProposalDifferenceSavedItemFormat");
    public static string ProposalDifferenceSavedTextFormat => Text("MotifApp.ProposalDifferenceSavedTextFormat");
    public static string ProposalDifferenceChipListedFormat => Text("MotifApp.ProposalDifferenceChipListedFormat");
    public static string ProposalDifferenceListedNoteFormat => Text("MotifApp.ProposalDifferenceListedNoteFormat");
    public static string ProposalDifferenceNotListedPattern => Text("MotifApp.ProposalDifferenceNotListedPattern");
    public static string ProposalDifferenceMoreNotListedPattern => Text("MotifApp.ProposalDifferenceMoreNotListedPattern");
    public static string ProposalDifferenceNoneListedPattern => Text("MotifApp.ProposalDifferenceNoneListedPattern");
    public static string ProposalDifferenceNoListedMatching => Text("MotifApp.ProposalDifferenceNoListedMatching");
    public static string ProposalDifferenceDetailUnreadable => Text("MotifApp.ProposalDifferenceDetailUnreadable");
    public static string ProposalDifferenceSavedUnreadablePattern => Text("MotifApp.ProposalDifferenceSavedUnreadablePattern");
    public static string ProposalDifferenceSavedDamagedFormat => Text("MotifApp.ProposalDifferenceSavedDamagedFormat");
    public static string ProposalDifferenceSavedDamagedRemake => Text("MotifApp.ProposalDifferenceSavedDamagedRemake");
    public static string ProposalDifferenceSavedHuman => Text("MotifApp.ProposalDifferenceSavedHuman");
    public static string ProposalDifferenceSavedSourceValidated => Text("MotifApp.ProposalDifferenceSavedSourceValidated");
    public static string ProposalDifferenceSavedMorePattern => Text("MotifApp.ProposalDifferenceSavedMorePattern");
    public static string ProposalDifferenceMovedTitle => Text("MotifApp.ProposalDifferenceMovedTitle");
    public static string ProposalDifferenceCheckTitle => Text("MotifApp.ProposalDifferenceCheckTitle");
    public static string ProposalDifferenceImprovedTitle => Text("MotifApp.ProposalDifferenceImprovedTitle");
    public static string ProposalDifferenceLimitsTitle => Text("MotifApp.ProposalDifferenceLimitsTitle");
    public static string ProposalDifferenceNotesTitle => Text("MotifApp.ProposalDifferenceNotesTitle");
    public static string ProposalDifferenceCheckChip => Text("MotifApp.ProposalDifferenceCheckChip");
    public static string ProposalDifferenceClear => Text("MotifApp.ProposalDifferenceClear");
    public static string ProposalDifferenceWordsTitle => Text("MotifApp.ProposalDifferenceWordsTitle");
    public static string ProposalDifferenceShowingFormat => Text("MotifApp.ProposalDifferenceShowingFormat");
    public static string ProposalDifferenceNoMatching => Text("MotifApp.ProposalDifferenceNoMatching");
    public static string ProposalDifferenceNarrowHint => Text("MotifApp.ProposalDifferenceNarrowHint");
    public static string ProposalDifferenceMoreWords => Text("MotifApp.ProposalDifferenceMoreWords");
    public static string ProposalDifferenceMorePlaces => Text("MotifApp.ProposalDifferenceMorePlaces");
    public static string ProposalDifferencePlacesTitleFormat => Text("MotifApp.ProposalDifferencePlacesTitleFormat");
    public static string ProposalDifferenceStatesFormat => Text("MotifApp.ProposalDifferenceStatesFormat");
    public static string ProposalDifferencePlaceFormat => Text("MotifApp.ProposalDifferencePlaceFormat");
    public static string ProposalDifferencePlacesNotListedFormat => Text("MotifApp.ProposalDifferencePlacesNotListedFormat");
    public static string ProposalDifferenceNoPlaces => Text("MotifApp.ProposalDifferenceNoPlaces");
    public static string ProposalDifferenceOpenWord => Text("MotifApp.ProposalDifferenceOpenWord");
    public static string FilterChipAccessibleNameFormat => Text("MotifApp.FilterChipAccessibleNameFormat");
    public static string ProposalDifferenceNoteParser => Text("MotifApp.ProposalDifferenceNoteParser");
    public static string ProposalDifferenceNoteTokeniser => Text("MotifApp.ProposalDifferenceNoteTokeniser");
    public static string ProposalDifferenceNoteRetry => Text("MotifApp.ProposalDifferenceNoteRetry");
    public static string ProposalDifferenceNoteSourceValidated => Text("MotifApp.ProposalDifferenceNoteSourceValidated");
    public static string ProposalDifferenceAdviceSourceValidated => Text("MotifApp.ProposalDifferenceAdviceSourceValidated");
    public static string ProposalDifferenceEvidenceUnreadableSaved => Text("MotifApp.ProposalDifferenceEvidenceUnreadableSaved");
    public static string ProposalDifferenceEvidenceUnreadableFormat => Text("MotifApp.ProposalDifferenceEvidenceUnreadableFormat");
    public static string ProposalDifferenceEvidenceFormat => Text("MotifApp.ProposalDifferenceEvidenceFormat");
    public static string ProposalDifferenceEvidenceWordFormat => Text("MotifApp.ProposalDifferenceEvidenceWordFormat");
    public static string ProposalDifferenceEvidenceAnalysisFormat => Text("MotifApp.ProposalDifferenceEvidenceAnalysisFormat");
    public static string ProposalDifferenceEvidenceAgentFormat => Text("MotifApp.ProposalDifferenceEvidenceAgentFormat");
    public static string DifferenceEvidenceTierSourceApproved => Text("MotifApp.DifferenceEvidenceTierSourceApproved");
    public static string DifferenceEvidenceTierValidAttested => Text("MotifApp.DifferenceEvidenceTierValidAttested");
    public static string DifferenceEvidenceTierValidProductive => Text("MotifApp.DifferenceEvidenceTierValidProductive");
    public static string DifferenceEvidenceTierTypo => Text("MotifApp.DifferenceEvidenceTierTypo");
    public static string DifferenceEvidenceTierIllFormed => Text("MotifApp.DifferenceEvidenceTierIllFormed");
    public static string DifferenceEvidenceTierNonstandardVariant => Text("MotifApp.DifferenceEvidenceTierNonstandardVariant");
    public static string DifferenceEvidenceTierJoinSplit => Text("MotifApp.DifferenceEvidenceTierJoinSplit");
    public static string DifferenceEvidenceTierNameForeignNumber => Text("MotifApp.DifferenceEvidenceTierNameForeignNumber");
    public static string DifferenceEvidenceTierUndecided => Text("MotifApp.DifferenceEvidenceTierUndecided");
    public static string ProposalDifferenceNoteOther => Text("MotifApp.ProposalDifferenceNoteOther");
    public static string ProposalDifferencePanelAccessibleName => Text("MotifApp.ProposalDifferencePanelAccessibleName");
    public static string ProposalDifferenceMakeAdvice => Text("MotifApp.ProposalDifferenceMakeAdvice");
    public static string ProposalDifferenceTextLabel => Text("MotifApp.ProposalDifferenceTextLabel");
    public static string ProposalDifferenceMake => Text("MotifApp.ProposalDifferenceMake");
    public static string ProposalDifferenceMakeAgain => Text("MotifApp.ProposalDifferenceMakeAgain");
    public static string ProposalDifferenceMaking => Text("MotifApp.ProposalDifferenceMaking");
    public static string ProposalDifferenceMakingReadingText => Text("MotifApp.ProposalDifferenceMakingReadingText");
    public static string ProposalDifferenceMakingFormat => Text("MotifApp.ProposalDifferenceMakingFormat");
    public static string ProposalDifferenceCancelling => Text("MotifApp.ProposalDifferenceCancelling");
    public static string ProposalDifferenceNoTexts => Text("MotifApp.ProposalDifferenceNoTexts");
    public static string ProposalDifferenceNotTried => Text("MotifApp.ProposalDifferenceNotTried");
    public static string ProposalDifferenceDecidedNone => Text("MotifApp.ProposalDifferenceDecidedNone");
    public static string ProposalDifferenceMakeFailed => Text("MotifApp.ProposalDifferenceMakeFailed");
    public static string ProposalDifferenceProgressAccessibleName => Text("MotifApp.ProposalDifferenceProgressAccessibleName");
    public static string ProposalDifferenceTextAccessibleName => Text("MotifApp.ProposalDifferenceTextAccessibleName");
    public static string DifferenceStateNoParse => Text("MotifApp.DifferenceStateNoParse");
    public static string DifferenceStateNoApprovedMatch => Text("MotifApp.DifferenceStateNoApprovedMatch");
    public static string DifferenceStateApprovedWithAlternatives => Text("MotifApp.DifferenceStateApprovedWithAlternatives");
    public static string DifferenceStateApprovedUnique => Text("MotifApp.DifferenceStateApprovedUnique");
    public static string DifferenceStateNoApprovedExpectations => Text("MotifApp.DifferenceStateNoApprovedExpectations");
    public static string DifferenceStateIncomplete => Text("MotifApp.DifferenceStateIncomplete");
    public static string DifferenceStateUnavailable => Text("MotifApp.DifferenceStateUnavailable");
    public static string DifferenceStateNotMeasured => Text("MotifApp.DifferenceStateNotMeasured");
    public static string DifferenceStateConflictingDecisions => Text("MotifApp.DifferenceStateConflictingDecisions");
    public static string DifferenceBucketRegression => Text("MotifApp.DifferenceBucketRegression");
    public static string DifferenceBucketNewCompetingParse => Text("MotifApp.DifferenceBucketNewCompetingParse");
    public static string DifferenceBucketOvergeneration => Text("MotifApp.DifferenceBucketOvergeneration");
    public static string DifferenceBucketNewWrongReading => Text("MotifApp.DifferenceBucketNewWrongReading");
    public static string DifferenceBucketAnalysisChanged => Text("MotifApp.DifferenceBucketAnalysisChanged");
    public static string DifferenceBucketAmbiguityBlowup => Text("MotifApp.DifferenceBucketAmbiguityBlowup");
    public static string DifferenceBucketLostParse => Text("MotifApp.DifferenceBucketLostParse");
    public static string DifferenceBucketImprovedApprovedMatch => Text("MotifApp.DifferenceBucketImprovedApprovedMatch");
    public static string DifferenceBucketImprovedUnique => Text("MotifApp.DifferenceBucketImprovedUnique");
    public static string DifferenceBucketImprovedNegativeRejection => Text("MotifApp.DifferenceBucketImprovedNegativeRejection");
    public static string DifferenceLimitationCapped => Text("MotifApp.DifferenceLimitationCapped");
    public static string DifferenceLimitationTimedOut => Text("MotifApp.DifferenceLimitationTimedOut");
    public static string DifferenceLimitationProjectionUnavailable => Text("MotifApp.DifferenceLimitationProjectionUnavailable");
    public static string DifferenceLimitationParserRefused => Text("MotifApp.DifferenceLimitationParserRefused");
    public static string DifferenceLimitationInvalidShape => Text("MotifApp.DifferenceLimitationInvalidShape");
    public static string DifferenceLimitationMalformedEvidence => Text("MotifApp.DifferenceLimitationMalformedEvidence");
    public static string DifferenceLimitationInvalidExpectedMorphology => Text("MotifApp.DifferenceLimitationInvalidExpectedMorphology");

    public static string ProblemFindingFormat => Text("MotifApp.ProblemFindingFormat");

    public static string WordGroupCountPattern => Text("MotifApp.WordGroupCountPattern");

    public static string ChangeGroupCountPattern => Text("MotifApp.ChangeGroupCountPattern");

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

    public static string ParseChosenForLimitsPlaceholder => Text("MotifApp.ParseChosenForLimitsPlaceholder");
    public static string ChooseWhatToParseForLimitsPlaceholder => Text("MotifApp.ChooseWhatToParseForLimitsPlaceholder");

    public static string ParserWorkLimitPerWordLabel => Text("MotifApp.ParserWorkLimitPerWordLabel");

    public static string ParserWorkLimitDefaultCaption => Text("MotifApp.ParserWorkLimitDefaultCaption");

    public static string ResetParserWorkLimit => Text("MotifApp.ResetParserWorkLimit");

    public static string ResetParserWorkLimitTo20000000AccessibleName => Text("MotifApp.ResetParserWorkLimitTo20000000AccessibleName");

    public static string NoParserWorkLimitAccessibleName => Text("MotifApp.NoParserWorkLimitAccessibleName");

    public static string TimeLimitPerWord => Text("MotifApp.TimeLimitPerWord");

    public static string TimeLimitFollowsParserWorkLimitCaption => Text("MotifApp.TimeLimitFollowsParserWorkLimitCaption");

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

    public static string SetupParserWorkLimitPerWord => Text("MotifApp.SetupParserWorkLimitPerWord");

    public static string SetupParserWorkLimitDefaultCaption => Text("MotifApp.SetupParserWorkLimitDefaultCaption");

    public static string SetupNoParserWorkLimitOption => Text("MotifApp.SetupNoParserWorkLimitOption");

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

    public static string TimeLimitPositiveRefusal => Text("MotifApp.TimeLimitPositiveRefusal");

    public static string ParsingFooterRunning => Text("MotifApp.ParsingFooterRunning");

    public static string ParsingFooterIdle => Text("MotifApp.ParsingFooterIdle");

    public static string StoredAnalysesSaveRefusalFormat => Text("MotifApp.StoredAnalysesSaveRefusalFormat");

    public static string ParserWorkLimitWholeNumberRefusal => Text("MotifApp.ParserWorkLimitWholeNumberRefusal");

    public static string TimeLimitRangeRefusal => Text("MotifApp.TimeLimitRangeRefusal");

    public static string Copied => Text("MotifApp.Copied");

    public static string CopyVersions => Text("MotifApp.CopyVersions");

    public static string EnterPositiveWholeNumberOfParserWork => Text("MotifApp.EnterPositiveWholeNumberOfParserWork");

    public static string RunningNoParserWorkLimit => Text("MotifApp.RunningNoParserWorkLimit");

    public static string RunningParserWorkLimitFormat => Text("MotifApp.RunningParserWorkLimitFormat");

    public static string SetupNextLimits => Text("MotifApp.SetupNextLimits");

    public static string SetupNextFirstRun => Text("MotifApp.SetupNextFirstRun");
    public static string SetupNextUseSelection => Text("MotifApp.SetupNextUseSelection");
    public static string WordCardNoParseWarning => Text("MotifApp.WordCardNoParseWarning");
    public static string ReaderMarkLegend => Text("MotifApp.ReaderMarkLegend");
    public static string ProposalsWaitingBadgePattern => Text("MotifApp.ProposalsWaitingBadgePattern");

    public static string SetupNextTexts => Text("MotifApp.SetupNextTexts");

    public static string SetupStepCaptionFormat => Text("MotifApp.SetupStepCaptionFormat");

    public static string SetupNoParserWorkLimitCaption => Text("MotifApp.SetupNoParserWorkLimitCaption");

    public static string SetupParserWorkLimitFormat => Text("MotifApp.SetupParserWorkLimitFormat");

    public static string ParserWorkLimitPattern => Text("MotifApp.ParserWorkLimitPattern");

    public static string NoAddedWords => Text("MotifApp.NoAddedWords");

    public static string UseThisSelection => Text("MotifApp.UseThisSelection");

    public static string StartFirstRun => Text("MotifApp.StartFirstRun");

    public static string SetupUseSelectionTitle => Text("MotifApp.SetupUseSelectionTitle");

    public static string SetupFirstRunTitle => Text("MotifApp.SetupFirstRunTitle");

    public static string SetupUseSelectionDescription => Text("MotifApp.SetupUseSelectionDescription");

    public static string SetupFirstRunDescription => Text("MotifApp.SetupFirstRunDescription");

    public static string SetupChooseTextOrWord => Text("MotifApp.SetupChooseTextOrWord");

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

    public static string SetupParserWorkLimitPerWordAccessibleName => Text("MotifApp.SetupParserWorkLimitPerWordAccessibleName");

    public static string SetupCancel => Text("MotifApp.SetupCancel");

    public static string AiHandoffHeading => Text("MotifApp.AiHandoffHeading");

    public static string WriteTheAIHandoffFolderAccessibleName => Text("MotifApp.WriteTheAIHandoffFolderAccessibleName");

    public static string Judgment => Text("MotifApp.Judgment");

    public static string Defer => Text("MotifApp.Defer");


    public static string Ask => Text("MotifApp.Ask");

    public static string ObjectTimingDetailIncomplete => Text("MotifApp.ObjectTimingDetailIncomplete");

    public static string AssessBudgetStoppedFormat => Text("MotifApp.AssessBudgetStoppedFormat");
    public static string AssessBudgetLabel => Text("MotifApp.AssessBudgetLabel");
    public static string AssessBudgetCaption => Text("MotifApp.AssessBudgetCaption");
    public static string AssessBudgetRefusal => Text("MotifApp.AssessBudgetRefusal");
    public static string ParserWorkLimitField => Text("MotifApp.ParserWorkLimitField");

    public static string TimingTimeColumnHeading => Text("MotifApp.TimingTimeColumnHeading");

    public static string Inspect => Text("MotifApp.Inspect");

    public static string Slowest => Text("MotifApp.Slowest");

    public static string More => Text("MotifApp.More");

    public static string FixTheseFirst => Text("MotifApp.FixTheseFirst");

    public static string WhatTheseWordsShare => Text("MotifApp.WhatTheseWordsShare");

    public static string AddAsUnknown => Text("MotifApp.AddAsUnknown");

    public static string IncorrectSpelling => Text("MotifApp.IncorrectSpelling");

    public static string CompareParseAgainTip => Text("MotifApp.CompareParseAgainTip");

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

    public static string Severity => Text("MotifApp.Severity");

    public static string ShowFindingsWithExactUsesInYourWordsAccessibleName => Text("MotifApp.ShowFindingsWithExactUsesInYourWordsAccessibleName");

    public static string NamedWords => Text("MotifApp.NamedWords");

    public static string WhatIsWrong => Text("MotifApp.WhatIsWrong");

    public static string WhyPanGlossReportedIt => Text("MotifApp.WhyPanGlossReportedIt");

    public static string WordformsNotCompared => Text("MotifApp.WordformsNotCompared");

    public static string WhatToDoInFieldWorks => Text("MotifApp.WhatToDoInFieldWorks");

    public static string WarningNoAdviceYet => Text("MotifApp.WarningNoAdviceYet");

    public static string WarningWordsUsingNamedResource => Text("MotifApp.WarningWordsUsingNamedResource");

    public static string AfterYouFixIt => Text("MotifApp.AfterYouFixIt");

    public static string WarningAfterFixSteps => Text("MotifApp.WarningAfterFixSteps");

    public static string LearnMore => Text("MotifApp.LearnMore");

    public static string AIHandoffForThisWarning => Text("MotifApp.AIHandoffForThisWarning");

    public static string ParseTheseWordsAgain => Text("MotifApp.ParseTheseWordsAgain");

    public static string SelectedWarningDetailsAccessibleName => Text("MotifApp.SelectedWarningDetailsAccessibleName");

    public static string Tracing => Text("MotifApp.Tracing");

    public static string NoStepRecorded => Text("MotifApp.NoStepRecorded");

    public static string TimeLabel => Text("MotifApp.TimeLabel");

    public static string TryIt => Text("MotifApp.TryIt");

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


    public static string DispositionIncorrectSpellingTip => Text("MotifApp.DispositionIncorrectSpellingTip");

    public static string WhatFieldWorksHoldsForThisWordAccessibleName => Text("MotifApp.WhatFieldWorksHoldsForThisWordAccessibleName");

    public static string MarkThisSpellingIncorrectAccessibleName => Text("MotifApp.MarkThisSpellingIncorrectAccessibleName");

    public static string KeepFieldWorksAccessibleName => Text("MotifApp.KeepFieldWorksAccessibleName");

    public static string PanGlossAnalysesToAddToFieldWorksAccessibleName => Text("MotifApp.PanGlossAnalysesToAddToFieldWorksAccessibleName");


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

    public static string WorkByRule => Text("MotifApp.WorkByRule");

    public static string WordCardOtherRules => Text("MotifApp.WordCardOtherRules");

    public static string WordCardParserWorkNotRecorded => Text("MotifApp.WordCardParserWorkNotRecorded");

    public static string WordCardWorkByRuleNotRecorded => Text("MotifApp.WordCardWorkByRuleNotRecorded");

    public static string WhatToDo => Text("MotifApp.WhatToDo");

    public static string WordCardFieldWorksHoldsNothing => Text("MotifApp.WordCardFieldWorksHoldsNothing");

    public static string WordCardNotBuiltLabel => Text("MotifApp.WordCardNotBuiltLabel");

    public static string WordCardApprovedNotBuilt => Text("MotifApp.WordCardApprovedNotBuilt");

    public static string PanGlossHelp => Text("MotifApp.PanGlossHelp");

    public static string MarkAsUnread => Text("MotifApp.MarkAsUnread");

    public static string MarkAsUnreadTip => Text("MotifApp.MarkAsUnreadTip");

    public static string TechDemoVersionFormat => Text("MotifApp.TechDemoVersionFormat");
    public static string ShortcutOpenProject => Text("MotifApp.ShortcutOpenProject");
    public static string TraceCategoryStatisticsNote => Text("MotifApp.TraceCategoryStatisticsNote");
    public static string ShowHandoffByCopyingFiles => Text("MotifApp.ShowHandoffByCopyingFiles");

    public static string WordCardChangeSentenceAccessibleName => Text("MotifApp.WordCardChangeSentenceAccessibleName");

    public static string SentenceContextAccessibleName => Text("MotifApp.SentenceContextAccessibleName");

    public static string AboutPanGlossAccessibleName => Text("MotifApp.AboutPanGlossAccessibleName");

    public static string MarkThisOccurrenceAsUnreadAccessibleName => Text("MotifApp.MarkThisOccurrenceAsUnreadAccessibleName");

    public static string CheckThisUncertainChangeAgainAccessibleName => Text("MotifApp.CheckThisUncertainChangeAgainAccessibleName");

    public static string StripTokenNothingInFieldWorks => Text("MotifApp.StripTokenNothingInFieldWorks");

    public static string Staged => Text("MotifApp.Staged");

    public static string TimingAdviceTimeLimit => Text("MotifApp.TimingAdviceTimeLimit");

    public static string TimingPhraseSomeOfAllFormat => Text("MotifApp.TimingPhraseSomeOfAllFormat");

    public static string TimingPhraseSomeOfTheseFormat => Text("MotifApp.TimingPhraseSomeOfTheseFormat");

    public static string TimingPhraseThisWord => Text("MotifApp.TimingPhraseThisWord");

    public static string TimingPhraseAllWordsFormat => Text("MotifApp.TimingPhraseAllWordsFormat");

    public static string TimingPhraseTheseWordsFormat => Text("MotifApp.TimingPhraseTheseWordsFormat");

    public static string TimingNotAttributedLabel => Text("MotifApp.TimingNotAttributedLabel");

    public static string TimingShareOfFormat => Text("MotifApp.TimingShareOfFormat");

    public static string TimingShareOfOneMissingFormat => Text("MotifApp.TimingShareOfOneMissingFormat");

    public static string TimingShareOfManyMissingFormat => Text("MotifApp.TimingShareOfManyMissingFormat");

    public static string TimingNotRecorded => Text("MotifApp.TimingNotRecorded");

    public static string TimingCallsPartialFormat => Text("MotifApp.TimingCallsPartialFormat");

    public static string TimingCallsPattern => Text("MotifApp.TimingCallsPattern");

    public static string TimingNotCounted => Text("MotifApp.TimingNotCounted");

    public static string TimingKindMorphologicalRules => Text("MotifApp.TimingKindMorphologicalRules");

    public static string TimingKindPhonologicalRules => Text("MotifApp.TimingKindPhonologicalRules");

    public static string TimingKindRootLookup => Text("MotifApp.TimingKindRootLookup");

    public static string TimingKindLexicalEntries => Text("MotifApp.TimingKindLexicalEntries");

    public static string TimingKindParser => Text("MotifApp.TimingKindParser");

    public static string TimingGrammarWarningTip => Text("MotifApp.TimingGrammarWarningTip");

    public static string TimingUnknownQuality => Text("MotifApp.TimingUnknownQuality");

    public static string ParsimonyDescribeFormat => Text("MotifApp.ParsimonyDescribeFormat");

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

    public static string StatsAdviceParserLimit => Text("MotifApp.StatsAdviceParserLimit");
    public static string StatsAdviceParserAndTimeLimits => Text("MotifApp.StatsAdviceParserAndTimeLimits");
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

    public static string StatsHeaderParserWork => Text("MotifApp.StatsHeaderParserWork");

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

    public static string WordListCountPattern => Text("MotifApp.WordListCountPattern");



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



    public static string TryWordOpinionCountFormat => Text("MotifApp.TryWordOpinionCountFormat");

    public static string TryWordPieceBuiltRefused => Text("MotifApp.TryWordPieceBuiltRefused");

    public static string TraceStepBuilt => Text("MotifApp.TraceStepBuilt");

    public static string TraceStepRefused => Text("MotifApp.TraceStepRefused");

    public static string TryWordPieceTried => Text("MotifApp.TryWordPieceTried");

    public static string TryWordPieceDiffer => Text("MotifApp.TryWordPieceDiffer");

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

    public static string ProgressStoppedMemoryLimitFormat => Text("MotifApp.ProgressStoppedMemoryLimitFormat");
    public static string ProgressStoppedAtMemoryLimit => Text("MotifApp.ProgressStoppedAtMemoryLimit");
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

    public static string TrialDroppedGrammarNotCheckedSentence => Text("MotifApp.TrialDroppedGrammarNotCheckedSentence");

    public static string TrialWorkingSetOnlySentence => Text("MotifApp.TrialWorkingSetOnlySentence");

    public static string RefusalParserMissing => Text("MotifApp.RefusalParserMissing");

    public static string RefusalParserMissingDetailsFormat => Text("MotifApp.RefusalParserMissingDetailsFormat");

    public static string RefusalParserUnusable => Text("MotifApp.RefusalParserUnusable");

    public static string RefusalParserWrong => Text("MotifApp.RefusalParserWrong");

    public static string RefusalStoreFromOtherVersion => Text("MotifApp.RefusalStoreFromOtherVersion");

    public static string RefusalStoreUpgradable => Text("MotifApp.RefusalStoreUpgradable");

    public static string RefusalStoreDamaged => Text("MotifApp.RefusalStoreDamaged");

    public static string RefusalTextFieldStale => Text("MotifApp.RefusalTextFieldStale");
    public static string RefusalTextStyleUnknown => Text("MotifApp.RefusalTextStyleUnknown");

    public static string RefusalGeneric => Text("MotifApp.RefusalGeneric");

    public static string RefusalApplyAppliedContentMismatch => Text("MotifApp.RefusalApplyAppliedContentMismatch");

    public static string RefusalApplyChangeNoLongerFits => Text("MotifApp.RefusalApplyChangeNoLongerFits");

    public static string RefusalApplyChangeUncertain => Text("MotifApp.RefusalApplyChangeUncertain");

    public static string RefusalApplyChangesChanged => Text("MotifApp.RefusalApplyChangesChanged");

    public static string RefusalApplyDrift => Text("MotifApp.RefusalApplyDrift");

    public static string RefusalApplyDroppedGrammar => Text("MotifApp.RefusalApplyDroppedGrammar");

    public static string RefusalApplyDryRunMissing => Text("MotifApp.RefusalApplyDryRunMissing");

    public static string RefusalProposalClosed => Text("MotifApp.RefusalProposalClosed");

    public static string RefusalProposalNotFound => Text("MotifApp.RefusalProposalNotFound");

    public static string CommentPlainText => Text("MotifApp.CommentPlainText");
    public static string CommentDelete => Text("MotifApp.CommentDelete");
    public static string CommentAdd => Text("MotifApp.CommentAdd");
    public static string CommentsHeading => Text("MotifApp.CommentsHeading");
    public static string CommentSaveOrCancelFirst => Text("MotifApp.CommentSaveOrCancelFirst");

    public static string RefusalProposalCommentInvalid => Text("MotifApp.RefusalProposalCommentInvalid");
    public static string RefusalProposalPictureInvalid => Text("MotifApp.RefusalProposalPictureInvalid");
    public static string RefusalProposalRationaleEmpty => Text("MotifApp.RefusalProposalRationaleEmpty");
    public static string RefusalProposalRationaleFindings => Text("MotifApp.RefusalProposalRationaleFindings");
    public static string RefusalProposalRationaleStale => Text("MotifApp.RefusalProposalRationaleStale");
    public static string RefusalProposalRationaleUnchecked => Text("MotifApp.RefusalProposalRationaleUnchecked");

    public static string RefusalProposalUnreadable => Text("MotifApp.RefusalProposalUnreadable");

    public static string RefusalApplyPreconditionFailed => Text("MotifApp.RefusalApplyPreconditionFailed");

    /// <summary>Asks the person to resolve pending changes before applying another Proposal.</summary>
    public static string RefusalApplyPendingChanges => Text("MotifApp.RefusalApplyPendingChanges");

    public static string RefusalApplyNotReady => Text("MotifApp.RefusalApplyNotReady");

    public static string RefusalApplyProjectInUse => Text("MotifApp.RefusalApplyProjectInUse");

    public static string RefusalApplyReconciliationNeeded => Text("MotifApp.RefusalApplyReconciliationNeeded");

    public static string RefusalApplyNotSaved => Text("MotifApp.RefusalApplyNotSaved");

    public static string RefusalOperationCascadingDelete => Text("MotifApp.RefusalOperationCascadingDelete");

    public static string RefusalOperationInvalidDependency => Text("MotifApp.RefusalOperationInvalidDependency");

    public static string RefusalApplyRegression => Text("MotifApp.RefusalApplyRegression");

    public static string RefusalApplyReopenFailed => Text("MotifApp.RefusalApplyReopenFailed");

    public static string RefusalParseAlreadyRunningHere => Text("MotifApp.RefusalParseAlreadyRunningHere");

    public static string RefusalParseAlreadyRunningElsewhere => Text("MotifApp.RefusalParseAlreadyRunningElsewhere");

    public static string RefusalAssessUnavailable => Text("MotifApp.RefusalAssessUnavailable");

    public static string RefusalTrialReviewBaselineChanged => Text("MotifApp.RefusalTrialReviewBaselineChanged");

    public static string RefusalTrialReviewInputUnavailable => Text("MotifApp.RefusalTrialReviewInputUnavailable");

    public static string RefusalTrialTraceUnavailable => Text("MotifApp.RefusalTrialTraceUnavailable");

    public static string RefusalAssessBaselineChanged => Text("MotifApp.RefusalAssessBaselineChanged");

    public static string RefusalAssessInvalidLimit => Text("MotifApp.RefusalAssessInvalidLimit");

    public static string RefusalAssessInvocationInconsistent => Text("MotifApp.RefusalAssessInvocationInconsistent");

    public static string RefusalAssessMeasurementsIncomplete => Text("MotifApp.RefusalAssessMeasurementsIncomplete");

    public static string RefusalAssessUnsupportedKind => Text("MotifApp.RefusalAssessUnsupportedKind");

    public static string RefusalAssessWordsAmbiguous => Text("MotifApp.RefusalAssessWordsAmbiguous");

    public static string RefusalAssessWordsFileEmpty => Text("MotifApp.RefusalAssessWordsFileEmpty");

    public static string RefusalAssessWordsMultiWord => Text("MotifApp.RefusalAssessWordsMultiWord");

    public static string RefusalAssessWordsFileUnreadable => Text("MotifApp.RefusalAssessWordsFileUnreadable");

    public static string RefusalAssessmentCancelled => Text("MotifApp.RefusalAssessmentCancelled");
    public static string RefusalAssessmentExpired => Text("MotifApp.RefusalAssessmentExpired");

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

    public static string RefusalDifferenceCancelled => Text("MotifApp.RefusalDifferenceCancelled");

    public static string RefusalDifferenceNoTrial => Text("MotifApp.RefusalDifferenceNoTrial");

    public static string RefusalDifferenceTrialIncomplete => Text("MotifApp.RefusalDifferenceTrialIncomplete");

    public static string RefusalDifferenceNoParseAssessment => Text("MotifApp.RefusalDifferenceNoParseAssessment");

    public static string RefusalDifferenceNoBefore => Text("MotifApp.RefusalDifferenceNoBefore");

    public static string RefusalDifferenceNoBaseline => Text("MotifApp.RefusalDifferenceNoBaseline");

    public static string RefusalDifferenceBaselineChanged => Text("MotifApp.RefusalDifferenceBaselineChanged");

    public static string RefusalDifferenceTextNotFound => Text("MotifApp.RefusalDifferenceTextNotFound");

    public static string RefusalDifferenceProposalDecided => Text("MotifApp.RefusalDifferenceProposalDecided");

    public static string RefusalDifferenceDuplicateParseRows => Text("MotifApp.RefusalDifferenceDuplicateParseRows");

    public static string RefusalDifferenceUnreadable => Text("MotifApp.RefusalDifferenceUnreadable");

    public static string RefusalDifferenceUnexpectedOpinion => Text("MotifApp.RefusalDifferenceUnexpectedOpinion");

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

    public static string RefusalProjectStartupBusy => Text("MotifApp.RefusalProjectStartupBusy");

    public static string RefusalProjectStoreIo => Text("MotifApp.RefusalProjectStoreIo");

    public static string RefusalProjectUnloadable => Text("MotifApp.RefusalProjectUnloadable");

    public static string RefusalProjectWaitCancelled => Text("MotifApp.RefusalProjectWaitCancelled");

    public static string RefusalReviewAssessmentNotFound => Text("MotifApp.RefusalReviewAssessmentNotFound");

    public static string RefusalReviewNumbersUnavailable => Text("MotifApp.RefusalReviewNumbersUnavailable");

    public static string RefusalReviewWrongAssessmentKind => Text("MotifApp.RefusalReviewWrongAssessmentKind");

    public static string RefusalRuleAttemptsInvalid => Text("MotifApp.RefusalRuleAttemptsInvalid");

    public static string RefusalRuleAttemptsNoBaseline => Text("MotifApp.RefusalRuleAttemptsNoBaseline");

    public static string RefusalRuleAttemptsPlanInvalid => Text("MotifApp.RefusalRuleAttemptsPlanInvalid");

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

    public static string RefusalSelectionReaderPresentationChanged => Text("MotifApp.RefusalSelectionReaderPresentationChanged");

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

    public static string RefusalStoreOtherProject => Text("MotifApp.RefusalStoreOtherProject");
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

    public static string RefusalRefreshFailedCode => Text("MotifApp.RefusalRefreshFailedCode");

    public static string RefusalRefreshCancelledCode => Text("MotifApp.RefusalRefreshCancelledCode");

    public static string RefusalRefreshUnavailableCode => Text("MotifApp.RefusalRefreshUnavailableCode");

    public static string RefusalTrialNothingPending => Text("MotifApp.RefusalTrialNothingPending");

    public static string RefusalTrialProposalGone => Text("MotifApp.RefusalTrialProposalGone");

    public static string RefusalTrialUpdateInProgress => Text("MotifApp.RefusalTrialUpdateInProgress");

    public static string RefusalRejectionTraceRequestInvalid => Text("MotifApp.RefusalRejectionTraceRequestInvalid");

    public static string RefusalRejectionTraceUnsupported => Text("MotifApp.RefusalRejectionTraceUnsupported");

    public static string RefusalRejectionTraceCaptureRefused => Text("MotifApp.RefusalRejectionTraceCaptureRefused");

    public static string RefusalRejectionTraceMalformed => Text("MotifApp.RefusalRejectionTraceMalformed");

    public static string RefusalRejectionTraceCaptureLimit => Text("MotifApp.RefusalRejectionTraceCaptureLimit");

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

    public static string TryWordRecordedTreeContextHeading => Text("MotifApp.TryWordRecordedTreeContextHeading");

    public static string TraceFullMorphDetailsHeading => Text("MotifApp.TraceFullMorphDetailsHeading");

    public static string TraceRawMorphRecordHeading => Text("MotifApp.TraceRawMorphRecordHeading");

    public static string TraceKindWord => Text("MotifApp.TraceKindWord");

    public static string ExpertAnalysisPathPlaceholder => Text("MotifApp.ExpertAnalysisPathPlaceholder");

    public static string StatisticsFilterPlaceholder => Text("MotifApp.StatisticsFilterPlaceholder");

    public static string CompareSearchWordsPlaceholder => Text("MotifApp.CompareSearchWordsPlaceholder");

    public static string ExpertMoreRuleStepsPattern => Text("MotifApp.ExpertMoreRuleStepsPattern");

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

    public static string ProjectCardTextsHeading => Text("MotifApp.ProjectCardTextsHeading");

    public static string ProjectCardTextsSummaryFormat => Text("MotifApp.ProjectCardTextsSummaryFormat");

    public static string ProjectCardTextsCountPattern => Text("MotifApp.ProjectCardTextsCountPattern");

    public static string ProjectCardNoTextsChosen => Text("MotifApp.ProjectCardNoTextsChosen");

    public static string ProjectCardButtonChoose => Text("MotifApp.ProjectCardButtonChoose");

    public static string ProjectCardButtonProjectSettings => Text("MotifApp.ProjectCardButtonProjectSettings");

    public static string ProjectCardProjectSettingsAccessibleName => Text("MotifApp.ProjectCardProjectSettingsAccessibleName");

    public static string ProjectCardButtonOpenAnother => Text("MotifApp.ProjectCardButtonOpenAnother");

    public static string ProjectCardOpenAnotherAccessibleName => Text("MotifApp.ProjectCardOpenAnotherAccessibleName");

    public static string TechDemoNoticeText => Text("MotifApp.TechDemoNoticeText");

    public static string TechDemoNoticeGotIt => Text("MotifApp.TechDemoNoticeGotIt");

    public static string TechDemoNoticeGotItAccessibleName => Text("MotifApp.TechDemoNoticeGotItAccessibleName");

    public static string ProjectCardTextsCountFormat => Text("MotifApp.ProjectCardTextsCountFormat");

    public static string OverviewSpeedWordsPattern => Text("MotifApp.OverviewSpeedWordsPattern");

    public static string OverviewSpeedMainFormat => Text("MotifApp.OverviewSpeedMainFormat");

    public static string OverviewSpeedNoTimes => Text("MotifApp.OverviewSpeedNoTimes");

    public static string OverviewSpeedMedianFormat => Text("MotifApp.OverviewSpeedMedianFormat");

    public static string OverviewSpeedElapsedFormat => Text("MotifApp.OverviewSpeedElapsedFormat");

    public static string OverviewSpeedMedianWithPercentileFormat => Text("MotifApp.OverviewSpeedMedianWithPercentileFormat");

    public static string OverviewSpeedStoppedPattern => Text("MotifApp.OverviewSpeedStoppedPattern");

    public static string OverviewSlowestSummaryFormat => Text("MotifApp.OverviewSlowestSummaryFormat");

    public static string OverviewSlowestWordFormat => Text("MotifApp.OverviewSlowestWordFormat");

    public static string OverviewSlowestStoppedWordFormat => Text("MotifApp.OverviewSlowestStoppedWordFormat");

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

    public static string ParserWorkFormat => Text("MotifApp.ParserWorkFormat");

    public static string TraceParserWorkNotRecorded => Text("MotifApp.TraceParserWorkNotRecorded");

    public static string ParserWorkPattern => Text("MotifApp.ParserWorkPattern");

    public static string ParserWorkNotRecorded => Text("MotifApp.ParserWorkNotRecorded");

    public static string DurationSecondsFormat => Text("MotifApp.DurationSecondsFormat");

    public static string DurationMillisecondsFormat => Text("MotifApp.DurationMillisecondsFormat");

    public static string DurationSubMillisecondsFormat => Text("MotifApp.DurationSubMillisecondsFormat");

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

    public static string TraceEffortDescription => Text("MotifApp.TraceEffortDescription");
    public static string TraceSegmentsExaminedLabel => Text("MotifApp.TraceSegmentsExaminedLabel");
    public static string TraceParserWorkLabel => Text("MotifApp.TraceParserWorkLabel");

    public static string DroppedGrammarBlockerFormat => Text("MotifApp.DroppedGrammarBlockerFormat");

    public static string DroppedGrammarObjectFormat => Text("MotifApp.DroppedGrammarObjectFormat");

    public static string DroppedGrammarUntitledObjectFormat => Text("MotifApp.DroppedGrammarUntitledObjectFormat");

    public static string DroppedGrammarUnknownObject => Text("MotifApp.DroppedGrammarUnknownObject");

    public static string DroppedGrammarMoreFormat => Text("MotifApp.DroppedGrammarMoreFormat");

    public static string DroppedKindEnvironment => Text("MotifApp.DroppedKindEnvironment");

    public static string DroppedKindAllomorph => Text("MotifApp.DroppedKindAllomorph");

    public static string DroppedKindEntry => Text("MotifApp.DroppedKindEntry");

    public static string DroppedKindRule => Text("MotifApp.DroppedKindRule");

    public static string DroppedKindSoundClass => Text("MotifApp.DroppedKindSoundClass");

    public static string DroppedKindSlot => Text("MotifApp.DroppedKindSlot");

    public static string DroppedKindStemName => Text("MotifApp.DroppedKindStemName");

    public static string DroppedKindProhibition => Text("MotifApp.DroppedKindProhibition");

    public static string DroppedKindStratum => Text("MotifApp.DroppedKindStratum");

    public static string DroppedKindOther => Text("MotifApp.DroppedKindOther");


    public static string OverviewAmbiguityTopCaption => Text("MotifApp.OverviewAmbiguityTopCaption");

    public static string ProposalParsimonyTitle => Text("MotifApp.ProposalParsimonyTitle");

    public static string ProposalParsimonyAccessibleName => Text("MotifApp.ProposalParsimonyAccessibleName");

    public static string ProposalParsimonyReading => Text("MotifApp.ProposalParsimonyReading");

    public static string ProposalParsimonyUnreadable => Text("MotifApp.ProposalParsimonyUnreadable");

    public static string ProposalParsimonyStateNotSetUp => Text("MotifApp.ProposalParsimonyStateNotSetUp");

    public static string ProposalParsimonyStateNotYetRun => Text("MotifApp.ProposalParsimonyStateNotYetRun");

    public static string ProposalParsimonyStateQueued => Text("MotifApp.ProposalParsimonyStateQueued");

    public static string ProposalParsimonyStateRunning => Text("MotifApp.ProposalParsimonyStateRunning");

    public static string ProposalParsimonyStateReady => Text("MotifApp.ProposalParsimonyStateReady");

    public static string ProposalParsimonyStateStale => Text("MotifApp.ProposalParsimonyStateStale");

    public static string ProposalParsimonyStateFailed => Text("MotifApp.ProposalParsimonyStateFailed");

    public static string ProposalParsimonyStateAbstained => Text("MotifApp.ProposalParsimonyStateAbstained");

    public static string ProposalParsimonyNotSetUp => Text("MotifApp.ProposalParsimonyNotSetUp");

    public static string ProposalParsimonyNotYetRunNoTrial => Text("MotifApp.ProposalParsimonyNotYetRunNoTrial");

    public static string ProposalParsimonyNotYetRunWaiting => Text("MotifApp.ProposalParsimonyNotYetRunWaiting");

    public static string ProposalParsimonyQueuedOther => Text("MotifApp.ProposalParsimonyQueuedOther");

    public static string ProposalParsimonyQueuedTrial => Text("MotifApp.ProposalParsimonyQueuedTrial");

    public static string ProposalParsimonyQueuedParse => Text("MotifApp.ProposalParsimonyQueuedParse");

    public static string ProposalParsimonyRunning => Text("MotifApp.ProposalParsimonyRunning");

    public static string ProposalParsimonyRunningStepFormat => Text("MotifApp.ProposalParsimonyRunningStepFormat");

    public static string ProposalParsimonyRunningStageFormat => Text("MotifApp.ProposalParsimonyRunningStageFormat");

    public static string ProposalParsimonyStageFacts => Text("MotifApp.ProposalParsimonyStageFacts");

    public static string ProposalParsimonyStageValidate => Text("MotifApp.ProposalParsimonyStageValidate");

    public static string ProposalParsimonyStageMeasure => Text("MotifApp.ProposalParsimonyStageMeasure");

    public static string ProposalParsimonyStageRender => Text("MotifApp.ProposalParsimonyStageRender");

    public static string ProposalParsimonyStageWaitingForParser => Text("MotifApp.ProposalParsimonyStageWaitingForParser");

    public static string ProposalParsimonyReady => Text("MotifApp.ProposalParsimonyReady");

    public static string ProposalParsimonyReadyBehind => Text("MotifApp.ProposalParsimonyReadyBehind");

    public static string ProposalParsimonyStaleFormat => Text("MotifApp.ProposalParsimonyStaleFormat");

    public static string ProposalParsimonyStaleUnknown => Text("MotifApp.ProposalParsimonyStaleUnknown");

    public static string ProposalParsimonyChangedBaseline => Text("MotifApp.ProposalParsimonyChangedBaseline");

    public static string ProposalParsimonyChangedProposal => Text("MotifApp.ProposalParsimonyChangedProposal");

    public static string ProposalParsimonyChangedPrerequisite => Text("MotifApp.ProposalParsimonyChangedPrerequisite");

    public static string ProposalParsimonyChangedParser => Text("MotifApp.ProposalParsimonyChangedParser");

    public static string ProposalParsimonyChangedSchema => Text("MotifApp.ProposalParsimonyChangedSchema");

    public static string ProposalParsimonyChangedMeasures => Text("MotifApp.ProposalParsimonyChangedMeasures");

    public static string ProposalParsimonyChangedSelection => Text("MotifApp.ProposalParsimonyChangedSelection");

    public static string ProposalParsimonyChangedJudgments => Text("MotifApp.ProposalParsimonyChangedJudgments");
    public static string ProposalParsimonyStateUnknown => Text("MotifApp.ProposalParsimonyStateUnknown");
    public static string ProposalParsimonyUnknown => Text("MotifApp.ProposalParsimonyUnknown");
    public static string ProposalParsimonyChangedUnknown => Text("MotifApp.ProposalParsimonyChangedUnknown");

    public static string ProposalParsimonyReadNothingNewComputed => Text("MotifApp.ProposalParsimonyReadNothingNewComputed");

    public static string ProposalParsimonyReadUnknownPattern => Text("MotifApp.ProposalParsimonyReadUnknownPattern");

    public static string ProposalParsimonyReadNotComputedPattern => Text("MotifApp.ProposalParsimonyReadNotComputedPattern");

    public static string ProposalParsimonyReadGrammarNoResult => Text("MotifApp.ProposalParsimonyReadGrammarNoResult");

    public static string ProposalParsimonyReadTextNotMeasured => Text("MotifApp.ProposalParsimonyReadTextNotMeasured");

    public static string ProposalParsimonyFindingsAllDecided => Text("MotifApp.ProposalParsimonyFindingsAllDecided");

    public static string ProposalParsimonyFindingsPartial => Text("MotifApp.ProposalParsimonyFindingsPartial");

    public static string ProposalParsimonyFindingsMoreLineFormat => Text("MotifApp.ProposalParsimonyFindingsMoreLineFormat");

    public static string ProposalParsimonyReadTitle => Text("MotifApp.ProposalParsimonyReadTitle");

    public static string ProposalParsimonyReadNothingNew => Text("MotifApp.ProposalParsimonyReadNothingNew");

    public static string ProposalParsimonyReadNewPattern => Text("MotifApp.ProposalParsimonyReadNewPattern");

    public static string ProposalParsimonyReadResolvedPattern => Text("MotifApp.ProposalParsimonyReadResolvedPattern");

    public static string ProposalParsimonyParserWorkLabel => Text("MotifApp.ProposalParsimonyParserWorkLabel");

    public static string ProposalParsimonyParserWorkMoreFormat => Text("MotifApp.ProposalParsimonyParserWorkMoreFormat");

    public static string ProposalParsimonyParserWorkLessFormat => Text("MotifApp.ProposalParsimonyParserWorkLessFormat");

    public static string ProposalParsimonyParserWorkSame => Text("MotifApp.ProposalParsimonyParserWorkSame");

    public static string ProposalParsimonyWallTimeSecondsPattern => Text("MotifApp.ProposalParsimonyWallTimeSecondsPattern");

    public static string ProposalParsimonyWallTimeMinutesPattern => Text("MotifApp.ProposalParsimonyWallTimeMinutesPattern");

    public static string ProposalParsimonyFindingsHeaderPattern => Text("MotifApp.ProposalParsimonyFindingsHeaderPattern");

    public static string ProposalParsimonyFindingsMorePattern => Text("MotifApp.ProposalParsimonyFindingsMorePattern");

    public static string ProposalParsimonyFindingsNone => Text("MotifApp.ProposalParsimonyFindingsNone");

    public static string ProposalParsimonyFindingsNoneComputed => Text("MotifApp.ProposalParsimonyFindingsNoneComputed");

    public static string ProposalParsimonyFindingKindNew => Text("MotifApp.ProposalParsimonyFindingKindNew");

    public static string ProposalParsimonyFindingKindChanged => Text("MotifApp.ProposalParsimonyFindingKindChanged");

    public static string ProposalParsimonyFindingLineFormat => Text("MotifApp.ProposalParsimonyFindingLineFormat");

    public static string ProposalParsimonyFindingsUnreadable => Text("MotifApp.ProposalParsimonyFindingsUnreadable");

    public static string ProposalParsimonyFindingsNotComparable => Text("MotifApp.ProposalParsimonyFindingsNotComparable");

    public static string ProposalParsimonyJudgmentsLineFormat => Text("MotifApp.ProposalParsimonyJudgmentsLineFormat");

    public static string ProposalParsimonyJudgmentsSuppressedPattern => Text("MotifApp.ProposalParsimonyJudgmentsSuppressedPattern");

    public static string ProposalParsimonyJudgmentsResurfacedPattern => Text("MotifApp.ProposalParsimonyJudgmentsResurfacedPattern");

    public static string ProposalParsimonyJudgmentsUnresolvedPattern => Text("MotifApp.ProposalParsimonyJudgmentsUnresolvedPattern");

    public static string ProposalParsimonyShowEvidence => Text("MotifApp.ProposalParsimonyShowEvidence");

    public static string ProposalParsimonyShowEvidenceAccessibleName => Text("MotifApp.ProposalParsimonyShowEvidenceAccessibleName");

    public static string ProposalParsimonyEvidenceReading => Text("MotifApp.ProposalParsimonyEvidenceReading");

    public static string ProposalParsimonyEvidenceNone => Text("MotifApp.ProposalParsimonyEvidenceNone");

    public static string ProposalParsimonyEvidenceLineFormat => Text("MotifApp.ProposalParsimonyEvidenceLineFormat");

    public static string ProposalParsimonyEvidenceRowsPattern => Text("MotifApp.ProposalParsimonyEvidenceRowsPattern");

    public static string ProposalParsimonyEvidenceNotKept => Text("MotifApp.ProposalParsimonyEvidenceNotKept");

    public static string ProposalParsimonyEvidenceNotAvailable => Text("MotifApp.ProposalParsimonyEvidenceNotAvailable");

    public static string ProposalParsimonyEvidenceUnreadable => Text("MotifApp.ProposalParsimonyEvidenceUnreadable");

    public static string ProposalParsimonyViewAffixContext => Text("MotifApp.ProposalParsimonyViewAffixContext");

    public static string ProposalParsimonyViewAdhocContext => Text("MotifApp.ProposalParsimonyViewAdhocContext");

    public static string ProposalParsimonyViewAllomorphContext => Text("MotifApp.ProposalParsimonyViewAllomorphContext");

    public static string ProposalParsimonyViewAlternationFamilies => Text("MotifApp.ProposalParsimonyViewAlternationFamilies");

    public static string ProposalParsimonyViewApprovedMorphSequences => Text("MotifApp.ProposalParsimonyViewApprovedMorphSequences");

    public static string ProposalParsimonyViewEnvironmentExcess => Text("MotifApp.ProposalParsimonyViewEnvironmentExcess");

    public static string ProposalParsimonyViewJoinQuality => Text("MotifApp.ProposalParsimonyViewJoinQuality");

    public static string ProposalParsimonyViewNaturalClassExcess => Text("MotifApp.ProposalParsimonyViewNaturalClassExcess");

    public static string ProposalParsimonyViewNaturalClassContext => Text("MotifApp.ProposalParsimonyViewNaturalClassContext");

    public static string ProposalParsimonyViewParserCases => Text("MotifApp.ProposalParsimonyViewParserCases");

    public static string ProposalParsimonyViewStatementUsage => Text("MotifApp.ProposalParsimonyViewStatementUsage");

    public static string ProposalParsimonyViewSlotContext => Text("MotifApp.ProposalParsimonyViewSlotContext");

    public static string ProposalParsimonyViewTemplateOrder => Text("MotifApp.ProposalParsimonyViewTemplateOrder");

    public static string ProposalParsimonyViewUnslottedAffixes => Text("MotifApp.ProposalParsimonyViewUnslottedAffixes");

    public static string ProposalParsimonyViewNullOptional => Text("MotifApp.ProposalParsimonyViewNullOptional");

    public static string ProposalParsimonyViewOther => Text("MotifApp.ProposalParsimonyViewOther");

    public static string ProposalParsimonyFailedFormat => Text("MotifApp.ProposalParsimonyFailedFormat");

    public static string ProposalParsimonyFailedTimedOut => Text("MotifApp.ProposalParsimonyFailedTimedOut");

    public static string ProposalParsimonyFailedNotAgain => Text("MotifApp.ProposalParsimonyFailedNotAgain");

    public static string ProposalParsimonyFailedParserUnavailable => Text("MotifApp.ProposalParsimonyFailedParserUnavailable");

    public static string ProposalParsimonyFailedFactsFailed => Text("MotifApp.ProposalParsimonyFailedFactsFailed");

    public static string ProposalParsimonyFailedEvidenceFailed => Text("MotifApp.ProposalParsimonyFailedEvidenceFailed");

    public static string ProposalParsimonyFailedCancelled => Text("MotifApp.ProposalParsimonyFailedCancelled");

    public static string ProposalParsimonyFailedInternal => Text("MotifApp.ProposalParsimonyFailedInternal");


    public static string ProposalParsimonyGroupGrammar => Text("MotifApp.ProposalParsimonyGroupGrammar");

    public static string ProposalParsimonyGroupText => Text("MotifApp.ProposalParsimonyGroupText");

    public static string ProposalParsimonyGrammarNotYetRun => Text("MotifApp.ProposalParsimonyGrammarNotYetRun");

    public static string ProposalParsimonyGroupAbstained => Text("MotifApp.ProposalParsimonyGroupAbstained");

    public static string ProposalParsimonyGroupFailed => Text("MotifApp.ProposalParsimonyGroupFailed");

    public static string ProposalParsimonyTextGroupManual => Text("MotifApp.ProposalParsimonyTextGroupManual");

    public static string ProposalParsimonyTextGroupNeedsSelection => Text("MotifApp.ProposalParsimonyTextGroupNeedsSelection");

    public static string ProposalParsimonyCountsHeader => Text("MotifApp.ProposalParsimonyCountsHeader");

    public static string ProposalParsimonyCountsNote => Text("MotifApp.ProposalParsimonyCountsNote");

    public static string ProposalParsimonyMeasureCountsFormat => Text("MotifApp.ProposalParsimonyMeasureCountsFormat");

    public static string ProposalParsimonyMeasureNotRun => Text("MotifApp.ProposalParsimonyMeasureNotRun");

    public static string ProposalParsimonyMeasureNotAvailable => Text("MotifApp.ProposalParsimonyMeasureNotAvailable");

    public static string ProposalParsimonyMeasureInconclusive => Text("MotifApp.ProposalParsimonyMeasureInconclusive");

    public static string ProposalParsimonyMeasureFailed => Text("MotifApp.ProposalParsimonyMeasureFailed");

    public static string ProposalParsimonyMeasureNoCounts => Text("MotifApp.ProposalParsimonyMeasureNoCounts");

    public static string OverviewPairFormat => Text("MotifApp.OverviewPairFormat");

    public static string TimingNoneToday => Text("MotifApp.TimingNoneToday");

    public static string WarningsGoneWithProposalStatus => Text("MotifApp.WarningsGoneWithProposalStatus");

    public static string WarningsNewWithProposalStatus => Text("MotifApp.WarningsNewWithProposalStatus");

    public static string WarningsGoneMark => Text("MotifApp.WarningsGoneMark");

    public static string WarningsNewMark => Text("MotifApp.WarningsNewMark");

    public static string WarningsDetailsForFormat => Text("MotifApp.WarningsDetailsForFormat");

    public static string WarningsActionsForFormat => Text("MotifApp.WarningsActionsForFormat");

    public static string WarningsMarkedRowFormat => Text("MotifApp.WarningsMarkedRowFormat");

    public static string ProposalDifferencePlacesMorePattern => Text("MotifApp.ProposalDifferencePlacesMorePattern");

    public static string WordRowWithProposalFormat => Text("MotifApp.WordRowWithProposalFormat");
    public static string WordRowSummaryWithProposalLabel => Text("MotifApp.WordRowSummaryWithProposalLabel");
    public static string WordRowSummaryPanGlossTodayFormat => Text("MotifApp.WordRowSummaryPanGlossTodayFormat");
    public static string WordRowTimeWithProposalFormat => Text("MotifApp.WordRowTimeWithProposalFormat");
    public static string TextsWordNotMeasured => Text("MotifApp.TextsWordNotMeasured");

    public static string DifferenceWithProposalFormat => Text("MotifApp.DifferenceWithProposalFormat");

    public static string DifferenceProposalStillProblemFormat => Text("MotifApp.DifferenceProposalStillProblemFormat");

    public static string DifferenceProposalTransitionFormat => Text("MotifApp.DifferenceProposalTransitionFormat");

    public static string DifferenceProposalCellFormat => Text("MotifApp.DifferenceProposalCellFormat");

    public static string DifferenceProposalEmptyState => Text("MotifApp.DifferenceProposalEmptyState");

    public static string DifferenceProposalOnlyInOneFormat => Text("MotifApp.DifferenceProposalOnlyInOneFormat");

    public static string DifferenceProposalFromLabel => Text("MotifApp.DifferenceProposalFromLabel");

    public static string DifferenceProposalToLabel => Text("MotifApp.DifferenceProposalToLabel");

    public static string DifferenceMovedNoneWorseUnknownFormat => Text("MotifApp.DifferenceMovedNoneWorseUnknownFormat");

    public static string DifferenceNowUnknownPattern => Text("MotifApp.DifferenceNowUnknownPattern");

    public static string HandoffStepOneMark => Text("MotifApp.HandoffStepOneMark");

    public static string HandoffStepTwoMark => Text("MotifApp.HandoffStepTwoMark");

    public static string HandoffStepThreeMark => Text("MotifApp.HandoffStepThreeMark");

    public static string ChosenCellSeparatorMark => Text("MotifApp.ChosenCellSeparatorMark");










    public static string LoadingProject => Text("MotifApp.LoadingProject");














    public static string RefusalRequestInvalid => Text("MotifApp.RefusalRequestInvalid");

    public static string RefusalRequestNoRequest => Text("MotifApp.RefusalRequestNoRequest");

    public static string RefusalRequestNotFound => Text("MotifApp.RefusalRequestNotFound");

    public static string RefusalRequestNotOpen => Text("MotifApp.RefusalRequestNotOpen");

    public static string TraceEventsOnPath => Text("MotifApp.TraceEventsOnPath");

    public static string TraceSearchEvents => Text("MotifApp.TraceSearchEvents");

    public static string TraceSelectedEvent => Text("MotifApp.TraceSelectedEvent");
    public static string CompareParagraphMovedMineFormat => Text("MotifApp.CompareParagraphMovedMineFormat");
    public static string CompareParagraphMovedOtherFormat => Text("MotifApp.CompareParagraphMovedOtherFormat");
    public static string CompareParagraphMovedBothFormat => Text("MotifApp.CompareParagraphMovedBothFormat");
    public static string CompareChooseParagraphOrderWaitingFormat => Text("MotifApp.CompareChooseParagraphOrderWaitingFormat");
    public static string CompareChangeNotUsed => Text("MotifApp.CompareChangeNotUsed");
    public static string CompareParagraphOnePosition => Text("MotifApp.CompareParagraphOnePosition");
    public static string CompareParagraphRemovedUnavailableFormat => Text("MotifApp.CompareParagraphRemovedUnavailableFormat");
    public static string CompareUseYourOrder => Text("MotifApp.CompareUseYourOrder");
    public static string CompareUseOtherOrderFormat => Text("MotifApp.CompareUseOtherOrderFormat");
    public static string CompareParagraphRemovedUnavailableAccessibleFormat => Text("MotifApp.CompareParagraphRemovedUnavailableAccessibleFormat");

    #region W2
    public static string SourcesFieldWorksTexts => Text("MotifApp.SourcesFieldWorksTexts");
    public static string SourcesNone => Text("MotifApp.SourcesNone");
    public static string SourcesExternalPassageUnavailable => Text("MotifApp.SourcesExternalPassageUnavailable");
    public static string SourcesUnavailable => Text("MotifApp.SourcesUnavailable");
    public static string SourcesRemoved => Text("MotifApp.SourcesRemoved");
    public static string SourcesActive => Text("MotifApp.SourcesActive");
    public static string SourcesOutdated => Text("MotifApp.SourcesOutdated");
    public static string SourcesNoLongerAvailable => Text("MotifApp.SourcesNoLongerAvailable");
    public static string SourcesContext => Text("MotifApp.SourcesContext");
    public static string SourcesCurrent => Text("MotifApp.SourcesCurrent");
    public static string SourcesFolder => Text("MotifApp.SourcesFolder");
    public static string SourcesSetFolder => Text("MotifApp.SourcesSetFolder");
    public static string SourcesSetCurrent => Text("MotifApp.SourcesSetCurrent");
    public static string SourcesClearCurrent => Text("MotifApp.SourcesClearCurrent");
    public static string SourcesStageContext => Text("MotifApp.SourcesStageContext");
    public static string SourcesAddParatext => Text("MotifApp.SourcesAddParatext");
    public static string SourcesId => Text("MotifApp.SourcesId");
    public static string SourcesName => Text("MotifApp.SourcesName");
    public static string SourcesVersification => Text("MotifApp.SourcesVersification");
    public static string SourcesStageAdd => Text("MotifApp.SourcesStageAdd");
    public static string SourcesStaged => Text("MotifApp.SourcesStaged");
    public static string SourcesLocalSaved => Text("MotifApp.SourcesLocalSaved");
    public static string SourcesLocalExplanation => Text("MotifApp.SourcesLocalExplanation");
    public static string SourcesFieldWorksExplanation => Text("MotifApp.SourcesFieldWorksExplanation");
    public static string SourcesRefresh => Text("MotifApp.SourcesRefresh");
    public static string SourcesSavedContext => Text("MotifApp.SourcesSavedContext");
    public static string SourcesFieldWorksSelection => Text("MotifApp.SourcesFieldWorksSelection");
    public static string SourcesPassageUnavailable => Text("MotifApp.SourcesPassageUnavailable");
    public static string SourcesInvalidChapter => Text("MotifApp.SourcesInvalidChapter");
    public static string SourcesInvalidContext => Text("MotifApp.SourcesInvalidContext");
    public static string SourcesFolderUnavailable => Text("MotifApp.SourcesFolderUnavailable");
    public static string SourcesChapterUnavailable => Text("MotifApp.SourcesChapterUnavailable");
    public static string SourcesActionUnavailable => Text("MotifApp.SourcesActionUnavailable");
    public static string SourcesPendingExplanation => Text("MotifApp.SourcesPendingExplanation");
    public static string SourcesUsePending => Text("MotifApp.SourcesUsePending");
    #endregion
    // w8 diagnostics resources.
    public static string DiagnosticsIntro => Text("MotifApp.DiagnosticsIntro");
    public static string DiagnosticsUnavailable => Text("MotifApp.DiagnosticsUnavailable");
    public static string DiagnosticsNoWarnings => Text("MotifApp.DiagnosticsNoWarnings");
    public static string DiagnosticsWarningCountFormat => Text("MotifApp.DiagnosticsWarningCountFormat");
    public static string DiagnosticsMeasuredFormat => Text("MotifApp.DiagnosticsMeasuredFormat");
    public static string DiagnosticsNoWork => Text("MotifApp.DiagnosticsNoWork");
    public static string DiagnosticsNoWorkRetriedWords => Text("MotifApp.DiagnosticsNoWorkRetriedWords");
    public static string DiagnosticsNoWorkWordWithoutWork => Text("MotifApp.DiagnosticsNoWorkWordWithoutWork");
    public static string DiagnosticsNoWorkObjectWithoutWorkOrIdentity => Text("MotifApp.DiagnosticsNoWorkObjectWithoutWorkOrIdentity");
    public static string DiagnosticsNoWorkNoUnattributedRow => Text("MotifApp.DiagnosticsNoWorkNoUnattributedRow");
    public static string DiagnosticsNoWorkSumsDoNotMatch => Text("MotifApp.DiagnosticsNoWorkSumsDoNotMatch");
    public static string DiagnosticsNoWorkNotWholeRun => Text("MotifApp.DiagnosticsNoWorkNotWholeRun");
    public static string DiagnosticsNoWorkStatisticsUnavailable => Text("MotifApp.DiagnosticsNoWorkStatisticsUnavailable");
    public static string DiagnosticsSkippedFormat => Text("MotifApp.DiagnosticsSkippedFormat");
    public static string DiagnosticsSkipped => Text("MotifApp.DiagnosticsSkipped");
    public static string DiagnosticsPartial => Text("MotifApp.DiagnosticsPartial");
    public static string DiagnosticsWarningsFormat => Text("MotifApp.DiagnosticsWarningsFormat");
    public static string DiagnosticsTrialUnavailable => Text("MotifApp.DiagnosticsTrialUnavailable");
    public static string DiagnosticsSignedUnavailable => Text("MotifApp.DiagnosticsSignedUnavailable");
    public static string DiagnosticsOperations => Text("MotifApp.DiagnosticsOperations");
    public static string DiagnosticsStepsFormat => Text("MotifApp.DiagnosticsStepsFormat");
    public static string DiagnosticsWorkUnavailable => Text("MotifApp.DiagnosticsWorkUnavailable");
    public static string DiagnosticsOutOfDate => Text("MotifApp.DiagnosticsOutOfDate");
    public static string DiagnosticsReading => Text("MotifApp.DiagnosticsReading");
    public static string DiagnosticsUnreadable => Text("MotifApp.DiagnosticsUnreadable");
    public static string DiagnosticsMismatched => Text("MotifApp.DiagnosticsMismatched");
    public static string DiagnosticsParsimonyMeasuring => Text("MotifApp.DiagnosticsParsimonyMeasuring");
    public static string DiagnosticsParsimonyMeasuringStepFormat => Text("MotifApp.DiagnosticsParsimonyMeasuringStepFormat");
    public static string DiagnosticsParsimonyMeasuringStageFormat => Text("MotifApp.DiagnosticsParsimonyMeasuringStageFormat");
    // W9-resources
    public static string ProgressReadingNext => Text("MotifApp.ProgressReadingNext");
    public static string ProgressNextUnavailable => Text("MotifApp.ProgressNextUnavailable");

    #region W5
    public static string ProposalDocumentOutdated => Text("MotifApp.ProposalDocumentOutdated");
    public static string ProposalDocumentUnreadable => Text("MotifApp.ProposalDocumentUnreadable");
    public static string ProposalEvidenceUnavailable => Text("MotifApp.ProposalEvidenceUnavailable");
    public static string ProposalEvidenceScope => Text("MotifApp.ProposalEvidenceScope");
    public static string ProposalEvidencePartial => Text("MotifApp.ProposalEvidencePartial");
    public static string ProposalEvidencePlacesUnavailable => Text("MotifApp.ProposalEvidencePlacesUnavailable");
    public static string ProposalEvidenceNoWords => Text("MotifApp.ProposalEvidenceNoWords");
    public static string ProposalCloseDocument => Text("MotifApp.ProposalCloseDocument");
    public static string ProposalComparisonHint => Text("MotifApp.ProposalComparisonHint");
    public static string ProposalComparisonLimit => Text("MotifApp.ProposalComparisonLimit");
    public static string ProposalApplyConfirm => Text("MotifApp.ProposalApplyConfirm");
    public static string ProposalApplyPrepare => Text("MotifApp.ProposalApplyPrepare");
    public static string ProposalRefreshEvidence => Text("MotifApp.ProposalRefreshEvidence");
    public static string ProposalCheckForComparison => Text("MotifApp.ProposalCheckForComparison");
    public static string ProposalReading => Text("MotifApp.ProposalReading");
    public static string ProposalReadingChanged => Text("MotifApp.ProposalReadingChanged");
    public static string ProposalSpellingChanged => Text("MotifApp.ProposalSpellingChanged");
    public static string ProposalEvidenceListedCount => Text("MotifApp.ProposalEvidenceListedCount");
    public static string ProposalEvidenceCompleteCount => Text("MotifApp.ProposalEvidenceCompleteCount");
    public static string ProposalDraft => Text("MotifApp.ProposalDraft");
    public static string ProposalReady => Text("MotifApp.ProposalReady");
    public static string ProposalDeferred => Text("MotifApp.ProposalDeferred");
    public static string ProposalDeclined => Text("MotifApp.ProposalDeclined");
    public static string ProposalSuperseded => Text("MotifApp.ProposalSuperseded");
    public static string ProposalWithdrawn => Text("MotifApp.ProposalWithdrawn");
    public static string ProposalActivateForApply => Text("MotifApp.ProposalActivateForApply");
    public static string ProposalSelectedNameFormat => Text("MotifApp.ProposalSelectedNameFormat");
    public static string ProposalReceiptFormat => Text("MotifApp.ProposalReceiptFormat");
    public static string ReviewApplyMeasuring => Text("MotifApp.ReviewApplyMeasuring");
    #endregion
    #region W6
    public static string ProposalCompareBack => Text("MotifApp.ProposalCompareBack");
    public static string ProposalCompareHeading => Text("MotifApp.ProposalCompareHeading");
    public static string ProposalCompareFirstHeading => Text("MotifApp.ProposalCompareFirstHeading");
    public static string ProposalCompareSecondHeading => Text("MotifApp.ProposalCompareSecondHeading");
    public static string ProposalCompareBasisFormat => Text("MotifApp.ProposalCompareBasisFormat");
    public static string ProposalCompareReading => Text("MotifApp.ProposalCompareReading");
    public static string ProposalCompareOutdatedTitle => Text("MotifApp.ProposalCompareOutdatedTitle");
    public static string ProposalCompareUnavailableTitle => Text("MotifApp.ProposalCompareUnavailableTitle");
    public static string ProposalCompareReasonNoEvidence => Text("MotifApp.ProposalCompareReasonNoEvidence");
    public static string ProposalCompareReasonOutdated => Text("MotifApp.ProposalCompareReasonOutdated");
    public static string ProposalCompareReasonText => Text("MotifApp.ProposalCompareReasonText");
    public static string ProposalCompareReasonBaseline => Text("MotifApp.ProposalCompareReasonBaseline");
    public static string ProposalCompareReasonWords => Text("MotifApp.ProposalCompareReasonWords");
    public static string ProposalCompareReasonDecisions => Text("MotifApp.ProposalCompareReasonDecisions");
    public static string ProposalCompareReasonDecisionsMissing => Text("MotifApp.ProposalCompareReasonDecisionsMissing");
    public static string ProposalCompareReasonAssessor => Text("MotifApp.ProposalCompareReasonAssessor");
    public static string ProposalCompareReasonIncomplete => Text("MotifApp.ProposalCompareReasonIncomplete");
    public static string ProposalCompareReasonUnreadable => Text("MotifApp.ProposalCompareReasonUnreadable");
    public static string ProposalCompareRemeasure => Text("MotifApp.ProposalCompareRemeasure");
    public static string ProposalCompareRemeasuring => Text("MotifApp.ProposalCompareRemeasuring");
    public static string ProposalCompareDiffer => Text("MotifApp.ProposalCompareDiffer");
    public static string ProposalCompareNoDifference => Text("MotifApp.ProposalCompareNoDifference");
    public static string ProposalCompareNoChange => Text("MotifApp.ProposalCompareNoChange");
    public static string ProposalCompareShowInTexts => Text("MotifApp.ProposalCompareShowInTexts");
    public static string ProposalCompareShowInTextsFormat => Text("MotifApp.ProposalCompareShowInTextsFormat");
    public static string ProposalCompareOverlap => Text("MotifApp.ProposalCompareOverlap");
    public static string ProposalCompareOperationsFormat => Text("MotifApp.ProposalCompareOperationsFormat");
    public static string ProposalCompareCommentsFormat => Text("MotifApp.ProposalCompareCommentsFormat");
    public static string ProposalCompareApplyNote => Text("MotifApp.ProposalCompareApplyNote");
    public static string ProposalCompareSideNameFormat => Text("MotifApp.ProposalCompareSideNameFormat");
    public static string ProposalCompareDifferentResult => Text("MotifApp.ProposalCompareDifferentResult");
    #endregion

    #region W3
    public static string OccurrencesNotAWordScopeFormat => Text("MotifApp.OccurrencesNotAWordScopeFormat");
    public static string OccurrencesStagedStatusPattern => Text("MotifApp.OccurrencesStagedStatusPattern");
    public static string OccurrencesPartlyStagedStatusFormat => Text("MotifApp.OccurrencesPartlyStagedStatusFormat");
    public static string OccurrencesHomographsUnreadStatus => Text("MotifApp.OccurrencesHomographsUnreadStatus");
    public static string OccurrencesHomographReadingFormat => Text("MotifApp.OccurrencesHomographReadingFormat");
    public static string OccurrencesOtherHomographStatusFormat => Text("MotifApp.OccurrencesOtherHomographStatusFormat");
    public static string OccurrencesNotAWordPartlyStagedFormat => Text("MotifApp.OccurrencesNotAWordPartlyStagedFormat");
    public static string OccurrencesNotAWordStagedFormat => Text("MotifApp.OccurrencesNotAWordStagedFormat");
    public static string OccurrencesNotStaged => Text("MotifApp.OccurrencesNotStaged");
    public static string OccurrencesChoose => Text("MotifApp.OccurrencesChoose");
    public static string OccurrencesSelectedPattern => Text("MotifApp.OccurrencesSelectedPattern");
    public static string OccurrencesCheckGaps => Text("MotifApp.OccurrencesCheckGaps");
    public static string OccurrencesNoReadingsReason => Text("MotifApp.OccurrencesNoReadingsReason");
    public static string OccurrencesUnsupportedKeepReason => Text("MotifApp.OccurrencesUnsupportedKeepReason");
    public static string OccurrencesChangedStatus => Text("MotifApp.OccurrencesChangedStatus");
    public static string OccurrencesAlreadyApprovedStatus => Text("MotifApp.OccurrencesAlreadyApprovedStatus");
    public static string OccurrencesAlreadyStagedStatus => Text("MotifApp.OccurrencesAlreadyStagedStatus");
    public static string OccurrencesNothingToKeepStatus => Text("MotifApp.OccurrencesNothingToKeepStatus");
    public static string ShortcutKeepAnalysis => Text("MotifApp.ShortcutKeepAnalysis");
    public static string ShortcutNextGap => Text("MotifApp.ShortcutNextGap");
    public static string ShortcutKeepAllLikeThis => Text("MotifApp.ShortcutKeepAllLikeThis");
    public static string OccurrencesStateNoAnalysis => Text("MotifApp.OccurrencesStateNoAnalysis");
    public static string OccurrencesStateGuessed => Text("MotifApp.OccurrencesStateGuessed");
    public static string OccurrencesStateStored => Text("MotifApp.OccurrencesStateStored");
    public static string OccurrencesPlacesPattern => Text("MotifApp.OccurrencesPlacesPattern");
    public static string OccurrencesPlacesHint => Text("MotifApp.OccurrencesPlacesHint");
    public static string OccurrencesPlaceFormat => Text("MotifApp.OccurrencesPlaceFormat");
    public static string OccurrencesPlaceStaged => Text("MotifApp.OccurrencesPlaceStaged");
    public static string OccurrencesPlaceApproved => Text("MotifApp.OccurrencesPlaceApproved");
    public static string OccurrencesIncludePlace => Text("MotifApp.OccurrencesIncludePlace");
    public static string OccurrencesAnalysis => Text("MotifApp.OccurrencesAnalysis");
    public static string OccurrencesApprovePattern => Text("MotifApp.OccurrencesApprovePattern");
    public static string OccurrencesApproveOne => Text("MotifApp.OccurrencesApproveOne");
    public static string OccurrencesNotAWord => Text("MotifApp.OccurrencesNotAWord");
    public static string OccurrencesRangeFormat => Text("MotifApp.OccurrencesRangeFormat");
    public static string OccurrencesPreviousPage => Text("MotifApp.OccurrencesPreviousPage");
    public static string OccurrencesNextPage => Text("MotifApp.OccurrencesNextPage");
    public static string OccurrencesPanelName => Text("MotifApp.OccurrencesPanelName");
    public static string OccurrencesPlacesName => Text("MotifApp.OccurrencesPlacesName");
    public static string OccurrencesChooseNote => Text("MotifApp.OccurrencesChooseNote");
    public static string OccurrencesNoReadingsNote => Text("MotifApp.OccurrencesNoReadingsNote");
    public static string OccurrencesApproveNote => Text("MotifApp.OccurrencesApproveNote");
    public static string PassageStateNeedsAnalysis => Text("MotifApp.PassageStateNeedsAnalysis");
    public static string PassageNoPlaceStatus => Text("MotifApp.PassageNoPlaceStatus");
    public static string PassageSeveralReadingsStatus => Text("MotifApp.PassageSeveralReadingsStatus");
    public static string PassageKeyFailedFormat => Text("MotifApp.PassageKeyFailedFormat");
    #endregion
    #region w10
    public static string WhyThisAnalysisStepsFormat => Text("MotifApp.WhyThisAnalysisStepsFormat");
    public static string WhyThisAnalysisStepsPattern => Text("MotifApp.WhyThisAnalysisStepsPattern");
    public static string WhyThisAnalysisStepsNotRecorded => Text("MotifApp.WhyThisAnalysisStepsNotRecorded");
    public static string WhyThisAnalysisCollapsedHint => Text("MotifApp.WhyThisAnalysisCollapsedHint");
    public static string WhyThisAnalysisLoading => Text("MotifApp.WhyThisAnalysisLoading");
    public static string WhyThisAnalysisNoProject => Text("MotifApp.WhyThisAnalysisNoProject");
    public static string WhyThisAnalysisRefusedFormat => Text("MotifApp.WhyThisAnalysisRefusedFormat");
    public static string WhyThisAnalysisMismatch => Text("MotifApp.WhyThisAnalysisMismatch");
    public static string WhyThisAnalysisWrongWord => Text("MotifApp.WhyThisAnalysisWrongWord");
    public static string WhyThisAnalysisNotRecorded => Text("MotifApp.WhyThisAnalysisNotRecorded");
    public static string WhyThisAnalysisBuilt => Text("MotifApp.WhyThisAnalysisBuilt");
    public static string WhyThisAnalysisNoAnalysis => Text("MotifApp.WhyThisAnalysisNoAnalysis");
    public static string WhyThisAnalysisRules => Text("MotifApp.WhyThisAnalysisRules");
    public static string WhyThisAnalysisFailedFormat => Text("MotifApp.WhyThisAnalysisFailedFormat");
    public static string WhyThisAnalysisFailedAtLeastFormat => Text("MotifApp.WhyThisAnalysisFailedAtLeastFormat");
    public static string WhyThisAnalysisFailedUnknown => Text("MotifApp.WhyThisAnalysisFailedUnknown");
    public static string WhyThisAnalysisFailedNoneRecorded => Text("MotifApp.WhyThisAnalysisFailedNoneRecorded");
    public static string WhyThisAnalysisCappedFormat => Text("MotifApp.WhyThisAnalysisCappedFormat");
    public static string WhyThisAnalysisAttemptsNotRecorded => Text("MotifApp.WhyThisAnalysisAttemptsNotRecorded");
    public static string WhyThisAnalysisMoreFailedFormat => Text("MotifApp.WhyThisAnalysisMoreFailedFormat");
    public static string WhyThisAnalysisOpenInFieldWorks => Text("MotifApp.WhyThisAnalysisOpenInFieldWorks");
    public static string WhyThisAnalysisOpenNameFormat => Text("MotifApp.WhyThisAnalysisOpenNameFormat");
    public static string WhyThisAnalysisFullTrace => Text("MotifApp.WhyThisAnalysisFullTrace");
    public static string WhyThisAnalysisHideFullTrace => Text("MotifApp.WhyThisAnalysisHideFullTrace");
    public static string WhyThisAnalysisFullTraceName => Text("MotifApp.WhyThisAnalysisFullTraceName");
    public static string WhyThisAnalysisFullTraceCountFormat => Text("MotifApp.WhyThisAnalysisFullTraceCountFormat");
    public static string WhyThisAnalysisCopyTrace => Text("MotifApp.WhyThisAnalysisCopyTrace");
    public static string WhyThisAnalysisCopyFailed => Text("MotifApp.WhyThisAnalysisCopyFailed");
    public static string WhyThisAnalysisOpenFailed => Text("MotifApp.WhyThisAnalysisOpenFailed");
    public static string WhyThisAnalysisRulesNote => Text("MotifApp.WhyThisAnalysisRulesNote");
    public static string WhyThisAnalysisFoldName => Text("MotifApp.WhyThisAnalysisFoldName");
    #endregion
    #region W11b
    public static string OccurrencesPageNotRead => Text("MotifApp.OccurrencesPageNotRead");
    public static string WordCardWhereItAppears => Text("MotifApp.WordCardWhereItAppears");
    #endregion
    #region W11a Grammar Guide
    public static string GuideEdit => Text("MotifApp.GuideEdit");
    public static string GuideDone => Text("MotifApp.GuideDone");
    public static string GuideApply => Text("MotifApp.GuideApply");
    public static string GuideUndoMarkdown => Text("MotifApp.GuideUndoMarkdown");
    public static string GuideOpenMarkdown => Text("MotifApp.GuideOpenMarkdown");
    public static string GuideMarkdownToggle => Text("MotifApp.GuideMarkdownToggle");
    public static string GuideMarkdownToggleName => Text("MotifApp.GuideMarkdownToggleName");
    public static string GuideEditAsMarkdown => Text("MotifApp.GuideEditAsMarkdown");
    public static string GuideParagraphStyle => Text("MotifApp.GuideParagraphStyle");
    public static string GuideCharacterStyle => Text("MotifApp.GuideCharacterStyle");
    public static string GuideWritingSystem => Text("MotifApp.GuideWritingSystem");
    public static string GuideLinkToSection => Text("MotifApp.GuideLinkToSection");
    public static string GuideLinkPathPlaceholder => Text("MotifApp.GuideLinkPathPlaceholder");
    public static string GuideInsertLink => Text("MotifApp.GuideInsertLink");
    public static string GuideBold => Text("MotifApp.GuideBold");
    public static string GuideItalic => Text("MotifApp.GuideItalic");
    public static string GuideUnderline => Text("MotifApp.GuideUnderline");
    public static string GuideNoCharacterStyle => Text("MotifApp.GuideNoCharacterStyle");
    public static string GuideEditorName => Text("MotifApp.GuideEditorName");
    public static string GuideCompare => Text("MotifApp.GuideCompare");
    public static string GuideStaleMessage => Text("MotifApp.GuideStaleMessage");
    public static string GuideUnappliedMessage => Text("MotifApp.GuideUnappliedMessage");
    public static string GuideCouldNotStage => Text("MotifApp.GuideCouldNotStage");
    public static string GuideNothingToCompare => Text("MotifApp.GuideNothingToCompare");
    public static string GuideNothingAppliedCouldNotStage => Text("MotifApp.GuideNothingAppliedCouldNotStage");
    public static string GuideApplied => Text("MotifApp.GuideApplied");
    public static string GuideNothingApplied => Text("MotifApp.GuideNothingApplied");
    public static string GuideStaysReadOnlyFormat => Text("MotifApp.GuideStaysReadOnlyFormat");
    public static string GuideStartAgainTitle => Text("MotifApp.GuideStartAgainTitle");
    public static string GuideGoBack => Text("MotifApp.GuideGoBack");
    public static string GuideDiscardMyEdit => Text("MotifApp.GuideDiscardMyEdit");
    public static string GuideStartAgainBody => Text("MotifApp.GuideStartAgainBody");
    public static string GuideEditMatchesFieldWorks => Text("MotifApp.GuideEditMatchesFieldWorks");
    public static string GuideEditFits => Text("MotifApp.GuideEditFits");
    public static string GuideShowTarget => Text("MotifApp.GuideShowTarget");
    public static string GuideLinkResolvedFormat => Text("MotifApp.GuideLinkResolvedFormat");
    public static string GuideLinkStaleSuffix => Text("MotifApp.GuideLinkStaleSuffix");
    public static string GuideLinkBrokenFormat => Text("MotifApp.GuideLinkBrokenFormat");
    public static string GuideMarkdownSave => Text("MotifApp.GuideMarkdownSave");
    public static string GuideMarkdownBack => Text("MotifApp.GuideMarkdownBack");
    public static string GuideMarkdownThrowAway => Text("MotifApp.GuideMarkdownThrowAway");
    public static string GuideMarkdownReload => Text("MotifApp.GuideMarkdownReload");
    public static string GuideMarkdownCopy => Text("MotifApp.GuideMarkdownCopy");
    public static string GuideEditingAsMarkdown => Text("MotifApp.GuideEditingAsMarkdown");
    public static string GuideMarkdownWait => Text("MotifApp.GuideMarkdownWait");
    public static string GuideMarkdownSaveFirstLeaving => Text("MotifApp.GuideMarkdownSaveFirstLeaving");
    public static string GuideMarkdownSaveFirstClosing => Text("MotifApp.GuideMarkdownSaveFirstClosing");
    public static string GuideMarkdownSavedFormat => Text("MotifApp.GuideMarkdownSavedFormat");
    public static string GuideMarkdownUndone => Text("MotifApp.GuideMarkdownUndone");
    public static string GuideMarkdownReloadOrCopy => Text("MotifApp.GuideMarkdownReloadOrCopy");
    public static string GuideNoProject => Text("MotifApp.GuideNoProject");
    // round2-guide
    public static string GuideListOldName => Text("MotifApp.GuideListOldName");
    public static string GuideListAmbiguous => Text("MotifApp.GuideListAmbiguous");
    public static string GuideNoRecords => Text("MotifApp.GuideNoRecords");
    public static string GuideNoBaseline => Text("MotifApp.GuideNoBaseline");
    public static string GuideReadFailedFormat => Text("MotifApp.GuideReadFailedFormat");
    public static string GuideOpenFailed => Text("MotifApp.GuideOpenFailed");
    public static string GuideInTheGrammar => Text("MotifApp.GuideInTheGrammar");
    public static string GuideLinkMissing => Text("MotifApp.GuideLinkMissing");
    public static string GuideLinkOutside => Text("MotifApp.GuideLinkOutside");
    public static string GuideOpenInFieldWorks => Text("MotifApp.GuideOpenInFieldWorks");
    public static string GuideOpenNameFormat => Text("MotifApp.GuideOpenNameFormat");
    public static string GuideOnThisPage => Text("MotifApp.GuideOnThisPage");
    public static string GuideSectionsName => Text("MotifApp.GuideSectionsName");
    public static string GuideExamplesHeading => Text("MotifApp.GuideExamplesHeading");
    public static string GuideStaleNotice => Text("MotifApp.GuideStaleNotice");
    public static string GuideDiscussionName => Text("MotifApp.GuideDiscussionName");
    #region fix1-progress
    public static string ProgressNothingParsedHeading => Text("MotifApp.ProgressNothingParsedHeading");
    public static string ProgressNothingParsedNext => Text("MotifApp.ProgressNothingParsedNext");
    public static string ProgressChooseTexts => Text("MotifApp.ProgressChooseTexts");
    public static string ProgressParsedHeading => Text("MotifApp.ProgressParsedHeading");
    public static string ProgressParsedPlacesFormat => Text("MotifApp.ProgressParsedPlacesFormat");
    public static string ProgressParsedWordsFormat => Text("MotifApp.ProgressParsedWordsFormat");
    public static string ProgressNotMeasuredPattern => Text("MotifApp.ProgressNotMeasuredPattern");
    public static string ProgressLastParsedFormat => Text("MotifApp.ProgressLastParsedFormat");
    public static string ProgressParseIsNotApproval => Text("MotifApp.ProgressParseIsNotApproval");
    public static string ProgressReviewWords => Text("MotifApp.ProgressReviewWords");
    public static string ProgressReadTexts => Text("MotifApp.ProgressReadTexts");
    public static string ProgressParseUnavailable => Text("MotifApp.ProgressParseUnavailable");
    #endregion
    // fix1-round1c
    public static string ProgressReadingParse => Text("MotifApp.ProgressReadingParse");
    public static string ProposalEvidenceNotMadeYet => Text("MotifApp.ProposalEvidenceNotMadeYet");
    // fix1-word
    public static string WordsHeadingParserSteps => Text("MotifApp.WordsHeadingParserSteps");
    public static string WordsHeadingProposalParserSteps => Text("MotifApp.WordsHeadingProposalParserSteps");
    public static string OccurrencesReadingCountPattern => Text("MotifApp.OccurrencesReadingCountPattern");
    public static string OccurrencesSingleReadingNote => Text("MotifApp.OccurrencesSingleReadingNote");
    // fix1-diag
    public static string WarningsListSeparator => Text("MotifApp.WarningsListSeparator");
    public static string DiagnosticsFindingsSummaryFormat => Text("MotifApp.DiagnosticsFindingsSummaryFormat");
    public static string WhyThisAnalysisRuleFormat => Text("MotifApp.WhyThisAnalysisRuleFormat");
    // fix1-proposal
    public static string ProposalUntitledDraft => Text("MotifApp.ProposalUntitledDraft");
    public static string ProposalUntitled => Text("MotifApp.ProposalUntitled");
    public static string ProposalApplyNeedsTrialFormat => Text("MotifApp.ProposalApplyNeedsTrialFormat");
    public static string ProposalApplyLosesFormat => Text("MotifApp.ProposalApplyLosesFormat");
    public static string ProposalApplyLosesNone => Text("MotifApp.ProposalApplyLosesNone");
    public static string ProposalEvidenceUnavailableNextStep => Text("MotifApp.ProposalEvidenceUnavailableNextStep");
    public static string ApplyBackupNotice => Text("MotifApp.ApplyBackupNotice");
    public static string ApplyBackupLink => Text("MotifApp.ApplyBackupLink");
    // fix1-layout
    public static string RefusalStoreLockMissing => Text("MotifApp.RefusalStoreLockMissing");
    public static string ProgressRefreshToUpgrade => Text("MotifApp.ProgressRefreshToUpgrade");
    public static string ProgressChosenTextsPattern => Text("MotifApp.ProgressChosenTextsPattern");
    public static string ProgressChosenWordsReady => Text("MotifApp.ProgressChosenWordsReady");
    public static string ProgressNextStoreProblem => Text("MotifApp.ProgressNextStoreProblem");
    // round2-diag
    public static string DiagnosticKindConversionSourceProvenanceUnknown => Text("MotifApp.DiagnosticKindConversionSourceProvenanceUnknown");
    public static string DiagnosticKindConversionUnsegmentableForm => Text("MotifApp.DiagnosticKindConversionUnsegmentableForm");
    public static string DiagnosticKindFstCircumfixNoRoute => Text("MotifApp.DiagnosticKindFstCircumfixNoRoute");
    public static string DiagnosticKindFstPatternRootShape => Text("MotifApp.DiagnosticKindFstPatternRootShape");
    public static string DiagnosticKindFstProcessNotEmittable => Text("MotifApp.DiagnosticKindFstProcessNotEmittable");
    public static string DiagnosticKindFstReduplicationNoRoute => Text("MotifApp.DiagnosticKindFstReduplicationNoRoute");
    public static string DiagnosticKindFstShapeNotInSurfaceTable => Text("MotifApp.DiagnosticKindFstShapeNotInSurfaceTable");
    public static string DiagnosticKindFwdataDanglingReference => Text("MotifApp.DiagnosticKindFwdataDanglingReference");
    public static string DiagnosticKindFwdataEmptyRepresentation => Text("MotifApp.DiagnosticKindFwdataEmptyRepresentation");
    public static string DiagnosticKindFwdataInvalidParserParameter => Text("MotifApp.DiagnosticKindFwdataInvalidParserParameter");
    public static string DiagnosticKindFwdataMissingLangProject => Text("MotifApp.DiagnosticKindFwdataMissingLangProject");
    public static string DiagnosticKindFwdataMissingRequiredField => Text("MotifApp.DiagnosticKindFwdataMissingRequiredField");
    public static string DiagnosticKindFwdataNoUsableAllomorphs => Text("MotifApp.DiagnosticKindFwdataNoUsableAllomorphs");
    public static string DiagnosticKindFwdataOnlyFirstUsed => Text("MotifApp.DiagnosticKindFwdataOnlyFirstUsed");
    public static string DiagnosticKindFwdataReferenceNotInScope => Text("MotifApp.DiagnosticKindFwdataReferenceNotInScope");
    public static string DiagnosticKindFwdataStaleAdhocProhibition => Text("MotifApp.DiagnosticKindFwdataStaleAdhocProhibition");
    public static string DiagnosticKindFwdataUnexpectedClass => Text("MotifApp.DiagnosticKindFwdataUnexpectedClass");
    public static string DiagnosticKindFwdataUnknownMorphTypeGuid => Text("MotifApp.DiagnosticKindFwdataUnknownMorphTypeGuid");
    public static string DiagnosticKindFwdataUnrecognizedEnumValue => Text("MotifApp.DiagnosticKindFwdataUnrecognizedEnumValue");
    public static string DiagnosticKindFwdataWritingSystemStoreUnreadable => Text("MotifApp.DiagnosticKindFwdataWritingSystemStoreUnreadable");
    public static string DiagnosticKindGrammarAdhocProhibitionUnresolved => Text("MotifApp.DiagnosticKindGrammarAdhocProhibitionUnresolved");
    public static string DiagnosticKindGrammarAllomorphEnvironmentBuildFailed => Text("MotifApp.DiagnosticKindGrammarAllomorphEnvironmentBuildFailed");
    public static string DiagnosticKindGrammarAllomorphFeatureBuildFailed => Text("MotifApp.DiagnosticKindGrammarAllomorphFeatureBuildFailed");
    public static string DiagnosticKindGrammarAllomorphInflectionClassUnresolved => Text("MotifApp.DiagnosticKindGrammarAllomorphInflectionClassUnresolved");
    public static string DiagnosticKindGrammarAllomorphMorphTypeUnsupportedAsRuleForm => Text("MotifApp.DiagnosticKindGrammarAllomorphMorphTypeUnsupportedAsRuleForm");
    public static string DiagnosticKindGrammarAllomorphMorphTypeUnsupported => Text("MotifApp.DiagnosticKindGrammarAllomorphMorphTypeUnsupported");
    public static string DiagnosticKindGrammarAllomorphNotARuleForm => Text("MotifApp.DiagnosticKindGrammarAllomorphNotARuleForm");
    public static string DiagnosticKindGrammarAllomorphProcessBuildFailed => Text("MotifApp.DiagnosticKindGrammarAllomorphProcessBuildFailed");
    public static string DiagnosticKindGrammarAllomorphReduplicationInvalid => Text("MotifApp.DiagnosticKindGrammarAllomorphReduplicationInvalid");
    public static string DiagnosticKindGrammarAllomorphUnsegmentable => Text("MotifApp.DiagnosticKindGrammarAllomorphUnsegmentable");
    public static string DiagnosticKindGrammarBoundaryMorphMarkerUnresolved => Text("MotifApp.DiagnosticKindGrammarBoundaryMorphMarkerUnresolved");
    public static string DiagnosticKindGrammarBoundaryNfdCollision => Text("MotifApp.DiagnosticKindGrammarBoundaryNfdCollision");
    public static string DiagnosticKindGrammarBoundaryNoRepresentation => Text("MotifApp.DiagnosticKindGrammarBoundaryNoRepresentation");
    public static string DiagnosticKindGrammarCircumfixEnvironmentCombinationSkipped => Text("MotifApp.DiagnosticKindGrammarCircumfixEnvironmentCombinationSkipped");
    public static string DiagnosticKindGrammarCircumfixMissingHalf => Text("MotifApp.DiagnosticKindGrammarCircumfixMissingHalf");
    public static string DiagnosticKindGrammarCompileFailed => Text("MotifApp.DiagnosticKindGrammarCompileFailed");
    public static string DiagnosticKindGrammarCompoundRuleBuildFailed => Text("MotifApp.DiagnosticKindGrammarCompoundRuleBuildFailed");
    public static string DiagnosticKindGrammarCompoundRuleSideExceptionFeatureUnresolved => Text("MotifApp.DiagnosticKindGrammarCompoundRuleSideExceptionFeatureUnresolved");
    public static string DiagnosticKindGrammarCompoundRuleSidePosUnresolved => Text("MotifApp.DiagnosticKindGrammarCompoundRuleSidePosUnresolved");
    public static string DiagnosticKindGrammarCooccurrenceTargetUnreachable => Text("MotifApp.DiagnosticKindGrammarCooccurrenceTargetUnreachable");
    public static string DiagnosticKindGrammarEnvironmentInvalid => Text("MotifApp.DiagnosticKindGrammarEnvironmentInvalid");
    public static string DiagnosticKindGrammarEnvironmentMissingNaturalClass => Text("MotifApp.DiagnosticKindGrammarEnvironmentMissingNaturalClass");
    public static string DiagnosticKindGrammarEnvironmentUnresolved => Text("MotifApp.DiagnosticKindGrammarEnvironmentUnresolved");
    public static string DiagnosticKindGrammarFeatureNegatedValueUnsupported => Text("MotifApp.DiagnosticKindGrammarFeatureNegatedValueUnsupported");
    public static string DiagnosticKindGrammarFeaturePhonComplexUnsupported => Text("MotifApp.DiagnosticKindGrammarFeaturePhonComplexUnsupported");
    public static string DiagnosticKindGrammarLimitFeatureValues => Text("MotifApp.DiagnosticKindGrammarLimitFeatureValues");
    public static string DiagnosticKindGrammarLimitMprFeatures => Text("MotifApp.DiagnosticKindGrammarLimitMprFeatures");
    public static string DiagnosticKindGrammarLimitPartsOfSpeech => Text("MotifApp.DiagnosticKindGrammarLimitPartsOfSpeech");
    public static string DiagnosticKindGrammarMruleUnreachableCompacted => Text("MotifApp.DiagnosticKindGrammarMruleUnreachableCompacted");
    public static string DiagnosticKindGrammarMsaBuildFailed => Text("MotifApp.DiagnosticKindGrammarMsaBuildFailed");
    public static string DiagnosticKindGrammarMsaExceptionFeatureUnresolved => Text("MotifApp.DiagnosticKindGrammarMsaExceptionFeatureUnresolved");
    public static string DiagnosticKindGrammarMsaInflectionClassUnresolved => Text("MotifApp.DiagnosticKindGrammarMsaInflectionClassUnresolved");
    public static string DiagnosticKindGrammarMsaLexEntryInflTypeUnresolved => Text("MotifApp.DiagnosticKindGrammarMsaLexEntryInflTypeUnresolved");
    public static string DiagnosticKindGrammarMsaNoAllomorphs => Text("MotifApp.DiagnosticKindGrammarMsaNoAllomorphs");
    public static string DiagnosticKindGrammarMsaNoRuleFormAllomorphs => Text("MotifApp.DiagnosticKindGrammarMsaNoRuleFormAllomorphs");
    public static string DiagnosticKindGrammarMsaStemNameUnresolved => Text("MotifApp.DiagnosticKindGrammarMsaStemNameUnresolved");
    public static string DiagnosticKindGrammarNatclassComplexFeatureUnsupported => Text("MotifApp.DiagnosticKindGrammarNatclassComplexFeatureUnsupported");
    public static string DiagnosticKindGrammarNatclassFeatureConstraintUnresolved => Text("MotifApp.DiagnosticKindGrammarNatclassFeatureConstraintUnresolved");
    public static string DiagnosticKindGrammarNatclassSegmentsMemberUnresolved => Text("MotifApp.DiagnosticKindGrammarNatclassSegmentsMemberUnresolved");
    public static string DiagnosticKindGrammarNatclassUnreferencedCompacted => Text("MotifApp.DiagnosticKindGrammarNatclassUnreferencedCompacted");
    public static string DiagnosticKindGrammarNullAffixMprUnresolved => Text("MotifApp.DiagnosticKindGrammarNullAffixMprUnresolved");
    public static string DiagnosticKindGrammarNullAffixSegmentFailed => Text("MotifApp.DiagnosticKindGrammarNullAffixSegmentFailed");
    public static string DiagnosticKindGrammarNullAffixSynFsFailed => Text("MotifApp.DiagnosticKindGrammarNullAffixSynFsFailed");
    public static string DiagnosticKindGrammarPhonemeComplexFeatureUnsupported => Text("MotifApp.DiagnosticKindGrammarPhonemeComplexFeatureUnsupported");
    public static string DiagnosticKindGrammarPhonemeFeatureUnresolved => Text("MotifApp.DiagnosticKindGrammarPhonemeFeatureUnresolved");
    public static string DiagnosticKindGrammarPhonemeNfdCollision => Text("MotifApp.DiagnosticKindGrammarPhonemeNfdCollision");
    public static string DiagnosticKindGrammarPhonemeNoRepresentation => Text("MotifApp.DiagnosticKindGrammarPhonemeNoRepresentation");
    public static string DiagnosticKindGrammarRuleBuildFailed => Text("MotifApp.DiagnosticKindGrammarRuleBuildFailed");
    public static string DiagnosticKindGrammarRuleFeatureConstraintPhonFeatureUnresolved => Text("MotifApp.DiagnosticKindGrammarRuleFeatureConstraintPhonFeatureUnresolved");
    public static string DiagnosticKindGrammarRuleFeatureConstraintUnresolved => Text("MotifApp.DiagnosticKindGrammarRuleFeatureConstraintUnresolved");
    public static string DiagnosticKindGrammarRuleRuleFeatureUnresolved => Text("MotifApp.DiagnosticKindGrammarRuleRuleFeatureUnresolved");
    public static string DiagnosticKindGrammarStemNameBuildFailed => Text("MotifApp.DiagnosticKindGrammarStemNameBuildFailed");
    public static string DiagnosticKindGrammarStemNameEmptyRegions => Text("MotifApp.DiagnosticKindGrammarStemNameEmptyRegions");
    public static string DiagnosticKindGrammarStoredAnalysisBudgetReached => Text("MotifApp.DiagnosticKindGrammarStoredAnalysisBudgetReached");
    public static string DiagnosticKindGrammarStoredAnalysisIncomplete => Text("MotifApp.DiagnosticKindGrammarStoredAnalysisIncomplete");
    public static string DiagnosticKindGrammarStoredAnalysisNoLongerParses => Text("MotifApp.DiagnosticKindGrammarStoredAnalysisNoLongerParses");
    public static string DiagnosticKindGrammarStrataUnknownRule => Text("MotifApp.DiagnosticKindGrammarStrataUnknownRule");
    public static string DiagnosticKindGrammarTemplateBuildFailed => Text("MotifApp.DiagnosticKindGrammarTemplateBuildFailed");
    public static string DiagnosticKindGrammarTemplateNoSlots => Text("MotifApp.DiagnosticKindGrammarTemplateNoSlots");
    public static string DiagnosticKindGrammarTemplateSlotNoRules => Text("MotifApp.DiagnosticKindGrammarTemplateSlotNoRules");
    public static string DiagnosticKindGrammarTemplateSlotUnresolved => Text("MotifApp.DiagnosticKindGrammarTemplateSlotUnresolved");
    public static string DiagnosticKindGrammarVariantComponentUnresolved => Text("MotifApp.DiagnosticKindGrammarVariantComponentUnresolved");
    public static string DiagnosticKindHcDuplicateFeatureBundle => Text("MotifApp.DiagnosticKindHcDuplicateFeatureBundle");
    public static string DiagnosticKindHcInflectionalAffixMissingTemplateSlot => Text("MotifApp.DiagnosticKindHcInflectionalAffixMissingTemplateSlot");
    public static string DiagnosticKindHcPartialReasonUnspecified => Text("MotifApp.DiagnosticKindHcPartialReasonUnspecified");
    public static string DiagnosticKindHcStemNoGrammaticalCategory => Text("MotifApp.DiagnosticKindHcStemNoGrammaticalCategory");
    public static string DiagnosticKindHcUnclassifiedAffix => Text("MotifApp.DiagnosticKindHcUnclassifiedAffix");
    public static string DiagnosticKindHcUndeclaredSegment => Text("MotifApp.DiagnosticKindHcUndeclaredSegment");
    public static string DiagnosticKindInvalidSourceActiveParser => Text("MotifApp.DiagnosticKindInvalidSourceActiveParser");
    public static string DiagnosticKindInvalidSourceDuplicateGuid => Text("MotifApp.DiagnosticKindInvalidSourceDuplicateGuid");
    public static string DiagnosticKindInvalidSourceMissingGuid => Text("MotifApp.DiagnosticKindInvalidSourceMissingGuid");
    public static string DiagnosticKindMigrationInferredSegmentWithFeatureRule => Text("MotifApp.DiagnosticKindMigrationInferredSegmentWithFeatureRule");
    public static string DiagnosticKindProvisionalBoundary => Text("MotifApp.DiagnosticKindProvisionalBoundary");
    public static string DiagnosticKindProvisionalLetter => Text("MotifApp.DiagnosticKindProvisionalLetter");
    public static string DiagnosticKindProvisionalPhonemeFeatures => Text("MotifApp.DiagnosticKindProvisionalPhonemeFeatures");
    public static string DiagnosticKindSnapshotDanglingReference => Text("MotifApp.DiagnosticKindSnapshotDanglingReference");
    public static string DiagnosticKindSnapshotFeatureStructureUnresolved => Text("MotifApp.DiagnosticKindSnapshotFeatureStructureUnresolved");
    public static string DiagnosticKindSnapshotReferenceOutOfScope => Text("MotifApp.DiagnosticKindSnapshotReferenceOutOfScope");
    public static string DiagnosticKindSnapshotRuleFeatureUnresolved => Text("MotifApp.DiagnosticKindSnapshotRuleFeatureUnresolved");
    public static string DiagnosticKindSubstrateClassificationAmbiguous => Text("MotifApp.DiagnosticKindSubstrateClassificationAmbiguous");
    public static string DiagnosticKindSubstratePositionUnmapped => Text("MotifApp.DiagnosticKindSubstratePositionUnmapped");
    public static string HealthBreakdownFormat => Text("MotifApp.HealthBreakdownFormat");
    public static string HealthBarFormat => Text("MotifApp.HealthBarFormat");
    public static string DiagnosticsRuleCodeTipFormat => Text("MotifApp.DiagnosticsRuleCodeTipFormat");

    /// <summary>The readable heading for a Diagnostics rule code, or null when the app has no name for it.</summary>
    public static string? DiagnosticKindName(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var key = "MotifApp.DiagnosticKind" + string.Concat(code.Split('.', '-')
            .Where(part => part.Length > 0).Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
        return Resources.GetString(key);
    }
    // round2-word
    public static string OccurrencesApproveChooseReading => Text("MotifApp.OccurrencesApproveChooseReading");
    public static string OccurrencesApproveCheckPlace => Text("MotifApp.OccurrencesApproveCheckPlace");
    public static string OccurrencesApproveNothing => Text("MotifApp.OccurrencesApproveNothing");
    public static string WordReadingsButtonName => Text("MotifApp.WordReadingsButtonName");
    public static string WordReadingsTipPattern => Text("MotifApp.WordReadingsTipPattern");
    // round2-proposal
    public static string ProposalStatusDraft => Text("MotifApp.ProposalStatusDraft");
    public static string ProposalStatusReady => Text("MotifApp.ProposalStatusReady");
    public static string ProposalApplyNeedsDifference => Text("MotifApp.ProposalApplyNeedsDifference");
    public static string ProposalApplyDifferenceUnavailable => Text("MotifApp.ProposalApplyDifferenceUnavailable");
    // round2-deferred
    public static string ProposalLostApprovedTitle => Text("MotifApp.ProposalLostApprovedTitle");
    public static string ProposalLostApprovedCountPattern => Text("MotifApp.ProposalLostApprovedCountPattern");
    public static string ProposalLostApprovedMorePattern => Text("MotifApp.ProposalLostApprovedMorePattern");
    public static string ProposalLostApprovedPlaceFormat => Text("MotifApp.ProposalLostApprovedPlaceFormat");
    public static string ProposalsFilterChipFormat => Text("MotifApp.ProposalsFilterChipFormat");
    public static string ProposalsDeferredEmpty => Text("MotifApp.ProposalsDeferredEmpty");
    #endregion
}

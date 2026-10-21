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

    public static string ProjectMenuAccessibleName => Text("MotifApp.ProjectMenuAccessibleName");

    public static string SelectNew => Text("MotifApp.SelectNew");

    public static string SelectNewDetail => Text("MotifApp.SelectNewDetail");

    public static string SelectNewAccessibleName => Text("MotifApp.SelectNewAccessibleName");

    public static string OpenRecent => Text("MotifApp.OpenRecent");

    public static string OpenRecentAccessibleName => Text("MotifApp.OpenRecentAccessibleName");

    public static string Configure => Text("MotifApp.Configure");

    public static string ConfigureAccessibleName => Text("MotifApp.ConfigureAccessibleName");

    public static string WritingSystemsAndFonts => Text("MotifApp.WritingSystemsAndFonts");

    public static string WritingSystemsAndFontsDetail => Text("MotifApp.WritingSystemsAndFontsDetail");

    public static string WritingSystemsAndFontsAccessibleName => Text("MotifApp.WritingSystemsAndFontsAccessibleName");

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
}

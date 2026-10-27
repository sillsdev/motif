using System.Globalization;
using System.Reflection;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Generator;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins ADR 0072 for the Overview speed sentences and the Try a Word trace summaries: the English lives in
/// MotifAppStrings.resx, count sentences are ICU plural patterns, and the two view models build no English of their own.
/// </summary>
public sealed class OverviewTraceSentenceResourceTests
{
    public static TheoryData<string, string> Entries => new()
    {
        { "OverviewSpeedWordsPattern", "{0, plural, one {# word} other {# words}}" },
        { "OverviewSpeedMainFormat", "{0} · {1} total word time" },
        { "OverviewSpeedNoTimes", "No parse times recorded" },
        { "OverviewSpeedMedianFormat", "median {0} a word" },
        { "OverviewSpeedMedianWithPercentileFormat", "median {0} a word · 95th percentile {1}" },
        { "OverviewSpeedStoppedPattern", "{0, plural, one {# stopped at a search limit} other {# stopped at a search limit}}" },
        { "OverviewSlowestSummaryFormat", "Slowest: {0}" },
        { "OverviewSlowestWordFormat", "{0} · {1}" },
        { "OverviewSlowestStoppedWordFormat", "{0} Stopped · {1}" },
        { "OverviewTimingOverrunFormat", "Object timers exceed total word time by {0}." },
        { "OverviewSeeWordsPattern", "{0, plural, one {See the # word} other {See the # words}}" },
        { "OverviewStoppedWordsRowPattern", "{0, plural, one {# word stopped at a search limit.} other {# words stopped at a search limit.}}" },
        { "OverviewStoppedWordsDetailFormat", "{0} of {1} total word time" },
        { "TraceParsed", "Parsed" },
        { "TraceSearchIncomplete", "Search incomplete" },
        { "TraceAnalysesPattern", "{0, plural, one {# analysis} other {# analyses}}" },
        { "TraceSourceRecordsPattern", "{0, plural, one {# source record} other {# source records}}" },
        { "TraceAnalysesWithRecordsFormat", "{0}, {1}" },
        { "TraceParsedAnalysesFormat", "Parsed: {0}" },
        { "TraceAnalysisAttemptsPattern", "{0, plural, one {# analysis attempt} other {# analysis attempts}}" },
        { "TraceGuessed", "guessed" },
        { "TraceOverallFormat", "{0} overall" },
        { "TraceOverallNotRecorded", "overall time not recorded" },
        { "TraceInParserFormat", "{0} in the parser, {1}" },
        { "TraceParserTimeFormat", "Parser time · {0}" },
        { "TraceDroppedPathsPattern", "{0, plural, one {# other path the parser tried and dropped (normal)} other {# other paths the parser tried and dropped (normal)}}" },
        { "TraceAttemptsFailedPattern", "{0, plural, one {# attempt failed · choose a group to see its recorded outcomes} other {# attempts failed · choose a group to see its recorded outcomes}}" },
        { "TraceAttemptsPattern", "{0, plural, one {# attempt} other {# attempts}}" },
        { "TraceClosestAttemptsHeadingOne", "The attempt that got furthest" },
        { "TraceClosestAttemptsHeadingMany", "Attempts that got furthest" },
        { "TraceMoreAttemptsCountPattern", "{0, plural, one {attempt} other {# attempts}}" },
        { "TraceShowOtherFormat", "Show the other {0}" },
        { "TraceShowOtherRefusedByFormat", "Show the other {0} refused by {1}" },
        { "TraceSearchIncompleteFormat", "Search incomplete: {0}" },
        { "TraceReasonNotRecorded", "Reason not recorded" },
        { "TraceSearchComplete", "Search complete" },
        { "TraceSearchCompletionNotRecorded", "Search completion not recorded" },
        { "TraceStatusHiddenFormat", "{0}  ·  {1}" },
        { "TraceHiddenByFiltersPattern", "{0, plural, one {# hidden by filters} other {# hidden by filters}}" },
        { "TraceFilterFailed", "failed" },
        { "TraceFilterSucceeded", "succeeded" },
        { "TraceFilterRuleFormat", "rule: {0}" },
        { "TraceFilterMorphFormat", "morph: {0}" },
        { "TraceFilterAllSteps", "all recorded steps" },
        { "TraceStepsHiddenPattern", "{0, plural, one {# step hidden by filters} other {# steps hidden by filters}}" },
        { "TraceFilterHiddenSuffixPattern", "{0, plural, one {# hidden} other {# hidden}}" },
        { "TraceRecordedTimesPattern", "{0, plural, =2 {Recorded twice} other {Recorded # times}}" },
        { "OverviewApprovedDifferentEntryPattern", "{0, plural, one {# approved word is built from a different entry with the same form and gloss.} other {# approved words are built from a different entry with the same form and gloss.}}" },
        { "OverviewSeeLostWordsPattern", "{0, plural, one {See the word} other {See the # words}}" },
        { "OverviewApprovedLostPattern", "{0, plural, one {# approved word is Lost; the grammar builds nothing for it.} other {# approved words are Lost; the grammar builds nothing for them.}}" },
        { "OverviewSharedMorphemeUseFormat", "{0} use {1}" },
        { "OverviewSharedMorphemeNamedByWarning", " (named by a grammar warning)" },
        { "OverviewSharedMorphemesUnavailable", "Shared morpheme information is unavailable." },
        { "OverviewSharedMorphemesNone", "No shared morphemes were recorded." },
        { "OverviewUnknownDifferentPattern", "{0, plural, one {# Unknown word differs from what PanGloss builds.} other {# Unknown words differ from what PanGloss builds.}}" },
        { "OverviewLostWordsParseFirst", "Parse your texts to see which approved words are Lost." },
        { "OverviewLostWordsNone", "No approved word is Lost in this parse." },
        { "OverviewLostWordsSome", "Approved words the grammar no longer builds." },
        { "OverviewNoKnownProjects", "No recent projects on this computer." },
        { "OverviewSelectionNotResolved", "not resolved" },
        { "OverviewSelectionNotSetUp", "not set up" },
        { "OverviewNotMeasured", "Not measured" },
        { "OverviewNotAttributed", "Not attributed" },
        { "OverviewNotParsedYet", "Not parsed yet" },
        { "OverviewNotRecorded", "not recorded" },
        { "OverviewCoverageMainFormat", "{0} of {1} words parse" },
        { "OverviewCoverageWordsFormat", "{0} of the words in your Selection · {1}" },
        { "OverviewCoverageNoPlaces", "No places in the chosen Texts" },
        { "OverviewCoveragePlacesFormat", "{0} of their {1} places" },
        { "OverviewLabelSame", "same" },
        { "OverviewLabelDifferent", "different" },
        { "OverviewLabelNoParse", "no parse" },
        { "OverviewLabelStopped", "stopped" },
        { "OverviewLabelCantRead", "can't read" },
        { "OverviewOutcomeSame", "Same outcome" },
        { "OverviewOutcomeDifferent", "Different outcome" },
        { "OverviewOutcomeNoParse", "No parse" },
        { "OverviewOutcomeStopped", "Stopped" },
        { "OverviewOpenOutcomeWordsFormat", "Open {0} {1} words in the Matrix" },
        { "OverviewOpenApprovedWordsFormat", "Open {0} Approved {1} words in the Matrix" },
        { "OverviewAccuracyNothingApproved", "No analyses are approved yet, so there is nothing to rebuild" },
        { "OverviewAccuracyMainFormat", "{0} of {1} rebuilt exactly" },
        { "OverviewAccuracyBuiltElseUnavailableFormat", "{0} built something else; entry comparison unavailable" },
        { "OverviewAccuracySameFormsFormat", "{0} with the same forms and glosses but a different entry" },
        { "OverviewAccuracyBuiltDifferentlyFormat", "{0} built differently" },
        { "OverviewAccuracyLostFormat", "{0} Lost" },
        { "OverviewAccuracyStoppedFormat", "{0} Stopped" },
        { "OverviewAccuracyNotParsedFormat", "{0} Not parsed" },
        { "OverviewDisapprovedStillBuiltPattern", "{0, plural, one {# disapproved analysis still built} other {# disapproved analyses still built}}" },
        { "OverviewCandidatesConfirmedFormat", "PanGloss confirms {0} of {1} marked Unknown" },
        { "OverviewLabelKept", "kept" },
        { "OverviewLabelBuiltSomethingElse", "built something else" },
        { "OverviewLabelLost", "lost" },
        { "OverviewOutcomeKept", "Kept" },
        { "OverviewOutcomeBuiltSomethingElse", "Built something else" },
        { "OverviewOutcomeLost", "Lost" },
        { "OverviewOutcomeNoResult", "No result" },
        { "OverviewWarningsParseToSee", "Parse to see which of your words they touch" },
        { "OverviewWarningsNoConfirmedUse", "No confirmed word uses; possible matches were found" },
        { "OverviewWarningsNoWordUse", "None of your words use what a warning names" },
        { "OverviewWarningsWordsUseFormat", "{0} of your words use what a warning names" },
        { "OverviewSpellingOnlyMatchesPattern", "{0, plural, one {# spelling-only match; not confirmed uses} other {# spelling-only matches; not confirmed uses}}" },
        { "OverviewWarningErrorsPattern", "{0, plural, one {# error} other {# errors}}" },
        { "OverviewWarningWarningsPattern", "{0, plural, one {# warning} other {# warnings}}" },
        { "OverviewWarningInformationPattern", "{0, plural, one {# information finding} other {# information findings}}" },
        { "OverviewWarningsNotCheckedYet", "Not checked yet" },
        { "OverviewWarningsHeadlineOnly", "Warnings" },
        { "OverviewWarningsWithErrorsFormat", "{0} · {1}" },
        { "OverviewReadRefusalNumbers", "Motif could not read this project's numbers." },
        { "TraceGrammarSourceNotRecorded", "Grammar source not recorded" },
        { "TraceGrammarSourceBaselineFormat", "Grammar source: Baseline captured {0} · source saved {1:u} · {2}" },
        { "TraceGrammarSourceFormat", "Grammar source: {0}" },
        { "TraceProvenanceNotRecorded", "Compatibility with the current FieldWorks project is not recorded; live links are unavailable for this saved diagnostic." },
        { "TraceNoTerminalAttemptStopped", "No terminal attempt was recorded before the search stopped. Recorded progress is retained." },
        { "TraceNoTerminalAttempt", "No terminal attempt was recorded." },
        { "TraceWhyOtherAttemptsFailed", "Why the other attempts failed" },
        { "TraceWhyNotParsed", "Why it did not parse" },
        { "TraceWritingSystemsNotRecorded", "Writing systems: not recorded; using the UI font fallback." },
        { "TraceWritingSystemFormat", "{0} ({1}; font: {2})" },
        { "TraceDirectionNotRecorded", "direction not recorded" },
        { "TraceFontUiFallback", "UI fallback" },
        { "TraceWritingSystemsSummaryFormat", "Writing systems: {0}. Font fallback: Noto Sans, Segoe UI, Arial." },
        { "TraceLabelValueFormat", "{0}: {1}" },
        { "TraceCaptureFormatLabel", "Format" },
        { "TraceCaptureParserLabel", "Parser" },
        { "TraceCaptureParserVersionLabel", "Parser version" },
        { "TraceCaptureProfileLabel", "Trace profile" },
        { "TraceCaptureGrammarHashLabel", "Grammar hash" },
        { "TraceCaptureGrammarHashSemanticsLabel", "Grammar hash semantics" },
        { "TraceCaptureProjectIdentityLabel", "Project identity" },
        { "TraceCaptureBundleDigestLabel", "Bundle digest" },
        { "TraceBaselineCapturedFormat", "Baseline captured: {0}" },
        { "TraceSourceSavedFormat", "Source saved: {0:u}" },
        { "TraceBaselineSourceNotRecorded", "Baseline source not recorded" },
        { "TraceCapturedAtFormat", "Captured: {0:u}" },
        { "TraceHostElapsedFormat", "Host elapsed: {0} ms" },
        { "TraceCaptureDetailsNotRecorded", "Capture details not recorded." },
        { "TraceShowAnalyses", "Show Analyses" },
        { "TraceFocusThisWord", "Focus this word" },
        { "TraceSurfaceNotRecorded", "Surface not recorded" },
        { "TraceAvailabilityNotRecorded", "availability not recorded" },
        { "TraceMorphemesFormat", "Morphemes: {0}" },
        { "TraceParserMorphemesTipFormat", "The parser's morphemes: {0}" },
        { "TraceAnalysisNumberFormat", "Analysis {0}" },
        { "TraceRecordedAnalysis", "Recorded analysis" },
        { "TraceProjectionErrorText", "The parser found this analysis, but can't match its morphemes to FieldWorks entries." },
        { "TraceRawMorphNotRecorded", "Full raw morph details not recorded" },
        { "TraceValueNotRecorded", "not recorded" },
        { "TraceFieldNotRecordedFormat", "{0} not recorded" },
        { "TraceMorphFormLabel", "Form" },
        { "TraceMorphHeadwordLabel", "Headword" },
        { "TraceMorphGlossLabel", "Gloss" },
        { "TraceMorphCategoryLabel", "Category" },
        { "TraceMorphSlotLabel", "Slot" },
        { "TraceMorphInflectionClassLabel", "Inflection class" },
        { "TraceMorphFeaturesLabel", "Features" },
        { "TraceMorphGuessedStringLabel", "Guessed string" },
        { "TraceMorphGuessedLabel", "Guessed" },
        { "TraceMorphIdentityLabel", "Identity" },
        { "TraceMorphIdentityQualityLabel", "Identity quality" },
        { "TraceMorphFormIdLabel", "Form ID" },
        { "TraceMorphEntryIdLabel", "Entry ID" },
        { "TraceMorphMsaIdLabel", "Grammatical info ID" },
        { "TraceMorphInflTypeIdLabel", "Inflection type ID" },
        { "TraceMorphWritingSystemsLabel", "Writing systems" },
        { "TraceWsRoleForm", "form" },
        { "TraceWsRoleHeadword", "headword" },
        { "TraceWsRoleGloss", "gloss" },
        { "TraceWsRoleFormat", "{0}={1}" },
        { "TraceIdentityQualityUnknown", "unknown" },
        { "TraceOpenEntryFormat", "Open the entry for {0} in {1}" },
        { "TraceOpenHeadwordFormat", "Open {0} in {1}" },
        { "TraceMsaNotRecorded", "Grammatical info not recorded." },
        { "TraceMsaUnavailable", "Grammatical info unavailable; the raw morph record is kept below." },
        { "TraceMsaKindLabel", "Grammatical info" },
        { "TraceMsaFromCategoryLabel", "From category" },
        { "TraceMsaToCategoryLabel", "To category" },
        { "TraceMsaFromInflectionClassLabel", "From inflection class" },
        { "TraceMsaToInflectionClassLabel", "To inflection class" },
        { "TraceMsaFromFeaturesLabel", "From features" },
        { "TraceMsaToFeaturesLabel", "To features" },
        { "TraceMsaAttachesToLabel", "Attaches to" },
        { "TraceMsaSlotsLabel", "Slots" },
        { "TraceMsaOptionalFormat", " (optional: {0})" },
        { "TraceRecordedRefusal", "Recorded refusal" },
        { "TraceDidNotRecordWhy", "PanGloss didn't record why." },
        { "TraceSourceKindLower", "source" },
        { "TraceSourceKindTitle", "Source" },
        { "TraceQualityNotRecorded", "quality not recorded" },
        { "TraceSourceIdentityFormat", "{0}: {1} ({2})" },
        { "TraceSourceIdentityNotRecorded", "Source identity not recorded" },
        { "TraceRecordedAttempt", "Recorded attempt" },
        { "TraceRefusedByFormat", "Refused by {0}" },
        { "TraceFailureContextNotRecorded", "Failure context not recorded" },
        { "TraceStatusBuiltTheWord", "built the word" },
        { "TraceEffortDidNotApplyFormat", "{0} did not apply" },
        { "TraceEffortNoRootFormat", "{0} found no root" },
        { "TraceEffortSurfaceMismatchFormat", "{0} did not match the word" },
        { "TraceNotTimed", "not timed" },
        { "TraceBuildingStemFormFormat", "Stem form · {0}" },
        { "TraceBuildingStemUsedFormat", "Stem used · {0}" },
        { "TraceBuildingStemFoundFormat", "Stem found · {0}" },
        { "TraceBuildingAffix", "Affix" },
        { "TraceBuildingSoundRule", "Sound rule" },
        { "TraceProducerFormat", "Producer: {0}" },
        { "TraceCapturedFieldWorksFormat", "Captured FieldWorks: {0}" },
        { "TraceCapturedKindLabel", "Kind" },
        { "TraceCapturedProducerLabel", "Producer" },
        { "TraceCapturedFieldWorksLabel", "Captured FieldWorks" },
        { "TraceCapturedProducerEventIdLabel", "Producer event ID" },
        { "TraceCapturedTreeAddressLabel", "Tree address" },
        { "TraceCapturedSubruleLabel", "Subrule" },
        { "TraceCapturedInputLabel", "Input" },
        { "TraceCapturedOutputLabel", "Output" },
        { "TraceCapturedOutcomeLabel", "Outcome" },
        { "TraceCapturedEventLabel", "Event" },
        { "TraceCapturedReasonLabel", "Reason" },
        { "TraceCapturedExplanationLabel", "Explanation" },
        { "TraceCapturedEvaluatorDetailsLabel", "Evaluator details" },
        { "TraceOutcomeRefused", "Refused" },
        { "TraceOutcomeTried", "Tried" },
        { "TraceOutcomeApplied", "Applied" },
        { "TraceOutcomeNotRecorded", "Outcome not recorded" },
        { "TraceInputNotRecorded", "Input not recorded" },
        { "TraceInputShapeTip", "Recorded input shape, in the parser's direction." },
        { "TraceOutputNotRecorded", "Output not recorded" },
        { "TraceOutputShapeTip", "Recorded output shape. A recorded output alone does not prove application." },
        { "TraceRecordedEventFormat", "Recorded event: {0}" },
        { "TraceEventAddressNotRecorded", "Event address not recorded" },
        { "TraceProducerEventWithTreeFormat", "Producer event ID: {0} (tree address {1})" },
        { "TraceProducerEventNoTreeFormat", "Producer event ID: {0}; tree address not recorded" },
        { "TraceTreeAddressOnlyFormat", "Tree address: {0}; producer event ID not recorded" },
        { "TraceProducerEventNotRecorded", "Producer event ID not recorded" },
        { "TraceRejectionDetailsNotRecorded", "Rejection details not recorded" },
        { "TraceRejectionDetailsRecorded", "Rejection details recorded" },
        { "TraceRequiredFormat", "Required: {0}" },
        { "TraceActualFormat", "Actual: {0}" },
        { "TraceEnvironmentFormat", "Environment: {0}" },
        { "TraceEvidenceStatusFormat", "Evidence status: {0}" },
        { "TraceEvidenceKindFormat", "Evidence kind: {0}" },
        { "TraceEvidenceSourceFormat", "Evidence source: {0}" },
        { "TraceEvidenceReasonCodeFormat", "Evidence reason code: {0}" },
        { "TraceEvidenceUnavailableReasonFormat", "Evidence unavailable reason: {0}" },
        { "TraceEvidenceReasonFormat", "Evidence reason: {0}" },
        { "TraceEvidenceRequiredFormat", "Evidence required: {0}" },
        { "TraceEvidenceActualFormat", "Evidence actual: {0}" },
        { "TraceEvidenceEnvironmentFormat", "Evidence environment: {0}" },
        { "TraceSubruleFormat", "Subrule: {0}" },
        { "TraceContextualFailureNotRecorded", "Contextual failure not recorded" },
        { "TraceStepApplied", "applied" },
    };

    [Theory]
    [MemberData(nameof(Entries))]
    public void EachSentenceIsAResxEntryWithItsEnglishValue(string accessor, string expected)
    {
        var property = typeof(MotifAppStrings).GetProperty(accessor, BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(property);
        Assert.Equal(expected, (string?)property.GetValue(null));
    }

    [Theory]
    [InlineData("TraceAnalysesPattern", 0, "0 analyses")]
    [InlineData("TraceAnalysesPattern", 1, "1 analysis")]
    [InlineData("TraceAnalysesPattern", 2, "2 analyses")]
    [InlineData("TraceAnalysesPattern", 1000, "1,000 analyses")]
    [InlineData("TraceMoreAttemptsCountPattern", 1, "attempt")]
    [InlineData("TraceMoreAttemptsCountPattern", 3, "3 attempts")]
    [InlineData("TraceRecordedTimesPattern", 2, "Recorded twice")]
    [InlineData("TraceRecordedTimesPattern", 3, "Recorded 3 times")]
    [InlineData("OverviewSeeWordsPattern", 1, "See the 1 word")]
    [InlineData("OverviewStoppedWordsRowPattern", 3, "3 words stopped at a search limit.")]
    [InlineData("OverviewSpeedWordsPattern", 1, "1 word")]
    [InlineData("OverviewSpeedWordsPattern", 1318, "1,318 words")]
    [InlineData("OverviewSeeLostWordsPattern", 1, "See the word")]
    [InlineData("OverviewSeeLostWordsPattern", 4, "See the 4 words")]
    [InlineData("OverviewUnknownDifferentPattern", 1, "1 Unknown word differs from what PanGloss builds.")]
    [InlineData("OverviewUnknownDifferentPattern", 2, "2 Unknown words differ from what PanGloss builds.")]
    [InlineData("OverviewApprovedLostPattern", 1, "1 approved word is Lost; the grammar builds nothing for it.")]
    [InlineData("OverviewApprovedLostPattern", 2, "2 approved words are Lost; the grammar builds nothing for them.")]
    [InlineData("OverviewApprovedDifferentEntryPattern", 1, "1 approved word is built from a different entry with the same form and gloss.")]
    [InlineData("OverviewApprovedDifferentEntryPattern", 1500, "1,500 approved words are built from a different entry with the same form and gloss.")]
    [InlineData("OverviewDisapprovedStillBuiltPattern", 0, "0 disapproved analyses still built")]
    [InlineData("OverviewDisapprovedStillBuiltPattern", 1, "1 disapproved analysis still built")]
    [InlineData("OverviewSpellingOnlyMatchesPattern", 1, "1 spelling-only match; not confirmed uses")]
    [InlineData("OverviewSpellingOnlyMatchesPattern", 3, "3 spelling-only matches; not confirmed uses")]
    [InlineData("OverviewWarningErrorsPattern", 1, "1 error")]
    [InlineData("OverviewWarningWarningsPattern", 2, "2 warnings")]
    [InlineData("OverviewWarningInformationPattern", 1, "1 information finding")]
    [InlineData("OverviewWarningInformationPattern", 5, "5 information findings")]
    public void CountSentencesGoThroughThePluralHelper(string accessor, int count, string expected)
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var pattern = (string)typeof(MotifAppStrings).GetProperty(accessor)!.GetValue(null)!;
        Assert.True(PluralText.IsPattern(pattern));
        PluralText.Validate(pattern);
        Assert.Equal(expected, PluralText.Format(pattern, count));
    }

    [Fact]
    public void CountsReadWithTheCurrentCulturesGroupingUnderGerman()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("de-DE"));
        var pattern = (string)typeof(MotifAppStrings).GetProperty("OverviewSpeedWordsPattern")!.GetValue(null)!;
        Assert.Equal("1.318 words", PluralText.Format(pattern, 1318));
    }

    [Theory]
    [InlineData("OverviewPageModel.cs")]
    [InlineData("TraceWordViewModel.cs")]
    public void TheViewModelBuildsNoEnglishSpeedOrTraceSentenceOfItsOwn(string file)
    {
        var path = Path.Combine(RepoPaths.FindRepoRoot(), "src", "SIL.Motif.App", "ViewModels", file);
        var code = string.Join('\n', File.ReadAllLines(path).Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));
        string[] fragments =
        [
            "total word time", "stopped at a search limit", "Slowest: ", "Object timers exceed", "\"median ",
            "No parse times recorded", "analysis attempt", "tried and dropped", "choose a group to see",
            "Show the other", "Search incomplete", "Search complete", "hidden by filters", "Recorded twice",
            "Parser time", "} overall", "overall time not recorded", "got furthest", "source records",
            "\"Parsed", "\"guessed\"", "\"1 attempt\"", "? \"failed\" : \"succeeded\"", "all recorded steps",
        ];
        Assert.Empty(fragments.Where(fragment => code.Contains(fragment, StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("TraceWordViewModel.cs")]
    public void TheTraceViewModelBuildsNoEnglishDetailTextOfItsOwn(string file)
    {
        var path = Path.Combine(RepoPaths.FindRepoRoot(), "src", "SIL.Motif.App", "ViewModels", file);
        var code = string.Join('\n', File.ReadAllLines(path)
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal) && !line.TrimStart().StartsWith("///", StringComparison.Ordinal)));
        string[] fragments =
        [
            "\"Grammar source",
            "Compatibility with the current",
            "No terminal attempt",
            "Why the other attempts",
            "Why it did not parse",
            "Writing systems: ",
            "UI fallback",
            "direction not recorded",
            "Baseline captured",
            "Source saved",
            "Host elapsed",
            "Capture details not recorded",
            "\"Show Analyses\"",
            "\"Focus this word\"",
            "Surface not recorded",
            "availability not recorded",
            "\"Morphemes: ",
            "The parser's morphemes",
            "Recorded analysis",
            "can't match its morphemes",
            "Full raw morph details",
            "not recorded\"",
            "Grammatical info",
            "Open the entry for",
            "\"Recorded refusal\"",
            "didn't record why",
            "\"Recorded attempt\"",
            "Refused by",
            "\"Built the word\"",
            "\"No analysis found\"",
            "\"built the word\"",
            "did not apply",
            "found no root",
            "did not match the word",
            "\"not timed\"",
            "\"Stem form",
            "\"Stem used",
            "\"Stem found",
            "\"Sound rule\"",
            "\"Affix\"",
            "\"Producer: ",
            "Captured FieldWorks: ",
            "Producer event ID",
            "Tree address",
            "\"Refused\"",
            "\"Applied\"",
            "\"Tried\"",
            "\"refused\"",
            "\"applied\"",
            "\"tried\"",
            "Recorded input shape",
            "Recorded output shape",
            "Recorded event: ",
            "Rejection details",
            "\"Required: ",
            "\"Actual: ",
            "\"Environment: ",
            "\"Evidence ",
            "\"Subrule: ",
            "Contextual failure not recorded",
            "Failure context not recorded",
            "Reason not recorded",
            "Evaluator details",
        ];
        Assert.Empty(fragments.Where(fragment => code.Contains(fragment, StringComparison.Ordinal)));
    }

    [Fact]
    public void OverviewPageModelBuildsNoEnglishLookFirstCaptionOrWarningSentenceOfItsOwn()
    {
        var path = Path.Combine(RepoPaths.FindRepoRoot(), "src", "SIL.Motif.App", "ViewModels", "OverviewPageModel.cs");
        var code = string.Join('\n', File.ReadAllLines(path).Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));
        string[] fragments =
        [
            "are Lost", "is Lost", "different entry", "differ \"", "Shared morpheme", "No shared morphemes",
            "Parse your texts", "No approved word", "no longer builds", "No recent projects", "\"not resolved\"",
            "\"not set up\"", "\"Not measured\"", "\"Not attributed\"", "\"Not parsed yet\"", "\"not recorded\"",
            "words parse", "rebuilt exactly", "built something else", "built differently", "nothing to rebuild",
            "PanGloss confirms", "still built", "Open {", "your words use", "Parse to see", "spelling-only",
            "\"Not checked yet\"", "\"Warnings\"", "could not read this project", "\"Same outcome\"", "\"Kept\"",
            "\"can't read\"", "of the words in your Selection", "No places in",
        ];
        Assert.Empty(fragments.Where(fragment => code.Contains(fragment, StringComparison.Ordinal)));
    }

    private static WordTraceResponse TraceWith(long? parserSteps, params TraceStep[] children) => new(
        "word", Parsed: false, Complete: true, StopReason: null, StepCount: 2, DeepestRule: null, ElapsedMs: 1,
        TraceReadingBuilder.Build("word", new TraceStep("WordSynthesis", "root", "in", "out", null, children), [], []))
    {
        ParserSteps = parserSteps,
    };

    [Fact]
    public void OneStepHiddenByAFilterReadsInTheSingularThroughTheViewModel()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var failed = new TraceStep("Failed", "rule-x", "in", "out", "blocked", []) { OutcomeStatus = "failed" };
        var succeeded = new TraceStep("Done", "rule-y", "in", "out", null, []) { OutcomeStatus = "succeeded" };
        var trace = new TraceWordViewModel { Result = TraceWith(null, failed, succeeded) };

        trace.OutcomeFilter = TraceOutcomeFilter.Failed;

        Assert.Equal(1, trace.HiddenStepCount);
        Assert.Equal("failed · 1 hidden", trace.SelectedFilterDescription);
        Assert.EndsWith("1 hidden by filters", trace.SearchStatusText, StringComparison.Ordinal);

        trace.OutcomeFilter = TraceOutcomeFilter.All;
        trace.RuleFilter = "no-such-rule";
        Assert.True(trace.HiddenStepCount > 0);
        Assert.StartsWith("rule: no-such-rule", trace.SelectedFilterDescription, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-5L, "0 analysis attempts")]
    [InlineData(1L, "1 analysis attempt")]
    [InlineData(long.MaxValue, "2,147,483,647 analysis attempts")]
    public void ParserStepsAreClampedIntoTheCountRangeOfThePluralHelper(long steps, string expected)
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var trace = new TraceWordViewModel { Result = TraceWith(steps) };

        Assert.Contains(expected, trace.SummaryText, StringComparison.Ordinal);
    }
}

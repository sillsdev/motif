using System.Globalization;
using System.Reflection;
using SIL.Motif.App;
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
}

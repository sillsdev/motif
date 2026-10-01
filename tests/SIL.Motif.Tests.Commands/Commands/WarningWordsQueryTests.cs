using System.Linq;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// Pins how a grammar finding reaches the Selection's words: by identity through the stored analyses that use what
/// it reaches, by identity through the stored per-word rule times, by spelling only for letters and labelled so, and
/// "can't tell" when nothing is named. The join shows use, never cause.
/// </summary>
public sealed class WarningWordsQueryTests
{
    private static ParserReadingMorph Morph(string form, string gloss, string id) =>
        new(form, gloss, "v", null, false, null) { AllomorphId = "form-" + id, GrammaticalInfoId = "msa-" + id };

    private static readonly ParserReadingMorph Wa = Morph("wa-", "3PL", "wa");
    private static readonly ParserReadingMorph Li = Morph("li-", "PST", "li");
    private static readonly ParserReadingMorph Kat = Morph("kat", "cut", "kat");
    private static readonly ParserReadingMorph KatSkin = Morph("kat", "skin", "kat-skin");
    private static readonly ParserReadingMorph Kul = Morph("kul", "eat", "kul");
    private static readonly ParserReadingMorph Fik = Morph("fik", "arrive", "fik");
    private static readonly ParserReadingMorph Fv = Morph("-a", "FV", "fv");

    private static AssessmentWordResult Word(string word, string outcome, params ParserReadingMorph[] morphs) =>
        new(word, outcome, false, "Search completed", 1, null)
        {
            ProjectStanding = ProjectStanding.Approved,
            StoredAnalyses = [new ParserReading(morphs) { StoredAnalysisId = "analysis-" + word, StoredAnalysisOpinion = ReadingGrade.Approved }],
            ReadingGrades = outcome == "analysed" ? [ReadingGrade.Approved] : [],
            Morphology = outcome == "analysed"
                ? new ParseWordEvidence("v1", 0, word, 1, false, false, false, [new ParseAnalysis([new ParseMorph("x", "y", null, null)])], [])
                : null,
            OccurrenceCount = 1,
        };

    private static readonly AssessmentWordResult[] Words =
    [
        Word("walikata", "no-analysis", Wa, Li, Kat, Fv),
        Word("walikula", "analysed", Wa, Li, Kul, Fv),
        Word("anakata", "no-analysis", Kat, Fv),
        Word("ngozi", "analysed", KatSkin),
        Word("wamefika", "analysed", Wa, Fik, Fv),
    ];

    private static AssessmentObjectTiming Timing(string kind, string key, string word, int attempts, long ns) =>
        new(kind, key, "authored", "analysis", "Vowel harmony", word, attempts, null, ns);

    private static GrammarWarningPart Subject(string kind, string title, WarningReach? reach, string? guid = "g") =>
        new(title, guid is null ? GrammarWarningPartRole.Text : GrammarWarningPartRole.Object, guid, kind)
        {
            Title = title,
            SubjectGuid = guid,
            Reach = reach,
        };

    private static GrammarWarning Finding(string code, params GrammarWarningPart[] subjects) =>
        new(GrammarDiagnosticLevel.Warning, code, subjects, [], code) { Code = code };

    private static WarningWords YourWords(GrammarWarning finding,
        params AssessmentObjectTiming[] timings) =>
        WarningWordsQuery.YourWordsOf(finding, Words, timings)!;

    [Fact]
    public void AnAllomorphsWordsAreTheAnalysesThatUseItByIdentityNeverBySpelling()
    {
        var found = YourWords(Finding("hc-unsegmentable", Subject("MoForm", "kat",
            new WarningReach(WarningWordsPath.Uses) { AllomorphIds = [Kat.AllomorphId!] })));

        Assert.Equal(WarningWordsMatch.Identity, found.Match);
        Assert.Equal([WarningWordsPath.Uses], found.Paths);
        Assert.Equal(["walikata", "anakata"], found.Words.Select(word => word.Row.Word));
        Assert.Equal([("Lost", 2)], found.ByMeaning.Select(meaning => (meaning.Meaning, meaning.Words)));
    }

    [Fact]
    public void AGrammaticalInfosWordsAreTheAnalysesThatUseIt()
    {
        var found = YourWords(Finding("hc-no-category", Subject("MoStemMsa", "kat",
            new WarningReach(WarningWordsPath.Uses) { GrammaticalInfoIds = [KatSkin.GrammaticalInfoId!] })));

        Assert.Equal(["ngozi"], found.Words.Select(word => word.Row.Word));
    }

    [Fact]
    public void AnEnvironmentReachesWordsThroughItsAllomorphsInSelectionOrder()
    {
        var found = YourWords(Finding("hc-bad-environment", Subject("PhEnvironment", "/ _ [V]",
            new WarningReach(WarningWordsPath.ThroughAllomorphs) { AllomorphIds = [Fik.AllomorphId!, Kat.AllomorphId!] })));

        Assert.Equal([WarningWordsPath.ThroughAllomorphs], found.Paths);
        Assert.Equal(["walikata", "anakata", "wamefika"], found.Words.Select(word => word.Row.Word));
        Assert.Equal([("Lost", 2), ("Kept", 1)], found.ByMeaning.Select(meaning => (meaning.Meaning, meaning.Words)));
    }

    [Fact]
    public void ANaturalClassReachesWordsThroughItsEnvironmentsAllomorphsAndTheRulesThatRanOnIt()
    {
        var rule = new TraceTimingKey("phon_rule", "5C9E433D-CC9B-4D12-B8CB-B5840F46DBD2");
        var found = YourWords(Finding("hc-bad-class", Subject("PhNaturalClass", "V",
                new WarningReach(WarningWordsPath.ThroughEnvironmentsAndRules)
                {
                    AllomorphIds = [Kat.AllomorphId!],
                    TimingKeys = [rule],
                })),
            Timing("phon_rule", "5c9e433d-cc9b-4d12-b8cb-b5840f46dbd2", "anakata", 3, 300),
            Timing("phon_rule", "5c9e433d-cc9b-4d12-b8cb-b5840f46dbd2", "walikula", 2, 200));

        Assert.Equal(["walikata", "walikula", "anakata"], found.Words.Select(word => word.Row.Word));
        Assert.Equal([null, 2, 3], found.Words.Select(word => word.Calls));
    }

    [Fact]
    public void ARulesWordsComeFromTheStoredRuleTimesByKey()
    {
        var found = YourWords(Finding("hc-unused-rule", Subject("PhRegularRule", "Vowel harmony",
                new WarningReach(WarningWordsPath.RuleTimes) { TimingKeys = [new TraceTimingKey("phon_rule", "rule-1")] })),
            Timing("phon_rule", "rule-1", "wamefika", 4, 400),
            Timing("phon_rule", "rule-2", "walikata", 4, 400),
            Timing("morph_rule", "rule-1", "anakata", 4, 400),
            Timing("phon_rule", "rule-1", "ngozi", 0, 0));

        Assert.Equal([WarningWordsPath.RuleTimes], found.Paths);
        Assert.Equal([("wamefika", 4, 400L)],
            found.Words.Select(word => (word.Row.Word, word.Calls ?? 0, word.ElapsedNs ?? 0)));
    }

    [Fact]
    public void ALetterMatchesBySpellingAndSaysSo()
    {
        var found = YourWords(Finding("hc-undeclared-segment",
            Subject("PhPhoneme", "ng", new WarningReach(WarningWordsPath.Spelling) { Spellings = ["NG", "kul"] })));

        Assert.Equal(WarningWordsMatch.Spelling, found.Match);
        Assert.Equal([WarningWordsPath.Spelling], found.Paths);
        Assert.Equal(["walikula", "ngozi"], found.Words.Select(word => word.Row.Word));
    }

    [Fact]
    public void IdentityWinsOverSpellingInOneFinding()
    {
        var found = YourWords(Finding("hc-partial-morpheme",
            Subject("PhPhoneme", "ng", new WarningReach(WarningWordsPath.Spelling) { Spellings = ["ng"] }),
            Subject("MoForm", "kat", new WarningReach(WarningWordsPath.Uses) { AllomorphIds = [Kat.AllomorphId!] })));

        Assert.Equal(WarningWordsMatch.Identity, found.Match);
        Assert.Equal(["walikata", "anakata"], found.Words.Select(word => word.Row.Word));
    }

    [Fact]
    public void AFindingThatNamesNothingCantTellAndSaysWhy()
    {
        var nothing = YourWords(Finding("fwdata.no-usable-allomorphs", Subject("", "no single place", null, guid: null)));
        var template = YourWords(Finding("hc-empty-template", Subject("MoInflAffixTemplate", "Verb template",
            new WarningReach(WarningWordsPath.CantTell) { CantTell = WarningCantTell.KindNotFollowed })));
        var gone = YourWords(Finding("hc-no-category", Subject("LexEntry", "kata",
            new WarningReach(WarningWordsPath.CantTell) { CantTell = WarningCantTell.NotInProject })));

        Assert.Equal((WarningWordsMatch.CantTell, WarningCantTell.NothingNamed), (nothing.Match, nothing.CantTell));
        Assert.Equal(WarningCantTell.KindNotFollowed, template.CantTell);
        Assert.Equal(WarningCantTell.NotInProject, gone.CantTell);
        Assert.All([nothing, template, gone], found => Assert.Empty(found.Words));
    }

    [Fact]
    public void ASubjectStoredWithoutItsReachLeavesTheWordsUnknown()
    {
        var finding = Finding("hc-unsegmentable", Subject("MoForm", "kat", reach: null));

        Assert.Null(WarningWordsQuery.YourWordsOf(finding, Words, []));
        Assert.Null(WarningWordsQuery.Touched([finding]));
    }

    [Fact]
    public void TheWordsWarningsTouchAreCountedOnceWithTheirNoParseAndTheSpellingOnlyOnes()
    {
        var check = new GrammarCheckResponse(
        [
            Finding("hc-unsegmentable", Subject("MoForm", "kat",
                new WarningReach(WarningWordsPath.Uses) { AllomorphIds = [Kat.AllomorphId!] })),
            Finding("hc-bad-environment", Subject("PhEnvironment", "/ _ #",
                new WarningReach(WarningWordsPath.ThroughAllomorphs) { AllomorphIds = [Kat.AllomorphId!, Fik.AllomorphId!] })),
            Finding("hc-undeclared-segment", Subject("PhPhoneme", "ng",
                new WarningReach(WarningWordsPath.Spelling) { Spellings = ["ng", "kat"] })),
            Finding("fwdata.no-usable-allomorphs"),
        ], HasBaseline: true);

        var touched = WarningWordsQuery.Touched(WarningWordsQuery.WithYourWords(check, Words, []).Findings)!;

        Assert.Equal(4, touched.Words);
        Assert.Equal(2, touched.NoParse);
        Assert.Equal(1, touched.BySpellingOnly);
        Assert.Equal([("Lost", 2), ("Kept", 2)], touched.ByMeaning.Select(meaning => (meaning.Meaning, meaning.Words)));
        Assert.Equal(0, WarningWordsQuery.Touched([])!.Words);
    }
}

using System.Linq;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// Pins how a grammar finding reaches the Selection's words: by identity through the stored analyses that use what
/// it reaches, by identity through the stored per-word rule times, by spelling only for letters and labelled so, and
/// an explicit reason when no word attribution is possible. The join shows use, never cause.
/// </summary>
[Trait("MotifTestLevel", "Unit")]
public sealed class WarningWordsQueryTests
{
    [Fact]
    public void TypedWordsUseParserAnalysisIdentitiesWithoutFieldWorksAnalyses()
    {
        var word = new AssessmentWordResult("trois", "analysed", false, "Search completed", 1, null)
        {
            Morphology = new ParseWordEvidence("v1", 0, "trois", 1, false, false, false,
                [new ParseAnalysis([new ParseMorph("form-trois", "msa-trois", null, null)])], []),
        };
        var finding = Finding("no-category", Subject("LexEntry", "trois",
            new WarningReach(WarningWordsPath.ThroughAllomorphs) { AllomorphIds = ["form-trois"] }));

        var found = WarningWordsQuery.YourWordsOf(finding, [word], [])!;

        Assert.Equal(WarningAttributionState.ExactUses, found.State);
        Assert.Equal("trois", Assert.Single(found.Words).Row.Word);
    }

    [Fact]
    public void RefusedTypedWordsCanMatchAllomorphSpellingWithoutClaimingIdentity()
    {
        var words = new[] { "Fenêtre", "fenêtres", "other" }.Select(word =>
            new AssessmentWordResult(word, "skipped", false, "Invalid shape", 0, null)
            {
                Morphology = new ParseWordEvidence("v1", 0, word, 0, false, false, true, [], []),
            }).ToArray();
        var finding = Finding("unsegmentable", Subject("MoForm", "fenêtre",
            new WarningReach(WarningWordsPath.Uses) { AllomorphIds = ["form-fenetre"], Spellings = ["fenêtre"] }));
        var found = WarningWordsQuery.YourWordsOf(finding, words, [])!;

        Assert.Equal(WarningAttributionState.SpellingCandidates, found.State);
        Assert.Empty(found.Words);
        Assert.Equal(["Fenêtre", "fenêtres"], found.SpellingCandidates.Select(word => word.Row.Word));
    }

    [Fact]
    public void LexicalSpellingDoesNotOverrideADifferentRecordedAnalysis()
    {
        var word = new AssessmentWordResult("chat", "analysed", false, "Search completed", 1, null)
        {
            Morphology = new ParseWordEvidence("v1", 0, "chat", 1, false, false, false,
                [new ParseAnalysis([new ParseMorph("another-form", "another-msa", null, null)])], []),
        };
        var finding = Finding("unsegmentable", Subject("MoForm", "chat",
            new WarningReach(WarningWordsPath.Uses) { AllomorphIds = ["form-chat"], Spellings = ["chat"] }));
        var result = WarningWordsQuery.YourWordsOf(finding, [word], [])!;
        Assert.Empty(result.Words);
        Assert.Empty(result.SpellingCandidates);
    }

    [Fact]
    public void RepeatedParserAnalysesCountEachSelectionWordOnceByCanonicalGuid()
    {
        var id = Guid.NewGuid().ToString("D");
        var analysis = new ParseAnalysis([new ParseMorph(id.ToUpperInvariant(), null, null, null)]);
        var word = new AssessmentWordResult("trois", "analysed", false, "Search completed", 1, null)
        {
            Morphology = new ParseWordEvidence("v1", 0, "trois", 1, false, false, false, [analysis, analysis], []),
        };
        var finding = Finding("no-category", Subject("LexEntry", "trois",
            new WarningReach(WarningWordsPath.ThroughAllomorphs) { AllomorphIds = [id] }));
        var result = WarningWordsQuery.YourWordsOf(finding, [word], [])!;
        Assert.Equal("trois", Assert.Single(result.Words).Row.Word);
    }

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
        Assert.All(found.Words, word => Assert.Equal(
            WordRowReadingAvailability.NotRequested, word.Row.PanGlossReadingAvailability));
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
        Assert.Equal(["ngozi"], found.SpellingCandidates.Select(word => word.Row.Word));
    }

    [Fact]
    public void MembershipIsSeparateFromExactUsesAndSpellingInAMixedFinding()
    {
        var found = YourWords(Finding("mixed",
            Subject("MoForm", "kat", new WarningReach(WarningWordsPath.Uses) { AllomorphIds = [Kat.AllomorphId!] }),
            Subject("MoInflAffixSlot", "Plural", new WarningReach(WarningWordsPath.Membership)
                { GrammaticalInfoIds = [Kul.GrammaticalInfoId!] }),
            Subject("PhPhoneme", "ng", new WarningReach(WarningWordsPath.Spelling) { Spellings = ["ng"] })));

        Assert.Equal(WarningAttributionState.ExactUses, found.State);
        Assert.Equal(["walikata", "anakata"], found.Words.Select(word => word.Row.Word));
        Assert.Equal(["walikula"], found.MembershipCandidates.Select(word => word.Row.Word));
        Assert.Equal(["ngozi"], found.SpellingCandidates.Select(word => word.Row.Word));
        var touched = WarningWordsQuery.Touched([Finding("mixed") with { YourWords = found }])!;
        Assert.Equal((2, 2, 1, 1), (touched.Words, touched.NoParse, touched.ByMembershipOnly, touched.BySpellingOnly));
    }

    [Fact]
    public void CandidateOnlyFindingsHaveNoExactHeadlineCount()
    {
        var membership = YourWords(Finding("template", Subject("MoInflAffixTemplate", "Plural",
            new WarningReach(WarningWordsPath.Membership) { AllomorphIds = [Kat.AllomorphId!] })));
        var spelling = YourWords(Finding("phoneme", Subject("PhPhoneme", "kat",
            new WarningReach(WarningWordsPath.Spelling) { Spellings = ["kat", "ng"] })));
        Assert.Equal(WarningAttributionState.MembershipCandidates, membership.State);
        Assert.Equal(WarningAttributionState.SpellingCandidates, spelling.State);
        var touched = WarningWordsQuery.Touched([
            Finding("template") with { YourWords = membership }, Finding("phoneme") with { YourWords = spelling }])!;
        Assert.Equal((0, 0, 2, 1), (touched.Words, touched.NoParse, touched.ByMembershipOnly, touched.BySpellingOnly));
        Assert.Empty(touched.ByMeaning);
    }

    [Theory]
    [InlineData(WarningWordsPath.Uses)]
    [InlineData(WarningWordsPath.Membership)]
    [InlineData(WarningWordsPath.Spelling)]
    public void SupportedEmptyRoutesSayNoneInThisSelection(WarningWordsPath path)
    {
        var result = YourWords(Finding("empty", Subject("MoForm", "unused", new WarningReach(path))));
        Assert.Equal(WarningAttributionState.NoneInSelection, result.State);
        Assert.Empty(result.Words);
    }

    [Theory]
    [InlineData(WarningWordsPath.MissingObject, WarningAttributionReason.StaleGuid, WarningAttributionState.MissingObject)]
    [InlineData(WarningWordsPath.MissingObject, WarningAttributionReason.WrongClass, WarningAttributionState.MissingObject)]
    [InlineData(WarningWordsPath.UnresolvedIdentity, WarningAttributionReason.NamedWithoutProjectGuid, WarningAttributionState.UnresolvedIdentity)]
    [InlineData(WarningWordsPath.ProjectWide, WarningAttributionReason.NoWordAttribution, WarningAttributionState.ProjectWide)]
    public void UnattributedFindingsKeepTheirSpecificReason(WarningWordsPath path, WarningAttributionReason reason,
        WarningAttributionState state)
    {
        var result = YourWords(Finding("unattributed", Subject("Grammar", "named",
            new WarningReach(path) { Reason = reason })));
        Assert.Equal(state, result.State);
        Assert.Equal(reason, result.Reason);
        Assert.Empty(result.Words);
    }

    [Fact]
    public void NoSubjectIsExplicitAndIsNotANamedObjectWithoutAGuid()
    {
        var result = YourWords(Finding("no-subject"));
        Assert.Equal(WarningAttributionState.UnresolvedIdentity, result.State);
        Assert.Equal(WarningAttributionReason.NoSubject, result.Reason);
    }

    [Fact]
    public void FeatureOwnersCanSupplyMembershipAlongsideExactUses()
    {
        var result = YourWords(Finding("feature", Subject("FsClosedFeature", "Number",
            new WarningReach(WarningWordsPath.ThroughFeatureOwners)
            {
                GrammaticalInfoIds = [Kat.GrammaticalInfoId!],
                MembershipGrammaticalInfoIds = [Kul.GrammaticalInfoId!],
                Spellings = ["ng"],
            })));
        Assert.Equal(["walikata", "anakata"], result.Words.Select(word => word.Row.Word));
        Assert.Equal(["walikula"], result.MembershipCandidates.Select(word => word.Row.Word));
        Assert.Equal(["ngozi"], result.SpellingCandidates.Select(word => word.Row.Word));
    }

    [Fact]
    public void StrongerRoutesRemoveDuplicatesWithinAndAcrossFindings()
    {
        var mixed = YourWords(Finding("mixed",
            Subject("MoForm", "kat", new WarningReach(WarningWordsPath.Uses) { AllomorphIds = [Kat.AllomorphId!] }),
            Subject("MoInflAffixSlot", "slot", new WarningReach(WarningWordsPath.Membership)
                { AllomorphIds = [Kat.AllomorphId!, Kul.AllomorphId!] }),
            Subject("PhPhoneme", "letters", new WarningReach(WarningWordsPath.Spelling) { Spellings = ["kat", "kul", "ng"] })));
        Assert.Equal(["walikula"], mixed.MembershipCandidates.Select(word => word.Row.Word));
        Assert.Equal(["ngozi"], mixed.SpellingCandidates.Select(word => word.Row.Word));
        var exact = YourWords(Finding("exact", Subject("MoForm", "kul",
            new WarningReach(WarningWordsPath.Uses) { AllomorphIds = [Kul.AllomorphId!] })));
        var touched = WarningWordsQuery.Touched([
            Finding("mixed") with { YourWords = mixed }, Finding("exact") with { YourWords = exact }])!;
        Assert.Equal((3, 0, 1), (touched.Words, touched.ByMembershipOnly, touched.BySpellingOnly));
    }

    [Fact]
    public void EmptyExactRouteKeepsCandidateEvidenceAndItsAttributionState()
    {
        var result = YourWords(Finding("mixed", Subject("FsClosedFeature", "feature",
            new WarningReach(WarningWordsPath.ThroughFeatureOwners)
            {
                MembershipGrammaticalInfoIds = [Kul.GrammaticalInfoId!],
                Spellings = ["ng"],
            })));
        Assert.Equal(WarningWordsMatch.Identity, result.Match);
        Assert.Equal(WarningAttributionState.MembershipCandidates, result.State);
        Assert.Empty(result.Words);
        Assert.Equal("walikula", Assert.Single(result.MembershipCandidates).Row.Word);
        Assert.Equal("ngozi", Assert.Single(result.SpellingCandidates).Row.Word);
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

        Assert.Equal(3, touched.Words);
        Assert.Equal(2, touched.NoParse);
        Assert.Equal(1, touched.BySpellingOnly);
        Assert.Equal([("Lost", 2), ("Kept", 1)], touched.ByMeaning.Select(meaning => (meaning.Meaning, meaning.Words)));
        Assert.Equal(0, WarningWordsQuery.Touched([])!.Words);
    }
}

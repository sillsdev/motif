using System.Linq;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// Pins what the inspector and a Matrix cell read about an object: the Selection's words whose stored analyses
/// use it, matched by identity and never by spelling, split by meaning; the morphemes a set of words shares; and
/// the words an object ran in, from the stored per-word timings, by key.
/// </summary>
public sealed class ObjectUsesQueryTests
{
    private static ParserReadingMorph Morph(string form, string gloss, string id) =>
        new(form, gloss, "v", null, false, null) { AllomorphId = "form-" + id, GrammaticalInfoId = "msa-" + id };

    private static readonly ParserReadingMorph Wa = Morph("wa-", "3PL", "wa");
    private static readonly ParserReadingMorph A = Morph("a-", "3SG", "a");
    private static readonly ParserReadingMorph Ha = Morph("ha-", "NEG", "ha");
    private static readonly ParserReadingMorph Tu = Morph("tu-", "1PL", "tu");
    private static readonly ParserReadingMorph M = Morph("m-", "2PL", "m");
    private static readonly ParserReadingMorph Li = Morph("li-", "PST", "li");
    private static readonly ParserReadingMorph Na = Morph("na-", "PRS", "na");
    private static readonly ParserReadingMorph Me = Morph("me-", "PRF", "me");
    private static readonly ParserReadingMorph Ja = Morph("ja-", "NEG.PERF", "ja");
    private static readonly ParserReadingMorph Kat = Morph("kat", "cut", "kat");
    private static readonly ParserReadingMorph Fik = Morph("fik", "arrive", "fik");
    private static readonly ParserReadingMorph On = Morph("on", "see", "on");
    private static readonly ParserReadingMorph Fv = Morph("-a", "FV", "fv");

    private static ParserReading Stored(string opinion, params ParserReadingMorph[] morphs) =>
        new(morphs) { StoredAnalysisId = "analysis-" + string.Concat(morphs.Select(morph => morph.Form)), StoredAnalysisOpinion = opinion };

    private static AssessmentWordResult Word(string word, string standing, string outcome, params ParserReading[] stored) =>
        new(word, outcome, false, "Search completed", 1, null)
        {
            ProjectStanding = standing,
            StoredAnalyses = stored,
            ReadingGrades = outcome == "analysed" ? [ReadingGrade.Approved] : [],
            Morphology = outcome == "analysed"
                ? new ParseWordEvidence("v1", 0, word, 1, false, false, false, [new ParseAnalysis([new ParseMorph("x", "y", null, null)])], [])
                : null,
            OccurrenceCount = 1,
        };

    private static AssessmentWordResult Lost(string word, params ParserReadingMorph[] morphs) =>
        Word(word, ProjectStanding.Approved, "no-analysis", Stored(ReadingGrade.Approved, morphs));

    // The Matrix's Approved × No parse cell in the design's sample: three words use kat and three use ja-.
    private static readonly AssessmentWordResult[] LostCell =
    [
        Lost("walikata", Wa, Li, Kat, Fv),
        Lost("anakata", A, Na, Kat, Fv),
        Lost("wamekata", Wa, Me, Kat, Fv),
        Lost("hawajafika", Ha, Wa, Ja, Fik, Fv),
        Lost("hatujaona", Ha, Tu, Ja, On, Fv),
        Lost("hamjafika", Ha, M, Ja, Fik, Fv),
    ];

    [Fact]
    public void APartiallySuppliedTimingAddressIsNeverFilledFromAnotherObject()
    {
        var reference = new ObjectUseRef { TimingKey = "missing", AllomorphId = "form" };
        var facts = new ObjectFacts { TimingKey = new TraceTimingKey("morph_rule", "another") };
        Assert.Equal(reference, ObjectUsesQuery.WithTimingKey(reference, facts));
    }

    [Fact]
    public void SharedMorphemesCompareTuplesRatherThanConcatenatedOpaqueKeys()
    {
        var first = A with { AllomorphId = "a/b", GrammaticalInfoId = "c" };
        var second = A with { AllomorphId = "a", GrammaticalInfoId = "b/c" };
        var words = new[] { Lost("one", first), Lost("two", second) };
        Assert.Empty(ObjectUsesQuery.SharedBy(words, ["one", "two"]));
    }

    [Fact]
    public void StructuralTimingKeysAreNotCanonicalizedEvenWhenTheyLookLikeGuids()
    {
        const string key = "aaaaaaaa-0000-0000-0000-000000000001";
        var timings = new[] { new AssessmentObjectTiming("phon_rule", "{" + key + "}", "structural",
            "analysis", "Same label", "walikata", 1, null, 1) };
        Assert.Empty(ObjectUsesQuery.RanIn(LostCell, timings,
            ObjectUseRef.ForTimingKey(new TraceTimingKey("phon_rule", key) { IdentityQuality = "structural" })).Words);
    }

    [Fact]
    public void TheLostCellsSixWordsShareKatInThreeAndJaInThree()
    {
        var shared = ObjectUsesQuery.SharedBy(LostCell, LostCell.Select(word => word.Word).ToArray());

        Assert.Equal(["walikata", "anakata", "wamekata"], shared.Single(morph => morph.Morpheme.Form == "kat").Words);
        Assert.Equal(["hawajafika", "hatujaona", "hamjafika"], shared.Single(morph => morph.Morpheme.Form == "ja-").Words);
        Assert.Equal(3, shared.Single(morph => morph.Morpheme.Form == "ja-").Count);
        Assert.Equal(("-a", 6), (shared[0].Morpheme.Form, shared[0].Count));
        Assert.Equal(["-a", "wa-", "kat", "ha-", "ja-", "fik"], shared.Select(morph => morph.Morpheme.Form));
        Assert.All(shared, morph => Assert.True(morph.Count >= 2));
    }

    [Fact]
    public void TwoHomographsWithDifferentEntriesStayApart()
    {
        var katCut = Morph("kat", "cut", "kat-cut");
        var katSkin = Morph("kat", "skin", "kat-skin");
        var words = new[]
        {
            Lost("walikata", Wa, Li, katCut, Fv),
            Word("anakata", ProjectStanding.Approved, "analysed", Stored(ReadingGrade.Approved, A, Na, katSkin, Fv)),
        };

        var cut = ObjectUsesQuery.UsesOf(words, ObjectUseRef.ForMorpheme(katCut));
        var skin = ObjectUsesQuery.UsesOf(words, ObjectUseRef.ForMorpheme(katSkin));
        var shared = ObjectUsesQuery.SharedBy(words, ["walikata", "anakata"]);

        Assert.Equal(["walikata"], cut.Words.Select(word => word.Row.Word));
        Assert.Equal(["anakata"], skin.Words.Select(word => word.Row.Word));
        Assert.DoesNotContain(shared, morph => morph.Morpheme.Form == "kat");
        Assert.Equal(["-a"], shared.Select(morph => morph.Morpheme.Form));
    }

    [Fact]
    public void TheWordsThatUseAMorphemeAreSplitByMeaning()
    {
        var words = LostCell.Append(
            Word("walikata", ProjectStanding.Approved, "analysed", Stored(ReadingGrade.Approved, Wa, Li, Kat, Fv)) with
            {
                Word = "tulikata",
            }).ToArray();

        var uses = ObjectUsesQuery.UsesOf(words, ObjectUseRef.ForMorpheme(Kat));

        Assert.Equal(["walikata", "anakata", "wamekata", "tulikata"], uses.Words.Select(word => word.Row.Word));
        Assert.Equal([("Lost", WordRowTone.Problem, 3), ("Kept", WordRowTone.Fine, 1)],
            uses.ByMeaning.Select(meaning => (meaning.Meaning, meaning.Tone, meaning.Words)));
        Assert.All(uses.Words, word => Assert.Null(word.ElapsedNs));
    }

    [Fact]
    public void ADisapprovedAnalysisIsNotAUse()
    {
        var words = new[]
        {
            Word("anakata", ProjectStanding.Rejected, "no-analysis", Stored(ReadingGrade.Disapproved, A, Na, Kat, Fv)),
        };

        Assert.Empty(ObjectUsesQuery.UsesOf(words, ObjectUseRef.ForMorpheme(Kat)).Words);
        Assert.Empty(ObjectUsesQuery.SharedBy([.. words, Lost("walikata", Wa, Li, Kat, Fv)], ["anakata", "walikata"]));
    }

    [Fact]
    public void TheWordsLeftOutForADisapprovedAnalysisAreCountedSoTheWindowCanSaySo()
    {
        var words = new[]
        {
            Word("anakata", ProjectStanding.Rejected, "no-analysis", Stored(ReadingGrade.Disapproved, A, Na, Kat, Fv)),
            Word("wamekata", ProjectStanding.Rejected, "no-analysis", Stored(ReadingGrade.Disapproved, Wa, Me, Kat, Fv),
                Stored(ReadingGrade.Disapproved, Wa, Me, Kat)),
            Word("walikata", ProjectStanding.Approved, "no-analysis", Stored(ReadingGrade.Approved, Wa, Li, Kat, Fv),
                Stored(ReadingGrade.Disapproved, Wa, Li, Kat)),
            Lost("hawajafika", Ha, Wa, Ja, Fik, Fv),
        };

        var uses = ObjectUsesQuery.UsesOf(words, ObjectUseRef.ForMorpheme(Kat));

        Assert.Equal(["walikata"], uses.Words.Select(word => word.Row.Word));
        Assert.Equal(2, uses.NotCountingDisapproved);
        Assert.Equal(0, ObjectUsesQuery.UsesOf(words, ObjectUseRef.ForMorpheme(Ja)).NotCountingDisapproved);
    }

    [Fact]
    public void ARefNamingOnlyTheGrammaticalInfoMatchesEveryAllomorphOfIt()
    {
        var kata = Kat with { Form = "kata", AllomorphId = "form-kata" };
        var words = new[] { Lost("walikata", Wa, Li, Kat, Fv), Lost("kukata", Morph("ku-", "INF", "ku"), kata) };

        var byGrammaticalInfo = ObjectUsesQuery.UsesOf(words, new ObjectUseRef { GrammaticalInfoId = "msa-kat" });
        var byAllomorph = ObjectUsesQuery.UsesOf(words, new ObjectUseRef { AllomorphId = "form-kata" });

        Assert.Equal(["walikata", "kukata"], byGrammaticalInfo.Words.Select(word => word.Row.Word));
        Assert.Equal(["kukata"], byAllomorph.Words.Select(word => word.Row.Word));
    }

    [Fact]
    public void ARulesWordsComeFromTheStoredTimingsByKey()
    {
        var harmonyKey = "6f1d2c3b-0000-4000-8000-000000000001";
        var timings = new[]
        {
            Timing("phon_rule", harmonyKey, "Vowel harmony", "walikata", 4, 100_000),
            Timing("phon_rule", harmonyKey, "Vowel harmony", "walikata", 2, 50_000, "synthesis"),
            Timing("phon_rule", harmonyKey.ToUpperInvariant(), "Vowel harmony", "anakata", 1, null),
            Timing("phon_rule", "6f1d2c3b-0000-4000-8000-000000000002", "Vowel harmony", "wamekata", 9, 900_000),
            Timing("morph_rule", harmonyKey, "Vowel harmony", "hatujaona", 3, 300_000),
            Timing("phon_rule", harmonyKey, "Vowel harmony", "hamjafika", 0, 0),
            Timing("phon_rule", harmonyKey, "Vowel harmony", "not-in-the-selection", 5, 500_000),
        };

        var ran = ObjectUsesQuery.RanIn(LostCell, timings, new ObjectUseRef { TimingKind = "phon_rule", TimingKey = harmonyKey });

        Assert.Equal([("walikata", 6, 150_000L), ("anakata", 1, (long?)null)],
            ran.Words.Select(word => (word.Row.Word, word.Calls, word.ElapsedNs)));
        Assert.Equal([("Lost", WordRowTone.Problem, 2)], ran.ByMeaning.Select(meaning => (meaning.Meaning, meaning.Tone, meaning.Words)));
    }

    [Fact]
    public void ARefWithoutATimingKeyRanInNothingItCanName()
    {
        Assert.Null(ObjectUsesQuery.Read(LostCell, [], ObjectUseRef.ForMorpheme(Kat), null).RanIn);
        Assert.Null(ObjectUsesQuery.Read(LostCell, [], new ObjectUseRef { TimingKind = "phon_rule", TimingKey = "x" }, null).Uses);
    }

    [Fact]
    public void ReadAnswersBothQuestionsAndNamesTheWordsTheAssessmentDoesNotHold()
    {
        var response = ObjectUsesQuery.Read(LostCell, [], ObjectUseRef.ForMorpheme(Kat),
            ["walikata", "anakata", " mtoto "]);

        Assert.Equal(ObjectUseRef.ForMorpheme(Kat), response.Ref);
        Assert.Equal(3, response.Uses!.Words.Count);
        Assert.Equal(["kat", "-a"], response.Shared!.Select(morph => morph.Morpheme.Form));
        Assert.Equal(["mtoto"], response.UnknownWords);
    }

    [Fact]
    public void AMorphemesRefKeepsItsIdentityAndItsLabelsForDisplayOnly()
    {
        var reference = ObjectUseRef.ForMorpheme(Kat);

        Assert.Equal(("form-kat", "msa-kat", "kat", "cut"),
            (reference.AllomorphId, reference.GrammaticalInfoId, reference.Label, reference.Gloss));
        Assert.Null(reference.TimingKey);
        Assert.Empty(ObjectUsesQuery.UsesOf(LostCell, reference with { AllomorphId = "form-other" }).Words);
        Assert.Equal(3, ObjectUsesQuery.UsesOf(LostCell, reference with { Label = "other", Gloss = "other" }).Words.Count);
    }

    private static AssessmentObjectTiming Timing(string kind, string key, string label, string word, int? attempts,
        long? elapsedNs, string direction = "analysis") =>
        new(kind, key, "authored", direction, label, word, attempts, null, elapsedNs);
}

using System.Text.Json;
using SIL.Motif.Contract.Query;
using Xunit;

namespace SIL.Motif.Tests.Contract.Parsimony;

public sealed class SplitQueryTests
{
    private static readonly string[] Letters = ["a", "u", "d", "r", "n", "s", "k"];

    private static QueryRow Stem(string id, string form, string[]? features = null, string entry = "") =>
        new(id, [[form], [entry.Length == 0 ? "e-" + id : entry], ["V"], features ?? [], ["go"], ["1"], ["0"]]);

    private static QueryHypothesisInputs Inputs(IEnumerable<string>? evidence = null, IEnumerable<string>? discovery = null,
        IEnumerable<string>? heldOut = null, IEnumerable<(string, string)>? forms = null, Func<QueryParseEvidence?>? parse = null) =>
        new([.. evidence ?? []], [.. discovery ?? []], [.. heldOut ?? []], [.. forms ?? []], parse);

    private static QueryHypothesis DToR(bool sets = true) => new("d", "r", "final", ["an"], sets ? ["union"] : [], sets ? ["disc"] : null,
        sets ? ["held"] : null);

    private static QueryRecord Split(QueryHypothesis? change = null, params string[] extra) =>
        new(QuerySources.Stems, [], [], Examples: 3, Change: change, Score: ["split"],
            Exceptions: extra.Length == 0 ? null : [new QueryCondition("features", "has", extra[0])]);

    private static QueryContext Context(QueryResult result, string column, string value) =>
        result.Scores!.Single(s => s.Scorer == "split").Contexts!.Single(c => c.Column == column && c.Value == value);

    // 300 stems ending ad (a few keep d) and 100 ending ud (all keep d), sharing one context final:2=ud.
    private static List<QueryRow> UdKeepers(int adKeepers = 3)
    {
        var stems = new List<QueryRow>();
        for (var i = 0; i < 300; i++) stems.Add(Stem("a" + i, $"s{i:000}ad", i < adKeepers ? ["KEEP"] : []));
        for (var i = 0; i < 100; i++) stems.Add(Stem("u" + i, $"k{i:000}ud", ["KEEP"]));
        return stems;
    }

    [Fact]
    public void AContextThatCoversAndPredictsTheDeviantsIsASubRuleOnlyWhenFieldWorksCanStateItAndHeldOutHolds()
    {
        var stems = UdKeepers();
        var withoutGraphemes = FormQuery.RunRows(Split(extra: "KEEP"), stems, []);
        var unchecked1 = Context(withoutGraphemes, "final:2", "ud");
        Assert.Equal((100, 100), (unchecked1.Scope, unchecked1.Deviants));
        Assert.True(unchecked1.Covers);
        Assert.True(unchecked1.Reliable);
        Assert.True(unchecked1.Smaller);
        Assert.Equal("not-checked", unchecked1.Expressible);
        Assert.Equal("not-checked", unchecked1.HeldOut);
        Assert.Equal("not-checked", unchecked1.Status);
        Assert.Equal("not-checked", withoutGraphemes.Scores![0].Verdict);

        var withGraphemes = FormQuery.RunRows(Split(extra: "KEEP"), stems, Letters);
        var known = Context(withGraphemes, "final:2", "ud");
        Assert.Equal("yes", known.Expressible);
        Assert.Equal("not-checked", known.HeldOut);
        Assert.Equal("not-checked", known.Status);
    }

    [Fact]
    public void HeldOutDecidesASubRuleFromCountsAloneAndOnlyAboveTwentyWords()
    {
        var stems = UdKeepers(adKeepers: 0);
        var evidence = new List<string>();
        var held = new List<string> { "secretword" };
        foreach (var stem in stems)
        {
            var form = stem.Values[0][0];
            if (form.EndsWith("ad", StringComparison.Ordinal)) evidence.Add(form[..^1] + "ran");
            else evidence.Add(form + "an");
        }
        for (var i = 0; i < 40; i++) held.Add($"k{i:000}udan");
        for (var i = 0; i < 40; i++) held.Add($"s{i:000}aran");
        var query = Split(DToR());
        var result = FormQuery.RunHypothesis(query, stems, Letters, Inputs(evidence, ["zzz"], held));
        var context = Context(result, "final:2", "ud");
        Assert.Equal("holds", context.HeldOut);
        Assert.Equal("A", context.Status);
        Assert.Equal("A", result.Scores![0].Verdict);
        Assert.Equal(new Dictionary<string, long> { ["deviants"] = 40, ["rule"] = 0 }, context.HeldOutCounts);
        var json = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("secretword", json);
        Assert.DoesNotContain("k000udan", json);

        var fewHeld = held.Where(w => w == "secretword" || !w.StartsWith('k') || string.CompareOrdinal(w, "k010") < 0).ToList();
        var few = Context(FormQuery.RunHypothesis(query, stems, Letters, Inputs(evidence, ["zzz"], fewHeld)), "final:2", "ud");
        Assert.Equal("too-few", few.HeldOut);
        Assert.Null(few.HeldOutCounts);
        Assert.Equal("not-checked", few.Status);
    }

    [Fact]
    public void AContextThatIsAFactAboutOtherWordsIsPredictableButNotARule()
    {
        var stems = new List<QueryRow>();
        var forms = new List<(string, string)>();
        for (var i = 0; i < 300; i++)
        {
            var collide = i < 60;
            stems.Add(Stem("m" + i, $"s{i:000}ad", collide ? ["KEEP"] : []));
            if (collide) forms.Add(($"s{i:000}aran", "other"));
        }
        var result = FormQuery.RunHypothesis(Split(DToR(sets: false), "KEEP"), stems, Letters, Inputs(forms: forms));
        var collides = Context(result, "collides", "entry");
        Assert.Equal((60, 60), (collides.Scope, collides.Deviants));
        Assert.Equal("no", collides.Expressible);
        Assert.Equal("B", collides.Status);
        Assert.Equal("B", result.Scores![0].Verdict);
    }

    [Fact]
    public void DeviantsNoContextCoversAreALexicalList()
    {
        var stems = Enumerable.Range(0, 400).Select(i => Stem("m" + i, $"{Letters[i * 3 % 7]}{i % 5}{Letters[i * 5 % 7]}{i:000}ad",
            i % 11 == 0 ? ["KEEP"] : [])).ToList();
        var result = FormQuery.RunRows(Split(extra: "KEEP"), stems, Letters);
        var score = result.Scores!.Single();
        Assert.Equal("C", score.Verdict);
        Assert.DoesNotContain(score.Contexts!, c => c.Status is "A" or "B" or "not-checked");
    }

    [Fact]
    public void ASubPatternOfFewerThanTwentyRowsIsOnlyTooFewToTell()
    {
        var stems = new List<QueryRow>();
        for (var i = 0; i < 300; i++) stems.Add(Stem("a" + i, $"s{i:000}ad", i < 19 ? ["KEEP"] : []));
        for (var i = 0; i < 19; i++) stems[i] = Stem("a" + i, $"k{i:000}ud", ["KEEP"]);
        var result = FormQuery.RunRows(Split(extra: "KEEP"), stems, Letters);
        var small = Context(result, "final:2", "ud");
        Assert.Equal("too-few-to-tell", small.Status);
        Assert.Equal(19, small.Scope);
        Assert.Null(small.Lower);
        Assert.Null(small.Covers);
        Assert.Equal("C", result.Scores![0].Verdict);
        Assert.Contains(result.Scores[0].Notes, n => n.Contains("too few to tell"));
        stems[19] = Stem("a19", "k019ud", ["KEEP"]);
        Assert.NotEqual("too-few-to-tell", Context(FormQuery.RunRows(Split(extra: "KEEP"), stems, Letters), "final:2", "ud").Status);
    }

    [Fact]
    public void ASecondSplitOnTheResidueFindsTheOtherHalfAndStopsThere()
    {
        var stems = new List<QueryRow>();
        for (var i = 0; i < 400; i++) stems.Add(Stem("a" + i, $"s{i:000}ad"));
        for (var i = 0; i < 60; i++) stems.Add(Stem("u" + i, $"k{i:000}ud", ["KEEP"]));
        for (var i = 0; i < 40; i++) stems.Add(Stem("n" + i, $"n{i:000}nd", ["KEEP"]));
        var contexts = FormQuery.RunRows(Split(extra: "KEEP"), stems, Letters).Scores!.Single().Contexts!;
        Assert.DoesNotContain(contexts, c => c.Depth > 2);
        var second = contexts.Single(c => c.Depth == 2 && c.Column == "final:2" && c.Value == "nd");
        Assert.Equal("final:2=ud", second.Outside);
        Assert.Equal("not-checked", second.Status);
        Assert.True(second.Covers);
    }

    [Fact]
    public void SizeCountsObjectsAndReferencesInOneUnitEach()
    {
        var stems = UdKeepers();
        var withContext = FormQuery.RunRows(new QueryRecord(QuerySources.Stems, [], [], Score: ["size"],
            Exceptions: [new("features", "has", "KEEP")], Context: [new("final:2", "is", "ud")]), stems, Letters).Scores!.Single();
        Assert.Equal(104, withContext.Counts["before_total"]);
        Assert.Equal(1, withContext.Counts["before_objects"]);
        Assert.Equal(103, withContext.Counts["before_references"]);
        Assert.Equal(5, withContext.Counts["after_total"]);
        Assert.Equal("smaller", withContext.Verdict);
        var polarity = FormQuery.RunRows(new QueryRecord(QuerySources.Stems, [], [], Score: ["size"],
            Exceptions: [new("features", "has", "KEEP")]), stems, Letters).Scores!.Single();
        Assert.Equal(297, polarity.Counts["after_references"]);
        Assert.Equal("larger", polarity.Verdict);
        Assert.Throws<ArgumentException>(() => FormQuery.RunRows(new QueryRecord(QuerySources.Stems, [], [], Score: ["split"],
            Context: [new("final:2", "is", "ud")], Exceptions: [new("features", "has", "KEEP")]), stems, Letters));
    }

    [Fact]
    public void WhatIsNotMeasuredIsRefusedAndNeverReadAsZero()
    {
        var stems = UdKeepers();
        Assert.Throws<QueryNotMeasuredException>(() => FormQuery.RunHypothesis(Split(DToR(sets: false)), stems, Letters, Inputs()));
        Assert.Throws<ArgumentException>(() => FormQuery.RunRows(Split(), stems, Letters));
        Assert.Throws<ArgumentException>(() => FormQuery.RunRows(new QueryRecord(QuerySources.Uses, [], [], Score: ["split"],
            Exceptions: [new("target", "is", "x")]), [], Letters));
        Assert.Throws<ArgumentException>(() => FormQuery.RunRows(new QueryRecord(QuerySources.Stems, [], [], Score: ["tolerance"]), stems, Letters));
        Assert.Throws<ArgumentException>(() => FormQuery.RunRows(new QueryRecord(QuerySources.Stems, [], ["shape"], Score: ["split"],
            Exceptions: [new("features", "has", "KEEP")]), stems, Letters));
        var one = FormQuery.RunRows(new QueryRecord(QuerySources.Stems, [new("id", "is", "a1")], [], Score: ["split"],
            Exceptions: [new("id", "is", "a1")]), stems, Letters).Scores!.Single();
        Assert.Null(one.Verdict);
    }

    [Fact]
    public void EvidenceShowsSharedContextsCounterExamplesScoresQuestionsAndWhatItDidNotMeasure()
    {
        var stems = UdKeepers();
        stems.Add(Stem("x", "k999ud"));
        var evidenceWords = stems.Where(s => s.Values[0][0].EndsWith("ad", StringComparison.Ordinal))
            .Select(s => s.Values[0][0][..^1] + "ran").Take(150).ToList();
        var query = new QueryRecord(QuerySources.Stems, [new("final:2", "is", "ud")], [], Examples: 4, Change: DToR(), Evidence: true);
        var result = FormQuery.RunHypothesis(query, stems, Letters, Inputs(evidenceWords, ["zzz"], ["yyy"]));
        var evidence = result.Evidence!;
        Assert.Equal((101, 401), (evidence.Members, evidence.Universe));
        var udContext = evidence.Shared.Single(c => c.Label == "final:2=ud");
        Assert.Equal((101, 101), (udContext.Scope, udContext.Deviants));
        Assert.DoesNotContain(evidence.Shared, c => c.Column == "final:1");
        Assert.All(evidence.Shared, c => Assert.True(c.CounterExamples!.Count <= 3));
        Assert.Contains(evidence.Scores, s => s.Scorer == "tolerance" && s.Unit == "stem");
        Assert.Contains(evidence.Scores, s => s.Scorer == "reliability");
        Assert.Contains(evidence.Scores, s => s.Scorer == "size");
        Assert.Equal(4, evidence.Examples.Count);
        Assert.Equal(4, evidence.Questions.Count);
        Assert.Contains("a word?", evidence.Questions[0].Text);
        Assert.Equal(("d>r@final", 101L, true), (evidence.SameShape!.Key, evidence.SameShape.Count, evidence.SameShape.RuleAttemptRequired));
        Assert.Contains(evidence.NotMeasured, n => n.Contains("Token frequency is not measured"));
        Assert.Null(result.Items?.FirstOrDefault());
    }

    [Fact]
    public void EvidenceWithNoChangeNamesWhatNeedsOneAndTakesMembersFromIds()
    {
        var stems = UdKeepers();
        var ids = Enumerable.Range(0, 100).Select(i => "u" + i).ToList();
        var result = FormQuery.RunRows(new QueryRecord(QuerySources.Stems, [], [], Evidence: true, Members: ids), stems, Letters);
        var evidence = result.Evidence!;
        Assert.Equal(100, evidence.Members);
        Assert.Contains(evidence.Shared, c => c.Label == "final:2=ud");
        Assert.Contains(evidence.NotMeasured, n => n.StartsWith("tolerance, reliability and control", StringComparison.Ordinal));
        Assert.Contains(evidence.NotMeasured, n => n.StartsWith("Speaker questions", StringComparison.Ordinal));
        Assert.Contains(evidence.NotMeasured, n => n.StartsWith("Same-shape", StringComparison.Ordinal));
        Assert.Null(evidence.SameShape);
        Assert.Throws<ArgumentException>(() => FormQuery.RunRows(new QueryRecord(QuerySources.Stems, [new("final:2", "is", "ud")], [],
            Evidence: true, Members: ids), stems, Letters));
        Assert.Throws<ArgumentException>(() => FormQuery.RunRows(new QueryRecord(QuerySources.Stems, [], [], Evidence: true), stems, Letters));
        Assert.Throws<ArgumentException>(() => FormQuery.RunRows(new QueryRecord(QuerySources.Stems, [new("final:2", "is", "zz")], [],
            Evidence: true), stems, Letters));
    }

    [Fact]
    public void EvidenceOverFormsCountsTheSameShapeAndFiveMeansARuleAttemptIsRequired()
    {
        var forms = new List<QueryForm>();
        for (var i = 0; i < 40; i++)
            forms.Add(new QueryForm("v" + i, $"s{i:00}ri", $"s{i:00}di", "variant", "e" + i, "stem", "free", ["/_V"], ["Verb"], Tokens: 100 - i));
        for (var i = 0; i < 4; i++)
            forms.Add(new QueryForm("l" + i, $"t{i:00}di", $"t{i:00}di", "lexeme", "x" + i, "stem", "", [], ["Verb"], Tokens: 5));
        var result = FormQuery.Run(new QueryRecord(QuerySources.Forms, [new("kind", "is", "variant")], [], Examples: 3, Evidence: true), forms, Letters);
        var evidence = result.Evidence!;
        Assert.Equal((40, 44), (evidence.Members, evidence.Universe));
        Assert.Equal(40, evidence.SameShape!.Count);
        Assert.True(evidence.SameShape.RuleAttemptRequired);
        Assert.StartsWith("variant|free|env:/_V|", evidence.SameShape.Key);
        Assert.Equal("v0", evidence.Examples[0].Id);
        Assert.Equal(100, evidence.Examples[0].Tokens);
        Assert.Contains(evidence.Shared, c => c.Column == "environments" && c.Value == "/_V");
        Assert.Contains(evidence.NotMeasured, n => n.StartsWith("Speaker questions", StringComparison.Ordinal));
    }

    [Fact]
    public void EvidenceReadsParserEvidenceAtMostOnceAndReturnsBoundedLists()
    {
        var stems = UdKeepers();
        var reads = 0;
        var query = new QueryRecord(QuerySources.Stems, [new("final:2", "is", "ud")], [], Examples: 2, Change: DToR(), Evidence: true);
        var result = FormQuery.RunHypothesis(query, stems, Letters, Inputs(["x"], ["d"], ["h"], parse: () => { reads++; return null; }));
        Assert.Equal(1, reads);
        var evidence = result.Evidence!;
        Assert.True(evidence.Shared.Count <= 5);
        Assert.True(evidence.Examples.Count <= 2);
        Assert.True(evidence.Questions.Count <= 2);
        Assert.Contains(result.Notes!, n => n.Contains("no current parser evidence"));
    }

    [Fact]
    public void ACategoryContextIsExpressibleWhereverGraphemesAreKnown()
    {
        var stems = new List<QueryRow>();
        for (var i = 0; i < 300; i++) stems.Add(new QueryRow("a" + i, [[$"s{i:000}ad"], ["e" + i], ["V"], [], ["go"], ["1"], ["0"]]));
        for (var i = 0; i < 40; i++) stems.Add(new QueryRow("b" + i, [[$"k{i:000}od"], ["f" + i], ["Vb"], ["KEEP"], ["go"], ["1"], ["0"]]));
        var result = FormQuery.RunRows(Split(extra: "KEEP"), stems, []);
        var category = Context(result, "category", "Vb");
        Assert.Equal("yes", category.Expressible);
        Assert.Equal("not-checked", Context(result, "final:2", "od").Expressible);
    }
}

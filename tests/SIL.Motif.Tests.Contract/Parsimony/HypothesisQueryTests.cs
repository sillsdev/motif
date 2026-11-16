using SIL.Motif.Contract.Query;
using Xunit;

namespace SIL.Motif.Tests.Contract.Parsimony;

public sealed class HypothesisQueryTests
{
    private static QueryRow Stem(string id, string form, string[]? features = null, string entry = "") =>
        new(id, [[form], [entry.Length == 0 ? "e-" + id : entry], ["V"], features ?? [], ["go"], ["1"], ["0"]]);

    private static QueryHypothesisInputs Inputs(IEnumerable<string>? evidence = null, IEnumerable<string>? discovery = null,
        IEnumerable<string>? heldOut = null, IEnumerable<(string, string)>? forms = null, Func<QueryParseEvidence?>? parse = null) =>
        new([.. evidence ?? []], [.. discovery ?? []], [.. heldOut ?? []], [.. forms ?? []], parse);

    private static QueryHypothesis DToR(bool evidence = true) => new("d", "r", "final", ["an", "un", "on", "i", "a"],
        evidence ? ["union"] : []);

    private static QueryRecord Query(QueryHypothesis change, params string[] score) =>
        new(QuerySources.Stems, [new("final:2", "~", "^[aiu]d$")], [], Change: change, Score: score);

    // The universe of the #180 d>r example: 650 stems of which 179 show r, 5 both, 5 d only and 461 no evidence.
    private static (List<QueryRow> Stems, List<string> Union) Cebish()
    {
        var stems = new List<QueryRow>();
        var union = new List<string>();
        for (var i = 0; i < 650; i++)
        {
            var form = $"s{i:000}ad";
            var keep = i < 30 || i is >= 179 and < 184 ? new[] { "KEEPD" } : [];
            stems.Add(Stem("m" + i, form, keep));
            var r = form[..^1] + "r" + "an";
            if (i < 179) union.Add(r);
            else if (i < 184) { union.Add(r); union.Add(form + "un"); }
            else if (i < 189) union.Add(form + "un");
        }
        return (stems, union);
    }

    [Fact]
    public void ReliabilityToleranceAndEvidenceReproduceTheD2RExampleArithmetic()
    {
        var (stems, union) = Cebish();
        var result = FormQuery.RunHypothesis(Query(DToR(), "reliability", "tolerance") with { Examples = 3 }, stems, [], Inputs(union));
        var reliability = result.Scores!.Single(s => s.Scorer == "reliability");
        Assert.Equal(184, reliability.Counts["hits"]);
        Assert.Equal(189, reliability.Counts["scope"]);
        Assert.Equal(650, reliability.Counts["universe"]);
        Assert.Equal(0.9628, reliability.Numbers["lower"]!.Value, 3);
        Assert.Contains("sort key, not a verdict", reliability.Version);
        var tolerance = result.Scores!.Single(s => s.Scorer == "tolerance");
        Assert.Equal(650, tolerance.Counts["N"]);
        Assert.Equal(5, tolerance.Counts["e"]);
        Assert.Equal(100.4, tolerance.Numbers["threshold"]!.Value, 1);
        Assert.Equal("productive", tolerance.Verdict);
        Assert.Equal(3, result.Items!.Count);
        var groups = FormQuery.RunHypothesis(Query(DToR()) with { Group = ["evidence"] }, stems, [], Inputs(union)).Groups
            .ToDictionary(g => g.Key[0], g => g.Count);
        Assert.Equal(new Dictionary<string, int> { ["rule"] = 179, ["both"] = 5, ["base"] = 5, ["none"] = 461 }, groups);
    }

    [Fact]
    public void ToleranceCountsKeepTagsAsMsasAndRefusesToMixThemWithStems()
    {
        var (stems, union) = Cebish();
        var keep = new[] { new QueryCondition("features", "has", "KEEPD") };
        var tolerance = FormQuery.RunHypothesis(Query(DToR(), "tolerance") with { Exceptions = keep }, stems, [], Inputs(union)).Scores![0];
        Assert.Equal(35, tolerance.Counts["e"]);
        Assert.Equal("msa", tolerance.Unit);
        var mixed = Assert.Throws<QueryMixedUnitsException>(() => FormQuery.RunHypothesis(
            Query(DToR(), "tolerance") with { Exceptions = keep, Unit = "stem" }, stems, [], Inputs(union)));
        Assert.Contains("mix units", mixed.Message);
        Assert.Throws<QueryMixedUnitsException>(() => FormQuery.RunHypothesis(
            Query(DToR(), "control") with { Unit = "msa" }, stems, [], Inputs(union, ["x"], ["y"])));
    }

    [Fact]
    public void CollidesIsMeasuredPerPredictedFormAndNeverReadsNoneWithoutParserEvidence()
    {
        var stems = new[] { Stem("a", "kaad"), Stem("b", "kuud"), Stem("c", "kiid"), Stem("d", "kood") };
        var forms = new[] { ("kaaran", "other"), ("kuurun", "e-b") };
        QueryHypothesisInputs With(Func<QueryParseEvidence?>? parse) => Inputs(forms: forms, parse: parse);
        string[] Collides(QueryResult r, string id) => r.Items!.Single(i => i.Id == id).Values["collides"];
        var query = new QueryRecord(QuerySources.Stems, [], [], Change: new("d", "r", "final", ["an", "un"], []));
        var none = FormQuery.RunHypothesis(query, stems, [], With(null));
        Assert.Equal(["entry"], Collides(none, "a"));
        Assert.Equal(["not-measured"], Collides(none, "b"));
        Assert.Equal(["not-measured"], Collides(none, "d"));
        Assert.Contains(none.Notes!, n => n.Contains("no current parser evidence"));
        Assert.Contains(none.Notes!, n => n.Contains("no --wordset"));
        var parsed = new QueryParseEvidence(new Dictionary<string, bool>
            { ["kuurun"] = true, ["kuuran"] = false, ["kooran"] = false, ["koorun"] = false }, Stale: true);
        var some = FormQuery.RunHypothesis(query, stems, [], With(() => parsed));
        Assert.Equal(["parses"], Collides(some, "b"));
        Assert.Equal(["none"], Collides(some, "d"));
        Assert.Equal(["not-measured"], Collides(some, "c"));
        Assert.Contains(some.Notes!, n => n.Contains("stale"));
        Assert.Contains(some.Notes!, n => n.Contains("1 of 4 rows"));
    }

    [Fact]
    public void ParserEvidenceIsReadOnlyWhenCollidesIsEvaluated()
    {
        var (stems, union) = Cebish();
        var reads = 0;
        QueryHypothesisInputs Counting() => Inputs(union, parse: () => { reads++; return null; });
        FormQuery.RunHypothesis(Query(DToR(), "reliability", "tolerance") with { Examples = 0 }, stems, [], Counting());
        Assert.Equal(0, reads);
        FormQuery.RunHypothesis(Query(DToR(), "reliability") with { Examples = 2 }, stems, [], Counting());
        Assert.Equal(1, reads);
    }

    [Fact]
    public void RefusesWhatItCannotMeasure()
    {
        var (stems, union) = Cebish();
        Assert.Throws<QueryNotMeasuredException>(() => FormQuery.RunHypothesis(Query(DToR(evidence: false), "reliability"), stems, [], Inputs()));
        Assert.Throws<QueryNotMeasuredException>(() => FormQuery.RunHypothesis(Query(DToR(evidence: false), "tolerance"), stems, [], Inputs()));
        Assert.Throws<QueryNotMeasuredException>(() => FormQuery.RunHypothesis(Query(DToR(), "control"), stems, [], Inputs(union)));
        Assert.Throws<ArgumentException>(() => FormQuery.RunHypothesis(Query(DToR(), "reliability") with { Group = ["evidence"] }, stems, [], Inputs(union)));
        Assert.Throws<ArgumentException>(() => FormQuery.RunHypothesis(Query(DToR(), "bogus"), stems, [], Inputs(union)));
        Assert.Throws<ArgumentException>(() => FormQuery.RunRows(Query(DToR(), "reliability"), stems, []));
        Assert.Throws<ArgumentException>(() => FormQuery.RunHypothesis(Query(new("", "r", "final", ["an"], [])), stems, [], Inputs()));
        var unevidenced = FormQuery.RunHypothesis(new QueryRecord(QuerySources.Stems, [], [], Examples: 1, Change: DToR(evidence: false)), stems, [], Inputs());
        Assert.Equal(["not-measured"], unevidenced.Items![0].Values["evidence"]);
    }

    [Fact]
    public void ControlMatchesControlTestPyOnTheSameInputsAndReturnsCountsOnly()
    {
        string[] bases = ["k", "t", "b", "s", "l", "p", "m", "n", "g", "h", "r", "w", "y", "d", "pak", "tub", "sul", "mag", "nib", "hul", "gab", "bat", "lip", "sab"];
        var stems = bases.Select((b, i) => Stem("m" + i, b + "ad")).ToList();
        var heldOut = new HashSet<string> { "xqz" };
        var discovery = new HashSet<string>();
        for (var i = 0; i < bases.Length; i++)
        {
            var b = bases[i] + "a";
            heldOut.Add(b + "ran");
            if (i % 3 == 0) heldOut.Add(b + "lan");
            if (i % 4 == 0) heldOut.Add(b + "nan");
            if (i % 5 == 0) discovery.Add(b + "ran");
            if (i % 2 == 0) heldOut.Add(b + "ri");
            discovery.Add(bases[i] + "adun");
        }
        var change = new QueryHypothesis("d", "r", "final", ["an", "i"], [], ["disc"], ["held"]);
        var result = FormQuery.RunHypothesis(new QueryRecord(QuerySources.Stems, [], [], Change: change, Score: ["control"], Examples: 0),
            stems, [], Inputs(discovery: discovery, heldOut: heldOut));
        var control = result.Scores!.Single();
        // Expected values are the output of the skill's control_test.py on these inputs.
        Assert.Equal(48, control.Counts["predicted"]);
        Assert.Equal(31, control.Counts["rule_hits"]);
        Assert.Equal(new Dictionary<string, long> { ["l"] = 8, ["n"] = 6, ["b"] = 0, ["g"] = 0, ["s"] = 0 }, control.Controls);
        Assert.Equal(23, control.Counts["margin"]);
        Assert.Equal("PASS", control.Verdict);
        Assert.DoesNotContain("xqz", System.Text.Json.JsonSerializer.Serialize(result));
    }

    [Fact]
    public void RowsOutsideTheChangeAreNotInTheUniverseAndInitialChangesPrefix()
    {
        var stems = new[] { Stem("a", "dapa"), Stem("b", "tapa") };
        var change = new QueryHypothesis("d", "r", "initial", ["ma-"], ["w"]);
        var result = FormQuery.RunHypothesis(new QueryRecord(QuerySources.Stems, [], [], Change: change), stems, [], Inputs(["marapa"]));
        var row = Assert.Single(result.Items!);
        Assert.Equal("a", row.Id);
        Assert.Equal(["marapa"], row.Values["predicted"]);
        Assert.Equal(["rule"], row.Values["evidence"]);
    }
}

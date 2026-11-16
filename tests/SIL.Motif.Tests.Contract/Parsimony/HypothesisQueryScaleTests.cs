using System.Diagnostics;
using System.Globalization;
using SIL.Motif.Contract.Query;
using SIL.Motif.Tests.QueryScale;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.Contract.Parsimony;

/// <summary>Scale and real-data checks of the hypothesised-change query; both skip outside their own switch.</summary>
[Trait("Category", "SlowScale")]
public sealed class HypothesisQueryScaleTests(ITestOutputHelper output)
{
    private static QueryRow Stem(string id, string form, string tag, int msas) =>
        new(id, [[form], ["e-" + id], ["Verb"], tag.Length == 0 ? [] : [tag], ["g"], [msas.ToString(CultureInfo.InvariantCulture)], ["0"]]);

    [SlowScaleFact]
    public void ThirtyFourThousandStemsAgainstSixtyThousandWordsEvaluateWithinThreeHundredMilliseconds()
    {
        var stems = new List<QueryRow>();
        var words = new List<string>();
        var forms = new List<(string, string)>();
        var random = new Random(7);
        const string letters = "abdgiklmnoprstuw";
        for (var i = 0; i < 34000; i++)
        {
            var form = new string(Enumerable.Range(0, 4).Select(_ => letters[random.Next(letters.Length)]).ToArray()) + i.ToString("x") + (i % 3 == 0 ? "ad" : "ek");
            stems.Add(Stem("m" + i, form, "", 1));
            forms.Add((form, "e-m" + i));
            if (i % 2 == 0 && words.Count < 60000) words.Add(form[..^1] + "ran");
        }
        var change = new QueryHypothesis("d", "r", "final", ["an", "un", "on", "i", "a"], ["u"], ["disc"], ["held"]);
        var query = new QueryRecord(QuerySources.Stems, [], [], Change: change, Score: ["reliability", "tolerance", "control"], Examples: 5);
        var inputs = new QueryHypothesisInputs(words, words.Take(1000).ToList(), words.Skip(1000).Take(20000).ToList(), forms, null);
        FormQuery.RunHypothesis(query, stems, [], inputs);
        var clock = Stopwatch.StartNew();
        var result = FormQuery.RunHypothesis(query, stems, [], inputs);
        clock.Stop();
        output.WriteLine($"evaluate {clock.ElapsedMilliseconds} ms over {stems.Count} stems, {words.Count} words");
        Assert.NotNull(result.Scores);
        Assert.True(clock.ElapsedMilliseconds <= 300, $"{clock.ElapsedMilliseconds} ms");
    }

    // Runs only with exported CEB files in MOTIF_QUERY_REPRO_DIR.
    [Fact]
    public void TheD2RFiguresOnTheExportedCebLexiconMatchTheReferenceScripts()
    {
        var dir = Environment.GetEnvironmentVariable("MOTIF_QUERY_REPRO_DIR");
        if (dir is null || !File.Exists(Path.Combine(dir, "stems.tsv"))) return;
        var stems = File.ReadLines(Path.Combine(dir, "stems.tsv")).Select(l => l.Split('\t'))
            .Select(p => new QueryRow(p[0], [[p[2].Trim()], [p[1]], [p[3]], p[4].Length == 0 ? [] : [p[4]], ["g"], [p[5]], ["0"]])).ToList();
        var forms = File.ReadLines(Path.Combine(dir, "forms.tsv")).Select(l => l.Split('\t')).Select(p => (p[1], p[0])).ToList();
        List<string> Words(string name) => [.. File.ReadAllLines(Path.Combine(dir, name)).Where(l => l.Length > 0)];
        var change = new QueryHypothesis("d", "r", "final", ["an", "un", "on", "i", "a"], ["union"], ["disc"], ["held"]);
        var inputs = new QueryHypothesisInputs(Words("union.txt"), Words("discovery.txt"), Words("heldout.txt"), forms, null);
        QueryCondition[] universe = [new("category", "in", "Verb,Noun,Adjective"), new("form", "~", "(?i)^[a-z]{2,}[aeiou]d$")];
        var scored = FormQuery.RunHypothesis(new QueryRecord(QuerySources.Stems, universe, [], Change: change, Examples: 0,
            Score: ["reliability", "tolerance", "control"]), stems, [], inputs);
        var byUnit = FormQuery.RunHypothesis(new QueryRecord(QuerySources.Stems, universe, [], Change: change, Examples: 0,
            Score: ["reliability"], Unit: "stem"), stems, [], inputs).Scores![0];
        var msa = scored.Scores!.Single(s => s.Scorer == "reliability");
        var tolerance = scored.Scores!.Single(s => s.Scorer == "tolerance");
        var control = scored.Scores!.Single(s => s.Scorer == "control");
        var tagged = FormQuery.RunHypothesis(new QueryRecord(QuerySources.Stems, universe, [], Change: change, Examples: 0,
            Score: ["tolerance"], Exceptions: [new("features", "has", "D>R")]), stems, [], inputs).Scores![0];
        var stemTolerance = FormQuery.RunHypothesis(new QueryRecord(QuerySources.Stems, universe, [], Change: change, Examples: 0,
            Score: ["tolerance"], Unit: "stem"), stems, [], inputs).Scores![0];
        output.WriteLine($"stem tolerance N={stemTolerance.Counts["N"]} e={stemTolerance.Counts["e"]} threshold {stemTolerance.Numbers["threshold"]:0.0} {stemTolerance.Verdict}");
        int Ids(params QueryCondition[] more) => FormQuery.RunHypothesis(new QueryRecord(QuerySources.Stems, [.. universe, .. more], [],
            Change: change, Examples: 0, Ids: true), stems, [], inputs).Ids.Count;
        var collidesAll = Ids(new QueryCondition("collides", "is", "entry"));
        var collidesTagged = Ids(new QueryCondition("collides", "is", "entry"), new QueryCondition("features", "has", "D>R"));
        output.WriteLine($"stem reliability {byUnit.Counts["hits"]}/{byUnit.Counts["scope"]} lower {byUnit.Numbers["lower"]:0.0000}");
        output.WriteLine($"msa reliability {msa.Counts["hits"]}/{msa.Counts["scope"]} of {msa.Counts["universe"]}");
        output.WriteLine($"tolerance (evidence) N={tolerance.Counts["N"]} e={tolerance.Counts["e"]}; (D>R tag) e={tagged.Counts["e"]} {tagged.Verdict}");
        output.WriteLine($"control predicted {control.Counts["predicted"]} hits {control.Counts["rule_hits"]} controls {string.Join(",", control.Controls!.Select(p => p.Key + "=" + p.Value))} {control.Verdict}");
        output.WriteLine($"collides=entry msas {collidesAll}, of which tagged D>R {collidesTagged}");
        Assert.Equal(182, byUnit.Counts["hits"]);
        Assert.Equal(186, byUnit.Counts["scope"]);
        Assert.Equal(10, control.Counts["rule_hits"]);
        Assert.Equal(3285, control.Counts["predicted"]);
        Assert.Equal(new Dictionary<string, long> { ["l"] = 2, ["n"] = 4, ["g"] = 3, ["m"] = 0, ["p"] = 3 }, control.Controls);
        Assert.Equal(1111, tolerance.Counts["N"]);
        Assert.Equal(318, tagged.Counts["e"]);
        Assert.Equal(657, stemTolerance.Counts["N"]);
        Assert.Equal("productive", stemTolerance.Verdict);
        Assert.Equal(146, collidesAll);
        Assert.Equal(82, collidesTagged);
    }
}

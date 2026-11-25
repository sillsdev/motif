using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Query.Corpus;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary><c>--baseline-grammar</c>: two parses of one corpus compared word by word as <c>change</c>.</summary>
public sealed class BaselineCompareQueryTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("baseline-query").FullName;

    public void Dispose() => Directory.Delete(_folder, true);

    private static string Guid(string text) => new System.Guid(MD5.HashData(Encoding.UTF8.GetBytes(text))).ToString("D");

    // A reading is a list of morph texts; the ids come from the texts, so the same morphs make the same reading.
    private static string Line(string word, bool capped, bool timedOut, params string[][] readings)
    {
        var analyses = new JsonArray();
        foreach (var reading in readings)
        {
            var morphs = new JsonArray();
            foreach (var text in reading) morphs.Add(new JsonObject { ["form"] = Guid("f" + text), ["msa"] = Guid("m" + text), ["inflType"] = null, ["guessedString"] = null });
            analyses.Add(new JsonObject { ["morphs"] = morphs });
        }
        return new JsonObject
        {
            ["schema"] = "fieldworks-parse-analysis/v1", ["index"] = 0, ["word"] = word, ["elapsedMs"] = 0,
            ["capped"] = capped, ["timedOut"] = timedOut, ["invalidShape"] = false, ["analyses"] = analyses, ["unavailable"] = new JsonArray(),
        }.ToJsonString();
    }

    private static string[] R(params string[] morphs) => morphs;

    private static readonly string[] Words = ["gain", "lose", "more", "fewer", "swap", "same", "none", "capnow", "capbase", "failnow", "slow"];

    private static readonly long[] Counts = [100, 50, 20, 10, 8, 6, 4, 3, 2, 1, 1];

    // Each grammar's recording is chosen by the text of its file.
    private static readonly Dictionary<string, string[]> Recordings = new()
    {
        ["base"] =
        [
            Line("gain", false, false), Line("lose", false, false, R("a")), Line("more", false, false, R("a")),
            Line("fewer", false, false, R("a"), R("b")), Line("swap", false, false, R("a")), Line("same", false, false, R("a"), R("b")),
            Line("none", false, false), Line("capnow", false, false), Line("capbase", true, false, R("a")), Line("failnow", false, false, R("a")),
            Line("slow", false, true, R("a")),
        ],
        ["now"] =
        [
            Line("gain", false, false, R("a")), Line("lose", false, false), Line("more", false, false, R("a"), R("b")),
            Line("fewer", false, false, R("a")), Line("swap", false, false, R("c")), Line("same", false, false, R("b"), R("a")),
            Line("none", false, false), Line("capnow", true, false), Line("capbase", false, false, R("a")),
            Line("slow", false, false, R("a")),
        ],
    };

    private sealed class Recorded : IRealizesIndexBuilder
    {
        public string Identity { get; } = "compare-recording".PadRight(16, '0');

        public List<string> Built { get; } = [];

        public string Build(string grammarPath, IReadOnlyList<string> words, CancellationToken cancellationToken)
        {
            var kind = File.ReadAllText(grammarPath);
            Built.Add(kind);
            var path = Path.Combine(Directory.CreateTempSubdirectory("recorded-compare").FullName, "analyses.jsonl");
            File.WriteAllText(path, string.Join("\n", Recordings[kind]) + "\n");
            return path;
        }
    }

    private readonly Recorded _builder = new();

    private string Grammar(string name, string kind)
    {
        var path = Path.Combine(_folder, name + ".fwdata");
        File.WriteAllText(path, kind);
        return path;
    }

    private string Corpus()
    {
        var path = Path.Combine(_folder, "words.tsv");
        File.WriteAllText(path, string.Concat(Words.Select((word, position) => word + "\t" + Counts[position] + "\n")));
        return path;
    }

    private CommandOutcome<CorpusFormsResponse> Run(string? where, string? groupBy = null, string? sum = null, string? baseline = "base", string? grammar = "now", int? limit = null) =>
        CorpusQueryCommand.Forms(new CorpusFormsRequest(Corpus(), where, null, grammar is null ? null : Grammar("g-" + grammar, grammar), GroupBy: groupBy, Sum: sum, Limit: limit,
            BaselineGrammarPath: baseline is null ? null : Grammar("b-" + baseline, baseline)), null, default, _builder, Path.Combine(_folder, "cache"));

    private static CorpusFormsResponse Ok(CommandOutcome<CorpusFormsResponse> outcome) => outcome.Refusal is null
        ? outcome.Value!
        : throw new Xunit.Sdk.XunitException(outcome.Refusal.Code + ": " + outcome.Refusal.Message);

    private static string[] Forms(CorpusFormsResponse response) => [.. response.Rows.Select(row => row.Form).Order(StringComparer.Ordinal)];

    [Theory]
    [InlineData("gained", "gain")]
    [InlineData("lost", "lose")]
    [InlineData("more-readings", "more")]
    [InlineData("fewer-readings", "fewer")]
    [InlineData("readings-changed", "swap")]
    [InlineData("same", "none,same")]
    [InlineData("unknown", "capbase,capnow,failnow,slow")]
    public void EachKindOfChangeFindsItsWords(string change, string expected)
    {
        var response = Ok(Run($"change = \"{change}\""));
        Assert.Equal(expected.Split(','), Forms(response));
        Assert.All(response.Rows, row => Assert.Equal(change, row.Baseline!.Change));
    }

    [Fact]
    public void ARowCarriesTheBaselineParseAndTheTargetsOwn()
    {
        var response = Ok(Run("form = more"));
        var row = Assert.Single(response.Rows);
        Assert.Equal(("parsed", 2), (row.ParseState, row.Readings));
        Assert.Equal(("parsed", 1, "more-readings"), (row.Baseline!.ParseState, row.Baseline.Readings, row.Baseline.Change));
        Assert.Equal(["failnow", "lose"], Forms(Ok(Run("base.readings = 1 and readings = 0"))));
        Assert.Equal(["gain", "none"], Forms(Ok(Run("base.parse.state = none"))).Where(form => form is "gain" or "none").ToArray());
    }

    [Fact]
    public void AWordEitherParseDidNotFinishIsUnknownNeverGainedOrLost()
    {
        var response = Ok(Run(null, groupBy: "change", sum: "tokens"));
        var groups = response.Groups!.Groups.ToDictionary(group => group.Key);
        Assert.Equal((1, 100L), (groups["gained"].Types, groups["gained"].Tokens));
        Assert.Equal((1, 50L), (groups["lost"].Types, groups["lost"].Tokens));
        Assert.Equal((4, 7L), (groups["unknown"].Types, groups["unknown"].Tokens));
        Assert.Equal((2, 10L), (groups["same"].Types, groups["same"].Tokens));
        Assert.Equal(11, response.Totals!.Types);
        Assert.Equal(["change"], response.Groups.Columns);
    }

    [Fact]
    public void UnknownWordsMakeTheResultIncompleteAndSaySoInTheNote()
    {
        var response = Ok(Run("change = gained"));
        Assert.False(response.ReadingsInfo!.Complete);
        Assert.Contains("change = unknown", response.ReadingsInfo.Note);
        Assert.Contains("1 words stopped at the work limit, 1 at the word timeout", response.ReadingsInfo.Note);
        Assert.False(response.BaselineInfo!.Complete);
        Assert.Equal((1, 1), (response.BaselineInfo.Capped, response.BaselineInfo.TimedOut));
    }

    [Fact]
    public void ACleanComparisonIsCompleteAndEachGrammarIsParsedOnceWhateverTheNumberOfRuns()
    {
        Recordings["cleanbase"] = [.. Words.Select(word => Line(word, false, false, R("a")))];
        Recordings["cleannow"] = [.. Words.Select(word => Line(word, false, false, R("a"), R("b")))];
        var first = Ok(Run("change = more-readings", baseline: "cleanbase", grammar: "cleannow"));
        Assert.Equal(11, first.Matched);
        Assert.True(first.ReadingsInfo!.Complete);
        Assert.True(first.BaselineInfo!.Complete);
        Ok(Run("change = same", baseline: "cleanbase", grammar: "cleannow"));
        Assert.Equal(["cleanbase", "cleannow"], [.. _builder.Built.Order(StringComparer.Ordinal)]);
    }

    [Fact]
    public void ARowOfAJsonQueryNamesTheChange()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(Ok(Run("change = lost")), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.Contains("\"baseline\":{\"parseState\":\"parsed\",\"readings\":1,\"change\":\"lost\"}", json);
        Assert.Contains("\"baselineInfo\"", json);
    }

    [Fact]
    public void TheSameGrammarAsBaselineAndTargetIsRefused()
    {
        var path = Grammar("same", "now");
        var outcome = CorpusQueryCommand.Forms(new CorpusFormsRequest(Corpus(), "change = lost", null, path, BaselineGrammarPath: path), null, default, _builder, Path.Combine(_folder, "cache"));
        Assert.Equal(RefusalCodes.QueryBaselineSameGrammar, outcome.Refusal!.Code);
        Assert.Empty(_builder.Built);
    }

    [Fact]
    public void ABaselineNeedsAGrammarAndTheColumnsNeedABaseline()
    {
        Assert.Equal(RefusalCodes.QueryReadingsNeedsGrammar, Run("change = lost", grammar: null).Refusal!.Code);
        var columns = Run("change = lost", baseline: null);
        Assert.Equal(RefusalCodes.QueryCorpusInvalid, columns.Refusal!.Code);
        Assert.Contains("--baseline-grammar", columns.Refusal.Message);
        Assert.Equal(RefusalCodes.QueryCorpusInvalid, Run(null, groupBy: "change", baseline: null).Refusal!.Code);
    }

    [Fact]
    public void ABaselineThatCannotBeParsedIsRefusedNotAnsweredAsAllLost()
    {
        var outcome = CorpusQueryCommand.Forms(new CorpusFormsRequest(Corpus(), "change = lost", null, Grammar("g", "now"), BaselineGrammarPath: Grammar("x", "missing")),
            null, default, new FailingBaseline(), Path.Combine(_folder, "cache"));
        Assert.Equal(RefusalCodes.QueryRealizesIndexFailed, outcome.Refusal!.Code);
    }

    private sealed class FailingBaseline : IRealizesIndexBuilder
    {
        public string Identity { get; } = "failing-recording".PadRight(16, '0');

        public string Build(string grammarPath, IReadOnlyList<string> words, CancellationToken cancellationToken)
        {
            if (File.ReadAllText(grammarPath) == "missing") throw new RealizesIndexException("the baseline batch failed");
            var path = Path.Combine(Directory.CreateTempSubdirectory("recorded-compare").FullName, "analyses.jsonl");
            File.WriteAllText(path, string.Join("\n", Recordings["now"]) + "\n");
            return path;
        }
    }
}

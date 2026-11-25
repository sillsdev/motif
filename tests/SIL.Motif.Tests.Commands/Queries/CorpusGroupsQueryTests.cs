using System.Text.Json.Nodes;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Query.Corpus;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary><c>--group-by &lt;column&gt;</c>, <c>--sum tokens</c>, <c>--sample</c>, <c>--write-words</c> and several corpus sources over a plain word list.</summary>
public sealed class CorpusGroupsQueryTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("corpus-groups").FullName;

    public void Dispose() => Directory.Delete(_folder, true);

    private string Write(string name, string text)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, text);
        return path;
    }

    private string Listed() => Write("listed.tsv", "form\tcount\tstate\n" + string.Concat(Row("kaon", 5, "parsed"), Row("kaonan", 3, "parsed"), Row("kaonin", 1, "unparsed"),
        Row("baha", 2, "unparsed"), Row("bahaan", 4, "parsed"), Row("lis", 1, "")));

    private static string Row(string form, int count, string state) => form + "\t" + count + "\t" + state + "\n";

    private static CommandOutcome<CorpusFormsResponse> Forms(CorpusFormsRequest request) => CorpusQueryCommand.Forms(request);

    [Fact]
    public void GroupByAColumnCountsTypesAndTokensPerValueAndTotalsTheMatches()
    {
        var outcome = Forms(new CorpusFormsRequest(Listed(), GroupBy: "parse.state"));
        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var response = outcome.Value!;
        Assert.Equal(new[] { ("parsed", 3, 12L), ("unparsed", 2, 3L), ("(none)", 1, 1L) }, response.Groups!.Groups.Select(group => (group.Key, group.Types, group.Tokens)));
        Assert.Equal(new TypesTokens(6, 16), response.Totals);
        Assert.Equal(6, response.Matched);
        Assert.Empty(response.Rows);
    }

    [Fact]
    public void AWhereNarrowsTheGroupsAndTwoColumnsKeyAsAPair()
    {
        var response = Forms(new CorpusFormsRequest(Listed(), "form ~ \"kaon.*\"", GroupBy: "parse.state,syllables")).Value!;
        Assert.All(response.Groups!.Groups, group => Assert.Equal(2, group.Values.Count));
        Assert.Equal(3, response.Totals!.Types);
        Assert.Equal(9L, response.Totals.Tokens);
        Assert.Equal(new[] { "parsed | 2", "parsed | 3", "unparsed | 3" }.Order(StringComparer.Ordinal), response.Groups.Groups.Select(group => group.Key).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ALimitWithAGroupingListsRowsToo()
    {
        var response = Forms(new CorpusFormsRequest(Listed(), GroupBy: "shape", Limit: 2)).Value!;
        Assert.Equal(2, response.Rows.Count);
    }

    [Fact]
    public void ABadGroupingIsRefusedAndReadingsNeedAGrammar()
    {
        Assert.Contains("word", Forms(new CorpusFormsRequest(Listed(), GroupBy: "word")).Refusal!.Message);
        Assert.Equal(RefusalCodes.QueryCorpusInvalid, Forms(new CorpusFormsRequest(Listed(), GroupBy: "cause,shape")).Refusal!.Code);
        Assert.Equal(RefusalCodes.QueryCorpusInvalid, Forms(new CorpusFormsRequest(Listed(), GroupBy: "shape,shape")).Refusal!.Code);
        Assert.Equal(RefusalCodes.QueryCorpusInvalid, Forms(new CorpusFormsRequest(Listed(), GroupBy: "a,b,c")).Refusal!.Code);
        Assert.Contains("grammar", Forms(new CorpusFormsRequest(Listed(), GroupBy: "readings")).Refusal!.Message);
        Assert.Equal(RefusalCodes.QueryCorpusInvalid, Forms(new CorpusFormsRequest(Listed(), GroupBy: "shape", Sum: "types")).Refusal!.Code);
    }

    [Fact]
    public void SumTokensAloneGivesTheTotalsOfTheMatches()
    {
        var response = Forms(new CorpusFormsRequest(Listed(), "form ~ \"kaon.*\"", Sum: "tokens")).Value!;
        Assert.Equal(new TypesTokens(3, 9), response.Totals);
        Assert.Null(response.Groups);
        Assert.Null(Forms(new CorpusFormsRequest(Listed(), "form ~ \"kaon.*\"")).Value!.Totals);
    }

    private string Many()
    {
        var builder = new System.Text.StringBuilder("form\tcount\n");
        for (var i = 0; i < 60; i++) builder.Append("w").Append(i.ToString("D2")).Append('\t').Append(i == 0 ? 1000 : 1 + i % 3).Append('\n');
        return Write("many.tsv", builder.ToString());
    }

    [Fact]
    public void ASampleIsDeterministicBySeedAndIgnoresFrequency()
    {
        var first = Forms(new CorpusFormsRequest(Many(), "tokens > 0", Sample: 10, Seed: 7)).Value!;
        var again = Forms(new CorpusFormsRequest(Many(), "tokens > 0", Sample: 10, Seed: 7)).Value!;
        var other = Forms(new CorpusFormsRequest(Many(), "tokens > 0", Sample: 10, Seed: 8)).Value!;
        Assert.Equal(first.Rows.Select(row => row.Form), again.Rows.Select(row => row.Form));
        Assert.NotEqual(first.Rows.Select(row => row.Form).Order(StringComparer.Ordinal), other.Rows.Select(row => row.Form).Order(StringComparer.Ordinal));
        Assert.Equal(10, first.Rows.Count);
        Assert.Equal(60, first.Matched);
        Assert.Equal(new SampleInfo(10, 7, 60), first.Sample);
        // The most frequent word is not favoured: across seeds it is in about one sample in six.
        var hits = Enumerable.Range(0, 60).Count(seed => Forms(new CorpusFormsRequest(Many(), "tokens > 0", Sample: 10, Seed: seed)).Value!.Rows.Any(row => row.Form == "w00"));
        Assert.InRange(hits, 3, 25);
        var sorted = first.Rows.Select(row => row.Tokens).ToArray();
        Assert.Equal(sorted.OrderByDescending(x => x), sorted);
    }

    [Fact]
    public void ASampleKeepsGroupsAndTotalsOverEveryMatchAndASmallMatchSetWhole()
    {
        var response = Forms(new CorpusFormsRequest(Many(), GroupBy: "shape", Sample: 5, Seed: 1)).Value!;
        Assert.Equal(60, response.Totals!.Types);
        Assert.Equal(5, response.Rows.Count);
        var small = Forms(new CorpusFormsRequest(Listed(), Sample: 50)).Value!;
        Assert.Equal(6, small.Rows.Count);
        Assert.Equal(6, Forms(new CorpusFormsRequest(Many(), Sample: 150, Limit: 6)).Value!.Rows.Count);
        Assert.Equal(RefusalCodes.QueryCorpusInvalid, Forms(new CorpusFormsRequest(Listed(), Sample: 0)).Refusal!.Code);
        Assert.Equal(RefusalCodes.QueryCorpusInvalid, Forms(new CorpusFormsRequest(Listed(), Seed: 3)).Refusal!.Code);
    }

    [Fact]
    public void WriteWordsWritesEveryMatchNotJustTheLimitAndRefusesToOverwriteWithoutForce()
    {
        var file = Path.Combine(_folder, "out", "words.txt");
        var outcome = Forms(new CorpusFormsRequest(Listed(), "form ~ \"kaon.*\"", Limit: 1, WriteWords: file));
        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        Assert.Equal(["kaon", "kaonan", "kaonin"], File.ReadAllLines(file));
        Assert.Equal(new WordsWritten(file, 3, false), outcome.Value!.Written);
        Assert.Single(outcome.Value.Rows);
        var refused = Forms(new CorpusFormsRequest(Listed(), "form ~ \"bah.*\"", WriteWords: file));
        Assert.Equal(RefusalCodes.QueryWriteWordsExists, refused.Refusal!.Code);
        Assert.Equal(["kaon", "kaonan", "kaonin"], File.ReadAllLines(file));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(file)!, "*.tmp-*"));
        Assert.True(Forms(new CorpusFormsRequest(Listed(), "form ~ \"bah.*\"", WriteWords: file, Force: true, WithCounts: true)).Succeeded);
        Assert.Equal(["bahaan\t4", "baha\t2"], File.ReadAllLines(file));
        Assert.DoesNotContain((byte)0xEF, File.ReadAllBytes(file).Take(1));
    }

    [Fact]
    public void WriteWordsWritesTheSampleWhenOneIsAsked()
    {
        var file = Path.Combine(_folder, "sample.txt");
        var response = Forms(new CorpusFormsRequest(Many(), Sample: 7, Seed: 3, WriteWords: file)).Value!;
        Assert.Equal(response.Rows.Select(row => row.Form), File.ReadAllLines(file));
        Assert.Equal(RefusalCodes.QueryCorpusInvalid, Forms(new CorpusFormsRequest(Listed(), WithCounts: true)).Refusal!.Code);
    }

    [Fact]
    public void SeveralSourcesAreOneCorpusWithSummedCountsAndEachSourceNamed()
    {
        var weights = Write("a.weights.json", "{\"kaon\":10,\"zed\":2}");
        var other = Write("b.weights.json", "{\"kaon\":5,\"yak\":1}");
        var joined = string.Join(",", weights, other, Listed());
        var response = Forms(new CorpusFormsRequest(joined, "form = \"kaon\"")).Value!;
        Assert.Equal(20, response.Rows[0].Tokens);
        Assert.Equal(["weights.json", "weights.json", "tsv"], response.Corpus.Sources!.Select(source => source.Kind));
        Assert.Equal(new[] { (2, 12L), (2, 6L), (6, 16L) }, response.Corpus.Sources!.Select(source => (source.Types, source.Tokens)));
        Assert.Equal(8, response.Corpus.Types);
        Assert.Equal(34, response.Corpus.Tokens);
        Assert.Equal(response.Corpus.Source, joined);
        var newline = Forms(new CorpusFormsRequest(weights + "\n" + other, Sum: "tokens")).Value!;
        Assert.Equal(new TypesTokens(3, 18), newline.Totals);
        Assert.Null(Forms(new CorpusFormsRequest(weights, Sum: "tokens")).Value!.Corpus.Sources);
    }

    [Fact]
    public void ABadWeightsFileNamesItselfAndAMissingSourceIsRefused()
    {
        var bad = Write("bad.weights.json", "{\"kaon\":\"many\"}");
        Assert.Contains("bad.weights.json", Forms(new CorpusFormsRequest(bad, Sum: "tokens")).Refusal!.Message);
        Assert.Equal(RefusalCodes.QueryCorpusUnreadable, Forms(new CorpusFormsRequest(Write("w.json", "[1]"), Sum: "tokens")).Refusal!.Code);
        Assert.Equal(RefusalCodes.QueryCorpusUnreadable, Forms(new CorpusFormsRequest(Listed() + "," + Path.Combine(_folder, "nope.tsv"), Sum: "tokens")).Refusal!.Code);
    }

    private static string Line(string word, params string[][] readings)
    {
        var analyses = new JsonArray();
        foreach (var reading in readings)
        {
            var morphs = new JsonArray();
            var spans = new JsonArray();
            var at = 0;
            for (var item = 0; item < reading.Length; item++)
            {
                var id = Guid.Parse($"00000000-0000-0000-0000-0000000000{item + 1:x2}").ToString("D");
                morphs.Add(new JsonObject { ["form"] = id, ["msa"] = id, ["inflType"] = null, ["guessedString"] = null });
                var end = at + reading[item].Length;
                spans.Add(new JsonObject
                {
                    ["item"] = item, ["reason"] = null, ["rule"] = null,
                    ["spans"] = new JsonArray(new JsonObject { ["offsets"] = new JsonArray(at, end), ["segments"] = new JsonArray(at, end), ["text"] = reading[item] }),
                });
                at = end;
            }
            analyses.Add(new JsonObject
            {
                ["morphs"] = morphs,
                ["morphSpans"] = new JsonObject { ["morphemes"] = spans, ["segmentCount"] = at + 1, ["surface"] = word, ["unattributed"] = new JsonArray() },
            });
        }
        return new JsonObject
        {
            ["schema"] = "fieldworks-parse-analysis/v1", ["index"] = 0, ["word"] = word, ["elapsedMs"] = 0,
            ["capped"] = false, ["timedOut"] = false, ["invalidShape"] = false, ["analyses"] = analyses, ["unavailable"] = new JsonArray(),
        }.ToJsonString();
    }

    private sealed class Recorded(string recording) : IRealizesIndexBuilder
    {
        public string Identity { get; } = "groups-recorded".PadRight(16, '0');

        public string Build(string grammarPath, IReadOnlyList<string> words, CancellationToken cancellationToken)
        {
            var path = Path.Combine(Directory.CreateTempSubdirectory("recorded-groups").FullName, "analyses.jsonl");
            File.WriteAllText(path, recording);
            return path;
        }
    }

    [Fact]
    public void ParseStateAndReadingsComeFromTheGrammarsParseWhenOneIsGiven()
    {
        // kaonan parses once, kaonin twice, the rest got no answer; the list's own states are ignored.
        var recording = string.Join("\n", Line("kaonan", ["kaon", "an"]), Line("kaonin", ["kaon", "in"], ["kaonin"])) + "\n";
        var grammar = Write("g.json", "g");
        CommandOutcome<CorpusFormsResponse> Go(string group, string? where = null) => CorpusQueryCommand.Forms(
            new CorpusFormsRequest(Listed(), where, GrammarPath: grammar, GroupBy: group, Sum: "tokens"), null, default, new Recorded(recording), Path.Combine(_folder, "cache"));
        var state = Go("parse.state");
        Assert.True(state.Succeeded, state.Refusal?.Message);
        Assert.Equal(new[] { ("failed", 4, 12L), ("parsed", 2, 4L) }, state.Value!.Groups!.Groups.Select(group => (group.Key, group.Types, group.Tokens)));
        Assert.Equal(new TypesTokens(6, 16), state.Value.Totals);
        var readings = Go("readings", "form ~ \"kaon.*\"");
        Assert.Equal(new[] { ("0", 1, 5L), ("1", 1, 3L), ("2", 1, 1L) }, readings.Value!.Groups!.Groups.OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => (group.Key, group.Types, group.Tokens)));
        Assert.NotNull(readings.Value.ReadingsInfo);
        var pair = Go("parse.state,readings").Value!.Groups!;
        Assert.Contains("parsed | 2", pair.Groups.Select(group => group.Key));
    }
}

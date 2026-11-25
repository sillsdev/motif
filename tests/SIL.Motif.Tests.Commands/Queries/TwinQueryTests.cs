using System.Text.Json.Nodes;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Query.Corpus;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary><c>--twin</c>: each match of the where's regex rewritten from its groups, with the twin's corpus and parse columns.</summary>
public sealed class TwinQueryTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("twin-query").FullName;

    public void Dispose() => Directory.Delete(_folder, true);

    private static readonly string[] Words = ["tamililirunthu", "tamilil", "kadilirunthu", "vedilirunthu", "plain", "tamirunthu"];

    private static readonly long[] Counts = [10, 4, 7, 3, 2, 1];

    private static string Line(string word, params string[] morphs)
    {
        var analyses = new JsonArray();
        if (morphs.Length > 0)
        {
            var guids = new JsonArray();
            var spans = new JsonArray();
            var at = 0;
            for (var item = 0; item < morphs.Length; item++)
            {
                var msa = Guid.Parse($"00000000-0000-0000-0000-0000000000b{item}").ToString("D");
                guids.Add(new JsonObject { ["form"] = msa.Replace("b", "c"), ["msa"] = msa, ["inflType"] = null, ["guessedString"] = null });
                var end = at + morphs[item].Length;
                spans.Add(new JsonObject
                {
                    ["item"] = item, ["reason"] = null, ["rule"] = null,
                    ["spans"] = new JsonArray(new JsonObject { ["offsets"] = new JsonArray(at, end), ["segments"] = new JsonArray(at, end), ["text"] = morphs[item] }),
                });
                at = end;
            }
            analyses.Add(new JsonObject
            {
                ["morphs"] = guids,
                ["morphSpans"] = new JsonObject { ["morphemes"] = spans, ["segmentCount"] = at + 1, ["surface"] = word, ["unattributed"] = new JsonArray() },
            });
        }
        return new JsonObject
        {
            ["schema"] = "fieldworks-parse-analysis/v1", ["index"] = 0, ["word"] = word, ["elapsedMs"] = 0,
            ["capped"] = false, ["timedOut"] = false, ["invalidShape"] = false, ["analyses"] = analyses, ["unavailable"] = new JsonArray(),
        }.ToJsonString();
    }

    // One recording covers the corpus words and the twins the corpus lacks.
    private static readonly string Recording = string.Join("\n", new[]
    {
        Line("tamililirunthu", "tamil", "il", "irunthu"), Line("tamilil", "tamil", "il"), Line("kadilirunthu", "kad", "ilirunthu"),
        Line("vedilirunthu", "ved", "ilirunthu"), Line("plain", "plain"), Line("tamirunthu", "tam", "irunthu"),
        Line("kadil"), Line("vedil", "ved", "il"),
    }) + "\n";

    private sealed class Recorded : IRealizesIndexBuilder
    {
        public int Builds { get; private set; }

        public int FailAt { get; set; }

        public HashSet<string> Capped { get; } = [];

        public string Identity { get; } = "twin-recording".PadRight(16, '0');

        public string Build(string grammarPath, IReadOnlyList<string> words, CancellationToken cancellationToken)
        {
            Builds++;
            if (Builds == FailAt) throw new RealizesIndexException("the twin batch failed");
            var path = Path.Combine(Directory.CreateTempSubdirectory("recorded-twin").FullName, "analyses.jsonl");
            var text = Recording;
            if (Capped.Count > 0)
                text = string.Join("\n", Recording.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line =>
                {
                    var node = JsonNode.Parse(line)!;
                    if (Capped.Contains((string)node["word"]!)) node["capped"] = true;
                    return node.ToJsonString();
                })) + "\n";
            File.WriteAllText(path, text);
            return path;
        }
    }

    private string Corpus()
    {
        var path = Path.Combine(_folder, "words.tsv");
        File.WriteAllText(path, string.Concat(Words.Select((word, position) => word + "\t" + Counts[position] + "\n")));
        return path;
    }

    private readonly Recorded _builder = new();

    private CommandOutcome<CorpusFormsResponse> Run(string? where, string[]? twin, bool grammar = true, string? groupBy = null, bool foldCase = false)
    {
        var grammarPath = Path.Combine(_folder, "grammar.json");
        File.WriteAllText(grammarPath, "grammar");
        return CorpusQueryCommand.Forms(new CorpusFormsRequest(Corpus(), where, null, grammar ? grammarPath : null, FoldCase: foldCase, GroupBy: groupBy, Twin: twin),
            null, default, _builder, Path.Combine(_folder, "cache"));
    }

    private static CorpusFormsResponse Ok(CommandOutcome<CorpusFormsResponse> outcome) => outcome.Refusal is null
        ? outcome.Value!
        : throw new Xunit.Sdk.XunitException(outcome.Refusal.Code + ": " + outcome.Refusal.Message);

    private const string Suffix = "form ~ \"(.*)ilirunthu$\"";

    [Fact]
    public void ATwinRewritesTheMatchFromItsGroupsAndCarriesCorpusTokensWithoutAGrammar()
    {
        var response = Ok(Run(Suffix, ["$1il"], grammar: false));
        var rows = response.Rows.ToDictionary(row => row.Form);
        Assert.Equal(["kadilirunthu", "tamililirunthu", "vedilirunthu"], rows.Keys.Order(StringComparer.Ordinal));
        var tamil = Assert.Single(rows["tamililirunthu"].Twins!);
        Assert.Equal(("twin", "tamilil", 4L, true, null, null), (tamil.Label, tamil.Form, tamil.Tokens, tamil.InCorpus, tamil.ParseState, tamil.Readings));
        var kad = Assert.Single(rows["kadilirunthu"].Twins!);
        Assert.Equal(("kadil", 0L, false), (kad.Form, kad.Tokens, kad.InCorpus));
        Assert.Equal(0, _builder.Builds);
    }

    [Fact]
    public void TwinColumnsWorkInWhereAndInGroupByAndAbsentTwinsAreParsedByOneExtraBatch()
    {
        var missing = Ok(Run(Suffix + " and twin.inCorpus is \"false\"", ["$1il"]));
        Assert.Equal(["kadilirunthu", "vedilirunthu"], missing.Rows.Select(row => row.Form).Order(StringComparer.Ordinal));
        var states = Ok(Run(Suffix + " and twin.tokens < 1", ["$1il"], groupBy: "parse.state,twin.parse.state"));
        Assert.Equal(["parsed | none", "parsed | parsed"], states.Groups!.Groups.Select(group => group.Key).Order(StringComparer.Ordinal));
        Assert.Equal(7, states.Groups.Groups.Single(group => group.Key == "parsed | none").Tokens);
        Assert.Equal(3, states.Groups.Groups.Single(group => group.Key == "parsed | parsed").Tokens);
        var rows = Ok(Run(Suffix, ["$1il"], groupBy: null)).Rows.ToDictionary(row => row.Form);
        Assert.Equal(("none", 0), (rows["kadilirunthu"].Twins![0].ParseState, rows["kadilirunthu"].Twins![0].Readings));
        Assert.Equal(("parsed", 1), (rows["tamililirunthu"].Twins![0].ParseState, rows["tamililirunthu"].Twins![0].Readings));
        Assert.Equal(2, _builder.Builds);
    }

    [Fact]
    public void NamedGroupsSeveralTemplatesAndTheMatchesOperatorRewriteAsASubstitution()
    {
        var named = Ok(Run("form ~ \"(?<stem>.*)ilirunthu$\"", ["${stem}il", "${stem}$$"], grammar: false));
        var twins = named.Rows.Single(row => row.Form == "kadilirunthu").Twins!;
        Assert.Equal(["twin1", "twin2"], twins.Select(twin => twin.Label));
        Assert.Equal(["kadil", "kad$"], twins.Select(twin => twin.Form));
        var anywhere = Ok(Run("form ~ \"il\"", ["IL"], grammar: false));
        Assert.Equal("tamILilirunthu", anywhere.Rows.Single(row => row.Form == "tamililirunthu").Twins![0].Form);
        var whole = Ok(Run("form matches \"(.*)ilirunthu\"", ["$1"], grammar: false));
        Assert.Equal(["kad", "tamil", "ved"], whole.Rows.Select(row => row.Twins![0].Form).Order(StringComparer.Ordinal));
        var second = Ok(Run(Suffix + " and twin2.form is \"kad$\"", ["$1il", "${1}$$"], grammar: false));
        Assert.Equal(["kadilirunthu"], second.Rows.Select(row => row.Form));
    }

    [Fact]
    public void ARegexIsNeededAndEveryGroupOfATemplateMustExist()
    {
        Assert.Equal(RefusalCodes.QueryTwinNeedsRegex, Run("tokens > 3", ["$1"], grammar: false).Refusal!.Code);
        Assert.Equal(RefusalCodes.QueryTwinNeedsRegex, Run("not form ~ \"a\"", ["$1"], grammar: false).Refusal!.Code);
        Assert.Equal(RefusalCodes.QueryTwinGroupUnknown, Run(Suffix, ["$2il"], grammar: false).Refusal!.Code);
        Assert.Equal(RefusalCodes.QueryTwinGroupUnknown, Run(Suffix, ["${nope}il"], grammar: false).Refusal!.Code);
        Assert.Equal(RefusalCodes.QueryTwinGroupUnknown, Run(Suffix, ["$x"], grammar: false).Refusal!.Code);
        Assert.Equal(RefusalCodes.QueryCorpusInvalid, Run(Suffix + " and twin.inCorpus is \"true\"", null, grammar: false).Refusal!.Code);
        Assert.Equal(RefusalCodes.QueryCorpusInvalid, Run(Suffix + " and twin2.tokens > 1", ["$1"], grammar: false).Refusal!.Code);
    }

    [Fact]
    public void TheParseColumnsOfATwinNeedAGrammar()
    {
        Assert.Equal(RefusalCodes.QueryReadingsNeedsGrammar, Run(Suffix + " and twin.parse.state is \"none\"", ["$1il"], grammar: false).Refusal!.Code);
    }

    [Fact]
    public void MoreThanOneDistinctRegexOrOneUnderOrIsRefusedAndTheSameRegexTwiceIsNot()
    {
        Assert.Equal(RefusalCodes.QueryTwinRegexAmbiguous, Run(Suffix + " and form ~ \"tamil\"", ["$1il"], grammar: false).Refusal!.Code);
        Assert.Equal(RefusalCodes.QueryTwinRegexAmbiguous, Run(Suffix + " or form ~ \"plain\"", ["$1il"], grammar: false).Refusal!.Code);
        Assert.Equal(RefusalCodes.QueryTwinRegexAmbiguous, Run("(" + Suffix + " or tokens > 3)", ["$1il"], grammar: false).Refusal!.Code);
        Assert.Equal(RefusalCodes.QueryTwinRegexAmbiguous, Run(Suffix + " and form matches \".*\"", ["$1il"], grammar: false).Refusal!.Code);
        Ok(Run(Suffix + " and " + Suffix + " and not form ~ \"zzz\"", ["$1il"], grammar: false));
    }

    [Fact]
    public void TwinFormsAreNormalisedLikeCorpusFormsBeforeTheLookup()
    {
        // The corpus holds composed e-acute; the template types it decomposed.
        File.WriteAllText(Path.Combine(_folder, "nfc.tsv"), "tam\u00e9lirunthu\t5\ntam\u00e9l\t9\n");
        var response = Ok(RunCorpus(Path.Combine(_folder, "nfc.tsv"), "form ~ \"^(tam)\u00e9lirunthu$\"", ["$1e\u0301l"]));
        var cell = Assert.Single(response.Rows.Single().Twins!);
        Assert.Equal(("tam\u00e9l", 9L, true), (cell.Form, cell.Tokens, cell.InCorpus));
        File.WriteAllText(Path.Combine(_folder, "fold.tsv"), "kadilirunthu\t5\nkadil\t2\n");
        var folded = Ok(RunCorpus(Path.Combine(_folder, "fold.tsv"), "form ~ \"^(kad)ilirunthu$\"", ["$1IL"], foldCase: true));
        var foldedCell = Assert.Single(folded.Rows.Single().Twins!);
        Assert.Equal(("kadil", 2L, true), (foldedCell.Form, foldedCell.Tokens, foldedCell.InCorpus));
        var plain = Ok(RunCorpus(Path.Combine(_folder, "fold.tsv"), "form ~ \"^(kad)ilirunthu$\"", ["$1IL"]));
        Assert.False(plain.Rows.Single().Twins![0].InCorpus);
    }

    private CommandOutcome<CorpusFormsResponse> RunCorpus(string corpus, string where, string[] twin, bool foldCase = false) =>
        CorpusQueryCommand.Forms(new CorpusFormsRequest(corpus, where, FoldCase: foldCase, Twin: twin), null, default, _builder, Path.Combine(_folder, "cache"));

    [Fact]
    public void ACappedTwinMakesTheReadingsReportIncompleteAndTheTwinBatchKeepsItsOwnFolder()
    {
        _builder.Capped.Add("vedil");
        var response = Ok(Run(Suffix, ["$1il"]));
        Assert.False(response.ReadingsInfo!.Complete);
        Assert.Equal(1, response.ReadingsInfo.Capped);
        Assert.Equal(6 + 2, response.ReadingsInfo.Types);
        Assert.Equal("capped", response.Rows.Single(row => row.Form == "vedilirunthu").Twins![0].ParseState);
        Assert.True(Directory.GetFiles(Path.Combine(_folder, "cache", "twins"), "*.gz").Length >= 1);
    }

    [Fact]
    public void AFailedTwinBatchIsARefusedRunAndTooManyAbsentTwinsAreRefusedBeforeParsing()
    {
        _builder.FailAt = 2;
        Assert.Equal(RefusalCodes.QueryRealizesIndexFailed, Run(Suffix, ["$1il"]).Refusal!.Code);
        var cap = CorpusQueryCommand.MaxAbsentTwins;
        try
        {
            CorpusQueryCommand.MaxAbsentTwins = 1;
            var refused = Run(Suffix, ["$1il"]);
            Assert.Equal(RefusalCodes.QueryCorpusInvalid, refused.Refusal!.Code);
        }
        finally { CorpusQueryCommand.MaxAbsentTwins = cap; }
    }

    [Fact]
    public void DollarZeroIsTheMatchedSpanUnderContainsAndTheWholeWordUnderMatches()
    {
        Assert.Equal("tam[il]ilirunthu", Ok(Run("form ~ \"il\"", ["[$0]"], grammar: false)).Rows.Single(row => row.Form == "tamililirunthu").Twins![0].Form);
        Assert.Equal("<kadilirunthu>", Ok(Run("form matches \"kad.*\"", ["<$0>"], grammar: false)).Rows.Single().Twins![0].Form);
    }
}

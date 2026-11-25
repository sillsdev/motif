using System.Text.Json;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class CorpusQueryCliTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("corpus-cli").FullName;

    public void Dispose() => Directory.Delete(_folder, true);

    private string Write(string name, string text)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, text);
        return path;
    }

    private static (int Code, string Output, string Error) Run(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var code = SIL.Motif.Cli.Program.Run(args, output, error, new Dictionary<string, string?>(), (_, _) => { });
        return (code, output.ToString(), error.ToString());
    }

    private static string Tsv(params (string Form, int Count)[] rows) =>
        "form\tcount\n" + string.Concat(rows.Select(row => row.Form + "\t" + row.Count + "\n"));

    private string Words() => Write("words.tsv", Tsv(("kaon", 5), ("kaonan", 3), ("kaonin", 1), ("baha", 2), ("bahaan", 4), ("lis", 1), ("lisan", 4)));

    [Fact]
    public void PairsPrintsAReadableTableWithNoProject()
    {
        var (code, output, error) = Run("query", "--corpus", Words(), "pairs", "--differ", "suffix");
        Assert.Equal(0, code);
        Assert.Equal("", error);
        Assert.Contains("7 types, 20 tokens, segmentation approximate", output);
        Assert.Contains("kaon > kaonan", output);
        Assert.Matches(@"an\s+3\s+11\s+8\s", output);
        Assert.DoesNotContain("TRUNCATED", output);
    }

    [Fact]
    public void JsonIsVersionedAndHavingSelectsBases()
    {
        var (code, output, _) = Run("query", "--corpus", Words(), "pairs", "--differ", "suffix", "--having", "an,in", "--json");
        Assert.Equal(0, code);
        using var json = JsonDocument.Parse(output);
        var root = json.RootElement;
        Assert.Equal("motif.query.pairs.v1", root.GetProperty("schema").GetString());
        Assert.Equal(["kaon"], root.GetProperty("bases").EnumerateArray().Select(b => b.GetProperty("base").GetString()!).ToArray());
        Assert.False(root.GetProperty("truncation").GetProperty("truncated").GetBoolean());
        Assert.Equal("approximate", root.GetProperty("corpus").GetProperty("segmentation").GetString());
    }

    [Fact]
    public void FoldCaseAndSplitHyphensAreSwitchesAndFoldTheHavingValues()
    {
        var corpus = Write("fold.tsv", Tsv(("Kaon", 1), ("KAONAN", 2)));
        var (code, output, _) = Run("query", "--corpus", corpus, "--fold-case", "--split-hyphens", "pairs", "--differ", "suffix", "--having", "AN", "--json");
        Assert.Equal(0, code);
        using var json = JsonDocument.Parse(output);
        Assert.Equal(1, json.RootElement.GetProperty("pairCount").GetInt32());
    }

    [Fact]
    public void ATruncatedResultSaysSo()
    {
        var (_, output, _) = Run("query", "--corpus", Words(), "pairs", "--differ", "suffix", "--limit", "1");
        Assert.Contains("TRUNCATED: showing 1 of", output);
    }

    [Fact]
    public void RegexAndSegmentFileAndPrefixAreHonoured()
    {
        var segments = Write("seg.txt", "ng");
        var corpus = Write("w.tsv", Tsv(("ka", 1), ("kang", 2), ("maka", 1)));
        var (code, output, _) = Run("query", "--corpus", corpus, "pairs", "--differ", "suffix", "--x", "ng", "--segments", segments, "--max-x", "1", "--json");
        Assert.Equal(0, code);
        using var json = JsonDocument.Parse(output);
        Assert.Equal(["ng"], json.RootElement.GetProperty("xs").EnumerateArray().Select(x => x.GetProperty("x").GetString()!).ToArray());
        Assert.Equal("segment-file", json.RootElement.GetProperty("corpus").GetProperty("segmentation").GetString());
        var prefix = Run("query", "--corpus", corpus, "pairs", "--differ", "prefix");
        Assert.Contains("ma", prefix.Output);
    }

    [Fact]
    public void ABadInvocationPrintsUsage_AnUnreadableCorpusAndABadPatternAreRefused()
    {
        Assert.Equal(1, Run("query", "--corpus", Words(), "pairs").Code);
        Assert.Contains("query pairs --corpus", Run("query", "--corpus", Words(), "pairs").Error);
        Assert.Equal(0, Run("query", "pairs", "--corpus", Words(), "--differ", "suffix").Code);
        Assert.Equal(1, Run("query", "--corpus", Words(), "forms", "--differ", "suffix").Code);
        var missing = Run("query", "--corpus", Path.Combine(_folder, "nope.tsv"), "pairs", "--differ", "suffix", "--json");
        Assert.Equal(1, missing.Code);
        Assert.Contains("query.corpus-unreadable", missing.Error);
        var pattern = Run("query", "--corpus", Words(), "pairs", "--differ", "suffix", "--x", "(", "--json");
        Assert.Contains("query.corpus-invalid", pattern.Error);
        Assert.Contains("query.corpus-invalid", Run("query", "--corpus", Words(), "pairs", "--differ", "infix", "--json").Error);
    }

    [Fact]
    public void GroupByCauseAndWhatIfRetireNeedAGrammarAndSayWhichFlag()
    {
        var (code, _, error) = Run("query", "--corpus", Words(), "forms", "--group-by", "cause", "--json");
        Assert.NotEqual(0, code);
        Assert.Contains("query.readings-needs-grammar", error);
        var (retireCode, retireOutput, retireError) = Run("query", "--corpus", Words(), "forms", "--what-if-retire", "msa:00000000-0000-0000-0000-000000000001,form:00000000-0000-0000-0000-000000000002", "--json");
        Assert.NotEqual(0, retireCode);
        Assert.Contains("--grammar", retireOutput + retireError);
    }

    [Fact]
    public void AReadingsQueryWithNeitherWhereNorFlagIsStillUsageAndAnUnknownGroupIsRefused()
    {
        Assert.Equal(1, Run("query", "--corpus", Words(), "forms").Code);
        var (code, output, error) = Run("query", "--corpus", Words(), "forms", "--group-by", "word", "--grammar", Write("g.json", "g"), "--json");
        Assert.NotEqual(0, code);
        Assert.Contains("cause", output + error);
    }

    [Fact]
    public void GroupByAColumnPrintsTheGroupsAndTotalsAndRepeatedCorpusFlagsAreOneCorpus()
    {
        var other = Write("more.weights.json", "{\"kaon\":10,\"zed\":2}");
        var (code, output, error) = Run("query", "--corpus", Words(), "--corpus", other, "forms", "--group-by", "syllables", "--sum", "tokens");
        Assert.Equal(0, code);
        Assert.Equal("", error);
        Assert.Contains("source " + other + " (weights.json): 2 types, 12 tokens", output);
        Assert.Contains("Totals: 8 types, 32 tokens", output);
        Assert.Contains("Groups by syllables", output);
        var (_, json, _) = Run("query", "--corpus", Words() + "," + other, "forms", "--group-by", "syllables", "--json");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal(2, root.GetProperty("corpus").GetProperty("sources").GetArrayLength());
        Assert.Equal(8, root.GetProperty("totals").GetProperty("types").GetInt32());
        Assert.Equal(32, root.GetProperty("totals").GetProperty("tokens").GetInt64());
        var first = root.GetProperty("groups").GetProperty("groups")[0];
        Assert.True(first.TryGetProperty("key", out _) && first.TryGetProperty("types", out _) && first.TryGetProperty("tokens", out _));
    }

    [Fact]
    public void SampleSeedAndWriteWordsWorkFromTheCommandLineAndWriteWordsNeedsForce()
    {
        var file = Path.Combine(_folder, "set.txt");
        var (code, output, _) = Run("query", "--corpus", Words(), "forms", "--sample", "3", "--seed", "5", "--write-words", file, "--with-counts");
        Assert.Equal(0, code);
        Assert.Contains("Sample: 3 of 7 matching words, seed 5", output);
        Assert.Contains("Wrote 3 words to " + file + " (form, count)", output);
        Assert.DoesNotContain("TRUNCATED", output);
        Assert.All(File.ReadAllLines(file), line => Assert.Equal(2, line.Split('\t').Length));
        var again = Run("query", "--corpus", Words(), "forms", "--sample", "3", "--write-words", file);
        Assert.NotEqual(0, again.Code);
        Assert.Contains("nothing was written", again.Output + again.Error);
        var asJson = Run("query", "--corpus", Words(), "forms", "--sample", "3", "--write-words", file, "--json");
        Assert.Contains("query.write-words-exists", asJson.Output + asJson.Error);
        Assert.Equal(0, Run("query", "--corpus", Words(), "forms", "--sample", "3", "--write-words", file, "--force").Code);
        Assert.Equal(3, File.ReadAllLines(file).Length);
        Assert.Equal(1, Run("query", "--corpus", Words(), "forms", "--sample", "many").Code);
        Assert.Equal(1, Run("query", "--corpus", Words(), "forms", "--seed", "x", "--sample", "2").Code);
    }
}

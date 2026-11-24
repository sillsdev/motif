using System.Text.Json;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Query;
using SIL.Motif.Contract.Query.Corpus;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary><c>realizes(M, attested)</c> over a real saved project and a corpus word list, and the pure compiler under it.</summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group1)]
public sealed class RealizesQueryTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("realizes-query").FullName;

    public void Dispose() => Directory.Delete(_folder, true);

    private string Words(params string[] words)
    {
        var path = Path.Combine(_folder, "words.tsv");
        File.WriteAllText(path, string.Concat(words.Select((word, index) => word + "\t" + (20 - index) + "\n")));
        return path;
    }

    private static readonly string[] Corpus = ["kaonan", "kaonhan", "kaonon", "kaon", "anak", "lihan", "kaonxan", "hanan"];

    private CommandOutcome<CorpusFormsResponse> Run(string project, string where) =>
        CorpusQueryCommand.Forms(new CorpusFormsRequest(Words(Corpus), Where: where, GrammarPath: project));

    [Fact]
    public void ASuffixCompilesToItsAttestedFormsAnchoredAtTheEndAndLabelsEachHit()
    {
        var outcome = Run(RealizesCorpusFixture.Create(pristine), "realizes(sfx:-an, attested)");
        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var response = outcome.Value!;
        Assert.Equal(["kaonan", "kaonhan", "lihan", "kaonxan", "hanan"], response.Rows.Select(row => row.Form));
        var set = Assert.Single(response.Realizes!);
        Assert.Equal("attested", set.Tier);
        Assert.Equal(["an", "han"], set.Forms.Select(form => form.Form).Order(StringComparer.Ordinal));
        Assert.Equal("(?:han|an)\\z", set.Pattern.Text);
        Assert.Equal(("end", "approximate"), (set.Pattern.Anchor, set.Pattern.Segmentation));
        Assert.True(set.Complete);
        Assert.Equal("lexeme-form", set.Forms.Single(form => form.Form == "an").Source);
        Assert.Equal("alternate-form", set.Forms.Single(form => form.Form == "han").Source);
        Assert.DoesNotContain("on", set.Forms.Select(form => form.Form));
        var hit = Assert.Single(response.Rows.Single(row => row.Form == "kaonhan").Realizes!);
        Assert.Equal(["an", "han"], hit.Forms.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void AnUnwrittenBundleMakesTheSetIncompleteAndNegationIsRefused()
    {
        var project = RealizesCorpusFixture.Create(pristine, withUnwrittenBundle: true);
        var positive = Run(project, "realizes(sfx:-an, attested)");
        var set = Assert.Single(positive.Value!.Realizes!);
        Assert.False(set.Complete);
        Assert.Equal("unwritten_variant", Assert.Single(set.Reasons).Code);
        var negated = Run(project, "not realizes(sfx:-an, attested)");
        Assert.False(negated.Succeeded);
        Assert.Equal(RefusalCodes.QueryRealizesNegationIncomplete, negated.Refusal!.Code);
        Assert.Equal("unwritten_variant", negated.Refusal.Facts!["reasons"]);
    }

    [Fact]
    public void NegationOverACompleteSetKeepsTheWordsWithoutTheMorpheme()
    {
        var outcome = Run(RealizesCorpusFixture.Create(pristine), "not realizes(sfx:-an, attested)");
        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        Assert.Equal(["anak", "kaon", "kaonon"], outcome.Value!.Rows.Select(row => row.Form).Order(StringComparer.Ordinal));
        Assert.All(outcome.Value.Rows, row => Assert.Null(row.Realizes));
    }

    [Fact]
    public void AProjectWithNoAnalysesRefusesAndSaysSo()
    {
        var outcome = Run(RealizesCorpusFixture.Create(pristine, withAnalyses: false), "realizes(sfx:-an, attested)");
        Assert.Equal(RefusalCodes.QueryRealizesNoParses, outcome.Refusal!.Code);
        Assert.Contains("none", outcome.Refusal.Message);
    }

    [Fact]
    public void WithoutAProjectItIsRefusedWithTheFlagToPass()
    {
        var outcome = CorpusQueryCommand.Forms(new CorpusFormsRequest(Words(Corpus), Where: "realizes(sfx:-an, attested)"));
        Assert.Equal(RefusalCodes.QueryRealizesNeedsProject, outcome.Refusal!.Code);
        Assert.Equal("--grammar", outcome.Refusal.Facts!["flag"]);
    }

    [Theory]
    [InlineData("possible")]
    [InlineData("generated")]
    public void TiersThatAreNotBuiltYetAreRefusedAsNotYetAvailable(string tier)
    {
        var outcome = Run(RealizesCorpusFixture.Create(pristine), $"realizes(sfx:-an, {tier})");
        Assert.Equal(RefusalCodes.QueryNotYetAvailable, outcome.Refusal!.Code);
    }

    [Fact]
    public void AMorphemeThatIsNotInTheLexiconIsRefused()
    {
        var outcome = Run(RealizesCorpusFixture.Create(pristine), "realizes(sfx:-zzz, attested)");
        Assert.Equal(RefusalCodes.FormsExplainMorphemeUnresolved, outcome.Refusal!.Code);
    }

    [Fact]
    public void TheResponseJsonCarriesTheSetAndTheRowLabels()
    {
        var outcome = Run(RealizesCorpusFixture.Create(pristine), "realizes(sfx:-an, attested)");
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(outcome.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var set = json.RootElement.GetProperty("realizes")[0];
        Assert.Equal("motif.realizes.v1", set.GetProperty("schema").GetString());
        Assert.True(set.GetProperty("complete").GetBoolean());
        Assert.False(set.TryGetProperty("stopReason", out _));
        Assert.Equal(JsonValueKind.Array, json.RootElement.GetProperty("rows")[0].GetProperty("realizes").ValueKind);
    }

    [Theory]
    [InlineData("start", "\\A(?:mag|ma)")]
    [InlineData("end", "(?:mag|ma)\\z")]
    [InlineData("none", "(?:mag|ma)")]
    public void TheAnchorIsWrittenIntoTheExpression(string anchor, string expected) =>
        Assert.Equal(expected, RealizesPatternCompiler.Compile(["mag", "ma"], anchor));

    [Fact]
    public void FormsAreNormalisedDistinctAndLongestFirstAndRegexMetacharactersAreEscaped()
    {
        Assert.Equal(["a.b", "ab", "b"], RealizesPatternCompiler.Normalize(["b", "ab", "", "a.b", "ab"]));
        Assert.Equal("(?:a\\.b|b)\\z", RealizesPatternCompiler.Compile(["a.b", "b"], "end"));
        Assert.Equal(["é"], RealizesPatternCompiler.Normalize(["é"]).Select(f => f.Normalize(System.Text.NormalizationForm.FormD)));
    }

    [Fact]
    public void ALargeSetIsChunkedUnderTheRegexLimitAndEveryFormIsInSomeChunk()
    {
        var forms = RealizesPatternCompiler.Normalize(Enumerable.Range(0, 400).Select(i => "x" + i.ToString("D4")));
        var chunks = RealizesPatternCompiler.Chunks(forms, "end", RealizesLowering.ChunkLimit);
        Assert.True(chunks.Count > 1);
        Assert.All(chunks, chunk => Assert.True(chunk.Length <= RealizesLowering.ChunkLimit));
        Assert.All(forms, form => Assert.Contains(chunks, chunk => chunk.Contains(form, StringComparison.Ordinal)));
        Assert.Throws<ArgumentException>(() => RealizesPatternCompiler.Chunks([new string('q', 400)], "none", RealizesLowering.ChunkLimit));
    }
}

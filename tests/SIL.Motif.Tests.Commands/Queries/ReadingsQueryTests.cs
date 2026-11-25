using System.Text.Json.Nodes;
using SIL.LCModel;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Query.Corpus;
using SIL.Motif.Contract.Query.Readings;
using SIL.Motif.Host.Baselines;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// <c>readings</c>, <c>--group-by cause</c>, <c>only-via</c> and <c>--what-if-retire</c> over a corpus parse. The batch lines are built here in the
/// <c>fieldworks-parse-analysis/v1</c> shape with spans that tile each word (the pinned PanGloss's own recording has one reading per word).
/// </summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group1)]
public sealed class ReadingsQueryTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("readings-query").FullName;

    public void Dispose() => Directory.Delete(_folder, true);

    private static readonly string Root = Guid.Parse("00000000-0000-0000-0000-0000000000a1").ToString("D");
    private static readonly string Fused = Guid.Parse("00000000-0000-0000-0000-0000000000a2").ToString("D");
    private static readonly string Deriv = Guid.Parse("00000000-0000-0000-0000-0000000000a3").ToString("D");
    private static readonly string Sm = Guid.Parse("00000000-0000-0000-0000-0000000000b1").ToString("D");
    private static readonly string Om = Guid.Parse("00000000-0000-0000-0000-0000000000b2").ToString("D");

    private static string Form(string msa) => msa.Replace("a", "c").Replace("b", "d");

    /// <summary>One morph of a built reading: its analysis id and surface text.</summary>
    private static (string Msa, string Text) M(string msa, string text) => (msa, text);

    private static string Line(string word, string flags, params (string Msa, string Text)[][] readings)
    {
        var analyses = new JsonArray();
        foreach (var reading in readings)
        {
            var morphs = new JsonArray();
            var spans = new JsonArray();
            var at = 0;
            for (var item = 0; item < reading.Length; item++)
            {
                morphs.Add(new JsonObject { ["form"] = Form(reading[item].Msa), ["msa"] = reading[item].Msa, ["inflType"] = null, ["guessedString"] = null });
                var end = at + reading[item].Text.Length;
                spans.Add(new JsonObject
                {
                    ["item"] = item, ["reason"] = null, ["rule"] = null,
                    ["spans"] = new JsonArray(new JsonObject { ["offsets"] = new JsonArray(at, end), ["segments"] = new JsonArray(at, end), ["text"] = reading[item].Text }),
                });
                at = end;
            }
            analyses.Add(new JsonObject
            {
                ["morphs"] = morphs,
                ["morphSpans"] = new JsonObject { ["morphemes"] = spans, ["segmentCount"] = at + 1, ["surface"] = word, ["unattributed"] = new JsonArray() },
            });
        }
        var row = new JsonObject
        {
            ["schema"] = "fieldworks-parse-analysis/v1", ["index"] = 0, ["word"] = word, ["elapsedMs"] = 0,
            ["capped"] = flags == "capped", ["timedOut"] = false, ["invalidShape"] = false, ["analyses"] = analyses, ["unavailable"] = new JsonArray(),
        };
        return row.ToJsonString();
    }

    private sealed class Recorded(string recording) : IRealizesIndexBuilder
    {
        public string Identity { get; } = "readings-recorded".PadRight(16, '0');

        public string Build(string grammarPath, IReadOnlyList<string> words, CancellationToken cancellationToken)
        {
            var path = Path.Combine(Directory.CreateTempSubdirectory("recorded-readings").FullName, "analyses.jsonl");
            File.WriteAllText(path, recording);
            return path;
        }
    }

    // tofauti fused vs derived, kulishana reordered, pendwa SM vs OM, dupe unrelated.
    private static readonly string Recording = string.Join("\n",
        Line("tofauti", "", [M(Fused, "tofauti")], [M(Root, "tofaut"), M(Deriv, "i")]),
        Line("kulishana", "", [M(Root, "kuli"), M(Sm, "sha"), M(Om, "na")], [M(Root, "kuli"), M(Om, "sha"), M(Sm, "na")]),
        Line("pendwa", "", [M(Root, "pend"), M(Sm, "wa")], [M(Root, "pend"), M(Om, "wa")]),
        Line("dupe", "", [M(Root, "du"), M(Sm, "pe")], [M(Deriv, "dup"), M(Om, "e")]),
        Line("solo", "", [M(Root, "so"), M(Sm, "lo")]),
        Line("cappedw", "capped", [M(Fused, "cap"), M(Sm, "pedw")]),
        Line("nothing", "")) + "\n";

    private static readonly string[] Words = ["tofauti", "kulishana", "pendwa", "dupe", "solo", "cappedw", "nothing", "missing"];

    private string Corpus()
    {
        var path = Path.Combine(_folder, "words.tsv");
        File.WriteAllText(path, string.Concat(Words.Select((word, position) => word + "\t" + (20 - position) + "\n")));
        return path;
    }

    private CommandOutcome<CorpusFormsResponse> Run(string? where, string? groupBy = null, string[]? retire = null, string? project = null,
        string? grammar = "grammar.json", int? limit = null, string? json = null)
    {
        var grammarPath = grammar is null ? null : Path.Combine(_folder, grammar);
        if (grammarPath is not null && !File.Exists(grammarPath)) File.WriteAllText(grammarPath, "grammar");
        return CorpusQueryCommand.Forms(new CorpusFormsRequest(Corpus(), where, json, grammarPath, ProjectPath: project, Limit: limit, GroupBy: groupBy, WhatIfRetire: retire),
            null, default, new Recorded(Recording), Path.Combine(_folder, "cache"));
    }

    [Fact]
    public void ReadingsIsANumberColumnAndRowsCarryTheCountAndLiveParseState()
    {
        var outcome = Run("readings > 1");
        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var rows = outcome.Value!.Rows;
        Assert.Equal(["tofauti", "kulishana", "pendwa", "dupe"], rows.Select(row => row.Form));
        Assert.All(rows, row => Assert.Equal((2, "parsed"), (row.Readings, row.ParseState)));
        Assert.Equal("readings > 1", outcome.Value.Where);
        var none = Run("readings = 0 or parse.state = none");
        Assert.Contains("nothing", none.Value!.Rows.Select(row => row.Form));
        var state = Run("parse.state = capped").Value!.Rows;
        Assert.Equal(["cappedw"], state.Select(row => row.Form));
    }

    [Fact]
    public void ACappedOrUnansweredWordMakesTheCountsIncompleteAndTheReportSaysWhy()
    {
        var info = Run("readings > 1").Value!.ReadingsInfo!;
        Assert.False(info.Complete);
        Assert.Equal((1, 1, 8), (info.Capped, info.Failed, info.Types));
        Assert.Contains("may be low", info.Note);
    }

    [Fact]
    public void ACorpusWithOnlyFinishedParsesIsComplete()
    {
        File.WriteAllText(Path.Combine(_folder, "g.json"), "g");
        var path = Path.Combine(_folder, "few.tsv");
        File.WriteAllText(path, "solo\t3\ntofauti\t2\n");
        var outcome = CorpusQueryCommand.Forms(new CorpusFormsRequest(path, "readings > 0", GrammarPath: Path.Combine(_folder, "g.json")), null, default,
            new Recorded(Recording), Path.Combine(_folder, "cache2"));
        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        Assert.True(outcome.Value!.ReadingsInfo!.Complete);
        Assert.Null(outcome.Value.ReadingsInfo.Note);
    }

    [Fact]
    public void GroupByCauseNamesEachKindInPriorityOrderWithAnExample()
    {
        var outcome = Run(null, groupBy: "cause");
        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var causes = outcome.Value!.Causes!;
        Assert.Empty(outcome.Value.Rows);
        Assert.False(causes.Complete);
        Assert.Equal((4, 20 + 19 + 18 + 17), (causes.Words, (int)causes.Tokens));
        Assert.Equal(["lexicalised-vs-derived", "same-morphs-reordered", "homophonous-affix", "other"], causes.Groups.Select(group => group.Cause));
        var lexicalised = causes.Groups[0];
        Assert.Equal(("tofauti", 1, false), (lexicalised.Example.Word, lexicalised.Words, lexicalised.Benign));
        Assert.Equal(["tofauti"], lexicalised.Example.Readings[0].Morphs.Select(morph => morph.Text));
        Assert.Equal(["tofaut", "i"], lexicalised.Example.Readings[1].Morphs.Select(morph => morph.Text));
        Assert.Equal("same-morphs-reordered", causes.Groups[1].Cause);
        Assert.Equal("kulishana", causes.Groups[1].Example.Word);
        Assert.True(causes.Groups[2].Benign);
        Assert.Equal(("pendwa", "dupe"), (causes.Groups[2].Example.Word, causes.Groups[3].Example.Word));
        Assert.False(causes.Groups[3].Benign);
    }

    [Fact]
    public void GroupByCauseHonoursTheWhereAndRefusesAnythingButCause()
    {
        var outcome = Run("tokens < 19", groupBy: "cause");
        Assert.Equal(2, outcome.Value!.Causes!.Words);
        var refused = Run(null, groupBy: "word");
        Assert.Equal(RefusalCodes.QueryCorpusInvalid, refused.Refusal!.Code);
    }

    [Fact]
    public void OnlyViaFindsWordsWhoseEveryReadingUsesTheAnalysis()
    {
        var outcome = Run($"only-via(msa:{Root})");
        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        Assert.Equal(["kulishana", "pendwa", "solo"], outcome.Value!.Rows.Select(row => row.Form));
        var viaForm = Run($"only-via(form:{Form(Fused)})").Value!.Rows;
        Assert.Equal(["cappedw"], viaForm.Select(row => row.Form));
        var negated = Run($"readings > 0 and not only-via(msa:{Root})").Value!.Rows.Select(row => row.Form);
        Assert.Contains("tofauti", negated);
        Assert.DoesNotContain("solo", negated);
        var json = Run(null, json: "{\"version\":\"motif.query.v2\",\"from\":\"forms\",\"where\":{\"only-via\":{\"msa\":\"" + Root + "\"}}}");
        Assert.True(json.Succeeded, json.Refusal?.Message);
        Assert.Equal(["kulishana", "pendwa", "solo"], json.Value!.Rows.Select(row => row.Form));
    }

    [Fact]
    public void AnEntryIdWithNoProjectIsRefusedAndNamesTheFlag()
    {
        var outcome = Run($"only-via(entry:{Root})");
        Assert.Equal(RefusalCodes.QueryReadingsNeedsProject, outcome.Refusal!.Code);
        Assert.Contains("--project", outcome.Refusal.Message);
        Assert.Equal(RefusalCodes.QueryReadingsNeedsProject, Run(null, retire: [$"entry:{Root}"]).Refusal!.Code);
    }

    [Fact]
    public void AReadingsQueryWithNoGrammarIsRefusedAndNamesTheFlag()
    {
        var outcome = Run("readings > 1", grammar: null);
        Assert.Equal(RefusalCodes.QueryReadingsNeedsGrammar, outcome.Refusal!.Code);
        Assert.Equal(RefusalCodes.QueryReadingsNeedsGrammar, Run(null, retire: [Root], grammar: null).Refusal!.Code);
    }

    [Fact]
    public void WhatIfRetireCountsLostAndReducedWordsWeightedByTokensAndIsLabelledAnEstimate()
    {
        var outcome = Run(null, retire: [$"msa:{Sm}"]);
        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var what = outcome.Value!.WhatIf!;
        Assert.True(what.Estimate);
        Assert.False(what.Exact);
        Assert.Contains("confirm with a Trial", what.Note);
        Assert.Equal([$"msa:{Sm}"], what.Retire);
        Assert.Equal(["kulishana", "solo", "cappedw", "pendwa", "dupe"], what.Words.Select(word => word.Word));
        var lost = what.Words[0];
        Assert.Equal((2, 2, 0, true, false), (lost.Readings, lost.ReadingsVia, lost.ReadingsOther, lost.Lost, lost.Reduced));
        Assert.True(what.Words[2].MayHaveMore);
        Assert.False(what.Words[0].MayHaveMore);
        var reduced = what.Words[3];
        Assert.Equal((2, 1, 1, false, true), (reduced.Readings, reduced.ReadingsVia, reduced.ReadingsOther, reduced.Lost, reduced.Reduced));
        Assert.Equal(new TypesTokens(5, 19 + 18 + 17 + 16 + 15), what.Affected);
        Assert.Equal(new TypesTokens(3, 19 + 16 + 15), what.Lost);
        Assert.Equal(new TypesTokens(2, 18 + 17), what.Reduced);
        Assert.False(what.Complete);
    }

    [Fact]
    public void WhatIfRetireTokensAddUpAndAnIdTheCorpusLacksAffectsNothing()
    {
        var what = Run(null, retire: [Fused]).Value!.WhatIf!;
        Assert.Equal([$"id:{Fused}"], what.Retire);
        Assert.Equal(["cappedw", "tofauti"], what.Words.Select(word => word.Word));
        Assert.Equal(new TypesTokens(2, 20 + 15), what.Affected);
        Assert.Equal(new TypesTokens(1, 15), what.Lost);
        Assert.Equal(new TypesTokens(1, 20), what.Reduced);
        var none = Run(null, retire: ["ffffffff-ffff-ffff-ffff-ffffffffffff"]).Value!.WhatIf!;
        Assert.Equal((0, 0), (none.Affected.Types, none.Words.Count));
    }

    [Fact]
    public void WhatIfRetireOnlyCountsTheWordsTheWhereKeeps()
    {
        var what = Run("tokens > 18", retire: [Fused]).Value!.WhatIf!;
        Assert.Equal(["tofauti"], what.Words.Select(word => word.Word));
    }

    [Fact]
    public void AProjectNamesEachMorphsEntryExactlyAndItsClass()
    {
        var project = RealizesCorpusFixture.Create(pristine);
        using var read = BaselineReadCache.Open(project);
        var entries = read.Cache.ServiceLocator.GetInstance<ILexEntryRepository>().AllInstances().ToList();
        var stemEntry = entries.Single(entry => entry.HeadWord?.Text == "kaon");
        var suffixEntry = entries.Single(entry => entry.HeadWord?.Text?.Trim('-') == "an");
        var stem = stemEntry.MorphoSyntaxAnalysesOC.Single().Guid.ToString("D");
        var suffix = suffixEntry.MorphoSyntaxAnalysesOC.Single().Guid.ToString("D");
        var recording = string.Join("\n",
            Line("kaonan", "", [M(stem, "kaon"), M(suffix, "an")]),
            Line("kaonon", "", [M(stem, "kaon"), M(suffix, "on")], [M(stem, "kaonon")]),
            Line("anak", "", [M(suffix, "an"), M(stem, "ak")])) + "\n";
        var path = Path.Combine(_folder, "p.tsv");
        File.WriteAllText(path, "kaonan\t5\nkaonon\t4\nanak\t3\n");
        var grammar = Path.Combine(_folder, "g.json");
        File.WriteAllText(grammar, "g");
        CommandOutcome<CorpusFormsResponse> Go(string? where, string[]? retire = null, string? group = null) => CorpusQueryCommand.Forms(
            new CorpusFormsRequest(path, where, GrammarPath: grammar, ProjectPath: project, GroupBy: group, WhatIfRetire: retire), null, default,
            new Recorded(recording), Path.Combine(_folder, "cache3"));

        var via = Go($"only-via(entry:{suffixEntry.Guid:D})");
        Assert.True(via.Succeeded, via.Refusal?.Message);
        Assert.Equal(["kaonan", "anak"], via.Value!.Rows.Select(row => row.Form));
        Assert.Equal("project", via.Value.ReadingsInfo!.MorphClasses);
        var what = Go(null, [$"entry:{suffixEntry.Guid:D}"]).Value!.WhatIf!;
        Assert.Equal(["anak", "kaonan", "kaonon"], what.Words.Select(word => word.Word).Order(StringComparer.Ordinal));
        Assert.Equal((2, 1), (what.Lost.Types, what.Reduced.Types));
        var bare = Go(null, [suffixEntry.Guid.ToString("D")]).Value!.WhatIf!;
        Assert.Equal([$"entry:{suffixEntry.Guid:D}"], bare.Retire);
        Assert.Equal(what.Affected, bare.Affected);
        // An inflectional suffix is not derivation once the project names the classes.
        var cause = Go(null, group: "cause").Value!.Causes!;
        var only = Assert.Single(cause.Groups);
        Assert.Equal("other", only.Cause);
        Assert.Equal(["stem", "inflectional"], only.Example.Readings[0].Morphs.Select(morph => morph.Kind));
        Assert.Equal(stemEntry.Guid.ToString("D"), only.Example.Readings[0].Morphs[0].Entry);
    }
}

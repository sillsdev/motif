using System.Text.Json.Nodes;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Query.Corpus;
using SIL.Motif.Contract.Query.Readings;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// <c>morph</c>, <c>seq</c>, <c>--count-by morph-seq</c> and <c>realizes(@stems)</c> with <c>--by-morph</c> over a corpus parse. The batch lines carry
/// spans that tile each word, as in <see cref="ReadingsQueryTests"/>.
/// </summary>
public sealed class MorphSeqQueryTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("morph-seq-query").FullName;

    public void Dispose() => Directory.Delete(_folder, true);

    private static string Id(int n) => Guid.Parse($"00000000-0000-0000-0000-0000000000a{n}").ToString("D");

    private static readonly string Root = Id(1);
    private static readonly string Caus = Id(2);
    private static readonly string Recp = Id(3);
    private static readonly string Sm = Id(4);
    private static readonly string Absent = Id(9);
    private static readonly string[] Entries = Enumerable.Range(1, 4).Select(n => Guid.Parse($"00000000-0000-0000-0000-0000000000e{n}").ToString("D")).ToArray();

    private static string Form(string msa) => msa.Replace("a", "c");

    private static (string Msa, string Text) M(string msa, string text) => (msa, text);

    private static string Line(string word, bool capped, params (string Msa, string Text)[][] readings)
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
        return new JsonObject
        {
            ["schema"] = "fieldworks-parse-analysis/v1", ["index"] = 0, ["word"] = word, ["elapsedMs"] = 0,
            ["capped"] = capped, ["timedOut"] = false, ["invalidShape"] = false, ["analyses"] = analyses, ["unavailable"] = new JsonArray(),
        }.ToJsonString();
    }

    private sealed class Recorded(string recording, bool capped) : IRealizesIndexBuilder
    {
        public string Identity { get; } = (capped ? "seq-capped" : "seq-clean").PadRight(16, '0');

        public string Build(string grammarPath, IReadOnlyList<string> words, CancellationToken cancellationToken)
        {
            var path = Path.Combine(Directory.CreateTempSubdirectory("recorded-seq").FullName, "analyses.jsonl");
            File.WriteAllText(path, recording);
            return path;
        }
    }

    // Chains: root CAUS SM; root CAUS RECP SM and root RECP SM; CAUS root; root SM RECP.
    private static string Recording(bool withCapped) => string.Join("\n", new[]
    {
        Line("fanyisha", false, [M(Root, "fany"), M(Caus, "ish"), M(Sm, "a")]),
        Line("fanyishana", false, [M(Root, "fany"), M(Caus, "ish"), M(Recp, "an"), M(Sm, "a")], [M(Root, "fany"), M(Recp, "ishan"), M(Sm, "a")]),
        Line("ishfany", false, [M(Caus, "ish"), M(Root, "fany")]),
        Line("fanyaana", false, [M(Root, "fany"), M(Sm, "a"), M(Recp, "ana")]),
        Line("plain", false, [M(Root, "plai"), M(Sm, "n")]),
        Line("cappedw", withCapped, [M(Root, "cap"), M(Sm, "pedw")]),
        Line("other", false, [M(Root, "ot"), M(Sm, "her")]),
    }) + "\n";

    private static readonly string[] Words = ["fanyisha", "fanyishana", "ishfany", "fanyaana", "plain", "cappedw", "other"];

    private static readonly long[] Counts = [50, 40, 30, 20, 10, 5, 4];

    private string Corpus()
    {
        var path = Path.Combine(_folder, "words.tsv");
        File.WriteAllText(path, string.Concat(Words.Select((word, position) => word + "\t" + Counts[position] + "\n")));
        return path;
    }

    private static MorphCatalog Catalog() => MorphCatalog.From(new (string, string, string, string?)[]
    {
        (Root, Entries[0], MorphKinds.Stem, "do"), (Caus, Entries[1], MorphKinds.Derivational, "CAUS"),
        (Recp, Entries[2], MorphKinds.Derivational, "RECP"), (Sm, Entries[3], MorphKinds.Inflectional, "SM"),
    });

    private CommandOutcome<CorpusFormsResponse> Run(string? where, bool capped = false, string? countBy = null, bool adjacent = false, bool byMorph = false,
        bool withCatalog = true, string? project = "p.fwdata", string? json = null, int? limit = null, bool grammar = true)
    {
        var grammarPath = Path.Combine(_folder, "grammar.json");
        File.WriteAllText(grammarPath, "grammar");
        return CorpusQueryCommand.Forms(new CorpusFormsRequest(Corpus(), where, json, grammar ? grammarPath : null, ProjectPath: project, Limit: limit, CountBy: countBy,
                Adjacent: adjacent, ByMorph: byMorph),
            null, default, new Recorded(Recording(capped), capped), Path.Combine(_folder, "cache"), catalog: withCatalog ? Catalog() : null,
            entryOfForm: item => item.Value == "fany" ? Entries[0] : item.Value == "ish" ? Entries[1] : null);
    }

    private static string[] Matched(CommandOutcome<CorpusFormsResponse> outcome) => outcome.Refusal is null
        ? [.. outcome.Value!.Rows.Select(row => row.Form).Order(StringComparer.Ordinal)]
        : throw new Xunit.Sdk.XunitException(outcome.Refusal.Code + ": " + outcome.Refusal.Message);

    [Fact]
    public void SeqKeepsTheOrderAndAllowsGapsByDefault()
    {
        Assert.Equal(["fanyaana", "fanyishana"], Matched(Run($"seq(msa:{Root}, msa:{Recp})")));
        Assert.Equal(["fanyisha", "fanyishana"], Matched(Run($"seq(msa:{Root}, msa:{Caus})")));
        Assert.Equal(["ishfany"], Matched(Run($"seq(msa:{Caus}, msa:{Root})")));
    }

    [Fact]
    public void AdjacentNeedsTheMorphsNextToEachOtherByCallOrByFlag()
    {
        var gaps = Matched(Run($"seq(msa:{Caus}, msa:{Sm})"));
        Assert.Equal(["fanyisha", "fanyishana"], gaps);
        Assert.Equal(["fanyisha"], Matched(Run($"seq(msa:{Caus}, msa:{Sm}, adjacent=true)")));
        Assert.Equal(["fanyisha"], Matched(Run($"seq(msa:{Caus}, msa:{Sm})", adjacent: true)));
        Assert.Equal(["fanyishana"], Matched(Run($"seq(msa:{Root}, msa:{Caus}, msa:{Recp})", adjacent: true)));
    }

    [Fact]
    public void AMorphIsUsedOnceSoARepeatedItemNeedsTwoMorphs()
    {
        Assert.Empty(Matched(Run($"seq(msa:{Root}, msa:{Root})")));
        Assert.Empty(Matched(Run($"seq(msa:{Sm}, msa:{Sm})")));
    }

    [Fact]
    public void MorphMatchesAnyReadingWithTheMorph()
    {
        Assert.Equal(["fanyaana", "fanyishana"], Matched(Run($"morph(msa:{Recp})")));
        Assert.Equal(["fanyaana", "fanyishana"], Matched(Run($"morph({Recp})")));
        Assert.Equal(["fanyaana", "fanyishana"], Matched(Run($"morph(form:{Form(Recp)})")));
        Assert.Equal(["fanyishana"], Matched(Run("morph(text:ishan)")));
    }

    [Fact]
    public void ClassTokensMatchGlossClassOrInflectionTypeThroughTheProject()
    {
        Assert.Equal(["fanyishana"], Matched(Run("seq(do, CAUS, RECP)")));
        Assert.Equal(["fanyishana"], Matched(Run("seq(stem, caus, recp)")));
        Assert.Equal(["fanyaana", "fanyishana"], Matched(Run("morph(derivational) and morph(RECP)")));
    }

    [Fact]
    public void RootPfxAndSfxItemsResolveThroughTheLexiconToTheEntry()
    {
        Assert.Equal(["fanyisha", "fanyishana"], Matched(Run("seq(root:fany, sfx:ish)")));
        Assert.Equal(["ishfany"], Matched(Run("seq(sfx:ish, root:fany)")));
        Assert.Equal(["fanyisha", "fanyishana", "ishfany"], Matched(Run($"morph(entry:{Entries[1]})")));
    }

    [Fact]
    public void ItemsThatNeedTheProjectAreRefusedWithoutOne()
    {
        var refused = Run("seq(CAUS, RECP)", withCatalog: false, project: null);
        Assert.Equal(RefusalCodes.QueryReadingsNeedsProject, refused.Refusal!.Code);
        Assert.Equal(RefusalCodes.QueryReadingsNeedsProject, Run($"morph(entry:{Entries[0]})", withCatalog: false, project: null).Refusal!.Code);
        Assert.Equal(["fanyaana", "fanyishana"], Matched(Run($"morph(msa:{Recp})", withCatalog: false, project: null)));
        Assert.Equal(["fanyishana"], Matched(Run("morph(text:ishan)", withCatalog: false, project: null)));
    }

    [Fact]
    public void AnItemThatMatchesNoMorphIsRefusedNotAnsweredWithZero()
    {
        Assert.Equal(RefusalCodes.QuerySeqItemUnknown, Run("seq(do, CUAS)").Refusal!.Code);
        Assert.Equal(RefusalCodes.QuerySeqItemUnknown, Run($"morph(msa:{Absent})").Refusal!.Code);
        Assert.Equal(RefusalCodes.QuerySeqItemUnknown, Run("morph(text:nowhere)").Refusal!.Code);
    }

    [Fact]
    public void SeqNeedsAGrammar()
    {
        Assert.Equal(RefusalCodes.QueryReadingsNeedsGrammar, Run($"morph(msa:{Root})", grammar: false).Refusal!.Code);
    }

    [Fact]
    public void NegationIsRefusedUnlessTheParseIsComplete()
    {
        var refused = Run($"not seq(msa:{Caus}, msa:{Root})", capped: true);
        Assert.Equal(RefusalCodes.QuerySeqNegationIncomplete, refused.Refusal!.Code);
        var complete = Run($"not seq(msa:{Caus}, msa:{Root})");
        Assert.Equal(["cappedw", "fanyaana", "fanyisha", "fanyishana", "other", "plain"], Matched(complete));
        Assert.True(complete.Value!.ReadingsInfo!.Complete);
    }

    [Fact]
    public void CountByMorphSeqCountsOneWordPerDistinctSequenceOverMatchingReadings()
    {
        var outcome = Run($"morph(msa:{Recp})", countBy: "morph-seq");
        var report = outcome.Value!.MorphSeqs!;
        Assert.True(report.Complete);
        Assert.Equal(2, report.Words);
        Assert.Equal(60, report.Tokens);
        Assert.Equal(3, report.Distinct);
        Assert.Equal(["fany/do", "ish/CAUS", "an/RECP", "a/SM"], report.Sequences[0].Seq);
        Assert.Equal([1, 40, 1], [report.Sequences[0].Types, (int)report.Sequences[0].Tokens, report.Sequences[0].Examples.Count]);
        Assert.Equal("fanyishana", report.Sequences[0].Examples[0]);
        Assert.Equal(["fanyaana"], report.Sequences.Single(entry => entry.Seq.Contains("ana/RECP")).Examples);
    }

    [Fact]
    public void CountByMorphSeqWithASeqKeepsOnlyTheReadingsThatSatisfyIt()
    {
        var report = Run("seq(do, CAUS, RECP)", countBy: "morph-seq").Value!.MorphSeqs!;
        var only = Assert.Single(report.Sequences);
        Assert.Equal(["fany/do", "ish/CAUS", "an/RECP", "a/SM"], only.Seq);
        Assert.Equal(1, report.Distinct);
    }

    [Fact]
    public void CountByMorphSeqWithoutAWhereCountsEveryReadingAndIsMarkedIncompleteWhenAWordWasCapped()
    {
        var outcome = Run(null, capped: true, countBy: "morph-seq", limit: 2);
        var report = outcome.Value!.MorphSeqs!;
        Assert.False(report.Complete);
        Assert.Equal(2, report.Sequences.Count);
        Assert.True(report.Distinct > 2);
        Assert.Equal("fany/do ish/CAUS a/SM".Split(' '), report.Sequences[0].Seq);
        Assert.Equal(RefusalCodes.QueryCorpusInvalid, Run(null, countBy: "bogus").Refusal!.Code);
    }

    [Fact]
    public void ASeqInAQueryJsonDocumentRunsAndRoundTripsIntoTheHeader()
    {
        var json = "{\"version\":\"motif.query.v2\",\"from\":\"forms\",\"where\":{\"seq\":{\"items\":[\"msa:" + Caus + "\",\"msa:" + Sm + "\"],\"adjacent\":true}}}";
        var outcome = Run(null, json: json);
        Assert.Equal(["fanyisha"], Matched(outcome));
        Assert.Contains("adjacent=true", outcome.Value!.Where, StringComparison.Ordinal);
    }

    private string Stems(params string[] lines)
    {
        var path = Path.Combine(_folder, "stems-" + Guid.NewGuid().ToString("N")[..6] + ".txt");
        File.WriteAllLines(path, lines);
        return path;
    }

    [Fact]
    public void ARealizesStemsFileMeansAnyOfItsLinesAndByMorphGroupsPerMorph()
    {
        var stems = Stems("# the stems", Root, "", Caus, Absent);
        var outcome = Run($"realizes(@{stems}, attested)", byMorph: true, project: null, withCatalog: false);
        Assert.Equal(["cappedw", "fanyaana", "fanyisha", "fanyishana", "ishfany", "other", "plain"], Matched(outcome));
        var groups = outcome.Value!.ByMorph!;
        Assert.Equal(3, groups.Count);
        Assert.Equal(1, groups.Count(group => group.Types == 0 && group.Tokens == 0));
        var root = groups.Single(group => group.Morpheme.Contains(Root, StringComparison.Ordinal));
        Assert.Equal(7, root.Types);
        Assert.Equal(159, root.Tokens);
        Assert.Equal("fanyisha", root.Examples[0]);
        var caus = groups.Single(group => group.Morpheme.Contains(Caus, StringComparison.Ordinal));
        Assert.Equal(3, caus.Types);
        Assert.Equal(120, caus.Tokens);
        Assert.Equal(3, outcome.Value.Realizes!.Count);
        Assert.Contains(outcome.Value.Realizes!, set => set.Reasons.Any(reason => reason.Code == "no-parses"));
    }

    [Fact]
    public void AStemsFileOfOneMorphBehavesAsTheSingleArgument()
    {
        var single = Matched(Run($"realizes({Recp}, attested)", project: null, withCatalog: false));
        var listed = Matched(Run($"realizes(@{Stems(Recp)}, attested)", project: null, withCatalog: false));
        Assert.Equal(single, listed);
        Assert.NotEmpty(listed);
    }

    [Fact]
    public void ABadStemsFileOrByMorphWithoutRealizesIsRefused()
    {
        Assert.Equal(RefusalCodes.QueryCorpusUnreadable, Run($"realizes(@{Path.Combine(_folder, "missing.txt")}, attested)", project: null, withCatalog: false).Refusal!.Code);
        Assert.Equal(RefusalCodes.QueryCorpusInvalid, Run($"realizes(@{Stems("# nothing", "")}, attested)", project: null, withCatalog: false).Refusal!.Code);
        Assert.Equal(RefusalCodes.QueryCorpusInvalid, Run($"morph(msa:{Root})", byMorph: true).Refusal!.Code);
        Assert.Equal(RefusalCodes.QueryRealizesNoParses, Run($"realizes({Absent}, attested)", project: null, withCatalog: false).Refusal!.Code);
    }

    [Fact]
    public void NegatedStemsFileWithAnAbsentMorphIsRefusedAsIncomplete()
    {
        var refused = Run($"not realizes(@{Stems(Root, Absent)}, attested)", project: null, withCatalog: false);
        Assert.Equal(RefusalCodes.QueryRealizesNegationIncomplete, refused.Refusal!.Code);
    }
}

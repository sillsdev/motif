using System.Diagnostics;
using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Store;
using SIL.LCModel;
using SIL.LCModel.DomainServices;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// Times compose, Dry Run and apply of a Proposal far larger than the old 1 MiB job-input cap. It runs only when
/// <c>MOTIF_BENCH_ENTRIES</c> names the number of lexical entries to author, so the ordinary suite skips it.
/// </summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group0)]
public sealed class BulkProposalBenchmark(PristineProjectFixture pristine, ITestOutputHelper output)
{
    [Fact]
    public void ABulkProposalLargerThanOneMebibyteComposesDryRunsAndApplies()
    {
        if (!int.TryParse(Environment.GetEnvironmentVariable("MOTIF_BENCH_ENTRIES"), out var entries) || entries < 1)
            return;
        string path, vern, analysis, category;
        using (var cache = pristine.NewScratch())
        {
            path = cache.ProjectId.Path;
            vern = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
            analysis = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
            category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId).Value;
        }
        const string version = "1.0";
        var morphType = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphStem).Value;
        var clock = Stopwatch.StartNew();
        string Lap(string phase)
        {
            var line = $"BENCH {phase}: {clock.Elapsed.TotalSeconds:F1}s";
            output.WriteLine(line);
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "motif-bulk-bench.txt"), line + "\n");
            clock.Restart();
            return line;
        }

        Assert.True(ProposalCommands.New(new NewDraftRequest(path, version, "bulk", null)).Succeeded);
        const int batchSize = 500;
        for (var start = 0; start < entries; start += batchSize)
        {
            var intents = Enumerable.Range(start, Math.Min(batchSize, entries - start)).Select(index => new
            {
                verb = "compose-author-lexical-entry",
                intent = new
                {
                    form = "bulkform" + index, formWs = vern, morphType, gloss = "bulk gloss " + index,
                    analysisWs = analysis, msa = new { kind = "stem", category },
                },
            });
            var composed = ProposalCommands.ComposeBatch(new(path, version, "bulk",
                JsonSerializer.Serialize(new { intents })));
            Assert.True(composed.Succeeded, composed.Refusal?.Message);
        }
        DraftRationale.Author(path, "bulk", "Bulk lexicon", "Measure a large Proposal.");
        var finalized = ProposalCommands.Finalize(new FinalizeRequest(path, version, "bulk"));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var proposalId = finalized.Value!.ProposalId;
        using (var database = ProjectMotifDatabase.Open(path))
        {
            var json = new ProposalRepository(database).Get(CanonicalId.Parse(proposalId)).ProposalJson ?? "";
            var ops = System.Text.RegularExpressions.Regex.Matches(json, "\"operationId\"").Count;
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "motif-bulk-bench.txt"),
                $"BENCH proposal: {ops} operations, {System.Text.Encoding.UTF8.GetByteCount(json)} UTF-8 bytes\n");
        }
        Lap($"compose+finalize {entries} entries");

        var dryRun = DryRunJobRunner.Run(path, version, proposalId);
        Assert.True(dryRun.Succeeded, dryRun.Refusal?.Message);
        Lap("enqueue + dry run");
        using (var database = ProjectMotifDatabase.Open(path))
        {
            var job = new JobRepository(database).ListByProjectAndKind(
                new JobRepository(database).ListActive().FirstOrDefault()?.ProjectKey ?? "", JobCommands.DryRunKind);
            var line = $"BENCH dry-run jobs listed: {job.Count}";
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "motif-bulk-bench.txt"), line + "\n");
        }
        clock.Restart();

        var applied = ProposalCommands.Apply(new ApplyRequest(path, version, proposalId, Force: true));
        Assert.True(applied.Succeeded, applied.Refusal?.Message);
        Lap("apply");
    }
}

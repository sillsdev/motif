using System.Diagnostics;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.Worker;

[Collection(LcmCacheParallelCollections.Group0)]
public sealed class ProgressCapacityTests(PristineProjectFixture pristine, ITestOutputHelper output)
{
    [Fact]
    public Task CebScaleCopyServesSummaryAndPage() => RunCapacityCase(enforceTimeLimit: false);

    [ProgressSlowScaleFact]
    [Trait("Category", "SlowScale")]
    public Task CebScaleCopyServesSummaryAndPageWithinOneSecond() => RunCapacityCase(enforceTimeLimit: true);

    [ProgressSlowScaleFact]
    [Trait("Category", "SlowScale")]
    public Task SixtyTwoThousandTypesUpdateWithinOneSecond() => RunCapacityCase(enforceTimeLimit: true, large: true);

    private async Task RunCapacityCase(bool enforceTimeLimit, bool large = false)
    {
        var path = pristine.CopyProjectFile();
        var root = Path.Combine(Path.GetTempPath(), "motif-progress-capacity", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            SeededText seededText;
            using (var cache = new FwDataProjectLoader().LoadScratchCache(path))
            {
                seededText = SeededProject.SeedText(cache, pristine.Seed);
                new FwDataProjectLoader().Save(cache);
            }
            var common = seededText.AnalysedWordformId;
            var texts = new List<ProgressText>();
            for (var i = 0; i < 2378; i++)
            {
                var incidence = new List<ProgressIncidence>
                {
                    new(common, seededText.ApprovedAnalysisId, "ceb", "common", "unmeasured", "parser-evidence-unavailable", 1),
                };
                for (var j = i; j < (large ? 61999 : 21499); j += 2378)
                    incidence.Add(new ProgressIncidence(Guid.NewGuid(), null, "ceb", "word" + j,
                        "unmeasured", "parser-evidence-unavailable", j < 3658 ? 37 : 36));
                texts.Add(new ProgressText(Guid.NewGuid(), "Text " + i, incidence, new Dictionary<string, int>()));
            }
            var seeded = ProjectStoreCommand.Run(path, MotifProductVersion.CurrentText, (database, _) =>
            {
                using var connection = database.OpenConnection();
                using var transaction = connection.BeginTransaction();
                ProgressRepository.Publish(connection, transaction, new ProgressProjection(texts), "content/copy",
                    "judgement/copy", File.GetLastWriteTimeUtc(path));
                transaction.Commit();
                return CommandOutcome<string>.Success("seeded");
            });
            Assert.True(seeded.Succeeded, seeded.Refusal?.Message);
            foreach (var detail in new[] { "summary", "passages" })
            {
                var queries = 0;
                var rowsRead = 0;
                ProgressRepository.TextRowsRead.Value = count => { queries++; rowsRead += count; };
                var watch = Stopwatch.StartNew();
                CommandOutcome<ProgressResponse> result;
                try { result = ProgressQuery.Query(new ProgressRequest(path, new ProgressScope("texts"), detail)); }
                finally { ProgressRepository.TextRowsRead.Value = null; }
                watch.Stop();
                Assert.True(result.Succeeded, result.Refusal?.Message);
                Assert.Equal(detail == "summary" ? 0 : 1, queries);
                Assert.Equal(detail == "summary" ? 0 : 51, rowsRead);
                if (detail == "summary")
                {
                    Assert.Equal(large ? 62000 : 21500, result.Value!.Totals!.Types.Total);
                    Assert.Equal(large ? 2238000 : 780000, result.Value.Totals.Tokens.Total);
                }
                else Assert.Equal(50, result.Value!.Rows.Count);
                output.WriteLine($"{detail} in-process: {watch.Elapsed.TotalMilliseconds:F1} ms");
                if (enforceTimeLimit && !large)
                    Assert.True(watch.ElapsedMilliseconds <= 1000, $"{detail} in-process took {watch.ElapsedMilliseconds} ms");

                var start = CliProcess.CreateStartInfo(root, null, false,
                    "progress", "--project", path, "--detail", detail, "--json");
                CliProcess.EnableAdvancedAiMode(start, root);
                watch.Restart();
                var cli = await CliProcess.RunAsync(start);
                watch.Stop();
                Assert.True(cli.ExitCode == 0, cli.Error + cli.Output);
                output.WriteLine($"{detail} cold CLI: {watch.Elapsed.TotalMilliseconds:F1} ms");
                if (enforceTimeLimit && !large)
                    Assert.True(watch.ElapsedMilliseconds <= 1000, $"{detail} cold CLI took {watch.ElapsedMilliseconds} ms");
            }

            var selectedRows = 0;
            var selectedQueries = 0;
            ProgressRepository.TextRowsRead.Value = count => { selectedQueries++; selectedRows += count; };
            try
            {
                var scope = new ProgressScope("texts", [texts[^1].Id]);
                Assert.True(ProgressQuery.Query(new ProgressRequest(path, scope, "passages", 1)).Succeeded);
                Assert.True(ProgressQuery.Query(new ProgressRequest(path, scope, "passages", 1)).Succeeded);
            }
            finally { ProgressRepository.TextRowsRead.Value = null; }
            Assert.Equal(3, selectedQueries);
            Assert.Equal(3, selectedRows);

            using var updatedCache = new FwDataProjectLoader().LoadScratchCache(path);
            var analysis = updatedCache.ServiceLocator.GetInstance<IWfiAnalysisRepository>()
                .GetObject(seededText.ApprovedAnalysisId);
            NonUndoableUnitOfWorkHelper.Do(updatedCache.ActionHandlerAccessor,
                () => updatedCache.LangProject.DefaultUserAgent.SetEvaluation(analysis, Opinions.approves));
            var marked = ProjectStoreCommand.Run(path, MotifProductVersion.CurrentText, (database, _) =>
            {
                using var connection = database.OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE ProgressState SET PendingReceiptId = 'receipt/capacity' WHERE Id = 1;";
                command.ExecuteNonQuery();
                return CommandOutcome<string>.Success("marked");
            });
            Assert.True(marked.Succeeded, marked.Refusal?.Message);
            var deltaWatch = Stopwatch.StartNew();
            var published = ProjectStoreCommand.Run(path, MotifProductVersion.CurrentText, (database, _) =>
            {
                var pending = ProgressRepository.ReadPending(database, "receipt/capacity");
                Assert.NotNull(pending);
                var updated = ProgressDeltaBuilder.Apply(updatedCache, pending.Value.Projection,
                    [new OperationEnvelope(CanonicalId.Mint(), WfiAnalysisOperationKinds.AddRefEvaluations,
                        target: CanonicalId.FromGuid(analysis.Guid))]);
                Assert.NotNull(updated);
                ProgressRepository.PublishApplied(database, updated, "receipt/capacity", File.GetLastWriteTimeUtc(path),
                    pending.Value.State.ContentRevision, pending.Value.Projection);
                return CommandOutcome<string>.Success("published");
            });
            deltaWatch.Stop();
            Assert.True(published.Succeeded, published.Refusal?.Message);
            var after = ProgressQuery.Query(new ProgressRequest(path, new ProgressScope("texts")));
            Assert.True(after.Succeeded, after.Refusal?.Message);
            Assert.Equal(2378, after.Value!.Totals!.Tokens.Approved);
            Assert.Equal(large ? 2238000 : 780000, after.Value.Totals.Tokens.Total);
            output.WriteLine($"common-word delta and publication: {deltaWatch.Elapsed.TotalMilliseconds:F1} ms");
            if (enforceTimeLimit)
                Assert.True(deltaWatch.ElapsedMilliseconds <= 1000,
                    $"common-word delta and publication took {deltaWatch.ElapsedMilliseconds} ms");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}

internal sealed class ProgressSlowScaleFactAttribute : FactAttribute
{
    public ProgressSlowScaleFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("MOTIF_SLOW_SCALE") != "1")
            Skip = "Set MOTIF_SLOW_SCALE=1 to run representative-scale measurements.";
    }
}

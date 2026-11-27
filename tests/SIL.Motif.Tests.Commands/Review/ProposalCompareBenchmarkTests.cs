using System.Diagnostics;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Review;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.HandAnalysis;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;
using SIL.Motif.Host.HandAnalysis;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;
using static SIL.Motif.Tests.TestFixtures.ProposalCompareExports;

namespace SIL.Motif.Tests.Commands.Review;

public sealed class ProposalCompareBenchmarkTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private sealed class BenchmarkFactAttribute : FactAttribute
    {
        public BenchmarkFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("MOTIF_COMPARE_BENCH") != "1")
                Skip = "Timing benchmark; set MOTIF_COMPARE_BENCH=1 to run it.";
        }
    }

    [BenchmarkFact]
    public void CompareCostGrowsLinearlyWithTheFormsOfTwoTrials()
    {
        foreach (var forms in new[] { 100, 1_000, 10_000 })
            for (var run = 1; run <= 3; run++)
                Measure(forms, run);
    }

    private void Measure(int count, int run)
    {
        var root = Directory.CreateTempSubdirectory("motif-compare-bench-").FullName;
        try
        {
            var path = Path.Combine(root, "project.fwdata");
            File.WriteAllText(path, "");
            var project = new ProjectLocator(path, "project");
            using var database = MotifDatabase.OpenOwned(ProjectDatabaseCatalog.DatabasePathFor(project), project,
                MotifSchema.CurrentSchema, new(1, 0));
            var specs = Enumerable.Range(0, count).Select(index => new FormSpec($"w{index:00000}", [1], [1, 2])).ToList();
            var a = AddTrial(database, project, specs);
            var b = AddTrial(database, project, specs.Select(spec => spec with { After = [1] }));
            var loader = new ProposalCompareReads.ApprovedLoader((_, _, _) =>
                CommandOutcome<ProposalCompareReads.ApprovedBasis>.Success(new("stamp", ApprovedReadingSet.From([], 0))));
            CommandOutcome<SIL.Motif.Contract.Responses.ProposalComparison> Compare(int? page, string? snapshot) =>
                ProposalCompareReads.Read(new(path, [a, b], null, page, snapshot, null), loader);

            var jobs = new[] { a, b }.Select(id => Trials.NewestFirst(Trials.JobsFor(database, project, CanonicalId.Parse(id)))[0].JobId).ToArray();
            var exportPages = TrialExportArtifact.Read(database, jobs[0])!.Pages;
            var readBytes = GC.GetAllocatedBytesForCurrentThread();
            var readTimer = Stopwatch.StartNew();
            foreach (var job in jobs)
                for (var page = 1; page <= exportPages; page++)
                    Assert.NotNull(TrialExportArtifact.Read(database, job, page: page)!.Page);
            var storeReadMs = readTimer.Elapsed.TotalMilliseconds;
            var storeReadMb = (GC.GetAllocatedBytesForCurrentThread() - readBytes) / 1048576.0;

            var bytes = GC.GetAllocatedBytesForCurrentThread();
            var timer = Stopwatch.StartNew();
            var summary = Compare(null, null).Value!;
            var coldMs = timer.Elapsed.TotalMilliseconds;
            var coldMb = (GC.GetAllocatedBytesForCurrentThread() - bytes) / 1048576.0;

            bytes = GC.GetAllocatedBytesForCurrentThread();
            timer.Restart();
            for (var page = 1; page <= summary.Pages; page++)
                Assert.True(Compare(page, summary.Snapshot).Succeeded);
            var pagesMs = timer.Elapsed.TotalMilliseconds;
            var pagesMb = (GC.GetAllocatedBytesForCurrentThread() - bytes) / 1048576.0;

            Assert.Equal(1, ProposalCompareReads.JoinsComputedFor(database.FullPath));
            output.WriteLine($"forms={count} run={run} rows={summary.RowsTotal} comparePages={summary.Pages} exportPages={exportPages} " +
                $"storeReadMs={storeReadMs:F1} storeReadMB={storeReadMb:F1} coldSummaryMs={coldMs:F1} coldMB={coldMb:F1} coldKBPerForm={coldMb * 1024 / count:F1} warmAllPagesMs={pagesMs:F1} " +
                $"warmMsPerPage={pagesMs / summary.Pages:F2} warmMB={pagesMb:F1}");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}

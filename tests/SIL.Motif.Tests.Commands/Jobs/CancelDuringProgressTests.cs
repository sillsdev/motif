using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Jobs;

public sealed class CancelDuringProgressTests
{
    [Fact]
    public async Task AppCancelCommandSurvivesProgressWritesOnEveryFreshJob()
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-cancel-command-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "project.fwdata");
            File.WriteAllText(path, "");
            var project = new ProjectLocator(path, "project");
            var databasePath = ProjectDatabaseCatalog.DatabasePathFor(project);
            using var database = MotifDatabase.OpenOwned(databasePath, project, MotifSchema.CurrentSchema,
                new Version(1, 0));
            var jobs = new JobRepository(database);
            for (var iteration = 0; iteration < 12; iteration++)
            {
                var jobId = "job-" + iteration;
                var queued = jobs.Create(new JobRecord(jobId, ProjectWorkspaceKey.Compute(project), "dry-run",
                    JobStatus.Queued, 1, "{\"proposal\":[]}", null, "2026-09-25T00:00:00Z",
                    "2026-09-25T00:00:00Z"));
                jobs.Transition(queued.JobId, JobStatus.Running);
                var firstWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var writer = Task.Run(() =>
                {
                    using var writerDatabase = MotifDatabase.OpenOwned(databasePath, project,
                        MotifSchema.CurrentSchema, new Version(1, 0));
                    var writerJobs = new JobRepository(writerDatabase);
                    for (var progress = 0; progress < 100; progress++)
                    {
                        try
                        {
                            var current = writerJobs.Get(jobId)!;
                            writerJobs.UpdateProgress(jobId, "{\"completed\":" + progress + "}", current.Version);
                            firstWrite.TrySetResult();
                        }
                        catch (InvalidOperationException) { }
                        Thread.Yield();
                    }
                });
                await firstWrite.Task.WaitAsync(TimeSpan.FromSeconds(5));
                var cancelled = JobCommands.Cancel(new CancelJobRequest(path, jobId, "1.0"));
                Assert.True(cancelled.Succeeded, cancelled.Refusal?.Message);
                Assert.True(cancelled.Value!.CancellationRequested);
                await writer;
                Assert.True(jobs.Get(jobId)!.CancellationRequested);
            }
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}

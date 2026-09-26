using System.Collections;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheTestCollection.Name)]
public sealed class ProcessRunnerLauncherTests(PristineProjectFixture pristine)
{
    private static readonly string[] RunnerVariables =
    [
        RunnerOptions.RootVariable,
        RunnerOptions.NamespaceVariable,
        RunnerOptions.IdleVariable,
        RunnerOptions.LeaseVariable,
        ProcessRunnerLauncher.ExecutableVariable,
        ProcessRunnerLauncher.SuppressVariable,
    ];

    [Fact]
    public async Task ALaunchedRunnerDrainsTheQueueUnderItsOwnRootWithoutProcessEnvironment()
    {
        var before = MotifVariables();
        Assert.All(RunnerVariables, name => Assert.Null(Environment.GetEnvironmentVariable(name)));
        using var scratch = pristine.NewScratch();
        var path = scratch.ProjectId.Path;
        Guid wordformId = Guid.Empty;
        NonUndoableUnitOfWorkHelper.Do(scratch.ActionHandlerAccessor, () =>
            wordformId = scratch.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString("launched-word", scratch.DefaultVernWs)).Guid);
        new FwDataProjectLoader().Save(scratch);
        var root = Path.Combine(Path.GetDirectoryName(path)!, "launched-runner-root");
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root).Succeeded);
        var version = MotifProductVersion.CurrentText;
        var initial = PendingChanges.Load(new PendingChangesRequest(path, version)).Value!;
        var pending = PendingChanges.Put(new PutPendingChangeRequest(path, version, initial.Revision,
            new ChangeIntent(CanonicalId.Mint().Value, "incorrect-spelling",
                CanonicalId.FromGuid(wordformId).Value, "launched-word"))).Value!;

        var measured = await PendingChangesWorkflow.Measure(new MeasurePendingRequest(path, pending.DraftId!,
            pending.Revision, ["launched-word"]), new Progress<MeasureProgress>(), CancellationToken.None,
            TimeSpan.FromSeconds(60), IsolatedRunner.Process(root));

        Assert.True(measured.Succeeded, measured.Refusal?.Message);
        Assert.Equal(JobStatus.Completed, JobCommands.Show(new ShowJobRequest(path,
            measured.Value!.JobId, version)).Value!.Status);
        Assert.True(File.Exists(Path.Combine(root, "motif.db")), "The runner did not open its root's machine database.");
        Assert.Equal(before, MotifVariables());
    }

    [Fact]
    public void EveryRunnerSettingTravelsAsALaunchArgumentAndTheExecutableDoesNot()
    {
        var directory = Path.Combine(Path.GetTempPath(), "motif-launch-arguments-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var parser = Path.Combine(directory, "pangloss.exe");
            File.WriteAllText(parser, "");
            var executable = Path.Combine(directory, "runner.exe");
            var options = new JobRunnerLaunchOptions(Path.Combine(directory, "root"), parser)
            {
                WorkerExecutable = executable,
                OwnerNamespace = "launch-namespace",
                IdleTimeout = TimeSpan.FromMilliseconds(1500),
                Lease = TimeSpan.FromMilliseconds(2500),
            };

            var arguments = ProcessRunnerLauncher.LaunchArguments(options).ToArray();
            var read = RunnerOptions.Read(arguments);

            Assert.Equal(options.Root, read.Root);
            Assert.Equal(parser, read.ParserPath);
            Assert.Equal("launch-namespace", read.OwnerNamespace);
            Assert.Equal(TimeSpan.FromMilliseconds(1500), read.IdleTimeout);
            Assert.Equal(TimeSpan.FromMilliseconds(2500), read.Lease);
            Assert.DoesNotContain(executable, arguments);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AConfiguredRunnerThatDoesNotExistIsNotStarted()
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-missing-runner-" + Guid.NewGuid().ToString("N"));
        var launcher = new ProcessRunnerLauncher(IsolatedRunner.Options(root) with
        {
            WorkerExecutable = Path.Combine(root, "absent", "SIL.Motif.Worker.exe"),
        });
        var warnings = new List<string>();

        launcher.Start(Path.Combine(root, "project.fwdata"), warnings.Add);

        Assert.Empty(warnings);
        Assert.False(Directory.Exists(root));
    }

    private static SortedDictionary<string, string?> MotifVariables()
    {
        var variables = new SortedDictionary<string, string?>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
            if (entry.Key is string name && name.StartsWith("MOTIF_", StringComparison.OrdinalIgnoreCase))
                variables[name] = entry.Value as string;
        return variables;
    }
}

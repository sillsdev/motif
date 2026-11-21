using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
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

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group0)]
public sealed class ProcessRunnerLauncherTests(PristineProjectFixture pristine)
{
    private static readonly string[] RunnerVariables =
    [
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
        // Only the isolation every test process sets; the launch arguments must win over it.
        Assert.Equal(ProcessRunnerEnvironmentInitializer.Namespace,
            Environment.GetEnvironmentVariable(RunnerOptions.NamespaceVariable));
        Assert.Equal(ProcessRunnerEnvironmentInitializer.Root,
            Environment.GetEnvironmentVariable(RunnerOptions.RootVariable));
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
            var parser = Path.Combine(directory, FakeParser.ExecutableFileName);
            File.WriteAllText(parser, "");
            var executable = Path.Combine(directory, OperatingSystem.IsWindows() ? "runner.exe" : "runner");
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
    public void AConfiguredRunnerThatDoesNotExistIsNotStartedAndSaysSo()
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-missing-runner-" + Guid.NewGuid().ToString("N"));
        var launcher = new ProcessRunnerLauncher(IsolatedRunner.Options(root) with
        {
            WorkerExecutable = Path.Combine(root, "absent",
                OperatingSystem.IsWindows() ? "SIL.Motif.Worker.exe" : "SIL.Motif.Worker"),
        });
        var warnings = new List<string>();

        launcher.Start(Path.Combine(root, "project.fwdata"), warnings.Add);

        var warning = Assert.Single(warnings);
        Assert.Contains("does not exist", warning, StringComparison.Ordinal);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void ALiveRunnerFromAnotherBuildIsNamedAndWarnedAboutWhenAnotherLaunchFindsIt()
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-stale-runner-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(RunnerIdentity.PathFor(root), Environment.ProcessId + "\nolder-build");
            var launcher = new ProcessRunnerLauncher(IsolatedRunner.Options(root) with
            {
                WorkerExecutable = Path.Combine(root, "absent", "SIL.Motif.Worker"),
            });
            var warnings = new List<string>();

            launcher.Start(Path.Combine(root, "project.fwdata"), warnings.Add);

            var stale = Assert.Single(warnings, warning => warning.Contains("older-build", StringComparison.Ordinal));
            Assert.Contains(Environment.ProcessId.ToString(), stale, StringComparison.Ordinal);
            Assert.Contains(RunnerIdentity.BuildId, stale, StringComparison.Ordinal);
            Assert.Contains("stop", stale, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ARunnerOfTheSameBuildOrOneThatIsGoneRaisesNoWarning()
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-same-runner-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            RunnerIdentity.Publish(root);
            Assert.Null(RunnerIdentity.StaleWarning(root));
            File.WriteAllText(RunnerIdentity.PathFor(root), int.MaxValue + "\nolder-build");
            Assert.Null(RunnerIdentity.StaleWarning(root));
            File.Delete(RunnerIdentity.PathFor(root));
            Assert.Null(RunnerIdentity.StaleWarning(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ALaunchedRunnerDoesNotInheritALockTheCallerHolds()
    {
        if (!OperatingSystem.IsLinux())
            return;
        var directory = Path.Combine(Path.GetTempPath(), "motif-fd-leak-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var lockFile = Path.Combine(directory, "caller.lock");
        File.WriteAllText(lockFile, "");
        var pidFile = Path.Combine(directory, "runner.pid");
        var runner = Path.Combine(directory, "SIL.Motif.Worker");
        File.WriteAllText(runner, "#!/bin/sh\necho $$ > '" + pidFile + ".tmp'\nmv '" + pidFile + ".tmp' '" + pidFile +
            "'\nexec sleep 60\n");
        File.SetUnixFileMode(runner, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        // Opened without O_CLOEXEC, as `flock <file> motif ...` leaves it.
        var descriptor = FdLock.Open(lockFile);
        Process? runnerProcess = null;
        try
        {
            Assert.True(descriptor >= 0, "The lock file did not open.");
            Assert.Equal(0, FdLock.Flock(descriptor, FdLock.Exclusive));
            var launcher = new ProcessRunnerLauncher(IsolatedRunner.Options(Path.Combine(directory, "root")) with
            {
                WorkerExecutable = runner,
            });

            launcher.Start(Path.Combine(directory, "project.fwdata"));

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (!File.Exists(pidFile) && DateTime.UtcNow < deadline)
                Thread.Sleep(20);
            Assert.True(File.Exists(pidFile), "The fake runner never started.");
            runnerProcess = Process.GetProcessById(int.Parse(File.ReadAllText(pidFile).Trim(), CultureInfo.InvariantCulture));
            // The caller closes its descriptor; the lock outlives that only in a process that inherited it.
            FdLock.Close(descriptor);
            descriptor = -1;
            var other = FdLock.Open(lockFile);
            try
            {
                Assert.Equal(0, FdLock.Flock(other, FdLock.Exclusive | FdLock.NonBlocking));
            }
            finally
            {
                FdLock.Close(other);
            }
        }
        finally
        {
            try { runnerProcess?.Kill(); }
            catch (InvalidOperationException) { }
            runnerProcess?.Dispose();
            FdLock.Close(descriptor);
            Directory.Delete(directory, recursive: true);
        }
    }

    private static class FdLock
    {
        public const int Exclusive = 2;
        public const int NonBlocking = 4;

        public static int Open(string path) => open(path, 2);

        public static int Flock(int descriptor, int operation) => flock(descriptor, operation);

        public static void Close(int descriptor)
        {
            if (descriptor >= 0)
                _ = close(descriptor);
        }

        [DllImport("libc", SetLastError = true)]
        private static extern int open(string path, int flags);

        [DllImport("libc", SetLastError = true)]
        private static extern int flock(int fd, int operation);

        [DllImport("libc", SetLastError = true)]
        private static extern int close(int fd);
    }

    /// <summary>Only the variables a launcher could set or leak, so sibling tests do not race it.</summary>
    private static SortedDictionary<string, string?> MotifVariables()
    {
        var variables = new SortedDictionary<string, string?>(StringComparer.Ordinal);
        foreach (var name in RunnerVariables.Append(RunnerOptions.NamespaceVariable).Append(RunnerOptions.RootVariable))
            variables[name] = Environment.GetEnvironmentVariable(name);
        return variables;
    }
}

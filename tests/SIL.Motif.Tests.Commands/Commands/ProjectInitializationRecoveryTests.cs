using System.Security.Cryptography;
using System.Diagnostics;
using System.Reflection;
using SIL.LCModel;
using SIL.LCModel.Core.Cellar;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.WritingSystems;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.LiveHost.HumanJudgments;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group0)]
public sealed class ProjectInitializationRecoveryTests : IDisposable
{
    private const string Confirmation = "Initialize this project to work with Motif Proposals?";
    private const string CrashChildVariable = "MOTIF_INITIALIZATION_CRASH_CHILD";
    private const string CrashProjectVariable = "MOTIF_INITIALIZATION_CRASH_PROJECT";
    private const string CrashWorkerRootVariable = "MOTIF_INITIALIZATION_CRASH_WORKER_ROOT";
    private const string ChildReadyPathVariable = "MOTIF_INITIALIZATION_CHILD_READY";
    private const string ChildReleasePathVariable = "MOTIF_INITIALIZATION_CHILD_RELEASE";
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "motif-project-initialization-" + Guid.NewGuid().ToString("N"));
    private readonly string _workerRoot;

    public ProjectInitializationRecoveryTests()
    {
        Directory.CreateDirectory(_root);
        _workerRoot = Path.Combine(_root, "worker");
        Directory.CreateDirectory(_workerRoot);
    }

    [Fact]
    public void InitializationXmlSaveReopensOneCompatibleDefinition()
    {
        var project = CreateProject(seed: true);
        var before = ReadProjectIdentity(project);

        var outcome = Initialize(project);

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        Assert.Equal("initialized", outcome.Value!.Status);
        Assert.Equal(before, ReadProjectIdentity(project));
        using var cache = new FwDataProjectLoader().LoadScratchCache(project);
        var metadata = (IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor;
        var field = Assert.Single(ReservedFields(cache));
        Assert.Equal("RnGenericRec", cache.MetaDataCacheAccessor.GetClassName(
            cache.MetaDataCacheAccessor.GetOwnClsId(field)));
        Assert.Equal((int)CellarPropertyType.String, metadata.GetFieldType(field));
        Assert.Equal(WritingSystemServices.kwsAnal, cache.MetaDataCacheAccessor.GetFieldWs(field));
        Assert.Equal(0, cache.MetaDataCacheAccessor.GetDstClsId(field));
        Assert.Equal(Guid.Empty, metadata.GetFieldListRoot(field));
    }

    [Fact]
    public void InitializationPersistsMetadataWhenNoOrdinaryProjectDataChanged()
    {
        var project = CreateProject(seed: false);
        var before = File.ReadAllBytes(project);

        var outcome = Initialize(project);

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        Assert.NotEqual(before, File.ReadAllBytes(project));
        Assert.Single(ReservedFields(project));
    }

    [Fact]
    public void InitializationTwiceDoesNotWrite()
    {
        var project = CreateProject(seed: true);
        Assert.True(Initialize(project).Succeeded);
        var before = SHA256.HashData(File.ReadAllBytes(project));

        var loader = new CountingSaveLoader();
        var outcome = Initialize(project, loader);

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        Assert.Equal("already-initialized", outcome.Value!.Status);
        Assert.Equal(0, loader.SaveCount);
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(project)));
    }

    [Fact]
    public void InitializationFailureBeforeSaveLeavesOriginalUnchanged()
    {
        var project = CreateProject(seed: true);
        var original = File.ReadAllBytes(project);
        var outcome = Initialize(project, new FailingSaveLoader(project, corruptAfterSave: false));

        Assert.False(outcome.Succeeded);
        Assert.NotNull(outcome.Refusal);
        Assert.Equal(original, File.ReadAllBytes(project));
        Assert.Empty(ReservedFields(project));
        Assert.False(Directory.Exists(Path.Combine(_workerRoot, "initialization-recovery")));
    }

    [Fact]
    public void InitializationRequiresTheExactConfirmationBeforeReadingOrWritingTheProject()
    {
        var project = CreateProject(seed: true);
        var original = File.ReadAllBytes(project);

        var outcome = ProjectInitializationCommand.Initialize(
            new ProjectInitializationRequest(project, "yes"), _workerRoot);

        Assert.False(outcome.Succeeded);
        Assert.Equal("initialization.confirmation-required", outcome.Refusal!.Code);
        Assert.Equal(original, File.ReadAllBytes(project));
        Assert.False(Directory.Exists(Path.Combine(_workerRoot, "initialization-recovery")));
    }

    [Fact]
    public void InitializationCancellationBeforeMutationLeavesOriginalUnchanged()
    {
        var project = CreateProject(seed: true);
        var original = File.ReadAllBytes(project);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var loader = new CountingLoadLoader();

        var outcome = ProjectInitializationCommand.Initialize(
            new ProjectInitializationRequest(project, Confirmation), _workerRoot, loader, cancellation.Token);

        Assert.False(outcome.Succeeded);
        Assert.Equal("initialization.cancelled", outcome.Refusal!.Code);
        Assert.Equal(0, loader.LoadCount);
        Assert.Equal(original, File.ReadAllBytes(project));
        Assert.False(Directory.Exists(Path.Combine(_workerRoot, "initialization-recovery")));
    }

    [Fact]
    public void InitializationDoesNotSaveWhileAUnitOfWorkIsOpen()
    {
        var project = CreateProject(seed: true);
        var loader = new ClosedUnitSaveLoader();

        var outcome = Initialize(project, loader);

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        Assert.Equal(1, loader.SaveCount);
        Assert.False(loader.UnitWasOpenAtSave, loader.UnitStateAtSave);
    }

    [Fact]
    public void InitializationRefusesWhenTheProjectHasNoAnalysisWritingSystem()
    {
        var project = CreateProject(seed: true);
        var original = File.ReadAllBytes(project);
        var outcome = Initialize(project, new NoAnalysisWritingSystemLoader());

        Assert.False(outcome.Succeeded);
        Assert.Equal("initialization.analysis-writing-system-required", outcome.Refusal!.Code);
        Assert.Equal(original, File.ReadAllBytes(project));
        Assert.Empty(ReservedFields(project));
    }

    [Fact]
    public void OrdinaryDataRollbackDoesNotUndoMetadataMutation()
    {
        var project = CreateProject(seed: false);
        using var cache = new FwDataProjectLoader().LoadCache(project);
        var metadata = (IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor;

        Assert.Throws<InvalidOperationException>(() => UndoableUnitOfWorkHelper.Do(
            "Rollback metadata control", "Rollback metadata control", cache.ActionHandlerAccessor, () =>
            {
                metadata.AddCustomField("RnGenericRec", "MotifRollbackControl", CellarPropertyType.String,
                    0, "control", WritingSystemServices.kwsAnal, Guid.Empty);
                throw new InvalidOperationException("Rollback the ordinary data unit of work.");
            }));

        Assert.Contains(metadata.GetFieldIds(), flid =>
            metadata.GetFieldName(flid) == "MotifRollbackControl");
    }

    [Fact]
    public void InitializationSaveFailureRetainsRecoveryAndClassifiesOutcome()
    {
        var project = CreateProject(seed: true);
        var original = File.ReadAllBytes(project);
        var identity = ReadProjectIdentity(project);

        var outcome = Initialize(project, new FailingSaveLoader(project, corruptAfterSave: true));

        Assert.False(outcome.Succeeded);
        Assert.Equal("initialization.outcome-unknown", outcome.Refusal!.Code);
        Assert.True(outcome.Refusal.Facts!.TryGetValue("recoveryCopyPath", out var recoveryPath));
        Assert.Equal(original, File.ReadAllBytes(recoveryPath!));
        using var recovery = new FwDataProjectLoader().LoadScratchCache(recoveryPath!);
        Assert.Equal(identity, recovery.LangProject.Guid);

        var retry = Initialize(project);
        Assert.False(retry.Succeeded);
        Assert.Equal("initialization.outcome-unknown", retry.Refusal!.Code);
        Assert.Equal(recoveryPath, retry.Refusal.Facts!["recoveryCopyPath"]);
        Assert.Equal(original, File.ReadAllBytes(recoveryPath!));
    }

    [Fact]
    public async Task InitializationCrashAfterSaveRetriesAsNoOp()
    {
        var childMode = Environment.GetEnvironmentVariable(CrashChildVariable);
        if (childMode == "hold-project-open")
        {
            using var heldCache = new FwDataProjectLoader().LoadCache(
                Environment.GetEnvironmentVariable(CrashProjectVariable)!);
            await File.WriteAllTextAsync(Environment.GetEnvironmentVariable(ChildReadyPathVariable)!, string.Empty);
            await WaitForFileAsync(Environment.GetEnvironmentVariable(ChildReleasePathVariable)!, TimeSpan.FromSeconds(30));
            return;
        }
        if (childMode == "after-save")
        {
            var childProject = Environment.GetEnvironmentVariable(CrashProjectVariable)!;
            var childWorkerRoot = Environment.GetEnvironmentVariable(CrashWorkerRootVariable)!;
            ProjectInitializationCommand.Initialize(
                new ProjectInitializationRequest(childProject, Confirmation),
                childWorkerRoot,
                new ExitAfterSaveLoader());
            throw new InvalidOperationException("The initialization crash point was not reached.");
        }

        var project = CreateProject(seed: true);
        var original = File.ReadAllBytes(project);
        var recoveryCopy = ProjectInitializationRecovery.GetRecoveryCopyPath(project, _workerRoot);
        using var child = StartChild("after-save", project);
        child.StandardInput.Close();
        var output = child.StandardOutput.ReadToEndAsync();
        var error = child.StandardError.ReadToEndAsync();
        if (!child.WaitForExit((int)TimeSpan.FromSeconds(90).TotalMilliseconds))
        {
            child.Kill(entireProcessTree: true);
            throw new TimeoutException("The initialization crash child did not exit after its save.");
        }
        var childOutput = await output;
        var childError = await error;
        Assert.NotEqual(0, child.ExitCode);
        Assert.Equal(original, File.ReadAllBytes(recoveryCopy));

        var beforeRetry = SHA256.HashData(File.ReadAllBytes(project));
        var loader = new CountingSaveLoader();
        var retry = Initialize(project, loader);

        Assert.True(retry.Succeeded, retry.Refusal?.Message);
        Assert.Equal("already-initialized", retry.Value!.Status);
        Assert.Equal(0, loader.SaveCount);
        Assert.Equal(beforeRetry, SHA256.HashData(File.ReadAllBytes(project)));
        Assert.NotEqual(original, File.ReadAllBytes(project));
        Assert.False(File.Exists(recoveryCopy));
        Assert.Contains("crash", childOutput + childError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InitializationRefusesLockedProject()
    {
        var project = CreateProject(seed: true);
        var readyPath = Path.Combine(_root, "child-ready");
        var releasePath = Path.Combine(_root, "child-release");
        using var child = StartChild("hold-project-open", project, readyPath, releasePath);
        child.StandardInput.Close();
        var output = child.StandardOutput.ReadToEndAsync();
        var error = child.StandardError.ReadToEndAsync();
        try
        {
            await WaitForChildFileAsync(child, readyPath, TimeSpan.FromSeconds(30));
            var outcome = Initialize(project);

            Assert.False(outcome.Succeeded);
            Assert.Equal("project.in-use", outcome.Refusal!.Code);
        }
        finally
        {
            await File.WriteAllTextAsync(releasePath, string.Empty);
            if (!child.WaitForExit((int)TimeSpan.FromSeconds(30).TotalMilliseconds))
                child.Kill(entireProcessTree: true);
        }

        Assert.True(child.ExitCode == 0,
            $"Lock-holding child failed: {await error}{Environment.NewLine}{await output}");
    }

    [Fact]
    public void InitializationRefusesIncompatibleAndAmbiguousReservedDefinitions()
    {
        var incompatible = CreateProject(seed: false);
        AddReservedField(incompatible, "RnGenericRec", CellarPropertyType.Integer);
        var incompatibleResult = Initialize(incompatible);
        Assert.False(incompatibleResult.Succeeded);
        Assert.Equal("judgment.field-incompatible", incompatibleResult.Refusal!.Code);

        var wrongOwner = CreateProject(seed: false);
        AddReservedField(wrongOwner, "LexEntry", CellarPropertyType.String);
        var wrongOwnerResult = Initialize(wrongOwner);
        Assert.False(wrongOwnerResult.Succeeded);
        Assert.Equal("judgment.field-incompatible", wrongOwnerResult.Refusal!.Code);

        var wrongWritingSystemSelector = CreateProject(seed: false);
        AddReservedField(wrongWritingSystemSelector, "RnGenericRec", CellarPropertyType.String,
            WritingSystemServices.kwsVern);
        var wrongSelectorResult = Initialize(wrongWritingSystemSelector);
        Assert.False(wrongSelectorResult.Succeeded);
        Assert.Equal("judgment.field-incompatible", wrongSelectorResult.Refusal!.Code);

        var ambiguous = CreateProject(seed: false);
        AddReservedField(ambiguous, "RnGenericRec", CellarPropertyType.String);
        AddReservedField(ambiguous, "LexEntry", CellarPropertyType.String);
        var ambiguousResult = Initialize(ambiguous);
        Assert.False(ambiguousResult.Succeeded);
        Assert.Equal("judgment.field-ambiguous", ambiguousResult.Refusal!.Code);
    }

    [Fact]
    public void InitializationIdentityDoesNotDependOnFieldOrderLabelOrCurrentAnalysisDefault()
    {
        var first = CreateProject(seed: false);
        var second = CreateProject(seed: false);
        AddUnrelatedField(second, "RnGenericRec", "MotifBeforeJudgment", CellarPropertyType.Integer);
        Assert.True(Initialize(first).Succeeded);
        Assert.True(Initialize(second).Succeeded);
        Assert.NotEqual(Assert.Single(ReservedFields(first)), Assert.Single(ReservedFields(second)));

        RenameReservedField(first);
        ChangeFirstAnalysisWritingSystem(first);
        var before = SHA256.HashData(File.ReadAllBytes(first));

        var outcome = Initialize(first);

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        Assert.Equal("already-initialized", outcome.Value!.Status);
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(first)));
    }

    [Theory]
    [InlineData("save-outcome-unknown")]
    [InlineData("reopened-save-outcome-unknown")]
    [InlineData("definition-incompatible")]
    [InlineData("definition-ambiguous")]
    [InlineData("copy-no-longer-matches")]
    [InlineData("not-saved-with-retained-copy")]
    public void EveryRefusalNamingTheRecoveryCopyGivesTheRestoreStep(string scenario)
    {
        // Cleanup keeps the copy only when its directory is read-only, which Windows does not model here.
        if (scenario == "not-saved-with-retained-copy" && OperatingSystem.IsWindows()) return;

        var (project, outcome, before) = RunRefusalScenario(scenario);
        var copy = ProjectInitializationRecovery.GetRecoveryCopyPath(project, _workerRoot);
        var fullProject = Path.GetFullPath(project);

        Assert.False(outcome.Succeeded);
        Assert.NotNull(outcome.Refusal);
        Assert.Equal(copy, outcome.Refusal.Facts!["recoveryCopyPath"]);
        Assert.Contains($"Close FieldWorks, then copy {copy} over {fullProject}.", outcome.Refusal.Message);
        Assert.Equal(before, File.ReadAllBytes(project));
    }

    [Theory]
    [InlineData("initialized")]
    [InlineData("already-initialized")]
    public void SuccessKeepingACopyNamesItWithoutTheRestoreStep(string status)
    {
        // Cleanup keeps the copy only when its directory is read-only, which Windows does not model here.
        if (OperatingSystem.IsWindows()) return;

        string project;
        CommandOutcome<ProjectInitializationResponse> outcome;
        var recoveryDirectory = string.Empty;
        try
        {
            if (status == "initialized")
            {
                project = CreateProject(seed: true);
                recoveryDirectory = Path.GetDirectoryName(
                    ProjectInitializationRecovery.GetRecoveryCopyPath(project, _workerRoot))!;
                outcome = Initialize(project, new ReadOnlyRecoveryDirectoryAfterSaveLoader(recoveryDirectory));
            }
            else
            {
                project = CreateProject(seed: false);
                Assert.True(Initialize(project).Succeeded);
                PlantRecoveryCopy(project, project);
                recoveryDirectory = Path.GetDirectoryName(
                    ProjectInitializationRecovery.GetRecoveryCopyPath(project, _workerRoot))!;
                File.SetUnixFileMode(recoveryDirectory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
                outcome = Initialize(project);
            }
        }
        finally
        {
            if (recoveryDirectory.Length > 0)
                File.SetUnixFileMode(recoveryDirectory,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var copy = ProjectInitializationRecovery.GetRecoveryCopyPath(project, _workerRoot);
        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        Assert.Equal(status, outcome.Value!.Status);
        Assert.Equal(copy, outcome.Value.RecoveryCopyPath);
    }

    private (string Project, CommandOutcome<ProjectInitializationResponse> Outcome, byte[] Before)
        RunRefusalScenario(string scenario)
    {
        switch (scenario)
        {
            case "save-outcome-unknown":
            {
                // Motif restores nothing after the failing save, so the bytes must be the ones it left.
                var project = CreateProject(seed: true);
                var outcome = Initialize(project, new FailingSaveLoader(project, corruptAfterSave: true));
                return (project, outcome, File.ReadAllBytes(project));
            }
            case "reopened-save-outcome-unknown":
            {
                var project = CreateProject(seed: true);
                Initialize(project, new FailingSaveLoader(project, corruptAfterSave: true));
                var before = File.ReadAllBytes(project);
                return (project, Initialize(project), before);
            }
            case "definition-incompatible":
            {
                var project = CreateProject(seed: false);
                AddReservedField(project, "RnGenericRec", CellarPropertyType.Integer);
                PlantRecoveryCopy(project, project);
                var before = File.ReadAllBytes(project);
                return (project, Initialize(project), before);
            }
            case "definition-ambiguous":
            {
                var project = CreateProject(seed: false);
                AddReservedField(project, "RnGenericRec", CellarPropertyType.String);
                AddReservedField(project, "LexEntry", CellarPropertyType.String);
                PlantRecoveryCopy(project, project);
                var before = File.ReadAllBytes(project);
                return (project, Initialize(project), before);
            }
            case "copy-no-longer-matches":
            {
                var project = CreateProject(seed: true);
                var other = CreateProject(seed: true);
                PlantRecoveryCopy(project, other);
                var before = File.ReadAllBytes(project);
                return (project, Initialize(project), before);
            }
            case "not-saved-with-retained-copy":
            {
                var project = CreateProject(seed: true);
                var before = File.ReadAllBytes(project);
                var recoveryDirectory = Path.GetDirectoryName(
                    ProjectInitializationRecovery.GetRecoveryCopyPath(project, _workerRoot))!;
                try
                {
                    var outcome = Initialize(project, new ReadOnlyRecoveryDirectorySaveFailure(recoveryDirectory));
                    return (project, outcome, before);
                }
                finally
                {
                    if (!OperatingSystem.IsWindows())
                        File.SetUnixFileMode(recoveryDirectory,
                            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null);
        }
    }

    private void PlantRecoveryCopy(string project, string sourceProject)
    {
        var copy = ProjectInitializationRecovery.GetRecoveryCopyPath(project, _workerRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
        File.Copy(sourceProject, copy);
    }

    private CommandOutcome<ProjectInitializationResponse> Initialize(
        string project, FwDataProjectLoader? loader = null) =>
        ProjectInitializationCommand.Initialize(
            new ProjectInitializationRequest(project, Confirmation), _workerRoot, loader);

    private string CreateProject(bool seed)
    {
        var root = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var cache = NewLangProjFixture.CreateCache(root);
        if (seed) SeededProject.Seed(cache);
        new FwDataProjectLoader().Save(cache);
        return NewLangProjFixture.FwDataPath(root);
    }

    private static Guid ReadProjectIdentity(string project)
    {
        using var cache = new FwDataProjectLoader().LoadScratchCache(project);
        return cache.LangProject.Guid;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Motif.sln"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not find Motif.sln above the test output directory.");
    }

    private Process StartChild(string mode, string project, string? readyPath = null, string? releasePath = null)
    {
        var testProject = Path.Combine(FindRepositoryRoot(), "tests", "SIL.Motif.Tests.Commands",
            "SIL.Motif.Tests.Commands.csproj");
        var childStart = DotnetTestProcess.CreateTestStartInfo(testProject,
            "FullyQualifiedName=SIL.Motif.Tests.Commands.ProjectInitializationRecoveryTests.InitializationCrashAfterSaveRetriesAsNoOp");
        childStart.RedirectStandardInput = true;
        childStart.Environment.Remove(ShardedTestFramework.ShardVariable);
        childStart.Environment.Remove(ShardedTestFramework.WeightsVariable);
        childStart.Environment[CrashChildVariable] = mode;
        childStart.Environment[CrashProjectVariable] = project;
        childStart.Environment[CrashWorkerRootVariable] = _workerRoot;
        if (readyPath is not null) childStart.Environment[ChildReadyPathVariable] = readyPath;
        if (releasePath is not null) childStart.Environment[ChildReleasePathVariable] = releasePath;
        return Process.Start(childStart)
            ?? throw new InvalidOperationException("Could not start the initialization recovery child.");
    }

    private static async Task WaitForChildFileAsync(Process child, string path, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!File.Exists(path))
        {
            if (child.HasExited)
            {
                await child.WaitForExitAsync();
                throw new InvalidOperationException($"The lock-holding child exited early with code {child.ExitCode}.");
            }
            if (DateTime.UtcNow >= deadline)
            {
                child.Kill(entireProcessTree: true);
                throw new TimeoutException("The lock-holding child did not acquire the project in time.");
            }
            await Task.Delay(20);
        }
    }

    private static async Task WaitForFileAsync(string path, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!File.Exists(path))
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("The parent did not release the project lock in time.");
            await Task.Delay(20);
        }
    }

    private static int[] ReservedFields(LcmCache cache) =>
        ((IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor).GetFieldIds()
            .Where(flid => cache.MetaDataCacheAccessor.GetFieldName(flid) == "MotifHumanJudgment")
            .ToArray();

    private static int[] ReservedFields(string project)
    {
        using var cache = new FwDataProjectLoader().LoadScratchCache(project);
        return ReservedFields(cache);
    }

    private static void AddReservedField(
        string project, string ownerClass, CellarPropertyType type, int writingSystemSelector = WritingSystemServices.kwsAnal) =>
        AddUnrelatedField(project, ownerClass, "MotifHumanJudgment", type, writingSystemSelector);

    private static void AddUnrelatedField(
        string project, string ownerClass, string name, CellarPropertyType type,
        int writingSystemSelector = WritingSystemServices.kwsAnal)
    {
        using var cache = new FwDataProjectLoader().LoadCache(project);
        var metadata = (IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            metadata.AddCustomField(ownerClass, name, type, 0, "field label",
                writingSystemSelector, Guid.Empty));
        new FwDataProjectLoader().Save(cache);
    }

    private static void RenameReservedField(string project)
    {
        using var cache = new FwDataProjectLoader().LoadCache(project);
        var metadata = (IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor;
        var field = Assert.Single(ReservedFields(cache));
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            metadata.UpdateCustomField(field, string.Empty, WritingSystemServices.kwsAnal, "A renamed label"));
        new FwDataProjectLoader().Save(cache);
    }

    private static void ChangeFirstAnalysisWritingSystem(string project)
    {
        using var cache = new FwDataProjectLoader().LoadCache(project);
        var systems = cache.ServiceLocator.WritingSystems;
        cache.ServiceLocator.WritingSystemManager.GetOrSet("de", out var changed);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var original = systems.AnalysisWritingSystems.Single();
            systems.AnalysisWritingSystems.Add(changed);
            systems.CurrentAnalysisWritingSystems.Add(changed);
            systems.CurrentAnalysisWritingSystems.Remove(original);
        });
        new FwDataProjectLoader().Save(cache);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed class FailingSaveLoader(string project, bool corruptAfterSave) : FwDataProjectLoader
    {
        public override void Save(LcmCache cache)
        {
            if (!corruptAfterSave)
                throw new IOException("Injected failure before the XML save.");

            base.Save(cache);
            File.WriteAllText(project, "<languageproject><incomplete>");
            throw new IOException("Injected failure after the XML save.");
        }
    }

    private sealed class ReadOnlyRecoveryDirectorySaveFailure(string recoveryDirectory) : FwDataProjectLoader
    {
        public override void Save(LcmCache cache)
        {
            // Keeps the recovery copy in place because its deletion now lacks write permission.
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(recoveryDirectory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            throw new IOException("Injected failure before the XML save.");
        }
    }

    private sealed class ReadOnlyRecoveryDirectoryAfterSaveLoader(string recoveryDirectory) : FwDataProjectLoader
    {
        public override void Save(LcmCache cache)
        {
            base.Save(cache);
            // Keeps the recovery copy in place after a successful save because its deletion lacks write permission.
            File.SetUnixFileMode(recoveryDirectory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        }
    }

    private sealed class CountingSaveLoader : FwDataProjectLoader
    {
        public int SaveCount { get; private set; }

        public override void Save(LcmCache cache)
        {
            SaveCount++;
            base.Save(cache);
        }
    }

    private sealed class CountingLoadLoader : FwDataProjectLoader
    {
        public int LoadCount { get; private set; }

        public override LcmCache LoadCache(string fwDataFilePath, string? templatesFolder = null)
        {
            LoadCount++;
            return base.LoadCache(fwDataFilePath, templatesFolder);
        }
    }

    private sealed class ClosedUnitSaveLoader : FwDataProjectLoader
    {
        public int SaveCount { get; private set; }
        public bool UnitWasOpenAtSave { get; private set; }
        public string UnitStateAtSave { get; private set; } = string.Empty;

        public override void Save(LcmCache cache)
        {
            var activeTask = cache.ActionHandlerAccessor.GetType().GetProperty(
                "IsUndoTaskActive", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var nonUndoableStack = cache.ActionHandlerAccessor.GetType().GetProperty(
                "NonUndoableStack", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var activeUndoTask = activeTask is not null &&
                Convert.ToBoolean(activeTask.GetValue(cache.ActionHandlerAccessor));
            var activeUnits = nonUndoableStack?.GetValue(cache.ActionHandlerAccessor);
            var count = activeUnits?.GetType().GetProperty("Count")?.GetValue(activeUnits);
            var activeUnitCount = Convert.ToInt32(count);
            UnitWasOpenAtSave = activeUndoTask || activeUnitCount != 0;
            UnitStateAtSave = $"Undo task active: {activeUndoTask}; non-undoable stack count: {activeUnitCount}; " +
                $"handler type: {cache.ActionHandlerAccessor.GetType().FullName}.";
            SaveCount++;
            base.Save(cache);
        }
    }

    private sealed class ExitAfterSaveLoader : FwDataProjectLoader
    {
        public override void Save(LcmCache cache)
        {
            base.Save(cache);
            Environment.Exit(86);
        }
    }

    private sealed class NoAnalysisWritingSystemLoader : FwDataProjectLoader
    {
        public override LcmCache LoadCache(string fwDataFilePath, string? templatesFolder = null)
        {
            var cache = base.LoadCache(fwDataFilePath, templatesFolder);
            var systems = cache.ServiceLocator.WritingSystems;
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                foreach (var writingSystem in systems.AnalysisWritingSystems.ToArray())
                    systems.AnalysisWritingSystems.Remove(writingSystem);
                foreach (var writingSystem in systems.CurrentAnalysisWritingSystems.ToArray())
                    systems.CurrentAnalysisWritingSystems.Remove(writingSystem);
            });
            return cache;
        }
    }
}

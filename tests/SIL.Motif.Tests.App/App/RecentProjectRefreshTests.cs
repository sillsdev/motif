using System.Collections.Concurrent;
using Avalonia.Input;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class RecentProjectRefreshTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OverlappingOpensKeepTheCurrentIdentityWithoutReadingOnTheDispatcher(bool firstFinishesFirst)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var files = new ProjectFiles();
            using var lookup = new ControlledLookup(Environment.CurrentManagedThreadId,
                (files.FirstPath, false), (files.SecondPath, false));
            var fake = NewClient();
            fake.KnownProjectsListIs([
                files.CopyOf(files.FirstIdentity, "first-copy.fwdata"),
                files.CopyOf(files.SecondIdentity, "second-copy.fwdata"),
            ]);
            var workspace = NewWorkspace(fake, lookup.Read);
            Task? first = null;
            Task? second = null;
            var publications = new ConcurrentQueue<int>();
            var dispatcherThread = Environment.CurrentManagedThreadId;
            workspace.RecentProjects.CollectionChanged += (_, _) => publications.Enqueue(Environment.CurrentManagedThreadId);
            try
            {
                await workspace.Project.LoadKnownProjectsAsync();
                first = workspace.SetProjectAsync(files.FirstPath, captureFreshProject: false);
                await lookup.Started(files.FirstPath);
                second = workspace.SetProjectAsync(files.SecondPath, captureFreshProject: false);
                await lookup.Started(files.SecondPath);
                workspace.Project.KnownProjects.Add(files.CopyOf(Guid.NewGuid(), "additional.fwdata"));

                if (firstFinishesFirst)
                {
                    var before = workspace.RecentProjects.ToArray();
                    lookup.Release(files.FirstPath);
                    await first;
                    Assert.Equal(before, workspace.RecentProjects);
                    Assert.DoesNotContain(lookup.Calls, call => call.OnDispatcher);
                    lookup.Release(files.SecondPath);
                    await second;
                }
                else
                {
                    lookup.Release(files.SecondPath);
                    await second;
                    Assert.DoesNotContain(workspace.RecentProjects, row => row.ProjectIdentity == files.SecondIdentity.ToString("D"));
                    lookup.Release(files.FirstPath);
                    await first;
                }

                Assert.Equal(files.SecondPath, workspace.Context.ProjectPath);
                Assert.Null(workspace.Context.Baseline?.Token);
                Assert.Contains(workspace.RecentProjects, row => row.ProjectIdentity == files.FirstIdentity.ToString("D"));
                Assert.DoesNotContain(workspace.RecentProjects, row => row.ProjectIdentity == files.SecondIdentity.ToString("D"));
                Assert.Empty(lookup.Calls.Where(call => call.OnDispatcher));
                Assert.Single(lookup.Calls, call => call.Path == files.FirstPath);
                Assert.Single(lookup.Calls, call => call.Path == files.SecondPath);
                Assert.All(publications, thread => Assert.Equal(dispatcherThread, thread));
            }
            finally
            {
                lookup.ReleaseAll();
                if (first is not null) await first;
                if (second is not null) await second;
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void ASaveDuringPrewarmDoesNotRetryTheIdentityReadOnTheDispatcher()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var files = new ProjectFiles();
            using var lookup = new ControlledLookup(Environment.CurrentManagedThreadId, (files.FirstPath, true));
            var fake = NewClient();
            fake.KnownProjectsListIs([files.CopyOf(files.FirstIdentity, "copy.fwdata")]);
            var workspace = NewWorkspace(fake, lookup.Read);
            Task? opening = null;
            try
            {
                await workspace.Project.LoadKnownProjectsAsync();
                opening = workspace.SetProjectAsync(files.FirstPath, captureFreshProject: false);
                await lookup.Started(files.FirstPath);
                var before = File.GetLastWriteTimeUtc(files.FirstPath);
                File.WriteAllText(files.FirstPath, ProjectFiles.Xml(files.FirstIdentity, "<saved />"));
                File.SetLastWriteTimeUtc(files.FirstPath, before.AddMinutes(1));
                lookup.Release(files.FirstPath);
                await opening;

                Assert.Empty(workspace.RecentProjects);
                Assert.Empty(lookup.Calls.Where(call => call.OnDispatcher));
                Assert.Single(lookup.Calls);
                workspace.Project.KnownProjects.Add(files.CopyOf(Guid.NewGuid(), "additional.fwdata"));
                Assert.Single(workspace.RecentProjects);
                Assert.Single(lookup.Calls);
            }
            finally
            {
                lookup.ReleaseAll();
                if (opening is not null) await opening;
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("count")]
    [InlineData("state")]
    [InlineData("lastSeen")]
    public void ReloadingTheSamePathsPublishesChangedIdentityCountStateAndLastSeen(string changedField)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var files = new ProjectFiles();
            var fake = NewClient();
            var first = new KnownProjectSummary(files.FirstPath, DateTimeOffset.UtcNow.AddDays(-1))
            {
                ProjectIdentity = files.FirstIdentity.ToString("D"), WordformCount = 91,
                StateText = "Results available",
            };
            fake.KnownProjectsListIs([first]);
            var workspace = NewWorkspace(fake, KnownProjectsQuery.ReadProjectIdentity);
            var overview = workspace.PageModel<OverviewPageModel>();
            var notifications = 0;
            try
            {
                await workspace.Project.LoadKnownProjectsAsync();
                overview.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(OverviewPageModel.ChooseProjectRows)) notifications++;
                };
                var updated = changedField switch
                {
                    "identity" => first with { ProjectIdentity = files.SecondIdentity.ToString("D") },
                    "count" => first with { WordformCount = 104 },
                    "state" => first with { StateText = "Refresh needed" },
                    "lastSeen" => first with { LastSeenUtc = first.LastSeenUtc.AddHours(1) },
                    _ => throw new ArgumentOutOfRangeException(nameof(changedField)),
                };
                fake.KnownProjectsListIs([updated]);
                await workspace.Project.LoadKnownProjectsAsync();

                Assert.Equal(updated, Assert.Single(workspace.Project.KnownProjects));
                var row = Assert.Single(overview.ChooseProjectRows);
                Assert.Equal(updated.ProjectIdentity, row.ProjectIdentity);
                Assert.Equal(updated.WordformCount, row.WordformCount);
                Assert.Equal($"{updated.WordformCount} wordforms", row.WordformCountText);
                Assert.Equal(updated.StateText, row.StateText);
                var recent = Assert.Single(workspace.RecentProjects);
                Assert.Equal(updated.ProjectIdentity, recent.ProjectIdentity);
                Assert.Equal(updated.WordformCount, recent.WordformCount);
                Assert.Equal(updated.StateText, recent.StateText);
                Assert.True(notifications > 0);
            }
            finally { await workspace.DisposeAsync(); }
        }, TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void AnEqualListReloadRetriesAMissingIdentityWithoutStartingAPublicationLoop()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            for (var iteration = 0; iteration < 200; iteration++)
            {
                using var files = new ProjectFiles();
                var fake = NewClient();
                fake.KnownProjectsListIs([files.CopyOf(files.FirstIdentity, "copy.fwdata")]);
                var dispatcherThread = Environment.CurrentManagedThreadId;
                var threads = new ConcurrentQueue<int>();
                var lookups = 0;
                var workspace = NewWorkspace(fake, path =>
                {
                    threads.Enqueue(Environment.CurrentManagedThreadId);
                    return Interlocked.Increment(ref lookups) == 1 ? null : KnownProjectsQuery.ReadProjectIdentity(path);
                });
                try
                {
                    await workspace.Project.LoadKnownProjectsAsync();
                    await workspace.SetProjectAsync(files.FirstPath, captureFreshProject: false);
                    Assert.Single(workspace.RecentProjects);
                    Assert.Equal(1, lookups);

                    await workspace.RefreshKnownProjectsAsync();

                    Assert.Empty(workspace.RecentProjects);
                    Assert.Equal(2, lookups);
                    Assert.DoesNotContain(dispatcherThread, threads);
                }
                finally { await workspace.DisposeAsync(); }
            }
        }, TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void DisposalWaitsForABlockedIdentityReadAndRejectsItsPublication()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var files = new ProjectFiles();
            using var lookup = new ControlledLookup(Environment.CurrentManagedThreadId, (files.FirstPath, false));
            var fake = NewClient();
            fake.KnownProjectsListIs([files.CopyOf(files.FirstIdentity, "copy.fwdata")]);
            var workspace = NewWorkspace(fake, lookup.Read);
            Task? opening = null;
            Task? disposing = null;
            var publications = 0;
            try
            {
                await workspace.Project.LoadKnownProjectsAsync();
                opening = workspace.SetProjectAsync(files.FirstPath, captureFreshProject: false);
                await lookup.Started(files.FirstPath);
                workspace.RecentProjects.CollectionChanged += (_, _) => publications++;
                disposing = workspace.DisposeAsync().AsTask();
                Assert.False(disposing.IsCompleted);
                lookup.Release(files.FirstPath);
                await Task.WhenAll(opening, disposing);

                Assert.Equal(0, publications);
                workspace.Project.KnownProjects.Add(files.CopyOf(Guid.NewGuid(), "additional.fwdata"));
                Assert.Equal(0, publications);
                Assert.Single(lookup.Calls);
            }
            finally
            {
                lookup.ReleaseAll();
                if (opening is not null) await opening;
                if (disposing is not null) await disposing;
                else await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    private static FakeCommandClient NewClient()
    {
        var client = new FakeCommandClient();
        client.CurrentBaselineCompletesWith(new CurrentBaselineResponse(null, default, false));
        return client;
    }

    private static WorkspaceShellViewModel NewWorkspace(FakeCommandClient client, Func<string, string?> lookup)
    {
        var selection = new SelectionViewModel(client);
        return new WorkspaceShellViewModel(new ProjectViewModel(client, new ProjectPicker()),
            new BaselineViewModel(client), selection, new AssessViewModel(client, selection),
            new FolderPicker(), new DragSource(), client) { ReadSavedProjectIdentity = lookup };
    }

    private sealed class ControlledLookup : IDisposable
    {
        private readonly int _dispatcherThread;
        private readonly Dictionary<string, Gate> _gates;
        public ConcurrentQueue<(string Path, bool OnDispatcher)> Calls { get; } = new();

        public ControlledLookup(int dispatcherThread, params (string Path, bool ReadBeforeRelease)[] paths)
        {
            _dispatcherThread = dispatcherThread;
            _gates = paths.ToDictionary(path => path.Path, path => new Gate(path.ReadBeforeRelease));
        }

        public string? Read(string path)
        {
            var onDispatcher = Environment.CurrentManagedThreadId == _dispatcherThread;
            Calls.Enqueue((path, onDispatcher));
            if (onDispatcher) return KnownProjectsQuery.ReadProjectIdentity(path);
            var gate = _gates[path];
            var identity = gate.ReadBeforeRelease ? KnownProjectsQuery.ReadProjectIdentity(path) : null;
            gate.Started.TrySetResult();
            gate.Released.Wait();
            return gate.ReadBeforeRelease ? identity : KnownProjectsQuery.ReadProjectIdentity(path);
        }

        public Task Started(string path) => _gates[path].Started.Task;
        public void Release(string path) => _gates[path].Released.Set();
        public void ReleaseAll()
        {
            foreach (var gate in _gates.Values) gate.Released.Set();
        }

        public void Dispose()
        {
            ReleaseAll();
            foreach (var gate in _gates.Values) gate.Released.Dispose();
        }

        private sealed class Gate(bool readBeforeRelease)
        {
            public bool ReadBeforeRelease { get; } = readBeforeRelease;
            public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public ManualResetEventSlim Released { get; } = new(false);
        }
    }

    private sealed class ProjectFiles : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "MotifRecentRefresh", Guid.NewGuid().ToString("N"));
        public Guid FirstIdentity { get; } = Guid.NewGuid();
        public Guid SecondIdentity { get; } = Guid.NewGuid();
        public string FirstPath => Path.Combine(_root, "first.fwdata");
        public string SecondPath => Path.Combine(_root, "second.fwdata");

        public ProjectFiles()
        {
            Directory.CreateDirectory(_root);
            File.WriteAllText(FirstPath, Xml(FirstIdentity));
            File.WriteAllText(SecondPath, Xml(SecondIdentity));
        }

        public KnownProjectSummary CopyOf(Guid identity, string name) =>
            new(Path.Combine(_root, name), DateTimeOffset.UtcNow) { ProjectIdentity = identity.ToString("D") };

        public static string Xml(Guid identity, string content = "") =>
            $"<languageproject><rt class=\"LangProject\" guid=\"{identity:D}\" />{content}</languageproject>";

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }

    private sealed class ProjectPicker : IProjectPicker
    {
        public Task<string?> PickProjectFileAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

    private sealed class FolderPicker : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

    private sealed class DragSource : IFileDragSource
    {
        public Task<DragDropEffects> StartDragAsync(PointerPressedEventArgs trigger,
            IReadOnlyList<string> filePaths, DragDropEffects allowedEffects) => Task.FromResult(DragDropEffects.None);
    }
}

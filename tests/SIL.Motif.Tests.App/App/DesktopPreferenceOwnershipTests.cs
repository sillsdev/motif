using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Threading;
using SIL.Motif.App;
using SIL.Motif.App.Composition;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class DesktopPreferenceOwnershipTests(AvaloniaHeadlessFixture avalonia)
{
    [Fact]
    public void ClosingAnOwnedSessionReleasesItsPreferencesSettingsAndWorkspace()
    {
        using var folder = new TemporaryFolder();
        WeakReference[]? references = null;
        try
        {
            AvaloniaHeadlessFixture.RunUntilComplete(async () =>
            {
                references = await CloseOwnedSessionAsync(folder.Path);
            }, TimeSpan.FromSeconds(30));
            avalonia.Invoke(() => Dispatcher.UIThread.RunJobs());

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Assert.NotNull(references);
            Assert.All(references, reference => Assert.False(reference.IsAlive));
        }
        finally
        {
            if (references?[0].Target is FileUserPreferencesStore leaked) leaked.Dispose();
            avalonia.Invoke(() => WindowZoomPolicy.Apply(Application.Current!, ZoomPolicy.DefaultPercent));
        }
    }

    [Fact]
    public void ReusingInstallationOptionsCreatesFreshPreferencesForEachSession()
    {
        using var folder = new TemporaryFolder();
        var stores = new List<FileUserPreferencesStore>();
        var path = System.IO.Path.Combine(folder.Path, "user-preferences.json");
        var options = Options(folder.Path) with
        {
            OwnedUserPreferencesFactory = () =>
            {
                var store = new FileUserPreferencesStore(path, TimeSpan.FromMinutes(5));
                stores.Add(store);
                return store;
            },
        };
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            try
            {
                await CloseSessionAsync(options, 125);
                Assert.Equal(125, new FileUserPreferencesStore(path).Current.ZoomPercent);
                await CloseSessionAsync(options, 150);
                Assert.Equal(150, new FileUserPreferencesStore(path).Current.ZoomPercent);
                Assert.Equal(2, stores.Count);
                Assert.NotSame(stores[0], stores[1]);
            }
            finally
            {
                foreach (var store in stores) store.Dispose();
                WindowZoomPolicy.Apply(Application.Current!, ZoomPolicy.DefaultPercent);
            }
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void CallerOwnedPreferencesRemainUsableAcrossSessionCloses()
    {
        using var folder = new TemporaryFolder();
        var path = System.IO.Path.Combine(folder.Path, "user-preferences.json");
        using var shared = new FileUserPreferencesStore(path, TimeSpan.FromMinutes(5));
        var options = Options(folder.Path) with
        {
            UserPreferencesStore = shared,
            OwnedUserPreferencesFactory = () => throw new InvalidOperationException("A supplied store takes precedence."),
        };
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            try
            {
                await CloseSessionAsync(options, 125);
                Assert.Equal(125, new FileUserPreferencesStore(path).Current.ZoomPercent);
                await CloseSessionAsync(options, 150);
                shared.Update(preferences => preferences with { Theme = UserTheme.Dark });
                shared.Flush();
                var saved = new FileUserPreferencesStore(path).Current;
                Assert.Equal(150, saved.ZoomPercent);
                Assert.Equal(UserTheme.Dark, saved.Theme);
            }
            finally
            {
                WindowZoomPolicy.Apply(Application.Current!, ZoomPolicy.DefaultPercent);
            }
        }, TimeSpan.FromSeconds(30));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference[]> CloseOwnedSessionAsync(string root)
    {
        var path = System.IO.Path.Combine(root, "user-preferences.json");
        var store = new FileUserPreferencesStore(path, TimeSpan.FromMinutes(5));
        try
        {
            var options = Options(root) with { OwnedUserPreferencesFactory = () => store };
            var composition = MotifAppComposition.Create(options);
            var settings = composition.Workspace.PageModel<SettingsPageModel>().Settings!;
            var references = new[]
            {
                new WeakReference(store), new WeakReference(settings), new WeakReference(composition.Workspace),
            };
            var session = new MotifDesktopSession(composition, Task.CompletedTask);
            try
            {
                session.Window.Show();
                settings.ZoomPercent = 150;
            }
            finally
            {
                await session.CloseAsync();
            }
            Assert.Equal(150, new FileUserPreferencesStore(path).Current.ZoomPercent);
            return references;
        }
        catch
        {
            store.Dispose();
            throw;
        }
    }

    private static async Task CloseSessionAsync(MotifAppOptions options, int zoom)
    {
        var composition = MotifAppComposition.Create(options);
        var session = new MotifDesktopSession(composition, Task.CompletedTask);
        try
        {
            session.Window.Show();
            composition.Workspace.PageModel<SettingsPageModel>().Settings!.ZoomPercent = zoom;
        }
        finally
        {
            await session.CloseAsync();
            Assert.Same(session.Closed, session.CloseAsync());
        }
    }

    private static MotifAppOptions Options(string root) => new(root, null,
        new NoRunnerLauncher(new JobRunnerLaunchOptions(root, null)), TimeProvider.System, RememberBounds: true);

    private sealed class TemporaryFolder : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("motif-preference-ownership-").FullName;

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}

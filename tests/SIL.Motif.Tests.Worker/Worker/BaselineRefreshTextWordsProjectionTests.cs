using System.Text.Json;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Worker;

/// <summary>
/// Pins that the worker's Baseline refresh stores the Text words projection of the Baseline it records, in
/// place of the previous one, and that a stored projection is served only for the Baseline it was built from.
/// </summary>
[Collection(LcmCacheTestCollection.Name)]
public sealed class BaselineRefreshTextWordsProjectionTests : IDisposable
{
    private readonly PristineProjectFixture _pristine;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.BaselineRefreshTextWordsProjectionTests",
        Guid.NewGuid().ToString("N"));

    public BaselineRefreshTextWordsProjectionTests(PristineProjectFixture pristine)
    {
        _pristine = pristine;
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task RefreshReplacesTheTextWordsProjectionWithTheCurrentBaselines()
    {
        using var cache = _pristine.NewScratch();
        var firstText = SeededProject.SeedText(cache, _pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        var projectPath = cache.ProjectId.Path;
        var project = new ProjectLocator(projectPath, Path.GetFileNameWithoutExtension(projectPath));
        using var database = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, new Version(1, 0))
            .OpenOwned(project);
        var repository = new BaselineRepository(database);
        var refresh = new BaselineRefresh(repository, Path.Combine(_root, "managed"));
        var projectKey = ProjectWorkspaceKey.Compute(project);

        var firstToken = await refresh.RefreshAsync(cache, project, CancellationToken.None);
        var first = repository.GetCurrentTextWords(projectKey)!;

        Assert.Equal(firstToken.BundleDigest, first.Baseline.Token.BundleDigest);
        Assert.Equal([firstText.TextId], first.Projection.Texts.Select(text => text.TextId));

        var secondText = SeededProject.SeedText(cache, _pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        var secondToken = await refresh.RefreshAsync(cache, project, CancellationToken.None);
        var second = repository.GetCurrentTextWords(projectKey)!;

        Assert.NotEqual(firstToken.BundleDigest, secondToken.BundleDigest);
        Assert.Equal(secondToken.BundleDigest, second.Baseline.Token.BundleDigest);
        Assert.Equal(new HashSet<Guid> { firstText.TextId, secondText.TextId },
            second.Projection.Texts.Select(text => text.TextId).ToHashSet());
        Assert.Equal(Serialize(TextWordsProjectionBuilder.Build(cache)), Serialize(second.Projection));
        Assert.Equal(1, CountProjectionRows(database, projectKey));
    }

    [Fact]
    public async Task AProjectionBuiltFromAnotherBaselineIsRefusedRatherThanServed()
    {
        using var cache = _pristine.NewScratch();
        SeededProject.SeedText(cache, _pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        var projectPath = cache.ProjectId.Path;
        var project = new ProjectLocator(projectPath, Path.GetFileNameWithoutExtension(projectPath));
        using var database = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, new Version(1, 0))
            .OpenOwned(project);
        var repository = new BaselineRepository(database);
        await new BaselineRefresh(repository, Path.Combine(_root, "managed"))
            .RefreshAsync(cache, project, CancellationToken.None);
        var projectKey = ProjectWorkspaceKey.Compute(project);
        using (var connection = database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE BaselineTextWords SET BundleDigest = 'sha256:other' WHERE ProjectKey = $project;";
            command.Parameters.AddWithValue("$project", projectKey);
            Assert.Equal(1, command.ExecuteNonQuery());
        }

        Assert.Throws<InvalidDataException>(() => repository.GetCurrentTextWords(projectKey));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string Serialize(object value) => JsonSerializer.Serialize(value, MotifJson.CreateOptions());

    private static long CountProjectionRows(MotifDatabase database, string projectKey)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM BaselineTextWords WHERE ProjectKey = $project;";
        command.Parameters.AddWithValue("$project", projectKey);
        return (long)command.ExecuteScalar()!;
    }
}

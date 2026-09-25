using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// Pins that <see cref="TextWordsQuery"/> answers from the words stored with the current Baseline: once the
/// managed Baseline copy is moved away, the query still returns the response it gave before, whether that
/// Baseline came from interactive capture or from the worker's refresh.
/// </summary>
[Collection(LcmCacheTestCollection.Name)]
public sealed class TextWordsReadNoProjectFileTests : IDisposable
{
    private readonly PristineProjectFixture _pristine;
    private readonly string _managedRootsParent =
        Path.Combine(Path.GetTempPath(), "SIL.Motif.TextWordsReadNoProjectFileTests", Guid.NewGuid().ToString("N"));

    public TextWordsReadNoProjectFileTests(PristineProjectFixture pristine)
    {
        _pristine = pristine;
        Directory.CreateDirectory(_managedRootsParent);
    }

    public void Dispose()
    {
        try { Directory.Delete(_managedRootsParent, recursive: true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void CapturedTextWordsAreReadWithoutTheManagedBaselineProjectFile()
    {
        using var cache = _pristine.NewScratch();
        var firstText = SeededProject.SeedText(cache, _pristine.Seed);
        var secondText = SeededProject.SeedText(cache, _pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        var fwDataPath = cache.ProjectId.Path;
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), NewManagedRoot());
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        var request = new TextWordsRequest(fwDataPath, [secondText.TextId, firstText.TextId]);

        var before = TextWordsQuery.Query(request);

        Assert.True(before.Succeeded, before.Refusal?.Message);
        var merged = Assert.Single(before.Value!.Words, word => word.Form == SeededProject.AnalysedWordForm);
        Assert.Equal(secondText.AnalysedWordformId, Guid.Parse(merged.WordformGuid!));
        Assert.Equal([secondText.TextId, firstText.TextId], merged.Occurrences.Select(occurrence => occurrence.TextId));
        Assert.Equal([secondText.TextId, firstText.TextId], before.Value.Texts.Select(text => text.TextId));
        AssertSameResponseWithoutFile(captured.Value!.FwDataPath, request, before.Value);
    }

    [Fact]
    public void RefreshedTextWordsReplaceCapturedOnesAndAreReadWithoutTheManagedBaselineProjectFile()
    {
        using var cache = _pristine.NewScratch();
        var firstText = SeededProject.SeedText(cache, _pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        var fwDataPath = cache.ProjectId.Path;
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), NewManagedRoot());
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        var secondText = SeededProject.SeedText(cache, _pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        var request = new TextWordsRequest(fwDataPath, [firstText.TextId, secondText.TextId]);
        Assert.Equal([firstText.TextId],
            TextWordsQuery.Query(request).Value!.Texts.Select(text => text.TextId));

        var refreshRoot = NewManagedRoot();
        var refreshed = ProjectStoreCommand.Run(fwDataPath, MotifProductVersion.CurrentText, (database, project) =>
        {
            var token = new BaselineRefresh(new BaselineRepository(database), refreshRoot)
                .RefreshAsync(cache, project, CancellationToken.None).GetAwaiter().GetResult();
            var current = new BaselineRepository(database).GetCurrent(ProjectWorkspaceKey.Compute(project))!;
            Assert.Equal(token.BundleDigest, current.Token.BundleDigest);
            return CommandOutcome<BaselineRecord>.Success(current);
        });
        Assert.True(refreshed.Succeeded, refreshed.Refusal?.Message);
        Assert.NotEqual(captured.Value!.FwDataPath, refreshed.Value!.FwDataPath);

        var before = TextWordsQuery.Query(request);

        Assert.True(before.Succeeded, before.Refusal?.Message);
        Assert.Equal([firstText.TextId, secondText.TextId], before.Value!.Texts.Select(text => text.TextId));
        AssertSameResponseWithoutFile(refreshed.Value.FwDataPath, request, before.Value);
    }

    private static void AssertSameResponseWithoutFile(
        string baselineFwDataPath, TextWordsRequest request, TextWordsResponse expected)
    {
        var moved = baselineFwDataPath + ".unavailable";
        File.Move(baselineFwDataPath, moved);
        try
        {
            var after = TextWordsQuery.Query(request);

            Assert.True(after.Succeeded, after.Refusal?.Message);
            Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(after.Value));
        }
        finally
        {
            File.Move(moved, baselineFwDataPath);
        }
    }

    private string NewManagedRoot()
    {
        var root = Path.Combine(_managedRootsParent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}

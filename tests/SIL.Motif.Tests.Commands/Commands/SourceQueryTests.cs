using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Scripture;
using SIL.Motif.Tests.Scripture;
using SIL.Motif.Tests.Scripture.Fixtures;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Host;
using SIL.Motif.Worker.Projects;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
public sealed class SourceQueryTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.Tests.SourceQuery", Guid.NewGuid().ToString("N"));

    private static readonly SeededSourceList.Item[] Sources =
    [
        new("WEB", "World English Bible", "GEN-ROM; 1CO 13-15", "Paratext", "English"),
        new("CEB", "Cebuano", "NT", "Paratext", "English"),
        new("OLD", "Old", "GEN", "Paratext", "English", "Removed"),
        new("BAD", "Bad", "1COR 3", "Paratext", "English"),
    ];

    [Fact]
    public void ListHidesRemovedSourcesUnlessAskedAndReportsABadItemWithoutThrowing()
    {
        var path = ProjectWithSources(captureBaseline: true);

        var listed = SourceQuery.List(new SourceListRequest(path));
        var all = SourceQuery.List(new SourceListRequest(path, All: true));

        Assert.True(listed.Succeeded, listed.Refusal?.Message);
        Assert.NotNull(listed.Value!.Baseline);
        Assert.Equal(["WEB", "CEB", "BAD"], listed.Value.Sources.Select(source => source.Id));
        Assert.Equal(["WEB", "CEB", "OLD", "BAD"], all.Value!.Sources.Select(source => source.Id));
        var web = listed.Value.Sources[0];
        Assert.Equal("Paratext", web.Kind);
        Assert.Equal("Active", web.Status);
        Assert.Equal("English", web.Versification);
        Assert.Equal(new SourceContextRange("GEN", "all"), web.ContextRanges[0]);
        Assert.Contains(new SourceContextRange("ROM", "all"), web.ContextRanges);
        Assert.Contains(web.ContextRanges, range => range is { Book: "1CO", Chapters: "13-15" });
        Assert.Null(web.Error);
        Assert.Equal("Removed", all.Value.Sources[2].Status);
        var bad = listed.Value.Sources[2];
        Assert.Contains("1COR", bad.Error);
        Assert.Empty(bad.ContextRanges);
    }

    [Fact]
    public void ShowReadsOneSourceIgnoringCaseWithItsChapterCountAndFolder()
    {
        var path = ProjectWithSources(captureBaseline: true);
        using var tiny = TinyProject.Create();
        ScriptureLocalStore.ForInstallation().SetSourcePath(KeyOf(path), "WEB", tiny.Folder);

        var shown = SourceQuery.Show(new SourceShowRequest(path, "web"));

        Assert.True(shown.Succeeded, shown.Refusal?.Message);
        Assert.Equal("WEB", shown.Value!.Source.Id);
        Assert.Equal(tiny.Folder, shown.Value.Source.Folder);
        Assert.True(shown.Value.ChapterCount > 3);
        Assert.Equal("Removed", SourceQuery.Show(new SourceShowRequest(path, "OLD")).Value!.Source.Status);
    }

    [Fact]
    public void ShowRefusesAnUnknownOrEmptyId()
    {
        var path = ProjectWithSources(captureBaseline: true);

        Assert.Equal(RefusalCodes.SourceNotFound, SourceQuery.Show(new SourceShowRequest(path, "NOPE")).Refusal!.Code);
        Assert.Equal(RefusalCodes.SourceIdInvalid, SourceQuery.Show(new SourceShowRequest(path, " ")).Refusal!.Code);
    }

    [Fact]
    public void WhereClassifiesAgainstTheContextAndTheCurrentChapterWithoutAFolder()
    {
        var path = ProjectWithSources(captureBaseline: true);
        ScriptureLocalStore.ForInstallation().SetCurrent(KeyOf(path), "CEB", new ChapterRef("MAL", 2));

        var web = SourceQuery.Where(new SourceWhereRequest(path, "mat 5", "WEB"));
        var every = SourceQuery.Where(new SourceWhereRequest(path, "MAL 2"));

        Assert.Equal("MAT 5", web.Value!.Ref);
        Assert.Equal("InContext", Assert.Single(web.Value.Places).Place);
        // Active Sources only: WEB, CEB and BAD; BAD has no Context to place against.
        Assert.Equal(["WEB", "CEB", "BAD"], every.Value!.Places.Select(place => place.Source));
        Assert.Equal("InContext", every.Value.Places[0].Place);
        Assert.Equal("Current", every.Value.Places[1].Place);
        Assert.Null(every.Value.Places[2].Place);
    }

    [Fact]
    public void WhereSeparatesCurrentFromLaterAndReportsAChapterTheTextLacks()
    {
        var path = ProjectWithSources(captureBaseline: true);
        using var tiny = TinyProject.Create();
        var store = ScriptureLocalStore.ForInstallation();
        store.SetSourcePath(KeyOf(path), "CEB", tiny.Folder);
        store.SetCurrent(KeyOf(path), "CEB", new ChapterRef("GEN", 2));

        string PlaceOf(string reference) => Assert.Single(
            SourceQuery.Where(new SourceWhereRequest(path, reference, "CEB")).Value!.Places).Place ?? "none";

        Assert.Equal("InContext", PlaceOf("MAT 1"));
        Assert.Equal("Current", PlaceOf("GEN 2"));
        Assert.Equal("Later", PlaceOf("GEN 3"));
        var missing = Assert.Single(SourceQuery.Where(new SourceWhereRequest(path, "MAT 99", "CEB")).Value!.Places);
        Assert.Null(missing.Place);
        Assert.Contains("no text", missing.Error);
    }

    [Theory]
    [InlineData("MAT")]
    [InlineData("MAT 0")]
    [InlineData("XYZ 5")]
    [InlineData("MAT five")]
    public void WhereRefusesAChapterThatIsNotOne(string reference)
    {
        var path = ProjectWithSources(captureBaseline: true);

        Assert.Equal(RefusalCodes.SourceRefInvalid, SourceQuery.Where(new SourceWhereRequest(path, reference)).Refusal!.Code);
    }

    [Fact]
    public void WhereRefusesAnUnknownSource()
    {
        var path = ProjectWithSources(captureBaseline: true);

        Assert.Equal(RefusalCodes.SourceNotFound, SourceQuery.Where(new SourceWhereRequest(path, "MAT 1", "NOPE")).Refusal!.Code);
    }

    [Fact]
    public void CurrentGetIsEmptyUntilOneIsSetThenPlacesEachAgainstItsContext()
    {
        var path = ProjectWithSources(captureBaseline: true);
        var empty = SourceQuery.CurrentGet(new CurrentGetRequest(path));
        Assert.True(empty.Succeeded, empty.Refusal?.Message);
        Assert.Empty(empty.Value!.Current);

        var store = ScriptureLocalStore.ForInstallation();
        store.SetCurrent(KeyOf(path), "WEB", new ChapterRef("MAT", 5));
        store.SetCurrent(KeyOf(path), "CEB", new ChapterRef("JOB", 1));

        var read = SourceQuery.CurrentGet(new CurrentGetRequest(path)).Value!;
        Assert.Equal([("CEB", "JOB 1", "Current"), ("WEB", "MAT 5", "InContext")],
            read.Current.Select(row => (row.Source, row.Chapter, row.Place!)));
        Assert.Equal("WEB", Assert.Single(SourceQuery.CurrentGet(new CurrentGetRequest(path, "web")).Value!.Current).Source);
        Assert.Equal(RefusalCodes.SourceNotFound, SourceQuery.CurrentGet(new CurrentGetRequest(path, "NOPE")).Refusal!.Code);
    }

    [Fact]
    public void AProjectWithNoSourceListHasNoSources()
    {
        var path = ProjectWith(_ => { }, captureBaseline: true);

        Assert.Empty(SourceQuery.List(new SourceListRequest(path)).Value!.Sources);
    }

    [Fact]
    public void TwoListsWithTheSameNameAreRefusedAsAmbiguous()
    {
        var path = ProjectWith(cache =>
        {
            SeededSourceList.AddList(cache, Sources[0]);
            SeededSourceList.AddList(cache, Sources[1]);
        }, captureBaseline: true);

        Assert.Equal(RefusalCodes.SourceListAmbiguous, SourceQuery.List(new SourceListRequest(path)).Refusal!.Code);
        Assert.Equal(SourceListAmbiguousException.Code, RefusalCodes.SourceListAmbiguous);
    }

    [Fact]
    public void WithoutABaselineEveryReadIsRefusedLikeOtherBaselineReads()
    {
        var path = ProjectWithSources(captureBaseline: false);

        Assert.Equal(RefusalCodes.BaselineMissing, SourceQuery.List(new SourceListRequest(path)).Refusal!.Code);
        Assert.Equal(RefusalCodes.BaselineMissing, SourceQuery.CurrentGet(new CurrentGetRequest(path)).Refusal!.Code);
    }

    [Fact]
    public void ACaptureAfterTheListChangedIsWhatTheReadSees()
    {
        var path = ProjectWithSources(captureBaseline: true);
        var first = SourceQuery.List(new SourceListRequest(path)).Value!;
        var stale = first.Baseline;

        // A read that names a Baseline other than the current one is refused.
        var other = SourceQuery.List(new SourceListRequest(path, ExpectedBaseline: new BaselineToken(stale!.ProjectIdentity, stale.SemanticSnapshotDigest, stale.ProjectionVersion, stale.CapturedUtc, "sha256:" + new string('0', 64))));

        Assert.Equal(RefusalCodes.GdrBaselineChanged, other.Refusal!.Code);
    }

    [Fact]
    public void SetFolderAndClearFolderChangeOnlyTheLocalFileAndNeedAListedSource()
    {
        var path = ProjectWithSources(captureBaseline: true);
        using var tiny = TinyProject.Create();

        var set = SourceLocalCommand.SetFolder(new SourceSetFolderRequest(path, "web", tiny.Folder));

        Assert.True(set.Succeeded, set.Refusal?.Message);
        Assert.Equal(("WEB", Path.GetFullPath(tiny.Folder)), (set.Value!.Source, set.Value.Folder));
        Assert.Equal(Path.GetFullPath(tiny.Folder), ScriptureLocalStore.ForInstallation().Read(KeyOf(path)).SourcePaths["WEB"]);
        Assert.Equal(Path.GetFullPath(tiny.Folder), SourceQuery.Show(new SourceShowRequest(path, "WEB")).Value!.Source.Folder);

        Assert.True(SourceLocalCommand.ClearFolder(new SourceClearFolderRequest(path, "WEB")).Succeeded);
        Assert.Empty(ScriptureLocalStore.ForInstallation().Read(KeyOf(path)).SourcePaths);
        Assert.True(SourceLocalCommand.ClearFolder(new SourceClearFolderRequest(path, "WEB")).Succeeded);

        Assert.Equal(RefusalCodes.SourceNotFound, SourceLocalCommand.SetFolder(new SourceSetFolderRequest(path, "NOPE", tiny.Folder)).Refusal!.Code);
        Assert.Equal(RefusalCodes.SourceNotFound, SourceLocalCommand.ClearFolder(new SourceClearFolderRequest(path, "NOPE")).Refusal!.Code);
        Assert.Equal(RefusalCodes.SourceIdInvalid, SourceLocalCommand.SetFolder(new SourceSetFolderRequest(path, " ", tiny.Folder)).Refusal!.Code);
        Assert.Empty(ScriptureLocalStore.ForInstallation().Read(KeyOf(path)).SourcePaths);
    }

    [Fact]
    public void SetFolderRefusesAFolderThatDoesNotExist()
    {
        var path = ProjectWithSources(captureBaseline: true);

        var refused = SourceLocalCommand.SetFolder(new SourceSetFolderRequest(path, "WEB", Path.Combine(_root, "absent")));

        Assert.Equal(RefusalCodes.SourceFolderMissing, refused.Refusal!.Code);
        Assert.Contains("absent", refused.Refusal.Facts["path"]);
        Assert.Empty(ScriptureLocalStore.ForInstallation().Read(KeyOf(path)).SourcePaths);
    }

    [Fact]
    public void SetCurrentChecksTheChapterAgainstTheFolderWhenOneIsRecordedAndClearForgetsIt()
    {
        var path = ProjectWithSources(captureBaseline: true);
        using var tiny = TinyProject.Create();
        var local = ScriptureLocalStore.ForInstallation();

        // No folder: taken as given, book normalized.
        var free = SourceLocalCommand.SetCurrent(new CurrentSetRequest(path, "CEB", "mal 2"));
        Assert.True(free.Succeeded, free.Refusal?.Message);
        Assert.Equal("MAL 2", free.Value!.Chapter);
        Assert.Equal(new ChapterRef("MAL", 2), local.Read(KeyOf(path)).Current["CEB"]);

        Assert.True(SourceLocalCommand.SetFolder(new SourceSetFolderRequest(path, "CEB", tiny.Folder)).Succeeded);
        var inside = SourceLocalCommand.SetCurrent(new CurrentSetRequest(path, "CEB", "GEN 2"));
        Assert.True(inside.Succeeded, inside.Refusal?.Message);
        Assert.Equal(new ChapterRef("GEN", 2), local.Read(KeyOf(path)).Current["CEB"]);

        var outside = SourceLocalCommand.SetCurrent(new CurrentSetRequest(path, "CEB", "MAT 99"));
        Assert.Equal(RefusalCodes.SourceChapterNotInSource, outside.Refusal!.Code);
        Assert.Equal(new ChapterRef("GEN", 2), local.Read(KeyOf(path)).Current["CEB"]);

        var cleared = SourceLocalCommand.ClearCurrent(new CurrentClearRequest(path, "ceb"));
        Assert.True(cleared.Succeeded, cleared.Refusal?.Message);
        Assert.Empty(local.Read(KeyOf(path)).Current);
        Assert.True(SourceLocalCommand.ClearCurrent(new CurrentClearRequest(path, "CEB")).Succeeded);
    }

    [Fact]
    public void SetCurrentRefusesABadChapterAnUnknownSourceAndAFolderThatWentAway()
    {
        var path = ProjectWithSources(captureBaseline: true);
        Assert.Equal(RefusalCodes.SourceRefInvalid, SourceLocalCommand.SetCurrent(new CurrentSetRequest(path, "WEB", "MAT")).Refusal!.Code);
        Assert.Equal(RefusalCodes.SourceNotFound, SourceLocalCommand.SetCurrent(new CurrentSetRequest(path, "NOPE", "MAT 1")).Refusal!.Code);
        Assert.Equal(RefusalCodes.SourceNotFound, SourceLocalCommand.ClearCurrent(new CurrentClearRequest(path, "NOPE")).Refusal!.Code);

        var gone = Path.Combine(_root, "gone");
        Directory.CreateDirectory(gone);
        Assert.True(SourceLocalCommand.SetFolder(new SourceSetFolderRequest(path, "WEB", gone)).Succeeded);
        Directory.Delete(gone);
        Assert.Equal(RefusalCodes.SourceFolderMissing, SourceLocalCommand.SetCurrent(new CurrentSetRequest(path, "WEB", "MAT 1")).Refusal!.Code);
        Assert.Empty(ScriptureLocalStore.ForInstallation().Read(KeyOf(path)).Current);
    }

    // The id the local file is keyed by, taken the way the query takes it.
    private static string KeyOf(string fwdataPath) => ProjectStoreCommand.Run<string>(
        fwdataPath, MotifProductVersion.CurrentText,
        (_, project) => CommandOutcome<string>.Success(ProjectWorkspaceKey.Compute(project))).Value!;

    private string ProjectWithSources(bool captureBaseline) =>
        ProjectWith(cache => SeededSourceList.AddList(cache, Sources), captureBaseline);

    private string ProjectWith(Action<SIL.LCModel.LcmCache> seed, bool captureBaseline)
    {
        using var cache = pristine.NewScratch();
        seed(cache);
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        if (captureBaseline)
        {
            var root = Path.Combine(_root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root).Succeeded);
        }
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}

using System.Security.Cryptography;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Host;
using SIL.Motif.Host.Baselines;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Gdr;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using SIL.TextEditing;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
public sealed class GdrQueryTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.Tests.GdrQuery", Guid.NewGuid().ToString("N"));

    [Fact]
    public void ListNamesEachRecordByNumberTitleAndAbstract()
    {
        var path = ProjectWithGdrs(captureBaseline: true);

        var listed = GdrQuery.List(new GdrListRequest(path));

        Assert.True(listed.Succeeded, listed.Refusal?.Message);
        Assert.True(listed.Value!.HasBaseline);
        Assert.NotNull(listed.Value.Baseline);
        Assert.Collection(listed.Value.Records,
            first =>
            {
                Assert.Equal("GDR-1", first.Abbreviation);
                Assert.Equal("title-1", first.Title);
                Assert.Equal("abstract-1", first.Description);
                Assert.Null(first.ParentId);
            },
            nested =>
            {
                Assert.Equal("GDR-1a", nested.Abbreviation);
                Assert.Equal(listed.Value.Records[0].Id, nested.ParentId);
            },
            second => Assert.Equal("GDR-2", second.Abbreviation),
            third =>
            {
                Assert.Equal("", third.Abbreviation);
                Assert.Equal("title-3", third.Title);
                Assert.Equal("", third.Description);
            });
    }

    [Fact]
    public void ShowOpensASubItemByNumberAndById()
    {
        var path = ProjectWithGdrs(captureBaseline: true);
        var nested = GdrQuery.List(new GdrListRequest(path)).Value!.Records[1];

        var byNumber = GdrQuery.Show(new GdrShowRequest(path, "GDR-1a"));
        var byId = GdrQuery.Show(new GdrShowRequest(path, nested.Id));

        Assert.True(byNumber.Succeeded, byNumber.Refusal?.Message);
        Assert.True(byId.Succeeded, byId.Refusal?.Message);
        Assert.Equal(nested.ParentId, byNumber.Value!.Record.ParentId);
        Assert.Equal("z", Assert.Single(Assert.Single(byId.Value!.Discussion).Runs).Text);
    }

    [Fact]
    public void ARecordWithoutAnAbbreviationIsNeverOpenedByAPlaceholder()
    {
        var path = ProjectWithGdrs(captureBaseline: true);
        var untitled = GdrQuery.List(new GdrListRequest(path)).Value!.Records[3];

        Assert.Equal(RefusalCodes.GdrNotFound, GdrQuery.Show(new GdrShowRequest(path, "***")).Refusal!.Code);
        Assert.Equal(RefusalCodes.GdrNotFound, GdrQuery.Show(new GdrShowRequest(path, "-")).Refusal!.Code);
        Assert.Equal("title-3", GdrQuery.Show(new GdrShowRequest(path, untitled.Id)).Value!.Record.Title);
    }

    [Fact]
    public void ShowKeepsALinkTargetAndAnObjectRunsData()
    {
        var path = ProjectWithGdrs(captureBaseline: true);

        var runs = GdrQuery.Show(new GdrShowRequest(path, "GDR-1")).Value!.Discussion[0].Runs;

        Assert.Equal(SeededGdrs.HyperlinkData, runs.Single(run => run.Text == "link").ObjData);
        var embedded = runs.Single(run => run.Text == "￼");
        Assert.Equal(SeededGdrs.ObjectData, embedded.ObjData);
        Assert.All(runs.Where(run => run.Text is "x" or "y"), run => Assert.Null(run.ObjData));
    }

    [Fact]
    public void AStyleCycleIsARefusalNotAnInvalidOperation()
    {
        using var cache = pristine.NewScratch();
        SeededGdrs.Seed(cache);
        SeededGdrs.SeedStyleCycle(cache);

        var refused = Assert.Throws<GdrRefusalException>(() => GdrReader.Show(cache, "GDR-1"));

        Assert.Equal(RefusalCodes.GdrStyleCycle, refused.Refusal.Code);
    }

    [Fact]
    public void TwoListsWithTheRecordsNameRefuseTheListAndShow()
    {
        var path = ProjectWithGdrs(captureBaseline: true, SeededGdrs.SeedSecondList);

        Assert.Equal(RefusalCodes.GdrAmbiguous, GdrQuery.List(new GdrListRequest(path)).Refusal!.Code);
        Assert.Equal(RefusalCodes.GdrAmbiguous, GdrQuery.Show(new GdrShowRequest(path, "GDR-1")).Refusal!.Code);
    }

    [Fact]
    public void TwoRecordsWithOneAbbreviationRefuseShowByThatNumberButNotById()
    {
        var path = ProjectWithGdrs(captureBaseline: true, SeededGdrs.SeedDuplicateAbbreviation);

        Assert.Equal(RefusalCodes.GdrAmbiguous, GdrQuery.Show(new GdrShowRequest(path, "GDR-2")).Refusal!.Code);
        var id = GdrQuery.List(new GdrListRequest(path)).Value!.Records.Single(record => record.Title == "title-4").Id;
        Assert.Equal("title-4", GdrQuery.Show(new GdrShowRequest(path, id)).Value!.Record.Title);
    }

    [Fact]
    public void ShowGivesTheDiscussionAsRunsWithTheStylesAndWritingSystemsItUses()
    {
        var path = ProjectWithGdrs(captureBaseline: true);

        var shown = GdrQuery.Show(new GdrShowRequest(path, "gdr-1"));

        Assert.True(shown.Succeeded, shown.Refusal?.Message);
        var response = shown.Value!;
        Assert.Equal("GDR-1", response.Record.Abbreviation);
        Assert.Equal(2, response.Discussion.Count);

        var first = response.Discussion[0];
        Assert.False(first.RightToLeft);
        Assert.Equal("Normal", first.Style);
        Assert.Equal("x", first.Runs[0].Text);
        Assert.Equal("y", first.Runs.Single(run => run.Style == "Emphasis").Text);

        var second = response.Discussion[1];
        Assert.True(second.RightToLeft);
        Assert.Equal(SeededProject.RightToLeftTag, second.Runs[0].WritingSystem);
        Assert.Equal("ar-text", second.Runs[0].Text);

        var characterStyle = response.Styles.Single(style => style.Name == "Emphasis");
        Assert.Equal(GdrStyleKind.Character, characterStyle.Kind);
        Assert.Equal(1, characterStyle.ByWritingSystem[response.DefaultAnalysis].Ints[TextPropertyNumbers.Italic].Value);
        Assert.Equal(GdrStyleKind.Paragraph, response.Styles.Single(style => style.Name == "Normal").Kind);

        Assert.Equal("rtl", response.WritingSystems.Single(system => system.Tag == SeededProject.RightToLeftTag).Direction);
        Assert.Equal("ltr", response.WritingSystems.Single(system => system.Tag == response.DefaultAnalysis).Direction);
        Assert.NotNull(response.Baseline);
    }

    [Fact]
    public void ShowAcceptsTheCanonicalIdAsWellAsTheNumber()
    {
        var path = ProjectWithGdrs(captureBaseline: true);
        var id = GdrQuery.List(new GdrListRequest(path)).Value!.Records[2].Id;

        var shown = GdrQuery.Show(new GdrShowRequest(path, id));

        Assert.True(shown.Succeeded, shown.Refusal?.Message);
        Assert.Equal("GDR-2", shown.Value!.Record.Abbreviation);
        Assert.Empty(shown.Value.Discussion);
    }

    [Fact]
    public void ShowRefusesAnUnknownRecordAndAnEmptyId()
    {
        var path = ProjectWithGdrs(captureBaseline: true);

        Assert.Equal(RefusalCodes.GdrNotFound, GdrQuery.Show(new GdrShowRequest(path, "GDR-99")).Refusal!.Code);
        Assert.Equal(RefusalCodes.GdrEmptyId, GdrQuery.Show(new GdrShowRequest(path, " ")).Refusal!.Code);
    }

    [Fact]
    public void WithoutABaselineTheListIsEmptyAndShowRefuses()
    {
        var path = ProjectWithGdrs(captureBaseline: false);

        var listed = GdrQuery.List(new GdrListRequest(path));

        Assert.True(listed.Succeeded, listed.Refusal?.Message);
        Assert.False(listed.Value!.HasBaseline);
        Assert.Empty(listed.Value.Records);
        Assert.Equal(RefusalCodes.GdrNoBaseline, GdrQuery.Show(new GdrShowRequest(path, "GDR-1")).Refusal!.Code);
    }

    [Fact]
    public void AProjectWithoutTheListHasNoRecords()
    {
        using var cache = pristine.NewScratch();

        Assert.Empty(GdrReader.List(cache));
        Assert.Null(GdrReader.Show(cache, "GDR-1"));
    }

    [Fact]
    public void ReadingLeavesTheProjectFileUntouched()
    {
        var path = ProjectWithGdrs(captureBaseline: true);
        var baselineFile = BaselineFile(path);
        var before = SHA256.HashData(File.ReadAllBytes(baselineFile));

        GdrQuery.List(new GdrListRequest(path));
        GdrQuery.Show(new GdrShowRequest(path, "GDR-1"));

        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(baselineFile)));
    }

    private static string BaselineFile(string projectPath) => ProjectStoreCommand.Run(projectPath, MotifProductVersion.CurrentText,
        (database, project) => CommandOutcome<string>.Success(
            new BaselineRepository(database).GetCurrent(ProjectWorkspaceKey.Compute(project))!.FwDataPath)).Value!;

    private string ProjectWithGdrs(bool captureBaseline, Action<LcmCache>? extra = null)
    {
        using var cache = pristine.NewScratch();
        SeededGdrs.Seed(cache);
        extra?.Invoke(cache);
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

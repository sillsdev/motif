using System.Security.Cryptography;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
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
                Assert.Equal("Word boundaries", first.Title);
                Assert.Equal("Where one word ends.", first.Description);
            },
            second => Assert.Equal("GDR-2", second.Abbreviation));
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
        Assert.Equal("Words end at ", first.Runs[0].Text);
        Assert.Equal("spaces", first.Runs.Single(run => run.Style == "Emphasis").Text);

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
        var id = GdrQuery.List(new GdrListRequest(path)).Value!.Records[1].Id;

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
        var before = SHA256.HashData(File.ReadAllBytes(path));

        GdrQuery.List(new GdrListRequest(path));
        GdrQuery.Show(new GdrShowRequest(path, "GDR-1"));

        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(path)));
    }

    private string ProjectWithGdrs(bool captureBaseline)
    {
        using var cache = pristine.NewScratch();
        SeededGdrs.Seed(cache);
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

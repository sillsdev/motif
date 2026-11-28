using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>Pins <see cref="GroupQueryCommand"/> over a real, file-backed project with a small Text and lexicon.</summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group1)]
public sealed class GroupQueryCommandTests(PristineProjectFixture pristine)
{
    private string Fixture() => GroupCorpusFixture.Create(pristine);

    private static GroupRequest Suffix(string project, string? state = null, string? asOf = null) =>
        new(project, "suffix", Ending: "an", ParseState: state, AsOf: asOf);

    [Fact]
    public void SuffixGroupsTheTextsWordsByStemAndMarksEachStem()
    {
        var project = Fixture();
        var outcome = GroupQueryCommand.Run(Suffix(project));
        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var rows = outcome.Value!.Rows.ToDictionary(row => row.Stem);
        Assert.Equal("known", rows["kaon"].Status);
        Assert.Equal("fitting", rows["baha"].Status);
        Assert.Equal("not-fitting", rows["lis"].Status);
        Assert.Equal((1, 2), (rows["lis"].WordsTotal, rows["lis"].Tokens));
        Assert.Equal(3, rows["kaon"].Tokens);
        Assert.Equal("approved", rows["kaon"].Words[0].ParseState);
        Assert.Equal("parsed", rows["baha"].Words[0].ParseState);
        Assert.Equal("unparsed", rows["lis"].Words[0].ParseState);
        Assert.Equal("saved project", outcome.Value.ReadAt);
    }

    [Theory]
    [InlineData("approved", "kaon")]
    [InlineData("parsed", "baha")]
    [InlineData("unparsed", "lis")]
    public void ParseStateKeepsOnlyThatStatesWords(string state, string stem)
    {
        var outcome = GroupQueryCommand.Run(Suffix(Fixture(), state));
        Assert.Equal(stem, Assert.Single(outcome.Value!.Rows).Stem);
    }

    [Fact]
    public void PrefixAndPatternGroupToo()
    {
        var project = Fixture();
        var prefix = GroupQueryCommand.Run(new GroupRequest(project, "prefix", Start: "ba"));
        Assert.True(prefix.Succeeded, prefix.Refusal?.Message);
        Assert.Contains(prefix.Value!.Rows, row => row.Stem == "ha");
        var pattern = GroupQueryCommand.Run(new GroupRequest(project, "suffix", Pattern: "(?<stem>[a-z]+)an"));
        Assert.Equal(3, pattern.Value!.Groups);
    }

    [Fact]
    public void APatternThatCannotBeComposedIsRefusedByItsOwnCodeNotAsAnInvalidRequest()
    {
        var outcome = GroupQueryCommand.Run(new GroupRequest(Fixture(), "suffix", Pattern: "(?<stem>.+)\u0BC6(\u0BBE)"));
        Assert.Equal(RefusalCodes.QueryPatternNotNormalizable, outcome.Refusal?.Code);
    }

    private static List<string> Snapshot(string folder) =>
        [.. Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(file =>
            $"{Path.GetRelativePath(folder, file)} {File.GetLastWriteTimeUtc(file).Ticks} {Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file)))}")];

    [Fact]
    public void ReadingNeverChangesTheProjectFile()
    {
        var project = Fixture();
        var folder = Path.GetDirectoryName(project)!;
        var before = Snapshot(folder);
        Assert.True(GroupQueryCommand.Run(Suffix(project)).Succeeded);
        Assert.False(GroupQueryCommand.Run(Suffix(project, asOf: "baseline")).Succeeded);
        Assert.Equal(before, Snapshot(folder));
    }

    [Fact]
    public void ABadRequestIsRefusedWithItsRegisteredCode()
    {
        var project = Fixture();
        var both = GroupQueryCommand.Run(new GroupRequest(project, "suffix", Ending: "an", Start: "ba"));
        Assert.Equal(RefusalCodes.GroupRequestInvalid, both.Refusal!.Code);
        var none = GroupQueryCommand.Run(new GroupRequest(project, "suffix"));
        Assert.Equal(RefusalCodes.GroupRequestInvalid, none.Refusal!.Code);
        var page = GroupQueryCommand.Run(new GroupRequest(project, "suffix", Ending: "an", Cursor: "9"));
        Assert.Equal(RefusalCodes.PagingInvalid, page.Refusal!.Code);
    }

    [Fact]
    public void ARevisionDigestIsRefusedAsUnavailable()
    {
        var digest = GroupQueryCommand.Run(Suffix(Fixture(), asOf: "sha256:abc"));
        Assert.Equal(RefusalCodes.GroupAsOfUnavailable, digest.Refusal!.Code);
    }

    [Fact]
    public void AsOfBaselineRefusesWhenThereIsNoStoreOrBaseline()
    {
        var outcome = GroupQueryCommand.Run(Suffix(Fixture(), asOf: "baseline"));
        Assert.Equal(RefusalCodes.GroupAsOfUnavailable, outcome.Refusal!.Code);
    }
}

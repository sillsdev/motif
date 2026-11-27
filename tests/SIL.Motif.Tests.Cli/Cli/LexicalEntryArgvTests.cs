using SIL.Motif.Commands.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
public sealed class LexicalEntryArgvTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-lexical-entry-argv-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ASenseIdReadsItsEntryAndANonIdIsUsage()
    {
        Directory.CreateDirectory(_root);
        var path = pristine.CopyProjectFile();
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), _root).Succeeded);
        var sense = CanonicalId.FromGuid(pristine.Seed.FirstSenseId).Value;

        var json = await CliProcess.RunAsync(_root, null, false, "lexical-entry", "--project", path, sense, "--json");
        var human = await CliProcess.RunAsync(_root, null, false, "lexical-entry", "--project", path, sense);
        var bare = await CliProcess.RunAsync(_root, null, false, "lexical-entry", "--project", path);

        Assert.True(json.ExitCode == 0, json.Error);
        var lookup = Assert.Single(ProjectionJson.Deserialize<LexicalEntryResponse>(json.Output)!.Lookups);
        Assert.Equal((true, sense, SeededProject.FirstForm), (lookup.Found, lookup.SenseId, lookup.Headword));
        Assert.Equal(SeededProject.FirstGloss, Assert.Single(lookup.Senses).Gloss);
        Assert.Contains(SeededProject.FirstForm, human.Output);
        Assert.Contains("* 1 " + SeededProject.FirstGloss, human.Output);
        Assert.True(bare.ExitCode == FailureEnvelope.ExitCodeFor(FailureReason.InvalidArgument), bare.Error);
    }

    [Fact]
    public async Task AGuidReadsTheSameEntryAsTheCanonicalId()
    {
        Directory.CreateDirectory(_root);
        var path = pristine.CopyProjectFile();
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), _root).Succeeded);

        var json = await CliProcess.RunAsync(_root, null, false, "lexical-entry", "--project", path,
            pristine.Seed.FirstSenseId.ToString("D"), "--json");

        Assert.True(json.ExitCode == 0, json.Error);
        var lookup = Assert.Single(ProjectionJson.Deserialize<LexicalEntryResponse>(json.Output)!.Lookups);
        Assert.Equal((true, SeededProject.FirstForm), (lookup.Found, lookup.Headword));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}

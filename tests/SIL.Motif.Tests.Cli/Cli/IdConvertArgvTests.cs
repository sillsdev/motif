using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class IdConvertArgvTests
{
    private static readonly Guid Sample = Guid.Parse("3f2a8c10-5b7e-4d21-9a63-0c1e2f3a4b5c");
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-id-convert-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task EachFormConvertsToBothWithNoProjectAndJsonIsAnArray()
    {
        Directory.CreateDirectory(_root);
        var canonical = CanonicalId.FromGuid(Sample).Value;

        var result = await CliProcess.RunAsync(_root, null, false, "id", "convert", Sample.ToString("D"), canonical, "--json");

        Assert.True(result.ExitCode == 0, result.Error);
        var items = ProjectionJson.Deserialize<List<IdConvertItem>>(result.Output)!;
        Assert.Equal(2, items.Count);
        Assert.All(items, item => Assert.Equal((Sample.ToString("D"), canonical), (item.Guid, item.Canonical)));
        Assert.Equal([Sample.ToString("D"), canonical], items.Select(item => item.Input));
    }

    [Fact]
    public async Task AnArgumentThatIsNeitherFormIsCodedAndTheOthersStillPrint()
    {
        Directory.CreateDirectory(_root);
        var canonical = CanonicalId.FromGuid(Sample).Value;

        var json = await CliProcess.RunAsync(_root, null, false, "id", "convert", "nonsense", canonical, "--json");
        var human = await CliProcess.RunAsync(_root, null, false, "id", "convert", "nonsense", canonical);

        Assert.Equal(FailureEnvelope.ExitCodeFor(FailureReason.InvalidArgument), json.ExitCode);
        var items = ProjectionJson.Deserialize<List<IdConvertItem>>(json.Output)!;
        Assert.Equal(IdConvertResponse.InvalidIdCode, items[0].Code);
        Assert.Null(items[0].Guid);
        Assert.Equal(Sample.ToString("D"), items[1].Guid);
        Assert.Contains("id.invalid", human.Output);
        Assert.Contains("guid " + Sample.ToString("D"), human.Output);
    }

    [Fact]
    public async Task NoIdIsUsage()
    {
        Directory.CreateDirectory(_root);

        var result = await CliProcess.RunAsync(_root, null, false, "id", "convert");

        Assert.Equal(FailureEnvelope.ExitCodeFor(FailureReason.InvalidArgument), result.ExitCode);
    }
}

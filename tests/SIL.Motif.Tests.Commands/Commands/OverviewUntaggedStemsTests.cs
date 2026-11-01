using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using Xunit;
using static SIL.Motif.Tests.Commands.StoredWords;

namespace SIL.Motif.Tests.Commands;

public sealed class OverviewUntaggedStemsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.OverviewUntaggedStemsTests",
        Guid.NewGuid().ToString("N"));

    public OverviewUntaggedStemsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static UntaggedStems Stems(int count) => new(count,
        [new(Guid.NewGuid(), Guid.NewGuid().ToString("D"), "kat", "Noun", 3)]);

    [Fact]
    public void TheBaselineSummarysCountAndTopEntriesReachTheOverviewWithoutAnyStoredParse()
    {
        using var fixture = new OverviewMeasuresFixture(_root, new Dictionary<string, int> { ["ok"] = 1 },
            untagged: Stems(7));

        var response = OverviewCommand.Overview(new OverviewRequest(fixture.FwDataPath)).Value!;

        var untagged = response.LookFirst.StemsWithoutInflectionClass!;
        Assert.Equal(7, untagged.Count);
        Assert.Equal(("kat", "Noun", 3), (untagged.Top[0].Headword, untagged.Top[0].Category, untagged.Top[0].RestrictedAffixes));
    }

    [Fact]
    public void ABaselineWithNoSuchStemsLeavesTheMeasureOutAndAStoredParseDoesNotChangeIt()
    {
        using var none = new OverviewMeasuresFixture(_root, new Dictionary<string, int> { ["ok"] = 1 });
        using var some = new OverviewMeasuresFixture(_root, new Dictionary<string, int> { ["ok"] = 1 }, untagged: Stems(2));
        some.Assess(Parsed("ok", 1));

        var quiet = OverviewCommand.Overview(new OverviewRequest(none.FwDataPath)).Value!;
        var parsed = OverviewCommand.Overview(new OverviewRequest(some.FwDataPath)).Value!;

        Assert.Null(quiet.LookFirst.StemsWithoutInflectionClass);
        Assert.Equal(2, parsed.LookFirst.StemsWithoutInflectionClass!.Count);
    }
}

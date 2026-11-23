using System.Text.Json;
using SIL.Motif.Commands.Review;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Review;
using Xunit;

namespace SIL.Motif.Tests.Commands.Review;

public sealed class RegressionOverlapTests
{
    private static readonly Guid Entry = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Form = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid Other = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003");

    private static RegressedReading Reading() => new("a + b", [new RegressedMorph(
        CanonicalId.FromGuid(Entry).ToString(), null, CanonicalId.FromGuid(Form).ToString())]);

    [Fact]
    public void OnlyOperationsThatShareAnIdAreListedAndTheyAreCalledAnOverlap()
    {
        var context = new RegressionContext(
        [
            new OperationObjects(1, "op-1", "grammar/phEnvironment/setName", new HashSet<Guid> { Other }),
            new OperationObjects(2, "op-2", "lexical/lexEntry/createAlternateForms", new HashSet<Guid> { Entry, Other }),
        ], null);
        var touched = Assert.Single(RegressionContext.Overlap(context, [Reading()]));
        Assert.Equal(2, touched.Position);
        Assert.Equal([CanonicalId.FromGuid(Entry).ToString()], touched.SharedIds);
        Assert.Contains("not a finding that the operation caused the loss", touched.Statement);
    }

    [Fact]
    public void NoSharedIdGivesNoOperation()
    {
        var context = new RegressionContext([new OperationObjects(1, "op-1", "k/x", new HashSet<Guid> { Other })], null);
        Assert.Empty(RegressionContext.Overlap(context, [Reading()]));
        Assert.Empty(RegressionContext.Overlap(context, []));
    }

    [Fact]
    public void ACanonicalIdAndAGuidParseToTheSameId()
    {
        Assert.Equal(Form, RegressionContext.ToGuid(CanonicalId.FromGuid(Form).ToString()));
        Assert.Equal(Form, RegressionContext.ToGuid(Form.ToString()));
        Assert.Null(RegressionContext.ToGuid("not an id"));
    }

    [Theory]
    [InlineData("kwish", "'kwish'")]
    [InlineData("a\"b$c`d", "'a\"b$c`d'")]
    [InlineData("it's", "'it'\\''s'")]
    public void ATraceCommandQuotesTheWordForAShell(string word, string expected) =>
        Assert.Equal(expected, TrialRegressionReader.ShellQuote(word));

    [Fact]
    public void AWordWrittenBeforeIdsWereKeptStillReadsAndAnUncomputedOverlapStaysNull()
    {
        var old = JsonSerializer.Deserialize<RegressedWord>(
            "{\"word\":\"w\",\"caseKey\":\"k\",\"beforeReadingCount\":1,\"afterReadingCount\":0,\"before\":[\"a\"],\"after\":[]}",
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Null(old.TouchedBy);
        var json = JsonSerializer.Serialize(old, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("\"touchedBy\":null", json);
    }
}

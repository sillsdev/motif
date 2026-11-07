using System.Text.Json;
using SIL.Motif.Contract.Review;
using Xunit;

namespace SIL.Motif.Tests.Contract.Review;

public class ReviewReportContractTests
{
    [Fact]
    public void FlagsHaveExactlyFiveIdsAndIncompleteIsSeparate()
    {
        Assert.Equal(5, Enum.GetValues<ReviewFlagId>().Length);
        Assert.DoesNotContain(Enum.GetNames<ReviewFlagId>(), name => name.Contains("Incomplete"));
        Assert.Equal(new[] { "\"new-word-failures\"", "\"confirmed-readings-lost\"", "\"known-nonwords-now-accepted\"",
            "\"parser-work-increase\"", "\"new-warnings-or-parsimony\"" },
            Enum.GetValues<ReviewFlagId>().Select(value => JsonSerializer.Serialize(value, ReviewJson.Options)));
    }

    [Fact]
    public void NumericEnumValuesAreRejected()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ReviewFlagId>("0", ReviewJson.Options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<WorkComparison>("0", ReviewJson.Options));
    }

    [Fact]
    public void UnknownValuesRemainNull()
    {
        var work = new WorkChange(null, 0, "scope", WorkComparison.NotComparable, ["missing"]);
        var json = JsonSerializer.Serialize(work, ReviewJson.Options);
        Assert.Contains("\"percentChange\":null", json);
        Assert.Null(JsonSerializer.Deserialize<WorkChange>(json, ReviewJson.Options)!.PercentChange);
    }

    [Fact]
    public void WireNumbersAndMemberNamesAreStrict()
    {
        const string valid = "{\"kind\":\"parser-work\",\"percentChange\":null,\"wordCount\":0,\"scopeDigest\":\"x\",\"comparison\":\"not-comparable\",\"reasonCodes\":[]}";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<WorkChange>(valid.Replace("\"wordCount\":0", "\"wordCount\":\"0\""), ReviewJson.Options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<WorkChange>(valid.Replace("wordCount", "WordCount"), ReviewJson.Options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<WorkChange>(valid.Replace("\"wordCount\":0", "\"wordCount\":9007199254740992"), ReviewJson.Options));
    }

    [Fact]
    public void InvalidRuntimeCountsAreRejectedBeforeSerialization()
    {
        var work = new WorkChange(null, -1, "scope", WorkComparison.NotComparable, []);
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(work, ReviewJson.Options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ReadArguments>("{\"limit\":0}", ReviewJson.Options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ReadArguments>("{\"limit\":201}", ReviewJson.Options));
    }

    [Fact]
    public void RequiredNullsSurviveAnIgnoreNullSerializer()
    {
        var options = new JsonSerializerOptions(ReviewJson.Options)
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };
        var json = JsonSerializer.Serialize(new WorkChange(null, 0, "scope", WorkComparison.NotComparable, []), options);
        Assert.Contains("\"percentChange\":null", json);
    }

    [Fact]
    public void ReadTargetsCarryClosedTrialInputs()
    {
        const string json = "{\"proposal\":\"p\",\"action\":\"run\",\"review_sets\":[\"set\"],\"wait_seconds\":40}";
        var args = JsonSerializer.Deserialize<ReadArguments>(json, ReviewJson.Options)!;
        Assert.Contains("\"review_sets\":[\"set\"]", JsonSerializer.Serialize(args, ReviewJson.Options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ReadArguments>(
            "{\"proposal\":\"p\",\"sql\":\"select\"}", ReviewJson.Options));
    }

    [Fact]
    public void UnknownFieldsAndNonfinitePercentAreRejected()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<WorkChange>(
            "{\"kind\":\"parser-work\",\"percentChange\":null,\"wordCount\":0,\"scopeDigest\":\"x\",\"comparison\":\"not-comparable\",\"reasonCodes\":[],\"extra\":0}", ReviewJson.Options));
        Assert.ThrowsAny<Exception>(() => JsonSerializer.Serialize(
            new WorkChange(double.NaN, 0, "x", WorkComparison.Comparable, []), ReviewJson.Options));
    }
}

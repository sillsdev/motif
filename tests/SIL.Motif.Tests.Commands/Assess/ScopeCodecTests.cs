using System.Text.Json;
using SIL.Motif.Host.Assess;
using SIL.Motif.Contract.Assess;
using Xunit;

namespace SIL.Motif.Tests.Assess;

public sealed class ScopeCodecTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(200000)]
    public void TrialRoundTripsBothBudgetsWithoutAnEngine(int steps)
    {
        var original = new StoredScope.Trial("words", new[] { "cat" },
            new[] { AssessmentKind.ParseTime }, TimeSpan.FromMilliseconds(750), steps);
        var json = ScopeCodec.Write(original);
        using var document = JsonDocument.Parse(json);
        var cap = document.RootElement.GetProperty("perWordParserWorkLimit");
        Assert.Equal(steps, cap.GetProperty("work").GetInt64());
        Assert.False(cap.GetProperty("isUnbounded").GetBoolean());
        var scope = ScopeCodec.ReadTrial(json, "coverage");
        Assert.Equal(new ParserWorkLimit(steps), scope.PerWordParserWorkLimit);
        Assert.Equal(TimeSpan.FromMilliseconds(750), scope.PerWordLimit);
        Assert.Equal(original.Words, scope.Words);
        Assert.Equal(original.Collect, scope.Collect);
        Assert.DoesNotContain("engine", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"engine\":\"fast\",\"perWordLimitMs\":1000}")]
    [InlineData("{\"perWordLimitMs\":1000}")]
    [InlineData("{\"perWordLimitMs\":1000,\"perWordParserWorkLimit\":{\"steps\":-1,\"isUnbounded\":false}}")]
    [InlineData("{\"perWordLimitMs\":1000,\"perWordParserWorkLimit\":{\"steps\":0,\"isUnbounded\":false}}")]
    [InlineData("{\"perWordLimitMs\":1000,\"perWordParserWorkLimit\":{\"steps\":200000,\"isUnbounded\":true}}")]
    [InlineData("{\"perWordLimitMs\":1000,\"perWordParserWorkLimit\":{\"steps\":null,\"isUnbounded\":false}}")]
    [InlineData("{\"perWordLimitMs\":0,\"perWordParserWorkLimit\":200000}")]
    [InlineData("{\"perWordLimitMs\":1000,\"perWordParserWorkLimit\":200000,\"engine\":\"fast\"}")]
    [InlineData("[]")]
    [InlineData("{\"perWordLimitMs\":9223372036854775807,\"perWordParserWorkLimit\":200000}")]
    [InlineData("{\"perWordLimitMs\":1000,\"perWordParserWorkLimit\":\"many\"}")]
    [InlineData("{\"perWordLimitMs\":1000,\"perWordParserWorkLimit\":200000,\"budget\":1}")]
    public void ObsoleteOrInvalidScopesRefuseWithRecreationGuidance(string json)
    {
        var refusal = Assert.Throws<ReportRefusalException>(() => ScopeCodec.ReadTrial(json, "coverage"));
        Assert.Contains("recreate", refusal.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("{\"perWordLimitMs\":750,\"perWordStepLimit\":{\"steps\":200000,\"isUnbounded\":false}}", 20_000_000L)]
    [InlineData("{\"perWordLimitMs\":750,\"perWordParserWorkLimit\":{\"steps\":3,\"isUnbounded\":false}}", 300L)]
    [InlineData("{\"perWordLimitMs\":750,\"perWordParserWorkLimit\":{\"work\":12345,\"isUnbounded\":false}}", 12_345L)]
    public void ASavedScopeWithAnOldAttemptLimitReadsAsOneHundredTimesItsValue(string json, long expectedWork)
    {
        var scope = ScopeCodec.ReadTrial(json, "coverage");
        Assert.Equal(new ParserWorkLimit(expectedWork), scope.PerWordParserWorkLimit);
        var rewritten = ScopeCodec.ReadTrial(ScopeCodec.Write(scope), "coverage");
        Assert.Equal(scope.PerWordParserWorkLimit, rewritten.PerWordParserWorkLimit);
    }

    [Fact]
    public void ScopeDefaultsToTwoIndependentBudgets()
    {
        var scope = new AssessmentScope.Words(Array.Empty<string>(), Array.Empty<AssessmentKind>(), TimeSpan.FromSeconds(1));
        Assert.Equal(ParserWorkLimit.Default, scope.PerWordParserWorkLimit);
        Assert.Throws<ArgumentOutOfRangeException>(() => new AssessmentScope.Words(
            Array.Empty<string>(), Array.Empty<AssessmentKind>(), TimeSpan.FromSeconds(1), -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AssessmentScope.Words(
            Array.Empty<string>(), Array.Empty<AssessmentKind>(), TimeSpan.Zero, 0));
    }

    [Fact]
    public void TrialWritesAndReadsAnExplicitUnboundedParserWorkLimit()
    {
        var trial = new StoredScope.Trial("words", ["cat"], [AssessmentKind.ParseTime],
            TimeSpan.FromSeconds(1), ParserWorkLimit.Unbounded);

        var json = ScopeCodec.Write(trial);
        using var document = JsonDocument.Parse(json);
        var cap = document.RootElement.GetProperty("perWordParserWorkLimit");
        Assert.Equal(JsonValueKind.Null, cap.GetProperty("work").ValueKind);
        Assert.True(cap.GetProperty("isUnbounded").GetBoolean());

        var stored = ScopeCodec.ReadTrial(json, "coverage");

        Assert.Equal(ParserWorkLimit.Unbounded, stored.PerWordParserWorkLimit);
    }

    [Fact]
    public void TrialWritesAndReadsNoPerWordTimeLimit()
    {
        var json = ScopeCodec.Write(new StoredScope.Trial(
            "assess", ["motifa"], [AssessmentKind.ParseTime], null, ParserWorkLimit.Unbounded));

        using var document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.TryGetProperty("perWordLimitMs", out var limit));
        Assert.Equal(JsonValueKind.Null, limit.ValueKind);
        var stored = ScopeCodec.ReadTrial(json, "report");
        Assert.Null(stored.PerWordLimit);
        Assert.Equal(ParserWorkLimit.Unbounded, stored.PerWordParserWorkLimit);
    }
}

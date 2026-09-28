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
        var cap = document.RootElement.GetProperty("perWordStepLimit");
        Assert.Equal(steps, cap.GetProperty("steps").GetInt64());
        Assert.False(cap.GetProperty("isUnbounded").GetBoolean());
        var scope = ScopeCodec.ReadTrial(json, "coverage");
        Assert.Equal(new StepCap(steps), scope.PerWordStepLimit);
        Assert.Equal(TimeSpan.FromMilliseconds(750), scope.PerWordLimit);
        Assert.Equal(original.Words, scope.Words);
        Assert.Equal(original.Collect, scope.Collect);
        Assert.DoesNotContain("engine", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"engine\":\"fast\",\"perWordLimitMs\":1000}")]
    [InlineData("{\"perWordLimitMs\":1000}")]
    [InlineData("{\"perWordLimitMs\":1000,\"perWordStepLimit\":{\"steps\":-1,\"isUnbounded\":false}}")]
    [InlineData("{\"perWordLimitMs\":1000,\"perWordStepLimit\":{\"steps\":0,\"isUnbounded\":false}}")]
    [InlineData("{\"perWordLimitMs\":1000,\"perWordStepLimit\":{\"steps\":200000,\"isUnbounded\":true}}")]
    [InlineData("{\"perWordLimitMs\":1000,\"perWordStepLimit\":{\"steps\":null,\"isUnbounded\":false}}")]
    [InlineData("{\"perWordLimitMs\":0,\"perWordStepLimit\":200000}")]
    [InlineData("{\"perWordLimitMs\":1000,\"perWordStepLimit\":200000,\"engine\":\"fast\"}")]
    [InlineData("[]")]
    [InlineData("{\"perWordLimitMs\":9223372036854775807,\"perWordStepLimit\":200000}")]
    [InlineData("{\"perWordLimitMs\":1000,\"perWordStepLimit\":\"many\"}")]
    [InlineData("{\"perWordLimitMs\":1000,\"perWordStepLimit\":200000,\"budget\":1}")]
    public void ObsoleteOrInvalidScopesRefuseWithRecreationGuidance(string json)
    {
        var refusal = Assert.Throws<ReportRefusalException>(() => ScopeCodec.ReadTrial(json, "coverage"));
        Assert.Contains("recreate", refusal.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ScopeDefaultsToTwoIndependentBudgets()
    {
        var scope = new AssessmentScope(Array.Empty<string>(), Array.Empty<AssessmentKind>(), TimeSpan.FromSeconds(1));
        Assert.Equal(StepCap.Default, scope.PerWordStepLimit);
        Assert.Throws<ArgumentOutOfRangeException>(() => new AssessmentScope(
            Array.Empty<string>(), Array.Empty<AssessmentKind>(), TimeSpan.FromSeconds(1), -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AssessmentScope(
            Array.Empty<string>(), Array.Empty<AssessmentKind>(), TimeSpan.Zero, 0));
    }

    [Fact]
    public void TrialWritesAndReadsAnExplicitUnboundedStepCap()
    {
        var trial = new StoredScope.Trial("words", ["cat"], [AssessmentKind.ParseTime],
            TimeSpan.FromSeconds(1), StepCap.Unbounded);

        var json = ScopeCodec.Write(trial);
        using var document = JsonDocument.Parse(json);
        var cap = document.RootElement.GetProperty("perWordStepLimit");
        Assert.Equal(JsonValueKind.Null, cap.GetProperty("steps").ValueKind);
        Assert.True(cap.GetProperty("isUnbounded").GetBoolean());

        var stored = ScopeCodec.ReadTrial(json, "coverage");

        Assert.Equal(StepCap.Unbounded, stored.PerWordStepLimit);
    }

    [Fact]
    public void TrialWritesAndReadsNoPerWordTimeLimit()
    {
        var json = ScopeCodec.Write(new StoredScope.Trial(
            "assess", ["motifa"], [AssessmentKind.ParseTime], null, StepCap.Unbounded));

        using var document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.TryGetProperty("perWordLimitMs", out var limit));
        Assert.Equal(JsonValueKind.Null, limit.ValueKind);
        var stored = ScopeCodec.ReadTrial(json, "report");
        Assert.Null(stored.PerWordLimit);
        Assert.Equal(StepCap.Unbounded, stored.PerWordStepLimit);
    }
}

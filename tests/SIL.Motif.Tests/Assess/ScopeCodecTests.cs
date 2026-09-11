using SIL.Motif.Host.Assess;
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
        var scope = ScopeCodec.ReadTrial(json, "coverage");
        Assert.Equal(steps, scope.PerWordStepLimit);
        Assert.Equal(TimeSpan.FromMilliseconds(750), scope.PerWordLimit);
        Assert.Equal(original.Words, scope.Words);
        Assert.Equal(original.Collect, scope.Collect);
        Assert.DoesNotContain("engine", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"engine\":\"fast\",\"perWordLimitMs\":1000}")]
    [InlineData("{\"perWordLimitMs\":1000}")]
    [InlineData("{\"perWordLimitMs\":1000,\"perWordStepLimit\":-1}")]
    [InlineData("{\"perWordLimitMs\":1000,\"perWordStepLimit\":0}")]
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
        Assert.Equal(200000, scope.PerWordStepLimit);
        Assert.Throws<ArgumentOutOfRangeException>(() => new AssessmentScope(
            Array.Empty<string>(), Array.Empty<AssessmentKind>(), TimeSpan.FromSeconds(1), -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AssessmentScope(
            Array.Empty<string>(), Array.Empty<AssessmentKind>(), TimeSpan.Zero, 0));
    }
}

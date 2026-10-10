using SIL.Motif.EvalSets;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Evals;

[Collection(LcmCacheParallelCollections.Group0)]
public sealed class EvaluationSetVerificationTests
{
    [RealEvalSetParserFact]
    public async Task T0SetParsesEveryGoldWordAndRejectsEveryNegative()
    {
        var result = await Verify("eval-t0-qaxu");

        Assert.Equal(180, result.TrainWords);
        Assert.Equal(60, result.HeldoutWords);
        Assert.Equal(120, result.NegativeWords);
        Assert.Equal(240, result.GoldAnalysesMatched);
        Assert.Equal(0, result.NegativeParses);
    }

    [RealEvalSetParserFact]
    public async Task T1SetParsesEveryGoldWordAndRejectsEveryNegative()
    {
        var result = await Verify("eval-t1-vexu");

        Assert.Equal(180, result.TrainWords);
        Assert.Equal(60, result.HeldoutWords);
        Assert.Equal(120, result.NegativeWords);
        Assert.Equal(240, result.GoldAnalysesMatched);
        Assert.Equal(0, result.NegativeParses);
    }

    [RealEvalSetParserFact]
    public async Task T2SetParsesEveryGoldWordAndRejectsEveryNegative()
    {
        var result = await Verify("eval-t2-lomi");

        Assert.Equal(180, result.TrainWords);
        Assert.Equal(60, result.HeldoutWords);
        Assert.Equal(120, result.NegativeWords);
        Assert.Equal(240, result.GoldAnalysesMatched);
        Assert.Equal(0, result.NegativeParses);
    }

    [RealEvalSetParserFact]
    public async Task T3SetParsesEveryGoldWordAndRejectsEveryNegative()
    {
        var result = await Verify("eval-t3-panu");

        Assert.Equal(180, result.TrainWords);
        Assert.Equal(60, result.HeldoutWords);
        Assert.Equal(120, result.NegativeWords);
        Assert.Equal(240, result.GoldAnalysesMatched);
        Assert.Equal(0, result.NegativeParses);
    }

    private static async Task<EvaluationVerificationResult> Verify(string setId)
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-eval-sets-" + Guid.NewGuid().ToString("N"));
        try
        {
            return await EvaluationSetVerifier.VerifySetAsync(
                Path.Combine(TestGrammars.SetsRoot!, setId),
                root);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}

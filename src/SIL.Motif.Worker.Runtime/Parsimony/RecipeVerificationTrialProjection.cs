using System.Text.Json;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Worker.Parsimony;

/// <summary>Projects a stored Trial Assessment without consulting its mutated project scratch.</summary>
public static class RecipeVerificationTrialProjection
{
    /// <summary>Builds an immutable comparison input from one stored Assessment and its invocation record.</summary>
    public static RecipeVerificationRun From(AssessmentRecord assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        BaselineToken baseline;
        try
        {
            baseline = JsonSerializer.Deserialize<BaselineToken>(assessment.BaselineToken, MotifJson.CreateOptions())
                ?? throw new JsonException("Null Baseline token.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The Trial Assessment has an invalid Baseline binding.", exception);
        }

        var unavailable = new List<string>();
        if (assessment.Words is null)
            unavailable.Add("The Trial Assessment has no retained word rows.");
        var invocation = assessment.Invocation;
        RecipeVerificationOptions? options = null;
        if (invocation is null)
        {
            unavailable.Add("The Trial Assessment has no retained parser invocation limits.");
        }
        else
        {
            options = new RecipeVerificationOptions(invocation.ExecutableBytesSha256,
                invocation.PerWordTimeoutMs, invocation.PerWordStepLimit, invocation.Threads,
                invocation.CollectStatistics);
        }

        return new RecipeVerificationRun(assessment.ToComparable(), baseline, options,
            Array.AsReadOnly(unavailable.ToArray()),
            Array.AsReadOnly(assessment.Selection.Words.ToArray()));
    }
}

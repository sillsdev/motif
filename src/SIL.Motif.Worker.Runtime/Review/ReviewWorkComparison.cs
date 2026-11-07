using System.Numerics;
using SIL.Motif.Contract.Review;

namespace SIL.Motif.Worker.Runtime.Review;

/// <summary>Compares work only over a complete, compatible, explicitly resolved scope.</summary>
public static class ReviewWorkComparison
{
    /// <summary>Returns a percent change or the reasons that the comparison is unavailable.</summary>
    public static WorkChange Compare(ReviewWorkInputs inputs) => Calculate(inputs).Change;

    /// <summary>Whether exact, unrounded scope totals exceed the twenty-percent threshold.</summary>
    public static bool IsRedFlag(ReviewWorkInputs inputs) => Calculate(inputs).Flag;

    /// <summary>Computes percent change from nonnegative decimal totals, leaving a zero baseline unknown.</summary>
    public static double? PercentChange(decimal before, decimal after)
    {
        if (before <= 0 || after < 0) return null;
        try { return (double)checked((after - before) / before * 100m); }
        catch (OverflowException) { return null; }
    }

    private static (WorkChange Change, bool Flag) Calculate(ReviewWorkInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var requested = inputs.RequiredCases.Distinct(StringComparer.Ordinal).ToArray();
        var reasons = new List<string>();
        var b = inputs.BeforeScope;
        var a = inputs.AfterScope;
        if (string.IsNullOrWhiteSpace(b.ScopeDigest) || string.IsNullOrWhiteSpace(b.BaselineTokenDigest) ||
            b.ScopeDigest != a.ScopeDigest || b.BaselineTokenDigest != a.BaselineTokenDigest) reasons.Add("scope-mismatch");
        if (string.IsNullOrWhiteSpace(b.Assessor) || b.Assessor != a.Assessor) reasons.Add("assessor-mismatch");
        if (string.IsNullOrWhiteSpace(b.ParserDigest) || b.ParserDigest != a.ParserDigest) reasons.Add("parser-mismatch");
        if (string.IsNullOrWhiteSpace(b.CounterSemantics) || b.CounterSemantics != a.CounterSemantics) reasons.Add("counter-version-mismatch");
        if (string.IsNullOrWhiteSpace(b.SettingsDigest) || b.SettingsDigest != a.SettingsDigest) reasons.Add("parser-settings-mismatch");
        if (string.IsNullOrWhiteSpace(b.TokeniserDigest) || b.TokeniserDigest != a.TokeniserDigest) reasons.Add("tokeniser-mismatch");
        if (requested.Length == 0) reasons.Add("empty-scope");
        var before = Index(inputs.Before, reasons);
        var after = Index(inputs.After, reasons);
        decimal beforeSum = 0, afterSum = 0;
        foreach (var key in requested)
        {
            if (!before.TryGetValue(key, out var old) || !after.TryGetValue(key, out var current) ||
                !old.Complete || !current.Complete || old.Work is null or < 0 || current.Work is null or < 0)
            {
                reasons.Add("incomplete-or-missing-work");
                continue;
            }
            try { beforeSum = checked(beforeSum + old.Work.Value); afterSum = checked(afterSum + current.Work.Value); }
            catch (OverflowException) { reasons.Add("work-overflow"); }
        }
        if (reasons.Count == 0 && beforeSum == 0) reasons.Add("zero-baseline-work");
        if (reasons.Count != 0)
            return (new WorkChange(null, requested.Length, a.ScopeDigest, WorkComparison.NotComparable,
                reasons.Distinct(StringComparer.Ordinal).ToArray()), false);
        var percent = PercentChange(beforeSum, afterSum);
        if (percent is null)
            return (new WorkChange(null, requested.Length, a.ScopeDigest, WorkComparison.NotComparable,
                ["work-overflow"]), false);
        var annotations = before.Keys.Except(requested, StringComparer.Ordinal).Any()
            ? new[] { "baseline-extra-cases-excluded" } : [];
        var flag = (BigInteger)afterSum * 5 > (BigInteger)beforeSum * 6;
        return (new WorkChange(percent, requested.Length, a.ScopeDigest, WorkComparison.Comparable, annotations), flag);
    }

    private static Dictionary<string, ReviewWorkCase> Index(IReadOnlyList<ReviewWorkCase> cases, List<string> reasons)
    {
        var result = new Dictionary<string, ReviewWorkCase>(StringComparer.Ordinal);
        foreach (var item in cases)
            if (!result.TryAdd(item.CaseKey, item)) reasons.Add("duplicate-case");
        return result;
    }
}

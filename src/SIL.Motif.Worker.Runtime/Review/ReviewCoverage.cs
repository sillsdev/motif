using SIL.Motif.Contract.Review;

namespace SIL.Motif.Worker.Runtime.Review;

/// <summary>Counts observed coverage against the complete frozen denominator.</summary>
public static class ReviewCoverage
{
    private const long MaximumWireCount = 9007199254740991L;

    /// <summary>Summarizes weighted observations without rounding public counts.</summary>
    public static Coverage Count(IEnumerable<(bool? Covered, long Weight)> observations)
    {
        long yes = 0, no = 0, unknown = 0;
        try
        {
            foreach (var (covered, weight) in observations)
            {
                if (weight <= 0) return Unavailable("invalid-weight");
                if (covered == true) yes = checked(yes + weight);
                else if (covered == false) no = checked(no + weight);
                else unknown = checked(unknown + weight);
            }
            var denominator = checked(yes + no + unknown);
            if (denominator > MaximumWireCount) return Unavailable("count-overflow");
            if (denominator == 0) return new(0, 0, 0, 0, null, CoveragePrecision.Unavailable, ["empty-set"]);
            return new(yes, denominator, no, unknown, (double)((decimal)yes * 100 / denominator),
                unknown == 0 ? CoveragePrecision.Exact : CoveragePrecision.LowerBound, []);
        }
        catch (OverflowException) { return Unavailable("count-overflow"); }
    }

    /// <summary>Measures weighted frozen token memberships, including unmeasured positions.</summary>
    public static Coverage Tokens(IEnumerable<ReviewCaseDelta> cases, bool before)
    {
        var observations = cases.SelectMany(item => item.Memberships.Where(member => member.Role == "sampled-tokens")
            .Select(member => (Side: before ? item.Before : item.After, member.Weight))).ToArray();
        return WithPartial(Count(observations.Select(item => (Observed(item.Side), item.Weight))),
            observations.Any(item => item.Side.Completion != Completion.Complete));
    }

    /// <summary>Measures each frozen cell once; declared spelling variants are alternatives.</summary>
    public static Coverage Cells(IEnumerable<ReviewCaseDelta> cases, bool before)
    {
        var variants = cases.SelectMany(item => item.Memberships
            .Where(member => member.Role == "paradigm-cells" && member.CellId is not null)
            .Select(member => (member.SetId, Cell: member.CellId!, member.ExpectationIds,
                Side: before ? item.Before : item.After)));
        var captured = variants.ToArray();
        var coverage = Count(captured.GroupBy(item => (item.SetId, item.Cell)).Select(group =>
        {
            var values = group.Select(variant => Variant(variant.ExpectationIds, variant.Side)).ToArray();
            bool? covered = values.Any(value => value == true) ? true : values.All(value => value == false) ? false : null;
            return (covered, 1L);
        }));
        return WithPartial(coverage, captured.Any(item => item.Side.Completion != Completion.Complete));
    }

    internal static bool? Observed(ReviewCaseSideFacts side) => side.Parsed == true ? true :
        side.Completion == Completion.Complete && side.Parsed == false ? false : null;

    internal static Coverage WithPartial(Coverage coverage, bool partial) =>
        partial && coverage.Precision == CoveragePrecision.Exact
            ? coverage with { Precision = CoveragePrecision.LowerBound, ReasonCodes = ["partial-evidence"] } : coverage;

    private static bool? Variant(IReadOnlyList<string> ids, ReviewCaseSideFacts side)
    {
        if (ids.Count == 0) return null;
        if (ids.Any(id => side.MatchedExpectationIds.Contains(id))) return true;
        if (side.Completion == Completion.Complete && ids.All(id => side.MissingExpectationIds.Contains(id))) return false;
        return null;
    }

    /// <summary>Returns a percentage-point change only for exact, identical denominators.</summary>
    public static double? DeltaPoints(Coverage before, Coverage after, bool sameMembershipAndExpectations) =>
        sameMembershipAndExpectations && before.Precision == CoveragePrecision.Exact && after.Precision == CoveragePrecision.Exact &&
        before.Denominator == after.Denominator && before.Percent.HasValue && after.Percent.HasValue
            ? after.Percent.Value - before.Percent.Value : null;

    private static Coverage Unavailable(string reason) => new(null, null, null, null, null,
        CoveragePrecision.Unavailable, [reason]);
}

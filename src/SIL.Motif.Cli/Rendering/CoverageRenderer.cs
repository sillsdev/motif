using System.Globalization;
using System.Text;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Cli.Rendering;

internal static class CoverageRenderer
{
    private const int MechanismsShown = 10;

    public static string Render(CoverageResponse response)
    {
        var text = new StringBuilder();
        var done = response.Done;
        text.AppendLine($"Done: {done.Verdict} (accounted {Describe(done.Accounted)}; correct {Describe(done.Correct)}; stable {Describe(done.Stable)}).");
        text.AppendLine($"Grammar revision {response.Stamp.GrammarRevision}; tokenizer {response.Stamp.TokenizerId}.");
        text.AppendLine();
        text.AppendLine("State           current types   stale types   current tokens   stale tokens");
        foreach (var (name, split) in States(response.ByState))
            text.AppendLine($"{name,-15} {N(split.Current.Types),13}   {N(split.Stale.Types),11}   {N(split.Current.Tokens),14}   {N(split.Stale.Tokens),12}");
        text.AppendLine();
        var open = response.Open;
        text.AppendLine($"Open: {N(open.Recurring.Types)} recurring types ({N(open.Recurring.Tokens)} tokens), {N(open.Singletons.Types)} singleton types.");
        if (open.ByMechanism.Count > 0)
        {
            text.AppendLine("Open by mechanism:");
            foreach (var (mechanism, count) in open.ByMechanism.OrderByDescending(item => item.Value.Types)
                         .ThenBy(item => item.Key, StringComparer.Ordinal).Take(MechanismsShown))
                text.AppendLine($"  {mechanism}  {N(count.Types)} ({N(count.Current)} current, {N(count.Stale)} stale)");
            if (open.MechanismTotal > MechanismsShown) text.AppendLine($"  and {open.MechanismTotal - MechanismsShown} more; see --json.");
        }
        if (open.Ranked.Count > 0)
        {
            text.AppendLine($"Top recurring Open types ({open.Ranked.Count} of {N(open.RankedTotal)}):");
            foreach (var item in open.Ranked)
                text.AppendLine($"  {item.Word}  {N(item.Weight)}  {item.Mechanism}" + (item.Stale ? "  stale" : ""));
        }
        return text.ToString();
    }

    private static string Describe(CoverageDoneTest test) =>
        test.Verdict + (test.Reasons.Count == 0 ? "" : " [" + string.Join(", ", test.Reasons) + "]");

    private static IEnumerable<(string Name, CoverageCurrentStale Split)> States(CoverageByState state) =>
    [
        ("expected", state.Expected), ("unverified", state.Unverified), ("disposition", state.Disposition),
        ("open", state.Open), ("lost", state.Lost),
    ];

    private static string N(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
}

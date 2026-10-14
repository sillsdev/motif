using Xunit;

namespace SIL.Motif.Tests.App;

internal sealed record LayoutException(
    LayoutFindingContext Context, string Check, ControlDescription Control,
    ControlDescription? RelatedControl, string Owner, string Reason, string Identity, int Count = 1)
{
    internal bool Matches(LayoutFinding finding) =>
        Context == new LayoutFindingContext(finding.Scene, finding.Size, finding.Zoom, finding.Theme) &&
        Check == finding.Check && Control == finding.Control && RelatedControl == finding.RelatedControl &&
        Identity == finding.Identity && Count == finding.Count;
}

internal static partial class LayoutInvariantWorkList
{
    // Each tier inspects its own cells, so an entry is checked for staleness only by the tier that names it.
    internal static IReadOnlyList<LayoutException> For(string tier) => tier == "quick" ? QuickEntries : Entries;

    internal static void AssertNoUnexpected(
        IReadOnlyList<LayoutFinding> findings, IReadOnlyList<LayoutException> exceptions,
        IReadOnlySet<LayoutFindingContext> inspected)
    {
        var errors = new List<string>();
        foreach (var entry in exceptions)
        {
            if (string.IsNullOrWhiteSpace(entry.Owner) || string.IsNullOrWhiteSpace(entry.Reason))
                errors.Add($"Exception {entry.Context}/{entry.Check} needs an owner and reason.");
            if (!inspected.Contains(entry.Context))
                errors.Add($"Exception {entry.Context}/{entry.Check} refers to an uninspected state.");
            else if (!findings.Any(entry.Matches))
                errors.Add($"Stale exception {entry.Context}/{entry.Check}/{entry.Control} did not recur.");
        }
        foreach (var finding in findings)
        {
            var matches = exceptions.Count(entry => entry.Matches(finding));
            if (matches != 1)
                errors.Add($"{finding.Scene}/{finding.Size}/{finding.Zoom}/{finding.Theme}: " +
                    $"{finding.Check} [{finding.Control}] ({matches} exceptions): {finding.Detail}");
        }
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors.Take(40)) +
            $"{Environment.NewLine}{errors.Count} errors; full findings are in the layout-invariants JSON artifact.");
    }
}

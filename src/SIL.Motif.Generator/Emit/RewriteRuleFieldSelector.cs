using SIL.Motif.Generator.Join;
using SIL.Motif.Generator.Model;

namespace SIL.Motif.Generator.Emit;

/// <summary>Selects the closed direction enum field of a segment rewrite rule.</summary>
public static class RewriteRuleFieldSelector
{
    private static readonly FieldKey Direction = new("PhSegmentRule", "Direction");

    /// <summary>Selects the reachable direction enum only when it has a known, closed value set.</summary>
    /// <param name="rows">The joined LibLCM model and authoring manifest.</param>
    /// <returns>The selected direction row, or an empty list when its shape no longer matches.</returns>
    public static IReadOnlyList<JoinedRow> SelectDirection(IReadOnlyList<JoinedRow> rows) =>
        rows.Where(row =>
                row.Key == Direction &&
                row.Manifest.Scope == "in" &&
                row.Manifest.Group == "grammar" &&
                row.Manifest.Classification == "semantic-operation" &&
                row.Manifest.HcReachable == "yes" &&
                row.Kind == FieldKind.Basic &&
                row.Sig == "Integer" &&
                row.Manifest.Verbs == "set|clear" &&
                !string.IsNullOrWhiteSpace(row.Manifest.EnumValues) &&
                row.Manifest.EnumValues != "unknown")
            .ToList();
}

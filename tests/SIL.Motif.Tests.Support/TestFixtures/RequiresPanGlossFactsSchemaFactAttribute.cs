using Xunit;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>
/// Skips at discovery unless <c>MOTIF_PANGLOSS_FACTS_SCHEMA</c> names PanGloss's <c>pg-facts</c> schema file.
/// </summary>
/// <remarks>
/// The fake's schema is compared to PanGloss's DDL, which lives in another repository, so a checkout
/// supplies its path. CI that has no PanGloss checkout reports this test as skipped, not passed.
/// </remarks>
public sealed class RequiresPanGlossFactsSchemaFactAttribute : FactAttribute
{
    public const string Variable = "MOTIF_PANGLOSS_FACTS_SCHEMA";

    public RequiresPanGlossFactsSchemaFactAttribute()
    {
        var path = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            Skip = $"Set {Variable} to PanGloss's rust/crates/pg-facts/src/schema.sql to compare the fake schema.";
    }
}

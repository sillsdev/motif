using System.Diagnostics;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.Worker;

/// <summary>
/// Times the redundancy measures over a real project's grammar-facts file when MOTIF_REAL_FACTS names one; a no-op
/// otherwise. Load (the session's reads) and decide (grouping and findings) are timed apart, the first read being
/// the cold one.
/// </summary>
[Collection(LcmCacheTestCollection.Name)]
public sealed class RedundancyMeasureRealFactsTimingTests(PristineProjectFixture pristine, ITestOutputHelper output) : IDisposable
{
    private readonly ParsimonyFactsSessionHarness _harness = new(pristine);

    public void Dispose() => _harness.Dispose();

    // The harness facts keep their own metadata, which the session checks; only the rows the measures read come across.
    private static void CopyTables(string real, string path)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(path), Pooling = false, ForeignKeys = false,
        }.ToString());
        connection.Open();
        ParsimonyFactsSessionHarness.Insert(connection, "ATTACH DATABASE $p0 AS real;", Path.GetFullPath(real));
        foreach (var table in new[]
                 {
                     "rewrite_rhs_rule_feature", "rewrite_rhs_pos", "rewrite_rhs", "phonological_rule", "msa_exception_feature",
                     "msa_category", "sense_text", "sense", "msa", "entry_variant_type", "entry_variant",
                     "allomorph_environment", "allomorph_gate", "allomorph_form", "allomorph", "entry_citation_form",
                     "lex_entry", "exception_feature", "category", "writing_system",
                 })
            ParsimonyFactsSessionHarness.Insert(connection, $"DELETE FROM main.{table};");
        foreach (var table in new[]
                 {
                     "writing_system", "category", "exception_feature", "lex_entry", "allomorph", "allomorph_form",
                     "allomorph_environment", "entry_variant", "entry_variant_type", "msa", "sense", "msa_category",
                     "msa_exception_feature", "phonological_rule", "rewrite_rhs", "rewrite_rhs_pos", "rewrite_rhs_rule_feature",
                 })
            ParsimonyFactsSessionHarness.Insert(connection, $"INSERT INTO main.{table} SELECT * FROM real.{table};");
        ParsimonyFactsSessionHarness.Insert(connection, "UPDATE main.artifact_section SET status='complete', reason_code=NULL;");
    }

    [Fact]
    public async Task RealFactsTimings()
    {
        var real = Environment.GetEnvironmentVariable("MOTIF_REAL_FACTS");
        if (string.IsNullOrEmpty(real) || !File.Exists(real)) return;
        var lines = await _harness.RunAsync(path => CopyTables(real, path), (session, bundle) =>
        {
            var report = new List<string>();
            var context = session.ReadMeasureContext(bundle);
            string Time<T>(string name, Func<T> load, Func<T, ParsimonyMeasureResult> decide)
            {
                var timer = Stopwatch.StartNew();
                var input = load();
                var loadMs = timer.Elapsed.TotalMilliseconds;
                timer.Restart();
                var result = decide(input);
                var decideMs = timer.Elapsed.TotalMilliseconds;
                return $"{name}: load {loadMs:0.0} ms, measure {decideMs:0.0} ms, eligible {result.Run.EligibleItems}, findings {result.Run.FindingItems} ({result.Findings.Count} groups)";
            }
            report.Add(Time("P-lexeme-near-duplicate", session.ReadLexemeForms,
                input => MeasureRunner.DecideNearDuplicateLexemes(MeasureCatalog.Find("P-lexeme-near-duplicate")!, input, context)));
            report.Add(Time("P-variant-same-shape", session.ReadVariantShapes,
                input => MeasureRunner.DecideVariantSameShape(MeasureCatalog.Find("P-variant-same-shape")!, input, context)));
            report.Add(Time("P-tag-polarity-majority", session.ReadTagPolarity,
                input => MeasureRunner.DecideTagPolarity(MeasureCatalog.Find("P-tag-polarity-majority")!, input, context)));
            return report;
        });
        foreach (var line in lines) output.WriteLine(line);
    }
}

using System.Diagnostics;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;
using static SIL.Motif.Tests.Worker.ParsimonyFactsSessionHarness;

namespace SIL.Motif.Tests.Worker;

/// <summary>The three executable redundancy measures over real schema-v9 facts files, and each at CEB scale.</summary>
[Collection(LcmCacheTestCollection.Name)]
public sealed class RedundancyMeasureTests(PristineProjectFixture pristine, ITestOutputHelper output) : IDisposable
{
    private static readonly string[] Sections =
        ["allomorphs", "categories", "entries", "environments", "features", "msas", "phonology"];
    private readonly ParsimonyFactsSessionHarness _harness = new(pristine);

    public void Dispose() => _harness.Dispose();

    private static string G(char kind, int number) => $"{kind}0000000-0000-0000-0000-{number:D12}";

    private Task<ParsimonyMeasureResult> Run(string measure, Action<SqliteConnection> seed) => _harness.RunAsync(
        path => EditFacts(path, Sections, seed), (session, bundle) => MeasureRunner.Execute(measure, session, bundle));

    private static void Lexeme(SqliteConnection c, int number, string form, string morphType = "stem") =>
        WithForm(c, G('e', number), number, form, morphType);

    private static void WithForm(SqliteConnection c, string entry, int number, string form, string morphType = "stem")
    {
        WritingSystem(c);
        Entry(c, entry, number, morphType);
        Allomorph(c, G('a', number), entry, 0, morphType, form);
    }

    [Fact]
    public async Task NearDuplicateLexemesFlagFormsThatDifferOnlyInCaseOrIyAndNotExactRepeats()
    {
        var result = await Run("P-lexeme-near-duplicate", c =>
        {
            Lexeme(c, 1, "Diyos"); Lexeme(c, 2, "dyos");
            Lexeme(c, 3, "kata"); Lexeme(c, 4, "kata");
            Lexeme(c, 5, "bag"); Lexeme(c, 6, "baga");
        });

        Assert.Equal(ParsimonyMeasureStatus.Computed, result.Run.Status);
        Assert.Equal(6, result.Run.EligibleItems);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(ParsimonyGroupKind.NearDuplicateLexemes, finding.AttachesTo.GroupKind);
        Assert.True(ParsimonyFindingIdentity.GroupIdentityIsConsistent(finding));
        Assert.Contains(G('e', 1), finding.Limitations[0]);
        Assert.Contains(G('e', 2), finding.Limitations[0]);
        Assert.Equal(2, finding.EvidenceRefs.Count);
    }

    private static void Variants(SqliteConnection c, int count, int firstNumber, string type, string environment,
        Func<int, (string Main, string Variant)> forms)
    {
        for (var i = 0; i < count; i++)
        {
            var n = firstNumber + i;
            var (main, variant) = forms(i);
            Lexeme(c, n, main);
            var variantEntry = G('e', n + 5000);
            WithForm(c, variantEntry, n + 5000, variant);
            Insert(c, "INSERT OR IGNORE INTO allomorph_environment(allomorph_guid, role, ordinal, environment_guid) " +
                "VALUES ($p0, 'phone', 0, $p1);", G('a', n + 5000), environment);
            Insert(c, "INSERT INTO entry_variant(variant_entry_guid, ref_guid, ordinal, component_guid, component_kind) " +
                "VALUES ($p0, $p1, 0, $p2, 'entry');", variantEntry, G('r', n), G('e', n));
            Insert(c, "INSERT INTO entry_variant_type(ref_guid, ordinal, type_guid, is_infl_type) VALUES ($p0, 0, $p1, 0);",
                G('r', n), type);
        }
    }

    [Fact]
    public async Task VariantSameShapeFlagsFiveOrMoreOfOneShapeAndLeavesFourAlone()
    {
        var result = await Run("P-variant-same-shape", c =>
        {
            Variants(c, 6, 100, G('t', 1), G('v', 1), i => ("kata" + (char)('a' + i) + "a", "kata" + (char)('a' + i)));
            Variants(c, 4, 200, G('t', 1), G('v', 2), i => ("bago" + (char)('a' + i), "bago" + (char)('a' + i) + "n"));
        });

        Assert.Equal(ParsimonyMeasureStatus.Computed, result.Run.Status);
        Assert.Equal(10, result.Run.EligibleItems);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(ParsimonyGroupKind.VariantSameShape, finding.AttachesTo.GroupKind);
        Assert.True(ParsimonyFindingIdentity.GroupIdentityIsConsistent(finding));
        Assert.Equal(5, result.Run.FindingItems);
        Assert.Contains("6 variants share the shape", finding.Limitations[0]);
    }

    private static void Stems(SqliteConnection c, string category, int count, int carriers, string tag, int first)
    {
        for (var i = 0; i < count; i++)
        {
            var n = first + i;
            var entry = G('e', n);
            Entry(c, entry, n);
            Msa(c, G('m', n), entry, "stem");
            Insert(c, "INSERT INTO msa_category(msa_guid, role, ordinal, category_guid) VALUES ($p0, 'pos', 0, $p1);",
                G('m', n), category);
            if (i < carriers) MsaFeature(c, G('m', n), tag);
        }
    }

    private static void Scoped(SqliteConnection c, string rule, string tag, string category)
    {
        Rule(c, rule, "rule-" + rule[^3..], tag);
        Insert(c, "INSERT INTO rewrite_rhs_pos(rule_guid, rhs_ordinal, ordinal, category_guid) VALUES ($p0, 0, 0, $p1);",
            rule, category);
    }

    [Fact]
    public async Task TagPolarityFlagsATagMostHostsCarryAndNotMinorityOrTinyScopes()
    {
        var tag = G('f', 1);
        var result = await Run("P-tag-polarity-majority", c =>
        {
            Feature(c, tag, "ka");
            foreach (var (n, name) in new[] { (1, "N"), (2, "V"), (3, "ADJ") })
                Insert(c, "INSERT INTO category(guid, sibling_ordinal, name, abbreviation) VALUES ($p0, 0, $p1, $p1);",
                    G('c', n), name);
            Stems(c, G('c', 1), 10, 6, tag, 1000);
            Stems(c, G('c', 2), 10, 1, tag, 2000);
            Stems(c, G('c', 3), 3, 3, tag, 3000);
            Scoped(c, G('d', 1), tag, G('c', 1));
            Scoped(c, G('d', 2), tag, G('c', 2));
            Scoped(c, G('d', 3), tag, G('c', 3));
        });

        Assert.Equal(ParsimonyMeasureStatus.Computed, result.Run.Status);
        Assert.Equal(2, result.Run.EligibleItems);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(ParsimonyGroupKind.TagPolarity, finding.AttachesTo.GroupKind);
        Assert.True(ParsimonyFindingIdentity.GroupIdentityIsConsistent(finding));
        Assert.EndsWith(G('d', 1), finding.AttachesTo.Identity);
        Assert.Contains("6 of 10 stems", finding.Limitations[0]);
    }

    [Fact]
    public async Task TagImpliedByContextStaysNotAvailableUntilPanGlossSuppliesRuleContextMatches()
    {
        var result = await Run("P-tag-implied-by-context", _ => { });
        Assert.Equal(ParsimonyMeasureStatus.NotAvailable, result.Run.Status);
    }

    [Fact]
    public async Task RedundancyMeasuresAtCebScaleEachRunInUnderASecond()
    {
        var tag = G('f', 1);
        var session = await _harness.RunAsync(path => EditFacts(path, Sections, c =>
        {
            Feature(c, tag, "ka");
            for (var n = 1; n <= 10; n++)
                Insert(c, "INSERT INTO category(guid, sibling_ordinal, name, abbreviation) VALUES ($p0, 0, $p1, $p1);",
                    G('c', n), "c" + n);
            for (var i = 0; i < 8000; i++) Lexeme(c, 10000 + i, "form" + i + (i % 400 == 1 ? "iy" : ""));
            Lexeme(c, 90000, "FORM0");
            Variants(c, 40, 50000, G('t', 1), G('v', 1), i => ("kata" + i + "a", "kata" + i));
            Stems(c, G('c', 1), 1200, 700, tag, 200000);
            for (var r = 0; r < 60; r++) Scoped(c, G('d', r), tag, G('c', 1 + r % 10));
        }), (session, bundle) =>
        {
            var timings = new List<string>();
            foreach (var measure in new[] { "P-lexeme-near-duplicate", "P-variant-same-shape", "P-tag-polarity-majority" })
            {
                var timer = Stopwatch.StartNew();
                var result = MeasureRunner.Execute(measure, session, bundle);
                timer.Stop();
                timings.Add($"{measure}: {timer.ElapsedMilliseconds} ms, eligible {result.Run.EligibleItems}, findings {result.Run.FindingItems}");
                Assert.True(timer.ElapsedMilliseconds < 1000, $"{measure} took {timer.ElapsedMilliseconds} ms");
            }
            return timings;
        });
        foreach (var line in session) output.WriteLine(line);
    }
}

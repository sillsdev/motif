using System.Diagnostics;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;
using static SIL.Motif.Tests.Worker.ParsimonyFactsSessionHarness;

namespace SIL.Motif.Tests.Worker;

/// <summary>P-feature-few-carriers over real schema-v9 facts files and over plain inputs for edge cases.</summary>
[Collection(LcmCacheTestCollection.Name)]
public sealed class FeatureFewCarriersTests(PristineProjectFixture pristine, ITestOutputHelper output) : IDisposable
{
    private const string Measure = "P-feature-few-carriers";
    private static readonly string[] Sections =
        ["categories", "compound_rules", "entries", "features", "msas", "patterns", "phonology"];
    private readonly ParsimonyFactsSessionHarness _harness = new(pristine);

    public void Dispose() => _harness.Dispose();

    private static string Guid(int number) => $"f0000000-0000-0000-0000-{number:D12}";

    private static readonly string Single = Guid(1), None = Guid(2), Orphan = Guid(3), Three = Guid(4),
        Four = Guid(5), TwoNoUser = Guid(6), CompoundOnly = Guid(7);

    // Seeds the grammar. With `listed` the features are listed words; without it each one has four carriers.
    private static void Seed(SqliteConnection connection, bool listed)
    {
        Feature(connection, Single, "single-word"); Feature(connection, None, "nobody");
        Feature(connection, Orphan, "orphan"); Feature(connection, Three, "three-words");
        Feature(connection, Four, "productive"); Feature(connection, TwoNoUser, "two-no-user");
        var next = 100;
        void Carriers(string feature, int count)
        {
            for (var index = 0; index < count; index++, next++)
            {
                var entry = Guid(next);
                Entry(connection, entry, next);
                Msa(connection, Guid(next + 100000), entry, "stem");
                MsaFeature(connection, Guid(next + 100000), feature);
            }
        }
        Carriers(Single, listed ? 1 : 4); Carriers(Three, listed ? 3 : 4); Carriers(Four, 4);
        Carriers(TwoNoUser, 2);
        // A stem MSA that merely lists a feature as from_required is not a carrier.
        Entry(connection, Guid(50), 50); Msa(connection, Guid(50050), Guid(50), "stem");
        MsaFeature(connection, Guid(50050), None, role: "from_required");
        // Affix MSAs and rules use the features.
        var affix = 0;
        foreach (var feature in new[] { Single, None, Four })
        {
            var entry = Guid(200 + affix);
            Entry(connection, entry, 200 + affix, "suffix");
            Msa(connection, Guid(300000 + affix), entry, "inflectional");
            MsaFeature(connection, Guid(300000 + affix), feature);
            affix++;
        }
        Rule(connection, Guid(400), "ka-lowering", Single);
        Rule(connection, Guid(401), "nasal-assimilation", Three, "excluded");
        // A feature carried by two entries and required only by a compound rule is a finding too.
        Feature(connection, CompoundOnly, "compound-only");
        for (var index = 0; index < (listed ? 2 : 4); index++)
        {
            Entry(connection, Guid(600 + index), 600 + index);
            Msa(connection, Guid(600600 + index), Guid(600 + index), "stem");
            MsaFeature(connection, Guid(600600 + index), CompoundOnly);
        }
        Insert(connection, "INSERT INTO compound_rule(guid, kind, name, disabled) VALUES ($p0, 'endocentric', 'noun-noun', 0);",
            Guid(700));
        Insert(connection, "INSERT INTO compound_rule_exception_feature(rule_guid, side, ordinal, target_guid) " +
            "VALUES ($p0, 'left', 0, $p1);", Guid(700), CompoundOnly);
        // Slot count: two inflection classes (one a subclass) and one entry inflection type.
        Insert(connection, "INSERT INTO category(guid, sibling_ordinal, name, abbreviation) VALUES ($p0, 0, 'N', 'n');",
            Guid(500));
        Insert(connection, "INSERT INTO inflection_class(guid, owner_category_guid, sibling_ordinal, name, abbreviation) " +
            "VALUES ($p0, $p1, 0, 'c1', 'c1');", Guid(501), Guid(500));
        Insert(connection, "INSERT INTO inflection_class(guid, owner_category_guid, parent_guid, sibling_ordinal, name, " +
            "abbreviation) VALUES ($p0, $p1, $p2, 0, 'c2', 'c2');", Guid(502), Guid(500), Guid(501));
        Insert(connection, "INSERT INTO lex_entry_infl_type(guid, name, abbreviation) VALUES ($p0, 'past', 'pst');",
            Guid(503));
    }

    private Task<ParsimonyMeasureResult> RunSeeded(bool listed) => _harness.RunAsync(
        path => EditFacts(path, Sections, connection => Seed(connection, listed)),
        (session, bundle) => MeasureRunner.Execute(Measure, session, bundle));

    [Fact]
    public async Task FeatureFewCarriersListedWordsAreFlaggedWithCarriersUsersAndTheSlotCount()
    {
        var result = await RunSeeded(listed: true);

        Assert.Equal(ParsimonyMeasureStatus.Computed, result.Run.Status);
        Assert.Equal(7, result.Run.EligibleItems);
        Assert.Equal(4, result.Run.FindingItems);
        var byFeature = result.Findings.ToDictionary(finding => finding.AttachesTo.Identity.Split('/').Last());
        Assert.Equal(new[] { Single, None, Three, CompoundOnly }.Order(), byFeature.Keys.Order());
        Assert.All(result.Findings, finding =>
        {
            Assert.Equal(ParsimonyGroupKind.ExceptionFeature, finding.AttachesTo.GroupKind);
            Assert.True(ParsimonyFindingIdentity.GroupIdentityIsConsistent(finding));
            Assert.Contains(finding.Limitations, line => line.Contains("PanGloss feature slots used: 10 of 64 (headroom 54)"));
            Assert.Contains(finding.Limitations, line => line.Contains("1 exception feature(s) have no carrier and no user"));
        });
        var single = byFeature[Single].Limitations;
        Assert.Contains(single, line => line.Contains("carried by 1 entry"));
        Assert.Contains(single, line => line.Contains("Required by 1 affix MSA(s)"));
        Assert.Contains(single, line => line.Contains("\"ka-lowering\" requires") &&
            line.Contains("a rule that applies to a single word"));
        Assert.Contains(byFeature[None].Limitations, line => line.Contains("carried by no entry"));
        Assert.Contains(byFeature[Three].Limitations, line => line.Contains("carried by 3 entries"));
        Assert.DoesNotContain(byFeature[Three].Limitations, line => line.Contains("single word"));
        Assert.Contains(byFeature[CompoundOnly].Limitations, line => line.Contains("Compound rule \"noun-noun\" requires") &&
            line.Contains("left constituent"));
        Assert.Contains("headroom 54", result.Run.Detail);
        Assert.Single(byFeature[Single].EvidenceRefs);
        Assert.Equal(ParsimonyViews.AffixContext, byFeature[Single].EvidenceRefs[0].View);
    }

    [Fact]
    public async Task FeatureFewCarriersCleanControlHasNoFindings()
    {
        var result = await RunSeeded(listed: false);

        Assert.Equal(ParsimonyMeasureStatus.Computed, result.Run.Status);
        Assert.Equal(7, result.Run.EligibleItems);
        // The control still holds the zero-carrier feature an affix requires, which is a finding by design.
        var none = Assert.Single(result.Findings);
        Assert.EndsWith(None, none.AttachesTo.Identity);
        Assert.DoesNotContain(result.Findings, finding => finding.AttachesTo.Identity.EndsWith(Single) ||
            finding.AttachesTo.Identity.EndsWith(Three) || finding.AttachesTo.Identity.EndsWith(Four));
    }

    [Fact]
    public async Task FeatureFewCarriersIsNotAvailableWithoutTheMsaFacts()
    {
        var result = await _harness.RunAsync(path => EditFacts(path, [], _ => { }),
            (session, bundle) => MeasureRunner.Execute(Measure, session, bundle));

        Assert.Equal(ParsimonyMeasureStatus.NotAvailable, result.Run.Status);
    }

    [Fact]
    public async Task FeatureFewCarriersRunsInUnderASecondAtCebScale()
    {
        var elapsed = await _harness.RunAsync(path => EditFacts(path, Sections, connection =>
        {
            const int entries = 25000, features = 60, heavy = 50;
            for (var feature = 0; feature < features; feature++) Feature(connection, Guid(feature + 1), "f" + feature);
            for (var entry = 0; entry < entries; entry++)
            {
                Entry(connection, Guid(1000 + entry), entry, entry % 4 == 0 ? "suffix" : "stem");
                // Stem entries carry a feature; most features gather many carriers, every fifth only a few.
                Msa(connection, Guid(1000000 + entry), Guid(1000 + entry), entry % 4 == 0 ? "inflectional" : "stem");
                if (entry % 8 < 6)
                    MsaFeature(connection, Guid(1000000 + entry), Guid(1 + (entry % 5 == 0 ? entry % 12 : entry % heavy)));
            }
            for (var extra = 0; extra < 8000; extra++)
            {
                Msa(connection, Guid(2000000 + extra), Guid(1000 + extra), "derivational");
                if (extra < 200) MsaFeature(connection, Guid(2000000 + extra), Guid(heavy + 1 + extra % 10));
            }
            for (var rule = 0; rule < 20; rule++) Rule(connection, Guid(3000000 + rule), "r" + rule, Guid(41 + rule));
        }), (session, bundle) =>
        {
            var timer = Stopwatch.StartNew();
            var result = MeasureRunner.Execute(Measure, session, bundle);
            timer.Stop();
            Assert.Equal(ParsimonyMeasureStatus.Computed, result.Run.Status);
            output.WriteLine($"{Measure}: {result.Findings.Count} findings in {timer.ElapsedMilliseconds} ms");
            return timer.ElapsedMilliseconds;
        });

        Assert.True(elapsed < 1000, $"{Measure} took {elapsed} ms.");
    }

    // ---- decisions over plain inputs

    private static MeasureContext Context() => MeasureContext.For("bundle",
        new MeasureDigestContext(1, 1, ParsimonyEvidenceScopeKind.ProjectApproved, null, null),
        new Dictionary<string, string> { ["entry-1"] = "pim" });

    private static ExceptionFeatureFact Fact(string guid, int carriers, int affixUsers = 0, int ruleUsers = 0,
        string polarity = "required", int compoundUsers = 0) => new(guid, "name-" + guid, "n", Enumerable.Range(0, carriers)
            .Select(index => "entry-" + index).ToArray(),
        Enumerable.Range(0, affixUsers).Select(index => new FeatureAffixUser("msa-" + index, "entry-s", "required")).ToArray(),
        Enumerable.Range(0, ruleUsers).Select(index => new FeatureRuleUser("rule-" + index, "rule " + index, polarity)).ToArray(),
        Enumerable.Range(0, compoundUsers).Select(index => new FeatureCompoundUser("compound-" + index, null, "left")).ToArray());

    private static ParsimonyMeasureResult Decide(long classes, params ExceptionFeatureFact[] facts) =>
        MeasureRunner.DecideFewCarrierFeatures(MeasureCatalog.Find(Measure)!,
            new ExceptionFeatureInput(facts, new FeatureSlotCount(classes, facts.Length, 0)), Context());

    [Theory]
    [InlineData(0, 1, 0, 0, true)]
    [InlineData(3, 1, 0, 0, true)]
    [InlineData(4, 1, 0, 0, false)]
    [InlineData(2, 0, 1, 0, true)]
    [InlineData(2, 0, 0, 1, true)]
    [InlineData(2, 0, 0, 0, false)]
    [InlineData(0, 0, 0, 0, false)]
    public void TheCarrierThresholdIsFourAndAFeatureMustBeUsed(int carriers, int affixUsers, int ruleUsers,
        int compoundUsers, bool flagged) =>
        Assert.Equal(flagged ? 1 : 0, Decide(0, Fact("f", carriers, affixUsers, ruleUsers, compoundUsers: compoundUsers))
            .Findings.Count);

    [Fact]
    public void AFeatureWithNoCarrierAndNoUserIsCountedInTheLimitationNotFlagged()
    {
        var result = Decide(0, Fact("a", 0), Fact("b", 1, affixUsers: 1));

        Assert.Single(result.Findings);
        Assert.Contains("1 exception feature(s) have no carrier and no user", result.Run.Detail);
    }

    [Fact]
    public void AnExcludingRuleWithOneCarrierIsNotCalledARuleThatAppliesToASingleWord()
    {
        var finding = Assert.Single(Decide(0, Fact("a", 1, ruleUsers: 1, polarity: "excluded")).Findings);

        Assert.DoesNotContain(finding.Limitations, line => line.Contains("applies to a single word"));
        Assert.Contains(finding.Limitations, line => line.Contains("every word except one"));
    }

    [Theory]
    [InlineData(63, 63, "headroom 1")]
    [InlineData(64, 64, "headroom 0")]
    [InlineData(71, 71, "over the cap by 7")]
    public void TheSlotLineStatesHeadroomAgainstTheSixtyFourCap(long classes, long total, string words)
    {
        var finding = Assert.Single(Decide(classes - 1, Fact("a", 1, affixUsers: 1)).Findings);

        Assert.Contains(finding.Limitations, line => line.Contains($"{total} of 64") && line.Contains(words));
    }

    [Fact]
    public void FindingIdsAreStableAndOrderedAndNamedByTheFeature()
    {
        var first = Decide(0, Fact("b", 1, 1), Fact("a", 2, 1)).Findings.Select(item => item.FindingId).ToArray();
        var again = Decide(5, Fact("a", 3, 2), Fact("b", 0, 1)).Findings.Select(item => item.FindingId).ToArray();

        Assert.Equal(first, again);
        Assert.Equal(first.Order(StringComparer.Ordinal), first);
    }
}

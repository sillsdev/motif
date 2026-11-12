using System.Diagnostics;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.Worker;

/// <summary>P-entry-duplicate over real facts files whose lexicon each test authors.</summary>
[Collection(LcmCacheTestCollection.Name)]
public sealed class EntryDuplicateTests(PristineProjectFixture pristine, ITestOutputHelper output) : IDisposable
{
    private const string Noun = "c0000000-0000-0000-0000-000000000001";
    private const string Verb = "c0000000-0000-0000-0000-000000000002";
    private readonly LexiconBundleHarness _harness = new(pristine);

    public void Dispose() => _harness.Dispose();

    // With broken false the same grammar has every defect corrected, so the control differs only by the defects.
    private static void Grammar(LexiconSeed seed, bool broken)
    {
        // A reduplication entered twice: no form text, so only class, gloss and MSA tell them apart.
        seed.ProcessEntry("p1", "suffix").Msa("p1-m", "p1", "derivational", ["from_pos:" + Noun, "to_pos:" + Verb])
            .Sense("p1-s", "p1", "REDUP")
            .ProcessEntry("p2", "suffix").Msa("p2-m", "p2", "derivational", ["from_pos:" + Noun, "to_pos:" + Verb])
            .Sense("p2-s", "p2", broken ? "REDUP" : "ITER");
        // A hyphen twin: the same prefix typed with and without the trailing hyphen.
        seed.Entry("h1", "prefix", "mag").Msa("h1-m", "h1", "inflectional", [Verb], ["slot-1"]).Sense("h1-s", "h1", "AV")
            .Entry("h2", "prefix", "mag-").Msa("h2-m", "h2", "inflectional", [Verb], broken ? ["slot-1"] : ["slot-2"])
            .Sense("h2-s", "h2", "AV");
        // A clone whose gloss is a homograph's gloss plus a dotted suffix.
        seed.Entry("k1", "suffix", "-ka").Msa("k1-m", "k1", "inflectional", [Verb], ["slot-3"]).Sense("k1-s", "k1", "NMLZ")
            .Entry("k2", "suffix", "-ka").Msa("k2-m", "k2", "inflectional", [Verb], ["slot-4"])
            .Sense("k2-s", "k2", broken ? "NMLZ.V" : "FOCUS");
        // A clone whose gloss is the capitalised form plus a dotted suffix.
        seed.Entry("d1", "stem", "dul").Msa("d1-m", "d1", "stem", [Verb]).Sense("d1-s", "d1", "run")
            .Entry("d2", "stem", "dul").Msa("d2-m", "d2", "stem", [Noun]).Sense("d2-s", "d2", broken ? "DUL.PANGA" : "course");
        // Two stem entries with the same form, gloss and category.
        seed.Entry("t1", "stem", "pim").Msa("t1-m", "t1", "stem", [Noun]).Sense("t1-s", "t1", "tree")
            .Entry("t2", "stem", "pim").Msa("t2-m", "t2", "stem", [broken ? Noun : Verb]).Sense("t2-s", "t2", "tree");
        // Always correct: unrelated homograph affixes, a stem and a prefix of one form, and pure variant entries.
        seed.Entry("a1", "suffix", "-a").Msa("a1-m", "a1", "inflectional", [Noun], ["slot-5"]).Sense("a1-s", "a1", "PL")
            .Entry("a2", "suffix", "-a").Msa("a2-m", "a2", "derivational", ["from_pos:" + Verb, "to_pos:" + Noun])
            .Sense("a2-s", "a2", "AGENT")
            .Entry("s1", "stem", "bag").Msa("s1-m", "s1", "stem", [Noun]).Sense("s1-s", "s1", "bag")
            .Entry("s2", "prefix", "bag-").Msa("s2-m", "s2", "inflectional", [Noun], ["slot-6"]).Sense("s2-s", "s2", "bag")
            .Entry("v1", "stem", "var").Entry("v2", "stem", "var");
    }

    [Fact]
    public async Task EntryDuplicateBadFlagsEachDuplicateAndCloneGroup()
    {
        using var session = await _harness.OpenAsync(seed => Grammar(seed, broken: true));

        var result = MeasureRunner.Execute("P-entry-duplicate", session, session.BundleId);

        Assert.Equal(ParsimonyMeasureStatus.Computed, result.Run.Status);
        Assert.Equal(16, result.Run.EligibleItems);
        Assert.Equal(5, result.Run.FindingItems);
        var groups = result.Findings.Select(finding => (Kind: finding.AttachesTo.Identity.Split('/')[1],
            Members: finding.Limitations[0])).ToArray();
        Assert.Equal(3, groups.Count(group => group.Kind == "duplicate"));
        Assert.Equal(2, groups.Count(group => group.Kind == "clone"));
        AssertGroup(result, "duplicate", "p1", "p2");
        AssertGroup(result, "duplicate", "h1", "h2");
        AssertGroup(result, "duplicate", "t1", "t2");
        AssertGroup(result, "clone", "k1", "k2");
        AssertGroup(result, "clone", "d1", "d2");
        Assert.All(result.Findings, finding =>
        {
            Assert.Equal(ParsimonyGroupKind.DuplicateEntries, finding.AttachesTo.GroupKind);
            Assert.True(ParsimonyFindingIdentity.GroupIdentityIsConsistent(finding));
            Assert.Equal(new ParsimonyMeasureNumber(1, 16, "lexical entries"), finding.Number);
            Assert.Equal(2, finding.EvidenceRefs.Count);
        });
    }

    private static void AssertGroup(ParsimonyMeasureResult result, string kind, params string[] members)
    {
        var finding = Assert.Single(result.Findings, item => item.AttachesTo.Identity.StartsWith(
            $"entries/{kind}/{members[0]}/", StringComparison.Ordinal));
        foreach (var member in members)
            Assert.True(finding.Limitations[0].Contains(", " + member + ")") || finding.Limitations[0].Contains(member + " ("),
                finding.Limitations[0]);
    }

    [Fact]
    public async Task EntryDuplicateCleanControlHasNoFindingsOverTheSameDenominator()
    {
        using var session = await _harness.OpenAsync(seed => Grammar(seed, broken: false));

        var result = MeasureRunner.Execute("P-entry-duplicate", session, session.BundleId);

        Assert.Equal(ParsimonyMeasureStatus.Computed, result.Run.Status);
        Assert.Equal(16, result.Run.EligibleItems);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task EntryDuplicateAtCebScaleRunsInUnderASecond()
    {
        using var session = await _harness.OpenAsync(seed =>
        {
            for (var i = 0; i < 25000; i++)
            {
                var entry = "e" + i;
                var homograph = i % 500 == 1 ? i - 1 : i;
                seed.Entry(entry, i % 3 == 0 ? "suffix" : "stem", "f" + homograph);
                if (i % 7 == 0) seed.Alternate(entry, "alt" + i);
                seed.Msa("m" + i, entry, i % 3 == 0 ? "inflectional" : "stem", ["c" + (i % 40)]);
                seed.Sense("s" + i, entry, i % 500 == 1 ? "g" + homograph : "g" + i);
                seed.Sense("t" + i, entry, "h" + i);
            }
        });

        var timer = Stopwatch.StartNew();
        var result = MeasureRunner.Execute("P-entry-duplicate", session, session.BundleId);
        timer.Stop();

        output.WriteLine($"P-entry-duplicate over {result.Run.EligibleItems} entries: " +
            $"{timer.ElapsedMilliseconds} ms, {result.Run.FindingItems} redundant entries");
        Assert.True(timer.ElapsedMilliseconds < 1000, $"took {timer.ElapsedMilliseconds} ms");
    }
}

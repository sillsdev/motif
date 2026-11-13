using System.Diagnostics;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.Worker;

/// <summary>P-msa-no-category over real facts files whose lexicon each test authors.</summary>
[Collection(LcmCacheTestCollection.Name)]
public sealed class MsaNoCategoryTests(PristineProjectFixture pristine, ITestOutputHelper output) : IDisposable
{
    private const string Pos = "c0000000-0000-0000-0000-000000000001";
    private readonly LexiconBundleHarness _harness = new(pristine);

    public void Dispose() => _harness.Dispose();

    // The one defect: a stem MSA with no part of speech.
    private static void MsaNoCategoryBad(LexiconSeed seed) => seed
        .Entry("e-glossed", "stem", "xa").Msa("m-glossed", "e-glossed", "stem").Sense("s-glossed", "e-glossed", "go")
        .Entry("e-stub", "stem", "pim").Msa("m-stub", "e-stub", "stem")
        .Entry("e-stub-blank", "stem", "pom").Msa("m-stub-blank", "e-stub-blank", "stem")
            .Sense("s-blank", "e-stub-blank", "  ")
        .Entry("e-fine", "stem", "xo").Msa("m-fine", "e-fine", "stem", [Pos]).Sense("s-fine", "e-fine", "go")
        .Entry("e-affix", "suffix", "-ka").Msa("m-affix", "e-affix", "inflectional").Sense("s-affix", "e-affix", "PL");

    private static void MsaNoCategoryCleanControl(LexiconSeed seed) => seed
        .Entry("e-glossed", "stem", "xa").Msa("m-glossed", "e-glossed", "stem", [Pos]).Sense("s-glossed", "e-glossed", "go")
        .Entry("e-stub", "stem", "pim").Msa("m-stub", "e-stub", "stem", [Pos])
        .Entry("e-stub-blank", "stem", "pom").Msa("m-stub-blank", "e-stub-blank", "stem", [Pos])
            .Sense("s-blank", "e-stub-blank", "  ")
        .Entry("e-fine", "stem", "xo").Msa("m-fine", "e-fine", "stem", [Pos]).Sense("s-fine", "e-fine", "go")
        .Entry("e-affix", "suffix", "-ka").Msa("m-affix", "e-affix", "inflectional").Sense("s-affix", "e-affix", "PL");

    [Fact]
    public async Task MsaNoCategoryBadFlagsEachStemMsaWithoutAPartOfSpeech()
    {
        using var session = await _harness.OpenAsync(MsaNoCategoryBad);

        var result = MeasureRunner.Execute("P-msa-no-category", session, session.BundleId);

        Assert.Equal(ParsimonyMeasureStatus.Computed, result.Run.Status);
        Assert.Equal(4, result.Run.EligibleItems);
        Assert.Equal(3, result.Run.FindingItems);
        Assert.Equal(["m-glossed", "m-stub", "m-stub-blank"],
            result.Findings.Select(finding => finding.AttachesTo.Identity.Split('/')[^1]).Order(StringComparer.Ordinal));
        Assert.All(result.Findings, finding =>
        {
            Assert.Equal(ParsimonyGroupKind.MsaWithoutCategory, finding.AttachesTo.GroupKind);
            Assert.True(ParsimonyFindingIdentity.GroupIdentityIsConsistent(finding));
            Assert.Equal(new ParsimonyMeasureNumber(1, 4, "stem MSAs"), finding.Number);
        });
        var glossed = Assert.Single(result.Findings, finding => finding.AttachesTo.Identity.EndsWith("/m-glossed"));
        Assert.Contains("\"xa\"", glossed.Limitations[0]);
        Assert.Contains("probably missing", glossed.Limitations[0]);
        var stub = Assert.Single(result.Findings, finding => finding.AttachesTo.Identity.EndsWith("/m-stub"));
        Assert.Contains("stub", stub.Limitations[0]);
        var blank = Assert.Single(result.Findings, finding => finding.AttachesTo.Identity.EndsWith("/m-stub-blank"));
        Assert.Contains("stub", blank.Limitations[0]);
    }

    [Fact]
    public async Task MsaNoCategoryCleanControlHasNoFindingsOverTheSameDenominator()
    {
        using var session = await _harness.OpenAsync(MsaNoCategoryCleanControl);

        var result = MeasureRunner.Execute("P-msa-no-category", session, session.BundleId);

        Assert.Equal(ParsimonyMeasureStatus.Computed, result.Run.Status);
        Assert.Equal(4, result.Run.EligibleItems);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task AStemMsaWithOnlyAFromCategoryStillHasNoPartOfSpeech()
    {
        using var session = await _harness.OpenAsync(seed => seed
            .Entry("e-1", "stem", "xa").Msa("m-1", "e-1", "stem", ["from_pos:" + Pos]));

        var result = MeasureRunner.Execute("P-msa-no-category", session, session.BundleId);

        Assert.Single(result.Findings);
    }

    [Fact]
    public async Task MsaNoCategoryAtCebScaleRunsInUnderASecond()
    {
        using var session = await _harness.OpenAsync(seed =>
        {
            for (var i = 0; i < 25000; i++)
            {
                var entry = "e" + i;
                var affix = i % 3 == 0;
                seed.Entry(entry, affix ? "suffix" : "stem", (affix ? "-" : "") + "f" + i);
                // 33,000 MSAs over 25,000 entries; about 17,000 are stem MSAs and 124 of those lack a category.
                seed.Msa("m" + i, entry, affix ? "inflectional" : "stem",
                    !affix && i % 100 == 1 && i < 18750 ? [] : [Pos]);
                if (i % 3 == 1) seed.Msa("n" + i, entry, "stem", [Pos]);
                seed.Sense("s" + i, entry, "g" + i);
                seed.Sense("t" + i, entry, i % 2 == 0 ? "h" + i : "k" + i);
            }
        });

        var timer = Stopwatch.StartNew();
        var result = MeasureRunner.Execute("P-msa-no-category", session, session.BundleId);
        timer.Stop();

        output.WriteLine($"P-msa-no-category at {result.Run.EligibleItems} stem MSAs: {timer.ElapsedMilliseconds} ms, " +
            $"{result.Run.FindingItems} findings");
        Assert.True(result.Run.FindingItems > 100);
        Assert.True(timer.ElapsedMilliseconds < 1000, $"took {timer.ElapsedMilliseconds} ms");
    }
}

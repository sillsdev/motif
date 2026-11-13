using System.Diagnostics;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.Worker;

/// <summary>P-gloss-echoes-form over real facts files whose lexicon each test authors.</summary>
[Collection(LcmCacheTestCollection.Name)]
public sealed class GlossEchoesFormTests(PristineProjectFixture pristine, ITestOutputHelper output) : IDisposable
{
    private readonly LexiconBundleHarness _harness = new(pristine);

    public void Dispose() => _harness.Dispose();

    // The one defect: a gloss that is its entry's form in capitals. A proper name or a loan echo is fine.
    private static void GlossEchoesFormBad(LexiconSeed seed) => seed
        .Entry("e-echo", "prefix", "xyz-").Sense("s-echo", "e-echo", "XYZ")
        .Entry("e-dotted", "suffix", "-abc").Sense("s-dotted", "e-dotted", "ABC.V")
        .Entry("e-alt", "stem", "pqr").Alternate("e-alt", "pq").Sense("s-alt", "e-alt", "PQ")
        .Entry("e-clitic", "enclitic", "=qrs").Sense("s-clitic", "e-clitic", "QRS-")
        .Entry("e-nonascii", "stem", "ñu").Sense("s-nonascii", "e-nonascii", "ÑU")
        .ProcessEntry("e-process", "suffix").Sense("s-process", "e-process", "XYZ")
        .Entry("e-fine", "stem", "xo").Sense("s-fine", "e-fine", "go")
        .Entry("e-name", "stem", "Name").Sense("s-name", "e-name", "Name")
        .Entry("e-loan", "stem", "radar").Sense("s-loan", "e-loan", "radar");

    private static void GlossEchoesFormCleanControl(LexiconSeed seed) => seed
        .Entry("e-echo", "prefix", "xyz-").Sense("s-echo", "e-echo", "PLURAL")
        .Entry("e-dotted", "suffix", "-abc").Sense("s-dotted", "e-dotted", "NMLZ.V")
        .Entry("e-alt", "stem", "pqr").Alternate("e-alt", "pq").Sense("s-alt", "e-alt", "water")
        .Entry("e-clitic", "enclitic", "=qrs").Sense("s-clitic", "e-clitic", "TOP")
        .Entry("e-nonascii", "stem", "ñu").Sense("s-nonascii", "e-nonascii", "fish")
        .ProcessEntry("e-process", "suffix").Sense("s-process", "e-process", "XYZ")
        .Entry("e-fine", "stem", "xo").Sense("s-fine", "e-fine", "go")
        .Entry("e-name", "stem", "Name").Sense("s-name", "e-name", "Name")
        .Entry("e-loan", "stem", "radar").Sense("s-loan", "e-loan", "radar");

    [Fact]
    public async Task GlossEchoesFormBadFlagsExactlyTheEchoingSenses()
    {
        using var session = await _harness.OpenAsync(GlossEchoesFormBad);

        var result = MeasureRunner.Execute("P-gloss-echoes-form", session, session.BundleId);

        Assert.Equal(ParsimonyMeasureStatus.Computed, result.Run.Status);
        Assert.Equal(9, result.Run.EligibleItems);
        Assert.Equal(["s-alt", "s-clitic", "s-dotted", "s-echo", "s-nonascii"],
            result.Findings.Select(finding => finding.AttachesTo.Identity.Split('/')[^1]).Order(StringComparer.Ordinal));
        Assert.All(result.Findings, finding =>
        {
            Assert.Equal(ParsimonyGroupKind.GlossEchoesForm, finding.AttachesTo.GroupKind);
            Assert.True(ParsimonyFindingIdentity.GroupIdentityIsConsistent(finding));
            Assert.Equal(new ParsimonyMeasureNumber(1, 9, "glossed senses"), finding.Number);
            Assert.Single(finding.EvidenceRefs);
        });
        Assert.Contains("capitals plus a dotted suffix", Assert.Single(result.Findings,
            finding => finding.AttachesTo.Identity.EndsWith("/s-dotted")).Limitations[0]);
    }

    [Fact]
    public async Task GlossEchoesFormCleanControlHasNoFindingsOverTheSameDenominator()
    {
        using var session = await _harness.OpenAsync(GlossEchoesFormCleanControl);

        var result = MeasureRunner.Execute("P-gloss-echoes-form", session, session.BundleId);

        Assert.Equal(ParsimonyMeasureStatus.Computed, result.Run.Status);
        Assert.Equal(9, result.Run.EligibleItems);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task GlossEchoesFormAtCebScaleRunsInUnderASecond()
    {
        using var session = await _harness.OpenAsync(seed =>
        {
            for (var i = 0; i < 25000; i++)
            {
                var entry = "e" + i;
                seed.Entry(entry, i % 3 == 0 ? "suffix" : "stem", "f" + i);
                if (i % 10 == 0) seed.Alternate(entry, "alt" + i);
                seed.Sense("s" + i, entry, i % 1500 == 0 ? "F" + i : "g" + i);
                seed.Sense("t" + i, entry, "h" + i);
                if (i % 2 == 0) seed.Sense("u" + i, entry, "k" + i);
            }
        });

        var timer = Stopwatch.StartNew();
        var result = MeasureRunner.Execute("P-gloss-echoes-form", session, session.BundleId);
        timer.Stop();

        output.WriteLine($"P-gloss-echoes-form over {result.Run.EligibleItems} glossed senses: " +
            $"{timer.ElapsedMilliseconds} ms, {result.Run.FindingItems} findings");
        Assert.Equal(17, result.Run.FindingItems);
        var readTimer = Stopwatch.StartNew();
        var input = session.ReadGlossEchoCandidates();
        readTimer.Stop();
        var context = session.ReadMeasureContext(session.BundleId);
        var decideTimer = Stopwatch.StartNew();
        MeasureRunner.DecideGlossEchoesForm(MeasureCatalog.Find("P-gloss-echoes-form")!, input, context);
        decideTimer.Stop();
        output.WriteLine($"P-gloss-echoes-form split: read {readTimer.ElapsedMilliseconds} ms, decide and build findings {decideTimer.ElapsedMilliseconds} ms");
        Assert.True(timer.ElapsedMilliseconds < 1000, $"took {timer.ElapsedMilliseconds} ms");
    }
}

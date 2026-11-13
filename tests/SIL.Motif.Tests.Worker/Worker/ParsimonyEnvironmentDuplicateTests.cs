using System.Diagnostics;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.Worker;

/// <summary>P-env-duplicate over facts a stand-in parser wrote and the test patched with the defect.</summary>
[Collection(LcmCacheTestCollection.Name)]
public sealed class ParsimonyEnvironmentDuplicateTests(PristineProjectFixture pristine, ITestOutputHelper output)
{
    private const string Measure = "P-env-duplicate";
    private const string Entry = "10000000-0000-0000-0000-000000000001";
    private const string SpacedCopy = "10000000-0000-0000-0000-0000000000a1";
    private const string TrimmedCopy = "10000000-0000-0000-0000-0000000000a2";
    private const string Distinct = "10000000-0000-0000-0000-0000000000a3";
    private const string Users = "10000000-0000-0000-0000-0000000000b";

    private static void WriteGrammar(FactsWriter facts, string trimmedRightKey)
    {
        facts.WritingSystem();
        facts.Entry(Entry);
        facts.Environment(SpacedCopy, "/  _ l", "key-empty", "key-l");
        facts.Environment(TrimmedCopy, "/ _ l", "key-empty", trimmedRightKey);
        facts.Environment(Distinct, "/ n _", "key-n", "key-empty");
        facts.Allomorph(Users + "1", Entry, "pim", ("phone", SpacedCopy));
        facts.Allomorph(Users + "2", Entry, "pam", ("position", TrimmedCopy));
        facts.Allomorph(Users + "3", Entry, "pum", ("phone", Distinct));
    }

    [Fact]
    public async Task P_env_duplicate_spacing_pair_names_both_copies_and_every_user()
    {
        using var harness = new PatchedFactsHarness(pristine);
        await harness.RunAsync(Measure, (connection, transaction) =>
            WriteGrammar(new FactsWriter(connection, transaction), "key-l"), (session, bundleId) =>
        {
            var result = MeasureRunner.Execute(Measure, session, bundleId);

            Assert.Equal(ParsimonyMeasureStatus.Computed, result.Run.Status);
            Assert.Equal(3, result.Run.EligibleItems);
            Assert.Equal(1, result.Run.FindingItems);
            var finding = Assert.Single(result.Findings);
            Assert.Equal(ParsimonyGroupKind.DuplicateEnvironments, finding.AttachesTo.GroupKind);
            Assert.Contains(finding.Limitations, text => text.Contains("\"/  _ l\"") && text.Contains("pim"));
            Assert.Contains(finding.Limitations,
                text => text.Contains("\"/ _ l\"") && text.Contains("pam") && text.Contains("infix position"));
            Assert.DoesNotContain(finding.Limitations, text => text.Contains("pum"));
        });
    }

    [Fact]
    public async Task P_env_duplicate_clean_control_has_no_finding()
    {
        using var harness = new PatchedFactsHarness(pristine);
        await harness.RunAsync(Measure, (connection, transaction) =>
            WriteGrammar(new FactsWriter(connection, transaction), "key-m"), (session, bundleId) =>
        {
            var result = MeasureRunner.Execute(Measure, session, bundleId);

            Assert.Equal(ParsimonyMeasureStatus.Computed, result.Run.Status);
            Assert.Equal(3, result.Run.EligibleItems);
            Assert.Empty(result.Findings);
        });
    }

    [Fact]
    public async Task P_env_duplicate_runs_in_under_a_second_at_CEB_scale()
    {
        using var harness = new PatchedFactsHarness(pristine);
        await harness.RunAsync(Measure, (connection, transaction) =>
        {
            var facts = new FactsWriter(connection, transaction);
            facts.WritingSystem();
            facts.Entry(Entry);
            for (var index = 0; index < 2000; index++)
                facts.Environment($"20000000-0000-0000-0000-{index:D12}", $"/ _ x{index}", "key-empty",
                    index % 500 == 0 ? "key-shared" : $"key-{index}");
            for (var index = 0; index < 26000; index++)
                facts.Allomorph($"30000000-0000-0000-0000-{index:D12}", Entry, $"f{index}",
                    ("phone", $"20000000-0000-0000-0000-{index % 2000:D12}"));
        }, (session, bundleId) =>
        {
            var clock = Stopwatch.StartNew();
            var result = MeasureRunner.Execute(Measure, session, bundleId);
            clock.Stop();
            output.WriteLine($"P-env-duplicate at 2000 environments, 26000 allomorphs: {clock.ElapsedMilliseconds} ms");

            Assert.Equal(ParsimonyMeasureStatus.Computed, result.Run.Status);
            Assert.Equal(new ParsimonyMeasureNumber(3, 2000, "environments"), Assert.Single(result.Findings).Number);
            Assert.True(clock.ElapsedMilliseconds < 1000, $"took {clock.ElapsedMilliseconds} ms");
        });
    }
}

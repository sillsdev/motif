using System.Diagnostics;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;
using static SIL.Motif.Tests.Worker.ParsimonyFactsSessionHarness;

namespace SIL.Motif.Tests.Worker;

/// <summary>P-allo-repeated-form over real schema-v9 facts files, and over plain inputs for the edge cases.</summary>
[Collection(LcmCacheTestCollection.Name)]
public sealed class AlloRepeatedFormTests(PristineProjectFixture pristine, ITestOutputHelper output) : IDisposable
{
    private const string Measure = "P-allo-repeated-form";
    private static readonly string[] Sections = ["allomorphs", "environments"];
    private readonly ParsimonyFactsSessionHarness _harness = new(pristine);

    public void Dispose() => _harness.Dispose();

    private static string Id(int number) => $"a0000000-0000-0000-0000-{number:D12}";

    private static void Environments(SqliteConnection connection)
    {
        WritingSystem(connection);
        Environment(connection, Id(901), "/ [V] _"); Environment(connection, Id(902), "/ [N] _");
        Environment(connection, Id(903), "/ _ #");
    }

    private static void Bad(SqliteConnection connection)
    {
        Environments(connection);
        Entry(connection, Id(1), 1, "suffix");
        Allomorph(connection, Id(11), Id(1), 0, "suffix", "xa");
        Allomorph(connection, Id(12), Id(1), 1, "suffix", "xa", Id(901));
        Entry(connection, Id(2), 2, "prefix");
        Allomorph(connection, Id(21), Id(2), 0, "prefix", "pim");
        Allomorph(connection, Id(22), Id(2), 1, "prefix", "pim", Id(902));
        Allomorph(connection, Id(23), Id(2), 2, "prefix", "pim", Id(903));
        Entry(connection, Id(3), 3, "suffix");
        Allomorph(connection, Id(31), Id(3), 0, "suffix", "ka", Id(901), Id(902));
        Allomorph(connection, Id(32), Id(3), 1, "suffix", "ka", Id(901));
    }

    private static void Control(SqliteConnection connection)
    {
        Environments(connection);
        Entry(connection, Id(4), 4, "suffix"); // disjoint non-empty environments
        Allomorph(connection, Id(41), Id(4), 0, "suffix", "ta", Id(901));
        Allomorph(connection, Id(42), Id(4), 1, "suffix", "ta", Id(902));
        Entry(connection, Id(5), 5, "suffix"); // exact duplicates belong to P-allo-duplicate-form
        Allomorph(connection, Id(51), Id(5), 0, "suffix", "ma", Id(901));
        Allomorph(connection, Id(52), Id(5), 1, "suffix", "ma", Id(901));
        Entry(connection, Id(6), 6, "suffix"); // one allomorph
        Allomorph(connection, Id(61), Id(6), 0, "suffix", "na");
        Entry(connection, Id(7), 7, "suffix"); // process allomorphs with no forms
        Allomorph(connection, Id(71), Id(7), 0, "suffix", null);
        Allomorph(connection, Id(72), Id(7), 1, "suffix", null, Id(901));
        Entry(connection, Id(8), 8, "prefix"); // same form, different morph types
        Allomorph(connection, Id(81), Id(8), 0, "prefix", "sa");
        Allomorph(connection, Id(82), Id(8), 1, "suffix", "sa", Id(901));
    }

    private Task<ParsimonyMeasureResult> Run(Action<SqliteConnection> seed) => _harness.RunAsync(
        path => EditFacts(path, Sections, seed), (session, bundle) => MeasureRunner.Execute(Measure, session, bundle));

    [Fact]
    public async Task AlloRepeatedFormSubsumedCopiesAreFlaggedPerEntry()
    {
        var result = await Run(Bad);

        Assert.Equal(ParsimonyMeasureStatus.Computed, result.Run.Status);
        Assert.Equal(3, result.Run.EligibleItems);
        Assert.Equal(3, result.Run.FindingItems);
        var byEntry = result.Findings.ToDictionary(finding => finding.GroupKey!);
        Assert.Equal(new[] { Id(1), Id(2), Id(3) }.Select(ParsimonyFindingIdentity.EntryGroupKey).Order(),
            byEntry.Keys.Order());
        Assert.All(result.Findings, finding =>
        {
            Assert.Equal(ParsimonyGroupKind.AllomorphDuplicateForms, finding.AttachesTo.GroupKind);
            Assert.True(ParsimonyFindingIdentity.GroupIdentityIsConsistent(finding));
        });
        var suffix = byEntry[ParsimonyFindingIdentity.EntryGroupKey(Id(1))];
        Assert.Contains(suffix.Limitations, line => line.Contains("unconditioned copy") && line.Contains("\"xa\"") &&
            line.Contains("/ [V] _") && line.Contains(Id(11)) && line.Contains(Id(12)));
        Assert.Equal(new ParsimonyMeasureNumber(1, 2, suffix.Number.Unit), suffix.Number);
        var prefix = byEntry[ParsimonyFindingIdentity.EntryGroupKey(Id(2))];
        Assert.Equal(new ParsimonyMeasureNumber(2, 3, prefix.Number.Unit), prefix.Number);
        Assert.Equal(3, prefix.EvidenceRefs.Count);
        Assert.Contains(byEntry[ParsimonyFindingIdentity.EntryGroupKey(Id(3))].Limitations,
            line => line.Contains("subsumed by the copy conditioned on"));
    }

    [Fact]
    public async Task AlloRepeatedFormCleanControlHasNoFindings()
    {
        var result = await Run(Control);

        Assert.Equal(ParsimonyMeasureStatus.Computed, result.Run.Status);
        Assert.Equal(3, result.Run.EligibleItems);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task AlloRepeatedFormRunsInUnderASecondAtCebScale()
    {
        var elapsed = await _harness.RunAsync(path => EditFacts(path, Sections, connection =>
        {
            Environments(connection);
            for (var environment = 0; environment < 200; environment++)
                Environment(connection, Id(10000 + environment), "/ [C" + environment + "] _");
            for (var entry = 0; entry < 25000; entry++)
            {
                var guid = Id(100000 + entry);
                Entry(connection, guid, entry, entry % 3 == 0 ? "suffix" : "stem");
                var type = entry % 3 == 0 ? "suffix" : "stem";
                Allomorph(connection, Id(1000000 + entry * 2), guid, 0, type, "f" + entry % 4000,
                    entry % 2 == 0 ? [Id(10000 + entry % 200)] : []);
                // Every 125th entry repeats its form unconditioned and conditioned.
                if (entry % 125 == 0)
                    Allomorph(connection, Id(1000001 + entry * 2), guid, 1, type, "f" + entry % 4000,
                        entry % 2 == 0 ? [] : [Id(10000 + entry % 200)]);
            }
            for (var extra = 0; extra < 25; extra++)
            {
                var guid = Id(100000 + extra);
                Allomorph(connection, Id(5000000 + extra), guid, 5 + extra, "suffix", "g" + extra);
            }
        }), (session, bundle) =>
        {
            var timer = Stopwatch.StartNew();
            var result = MeasureRunner.Execute(Measure, session, bundle);
            timer.Stop();
            Assert.Equal(ParsimonyMeasureStatus.Computed, result.Run.Status);
            output.WriteLine($"{Measure}: {result.Findings.Count} findings in {timer.ElapsedMilliseconds} ms");
            Assert.Equal(200, result.Findings.Count);
            return timer.ElapsedMilliseconds;
        });

        Assert.True(elapsed < 1000, $"{Measure} took {elapsed} ms.");
    }

    // ---- decisions over plain inputs

    private static MeasureContext Context() => MeasureContext.For("bundle",
        new MeasureDigestContext(1, 1, ParsimonyEvidenceScopeKind.ProjectApproved, null, null));

    private static AllomorphMeasureFact Allo(string guid, string entry = "entry-1", string morphType = "suffix",
        string form = "ka", params string[] environments) =>
        new(guid, entry, 0, morphType, false,
            new Dictionary<string, IReadOnlyList<string>> { ["qaa"] = [form] },
            environments.Select((environment, index) => new AllomorphEnvironmentFact("phone", index, environment))
                .ToArray());

    private static ParsimonyMeasureResult Decide(params AllomorphMeasureFact[] allomorphs) =>
        MeasureRunner.DecideRepeatedFormAllomorphs(MeasureCatalog.Find(Measure)!, allomorphs, Context());

    [Fact]
    public void NothingIsEligibleOrFlaggedWithoutAllomorphs()
    {
        var result = Decide();

        Assert.Equal(0, result.Run.EligibleItems);
        Assert.Empty(result.Findings);
    }

    [Theory]
    [InlineData("unconditioned", true)]
    [InlineData("subset", true)]
    [InlineData("same-set-reordered", true)]
    [InlineData("disjoint", false)]
    [InlineData("overlapping", false)]
    [InlineData("exact-duplicate", false)]
    public void OnlySubsumedCopiesAreFindings(string shape, bool flagged)
    {
        var allomorphs = shape switch
        {
            "unconditioned" => new[] { Allo("a"), Allo("b", environments: "e1") },
            "subset" => new[] { Allo("a", environments: ["e1", "e2"]), Allo("b", environments: "e1") },
            "same-set-reordered" => new[] { Allo("a", environments: ["e1", "e2"]), Allo("b", environments: ["e2", "e1"]) },
            "disjoint" => new[] { Allo("a", environments: "e1"), Allo("b", environments: "e2") },
            "overlapping" => new[] { Allo("a", environments: ["e1", "e2"]), Allo("b", environments: ["e2", "e3"]) },
            _ => new[] { Allo("a", environments: "e1"), Allo("b", environments: "e1") },
        };

        var result = Decide(allomorphs);

        Assert.Equal(flagged ? 1 : 0, result.Findings.Count);
        Assert.Equal(1, result.Run.EligibleItems);
    }

    [Fact]
    public void AnUnconditionedCopyWithTwoDisjointConditionedCopiesIsOneFindingWithTwoSubsumedCopies()
    {
        var finding = Assert.Single(Decide(Allo("a"), Allo("b", environments: "e1"), Allo("c", environments: "e2")).Findings);

        Assert.Equal(new ParsimonyMeasureNumber(2, 3, finding.Number.Unit), finding.Number);
        Assert.Equal(2, finding.Limitations.Count(line => line.Contains("unconditioned copy")));
    }

    [Fact]
    public void DifferentMorphTypesOrEntriesNeverGroup()
    {
        Assert.Empty(Decide(Allo("a"), Allo("b", morphType: "prefix", environments: "e1")).Findings);
        Assert.Empty(Decide(Allo("a"), Allo("b", entry: "entry-2", environments: "e1")).Findings);
    }

    [Fact]
    public void TheFindingIdSurvivesAnEnvironmentEditButNotAMemberChange()
    {
        string IdOf(params AllomorphMeasureFact[] members) => Assert.Single(Decide(members).Findings).FindingId;

        var original = IdOf(Allo("a"), Allo("b", environments: "e1"));
        Assert.Equal(original, IdOf(Allo("a"), Allo("b", environments: ["e1", "e2"])));
        Assert.NotEqual(original, IdOf(Allo("a"), Allo("c", environments: "e1")));
    }

    [Fact]
    public void ExactDuplicatesAmongSubsumedCopiesAreOneCopy()
    {
        var finding = Assert.Single(Decide(Allo("a"), Allo("b", environments: "e1"), Allo("c", environments: "e1")).Findings);

        Assert.Equal(new ParsimonyMeasureNumber(2, 3, finding.Number.Unit), finding.Number);
        Assert.Single(finding.Limitations, line => line.Contains("unconditioned copy"));
    }
}

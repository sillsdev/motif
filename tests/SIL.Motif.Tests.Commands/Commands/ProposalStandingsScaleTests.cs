using System.Diagnostics;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.Commands;

/// <summary>#642: listing Proposals must not read the Baseline's words again for every Proposal.</summary>
public sealed class ProposalStandingsScaleTests(ITestOutputHelper output) : IDisposable
{
    private const int BaselineWords = 400;
    private const int TrialWords = 20;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.ProposalStandingsScale",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void TheRowsReadGrowWithTheTrialsAndNotWithTheBaselineTheyShare()
    {
        var few = Measure(5);
        var many = Measure(20);
        var perProposal = (many.RecordsDeserialized - few.RecordsDeserialized) / 15;
        output.WriteLine($"rows per added Proposal {perProposal}; baseline words {BaselineWords}");
        // One Baseline read costs several rows a word; a Trial of TrialWords words must cost far less than that.
        Assert.True(perProposal < BaselineWords, $"each added Proposal read {perProposal} rows, as if it re-read the Baseline.");
    }

    private ScaleRepositoryReadCount Measure(int proposals)
    {
        var directory = Path.Combine(_root, proposals.ToString());
        Directory.CreateDirectory(directory);
        using var fixture = new TrialsTests.ProjectFixture(directory);
        var baseline = Enumerable.Range(0, BaselineWords).Select(i => new AssessedWord("w" + i, "analysed", [])).ToArray();
        fixture.RecordProjectAssessment("project-root", AssessmentKinds.ParseTime, baseline);
        for (var i = 0; i < proposals; i++)
        {
            var proposal = fixture.SaveProposal();
            fixture.RecordAssessment(proposal, "trial-root-" + i, AssessmentKinds.ParseTime, words: baseline[..TrialWords]);
            fixture.CreateJob(proposal.Json, "2026-09-24T11:00:00Z", "trial-" + i, ["trial-root-" + i], allWords: true,
                baselineAssessmentIds: ["project-root"]);
        }
        using var reads = ScaleCountHarness.ObserveRepositoryReads();
        var watch = Stopwatch.StartNew();
        var standings = ProposalStandingsCommand.Read(fixture.Database, fixture.Project, includeSetAside: false).Value!;
        var count = reads.Snapshot();
        Assert.Equal(proposals, standings.Proposals.Count);
        var line = $"PROBE n={proposals} ms={watch.ElapsedMilliseconds} queries={count.Queries} deserialized={count.RecordsDeserialized}";
        output.WriteLine(line);
        return count;
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }
}

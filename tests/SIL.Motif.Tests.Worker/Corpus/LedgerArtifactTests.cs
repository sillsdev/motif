using System.Text.Json;
using SIL.Motif.Worker.Corpus.Ledger;
using Xunit;
using static SIL.Motif.Tests.Worker.Corpus.LedgerFixtures;

namespace SIL.Motif.Tests.Worker.Corpus;

public sealed class LedgerArtifactTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("motif-artifacts-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static readonly string A = Reading(1, 2);

    [Fact]
    public void AComparableCheckMadeAtAnotherPinIsRefusedWithATypedCode()
    {
        var check = new TypeCheck(CheckCompleteness.Complete, true, false, false, [], "another-pin");

        var exception = Assert.Throws<LedgerRefusalException>(() =>
            Report([Row("a", 5, "Unverified")], [("a", 5)], new Dictionary<string, TypeCheck> { ["a"] = check }));

        Assert.Equal(LedgerRefusalCode.CheckPinMismatch, exception.Code);
    }

    [Fact]
    public void AComparableCheckWithNoPinIsRefusedToo()
    {
        var check = new TypeCheck(CheckCompleteness.Partial, true, true);

        var exception = Assert.Throws<LedgerRefusalException>(() =>
            Report([Row("a", 5, "Unverified")], [("a", 5)], new Dictionary<string, TypeCheck> { ["a"] = check }));

        Assert.Equal(LedgerRefusalCode.CheckPinMismatch, exception.Code);
    }

    [Fact]
    public void ACheckMarkedNotComparableMayCarryAnotherPinAndNamesItsReason()
    {
        var check = new TypeCheck(CheckCompleteness.Complete, false, false, false, [], "another-pin", CheckIncomparable.RevisionIncompatible);

        var report = Report([Row("a", 5, "Unverified")], [("a", 5)], new Dictionary<string, TypeCheck> { ["a"] = check });

        Assert.Contains("check-revision-incompatible", report.Rows.Single().Decision.Reasons);
        Assert.Equal(StateFreshness.Stale, report.Rows.Single().Decision.Freshness);
    }

    private static AuditManifest Manifest() =>
        new("members", Frequency().Digest, Pin, "seeded-shuffle", 42, "scorer", "1", "ref-rev", "ref-digest", ["s1", "s2"]);

    private static string[] Ids() => Enumerable.Range(0, DoneVerdict.MinimumSample).Select(n => "occ-" + n).ToArray();

    [Fact]
    public void TheAuditSampleWriterIsByteDeterministic()
    {
        var ids = Ids();
        var outcomes = ids.Select((id, n) => new AuditOutcome(id, n % 100 == 0 ? "miss" : "right")).ToArray();
        var first = Path.Combine(_root, "a");
        var second = Path.Combine(_root, "b");

        AuditSampleWriter.Write(first, Manifest(), ids, Revision, outcomes);
        AuditSampleWriter.Write(second, Manifest(), ids, Revision, outcomes);

        foreach (var name in new[] { "manifest.json", "selection.jsonl", "outcomes.jsonl" })
            Assert.Equal(File.ReadAllBytes(Path.Combine(first, name)), File.ReadAllBytes(Path.Combine(second, name)));
    }

    [Fact]
    public void AWrittenAuditSampleRoundTripsThroughTheDoneReader()
    {
        var ids = Ids();
        var outcomes = ids.Select((id, n) => new AuditOutcome(id, n < 10 ? "miss" : "right")).ToArray();
        var directory = Path.Combine(_root, "sample");
        var digest = AuditSampleWriter.Write(directory, Manifest(), ids, Revision, outcomes);
        var report = Report([Row("a", 5, "Unverified")], [("a", 5)]);

        var correct = DoneVerdict.Correct(report with { Stamp = report.Stamp with { FrequencySha256 = Frequency().Digest, TokenizerSha256 = Pin } },
            directory);

        Assert.Equal(DoneVerdictKind.Pass, correct.Verdict);
        Assert.Equal(DoneVerdict.MinimumSample, correct.Counts["size"]);
        Assert.Equal(DoneVerdict.MinimumSample - 10, correct.Counts["right"]);
        Assert.Equal(10, correct.Counts["miss"]);
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "manifest.json")));
        Assert.Equal(digest, manifest.RootElement.GetProperty("selectionDigest").GetString());
    }

    [Fact]
    public void TheAuditWriterRefusesADuplicateSelectionOrAnOutcomeOutsideIt()
    {
        var directory = Path.Combine(_root, "bad");
        Assert.Throws<ArgumentException>(() => AuditSampleWriter.Write(directory, Manifest(), ["x", "x"], Revision, []));
        Assert.Throws<ArgumentException>(() =>
            AuditSampleWriter.Write(directory, Manifest(), ["x"], Revision, [new AuditOutcome("y", "right")]));
    }

    [Fact]
    public void TheProtectedSetWriterIsByteDeterministicWhateverTheInputOrder()
    {
        var pairs = new[] { ("b", A), ("a", Reading(5, 6)), ("a", A) };
        var first = Path.Combine(_root, "p1.json");
        var second = Path.Combine(_root, "p2.json");

        var digest = ProtectedSetWriter.Write(first, Revision, pairs);
        ProtectedSetWriter.Write(second, Revision, pairs.Reverse());

        Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));
        Assert.Equal(64, digest.Length);
    }

    [Fact]
    public void AWrittenProtectedSetRoundTripsThroughTheDoneReader()
    {
        var path = Path.Combine(_root, "protected.json");
        ProtectedSetWriter.Write(path, Revision, [("a", A)]);
        var checks = new Dictionary<string, TypeCheck> { ["a"] = Complete(true, A) };

        var kept = Report([Row("a", 5, "Unverified")], [("a", 5)], checks, null, path).Done.Stable;
        var lostChecks = new Dictionary<string, TypeCheck> { ["a"] = Complete(true) };
        var lost = Report([Row("a", 5, "Unverified")], [("a", 5)], lostChecks, null, path).Done.Stable;

        Assert.Equal(DoneVerdictKind.Pass, kept.Verdict);
        Assert.Equal(1, kept.Counts["kept"]);
        Assert.Equal(DoneVerdictKind.Fail, lost.Verdict);
    }

    [Fact]
    public void TheProtectedSetWriterRefusesAnEmptyOrDuplicatedSet()
    {
        var path = Path.Combine(_root, "none.json");
        Assert.Throws<ArgumentException>(() => ProtectedSetWriter.Write(path, Revision, []));
        Assert.Throws<ArgumentException>(() => ProtectedSetWriter.Write(path, Revision, [("a", A), ("a", A)]));
    }
}

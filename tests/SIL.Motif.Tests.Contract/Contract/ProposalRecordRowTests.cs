using System.Runtime.CompilerServices;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Contract;

/// <summary>A record import's per-file manifest reads back as one row per file, in the orders <c>show --records</c> offers.</summary>
public sealed class ProposalRecordRowTests
{
    private const string Extensions = """
        {"composers":[{"composer":"RecordImport","input":{"recordImport":{"entries":[
          {"path":"b/two.fw.md","sha256":"s2","status":"updated","kind":"lexicon.entry","record":"r2","operations":3,"text":"x"},
          {"path":"a/one.fw.md","sha256":"s1","status":"created","kind":"lexicon.entry","record":"r1","operations":5,"dependsOn":["r2"],"text":"x"},
          {"path":"c/three.fw.md","sha256":"s3","status":"refused","kind":"phonology.rule","code":"record-import.plan-refused"},
          {"path":"d/four.fw.md","sha256":"s4","status":"unchanged","kind":"lexicon.entry","record":"r4"}]}}},
          {"composer":"Other","input":{}}]}
        """;

    private static string GoldenPath([CallerFilePath] string source = "") =>
        Path.Combine(Path.GetDirectoryName(source)!, "..", "Golden", "ProposalShow", "records.json");

    [Fact]
    public void TheManifestEntriesBecomeRowsInManifestOrder()
    {
        var rows = ProposalRecordRow.From(Extensions)!;

        Assert.Equal(["b/two.fw.md", "a/one.fw.md", "c/three.fw.md", "d/four.fw.md"], rows.Select(row => row.Path));
        Assert.Equal(["update", "create", "refused", "unchanged"], rows.Select(row => row.Change));
        Assert.Equal([3, 5, null, null], rows.Select(row => row.Operations));
        Assert.Equal([false, true, false, false], rows.Select(row => row.HasBatchEdges));
        Assert.Equal("record-import.plan-refused", rows[2].Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("""{"composers":[{"composer":"Other","input":{}}]}""")]
    [InlineData("""{"other":1}""")]
    public void AProposalWithoutARecordImportHasNoRows(string? extensions) =>
        Assert.Null(ProposalRecordRow.From(extensions));

    [Fact]
    public void RowsSortByKindOrBatchEdgesAndFilterToRefused()
    {
        var rows = ProposalRecordRow.From(Extensions)!;

        Assert.Equal(["a/one.fw.md", "b/two.fw.md", "d/four.fw.md", "c/three.fw.md"],
            ProposalRecordRow.Arrange(rows, "kind", false).Select(row => row.Path));
        Assert.Equal("a/one.fw.md", ProposalRecordRow.Arrange(rows, "batch-edges", false)[0].Path);
        Assert.Equal(["c/three.fw.md"], ProposalRecordRow.Arrange(rows, "path", true).Select(row => row.Path));
    }

    [Fact]
    public void TheGroupedShowHasTheGoldenShape()
    {
        var rows = ProposalRecordRow.From(Extensions)!;
        var detail = new ProposalDetailProjection("p1", "draft", null, null, null, []) { Records = rows, RecordTotal = rows.Count };

        var generated = ProjectionJson.Serialize(detail);

        if (Environment.GetEnvironmentVariable("MOTIF_UPDATE_GOLDEN") == "1") File.WriteAllText(GoldenPath(), generated);
        Assert.Equal(File.ReadAllText(GoldenPath()), generated);
    }

    [Fact]
    public void AShowThatIsNotGroupedSerializesNoRecordMembers()
    {
        var text = ProjectionJson.Serialize(new ProposalDetailProjection("p1", "draft", null, null, null, []));

        Assert.DoesNotContain("records", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("recordTotal", text, StringComparison.OrdinalIgnoreCase);
    }
}

using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class EvidenceExportTests : IDisposable
{
    private const string Version = "0.1.0";

    static EvidenceExportTests() => System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(
        typeof(WfiAnalysisOperationKinds).Module.ModuleHandle);

    private readonly string _fwDataPath = PlaceholderProject.Create("SIL.Motif.Tests.EvidenceExport");
    private readonly string _workerRoot = Path.Combine(Path.GetTempPath(), "motif-evidence-export-" + Guid.NewGuid().ToString("N"));
    private string OutDirectory => Path.Combine(_workerRoot, "out");

    public EvidenceExportTests() => Directory.CreateDirectory(OutDirectory);

    public void Dispose()
    {
        var projectDir = Path.GetDirectoryName(_fwDataPath)!;
        if (Directory.Exists(projectDir)) Directory.Delete(projectDir, recursive: true);
        if (Directory.Exists(_workerRoot)) Directory.Delete(_workerRoot, recursive: true);
    }

    // One Proposal holding one evidence operation per tier given; applied unless told otherwise.
    private string[] Stage(bool apply, params string[] tiers) => StageWith(apply, "sita", null, tiers);

    // Minted ids are not ordinal-ordered by creation, so a test that needs an order names the ids.
    private const string FirstId = "111111111111111111111A";
    private const string SecondId = "222222222222222222222A";

    private string[] StageAs(string proposalId, params string[] tiers) =>
        StageWith(true, "sita", CanonicalId.Parse(proposalId), tiers);

    private string[] StageWith(bool apply, string intendedForm, CanonicalId? named, params string[] tiers)
    {
        var proposalId = named ?? CanonicalId.Mint();
        var subjects = tiers.Select(_ => CanonicalId.Mint().Value).ToArray();
        var operations = tiers.Select((tier, index) =>
            "{\"operationId\":\"" + CanonicalId.Mint().Value + "\",\"kind\":\"analysis/decisionEvidence/record\"," +
            "\"target\":\"" + subjects[index] + "\",\"after\":{},\"provenance\":[{\"type\":\"decisionEvidence\"," +
            "\"tier\":\"" + tier + "\",\"sources\":[],\"detail\":{\"intendedForm\":" + JsonSerializer.Serialize(intendedForm) + "," +
            "\"editType\":\"vowel\",\"violatedRule\":\"class prefix\",\"standardForm\":\"sita\"}}]}");
        var json = "{\"contractVersions\":{\"analysis\":\"1.0\"},\"proposalId\":\"" + proposalId.Value +
            "\",\"requires\":[],\"operations\":[" + string.Join(",", operations) + "]}";
        var proposal = ProposalJsonParser.Parse(json);
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        var repository = new ProposalRepository(database);
        repository.SaveRevision(new ProposalRevisionRecord(
            proposalId, SIL.Motif.Contract.Canonicalization.IntentDigest.Compute(proposal), json, "proposed", null, null, null));
        if (apply) repository.SetStatus(proposalId, "applied", null);
        return subjects;
    }

    private string[] Stage(params string[] tiers) => Stage(true, tiers);

    private DecisionEvidenceExportResponse Page(string? tier = null, string? after = null, int limit = 500)
    {
        var page = ProposalCommands.EvidenceExport(new EvidenceExportRequest(_fwDataPath, Version, tier, after, limit));
        Assert.True(page.Succeeded, page.Refusal?.Code + " " + page.Refusal?.Message);
        return page.Value!;
    }

    private List<DecisionEvidenceRow> Walk(string? tier, int limit, out int pages)
    {
        var rows = new List<DecisionEvidenceRow>();
        string? after = null;
        pages = 0;
        do
        {
            var page = Page(tier, after, limit);
            Assert.True(page.Rows.Count <= limit);
            rows.AddRange(page.Rows);
            after = page.Next;
            pages++;
        } while (after is not null);
        return rows;
    }

    [Fact]
    public void OnePageHoldsEveryRowInProposalIdOrderAndNamesItsFormat()
    {
        Stage("ill-formed", "undecided");
        Stage("misspelled-typo");

        var page = Page();

        Assert.Equal("motif-decision-evidence/1", page.Format);
        Assert.Equal(3, page.Rows.Count);
        Assert.Null(page.Next);
        Assert.All(page.Rows, row => Assert.False(row.Human));
        Assert.Equal(page.Rows.Select(row => row.ProposalId).OrderBy(id => id, StringComparer.Ordinal),
            page.Rows.Select(row => row.ProposalId));
    }

    [Fact]
    public void AProjectWithNoEvidenceExportsAnEmptyPage()
    {
        var page = Page();
        Assert.Empty(page.Rows);
        Assert.Null(page.Next);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    public void PagesNeverExceedTheLimitAndTogetherEqualTheListInTheSameOrder(int limit)
    {
        Stage("ill-formed", "undecided", "ill-formed");
        Stage("misspelled-typo");
        Stage("undecided", "undecided", "ill-formed");

        var walked = Walk(null, limit, out var pages);

        var listed = ProposalCommands.EvidenceList(new EvidenceListRequest(_fwDataPath, Version, Limit: 500)).Value!.Rows;
        Assert.Equal(7, walked.Count);
        Assert.Equal(listed.Select(row => (row.ProposalId, row.OperationId)), walked.Select(row => (row.ProposalId, row.OperationId)));
        Assert.Equal((int)Math.Ceiling(7 / (double)limit), pages);
    }

    [Fact]
    public void ThePageThatEndsExactlyOnTheLastRowHasNoNextCursor()
    {
        Stage("ill-formed", "undecided");

        var page = Page(limit: 2);

        Assert.Equal(2, page.Rows.Count);
        Assert.Null(page.Next);
    }

    [Fact]
    public void ATierFilterPagesOnlyThatTier()
    {
        Stage("ill-formed", "undecided", "ill-formed");
        Stage("ill-formed");

        var walked = Walk("ill-formed", 2, out var pages);

        Assert.Equal(3, walked.Count);
        Assert.All(walked, row => Assert.Equal("ill-formed", row.Tier));
        Assert.Equal(2, pages);
    }

    [Fact]
    public void AProposalThatIsNotAppliedIsNotExported()
    {
        Stage(apply: false, "undecided");
        Assert.Empty(Page().Rows);
    }

    [Theory]
    [InlineData("gold", null, 10, "evidence.unknown-tier")]
    [InlineData(null, null, 0, "evidence.invalid-page")]
    [InlineData(null, null, 501, "evidence.invalid-page")]
    public void ExportRefusesABadTierOrLimitWithItsCode(string? tier, string? after, int limit, string code) =>
        Assert.Equal(code, ProposalCommands.EvidenceExport(
            new EvidenceExportRequest(_fwDataPath, Version, tier, after, limit)).Refusal?.Code);

    [Theory]
    [InlineData("1::AAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("x::AAAAAAAAAAAAAAAAAAAAAA:0")]
    [InlineData("-1::AAAAAAAAAAAAAAAAAAAAAA:0")]
    [InlineData("1:gold:AAAAAAAAAAAAAAAAAAAAAA:0")]
    [InlineData("1::AAAAAAAAAAAAAAAAAAAAAA:x")]
    [InlineData("1::AAAAAAAAAAAAAAAAAAAAAA:-1")]
    [InlineData("1::AAAAAAAAAAAAAAAAAAAAAA:+1")]
    [InlineData("1::not-an-id:0")]
    [InlineData(":::0")]
    [InlineData("not-a-cursor")]
    public void AMalformedFourFieldCursorIsRefusedAsInvalidEvenWhenTheStoreAgrees(string after)
    {
        Stage("ill-formed");

        var refused = ProposalCommands.EvidenceExport(new EvidenceExportRequest(_fwDataPath, Version, null, after, 10));

        Assert.Equal("evidence.invalid-cursor", refused.Refusal?.Code);
        Assert.Contains("'after' must be the 'next' value", refused.Refusal!.Message);
    }

    [Fact]
    public void AProposalIdWhoseSpellingHoldsAColonWalksOnACursorNamingIt()
    {
        var prefixed = "ns:" + FirstId;
        StageAs(SecondId, "ill-formed");
        StageAs(prefixed, "undecided", "ill-formed");
        var rows = new List<DecisionEvidenceRow>();
        var cursors = new List<string>();
        string? after = null;
        do
        {
            var page = Page(after: after, limit: 1);
            rows.AddRange(page.Rows);
            after = page.Next;
            if (after is not null) cursors.Add(after);
        } while (after is not null);

        Assert.Equal([SecondId, prefixed, prefixed], rows.Select(row => row.ProposalId));
        Assert.Contains(cursors, cursor => cursor.Contains(":ns:" + FirstId + ":1", StringComparison.Ordinal));
    }

    [Fact]
    public void ExportingChangesNothingInTheStore()
    {
        Stage("ill-formed");
        var before = File.ReadAllBytes(_fwDataPath);
        var listedBefore = ProposalCommands.EvidenceList(new EvidenceListRequest(_fwDataPath, Version)).Value!;

        Page();

        var listedAfter = ProposalCommands.EvidenceList(new EvidenceListRequest(_fwDataPath, Version)).Value!;
        Assert.Equal(listedBefore.Total, listedAfter.Total);
        Assert.Equal(listedBefore.AppliedProposals, listedAfter.AppliedProposals);
        Assert.Equal(before, File.ReadAllBytes(_fwDataPath));
    }

    private void CorruptContent(string proposalId)
    {
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE ProposalRevisions SET ProposalJson = X'00' WHERE ProposalId = $id;";
        command.Parameters.AddWithValue("$id", proposalId);
        Assert.Equal(1, command.ExecuteNonQuery());
    }

    [Fact]
    public void APageLoadsOnlyTheProposalsItReadsRowsFrom()
    {
        StageAs(FirstId, "ill-formed", "undecided");
        StageAs(SecondId, "undecided");
        CorruptContent(SecondId);

        var first = Page(limit: 1);

        Assert.Single(first.Rows);
        Assert.NotNull(first.Next);
        var reachesIt = ProposalCommands.EvidenceExport(new EvidenceExportRequest(_fwDataPath, Version, null, first.Next, 1));
        Assert.False(reachesIt.Succeeded);
    }

    [Fact]
    public void APageAfterAnUnreadableProposalNeverLoadsIt()
    {
        StageAs(FirstId, "ill-formed");
        StageAs(SecondId, "undecided");
        CorruptContent(FirstId);

        var page = Page(after: "2::" + SecondId + ":0");

        Assert.Equal("undecided", Assert.Single(page.Rows).Tier);
    }

    [Fact]
    public void AnUnreadableProposalRefusalNamesThatProposal()
    {
        StageAs(FirstId, "ill-formed");
        StageAs(SecondId, "undecided");
        CorruptContent(SecondId);

        var refused = ProposalCommands.EvidenceExport(new EvidenceExportRequest(_fwDataPath, Version, null, null, 1));

        Assert.Equal(SecondId, refused.Refusal!.Facts["proposalId"]);
        Assert.False(refused.Succeeded);
    }

    [Fact]
    public void ACursorRefusesWhenAProposalWasAppliedAfterItWasIssued()
    {
        Stage("ill-formed", "undecided");
        var first = Page(limit: 1);
        Stage("misspelled-typo");

        var refused = ProposalCommands.EvidenceExport(new EvidenceExportRequest(_fwDataPath, Version, null, first.Next, 1));

        Assert.Equal("evidence.invalid-cursor", refused.Refusal?.Code);
        Assert.Contains("Start the export again", refused.Refusal!.Message);
    }

    [Fact]
    public void ACursorRefusesAChangedTierFilterInEitherDirection()
    {
        Stage("ill-formed", "ill-formed", "undecided");
        var unfiltered = Page(limit: 1);
        var filtered = Page("ill-formed", limit: 1);

        Assert.Equal("evidence.invalid-cursor", ProposalCommands.EvidenceExport(
            new EvidenceExportRequest(_fwDataPath, Version, "ill-formed", unfiltered.Next, 1)).Refusal?.Code);
        Assert.Equal("evidence.invalid-cursor", ProposalCommands.EvidenceExport(
            new EvidenceExportRequest(_fwDataPath, Version, null, filtered.Next, 1)).Refusal?.Code);
        Assert.Equal("evidence.invalid-cursor", ProposalCommands.EvidenceExport(
            new EvidenceExportRequest(_fwDataPath, Version, "undecided", filtered.Next, 1)).Refusal?.Code);
    }

    [Fact]
    public void ACursorNamingAProposalThatIsGoneResumesAtTheNextOneInOrder()
    {
        var before = new string('0', 21) + "A";
        Stage("ill-formed");
        Stage("undecided");
        var after = new string('z', 21) + "A";

        Assert.Equal(2, Page(after: "2::" + before + ":0").Rows.Count);
        Assert.Equal(2, Page(after: "2::" + before + ":5").Rows.Count);
        Assert.Empty(Page(after: "2::" + after + ":0").Rows);
    }

    [Fact]
    public void ATierFilterWalksAProposalThatSplitsAcrossPagesOneRowAtATime()
    {
        Stage("ill-formed", "undecided", "ill-formed", "ill-formed");

        var walked = Walk("ill-formed", 1, out var pages);

        Assert.Equal(3, walked.Count);
        Assert.Equal(3, pages);
        Assert.Equal(3, walked.Select(row => row.OperationId).Distinct().Count());
    }

    [Fact]
    public void ANonAsciiAndQuoteBearingValueSurvivesTheFileAndStandardOutput()
    {
        const string form = "si\"ta \\ ñ 日本 \U0001F600";
        StageWith(true, form, null, "misspelled-typo");
        var path = Path.Combine(OutDirectory, "evidence.json");

        Assert.Equal(0, Cli("evidence", "export", "--out", path).ExitCode);
        var streamed = Cli("evidence", "export");

        foreach (var text in new[] { File.ReadAllText(path), streamed.Output })
        {
            using var document = JsonDocument.Parse(text);
            Assert.Equal(form, document.RootElement.GetProperty("rows")[0].GetProperty("detail")
                .GetProperty("intendedForm").GetString());
        }
    }

    [Fact]
    public void AWriteFailureLeavesNoTemporaryFileAndIsRefusedAsUnwritable()
    {
        Stage("ill-formed", "undecided");
        var path = Path.Combine(OutDirectory, "evidence.json");
        string? temporary = null;

        var result = SIL.Motif.Cli.EvidenceExportCli.Run(_fwDataPath, Version, null, 500, path, TextWriter.Null, asJson: true,
            createTemporary: name =>
            {
                temporary = name;
                return new FailingStream(new FileStream(name, FileMode.CreateNew, FileAccess.Write, FileShare.None));
            });

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("evidence.out-unwritable", result.Output);
        Assert.NotNull(temporary);
        Assert.False(File.Exists(temporary));
        Assert.Empty(Directory.GetFileSystemEntries(OutDirectory));
    }

    [Fact]
    public void AnOutThatIsAnExistingDirectoryIsRefusedAndLeavesNoTemporaryFileAndTheDirectoryUnchanged()
    {
        Stage("ill-formed");
        var target = Path.Combine(OutDirectory, "target");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "keep.txt"), "kept");

        var result = Cli("evidence", "export", "--out", target, "--json");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("evidence.out-unwritable", result.Error + result.Output);
        Assert.Equal(["target"], Directory.GetFileSystemEntries(OutDirectory).Select(Path.GetFileName));
        Assert.Equal(["keep.txt"], Directory.GetFileSystemEntries(target).Select(Path.GetFileName));
        Assert.Equal("kept", File.ReadAllText(Path.Combine(target, "keep.txt")));
    }

    private sealed class FailingStream(Stream inner) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) => throw new IOException("No space left on device.");
        public override void Write(ReadOnlySpan<byte> buffer) => throw new IOException("No space left on device.");
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }

    private CliProcessResult Cli(params string[] arguments) =>
        CliInProcess.Run(_workerRoot, null, true, [.. arguments, "--project", _fwDataPath]);

    [Fact]
    public void WithOutTheFileHoldsOneDocumentOfEveryRowCountedAtTheEnd()
    {
        Stage("ill-formed", "undecided", "ill-formed");
        Stage("misspelled-typo");
        var path = Path.Combine(OutDirectory, "evidence.json");

        var result = Cli("evidence", "export", "--out", path, "--page-size", "2", "--json");

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        Assert.Equal("motif-decision-evidence/1", root.GetProperty("format").GetString());
        Assert.Equal(4, root.GetProperty("rows").GetArrayLength());
        Assert.Equal(4, root.GetProperty("total").GetInt32());
        Assert.Equal(2, root.GetProperty("tierCounts").GetProperty("ill-formed").GetInt32());
        Assert.All(root.GetProperty("rows").EnumerateArray(), row =>
        {
            Assert.Equal("source-validated", row.GetProperty("label").GetString());
            Assert.False(row.GetProperty("human").GetBoolean());
        });
        using var summary = JsonDocument.Parse(result.Output);
        Assert.Equal(path, summary.RootElement.GetProperty("path").GetString());
        Assert.Equal(4, summary.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(["evidence.json"], Directory.GetFileSystemEntries(OutDirectory).Select(Path.GetFileName));
    }

    [Fact]
    public void WithOutTheTextSummaryNamesTheFileAndTheCounts()
    {
        Stage("ill-formed");
        var path = Path.Combine(OutDirectory, "evidence.json");

        var result = Cli("evidence", "export", "--out", path);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("1 source-validated record(s)", result.Output);
        Assert.Contains(path, result.Output);
        Assert.Contains("ill-formed: 1", result.Output);
    }

    [Fact]
    public void WithoutOutTheDocumentStreamsToStandardOutputAndPageSizeDoesNotChangeIt()
    {
        Stage("ill-formed", "undecided", "ill-formed");

        var whole = Cli("evidence", "export");
        var paged = Cli("evidence", "export", "--page-size", "1");

        Assert.Equal(0, whole.ExitCode);
        using var document = JsonDocument.Parse(whole.Output);
        Assert.Equal(3, document.RootElement.GetProperty("rows").GetArrayLength());
        Assert.Equal(whole.Output, paged.Output);
    }

    [Fact]
    public void ATierFilterReachesTheFile()
    {
        Stage("ill-formed", "undecided");

        var result = Cli("evidence", "export", "--tier", "undecided");

        using var document = JsonDocument.Parse(result.Output);
        Assert.Equal(1, document.RootElement.GetProperty("total").GetInt32());
        Assert.Equal("undecided", document.RootElement.GetProperty("rows")[0].GetProperty("tier").GetString());
    }

    [Fact]
    public void AnUnwritableOutIsRefusedWithItsCodeAndLeavesNothingBehind()
    {
        Stage("ill-formed");
        var missingDirectory = Path.Combine(OutDirectory, "absent", "evidence.json");

        var result = Cli("evidence", "export", "--out", missingDirectory, "--json");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("evidence.out-unwritable", result.Error + result.Output);
        Assert.False(File.Exists(missingDirectory));
        Assert.Empty(Directory.GetFileSystemEntries(OutDirectory));
    }

    [Fact]
    public void ARefusedExportDoesNotReplaceAnExistingFile()
    {
        Stage("ill-formed");
        var path = Path.Combine(OutDirectory, "evidence.json");
        File.WriteAllText(path, "earlier");

        var result = Cli("evidence", "export", "--out", path, "--tier", "gold", "--json");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("evidence.unknown-tier", result.Error + result.Output);
        Assert.Equal("earlier", File.ReadAllText(path));
        Assert.Equal(["evidence.json"], Directory.GetFileSystemEntries(OutDirectory).Select(Path.GetFileName));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("501")]
    [InlineData("many")]
    public void ABadPageSizeIsRefusedBeforeAnythingIsWritten(string size)
    {
        var path = Path.Combine(OutDirectory, "evidence.json");

        var result = Cli("evidence", "export", "--out", path, "--page-size", size);

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(File.Exists(path));
    }
}

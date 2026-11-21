using System.Text;
using Microsoft.Data.Sqlite;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Worker.Corpus.Ledger;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>Word-type coverage over lane ledger files: the Done tests, the state table, what is Open and every refusal.</summary>
public sealed class CoverageCommandsTests : IDisposable
{
    private const string Revision = "rev-a";
    private const string Pin = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-coverage", Guid.NewGuid().ToString("N"));
    private readonly string _project;

    public CoverageCommandsTests()
    {
        Directory.CreateDirectory(_root);
        _project = Path.Combine(_root, "lane.fwdata");
        File.WriteAllText(_project, "placeholder");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private static string Id(int n) => CanonicalId.FromGuid(new Guid(n, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1)).Value;
    private static string Reading(int entry, int morph) => $"{Id(entry)}/{Id(entry + 100)}/{Id(entry + 200)}|{Id(morph)}|";

    private static string Row(string word, long tokens, string state, string expected = "", string cls = "", string evidence = "",
        string revision = Revision) => string.Join('\t', word, tokens.ToString(), state, expected, cls, evidence, "p", revision);

    private static string LedgerText(params string[] rows) =>
        LedgerTsvReader.Header + "\n" + string.Concat(rows.Select(row => row + "\n"));

    private static string Sidecar(long rowCount, long tokenSum) => $$"""
        {"tokenizerId":"t@1","tokenizerSha256":"{{Pin}}","fold":"none","normalization":"NFC","sourceIds":["s1"],
         "scopeFilter":"all","frequencySource":"freq.tsv","grammarRevision":"{{Revision}}","rowCount":{{rowCount}},"tokenSum":{{tokenSum}}}
        """;

    private static TypeCheck Complete(bool present, params string[] produced) =>
        new(CheckCompleteness.Complete, true, present, false, produced, Revision);

    private static string Frequency(params (string Word, long Weight)[] entries) =>
        string.Concat(entries.Select(item => $"{item.Word}\t{item.Weight}\n"));

    private CoverageRequest Write(string[] rows, (string Word, long Weight)[] frequency, int limit = 50, bool tokens = false)
    {
        var ledger = LedgerText(rows);
        var parsed = LedgerTsvReader.Read(Encoding.UTF8.GetBytes(ledger));
        return Write(ledger, Sidecar(parsed.Rows.Count, parsed.TokenSum),
            Frequency(frequency), limit, tokens);
    }

    private CoverageRequest Write(string ledger, string sidecar, string frequency, int limit = 50, bool tokens = false)
    {
        var ledgerPath = Path.Combine(_root, "ledger.tsv");
        var sidecarPath = Path.Combine(_root, "ledger.meta.json");
        var frequencyPath = Path.Combine(_root, "freq.tsv");
        File.WriteAllText(ledgerPath, ledger);
        File.WriteAllText(sidecarPath, sidecar);
        File.WriteAllText(frequencyPath, frequency);
        return new(_project, "1.0", ledgerPath, sidecarPath, frequencyPath, Limit: limit, Tokens: tokens);
    }

    private static CommandOutcome<StoreInputs> NoStore(LedgerSidecar _) => CommandOutcome<StoreInputs>.Success(StoreInputs.None);

    private static Func<LedgerSidecar, CommandOutcome<StoreInputs>> Checks(params (string Word, TypeCheck Check)[] checks) =>
        _ => CommandOutcome<StoreInputs>.Success(new(checks.ToDictionary(item => item.Word, item => item.Check), null));

    private static CoverageResponse Succeeded(CommandOutcome<CoverageResponse> outcome)
    {
        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        return outcome.Value!;
    }

    private static string[] TwoTypes() => [Row("alpha", 3, "Expected", Reading(1, 2)), Row("beta", 2, "Open", cls: "compounding")];

    [Fact]
    public void LedgerFilesAloneLeaveDoneUnmeasuredAndKeepOpenTypesStale()
    {
        var request = Write(TwoTypes(), [("alpha", 3), ("beta", 2)]);

        var report = Succeeded(CoverageCommands.Coverage(request));

        Assert.Equal(1, report.SchemaVersion);
        Assert.Equal("unmeasured", report.Done.Verdict);
        Assert.Equal("unmeasured", report.Done.Accounted.Verdict);
        Assert.Contains("recurring-open-unproved", report.Done.Accounted.Reasons);
        Assert.Equal(["audit-sample-absent"], report.Done.Correct.Reasons);
        Assert.Equal(["protected-set-absent"], report.Done.Stable.Reasons);
        Assert.Equal(new CoverageMechanismCount(1, 0, 1), report.Open.ByMechanism["compounding"]);
        Assert.Equal(new CoverageTally(1, 2), report.Open.Recurring);
        Assert.Equal(Revision, report.Stamp.GrammarRevision);
        Assert.Equal("t@1", report.Stamp.TokenizerId);
        Assert.Equal(64, report.Stamp.LedgerSha256.Length);
        Assert.Equal(64, report.Stamp.FrequencySha256.Length);
    }

    [Fact]
    public void ACurrentCheckThatProvesARecurringTypeOpenFailsAccounted()
    {
        var request = Write(TwoTypes(), [("alpha", 3), ("beta", 2)]);

        var report = Succeeded(CoverageCommands.Measure(request,
            Checks(("alpha", Complete(true, Reading(1, 2))), ("beta", Complete(false)))));

        Assert.Equal("fail", report.Done.Verdict);
        Assert.Equal("fail", report.Done.Accounted.Verdict);
        Assert.Equal(["recurring-open-current"], report.Done.Accounted.Reasons);
        Assert.Equal(1, report.Done.Accounted.Counts["recurringOpenTypes"]);
        Assert.Equal(new CoverageMechanismCount(1, 1, 0), report.Open.ByMechanism["compounding"]);
        Assert.Equal(new CoverageTally(1, 3), report.ByState.Expected.Current);
        var ranked = Assert.Single(report.Open.Ranked);
        Assert.Equal(new CoverageRankedOpen("beta", 2, "compounding", false), ranked);
    }

    [Fact]
    public void EveryRecurringTypeAccountedPassesTheAccountedTestWhileTheOthersStayUnmeasured()
    {
        var request = Write([Row("alpha", 3, "Expected", Reading(1, 2)), Row("beta", 2, "Disposition", cls: "name", evidence: "a place")],
            [("alpha", 3), ("beta", 2)]);

        var report = Succeeded(CoverageCommands.Measure(request,
            Checks(("alpha", Complete(true, Reading(1, 2))), ("beta", Complete(false)))));

        Assert.Equal("pass", report.Done.Accounted.Verdict);
        Assert.Empty(report.Done.Accounted.Reasons);
        Assert.Equal("unmeasured", report.Done.Verdict);
        Assert.Equal(new CoverageTally(1, 2), report.ByState.Disposition.Current);
        Assert.Empty(report.Open.ByMechanism);
        Assert.Empty(report.Open.Ranked);
    }

    [Fact]
    public void LimitCapsTheRankedListHeaviestFirstAndKeepsTheTotal()
    {
        var request = Write([Row("a", 5, "Open"), Row("b", 4, "Open", cls: "clitic"), Row("c", 3, "Open")],
            [("a", 5), ("b", 4), ("c", 3)], limit: 2);

        var report = Succeeded(CoverageCommands.Measure(request,
            Checks(("a", Complete(false)), ("b", Complete(false)), ("c", Complete(false)))));

        Assert.Equal(["a", "b"], report.Open.Ranked.Select(item => item.Word));
        Assert.Equal(3, report.Open.RankedTotal);
        Assert.Equal(new CoverageMechanismCount(2, 2, 0), report.Open.ByMechanism["unclassified"]);
        Assert.Equal(new CoverageMechanismCount(1, 1, 0), report.Open.ByMechanism["clitic"]);
    }

    [Fact]
    public void TheTokenShareAppearsOnlyWhenAsked()
    {
        var rows = new[] { Row("a", 3, "Expected", Reading(1, 2)), Row("s", 1, "Open") };
        var frequency = new[] { ("a", 3L), ("s", 1L) };
        var checks = Checks(("a", Complete(true, Reading(1, 2))), ("s", Complete(false)));

        var without = Succeeded(CoverageCommands.Measure(Write(rows, frequency), checks));
        var with = Succeeded(CoverageCommands.Measure(Write(rows, frequency, tokens: true), checks));

        Assert.Null(without.TokenShare);
        Assert.Equal(0.25, with.TokenShare);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public void ALimitOutsideOneToTwoHundredIsRefused(int limit)
    {
        var request = Write(TwoTypes(), [("alpha", 3), ("beta", 2)], limit);

        var refusal = CoverageCommands.Measure(request, NoStore).Refusal;

        Assert.Equal(RefusalCodes.CoverageInvalidRequest, refusal?.Code);
    }

    [Fact]
    public void AFrequencyFormatOutsideTheListIsRefusedAndJsonAndHeaderFormatsRead()
    {
        var request = Write(TwoTypes(), [("alpha", 3), ("beta", 2)]);

        Assert.Equal(RefusalCodes.CoverageInvalidRequest,
            CoverageCommands.Measure(request with { FrequencyFormat = "csv" }, NoStore).Refusal?.Code);

        File.WriteAllText(request.FrequencyPath, "word\tcount\n" + Frequency(("alpha", 3), ("beta", 2)));
        Assert.True(CoverageCommands.Measure(request with { FrequencyFormat = "tsv-header" }, NoStore).Succeeded);

        var jsonPath = Path.Combine(_root, "freq.json");
        File.WriteAllText(jsonPath, "{\"alpha\":3,\"beta\":2}");
        Assert.True(CoverageCommands.Measure(request with { FrequencyPath = jsonPath }, NoStore).Succeeded);
    }

    [Fact]
    public void AMissingFileIsRefusedByNameAndAMissingSidecarHasItsOwnCode()
    {
        var request = Write(TwoTypes(), [("alpha", 3), ("beta", 2)]);

        Assert.Equal(RefusalCodes.CoverageFileUnreadable,
            CoverageCommands.Measure(request with { LedgerPath = Path.Combine(_root, "none.tsv") }, NoStore).Refusal?.Code);
        Assert.Equal(RefusalCodes.CoverageSidecarMissing,
            CoverageCommands.Measure(request with { SidecarPath = Path.Combine(_root, "none.json") }, NoStore).Refusal?.Code);
        Assert.Equal(RefusalCodes.CoverageFileUnreadable,
            CoverageCommands.Measure(request with { FrequencyPath = Path.Combine(_root, "none.tsv") }, NoStore).Refusal?.Code);
        Assert.Equal(RefusalCodes.CoverageInvalidRequest,
            CoverageCommands.Measure(request with { LedgerPath = " " }, NoStore).Refusal?.Code);
    }

    public static TheoryData<string> AllRefusalCodes()
    {
        var data = new TheoryData<string>();
        foreach (var code in Enum.GetNames<LedgerRefusalCode>()) data.Add(code);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllRefusalCodes))]
    public void EveryLedgerRefusalCodeSurfacesOnceAsARegisteredCoverageCode(string codeName)
    {
        var code = Enum.Parse<LedgerRefusalCode>(codeName);
        var good = Row("a", 1, "Unverified");
        var request = Write([good], [("a", 1)]);
        var checks = Checks(("a", Complete(false)));
        var malformedCheck = Checks(("a", new TypeCheck(CheckCompleteness.Complete, true, false, false, null, Revision)));
        var otherPin = Checks(("a", new TypeCheck(CheckCompleteness.Complete, true, false, false, [], "other-pin")));
        string Ledger(string body) => LedgerTsvReader.Header + "\n" + body + "\n";
        var good1 = Sidecar(1, 1);
        (CoverageRequest Request, Func<LedgerSidecar, CommandOutcome<StoreInputs>> Store) Case() => code switch
        {
            LedgerRefusalCode.NotUtf8 => Bytes(request, ledger: [0xFF, 0xFE, 0x41]),
            LedgerRefusalCode.EmptyFile => Bytes(request, ledger: []),
            LedgerRefusalCode.BadHeader => (Write(LedgerTsvReader.Header.Replace("tokens", "count") + "\n" + good + "\n", good1, "a\t1\n"), checks),
            LedgerRefusalCode.WrongFieldCount => (Write(Ledger(good + "\textra"), good1, "a\t1\n"), checks),
            LedgerRefusalCode.EmptyWord => (Write(Ledger(Row("", 1, "Unverified")), good1, "a\t1\n"), checks),
            LedgerRefusalCode.EmptyGrammarRevision => (Write(Ledger(Row("a", 1, "Unverified", revision: "")), good1, "a\t1\n"), checks),
            LedgerRefusalCode.BadTokens => (Write(Ledger(Row("a", 0, "Unverified")), good1, "a\t1\n"), checks),
            LedgerRefusalCode.BadState => (Write(Ledger(Row("a", 1, "Accepted")), good1, "a\t1\n"), checks),
            LedgerRefusalCode.BadExpectedReadings => (Write(Ledger(Row("a", 1, "Expected", Reading(1, 2) + "|x")), good1, "a\t1\n"), checks),
            LedgerRefusalCode.RowCountMismatch => (Write(Ledger(good), Sidecar(2, 1), "a\t1\n"), checks),
            LedgerRefusalCode.TokenSumMismatch => (Write(Ledger(good), Sidecar(1, 9), "a\t1\n"), checks),
            LedgerRefusalCode.DigestMismatch => (Write(Ledger(good),
                good1.Replace("\"rowCount\"", "\"ledgerSha256\":\"" + new string('0', 64) + "\",\"rowCount\""), "a\t1\n"), checks),
            LedgerRefusalCode.SidecarMissing => (request with { SidecarPath = Path.Combine(_root, "absent.json") }, checks),
            LedgerRefusalCode.SidecarInvalidJson => (Write(Ledger(good), "not json", "a\t1\n"), checks),
            LedgerRefusalCode.SidecarDuplicateKey => (Write(Ledger(good), "{\"a\":1,\"a\":2}", "a\t1\n"), checks),
            LedgerRefusalCode.SidecarMissingKey => (Write(Ledger(good), good1.Replace("\"scopeFilter\":\"all\",", ""), "a\t1\n"), checks),
            LedgerRefusalCode.SidecarInvalidValue => (Write(Ledger(good), good1.Replace("NFC", "NFKC"), "a\t1\n"), checks),
            LedgerRefusalCode.FrequencyMalformed => (Write(Ledger(good), good1, "a\tmany\n"), checks),
            LedgerRefusalCode.CheckMalformed => (request, malformedCheck),
            LedgerRefusalCode.CheckPinMismatch => (request, otherPin),
            _ => throw new ArgumentOutOfRangeException(nameof(code)),
        };
        var (run, store) = Case();

        var refusal = CoverageCommands.Measure(run, store).Refusal;

        Assert.NotNull(refusal);
        Assert.StartsWith("coverage.", refusal.Code, StringComparison.Ordinal);
        Assert.Equal(CoverageCommands.CodeFor(code), refusal.Code);
        Assert.True(RefusalCatalog.Specs.ContainsKey(refusal.Code), refusal.Code);
    }

    [Fact]
    public void EveryLedgerRefusalCodeMapsToADistinctRegisteredCode()
    {
        var codes = Enum.GetValues<LedgerRefusalCode>().Select(CoverageCommands.CodeFor).ToArray();

        Assert.Equal(codes.Length, codes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(codes, code => Assert.True(RefusalCatalog.Specs.ContainsKey(code), code));
    }

    [Fact]
    public void ARowRefusalNamesItsLineNumber()
    {
        var request = Write(LedgerText(Row("a", 1, "Unverified"), Row("b", 0, "Unverified")), Sidecar(2, 1), "a\t1\n");

        var refusal = CoverageCommands.Measure(request, NoStore).Refusal;

        Assert.Equal(RefusalCodes.CoverageBadTokens, refusal?.Code);
        Assert.Equal("3", refusal?.Facts?["row"]);
    }

    private (CoverageRequest, Func<LedgerSidecar, CommandOutcome<StoreInputs>>) Bytes(CoverageRequest request, byte[] ledger)
    {
        File.WriteAllBytes(request.LedgerPath, ledger);
        return (request, NoStore);
    }
    [Fact]
    public void ARefusalNamesTheFileAtFaultAndAsksForTheNextStep()
    {
        var good = Row("a", 1, "Unverified");
        var ledger = LedgerText(good);
        var badHeader = LedgerTsvReader.Header.Replace("tokens", "count") + "\n" + good + "\n";

        void Check(CoverageRequest run, string file, string hint)
        {
            var message = CoverageCommands.Measure(run, NoStore).Refusal!.Message;
            Assert.True(message.Contains(file, StringComparison.Ordinal), message);
            Assert.True(message.Contains("Next:", StringComparison.Ordinal), message);
            Assert.True(message.Contains(hint, StringComparison.OrdinalIgnoreCase), message);
        }

        var badLedger = Write(badHeader, Sidecar(1, 1), "a\t1\n");
        Check(badLedger, badLedger.LedgerPath, "ledger");
        var badSidecar = Write(ledger, "not json", "a\t1\n");
        Check(badSidecar, badSidecar.SidecarPath, "sidecar");
        var staleSidecar = Write(ledger, Sidecar(2, 1), "a\t1\n");
        Check(staleSidecar, staleSidecar.SidecarPath, "regenerate the sidecar");
        var badFrequency = Write(ledger, Sidecar(1, 1), "a\tmany\n");
        Check(badFrequency, badFrequency.FrequencyPath, "frequency");
    }

    [Fact]
    public void AMistypedAuditDirectoryOrProtectedSetIsRefusedByNameButAbsenceStaysUnmeasured()
    {
        var request = Write(TwoTypes(), [("alpha", 3), ("beta", 2)]);
        var missingAudit = Path.Combine(_root, "audti");
        var missingProtected = Path.Combine(_root, "protcted.tsv");

        var audit = CoverageCommands.Measure(request with { AuditDirectory = missingAudit }, NoStore).Refusal;
        var protectedSet = CoverageCommands.Measure(request with { ProtectedSetPath = missingProtected }, NoStore).Refusal;

        Assert.Equal(RefusalCodes.CoverageFileUnreadable, audit?.Code);
        Assert.Contains(missingAudit, audit!.Message, StringComparison.Ordinal);
        Assert.Equal(RefusalCodes.CoverageFileUnreadable, protectedSet?.Code);
        Assert.Contains(missingProtected, protectedSet!.Message, StringComparison.Ordinal);
        Assert.True(CoverageCommands.Measure(request, NoStore).Succeeded);
    }

    [Fact]
    public void OpenByMechanismIsCappedAtTheTopMechanismsWithAnOtherBucketAndTheTotal()
    {
        var count = CoverageCommands.MechanismCap + 5;
        var rows = Enumerable.Range(0, count).Select(n => Row("w" + n, 2, "Open", cls: "m" + n.ToString("D3"))).ToArray();
        var request = Write(rows, Enumerable.Range(0, count).Select(n => ("w" + n, 2L)).ToArray());

        var open = Succeeded(CoverageCommands.Measure(request, NoStore)).Open;

        Assert.Equal(CoverageCommands.MechanismCap, open.ByMechanism.Count);
        Assert.Equal(count, open.MechanismTotal);
        Assert.Equal(new CoverageMechanismCount(5, 0, 5), open.OtherMechanisms);
        Assert.Equal(["m000", "m001"], open.ByMechanism.Keys.Take(2));
    }
}

using System.Text;
using SIL.Motif.Worker.Corpus.Ledger;
using Xunit;
using static SIL.Motif.Tests.Worker.Corpus.LedgerFixtures;

namespace SIL.Motif.Tests.Worker.Corpus;

public sealed class LedgerAdapterTests
{
    [Fact]
    public void ReadsRowsExactlyWithBomCrlfAndAbsentOptionalFields()
    {
        var text = LedgerTsvReader.Header + "\r\n" + Row(" Spaced ", 3, "Disposition", "", "name", "e", "prov") + "\r\n" +
            Row("b", 1, "Unverified") + "\r\n";
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(text)).ToArray();

        var file = LedgerTsvReader.Read(bytes);

        Assert.Equal(2, file.Rows.Count);
        Assert.Equal(4, file.TokenSum);
        Assert.Equal(" Spaced ", file.Rows[0].Word);
        Assert.Equal(LedgerState.Disposition, file.Rows[0].State);
        Assert.Equal("name", file.Rows[0].Class);
        Assert.Null(file.Rows[1].Class);
        Assert.Null(file.Rows[1].Evidence);
        Assert.Equal(2, file.Rows[0].RowNumber);
        Assert.Equal(LedgerTsvReader.Sha256(bytes), file.Digest);
    }

    [Theory]
    [InlineData("header", "BadHeader")]
    [InlineData("fields", "WrongFieldCount")]
    [InlineData("word", "EmptyWord")]
    [InlineData("revision", "EmptyGrammarRevision")]
    [InlineData("zero", "BadTokens")]
    [InlineData("plus", "BadTokens")]
    [InlineData("space", "BadTokens")]
    [InlineData("state", "BadState")]
    [InlineData("lower", "BadState")]
    [InlineData("blank", "WrongFieldCount")]
    [InlineData("extraDelimiter", "BadExpectedReadings")]
    [InlineData("zeroGuid", "BadExpectedReadings")]
    [InlineData("emptyMorph", "BadExpectedReadings")]
    [InlineData("badFeature", "BadExpectedReadings")]
    public void RefusesMalformedLedgersWithATypedReason(string kind, string expectedName)
    {
        var good = Row("a", 1, "Unverified");
        var rows = kind switch
        {
            "header" => LedgerTsvReader.Header.Replace("tokens", "count") + "\n" + good + "\n",
            "fields" => LedgerTsvReader.Header + "\n" + good + "\textra\n",
            "word" => LedgerTsvReader.Header + "\n" + Row("", 1, "Unverified") + "\n",
            "revision" => LedgerTsvReader.Header + "\n" + Row("a", 1, "Unverified", revision: "") + "\n",
            "zero" => LedgerTsvReader.Header + "\n" + Row("a", 0, "Unverified") + "\n",
            "plus" => LedgerTsvReader.Header + "\n" + good.Replace("\t1\t", "\t+1\t") + "\n",
            "space" => LedgerTsvReader.Header + "\n" + good.Replace("\t1\t", "\t1 \t") + "\n",
            "state" => LedgerTsvReader.Header + "\n" + Row("a", 1, "Accepted") + "\n",
            "lower" => LedgerTsvReader.Header + "\n" + Row("a", 1, "expected") + "\n",
            "blank" => LedgerTsvReader.Header + "\n" + good + "\n\n" + good + "\n",
            "extraDelimiter" => LedgerTsvReader.Header + "\n" + Row("a", 1, "Expected", Reading(1, 2) + "|x") + "\n",
            "zeroGuid" => LedgerTsvReader.Header + "\n" + Row("a", 1, "Expected", "AAAAAAAAAAAAAAAAAAAAAA/" + Id(2) + "/" + Id(3) + "|" + Id(4) + "|") + "\n",
            "emptyMorph" => LedgerTsvReader.Header + "\n" + Row("a", 1, "Expected", $"{Id(1)}/{Id(2)}/{Id(3)}|{Id(4)},|") + "\n",
            _ => LedgerTsvReader.Header + "\n" + Row("a", 1, "Expected", Reading(1, 2, $"{Id(9)}={Id(10)}={Id(11)}")) + "\n",
        };

        var exception = Assert.Throws<LedgerRefusalException>(() => LedgerTsvReader.Read(Encoding.UTF8.GetBytes(rows)));

        Assert.Equal(Enum.Parse<LedgerRefusalCode>(expectedName), exception.Code);
    }

    [Fact]
    public void RefusesInvalidUtf8AndAnEmptyFile()
    {
        Assert.Equal(LedgerRefusalCode.NotUtf8,
            Assert.Throws<LedgerRefusalException>(() => LedgerTsvReader.Read([0xFF, 0xFE, 0x41])).Code);
        Assert.Equal(LedgerRefusalCode.EmptyFile, Assert.Throws<LedgerRefusalException>(() => LedgerTsvReader.Read([])).Code);
    }

    [Fact]
    public void CanonicalizesExpectedReadingsAndCountsRepeatedFeaturePairsOnce()
    {
        var guid = new Guid(5, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1).ToString();
        var cell = $"{guid}/{Id(6)}/|{Id(7)},{Id(8)}|{Id(11)}={Id(12)},{Id(9)}={Id(10)},{Id(11)}={Id(12)}";
        var row = Assert.Single(LedgerTsvReader.Read(Tsv(Row("a", 1, "Expected", cell + ";" + Reading(1, 2)))).Rows);

        Assert.Equal(2, row.ExpectedReadings.Count);
        var first = row.ExpectedReadings[0];
        Assert.Equal(Id(5), first.EntryId);
        Assert.Null(first.MsaId);
        Assert.Equal(new[] { $"{Id(11)}={Id(12)}", $"{Id(9)}={Id(10)}" }.Order(StringComparer.Ordinal), first.Features);
        Assert.Equal(2, first.MorphIds.Count);
    }

    [Fact]
    public void ReadsTheTenSidecarKeysAndIgnoresExtraOnes()
    {
        var sidecar = LedgerSidecarReader.Read(Encoding.UTF8.GetBytes(
            SidecarJson(3, 9, fold: "lower+apostrophe", normalization: "NFD", extra: "\"conversionAudit\":{\"a\":1}")));

        Assert.Equal("lower+apostrophe", sidecar.Fold);
        Assert.Equal("NFD", sidecar.Normalization);
        Assert.Equal(["s1", "s2"], sidecar.SourceIds);
        Assert.Equal(3, sidecar.RowCount);
        Assert.Equal(9, sidecar.TokenSum);
    }

    [Theory]
    [InlineData("{\"a\":1,\"a\":2}", "SidecarDuplicateKey")]
    [InlineData("not json", "SidecarInvalidJson")]
    [InlineData("[]", "SidecarInvalidJson")]
    [InlineData("missing", "SidecarMissingKey")]
    [InlineData("fold", "SidecarInvalidValue")]
    [InlineData("normalization", "SidecarInvalidValue")]
    [InlineData("sha", "SidecarInvalidValue")]
    [InlineData("count", "SidecarInvalidValue")]
    [InlineData("fractional", "SidecarInvalidValue")]
    [InlineData("duplicateSource", "SidecarInvalidValue")]
    [InlineData("emptyScope", "SidecarInvalidValue")]
    [InlineData("nestedDuplicate", "SidecarDuplicateKey")]
    public void RefusesAMalformedSidecarWithATypedReason(string kind, string expectedName)
    {
        var good = SidecarJson(1, 1);
        var json = kind switch
        {
            "missing" => good.Replace("\"scopeFilter\":\"all\",", ""),
            "fold" => SidecarJson(1, 1, fold: "upper"),
            "normalization" => SidecarJson(1, 1, normalization: "NFKC"),
            "sha" => good.Replace(Pin, "xyz"),
            "count" => good.Replace("\"rowCount\":1", "\"rowCount\":-1"),
            "fractional" => good.Replace("\"rowCount\":1", "\"rowCount\":1.5"),
            "duplicateSource" => good.Replace("[\"s1\",\"s2\"]", "[\"s1\",\"s1\"]"),
            "emptyScope" => good.Replace("\"all\"", "[]"),
            "nestedDuplicate" => SidecarJson(1, 1, extra: "\"x\":{\"k\":1,\"k\":2}"),
            _ => kind,
        };

        Assert.Equal(Enum.Parse<LedgerRefusalCode>(expectedName), Assert.Throws<LedgerRefusalException>(() => LedgerSidecarReader.Read(Encoding.UTF8.GetBytes(json))).Code);
    }

    [Fact]
    public void AcceptsStructuredScopeFilters()
    {
        var sidecar = LedgerSidecarReader.Read(Encoding.UTF8.GetBytes(
            SidecarJson(1, 1).Replace("\"all\"", "{\"split\":\"bible_h\",\"books\":[\"RUT\"]}")));

        Assert.Contains("bible_h", sidecar.ScopeFilter);
    }

    [Fact]
    public void ChecksRowCountTokenSumAndTheDeclaredLedgerDigestBeforeAnyJoin()
    {
        var ledger = LedgerTsvReader.Read(Tsv(Row("a", 2, "Unverified"), Row("b", 3, "Unverified")));

        LedgerSidecarReader.Validate(Sidecar(2, 5), ledger, ledger.Digest);
        Assert.Equal(LedgerRefusalCode.RowCountMismatch,
            Assert.Throws<LedgerRefusalException>(() => LedgerSidecarReader.Validate(Sidecar(3, 5), ledger)).Code);
        Assert.Equal(LedgerRefusalCode.TokenSumMismatch,
            Assert.Throws<LedgerRefusalException>(() => LedgerSidecarReader.Validate(Sidecar(2, 6), ledger)).Code);
        Assert.Equal(LedgerRefusalCode.DigestMismatch,
            Assert.Throws<LedgerRefusalException>(() => LedgerSidecarReader.Validate(Sidecar(2, 5), ledger, "00")).Code);
    }
}

using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Composers;
using Xunit;

namespace SIL.Motif.Tests.Composers;

public sealed class SetEntryAttributionParserTests
{
    private const string Tag = "lang-sme@9ba7be7 GPL-3.0";

    [Fact]
    public void AnEntryListReadsIdsAndGuidsSkipsBlankAndCommentLinesAndKeepsLineNumbers()
    {
        var guid = Guid.NewGuid();
        var id = CanonicalId.Mint();
        var list = $"# entries\n\n{guid:D}\r\n  {id.Value}  \n";

        var intent = SetEntryAttributionIntentParser.Parse(SetEntryAttributionIntentParser.FromList(list, Tag, "en"));

        Assert.Equal(Tag, intent.Tag.Text);
        Assert.Equal("en", intent.AnalysisWritingSystem);
        Assert.Equal([CanonicalId.FromGuid(guid), id], intent.Entries.Select(entry => entry.Entry));
        Assert.Equal(["line 3", "line 4"], intent.Entries.Select(entry => entry.At));
    }

    [Fact]
    public void UnreadableLinesAreAllReportedWithTheirLineNumbers()
    {
        var failure = Assert.Throws<ContractParseException>(() =>
            SetEntryAttributionIntentParser.FromList($"{Guid.NewGuid():D}\nnot-an-entry\n\nalso bad\n", Tag));

        Assert.Contains("2 problem(s)", failure.Message, StringComparison.Ordinal);
        Assert.Contains("line 2: 'not-an-entry'", failure.Message, StringComparison.Ordinal);
        Assert.Contains("line 4: 'also bad'", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyListAndAnUnreadableTagAreRefused()
    {
        Assert.Throws<ContractParseException>(() => SetEntryAttributionIntentParser.Parse(
            SetEntryAttributionIntentParser.FromList("# nothing\n", Tag)));
        Assert.Throws<ContractParseException>(() => SetEntryAttributionIntentParser.Parse(
            SetEntryAttributionIntentParser.FromList($"{Guid.NewGuid():D}\n", "no tag")));
    }

    [Fact]
    public void ARepeatedEntryAndAnUnknownPropertyAreRefused()
    {
        var id = CanonicalId.Mint().Value;
        var repeated = JsonSerializer.SerializeToElement(new
        { tag = Tag, entries = new[] { new { entry = id, at = "line 1" }, new { entry = id, at = "line 2" } } });
        var failure = Assert.Throws<ContractParseException>(() => SetEntryAttributionIntentParser.Parse(repeated));
        Assert.Contains("entry 2 (line 2) repeats entry 1 (line 1)", failure.Message, StringComparison.Ordinal);
        Assert.Throws<ContractParseException>(() => SetEntryAttributionIntentParser.Parse(
            JsonSerializer.SerializeToElement(new { tag = Tag, entries = new[] { new { entry = id } }, extra = 1 })));
    }
}

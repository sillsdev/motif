using System.Text;
using SIL.Motif.Cli.Rendering;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Query;
using SIL.Motif.Contract.Query.Phon;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class PhonTextTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("phon-text").FullName;

    public void Dispose() => Directory.Delete(_folder, true);

    [Fact]
    public void TheHeaderAlwaysStatesTheUnsegmentableAndIncompleteCountsEvenAtZero()
    {
        var text = new StringBuilder();
        PhonText.Append(text, new PhonReport(24103, 0, [], 0, [], true, null, "0.28.1", 1));
        var header = text.ToString();
        Assert.Contains("Unsegmentable: 0", header);
        Assert.Contains("Incomplete: 0", header);
    }

    [Fact]
    public void ACountAboveZeroKeepsItsSampleLine()
    {
        var text = new StringBuilder();
        PhonText.Append(text, new PhonReport(10, 2, [new QuerySkippedWord("x1", "no")], 1, [new QuerySkippedWord("y", "stop")], false, "deadline", null, 1));
        var header = text.ToString();
        Assert.Contains("UNSEGMENTABLE: 2", header);
        Assert.Contains("INCOMPLETE: 1", header);
        Assert.DoesNotContain("Unsegmentable: 0", header);
    }

    [Fact]
    public void TheValuesListLeadsWithTheTokenToTypeAndBracketsANameWithSpaces()
    {
        var response = new PhonValuesResponse(PhonValuesResponse.SchemaName, "grammar",
            [new PhonValue("V", null, null, "V", "segment"), new PhonValue("Exact featured V", null, null, "[Exact featured V]", "feature"), new PhonValue("hidden", null, null, null, "segment")],
            [new PhonValue("voice", null, Values: ["+", "-"])], "note");
        var text = PhonText.Values(response);
        Assert.Contains("  V  V  segment", text);
        Assert.Contains("  [Exact featured V]  Exact featured V  feature", text);
        Assert.Contains("  (none)  hidden  segment  (no spelling reaches this class)", text);
        Assert.Contains("  voice  + -", text);
    }
}

using System.IO;
using System.Linq;
using SIL.Motif.Contract.Gdr;
using SIL.Motif.Generator;
using Xunit;

namespace SIL.Motif.Tests.Contract;

public class GdrTemplateTests
{
    [Fact]
    public void HeadingsFollowTheOrderStatedInAdr0005()
    {
        var adr = File.ReadAllText(Path.Combine(RepoPaths.FindRepoRoot(), "docs", "adr", "0005-the-grammar-guide.md"));
        var d2 = adr[adr.IndexOf("### D2.", System.StringComparison.Ordinal)..adr.IndexOf("### D3.", System.StringComparison.Ordinal)];
        var flat = string.Join(' ', d2.Split('\n').Select(l => l.Trim()));
        var start = flat.IndexOf("The body's parts, in order, are", System.StringComparison.Ordinal);
        Assert.True(start >= 0);
        var sentence = flat[start..flat.IndexOf('.', start)];
        var found = GdrTemplate.Headings
            .Select(h => (heading: h, at: sentence.IndexOf("**" + h + "**", System.StringComparison.Ordinal)))
            .ToList();

        Assert.All(found, f => Assert.True(f.at >= 0, f.heading));
        Assert.Equal(found.OrderBy(f => f.at).Select(f => f.heading), GdrTemplate.Headings);
        Assert.Equal(sentence.Split("**").Length / 2, GdrTemplate.Headings.Count);
    }

    [Fact]
    public void BlockTagsAreDocumentedInAdr0005()
    {
        var adr = File.ReadAllText(Path.Combine(RepoPaths.FindRepoRoot(), "docs", "adr", "0005-the-grammar-guide.md"));
        Assert.Contains("```" + GdrTemplate.QueryBlockTag, adr);
        Assert.Contains("```" + GdrTemplate.ShowBlockTag, adr);
    }
}

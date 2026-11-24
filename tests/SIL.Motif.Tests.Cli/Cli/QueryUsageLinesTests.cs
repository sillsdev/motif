using System.Text.RegularExpressions;
using SIL.Motif.Cli;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>Every <c>query</c> usage line the catalog prints for a command that needs no project must reach its own command, not the usage text.</summary>
public sealed class QueryUsageLinesTests : IDisposable
{
    private static readonly string[] NoProjectCommands = ["query pairs", "query values", "query corpus forms"];

    private readonly string _folder = Directory.CreateTempSubdirectory("query-usage").FullName;

    public void Dispose() => Directory.Delete(_folder, true);

    private const string Grammar = """
        <?xml version="1.0" encoding="utf-8"?>
        <languageproject version="7000072">
        <rt class="PhNCSegments" guid="a" ownerguid="p"><Abbreviation><AUni ws="en">V</AUni></Abbreviation><Name><AUni ws="en">Vowels</AUni></Name></rt>
        <rt class="LangProject" guid="lp"><AnalysisWss><Uni>en</Uni></AnalysisWss></rt>
        </languageproject>
        """;

    private string Path_(string name, string text)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, text);
        return path;
    }

    // Usage line to argv: every alternative of ( a | b ) and of word|word, with optional parts dropped or kept.
    private static IEnumerable<string[]> Expand(string line, bool withOptional)
    {
        var tokens = Regex.Matches(line, @"<[^>]*>|[()\[\]]|\||[^\s()\[\]|<]+(?:\|[^\s()\[\]|<]+)*").Select(match => match.Value).ToList();
        var position = 0;
        return Sequence(tokens, ref position, withOptional).Select(words => words.ToArray());
    }

    private static List<List<string>> Sequence(List<string> tokens, ref int position, bool withOptional)
    {
        var alternatives = new List<List<string>>();
        var current = new List<List<string>> { new() };
        while (position < tokens.Count && tokens[position] is not (")" or "]"))
        {
            var token = tokens[position++];
            if (token == "|")
            {
                alternatives.AddRange(current);
                current = [[]];
            }
            else if (token == "(")
            {
                var inner = Sequence(tokens, ref position, withOptional);
                position++;
                current = [.. current.SelectMany(prefix => inner.Select(choice => (List<string>)[.. prefix, .. choice]))];
            }
            else if (token == "[")
            {
                var inner = Sequence(tokens, ref position, withOptional);
                position++;
                if (withOptional) current = [.. current.SelectMany(prefix => inner.Take(1).Select(choice => (List<string>)[.. prefix, .. choice]))];
            }
            else
            {
                var options = token.StartsWith('<') ? [token] : token.Split('|');
                current = [.. current.SelectMany(prefix => options.Select(option => (List<string>)[.. prefix, option]))];
            }
        }
        alternatives.AddRange(current);
        return alternatives;
    }

    private string[] Fill(string[] words)
    {
        var corpus = Path_("words.tsv", "form\tcount\n" + string.Concat(new[] { ("kaon", 5), ("kaonan", 3) }.Select(row => row.Item1 + "\t" + row.Item2 + "\n")));
        var grammar = Path_("grammar.fwdata", Grammar);
        var json = Path_("query.json", "{}");
        return [.. words.Select(word => word switch
        {
            "<words.tsv|folder>" => corpus,
            "<file|->" => json,
            "<expression>" => "form ~ \"k.*\"",
            "<path>" or "<fwdata>" => grammar,
            "<regex>" => "an",
            "<x1,x2>" => "an,in",
            "<file>" => Path_("seg.txt", "ng"),
            "<n>" or "<1-12>" => "3",
            _ => word,
        })];
    }

    public static IEnumerable<object[]> Lines() =>
        CliVerbCatalog.All.Where(verb => NoProjectCommands.Contains(verb.CommandName)).SelectMany(verb => verb.UsageLines)
            .SelectMany(line => new[] { false, true }.Select(withOptional => new object[] { line, withOptional }));

    [Theory]
    [MemberData(nameof(Lines))]
    public void EveryAdvertisedFormRunsAndIsNotAnsweredWithUsage(string line, bool withOptional)
    {
        var usageLines = CliVerbCatalog.All.SelectMany(verb => verb.UsageLines).ToHashSet();
        var ran = 0;
        foreach (var words in Expand(line, withOptional))
        {
            if (words.Contains("--project") && !line.StartsWith("query corpus forms", StringComparison.Ordinal)) continue;
            var output = new StringWriter();
            var error = new StringWriter();
            var args = Fill(words);
            // A flag whose value is left out by an unfilled placeholder is a bug in this test, not in the catalog.
            Assert.DoesNotContain(args, argument => argument.StartsWith('<'));
            SIL.Motif.Cli.Program.Run(args, output, error, new Dictionary<string, string?>(), (_, _) => { });
            Assert.DoesNotContain(usageLines, usage => error.ToString().Contains(usage, StringComparison.Ordinal));
            ran++;
        }
        Assert.True(ran > 0, line);
    }
}

using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Gdr;
using SIL.Motif.Contract.Query;
using SIL.Motif.Contract.Query.Corpus;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Contract.Query;

/// <summary>A v2 <c>motif query corpus forms</c> line as a Guide block's run: read-only only, with columns resolved against the v2 fields.</summary>
public sealed class CorpusRunLineTests
{
    private const string Base = "motif query corpus forms --corpus words.tsv --grammar g.fwdata ";

    private static CorpusFormsRequest Parse(string run)
    {
        Assert.True(QueryRunLine.TryParseRun(run, out var line, out var refusal), refusal?.Message);
        return Assert.IsType<CorpusFormsRequest>(line.Corpus);
    }

    [Theory]
    [InlineData("forms where 'only-via(entry:00000000-0000-0000-0000-000000000001)' --project p.fwdata")]
    [InlineData("forms where 'readings > 3'")]
    [InlineData("forms --group-by cause --project p.fwdata")]
    [InlineData("forms where 'parse.state = none' --group-by initial:2,parse.state --sum tokens")]
    [InlineData("forms where 'form ~ \"(.*)ிலிருந்து$\"' --twin '$1ில்' --group-by parse.state,twin.parse.state")]
    [InlineData("forms --baseline-grammar before.fwdata --group-by change")]
    [InlineData("forms where 'parse.state = none' --sample 50 --seed 1 --limit 20 --json")]
    public void ReadOnlyV2LinesAreAccepted(string tail) => Assert.NotNull(Parse(Base + tail));

    [Fact]
    public void TheRequestCarriesTheFlagsAndRepeatedTwinsAndCorpora()
    {
        var request = Parse("motif query corpus forms --corpus a.tsv --corpus b.tsv --grammar g forms where 'form ~ \"x(.)\"' --twin '$1a' --twin '$1b' --group-by change --baseline-grammar old --limit 5");
        Assert.Equal("a.tsv\nb.tsv", request.CorpusPath);
        Assert.Equal(["$1a", "$1b"], request.Twin);
        Assert.Equal(("change", "old", 5, "g"), (request.GroupBy, request.BaselineGrammarPath, request.Limit, request.GrammarPath));
        Assert.Equal("form ~ \"x(.)\"", request.Where);
    }

    [Theory]
    [InlineData("forms where 'parse.state = none' --write-words out.tsv", "write-words")]
    [InlineData("forms --write-words out.tsv --with-counts --force", "write-words")]
    [InlineData("forms where 'readings > 1' --force", "force")]
    public void AnythingThatWritesIsRefusedAsNotReadOnly(string tail, string flag)
    {
        Assert.False(QueryRunLine.TryParseRun(Base + tail, out _, out var refusal));
        Assert.Equal(RefusalCodes.GuideQueryRunNotReadOnly, refusal!.Code);
        Assert.Equal(flag, refusal.Facts!["flag"]);
    }

    [Theory]
    [InlineData("forms where 'readings > 1' --what-if-retire entry:1")]
    [InlineData("forms --query-json -")]
    [InlineData("forms where 'readings > 1' --frobnicate x")]
    [InlineData("forms where 'readings > 1' --limit many")]
    [InlineData("where 'readings > 1'")]
    public void LinesABlockCannotShowOrThatAreMalformedAreInvalid(string tail)
    {
        Assert.False(QueryRunLine.TryParseRun(Base + tail, out _, out var refusal));
        Assert.Equal(RefusalCodes.GuideQueryRunInvalid, refusal!.Code);
    }

    [Fact]
    public void AMissingCorpusAndShellChainingAreRefused()
    {
        Assert.False(QueryRunLine.TryParseRun("motif query corpus forms forms where 'readings > 1'", out _, out var none));
        Assert.Equal(RefusalCodes.GuideQueryRunInvalid, none!.Code);
        Assert.False(QueryRunLine.TryParseRun(Base + "forms > out.txt", out _, out var redirect));
        Assert.Equal(RefusalCodes.GuideQueryRunNotReadOnly, redirect!.Code);
    }

    [Fact]
    public void RelativePathsResolveAgainstTheProjectFolderAndAbsoluteOnesStay()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "proj"));
        var absolute = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "abs.tsv"));
        var request = CorpusRunLine.Resolve(Parse($"motif query corpus forms --corpus words.tsv,{absolute} --grammar g/g.fwdata --baseline-grammar ../old.fwdata forms"), root);
        Assert.Equal(Path.Combine(root, "words.tsv") + "\n" + absolute, request.CorpusPath);
        Assert.Equal(Path.Combine(root, "g", "g.fwdata"), request.GrammarPath);
        Assert.Equal(Path.GetFullPath(Path.Combine(root, "..", "old.fwdata")), request.BaselineGrammarPath);
    }

    [Fact]
    public void ColumnsResolveAgainstTheV2FieldsOfTheQuery()
    {
        var rows = Parse(Base + "forms where 'readings > 1' --baseline-grammar old --twin '$1x'");
        Assert.Equal(["form", "tokens", "parse.state", "readings", "base.parse.state", "base.readings", "change",
            "twin.form", "twin.tokens", "twin.inCorpus", "twin.parse.state", "twin.readings"], GuideEvidenceTable.ColumnsFor(rows));
        Assert.Equal(["cause", "types", "tokens", "benign", "example"], GuideEvidenceTable.ColumnsFor(Parse(Base + "forms --group-by cause")));
        Assert.Equal(["parse.state", "initial:2", "types", "tokens"], GuideEvidenceTable.ColumnsFor(Parse(Base + "forms --group-by parse.state,initial:2")));
        Assert.Equal(["twin1.form"], GuideEvidenceTable.Unknown(Parse(Base + "forms --twin '$1x'"), ["twin1.form", "form"]));
        Assert.Empty(GuideEvidenceTable.Unknown(Parse(Base + "forms --twin '$1x' --twin '$1y'"), ["twin1.form", "twin2.parse.state"]));
    }

    private const string Block = "```motif-query\nid: evidence\ntitle: Evidence\nrun: {0}\ncolumns: {1}\nsort: {2}\n```";

    [Fact]
    public void AnUnknownColumnOrSortIsABlockDiagnosticNotACrash()
    {
        var blocks = GuideQueryBlocks.Parse(string.Format(Block, Base + "forms --group-by parse.state", "parse.state, types, wombat", "frog desc"));
        var unknown = blocks.Diagnostics.Where(item => item.Kind == GuideBlockDiagnosticKind.UnknownColumn).ToList();
        Assert.Equal(["columns", "sort"], unknown.Select(item => item.Field));
        Assert.Contains("wombat", unknown[0].Message);
        Assert.Contains("frog", unknown[1].Message);
        Assert.Single(blocks.Queries);
    }

    [Fact]
    public void KnownColumnsAndShowColumnsAreChecked()
    {
        var ok = GuideQueryBlocks.Parse(string.Format(Block, Base + "forms --group-by cause", "cause, types, tokens", "tokens desc"));
        Assert.Empty(ok.Diagnostics);
        var show = GuideQueryBlocks.Parse(string.Format(Block, Base + "forms where 'readings > 1'", "form, tokens", "tokens desc") +
            "\n```motif-show\nquery: evidence\ncolumns: form, nope\n```");
        Assert.Single(show.Diagnostics, item => item.Kind == GuideBlockDiagnosticKind.UnknownColumn);
    }

    [Fact]
    public void ARunLineThatWritesIsAnInvalidRunDiagnostic()
    {
        var blocks = GuideQueryBlocks.Parse(string.Format(Block, Base + "forms --write-words x.tsv", "form", "tokens"));
        var run = Assert.Single(blocks.Diagnostics, item => item.Kind == GuideBlockDiagnosticKind.InvalidRun);
        Assert.Equal(RefusalCodes.GuideQueryRunNotReadOnly, run.RefusalCode);
    }

    private static CorpusFormsResponse Response(ReadingsReport? info, IReadOnlyList<CorpusFormRow>? rows = null, int matched = 5) =>
        new(CorpusFormsResponse.SchemaName, new CorpusSummary("c", 5, 10, "none", false), null, matched, rows ?? [new CorpusFormRow("kaon", 7, "unique", null, null, 1)],
            null, ReadingsInfo: info, Totals: new TypesTokens(matched, 99));

    [Fact]
    public void AnIncompleteParseSaysAtLeastAndWhyInTheHeadlineAndCount()
    {
        var request = Parse(Base + "forms where 'readings > 0'");
        var info = new ReadingsReport(false, 5, 2, 1, 0, 0, 0, "unavailable", null);
        var table = GuideEvidenceTable.Build(request, Response(info), null, null, null);
        Assert.False(table.Complete);
        Assert.StartsWith("at least 5 word types (at least 99 tokens)", table.Headline);
        Assert.Contains("2 words stopped at the parser's work limit", table.Headline);
        Assert.Equal("at least 5", table.CountText);
        Assert.Equal((2, 1, 0), (table.Capped, table.TimedOut, table.Failed));
        var done = GuideEvidenceTable.Build(request, Response(new ReadingsReport(true, 5, 0, 0, 0, 0, 0, "project", null)), null, null, null);
        Assert.Equal("5 word types (99 tokens)", done.Headline);
        Assert.Equal("5", done.CountText);
    }

    [Fact]
    public void SortLimitAndTruncationApplyAfterTheCountsAreFixed()
    {
        var request = Parse(Base + "forms where 'tokens > 0'");
        var rows = new[] { new CorpusFormRow("a", 3), new CorpusFormRow("b", 20), new CorpusFormRow("c", 100) };
        var table = GuideEvidenceTable.Build(request, Response(null, rows, 3), ["form", "tokens"], new GuideSort("tokens", true), 2);
        Assert.Equal(["c", "b"], table.Rows.Select(row => row[0]));
        Assert.Equal(1, table.Truncated);
        Assert.Equal(3, table.Types);
    }

    [Theory]
    [InlineData("forms where \"form ~ $(curl evil)\"")]
    [InlineData("forms where \"form ~ `id`\"")]
    [InlineData("forms where \"form ~ a\nb\"")]
    public void ShellSyntaxInsideDoubleQuotesIsRefused(string tail)
    {
        Assert.False(QueryRunLine.TryParseRun(Base + tail, out _, out var refusal));
        Assert.Equal(RefusalCodes.GuideQueryRunNotReadOnly, refusal!.Code);
    }

    [Fact]
    public void SingleQuotesStillHoldADollarAnchor() => Assert.NotNull(Parse(Base + "forms where 'form ~ \"x$\"'"));

    [Theory]
    [InlineData("--corpus /etc/passwd --grammar g", "corpus")]
    [InlineData("--corpus a.tsv --grammar ../g", "grammar")]
    [InlineData("--corpus a.tsv --project /p/q.fwdata", "project")]
    [InlineData("--corpus a.tsv --baseline-grammar x/../../b", "baseline-grammar")]
    [InlineData(@"--corpus C:\data\a.tsv", "corpus")]
    [InlineData(@"--corpus \\server\share\a.tsv", "corpus")]
    [InlineData(@"--corpus a.tsv --grammar ..\g", "grammar")]
    public void PathsThatLeaveTheProjectFolderAreRefused(string flags, string flag)
    {
        var request = Parse("motif query corpus forms " + flags + " forms");
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "proj"));
        Assert.False(CorpusRunLine.TryResolve(request, root, out _, out var refusal));
        Assert.Equal(RefusalCodes.GuideQueryRunPathOutsideProject, refusal!.Code);
        Assert.Equal(flag, refusal.Facts["flag"]);
    }

    [Fact]
    public void PathsInsideTheProjectFolderResolve()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "proj"));
        Assert.True(CorpusRunLine.TryResolve(Parse(Base.Replace("words.tsv", "sub/../words.tsv") + "forms"), root, out var resolved, out _));
        Assert.Equal(Path.Combine(root, "words.tsv"), resolved!.CorpusPath);
    }

    [Fact]
    public void TheRefusalNamesThePathAndSaysOnlyProjectFilesAreRead()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "proj"));
        Assert.False(CorpusRunLine.TryResolve(Parse("motif query corpus forms --corpus /etc/passwd forms"), root, out _, out var refusal));
        Assert.Contains("/etc/passwd", refusal!.Message);
        Assert.Contains("only files inside the project folder", refusal.Message);
    }

    [Fact]
    public void ABackslashRelativePathInsideTheFolderIsAccepted()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "proj"));
        Assert.True(CorpusRunLine.TryResolve(Parse(@"motif query corpus forms --corpus sub\words.tsv forms"), root, out _, out _));
    }

    [Fact]
    public void ASymlinkInsideTheFolderThatPointsOutsideIsRefused()
    {
        var parent = Path.Combine(Path.GetTempPath(), "motif487-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(parent, "proj");
        var outside = Path.Combine(parent, "outside");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "secret.tsv"), "x");
        try
        {
            try
            {
                Directory.CreateSymbolicLink(Path.Combine(root, "link"), outside);
                File.CreateSymbolicLink(Path.Combine(root, "file.tsv"), Path.Combine(outside, "secret.tsv"));
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { return; }
            foreach (var flags in new[] { "--corpus link/secret.tsv", "--corpus file.tsv" })
            {
                Assert.False(CorpusRunLine.TryResolve(Parse("motif query corpus forms " + flags + " forms"), root, out _, out var refusal));
                Assert.Equal(RefusalCodes.GuideQueryRunPathOutsideProject, refusal!.Code);
            }
        }
        finally { Directory.Delete(parent, true); }
    }
}

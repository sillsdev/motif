using System.Diagnostics;
using System.Text;
using Microsoft.Data.Sqlite;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Dispositions;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary><c>wordform dispose --from-file</c>: the three formats, per-row refusals, one transaction, dry run and scale.</summary>
public sealed class WordTypeDispositionFileTests : IDisposable
{
    private const string LedgerHeader = "word\ttokens\tstate\texpected_readings\tclass\tevidence\tprovenance\tgrammar_revision";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-disposition-files", Guid.NewGuid().ToString("N"));
    private readonly string _project;

    public WordTypeDispositionFileTests()
    {
        Directory.CreateDirectory(_root);
        _project = Path.Combine(_root, "files.fwdata");
        File.WriteAllText(_project, "placeholder");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private static string Lines(params string[] lines) => string.Join("\n", lines) + "\n";

    private string Write(string name, string text)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, text);
        return path;
    }

    private CommandOutcome<DisposeFromFileResponse> TryFile(string path, string? format = null, bool replace = false,
        bool dryRun = false, bool skipRefused = false, bool detail = false) =>
        WordTypeDispositionCommands.RecordFromFile(new DisposeFromFileRequest(_project, "1.0", path, format, replace, dryRun,
            skipRefused, detail, "lane"));

    private DisposeFromFileResponse Disposed(string path, string? format = null, bool replace = false, bool dryRun = false,
        bool skipRefused = false, bool detail = false)
    {
        var outcome = TryFile(path, format, replace, dryRun, skipRefused, detail);
        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        return outcome.Value!;
    }

    private void Dispose2(string form, string intendedNfc)
    {
        using var database = ProjectMotifDatabase.Open(_project);
        new SIL.Motif.Worker.Store.WordTypeDispositionRepository(database).Record([form], DispositionCategory.Misspelling, "typo", "x", "lane", false);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE WordTypeDispositions SET Intended = $i;";
        command.Parameters.AddWithValue("$i", intendedNfc);
        command.ExecuteNonQuery();
    }

    private long Rows()
    {
        using var database = ProjectMotifDatabase.Open(_project);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM WordTypeDispositions;";
        return (long)command.ExecuteScalar()!;
    }

    private WordTypeDisposition? Standing(string form)
    {
        var shown = WordTypeDispositionCommands.Show(new ShowDispositionRequest(_project, "1.0", form));
        Assert.True(shown.Succeeded, shown.Refusal?.Message);
        return shown.Value!.Current;
    }

    [Fact]
    public void ATsvWithAHeaderInAnyColumnOrderIsRecordedAndDetected()
    {
        var path = Write("d.tsv", "evidence\tform\tcategory\tintended\n" +
            "in the gazetteer\tNairobi\tname\t\n" +
            "typo in 1998\trecieve\tmisspelling\treceive\n");

        var response = Disposed(path);

        Assert.Equal(("tsv", 2, 2, true), (response.Format, response.Rows, response.Added, response.Written));
        Assert.Equal(("name", "in the gazetteer", "lane"), (Standing("Nairobi")!.Category, Standing("Nairobi")!.Evidence, Standing("Nairobi")!.RecordedBy));
        Assert.Equal("receive", Standing("recieve")!.Intended);
    }

    [Fact]
    public void ATsvWithNoHeaderReadsFormCategoryIntendedEvidence()
    {
        var response = Disposed(Write("d.tsv", "Nairobi\tname\t\ta town\nrecieve\tmisspelling\treceive\ttypo\n"));

        Assert.Equal((2, 2), (response.Rows, response.Added));
        Assert.Equal("typo", Standing("recieve")!.Evidence);
    }

    [Fact]
    public void AJsonlFileIsRecordedAndDetected()
    {
        var path = Write("d.jsonl",
            "{\"form\":\"Nairobi\",\"category\":\"name\",\"evidence\":\"a town\"}\n\n" +
            "{\"form\":\"recieve\",\"category\":\"misspelling\",\"intended\":\"receive\",\"evidence\":\"typo\"}\n");

        var response = Disposed(path);

        Assert.Equal(("jsonl", 2, 2), (response.Format, response.Rows, response.Added));
        Assert.Equal("receive", Standing("recieve")!.Intended);
    }

    [Fact]
    public void ALedgerReadsItsDispositionRowsAndLeavesTheOthers()
    {
        var path = Write("ledger.tsv", LedgerHeader + "\n" +
            "tanzania\t24320\tDisposition\t\tname\tThe country. | prior: capitalised\tledger2:37\tabc\n" +
            "kitabu\t900\tExpected\t\t\t\tp\tabc\n" +
            "machester\t7\tDisposition\t\tmisspelling\tintended=Manchester; exact spelling status verified | prior: x\tp\tabc\n" +
            "olouch\t4\tDisposition\t\tmisspelling\tintended=Oluoch | one token names the deputy\tp\tabc\n" +
            "mchezo\t9\tOpen\t\t\t\tp\tabc\n" +
            "a1\t3\tDisposition\t\tartifact\tOCR fragment\tp\tabc\n" +
            "ya\t30\tDisposition\t\tmixed\tseveral uses\tp\tabc\n");

        var response = Disposed(path, skipRefused: true, detail: true);

        Assert.Equal(("ledger", 5, 3, 2, 2), (response.Format, response.Rows, response.Added, response.Refused, response.Skipped));
        Assert.Equal([4, 8], response.Refusals.Select(r => r.Row));
        Assert.Equal("machester", response.Refusals[0].Form);
        Assert.Contains("intended", response.Refusals[0].Message, StringComparison.Ordinal);
        Assert.Null(Standing("machester"));
        Assert.Equal("one token names the deputy", Standing("olouch")!.Evidence);
        Assert.Equal("non-word", Standing("a1")!.Category);
        Assert.Equal("The country. | prior: capitalised", Standing("tanzania")!.Evidence);
        Assert.Null(Standing("kitabu"));
    }

    [Fact]
    public void ALedgerIntendedIsEverythingUpToTheBarSoAMultiWordOneIsRefusedWithItsRow()
    {
        var path = Write("ledger.tsv", LedgerHeader + "\nnewyork\t1\tDisposition\t\tmisspelling\tintended=New York | typo\tp\tabc\n");

        var response = Disposed(path);

        Assert.Equal((false, 1), (response.Written, response.Refused));
        Assert.Equal(2, response.Refusals[0].Row);
    }

    [Fact]
    public void AStoredComposedIntendedIsTheSameDispositionAsTheDecomposedOne()
    {
        Dispose2("recieve", "caf\u00e9");
        TryFile(Write("n.tsv", Lines("form\tcategory\tintended\tevidence", "recieve\tmisspelling\tcafe\u0301\ttypo")));
        var composed = Disposed(Write("c.tsv", Lines("form\tcategory\tintended\tevidence", "recieve\tmisspelling\tcaf\u00e9\ttypo")));
        var decomposed = Disposed(Write("d.tsv", Lines("form\tcategory\tintended\tevidence", "recieve\tmisspelling\tcafe\u0301\ttypo")));

        Assert.Equal((0, 0, 1, 0), (composed.Added, composed.Replaced, composed.Unchanged, composed.Refused));
        Assert.Equal((0, 0, 1, 0), (decomposed.Added, decomposed.Replaced, decomposed.Unchanged, decomposed.Refused));
        Assert.Equal(1, Rows());
    }

    [Fact]
    public void MergedEvidenceIsCutToTheLongestADispositionHolds()
    {
        TryFile(Write("a.tsv", Lines("form\tcategory\tevidence", "Nairobi\tname\t" + new string('a', 1900))));

        var more = Disposed(Write("b.tsv", Lines("form\tcategory\tevidence", "Nairobi\tname\t" + new string('b', 1900))));

        Assert.Equal((1, 1), (more.Unchanged, more.EvidenceExtended));
        var evidence = Standing("Nairobi")!.Evidence;
        Assert.Equal(WordTypeDispositionCommands.MaxTextLength, evidence.Length);
        Assert.EndsWith("...", evidence, StringComparison.Ordinal);
    }

    [Fact]
    public void LedgerEvidenceLongerThanADispositionHoldsIsCutAndCounted()
    {
        var path = Write("ledger.tsv", LedgerHeader + "\nx\t1\tDisposition\t\tname\t" + new string('e', 5000) + "\tp\tabc\n");

        var response = Disposed(path);

        Assert.Equal((1, 1), (response.Added, response.EvidenceTruncated));
        Assert.Equal(WordTypeDispositionCommands.MaxTextLength, Standing("x")!.Evidence.Length);
    }

    [Fact]
    public void RefusalsAreCollectedWithTheirLineNumbersAndNothingIsWritten()
    {
        TryFile(Write("seed.tsv", "form\tcategory\tevidence\nNairobi\tname\ta town\n"));
        var rows = Rows();
        var path = Write("d.tsv", "form\tcategory\tintended\tevidence\n" +        // 1
            "Good\tname\t\tfine\n" +                                              // 2
            "Bad\tplace\t\tfine\n" +                                              // 3 category
            "recieve\tmisspelling\t\ttypo\n" +                                     // 4 no intended
            "\tname\t\tempty form\n" +                                             // 5
            "Nairobi\tloan\t\tcontradiction\n" +                                  // 6
            "two words\tname\t\tspace\n" +                                         // 7
            "NoEvidence\tname\t\t\n" +                                            // 8
            "Other\tname\tx\tintended on a name\n");                              // 9

        var response = Disposed(path, detail: true);

        Assert.False(response.Written);
        Assert.Equal(7, response.Refused);
        Assert.Equal([3, 4, 5, 6, 7, 8, 9], response.Refusals.Select(r => r.Row));
        Assert.Equal(RefusalCodes.DispositionContradiction, response.Refusals.Single(r => r.Row == 6).Code);
        Assert.Equal(RefusalCodes.DispositionInvalid, response.Refusals.Single(r => r.Row == 3).Code);
        Assert.Contains("one of", response.Refusals.Single(r => r.Row == 3).Message, StringComparison.Ordinal);
        Assert.Equal(rows, Rows());
        Assert.Null(Standing("Good"));
    }

    [Fact]
    public void SkipRefusedWritesTheAcceptedRowsAndReportsTheRest()
    {
        var path = Write("d.tsv", Lines("form\tcategory\tevidence", "Good\tname\tfine", "Bad\tplace\tx", "Also\tloan\tborrowed"));

        var response = Disposed(path, skipRefused: true);

        Assert.Equal((true, 2, 1), (response.Written, response.Added, response.Refused));
        Assert.Equal(2, Rows());
        Assert.NotNull(Standing("Also"));
    }

    [Fact]
    public void AContradictionIsRefusedWithoutReplaceAndEndsTheStandingRowWithIt()
    {
        TryFile(Write("seed.tsv", "form\tcategory\tevidence\nNairobi\tname\ta town\n"));
        var path = Write("d.tsv", Lines("form\tcategory\tevidence", "Nairobi\tloan\tborrowed", "Kisumu\tname\ta town"));

        var refused = Disposed(path);
        Assert.Equal((false, 1), (refused.Written, refused.Refused));
        Assert.Null(Standing("Kisumu"));

        var replaced = Disposed(path, replace: true);
        Assert.Equal((true, 1, 1, 0), (replaced.Written, replaced.Added, replaced.Replaced, replaced.Refused));
        Assert.Equal("loan", Standing("Nairobi")!.Category);
    }

    [Fact]
    public void ARepeatedFormIsOneTypeAndAConflictingRepeatIsRefused()
    {
        var path = Write("d.tsv", Lines("form\tcategory\tevidence", "Aa\tname\tone", "Aa\tname\ttwo", "Bb\tname\tone", "Bb\tloan\ttwo"));

        var response = Disposed(path);

        Assert.Equal(4, response.Rows);
        Assert.Equal((false, 1), (response.Written, response.Refused));
        Assert.Equal(5, Assert.Single(response.Refusals).Row);
    }

    [Fact]
    public void ADryRunReportsTheCountsAndWritesNothingEvenWithNoStore()
    {
        var path = Write("d.tsv", Lines("form\tcategory\tevidence", "Nairobi\tname\ta town", "Kisumu\tname\ta town"));

        var cold = Disposed(path, dryRun: true);
        Assert.Equal((true, false, 2), (cold.DryRun, cold.Written, cold.Added));
        Assert.Null(Standing("Nairobi"));

        Disposed(path);
        var rows = Rows();
        var again = Disposed(Write("e.tsv", Lines("form\tcategory\tevidence", "Nairobi\tname\ta town", "Kisumu\tloan\tx", "Fresh\tname\ty")),
            dryRun: true, replace: true, detail: true);
        Assert.Equal((1, 1, 1, false), (again.Added, again.Replaced, again.Unchanged, again.Written));
        Assert.Equal(["already-recorded", "replaced", "recorded"], again.Results.Select(r => r.Result));
        Assert.Equal(rows, Rows());
        Assert.Null(Standing("Fresh"));
        Assert.Equal("name", Standing("Kisumu")!.Category);
    }

    [Fact]
    public void RunningTheSameFileAgainChangesNothingAndNewEvidenceIsAppended()
    {
        var path = Write("d.tsv", "form\tcategory\tevidence\nNairobi\tname\ta town\n");
        Disposed(path);
        var rows = Rows();

        var same = Disposed(path);
        Assert.Equal((0, 1, 0, true), (same.Added, same.Unchanged, same.EvidenceExtended, same.Written));

        var more = Disposed(Write("e.tsv", "form\tcategory\tevidence\nNairobi\tname\tseen in 1998 too\n"));
        Assert.Equal((1, 1), (more.Unchanged, more.EvidenceExtended));
        Assert.Equal("a town | seen in 1998 too", Standing("Nairobi")!.Evidence);
        Assert.Equal(rows, Rows());
    }

    [Theory]
    [InlineData("", "empty")]
    [InlineData("form\tcategory\n", "header")]
    [InlineData("form\tcategory\tevidence\n", "no rows")]
    public void AFileThatCannotBeReadAtAllIsRefusedWhole(string text, string why)
    {
        var outcome = TryFile(Write("d.tsv", text));

        Assert.False(outcome.Succeeded, why);
        Assert.Equal(RefusalCodes.DispositionInvalid, outcome.Refusal!.Code);
        Assert.False(TryFile(Path.Combine(_root, "absent.tsv")).Succeeded);
        Assert.False(TryFile(Write("x.tsv", "a\tb\n"), format: "yaml").Succeeded);
    }

    [Fact]
    public void ATwentyFourThousandRowFileIsRecordedWithinTheBound()
    {
        var text = new StringBuilder("form\tcategory\tintended\tevidence\n");
        for (var i = 0; i < 24_000; i++)
            text.Append(i % 10 == 0 ? $"misspelt{i}\tmisspelling\tword{i}\ttypo {i}\n" : $"Name{i}\tname\t\tseen in the corpus {i}\n");
        var path = Write("big.tsv", text.ToString());
        var clock = Stopwatch.StartNew();

        var response = Disposed(path);
        var recorded = clock.Elapsed;
        var again = Disposed(path);
        var repeated = clock.Elapsed - recorded;

        Assert.Equal((24_000, 24_000, true), (response.Rows, response.Added, response.Written));
        Assert.Equal((0, 24_000, 0), (again.Added, again.Unchanged, again.EvidenceExtended));
        Assert.Equal(24_000, Rows());
        Assert.True(recorded < TimeSpan.FromSeconds(60), $"first run took {recorded}");
        Assert.True(repeated < TimeSpan.FromSeconds(60), $"second run took {repeated}");
    }
}

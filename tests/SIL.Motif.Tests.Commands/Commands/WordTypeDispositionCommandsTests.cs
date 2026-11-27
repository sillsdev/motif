using Microsoft.Data.Sqlite;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Dispositions;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>Word type dispositions in Motif's store: no-op, contradiction refusal, replace, provenance and history.</summary>
public sealed class WordTypeDispositionCommandsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-dispositions", Guid.NewGuid().ToString("N"));
    private readonly string _project;

    public WordTypeDispositionCommandsTests()
    {
        Directory.CreateDirectory(_root);
        _project = Path.Combine(_root, "dispositions.fwdata");
        File.WriteAllText(_project, "placeholder");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private CommandOutcome<DisposeFormsResponse> TryDispose(string category, string evidence,
        string? intended = null, bool replace = false, string by = "ana", params string[] forms) =>
        WordTypeDispositionCommands.Record(new DisposeWordFormsRequest(_project, "1.0", forms, category, evidence, intended, replace, by));

    private DisposeFormsResponse Recorded(string category, string evidence, string? intended = null,
        bool replace = false, string by = "ana", params string[] forms)
    {
        var outcome = TryDispose(category, evidence, intended, replace, by, forms);
        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        return outcome.Value!;
    }

    private ShowDispositionResponse Show(string form)
    {
        var outcome = WordTypeDispositionCommands.Show(new ShowDispositionRequest(_project, "1.0", form));
        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        return outcome.Value!;
    }

    private long Rows()
    {
        using var database = ProjectMotifDatabase.Open(_project);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM WordTypeDispositions;";
        return (long)command.ExecuteScalar()!;
    }

    [Fact]
    public void ADispositionIsRecordedWithItsEvidenceWhoAndWhenAndIsWordWide()
    {
        var response = Recorded("name", "a town in the gazetteer", forms: ["Nairobi", "Mombasa"]);

        Assert.Equal((2, 0, 0), (response.Recorded, response.Replaced, response.AlreadyRecorded));
        var shown = Show("Nairobi");
        Assert.Equal("word-wide", shown.Scope);
        var current = Assert.IsType<WordTypeDisposition>(shown.Current);
        Assert.Equal(("name", "a town in the gazetteer", "ana", null), (current.Category, current.Evidence, current.RecordedBy, current.Intended));
        Assert.True(DateTimeOffset.TryParse(current.RecordedUtc, out var at) && at > DateTimeOffset.UtcNow.AddMinutes(-5));
        Assert.Equal(WordTypeKey.Of("Nairobi"), current.Key);
        Assert.Empty(shown.History);
    }

    [Fact]
    public void AnIdenticalWriteIsANoOpThatAppendsNewEvidenceAndKeepsWhoRecordedItAndWritesNoRow()
    {
        Recorded("loan", "first evidence", forms: ["kompyuta"]);
        var rows = Rows();

        var again = Recorded("loan", "second evidence", by: "ben", forms: ["kompyuta"]);
        var repeated = Recorded("loan", "second evidence", by: "ben", forms: ["kompyuta"]);

        Assert.Equal((0, 0, 1), (again.Recorded, again.Replaced, again.AlreadyRecorded));
        Assert.Equal("evidence-added", Assert.Single(again.Results).Result);
        Assert.Equal("already-recorded", Assert.Single(repeated.Results).Result);
        Assert.Equal(rows, Rows());
        var current = Show("kompyuta").Current!;
        Assert.Equal(("first evidence | second evidence", "ana"), (current.Evidence, current.RecordedBy));
    }

    [Fact]
    public void AnIntendedFormIsStoredDecomposedSoAComposedSpellingIsTheSameDisposition()
    {
        Recorded("misspelling", "typo", intended: "caf\u00e9", forms: ["cafe"]);

        Assert.Equal(WordTypeKey.Of("caf\u00e9"), Show("cafe").Current!.Intended);
        Assert.Equal(1, Recorded("misspelling", "typo", intended: "cafe\u0301", forms: ["cafe"]).AlreadyRecorded);
        Assert.Equal(1, Rows());
    }

    [Fact]
    public void AnIntendedFormMayBeSeveralWordsForARunTogetherMisspelling()
    {
        Recorded("misspelling", "two words run together", intended: "New York", forms: ["newyork"]);

        Assert.Equal("New York".Normalize(System.Text.NormalizationForm.FormD), Show("newyork").Current!.Intended);
        Assert.Equal(1, Recorded("misspelling", "again", intended: "New York", forms: ["newyork"]).AlreadyRecorded);
        Recorded("misspelling", "x", intended: "caf\u00e9 au lait", forms: ["cafelait"]);
        Assert.Equal(1, Recorded("misspelling", "again", intended: "cafe\u0301 au lait", forms: ["cafelait"]).AlreadyRecorded);
    }

    [Fact]
    public void AnIntendedFormWithASemicolonIsRefusedWithAHintToPickOne()
    {
        var refused = TryDispose("misspelling", "x", intended: "of; the", forms: ["ofthe"]);

        Assert.Equal(RefusalCodes.DispositionInvalid, refused.Refusal!.Code);
        Assert.Contains("pick one", refused.Refusal.Message, StringComparison.Ordinal);
        Assert.Equal(0, Rows());
    }

    [Fact]
    public void ADifferentCategoryIsRefusedAndTheWholeCallWritesNothing()
    {
        Recorded("name", "a town", forms: ["Nairobi"]);
        var rows = Rows();

        var refused = TryDispose("loan", "english", forms: ["Fresh", "Nairobi"]);

        Assert.False(refused.Succeeded);
        Assert.Equal(RefusalCodes.DispositionContradiction, refused.Refusal!.Code);
        Assert.Equal(FailureReason.Refused, refused.Refusal.Reason);
        Assert.Contains("Nairobi is already name", refused.Refusal.Message, StringComparison.Ordinal);
        Assert.Contains("--replace", refused.Refusal.Message, StringComparison.Ordinal);
        Assert.Equal(rows, Rows());
        Assert.Null(Show("Fresh").Current);
        Assert.Equal("name", Show("Nairobi").Current!.Category);
    }

    [Fact]
    public void AMisspellingWithAnotherIntendedFormContradictsTheStandingOne()
    {
        Recorded("misspelling", "typo", intended: "receive", forms: ["recieve"]);

        Assert.Equal(RefusalCodes.DispositionContradiction,
            TryDispose("misspelling", "typo", intended: "believe", forms: ["recieve"]).Refusal!.Code);
        Assert.Equal(1, Recorded("misspelling", "typo", intended: "receive", forms: ["recieve"]).AlreadyRecorded);
    }

    [Fact]
    public void ReplaceEndsTheStandingRowAsReplacedKeepsItAsHistoryAndRecordsTheNewOne()
    {
        Recorded("name", "a town", forms: ["Nairobi"]);

        var replaced = Recorded("loan", "borrowed place word", replace: true, by: "ben", forms: ["Nairobi", "Kisumu"]);

        Assert.Equal((1, 1, 0), (replaced.Recorded, replaced.Replaced, replaced.AlreadyRecorded));
        var shown = Show("Nairobi");
        Assert.Equal(("loan", "ben"), (shown.Current!.Category, shown.Current.RecordedBy));
        var ended = Assert.Single(shown.History);
        Assert.Equal(("name", "replaced", "ben"), (ended.Disposition.Category, ended.EndedKind, ended.EndedBy));
        Assert.Equal("ana", ended.Disposition.RecordedBy);
        Assert.Equal(3, Rows());
    }

    [Fact]
    public void ReplaceOfAnIdenticalDispositionIsStillANoOp()
    {
        Recorded("name", "a town", forms: ["Nairobi"]);

        Assert.Equal(1, Recorded("name", "a town", replace: true, forms: ["Nairobi"]).AlreadyRecorded);
        Assert.Empty(Show("Nairobi").History);
    }

    [Fact]
    public void ClearingKeepsTheRowAsHistoryWithItsReasonAndAFormWithNoneIsReportedNotRecorded()
    {
        Recorded("numeral", "digits", forms: ["1998"]);

        var cleared = WordTypeDispositionCommands.Clear(new UndisposeWordFormsRequest(_project, "1.0", ["1998", "none"], "now a year name", "ben"));

        Assert.True(cleared.Succeeded, cleared.Refusal?.Message);
        Assert.Equal((1, 1), (cleared.Value!.Cleared, cleared.Value.NotRecorded));
        Assert.Equal(["cleared", "not-recorded"], cleared.Value.Results.Select(r => r.Result));
        var shown = Show("1998");
        Assert.Null(shown.Current);
        var ended = Assert.Single(shown.History);
        Assert.Equal(("numeral", "cleared", "ben", "now a year name"), (ended.Disposition.Category, ended.EndedKind, ended.EndedBy, ended.EndedNote));
        Assert.Equal(1, WordTypeDispositionCommands.Clear(new UndisposeWordFormsRequest(_project, "1.0", ["1998"])).Value!.NotRecorded);

        Recorded("name", "a person called 1998", forms: ["1998"]);
        Assert.Equal("name", Show("1998").Current!.Category);
        Assert.Single(Show("1998").History);
    }

    [Fact]
    public void ComposedAndDecomposedSpellingsAreOneWordType()
    {
        Recorded("name", "a place", forms: ["Éa"]);

        Assert.Equal(1, Recorded("name", "a place", forms: ["Éa"]).AlreadyRecorded);
        Assert.Equal("Éa", Show("Éa").Current!.Form);
        Assert.Equal(WordTypeKey.Of("Éa"), Show("Éa").Key);
        Assert.Equal(1, Rows());
    }

    [Fact]
    public void FormsThatAreOneTypeInOneCallAreWrittenOnce()
    {
        var response = Recorded("name", "a place", forms: ["Éa", "Éa", "Éa"]);

        Assert.Single(response.Results);
        Assert.Equal(1, Rows());
    }

    [Fact]
    public void ArtifactIsAnotherNameForNonWord()
    {
        Recorded("artifact", "page number", forms: ["xxii"]);

        Assert.Equal("non-word", Show("xxii").Current!.Category);
    }

    public static TheoryData<string, string, string?, string[]> Invalid => new()
    {
        { "person", "evidence", null, ["a"] },
        { "", "evidence", null, ["a"] },
        { "name", "", null, ["a"] },
        { "name", "   ", null, ["a"] },
        { "misspelling", "typo", null, ["a"] },
        { "misspelling", "typo", "has\ttab", ["a"] },
        { "misspelling", "typo", "a", ["a"] },
        { "name", "evidence", "b", ["a"] },
        { "name", "evidence", null, [] },
        { "name", "evidence", null, [""] },
        { "name", "evidence", null, ["two words"] },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void AnInvalidRequestIsRefusedWithATypedReasonAndWritesNothing(
        string category, string evidence, string? intended, string[] forms)
    {
        var refused = TryDispose(category, evidence, intended, forms: forms);

        Assert.False(refused.Succeeded);
        Assert.Equal(RefusalCodes.DispositionInvalid, refused.Refusal!.Code);
        Assert.Equal(FailureReason.InvalidArgument, refused.Refusal.Reason);
        Assert.Equal(0, Rows());
    }

    [Fact]
    public void AnUnknownCategoryNamesTheClosedSet()
    {
        var refused = TryDispose("person", "x", forms: ["a"]);

        foreach (var name in new[] { "name", "loan", "other-language", "abbreviation", "numeral", "non-word", "misspelling" })
            Assert.Contains(name, refused.Refusal!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheListPagesInKeyOrderFiltersByCategoryAndCountsEveryCategory()
    {
        Recorded("name", "town", forms: ["Cc", "Aa", "Bb"]);
        Recorded("numeral", "digits", forms: ["1", "2"]);
        Recorded("misspelling", "typo", intended: "Aa", forms: ["Aaa"]);

        var first = WordTypeDispositionCommands.List(new ListDispositionsRequest(_project, "1.0", Limit: 3)).Value!;
        Assert.Equal((6, 2, 1, 3), (first.Total, first.Pages, first.Page, first.Items.Count));
        Assert.Equal(["1", "2", "Aa"], first.Items.Select(i => i.Form));
        var second = WordTypeDispositionCommands.List(new ListDispositionsRequest(_project, "1.0", Limit: 3, Page: 2)).Value!;
        Assert.Equal(["Aaa", "Bb", "Cc"], second.Items.Select(i => i.Form));

        var names = WordTypeDispositionCommands.List(new ListDispositionsRequest(_project, "1.0", "name")).Value!;
        Assert.Equal((3, "name"), (names.Total, names.Category));
        Assert.Equal(["Aa", "Bb", "Cc"], names.Items.Select(i => i.Form));
        Assert.Equal(new[] { 3, 0, 0, 0, 2, 0, 1 }, names.ByCategory.Select(c => c.Types));
        Assert.Equal(["name", "loan", "other-language", "abbreviation", "numeral", "non-word", "misspelling"], names.ByCategory.Select(c => c.Category));
    }

    [Theory]
    [InlineData("person", 50, 1)]
    [InlineData(null, 0, 1)]
    [InlineData(null, 201, 1)]
    [InlineData(null, 50, 0)]
    public void AnInvalidListRequestIsRefused(string? category, int limit, int page)
    {
        var refused = WordTypeDispositionCommands.List(new ListDispositionsRequest(_project, "1.0", category, limit, page));

        Assert.Equal(RefusalCodes.DispositionInvalid, refused.Refusal!.Code);
    }

    [Fact]
    public void ReadingAProjectWithNoStoreAnswersEmptyAndCreatesNone()
    {
        var shown = Show("anything");
        var listed = WordTypeDispositionCommands.List(new ListDispositionsRequest(_project, "1.0")).Value!;

        Assert.Null(shown.Current);
        Assert.Equal((0, 1), (listed.Total, listed.Pages));
        Assert.Empty(Directory.GetFiles(_root, "*.db"));
    }

    [Fact]
    public void TheStoreItselfRefusesTwoStandingRowsForOneTypeAndAMisspellingWithoutAnIntendedForm()
    {
        Recorded("name", "a town", forms: ["Nairobi"]);
        using var database = ProjectMotifDatabase.Open(_project);
        using var connection = database.OpenConnection();

        const string insert = "INSERT INTO WordTypeDispositions (FormKey, Form, Category, Evidence, Intended, RecordedBy, RecordedUtc) " +
            "VALUES ('{0}', 'f', '{1}', 'e', {2}, 'x', 't');";
        Assert.Throws<SqliteException>(() => Run(connection, string.Format(insert, WordTypeKey.Of("Nairobi"), "loan", "NULL")));
        Assert.Throws<SqliteException>(() => Run(connection, string.Format(insert, "other", "misspelling", "NULL")));
        Assert.Throws<SqliteException>(() => Run(connection, string.Format(insert, "other", "name", "'x'")));
        Assert.Throws<SqliteException>(() => Run(connection, string.Format(insert, "other", "person", "NULL")));
    }

    private static void Run(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}

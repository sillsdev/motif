using System;
using System.IO;
using System.Linq;
using System.Threading;
using SIL.Motif.Commands;
using Microsoft.Data.Sqlite;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Host.Assess;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// Pins <see cref="StoredGrammarCheckQuery"/>: it answers with the grammar check stored for the current
/// Baseline, says when there is none, and never runs the parser itself.
/// </summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
public sealed class StoredGrammarCheckQueryTests : IDisposable
{
    private readonly PristineProjectFixture _pristine;
    private readonly string _managedRootsParent =
        Path.Combine(Path.GetTempPath(), "SIL.Motif.StoredGrammarCheckQueryTests", Guid.NewGuid().ToString("N"));

    public StoredGrammarCheckQueryTests(PristineProjectFixture pristine)
    {
        _pristine = pristine;
        Directory.CreateDirectory(_managedRootsParent);
    }

    public void Dispose()
    {
        try { Directory.Delete(_managedRootsParent, recursive: true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void AStoredCheckOfTheCurrentBaselineIsReturnedWithoutRunningTheParser()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        Capture(fwDataPath);
        var invoker = new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Completed(
                ReportWithOneFinding,
                string.Empty, TimeSpan.Zero),
        };
        var request = new GrammarCheckRequest(fwDataPath);
        var checkedNow = GrammarCheckQuery.Query(request, invoker, CancellationToken.None, parserStamp: "build-1");
        Assert.True(checkedNow.Succeeded, checkedNow.Refusal?.Message);

        var stored = StoredGrammarCheckQuery.Query(request);

        Assert.True(stored.Succeeded, stored.Refusal?.Message);
        Assert.Equal(checkedNow.Value!.Findings.Single().Text, stored.Value!.Check!.Findings.Single().Text);
        Assert.Single(invoker.Requests);
    }

    [Fact]
    public void ASecondCheckOfTheSameBaselineReplacesTheFirstAfterSelectionChanges()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        Capture(fwDataPath);
        var request = new GrammarCheckRequest(fwDataPath);
        var firstSelection = SelectionCommands.SetDefault(new SetDefaultSelectionRequest(
            fwDataPath, "First", [], ["first"]));
        Assert.True(firstSelection.Succeeded, firstSelection.Refusal?.Message);
        var first = GrammarCheckQuery.Query(request, new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Completed(ReportWithOneFinding, string.Empty, TimeSpan.Zero),
        }, CancellationToken.None, parserStamp: "build-1");
        Assert.True(first.Succeeded, first.Refusal?.Message);
        var firstRow = ReadGrammarCheckRow(fwDataPath);
        Assert.True(firstRow.Succeeded, firstRow.Refusal?.Message);

        var secondSelection = SelectionCommands.SetDefault(new SetDefaultSelectionRequest(
            fwDataPath, "Second", [], ["second"]));
        Assert.True(secondSelection.Succeeded, secondSelection.Refusal?.Message);
        var second = GrammarCheckQuery.Query(request, new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Completed(EmptyReport, string.Empty, TimeSpan.Zero),
        }, CancellationToken.None, parserStamp: "build-2");
        Assert.True(second.Succeeded, second.Refusal?.Message);

        var stored = StoredGrammarCheckQuery.Query(request);
        Assert.True(stored.Succeeded, stored.Refusal?.Message);
        Assert.Empty(stored.Value!.Check!.Findings);
        var rows = ReadGrammarCheckRow(fwDataPath);
        Assert.True(rows.Succeeded, rows.Refusal?.Message);
        Assert.Equal(1, rows.Value!.Count);
        Assert.NotEqual(firstRow.Value!.SelectionSha256, rows.Value.SelectionSha256);
    }

    private static CommandOutcome<GrammarCheckRows> ReadGrammarCheckRow(string fwDataPath) =>
        ProjectStoreCommand.Run(fwDataPath, MotifProductVersion.CurrentText, (database, _) =>
        {
            using var connection = database.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*), MAX(SelectionSha256) FROM GrammarChecks;";
            using var reader = command.ExecuteReader();
            reader.Read();
            return CommandOutcome<GrammarCheckRows>.Success(new(reader.GetInt32(0), reader.GetString(1)));
        });

    private sealed record GrammarCheckRows(int Count, string SelectionSha256);

    [Fact]
    public void StoredFindingsSurviveRemovalOfTheBaselineSideCache()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        Capture(fwDataPath);
        var request = new GrammarCheckRequest(fwDataPath);
        var checkedNow = GrammarCheckQuery.Query(request, new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Completed(ReportWithOneFinding, string.Empty, TimeSpan.Zero),
        }, CancellationToken.None, parserStamp: "build-1");
        Assert.True(checkedNow.Succeeded, checkedNow.Refusal?.Message);

        foreach (var path in Directory.GetFiles(_managedRootsParent, "grammar-check.json", SearchOption.AllDirectories))
            File.Delete(path);

        var stored = StoredGrammarCheckQuery.Query(request);

        Assert.True(stored.Succeeded, stored.Refusal?.Message);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(checkedNow.Value),
            System.Text.Json.JsonSerializer.Serialize(stored.Value!.Check));
    }

    [Fact]
    public void StoredFindingsDoNotRequireTheParserStamp()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        Capture(fwDataPath);
        var request = new GrammarCheckRequest(fwDataPath);
        GrammarCheckQuery.Query(request, new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Completed(EmptyReport, string.Empty, TimeSpan.Zero),
        }, CancellationToken.None, parserStamp: "build-1");

        Assert.NotNull(StoredGrammarCheckQuery.Query(request).Value!.Check);
    }

    [Fact]
    public void ABaselineNeverCheckedIsNotCheckedYet()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        Capture(fwDataPath);

        var stored = StoredGrammarCheckQuery.Query(new GrammarCheckRequest(fwDataPath));

        Assert.True(stored.Succeeded, stored.Refusal?.Message);
        Assert.Null(stored.Value!.Check);
    }

    [Fact]
    public void NoBaselineIsAnEmptyCheckThatSaysSo()
    {
        var fwDataPath = _pristine.CopyProjectFile();

        var stored = StoredGrammarCheckQuery.Query(new GrammarCheckRequest(fwDataPath));

        Assert.True(stored.Succeeded, stored.Refusal?.Message);
        Assert.False(stored.Value!.Check!.HasBaseline);
    }

    [Fact]
    public void EachStoredFindingCarriesTheSelectionsWordsItTouchesFromTheStoredParseAllWords()
    {
        var grammar = WarningGrammar.Author(_pristine, withText: true);
        var seed = _pristine.Seed;
        Assert.True(SelectionCommands.SetDefault(new SetDefaultSelectionRequest(
            grammar.FwDataPath, "Default", [grammar.TextId!.Value], [])).Succeeded);
        var report = GrammarHealthReports.With(
            ("allomorph", [new("MoForm", "motifb", seed.SecondLexemeFormId)]),
            ("environment", [new("PhEnvironment", "/ _ [V]", grammar.VowelsBefore)]),
            ("natural-class", [new("PhNaturalClass", "V", grammar.Vowels)]),
            ("rule", [new("PhRegularRule", "Vowel harmony", grammar.Harmony)]),
            ("phoneme", [new("PhPhoneme", "u", grammar.U)]),
            ("template", [new("MoInflAffixTemplate", "Verb template", Guid.NewGuid())]),
            ("nothing", []));
        var request = new GrammarCheckRequest(grammar.FwDataPath);
        Assess(grammar);
        Assert.True(GrammarCheckQuery.Query(request, new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Completed(report, string.Empty, TimeSpan.Zero),
        }, CancellationToken.None).Succeeded);

        var stored = StoredGrammarCheckQuery.Query(request);

        Assert.True(stored.Succeeded, stored.Refusal?.Message);
        var yours = stored.Value!.Check!.Findings.ToDictionary(finding => finding.Code!, finding => finding.YourWords!);
        string[] Words(string code) => yours[code].Words.Select(word => word.Row.Word).ToArray();
        Assert.Equal(WarningWordsMatch.Identity, yours["allomorph"].Match);
        Assert.Equal([SeededProject.AnalysedWordForm], Words("allomorph"));
        Assert.Equal([("Lost", 1)], yours["allomorph"].ByMeaning.Select(meaning => (meaning.Meaning, meaning.Words)));
        Assert.Equal([SeededProject.AnalysedWordForm], Words("environment"));
        Assert.Equal([WarningWordsPath.ThroughAllomorphs], yours["environment"].Paths);
        Assert.Equal([SeededProject.UnanalysedWordForm], Words("natural-class"));
        Assert.Equal([WarningAttributionReason.UnresolvedEnvironmentNotation],
            stored.Value.Check.Findings.Single(finding => finding.Code == "natural-class").AttributionLimits);
        Assert.Equal(WarningWordsMatch.Identity, yours["natural-class"].Match);
        Assert.Equal([SeededProject.UnanalysedWordForm], Words("rule"));
        Assert.Equal((3, 3_000_000L), (yours["rule"].Words[0].Calls!.Value, yours["rule"].Words[0].ElapsedNs!.Value));
        Assert.Equal(WarningWordsMatch.Spelling, yours["phoneme"].Match);
        Assert.Equal([SeededProject.UnanalysedWordForm], Words("phoneme"));
        Assert.Equal((WarningWordsMatch.MissingObject, WarningAttributionReason.StaleGuid),
            (yours["template"].Match, yours["template"].Reason));
        Assert.Equal((WarningWordsMatch.UnresolvedIdentity, WarningAttributionReason.NoSubject),
            (yours["nothing"].Match, yours["nothing"].Reason));

        var warnings = WarningsCommand.Warnings(new WarningsRequest(grammar.FwDataPath)).Value!;
        Assert.Equal((2, 2, 0), (warnings.YourWords!.Words, warnings.YourWords.NoParse, warnings.YourWords.BySpellingOnly));
        Assert.Equal(0, warnings.ByKind.Single(kind => kind.Code == "phoneme").YourWords);
        Assert.Equal(1, warnings.ByKind.Single(kind => kind.Code == "phoneme").BySpellingOnly);
        Assert.Null(warnings.ByKind.Single(kind => kind.Code == "nothing").YourWords);
        Assert.Equal((0, 1, 0), (
            warnings.ByKind.Single(kind => kind.Code == "phoneme").YourWords,
            warnings.ByKind.Single(kind => kind.Code == "phoneme").BySpellingOnly,
            warnings.ByKind.Single(kind => kind.Code == "phoneme").ByMembershipOnly));
        var overview = OverviewCommand.Overview(new OverviewRequest(grammar.FwDataPath)).Value!;
        Assert.Equal((warnings.YourWords.Words, warnings.YourWords.NoParse),
            (overview.Warnings!.YourWords!.Words, overview.Warnings.YourWords.NoParse));
        Assert.Equal(warnings.YourWords.ByMeaning, overview.Warnings.YourWords.ByMeaning);
        Assert.Equal(ProjectionJson.Serialize(warnings.ByKind), ProjectionJson.Serialize(overview.Warnings.ByKind));
        Assert.Equal(0, overview.Warnings.ByKind.Single(kind => kind.Code == "phoneme").YourWords);
    }

    [Fact]
    public void WithoutAStoredParseAllWordsTheWordsAreUnknownNotNone()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        Capture(fwDataPath);
        var request = new GrammarCheckRequest(fwDataPath);
        Assert.True(GrammarCheckQuery.Query(request, new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Completed(GrammarHealthReports.With(
                ("allomorph", [new("MoForm", "motifa", _pristine.Seed.FirstLexemeFormId)])), string.Empty, TimeSpan.Zero),
        }, CancellationToken.None).Succeeded);

        var finding = StoredGrammarCheckQuery.Query(request).Value!.Check!.Findings.Single();

        Assert.NotNull(finding.Subject.Single().Reach);
        Assert.Null(finding.YourWords);
        Assert.Null(WarningsCommand.Warnings(new WarningsRequest(fwDataPath)).Value!.YourWords);
    }

    // The analysed word uses both seeded forms but PanGloss builds nothing; the rule ran only in the other word.
    private void Assess(WarningGrammar grammar)
    {
        var cachePath = Path.Combine(_managedRootsParent, Guid.NewGuid().ToString("N") + "-stats.sqlite");
        WriteRuleTimes(cachePath, grammar.Harmony.ToString("D").ToUpperInvariant(),
            (SeededProject.AnalysedWordForm, 0, 0L), (SeededProject.UnanalysedWordForm, 3, 3_000_000L));
        var digest = BatchInvocationEvidence.DigestFile(cachePath);
        var assessor = new FakeAssessor("fake-assessor", [AssessmentKind.ParseTime, AssessmentKind.ObjectTiming],
            kind => kind switch
            {
                AssessmentKind.ParseTime => new AssessmentRaw.Batch(new SIL.Motif.Host.Parser.BatchAnalysis(
                    [new(0, SeededProject.AnalysedWordForm, 9, SIL.Motif.Host.Parser.WordOutcome.NoAnalysis, "none"),
                     new(1, SeededProject.UnanalysedWordForm, 15, SIL.Motif.Host.Parser.WordOutcome.NoAnalysis, "none")],
                    1000, grammar.FwDataPath, []) { PerWordStepLimit = StepCap.Default }),
                AssessmentKind.ObjectTiming => new AssessmentRaw.FileCache(cachePath, digest),
                _ => new AssessmentRaw.WordMeasurements([]),
            })
        {
            CaptureEvidence = (scope, candidate) => FakeAssessmentEvidence.Capture(_managedRootsParent, scope, candidate),
        };
        var assessed = AssessCommand.Run(new AssessRequest(grammar.FwDataPath), NewManagedRoot(), assessor,
            new FakeInvoker(), null, CancellationToken.None);
        Assert.True(assessed.Succeeded, assessed.Refusal?.Message);
    }

    private static void WriteRuleTimes(string path, string ruleKey,
        params (string Word, int Attempts, long SelfTimeNs)[] words)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE cache_identity (cache_id INTEGER PRIMARY KEY, schema_version INTEGER NOT NULL);
            INSERT INTO cache_identity VALUES (1, 8);
            CREATE TABLE word (
                word_id INTEGER PRIMARY KEY, form TEXT NOT NULL, elapsed_ns INTEGER NOT NULL,
                attempts INTEGER NOT NULL, passes INTEGER NOT NULL, capped INTEGER NOT NULL,
                timed_out INTEGER NOT NULL, invalid_shape INTEGER NOT NULL);
            CREATE TABLE object (object_id INTEGER PRIMARY KEY, key TEXT NOT NULL, kind TEXT NOT NULL,
                label TEXT NOT NULL, identity_quality TEXT NOT NULL);
            CREATE TABLE fact (word_id INTEGER NOT NULL, object_id INTEGER NOT NULL,
                direction TEXT NOT NULL, attempts INTEGER NOT NULL, self_time_ns INTEGER NOT NULL);
            INSERT INTO object VALUES (1, $key, 'phon_rule', 'Vowel harmony', 'authored');
            """;
        command.Parameters.AddWithValue("$key", ruleKey);
        command.ExecuteNonQuery();
        foreach (var (word, attempts, selfTimeNs) in words)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO word (form, elapsed_ns, attempts, passes, capped, timed_out, invalid_shape)
                VALUES ($form, 0, 1, 0, 0, 0, 0);
                INSERT INTO fact (word_id, object_id, direction, attempts, self_time_ns)
                SELECT word_id, 1, 'analysis', $attempts, $self_time_ns FROM word WHERE form = $form;
                """;
            insert.Parameters.AddWithValue("$form", word);
            insert.Parameters.AddWithValue("$attempts", attempts);
            insert.Parameters.AddWithValue("$self_time_ns", selfTimeNs);
            insert.ExecuteNonQuery();
        }
    }

    private const string ReportWithOneFinding = """
        {
          "schema_version": 4,
          "fieldworks_project": {
            "name": null,
            "source": null
          },
          "summary": [
            {
              "code": "hc-undeclared-segment",
              "group_name": "Undeclared segment",
              "level": "info",
              "count": 1
            }
          ],
          "diagnostics": [
            {
              "level": "info",
              "code": "hc-undeclared-segment",
              "group_name": "Undeclared segment",
              "origin": "check",
              "description": "Segment x is undeclared.",
              "guidance": null,
              "subjects": [],
              "title": "Undeclared segment",
              "explanation": "Segment x is undeclared.",
              "help_path": null,
              "help_body": null,
              "fieldworks_places": [],
              "scope": "project_settings"
            }
          ],
          "locale": "en"
        }
        """;

    private const string EmptyReport = """
        {
          "schema_version": 4,
          "fieldworks_project": {
            "name": null,
            "source": null
          },
          "summary": [],
          "diagnostics": [],
          "locale": "en"
        }
        """;

    private void Capture(string fwDataPath)
    {
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), NewManagedRoot());
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
    }

    private string NewManagedRoot()
    {
        var root = Path.Combine(_managedRootsParent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}

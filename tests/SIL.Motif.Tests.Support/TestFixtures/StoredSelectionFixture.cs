using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.Commands.SelectionReading;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host.Store;
using SIL.Motif.Host.Texts;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>Publishes explicit current-format Text records for reader tests that do not open a LibLCM project.</summary>
public sealed partial class StoredSelectionFixture : IDisposable
{
    /// <summary>Folder below the temporary directory that holds disposable fixture stores.</summary>
    public const string TemporaryRootFolder = "SIL.Motif.StoredSelectionFixture";

    private readonly string _root = Path.Combine(Path.GetTempPath(), TemporaryRootFolder,
        Guid.NewGuid().ToString("N"));
    private readonly MotifDatabase _database;
    private readonly string _projectKey;
    private readonly BaselineToken _baseline;

    public StoredSelectionFixture(TextWordsProjection projection, BaselineToken? baseline = null)
    {
        Directory.CreateDirectory(_root);
        ProjectPath = Path.Combine(_root, "project.fwdata");
        var project = new ProjectLocator(ProjectPath, "project");
        _projectKey = ProjectWorkspaceKey.Compute(project);
        _database = OpenFromEmptySchemaTemplate(project);
        var token = baseline ?? new BaselineToken("fixture-project", "sha256:" + new string('a', 64), "1",
            "2026-09-05T11:02:00Z", "sha256:" + new string('b', 64));
        _baseline = token;
        File.WriteAllText(ProjectPath, Guid.TryParse(token.ProjectIdentity, out var projectIdentity)
            ? "<languageproject><rt class=\"LangProject\" guid=\"" + projectIdentity + "\"/>" +
                string.Concat(projection.Wordforms.Select(word =>
                    "<rt class=\"WfiWordform\" guid=\"" + word.WordformId + "\"/>")) + "</languageproject>"
            : "synthetic saved-project marker");
        var texts = projection.Texts.Select(text =>
        {
            var tokens = text.Lines.SelectMany(line => line.Tokens).Where(word => word.WordformId is not null)
                .ToArray();
            var words = tokens
                .SelectMany(word => word.Forms).GroupBy(form => form.Text, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            var interlinearized = tokens.Where(word => word.AnalysisId is not null).ToArray();
            return new ProjectTextSummary(text.TextId, text.Title, words.Count, tokens.Length, words)
            {
                InterlinearizedWordCount = interlinearized.SelectMany(word => word.Forms).Select(form => form.Text)
                    .Distinct(StringComparer.Ordinal).Count(),
                InterlinearizedOccurrenceCount = interlinearized.Length,
            };
        }).ToArray();
        var forms = texts.SelectMany(text => text.OccurrencesByWord.Keys).Distinct(StringComparer.Ordinal).ToArray();
        var summary = new ProjectSummarySnapshot(forms.Length, texts.Sum(text => text.OccurrenceCount),
            projection.Wordforms.Count, 0, 0, forms, texts);
        new BaselineRepository(_database).Record(_projectKey,
            new BaselinePublication(_root, ProjectPath, token), DateTimeOffset.UtcNow,
            new DateTimeOffset(File.GetLastWriteTimeUtc(ProjectPath), TimeSpan.Zero), projection, summary);
    }

    public string ProjectPath { get; }

    // Created once per process; each fixture copies it and rewrites the one path it records.
    private static readonly Lazy<string> EmptySchemaTemplate = new(CreateEmptySchemaTemplate);

    private static string CreateEmptySchemaTemplate()
    {
        var folder = Path.Combine(Path.GetTempPath(), TemporaryRootFolder, "schema-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var template = new ProjectLocator(Path.Combine(folder, "project.fwdata"), "project");
        new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, new Version(1, 0)).OpenOwned(template).Dispose();
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Directory.Delete(folder, recursive: true); }
            catch { /* best effort */ }
        };
        return ProjectDatabaseCatalog.DatabasePathFor(template);
    }

    private static MotifDatabase OpenFromEmptySchemaTemplate(ProjectLocator project)
    {
        var path = ProjectDatabaseCatalog.DatabasePathFor(project);
        File.Copy(EmptySchemaTemplate.Value, path);
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE MotifMetadata SET FullFwDataPath = $path WHERE Id = 1;";
            command.Parameters.AddWithValue("$path", project.FullFwDataPath);
            command.ExecuteNonQuery();
        }
        return new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, new Version(1, 0)).OpenOwned(project);
    }

    /// <summary>Publishes the current warning evidence before a fixture opens its reader.</summary>
    public void RecordGrammarCheck(GrammarCheckResponse check) => new GrammarCheckRepository(_database).Save(
        JsonSerializer.Serialize(_baseline, MotifJson.CreateOptions()), "sha256:fixture-selection", null, check);

    /// <summary>Saves the project's Default Selection, as Setup does, so a reader opened on it can find its word list.</summary>
    public void SaveDefaultSelection(IReadOnlyList<Guid> textIds, IReadOnlyList<string> addedWords,
        SelectionWordList? wordList = null)
    {
        var selections = new NamedSelectionRepository(_database);
        selections.SetDefault("Default", textIds, addedWords,
            new SelectionParsingLimits(ParserWorkLimit.Default, SelectionTimeLimitMode.Estimated, null),
            selections.GetDefault()?.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture), wordList);
    }

    /// <summary>The Baseline token this fixture recorded, for a request that names the evidence it already shows.</summary>
    public BaselineToken Baseline => _baseline;

    /// <summary>
    /// Leaves the saved Default's stored word list unreadable, as a damaged store would: its text stops after the
    /// hash when <paramref name="keepHash"/> is true, else it is not JSON at all.
    /// </summary>
    public void DamageDefaultWordList(bool keepHash = true)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = keepHash
            ? "UPDATE NamedSelections SET WordListJson = substr(WordListJson, 1, instr(WordListJson, '\"Counts\"') - 1) " +
                "WHERE WordListJson IS NOT NULL;"
            : "UPDATE NamedSelections SET WordListJson = '{not json' WHERE WordListJson IS NOT NULL;";
        if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("No saved word list to damage.");
    }

    /// <summary>Makes the saved Selections unreadable, or readable again, as a locked or failing store would.</summary>
    public void HideSavedSelections(bool hidden)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = hidden
            ? "ALTER TABLE NamedSelections RENAME TO HiddenNamedSelections;"
            : "ALTER TABLE HiddenNamedSelections RENAME TO NamedSelections;";
        command.ExecuteNonQuery();
    }

    /// <summary>Publishes a named current-format Assessment alongside the fixture's Baseline records.</summary>
    public void RecordAssessment(NewAssessmentRecord assessment) => new AssessmentRepository(_database).Record(assessment);

    /// <summary>Opens the supplied source identities against this fixture's published rows and compact indexes.</summary>
    public Task<CommandOutcome<SelectionReader>> OpenAsync(OpenSelectionReaderRequest request,
        CancellationToken cancellationToken = default) => SelectionReader.OpenAsync(_database,
            new SelectionReadRequest(_projectKey, request.TextIds, request.AddedWords)
            {
                ProjectPath = ProjectPath,
                EvidenceSnapshot = request.EvidenceSnapshot,
                DisplayedEvidence = request.DisplayedEvidence,
                ShownAssessment = request.ShownAssessment,
            }, cancellationToken);

    /// <inheritdoc />
    public void Dispose()
    {
        _database.Dispose();
        Directory.Delete(_root, recursive: true);
    }
}

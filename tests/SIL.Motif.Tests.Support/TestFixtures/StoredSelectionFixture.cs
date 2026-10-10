using System.Text.Json;
using SIL.Motif.Commands.SelectionReading;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;
using SIL.Motif.Host.Texts;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>Publishes explicit current-format Text records for reader tests that do not open a LibLCM project.</summary>
public sealed partial class StoredSelectionFixture : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.StoredSelectionFixture",
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
        _database = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, new Version(1, 0)).OpenOwned(project);
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
            var words = text.Lines.SelectMany(line => line.Tokens).Where(word => word.WordformId is not null)
                .SelectMany(word => word.Forms).GroupBy(form => form.Text, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            return new ProjectTextSummary(text.TextId, text.Title, words.Count,
                text.Lines.Sum(line => line.Tokens.Count(word => word.WordformId is not null)), words);
        }).ToArray();
        var forms = texts.SelectMany(text => text.OccurrencesByWord.Keys).Distinct(StringComparer.Ordinal).ToArray();
        var summary = new ProjectSummarySnapshot(forms.Length, texts.Sum(text => text.OccurrenceCount),
            projection.Wordforms.Count, 0, 0, forms, texts);
        new BaselineRepository(_database).Record(_projectKey,
            new BaselinePublication(_root, ProjectPath, token), DateTimeOffset.UtcNow,
            new DateTimeOffset(File.GetLastWriteTimeUtc(ProjectPath), TimeSpan.Zero), projection, summary);
    }

    public string ProjectPath { get; }

    /// <summary>Publishes the current warning evidence before a fixture opens its reader.</summary>
    public void RecordGrammarCheck(GrammarCheckResponse check) => new GrammarCheckRepository(_database).Save(
        JsonSerializer.Serialize(_baseline, MotifJson.CreateOptions()), "sha256:fixture-selection", null, check);

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

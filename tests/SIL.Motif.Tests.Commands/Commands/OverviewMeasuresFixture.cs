using System.Text.Json;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Store;
using SIL.Motif.Host.Texts;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// A synthetic project store with one Baseline, one chosen Text whose words and places the test names, and a
/// stored Parse all words over the resolved Selection, so the Overview can be read end to end without LibLCM.
/// </summary>
internal sealed class OverviewMeasuresFixture : IDisposable
{
    private readonly string _root;
    private readonly string _tokenJson;
    private readonly Guid _textId = Guid.NewGuid();

    public OverviewMeasuresFixture(string testRoot, IReadOnlyDictionary<string, int> textPlaces,
        int digitOnlyPlaces = 0, int otherWritingSystemPlaces = 0, IReadOnlyList<string>? addedWords = null,
        bool chooseText = true)
    {
        _root = Path.Combine(testRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "project.fwdata");
        File.WriteAllText(path, "synthetic project marker");
        Project = new ProjectLocator(path, "project");
        Database = MotifDatabase.OpenOwned(Path.Combine(_root, "project.motif.db"), Project,
            MotifSchema.CurrentSchema, new Version(1, 0));
        Token = new BaselineToken("project-id", "sha256:" + new string('1', 64), "projection-v1",
            "2026-09-24T10:00:00Z", "sha256:" + new string('a', 64));
        _tokenJson = JsonSerializer.Serialize(Token, MotifJson.CreateOptions());
        var baselineRoot = Path.Combine(_root, "baseline");
        var summary = new ProjectSummarySnapshot(textPlaces.Count, textPlaces.Values.Sum(), textPlaces.Count, 0, 0,
            [.. textPlaces.Keys],
            [new ProjectTextSummary(_textId, "Text", textPlaces.Count, textPlaces.Values.Sum(), textPlaces)
            {
                DigitOccurrenceCount = digitOnlyPlaces,
                OtherWritingSystemOccurrenceCount = otherWritingSystemPlaces,
            }]);
        new BaselineRepository(Database).Record(ProjectWorkspaceKey.Compute(Project),
            new BaselinePublication(baselineRoot, Path.Combine(baselineRoot, "project.fwdata"), Token),
            DateTimeOffset.Parse("2026-09-24T10:30:00Z"), DateTimeOffset.Parse(Token.CapturedUtc),
            TestTextWords.Empty, summary);
        new NamedSelectionRepository(Database).SetDefault("Default", chooseText ? [_textId] : [], addedWords ?? [],
            new SelectionParsingLimits(StepCap.Default, SelectionTimeLimitMode.Estimated, null),
            expectedRevision: null);
        SelectionWords = Selection.Create("Default", [.. (chooseText ? textPlaces.Keys : []), .. addedWords ?? []]).Words;
    }

    public ProjectLocator Project { get; }
    public MotifDatabase Database { get; }
    public BaselineToken Token { get; }
    public string FwDataPath => Project.FullFwDataPath;
    public IReadOnlyList<string> SelectionWords { get; }

    /// <summary>Stores a Parse all words over the whole Selection with the given word results.</summary>
    public void Assess(params AssessedWord[] words) => new AssessmentRepository(Database).Record(new NewAssessmentRecord(
        AssessmentId: "project-root", ProposalId: null, ProposalIntentDigest: null, Assessor: "pangloss",
        Kind: AssessmentKinds.ParseTime, ScopeJson: "{}", ScopeDigest: "sha256:scope",
        Subject: new AssessmentSubject.Words(Selection.Create("Default", SelectionWords), "whitespace", "1"),
        BaselineToken: _tokenJson, OutcomeDigest: "sha256:outcome", SemanticDigest: "sha256:semantic",
        GrammarSourceSha256: "sha256:grammar", ModelFingerprint: "fingerprint", Pipeline: "pipeline",
        DiagnosticCount: 0, Material: new AssessmentRecordMaterial.Words(words, null, null, []),
        SavedUtc: "2026-09-24T10:45:00Z"));

    public void Dispose()
    {
        Database.Dispose();
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

/// <summary>Stored word results as the parser records them, one per way a word can end.</summary>
internal static class StoredWords
{
    public static AssessedWord Parsed(string word, int readings, string standing = ProjectStanding.NotPresent) =>
        new(word, "analysed", [])
        {
            Morphology = Evidence(word, readings),
            ProjectStanding = standing,
            ReadingGrades = Enumerable.Repeat(ReadingGrade.NoOpinion, readings).ToArray(),
        };

    public static AssessedWord NoAnalysis(string word, string standing = ProjectStanding.NotPresent) =>
        new(word, "no-analysis", []) { Morphology = Evidence(word, 0), ProjectStanding = standing };

    public static AssessedWord Refused(string word, string code, string standing = ProjectStanding.NotPresent) =>
        new(word, "no-analysis", [])
        {
            Morphology = Evidence(word, 0) with { Refusal = new SIL.Motif.Contract.Responses.ParserRefusal(code, "recorded reason") },
            ProjectStanding = standing,
        };

    public static AssessedWord InvalidShape(string word) =>
        new(word, "no-analysis", []) { Morphology = Evidence(word, 0) with { InvalidShape = true } };

    public static AssessedWord Stopped(string word, bool work, bool time, string standing = ProjectStanding.NotPresent) =>
        new(word, work ? "capped" : "timed-out", [])
        {
            IsIncomplete = true,
            Morphology = Evidence(word, 0) with { Capped = work, TimedOut = time },
            ProjectStanding = standing,
        };

    public static ParseWordEvidence Evidence(string word, int readings) => new("v1", 0, word, 1, false, false, false,
        Enumerable.Range(0, readings).Select(index => new ParseAnalysis(
            [new ParseMorph($"form-{index}", "v", null, null)])).ToArray(), []);
}

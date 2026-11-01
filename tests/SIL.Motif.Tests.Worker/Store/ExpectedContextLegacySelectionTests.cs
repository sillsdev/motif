using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Store;

/// <summary>
/// A Selection is stored with the words and hash it had when it was written. Words are decomposed to NFD on creation
/// now, so an Assessment stored with a composed word must still be recognised as the evidence a Proposal or a read
/// mark names, and a stored hash that does not match its words must still be refused.
/// </summary>
public sealed class ExpectedContextLegacySelectionTests : IDisposable
{
    private const string Composed = "\u0BAA\u0BCA\u0BA9\u0BCD";
    private const string Decomposed = "\u0BAA\u0BC6\u0BBE\u0BA9\u0BCD";
    private const string Declaration = "legacy-declaration";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-legacy-selection-" + Guid.NewGuid().ToString("N"));
    private readonly MotifDatabase _database;
    private readonly AssessmentRepository _assessments;
    private readonly BaselineToken _baseline = new("legacy-project", "sha256:" + new string('a', 64), "1",
        "2026-08-29T00:00:00Z", "sha256:" + new string('b', 64));

    public ExpectedContextLegacySelectionTests()
    {
        Directory.CreateDirectory(_root);
        var project = new ProjectLocator(Path.Combine(_root, "legacy.fwdata"), "legacy");
        _database = MotifDatabase.OpenOwned(Path.Combine(_root, "legacy.motif.db"), project,
            MotifSchema.CurrentSchema, new Version(1, 0));
        _assessments = new AssessmentRepository(_database);
    }

    // The hash a Selection was given before its words were decomposed: sha256 of the stored words as they are.
    private static string HashAsStored(string word) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(word))).ToLowerInvariant();

    private string BaselineJson => JsonSerializer.Serialize(_baseline, MotifJson.CreateOptions());

    private void Record(string id, Selection selection, string? proposalId = null, string? intentDigest = null)
    {
        _assessments.Record(new NewAssessmentRecord(
            AssessmentId: id, ProposalId: proposalId is null ? null : CanonicalId.Parse(proposalId),
            ProposalIntentDigest: intentDigest, Assessor: "pangloss", Kind: "ParseTime",
            ScopeJson: "{\"words\":\"all\"}", ScopeDigest: "sha256:scope",
            Subject: new AssessmentSubject.Words(selection, "whitespace", "1"),
            BaselineToken: BaselineJson, OutcomeDigest: "sha256:outcome", SemanticDigest: "sha256:semantic",
            GrammarSourceSha256: "sha256:grammar", ModelFingerprint: "fingerprint", Pipeline: "pipeline",
            DiagnosticCount: 0, Material: new AssessmentRecordMaterial.Words(
                [SIL.Motif.Tests.TestFixtures.CorrectnessFixture.Word(selection.Words[0], true)], null, null, [])));
    }

    private bool IsCurrent(string id, Selection selection)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var expected = new ExpectedContext(_baseline)
        {
            SelectionEvidence = new SelectionEvidenceExpectation("explicit", Declaration, [], [], selection.Sha256,
                id, [], [], []),
        };
        return ExpectedContextRepository.IsCurrentSelectionEvidence(_database, connection, transaction, "legacy", expected);
    }

    [Fact]
    public void AnAssessmentStoredWithAComposedWordIsStillTheEvidenceAProposalNamed()
    {
        var legacy = new Selection(Declaration, [Composed], HashAsStored(Composed));
        Record("assessment/legacy", legacy);

        var stored = _assessments.Get("assessment/legacy").Selection;

        Assert.Equal([Composed], stored.Words);
        Assert.Equal(legacy.Sha256, stored.Sha256);
        Assert.True(IsCurrent("assessment/legacy", stored));
    }

    [Fact]
    public void AnAssessmentStoredWithADecomposedWordIsStillTheEvidenceAProposalNamed()
    {
        var current = Selection.Create(Declaration, [Composed]);
        Record("assessment/current", current);

        Assert.Equal([Decomposed], current.Words);
        Assert.True(IsCurrent("assessment/current", _assessments.Get("assessment/current").Selection));
    }

    [Fact]
    public void AStoredHashThatIsNotTheHashOfItsWordsIsStillRefused()
    {
        var tampered = new Selection(Declaration, [Composed], HashAsStored("another"));
        Record("assessment/tampered", tampered);

        Assert.False(IsCurrent("assessment/tampered", _assessments.Get("assessment/tampered").Selection));
    }

    [Fact]
    public void ATrialRootStoredWithAComposedWordStillBelongsToItsProposal()
    {
        var proposalId = CanonicalId.Mint("proposal/").Value;
        using (var connection = _database.OpenConnection())
        {
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO Proposals (ProposalId, CurrentIntentDigest, Status) " +
                "VALUES ($id, 'sha256:intent', 'proposed');";
            command.Parameters.AddWithValue("$id", proposalId);
            command.ExecuteNonQuery();
        }
        Record("assessment/trial", new Selection(Declaration, [Composed], HashAsStored(Composed)), proposalId, "sha256:intent");

        var root = _assessments.GetHeader("assessment/trial")!;

        Assert.True(ExpectedContextRepository.MatchesTrialRoot(root, proposalId, "sha256:intent", BaselineJson));
    }

    public void Dispose()
    {
        _database.Dispose();
        try { Directory.Delete(_root, recursive: true); }
        catch (DirectoryNotFoundException) { }
    }
}

using SIL.Motif.Projection.Store;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host.Store;
using SIL.Motif.Runner.DryRun;
using SIL.Motif.Model.Receipts;

namespace SIL.Motif.Worker.Store;

/// <summary>The result of committing a Draft.</summary>
public enum DraftFinalizationResult
{
    /// <summary>The Draft became its Proposal's first committed revision.</summary>
    Finalized,

    /// <summary>The Draft became a new revision of a Proposal that was already committed.</summary>
    Amended,

    /// <summary>The Draft no longer matches the expected revision, and no store rows changed.</summary>
    DraftChanged,

    /// <summary>The requested follow-up status move was refused, so the Draft was not finalized either.</summary>
    MoveRefused,

    /// <summary>The Draft's Rationale is no longer the one the caller checked, and no store rows changed.</summary>
    RationaleChanged,
}

/// <summary>
/// The status and Draft name a Proposal had when its new Rationale was checked, which the write needs unchanged, and,
/// when <paramref name="Rationale"/> is given, the Rationale it replaces.
/// </summary>
public sealed record RationaleWriteExpectation(string Status, string? DraftName, string? Rationale = null);

/// <summary>A status move a finalize performs in its own transaction, so both happen or neither does.</summary>
public sealed record FinalizeMove(IReadOnlyCollection<string> AllowedFrom, string To, string? Comment = null);

/// <summary>Durable Proposal workflow access through a worker-owned Motif database.</summary>
public interface IProposalRepository
{
    /// <summary>
    /// Gets the current revision and review state for one Proposal, whether committed or still a Draft
    /// (marked by <see cref="ProposalRecord.DraftName"/>, whose content is <c>DraftJson</c> rather than a
    /// committed revision's bytes).
    /// </summary>
    ProposalRecord Get(CanonicalId proposalId);
    /// <summary>Lists current Proposals, optionally restricted to one workflow status; drafts are included, marked by <see cref="ProposalRecord.DraftName"/>.</summary>
    IReadOnlyList<ProposalRecord> List(ProposalListFilter filter);
    /// <summary>Stores one immutable revision and moves its Proposal pointer to that revision.</summary>
    void SaveRevision(ProposalRevisionRecord revision);
    /// <summary>
    /// Creates a Draft: a Proposal identified by <paramref name="proposalId"/> with <c>DraftName</c> set and
    /// no committed revision yet — <c>CurrentIntentDigest</c> stays <c>NULL</c> until <see cref="Finalize"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">A draft already exists under <paramref name="draftName"/>.</exception>
    void CreateDraft(string draftName, CanonicalId proposalId, string draftJson, string? role = null);
    /// <summary>Replaces a Draft's in-progress content. The Draft keeps its identity and name.</summary>
    void SaveDraft(string draftName, string draftJson);
    /// <summary>Replaces a Draft only if its stored bytes still equal the caller's read.</summary>
    bool TrySaveDraft(string draftName, string expectedJson, string draftJson);
    /// <summary>Creates a Draft only if its name is still free, carrying <paramref name="role"/> when given.</summary>
    bool TryCreateDraft(string draftName, CanonicalId proposalId, string draftJson, string? role = null);
    /// <summary>Gets the Draft that holds the pending changes, or <see langword="null"/> when none is open.</summary>
    ProposalRecord? FindPendingChangesDraft();
    /// <summary>Reads one named Draft and its prior committed identity in one snapshot.</summary>
    ProposalRecord GetDraft(string draftName);
    /// <summary>
    /// Writes a Proposal's Rationale (a Draft's or a finalized one's) and stamps it with the Proposal's current intent
    /// digest and the time; a Draft that was never finalized has no digest yet, so the stamp is empty until finalize.
    /// </summary>
    /// <remarks>
    /// While the Proposal is open the text is also kept as a revision carrying <paramref name="author"/>, a person with
    /// no name when omitted; a decided Proposal's revisions are dropped at the decision.
    /// </remarks>
    /// <returns>
    /// Whether it was written: false when no Proposal has this id, or when <paramref name="expected"/> is given and the
    /// Proposal's status or Draft name is no longer that.
    /// </returns>
    bool SetRationale(CanonicalId proposalId, string? rationale, DiscussionAuthor? author = null,
        RationaleWriteExpectation? expected = null);
    /// <summary>The content of one stored revision of a Proposal, or null when that revision is not stored.</summary>
    Proposal? GetRevisionContent(CanonicalId proposalId, string intentDigest);
    /// <summary>Lists every Draft, ordered by name.</summary>
    IReadOnlyList<ProposalRecord> ListDrafts();
    /// <summary>
    /// Commits a Draft: in one transaction, writes its first <c>ProposalRevisions</c> row, sets
    /// <c>CurrentIntentDigest</c>, clears <c>DraftName</c>/<c>DraftJson</c>, retains an anchor bound to the same digest, and moves
    /// the Proposal to <c>proposed</c>. Finalizing to an intent digest this Proposal already has rewrites that
    /// revision's stored JSON: the intent is the same, so only review metadata such as a citation changed. A failure
    /// leaves the Draft exactly as it was — none of the four effects is observable without all of them.
    /// </summary>
    /// <param name="draftName">The name identifying the Draft to commit.</param>
    /// <param name="intentDigest">The digest of the finalized Proposal content.</param>
    /// <param name="proposalJson">The finalized Proposal JSON, stored as the new revision.</param>
    /// <param name="label">The Proposal's short description.</param>
    /// <param name="expectedDraftRevision">The expected Draft content revision, or <see langword="null"/> to skip the check.</param>
    /// <param name="thenMove">A status move made in the same transaction; if it is refused, nothing is finalized.</param>
    /// <returns>
    /// <see cref="DraftFinalizationResult.Finalized"/> for a first commit,
    /// <see cref="DraftFinalizationResult.Amended"/> for a new revision, or
    /// <see cref="DraftFinalizationResult.DraftChanged"/> when the expected revision no longer matches and no rows changed, or
    /// <see cref="DraftFinalizationResult.MoveRefused"/> when <paramref name="thenMove"/> was refused and no rows changed, or
    /// <see cref="DraftFinalizationResult.RationaleChanged"/> when <paramref name="expectedRationale"/> is given and the
    /// Draft's Rationale is no longer that, and no rows changed.
    /// </returns>
    DraftFinalizationResult Finalize(string draftName, string intentDigest, string proposalJson, string label,
        string? expectedDraftRevision = null, FinalizeMove? thenMove = null, string? expectedRationale = null);
    /// <summary>
    /// Loads one finalized Proposal, verifying that its recorded revision exists and that the
    /// revision's own embedded id and recomputed digest agree with the pointer that named it.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No finalized Proposal is recorded under this id.</exception>
    /// <exception cref="InvalidDataException">
    /// The pointer names a revision that is missing, or the revision's embedded id or content digest
    /// disagrees with it (store inconsistency).
    /// </exception>
    (ProposalRecord Record, Proposal Envelope) GetFinalized(CanonicalId proposalId);
    /// <summary>
    /// Resolves <paramref name="requested"/>'s finalized prerequisite closure through <see cref="GetFinalized"/>
    /// and returns the Proposals that must prepare a Dry Run scratch, in deterministic topological order.
    /// Applied prerequisites (<paramref name="appliedProposalIds"/>) satisfy their dependency branch
    /// without requiring their revisions to be readable.
    /// </summary>
    PrerequisiteExecutionPlan PlanPrerequisites(Proposal requested, IReadOnlyCollection<Guid> appliedProposalIds);
    /// <summary>Sets the bound-DryRun anchor recorded against a Proposal's current revision, or clears it.</summary>
    void SetAnchor(CanonicalId proposalId, string? anchorJson);
    /// <summary>
    /// Gets one finalized (non-draft) Proposal's review state for a status transition, without
    /// requiring its revision to be readable — a transition reads and writes only <c>Proposals</c>.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No finalized Proposal is recorded under this id.</exception>
    ProposalRecord GetForTransition(CanonicalId proposalId);
    /// <summary>
    /// Moves a finalized Proposal to <paramref name="to"/> only if its status, read in the same store
    /// transaction as the write, is one of <paramref name="allowedFrom"/>; otherwise changes nothing and
    /// reports the status found, so an Apply that commits first is never overwritten.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No finalized Proposal is recorded under this id.</exception>
    StatusTransitionOutcome Transition(
        CanonicalId proposalId, IReadOnlyCollection<string> allowedFrom, string to, string? supersededBy = null);
    /// <summary>
    /// Writes a status with no check of the current one, for fixtures and for callers that already hold the
    /// right to overwrite; a command that moves a Proposal on a user's request uses <see cref="Transition"/>.
    /// </summary>
    void SetStatus(CanonicalId proposalId, string status, string? supersededBy);
    /// <summary>Records the Apply Receipt and marks its Proposal applied in one store transaction.</summary>
    void RecordAppliedReceipt(Receipt receipt);
    /// <summary>Whether a Draft is currently registered under this name.</summary>
    bool DraftNameExists(string draftName);
    /// <summary>
    /// Turns an already-finalized Proposal back into a Draft under a new local draft name, keeping its
    /// id and current committed content addressable until the draft is finalized again as an amend. Only its
    /// Draft name and content are set: a declined or withdrawn Proposal stays closed until the Draft is
    /// finalized, which opens it again. An applied or superseded one is never reopened.
    /// </summary>
    /// <exception cref="InvalidOperationException">A draft already exists under <paramref name="draftName"/>.</exception>
    /// <exception cref="KeyNotFoundException">No finalized Proposal is recorded under this id.</exception>
    void ReopenAsDraft(CanonicalId proposalId, string draftName, string draftJson);
    /// <summary>
    /// Discards a Draft, in one transaction, decided by whether it carries a committed revision.
    /// A never-finalized Draft (<c>CurrentIntentDigest IS NULL</c>) has no <c>ProposalRevisions</c> row
    /// yet, so its whole <c>Proposals</c> row is deleted and <paramref name="draftName"/> is free for
    /// immediate reuse. A Draft <see cref="ReopenAsDraft"/> produced keeps its source Proposal's
    /// <c>CurrentIntentDigest</c>, so only <c>DraftName</c> and <c>DraftJson</c> are cleared — the exact
    /// inverse of what <see cref="ReopenAsDraft"/> set, status included — leaving the Proposal exactly as committed, with
    /// its <c>ProposalRevisions</c> untouched.
    /// </summary>
    /// <returns>
    /// <c>true</c> when the discarded Draft had been reopened from a finalized Proposal, which is left
    /// exactly at its prior committed revision; <c>false</c> when its whole row was deleted instead.
    /// </returns>
    /// <param name="draftName">The Draft to discard.</param>
    /// <param name="expectedJson">The content the caller read, or <see langword="null"/> to skip the check.</param>
    /// <exception cref="KeyNotFoundException">No Draft is registered under this name.</exception>
    /// <exception cref="DraftChangedException">The Draft no longer holds <paramref name="expectedJson"/>.</exception>
    bool DiscardDraft(string draftName, string? expectedJson = null);
}

/// <summary>
/// The current pointer and review state of a Proposal. A non-null <see cref="DraftName"/> marks editable
/// content in <see cref="ProposalJson"/>. A reopened Draft retains its prior committed <see cref="IntentDigest"/>;
/// a never-finalized Draft has no committed digest.
/// </summary>
public sealed record ProposalRecord(
    CanonicalId ProposalId, string? IntentDigest, string? ProposalJson, string Status, string? Label,
    string? Rationale, string? SupersededBy, byte[]? ProposalJsonBytes = null,
    string? AnchorJson = null, string? ArchivedUtc = null, string? DraftName = null, string? Role = null)
{
    /// <summary>The intent digest the Rationale was last written for, or null before a Draft's first finalize.</summary>
    public string? RationaleIntentDigest { get; init; }

    /// <summary>When the Rationale was last written, as a round-trip UTC timestamp.</summary>
    public string? RationaleUpdatedUtc { get; init; }

    /// <summary>Whether this Proposal holds the pending changes, the window's default staging target.</summary>
    public bool IsPendingChanges => Role == ProposalRoles.PendingChanges;
}

/// <summary>The roles a Proposal row can carry; a row with no role is an ordinary Proposal.</summary>
public static class ProposalRoles
{
    /// <summary>
    /// The pending changes: the Proposal the window stages into by default and FieldWorks applies at its save
    /// boundary. At most one Draft carries it.
    /// </summary>
    public const string PendingChanges = "pending-changes";
}

/// <summary>An immutable Proposal revision, retaining the exact source JSON bytes.</summary>
public sealed record ProposalRevisionRecord(
    CanonicalId ProposalId, string IntentDigest, string ProposalJson, string Status, string? Label,
    string? Rationale, string? SupersededBy, string? CreatedUtc = null, byte[]? ProposalJsonBytes = null);


/// <summary>A Draft's content is no longer what the caller read, so the change was refused.</summary>
public sealed class DraftChangedException(string draftName)
    : InvalidOperationException($"Draft '{draftName}' changed. Reload it and try again.");

/// <summary>Another open Proposal already answers this Request.</summary>
public sealed class RequestProposalOpenException(string requestId) : InvalidOperationException
{
    /// <summary>The Request already answered by an open Proposal.</summary>
    public string RequestId { get; } = requestId;
}

/// <summary>A Draft has authored content that automatic cleanup must preserve.</summary>
internal sealed class DraftAuthoredContentException()
    : InvalidOperationException("This Draft has a Rationale or Comments or pictures; its authored content is kept.");

/// <summary>A Proposal's identity and label, read without its revision content.</summary>
public sealed record ProposalListing(CanonicalId ProposalId, string? Label);

/// <summary>Selection options for current Proposal rows.</summary>
public sealed record ProposalListFilter(string? Status = null, bool IncludeArchived = false);

/// <summary>Reads and writes normalized Proposal and review tables.</summary>
public sealed class ProposalRepository : IProposalRepository
{
    private readonly MotifDatabase _database;
    private readonly IJobClock _clock;

    /// <summary>Creates a repository over an already worker-owned database.</summary>
    public ProposalRepository(MotifDatabase database, IJobClock? clock = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _clock = clock ?? new SystemJobClock();
    }

    /// <inheritdoc />
    public ProposalRecord Get(CanonicalId proposalId)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = CurrentSelectSql + " WHERE p.ProposalId = $id;";
        command.Parameters.AddWithValue("$id", proposalId.Value);
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? ReadRecord(reader)
            : throw new KeyNotFoundException($"Proposal '{proposalId.Value}' was not found.");
    }

    /// <inheritdoc />
    public IReadOnlyList<ProposalRecord> List(ProposalListFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        var archived = filter.IncludeArchived ? "" :
            " AND (p.Status NOT IN ('applied', 'rejected', 'superseded', 'withdrawn') OR p.DraftName IS NOT NULL)";
        command.CommandText = """
            SELECT p.ProposalId, p.CurrentIntentDigest, r.ProposalJson,
                   p.Status, p.Label, p.Rationale, p.SupersededBy,
                   p.AnchorJson, p.ArchivedUtc, p.DraftName, p.DraftJson, p.Role,
                   p.RationaleIntentDigest, p.RationaleUpdatedUtc
            FROM Proposals p LEFT JOIN ProposalRevisions r ON r.ProposalId = p.ProposalId
                AND r.IntentDigest = p.CurrentIntentDigest
            WHERE ($status IS NULL OR p.Status = $status)
            """ + archived + " ORDER BY p.ProposalId;";
        command.Parameters.AddWithValue("$status", (object?)filter.Status ?? DBNull.Value);
        using var reader = command.ExecuteReader();
        var records = new List<ProposalRecord>();
        while (reader.Read()) records.Add(ReadRecord(reader));
        return records;
    }

    /// <summary>
    /// Lists the Proposals <see cref="List"/> would, with their label and without any revision content, so a
    /// caller that wants a few of them loads only those through <see cref="GetFinalized"/>.
    /// </summary>
    public IReadOnlyList<ProposalListing> ListWithoutContent(ProposalListFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        var archived = filter.IncludeArchived ? "" :
            " AND (p.Status NOT IN ('applied', 'rejected', 'superseded', 'withdrawn') OR p.DraftName IS NOT NULL)";
        command.CommandText = "SELECT p.ProposalId, p.Label FROM Proposals p WHERE ($status IS NULL OR p.Status = $status)" +
            archived + " ORDER BY p.ProposalId;";
        command.Parameters.AddWithValue("$status", (object?)filter.Status ?? DBNull.Value);
        using var reader = command.ExecuteReader();
        var listings = new List<ProposalListing>();
        while (reader.Read())
            listings.Add(new ProposalListing(CanonicalId.Parse(reader.GetString(0)), reader.IsDBNull(1) ? null : reader.GetString(1)));
        return listings;
    }

    /// <inheritdoc />
    public void SaveRevision(ProposalRevisionRecord revision)
    {
        _ = revision.ProposalId.Value;
        if (string.IsNullOrWhiteSpace(revision.IntentDigest) || revision.ProposalJson is null ||
            string.IsNullOrWhiteSpace(revision.Status))
            throw new ArgumentException("Revision digest, JSON, and status are required.", nameof(revision));

        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var created = revision.CreatedUtc ?? DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        var bytes = revision.ProposalJsonBytes ?? Encoding.UTF8.GetBytes(revision.ProposalJson);
        var archived = ProposalLifecycle.IsTerminal(revision.Status)
            ? _clock.UtcNow.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) : null;
        using (var parent = connection.CreateCommand())
        {
            parent.Transaction = transaction;
            parent.CommandText = """
                INSERT INTO Proposals
                    (ProposalId, CurrentIntentDigest, Status, Label, Rationale, RationaleIntentDigest, RationaleUpdatedUtc,
                     SupersededBy, ArchivedUtc)
                VALUES ($id, $digest, $status, $label, $rationale, CASE WHEN $rationale IS NULL THEN NULL ELSE $digest END,
                    CASE WHEN $rationale IS NULL THEN NULL ELSE $created END, $superseded, $archived)
                ON CONFLICT(ProposalId) DO UPDATE SET CurrentIntentDigest = excluded.CurrentIntentDigest,
                    Status = excluded.Status, Label = excluded.Label, Rationale = excluded.Rationale,
                    RationaleIntentDigest = excluded.RationaleIntentDigest,
                    RationaleUpdatedUtc = excluded.RationaleUpdatedUtc,
                    SupersededBy = excluded.SupersededBy, AnchorJson = NULL,
                    ArchivedUtc = CASE WHEN excluded.Status IN ('applied','rejected','superseded','withdrawn')
                        THEN COALESCE(Proposals.ArchivedUtc, excluded.ArchivedUtc) ELSE NULL END;
                """;
            AddParameters(parent, revision, bytes, created);
            parent.Parameters.AddWithValue("$superseded", (object?)revision.SupersededBy ?? DBNull.Value);
            parent.Parameters.AddWithValue("$archived", (object?)archived ?? DBNull.Value);
            parent.ExecuteNonQuery();
        }

        using (var check = connection.CreateCommand())
        {
            check.Transaction = transaction;
            check.CommandText = """
                SELECT ProposalJson
                FROM ProposalRevisions WHERE ProposalId = $id AND IntentDigest = $digest;
                """;
            check.Parameters.AddWithValue("$id", revision.ProposalId.Value);
            check.Parameters.AddWithValue("$digest", revision.IntentDigest);
            using var reader = check.ExecuteReader();
            if (reader.Read())
            {
                if (reader[0] is not byte[] existingBytes)
                    throw new InvalidDataException("Proposal revision JSON is not stored as a BLOB.");
                if (!existingBytes.SequenceEqual(bytes))
                    throw new InvalidDataException(
                        $"Proposal revision '{revision.IntentDigest}' already exists with different content.");
                transaction.Commit();
                return;
            }
        }
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO ProposalRevisions
                (ProposalId, IntentDigest, ProposalJson, CreatedUtc)
            VALUES ($id, $digest, $bytes, $created);
            """;
        AddParameters(insert, revision, bytes, created);
        insert.ExecuteNonQuery();
        transaction.Commit();
    }

    /// <inheritdoc />

    public IReadOnlyList<ProposalRecord> ListArchived(DateTimeOffset now, TimeRetentionPolicy? policy = null)
    {
        policy ??= TimeRetentionPolicy.Default;
        return List(new ProposalListFilter(IncludeArchived: true))
            .Where(proposal => ProposalLifecycle.IsTerminal(proposal.Status) && proposal.Status != ProposalLifecycle.ToWire(ProposalStatus.Applied) &&
                proposal.DraftName is null &&
                policy.ShouldPurge(ParseNullableUtc(proposal.ArchivedUtc), now)).ToArray();
    }

    /// <summary>Deletes an unreferenced terminal Proposal and the evidence nothing else retains.</summary>
    /// <param name="proposalId">The id of the Proposal to delete.</param>
    public void DeleteArchived(CanonicalId proposalId)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: false);
        ProposalDeletion.Delete(connection, transaction, proposalId);
        transaction.Commit();
        TrialScratchRetention.DeleteProposal(_database, proposalId);
    }

    private static void AddParameters(SqliteCommand command, ProposalRevisionRecord revision, byte[] bytes, string created)
    {
        command.Parameters.AddWithValue("$id", revision.ProposalId.Value);
        command.Parameters.AddWithValue("$digest", revision.IntentDigest);
        command.Parameters.AddWithValue("$json", revision.ProposalJson);
        command.Parameters.AddWithValue("$bytes", bytes);
        command.Parameters.AddWithValue("$status", revision.Status);
        command.Parameters.AddWithValue("$label", (object?)revision.Label ?? DBNull.Value);
        command.Parameters.AddWithValue("$rationale", (object?)revision.Rationale ?? DBNull.Value);
        command.Parameters.AddWithValue("$created", created);
    }

    /// <inheritdoc />
    public void CreateDraft(string draftName, CanonicalId proposalId, string draftJson, string? role = null)
    {
        if (string.IsNullOrWhiteSpace(draftName)) throw new ArgumentException("A draft name is required.", nameof(draftName));
        if (string.IsNullOrWhiteSpace(draftJson)) throw new ArgumentException("Draft content is required.", nameof(draftJson));
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        RequireDraftNameFree(connection, transaction, draftName);
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO Proposals (ProposalId, CurrentIntentDigest, Status, DraftName, DraftJson, Role)
            VALUES ($id, NULL, $status, $name, $json, $role);
            """;
        insert.Parameters.AddWithValue("$role", (object?)role ?? DBNull.Value);
        insert.Parameters.AddWithValue("$id", proposalId.Value);
        insert.Parameters.AddWithValue("$status", DraftStatus);
        insert.Parameters.AddWithValue("$name", draftName);
        insert.Parameters.AddWithValue("$json", draftJson);
        insert.ExecuteNonQuery();
        transaction.Commit();
    }

    /// <inheritdoc />
    public void SaveDraft(string draftName, string draftJson)
    {
        if (string.IsNullOrWhiteSpace(draftJson)) throw new ArgumentException("Draft content is required.", nameof(draftJson));
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Proposals SET DraftJson = $json WHERE DraftName = $name;";
        command.Parameters.AddWithValue("$json", draftJson);
        command.Parameters.AddWithValue("$name", draftName);
        if (command.ExecuteNonQuery() != 1) throw new KeyNotFoundException($"Draft '{draftName}' was not found.");
    }

    public bool TrySaveDraft(string draftName, string expectedJson, string draftJson)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Proposals SET DraftJson = $json WHERE DraftName = $name AND DraftJson = $expected;";
        command.Parameters.AddWithValue("$json", draftJson);
        command.Parameters.AddWithValue("$name", draftName);
        command.Parameters.AddWithValue("$expected", expectedJson);
        return command.ExecuteNonQuery() == 1;
    }

    /// <summary>Saves a Draft only while both its revision and the expected Baseline remain current.</summary>
    public bool TrySaveDraftForBaseline(string draftName, string expectedJson, string draftJson,
        string projectKey, BaselineToken expectedBaseline)
        => TrySaveDraftForExpectedContext(draftName, expectedJson, draftJson, projectKey,
            new ExpectedContext(expectedBaseline));

    /// <summary>Checks a reader's expected Baseline and Selection evidence in one read transaction.</summary>
    public bool IsCurrentExpectedContext(string projectKey, ExpectedContext expectedContext)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: true);
        var current = ExpectedContextRepository.IsAcceptableWriteContext(_database, connection, transaction,
            projectKey, expectedContext);
        transaction.Commit();
        return current;
    }

    /// <summary>Saves a Draft only while its revision and full expected Selection evidence remain current.</summary>
    public bool TrySaveDraftForExpectedContext(string draftName, string expectedJson, string draftJson,
        string projectKey, ExpectedContext expectedContext)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        if (!ExpectedContextRepository.IsCurrentSelectionEvidence(_database, connection, transaction,
                projectKey, expectedContext))
        {
            transaction.Commit();
            return false;
        }
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE Proposals SET DraftJson = $json
            WHERE DraftName = $name AND DraftJson = $expected AND EXISTS (
                SELECT 1 FROM Baselines b WHERE b.ProjectKey = $project
                    AND b.ProjectIdentity = $identity AND b.SemanticSnapshotDigest = $semantic
                    AND b.ProjectionVersion = $projection AND b.CapturedUtc = $captured
                    AND b.BundleDigest = $bundle
                    AND b.CapturedHostSessionId IS $hostSession
                    AND b.CapturedEditGeneration IS $editGeneration);
            """;
        command.Parameters.AddWithValue("$json", draftJson);
        command.Parameters.AddWithValue("$name", draftName);
        command.Parameters.AddWithValue("$expected", expectedJson);
        AddBaselineParameters(command, projectKey, expectedContext.Baseline);
        var saved = command.ExecuteNonQuery() == 1;
        transaction.Commit();
        return saved;
    }

    public bool TryCreateDraft(string draftName, CanonicalId proposalId, string draftJson, string? role = null)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Proposals (ProposalId, CurrentIntentDigest, Status, DraftName, DraftJson, Role)
            VALUES ($id, NULL, $status, $name, $json, $role)
            ON CONFLICT DO NOTHING;
            """;
        command.Parameters.AddWithValue("$role", (object?)role ?? DBNull.Value);
        command.Parameters.AddWithValue("$id", proposalId.Value);
        command.Parameters.AddWithValue("$status", DraftStatus);
        command.Parameters.AddWithValue("$name", draftName);
        command.Parameters.AddWithValue("$json", draftJson);
        return command.ExecuteNonQuery() == 1;
    }

    /// <summary>Creates a Draft only while the expected Baseline remains current.</summary>
    public bool TryCreateDraftForBaseline(string draftName, CanonicalId proposalId, string draftJson,
        string projectKey, BaselineToken expectedBaseline)
        => TryCreateDraftForExpectedContext(draftName, proposalId, draftJson, projectKey,
            new ExpectedContext(expectedBaseline));

    /// <summary>Creates a Draft only while the full expected Selection evidence remains current.</summary>
    public bool TryCreateDraftForExpectedContext(string draftName, CanonicalId proposalId, string draftJson,
        string projectKey, ExpectedContext expectedContext, string? role = null)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        if (!ExpectedContextRepository.IsCurrentSelectionEvidence(_database, connection, transaction,
                projectKey, expectedContext))
        {
            transaction.Commit();
            return false;
        }
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Proposals (ProposalId, CurrentIntentDigest, Status, DraftName, DraftJson, Role)
            SELECT $id, NULL, $status, $name, $json, $role
            WHERE EXISTS (
                SELECT 1 FROM Baselines b WHERE b.ProjectKey = $project
                    AND b.ProjectIdentity = $identity AND b.SemanticSnapshotDigest = $semantic
                    AND b.ProjectionVersion = $projection AND b.CapturedUtc = $captured
                    AND b.BundleDigest = $bundle
                    AND b.CapturedHostSessionId IS $hostSession
                    AND b.CapturedEditGeneration IS $editGeneration)
            ON CONFLICT DO NOTHING;
            """;
        command.Parameters.AddWithValue("$role", (object?)role ?? DBNull.Value);
        command.Parameters.AddWithValue("$id", proposalId.Value);
        command.Parameters.AddWithValue("$status", DraftStatus);
        command.Parameters.AddWithValue("$name", draftName);
        command.Parameters.AddWithValue("$json", draftJson);
        AddBaselineParameters(command, projectKey, expectedContext.Baseline);
        var created = command.ExecuteNonQuery() == 1;
        transaction.Commit();
        return created;
    }

    private static void AddBaselineParameters(SqliteCommand command, string projectKey, BaselineToken token)
    {
        command.Parameters.AddWithValue("$project", projectKey);
        command.Parameters.AddWithValue("$identity", token.ProjectIdentity);
        command.Parameters.AddWithValue("$semantic", token.SemanticSnapshotDigest);
        command.Parameters.AddWithValue("$projection", token.ProjectionVersion);
        command.Parameters.AddWithValue("$captured", token.CapturedUtc);
        command.Parameters.AddWithValue("$bundle", token.BundleDigest);
        command.Parameters.AddWithValue("$hostSession", (object?)token.CapturedHostSessionId ?? DBNull.Value);
        command.Parameters.AddWithValue("$editGeneration", (object?)token.CapturedEditGeneration ?? DBNull.Value);
    }

    /// <inheritdoc />
    public ProposalRecord GetDraft(string draftName)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = CurrentSelectSql + " WHERE p.DraftName = $name;";
        command.Parameters.AddWithValue("$name", draftName);
        RepositoryReadCounters.QueryExecuted();
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw new KeyNotFoundException($"Draft '{draftName}' was not found.");
        RepositoryReadCounters.RecordDeserialized();
        return ReadRecord(reader);
    }

    /// <inheritdoc />
    public ProposalRecord? FindPendingChangesDraft()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = DraftSelectSql + " WHERE Role = $role AND DraftName IS NOT NULL;";
        command.Parameters.AddWithValue("$role", ProposalRoles.PendingChanges);
        RepositoryReadCounters.QueryExecuted();
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        RepositoryReadCounters.RecordDeserialized();
        return ReadDraftRecord(reader);
    }

    /// <inheritdoc />
    public IReadOnlyList<ProposalRecord> ListDrafts()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = DraftSelectSql + " WHERE DraftName IS NOT NULL ORDER BY DraftName;";
        using var reader = command.ExecuteReader();
        var records = new List<ProposalRecord>();
        while (reader.Read()) records.Add(ReadDraftRecord(reader));
        return records;
    }

    /// <inheritdoc />
    public DraftFinalizationResult Finalize(string draftName, string intentDigest, string proposalJson, string label,
        string? expectedDraftRevision = null, FinalizeMove? thenMove = null, string? expectedRationale = null)
    {
        if (string.IsNullOrWhiteSpace(intentDigest) || string.IsNullOrWhiteSpace(proposalJson))
            throw new ArgumentException("Finalize requires an intent digest and Proposal content.", nameof(intentDigest));
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        string proposalId;
        bool wasAmend;
        string? draftJson;
        using (var find = connection.CreateCommand())
        {
            find.Transaction = transaction;
            find.CommandText = "SELECT ProposalId, CurrentIntentDigest, DraftJson FROM Proposals WHERE DraftName = $name;";
            find.Parameters.AddWithValue("$name", draftName);
            using var reader = find.ExecuteReader();
            if (!reader.Read()) throw new KeyNotFoundException($"Draft '{draftName}' was not found.");
            proposalId = reader.GetString(0);
            wasAmend = !reader.IsDBNull(1);
            draftJson = reader.IsDBNull(2) ? null : reader.GetString(2);
        }
        if (expectedDraftRevision is not null && DraftRevision.Compute(draftJson) != expectedDraftRevision)
            return DraftFinalizationResult.DraftChanged;

        var bytes = Encoding.UTF8.GetBytes(proposalJson);
        object? stored;
        using (var existing = connection.CreateCommand())
        {
            existing.Transaction = transaction;
            existing.CommandText = "SELECT ProposalJson FROM ProposalRevisions WHERE ProposalId = $id AND IntentDigest = $digest;";
            existing.Parameters.AddWithValue("$id", proposalId);
            existing.Parameters.AddWithValue("$digest", intentDigest);
            stored = existing.ExecuteScalar();
        }
        if (stored is byte[] storedBytes && !storedBytes.SequenceEqual(bytes))
        {
            // Same intent digest, so only review metadata changed: the revision takes the new bytes.
            using var amend = connection.CreateCommand();
            amend.Transaction = transaction;
            amend.CommandText =
                "UPDATE ProposalRevisions SET ProposalJson = $bytes WHERE ProposalId = $id AND IntentDigest = $digest;";
            amend.Parameters.AddWithValue("$id", proposalId);
            amend.Parameters.AddWithValue("$digest", intentDigest);
            amend.Parameters.AddWithValue("$bytes", bytes);
            amend.ExecuteNonQuery();
        }
        else if (stored is null || stored is DBNull)
        {
            using var revision = connection.CreateCommand();
            revision.Transaction = transaction;
            revision.CommandText = """
                INSERT INTO ProposalRevisions (ProposalId, IntentDigest, ProposalJson, CreatedUtc)
                VALUES ($id, $digest, $bytes, $created);
                """;
            revision.Parameters.AddWithValue("$id", proposalId);
            revision.Parameters.AddWithValue("$digest", intentDigest);
            revision.Parameters.AddWithValue("$bytes", bytes);
            revision.Parameters.AddWithValue(
                "$created", _clock.UtcNow.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            revision.ExecuteNonQuery();
        }
        using (var commit = connection.CreateCommand())
        {
            commit.Transaction = transaction;
            commit.CommandText = """
                UPDATE Proposals SET CurrentIntentDigest = $digest, Status = $status, Label = $label,
                    RationaleIntentDigest = COALESCE(RationaleIntentDigest, $digest),
                    DraftName = NULL, DraftJson = NULL, ArchivedUtc = NULL,
                    AnchorJson = CASE WHEN json_extract(AnchorJson, '$.IntentDigest') = $digest
                        OR json_extract(AnchorJson, '$.intentDigest') = $digest THEN AnchorJson ELSE NULL END
                WHERE DraftName = $name AND ($expectedRationale IS NULL OR Rationale IS $expectedRationale);
                """;
            commit.Parameters.AddWithValue("$expectedRationale", (object?)expectedRationale ?? DBNull.Value);
            commit.Parameters.AddWithValue("$digest", intentDigest);
            commit.Parameters.AddWithValue("$status", ProposalLifecycle.ToWire(ProposalStatus.Proposed));
            commit.Parameters.AddWithValue("$label", label);
            commit.Parameters.AddWithValue("$name", draftName);
            if (commit.ExecuteNonQuery() != 1) return DraftFinalizationResult.RationaleChanged;
        }
        ProposalDiscussionRepository.StampDigest(connection, transaction, proposalId, intentDigest);
        RequestRepository.AnswerFor(connection, transaction, proposalId, Now());
        if (thenMove is not null &&
            !MoveInTransaction(connection, transaction, CanonicalId.Parse(proposalId), thenMove.AllowedFrom,
                thenMove.To, null, thenMove.Comment).Moved)
            return DraftFinalizationResult.MoveRefused;
        transaction.Commit();
        if (thenMove is not null && !ProposalDifferenceRetention.KeepsWordDetail(thenMove.To))
        {
            ReviewRowArtifacts.DeleteReleased(_database, proposalId);
            TrialScratchRetention.TrySweepReviewInputs(_database);
        }
        return wasAmend ? DraftFinalizationResult.Amended : DraftFinalizationResult.Finalized;
    }

    /// <inheritdoc />
    public (ProposalRecord Record, Proposal Envelope) GetFinalized(CanonicalId proposalId)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT p.CurrentIntentDigest, r.ProposalJson, p.Status, p.Label, p.Rationale, p.SupersededBy, p.AnchorJson,
                   p.RationaleIntentDigest, p.RationaleUpdatedUtc
            FROM Proposals p LEFT JOIN ProposalRevisions r
                ON r.ProposalId = p.ProposalId AND r.IntentDigest = p.CurrentIntentDigest
            WHERE p.ProposalId = $id AND p.DraftName IS NULL;
            """;
        command.Parameters.AddWithValue("$id", proposalId.Value);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new KeyNotFoundException(
                $"Proposal '{proposalId.Value}' not found in store. Run 'list' to see " +
                "committed proposals.");
        }

        var digest = reader.IsDBNull(0) ? null : reader.GetString(0);
        if (digest is null || reader[1] is not byte[] bytes)
        {
            throw new InvalidDataException(
                $"Proposal '{proposalId.Value}' manifest points at intentDigest '{digest}', but no " +
                "matching revision is recorded (store inconsistency).");
        }

        var status = reader.GetString(2);
        var label = reader.IsDBNull(3) ? null : reader.GetString(3);
        var comment = reader.IsDBNull(4) ? null : reader.GetString(4);
        var supersededBy = reader.IsDBNull(5) ? null : reader.GetString(5);
        var anchorJson = reader.IsDBNull(6) ? null : reader.GetString(6);

        string json;
        try
        {
            json = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException(
                "The stored Proposal JSON is not valid UTF-8 (store inconsistency).", exception);
        }

        var envelope = ProposalJsonParser.Parse(json);
        if (!string.Equals(envelope.ProposalId.Value, proposalId.Value, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Proposal store identity is inconsistent: lookup id {proposalId.Value} names a " +
                $"revision whose Proposal id is {envelope.ProposalId.Value} (store inconsistency).");
        }

        var actualDigest = IntentDigest.Compute(envelope);
        if (!string.Equals(actualDigest, digest, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Proposal {proposalId.Value} revision content computes intentDigest '{actualDigest}', " +
                $"not the bound '{digest}' (store inconsistency).");
        }

        var record = new ProposalRecord(proposalId, digest, json, status, label, comment, supersededBy, bytes, anchorJson)
        {
            RationaleIntentDigest = Text(reader, 7), RationaleUpdatedUtc = Text(reader, 8),
        };
        return (record, envelope);
    }

    /// <inheritdoc />
    public PrerequisiteExecutionPlan PlanPrerequisites(Proposal requested, IReadOnlyCollection<Guid> appliedProposalIds)
    {
        if (requested is null) throw new ArgumentNullException(nameof(requested));
        if (appliedProposalIds is null) throw new ArgumentNullException(nameof(appliedProposalIds));

        var candidates = PrerequisiteClosureResolver.Resolve(
            requested,
            id => GetFinalized(CanonicalId.Parse(id)).Envelope,
            appliedProposalIds);
        return PrerequisiteExecutionPlan.Create(requested, candidates, appliedProposalIds);
    }

    /// <summary>Captures the exact finalized revisions reachable from a Proposal's declared dependencies.</summary>
    public IReadOnlyList<FrozenProposalRevision> FreezePrerequisites(Proposal requested)
    {
        ArgumentNullException.ThrowIfNull(requested);
        var frozen = new Dictionary<string, FrozenProposalRevision>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal) { requested.ProposalId.Value };
        Visit(requested);
        return Array.AsReadOnly(frozen.Values.OrderBy(item => item.ProposalId, StringComparer.Ordinal).ToArray());

        void Visit(Proposal proposal)
        {
            foreach (var required in proposal.Requires)
            {
                if (!visited.Add(required.Value) || required.Value == requested.ProposalId.Value) continue;
                var (record, envelope) = GetFinalized(required);
                if (record.ProposalJson is null || record.IntentDigest is null ||
                    record.IntentDigest != IntentDigest.Compute(envelope))
                    throw new InvalidDataException("A prerequisite revision has no consistent frozen content.");
                var revision = FrozenProposalRevision.Create(record.ProposalJson);
                if (revision.ProposalId != required.Value || revision.IntentDigest != record.IntentDigest)
                    throw new InvalidDataException("A prerequisite revision does not match its declared identity.");
                frozen.Add(required.Value, revision);
                Visit(envelope);
            }
        }
    }

    /// <inheritdoc />
    public void SetAnchor(CanonicalId proposalId, string? anchorJson)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Proposals SET AnchorJson = $anchor WHERE ProposalId = $id;";
        command.Parameters.AddWithValue("$anchor", (object?)anchorJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$id", proposalId.Value);
        command.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public ProposalRecord GetForTransition(CanonicalId proposalId)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Status, Label, Rationale, SupersededBy, CurrentIntentDigest,
                   RationaleIntentDigest, RationaleUpdatedUtc
            FROM Proposals WHERE ProposalId = $id AND DraftName IS NULL;
            """;
        command.Parameters.AddWithValue("$id", proposalId.Value);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new KeyNotFoundException(
                $"Proposal '{proposalId.Value}' not found in store. Run 'list' to see " +
                "committed proposals.");
        }

        return new ProposalRecord(
            proposalId,
            reader.IsDBNull(4) ? null : reader.GetString(4),
            null,
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3))
        {
            RationaleIntentDigest = Text(reader, 5), RationaleUpdatedUtc = Text(reader, 6),
        };
    }

    private static string? Text(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    /// <inheritdoc />
    public bool SetRationale(CanonicalId proposalId, string? rationale, DiscussionAuthor? author = null,
        RationaleWriteExpectation? expected = null)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE Proposals SET Rationale = $text, RationaleIntentDigest = CurrentIntentDigest,
                RationaleUpdatedUtc = $now WHERE ProposalId = $id
                AND ($expected = 0 OR (Status IS $status AND DraftName IS $draftName))
                AND ($replaces = 0 OR Rationale IS $replaced);
            """;
        update.Parameters.AddWithValue("$replaces", expected?.Rationale is null ? 0 : 1);
        update.Parameters.AddWithValue("$replaced", (object?)expected?.Rationale ?? DBNull.Value);
        update.Parameters.AddWithValue("$expected", expected is null ? 0 : 1);
        update.Parameters.AddWithValue("$status", (object?)expected?.Status ?? DBNull.Value);
        update.Parameters.AddWithValue("$draftName", (object?)expected?.DraftName ?? DBNull.Value);
        var now = Now();
        update.Parameters.AddWithValue("$text", (object?)rationale ?? DBNull.Value);
        update.Parameters.AddWithValue("$now", now);
        update.Parameters.AddWithValue("$id", proposalId.Value);
        if (update.ExecuteNonQuery() != 1) return false;
        if (rationale is not null)
            ProposalDiscussionRepository.RecordRationaleRevision(connection, transaction, proposalId.Value, rationale,
                author ?? new DiscussionAuthor(null, ""), now);
        transaction.Commit();
        return true;
    }

    /// <inheritdoc />
    public Proposal? GetRevisionContent(CanonicalId proposalId, string intentDigest)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ProposalJson FROM ProposalRevisions WHERE ProposalId = $id AND IntentDigest = $digest;";
        command.Parameters.AddWithValue("$id", proposalId.Value);
        command.Parameters.AddWithValue("$digest", intentDigest);
        if (command.ExecuteScalar() is not byte[] bytes) return null;
        return ProposalJsonParser.Parse(new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'));
    }

    private string Now() => _clock.UtcNow.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public StatusTransitionOutcome Transition(
        CanonicalId proposalId, IReadOnlyCollection<string> allowedFrom, string to, string? supersededBy = null)
        => TransitionCore(proposalId, allowedFrom, to, supersededBy, null);

    /// <summary>Records a person's Comment and moves the Proposal in the same transaction.</summary>
    public StatusTransitionOutcome TransitionWithComment(
        CanonicalId proposalId, IReadOnlyCollection<string> allowedFrom, string to, string comment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(comment);
        return TransitionCore(proposalId, allowedFrom, to, null, comment);
    }

    private StatusTransitionOutcome TransitionCore(CanonicalId proposalId, IReadOnlyCollection<string> allowedFrom,
        string to, string? supersededBy, string? comment)
    {
        ArgumentNullException.ThrowIfNull(allowedFrom);
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var outcome = MoveInTransaction(connection, transaction, proposalId, allowedFrom, to, supersededBy, comment);
        if (outcome.Moved)
        {
            transaction.Commit();
            if (!ProposalDifferenceRetention.KeepsWordDetail(to))
            {
                ReviewRowArtifacts.DeleteReleased(_database, proposalId.Value);
                TrialScratchRetention.TrySweepReviewInputs(_database);
            }
        }
        return outcome;
    }

    private StatusTransitionOutcome MoveInTransaction(SqliteConnection connection, SqliteTransaction transaction,
        CanonicalId proposalId, IReadOnlyCollection<string> allowedFrom, string to, string? supersededBy,
        string? comment)
    {
        var placeholders = string.Join(", ", allowedFrom.Select((_, i) => "$from" + i));
        if (comment is not null)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = $"""
                INSERT INTO ProposalComments (ProposalId, Kind, AuthorLabel, PersonName, Text, IntentDigest, CreatedUtc)
                SELECT ProposalId, 'standard', NULL, '', $text, CurrentIntentDigest, $now
                FROM Proposals WHERE ProposalId = $id AND DraftName IS NULL AND Status IN ({placeholders});
                """;
            insert.Parameters.AddWithValue("$text", comment);
            insert.Parameters.AddWithValue("$now", Now());
            insert.Parameters.AddWithValue("$id", proposalId.Value);
            var fromIndex = 0;
            foreach (var from in allowedFrom) insert.Parameters.AddWithValue("$from" + fromIndex++, from);
            insert.ExecuteNonQuery();
        }
        using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = $"""
            UPDATE Proposals SET Status = $status, SupersededBy = $superseded
            WHERE ProposalId = $id AND DraftName IS NULL AND Status IN ({placeholders});
            """;
        update.Parameters.AddWithValue("$status", to);
        update.Parameters.AddWithValue("$superseded", (object?)supersededBy ?? DBNull.Value);
        update.Parameters.AddWithValue("$id", proposalId.Value);
        var index = 0;
        foreach (var from in allowedFrom) update.Parameters.AddWithValue("$from" + index++, from);
        if (update.ExecuteNonQuery() > 0)
        {
            DropWordDetailIfDecided(connection, transaction, proposalId.Value, to);
            ReopenRequestIfUndecided(connection, transaction, proposalId.Value, to);
            return new StatusTransitionOutcome(true, to);
        }

        using var read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = "SELECT Status FROM Proposals WHERE ProposalId = $id AND DraftName IS NULL;";
        read.Parameters.AddWithValue("$id", proposalId.Value);
        var observed = read.ExecuteScalar() as string ??
            throw new KeyNotFoundException(
                $"Proposal '{proposalId.Value}' not found in store. Run 'list' to see committed proposals.");
        return new StatusTransitionOutcome(false, observed);
    }

    /// <inheritdoc />
    public void SetStatus(CanonicalId proposalId, string status, string? supersededBy)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = "UPDATE Proposals SET Status = $status, SupersededBy = $superseded WHERE ProposalId = $id;";
        update.Parameters.AddWithValue("$status", status);
        update.Parameters.AddWithValue("$superseded", (object?)supersededBy ?? DBNull.Value);
        update.Parameters.AddWithValue("$id", proposalId.Value);
        update.ExecuteNonQuery();
        DropWordDetailIfDecided(connection, transaction, proposalId.Value, status);
        ReopenRequestIfUndecided(connection, transaction, proposalId.Value, status);
        transaction.Commit();
        if (!ProposalDifferenceRetention.KeepsWordDetail(status))
        {
            ReviewRowArtifacts.DeleteReleased(_database, proposalId.Value);
            TrialScratchRetention.TrySweepReviewInputs(_database);
        }
    }

    // A decline keeps its Request answered; a withdrawal or replacement reopens it.
    private static void ReopenRequestIfUndecided(SqliteConnection connection, SqliteTransaction transaction,
        string proposalId, string status)
    {
        if (status is ManifestStatus.Withdrawn or ManifestStatus.Superseded)
            RequestRepository.ReopenFor(connection, transaction, proposalId);
    }

    private void DropWordDetailIfDecided(SqliteConnection connection, SqliteTransaction transaction, string proposalId,
        string status)
    {
        if (!ProposalDifferenceRetention.KeepsWordDetail(status))
        {
            ProposalDifferenceRetention.DropWordDetail(connection, transaction, proposalId, Now());
            ReviewReportRetention.DropDetail(connection, transaction, proposalId, Now());
        }
        ProposalDiscussionRepository.ClearRationaleHistoryIfDecided(connection, transaction, proposalId);
        if (ProposalLifecycle.IsTerminal(status))
            EvidenceArtifactRepository.ReleaseClosedProposalPins(connection, transaction, proposalId);
    }

    /// <inheritdoc />
    public void RecordAppliedReceipt(Receipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var current = connection.CreateCommand();
        current.Transaction = transaction;
        current.CommandText = "SELECT CurrentIntentDigest FROM Proposals WHERE ProposalId = $id;";
        current.Parameters.AddWithValue("$id", receipt.ProposalId.Value);
        if (!string.Equals(current.ExecuteScalar() as string, receipt.IntentDigest, StringComparison.Ordinal))
            throw new InvalidDataException("The Receipt digest does not match the Proposal's current revision.");
        if (!receipt.IntentDigest.StartsWith("sha256:", StringComparison.Ordinal) ||
            !string.Equals(receipt.AppliedLogEntry.IntentDigest, receipt.IntentDigest[7..], StringComparison.Ordinal))
            throw new InvalidDataException("The Receipt digest does not match the project's applied log entry.");
        using var existing = connection.CreateCommand();
        existing.Transaction = transaction;
        existing.CommandText = "SELECT IntentDigest FROM Receipts WHERE ProposalId = $id;";
        existing.Parameters.AddWithValue("$id", receipt.ProposalId.Value);
        var recordedDigest = existing.ExecuteScalar() as string;
        if (recordedDigest is not null &&
            !string.Equals(recordedDigest, receipt.IntentDigest, StringComparison.Ordinal))
            throw new InvalidDataException("The Proposal already has a Receipt for different content.");
        if (recordedDigest is null)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO Receipts (ReceiptId, ProposalId, IntentDigest, ReceiptJson, RecordedUtc)
                VALUES ($receiptId, $proposalId, $digest, $json, $recordedUtc);
                """;
            insert.Parameters.AddWithValue("$receiptId", CanonicalId.Mint().Value);
            insert.Parameters.AddWithValue("$proposalId", receipt.ProposalId.Value);
            insert.Parameters.AddWithValue("$digest", receipt.IntentDigest);
            insert.Parameters.AddWithValue("$json", JsonSerializer.Serialize(receipt));
            insert.Parameters.AddWithValue("$recordedUtc", _clock.UtcNow.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            insert.ExecuteNonQuery();
        }
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE Proposals SET Status = 'applied' WHERE ProposalId = $id;";
        command.Parameters.AddWithValue("$id", receipt.ProposalId.Value);
        command.ExecuteNonQuery();
        DropWordDetailIfDecided(connection, transaction, receipt.ProposalId.Value, ProposalLifecycle.ToWire(ProposalStatus.Applied));
        if (recordedDigest is null)
        {
            using var drift = connection.CreateCommand();
            drift.Transaction = transaction;
            drift.CommandText = "UPDATE Proposals SET AnchorJson = NULL WHERE ProposalId <> $id;";
            drift.Parameters.AddWithValue("$id", receipt.ProposalId.Value);
            drift.ExecuteNonQuery();
        }
        transaction.Commit();
        ReviewRowArtifacts.DeleteReleased(_database, receipt.ProposalId.Value);
        TrialScratchRetention.DeleteProposal(_database, receipt.ProposalId);
    }

    // On the caller's own transaction: a second connection cannot see uncommitted rows, leaving a window.
    private static void RequireDraftNameFree(SqliteConnection connection, SqliteTransaction transaction,
        string draftName)
    {
        using var check = connection.CreateCommand();
        check.Transaction = transaction;
        check.CommandText = "SELECT 1 FROM Proposals WHERE DraftName = $name;";
        check.Parameters.AddWithValue("$name", draftName);
        if (check.ExecuteScalar() is null) return;
        // Deliberately silent on discard-draft: it removes the colliding draft, not this call's caller.
        throw new InvalidOperationException($"Draft '{draftName}' already exists. Finalize it, or use " +
            "another name.");
    }

    /// <inheritdoc />
    public bool DraftNameExists(string draftName)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM Proposals WHERE DraftName = $name;";
        command.Parameters.AddWithValue("$name", draftName);
        RepositoryReadCounters.QueryExecuted();
        return command.ExecuteScalar() is not null;
    }

    /// <inheritdoc />
    public void ReopenAsDraft(CanonicalId proposalId, string draftName, string draftJson)
    {
        if (string.IsNullOrWhiteSpace(draftName)) throw new ArgumentException("A draft name is required.", nameof(draftName));
        if (string.IsNullOrWhiteSpace(draftJson)) throw new ArgumentException("Draft content is required.", nameof(draftJson));
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        RequireDraftNameFree(connection, transaction, draftName);
        if (RequestRepository.ConflictingRequest(connection, transaction, proposalId.Value) is { } requestId)
            throw new RequestProposalOpenException(requestId);
        using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE Proposals SET DraftName = $name, DraftJson = $json
            WHERE ProposalId = $id AND CurrentIntentDigest IS NOT NULL AND Status NOT IN ('applied', 'superseded');
            """;
        update.Parameters.AddWithValue("$name", draftName);
        update.Parameters.AddWithValue("$json", draftJson);
        update.Parameters.AddWithValue("$id", proposalId.Value);
        if (update.ExecuteNonQuery() != 1)
        {
            throw new KeyNotFoundException(
                $"Proposal '{proposalId.Value}' not found in store. Run 'list' to see " +
                "committed proposals.");
        }
        transaction.Commit();
    }

    /// <inheritdoc />
    public bool DiscardDraft(string draftName, string? expectedJson = null) =>
        DiscardDraft(draftName, expectedJson, beforeEvidenceRemoved: null);

    /// <summary>
    /// Discards a Draft like <see cref="DiscardDraft(string, string?)"/>. A never-finalized Draft first loses the
    /// evidence its Trials recorded, and is kept, with nothing changed, when that evidence is still pinned or retained.
    /// </summary>
    /// <param name="draftName">The Draft to discard.</param>
    /// <param name="expectedJson">The content the caller read, or <see langword="null"/> to skip the check.</param>
    /// <param name="beforeEvidenceRemoved">
    /// Called inside the transaction for a never-finalized Draft, before its evidence goes, so the caller can remove
    /// records the store does not link to the Proposal (its Trial jobs).
    /// </param>
    /// <param name="emptyDraftId">When supplied, requires this never-finalized Draft with no authored content.</param>
    /// <exception cref="ProposalRetainedByExampleSetException">A phonology Example Set cites the Draft.</exception>
    /// <exception cref="ProposalEvidenceRetainedException">A result of the Draft is pinned or retained.</exception>
    internal bool DiscardDraft(string draftName, string? expectedJson,
        Action<SqliteConnection, SqliteTransaction, CanonicalId>? beforeEvidenceRemoved, CanonicalId? emptyDraftId = null)
    {
        if (string.IsNullOrWhiteSpace(draftName)) throw new ArgumentException("A draft name is required.", nameof(draftName));
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        bool wasReopened;
        CanonicalId proposalId;
        using (var find = connection.CreateCommand())
        {
            find.Transaction = transaction;
            find.CommandText = "SELECT CurrentIntentDigest, DraftJson, ProposalId FROM Proposals WHERE DraftName = $name;";
            find.Parameters.AddWithValue("$name", draftName);
            using var reader = find.ExecuteReader();
            if (!reader.Read()) throw new KeyNotFoundException($"Draft '{draftName}' was not found.");
            if (expectedJson is not null && (reader.IsDBNull(1) || reader.GetString(1) != expectedJson))
                throw new DraftChangedException(draftName);
            wasReopened = !reader.IsDBNull(0);
            proposalId = CanonicalId.Parse(reader.GetString(2));
        }
        if (emptyDraftId is { } expectedId)
        {
            if (wasReopened || proposalId != expectedId || expectedJson is null)
                throw new DraftChangedException(draftName);
            using var discussion = connection.CreateCommand();
            discussion.Transaction = transaction;
            discussion.CommandText = """
                SELECT Role, Rationale,
                    EXISTS (SELECT 1 FROM ProposalComments WHERE ProposalId = $id),
                    EXISTS (SELECT 1 FROM ProposalRationaleRevisions WHERE ProposalId = $id),
                    EXISTS (SELECT 1 FROM ProposalPictures WHERE ProposalId = $id)
                FROM Proposals WHERE ProposalId = $id;
                """;
            discussion.Parameters.AddWithValue("$id", proposalId.Value);
            using var reader = discussion.ExecuteReader();
            if (!reader.Read() || (!reader.IsDBNull(0) && reader.GetString(0) == ProposalRoles.PendingChanges))
                throw new DraftChangedException(draftName);
            if (!reader.IsDBNull(1) || reader.GetBoolean(2) || reader.GetBoolean(3) || reader.GetBoolean(4))
                throw new DraftAuthoredContentException();
        }
        if (!wasReopened)
        {
            beforeEvidenceRemoved?.Invoke(connection, transaction, proposalId);
            ProposalDeletion.RemoveEvidence(connection, transaction, proposalId.Value);
        }
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // A reopened draft only ever had DraftName/DraftJson set; clearing just those undoes exactly that.
        command.CommandText = wasReopened
            ? "UPDATE Proposals SET DraftName = NULL, DraftJson = NULL WHERE DraftName = $name;"
            : "DELETE FROM Proposals WHERE DraftName = $name;";
        command.Parameters.AddWithValue("$name", draftName);
        command.ExecuteNonQuery();
        // The reopened Proposal is decided again, so what was revised while it was open is history to drop.
        if (wasReopened) ProposalDiscussionRepository.ClearRationaleHistoryIfDecided(connection, transaction, proposalId.Value);
        transaction.Commit();
        TrialScratchRetention.DeleteProposal(_database, proposalId);
        return wasReopened;
    }

    private const string CurrentSelectSql = """
        SELECT p.ProposalId, p.CurrentIntentDigest, r.ProposalJson,
               p.Status, p.Label, p.Rationale, p.SupersededBy,
               p.AnchorJson, p.ArchivedUtc, p.DraftName, p.DraftJson, p.Role,
               p.RationaleIntentDigest, p.RationaleUpdatedUtc
        FROM Proposals p LEFT JOIN ProposalRevisions r ON r.ProposalId = p.ProposalId
            AND r.IntentDigest = p.CurrentIntentDigest
        """;

    private const string DraftSelectSql =
        "SELECT ProposalId, Status, Label, Rationale, SupersededBy, AnchorJson, ArchivedUtc, DraftName, DraftJson, Role, " +
        "RationaleIntentDigest, RationaleUpdatedUtc, CurrentIntentDigest " +
        "FROM Proposals";

    private static ProposalRecord ReadDraftRecord(SqliteDataReader reader)
    {
        var proposalId = CanonicalId.Parse(reader.GetString(0));
        return new ProposalRecord(proposalId, Text(reader, 12), reader.IsDBNull(8) ? null : reader.GetString(8), reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4), null,
            reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7), reader.IsDBNull(9) ? null : reader.GetString(9))
        {
            RationaleIntentDigest = Text(reader, 10), RationaleUpdatedUtc = Text(reader, 11),
        };
    }

    private static ProposalRecord ReadRecord(SqliteDataReader reader)
    {
        var proposalId = CanonicalId.Parse(reader.GetString(0));
        var draftName = reader.IsDBNull(9) ? null : reader.GetString(9);
        var intentDigest = reader.IsDBNull(1) ? null : reader.GetString(1);
        string? json;
        byte[]? bytes = null;
        if (draftName is not null)
        {
            // A reopened Draft keeps its committed digest, but reads must show its editable content.
            json = reader.IsDBNull(10) ? null : reader.GetString(10);
        }
        else
        {
            if (reader.IsDBNull(2) || reader[2] is not byte[] committedBytes)
                throw new InvalidDataException("Stored Proposal JSON must be a non-null BLOB (store inconsistency).");
            bytes = committedBytes;
            try
            {
                json = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
            }
            catch (DecoderFallbackException exception)
            {
                throw new InvalidDataException("The stored Proposal JSON is not valid UTF-8.", exception);
            }
        }
        return new ProposalRecord(proposalId, intentDigest, json, reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6), bytes,
            reader.IsDBNull(7) ? null : reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetString(8),
            draftName, reader.IsDBNull(11) ? null : reader.GetString(11))
        {
            RationaleIntentDigest = Text(reader, 12), RationaleUpdatedUtc = Text(reader, 13),
        };
    }

    private const string DraftStatus = "draft";

    private static DateTimeOffset? ParseNullableUtc(string? value) => value is null ? null : DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
}

/// <summary>What a guarded status write did: whether it moved the Proposal, and the status it left it in.</summary>
/// <param name="Moved">True when the Proposal was in an allowed origin status and now has the new one.</param>
/// <param name="Status">The new status when moved, otherwise the status found in the same transaction.</param>
public sealed record StatusTransitionOutcome(bool Moved, string Status);

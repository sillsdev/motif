using SIL.Motif.Projection.Store;
using System.Globalization;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Host.Store;

namespace SIL.Motif.Worker.Store;

/// <summary>The states a Request is in.</summary>
public static class RequestStatuses
{
    /// <summary>Asked, and no finalized Proposal answers it yet.</summary>
    public const string Open = "open";

    /// <summary>A Proposal that answers it was finalized and has not been withdrawn or superseded.</summary>
    public const string Answered = "answered";

    /// <summary>The linguist took it back.</summary>
    public const string Withdrawn = ManifestStatus.Withdrawn;
}

/// <summary>What a Request is about.</summary>
public static class RequestTargetKinds
{
    /// <summary>A word, named by its written form.</summary>
    public const string Word = "word";

    /// <summary>A Warning, named by its code and subject.</summary>
    public const string Warning = "warning";

    /// <summary>One cell of the Compare matrix.</summary>
    public const string MatrixCell = "matrix-cell";

    /// <summary>Every kind a Request can name.</summary>
    public static IReadOnlyList<string> All { get; } = [Word, Warning, MatrixCell];
}

/// <summary>One Request: what the linguist asked an assistant to fix.</summary>
/// <param name="RequestId">The Request's stable id.</param>
/// <param name="TargetKind">A <see cref="RequestTargetKinds"/> value.</param>
/// <param name="TargetId">The word, Warning or cell the Request names, as the window identified it.</param>
/// <param name="TargetLabel">A short label a person or assistant can read.</param>
/// <param name="Note">The linguist's own words, or null when there are none.</param>
/// <param name="Status">A <see cref="RequestStatuses"/> value.</param>
/// <param name="CreatedUtc">When it was asked.</param>
/// <param name="ClosedUtc">When it was last answered or withdrawn; null while open.</param>
/// <param name="AskedAgainOf">The Request this one was asked again from, or null.</param>
public sealed record RequestRecord(
    string RequestId, string TargetKind, string TargetId, string TargetLabel, string? Note, string Status,
    string CreatedUtc, string? ClosedUtc, string? AskedAgainOf);

/// <summary>What a Request write did.</summary>
public enum RequestWriteOutcome
{
    /// <summary>The write took effect.</summary>
    Written,
    /// <summary>No Request has that id.</summary>
    NotFound,
    /// <summary>The Request is not open.</summary>
    NotOpen,
    /// <summary>Another Proposal that is not yet decided already answers the Request.</summary>
    ProposalOpen,
}

/// <summary>
/// The Requests of one store. A Request is authored by the linguist, so a store upgrade keeps it as it keeps a Proposal.
/// A Proposal answers a Request through the Proposal's own <c>RequestId</c>; the lifecycle hooks here run inside the
/// Proposal's own transaction so no path can finalize or withdraw without moving the Request.
/// </summary>
public sealed class RequestRepository
{
    private readonly MotifDatabase _database;
    private readonly IJobClock _clock;

    /// <summary>Opens the Requests of one store.</summary>
    public RequestRepository(MotifDatabase database, IJobClock? clock = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _clock = clock ?? new SystemJobClock();
    }

    private const string Columns =
        "RequestId, TargetKind, TargetId, TargetLabel, Note, Status, CreatedUtc, ClosedUtc, AskedAgainOf";

    /// <summary>Stores a new open Request.</summary>
    public RequestRecord Create(string targetKind, string targetId, string targetLabel, string? note,
        string? askedAgainOf = null)
    {
        var record = new RequestRecord(CanonicalId.Mint("request/").Value, targetKind, targetId, targetLabel, note,
            RequestStatuses.Open, Now(), null, askedAgainOf);
        using var connection = _database.OpenConnection();
        using var insert = connection.CreateCommand();
        insert.CommandText = $"INSERT INTO Requests ({Columns}) VALUES ($id, $kind, $target, $label, $note, $status, $created, NULL, $again);";
        insert.Parameters.AddWithValue("$id", record.RequestId);
        insert.Parameters.AddWithValue("$kind", record.TargetKind);
        insert.Parameters.AddWithValue("$target", record.TargetId);
        insert.Parameters.AddWithValue("$label", record.TargetLabel);
        insert.Parameters.AddWithValue("$note", (object?)record.Note ?? DBNull.Value);
        insert.Parameters.AddWithValue("$status", record.Status);
        insert.Parameters.AddWithValue("$created", record.CreatedUtc);
        insert.Parameters.AddWithValue("$again", (object?)record.AskedAgainOf ?? DBNull.Value);
        insert.ExecuteNonQuery();
        return record;
    }

    /// <summary>The Request with this id, or null.</summary>
    public RequestRecord? Get(string requestId)
    {
        using var connection = _database.OpenConnection();
        return Get(connection, null, requestId);
    }

    /// <summary>The open Request that was asked again from this one, or null.</summary>
    public RequestRecord? OpenAskedAgainOf(string requestId)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM Requests WHERE AskedAgainOf = $id AND Status = 'open' ORDER BY CreatedUtc LIMIT 1;";
        command.Parameters.AddWithValue("$id", requestId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    /// <summary>How many Requests are open.</summary>
    public int OpenCount()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Requests WHERE Status = 'open';";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// One page of Requests, oldest first, and how many match. <paramref name="status"/> is a
    /// <see cref="RequestStatuses"/> value, or null for open and answered together; withdrawn Requests are never listed.
    /// </summary>
    public (IReadOnlyList<RequestRecord> Page, int Total) List(string? status, int offset, int limit)
    {
        using var connection = _database.OpenConnection();
        var filter = status is null ? "Status IN ('open', 'answered')" : "Status = $status";
        using var count = connection.CreateCommand();
        count.CommandText = $"SELECT COUNT(*) FROM Requests WHERE {filter};";
        if (status is not null) count.Parameters.AddWithValue("$status", status);
        var total = Convert.ToInt32(count.ExecuteScalar(), CultureInfo.InvariantCulture);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM Requests WHERE {filter} ORDER BY CreatedUtc, RequestId LIMIT $limit OFFSET $offset;";
        if (status is not null) command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$offset", offset);
        using var reader = command.ExecuteReader();
        var page = new List<RequestRecord>();
        while (reader.Read()) page.Add(Read(reader));
        return (page, total);
    }

    /// <summary>Takes an open Request back; an answered or withdrawn one is refused.</summary>
    public RequestWriteOutcome Withdraw(string requestId)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var current = Get(connection, transaction, requestId);
        if (current is null) return RequestWriteOutcome.NotFound;
        if (current.Status != RequestStatuses.Open) return RequestWriteOutcome.NotOpen;
        using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = "UPDATE Requests SET Status = 'withdrawn', ClosedUtc = $now WHERE RequestId = $id;";
        update.Parameters.AddWithValue("$now", Now());
        update.Parameters.AddWithValue("$id", requestId);
        update.ExecuteNonQuery();
        transaction.Commit();
        return RequestWriteOutcome.Written;
    }

    /// <summary>
    /// Links a new Draft to the open Request it answers. A Request that is not open, or that another undecided Proposal
    /// already answers, is refused, so at most one open Proposal answers a Request.
    /// </summary>
    public RequestWriteOutcome Link(CanonicalId proposalId, string requestId)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var current = Get(connection, transaction, requestId);
        if (current is null) return RequestWriteOutcome.NotFound;
        if (current.Status != RequestStatuses.Open) return RequestWriteOutcome.NotOpen;
        if (AnotherUndecided(connection, transaction, requestId, proposalId.Value))
            return RequestWriteOutcome.ProposalOpen;
        using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = "UPDATE Proposals SET RequestId = $request WHERE ProposalId = $id;";
        update.Parameters.AddWithValue("$request", requestId);
        update.Parameters.AddWithValue("$id", proposalId.Value);
        if (update.ExecuteNonQuery() == 0) return RequestWriteOutcome.NotFound;
        transaction.Commit();
        return RequestWriteOutcome.Written;
    }

    internal static string? ConflictingRequest(SqliteConnection connection, SqliteTransaction transaction, string proposalId) =>
        RequestOf(connection, transaction, proposalId) is { } requestId &&
        AnotherUndecided(connection, transaction, requestId, proposalId) ? requestId : null;

    private static bool AnotherUndecided(SqliteConnection connection, SqliteTransaction? transaction, string requestId,
        string proposalId)
    {
        using var other = connection.CreateCommand();
        other.Transaction = transaction;
        other.CommandText = "SELECT COUNT(*) FROM Proposals WHERE RequestId = $request AND ProposalId <> $id AND " +
            "(DraftName IS NOT NULL OR Status NOT IN ('applied', 'rejected', 'superseded', 'withdrawn'));";
        other.Parameters.AddWithValue("$request", requestId);
        other.Parameters.AddWithValue("$id", proposalId);
        return Convert.ToInt32(other.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
    }

    /// <summary>The id of the Request a Proposal answers, or null.</summary>
    public string? RequestOf(CanonicalId proposalId)
    {
        using var connection = _database.OpenConnection();
        return RequestOf(connection, null, proposalId.Value);
    }

    /// <summary>Every Proposal that links to a Request, by Proposal id.</summary>
    public IReadOnlyDictionary<string, string> LinkedProposals()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ProposalId, RequestId FROM Proposals WHERE RequestId IS NOT NULL;";
        using var reader = command.ExecuteReader();
        var links = new Dictionary<string, string>(StringComparer.Ordinal);
        while (reader.Read()) links[reader.GetString(0)] = reader.GetString(1);
        return links;
    }

    /// <summary>A finalized Proposal answers its Request; starting a Draft never does.</summary>
    internal static void AnswerFor(SqliteConnection connection, SqliteTransaction transaction, string proposalId,
        string nowUtc) => SetFor(connection, transaction, proposalId, RequestStatuses.Open, RequestStatuses.Answered, nowUtc);

    /// <summary>A Proposal withdrawn or superseded before a decision reopens the Request it answered.</summary>
    internal static void ReopenFor(SqliteConnection connection, SqliteTransaction transaction, string proposalId) =>
        SetFor(connection, transaction, proposalId, RequestStatuses.Answered, RequestStatuses.Open, null);

    private static void SetFor(SqliteConnection connection, SqliteTransaction transaction, string proposalId,
        string from, string to, string? nowUtc)
    {
        using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = "UPDATE Requests SET Status = $to, ClosedUtc = $closed WHERE Status = $from AND " +
            "RequestId = (SELECT RequestId FROM Proposals WHERE ProposalId = $id);";
        update.Parameters.AddWithValue("$to", to);
        update.Parameters.AddWithValue("$from", from);
        update.Parameters.AddWithValue("$closed", (object?)nowUtc ?? DBNull.Value);
        update.Parameters.AddWithValue("$id", proposalId);
        update.ExecuteNonQuery();
    }

    private static string? RequestOf(SqliteConnection connection, SqliteTransaction? transaction, string proposalId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT RequestId FROM Proposals WHERE ProposalId = $id;";
        command.Parameters.AddWithValue("$id", proposalId);
        return command.ExecuteScalar() as string;
    }

    private static RequestRecord? Get(SqliteConnection connection, SqliteTransaction? transaction, string requestId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT {Columns} FROM Requests WHERE RequestId = $id;";
        command.Parameters.AddWithValue("$id", requestId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    private static RequestRecord Read(SqliteDataReader reader) => new(reader.GetString(0), reader.GetString(1),
        reader.GetString(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5),
        reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetString(8));

    private string Now() => _clock.UtcNow.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}

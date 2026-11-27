using System.Globalization;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Jobs;

namespace SIL.Motif.Worker.Store;

/// <summary>
/// Who wrote a Rationale revision or a Comment. The label is what an agent said of itself and is shown to readers
/// only; it is never checked and nothing may depend on it. A null label means the person wrote it.
/// </summary>
/// <param name="Label">The agent's self-reported label as typed, or null when the person wrote it.</param>
/// <param name="PersonName">The person the writing was done for or by; empty when unknown.</param>
public readonly record struct DiscussionAuthor(string? Label, string PersonName)
{
    /// <summary>Whether the person wrote it herself rather than an agent on her behalf.</summary>
    public bool IsPerson => Label is null;
}

/// <summary>One Comment on a Proposal.</summary>
/// <param name="CommentId">The Comment's store-wide number.</param>
/// <param name="ProposalId">The Proposal the Comment is on.</param>
/// <param name="Kind"><c>standard</c>, or <c>system</c> for a Comment Motif wrote.</param>
/// <param name="Author">Who wrote it.</param>
/// <param name="Text">The Markdown text.</param>
/// <param name="IntentDigest">The Proposal content current when the text was last written; null for a Draft never finalized.</param>
/// <param name="CreatedUtc">When it was added.</param>
/// <param name="EditedUtc">When a person last edited it; null when it was never edited.</param>
public sealed record ProposalCommentRecord(
    long CommentId, string ProposalId, string Kind, DiscussionAuthor Author, string Text, string? IntentDigest,
    string CreatedUtc, string? EditedUtc)
{
    /// <summary>When the text last changed.</summary>
    public string WrittenUtc => EditedUtc ?? CreatedUtc;
}

/// <summary>One earlier text of a Rationale, kept only while its Proposal is open.</summary>
public sealed record RationaleRevisionRecord(
    long RevisionId, string ProposalId, string Text, string? IntentDigest, DiscussionAuthor Author, string CreatedUtc);

/// <summary>What a Comment write did.</summary>
public enum CommentWriteOutcome
{
    /// <summary>The Comment was written.</summary>
    Written,
    /// <summary>No Proposal has that id.</summary>
    ProposalNotFound,
    /// <summary>The Proposal is decided, so its discussion is frozen.</summary>
    Closed,
    /// <summary>The Proposal has no Comment with that number.</summary>
    CommentNotFound,
}

/// <summary>The outcome of a Comment write and the number of the Comment it touched.</summary>
public readonly record struct CommentWrite(CommentWriteOutcome Outcome, long CommentId = 0);

/// <summary>
/// The discussion on a Proposal: Comments, and the Rationale revisions kept while it is open. A decision
/// (applied, declined, withdrawn or superseded) freezes both: the revisions are dropped and the latest text stays.
/// </summary>
public sealed class ProposalDiscussionRepository
{
    /// <summary>How many Rationale revisions an open Proposal keeps; older ones drop as new ones arrive.</summary>
    internal const int MaxRationaleRevisions = 100;

    // The open set of ProposalLifecycle.IsTerminal, for a write that must not race a decision.
    internal const string OpenSql =
        "(DraftName IS NOT NULL OR Status NOT IN ('applied', 'rejected', 'superseded', 'withdrawn'))";

    private readonly MotifDatabase _database;
    private readonly IJobClock _clock;

    /// <summary>Opens the discussion on the Proposals of one store.</summary>
    public ProposalDiscussionRepository(MotifDatabase database, IJobClock? clock = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _clock = clock ?? new SystemJobClock();
    }

    /// <summary>Adds a Comment to an open Proposal, stamped with the Proposal's current content digest.</summary>
    public CommentWrite AddComment(CanonicalId proposalId, string text, DiscussionAuthor author)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var state = StateOf(connection, transaction, proposalId.Value);
        if (state != CommentWriteOutcome.Written) return new CommentWrite(state);
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO ProposalComments (ProposalId, Kind, AuthorLabel, PersonName, Text, IntentDigest, CreatedUtc)
            SELECT ProposalId, 'standard', $label, $person, $text, CurrentIntentDigest, $now
            FROM Proposals WHERE ProposalId = $id;
            SELECT last_insert_rowid();
            """;
        AddAuthor(insert, author);
        insert.Parameters.AddWithValue("$text", text);
        insert.Parameters.AddWithValue("$now", Now());
        insert.Parameters.AddWithValue("$id", proposalId.Value);
        var commentId = Convert.ToInt64(insert.ExecuteScalar(), CultureInfo.InvariantCulture);
        transaction.Commit();
        return new CommentWrite(CommentWriteOutcome.Written, commentId);
    }

    /// <summary>Replaces a Comment's text on an open Proposal and records that it was edited, keeping no history.</summary>
    public CommentWrite EditComment(CanonicalId proposalId, long commentId, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var state = CommentState(connection, transaction, proposalId.Value, commentId);
        if (state != CommentWriteOutcome.Written) return new CommentWrite(state, commentId);
        using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE ProposalComments SET Text = $text, EditedUtc = $now,
                IntentDigest = (SELECT CurrentIntentDigest FROM Proposals WHERE ProposalId = $id)
            WHERE CommentId = $comment AND ProposalId = $id;
            """;
        update.Parameters.AddWithValue("$text", text);
        update.Parameters.AddWithValue("$now", Now());
        update.Parameters.AddWithValue("$id", proposalId.Value);
        update.Parameters.AddWithValue("$comment", commentId);
        update.ExecuteNonQuery();
        transaction.Commit();
        return new CommentWrite(CommentWriteOutcome.Written, commentId);
    }

    /// <summary>Deletes a Comment, and the pictures attached to it, from an open Proposal.</summary>
    public CommentWrite DeleteComment(CanonicalId proposalId, long commentId)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var state = CommentState(connection, transaction, proposalId.Value, commentId);
        if (state != CommentWriteOutcome.Written) return new CommentWrite(state, commentId);
        ExecuteDelete(connection, transaction, "DELETE FROM ProposalPictures WHERE CommentId = $comment;", "$comment", commentId);
        ExecuteDelete(connection, transaction, "DELETE FROM ProposalComments WHERE CommentId = $comment;", "$comment", commentId);
        transaction.Commit();
        return new CommentWrite(CommentWriteOutcome.Written, commentId);
    }

    /// <summary>Deletes the Comments a person wrote on an open Proposal, leaving an agent's alone.</summary>
    public CommentWrite DeletePersonComments(CanonicalId proposalId)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var state = StateOf(connection, transaction, proposalId.Value);
        if (state != CommentWriteOutcome.Written) return new CommentWrite(state);
        ExecuteDelete(connection, transaction,
            "DELETE FROM ProposalPictures WHERE CommentId IN (SELECT CommentId FROM ProposalComments " +
            "WHERE ProposalId = $id AND AuthorLabel IS NULL);", "$id", proposalId.Value);
        ExecuteDelete(connection, transaction,
            "DELETE FROM ProposalComments WHERE ProposalId = $id AND AuthorLabel IS NULL;", "$id", proposalId.Value);
        transaction.Commit();
        return new CommentWrite(CommentWriteOutcome.Written);
    }

    private static void ExecuteDelete(SqliteConnection connection, SqliteTransaction transaction, string sql,
        string name, object value)
    {
        using var delete = connection.CreateCommand();
        delete.Transaction = transaction;
        delete.CommandText = sql;
        delete.Parameters.AddWithValue(name, value);
        delete.ExecuteNonQuery();
    }

    /// <summary>Lists a Proposal's Comments, oldest first.</summary>
    public IReadOnlyList<ProposalCommentRecord> ListComments(CanonicalId proposalId)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = CommentSelect + " WHERE ProposalId = $id ORDER BY CommentId;";
        command.Parameters.AddWithValue("$id", proposalId.Value);
        return ReadComments(command);
    }

    /// <summary>How many Comments each Proposal that has any holds, by Proposal id.</summary>
    public IReadOnlyDictionary<string, int> CommentCounts()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ProposalId, COUNT(*) FROM ProposalComments GROUP BY ProposalId;";
        using var reader = command.ExecuteReader();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        while (reader.Read()) counts[reader.GetString(0)] = reader.GetInt32(1);
        return counts;
    }

    /// <summary>Lists the kept revisions of a Proposal's Rationale, oldest first; empty once it is decided.</summary>
    public IReadOnlyList<RationaleRevisionRecord> ListRationaleRevisions(CanonicalId proposalId)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT RevisionId, ProposalId, Text, IntentDigest, AuthorLabel, PersonName, CreatedUtc
            FROM ProposalRationaleRevisions WHERE ProposalId = $id ORDER BY RevisionId;
            """;
        command.Parameters.AddWithValue("$id", proposalId.Value);
        using var reader = command.ExecuteReader();
        var revisions = new List<RationaleRevisionRecord>();
        while (reader.Read())
            revisions.Add(new RationaleRevisionRecord(reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
                Text(reader, 3), new DiscussionAuthor(Text(reader, 4), reader.GetString(5)), reader.GetString(6)));
        return revisions;
    }

    /// <summary>Records a Rationale text as a revision when the Proposal is open; a decided Proposal gets none.</summary>
    internal static void RecordRationaleRevision(SqliteConnection connection, SqliteTransaction? transaction,
        string proposalId, string text, DiscussionAuthor author, string nowUtc)
    {
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = $"""
            INSERT INTO ProposalRationaleRevisions (ProposalId, Text, IntentDigest, AuthorLabel, PersonName, CreatedUtc)
            SELECT ProposalId, $text, CurrentIntentDigest, $label, $person, $now
            FROM Proposals WHERE ProposalId = $id AND {OpenSql};
            """;
        AddAuthor(insert, author);
        insert.Parameters.AddWithValue("$text", text);
        insert.Parameters.AddWithValue("$now", nowUtc);
        insert.Parameters.AddWithValue("$id", proposalId);
        insert.ExecuteNonQuery();
        using var trim = connection.CreateCommand();
        trim.Transaction = transaction;
        trim.CommandText = "DELETE FROM ProposalRationaleRevisions WHERE ProposalId = $id AND RevisionId NOT IN " +
            "(SELECT RevisionId FROM ProposalRationaleRevisions WHERE ProposalId = $id ORDER BY RevisionId DESC LIMIT $keep);";
        trim.Parameters.AddWithValue("$id", proposalId);
        trim.Parameters.AddWithValue("$keep", MaxRationaleRevisions);
        trim.ExecuteNonQuery();
    }

    /// <summary>Drops the Rationale revisions of a Proposal that has just been decided; the latest text stays on the Proposal.</summary>
    internal static void ClearRationaleHistoryIfDecided(SqliteConnection connection, SqliteTransaction transaction,
        string proposalId)
    {
        using var delete = connection.CreateCommand();
        delete.Transaction = transaction;
        delete.CommandText = $"DELETE FROM ProposalRationaleRevisions WHERE ProposalId = $id " +
            $"AND ProposalId IN (SELECT ProposalId FROM Proposals WHERE NOT {OpenSql});";
        delete.Parameters.AddWithValue("$id", proposalId);
        delete.ExecuteNonQuery();
    }

    /// <summary>Stamps the revisions and Comments written before a Draft's first finalize with the digest it now has.</summary>
    internal static void StampDigest(SqliteConnection connection, SqliteTransaction transaction, string proposalId,
        string digest)
    {
        foreach (var table in new[] { "ProposalRationaleRevisions", "ProposalComments" })
        {
            using var stamp = connection.CreateCommand();
            stamp.Transaction = transaction;
            stamp.CommandText = $"UPDATE {table} SET IntentDigest = $digest WHERE ProposalId = $id AND IntentDigest IS NULL;";
            stamp.Parameters.AddWithValue("$digest", digest);
            stamp.Parameters.AddWithValue("$id", proposalId);
            stamp.ExecuteNonQuery();
        }
    }

    /// <summary>Removes every revision, Comment and picture of a Proposal, on the caller's transaction.</summary>
    internal static void RemoveAll(SqliteConnection connection, SqliteTransaction transaction, string proposalId)
    {
        foreach (var table in new[] { "ProposalPictures", "ProposalComments", "ProposalRationaleRevisions" })
        {
            using var delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = $"DELETE FROM {table} WHERE ProposalId = $id;";
            delete.Parameters.AddWithValue("$id", proposalId);
            delete.ExecuteNonQuery();
        }
    }

    internal static void AddAuthor(SqliteCommand command, DiscussionAuthor author)
    {
        command.Parameters.AddWithValue("$label", (object?)author.Label ?? DBNull.Value);
        command.Parameters.AddWithValue("$person", author.PersonName ?? "");
    }

    private const string CommentSelect = """
        SELECT CommentId, ProposalId, Kind, AuthorLabel, PersonName, Text, IntentDigest, CreatedUtc, EditedUtc
        FROM ProposalComments
        """;

    private static List<ProposalCommentRecord> ReadComments(SqliteCommand command)
    {
        using var reader = command.ExecuteReader();
        var comments = new List<ProposalCommentRecord>();
        while (reader.Read())
            comments.Add(new ProposalCommentRecord(reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
                new DiscussionAuthor(Text(reader, 3), reader.GetString(4)), reader.GetString(5), Text(reader, 6),
                reader.GetString(7), Text(reader, 8)));
        return comments;
    }

    // Written means the Proposal exists and is open.
    internal static CommentWriteOutcome StateOf(SqliteConnection connection, SqliteTransaction transaction, string proposalId)
    {
        using var read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = $"SELECT CASE WHEN {OpenSql} THEN 1 ELSE 0 END FROM Proposals WHERE ProposalId = $id;";
        read.Parameters.AddWithValue("$id", proposalId);
        return read.ExecuteScalar() switch
        {
            null => CommentWriteOutcome.ProposalNotFound,
            long open when open == 0 => CommentWriteOutcome.Closed,
            _ => CommentWriteOutcome.Written,
        };
    }

    private static CommentWriteOutcome CommentState(SqliteConnection connection, SqliteTransaction transaction,
        string proposalId, long commentId)
    {
        var state = StateOf(connection, transaction, proposalId);
        if (state != CommentWriteOutcome.Written) return state;
        using var read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = "SELECT 1 FROM ProposalComments WHERE CommentId = $comment AND ProposalId = $id;";
        read.Parameters.AddWithValue("$comment", commentId);
        read.Parameters.AddWithValue("$id", proposalId);
        return read.ExecuteScalar() is null ? CommentWriteOutcome.CommentNotFound : CommentWriteOutcome.Written;
    }

    private static string? Text(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private string Now() => _clock.UtcNow.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}

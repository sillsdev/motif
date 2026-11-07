using System.Globalization;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Host.Store;

namespace SIL.Motif.Worker.Store;

/// <summary>One stored picture of a Proposal.</summary>
/// <param name="PictureId">The picture's store-wide number.</param>
/// <param name="ProposalId">The Proposal it is on.</param>
/// <param name="CommentId">The Comment it is attached to; null when it is attached to the Proposal.</param>
/// <param name="MediaType"><c>image/png</c> or <c>image/jpeg</c>.</param>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
/// <param name="ByteCount">The size of the stored bytes.</param>
/// <param name="CreatedUtc">When it was added.</param>
/// <param name="Caption">The one-line caption the person gave it; null when it has none.</param>
public sealed record ProposalPictureRecord(
    long PictureId, string ProposalId, long? CommentId, string MediaType, int Width, int Height, int ByteCount,
    string CreatedUtc, string? Caption = null);

/// <summary>What a picture write did.</summary>
public enum PictureWriteOutcome
{
    /// <summary>The picture was written.</summary>
    Written,
    /// <summary>No Proposal has that id.</summary>
    ProposalNotFound,
    /// <summary>The Proposal is decided, so its pictures are frozen.</summary>
    Closed,
    /// <summary>The Proposal has no Comment, or no picture, with that number.</summary>
    TargetNotFound,
    /// <summary>The Proposal already holds as many pictures as it may.</summary>
    TooMany,
}

/// <summary>The outcome of a picture write and the number of the picture it touched.</summary>
public readonly record struct PictureWrite(PictureWriteOutcome Outcome, long PictureId = 0);

/// <summary>
/// The pictures on a Proposal. They live in the project store, count toward its size budget and are never pruned;
/// they go with the Proposal when it is deleted, and freeze when it is decided, as its Comments do.
/// </summary>
public sealed class ProposalPictureRepository
{
    /// <summary>How many pictures one Proposal holds; the budget cannot trim authored data, so the count is bounded.</summary>
    public const int MaxPicturesPerProposal = 10;

    private readonly MotifDatabase _database;
    private readonly IJobClock _clock;

    /// <summary>Opens the pictures of one store.</summary>
    public ProposalPictureRepository(MotifDatabase database, IJobClock? clock = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _clock = clock ?? new SystemJobClock();
    }

    /// <summary>Adds a picture to an open Proposal, attached to one of its Comments when a Comment is named.</summary>
    public PictureWrite Add(CanonicalId proposalId, long? commentId, string mediaType, int width, int height, byte[] bytes,
        string? caption = null)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var state = ProposalDiscussionRepository.StateOf(connection, transaction, proposalId.Value);
        if (state == CommentWriteOutcome.ProposalNotFound) return new PictureWrite(PictureWriteOutcome.ProposalNotFound);
        if (state == CommentWriteOutcome.Closed) return new PictureWrite(PictureWriteOutcome.Closed);
        if (Count(connection, transaction, "SELECT COUNT(*) FROM ProposalPictures WHERE ProposalId = $id;",
                proposalId.Value, null) >= MaxPicturesPerProposal)
            return new PictureWrite(PictureWriteOutcome.TooMany);
        if (commentId is { } comment && Count(connection, transaction,
                "SELECT COUNT(*) FROM ProposalComments WHERE ProposalId = $id AND CommentId = $other;",
                proposalId.Value, comment) == 0)
            return new PictureWrite(PictureWriteOutcome.TargetNotFound);
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO ProposalPictures (ProposalId, CommentId, MediaType, Width, Height, Bytes, CreatedUtc, Caption)
            VALUES ($id, $comment, $type, $width, $height, $bytes, $now, $caption);
            SELECT last_insert_rowid();
            """;
        insert.Parameters.AddWithValue("$id", proposalId.Value);
        insert.Parameters.AddWithValue("$comment", (object?)commentId ?? DBNull.Value);
        insert.Parameters.AddWithValue("$type", mediaType);
        insert.Parameters.AddWithValue("$width", width);
        insert.Parameters.AddWithValue("$height", height);
        insert.Parameters.AddWithValue("$bytes", bytes);
        insert.Parameters.AddWithValue("$caption", (object?)caption ?? DBNull.Value);
        insert.Parameters.AddWithValue("$now", _clock.UtcNow.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        var pictureId = Convert.ToInt64(insert.ExecuteScalar(), CultureInfo.InvariantCulture);
        transaction.Commit();
        return new PictureWrite(PictureWriteOutcome.Written, pictureId);
    }

    /// <summary>Removes a picture from an open Proposal.</summary>
    public PictureWrite Remove(CanonicalId proposalId, long pictureId)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var state = ProposalDiscussionRepository.StateOf(connection, transaction, proposalId.Value);
        if (state == CommentWriteOutcome.ProposalNotFound) return new PictureWrite(PictureWriteOutcome.ProposalNotFound, pictureId);
        if (state == CommentWriteOutcome.Closed) return new PictureWrite(PictureWriteOutcome.Closed, pictureId);
        using var delete = connection.CreateCommand();
        delete.Transaction = transaction;
        delete.CommandText = "DELETE FROM ProposalPictures WHERE PictureId = $picture AND ProposalId = $id;";
        delete.Parameters.AddWithValue("$picture", pictureId);
        delete.Parameters.AddWithValue("$id", proposalId.Value);
        if (delete.ExecuteNonQuery() == 0) return new PictureWrite(PictureWriteOutcome.TargetNotFound, pictureId);
        transaction.Commit();
        return new PictureWrite(PictureWriteOutcome.Written, pictureId);
    }

    /// <summary>Lists a Proposal's pictures without their bytes, oldest first.</summary>
    public IReadOnlyList<ProposalPictureRecord> List(CanonicalId proposalId)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT PictureId, ProposalId, CommentId, MediaType, Width, Height, length(Bytes), CreatedUtc, Caption
            FROM ProposalPictures WHERE ProposalId = $id ORDER BY PictureId;
            """;
        command.Parameters.AddWithValue("$id", proposalId.Value);
        using var reader = command.ExecuteReader();
        var pictures = new List<ProposalPictureRecord>();
        while (reader.Read())
            pictures.Add(new ProposalPictureRecord(reader.GetInt64(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetInt64(2), reader.GetString(3), reader.GetInt32(4),
                reader.GetInt32(5), reader.GetInt32(6), reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetString(8)));
        return pictures;
    }

    /// <summary>Reads the bytes of one picture of a Proposal, or null when it has none with that number.</summary>
    public byte[]? ReadBytes(CanonicalId proposalId, long pictureId)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Bytes FROM ProposalPictures WHERE PictureId = $picture AND ProposalId = $id;";
        command.Parameters.AddWithValue("$picture", pictureId);
        command.Parameters.AddWithValue("$id", proposalId.Value);
        return command.ExecuteScalar() as byte[];
    }

    private static long Count(SqliteConnection connection, SqliteTransaction transaction, string sql, string proposalId,
        long? other)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", proposalId);
        if (other is { } value) command.Parameters.AddWithValue("$other", value);
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }
}

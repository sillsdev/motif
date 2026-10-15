using System.Globalization;
using Microsoft.Data.Sqlite;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands.Review;

/// <summary>One stored "looked at" mark: which entry, and the content and evidence it was judged against.</summary>
/// <param name="EntryKey">The entry's key within its Proposal.</param>
/// <param name="ContentDigest">The entry's content digest when it was looked at.</param>
/// <param name="EvidenceDigest">The Trial evidence the entry was judged against.</param>
/// <param name="LookedAtUtc">When the mark was recorded.</param>
public sealed record ReviewMark(string EntryKey, string ContentDigest, string EvidenceDigest, string LookedAtUtc);

/// <summary>
/// Remembers which entries of a Proposal's review list a person has looked at. A mark is attention, not
/// approval: it never changes the Proposal, and it stops counting once the entry's content or the Trial
/// evidence it was judged against changes. No person is recorded.
/// </summary>
public static class ReviewMarks
{
    /// <summary>The evidence digest of a Proposal that has no Trial.</summary>
    public const string NoEvidence = "none";

    /// <summary>Marks an entry looked at, or clears its mark.</summary>
    public static CommandOutcome<ReviewMarkResponse> Set(ReviewMarkRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ProjectStoreCommand.Run(request.ProjectPath, MotifProductVersion.CurrentText, (database, _) =>
        {
            if (string.IsNullOrWhiteSpace(request.EntryKey) || string.IsNullOrWhiteSpace(request.ContentDigest) ||
                string.IsNullOrWhiteSpace(request.EvidenceDigest))
                return CommandOutcome<ReviewMarkResponse>.Refused(new Refusal("review.invalid-mark",
                    FailureReason.InvalidArgument, "A review mark needs an entry, a content digest and an evidence digest."));
            string id;
            try
            {
                id = ProposalCommands.NormalizeId(request.ProposalId);
                new ProposalRepository(database).Get(CanonicalId.Parse(id));
            }
            catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException)
            {
                return CommandOutcome<ReviewMarkResponse>.Refused(ProposalCommands.ProposalLoadRefusal(exception));
            }
            if (request.LookedAt) Put(database, id, request.EntryKey, request.ContentDigest, request.EvidenceDigest);
            else Clear(database, id, [request.EntryKey]);
            return CommandOutcome<ReviewMarkResponse>.Success(
                new ReviewMarkResponse(id, request.EntryKey, request.LookedAt));
        });
    }

    /// <summary>Records one mark, replacing the earlier mark of the same entry.</summary>
    public static void Put(MotifDatabase database, string proposalId, string entryKey, string contentDigest,
        string evidenceDigest)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ReviewMarks (ProposalId, EntryKey, ContentDigest, EvidenceDigest, LookedAtUtc)
            VALUES ($proposal, $entry, $content, $evidence, $at)
            ON CONFLICT (ProposalId, EntryKey) DO UPDATE SET ContentDigest = excluded.ContentDigest,
                EvidenceDigest = excluded.EvidenceDigest, LookedAtUtc = excluded.LookedAtUtc;
            """;
        command.Parameters.AddWithValue("$proposal", proposalId);
        command.Parameters.AddWithValue("$entry", entryKey);
        command.Parameters.AddWithValue("$content", contentDigest);
        command.Parameters.AddWithValue("$evidence", evidenceDigest);
        command.Parameters.AddWithValue("$at", TimeProvider.System.GetUtcNow().ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    /// <summary>Removes the marks of the named entries.</summary>
    public static void Clear(MotifDatabase database, string proposalId, IEnumerable<string> entryKeys)
    {
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        foreach (var key in entryKeys.Distinct(StringComparer.Ordinal))
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM ReviewMarks WHERE ProposalId = $proposal AND EntryKey = $entry;";
            command.Parameters.AddWithValue("$proposal", proposalId);
            command.Parameters.AddWithValue("$entry", key);
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    /// <summary>Removes every mark on a Proposal that no longer exists.</summary>
    public static void ClearAll(MotifDatabase database, string proposalId)
    {
        ArgumentNullException.ThrowIfNull(database);
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        ClearAll(connection, transaction, proposalId);
        transaction.Commit();
    }

    /// <summary>Removes every mark for one Proposal inside the caller's transaction.</summary>
    /// <param name="connection">The open connection that owns <paramref name="transaction"/>.</param>
    /// <param name="transaction">The transaction that includes this review-mark deletion.</param>
    /// <param name="proposalId">The id of the Proposal whose marks are removed.</param>
    internal static void ClearAll(SqliteConnection connection, SqliteTransaction transaction, string proposalId)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM ReviewMarks WHERE ProposalId = $proposal;";
        command.Parameters.AddWithValue("$proposal", proposalId);
        command.ExecuteNonQuery();
    }

    /// <summary>Reads every stored mark of one Proposal, keyed by entry.</summary>
    public static IReadOnlyDictionary<string, ReviewMark> Read(MotifDatabase database, string proposalId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EntryKey, ContentDigest, EvidenceDigest, LookedAtUtc FROM ReviewMarks
            WHERE ProposalId = $proposal;
            """;
        command.Parameters.AddWithValue("$proposal", proposalId);
        var marks = new Dictionary<string, ReviewMark>(StringComparer.Ordinal);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            marks[reader.GetString(0)] = new ReviewMark(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3));
        return marks;
    }
}

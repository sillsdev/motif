using System.Globalization;
using Microsoft.Data.Sqlite;

namespace SIL.Motif.Host.Store;

/// <summary>One project this installation knows, its machine-local tag, and when it was last seen.</summary>
public sealed record KnownProjectRecord(
    string WorkspaceKey, string FullFwDataPath, DateTimeOffset LastSeenUtc, string? ProjectTag);

/// <summary>Maintains Known projects in the machine store and assigns each one a unique project tag.</summary>
public sealed class KnownProjectRegistry
{
    private readonly MachineDatabase _database;

    public KnownProjectRegistry(MachineDatabase database) =>
        _database = database ?? throw new ArgumentNullException(nameof(database));

    /// <summary>Records a project, preserving its tag when the same workspace key is recorded again.</summary>
    public KnownProjectRecord Record(string workspaceKey, string fullFwDataPath, DateTimeOffset lastSeenUtc)
    {
        ValidateRecord(workspaceKey, fullFwDataPath, lastSeenUtc);
        var path = Path.GetFullPath(fullFwDataPath);
        var seen = lastSeenUtc.ToString("O", CultureInfo.InvariantCulture);
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var existing = Find(connection, transaction, workspaceKey);
        if (existing is not null)
        {
            var tag = existing.ProjectTag ?? TryDefaultTag(connection, transaction, path);
            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE KnownProjects SET FullFwDataPath = $path, LastSeenUtc = $seen, " +
                "ProjectTag = $tag WHERE WorkspaceKey = $key;";
            update.Parameters.AddWithValue("$key", workspaceKey);
            update.Parameters.AddWithValue("$path", path);
            update.Parameters.AddWithValue("$seen", seen);
            update.Parameters.AddWithValue("$tag", (object?)tag ?? DBNull.Value);
            update.ExecuteNonQuery();
            transaction.Commit();
            return existing with { FullFwDataPath = path, LastSeenUtc = lastSeenUtc, ProjectTag = tag };
        }

        var tagForNewProject = TryDefaultTag(connection, transaction, path);
        Insert(connection, transaction, workspaceKey, path, seen, tagForNewProject);
        transaction.Commit();
        return new KnownProjectRecord(workspaceKey, path, lastSeenUtc, tagForNewProject);
    }

    /// <summary>Ensures a project has a usable tag, refusing when no automatic candidate is available.</summary>
    /// <exception cref="ProjectTagException">The vernacular or folder candidate cannot be assigned.</exception>
    public KnownProjectRecord EnsureTag(string workspaceKey, string fullFwDataPath, DateTimeOffset lastSeenUtc)
    {
        ValidateRecord(workspaceKey, fullFwDataPath, lastSeenUtc);
        var path = Path.GetFullPath(fullFwDataPath);
        var seen = lastSeenUtc.ToString("O", CultureInfo.InvariantCulture);
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var existing = Find(connection, transaction, workspaceKey);
        if (existing?.ProjectTag is { } retainedTag)
        {
            UpdatePathAndSeen(connection, transaction, workspaceKey, path, seen, retainedTag);
            transaction.Commit();
            return existing with { FullFwDataPath = path, LastSeenUtc = lastSeenUtc };
        }

        var tag = ProjectTags.Default(path, candidate => IsTaken(connection, transaction, candidate, null));
        if (existing is null)
            Insert(connection, transaction, workspaceKey, path, seen, tag);
        else
            UpdatePathAndSeen(connection, transaction, workspaceKey, path, seen, tag);
        transaction.Commit();
        return new KnownProjectRecord(workspaceKey, path, lastSeenUtc, tag);
    }

    /// <summary>Records a project with a person's explicitly chosen tag.</summary>
    /// <exception cref="ProjectTagException">The tag is invalid or belongs to another Known project.</exception>
    public KnownProjectRecord RecordWithTag(
        string workspaceKey, string fullFwDataPath, DateTimeOffset lastSeenUtc, string tag)
    {
        ValidateRecord(workspaceKey, fullFwDataPath, lastSeenUtc);
        var path = Path.GetFullPath(fullFwDataPath);
        RequireTag(tag, path);
        var seen = lastSeenUtc.ToString("O", CultureInfo.InvariantCulture);
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var existing = Find(connection, transaction, workspaceKey);
        if (IsTaken(connection, transaction, tag, existing?.WorkspaceKey))
            throw new ProjectTagException(ProjectTagProblem.Taken, tag, path);

        if (existing is null)
        {
            Insert(connection, transaction, workspaceKey, path, seen, tag);
        }
        else
        {
            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE KnownProjects SET FullFwDataPath = $path, LastSeenUtc = $seen, " +
                "ProjectTag = $tag WHERE WorkspaceKey = $key;";
            update.Parameters.AddWithValue("$key", workspaceKey);
            update.Parameters.AddWithValue("$path", path);
            update.Parameters.AddWithValue("$seen", seen);
            update.Parameters.AddWithValue("$tag", tag);
            update.ExecuteNonQuery();
        }

        transaction.Commit();
        return new KnownProjectRecord(workspaceKey, path, lastSeenUtc, tag);
    }

    /// <summary>Finds one recorded project by its workspace key.</summary>
    public KnownProjectRecord? Find(string workspaceKey)
    {
        RequireWorkspaceKey(workspaceKey);
        using var connection = _database.OpenConnection();
        return Find(connection, null, workspaceKey);
    }

    /// <summary>Lists every recorded project, ordered by workspace key.</summary>
    public IReadOnlyList<KnownProjectRecord> List()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT WorkspaceKey, FullFwDataPath, LastSeenUtc, ProjectTag " +
            "FROM KnownProjects ORDER BY WorkspaceKey;";
        using var reader = command.ExecuteReader();
        var records = new List<KnownProjectRecord>();
        while (reader.Read()) records.Add(Read(reader));
        return records;
    }

    /// <summary>Removes a project from the registry. A project not present is left silently absent.</summary>
    public void Forget(string workspaceKey)
    {
        RequireWorkspaceKey(workspaceKey);
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM KnownProjects WHERE WorkspaceKey = $key;";
        command.Parameters.AddWithValue("$key", workspaceKey);
        command.ExecuteNonQuery();
    }

    private static KnownProjectRecord? Find(
        SqliteConnection connection, SqliteTransaction? transaction, string workspaceKey)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT WorkspaceKey, FullFwDataPath, LastSeenUtc, ProjectTag " +
            "FROM KnownProjects WHERE WorkspaceKey = $key;";
        command.Parameters.AddWithValue("$key", workspaceKey);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    private static void Insert(
        SqliteConnection connection, SqliteTransaction transaction, string key, string path, string seen, string? tag)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO KnownProjects (WorkspaceKey, FullFwDataPath, LastSeenUtc, ProjectTag) " +
            "VALUES ($key, $path, $seen, $tag);";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$path", path);
        command.Parameters.AddWithValue("$seen", seen);
        command.Parameters.AddWithValue("$tag", (object?)tag ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static void UpdatePathAndSeen(
        SqliteConnection connection, SqliteTransaction transaction, string key, string path, string seen, string tag)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE KnownProjects SET FullFwDataPath = $path, LastSeenUtc = $seen, " +
            "ProjectTag = $tag WHERE WorkspaceKey = $key;";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$path", path);
        command.Parameters.AddWithValue("$seen", seen);
        command.Parameters.AddWithValue("$tag", tag);
        command.ExecuteNonQuery();
    }

    private static string? TryDefaultTag(
        SqliteConnection connection, SqliteTransaction transaction, string path)
    {
        try
        {
            return ProjectTags.Default(path, candidate => IsTaken(connection, transaction, candidate, null));
        }
        catch (ProjectTagException exception) when (exception.Problem == ProjectTagProblem.Unavailable)
        {
            return null;
        }
    }

    private static bool IsTaken(
        SqliteConnection connection, SqliteTransaction transaction, string tag, string? exceptWorkspaceKey)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = exceptWorkspaceKey is null
            ? "SELECT EXISTS (SELECT 1 FROM KnownProjects WHERE ProjectTag = $tag);"
            : "SELECT EXISTS (SELECT 1 FROM KnownProjects WHERE ProjectTag = $tag AND WorkspaceKey <> $key);";
        command.Parameters.AddWithValue("$tag", tag);
        if (exceptWorkspaceKey is not null) command.Parameters.AddWithValue("$key", exceptWorkspaceKey);
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) != 0;
    }

    private static KnownProjectRecord Read(SqliteDataReader reader)
    {
        try
        {
            var lastSeen = DateTimeOffset.ParseExact(reader.GetString(2), "O", CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind);
            if (lastSeen.Offset != TimeSpan.Zero)
                throw new InvalidDataException("The persisted last-seen time is not UTC.");
            var tag = reader.IsDBNull(3) ? null : reader.GetString(3);
            if (tag is not null && !ProjectTags.IsValid(tag))
                throw new InvalidDataException("The persisted project tag is invalid.");
            return new KnownProjectRecord(reader.GetString(0), reader.GetString(1), lastSeen, tag);
        }
        catch (InvalidDataException) { throw; }
        catch (Exception exception) when (exception is FormatException or InvalidCastException)
        {
            throw new InvalidDataException("The persisted Known project row is malformed.", exception);
        }
    }

    private static void ValidateRecord(string workspaceKey, string fullFwDataPath, DateTimeOffset lastSeenUtc)
    {
        RequireWorkspaceKey(workspaceKey);
        if (string.IsNullOrWhiteSpace(fullFwDataPath))
            throw new ArgumentException("A full .fwdata path is required.", nameof(fullFwDataPath));
        if (lastSeenUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("The last-seen time must be UTC.", nameof(lastSeenUtc));
    }

    private static void RequireTag(string tag, string path)
    {
        if (!ProjectTags.IsValid(tag))
            throw new ProjectTagException(ProjectTagProblem.Invalid, tag ?? string.Empty, path);
    }

    private static void RequireWorkspaceKey(string workspaceKey)
    {
        if (string.IsNullOrWhiteSpace(workspaceKey))
            throw new ArgumentException("A project workspace key is required.", nameof(workspaceKey));
    }
}

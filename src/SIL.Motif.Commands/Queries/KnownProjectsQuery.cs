using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml;
using Microsoft.Data.Sqlite;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands.Queries;

/// <summary>One Known project ready to display in a project picker.</summary>
public sealed record KnownProjectSummary(string FullFwDataPath, DateTimeOffset LastSeenUtc)
{
    /// <summary>The LangProject GUID read from the saved project file or its current Baseline.</summary>
    public string? ProjectIdentity { get; init; }

    /// <summary>The count of wordforms in the current stored Baseline, when available.</summary>
    public int? WordformCount { get; init; }

    /// <summary>The project's stored state in the window's words, when Motif has recorded one.</summary>
    public string? StateText { get; init; }
}

/// <summary>
/// Lists the machine store's Known projects for the application's project picker without changing its registry.
/// Optional project-store details are read without creating stores. This query has no CLI verb.
/// </summary>
public static class KnownProjectsQuery
{
    /// <summary>Lists Known projects, most-recently-seen first, resolving the real installation's managed root.</summary>
    public static IReadOnlyList<KnownProjectSummary> List() => List(RunnerOptions.ResolveRoot());

    /// <summary>Lists Known projects under an explicitly supplied managed root, for a test's own disposable root.</summary>
    public static IReadOnlyList<KnownProjectSummary> List(string managedRoot)
    {
        using var machine = MachineDatabase.Open(managedRoot);
        var registry = new KnownProjectRegistry(machine);
        var kept = new List<KnownProjectSummary>();
        var metadataTime = TimeSpan.Zero;
        var metadataBlocked = false;
        foreach (var known in registry.List().OrderByDescending(project => project.LastSeenUtc))
        {
            // An unavailable file can return, so browsing Recent never forgets its registry entry.
            if (!File.Exists(known.FullFwDataPath)) continue;
            var summary = new KnownProjectSummary(known.FullFwDataPath, known.LastSeenUtc);
            if (!metadataBlocked && metadataTime < TimeSpan.FromMilliseconds(250))
            {
                var clock = Stopwatch.StartNew();
                summary = ReadProjectStoreSummary(summary, out metadataBlocked);
                metadataTime += clock.Elapsed;
            }
            kept.Add(summary with
            {
                ProjectIdentity = summary.ProjectIdentity ?? ProjectFileIdentityReader.Read(summary.FullFwDataPath),
            });
        }
        return MostRecentlySeenCopies(kept);
    }

    /// <summary>Reads the saved LangProject GUID, reusing an unchanged file's successful cached identity.</summary>
    /// <param name="path">The project data-file path.</param>
    /// <returns>The saved GUID, or null when the file cannot currently supply one.</returns>
    public static string? ReadProjectIdentity(string path) => ProjectFileIdentityReader.Read(path);

    /// <summary>Returns each project's newest copy, matching GUIDs without case and paths by platform rules.</summary>
    public static IReadOnlyList<KnownProjectSummary> MostRecentlySeenCopies(IEnumerable<KnownProjectSummary> projects)
    {
        ArgumentNullException.ThrowIfNull(projects);
        var pathComparer = OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        return projects
            .GroupBy(project => project.ProjectIdentity is { Length: > 0 } identity
                ? "identity:" + identity.ToUpperInvariant()
                : "path:" + Path.GetFullPath(project.FullFwDataPath), pathComparer)
            .Select(group => group.MaxBy(project => project.LastSeenUtc)!)
            .OrderByDescending(project => project.LastSeenUtc)
            .ToArray();
    }

    private static KnownProjectSummary ReadProjectStoreSummary(KnownProjectSummary known, out bool blocked)
    {
        blocked = false;
        var fullPath = Path.GetFullPath(known.FullFwDataPath);
        var project = new ProjectLocator(fullPath, Path.GetFileNameWithoutExtension(fullPath));
        var databasePath = ProjectDatabaseCatalog.DatabasePathFor(project);
        if (!File.Exists(databasePath)) return known;
        try
        {
            return MotifDatabase.ReadExisting(databasePath, project, (connection, transaction) => ReadSummary(connection, transaction, project, known));
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode is 5 or 6)
        {
            blocked = true;
            return known;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or
                                          NotSupportedException or SqliteException or
                                          ArgumentException or FormatException or JsonException)
        {
            return known;
        }
    }

    private static KnownProjectSummary ReadSummary(
        SqliteConnection connection, SqliteTransaction transaction, ProjectLocator project, KnownProjectSummary known)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT b.ProjectIdentity, b.SourceLastWriteUtc, b.PublishedUtc,
                   json_extract(s.SummaryJson, '$.WordformCount'),
                   b.SemanticSnapshotDigest, b.ProjectionVersion, a.BaselineToken,
                   (SELECT DraftJson FROM Proposals WHERE Role = $role AND DraftName IS NOT NULL),
                   EXISTS (SELECT 1 FROM Receipts WHERE julianday(RecordedUtc) > julianday(b.PublishedUtc))
            FROM MotifMetadata m
            LEFT JOIN Baselines b ON b.ProjectKey = $project
            LEFT JOIN BaselineSummaries s ON s.ProjectKey = b.ProjectKey
            LEFT JOIN Assessments a ON a.AssessmentId = m.CurrentAssessmentId
            WHERE m.Id = 1;
            """;
        command.Parameters.AddWithValue("$project", ProjectWorkspaceKey.Compute(project));
        command.Parameters.AddWithValue("$role", ProposalRoles.PendingChanges);
        using var reader = command.ExecuteReader();
        if (!reader.Read() || reader.IsDBNull(0)) return known;
        if (!Guid.TryParse(reader.GetString(0), out var projectGuid)) return known;
        var identity = projectGuid.ToString("D");
        var sourceTime = DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture);
        _ = DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture);
        var current = sourceTime.UtcDateTime == File.GetLastWriteTimeUtc(project.FullFwDataPath);
        var savedIdentity = current ? identity : ProjectFileIdentityReader.Read(project.FullFwDataPath);
        if (!string.Equals(identity, savedIdentity, StringComparison.OrdinalIgnoreCase)) return known;
        var appliedAfter = reader.GetBoolean(8);
        var hasPending = !reader.IsDBNull(7) && HasOperations(reader.GetString(7));
        var assessment = reader.IsDBNull(6) ? null : JsonSerializer.Deserialize<BaselineToken>(reader.GetString(6));
        var hasAssessment = assessment is not null &&
            string.Equals(identity, assessment.ProjectIdentity, StringComparison.OrdinalIgnoreCase) &&
            assessment.SemanticSnapshotDigest == reader.GetString(4) &&
            assessment.ProjectionVersion == reader.GetString(5);
        return known with
        {
            ProjectIdentity = savedIdentity,
            WordformCount = reader.IsDBNull(3) ? null : reader.GetInt32(3),
            StateText = !current || appliedAfter ? "Refresh needed"
                : hasPending ? "Changes not applied yet"
                : hasAssessment ? "Results available" : "Not parsed yet",
        };
    }

    private static bool HasOperations(string json)
    {
        var draft = JsonSerializer.Deserialize<DraftDocument>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return draft?.Operations.Count > 0;
    }
}

internal static class ProjectFileIdentityReader
{
    private sealed record Entry(long Size, DateTime LastWriteUtc, string? Identity);
    private static readonly ConcurrentDictionary<string, Entry> Cache = new(
        OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);

    public static string? Read(string path)
    {
        try
        {
            path = Path.GetFullPath(path);
            var before = new FileInfo(path);
            if (!before.Exists) return null;
            var size = before.Length;
            var lastWrite = before.LastWriteTimeUtc;
            if (Cache.TryGetValue(path, out var entry) && entry.Size == size && entry.LastWriteUtc == lastWrite)
                return entry.Identity;
            var identity = ReadFile(path);
            var after = new FileInfo(path);
            if (identity is not null && after.Exists && after.Length == size && after.LastWriteTimeUtc == lastWrite)
                Cache[path] = new Entry(size, lastWrite, identity);
            return identity;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static string? ReadFile(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, bufferSize: 4096, FileOptions.SequentialScan);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreWhitespace = true,
            });
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "rt" ||
                    reader.GetAttribute("class") != "LangProject" ||
                    !Guid.TryParse(reader.GetAttribute("guid"), out var identity)) continue;
                return identity.ToString("D");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or XmlException)
        {
            return null;
        }
        return null;
    }
}

using SIL.LCModel;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Baselines;
using SIL.Motif.Scripture;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Scripture;

namespace SIL.Motif.Commands.Queries;

/// <summary>
/// Reads the "Motif Sources" list from the current Baseline, never from the live project, and places chapters against
/// each Source's Context and the Current chapter this machine keeps. Every read is a read: nothing here writes the
/// project or the local file. The local file is keyed by <see cref="ProjectWorkspaceKey.Compute"/> of the project.
/// </summary>
public static class SourceQuery
{
    /// <summary>Lists the Sources; Removed ones only when asked.</summary>
    public static CommandOutcome<SourceListResponse> List(SourceListRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Read(request.ProjectPath, request.ExpectedBaseline, (context, baseline, stale) =>
        {
            var rows = context.Entries.Where(entry => request.All || entry.Status == SourceStatus.Active)
                .Select(entry => context.Summarize(entry, null)).ToArray();
            return CommandOutcome<SourceListResponse>.Success(new(rows) { Baseline = baseline, IsStale = stale });
        });
    }

    /// <summary>Reads one Source, Removed or not.</summary>
    public static CommandOutcome<SourceShowResponse> Show(SourceShowRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Id)) return EmptyId<SourceShowResponse>();
        return Read(request.ProjectPath, request.ExpectedBaseline, (context, baseline, stale) =>
        {
            var entry = context.Find(request.Id);
            if (entry is null) return NotFound<SourceShowResponse>(request.Id);
            var summary = context.Summarize(entry, null);
            var count = entry.Context is { } parsed ? parsed.Books.Sum(book => parsed.ChaptersOf(book).Count) : 0;
            return CommandOutcome<SourceShowResponse>.Success(new(summary, count) { Baseline = baseline, IsStale = stale });
        });
    }

    /// <summary>Places a chapter in one Source, or in every Active Source.</summary>
    public static CommandOutcome<SourceWhereResponse> Where(SourceWhereRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!TryParseChapter(request.Ref, out var chapter))
            return CommandOutcome<SourceWhereResponse>.Refused(new Refusal(RefusalCodes.SourceRefInvalid, FailureReason.InvalidArgument,
                $"'{request.Ref}' is not a chapter. Write a book and a chapter number, for example MAT 5."));
        return Read(request.ProjectPath, request.ExpectedBaseline, (context, baseline, stale) =>
        {
            IReadOnlyList<SourceListEntry> asked;
            if (string.IsNullOrWhiteSpace(request.Source)) asked = context.Entries.Where(entry => entry.Status == SourceStatus.Active).ToArray();
            else if (context.Find(request.Source) is { } one) asked = [one];
            else return NotFound<SourceWhereResponse>(request.Source);
            var places = asked.Select(entry =>
            {
                var summary = context.Summarize(entry, chapter);
                return new SourcePlace(entry.Id, summary.Place, summary.Error);
            }).ToArray();
            return CommandOutcome<SourceWhereResponse>.Success(new(chapter.ToString(), places) { Baseline = baseline, IsStale = stale });
        });
    }

    /// <summary>Reads the Current chapters this machine keeps for the project; none is a valid answer.</summary>
    public static CommandOutcome<CurrentGetResponse> CurrentGet(CurrentGetRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Read(request.ProjectPath, request.ExpectedBaseline, (context, baseline, stale) =>
        {
            if (!string.IsNullOrWhiteSpace(request.Source) && context.Find(request.Source) is null &&
                !context.Local.Current.ContainsKey(request.Source.Trim()))
                return NotFound<CurrentGetResponse>(request.Source);
            var rows = context.Local.Current
                .Where(pair => string.IsNullOrWhiteSpace(request.Source) ||
                    string.Equals(pair.Key, request.Source.Trim(), StringComparison.OrdinalIgnoreCase))
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair =>
                {
                    var entry = context.Find(pair.Key);
                    var place = entry is null ? null : context.Summarize(entry, pair.Value).Place;
                    return new CurrentChapter(entry?.Id ?? pair.Key, pair.Value.ToString(), place);
                }).ToArray();
            return CommandOutcome<CurrentGetResponse>.Success(new(rows) { Baseline = baseline, IsStale = stale });
        });
    }

    private static CommandOutcome<T> Read<T>(string projectPath, SIL.Motif.Contract.Baselines.BaselineToken? expected,
        Func<SourceReadContext, SIL.Motif.Contract.Baselines.BaselineToken, bool, CommandOutcome<T>> body) where T : class =>
        ProjectStoreCommand.Run(projectPath, MotifProductVersion.CurrentText, (database, project) =>
        {
            var baseline = new BaselineRepository(database).GetCurrent(ProjectWorkspaceKey.Compute(project));
            if (expected is { } wanted && wanted != baseline?.Token)
                return CommandOutcome<T>.Refused(new Refusal(RefusalCodes.GdrBaselineChanged, FailureReason.Refused,
                    "The Baseline changed before the Sources were read. Refresh and try again."));
            if (baseline is null)
                return CommandOutcome<T>.Refused(new Refusal(RefusalCodes.BaselineMissing, FailureReason.NotFound,
                    "This project has no Baseline yet. Capture one to read its Sources."));
            if (!File.Exists(baseline.FwDataPath))
                return CommandOutcome<T>.Refused(new Refusal(RefusalCodes.GdrBaselineUnavailable, FailureReason.StoreInconsistent,
                    "The exact Baseline file is unavailable. Capture a new Baseline to read its Sources."));
            using var reader = BaselineReadCache.Open(baseline.FwDataPath);
            SourceReadContext context;
            try
            {
                context = new SourceReadContext(
                    SourceList.Read(reader.Cache), ScriptureLocalStore.ForInstallation(), ProjectWorkspaceKey.Compute(project));
            }
            catch (SourceListAmbiguousException ambiguous)
            {
                return CommandOutcome<T>.Refused(new Refusal(RefusalCodes.SourceListAmbiguous, FailureReason.Refused, ambiguous.Message));
            }
            var stale = File.Exists(project.FullFwDataPath) &&
                new DateTimeOffset(File.GetLastWriteTimeUtc(project.FullFwDataPath), TimeSpan.Zero) > baseline.SourceLastWriteUtc;
            return body(context, baseline.Token, stale);
        });

    private static bool TryParseChapter(string? text, out ChapterRef chapter)
    {
        chapter = default;
        var parts = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !int.TryParse(parts[1], out var number) || number < 1) return false;
        var book = parts[0].ToUpperInvariant();
        if (Canon.BookIdToNumber(book) <= 0) return false;
        chapter = new ChapterRef(book, number);
        return true;
    }

    private static CommandOutcome<T> EmptyId<T>() where T : class => CommandOutcome<T>.Refused(new Refusal(
        RefusalCodes.SourceIdInvalid, FailureReason.InvalidArgument, "Name a Source, for example WEB."));

    private static CommandOutcome<T> NotFound<T>(string id) where T : class => CommandOutcome<T>.Refused(new Refusal(
        RefusalCodes.SourceNotFound, FailureReason.NotFound, $"No Source is named '{id.Trim()}'."));

    private sealed class SourceReadContext(IReadOnlyList<SourceListEntry> entries, ScriptureLocalStore store, string projectId)
    {
        public IReadOnlyList<SourceListEntry> Entries { get; } = entries;
        public ScriptureLocalState Local { get; } = store.Read(projectId);

        public SourceListEntry? Find(string id) =>
            Entries.FirstOrDefault(entry => string.Equals(entry.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// The row for a Source. With a chapter it places it: against the opened Paratext folder when one is recorded
        /// and readable, else by the Context and Current chapter alone.
        /// </summary>
        public SourceSummary Summarize(SourceListEntry entry, ChapterRef? chapter)
        {
            var folder = Local.SourcePaths.GetValueOrDefault(entry.Id);
            var summary = new SourceSummary(entry.Id, entry.Name, entry.Kind.ToString(), entry.Status.ToString(),
                entry.VersificationName, entry.ContextText, Ranges(entry.Context), folder, entry.Error);
            if (chapter is not { } asked || entry.Context is not { } parsed) return summary;

            var current = Local.Current.TryGetValue(entry.Id, out var kept) ? kept : (ChapterRef?)null;
            if (folder is null || !Directory.Exists(folder))
                return summary with { Place = Place(parsed, current, asked) };
            try
            {
                var source = ParatextSourceReader.Open(folder);
                return summary with { Place = SourceScope.Classify(source, parsed, current, asked).ToString() };
            }
            catch (ArgumentException)
            {
                return summary with { Error = $"The Source '{entry.Id}' has no text for {asked}." };
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                return summary with { Error = $"The folder of '{entry.Id}' could not be read: {exception.Message}" };
            }
        }

        private static string Place(ScriptureContext context, ChapterRef? current, ChapterRef chapter) =>
            (context.Contains(chapter.Book, chapter.Chapter) ? ChapterScope.InContext
                : current == chapter ? ChapterScope.Current : ChapterScope.Later).ToString();

        // Normalize() writes "GEN; 1CO 13-15": a book alone is the whole book.
        private static IReadOnlyList<SourceContextRange> Ranges(ScriptureContext? context) =>
            context is null ? [] : context.Normalize().Split("; ", StringSplitOptions.RemoveEmptyEntries).Select(part =>
            {
                var space = part.IndexOf(' ');
                return space < 0 ? new SourceContextRange(part, "all") : new SourceContextRange(part[..space], part[(space + 1)..]);
            }).ToArray();
    }
}

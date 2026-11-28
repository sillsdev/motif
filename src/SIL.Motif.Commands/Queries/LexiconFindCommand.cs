using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SIL.LCModel;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Query;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Baselines;
using SIL.Motif.Projection;
using SIL.Motif.Projection.Grammar;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;

namespace SIL.Motif.Commands.Queries;

/// <summary>
/// Finds lexical entries in the current Baseline. The CLI's <c>lexicon find</c> and the MCP tool <c>motif_lexicon</c>
/// both run <see cref="ReadPage"/>, so a filter means the same in each.
/// </summary>
public static class LexiconFindCommand
{
    /// <summary>The most entries one page returns.</summary>
    public const int MaximumPageSize = 1_000_000;

    /// <summary>The largest offset a page may start at.</summary>
    public const int MaximumOffset = 10_000_000;

    /// <summary>Reads one page for the CLI, always with forms and inflection classes on each entry.</summary>
    public static CommandOutcome<JsonObject> Find(LexiconFindRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Open(request.ProjectPath, request.At, (cache, stamp) =>
            Page(cache, request).Map(page => { stamp(page); return page; }));
    }

    /// <summary>
    /// Reads one page per request from a single open copy of the Baseline, so a lane asking many cheap questions pays the
    /// cost of opening the project once. Each request keeps its own outcome; the project and <c>at</c> come from the first.
    /// </summary>
    public static CommandOutcome<IReadOnlyList<CommandOutcome<JsonObject>>> FindMany(
        string projectPath, string? at, IReadOnlyList<LexiconFindRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);
        return Open(projectPath, at, (cache, stamp) =>
        {
            var pages = requests.Select(request => Page(cache, request).Map(page => { stamp(page); return page; })).ToList();
            return CommandOutcome<IReadOnlyList<CommandOutcome<JsonObject>>>.Success(pages);
        });
    }

    private static CommandOutcome<JsonObject> Page(LcmCache cache, LexiconFindRequest request)
    {
        if (Validate(request) is { } refusal) return CommandOutcome<JsonObject>.Refused(refusal);
        var filter = new LexiconFilter(request.Query, request.MorphTypes, request.Affix, request.Category, request.Headword,
            request.HeadwordContains, request.HasGloss, request.Exact, request.FormRegex, request.Suffix, request.InflectionClass,
            request.Environment, request.StemName, request.UsedBy);
        return ReadPage(cache, filter, request.Limit, request.CursorOffset(), detailed: false, withForms: true, request.IdFormat, withAllomorphs: request.Detail);
    }

    /// <summary>The refusal for a request whose page, pattern or text is unusable, or null when it can run.</summary>
    public static Refusal? Validate(LexiconFindRequest request)
    {
        if (request.CursorOffset() is < 0 or > MaximumOffset)
            return PagingRefusal("The cursor must be the nextCursor a previous page returned.");
        if (request.Limit is < 1 or > MaximumPageSize)
            return PagingRefusal($"The limit must be between 1 and {MaximumPageSize}.");
        if (request.FormRegex is { } composable)
        {
            try { _ = PatternNormalization.Pattern(composable); }
            catch (QueryRefusalException refused) { return refused.Refusal; }
        }
        if (request.FormRegex is { } pattern && LexiconFilter.RegexProblem(pattern) is { } problem)
            return InvalidRefusal($"--form-regex is not a valid regular expression: {problem}");
        if (request.Exact is not null && string.IsNullOrWhiteSpace(request.Exact))
            return InvalidRefusal("An exact form cannot be empty.");
        return null;
    }

    private static CommandOutcome<T> Open<T>(string projectPath, string? at,
        Func<LcmCache, Action<JsonObject>, CommandOutcome<T>> use) where T : class
    {
        if (GrammarReadQuery.ValidateAt(at, out var proposalId) is { } badAt)
            return CommandOutcome<T>.Refused(badAt);
        return ProjectStoreCommand.ReadExisting(projectPath,
            CommandOutcome<T>.Refused(new Refusal(RefusalCodes.LexiconFindBaselineMissing, FailureReason.NotFound,
                "Capture a Baseline before reading the lexicon.")),
            (database, project) =>
            {
                if (proposalId is not null)
                {
                    var traced = TracedGrammar.Resolve(database, project, proposalId);
                    if (!traced.Succeeded) return CommandOutcome<T>.Refused(traced.Refusal!);
                    using var trialReader = BaselineReadCache.Open(traced.Value!.FwDataPath);
                    return use(trialReader.Cache, page => page["at"] = traced.Value.TracedAgainst);
                }
                var baseline = new BaselineRepository(database).GetCurrent(ProjectWorkspaceKey.Compute(project));
                if (baseline is null)
                    return CommandOutcome<T>.Refused(new Refusal(RefusalCodes.LexiconFindBaselineMissing,
                        FailureReason.NotFound, "Capture a Baseline before reading the lexicon."));
                if (!File.Exists(baseline.FwDataPath))
                    return CommandOutcome<T>.Refused(new Refusal(RefusalCodes.LexiconFindBaselineUnavailable,
                        FailureReason.StoreInconsistent, "The current Baseline file is missing. Capture a new Baseline before reading the lexicon."));
                using var reader = BaselineReadCache.Open(baseline.FwDataPath);
                return use(reader.Cache, page =>
                {
                    page["at"] = "baseline";
                    page["source"] = ProjectionJson.ToNode(new BaselineReadSource(
                        baseline.Token, baseline.SourceLastWriteUtc, baseline.PublishedUtc));
                });
            });
    }

    /// <summary>
    /// One page of <paramref name="filter"/> from an open Baseline copy, refusing an unknown morph type or a pattern that
    /// takes too long. This is the one implementation the MCP tool and the CLI verb share.
    /// </summary>
    public static CommandOutcome<JsonObject> ReadPage(LcmCache cache, LexiconFilter filter, int limit, int offset,
        bool detailed, bool withForms = false, LexiconIdFormat idFormat = LexiconIdFormat.Canonical, bool withAllomorphs = false)
    {
        if (filter.MorphTypesOrNull is { Count: > 0 } types &&
            GrammarReadProjectionBuilder.UnknownMorphTypes(cache, string.Join(",", types)) is { Count: > 0 } unknown)
            return Invalid(RefusalCodes.GrammarReadInvalidRequest,
                $"Unknown morph type {string.Join(", ", unknown.Select(name => $"'{name}'"))}. " +
                $"Morph types are: {string.Join(", ", GrammarReadProjectionBuilder.MorphTypeNames(cache))}.");
        if (filter.AmbiguousUsedBy(cache) is { Count: > 0 } candidates)
            return Invalid(RefusalCodes.LexiconUsedByAmbiguous,
                $"--used-by '{filter.UsedBy}' names more than one slot or template: {string.Join("; ", candidates)}. Pass the id of one of them.");
        JsonObject page;
        try { page = LexiconReader.Read(cache, filter, limit, offset, detailed, withForms, withAllomorphs); }
        catch (RegexMatchTimeoutException)
        {
            return Invalid($"--form-regex took longer than {LexiconFilter.RegexTimeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds on one form; simplify the pattern.");
        }
        if (idFormat == LexiconIdFormat.Guid) ConvertIds(page);
        return CommandOutcome<JsonObject>.Success(page);
    }

    // Canonical ids sit under "id" and "morphTypeId" keys at any depth; a guid spelling replaces each in place.
    private static void ConvertIds(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(pair => pair.Key).ToList())
                {
                    if (key is "id" or "morphTypeId" && obj[key] is JsonValue value && value.TryGetValue<string>(out var text) &&
                        CanonicalId.TryParse(text, out var id))
                        obj[key] = id.ToGuid().ToString("D", CultureInfo.InvariantCulture);
                    else
                        ConvertIds(obj[key]);
                }
                break;
            case JsonArray array:
                foreach (var item in array) ConvertIds(item);
                break;
        }
    }

    private static Refusal PagingRefusal(string message) =>
        new(RefusalCodes.PagingInvalid, FailureReason.InvalidArgument, message);

    private static Refusal InvalidRefusal(string message) =>
        new(RefusalCodes.LexiconFindInvalidRequest, FailureReason.InvalidArgument, message);

    private static CommandOutcome<JsonObject> Invalid(string message) => CommandOutcome<JsonObject>.Refused(InvalidRefusal(message));

    private static CommandOutcome<JsonObject> Invalid(string code, string message) =>
        CommandOutcome<JsonObject>.Refused(new Refusal(code, FailureReason.InvalidArgument, message));
}

using System.Text.Json;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Query;
using SIL.Motif.Contract.Query.Phon;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Baselines;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Projects;

namespace SIL.Motif.Commands.Queries;

/// <summary>
/// Runs <c>phon</c> and <c>env</c> patterns on the resident <c>pangloss serve</c> that holds the grammar, through its
/// <c>match</c> op: one request per pattern and batch of words, never one process per word.
/// </summary>
internal sealed class ResidentPhonMatcher(WarmPanGlossPool pool, string key, string stateToken, string grammarPath,
    string? parserVersion) : IPhonMatcher
{
    public string? ParserVersion { get; } = parserVersion;

    public PhonBatchResult Match(string patternText, IReadOnlyList<string> words, CancellationToken cancellationToken)
    {
        var reply = pool.TryMatchAsync(key, stateToken, grammarPath, new ServeMatchRequest(patternText, words), cancellationToken)
            .GetAwaiter().GetResult();
        switch (reply)
        {
            case WarmMatchResult.Completed completed:
                return Read(completed.Json);
            case WarmMatchResult.Unsupported unsupported:
                throw QueryRefusalException.PhonUnavailable(PanGlossInterfaceVersions.ServeMatchNeeded, unsupported.Detail);
            case WarmMatchResult.Rejected rejected:
                throw Refused(rejected, patternText);
            case WarmMatchResult.Failed failed:
                throw QueryRefusalException.PhonFailed($"The resident PanGloss could not match the pattern ({failed.Code}): {failed.Message}");
            default:
                throw new OperationCanceledException(cancellationToken);
        }
    }

    // PanGloss's typed error, kept whole: its code, and every field it names, ride along as facts.
    internal static QueryRefusalException Refused(WarmMatchResult.Rejected rejected, string patternText)
    {
        var facts = new Dictionary<string, string>(StringComparer.Ordinal) { ["panglossCode"] = rejected.Code, ["pattern"] = patternText };
        try
        {
            using var document = JsonDocument.Parse(rejected.Json);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Name is "id" or "type" or "op" or "code" or "message") continue;
                facts[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString() ?? "",
                    JsonValueKind.Array => string.Join(", ", property.Value.EnumerateArray().Select(item => item.ToString())),
                    _ => property.Value.ToString(),
                };
            }
        }
        catch (JsonException) { }
        var detail = facts.TryGetValue("construct", out var construct)
            ? $"PanGloss does not support {construct} patterns yet ({rejected.Code}): {rejected.Message}"
            : $"PanGloss refused the pattern ({rejected.Code}): {rejected.Message}";
        return QueryRefusalException.PhonPatternRefused(detail + $" The pattern sent was: {patternText}", facts);
    }

    internal static PhonBatchResult Read(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var words = new List<PhonWordResult>();
            foreach (var word in root.GetProperty("words").EnumerateArray())
            {
                var spans = new List<PhonRawSpan>();
                if (word.TryGetProperty("matches", out var matches))
                    foreach (var match in matches.EnumerateArray())
                    {
                        var segments = match.GetProperty("segments");
                        var offsets = match.GetProperty("offsets");
                        spans.Add(new PhonRawSpan(segments[0].GetInt32(), segments[1].GetInt32(), offsets[0].GetInt32(), offsets[1].GetInt32()));
                    }
                string? Text(string name) => word.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                int? charIndex = word.TryGetProperty("at", out var at) && at.TryGetProperty("charIndex", out var index) ? index.GetInt32() : null;
                words.Add(new PhonWordResult(word.GetProperty("word").GetString() ?? "", word.GetProperty("status").GetString() ?? "",
                    spans, Text("reason") ?? Text("stop"), charIndex, Text("normalized")));
            }
            return new PhonBatchResult(words, root.GetProperty("complete").GetBoolean(),
                root.TryGetProperty("stopReason", out var stop) && stop.ValueKind == JsonValueKind.String ? stop.GetString() : null);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw QueryRefusalException.PhonFailed("PanGloss wrote a match answer Motif cannot read: " + exception.Message);
        }
    }
}

/// <summary>Chooses the grammar and resident parser a query's patterns run on.</summary>
internal static class PhonMatchers
{
    /// <summary>The project's current Baseline grammar on the shared warm pool, the same session Try a Word uses.</summary>
    internal static CommandOutcome<IPhonMatcher> ForProject(MotifDatabase database, ProjectLocator project, string projectKey, string? parserPath)
    {
        var executable = parserPath ?? PanGlossExecutable.TryLocate();
        if (executable is null) return Missing();
        var baseline = new SIL.Motif.Worker.Baselines.BaselineRepository(database).GetCurrentPublished(ProjectWorkspaceKey.Compute(project));
        if (baseline is null)
            return CommandOutcome<IPhonMatcher>.Refused(new Refusal(RefusalCodes.WordTraceNoBaseline, FailureReason.Refused,
                "The project has no Baseline yet. Run 'motif baseline capture --project <fwdata>' before a phon or env query."));
        return CommandOutcome<IPhonMatcher>.Success(new ResidentPhonMatcher(TryWordSessions.PoolFor(executable), projectKey,
            TryWordSessions.TokenText(baseline.Token), baseline.FwDataPath, ParserExecutableIdentity.ReadVersion(executable)));
    }

    /// <summary>A grammar file named on the command line, such as <c>--grammar</c>; the state token is the file's digest so an edit restarts the session.</summary>
    internal static CommandOutcome<IPhonMatcher> ForGrammar(string grammarPath, string? parserPath)
    {
        var executable = parserPath ?? PanGlossExecutable.TryLocate();
        if (executable is null) return Missing();
        if (!File.Exists(grammarPath))
            return CommandOutcome<IPhonMatcher>.Refused(new Refusal(RefusalCodes.QueryCorpusUnreadable, FailureReason.InvalidArgument,
                $"The grammar '{grammarPath}' is not a file."));
        var full = Path.GetFullPath(grammarPath);
        return CommandOutcome<IPhonMatcher>.Success(new ResidentPhonMatcher(TryWordSessions.PoolFor(executable), full,
            BatchInvocationEvidence.DigestFile(full), full, ParserExecutableIdentity.ReadVersion(executable)));
    }

    private static CommandOutcome<IPhonMatcher> Missing() => CommandOutcome<IPhonMatcher>.Refused(new Refusal(
        RefusalCodes.WordTraceParserUnavailable, FailureReason.Refused, PanGlossExecutable.NotFoundMessage,
        ParserNotFoundFact.Mark(null, true)));
}

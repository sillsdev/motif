using SIL.LCModel;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Baselines;
using SIL.Motif.Projection;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;

namespace SIL.Motif.Commands.Queries;

/// <summary>
/// Reads an entry, or the entry that owns a sense, from the current Baseline: the headword and every sense with its
/// glosses. It opens the saved copy, never the live project, and needs no Trial or Selection.
/// </summary>
public static class LexicalEntryQuery
{
    /// <summary>The most ids one read takes, so a single call stays one bounded pass over the copy.</summary>
    public const int MaxIds = 100;

    /// <summary>Reads every requested id from the current Baseline; an unusable request or Baseline comes back as a Refusal.</summary>
    public static CommandOutcome<LexicalEntryResponse> Query(LexicalEntryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Ids is null || request.Ids.Count == 0)
            return Refused(RefusalCodes.LexicalEntryNoId, FailureReason.InvalidArgument, "Name at least one entry or sense id.");
        if (request.Ids.Count > MaxIds)
            return Refused(RefusalCodes.LexicalEntryTooManyIds, FailureReason.InvalidArgument,
                $"Read at most {MaxIds} ids at a time.");
        var parsed = new List<(string Id, CanonicalId Canonical)>();
        foreach (var raw in request.Ids)
        {
            var id = raw?.Trim() ?? "";
            if (!CanonicalId.TryParse(id, out var canonical))
                return Refused(RefusalCodes.LexicalEntryInvalidId, FailureReason.InvalidArgument,
                    $"'{id}' is not a portable id. Use an id from a Proposal change or from motif_lexicon.");
            parsed.Add((id, canonical));
        }

        return ProjectStoreCommand.ReadExisting(request.ProjectPath,
            Refused(RefusalCodes.BaselineMissing, FailureReason.NotFound, "Motif has no saved copy of this project to read yet."),
            (database, project) =>
        {
            var baseline = new BaselineRepository(database).GetCurrent(ProjectWorkspaceKey.Compute(project));
            if (baseline is null)
                return Refused(RefusalCodes.BaselineMissing, FailureReason.NotFound,
                    "Motif has no saved copy of this project to read yet.");
            if (!File.Exists(baseline.FwDataPath))
                return Refused(RefusalCodes.LexicalEntryBaselineUnavailable, FailureReason.StoreInconsistent,
                    "Motif's saved copy of this project is missing from disk.");
            using var reader = BaselineReadCache.Open(baseline.FwDataPath);
            var lookups = parsed.Select(item => Read(reader.Cache, item.Id, item.Canonical)).ToArray();
            return CommandOutcome<LexicalEntryResponse>.Success(new LexicalEntryResponse(
                lookups, baseline.Token, baseline.SourceLastWriteUtc, baseline.PublishedUtc));
        });
    }

    private static LexicalEntryLookup Read(LcmCache cache, string id, CanonicalId canonical)
    {
        var missing = new LexicalEntryLookup(id, false, null, null, null, []);
        if (!cache.ServiceLocator.ObjectRepository.TryGetObject(canonical.ToGuid(), out var target)) return missing;
        ILexEntry? entry;
        string? senseId = null;
        switch (target)
        {
            case ILexEntry asked: entry = asked; break;
            case ILexSense sense: entry = sense.Entry; senseId = id; break;
            default: return missing;
        }
        if (entry is null) return missing;
        var senses = entry.AllSenses.Select(sense =>
        {
            var gloss = WritingSystemTextReader.BestAnalysis(cache, sense.Gloss);
            return new LexicalSenseRead(CanonicalId.FromGuid(sense.Guid).Value, sense.LexSenseOutline?.Text ?? "",
                gloss.Text, gloss.WritingSystem, Alternatives(cache, sense.Gloss));
        }).ToArray();
        return new LexicalEntryLookup(id, true, CanonicalId.FromGuid(entry.Guid).Value, entry.HeadWord?.Text, senseId, senses);
    }

    private static Dictionary<string, string> Alternatives(LcmCache cache, IMultiAccessorBase accessor)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var ws in accessor.AvailableWritingSystemIds.Order())
            if (accessor.get_String(ws)?.Text is { Length: > 0 } text)
                result[cache.WritingSystemFactory.GetStrFromWs(ws)] = text;
        return result;
    }

    private static CommandOutcome<LexicalEntryResponse> Refused(string code, FailureReason reason, string message) =>
        CommandOutcome<LexicalEntryResponse>.Refused(new Refusal(code, reason, message));
}

using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Query;
using SIL.Motif.Host.Config;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Store;

namespace SIL.Motif.Commands.Queries;

/// <summary>Reads what a hypothesised-change query needs besides the stems: attested word sets and parser evidence.</summary>
internal static class QueryHypothesisInputsReader
{
    internal static QueryHypothesisInputs Read(QueryRecord query, ProjectLocator project, MotifDatabase database,
        IReadOnlyCollection<(string Form, string Entry)>? formEntries)
    {
        var change = query.Change!;
        var heldOut = change.HeldOut ?? [];
        var discovery = change.Discovery ?? [];
        if (change.WordSets.Intersect(heldOut, StringComparer.Ordinal).Any())
            throw new ArgumentException("A held-out word set cannot also be the evidence word set: held-out words are read by control alone, as counts.");
        var configuration = new ProjectConfigurationReader().Read(project);
        var path = ProjectConfigurationReader.PathFor(project);
        var weights = new Dictionary<string, long>(StringComparer.Ordinal);
        IReadOnlyCollection<string> Words(IEnumerable<string> ids, Dictionary<string, long>? weigh = null)
        {
            var words = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in ids.Distinct(StringComparer.Ordinal))
            {
                var set = configuration.ReviewWordSets.FirstOrDefault(s => s.Id == id) ?? throw new ArgumentException(
                    $"Word set '{id}' is not declared; declared sets: {string.Join(", ", configuration.ReviewWordSets.Select(s => s.Id))}.");
                if (set.Role != "sampled-tokens" || set.Manifest is null)
                    throw new ArgumentException($"Word set '{id}' has role {set.Role}; only sampled-tokens sets are attested evidence, " +
                        "and a paradigm cell or reference set is generated.");
                foreach (var member in ReviewWordSetManifestReader.Read(set.ResolveManifestPath(path)!, set.Role).Members)
                {
                    words.Add(member.Word);
                    if (weigh is not null && member.Token is { } token) weigh[member.Word] = weigh.GetValueOrDefault(member.Word) + token.Weight;
                }
            }
            return words;
        }
        return new QueryHypothesisInputs(Words(change.WordSets, weights), Words(discovery), Words(heldOut), formEntries ?? [],
            () => ReadParse(database, project), weights.Count == 0 ? null : weights);
    }

    private static QueryParseEvidence? ReadParse(MotifDatabase database, ProjectLocator project)
    {
        var current = CurrentEvidenceQuery.ReadCurrentEvidence(database, project, includeDefaultSelection: false,
            includeResolvedReadings: false, includeWordContext: false);
        if (!current.Succeeded || current.Value!.MatchingAssessment is null) return null;
        var parses = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var word in current.Value.EffectiveWords)
        {
            if (!word.Outcome.TryParseStoredOutcome(out var outcome) || word.IsIncomplete) continue;
            if (outcome == WordOutcome.Analysed) parses[word.Word] = true;
            else if (outcome == WordOutcome.NoAnalysis) parses.TryAdd(word.Word, false);
        }
        return new QueryParseEvidence(parses, current.Value.Freshness != EvidenceFreshness.Current);
    }
}

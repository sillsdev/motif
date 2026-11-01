using System.Security.Cryptography;
using System.Text;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Store;
using SIL.Motif.Projection.Store;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands;

/// <summary>
/// The decisions a named non-human agent staged through Applied Proposals, read from Motif's store as they were
/// staged. They are never a person's decisions, and the project is not opened to read them.
/// </summary>
internal sealed class SourceValidatedDecisions
{
    private static readonly HashSet<string> IncorrectTiers = new(StringComparer.Ordinal)
    {
        "misspelled-typo", "ill-formed",
    };

    private const int MostCachedProjects = 4;

    private sealed record StagedEvidence(string Tier, string? Agent, string? Opinion, Guid? AgentId);

    private static readonly object CacheGate = new();
    private static readonly Dictionary<string, (string Key, SourceValidatedDecisions Value)> Cache = new(StringComparer.Ordinal);

    private readonly Dictionary<string, List<StagedEvidence>> _evidence;

    private SourceValidatedDecisions(Dictionary<(Guid AnalysisId, Guid AgentId), string> opinions,
        HashSet<Guid> incorrectWordforms, Dictionary<string, List<StagedEvidence>> evidence,
        IReadOnlyList<ProposalDifferenceUnreadableEvidence> unreadable)
    {
        Opinions = opinions;
        IncorrectWordforms = incorrectWordforms;
        _evidence = evidence;
        Unreadable = unreadable;
        var resolved = new Dictionary<Guid, string>();
        var conflicting = new HashSet<Guid>();
        foreach (var group in opinions.GroupBy(pair => pair.Key.AnalysisId))
        {
            var values = group.Select(pair => pair.Value).Distinct(StringComparer.Ordinal).Take(2).ToArray();
            resolved[group.Key] = values.Length == 1 ? values[0] : "unknown";
            if (values.Length > 1) conflicting.Add(group.Key);
        }
        ResolvedOpinions = resolved;
        ConflictingAnalysisIds = conflicting;
    }

    /// <summary>The latest staged opinion per agent and analysis, <c>approved</c> or <c>disapproved</c>.</summary>
    public IReadOnlyDictionary<(Guid AnalysisId, Guid AgentId), string> Opinions { get; }

    /// <summary>Analysis ids where staged agents disagree.</summary>
    public IReadOnlySet<Guid> ConflictingAnalysisIds { get; }

    /// <summary>Wordforms whose newest staged tier says the form is wrong.</summary>
    public IReadOnlySet<Guid> IncorrectWordforms { get; }

    /// <summary>Applied Proposals whose revision could not be read; their decisions are left out.</summary>
    public IReadOnlyList<ProposalDifferenceUnreadableEvidence> Unreadable { get; }

    /// <summary>Returns an opinion only when all staged agents agree; disagreements stay unknown.</summary>
    public string OpinionFor(Guid analysisId) => ResolvedOpinions.GetValueOrDefault(analysisId, "unknown");

    private IReadOnlyDictionary<Guid, string> ResolvedOpinions { get; }

    /// <summary>
    /// A fingerprint of every staged decision, so a saved Difference can tell whether the decisions it was read
    /// against are still the ones staged. It does not grow with the number of decisions. It leaves out the
    /// evidence tier, which is read when a page is shown and so cannot make a saved Difference stale.
    /// </summary>
    public string Digest
    {
        get
        {
            var lines = Opinions.Select(pair => $"o\n{pair.Key.AnalysisId:D}\n{pair.Key.AgentId:D}\n{pair.Value}")
                .Concat(IncorrectWordforms.Select(id => $"i\n{id:D}"))
                .Concat(Unreadable.Select(item => $"u\n{item.ProposalId}")).Order(StringComparer.Ordinal);
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\u001e', lines))))
                .ToLowerInvariant();
        }
    }

    /// <summary>
    /// Marks the analyses on which staged agents disagree as conflicting decisions, so neither opinion is counted
    /// as approved. The result does not depend on the order the Proposals were applied in.
    /// </summary>
    public ProposalDifferenceCaseFacts MarkDisagreements(ProposalDifferenceCaseFacts facts, IEnumerable<Guid> analysisIds)
    {
        var ids = analysisIds.Where(ConflictingAnalysisIds.Contains).Select(id => CanonicalId.FromGuid(id).Value)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return ids.Length == 0 ? facts : facts with { Before = MarkDisagreements(facts.Before, ids), After = MarkDisagreements(facts.After, ids) };
    }

    private static HumanAgreementFacts MarkDisagreements(HumanAgreementFacts facts, string[] analysisIds)
    {
        var opposed = facts.Conflicts.Where(conflict => conflict.Kind == HumanAgreementConflictKind.OpposedOpinions)
            .SelectMany(conflict => conflict.AnalysisIds).Concat(analysisIds)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var conflicts = facts.Conflicts.Where(conflict => conflict.Kind != HumanAgreementConflictKind.OpposedOpinions)
            .Append(new HumanAgreementConflict(HumanAgreementConflictKind.OpposedOpinions, opposed)).ToArray();
        var state = facts.ApprovedState is HumanAgreementApprovalState.Unavailable or HumanAgreementApprovalState.NotMeasured
            ? facts.ApprovedState : HumanAgreementApprovalState.ConflictingDecisions;
        return facts with { ApprovedState = state, Conflicts = conflicts };
    }

    /// <summary>
    /// Attaches to each word the tier and agent of the evidence staged for its wordform and for the analyses its facts
    /// name, found in the Applied Proposals' records. Nothing is stored, so the kept record does not grow.
    /// </summary>
    public IReadOnlyList<ProposalDifferenceWord> WithEvidence(IReadOnlyList<ProposalDifferenceWord> words)
    {
        if (words.Count == 0 || _evidence.Count == 0) return words;
        return [.. words.Select(word =>
        {
            var subjects = AnalysisIds(word.Facts).Select(id => (Id: id, Level: "analysis")).ToList();
            if (word.Facts.WordformId is { } wordformId) subjects.Insert(0, (wordformId, "wordform"));
            var evidence = subjects.Where(subject => _evidence.ContainsKey(subject.Id)).SelectMany(subject =>
                _evidence[subject.Id].Select(item => new ProposalDifferenceWordEvidence(subject.Id, subject.Level,
                    item.Tier, item.Agent, item.Opinion))).ToArray();
            return evidence.Length == 0 ? word : word with { Evidence = evidence };
        })];
    }

    // Every analysis a word's classification names, on either side, in a stable order.
    private static IEnumerable<string> AnalysisIds(ProposalDifferenceCaseFacts facts) =>
        new[] { facts.Before, facts.After }.SelectMany(side => side.MatchedApprovedAnalysisIds
                .Concat(side.MatchedDisapprovedAnalysisIds).Concat(side.MatchedUnknownAnalysisIds)
                .Concat(side.ProvenMissingApprovedAnalysisIds).Concat(side.ObservedUnmatchedExpectedAnalysisIds))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);

    /// <summary>Drops what was kept from earlier reads, so the next read parses the Applied revisions again.</summary>
    internal static void ForgetKept()
    {
        lock (CacheGate) Cache.Clear();
    }

    /// <summary>
    /// Reads Applied Proposals in application order, retaining each agent's newest staging. A Proposal whose revision
    /// cannot be read is named in <see cref="Unreadable"/> rather than failing the read. The result is kept for the
    /// next read while the Applied revisions are the same ones.
    /// </summary>
    public static SourceValidatedDecisions Read(MotifDatabase database)
    {
        var appliedAt = new Dictionary<string, string>(StringComparer.Ordinal);
        var applied = new List<(string Id, string? Label, string Revision)>();
        using (var connection = database.OpenConnection())
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT ProposalId, MAX(RecordedUtc) FROM Receipts GROUP BY ProposalId;";
                using var reader = command.ExecuteReader();
                while (reader.Read()) appliedAt[reader.GetString(0)] = reader.GetString(1);
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT ProposalId, Label, CurrentIntentDigest FROM Proposals " +
                    $"WHERE Status = '{ManifestStatus.Applied}' AND DraftName IS NULL ORDER BY ProposalId;";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                    applied.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1),
                        $"{reader.GetString(0)}\n{(reader.IsDBNull(2) ? "" : reader.GetString(2))}\n" +
                        appliedAt.GetValueOrDefault(reader.GetString(0), "")));
            }
        }
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\u001e', applied.Select(item => item.Revision)))));
        lock (CacheGate)
            if (Cache.TryGetValue(database.FullPath, out var kept) && kept.Key == key) return kept.Value;

        var loaded = Load(database, applied.Select(item => (item.Id, item.Label)).ToArray(), appliedAt);
        lock (CacheGate)
        {
            if (loaded.Unreadable.Count > 0) Cache.Remove(database.FullPath);
            else
            {
                if (Cache.Count >= MostCachedProjects && !Cache.ContainsKey(database.FullPath)) Cache.Clear();
                Cache[database.FullPath] = (key, loaded);
            }
        }
        return loaded;
    }

    private static SourceValidatedDecisions Load(MotifDatabase database, (string Id, string? Label)[] listed,
        Dictionary<string, string> appliedAt)
    {
        var repository = new ProposalRepository(database);
        var applied = listed
            .OrderBy(item => appliedAt.GetValueOrDefault(item.Id, string.Empty), StringComparer.Ordinal)
            .ThenBy(item => item.Id, StringComparer.Ordinal).ToArray();
        var opinions = new Dictionary<(Guid AnalysisId, Guid AgentId), string>();
        var incorrect = new HashSet<Guid>();
        var evidenceBySubject = new Dictionary<string, List<StagedEvidence>>(StringComparer.Ordinal);
        var unreadable = new List<ProposalDifferenceUnreadableEvidence>();
        foreach (var record in applied)
        {
            Proposal envelope;
            try { envelope = repository.GetFinalized(CanonicalId.Parse(record.Id)).Envelope; }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                unreadable.Add(new ProposalDifferenceUnreadableEvidence(record.Id, record.Label,
                    ProposalCommands.ProposalLoadRefusal(exception).Message));
                continue;
            }
            foreach (var operation in envelope.Operations)
            {
                if (operation.Target is not { } target) continue;
                var evidence = DecisionEvidence.FromProvenance(operation.Provenance);
                if (evidence is null) continue;
                string? agentName = null, agentOpinion = null;
                Guid? agentId = null;
                if (operation.Kind == WfiAnalysisAgentEvaluationOperationKinds.Add && operation.After is { } after)
                {
                    var evaluation = AgentEvaluationPayload.Parse(after);
                    opinions[(target.ToGuid(), evaluation.AgentId.ToGuid())] =
                        evaluation.Approves ? "approved" : "disapproved";
                    agentName = evaluation.Name;
                    agentId = evaluation.AgentId.ToGuid();
                    agentOpinion = evaluation.Approves ? "approves" : "disapproves";
                }
                else if (operation.Kind == DecisionEvidenceOperationKinds.Record)
                {
                    if (IncorrectTiers.Contains(evidence.Tier)) incorrect.Add(target.ToGuid());
                    else incorrect.Remove(target.ToGuid());
                }
                else continue;
                if (!evidenceBySubject.TryGetValue(target.Value, out var list))
                    evidenceBySubject[target.Value] = list = [];
                list.RemoveAll(earlier => earlier.AgentId == agentId);
                list.Add(new StagedEvidence(evidence.Tier, agentName, agentOpinion, agentId));
            }
        }
        return new SourceValidatedDecisions(opinions, incorrect, evidenceBySubject, unreadable);
    }
}

using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Projection;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Baselines;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands;

/// <summary>
/// The attested words a Proposal's edits could affect: those whose stored Baseline analyses use an allomorph or
/// grammatical info the Proposal edits, retires or places an alternate beside. Every match is by exact id.
/// </summary>
/// <param name="Words">The matched words, in Baseline order, at most <see cref="TrialTouchedWords.Cap"/>.</param>
/// <param name="Matched">Every word that matched, before the cap.</param>
/// <param name="ObjectCount">How many allomorph and grammatical-info ids the edits resolved to.</param>
public sealed record TouchedWords(IReadOnlyList<string> Words, int Matched, int ObjectCount)
{
    /// <summary>Matched words left out because the cap was reached.</summary>
    public int NotAdded => Matched - Words.Count;
}

/// <summary>Finds a Proposal's touched words from the stored Baseline Assessment and the Baseline's copy of the project.</summary>
public static class TrialTouchedWords
{
    /// <summary>The most touched words one Trial adds to its word list.</summary>
    public const int Cap = 500;

    /// <summary>Resolves the touched words of <paramref name="proposalId"/>.</summary>
    public static CommandOutcome<TouchedWords> Resolve(string projectPath, string productVersion, string proposalId)
    {
        var noEvidence = CommandOutcome<TouchedWords>.Refused(new Refusal(RefusalCodes.TrialTouchedNoBaseline,
            FailureReason.NotFound,
            "--touched reads the stored Baseline Assessment, and none matches the current Baseline. " +
            "Next: run `motif baseline` and `motif assess`, or leave --touched out."));
        return ProjectStoreCommand.Run(projectPath, productVersion, (database, project) =>
        {
            Proposal proposal;
            try
            {
                var repository = new ProposalRepository(database);
                var id = CanonicalId.Parse(ProposalCommands.NormalizeId(proposalId));
                var record = repository.Get(id);
                if (record.DraftName is null) (record, _) = repository.GetFinalized(id);
                proposal = ProposalJsonParser.Parse(JobCommands.CanonicalProposalJson(record));
            }
            catch (Exception exception) when (exception is not ProjectWritingSystemsUnavailableException)
            {
                return CommandOutcome<TouchedWords>.Refused(ProposalCommands.ProposalLoadRefusal(exception));
            }
            var current = CurrentEvidenceQuery.ReadCurrentEvidence(database, project);
            if (!current.Succeeded) return CommandOutcome<TouchedWords>.Refused(current.Refusal!);
            var snapshot = current.Value!;
            if (snapshot.Assessment is not { } assessment || snapshot.Baseline is not { } baseline) return noEvidence;
            var ids = TouchedIds(proposal);
            var morphIds = new HashSet<Guid>();
            var infoIds = new HashSet<Guid>();
            using (var reader = BaselineReadCache.Open(baseline.FwDataPath))
                foreach (var id in ids) Expand(reader.Cache, id, morphIds, infoIds);
            if (morphIds.Count + infoIds.Count == 0)
                return CommandOutcome<TouchedWords>.Refused(new Refusal(RefusalCodes.TrialTouchedNoObjects,
                    FailureReason.NotFound,
                    "The Proposal edits no allomorph, entry or sense that the Baseline holds, so no attested word is touched. " +
                    "Next: name the words to measure with --words and leave --touched out."));
            var matched = assessment.Words.Where(word => word.StoredAnalyses
                .Where(analysis => analysis.StoredAnalysisOpinion != ReadingGrade.Disapproved)
                .SelectMany(analysis => analysis.Morphs).Any(morph =>
                    Has(morphIds, morph.AllomorphId) || Has(infoIds, morph.GrammaticalInfoId)))
                .Select(word => word.Word).Distinct(StringComparer.Ordinal).ToArray();
            return CommandOutcome<TouchedWords>.Success(new TouchedWords(matched.Take(Cap).ToArray(), matched.Length,
                morphIds.Count + infoIds.Count));
        });
    }

    /// <summary>
    /// The existing objects the operations name: each target, the owner a create adds to, and the neighbours an
    /// ordering is placed against. A newly proposed object is not in the Baseline, so it names nothing here.
    /// </summary>
    internal static IReadOnlyList<Guid> TouchedIds(Proposal proposal)
    {
        var ids = new List<Guid>();
        void Add(CanonicalId? id) { if (id is { } value) ids.Add(value.ToGuid()); }
        foreach (var operation in proposal.Operations)
        {
            Add(operation.Target);
            Add(operation.Placement?.After);
            Add(operation.Placement?.Before);
            if (operation.After is { ValueKind: JsonValueKind.Object } after &&
                after.TryGetProperty("owner", out var owner) && owner.ValueKind == JsonValueKind.String &&
                CanonicalId.TryParse(owner.GetString()!, out var ownerId))
                Add(ownerId);
        }
        return ids.Distinct().ToArray();
    }

    private static bool Has(HashSet<Guid> ids, string? text) => text is not null && Guid.TryParse(text, out var id) && ids.Contains(id);

    private static void Expand(LcmCache cache, Guid id, HashSet<Guid> morphs, HashSet<Guid> infos)
    {
        if (!cache.ServiceLocator.ObjectRepository.TryGetObject(id, out var found)) return;
        switch (found)
        {
            case ILexEntry entry: AddEntry(entry, morphs, infos); break;
            case ILexSense sense:
                if (sense.MorphoSyntaxAnalysisRA is { } msa) infos.Add(msa.Guid);
                break;
            case IMoForm form: morphs.Add(form.Guid); break;
            case IMoMorphSynAnalysis analysis: infos.Add(analysis.Guid); break;
            case IPhEnvironment environment:
                foreach (var form in environment.ReferringObjects.OfType<IMoForm>()) morphs.Add(form.Guid);
                break;
        }
    }

    private static void AddEntry(ILexEntry entry, HashSet<Guid> morphs, HashSet<Guid> infos)
    {
        if (entry.LexemeFormOA is { } lexeme) morphs.Add(lexeme.Guid);
        foreach (var alternate in entry.AlternateFormsOS) morphs.Add(alternate.Guid);
        foreach (var msa in entry.MorphoSyntaxAnalysesOC) infos.Add(msa.Guid);
    }
}

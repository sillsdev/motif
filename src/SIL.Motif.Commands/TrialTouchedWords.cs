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

/// <summary>One kind of object a Proposal edits that <c>--touched</c> cannot trace to words.</summary>
/// <param name="Kind">The LibLCM class of the object, or "not in Baseline" when the Baseline holds no such object.</param>
/// <param name="Count">How many of the Proposal's targets are of this kind.</param>
public sealed record UnexpandedTarget(string Kind, int Count);

/// <summary>
/// The attested words a Proposal's edits could affect: those whose stored Baseline analyses use an allomorph or
/// grammatical info the Proposal edits, retires or places an alternate beside. Every match is by exact id.
/// </summary>
/// <param name="Words">The matched words, in Baseline order, at most <see cref="TrialTouchedWords.Cap"/>.</param>
/// <param name="Matched">Every word that matched, before the cap.</param>
/// <param name="ObjectCount">How many allomorph and grammatical-info ids the edits resolved to.</param>
public sealed record TouchedWords(IReadOnlyList<string> Words, int Matched, int ObjectCount)
{
    /// <summary>Targets the match could not follow to words, by kind; each is a way the touched set may be incomplete.</summary>
    public IReadOnlyList<UnexpandedTarget> Unexpanded { get; init; } = [];

    /// <summary>How many words the Baseline Assessment holds, which is every word the match could find.</summary>
    public int BaselineWords { get; init; }

    /// <summary>How many words the project's default Selection holds, or null when it is not known.</summary>
    public int? AttestedWords { get; init; }

    /// <summary>Matched words left out because the cap was reached.</summary>
    public int NotAdded => Matched - Words.Count;

    /// <summary>The one-line account the CLI prints on stderr and the MCP tool returns as its next step.</summary>
    public string Describe()
    {
        var text = new System.Text.StringBuilder("touched: added " + Words.Count + " word(s) that use " + ObjectCount + " edited object(s)");
        if (NotAdded > 0) text.Append("; " + NotAdded + " more matched and were not added (cap " + TrialTouchedWords.Cap + ")");
        text.Append("; matched against " + BaselineWords + " Baseline word(s)");
        if (AttestedWords is { } attested && BaselineWords < attested) text.Append(" of " + attested + " attested");
        if (Unexpanded.Count > 0)
            text.Append("; not traced to words: " + string.Join(", ", Unexpanded.Select(target => target.Kind + " x" + target.Count)) +
                ". The touched set may be incomplete; add --words for words these affect");
        return text.ToString();
    }
}

/// <summary>Finds a Proposal's touched words from the stored Baseline Assessment and the Baseline's copy of the project.</summary>
public static class TrialTouchedWords
{
    /// <summary>The most touched words one Trial adds to its word list.</summary>
    public const int Cap = 500;

    private static CommandOutcome<TouchedWords> NoBaseline() => CommandOutcome<TouchedWords>.Refused(new Refusal(
        RefusalCodes.TrialTouchedNoBaseline, FailureReason.NotFound,
        "--touched reads the stored Baseline Assessment, and none matches the current Baseline. " +
        "Next: run `motif baseline` and `motif assess`, or leave --touched out."));

    /// <summary>Resolves the touched words of <paramref name="proposalId"/>.</summary>
    public static CommandOutcome<TouchedWords> Resolve(string projectPath, string productVersion, string proposalId) =>
        ProjectStoreCommand.Run(projectPath, productVersion, (database, project) =>
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
            if (snapshot.Assessment is not { } assessment || snapshot.Baseline is not { } baseline) return NoBaseline();
            using var reader = BaselineReadCache.Open(baseline.FwDataPath);
            return Find(proposal, assessment.Words, snapshot.Selection?.Selection.Words.Count, reader.Cache);
        });

    /// <summary>
    /// Follows the Proposal's targets through <paramref name="cache"/> to allomorph and grammatical-info ids, then
    /// matches <paramref name="words"/> (null when no Baseline Assessment matches) against them.
    /// </summary>
    internal static CommandOutcome<TouchedWords> Find(Proposal proposal, IReadOnlyList<AssessmentWordResult>? words,
        int? attestedWords, LcmCache? cache)
    {
        if (words is null) return NoBaseline();
        var morphIds = new HashSet<Guid>();
        var infoIds = new HashSet<Guid>();
        var unexpanded = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var id in TouchedIds(proposal))
            if (Expand(cache!, id, morphIds, infoIds) is { } kind) unexpanded[kind] = unexpanded.GetValueOrDefault(kind) + 1;
        var kinds = unexpanded.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new UnexpandedTarget(pair.Key, pair.Value)).ToArray();
        if (morphIds.Count + infoIds.Count == 0)
            return CommandOutcome<TouchedWords>.Refused(new Refusal(RefusalCodes.TrialTouchedNoObjects,
                FailureReason.NotFound,
                "The Proposal edits no allomorph, entry or sense that the Baseline holds, so no attested word is touched" +
                (kinds.Length == 0 ? "" : " (not traced to words: " + string.Join(", ", kinds.Select(target => target.Kind + " x" + target.Count)) + ")") +
                ". Next: name the words to measure with --words and leave --touched out."));
        var matched = Match(words, morphIds, infoIds);
        return CommandOutcome<TouchedWords>.Success(new TouchedWords(matched.Take(Cap).ToArray(), matched.Length,
            morphIds.Count + infoIds.Count) { Unexpanded = kinds, BaselineWords = words.Count, AttestedWords = attestedWords });
    }

    /// <summary>The words with a non-Disapproved stored analysis that uses one of the ids, in order, once each.</summary>
    internal static string[] Match(IEnumerable<AssessmentWordResult> words, HashSet<Guid> morphIds, HashSet<Guid> infoIds) =>
        [.. words.Where(word => word.StoredAnalyses
            .Where(analysis => analysis.StoredAnalysisOpinion != ReadingGrade.Disapproved)
            .SelectMany(analysis => analysis.Morphs).Any(morph =>
                Has(morphIds, morph.AllomorphId) || Has(infoIds, morph.GrammaticalInfoId)))
            .Select(word => word.Word).Distinct(StringComparer.Ordinal)];

    /// <summary>The caller's words first and in their order, then the touched words not already among them.</summary>
    internal static string[] Merge(IEnumerable<string> callerWords, IEnumerable<string> touched) =>
        [.. callerWords.Concat(touched).Distinct(StringComparer.Ordinal)];

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

    /// <summary>Adds the ids the object stands for; returns the object's kind when it cannot be followed to words.</summary>
    internal static string? Expand(LcmCache cache, Guid id, HashSet<Guid> morphs, HashSet<Guid> infos)
    {
        if (!cache.ServiceLocator.ObjectRepository.TryGetObject(id, out var found)) return "not in Baseline";
        switch (found)
        {
            case ILexEntry entry: AddEntry(entry, morphs, infos); return null;
            case ILexSense sense:
                if (sense.MorphoSyntaxAnalysisRA is { } msa) infos.Add(msa.Guid);
                return null;
            case IMoForm form: morphs.Add(form.Guid); return null;
            case IMoMorphSynAnalysis analysis: infos.Add(analysis.Guid); return null;
            case IPhEnvironment environment:
                foreach (var form in environment.ReferringObjects.OfType<IMoForm>()) morphs.Add(form.Guid);
                return null;
            default: return found.ClassName;
        }
    }

    private static void AddEntry(ILexEntry entry, HashSet<Guid> morphs, HashSet<Guid> infos)
    {
        if (entry.LexemeFormOA is { } lexeme) morphs.Add(lexeme.Guid);
        foreach (var alternate in entry.AlternateFormsOS) morphs.Add(alternate.Guid);
        foreach (var msa in entry.MorphoSyntaxAnalysesOC) infos.Add(msa.Guid);
    }
}

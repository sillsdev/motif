using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Runner.Resolution;
using SIL.LCModel;

namespace SIL.Motif.Host.Analysis;

/// <summary>One action a person collected for a word and a chosen analysis.</summary>
/// <param name="Kind">The operation kind to compose.</param>
/// <param name="WordformId">The wordform that owns the analysis.</param>
/// <param name="Reading">The selected parser reading, when the action uses one.</param>
/// <param name="StoredAnalysisId">The exact stored analysis selected by the person.</param>
/// <param name="ChangeId">The pending change identity carried into operation metadata.</param>
public sealed record AnalysisChangeIntent(string Kind, CanonicalId WordformId, ParseAnalysis? Reading,
    CanonicalId? StoredAnalysisId = null, string? ChangeId = null);

/// <summary>Actions that a collected word change can request.</summary>
public static class AnalysisChangeKinds
{
    /// <summary>Approve the selected analysis.</summary>
    public const string Approve = "approve";
    /// <summary>Disapprove the selected analysis.</summary>
    public const string Reject = "reject";
    /// <summary>Return the selected analysis to Unknown.</summary>
    public const string Candidate = "candidate";
    /// <summary>Add the selected parser reading as Unknown.</summary>
    public const string AddCandidate = "add-candidate";
    /// <summary>Mark the wordform spelling as incorrect.</summary>
    public const string IncorrectSpelling = "incorrect-spelling";
}

/// <summary>Composes a collected word change into closed analysis operations against a live project.</summary>
public static class AnalysisChangeComposer
{
    /// <summary>Composes one collected analysis decision into closed operations for the live project.</summary>
    /// <param name="cache">The caller-owned project cache.</param>
    /// <param name="intent">The selected decision and its analysis identity.</param>
    /// <returns>The operations that implement the decision.</returns>
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, AnalysisChangeIntent intent)
    {
        var wordform = Resolve<IWfiWordform>(cache, intent.WordformId);
        if (intent.Kind == AnalysisChangeKinds.IncorrectSpelling)
            return [new OperationEnvelope(CanonicalId.Mint(), WfiWordformSpellingStatusOperationKinds.SetSpellingStatus,
                target: intent.WordformId, after: JsonSerializer.SerializeToElement(new { value = 2 }),
                extensions: ChangeExtension(intent.ChangeId))];

        var reading = intent.Reading ?? throw new InvalidOperationException(
            $"'{intent.Kind}' requires an Assessment with a parser reading for this word.");
        if (reading.Morphs.Count == 0)
            throw new InvalidOperationException("A parser reading requires at least one morph.");
        var existing = wordform.AnalysesOC.Where(analysis => Matches(analysis, reading) &&
            (intent.StoredAnalysisId is null || analysis.Guid == intent.StoredAnalysisId.Value.ToGuid())).ToArray();
        if (intent.StoredAnalysisId is not null && existing.Length != 1)
            throw new InvalidOperationException("The chosen stored analysis does not match the reading.");
        if (intent.Kind is AnalysisChangeKinds.Approve or AnalysisChangeKinds.Reject or AnalysisChangeKinds.Candidate &&
            existing.Length > 1)
            throw new InvalidOperationException("Several stored analyses match this reading. Choose one by its stored identity.");

        if (intent.Kind == AnalysisChangeKinds.Candidate)
        {
            if (existing.Length == 0)
                throw new InvalidOperationException("The parser reading has no stored analysis to return to candidate.");
            return existing.Select(analysis => (Analysis: analysis,
                    Opinion: analysis.GetAgentOpinion(cache.LangProject.DefaultUserAgent)))
                .Where(item => item.Opinion != Opinions.noopinion)
                .Select(item => OpinionOperation(item.Analysis, item.Opinion == Opinions.approves, intent.ChangeId))
                .ToArray();
        }

        if (intent.Kind == AnalysisChangeKinds.AddCandidate && existing.Length > 0)
            throw new InvalidOperationException("The parser reading already has a stored analysis.");
        if (intent.Kind is not (AnalysisChangeKinds.Approve or AnalysisChangeKinds.Reject or AnalysisChangeKinds.AddCandidate))
            throw new InvalidOperationException($"Unknown collected change kind '{intent.Kind}'.");

        var operations = new List<OperationEnvelope>();
        CanonicalId analysisId;
        CanonicalId? creationId = null;
        if (existing.Length == 0)
        {
            foreach (var morph in reading.Morphs)
            {
                if (morph.Form is { } form)
                    Resolve<IMoForm>(cache, FromGuid(form));
                if (morph.Msa is { } msa)
                    Resolve<IMoMorphSynAnalysis>(cache, FromGuid(msa));
                if (morph.InflType is { } infl)
                    Resolve<ILexEntryInflType>(cache, FromGuid(infl));
            }
            creationId = CanonicalId.Mint();
            analysisId = CanonicalId.Mint();
            operations.Add(new OperationEnvelope(creationId.Value, WfiAnalysisOperationKinds.CreateAnalysis,
                entityId: analysisId, target: intent.WordformId, after: MorphPayload(reading),
                extensions: ChangeExtension(intent.ChangeId)));
        }
        else
            analysisId = CanonicalId.FromGuid(existing[0].Guid);

        if (intent.Kind != AnalysisChangeKinds.AddCandidate)
        {
            IEnumerable<CanonicalId> targets = creationId is null ? existing.Select(analysis => CanonicalId.FromGuid(analysis.Guid)) :
                [analysisId];
            foreach (var target in targets)
                operations.Add(new OperationEnvelope(CanonicalId.Mint(), WfiAnalysisOperationKinds.AddRefEvaluations,
                    target: target,
                    after: JsonSerializer.SerializeToElement(new
                {
                    member = intent.Kind == AnalysisChangeKinds.Approve ? HumanEvaluationPayload.Approves : HumanEvaluationPayload.Disapproves,
                }),
                dependsOn: creationId is { } prerequisite ? [new OperationDependency(prerequisite)] : [],
                extensions: ChangeExtension(intent.ChangeId)));
        }
        return operations;
    }

    private static OperationEnvelope OpinionOperation(IWfiAnalysis analysis, bool approves, string? changeId) =>
        new(CanonicalId.Mint(), WfiAnalysisOperationKinds.RemoveRefEvaluations,
            target: CanonicalId.FromGuid(analysis.Guid),
            after: JsonSerializer.SerializeToElement(new
            {
                member = approves ? HumanEvaluationPayload.Approves : HumanEvaluationPayload.Disapproves,
            }), extensions: ChangeExtension(changeId));

    private static JsonElement? ChangeExtension(string? changeId) => changeId is null
        ? null : JsonSerializer.SerializeToElement(new { changeId });

    /// <summary>Tests whether a live stored analysis has the selected parser reading's morphology.</summary>
    /// <param name="analysis">The stored analysis in the caller-owned project.</param>
    /// <param name="reading">The parser reading to compare.</param>
    /// <returns>Whether the ordered morph identities and guessed forms match.</returns>
    public static bool Matches(IWfiAnalysis analysis, ParseAnalysis reading) =>
        AnalysisMorphologyMatcher.Matches(reading, new ApprovedMorphology(analysis.MorphBundlesOS
            .Select(bundle => new ApprovedMorph(bundle.MorphRA?.Guid.ToString("D"), bundle.MsaRA?.Guid.ToString("D"),
                bundle.InflTypeRA?.Guid.ToString("D"), bundle.Form.AvailableWritingSystemIds
                    .Select(ws => bundle.Form.get_String(ws)?.Text)
                    .Where(text => text is not null).Cast<string>().ToArray()))
            .ToArray()));

    private static TRef Resolve<TRef>(LcmCache cache, CanonicalId id) where TRef : ICmObject
    {
        var resolved = CanonicalIdResolver.Resolve(cache, id);
        if (resolved is TRef typed) return typed;
        var expectedName = typeof(TRef).Name.StartsWith("I", StringComparison.Ordinal)
            ? typeof(TRef).Name[1..] : typeof(TRef).Name;
        throw new InvalidOperationException(
            $"'{nameof(AnalysisChangeComposer)}' operation: referenced object '{id.Value}' is not a {expectedName} " +
            $"(it is a {resolved.GetType().Name}).");
    }

    private static CanonicalId FromGuid(string value) => CanonicalId.FromGuid(Guid.Parse(value));

    private static JsonElement MorphPayload(ParseAnalysis reading)
    {
        var morphs = reading.Morphs.Select(morph =>
        {
            var fields = new Dictionary<string, string>();
            if (morph.Form is { } form) fields["form"] = FromGuid(form).Value;
            if (morph.Msa is { } msa) fields["msa"] = FromGuid(msa).Value;
            if (morph.InflType is { } infl) fields["inflType"] = FromGuid(infl).Value;
            if (morph.GuessedString is { } guessed) fields["guessedString"] = guessed;
            return fields;
        }).ToArray();
        return JsonSerializer.SerializeToElement(new { morphs });
    }
}

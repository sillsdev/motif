using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Operations;
using SIL.LCModel;

namespace SIL.Motif.Runner.Composers;

/// <summary>One action a person collected for a word's first parser reading.</summary>
public sealed record AnalysisChangeIntent(string Kind, CanonicalId WordformId, ParseAnalysis? Reading);

/// <summary>Composes a collected word change into closed analysis operations against a live project.</summary>
public static class AnalysisChangeComposer
{
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, AnalysisChangeIntent intent)
    {
        var wordform = ReferenceFieldLowering.Resolve<IWfiWordform>(cache, intent.WordformId, nameof(AnalysisChangeComposer));
        if (intent.Kind == "incorrect-spelling")
            return [new OperationEnvelope(CanonicalId.Mint(), WfiWordformSpellingStatusOperationKinds.SetSpellingStatus,
                target: intent.WordformId, after: JsonSerializer.SerializeToElement(new { value = 2 }))];

        var reading = intent.Reading ?? throw new InvalidOperationException(
            $"'{intent.Kind}' requires an Assessment with a parser reading for this word.");
        if (reading.Morphs.Count == 0)
            throw new InvalidOperationException("A parser reading requires at least one morph.");
        var existing = wordform.AnalysesOC.Where(analysis => Matches(analysis, reading)).ToArray();

        if (intent.Kind == "candidate")
        {
            if (existing.Length == 0)
                throw new InvalidOperationException("The parser reading has no stored analysis to return to candidate.");
            return existing.Select(analysis => (Analysis: analysis,
                    Opinion: analysis.GetAgentOpinion(cache.LangProject.DefaultUserAgent)))
                .Where(item => item.Opinion != Opinions.noopinion)
                .Select(item => OpinionOperation(item.Analysis, item.Opinion == Opinions.approves))
                .ToArray();
        }

        if (intent.Kind == "add-candidate" && existing.Length > 0)
            throw new InvalidOperationException("The parser reading already has a stored analysis.");
        if (intent.Kind is not ("approve" or "reject" or "add-candidate"))
            throw new InvalidOperationException($"Unknown collected change kind '{intent.Kind}'.");

        var operations = new List<OperationEnvelope>();
        CanonicalId analysisId;
        CanonicalId? creationId = null;
        if (existing.Length == 0)
        {
            foreach (var morph in reading.Morphs)
            {
                if (morph.Form is { } form)
                    ReferenceFieldLowering.Resolve<IMoForm>(cache, FromGuid(form), nameof(AnalysisChangeComposer));
                if (morph.Msa is { } msa)
                    ReferenceFieldLowering.Resolve<IMoMorphSynAnalysis>(cache, FromGuid(msa), nameof(AnalysisChangeComposer));
                if (morph.InflType is { } infl)
                    ReferenceFieldLowering.Resolve<ILexEntryInflType>(cache, FromGuid(infl), nameof(AnalysisChangeComposer));
            }
            creationId = CanonicalId.Mint();
            analysisId = CanonicalId.Mint();
            operations.Add(new OperationEnvelope(creationId.Value, WfiAnalysisOperationKinds.CreateAnalysis,
                entityId: analysisId, target: intent.WordformId, after: MorphPayload(reading)));
        }
        else
            analysisId = CanonicalId.FromGuid(existing[0].Guid);

        if (intent.Kind != "add-candidate")
        {
            IEnumerable<CanonicalId> targets = creationId is null ? existing.Select(analysis => CanonicalId.FromGuid(analysis.Guid)) :
                [analysisId];
            foreach (var target in targets)
                operations.Add(new OperationEnvelope(CanonicalId.Mint(), WfiAnalysisOperationKinds.AddRefEvaluations,
                    target: target,
                    after: JsonSerializer.SerializeToElement(new
                {
                    member = intent.Kind == "approve" ? HumanEvaluationPayload.Approves : HumanEvaluationPayload.Disapproves,
                }),
                dependsOn: creationId is { } prerequisite ? [new OperationDependency(prerequisite)] : []));
        }
        return operations;
    }

    private static OperationEnvelope OpinionOperation(IWfiAnalysis analysis, bool approves) =>
        new(CanonicalId.Mint(), WfiAnalysisOperationKinds.RemoveRefEvaluations,
            target: CanonicalId.FromGuid(analysis.Guid),
            after: JsonSerializer.SerializeToElement(new
            {
                member = approves ? HumanEvaluationPayload.Approves : HumanEvaluationPayload.Disapproves,
            }));

    public static bool Matches(IWfiAnalysis analysis, ParseAnalysis reading) =>
        analysis.MorphBundlesOS.Count == reading.Morphs.Count &&
        analysis.MorphBundlesOS.Zip(reading.Morphs).All(pair =>
            MatchesGuid(pair.First.MorphRA?.Guid, pair.Second.Form) &&
            MatchesGuid(pair.First.MsaRA?.Guid, pair.Second.Msa) &&
            MatchesGuid(pair.First.InflTypeRA?.Guid, pair.Second.InflType) &&
            (pair.Second.GuessedString is null ||
             pair.First.Form.AvailableWritingSystemIds.Any(ws =>
                 string.Equals(pair.First.Form.get_String(ws)?.Text?.Normalize(System.Text.NormalizationForm.FormD),
                     pair.Second.GuessedString.Normalize(System.Text.NormalizationForm.FormD), StringComparison.Ordinal))));

    private static bool MatchesGuid(Guid? existing, string? candidate) =>
        existing is null ? candidate is null : Guid.TryParse(candidate, out var parsed) && existing == parsed;

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

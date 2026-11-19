using System.Text;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Runner.Resolution;

namespace SIL.Motif.Host.Analysis;

/// <summary>
/// Lowers one human decision about an existing analysis or wordform through <see cref="AnalysisChangeComposer"/>,
/// the same lowering a collected word change uses, so the two never diverge.
/// </summary>
public static class EditAnalysisDecisionComposer
{
    /// <summary>Resolves the identities against the project and returns the operations that record the decision.</summary>
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, EditAnalysisDecisionIntent intent)
    {
        if (intent.Decision == AnalysisDecisions.Misspelled)
        {
            var wordform = intent.Wordform!.Value;
            if (CanonicalIdResolver.Resolve(cache, wordform) is not IWfiWordform)
                throw new InvalidOperationException(
                    $"'EditAnalysisDecision': '{wordform.Value}' is not an existing wordform.");
            if (intent.IntendedForm is null && intent.Note is null)
                return AnalysisChangeComposer.Build(cache,
                    new AnalysisChangeIntent(AnalysisChangeKinds.IncorrectSpelling, wordform, null));
            return [Misspelled(cache, intent, wordform)];
        }

        if (intent.Decision == AnalysisDecisions.ClearMisspelled)
        {
            var wordform = intent.Wordform!.Value;
            if (CanonicalIdResolver.Resolve(cache, wordform) is not IWfiWordform marked)
                throw new InvalidOperationException(
                    $"'EditAnalysisDecision': '{wordform.Value}' is not an existing wordform.");
            if (marked.SpellingStatus != IncorrectStatus)
                throw new InvalidOperationException(
                    $"'EditAnalysisDecision': wordform '{wordform.Value}' is not marked misspelled, so there is no mark to clear.");
            var detail = new Dictionary<string, string>();
            if (intent.Note is { } clearNote) detail["note"] = clearNote;
            var evidence = DecisionEvidence.Create("undecided", null, detail, null);
            return [new OperationEnvelope(CanonicalId.Mint(), WfiWordformSpellingStatusOperationKinds.ClearSpellingStatus,
                target: wordform, after: JsonSerializer.SerializeToElement(new { }),
                provenance: [evidence.ToProvenanceEntry()])];
        }

        if (intent.Decision == AnalysisDecisions.AddReading)
        {
            var target = intent.Wordform!.Value;
            if (CanonicalIdResolver.Resolve(cache, target) is not IWfiWordform)
                throw new InvalidOperationException(
                    $"'EditAnalysisDecision': '{target.Value}' is not an existing wordform.");
            var reading = new ParseAnalysis(intent.Morphs!.Select(morph => new ParseMorph(
                morph.Form?.ToGuid().ToString("D"), morph.Msa?.ToGuid().ToString("D"),
                morph.InflType?.ToGuid().ToString("D"), morph.GuessedString)).ToArray());
            return AnalysisChangeComposer.Build(cache, new AnalysisChangeIntent(
                AnalysisChangeKinds.AddCandidate, target, reading));
        }

        var analysisId = intent.Analysis!.Value;
        if (CanonicalIdResolver.Resolve(cache, analysisId) is not IWfiAnalysis analysis ||
            analysis.Owner is not IWfiWordform owner)
            throw new InvalidOperationException(
                $"'EditAnalysisDecision': '{analysisId.Value}' is not a stored analysis of a wordform.");
        var kind = intent.Decision switch
        {
            AnalysisDecisions.Approve => AnalysisChangeKinds.Approve,
            AnalysisDecisions.Disapprove => AnalysisChangeKinds.Reject,
            _ => AnalysisChangeKinds.Candidate,
        };
        var operations = AnalysisChangeComposer.Build(cache, new AnalysisChangeIntent(
            kind, CanonicalId.FromGuid(owner.Guid), null, analysisId));
        return operations.Count > 0 ? operations : throw new InvalidOperationException(
            $"'EditAnalysisDecision': analysis '{analysisId.Value}' has no decision to return to unknown.");
    }

    private const int IncorrectStatus = 2;

    // A word-level misspelling: the status, with the intended form and note as the operation's evidence.
    private static OperationEnvelope Misspelled(LcmCache cache, EditAnalysisDecisionIntent intent, CanonicalId wordformId)
    {
        if (intent.IntendedForm is not { } intended)
            throw new InvalidOperationException(
                "'EditAnalysisDecision': a misspelled note needs the intended form, as 'intendedForm' with 'form' and 'writingSystem'.");
        var wordform = (IWfiWordform)CanonicalIdResolver.Resolve(cache, wordformId)!;
        if (!cache.ServiceLocator.WritingSystemManager.TryGet(intended.WritingSystem, out var writingSystem))
            throw new InvalidOperationException(
                $"'EditAnalysisDecision': '{intended.WritingSystem}' is not a writing system of this project.");
        var form = intended.Form.Normalize(NormalizationForm.FormD);
        if (string.IsNullOrWhiteSpace(form))
            throw new InvalidOperationException("'EditAnalysisDecision': the intended form must not be blank.");
        var current = wordform.Form.get_String(writingSystem.Handle).Text?.Normalize(NormalizationForm.FormD);
        if (string.Equals(current, form, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"'EditAnalysisDecision': the intended form '{intended.Form}' is the wordform's own spelling, so nothing is misspelled.");
        var detail = new Dictionary<string, string>
        {
            ["intendedForm"] = form, ["intendedFormWritingSystem"] = writingSystem.Id,
        };
        if (intent.Note is { } note) detail["note"] = note;
        var evidence = DecisionEvidence.Create("misspelled", null, detail, null);
        return new OperationEnvelope(CanonicalId.Mint(), WfiWordformSpellingStatusOperationKinds.SetSpellingStatus,
            target: wordformId, after: JsonSerializer.SerializeToElement(new { value = IncorrectStatus }),
            provenance: [evidence.ToProvenanceEntry()]);
    }
}

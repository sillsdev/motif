using System.Text.Json;
using System.Text;
using SIL.LCModel;
using SIL.Motif.Commands.Parsimony;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Retirement;
using SIL.Motif.Model.DryRun;
using SIL.Motif.Projection.Retirement;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Commands.Retirement;

internal static class RetirementReviewEvidenceCapture
{
    private static readonly JsonSerializerOptions DraftOptions = new(JsonSerializerDefaults.Web);

    internal static ReplaceListedAllomorphsWithRuleIntent? FindRetirementComposition(
        DraftDocument draft, Proposal proposal)
    {
        var authored = new List<ReplaceListedAllomorphsWithRuleIntent>();
        foreach (var provenance in draft.ComposerProvenance)
        {
            if (provenance.ValueKind != JsonValueKind.Object ||
                !provenance.TryGetProperty("composer", out var composer) ||
                composer.ValueKind != JsonValueKind.String || composer.GetString() != "RetireAllomorph" ||
                !provenance.TryGetProperty("input", out var input) || input.ValueKind != JsonValueKind.Object ||
                !input.TryGetProperty("format", out var format) || format.GetString() != "motif-allomorph-retirement")
                continue;
            authored.Add(AllomorphRetirementCodec.Parse(input.GetRawText()));
        }
        if (authored.Count == 0) return null;
        if (authored.Count != 1)
            throw new InvalidDataException("A Draft with several rule-retirement compositions is ambiguous.");

        if (!StringComparer.Ordinal.Equals(draft.ProposalId, proposal.ProposalId.Value))
            throw new InvalidDataException("The retirement Draft and Proposal name different identities.");
        return authored[0];
    }

    internal static RetirementExpectationTranslation? CaptureTranslation(string draftJson, Proposal proposal,
        BaselineToken baseline, LcmCache source, DryRun dryRun, ParsimonyScopeBinding scope)
    {
        var draft = JsonSerializer.Deserialize<DraftDocument>(draftJson, DraftOptions)
            ?? throw new InvalidDataException("The Draft content is empty.");
        var currentProposal = ProposalJsonParser.Parse(ProposalCommands.BuildProposalJson(draft));
        if (currentProposal.ProposalId != proposal.ProposalId ||
            IntentDigest.Compute(currentProposal) != IntentDigest.Compute(proposal))
            throw new InvalidDataException("The retirement Draft changed after its Dry Run was recorded.");
        var retirement = FindRetirementComposition(draft, proposal);
        if (retirement is null) return null;

        var retiredIds = retirement.Retirements.SelectMany(item => item.RetiredForms)
            .Select(item => CanonicalId.Parse(item.Id).ToGuid()).Distinct().ToArray();
        var footprint = AllomorphReferenceFootprintReader.Read(source, retiredIds);
        var selectionWords = scope.Selection is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : scope.Selection.ResolvedWords.ToHashSet(StringComparer.Ordinal);
        var selectionCases = new HashSet<string>(StringComparer.Ordinal);
        if (selectionWords.Count > 0)
        {
            foreach (var wordform in footprint.AffectedWordforms)
            foreach (var form in wordform.Forms)
            {
                var surface = form.Text.Normalize(NormalizationForm.FormD);
                if (selectionWords.Contains(surface))
                    selectionCases.Add(FrozenExpectationCapture.CaseKey(
                        CanonicalId.FromGuid(wordform.Wordform).Value, form.WritingSystem, surface));
            }
        }

        var frozen = FrozenExpectationCapture.Capture(source, baseline,
            footprint.AffectedWordforms.Select(item => item.Wordform).Distinct().ToArray(), selectionCases,
            new HashSet<string>(StringComparer.Ordinal));
        return RetirementExpectationTranslator.Translate(frozen, baseline, retirement.Retirements,
            proposal, dryRun);
    }
}

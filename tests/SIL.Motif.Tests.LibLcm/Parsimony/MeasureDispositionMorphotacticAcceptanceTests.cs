using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Parsimony;

public sealed partial class MorphotacticRecipeAcceptanceTests
{
    [ParsimonyFactsFact]
    public async Task DuplicateProhibitionDispositionRoutesUseTheActualMeasureFinding()
    {
        string projectPath;
        using (var cache = pristine.NewScratch())
        {
            var fixture = MakeAdhocSlotOrder(cache);
            var original = Resolve<IMoMorphAdhocProhib>(cache, fixture.FirstProhibition);
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var duplicate = cache.ServiceLocator.GetInstance<IMoMorphAdhocProhibFactory>().Create();
                cache.LangProject.MorphologicalDataOA.AdhocCoProhibitionsOC.Add(duplicate);
                duplicate.FirstMorphemeRA = original.FirstMorphemeRA;
                duplicate.Adjacency = original.Adjacency;
                foreach (var target in original.RestOfMorphsRS) duplicate.RestOfMorphsRS.Add(target);
            });
            projectPath = cache.ProjectId.Path;
            new FwDataProjectLoader().Save(cache);
        }

        await ParsimonyMeasureDispositionAcceptance.AssertEveryNoEditRouteAsync(projectPath,
            Path.Combine(_root, "adhoc-duplicate-dispositions"), "P-adhoc-duplicate",
            "Both prohibitions retain distinct linguistic explanations that are not yet captured in grouped facts.",
            "Which wording should remain for these two rules, and do their descriptions carry different linguistic explanations?",
            "Defer until the full ordered targets and grouped rationale for both prohibitions are available.");
    }

    [RealParserFact]
    public async Task ReviewedNegativeDispositionRoutesUseTheActualMeasureFinding()
    {
        string projectPath;
        IReadOnlyList<string> words;
        using (var cache = pristine.NewScratch())
        {
            var fixture = MakeReviewedNegativeOrder(cache);
            var recordType = AddNotebookRecordType(cache, "Reviewed negative");
            ConfirmSurfaceNegative(cache, recordType, "katn");
            words = fixture.Words;
            projectPath = cache.ProjectId.Path;
            new FwDataProjectLoader().Save(cache);
        }

        await ParsimonyMeasureDispositionAcceptance.AssertEveryNoEditRouteAsync(projectPath,
            Path.Combine(_root, "reviewed-negative-dispositions"), "R-word-negative-accepted",
            "The reversed order is valid in a distinct use; revise the negative judgment separately and leave the grammar unchanged.",
            "The Approved sequence is kant, while the reviewed negative katn has the reversed A/B order. Is katn impossible in this category, or valid in another use?",
            "Defer until the reviewed-negative revision, matching positive order, and complete parser case can be compared.", words);
    }

    [ParsimonyFactsFact]
    public async Task TemplatePrecedenceDispositionRoutesUseTheActualMeasureFinding()
    {
        string projectPath;
        using (var cache = pristine.NewScratch())
        {
            _ = MakeTemplatePrecedence(cache, includeAlternative: true);
            projectPath = cache.ProjectId.Path;
            new FwDataProjectLoader().Save(cache);
        }

        await ParsimonyMeasureDispositionAcceptance.AssertEveryNoEditRouteAsync(projectPath,
            Path.Combine(_root, "template-precedence-dispositions"), "R-tmpl-precedence",
            "Both slot orders are Approved; keep the current order while the alternative template scope is unresolved.",
            "The cited Approved sequence has B before A while this template requires A before B. Should the template reverse, or do these forms use a separate template?",
            "Defer until every applicable template embedding and the exact Approved sequence attribution are complete.");
    }

    [ParsimonyFactsFact]
    public async Task SlotBlockingDispositionRoutesUseTheActualMeasureFinding()
    {
        string projectPath;
        string requiredSlot;
        using (var cache = pristine.NewScratch())
        {
            var fixture = MakeRequiredSlot(cache);
            requiredSlot = fixture.RequiredSlot;
            projectPath = cache.ProjectId.Path;
            new FwDataProjectLoader().Save(cache);
        }

        await ParsimonyMeasureDispositionAcceptance.AssertEveryNoEditRouteAsync(projectPath,
            Path.Combine(_root, "slot-blocking-dispositions"), "R-slot-blocking",
            "The required slot carries a meaningful marker, so its obligation remains in place.",
            "The Approved form kan leaves the required slot empty. May every listed user omit it, or only this paradigm?",
            "Defer until the one-slot parser comparison and all shared slot users are available.",
            findingIdentity: requiredSlot);
    }

    [ParsimonyFactsFact]
    public async Task UnslottedAffixDispositionRoutesUseTheActualMeasureFinding()
    {
        string projectPath;
        using (var cache = pristine.NewScratch())
        {
            _ = MakeAffixPosition(cache);
            projectPath = cache.ProjectId.Path;
            new FwDataProjectLoader().Save(cache);
        }

        await ParsimonyMeasureDispositionAcceptance.AssertEveryNoEditRouteAsync(projectPath,
            Path.Combine(_root, "unslotted-affix-dispositions"), "B-affix-unslotted",
            "The affix intentionally remains outside this template while its position is only partially ordered.",
            "The cited order is A before B. Is that order required, and does A share a position with another affix?",
            "Defer until the root, category, and additional Approved sequence evidence support a position choice.");
    }

    [ParsimonyFactsFact]
    public async Task AdhocSlotOrderDispositionRoutesUseTheActualMeasureFinding()
    {
        string projectPath;
        using (var cache = pristine.NewScratch())
        {
            _ = MakeAdhocSlotOrder(cache, dispositionFindingFixture: true);
            projectPath = cache.ProjectId.Path;
            new FwDataProjectLoader().Save(cache);
        }

        await ParsimonyMeasureDispositionAcceptance.AssertEveryNoEditRouteAsync(projectPath,
            Path.Combine(_root, "adhoc-slot-order-dispositions"), "B-adhoc-is-slot-order",
            "These rules express immediate adjacency, which a broader precedence rule would overstate.",
            "These rules forbid reversed A/B order. Does A always precede B when another affix intervenes, or is only immediate adjacency forbidden?",
            "Defer until grouped ad hoc provenance, including rule members and rationale, is complete.",
            findingIdPrefix: "B-adhoc-is-slot-order:adjacency:");
    }
}

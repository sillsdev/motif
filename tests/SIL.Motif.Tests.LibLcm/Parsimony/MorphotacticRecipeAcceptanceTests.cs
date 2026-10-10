using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Composers;
using SIL.Motif.Commands.Parsimony;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Retirement;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.LiveHost.Baselines;
using SIL.Motif.LiveHost.HumanJudgments;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Model.DryRun;
using SIL.Motif.Runner.DryRun;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Worker.Assess;
using SIL.Motif.Worker.Store;
using SIL.Motif.Projection.HumanJudgments;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Tests.Composers;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.Parsimony;

[Collection(LcmCacheParallelCollections.Group3)]
[Trait("MotifTestLevel", "System")]
public sealed partial class MorphotacticRecipeAcceptanceTests(
    PristineProjectFixture pristine, ITestOutputHelper output)
{
    private const string ProductVersion = "0.1.0";
    private const int Anywhere = 0;
    private const int SomewhereToLeft = 1;
    private const int AdjacentToLeft = 3;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-morphotactic-acceptance-" +
        Guid.NewGuid().ToString("N"));

    [RealParserFact]
    public async Task AdhocDuplicateDisablingOneExactDuplicateKeepsCompletedParseSetsUnchanged()
    {
        using var cache = pristine.NewScratch();
        var fixture = MakeAdhocSlotOrder(cache);
        var original = Resolve<IMoMorphAdhocProhib>(cache, fixture.FirstProhibition);
        IMoMorphAdhocProhib duplicate = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            duplicate = cache.ServiceLocator.GetInstance<IMoMorphAdhocProhibFactory>().Create();
            cache.LangProject.MorphologicalDataOA.AdhocCoProhibitionsOC.Add(duplicate);
            duplicate.FirstMorphemeRA = original.FirstMorphemeRA;
            duplicate.Adjacency = original.Adjacency;
            foreach (var target in original.RestOfMorphsRS) duplicate.RestOfMorphsRS.Add(target);
        });
        var duplicateId = CanonicalId.FromGuid(duplicate.Guid).Value;

        var result = await RunProposalAsync(cache, fixture.Wordforms, fixture.Words, "adhoc-duplicate-disable",
            (path, draft) => ComposeDisableProhibition(path, draft, duplicateId),
            RecipeVerificationCriteria.StructuralCleanup);

        Assert.Single(result.Proposal.Operations);
        Assert.Equal(MoAdhocProhibDisabledOperationKinds.SetDisabled, result.Proposal.Operations[0].Kind);
        Assert.Equal(duplicateId, result.Proposal.Operations[0].Target!.Value.Value);
        Assert.Single(result.DryRun.ExpectedEffects,
            effect => effect.Field == SnapshotFields.MoAdhocProhibDisabled);
        Assert.Equal(RecipeVerificationStatus.Pass, result.Verification.Status);
        Assert.Equal(RecipeVerificationCriteria.StructuralCleanup, result.Verification.Criteria);
        Assert.Equal(0, result.Verification.ChangedAnalysisCases);
        Assert.Equal(0, result.Verification.LostApprovedReadings);
        Assert.Equal(0, result.Verification.NewlyAcceptedNegativeCases);

        using var after = new FwDataProjectLoader().LoadScratchCache(result.AfterProjectPath);
        Assert.False(Resolve<IMoMorphAdhocProhib>(after, fixture.FirstProhibition).Disabled);
        Assert.True(Resolve<IMoMorphAdhocProhib>(after, duplicateId).Disabled);
    }

    [RealParserFact]
    public async Task ReviewedNegativeOrderEditingTheWrongOrderRejectsAReviewedNegativeAndPreservesApprovedForms()
    {
        using var cache = pristine.NewScratch();
        var fixture = MakeReviewedNegativeOrder(cache);
        var recordType = AddNotebookRecordType(cache, "ReviewedNegativeOrder reviewed negative");
        ConfirmSurfaceNegative(cache, recordType, "katn");

        var result = await RunProposalAsync(cache, fixture.Wordforms, fixture.Words, "reviewed-negative-order-reject",
            (path, draft) => ComposeEditTemplate(path, draft, fixture.Template,
                fixture.SecondSlot, fixture.FirstSlot, reverse: true));

        Assert.Equal(RecipeVerificationStatus.Pass, result.Verification.Status);
        Assert.Equal(0, result.Verification.LostApprovedReadings);
        Assert.Equal(0, result.Verification.NewlyAcceptedNegativeCases);
        var negative = Assert.Single(result.Verification.Negatives, item => item.Target == "surface");
        Assert.True(negative.BeforeComplete);
        Assert.True(negative.AfterComplete);
        Assert.True(negative.BeforeAccepted);
        Assert.False(negative.AfterAccepted);
        Assert.False(negative.NewlyAccepted);
        Assert.All(result.Verification.Readings, reading =>
            Assert.Equal(RecipeVerificationReadingStatus.Preserved, reading.Status));

        using var after = new FwDataProjectLoader().LoadScratchCache(result.AfterProjectPath);
        var template = Resolve<IMoInflAffixTemplate>(after, fixture.Template);
        Assert.Equal(new[] { fixture.FirstSlot, fixture.SecondSlot },
            template.SuffixSlotsRS.Select(slot => CanonicalId.FromGuid(slot.Guid).Value));
    }

    [RealParserFact]
    public async Task RequiredSlotOptionalSlotProposalRecoversExactReadingWithoutLossesOrNewNegativeAcceptances()
    {
        using var cache = pristine.NewScratch();
        var fixture = MakeRequiredSlot(cache);
        var result = await RunProposalAsync(cache, fixture.Wordforms, fixture.Words, "required-slot-optional",
            (path, draft) => ComposeEditSlot(path, draft, fixture.RequiredSlot));

        Assert.Single(result.Proposal.Operations);
        Assert.Contains(result.Proposal.Operations,
            operation => operation.Kind.EndsWith("/setOptional", StringComparison.Ordinal));
        Assert.Single(result.DryRun.ExpectedEffects);
        Assert.Equal(SnapshotFields.MoInflAffixSlotOptional,
            Assert.Single(result.DryRun.ExpectedEffects).Field);
        var target = Assert.Single(result.Verification.Readings,
            item => item.ReadingId == fixture.TargetReading);
        Assert.Equal(RecipeVerificationReadingStatus.Recovered, target.Status);
        Assert.True(target.BeforeComplete);
        Assert.True(target.AfterComplete);
        Assert.Equal(0, result.Verification.LostApprovedReadings);
        Assert.Equal(0, result.Verification.NewlyAcceptedNegativeCases);
        Assert.False(result.Verification.NegativeEvidenceGap);
        Assert.Equal(RecipeVerificationStatus.Pass, result.Verification.Status);
        Assert.True(CanRecommendOptionality(result.Verification, target));

        using var after = new FwDataProjectLoader().LoadScratchCache(result.AfterProjectPath);
        var requiredSlot = Resolve<IMoInflAffixSlot>(after, fixture.RequiredSlot);
        Assert.True(requiredSlot.Optional);
        output.WriteLine("exact Approved reading recovered; 0 Approved readings lost; 0 reviewed negatives newly accepted.");
        output.WriteLine("Shared users: Parent template (Parent category); Child template (Child subcategory).");
        output.WriteLine("Optionality recommendation: supported by the paired parser result.");
    }

    [RealParserFact]
    public async Task TemplatePrecedenceEditAffixTemplateRestoresTheAttestedOrderAndPreservesOtherApprovedReading()
    {
        using var cache = pristine.NewScratch();
        var fixture = MakeTemplatePrecedence(cache, includeAlternative: false);
        var result = await RunProposalAsync(cache, fixture.Wordforms, fixture.Words, "template-precedence-reorder",
            (path, draft) => ComposeEditTemplate(path, draft, fixture.Template, fixture.FirstSlot,
                fixture.SecondSlot, reverse: true));

        Assert.Single(result.Proposal.Operations);
        Assert.Single(result.DryRun.ExpectedEffects);
        Assert.Equal(RecipeVerificationStatus.Pass, result.Verification.Status);
        Assert.Equal(RecipeVerificationReadingStatus.Recovered,
            Assert.Single(result.Verification.Readings, item => item.ReadingId == fixture.TargetReading).Status);
        Assert.Contains(result.Verification.Readings,
            item => item.ReadingId == fixture.ControlReading &&
                item.Status == RecipeVerificationReadingStatus.Preserved);
        Assert.Equal(0, result.Verification.LostApprovedReadings);
        Assert.Equal(0, result.Verification.NewlyAcceptedNegativeCases);
        using var after = new FwDataProjectLoader().LoadScratchCache(result.AfterProjectPath);
        Assert.Equal(new[] { fixture.SecondSlot, fixture.FirstSlot },
            Resolve<IMoInflAffixTemplate>(after, fixture.Template).SuffixSlotsRS
                .Select(slot => CanonicalId.FromGuid(slot.Guid).Value));
        AssertDoubleFillRejected(result.Verification);
        output.WriteLine("EditAffixTemplate recovered the attested reverse order and preserved the independent Approved reading.");
        output.WriteLine("The reviewed double-fill reading remained rejected; no swap label was supplied.");
    }

    [RealParserFact]
    public async Task TemplatePrecedenceAlternativeTemplateKeepsBothApprovedOrders()
    {
        using var cache = pristine.NewScratch();
        var fixture = MakeTemplatePrecedence(cache, includeAlternative: true);
        var result = await RunProposalAsync(cache, fixture.Wordforms, fixture.Words, "template-precedence-alternative",
            (path, draft) => ComposeAlternativeTemplate(path, draft, fixture.Category,
                fixture.FirstSlot, fixture.SecondSlot));

        Assert.Contains(result.Proposal.Operations,
            operation => operation.Kind.EndsWith("/createAffixTemplates", StringComparison.Ordinal));
        Assert.Equal(RecipeVerificationStatus.Pass, result.Verification.Status);
        Assert.Equal(RecipeVerificationReadingStatus.Recovered,
            Assert.Single(result.Verification.Readings, item => item.ReadingId == fixture.TargetReading).Status);
        Assert.Equal(RecipeVerificationReadingStatus.Preserved,
            Assert.Single(result.Verification.Readings, item => item.ReadingId == fixture.ControlReading).Status);
        Assert.Equal(0, result.Verification.LostApprovedReadings);
        Assert.Equal(0, result.Verification.NewlyAcceptedNegativeCases);
        AssertDoubleFillRejected(result.Verification);
        using var after = new FwDataProjectLoader().LoadScratchCache(result.AfterProjectPath);
        var category = Resolve<IPartOfSpeech>(after, fixture.Category);
        Assert.Contains(category.AffixTemplatesOS, template =>
            template.SuffixSlotsRS.Select(slot => CanonicalId.FromGuid(slot.Guid).Value)
                .SequenceEqual(new[] { fixture.SecondSlot, fixture.FirstSlot }));
        output.WriteLine("explicit alternative recovered the reverse order and preserved the original Approved order.");
        output.WriteLine("The reviewed double-fill reading remained rejected; no swap label was supplied.");
    }

    [RealParserFact]
    public async Task AffixPositionExistingSlotAssignmentPreservesTheTargetAndLeavesNoCandidateUnslotted()
    {
        using var cache = pristine.NewScratch();
        var fixture = MakeAffixPosition(cache);
        Assert.Empty(Resolve<IMoInflAffMsa>(cache, fixture.Msa).SlotsRC);
        var result = await RunProposalAsync(cache, fixture.Wordforms, fixture.Words, "affix-position-assign-existing-slot",
            (path, draft) => ComposeAffixAssignment(path, draft, fixture.Msa, fixture.Slot));

        Assert.Single(result.Proposal.Operations);
        Assert.Single(result.DryRun.ExpectedEffects);
        Assert.Equal(RecipeVerificationStatus.Pass, result.Verification.Status);
        Assert.Equal(RecipeVerificationReadingStatus.Preserved,
            Assert.Single(result.Verification.Readings, item => item.ReadingId == fixture.TargetReading).Status);
        Assert.Equal(RecipeVerificationReadingStatus.Preserved,
            Assert.Single(result.Verification.Readings, item => item.ReadingId == fixture.ControlReading).Status);
        Assert.Equal(0, result.Verification.LostApprovedReadings);
        Assert.Equal(0, result.Verification.NewlyAcceptedNegativeCases);
        using var after = new FwDataProjectLoader().LoadScratchCache(result.AfterProjectPath);
        var msa = Resolve<IMoInflAffMsa>(after, fixture.Msa);
        Assert.Equal(new[] { fixture.Slot }, msa.SlotsRC.Select(slot => CanonicalId.FromGuid(slot.Guid).Value));
        AssertDoubleFillRejected(result.Verification);
        output.WriteLine("existing-slot assignment preserved the parsed Approved reading; candidate unslotted count=1→0.");
        output.WriteLine("The reviewed double-fill reading remained rejected; no swap label was supplied.");
    }

    [RealParserFact]
    public async Task AdhocSlotOrderTemplateOrderReplacesTwoProhibitionsAndKeepsReviewedSwapsRejected()
    {
        using var cache = pristine.NewScratch();
        var fixture = MakeAdhocSlotOrder(cache);
        var result = await RunProposalAsync(cache, fixture.Wordforms, fixture.Words, "adhoc-slot-order-replacement",
            (path, draft) => ComposeAdhocSlotOrderReplacement(path, draft, fixture));

        Assert.Equal(3, result.Proposal.Operations.Count);
        var disabledRules = result.Proposal.Operations.Where(operation =>
            operation.Kind == MoAdhocProhibDisabledOperationKinds.SetDisabled).ToArray();
        Assert.Equal(fixture.ReplacementProhibitions.Order(StringComparer.Ordinal),
            disabledRules.Select(operation => operation.Target.GetValueOrDefault().Value)
                .Order(StringComparer.Ordinal));
        Assert.Single(result.Proposal.Operations,
            operation => operation.Kind.EndsWith("/moveSuffixSlots", StringComparison.Ordinal));
        Assert.Equal(3, result.DryRun.ExpectedEffects.Count);
        Assert.Equal(0, result.Verification.LostApprovedReadings);
        Assert.Equal(0, result.Verification.NewlyAcceptedNegativeCases);
        Assert.False(result.Verification.NegativeEvidenceGap);
        Assert.Equal(RecipeVerificationStatus.Pass, result.Verification.Status);
        foreach (var readingId in fixture.ApprovedReadings)
        {
            var reading = Assert.Single(result.Verification.Readings, item => item.ReadingId == readingId);
            Assert.Equal(RecipeVerificationReadingStatus.Preserved, reading.Status);
            Assert.True(reading.BeforeComplete);
            Assert.True(reading.AfterComplete);
        }
        foreach (var word in fixture.ForbiddenSwaps)
        {
            var frozenCase = Assert.Single(result.Frozen.Cases, item => item.Surface == word);
            Assert.Contains(frozenCase.Readings, item => item.Opinion == "disapproved");
            var caseId = frozenCase.CaseId;
            var negative = Assert.Single(result.Verification.Negatives, item => item.CaseId == caseId);
            Assert.True(negative.IdentityAvailable);
            Assert.True(negative.BeforeComplete);
            Assert.True(negative.AfterComplete);
            Assert.False(negative.BeforeAccepted);
            Assert.False(negative.AfterAccepted);
            Assert.False(negative.NewlyAccepted);
        }

        using var after = new FwDataProjectLoader().LoadScratchCache(result.AfterProjectPath);
        var reverseTemplate = Resolve<IMoInflAffixTemplate>(after, fixture.ReverseTemplate);
        Assert.Equal(new[] { fixture.FirstSlot, fixture.SecondSlot },
            reverseTemplate.SuffixSlotsRS.Select(slot => CanonicalId.FromGuid(slot.Guid).Value));
        foreach (var ruleId in fixture.ReplacementProhibitions)
            Assert.True(Resolve<IMoMorphAdhocProhib>(after, ruleId).Disabled);
        foreach (var control in fixture.KeptControls)
        {
            var rule = Resolve<IMoMorphAdhocProhib>(after, control.Guid);
            Assert.False(rule.Disabled);
            Assert.Equal(control.Adjacency, rule.Adjacency);
            Assert.Equal(control.Primary, CanonicalId.FromGuid(rule.FirstMorphemeRA.Guid).Value);
            Assert.Equal(control.Targets, rule.RestOfMorphsRS
                .Select(target => CanonicalId.FromGuid(target.Guid).Value));
        }

        output.WriteLine("EditAffixTemplate plus two EditAdhocProhibition actions preserved every Approved reading.");
        output.WriteLine("Both reviewed swaps stayed rejected; adjacency, Anywhere, and multi-target controls stayed active.");
    }

    [RealParserFact]
    public async Task NaturalClassEditRemovesAnIndependentlyReviewedLeakAndListsEverySharedUser()
    {
        using var cache = pristine.NewScratch();
        var fixture = MakeNaturalClass(cache);
        ConfirmSurfaceNegative(cache, fixture.RecordType, "tma");
        var result = await RunProposalAsync(cache, fixture.Wordforms, fixture.Words, "natural-class-edit-reviewed-leak",
            (path, draft) => ProposalCommands.ComposeEditNaturalClass(new ComposeEditNaturalClassRequest(
                path, ProductVersion, draft, JsonSerializer.Serialize(new
                {
                    target = fixture.SourceClass.Value,
                    expectedMembers = fixture.InitialMembers.Select(item => item.Value),
                    members = fixture.ProductiveMembers.Select(item => item.Value),
                }))));

        Assert.Equal(RecipeVerificationStatus.Pass, result.Verification.Status);
        Assert.Equal(0, result.Verification.LostApprovedReadings);
        Assert.Equal(0, result.Verification.NewlyAcceptedNegativeCases);
        Assert.Contains(result.Proposal.Operations,
            operation => operation.Kind == PhNCSegmentsSegmentsOperationKinds.RemoveRefSegments);
        Assert.Equal(1, result.Proposal.Operations.Count(operation =>
            operation.Kind == PhNCSegmentsSegmentsOperationKinds.RemoveRefSegments));
        var negative = Assert.Single(result.Verification.Negatives, item => item.Target == "surface");
        Assert.True(negative.BeforeComplete);
        Assert.True(negative.AfterComplete);
        Assert.True(negative.BeforeAccepted);
        Assert.False(negative.AfterAccepted);
        Assert.All(fixture.ProductiveReadings, reading => Assert.Contains(result.Verification.Readings,
            item => item.ReadingId == reading && item.Status == RecipeVerificationReadingStatus.Preserved));
        AssertSharedUsers(result.Composed.RelatedObjects, fixture);

        using var after = new FwDataProjectLoader().LoadScratchCache(result.AfterProjectPath);
        var corrected = Resolve<IPhNCSegments>(after, fixture.SourceClass.Value);
        Assert.Equal(fixture.ProductiveMembers.OrderBy(item => item.Value, StringComparer.Ordinal),
            corrected.SegmentsRC.Select(item => CanonicalId.FromGuid(item.Guid))
                .OrderBy(item => item.Value, StringComparer.Ordinal));
        output.WriteLine("The reviewed tma negative stopped parsing; Approved p/b/m readings and the shared b-context user remained.");
        output.WriteLine("The Draft lists every environment and rewrite rule that uses the edited class.");
    }

    [RealParserFact]
    public async Task NaturalClassDistinctClassMeaningRelinksOnlyTheSelectedUser()
    {
        using var cache = pristine.NewScratch();
        var fixture = MakeNaturalClass(cache, includeRetainedTControl: true);
        ConfirmSurfaceNegative(cache, fixture.RecordType, "tma");
        CanonicalId replacementClass = default;
        CanonicalId replacementCreation = default;
        var result = await RunProposalAsync(cache, fixture.Wordforms, fixture.Words, "natural-class-relink-distinct-meaning",
            (path, draft) =>
            {
                var created = ProposalCommands.ComposeAuthorNaturalClass(new ComposeAuthorNaturalClassRequest(
                    path, ProductVersion, draft, JsonSerializer.Serialize(new
                    {
                        name = "Selected bilabial triggers",
                        abbreviation = "BIL",
                        members = fixture.ProductiveMembers.Select(item => item.Value),
                    })));
                Assert.True(created.Succeeded, created.Refusal?.Message);
                var creation = Assert.Single(created.Value!.Operations,
                    operation => operation.Kind == PhPhonDataNaturalClassesOperationKinds.Create);
                replacementClass = CanonicalId.Parse(creation.EntityId!);
                replacementCreation = CanonicalId.Parse(creation.OperationId);
                return ProposalCommands.ComposeRelinkNaturalClass(new ComposeRelinkNaturalClassRequest(
                    path, ProductVersion, draft, JsonSerializer.Serialize(new
                    {
                        source = fixture.SourceClass.Value,
                        replacement = replacementClass.Value,
                        replacementCreationOperation = replacementCreation.Value,
                        environments = Array.Empty<object>(),
                        ruleContexts = new[] { fixture.TargetRuleContext.Value },
                    })));
            });

        Assert.Equal(RecipeVerificationStatus.Pass, result.Verification.Status);
        Assert.Equal(0, result.Verification.LostApprovedReadings);
        Assert.Equal(0, result.Verification.NewlyAcceptedNegativeCases);
        Assert.Contains(result.Proposal.Operations,
            operation => operation.Kind == PhSimpleContextNCFeatureStructureOperationKinds.SetFeatureStructure &&
                operation.Target == fixture.TargetRuleContext);
        Assert.DoesNotContain(result.Proposal.Operations,
            operation => operation.Kind == PhSimpleContextNCFeatureStructureOperationKinds.SetFeatureStructure &&
                operation.Target == fixture.RetainedRuleContext);
        Assert.All(fixture.ProductiveReadings, reading => Assert.Contains(result.Verification.Readings,
            item => item.ReadingId == reading && item.Status == RecipeVerificationReadingStatus.Preserved));
        Assert.Contains(result.Verification.Readings,
            item => item.ReadingId == fixture.RetainedTReading &&
                item.Status == RecipeVerificationReadingStatus.Preserved);
        var negative = Assert.Single(result.Verification.Negatives, item => item.Target == "surface");
        Assert.True(negative.BeforeAccepted);
        Assert.False(negative.AfterAccepted);
        AssertSharedUsers(result.Composed.RelatedObjects, fixture);

        using var after = new FwDataProjectLoader().LoadScratchCache(result.AfterProjectPath);
        var retained = Resolve<IPhSimpleContextNC>(after, fixture.RetainedRuleContext.Value);
        Assert.Equal(fixture.SourceClass.ToGuid(), retained.FeatureStructureRA!.Guid);
        var environment = Resolve<IPhEnvironment>(after, fixture.SharedEnvironment.Value);
        Assert.Contains("[CTX]", environment.StringRepresentation.Text, StringComparison.Ordinal);
        output.WriteLine("The selected rule uses the narrower class; the other rule and its t-context reading remain on the source class.");
        output.WriteLine("The reviewed tma negative stopped parsing while Approved readings were preserved.");
    }

    [RealParserFact]
    public async Task NaturalClassProductiveClassKeepRecordsReasonWithoutChangingTheGrammar()
    {
        using var cache = pristine.NewScratch();
        var fixture = MakeNaturalClass(cache, ["p", "b", "m"]);
        ConfirmSurfaceNegative(cache, fixture.RecordType, "tma");
        const string reason = "Held-out b and m forms parse, so the class remains productive beyond p.";
        var result = await RunProposalAsync(cache, fixture.Wordforms, fixture.Words, "natural-class-keep-productive-class",
            (path, draft) => ProposalCommands.ComposeRecordParsimonyDisposition(
                new ComposeRecordParsimonyDispositionRequest(path, ProductVersion, draft,
                    JsonSerializer.Serialize(new
                    {
                        recordTypeId = fixture.RecordType.Value,
                        measureId = "R-nc-excess",
                        subject = new
                        {
                            kind = "object",
                            @object = new { @class = "PhNCSegments", id = fixture.SourceClass.Value },
                        },
                        disposition = "keep",
                        evidenceDigest = "sha256:" + new string('a', 64),
                        evidenceContract = "natural-class-excess/v1",
                        subjectCaption = "bilabial contexts",
                        measureCaption = "Class contains extra segments",
                        reason,
                    }))));

        Assert.Equal(RecipeVerificationStatus.Pass, result.Verification.Status);
        Assert.Equal(0, result.Verification.LostApprovedReadings);
        Assert.Equal(0, result.Verification.NewlyAcceptedNegativeCases);
        Assert.DoesNotContain(result.Proposal.Operations, operation =>
            operation.Kind.StartsWith("grammar/phNC", StringComparison.Ordinal));
        Assert.All(fixture.HeldOutReadings, reading => Assert.Contains(result.Verification.Readings,
            item => item.ReadingId == reading && item.Status == RecipeVerificationReadingStatus.Preserved));
        Assert.Single(result.Verification.Negatives, item => item.Target == "surface" &&
            item.BeforeComplete && item.AfterComplete && !item.AfterAccepted);

        using var applied = new FwDataProjectLoader().LoadScratchCache(result.BeforeProjectPath);
        ProposalApplier.Apply(applied, result.Proposal, result.DryRun.Anchor, "linguist");
        var saved = Assert.Single(HumanJudgmentReader.Read(applied).Judgments,
            item => item.Judgment.Body is DispositionJudgment disposition &&
                disposition.MeasureId == "R-nc-excess");
        var keep = Assert.IsType<DispositionJudgment>(saved.Judgment.Body);
        Assert.Equal(ParsimonyDispositionKind.Keep, keep.Disposition);
        Assert.Equal(reason, saved.Judgment.Reason);
        Assert.Equal(fixture.SourceClass.ToGuid(), Resolve<IPhNCSegments>(applied, fixture.SourceClass.Value).Guid);
        output.WriteLine("The B={p,b,m} class kept its held-out b/m readings with a saved Keep reason and no grammar edit.");
    }

    private static void AssertSharedUsers(IReadOnlyList<ComposedRelatedObject>? users, NaturalClassFixture fixture)
    {
        Assert.NotNull(users);
        Assert.Equal(3, users.Count);
        Assert.Contains(users, item => item.Kind == "environment" && item.Id == fixture.SharedEnvironment.Value);
        Assert.Contains(users, item => item.Kind == "phonological-rule" && item.Id == fixture.TargetRuleId.Value);
        Assert.Contains(users, item => item.Kind == "phonological-rule" && item.Id == fixture.RetainedRuleId.Value);
    }

    private async Task<Acceptance> RunProposalAsync(LcmCache cache, IReadOnlyList<Guid> wordforms,
        IReadOnlyList<string> words, string draftName,
        Func<string, string, CommandOutcome<ComposedOperationsResponse>> compose,
        RecipeVerificationCriteria criteria = RecipeVerificationCriteria.Tightening)
    {
        Directory.CreateDirectory(_root);
        var projectPath = cache.ProjectId.Path;
        EnsureReservedField(cache);
        new FwDataProjectLoader().Save(cache);
        var baseline = Token(cache);
        var frozen = FrozenExpectationCapture.Capture(cache, baseline, wordforms, new HashSet<string>(),
            new HashSet<string>());
        Assert.Empty(frozen.Unavailable);
        Assert.True(frozen.ReviewedNegatives.Count > 0 || frozen.Cases.SelectMany(item => item.Readings)
            .Any(item => item.Opinion == "disapproved"));
        cache.Dispose();

        using var invoker = new PanGlossInvoker(PanGlossExecutable.TryLocate());
        var before = await AssessAsync(projectPath, invoker, words, Path.Combine(_root, draftName + "-before"),
            baseline);
        Assert.True(ProposalCommands.New(new(projectPath, ProductVersion, draftName,
            "Verify a morphotactic repair with paired parser runs.")).Succeeded);
        var composed = compose(projectPath, draftName);
        Assert.True(composed.Succeeded, composed.Refusal?.Message);
        Assert.True(ProposalCommands.Label(new(projectPath, ProductVersion, draftName,
            "Morphotactic repair acceptance")).Succeeded);
        Assert.True(ProposalCommands.Comment(new(projectPath, ProductVersion, draftName,
            "The exact Approved readings and reviewed negatives are checked against paired Assessments.")).Succeeded);
        var finalized = ProposalCommands.Finalize(new(projectPath, ProductVersion, draftName));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var loaded = ProjectStoreCommand.Run<Proposal>(projectPath, ProductVersion, (database, _) =>
            CommandOutcome<Proposal>.Success(new ProposalRepository(database)
                .GetFinalized(CanonicalId.Parse(finalized.Value!.ProposalId)).Envelope));
        Assert.True(loaded.Succeeded, loaded.Refusal?.Message);
        var proposal = loaded.Value!;

        var scratchRoot = Path.Combine(_root, draftName + "-dry-run");
        var dryRunCache = new ScratchCacheFactory().CreateFromFileCopy(projectPath, scratchRoot);
        string afterPath;
        DryRun dryRun;
        using (var scratch = DryRunScratch.Adopt(dryRunCache, "F1 recipe acceptance proposal Dry Run"))
        {
            dryRun = ProposalDryRunner.Run(scratch, proposal);
            afterPath = scratch.PeekCache().ProjectId.Path;
            new FwDataProjectLoader().Save(scratch.PeekCache());
        }

        var after = await AssessAsync(afterPath, invoker, words, Path.Combine(_root, draftName + "-after"), baseline);
        var verification = RecipeVerification.Compare(frozen, before, after, criteria);
        return new Acceptance(proposal, dryRun, verification, afterPath, frozen, composed.Value!, projectPath);
    }

    private static void EnsureReservedField(LcmCache cache)
    {
        if (HumanJudgmentFieldDefinition.Inspect(cache).State == HumanJudgmentFieldState.Missing)
            NotebookJudgmentFixture.InitializeReservedField(cache);
    }

    private static CommandOutcome<ComposedOperationsResponse> ComposeEditSlot(string path, string draft,
        string slot) => ProposalCommands.ComposeEditAffixSlot(new ComposeEditAffixSlotRequest(path,
        ProductVersion, draft, JsonSerializer.Serialize(new
        {
            target = slot,
            expectedOptional = false,
            optional = true,
        })));

    private static CommandOutcome<ComposedOperationsResponse> ComposeEditTemplate(string path, string draft,
        string template, string firstSlot, string secondSlot, bool reverse) =>
        ProposalCommands.ComposeEditAffixTemplate(new ComposeEditAffixTemplateRequest(path, ProductVersion, draft,
            JsonSerializer.Serialize(new
            {
                target = template,
                expectedPrefixSlots = Array.Empty<string>(),
                prefixSlots = Array.Empty<string>(),
                expectedSuffixSlots = new[] { firstSlot, secondSlot },
                suffixSlots = reverse ? new[] { secondSlot, firstSlot } : new[] { firstSlot, secondSlot },
            })));

    private static CommandOutcome<ComposedOperationsResponse> ComposeAlternativeTemplate(string path, string draft,
        string category, string firstSlot, string secondSlot) =>
        ProposalCommands.ComposeAuthorAffixTemplate(new ComposeAuthorAffixTemplateRequest(path, ProductVersion,
            draft, JsonSerializer.Serialize(new
            {
                category,
                name = "Reverse order alternative",
                ws = "en",
                prefixSlots = Array.Empty<string>(),
                suffixSlots = new[] { secondSlot, firstSlot },
                final = true,
            })));

    private static CommandOutcome<ComposedOperationsResponse> ComposeAffixAssignment(string path, string draft,
        string msa, string slot) => ProposalCommands.ComposeEditInflectionalAffix(
        new ComposeEditInflectionalAffixRequest(path, ProductVersion, draft,
            JsonSerializer.Serialize(new { target = msa, expectedSlots = Array.Empty<string>(), slots = new[] { slot } })));

    private static CommandOutcome<ComposedOperationsResponse> ComposeDisableProhibition(string path, string draft,
        string prohibition) => ProposalCommands.ComposeEditAdhocProhibition(new ComposeEditAdhocProhibitionRequest(
        path, ProductVersion, draft, JsonSerializer.Serialize(new
        {
            target = prohibition,
            expectedDisabled = false,
            disabled = true,
        })));

    private static CommandOutcome<ComposedOperationsResponse> ComposeAdhocSlotOrderReplacement(string path, string draft,
        AdhocSlotOrderFixture fixture)
    {
        var template = ComposeEditTemplate(path, draft, fixture.ReverseTemplate, fixture.SecondSlot,
            fixture.FirstSlot, reverse: true);
        if (!template.Succeeded) return template;
        CommandOutcome<ComposedOperationsResponse> last = template;
        foreach (var prohibition in fixture.ReplacementProhibitions)
        {
            last = ProposalCommands.ComposeEditAdhocProhibition(new ComposeEditAdhocProhibitionRequest(
                path, ProductVersion, draft, JsonSerializer.Serialize(new
                {
                    target = prohibition,
                    expectedDisabled = false,
                    disabled = true,
                })));
            if (!last.Succeeded) return last;
        }
        return last;
    }

    private async Task<RecipeVerificationRun> AssessAsync(string projectPath, PanGlossInvoker invoker,
        IReadOnlyList<string> words, string artifactRoot, BaselineToken baseline)
    {
        var store = new StatsCacheStore(WorkspaceOwnership.Bootstrap(artifactRoot));
        var assessor = new PanGlossAssessor(store, invoker);
        var produced = await assessor.ProduceAsync(new AssessmentScope(words, [AssessmentKind.ParseTime],
                TimeSpan.FromMilliseconds(1000), new StepCap(200_000)), Path.GetDirectoryName(projectPath)!,
            CancellationToken.None);
        var assessment = Assert.Single(produced);
        try
        {
            var invocation = Assert.IsType<BatchInvocationEvidence>(assessment.Invocation);
            var batch = Assert.IsType<AssessmentRaw.Batch>(assessment.Raw).Analysis;
            var options = new RecipeVerificationOptions(invocation.ExecutableBytesSha256,
                invocation.PerWordTimeoutMs, invocation.PerWordStepLimit, invocation.Threads,
                invocation.CollectStatistics);
            var comparable = new ComparableAssessment(CanonicalId.Mint().Value, PanGlossAssessor.AssessorName,
                AssessmentKind.ParseTime.ToStoredKind(), "pangloss", "0.7.0", ToAssessedWords(batch.Words));
            return new RecipeVerificationRun(comparable, baseline, options, [], words);
        }
        finally
        {
            assessment.ArtifactLease?.Dispose();
        }
    }

    private static IReadOnlyList<AssessedWord> ToAssessedWords(IReadOnlyList<WordAnalysis> words) =>
        words.Select(word => new AssessedWord(word.Word, word.Outcome.ToStoredOutcome(), [], word.ElapsedMs,
            word.Signature) { Morphology = word.Morphology }).ToArray();

    private static RequiredSlotFixture MakeRequiredSlot(LcmCache cache)
    {
        IPartOfSpeech parent = null!;
        IPartOfSpeech child = null!;
        IMoInflAffixSlot required = null!;
        IMoInflAffixSlot optional = null!;
        IMoInflAffixTemplate parentTemplate = null!;
        IMoInflAffixTemplate childTemplate = null!;
        Morph root = null!;
        Morph requiredAffix = null!;
        Morph optionalAffix = null!;
        Guid targetWordform = Guid.Empty;
        Guid targetReading = Guid.Empty;
        Guid controlWordform = Guid.Empty;
        Guid fullWordform = Guid.Empty;
        Guid negativeWordform = Guid.Empty;
        Guid independentWordform = Guid.Empty;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            parent = CreateCategory(cache, "RequiredSlot parent");
            child = CreateCategory(cache, "RequiredSlot child");
            parent.SubPossibilitiesOS.Add(child);
            required = CreateSlot(cache, parent, "Required Number", optional: false);
            optional = CreateSlot(cache, child, "Optional Marker", optional: true);
            parentTemplate = CreateTemplate(cache, parent, "Parent template", [required]);
            childTemplate = CreateTemplate(cache, child, "Child template", [required, optional]);
            Assert.Contains(parentTemplate.SuffixSlotsRS, item => item.Guid == required.Guid);
            Assert.Contains(childTemplate.SuffixSlotsRS, item => item.Guid == required.Guid);
            root = CreateMorph(cache, child, MoMorphTypeTags.kguidMorphStem, "ka", "root", null);
            requiredAffix = CreateMorph(cache, child, MoMorphTypeTags.kguidMorphSuffix, "-t", "required", required);
            optionalAffix = CreateMorph(cache, child, MoMorphTypeTags.kguidMorphSuffix, "-n", "marker", optional);
            targetWordform = AddReading(cache, "kan", "approves", root, optionalAffix);
            targetReading = cache.ServiceLocator.GetInstance<IWfiWordformRepository>().GetObject(targetWordform)
                .AnalysesOC.Single().Guid;
            controlWordform = AddReading(cache, "kat", "approves", root, requiredAffix);
            fullWordform = AddReading(cache, "katn", "approves", root, requiredAffix, optionalAffix);
            negativeWordform = AddReadingWithIdentity(cache, "kann", "disapproves",
                root, optionalAffix, optionalAffix).Wordform;
            independentWordform = AddIndependentControl(cache);
        });
        RealParserProject.PrepareForParsing(cache, "k", "a", "t", "n", "m", "i");
        return new RequiredSlotFixture(CanonicalId.FromGuid(required.Guid).Value,
            CanonicalId.FromGuid(targetReading).Value, [targetWordform, controlWordform, negativeWordform,
                fullWordform, independentWordform], ["kan", "kat", "katn", "kann", "mi"]);
    }

    private static TemplatePrecedenceFixture MakeTemplatePrecedence(LcmCache cache, bool includeAlternative)
    {
        IPartOfSpeech category = null!;
        IMoInflAffixSlot first = null!;
        IMoInflAffixSlot second = null!;
        IMoInflAffixTemplate template = null!;
        Morph root = null!;
        Morph firstAffix = null!;
        Morph secondAffix = null!;
        Guid targetWordform = Guid.Empty;
        Guid targetReading = Guid.Empty;
        Guid controlWordform = Guid.Empty;
        Guid controlReading = Guid.Empty;
        Guid negativeWordform = Guid.Empty;
        Guid independentWordform = Guid.Empty;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            category = CreateCategory(cache, includeAlternative ? "TemplatePrecedence alternative" : "TemplatePrecedence order");
            first = CreateSlot(cache, category, "A");
            second = CreateSlot(cache, category, "B");
            template = CreateTemplate(cache, category, "Attested first order", [first, second]);
            root = CreateMorph(cache, category, MoMorphTypeTags.kguidMorphStem, "ka", "root", null);
            firstAffix = CreateMorph(cache, category, MoMorphTypeTags.kguidMorphSuffix, "-n", "A", first);
            secondAffix = CreateMorph(cache, category, MoMorphTypeTags.kguidMorphSuffix, "-t", "B", second);
            if (includeAlternative)
            {
                controlWordform = AddReading(cache, "kant", "approves", root, firstAffix, secondAffix);
                controlReading = cache.ServiceLocator.GetInstance<IWfiWordformRepository>().GetObject(controlWordform)
                    .AnalysesOC.Single().Guid;
            }
            targetWordform = AddReading(cache, "katn", "approves", root, secondAffix, firstAffix);
            targetReading = cache.ServiceLocator.GetInstance<IWfiWordformRepository>().GetObject(targetWordform)
                .AnalysesOC.Single().Guid;
            negativeWordform = AddReadingWithIdentity(cache, "kann", "disapproves", root,
                firstAffix, firstAffix).Wordform;
            independentWordform = AddIndependentControl(cache);
            if (!includeAlternative)
            {
                controlWordform = independentWordform;
                controlReading = cache.ServiceLocator.GetInstance<IWfiWordformRepository>().GetObject(controlWordform)
                    .AnalysesOC.Single().Guid;
            }
        });
        RealParserProject.PrepareForParsing(cache, "k", "a", "n", "t", "m", "i");
        var words = includeAlternative ? new[] { "kant", "katn", "kann", "mi" } :
            new[] { "katn", "kann", "mi" };
        return new TemplatePrecedenceFixture(CanonicalId.FromGuid(category.Guid).Value,
            CanonicalId.FromGuid(template.Guid).Value, CanonicalId.FromGuid(first.Guid).Value,
            CanonicalId.FromGuid(second.Guid).Value, CanonicalId.FromGuid(targetReading).Value,
            CanonicalId.FromGuid(controlReading).Value,
            includeAlternative ? [targetWordform, controlWordform, negativeWordform, independentWordform] :
                [targetWordform, negativeWordform, controlWordform], words);
    }

    private static ReviewedNegativeOrderFixture MakeReviewedNegativeOrder(LcmCache cache)
    {
        IPartOfSpeech category = null!;
        IMoInflAffixSlot firstSlot = null!;
        IMoInflAffixSlot secondSlot = null!;
        IMoInflAffixTemplate template = null!;
        Morph root = null!;
        Morph firstAffix = null!;
        Morph secondAffix = null!;
        Guid approvedWordform = Guid.Empty;
        Guid negativeWordform = Guid.Empty;
        Guid independentWordform = Guid.Empty;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            category = CreateCategory(cache, "ReviewedNegativeOrder order contrast");
            firstSlot = CreateSlot(cache, category, "A");
            secondSlot = CreateSlot(cache, category, "B");
            _ = CreateTemplate(cache, category, "Attested order", [firstSlot, secondSlot]);
            template = CreateTemplate(cache, category, "Order under review", [secondSlot, firstSlot]);
            root = CreateMorph(cache, category, MoMorphTypeTags.kguidMorphStem, "ka", "root", null);
            firstAffix = CreateMorph(cache, category, MoMorphTypeTags.kguidMorphSuffix, "-n", "A", firstSlot);
            secondAffix = CreateMorph(cache, category, MoMorphTypeTags.kguidMorphSuffix, "-t", "B", secondSlot);
            approvedWordform = AddReading(cache, "kant", "approves", root, firstAffix, secondAffix);
            negativeWordform = AddReading(cache, "katn", "disapproves", root, secondAffix, firstAffix);
            independentWordform = AddIndependentControl(cache);
        });
        RealParserProject.PrepareForParsing(cache, "k", "a", "n", "t", "m", "i");
        return new ReviewedNegativeOrderFixture(CanonicalId.FromGuid(template.Guid).Value,
            CanonicalId.FromGuid(firstSlot.Guid).Value, CanonicalId.FromGuid(secondSlot.Guid).Value,
            [approvedWordform, negativeWordform, independentWordform], ["kant", "katn", "mi"]);
    }

    private static AffixPositionFixture MakeAffixPosition(LcmCache cache)
    {
        IPartOfSpeech category = null!;
        IMoInflAffixSlot slot = null!;
        Morph root = null!;
        Morph affix = null!;
        Guid targetWordform = Guid.Empty;
        Guid targetReading = Guid.Empty;
        Guid negativeWordform = Guid.Empty;
        Guid independentWordform = Guid.Empty;
        Guid msa = Guid.Empty;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            category = CreateCategory(cache, "AffixPosition existing slot");
            slot = CreateSlot(cache, category, "Existing T slot");
            _ = CreateTemplate(cache, category, "Existing template", [slot]);
            root = CreateMorph(cache, category, MoMorphTypeTags.kguidMorphStem, "ka", "root", null);
            affix = CreateMorph(cache, category, MoMorphTypeTags.kguidMorphSuffix, "-t", "T", null);
            msa = affix.Msa.Guid;
            targetWordform = AddReading(cache, "kat", "approves", root, affix);
            targetReading = cache.ServiceLocator.GetInstance<IWfiWordformRepository>().GetObject(targetWordform)
                .AnalysesOC.Single().Guid;
            independentWordform = AddIndependentControl(cache);
            negativeWordform = AddReadingWithIdentity(cache, "katt", "disapproves", root, affix, affix).Wordform;
        });
        RealParserProject.PrepareForParsing(cache, "k", "a", "t", "m", "i");
        var controlReading = cache.ServiceLocator.GetInstance<IWfiWordformRepository>().GetObject(independentWordform)
            .AnalysesOC.Single().Guid;
        return new AffixPositionFixture(CanonicalId.FromGuid(msa).Value, CanonicalId.FromGuid(slot.Guid).Value,
            CanonicalId.FromGuid(targetReading).Value, CanonicalId.FromGuid(controlReading).Value,
            [targetWordform, independentWordform, negativeWordform],
            ["kat", "katt", "mi"]);
    }

    private static NaturalClassFixture MakeNaturalClass(LcmCache cache, IReadOnlyList<string>? classMemberLetters = null,
        bool includeRetainedTControl = false)
    {
        var features = AddNaturalClassParserFeatures(cache);
        var phonemes = new Dictionary<string, CanonicalId>(StringComparer.Ordinal);
        foreach (var letter in new[] { "p", "b", "m", "t", "n", "a", "s", "z" })
        {
            var profile = letter switch
            {
                "p" or "b" => new[] { features.Labial, features.Oral },
                "m" => new[] { features.Labial, features.Nasal },
                "n" => new[] { features.Other, features.Nasal },
                _ => new[] { features.Other, features.Oral },
            };
            phonemes.Add(letter, SoundSystemComposerTests.Compose(cache, [],
                () => AuthorPhonemeComposer.Build(cache, new(letter, [letter], profile))));
        }
        RealParserProject.PrepareForParsing(cache);
        var members = (classMemberLetters ?? ["p", "b", "m", "t"])
            .Select(letter => phonemes[letter]).ToArray();
        var productiveMembers = new[] { phonemes["p"], phonemes["b"], phonemes["m"] };
        var classOperations = AuthorNaturalClassComposer.Build(cache,
            new AuthorNaturalClassIntent("NaturalClass broad contexts", "CTX", members));
        SoundSystemComposerTests.Execute(cache, classOperations);
        var sourceClass = Assert.Single(classOperations,
            operation => operation.Kind == PhPhonDataNaturalClassesOperationKinds.Create).EntityId!.Value;

        var targetRuleOperations = AuthorPhonologicalRuleComposer.Build(cache,
            new AuthorPhonologicalRuleIntent("NaturalClass nasal change", PhonologicalRuleDirection.Simultaneous,
                [new(Phoneme: phonemes["n"])], [new(Phoneme: phonemes["m"])],
                [new(NaturalClass: sourceClass)], [new(Phoneme: phonemes["a"])]));
        SoundSystemComposerTests.Execute(cache, targetRuleOperations);
        var targetRule = Assert.Single(targetRuleOperations,
            operation => operation.Kind == PhPhonDataPhonRulesOperationKinds.Create).EntityId!.Value;
        var targetRuleContext = RuleContextFor(targetRuleOperations, sourceClass);

        var retainedRuleOperations = AuthorPhonologicalRuleComposer.Build(cache,
            new AuthorPhonologicalRuleIntent("NaturalClass sibilant change", PhonologicalRuleDirection.Simultaneous,
                [new(Phoneme: phonemes["s"])], [new(Phoneme: phonemes["z"])],
                [new(NaturalClass: sourceClass)], [new(Phoneme: phonemes["a"])],
                new Placement(targetRule, null)));
        SoundSystemComposerTests.Execute(cache, retainedRuleOperations);
        var retainedRule = Assert.Single(retainedRuleOperations,
            operation => operation.Kind == PhPhonDataPhonRulesOperationKinds.Create).EntityId!.Value;
        var retainedRuleContext = RuleContextFor(retainedRuleOperations, sourceClass);

        var environmentOperations = AuthorEnvironmentComposer.Build(cache,
            new AuthorEnvironmentIntent("NaturalClass shared class user", [new(NaturalClass: sourceClass)], []));
        SoundSystemComposerTests.Execute(cache, environmentOperations);
        var environment = Assert.Single(environmentOperations, operation => operation.EntityId.HasValue)
            .EntityId!.Value;

        Morph tRoot = null!;
        Morph pRoot = null!;
        Morph bRoot = null!;
        Morph mRoot = null!;
        Morph bSibilantRoot = null!;
        Morph tSibilantRoot = null!;
        (Guid Wordform, Guid Analysis) reviewedLeak = default;
        (Guid Wordform, Guid Analysis) pPositive = default;
        (Guid Wordform, Guid Analysis) bPositive = default;
        (Guid Wordform, Guid Analysis) mPositive = default;
        (Guid Wordform, Guid Analysis) bRetained = default;
        (Guid Wordform, Guid Analysis) tRetained = default;
        IPartOfSpeech category = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            category = CreateCategory(cache, "NaturalClass sound class");
            tRoot = CreateMorph(cache, category, MoMorphTypeTags.kguidMorphStem, "tna", "leak", null);
            pRoot = CreateMorph(cache, category, MoMorphTypeTags.kguidMorphStem, "pna", "p-positive", null);
            bRoot = CreateMorph(cache, category, MoMorphTypeTags.kguidMorphStem, "bna", "b-positive", null);
            mRoot = CreateMorph(cache, category, MoMorphTypeTags.kguidMorphStem, "mna", "m-positive", null);
            bSibilantRoot = CreateMorph(cache, category, MoMorphTypeTags.kguidMorphStem, "bsa", "retained-b", null);
            tSibilantRoot = CreateMorph(cache, category, MoMorphTypeTags.kguidMorphStem, "tsa", "retained-t", null);
            reviewedLeak = AddReadingWithIdentity(cache, "tma", "noopinion", tRoot);
            pPositive = AddReadingWithIdentity(cache, "pma", "approves", pRoot);
            bPositive = AddReadingWithIdentity(cache, "bma", "approves", bRoot);
            mPositive = AddReadingWithIdentity(cache, "mma", "approves", mRoot);
            bRetained = AddReadingWithIdentity(cache, "bza", "approves", bSibilantRoot);
            if (includeRetainedTControl)
                tRetained = AddReadingWithIdentity(cache, "tza", "approves", tSibilantRoot);
        });
        var recordType = AddNotebookRecordType(cache, "Parsimony review");
        new FwDataProjectLoader().Save(cache);
        var productiveReadings = new[]
        {
            CanonicalId.FromGuid(pPositive.Analysis).Value,
            CanonicalId.FromGuid(bPositive.Analysis).Value,
            CanonicalId.FromGuid(mPositive.Analysis).Value,
            CanonicalId.FromGuid(bRetained.Analysis).Value,
        };
        var heldOutReadings = new[]
        {
            CanonicalId.FromGuid(bPositive.Analysis).Value,
            CanonicalId.FromGuid(mPositive.Analysis).Value,
        };
        var wordforms = new List<Guid>
        {
            reviewedLeak.Wordform, pPositive.Wordform, bPositive.Wordform, mPositive.Wordform, bRetained.Wordform,
        };
        var words = new List<string> { "tma", "pma", "bma", "mma", "bza" };
        var retainedTReading = includeRetainedTControl ? CanonicalId.FromGuid(tRetained.Analysis).Value : null;
        if (includeRetainedTControl)
        {
            wordforms.Add(tRetained.Wordform);
            words.Add("tza");
        }
        return new NaturalClassFixture(sourceClass, members, productiveMembers, targetRuleContext,
            retainedRuleContext, targetRule, retainedRule, environment, recordType, wordforms.ToArray(),
            words.ToArray(), productiveReadings, heldOutReadings, retainedTReading);
    }

    private static NaturalClassParserFeatures AddNaturalClassParserFeatures(LcmCache cache)
    {
        IFsClosedFeature place = null!;
        IFsClosedFeature manner = null!;
        IFsSymFeatVal labial = null!;
        IFsSymFeatVal other = null!;
        IFsSymFeatVal nasal = null!;
        IFsSymFeatVal oral = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            place = cache.ServiceLocator.GetInstance<IFsClosedFeatureFactory>().Create();
            manner = cache.ServiceLocator.GetInstance<IFsClosedFeatureFactory>().Create();
            cache.LangProject.PhFeatureSystemOA.FeaturesOC.Add(place);
            cache.LangProject.PhFeatureSystemOA.FeaturesOC.Add(manner);
            place.Name.set_String(cache.DefaultAnalWs, "Place");
            manner.Name.set_String(cache.DefaultAnalWs, "Manner");
            labial = cache.ServiceLocator.GetInstance<IFsSymFeatValFactory>().Create();
            other = cache.ServiceLocator.GetInstance<IFsSymFeatValFactory>().Create();
            nasal = cache.ServiceLocator.GetInstance<IFsSymFeatValFactory>().Create();
            oral = cache.ServiceLocator.GetInstance<IFsSymFeatValFactory>().Create();
            place.ValuesOC.Add(labial);
            place.ValuesOC.Add(other);
            manner.ValuesOC.Add(nasal);
            manner.ValuesOC.Add(oral);
            labial.Name.set_String(cache.DefaultAnalWs, "labial");
            other.Name.set_String(cache.DefaultAnalWs, "other");
            nasal.Name.set_String(cache.DefaultAnalWs, "nasal");
            oral.Name.set_String(cache.DefaultAnalWs, "oral");
        });
        return new NaturalClassParserFeatures(
            new(CanonicalId.FromGuid(place.Guid), CanonicalId.FromGuid(labial.Guid)),
            new(CanonicalId.FromGuid(place.Guid), CanonicalId.FromGuid(other.Guid)),
            new(CanonicalId.FromGuid(manner.Guid), CanonicalId.FromGuid(nasal.Guid)),
            new(CanonicalId.FromGuid(manner.Guid), CanonicalId.FromGuid(oral.Guid)));
    }

    private sealed record NaturalClassParserFeatures(PhonologicalFeatureValue Labial, PhonologicalFeatureValue Other,
        PhonologicalFeatureValue Nasal, PhonologicalFeatureValue Oral);

    private static CanonicalId RuleContextFor(IReadOnlyList<OperationEnvelope> operations, CanonicalId naturalClass) =>
        Assert.Single(operations, operation =>
            operation.Kind == PhSimpleContextNCFeatureStructureOperationKinds.SetFeatureStructure &&
            operation.After!.Value.GetProperty("ref").GetString() == naturalClass.Value).Target!.Value;

    private static void ConfirmSurfaceNegative(LcmCache cache, CanonicalId recordType, string form)
    {
        NotebookJudgmentFixture.InitializeReservedField(cache);
        var caseId = CanonicalId.Mint().Value;
        var intent = new RecordReviewedNegativeIntent(recordType, caseId,
            cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs), form, "standard dialect",
            new SurfaceNegativeTarget(), Reason: "An independent contrast confirms that this surface is rejected.");
        var proposalId = CanonicalId.Mint();
        var operations = RecordReviewedNegativeComposer.Build(cache, intent, proposalId);
        var versions = operations.Select(operation => OperationKind.GetGroup(operation.Kind))
            .Distinct(StringComparer.Ordinal).ToDictionary(group => group, _ => "1.0", StringComparer.Ordinal);
        var proposal = new Proposal(versions, proposalId, null, operations);
        var dryRun = ScratchDryRun.Of(cache, proposal);
        ProposalApplier.Apply(cache, proposal, dryRun.Anchor, "linguist");
        Assert.Contains(HumanJudgmentReader.Read(cache).Judgments,
            judgment => judgment.Judgment.Body is ReviewedNegativeJudgment negative && negative.CaseId == caseId);
    }

    private static CanonicalId AddNotebookRecordType(LcmCache cache, string name)
    {
        ICmPossibility type = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var notebook = cache.LangProject.ResearchNotebookOA;
            var list = notebook.RecTypesOA ?? cache.ServiceLocator.GetInstance<ICmPossibilityListFactory>().Create();
            notebook.RecTypesOA = list;
            type = cache.ServiceLocator.GetInstance<ICmPossibilityFactory>().Create();
            list.PossibilitiesOS.Add(type);
            type.Name.set_String(cache.DefaultAnalWs, TsStringUtils.MakeString(name, cache.DefaultAnalWs));
        });
        return CanonicalId.FromGuid(type.Guid);
    }

    private static AdhocSlotOrderFixture MakeAdhocSlotOrder(LcmCache cache, bool dispositionFindingFixture = false)
    {
        IPartOfSpeech category = null!;
        IMoInflAffixSlot firstSlot = null!;
        IMoInflAffixSlot secondSlot = null!;
        IMoInflAffixTemplate? reverseTemplate = null;
        Morph root = null!;
        Morph first = null!;
        Morph second = null!;
        Morph third = null!;
        Morph fourth = null!;
        Morph adjacencyPrimary = null!;
        Morph adjacencyOther = null!;
        Morph anywherePrimary = null!;
        Morph anywhereOther = null!;
        Morph multiPrimary = null!;
        Morph multiOtherOne = null!;
        Morph multiOtherTwo = null!;
        IMoMorphAdhocProhib firstProhibition = null!;
        IMoMorphAdhocProhib secondProhibition = null!;
        IMoMorphAdhocProhib adjacencyControl = null!;
        IMoMorphAdhocProhib anywhereControl = null!;
        IMoMorphAdhocProhib multiTargetControl = null!;
        Guid firstWordform = Guid.Empty;
        Guid secondWordform = Guid.Empty;
        Guid firstReading = Guid.Empty;
        Guid secondReading = Guid.Empty;
        Guid firstNegativeWordform = Guid.Empty;
        Guid secondNegativeWordform = Guid.Empty;
        Guid independentWordform = Guid.Empty;

        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            category = CreateCategory(cache, "AdhocSlotOrder order replacement");
            firstSlot = CreateSlot(cache, category, "A position");
            secondSlot = CreateSlot(cache, category, "B position");
            _ = CreateTemplate(cache, category, "Confirmed order", [firstSlot, secondSlot]);
            if (!dispositionFindingFixture)
                reverseTemplate = CreateTemplate(cache, category, "Alternative under review", [secondSlot, firstSlot]);
            root = CreateMorph(cache, category, MoMorphTypeTags.kguidMorphStem, "ka", "root", null);
            first = CreateMorph(cache, category, MoMorphTypeTags.kguidMorphSuffix, "-n", "A", firstSlot);
            second = CreateMorph(cache, category, MoMorphTypeTags.kguidMorphSuffix, "-t", "B", secondSlot);
            third = CreateMorph(cache, category, MoMorphTypeTags.kguidMorphSuffix, "-m", "C", firstSlot);
            fourth = CreateMorph(cache, category, MoMorphTypeTags.kguidMorphSuffix, "-i", "D", secondSlot);
            adjacencyPrimary = CreateMorph(cache, category, MoMorphTypeTags.kguidMorphSuffix, "-p", "E", firstSlot);
            adjacencyOther = CreateMorph(cache, category, MoMorphTypeTags.kguidMorphSuffix, "-q", "F", secondSlot);
            anywherePrimary = CreateMorph(cache, category, MoMorphTypeTags.kguidMorphSuffix, "-e", "G", firstSlot);
            anywhereOther = CreateMorph(cache, category, MoMorphTypeTags.kguidMorphSuffix, "-r", "H", secondSlot);
            multiPrimary = CreateMorph(cache, category, MoMorphTypeTags.kguidMorphSuffix, "-u", "I", firstSlot);
            multiOtherOne = CreateMorph(cache, category, MoMorphTypeTags.kguidMorphSuffix, "-v", "J", secondSlot);
            multiOtherTwo = CreateMorph(cache, category, MoMorphTypeTags.kguidMorphSuffix, "-w", "K", secondSlot);

            firstProhibition = CreateMorphemeProhibition(cache, first, SomewhereToLeft, second);
            secondProhibition = CreateMorphemeProhibition(cache, third, SomewhereToLeft, fourth);
            adjacencyControl = CreateMorphemeProhibition(cache, adjacencyPrimary, AdjacentToLeft, adjacencyOther);
            anywhereControl = CreateMorphemeProhibition(cache, anywherePrimary, Anywhere, anywhereOther);
            multiTargetControl = CreateMorphemeProhibition(cache, multiPrimary, SomewhereToLeft,
                multiOtherOne, multiOtherTwo);

            (firstWordform, firstReading) = AddReadingWithIdentity(cache, "kant", "approves", root, first, second);
            (secondWordform, secondReading) = AddReadingWithIdentity(cache, "kami", "approves", root, third, fourth);
            firstNegativeWordform = AddReadingWithIdentity(cache, "katn", "disapproves", root, second, first).Wordform;
            secondNegativeWordform = AddReadingWithIdentity(cache, "kaim", "disapproves", root, fourth, third).Wordform;
            independentWordform = AddIndependentControl(cache);
        });

        RealParserProject.PrepareForParsing(cache, "k", "a", "n", "t", "m", "i", "p", "q", "e", "r", "u", "v", "w");
        return new AdhocSlotOrderFixture(reverseTemplate is null ? string.Empty : CanonicalId.FromGuid(reverseTemplate.Guid).Value,
            CanonicalId.FromGuid(firstSlot.Guid).Value, CanonicalId.FromGuid(secondSlot.Guid).Value,
            CanonicalId.FromGuid(firstProhibition.Guid).Value,
            CanonicalId.FromGuid(secondProhibition.Guid).Value,
            [CanonicalId.FromGuid(firstReading).Value, CanonicalId.FromGuid(secondReading).Value], ["katn", "kaim"],
            [firstWordform, secondWordform, firstNegativeWordform, secondNegativeWordform, independentWordform],
            ["kant", "kami", "katn", "kaim", "mi"],
            [
                new KeptProhibition(CanonicalId.FromGuid(adjacencyControl.Guid).Value,
                    CanonicalId.FromGuid(adjacencyPrimary.Msa.Guid).Value, AdjacentToLeft,
                    [CanonicalId.FromGuid(adjacencyOther.Msa.Guid).Value]),
                new KeptProhibition(CanonicalId.FromGuid(anywhereControl.Guid).Value,
                    CanonicalId.FromGuid(anywherePrimary.Msa.Guid).Value, Anywhere,
                    [CanonicalId.FromGuid(anywhereOther.Msa.Guid).Value]),
                new KeptProhibition(CanonicalId.FromGuid(multiTargetControl.Guid).Value,
                    CanonicalId.FromGuid(multiPrimary.Msa.Guid).Value, SomewhereToLeft,
                    [CanonicalId.FromGuid(multiOtherOne.Msa.Guid).Value,
                        CanonicalId.FromGuid(multiOtherTwo.Msa.Guid).Value]),
            ]);
    }

    private static IMoMorphAdhocProhib CreateMorphemeProhibition(LcmCache cache, Morph first, int adjacency,
        params Morph[] others)
    {
        var prohibition = cache.ServiceLocator.GetInstance<IMoMorphAdhocProhibFactory>().Create();
        cache.LangProject.MorphologicalDataOA.AdhocCoProhibitionsOC.Add(prohibition);
        prohibition.FirstMorphemeRA = first.Msa;
        prohibition.Adjacency = adjacency;
        foreach (var other in others) prohibition.RestOfMorphsRS.Add(other.Msa);
        return prohibition;
    }

    private static IPartOfSpeech CreateCategory(LcmCache cache, string name)
    {
        var category = cache.ServiceLocator.GetInstance<IPartOfSpeechFactory>().Create();
        cache.LangProject.PartsOfSpeechOA.PossibilitiesOS.Add(category);
        category.Name.set_String(cache.DefaultAnalWs, name);
        return category;
    }

    private static IMoInflAffixSlot CreateSlot(LcmCache cache, IPartOfSpeech category, string name,
        bool optional = false)
    {
        var slot = cache.ServiceLocator.GetInstance<IMoInflAffixSlotFactory>().Create();
        category.AffixSlotsOC.Add(slot);
        slot.Name.set_String(cache.DefaultAnalWs, name);
        slot.Optional = optional;
        return slot;
    }

    private static IMoInflAffixTemplate CreateTemplate(LcmCache cache, IPartOfSpeech category, string name,
        IReadOnlyList<IMoInflAffixSlot> suffixes)
    {
        var template = cache.ServiceLocator.GetInstance<IMoInflAffixTemplateFactory>().Create();
        category.AffixTemplatesOS.Add(template);
        template.Name.set_String(cache.DefaultAnalWs, name);
        template.Final = true;
        foreach (var suffix in suffixes) template.SuffixSlotsRS.Add(suffix);
        return template;
    }

    private static Morph CreateMorph(LcmCache cache, IPartOfSpeech category, Guid morphType, string form,
        string gloss, IMoInflAffixSlot? slot)
    {
        var entry = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create(
            cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>().GetObject(morphType),
            TsStringUtils.MakeString(form, cache.DefaultVernWs), gloss,
            SandboxGenericMSA.Create(morphType == MoMorphTypeTags.kguidMorphStem ? MsaType.kStem : MsaType.kInfl,
                category));
        var msa = entry.MorphoSyntaxAnalysesOC.Single();
        if (slot is not null) Assert.IsAssignableFrom<IMoInflAffMsa>(msa).SlotsRC.Add(slot);
        return new Morph(entry.LexemeFormOA!, msa, entry);
    }

    private static Guid AddReading(LcmCache cache, string surface, string opinion, params Morph[] morphs) =>
        AddReadingWithIdentity(cache, surface, opinion, morphs).Wordform;

    private static (Guid Wordform, Guid Analysis) AddReadingWithIdentity(LcmCache cache, string surface,
        string opinion, params Morph[] morphs)
    {
        var services = cache.ServiceLocator;
        var evaluation = opinion switch
        {
            "approves" => Opinions.approves,
            "disapproves" => Opinions.disapproves,
            "noopinion" => Opinions.noopinion,
            _ => throw new ArgumentOutOfRangeException(nameof(opinion), opinion, "Unknown FieldWorks opinion."),
        };
        var wordform = services.GetInstance<IWfiWordformFactory>().Create(
            TsStringUtils.MakeString(surface, cache.DefaultVernWs));
        var analysis = services.GetInstance<IWfiAnalysisFactory>().Create();
        wordform.AnalysesOC.Add(analysis);
        foreach (var morph in morphs)
        {
            var bundle = services.GetInstance<IWfiMorphBundleFactory>().Create();
            analysis.MorphBundlesOS.Add(bundle);
            bundle.MorphRA = morph.Form;
            bundle.MsaRA = morph.Msa;
        }
        cache.LangProject.DefaultUserAgent.SetEvaluation(analysis, evaluation);
        return (wordform.Guid, analysis.Guid);
    }

    private static Guid AddIndependentControl(LcmCache cache)
    {
        var category = CreateCategory(cache, "Independent control");
        var root = CreateMorph(cache, category, MoMorphTypeTags.kguidMorphStem, "mi", "control", null);
        return AddReading(cache, "mi", "approves", root);
    }

    private static bool CanRecommendOptionality(RecipeVerificationResult result,
        RecipeVerificationReading target) => target.Status == RecipeVerificationReadingStatus.Recovered &&
        result.Status == RecipeVerificationStatus.Pass && result.LostApprovedReadings == 0 &&
        result.NewlyAcceptedNegativeCases == 0 && !result.NegativeEvidenceGap;

    private static void AssertDoubleFillRejected(RecipeVerificationResult result)
    {
        var doubleFill = Assert.Single(result.Negatives, item => item.Target == "disapproved-reading");
        Assert.True(doubleFill.BeforeComplete);
        Assert.True(doubleFill.AfterComplete);
        Assert.False(doubleFill.AfterAccepted);
        Assert.False(doubleFill.NewlyAccepted);
    }

    private static T Resolve<T>(LcmCache cache, string id) where T : class =>
        Assert.IsAssignableFrom<T>(cache.ServiceLocator.GetInstance<ICmObjectRepository>()
            .GetObject(CanonicalId.Parse(id).ToGuid()));

    private static BaselineToken Token(LcmCache cache) => new(cache.LangProject.Guid.ToString("D"),
        BaselineSemanticDigest.Compute(cache), BaselineSemanticDigest.ProjectionVersion,
        DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"), BatchInvocationEvidence.DigestFile(cache.ProjectId.Path));

    private sealed record Morph(IMoForm Form, IMoMorphSynAnalysis Msa, ILexEntry Entry);

    private sealed record RequiredSlotFixture(string RequiredSlot, string TargetReading, Guid[] Wordforms,
        string[] Words);

    private sealed record ReviewedNegativeOrderFixture(string Template, string FirstSlot, string SecondSlot,
        Guid[] Wordforms, string[] Words);

    private sealed record TemplatePrecedenceFixture(string Category, string Template, string FirstSlot, string SecondSlot,
        string TargetReading, string ControlReading, Guid[] Wordforms, string[] Words);

    private sealed record AffixPositionFixture(string Msa, string Slot, string TargetReading, string ControlReading,
        Guid[] Wordforms, string[] Words);

    private sealed record NaturalClassFixture(CanonicalId SourceClass, IReadOnlyList<CanonicalId> InitialMembers,
        IReadOnlyList<CanonicalId> ProductiveMembers, CanonicalId TargetRuleContext,
        CanonicalId RetainedRuleContext, CanonicalId TargetRuleId, CanonicalId RetainedRuleId,
        CanonicalId SharedEnvironment, CanonicalId RecordType, Guid[] Wordforms, string[] Words,
        string[] ProductiveReadings, string[] HeldOutReadings, string? RetainedTReading);

    private sealed record AdhocSlotOrderFixture(string ReverseTemplate, string FirstSlot, string SecondSlot,
        string FirstProhibition, string SecondProhibition, string[] ApprovedReadings,
        string[] ForbiddenSwaps, Guid[] Wordforms, string[] Words, KeptProhibition[] KeptControls)
    {
        public string[] ReplacementProhibitions => [FirstProhibition, SecondProhibition];
    }

    private sealed record KeptProhibition(string Guid, string Primary, int Adjacency, string[] Targets);

    private sealed record Acceptance(Proposal Proposal, DryRun DryRun, RecipeVerificationResult Verification,
        string AfterProjectPath, FrozenExpectationSet Frozen, ComposedOperationsResponse Composed,
        string BeforeProjectPath);
}

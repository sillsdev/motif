using System.Collections.Generic;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Runner.Snapshotting;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Runner;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class RewriteRuleContextTests(PristineProjectFixture pristine)
{
    [Fact]
    public void OrderedRuleMove_UsesTheDeclaredNeighbourAndKeepsTheRuleIdentity()
    {
        using var cache = pristine.NewScratch();
        var data = cache.LangProject.PhonologicalDataOA;
        IPhRegularRule first = null!;
        IPhRegularRule second = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            first = cache.ServiceLocator.GetInstance<IPhRegularRuleFactory>().Create();
            second = cache.ServiceLocator.GetInstance<IPhRegularRuleFactory>().Create();
            data.PhonRulesOS.Add(first);
            data.PhonRulesOS.Add(second);
        });
        var firstId = CanonicalId.FromGuid(first.Guid);
        var secondId = CanonicalId.FromGuid(second.Guid);
        var move = new OperationEnvelope(CanonicalId.Mint(), "grammar/phPhonData/movePhonRules",
            target: CanonicalId.FromGuid(data.Guid),
            after: JsonSerializer.SerializeToElement(new { member = firstId.Value }),
            placement: new Placement(secondId, null));
        var proposal = Proposal([move]);

        var dryRun = ScratchDryRun.Of(cache, proposal);
        ProposalApplier.Apply(cache, proposal, dryRun.Anchor, "rewrite-rule-order-tests");

        Assert.Equal(new[] { secondId, firstId }, data.PhonRulesOS.Select(rule => CanonicalId.FromGuid(rule.Guid)));
        Assert.Equal(first.Guid, data.PhonRulesOS[1].Guid);
    }

    [Fact]
    public void RuleMoveSlots_UseTheRuleMemberIdentityAsTheirDiscriminator()
    {
        var data = CanonicalId.Mint();
        var first = CanonicalId.Mint();
        var second = CanonicalId.Mint();
        var firstMove = MoveRule(data, first, second);
        var sameSlotMove = MoveRule(data, first, second);
        var otherRuleMove = MoveRule(data, second, first);

        Assert.Null(ProposalOperationSlotValidator.FindConflict([firstMove, otherRuleMove]));
        Assert.NotNull(ProposalOperationSlotValidator.FindConflict([firstMove, sameSlotMove]));
    }

    [Fact]
    public void RuleOrderModel_UsesMinimalMovesAndDeclaresTheirExecutionDependencies()
    {
        var data = CanonicalId.Mint();
        var first = CanonicalId.Mint();
        var second = CanonicalId.Mint();
        var third = CanonicalId.Mint();

        var operations = PhonologicalRuleOrderModel.PlanMoves(data, [first, second, third], [third, second, first]);

        Assert.Equal(2, operations.Count);
        Assert.Equal(new[] { first.Value, second.Value }, operations.Select(operation =>
            operation.After!.Value.GetProperty("member").GetString()));
        Assert.Equal(new Placement(third, null), operations[0].Placement);
        Assert.Equal(new Placement(third, first), operations[1].Placement);
        Assert.Contains(operations[0].OperationId,
            operations[1].DependsOn.Select(dependency => dependency.OperationId));
    }

    [Fact]
    public void OperationExecutionOrder_RejectsMissingAndCyclicDependencies()
    {
        var firstId = CanonicalId.Mint();
        var secondId = CanonicalId.Mint();
        var first = new OperationEnvelope(firstId, "test/first");
        var missing = new OperationEnvelope(secondId, "test/second", dependsOn: [new(CanonicalId.Mint())]);
        Assert.Throws<ContractParseException>(() => OperationExecutionOrder.Sort([first, missing]));

        var cyclicFirst = new OperationEnvelope(firstId, "test/first", dependsOn: [new(secondId)]);
        var cyclicSecond = new OperationEnvelope(secondId, "test/second", dependsOn: [new(firstId)]);
        Assert.Throws<ContractParseException>(() => OperationExecutionOrder.Sort([cyclicFirst, cyclicSecond]));
    }

    [Theory]
    [InlineData("input")]
    [InlineData("output")]
    public void RuleIntentParser_RejectsMoreThanOneChangedItem(string field)
    {
        var twoItems = $$"""[{"phoneme":"{{CanonicalId.Mint().Value}}"},{"phoneme":"{{CanonicalId.Mint().Value}}"}]""";
        var input = field == "input" ? twoItems : "[]";
        var output = field == "output" ? twoItems : "[]";
        using var document = JsonDocument.Parse($$"""
            {"name":"n to m","direction":"left-to-right","input":{{input}},"output":{{output}},
             "left":[{"boundary":"word"}],"right":[{"boundary":"word"}]}
            """);

        Assert.Throws<ContractParseException>(() => AuthorPhonologicalRuleIntentParser.Parse(document.RootElement));
    }

    [Theory]
    [InlineData("alpha")]
    [InlineData("metathesis")]
    public void RuleIntentParser_RejectsUnsupportedRuleNotation(string property)
    {
        using var document = JsonDocument.Parse($$"""
            {"name":"n to m","direction":"left-to-right","input":[],"output":[],
             "left":[{"boundary":"word"}],"right":[{"boundary":"word"}],"{{property}}":true}
            """);

        Assert.Throws<ContractParseException>(() => AuthorPhonologicalRuleIntentParser.Parse(document.RootElement));
    }

    [Fact]
    public void RuleComposer_AuthorsInsertionAndDeletionAndEnablesCompleteRules()
    {
        using var cache = pristine.NewScratch();
        var sound = AddSoundSystem(cache);
        var wordBoundary = CanonicalId.FromGuid(LangProjectTags.kguidPhRuleWordBdry);
        var insertion = AuthorPhonologicalRuleComposer.Build(cache, new("insert m", PhonologicalRuleDirection.Simultaneous,
            [], [new(Phoneme: sound.PhonemeM)], [new(Boundary: "word")], [new(Boundary: "word")]));
        var insertionProposal = Proposal(insertion);
        var insertionDryRun = ScratchDryRun.Of(cache, insertionProposal);
        ProposalApplier.Apply(cache, insertionProposal, insertionDryRun.Anchor, "rewrite-rule-composer-tests");
        var insertionRuleId = insertion.Single(operation => operation.Kind == PhPhonDataPhonRulesOperationKinds.Create)
            .EntityId!.Value;
        var insertionRule = Assert.IsAssignableFrom<IPhRegularRule>(cache.ServiceLocator
            .GetInstance<ICmObjectRepository>().GetObject(insertionRuleId.ToGuid()));
        Assert.False(insertionRule.Disabled);
        Assert.Empty(insertionRule.StrucDescOS);
        var insertionRhs = Assert.Single(insertionRule.RightHandSidesOS);
        Assert.Equal(sound.PhonemeM.ToGuid(), Assert.IsAssignableFrom<IPhSimpleContextSeg>(
            Assert.Single(insertionRhs.StrucChangeOS)).FeatureStructureRA.Guid);
        Assert.Equal(wordBoundary.ToGuid(), Assert.IsAssignableFrom<IPhSimpleContextBdry>(
            insertionRhs.LeftContextOA).FeatureStructureRA.Guid);
        Assert.Equal(wordBoundary.ToGuid(), Assert.IsAssignableFrom<IPhSimpleContextBdry>(
            insertionRhs.RightContextOA).FeatureStructureRA.Guid);

        var deletion = AuthorPhonologicalRuleComposer.Build(cache, new("delete n", PhonologicalRuleDirection.RightToLeftIterative,
            [new(Phoneme: sound.PhonemeN)], [], [new(Boundary: "word")], [new(Boundary: "word")],
            new(insertionRuleId, null)));
        var deletionProposal = Proposal(deletion);
        var deletionDryRun = ScratchDryRun.Of(cache, deletionProposal);
        ProposalApplier.Apply(cache, deletionProposal, deletionDryRun.Anchor, "rewrite-rule-composer-tests");
        var deletionRuleId = deletion.Single(operation => operation.Kind == PhPhonDataPhonRulesOperationKinds.Create)
            .EntityId!.Value;
        var deletionRule = Assert.IsAssignableFrom<IPhRegularRule>(cache.ServiceLocator
            .GetInstance<ICmObjectRepository>().GetObject(deletionRuleId.ToGuid()));
        Assert.False(deletionRule.Disabled);
        Assert.Equal(sound.PhonemeN.ToGuid(), Assert.IsAssignableFrom<IPhSimpleContextSeg>(
            Assert.Single(deletionRule.StrucDescOS)).FeatureStructureRA.Guid);
        Assert.Empty(Assert.Single(deletionRule.RightHandSidesOS).StrucChangeOS);
    }

    [Fact]
    public void RuleComposer_UsesWellKnownBoundaryIdentitiesAndKeepsExplicitMarkerLiteralAfterReopen()
    {
        using var cache = pristine.NewScratch();
        var sound = AddSoundSystem(cache);
        var wordBoundary = CanonicalId.FromGuid(LangProjectTags.kguidPhRuleWordBdry);
        var wordOperations = AuthorPhonologicalRuleComposer.Build(cache, new("word-boundary rule",
            PhonologicalRuleDirection.Simultaneous, [new(Phoneme: sound.PhonemeN)], [new(Phoneme: sound.PhonemeM)],
            [new(Boundary: "word")], [new(NaturalClass: sound.NaturalClass)]));
        var wordRuleId = wordOperations.Single(operation =>
            operation.Kind == PhPhonDataPhonRulesOperationKinds.Create).EntityId!.Value;
        Execute(cache, wordOperations);
        var wordRule = Assert.IsAssignableFrom<IPhRegularRule>(cache.ServiceLocator
            .GetInstance<ICmObjectRepository>().GetObject(wordRuleId.ToGuid()));
        Assert.Equal(wordBoundary.ToGuid(), Assert.IsAssignableFrom<IPhSimpleContextBdry>(
            Assert.Single(wordRule.RightHandSidesOS).LeftContextOA).FeatureStructureRA.Guid);
        var wordMarker = Assert.Single(cache.LangProject.PhonologicalDataOA.PhonemeSetsOS.Single().BoundaryMarkersOC,
            marker => marker.Guid == LangProjectTags.kguidPhRuleWordBdry);
        Assert.Equal(cache.LangProject.PhonologicalDataOA.PhonemeSetsOS.Single().Guid, wordMarker.Owner?.Guid);

        var morphemeBoundary = AddBoundaryMarker(cache, LangProjectTags.kguidPhRuleMorphBdry, "+");
        var morphemeOperations = AuthorPhonologicalRuleComposer.Build(cache, new("morpheme-boundary rule",
            PhonologicalRuleDirection.Simultaneous, [new(Phoneme: sound.PhonemeN)], [new(Phoneme: sound.PhonemeM)],
            [new(Boundary: "morpheme")], [new(NaturalClass: sound.NaturalClass)], new(wordRuleId, null)));
        var morphemeRuleId = morphemeOperations.Single(operation =>
            operation.Kind == PhPhonDataPhonRulesOperationKinds.Create).EntityId!.Value;
        Execute(cache, morphemeOperations);
        var morphemeRule = Assert.IsAssignableFrom<IPhRegularRule>(cache.ServiceLocator
            .GetInstance<ICmObjectRepository>().GetObject(morphemeRuleId.ToGuid()));
        Assert.Equal(morphemeBoundary.ToGuid(), Assert.IsAssignableFrom<IPhSimpleContextBdry>(
            Assert.Single(morphemeRule.RightHandSidesOS).LeftContextOA).FeatureStructureRA.Guid);

        var literalHash = AddBoundaryMarker(cache, Guid.NewGuid(), "#");
        var literalOperations = AuthorPhonologicalRuleComposer.Build(cache, new("literal hash rule",
            PhonologicalRuleDirection.Simultaneous, [new(Phoneme: sound.PhonemeN)], [new(Phoneme: sound.PhonemeM)],
            [new(BoundaryMarker: literalHash)], [new(NaturalClass: sound.NaturalClass)],
            new(morphemeRuleId, null)));
        var literalRuleId = literalOperations.Single(operation =>
            operation.Kind == PhPhonDataPhonRulesOperationKinds.Create).EntityId!.Value;
        Execute(cache, literalOperations);
        var literalRule = Assert.IsAssignableFrom<IPhRegularRule>(cache.ServiceLocator
            .GetInstance<ICmObjectRepository>().GetObject(literalRuleId.ToGuid()));
        var actualMarker = Assert.IsAssignableFrom<IPhSimpleContextBdry>(
            Assert.Single(literalRule.RightHandSidesOS).LeftContextOA).FeatureStructureRA;
        Assert.Equal(literalHash.ToGuid(), actualMarker.Guid);
        Assert.NotEqual(LangProjectTags.kguidPhRuleWordBdry, actualMarker.Guid);

        var snapshot = SoundSystemSnapshots.Read(cache);
        new FwDataProjectLoader().Save(cache);
        using var reopened = new FwDataProjectLoader().LoadScratchCache(cache.ProjectId.Path);
        Assert.Empty(SoundSystemSnapshots.Compare(snapshot, SoundSystemSnapshots.Read(reopened)));
        var repository = reopened.ServiceLocator.GetInstance<ICmObjectRepository>();
        foreach (var (ruleId, expectedBoundary) in new[]
                 {
                     (wordRuleId, LangProjectTags.kguidPhRuleWordBdry),
                     (morphemeRuleId, LangProjectTags.kguidPhRuleMorphBdry),
                     (literalRuleId, literalHash.ToGuid()),
                 })
        {
            var reopenedRule = Assert.IsAssignableFrom<IPhRegularRule>(repository.GetObject(ruleId.ToGuid()));
            Assert.Equal(expectedBoundary, Assert.IsAssignableFrom<IPhSimpleContextBdry>(
                Assert.Single(reopenedRule.RightHandSidesOS).LeftContextOA).FeatureStructureRA.Guid);
        }
    }

    [Fact]
    public void RuleComposer_CreatesMissingReservedBoundaryMarkersAsOwnedAndReportsThemInDryRun()
    {
        using var cache = pristine.NewScratch();
        var sound = AddSoundSystem(cache);
        var set = cache.LangProject.PhonologicalDataOA.PhonemeSetsOS.Single();
        var reservedMarkers = set.BoundaryMarkersOC.Where(marker =>
            marker.Guid == LangProjectTags.kguidPhRuleWordBdry ||
            marker.Guid == LangProjectTags.kguidPhRuleMorphBdry).ToArray();
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            foreach (var marker in reservedMarkers) marker.Delete();
        });
        var before = SoundSystemSnapshots.Read(cache);
        var wordBoundary = CanonicalId.FromGuid(LangProjectTags.kguidPhRuleWordBdry);
        var morphemeBoundary = CanonicalId.FromGuid(LangProjectTags.kguidPhRuleMorphBdry);

        var operations = AuthorPhonologicalRuleComposer.Build(cache, new("reserved boundaries",
            PhonologicalRuleDirection.Simultaneous,
            [new(Phoneme: sound.PhonemeN)], [new(Phoneme: sound.PhonemeM)],
            [new(Boundary: "word")], [new(Boundary: "morpheme")]));
        var markerCreates = operations.Where(operation =>
            operation.Kind == PhPhonemeSetBoundaryMarkersOperationKinds.Create).ToArray();

        Assert.Equal(new[] { wordBoundary, morphemeBoundary }.OrderBy(id => id.Value, StringComparer.Ordinal),
            markerCreates.Select(operation => operation.EntityId!.Value)
                .OrderBy(id => id.Value, StringComparer.Ordinal));

        var proposal = Proposal(operations);
        var dryRun = ScratchDryRun.Of(cache, proposal);
        var markerEffects = dryRun.ExpectedEffects.Where(effect =>
            effect.CanonicalId == CanonicalId.FromGuid(set.Guid) &&
            effect.Field == SnapshotFields.PhPhonemeSetBoundaryMarkers).ToArray();
        Assert.Equal(2, markerEffects.Length);
        Assert.Contains(markerEffects, effect => effect.After.Values.Contains(wordBoundary.Value));
        Assert.Contains(markerEffects, effect => effect.After.Values.Contains(morphemeBoundary.Value));
        Assert.Empty(SoundSystemSnapshots.Compare(before, SoundSystemSnapshots.Read(cache)));

        var ruleId = operations.Single(operation =>
            operation.Kind == PhPhonDataPhonRulesOperationKinds.Create).EntityId!.Value;
        ProposalApplier.Apply(cache, proposal, dryRun.Anchor, "rewrite-context-tests");
        var rule = Assert.IsAssignableFrom<IPhRegularRule>(cache.ServiceLocator.ObjectRepository.GetObject(ruleId.ToGuid()));
        Assert.Equal(wordBoundary.ToGuid(), Assert.IsAssignableFrom<IPhSimpleContextBdry>(
            Assert.Single(rule.RightHandSidesOS).LeftContextOA).FeatureStructureRA!.Guid);
        Assert.Equal(morphemeBoundary.ToGuid(), Assert.IsAssignableFrom<IPhSimpleContextBdry>(
            Assert.Single(rule.RightHandSidesOS).RightContextOA).FeatureStructureRA!.Guid);

        var loader = new FwDataProjectLoader();
        loader.Save(cache);
        using var reopened = loader.LoadScratchCache(cache.ProjectId.Path);
        var reopenedSet = reopened.LangProject.PhonologicalDataOA.PhonemeSetsOS.Single();
        foreach (var (id, code) in new[] { (wordBoundary, "#"), (morphemeBoundary, "+") })
        {
            var marker = Assert.Single(reopenedSet.BoundaryMarkersOC, item => item.Guid == id.ToGuid());
            Assert.Equal(reopenedSet.Guid, marker.Owner?.Guid);
            Assert.Equal(code, marker.Name.get_String(reopened.DefaultVernWs)?.Text);
            Assert.Equal(code, Assert.Single(marker.CodesOS).Representation.get_String(reopened.DefaultVernWs)?.Text);
        }
    }

    [Fact]
    public void RuleComposer_RefusesAReservedBoundaryIdentityOwnedByAnotherObjectType()
    {
        using var cache = pristine.NewScratch();
        var sound = AddSoundSystem(cache);
        var set = cache.LangProject.PhonologicalDataOA.PhonemeSetsOS.Single();
        var existingWordMarker = set.BoundaryMarkersOC.SingleOrDefault(marker =>
            marker.Guid == LangProjectTags.kguidPhRuleWordBdry);
        if (existingWordMarker is not null)
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, existingWordMarker.Delete);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var wrongType = cache.ServiceLocator.GetInstance<IPhPhonemeFactory>()
                .Create(LangProjectTags.kguidPhRuleWordBdry);
            set.PhonemesOC.Add(wrongType);
        });
        var before = SoundSystemSnapshots.Read(cache);

        var error = Assert.Throws<InvalidOperationException>(() => AuthorPhonologicalRuleComposer.Build(cache,
            new("wrong reserved identity type", PhonologicalRuleDirection.Simultaneous,
                [new(Phoneme: sound.PhonemeN)], [new(Phoneme: sound.PhonemeM)],
                [new(Boundary: "word")], [new(NaturalClass: sound.NaturalClass)])));

        Assert.Contains("PhBdryMarker", error.Message, StringComparison.Ordinal);
        Assert.Empty(SoundSystemSnapshots.Compare(before, SoundSystemSnapshots.Read(cache)));
    }

    [Fact]
    public void RuleContextLowering_RequiresTheOwnedBoundaryMarkerToExist()
    {
        using var cache = pristine.NewScratch();
        var sound = AddSoundSystem(cache);
        var operations = AuthorPhonologicalRuleComposer.Build(cache, new("word edge",
            PhonologicalRuleDirection.Simultaneous,
            [new(Phoneme: sound.PhonemeN)], [new(Phoneme: sound.PhonemeM)],
            [new(Boundary: "word")], [new(NaturalClass: sound.NaturalClass)]));
        var wordMarker = cache.LangProject.PhonologicalDataOA.PhonemeSetsOS.Single().BoundaryMarkersOC.Single(marker =>
            marker.Guid == LangProjectTags.kguidPhRuleWordBdry);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, wordMarker.Delete);

        var error = Assert.Throws<KeyNotFoundException>(() => ScratchDryRun.Of(cache, Proposal(operations)));

        Assert.Contains(LangProjectTags.kguidPhRuleWordBdry.ToString("D"), error.Message,
            StringComparison.Ordinal);
        Assert.False(cache.ServiceLocator.ObjectRepository.TryGetObject(LangProjectTags.kguidPhRuleWordBdry, out _));
    }

    [Fact]
    public void RuleComposer_OrdersSequenceContextsAndEnablesOnlyAfterEveryDeclaredOperation()
    {
        using var cache = pristine.NewScratch();
        var sound = AddSoundSystem(cache);
        var wordBoundary = CanonicalId.FromGuid(LangProjectTags.kguidPhRuleWordBdry);
        var data = cache.LangProject.PhonologicalDataOA;
        IPhRegularRule first = null!;
        IPhRegularRule second = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            first = cache.ServiceLocator.GetInstance<IPhRegularRuleFactory>().Create();
            second = cache.ServiceLocator.GetInstance<IPhRegularRuleFactory>().Create();
            data.PhonRulesOS.Add(first);
            data.PhonRulesOS.Add(second);
        });
        var firstId = CanonicalId.FromGuid(first.Guid);
        var secondId = CanonicalId.FromGuid(second.Guid);
        var before = SoundSystemSnapshots.Read(cache);
        var operations = AuthorPhonologicalRuleComposer.Build(cache, new("n becomes m at word edge",
            PhonologicalRuleDirection.LeftToRightIterative, [new(Phoneme: sound.PhonemeN)],
            [new(Phoneme: sound.PhonemeM)], [new(Boundary: "word"), new(Phoneme: sound.PhonemeN)],
            [new(NaturalClass: sound.NaturalClass), new(Boundary: "word")], new(firstId, secondId)));
        var enable = operations.Single(operation => operation.Kind == PhSegmentRuleDisabledOperationKinds.SetDisabled);
        Assert.False(enable.After!.Value.GetProperty("value").GetBoolean());
        Assert.Equal(operations.Where(operation => operation.OperationId != enable.OperationId)
            .Select(operation => operation.OperationId).Order(), enable.DependsOn.Select(dependency => dependency.OperationId).Order());

        var proposal = Proposal(operations.Reverse().ToArray());
        var dryRun = ScratchDryRun.Of(cache, proposal);
        Assert.NotEmpty(dryRun.ExpectedEffects);
        var rollback = Assert.Throws<InvalidOperationException>(() => ProposalApplier.Apply(cache, proposal,
            dryRun.Anchor, "rewrite-rule-composer-tests", "Rollback the complete rule", afterOperation: (_, operation) =>
            {
                if (operation.OperationId == enable.OperationId)
                    throw new InvalidOperationException("injected complete-rule rollback");
            }));
        Assert.Equal("injected complete-rule rollback", rollback.Message);
        Assert.Empty(SoundSystemSnapshots.Compare(before, SoundSystemSnapshots.Read(cache)));

        ProposalApplier.Apply(cache, proposal, dryRun.Anchor, "rewrite-rule-composer-tests");
        var ruleId = operations.Single(operation => operation.Kind == PhPhonDataPhonRulesOperationKinds.Create)
            .EntityId!.Value;
        Assert.Equal(new[] { first.Guid, ruleId.ToGuid(), second.Guid }, data.PhonRulesOS.Select(rule => rule.Guid));
        var rule = Assert.IsAssignableFrom<IPhRegularRule>(cache.ServiceLocator.GetInstance<ICmObjectRepository>()
            .GetObject(ruleId.ToGuid()));
        Assert.False(rule.Disabled);
        Assert.Equal(sound.PhonemeN.ToGuid(), Assert.IsAssignableFrom<IPhSimpleContextSeg>(
            Assert.Single(rule.StrucDescOS)).FeatureStructureRA.Guid);
        var rhs = Assert.Single(rule.RightHandSidesOS);
        var left = Assert.IsAssignableFrom<IPhSequenceContext>(rhs.LeftContextOA);
        Assert.Equal(new[] { wordBoundary.ToGuid(), sound.PhonemeN.ToGuid() },
            left.MembersRS.Select(ContextReference));
        var right = Assert.IsAssignableFrom<IPhSequenceContext>(rhs.RightContextOA);
        Assert.Equal(new[] { sound.NaturalClass.ToGuid(), wordBoundary.ToGuid() },
            right.MembersRS.Select(ContextReference));
    }

    [Fact]
    public void RuleComposer_RefusesWrongAndEmptyNaturalClasses()
    {
        using var cache = pristine.NewScratch();
        var sound = AddSoundSystem(cache);
        var wrongClass = Assert.Throws<InvalidOperationException>(() => AuthorPhonologicalRuleComposer.Build(cache,
            new("wrong class", PhonologicalRuleDirection.Simultaneous,
                [new(NaturalClass: sound.PhonemeN)], [new(Phoneme: sound.PhonemeM)],
                [new(Boundary: "word")], [new(Boundary: "word")])));
        Assert.Contains("PhNaturalClass", wrongClass.Message, StringComparison.Ordinal);

        IPhNaturalClass empty = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            empty = cache.ServiceLocator.GetInstance<IPhNCSegmentsFactory>().Create();
            cache.LangProject.PhonologicalDataOA.NaturalClassesOS.Add(empty);
        });
        var emptyClass = Assert.Throws<InvalidOperationException>(() => AuthorPhonologicalRuleComposer.Build(cache,
            new("empty class", PhonologicalRuleDirection.Simultaneous,
                [new(NaturalClass: CanonicalId.FromGuid(empty.Guid))], [new(Phoneme: sound.PhonemeM)],
                [new(Boundary: "word")], [new(Boundary: "word")])));
        Assert.Contains("empty natural class", emptyClass.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TypedContexts_DryRunApplySaveAndReopen_PreserveOwnersReferencesAndSequencePlacement()
    {
        using var cache = pristine.NewScratch();
        var sound = AddSoundSystem(cache);
        var built = BuildProposal(cache, sound);
        var proposal = built.Proposal;
        var before = SoundSystemSnapshots.Read(cache);

        var dryRun = ScratchDryRun.Of(cache, proposal);
        Assert.NotEmpty(dryRun.ExpectedEffects);
        Assert.Empty(SoundSystemSnapshots.Compare(before, SoundSystemSnapshots.Read(cache)));

        var rollback = Assert.Throws<InvalidOperationException>(() => ProposalApplier.Apply(cache, proposal,
            dryRun.Anchor, "rewrite-context-tests", "Rollback context creation", afterOperation: (_, operation) =>
            {
                if (operation.Kind == PhSegmentRuleStrucDescOperationKinds.Create)
                    throw new InvalidOperationException("injected context rollback");
            }));
        Assert.Equal("injected context rollback", rollback.Message);
        Assert.Empty(SoundSystemSnapshots.Compare(before, SoundSystemSnapshots.Read(cache)));

        ProposalApplier.Apply(cache, proposal, dryRun.Anchor, "rewrite-context-tests");
        var after = SoundSystemSnapshots.Read(cache);
        Assert.Contains(SoundSystemSnapshots.Compare(before, after), effect =>
            effect.CanonicalId == CanonicalId.FromGuid(cache.LangProject.PhonologicalDataOA.Guid) &&
            effect.Field == SnapshotFields.PhPhonDataContexts);

        var repository = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
        var rule = Assert.IsAssignableFrom<IPhRegularRule>(repository.GetObject(built.Rule.ToGuid()));
        var rhs = Assert.Single(rule.RightHandSidesOS);
        Assert.True(rule.Disabled);
        Assert.Equal(rule.Guid, rhs.Owner?.Guid);
        Assert.Equal(sound.PhonemeN.ToGuid(), Assert.IsAssignableFrom<IPhSimpleContextSeg>(Assert.Single(rule.StrucDescOS)).FeatureStructureRA.Guid);
        Assert.Equal(sound.PhonemeM.ToGuid(), Assert.IsAssignableFrom<IPhSimpleContextSeg>(Assert.Single(rhs.StrucChangeOS)).FeatureStructureRA.Guid);
        Assert.Equal(sound.NaturalClass.ToGuid(), Assert.IsAssignableFrom<IPhSimpleContextNC>(rhs.RightContextOA).FeatureStructureRA.Guid);
        var labialClass = Assert.IsAssignableFrom<IPhNCFeatures>(repository.GetObject(sound.NaturalClass.ToGuid()));
        var featureValue = Assert.IsAssignableFrom<IFsClosedValue>(Assert.Single(labialClass.FeaturesOA.FeatureSpecsOC));
        Assert.Equal(cache.LangProject.PhFeatureSystemOA.Guid, featureValue.FeatureRA.Owner?.Guid);
        Assert.Equal(featureValue.FeatureRA.Guid, featureValue.ValueRA.Owner?.Guid);

        var left = Assert.IsAssignableFrom<IPhSequenceContext>(rhs.LeftContextOA);
        Assert.Equal(new[] { built.PoolSegment.Value, built.PoolBoundary.Value },
            left.MembersRS.Select(member => CanonicalId.FromGuid(member.Guid).Value));
        Assert.All(left.MembersRS, member => Assert.Equal(cache.LangProject.PhonologicalDataOA.Guid,
            member.Owner?.Guid));
        Assert.Equal(sound.Boundary.ToGuid(), Assert.IsAssignableFrom<IPhSimpleContextBdry>(
            left.MembersRS[1]).FeatureStructureRA.Guid);
        var poolSequence = Assert.IsAssignableFrom<IPhSequenceContext>(repository.GetObject(built.PoolSequence.ToGuid()));
        Assert.Equal(new[] { built.PoolSequenceSegment.ToGuid(), built.PoolSequenceBoundary.ToGuid() },
            poolSequence.MembersRS.Select(member => member.Guid));

        new FwDataProjectLoader().Save(cache);
        using var reopened = new FwDataProjectLoader().LoadScratchCache(cache.ProjectId.Path);
        Assert.Empty(SoundSystemSnapshots.Compare(after, SoundSystemSnapshots.Read(reopened)));
        var reopenedRule = Assert.IsAssignableFrom<IPhRegularRule>(reopened.ServiceLocator
            .GetInstance<ICmObjectRepository>().GetObject(built.Rule.ToGuid()));
        Assert.Equal(new[] { built.PoolSegment.ToGuid(), built.PoolBoundary.ToGuid() },
            Assert.IsAssignableFrom<IPhSequenceContext>(Assert.Single(reopenedRule.RightHandSidesOS).LeftContextOA)
                .MembersRS.Select(member => member.Guid));
    }

    [Fact]
    public void EmptyRuleInputAndOutputSides_RepresentInsertionAndDeletion()
    {
        using var cache = pristine.NewScratch();
        var data = cache.LangProject.PhonologicalDataOA;
        var ruleCreate = Create(PhPhonDataPhonRulesOperationKinds.Create, data.Guid, "PhRegularRule");
        var rhsCreate = Create(PhRegularRuleRightHandSidesOperationKinds.Create, ruleCreate.EntityId!.Value.ToGuid(), null,
            Depends(ruleCreate));
        var proposal = Proposal([ruleCreate, rhsCreate]);

        var dryRun = ScratchDryRun.Of(cache, proposal);
        ProposalApplier.Apply(cache, proposal, dryRun.Anchor, "rewrite-context-tests");

        var rule = Assert.IsAssignableFrom<IPhRegularRule>(cache.ServiceLocator.GetInstance<ICmObjectRepository>()
            .GetObject(ruleCreate.EntityId!.Value.ToGuid()));
        Assert.Empty(rule.StrucDescOS);
        Assert.Empty(Assert.Single(rule.RightHandSidesOS).StrucChangeOS);
        Assert.True(rule.Disabled);
    }

    [Fact]
    public void ContextCreation_DeclaresDependenciesOnCreatedOwnersReferencesAndPlacementNeighbours()
    {
        using var cache = pristine.NewScratch();
        var built = BuildProposal(cache, AddSoundSystem(cache));
        var creates = built.Proposal.Operations.Where(operation => operation.EntityId is not null)
            .ToDictionary(operation => operation.EntityId!.Value, operation => operation.OperationId);

        foreach (var operation in built.Proposal.Operations)
        {
            var references = new List<CanonicalId>();
            if (operation.Target is { } target) references.Add(target);
            if (operation.Placement?.After is { } after) references.Add(after);
            if (operation.Placement?.Before is { } before) references.Add(before);
            if (operation.After is { } payload && payload.ValueKind == JsonValueKind.Object)
                foreach (var name in new[] { "ref", "member" })
                    if (payload.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String &&
                        CanonicalId.TryParse(property.GetString(), out var reference)) references.Add(reference);
            foreach (var reference in references.Distinct())
                if (creates.TryGetValue(reference, out var dependency))
                    Assert.Contains(operation.DependsOn, item => item.OperationId == dependency);
        }
    }

    [Fact]
    public void ContextSequences_RejectCyclesAndReusedReferences()
    {
        using var cache = pristine.NewScratch();
        var sound = AddSoundSystem(cache);
        var cycle = BuildProposal(cache, sound);
        var cycleSequence = cycle.Proposal.Operations.Single(operation =>
            operation.Kind == PhSegRuleRHSLeftContextOperationKinds.Create);
        var cycleTail = new OperationEnvelope(CanonicalId.Mint(), PhSequenceContextMembersOperationKinds.AddRefMembers,
            target: cycle.LeftContext, after: JsonSerializer.SerializeToElement(new { member = cycle.LeftContext.Value }),
            placement: new Placement(cycle.PoolBoundary, null),
            dependsOn: [new(cycleSequence.OperationId), new(cycle.Proposal.Operations.Single(operation =>
                operation.EntityId == cycle.PoolBoundary).OperationId)]);
        var cyclic = WithOperations(cycle.Proposal, cycle.Proposal.Operations.Append(cycleTail).ToArray());
        var cycleError = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(cache, cyclic));
        Assert.Contains("cycle", cycleError.Message, StringComparison.OrdinalIgnoreCase);

        var reused = BuildProposal(cache, sound, reuseRightSequence: true);
        var reuseError = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(cache, reused.Proposal));
        Assert.Contains("reused", reuseError.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ContextSequences_RejectNestedAndOverlongShapes()
    {
        using var cache = pristine.NewScratch();
        var sound = AddSoundSystem(cache);
        var nested = BuildProposal(cache, sound);
        var nestedSequence = nested.Proposal.Operations.Single(operation =>
            operation.Kind == PhPhonDataContextsOperationKinds.Create &&
            operation.After is { } after && after.GetProperty("class").GetString() == "PhSequenceContext");
        var leftSequence = nested.Proposal.Operations.Single(operation =>
            operation.Kind == PhSegRuleRHSLeftContextOperationKinds.Create);
        var boundaryCreate = nested.Proposal.Operations.Single(operation => operation.EntityId == nested.PoolBoundary);
        var nestedTail = AddMember(leftSequence, nestedSequence, nested.PoolBoundary, boundaryCreate);
        var nestedProposal = WithOperations(nested.Proposal, nested.Proposal.Operations.Append(nestedTail).ToArray());
        var nestedError = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(cache, nestedProposal));
        Assert.Contains("only simple contexts", nestedError.Message, StringComparison.Ordinal);

        var longSequence = BuildProposal(cache, sound);
        var operations = longSequence.Proposal.Operations.ToList();
        var owner = cache.LangProject.PhonologicalDataOA;
        var left = operations.Single(operation => operation.Kind == PhSegRuleRHSLeftContextOperationKinds.Create);
        var previousId = longSequence.PoolBoundary;
        var previousCreate = operations.Single(operation => operation.EntityId == previousId);
        for (var i = 0; i < 7; i++)
        {
            var context = Create(PhPhonDataContextsOperationKinds.Create, owner.Guid, "PhSimpleContextSeg");
            operations.Add(context);
            operations.Add(Set(PhSimpleContextSegFeatureStructureOperationKinds.SetFeatureStructure,
                context, sound.PhonemeN));
            operations.Add(AddMember(left, context, previousId, previousCreate));
            previousId = context.EntityId!.Value;
            previousCreate = context;
        }
        var longProposal = WithOperations(longSequence.Proposal, operations);
        var longError = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(cache, longProposal));
        Assert.Contains("between 1 and 8", longError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ContextPoolDrift_RequiresANewDryRunBeforeApply()
    {
        using var cache = pristine.NewScratch();
        var built = BuildProposal(cache, AddSoundSystem(cache));
        var dryRun = ScratchDryRun.Of(cache, built.Proposal);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            cache.LangProject.PhonologicalDataOA.ContextsOS.Add(
                cache.ServiceLocator.GetInstance<IPhSimpleContextSegFactory>().Create()));

        var error = Assert.Throws<ApplyPreconditionException>(() => ProposalApplier.Apply(cache,
            built.Proposal, dryRun.Anchor, "rewrite-context-tests"));

        Assert.Contains("Footprint drift detected", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SequenceMemberOperation_RejectsAnOwnerThatIsNotASequenceContext()
    {
        using var cache = pristine.NewScratch();
        var data = cache.LangProject.PhonologicalDataOA;
        var invalid = Proposal([new OperationEnvelope(CanonicalId.Mint(),
            PhSequenceContextMembersOperationKinds.AddRefMembers,
            target: CanonicalId.FromGuid(data.Guid),
            after: JsonSerializer.SerializeToElement(new { member = CanonicalId.Mint().Value }))]);

        var error = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(cache, invalid));

        Assert.Contains("Target", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ActivationGuard_RejectsAnUnsupportedIterationContext()
    {
        using var cache = pristine.NewScratch();
        IPhRegularRule rule = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            rule = cache.ServiceLocator.GetInstance<IPhRegularRuleFactory>().Create();
            cache.LangProject.PhonologicalDataOA.PhonRulesOS.Add(rule);
            var rhs = rule.RightHandSidesOS.FirstOrDefault();
            if (rhs is null)
            {
                rhs = cache.ServiceLocator.GetInstance<IPhSegRuleRHSFactory>().Create();
                rule.RightHandSidesOS.Add(rhs);
            }
            rhs.LeftContextOA = cache.ServiceLocator.GetInstance<IPhIterationContextFactory>().Create();
            rhs.RightContextOA = cache.ServiceLocator.GetInstance<IPhSimpleContextBdryFactory>().Create();
        });
        Assert.NotEmpty(rule.RightHandSidesOS);
        Assert.NotNull(Assert.Single(rule.RightHandSidesOS).LeftContextOA);
        Assert.NotNull(Assert.Single(rule.RightHandSidesOS).RightContextOA);

        var error = Assert.Throws<InvalidOperationException>(() => RegularRuleActivationGuard.RequireComplete(rule));

        Assert.Contains("not supported", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ContextProposal_RejectsAnIncompleteSegmentReference()
    {
        using var cache = pristine.NewScratch();
        var built = BuildProposal(cache, AddSoundSystem(cache));
        var operations = built.Proposal.Operations.Where(operation =>
            !(operation.Kind == PhSimpleContextSegFeatureStructureOperationKinds.SetFeatureStructure &&
              operation.Target is { } target && target == built.Proposal.Operations.Single(candidate =>
                  candidate.Kind == PhSegmentRuleStrucDescOperationKinds.Create).EntityId)).ToArray();
        var incomplete = WithOperations(built.Proposal, operations);

        var error = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(cache, incomplete));

        Assert.Contains("segment context must reference a phoneme", error.Message, StringComparison.Ordinal);
    }

    private static (CanonicalId PhonemeN, CanonicalId PhonemeM, CanonicalId NaturalClass, CanonicalId Boundary) AddSoundSystem(LcmCache cache)
    {
        var pair = Feature(cache);
        var first = AuthorPhonemeComposer.Build(cache, new("n", ["n"], [pair]));
        Execute(cache, first);
        var phonemeN = first.Single(operation => operation.Kind == PhPhonemeSetPhonemesOperationKinds.Create).EntityId!.Value;
        var boundary = CanonicalId.FromGuid(LangProjectTags.kguidPhRuleMorphBdry);
        var second = AuthorPhonemeComposer.Build(cache, new("m", ["m"], [pair]));
        Execute(cache, second);
        var phonemeM = second.Single(operation => operation.Kind == PhPhonemeSetPhonemesOperationKinds.Create).EntityId!.Value;
        var classOperations = AuthorNaturalClassComposer.Build(cache,
            new("labials", "LAB", Features: [pair]));
        Execute(cache, classOperations);
        var naturalClass = classOperations.Single(operation => operation.Kind == PhPhonDataNaturalClassesOperationKinds.Create).EntityId!.Value;
        return (phonemeN, phonemeM, naturalClass, boundary);
    }

    private static CanonicalId AddBoundaryMarker(LcmCache cache, Guid guid, string code)
    {
        IPhBdryMarker boundary = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var set = cache.LangProject.PhonologicalDataOA.PhonemeSetsOS.Single();
            boundary = set.BoundaryMarkersOC.SingleOrDefault(marker => marker.Guid == guid)!;
            if (boundary is null)
            {
                boundary = cache.ServiceLocator.GetInstance<IPhBdryMarkerFactory>().Create(guid);
                set.BoundaryMarkersOC.Add(boundary);
            }
            boundary.Name.set_String(cache.DefaultVernWs, code);
            var phoneticCode = boundary.CodesOS.FirstOrDefault() ?? cache.ServiceLocator.GetInstance<IPhCodeFactory>().Create();
            if (!boundary.CodesOS.Contains(phoneticCode)) boundary.CodesOS.Add(phoneticCode);
            phoneticCode.Representation.set_String(cache.DefaultVernWs, code);
        });
        return CanonicalId.FromGuid(boundary.Guid);
    }

    private static Guid ContextReference(IPhPhonContext context) => context switch
    {
        IPhSimpleContextSeg segment => segment.FeatureStructureRA!.Guid,
        IPhSimpleContextNC naturalClass => naturalClass.FeatureStructureRA!.Guid,
        IPhSimpleContextBdry boundary => boundary.FeatureStructureRA!.Guid,
        _ => throw new InvalidOperationException("The context sequence contains an unsupported member."),
    };

    private static PhonologicalFeatureValue Feature(LcmCache cache)
    {
        IFsClosedFeature feature = null!;
        IFsSymFeatVal value = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            feature = cache.ServiceLocator.GetInstance<IFsClosedFeatureFactory>().Create();
            cache.LangProject.PhFeatureSystemOA.FeaturesOC.Add(feature);
            value = cache.ServiceLocator.GetInstance<IFsSymFeatValFactory>().Create();
            feature.ValuesOC.Add(value);
        });
        return new(CanonicalId.FromGuid(feature.Guid), CanonicalId.FromGuid(value.Guid));
    }

    private static ContextProposal BuildProposal(LcmCache cache,
        (CanonicalId PhonemeN, CanonicalId PhonemeM, CanonicalId NaturalClass, CanonicalId Boundary) sound,
        bool reuseRightSequence = false)
    {
        var data = cache.LangProject.PhonologicalDataOA;
        var operations = new List<OperationEnvelope>();
        var poolSegment = Create(PhPhonDataContextsOperationKinds.Create, data.Guid, "PhSimpleContextSeg");
        var poolBoundary = Create(PhPhonDataContextsOperationKinds.Create, data.Guid, "PhSimpleContextBdry");
        var poolClass = Create(PhPhonDataContextsOperationKinds.Create, data.Guid, "PhSimpleContextNC");
        var poolSequence = Create(PhPhonDataContextsOperationKinds.Create, data.Guid, "PhSequenceContext");
        var poolSequenceSegment = Create(PhPhonDataContextsOperationKinds.Create, data.Guid, "PhSimpleContextSeg");
        var poolSequenceBoundary = Create(PhPhonDataContextsOperationKinds.Create, data.Guid, "PhSimpleContextBdry");
        operations.AddRange([poolSegment, poolBoundary, poolClass, poolSequence, poolSequenceSegment, poolSequenceBoundary]);
        operations.Add(Set(PhSimpleContextSegFeatureStructureOperationKinds.SetFeatureStructure, poolSegment,
            sound.PhonemeN));
        operations.Add(Set(PhSimpleContextBdryFeatureStructureOperationKinds.SetFeatureStructure, poolBoundary,
            sound.Boundary));
        operations.Add(Set(PhSimpleContextNCFeatureStructureOperationKinds.SetFeatureStructure, poolClass,
            sound.NaturalClass));
        operations.Add(Set(PhSimpleContextSegFeatureStructureOperationKinds.SetFeatureStructure, poolSequenceSegment,
            sound.PhonemeM));
        operations.Add(Set(PhSimpleContextBdryFeatureStructureOperationKinds.SetFeatureStructure, poolSequenceBoundary,
            sound.Boundary));

        var poolSequenceFirst = AddMember(poolSequence, poolSequenceSegment, null);
        var poolSequenceSecond = AddMember(poolSequence, poolSequenceBoundary, poolSequenceSegment.EntityId, poolSequenceSegment);
        operations.Add(poolSequenceFirst);
        operations.Add(poolSequenceSecond);

        var rule = Create(PhPhonDataPhonRulesOperationKinds.Create, data.Guid, "PhRegularRule");
        var rhs = Create(PhRegularRuleRightHandSidesOperationKinds.Create, rule.EntityId!.Value.ToGuid(), null,
            Depends(rule));
        var input = Create(PhSegmentRuleStrucDescOperationKinds.Create, rule.EntityId.Value.ToGuid(), "PhSimpleContextSeg",
            Depends(rule));
        var output = Create(PhSegRuleRHSStrucChangeOperationKinds.Create, rhs.EntityId!.Value.ToGuid(), "PhSimpleContextSeg",
            Depends(rhs));
        var left = Create(PhSegRuleRHSLeftContextOperationKinds.Create, rhs.EntityId.Value.ToGuid(), "PhSequenceContext",
            Depends(rhs));
        var right = Create(PhSegRuleRHSRightContextOperationKinds.Create, rhs.EntityId.Value.ToGuid(),
            reuseRightSequence ? "PhSequenceContext" : "PhSimpleContextNC",
            Depends(rhs));
        operations.AddRange([rule, rhs, input, output, left, right]);
        operations.Add(Set(PhSimpleContextSegFeatureStructureOperationKinds.SetFeatureStructure, input, sound.PhonemeN));
        operations.Add(Set(PhSimpleContextSegFeatureStructureOperationKinds.SetFeatureStructure, output, sound.PhonemeM));
        if (!reuseRightSequence)
            operations.Add(Set(PhSimpleContextNCFeatureStructureOperationKinds.SetFeatureStructure, right, sound.NaturalClass));
        operations.Add(AddMember(left, poolSegment, null));
        operations.Add(AddMember(left, poolBoundary, poolSegment.EntityId, poolSegment));
        if (reuseRightSequence)
        {
            operations.Add(AddMember(right, poolSegment, null));
            operations.Add(AddMember(right, poolBoundary, poolSegment.EntityId, poolSegment));
        }
        return new(Proposal(operations), rule.EntityId.Value, poolSegment.EntityId!.Value,
            poolBoundary.EntityId!.Value, left.EntityId!.Value, poolSequence.EntityId!.Value,
            poolSequenceSegment.EntityId!.Value, poolSequenceBoundary.EntityId!.Value);
    }

    private static OperationEnvelope Create(string kind, Guid target, string? concreteClass,
        IReadOnlyList<OperationDependency>? dependsOn = null)
    {
        var after = concreteClass is null
            ? JsonSerializer.SerializeToElement(new { })
            : JsonSerializer.SerializeToElement(new { @class = concreteClass });
        return new(CanonicalId.Mint(), kind, CanonicalId.Mint(), CanonicalId.FromGuid(target), after,
            dependsOn: dependsOn?.ToArray());
    }

    private static OperationEnvelope MoveRule(CanonicalId data, CanonicalId member, CanonicalId after) =>
        new(CanonicalId.Mint(), "grammar/phPhonData/movePhonRules", target: data,
            after: JsonSerializer.SerializeToElement(new { member = member.Value }),
            placement: new Placement(after, null));

    private static OperationEnvelope Set(string kind, OperationEnvelope create, CanonicalId reference) =>
        new(CanonicalId.Mint(), kind, target: create.EntityId,
            after: JsonSerializer.SerializeToElement(new { @ref = reference.Value }),
            dependsOn: Depends(create).ToArray());

    private static OperationEnvelope AddMember(OperationEnvelope sequence, OperationEnvelope member,
        CanonicalId? afterMember, params OperationEnvelope[] placementDependencies) =>
        new(CanonicalId.Mint(), PhSequenceContextMembersOperationKinds.AddRefMembers,
            target: sequence.EntityId, after: JsonSerializer.SerializeToElement(new { member = member.EntityId!.Value.Value }),
            placement: afterMember is { } previous ? new Placement(previous, null) : null,
            dependsOn: Depends([sequence, member, .. placementDependencies]).ToArray());

    private static OperationEnvelope AddMember(OperationEnvelope sequence, CanonicalId member,
        CanonicalId? afterMember, params OperationEnvelope[] placementDependencies) =>
        new(CanonicalId.Mint(), PhSequenceContextMembersOperationKinds.AddRefMembers,
            target: sequence.EntityId, after: JsonSerializer.SerializeToElement(new { member = member.Value }),
            placement: afterMember is { } previous ? new Placement(previous, null) : null,
            dependsOn: Depends([sequence, .. placementDependencies]).ToArray());

    private static IReadOnlyList<OperationDependency> Depends(params OperationEnvelope[] operations) =>
        operations.Select(operation => new OperationDependency(operation.OperationId)).ToArray();

    private static Proposal Proposal(IReadOnlyList<OperationEnvelope> operations) =>
        new(new Dictionary<string, string> { ["grammar"] = "1.0" }, CanonicalId.Mint(), null, operations);

    private static Proposal WithOperations(Proposal proposal, IReadOnlyList<OperationEnvelope> operations) =>
        new(proposal.ContractVersions, proposal.ProposalId, proposal.Requires, operations);

    private static void Execute(LcmCache cache, IReadOnlyList<OperationEnvelope> operations) =>
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            foreach (var operation in operations)
                OperationHandlerRegistry.Resolve(operation.Kind, "rewrite-context fixture")
                    .ApplyAndCaptureEffect(cache, operation, []);
        });

    private sealed record ContextProposal(Proposal Proposal, CanonicalId Rule, CanonicalId PoolSegment,
        CanonicalId PoolBoundary, CanonicalId LeftContext, CanonicalId PoolSequence,
        CanonicalId PoolSequenceSegment, CanonicalId PoolSequenceBoundary);
}

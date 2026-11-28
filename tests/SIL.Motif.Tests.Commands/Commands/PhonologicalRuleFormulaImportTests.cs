using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands.Records;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using SIL.TextEditing.Markdown.Records;
using SIL.TextEditing.Project;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>A regular rule's structural description, change and contexts import as rule-formula text, position by position, keeping the objects whose kind is unchanged.</summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class PhonologicalRuleFormulaImportTests(PristineProjectFixture pristine)
{
    private string Start(out Guid ruleId, out Guid swapId)
    {
        using var cache = pristine.NewScratch();
        PhonologicalRuleRecordImportTests.Build(cache);
        new FwDataProjectLoader().Save(cache);
        ruleId = cache.LangProject.PhonologicalDataOA.PhonRulesOS.OfType<IPhRegularRule>().Single().Guid;
        swapId = cache.LangProject.PhonologicalDataOA.PhonRulesOS.OfType<IPhMetathesisRule>().Single().Guid;
        return cache.ProjectId.Path;
    }

    private static RecordObject Rhs(RecordDocument document) => PhonologicalRuleRecordImportTests.Rhs(document);

    private static string[] Kinds(RecordLowering lowering) => lowering.Operations.Select(operation => operation.Kind).ToArray();

    private static string Text(LcmCache cache, Guid ruleId, RuleField field)
    {
        var rule = (IPhRegularRule)cache.ServiceLocator.GetObject(ruleId);
        return RuleNotation.Render(field == RuleField.StrucDesc ? rule : rule.RightHandSidesOS.Single(), field, cache);
    }

    private static void ImportRight(string path, string draft, Guid ruleId, string? text) =>
        PhonologicalRuleRecordImportTests.Import(path, draft, ruleId, document =>
        {
            if (text is null) Rhs(document)["right-context"] = null;
            else Rhs(document).Set("right-context", new RecordScalar(text));
        });

    private static void AssertNothingLeftToChange(string path, Guid ruleId)
    {
        using var cache = new FwDataProjectLoader().LoadCache(path);
        Assert.Empty(PhonologicalRuleRecordImportTests.PlanEdit(cache, ruleId, _ => { }, out _).ChangeSet.Changes);
    }

    [Fact]
    public void AFeatureEditKeepsTheObjectAndAnAppendKeepsTheEarlierOnes()
    {
        var path = Start(out var ruleId, out _);
        Guid first;
        using (var cache = new FwDataProjectLoader().LoadCache(path))
        {
            first = ((IPhRegularRule)cache.ServiceLocator.GetObject(ruleId)).StrucDescOS.Single().Guid;
            var lowering = PhonologicalRuleRecordImportTests.Lower(cache, ruleId, document => document.Data.Set("struc-desc", new RecordScalar("t")));
            Assert.Empty(lowering.Refusals);
            Assert.Equal(new[] { "grammar/phSimpleContextSeg/setFeatureStructure" }, Kinds(lowering));
            Assert.Equal(first, lowering.Operations.Single().Target!.Value.ToGuid());
        }
        PhonologicalRuleRecordImportTests.Import(path, "edit", ruleId, document => document.Data.Set("struc-desc", new RecordScalar("t")));
        PhonologicalRuleRecordImportTests.Import(path, "append", ruleId, document => document.Data.Set("struc-desc", new RecordScalar("t k")));
        using var held = new FwDataProjectLoader().LoadCache(path);
        var rule = (IPhRegularRule)held.ServiceLocator.GetObject(ruleId);
        Assert.Equal(2, rule.StrucDescOS.Count);
        Assert.Equal(first, rule.StrucDescOS[0].Guid);
        Assert.Equal("t k", Text(held, ruleId, RuleField.StrucDesc));
        AssertNothingLeftToChange(path, ruleId);
    }

    [Fact]
    public void AKindChangeAtOnePositionRecreatesOnlyThatContext()
    {
        var path = Start(out var ruleId, out _);
        PhonologicalRuleRecordImportTests.Import(path, "two", ruleId, document => document.Data.Set("struc-desc", new RecordScalar("p t")));
        Guid first, second;
        using (var cache = new FwDataProjectLoader().LoadCache(path))
        {
            var rule = (IPhRegularRule)cache.ServiceLocator.GetObject(ruleId);
            (first, second) = (rule.StrucDescOS[0].Guid, rule.StrucDescOS[1].Guid);
            Assert.Equal(new[] { "grammar/phSegmentRule/createStrucDesc", "grammar/phSegmentRule/deleteContexts", "grammar/phSimpleContextNC/setFeatureStructure" },
                Kinds(PhonologicalRuleRecordImportTests.Lower(cache, ruleId, document => document.Data.Set("struc-desc", new RecordScalar("[C] t")))).Order(StringComparer.Ordinal));
        }
        PhonologicalRuleRecordImportTests.Import(path, "kind", ruleId, document => document.Data.Set("struc-desc", new RecordScalar("[C] t")));
        using var held = new FwDataProjectLoader().LoadCache(path);
        var after = ((IPhRegularRule)held.ServiceLocator.GetObject(ruleId)).StrucDescOS;
        Assert.IsAssignableFrom<IPhSimpleContextNC>(after[0]);
        Assert.NotEqual(first, after[0].Guid);
        Assert.Equal(second, after[1].Guid);
        Assert.Null(held.ServiceLocator.ObjectRepository.TryGetObject(first, out _) ? "alive" : null);
        AssertNothingLeftToChange(path, ruleId);
    }

    [Fact]
    public void ABracketWithNewFeaturesIsAFreshClassAndNoClassIsEverDeleted()
    {
        var path = Start(out var ruleId, out _);
        int classes;
        Guid change;
        using (var cache = new FwDataProjectLoader().LoadCache(path))
        {
            classes = cache.LangProject.PhonologicalDataOA.NaturalClassesOS.Count;
            change = ((IPhRegularRule)cache.ServiceLocator.GetObject(ruleId)).RightHandSidesOS.Single().StrucChangeOS.Single().Guid;
        }
        PhonologicalRuleRecordImportTests.Import(path, "plus", ruleId, document => Rhs(document).Set("struc-change", new RecordScalar("[voi:+]")));
        PhonologicalRuleRecordImportTests.Import(path, "minus", ruleId, document => Rhs(document).Set("struc-change", new RecordScalar("[voi:-]")));
        using (var cache = new FwDataProjectLoader().LoadCache(path))
        {
            var data = cache.LangProject.PhonologicalDataOA;
            Assert.Equal(classes + 2, data.NaturalClassesOS.Count);
            Assert.Equal("[voi:-]", Text(cache, ruleId, RuleField.StrucChange));
            var kept = ((IPhRegularRule)cache.ServiceLocator.GetObject(ruleId)).RightHandSidesOS.Single().StrucChangeOS.Single();
            Assert.IsAssignableFrom<IPhSimpleContextNC>(kept);
            Assert.NotEqual(change, kept.Guid);
            var fresh = data.NaturalClassesOS.OfType<IPhNCFeatures>().Where(item => item.Name.get_String(cache.DefaultAnalWs).Text?.StartsWith("Created automatically for rule \"Voicing\"") == true).ToList();
            Assert.Equal(2, fresh.Count);
        }
        using var again = new FwDataProjectLoader().LoadCache(path);
        var lowering = PhonologicalRuleRecordImportTests.Lower(again, ruleId, document => Rhs(document).Set("struc-change", new RecordScalar("[voi:+]")));
        Assert.Empty(lowering.Refusals);
        Assert.DoesNotContain(Kinds(lowering), kind => kind.Contains("delete", StringComparison.OrdinalIgnoreCase) && kind.Contains("NaturalClass", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(new[] { "grammar/phSimpleContextNC/setFeatureStructure" }, Kinds(lowering).Where(kind => kind.StartsWith("grammar/phSimpleContext")).ToArray());
        AssertNothingLeftToChange(path, ruleId);
    }

    [Fact]
    public void SequenceAndRepeatEditsKeepWhatTheirKindsKeepAndLeaveNoOrphanedContext()
    {
        var path = Start(out var ruleId, out _);
        ImportRight(path, "sequence", ruleId, "[V] a");
        Guid sequence, member0, member1;
        using (var cache = new FwDataProjectLoader().LoadCache(path))
        {
            var held = ((IPhSequenceContext)((IPhRegularRule)cache.ServiceLocator.GetObject(ruleId)).RightHandSidesOS.Single().RightContextOA!);
            (sequence, member0, member1) = (held.Guid, held.MembersRS[0].Guid, held.MembersRS[1].Guid);
            Assert.Equal("[V] a", Text(cache, ruleId, RuleField.RightContext));
            Assert.Equal(2, cache.LangProject.PhonologicalDataOA.ContextsOS.Count);
            Assert.Equal(new[] { "grammar/phSimpleContextSeg/setFeatureStructure" },
                Kinds(PhonologicalRuleRecordImportTests.Lower(cache, ruleId, document => Rhs(document).Set("right-context", new RecordScalar("[V] i")))));
        }
        ImportRight(path, "in-place", ruleId, "[V] i");
        ImportRight(path, "append", ruleId, "[V] i u");
        ImportRight(path, "repeat", ruleId, "[C]{1,3} i u");
        Guid iteration;
        using (var cache = new FwDataProjectLoader().LoadCache(path))
        {
            var held = (IPhSequenceContext)((IPhRegularRule)cache.ServiceLocator.GetObject(ruleId)).RightHandSidesOS.Single().RightContextOA!;
            Assert.Equal(sequence, held.Guid);
            Assert.Equal(3, held.MembersRS.Count);
            var repeated = Assert.IsAssignableFrom<IPhIterationContext>(held.MembersRS[0]);
            iteration = repeated.Guid;
            Assert.NotEqual(member0, iteration);
            Assert.Equal(member1, held.MembersRS[1].Guid);
            Assert.Equal("[C]{1,3} i u", Text(cache, ruleId, RuleField.RightContext));
            Assert.Equal(new[] { "grammar/phIterationContext/setBounds" },
                Kinds(PhonologicalRuleRecordImportTests.Lower(cache, ruleId, document => Rhs(document).Set("right-context", new RecordScalar("[C]{2,3} i u")))));
        }
        ImportRight(path, "bounds", ruleId, "[C]{2,3} i u");
        using (var cache = new FwDataProjectLoader().LoadCache(path))
        {
            var held = (IPhSequenceContext)((IPhRegularRule)cache.ServiceLocator.GetObject(ruleId)).RightHandSidesOS.Single().RightContextOA!;
            Assert.Equal(iteration, held.MembersRS[0].Guid);
            Assert.Equal(2, ((IPhIterationContext)held.MembersRS[0]).Minimum);
        }
        ImportRight(path, "drop", ruleId, "[C]{2,3} i");
        ImportRight(path, "bare", ruleId, "i");
        using (var cache = new FwDataProjectLoader().LoadCache(path))
        {
            var held = ((IPhRegularRule)cache.ServiceLocator.GetObject(ruleId)).RightHandSidesOS.Single().RightContextOA!;
            Assert.IsAssignableFrom<IPhSimpleContextSeg>(held);
            Assert.NotEqual(sequence, held.Guid);
            Assert.Empty(cache.LangProject.PhonologicalDataOA.ContextsOS);
        }
        ImportRight(path, "optional", ruleId, "(i)");
        using (var cache = new FwDataProjectLoader().LoadCache(path))
        {
            var held = Assert.IsAssignableFrom<IPhIterationContext>(((IPhRegularRule)cache.ServiceLocator.GetObject(ruleId)).RightHandSidesOS.Single().RightContextOA);
            Assert.Equal((0, 1), (held.Minimum, held.Maximum));
            Assert.Single(cache.LangProject.PhonologicalDataOA.ContextsOS);
            Assert.Equal("(i)", Text(cache, ruleId, RuleField.RightContext));
        }
        AssertNothingLeftToChange(path, ruleId);
        ImportRight(path, "clear", ruleId, null);
        using var cleared = new FwDataProjectLoader().LoadCache(path);
        Assert.Null(((IPhRegularRule)cleared.ServiceLocator.GetObject(ruleId)).RightHandSidesOS.Single().RightContextOA);
        Assert.Empty(cleared.LangProject.PhonologicalDataOA.ContextsOS);
        Assert.Equal(string.Empty, Text(cleared, ruleId, RuleField.RightContext));
    }

    [Fact]
    public void AWordBoundaryInAStructuralChangeAndARepeatedBoundaryAreRefusedAndTheirSiblingsWithThem()
    {
        var path = Start(out var ruleId, out _);
        using var cache = new FwDataProjectLoader().LoadCache(path);
        void Refused(string code, Action<RecordDocument> edit)
        {
            var lowering = PhonologicalRuleRecordImportTests.Lower(cache, ruleId, edit);
            Assert.Empty(lowering.Operations);
            Assert.Equal(code, lowering.Refusals.First().Code);
        }
        Refused(RefusalCodes.RuleNotationContextNotAllowed, document => document.Data.Set("struc-desc", new RecordScalar("p #")));
        Refused(RefusalCodes.RuleNotationContextNotAllowed, document => Rhs(document).Set("struc-change", new RecordScalar("# b")));
        Refused(RefusalCodes.RuleNotationContextNotAllowed, document => Rhs(document).Set("right-context", new RecordScalar("+{0,1}")));
        Refused(RefusalCodes.RuleNotationContextNotAllowed, document => Rhs(document).Set("right-context", new RecordScalar("(+)")));
        Refused(RefusalCodes.RuleNotationLimitExceeded, document => Rhs(document).Set("right-context", new RecordScalar("a a a a a a a a a")));
        var sibling = PhonologicalRuleRecordImportTests.Lower(cache, ruleId, document =>
        {
            document.Data.Set("struc-desc", new RecordScalar("t"));
            Rhs(document).Set("right-context", new RecordScalar("+{0,1}"));
        });
        Assert.Empty(sibling.Operations);
        Assert.Equal(2, sibling.Refusals.Count);
        Assert.Contains(sibling.Refusals, refusal => refusal.Field == "struc-desc");
        Assert.All(sibling.Refusals, refusal => Assert.Equal(RefusalCodes.RuleNotationContextNotAllowed, refusal.Code));
        var word = PhonologicalRuleRecordImportTests.Lower(cache, ruleId, document => Rhs(document).Set("right-context", new RecordScalar("a #")));
        Assert.Empty(word.Refusals);
    }

    [Fact]
    public void AStaleFieldIsRefusedAndAMetathesisFormulaIsNotOffered()
    {
        var path = Start(out var ruleId, out _);
        using var cache = new FwDataProjectLoader().LoadCache(path);
        var plan = PhonologicalRuleRecordImportTests.PlanEdit(cache, ruleId, document => document.Data.Set("struc-desc", new RecordScalar("t")), out var schema);
        var lowerer = new RecordChangeSetLowerer(schema, new RecordWritableFields(schema));
        Assert.Empty(lowerer.Lower(cache, plan).Refusals);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var rule = (IPhRegularRule)cache.ServiceLocator.GetObject(ruleId);
            rule.StrucDescOS.OfType<IPhSimpleContextSeg>().Single().FeatureStructureRA = cache.LangProject.PhonologicalDataOA.PhonemeSetsOS[0].PhonemesOC
                .First(item => item.CodesOS[0].Representation.get_String(cache.DefaultVernWs).Text == "k");
        });
        Assert.Equal(RefusalCodes.RecordImportValueStale, lowerer.Lower(cache, plan).Refusals.Single().Code);
        var catalog = new RecordWritableFields(schema);
        Assert.IsType<WritableFieldResolution.Covered>(catalog.Resolve("PhRegularRule", "struc-desc", RecordWriteVerb.Set, "PhSimpleContextSeg"));
        Assert.IsType<WritableFieldResolution.Uncovered>(catalog.Resolve("PhMetathesisRule", "struc-desc", RecordWriteVerb.Set, "PhSimpleContextSeg"));
    }

    [Fact]
    public void ANamedContextIsKeptThroughAnEditOfItsKindAndRefusedWhenTheEditWouldDeleteIt()
    {
        var path = Start(out var ruleId, out _);
        using (var cache = new FwDataProjectLoader().LoadCache(path))
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                ((IPhRegularRule)cache.ServiceLocator.GetObject(ruleId)).RightHandSidesOS.Single().LeftContextOA!.Name
                    .set_String(cache.DefaultAnalWs, TsStringUtils.MakeString("near", cache.DefaultAnalWs)));
            new FwDataProjectLoader().Save(cache);
        }
        using var held = new FwDataProjectLoader().LoadCache(path);
        var shape = PhonologicalRuleRecordImportTests.Lower(held, ruleId, document => Rhs(document).Set("left-context", new RecordScalar("a i")));
        Assert.Empty(shape.Operations);
        Assert.Equal(RefusalCodes.RuleNotationContextHasData, shape.Refusals.Single().Code);
        var kind = PhonologicalRuleRecordImportTests.Lower(held, ruleId, document => Rhs(document).Set("left-context", new RecordScalar("a")));
        Assert.Equal(RefusalCodes.RuleNotationContextHasData, kind.Refusals.Single().Code);
        var kept = PhonologicalRuleRecordImportTests.Lower(held, ruleId, document => Rhs(document).Set("left-context", new RecordScalar("[C]")));
        Assert.Empty(kept.Refusals);
        Assert.Equal(new[] { "grammar/phSimpleContextNC/setFeatureStructure" }, Kinds(kept));
    }

    [Fact]
    public void AContextAnotherObjectReferencesIsNotChanged()
    {
        var path = Start(out var ruleId, out _);
        using (var cache = new FwDataProjectLoader().LoadCache(path))
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var data = cache.LangProject.PhonologicalDataOA;
                var other = cache.ServiceLocator.GetInstance<IPhSequenceContextFactory>().Create();
                data.ContextsOS.Add(other);
                other.MembersRS.Add(((IPhRegularRule)cache.ServiceLocator.GetObject(ruleId)).RightHandSidesOS.Single().LeftContextOA!);
            });
            new FwDataProjectLoader().Save(cache);
        }
        using var held = new FwDataProjectLoader().LoadCache(path);
        var lowering = PhonologicalRuleRecordImportTests.Lower(held, ruleId, document => Rhs(document).Set("left-context", new RecordScalar("[C]")));
        Assert.Empty(lowering.Operations);
        Assert.Equal(RefusalCodes.RuleNotationSharedClass, lowering.Refusals.Single().Code);
    }

    private static IPhSequenceContext RightSequence(LcmCache cache, Guid ruleId) =>
        (IPhSequenceContext)((IPhRegularRule)cache.ServiceLocator.GetObject(ruleId)).RightHandSidesOS.Single().RightContextOA!;

    private string StartWithSequence(out Guid ruleId, out Guid member, Action<LcmCache, IPhPhonContext> alter)
    {
        var path = Start(out ruleId, out _);
        ImportRight(path, "sequence", ruleId, "[V] a i");
        using var cache = new FwDataProjectLoader().LoadCache(path);
        var last = RightSequence(cache, ruleId).MembersRS[2];
        member = last.Guid;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () => alter(cache, last));
        new FwDataProjectLoader().Save(cache);
        return path;
    }

    private static void AlsoUsedBy(LcmCache cache, IPhPhonContext member)
    {
        var other = cache.ServiceLocator.GetInstance<IPhSequenceContextFactory>().Create();
        cache.LangProject.PhonologicalDataOA.ContextsOS.Add(other);
        other.MembersRS.Add(member);
    }

    private static void Named(LcmCache cache, IPhPhonContext member) =>
        member.Name.set_String(cache.DefaultAnalWs, TsStringUtils.MakeString("kept", cache.DefaultAnalWs));

    [Theory]
    [InlineData(true, "a", RefusalCodes.RuleNotationSharedClass)]
    [InlineData(true, "[V] a", RefusalCodes.RuleNotationSharedClass)]
    [InlineData(false, "a", RefusalCodes.RuleNotationContextHasData)]
    [InlineData(false, "[V] a", RefusalCodes.RuleNotationContextHasData)]
    public void ASharedOrNamedPooledMemberIsSparedWhenAShapeChangeOrADroppedMemberWouldDeleteIt(bool shared, string edited, string code)
    {
        var path = StartWithSequence(out var ruleId, out var member, shared ? AlsoUsedBy : Named);
        using var cache = new FwDataProjectLoader().LoadCache(path);
        var lowering = PhonologicalRuleRecordImportTests.Lower(cache, ruleId, document => Rhs(document).Set("right-context", new RecordScalar(edited)));
        Assert.Empty(lowering.Operations);
        Assert.Equal(code, lowering.Refusals.First().Code);
        var survivor = cache.ServiceLocator.GetObject(member);
        Assert.IsAssignableFrom<IPhSimpleContextSeg>(survivor);
        Assert.Equal(member, RightSequence(cache, ruleId).MembersRS[2].Guid);
    }

    [Fact]
    public void AMemberThatBecomesSharedAfterLoweringIsNotDeletedWhenTheOperationApplies()
    {
        var path = Start(out var ruleId, out _);
        ImportRight(path, "sequence", ruleId, "[V] a");
        using var cache = new FwDataProjectLoader().LoadCache(path);
        var lowering = PhonologicalRuleRecordImportTests.Lower(cache, ruleId, document => Rhs(document).Set("right-context", new RecordScalar("a")));
        Assert.Empty(lowering.Refusals);
        var delete = lowering.Operations.Single(operation => operation.Kind == PhonologicalRuleDeleteOperationKinds.DeleteContexts);
        var member = RightSequence(cache, ruleId).MembersRS[1];
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () => AlsoUsedBy(cache, member));
        var refused = Assert.IsAssignableFrom<SIL.Motif.Runner.Execution.ProposalRefusalException>(Record.Exception(() => NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            OperationHandlerRegistry.Resolve(delete.Kind, "apply-time guard test").ApplyAndCaptureEffect(cache, delete, []))));
        Assert.Equal(RefusalCodes.RuleNotationContextInUse, refused.Code);
        Assert.Contains("still used", refused.Message);
        Assert.NotNull(cache.ServiceLocator.GetObject(member.Guid));
        Assert.Equal(2, RightSequence(cache, ruleId).MembersRS.Count);
    }

    [Fact]
    public void ALeftContextAddedWhereThereWasNoneIsCreatedAsAFreshContext()
    {
        var path = Start(out var ruleId, out _);
        PhonologicalRuleRecordImportTests.Import(path, "clear-left", ruleId, document => Rhs(document)["left-context"] = null);
        using (var cache = new FwDataProjectLoader().LoadCache(path))
        {
            Assert.Null(((IPhRegularRule)cache.ServiceLocator.GetObject(ruleId)).RightHandSidesOS.Single().LeftContextOA);
            var lowering = PhonologicalRuleRecordImportTests.Lower(cache, ruleId, document => Rhs(document).Set("left-context", new RecordScalar("[C] a")));
            Assert.Empty(lowering.Refusals);
            Assert.Contains("grammar/phSegRuleRHS/createLeftContext", Kinds(lowering));
        }
        PhonologicalRuleRecordImportTests.Import(path, "add-left", ruleId, document => Rhs(document).Set("left-context", new RecordScalar("[C] a")));
        using var after = new FwDataProjectLoader().LoadCache(path);
        Assert.Equal("[C] a", Text(after, ruleId, RuleField.LeftContext));
        AssertNothingLeftToChange(path, ruleId);
    }
}

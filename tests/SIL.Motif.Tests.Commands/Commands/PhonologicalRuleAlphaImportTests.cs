using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands.Records;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Runner.Execution;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using SIL.TextEditing.Markdown.Records;
using SIL.TextEditing.Project;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>Alpha variables in a regular rule's formula text become the rule's feature constraints: found or made once per feature, moved between contexts, and collected only when nothing refers to them.</summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class PhonologicalRuleAlphaImportTests(PristineProjectFixture pristine)
{
    private const string Collect = "grammar/phRegularRule/collectUnusedFeatConstraints";

    private string Start(out Guid ruleId)
    {
        using var cache = pristine.NewScratch();
        PhonologicalRuleRecordImportTests.Build(cache);
        new FwDataProjectLoader().Save(cache);
        ruleId = cache.LangProject.PhonologicalDataOA.PhonRulesOS.OfType<IPhRegularRule>().Single().Guid;
        return cache.ProjectId.Path;
    }

    private static RecordObject Rhs(RecordDocument document) => PhonologicalRuleRecordImportTests.Rhs(document);

    private static string[] Kinds(RecordLowering lowering) => lowering.Operations.Select(operation => operation.Kind).ToArray();

    private static string Text(LcmCache cache, Guid ruleId, RuleField field)
    {
        var rule = (IPhRegularRule)cache.ServiceLocator.GetObject(ruleId);
        return RuleNotation.Render(field == RuleField.StrucDesc ? rule : rule.RightHandSidesOS.Single(), field, cache);
    }

    private static IPhRegularRule Rule(LcmCache cache, Guid ruleId) => (IPhRegularRule)cache.ServiceLocator.GetObject(ruleId);

    private static IPhSimpleContextNC Left(LcmCache cache, Guid ruleId) =>
        (IPhSimpleContextNC)Rule(cache, ruleId).RightHandSidesOS.Single().LeftContextOA!;

    private static void Edit(string path, string draft, Guid ruleId, string? left = null, string? right = null, string? desc = null, string? change = null) =>
        PhonologicalRuleRecordImportTests.Import(path, draft, ruleId, document =>
        {
            if (left is not null) Rhs(document).Set("left-context", new RecordScalar(left));
            if (right is not null) Rhs(document).Set("right-context", new RecordScalar(right));
            if (change is not null) Rhs(document).Set("struc-change", new RecordScalar(change));
            if (desc is not null) document.Data.Set("struc-desc", new RecordScalar(desc));
        });

    private static RecordLowering Lower(LcmCache cache, Guid ruleId, string? left = null, string? right = null, string? desc = null) =>
        PhonologicalRuleRecordImportTests.Lower(cache, ruleId, document =>
        {
            if (left is not null) Rhs(document).Set("left-context", new RecordScalar(left));
            if (right is not null) Rhs(document).Set("right-context", new RecordScalar(right));
            if (desc is not null) document.Data.Set("struc-desc", new RecordScalar(desc));
        });

    private static void AssertNothingLeftToChange(string path, Guid ruleId)
    {
        using var cache = new FwDataProjectLoader().LoadCache(path);
        Assert.Empty(PhonologicalRuleRecordImportTests.PlanEdit(cache, ruleId, _ => { }, out _).ChangeSet.Changes);
    }

    private static IFsClosedFeature Feature(LcmCache cache, string abbreviation) =>
        cache.LangProject.PhFeatureSystemOA.FeaturesOC.OfType<IFsClosedFeature>()
            .Single(feature => feature.Abbreviation.get_String(cache.DefaultAnalWs).Text == abbreviation);

    [Fact]
    public void OneAlphaVariableInTwoFieldsMakesOneConstraintThatBothContextsReferTo()
    {
        var path = Start(out var ruleId);
        using (var cache = new FwDataProjectLoader().LoadCache(path))
        {
            Assert.Empty(cache.LangProject.PhonologicalDataOA.FeatConstraintsOS);
            var lowering = PhonologicalRuleRecordImportTests.Lower(cache, ruleId, document =>
            {
                document.Data.Set("struc-desc", new RecordScalar("[voi:α]"));
                Rhs(document).Set("left-context", new RecordScalar("[V voi:α]"));
            });
            Assert.Empty(lowering.Refusals);
            Assert.Single(Kinds(lowering), kind => kind == "grammar/phPhonData/createFeatConstraints");
            Assert.Single(Kinds(lowering), kind => kind == "grammar/phFeatureConstraint/setFeature");
            Assert.Equal(2, Kinds(lowering).Count(kind => kind == "grammar/phSimpleContextNC/addRefPlusConstr"));
            Assert.DoesNotContain(Collect, Kinds(lowering));
        }
        Edit(path, "alpha", ruleId, left: "[V voi:α]", desc: "[voi:α]");
        using var held = new FwDataProjectLoader().LoadCache(path);
        var constraint = cache0(held);
        Assert.Equal(Feature(held, "voi").Guid, constraint.FeatureRA.Guid);
        Assert.Contains(constraint, Left(held, ruleId).PlusConstrRS);
        Assert.Contains(constraint, ((IPhSimpleContextNC)Rule(held, ruleId).StrucDescOS.Single()).PlusConstrRS);
        Assert.Equal("[V voi:α]", Text(held, ruleId, RuleField.LeftContext));
        Assert.Equal("[voi:α]", Text(held, ruleId, RuleField.StrucDesc));
        Assert.Equal(new[] { constraint }, Rule(held, ruleId).FeatureConstraints.ToArray());
        AssertNothingLeftToChange(path, ruleId);

        static IPhFeatureConstraint cache0(LcmCache cache) => cache.LangProject.PhonologicalDataOA.FeatConstraintsOS.Single();
    }

    [Fact]
    public void ChangingAnAgreeingVariableToADisagreeingOneMovesTheReferenceAndKeepsTheContextAndTheConstraint()
    {
        var path = Start(out var ruleId);
        Edit(path, "agree", ruleId, left: "[V voi:α]");
        Guid context, constraint;
        using (var cache = new FwDataProjectLoader().LoadCache(path))
        {
            (context, constraint) = (Left(cache, ruleId).Guid, cache.LangProject.PhonologicalDataOA.FeatConstraintsOS.Single().Guid);
            var lowering = Lower(cache, ruleId, left: "[V voi:-α]");
            Assert.Empty(lowering.Refusals);
            Assert.Equal(new[] { "grammar/phSimpleContextNC/addRefMinusConstr", "grammar/phSimpleContextNC/removeRefPlusConstr", Collect },
                Kinds(lowering).OrderBy(kind => kind == Collect).ThenBy(kind => kind, StringComparer.Ordinal).ToArray());
        }
        Edit(path, "disagree", ruleId, left: "[V voi:-α]");
        using var held = new FwDataProjectLoader().LoadCache(path);
        var left = Left(held, ruleId);
        Assert.Equal(context, left.Guid);
        Assert.Empty(left.PlusConstrRS);
        Assert.Equal(constraint, left.MinusConstrRS.Single().Guid);
        Assert.Equal(constraint, held.LangProject.PhonologicalDataOA.FeatConstraintsOS.Single().Guid);
        Assert.Equal("[V voi:-α]", Text(held, ruleId, RuleField.LeftContext));
        AssertNothingLeftToChange(path, ruleId);
    }

    [Fact]
    public void RemovingTheLastUseCollectsTheConstraintAndOnlyThatOne()
    {
        var path = Start(out var ruleId);
        Edit(path, "agree", ruleId, left: "[V voi:α]");
        Guid orphan = Guid.Empty;
        using (var cache = new FwDataProjectLoader().LoadCache(path))
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var stray = cache.ServiceLocator.GetInstance<IPhFeatureConstraintFactory>().Create();
                cache.LangProject.PhonologicalDataOA.FeatConstraintsOS.Add(stray);
                stray.FeatureRA = Feature(cache, "cons");
                orphan = stray.Guid;
            });
            new FwDataProjectLoader().Save(cache);
        }
        Edit(path, "plain", ruleId, left: "[V]");
        using var held = new FwDataProjectLoader().LoadCache(path);
        Assert.Equal(orphan, held.LangProject.PhonologicalDataOA.FeatConstraintsOS.Single().Guid);
        Assert.Empty(Left(held, ruleId).PlusConstrRS);
        Assert.Equal("[V]", Text(held, ruleId, RuleField.LeftContext));
        AssertNothingLeftToChange(path, ruleId);
    }

    private string StartWithSharedConstraint(out Guid ruleId, out Guid sharedContext)
    {
        var path = Start(out ruleId);
        var rule = ruleId;
        Edit(path, "agree", ruleId, left: "[V voi:α]");
        using var cache = new FwDataProjectLoader().LoadCache(path);
        Guid made = Guid.Empty;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var data = cache.LangProject.PhonologicalDataOA;
            var copy = cache.ServiceLocator.GetInstance<IPhRegularRuleFactory>().Create();
            data.PhonRulesOS.Add(copy);
            var rhs = cache.ServiceLocator.GetInstance<IPhSegRuleRHSFactory>().Create();
            copy.RightHandSidesOS.Add(rhs);
            var context = cache.ServiceLocator.GetInstance<IPhSimpleContextNCFactory>().Create();
            data.ContextsOS.Add(context);
            var original = Left(cache, rule);
            context.FeatureStructureRA = original.FeatureStructureRA;
            context.PlusConstrRS.Add(data.FeatConstraintsOS.Single());
            rhs.LeftContextOA = context;
            made = context.Guid;
        });
        new FwDataProjectLoader().Save(cache);
        sharedContext = made;
        return path;
    }

    [Fact]
    public void AConstraintAnotherRuleAlsoUsesIsKeptWhenThisRuleStopsUsingIt()
    {
        var path = StartWithSharedConstraint(out var ruleId, out var other);
        Guid constraint;
        using (var cache = new FwDataProjectLoader().LoadCache(path))
        {
            constraint = cache.LangProject.PhonologicalDataOA.FeatConstraintsOS.Single().Guid;
            Assert.Contains(Collect, Kinds(Lower(cache, ruleId, left: "[V]")));
        }
        Edit(path, "plain", ruleId, left: "[V]");
        using var held = new FwDataProjectLoader().LoadCache(path);
        var kept = held.LangProject.PhonologicalDataOA.FeatConstraintsOS.Single();
        Assert.Equal(constraint, kept.Guid);
        Assert.Equal(other, kept.ReferringObjects.Single().Guid);
        Assert.Empty(Left(held, ruleId).PlusConstrRS);
    }

    [Fact]
    public void ADeletedContextAndAConstraintAnotherRuleSharesLeaveTheConstraintInPlace()
    {
        var path = StartWithSharedConstraint(out var ruleId, out var other);
        Edit(path, "shape", ruleId, left: "a");
        using var held = new FwDataProjectLoader().LoadCache(path);
        var kept = held.LangProject.PhonologicalDataOA.FeatConstraintsOS.Single();
        Assert.Equal(other, kept.ReferringObjects.Single().Guid);
        Assert.Equal("a", Text(held, ruleId, RuleField.LeftContext));
    }

    [Fact]
    public void ASharedConstraintIsFoundNotMadeAgainWhenThisRuleUsesItsFeatureAnew()
    {
        var path = StartWithSharedConstraint(out var ruleId, out _);
        using var cache = new FwDataProjectLoader().LoadCache(path);
        var lowering = Lower(cache, ruleId, right: "[C voi:α]");
        Assert.Empty(lowering.Refusals);
        Assert.DoesNotContain("grammar/phPhonData/createFeatConstraints", Kinds(lowering));
        Assert.Equal(cache.LangProject.PhonologicalDataOA.FeatConstraintsOS.Single().Guid,
            lowering.Operations.Single(operation => operation.Kind == "grammar/phSimpleContextNC/addRefPlusConstr").After!.Value.GetProperty("member").GetString() is { } member
                ? CanonicalIdGuid(member) : Guid.Empty);
    }

    private static Guid CanonicalIdGuid(string value) => SIL.Motif.Contract.Ids.CanonicalId.Parse(value).ToGuid();

    [Fact]
    public void AConsistentButNonCanonicalLetteringIsAcceptedAndShownBackInTheProjectsOrder()
    {
        var path = Start(out var ruleId);
        Edit(path, "letters", ruleId, left: "[V voi:β]", right: "[C cons:α]");
        using var held = new FwDataProjectLoader().LoadCache(path);
        Assert.Equal(2, held.LangProject.PhonologicalDataOA.FeatConstraintsOS.Count);
        var left = Text(held, ruleId, RuleField.LeftContext);
        var right = Text(held, ruleId, RuleField.RightContext);
        Assert.Equal(2, new[] { left, right }.Select(text => text[^2]).Distinct().Count());
        Assert.Contains(left[^2], "αβ");
        Assert.Contains(right[^2], "αβ");
        AssertNothingLeftToChange(path, ruleId);
    }

    [Fact]
    public void ALetterForTwoFeaturesOrTwoLettersForOneFeatureAreRefusedAcrossTheRuleAsItWillStand()
    {
        var path = Start(out var ruleId);
        Edit(path, "agree", ruleId, left: "[V voi:α]");
        using var cache = new FwDataProjectLoader().LoadCache(path);
        void Mismatch(Action<RecordDocument> edit, int refused)
        {
            var lowering = PhonologicalRuleRecordImportTests.Lower(cache, ruleId, edit);
            Assert.Empty(lowering.Operations);
            Assert.Equal(refused, lowering.Refusals.Count);
            Assert.All(lowering.Refusals, refusal => Assert.Equal(RefusalCodes.RuleNotationAlphaMismatch, refusal.Code));
        }
        Mismatch(document => Rhs(document).Set("right-context", new RecordScalar("[C cons:α]")), 1);
        Mismatch(document => Rhs(document).Set("right-context", new RecordScalar("[C voi:β]")), 1);
        Mismatch(document =>
        {
            Rhs(document).Set("left-context", new RecordScalar("[V cons:α]"));
            Rhs(document).Set("right-context", new RecordScalar("[C voi:α]"));
        }, 2);
        Assert.Empty(Lower(cache, ruleId, right: "[C cons:β]").Refusals);
    }

    [Fact]
    public void AnAlphaVariableInASequenceAndARepeatLowersAndReadsBack()
    {
        var path = Start(out var ruleId);
        Edit(path, "shapes", ruleId, right: "[C voi:α]{1,2} [V voi:-α] a");
        using (var held = new FwDataProjectLoader().LoadCache(path))
        {
            var constraint = held.LangProject.PhonologicalDataOA.FeatConstraintsOS.Single();
            var sequence = (IPhSequenceContext)Rule(held, ruleId).RightHandSidesOS.Single().RightContextOA!;
            var repeat = Assert.IsAssignableFrom<IPhIterationContext>(sequence.MembersRS[0]);
            Assert.Equal(constraint, ((IPhSimpleContextNC)repeat.MemberRA).PlusConstrRS.Single());
            Assert.Equal(constraint, ((IPhSimpleContextNC)sequence.MembersRS[1]).MinusConstrRS.Single());
            Assert.Equal("[C voi:α]{1,2} [V voi:-α] a", Text(held, ruleId, RuleField.RightContext));
        }
        AssertNothingLeftToChange(path, ruleId);
        Edit(path, "inner", ruleId, right: "[C voi:-α]{1,2} [V voi:-α] a");
        using var after = new FwDataProjectLoader().LoadCache(path);
        var again = (IPhSequenceContext)Rule(after, ruleId).RightHandSidesOS.Single().RightContextOA!;
        Assert.Single(after.LangProject.PhonologicalDataOA.FeatConstraintsOS);
        Assert.Empty(((IPhSimpleContextNC)((IPhIterationContext)again.MembersRS[0]).MemberRA).PlusConstrRS);
        AssertNothingLeftToChange(path, ruleId);
    }

    [Fact]
    public void AnAlphaOnlyBracketMakesAFeatureClassWithNoValueAndANamedClassTakesAlphaToo()
    {
        var path = Start(out var ruleId);
        Edit(path, "only", ruleId, change: "[voi:α]", left: "[V voi:α]");
        using var held = new FwDataProjectLoader().LoadCache(path);
        var change = (IPhSimpleContextNC)Rule(held, ruleId).RightHandSidesOS.Single().StrucChangeOS.Single();
        var features = Assert.IsAssignableFrom<IPhNCFeatures>(change.FeatureStructureRA);
        Assert.Empty(features.FeaturesOA.FeatureSpecsOC);
        Assert.Equal("[voi:α]", Text(held, ruleId, RuleField.StrucChange));
        Assert.Equal("[V voi:α]", Text(held, ruleId, RuleField.LeftContext));
        Assert.Single(held.LangProject.PhonologicalDataOA.FeatConstraintsOS);
        AssertNothingLeftToChange(path, ruleId);
    }

    [Fact]
    public void ANaturalClassIsNeverDeletedWhileAlphaVariablesChange()
    {
        var path = Start(out var ruleId);
        int classes;
        using (var cache = new FwDataProjectLoader().LoadCache(path))
            classes = cache.LangProject.PhonologicalDataOA.NaturalClassesOS.Count;
        Edit(path, "a", ruleId, left: "[V voi:α]");
        Edit(path, "b", ruleId, left: "[C]");
        Edit(path, "c", ruleId, left: "[V cons:α]");
        using var held = new FwDataProjectLoader().LoadCache(path);
        Assert.Equal(classes, held.LangProject.PhonologicalDataOA.NaturalClassesOS.Count);
    }

    [Fact]
    public void AContextThatIsSharedWhenTheDeleteAppliesIsRefusedWithANamedCode()
    {
        var path = Start(out var ruleId);
        PhonologicalRuleRecordImportTests.Import(path, "sequence", ruleId, document => Rhs(document).Set("right-context", new RecordScalar("[V] a")));
        using var cache = new FwDataProjectLoader().LoadCache(path);
        var lowering = Lower(cache, ruleId, right: "a");
        Assert.Empty(lowering.Refusals);
        var delete = lowering.Operations.Single(operation => operation.Kind == PhonologicalRuleDeleteOperationKinds.DeleteContexts);
        var member = ((IPhSequenceContext)Rule(cache, ruleId).RightHandSidesOS.Single().RightContextOA!).MembersRS[1];
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () => AlsoUsedBy(cache, member));
        var refused = Assert.IsAssignableFrom<ProposalRefusalException>(Record.Exception(() => NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            OperationHandlerRegistry.Resolve(delete.Kind, "apply-time guard test").ApplyAndCaptureEffect(cache, delete, []))));
        Assert.Equal(RefusalCodes.RuleNotationContextInUse, refused.Code);
        Assert.Equal(FailureReason.Refused, refused.Reason);
        Assert.NotNull(cache.ServiceLocator.GetObject(member.Guid));
    }

    [Fact]
    public void APooledMemberThatIsSharedWhenItsOwnDeleteAppliesIsRefusedWithTheSameCode()
    {
        var path = Start(out var ruleId);
        PhonologicalRuleRecordImportTests.Import(path, "sequence", ruleId, document => Rhs(document).Set("right-context", new RecordScalar("[V] a i")));
        using var cache = new FwDataProjectLoader().LoadCache(path);
        var lowering = Lower(cache, ruleId, right: "[V] a");
        Assert.Empty(lowering.Refusals);
        var delete = lowering.Operations.Single(operation => operation.Kind == AffixProcessMemberDeleteOperationKinds.DeleteContext);
        var sequence = (IPhSequenceContext)Rule(cache, ruleId).RightHandSidesOS.Single().RightContextOA!;
        var member = sequence.MembersRS[2];
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            AlsoUsedBy(cache, member);
            sequence.MembersRS.Remove(member);
        });
        var refused = Assert.IsAssignableFrom<ProposalRefusalException>(Record.Exception(() => NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            OperationHandlerRegistry.Resolve(delete.Kind, "apply-time guard test").ApplyAndCaptureEffect(cache, delete, []))));
        Assert.Equal(RefusalCodes.RuleNotationContextInUse, refused.Code);
        Assert.NotNull(cache.ServiceLocator.GetObject(member.Guid));
    }

    private static void AlsoUsedBy(LcmCache cache, IPhPhonContext member)
    {
        var other = cache.ServiceLocator.GetInstance<IPhSequenceContextFactory>().Create();
        cache.LangProject.PhonologicalDataOA.ContextsOS.Add(other);
        other.MembersRS.Add(member);
    }
}

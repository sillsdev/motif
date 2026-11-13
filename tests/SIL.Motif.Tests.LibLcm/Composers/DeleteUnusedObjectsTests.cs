using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using static SIL.Motif.Tests.Composers.AffixTestSupport;

namespace SIL.Motif.Tests.Composers;

/// <summary>The four composers that delete an object nothing refers to: delete, refuse naming referrers, Dry Run effect.</summary>
[Collection(LcmCacheParallelCollections.Group1)]
public sealed class DeleteUnusedObjectsTests(PristineProjectFixture pristine)
{
    private static CanonicalId NewFeature(LcmCache cache, string name) =>
        Created(ApplyOps(cache, AuthorExceptionFeatureComposer.Build(cache, new(name, Anal(cache)))),
            CmPossibilityListPossibilitiesOperationKinds.Create);

    private static CanonicalId NewNaturalClass(LcmCache cache, string name, string abbreviation)
    {
        IPhNaturalClass created = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            created = cache.ServiceLocator.GetInstance<IPhNCSegmentsFactory>().Create();
            cache.LangProject.PhonologicalDataOA.NaturalClassesOS.Add(created);
            created.Name.set_String(cache.DefaultAnalWs, name);
            created.Abbreviation.set_String(cache.DefaultAnalWs, abbreviation);
        });
        return Id(created);
    }

    private static string Refusal(Action build) => Assert.Throws<InvalidOperationException>(build).Message;

    [Fact]
    public void AnUnreferencedExceptionFeatureIsDeletedAndTheDryRunReportsTheEffect()
    {
        using var cache = pristine.NewScratch();
        var feature = NewFeature(cache, "Loan stem");
        var operations = DeleteUnusedObjectsComposer.BuildExceptionFeature(cache, new(feature, "Loan stem"));
        var operation = Assert.Single(operations);
        Assert.Equal(UnusedObjectDeleteOperationKinds.DeleteExceptionFeature, operation.Kind);
        var dry = ScratchDryRun.Of(cache, Proposal(operations));
        var effect = Assert.Single(dry.ExpectedEffects);
        Assert.Equal(feature, effect.CanonicalId);
        Assert.True(Exists(cache, feature));
        ApplyOps(cache, operations);
        Assert.False(Exists(cache, feature));
    }

    [Fact]
    public void AnExceptionFeatureAStemStillCarriesIsRefusedNamingTheStem()
    {
        using var cache = pristine.NewScratch();
        var verb = AddCategory(cache, "Verb");
        var stem = AddStem(cache, "kaun", verb);
        var feature = NewFeature(cache, "Loan stem");
        ApplyOps(cache, EditExceptionFeaturesComposer.Build(cache, new(stem.Msa, [feature])));
        var message = Refusal(() => DeleteUnusedObjectsComposer.BuildExceptionFeature(cache, new(feature, "Loan stem")));
        Assert.Contains("MoStemMsa", message);
        Assert.Contains(stem.Msa.ToGuid().ToString("D"), message);
        Assert.Contains("kaun", message);
        Assert.True(Exists(cache, feature));
        Assert.Contains("not 'Other'", Refusal(() =>
            DeleteUnusedObjectsComposer.BuildExceptionFeature(cache, new(feature, "Other"))));
    }

    [Fact]
    public void AnUnreferencedEnvironmentIsDeletedAndAnAllomorphUseIsRefused()
    {
        using var cache = pristine.NewScratch();
        var unused = AddEnvironment(cache, "free");
        var used = AddEnvironment(cache, "used");
        var proposal = ApplyOps(cache, AuthorLexicalEntryComposer.Build(cache, new AuthorLexicalEntryIntent("tu", Vern(cache),
            MorphType(MoMorphTypeTags.kguidMorphStem), "person", Anal(cache),
            new LexicalEntryMsa(LexicalEntryMsaKind.Stem, AddCategory(cache, "Noun")), null, null, [used])));
        var form = Created(proposal, LexEntryLexemeFormOperationKinds.CreateLexemeForm);
        var message = Refusal(() => DeleteUnusedObjectsComposer.BuildEnvironment(cache, new(used, "used")));
        Assert.Contains(form.ToGuid().ToString("D"), message);
        Assert.Contains("tu", message);

        var operations = DeleteUnusedObjectsComposer.BuildEnvironment(cache, new(unused, "free"));
        var dry = ScratchDryRun.Of(cache, Proposal(operations));
        Assert.Equal(unused, Assert.Single(dry.ExpectedEffects).CanonicalId);
        ApplyOps(cache, operations);
        Assert.False(Exists(cache, unused));
        Assert.True(Exists(cache, used));
    }

    [Fact]
    public void ANaturalClassNamedInAnEnvironmentStringIsRefusedAndAFreeOneIsDeleted()
    {
        using var cache = pristine.NewScratch();
        var named = NewNaturalClass(cache, "Consonants", "C");
        var free = NewNaturalClass(cache, "Vowels", "V");
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var environment = cache.ServiceLocator.GetInstance<IPhEnvironmentFactory>().Create();
            cache.LangProject.PhonologicalDataOA.EnvironmentsOS.Add(environment);
            environment.StringRepresentation = TsStringUtils.MakeString("/ [C] _", cache.DefaultVernWs);
        });
        var message = Refusal(() => DeleteUnusedObjectsComposer.BuildNaturalClass(cache, new(named, "Consonants")));
        Assert.Contains("PhEnvironment", message);

        var operations = DeleteUnusedObjectsComposer.BuildNaturalClass(cache, new(free, "Vowels"));
        var dry = ScratchDryRun.Of(cache, Proposal(operations));
        Assert.Equal(free, Assert.Single(dry.ExpectedEffects).CanonicalId);
        ApplyOps(cache, operations);
        Assert.False(Exists(cache, free));
        Assert.True(Exists(cache, named));
    }

    [Fact]
    public void ANaturalClassAnotherObjectReferencesIsRefusedAtComposeTime()
    {
        using var cache = pristine.NewScratch();
        var data = cache.LangProject.PhonologicalDataOA;
        var target = NewNaturalClass(cache, "Stops", "STP");
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var rule = cache.ServiceLocator.GetInstance<IPhRegularRuleFactory>().Create();
            data.PhonRulesOS.Add(rule);
            var rhs = cache.ServiceLocator.GetInstance<IPhSegRuleRHSFactory>().Create();
            rule.RightHandSidesOS.Add(rhs);
            var context = cache.ServiceLocator.GetInstance<IPhSimpleContextNCFactory>().Create();
            rhs.LeftContextOA = context;
            context.FeatureStructureRA = Get<IPhNaturalClass>(cache, target);
        });
        Assert.Contains("PhSimpleContextNC", Refusal(() =>
            DeleteUnusedObjectsComposer.BuildNaturalClass(cache, new(target, "Stops"))));
    }

    [Fact]
    public void ATextSegmentNoteIsDeletedAndTheDryRunReportsTheEffect()
    {
        using var cache = pristine.NewScratch();
        INote note = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var text = cache.ServiceLocator.GetInstance<ITextFactory>().Create();
            text.Name.set_String(cache.DefaultAnalWs, "T");
            text.ContentsOA = cache.ServiceLocator.GetInstance<IStTextFactory>().Create();
            var para = cache.ServiceLocator.GetInstance<IStTxtParaFactory>().Create();
            text.ContentsOA.ParagraphsOS.Add(para);
            para.Contents = TsStringUtils.MakeString("alpha beta.", cache.DefaultVernWs);
            ParagraphParser.ParseParagraph(para);
            note = cache.ServiceLocator.GetInstance<INoteFactory>().Create();
            para.SegmentsOS[0].NotesOS.Add(note);
            note.Content.set_String(cache.DefaultAnalWs, TsStringUtils.MakeString("check this", cache.DefaultAnalWs));
        });
        var id = Id(note);
        var segment = (ISegment)note.Owner;
        Assert.Contains("not 'other'", Refusal(() =>
            DeleteUnusedObjectsComposer.BuildTextSegmentNote(cache, new(id, "other"))));
        var operations = DeleteUnusedObjectsComposer.BuildTextSegmentNote(cache, new(id, "check this"));
        var dry = ScratchDryRun.Of(cache, Proposal(operations));
        Assert.Equal(id, Assert.Single(dry.ExpectedEffects).CanonicalId);
        ApplyOps(cache, operations);
        Assert.False(Exists(cache, id));
        Assert.Empty(segment.NotesOS);
    }

    [Fact]
    public void ANoteTargetThatIsNotASegmentNoteIsRefused()
    {
        using var cache = pristine.NewScratch();
        var feature = NewFeature(cache, "Loan stem");
        Assert.ThrowsAny<Exception>(() => DeleteUnusedObjectsComposer.BuildTextSegmentNote(cache, new(feature, "x")));
    }
}

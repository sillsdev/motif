using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Execution;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Runner;

[Collection(LcmCacheParallelCollections.Group1)]
public sealed class MakeVariantOfComposerTests : IDisposable
{
    private readonly LcmCache _cache;
    public MakeVariantOfComposerTests(PristineProjectFixture pristine) => _cache = pristine.NewScratch();
    public void Dispose() { if (!_cache.IsDisposed) _cache.Dispose(); }

    [Fact]
    public void TwoExistingEntriesGetAVariantReferenceThroughDryRunAndApply()
    {
        var (variant, main, type) = Seed();
        var proposal = Proposal(MakeVariantOfComposer.Build(_cache, new(Id(variant), Id(main), Id(type))));
        var dry = ScratchDryRun.Of(_cache, proposal);
        var effect = Assert.Single(dry.ExpectedEffects);
        Assert.Equal(SIL.Motif.Model.Snapshot.SnapshotFields.LexEntryEntryRefs, effect.Field);
        Assert.Equal(Id(variant), effect.CanonicalId);
        Assert.Empty(effect.Before);
        Assert.Single(effect.After);
        Assert.Empty(variant.EntryRefsOS);
        ProposalApplier.Apply(_cache, proposal, dry.Anchor, "tester");
        var reference = Assert.Single(variant.EntryRefsOS);
        Assert.Equal(LexEntryRefTags.krtVariant, reference.RefType);
        Assert.Equal(main.Guid, Assert.Single(reference.ComponentLexemesRS).Guid);
        Assert.Equal(type.Guid, Assert.Single(reference.VariantEntryTypesRS).Guid);
        Assert.Equal(CanonicalId.Parse(effect.After.Single().Value).ToGuid(), reference.Guid);
    }

    [Fact]
    public void TheCreatedReferenceMatchesWhatFieldWorksMakeVariantOfBuilds()
    {
        var (variant, main, type) = Seed();
        ILexEntry fieldWorksVariant = null!;
        ILexEntryRef expected = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            fieldWorksVariant = _cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create();
            expected = fieldWorksVariant.MakeVariantOf(main, type);
        });
        var proposal = Proposal(MakeVariantOfComposer.Build(_cache, new(Id(variant), Id(main), Id(type))));
        ProposalApplier.Apply(_cache, proposal, ScratchDryRun.Of(_cache, proposal).Anchor, "tester");
        var actual = Assert.Single(variant.EntryRefsOS);
        Assert.Equal(expected.RefType, actual.RefType);
        Assert.Equal(expected.HideMinorEntry, actual.HideMinorEntry);
        Assert.Equal(expected.PrimaryLexemesRS.Count, actual.PrimaryLexemesRS.Count);
        Assert.Equal(expected.ShowComplexFormsInRS.Count, actual.ShowComplexFormsInRS.Count);
        Assert.Equal(expected.ComponentLexemesRS.Select(x => x.Guid), actual.ComponentLexemesRS.Select(x => x.Guid));
        Assert.Equal(expected.VariantEntryTypesRS.Select(x => x.Guid), actual.VariantEntryTypesRS.Select(x => x.Guid));
    }

    [Fact]
    public void ASenseCanBeTheMainComponent()
    {
        var (variant, main, type) = Seed();
        ILexSense sense = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            sense = _cache.ServiceLocator.GetInstance<ILexSenseFactory>().Create();
            main.SensesOS.Add(sense);
        });
        var proposal = Proposal(MakeVariantOfComposer.Build(_cache, new(Id(variant), Id(sense), Id(type))));
        ProposalApplier.Apply(_cache, proposal, ScratchDryRun.Of(_cache, proposal).Anchor, "tester");
        Assert.Equal(sense.Guid, Assert.Single(Assert.Single(variant.EntryRefsOS).ComponentLexemesRS).Guid);
    }

    [Fact]
    public void SelfCycleDuplicateTypeAndEntryProblemsAreRefusedAtCompose()
    {
        var (variant, main, type) = Seed();
        Assert.Equal(RefusalCodes.VariantSelfLink, Code(() => MakeVariantOfComposer.Build(_cache, new(Id(variant), Id(variant), Id(type)))));
        Assert.Equal(RefusalCodes.VariantInvalidType, Code(() => MakeVariantOfComposer.Build(_cache, new(Id(variant), Id(main), Id(main)))));
        Assert.Equal(RefusalCodes.VariantInvalidComponent, Code(() => MakeVariantOfComposer.Build(_cache, new(Id(variant), Id(type), Id(type)))));
        Assert.Equal(RefusalCodes.VariantInvalidEntry, Code(() => MakeVariantOfComposer.Build(_cache, new(Id(type), Id(main), Id(type)))));
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () => variant.MakeVariantOf(main, type));
        Assert.Equal(RefusalCodes.VariantAlreadyLinked, Code(() => MakeVariantOfComposer.Build(_cache, new(Id(variant), Id(main), Id(type)))));
        Assert.Equal(RefusalCodes.VariantCycle, Code(() => MakeVariantOfComposer.Build(_cache, new(Id(main), Id(variant), Id(type)))));
    }

    [Fact]
    public void AGraphChangeAfterComposeIsRefusedWhenTheDryRunApplies()
    {
        var (variant, main, type) = Seed();
        var operations = MakeVariantOfComposer.Build(_cache, new(Id(variant), Id(main), Id(type)));
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () => main.MakeVariantOf(variant, type));
        Assert.ThrowsAny<Exception>(() => ScratchDryRun.Of(_cache, Proposal(operations)));
    }

    [Fact]
    public void AReferenceCreatedAfterTheDryRunInvalidatesIt()
    {
        var (variant, main, type) = Seed();
        var proposal = Proposal(MakeVariantOfComposer.Build(_cache, new(Id(variant), Id(main), Id(type))));
        var dry = ScratchDryRun.Of(_cache, proposal);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () => variant.MakeVariantOf(main, type));
        Assert.ThrowsAny<Exception>(() => ProposalApplier.Apply(_cache, proposal, dry.Anchor, "tester"));
    }

    private static string Code(Action action) => Assert.ThrowsAny<ProposalRefusalException>(action).Code;

    private (ILexEntry Variant, ILexEntry Main, ILexEntryType Type) Seed()
    {
        ILexEntry variant = null!, main = null!;
        ILexEntryType type = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var services = _cache.ServiceLocator;
            variant = services.GetInstance<ILexEntryFactory>().Create();
            main = services.GetInstance<ILexEntryFactory>().Create();
            var list = _cache.LangProject.LexDbOA.VariantEntryTypesOA;
            if (list is null)
            {
                list = services.GetInstance<ICmPossibilityListFactory>().Create();
                _cache.LangProject.LexDbOA.VariantEntryTypesOA = list;
            }
            type = services.GetInstance<ILexEntryTypeFactory>().Create();
            list.PossibilitiesOS.Add(type);
        });
        return (variant, main, type);
    }

    private static CanonicalId Id(ICmObject value) => CanonicalId.FromGuid(value.Guid);
    private static Proposal Proposal(IReadOnlyList<OperationEnvelope> operations) =>
        new(new Dictionary<string, string> { ["lexical"] = "1.0" }, CanonicalId.Mint(), null, operations);
}

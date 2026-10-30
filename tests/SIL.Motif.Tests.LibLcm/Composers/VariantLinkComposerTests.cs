using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Runner.Execution;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Runner;

[Collection(LcmCacheParallelCollections.Group1)]
public sealed class VariantLinkComposerTests : IDisposable
{
    private readonly LcmCache _cache;
    public VariantLinkComposerTests(PristineProjectFixture pristine) => _cache = pristine.NewScratch();
    public void Dispose() { if (!_cache.IsDisposed) _cache.Dispose(); }

    [Fact]
    public void LinkRoundTripsThroughDryRunAndApplyAndPreservesTheSourceUntilApply()
    {
        var (source, reference, target, type) = Seed();
        var operations = LinkVariantComposer.Build(_cache, new(Id(reference), Id(target), Id(type)));
        var proposal = Proposal(operations);
        var dry = ScratchDryRun.Of(_cache, proposal);
        Assert.Equal(2, dry.ExpectedEffects.Count);
        Assert.Empty(reference.ComponentLexemesRS);
        ProposalApplier.Apply(_cache, proposal, dry.Anchor, "tester");
        Assert.Equal(target.Guid, Assert.Single(reference.ComponentLexemesRS).Guid);
        Assert.Equal(type.Guid, Assert.Single(reference.VariantEntryTypesRS).Guid);
        Assert.Same(source, reference.Owner);
    }

    [Fact]
    public void ExistingTargetAndTypeAreReplacedTogether()
    {
        var (_, reference, target, type) = Seed();
        ILexEntry oldTarget = null!;
        ILexEntryType oldType = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            oldTarget = _cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create();
            oldType = _cache.ServiceLocator.GetInstance<ILexEntryTypeFactory>().Create();
            _cache.LangProject.LexDbOA.VariantEntryTypesOA.PossibilitiesOS.Add(oldType);
            reference.ComponentLexemesRS.Add(oldTarget);
            reference.VariantEntryTypesRS.Add(oldType);
        });
        var proposal = Proposal(LinkVariantComposer.Build(_cache, new(Id(reference), Id(target), Id(type))));
        var dry = ScratchDryRun.Of(_cache, proposal);
        Assert.Equal(Id(oldTarget).Value, Assert.Single(dry.ExpectedEffects[0].Before).Value);
        Assert.Equal(Id(oldType).Value, Assert.Single(dry.ExpectedEffects[1].Before).Value);
        ProposalApplier.Apply(_cache, proposal, dry.Anchor, "tester");
        Assert.Equal(target.Guid, Assert.Single(reference.ComponentLexemesRS).Guid);
        Assert.Equal(type.Guid, Assert.Single(reference.VariantEntryTypesRS).Guid);
    }

    [Fact]
    public void SelfAndIndirectCyclesAreRefusedAtComposeAndWhenTheGraphChangesBeforeDryRun()
    {
        var (source, reference, target, type) = Seed();
        Assert.Equal(RefusalCodes.VariantSelfLink, Assert.ThrowsAny<ProposalRefusalException>(() =>
            LinkVariantComposer.Build(_cache, new(Id(reference), Id(source), Id(type)))).Code);
        var operations = LinkVariantComposer.Build(_cache, new(Id(reference), Id(target), Id(type)));
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var reverse = _cache.ServiceLocator.GetInstance<ILexEntryRefFactory>().Create();
            target.EntryRefsOS.Add(reverse);
            reverse.RefType = LexEntryRefTags.krtVariant;
            reverse.ComponentLexemesRS.Add(source);
        });
        Assert.Equal(RefusalCodes.VariantCycle, Assert.ThrowsAny<ProposalRefusalException>(() =>
            LinkVariantComposer.Build(_cache, new(Id(reference), Id(target), Id(type)))).Code);
        Assert.ThrowsAny<Exception>(() => ScratchDryRun.Of(_cache, Proposal(operations)));
    }

    [Fact]
    public void ChangedReferenceAndNonVariantTypeAreRefused()
    {
        var (_, reference, target, type) = Seed();
        var operations = LinkVariantComposer.Build(_cache, new(Id(reference), Id(target), Id(type)));
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () => reference.ComponentLexemesRS.Add(target));
        Assert.ThrowsAny<Exception>(() => ScratchDryRun.Of(_cache, Proposal(operations)));
        Assert.Equal(RefusalCodes.VariantInvalidType, Assert.ThrowsAny<ProposalRefusalException>(() =>
            LinkVariantComposer.Build(_cache, new(Id(reference), Id(target), Id(target)))).Code);
        Assert.Equal(RefusalCodes.VariantInvalidComponent, Assert.ThrowsAny<ProposalRefusalException>(() =>
            LinkVariantComposer.Build(_cache, new(Id(reference), Id(type), Id(type)))).Code);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            reference.ComponentLexemesRS.Clear();
            reference.RefType = LexEntryRefTags.krtComplexForm;
        });
        Assert.Equal(RefusalCodes.VariantInvalidReference, Assert.ThrowsAny<ProposalRefusalException>(() =>
            LinkVariantComposer.Build(_cache, new(Id(reference), Id(target), Id(type)))).Code);
    }

    private (ILexEntry Source, ILexEntryRef Reference, ILexEntry Target, ILexEntryType Type) Seed()
    {
        ILexEntry source = null!, target = null!;
        ILexEntryRef reference = null!;
        ILexEntryType type = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var services = _cache.ServiceLocator;
            source = services.GetInstance<ILexEntryFactory>().Create();
            target = services.GetInstance<ILexEntryFactory>().Create();
            reference = services.GetInstance<ILexEntryRefFactory>().Create();
            source.EntryRefsOS.Add(reference);
            reference.RefType = LexEntryRefTags.krtVariant;
            var list = _cache.LangProject.LexDbOA.VariantEntryTypesOA;
            if (list is null)
            {
                list = services.GetInstance<ICmPossibilityListFactory>().Create();
                _cache.LangProject.LexDbOA.VariantEntryTypesOA = list;
            }
            type = services.GetInstance<ILexEntryTypeFactory>().Create();
            list.PossibilitiesOS.Add(type);
        });
        return (source, reference, target, type);
    }

    private static CanonicalId Id(ICmObject value) => CanonicalId.FromGuid(value.Guid);
    private static Proposal Proposal(IReadOnlyList<OperationEnvelope> operations) =>
        new(new Dictionary<string, string> { ["lexical"] = "1.0" }, CanonicalId.Mint(), null, operations);
}

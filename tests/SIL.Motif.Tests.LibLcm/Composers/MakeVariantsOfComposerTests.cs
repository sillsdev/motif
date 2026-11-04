using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Execution;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Runner;

[Collection(LcmCacheParallelCollections.Group1)]
public sealed class MakeVariantsOfComposerTests : IDisposable
{
    private readonly LcmCache _cache;
    public MakeVariantsOfComposerTests(PristineProjectFixture pristine) => _cache = pristine.NewScratch();
    public void Dispose() { if (!_cache.IsDisposed) _cache.Dispose(); }

    [Fact]
    public void ManyStubsBecomeVariantsInOneProposal()
    {
        var (entries, type) = Seed(4);
        var intent = new MakeVariantsOfIntent(Id(type),
            [new(Id(entries[0]), Id(entries[3])), new(Id(entries[1]), Id(entries[3])), new(Id(entries[2]), Id(entries[3]))], true);
        var proposal = Proposal(MakeVariantsOfComposer.Build(_cache, intent));
        ProposalApplier.Apply(_cache, proposal, ScratchDryRun.Of(_cache, proposal).Anchor, "tester");
        for (var i = 0; i < 3; i++)
            Assert.Equal(entries[3].Guid, Assert.Single(Assert.Single(entries[i].EntryRefsOS).ComponentLexemesRS).Guid);
        Assert.Empty(entries[3].EntryRefsOS);
    }

    [Fact]
    public void TwoLinksThatTogetherFormACycleAreRefusedNamingTheSecondLink()
    {
        var (entries, type) = Seed(2);
        var intent = new MakeVariantsOfIntent(Id(type),
            [new(Id(entries[0]), Id(entries[1])), new(Id(entries[1]), Id(entries[0]))], false);
        var refusal = Assert.ThrowsAny<ProposalRefusalException>(() => MakeVariantsOfComposer.Build(_cache, intent));
        Assert.Equal(RefusalCodes.VariantCycle, refusal.Code);
        Assert.StartsWith("Link 1 ", refusal.Message, StringComparison.Ordinal);
        Assert.Contains(Id(entries[0]).Value, refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AThreeLinkStagedCycleIsRefusedNamingTheLastLinkAndTheRoute()
    {
        var (entries, type) = Seed(3);
        var intent = new MakeVariantsOfIntent(Id(type),
            [new(Id(entries[0]), Id(entries[1])), new(Id(entries[1]), Id(entries[2])), new(Id(entries[2]), Id(entries[0]))], false);
        var refusal = Assert.ThrowsAny<ProposalRefusalException>(() => MakeVariantsOfComposer.Build(_cache, intent));
        Assert.Equal(RefusalCodes.VariantCycle, refusal.Code);
        Assert.StartsWith("Link 2 ", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("through", refusal.Message, StringComparison.Ordinal);
        Assert.Contains(Id(entries[1]).Value, refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACycleAgainstALinkAlreadyInTheProjectNamesTheRoute()
    {
        var (entries, type) = Seed(3);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            entries[1].MakeVariantOf(entries[0], type);
            entries[2].MakeVariantOf(entries[1], type);
        });
        var intent = new MakeVariantsOfIntent(Id(type), [new(Id(entries[0]), Id(entries[2]))], false);
        var refusal = Assert.ThrowsAny<ProposalRefusalException>(() => MakeVariantsOfComposer.Build(_cache, intent));
        Assert.Equal(RefusalCodes.VariantCycle, refusal.Code);
        Assert.Contains($"LexEntry {Id(entries[2]).Value}", refusal.Message, StringComparison.Ordinal);
        Assert.Contains(", through LexEntry ", refusal.Message, StringComparison.Ordinal);
        Assert.Contains(Id(entries[1]).Value, refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEntryWhoseSenseGainsASubSenseWithContentIsNotAStub()
    {
        var (entries, type) = Seed(2);
        ILexSense top = null!, sub = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            top = _cache.ServiceLocator.GetInstance<ILexSenseFactory>().Create();
            entries[0].SensesOS.Add(top);
        });
        var links = new VariantLinkItem[] { new(Id(entries[0]), Id(entries[1])) };
        Assert.Single(MakeVariantsOfComposer.Build(_cache, new(Id(type), links, true)));
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            sub = _cache.ServiceLocator.GetInstance<ILexSenseFactory>().Create();
            top.SensesOS.Add(sub);
        });
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            sub.Definition.set_String(_cache.DefaultAnalWs, TsStringUtils.MakeString("sky", _cache.DefaultAnalWs)));
        var refusal = Assert.ThrowsAny<ProposalRefusalException>(
            () => MakeVariantsOfComposer.Build(_cache, new(Id(type), links, true)));
        Assert.Contains("is not a stub", refusal.Message, StringComparison.Ordinal);
        Assert.Contains(Id(entries[0]).Value, refusal.Message, StringComparison.Ordinal);
        Assert.Contains("is not a stub", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAlreadyLinkedEntryAndARepeatedLinkAreRefusedNamingBothObjects()
    {
        var (entries, type) = Seed(2);
        var link = new VariantLinkItem(Id(entries[0]), Id(entries[1]));
        var repeated = Assert.ThrowsAny<ProposalRefusalException>(
            () => MakeVariantsOfComposer.Build(_cache, new(Id(type), [link, link], false)));
        Assert.Contains("twice in this list", repeated.Message, StringComparison.Ordinal);
        Assert.Contains(Id(entries[1]).Value, repeated.Message, StringComparison.Ordinal);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () => entries[0].MakeVariantOf(entries[1], type));
        var linked = Assert.ThrowsAny<ProposalRefusalException>(
            () => MakeVariantsOfComposer.Build(_cache, new(Id(type), [link], false)));
        Assert.Contains($"LexEntry {Id(entries[0]).Value}", linked.Message, StringComparison.Ordinal);
        Assert.Contains("is already a variant of", linked.Message, StringComparison.Ordinal);
        Assert.Contains(Id(entries[1]).Value, linked.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheIntentParserReadsOnlyStubsAndRefusesMissingLinksAndANonBooleanFlag()
    {
        var link = $$"""{"entry":"{{CanonicalId.Mint().Value}}","component":"{{CanonicalId.Mint().Value}}"}""";
        var type = CanonicalId.Mint().Value;
        var parsed = MakeVariantsOfIntentParser.Parse(Json($$"""{"variantType":"{{type}}","links":[{{link}}]}"""));
        Assert.False(parsed.OnlyStubs);
        Assert.True(MakeVariantsOfIntentParser.Parse(Json($$"""{"variantType":"{{type}}","links":[{{link}}],"onlyStubs":true}""")).OnlyStubs);
        Assert.Throws<Contract.Parsing.ContractParseException>(() =>
            MakeVariantsOfIntentParser.Parse(Json($$"""{"variantType":"{{type}}","links":[]}""")));
        Assert.Throws<Contract.Parsing.ContractParseException>(() =>
            MakeVariantsOfIntentParser.Parse(Json($$"""{"variantType":"{{type}}"}""")));
        var bad = Assert.Throws<Contract.Parsing.ContractParseException>(() =>
            MakeVariantsOfIntentParser.Parse(Json($$"""{"variantType":"{{type}}","links":[{{link}}],"onlyStubs":"yes"}""")));
        Assert.Contains("onlyStubs", bad.Message, StringComparison.Ordinal);
    }

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    [Fact]
    public void AnEntryWithContentIsRefusedWhenOnlyStubsIsSetAndConvertedOtherwise()
    {
        var (entries, type) = Seed(2);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var sense = _cache.ServiceLocator.GetInstance<ILexSenseFactory>().Create();
            entries[0].SensesOS.Add(sense);
            sense.Gloss.set_String(_cache.DefaultAnalWs, TsStringUtils.MakeString("sky", _cache.DefaultAnalWs));
        });
        var links = new VariantLinkItem[] { new(Id(entries[0]), Id(entries[1])) };
        var refusal = Assert.ThrowsAny<ProposalRefusalException>(
            () => MakeVariantsOfComposer.Build(_cache, new(Id(type), links, true)));
        Assert.Contains("is not a stub", refusal.Message, StringComparison.Ordinal);
        Assert.Single(MakeVariantsOfComposer.Build(_cache, new(Id(type), links, false)));
    }

    [Fact]
    public void ARepeatedLinkIsRefused()
    {
        var (entries, type) = Seed(2);
        var link = new VariantLinkItem(Id(entries[0]), Id(entries[1]));
        var refusal = Assert.ThrowsAny<ProposalRefusalException>(
            () => MakeVariantsOfComposer.Build(_cache, new(Id(type), [link, link], false)));
        Assert.Equal(RefusalCodes.VariantAlreadyLinked, refusal.Code);
    }

    private (ILexEntry[] Entries, ILexEntryType Type) Seed(int count)
    {
        var entries = new ILexEntry[count];
        ILexEntryType type = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var services = _cache.ServiceLocator;
            for (var i = 0; i < count; i++) entries[i] = services.GetInstance<ILexEntryFactory>().Create();
            var list = _cache.LangProject.LexDbOA.VariantEntryTypesOA;
            if (list is null)
            {
                list = services.GetInstance<ICmPossibilityListFactory>().Create();
                _cache.LangProject.LexDbOA.VariantEntryTypesOA = list;
            }
            type = services.GetInstance<ILexEntryTypeFactory>().Create();
            list.PossibilitiesOS.Add(type);
        });
        return (entries, type);
    }

    private static CanonicalId Id(ICmObject value) => CanonicalId.FromGuid(value.Guid);
    private static Proposal Proposal(IReadOnlyList<OperationEnvelope> operations) =>
        new(new Dictionary<string, string> { ["lexical"] = "1.0" }, CanonicalId.Mint(), null, operations);
}

using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Projection;
using SIL.Motif.Projection.Rendering;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Execution;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Runner;

[Collection(LcmCacheParallelCollections.Group1)]
public sealed class EditCliticAttachmentTests : IDisposable
{
    private readonly LcmCache _cache;
    private readonly SeededProject _seed;
    public EditCliticAttachmentTests(PristineProjectFixture pristine) { _cache = pristine.NewScratch(); _seed = pristine.Seed; }
    public void Dispose() { if (!_cache.IsDisposed) _cache.Dispose(); }

    private (ILexEntry Entry, IMoStemMsa Msa) Clitic() => Clitic(MoMorphTypeTags.kguidMorphEnclitic);

    private (ILexEntry Entry, IMoStemMsa Msa) Clitic(Guid morphType)
    {
        var entry = _cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(_seed.FirstEntryId);
        var msa = (IMoStemMsa)entry.SensesOS[0].MorphoSyntaxAnalysisRA;
        SetMorphType(entry, morphType);
        return (entry, msa);
    }

    private void SetMorphType(ILexEntry entry, Guid type) =>
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () => entry.LexemeFormOA.MorphTypeRA =
            _cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>().GetObject(type));

    private IPartOfSpeech Category(string name)
    {
        IPartOfSpeech category = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            category = _cache.ServiceLocator.GetInstance<IPartOfSpeechFactory>().Create();
            _cache.LangProject.PartsOfSpeechOA.PossibilitiesOS.Add(category);
            category.Name.set_String(_cache.DefaultAnalWs, name);
        });
        return category;
    }

    private void Attach(IMoStemMsa msa, params IPartOfSpeech[] categories) =>
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            msa.FromPartsOfSpeechRC.Clear();
            foreach (var category in categories) msa.FromPartsOfSpeechRC.Add(category);
        });

    private static CanonicalId Id(ICmObject value) => CanonicalId.FromGuid(value.Guid);
    private static CanonicalId[] Ids(params IPartOfSpeech[] values) => values.Select(Id).ToArray();
    private static Proposal Proposal(IReadOnlyList<OperationEnvelope> operations) =>
        new(new Dictionary<string, string> { ["grammar"] = "1.0" }, CanonicalId.Mint(), null, operations);

    [Fact]
    public void ExactSetEditAppliesTheDifferenceAndKeepsUnchangedMembers()
    {
        var (_, msa) = Clitic();
        IPartOfSpeech noun = Category("Noun"), pronoun = Category("Pronoun"), verb = Category("Verb");
        Attach(msa, noun, verb);
        var operations = EditCliticAttachmentComposer.Build(_cache,
            new(Id(msa), Ids(noun, verb), Ids(noun, pronoun)));
        var proposal = Proposal(operations);
        var dry = ScratchDryRun.Of(_cache, proposal);
        var effect = Assert.Single(dry.ExpectedEffects);
        Assert.Equal(Id(msa), effect.CanonicalId);
        Assert.Equal(2, effect.Before.Count);
        Assert.Equal(2, effect.After.Count);
        ProposalApplier.Apply(_cache, proposal, dry.Anchor, "tester");
        Assert.Equal(new[] { noun.Guid, pronoun.Guid }.Order(), msa.FromPartsOfSpeechRC.Select(item => item.Guid).Order());
    }

    [Fact]
    public void EmptySetRemovesTheRestrictionAndAnUnchangedSetComposesNothing()
    {
        var (_, msa) = Clitic();
        var noun = Category("Noun");
        Attach(msa, noun);
        Assert.Empty(EditCliticAttachmentComposer.Build(_cache, new(Id(msa), Ids(noun), Ids(noun))));
        var proposal = Proposal(EditCliticAttachmentComposer.Build(_cache, new(Id(msa), Ids(noun), [])));
        var dry = ScratchDryRun.Of(_cache, proposal);
        ProposalApplier.Apply(_cache, proposal, dry.Anchor, "tester");
        Assert.Empty(msa.FromPartsOfSpeechRC);
    }

    [Fact]
    public void AStemTargetIsACataloguedRefusal()
    {
        var entry = _cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(_seed.FirstEntryId);
        var msa = (IMoStemMsa)entry.SensesOS[0].MorphoSyntaxAnalysisRA;
        var error = Assert.ThrowsAny<ProposalRefusalException>(() =>
            EditCliticAttachmentComposer.Build(_cache, new(Id(msa), [], [])));
        Assert.Equal(RefusalCodes.CliticAttachmentNotClitic, error.Code);
        Assert.Equal(Id(msa).Value, error.Facts["target"]);
    }

    [Theory]
    [InlineData("proclitic")]
    [InlineData("enclitic")]
    [InlineData("clitic")]
    public void EveryCliticTypeIsAccepted(string name)
    {
        var type = name switch
        {
            "proclitic" => MoMorphTypeTags.kguidMorphProclitic,
            "enclitic" => MoMorphTypeTags.kguidMorphEnclitic,
            _ => MoMorphTypeTags.kguidMorphClitic,
        };
        var (_, msa) = Clitic(type);
        var noun = Category("Noun");
        Assert.Single(EditCliticAttachmentComposer.Build(_cache, new(Id(msa), [], Ids(noun))));
    }

    [Fact]
    public void AnAffixAnalysisIsNotAStemTarget()
    {
        IMoInflAffMsa affix = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var entry = _cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create();
            affix = _cache.ServiceLocator.GetInstance<IMoInflAffMsaFactory>().Create();
            entry.MorphoSyntaxAnalysesOC.Add(affix);
        });
        Assert.ThrowsAny<InvalidOperationException>(() =>
            EditCliticAttachmentComposer.Build(_cache, new(Id(affix), [], [])));
    }

    [Fact]
    public void ANonCategoryIdIsACataloguedRefusal()
    {
        var (entry, msa) = Clitic();
        var error = Assert.ThrowsAny<ProposalRefusalException>(() =>
            EditCliticAttachmentComposer.Build(_cache, new(Id(msa), [], [Id(entry)])));
        Assert.Equal(RefusalCodes.CliticAttachmentInvalidCategory, error.Code);
        Assert.Equal(Id(entry).Value, error.Facts["category"]);
        error = Assert.ThrowsAny<ProposalRefusalException>(() =>
            EditCliticAttachmentComposer.Build(_cache, new(Id(msa), [], [CanonicalId.Mint()])));
        Assert.Equal(RefusalCodes.CliticAttachmentInvalidCategory, error.Code);
    }

    [Fact]
    public void AMismatchedExpectedSetIsStaleAtComposeAndAtApply()
    {
        var (_, msa) = Clitic();
        IPartOfSpeech noun = Category("Noun"), pronoun = Category("Pronoun");
        Attach(msa, noun);
        var error = Assert.ThrowsAny<ProposalRefusalException>(() =>
            EditCliticAttachmentComposer.Build(_cache, new(Id(msa), [], Ids(pronoun))));
        Assert.Equal(RefusalCodes.CliticAttachmentStaleSource, error.Code);
        Assert.Equal(Id(noun).Value, error.Facts["actualCategories"]);
        var proposal = Proposal(EditCliticAttachmentComposer.Build(_cache, new(Id(msa), Ids(noun), Ids(pronoun))));
        Attach(msa, pronoun);
        error = Assert.ThrowsAny<ProposalRefusalException>(() => ScratchDryRun.Of(_cache, proposal));
        Assert.Equal(RefusalCodes.CliticAttachmentStaleSource, error.Code);
    }

    [Fact]
    public void MorphTypeThenAttachmentInOneProposalDoesNotRefuseOnPreDraftState()
    {
        var entry = _cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(_seed.FirstEntryId);
        var msa = (IMoStemMsa)entry.SensesOS[0].MorphoSyntaxAnalysisRA;
        var original = entry.LexemeFormOA.MorphTypeRA.Guid;
        IPartOfSpeech noun = Category("Noun"), adverb = Category("Adverb");
        var enclitic = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphEnclitic);
        var morphType = EditMorphTypeComposer.Build(_cache, new(Id(entry.LexemeFormOA), enclitic));
        SetMorphType(entry, MoMorphTypeTags.kguidMorphEnclitic);
        var attachment = EditCliticAttachmentComposer.Build(_cache, new(Id(msa), [], Ids(noun, adverb)));
        SetMorphType(entry, original);
        var proposal = Proposal(morphType.Concat(attachment).ToArray());
        var dry = ScratchDryRun.Of(_cache, proposal);
        Assert.Equal(2, dry.ExpectedEffects.Count);
        ProposalApplier.Apply(_cache, proposal, dry.Anchor, "tester");
        Assert.Equal(MoMorphTypeTags.kguidMorphEnclitic, entry.LexemeFormOA.MorphTypeRA.Guid);
        Assert.Equal(new[] { noun.Guid, adverb.Guid }.Order(), msa.FromPartsOfSpeechRC.Select(item => item.Guid).Order());
    }

    [Fact]
    public void AttachmentWithoutTheEarlierMorphTypeStepIsRefusedWhenItRuns()
    {
        var (_, msa) = Clitic();
        var noun = Category("Noun");
        var attachment = EditCliticAttachmentComposer.Build(_cache, new(Id(msa), [], Ids(noun)));
        SetMorphType(_cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(_seed.FirstEntryId),
            MoMorphTypeTags.kguidMorphStem);
        var error = Assert.ThrowsAny<ProposalRefusalException>(() => ScratchDryRun.Of(_cache, Proposal(attachment)));
        Assert.Equal(RefusalCodes.CliticAttachmentNotClitic, error.Code);
    }

    [Fact]
    public void DryRunTouchesOnlyFromPartsOfSpeechAndRendersBeforeAndAfterNames()
    {
        var (_, msa) = Clitic();
        IPartOfSpeech noun = Category("Noun"), verb = Category("Verb"), particle = Category("Particle");
        Attach(msa, noun, verb);
        var proposal = Proposal(EditCliticAttachmentComposer.Build(_cache,
            new(Id(msa), Ids(noun, verb), Ids(noun, particle))));
        var dry = ScratchDryRun.Of(_cache, proposal);
        var effect = Assert.Single(dry.ExpectedEffects);
        Assert.Equal("grammar/moStemMsa/fromPartsOfSpeech", effect.Field);
        var text = CommandTextRenderer.Render(DryRunProjectionBuilder.Build(proposal.ProposalId.Value, dry, proposal));
        Assert.Contains("Attaches to Categories: Noun, Verb -> Noun, Particle", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RestrictionOffToACategoryRendersAnyAsTheBeforeSide()
    {
        var (_, msa) = Clitic();
        var noun = Category("Noun");
        var proposal = Proposal(EditCliticAttachmentComposer.Build(_cache, new(Id(msa), [], Ids(noun))));
        var text = CommandTextRenderer.Render(DryRunProjectionBuilder.Build(
            proposal.ProposalId.Value, ScratchDryRun.Of(_cache, proposal), proposal));
        Assert.Contains("Attaches to Categories: (any) -> Noun", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ParserRejectsRepeatedMissingAndUnknownFields()
    {
        var id = CanonicalId.Mint().Value;
        Assert.ThrowsAny<Exception>(() => EditCliticAttachmentIntentParser.Parse(JsonDocument.Parse(
            $$"""{"target":"{{id}}","expectedCategories":["{{id}}","{{id}}"],"categories":[]}""").RootElement));
        Assert.ThrowsAny<Exception>(() => EditCliticAttachmentIntentParser.Parse(JsonDocument.Parse(
            $$"""{"target":"{{id}}","categories":[]}""").RootElement));
        Assert.ThrowsAny<Exception>(() => EditCliticAttachmentIntentParser.Parse(JsonDocument.Parse(
            $$"""{"target":"{{id}}","expectedCategories":[],"categories":[],"extra":1}""").RootElement));
    }
}

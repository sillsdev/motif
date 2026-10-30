using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Projection;
using SIL.Motif.Projection.Rendering;
using System.Text.Json;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Execution;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Runner;

[Collection(LcmCacheParallelCollections.Group1)]
public sealed class EditMorphTypeTests : IDisposable
{
    private readonly LcmCache _cache;
    private readonly SeededProject _seed;
    public EditMorphTypeTests(PristineProjectFixture pristine) { _cache = pristine.NewScratch(); _seed = pristine.Seed; }
    public void Dispose() { if (!_cache.IsDisposed) _cache.Dispose(); }

    [Fact]
    public void StemToEncliticReportsOnlyTheSelectedFormAndPreservesIdentityAndOtherForms()
    {
        var entry = _cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(_seed.FirstEntryId);
        var form = entry.LexemeFormOA;
        var original = form.MorphTypeRA.Guid;
        var analysis = (IMoStemMsa)entry.SensesOS[0].MorphoSyntaxAnalysisRA;
        var partOfSpeech = analysis.PartOfSpeechRA.Guid;
        IMoStemAllomorph alternate = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            alternate = _cache.ServiceLocator.GetInstance<IMoStemAllomorphFactory>().Create();
            entry.AlternateFormsOS.Add(alternate);
            alternate.MorphTypeRA = form.MorphTypeRA;
        });
        var proposal = Proposal(EditMorphTypeComposer.Build(_cache, new(Id(form), Type(MoMorphTypeTags.kguidMorphEnclitic))));
        var dry = ScratchDryRun.Of(_cache, proposal);
        var effect = Assert.Single(dry.ExpectedEffects);
        Assert.Equal(Id(form), effect.CanonicalId);
        Assert.Equal(original, CanonicalId.Parse(effect.Before["ref"]).ToGuid());
        Assert.Equal(MoMorphTypeTags.kguidMorphEnclitic, CanonicalId.Parse(effect.After["ref"]).ToGuid());
        Assert.Equal(original, form.MorphTypeRA.Guid);
        ProposalApplier.Apply(_cache, proposal, dry.Anchor, "tester");
        Assert.Equal(MoMorphTypeTags.kguidMorphEnclitic, form.MorphTypeRA.Guid);
        Assert.Equal(original, alternate.MorphTypeRA.Guid);
        Assert.Equal(form.Guid, entry.LexemeFormOA.Guid);
        Assert.Equal(analysis.Guid, entry.SensesOS[0].MorphoSyntaxAnalysisRA.Guid);
        Assert.Equal(partOfSpeech, analysis.PartOfSpeechRA.Guid);
    }

    public static IEnumerable<object[]> StemFamily() => new[]
    {
        MoMorphTypeTags.kguidMorphRoot, MoMorphTypeTags.kguidMorphBoundRoot,
        MoMorphTypeTags.kguidMorphStem, MoMorphTypeTags.kguidMorphBoundStem,
        MoMorphTypeTags.kguidMorphParticle, MoMorphTypeTags.kguidMorphClitic,
        MoMorphTypeTags.kguidMorphProclitic, MoMorphTypeTags.kguidMorphEnclitic,
        MoMorphTypeTags.kguidMorphPhrase, MoMorphTypeTags.kguidMorphDiscontiguousPhrase,
    }.Select(id => new object[] { id });

    [Theory]
    [MemberData(nameof(StemFamily))]
    public void FieldWorksStemFamilyIsAllowed(Guid desired)
    {
        var form = _cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(_seed.FirstEntryId).LexemeFormOA;
        var operations = EditMorphTypeComposer.Build(_cache, new(Id(form), Type(desired)));
        Assert.Equal(form.MorphTypeRA.Guid == desired ? 0 : 1, operations.Count);
    }

    [Fact]
    public void CrossFamilyEditsAreCataloguedRefusalsInBothDirections()
    {
        var entry = _cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(_seed.FirstEntryId);
        var stem = entry.LexemeFormOA;
        var error = Assert.ThrowsAny<ProposalRefusalException>(() =>
            EditMorphTypeComposer.Build(_cache, new(Id(stem), Type(MoMorphTypeTags.kguidMorphPrefix))));
        Assert.Equal(RefusalCodes.MorphTypeIncompatibleFamily, error.Code);
        IMoAffixAllomorph affix = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var affixEntry = _cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create();
            affix = _cache.ServiceLocator.GetInstance<IMoAffixAllomorphFactory>().Create();
            affixEntry.LexemeFormOA = affix;
            affix.MorphTypeRA = _cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphPrefix);
        });
        error = Assert.ThrowsAny<ProposalRefusalException>(() =>
            EditMorphTypeComposer.Build(_cache, new(Id(affix), Type(MoMorphTypeTags.kguidMorphStem))));
        Assert.Equal(RefusalCodes.MorphTypeIncompatibleFamily, error.Code);
    }

    [Fact]
    public void ChangedSourceTypeIsRefusedWhenTheProposalRuns()
    {
        var form = _cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(_seed.FirstEntryId).LexemeFormOA;
        var proposal = Proposal(EditMorphTypeComposer.Build(_cache, new(Id(form), Type(MoMorphTypeTags.kguidMorphEnclitic))));
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () => form.MorphTypeRA =
            _cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphRoot));
        var refusal = Assert.ThrowsAny<ProposalRefusalException>(() => ScratchDryRun.Of(_cache, proposal));
        Assert.Equal(RefusalCodes.MorphTypeStaleSource, refusal.Code);
        Assert.Equal(Id(form).Value, refusal.Facts["form"]);
        Assert.Equal(Type(MoMorphTypeTags.kguidMorphRoot).Value, refusal.Facts["actualMorphType"]);
    }

    [Fact]
    public void RenderedDryRunShowsTheSelectedFormAndBothTypeNames()
    {
        var form = _cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(_seed.FirstEntryId).LexemeFormOA;
        var enclitic = _cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphEnclitic);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            form.MorphTypeRA.Name.set_String(_cache.DefaultAnalWs, "Stem");
            enclitic.Name.set_String(_cache.DefaultAnalWs, "Enclitic");
        });
        var proposal = Proposal(EditMorphTypeComposer.Build(_cache, new(Id(form), Id(enclitic))));
        var dry = ScratchDryRun.Of(_cache, proposal);
        var projection = DryRunProjectionBuilder.Build(proposal.ProposalId.Value, dry, proposal);
        var text = CommandTextRenderer.Render(projection);
        Assert.Contains(Id(form).Value, text, StringComparison.Ordinal);
        Assert.Contains("morph type: Stem -> Enclitic", text, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(ProjectionJson.Serialize(projection));
        var effect = Assert.Single(json.RootElement.GetProperty("effects").EnumerateArray());
        Assert.Equal(Id(form).Value, effect.GetProperty("canonicalId").GetString());
        Assert.Equal("Stem", effect.GetProperty("preview").GetProperty("beforeTypeName").GetString());
        Assert.Equal("Enclitic", effect.GetProperty("preview").GetProperty("afterTypeName").GetString());
    }

    private static CanonicalId Id(ICmObject value) => CanonicalId.FromGuid(value.Guid);
    private static CanonicalId Type(Guid value) => CanonicalId.FromGuid(value);
    private static Proposal Proposal(IReadOnlyList<OperationEnvelope> operations) =>
        new(new Dictionary<string, string> { ["grammar"] = "1.0" }, CanonicalId.Mint(), null, operations);
}

using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using static SIL.Motif.Tests.Composers.AffixTestSupport;

namespace SIL.Motif.Tests.Composers;

/// <summary>An entry gains a second grammatical info, and its senses can point at either one.</summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class AddMorphoSyntaxAnalysisComposerTests : IDisposable
{
    private readonly LcmCache _cache;
    private readonly CanonicalId _verb;
    private readonly CanonicalId _copula;
    private readonly CanonicalId _suffix;
    private readonly CanonicalId _firstMsa;

    public AddMorphoSyntaxAnalysisComposerTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        _verb = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
        _copula = AddCategory(_cache, "Copula");
        var created = ApplyOps(_cache, AuthorLexicalEntryComposer.Build(_cache, new AuthorLexicalEntryIntent("ig",
            Vern(_cache), MorphType(MoMorphTypeTags.kguidMorphSuffix), "not", Anal(_cache),
            new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, _verb))));
        _suffix = Created(created, LexDbEntriesOperationKinds.Create);
        _firstMsa = Created(created, LexEntryMorphoSyntaxAnalysesOperationKinds.Create);
    }

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [Fact]
    public void ASecondAnalysisKeepsTheFirstAndANewSenseOrAnExistingSenseCanPointAtIt()
    {
        var entry = Get<ILexEntry>(_cache, _suffix);
        var firstSense = Id(entry.SensesOS[0]);
        var proposal = ApplyOps(_cache, AddMorphoSyntaxAnalysisComposer.Build(_cache, new(_suffix,
            new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, _copula), Gloss: "not (copula)",
            AnalysisWritingSystem: Anal(_cache), Definition: "negative copula")));
        var second = Created(proposal, LexEntryMorphoSyntaxAnalysesOperationKinds.Create);

        Assert.Equal(2, entry.MorphoSyntaxAnalysesOC.Count);
        Assert.Equal(_copula.ToGuid(), Get<IMoInflAffMsa>(_cache, second).PartOfSpeechRA.Guid);
        Assert.Equal(_verb.ToGuid(), Get<IMoInflAffMsa>(_cache, _firstMsa).PartOfSpeechRA.Guid);
        Assert.Equal(2, entry.SensesOS.Count);
        Assert.Equal(_firstMsa.ToGuid(), entry.SensesOS[0].MorphoSyntaxAnalysisRA.Guid);
        Assert.Equal(second.ToGuid(), entry.SensesOS[1].MorphoSyntaxAnalysisRA.Guid);
        Assert.Equal("not (copula)", entry.SensesOS[1].Gloss.get_String(_cache.DefaultAnalWs).Text);

        var third = Created(ApplyOps(_cache, AddMorphoSyntaxAnalysisComposer.Build(_cache, new(_suffix,
            new LexicalEntryMsa(LexicalEntryMsaKind.Unclassified, _copula), Senses: [firstSense]))),
            LexEntryMorphoSyntaxAnalysesOperationKinds.Create);
        Assert.Equal(third.ToGuid(), entry.SensesOS[0].MorphoSyntaxAnalysisRA.Guid);
        // LibLCM drops an analysis once no sense uses it, which is why the first one is gone here.
        Assert.Equal(new[] { second.ToGuid(), third.ToGuid() }.Order(), entry.MorphoSyntaxAnalysesOC.Select(msa => msa.Guid).Order());
    }

    [Fact]
    public void AStemAnalysisOnAnAffixAnAffixAnalysisOnAStemAndASenseOfAnotherEntryAreRefused()
    {
        var stem = AddStem(_cache, "kaun", _verb);
        var other = Get<ILexEntry>(_cache, stem.Entry);
        var foreign = Id(other.SensesOS[0]);

        var onAffix = Assert.Throws<InvalidOperationException>(() => AddMorphoSyntaxAnalysisComposer.Build(_cache,
            new(_suffix, new LexicalEntryMsa(LexicalEntryMsaKind.Stem, _copula))));
        Assert.Contains("affix form", onAffix.Message, StringComparison.Ordinal);
        var onStem = Assert.Throws<InvalidOperationException>(() => AddMorphoSyntaxAnalysisComposer.Build(_cache,
            new(stem.Entry, new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, _copula))));
        Assert.Contains("stem form", onStem.Message, StringComparison.Ordinal);
        var wrongSense = Assert.Throws<InvalidOperationException>(() => AddMorphoSyntaxAnalysisComposer.Build(_cache,
            new(_suffix, new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, _copula), Senses: [foreign])));
        Assert.Contains("does not belong to entry", wrongSense.Message, StringComparison.Ordinal);
        var derivation = Assert.Throws<InvalidOperationException>(() => AddMorphoSyntaxAnalysisComposer.Build(_cache,
            new(_suffix, new LexicalEntryMsa(LexicalEntryMsaKind.Derivational, _copula))));
        Assert.Contains("toCategory", derivation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AGlossNeedsItsWritingSystemAndADefinitionNeedsAGloss()
    {
        var entry = JsonSerializer.SerializeToElement(new { entry = _suffix.Value,
            msa = new { kind = "inflectional", category = _copula.Value }, gloss = "x" });
        Assert.Throws<ContractParseException>(() => AddMorphoSyntaxAnalysisIntentParser.Parse(entry));
        var definition = JsonSerializer.SerializeToElement(new { entry = _suffix.Value,
            msa = new { kind = "inflectional", category = _copula.Value }, definition = "x" });
        Assert.Throws<ContractParseException>(() => AddMorphoSyntaxAnalysisIntentParser.Parse(definition));
        var extra = JsonSerializer.SerializeToElement(new { entry = _suffix.Value,
            msa = new { kind = "inflectional", category = _copula.Value }, surprise = true });
        Assert.Throws<ContractParseException>(() => AddMorphoSyntaxAnalysisIntentParser.Parse(extra));
    }

    [Fact]
    public void AnAnalysisNoSenseWouldUseIsRefused()
    {
        var neither = Assert.Throws<InvalidOperationException>(() => AddMorphoSyntaxAnalysisComposer.Build(_cache,
            new(_suffix, new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, _copula))));
        Assert.Contains("'senses' or 'gloss'", neither.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnIdenticalAnalysisIsRefused()
    {
        var gloss = Anal(_cache);
        var same = Assert.Throws<InvalidOperationException>(() => AddMorphoSyntaxAnalysisComposer.Build(_cache,
            new(_suffix, new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, _verb), Gloss: "again",
                AnalysisWritingSystem: gloss)));
        Assert.Contains("identical analysis", same.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MovingTheLastSenseOffAnAnalysisSaysItGoesAwayAndIsRefusedWhileAProhibitionOrABundleStillUsesIt()
    {
        var entry = Get<ILexEntry>(_cache, _suffix);
        var firstSense = Id(entry.SensesOS[0]);
        var move = new AddMorphoSyntaxAnalysisIntent(_suffix,
            new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, _copula), Senses: [firstSense]);

        var clean = AddMorphoSyntaxAnalysisComposer.Build(_cache, move);
        var repoint = Assert.Single(clean, op => op.Kind == LexSenseMorphoSyntaxAnalysisOperationKinds.SetMorphoSyntaxAnalysis);
        Assert.Contains("removes that analysis", repoint.Rationale, StringComparison.Ordinal);
        Assert.Contains(_firstMsa.Value, repoint.Rationale, StringComparison.Ordinal);

        var (_, stem, _) = AddStem(_cache, "kaun", _verb);
        var prohibition = Created(ApplyOps(_cache, AuthorAdhocProhibitionComposer.Build(_cache,
            new("morpheme", _firstMsa, [stem], "anywhere"))), AdhocProhibitionCreateOperationKinds.Create);
        var byProhibition = Assert.Throws<InvalidOperationException>(() => AddMorphoSyntaxAnalysisComposer.Build(_cache, move));
        Assert.Contains("ad hoc prohibition", byProhibition.Message, StringComparison.Ordinal);
        Assert.Contains("leaving it out of 'senses'", byProhibition.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("compose-change-affix-kind", byProhibition.Message, StringComparison.Ordinal);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () => Get<ICmObject>(_cache, prohibition).Delete());

        var bundle = AddBundle(_firstMsa);
        var byBundle = Assert.Throws<InvalidOperationException>(() => AddMorphoSyntaxAnalysisComposer.Build(_cache, move));
        Assert.Contains("morph bundle", byBundle.Message, StringComparison.Ordinal);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () => Get<ICmObject>(_cache, bundle).Delete());
    }

    [Fact]
    public void MovingTheLastSenseOffASlottedAnalysisIsAllowedAndTheRationaleNamesTheSlotsThatGoWithIt()
    {
        var entry = Get<ILexEntry>(_cache, _suffix);
        var slot = Created(ApplyOps(_cache, AuthorAffixSlotComposer.Build(_cache,
            new AuthorAffixSlotIntent(_verb, "Outer", Anal(_cache), true))), PartOfSpeechAffixSlotsOperationKinds.Create);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            Get<IMoInflAffMsa>(_cache, _firstMsa).SlotsRC.Add(Get<IMoInflAffixSlot>(_cache, slot)));
        var operations = AddMorphoSyntaxAnalysisComposer.Build(_cache, new(_suffix,
            new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, _copula), Senses: [Id(entry.SensesOS[0])]));
        var repoint = Assert.Single(operations, op => op.Kind == LexSenseMorphoSyntaxAnalysisOperationKinds.SetMorphoSyntaxAnalysis);
        Assert.Contains("membership of slot", repoint.Rationale, StringComparison.Ordinal);
        Assert.Contains("Outer", repoint.Rationale, StringComparison.Ordinal);
        Assert.Contains(slot.ToGuid().ToString("D"), repoint.Rationale, StringComparison.Ordinal);
    }

    [Fact]
    public void SensesMovedTogetherAreCountedAsASetAndABundleOfAMovedSenseDoesNotBlock()
    {
        var entry = Get<ILexEntry>(_cache, _suffix);
        var second = AddSenseOn(entry, _firstMsa);
        var both = new AddMorphoSyntaxAnalysisIntent(_suffix,
            new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, _copula), Senses: [Id(entry.SensesOS[0]), second]);
        var (_, stem, _) = AddStem(_cache, "kaun", _verb);
        var prohibition = Created(ApplyOps(_cache, AuthorAdhocProhibitionComposer.Build(_cache,
            new("morpheme", _firstMsa, [stem], "anywhere"))), AdhocProhibitionCreateOperationKinds.Create);
        // Moving just one of the two leaves the other on the old analysis, so the prohibition does not matter.
        var one = AddMorphoSyntaxAnalysisComposer.Build(_cache, new(_suffix,
            new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, _copula), Senses: [second]));
        Assert.All(one.Where(op => op.Kind == LexSenseMorphoSyntaxAnalysisOperationKinds.SetMorphoSyntaxAnalysis),
            op => Assert.DoesNotContain("removes that analysis", op.Rationale, StringComparison.Ordinal));
        // Moving both together empties the old analysis, which the prohibition still uses.
        Assert.Throws<InvalidOperationException>(() => AddMorphoSyntaxAnalysisComposer.Build(_cache, both));
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () => Get<ICmObject>(_cache, prohibition).Delete());

        var set = AddMorphoSyntaxAnalysisComposer.Build(_cache, both);
        var repoints = set.Where(op => op.Kind == LexSenseMorphoSyntaxAnalysisOperationKinds.SetMorphoSyntaxAnalysis).ToArray();
        Assert.Equal(2, repoints.Length);
        Assert.All(repoints, op => Assert.Contains("removes that analysis", op.Rationale, StringComparison.Ordinal));

        // A bundle whose own sense moves does not block: LibLCM retargets it with the sense.
        var bundle = AddBundle(_firstMsa);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            Get<IWfiMorphBundle>(_cache, bundle).SenseRA = entry.SensesOS[0]);
        var bundled = AddMorphoSyntaxAnalysisComposer.Build(_cache, new(_suffix,
            new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, _copula), Senses: [Id(entry.SensesOS[0]), second]));
        Assert.NotEmpty(bundled);
    }

    private CanonicalId AddSenseOn(ILexEntry entry, CanonicalId msa)
    {
        ILexSense sense = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            sense = _cache.ServiceLocator.GetInstance<ILexSenseFactory>().Create();
            entry.SensesOS.Add(sense);
            sense.MorphoSyntaxAnalysisRA = Get<IMoMorphSynAnalysis>(_cache, msa);
        });
        return Id(sense);
    }

    private CanonicalId AddBundle(CanonicalId msa)
    {
        IWfiMorphBundle bundle = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var wordform = _cache.ServiceLocator.GetInstance<IWfiWordformFactory>().Create(
                TsStringUtils.MakeString("ig", _cache.DefaultVernWs));
            var analysis = _cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
            wordform.AnalysesOC.Add(analysis);
            bundle = _cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create();
            analysis.MorphBundlesOS.Add(bundle);
            bundle.MsaRA = Get<IMoMorphSynAnalysis>(_cache, msa);
        });
        return Id(bundle);
    }
}

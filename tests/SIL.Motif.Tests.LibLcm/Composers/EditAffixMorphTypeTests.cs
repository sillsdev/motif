using SIL.LCModel;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Execution;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using static SIL.Motif.Tests.Composers.AffixTestSupport;

namespace SIL.Motif.Tests.Composers;

/// <summary>Correcting an affix's morph type in place, for an ordinary affix form and for an affix process.</summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class EditAffixMorphTypeTests : IDisposable
{
    private readonly LcmCache _cache;
    private readonly CanonicalId _category;

    public EditAffixMorphTypeTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        _category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
    }

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    public static IEnumerable<object[]> AffixTypes() => new[]
    {
        MoMorphTypeTags.kguidMorphSuffix, MoMorphTypeTags.kguidMorphInfix, MoMorphTypeTags.kguidMorphSimulfix,
        MoMorphTypeTags.kguidMorphSuprafix, MoMorphTypeTags.kguidMorphPrefixingInterfix,
        MoMorphTypeTags.kguidMorphSuffixingInterfix, MoMorphTypeTags.kguidMorphInfixingInterfix,
    }.Select(id => new object[] { id });

    [Theory]
    [MemberData(nameof(AffixTypes))]
    public void AnOrdinaryPrefixChangesToAnotherAffixTypeInPlaceKeepingItsIdentityAndGrammaticalInfo(Guid desired)
    {
        var (entryId, msa, formId) = AddInflectionalPrefix(_cache, "mi", _category);

        var operations = EditMorphTypeComposer.Build(_cache, new(formId, MorphType(desired)));

        Assert.Single(operations);
        ApplyOps(_cache, operations);
        var entry = Get<ILexEntry>(_cache, entryId);
        Assert.Equal(formId.ToGuid(), entry.LexemeFormOA.Guid);
        Assert.Equal(desired, entry.LexemeFormOA.MorphTypeRA.Guid);
        Assert.Equal(msa.ToGuid(), Assert.Single(entry.MorphoSyntaxAnalysesOC).Guid);
    }

    [Fact]
    public void AnAffixProcessChangesItsMorphTypeInPlace()
    {
        var created = ApplyOps(_cache, AuthorAffixProcessComposer.Build(_cache, new AuthorAffixProcessIntent(
            MorphType(MoMorphTypeTags.kguidMorphPrefix), "repeat", Anal(_cache),
            new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, _category),
            [new("rest", Remaining: true)], [new(Copy: "rest"), new(Copy: "rest")])));
        var form = Created(created, LexEntryLexemeFormOperationKinds.CreateLexemeForm);

        ApplyOps(_cache, EditMorphTypeComposer.Build(_cache, new(form, MorphType(MoMorphTypeTags.kguidMorphSuffix))));

        var process = Assert.IsAssignableFrom<IMoAffixProcess>(Get<IMoForm>(_cache, form));
        Assert.Equal(MoMorphTypeTags.kguidMorphSuffix, process.MorphTypeRA.Guid);
    }

    [Fact]
    public void ACircumfixIsRefusedInBothDirectionsBecauseItIsTwoPartsInOneEntry()
    {
        var (_, _, formId) = AddInflectionalPrefix(_cache, "mi", _category);

        var into = Assert.ThrowsAny<ProposalRefusalException>(() =>
            EditMorphTypeComposer.Build(_cache, new(formId, MorphType(MoMorphTypeTags.kguidMorphCircumfix))));
        Assert.Equal(RefusalCodes.MorphTypeIncompatibleFamily, into.Code);
        Assert.Contains("circumfix", into.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAffixFormIsStillRefusedAStemTypeAndAStemFormAnAffixType()
    {
        var (_, _, formId) = AddInflectionalPrefix(_cache, "mi", _category);
        var toStem = Assert.ThrowsAny<ProposalRefusalException>(() =>
            EditMorphTypeComposer.Build(_cache, new(formId, MorphType(MoMorphTypeTags.kguidMorphStem))));
        Assert.Equal(RefusalCodes.MorphTypeIncompatibleFamily, toStem.Code);

        var (_, _, stemForm) = AddStem(_cache, "kaun", _category);
        var toAffix = Assert.ThrowsAny<ProposalRefusalException>(() =>
            EditMorphTypeComposer.Build(_cache, new(stemForm, MorphType(MoMorphTypeTags.kguidMorphSuffix))));
        Assert.Equal(RefusalCodes.MorphTypeIncompatibleFamily, toAffix.Code);
    }
}

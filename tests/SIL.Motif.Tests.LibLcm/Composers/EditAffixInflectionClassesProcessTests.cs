using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using static SIL.Motif.Tests.Composers.AffixTestSupport;

namespace SIL.Motif.Tests.Composers;

/// <summary>Editing the inflection classes of an inflectional reduplication, which is an affix process.</summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class EditAffixInflectionClassesProcessTests : IDisposable
{
    private readonly LcmCache _cache;
    private readonly CanonicalId _category;

    public EditAffixInflectionClassesProcessTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        _category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
    }

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [Fact]
    public void AnInflectionalReduplicationTakesAndLosesItsClassesThroughTheSameComposer()
    {
        var first = AddInflectionClass(_cache, _category, "Class 1");
        var second = AddInflectionClass(_cache, _category, "Class 2");
        var form = Reduplication(LexicalEntryMsaKind.Inflectional);

        var add = EditAffixInflectionClassesComposer.Build(_cache, new(form, [], [first, second]));
        Assert.All(add, operation => Assert.Equal(MoAffixFormInflectionClassesOperationKinds.AddRefInflectionClasses, operation.Kind));
        Assert.Equal(2, add.Count);
        ApplyOps(_cache, add);
        var process = Assert.IsAssignableFrom<IMoAffixProcess>(Get<IMoForm>(_cache, form));
        Assert.Equal(new[] { first.ToGuid(), second.ToGuid() }.Order(), process.InflectionClassesRC.Select(item => item.Guid).Order());

        var replace = EditAffixInflectionClassesComposer.Build(_cache, new(form, [first, second], [second]));
        Assert.Equal(MoAffixFormInflectionClassesOperationKinds.RemoveRefInflectionClasses, Assert.Single(replace).Kind);
        ApplyOps(_cache, replace);
        Assert.Equal([second.ToGuid()], process.InflectionClassesRC.Select(item => item.Guid));
    }

    [Fact]
    public void ADerivationalReductionStillPointsToTheDerivationalRestrictionAndTheExpectedSetIsChecked()
    {
        var cls = AddInflectionClass(_cache, _category, "Class 1");
        var derivational = Reduplication(LexicalEntryMsaKind.Derivational);
        Assert.Throws<InvalidOperationException>(() =>
            EditAffixInflectionClassesComposer.Build(_cache, new(derivational, [], [cls])));

        var inflectional = Reduplication(LexicalEntryMsaKind.Inflectional);
        var stale = Assert.Throws<InvalidOperationException>(() =>
            EditAffixInflectionClassesComposer.Build(_cache, new(inflectional, [cls], [])));
        Assert.Contains("differ from the expected set", stale.Message, StringComparison.Ordinal);
    }

    private CanonicalId Reduplication(LexicalEntryMsaKind kind)
    {
        var msa = kind == LexicalEntryMsaKind.Derivational
            ? new LexicalEntryMsa(kind, _category, _category) : new LexicalEntryMsa(kind, _category);
        var created = ApplyOps(_cache, AuthorAffixProcessComposer.Build(_cache, new AuthorAffixProcessIntent(
            MorphType(MoMorphTypeTags.kguidMorphPrefix), "repeat " + kind, Anal(_cache), msa,
            [new("rest", Remaining: true)], [new(Copy: "rest"), new(Copy: "rest")])));
        return Created(created, LexEntryLexemeFormOperationKinds.CreateLexemeForm);
    }
}

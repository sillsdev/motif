using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;
using static SIL.Motif.Tests.Composers.AffixTestSupport;

namespace SIL.Motif.Tests.Composers;

/// <summary>A null allomorph is an affix allomorph with an empty form, as FieldWorks stores it.</summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class NullAllomorphTests(PristineProjectFixture pristine)
{
    private static AuthorLexicalEntryIntent Intent(LcmCache cache, string form, Guid morphType, LexicalEntryMsa msa) =>
        new(form, Vern(cache), MorphType(morphType), "pst", Anal(cache), msa);

    [Fact]
    public void AnEmptyAffixFormIsAcceptedAndStoredAsTheNullAffixFormTheParserReads()
    {
        using var cache = pristine.NewScratch();
        var verb = AddCategory(cache, "Verb");
        var created = ApplyOps(cache, AuthorLexicalEntryComposer.Build(cache, Intent(cache, "",
            MoMorphTypeTags.kguidMorphSuffix, new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, verb))));

        var form = Get<IMoAffixAllomorph>(cache, Created(created, LexEntryLexemeFormOperationKinds.CreateLexemeForm));
        Assert.Equal("∅", form.Form.get_String(cache.DefaultVernWs).Text);
        Assert.IsAssignableFrom<IMoAffixAllomorph>(form);
        Assert.Equal(MoMorphTypeTags.kguidMorphSuffix, form.MorphTypeRA.Guid);
    }

    [Fact]
    public void AWhitespaceOnlyAffixFormIsAlsoANullAllomorphNotSpaces()
    {
        using var cache = pristine.NewScratch();
        var verb = AddCategory(cache, "Verb");
        var created = ApplyOps(cache, AuthorLexicalEntryComposer.Build(cache, Intent(cache, "  ",
            MoMorphTypeTags.kguidMorphSuffix, new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, verb))));

        var form = Get<IMoAffixAllomorph>(cache, Created(created, LexEntryLexemeFormOperationKinds.CreateLexemeForm));
        Assert.Equal("∅", form.Form.get_String(cache.DefaultVernWs).Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankStemIsStillRefusedAndTheRefusalSaysHowToAuthorANullAffix(string form)
    {
        using var cache = pristine.NewScratch();
        var verb = AddCategory(cache, "Verb");
        var refusal = Assert.Throws<InvalidOperationException>(() => AuthorLexicalEntryComposer.Build(cache,
            Intent(cache, form, MoMorphTypeTags.kguidMorphStem, new LexicalEntryMsa(LexicalEntryMsaKind.Stem, verb))));
        Assert.Contains("a stem needs a form", refusal.Message);
        Assert.Contains("null allomorph", refusal.Message);
        Assert.Contains("\"∅\"", refusal.Message);
        Assert.Contains("\"0\"", refusal.Message);
    }

    [Fact]
    public void ABlankGlossIsStillRefused()
    {
        using var cache = pristine.NewScratch();
        var verb = AddCategory(cache, "Verb");
        var intent = Intent(cache, "", MoMorphTypeTags.kguidMorphSuffix,
            new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, verb)) with { Gloss = " " };
        Assert.Contains("'gloss' must not be blank", Assert.Throws<InvalidOperationException>(
            () => AuthorLexicalEntryComposer.Build(cache, intent)).Message);
    }

    [Fact]
    public void AnEmptyAlternateAffixFormIsAcceptedAndABlankStemAlternateIsRefused()
    {
        using var cache = pristine.NewScratch();
        var verb = AddCategory(cache, "Verb");
        var suffix = ApplyOps(cache, AuthorLexicalEntryComposer.Build(cache, Intent(cache, "ta",
            MoMorphTypeTags.kguidMorphSuffix, new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, verb))));
        var stem = AddStem(cache, "kaun", verb);

        var alternate = ApplyOps(cache, AuthorAlternateFormComposer.Build(cache, new AuthorAlternateFormIntent(
            Created(suffix, LexDbEntriesOperationKinds.Create), "", Vern(cache),
            MorphType(MoMorphTypeTags.kguidMorphSuffix))));
        var form = Get<IMoAffixAllomorph>(cache, Created(alternate, LexEntryAlternateFormsCreateOperationKinds.CreateAlternateForms));
        Assert.Equal("∅", form.Form.get_String(cache.DefaultVernWs).Text);

        var refusal = Assert.Throws<InvalidOperationException>(() => AuthorAlternateFormComposer.Build(cache,
            new AuthorAlternateFormIntent(stem.Entry, "", Vern(cache), MorphType(MoMorphTypeTags.kguidMorphStem))));
        Assert.Contains("null allomorph", refusal.Message);
    }
}

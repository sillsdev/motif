using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using static SIL.Motif.Tests.Composers.AffixTestSupport;

namespace SIL.Motif.Tests.Composers;

/// <summary>A circumfix is authored as FieldWorks holds it: a circumfix lexeme form and a prefix and a suffix form.</summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class AuthorCircumfixComposerTests : IDisposable
{
    private readonly LcmCache _cache;
    private readonly CanonicalId _category;

    public AuthorCircumfixComposerTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        _category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
    }

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    private AuthorCircumfixIntent Circumfix(LexicalEntryMsa msa, IReadOnlyList<CanonicalId>? prefixEnvironments = null,
        IReadOnlyList<CanonicalId>? suffixEnvironments = null, IReadOnlyList<CanonicalId>? classes = null,
        string prefix = "ka", string suffix = "an") =>
        new(prefix, suffix, Vern(_cache), "abstract", Anal(_cache), msa, null, prefixEnvironments, suffixEnvironments, classes);

    [Fact]
    public void ACircumfixIsOneEntryWithACircumfixFormAndAPrefixAndASuffixEachWithItsOwnConditions()
    {
        var class12 = AddInflectionClass(_cache, _category, "1/2");
        var beforeVowel = AddEnvironment(_cache, "before vowel");
        var afterVowel = AddEnvironment(_cache, "after vowel");
        var intent = Circumfix(new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, _category), [beforeVowel], [afterVowel], [class12]);
        var proposal = Proposal(AuthorCircumfixComposer.Build(_cache, intent).Reverse().ToArray());
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "circumfix-tests");

        var entry = Get<ILexEntry>(_cache, Created(proposal, LexDbEntriesOperationKinds.Create));
        var lexeme = Assert.IsAssignableFrom<IMoAffixAllomorph>(entry.LexemeFormOA);
        Assert.Equal(MoMorphTypeTags.kguidMorphCircumfix, lexeme.MorphTypeRA.Guid);
        Assert.Equal("ka- -an", lexeme.Form.VernacularDefaultWritingSystem.Text);
        Assert.True(entry.IsCircumfix());
        Assert.True(lexeme.IsAbstract);
        var prefix = Assert.IsAssignableFrom<IMoAffixAllomorph>(entry.AlternateFormsOS[0]);
        var suffix = Assert.IsAssignableFrom<IMoAffixAllomorph>(entry.AlternateFormsOS[1]);
        Assert.Equal(2, entry.AlternateFormsOS.Count);
        Assert.Equal((MoMorphTypeTags.kguidMorphPrefix, "ka"), (prefix.MorphTypeRA.Guid, prefix.Form.VernacularDefaultWritingSystem.Text));
        Assert.Equal((MoMorphTypeTags.kguidMorphSuffix, "an"), (suffix.MorphTypeRA.Guid, suffix.Form.VernacularDefaultWritingSystem.Text));
        Assert.Equal([beforeVowel.ToGuid()], prefix.PhoneEnvRC.Select(item => item.Guid));
        Assert.Equal([afterVowel.ToGuid()], suffix.PhoneEnvRC.Select(item => item.Guid));
        Assert.Equal([class12.ToGuid()], prefix.InflectionClassesRC.Select(item => item.Guid));
        Assert.Equal([class12.ToGuid()], suffix.InflectionClassesRC.Select(item => item.Guid));
        Assert.Empty(lexeme.PhoneEnvRC);
        var sense = Assert.Single(entry.SensesOS);
        Assert.IsAssignableFrom<IMoInflAffMsa>(sense.MorphoSyntaxAnalysisRA);
    }

    [Fact]
    public void AStemMsaAHyphenatedOrDottedPartAndClassesOnADerivationalCircumfixAreRefused()
    {
        var inflectional = new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, _category);
        var stem = Assert.Throws<InvalidOperationException>(() =>
            AuthorCircumfixComposer.Build(_cache, Circumfix(new LexicalEntryMsa(LexicalEntryMsaKind.Stem, _category))));
        Assert.Contains("cannot be a stem", stem.Message, StringComparison.Ordinal);
        var hyphen = Assert.Throws<InvalidOperationException>(() =>
            AuthorCircumfixComposer.Build(_cache, Circumfix(inflectional, prefix: "ka-")));
        Assert.Contains("without affix hyphens", hyphen.Message, StringComparison.Ordinal);
        var stop = Assert.Throws<InvalidOperationException>(() =>
            AuthorCircumfixComposer.Build(_cache, Circumfix(inflectional, suffix: "k.an")));
        Assert.Contains("full stops", stop.Message, StringComparison.Ordinal);
        var derivational = new LexicalEntryMsa(LexicalEntryMsaKind.Derivational, _category, _category);
        var classes = Assert.Throws<InvalidOperationException>(() => AuthorCircumfixComposer.Build(_cache,
            Circumfix(derivational, classes: [AddInflectionClass(_cache, _category, "1/2")])));
        Assert.Contains("derivational", classes.Message, StringComparison.Ordinal);
    }
}

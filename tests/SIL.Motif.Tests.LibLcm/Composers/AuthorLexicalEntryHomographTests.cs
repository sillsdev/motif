using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using static SIL.Motif.Tests.Composers.AffixTestSupport;

namespace SIL.Motif.Tests.Composers;

/// <summary>
/// Authoring entries numbers homographs exactly as FieldWorks does (issue 167). LibLCM renumbers the set itself
/// when an entry gets a lexeme form; these tests pin that Motif's operation order reaches it, and that the Dry Run
/// shows the numbers.
/// </summary>
[Collection(LcmCacheParallelCollections.Group1)]
public sealed class AuthorLexicalEntryHomographTests(PristineProjectFixture pristine)
{
    private static int Number(LcmCache cache, CanonicalId entry) => Get<ILexEntry>(cache, entry).HomographNumber;

    private static IReadOnlyList<OperationEnvelope> Entry(LcmCache cache, string form, CanonicalId category) =>
        AuthorLexicalEntryComposer.Build(cache, new AuthorLexicalEntryIntent(form, Vern(cache),
            MorphType(MoMorphTypeTags.kguidMorphStem), form, Anal(cache),
            new LexicalEntryMsa(LexicalEntryMsaKind.Stem, category)));

    [Fact]
    public void HomographsAreNumberedOneThenTwoThenThreeAndAnUnrelatedFormStaysZero()
    {
        using var cache = pristine.NewScratch();
        var verb = AddCategory(cache, "Verb");
        var first = AddStem(cache, "anay", verb).Entry;
        var other = AddStem(cache, "buyu", verb).Entry;
        Assert.Equal(0, Number(cache, first));

        var second = AddStem(cache, "anay", verb).Entry;
        Assert.Equal([1, 2], new[] { first, second }.Select(id => Number(cache, id)));

        var third = AddStem(cache, "anay", verb).Entry;
        Assert.Equal([1, 2, 3], new[] { first, second, third }.Select(id => Number(cache, id)));
        Assert.Equal(0, Number(cache, other));
    }

    [Fact]
    public void AnAccentedSpellingIsADifferentFormNotAHomograph()
    {
        using var cache = pristine.NewScratch();
        var verb = AddCategory(cache, "Verb");
        var plain = AddStem(cache, "buyu", verb).Entry;
        var accented = AddStem(cache, "búyù", verb).Entry;
        Assert.Equal([0, 0], new[] { plain, accented }.Select(id => Number(cache, id)));
    }

    [Fact]
    public void TwoEntriesAuthoredInOneProposalAreNumbered()
    {
        using var cache = pristine.NewScratch();
        var verb = AddCategory(cache, "Verb");
        var operations = Entry(cache, "tali", verb).Concat(Entry(cache, "tali", verb)).ToArray();
        var proposal = ApplyOps(cache, operations);

        var entries = proposal.Operations.Where(operation => operation.Kind == LexDbEntriesOperationKinds.Create)
            .Select(operation => operation.EntityId!.Value).ToArray();
        Assert.Equal([1, 2], entries.Select(id => Number(cache, id)).Order());
    }

    [Fact]
    public void TheDryRunShowsTheNumbersTheNewEntryAndTheRenumberedOldEntryGet()
    {
        using var cache = pristine.NewScratch();
        var verb = AddCategory(cache, "Verb");
        var existing = AddStem(cache, "anay", verb).Entry;

        var proposal = Proposal(Entry(cache, "anay", verb));
        var created = Assert.Single(proposal.Operations, o => o.Kind == LexDbEntriesOperationKinds.Create).EntityId!.Value;
        var effects = ScratchDryRun.Of(cache, proposal).ExpectedEffects;

        var preview = Assert.Single(effects, effect => effect.Preview is not null).Preview!.Value;
        var numbers = preview.GetProperty("homographNumbers").EnumerateArray().ToArray();
        var thisEntry = Assert.Single(numbers, n => n.GetProperty("isThisEntry").GetBoolean());
        Assert.Equal(created.Value, thisEntry.GetProperty("entry").GetString());
        Assert.Equal((0, 2), (thisEntry.GetProperty("before").GetInt32(), thisEntry.GetProperty("after").GetInt32()));
        var old = Assert.Single(numbers, n => !n.GetProperty("isThisEntry").GetBoolean());
        Assert.Equal(existing.Value, old.GetProperty("entry").GetString());
        Assert.Equal((0, 1), (old.GetProperty("before").GetInt32(), old.GetProperty("after").GetInt32()));
        Assert.Contains("1", preview.GetProperty("summary").GetString());
        // The dry run only previews: the live project is unchanged.
        Assert.Equal(0, Number(cache, existing));
    }

    [Fact]
    public void AnEntryWithANewFormShowsNoHomographPreview()
    {
        using var cache = pristine.NewScratch();
        var verb = AddCategory(cache, "Verb");
        AddStem(cache, "anay", verb);
        var effects = ScratchDryRun.Of(cache, Proposal(Entry(cache, "buyu", verb))).ExpectedEffects;
        Assert.DoesNotContain(effects, effect => effect.Preview is not null);
    }

    [Fact]
    public void ThePreviewIsNotPartOfTheEffectDigest()
    {
        using var cache = pristine.NewScratch();
        var verb = AddCategory(cache, "Verb");
        AddStem(cache, "anay", verb);
        var effects = ScratchDryRun.Of(cache, Proposal(Entry(cache, "anay", verb))).ExpectedEffects;
        var withoutPreview = effects.Select(effect => effect with { Preview = null }).ToArray();
        Assert.Equal(SIL.Motif.Model.Effects.ExpectedEffectSetDigest.Compute(withoutPreview),
            SIL.Motif.Model.Effects.ExpectedEffectSetDigest.Compute(effects));
    }
}

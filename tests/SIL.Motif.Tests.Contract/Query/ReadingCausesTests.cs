using SIL.Motif.Contract.Query;
using SIL.Motif.Contract.Query.Readings;
using Xunit;

namespace SIL.Motif.Tests.Contract.Query;

/// <summary>The cause of a word's extra readings, the only-via syntax and the via counts, over small hand-built reading sets.</summary>
public sealed class ReadingCausesTests
{
    private const string A = "11111111-1111-1111-1111-111111111111";
    private const string B = "22222222-2222-2222-2222-222222222222";

    // One morph: id suffix doubles as form and analysis id; spans are by text and start.
    private static ReadingMorph M(string id, string text, int start, string? kind = null, string? msa = null) =>
        new("f" + id, msa ?? "m" + id, text, start, start < 0 ? -1 : start + text.Length, Kind: kind);

    private static WordReading R(params ReadingMorph[] morphs) => new(morphs);

    [Fact]
    public void ALexicalisedStemAgainstARootWithDerivationalAffixesIsLexicalisedVsDerived()
    {
        var lexicalised = R(M("stem", "tofauti", 0, MorphKinds.Stem), M("pl", "s", 7, MorphKinds.Inflectional));
        var derived = R(M("root", "tofaut", 0, MorphKinds.Stem), M("ish", "i", 6, MorphKinds.Derivational), M("pl", "s", 7, MorphKinds.Inflectional));
        Assert.Equal(ReadingCause.LexicalisedVsDerived, ReadingCause.Of([lexicalised, derived]));
        Assert.Equal(ReadingCause.LexicalisedVsDerived, ReadingCause.Of([derived, lexicalised]));
        Assert.False(ReadingCause.Benign(ReadingCause.LexicalisedVsDerived));
    }

    [Fact]
    public void AnInflectionalAffixInTheDerivedSideIsNotDerivationWhenClassesAreKnown()
    {
        var fused = R(M("stem", "ab", 0, MorphKinds.Stem));
        var split = R(M("root", "a", 0, MorphKinds.Stem), M("infl", "b", 1, MorphKinds.Inflectional));
        Assert.Equal(ReadingCause.Other, ReadingCause.Of([fused, split]));
    }

    [Fact]
    public void WithoutMorphClassesTheSpansAloneDecideLexicalisedVsDerived()
    {
        var lexicalised = R(M("stem", "ab", 0));
        var derived = R(M("root", "a", 0), M("x", "b", 1));
        Assert.Equal(ReadingCause.LexicalisedVsDerived, ReadingCause.Of([lexicalised, derived]));
        var unplaced = R(M("root", "a", -1), M("x", "b", -1));
        Assert.Equal(ReadingCause.Other, ReadingCause.Of([lexicalised, unplaced]));
    }

    [Fact]
    public void TheSameMorphsInAnotherOrderAreReordered()
    {
        var first = R(M("root", "kul", 0), M("caus", "ish", 3), M("recp", "an", 5));
        var second = R(M("root", "kul", 0), M("recp", "an", 3), M("caus", "ish", 5));
        Assert.Equal(ReadingCause.SameMorphsReordered, ReadingCause.Of([first, second]));
    }

    [Fact]
    public void AffixesThatSpellTheSameWithDifferentAnalysesAreHomophonousAndBenign()
    {
        var subject = R(M("root", "pend", 0), M("a", "wa", 4, MorphKinds.Inflectional, msa: "sm"));
        var obj = R(M("root", "pend", 0), M("a", "wa", 4, MorphKinds.Inflectional, msa: "om"));
        Assert.Equal(ReadingCause.HomophonousAffix, ReadingCause.Of([subject, obj]));
        Assert.True(ReadingCause.Benign(ReadingCause.HomophonousAffix));
    }

    [Fact]
    public void ADifferentStemWithTheSameSpellingIsNotHomophonousAffix()
    {
        var first = R(M("x", "pend", 0, MorphKinds.Stem, msa: "s1"));
        var second = R(M("x", "pend", 0, MorphKinds.Stem, msa: "s2"));
        Assert.Equal(ReadingCause.Other, ReadingCause.Of([first, second]));
    }

    [Fact]
    public void AMixOfHomophonousAndOtherDifferencesIsOtherAndPriorityIsKept()
    {
        var one = R(M("root", "pend", 0), M("a", "wa", 4, msa: "sm"));
        var two = R(M("root", "pend", 0), M("a", "wa", 4, msa: "om"));
        var three = R(M("other", "pendw", 0), M("a", "a", 5));
        Assert.Equal(ReadingCause.Other, ReadingCause.Of([one, two, three]));
        var fused = R(M("stem", "pendwa", 0));
        Assert.Equal(ReadingCause.LexicalisedVsDerived, ReadingCause.Of([one, two, fused]));
    }

    [Fact]
    public void IdenticalReadingsAloneAreOther()
    {
        var one = R(M("root", "pend", 0));
        Assert.Equal(ReadingCause.Other, ReadingCause.Of([one, one]));
    }

    [Fact]
    public void TheBuilderGroupsWordsInPriorityOrderWithTheMostFrequentWordAsExample()
    {
        var reordered = new WordReadings(ReadingStates.Parsed,
            [R(M("a", "x", 0), M("b", "y", 1)), R(M("b", "y", 0), M("a", "x", 1))]);
        var derived = new WordReadings(ReadingStates.Parsed, [R(M("s", "ab", 0)), R(M("r", "a", 0), M("d", "b", 1))]);
        var builder = new CauseGroupBuilder();
        builder.Add("xy", 3, reordered);
        builder.Add("yx", 9, reordered);
        builder.Add("ab", 1, derived);
        var groups = builder.Build();
        Assert.Equal([ReadingCause.LexicalisedVsDerived, ReadingCause.SameMorphsReordered], groups.Select(group => group.Cause));
        var second = groups[1];
        Assert.Equal((2, 12L, "yx"), (second.Words, second.Tokens, second.Example.Word));
        Assert.Equal(2, second.Example.Readings.Count);
        Assert.Equal(["x", "y"], second.Example.Readings[0].Morphs.Select(morph => morph.Text));
    }

    [Fact]
    public void OnlyViaNeedsAReadingAndEveryReadingUsingTheMorpheme()
    {
        var entry = new ReadingTarget("entry", A);
        var via = new ReadingMorph("f1", "m1", "a", 0, 1, Entry: A);
        var other = new ReadingMorph("f2", "m2", "a", 0, 1, Entry: B);
        var all = ViaCount.Of(new WordReadings(ReadingStates.Parsed, [R(via), R(via, other)]), [entry]);
        Assert.True(all.OnlyVia);
        var some = ViaCount.Of(new WordReadings(ReadingStates.Parsed, [R(via), R(other)]), [entry]);
        Assert.False(some.OnlyVia);
        Assert.Equal((2, 1, 1), (some.Readings, some.Via, some.Other));
        Assert.False(ViaCount.Of(new WordReadings(ReadingStates.None, []), [entry]).OnlyVia);
        Assert.Equal(0, ViaCount.Of(new WordReadings(ReadingStates.Parsed, [R(via)]), [new ReadingTarget("msa", A)]).Via);
    }

    [Theory]
    [InlineData("entry:" + A, "entry")]
    [InlineData("msa:" + A, "msa")]
    [InlineData("form:" + A, "form")]
    public void OnlyViaRoundTripsThroughTextAndJson(string body, string kind)
    {
        var expr = QueryExprText.Parse("only-via(" + body + ") and readings > 1");
        Assert.Equal("only-via(" + body + ") and readings > 1", QueryExprText.Print(expr));
        var and = Assert.IsType<QueryAnd>(expr);
        Assert.Equal(new QueryOnlyVia(kind, A), and.Items[0]);
        var document = new QueryDocument { From = "forms", Where = and.Items[0] };
        var json = document.ToJson();
        Assert.Contains("\"only-via\"", json);
        Assert.True(QueryDocument.TryParse(json, out var again, out _));
        Assert.Equal(and.Items[0], again.Where);
    }

    [Theory]
    [InlineData("only-via(foo)")]
    [InlineData("only-via(entry:not-a-guid)")]
    [InlineData("only-via(word:" + A + ")")]
    [InlineData("only-via(entry:" + A)]
    public void OnlyViaWithABadIdIsRefused(string text) => Assert.False(QueryExprText.TryParse(text, out _, out _));

    [Fact]
    public void OnlyViaLowersToASyntheticColumnAndIsFound()
    {
        var expr = QueryExprText.Parse("not only-via(msa:" + A + ")");
        Assert.Single(OnlyViaLowering.Find(expr));
        Assert.True(OnlyViaLowering.UsesReadings(expr));
        Assert.True(OnlyViaLowering.UsesReadings(QueryExprText.Parse("readings > 3")));
        Assert.False(OnlyViaLowering.UsesReadings(QueryExprText.Parse("tokens > 3")));
        var lowered = Assert.IsType<QueryNot>(OnlyViaLowering.Lower(expr));
        var field = Assert.IsType<QueryField>(lowered.Item);
        Assert.True(OnlyViaLowering.TryTarget(field.Field, out var target));
        Assert.Equal(new ReadingTarget("msa", A), target);
    }
}

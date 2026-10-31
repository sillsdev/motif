using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Composers;

[Collection(LcmCacheParallelCollections.Group1)]
public sealed class MergePhonemeComposerTests(PristineProjectFixture pristine)
{
    private static CanonicalId Phoneme(LcmCache cache, string name, params string[] representations) =>
        SoundSystemComposerTests.Compose(cache, [],
            () => AuthorPhonemeComposer.Build(cache, new(name, representations)));

    private static CanonicalId Class(LcmCache cache, string name, string abbreviation, params CanonicalId[] members) =>
        SoundSystemComposerTests.Compose(cache, [],
            () => AuthorNaturalClassComposer.Build(cache, new(name, abbreviation, members)));

    private static IPhPhoneme Find(LcmCache cache, CanonicalId id) =>
        (IPhPhoneme)cache.ServiceLocator.GetInstance<ICmObjectRepository>().GetObject(id.ToGuid());

    private static bool Exists(LcmCache cache, CanonicalId id) =>
        cache.ServiceLocator.GetInstance<ICmObjectRepository>().TryGetObject(id.ToGuid(), out _);

    private static string[] Codes(LcmCache cache, CanonicalId id) =>
        Find(cache, id).CodesOS.Select(c => c.Representation.get_String(cache.DefaultVernWs).Text).ToArray();

    private static CanonicalId[] Members(LcmCache cache, CanonicalId id) =>
        ((IPhNCSegments)cache.ServiceLocator.GetInstance<ICmObjectRepository>().GetObject(id.ToGuid()))
            .SegmentsRC.Select(m => CanonicalId.FromGuid(m.Guid)).ToArray();

    private static string Refusal(Action act) => Assert.Throws<InvalidOperationException>(act).Message;

    private static int PhonemeCount(LcmCache cache) =>
        cache.LangProject.PhonologicalDataOA.PhonemeSetsOS[0].PhonemesOC.Count;

    private static void StoreCode(LcmCache cache, CanonicalId id, string text) =>
        SIL.LCModel.Infrastructure.NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            Find(cache, id).CodesOS[0].Representation.set_String(cache.DefaultVernWs,
                TsStringUtils.MakeString(text, cache.DefaultVernWs)));

    [Fact]
    public void MovesEverySpellingToTheTargetAndRemovesTheSource()
    {
        using var cache = pristine.NewScratch();
        var u = Phoneme(cache, "u", "u");
        var o = Phoneme(cache, "o", "o", "oh");
        var before = PhonemeCount(cache);

        var operations = MergePhonemeComposer.Build(cache, new(o, u));
        SoundSystemComposerTests.Execute(cache, operations);

        Assert.Equal(["u", "o", "oh"], Codes(cache, u));
        Assert.False(Exists(cache, o));
        Assert.Equal(before - 1, PhonemeCount(cache));
        Assert.Single(operations, item => item.Kind == PhonemeDeleteOperationKinds.DeletePhonemes);
    }

    [Fact]
    public void DropsTheSourceFromEveryNaturalClassAndKeepsTheOtherMembers()
    {
        using var cache = pristine.NewScratch();
        var u = Phoneme(cache, "u", "u");
        var o = Phoneme(cache, "o", "o");
        var a = Phoneme(cache, "a", "a");
        var both = Class(cache, "Vowels", "V", a, u, o);
        var onlySource = Class(cache, "Mid vowels", "M", a, o);

        SoundSystemComposerTests.Execute(cache, MergePhonemeComposer.Build(cache, new(o, u)));

        Assert.Equal([a, u], Members(cache, both));
        Assert.Equal([a], Members(cache, onlySource));
    }

    [Fact]
    public void DryRunReportsTheNaturalClassesTheSourceLeaves()
    {
        using var cache = pristine.NewScratch();
        var u = Phoneme(cache, "u", "u");
        var o = Phoneme(cache, "o", "o");
        var a = Phoneme(cache, "a", "a");
        var vowels = Class(cache, "Vowels", "V", a, o);

        var users = MergePhonemeComposer.ReadAffectedUsers(cache, new(o, u));

        var user = Assert.Single(users);
        Assert.Equal("natural-class", user.Kind);
        Assert.Equal(vowels.Value, user.Id);
        Assert.Contains("Vowels", user.Name);
        Assert.Contains("dropped from this class", user.Name);
    }

    private static void StoreEnvironment(LcmCache cache, string text) =>
        SIL.LCModel.Infrastructure.NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var environment = cache.ServiceLocator.GetInstance<IPhEnvironmentFactory>().Create();
            cache.LangProject.PhonologicalDataOA.EnvironmentsOS.Add(environment);
            environment.Name.set_String(cache.DefaultAnalWs, "unspaced");
            environment.StringRepresentation = TsStringUtils.MakeString(text, cache.DefaultVernWs);
        });

    [Fact]
    public void RefusesAnUnparsedEnvironmentThatWritesASourceSpellingWithoutSpaces()
    {
        using var cache = pristine.NewScratch();
        var u = Phoneme(cache, "u", "u");
        var o = Phoneme(cache, "o", "o");
        Phoneme(cache, "m", "m");
        Phoneme(cache, "t", "t");
        StoreEnvironment(cache, "/ mo_t");

        var message = Refusal(() => MergePhonemeComposer.Build(cache, new(o, u)));

        Assert.StartsWith(MergePhonemeComposer.Referenced, message);
        Assert.Contains("unspaced", message);
    }

    [Fact]
    public void AnUnparsedEnvironmentThatSkipsAClassReferenceStillCountsItsLetters()
    {
        using var cache = pristine.NewScratch();
        var u = Phoneme(cache, "u", "u");
        var o = Phoneme(cache, "o", "o");
        Phoneme(cache, "t", "t");
        StoreEnvironment(cache, "/ [Vowel]t_");

        MergePhonemeComposer.Build(cache, new(o, u));
    }

    [Fact]
    public void RefusesToEmptyANaturalClass()
    {
        using var cache = pristine.NewScratch();
        var u = Phoneme(cache, "u", "u");
        var o = Phoneme(cache, "o", "o");
        Class(cache, "Only o", "O", o);

        var message = Refusal(() => MergePhonemeComposer.Build(cache, new(o, u)));

        Assert.StartsWith(MergePhonemeComposer.ClassWouldEmpty, message);
        Assert.Contains("Only o", message);
    }

    [Fact]
    public void RefusesAnEnvironmentThatWritesASourceSpelling()
    {
        using var cache = pristine.NewScratch();
        var u = Phoneme(cache, "u", "u");
        var o = Phoneme(cache, "o", "o");
        var t = Phoneme(cache, "t", "t");
        SoundSystemComposerTests.Execute(cache, AuthorEnvironmentComposer.Build(cache,
            new("after o", [new(Phoneme: o)], [new(Phoneme: t)])));

        var message = Refusal(() => MergePhonemeComposer.Build(cache, new(o, u)));

        Assert.StartsWith(MergePhonemeComposer.Referenced, message);
        Assert.Contains("after o", message);
        Assert.Contains(MergePhonemeComposer.ReadAffectedUsers(cache, new(o, u)),
            user => user.Kind == "environment" && user.Name == "after o");
    }

    [Fact]
    public void RefusesAPhonologicalRuleThatNamesTheSource()
    {
        using var cache = pristine.NewScratch();
        var u = Phoneme(cache, "u", "u");
        var o = Phoneme(cache, "o", "o");
        var t = Phoneme(cache, "t", "t");
        var d = Phoneme(cache, "d", "d");
        var a = Phoneme(cache, "a", "a");
        SoundSystemComposerTests.Execute(cache, AuthorPhonologicalRuleComposer.Build(cache,
            new AuthorPhonologicalRuleIntent("t voices after o", PhonologicalRuleDirection.Simultaneous,
                [new(Phoneme: t)], [new(Phoneme: d)], [new(Phoneme: o)], [new(Phoneme: a)])));

        var message = Refusal(() => MergePhonemeComposer.Build(cache, new(o, u)));

        Assert.StartsWith(MergePhonemeComposer.Referenced, message);
        Assert.Contains("t voices after o", message);
    }

    [Fact]
    public void RefusesASpellingAnotherPhonemeAlsoHas()
    {
        using var cache = pristine.NewScratch();
        var u = Phoneme(cache, "u", "u");
        var o = Phoneme(cache, "o", "o");
        StoreCode(cache, o, "u");

        var message = Refusal(() => MergePhonemeComposer.Build(cache, new(o, u)));

        Assert.StartsWith(MergePhonemeComposer.CodeConflict, message);
        Assert.Contains("(the phoneme being merged into)", message);
    }

    [Fact]
    public void RefusesASpellingAThirdPhonemeHas()
    {
        using var cache = pristine.NewScratch();
        var u = Phoneme(cache, "u", "u");
        var o = Phoneme(cache, "o", "o");
        var e = Phoneme(cache, "e", "e");
        StoreCode(cache, o, "e");

        var message = Refusal(() => MergePhonemeComposer.Build(cache, new(o, u)));

        Assert.StartsWith(MergePhonemeComposer.CodeConflict, message);
        Assert.Contains(e.Value, message);
    }

    [Fact]
    public void RefusesWithStableNames()
    {
        using var cache = pristine.NewScratch();
        var u = Phoneme(cache, "u", "u");
        var o = Phoneme(cache, "o", "o");

        Assert.StartsWith(MergePhonemeComposer.Same, Refusal(() => MergePhonemeComposer.Build(cache, new(u, u))));
        Assert.ThrowsAny<Exception>(() => MergePhonemeComposer.Build(cache, new(CanonicalId.Mint(), u)));
        Assert.ThrowsAny<Exception>(() => MergePhonemeComposer.Build(cache, new(o, CanonicalId.Mint())));
    }

    [Fact]
    public void SplitRestoresASeparatePhonemeWithTheSpellingAndLeavesTheRestBehind()
    {
        using var cache = pristine.NewScratch();
        var u = Phoneme(cache, "u", "u");
        var o = Phoneme(cache, "o", "o");
        var before = PhonemeCount(cache);
        SoundSystemComposerTests.Execute(cache, MergePhonemeComposer.Build(cache, new(o, u)));
        Assert.Equal(["u", "o"], Codes(cache, u));

        var operations = SplitPhonemeComposer.Build(cache, new(u, "o", "o"));
        SoundSystemComposerTests.Execute(cache, operations);

        Assert.Equal(["u"], Codes(cache, u));
        Assert.Equal(before, PhonemeCount(cache));
        var created = Find(cache, operations.First(item => item.Kind == PhPhonemeSetPhonemesOperationKinds.Create).EntityId!.Value);
        Assert.Equal(["o"], Codes(cache, CanonicalId.FromGuid(created.Guid)));
        Assert.Equal("o", created.Name.get_String(cache.DefaultVernWs).Text);
    }

    [Fact]
    public void SplitRefusesWithStableNames()
    {
        using var cache = pristine.NewScratch();
        var u = Phoneme(cache, "u", "u", "o");
        var t = Phoneme(cache, "t", "t");
        SoundSystemComposerTests.Execute(cache, AuthorEnvironmentComposer.Build(cache,
            new("after o", [new(Phoneme: u)], [new(Phoneme: t)])));

        Assert.StartsWith(EditPhonemeComposer.NotFound, Refusal(() => SplitPhonemeComposer.Build(cache, new(u, "x", "x"))));
        Assert.StartsWith(SplitPhonemeComposer.LastRepresentation, Refusal(() => SplitPhonemeComposer.Build(cache, new(t, "t", "t2"))));
        Assert.StartsWith(SplitPhonemeComposer.NameMissing, Refusal(() => SplitPhonemeComposer.Build(cache, new(u, "o", " "))));
        Assert.StartsWith(EditPhonemeComposer.Referenced, Refusal(() => SplitPhonemeComposer.Build(cache, new(u, "u", "x"))));
    }

    [Fact]
    public void ParsersAreClosedAndReadTheIntents()
    {
        var source = CanonicalId.Mint();
        var target = CanonicalId.Mint();
        var merge = MergePhonemeIntentParser.Parse(JsonDocument.Parse(
            $$"""{"source":"{{source.Value}}","target":"{{target.Value}}"}""").RootElement);
        Assert.Equal(new MergePhonemeIntent(source, target), merge);
        Assert.Throws<ContractParseException>(() => MergePhonemeIntentParser.Parse(JsonDocument.Parse(
            $$"""{"source":"{{source.Value}}","target":"{{target.Value}}","force":true}""").RootElement));
        Assert.Throws<ContractParseException>(() => MergePhonemeIntentParser.Parse(JsonDocument.Parse(
            $$"""{"source":"{{source.Value}}"}""").RootElement));
        var split = SplitPhonemeIntentParser.Parse(JsonDocument.Parse(
            $$"""{"source":"{{source.Value}}","representation":"o","name":"o"}""").RootElement);
        Assert.Equal(new SplitPhonemeIntent(source, "o", "o"), split);
        Assert.Throws<ContractParseException>(() => SplitPhonemeIntentParser.Parse(JsonDocument.Parse(
            $$"""{"source":"{{source.Value}}","representation":"o"}""").RootElement));
    }
}

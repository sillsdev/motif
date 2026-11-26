using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Composers;

[Collection(LcmCacheParallelCollections.Group1)]
public sealed class EditEnvironmentComposerTests(PristineProjectFixture pristine)
{
    private static CanonicalId Phoneme(LcmCache cache, string name, params string[] representations) =>
        SoundSystemComposerTests.Compose(cache, [], () => AuthorPhonemeComposer.Build(cache, new(name, representations)));

    private static CanonicalId Class(LcmCache cache, string name, string abbreviation, params CanonicalId[] members) =>
        SoundSystemComposerTests.Compose(cache, [],
            () => AuthorNaturalClassComposer.Build(cache, new(name, abbreviation, members, null)));

    private static IPhEnvironment Environment(LcmCache cache, CanonicalId id) =>
        (IPhEnvironment)cache.ServiceLocator.GetInstance<ICmObjectRepository>().GetObject(id.ToGuid());

    private static CanonicalId Author(LcmCache cache, string name, EnvironmentContext[] left, EnvironmentContext[] right)
    {
        SoundSystemComposerTests.Execute(cache, AuthorEnvironmentComposer.Build(cache, new(name, left, right)));
        return CanonicalId.FromGuid(cache.LangProject.PhonologicalDataOA.EnvironmentsOS.Single(e => e.Name.BestAnalysisAlternative.Text == name).Guid);
    }

    private static string Text(LcmCache cache, CanonicalId id) => Environment(cache, id).StringRepresentation.Text;

    private static string Refusal(Action act) => Assert.Throws<InvalidOperationException>(act).Message;

    [Fact]
    public void ReplacesThePhonemeContextWithANaturalClassAndTheAllomorphKeepsItsLink()
    {
        using var cache = pristine.NewScratch();
        var sign = Phoneme(cache, "-ā", "ா");
        var a = Phoneme(cache, "a", "a");
        var vowels = Class(cache, "Vowels", "V", a, sign);
        var environment = Author(cache, "after sign", [new(Phoneme: sign)], [new(Boundary: "word")]);
        var stem = (IMoStemAllomorph)cache.ServiceLocator.GetInstance<ILexEntryRepository>()
            .GetObject(pristine.Seed.FirstEntryId).LexemeFormOA!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () => stem.PhoneEnvRC.Add(Environment(cache, environment)));
        var before = cache.LangProject.PhonologicalDataOA.EnvironmentsOS.Count;

        var operations = EditEnvironmentComposer.Build(cache,
            new(environment, "/ ா _ #", [new(NaturalClass: vowels)], null));
        SoundSystemComposerTests.Execute(cache, operations);

        Assert.Single(operations);
        Assert.Equal("/ [V] _ #", Text(cache, environment));
        Assert.Equal(before, cache.LangProject.PhonologicalDataOA.EnvironmentsOS.Count);
        Assert.Equal([environment.ToGuid()], stem.PhoneEnvRC.Select(item => item.Guid));
        Assert.Equal("after sign", Environment(cache, environment).Name.BestAnalysisAlternative.Text);
    }

    [Fact]
    public void ReplacesTheRightSideAndKeepsTheLeftSideAsStored()
    {
        using var cache = pristine.NewScratch();
        var t = Phoneme(cache, "t", "t");
        var a = Phoneme(cache, "a", "a");
        var i = Phoneme(cache, "i", "i");
        var environment = Author(cache, "t_a", [new(Phoneme: t)], [new(Phoneme: a)]);

        SoundSystemComposerTests.Execute(cache,
            EditEnvironmentComposer.Build(cache, new(environment, "t_a", null, [new(Phoneme: i)])));

        Assert.Equal("/ t _ i", Text(cache, environment));
    }

    [Fact]
    public void ReplacesTheWholeRepresentationEvenWhenTheOldStringDoesNotParse()
    {
        using var cache = pristine.NewScratch();
        var a = Phoneme(cache, "a", "a");
        var environment = Author(cache, "odd", [new(Phoneme: a)], []);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            Environment(cache, environment).StringRepresentation = TsStringUtils.MakeString("/ zz(q)_", cache.DefaultVernWs));

        SoundSystemComposerTests.Execute(cache, EditEnvironmentComposer.Build(cache,
            new(environment, "/ zz(q)_", [new(Boundary: "word")], [new(Phoneme: a)])));

        Assert.Equal("/ # _ a", Text(cache, environment));
    }

    [Fact]
    public void ReportsTheAllomorphsThatHoldTheEnvironment()
    {
        using var cache = pristine.NewScratch();
        var a = Phoneme(cache, "a", "a");
        var environment = Author(cache, "after a", [new(Phoneme: a)], []);
        var stem = (IMoStemAllomorph)cache.ServiceLocator.GetInstance<ILexEntryRepository>()
            .GetObject(pristine.Seed.FirstEntryId).LexemeFormOA!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () => stem.PhoneEnvRC.Add(Environment(cache, environment)));

        var users = EditEnvironmentComposer.ReadAffectedUsers(cache, new(environment, "after a", [], null));

        Assert.Contains(users, user => user.Id == CanonicalId.FromGuid(stem.Guid).Value);
    }

    [Fact]
    public void RefusesWithStableNames()
    {
        using var cache = pristine.NewScratch();
        var t = Phoneme(cache, "t", "t");
        var a = Phoneme(cache, "a", "a");
        var missing = CanonicalId.Mint();
        var environment = Author(cache, "t_a", [new(Phoneme: t)], [new(Phoneme: a)]);
        var lone = Author(cache, "lone", [new(Phoneme: t)], []);

        Assert.StartsWith(EditEnvironmentComposer.Empty,
            Refusal(() => EditEnvironmentComposer.Build(cache, new(environment, "t_a", null, null))));
        Assert.StartsWith(EditEnvironmentComposer.NotFound,
            Refusal(() => EditEnvironmentComposer.Build(cache, new(missing, "x", [], null))));
        Assert.StartsWith(EditEnvironmentComposer.NotFound,
            Refusal(() => EditEnvironmentComposer.Build(cache, new(t, "x", [], null))));
        Assert.StartsWith(EditEnvironmentComposer.Stale,
            Refusal(() => EditEnvironmentComposer.Build(cache, new(environment, "/ q _ q", [new(Phoneme: a)], null))));
        Assert.StartsWith(EditEnvironmentComposer.Invalid,
            Refusal(() => EditEnvironmentComposer.Build(cache, new(environment, "t_a", [new(Phoneme: missing)], null))));
        Assert.StartsWith(EditEnvironmentComposer.Invalid,
            Refusal(() => EditEnvironmentComposer.Build(cache, new(environment, "t_a", [new(NaturalClass: missing)], null))));
        Assert.StartsWith(EditEnvironmentComposer.Invalid,
            Refusal(() => EditEnvironmentComposer.Build(cache, new(environment, "t_a", [new(Boundary: "morpheme")], null))));
        Assert.StartsWith(EditEnvironmentComposer.Invalid,
            Refusal(() => EditEnvironmentComposer.Build(cache, new(lone, "lone", [], []))));
        Assert.StartsWith(EditEnvironmentComposer.Unchanged,
            Refusal(() => EditEnvironmentComposer.Build(cache, new(environment, "t_a", [new(Phoneme: t)], null))));
    }

    [Fact]
    public void RefusesToKeepASideThatDoesNotReadBackAndNamesWhatToDo()
    {
        using var cache = pristine.NewScratch();
        var a = Phoneme(cache, "a", "a");
        var environment = Author(cache, "odd", [new(Phoneme: a)], []);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            Environment(cache, environment).StringRepresentation = TsStringUtils.MakeString("/ a _ (q)", cache.DefaultVernWs));

        var message = Refusal(() => EditEnvironmentComposer.Build(cache,
            new(environment, "/ a _ (q)", [new(Boundary: "word")], null)));

        Assert.StartsWith(EditEnvironmentComposer.Unparseable, message);
        Assert.Contains("give both", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParsesTheClosedIntentAndRejectsUnknownFields()
    {
        var id = CanonicalId.Mint().Value;
        var ok = EditEnvironmentIntentParser.Parse(System.Text.Json.JsonDocument.Parse(
            $$"""{"environment":"{{id}}","expectedText":"x","left":[{"boundary":"word"}]}""").RootElement);
        Assert.NotNull(ok.Left);
        Assert.Null(ok.Right);
        Assert.Throws<ContractParseException>(() => EditEnvironmentIntentParser.Parse(System.Text.Json.JsonDocument.Parse(
            $$"""{"environment":"{{id}}","expectedText":"x","name":"y"}""").RootElement));
    }
}

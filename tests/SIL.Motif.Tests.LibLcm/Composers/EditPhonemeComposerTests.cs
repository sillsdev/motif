using System.Text;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Composers;

[Collection(LcmCacheParallelCollections.Group1)]
public sealed class EditPhonemeComposerTests(PristineProjectFixture pristine)
{
    private static CanonicalId Phoneme(LcmCache cache, string name, params string[] representations) =>
        SoundSystemComposerTests.Compose(cache, [],
            () => AuthorPhonemeComposer.Build(cache, new(name, representations)));

    private static IPhPhoneme Find(LcmCache cache, CanonicalId id) =>
        (IPhPhoneme)cache.ServiceLocator.GetInstance<ICmObjectRepository>().GetObject(id.ToGuid());

    private static string[] Codes(LcmCache cache, CanonicalId id) =>
        Find(cache, id).CodesOS.Select(c => c.Representation.get_String(cache.DefaultVernWs).Text).ToArray();

    private static string Refusal(Action act) => Assert.Throws<InvalidOperationException>(act).Message;

    [Fact]
    public void AddsDigraphsToTheSamePhonemeInOrderWithoutANewSegment()
    {
        using var cache = pristine.NewScratch();
        var n = Phoneme(cache, "n", "n");
        var before = cache.LangProject.PhonologicalDataOA.PhonemeSetsOS[0].PhonemesOC.Count;

        var operations = EditPhonemeComposer.Build(cache, new(n, ["ng", "ts"], []));
        SoundSystemComposerTests.Execute(cache, operations);

        Assert.Equal(["n", "ng", "ts"], Codes(cache, n));
        Assert.Equal(before, cache.LangProject.PhonologicalDataOA.PhonemeSetsOS[0].PhonemesOC.Count);
        Assert.DoesNotContain(operations, o => o.Kind.EndsWith("/createPhonemes", StringComparison.Ordinal));
    }

    [Fact]
    public void RespellsACodeInPlaceKeepingItsIdentity()
    {
        using var cache = pristine.NewScratch();
        var d = Phoneme(cache, "d", "d");
        var codeGuid = Find(cache, d).CodesOS[0].Guid;

        SoundSystemComposerTests.Execute(cache, EditPhonemeComposer.Build(cache, new(d, [], [new("d", "dy")])));

        Assert.Equal(["dy"], Codes(cache, d));
        Assert.Equal(codeGuid, Find(cache, d).CodesOS[0].Guid);
    }

    [Fact]
    public void NormalizesSpellingsSoADecomposedDuplicateIsStillADuplicate()
    {
        using var cache = pristine.NewScratch();
        var a = Phoneme(cache, "a", "a");
        Phoneme(cache, "e", "é");
        Assert.StartsWith(EditPhonemeComposer.Duplicate,
            Refusal(() => EditPhonemeComposer.Build(cache, new(a, ["é"], []))));
    }

    [Theory]
    [InlineData("\u00E9", "e\u0301")]
    [InlineData("e\u0301", "\u00E9")]
    public void RefusesAnInputThatDiffersFromAnExistingRepresentationOnlyByNormalizationForm(string existing, string input)
    {
        using var cache = pristine.NewScratch();
        var a = Phoneme(cache, "a", "a");
        Phoneme(cache, "e", existing);
        Assert.StartsWith(EditPhonemeComposer.Duplicate,
            Refusal(() => EditPhonemeComposer.Build(cache, new(a, [input], []))));
    }

    [Fact]
    public void RefusesWithStableNames()
    {
        using var cache = pristine.NewScratch();
        var n = Phoneme(cache, "n", "n");
        var t = Phoneme(cache, "t", "t");
        Phoneme(cache, "ng", "ng");

        Assert.StartsWith(EditPhonemeComposer.Empty, Refusal(() => EditPhonemeComposer.Build(cache, new(n, [], []))));
        Assert.StartsWith(EditPhonemeComposer.Duplicate, Refusal(() => EditPhonemeComposer.Build(cache, new(n, ["ng"], []))));
        Assert.StartsWith(EditPhonemeComposer.Duplicate, Refusal(() => EditPhonemeComposer.Build(cache, new(n, ["ts", "ts"], []))));
        Assert.StartsWith(EditPhonemeComposer.Duplicate, Refusal(() => EditPhonemeComposer.Build(cache, new(n, ["n"], []))));
        Assert.StartsWith(EditPhonemeComposer.Invalid, Refusal(() => EditPhonemeComposer.Build(cache, new(n, ["∅"], []))));
        Assert.StartsWith(EditPhonemeComposer.Invalid, Refusal(() => EditPhonemeComposer.Build(cache, new(n, [""], []))));
        Assert.StartsWith(EditPhonemeComposer.NotFound, Refusal(() => EditPhonemeComposer.Build(cache, new(t, [], [new("x", "ts")]))));
    }

    [Fact]
    public void RefusesToRespellARepresentationAnEnvironmentWritesAndReportsItAsAffected()
    {
        using var cache = pristine.NewScratch();
        var t = Phoneme(cache, "t", "t");
        var a = Phoneme(cache, "a", "a");
        SoundSystemComposerTests.Execute(cache, AuthorEnvironmentComposer.Build(cache,
            new("after t", [new(Phoneme: t)], [new(Phoneme: a)])));

        Assert.StartsWith(EditPhonemeComposer.Referenced,
            Refusal(() => EditPhonemeComposer.Build(cache, new(t, [], [new("t", "ts")]))));
        Assert.Contains(EditPhonemeComposer.ReadAffectedUsers(cache, new(t, ["ts"], [])),
            user => user.Kind == "environment" && user.Name == "after t");
        // Adding a spelling leaves the environment's own string valid.
        Assert.Single(EditPhonemeComposer.Build(cache, new(t, ["ts"], [])), o => o.Kind.EndsWith("/createCodes", StringComparison.Ordinal));
    }

    [Fact]
    public void RefusesToRespellARepresentationAnUnparsedEnvironmentWritesWithoutSpaces()
    {
        using var cache = pristine.NewScratch();
        var t = Phoneme(cache, "t", "t");
        Phoneme(cache, "a", "a");
        SIL.LCModel.Infrastructure.NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var environment = cache.ServiceLocator.GetInstance<IPhEnvironmentFactory>().Create();
            cache.LangProject.PhonologicalDataOA.EnvironmentsOS.Add(environment);
            environment.Name.set_String(cache.DefaultAnalWs, "unspaced");
            environment.StringRepresentation = TsStringUtils.MakeString("/ a_ta", cache.DefaultVernWs);
        });

        Assert.StartsWith(EditPhonemeComposer.Referenced,
            Refusal(() => EditPhonemeComposer.Build(cache, new(t, [], [new("t", "ts")]))));
        Assert.Contains(EditPhonemeComposer.ReadAffectedUsers(cache, new(t, ["ts"], [])),
            user => user.Kind == "environment" && user.Name == "unspaced");
    }

    private static void StoreCode(LcmCache cache, CanonicalId id, string text) =>
        SIL.LCModel.Infrastructure.NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            Find(cache, id).CodesOS[0].Representation.set_String(cache.DefaultVernWs,
                TsStringUtils.MakeString(text, cache.DefaultVernWs)));

    [Fact]
    public void ADottedCircleOnlyCodeIsNotARepresentationToMerge()
    {
        using var cache = pristine.NewScratch();
        var source = Phoneme(cache, "display only", "d");
        var target = Phoneme(cache, "target", "t");
        StoreCode(cache, source, "\u25CC");

        Assert.StartsWith(MergePhonemeComposer.NothingToMove,
            Refusal(() => MergePhonemeComposer.Build(cache, new(source, target))));
    }

    [Theory]
    [InlineData("edit")]
    [InlineData("split")]
    [InlineData("merge")]
    public void DottedCircleOnlyCodesDoNotBlockCompositionOrRelatedObjectReporting(string composer)
    {
        using var cache = pristine.NewScratch();
        var displayOnly = Phoneme(cache, "display only", "d");
        StoreCode(cache, displayOnly, "\u25CC");
        var source = Phoneme(cache, "source", "s", "z");
        StoreCode(cache, source, "\u25CC");
        var target = Phoneme(cache, "target", "t");
        var a = Phoneme(cache, "a", "a");
        SoundSystemComposerTests.Execute(cache, AuthorEnvironmentComposer.Build(cache,
            new("after a", [new(Phoneme: a)], [])));

        switch (composer)
        {
            case "edit":
                Assert.Empty(EditPhonemeComposer.ReadAffectedUsers(cache, new(source, ["s"], [])));
                SoundSystemComposerTests.Execute(cache, EditPhonemeComposer.Build(cache, new(source, [], [new("z", "s")])));
                Assert.Equal(["\u25CC", "s"], Codes(cache, source));
                break;
            case "split":
                Assert.Empty(SplitPhonemeComposer.ReadAffectedUsers(cache, new(source, "z", "split")));
                SoundSystemComposerTests.Execute(cache, SplitPhonemeComposer.Build(cache, new(source, "z", "split")));
                Assert.Equal(["\u25CC"], Codes(cache, source));
                break;
            case "merge":
                Assert.Empty(MergePhonemeComposer.ReadAffectedUsers(cache, new(source, target)));
                SoundSystemComposerTests.Execute(cache, MergePhonemeComposer.Build(cache, new(source, target)));
                Assert.Equal(["t", "z"], Codes(cache, target));
                break;
        }
        Assert.Empty(EditPhonemeComposer.ReadAffectedUsers(cache, new(displayOnly, ["d"], [])));
    }

    [Fact]
    public void RespellsALiteralSlashDespiteAnUnrelatedWordInitialEnvironment()
    {
        using var cache = pristine.NewScratch();
        var slash = Phoneme(cache, "literal slash", "/");
        SoundSystemComposerTests.Execute(cache, AuthorEnvironmentComposer.Build(cache,
            new("word initial", [new(Boundary: "word")], [])));

        var users = EditPhonemeComposer.ReadAffectedUsers(cache, new(slash, [], [new("/", "!")]));
        SoundSystemComposerTests.Execute(cache, EditPhonemeComposer.Build(cache, new(slash, [], [new("/", "!")])));

        Assert.Empty(users);
        Assert.Equal(["!"], Codes(cache, slash));
        Assert.Equal("/ # _", cache.LangProject.PhonologicalDataOA.EnvironmentsOS[0].StringRepresentation.Text);
    }

    [Theory]
    [InlineData("/ / _")]
    [InlineData("/ _ /")]
    [InlineData("/ (/) _")]
    [InlineData("//_")]
    public void ARealLiteralSlashInAnEnvironmentSegmentStillProtectsItsPhoneme(string text)
    {
        using var cache = pristine.NewScratch();
        var slash = Phoneme(cache, "literal slash", "/", "s");
        var target = Phoneme(cache, "target", "t");
        SIL.LCModel.Infrastructure.NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var environment = cache.ServiceLocator.GetInstance<IPhEnvironmentFactory>().Create();
            cache.LangProject.PhonologicalDataOA.EnvironmentsOS.Add(environment);
            environment.Name.set_String(cache.DefaultAnalWs, "after literal slash");
            environment.StringRepresentation = TsStringUtils.MakeString(text, cache.DefaultVernWs);
        });

        Assert.StartsWith(EditPhonemeComposer.Referenced,
            Refusal(() => EditPhonemeComposer.Build(cache, new(slash, [], [new("/", "!")]))));
        Assert.StartsWith(EditPhonemeComposer.Referenced,
            Refusal(() => SplitPhonemeComposer.Build(cache, new(slash, "/", "split"))));
        Assert.StartsWith(MergePhonemeComposer.Referenced,
            Refusal(() => MergePhonemeComposer.Build(cache, new(slash, target))));
        Assert.Equal("after literal slash", Assert.Single(EditPhonemeComposer.ReadAffectedUsers(cache, new(slash, ["!"], []))).Name);
        Assert.Equal("after literal slash", Assert.Single(SplitPhonemeComposer.ReadAffectedUsers(cache, new(slash, "/", "split"))).Name);
        Assert.Equal("after literal slash", Assert.Single(MergePhonemeComposer.ReadAffectedUsers(cache, new(slash, target))).Name);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("_")]
    [InlineData("#")]
    [InlineData("(")]
    [InlineData(")")]
    [InlineData("a(b")]
    [InlineData("a#")]
    public void EnvironmentNotationDoesNotReferenceLiteralPhonemesOrConsumeAcrossSegmentRuns(string spelling)
    {
        using var cache = pristine.NewScratch();
        var source = Phoneme(cache, "literal", spelling, "s");
        var target = Phoneme(cache, "target", "t");
        Phoneme(cache, "a", "a");
        Phoneme(cache, "b", "b");
        SIL.LCModel.Infrastructure.NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var environment = cache.ServiceLocator.GetInstance<IPhEnvironmentFactory>().Create();
            cache.LangProject.PhonologicalDataOA.EnvironmentsOS.Add(environment);
            environment.Name.set_String(cache.DefaultAnalWs, "optional b");
            environment.StringRepresentation = TsStringUtils.MakeString("/ # a(b) _ a#", cache.DefaultVernWs);
        });

        Assert.Empty(EditPhonemeComposer.ReadAffectedUsers(cache, new(source, ["!"], [])));
        Assert.Empty(SplitPhonemeComposer.ReadAffectedUsers(cache, new(source, spelling, "split")));
        Assert.Empty(MergePhonemeComposer.ReadAffectedUsers(cache, new(source, target)));
        Assert.NotEmpty(SplitPhonemeComposer.Build(cache, new(source, spelling, "split")));
        Assert.NotEmpty(MergePhonemeComposer.Build(cache, new(source, target)));
        SoundSystemComposerTests.Execute(cache, EditPhonemeComposer.Build(cache, new(source, [], [new(spelling, "!")])));
        Assert.Equal(["!", "s"], Codes(cache, source));
    }

    [Fact]
    public void RespellsAStoredCodeWithTrailingWhitespaceUsingItsExactSpelling()
    {
        using var cache = pristine.NewScratch();
        var glottal = Phoneme(cache, "glottal stop", "g");
        StoreCode(cache, glottal, "- ");

        SoundSystemComposerTests.Execute(cache, EditPhonemeComposer.Build(cache, new(glottal, [], [new("- ", "-")])));

        Assert.Equal(["-"], Codes(cache, glottal));
    }

    [Fact]
    public void WhitespaceInALiteralRepresentationIsPreserved()
    {
        using var cache = pristine.NewScratch();
        var phone = Phoneme(cache, "letter", "g");
        SoundSystemComposerTests.Execute(cache, EditPhonemeComposer.Build(cache, new(phone, [], [new("g", "a b")])));
        Assert.Equal(["a b"], Codes(cache, phone));
    }

    [Fact]
    public void TextualEnvironmentRefusalOnAStoredCodeWithWhitespaceNamesTheClassRoute()
    {
        using var cache = pristine.NewScratch();
        var glottal = Phoneme(cache, "glottal stop", "g");
        var a = Phoneme(cache, "a", "a");
        StoreCode(cache, glottal, "- ");

        var message = Refusal(() => AuthorEnvironmentComposer.Build(cache,
            new("after glottal", [new(Phoneme: glottal)], [new(Phoneme: a)])));

        Assert.Contains("environment syntax", message);
        Assert.Contains("singleton natural class", message);
    }

    [Fact]
    public void ParserIsClosedAndReadsTheIntent()
    {
        var id = CanonicalId.Mint();
        var intent = EditPhonemeIntentParser.Parse(JsonDocument.Parse(
            $$"""{"target":"{{id.Value}}","add":[{"representation":"ng"}],"respell":[{"from":"d","to":"dy"}]}""").RootElement);
        Assert.Equal(["ng"], intent.Add);
        Assert.Equal(new PhonemeRespelling("d", "dy"), Assert.Single(intent.Respell));
        Assert.Throws<ContractParseException>(() => EditPhonemeIntentParser.Parse(JsonDocument.Parse(
            $$"""{"target":"{{id.Value}}","features":[]}""").RootElement));
        Assert.Throws<ContractParseException>(() => EditPhonemeIntentParser.Parse(JsonDocument.Parse(
            $$"""{"target":"{{id.Value}}","add":[{"representation":"ng","x":1}]}""").RootElement));
    }
}

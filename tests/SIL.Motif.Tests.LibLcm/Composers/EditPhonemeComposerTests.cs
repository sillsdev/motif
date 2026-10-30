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
        Assert.StartsWith(EditPhonemeComposer.Invalid, Refusal(() => EditPhonemeComposer.Build(cache, new(n, ["n g"], []))));
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

    private static void StoreCode(LcmCache cache, CanonicalId id, string text) =>
        SIL.LCModel.Infrastructure.NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            Find(cache, id).CodesOS[0].Representation.set_String(cache.DefaultVernWs,
                TsStringUtils.MakeString(text, cache.DefaultVernWs)));

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
    public void StillRefusesARespellToAValueWithWhitespace()
    {
        using var cache = pristine.NewScratch();
        var glottal = Phoneme(cache, "glottal stop", "g");
        StoreCode(cache, glottal, "- ");

        Assert.StartsWith(EditPhonemeComposer.Invalid,
            Refusal(() => EditPhonemeComposer.Build(cache, new(glottal, [], [new("- ", "- ")]))));
        Assert.StartsWith(EditPhonemeComposer.Invalid,
            Refusal(() => EditPhonemeComposer.Build(cache, new(glottal, [], [new("- ", "a b")]))));
    }

    [Fact]
    public void RuleComposerRefusalOnAStoredCodeWithWhitespaceNamesTheRepair()
    {
        using var cache = pristine.NewScratch();
        var glottal = Phoneme(cache, "glottal stop", "g");
        var a = Phoneme(cache, "a", "a");
        StoreCode(cache, glottal, "- ");

        var message = Refusal(() => AuthorEnvironmentComposer.Build(cache,
            new("after glottal", [new(Phoneme: glottal)], [new(Phoneme: a)])));

        Assert.Contains("phoneme-code-malformed", message);
        Assert.Contains("'- '", message);
        Assert.Contains("compose-edit-phoneme", message);
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

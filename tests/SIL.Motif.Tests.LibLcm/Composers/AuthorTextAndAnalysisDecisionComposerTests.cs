using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Analysis;
using SIL.Motif.Host.Texts;
using SIL.Motif.Model.DryRun;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Composers;

[Collection(LcmCacheParallelCollections.Group1)]
public sealed class AuthorTextAndAnalysisDecisionComposerTests : IDisposable
{
    private readonly LcmCache _cache;
    private readonly string _ws;
    private readonly CanonicalId _sita;

    public AuthorTextAndAnalysisDecisionComposerTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        _ws = _cache.ServiceLocator.WritingSystems.DefaultVernacularWritingSystem.Id;
        _sita = CanonicalId.FromGuid(Seed("sita").Guid);
    }

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [Fact]
    public void AuthoredText_CreatesOnlyMissingWordformsDeclaresThemAndReadsBack()
    {
        var operations = AuthorTextComposer.Build(_cache, Parse(
            $$"""{"title":"Hadithi","ws":"{{_ws}}","paragraphs":[{"segments":[["sita","kitu"],["kitu","Sita"]]}]}"""));

        var creates = operations.Where(op => op.Kind == LangProjectWordformsOperationKinds.Create).ToArray();
        var createText = Assert.Single(operations, op => op.Kind == LangProjectTextsOperationKinds.Create);
        Assert.Equal(2, creates.Length);
        Assert.All(creates, create => Assert.Contains(createText.DependsOn, d => d.OperationId == create.OperationId));
        Assert.DoesNotContain(creates, create => create.After!.Value.GetProperty("text").GetString() == "sita");

        Apply(operations);

        var text = _cache.ServiceLocator.GetInstance<ITextRepository>().GetObject(createText.EntityId!.Value.ToGuid());
        var paragraph = Assert.Single(text.ContentsOA!.ParagraphsOS.OfType<IStTxtPara>());
        Assert.Equal("sita kitu kitu Sita", paragraph.Contents.Text);
        Assert.Equal(2, paragraph.SegmentsOS.Count);
        var read = InterlinearTextReader.ReadWordforms(text).Select(pair => pair.Wordform).ToArray();
        Assert.Equal(_sita.ToGuid(), read[0].Guid);
        Assert.Equal("kitu", read[1].Form.get_String(_cache.DefaultVernWs).Text);
        Assert.Same(read[1], read[2]);
        Assert.Equal("Sita", read[3].Form.get_String(_cache.DefaultVernWs).Text);
        Assert.NotEqual(_sita.ToGuid(), read[3].Guid);
        Assert.Equal(creates.Select(create => create.EntityId!.Value.ToGuid()).Order(),
            new[] { read[1].Guid, read[3].Guid }.Order());
        Assert.Equal(3, _cache.ServiceLocator.GetInstance<IWfiWordformRepository>().AllInstances().Count());
    }

    [Fact]
    public void AuthoredText_WithOnlyKnownWordsCreatesJustTheText()
    {
        var operations = AuthorTextComposer.Build(_cache, Parse(
            $$"""{"title":"Moja","ws":"{{_ws}}","paragraphs":[{"segments":[["sita"]]}]}"""));

        var only = Assert.Single(operations);
        Assert.Equal(LangProjectTextsOperationKinds.Create, only.Kind);
        Assert.Empty(only.DependsOn);
    }

    [Fact]
    public void AuthoredText_RefusesAnUnknownWritingSystem()
    {
        var failure = Assert.Throws<InvalidOperationException>(() => AuthorTextComposer.Build(_cache, Parse(
            """{"title":"T","ws":"zzz-nope","paragraphs":[{"segments":[["a"]]}]}""")));
        Assert.Contains("not known", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DecisionComposer_ApprovesDisapprovesReturnsToUnknownAndMarksMisspelledThenReadsBack()
    {
        var wordform = _cache.ServiceLocator.GetInstance<IWfiWordformRepository>().GetObject(_sita.ToGuid());
        var (first, second) = (SeedAnalysis(wordform), SeedAnalysis(wordform));
        var agent = _cache.LangProject.DefaultUserAgent;

        Apply(Decide($$"""{"decision":"approve","analysis":"{{Id(first)}}"}"""));
        Assert.Equal(Opinions.approves, first.GetAgentOpinion(agent));

        Apply(Decide($$"""{"decision":"disapprove","analysis":"{{Id(second)}}"}"""));
        Assert.Equal(Opinions.disapproves, second.GetAgentOpinion(agent));

        Apply(Decide($$"""{"decision":"unknown","analysis":"{{Id(first)}}"}"""));
        Assert.Equal(Opinions.noopinion, first.GetAgentOpinion(agent));

        Apply(Decide($$"""{"decision":"misspelled","wordform":"{{_sita.Value}}"}"""));
        Assert.Equal(2, wordform.SpellingStatus);
    }

    [Fact]
    public void DecisionComposer_RefusesWhatItCannotResolveOrHasNothingToReturn()
    {
        var wordform = _cache.ServiceLocator.GetInstance<IWfiWordformRepository>().GetObject(_sita.ToGuid());
        var analysis = SeedAnalysis(wordform);

        Assert.Contains("no decision", Assert.Throws<InvalidOperationException>(() =>
            Decide($$"""{"decision":"unknown","analysis":"{{Id(analysis)}}"}""")).Message, StringComparison.Ordinal);
        Assert.Contains("not a stored analysis", Assert.Throws<InvalidOperationException>(() =>
            Decide($$"""{"decision":"approve","analysis":"{{_sita.Value}}"}""")).Message, StringComparison.Ordinal);
        Assert.Contains("not an existing wordform", Assert.Throws<InvalidOperationException>(() =>
            Decide($$"""{"decision":"misspelled","wordform":"{{Id(analysis)}}"}""")).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DecisionComposer_StoresAReadingAsUnknownThenApprovesItAndRefusesADuplicate()
    {
        var (form, msa) = EntryMorph();
        var morphs = $$"""[{"form":"{{form}}","msa":"{{msa}}"}]""";

        var added = Decide($$"""{"decision":"add-reading","wordform":"{{_sita.Value}}","morphs":{{morphs}}}""");
        var create = Assert.Single(added, op => op.Kind == WfiAnalysisOperationKinds.CreateAnalysis);
        Assert.Single(added);
        Apply(added);

        var wordform = _cache.ServiceLocator.GetInstance<IWfiWordformRepository>().GetObject(_sita.ToGuid());
        var stored = Assert.Single(wordform.AnalysesOC, item => item.Guid == create.EntityId!.Value.ToGuid());
        Assert.Equal(Opinions.noopinion, stored.GetAgentOpinion(_cache.LangProject.DefaultUserAgent));
        Assert.Equal(form, CanonicalId.FromGuid(stored.MorphBundlesOS.Single().MorphRA.Guid).Value);

        Apply(Decide($$"""{"decision":"approve","analysis":"{{create.EntityId!.Value.Value}}"}"""));
        Assert.Equal(Opinions.approves, stored.GetAgentOpinion(_cache.LangProject.DefaultUserAgent));

        Assert.Contains("already has a stored analysis", Assert.Throws<InvalidOperationException>(() =>
            Decide($$"""{"decision":"add-reading","wordform":"{{_sita.Value}}","morphs":{{morphs}}}""")).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AnalysisChange_ADecisionOnAStoredIdNeedsThatAnalysisUnderTheWordform()
    {
        var failure = Assert.Throws<InvalidOperationException>(() => AnalysisChangeComposer.Build(_cache,
            new AnalysisChangeIntent(AnalysisChangeKinds.Approve, _sita, null, CanonicalId.Mint())));
        Assert.Contains("Choose one stored analysis under this wordform", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalysisChange_AReadingWithAStoredIdStillMatchesByMorphologyAndRefusesAMismatch()
    {
        var (form, msa) = EntryMorph();
        Apply(Decide($$"""{"decision":"add-reading","wordform":"{{_sita.Value}}","morphs":[{"form":"{{form}}","msa":"{{msa}}"}]}"""));
        var wordform = _cache.ServiceLocator.GetInstance<IWfiWordformRepository>().GetObject(_sita.ToGuid());
        var stored = Assert.Single(wordform.AnalysesOC, item => item.MorphBundlesOS.Count == 1);
        var id = CanonicalId.FromGuid(stored.Guid);
        var reading = new ParseAnalysis([new ParseMorph(CanonicalId.Parse(form).ToGuid().ToString("D"),
            CanonicalId.Parse(msa).ToGuid().ToString("D"), null, null)]);

        var operations = AnalysisChangeComposer.Build(_cache,
            new AnalysisChangeIntent(AnalysisChangeKinds.Approve, _sita, reading, id));
        var approve = Assert.Single(operations);
        Assert.Equal(WfiAnalysisOperationKinds.AddRefEvaluations, approve.Kind);
        Assert.Equal(id, approve.Target);

        var other = new ParseAnalysis([new ParseMorph(null, null, null, "zzz")]);
        Assert.Contains("does not match the reading", Assert.Throws<InvalidOperationException>(() =>
            AnalysisChangeComposer.Build(_cache,
                new AnalysisChangeIntent(AnalysisChangeKinds.Approve, _sita, other, id))).Message,
            StringComparison.Ordinal);
    }

    private (string Form, string Msa) EntryMorph()
    {
        var entry = _cache.ServiceLocator.GetInstance<ILexEntryRepository>().AllInstances()
            .First(item => item.LexemeFormOA is not null && item.MorphoSyntaxAnalysesOC.Any());
        return (CanonicalId.FromGuid(entry.LexemeFormOA.Guid).Value,
            CanonicalId.FromGuid(entry.MorphoSyntaxAnalysesOC.First().Guid).Value);
    }

    private IReadOnlyList<OperationEnvelope> Decide(string intent) =>
        EditAnalysisDecisionComposer.Build(_cache, EditAnalysisDecisionIntentParser.Parse(Json(intent)));

    private void Apply(IReadOnlyList<OperationEnvelope> operations)
    {
        var proposal = new Proposal(new Dictionary<string, string> { ["analysis"] = "1.0", ["texts"] = "1.0" },
            CanonicalId.Mint(), null, operations);
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "tester");
    }

    private static string Id(IWfiAnalysis analysis) => CanonicalId.FromGuid(analysis.Guid).Value;

    private IWfiWordform Seed(string form)
    {
        IWfiWordform created = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () => created = _cache.ServiceLocator
            .GetInstance<IWfiWordformFactory>().Create(TsStringUtils.MakeString(form, _cache.DefaultVernWs)));
        return created;
    }

    private IWfiAnalysis SeedAnalysis(IWfiWordform wordform)
    {
        IWfiAnalysis created = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            created = _cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
            wordform.AnalysesOC.Add(created);
        });
        return created;
    }

    private static AuthorTextIntent Parse(string json) => AuthorTextIntentParser.Parse(Json(json));

    internal static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

public sealed class AuthorTextAndAnalysisDecisionParserTests
{
    private static JsonElement Json(string json) => AuthorTextAndAnalysisDecisionComposerTests.Json(json);

    [Fact]
    public void AuthorText_ParsesTitleWritingSystemAndTokens()
    {
        var intent = AuthorTextIntentParser.Parse(Json(
            """{"title":"T","ws":"swh","paragraphs":[{"segments":[["a","b"],["c"]]},{"segments":[["d"]]}]}"""));

        Assert.Equal(("T", "swh"), (intent.Title, intent.WritingSystem));
        Assert.Equal(2, intent.Paragraphs.Count);
        Assert.Equal(["a", "b"], intent.Paragraphs[0].Segments[0]);
    }

    [Theory]
    [InlineData("""{"title":"T","ws":"swh","paragraphs":[{"segments":[["a"]]}],"extra":1}""")]
    [InlineData("""{"title":"T","title":"U","ws":"swh","paragraphs":[{"segments":[["a"]]}]}""")]
    [InlineData("""{"title":"T","ws":"swh"}""")]
    [InlineData("""{"title":"T","ws":"swh","paragraphs":[]}""")]
    [InlineData("""{"title":"T","ws":"swh","paragraphs":[{"segments":[]}]}""")]
    [InlineData("""{"title":"T","ws":"swh","paragraphs":[{"segments":[[]]}]}""")]
    [InlineData("""{"title":"T","ws":"swh","paragraphs":[{"segments":[["a b"]]}]}""")]
    [InlineData("""{"title":"T","ws":"swh","paragraphs":[{"segments":[[""]]}]}""")]
    [InlineData("""{"title":"T","ws":"swh","paragraphs":[{"segments":[["a"]],"id":"x"}]}""")]
    [InlineData("""{"title":"T","ws":"swh","paragraphs":[{"segments":["a"]}]}""")]
    [InlineData("""{"title":" T","ws":"swh","paragraphs":[{"segments":[["a"]]}]}""")]
    [InlineData("{\"title\":\"T\",\"ws\":\"swh\",\"paragraphs\":[{\"segments\":[[\"\\u00e9\"]]}]}")]
    public void AuthorText_RefusesAnythingOutsideTheClosedShape(string json) =>
        Assert.Throws<ContractParseException>(() => AuthorTextIntentParser.Parse(Json(json)));

    [Theory]
    [InlineData("""{"decision":"approve","analysis":"x"}""", "approve")]
    [InlineData("""{"decision":"misspelled","wordform":"x"}""", "misspelled")]
    public void Decision_ParsesExactlyTheIdentityItNeeds(string template, string decision)
    {
        var id = CanonicalId.Mint().Value;
        var intent = EditAnalysisDecisionIntentParser.Parse(Json(template.Replace("\"x\"", $"\"{id}\"")
            .Replace("\"decision\":\"" + decision + "\",", "\"decision\":\"" + decision + "\",")));
        Assert.Equal(decision, intent.Decision);
        Assert.Equal(id, (intent.Analysis ?? intent.Wordform)!.Value.Value);
    }

    [Fact]
    public void Decision_ParsesAReadingAsAnOrderedMorphList()
    {
        var (a, b) = (CanonicalId.Mint().Value, CanonicalId.Mint().Value);
        var intent = EditAnalysisDecisionIntentParser.Parse(Json(
            $$"""{"decision":"add-reading","wordform":"{{a}}","morphs":[{"form":"{{b}}"},{"guessedString":"x"}]}"""));
        Assert.Equal(2, intent.Morphs!.Count);
        Assert.Equal(b, intent.Morphs[0].Form!.Value.Value);
        Assert.Equal("x", intent.Morphs[1].GuessedString);
    }

    [Theory]
    [InlineData("""{"decision":"add-reading","wordform":"AAAAAAAAAAAAAAAAAAAAAA"}""")]
    [InlineData("""{"decision":"add-reading","wordform":"AAAAAAAAAAAAAAAAAAAAAA","morphs":[]}""")]
    [InlineData("""{"decision":"add-reading","wordform":"AAAAAAAAAAAAAAAAAAAAAA","morphs":[{}]}""")]
    [InlineData("""{"decision":"add-reading","wordform":"AAAAAAAAAAAAAAAAAAAAAA","morphs":[{"form":"AAAAAAAAAAAAAAAAAAAAAA","x":1}]}""")]
    [InlineData("""{"decision":"add-reading","wordform":"AAAAAAAAAAAAAAAAAAAAAA","morphs":[{"guessedString":" "}]}""")]
    [InlineData("""{"decision":"add-reading","analysis":"AAAAAAAAAAAAAAAAAAAAAA","morphs":[{"guessedString":"x"}]}""")]
    [InlineData("""{"decision":"approve","analysis":"AAAAAAAAAAAAAAAAAAAAAA","morphs":[{"guessedString":"x"}]}""")]
    public void Decision_RefusesAMalformedReading(string json) =>
        Assert.ThrowsAny<Exception>(() => EditAnalysisDecisionIntentParser.Parse(Json(json)));

    [Theory]
    [InlineData("""{"decision":"maybe","analysis":"AAAAAAAAAAAAAAAAAAAAAA"}""")]
    [InlineData("""{"decision":"approve"}""")]
    [InlineData("""{"decision":"approve","wordform":"AAAAAAAAAAAAAAAAAAAAAA"}""")]
    [InlineData("""{"decision":"misspelled","analysis":"AAAAAAAAAAAAAAAAAAAAAA"}""")]
    [InlineData("""{"decision":"misspelled"}""")]
    [InlineData("""{"decision":"approve","analysis":"AAAAAAAAAAAAAAAAAAAAAA","wordform":"AAAAAAAAAAAAAAAAAAAAAA"}""")]
    [InlineData("""{"decision":"approve","decision":"unknown","analysis":"AAAAAAAAAAAAAAAAAAAAAA"}""")]
    [InlineData("""{"decision":"approve","analysis":"AAAAAAAAAAAAAAAAAAAAAA","extra":true}""")]
    [InlineData("""{"decision":"approve","analysis":"not-an-id"}""")]
    public void Decision_RefusesAnythingOutsideTheClosedShape(string json) =>
        Assert.ThrowsAny<Exception>(() => EditAnalysisDecisionIntentParser.Parse(Json(json)));
}

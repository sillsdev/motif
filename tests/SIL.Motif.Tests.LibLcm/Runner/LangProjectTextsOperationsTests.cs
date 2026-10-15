using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Host.Texts;
using SIL.Motif.Model.DryRun;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Runner.Snapshotting;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Tests.TestFixtures;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using Xunit;

namespace SIL.Motif.Tests.Runner;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group1)]
public sealed class LangProjectTextsOperationsTests : IDisposable
{
    private readonly LcmCache _cache;
    private readonly string _ws;
    private readonly CanonicalId _sita;
    private readonly CanonicalId _mutu;

    public LangProjectTextsOperationsTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        _ws = _cache.ServiceLocator.WritingSystems.DefaultVernacularWritingSystem.Id;
        _sita = Seed("sita");
        _mutu = Seed("mutu");
    }

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [Fact]
    public void CreateText_RoundTripsWithEveryIdentityParagraphTextAndOccurrenceBinding()
    {
        var textId = CanonicalId.Mint();
        var (paragraph, firstSegment, secondSegment) = (CanonicalId.Mint(), CanonicalId.Mint(), CanonicalId.Mint());
        var proposal = Proposal(textId, Payload(textId, CanonicalId.Mint(), paragraph,
            (firstSegment, [_sita, _mutu]), (secondSegment, [_sita])));
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        var effect = Assert.Single(dryRun.ExpectedEffects);
        Assert.Empty(effect.Before);
        Assert.Equal("3", effect.After["words"]);
        Assert.Equal("2", effect.After["segments"]);

        var receipt = ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "tester");

        var text = Assert.Single(_cache.ServiceLocator.GetInstance<ITextRepository>().AllInstances(),
            item => item.Guid == textId.ToGuid());
        var created = Assert.Single(text.ContentsOA!.ParagraphsOS.OfType<IStTxtPara>());
        Assert.Equal(paragraph.ToGuid(), created.Guid);
        Assert.Equal("sita mutu sita", created.Contents.Text);
        Assert.Equal([firstSegment.ToGuid(), secondSegment.ToGuid()], created.SegmentsOS.Select(segment => segment.Guid));
        Assert.Equal(new[] { _sita, _mutu, _sita }.Select(id => id.ToGuid()),
            InterlinearTextReader.ReadWordforms(text).Select(pair => pair.Wordform.Guid));
        Assert.Equal(effect.After["structure"], Assert.Single(receipt.ActualEffects).After["structure"]);
        Assert.Equal("Hadithi", text.Name.get_String(_cache.WritingSystemFactory.GetWsFromStr(_ws)).Text);
    }

    [Fact]
    public void CreateText_CanFollowItsWordformCreatesInOneProposal()
    {
        var wordform = CanonicalId.Mint();
        var createWord = new OperationEnvelope(CanonicalId.Mint(), LangProjectWordformsOperationKinds.Create, wordform,
            Root(), Json(new { ws = _ws, text = "kitu" }));
        var textId = CanonicalId.Mint();
        var createText = new OperationEnvelope(CanonicalId.Mint(), LangProjectTextsOperationKinds.Create, textId, Root(),
            Json(Payload(textId, CanonicalId.Mint(), CanonicalId.Mint(), (CanonicalId.Mint(), [wordform, _sita]))),
            dependsOn: [new OperationDependency(createWord.OperationId)]);
        var proposal = new Proposal(new Dictionary<string, string> { ["analysis"] = "1.0", ["texts"] = "1.0" },
            CanonicalId.Mint(), null, [createWord, createText]);

        var dryRun = ScratchDryRun.Of(_cache, proposal);
        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "tester");

        var text = _cache.ServiceLocator.GetInstance<ITextRepository>().GetObject(textId.ToGuid());
        Assert.Equal(wordform.ToGuid(), InterlinearTextReader.ReadWordforms(text).First().Wordform.Guid);
    }

    [Fact]
    public void CreateText_FailsForAMissingWordformOrOneWithoutAFormInTheParagraphLanguage()
    {
        var textId = CanonicalId.Mint();
        var missing = Proposal(textId, Payload(textId, CanonicalId.Mint(), CanonicalId.Mint(),
            (CanonicalId.Mint(), [CanonicalId.Mint()])));
        var failure = Assert.ThrowsAny<Exception>(() => ScratchDryRun.Of(_cache, missing));
        Assert.Contains("Create it first", failure.Message, StringComparison.Ordinal);

        var textId2 = CanonicalId.Mint();
        var otherWs = _cache.ServiceLocator.WritingSystems.DefaultAnalysisWritingSystem.Id;
        var wrongWs = Proposal(textId2, Payload(textId2, CanonicalId.Mint(), CanonicalId.Mint(),
            (CanonicalId.Mint(), [_sita]), ws: otherWs));
        Assert.Contains("has no form in", Assert.ThrowsAny<Exception>(() => ScratchDryRun.Of(_cache, wrongWs)).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CreateText_RefusesAnyCreatedIdentityThatAlreadyExists()
    {
        var textId = CanonicalId.Mint();
        var reused = Proposal(textId, Payload(textId, CanonicalId.Mint(), CanonicalId.Mint(), (_sita, [_sita])));

        var failure = Assert.ThrowsAny<Exception>(() => ScratchDryRun.Of(_cache, reused));

        Assert.Contains("semantic conflict", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateText_RefusesPlacementOverridesAndANonRootTarget()
    {
        var textId = CanonicalId.Mint();
        var after = Payload(textId, CanonicalId.Mint(), CanonicalId.Mint(), (CanonicalId.Mint(), [_sita]));
        OperationEnvelope Op(Placement? placement = null, CanonicalId? storage = null, CanonicalId? target = null) =>
            new(CanonicalId.Mint(), LangProjectTextsOperationKinds.Create, textId, target ?? Root(), Json(after),
                placement, storageIdOverride: storage);
        Proposal One(OperationEnvelope op) => new(new Dictionary<string, string> { ["texts"] = "1.0" },
            CanonicalId.Mint(), null, [op]);

        Assert.Contains("placement", Assert.ThrowsAny<Exception>(() => ScratchDryRun.Of(_cache,
            One(Op(placement: new Placement(CanonicalId.Mint(), null))))).Message, StringComparison.Ordinal);
        Assert.Contains("storage GUID", Assert.ThrowsAny<Exception>(() => ScratchDryRun.Of(_cache,
            One(Op(storage: CanonicalId.Mint())))).Message, StringComparison.Ordinal);
        Assert.Contains("not a LangProject", Assert.ThrowsAny<Exception>(() => ScratchDryRun.Of(_cache,
            One(Op(target: _sita)))).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateText_ChangedBindingsAfterTheDryRunRollBackNothingPersists()
    {
        var textId = CanonicalId.Mint();
        var proposal = Proposal(textId, Payload(textId, CanonicalId.Mint(), CanonicalId.Mint(),
            (CanonicalId.Mint(), [_sita])));
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            _cache.ServiceLocator.GetInstance<ICmObjectRepository>().GetObject(_sita.ToGuid()).Delete());
        var texts = _cache.ServiceLocator.GetInstance<ITextRepository>().Count;

        Assert.ThrowsAny<Exception>(() => ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "tester"));

        Assert.Equal(texts, _cache.ServiceLocator.GetInstance<ITextRepository>().Count);
    }

    [Fact]
    public void TextSnapshotProjectsIdentityStructureAndOccurrencesAndDiffsToTheAppliedEffect()
    {
        var textId = CanonicalId.Mint();
        var segment = CanonicalId.Mint();
        var proposal = Proposal(textId, Payload(textId, CanonicalId.Mint(), CanonicalId.Mint(), (segment, [_sita, _mutu])));
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "tester");

        var text = _cache.ServiceLocator.GetInstance<ITextRepository>().GetObject(textId.ToGuid());
        var fields = TextOwningSnapshots.Read(_cache, text).AlternativesFields[SnapshotFields.LangProjectTexts];

        Assert.Equal(textId.Value, fields["ref"]);
        Assert.Equal(_sita.Value, fields["w:" + segment.Value + ":0"]);
        Assert.Equal(_mutu.Value, fields["w:" + segment.Value + ":1"]);
        Assert.Equal("Hadithi", fields["name:" + _ws]);
    }

    [Fact]
    public void CreateText_BaselineReaderSeesTheContractedLinesOffsetsAndOccurrenceBindings()
    {
        var textId = CanonicalId.Mint();
        var (firstSegment, secondSegment) = (CanonicalId.Mint(), CanonicalId.Mint());
        var paragraphId = CanonicalId.Mint();
        var proposal = Proposal(textId, Payload(textId, CanonicalId.Mint(), paragraphId,
            (firstSegment, [_sita, _mutu]), (secondSegment, [_mutu, _sita, _sita])));
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "tester");

        var projection = TextWordsProjectionBuilder.Build(_cache, CancellationToken.None, new HashSet<Guid> { textId.ToGuid() });

        var text = Assert.Single(projection.Texts);
        Assert.Equal("Hadithi", text.Title);
        Assert.Equal([1, 2], text.Lines.Select(line => line.Number));
        Assert.All(text.Lines, line => Assert.Equal(paragraphId.ToGuid(), line.ParagraphId));
        Assert.Equal([firstSegment.ToGuid(), secondSegment.ToGuid()], text.Lines.Select(line => line.SegmentId));
        Assert.Equal(new[] { _sita, _mutu }.Select(id => id.ToGuid()), text.Lines[0].Tokens.Select(token => token.WordformId!.Value));
        Assert.Equal(new[] { _mutu, _sita, _sita }.Select(id => id.ToGuid()), text.Lines[1].Tokens.Select(token => token.WordformId!.Value));
        Assert.Equal([0, 1], text.Lines[0].Tokens.Select(token => token.OccurrenceIndex));
        Assert.Equal([0, 1, 2], text.Lines[1].Tokens.Select(token => token.OccurrenceIndex));
        Assert.Equal(["sita", "mutu"], text.Lines[0].Tokens.Select(token => token.Forms.Single().Text));
        var paragraph = Assert.Single(_cache.ServiceLocator.GetInstance<ITextRepository>().GetObject(textId.ToGuid())
            .ContentsOA!.ParagraphsOS.OfType<IStTxtPara>());
        Assert.Equal(["sita mutu ", "mutu sita sita"], paragraph.SegmentsOS.Select(segment => segment.BaselineText.Text));
        Assert.Equal([0, 10], paragraph.SegmentsOS.Select(segment => segment.BeginOffset));
        Assert.True(paragraph.ParseIsCurrent);
    }

    [Fact]
    public void CreateText_AWordformCreatedInTheSameProposalMustBeDeclaredWhateverTheArrayOrder()
    {
        var wordform = CanonicalId.Mint();
        var createWord = new OperationEnvelope(CanonicalId.Mint(), LangProjectWordformsOperationKinds.Create, wordform,
            Root(), Json(new { ws = _ws, text = "kitu" }));
        var textId = CanonicalId.Mint();
        var createText = new OperationEnvelope(CanonicalId.Mint(), LangProjectTextsOperationKinds.Create, textId, Root(),
            Json(Payload(textId, CanonicalId.Mint(), CanonicalId.Mint(), (CanonicalId.Mint(), [wordform]))));
        Proposal Of(params OperationEnvelope[] operations) => new(
            new Dictionary<string, string> { ["analysis"] = "1.0", ["texts"] = "1.0" }, CanonicalId.Mint(), null, operations);

        var wordformFirst = Assert.Throws<ContractParseException>(() => ScratchDryRun.Of(_cache, Of(createWord, createText)));
        var textFirst = Assert.Throws<ContractParseException>(() => ScratchDryRun.Of(_cache, Of(createText, createWord)));

        Assert.Contains("without declaring it in dependsOn", wordformFirst.Message, StringComparison.Ordinal);
        Assert.Contains("without declaring it in dependsOn", textFirst.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateText_ATransitiveDeclaredDependencyIsEnoughInAnyArrayOrder()
    {
        var (first, second) = (CanonicalId.Mint(), CanonicalId.Mint());
        var createFirst = new OperationEnvelope(CanonicalId.Mint(), LangProjectWordformsOperationKinds.Create, first, Root(),
            Json(new { ws = _ws, text = "kitu" }));
        var createSecond = new OperationEnvelope(CanonicalId.Mint(), LangProjectWordformsOperationKinds.Create, second, Root(),
            Json(new { ws = _ws, text = "kiti" }), dependsOn: [new OperationDependency(createFirst.OperationId)]);
        var textId = CanonicalId.Mint();
        var createText = new OperationEnvelope(CanonicalId.Mint(), LangProjectTextsOperationKinds.Create, textId, Root(),
            Json(Payload(textId, CanonicalId.Mint(), CanonicalId.Mint(), (CanonicalId.Mint(), [first, second]))),
            dependsOn: [new OperationDependency(createSecond.OperationId)]);
        var proposal = new Proposal(new Dictionary<string, string> { ["analysis"] = "1.0", ["texts"] = "1.0" },
            CanonicalId.Mint(), null, [createText, createSecond, createFirst]);

        var dryRun = ScratchDryRun.Of(_cache, proposal);
        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "tester");

        Assert.Equal(2, InterlinearTextReader.ReadWordforms(
            _cache.ServiceLocator.GetInstance<ITextRepository>().GetObject(textId.ToGuid())).Count());
    }

    [Fact]
    public void CreateText_AnEarlierSuccessfulOperationIsRolledBackWhenTheTextFails()
    {
        var wordform = CanonicalId.Mint();
        var createWord = new OperationEnvelope(CanonicalId.Mint(), LangProjectWordformsOperationKinds.Create, wordform,
            Root(), Json(new { ws = _ws, text = "kitu" }));
        var textId = CanonicalId.Mint();
        var createText = new OperationEnvelope(CanonicalId.Mint(), LangProjectTextsOperationKinds.Create, textId, Root(),
            Json(Payload(textId, CanonicalId.Mint(), CanonicalId.Mint(), (CanonicalId.Mint(), [wordform, _sita]))),
            dependsOn: [new OperationDependency(createWord.OperationId)]);
        var proposal = new Proposal(new Dictionary<string, string> { ["analysis"] = "1.0", ["texts"] = "1.0" },
            CanonicalId.Mint(), null, [createWord, createText]);
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            _cache.ServiceLocator.GetInstance<ICmObjectRepository>().GetObject(_sita.ToGuid()).Delete());
        var wordforms = _cache.ServiceLocator.GetInstance<IWfiWordformRepository>().Count;
        var texts = _cache.ServiceLocator.GetInstance<ITextRepository>().Count;

        Assert.ThrowsAny<Exception>(() => ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "tester"));

        Assert.Equal(wordforms, _cache.ServiceLocator.GetInstance<IWfiWordformRepository>().Count);
        Assert.Equal(texts, _cache.ServiceLocator.GetInstance<ITextRepository>().Count);
        Assert.False(_cache.ServiceLocator.GetInstance<ICmObjectRepository>().IsValidObjectId(wordform.ToGuid()));
    }

    [Fact]
    public void CreateTextPayload_EnforcesTheDeclaredLimits()
    {
        object Segment(int words) => new { id = CanonicalId.Mint().Value, words = Enumerable.Repeat(_sita.Value, words).ToArray() };
        object Paragraph(params object[] segments) => new { id = CanonicalId.Mint().Value, ws = _ws, segments };
        JsonElement Doc(params object[] paragraphs) => Json(new
        {
            name = new { ws = _ws, text = "T" }, contents = CanonicalId.Mint().Value, paragraphs,
        });

        _ = LangProjectTextsCreatePayload.Parse(Doc(Paragraph(Segment(LangProjectTextsCreatePayload.MaxWordsPerSegment))));
        _ = LangProjectTextsCreatePayload.Parse(Doc(Enumerable.Range(0, LangProjectTextsCreatePayload.MaxParagraphs)
            .Select(_ => Paragraph(Segment(1))).ToArray()));
        _ = LangProjectTextsCreatePayload.Parse(Doc(Enumerable.Range(0, 40).Select(_ => Paragraph(Segment(500))).ToArray()));

        Assert.Throws<ContractParseException>(() => LangProjectTextsCreatePayload.Parse(
            Doc(Paragraph(Segment(LangProjectTextsCreatePayload.MaxWordsPerSegment + 1)))));
        Assert.Throws<ContractParseException>(() => LangProjectTextsCreatePayload.Parse(
            Doc(Enumerable.Range(0, LangProjectTextsCreatePayload.MaxParagraphs + 1).Select(_ => Paragraph(Segment(1))).ToArray())));
        Assert.Throws<ContractParseException>(() => LangProjectTextsCreatePayload.Parse(
            Doc(Enumerable.Range(0, 41).Select(_ => Paragraph(Segment(500))).ToArray())));
    }

    [Theory]
    [InlineData("{\"name\":{\"ws\":\"swh\",\"text\":\"T\"},\"contents\":\"AAAAAAAAAAAAAAAAAAAAAQ\"}")]
    [InlineData("{\"name\":{\"ws\":\"swh\",\"text\":\"T\"},\"contents\":\"AAAAAAAAAAAAAAAAAAAAAQ\",\"paragraphs\":[]}")]
    [InlineData("{\"name\":{\"ws\":\"swh\",\"text\":\"T\"},\"contents\":\"AAAAAAAAAAAAAAAAAAAAAQ\",\"paragraphs\":[{\"id\":\"AAAAAAAAAAAAAAAAAAAAAg\",\"ws\":\"swh\",\"segments\":[]}]}")]
    [InlineData("{\"name\":{\"ws\":\"swh\",\"text\":\"T\"},\"contents\":\"AAAAAAAAAAAAAAAAAAAAAQ\",\"paragraphs\":[{\"id\":\"AAAAAAAAAAAAAAAAAAAAAQ\",\"ws\":\"swh\",\"segments\":[{\"id\":\"AAAAAAAAAAAAAAAAAAAAAw\",\"words\":[\"AAAAAAAAAAAAAAAAAAAABA\"]}]}]}")]
    [InlineData("{\"name\":{\"ws\":\"swh\",\"text\":\"T\"},\"contents\":\"AAAAAAAAAAAAAAAAAAAAAQ\",\"paragraphs\":[{\"id\":\"AAAAAAAAAAAAAAAAAAAAAg\",\"ws\":\"swh\",\"segments\":[{\"id\":\"AAAAAAAAAAAAAAAAAAAAAw\",\"words\":[]}]}]}")]
    [InlineData("{\"name\":{\"ws\":\"swh\",\"text\":\"T\"},\"contents\":\"AAAAAAAAAAAAAAAAAAAAAQ\",\"paragraphs\":[{\"id\":\"AAAAAAAAAAAAAAAAAAAAAg\",\"ws\":\"swh\",\"segments\":[{\"id\":\"AAAAAAAAAAAAAAAAAAAAAw\",\"words\":[\"not an id\"]}]}]}")]
    [InlineData("{\"name\":{\"ws\":\"swh\",\"text\":\"T\"},\"contents\":\"AAAAAAAAAAAAAAAAAAAAAQ\",\"extra\":1,\"paragraphs\":[]}")]
    public void CreateTextPayload_IsClosedAndRefusesMissingEmptyDuplicateOrMalformedParts(string json)
    {
        using var document = JsonDocument.Parse(json);

        Assert.Throws<ContractParseException>(() => LangProjectTextsCreatePayload.Parse(document.RootElement));
    }

    private CanonicalId Seed(string form)
    {
        IWfiWordform created = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () => created = _cache.ServiceLocator
            .GetInstance<IWfiWordformFactory>().Create(TsStringUtils.MakeString(form, _cache.DefaultVernWs)));
        return CanonicalId.FromGuid(created.Guid);
    }

    private CanonicalId Root() => CanonicalId.FromGuid(_cache.LangProject.Guid);

    private static JsonElement Json(object value)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value));
        return document.RootElement.Clone();
    }

    private object Payload(CanonicalId text, CanonicalId contents, CanonicalId paragraph,
        (CanonicalId Id, CanonicalId[] Words) first, string? ws = null) =>
        Payload(text, contents, paragraph, ws, first);

    private object Payload(CanonicalId text, CanonicalId contents, CanonicalId paragraph,
        params (CanonicalId Id, CanonicalId[] Words)[] segments) =>
        Payload(text, contents, paragraph, null, segments);

    private object Payload(CanonicalId text, CanonicalId contents, CanonicalId paragraph, string? ws,
        params (CanonicalId Id, CanonicalId[] Words)[] segments) => new
    {
        name = new { ws = _ws, text = "Hadithi" },
        contents = contents.Value,
        paragraphs = new[]
        {
            new
            {
                id = paragraph.Value, ws = ws ?? _ws,
                segments = segments.Select(segment => new { id = segment.Id.Value, words = segment.Words.Select(w => w.Value).ToArray() }).ToArray(),
            },
        },
    };

    private Proposal Proposal(CanonicalId textId, object after) => new(
        new Dictionary<string, string> { ["texts"] = "1.0" }, CanonicalId.Mint(), null,
        [new OperationEnvelope(CanonicalId.Mint(), LangProjectTextsOperationKinds.Create, textId, Root(), Json(after))]);
}

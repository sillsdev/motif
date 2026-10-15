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

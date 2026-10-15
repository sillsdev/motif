using System.Text;
using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Model.DryRun;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using Xunit;

namespace SIL.Motif.Tests.Runner;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group1)]
public sealed class LangProjectWordformsOperationsTests : IDisposable
{
    private readonly LcmCache _cache;
    private readonly string _ws;

    public LangProjectWordformsOperationsTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        _ws = _cache.ServiceLocator.WritingSystems.DefaultVernacularWritingSystem.Id;
    }

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [Fact]
    public void CreateWordform_RoundTripsThroughDryRunAndApplyWithItsFormAndIdentity()
    {
        var identity = CanonicalId.Mint();
        var proposal = Create(identity, "sita\u0301");
        var count = Wordforms().Count;

        var dryRun = ScratchDryRun.Of(_cache, proposal);
        var effect = Assert.Single(dryRun.ExpectedEffects);
        Assert.Empty(effect.Before);
        Assert.Equal(identity.Value, effect.After["ref"]);
        Assert.Equal("sita\u0301", effect.After["form:" + _ws]);
        Assert.Equal(count, Wordforms().Count);

        var receipt = ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "tester");

        var created = Assert.Single(Wordforms(), word => word.Guid == identity.ToGuid());
        Assert.Equal("sita\u0301", created.Form.get_String(_cache.WritingSystemFactory.GetWsFromStr(_ws)).Text);
        Assert.Equal(count + 1, Wordforms().Count);
        Assert.Single(receipt.ActualEffects);
    }

    [Fact]
    public void CreateWordform_RefusesAFormThatAlreadyExistsAndNamesTheExistingIdentity()
    {
        var first = CanonicalId.Mint();
        var proposal = Create(first, "mutu");
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "tester");

        var failure = Assert.ThrowsAny<Exception>(() => ScratchDryRun.Of(_cache, Create(CanonicalId.Mint(), "mutu")));

        Assert.Contains(first.Value, failure.Message, StringComparison.Ordinal);
        Assert.Contains("reuse that identity", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateWordform_RefusesAnIdentityThatAlreadyExistsAsAnotherClass()
    {
        var failure = Assert.ThrowsAny<Exception>(() => ScratchDryRun.Of(_cache,
            Create(CanonicalId.FromGuid(_cache.LangProject.Guid), "taken")));

        Assert.Contains("semantic conflict", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateWordform_RefusesAnUnknownWritingSystemWithoutChangingTheProject()
    {
        var count = Wordforms().Count;

        var failure = Assert.ThrowsAny<Exception>(() => ScratchDryRun.Of(_cache,
            Proposal(CanonicalId.Mint(), new { ws = "zz-not-a-tag-xx", text = "word" })));

        Assert.Contains("not known to this project", failure.Message, StringComparison.Ordinal);
        Assert.Equal(count, Wordforms().Count);
    }

    [Theory]
    [InlineData("{\"ws\":\"en\"}")]
    [InlineData("{\"ws\":\"en\",\"text\":\"a b\"}")]
    [InlineData("{\"ws\":\"en\",\"text\":\"\"}")]
    [InlineData("{\"ws\":\"en\",\"text\":\"word\",\"extra\":1}")]
    [InlineData("{\"ws\":\"en\",\"text\":\"\\u00e9\"}")]
    public void CreateWordformPayload_IsClosedAndRefusesBlankSpacedOrNonNfdForms(string json)
    {
        using var document = JsonDocument.Parse(json);

        Assert.Throws<ContractParseException>(() => LangProjectWordformsCreatePayload.Parse(document.RootElement));
    }

    [Fact]
    public void CreateWordformPayload_AcceptsOneNfdWordInOneWritingSystem()
    {
        using var document = JsonDocument.Parse("{\"ws\":\"swh\",\"text\":\"sita\"}");

        Assert.Equal(("swh", "sita"), LangProjectWordformsCreatePayload.Parse(document.RootElement));
        Assert.True("sita".IsNormalized(NormalizationForm.FormD));
    }

    private List<IWfiWordform> Wordforms() =>
        _cache.ServiceLocator.GetInstance<IWfiWordformRepository>().AllInstances().ToList();

    private Proposal Create(CanonicalId identity, string text) => Proposal(identity, new { ws = _ws, text });

    private Proposal Proposal(CanonicalId identity, object after)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(after));
        return new Proposal(new Dictionary<string, string> { ["analysis"] = "1.0" }, CanonicalId.Mint(), null,
            [new OperationEnvelope(CanonicalId.Mint(), LangProjectWordformsOperationKinds.Create, identity,
                CanonicalId.FromGuid(_cache.LangProject.Guid), json.RootElement.Clone())]);
    }
}

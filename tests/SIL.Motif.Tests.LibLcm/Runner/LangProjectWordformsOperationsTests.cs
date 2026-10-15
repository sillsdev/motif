using System.Text;
using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Model.DryRun;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Runner.Snapshotting;
using SIL.LCModel.Infrastructure;
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

    [Fact]
    public void CreateWordform_RefusesAnIdentityThatAlreadyExistsAsAWordform()
    {
        var existing = Seed("sameid");

        var failure = Assert.ThrowsAny<Exception>(() => ScratchDryRun.Of(_cache, Create(existing, "another")));

        Assert.Contains("already exists", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateWordform_RefusesTheSameFormTwiceInOneProposal()
    {
        var proposal = Many(Op(CanonicalId.Mint(), new { ws = _ws, text = "dup" }),
            Op(CanonicalId.Mint(), new { ws = _ws, text = "dup" }));

        var failure = Assert.ThrowsAny<Exception>(() => ScratchDryRun.Of(_cache, proposal));

        Assert.Contains("reuse that identity", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateWordform_RefusesPlacementAndStorageIdentityOverrides()
    {
        var after = new { ws = _ws, text = "plain" };
        var placed = Many(Op(CanonicalId.Mint(), after, placement: new Placement(CanonicalId.Mint(), null)));
        var overridden = Many(Op(CanonicalId.Mint(), after, storage: CanonicalId.Mint()));

        Assert.Contains("placement", Assert.ThrowsAny<Exception>(() => ScratchDryRun.Of(_cache, placed)).Message,
            StringComparison.Ordinal);
        Assert.Contains("storage GUID", Assert.ThrowsAny<Exception>(() => ScratchDryRun.Of(_cache, overridden)).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CreateWordform_RefusesATargetThatIsNotTheProjectRoot()
    {
        var other = Seed("elsewhere");
        var proposal = Many(Op(CanonicalId.Mint(), new { ws = _ws, text = "target" }, target: other));

        var failure = Assert.ThrowsAny<Exception>(() => ScratchDryRun.Of(_cache, proposal));

        Assert.Contains("not a LangProject", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateWordform_FormTakenUnderAnotherIdentityAfterTheDryRunChangesTheFootprintAndRollsEverythingBack()
    {
        var first = CanonicalId.Mint();
        var second = CanonicalId.Mint();
        var proposal = Many(Op(first, new { ws = _ws, text = "alpha" }), Op(second, new { ws = _ws, text = "beta" }));
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        Seed("beta");
        var count = Wordforms().Count;

        var failure = Assert.ThrowsAny<Exception>(() => ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "tester"));

        Assert.Contains("Dry Run", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(count, Wordforms().Count);
        Assert.DoesNotContain(Wordforms(), word => word.Guid == first.ToGuid());
    }

    [Fact]
    public void WordformSnapshotProjectsIdentityAndFormAndDiffsToTheAppliedEffect()
    {
        var identity = CanonicalId.Mint();
        var proposal = Create(identity, "snap");
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        var before = WordformOwningSnapshots.Read(_cache, identity);

        var receipt = ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "tester");

        var after = WordformOwningSnapshots.Read(_cache, identity);
        Assert.Empty(before.AlternativesFields);
        var fields = after.AlternativesFields[SnapshotFields.LangProjectWordforms];
        Assert.Equal(identity.Value, fields["ref"]);
        Assert.Equal("snap", fields["form:" + _ws]);
        var effect = Assert.Single(AnalysisFieldSnapshots.Diff(before, after));
        Assert.Equal(SnapshotFields.LangProjectWordforms, effect.Field);
        var actual = Assert.Single(receipt.ActualEffects);
        Assert.Equal(actual.After["form:" + _ws], effect.After["form:" + _ws]);
        Assert.Equal(actual.After["ref"], effect.After["ref"]);
    }

    private CanonicalId Seed(string form)
    {
        IWfiWordform created = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () => created = _cache.ServiceLocator
            .GetInstance<IWfiWordformFactory>().Create(TsStringUtils.MakeString(form, _cache.DefaultVernWs)));
        return CanonicalId.FromGuid(created.Guid);
    }

    private OperationEnvelope Op(CanonicalId identity, object after, Placement? placement = null,
        CanonicalId? storage = null, CanonicalId? target = null)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(after));
        return new OperationEnvelope(CanonicalId.Mint(), LangProjectWordformsOperationKinds.Create, identity,
            target ?? CanonicalId.FromGuid(_cache.LangProject.Guid), json.RootElement.Clone(), placement,
            storageIdOverride: storage);
    }

    private static Proposal Many(params OperationEnvelope[] operations) => new(
        new Dictionary<string, string> { ["analysis"] = "1.0" }, CanonicalId.Mint(), null, operations);

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

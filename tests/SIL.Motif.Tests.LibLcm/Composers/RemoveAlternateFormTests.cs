using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Composers;

[Collection(LcmCacheParallelCollections.Group3)]
public sealed class RemoveAlternateFormTests : IDisposable
{
    private readonly LcmCache _cache;

    public RemoveAlternateFormTests(PristineProjectFixture pristine) => _cache = pristine.NewScratch();

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [Fact]
    public void AnUnreferencedAlternateFormIsRemovedAndTheOthersKeepTheirOrder()
    {
        var (entry, forms) = EntryWithAlternates("tuig", "tinuig", "tuyig");
        var operations = Compose(entry, forms[0]);
        var proposal = Proposal(operations);
        ProposalApplier.Apply(_cache, proposal, ScratchDryRun.Of(_cache, proposal).Anchor, "remove-form-tests");

        Assert.Equal([forms[1]], Entry(entry).AlternateFormsOS.Select(form => form.Guid));
        Assert.False(Repository.IsValidObjectId(forms[0]));
        Assert.NotNull(Entry(entry).LexemeFormOA);
    }

    [Fact]
    public void AFormAMorphBundleReferencesIsRefusedNamingTheReferrer()
    {
        var (entry, forms) = EntryWithAlternates("tuig", "tinuig");
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var wordform = _cache.ServiceLocator.GetInstance<IWfiWordformFactory>().Create();
            wordform.Form.set_String(_cache.DefaultVernWs, "tinuig");
            var analysis = _cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
            wordform.AnalysesOC.Add(analysis);
            var bundle = _cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create();
            analysis.MorphBundlesOS.Add(bundle);
            bundle.MorphRA = (IMoForm)Repository.GetObject(forms[0]);
        });
        var refused = Assert.Throws<InvalidOperationException>(() => Compose(entry, forms[0]));
        Assert.Contains("WfiMorphBundle", refused.Message, StringComparison.Ordinal);
        Assert.Contains("tinuig", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLexemeFormAndAFormOfAnotherEntryAreRefused()
    {
        var (entry, forms) = EntryWithAlternates("tuig", "tinuig");
        var (_, otherForms) = EntryWithAlternates("ayu", "ayo");
        var lexeme = Entry(entry).LexemeFormOA.Guid;
        Assert.Contains("lexeme form", Assert.Throws<InvalidOperationException>(() => Compose(entry, lexeme)).Message,
            StringComparison.Ordinal);
        Assert.Contains("alternate form owned", Assert.Throws<InvalidOperationException>(() =>
            Compose(entry, otherForms[0])).Message, StringComparison.Ordinal);
        Assert.NotEmpty(forms);
    }

    [Fact]
    public void DryRunEffectsShowTheRemovedFormAndTheFootprintChangesNothing()
    {
        var (entry, forms) = EntryWithAlternates("tuig", "tinuig", "tuyig");
        var operations = Compose(entry, forms[0]);
        var proposal = Proposal(operations);
        var digestBefore = FootprintProbe.ComputeCurrentFootprintDigest(_cache, proposal);

        var dryRun = ScratchDryRun.Of(_cache, proposal);
        var effect = Assert.Single(dryRun.ExpectedEffects, e => e.Field == SnapshotFields.LexEntryAlternateForms);
        Assert.Equal(2, effect.Before.Count);
        Assert.Single(effect.After);
        var preview = effect.Preview!.Value.GetRawText();
        Assert.Contains("tinuig", preview, StringComparison.Ordinal);
        Assert.Contains("MoStemAllomorph", preview, StringComparison.Ordinal);

        Assert.Equal(2, Entry(entry).AlternateFormsOS.Count);
        Assert.Equal(digestBefore, FootprintProbe.ComputeCurrentFootprintDigest(_cache, proposal));
    }

    [Fact]
    public void ThePayloadIsClosed()
    {
        using var unknown = JsonDocument.Parse($"{{\"form\":\"{Id(Guid.NewGuid()).Value}\",\"force\":true}}");
        using var missing = JsonDocument.Parse("{}");
        Assert.ThrowsAny<Exception>(() => AlternateFormRemovePayload.Parse(unknown.RootElement));
        Assert.ThrowsAny<Exception>(() => AlternateFormRemovePayload.Parse(missing.RootElement));
    }

    private IReadOnlyList<OperationEnvelope> Compose(Guid entry, Guid form) =>
        RemoveAlternateFormComposer.Build(_cache, new RemoveAlternateFormIntent(Id(entry), Id(form)));

    private (Guid Entry, Guid[] Forms) EntryWithAlternates(string lexeme, params string[] alternates)
    {
        var services = _cache.ServiceLocator;
        Guid entryId = default;
        var forms = new List<Guid>();
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var entry = services.GetInstance<ILexEntryFactory>().Create();
            entryId = entry.Guid;
            var stem = services.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphStem);
            var main = services.GetInstance<IMoStemAllomorphFactory>().Create();
            entry.LexemeFormOA = main;
            main.MorphTypeRA = stem;
            main.Form.set_String(_cache.DefaultVernWs, lexeme);
            foreach (var text in alternates)
            {
                var form = services.GetInstance<IMoStemAllomorphFactory>().Create();
                entry.AlternateFormsOS.Add(form);
                form.MorphTypeRA = stem;
                form.Form.set_String(_cache.DefaultVernWs, text);
                forms.Add(form.Guid);
            }
        });
        return (entryId, forms.ToArray());
    }

    private ILexEntry Entry(Guid id) => _cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(id);

    private ICmObjectRepository Repository => _cache.ServiceLocator.GetInstance<ICmObjectRepository>();

    private static CanonicalId Id(Guid guid) => CanonicalId.FromGuid(guid);

    private static Proposal Proposal(IReadOnlyList<OperationEnvelope> operations) => new(
        new Dictionary<string, string> { ["lexical"] = "1.0", ["grammar"] = "1.0" }, CanonicalId.Mint(), null,
        operations);
}

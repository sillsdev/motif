using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Snapshotting;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Runner;

[Collection(LcmCacheParallelCollections.Group1)]
public sealed class MultilingualSnapshotNormalizationTests(PristineProjectFixture pristine)
{
    [Fact]
    public void UnicodeUsesNfdWhileRichStringsComposeWithinMatchingRuns()
    {
        using var cache = pristine.NewScratch();
        var entry = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(pristine.Seed.FirstEntryId);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            entry.CitationForm.set_String(cache.DefaultVernWs, "\u00e9");
            entry.Bibliography.set_String(cache.DefaultAnalWs, TsStringUtils.MakeString("e\u0301", cache.DefaultAnalWs));
            var split = TsStringUtils.MakeString("e\u0301", cache.DefaultAnalWs).GetBldr();
            split.SetIntPropValues(1, 2, (int)FwTextPropType.ktptBold, (int)FwTextPropVar.ktpvEnum, 1);
            entry.Comment.set_String(cache.DefaultAnalWs, split.GetString());
        });
        var snapshot = LexEntrySnapshotter.Snapshot(cache, entry);
        Assert.Equal("e\u0301", snapshot.AlternativesFields[SnapshotFields.LexEntryCitationForm][NewLangProjFixture.VernacularTag]);
        Assert.Equal("\u00e9", snapshot.AlternativesFields[SnapshotFields.LexEntryBibliography][NewLangProjFixture.AnalysisTag]);
        Assert.Equal("e\u0301", snapshot.AlternativesFields[SnapshotFields.LexEntryComment][NewLangProjFixture.AnalysisTag]);
    }
}

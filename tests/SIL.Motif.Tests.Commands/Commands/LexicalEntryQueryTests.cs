using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class LexicalEntryQueryTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-lexical-entry-" + Guid.NewGuid().ToString("N"));
    private string _path = "";
    private Guid _secondSense;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    private void Capture()
    {
        Directory.CreateDirectory(_root);
        using (var cache = pristine.NewScratch())
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var entry = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(pristine.Seed.FirstEntryId);
                var second = cache.ServiceLocator.GetInstance<ILexSenseFactory>().Create();
                entry.SensesOS.Add(second);
                second.Gloss.set_String(cache.DefaultAnalWs, "second sense gloss");
                _secondSense = second.Guid;
            });
            new FwDataProjectLoader().Save(cache);
            _path = cache.ProjectId.Path;
        }
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(_path), _root);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
    }

    private static string Id(Guid guid) => CanonicalId.FromGuid(guid).Value;

    [Fact]
    public void ASenseIdReadsItsEntryWithEverySenseAndMarksTheOneAskedFor()
    {
        Capture();

        var read = LexicalEntryQuery.Query(new LexicalEntryRequest(_path, [Id(_secondSense)]));

        Assert.True(read.Succeeded, read.Refusal?.Message);
        var lookup = Assert.Single(read.Value!.Lookups);
        Assert.True(lookup.Found);
        Assert.Equal(Id(pristine.Seed.FirstEntryId), lookup.EntryId);
        Assert.Equal(Id(_secondSense), lookup.SenseId);
        Assert.Equal(SeededProject.FirstForm, lookup.Headword);
        Assert.Equal([SeededProject.FirstGloss, "second sense gloss"], lookup.Senses.Select(sense => sense.Gloss));
        Assert.Equal(["1", "2"], lookup.Senses.Select(sense => sense.Outline));
        Assert.Equal(Id(pristine.Seed.FirstSenseId), lookup.Senses[0].SenseId);
        Assert.Equal("second sense gloss", lookup.Senses[1].Glosses.Values.Single());
    }

    [Fact]
    public void AnEntryIdReadsTheSameSensesWithNoSenseMarked()
    {
        Capture();

        var lookup = Assert.Single(LexicalEntryQuery.Query(new LexicalEntryRequest(_path, [Id(pristine.Seed.FirstEntryId)])).Value!.Lookups);

        Assert.True(lookup.Found);
        Assert.Null(lookup.SenseId);
        Assert.Equal(2, lookup.Senses.Count);
    }

    [Fact]
    public void AnUnknownIdIsNotFoundWhileTheOthersStillRead()
    {
        Capture();
        var unknown = CanonicalId.Mint().Value;

        var read = LexicalEntryQuery.Query(new LexicalEntryRequest(_path, [unknown, Id(pristine.Seed.FirstSenseId)]));

        Assert.Equal([false, true], read.Value!.Lookups.Select(lookup => lookup.Found));
        Assert.Equal(unknown, read.Value.Lookups[0].Id);
    }

    [Fact]
    public void ReadingWithoutABaselineOrWithBadIdsIsRefused()
    {
        Capture();
        Assert.Equal("lexical-entry.invalid-id", LexicalEntryQuery.Query(new LexicalEntryRequest(_path, ["not an id"])).Refusal!.Code);
        Assert.Equal("lexical-entry.no-id", LexicalEntryQuery.Query(new LexicalEntryRequest(_path, [])).Refusal!.Code);
        var tooMany = Enumerable.Range(0, LexicalEntryQuery.MaxIds + 1).Select(_ => CanonicalId.Mint().Value).ToArray();
        Assert.Equal("lexical-entry.too-many-ids", LexicalEntryQuery.Query(new LexicalEntryRequest(_path, tooMany)).Refusal!.Code);

        using var scratch = pristine.NewScratch();
        var none = LexicalEntryQuery.Query(new LexicalEntryRequest(scratch.ProjectId.Path, [Id(pristine.Seed.FirstEntryId)]));
        Assert.Equal("baseline.missing", none.Refusal!.Code);
    }
}

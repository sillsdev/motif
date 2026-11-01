using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.PanGloss;

/// <summary>
/// Pins that a word's readings come out in an order the project's lexicon decides, whatever order the parser
/// emitted them in, and that ordering never adds, drops or changes a reading.
/// </summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group0)]
public sealed class ReadingOrderTests : IDisposable
{
    private readonly LcmCache _cache;
    private readonly IMoMorphType _stem;
    private readonly IPartOfSpeech _open;
    private readonly IPartOfSpeech _closed;

    public ReadingOrderTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        var services = _cache.ServiceLocator;
        _stem = services.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphStem);
        IPartOfSpeech open = null!, closed = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            open = Pos("open");
            closed = Pos("closed");
            var affix = services.GetInstance<IMoInflAffMsaFactory>().Create();
            Entry("-s", affix, withSense: false);
            affix.PartOfSpeechRA = open;
        });
        _open = open;
        _closed = closed;
    }

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [Fact]
    public void AReadingWhoseRootCategoryNothingAttachesToComesFirst()
    {
        var noun = Stem("noun", _open);
        var particle = Stem("particle", _closed);

        var readings = Read(One(noun), One(particle));

        Assert.Equal(["particle", "noun"], Forms(readings));
    }

    [Fact]
    public void RootsOfOneCategoryGoByHomographNumberThenSensePositionThenMostExamples()
    {
        var second = Stem("kab", _open, homograph: 2);
        var first = Stem("kab", _open, homograph: 1, extraSenses: 1, senseIndex: 1);
        var firstSense0 = Stem("kab", _open, homograph: 1, examples: 1);
        var firstSense0Rich = Stem("kab", _open, homograph: 1, examples: 3);

        var readings = Read(One(second), One(first), One(firstSense0), One(firstSense0Rich));

        Assert.Equal([firstSense0Rich.Id, firstSense0.Id, first.Id, second.Id], Ids(readings));
    }

    [Fact]
    public void FewerMorphemesComeFirstWhenTheRootsTie()
    {
        var root = Stem("lakaw", _open);
        var suffix = Stem("-on", _open);

        var readings = Read(new[] { root, suffix }, new[] { root });

        Assert.Equal([1, 2], readings.Select(reading => reading.Morphs.Count));
    }

    [Fact]
    public void WhateverOrderTheParserEmitsTheResultIsTheSame()
    {
        var a = Stem("a", _open);
        var b = Stem("b", _open);
        var c = Stem("c", _open);
        var d = Stem("d", _closed);
        var emitted = new[] { One(a), One(b), One(c), One(d), new[] { a, b } };

        var expected = Ids(Read(emitted));
        var random = new Random(7);
        for (var round = 0; round < 20; round++)
            Assert.Equal(expected, Ids(Read(emitted.OrderBy(_ => random.Next()).ToArray())));
    }

    [Fact]
    public void OrderingKeepsTheReadingSetAndRecordsEachReadingsEmittedPosition()
    {
        var a = Stem("a", _open);
        var d = Stem("d", _closed);
        var emitted = new[] { One(a), One(d), new[] { a, d } };

        var evidence = Evidence(emitted);
        var readings = ParserReadingReader.Read(_cache, "p", evidence);

        Assert.Equal(evidence.Analyses.Count, readings.Count);
        Assert.Equal(Enumerable.Range(0, emitted.Length), readings.Select(reading => reading.SourceIndex!.Value).Order());
        foreach (var reading in readings)
            Assert.Equal(evidence.Analyses[reading.SourceIndex!.Value].Morphs.Select(morph => morph.Form),
                reading.Morphs.Select(morph => morph.AllomorphId));
        Assert.NotEqual(Enumerable.Range(0, emitted.Length), readings.Select(reading => reading.SourceIndex!.Value));
    }

    [Fact]
    public void AReadingWithNoLexiconKeysSortsAsATieInsteadOfFailing()
    {
        var known = Stem("known", _open);
        var unknown = new ParseAnalysis([new ParseMorph("0c686afa-8d21-4e3b-bc0e-41812150cf4c",
            "14ff3655-598a-4ce5-8186-805c65398553", null, "guess")]);

        var forward = ParserReadingReader.Read(_cache, "p", Evidence([One(known), unknown]));
        var backward = ParserReadingReader.Read(_cache, "p", Evidence([unknown, One(known)]));

        Assert.Equal(forward.Select(reading => reading.Morphs[0].Form), backward.Select(reading => reading.Morphs[0].Form));
    }

    [Fact]
    public void ReadingsThatDifferOnlyInTheCaseOfAnIdentifierStillOrderTheSameWhicheverIsEmittedFirst()
    {
        var root = Stem("same", _open);
        var lower = new ParseAnalysis([new ParseMorph(root.Allomorph.ToString("D"), root.Msa.ToString("D"), null, null)]);
        var upper = new ParseAnalysis([new ParseMorph(root.Allomorph.ToString("D").ToUpperInvariant(),
            root.Msa.ToString("D").ToUpperInvariant(), null, null)]);

        var forward = ParserReadingReader.Read(_cache, "p", Evidence([lower, upper]));
        var backward = ParserReadingReader.Read(_cache, "p", Evidence([upper, lower]));

        Assert.Equal(forward.Select(reading => reading.Morphs[0].AllomorphId),
            backward.Select(reading => reading.Morphs[0].AllomorphId));
        Assert.Equal(forward.Select(reading => reading.SourceIndex).Order(), [0, 1]);
        Assert.Equal(1, forward[0].SourceIndex);
        Assert.Equal(0, backward[0].SourceIndex);
    }

    [Fact]
    public void ClosedCategoriesPassedInGiveTheSameOrderAsThoseFoundPerCall()
    {
        var noun = Stem("noun", _open);
        var particle = Stem("particle", _closed);
        var evidence = Evidence(new[] { One(noun), One(particle) });

        var closed = ParserReadingReader.ClosedCategories(_cache);

        Assert.Contains(_closed.Guid, closed);
        Assert.DoesNotContain(_open.Guid, closed);
        Assert.Equal(Forms(ParserReadingReader.Read(_cache, "p", evidence)),
            Forms(ParserReadingReader.Read(_cache, "p", evidence, null, closed)));
    }

    private sealed record Root(string Form, Guid Allomorph, Guid Msa, Guid EntryId, Guid Id);

    private IPartOfSpeech Pos(string name)
    {
        var pos = _cache.ServiceLocator.GetInstance<IPartOfSpeechFactory>().Create();
        _cache.LangProject.PartsOfSpeechOA.PossibilitiesOS.Add(pos);
        pos.Name.set_String(_cache.DefaultAnalWs, name);
        return pos;
    }

    private ILexEntry Entry(string form, IMoMorphSynAnalysis msa, bool withSense)
    {
        var services = _cache.ServiceLocator;
        var entry = services.GetInstance<ILexEntryFactory>().Create();
        var lexeme = services.GetInstance<IMoStemAllomorphFactory>().Create();
        entry.LexemeFormOA = lexeme;
        lexeme.MorphTypeRA = _stem;
        lexeme.Form.set_String(_cache.DefaultVernWs, form);
        entry.MorphoSyntaxAnalysesOC.Add(msa);
        if (!withSense) return entry;
        var sense = services.GetInstance<ILexSenseFactory>().Create();
        entry.SensesOS.Add(sense);
        sense.MorphoSyntaxAnalysisRA = msa;
        return entry;
    }

    private Root Stem(string form, IPartOfSpeech pos, int homograph = 0, int examples = 0, int extraSenses = 0,
        int senseIndex = 0)
    {
        Root? root = null;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var services = _cache.ServiceLocator;
            var msa = services.GetInstance<IMoStemMsaFactory>().Create();
            var entry = Entry(form, msa, withSense: true);
            msa.PartOfSpeechRA = pos;
            if (homograph > 0) entry.HomographNumber = homograph;
            for (var index = 0; index < extraSenses; index++)
            {
                var other = services.GetInstance<ILexSenseFactory>().Create();
                entry.SensesOS.Insert(0, other);
            }
            for (var index = 0; index < examples; index++)
                entry.SensesOS[extraSenses].ExamplesOS.Add(services.GetInstance<ILexExampleSentenceFactory>().Create());
            _ = senseIndex;
            root = new Root(form, entry.LexemeFormOA!.Guid, msa.Guid, entry.Guid, entry.LexemeFormOA.Guid);
        });
        return root!;
    }

    private static ParseMorph Morph(Root root) => new(root.Allomorph.ToString("D"), root.Msa.ToString("D"), null, null);

    private static Root[] One(Root root) => [root];

    private static ParseAnalysis Analysis(Root[] morphs) => new(morphs.Select(Morph).ToArray());

    private static ParseWordEvidence Evidence(IEnumerable<object> analyses) => new(
        ParseMorphEvidence.Schema, 0, "word", 1, false, false, false,
        analyses.Select(item => item as ParseAnalysis ?? Analysis((Root[])item)).ToArray(), []);

    private IReadOnlyList<ParserReading> Read(params Root[][] emitted) =>
        ParserReadingReader.Read(_cache, "p", Evidence(emitted));

    private static string[] Forms(IEnumerable<ParserReading> readings) =>
        readings.Select(reading => reading.Morphs[0].Form).ToArray();

    private static Guid[] Ids(IEnumerable<ParserReading> readings) =>
        readings.Select(reading => Guid.Parse(reading.Morphs[0].AllomorphId!)).ToArray();
}

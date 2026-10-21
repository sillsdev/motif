using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Runner.Snapshotting;
using SIL.Motif.Tests.Samples;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using static SIL.Motif.Tests.Composers.AffixTestSupport;

namespace SIL.Motif.Tests.Composers;

/// <summary>Process allomorphs added to an entry whose lexeme form is already an affix process.</summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class AuthorAffixProcessAllomorphComposerTests : IDisposable
{
    private const string Tag = "affix-process-allomorph-tests";
    private readonly LcmCache _cache;
    private readonly CanonicalId _category;
    private readonly IPhPhoneme _p, _m, _n, _a;
    private readonly IPhNCSegments _stops;

    public AuthorAffixProcessAllomorphComposerTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        _category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
        _p = Phone("p");
        _m = Phone("m");
        _n = Phone("n");
        _a = Phone("a");
        IPhNCSegments stops = null!;
        Setup(() =>
        {
            stops = _cache.ServiceLocator.GetInstance<IPhNCSegmentsFactory>().Create();
            _cache.LangProject.PhonologicalDataOA.NaturalClassesOS.Add(stops);
            stops.Abbreviation.set_String(_cache.DefaultAnalWs, "Stop");
            stops.SegmentsRC.Add(_p);
        });
        _stops = stops;
    }

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [Fact]
    public void TheFirstAlternate_AppliesWithTheEntrysMorphTypeAndSurvivesReopen()
    {
        var entry = ProcessEntry();
        var entryObject = Get<ILexEntry>(_cache, entry);
        var lexeme = entryObject.LexemeFormOA!.Guid;
        var counts = (entryObject.SensesOS.Count, entryObject.MorphoSyntaxAnalysesOC.Count,
            _cache.LangProject.LexDbOA.Entries.Count());
        var before = SoundSystemSnapshots.Read(_cache);

        var operations = AuthorAffixProcessAllomorphComposer.Build(_cache, Intent(entry));
        Assert.Null(ProposalOperationSlotValidator.FindConflict(operations));
        var proposal = Proposal(operations.Reverse().ToArray());
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        Assert.Empty(SoundSystemSnapshots.Compare(before, SoundSystemSnapshots.Read(_cache)));
        Assert.Equal(dryRun.Anchor.FootprintDigest, FootprintProbe.ComputeCurrentFootprintDigest(_cache, proposal));
        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, Tag);

        var reloaded = Get<ILexEntry>(_cache, entry);
        var alternate = Assert.IsAssignableFrom<IMoAffixProcess>(Assert.Single(reloaded.AlternateFormsOS));
        Assert.Equal(Created(proposal, LexEntryAlternateFormsCreateOperationKinds.CreateAlternateForms).ToGuid(), alternate.Guid);
        Assert.Equal(reloaded.LexemeFormOA!.Guid, lexeme);
        Assert.Equal(((IMoAffixProcess)reloaded.LexemeFormOA).MorphTypeRA!.Guid, alternate.MorphTypeRA!.Guid);
        Assert.False(alternate.IsAbstract);
        Assert.Equal(new[] { "PhSimpleContextSeg", "PhVariable" }, alternate.InputOS.Select(part => part.ClassName));
        Assert.Equal(new[] { "MoInsertPhones", "MoInsertPhones", "MoInsertPhones", "MoCopyFromInput" },
            alternate.OutputOS.Select(part => part.ClassName));
        Assert.Equal(alternate.InputOS[1].Guid, Assert.Single(alternate.OutputOS.OfType<IMoCopyFromInput>()).ContentRA!.Guid);
        Assert.Equal(counts, (reloaded.SensesOS.Count, reloaded.MorphoSyntaxAnalysesOC.Count,
            _cache.LangProject.LexDbOA.Entries.Count()));
        var after = SoundSystemSnapshots.Read(_cache);
        Assert.Contains(SoundSystemSnapshots.Compare(before, after), effect => effect.CanonicalId == Id(alternate));
        new FwDataProjectLoader().Save(_cache);
        using var reopened = new FwDataProjectLoader().LoadScratchCache(_cache.ProjectId.Path);
        Assert.Empty(SoundSystemSnapshots.Compare(after, SoundSystemSnapshots.Read(reopened)));
    }

    [Fact]
    public void ASecondAlternate_NeedsPlacementAndLandsWhereItIsPlaced()
    {
        var entry = ProcessEntry();
        var first = AddAlternate(entry, Intent(entry));
        var second = Intent(entry) with
        {
            Input = [new("c", Phoneme: Id(_m)), new("rest", Remaining: true)],
            Placement = new Placement(first, null),
        };
        var third = second with
        {
            Input = [new("c", Phoneme: Id(_n)), new("rest", Remaining: true)],
            Placement = new Placement(null, first),
        };
        var afterFirst = AddAlternate(entry, second);
        var beforeFirst = AddAlternate(entry, third);
        Assert.Equal(new[] { beforeFirst, first, afterFirst }.Select(id => id.ToGuid()),
            Get<ILexEntry>(_cache, entry).AlternateFormsOS.Select(form => form.Guid));
        Assert.All(Get<ILexEntry>(_cache, entry).AlternateFormsOS, form => Assert.IsAssignableFrom<IMoAffixProcess>(form));
    }

    [Fact]
    public void InflectionClassesLinkToTheNewProcessOnly()
    {
        var entry = ProcessEntry();
        var inflectionClass = AddInflectionClass(_cache, _category, "Class 1");
        var alternate = AddAlternate(entry, Intent(entry) with { InflectionClasses = [inflectionClass] });
        Assert.Equal([inflectionClass.ToGuid()],
            Get<IMoAffixProcess>(_cache, alternate).InflectionClassesRC.Select(item => item.Guid));
        Assert.Empty(((IMoAffixProcess)Get<ILexEntry>(_cache, entry).LexemeFormOA!).InflectionClassesRC);
        var foreign = AddInflectionClass(_cache, AddCategory(_cache, "Other"), "Foreign");
        var failure = Assert.Throws<InvalidOperationException>(() => AuthorAffixProcessAllomorphComposer.Build(
            _cache, Intent(entry, 2) with { InflectionClasses = [foreign], Placement = new Placement(alternate, null) }));
        Assert.Contains("inflection class", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryEntryRefusalNamesTheEntry()
    {
        var missing = CanonicalId.Mint();
        Refused(Intent(missing), missing, "does not exist");
        var stem = AddStem(_cache, "tal", _category).Entry;
        Refused(Intent(stem), stem, "stem");
        var plain = AddInflectionalPrefix(_cache, "pa", _category).Entry;
        Refused(Intent(plain), plain, "mixes plain allomorphs");
        var entry = ProcessEntry();
        Refused(Intent(entry) with { Placement = new Placement(CanonicalId.Mint(), null) }, entry, "first alternate form");
        var first = AddAlternate(entry, Intent(entry));
        var second = Intent(entry, 2);
        Refused(second, entry, "requires placement");
        Refused(second with { Placement = new Placement(CanonicalId.Mint(), null) }, entry, "placement neighbours");
        Refused(Intent(entry) with { Placement = new Placement(first, null) }, entry, "already has");
        var noMsa = ProcessEntry();
        Setup(() => Get<ILexEntry>(_cache, noMsa).MorphoSyntaxAnalysesOC.Clear());
        Refused(Intent(noMsa), noMsa, "no affix grammatical info");
    }

    [Fact]
    public void ADuplicateOfTheLexemeFormPatternIsRefused()
    {
        var entry = ProcessEntry();
        var same = Intent(entry) with
        {
            Input = [new("a", Phoneme: Id(_a)), new("rest", Remaining: true)],
            Output = [new(Insert: [Id(_p), Id(_a)]), new(Copy: "a"), new(Copy: "rest")],
        };
        Refused(same, entry, "already has");
    }

    [Fact]
    public void ShapeAndReferenceFailuresAreRefusedBeforeAnyOperation()
    {
        var entry = ProcessEntry();
        Assert.Throws<InvalidOperationException>(() => AuthorAffixProcessAllomorphComposer.Build(_cache,
            Intent(entry) with { Output = [new(Copy: "c"), new(Copy: "rest")] }));
        Assert.Throws<KeyNotFoundException>(() => AuthorAffixProcessAllomorphComposer.Build(_cache,
            Intent(entry) with { Input = [new("c", Phoneme: CanonicalId.Mint()), new("rest", Remaining: true)] }));
        Assert.Throws<InvalidOperationException>(() => AuthorAffixProcessAllomorphComposer.Build(_cache,
            Intent(entry) with { Output = [new(Copy: "gone"), new(Copy: "rest")] }));
    }

    [Fact]
    public void TheOrdinaryAlternateFormComposerStillRefusesProcessEntries()
    {
        var entry = ProcessEntry();
        AddAlternate(entry, Intent(entry));
        var intent = new AuthorAlternateFormIntent(entry, "mw", Vern(_cache),
            MorphType(MoMorphTypeTags.kguidMorphPrefix));
        var failure = Assert.Throws<InvalidOperationException>(() => AuthorAlternateFormComposer.Build(_cache, intent));
        Assert.Contains("affix process", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AChangeToTheEntryAfterDryRunDriftsTheFootprintUntilTheProposalIsRecomputed()
    {
        var entry = ProcessEntry();
        var proposal = Proposal(AuthorAffixProcessAllomorphComposer.Build(_cache, Intent(entry)));
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        AddAlternate(entry, Intent(entry, 2));
        Assert.NotEqual(dryRun.Anchor.FootprintDigest, FootprintProbe.ComputeCurrentFootprintDigest(_cache, proposal));
        Assert.ThrowsAny<Exception>(() => ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, Tag));
        var existing = Assert.Single(Get<ILexEntry>(_cache, entry).AlternateFormsOS);
        var again = Proposal(AuthorAffixProcessAllomorphComposer.Build(_cache,
            Intent(entry, 3) with { Placement = new Placement(Id(existing), null) }));
        ProposalApplier.Apply(_cache, again, ScratchDryRun.Of(_cache, again).Anchor, Tag);
        Assert.Equal(2, Get<ILexEntry>(_cache, entry).AlternateFormsOS.Count);
    }

    [Fact]
    public void ParserAcceptsOnlyTheClosedShape()
    {
        using var stream = typeof(CanonicalId).Assembly.GetManifestResourceStream(
            "SIL.Motif.Contract.Schemas.author-affix-process-allomorph.vectors.json");
        using var vectors = JsonDocument.Parse(stream!);
        using var schemaStream = typeof(CanonicalId).Assembly.GetManifestResourceStream(
            "SIL.Motif.Contract.Schemas.author-affix-process-allomorph.schema.json");
        using var schema = JsonDocument.Parse(schemaStream!);
        foreach (var vector in vectors.RootElement.GetProperty("vectors").EnumerateArray())
        {
            var input = vector.GetProperty("intent");
            var exception = Record.Exception(() => AuthorAffixProcessAllomorphIntentParser.Parse(input));
            if (vector.GetProperty("accept").GetBoolean()) Assert.Null(exception);
            else Assert.IsType<ContractParseException>(exception);
            if (vector.TryGetProperty("schemaAccept", out var schemaAccept))
                Assert.True(schemaAccept.GetBoolean() ==
                    (SampleJsonSchemaValidator.Validate(input, schema.RootElement).Count == 0), vector.GetProperty("name").GetString());
        }
    }

    private void Refused(AuthorAffixProcessAllomorphIntent intent, CanonicalId entry, string reason)
    {
        var failure = Assert.Throws<InvalidOperationException>(() => AuthorAffixProcessAllomorphComposer.Build(_cache, intent));
        Assert.Contains(reason, failure.Message, StringComparison.Ordinal);
        Assert.Contains(entry.Value, failure.Message, StringComparison.Ordinal);
    }

    // Three distinct patterns on one entry; none repeats the entry's own lexeme-form pattern.
    private AuthorAffixProcessAllomorphIntent Intent(CanonicalId entry, int variant = 1) => variant switch
    {
        1 => new(entry, [new("c", Phoneme: Id(_p)), new("rest", Remaining: true)],
            [new(Insert: [Id(_m), Id(_a), Id(_n)]), new(Copy: "rest")]),
        2 => new(entry, [new("c", NaturalClass: Id(_stops)), new("rest", Remaining: true)],
            [new(Insert: [Id(_n)]), new(Copy: "rest")]),
        _ => new(entry, [new("c", Phoneme: Id(_n)), new("rest", Remaining: true)],
            [new(Insert: [Id(_m)]), new(Copy: "rest")]),
    };

    private CanonicalId ProcessEntry()
    {
        var proposal = ApplyOps(_cache, AuthorAffixProcessComposer.Build(_cache, new AuthorAffixProcessIntent(
            MorphType(MoMorphTypeTags.kguidMorphPrefix), "PAN", Anal(_cache),
            new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, _category),
            [new("a", Phoneme: Id(_a)), new("rest", Remaining: true)],
            [new(Insert: [Id(_p), Id(_a)]), new(Copy: "a"), new(Copy: "rest")])));
        return Created(proposal, LexDbEntriesOperationKinds.Create);
    }

    private CanonicalId AddAlternate(CanonicalId entry, AuthorAffixProcessAllomorphIntent intent)
    {
        var proposal = Proposal(AuthorAffixProcessAllomorphComposer.Build(_cache, intent));
        ProposalApplier.Apply(_cache, proposal, ScratchDryRun.Of(_cache, proposal).Anchor, Tag);
        return Created(proposal, LexEntryAlternateFormsCreateOperationKinds.CreateAlternateForms);
    }

    private void Setup(Action action) => UndoableUnitOfWorkHelper.Do("setup", "setup",
        _cache.ServiceLocator.GetInstance<IActionHandler>(), action);

    private IPhPhoneme Phone(string representation)
    {
        IPhPhoneme phone = null!;
        Setup(() =>
        {
            var set = _cache.LangProject.PhonologicalDataOA.PhonemeSetsOS.FirstOrDefault();
            if (set is null)
            {
                set = _cache.ServiceLocator.GetInstance<IPhPhonemeSetFactory>().Create();
                _cache.LangProject.PhonologicalDataOA.PhonemeSetsOS.Add(set);
            }
            phone = _cache.ServiceLocator.GetInstance<IPhPhonemeFactory>().Create();
            set.PhonemesOC.Add(phone);
            phone.CodesOS.Clear();
            var code = _cache.ServiceLocator.GetInstance<IPhCodeFactory>().Create();
            phone.CodesOS.Add(code);
            code.Representation.set_String(_cache.DefaultVernWs, representation);
        });
        return phone;
    }
}

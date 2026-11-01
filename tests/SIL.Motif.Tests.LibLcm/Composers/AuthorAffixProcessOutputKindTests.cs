using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Execution;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Runner.Snapshotting;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Composers;

/// <summary>Modify-from-input and insert-by-class outputs of an affix process: lowering, refusals, drift and reopen.</summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class AuthorAffixProcessOutputKindTests : IDisposable
{
    private readonly LcmCache _cache;
    private readonly IPartOfSpeech _category;
    private readonly IPhPhoneme _voiceless;
    private readonly IPhPhoneme _voiced;
    private readonly IPhNCSegments _segments;
    private readonly IPhNCFeatures _voicedClass;
    private readonly IFsClosedValue _classValue;
    private readonly IFsSymFeatVal _otherValue;

    public AuthorAffixProcessOutputKindTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        IPartOfSpeech category = null!;
        IPhNCSegments segments = null!;
        IPhNCFeatures features = null!;
        IFsClosedValue classValue = null!;
        IFsSymFeatVal other = null!;
        Setup(() =>
        {
            category = _cache.ServiceLocator.GetInstance<IPartOfSpeechFactory>().Create();
            _cache.LangProject.PartsOfSpeechOA.PossibilitiesOS.Add(category);
            category.Name.set_String(_cache.DefaultAnalWs, "Synthetic category");
            segments = _cache.ServiceLocator.GetInstance<IPhNCSegmentsFactory>().Create();
            _cache.LangProject.PhonologicalDataOA.NaturalClassesOS.Add(segments);
            segments.Abbreviation.set_String(_cache.DefaultAnalWs, "Seg");
            var feature = _cache.ServiceLocator.GetInstance<IFsClosedFeatureFactory>().Create();
            _cache.LangProject.PhFeatureSystemOA.FeaturesOC.Add(feature);
            var value = _cache.ServiceLocator.GetInstance<IFsSymFeatValFactory>().Create();
            feature.ValuesOC.Add(value);
            other = _cache.ServiceLocator.GetInstance<IFsSymFeatValFactory>().Create();
            feature.ValuesOC.Add(other);
            features = _cache.ServiceLocator.GetInstance<IPhNCFeaturesFactory>().Create();
            _cache.LangProject.PhonologicalDataOA.NaturalClassesOS.Add(features);
            features.Abbreviation.set_String(_cache.DefaultAnalWs, "Feat");
            var structure = _cache.ServiceLocator.GetInstance<IFsFeatStrucFactory>().Create();
            features.FeaturesOA = structure;
            classValue = _cache.ServiceLocator.GetInstance<IFsClosedValueFactory>().Create();
            structure.FeatureSpecsOC.Add(classValue);
            classValue.FeatureRA = feature;
            classValue.ValueRA = value;
        });
        _category = category;
        _segments = segments;
        _voicedClass = features;
        _classValue = classValue;
        _otherValue = other;
        _voiceless = Phone("p");
        _voiced = Phone("b");
        Setup(() => _segments.SegmentsRC.Add(_voiced));
    }

    public void Dispose() => _cache.Dispose();

    [Fact]
    public void ModifyAndInsertClassLowerToTheirMappingsAndSurviveDryRunApplyAndReopen()
    {
        var before = SoundSystemSnapshots.Read(_cache);
        var operations = AuthorAffixProcessComposer.Build(_cache, Intent(
            [new("c", NaturalClass: Id(_segments)), new("rest", Remaining: true)],
            [new(Modify: "c", ModifyFeatures: Id(_voicedClass)), new(InsertClass: Id(_voicedClass)),
             new(InsertClass: Id(_segments)), new(Copy: "rest")]));
        Assert.Null(ProposalOperationSlotValidator.FindConflict(operations));
        var proposal = Proposal(operations);
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        Assert.Empty(SoundSystemSnapshots.Compare(before, SoundSystemSnapshots.Read(_cache)));
        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "affix-output-kind-tests");

        var process = (IMoAffixProcess)Repository.GetObject(Created(operations, LexEntryLexemeFormOperationKinds.CreateLexemeForm).ToGuid());
        Assert.Equal(["MoModifyFromInput", "MoInsertNC", "MoInsertNC", "MoCopyFromInput"], process.OutputOS.Select(part => part.ClassName));
        var modify = (IMoModifyFromInput)process.OutputOS[0];
        Assert.Equal(process.InputOS[0].Guid, modify.ContentRA!.Guid);
        Assert.Equal(_voicedClass.Guid, modify.ModificationRA!.Guid);
        Assert.Equal(_voicedClass.Guid, ((IMoInsertNC)process.OutputOS[1]).ContentRA!.Guid);
        Assert.Equal(_segments.Guid, ((IMoInsertNC)process.OutputOS[2]).ContentRA!.Guid);
        var after = SoundSystemSnapshots.Read(_cache);
        new FwDataProjectLoader().Save(_cache);
        using var reopened = new FwDataProjectLoader().LoadScratchCache(_cache.ProjectId.Path);
        Assert.Empty(SoundSystemSnapshots.Compare(after, SoundSystemSnapshots.Read(reopened)));
    }

    [Fact]
    public void ARealizationThatOnlyModifiesOrOnlyInsertsAClassIsNotIdentityOnly()
    {
        AuthorAffixProcessComposer.Build(_cache, Intent([new("c", Phoneme: Id(_voiceless)), new("rest", Remaining: true)],
            [new(Modify: "c", ModifyFeatures: Id(_voicedClass)), new(Copy: "rest")]));
        AuthorAffixProcessComposer.Build(_cache, Intent([new("rest", Remaining: true)],
            [new(InsertClass: Id(_segments)), new(Copy: "rest")]));
    }

    [Fact]
    public void AModifyOfTheRemainingStemIsAllowed()
    {
        AuthorAffixProcessComposer.Build(_cache, Intent(
            [new("c", Phoneme: Id(_voiceless)), new("rest", Remaining: true)],
            [new(Copy: "c"), new(Modify: "rest", ModifyFeatures: Id(_voicedClass))]));
    }

    [Theory]
    [InlineData("segment-class")]
    [InlineData("unknown-part")]
    [InlineData("no-features")]
    [InlineData("two-kinds")]
    [InlineData("insert-class-unusable")]
    public void UnusableModificationsAndClassInsertionsAreRefused(string problem)
    {
        var parts = new AffixProcessInputPart[] { new("c", Phoneme: Id(_voiceless)), new("rest", Remaining: true) };
        AffixProcessOutputStep step = problem switch
        {
            "segment-class" => new(Modify: "c", ModifyFeatures: Id(_segments)),
            "unknown-part" => new(Modify: "x", ModifyFeatures: Id(_voicedClass)),
            "no-features" => new(Modify: "c"),
            "two-kinds" => new(Copy: "c", InsertClass: Id(_segments)),
            _ => new(InsertClass: Id(_category)),
        };
        Assert.Throws<InvalidOperationException>(() =>
            AuthorAffixProcessComposer.Build(_cache, Intent(parts, [step, new(Copy: "rest")])));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChangedFeatureClassValueRefusesApplyForModifyAndInsertClass(bool modify)
    {
        var output = modify ? new AffixProcessOutputStep(Modify: "c", ModifyFeatures: Id(_voicedClass))
            : new AffixProcessOutputStep(InsertClass: Id(_voicedClass));
        var proposal = Proposal(AuthorAffixProcessComposer.Build(_cache, Intent(
            [new("c", Phoneme: Id(_voiceless)), new("rest", Remaining: true)], [output, new(Copy: "c"), new(Copy: "rest")])));
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        Setup(() => _classValue.ValueRA = _otherValue);
        var failure = Assert.Throws<PreconditionRefusal>(() =>
            ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "affix-output-kind-tests"));
        Assert.Contains("Footprint drift", failure.Message, StringComparison.Ordinal);
    }

    private AuthorAffixProcessIntent Intent(IReadOnlyList<AffixProcessInputPart> input, IReadOnlyList<AffixProcessOutputStep> output) =>
        new(CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphPrefix), "MUT", _cache.WritingSystemFactory.GetStrFromWs(_cache.DefaultAnalWs),
            new(LexicalEntryMsaKind.Unclassified, Id(_category)), input, output);

    private void Setup(Action action) => UndoableUnitOfWorkHelper.Do("setup", "setup", _cache.ServiceLocator.GetInstance<IActionHandler>(), action);
    private ICmObjectRepository Repository => _cache.ServiceLocator.GetInstance<ICmObjectRepository>();
    private static CanonicalId Id(ICmObject value) => CanonicalId.FromGuid(value.Guid);
    private static CanonicalId Created(IReadOnlyList<OperationEnvelope> operations, string kind) =>
        Assert.Single(operations, operation => operation.Kind == kind).EntityId!.Value;
    private static Proposal Proposal(IReadOnlyList<OperationEnvelope> operations) => new(
        new Dictionary<string, string> { ["lexical"] = "1.0", ["grammar"] = "1.0", ["lists"] = "1.0" }, CanonicalId.Mint(), null, operations);

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

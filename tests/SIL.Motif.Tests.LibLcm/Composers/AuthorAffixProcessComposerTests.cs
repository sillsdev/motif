using SIL.Motif.Runner.Execution;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.AppliedLog;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Runner.Snapshotting;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Composers;

[Collection(LcmCacheParallelCollections.Group3)]
public sealed class AuthorAffixProcessComposerTests : IDisposable
{
    private readonly LcmCache _cache;
    private readonly IPartOfSpeech _category;
    private readonly IPhPhoneme _onset;
    private readonly IPhPhoneme _vowel;
    private readonly IPhPhoneme _insert;
    private readonly IPhPhoneme _unrelated;
    private readonly IPhNCSegments _class;

    public AuthorAffixProcessComposerTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        IPartOfSpeech category = null!;
        IPhNCSegments naturalClass = null!;
        Setup(() =>
        {
            category = _cache.ServiceLocator.GetInstance<IPartOfSpeechFactory>().Create();
            _cache.LangProject.PartsOfSpeechOA.PossibilitiesOS.Add(category);
            category.Name.set_String(_cache.DefaultAnalWs, "Synthetic category");
            naturalClass = _cache.ServiceLocator.GetInstance<IPhNCSegmentsFactory>().Create();
            _cache.LangProject.PhonologicalDataOA.NaturalClassesOS.Add(naturalClass);
            naturalClass.Abbreviation.set_String(_cache.DefaultAnalWs, "CoreC");
        });
        _category = category;
        _class = naturalClass;
        _onset = Phone("k");
        _vowel = Phone("a");
        _insert = Phone("i");
        _unrelated = Phone("z");
        Setup(() => _class.SegmentsRC.Add(_onset));
    }

    public void Dispose() => _cache.Dispose();

    [Fact]
    public void FlatInfixAndCvCopy_DryRunApplyReopen_PreserveCompleteGraphAndRepeatedInsertOrder()
    {
        var before = SoundSystemSnapshots.Read(_cache);
        var operations = AuthorAffixProcessComposer.Build(_cache, Intent());
        Assert.Null(ProposalOperationSlotValidator.FindConflict(operations));
        var proposal = Proposal(operations);
        var shuffled = new Proposal(proposal.ContractVersions, proposal.ProposalId, proposal.Requires,
            operations.Reverse().ToArray());
        var forwardDryRun = ScratchDryRun.Of(_cache, proposal);
        var dryRun = ScratchDryRun.Of(_cache, shuffled);
        Assert.Equal(forwardDryRun.EffectDigest, dryRun.EffectDigest);
        Assert.Equal(forwardDryRun.Anchor.FootprintDigest, dryRun.Anchor.FootprintDigest);
        Assert.Empty(SoundSystemSnapshots.Compare(before, SoundSystemSnapshots.Read(_cache)));
        Assert.DoesNotContain(dryRun.ExpectedEffects, effect => effect.Field.StartsWith("prerequisite/", StringComparison.Ordinal));
        var processId = Created(operations, LexEntryLexemeFormOperationKinds.CreateLexemeForm);
        Assert.False(Repository.IsValidObjectId(processId.ToGuid()));
        Assert.Contains(dryRun.ExpectedEffects, effect => effect.CanonicalId == processId && effect.Field == "class");
        Assert.Contains(dryRun.ExpectedEffects, effect => effect.CanonicalId == processId && effect.Field == "owner");
        var receipt = ProposalApplier.Apply(_cache, shuffled, dryRun.Anchor, "affix-core-tests");
        Assert.Equal(dryRun.EffectDigest, receipt.EffectDigest);
        var process = Assert.IsAssignableFrom<IMoAffixProcess>(Repository.GetObject(processId.ToGuid()));
        Assert.False(process.IsAbstract);
        Assert.Equal(0, process.Form.StringCount);
        Assert.Equal(new[] { "PhSimpleContextNC", "PhSimpleContextSeg", "PhVariable" }, process.InputOS.Select(part => part.ClassName));
        Assert.Equal(new[] { "MoCopyFromInput", "MoInsertPhones", "MoInsertPhones", "MoCopyFromInput",
            "MoCopyFromInput", "MoCopyFromInput", "MoCopyFromInput" }, process.OutputOS.Select(mapping => mapping.ClassName));
        Assert.Equal(7, process.OutputOS.Select(mapping => mapping.Guid).Distinct().Count());
        foreach (var insert in process.OutputOS.OfType<IMoInsertPhones>())
            Assert.Equal(_insert.Guid, Assert.Single(insert.ContentRS).Guid);
        var copies = process.OutputOS.OfType<IMoCopyFromInput>().ToArray();
        Assert.Equal(new[] { 0, 1, 0, 1, 2 }, copies.Select(copy => copy.ContentRA!.IndexInOwner));
        var entry = Assert.IsAssignableFrom<ILexEntry>(process.Owner);
        Assert.Equal("COPY", Assert.Single(entry.SensesOS).Gloss.get_String(_cache.DefaultAnalWs).Text);
        Assert.Equal("Synthetic process", Assert.Single(entry.SensesOS).Definition.get_String(_cache.DefaultAnalWs).Text);
        var after = SoundSystemSnapshots.Read(_cache);
        Assert.Contains(SoundSystemSnapshots.Compare(before, after), effect => effect.Field == SnapshotFields.MoAffixProcessInput);
        Assert.Contains(SoundSystemSnapshots.Compare(before, after), effect => effect.Field == SnapshotFields.MoAffixProcessOutput);
        new FwDataProjectLoader().Save(_cache);
        using var reopened = new FwDataProjectLoader().LoadScratchCache(_cache.ProjectId.Path);
        Assert.Empty(SoundSystemSnapshots.Compare(after, SoundSystemSnapshots.Read(reopened)));
    }

    [Fact]
    public void AHeadwordFormIsWrittenByApplyAndGoneAfterUndo()
    {
        var vernacular = _cache.WritingSystemFactory.GetStrFromWs(_cache.DefaultVernWs);
        var intent = Intent() with { Form = "in", FormWritingSystem = vernacular };
        var operations = AuthorAffixProcessComposer.Build(_cache, intent);
        Assert.Contains(operations, operation => operation.Kind == MoFormFormOperationKinds.SetForm);
        var proposal = Proposal(operations);
        var dryRun = ScratchDryRun.Of(_cache, proposal);

        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "affix-headword-tests");

        var process = Process(proposal);
        Assert.Equal("in", process.Form.get_String(_cache.DefaultVernWs).Text);
        var entry = Assert.IsAssignableFrom<ILexEntry>(process.Owner);
        Assert.Contains("in", entry.HeadWord.Text);
        var processId = process.Guid;

        _cache.ActionHandlerAccessor.Undo();

        Assert.False(Repository.IsValidObjectId(processId));
    }

    [Fact]
    public void WholeStemCopy_IsAnEmptyFormWithTwoDistinctCopyMappings()
    {
        var intent = Intent() with { Input = [new("stem", Remaining: true)], Output = [new(Copy: "stem"), new(Copy: "stem")] };
        var proposal = Proposal(AuthorAffixProcessComposer.Build(_cache, intent));
        ProposalApplier.Apply(_cache, proposal, ScratchDryRun.Of(_cache, proposal).Anchor, "affix-core-tests");
        var process = Process(proposal);
        Assert.IsAssignableFrom<IPhVariable>(Assert.Single(process.InputOS));
        Assert.Equal(2, process.OutputOS.Count);
        Assert.All(process.OutputOS.OfType<IMoCopyFromInput>(), copy => Assert.Equal(process.InputOS[0].Guid, copy.ContentRA?.Guid));
    }

    [Fact]
    public void OutputThatOmitsAnInputDeletesItAndRoundTripsThroughApplyAndReopen()
    {
        var intent = Intent() with
        {
            Input = [new("onset", Phoneme: Id(_onset)), new("rest", Remaining: true)],
            Output = [new(Insert: [Id(_insert)]), new(Insert: [Id(_vowel)]), new(Copy: "rest")],
        };
        var proposal = Proposal(AuthorAffixProcessComposer.Build(_cache, intent));
        ProposalApplier.Apply(_cache, proposal, ScratchDryRun.Of(_cache, proposal).Anchor, "affix-core-tests");
        var process = Process(proposal);
        Assert.Equal(new[] { "PhSimpleContextSeg", "PhVariable" }, process.InputOS.Select(part => part.ClassName));
        Assert.Equal(new[] { "MoInsertPhones", "MoInsertPhones", "MoCopyFromInput" }, process.OutputOS.Select(item => item.ClassName));
        Assert.Equal(process.InputOS[1].Guid, Assert.Single(process.OutputOS.OfType<IMoCopyFromInput>()).ContentRA!.Guid);
        Assert.DoesNotContain(process.OutputOS.OfType<IMoCopyFromInput>(), copy => copy.ContentRA!.Guid == process.InputOS[0].Guid);
        var after = SoundSystemSnapshots.Read(_cache);
        new FwDataProjectLoader().Save(_cache);
        using var reopened = new FwDataProjectLoader().LoadScratchCache(_cache.ProjectId.Path);
        Assert.Empty(SoundSystemSnapshots.Compare(after, SoundSystemSnapshots.Read(reopened)));
    }

    [Fact]
    public void OutputThatDeletesTheStemOrEverythingIsRefusedAtBuildAndAtApply()
    {
        var good = Intent() with
        {
            Input = [new("onset", Phoneme: Id(_onset)), new("rest", Remaining: true)],
            Output = [new(Insert: [Id(_insert)]), new(Copy: "rest")],
        };
        Assert.NotEmpty(AuthorAffixProcessComposer.Build(_cache, good));
        var dropsStem = good with { Output = [new(Copy: "onset"), new(Insert: [Id(_insert)])] };
        var copiesNothing = good with { Output = [new(Insert: [Id(_insert)])] };
        foreach (var bad in new[] { dropsStem, copiesNothing })
            Assert.Throws<InvalidOperationException>(() => AuthorAffixProcessComposer.Build(_cache, bad));
        var operations = AuthorAffixProcessComposer.Build(_cache, good);
        var onset = operations.First(operation => operation.Kind == MoAffixProcessInputOperationKinds.Create).EntityId!.Value;
        var redirected = operations.Select(operation => operation.Kind == MoCopyFromInputContentOperationKinds.SetContent
            ? ReplaceAfter(operation, new { @ref = onset.Value }) : operation).ToArray();
        var failure = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache, Proposal(redirected)));
        Assert.Contains("remaining-stem", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("class-membership")]
    [InlineData("insert-code")]
    [InlineData("input-code")]
    public void ChangedReferencedLinguisticPrerequisiteRefusesApply(string change)
    {
        var proposal = Proposal(AuthorAffixProcessComposer.Build(_cache, Intent()));
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        Setup(() =>
        {
            if (change == "class-membership") _class.SegmentsRC.Add(_unrelated);
            if (change == "insert-code") _insert.CodesOS[0].Representation.set_String(_cache.DefaultVernWs, "j");
            if (change == "input-code") _onset.CodesOS[0].Representation.set_String(_cache.DefaultVernWs, "q");
        });
        var failure = Assert.Throws<PreconditionRefusal>(() => ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "affix-core-tests"));
        Assert.Contains("Footprint drift", failure.Message, StringComparison.Ordinal);
        Assert.False(Repository.IsValidObjectId(Created(proposal.Operations, LexDbEntriesOperationKinds.Create).ToGuid()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FeatureClassAndCandidatePhonemeFeatureDriftRefuseApply(bool candidateChanged)
    {
        IPhNCFeatures naturalClass = null!;
        IFsClosedValue classValue = null!;
        IFsClosedValue phoneValue = null!;
        IFsSymFeatVal alternateValue = null!;
        Setup(() =>
        {
            var feature = _cache.ServiceLocator.GetInstance<IFsClosedFeatureFactory>().Create();
            _cache.LangProject.PhFeatureSystemOA.FeaturesOC.Add(feature);
            var value = _cache.ServiceLocator.GetInstance<IFsSymFeatValFactory>().Create();
            feature.ValuesOC.Add(value);
            alternateValue = _cache.ServiceLocator.GetInstance<IFsSymFeatValFactory>().Create();
            feature.ValuesOC.Add(alternateValue);
            naturalClass = _cache.ServiceLocator.GetInstance<IPhNCFeaturesFactory>().Create();
            _cache.LangProject.PhonologicalDataOA.NaturalClassesOS.Add(naturalClass);
            var structure = _cache.ServiceLocator.GetInstance<IFsFeatStrucFactory>().Create();
            naturalClass.FeaturesOA = structure;
            classValue = _cache.ServiceLocator.GetInstance<IFsClosedValueFactory>().Create();
            structure.FeatureSpecsOC.Add(classValue);
            classValue.FeatureRA = feature;
            classValue.ValueRA = value;
            var phonemeStructure = _cache.ServiceLocator.GetInstance<IFsFeatStrucFactory>().Create();
            _onset.FeaturesOA = phonemeStructure;
            phoneValue = _cache.ServiceLocator.GetInstance<IFsClosedValueFactory>().Create();
            phonemeStructure.FeatureSpecsOC.Add(phoneValue);
            phoneValue.FeatureRA = feature;
            phoneValue.ValueRA = value;
        });
        var intent = Intent() with { Input = [new("c", NaturalClass: Id(naturalClass)), Intent().Input[1], Intent().Input[2]] };
        var proposal = Proposal(AuthorAffixProcessComposer.Build(_cache, intent));
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        Setup(() => (candidateChanged ? phoneValue : classValue).ValueRA = alternateValue);
        var exception = Assert.Throws<PreconditionRefusal>(() => ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "affix-core-tests"));
        Assert.Contains("Footprint drift", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnrelatedPhoneAndClassEditsDoNotDriftTheProcess()
    {
        var proposal = Proposal(AuthorAffixProcessComposer.Build(_cache, Intent()));
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        Setup(() =>
        {
            _unrelated.CodesOS[0].Representation.set_String(_cache.DefaultVernWs, "y");
            var otherClass = _cache.ServiceLocator.GetInstance<IPhNCSegmentsFactory>().Create();
            _cache.LangProject.PhonologicalDataOA.NaturalClassesOS.Add(otherClass);
            otherClass.SegmentsRC.Add(_unrelated);
        });
        Assert.Equal(dryRun.Anchor.FootprintDigest, FootprintProbe.ComputeCurrentFootprintDigest(_cache, proposal));
        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "affix-core-tests");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OperationAndFinalValidationFailuresRollBackEveryMintedObjectAndAppliedLog(bool finalValidation)
    {
        var before = SoundSystemSnapshots.Read(_cache);
        var proposal = Proposal(AuthorAffixProcessComposer.Build(_cache, Intent()));
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        Assert.Throws<InvalidOperationException>(() => ProposalApplier.Apply(_cache, proposal, dryRun.Anchor,
            "affix-core-tests", string.Empty, afterOperation: (index, operation) =>
            {
                if (!finalValidation && operation.Kind == MoAffixProcessOutputOperationKinds.Create)
                    throw new InvalidOperationException("Injected operation failure");
                if (finalValidation && index == proposal.Operations.Count)
                    Process(proposal).OutputOS.OfType<IMoCopyFromInput>().First().ContentRA = null;
            }));
        foreach (var id in proposal.Operations.Where(operation => operation.EntityId is not null).Select(operation => operation.EntityId!.Value))
            Assert.False(Repository.IsValidObjectId(id.ToGuid()));
        Assert.DoesNotContain(ProjectAppliedLog.ReadAll(_cache), record => record.ProposalId == proposal.ProposalId.ToGuid());
        Assert.Empty(SoundSystemSnapshots.Compare(before, SoundSystemSnapshots.Read(_cache)));
    }

    [Fact]
    public void LayerZeroCrossProcessCopyAndDestructiveOutputAreRefused()
    {
        var first = Proposal(AuthorAffixProcessComposer.Build(_cache, Intent()));
        ProposalApplier.Apply(_cache, first, ScratchDryRun.Of(_cache, first).Anchor, "affix-core-tests");
        var foreign = CanonicalId.FromGuid(Process(first).InputOS[0].Guid);
        var operations = AuthorAffixProcessComposer.Build(_cache, Intent());
        var changed = false;
        var bad = operations.Select(operation =>
        {
            if (changed || operation.Kind != MoCopyFromInputContentOperationKinds.SetContent) return operation;
            changed = true;
            return ReplaceAfter(operation, new { @ref = foreign.Value });
        }).ToArray();
        var failure = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache, Proposal(bad)));
        Assert.Contains("same process", failure.Message, StringComparison.Ordinal);
        var omitted = operations.Where(operation => operation.Kind != MoCopyFromInputContentOperationKinds.SetContent).ToArray();
        Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache, Proposal(omitted)));
    }

    [Fact]
    public void TypedBuildRefusesInvalidShapeMetadataAndExternalReferences()
    {
        var good = Intent();
        var bad = new AuthorAffixProcessIntent[]
        {
            good with { Msa = new(LexicalEntryMsaKind.Stem, Id(_category)) },
            good with { MorphType = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphStem) },
            good with { AnalysisWritingSystem = "qaa-x-absent" },
            good with { Input = [new("stem", Remaining: true)], Output = [new(Copy: "stem")] },
            good with { Input = [new("stem", Remaining: true), new("more", Remaining: true)] },
            good with { Input = [new("c", Phoneme: Id(_category)), good.Input[1], good.Input[2]] },
            good with { Output = [new(Insert: []), new(Copy: "c"), new(Copy: "v"), new(Copy: "rest")] },
            good with { Output = [new(Copy: "unknown")] },
            good with { Output = [new(Copy: "c", Insert: [Id(_insert)])] },
            good with { Input = [new("c", NaturalClass: Id(_category)), good.Input[1], good.Input[2]] },
            good with { Gloss = " " },
            good with { Msa = new(LexicalEntryMsaKind.Unclassified, Id(_category), Slots: []) },
        };
        foreach (var intent in bad)
            Assert.Throws<InvalidOperationException>(() => AuthorAffixProcessComposer.Build(_cache, intent));
        Setup(() => _class.SegmentsRC.Clear());
        Assert.Throws<InvalidOperationException>(() => AuthorAffixProcessComposer.Build(_cache, good));
    }

    [Theory]
    [InlineData(17, 2, 1)]
    [InlineData(1, 65, 1)]
    [InlineData(1, 2, 65)]
    [InlineData(1, 6, 64)]
    public void DirectTypedBuildEnforcesAllFourBounds(int inputs, int outputs, int phones)
    {
        var intent = Intent() with
        {
            Input = Enumerable.Range(0, inputs).Select(index => new AffixProcessInputPart("p" + index, Phoneme: Id(_onset))).ToArray(),
            Output = new AffixProcessOutputStep[] { new(Copy: "p0") }.Concat(Enumerable.Range(1, outputs - 1)
                .Select(_ => new AffixProcessOutputStep(Insert: Enumerable.Repeat(Id(_insert), phones).ToArray()))).ToArray(),
        };
        Assert.Throws<InvalidOperationException>(() => AuthorAffixProcessComposer.Build(_cache, intent));
    }

    [Fact]
    public void TypedBuildAcceptsExactLimitsIncluding256ExpandedMappings()
    {
        var input = Enumerable.Range(0, 16).Select(index => new AffixProcessInputPart("p" + index, Phoneme: Id(_onset))).ToArray();
        var output = input.SelectMany(part => new[] { new AffixProcessOutputStep(Copy: part.Name),
            new AffixProcessOutputStep(Copy: part.Name) }).Concat(new[]
        {
            new AffixProcessOutputStep(Insert: Enumerable.Repeat(Id(_insert), 64).ToArray()),
            new AffixProcessOutputStep(Insert: Enumerable.Repeat(Id(_insert), 64).ToArray()),
            new AffixProcessOutputStep(Insert: Enumerable.Repeat(Id(_insert), 64).ToArray()),
            new AffixProcessOutputStep(Insert: Enumerable.Repeat(Id(_insert), 32).ToArray()),
        }).ToArray();
        var operations = AuthorAffixProcessComposer.Build(_cache, Intent() with { Input = input, Output = output });
        Assert.Equal(16, operations.Count(operation => operation.Kind == MoAffixProcessInputOperationKinds.Create));
        Assert.Equal(256, operations.Count(operation => operation.Kind == MoAffixProcessOutputOperationKinds.Create));
        var sixtyFour = AuthorAffixProcessComposer.Build(_cache, Intent() with
        {
            Input = [new("stem", Remaining: true)],
            Output = new[] { new AffixProcessOutputStep(Copy: "stem"), new AffixProcessOutputStep(Copy: "stem") }
                .Concat(Enumerable.Repeat(new AffixProcessOutputStep(Insert: [Id(_insert)]), 62)).ToArray(),
        });
        Assert.Equal(64, sixtyFour.Count(operation => operation.Kind == MoAffixProcessOutputOperationKinds.Create));
    }

    [Fact]
    public void TypedBuildRefusesForeignSetPhonemesAndUnusableInsertionCodes()
    {
        var foreign = Phone("x");
        Setup(() =>
        {
            var otherSet = _cache.ServiceLocator.GetInstance<IPhPhonemeSetFactory>().Create();
            _cache.LangProject.PhonologicalDataOA.PhonemeSetsOS.Add(otherSet);
            otherSet.PhonemesOC.Add(foreign);
        });
        var good = Intent();
        var foreignInput = good with { Input = [new("c", Phoneme: Id(foreign)), good.Input[1], good.Input[2]] };
        var failure = Assert.Throws<InvalidOperationException>(() => AuthorAffixProcessComposer.Build(_cache, foreignInput));
        Assert.Contains("first phoneme set", failure.Message, StringComparison.Ordinal);
        Setup(() => _insert.CodesOS[0].Representation.set_String(_cache.DefaultVernWs, " "));
        var emptyCode = Assert.Throws<InvalidOperationException>(() => AuthorAffixProcessComposer.Build(_cache, good));
        Assert.Contains("first vernacular code", emptyCode.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LayerZeroInsertionWithTwoPhonemesIsRefusedByFinalValidation()
    {
        var operations = AuthorAffixProcessComposer.Build(_cache, Intent());
        var insertCreate = operations.First(operation => operation.Kind == MoAffixProcessOutputOperationKinds.Create &&
            operation.After!.Value.GetProperty("class").GetString() == "MoInsertPhones");
        var invalid = operations.Concat([new OperationEnvelope(CanonicalId.Mint(), MoInsertPhonesContentOperationKinds.AddRefContent,
            target: insertCreate.EntityId, after: JsonSerializer.SerializeToElement(new { member = Id(_unrelated).Value }),
            dependsOn: [new OperationDependency(insertCreate.OperationId)])]).ToArray();
        var failure = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache, Proposal(invalid)));
        Assert.Contains("insert one phoneme", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExistingNestedProcessDoesNotBlockAnUnrelatedGlossEdit()
    {
        ILexSense sense = null!;
        Setup(() =>
        {
            var entry = _cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create();
            var process = _cache.ServiceLocator.GetInstance<IMoAffixProcessFactory>().Create();
            entry.LexemeFormOA = process;
            process.OutputOS.Clear();
            process.InputOS.Clear();
            var sequence = _cache.ServiceLocator.GetInstance<IPhSequenceContextFactory>().Create();
            process.InputOS.Add(sequence);
            sense = _cache.ServiceLocator.GetInstance<ILexSenseFactory>().Create();
            entry.SensesOS.Add(sense);
            sense.Gloss.set_String(_cache.DefaultAnalWs, "before");
        });
        var proposal = Proposal([new OperationEnvelope(CanonicalId.Mint(), LexicalSenseOperationKinds.SetGloss,
            target: Id(sense), after: JsonSerializer.SerializeToElement(new
            { ws = _cache.WritingSystemFactory.GetStrFromWs(_cache.DefaultAnalWs), text = "after" }))]);
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "affix-core-tests");
        Assert.Equal("after", sense.Gloss.get_String(_cache.DefaultAnalWs).Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinaryFormEditsBesideNestedProcessAlternateRemainPermitted(bool clear)
    {
        IMoAffixAllomorph form = null!;
        IMoAffixProcess alternate = null!;
        Setup(() =>
        {
            var entry = _cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create();
            form = _cache.ServiceLocator.GetInstance<IMoAffixAllomorphFactory>().Create();
            entry.LexemeFormOA = form;
            form.Form.set_String(_cache.DefaultVernWs, "before");
            alternate = AddNestedAlternate(entry);
        });
        var ws = _cache.WritingSystemFactory.GetStrFromWs(_cache.DefaultVernWs);
        var proposal = Proposal([new OperationEnvelope(CanonicalId.Mint(),
            clear ? MoFormFormOperationKinds.ClearForm : MoFormFormOperationKinds.SetForm,
            target: Id(form), after: clear ? JsonSerializer.SerializeToElement(new { ws }) :
                JsonSerializer.SerializeToElement(new { ws, text = "after" }))]);
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        Assert.Equal("before", form.Form.get_String(_cache.DefaultVernWs).Text);
        Setup(() => _insert.CodesOS[0].Representation.set_String(_cache.DefaultVernWs, "unrelated-change"));
        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "affix-core-tests");
        Assert.Equal(clear ? null : "after", form.Form.get_String(_cache.DefaultVernWs).Text);
        Assert.IsAssignableFrom<IPhSequenceContext>(Assert.Single(alternate.InputOS));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefaultVernacularSelectionIsBoundAcrossReopen(bool switchDefault)
    {
        var original = _cache.ServiceLocator.WritingSystems.DefaultVernacularWritingSystem;
        _cache.ServiceLocator.WritingSystemManager.GetOrSet("qaa-x-affix", out var alternate);
        Setup(() =>
        {
            _cache.ServiceLocator.WritingSystems.VernacularWritingSystems.Add(alternate);
            _cache.ServiceLocator.WritingSystems.CurrentVernacularWritingSystems.Add(alternate);
            foreach (var phone in new[] { _onset, _vowel, _insert })
                phone.CodesOS[0].Representation.set_String(alternate.Handle, "other-" + phone.CodesOS[0]
                    .Representation.get_String(original.Handle).Text);
        });
        var proposal = Proposal(AuthorAffixProcessComposer.Build(_cache, Intent()));
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        Assert.DoesNotContain(dryRun.ExpectedEffects, effect => effect.Field.StartsWith("prerequisite/", StringComparison.Ordinal));
        if (switchDefault)
            Setup(() =>
            {
                var current = _cache.ServiceLocator.WritingSystems.CurrentVernacularWritingSystems;
                current.Remove(alternate);
                current.Insert(0, alternate);
            });
        new FwDataProjectLoader().Save(_cache);
        using var reopened = new FwDataProjectLoader().LoadScratchCache(_cache.ProjectId.Path);
        Assert.Equal(switchDefault ? alternate.Id : original.Id,
            reopened.WritingSystemFactory.GetStrFromWs(reopened.DefaultVernWs));
        var inserted = (IPhPhoneme)reopened.ServiceLocator.GetInstance<ICmObjectRepository>().GetObject(_insert.Guid);
        Assert.Equal("i", inserted.CodesOS[0].Representation.get_String(original.Handle).Text);
        Assert.Equal("other-i", inserted.CodesOS[0].Representation.get_String(alternate.Handle).Text);
        if (switchDefault)
        {
            var failure = Assert.Throws<PreconditionRefusal>(() =>
                ProposalApplier.Apply(reopened, proposal, dryRun.Anchor, "affix-core-tests"));
            Assert.Contains("Footprint drift", failure.Message, StringComparison.Ordinal);
            Assert.False(reopened.ServiceLocator.GetInstance<ICmObjectRepository>().IsValidObjectId(
                Created(proposal.Operations, LexDbEntriesOperationKinds.Create).ToGuid()));
        }
        else
        {
            Assert.Equal(dryRun.Anchor.FootprintDigest, FootprintProbe.ComputeCurrentFootprintDigest(reopened, proposal));
            ProposalApplier.Apply(reopened, proposal, dryRun.Anchor, "affix-core-tests");
        }
    }

    [Theory]
    [InlineData("form")]
    [InlineData("context")]
    [InlineData("mapping")]
    public void TouchedProcessAloneIsValidatedBesideNestedAlternate(string field)
    {
        var create = Proposal(AuthorAffixProcessComposer.Build(_cache, Intent()));
        ProposalApplier.Apply(_cache, create, ScratchDryRun.Of(_cache, create).Anchor, "affix-core-tests");
        var process = Process(create);
        Setup(() => AddNestedAlternate((ILexEntry)process.Owner));
        var context = process.InputOS.OfType<IPhSimpleContextSeg>().Single();
        var copy = process.OutputOS.OfType<IMoCopyFromInput>().First();
        var operation = field switch
        {
            "form" => new OperationEnvelope(CanonicalId.Mint(), MoFormFormOperationKinds.SetForm,
                target: Id(process), after: JsonSerializer.SerializeToElement(new
                { ws = _cache.WritingSystemFactory.GetStrFromWs(_cache.DefaultVernWs), text = "x" })),
            "context" => new OperationEnvelope(CanonicalId.Mint(), PhSimpleContextSegFeatureStructureOperationKinds.SetFeatureStructure,
                target: Id(context), after: JsonSerializer.SerializeToElement(new { @ref = Id(_vowel).Value })),
            _ => new OperationEnvelope(CanonicalId.Mint(), MoCopyFromInputContentOperationKinds.SetContent,
                target: Id(copy), after: JsonSerializer.SerializeToElement(new { @ref = Id(copy.ContentRA).Value })),
        };
        var proposal = Proposal([operation]);
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        var original = copy.ContentRA;
        Assert.Throws<InvalidOperationException>(() => ProposalApplier.Apply(_cache, proposal, dryRun.Anchor,
            "affix-core-tests", string.Empty, afterOperation: (_, _) => copy.ContentRA = null));
        Assert.Equal(original.Guid, copy.ContentRA?.Guid);
        Assert.DoesNotContain(ProjectAppliedLog.ReadAll(_cache), record => record.ProposalId == proposal.ProposalId.ToGuid());
        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "affix-core-tests");
        Setup(() => copy.ContentRA = null);
        var probe = new OperationEnvelope(CanonicalId.Mint(), MoFormFormOperationKinds.SetForm,
            target: Id(process), after: JsonSerializer.SerializeToElement(new
            { ws = _cache.WritingSystemFactory.GetStrFromWs(_cache.DefaultVernWs), text = "probe" }));
        var failure = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache, Proposal([probe])));
        Assert.Contains("copy", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("gloss", "")]
    [InlineData("gloss", " \t\n")]
    [InlineData("analysisWs", "")]
    [InlineData("analysisWs", " \t\n")]
    [InlineData("definition", "")]
    [InlineData("definition", " \t\n")]
    public void TypedBuildRefusesBlankLexicalMetadata(string field, string value)
    {
        var intent = field switch
        {
            "gloss" => Intent() with { Gloss = value },
            "analysisWs" => Intent() with { AnalysisWritingSystem = value },
            _ => Intent() with { Definition = value },
        };
        Assert.Throws<InvalidOperationException>(() => AuthorAffixProcessComposer.Build(_cache, intent));
    }

    [Theory]
    [InlineData("unchanged")]
    [InlineData("referenced-code")]
    [InlineData("unrelated-code")]
    public void ExistingProcessChildCreationBindsItsPhonemePrerequisite(string change)
    {
        var create = Proposal(AuthorAffixProcessComposer.Build(_cache, Intent()));
        ProposalApplier.Apply(_cache, create, ScratchDryRun.Of(_cache, create).Anchor, "affix-core-tests");
        var process = Process(create);
        var outside = Phone("outside");
        var child = CanonicalId.Mint();
        var createId = CanonicalId.Mint();
        var proposal = Proposal([
            new OperationEnvelope(createId, MoAffixProcessOutputOperationKinds.Create,
                entityId: child, target: Id(process), after: JsonSerializer.SerializeToElement(new { @class = "MoInsertPhones" }),
                placement: new Placement(Id(process.OutputOS.Last()), null)),
            new OperationEnvelope(CanonicalId.Mint(), MoInsertPhonesContentOperationKinds.AddRefContent,
                target: child, after: JsonSerializer.SerializeToElement(new { member = Id(_unrelated).Value }),
                dependsOn: [new OperationDependency(createId)]),
        ]);
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        Assert.DoesNotContain(dryRun.ExpectedEffects, effect => effect.Field.StartsWith("prerequisite/", StringComparison.Ordinal));
        if (change != "unchanged")
            Setup(() => (change == "referenced-code" ? _unrelated : outside).CodesOS[0]
                .Representation.set_String(_cache.DefaultVernWs, "changed"));
        if (change == "referenced-code")
        {
            var failure = Assert.Throws<PreconditionRefusal>(() =>
                ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "affix-core-tests"));
            Assert.Contains("Footprint drift", failure.Message, StringComparison.Ordinal);
            Assert.False(Repository.IsValidObjectId(child.ToGuid()));
        }
        else
        {
            Assert.Equal(dryRun.Anchor.FootprintDigest, FootprintProbe.ComputeCurrentFootprintDigest(_cache, proposal));
            ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "affix-core-tests");
            Assert.Equal(child.ToGuid(), process.OutputOS.Last().Guid);
            Assert.Equal(_unrelated.Guid, Assert.Single(((IMoInsertPhones)process.OutputOS.Last()).ContentRS).Guid);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingProcessReferenceEditBindsTheNewPhoneme(bool changed)
    {
        var create = Proposal(AuthorAffixProcessComposer.Build(_cache, Intent()));
        ProposalApplier.Apply(_cache, create, ScratchDryRun.Of(_cache, create).Anchor, "affix-core-tests");
        var context = Process(create).InputOS.OfType<IPhSimpleContextSeg>().Single();
        var proposal = Proposal([new OperationEnvelope(CanonicalId.Mint(),
            PhSimpleContextSegFeatureStructureOperationKinds.SetFeatureStructure, target: Id(context),
            after: JsonSerializer.SerializeToElement(new { @ref = Id(_unrelated).Value }))]);
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        if (changed)
        {
            Setup(() => _unrelated.CodesOS[0].Representation.set_String(_cache.DefaultVernWs, "changed"));
            var failure = Assert.Throws<PreconditionRefusal>(() =>
                ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "affix-core-tests"));
            Assert.Contains("Footprint drift", failure.Message, StringComparison.Ordinal);
            Assert.Equal(_vowel.Guid, context.FeatureStructureRA.Guid);
        }
        else
        {
            ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "affix-core-tests");
            Assert.Equal(_unrelated.Guid, context.FeatureStructureRA.Guid);
        }
    }

    [Theory]
    [InlineData("unchanged")]
    [InlineData("member-code")]
    [InlineData("unrelated-code")]
    public void StagedSegmentClassBindsDeclaredExternalPhonemeMembers(string change)
    {
        var classId = CanonicalId.Mint();
        var classCreate = new OperationEnvelope(CanonicalId.Mint(), PhPhonDataNaturalClassesOperationKinds.Create,
            entityId: classId, target: Id(_cache.LangProject.PhonologicalDataOA),
            after: JsonSerializer.SerializeToElement(new { @class = "PhNCSegments" }),
            placement: new Placement(Id(_cache.LangProject.PhonologicalDataOA.NaturalClassesOS.Last()), null));
        var member = new OperationEnvelope(CanonicalId.Mint(), PhNCSegmentsSegmentsOperationKinds.AddRefSegments,
            target: classId, after: JsonSerializer.SerializeToElement(new { member = Id(_unrelated).Value }),
            dependsOn: [new OperationDependency(classCreate.OperationId)]);
        var operations = AuthorAffixProcessComposer.Build(_cache, Intent()).Select(operation =>
            operation.Kind == PhSimpleContextNCFeatureStructureOperationKinds.SetFeatureStructure
                ? new OperationEnvelope(operation.OperationId, operation.Kind, target: operation.Target,
                    after: JsonSerializer.SerializeToElement(new { @ref = classId.Value }),
                    dependsOn: operation.DependsOn.Concat([new OperationDependency(member.OperationId)]).ToArray())
                : operation).Concat([classCreate, member]).ToArray();
        var proposal = Proposal(operations);
        Assert.Null(ProposalOperationSlotValidator.FindConflict(operations));
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        Assert.False(Repository.IsValidObjectId(classId.ToGuid()));
        Assert.DoesNotContain(dryRun.ExpectedEffects, effect => effect.Field.StartsWith("prerequisite/", StringComparison.Ordinal));
        if (change != "unchanged")
            Setup(() => (change == "member-code" ? _unrelated : _onset).CodesOS[0]
                .Representation.set_String(_cache.DefaultVernWs, "changed"));
        if (change == "member-code")
        {
            var failure = Assert.Throws<PreconditionRefusal>(() =>
                ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "affix-core-tests"));
            Assert.Contains("Footprint drift", failure.Message, StringComparison.Ordinal);
            Assert.False(Repository.IsValidObjectId(classId.ToGuid()));
        }
        else
        {
            Assert.Equal(dryRun.Anchor.FootprintDigest, FootprintProbe.ComputeCurrentFootprintDigest(_cache, proposal));
            ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "affix-core-tests");
            var naturalClass = Assert.IsAssignableFrom<IPhNCSegments>(Repository.GetObject(classId.ToGuid()));
            Assert.Equal(_unrelated.Guid, Assert.Single(naturalClass.SegmentsRC).Guid);
        }
    }

    private IMoAffixProcess AddNestedAlternate(ILexEntry entry)
    {
        var alternate = _cache.ServiceLocator.GetInstance<IMoAffixProcessFactory>().Create();
        entry.AlternateFormsOS.Add(alternate);
        alternate.OutputOS.Clear();
        alternate.InputOS.Clear();
        alternate.InputOS.Add(_cache.ServiceLocator.GetInstance<IPhSequenceContextFactory>().Create());
        return alternate;
    }

    [Fact]
    public void AuthoredIdentityValueOrDeclaredOrderCannotRideAnOldAnchor()
    {
        var operations = AuthorAffixProcessComposer.Build(_cache, Intent());
        var proposal = Proposal(operations);
        var anchor = ScratchDryRun.Of(_cache, proposal).Anchor;
        foreach (var change in new[] { "identity", "value", "order" })
        {
            var index = Array.FindIndex(operations.ToArray(), operation => operation.Kind == MoAffixProcessOutputOperationKinds.Create && operation.Placement is not null &&
                operation.After!.Value.GetProperty("class").GetString() == "MoCopyFromInput");
            var selected = operations[index];
            var changed = new OperationEnvelope(selected.OperationId, selected.Kind,
                entityId: change == "identity" ? CanonicalId.Mint() : selected.EntityId, target: selected.Target,
                after: change == "value" ? JsonSerializer.SerializeToElement(new { @class = "MoInsertPhones" }) : selected.After,
                placement: change == "order" ? new Placement(null, selected.Placement!.After) : selected.Placement,
                dependsOn: selected.DependsOn);
            var rewritten = operations.ToArray();
            rewritten[index] = changed;
            Assert.Throws<PreconditionRefusal>(() => ProposalApplier.Apply(_cache,
                new Proposal(proposal.ContractVersions, proposal.ProposalId, proposal.Requires, rewritten), anchor, "affix-core-tests"));
        }
    }

    [Fact]
    public void ExistingProcessMsaOnlyChangesReceiveFinalValidationInBothEngines()
    {
        var create = Proposal(AuthorAffixProcessComposer.Build(_cache, Intent()));
        ProposalApplier.Apply(_cache, create, ScratchDryRun.Of(_cache, create).Anchor, "affix-core-tests");
        var msa = Assert.IsAssignableFrom<IMoUnclassifiedAffixMsa>(Repository.GetObject(
            Created(create.Operations, LexEntryMorphoSyntaxAnalysesOperationKinds.Create).ToGuid()));
        var clear = Proposal([new OperationEnvelope(CanonicalId.Mint(),
            MoUnclassifiedAffixMsaPartOfSpeechOperationKinds.ClearPartOfSpeech, target: Id(msa),
            after: JsonSerializer.SerializeToElement(new { }))]);
        Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache, clear));
        var set = Proposal([new OperationEnvelope(CanonicalId.Mint(),
            MoUnclassifiedAffixMsaPartOfSpeechOperationKinds.SetPartOfSpeech, target: Id(msa),
            after: JsonSerializer.SerializeToElement(new { @ref = Id(_category).Value }))]);
        var anchor = ScratchDryRun.Of(_cache, set).Anchor;
        Assert.Throws<InvalidOperationException>(() => ProposalApplier.Apply(_cache, set, anchor, "affix-core-tests",
            string.Empty, afterOperation: (_, _) => msa.PartOfSpeechRA = null));
        Assert.Equal(_category.Guid, msa.PartOfSpeechRA?.Guid);
        Assert.DoesNotContain(ProjectAppliedLog.ReadAll(_cache), record => record.ProposalId == set.ProposalId.ToGuid());
    }

    [Fact]
    public void PrerequisitesOfAnExistingProcessAreBoundWhateverPrefixSpellsItsIdentity()
    {
        var create = Proposal(AuthorAffixProcessComposer.Build(_cache, Intent()));
        ProposalApplier.Apply(_cache, create, ScratchDryRun.Of(_cache, create).Anchor, "affix-core-tests");
        var process = Process(create);
        Proposal Edit(CanonicalId target)
        {
            var input = CanonicalId.Mint();
            return Proposal([
                new OperationEnvelope(CanonicalId.Mint(), MoAffixProcessInputOperationKinds.Create, target: target,
                    entityId: input, after: JsonSerializer.SerializeToElement(new { @class = "PhSimpleContextSeg" })),
                new OperationEnvelope(CanonicalId.Mint(), PhSimpleContextSegFeatureStructureOperationKinds.SetFeatureStructure,
                    target: input, after: JsonSerializer.SerializeToElement(new { @ref = Id(_unrelated).Value })),
            ]);
        }
        foreach (var spelling in new[] { Id(process), CanonicalId.FromGuid(process.Guid, "p:") })
        {
            var proposal = Edit(spelling);
            var before = FootprintProbe.ComputeCurrentFootprintDigest(_cache, proposal);
            Setup(() => _unrelated.CodesOS[0].Representation.set_String(_cache.DefaultVernWs, "zz" + spelling.Prefix));
            Assert.NotEqual(before, FootprintProbe.ComputeCurrentFootprintDigest(_cache, proposal));
        }
    }

    [Fact]
    public void AMissingReferencedPhonemeIsRefusedByNameNotWithAKeyNotFoundException()
    {
        var intent = Intent() with { Input = [new("c", Phoneme: CanonicalId.Mint()), new("rest", Remaining: true)],
            Output = [new(Copy: "c"), new(Copy: "rest")] };
        var error = Assert.ThrowsAny<Exception>(() => AuthorAffixProcessComposer.Build(_cache, intent));
        Assert.IsNotType<KeyNotFoundException>(error);
        var create = Proposal(AuthorAffixProcessComposer.Build(_cache, Intent()));
        ProposalApplier.Apply(_cache, create, ScratchDryRun.Of(_cache, create).Anchor, "affix-core-tests");
        var edit = Proposal([
            new OperationEnvelope(CanonicalId.Mint(), MoAffixProcessInputOperationKinds.Create,
                target: Id(Process(create)), entityId: CanonicalId.Mint(),
                after: JsonSerializer.SerializeToElement(new { @class = "PhSimpleContextSeg" })),
        ]);
        var missing = CanonicalId.Mint();
        var withMissing = Proposal([.. edit.Operations, new OperationEnvelope(CanonicalId.Mint(),
            PhSimpleContextSegFeatureStructureOperationKinds.SetFeatureStructure, target: edit.Operations[0].EntityId,
            after: JsonSerializer.SerializeToElement(new { @ref = missing.Value }))]);
        var refusal = Assert.Throws<InvalidOperationException>(() =>
            FootprintProbe.ComputeCurrentFootprintDigest(_cache, withMissing));
        Assert.Contains(missing.Value, refusal.Message, StringComparison.Ordinal);
    }

    private AuthorAffixProcessIntent Intent() => new(CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphInfix), "COPY",
        _cache.WritingSystemFactory.GetStrFromWs(_cache.DefaultAnalWs), new(LexicalEntryMsaKind.Unclassified, Id(_category)),
        [new("c", NaturalClass: Id(_class)), new("v", Phoneme: Id(_vowel)), new("rest", Remaining: true)],
        [new(Copy: "c"), new(Insert: [Id(_insert), Id(_insert)]), new(Copy: "v"), new(Copy: "c"), new(Copy: "v"), new(Copy: "rest")],
        "Synthetic process");
    private void Setup(Action action) => UndoableUnitOfWorkHelper.Do("setup", "setup", _cache.ServiceLocator.GetInstance<IActionHandler>(), action);
    private ICmObjectRepository Repository => _cache.ServiceLocator.GetInstance<ICmObjectRepository>();
    private IMoAffixProcess Process(Proposal proposal) => (IMoAffixProcess)Repository.GetObject(Created(proposal.Operations,
        LexEntryLexemeFormOperationKinds.CreateLexemeForm).ToGuid());
    private static CanonicalId Id(ICmObject value) => CanonicalId.FromGuid(value.Guid);
    private static CanonicalId Created(IReadOnlyList<OperationEnvelope> operations, string kind) =>
        Assert.Single(operations, operation => operation.Kind == kind).EntityId!.Value;
    private static Proposal Proposal(IReadOnlyList<OperationEnvelope> operations) => new(
        new Dictionary<string, string> { ["lexical"] = "1.0", ["grammar"] = "1.0", ["lists"] = "1.0" }, CanonicalId.Mint(), null, operations);
    private static OperationEnvelope ReplaceAfter(OperationEnvelope operation, object after) => new(operation.OperationId,
        operation.Kind, entityId: operation.EntityId, target: operation.Target, after: JsonSerializer.SerializeToElement(after),
        placement: operation.Placement, dependsOn: operation.DependsOn);
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

using SIL.Motif.Runner.Execution;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.LiveHost.Baselines;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Runner.Snapshotting;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using static SIL.Motif.Tests.Composers.AffixTestSupport;

namespace SIL.Motif.Tests.Composers;

[Collection(LcmCacheParallelCollections.Group3)]
public sealed class InflectionClassEditingTests(PristineProjectFixture pristine)
{
    [Fact]
    public void DefaultSetClearAndNoopUseExplicitStateAndPreserveInheritedDefault()
    {
        using var cache = pristine.NewScratch();
        var parent = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
        var child = AddChildCategory(cache, parent);
        var concord = AddInflectionClass(cache, parent, "concord");
        var invariant = AddInflectionClass(cache, parent, "invariant");
        var operations = EditPartOfSpeechInflectionClassDefaultComposer.Build(cache, new(Alias(parent), null, Alias(concord)));
        var set = Assert.Single(operations);
        Assert.Equal(PartOfSpeechDefaultInflectionClassOperationKinds.SetDefaultInflectionClass, set.Kind);
        Assert.Equal(concord.Value, set.After!.Value.GetProperty("ref").GetString());
        ApplyOps(cache, operations);
        Assert.Empty(EditPartOfSpeechInflectionClassDefaultComposer.Build(cache, new(child, null, null)));
        Assert.Empty(EditPartOfSpeechInflectionClassDefaultComposer.Build(cache, new(parent, Alias(concord), concord)));
        Assert.Throws<InvalidOperationException>(() =>
            EditPartOfSpeechInflectionClassDefaultComposer.Build(cache, new(child, concord, invariant)));
        ApplyOps(cache, EditPartOfSpeechInflectionClassDefaultComposer.Build(cache, new(child, null, invariant)));
        var clear = EditPartOfSpeechInflectionClassDefaultComposer.Build(cache, new(child, invariant, null));
        Assert.Equal(PartOfSpeechDefaultInflectionClassOperationKinds.ClearDefaultInflectionClass, Assert.Single(clear).Kind);
        Assert.Empty(clear[0].After!.Value.EnumerateObject());
        ApplyOps(cache, clear);
        Assert.Null(Get<IPartOfSpeech>(cache, child).DefaultInflectionClassRA);
        Assert.Equal(concord.ToGuid(), Get<IPartOfSpeech>(cache, parent).DefaultInflectionClassRA.Guid);
    }

    [Fact]
    public void AffixSetsAreUnorderedNormalizedAndEmptyDesiredClears()
    {
        using var cache = pristine.NewScratch();
        var parent = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
        var child = AddChildCategory(cache, parent);
        var first = AddInflectionClass(cache, parent, "first");
        var second = AddInflectionClass(cache, parent, "second");
        var (_, msa, form) = AddInflectionalPrefix(cache, "n", child);
        ApplyOps(cache, EditAffixInflectionClassesComposer.Build(cache, new(Alias(form), [], [Alias(second), first])));
        Assert.Empty(EditAffixInflectionClassesComposer.Build(cache, new(form, [second, Alias(first)], [first, second])));
        var remove = EditAffixInflectionClassesComposer.Build(cache, new(form, [second, first], [second]));
        Assert.Equal(MoAffixFormInflectionClassesOperationKinds.RemoveRefInflectionClasses, Assert.Single(remove).Kind);
        Assert.Equal(first.Value, remove[0].After!.Value.GetProperty("member").GetString());
        ApplyOps(cache, remove);
        ApplyOps(cache, EditAffixInflectionClassesComposer.Build(cache, new(form, [second], [])));
        Assert.Empty(Get<IMoAffixAllomorph>(cache, form).InflectionClassesRC);
        Assert.Equal(child.ToGuid(), Get<IMoInflAffMsa>(cache, msa).PartOfSpeechRA.Guid);
        Assert.Equal("n", Get<IMoAffixAllomorph>(cache, form).Form.get_String(cache.DefaultVernWs).Text);
    }

    [Fact]
    public void MixedAffixEntryRefusalExplainsThatOnlyInflectionalInfoCanBeEditedHere()
    {
        using var cache = pristine.NewScratch();
        var category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
        var (entryId, _, form) = AddInflectionalPrefix(cache, "n", category);
        var entry = Get<ILexEntry>(cache, entryId);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var derivational = cache.ServiceLocator.GetInstance<IMoDerivAffMsaFactory>().Create();
            entry.MorphoSyntaxAnalysesOC.Add(derivational);
            derivational.FromPartOfSpeechRA = Get<IPartOfSpeech>(cache, category);
            derivational.ToPartOfSpeechRA = Get<IPartOfSpeech>(cache, category);
        });

        var failure = Assert.Throws<InvalidOperationException>(() =>
            EditAffixInflectionClassesComposer.Build(cache, new(form, [], [])));

        Assert.Contains("mixed", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("only inflectional grammatical info", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("compose-edit-derivational-affix-restriction", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NestedClassesCanRestrictOrdinaryPrefixAndSuffixAlternates(bool suffix)
    {
        using var cache = pristine.NewScratch();
        var parent = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
        var child = AddChildCategory(cache, parent);
        var cls = AddInflectionClass(cache, parent, "parent class");
        var nested = Created(ApplyOps(cache, AuthorInflectionClassComposer.Build(cache,
            new(parent, "nested", Anal(cache), Parent: cls))), MoInflClassSubclassesOperationKinds.Create);
        var type = MorphType(suffix ? MoMorphTypeTags.kguidMorphSuffix : MoMorphTypeTags.kguidMorphPrefix);
        var entry = Created(ApplyOps(cache, AuthorLexicalEntryComposer.Build(cache,
            new("n", Vern(cache), type, "concord", Anal(cache), new(LexicalEntryMsaKind.Inflectional, child)))),
            LexDbEntriesOperationKinds.Create);
        var alternate = Created(ApplyOps(cache, AuthorAlternateFormComposer.Build(cache,
            new(entry, "wa", Vern(cache), type))), LexEntryAlternateFormsCreateOperationKinds.CreateAlternateForms);
        ApplyOps(cache, EditAffixInflectionClassesComposer.Build(cache, new(alternate, [], [Alias(nested)]))
            .Concat(EditPartOfSpeechInflectionClassDefaultComposer.Build(cache, new(child, null, Alias(nested)))).ToArray());
        Assert.Equal(nested.ToGuid(), Get<IPartOfSpeech>(cache, child).DefaultInflectionClassRA.Guid);
        Assert.Equal(nested.ToGuid(), Assert.Single(Get<IMoAffixAllomorph>(cache, alternate).InflectionClassesRC).Guid);
        Assert.Empty(((IMoAffixAllomorph)Get<ILexEntry>(cache, entry).LexemeFormOA).InflectionClassesRC);
    }

    [Fact]
    public void ParserIneligibleBracketFormsAreRefusedBeforeLowering()
    {
        using var cache = pristine.NewScratch();
        var category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
        var cls = AddInflectionClass(cache, category, "concord");
        var (_, _, form) = AddInflectionalPrefix(cache, "n", category);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            Get<IMoAffixAllomorph>(cache, form).Form.set_String(cache.DefaultVernWs, "[C]"));
        var before = BaselineSemanticDigest.Compute(cache);
        Assert.Throws<InvalidOperationException>(() => EditAffixInflectionClassesComposer.Build(cache, new(form, [], [cls])));
        Assert.Equal(before, BaselineSemanticDigest.Compute(cache));
    }

    [Fact]
    public void DefaultClassOwnershipMustRemainValidWhenDryRunLowersTheCompleteGraph()
    {
        using var cache = pristine.NewScratch();
        var category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
        var cls = AddInflectionClass(cache, category, "concord");
        var proposal = Proposal(EditPartOfSpeechInflectionClassDefaultComposer.Build(cache, new(category, null, cls)));
        var foreign = AddCategory(cache, "foreign");
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            Get<IPartOfSpeech>(cache, foreign).InflectionClassesOC.Add(Get<IMoInflClass>(cache, cls)));
        var before = BaselineSemanticDigest.Compute(cache);
        var refusal = Record.Exception(() => { _ = ScratchDryRun.Of(cache, proposal); });
        Assert.Null(Get<IPartOfSpeech>(cache, category).DefaultInflectionClassRA);
        Assert.Equal(before, BaselineSemanticDigest.Compute(cache));
        Assert.IsType<InvalidOperationException>(refusal);
    }

    [Fact]
    public void WrongTypeForeignOwnerExpectedMismatchAndDecodedDuplicatesLeaveLiveStateAlone()
    {
        using var cache = pristine.NewScratch();
        var category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
        var cls = AddInflectionClass(cache, category, "concord");
        var foreign = AddInflectionClass(cache, AddCategory(cache, "foreign"), "foreign");
        var (_, _, form) = AddInflectionalPrefix(cache, "n", category);
        var before = BaselineSemanticDigest.Compute(cache);
        foreach (var desired in new[] { category, foreign, CanonicalId.Mint() })
        {
            Assert.ThrowsAny<Exception>(() => EditAffixInflectionClassesComposer.Build(cache, new(form, [], [desired])));
            Assert.ThrowsAny<Exception>(() => EditPartOfSpeechInflectionClassDefaultComposer.Build(cache, new(category, null, desired)));
        }
        Assert.ThrowsAny<Exception>(() => EditPartOfSpeechInflectionClassDefaultComposer.Build(cache, new(form, null, cls)));
        Assert.ThrowsAny<Exception>(() => EditAffixInflectionClassesComposer.Build(cache, new(category, [], [cls])));
        Assert.ThrowsAny<Exception>(() => EditAffixInflectionClassesComposer.Build(cache, new(form, [cls], [])));
        Assert.ThrowsAny<Exception>(() => EditPartOfSpeechInflectionClassDefaultComposer.Build(cache, new(category, cls, null)));
        Assert.ThrowsAny<Exception>(() => EditAffixInflectionClassesComposer.Build(cache, new(form, [], [cls, Alias(cls)])));
        Assert.ThrowsAny<Exception>(() => EditAffixInflectionClassesComposer.Build(cache, new(form, [cls, Alias(cls)], [])));
        Assert.Equal(before, BaselineSemanticDigest.Compute(cache));
    }

    [Theory]
    [InlineData("mixed")]
    [InlineData("unclassified")]
    [InlineData("missingCategory")]
    [InlineData("noMsa")]
    [InlineData("process")]
    [InlineData("circumfix")]
    [InlineData("abstract")]
    [InlineData("blank")]
    [InlineData("infix")]
    public void UnsupportedEntryScopesRejectEvenEmptyAndNoopEdits(string scope)
    {
        using var cache = pristine.NewScratch();
        var category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
        var (entryId, msaId, form) = AddInflectionalPrefix(cache, "n", category);
        var entry = Get<ILexEntry>(cache, entryId);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            switch (scope)
            {
                case "mixed": entry.MorphoSyntaxAnalysesOC.Add(cache.ServiceLocator.GetInstance<IMoStemMsaFactory>().Create()); break;
                case "unclassified":
                    entry.MorphoSyntaxAnalysesOC.Clear();
                    entry.MorphoSyntaxAnalysesOC.Add(cache.ServiceLocator.GetInstance<IMoUnclassifiedAffixMsaFactory>().Create());
                    break;
                case "missingCategory": Get<IMoInflAffMsa>(cache, msaId).PartOfSpeechRA = null; break;
                case "noMsa": entry.MorphoSyntaxAnalysesOC.Clear(); break;
                case "circumfix": entry.LexemeFormOA.MorphTypeRA = Get<IMoMorphType>(cache, MorphType(MoMorphTypeTags.kguidMorphCircumfix)); break;
                case "infix": entry.LexemeFormOA.MorphTypeRA = Get<IMoMorphType>(cache, MorphType(MoMorphTypeTags.kguidMorphInfix)); break;
                case "abstract": entry.LexemeFormOA.IsAbstract = true; break;
                case "blank": entry.LexemeFormOA.Form.set_String(cache.DefaultVernWs, " "); break;
                default: entry.AlternateFormsOS.Add(cache.ServiceLocator.GetInstance<IMoAffixProcessFactory>().Create()); break;
            }
        });
        var before = BaselineSemanticDigest.Compute(cache);
        Assert.ThrowsAny<Exception>(() => EditAffixInflectionClassesComposer.Build(cache, new(form, [], [])));
        if (scope == "process")
            Assert.ThrowsAny<Exception>(() => EditAffixInflectionClassesComposer.Build(cache,
                new(Id(entry.AlternateFormsOS[0]), [], [])));
        Assert.Equal(before, BaselineSemanticDigest.Compute(cache));
    }

    [Theory]
    [InlineData("removedClass")]
    [InlineData("ancestorName")]
    [InlineData("ancestorDefault")]
    [InlineData("inheritedClass")]
    [InlineData("msaCategory")]
    [InlineData("mixedMsa")]
    [InlineData("process")]
    [InlineData("subclassName")]
    [InlineData("subclassAdded")]
    [InlineData("classOwner")]
    [InlineData("categoryAncestry")]
    [InlineData("formOwner")]
    public void RemovalOnlyEditsBindClassCategoryDefaultAndEntryScope(string drift)
    {
        using var cache = pristine.NewScratch();
        var parent = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
        var child = AddChildCategory(cache, parent);
        var cls = AddInflectionClass(cache, parent, "restricted");
        var fallback = AddInflectionClass(cache, parent, "fallback");
        var subclass = Created(ApplyOps(cache, AuthorInflectionClassComposer.Build(cache,
            new(parent, "subclass", Anal(cache), Parent: cls))), MoInflClassSubclassesOperationKinds.Create);
        var otherCategory = AddCategory(cache, "other category");
        var (otherEntry, _, _) = AddInflectionalPrefix(cache, "other", child);
        ApplyOps(cache, EditPartOfSpeechInflectionClassDefaultComposer.Build(cache, new(parent, null, fallback)));
        var (entry, msa, form) = AddInflectionalPrefix(cache, "n", child);
        ApplyOps(cache, EditAffixInflectionClassesComposer.Build(cache, new(form, [], [cls])));
        var proposal = Proposal(EditAffixInflectionClassesComposer.Build(cache, new(form, [cls], [])));
        var dryRun = ScratchDryRun.Of(cache, proposal);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            switch (drift)
            {
                case "removedClass": Get<IMoInflClass>(cache, cls).Name.set_String(cache.DefaultAnalWs, "changed"); break;
                case "ancestorName": Get<IPartOfSpeech>(cache, parent).Name.set_String(cache.DefaultAnalWs, "changed"); break;
                case "ancestorDefault": Get<IPartOfSpeech>(cache, parent).DefaultInflectionClassRA = Get<IMoInflClass>(cache, cls); break;
                case "inheritedClass": Get<IMoInflClass>(cache, fallback).Name.set_String(cache.DefaultAnalWs, "changed"); break;
                case "msaCategory": Get<IMoInflAffMsa>(cache, msa).PartOfSpeechRA = Get<IPartOfSpeech>(cache, parent); break;
                case "mixedMsa": Get<ILexEntry>(cache, entry).MorphoSyntaxAnalysesOC.Add(cache.ServiceLocator.GetInstance<IMoStemMsaFactory>().Create()); break;
                case "subclassName": Get<IMoInflClass>(cache, subclass).Name.set_String(cache.DefaultAnalWs, "changed"); break;
                case "subclassAdded": Get<IMoInflClass>(cache, cls).SubclassesOC.Add(cache.ServiceLocator.GetInstance<IMoInflClassFactory>().Create()); break;
                case "classOwner": Get<IPartOfSpeech>(cache, otherCategory).InflectionClassesOC.Add(Get<IMoInflClass>(cache, cls)); break;
                case "categoryAncestry": Get<IPartOfSpeech>(cache, otherCategory).SubPossibilitiesOS.Add(Get<IPartOfSpeech>(cache, child)); break;
                case "formOwner": Get<ILexEntry>(cache, otherEntry).AlternateFormsOS.Add(Get<IMoAffixAllomorph>(cache, form)); break;
                default: Get<ILexEntry>(cache, entry).AlternateFormsOS.Add(cache.ServiceLocator.GetInstance<IMoAffixProcessFactory>().Create()); break;
            }
        });
        var before = BaselineSemanticDigest.Compute(cache);
        Assert.Throws<PreconditionRefusal>(() => ProposalApplier.Apply(cache, proposal, dryRun.Anchor, "class-editor-tests"));
        Assert.Equal(before, BaselineSemanticDigest.Compute(cache));
        Assert.Equal(cls.ToGuid(), Assert.Single(Get<IMoAffixAllomorph>(cache, form).InflectionClassesRC).Guid);
        if (drift is not ("mixedMsa" or "process"))
        {
            ProposalApplier.Apply(cache, proposal, ScratchDryRun.Of(cache, proposal).Anchor, "class-editor-tests");
            Assert.Empty(Get<IMoAffixAllomorph>(cache, form).InflectionClassesRC);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefaultSetAndClearBindAncestorDefaultsAndDefinitions(bool clear)
    {
        using var cache = pristine.NewScratch();
        var parent = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
        var child = AddChildCategory(cache, parent);
        var cls = AddInflectionClass(cache, parent, "explicit");
        var inherited = AddInflectionClass(cache, parent, "inherited");
        ApplyOps(cache, EditPartOfSpeechInflectionClassDefaultComposer.Build(cache, new(parent, null, inherited)));
        if (clear) ApplyOps(cache, EditPartOfSpeechInflectionClassDefaultComposer.Build(cache, new(child, null, cls)));
        var proposal = Proposal(EditPartOfSpeechInflectionClassDefaultComposer.Build(cache,
            new(child, clear ? cls : null, clear ? null : cls)));
        var dryRun = ScratchDryRun.Of(cache, proposal);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            Get<IMoInflClass>(cache, inherited).Name.set_String(cache.DefaultAnalWs, "changed"));
        var before = BaselineSemanticDigest.Compute(cache);
        Assert.Throws<PreconditionRefusal>(() => ProposalApplier.Apply(cache, proposal, dryRun.Anchor, "class-editor-tests"));
        Assert.Equal(before, BaselineSemanticDigest.Compute(cache));
        ProposalApplier.Apply(cache, proposal, ScratchDryRun.Of(cache, proposal).Anchor, "class-editor-tests");
        Assert.Equal(clear ? null : (Guid?)cls.ToGuid(), Get<IPartOfSpeech>(cache, child).DefaultInflectionClassRA?.Guid);
    }

    [Fact]
    public void UnrelatedLexicalChangesRemainPermitted()
    {
        using var cache = pristine.NewScratch();
        var category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
        var cls = AddInflectionClass(cache, category, "concord");
        var (_, _, form) = AddInflectionalPrefix(cache, "n", category);
        var (_, _, stem) = AddStem(cache, "root", category);
        var proposal = Proposal(EditAffixInflectionClassesComposer.Build(cache, new(form, [], [cls]))
            .Concat(EditPartOfSpeechInflectionClassDefaultComposer.Build(cache, new(category, null, cls))).ToArray());
        var dryRun = ScratchDryRun.Of(cache, proposal);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            Get<IMoStemAllomorph>(cache, stem).Form.set_String(cache.DefaultVernWs, "changed"));
        ProposalApplier.Apply(cache, proposal, dryRun.Anchor, "class-editor-tests");
        Assert.Equal("changed", Get<IMoStemAllomorph>(cache, stem).Form.get_String(cache.DefaultVernWs).Text);
    }

    [Fact]
    public void PreviewSnapshotMechanicalDiffAndSavedReadbackUseExistingReferenceFields()
    {
        using var cache = pristine.NewScratch();
        var category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
        var cls = AddInflectionClass(cache, category, "concord");
        var (_, _, form) = AddInflectionalPrefix(cache, "n", category);
        var before = Snapshots(cache, category, form);
        var proposal = Proposal(EditAffixInflectionClassesComposer.Build(cache, new(form, [], [cls]))
            .Concat(EditPartOfSpeechInflectionClassDefaultComposer.Build(cache, new(category, null, cls))).Reverse().ToArray());
        var baseline = BaselineSemanticDigest.Compute(cache);
        var dryRun = ScratchDryRun.Of(cache, proposal);
        Assert.Equal(baseline, BaselineSemanticDigest.Compute(cache));
        foreach (var field in new[] { SnapshotFields.MoAffixFormInflectionClasses, SnapshotFields.PartOfSpeechDefaultInflectionClass })
            Assert.Contains(dryRun.ExpectedEffects, effect => effect.Field == field && effect.After.Values.Contains(cls.Value));
        ProposalApplier.Apply(cache, proposal, dryRun.Anchor, "class-editor-tests");
        var diff = SoundSystemSnapshots.Compare(before, Snapshots(cache, category, form));
        Assert.Equal(2, diff.Count);
        Assert.All(diff, effect => Assert.Contains(cls.Value, effect.After.Values));
        var digest = BaselineSemanticDigest.Compute(cache);
        var loader = new FwDataProjectLoader();
        loader.Save(cache);
        using var reopened = loader.LoadScratchCache(cache.ProjectId.Path);
        Assert.Equal(cls.ToGuid(), Get<IPartOfSpeech>(reopened, category).DefaultInflectionClassRA.Guid);
        Assert.Equal(cls.ToGuid(), Assert.Single(Get<IMoAffixAllomorph>(reopened, form).InflectionClassesRC).Guid);
        Assert.Equal(digest, BaselineSemanticDigest.Compute(reopened));
    }

    [Fact]
    public void FailureAfterBothEditorWritesRollsBackTheWholeProposal()
    {
        using var cache = pristine.NewScratch();
        var category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
        var cls = AddInflectionClass(cache, category, "concord");
        var (_, _, form) = AddInflectionalPrefix(cache, "n", category);
        var edits = EditAffixInflectionClassesComposer.Build(cache, new(form, [], [cls]))
            .Concat(EditPartOfSpeechInflectionClassDefaultComposer.Build(cache, new(category, null, cls))).ToArray();
        var valid = Proposal(edits);
        var dryRun = ScratchDryRun.Of(cache, valid);
        var before = BaselineSemanticDigest.Compute(cache);
        var reachedBothWrites = false;
        Assert.Throws<InvalidOperationException>(() => ProposalApplier.Apply(cache, valid, dryRun.Anchor,
            "class-editor-tests", "", (index, _) =>
            {
                if (index != 2) return;
                Assert.Equal(cls.ToGuid(), Get<IPartOfSpeech>(cache, category).DefaultInflectionClassRA.Guid);
                Assert.Equal(cls.ToGuid(), Assert.Single(Get<IMoAffixAllomorph>(cache, form).InflectionClassesRC).Guid);
                reachedBothWrites = true;
                throw new InvalidOperationException("Injected failure after both class edits.");
            }));
        Assert.True(reachedBothWrites);
        Assert.Null(Get<IPartOfSpeech>(cache, category).DefaultInflectionClassRA);
        Assert.Empty(Get<IMoAffixAllomorph>(cache, form).InflectionClassesRC);
        Assert.Equal(before, BaselineSemanticDigest.Compute(cache));
    }

    [Fact]
    public void ClearingDefaultAndRestrictionsAppearsInPreviewAndAggregateMechanicalDiff()
    {
        using var cache = pristine.NewScratch();
        var category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
        var cls = AddInflectionClass(cache, category, "concord");
        var (_, _, form) = AddInflectionalPrefix(cache, "n", category);
        ApplyOps(cache, EditAffixInflectionClassesComposer.Build(cache, new(form, [], [cls]))
            .Concat(EditPartOfSpeechInflectionClassDefaultComposer.Build(cache, new(category, null, cls))).ToArray());
        var before = Snapshots(cache, category, form);
        var proposal = Proposal(EditAffixInflectionClassesComposer.Build(cache, new(form, [Alias(cls)], []))
            .Concat(EditPartOfSpeechInflectionClassDefaultComposer.Build(cache, new(category, Alias(cls), null))).ToArray());
        var dryRun = ScratchDryRun.Of(cache, proposal);
        foreach (var field in new[] { SnapshotFields.MoAffixFormInflectionClasses, SnapshotFields.PartOfSpeechDefaultInflectionClass })
            Assert.Contains(dryRun.ExpectedEffects, effect => effect.Field == field && effect.Before.Values.Contains(cls.Value) && effect.After.Count == 0);
        ProposalApplier.Apply(cache, proposal, dryRun.Anchor, "class-editor-tests");
        var diff = SoundSystemSnapshots.Compare(before, Snapshots(cache, category, form));
        Assert.Equal(2, diff.Count);
        Assert.All(diff, effect => { Assert.Contains(cls.Value, effect.Before.Values); Assert.Empty(effect.After); });
        var digest = BaselineSemanticDigest.Compute(cache);
        var loader = new FwDataProjectLoader();
        loader.Save(cache);
        using var reopened = loader.LoadScratchCache(cache.ProjectId.Path);
        Assert.Null(Get<IPartOfSpeech>(reopened, category).DefaultInflectionClassRA);
        Assert.Empty(Get<IMoAffixAllomorph>(reopened, form).InflectionClassesRC);
        Assert.Equal(digest, BaselineSemanticDigest.Compute(reopened));
    }

    private static CanonicalId Alias(CanonicalId id) => CanonicalId.Parse("class:" + id.Value);

    private static CanonicalId AddChildCategory(LcmCache cache, CanonicalId parent)
    {
        IPartOfSpeech child = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            child = cache.ServiceLocator.GetInstance<IPartOfSpeechFactory>().Create();
            Get<IPartOfSpeech>(cache, parent).SubPossibilitiesOS.Add(child);
            child.Name.set_String(cache.DefaultAnalWs, "child");
        });
        return Id(child);
    }

    private static IReadOnlyDictionary<CanonicalId, ObjectSnapshot> Snapshots(LcmCache cache, CanonicalId category, CanonicalId form) =>
        new Dictionary<CanonicalId, ObjectSnapshot>
        {
            [category] = InflectionSnapshots.Read(cache)[category],
            [form] = MoAffixFormRelationsSnapshotter.Snapshot(cache, Get<IMoAffixAllomorph>(cache, form)),
        };
}

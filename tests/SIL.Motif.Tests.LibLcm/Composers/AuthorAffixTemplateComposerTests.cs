using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Commands;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Model.DryRun;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Runner.Snapshotting;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Composers;

[Collection(LcmCacheParallelCollections.Group0)]
public sealed class AuthorAffixTemplateComposerTests : IDisposable
{
    private readonly LcmCache _cache;
    private readonly SeededProject _seed;

    public AuthorAffixTemplateComposerTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        _seed = pristine.Seed;
    }

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [Fact]
    public void AuthoringAlternativeTemplatePreservesOrderedSlotsAndFinality()
    {
        var category = GetCategory(_cache, _seed.PartOfSpeechId);
        var prefixes = new[] { CreateSlot(_cache, category, "inner prefix"), CreateSlot(_cache, category, "outer prefix") };
        var suffixes = new[] { CreateSlot(_cache, category, "inner suffix"), CreateSlot(_cache, category, "outer suffix") };
        var existing = CreateTemplate(_cache, category, "Existing", [prefixes[0]], [suffixes[0]]);
        var prefixIds = prefixes.Select(Id).ToArray();
        var suffixIds = suffixes.Select(Id).ToArray();
        var templateId = CanonicalId.Mint();
        var ids = new Queue<CanonicalId>([
            templateId, CanonicalId.Mint(), CanonicalId.Mint(), CanonicalId.Mint(),
            CanonicalId.Mint(), CanonicalId.Mint(), CanonicalId.Mint(), CanonicalId.Mint()]);
        var writingSystem = _cache.WritingSystemFactory.GetStrFromWs(_cache.DefaultAnalWs);

        var operations = AuthorAffixTemplateComposer.Build(_cache,
            new AuthorAffixTemplateIntent(Id(category), "café alternative", writingSystem,
                prefixIds, suffixIds, Final: true), () => ids.Dequeue());

        var create = Assert.Single(operations,
            operation => operation.Kind == PartOfSpeechAffixTemplatesOperationKinds.Create);
        Assert.Equal(templateId, create.EntityId);
        Assert.Equal(Id(existing), create.Placement?.After);
        var name = Assert.Single(operations, operation => operation.Kind == MoInflAffixTemplateNameOperationKinds.SetName);
        Assert.Equal((writingSystem, "cafe\u0301 alternative"),
            MoInflAffixTemplateNameSetPayload.Parse(name.After!.Value));
        var final = Assert.Single(operations, operation => operation.Kind == MoInflAffixTemplateFinalOperationKinds.SetFinal);
        Assert.True(MoInflAffixTemplateFinalSetPayload.Parse(final.After!.Value));
        Assert.DoesNotContain(operations, operation => operation.Kind == MoInflAffixTemplateDisabledOperationKinds.SetDisabled);

        var prefixAdds = operations.Where(operation =>
            operation.Kind == MoInflAffixTemplatePrefixSlotsOperationKinds.AddRefPrefixSlots).ToArray();
        var suffixAdds = operations.Where(operation =>
            operation.Kind == MoInflAffixTemplateSuffixSlotsOperationKinds.AddRefSuffixSlots).ToArray();
        Assert.Equal(prefixIds, prefixAdds.Select(operation =>
            MoInflAffixTemplatePrefixSlotsMemberPayload.Parse(operation.After!.Value,
                MoInflAffixTemplatePrefixSlotsOperationKinds.AddRefPrefixSlots)));
        Assert.Equal(suffixIds, suffixAdds.Select(operation =>
            MoInflAffixTemplateSuffixSlotsMemberPayload.Parse(operation.After!.Value,
                MoInflAffixTemplateSuffixSlotsOperationKinds.AddRefSuffixSlots)));
        Assert.Null(prefixAdds[0].Placement);
        Assert.Equal(prefixIds[0], prefixAdds[1].Placement?.After);
        Assert.Contains(prefixAdds[0].OperationId,
            prefixAdds[1].DependsOn.Select(dependency => dependency.OperationId));
        Assert.Null(suffixAdds[0].Placement);
        Assert.Equal(suffixIds[0], suffixAdds[1].Placement?.After);
        Assert.Contains(suffixAdds[0].OperationId,
            suffixAdds[1].DependsOn.Select(dependency => dependency.OperationId));
        Assert.All(operations.Where(operation => operation != create), operation =>
            Assert.Contains(create.OperationId,
                operation.DependsOn.Select(dependency => dependency.OperationId)));
        Assert.Single(category.AffixTemplatesOS);

        var proposal = Proposal(operations.Reverse().ToArray());
        Assert.Empty(ChangeFitPreflight.Check(_cache, proposal));
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        Assert.Contains(dryRun.ExpectedEffects, effect => effect.Field == SnapshotFields.PartOfSpeechAffixTemplates);
        Assert.Contains(dryRun.ExpectedEffects, effect => effect.Field == SnapshotFields.MoInflAffixTemplateName);
        Assert.Contains(dryRun.ExpectedEffects, effect => effect.Field == SnapshotFields.MoInflAffixTemplateFinal);
        var prefixEffect = dryRun.ExpectedEffects.Last(
            effect => effect.Field == SnapshotFields.MoInflAffixTemplatePrefixSlots);
        Assert.Equal(prefixIds, prefixEffect.After.OrderBy(pair => int.Parse(pair.Key))
            .Select(pair => CanonicalId.Parse(pair.Value)));
        var suffixEffect = dryRun.ExpectedEffects.Last(
            effect => effect.Field == SnapshotFields.MoInflAffixTemplateSuffixSlots);
        Assert.Equal(suffixIds, suffixEffect.After.OrderBy(pair => int.Parse(pair.Key))
            .Select(pair => CanonicalId.Parse(pair.Value)));

        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "author-affix-template-tests");
        var created = Assert.IsAssignableFrom<IMoInflAffixTemplate>(
            _cache.ServiceLocator.GetInstance<ICmObjectRepository>().GetObject(templateId.ToGuid()));
        Assert.Equal([existing.Guid, templateId.ToGuid()], category.AffixTemplatesOS.Select(template => template.Guid));
        Assert.Equal(prefixes.Select(slot => slot.Guid), created.PrefixSlotsRS.Select(slot => slot.Guid));
        Assert.Equal(suffixes.Select(slot => slot.Guid), created.SuffixSlotsRS.Select(slot => slot.Guid));
        Assert.Equal("cafe\u0301 alternative", created.Name.get_String(_cache.DefaultAnalWs)?.Text);
        Assert.True(created.Final);
        Assert.False(created.Disabled);
        var after = TemplateSnapshot(_cache, created);
        var changes = SoundSystemSnapshots.Compare(
            new Dictionary<CanonicalId, ObjectSnapshot> { [templateId] = ObjectSnapshot.Empty(templateId) },
            new Dictionary<CanonicalId, ObjectSnapshot> { [templateId] = after });
        Assert.Contains(changes, effect => effect.CanonicalId == templateId &&
            effect.Field == SnapshotFields.MoInflAffixTemplateName);
        Assert.Contains(changes, effect => effect.CanonicalId == templateId &&
            effect.Field == SnapshotFields.MoInflAffixTemplateFinal);

        var path = _cache.ProjectId.Path;
        new FwDataProjectLoader().Save(_cache);
        using var reopened = new FwDataProjectLoader().LoadScratchCache(path);
        var reopenedTemplate = Assert.IsAssignableFrom<IMoInflAffixTemplate>(
            reopened.ServiceLocator.GetInstance<ICmObjectRepository>().GetObject(templateId.ToGuid()));
        Assert.Empty(SoundSystemSnapshots.Compare(
            new Dictionary<CanonicalId, ObjectSnapshot> { [templateId] = after },
            new Dictionary<CanonicalId, ObjectSnapshot> { [templateId] = TemplateSnapshot(reopened, reopenedTemplate) }));
        Assert.Equal(prefixIds.Select(id => id.ToGuid()), reopenedTemplate.PrefixSlotsRS.Select(slot => slot.Guid));
        Assert.Equal(suffixIds.Select(id => id.ToGuid()), reopenedTemplate.SuffixSlotsRS.Select(slot => slot.Guid));
        Assert.True(reopenedTemplate.Final);
        Assert.False(reopenedTemplate.Disabled);
    }

    [Fact]
    public void IntentParserRequiresClosedFieldsOrderedListsAndBooleanFinality()
    {
        var id = CanonicalId.Mint().Value;
        using var unknown = JsonDocument.Parse($$"""
            {"category":"{{id}}","name":"alt","ws":"en","prefixSlots":[],"suffixSlots":[],"final":false,"extra":true}
            """);
        using var missingFinal = JsonDocument.Parse($$"""
            {"category":"{{id}}","name":"alt","ws":"en","prefixSlots":[],"suffixSlots":[]}
            """);
        using var wrongFinal = JsonDocument.Parse($$"""
            {"category":"{{id}}","name":"alt","ws":"en","prefixSlots":[],"suffixSlots":[],"final":"false"}
            """);

        Assert.Throws<ContractParseException>(() => AuthorAffixTemplateIntentParser.Parse(unknown.RootElement));
        Assert.Throws<ContractParseException>(() => AuthorAffixTemplateIntentParser.Parse(missingFinal.RootElement));
        Assert.Throws<ContractParseException>(() => AuthorAffixTemplateIntentParser.Parse(wrongFinal.RootElement));
    }

    [Fact]
    public void AuthoringTemplateRejectsRepeatedAndUnrelatedCategorySlots()
    {
        var category = GetCategory(_cache, _seed.PartOfSpeechId);
        var unrelated = CreateCategory(_cache, null, "Unrelated");
        var localSlot = CreateSlot(_cache, category, "Local");
        var unrelatedSlot = CreateSlot(_cache, unrelated, "Other");
        var writingSystem = _cache.WritingSystemFactory.GetStrFromWs(_cache.DefaultAnalWs);

        var duplicate = Assert.Throws<InvalidOperationException>(() => AuthorAffixTemplateComposer.Build(_cache,
            new AuthorAffixTemplateIntent(Id(category), "Duplicate", writingSystem,
                [Id(localSlot)], [Id(localSlot)], false)));
        Assert.Contains("cannot appear more than once", duplicate.Message, StringComparison.Ordinal);

        var crossCategory = Assert.Throws<InvalidOperationException>(() => AuthorAffixTemplateComposer.Build(_cache,
            new AuthorAffixTemplateIntent(Id(category), "Wrong category", writingSystem,
                [], [Id(unrelatedSlot)], false)));
        Assert.Contains("does not belong to the template category or one of its ancestors",
            crossCategory.Message, StringComparison.Ordinal);
        Assert.Empty(category.AffixTemplatesOS);
    }

    [Fact]
    public void AuthoringTemplateAcceptsAnAncestorSlotAndPreservesFalseFinality()
    {
        var ancestor = CreateCategory(_cache, null, "Ancestor");
        var child = CreateCategory(_cache, ancestor, "Child");
        var inheritedSlot = CreateSlot(_cache, ancestor, "Inherited");
        var writingSystem = _cache.WritingSystemFactory.GetStrFromWs(_cache.DefaultAnalWs);
        var operations = AuthorAffixTemplateComposer.Build(_cache,
            new AuthorAffixTemplateIntent(Id(child), "Non-final alternative", writingSystem,
                [Id(inheritedSlot)], [], false));

        var final = Assert.Single(operations,
            operation => operation.Kind == MoInflAffixTemplateFinalOperationKinds.SetFinal);
        Assert.False(MoInflAffixTemplateFinalSetPayload.Parse(final.After!.Value));
        Assert.Single(operations, operation =>
            operation.Kind == MoInflAffixTemplatePrefixSlotsOperationKinds.AddRefPrefixSlots);
        Assert.Empty(child.AffixTemplatesOS);
    }

    [Fact]
    public void CreateRefusesSameTypeAndWrongTypeCanonicalIdentityCollisions()
    {
        var category = GetCategory(_cache, _seed.PartOfSpeechId);
        var existing = CreateTemplate(_cache, category, "Existing", [], []);

        var reuse = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache,
            Proposal(BuildWithTemplateId(_cache, category, Id(existing)))));
        Assert.Contains("overwrite/reuse", reuse.Message, StringComparison.Ordinal);

        var conflict = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache,
            Proposal(BuildWithTemplateId(_cache, category, Id(category)))));
        Assert.Contains("semantic conflict", conflict.Message, StringComparison.Ordinal);
        Assert.Single(category.AffixTemplatesOS);
    }

    [Fact]
    public void MembershipFailureRollsBackTheNewTemplateAndItsWrites()
    {
        var category = GetCategory(_cache, _seed.PartOfSpeechId);
        var prefix = CreateSlot(_cache, category, "Required prefix");
        var writingSystem = _cache.WritingSystemFactory.GetStrFromWs(_cache.DefaultAnalWs);
        var templateId = CanonicalId.Mint();
        var values = new Queue<CanonicalId>([
            templateId, CanonicalId.Mint(), CanonicalId.Mint(), CanonicalId.Mint(), CanonicalId.Mint()]);
        var operations = AuthorAffixTemplateComposer.Build(_cache,
            new AuthorAffixTemplateIntent(Id(category), "Rollback alternative", writingSystem,
                [Id(prefix)], [], true), () => values.Dequeue());
        var proposal = Proposal(operations);
        var dryRun = ScratchDryRun.Of(_cache, proposal);

        var exception = Assert.Throws<InvalidOperationException>(() => ProposalApplier.Apply(
            _cache, proposal, dryRun.Anchor, "author-affix-template-tests", string.Empty,
            afterOperation: (_, operation) =>
            {
                if (operation.Kind == MoInflAffixTemplatePrefixSlotsOperationKinds.AddRefPrefixSlots)
                    throw new InvalidOperationException("injected after template membership");
            }));

        Assert.Equal("injected after template membership", exception.Message);
        Assert.False(_cache.ServiceLocator.GetInstance<ICmObjectRepository>().IsValidObjectId(templateId.ToGuid()));
        Assert.Empty(category.AffixTemplatesOS);
    }

    private static IPartOfSpeech GetCategory(LcmCache cache, Guid id) =>
        cache.ServiceLocator.GetInstance<ICmObjectRepository>().GetObject(id) as IPartOfSpeech ??
        throw new InvalidOperationException();

    private static ObjectSnapshot TemplateSnapshot(LcmCache cache, IMoInflAffixTemplate template)
    {
        var fields = new Dictionary<string, IReadOnlyDictionary<string, string>>(
            MoInflAffixTemplateAuthoringSnapshotter.Snapshot(cache, template).AlternativesFields);
        foreach (var (field, value) in MoInflAffixTemplateSnapshotter.Snapshot(cache, template).AlternativesFields)
            fields[field] = value;
        return new ObjectSnapshot(Id(template), fields);
    }

    private static IPartOfSpeech CreateCategory(LcmCache cache, IPartOfSpeech? parent, string name)
    {
        IPartOfSpeech category = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            category = cache.ServiceLocator.GetInstance<IPartOfSpeechFactory>().Create();
            (parent is null ? cache.LangProject.PartsOfSpeechOA.PossibilitiesOS : parent.SubPossibilitiesOS)
                .Add(category);
            category.Name.set_String(cache.DefaultAnalWs, name);
        });
        return category;
    }

    private static IMoInflAffixSlot CreateSlot(LcmCache cache, IPartOfSpeech category, string name)
    {
        IMoInflAffixSlot slot = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            slot = cache.ServiceLocator.GetInstance<IMoInflAffixSlotFactory>().Create();
            category.AffixSlotsOC.Add(slot);
            slot.Name.set_String(cache.DefaultAnalWs, name);
        });
        return slot;
    }

    private static IMoInflAffixTemplate CreateTemplate(LcmCache cache, IPartOfSpeech category, string name,
        IReadOnlyList<IMoInflAffixSlot> prefixes, IReadOnlyList<IMoInflAffixSlot> suffixes)
    {
        IMoInflAffixTemplate template = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            template = cache.ServiceLocator.GetInstance<IMoInflAffixTemplateFactory>().Create();
            category.AffixTemplatesOS.Add(template);
            template.Name.set_String(cache.DefaultAnalWs, name);
            foreach (var slot in prefixes) template.PrefixSlotsRS.Add(slot);
            foreach (var slot in suffixes) template.SuffixSlotsRS.Add(slot);
        });
        return template;
    }

    private static IReadOnlyList<OperationEnvelope> BuildWithTemplateId(
        LcmCache cache, IPartOfSpeech category, CanonicalId templateId)
    {
        var values = new Queue<CanonicalId>([
            templateId, CanonicalId.Mint(), CanonicalId.Mint(), CanonicalId.Mint()]);
        var writingSystem = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
        return AuthorAffixTemplateComposer.Build(cache,
            new AuthorAffixTemplateIntent(Id(category), "Alternative", writingSystem, [], [], false),
            () => values.Dequeue());
    }

    private static Proposal Proposal(IReadOnlyList<OperationEnvelope> operations) => new(
        new Dictionary<string, string> { ["grammar"] = "1.0" }, CanonicalId.Mint(), null, operations);

    private static CanonicalId Id(ICmObject value) => CanonicalId.FromGuid(value.Guid);
}

using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Retirement;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Retirement;

[Collection(LcmCacheParallelCollections.Group1)]
public sealed class RetirementComposeStepTests(PristineProjectFixture pristine)
{
    private const string Version = "0.1.0";

    private static ComposeStepRequest Step(string verb, string intentJson) =>
        new(verb, JsonDocument.Parse(intentJson).RootElement.Clone(), null, null);

    [Fact]
    public void AZeroAffixRetirementComposesAsAStepOfOneCall()
    {
        var project = RetireRedundantZeroAffixComposerTests.CreateZeroAffixProject(pristine, "^0", optional: true);
        string path;
        string entry;
        using (var cache = project.Cache)
        {
            path = cache.ProjectId.Path;
            entry = CanonicalId.FromGuid(project.Fixture.Entry.Guid).Value;
        }
        Assert.True(ProposalCommands.New(new(path, Version, "zero", "retire a zero affix")).Succeeded);

        var composed = ProposalCommands.Compose(new(path, Version, "zero", [Step("retire-redundant-zero-affix", ZeroAffixDocument(entry))], null));

        Assert.True(composed.Succeeded, composed.Refusal?.Message);
        var only = Assert.Single(composed.Value!.Intents);
        Assert.Equal("RetireRedundantZeroAffix", only.ComposerName);
        Assert.Contains(only.Operations, operation => operation.Kind == RetireRedundantZeroAffixOperationKinds.DeleteGraph);
    }

    [Fact]
    public void AZeroAffixDocumentWithAnExtraPropertyIsRefusedBeforeComposing()
    {
        var project = RetireRedundantZeroAffixComposerTests.CreateZeroAffixProject(pristine, "^0", optional: true);
        string path;
        string entry;
        using (var cache = project.Cache)
        {
            path = cache.ProjectId.Path;
            entry = CanonicalId.FromGuid(project.Fixture.Entry.Guid).Value;
        }
        Assert.True(ProposalCommands.New(new(path, Version, "zero-extra", "retire a zero affix")).Succeeded);

        var refused = ProposalCommands.Compose(new(path, Version, "zero-extra", [Step("retire-redundant-zero-affix",
            ZeroAffixDocument(entry, extra: true))], null));

        Assert.False(refused.Succeeded);
        Assert.Contains("unknown property 'extra'", refused.Refusal!.Message);
    }

    [Fact]
    public void ADuplicateFormRetirementComposesAsAStepOfOneCall()
    {
        var (path, document) = DuplicateAffixFixture();
        Assert.True(ProposalCommands.New(new(path, Version, "duplicate", "retire a duplicate form")).Succeeded);

        var composed = ProposalCommands.Compose(new(path, Version, "duplicate", [Step("retire-allomorph", document)], null));

        Assert.True(composed.Succeeded, composed.Refusal?.Message);
        var only = Assert.Single(composed.Value!.Intents);
        Assert.Equal("RetireAllomorph", only.ComposerName);
        Assert.NotEmpty(only.Operations);
    }

    [Fact]
    public void AStepAndTheSingleVerbLeaveTheSameDraftOperationKinds()
    {
        var (path, document) = DuplicateAffixFixture();
        Assert.True(ProposalCommands.New(new(path, Version, "by-step", "retire a duplicate form")).Succeeded);
        Assert.True(ProposalCommands.New(new(path, Version, "by-verb", "retire a duplicate form")).Succeeded);

        var step = ProposalCommands.Compose(new(path, Version, "by-step", [Step("retire-allomorph", document)], null));
        var verb = ProposalCommands.ComposeIntent(new(path, Version, "by-verb", "retire-allomorph", document));

        Assert.True(step.Succeeded, step.Refusal?.Message);
        Assert.True(verb.Succeeded, verb.Refusal?.Message);
        Assert.Equal(verb.Value!.Operations.Select(operation => operation.Kind),
            step.Value!.Intents.Single().Operations.Select(operation => operation.Kind));
    }

    private static string ZeroAffixDocument(string entry, bool extra = false) => JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["format"] = "motif-retire-redundant-zero-affix",
        ["version"] = 1,
        ["retirement"] = extra ? new { entry, extra = 1 } : new { entry },
    });

    private (string Path, string Document) DuplicateAffixFixture()
    {
        using var cache = pristine.NewScratch();
        var entry = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(pristine.Seed.FirstEntryId);
        var prefix = cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphPrefix);
        var forms = new IMoAffixAllomorph[2];
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            for (var index = 0; index < forms.Length; index++)
            {
                forms[index] = cache.ServiceLocator.GetInstance<IMoAffixAllomorphFactory>().Create();
                entry.AlternateFormsOS.Add(forms[index]);
                forms[index].MorphTypeRA = prefix;
                forms[index].Form.set_String(cache.DefaultVernWs, "step-retirement-duplicate");
            }
        });
        new FwDataProjectLoader().Save(cache);
        var entryId = CanonicalId.FromGuid(entry.Guid).Value;
        var identities = forms.Select(form => new AllomorphIdentity(CanonicalId.FromGuid(form.Guid).Value, entryId,
            form.ClassName, "alternate", "prefix", SIL.Motif.Runner.Retirement.AllomorphRetirementSemanticDigest.Compute(cache, form),
            null)).ToArray();
        return (cache.ProjectId.Path, RetireAllomorphCodec.ToJson(new RetireAllomorphIntentDocument(
            new RetireAllomorphIntent(entryId, AllomorphRetirementScope.Affix, [identities[0]], [], [], []),
            [identities[1]])));
    }
}

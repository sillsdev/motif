using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Model.DryRun;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Runner.DryRun;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Composers;

[Collection(LcmCacheParallelCollections.Group0)]
public sealed class EditAllomorphConditionComposerTests : IDisposable
{
    private readonly LcmCache _cache;
    private readonly ILexEntry _entry;
    private readonly IMoStemAllomorph _stem;
    private readonly IPhEnvironment _firstEnvironment;
    private readonly IPhEnvironment _secondEnvironment;
    private readonly IPhEnvironment _thirdEnvironment;

    public EditAllomorphConditionComposerTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        _entry = _cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(pristine.Seed.FirstEntryId);
        _stem = Assert.IsAssignableFrom<IMoStemAllomorph>(_entry.LexemeFormOA);
        IPhEnvironment first = null!;
        IPhEnvironment second = null!;
        IPhEnvironment third = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            first = AddEnvironment("first");
            second = AddEnvironment("second");
            third = AddEnvironment("third");
        });
        _firstEnvironment = first;
        _secondEnvironment = second;
        _thirdEnvironment = third;
    }

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [Fact]
    public void PhoneEnvUsesAnOrSetAndChangesOnlyTheSelectedSharedUser()
    {
        var other = CreateStemAlternate();
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            _stem.PhoneEnvRC.Add(_firstEnvironment);
            other.PhoneEnvRC.Add(_firstEnvironment);
        });
        var intent = new EditAllomorphConditionIntent(Id(_stem), AllomorphConditionField.PhoneEnv,
            [Id(_firstEnvironment)], [Id(_thirdEnvironment), Id(_secondEnvironment)]);

        var operations = EditAllomorphConditionComposer.Build(_cache, intent);

        Assert.Equal(3, operations.Count);
        Assert.Contains(operations, operation => operation.Kind == MoStemAllomorphPhoneEnvOperationKinds.RemoveRefPhoneEnv);
        Assert.Equal(2, operations.Count(operation =>
            operation.Kind == MoStemAllomorphPhoneEnvOperationKinds.AddRefPhoneEnv));
        Assert.All(operations, operation => Assert.Equal(Id(_stem), operation.Target));
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () => Apply(operations));

        Assert.Equal(new[] { Id(_secondEnvironment), Id(_thirdEnvironment) }.OrderBy(id => id.Value),
            _stem.PhoneEnvRC.Select(item => Id(item)).OrderBy(id => id.Value));
        Assert.Equal([_firstEnvironment.Guid], other.PhoneEnvRC.Select(item => item.Guid));
        Assert.Contains(_firstEnvironment, _cache.LangProject.PhonologicalDataOA.EnvironmentsOS);
    }

    [Fact]
    public void PositionUsesTheAffixSequenceAndLeavesItsPhoneEnvCollectionAlone()
    {
        var affix = CreateAffixAlternate();
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            affix.PositionRS.Add(_firstEnvironment);
            affix.PositionRS.Add(_secondEnvironment);
            affix.PhoneEnvRC.Add(_thirdEnvironment);
        });
        var intent = new EditAllomorphConditionIntent(Id(affix), AllomorphConditionField.Position,
            [Id(_firstEnvironment), Id(_secondEnvironment)], [Id(_secondEnvironment), Id(_firstEnvironment)]);

        var operation = Assert.Single(EditAllomorphConditionComposer.Build(_cache, intent));

        Assert.Equal(MoAffixAllomorphPositionOperationKinds.MovePosition, operation.Kind);
        Assert.Equal(Id(affix), operation.Target);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () => Apply([operation]));
        Assert.Equal([_secondEnvironment.Guid, _firstEnvironment.Guid], affix.PositionRS.Select(item => item.Guid));
        Assert.Equal([_thirdEnvironment.Guid], affix.PhoneEnvRC.Select(item => item.Guid));
    }

    [Fact]
    public void StaleCurrentConditionsAndWrongAllomorphFieldsAreRefused()
    {
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () => _stem.PhoneEnvRC.Add(_firstEnvironment));
        var stale = Assert.Throws<InvalidOperationException>(() => EditAllomorphConditionComposer.Build(_cache,
            new EditAllomorphConditionIntent(Id(_stem), AllomorphConditionField.PhoneEnv, [], [Id(_secondEnvironment)])));
        Assert.Contains("differ from the expected list", stale.Message, StringComparison.Ordinal);

        var unsupported = Assert.Throws<InvalidOperationException>(() => EditAllomorphConditionComposer.Build(_cache,
            new EditAllomorphConditionIntent(Id(_stem), AllomorphConditionField.Position, [], [])));
        Assert.Contains("must be an affix allomorph", unsupported.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConditionParserKeepsPhoneEnvUnorderedAndPositionOrdered()
    {
        var first = Id(_firstEnvironment).Value;
        var second = Id(_secondEnvironment).Value;
        using var phoneEnv = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            target = Id(_stem).Value,
            field = "phoneEnv",
            expectedEnvironments = new[] { second, first },
            environments = new[] { second, first },
        }));
        using var position = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            target = Id(CreateAffixAlternate()).Value,
            field = "position",
            expectedEnvironments = new[] { second, first },
            environments = new[] { second, first },
        }));

        var setIntent = EditAllomorphConditionIntentParser.Parse(phoneEnv.RootElement);
        var sequenceIntent = EditAllomorphConditionIntentParser.Parse(position.RootElement);

        Assert.Equal(new[] { first, second }.Order(StringComparer.Ordinal),
            setIntent.ExpectedEnvironments.Select(id => id.Value));
        Assert.Equal(new[] { second, first }, sequenceIntent.ExpectedEnvironments.Select(id => id.Value));
    }

    [Fact]
    public void ConditionIntentRefusesAnUnmodeledContextGraph()
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            target = Id(_stem).Value,
            field = "phoneEnv",
            expectedEnvironments = Array.Empty<string>(),
            environments = Array.Empty<string>(),
            contextGraph = new { sequence = new[] { "anything" } },
        }));

        Assert.Throws<SIL.Motif.Contract.Parsing.ContractParseException>(() =>
            EditAllomorphConditionIntentParser.Parse(document.RootElement));
    }

    [Fact]
    public void OrderAllomorphsMovesOnlyExistingAlternatesAndRefusesLexemeForm()
    {
        var first = CreateStemAlternate();
        var second = CreateStemAlternate();
        var current = _entry.AlternateFormsOS.Select(form => Id(form)).ToArray();
        var requested = current.Reverse().ToArray();
        var intent = new OrderAllomorphsIntent(Id(_entry), current, requested);

        var operation = Assert.Single(OrderAllomorphsComposer.Build(_cache, intent));

        Assert.Equal(LexEntryAlternateFormsOperationKinds.MoveAlternateForms, operation.Kind);
        Assert.Equal(Id(_entry), operation.Target);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () => Apply([operation]));
        Assert.Equal(requested.Select(id => id.ToGuid()), _entry.AlternateFormsOS.Select(form => form.Guid));

        var lexemeError = Assert.Throws<InvalidOperationException>(() => OrderAllomorphsComposer.Build(_cache,
            new OrderAllomorphsIntent(Id(_entry), requested,
                [.. requested, Id(_entry.LexemeFormOA!)])));
        Assert.Contains("lexeme form is not a movable", lexemeError.Message, StringComparison.Ordinal);
        Assert.Contains(first, _entry.AlternateFormsOS);
        Assert.Contains(second, _entry.AlternateFormsOS);
    }

    [Fact]
    public void StaleAlternateOrderIsRefused()
    {
        var form = CreateStemAlternate();
        var stale = Assert.Throws<InvalidOperationException>(() => OrderAllomorphsComposer.Build(_cache,
            new OrderAllomorphsIntent(Id(_entry), [], [Id(form)])));

        Assert.Contains("differ from the expected order", stale.Message, StringComparison.Ordinal);
    }

    private IPhEnvironment AddEnvironment(string name)
    {
        var environment = _cache.ServiceLocator.GetInstance<IPhEnvironmentFactory>().Create();
        _cache.LangProject.PhonologicalDataOA.EnvironmentsOS.Add(environment);
        environment.Name.set_String(_cache.DefaultAnalWs, name);
        return environment;
    }

    private IMoStemAllomorph CreateStemAlternate()
    {
        IMoStemAllomorph form = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            form = _cache.ServiceLocator.GetInstance<IMoStemAllomorphFactory>().Create();
            _entry.AlternateFormsOS.Add(form);
            form.MorphTypeRA = _entry.LexemeFormOA!.MorphTypeRA;
            form.Form.set_String(_cache.DefaultVernWs, "alternate");
        });
        return form;
    }

    private IMoAffixAllomorph CreateAffixAlternate()
    {
        IMoAffixAllomorph form = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            form = _cache.ServiceLocator.GetInstance<IMoAffixAllomorphFactory>().Create();
            _entry.AlternateFormsOS.Add(form);
            form.MorphTypeRA = _cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>()
                .GetObject(MoMorphTypeTags.kguidMorphSuffix);
            form.Form.set_String(_cache.DefaultVernWs, "suffix");
        });
        return form;
    }

    private void Apply(IEnumerable<OperationEnvelope> operations)
    {
        foreach (var operation in operations)
            OperationHandlerRegistry.Resolve(operation.Kind, "allomorph composer tests")
                .ApplyAndCaptureEffect(_cache, operation, []);
    }

    private static CanonicalId Id(ICmObject value) => CanonicalId.FromGuid(value.Guid);
}

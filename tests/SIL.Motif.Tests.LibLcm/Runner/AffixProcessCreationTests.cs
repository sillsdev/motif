using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Tests.Samples;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Runner;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class AffixProcessCreationTests(PristineProjectFixture pristine)
{
    [Fact]
    public void ProcessPayload_CreatesEmptyConcreteProcessWithoutFactoryDefaults()
    {
        using var cache = pristine.NewScratch();
        IMoAffixProcess process = null!;
        UndoableUnitOfWorkHelper.Do("setup", "setup", cache.ServiceLocator.GetInstance<IActionHandler>(), () =>
        {
            var entry = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create();
            var id = CanonicalId.Mint();
            var operation = new OperationEnvelope(CanonicalId.Mint(), LexEntryLexemeFormOperationKinds.CreateLexemeForm,
                entityId: id, target: CanonicalId.FromGuid(entry.Guid), after: JsonSerializer.SerializeToElement(new
                {
                    @class = "MoAffixProcess", morphType = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphPrefix).Value,
                }));
            OperationHandlerRegistry.Resolve(operation.Kind, "test").ApplyAndCaptureEffect(cache, operation, []);
            process = Assert.IsAssignableFrom<IMoAffixProcess>(entry.LexemeFormOA);
            Assert.Equal(id.ToGuid(), process.Guid);
            Assert.False(process.IsAbstract);
            Assert.Empty(process.InputOS);
            Assert.Empty(process.OutputOS);
            Assert.Equal(0, process.Form.StringCount);
        });
    }

    [Fact]
    public void LexemeFormIdentityVectorsMatchActualSchemaAndPayloadParsing()
    {
        using var schemaStream = typeof(CanonicalId).Assembly.GetManifestResourceStream(
            "SIL.Motif.Contract.Schemas.create-lexeme-form.schema.json");
        using var schema = JsonDocument.Parse(schemaStream!);
        using var vectorStream = typeof(CanonicalId).Assembly.GetManifestResourceStream(
            "SIL.Motif.Contract.Schemas.create-lexeme-form.vectors.json");
        using var vectors = JsonDocument.Parse(vectorStream!);
        using var cache = pristine.NewScratch();
        foreach (var vector in vectors.RootElement.GetProperty("vectors").EnumerateArray())
        {
            var payload = vector.GetProperty("payload");
            var accept = vector.GetProperty("accept").GetBoolean();
            Assert.Equal(accept, SampleJsonSchemaValidator.Validate(payload, schema.RootElement).Count == 0);
            var exception = Record.Exception(() =>
            {
                if (!payload.TryGetProperty("class", out _))
                    LexEntryLexemeFormCreatePayload.Parse(payload);
                else
                    UndoableUnitOfWorkHelper.Do("setup", "setup", cache.ServiceLocator.GetInstance<IActionHandler>(), () =>
                    {
                        var entry = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create();
                        var operation = new OperationEnvelope(CanonicalId.Mint(), LexEntryLexemeFormOperationKinds.CreateLexemeForm,
                            entityId: CanonicalId.Mint(), target: CanonicalId.FromGuid(entry.Guid), after: payload);
                        OperationHandlerRegistry.Resolve(operation.Kind, "test").ApplyAndCaptureEffect(cache, operation, []);
                    });
            });
            if (!accept) Assert.IsType<ContractParseException>(exception);
            else if (payload.TryGetProperty("class", out _))
            {
                Assert.NotNull(exception);
                Assert.IsNotType<ContractParseException>(exception);
            }
            else Assert.Null(exception);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PrefixedMorphTypeCreatesBothLexemeFormBranches(bool process)
    {
        using var cache = pristine.NewScratch();
        UndoableUnitOfWorkHelper.Do("setup", "setup", cache.ServiceLocator.GetInstance<IActionHandler>(), () =>
        {
            var entry = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create();
            var morphType = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphPrefix, "mt:").Value;
            var payload = process ? JsonSerializer.SerializeToElement(new { @class = "MoAffixProcess", morphType }) :
                JsonSerializer.SerializeToElement(new { morphType, ws = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs), text = "x" });
            var operation = new OperationEnvelope(CanonicalId.Mint(), LexEntryLexemeFormOperationKinds.CreateLexemeForm,
                entityId: CanonicalId.Mint(), target: CanonicalId.FromGuid(entry.Guid), after: payload);
            OperationHandlerRegistry.Resolve(operation.Kind, "test").ApplyAndCaptureEffect(cache, operation, []);
            Assert.Equal(process ? "MoAffixProcess" : "MoAffixAllomorph", entry.LexemeFormOA.ClassName);
            Assert.Equal(MoMorphTypeTags.kguidMorphPrefix, entry.LexemeFormOA.MorphTypeRA.Guid);
        });
    }

    [Theory]
    [InlineData("grammar/moAffixProcess/createInput", "PhVariable")]
    [InlineData("grammar/moAffixProcess/createInput", "PhSimpleContextSeg")]
    [InlineData("grammar/moAffixProcess/createInput", "PhSimpleContextNC")]
    [InlineData("grammar/moAffixProcess/createOutput", "MoCopyFromInput")]
    [InlineData("grammar/moAffixProcess/createOutput", "MoInsertPhones")]
    public void ProcessChildren_HaveRegisteredTypedFactories(string kind, string concreteClass)
    {
        using var cache = pristine.NewScratch();
        UndoableUnitOfWorkHelper.Do("setup", "setup", cache.ServiceLocator.GetInstance<IActionHandler>(), () =>
        {
            var entry = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create();
            var process = cache.ServiceLocator.GetInstance<IMoAffixProcessFactory>().Create();
            entry.LexemeFormOA = process;
            process.OutputOS.Clear();
            process.InputOS.Clear();
            var id = CanonicalId.Mint();
            var operation = new OperationEnvelope(CanonicalId.Mint(), kind, entityId: id,
                target: CanonicalId.FromGuid(process.Guid), after: JsonSerializer.SerializeToElement(new { @class = concreteClass }));
            OperationHandlerRegistry.Resolve(kind, "test").ApplyAndCaptureEffect(cache, operation, []);
            var child = cache.ServiceLocator.GetInstance<ICmObjectRepository>().GetObject(id.ToGuid());
            Assert.Equal(concreteClass, child.ClassName);
            Assert.Equal(process.Guid, child.Owner?.Guid);
        });
    }
    [Fact]
    public void ProcessCreationRefusesStorageOverrideAndIdentityCollisions()
    {
        using var cache = pristine.NewScratch();
        UndoableUnitOfWorkHelper.Do("setup", "setup", cache.ServiceLocator.GetInstance<IActionHandler>(), () =>
        {
            var owner = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create();
            var incumbent = cache.ServiceLocator.GetInstance<IMoAffixProcessFactory>().Create();
            owner.LexemeFormOA = incumbent;
            var entry = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create();
            var handler = OperationHandlerRegistry.Resolve(LexEntryLexemeFormOperationKinds.CreateLexemeForm, "test");
            foreach (var collision in new[] { (CanonicalId.FromGuid(incumbent.Guid), "overwrite/reuse"),
                         (CanonicalId.FromGuid(owner.Guid), "semantic conflict") })
            {
                var exception = Assert.Throws<InvalidOperationException>(() => handler.ApplyAndCaptureEffect(cache,
                    Create(entry, collision.Item1), []));
                Assert.Contains(collision.Item2, exception.Message, StringComparison.Ordinal);
            }
            var normal = Create(entry, CanonicalId.Mint());
            var overridden = new OperationEnvelope(normal.OperationId, normal.Kind, entityId: normal.EntityId,
                target: normal.Target, after: normal.After, storageIdOverride: CanonicalId.Mint());
            var refusal = Assert.Throws<InvalidOperationException>(() => handler.ApplyAndCaptureEffect(cache, overridden, []));
            Assert.Contains("Storage GUID overrides", refusal.Message, StringComparison.Ordinal);
            Assert.Null(entry.LexemeFormOA);
        });
    }

    [Fact]
    public void OccupiedSlotsAndOrdinaryReplacementOrDeleteOfProcessAreRefused()
    {
        using var cache = pristine.NewScratch();
        UndoableUnitOfWorkHelper.Do("setup", "setup", cache.ServiceLocator.GetInstance<IActionHandler>(), () =>
        {
            var entry = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create();
            var process = cache.ServiceLocator.GetInstance<IMoAffixProcessFactory>().Create();
            entry.LexemeFormOA = process;
            var count = process.InputOS.Count;
            var createHandler = OperationHandlerRegistry.Resolve(LexEntryLexemeFormOperationKinds.CreateLexemeForm, "test");
            var occupied = Assert.Throws<InvalidOperationException>(() => createHandler.ApplyAndCaptureEffect(cache,
                Create(entry, CanonicalId.Mint()), []));
            Assert.Contains("occupied", occupied.Message, StringComparison.Ordinal);
            Assert.Throws<InvalidOperationException>(() => LexEntryLexemeFormCreateLowering.Apply(cache, entry,
                Guid.NewGuid(), CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphPrefix),
                cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs), "x"));
            Assert.Throws<InvalidOperationException>(() => LexEntryLexemeFormDeleteLowering.Apply(entry));
            Assert.Equal(process.Guid, entry.LexemeFormOA.Guid);
            Assert.Equal(count, process.InputOS.Count);
        });
    }

    [Theory]
    [InlineData("PhSequenceContext", "grammar/moAffixProcess/createInput")]
    [InlineData("PhIterationContext", "grammar/moAffixProcess/createInput")]
    [InlineData("PhSimpleContextBdry", "grammar/moAffixProcess/createInput")]
    [InlineData("MoInsertNC", "grammar/moAffixProcess/createOutput")]
    [InlineData("MoModifyFromInput", "grammar/moAffixProcess/createOutput")]
    public void UnsupportedChildClassesAreRefused(string concreteClass, string kind)
    {
        using var cache = pristine.NewScratch();
        UndoableUnitOfWorkHelper.Do("setup", "setup", cache.ServiceLocator.GetInstance<IActionHandler>(), () =>
        {
            var entry = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create();
            var process = cache.ServiceLocator.GetInstance<IMoAffixProcessFactory>().Create();
            entry.LexemeFormOA = process;
            var operation = new OperationEnvelope(CanonicalId.Mint(), kind, entityId: CanonicalId.Mint(),
                target: CanonicalId.FromGuid(process.Guid), after: JsonSerializer.SerializeToElement(new { @class = concreteClass }));
            Assert.Throws<InvalidOperationException>(() => OperationHandlerRegistry.Resolve(kind, "test")
                .ApplyAndCaptureEffect(cache, operation, []));
        });
    }

    [Theory]
    [InlineData("{\"class\":\"MoAffixProcess\",\"morphType\":\"AAAAAAAAAAAAAAAAAAAAAA\",\"ws\":\"en\"}")]
    [InlineData("{\"class\":\"MoAffixProcess\",\"morphType\":null}")]
    [InlineData("{\"class\":\"MoAffixProcess\",\"class\":\"MoAffixProcess\",\"morphType\":\"AAAAAAAAAAAAAAAAAAAAAA\"}")]
    public void ProcessPayloadIsClosedAndRejectsNullAndDuplicateFields(string json)
    {
        using var cache = pristine.NewScratch();
        using var document = JsonDocument.Parse(json);
        UndoableUnitOfWorkHelper.Do("setup", "setup", cache.ServiceLocator.GetInstance<IActionHandler>(), () =>
        {
            var entry = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create();
            var operation = new OperationEnvelope(CanonicalId.Mint(), LexEntryLexemeFormOperationKinds.CreateLexemeForm,
                entityId: CanonicalId.Mint(), target: CanonicalId.FromGuid(entry.Guid), after: document.RootElement);
            Assert.Throws<SIL.Motif.Contract.Parsing.ContractParseException>(() => OperationHandlerRegistry
                .Resolve(operation.Kind, "test").ApplyAndCaptureEffect(cache, operation, []));
            Assert.Null(entry.LexemeFormOA);
        });
    }

    [Fact]
    public void OrdinaryReplacementBesideNestedProcessAlternateRetainsLegacyBehavior()
    {
        using var cache = pristine.NewScratch();
        ILexEntry entry = null!;
        IMoAffixProcess alternate = null!;
        UndoableUnitOfWorkHelper.Do("setup", "setup", cache.ServiceLocator.GetInstance<IActionHandler>(), () =>
        {
            entry = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create();
            entry.LexemeFormOA = cache.ServiceLocator.GetInstance<IMoAffixAllomorphFactory>().Create();
            alternate = cache.ServiceLocator.GetInstance<IMoAffixProcessFactory>().Create();
            entry.AlternateFormsOS.Add(alternate);
            alternate.OutputOS.Clear();
            alternate.InputOS.Clear();
            alternate.InputOS.Add(cache.ServiceLocator.GetInstance<IPhSequenceContextFactory>().Create());
        });
        var proposal = new Proposal(new Dictionary<string, string> { ["lexical"] = "1.0" }, CanonicalId.Mint(), null,
            [new OperationEnvelope(CanonicalId.Mint(), LexEntryLexemeFormOperationKinds.CreateLexemeForm,
                entityId: CanonicalId.Mint(), target: CanonicalId.FromGuid(entry.Guid),
                after: JsonSerializer.SerializeToElement(new
                {
                    morphType = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphPrefix).Value,
                    ws = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs), text = "ordinary",
                }))]);
        var dryRun = ScratchDryRun.Of(cache, proposal);
        SIL.Motif.Runner.Apply.ProposalApplier.Apply(cache, proposal, dryRun.Anchor, "affix-core-tests");
        Assert.IsAssignableFrom<IMoAffixAllomorph>(entry.LexemeFormOA);
        Assert.Equal(alternate.Guid, Assert.Single(entry.AlternateFormsOS).Guid);
    }

    [Fact]
    public void ZeroProcessIdentityIsRefusedBeforeAttachingAnything()
    {
        using var cache = pristine.NewScratch();
        UndoableUnitOfWorkHelper.Do("setup", "setup", cache.ServiceLocator.GetInstance<IActionHandler>(), () =>
        {
            var entry = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create();
            var operation = Create(entry, CanonicalId.FromGuid(Guid.Empty));
            var failure = Assert.Throws<InvalidOperationException>(() => OperationHandlerRegistry.Resolve(operation.Kind, "test")
                .ApplyAndCaptureEffect(cache, operation, []));
            Assert.Contains("zero", failure.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Null(entry.LexemeFormOA);
        });
    }

    [Theory]
    [InlineData("grammar/moAffixProcess/createInput", "PhVariable")]
    [InlineData("grammar/moAffixProcess/createOutput", "MoCopyFromInput")]
    public void ZeroChildIdentityIsRefusedBeforeChangingOwnership(string kind, string concreteClass)
    {
        using var cache = pristine.NewScratch();
        UndoableUnitOfWorkHelper.Do("setup", "setup", cache.ServiceLocator.GetInstance<IActionHandler>(), () =>
        {
            var entry = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create();
            var process = cache.ServiceLocator.GetInstance<IMoAffixProcessFactory>().Create();
            entry.LexemeFormOA = process;
            process.OutputOS.Clear();
            process.InputOS.Clear();
            var operation = new OperationEnvelope(CanonicalId.Mint(), kind, entityId: CanonicalId.FromGuid(Guid.Empty),
                target: CanonicalId.FromGuid(process.Guid), after: JsonSerializer.SerializeToElement(new { @class = concreteClass }));
            var failure = Assert.Throws<InvalidOperationException>(() => OperationHandlerRegistry.Resolve(kind, "test")
                .ApplyAndCaptureEffect(cache, operation, []));
            Assert.Contains("zero", failure.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(process.InputOS);
            Assert.Empty(process.OutputOS);
        });
    }

    [Fact]
    public void LibLcmZeroGuidFactoriesSubstituteFreshIdentity()
    {
        using var cache = pristine.NewScratch();
        UndoableUnitOfWorkHelper.Do("setup", "setup", cache.ServiceLocator.GetInstance<IActionHandler>(), () =>
        {
            var entry = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create();
            var process = cache.ServiceLocator.GetInstance<IMoAffixProcessFactory>().Create(Guid.Empty);
            entry.LexemeFormOA = process;
            process.OutputOS.Clear();
            process.InputOS.Clear();
            var input = cache.ServiceLocator.GetInstance<IPhVariableFactory>().Create(Guid.Empty);
            process.InputOS.Add(input);
            var output = cache.ServiceLocator.GetInstance<IMoCopyFromInputFactory>().Create(Guid.Empty);
            process.OutputOS.Add(output);
            Assert.NotEqual(Guid.Empty, process.Guid);
            Assert.NotEqual(Guid.Empty, input.Guid);
            Assert.NotEqual(Guid.Empty, output.Guid);
        });
    }

    private static OperationEnvelope Create(ILexEntry entry, CanonicalId identity) => new(CanonicalId.Mint(),
        LexEntryLexemeFormOperationKinds.CreateLexemeForm, entityId: identity,
        target: CanonicalId.FromGuid(entry.Guid), after: JsonSerializer.SerializeToElement(new
        { @class = "MoAffixProcess", morphType = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphPrefix).Value }));

}

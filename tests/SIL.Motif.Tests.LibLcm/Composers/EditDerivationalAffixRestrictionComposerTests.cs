using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Runner.Snapshotting;
using SIL.Motif.Tests.Samples;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using static SIL.Motif.Tests.Composers.AffixTestSupport;

namespace SIL.Motif.Tests.Composers;

/// <summary>Restricting the input of a derivational affix through its grammatical info.</summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class EditDerivationalAffixRestrictionComposerTests : IDisposable
{
    private readonly LcmCache _cache;
    private readonly CanonicalId _category;

    public EditDerivationalAffixRestrictionComposerTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        _category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
    }

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [Fact]
    public void TheClassIsSetOnTheDerivationalInfoAndNothingElseChanges()
    {
        var inflectionClass = AddInflectionClass(_cache, _category, "Class 1");
        var (entry, msa) = DerivationalPrefix("mi");
        var before = SoundSystemSnapshots.Read(_cache);

        var operations = EditDerivationalAffixRestrictionComposer.Build(_cache, new(msa, FromInflectionClass: inflectionClass));

        var operation = Assert.Single(operations);
        Assert.Equal(MoDerivAffMsaFromInflectionClassOperationKinds.SetFromInflectionClass, operation.Kind);
        Assert.Contains("EditDerivationalAffixRestriction", operation.Rationale!, StringComparison.Ordinal);
        var proposal = Proposal(operations);
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        Assert.Empty(SoundSystemSnapshots.Compare(before, SoundSystemSnapshots.Read(_cache)));
        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "deriv-restriction-tests");
        var reloaded = Get<IMoDerivAffMsa>(_cache, msa);
        Assert.Equal(inflectionClass.ToGuid(), reloaded.FromInflectionClassRA!.Guid);
        Assert.Empty(Get<ILexEntry>(_cache, entry).AlternateFormsOS);
        Assert.Empty(Assert.IsAssignableFrom<IMoAffixAllomorph>(Get<ILexEntry>(_cache, entry).LexemeFormOA).InflectionClassesRC);
        Assert.Empty(reloaded.FromProdRestrictRC);
    }

    [Fact]
    public void TheClassCanBeReplacedAndExplicitlyClearedWhileOmissionLeavesItUnchanged()
    {
        var firstClass = AddInflectionClass(_cache, _category, "Class 1");
        var secondClass = AddInflectionClass(_cache, _category, "Class 2");
        var (_, msa) = DerivationalPrefix("mi");

        ApplyOps(_cache, EditDerivationalAffixRestrictionComposer.Build(_cache,
            new(msa, FromInflectionClass: firstClass)));
        Assert.Equal(firstClass.ToGuid(), Get<IMoDerivAffMsa>(_cache, msa).FromInflectionClassRA!.Guid);

        Assert.Empty(EditDerivationalAffixRestrictionComposer.Build(_cache,
            new(msa, FromExceptionFeatures: [])));
        Assert.Equal(firstClass.ToGuid(), Get<IMoDerivAffMsa>(_cache, msa).FromInflectionClassRA!.Guid);

        var replace = EditDerivationalAffixRestrictionComposer.Build(_cache,
            new(msa, FromInflectionClass: secondClass));
        Assert.Equal(MoDerivAffMsaFromInflectionClassOperationKinds.SetFromInflectionClass,
            Assert.Single(replace).Kind);
        ApplyOps(_cache, replace);
        Assert.Equal(secondClass.ToGuid(), Get<IMoDerivAffMsa>(_cache, msa).FromInflectionClassRA!.Guid);

        var clear = EditDerivationalAffixRestrictionComposer.Build(_cache,
            new(msa, ClearFromInflectionClass: true));
        var clearOperation = Assert.Single(clear);
        Assert.Equal(MoDerivAffMsaFromInflectionClassOperationKinds.ClearFromInflectionClass, clearOperation.Kind);
        Assert.Empty(clearOperation.After!.Value.EnumerateObject());
        ApplyOps(_cache, clear);
        Assert.Null(Get<IMoDerivAffMsa>(_cache, msa).FromInflectionClassRA);
    }

    [Fact]
    public void ExceptionFeaturesAreReplacedAndUnchangedLinksAreSkipped()
    {
        var features = ExceptionFeatures("Irregular", "Loan", "Travel");
        var (_, msa) = DerivationalPrefix("mi");
        ApplyOps(_cache, EditDerivationalAffixRestrictionComposer.Build(_cache,
            new(msa, FromExceptionFeatures: [features[0], features[1]])));

        var operations = EditDerivationalAffixRestrictionComposer.Build(_cache,
            new(msa, FromExceptionFeatures: [features[0], features[2]]));

        var remove = Assert.Single(operations, operation =>
            operation.Kind == MoDerivAffMsaFromProdRestrictOperationKinds.RemoveRefFromProdRestrict);
        var add = Assert.Single(operations, operation =>
            operation.Kind == MoDerivAffMsaFromProdRestrictOperationKinds.AddRefFromProdRestrict);
        Assert.Equal(features[1].Value, remove.After!.Value.GetProperty("member").GetString());
        Assert.Equal(features[2].Value, add.After!.Value.GetProperty("member").GetString());
        Assert.Equal(msa, remove.Target);
        ApplyOps(_cache, operations);
        Assert.Equal(new[] { features[0], features[2] }.OrderBy(id => id.Value, StringComparer.Ordinal),
            Get<IMoDerivAffMsa>(_cache, msa).FromProdRestrictRC.Select(item => Id(item)).OrderBy(id => id.Value, StringComparer.Ordinal));
        Assert.Empty(EditDerivationalAffixRestrictionComposer.Build(_cache,
            new(msa, FromExceptionFeatures: [features[0], features[2]])));
    }

    [Fact]
    public void ClassAndFeaturesTogetherLowerToOneSetAndOneAddPerFeature()
    {
        var inflectionClass = AddInflectionClass(_cache, _category, "Class 1");
        var features = ExceptionFeatures("Irregular", "Loan");
        var (_, msa) = DerivationalPrefix("mi");

        var operations = EditDerivationalAffixRestrictionComposer.Build(_cache,
            new(msa, inflectionClass, features));

        Assert.Equal(1, operations.Count(operation => operation.Kind == MoDerivAffMsaFromInflectionClassOperationKinds.SetFromInflectionClass));
        Assert.Equal(2, operations.Count(operation => operation.Kind == MoDerivAffMsaFromProdRestrictOperationKinds.AddRefFromProdRestrict));
    }

    [Fact]
    public void AnEmptyExceptionFeatureSetRemovesEveryFeature()
    {
        var feature = Assert.Single(ExceptionFeatures("Irregular"));
        var (_, msa) = DerivationalPrefix("mi");
        ApplyOps(_cache, EditDerivationalAffixRestrictionComposer.Build(_cache, new(msa, FromExceptionFeatures: [feature])));

        var operations = EditDerivationalAffixRestrictionComposer.Build(_cache, new(msa, FromExceptionFeatures: []));

        var remove = Assert.Single(operations);
        Assert.Equal(MoDerivAffMsaFromProdRestrictOperationKinds.RemoveRefFromProdRestrict, remove.Kind);
        Assert.Equal(feature.Value, remove.After!.Value.GetProperty("member").GetString());
        ApplyOps(_cache, operations);
        Assert.Empty(Get<IMoDerivAffMsa>(_cache, msa).FromProdRestrictRC);
    }

    [Fact]
    public void ARepeatOfTheCurrentRestrictionIsANoOp()
    {
        var inflectionClass = AddInflectionClass(_cache, _category, "Class 1");
        var (_, msa) = DerivationalPrefix("mi");
        ApplyOps(_cache, EditDerivationalAffixRestrictionComposer.Build(_cache, new(msa, FromInflectionClass: inflectionClass)));

        Assert.Empty(EditDerivationalAffixRestrictionComposer.Build(_cache,
            new(msa, FromInflectionClass: inflectionClass)));
    }

    [Fact]
    public void AnInflectionalInfoIsRefusedAndTheMessageNamesTheInflectionalEditor()
    {
        var (_, inflectionalMsa, _) = AddInflectionalPrefix(_cache, "ku", _category);
        var inflectionClass = AddInflectionClass(_cache, _category, "Class 1");

        var failure = Assert.Throws<InvalidOperationException>(() =>
            EditDerivationalAffixRestrictionComposer.Build(_cache, new(inflectionalMsa, FromInflectionClass: inflectionClass)));

        Assert.Contains("not the derivational grammatical info", failure.Message, StringComparison.Ordinal);
        Assert.Contains("compose-edit-affix-inflection-classes", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InflectionalClassEditorRefusesADerivationalInfoAndNamesThisComposer()
    {
        var (entry, _) = DerivationalPrefix("mi");
        var form = Id(Get<ILexEntry>(_cache, entry).LexemeFormOA);

        var failure = Assert.Throws<InvalidOperationException>(() =>
            EditAffixInflectionClassesComposer.Build(_cache, new(form, [], [])));

        Assert.Contains("compose-edit-derivational-affix-restriction", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InflectionalClassEditorRedirectsSeveralDerivationalMsasToThisComposer()
    {
        var (entryId, _) = DerivationalPrefix("mi");
        var entry = Get<ILexEntry>(_cache, entryId);
        var original = Assert.IsAssignableFrom<IMoDerivAffMsa>(Assert.Single(entry.MorphoSyntaxAnalysesOC));
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var additional = _cache.ServiceLocator.GetInstance<IMoDerivAffMsaFactory>().Create();
            entry.MorphoSyntaxAnalysesOC.Add(additional);
            additional.FromPartOfSpeechRA = original.FromPartOfSpeechRA;
            additional.ToPartOfSpeechRA = original.ToPartOfSpeechRA;
        });
        var form = Id(entry.LexemeFormOA);

        var failure = Assert.Throws<InvalidOperationException>(() =>
            EditAffixInflectionClassesComposer.Build(_cache, new(form, [], [])));

        Assert.Contains("compose-edit-derivational-affix-restriction", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("mixed", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AClassOfAnotherCategoryIsRefused()
    {
        var other = AddCategory(_cache, "Adjective");
        var foreign = AddInflectionClass(_cache, other, "Adjective class");
        var (_, msa) = DerivationalPrefix("mi");

        var failure = Assert.Throws<InvalidOperationException>(() =>
            EditDerivationalAffixRestrictionComposer.Build(_cache, new(msa, FromInflectionClass: foreign)));

        Assert.Contains("does not belong to the category", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnIdThatIsNotAnExceptionFeatureIsRefused()
    {
        var (_, msa) = DerivationalPrefix("mi");

        var failure = Assert.Throws<InvalidOperationException>(() =>
            EditDerivationalAffixRestrictionComposer.Build(_cache, new(msa, FromExceptionFeatures: [_category])));

        Assert.Contains("not one of the project's exception features", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnIntentWithNeitherFieldIsRefusedByTheParserAndTheBuilder()
    {
        var (_, msa) = DerivationalPrefix("mi");
        using var json = JsonDocument.Parse($$"""{"msa":"{{msa.Value}}"}""");

        Assert.Throws<ContractParseException>(() => EditDerivationalAffixRestrictionIntentParser.Parse(json.RootElement));
        Assert.Throws<InvalidOperationException>(() => EditDerivationalAffixRestrictionComposer.Build(_cache, new(msa)));
    }

    [Fact]
    public void ClearMustBeTrueAndCannotBeCombinedWithAReplacementClass()
    {
        var (_, msa) = DerivationalPrefix("mi");
        var inflectionClass = AddInflectionClass(_cache, _category, "Class 1");
        Assert.Throws<InvalidOperationException>(() => EditDerivationalAffixRestrictionComposer.Build(_cache,
            new(msa, inflectionClass, ClearFromInflectionClass: true)));
        Assert.Throws<InvalidOperationException>(() => EditDerivationalAffixRestrictionComposer.Build(_cache,
            new(msa, ClearFromInflectionClass: false)));
    }

    [Fact]
    public void ParserAcceptsOnlyTheClosedShape()
    {
        using var stream = typeof(CanonicalId).Assembly.GetManifestResourceStream(
            "SIL.Motif.Contract.Schemas.edit-derivational-affix-restriction.vectors.json");
        using var vectors = JsonDocument.Parse(stream!);
        using var schemaStream = typeof(CanonicalId).Assembly.GetManifestResourceStream(
            "SIL.Motif.Contract.Schemas.edit-derivational-affix-restriction.schema.json");
        using var schema = JsonDocument.Parse(schemaStream!);
        foreach (var vector in vectors.RootElement.GetProperty("vectors").EnumerateArray())
        {
            var input = vector.GetProperty("intent");
            var exception = Record.Exception(() => EditDerivationalAffixRestrictionIntentParser.Parse(input));
            if (vector.GetProperty("accept").GetBoolean()) Assert.Null(exception);
            else Assert.IsType<ContractParseException>(exception);
            if (vector.TryGetProperty("schemaAccept", out var schemaAccept))
                Assert.True(schemaAccept.GetBoolean() ==
                    (SampleJsonSchemaValidator.Validate(input, schema.RootElement).Count == 0), vector.GetProperty("name").GetString());
        }
    }

    private (CanonicalId Entry, CanonicalId Msa) DerivationalPrefix(string form)
    {
        var derived = AddCategory(_cache, "Adjective");
        var proposal = ApplyOps(_cache, AuthorLexicalEntryComposer.Build(_cache, new AuthorLexicalEntryIntent(form, Vern(_cache),
            MorphType(MoMorphTypeTags.kguidMorphPrefix), form, Anal(_cache),
            new LexicalEntryMsa(LexicalEntryMsaKind.Derivational, _category, derived))));
        return (Created(proposal, LexDbEntriesOperationKinds.Create),
            Created(proposal, LexEntryMorphoSyntaxAnalysesOperationKinds.Create));
    }

    private CanonicalId[] ExceptionFeatures(params string[] names)
    {
        var ids = new List<CanonicalId>();
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var morphology = _cache.LangProject.MorphologicalDataOA;
            if (morphology.ProdRestrictOA is null)
                morphology.ProdRestrictOA = _cache.ServiceLocator.GetInstance<ICmPossibilityListFactory>().Create();
            foreach (var name in names)
            {
                var feature = _cache.ServiceLocator.GetInstance<ICmPossibilityFactory>().Create();
                morphology.ProdRestrictOA.PossibilitiesOS.Add(feature);
                feature.Name.set_String(_cache.DefaultAnalWs, name);
                ids.Add(Id(feature));
            }
        });
        return ids.ToArray();
    }
}

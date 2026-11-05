using System.Text;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using static SIL.Motif.Tests.Composers.AffixTestSupport;

namespace SIL.Motif.Tests.Composers;

[Collection(LcmCacheParallelCollections.Group0)]
public sealed class EditUnderlyingFormsComposerTests(PristineProjectFixture pristine)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AlternateRewritePreservesIdentityConditionsOtherAlternativesAndApprovedReferences(bool affix)
    {
        using var cache = pristine.NewScratch();
        var category = AddCategory(cache, "Word");
        var entryId = affix ? AddInflectionalPrefix(cache, "pa", category).Entry : AddStem(cache, "pata", category).Entry;
        var entry = Get<ILexEntry>(cache, entryId);
        var environment = Get<IPhEnvironment>(cache, AddEnvironment(cache, "Before vowel"));
        var inflectionClass = AddInflectionClass(cache, category, "Selected class");
        IMoForm target = null!;
        IMoForm sibling = null!;
        IMoAlloAdhocProhib prohibition = null!;
        IWfiAnalysis analysis = null!;
        IWfiMorphBundle bundle = null!;
        cache.ServiceLocator.WritingSystemManager.GetOrSet("qaa-x-other", out var otherWs);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            cache.ServiceLocator.WritingSystems.VernacularWritingSystems.Add(otherWs);
            target = affix ? cache.ServiceLocator.GetInstance<IMoAffixAllomorphFactory>().Create()
                : cache.ServiceLocator.GetInstance<IMoStemAllomorphFactory>().Create();
            sibling = affix ? cache.ServiceLocator.GetInstance<IMoAffixAllomorphFactory>().Create()
                : cache.ServiceLocator.GetInstance<IMoStemAllomorphFactory>().Create();
            entry.AlternateFormsOS.Add(target);
            entry.AlternateFormsOS.Add(sibling);
            target.MorphTypeRA = entry.LexemeFormOA.MorphTypeRA;
            sibling.MorphTypeRA = target.MorphTypeRA;
            target.Form.set_String(cache.DefaultVernWs, "páta");
            target.Form.set_String(otherWs.Handle, "another spelling");
            sibling.Form.set_String(cache.DefaultVernWs, "sibling");
            if (target is IMoAffixAllomorph a)
            {
                a.PhoneEnvRC.Add(environment);
                a.PositionRS.Add(environment);
                a.InflectionClassesRC.Add(Get<IMoInflClass>(cache, inflectionClass));
                a.MsEnvFeaturesOA = cache.ServiceLocator.GetInstance<IFsFeatStrucFactory>().Create();
            }
            else ((IMoStemAllomorph)target).PhoneEnvRC.Add(environment);
            prohibition = cache.ServiceLocator.GetInstance<IMoAlloAdhocProhibFactory>().Create();
            cache.LangProject.MorphologicalDataOA.AdhocCoProhibitionsOC.Add(prohibition);
            prohibition.FirstAllomorphRA = target;
            prohibition.RestOfAllosRS.Add(sibling);
            var word = cache.ServiceLocator.GetInstance<IWfiWordformFactory>().Create(
                TsStringUtils.MakeString("written", cache.DefaultVernWs));
            analysis = cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
            word.AnalysesOC.Add(analysis);
            bundle = cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create();
            analysis.MorphBundlesOS.Add(bundle);
            bundle.MorphRA = target;
            bundle.MsaRA = entry.MorphoSyntaxAnalysesOC.Single();
            bundle.SenseRA = entry.SensesOS.FirstOrDefault();
            cache.LangProject.DefaultUserAgent.SetEvaluation(analysis, Opinions.approves);
        });
        var order = entry.AlternateFormsOS.Select(form => form.Guid).ToArray();
        var morphType = target.MorphTypeRA.Guid;
        var msa = bundle.MsaRA.Guid;
        var sense = bundle.SenseRA?.Guid;
        var features = (target as IMoAffixAllomorph)?.MsEnvFeaturesOA.Guid;
        var operations = EditAllomorphFormComposer.Build(cache,
            new(entryId, Id(target), Vern(cache), "pa\u0301ta", "pātaʔ"));
        Assert.Equal(MoFormFormOperationKinds.SetForm, Assert.Single(operations).Kind);
        Assert.Equal("páta".Normalize(NormalizationForm.FormD), target.Form.get_String(cache.DefaultVernWs).Text);
        ApplyOps(cache, operations);
        var loader = new FwDataProjectLoader();
        loader.Save(cache);
        using var reloaded = loader.LoadScratchCache(cache.ProjectId.Path);
        var result = Get<IMoForm>(reloaded, Id(target));
        Assert.Equal("pātaʔ".Normalize(NormalizationForm.FormD), result.Form.get_String(reloaded.DefaultVernWs).Text);
        Assert.Equal("another spelling", result.Form.get_String(reloaded.WritingSystemFactory.GetWsFromStr(otherWs.Id)).Text);
        Assert.False(result.IsAbstract);
        Assert.Equal(morphType, result.MorphTypeRA.Guid);
        Assert.Equal(order, Get<ILexEntry>(reloaded, entryId).AlternateFormsOS.Select(form => form.Guid));
        Assert.Equal("sibling", Get<IMoForm>(reloaded, Id(sibling)).Form.get_String(reloaded.DefaultVernWs).Text);
        var phoneEnv = result is IMoStemAllomorph s ? s.PhoneEnvRC : ((IMoAffixAllomorph)result).PhoneEnvRC;
        Assert.Equal(environment.Guid, Assert.Single(phoneEnv).Guid);
        if (result is IMoAffixAllomorph edited)
        {
            Assert.Equal(environment.Guid, Assert.Single(edited.PositionRS).Guid);
            Assert.Equal(inflectionClass.ToGuid(), Assert.Single(edited.InflectionClassesRC).Guid);
            Assert.Equal(features, edited.MsEnvFeaturesOA.Guid);
        }
        Assert.Equal(target.Guid, Get<IMoAlloAdhocProhib>(reloaded, Id(prohibition)).FirstAllomorphRA.Guid);
        Assert.Equal(sibling.Guid, Assert.Single(Get<IMoAlloAdhocProhib>(reloaded, Id(prohibition)).RestOfAllosRS).Guid);
        var readBundle = Get<IWfiMorphBundle>(reloaded, Id(bundle));
        Assert.Equal(target.Guid, readBundle.MorphRA.Guid);
        Assert.Equal(msa, readBundle.MsaRA.Guid);
        Assert.Equal(sense, readBundle.SenseRA?.Guid);
        Assert.Equal(Opinions.approves,
            Get<IWfiAnalysis>(reloaded, Id(analysis)).GetAgentOpinion(reloaded.LangProject.DefaultUserAgent));
    }

    [Fact]
    public void StaleOwnerTargetWritingSystemAndFormRefuseWithoutMutatingAnything()
    {
        using var cache = pristine.NewScratch();
        var entry = Get<ILexEntry>(cache, CanonicalId.FromGuid(pristine.Seed.FirstEntryId));
        IMoStemAllomorph form = null!;
        IMoAffixProcess process = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            form = cache.ServiceLocator.GetInstance<IMoStemAllomorphFactory>().Create();
            entry.AlternateFormsOS.Add(form);
            form.Form.set_String(cache.DefaultVernWs, "pata");
            process = cache.ServiceLocator.GetInstance<IMoAffixProcessFactory>().Create();
            entry.AlternateFormsOS.Add(process);
        });
        var valid = new EditAllomorphFormIntent(Id(entry), Id(form), Vern(cache), "pata", "pātaʔ");
        var other = AddStem(cache, "other", AddCategory(cache, "Other"));
        foreach (var intent in new[] { valid with { Entry = other.Entry }, valid with { Entry = CanonicalId.Mint() },
                     valid with { Target = CanonicalId.Mint() }, valid with { Target = Id(entry.LexemeFormOA) },
                     valid with { Target = Id(process) }, valid with { WritingSystem = "qaa-x-missing" },
                     valid with { ExpectedForm = "wrong" }, valid with { NewForm = " " } })
            Assert.Throws<InvalidOperationException>(() => EditAllomorphFormComposer.Build(cache, intent));
        Assert.Empty(EditAllomorphFormComposer.Build(cache, valid with { NewForm = "pata" }));
        Assert.Equal("pata", form.Form.get_String(cache.DefaultVernWs).Text);
    }

    [Fact]
    public void ExplicitLexemeRewriteRetainsUnderlyingDistinctionsAndItsFormIdentity()
    {
        using var cache = pristine.NewScratch();
        var entry = AddStem(cache, "pata", AddCategory(cache, "Word"));
        var intent = RewriteLexemeFormIntentParser.Parse(JsonSerializer.SerializeToElement(new
        {
            foldStressMarks = false,
            entries = new[] { new { entryGuid = entry.Entry.Value, writingSystem = Vern(cache),
                expectedForm = "pata", newForm = "pātaʔ" } },
        }));
        ApplyOps(cache, RewriteLexemeFormComposer.Build(cache, intent));
        var result = Get<ILexEntry>(cache, entry.Entry);
        Assert.Equal(entry.Form.ToGuid(), result.LexemeFormOA.Guid);
        Assert.Equal("pātaʔ".Normalize(NormalizationForm.FormD), result.LexemeFormOA.Form.get_String(cache.DefaultVernWs).Text);
        Assert.Equal(entry.Msa.ToGuid(), Assert.Single(result.MorphoSyntaxAnalysesOC).Guid);
    }

    [Fact]
    public void CitationEditKeepsTheSelectedSourceHeadwordSeparateFromParserForms()
    {
        using var cache = pristine.NewScratch();
        var entry = Get<ILexEntry>(cache, CanonicalId.FromGuid(pristine.Seed.FirstEntryId));
        var lexeme = entry.LexemeFormOA;
        var before = lexeme.Form.get_String(cache.DefaultVernWs).Text;
        cache.ServiceLocator.WritingSystemManager.GetOrSet("qaa-x-other", out var otherWs);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            cache.ServiceLocator.WritingSystems.VernacularWritingSystems.Add(otherWs);
            entry.CitationForm.set_String(otherWs.Handle, "other headword");
        });
        var intent = new EditCitationFormIntent(Id(entry), Vern(cache), "", "páta");
        ApplyOps(cache, EditCitationFormComposer.Build(cache, intent));
        Assert.Empty(EditCitationFormComposer.Build(cache, intent with { ExpectedForm = "pa\u0301ta" }));
        Assert.Throws<InvalidOperationException>(() => EditCitationFormComposer.Build(cache, intent));
        Assert.Throws<InvalidOperationException>(() => EditCitationFormComposer.Build(cache, intent with { Target = Id(lexeme) }));
        Assert.Throws<InvalidOperationException>(() => EditCitationFormComposer.Build(cache, intent with { WritingSystem = "qaa-x-missing" }));
        Assert.Throws<InvalidOperationException>(() => EditCitationFormComposer.Build(cache, intent with { NewForm = " " }));
        var loader = new FwDataProjectLoader();
        loader.Save(cache);
        using var reloaded = loader.LoadScratchCache(cache.ProjectId.Path);
        var result = Get<ILexEntry>(reloaded, Id(entry));
        Assert.Equal("páta".Normalize(NormalizationForm.FormD), result.CitationForm.get_String(reloaded.DefaultVernWs).Text);
        Assert.Equal("other headword", result.CitationForm.get_String(reloaded.WritingSystemFactory.GetWsFromStr(otherWs.Id)).Text);
        Assert.Equal(lexeme.Guid, result.LexemeFormOA.Guid);
        Assert.Equal(before, result.LexemeFormOA.Form.get_String(reloaded.DefaultVernWs).Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GuardedClearPersistsOneAlternativeAndAbsentClearIsNoOp(bool citation)
    {
        using var cache = pristine.NewScratch();
        var entry = Get<ILexEntry>(cache, CanonicalId.FromGuid(pristine.Seed.FirstEntryId));
        IMoStemAllomorph form = null!;
        cache.ServiceLocator.WritingSystemManager.GetOrSet("qaa-x-other", out var otherWs);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            cache.ServiceLocator.WritingSystems.VernacularWritingSystems.Add(otherWs);
            form = cache.ServiceLocator.GetInstance<IMoStemAllomorphFactory>().Create();
            entry.AlternateFormsOS.Add(form);
            form.Form.set_String(cache.DefaultVernWs, "páta");
            form.Form.set_String(otherWs.Handle, "other");
            entry.CitationForm.set_String(cache.DefaultVernWs, "páta");
            entry.CitationForm.set_String(otherWs.Handle, "other");
        });
        IReadOnlyList<SIL.Motif.Contract.Model.OperationEnvelope> Build(string expected, string ws) => citation
            ? EditCitationFormComposer.Build(cache, new(Id(entry), ws, expected, ""))
            : EditAllomorphFormComposer.Build(cache, new(Id(entry), Id(form), ws, expected, ""));
        Assert.Throws<InvalidOperationException>(() => Build("stale", Vern(cache)));
        Assert.Throws<InvalidOperationException>(() => Build("páta", "qaa-x-missing"));
        var operations = Build("pa\u0301ta", Vern(cache));
        Assert.Equal(citation ? LexEntryCitationFormOperationKinds.ClearCitationForm : MoFormFormOperationKinds.ClearForm,
            Assert.Single(operations).Kind);
        var selected = citation ? entry.CitationForm : form.Form;
        Assert.Equal("páta".Normalize(NormalizationForm.FormD), selected.get_String(cache.DefaultVernWs).Text);
        ApplyOps(cache, operations);
        Assert.Empty(Build("", Vern(cache)));
        var loader = new FwDataProjectLoader();
        loader.Save(cache);
        using var reloaded = loader.LoadScratchCache(cache.ProjectId.Path);
        var result = citation ? Get<ILexEntry>(reloaded, Id(entry)).CitationForm : Get<IMoForm>(reloaded, Id(form)).Form;
        Assert.True(string.IsNullOrEmpty(result.get_String(reloaded.DefaultVernWs).Text));
        Assert.Equal("other", result.get_String(reloaded.WritingSystemFactory.GetWsFromStr(otherWs.Id)).Text);
        Assert.Contains(Get<ILexEntry>(reloaded, Id(entry)).AlternateFormsOS, item => item.Guid == form.Guid);
    }

    [Theory]
    [InlineData("entry")]
    [InlineData("target")]
    [InlineData("writingSystem")]
    [InlineData("expectedForm")]
    [InlineData("newForm")]
    public void AlternateEditRequiresEveryGuardAndRejectsUnknownFields(string missing)
    {
        var values = new Dictionary<string, object> { ["entry"] = CanonicalId.Mint().Value,
            ["target"] = CanonicalId.Mint().Value, ["writingSystem"] = "qaa", ["expectedForm"] = "pata", ["newForm"] = "pātaʔ" };
        Assert.NotNull(EditAllomorphFormIntentParser.Parse(JsonSerializer.SerializeToElement(values)));
        values.Remove(missing);
        Assert.Throws<ContractParseException>(() => EditAllomorphFormIntentParser.Parse(JsonSerializer.SerializeToElement(values)));
        values[missing] = "unused";
        values["foldStressMarks"] = true;
        Assert.Throws<ContractParseException>(() => EditAllomorphFormIntentParser.Parse(JsonSerializer.SerializeToElement(values)));
    }

    [Fact]
    public void CitationEditRequiresAnExplicitExpectedTextAndRejectsUnknownFieldsAndDuplicates()
    {
        var id = CanonicalId.Mint().Value;
        foreach (var json in new[] { $$"""{"target":"{{id}}","writingSystem":"qaa","newForm":"páta"}""",
                     $$"""{"target":"{{id}}","writingSystem":"qaa","expectedForm":null,"newForm":"páta"}""",
                     $$"""{"target":"{{id}}","writingSystem":"qaa","expectedForm":"","newForm":"páta","extra":true}""",
                     $$"""{"target":"{{id}}","writingSystem":"qaa","expectedForm":"","expectedForm":"x","newForm":"páta"}""" })
        {
            using var document = JsonDocument.Parse(json);
            Assert.Throws<ContractParseException>(() => EditCitationFormIntentParser.Parse(document.RootElement));
        }
    }
}

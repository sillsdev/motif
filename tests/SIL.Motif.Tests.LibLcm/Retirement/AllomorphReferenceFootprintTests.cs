using System.Security.Cryptography;
using System.Text;
using SIL.LCModel;
using SIL.LCModel.Core.Cellar;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Retirement;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Projection.Retirement;
using SIL.Motif.Runner.Retirement;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.Retirement;

[Collection(LcmCacheTestCollection.Name)]
public sealed class AllomorphReferenceFootprintTests : IDisposable
{
    private readonly LcmCache _cache;
    private readonly IMoAffixAllomorph _retired;
    private readonly Guid _wordform;

    public AllomorphReferenceFootprintTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        (_retired, _wordform) = SeedReferences(_cache, pristine.Seed);
    }

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [Fact]
    public void CensusIncludesEveryModelAndCustomReferenceAndAllAffectedReadings()
    {
        var before = ProjectObjectDigest(_cache);
        var judgmentSnapshot = SIL.Motif.Projection.HumanJudgments.HumanJudgmentReader.Read(_cache);
        Assert.True(judgmentSnapshot.Judgments.Count == 2,
            $"Capability={judgmentSnapshot.Capability}; message={judgmentSnapshot.Message}; " +
            $"records={_cache.LangProject.ResearchNotebookOA.RecordsOC.Count}; " +
            $"unavailable={string.Join(" | ", judgmentSnapshot.Unavailable.Select(item => item.Message))}");

        var footprint = AllomorphReferenceFootprintReader.Read(_cache, [_retired.Guid]);
        var again = AllomorphReferenceFootprintReader.Read(_cache, [_retired.Guid]);

        Assert.Equal(footprint.Digest, again.Digest);
        Assert.Equal(before, ProjectObjectDigest(_cache));
        Assert.True(Assert.IsAssignableFrom<IList<AllomorphReference>>(footprint.References).IsReadOnly);
        Assert.IsType<System.Collections.ObjectModel.ReadOnlyDictionary<string, int>>(footprint.Counts.ByReferenceKind);
        Assert.StartsWith("sha256:", footprint.MasterModelSha256);
        Assert.StartsWith("sha256:", footprint.CacheMetadataSha256);
        Assert.Contains(footprint.ReferenceFields, field => field.DeclaringClass == "WfiMorphBundle" && field.Name == "Morph");
        Assert.Contains(footprint.ReferenceFields, field => field.DeclaringClass == "MoAlloAdhocProhib" && field.Name == "FirstAllomorph");
        Assert.Contains(footprint.ReferenceFields, field => field.DeclaringClass == "MoAlloAdhocProhib" && field.Name == "RestOfAllos");
        Assert.Contains(footprint.ReferenceFields, field => field.DeclaringClass == "MoAlloAdhocProhib" && field.Name == "Allomorphs");
        Assert.Contains(footprint.ReferenceFields, field => field.DeclaringClass == "LexDb" && field.Name == "AllomorphIndex");
        Assert.Contains(footprint.ReferenceFields, field => field.DeclaringClass == "LexEntry" && field.Name == "MotifRetirementReference" && field.IsCustom);
        var bundleReferences = footprint.References.Where(item => item.Kind == "bundle-morph").ToArray();
        Assert.Equal(5, bundleReferences.Length);
        Assert.Equal(3, footprint.Counts.ApprovedBundles);
        Assert.Equal(1, footprint.Counts.DisapprovedBundles);
        Assert.Equal(1, footprint.Counts.UnknownBundles);
        Assert.Equal(2, footprint.Counts.ApprovedAnalyses);
        Assert.Equal(1, footprint.Counts.DisapprovedAnalyses);
        Assert.Equal(1, footprint.Counts.UnknownAnalyses);
        Assert.Equal(5, footprint.Counts.Bundles);

        var affected = Assert.Single(footprint.AffectedWordforms);
        Assert.Equal(_wordform, affected.Wordform);
        Assert.Equal(5, affected.Analyses.Count);
        Assert.Equal(3, affected.Analyses.Count(item => item.Opinion == "approved"));
        Assert.Contains(affected.Analyses, item => item.Morphs.All(morph => morph.Form != _retired.Guid));

        var adHoc = footprint.References.Where(item => item.Kind.StartsWith("adhoc-", StringComparison.Ordinal)).ToArray();
        Assert.Equal(5, adHoc.Length);
        Assert.Equal(2, adHoc.Select(item => item.Rule).Distinct().Count());
        Assert.Equal(1, adHoc.Count(item => item.Grouped == true));
        Assert.Equal(4, adHoc.Count(item => item.Enabled == false));
        Assert.Equal(2, adHoc.Count(item => item.Ordinal is null));
        Assert.Equal(3, adHoc.Count(item => item.Ordinal is not null));

        Assert.Contains(footprint.References, item => item.IsCustom && item.SourceClass == "LexEntry" &&
            item.Field == "MotifRetirementReference" && item.TargetForm == _retired.Guid);
        Assert.Contains(footprint.References, item => item.SourceClass == "LexDb" && item.Field == "AllomorphIndex");
        var historical = Assert.Single(footprint.HistoricalReferences,
            item => item.JudgmentKind == nameof(SIL.Motif.Contract.HumanJudgments.DispositionJudgment));
        Assert.Equal("MoAffixAllomorph", historical.Class);
        Assert.Equal(CanonicalId.FromGuid(_retired.Guid).Value, historical.Id);
        var negativeReference = Assert.Single(footprint.HistoricalReferences,
            item => item.JudgmentKind == nameof(SIL.Motif.Contract.HumanJudgments.ReviewedNegativeJudgment));
        Assert.Equal("MoForm", negativeReference.Class);
        Assert.Equal("negative-morph-form:0", negativeReference.Role);
        Assert.DoesNotContain(footprint.References,
            item => item.SourceObject == CanonicalId.Parse(historical.RecordId).ToGuid());
        Assert.Contains(footprint.OwnedDependents, item => item.TargetForm == _retired.Guid &&
            item.DeclaringClass == "MoAffixAllomorph" && item.Field == "MsEnvFeatures");
        Assert.Empty(footprint.Unavailable);
    }

    [Fact]
    public void SeededStemMsaVirtualBackReferenceDoesNotBlockTheCensus()
    {
        var metadata = (IFwMetaDataCacheManaged)_cache.MetaDataCacheAccessor;
        var kernelMetadata = (IFwMetaDataCache)metadata;
        var owningEntryFields = metadata.GetFieldIds().Where(field =>
            metadata.GetOwnClsName(field) == "MoMorphSynAnalysis" &&
            metadata.GetFieldName(field) == "OwningEntry").ToArray();
        Assert.NotEmpty(owningEntryFields);
        Assert.All(owningEntryFields, field => Assert.True(kernelMetadata.get_IsVirtual(field)));

        var seededStemMsa = _cache.ServiceLocator.GetInstance<ILexEntryRepository>().AllInstances()
            .SelectMany(entry => entry.MorphoSyntaxAnalysesOC)
            .OfType<IMoStemMsa>()
            .FirstOrDefault();
        Assert.NotNull(seededStemMsa);

        var footprint = AllomorphReferenceFootprintReader.Read(_cache, [_retired.Guid]);

        Assert.DoesNotContain(footprint.ReferenceFields,
            field => field.DeclaringClass == "MoMorphSynAnalysis" && field.Name == "OwningEntry");
        Assert.DoesNotContain(footprint.Unavailable,
            route => route.Contains("MoMorphSynAnalysis.OwningEntry", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingDestinationsNameExactDistinctApprovedAnalysesAndAdHocRules()
    {
        var footprint = AllomorphReferenceFootprintReader.Read(_cache, [_retired.Guid]);
        var intent = new SIL.Motif.Contract.Retirement.RetireAllomorphIntent("entry", SIL.Motif.Contract.Retirement.AllomorphRetirementScope.Affix, [], [], [], []);

        var diagnostics = AllomorphReferenceFootprintReader.Diagnose(footprint, intent);

        Assert.Equal(2, diagnostics.UnresolvedApprovedAnalyses);
        Assert.Equal(2, diagnostics.UnresolvedAdhocRules);
        Assert.Contains("2 Approved analyses and 2 ad hoc rules", diagnostics.Message, StringComparison.Ordinal);
        Assert.Contains(diagnostics.UnresolvedReferences, item => item.Kind == "bundle-morph" && item.Opinion == "approved");
        Assert.Contains(diagnostics.UnresolvedReferences, item => item.IsCustom);
        Assert.Contains(diagnostics.UnresolvedReferences, item => item.SourceClass == "LexDb");
        Assert.Equal(1, diagnostics.UnsupportedCustomReferences);
        Assert.Equal(1, diagnostics.UnsupportedOtherReferences);
        Assert.Equal(footprint.References.Count, diagnostics.Classifications.Count);
        Assert.All(diagnostics.Classifications, item => Assert.Contains(item.Disposition,
            new[] { "moved", "removed-with-the-graph", "blocking" }));
        Assert.Contains(diagnostics.Classifications, item => item.Reference.IsCustom &&
            item.Disposition == "blocking" && item.Reason.Length > 0);
        Assert.Contains(diagnostics.Classifications, item => item.Reference.Field == "AlternateForms" &&
            item.Disposition == "removed-with-the-graph");
        Assert.Contains(diagnostics.Classifications, item => item.Reference.SourceClass == "LexDb" &&
            item.Reference.Field == "AllomorphIndex" && item.Disposition == "blocking" && item.Reason.Length > 0);
    }

    [Fact]
    public void ANewIncomingReferenceChangesTheFrozenCensusDigest()
    {
        var before = AllomorphReferenceFootprintReader.Read(_cache, [_retired.Guid]);

        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var rule = _cache.ServiceLocator.GetInstance<IMoAlloAdhocProhibFactory>().Create();
            _cache.LangProject.MorphologicalDataOA.AdhocCoProhibitionsOC.Add(rule);
            rule.FirstAllomorphRA = _retired;
        });

        var after = AllomorphReferenceFootprintReader.Read(_cache, [_retired.Guid]);

        Assert.NotEqual(before.Digest, after.Digest);
        Assert.Equal(before.Counts.AdhocOccurrences + 1, after.Counts.AdhocOccurrences);
    }

    private static (IMoAffixAllomorph Retired, Guid Wordform) SeedReferences(
        LcmCache cache, SeededProject seed)
    {
        var services = cache.ServiceLocator;
        var entry = services.GetInstance<ILexEntryRepository>().GetObject(seed.FirstEntryId);
        var metadata = (IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor;
        IMoAffixAllomorph retired = null!;
        var moFormClass = metadata.GetClassIds().Single(id => metadata.GetClassName(id) == "MoForm");
        var customField = 0;
        IWfiWordform wordform = null!;
        var msa = entry.MorphoSyntaxAnalysesOC.Single();
        var otherForm = services.GetInstance<IMoFormRepository>().GetObject(seed.SecondLexemeFormId);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            retired = services.GetInstance<IMoAffixAllomorphFactory>().Create();
            entry.AlternateFormsOS.Add(retired);
            retired.MorphTypeRA = services.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphPrefix);
            retired.Form.set_String(cache.DefaultVernWs, "retirement-");
            retired.MsEnvFeaturesOA = services.GetInstance<IFsFeatStrucFactory>().Create();
            customField = metadata.AddCustomField("LexEntry", "MotifRetirementReference",
                CellarPropertyType.ReferenceAtomic, moFormClass);
            wordform = services.GetInstance<IWfiWordformFactory>().Create(
                TsStringUtils.MakeString("retirement-word", cache.DefaultVernWs));
            var first = AddAnalysis(cache, wordform, msa, retired, retired, otherForm);
            var second = AddAnalysis(cache, wordform, msa, retired);
            var disapproved = AddAnalysis(cache, wordform, msa, retired);
            _ = AddAnalysis(cache, wordform, msa, retired);
            var unaffected = AddAnalysis(cache, wordform, msa, otherForm);
            cache.LangProject.DefaultUserAgent.SetEvaluation(first, Opinions.approves);
            cache.LangProject.DefaultUserAgent.SetEvaluation(second, Opinions.approves);
            cache.LangProject.DefaultUserAgent.SetEvaluation(disapproved, Opinions.disapproves);
            cache.LangProject.DefaultUserAgent.SetEvaluation(unaffected, Opinions.approves);

            cache.DomainDataByFlid.SetObjProp(entry.Hvo, customField, retired.Hvo);
            AddAdHocReferences(cache, retired);
            AddLexDbReference(cache, retired, metadata);
        });

        var judgmentField = NotebookJudgmentFixture.InitializeReservedField(cache);
        var recordId = Guid.Parse("00000000-0000-0000-0000-000000000701");
        var judgment = new HumanJudgment(CanonicalId.FromGuid(cache.LangProject.Guid).Value,
            CanonicalId.FromGuid(recordId).Value, CanonicalId.FromGuid(recordId).Value, [],
            new DispositionJudgment(
                new ObjectJudgmentSubject(new("MoAffixAllomorph", CanonicalId.FromGuid(retired.Guid).Value)),
                "P-allo-alternation-family", ParsimonyDispositionKind.Keep, "sha256:" + new string('a', 64),
                "retirement-test-v1", "retired form", "Unwritten phonological rules"));
        NotebookJudgmentFixture.AddRecord(cache, recordId, judgmentField,
            TsStringUtils.MakeString(HumanJudgmentCodec.Format(judgment), cache.DefaultAnalWs));
        var negativeRecordId = Guid.Parse("00000000-0000-0000-0000-000000000703");
        var negativeCaseId = CanonicalId.FromGuid(Guid.Parse("00000000-0000-0000-0000-000000000704")).Value;
        var negativeJudgment = new HumanJudgment(CanonicalId.FromGuid(cache.LangProject.Guid).Value,
            CanonicalId.FromGuid(negativeRecordId).Value, CanonicalId.FromGuid(negativeRecordId).Value, [],
            new SIL.Motif.Contract.HumanJudgments.ReviewedNegativeJudgment(negativeCaseId,
                cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs), "retirement-", "test context",
                new SIL.Motif.Contract.HumanJudgments.ReadingNegativeTarget([
                    new SIL.Motif.Contract.HumanJudgments.NegativeJudgmentMorph(
                        new SIL.Motif.Contract.Responses.ParseMorph(CanonicalId.FromGuid(retired.Guid).Value,
                            CanonicalId.FromGuid(msa.Guid).Value, null, null), null, "retirement-", "test MSA")
                ])), Actor: new SIL.Motif.Contract.HumanJudgments.JudgmentActor(
                    SIL.Motif.Contract.HumanJudgments.JudgmentActorKind.Human));
        NotebookJudgmentFixture.AddRecord(cache, negativeRecordId, judgmentField,
            TsStringUtils.MakeString(HumanJudgmentCodec.Format(negativeJudgment), cache.DefaultAnalWs));
        return (retired, wordform.Guid);
    }

    private static IWfiAnalysis AddAnalysis(LcmCache cache, IWfiWordform wordform,
        IMoMorphSynAnalysis msa, params IMoForm[] forms)
    {
        var analysis = cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
        wordform.AnalysesOC.Add(analysis);
        foreach (var form in forms)
        {
            var bundle = cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create();
            analysis.MorphBundlesOS.Add(bundle);
            bundle.MorphRA = form;
            bundle.MsaRA = msa;
        }
        return analysis;
    }

    private static void AddAdHocReferences(LcmCache cache, IMoForm retired)
    {
        var root = cache.LangProject.MorphologicalDataOA.AdhocCoProhibitionsOC;
        var flat = cache.ServiceLocator.GetInstance<IMoAlloAdhocProhibFactory>().Create();
        root.Add(flat);
        flat.FirstAllomorphRA = retired;
        flat.RestOfAllosRS.Add(retired);
        flat.RestOfAllosRS.Add(retired);
        flat.AllomorphsRS.Add(retired);
        flat.Disabled = true;

        var group = cache.ServiceLocator.GetInstance<IMoAdhocProhibGrFactory>().Create();
        root.Add(group);
        var grouped = cache.ServiceLocator.GetInstance<IMoAlloAdhocProhibFactory>().Create();
        group.MembersOC.Add(grouped);
        grouped.FirstAllomorphRA = retired;
    }

    private static void AddLexDbReference(LcmCache cache, IMoForm retired, IFwMetaDataCacheManaged metadata)
    {
        var field = metadata.GetFieldIds().Single(id => metadata.GetOwnClsName(id) == "LexDb" &&
            metadata.GetFieldName(id) == "AllomorphIndex");
        var owner = cache.LangProject.LexDbOA;
        var count = cache.DomainDataByFlid.get_VecSize(owner.Hvo, field);
        var items = Enumerable.Range(0, count).Select(index => cache.DomainDataByFlid.get_VecItem(owner.Hvo, field, index)).ToList();
        if (!items.Contains(retired.Hvo)) items.Add(retired.Hvo);
        cache.DomainDataByFlid.Replace(owner.Hvo, field, 0, count, items.ToArray(), items.Count);
    }

    private static string ProjectObjectDigest(LcmCache cache) => string.Join('\n',
        cache.ServiceLocator.GetInstance<ICmObjectRepository>().AllInstances()
            .Select(item => $"{item.Guid:D}|{item.ClassID}|{item.Owner?.Guid:D}")
            .Order(StringComparer.Ordinal));
}

[Trait("MotifTestLevel", "System")]
public sealed class AllomorphReferenceFootprintSampleSurveyTests(ITestOutputHelper output)
{
    private static readonly Guid[] SenaStemForms =
    [
        Guid.Parse("9e21d8bc-0554-45c4-b1b1-30b83c2ec584"),
        Guid.Parse("6b12f1e2-5332-4ca1-bd92-a90b6d3896ee"),
        Guid.Parse("7f2bf300-4670-4f4d-94f2-db6b268a3c45"),
        Guid.Parse("4d269319-d678-46d6-a613-8effe67a15e6"),
    ];

    private static readonly SampleGrammar[] Samples =
    [
        new("Amharic", "amharic.fwdata"),
        new("Awetí", "aweti.fwdata"),
        new("Indonesian", "indonesian.fwdata"),
        new("Mbugwe", "mbugwe.fwdata"),
        new("Sena", "sena.fwdata"),
    ];

    [AllomorphRetirementSampleFact]
    public void FiveGrammarCopiesProduceReadOnlyAffixReferenceCounts()
    {
        var source = Path.GetFullPath(Environment.GetEnvironmentVariable("MOTIF_PANGLOSS_SAMPLES")!);
        var root = Path.Combine(Path.GetTempPath(), "motif-retirement-sample-survey-" + Guid.NewGuid().ToString("N"));
        var copied = Path.Combine(root, "samples");
        Directory.CreateDirectory(copied);
        CopyTree(Path.Combine(source, "WritingSystemStore"), Path.Combine(copied, "WritingSystemStore"));

        try
        {
            foreach (var sample in Samples)
            {
                var sourceProject = Path.Combine(source, sample.FileName);
                var sourceDigest = FileDigest(sourceProject);
                var projectPath = Path.Combine(copied, sample.FileName);
                File.Copy(sourceProject, projectPath);
                using var cache = new FwDataProjectLoader().LoadScratchCache(projectPath);

                var candidates = SelectCandidates(cache, sample.Name);
                Assert.InRange(candidates.Count, 2, 3);
                var footprint = AllomorphReferenceFootprintReader.Read(cache, candidates.Select(item => item.Guid));
                foreach (var form in candidates)
                {
                    var counts = footprint.References.Where(item => item.TargetForm == form.Guid)
                        .GroupBy(item => item.Kind, StringComparer.Ordinal)
                        .OrderBy(group => group.Key, StringComparer.Ordinal)
                        .Select(group => group.Key + "=" + group.Count());
                    output.WriteLine($"{sample.Name}\t{FormText(cache, form)}\t{form.Guid:D}\t{string.Join(", ", counts)}");
                }

                Assert.Equal(sourceDigest, FileDigest(sourceProject));
            }
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [AllomorphRetirementSampleFact]
    public void SenaRootsAndAwetiMToPStemPairProduceReadOnlyCensusAndContextDigests()
    {
        var source = Path.GetFullPath(Environment.GetEnvironmentVariable("MOTIF_PANGLOSS_SAMPLES")!);
        var sourceSena = Path.Combine(source, "sena.fwdata");
        var sourceAweti = Path.Combine(source, "aweti.fwdata");
        var sourceWritingSystems = Path.Combine(source, "WritingSystemStore");
        var sourceDigests = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [sourceSena] = FileDigest(sourceSena),
            [sourceAweti] = FileDigest(sourceAweti),
            [sourceWritingSystems] = DirectoryDigest(sourceWritingSystems),
        };
        var root = Path.Combine(Path.GetTempPath(), "motif-stem-retirement-sample-survey-" + Guid.NewGuid().ToString("N"));
        var copied = Path.Combine(root, "samples");
        Directory.CreateDirectory(copied);
        CopyTree(sourceWritingSystems, Path.Combine(copied, "WritingSystemStore"));
        var copiedSena = Path.Combine(copied, "sena.fwdata");
        var copiedAweti = Path.Combine(copied, "aweti.fwdata");
        File.Copy(sourceSena, copiedSena);
        File.Copy(sourceAweti, copiedAweti);
        var copiedSenaDigest = FileDigest(copiedSena);
        var copiedAwetiDigest = FileDigest(copiedAweti);
        output.WriteLine($"Sample copies\t{copied}\tSena={copiedSenaDigest}\tAwetí={copiedAwetiDigest}\t" +
            $"WritingSystemStore={Sha256String(DirectoryDigest(sourceWritingSystems))}");
        var reportedUnavailable = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            using (var cache = new FwDataProjectLoader().LoadScratchCache(copiedSena))
            {
                var repository = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
                var forms = SenaStemForms.Select(id => Assert.IsAssignableFrom<IMoStemAllomorph>(repository.GetObject(id)))
                    .ToArray();
                foreach (var form in forms)
                {
                    var footprint = AllomorphReferenceFootprintReader.Read(cache, [form.Guid]);
                    var context = StemAllomorphSelectionContextReader.Read(cache, [form.Guid]);
                    var byKind = footprint.References.Where(item => item.TargetForm == form.Guid)
                        .GroupBy(item => item.Kind, StringComparer.Ordinal)
                        .OrderBy(group => group.Key, StringComparer.Ordinal)
                        .Select(group => group.Key + "=" + group.Count());
                    output.WriteLine($"Sena\t{FormText(cache, form)}\t{form.Guid:D}\trefs={string.Join(',', byKind)}\t" +
                        $"owned={footprint.OwnedDependents.Count(item => item.TargetForm == form.Guid)}\t" +
                        $"affectedWordforms={footprint.AffectedWordforms.Count}\t" +
                        $"approved={footprint.Counts.ApprovedBundles}\tcontext={context.Digest}\t" +
                        $"unavailable={context.Unavailable.Count}");
                    foreach (var reason in context.Unavailable)
                        if (reportedUnavailable.Add("Sena|" + reason)) output.WriteLine($"Unavailable\tSena\t{reason}");
                    Assert.DoesNotContain(footprint.References,
                        item => item.TargetForm == form.Guid && item.Kind == "bundle-morph");
                }

                var stemSource = forms[0];
                var entry = Assert.IsAssignableFrom<ILexEntry>(stemSource.Owner);
                var refusal = Assert.Throws<InvalidOperationException>(() =>
                    ReplaceListedAllomorphsWithRuleComposer.Build(cache,
                        SenaStemReplacementIntent(cache, stemSource, entry), () => CanonicalId.FromGuid(Guid.NewGuid())));
                Assert.Contains("stem retirement evidence is incomplete", refusal.Message, StringComparison.Ordinal);
                output.WriteLine($"Sena recipe refusal\t{CanonicalId.FromGuid(stemSource.Guid)}\t{refusal.Message}");
            }

            using (var cache = new FwDataProjectLoader().LoadScratchCache(copiedAweti))
            {
                var pair = FindAwetiMToPStemPair(cache);
                output.WriteLine($"Awetí\tm~p candidates={pair.CandidateCount}");
                var footprint = AllomorphReferenceFootprintReader.Read(cache, [pair.M.Guid, pair.P.Guid]);
                var context = StemAllomorphSelectionContextReader.Read(cache, [pair.M.Guid, pair.P.Guid]);
                foreach (var form in new[] { pair.M, pair.P })
                {
                    var byKind = footprint.References.Where(item => item.TargetForm == form.Guid)
                        .GroupBy(item => item.Kind, StringComparer.Ordinal)
                        .OrderBy(group => group.Key, StringComparer.Ordinal)
                        .Select(group => group.Key + "=" + group.Count());
                    output.WriteLine($"Awetí\t{FormText(cache, form)}\t{form.Guid:D}\trefs={string.Join(',', byKind)}\t" +
                        $"owned={footprint.OwnedDependents.Count(item => item.TargetForm == form.Guid)}\t" +
                        $"affectedWordforms={footprint.AffectedWordforms.Count}\t" +
                        $"approved={footprint.Counts.ApprovedBundles}\tcontext={context.Digest}\t" +
                        $"unavailable={context.Unavailable.Count}");
                    foreach (var reason in context.Unavailable)
                        if (reportedUnavailable.Add("Awetí|" + reason)) output.WriteLine($"Unavailable\tAwetí\t{reason}");
                }
            }

            Assert.Equal(copiedSenaDigest, FileDigest(copiedSena));
            Assert.Equal(copiedAwetiDigest, FileDigest(copiedAweti));
            foreach (var (path, digest) in sourceDigests)
                Assert.Equal(digest, path == sourceWritingSystems ? DirectoryDigest(path) : FileDigest(path));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static ReplaceListedAllomorphsWithRuleIntent SenaStemReplacementIntent(
        LcmCache cache, IMoStemAllomorph retired, ILexEntry entry)
    {
        var entryId = CanonicalId.FromGuid(entry.Guid);
        var retiredId = CanonicalId.FromGuid(retired.Guid);
        var primary = Assert.IsAssignableFrom<IMoStemAllomorph>(entry.LexemeFormOA);
        var primaryId = CanonicalId.FromGuid(primary.Guid);
        var position = retired.MorphTypeRA?.Guid == MoMorphTypeTags.kguidMorphRoot ? "root" : "stem";
        var digest = "sha256:" + new string('0', 64);
        var retiredIdentity = new AllomorphIdentity(retiredId.Value, entryId.Value, "MoStemAllomorph",
            "alternate", position, digest, null);
        var primaryIdentity = new AllomorphIdentity(primaryId.Value, entryId.Value, "MoStemAllomorph",
            "lexeme", position, digest, null);
        var msa = entry.MorphoSyntaxAnalysesOC.OfType<IMoStemMsa>().Single();
        var phonemes = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
        var classId = CanonicalId.Mint();
        var ruleId = CanonicalId.Mint();
        var l = CanonicalId.FromGuid(Guid.Parse("937234ce-012f-4143-9130-c80cce3d8ace"));
        var r = CanonicalId.FromGuid(Guid.Parse("82b13fe1-552a-4065-9f16-28c91f09a789"));
        var nonFront = Assert.IsAssignableFrom<IPhNCSegments>(phonemes.GetObject(
            Guid.Parse("11b7ddde-8bc5-4e45-8fc3-6fc1087825ba")));
        var memberIds = nonFront.SegmentsRC.Select(item => CanonicalId.FromGuid(item.Guid).Value).ToArray();
        var retirement = new RetireAllomorphIntent(entryId.Value, AllomorphRetirementScope.Stem,
            [retiredIdentity],
            [new(retiredId.Value, CanonicalId.FromGuid(msa.Guid).Value, null, "whole", primaryIdentity)],
            [], []);
        var naturalClass = new RetirementNaturalClass("create", classId.Value, "non-front vowels", "NF",
            memberIds, null);
        var rule = new RetirementRule(ruleId.Value, "l becomes r after a non-front vowel", [l.Value], [r.Value],
            [new("natural-class", classId.Value)], [], new("first", null), true);
        var bindings = new List<RetirementOperationBinding>();
        var classCreate = Binding("class-create", classId, null, []);
        var classMembers = Binding("class-members", classId, null, [classCreate.OperationId]);
        bindings.Add(classCreate);
        bindings.Add(classMembers);
        var ruleCreate = Binding("rule-create", ruleId, null, [classMembers.OperationId]);
        bindings.Add(ruleCreate);
        foreach (var slot in new[] { "rule-input", "rule-output", "rule-left", "rule-right", "rule-placement", "rule-enabled" })
        {
            var binding = Binding(slot, ruleId, null, [classMembers.OperationId, ruleCreate.OperationId]);
            bindings.Add(binding);
        }
        bindings.Add(Binding("alternate-delete", entryId, retiredId,
            [.. bindings.Select(item => item.OperationId)]));
        return new(naturalClass, rule, [retirement], bindings);

        static RetirementOperationBinding Binding(string slot, CanonicalId target, CanonicalId? member,
            IReadOnlyList<string> dependencies) =>
            new(CanonicalId.Mint().Value, slot, target.Value, member?.Value, dependencies);
    }

    private static (IMoStemAllomorph M, IMoStemAllomorph P, int CandidateCount) FindAwetiMToPStemPair(LcmCache cache)
    {
        var candidates = new List<(IMoStemAllomorph M, IMoStemAllomorph P)>();
        foreach (var entry in cache.ServiceLocator.GetInstance<ILexEntryRepository>().AllInstances())
        {
            var forms = new[] { entry.LexemeFormOA }.Concat(entry.AlternateFormsOS)
                .OfType<IMoStemAllomorph>().ToArray();
            for (var first = 0; first < forms.Length; first++)
            for (var second = first + 1; second < forms.Length; second++)
            {
                if (NormalizeMToP(cache, forms[first], forms[second]))
                    candidates.Add((forms[first], forms[second]));
                else if (NormalizeMToP(cache, forms[second], forms[first]))
                    candidates.Add((forms[second], forms[first]));
            }
        }
        var ordered = candidates.OrderBy(pair => pair.M.Owner?.Guid).ThenBy(pair => pair.M.Guid).ToArray();
        Assert.NotEmpty(ordered);
        return (ordered[0].M, ordered[0].P, ordered.Length);
    }

    private static bool NormalizeMToP(LcmCache cache, IMoStemAllomorph m, IMoStemAllomorph p)
    {
        var mText = m.Form.get_String(cache.DefaultVernWs)?.Text.Normalize(NormalizationForm.FormD);
        var pText = p.Form.get_String(cache.DefaultVernWs)?.Text.Normalize(NormalizationForm.FormD);
        if (mText is null || pText is null || mText.Length != pText.Length) return false;
        var differences = Enumerable.Range(0, mText.Length).Where(index => mText[index] != pText[index]).ToArray();
        return differences.Length == 1 && mText[differences[0]] == 'm' && pText[differences[0]] == 'p';
    }

    private static string DirectoryDigest(string path) => string.Join('\n', Directory.GetFiles(path, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal)
        .Select(file => Path.GetRelativePath(path, file) + "|" + FileDigest(file)));

    private static string Sha256String(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static string FormText(LcmCache cache, IMoStemAllomorph form) =>
        form.Form.get_String(cache.DefaultVernWs)?.Text ?? string.Empty;

    private static IReadOnlyList<IMoAffixAllomorph> SelectCandidates(LcmCache cache, string grammar)
    {
        var alternates = cache.ServiceLocator.GetInstance<ILexEntryRepository>().AllInstances()
            .SelectMany(entry => entry.AlternateFormsOS.OfType<IMoAffixAllomorph>())
            .Where(IsOrdinaryPrefixOrSuffix)
            .OrderBy(form => form.Owner?.Guid)
            .ThenBy(form => FormText(cache, form), StringComparer.Ordinal)
            .ToArray();
        if (alternates.Length < 2) return alternates;

        var preferred = grammar switch
        {
            "Awetí" => new[] { "o", "w" },
            "Sena" => new[] { "dz" },
            _ => []
        };
        var chosen = alternates.Where(form => preferred.Any(text => NormalizeForm(FormText(cache, form)) == text))
            .Take(3).ToList();
        if (chosen.Count < 2)
        {
            var family = alternates.GroupBy(form => form.Owner?.Guid)
                .Where(group => group.Count() >= 2)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key)
                .FirstOrDefault();
            if (family is not null)
                chosen.AddRange(family.Where(form => !chosen.Contains(form)).Take(3 - chosen.Count));
        }
        if (chosen.Count < 2) chosen.AddRange(alternates.Where(form => !chosen.Contains(form)).Take(3 - chosen.Count));
        return chosen.Take(3).ToArray();
    }

    private static bool IsOrdinaryPrefixOrSuffix(IMoAffixAllomorph form) => form.MorphTypeRA?.Guid is
        var id && (id == MoMorphTypeTags.kguidMorphPrefix || id == MoMorphTypeTags.kguidMorphSuffix);

    private static string FormText(LcmCache cache, IMoAffixAllomorph form) =>
        form.Form.get_String(cache.DefaultVernWs)?.Text ?? string.Empty;

    private static string NormalizeForm(string form) => form.Trim('-').Normalize(NormalizationForm.FormD);

    private static string FileDigest(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (var directory in Directory.GetDirectories(source))
            CopyTree(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    private sealed record SampleGrammar(string Name, string FileName);
}

public sealed class AllomorphRetirementSampleFactAttribute : FactAttribute
{
    public AllomorphRetirementSampleFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MOTIF_PANGLOSS_SAMPLES")))
            Skip = "Set MOTIF_PANGLOSS_SAMPLES to the PanGloss samples/data directory for the read-only retirement survey.";
    }
}

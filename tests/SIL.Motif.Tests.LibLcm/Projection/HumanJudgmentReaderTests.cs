using System.Text;
using SIL.LCModel;
using SIL.LCModel.Core.Cellar;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Core.WritingSystems;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.LiveHost.HumanJudgments;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Projection.HumanJudgments;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Projection;

[Collection(LcmCacheTestCollection.Name)]
public sealed class HumanJudgmentReaderTests : IDisposable
{
    private static readonly string[] SubjectClasses =
    [
        "LexEntry", "LexSense", "WfiWordform", "WfiAnalysis", "MoStemAllomorph",
        "MoAffixAllomorph", "MoAffixProcess", "MoStemMsa", "MoInflAffMsa", "MoDerivAffMsa",
        "MoDerivStepMsa", "MoUnclassifiedAffixMsa", "MoInflAffixSlot", "MoInflAffixTemplate",
        "PhEnvironment", "PhNCSegments", "PhNCFeatures", "PhRegularRule", "PhMetathesisRule",
        "MoAlloAdhocProhib", "MoMorphAdhocProhib", "MoAdhocProhibGr", "PartOfSpeech", "MoInflClass",
        "MoStemName", "FsClosedFeature", "FsComplexFeature", "FsOpenFeature", "FsSymFeatVal",
        "FsFeatStruc", "PhPhoneme", "PhBdryMarker"
    ];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-judgment-reader-" + Guid.NewGuid().ToString("N"));

    public HumanJudgmentReaderTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void SavesAndReopensEveryTypedNotebookSubjectWithoutDependingOnFieldIdOrLabel()
    {
        var loader = new FwDataProjectLoader();
        var fieldId = 0;
        var expected = new Dictionary<string, string>(StringComparer.Ordinal);
        using (var cache = NewLangProjFixture.CreateCache(_root))
        {
            AddField(cache, "RnGenericRec", "MotifBeforeJudgment", CellarPropertyType.Integer,
                WritingSystemServices.kwsAnal, "Motif human judgment");
            fieldId = NotebookJudgmentFixture.InitializeReservedField(cache);
            var metadata = (IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor;
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                metadata.UpdateCustomField(fieldId, string.Empty, WritingSystemServices.kwsAnal, "Renamed label"));
            AddField(cache, "RnGenericRec", "MotifAfterJudgment", CellarPropertyType.Integer,
                WritingSystemServices.kwsAnal, "Unrelated label");

            var projectId = Id(cache.LangProject.Guid);
            for (var index = 0; index < SubjectClasses.Length; index++)
            {
                var recordId = Identity(1000 + index);
                var value = Disposition(projectId, Id(recordId), SubjectClasses[index], Identity(index + 1));
                var physical = HumanJudgmentCodec.Format(value);
                NotebookJudgmentFixture.AddRecord(cache, recordId, fieldId,
                    TsStringUtils.MakeString(physical, cache.DefaultAnalWs));
                expected.Add(Id(recordId), HumanJudgmentCodec.LogicalDigest(value));
            }

            var projectRecord = Identity(1100);
            var projectJudgment = Disposition(projectId, Id(projectRecord), "LangProject", cache.LangProject.Guid,
                projectSubject: true);
            NotebookJudgmentFixture.AddRecord(cache, projectRecord, fieldId,
                TsStringUtils.MakeString(HumanJudgmentCodec.Format(projectJudgment), cache.DefaultAnalWs));
            expected.Add(Id(projectRecord), HumanJudgmentCodec.LogicalDigest(projectJudgment));

            var edgeRecord = Identity(1101);
            var edgeJudgment = Disposition(projectId, Id(edgeRecord), "LexEntry", Identity(8001), edgeSubject: true);
            NotebookJudgmentFixture.AddRecord(cache, edgeRecord, fieldId,
                TsStringUtils.MakeString(HumanJudgmentCodec.Format(edgeJudgment), cache.DefaultAnalWs));
            expected.Add(Id(edgeRecord), HumanJudgmentCodec.LogicalDigest(edgeJudgment));

            var groupRecord = Identity(1102);
            var groupJudgment = Disposition(projectId, Id(groupRecord), "LexEntry", Identity(8002), groupSubject: true);
            NotebookJudgmentFixture.AddRecord(cache, groupRecord, fieldId,
                TsStringUtils.MakeString(HumanJudgmentCodec.Format(groupJudgment), cache.DefaultAnalWs));
            expected.Add(Id(groupRecord), HumanJudgmentCodec.LogicalDigest(groupJudgment));

            ChangeFirstAnalysisWritingSystem(cache);
            loader.Save(cache);
        }

        using var reopened = loader.LoadCache(NewLangProjFixture.FwDataPath(_root));
        var resolution = HumanJudgmentFieldResolver.Resolve(reopened);
        Assert.Equal(HumanJudgmentFieldCapability.Available, resolution.Capability);

        var snapshot = HumanJudgmentReader.Read(reopened);
        Assert.Equal(HumanJudgmentFieldCapability.Available, snapshot.Capability);
        Assert.Empty(snapshot.Unavailable);
        Assert.Equal(expected.Count, snapshot.Judgments.Count);
        Assert.Equal(expected, snapshot.Judgments.ToDictionary(item => item.RecordId,
            item => item.ContentDigest, StringComparer.Ordinal));
        Assert.Equal(SubjectClasses.Order(StringComparer.Ordinal), snapshot.Judgments
            .Where(item => item.Judgment.Body is DispositionJudgment { Subject: ObjectJudgmentSubject })
            .Select(item => ((ObjectJudgmentSubject)((DispositionJudgment)item.Judgment.Body).Subject).Object.Class)
            .Order(StringComparer.Ordinal));
        Assert.Equal(snapshot.LogicalDigest, HumanJudgmentReader.Read(reopened).LogicalDigest);
    }

    [Fact]
    public void ResolverUsesTheReservedNameWhenUnrelatedFieldsChangeTheirCreationOrder()
    {
        var firstRoot = Path.Combine(_root, "first-order");
        var secondRoot = Path.Combine(_root, "second-order");
        using var first = NewLangProjFixture.CreateCache(firstRoot);
        using var second = NewLangProjFixture.CreateCache(secondRoot);
        var firstId = NotebookJudgmentFixture.InitializeReservedField(first);
        AddField(second, "RnGenericRec", "MotifBeforeJudgment", CellarPropertyType.Integer,
            WritingSystemServices.kwsAnal, "unrelated");
        var secondId = NotebookJudgmentFixture.InitializeReservedField(second);

        Assert.NotEqual(firstId, secondId);
        Assert.Equal(HumanJudgmentFieldCapability.Available, HumanJudgmentFieldResolver.Resolve(first).Capability);
        Assert.Equal(HumanJudgmentFieldCapability.Available, HumanJudgmentFieldResolver.Resolve(second).Capability);
    }

    [Fact]
    public void ReservedCustomFieldMatchesItsCheckedInSignatureInventory()
    {
        using var cache = NewLangProjFixture.CreateCache(_root);
        NotebookJudgmentFixture.InitializeReservedField(cache);
        var resolution = HumanJudgmentFieldResolver.Resolve(cache);
        Assert.Equal(HumanJudgmentFieldCapability.Available, resolution.Capability);
        var signature = Assert.IsType<SIL.Motif.Projection.HumanJudgments.HumanJudgmentFieldSignature>(
            resolution.Signature);
        var rows = File.ReadAllLines(Path.Combine(FindRepositoryRoot(), "manifest", "human-judgment-custom-fields.tsv"));
        var header = rows[0].Split('\t').Select(cell => cell.Trim('"')).ToArray();
        var row = Assert.Single(rows.Skip(1), line => line.Length > 0)
            .Split('\t').Select(cell => cell.Trim('"')).ToArray();
        var inventory = header.Zip(row).ToDictionary(pair => pair.First, pair => pair.Second, StringComparer.Ordinal);

        Assert.Equal("RnGenericRec", inventory["DeclaringClass"]);
        Assert.Equal("MotifHumanJudgment", inventory["InternalName"]);
        Assert.Equal("String", inventory["Type"]);
        Assert.Equal("true", inventory["Custom"]);
        Assert.Equal("analysis", inventory["WritingSystemSelector"]);
        Assert.Equal("none", inventory["DestinationClass"]);
        Assert.Equal("none", inventory["ListRoot"]);
        Assert.Equal("declaring-class+internal-name", inventory["ResolverKey"]);
        Assert.Equal("ResearchNotebook.Records", inventory["RecordOwner"]);
        Assert.Equal("one-readable-value-per-revision", inventory["ValueStorage"]);
        Assert.Equal(HumanJudgmentCustomFieldOperationKinds.Set, inventory["OperationKind"]);
        Assert.Equal(SnapshotFields.RnGenericRecMotifHumanJudgment, inventory["SnapshotField"]);
        Assert.Equal("semantic-composer-only", inventory["WriteBoundary"]);
        Assert.Equal(inventory["DeclaringClass"], signature.DeclaringClass);
        Assert.Equal(inventory["Type"], signature.Type);
        Assert.Equal(bool.Parse(inventory["Custom"]), signature.IsCustom);
        Assert.Equal(WritingSystemServices.kwsAnal, signature.WritingSystemSelector);
        Assert.Equal(0, signature.DestinationClass);
        Assert.Equal(Guid.Empty, signature.ListRoot);
    }

    [Fact]
    public void ReadsReasonEditsAndIgnoresRichFormattingInTheLogicalDigest()
    {
        var loader = new FwDataProjectLoader();
        var fieldId = 0;
        var recordId = Identity(2000);
        string initialPhysical;
        using (var cache = NewLangProjFixture.CreateCache(_root))
        {
            fieldId = NotebookJudgmentFixture.InitializeReservedField(cache);
            var projectId = Id(cache.LangProject.Guid);
            var value = Disposition(projectId, Id(recordId), "MoAffixAllomorph", Identity(2001)) with
            {
                Reason = "Café: \"quoted\"\n第二行; Reason: stays data."
            };
            initialPhysical = HumanJudgmentCodec.Format(value);
            var styled = TsStringUtils.MakeString(initialPhysical, cache.DefaultAnalWs).GetBldr();
            styled.SetIntPropValues(0, initialPhysical.Length, (int)FwTextPropType.ktptBold,
                (int)FwTextPropVar.ktpvEnum, 1);
            NotebookJudgmentFixture.AddRecord(cache, recordId, fieldId,
                styled.GetString());
            loader.Save(cache);
        }

        string firstDigest;
        using (var cache = loader.LoadCache(NewLangProjFixture.FwDataPath(_root)))
        {
            var first = Assert.Single(HumanJudgmentReader.Read(cache).Judgments);
            Assert.Equal("Café: \"quoted\"\n第二行; Reason: stays data.".Normalize(NormalizationForm.FormD),
                first.Judgment.Reason);
            firstDigest = first.ContentDigest;

            var record = cache.ServiceLocator.GetInstance<ICmObjectRepository>().GetObject(recordId);
            var physical = cache.DomainDataByFlid.get_StringProp(record.Hvo,
                NotebookJudgmentFixture.FindReservedField(cache)).Text;
            var originalSnapshotDigest = HumanJudgmentReader.Read(cache).LogicalDigest;
            NotebookJudgmentFixture.SetValue(cache, (IRnGenericRec)record,
                TsStringUtils.MakeString(physical, cache.DefaultAnalWs));
            Assert.Equal(originalSnapshotDigest, HumanJudgmentReader.Read(cache).LogicalDigest);

            NotebookJudgmentFixture.SetValue(cache, (IRnGenericRec)record,
                TsStringUtils.MakeString(physical, cache.DefaultVernWs));
            Assert.Equal(originalSnapshotDigest, HumanJudgmentReader.Read(cache).LogicalDigest);

            var markerStart = physical.LastIndexOf(" [motif-human-judgment:v1:", StringComparison.Ordinal);
            var reasonStart = physical.IndexOf(" Reason: ", StringComparison.Ordinal);
            var edited = physical[..reasonStart] + " Reason: \"Updated from FieldWorks\"" + physical[markerStart..];
            NotebookJudgmentFixture.SetValue(cache, (IRnGenericRec)record,
                TsStringUtils.MakeString(edited, cache.DefaultVernWs));
            loader.Save(cache);
        }

        using (var cache = loader.LoadCache(NewLangProjFixture.FwDataPath(_root)))
        {
            var edited = Assert.Single(HumanJudgmentReader.Read(cache).Judgments);
            Assert.Equal("Updated from FieldWorks", edited.Judgment.Reason);
            Assert.NotEqual(firstDigest, edited.ContentDigest);
            Assert.NotEqual(initialPhysical, edited.PhysicalValue);

            var record = cache.ServiceLocator.GetInstance<ICmObjectRepository>().GetObject(recordId);
            var physical = cache.DomainDataByFlid.get_StringProp(record.Hvo,
                NotebookJudgmentFixture.FindReservedField(cache)).Text;
            var markerStart = physical.LastIndexOf(" [motif-human-judgment:v1:", StringComparison.Ordinal);
            var reasonStart = physical.IndexOf(" Reason: ", StringComparison.Ordinal);
            var withoutReason = physical[..reasonStart] + physical[markerStart..];
            NotebookJudgmentFixture.SetValue(cache, (IRnGenericRec)record,
                TsStringUtils.MakeString(withoutReason, cache.DefaultVernWs));
            loader.Save(cache);
        }

        using var reasonRemoved = loader.LoadCache(NewLangProjFixture.FwDataPath(_root));
        var noReason = Assert.Single(HumanJudgmentReader.Read(reasonRemoved).Judgments);
        Assert.Null(noReason.Judgment.Reason);
        Assert.Equal(HumanJudgmentCodec.LogicalDigest(noReason.Judgment), noReason.ContentDigest);
    }

    [Fact]
    public void MissingIncompatibleAndAmbiguousDefinitionsHaveExplicitCapabilityResults()
    {
        using (var missing = NewLangProjFixture.CreateCache(Path.Combine(_root, "missing")))
        {
            Assert.Equal(HumanJudgmentFieldCapability.Missing,
                HumanJudgmentReader.Read(missing).Capability);
        }

        using (var wrongType = NewLangProjFixture.CreateCache(Path.Combine(_root, "wrong-type")))
        {
            AddField(wrongType, "RnGenericRec", HumanJudgmentFieldDefinition.InternalName,
                CellarPropertyType.Integer, WritingSystemServices.kwsAnal, HumanJudgmentFieldDefinition.UserLabel);
            Assert.Equal(HumanJudgmentFieldCapability.Incompatible,
                HumanJudgmentReader.Read(wrongType).Capability);
        }

        using (var wrongWritingSystem = NewLangProjFixture.CreateCache(Path.Combine(_root, "wrong-ws")))
        {
            AddField(wrongWritingSystem, "RnGenericRec", HumanJudgmentFieldDefinition.InternalName,
                CellarPropertyType.String, WritingSystemServices.kwsVern, HumanJudgmentFieldDefinition.UserLabel);
            Assert.Equal(HumanJudgmentFieldCapability.Incompatible,
                HumanJudgmentReader.Read(wrongWritingSystem).Capability);
        }

        using (var ambiguous = NewLangProjFixture.CreateCache(Path.Combine(_root, "ambiguous")))
        {
            AddField(ambiguous, "RnGenericRec", HumanJudgmentFieldDefinition.InternalName,
                CellarPropertyType.String, WritingSystemServices.kwsAnal, HumanJudgmentFieldDefinition.UserLabel);
            AddField(ambiguous, "LexEntry", HumanJudgmentFieldDefinition.InternalName,
                CellarPropertyType.String, WritingSystemServices.kwsAnal, HumanJudgmentFieldDefinition.UserLabel);
            Assert.Equal(HumanJudgmentFieldCapability.Ambiguous,
                HumanJudgmentReader.Read(ambiguous).Capability);
        }
    }

    [Fact]
    public void ALookalikeLabelDoesNotResolveAndMalformedRecordsStayUnavailable()
    {
        var loader = new FwDataProjectLoader();
        using (var cache = NewLangProjFixture.CreateCache(_root))
        {
            AddField(cache, "RnGenericRec", "OtherInternalName", CellarPropertyType.String,
                WritingSystemServices.kwsAnal, HumanJudgmentFieldDefinition.UserLabel);
            Assert.Equal(HumanJudgmentFieldCapability.Missing,
                HumanJudgmentFieldResolver.Resolve(cache).Capability);

            var fieldId = NotebookJudgmentFixture.InitializeReservedField(cache);
            var projectId = Id(cache.LangProject.Guid);
            var good = HumanJudgmentCodec.Format(Disposition(projectId, Id(Identity(3000)), "LexEntry", Identity(3001)));
            var mismatch = good.Replace("Keep “", "Fix “", StringComparison.Ordinal);
            var duplicate = good + good;
            NotebookJudgmentFixture.AddRecord(cache, Identity(3000), fieldId,
                TsStringUtils.MakeString(good, cache.DefaultAnalWs));
            NotebookJudgmentFixture.AddRecord(cache, Identity(3002), fieldId,
                TsStringUtils.MakeString(mismatch, cache.DefaultAnalWs));
            NotebookJudgmentFixture.AddRecord(cache, Identity(3003), fieldId,
                TsStringUtils.MakeString(duplicate, cache.DefaultAnalWs));
            loader.Save(cache);
        }

        using var reopened = loader.LoadCache(NewLangProjFixture.FwDataPath(_root));
        var snapshot = HumanJudgmentReader.Read(reopened);
        Assert.Single(snapshot.Judgments);
        Assert.Equal(2, snapshot.Unavailable.Count);
        Assert.All(snapshot.Unavailable, item => Assert.False(string.IsNullOrWhiteSpace(item.Message)));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void AddField(
        LcmCache cache, string ownerClass, string name, CellarPropertyType type, int selector, string label)
    {
        var metadata = (IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            metadata.AddCustomField(ownerClass, name, type, 0, label, selector, Guid.Empty));
    }

    private static void ChangeFirstAnalysisWritingSystem(LcmCache cache)
    {
        cache.ServiceLocator.WritingSystemManager.GetOrSet("de", out var changed);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var systems = cache.ServiceLocator.WritingSystems;
            var first = systems.AnalysisWritingSystems.Single();
            systems.AnalysisWritingSystems.Add(changed);
            systems.CurrentAnalysisWritingSystems.Add(changed);
            systems.CurrentAnalysisWritingSystems.Remove(first);
        });
    }

    private static HumanJudgment Disposition(
        string projectId,
        string revisionId,
        string className,
        Guid subjectId,
        bool projectSubject = false,
        bool edgeSubject = false,
        bool groupSubject = false)
    {
        HumanJudgmentSubject subject;
        if (projectSubject)
            subject = new ProjectJudgmentSubject(projectId);
        else if (edgeSubject)
            subject = new EdgeJudgmentSubject(JudgmentEdgeRole.Membership,
                new("LexEntry", Id(subjectId)), new("LexEntry", Id(Identity(8100))),
                new("LexSense", Id(Identity(8101))));
        else if (groupSubject)
            subject = new GroupJudgmentSubject("M-family", JudgmentGroupRole.AlternationFamily,
                [new("MoAffixAllomorph", Id(Identity(8200))), new("MoAffixAllomorph", Id(Identity(8201))) ]);
        else
            subject = new ObjectJudgmentSubject(new(className, Id(subjectId),
                className.EndsWith("Msa", StringComparison.Ordinal) ? Id(Identity(9000)) : null));
        var judgment = new HumanJudgment(projectId, Id(Identity(9100)), revisionId, [],
            new DispositionJudgment(subject, "M-family", ParsimonyDispositionKind.Keep,
                "sha256:" + new string('a', 64), "test-evidence-v1", className, "Alternation family"));
        return judgment;
    }

    private static Guid Identity(int value) => Guid.Parse($"00000000-0000-0000-0000-{value:D12}");
    private static string Id(Guid value) => CanonicalId.FromGuid(value).Value;

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Motif.sln"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not find Motif.sln above the test output directory.");
    }
}

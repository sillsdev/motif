using System;
using System.Collections.Generic;
using System.Linq;
using SIL.LCModel;
using SIL.LCModel.Core.Cellar;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.TextEditing.Markdown;
using SIL.TextEditing.Markdown.Records;
using Xunit;

namespace SIL.TextEditing.Project.Tests
{
    public class ProjectRecordImportPlanTests
    {
        static ITsString Vern(LcmCache cache, string text) => TsStringUtils.MakeString(text, cache.DefaultVernWs);
        static ITsString Anal(LcmCache cache, string text) => TsStringUtils.MakeString(text, cache.DefaultAnalWs);
        static int AddCustom(LcmCache cache, string owner, string name, CellarPropertyType type)
            => ((IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor).AddCustomField(owner, name, type, 0);

        sealed class World
        {
            public ProjectFixture Fixture = null!;
            public ILexEntry Entry = null!;
            public ILexSense Sense = null!, Second = null!;
            public ICmSemanticDomain Domain = null!, OtherDomain = null!;
            public IMoMorphType OtherMorphType = null!;
            public int Count, Remark;
        }

        static World Build(ProjectFixture fixture)
        {
            var cache = fixture.Cache;
            var w = new World { Fixture = fixture };
            w.Count = AddCustom(cache, "LexEntry", "Count", CellarPropertyType.Integer);
            w.Remark = AddCustom(cache, "LexEntry", "Remark", CellarPropertyType.Unicode);
            fixture.Change(() =>
            {
                var entry = w.Entry = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create();
                var allomorph = cache.ServiceLocator.GetInstance<IMoStemAllomorphFactory>().Create();
                entry.LexemeFormOA = allomorph;
                allomorph.Form.set_String(cache.DefaultVernWs, Vern(cache, "palit"));
                var morphTypes = cache.LangProject.LexDbOA.MorphTypesOA.PossibilitiesOS.Cast<IMoMorphType>().ToList();
                allomorph.MorphTypeRA = morphTypes[0];
                w.OtherMorphType = morphTypes[1];
                entry.CitationForm.set_String(cache.DefaultVernWs, Vern(cache, "palit"));
                entry.DoNotUseForParsing = true;
                var comment = TsStringUtils.MakeStrBldr();
                comment.Replace(0, 0, "plain ", TsStringUtils.MakeProps("", cache.DefaultAnalWs));
                comment.Replace(comment.Length, comment.Length, "vernacular", TsStringUtils.MakeProps("", cache.DefaultVernWs));
                entry.Comment.set_String(cache.DefaultAnalWs, comment.GetString());
                var sense = w.Sense = cache.ServiceLocator.GetInstance<ILexSenseFactory>().Create();
                entry.SensesOS.Add(sense);
                sense.Gloss.set_String(cache.DefaultAnalWs, Anal(cache, "buy"));
                sense.Definition.set_String(cache.DefaultAnalWs, Anal(cache, "to pay for [something]"));
                var example = cache.ServiceLocator.GetInstance<ILexExampleSentenceFactory>().Create();
                sense.ExamplesOS.Add(example);
                example.Example.set_String(cache.DefaultVernWs, Vern(cache, "Mipalit ko"));
                var second = w.Second = cache.ServiceLocator.GetInstance<ILexSenseFactory>().Create();
                entry.SensesOS.Add(second);
                second.Gloss.set_String(cache.DefaultAnalWs, Anal(cache, "sell"));
                var domains = cache.LangProject.SemanticDomainListOA;
                w.Domain = cache.ServiceLocator.GetInstance<ICmSemanticDomainFactory>().Create();
                domains.PossibilitiesOS.Add(w.Domain);
                w.Domain.Name.set_String(cache.DefaultAnalWs, Anal(cache, "Buy"));
                w.OtherDomain = cache.ServiceLocator.GetInstance<ICmSemanticDomainFactory>().Create();
                domains.PossibilitiesOS.Add(w.OtherDomain);
                w.OtherDomain.Name.set_String(cache.DefaultAnalWs, Anal(cache, "Sell"));
                sense.SemanticDomainsRC.Add(w.Domain);
                cache.DomainDataByFlid.SetInt(entry.Hvo, w.Count, 42);
                cache.DomainDataByFlid.SetUnicode(entry.Hvo, w.Remark, "custom text", "custom text".Length);
            });
            return w;
        }

        static ProjectRecordExporter Exporter(ProjectFixture fixture, string overlay)
            => new ProjectRecordExporter(fixture.Cache, ProjectRecordSchema.Generate(fixture.Cache, overlay));

        static ProjectRecordExporter LexiconExporter(ProjectFixture fixture) => Exporter(fixture, ProjectRecordSchema.LexiconEntryOverlay);

        /// <summary>The export's file with the edit made in its document and written again.</summary>
        static string Edited(ProjectRecordExporter exporter, ProjectRecordExport export, Action<RecordDocument> edit)
        {
            var read = RecordFileReader.Read(export.Text!, exporter.Schema, exporter.CreateReadOptions());
            Assert.False(read.HasErrors);
            edit(read.Document!);
            return RecordFileWriter.Write(read.Document!, exporter.Schema);
        }

        static ProjectRecordPlan Plan(ProjectRecordExporter exporter, string text, IWritableFields? writable = null) => new ProjectRecordImporter(exporter).Plan(text, writable);

        static RecordObject Obj(RecordValue? v) => (RecordObject)v!;
        static RecordObject SenseAt(RecordDocument d, int index) => (RecordObject)((RecordList)d.Data["senses"]!).Items[index];
        static string Link(ICmObject target, string name) => "[" + name + "](/objects/" + target.Guid.ToString("D") + "){.name guid=" + target.Guid.ToString("D") + "}";

        static RecordRefusal Single(ProjectRecordPlan plan, RecordRefusalKind kind)
            => Assert.Single(plan.ChangeSet.Refusals, r => r.Kind == kind);

        sealed class Writable : IWritableFields
        {
            public Func<string, string, bool> Write = (c, f) => true;
            public Func<string, string, string, bool> Create = (o, f, c) => true;
            public Func<string, string, string, bool> Delete = (o, f, c) => true;
            public readonly List<string> Asked = new List<string>();
            public bool CanWrite(string className, string fieldName) { Asked.Add("write " + className + "." + fieldName); return Write(className, fieldName); }
            public bool CanCreate(string ownerClassName, string ownerFieldName, string className) { Asked.Add("create " + ownerClassName + "." + ownerFieldName + " " + className); return Create(ownerClassName, ownerFieldName, className); }
            public bool CanDelete(string ownerClassName, string ownerFieldName, string className) { Asked.Add("delete " + ownerClassName + "." + ownerFieldName + " " + className); return Delete(ownerClassName, ownerFieldName, className); }
        }

        // ------------------------------------------------------------------ no-op and read-only

        [Fact]
        public void AnUnchangedExportOfEachKindPlansNothingAndChangesNothing()
        {
            using var fixture = new ProjectFixture();
            var cache = fixture.Cache;
            var w = Build(fixture);
            int weather = AddCustom(cache, "RnGenericRec", "Weather", CellarPropertyType.Unicode);
            fixture.Change(() =>
            {
                var notebook = cache.LangProject.ResearchNotebookOA ?? (cache.LangProject.ResearchNotebookOA = cache.ServiceLocator.GetInstance<IRnResearchNbkFactory>().Create());
                var record = cache.ServiceLocator.GetInstance<IRnGenericRecFactory>().Create();
                notebook.RecordsOC.Add(record);
                record.Title = Anal(cache, "Survey");
                record.DescriptionOA = cache.ServiceLocator.GetInstance<IStTextFactory>().Create();
                var para = cache.ServiceLocator.GetInstance<IStTxtParaFactory>().Create();
                record.DescriptionOA.ParagraphsOS.Add(para);
                para.Contents = Anal(cache, "Some words.");
                var sub = cache.ServiceLocator.GetInstance<IRnGenericRecFactory>().Create();
                record.SubRecordsOS.Add(sub);
                sub.Title = Anal(cache, "Follow-up");
                cache.DomainDataByFlid.SetUnicode(record.Hvo, weather, "dry", 3);
                var item = cache.ServiceLocator.GetInstance<ICmPossibilityFactory>().Create();
                cache.LangProject.LexDbOA.PublicationTypesOA.PossibilitiesOS.Add(item);
                item.Name.set_String(cache.DefaultAnalWs, Anal(cache, "Dictionary"));
                var child = cache.ServiceLocator.GetInstance<ICmPossibilityFactory>().Create();
                item.SubPossibilitiesOS.Add(child);
                child.Name.set_String(cache.DefaultAnalWs, Anal(cache, "Concise"));
            });
            var repository = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
            Dictionary<Guid, DateTime> Snapshot() => repository.AllInstances().OfType<ICmObject>().ToDictionary(o => o.Guid, o => (o as ICmMajorObject)?.DateModified ?? default);
            var before = Snapshot();
            bool dirty = cache.ActionHandlerAccessor.CanUndo();
            int planned = 0;
            foreach (var (kind, overlay) in new[] { ("lexicon/entry", ProjectRecordSchema.LexiconEntryOverlay), ("notebook/record", ProjectRecordSchema.NotebookRecordOverlay), ("lists/item", ProjectRecordSchema.ListItemOverlay) })
            {
                var exporter = Exporter(fixture, overlay);
                var importer = new ProjectRecordImporter(exporter);
                foreach (var export in exporter.ExportAll(kind))
                {
                    var plan = importer.Plan(export.Text!);
                    Assert.True(plan.IsEmpty, kind + " " + export.Path + ": " + string.Join("; ", plan.ChangeSet.Refusals) + string.Join("; ", plan.ChangeSet.Changes));
                    Assert.Equal(ProjectRecordBaseVerdict.Fresh, plan.BaseVerdict);
                    Assert.Equal(export.Guid, plan.RecordGuid);
                    planned++;
                }
            }
            Assert.True(planned >= 3);
            var after = Snapshot();
            Assert.Equal(before.Count, after.Count);
            Assert.True(before.All(b => after.TryGetValue(b.Key, out var d) && d == b.Value), "an object appeared, vanished or changed its modified time");
            Assert.Equal(dirty, cache.ActionHandlerAccessor.CanUndo());
            Assert.Equal(0, cache.ActionHandlerAccessor.CurrentDepth);
        }

        [Fact]
        public void PlanningAnEditChangesNothingInTheProjectEither()
        {
            using var fixture = new ProjectFixture();
            var w = Build(fixture);
            var exporter = LexiconExporter(fixture);
            var export = exporter.Export(w.Entry.Guid);
            var repository = fixture.Cache.ServiceLocator.GetInstance<ICmObjectRepository>();
            int count = repository.AllInstances().Count();
            var modified = w.Entry.DateModified;
            var text = Edited(exporter, export, d => SenseAt(d, 0)["gloss"] = new RecordAlternatives().Add("en", new RecordScalar("purchase")));
            var plan = Plan(exporter, text);
            Assert.Single(plan.ChangeSet.Changes);
            Assert.Equal(count, repository.AllInstances().Count());
            Assert.Equal(modified, w.Entry.DateModified);
            Assert.Equal("buy", w.Sense.Gloss.get_String(fixture.Cache.DefaultAnalWs).Text);
        }

        // ------------------------------------------------------------------ every kind of field

        [Fact]
        public void EveryFieldKindOfAnEntryCanBeEditedAndIsPlannedWithoutRefusal()
        {
            using var fixture = new ProjectFixture();
            var w = Build(fixture);
            var exporter = LexiconExporter(fixture);
            var export = exporter.Export(w.Entry.Guid);
            var text = Edited(exporter, export, d =>
            {
                d.Data["do-not-use-for-parsing"] = RecordScalar.Of(false);
                d.Data["custom-LexEntry-Count"] = RecordScalar.Of(43);
                d.Data["custom-LexEntry-Remark"] = new RecordScalar("other text");
                ((RecordAlternatives)d.Data["citation-form"]!).Add("en", new RecordScalar("buy it"));
                ((RecordAlternatives)d.Data["comment"]!).Add("en", new RecordScalar("plain [changed]{.v}"));
                var sense = SenseAt(d, 0);
                ((RecordAlternatives)sense["gloss"]!).Add("en", new RecordScalar("purchase"));
                ((RecordAlternatives)sense["definition"]!).Add("en", new RecordScalar("to pay"));
                Obj(d.Data["lexeme-form"])["morph-type"] = new RecordScalar(Link(w.OtherMorphType, "other"));
                ((RecordList)sense["semantic-domains"]!).Add(new RecordScalar(Link(w.OtherDomain, "Sell")));
                ((RecordList)d.Data["senses"]!).Add(new RecordObject { Class = "LexSense" }.Set("gloss", new RecordAlternatives().Add("en", new RecordScalar("rent"))));
            });
            var plan = Plan(exporter, text);
            Assert.Empty(plan.ChangeSet.Refusals);
            Assert.Equal(ProjectRecordBaseVerdict.Fresh, plan.BaseVerdict);
            var kinds = plan.ChangeSet.Changes.Select(c => c.Kind + " " + c.Field).ToList();
            Assert.Contains("Set do-not-use-for-parsing", kinds);
            Assert.Contains("Set custom-LexEntry-Count", kinds);
            Assert.Contains("Set custom-LexEntry-Remark", kinds);
            Assert.Contains("SetAlternative citation-form", kinds);
            Assert.Contains("SetRich comment", kinds);
            Assert.Contains("SetAlternative gloss", kinds);
            Assert.Contains("SetRich definition", kinds);
            Assert.Contains("Set morph-type", kinds);
            Assert.Contains("AddReference semantic-domains", kinds);
            Assert.Contains("AddObject senses", kinds);
            var morph = plan.ChangeSet.Changes.Single(c => c.Field == "morph-type");
            Assert.Equal(w.OtherMorphType.Guid, morph.NewTarget);
            Assert.NotNull(plan.Export);
            Assert.False(plan.ChangeSet.HasOmissions);
        }

        [Fact]
        public void ReorderingSensesAndClearingAFieldByOmissionArePlanned()
        {
            using var fixture = new ProjectFixture();
            var w = Build(fixture);
            var exporter = LexiconExporter(fixture);
            var export = exporter.Export(w.Entry.Guid);
            var text = Edited(exporter, export, d =>
            {
                var senses = (RecordList)d.Data["senses"]!;
                (senses.Items[0], senses.Items[1]) = (senses.Items[1], senses.Items[0]);
                d.Data["comment"] = null;
                d.Data["custom-LexEntry-Remark"] = null;
            });
            var plan = Plan(exporter, text);
            Assert.Empty(plan.ChangeSet.Refusals);
            Assert.Contains(plan.ChangeSet.Changes, c => c.Kind == RecordChangeKind.MoveObject);
            Assert.Contains(plan.ChangeSet.Changes, c => c.Kind == RecordChangeKind.ClearRich && c.ByOmission);
            Assert.Contains(plan.ChangeSet.Changes, c => c.Kind == RecordChangeKind.Clear && c.ByOmission && c.Field == "custom-LexEntry-Remark");
        }

        [Fact]
        public void ANotebookRecordAndAListItemCanBeEdited()
        {
            using var fixture = new ProjectFixture();
            var cache = fixture.Cache;
            IRnGenericRec record = null!;
            ICmPossibility item = null!;
            fixture.Change(() =>
            {
                var notebook = cache.LangProject.ResearchNotebookOA ?? (cache.LangProject.ResearchNotebookOA = cache.ServiceLocator.GetInstance<IRnResearchNbkFactory>().Create());
                record = cache.ServiceLocator.GetInstance<IRnGenericRecFactory>().Create();
                notebook.RecordsOC.Add(record);
                record.Title = Anal(cache, "Kaon survey");
                record.DescriptionOA = cache.ServiceLocator.GetInstance<IStTextFactory>().Create();
                var para = cache.ServiceLocator.GetInstance<IStTxtParaFactory>().Create();
                record.DescriptionOA.ParagraphsOS.Add(para);
                para.Contents = Anal(cache, "On 3 May the speakers used kaon.");
                item = cache.ServiceLocator.GetInstance<ICmPossibilityFactory>().Create();
                cache.LangProject.LexDbOA.PublicationTypesOA.PossibilitiesOS.Add(item);
                item.Name.set_String(cache.DefaultAnalWs, Anal(cache, "Dictionary"));
                item.DiscussionOA = cache.ServiceLocator.GetInstance<IStTextFactory>().Create();
                var discussion = cache.ServiceLocator.GetInstance<IStTxtParaFactory>().Create();
                item.DiscussionOA.ParagraphsOS.Add(discussion);
                discussion.Contents = Anal(cache, "Printed.");
            });
            var notebookExporter = Exporter(fixture, ProjectRecordSchema.NotebookRecordOverlay);
            var notebookExport = notebookExporter.Export(record.Guid);
            var notebookText = Edited(notebookExporter, notebookExport, d => d.Data["description"] = new RecordScalar("On 4 May the speakers used kaon."));
            var notebookPlan = Plan(notebookExporter, notebookText);
            Assert.Empty(notebookPlan.ChangeSet.Refusals);
            var change = Assert.Single(notebookPlan.ChangeSet.Changes);
            Assert.Equal(RecordChangeKind.SetRich, change.Kind);
            Assert.Equal(record.Guid, change.ObjectGuid);

            var listExporter = Exporter(fixture, ProjectRecordSchema.ListItemOverlay);
            var listExport = listExporter.Export(item.Guid);
            var listText = Edited(listExporter, listExport, d =>
            {
                ((RecordAlternatives)d.Data["name"]!).Add("en", new RecordScalar("Dictionaries"));
                d.Data["discussion"] = new RecordScalar("Printed and online.");
            });
            var listPlan = Plan(listExporter, listText);
            Assert.Empty(listPlan.ChangeSet.Refusals);
            Assert.Equal(2, listPlan.ChangeSet.Changes.Count);
            Assert.All(listPlan.ChangeSet.Changes, c => Assert.Equal(item.Guid, c.ObjectGuid));
        }

        // ------------------------------------------------------------------ the base

        [Fact]
        public void AProjectEditAfterTheExportMakesTheWholeFileStale()
        {
            using var fixture = new ProjectFixture();
            var w = Build(fixture);
            var exporter = LexiconExporter(fixture);
            var export = exporter.Export(w.Entry.Guid);
            Assert.Matches("^sha256:[0-9a-f]{64}$", export.Base);
            Assert.Contains("base: ", export.Text);
            var text = Edited(exporter, export, d => d.Data["do-not-use-for-parsing"] = RecordScalar.Of(false));
            fixture.Change(() => w.Second.Gloss.set_String(fixture.Cache.DefaultAnalWs, Anal(fixture.Cache, "trade")));
            var plan = Plan(exporter, text);
            Assert.Equal(ProjectRecordBaseVerdict.Stale, plan.BaseVerdict);
            Assert.Empty(plan.ChangeSet.Changes);
            Single(plan, RecordRefusalKind.StaleBase);
        }

        [Fact]
        public void AChangeToAValueTheFileCannotShowStalesTheFileToo()
        {
            using var fixture = new ProjectFixture();
            var w = Build(fixture);
            var exporter = LexiconExporter(fixture);
            var export = exporter.Export(w.Entry.Guid);
            Assert.Contains(export.Unexported, u => u.Field == "DateModified");
            fixture.Change(() => w.Entry.DateModified = w.Entry.DateModified.AddDays(1));
            var plan = Plan(exporter, export.Text!);
            Assert.Equal(ProjectRecordBaseVerdict.Stale, plan.BaseVerdict);
            Single(plan, RecordRefusalKind.StaleBase);
            Assert.Empty(plan.ChangeSet.Changes);
        }

        [Fact]
        public void TheDigestIsStableAndDependsOnTheTextAndTheUnexportedValues()
        {
            using var fixture = new ProjectFixture();
            var w = Build(fixture);
            var exporter = LexiconExporter(fixture);
            var first = exporter.Export(w.Entry.Guid);
            var second = exporter.Export(w.Entry.Guid);
            Assert.Equal(first.Base, second.Base);
            Assert.Equal(first.Text, second.Text);
            Assert.NotEqual(RecordBaseDigest.Compute("a", first.Unexported), RecordBaseDigest.Compute("b", first.Unexported));
            Assert.NotEqual(RecordBaseDigest.Compute("a", first.Unexported), RecordBaseDigest.Compute("a", new UnexportedValue[0]));
        }

        [Fact]
        public void AFileWithoutABaseIsRefusedWhole()
        {
            using var fixture = new ProjectFixture();
            var w = Build(fixture);
            var exporter = LexiconExporter(fixture);
            var text = Edited(exporter, exporter.Export(w.Entry.Guid), d => d.Base = null);
            var plan = Plan(exporter, text);
            Assert.Equal(ProjectRecordBaseVerdict.Missing, plan.BaseVerdict);
            Single(plan, RecordRefusalKind.MissingBase);
            Assert.Empty(plan.ChangeSet.Changes);
        }

        [Fact]
        public void TextThatIsNotARecordOfTheProjectIsRefusedByName()
        {
            using var fixture = new ProjectFixture();
            var w = Build(fixture);
            var exporter = LexiconExporter(fixture);
            Single(Plan(exporter, "hello"), RecordRefusalKind.UnreadableFile);
            var export = exporter.Export(w.Entry.Guid);
            var noGuid = Edited(exporter, export, d => d.Data.Guid = null);
            Single(Plan(exporter, noGuid), RecordRefusalKind.UnknownRecord);
            var gone = Edited(exporter, export, d => d.Data.Guid = Guid.NewGuid());
            Single(Plan(exporter, gone), RecordRefusalKind.UnknownRecord);
            Assert.Equal(ProjectRecordBaseVerdict.NotChecked, Plan(exporter, gone).BaseVerdict);
        }

        // ------------------------------------------------------------------ references, writing systems, styles

        [Fact]
        public void AReferenceToAMissingObjectIsRefusedAndTheRestOfTheFileStillPlans()
        {
            using var fixture = new ProjectFixture();
            var w = Build(fixture);
            var exporter = LexiconExporter(fixture);
            var ghost = Guid.NewGuid();
            var text = Edited(exporter, exporter.Export(w.Entry.Guid), d =>
            {
                Obj(d.Data["lexeme-form"])["morph-type"] = new RecordScalar("[ghost](/objects/" + ghost.ToString("D") + "){.name guid=" + ghost.ToString("D") + "}");
                d.Data["custom-LexEntry-Count"] = RecordScalar.Of(7);
            });
            var plan = Plan(exporter, text);
            var refusal = Single(plan, RecordRefusalKind.UnknownReference);
            Assert.Equal("morph-type", refusal.Field);
            Assert.Equal("custom-LexEntry-Count", Assert.Single(plan.ChangeSet.Changes).Field);
        }

        [Fact]
        public void AReferenceToTheWrongClassOfObjectIsRefused()
        {
            using var fixture = new ProjectFixture();
            var w = Build(fixture);
            var exporter = LexiconExporter(fixture);
            var text = Edited(exporter, exporter.Export(w.Entry.Guid), d =>
                Obj(d.Data["lexeme-form"])["morph-type"] = new RecordScalar(Link(w.Domain, "Buy")));
            var plan = Plan(exporter, text);
            Single(plan, RecordRefusalKind.WrongReferenceClass);
            Assert.Empty(plan.ChangeSet.Changes);
        }

        [Fact]
        public void AReferenceListItemThatDoesNotFitIsRefused()
        {
            using var fixture = new ProjectFixture();
            var w = Build(fixture);
            var exporter = LexiconExporter(fixture);
            var text = Edited(exporter, exporter.Export(w.Entry.Guid), d =>
                ((RecordList)SenseAt(d, 0)["semantic-domains"]!).Add(new RecordScalar(Link(w.OtherMorphType, "other"))));
            Single(Plan(exporter, text), RecordRefusalKind.WrongReferenceClass);
        }

        [Fact]
        public void AReferenceInsideANewObjectIsChecked()
        {
            using var fixture = new ProjectFixture();
            var w = Build(fixture);
            var exporter = LexiconExporter(fixture);
            var ghost = Guid.NewGuid();
            var text = Edited(exporter, exporter.Export(w.Entry.Guid), d =>
                ((RecordList)d.Data["senses"]!).Add(new RecordObject { Class = "LexSense" }.Set("semantic-domains",
                    new RecordList().Add(new RecordScalar("[ghost](/objects/" + ghost.ToString("D") + "){.name guid=" + ghost.ToString("D") + "}")))));
            var plan = Plan(exporter, text);
            Single(plan, RecordRefusalKind.UnknownReference);
            Assert.Empty(plan.ChangeSet.Changes);
        }

        [Fact]
        public void AnUnknownWritingSystemAndAnUndefinedStyleAreRefused()
        {
            using var fixture = new ProjectFixture();
            var w = Build(fixture);
            var exporter = LexiconExporter(fixture);
            var export = exporter.Export(w.Entry.Guid);
            var ws = Edited(exporter, export, d => ((RecordAlternatives)d.Data["comment"]!).Add("en", new RecordScalar("plain [x]{ws=zzq}")));
            var plan = Plan(exporter, ws);
            Single(plan, RecordRefusalKind.UnknownWritingSystem);
            Assert.Empty(plan.ChangeSet.Changes);
            Assert.Equal(ProjectRecordBaseVerdict.Fresh, plan.BaseVerdict);

            var style = Edited(exporter, export, d => ((RecordAlternatives)d.Data["comment"]!).Add("en", new RecordScalar("plain [x]{custom-style=\"Nonesuch\"}")));
            Single(Plan(exporter, style), RecordRefusalKind.UndefinedStyle);
        }

        // ------------------------------------------------------------------ computed and unexported

        [Fact]
        public void AnEditToAComputedFieldIsRefusedButAnUnchangedOneIsNot()
        {
            using var fixture = new ProjectFixture();
            var w = Build(fixture);
            var exporter = LexiconExporter(fixture);
            var export = exporter.Export(w.Entry.Guid);
            Assert.NotNull(export.Document.Data["head-word"]);
            var text = Edited(exporter, export, d => d.Data["head-word"] = new RecordScalar("[other]{.v}"));
            var plan = Plan(exporter, text);
            Single(plan, RecordRefusalKind.ComputedField);
            Assert.Empty(plan.ChangeSet.Changes);
            Assert.True(Plan(exporter, export.Text!).IsEmpty);
        }

        [Fact]
        public void AValueTheExportLeftOutCannotBeNamedInAFile()
        {
            using var fixture = new ProjectFixture();
            var w = Build(fixture);
            var exporter = LexiconExporter(fixture);
            var export = exporter.Export(w.Entry.Guid);
            var text = export.Text!.Replace("record-data:\n", "record-data:\n  date-modified: \"2030-01-01\"\n");
            Assert.NotEqual(export.Text, text);
            var plan = Plan(exporter, text);
            Single(plan, RecordRefusalKind.UnknownField);
            Assert.Empty(plan.ChangeSet.Changes);
        }

        [Fact]
        public void AFieldWhoseFormattingTheExportCouldNotHoldIsLockedAgainstEdits()
        {
            using var fixture = new ProjectFixture();
            var w = Build(fixture);
            var cache = fixture.Cache;
            fixture.Change(() =>
            {
                var bold = TsStringUtils.MakeStrBldr();
                bold.Replace(0, 0, "trade", TsStringUtils.MakeProps("", cache.DefaultAnalWs));
                bold.SetIntPropValues(0, 5, (int)FwTextPropType.ktptBold, (int)FwTextPropVar.ktpvEnum, (int)FwTextToggleVal.kttvForceOn);
                w.Second.Gloss.set_String(cache.DefaultAnalWs, bold.GetString());
            });
            var exporter = LexiconExporter(fixture);
            var export = exporter.Export(w.Entry.Guid);
            Assert.Contains(export.Unexported, u => u.Object == w.Second.Guid && u.Field == "Gloss");
            var text = Edited(exporter, export, d =>
            {
                ((RecordAlternatives)SenseAt(d, 1)["gloss"]!).Add("en", new RecordScalar("barter"));
                ((RecordAlternatives)SenseAt(d, 0)["gloss"]!).Add("en", new RecordScalar("purchase"));
            });
            var plan = Plan(exporter, text);
            var refusal = Single(plan, RecordRefusalKind.UnexportedValue);
            Assert.Equal(w.Second.Guid, refusal.ObjectGuid);
            var change = Assert.Single(plan.ChangeSet.Changes);
            Assert.Equal(w.Sense.Guid, change.ObjectGuid);
        }

        [Fact]
        public void OmittingAFieldThatHoldsUnexportedFormattingIsRefusedToo()
        {
            using var fixture = new ProjectFixture();
            var w = Build(fixture);
            var cache = fixture.Cache;
            fixture.Change(() =>
            {
                var bold = TsStringUtils.MakeStrBldr();
                bold.Replace(0, 0, "trade", TsStringUtils.MakeProps("", cache.DefaultAnalWs));
                bold.SetIntPropValues(0, 5, (int)FwTextPropType.ktptBold, (int)FwTextPropVar.ktpvEnum, (int)FwTextToggleVal.kttvForceOn);
                w.Second.Gloss.set_String(cache.DefaultAnalWs, bold.GetString());
            });
            var exporter = LexiconExporter(fixture);
            var text = Edited(exporter, exporter.Export(w.Entry.Guid), d => SenseAt(d, 1)["gloss"] = null);
            var plan = Plan(exporter, text);
            var refusal = Single(plan, RecordRefusalKind.UnexportedValue);
            Assert.Equal(w.Second.Guid, refusal.ObjectGuid);
            Assert.Empty(plan.ChangeSet.Changes);
        }

        // ------------------------------------------------------------------ what the host can write

        [Fact]
        public void TheHostsWritableFieldsRefuseFieldsCreationAndDeletionAndNullSkipsTheCheck()
        {
            using var fixture = new ProjectFixture();
            var w = Build(fixture);
            var exporter = LexiconExporter(fixture);
            var export = exporter.Export(w.Entry.Guid);
            var text = Edited(exporter, export, d =>
            {
                d.Data["custom-LexEntry-Count"] = RecordScalar.Of(43);
                ((RecordAlternatives)SenseAt(d, 0)["gloss"]!).Add("en", new RecordScalar("purchase"));
                ((RecordList)d.Data["senses"]!).Add(new RecordObject { Class = "LexSense" }.Set("gloss", new RecordAlternatives().Add("en", new RecordScalar("rent"))));
                ((RecordList)d.Data["senses"]!).Items.RemoveAt(1);
            });
            var open = Plan(exporter, text, null);
            Assert.Empty(open.ChangeSet.Refusals);
            Assert.Equal(4, open.ChangeSet.Changes.Count);

            var host = new Writable { Write = (c, f) => f != "Gloss", Create = (o, f, c) => false, Delete = (o, f, c) => false };
            var plan = Plan(exporter, text, host);
            Single(plan, RecordRefusalKind.FieldNotWritable);
            var create = Single(plan, RecordRefusalKind.CreateNotWritable);
            Assert.Contains("LexSense", create.Reason);
            Single(plan, RecordRefusalKind.DeleteNotWritable);
            Assert.Equal("custom-LexEntry-Count", Assert.Single(plan.ChangeSet.Changes).Field);
            Assert.Contains("write LexSense.Gloss", host.Asked);
            Assert.Contains("write LexEntry.Count", host.Asked);
            Assert.Contains(host.Asked, a => a.StartsWith("create LexEntry.Senses LexSense", StringComparison.Ordinal));
            Assert.Contains(host.Asked, a => a.StartsWith("delete LexEntry.Senses LexSense", StringComparison.Ordinal));

            var allowing = Plan(exporter, text, new Writable());
            Assert.Empty(allowing.ChangeSet.Refusals);
            Assert.Equal(4, allowing.ChangeSet.Changes.Count);
        }

        // ------------------------------------------------------------------ delete by omission

        [Fact]
        public void ASenseWithNothingHiddenAndNoReferencesCanBeDeletedByOmission()
        {
            using var fixture = new ProjectFixture();
            var w = Build(fixture);
            var exporter = LexiconExporter(fixture);
            var text = Edited(exporter, exporter.Export(w.Entry.Guid), d => ((RecordList)d.Data["senses"]!).Items.RemoveAt(1));
            var plan = Plan(exporter, text);
            Assert.Empty(plan.ChangeSet.Refusals);
            var change = Assert.Single(plan.ChangeSet.Changes);
            Assert.Equal(RecordChangeKind.RemoveObject, change.Kind);
            Assert.True(change.ByOmission);
            Assert.Equal(w.Second.Guid, change.ItemGuid);
            Assert.True(plan.ChangeSet.HasOmissions);
        }

        [Fact]
        public void ADeleteByOmissionIsRefusedWhenTheObjectHoldsUnexportedData()
        {
            using var fixture = new ProjectFixture();
            var w = Build(fixture);
            var cache = fixture.Cache;
            int blob = AddCustom(cache, "LexSense", "Blob", CellarPropertyType.Binary);
            fixture.Change(() =>
            {
                cache.DomainDataByFlid.SetBinary(w.Second.Hvo, blob, new byte[] { 1, 2, 3 }, 3);
            });
            var exporter = LexiconExporter(fixture);
            var export = exporter.Export(w.Entry.Guid);
            Assert.Contains(export.Unexported, u => u.Object == w.Second.Guid && u.Field == "Blob");
            var text = Edited(exporter, export, d => ((RecordList)d.Data["senses"]!).Items.RemoveAt(1));
            var plan = Plan(exporter, text);
            var refusal = Single(plan, RecordRefusalKind.DeleteHasUnexportedData);
            Assert.Contains("Blob", refusal.Reason);
            Assert.Empty(plan.ChangeSet.Changes);
        }

        [Fact]
        public void ACustomTimeFieldIsDataAndBlocksADeleteWhileLibLcmsOwnStampsDoNot()
        {
            using var fixture = new ProjectFixture();
            var w = Build(fixture);
            var cache = fixture.Cache;
            int when = AddCustom(cache, "LexSense", "Visited", CellarPropertyType.Time);
            fixture.Change(() => cache.DomainDataByFlid.SetTime(w.Second.Hvo, when, SIL.LCModel.Core.Cellar.SilTime.ConvertToSilTime(new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc))));
            var exporter = LexiconExporter(fixture);
            var export = exporter.Export(w.Entry.Guid);
            Assert.Contains(export.Unexported, u => u.Object == w.Second.Guid && u.Field == "Visited");
            var text = Edited(exporter, export, d => ((RecordList)d.Data["senses"]!).Items.RemoveAt(1));
            var plan = Plan(exporter, text);
            Assert.Contains("Visited", Single(plan, RecordRefusalKind.DeleteHasUnexportedData).Reason);
        }

        [Fact]
        public void ADeleteByOmissionIsRefusedWhenAnotherObjectRefersToTheObject()
        {
            using var fixture = new ProjectFixture();
            var cache = fixture.Cache;
            var w = Build(fixture);
            ICmPossibility parent = null!, referenced = null!, free = null!;
            fixture.Change(() =>
            {
                parent = cache.ServiceLocator.GetInstance<ICmPossibilityFactory>().Create();
                cache.LangProject.LexDbOA.PublicationTypesOA.PossibilitiesOS.Add(parent);
                parent.Name.set_String(cache.DefaultAnalWs, Anal(cache, "Dictionary"));
                referenced = cache.ServiceLocator.GetInstance<ICmPossibilityFactory>().Create();
                parent.SubPossibilitiesOS.Add(referenced);
                referenced.Name.set_String(cache.DefaultAnalWs, Anal(cache, "Concise"));
                free = cache.ServiceLocator.GetInstance<ICmPossibilityFactory>().Create();
                parent.SubPossibilitiesOS.Add(free);
                free.Name.set_String(cache.DefaultAnalWs, Anal(cache, "Pocket"));
                w.Sense.DoNotPublishInRC.Add(referenced);
            });
            var exporter = Exporter(fixture, ProjectRecordSchema.ListItemOverlay);
            var export = exporter.Export(parent.Guid);

            var both = Edited(exporter, export, d => ((RecordList)d.Data["sub-possibilities"]!).Items.Clear());
            var plan = Plan(exporter, both);
            var refusal = Single(plan, RecordRefusalKind.DeleteHasIncomingReferences);
            Assert.Equal(parent.Guid, refusal.ObjectGuid);
            Assert.Contains("refers to", refusal.Reason);
            var deleted = Assert.Single(plan.ChangeSet.Changes);
            Assert.Equal(free.Guid, deleted.ItemGuid);
            Assert.Equal(RecordChangeKind.RemoveObject, deleted.Kind);
        }

        // ------------------------------------------------------------------ homographs, unnamed items, partial files, anchors

        static ProjectRecordPlan DeleteSecondSense(World w, out ProjectRecordExporter exporter)
        {
            exporter = LexiconExporter(w.Fixture);
            var text = Edited(exporter, exporter.Export(w.Entry.Guid), d => ((RecordList)d.Data["senses"]!).Items.RemoveAt(1));
            return Plan(exporter, text);
        }

        [Fact]
        public void ALinkInsideRichTextBlocksADeleteByOmission()
        {
            using var fixture = new ProjectFixture();
            var w = Build(fixture);
            var cache = fixture.Cache;
            fixture.Change(() =>
            {
                var other = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create();
                var sense = cache.ServiceLocator.GetInstance<ILexSenseFactory>().Create();
                other.SensesOS.Add(sense);
                var b = TsStringUtils.MakeStrBldr();
                b.Replace(0, 0, "see sell", TsStringUtils.MakeProps("", cache.DefaultAnalWs));
                b.SetStrPropValue(0, 8, (int)FwTextPropType.ktptObjData, "E" + "silfw://localhost/link?database=x&tool=default&guid=" + w.Second.Guid.ToString("D"));
                sense.Definition.set_String(cache.DefaultAnalWs, b.GetString());
            });
            var plan = DeleteSecondSense(w, out _);
            var refusal = Single(plan, RecordRefusalKind.DeleteHasIncomingReferences);
            Assert.Contains("link", refusal.Reason);
            Assert.Empty(plan.ChangeSet.Changes);
        }

        [Fact]
        public void ASequenceReferenceAndACustomReferenceBlockADeleteButOneInsideTheDeletedSetDoesNot()
        {
            using var fixture = new ProjectFixture();
            var w = Build(fixture);
            var cache = fixture.Cache;
            var sensesOnly = DeleteSecondSense(w, out _);
            Assert.Empty(sensesOnly.ChangeSet.Refusals);

            ILexEntryRef reference = null!;
            fixture.Change(() =>
            {
                var other = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create();
                reference = cache.ServiceLocator.GetInstance<ILexEntryRefFactory>().Create();
                other.EntryRefsOS.Add(reference);
                reference.ComponentLexemesRS.Add(w.Second);
            });
            Single(DeleteSecondSense(w, out _), RecordRefusalKind.DeleteHasIncomingReferences);
            fixture.Change(() => reference.ComponentLexemesRS.Clear());
            Assert.Empty(DeleteSecondSense(w, out _).ChangeSet.Refusals);

            int custom = ((IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor).AddCustomField("LexEntry", "Pointer", CellarPropertyType.ReferenceAtom, cache.MetaDataCacheAccessor.GetClassId("LexSense"));
            fixture.Change(() => cache.DomainDataByFlid.SetObjProp(w.Entry.Hvo, custom, w.Second.Hvo));
            var plan = DeleteSecondSense(w, out _);
            // The entry is not inside the deleted sense, so its custom reference blocks the delete.
            Single(plan, RecordRefusalKind.DeleteHasIncomingReferences);
        }

        [Fact]
        public void AReferrerInsideTheDeletedObjectDoesNotBlockItsDelete()
        {
            using var fixture = new ProjectFixture();
            var w = Build(fixture);
            var cache = fixture.Cache;
            fixture.Change(() =>
            {
                // A sense's own example sentence referring back to the sense through a rich-text link.
                var example = w.Second.ExamplesOS.Count > 0 ? w.Second.ExamplesOS[0] : cache.ServiceLocator.GetInstance<ILexExampleSentenceFactory>().Create();
                if (w.Second.ExamplesOS.Count == 0) w.Second.ExamplesOS.Add(example);
                var b = TsStringUtils.MakeStrBldr();
                b.Replace(0, 0, "me", TsStringUtils.MakeProps("", cache.DefaultAnalWs));
                b.SetStrPropValue(0, 2, (int)FwTextPropType.ktptObjData, "E" + "silfw://localhost/link?guid=" + w.Second.Guid.ToString("D"));
                example.Example.set_String(cache.DefaultAnalWs, b.GetString());
            });
            var plan = DeleteSecondSense(w, out _);
            Assert.Empty(plan.ChangeSet.Refusals);
            Assert.Single(plan.ChangeSet.Changes);
        }

        [Fact]
        public void ANewObjectWithAGuidTheProjectAlreadyHasIsRefused()
        {
            using var fixture = new ProjectFixture();
            var w = Build(fixture);
            var exporter = LexiconExporter(fixture);
            var text = Edited(exporter, exporter.Export(w.Entry.Guid), d =>
                ((RecordList)d.Data["senses"]!).Add(new RecordObject { Class = "LexSense", Guid = w.OtherDomain.Guid }.Set("gloss", new RecordAlternatives().Add("en", new RecordScalar("x")))));
            var plan = Plan(exporter, text);
            Single(plan, RecordRefusalKind.GuidInUse);
            Assert.Empty(plan.ChangeSet.Changes);
        }

        [Fact]
        public void AnExportThatCouldNotBeWrittenRefusesTheWholeFile()
        {
            using var fixture = new ProjectFixture();
            var w = Build(fixture);
            var exporter = LexiconExporter(fixture);
            var export = exporter.Export(w.Entry.Guid);
            var importer = new ProjectRecordImporter(exporter)
            {
                ExportOverride = (k, g) => new ProjectRecordExport(export.Kind, export.Guid, export.Path, null, export.Document, export.Unexported),
            };
            var plan = importer.Plan(export.Text!);
            Single(plan, RecordRefusalKind.ExportFailed);
            Assert.Empty(plan.ChangeSet.Changes);
        }

        [Fact]
        public void HomographsArePlannedByTheirOwnGuid()
        {
            using var fixture = new ProjectFixture();
            var cache = fixture.Cache;
            var entries = new List<ILexEntry>();
            fixture.Change(() =>
            {
                for (int i = 0; i < 2; i++)
                {
                    var entry = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create();
                    var allomorph = cache.ServiceLocator.GetInstance<IMoStemAllomorphFactory>().Create();
                    entry.LexemeFormOA = allomorph;
                    allomorph.Form.set_String(cache.DefaultVernWs, Vern(cache, "palit"));
                    allomorph.MorphTypeRA = (IMoMorphType)cache.LangProject.LexDbOA.MorphTypesOA.PossibilitiesOS.First();
                    entry.CitationForm.set_String(cache.DefaultVernWs, Vern(cache, "palit"));
                    var sense = cache.ServiceLocator.GetInstance<ILexSenseFactory>().Create();
                    entry.SensesOS.Add(sense);
                    sense.Gloss.set_String(cache.DefaultAnalWs, Anal(cache, i == 0 ? "buy" : "pay"));
                    entries.Add(entry);
                }
            });
            var exporter = LexiconExporter(fixture);
            var first = exporter.Export(entries[0].Guid);
            var second = exporter.Export(entries[1].Guid);
            Assert.NotEqual(first.Path, second.Path);
            Assert.True(Plan(exporter, first.Text!).IsEmpty);
            Assert.True(Plan(exporter, second.Text!).IsEmpty);
            var text = Edited(exporter, second, d => ((RecordAlternatives)SenseAt(d, 0)["gloss"]!).Add("en", new RecordScalar("spend")));
            var plan = Plan(exporter, text);
            var change = Assert.Single(plan.ChangeSet.Changes);
            Assert.Equal(entries[1].SensesOS.Single().Guid, change.ObjectGuid);
            Assert.Equal(entries[1].Guid, plan.RecordGuid);
            // The first entry's file is still fresh: nothing was written.
            Assert.Equal(ProjectRecordBaseVerdict.Fresh, Plan(exporter, first.Text!).BaseVerdict);
        }

        [Fact]
        public void UnnamedListItemsArePlannedAndEdited()
        {
            using var fixture = new ProjectFixture();
            var cache = fixture.Cache;
            ICmPossibility unnamed = null!;
            fixture.Change(() =>
            {
                unnamed = cache.ServiceLocator.GetInstance<ICmPossibilityFactory>().Create();
                cache.LangProject.LexDbOA.PublicationTypesOA.PossibilitiesOS.Add(unnamed);
                var child = cache.ServiceLocator.GetInstance<ICmPossibilityFactory>().Create();
                unnamed.SubPossibilitiesOS.Add(child);
            });
            var exporter = Exporter(fixture, ProjectRecordSchema.ListItemOverlay);
            var export = exporter.Export(unnamed.Guid);
            Assert.EndsWith(unnamed.Guid.ToString("D"), export.Path);
            Assert.True(Plan(exporter, export.Text!).IsEmpty);
            var text = Edited(exporter, export, d => d.Data["name"] = new RecordAlternatives().Add("en", new RecordScalar("Now named")));
            var change = Assert.Single(Plan(exporter, text).ChangeSet.Changes);
            Assert.Equal(unnamed.Guid, change.ObjectGuid);
            Assert.Equal(RecordChangeKind.SetAlternative, change.Kind);
        }

        [Fact]
        public void APartialFileIsPlannedAgainstTheRecordThatHoldsItsObject()
        {
            using var fixture = new ProjectFixture();
            var w = Build(fixture);
            var exporter = LexiconExporter(fixture);
            var export = exporter.Export(w.Entry.Guid);
            var read = RecordFileReader.Read(export.Text!, exporter.Schema, exporter.CreateReadOptions()).Document!;
            var sense = SenseAt(read, 0);
            ((RecordAlternatives)sense["gloss"]!).Add("en", new RecordScalar("purchase"));
            var partial = new RecordDocument("lexicon/entry") { Root = export.Path + "/senses/1", Base = export.Base, Analysis = read.Analysis, Vernacular = read.Vernacular, Data = sense };
            foreach (var pair in read.WritingSystems) partial.WritingSystems.Add(pair);
            var plan = Plan(exporter, RecordFileWriter.Write(partial, exporter.Schema));
            Assert.Empty(plan.ChangeSet.Refusals);
            Assert.True(plan.ChangeSet.IsPartial);
            var change = Assert.Single(plan.ChangeSet.Changes);
            Assert.Equal(w.Sense.Guid, change.ObjectGuid);
            Assert.Equal(w.Entry.Guid, plan.RecordGuid);
            Assert.False(plan.ChangeSet.HasOmissions);
        }

        [Fact]
        public void WhenTheChangeAnAddedObjectIsPlacedAfterIsRefusedTheDependentChangeIsRefusedAndAnchorsStayRight()
        {
            using var fixture = new ProjectFixture();
            var w = Build(fixture);
            var exporter = LexiconExporter(fixture);
            var ghost = Guid.NewGuid();
            var text = Edited(exporter, exporter.Export(w.Entry.Guid), d =>
            {
                var senses = (RecordList)d.Data["senses"]!;
                senses.Add(new RecordObject { Class = "LexSense" }.Set("semantic-domains", new RecordList().Add(new RecordScalar("[g](/objects/" + ghost.ToString("D") + "){.name guid=" + ghost.ToString("D") + "}"))));
                senses.Add(new RecordObject { Class = "LexSense" }.Set("gloss", new RecordAlternatives().Add("en", new RecordScalar("lend"))));
            });
            var plan = Plan(exporter, text);
            Single(plan, RecordRefusalKind.UnknownReference);
            Single(plan, RecordRefusalKind.DependsOnRefusedChange);
            Assert.Empty(plan.ChangeSet.Changes);
        }
    }
}

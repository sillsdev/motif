using System;
using System.Collections.Generic;
using System.Linq;
using SIL.LCModel;
using SIL.LCModel.Core.Cellar;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.TextEditing.Markdown.Records;
using Xunit;

namespace SIL.TextEditing.Project.Tests
{
    public class ProjectRecordExportTests
    {
        static ISilDataAccess Data(LcmCache cache) => cache.DomainDataByFlid;

        static int AddCustom(LcmCache cache, string owner, string name, CellarPropertyType type)
            => ((IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor).AddCustomField(owner, name, type, 0);

        static ITsString Vern(LcmCache cache, string text) => TsStringUtils.MakeString(text, cache.DefaultVernWs);
        static ITsString Anal(LcmCache cache, string text) => TsStringUtils.MakeString(text, cache.DefaultAnalWs);

        static ILexEntry MakeEntry(ProjectFixture fixture, int customInteger, int customText, int customGenDate)
        {
            var cache = fixture.Cache;
            ILexEntry entry = null!;
            fixture.Change(() =>
            {
                entry = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create();
                var allomorph = cache.ServiceLocator.GetInstance<IMoStemAllomorphFactory>().Create();
                entry.LexemeFormOA = allomorph;
                allomorph.Form.set_String(cache.DefaultVernWs, Vern(cache, "palit"));
                allomorph.MorphTypeRA = (IMoMorphType)cache.LangProject.LexDbOA.MorphTypesOA.PossibilitiesOS.First();
                entry.CitationForm.set_String(cache.DefaultVernWs, Vern(cache, "palit"));
                entry.DoNotUseForParsing = true;
                var comment = TsStringUtils.MakeStrBldr();
                comment.Replace(0, 0, "plain ", TsStringUtils.MakeProps("", cache.DefaultAnalWs));
                comment.Replace(comment.Length, comment.Length, "vernacular", TsStringUtils.MakeProps("", cache.DefaultVernWs));
                entry.Comment.set_String(cache.DefaultAnalWs, comment.GetString());
                var sense = cache.ServiceLocator.GetInstance<ILexSenseFactory>().Create();
                entry.SensesOS.Add(sense);
                sense.Gloss.set_String(cache.DefaultAnalWs, Anal(cache, "buy"));
                sense.Definition.set_String(cache.DefaultAnalWs, Anal(cache, "to pay for [something]"));
                var example = cache.ServiceLocator.GetInstance<ILexExampleSentenceFactory>().Create();
                sense.ExamplesOS.Add(example);
                example.Example.set_String(cache.DefaultVernWs, Vern(cache, "Mipalit ko"));
                var translation = cache.ServiceLocator.GetInstance<ICmTranslationFactory>().Create(example,
                    cache.ServiceLocator.GetInstance<ICmPossibilityRepository>().GetObject(CmPossibilityTags.kguidTranFreeTranslation));
                translation.Translation.set_String(cache.DefaultAnalWs, Anal(cache, "I buy"));
                var domains = cache.LangProject.SemanticDomainListOA;
                var domain = cache.ServiceLocator.GetInstance<ICmSemanticDomainFactory>().Create();
                domains.PossibilitiesOS.Add(domain);
                domain.Name.set_String(cache.DefaultAnalWs, Anal(cache, "Buy"));
                sense.SemanticDomainsRC.Add(domain);
                var data = Data(cache);
                data.SetInt(entry.Hvo, customInteger, 42);
                data.SetUnicode(entry.Hvo, customText, "custom text", "custom text".Length);
                data.SetInt(entry.Hvo, customGenDate, new GenDate(GenDate.PrecisionType.Approximate, 3, 5, 2024, true).ToInt());
            });
            return entry;
        }

        static (ProjectRecordExporter Exporter, ProjectRecordSchemaResult Result) Exporter(ProjectFixture fixture, string overlay)
        {
            var result = ProjectRecordSchema.Generate(fixture.Cache, overlay);
            return (new ProjectRecordExporter(fixture.Cache, result), result);
        }

        static void ReadsBackClean(ProjectRecordExporter exporter, ProjectRecordExport export)
        {
            
            Assert.NotNull(export.Text);
            var read = RecordFileReader.Read(export.Text!, exporter.Schema, exporter.CreateReadOptions());
            var errors = read.Diagnostics.Where(d => d.Severity == SIL.TextEditing.Markdown.MarkdownSeverity.Error).ToList();
            Assert.True(errors.Count == 0, export.Path + ": " + string.Join("; ", errors.Select(e => e.Id + " " + e.Message)) + "\n" + export.Text);
            Assert.True(RecordDocument.AreEquivalent(export.Document, read.Document!, exporter.Schema), export.Path + " does not read back equal:\n" + export.Text);
        }

        [Fact]
        public void ALexiconEntryExportsEveryFieldKindAndReadsBackEqual()
        {
            using var fixture = new ProjectFixture();
            int integer = AddCustom(fixture.Cache, "LexEntry", "Count", CellarPropertyType.Integer);
            int text = AddCustom(fixture.Cache, "LexEntry", "Remark", CellarPropertyType.Unicode);
            int date = AddCustom(fixture.Cache, "LexEntry", "Seen", CellarPropertyType.GenDate);
            var entry = MakeEntry(fixture, integer, text, date);
            var (exporter, result) = Exporter(fixture, ProjectRecordSchema.LexiconEntryOverlay);
            var export = exporter.Export(entry.Guid);

            ReadsBackClean(exporter, export);
            Assert.Equal("lexicon/entry", export.Kind);
            Assert.Equal("/lexicon/palit", export.Path);
            var data = export.Document.Data;
            Assert.Equal(entry.Guid, data.Guid);
            Assert.Equal("true", ((RecordScalar)data["do-not-use-for-parsing"]!).Text);
            Assert.Equal("42", ((RecordScalar)data["custom-LexEntry-Count"]!).Text);
            Assert.Equal("custom text", ((RecordScalar)data["custom-LexEntry-Remark"]!).Text);
            Assert.Null(data["custom-LexEntry-Seen"]);
            var allomorph = (RecordObject)data["lexeme-form"]!;
            Assert.Equal("MoStemAllomorph", allomorph.Class);
            Assert.Equal("palit", ((RecordAlternatives)allomorph["form"]!)[fixture.Cache.ServiceLocator.WritingSystems.DefaultVernacularWritingSystem.Id]!.Text);
            Assert.Matches(@"^\[.*\]\(.*\)\{.*guid=", ((RecordScalar)allomorph["morph-type"]!).Text);
            var sense = (RecordObject)((RecordList)data["senses"]!).Items.Single();
            var domain = (RecordList)sense["semantic-domains"]!;
            Assert.Contains("guid=", ((RecordScalar)domain.Items.Single()).Text);
            var comment = (RecordAlternatives)data["comment"]!;
            Assert.Contains("{.v", comment["en"]!.Text);
        }

        [Fact]
        public void TimestampsAndCustomFieldsTheSchemaOmitsAreReportedWithTheirValues()
        {
            using var fixture = new ProjectFixture();
            int date = AddCustom(fixture.Cache, "LexEntry", "Seen", CellarPropertyType.GenDate);
            int integer = AddCustom(fixture.Cache, "LexEntry", "Count", CellarPropertyType.Integer);
            int text = AddCustom(fixture.Cache, "LexEntry", "Remark", CellarPropertyType.Unicode);
            var entry = MakeEntry(fixture, integer, text, date);
            var (exporter, result) = Exporter(fixture, ProjectRecordSchema.LexiconEntryOverlay);
            Assert.Contains(result.OmittedCustomFields, o => o.Field == "Seen");
            var export = exporter.Export(entry.Guid);
            var created = Assert.Single(export.Unexported, u => u.Object == entry.Guid && u.Field == "DateCreated");
            Assert.Matches(@"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d\.\d{7}Z$", created.Value);
            Assert.Contains("timestamp", created.Reason);
            var seen = Assert.Single(export.Unexported, u => u.Field == "Seen");
            Assert.Equal("AD 2024-3-5 Approximate", seen.Value);
            Assert.Contains("general date", seen.Reason);
            Assert.All(export.Unexported, u => Assert.NotEqual("Guid", u.Field));
            Assert.Equal(export.Unexported.Select(u => u.ToString()), exporter.Export(entry.Guid).Unexported.Select(u => u.ToString()));
        }

        [Fact]
        public void ANotebookRecordExportsItsBodyAndSubrecordsAndReadsBackEqual()
        {
            using var fixture = new ProjectFixture();
            int custom = AddCustom(fixture.Cache, "RnGenericRec", "Weather", CellarPropertyType.Unicode);
            var cache = fixture.Cache;
            IRnGenericRec record = null!;
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
                var second = cache.ServiceLocator.GetInstance<IStTxtParaFactory>().Create();
                record.DescriptionOA.ParagraphsOS.Add(second);
                second.Contents = Vern(cache, "kaon");
                var sub = cache.ServiceLocator.GetInstance<IRnGenericRecFactory>().Create();
                record.SubRecordsOS.Add(sub);
                sub.Title = Anal(cache, "Follow-up");
                var participant = cache.ServiceLocator.GetInstance<IRnRoledParticFactory>().Create();
                record.ParticipantsOC.Add(participant);
                Data(cache).SetUnicode(record.Hvo, custom, "dry", 3);
            });
            var (exporter, _) = Exporter(fixture, ProjectRecordSchema.NotebookRecordOverlay);
            var all = exporter.ExportAll("notebook/record");
            var export = Assert.Single(all);
            ReadsBackClean(exporter, export);
            Assert.StartsWith("/notebook/Kaon survey(" + record.Guid.ToString("N").Substring(0, 8) + ")", export.Path);
            Assert.True(export.Text!.Contains("\n---\n\nOn 3 May the speakers used kaon."), export.Text);
            Assert.DoesNotContain("description:", export.Text);
            Assert.Single(((RecordList)export.Document.Data["sub-records"]!).Items);
            Assert.Equal("dry", ((RecordScalar)export.Document.Data["custom-RnGenericRec-Weather"]!).Text);
        }

        [Fact]
        public void AListItemExportsUnderAGuidPathWithItsClassAndSubItems()
        {
            using var fixture = new ProjectFixture();
            var cache = fixture.Cache;
            int custom = AddCustom(fixture.Cache, "CmPossibility", "Rank", CellarPropertyType.Integer);
            ICmPossibility item = null!;
            ICmSemanticDomain domain = null!;
            fixture.Change(() =>
            {
                var list = cache.LangProject.LexDbOA.PublicationTypesOA;
                item = cache.ServiceLocator.GetInstance<ICmPossibilityFactory>().Create();
                list.PossibilitiesOS.Add(item);
                item.Name.set_String(cache.DefaultAnalWs, Anal(cache, "Dictionary"));
                item.Abbreviation.set_String(cache.DefaultAnalWs, Anal(cache, "Dict"));
                var sub = cache.ServiceLocator.GetInstance<ICmPossibilityFactory>().Create();
                item.SubPossibilitiesOS.Add(sub);
                sub.Name.set_String(cache.DefaultAnalWs, Anal(cache, "Concise"));
                item.DiscussionOA = cache.ServiceLocator.GetInstance<IStTextFactory>().Create();
                var para = cache.ServiceLocator.GetInstance<IStTxtParaFactory>().Create();
                item.DiscussionOA.ParagraphsOS.Add(para);
                para.Contents = Anal(cache, "Printed dictionary.");
                Data(cache).SetInt(item.Hvo, custom, 7);
                domain = cache.ServiceLocator.GetInstance<ICmSemanticDomainFactory>().Create();
                cache.LangProject.SemanticDomainListOA.PossibilitiesOS.Add(domain);
                domain.Name.set_String(cache.DefaultAnalWs, Anal(cache, "Universe"));
            });
            var (exporter, _) = Exporter(fixture, ProjectRecordSchema.ListItemOverlay);
            var export = exporter.Export(item.Guid);
            ReadsBackClean(exporter, export);
            Assert.Equal("lists/item", export.Kind);
            Assert.EndsWith("/" + item.Guid.ToString("D"), export.Path);
            Assert.Equal(3, SIL.TextEditing.Markdown.ProjectPath.Parse(export.Path)!.Segments.Count);
            Assert.Equal("CmPossibility", export.Document.Data.Class);
            Assert.Equal("7", ((RecordScalar)export.Document.Data["custom-CmPossibility-Rank"]!).Text);
            Assert.Single(((RecordList)export.Document.Data["sub-possibilities"]!).Items);
            Assert.Contains("Printed dictionary.", export.Text);
            var semantic = exporter.Export(domain.Guid);
            ReadsBackClean(exporter, semantic);
            Assert.Equal("CmSemanticDomain", semantic.Document.Data.Class);
        }

        [Fact]
        public void ExportIsDeterministicAndEveryRootOfEveryKindReadsBackClean()
        {
            using var fixture = new ProjectFixture();
            int integer = AddCustom(fixture.Cache, "LexEntry", "Count", CellarPropertyType.Integer);
            int text = AddCustom(fixture.Cache, "LexEntry", "Remark", CellarPropertyType.Unicode);
            int date = AddCustom(fixture.Cache, "LexEntry", "Seen", CellarPropertyType.GenDate);
            MakeEntry(fixture, integer, text, date);
            MakeEntry(fixture, integer, text, date);
            var (lexicon, _) = Exporter(fixture, ProjectRecordSchema.LexiconEntryOverlay);
            var first = lexicon.ExportAll("lexicon/entry");
            Assert.Equal(2, first.Count);
            Assert.Equal(first.Select(e => e.Text), lexicon.ExportAll("lexicon/entry").Select(e => e.Text));
            Assert.Equal(first.Select(e => e.Path).OrderBy(p => p, StringComparer.Ordinal), first.Select(e => e.Path));
            foreach (var export in first) ReadsBackClean(lexicon, export);
            var (lists, _) = Exporter(fixture, ProjectRecordSchema.ListItemOverlay);
            var items = lists.ExportAll("lists/item");
            Assert.NotEmpty(items);
            foreach (var export in items) ReadsBackClean(lists, export);
            Assert.Equal(items.Select(e => e.Text), lists.ExportAll("lists/item").Select(e => e.Text));
        }


        [Fact]
        public void CustomFieldsOfEveryKindExportUnderTheirKeysAndReadBackEqual()
        {
            using var fixture = new ProjectFixture();
            var cache = fixture.Cache;
            var metadata = (IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor;
            int possibility = metadata.GetClassId("CmPossibility");
            int yes = metadata.AddCustomField("LexEntry", "Flag", CellarPropertyType.Boolean, 0);
            int id = metadata.AddCustomField("LexEntry", "Token", CellarPropertyType.Guid, 0);
            int rich = metadata.AddCustomField("LexEntry", "Note", CellarPropertyType.String, 0);
            int names = metadata.AddCustomField("LexEntry", "Names", CellarPropertyType.MultiUnicode, 0);
            int notes = metadata.AddCustomField("LexEntry", "Notes", CellarPropertyType.MultiString, 0);
            int one = metadata.AddCustomField("LexEntry", "Kind", CellarPropertyType.ReferenceAtom, possibility);
            int many = metadata.AddCustomField("LexEntry", "Kinds", CellarPropertyType.ReferenceSequence, possibility);
            int set = metadata.AddCustomField("LexEntry", "Tags", CellarPropertyType.ReferenceCollection, possibility);
            var token = Guid.NewGuid();
            ILexEntry entry = null!;
            ICmPossibility first = null!, second = null!;
            fixture.Change(() =>
            {
                entry = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create();
                var list = cache.LangProject.LexDbOA.PublicationTypesOA;
                first = cache.ServiceLocator.GetInstance<ICmPossibilityFactory>().Create();
                list.PossibilitiesOS.Add(first);
                first.Name.set_String(cache.DefaultAnalWs, Anal(cache, "First"));
                second = cache.ServiceLocator.GetInstance<ICmPossibilityFactory>().Create();
                list.PossibilitiesOS.Add(second);
                second.Name.set_String(cache.DefaultAnalWs, Anal(cache, "Second"));
                var data = Data(cache);
                data.SetBoolean(entry.Hvo, yes, true);
                data.SetGuid(entry.Hvo, id, token);
                data.SetString(entry.Hvo, rich, Anal(cache, "a note"));
                data.SetMultiStringAlt(entry.Hvo, names, cache.DefaultAnalWs, Anal(cache, "name"));
                data.SetMultiStringAlt(entry.Hvo, names, cache.DefaultVernWs, Vern(cache, "ngalan"));
                data.SetMultiStringAlt(entry.Hvo, notes, cache.DefaultVernWs, Vern(cache, "nota"));
                data.SetObjProp(entry.Hvo, one, first.Hvo);
                data.Replace(entry.Hvo, many, 0, 0, new[] { second.Hvo, first.Hvo }, 2);
                data.Replace(entry.Hvo, set, 0, 0, new[] { second.Hvo, first.Hvo }, 2);
            });
            var (exporter, result) = Exporter(fixture, ProjectRecordSchema.LexiconEntryOverlay);
            Assert.Empty(result.OmittedCustomFields);
            var export = exporter.Export(entry.Guid);
            ReadsBackClean(exporter, export);
            var d = export.Document.Data;
            Assert.Equal("true", ((RecordScalar)d["custom-LexEntry-Flag"]!).Text);
            Assert.Equal(token.ToString("D"), ((RecordScalar)d["custom-LexEntry-Token"]!).Text);
            Assert.Equal("a note", ((RecordScalar)d["custom-LexEntry-Note"]!).Text);
            Assert.Equal(new[] { "ar", "en" }, ((RecordAlternatives)d["custom-LexEntry-Names"]!).Items.Select(i => i.Key));
            Assert.Equal("nota", ((RecordAlternatives)d["custom-LexEntry-Notes"]!)["ar"]!.Text);
            Assert.Contains("guid=" + first.Guid.ToString("D"), ((RecordScalar)d["custom-LexEntry-Kind"]!).Text);
            var sequence = ((RecordList)d["custom-LexEntry-Kinds"]!).Items.Select(i => ((RecordScalar)i).Text).ToList();
            Assert.Contains(second.Guid.ToString("D"), sequence[0]);
            Assert.Contains(first.Guid.ToString("D"), sequence[1]);
            var collection = ((RecordList)d["custom-LexEntry-Tags"]!).Items.Select(i => ((RecordScalar)i).Text).ToList();
            var ordered = new[] { first.Guid, second.Guid }.OrderBy(g => g).ToList();
            Assert.Contains(ordered[0].ToString("D"), collection[0]);
            Assert.Contains(ordered[1].ToString("D"), collection[1]);
        }

        [Fact]
        public void MarkupQuotesAndControlCharactersSurviveTheRoundTrip()
        {
            using var fixture = new ProjectFixture();
            var cache = fixture.Cache;
            fixture.Style("Strong", StyleType.kstCharacter, null, b => b.SetIntPropValues(2, 3, 1));
            ILexEntry entry = null!;
            fixture.Change(() =>
            {
                entry = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create();
                var sense = cache.ServiceLocator.GetInstance<ILexSenseFactory>().Create();
                entry.SensesOS.Add(sense);
                sense.Gloss.set_String(cache.DefaultAnalWs, Anal(cache, "say \"hi\" \\ tab\there\nline two [x](y) *z* # w"));
                var definition = TsStringUtils.MakeStrBldr();
                definition.Replace(0, 0, "plain ", TsStringUtils.MakeProps("", cache.DefaultAnalWs));
                var strong = TsStringUtils.MakePropsBldr();
                strong.SetIntPropValues((int)FwTextPropType.ktptWs, 0, cache.DefaultAnalWs);
                strong.SetStrPropValue((int)FwTextPropType.ktptNamedStyle, "Strong");
                definition.Replace(definition.Length, definition.Length, "bold *star*", strong.GetTextProps());
                definition.Replace(definition.Length, definition.Length, " \u0645\u0631\u062d\u0628\u0627 ", TsStringUtils.MakeProps("", cache.DefaultVernWs));
                sense.Definition.set_String(cache.DefaultAnalWs, definition.GetString());
            });
            var (exporter, _) = Exporter(fixture, ProjectRecordSchema.LexiconEntryOverlay);
            var export = exporter.Export(entry.Guid);
            ReadsBackClean(exporter, export);
            var sense1 = (RecordObject)((RecordList)export.Document.Data["senses"]!).Items.Single();
            Assert.Equal("say \"hi\" \\ tab\there\nline two [x](y) *z* # w", ((RecordAlternatives)sense1["gloss"]!)["en"]!.Text);
            Assert.Contains("Strong", ((RecordAlternatives)sense1["definition"]!)["en"]!.Text);
        }

        [Fact]
        public void ACharacterStyleTheProjectLacksIsReportedAndLeftOutSoTheFileReadsBack()
        {
            using var fixture = new ProjectFixture();
            var cache = fixture.Cache;
            ILexEntry first = null!;
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
                    if (i == 0) first = entry;
                }
            });
            var (exporter, _) = Exporter(fixture, ProjectRecordSchema.LexiconEntryOverlay);
            var export = exporter.Export(first.Guid);
            ReadsBackClean(exporter, export);
            var style = Assert.Single(export.Unexported, u => u.Value == "Homograph-Number");
            Assert.Equal("Homograph-Number", style.Value);
            Assert.Contains("character style", style.Reason);
        }



        static string Notation(string className, string name, string notation)
        {
            var document = System.Xml.Linq.XDocument.Parse(ProjectRecordSchema.LexiconEntryOverlay);
            document.Root!.Elements("class").Single(e => (string?)e.Attribute("name") == className)
                .Elements("field").Single(e => (string?)e.Attribute("name") == name).SetAttributeValue("notation", notation);
            return document.ToString();
        }

        static ITsString Bold(LcmCache cache, string text, int ws, bool withWs)
        {
            var props = TsStringUtils.MakePropsBldr();
            if (withWs) props.SetIntPropValues((int)FwTextPropType.ktptWs, 0, ws);
            props.SetIntPropValues((int)FwTextPropType.ktptBold, (int)FwTextPropVar.ktpvEnum, (int)FwTextToggleVal.kttvForceOn);
            var builder = TsStringUtils.MakeStrBldr();
            builder.Replace(0, 0, text, props.GetTextProps());
            return builder.GetString();
        }

        [Fact]
        public void FormattingInAPlainAlternativeIsReportedWithItsWritingSystem()
        {
            using var fixture = new ProjectFixture();
            var cache = fixture.Cache;
            ILexEntry entry = null!;
            fixture.Change(() =>
            {
                entry = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create();
                entry.Comment.set_String(cache.DefaultAnalWs, Bold(cache, "loud", cache.DefaultAnalWs, true));
                entry.Comment.set_String(cache.DefaultVernWs, Vern(cache, "quiet"));
            });
            var exporter = new ProjectRecordExporter(cache, ProjectRecordSchema.Generate(cache, Notation("LexEntry", "Comment", "test")));
            var export = exporter.Export(entry.Guid);
            var lost = Assert.Single(export.Unexported, u => u.Field == "Comment");
            Assert.Contains("alternative en", lost.Reason);
            Assert.Equal("loud", ((RecordAlternatives)export.Document.Data["comment"]!)["en"]!.Text);
        }

        [Fact]
        public void ASingleDirectPropertyWithNoWritingSystemCountsAsFormatting()
        {
            using var fixture = new ProjectFixture();
            var cache = fixture.Cache;
            int field = cache.MetaDataCacheAccessor.GetFieldId("LexEntry", "ImportResidue", false);
            ILexEntry bolded = null!, plain = null!;
            fixture.Change(() =>
            {
                bolded = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create();
                plain = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create();
                Data(cache).SetString(bolded.Hvo, field, Bold(cache, "heavy", cache.DefaultAnalWs, false));
                Data(cache).SetString(plain.Hvo, field, Anal(cache, "light"));
            });
            var exporter = new ProjectRecordExporter(cache, ProjectRecordSchema.Generate(cache, Notation("LexEntry", "ImportResidue", "test")));
            Assert.Single(exporter.Export(bolded.Guid).Unexported, u => u.Field == "ImportResidue");
            Assert.DoesNotContain(exporter.Export(plain.Guid).Unexported, u => u.Field == "ImportResidue");
        }

        [Fact]
        public void DirectParagraphAndRunFormattingEitherRoundTripsOrIsReported()
        {
            using var fixture = new ProjectFixture();
            var cache = fixture.Cache;
            IRnGenericRec record = null!;
            fixture.Change(() =>
            {
                var notebook = cache.LangProject.ResearchNotebookOA ?? (cache.LangProject.ResearchNotebookOA = cache.ServiceLocator.GetInstance<IRnResearchNbkFactory>().Create());
                record = cache.ServiceLocator.GetInstance<IRnGenericRecFactory>().Create();
                notebook.RecordsOC.Add(record);
                record.ConclusionsOA = cache.ServiceLocator.GetInstance<IStTextFactory>().Create();
                var para = cache.ServiceLocator.GetInstance<IStTxtParaFactory>().Create();
                record.ConclusionsOA.ParagraphsOS.Add(para);
                var rules = TsStringUtils.MakePropsBldr();
                rules.SetIntPropValues((int)FwTextPropType.ktptAlign, (int)FwTextPropVar.ktpvEnum, (int)FwTextAlign.ktalCenter);
                rules.SetIntPropValues((int)FwTextPropType.ktptFirstIndent, (int)FwTextPropVar.ktpvMilliPoint, 36000);
                para.StyleRules = rules.GetTextProps();
                var run = TsStringUtils.MakePropsBldr();
                run.SetIntPropValues((int)FwTextPropType.ktptWs, 0, cache.DefaultAnalWs);
                run.SetStrPropValue((int)FwTextPropType.ktptFontFamily, "Arial");
                var text = TsStringUtils.MakeStrBldr();
                text.Replace(0, 0, "centred in Arial", run.GetTextProps());
                para.Contents = text.GetString();
            });
            var (exporter, _) = Exporter(fixture, ProjectRecordSchema.NotebookRecordOverlay);
            var export = exporter.Export(record.Guid);
            var read = RecordFileReader.Read(export.Text!, exporter.Schema, exporter.CreateReadOptions());
            var paragraph = ((RecordScalar)read.Document!.Data["conclusions"]!).Paragraphs!.Single();
            bool keptAlign = paragraph.Props.IntProps.ContainsKey((int)FwTextPropType.ktptAlign) && paragraph.Props.IntProps.ContainsKey((int)FwTextPropType.ktptFirstIndent);
            bool keptFont = paragraph.Runs.Single().Props.StrProps.ContainsKey((int)FwTextPropType.ktptFontFamily);
            bool reported = export.Unexported.Any(u => u.Field == "Conclusions" && u.Reason.Contains("Direct formatting"));
            Assert.True((keptAlign && keptFont) || reported, "direct formatting was lost without a report:\n" + export.Text);
            Assert.False((keptAlign && keptFont) && reported, "formatting was kept but reported as lost");
        }

        [Fact]
        public void BinaryCustomFieldsAreReportedAsBase64AndALongOneWithoutItsBytes()
        {
            using var fixture = new ProjectFixture();
            var cache = fixture.Cache;
            var metadata = (IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor;
            int small = metadata.AddCustomField("LexEntry", "Small", CellarPropertyType.Binary, 0);
            int large = metadata.AddCustomField("LexEntry", "Large", CellarPropertyType.Binary, 0);
            ILexEntry entry = null!;
            fixture.Change(() =>
            {
                entry = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create();
                Data(cache).SetBinary(entry.Hvo, small, new byte[] { 1, 2, 3 }, 3);
                Data(cache).SetBinary(entry.Hvo, large, new byte[2048], 2048);
            });
            var (exporter, result) = Exporter(fixture, ProjectRecordSchema.LexiconEntryOverlay);
            Assert.Equal(2, result.OmittedCustomFields.Count);
            var export = exporter.Export(entry.Guid);
            ReadsBackClean(exporter, export);
            Assert.Equal("AQID", Assert.Single(export.Unexported, u => u.Field == "Small").Value);
            var big = Assert.Single(export.Unexported, u => u.Field == "Large");
            Assert.Null(big.Value);
            Assert.Contains("Binary", big.Reason);
        }

        [Fact]
        public void ExportChangesNothingInTheProject()
        {
            using var fixture = new ProjectFixture();
            int integer = AddCustom(fixture.Cache, "LexEntry", "Count", CellarPropertyType.Integer);
            int text = AddCustom(fixture.Cache, "LexEntry", "Remark", CellarPropertyType.Unicode);
            int date = AddCustom(fixture.Cache, "LexEntry", "Seen", CellarPropertyType.GenDate);
            var entry = MakeEntry(fixture, integer, text, date);
            var repository = fixture.Cache.ServiceLocator.GetInstance<ICmObjectRepository>();
            int objects = repository.AllInstances().Count();
            var modified = entry.DateModified;
            var (exporter, _) = Exporter(fixture, ProjectRecordSchema.LexiconEntryOverlay);
            exporter.ExportAll("lexicon/entry");
            Assert.Equal(objects, repository.AllInstances().Count());
            Assert.Equal(modified, entry.DateModified);
        }

        [Fact]
        public void ExportRefusesWhatTheSchemaDoesNotName()
        {
            using var fixture = new ProjectFixture();
            var (exporter, _) = Exporter(fixture, ProjectRecordSchema.LexiconEntryOverlay);
            Assert.Throws<ArgumentException>(() => exporter.Export(Guid.NewGuid()));
            Assert.Throws<ArgumentException>(() => exporter.Export("lists/item", Guid.NewGuid()));
            Assert.Throws<ArgumentException>(() => exporter.ExportAll("lists/item"));
            Assert.Throws<ArgumentException>(() => exporter.Export(fixture.Cache.LangProject.Guid));
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SIL.LCModel;
using SIL.LCModel.Core.Cellar;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.TextEditing.Markdown.Records;
using Xunit;

namespace SIL.TextEditing.Project.Tests
{
    /// <summary>Checks an exported file, read back, against the project itself: every schema field of every owned object is read from the cache again,
    /// by code that shares nothing with the exporter, and compared with what the file says. A stored field that is neither in the file nor reported fails too.</summary>
    static class ExportOracle
    {
        const string Plumbing = "|Self|Owner|OwningFlid|OwnOrd|Guid|ClassID|ShortNameTSS|";

        public static void Check(LcmCache cache, ProjectRecordSchemaResult result, ProjectRecordExport export, RecordDocument read)
        {
            var root = cache.ServiceLocator.ObjectRepository.GetObject(export.Guid);
            var kind = result.Schema.Kind(export.Kind)!;
            var type = kind.Roots.First(t => t.Name == root.ClassName);
            Object(cache, result, export, root, type, read.Data, export.Path);
        }

        static string N(string? s) => (s ?? string.Empty).Normalize(NormalizationForm.FormD);

        static void Object(LcmCache cache, ProjectRecordSchemaResult result, ProjectRecordExport export, ICmObject obj, RecordObjectType type, RecordObject data, string where)
        {
            var sda = cache.DomainDataByFlid;
            var md = (IFwMetaDataCache)cache.MetaDataCacheAccessor;
            var managed = (IFwMetaDataCacheManaged)md;
            Assert.Equal(obj.Guid, data.Guid);
            var keys = result.Bindings.Keys[type.Name];
            foreach (var field in type.Fields)
            {
                int flid = keys[field.Name];
                string at = where + " " + obj.ClassName + "." + field.Name;
                var actual = data[field.Name];
                bool actualEmpty = actual == null || actual.IsEmpty;
                if (field.MultiWritingSystem)
                {
                    var expected = new Dictionary<string, string>();
                    var multi = sda.get_MultiStringProp(obj.Hvo, flid);
                    for (int i = 0; i < multi.StringCount; i++)
                    {
                        var s = multi.GetStringFromIndex(i, out int ws);
                        if (s != null && s.Length > 0) expected[cache.WritingSystemFactory.GetStrFromWs(ws)] = N(s.Text);
                    }
                    var got = actualEmpty ? new Dictionary<string, string>()
                        : ((RecordAlternatives)actual!).Items.Where(i => !i.Value.IsEmpty).ToDictionary(i => i.Key, i => N(Plain(i.Value)));
                    Assert.True(expected.Count == got.Count && expected.All(e => got.TryGetValue(e.Key, out var g) && g == e.Value), at + " alternatives differ");
                    continue;
                }
                var type_ = (CellarPropertyType)md.GetFieldType(flid);
                switch (field.Kind)
                {
                    case RecordFieldKind.Text:
                    case RecordFieldKind.Notation:
                    {
                        string expected = type_ == CellarPropertyType.String ? sda.get_StringProp(obj.Hvo, flid).Text ?? "" : sda.get_UnicodeProp(obj.Hvo, flid) ?? "";
                        Assert.True(N(expected) == (actualEmpty ? "" : N(((RecordScalar)actual!).Text)), at + " text differs");
                        break;
                    }
                    case RecordFieldKind.Rich:
                    {
                        string expected;
                        if (field.Paragraphs)
                        {
                            int target = sda.get_ObjectProp(obj.Hvo, flid);
                            expected = target == 0 ? "" : string.Join("\n", ((IStText)cache.ServiceLocator.GetObject(target)).ParagraphsOS.OfType<IStTxtPara>()
                                .Select(p => N(p.Contents.Text)).Where(t => t.Length > 0));
                        }
                        else expected = N(sda.get_StringProp(obj.Hvo, flid).Text);
                        string got = actualEmpty ? "" : field.Paragraphs
                            ? string.Join("\n", ((RecordScalar)actual!).Paragraphs!.Select(p => N(string.Concat(p.Runs.Select(r => r.Text)))).Where(t => t.Length > 0))
                            : N(Plain((RecordScalar)actual!));
                        Assert.True(expected == got, at + " rich text differs: [" + expected + "] vs [" + got + "]");
                        break;
                    }
                    case RecordFieldKind.Integer:
                        Assert.True(sda.get_IntProp(obj.Hvo, flid) == ((RecordScalar)actual!).AsInteger(), at + " integer differs");
                        break;
                    case RecordFieldKind.Boolean:
                        Assert.True(sda.get_BooleanProp(obj.Hvo, flid) == ((RecordScalar)actual!).AsBoolean(), at + " boolean differs");
                        break;
                    case RecordFieldKind.Guid:
                        Assert.True(sda.get_GuidProp(obj.Hvo, flid) == ((RecordScalar)actual!).AsGuid(), at + " guid differs");
                        break;
                    case RecordFieldKind.Reference:
                    {
                        int target = sda.get_ObjectProp(obj.Hvo, flid);
                        Assert.True(target == 0 ? actualEmpty : cache.ServiceLocator.GetObject(target).Guid == ((RecordScalar)actual!).Target, at + " reference differs");
                        break;
                    }
                    case RecordFieldKind.Object:
                    {
                        int target = sda.get_ObjectProp(obj.Hvo, flid);
                        if (target == 0) Assert.True(actualEmpty, at + " has an object the project lacks");
                        else Child(cache, result, export, cache.ServiceLocator.GetObject(target), field, (RecordObject)actual!, at);
                        break;
                    }
                    default:
                    {
                        int count = sda.get_VecSize(obj.Hvo, flid);
                        var items = Enumerable.Range(0, count).Select(i => cache.ServiceLocator.GetObject(sda.get_VecItem(obj.Hvo, flid, i))).ToList();
                        var got = actualEmpty ? new List<RecordValue>() : ((RecordList)actual!).Items;
                        Assert.True(items.Count == got.Count, at + " has " + items.Count + " items in the project and " + got.Count + " in the file");
                        if (field.Kind == RecordFieldKind.Collection) items = items.OrderBy(i => i.Guid).ToList();
                        for (int i = 0; i < items.Count; i++)
                        {
                            if (field.Item!.Kind == RecordFieldKind.Reference) Assert.True(items[i].Guid == ((RecordScalar)got[i]).Target, at + "/" + (i + 1) + " reference differs");
                            else Child(cache, result, export, items[i], field.Item, (RecordObject)got[i], at + "/" + (i + 1));
                        }
                        break;
                    }
                }
            }
            // Every stored field with data is in the file or reported.
            foreach (int flid in managed.GetFields(md.GetClassId(obj.ClassName), true, -1))
            {
                string name = md.GetFieldName(flid);
                if (Plumbing.Contains("|" + name + "|") || md.get_IsVirtual(flid) || keys.Values.Contains(flid)) continue;
                if (!HasData(cache, obj, flid, (CellarPropertyType)md.GetFieldType(flid))) continue;
                Assert.True(export.Unexported.Any(u => u.Object == obj.Guid && u.Field == name), where + " " + obj.ClassName + "." + name + " has data and is neither exported nor reported");
            }
        }

        static void Child(LcmCache cache, ProjectRecordSchemaResult result, ProjectRecordExport export, ICmObject child, RecordField field, RecordObject data, string at)
        {
            var type = field.ObjectTypes.Single(t => t.Name == child.ClassName);
            Object(cache, result, export, child, type, data, at);
        }

        static string Plain(RecordScalar scalar) => scalar.Paragraphs == null ? scalar.Text : string.Concat(scalar.Paragraphs.SelectMany(p => p.Runs.Select(r => r.Text)));

        static bool HasData(LcmCache cache, ICmObject obj, int flid, CellarPropertyType type)
        {
            var sda = cache.DomainDataByFlid;
            switch (type)
            {
                case CellarPropertyType.Boolean: return sda.get_BooleanProp(obj.Hvo, flid);
                case CellarPropertyType.Integer: case CellarPropertyType.Numeric: return sda.get_IntProp(obj.Hvo, flid) != 0;
                case CellarPropertyType.Time: return sda.get_TimeProp(obj.Hvo, flid) != 0;
                case CellarPropertyType.Guid: return sda.get_GuidProp(obj.Hvo, flid) != Guid.Empty;
                case CellarPropertyType.Unicode: return !string.IsNullOrEmpty(sda.get_UnicodeProp(obj.Hvo, flid));
                case CellarPropertyType.String: return sda.get_StringProp(obj.Hvo, flid).Length > 0;
                case CellarPropertyType.MultiUnicode: case CellarPropertyType.MultiString:
                {
                    var multi = sda.get_MultiStringProp(obj.Hvo, flid);
                    for (int i = 0; i < multi.StringCount; i++) if (multi.GetStringFromIndex(i, out _).Length > 0) return true;
                    return false;
                }
                case CellarPropertyType.ReferenceAtom: case CellarPropertyType.OwningAtomic: return sda.get_ObjectProp(obj.Hvo, flid) != 0;
                case CellarPropertyType.ReferenceCollection: case CellarPropertyType.ReferenceSequence:
                case CellarPropertyType.OwningCollection: case CellarPropertyType.OwningSequence: return sda.get_VecSize(obj.Hvo, flid) > 0;
                default: return false; // binary, image and general date are checked by the unexported tests
            }
        }
    }
}

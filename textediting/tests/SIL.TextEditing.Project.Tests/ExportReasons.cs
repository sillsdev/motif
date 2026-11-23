using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace SIL.TextEditing.Project.Tests
{
    /// <summary>Every reason the exporter gives for leaving a value out of a record file, as the text a test asserts on.</summary>
    internal static class ExportReasons
    {
        public const string RecordNotWritten = "The record file could not be written";
        public const string ValueNotConverted = "The value could not be converted";
        public const string PlainFieldFormatting = "plain text; the string's formatting is not exported";
        public const string DirectFormatting = "Direct formatting that the file's Markdown does not spell";
        public const string NonTextParagraph = "Only structured text paragraphs are exported.";
        public const string UndefinedCharacterStyle = "does not define this character style";
        public const string UndefinedParagraphStyle = "does not define this paragraph style";
        public const string StTextProperty = "A structured text's own property is not part of the record file.";
        public const string ParagraphAnalysis = "A paragraph's analysis data is not part of the record file.";
        public const string Timestamp = "A timestamp has no record value kind.";
        public const string GeneralDate = "A general date has no record value kind.";
        public const string BinaryData = "Binary data has no record value kind.";
        public const string OwnedObjects = "Owned objects of this field are not part of the record schema.";
        public const string FieldLeftOut = "The schema leaves this field out.";

        /// <summary>Every reason, by constant name.</summary>
        public static IReadOnlyDictionary<string, string> All()
            => typeof(ExportReasons).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string))
                .ToDictionary(f => f.Name, f => (string)f.GetRawConstantValue()!, StringComparer.Ordinal);

        /// <summary>Reasons no test has produced yet, with why. A reason leaves this list when a test asserts it.</summary>
        public static readonly IReadOnlyDictionary<string, string> NotProduced = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [nameof(RecordNotWritten)] = "RecordFileWriter refuses a document only for a value no project fixture has made; the importer's ExportFailed refusal is tested by overriding the export.",
            [nameof(ValueNotConverted)] = "Needs a rich value whose conversion throws; no fixture string has been found that does.",
            [nameof(DirectFormatting)] = "DirectParagraphAndRunFormattingEitherRoundTripsOrIsReported sees it only when the formatting is not kept; no property is known that the Markdown never spells.",
            [nameof(NonTextParagraph)] = "A structured text holds only IStTxtPara in the shipped model, so the branch looks unreachable.",
            [nameof(FieldLeftOut)] = "Every field type the exporter can name has its own reason; no field reaches the default.",
        };
    }

    /// <summary>Marks a test that makes the exporter give <see cref="Reason"/> (a constant name of <see cref="ExportReasons"/>) and asserts it.</summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    internal sealed class ExportReasonAttribute : Attribute
    {
        public ExportReasonAttribute(string reason) { Reason = reason; }
        public string Reason { get; }
    }

    public class ExportReasonGuardTests
    {
        static string ExporterSource()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                string path = Path.Combine(dir.FullName, "src", "SIL.TextEditing.Project", "Records", "ProjectRecordExporter.cs");
                if (File.Exists(path)) return File.ReadAllText(path);
            }
            throw new FileNotFoundException("ProjectRecordExporter.cs was not found above " + AppContext.BaseDirectory);
        }

        static HashSet<string> Asserted()
            => typeof(ExportReasonGuardTests).Assembly.GetTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                .SelectMany(m => m.GetCustomAttributes<ExportReasonAttribute>())
                .Select(a => a.Reason).ToHashSet(StringComparer.Ordinal);

        [Fact]
        public void EveryReasonIsAssertedByATestOrListedAsNotProducedWithAWhy()
        {
            var all = ExportReasons.All();
            var asserted = Asserted();
            Assert.All(asserted, name => Assert.True(all.ContainsKey(name), name + " is not a reason in ExportReasons"));
            var missing = all.Keys.Where(n => !asserted.Contains(n) && !ExportReasons.NotProduced.ContainsKey(n)).ToList();
            Assert.True(missing.Count == 0, "no test asserts, and nothing explains: " + string.Join(", ", missing));
            var both = ExportReasons.NotProduced.Keys.Where(asserted.Contains).ToList();
            Assert.True(both.Count == 0, "a test asserts these, so remove them from NotProduced: " + string.Join(", ", both));
            Assert.All(ExportReasons.NotProduced, p => { Assert.True(all.ContainsKey(p.Key)); Assert.False(string.IsNullOrWhiteSpace(p.Value)); });
        }

        [Fact]
        public void TheExporterGivesNoReasonThatExportReasonsLacks()
        {
            string source = ExporterSource();
            foreach (var pair in ExportReasons.All())
                Assert.True(source.Contains(pair.Value), pair.Key + " is no longer in the exporter's text");
            // Each place that reports a value names its reason once; a new place needs a new entry in ExportReasons.
            int sites = 0;
            for (int at = source.IndexOf("new UnexportedValue(", StringComparison.Ordinal); at >= 0; at = source.IndexOf("new UnexportedValue(", at + 1, StringComparison.Ordinal)) sites++;
            Assert.Equal(8, sites);
            Assert.Equal(14, ExportReasons.All().Count);
        }
    }
}

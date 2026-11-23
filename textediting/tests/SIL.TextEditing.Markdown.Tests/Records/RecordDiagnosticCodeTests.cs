using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using SIL.TextEditing.Markdown.Records;
using Xunit;

namespace SIL.TextEditing.Markdown.Tests.Records
{
    /// <summary>Each finding id of the record-file reader is asserted by its constant: the reader is run on a small input that has the finding and the
    /// finding must carry the constant, and a guard fails when an id has no such test.</summary>
    public class RecordDiagnosticCodeTests
    {
        static IReadOnlyDictionary<string, string> Ids()
            => typeof(RecordDiagnosticIds).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string))
                .ToDictionary(f => f.Name, f => (string)f.GetRawConstantValue()!, StringComparer.Ordinal);

        /// <summary>The ids that other tests assert by constant, each named by a constant in a test of its own.</summary>
        static readonly string[] AssertedElsewhere =
        {
            nameof(RecordDiagnosticIds.TabInPlainBlock), nameof(RecordDiagnosticIds.TooDeep),
            nameof(RecordDiagnosticIds.ScalarStyle), nameof(RecordDiagnosticIds.Notation),
            nameof(RecordDiagnosticIds.UnknownField), nameof(RecordDiagnosticIds.ClassNotAllowed), nameof(RecordDiagnosticIds.BadRoot),
            nameof(RecordDiagnosticIds.OutsideRoot), nameof(RecordDiagnosticIds.BlockNotAllowed),
        };

        /// <summary>Each id with a conformance input that has it; the reader is run on that input here, and the id is asserted by its constant.</summary>
        static readonly (string Constant, string CaseId)[] Table =
        {
            (nameof(RecordDiagnosticIds.YamlSyntax), "records/02-traps.json::an unclosed [ as a front-matter value is a syntax error, not an exception"),
            (nameof(RecordDiagnosticIds.CommentDropped), "records/02-traps.json::comments are dropped with information whose fix removes them"),
            (nameof(RecordDiagnosticIds.Tab), "records/02-traps.json::a tab after the indentation's spaces is an error too"),
            (nameof(RecordDiagnosticIds.LineSeparator), "records/02-traps.json::a raw line separator in a rich block is an error whose fix is its character reference"),
            (nameof(RecordDiagnosticIds.DuplicateKey), "records/02-traps.json::a duplicate key is an error; the first is read"),
            (nameof(RecordDiagnosticIds.Anchor), "records/02-traps.json::an anchor is an error whose fix removes it"),
            (nameof(RecordDiagnosticIds.Alias), "records/02-traps.json::a bound root written plain is a YAML alias: an error whose fix quotes it"),
            (nameof(RecordDiagnosticIds.Tag), "records/02-traps.json::a tag is an error whose fix removes it"),
            (nameof(RecordDiagnosticIds.MergeKey), "records/02-traps.json::a merge key is an error whose fix removes it, since it is not read"),
            (nameof(RecordDiagnosticIds.Folded), "records/06-review-nits.json::a raw tab in a folded plain block keeps the folded finding"),
            (nameof(RecordDiagnosticIds.SecondDocument), "records/02-traps.json::a --- line with text after it starts a second YAML document: an error"),
            (nameof(RecordDiagnosticIds.Unquoted), "records/02-traps.json::a rich value written plain is read; the canonical spelling is a block"),
            (nameof(RecordDiagnosticIds.Flow), "records/02-traps.json::columns count UTF-16 code units: a finding after a character outside the BMP"),
            (nameof(RecordDiagnosticIds.Empty), "records/02-traps.json::an empty key is an empty value, left out"),
            (nameof(RecordDiagnosticIds.KeyOrder), "records/02-traps.json::keys out of the schema's order are read, with information"),
            (nameof(RecordDiagnosticIds.NotARecord), "records/03-schema.json::text with no front matter is not a record file"),
            (nameof(RecordDiagnosticIds.UnknownKey), "records/03-schema.json::an unknown front-matter key is an error in a record file"),
            (nameof(RecordDiagnosticIds.UnknownKind), "records/03-schema.json::a record kind the schema does not have"),
            (nameof(RecordDiagnosticIds.SchemaMismatch), "records/03-schema.json::a schema other than the reader's"),
            (nameof(RecordDiagnosticIds.WrongShape), "records/03-schema.json::a value of the wrong shape: a list where alternatives are expected, a string where an object is"),
            (nameof(RecordDiagnosticIds.NotAnInteger), "records/03-schema.json::not a whole number in an integer field"),
            (nameof(RecordDiagnosticIds.NotABoolean), "records/02-traps.json::yes in a Boolean field is an error whose fix is true"),
            (nameof(RecordDiagnosticIds.BadGuid), "records/03-schema.json::not a GUID in a GUID field"),
            (nameof(RecordDiagnosticIds.DuplicateGuid), "records/03-schema.json::one GUID on two objects"),
            (nameof(RecordDiagnosticIds.NotAReference), "records/03-schema.json::a reference that is not a link"),
            (nameof(RecordDiagnosticIds.PlainFieldMarkup), "records/03-schema.json::formatting in a plain text field written as a block is an error"),
            (nameof(RecordDiagnosticIds.Unwritable), "records/04-unwritable.json::a raw NUL in the body cannot be written"),
            (nameof(RecordDiagnosticIds.UnexpectedBody), "records/03-schema.json::text below the front matter of a record with no body field"),
            (nameof(RecordDiagnosticIds.BodyInFrame), "records/03-schema.json::the body field in record-data is an error"),
        };

        public static IEnumerable<object[]> Cases() => Table.Select(t => new object[] { t.Constant, t.CaseId });

        [Theory]
        [MemberData(nameof(Cases))]
        public void TheReaderReportsTheIdOnAnInputThatHasIt(string constant, string caseId)
        {
            string id = Ids()[constant];
            var c = RecordFixtures.Find(caseId);
            Assert.True(c.IsRead, caseId + " is not a read case");
            var result = RecordFileReader.Read(c.Text!, c.Schema, RecordFixtures.Options(c));
            Assert.Contains(result.Diagnostics, d => d.Id == id);
        }

        [Fact]
        public void EveryIdIsListedAndAssertedByAConstantSomewhere()
        {
            var ids = Ids();
            Assert.Equal(ids.Values.OrderBy(v => v, StringComparer.Ordinal), RecordDiagnosticIds.All.OrderBy(v => v, StringComparer.Ordinal));
            var here = Table.Select(t => t.Constant).ToHashSet(StringComparer.Ordinal);
            var uncovered = ids.Keys.Where(n => !here.Contains(n) && !AssertedElsewhere.Contains(n)).ToList();
            Assert.True(uncovered.Count == 0, "no test asserts: " + string.Join(", ", uncovered));
            // An id listed as asserted elsewhere must still be named by a constant in another test file.
            string root = TestsRoot();
            string others = string.Concat(Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(p => !p.EndsWith("RecordDiagnosticCodeTests.cs", StringComparison.Ordinal)).Select(File.ReadAllText));
            foreach (string name in AssertedElsewhere)
                Assert.True(Regex.IsMatch(others, @"RecordDiagnosticIds\." + name + @"\b"), name + " is not named by a constant in another test");
            Assert.Empty(AssertedElsewhere.Intersect(here));
        }

        static string TestsRoot()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                string path = Path.Combine(dir.FullName, "tests", "SIL.TextEditing.Markdown.Tests", "Records");
                if (Directory.Exists(path)) return path;
            }
            throw new DirectoryNotFoundException("The Records tests folder was not found above " + AppContext.BaseDirectory);
        }
    }
}

using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SIL.TextEditing.Markdown.Records;
using Xunit;

namespace SIL.TextEditing.Markdown.Tests.Records
{
    /// <summary>Fails when a <see cref="RecordRefusalKind"/> is named by no assertion in any textediting test project, so a new kind cannot arrive untested.</summary>
    public class RecordRefusalKindGuardTests
    {
        static string TestsFolder()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                string path = Path.Combine(dir.FullName, "tests", "SIL.TextEditing.Markdown.Tests");
                if (Directory.Exists(path)) return Path.Combine(dir.FullName, "tests");
            }
            throw new DirectoryNotFoundException("The tests folder was not found above " + AppContext.BaseDirectory);
        }

        [Fact]
        public void EveryRefusalKindIsAssertedByATest()
        {
            var assertion = new Regex(@"Assert|Single\(|\.Kind\s*==");
            var lines = Directory.GetFiles(TestsFolder(), "*.cs", SearchOption.AllDirectories)
                .Where(p => !p.EndsWith("RecordRefusalKindGuardTests.cs", StringComparison.Ordinal) && p.IndexOf(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal) < 0)
                .SelectMany(File.ReadAllLines).Where(l => assertion.IsMatch(l)).ToList();
            var missing = Enum.GetValues(typeof(RecordRefusalKind)).Cast<RecordRefusalKind>()
                .Where(k => !lines.Any(l => Regex.IsMatch(l, @"RecordRefusalKind\." + k + @"\b"))).ToList();
            Assert.True(missing.Count == 0, "no test asserts: " + string.Join(", ", missing));
        }
    }
}

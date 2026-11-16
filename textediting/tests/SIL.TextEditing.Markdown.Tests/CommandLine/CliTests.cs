#if NET10_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SIL.TextEditing.CommandLine;
using SIL.TextEditing.Markdown.Records;
using SIL.TextEditing.Markdown.Tests.Conformance;
using SIL.TextEditing.Markdown.Tests.Records;
using Xunit;

namespace SIL.TextEditing.Markdown.Tests.CommandLine;

public sealed class CliTests
{
    static (int Code, string Output, string Error) Run(string text, string[] args, DocumentService? service = null,
        IRecordSchemaLoader? loader = null)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        int code = new CliApplication(loader, service).Run(args, new StringReader(text), output, error);
        return (code, output.ToString(), error.ToString());
    }

    public static IEnumerable<object[]> MarkdownCases() => ConformanceLoader.All().Where(c => c.IsRead && !c.Inline).Select(c => new object[] { c.Id });
    public static IEnumerable<object[]> RecordCases() => RecordFixtures.All().Where(c => c.IsRead).Select(c => new object[] { c.Id });

    [Theory]
    [MemberData(nameof(MarkdownCases))]
    public void MarkdownFixturesMatchTheLibrary(string id)
    {
        var c = ConformanceLoader.Find(id);
        var options = ConformanceTests.OptionsFor(c, out _);
        var expected = MarkdownConverter.Parse(c.Markdown!, options);
        if (expected.Diagnostics.Any(d => d.Id == MarkdownDiagnosticIds.RecordFile))
        {
            AssertRecordFrame(c.Markdown!);
            return;
        }
        var actual = Run(c.Markdown!, new[] { "check", "-", "--json" }, new DocumentService(hostOptions: options));
        AssertFindings(actual, expected.Diagnostics);
        if (!expected.HasErrors && c.CanonicalSpecified)
        {
            var service = new DocumentService(hostOptions: options);
            string formatted = Run(c.Markdown!, new[] { "format" }, service).Output;
            Assert.Equal(MarkdownConverter.ToMarkdown(expected.Document, options), formatted);
            Assert.Equal(formatted, Run(formatted, new[] { "format" }, service).Output);
        }
    }

    [Theory]
    [MemberData(nameof(RecordCases))]
    public void RecordFixturesMatchTheLibrary(string id)
    {
        var c = RecordFixtures.Find(id);
        var options = RecordFixtures.Options(c);
        var expected = RecordFileReader.Read(c.Text!, c.Schema, options);
        var service = new DocumentService(schema: c.Schema, hostOptions: options.Markdown, recordOptions: options);
        var dispatched = new DocumentService().Analyse(c.Text!);
        if (!dispatched.IsRecord)
        {
            AssertFindings(Run(c.Text!, new[] { "check", "--json" }, service), MarkdownConverter.Parse(c.Text!, options.Markdown).Diagnostics);
            return;
        }
        AssertFindings(Run(c.Text!, new[] { "check", "-", "--json" }, service), expected.Diagnostics);
        AssertRecordFrame(c.Text!);
        if (!expected.HasErrors && expected.Document != null)
        {
            string formatted = service.Analyse(c.Text!).Format();
            Assert.Equal(RecordFileWriter.Write(expected.Document, c.Schema), formatted);
            Assert.Equal(formatted, service.Analyse(formatted).Format());
        }
    }

    static void AssertRecordFrame(string text)
    {
        var expected = RecordFileReader.ReadFrame(text);
        var actual = Run(text, new[] { "check", "-", "--json" });
        AssertFindings(actual, expected.Diagnostics);
        Assert.DoesNotContain(MarkdownDiagnosticIds.RecordFile, actual.Output);
    }

    static void AssertFindings((int Code, string Output, string Error) actual, IReadOnlyList<MarkdownDiagnostic> expected)
    {
        Assert.Equal(expected.Any(d => d.Severity == MarkdownSeverity.Error) ? 1 : 0, actual.Code);
        Assert.Empty(actual.Error);
        using var json = JsonDocument.Parse(actual.Output);
        var findings = json.RootElement[0].GetProperty("findings").EnumerateArray().ToList();
        Assert.Equal(expected.Count, findings.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            var d = expected[i];
            var finding = findings[i];
            Assert.Equal(d.Id, finding.GetProperty("id").GetString());
            Assert.Equal(d.Severity.ToString().ToLowerInvariant(), finding.GetProperty("severity").GetString());
            Assert.Equal(d.Message, finding.GetProperty("message").GetString());
            Assert.Equal(d.Line, finding.GetProperty("line").GetInt32());
            Assert.Equal(d.Column, finding.GetProperty("col").GetInt32());
            Assert.Equal(d.EndLine, finding.GetProperty("endLine").GetInt32());
            Assert.Equal(d.EndColumn, finding.GetProperty("endCol").GetInt32());
        }
    }

    [Theory]
    [InlineData("check --wat")]
    [InlineData("check --config")]
    [InlineData("check --schema")]
    [InlineData("check --schema missing.json")]
    [InlineData("check --config --json")]
    [InlineData("check --json --json")]
    [InlineData("check - missing.md")]
    [InlineData("check missing.md")]
    [InlineData("format --write --check")]
    [InlineData("format --write -")]
    [InlineData("format --json")]
    [InlineData("lsp")]
    [InlineData("")]
    public void UsageErrorsReturnTwo(string command)
    {
        var actual = Run("", command.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal(2, actual.Code);
        Assert.Empty(actual.Output);
        Assert.NotEmpty(actual.Error);
    }

    [Fact]
    public void HelpDescribesCommandsAndLimits()
    {
        var result = Run("", new[] { "--help" });
        Assert.Equal(0, result.Code);
        Assert.Equal(CliApplication.Usage + Environment.NewLine, result.Output);
        Assert.Empty(result.Error);
    }

    [Fact]
    public void WarningAndErrorExitCodes()
    {
        var warning = ConformanceLoader.All().First(c => c.IsRead && !c.Inline && !c.HasErrors && c.Findings.Any(f => f.Severity == "warning"));
        var service = new DocumentService(hostOptions: ConformanceTests.OptionsFor(warning, out _));
        Assert.Equal(0, Run(warning.Markdown!, new[] { "check" }, service).Code);
        Assert.Equal(1, Run(warning.Markdown!, new[] { "check", "--warnings-as-errors" }, service).Code);
        Assert.Equal(1, Run("---\nfwmd: 99\n---\ntext\n", new[] { "check" }).Code);
        Assert.Equal(0, Run("text\n", new[] { "check" }).Code);
    }

    [Theory]
    [InlineData("clean", "text\n")]
    [InlineData("version", "---\nfwmd: 99\n---\ntext\n")]
    [InlineData("record", "---\nfwmd: 1\nrecord: \"entry\"\n---\n")]
    public void JsonGoldens(string name, string text)
    {
        var actual = Run(text, new[] { "check", "--json" });
        var path = Path.Combine(AppContext.BaseDirectory, "CommandLine", "Golden", name + ".json");
        Assert.Equal(File.ReadAllText(path).Replace("\r\n", "\n").TrimEnd(), actual.Output.Replace("\r\n", "\n").TrimEnd());
    }

    [Fact]
    public void FormatCanonicalizesAndChecksWithoutWriting()
    {
        const string text = "**bold**\r\n\r\nplain\r\n";
        var formatted = Run(text, new[] { "format" });
        Assert.Equal(0, formatted.Code);
        Assert.Equal(MarkdownConverter.ToMarkdown(MarkdownConverter.Parse(text).Document,
            new MarkdownOptions { WriteFrontMatter = false }), formatted.Output);
        Assert.Equal(1, Run(text, new[] { "format", "--check" }).Code);
        Assert.Equal(0, Run(formatted.Output, new[] { "format", "--check" }).Code);
        Assert.Equal(formatted.Output, Run(formatted.Output, new[] { "format" }).Output);
    }

    [Fact]
    public void ErrorsAndMissingRecordSchemaNeverWrite()
    {
        using var files = new Files();
        string good = files.Add("a.md", "text\r\n");
        string bad = files.Add("b.md", "---\nfwmd: 99\n---\ntext\n");
        Assert.Equal(1, Run("", new[] { "format", files.Root, "--write" }).Code);
        Assert.Equal("text\r\n", File.ReadAllText(good));
        Assert.Equal(1, Run("", new[] { "format", bad }).Code);
        string record = files.Add("r.md", "---\nfwmd: 1\nrecord: \"entry\"\n---\n");
        var result = Run("", new[] { "format", record, "--write" });
        Assert.Equal(2, result.Code);
        Assert.Contains("schema", result.Error);
        Assert.Contains("record:", File.ReadAllText(record));
    }

    [Fact]
    public void DirectoriesRecurseDeduplicateAndWriteOnlyMarkdown()
    {
        using var files = new Files();
        string a = files.Add("a.md", "one\r\n");
        string b = files.Add("sub/b.md", "two\r\n");
        string other = files.Add("sub/c.txt", "three\r\n");
        var check = Run("", new[] { "check", files.Root, a, "--json" });
        using var json = JsonDocument.Parse(check.Output);
        Assert.Equal(0, check.Code);
        Assert.Equal(new[] { a, b }, json.RootElement.EnumerateArray().Select(e => e.GetProperty("path").GetString()));
        Assert.Equal(0, Run("", new[] { "format", files.Root, "--write" }).Code);
        Assert.Equal("one\n", File.ReadAllText(a));
        Assert.Equal("two\n", File.ReadAllText(b));
        Assert.Equal("three\r\n", File.ReadAllText(other));
        Assert.Equal(0, Run("", new[] { "format", files.Root, "--check" }).Code);
    }

    [Fact]
    public void ConfigSuppliesWritingSystemsAndStylesAndFrontMatterWins()
    {
        using var files = new Files();
        string config = files.Add("fwmd.json", """
            {"analysis":"fr","vernacular":"fr","writingSystems":{"de":{}},"styles":{"paragraph":["Normal"],"character":["Emphasis"]}}
            """);
        var args = new[] { "check", "--config", config, "--json" };
        Assert.Equal(0, Run("[word]{ws=de}\n", args).Code);
        Assert.Equal(1, Run("[word]{ws=es}\n", args).Code);
        Assert.Equal(1, Run("[word]{custom-style=Missing}\n", args).Code);
        Assert.Equal(0, Run("[word]{custom-style=Emphasis}\n", args).Code);
        Assert.Equal(0, Run("[word]{custom-style=Missing}\n", new[] { "check" }).Code);
        const string text = "---\nfwmd: 1\nanalysis: en\nvernacular: es\nwritingSystems:\n  ar:\n    lang: ar\n    dir: rtl\n    font: Charis SIL\n---\n[word]{ws=ar}\n";
        Assert.Equal(0, Run(text, args).Code);
        var formatted = Run(text, new[] { "format" });
        Assert.Equal(0, formatted.Code);
        Assert.Contains("analysis: \"en\"", formatted.Output);
        Assert.Contains("vernacular: \"es\"", formatted.Output);
        Assert.Contains("dir: \"rtl\"", formatted.Output);
        Assert.Equal(formatted.Output, Run(formatted.Output, new[] { "format" }).Output);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{")]
    [InlineData("{\"styles\":[]}")]
    [InlineData("{\"analysis\":2}")]
    [InlineData("{\"analysis\":\"\"}")]
    [InlineData("{\"unknown\":true}")]
    [InlineData("{\"writingSystems\":{\"en\":{\"direction\":\"up\"}}}")]
    public void InvalidConfigIsAUsageError(string config)
    {
        using var files = new Files();
        string path = files.Add("fwmd.json", config);
        Assert.Equal(2, Run("", new[] { "check", "--config", path }).Code);
    }

    [Fact]
    public void PicturesArePreservedWithoutResolvingProjectObjects()
    {
        const string text = "![A hut](/files/hut.jpg){guid=1f2e3d4c-5b6a-4978-8695-a4b3c2d1e0f9}\n";
        var check = Run(text, new[] { "check", "--json" });
        Assert.Equal(0, check.Code);
        Assert.DoesNotContain("fwmd.picture.unknown", check.Output);
        var formatted = Run(text, new[] { "format" });
        Assert.Equal(0, formatted.Code);
        Assert.Equal(text, formatted.Output);
        Assert.Equal(formatted.Output, Run(formatted.Output, new[] { "format" }).Output);
    }

    [Fact]
    public void SchemaLoaderCanBeWiredWithoutChangingTheRunner()
    {
        var c = RecordFixtures.All().First(c => c.IsRead && c.CanonicalSpecified && !c.HasErrors);
        var result = Run(c.Text!, new[] { "check", "--schema", "schema.json", "--json" }, loader: new Loader(c.Schema));
        Assert.NotEqual(2, result.Code);
        using var json = JsonDocument.Parse(result.Output);
        Assert.True(json.RootElement[0].GetProperty("checks").GetProperty("recordFields").GetBoolean());
    }

#if TEXTEDITING_COUNTERS
    [Fact]
    public void RecordFrameWorkIsProportionalToInput()
    {
        long Read(int count)
        {
            RecordWork.Steps = 0;
            var text = "---\nfwmd: 1\nrecord: \"entry\"\nrecord-data:\n"
                + string.Concat(Enumerable.Range(0, count).Select(i => "  key" + i + ": \"value\"\n")) + "---\n";
            Assert.Equal(0, Run(text, new[] { "check", "--json" }).Code);
            return RecordWork.Steps;
        }
        long small = Read(100), large = Read(200);
        Assert.True(small > 0);
        Assert.True(large <= small * 3, $"{small} -> {large}");
    }
#endif

    sealed class Loader(RecordSchema schema) : IRecordSchemaLoader
    {
        public RecordSchema Load(string path) => schema;
    }

    sealed class Files : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "fwmd-" + Guid.NewGuid().ToString("N"));
        public string Add(string name, string text)
        {
            string path = Path.Combine(Root, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
            return path;
        }
        public void Dispose() => Directory.Delete(Root, true);
    }
}
#endif

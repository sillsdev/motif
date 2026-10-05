using System.Text.Json;
using System.Xml.Linq;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.PanGloss;

[Collection(LcmCacheParallelCollections.Group1)]
[Trait("MotifTestLevel", "System")]
public sealed class RealCompileDiagnosticTests(PristineProjectFixture pristine, ITestOutputHelper output)
{
    [RealParserFact]
    public async Task DanglingEnvironmentPreservesTheRealFatalAttachmentDiagnostic()
    {
        string path;
        Guid environmentId;
        using (var cache = pristine.NewScratch())
        {
            path = cache.ProjectId.Path;
            IPhEnvironment environment = null!;
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                environment = cache.ServiceLocator.GetInstance<IPhEnvironmentFactory>().Create();
                cache.LangProject.PhonologicalDataOA.EnvironmentsOS.Add(environment);
                environment.StringRepresentation = TsStringUtils.MakeString("/ _ a", cache.DefaultVernWs);
                var form = (IMoStemAllomorph)cache.ServiceLocator.GetInstance<IMoFormRepository>()
                    .GetObject(pristine.Seed.FirstLexemeFormId);
                form.PhoneEnvRC.Add(environment);
            });
            environmentId = environment.Guid;
            RealParserProject.PrepareForParsing(cache, "m", "o", "t", "i", "f", "a", "b");
        }

        var document = XDocument.Load(path);
        Assert.Single(document.Descendants("rt"), row =>
            (string?)row.Attribute("guid") == environmentId.ToString()).Remove();
        document.Save(path);

        var refusal = await RunRefusal(path);
        using var wire = ReadWire(refusal);
        var row = Assert.Single(wire.RootElement.GetProperty("issues").EnumerateArray(), issue =>
            issue.GetProperty("code").GetString() == "grammar.environment.unresolved" &&
            issue.GetProperty("object_guid").GetString() == pristine.Seed.FirstLexemeFormId.ToString());
        Assert.True(row.GetProperty("fatal").GetBoolean());
        Assert.Equal("MoForm", row.GetProperty("object_kind").GetString());
        var issue = Assert.Single(refusal.ParserDiagnostic!.Issues, issue =>
            issue.Code == "grammar.environment.unresolved" && issue.ObjectGuid == pristine.Seed.FirstLexemeFormId.ToString());
        AssertWireIssue(row, issue);
        var fatalRows = wire.RootElement.GetProperty("issues").EnumerateArray()
            .Where(issue => issue.GetProperty("fatal").GetBoolean()).ToArray();
        Assert.Equal(fatalRows.Length, refusal.ParserDiagnostic.Issues.Count);
        foreach (var pair in fatalRows.Zip(refusal.ParserDiagnostic.Issues))
            AssertWireIssue(pair.First, pair.Second);
    }

    [RealParserFact]
    public async Task XmlCompileFailurePreservesTheRealSourceFreeDiagnostic()
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-real-compile-error-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "invalid.xml");
            File.WriteAllText(path, "<HermitCrabInput><Language>");
            var refusal = await RunRefusal(path);
            using var wire = ReadWire(refusal);
            var row = Assert.Single(wire.RootElement.GetProperty("issues").EnumerateArray());
            Assert.Equal("grammar.compile.failed", row.GetProperty("code").GetString());
            Assert.True(row.GetProperty("fatal").GetBoolean());
            var issue = Assert.Single(refusal.ParserDiagnostic!.Issues);
            Assert.Null(issue.ObjectGuid);
            Assert.Null(issue.ObjectKind);
            Assert.Null(issue.Field);
            AssertWireIssue(row, issue);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private async Task<Refusal> RunRefusal(string path)
    {
        var executable = PanGlossExecutable.TryLocate()!;
        output.WriteLine("Parser: " + ParserExecutableIdentity.Read(executable));
        using var invoker = new PanGlossInvoker(executable);
        var result = await invoker.RunAsync(new PanGlossRequest.Trace(path, SeededProject.FirstForm),
            "compile-refusal integration", CancellationToken.None, TimeSpan.FromSeconds(30));
        var failure = Assert.IsType<PanGlossOutcome.Refused>(result);
        output.WriteLine("Exit: " + failure.ExitCode);
        output.WriteLine("stdout: " + failure.StandardOutput);
        output.WriteLine("stderr: " + failure.StandardError);
        Assert.True(failure.ExitCode != 0, failure.StandardError);
        Assert.DoesNotContain("ConversionError {", failure.StandardError);
        var refusal = ParserExecutionRefusal.From(RefusalCodes.WordTraceParserRefused, path, failure);
        Assert.NotNull(refusal.ParserDiagnostic);
        Assert.Contains("can't use this grammar", refusal.Message);
        Assert.Equal(failure.StandardError, refusal.Facts["standardError"]);
        Assert.Contains(failure.StandardError, refusal.ParserDiagnostic.RawText);
        return refusal;
    }

    private static JsonDocument ReadWire(Refusal refusal)
    {
        foreach (var line in refusal.Facts["standardError"].Split('\n'))
        {
            JsonDocument document;
            try { document = JsonDocument.Parse(line); }
            catch (JsonException) { continue; }
            if (document.RootElement.TryGetProperty("status", out var status) && status.GetString() == "compile_error")
            {
                Assert.Equal(1, document.RootElement.GetProperty("schema_version").GetInt32());
                return document;
            }
            document.Dispose();
        }
        throw new InvalidOperationException("The real parser emitted no compile_error JSON stderr line.");
    }

    private static void AssertWireIssue(JsonElement row, ParserCompileIssue issue)
    {
        Assert.Equal(row.GetProperty("code").GetString(), issue.Code);
        Assert.Equal(row.GetProperty("kind").GetString(), issue.Kind);
        Assert.Equal(row.GetProperty("object_guid").GetString(), issue.ObjectGuid);
        Assert.Equal(row.GetProperty("object_kind").GetString(), issue.ObjectKind);
        Assert.Equal(row.GetProperty("field").GetString(), issue.Field);
        Assert.Equal(row.GetProperty("text").GetString(), issue.Text);
        Assert.Equal(row.GetProperty("advice").GetString(), issue.Advice);
        Assert.False(string.IsNullOrWhiteSpace(issue.Text));
        Assert.False(string.IsNullOrWhiteSpace(issue.Advice));
    }
}

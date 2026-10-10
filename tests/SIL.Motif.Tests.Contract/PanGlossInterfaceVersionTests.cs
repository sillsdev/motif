using System.Linq;
using System.Text.Json;
using SIL.Motif.Generator;
using SIL.Motif.Host.PanGloss;
using Xunit;

namespace SIL.Motif.Tests.PanGloss;

public sealed class PanGlossInterfaceVersionTests
{
    [Fact]
    public void ReleaseManifestMatchesEveryPanGlossReaderVersion()
    {
        var path = Path.Combine(RepoPaths.FindRepoRoot(), "pangloss-release.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var interfaces = document.RootElement.GetProperty("interfaces");
        var sourceTag = interfaces.GetProperty("sourceTag").GetString();
        Assert.Matches(@"^v\d+\.\d+\.\d+$", sourceTag);
        Assert.Equal(PanGlossInterfaceVersions.SourceTag, sourceTag);
        Assert.Equal(PanGlossInterfaceVersions.DescribeSchemaVersion,
            interfaces.GetProperty("describeSchemaVersion").GetInt32());
        Assert.Equal(PanGlossInterfaceVersions.GrammarHealthSchemaVersion,
            interfaces.GetProperty("grammarHealthSchemaVersion").GetInt32());
        Assert.Equal(PanGlossInterfaceVersions.TraceDetailsSchemaId,
            interfaces.GetProperty("traceDetailsSchemaId").GetString());
        Assert.Equal(PanGlossInterfaceVersions.StatsCacheVersion,
            interfaces.GetProperty("statsCacheVersion").GetInt32());
        Assert.Equal(PanGlossInterfaceVersions.CompileErrorSchemaVersion,
            interfaces.GetProperty("compileErrorSchemaVersion").GetInt32());
        Assert.Equal(PanGlossInterfaceVersions.CompileErrorSchemaVersion, PanGlossCompileError.SchemaVersion);
        Assert.Equal(PanGlossInterfaceVersions.CompileErrorSchemaVersion, ParserCompileDiagnosticReader.SchemaVersion);

        var columns = interfaces.GetProperty("batchTsvColumns").EnumerateArray()
            .Select(column => column.GetString()).ToArray();
        Assert.Equal(PanGlossInterfaceVersions.BatchTsvColumns, columns);
        Assert.Equal(PanGlossInterfaceVersions.BatchTsvCompletionColumnCount, columns.Length);
        Assert.Equal("index", columns[PanGlossInterfaceVersions.BatchTsvIndexColumn]);
        Assert.Equal("word", columns[PanGlossInterfaceVersions.BatchTsvWordColumn]);
        Assert.Equal("elapsed_ms", columns[PanGlossInterfaceVersions.BatchTsvElapsedMsColumn]);
        Assert.Equal("status", columns[PanGlossInterfaceVersions.BatchTsvStatusColumn]);
        Assert.Equal("signature", columns[PanGlossInterfaceVersions.BatchTsvSignatureColumn]);

        var facts = interfaces.GetProperty("facts");
        Assert.Equal(PanGlossInterfaceVersions.FactsSchemaVersion, facts.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(PanGlossInterfaceVersions.FactsContextVersion, facts.GetProperty("contextVersion").GetInt32());
        Assert.Equal(PanGlossInterfaceVersions.StatsManifestVersion,
            facts.GetProperty("statsManifestVersion").GetInt32());
    }
}

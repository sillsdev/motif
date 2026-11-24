using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using SIL.Motif.Contract.Query;
using Xunit;

namespace SIL.Motif.Tests.Contract.Query;

public sealed class QuerySchemaTests
{
    private static string GoldenPath([CallerFilePath] string source = "") =>
        Path.Combine(Path.GetDirectoryName(source)!, "..", "Golden", "query.schema.json");

    [Fact]
    public void TheGeneratedSchemaMatchesTheGolden()
    {
        var generated = QuerySchema.Generate();
        var path = GoldenPath();
        if (Environment.GetEnvironmentVariable("MOTIF_UPDATE_GOLDEN") == "1") File.WriteAllText(path, generated);
        Assert.Equal(File.ReadAllText(path).ReplaceLineEndings("\n"), generated);
    }

    [Fact]
    public void TheSchemaListsEveryOpEverySourceAndEveryReservedKind()
    {
        var schema = JsonNode.Parse(QuerySchema.Generate())!;
        var ops = schema["$defs"]!["field"]!["properties"]!["op"]!["enum"]!.AsArray().Select(op => op!.GetValue<string>());
        Assert.Equal(QueryOps.All, ops);
        var sources = schema["properties"]!["from"]!["enum"]!.AsArray().Select(source => source!.GetValue<string>());
        Assert.Equal(QuerySources.All, sources);
        Assert.Equal(QueryOps.ReservedKinds, schema["x-reserved"]!.AsArray().Select(kind => kind!.GetValue<string>()));
        Assert.Equal(QuerySources.All, schema["x-columns"]!.AsObject().Select(pair => pair.Key));
        Assert.Contains(schema["x-columns"]!["forms"]!.AsArray(), column => column!["name"]!.GetValue<string>() == "tokens" &&
            column["type"]!.GetValue<string>() == "numeric");
    }

    [Fact]
    public void EveryPropertyOfTheDocumentRecordIsInTheSchemaAndNoOtherIs()
    {
        var schema = JsonNode.Parse(QuerySchema.Generate())!;
        var listed = schema["properties"]!.AsObject().Select(pair => pair.Key).Order(StringComparer.Ordinal).ToArray();
        var declared = typeof(QueryDocument).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.DeclaredOnly).Select(property => char.ToLowerInvariant(property.Name[0]) + property.Name[1..])
            .Append("version").Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(declared, listed);
        Assert.Equal(["from", "version"], schema["required"]!.AsArray().Select(name => name!.GetValue<string>()).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheSchemaIsDeterministic() => Assert.Equal(QuerySchema.Generate(), QuerySchema.Generate());

    [Fact]
    public void PhonAndEnvAreRealKindsWithTheirPatternSyntaxDocumented()
    {
        var schema = JsonNode.Parse(QuerySchema.Generate())!;
        var kinds = schema["$defs"]!["expr"]!["oneOf"]!.AsArray().Select(node => node!["required"]?.AsArray().FirstOrDefault()?.GetValue<string>()).ToArray();
        Assert.Contains("phon", kinds);
        Assert.Contains("env", kinds);
        Assert.DoesNotContain("phon", schema["x-reserved"]!.AsArray().Select(kind => kind!.GetValue<string>()));
        var syntax = schema["$defs"]!["expr"]!["oneOf"]!.AsArray().First(node => node!["required"]?[0]?.GetValue<string>() == "phon")!["properties"]!["phon"]!["description"]!.GetValue<string>();
        foreach (var part in new[] { "natural-class", "[+voice -nasal]", "[αvoice]", "/p/", "( )*", "#", "+" }) Assert.Contains(part, syntax);
    }
}

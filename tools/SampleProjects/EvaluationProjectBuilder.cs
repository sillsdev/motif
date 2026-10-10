using System.Text.Json;
using System.Text.Json.Serialization;

namespace SIL.Motif.SampleProjects;

/// <summary>A generated project's path and the stable form identities used to check its analyses.</summary>
public sealed record EvaluationBuildResult(string ProjectPath, IReadOnlyDictionary<string, string> FormIds);

/// <summary>Builds an evaluation project from a JSON-compatible YAML language spec.</summary>
public static class EvaluationProjectBuilder
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Creates a blank LibLCM project and seeds either its lexicon alone or the complete declared grammar.
    /// The caller supplies a fresh output root for each build so writing systems and project files stay local.
    /// </summary>
    public static EvaluationBuildResult Build(string languageSpecYaml, string outputRoot, string start)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(languageSpecYaml);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(start);
        var spec = JsonSerializer.Deserialize<SampleSpec>(languageSpecYaml, Options)
            ?? throw new InvalidDataException("The language specification is empty.");
        return SampleBuilder.BuildForEvaluation(spec, outputRoot, start);
    }
}

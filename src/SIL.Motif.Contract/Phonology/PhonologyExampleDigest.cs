using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Phonology;

public static class PhonologyExampleDigest
{
    public static string Executable(IReadOnlyList<PhonologyExample> examples)
    {
        ArgumentNullException.ThrowIfNull(examples);
        var projection = new ExecutableProjection(1, examples.Select(example =>
            new ExecutableExample(example.Id, example.Underlying, example.Expected, example.Context)).ToArray());
        return Hash(JsonSerializer.Serialize(projection, PhonologyJson.CreateOptions()));
    }

    public static string CitedRevision(string name, string? proposalId, IReadOnlyList<PhonologyExample> examples)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(examples);
        return Hash(JsonSerializer.Serialize(new CitedProjection(1, name, proposalId, examples),
            PhonologyJson.CreateOptions()));
    }

    private static string Hash(string json) => "sha256:" +
        Convert.ToHexString(SHA256.HashData(CanonicalJson.CanonicalizeToUtf8(json))).ToLowerInvariant();

    private sealed record ExecutableProjection(
        [property: JsonPropertyName("version")] int Version,
        [property: JsonPropertyName("examples")] IReadOnlyList<ExecutableExample> Examples);

    private sealed record ExecutableExample(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("underlying")] string Underlying,
        [property: JsonPropertyName("expected")] IReadOnlyList<string> Expected,
        [property: JsonPropertyName("context")] PhonologyGenerationContext Context);

    private sealed record CitedProjection(
        [property: JsonPropertyName("version")] int Version,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("proposalId")] string? ProposalId,
        [property: JsonPropertyName("examples")] IReadOnlyList<PhonologyExample> Examples);
}

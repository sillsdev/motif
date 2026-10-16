using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SIL.Motif.Contract.Phonology;
using SIL.Motif.Host.Assess;

namespace SIL.Motif.Host.PanGloss;

internal sealed record PhonologyInvocationInputs(string GrammarPath, string GrammarSha256,
    IReadOnlyList<PhonologyGrammarDependency> Dependencies, string RequestPath, string RequestFileSha256,
    string ExecutableSha256)
{
    internal const int MaxBytes = 32 * 1024 * 1024;

    internal static PanGlossRequest.PhonologyExamples Freeze(PanGlossRequest.PhonologyExamples request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.GrammarPath);
        if (!File.Exists(request.GrammarPath)) throw new FileNotFoundException("The grammar is absent.", request.GrammarPath);
        ArgumentNullException.ThrowIfNull(request.Limits);
        if (!PhonologyExampleValidation.AreLimitsInRange(request.Limits))
            throw new ArgumentOutOfRangeException(nameof(request), "Phonology ceilings must be positive and bounded.");
        if (request.Examples is null || request.Examples.Count is < 1 or > 500)
            throw new ArgumentException("Phonology requires 1–500 examples.", nameof(request));
        var examples = new PhonologyExample[request.Examples.Count];
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < examples.Length; i++)
        {
            var example = request.Examples[i] ?? throw new ArgumentException("An example is null.");
            Text(example.Id, 128);
            if (!ids.Add(example.Id)) throw new ArgumentException("Example ids must be unique.");
            Text(example.Underlying, 256);
            if (example.Expected is null || example.Expected.Count is < 1 or > 16)
                throw new ArgumentException("Each example needs 1–16 expectations.");
            var expected = example.Expected.ToArray();
            foreach (var form in expected) Text(form, 256);
            ValidateContext(example.Context);
            examples[i] = example with { Expected = Array.AsReadOnly(expected) };
        }
        return request with { GrammarPath = Path.GetFullPath(request.GrammarPath), Examples = Array.AsReadOnly(examples), Inputs = null };
    }

    internal static void ValidateContext(PhonologyGenerationContext? context)
    {
        switch (context)
        {
            case PhonologyGenerationContext.HcXmlEntry hc: Text(hc.EntryId, 256); break;
            case PhonologyGenerationContext.FwdataEntry fw when fw.MsaClass == PhonologyMsaClass.MoStemMsa:
                Text(fw.EntryGuid, 256); Text(fw.MsaGuid, 256); break;
            default: throw new ArgumentException("Unsupported phonology context.");
        }
    }

    internal static void Text(string? text, int max, bool allowEmpty = false)
    {
        if (text is null || (!allowEmpty && string.IsNullOrWhiteSpace(text)))
            throw new ArgumentException("A bounded Unicode string is required.");
        var remaining = text.AsSpan();
        var count = 0;
        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out _, out var used) != OperationStatus.Done || ++count > max)
                throw new ArgumentException("Text exceeds its Unicode scalar bound or contains invalid Unicode.");
            remaining = remaining[used..];
        }
    }

    internal static async Task<PanGlossRequest.PhonologyExamples> PrepareAsync(
        PanGlossRequest.PhonologyExamples request, string scratch, string executable, CancellationToken token)
    {
        var inputDirectory = Path.Combine(scratch, "inputs");
        Directory.CreateDirectory(inputDirectory);
        var grammar = Path.Combine(inputDirectory, Path.GetFileName(request.GrammarPath));
        File.Copy(request.GrammarPath, grammar);
        var dependencies = new List<PhonologyGrammarDependency>();
        var store = Path.Combine(Path.GetDirectoryName(request.GrammarPath)!, "WritingSystemStore");
        if (string.Equals(Path.GetExtension(request.GrammarPath), ".fwdata", StringComparison.OrdinalIgnoreCase) && Directory.Exists(store))
        {
            var destination = Path.Combine(inputDirectory, "WritingSystemStore");
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.EnumerateFiles(store).Where(p => Path.GetExtension(p) == ".ldml").OrderBy(p => FileNameSortKey(Path.GetFileName(p)), StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested();
                var name = Path.GetFileName(file);
                var copied = Path.Combine(destination, name);
                File.Copy(file, copied);
                dependencies.Add(new PhonologyGrammarDependency("WritingSystemStore/" + name, DigestFile(copied)));
            }
        }
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new PhonologyWireRequest(1, request.Limits,
            request.Examples.Select(e => new PhonologyWireExample(e.Id, PhonologyWire.WriteContext(e.Context), e.Underlying, e.Expected)).ToArray()),
            PhonologyWire.Options);
        if (bytes.Length > MaxBytes) throw new IOException("Phonology request exceeds 32 MiB.");
        var requestPath = Path.Combine(scratch, "request.json");
        await File.WriteAllBytesAsync(requestPath, bytes, token).ConfigureAwait(false);
        return request with { GrammarPath = grammar, Inputs = new PhonologyInvocationInputs(grammar, DigestFile(grammar),
            dependencies.AsReadOnly(), requestPath, Digest(bytes), DigestFile(executable)) };
    }

    internal bool Unchanged(string executable)
    {
        if (GrammarSha256 != DigestFile(GrammarPath) || RequestFileSha256 != DigestFile(RequestPath) ||
            ExecutableSha256 != DigestFile(executable)) return false;
        var store = Path.Combine(Path.GetDirectoryName(GrammarPath)!, "WritingSystemStore");
        var current = Directory.Exists(store)
            ? Directory.EnumerateFiles(store).Where(p => Path.GetExtension(p) == ".ldml").Select(Path.GetFileName).OrderBy(p => FileNameSortKey(p!), StringComparer.Ordinal).ToArray() : [];
        if (!current.SequenceEqual(Dependencies.Select(d => Path.GetFileName(d.Path)))) return false;
        return Dependencies.All(d => d.Sha256 == DigestFile(Path.Combine(Path.GetDirectoryName(GrammarPath)!, d.Path)));
    }

    private static string FileNameSortKey(string name) => Convert.ToHexString(Encoding.UTF8.GetBytes(name));

    internal static byte[] ReadBounded(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaxBytes) throw new IOException("Phonology output exceeds 32 MiB.");
        using var result = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = stream.Read(buffer)) != 0)
        {
            if (result.Length + read > MaxBytes) throw new IOException("Phonology output exceeds 32 MiB.");
            result.Write(buffer, 0, read);
        }
        return result.ToArray();
    }

    internal static string DigestFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    internal static string Digest(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}

internal sealed record PhonologyWireRequest(int Version, PhonologyLimits Limits, PhonologyWireExample[] Examples);
internal sealed record PhonologyWireExample(string Id, JsonElement Context, string Underlying,
    IReadOnlyList<string> Expected);

internal static class PhonologyWire
{
    internal static JsonElement WriteContext(PhonologyGenerationContext context) => context switch
    {
        PhonologyGenerationContext.HcXmlEntry hc => JsonSerializer.SerializeToElement(new HcContext("hcXmlEntry", hc.EntryId), Options),
        PhonologyGenerationContext.FwdataEntry fw => JsonSerializer.SerializeToElement(
            new FwContext("fwdataEntry", fw.EntryGuid, fw.MsaGuid, "MoStemMsa"), Options),
        _ => throw new ArgumentException("Unsupported context."),
    };

    internal static PhonologyGenerationContext ReadContext(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("kind", out var kind))
            throw new JsonException("Context kind is missing.");
        PhonologyGenerationContext context;
        switch (kind.GetString())
        {
            case "hcXmlEntry":
                var hc = element.Deserialize<HcContext>(Options)!;
                context = new PhonologyGenerationContext.HcXmlEntry(hc.EntryId);
                break;
            case "fwdataEntry":
                var fw = element.Deserialize<FwContext>(Options)!;
                if (fw.MsaClass != "MoStemMsa") throw new JsonException("Unsupported MSA class.");
                context = new PhonologyGenerationContext.FwdataEntry(fw.EntryGuid, fw.MsaGuid, PhonologyMsaClass.MoStemMsa);
                break;
            default: throw new JsonException("Unknown context kind.");
        }
        PhonologyInvocationInputs.ValidateContext(context);
        return context;
    }

    private sealed record HcContext(string Kind, string EntryId);
    private sealed record FwContext(string Kind, string EntryGuid, string MsaGuid, string MsaClass);

    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        AllowOutOfOrderMetadataProperties = true,
    };
}

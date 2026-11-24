using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using SIL.Motif.Host.Parsimony;

namespace SIL.Motif.Host.PanGloss;

internal sealed record PanGlossSurfaceCheck(
    bool IsValid, string Message, PanGlossCapabilities? Capabilities = null)
{
    internal PanGlossOutcome? Failure { get; init; }
}

internal sealed record PanGlossCapabilities(
    ImmutableDictionary<string, PanGlossCommandCapabilities> Commands,
    bool FactsContractSupported, int? ServeProtocol = null, int? BatchTsv = null,
    PanGlossWhyNotCapability? WhyNot = null, PanGlossServeGenerateCapability? ServeGenerate = null,
    PanGlossServeMatchCapability? ServeMatch = null, PanGlossServeInventoryCapability? ServeInventory = null)
{
    /// <summary>
    /// Why this parser cannot match pattern text over a word list on <c>serve</c>, or null when it can: the description must
    /// declare <c>interfaces.serveOps.match</c>, and list every construct the pattern needs (<c>environment</c>,
    /// <c>featureBundle</c>, <c>alphaVariable</c>) under its <c>constructs.supported</c>.
    /// </summary>
    internal string? ServeMatchRefusal(IReadOnlyCollection<string>? needs = null)
    {
        if (ServeMatch is null) return "its description declares no interfaces.serveOps.match";
        foreach (var need in needs ?? [])
            if (!ServeMatch.Supported.Contains(need))
                return $"its interfaces.serveOps.match does not list {need} among its supported constructs";
        return null;
    }

    /// <summary>Why this parser cannot list a grammar's natural classes and features on <c>serve</c>, or null when it can.</summary>
    internal string? ServeInventoryRefusal() =>
        ServeInventory is null ? "its description declares no interfaces.serveOps.inventory" : null;

    /// <summary>
    /// Why this parser cannot answer a traced <c>generate</c> over <c>serve</c>, or null when it can: the description must
    /// declare <c>interfaces.serveOps.generate</c> with <c>trace</c> true.
    /// </summary>
    internal string? ServeGenerateRefusal() =>
        ServeGenerate is null ? "its description declares no interfaces.serveOps.generate"
        : !ServeGenerate.Trace ? "it declares serveOps.generate without a rule trace"
        : null;

    /// <summary>
    /// Why this parser cannot answer why-not, or null when it can: the description must expose a visible
    /// <c>why-not</c> command and declare <c>interfaces.whyNot</c> with Motif's schema and the <c>grammar-xml</c> id promise.
    /// </summary>
    internal string? WhyNotRefusal()
    {
        if (!Commands.TryGetValue("why-not", out var command) || command.Hidden)
            return "its description has no why-not command";
        if (WhyNot is null) return "its description declares no interfaces.whyNot";
        if (WhyNot.Schema != PanGlossInterfaceVersions.WhyNotSchemaId)
            return $"it declares why-not schema '{WhyNot.Schema ?? "none"}', and Motif reads {PanGlossInterfaceVersions.WhyNotSchemaId}";
        return WhyNot.Ids == PanGlossInterfaceVersions.WhyNotIds
            ? null
            : $"it declares why-not ids '{WhyNot.Ids ?? "none"}', and Motif needs {PanGlossInterfaceVersions.WhyNotIds}";
    }

    /// <summary>Why these candidates cannot be sent, when one is named by guid alone and the parser does not declare it reads that.</summary>
    internal string? GuidChoiceRefusal(IReadOnlyList<WhyNotCandidate> candidates) =>
        WhyNotInput.HasGuidOnlyChoice(candidates) && WhyNot is { ExpectGuidChoices: false }
            ? "its description does not declare interfaces.whyNot.expectGuidChoices"
            : null;

    internal string? ValidateRequest(PanGlossRequest request)
    {
        if ((request is PanGlossRequest.Facts or PanGlossRequest.GrammarHealth { FactsContextJson: not null }) && !FactsContractSupported)
            return "the description does not declare the supported grammar-facts and context identity.";
        var startInfo = new ProcessStartInfo();
        request.AddArguments(startInfo, "scratch");
        if (startInfo.ArgumentList.Count == 0 ||
            !string.Equals(startInfo.ArgumentList[0], request.Subcommand, StringComparison.Ordinal))
            return $"the request does not emit its declared '{request.Subcommand}' command.";

        var commandName = startInfo.ArgumentList[0];
        if (!Commands.TryGetValue(commandName, out var command))
            return $"the description does not expose '{commandName}'.";
        if (command.Hidden) return $"the description hides '{commandName}'.";

        if (request is PanGlossRequest.PhonologyExamples &&
            !command.Positionals.SequenceEqual(new[] { "grammar", "request.json", "out.json?" }))
            return "the phonology-examples description does not declare the version-one request/result surface.";
        var positionalCount = 0;
        for (var index = 1; index < startInfo.ArgumentList.Count; index++)
        {
            var argument = startInfo.ArgumentList[index];
            if (!argument.StartsWith("-", StringComparison.Ordinal))
            {
                positionalCount++;
                continue;
            }
            var separator = argument.IndexOf('=');
            var flagName = separator < 0 ? argument : argument[..separator];
            if (!command.Flags.TryGetValue(flagName, out var takesValue))
                return $"the description does not declare '{commandName} {flagName}'.";
            if (separator >= 0)
            {
                if (!takesValue)
                    return $"the description declares '{commandName} {flagName}' without a value.";
                continue;
            }
            if (!takesValue) continue;
            var bareTrace = commandName == "parse" && flagName == "--trace";
            if (bareTrace && (index + 1 == startInfo.ArgumentList.Count ||
                startInfo.ArgumentList[index + 1].StartsWith("-", StringComparison.Ordinal)))
                continue;
            if (index + 1 == startInfo.ArgumentList.Count ||
                startInfo.ArgumentList[index + 1].StartsWith("-", StringComparison.Ordinal))
                return $"the description does not accept a value for '{commandName} {argument}'.";
            index++;
        }

        var requiredPositionals = command.Positionals.Count(positional =>
            !positional.EndsWith("?", StringComparison.Ordinal));
        return positionalCount < requiredPositionals || positionalCount > command.Positionals.Length
            ? $"the description declares the wrong positional shape for '{commandName}'."
            : null;
    }
}

/// <summary>What <c>interfaces.whyNot</c> declares.</summary>
/// <param name="Schema">The report schema id, or null when the key is missing.</param>
/// <param name="Ids">The id promise, or null when the key is missing.</param>
/// <param name="Kinds">The failure kinds the parser answers; every other kind comes back unknown.</param>
/// <param name="SuiteRows">Whether the parser also runs the shared suite's why-not rows.</param>
internal sealed record PanGlossWhyNotCapability(string? Schema, string? Ids, ImmutableArray<string> Kinds, bool SuiteRows, bool ExpectGuidChoices = false);

/// <summary>What <c>interfaces.serveOps.match</c> declares.</summary>
/// <param name="Version">The op's own version, or null when the key is missing.</param>
/// <param name="Supported">The constructs <c>constructs.supported</c> lists, such as <c>environment</c>, <c>featureBundle</c> and <c>alphaVariable</c>.</param>
internal sealed record PanGlossServeMatchCapability(int? Version, ImmutableHashSet<string>? SupportedConstructs = null)
{
    /// <summary>The constructs the op declares supported; empty when the description lists none.</summary>
    internal ImmutableHashSet<string> Supported => SupportedConstructs ?? [];
}

/// <summary>What <c>interfaces.serveOps.inventory</c> declares.</summary>
/// <param name="Version">The op's own version, or null when the key is missing.</param>
internal sealed record PanGlossServeInventoryCapability(int? Version);

/// <summary>What <c>interfaces.serveOps.generate</c> declares.</summary>
/// <param name="Version">The op's own version, or null when the key is missing.</param>
/// <param name="Trace">Whether the op can return each surface's rule steps.</param>
/// <param name="RequestSchema">The <c>generate --request</c> schema version the op reads, or null.</param>
internal sealed record PanGlossServeGenerateCapability(int? Version, bool Trace, int? RequestSchema);

internal sealed record PanGlossCommandCapabilities(
    bool Hidden, ImmutableArray<string> Positionals, ImmutableDictionary<string, bool> Flags);

internal static class PanGlossSurface
{
    /// <summary>The parser surface probe's timeout when callers do not supply a cap.</summary>
    internal const int DefaultDescriptionCapSeconds = 15;

    internal static async Task<PanGlossSurfaceCheck> CheckAsync(
        string executable, PanGlossContainmentJob containment, CancellationToken cancellationToken,
        TimeSpan? descriptionCap = null)
    {
        var cap = descriptionCap ?? TimeSpan.FromSeconds(DefaultDescriptionCapSeconds);
        var outcome = await PanGlossProbe.RunAsync(executable, "--describe", containment, cap, cancellationToken)
            .ConfigureAwait(false);
        if (PanGlossProbe.FatalFailure(outcome) is { } failure)
            return Invalid(executable, failure.Message) with { Failure = failure };
        if (outcome is not PanGlossOutcome.Completed completed)
            return Invalid(executable, outcome.Message);
        var standardOutput = completed.Output;
        try
        {
            using var description = JsonDocument.Parse(standardOutput);
            var capabilities = Validate(description.RootElement, out var detail);
            return capabilities is not null
                ? new PanGlossSurfaceCheck(true, string.Empty, capabilities)
                : Invalid(executable, detail!);
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidOperationException or ArgumentException or KeyNotFoundException or
            FormatException or OverflowException or NotSupportedException)
        {
            return Invalid(executable, "--describe returned invalid JSON: " + exception.Message);
        }
    }

    internal static PanGlossSurfaceCheck CheckRequest(
        string executable, PanGlossRequest request, PanGlossSurfaceCheck surface)
    {
        if (!surface.IsValid) return surface;
        if (surface.Capabilities is null)
            return Invalid(executable, "the description has no command capabilities.");
        var detail = surface.Capabilities.ValidateRequest(request);
        return detail is null ? surface : Invalid(executable, detail);
    }

    private static PanGlossCapabilities? Validate(JsonElement root, out string? detail)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return InvalidDescription("the description is not a JSON object.", out detail);
        if (!root.TryGetProperty("schema_version", out var version) ||
            version.GetInt32() != PanGlossInterfaceVersions.DescribeSchemaVersion)
            return InvalidDescription(
                $"the description does not declare schema version {PanGlossInterfaceVersions.DescribeSchemaVersion}.", out detail);
        if (!root.TryGetProperty("binary", out var binary) || binary.GetString() != "pangloss")
            return InvalidDescription("the description does not identify pangloss.", out detail);
        if (!root.TryGetProperty("commands", out var commands) || commands.ValueKind != JsonValueKind.Array)
            return InvalidDescription("the description has no command list.", out detail);

        var byName = ImmutableDictionary.CreateBuilder<string, PanGlossCommandCapabilities>(StringComparer.Ordinal);
        foreach (var command in commands.EnumerateArray())
        {
            if (command.ValueKind != JsonValueKind.Object ||
                !command.TryGetProperty("name", out var nameElement) ||
                nameElement.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(nameElement.GetString()))
                return InvalidDescription("the description contains a command without a name.", out detail);
            var name = nameElement.GetString()!;
            if (!command.TryGetProperty("positionals", out var positionals) ||
                positionals.ValueKind != JsonValueKind.Array ||
                !command.TryGetProperty("flags", out var flags) || flags.ValueKind != JsonValueKind.Array)
                return InvalidDescription($"the description is incomplete for '{name}'.", out detail);
            var hidden = false;
            if (command.TryGetProperty("hidden", out var hiddenElement))
            {
                if (hiddenElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    return InvalidDescription($"the description has an invalid hidden value for '{name}'.", out detail);
                hidden = hiddenElement.GetBoolean();
            }
            var positionalNames = ImmutableArray.CreateBuilder<string>();
            foreach (var positional in positionals.EnumerateArray())
            {
                if (positional.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(positional.GetString()))
                    return InvalidDescription($"the description has an invalid positional for '{name}'.", out detail);
                positionalNames.Add(positional.GetString()!);
            }
            var declaredFlags = ImmutableDictionary.CreateBuilder<string, bool>(StringComparer.Ordinal);
            foreach (var flag in flags.EnumerateArray())
            {
                if (flag.ValueKind != JsonValueKind.Object ||
                    !flag.TryGetProperty("name", out var flagNameElement) ||
                    flagNameElement.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(flagNameElement.GetString()) ||
                    !flag.TryGetProperty("takes_value", out var takesValueElement) ||
                    takesValueElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    return InvalidDescription($"the description has an invalid flag for '{name}'.", out detail);
                declaredFlags.Add(flagNameElement.GetString()!, takesValueElement.GetBoolean());
            }
            byName.Add(name, new PanGlossCommandCapabilities(
                hidden, positionalNames.ToImmutable(), declaredFlags.ToImmutable()));
        }

        detail = null;
        int? serveProtocol = root.TryGetProperty("interfaces", out var interfaces) &&
            interfaces.ValueKind == JsonValueKind.Object && interfaces.TryGetProperty("serveProtocol", out var serveElement) &&
            serveElement.ValueKind == JsonValueKind.Number && serveElement.TryGetInt32(out var served) ? served : null;
        int? batchTsv = root.TryGetProperty("interfaces", out interfaces) &&
            interfaces.ValueKind == JsonValueKind.Object && interfaces.TryGetProperty("batchTsv", out var batchElement) &&
            batchElement.ValueKind == JsonValueKind.Number && batchElement.TryGetInt32(out var batchVersion) ? batchVersion : null;
        return new PanGlossCapabilities(byName.ToImmutable(), HasFactsIdentity(root), serveProtocol, batchTsv,
            ReadWhyNot(root), ReadServeGenerate(root), ReadServeMatch(root), ReadServeInventory(root));
    }

    private static PanGlossServeMatchCapability? ReadServeMatch(JsonElement root)
    {
        if (!root.TryGetProperty("interfaces", out var interfaces) || interfaces.ValueKind != JsonValueKind.Object ||
            !interfaces.TryGetProperty("serveOps", out var ops) || ops.ValueKind != JsonValueKind.Object ||
            !ops.TryGetProperty("match", out var match) || match.ValueKind != JsonValueKind.Object)
            return null;
        var supported = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        if (match.TryGetProperty("constructs", out var constructs) && constructs.ValueKind == JsonValueKind.Object &&
            constructs.TryGetProperty("supported", out var list) && list.ValueKind == JsonValueKind.Array)
            foreach (var item in list.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String) supported.Add(item.GetString()!);
        return new PanGlossServeMatchCapability(match.TryGetProperty("version", out var version) &&
            version.ValueKind == JsonValueKind.Number && version.TryGetInt32(out var number) ? number : null, supported.ToImmutable());
    }

    private static PanGlossServeInventoryCapability? ReadServeInventory(JsonElement root)
    {
        if (!root.TryGetProperty("interfaces", out var interfaces) || interfaces.ValueKind != JsonValueKind.Object ||
            !interfaces.TryGetProperty("serveOps", out var ops) || ops.ValueKind != JsonValueKind.Object ||
            !ops.TryGetProperty("inventory", out var inventory) || inventory.ValueKind != JsonValueKind.Object)
            return null;
        return new PanGlossServeInventoryCapability(inventory.TryGetProperty("version", out var version) &&
            version.ValueKind == JsonValueKind.Number && version.TryGetInt32(out var number) ? number : null);
    }

    private static PanGlossServeGenerateCapability? ReadServeGenerate(JsonElement root)
    {
        if (!root.TryGetProperty("interfaces", out var interfaces) || interfaces.ValueKind != JsonValueKind.Object ||
            !interfaces.TryGetProperty("serveOps", out var ops) || ops.ValueKind != JsonValueKind.Object ||
            !ops.TryGetProperty("generate", out var generate) || generate.ValueKind != JsonValueKind.Object)
            return null;
        int? Number(string name) =>
            generate.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt32(out var number) ? number : null;
        return new PanGlossServeGenerateCapability(Number("version"),
            generate.TryGetProperty("trace", out var trace) && trace.ValueKind == JsonValueKind.True, Number("requestSchema"));
    }

    private static PanGlossWhyNotCapability? ReadWhyNot(JsonElement root)
    {
        if (!root.TryGetProperty("interfaces", out var interfaces) || interfaces.ValueKind != JsonValueKind.Object ||
            !interfaces.TryGetProperty("whyNot", out var whyNot) || whyNot.ValueKind != JsonValueKind.Object)
            return null;
        string? Text(string name) =>
            whyNot.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        var kinds = ImmutableArray.CreateBuilder<string>();
        if (whyNot.TryGetProperty("kinds", out var list) && list.ValueKind == JsonValueKind.Array)
            foreach (var kind in list.EnumerateArray())
                if (kind.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(kind.GetString()))
                    kinds.Add(kind.GetString()!);
        var suite = whyNot.TryGetProperty("suiteRows", out var rows) && rows.ValueKind == JsonValueKind.True;
        var guidChoices = whyNot.TryGetProperty("expectGuidChoices", out var flag) && flag.ValueKind == JsonValueKind.True;
        return new PanGlossWhyNotCapability(Text("schema"), Text("ids"), kinds.ToImmutable(), suite, guidChoices);
    }

    /// <summary>Reads one <c>--describe</c> document; the seam tests drive with a recorded description.</summary>
    internal static PanGlossCapabilities? ReadDescription(string json, out string? detail)
    {
        using var description = JsonDocument.Parse(json);
        return Validate(description.RootElement, out detail);
    }

    private static bool HasFactsIdentity(JsonElement root)
    {
        if (!root.TryGetProperty("facts", out var facts) || facts.ValueKind != JsonValueKind.Object ||
            !facts.TryGetProperty("format", out var format) || format.ValueKind != JsonValueKind.String ||
            !facts.TryGetProperty("schemaVersion", out var schemaVersion) ||
            schemaVersion.ValueKind != JsonValueKind.Number ||
            !facts.TryGetProperty("applicationId", out var applicationId) ||
            applicationId.ValueKind != JsonValueKind.Number ||
            !facts.TryGetProperty("contextFormat", out var contextFormat) ||
            contextFormat.ValueKind != JsonValueKind.String ||
            !facts.TryGetProperty("contextVersion", out var contextVersion) ||
            contextVersion.ValueKind != JsonValueKind.Number)
            return false;

        return format.GetString() == GrammarFactsReader.Format &&
            schemaVersion.TryGetInt32(out var schema) && schema == GrammarFactsReader.SchemaVersion &&
            applicationId.TryGetInt64(out var appId) && appId == GrammarFactsReader.ApplicationId &&
            contextFormat.GetString() == "pangloss-facts-context" &&
            contextVersion.TryGetInt32(out var context) && context == 1;
    }

    private static PanGlossCapabilities? InvalidDescription(string message, out string? detail)
    {
        detail = message;
        return null;
    }

    private static PanGlossSurfaceCheck Invalid(string executable, string detail) =>
        new(false, $"The parser executable '{executable}' does not provide Motif's expected --describe surface: {detail}");
}

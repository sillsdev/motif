using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace SIL.Motif.Host.PanGloss;

internal sealed record PanGlossSurfaceCheck(
    bool IsValid, string Message, PanGlossCapabilities? Capabilities = null);

internal sealed record PanGlossCapabilities(
    ImmutableDictionary<string, PanGlossCommandCapabilities> Commands)
{
    internal string? ValidateRequest(PanGlossRequest request)
    {
        var startInfo = new ProcessStartInfo();
        request.AddArguments(startInfo, "scratch");
        if (startInfo.ArgumentList.Count == 0 ||
            !string.Equals(startInfo.ArgumentList[0], request.Subcommand, StringComparison.Ordinal))
            return $"the request does not emit its declared '{request.Subcommand}' command.";

        var commandName = startInfo.ArgumentList[0];
        if (!Commands.TryGetValue(commandName, out var command))
            return $"the description does not expose '{commandName}'.";
        if (command.Hidden) return $"the description hides '{commandName}'.";

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
        cancellationToken.ThrowIfCancellationRequested();
        var cap = descriptionCap ?? TimeSpan.FromSeconds(DefaultDescriptionCapSeconds);
        var startInfo = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        PanGlossProcessEnvironment.Configure(startInfo);
        startInfo.ArgumentList.Add("--describe");

        PanGlossChildProcess? process;
        try
        {
            process = containment.Start(startInfo);
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException or ArgumentException or UnauthorizedAccessException)
        {
            return Invalid(executable, $"could not start --describe: {exception.Message}");
        }
        if (process is null) return Invalid(executable, "could not start --describe.");

        using (process)
        {
            var output = process.ReadStandardOutputAsync();
            var error = process.ReadStandardErrorAsync();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(cap);
            try
            {
                await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
                await process.WaitForContainmentAsync(deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                containment.Terminate(process);
                using var stopped = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try
                {
                    await process.WaitForExitAsync(stopped.Token).ConfigureAwait(false);
                    await process.WaitForContainmentAsync(stopped.Token).ConfigureAwait(false);
                    await Task.WhenAll(output, error).WaitAsync(stopped.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                if (cancellationToken.IsCancellationRequested)
                    throw new OperationCanceledException(cancellationToken);
                var seconds = cap.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
                var unit = cap == TimeSpan.FromSeconds(1) ? "second" : "seconds";
                return Invalid(executable, $"--describe did not finish within {seconds} {unit}.");
            }

            var standardOutput = await output.ConfigureAwait(false);
            var standardError = await error.ConfigureAwait(false);
            if (process.ExitCode != 0)
                return Invalid(executable, $"--describe exited {process.ExitCode}: {standardError.Trim()}");

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
        if (!root.TryGetProperty("schema_version", out var version) || version.GetInt32() != 1)
            return InvalidDescription("the description does not declare schema version 1.", out detail);
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
        return new PanGlossCapabilities(byName.ToImmutable());
    }

    private static PanGlossCapabilities? InvalidDescription(string message, out string? detail)
    {
        detail = message;
        return null;
    }

    private static PanGlossSurfaceCheck Invalid(string executable, string detail) =>
        new(false, $"The parser executable '{executable}' does not provide Motif's expected --describe surface: {detail}");
}

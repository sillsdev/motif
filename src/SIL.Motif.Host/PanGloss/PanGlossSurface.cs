using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace SIL.Motif.Host.PanGloss;

internal sealed record PanGlossSurfaceCheck(bool IsValid, string Message);

internal static class PanGlossSurface
{
    private static readonly TimeSpan DescriptionCap = TimeSpan.FromSeconds(15);

    internal static async Task<PanGlossSurfaceCheck> CheckAsync(
        string executable, Action<Process> contain, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        PanGlossProcessEnvironment.Configure(startInfo);
        startInfo.ArgumentList.Add("--describe");

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException or ArgumentException or UnauthorizedAccessException)
        {
            return Invalid(executable, $"could not start --describe: {exception.Message}");
        }
        if (process is null) return Invalid(executable, "could not start --describe.");

        using (process)
        {
            contain(process);
            var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var error = process.StandardError.ReadToEndAsync(CancellationToken.None);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(DescriptionCap);
            try
            {
                await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                catch (Win32Exception) { }
                if (cancellationToken.IsCancellationRequested) throw;
                return Invalid(executable, "--describe did not finish within 15 seconds.");
            }

            var standardOutput = await output.ConfigureAwait(false);
            var standardError = await error.ConfigureAwait(false);
            if (process.ExitCode != 0)
                return Invalid(executable, $"--describe exited {process.ExitCode}: {standardError.Trim()}");

            try
            {
                using var description = JsonDocument.Parse(standardOutput);
                var detail = Validate(description.RootElement);
                return detail is null
                    ? new PanGlossSurfaceCheck(true, string.Empty)
                    : Invalid(executable, detail);
            }
            catch (Exception exception) when (
                exception is JsonException or InvalidOperationException or ArgumentException or KeyNotFoundException or
                FormatException or OverflowException or NotSupportedException)
            {
                return Invalid(executable, "--describe returned invalid JSON: " + exception.Message);
            }
        }
    }

    private static string? Validate(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return "the description is not a JSON object.";
        if (!root.TryGetProperty("schema_version", out var version) || version.GetInt32() != 1)
            return "the description does not declare schema version 1.";
        if (!root.TryGetProperty("binary", out var binary) || binary.GetString() != "pangloss")
            return "the description does not identify pangloss.";
        if (!root.TryGetProperty("commands", out var commands) || commands.ValueKind != JsonValueKind.Array)
            return "the description has no command list.";

        var byName = commands.EnumerateArray()
            .Where(command => command.TryGetProperty("name", out _))
            .ToDictionary(command => command.GetProperty("name").GetString()!, StringComparer.Ordinal);
        PanGlossRequest[] requests =
        [
            new PanGlossRequest.Batch("project.fwdata", ["motifa"], TimeSpan.FromSeconds(1), "cache.sqlite"),
            new PanGlossRequest.Stats("project.fwdata", "cache.sqlite", ["--group", "word", "--format", "jsonl"]),
            new PanGlossRequest.Import("project.fwdata", "grammar.json"),
            new PanGlossRequest.GrammarHealth("project.fwdata"),
        ];
        foreach (var request in requests)
        {
            if (!byName.TryGetValue(request.Subcommand, out var command))
                return $"the description does not expose '{request.Subcommand}'.";
            if (command.TryGetProperty("hidden", out var hidden) && hidden.GetBoolean())
                return $"the description hides '{request.Subcommand}'.";
            if (!command.TryGetProperty("positionals", out var positionals) ||
                !command.TryGetProperty("flags", out var flags))
                return $"the description is incomplete for '{request.Subcommand}'.";

            var declaredFlags = flags.EnumerateArray()
                .Where(flag => flag.TryGetProperty("name", out _))
                .ToDictionary(flag => flag.GetProperty("name").GetString()!, StringComparer.Ordinal);
            var startInfo = new ProcessStartInfo();
            request.AddArguments(startInfo, "scratch");
            var positionalCount = 0;
            for (var index = 1; index < startInfo.ArgumentList.Count; index++)
            {
                var argument = startInfo.ArgumentList[index];
                if (!argument.StartsWith("--", StringComparison.Ordinal))
                {
                    positionalCount++;
                    continue;
                }
                if (!declaredFlags.TryGetValue(argument, out var flag))
                    return $"the description does not declare '{request.Subcommand} {argument}'.";
                if (!flag.GetProperty("takes_value").GetBoolean()) continue;
                if (++index >= startInfo.ArgumentList.Count || startInfo.ArgumentList[index].StartsWith("--"))
                    return $"the description does not accept a value for '{request.Subcommand} {argument}'.";
            }
            if (positionals.GetArrayLength() != positionalCount)
                return $"the description declares the wrong positional shape for '{request.Subcommand}'.";
        }
        return null;
    }

    private static PanGlossSurfaceCheck Invalid(string executable, string detail) =>
        new(false, $"The parser executable '{executable}' does not provide Motif's expected --describe surface: {detail}");
}

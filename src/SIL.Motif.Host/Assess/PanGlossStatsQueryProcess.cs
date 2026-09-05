using System.Diagnostics;
using SIL.Motif.Host.Parser;

namespace SIL.Motif.Host.Assess;

/// <summary>
/// Runs PanGloss's <c>stats</c> command and returns both streams untouched.
/// </summary>
/// <remarks>
/// <para>
/// Argument construction, draining both streams before waiting, cancellation, and process-tree kill follow
/// <see cref="PanGlossAssessmentProcess"/>'s shape, because the hazards a process boundary creates do not
/// change with which PanGloss subcommand is on the far side of it.
/// </para>
/// <para>
/// <b>What must never happen here</b> is any inspection of <c>forwardedArguments</c> beyond appending each
/// one, in order, to the argument list PanGloss receives: no split on whitespace, no reordering, no
/// deduplication, no case change. Motif's own two arguments — the grammar path and the cache path — are the
/// only ones this type contributes; PanGloss owns the rest of its vocabulary.
/// </para>
/// </remarks>
public sealed class PanGlossStatsQueryProcess : IPanGlossStatsQuery
{
    private readonly string _executable;

    /// <param name="executablePath">The parser's path; discovered via <see cref="PanGlossExecutable"/> when null.</param>
    public PanGlossStatsQueryProcess(string? executablePath = null)
    {
        _executable = executablePath ?? PanGlossExecutable.TryLocate()
            ?? throw new ParserUnavailableException(
                $"Could not find the pangloss executable. Build it with " +
                $"`cargo build --release -p pg-cli` in the PanGloss checkout, or set " +
                $"{PanGlossExecutable.PathVariable} to its path.");
    }

    /// <inheritdoc />
    public async Task<PanGlossStatsOutput> QueryAsync(string grammarPath, string cachePath,
        IReadOnlyList<string> forwardedArguments, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(grammarPath)) throw new ArgumentException("Required.", nameof(grammarPath));
        if (string.IsNullOrWhiteSpace(cachePath)) throw new ArgumentException("Required.", nameof(cachePath));
        ArgumentNullException.ThrowIfNull(forwardedArguments);

        var startInfo = new ProcessStartInfo(_executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("stats");
        startInfo.ArgumentList.Add(grammarPath);
        startInfo.ArgumentList.Add("--cache");
        startInfo.ArgumentList.Add(cachePath);
        // Verbatim, in order: this is Motif's whole contribution to the passthrough (design decision 5).
        foreach (var argument in forwardedArguments) startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new ParserUnavailableException($"Could not start '{_executable}'.");

        // Read both streams before waiting: a full pipe buffer deadlocks a process that is still writing.
        var stdErrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
        var stdOutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            throw;
        }

        var stdErr = await stdErrTask.ConfigureAwait(false);
        var stdOut = await stdOutTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new ParserUnavailableException(
                $"pangloss stats exited {process.ExitCode} for '{grammarPath}':" +
                Environment.NewLine + stdErr.Trim());
        }

        return new PanGlossStatsOutput(stdOut, stdErr);
    }
}

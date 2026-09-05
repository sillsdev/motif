using System.Diagnostics;

namespace SIL.Motif.Host.Parser;

/// <summary>
/// Runs PanGloss's <c>import</c> command against a saved <c>.fwdata</c> file and writes its grammar
/// snapshot to a caller-chosen path.
/// </summary>
/// <remarks>
/// <para>
/// This is the other half of the process seam family <see cref="PanGlossAssessmentProcess"/> belongs to:
/// argument construction, draining both streams before waiting, cancellation, and process-tree kill all
/// follow the same shape, because the hazards a process boundary creates do not change with which PanGloss
/// subcommand is on the far side of it.
/// </para>
/// <para>
/// A clean exit code is not success by itself — the same discipline
/// <see cref="PanGlossAssessmentProcess"/> applies to a report. PanGloss owns the snapshot's format
/// entirely; this type never reads or validates its content, only that it exists.
/// </para>
/// </remarks>
public sealed class PanGlossGrammarImportProcess : IPanGlossGrammarImporter
{
    private readonly string _executable;

    /// <param name="executablePath">The parser's path; discovered via <see cref="PanGlossExecutable"/> when null.</param>
    public PanGlossGrammarImportProcess(string? executablePath = null)
    {
        _executable = executablePath ?? PanGlossExecutable.TryLocate()
            ?? throw new ParserUnavailableException(
                $"Could not find the pangloss executable. Build it with " +
                $"`cargo build --release -p pg-cli` in the PanGloss checkout, or set " +
                $"{PanGlossExecutable.PathVariable} to its path.");
    }

    /// <inheritdoc />
    public async Task ImportAsync(string fwDataPath, string grammarJsonPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fwDataPath)) throw new ArgumentException("Required.", nameof(fwDataPath));
        if (string.IsNullOrWhiteSpace(grammarJsonPath))
            throw new ArgumentException("Required.", nameof(grammarJsonPath));
        if (!File.Exists(fwDataPath))
            throw new FileNotFoundException("The project file the parser must read does not exist.", fwDataPath);

        var startInfo = new ProcessStartInfo(_executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("import");
        startInfo.ArgumentList.Add(fwDataPath);
        startInfo.ArgumentList.Add(grammarJsonPath);

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
        _ = await stdOutTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new ParserUnavailableException(
                $"pangloss import exited {process.ExitCode} for '{fwDataPath}':" +
                Environment.NewLine + stdErr.Trim());
        }

        if (!File.Exists(grammarJsonPath))
        {
            throw new ParserUnavailableException(
                $"pangloss import reported success but wrote no grammar to '{grammarJsonPath}'.");
        }
    }
}

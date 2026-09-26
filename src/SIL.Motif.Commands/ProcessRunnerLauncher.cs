using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using WorkerRunnerOptions = SIL.Motif.Worker.RunnerOptions;

namespace SIL.Motif.Commands;

/// <summary>Starts the sibling Worker process with explicit root and parser settings.</summary>
public sealed class ProcessRunnerLauncher : IJobRunnerLauncher
{
    public void Start(string projectPath, JobRunnerLaunchOptions options, Action<string>? reportWarning = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentNullException.ThrowIfNull(options);
        var executable = Locate();
        if (executable is null) return;

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                MakeOwnStandardHandlesNonInheritable();
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add(WorkerRunnerOptions.RootArgument);
            start.ArgumentList.Add(options.Root);
            if (options.ParserPath is { } parserPath)
            {
                start.ArgumentList.Add(WorkerRunnerOptions.ParserArgument);
                start.ArgumentList.Add(parserPath);
            }
            else
            {
                start.ArgumentList.Add(WorkerRunnerOptions.NoParserArgument);
            }
            Process.Start(start);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            reportWarning?.Invoke("warning: could not start the background runner (" + exception.Message +
                "). Queued work will run once one is started.");
        }
    }

    private const int StdInputHandle = -10;
    private const int StdOutputHandle = -11;
    private const int StdErrorHandle = -12;
    private const uint HandleFlagInherit = 1;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetHandleInformation(IntPtr hObject, uint dwMask, uint dwFlags);

    private static string? Locate()
    {
        var fileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "SIL.Motif.Worker.exe"
            : "SIL.Motif.Worker";
        var sibling = Path.Combine(AppContext.BaseDirectory, fileName);
        return File.Exists(sibling) ? sibling : null;
    }

    private static void MakeOwnStandardHandlesNonInheritable()
    {
        foreach (var which in new[] { StdInputHandle, StdOutputHandle, StdErrorHandle })
        {
            var handle = GetStdHandle(which);
            if (handle != IntPtr.Zero && handle != new IntPtr(-1))
                SetHandleInformation(handle, HandleFlagInherit, 0);
        }
    }
}

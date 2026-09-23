using System.Diagnostics;
using System.Reflection;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Handoff;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Handoff;

/// <summary>
/// Pins <c>parse_grammar_texts_assessment.py</c> as an embedded resource and its <c>--help</c> output as a
/// teaching surface (ADR 0045's own description): a reader who runs nothing but <c>--help</c> must still
/// learn what the three JSON files are and that <c>handoff.md</c> is the file that introduces all of them.
/// Round-trip coverage against a real, writer-produced Handoff lives in
/// <see cref="HandoffWriterTests.ThePythonHelperReadsBackTheWordAndTextRecordsTheWriterActuallyWrote"/>.
/// </summary>
public sealed class HandoffPythonHelperTests : IDisposable
{
    private const string PythonHelperResource =
        "SIL.Motif.Commands.Handoff.Assets.parse_grammar_texts_assessment.py";

    private static readonly Assembly CommandsAssembly = typeof(CommandCatalog).Assembly;

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "SIL.Motif.HandoffPythonHelperTests", Guid.NewGuid().ToString("N"));

    public HandoffPythonHelperTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void TheReaderScriptIsEmbeddedInCommands()
    {
        Assert.Contains(PythonHelperResource, CommandsAssembly.GetManifestResourceNames());
    }

    [RequiresPythonFact]
    public void TheReaderScriptsHelpNamesTheOtherFourHandoffFilesAndEveryCommand()
    {
        var scriptPath = WriteScriptToDisk();

        var help = RunHelp(scriptPath);

        Assert.Contains("grammar.json", help, StringComparison.Ordinal);
        Assert.Contains("texts.json", help, StringComparison.Ordinal);
        Assert.Contains("assessment.json", help, StringComparison.Ordinal);
        Assert.Contains("handoff.md", help, StringComparison.Ordinal);

        // Every subcommand this file actually implements, so --help cannot fall behind the code.
        Assert.Contains("outcome", help, StringComparison.Ordinal);
        Assert.Contains("word", help, StringComparison.Ordinal);
        Assert.Contains("slowest", help, StringComparison.Ordinal);
        Assert.Contains("trace", help, StringComparison.Ordinal);
        Assert.Contains("text", help, StringComparison.Ordinal);
        Assert.Contains("words", help, StringComparison.Ordinal);
        Assert.Contains("grammar", help, StringComparison.Ordinal);
    }

    [RequiresPythonFact]
    public void TheReaderScriptCompilesCleanly()
    {
        var scriptPath = WriteScriptToDisk();
        var startInfo = new ProcessStartInfo(PythonExecutable.Path!)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("-m");
        startInfo.ArgumentList.Add("py_compile");
        startInfo.ArgumentList.Add(scriptPath);

        using var process = Process.Start(startInfo)!;
        var stderr = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(15000), "python -m py_compile did not exit within 15 seconds.");
        Assert.True(process.ExitCode == 0, $"py_compile failed: {stderr}");
    }

    private string WriteScriptToDisk()
    {
        var scriptPath = Path.Combine(_root, "parse_grammar_texts_assessment.py");
        File.WriteAllText(scriptPath, ReadEmbeddedText(PythonHelperResource));
        return scriptPath;
    }

    private static string RunHelp(string scriptPath)
    {
        var startInfo = new ProcessStartInfo(PythonExecutable.Path!)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("--help");

        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(15000), "python --help did not exit within 15 seconds.");
        Assert.True(process.ExitCode == 0, $"python --help exited {process.ExitCode}: {stderr}");
        return stdout;
    }

    private static string ReadEmbeddedText(string resourceName)
    {
        using var stream = CommandsAssembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

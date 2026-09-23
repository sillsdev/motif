using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.Store;
using SIL.Motif.Projection.Usage;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class CorpusCommandDispatchTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "motif-corpus-dispatch-tests", Guid.NewGuid().ToString("N"));
    private readonly string _fwDataPath;
    private readonly string _workerRoot;

    public CorpusCommandDispatchTests()
    {
        Directory.CreateDirectory(_root);
        _fwDataPath = Path.Combine(_root, "Project.fwdata");
        File.WriteAllText(_fwDataPath, string.Empty);
        _workerRoot = Path.Combine(_root, "worker-root");
    }

    private CommandOutcome<CorpusAddedResponse> AddCorpus(
        string fwDataPath, string productVersion, string corpusId, string description, string? uri, string? licence,
        LicenceCapabilities capabilities, string tokeniser, string tokeniserVersion, string? tokeniserNotes) =>
        CorpusCommands.AddCorpus(new AddCorpusRequest(
            fwDataPath, productVersion, corpusId, description, uri, licence, capabilities, tokeniser,
            tokeniserVersion, tokeniserNotes));

    private CommandOutcome<CorpusDocumentAddedResponse> AddDocument(
        string fwDataPath, string productVersion, string corpusId, string documentId, string fileOrUrl, string? title,
        string? licence, LicenceCapabilities? capabilities) =>
        CorpusCommands.AddDocument(new AddDocumentRequest(
            fwDataPath, productVersion, corpusId, documentId, fileOrUrl, title, licence, capabilities));
    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void CorporaJsonFlagDispatchesToStructuredOutputAndRecordsUsage()
    {
        var (exitCode, output, error) = Run("corpora");

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error);
        Assert.Equal(
            "{" + Environment.NewLine + "  \"corpora\": []" + Environment.NewLine + "}" + Environment.NewLine,
            output);
        var entry = Assert.Single(ReadUsage());
        Assert.Equal("corpora", entry.Command);
        Assert.Equal(new[] { "fwDataPath:text" }, entry.ArgumentShape);
    }

    [Fact]
    public void ShowCorpusJsonFlagDispatchesToStructuredOutputAndRecordsOnlyArgumentShape()
    {
        const string corpusId = "dispatch-corpus";
        const string description = "Private dispatch corpus";
        var add = AddCorpus(
            _fwDataPath, "1.0", corpusId, description, "https://private.test/corpus", "private-licence",
            LicenceCapabilities.Unknown(), "test-tokeniser", "1", "private notes");
        Assert.True(add.Succeeded);
        var sourcePath = Path.Combine(_root, "private-source.txt");
        File.WriteAllText(sourcePath, "private document text");
        Assert.True(
            AddDocument(
                _fwDataPath, "1.0", corpusId, "private-document", sourcePath, "Private document", null, null)
                .Succeeded);

        var (exitCode, output, error) = Run($"show-corpus {corpusId}");

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error);
        Assert.Contains($"\"corpusId\": \"{corpusId}\"", output);
        Assert.Contains($"\"description\": \"{description}\"", output);
        Assert.DoesNotContain("private document text", output);

        var entry = Assert.Single(ReadUsage());
        Assert.Equal("show-corpus", entry.Command);
        Assert.Equal(new[] { "fwDataPath:text", "corpusId:text" }, entry.ArgumentShape);
    }

    /// <summary>Reads back what the spawned CLI recorded into this test's isolated machine store.</summary>
    private IReadOnlyList<UsageLogEntry> ReadUsage()
    {
        using var machine = MachineDatabase.Open(_workerRoot);
        return new MachineUsageLog(machine).ReadAll();
    }

    private (int ExitCode, string Output, string Error) Run(string command)
    {
        var executable = BuildOutput.Cli;
        var start = new ProcessStartInfo(executable)
        {
            Arguments = $"{command} --project \"{_fwDataPath}\" --json",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.Environment[RunnerOptions.RootVariable] = _workerRoot;

        using var process = Process.Start(start)!;
        // Both pipes drain concurrently: a sequential read deadlocks past the pipe buffer.
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        var output = outputTask.GetAwaiter().GetResult();
        var error = errorTask.GetAwaiter().GetResult();
        process.WaitForExit();
        return (process.ExitCode, output, error);
    }
}

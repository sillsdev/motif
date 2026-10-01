using System;
using System.IO;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class ProjectStoreOperationFactsTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "motif-project-store-operation-facts-" + Guid.NewGuid().ToString("N"));

    public ProjectStoreOperationFactsTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void AnnotatedIOExceptionKeepsOperationIoRefusalAndAddsItsDiagnostics()
    {
        const int expectedHResult = unchecked((int)0x80070020);
        var exception = new IOException("controlled publication failure", expectedHResult);
        exception.Data["baselinePublicationPhase"] = "archive-extraction";
        var projectPath = CreateProject("io");

        var outcome = ProjectStoreCommand.Run<string>(
            projectPath, "1.0", (_, _) => throw exception);

        Assert.False(outcome.Succeeded);
        var refusal = outcome.Refusal!;
        Assert.Equal("project.operation-io", refusal.Code);
        Assert.Equal(FailureReason.Refused, refusal.Reason);
        Assert.Equal(exception.Message, refusal.Message);
        Assert.Equal(projectPath, refusal.Facts["fwDataPath"]);
        Assert.Equal("archive-extraction", refusal.Facts["baselinePublicationPhase"]);
        Assert.Equal("0x80070020", refusal.Facts["exceptionHResult"]);
        Assert.Equal(typeof(IOException).FullName, refusal.Facts["exceptionType"]);
        Assert.Equal(4, refusal.Facts.Count);
    }

    [Fact]
    public void AnnotatedUnauthorizedAccessKeepsOperationIoRefusalAndAddsItsDiagnostics()
    {
        const int expectedHResult = unchecked((int)0x80070005);
        var exception = new UnauthorizedAccessException("controlled access failure");
        Assert.Equal(expectedHResult, exception.HResult);
        exception.Data["baselinePublicationPhase"] = "incoming-reclamation";
        var projectPath = CreateProject("unauthorized");

        var outcome = ProjectStoreCommand.Run<string>(
            projectPath, "1.0", (_, _) => throw exception);

        Assert.False(outcome.Succeeded);
        var refusal = outcome.Refusal!;
        Assert.Equal("project.operation-io", refusal.Code);
        Assert.Equal(FailureReason.Refused, refusal.Reason);
        Assert.Equal(exception.Message, refusal.Message);
        Assert.Equal(projectPath, refusal.Facts["fwDataPath"]);
        Assert.Equal("incoming-reclamation", refusal.Facts["baselinePublicationPhase"]);
        Assert.Equal("0x80070005", refusal.Facts["exceptionHResult"]);
        Assert.Equal(typeof(UnauthorizedAccessException).FullName, refusal.Facts["exceptionType"]);
        Assert.Equal(4, refusal.Facts.Count);
    }

    [Fact]
    public void UnannotatedIOExceptionRetainsTheOriginalOperationIoFacts()
    {
        var exception = new IOException("ordinary operation failure");
        var projectPath = CreateProject("unannotated");

        var outcome = ProjectStoreCommand.Run<string>(
            projectPath, "1.0", (_, _) => throw exception);

        Assert.False(outcome.Succeeded);
        var refusal = outcome.Refusal!;
        Assert.Equal("project.operation-io", refusal.Code);
        Assert.Equal(FailureReason.Refused, refusal.Reason);
        Assert.Equal(exception.Message, refusal.Message);
        Assert.Single(refusal.Facts);
        Assert.Equal(projectPath, refusal.Facts["fwDataPath"]);
    }

    private string CreateProject(string name)
    {
        var path = Path.Combine(_root, name + ".fwdata");
        File.WriteAllText(path, "private test project");
        return path;
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
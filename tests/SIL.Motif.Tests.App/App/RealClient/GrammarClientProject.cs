using SIL.Motif.App.Services;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.RealClient;

internal sealed class GrammarClientProject : IDisposable
{
    private readonly WalkthroughProject _project;

    private GrammarClientProject(WalkthroughProject project, string parserPath)
    {
        _project = project;
        ParserPath = parserPath;
        Client = RealCommandClient.Create(project.ManagedRoot, parserPath);
    }

    public string FwDataPath => _project.FwDataPath;

    public string ManagedRoot => _project.ManagedRoot;

    public string ParserPath { get; }

    public CommandClient Client { get; }

    public static async Task<GrammarClientProject> OpenAsync(PristineProjectFixture pristine)
    {
        var project = new WalkthroughProject(pristine);
        try
        {
            var parserPath = FakeParser.CopyRecordingInvocations(project.ManagedRoot);
            var result = new GrammarClientProject(project, parserPath);
            var captured = await result.Client.CaptureBaselineAsync(
                new BaselineCaptureRequest(project.FwDataPath), CancellationToken.None);
            Assert.True(captured.Succeeded, captured.Refusal?.Message);
            return result;
        }
        catch
        {
            project.Dispose();
            throw;
        }
    }

    public void Behave(object behavior) => FakeParser.BehaveBesideExecutable(ParserPath, behavior);

    public IReadOnlyList<string> Invocations() => FakeParser.Invocations(ParserPath);

    public void Dispose() => _project.Dispose();
}

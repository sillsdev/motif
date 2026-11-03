using SIL.Motif.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group3)]
public sealed class ProjectWordFormingCommandTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-word-forming-" + Guid.NewGuid().ToString("N"));

    public ProjectWordFormingCommandTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void DeclaringTheHyphenAndDigitsKeepsAnOrdinalWrittenWithANumeralInOneToken()
    {
        var created = ProjectCreateCommand.Create(new ProjectCreateRequest(_root, "Cebuano", "ceb", "Cebuano", "'", null));
        Assert.True(created.Succeeded, created.Refusal?.Message);
        var path = created.Value!.FwDataPath;
        Assert.Equal(["ika", "ay"], Tokenise(path, "ika-14 ay"));

        var added = ProjectWordFormingCommand.Add(new ProjectWordFormingRequest(path, "-0123456789'"));
        Assert.True(added.Succeeded, added.Refusal?.Message);
        Assert.Equal(["-", "0", "1", "2", "3", "4", "5", "6", "7", "8", "9"], added.Value!.Added);
        Assert.Equal(12, added.Value.WordFormingCharacters.Count);
        Assert.Equal(["ika-14", "ay"], Tokenise(path, "ika-14 ay"));

        var again = ProjectWordFormingCommand.Add(new ProjectWordFormingRequest(path, "14"));
        Assert.True(again.Succeeded, again.Refusal?.Message);
        Assert.Empty(again.Value!.Added);
    }

    [Theory]
    [InlineData("", "project.invalid")]
    [InlineData("1 2", "project-create.invalid-word-forming")]
    public void AnEmptyOrNonVisibleCharacterIsRefused(string characters, string code)
    {
        var created = ProjectCreateCommand.Create(new ProjectCreateRequest(_root, "Refused", "ceb", "Cebuano", null, null));
        Assert.True(created.Succeeded, created.Refusal?.Message);
        var refused = ProjectWordFormingCommand.Add(new ProjectWordFormingRequest(created.Value!.FwDataPath, characters));
        Assert.Equal(code, refused.Refusal?.Code);
    }

    [Fact]
    public void AProjectFieldWorksHasOpenIsRefusedAsInUseAndLeftUnchanged()
    {
        var created = ProjectCreateCommand.Create(new ProjectCreateRequest(_root, "Held", "ceb", "Cebuano", null, null));
        Assert.True(created.Succeeded, created.Refusal?.Message);
        var path = created.Value!.FwDataPath;
        using var held = new FileStream(path + ".lock", FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        var refused = ProjectWordFormingCommand.Add(new ProjectWordFormingRequest(path, "0123"));

        Assert.Equal("project.in-use", refused.Refusal?.Code);
        held.Dispose();
        Assert.Equal(["ika", "ay"], Tokenise(path, "ika-14 ay"));
    }

    [Fact]
    public void AMissingProjectIsRefused()
    {
        var refused = ProjectWordFormingCommand.Add(new ProjectWordFormingRequest(Path.Combine(_root, "none.fwdata"), "1"));
        Assert.Equal("project.not-found", refused.Refusal?.Code);
    }

    private static string[] Tokenise(string path, string text)
    {
        using var cache = new FwDataProjectLoader().LoadScratchCache(path);
        return new WritingSystemWordTokeniser(cache.ServiceLocator.WritingSystems.DefaultVernacularWritingSystem)
            .Tokenise(text).ToArray();
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

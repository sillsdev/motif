using System.Security.Cryptography;

namespace SIL.Motif.Tests.TestFixtures;

public sealed class ConformanceProject : IDisposable
{
    private const string FixtureDirectoryName = "deep-optional-affix-nesting";
    private const string ProjectDirectoryName = "DeepOptionalAffixNesting";

    private readonly string _projectRoot;

    public ConformanceProject()
    {
        _projectRoot = Path.Combine(
            Path.GetTempPath(), "SIL.Motif.Conformance", Guid.NewGuid().ToString("N"));
        ManagedRoot = Path.Combine(
            Path.GetTempPath(), "SIL.Motif.Conformance.Managed", Guid.NewGuid().ToString("N"));

        try
        {
            var projectDirectory = Path.Combine(_projectRoot, ProjectDirectoryName);
            var fixtureDirectory = Path.Combine(
                AppContext.BaseDirectory, "TestFixtures", "Conformance", FixtureDirectoryName);
            CopyFixtureDirectory(fixtureDirectory, projectDirectory);
            var copiedFixtureProject = Path.Combine(projectDirectory, "project.fwdata");
            FwDataPath = Path.Combine(projectDirectory, ProjectDirectoryName + ".fwdata");
            File.Move(copiedFixtureProject, FwDataPath);
            Directory.CreateDirectory(ManagedRoot);
            SourceSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(FwDataPath)));
        }
        catch
        {
            DeleteDirectory(_projectRoot);
            DeleteDirectory(ManagedRoot);
            throw;
        }
    }

    public const string OneAnalysisShort = "k";

    public const string OneAnalysisLong = "xxxxxxxxxxxxk";

    public const string NineHundredTwentyFour = "xxxxxxk";

    public static readonly IReadOnlyList<string> SlowWords = ["xxxxk", "xxxxxk", "xxxxxxk", "xxxxxxxk", "xxxxxxxxk"];

    public string FwDataPath { get; }

    public string ManagedRoot { get; }

    public string SourceSha256 { get; }

    public void Dispose()
    {
        DeleteDirectory(_projectRoot);
        DeleteDirectory(ManagedRoot);
    }

    private static void CopyFixtureDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var sourceFile in Directory.EnumerateFiles(sourceDirectory))
            File.Copy(sourceFile, Path.Combine(destinationDirectory, Path.GetFileName(sourceFile)));

        foreach (var sourceSubdirectory in Directory.EnumerateDirectories(sourceDirectory))
            CopyFixtureDirectory(
                sourceSubdirectory, Path.Combine(destinationDirectory, Path.GetFileName(sourceSubdirectory)));
    }

    private static void DeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

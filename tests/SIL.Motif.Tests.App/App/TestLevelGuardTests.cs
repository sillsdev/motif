using System.Collections;
using System.Reflection;
using System.Xml.Linq;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.App;

[CollectionDefinition("Test level guard", DisableParallelization = true)]
public sealed class TestLevelGuardCollection
{
    public const string Name = "Test level guard";
}

[Collection(TestLevelGuardCollection.Name)]
public sealed class TestLevelGuardTests
{
    private static readonly HashSet<string> ValidLevels = ["Unit", "Integration", "System"];

    [Fact]
    public void EveryTestProjectDeclaresOneDefaultLevel()
    {
        var root = FindRepositoryRoot();
        var projects = Directory.GetFiles(Path.Combine(root, "tests"), "*.csproj", SearchOption.AllDirectories)
            .Select(path => (Path: path, Document: XDocument.Load(path)))
            .Where(project => string.Equals(
                project.Document.Descendants().FirstOrDefault(element => element.Name.LocalName == "IsTestProject")?.Value,
                "true",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.NotEmpty(projects);
        foreach (var project in projects)
        {
            var levels = project.Document.Descendants()
                .Where(element => element.Name.LocalName == "MotifTestDefaultLevel")
                .Select(element => element.Value.Trim())
                .ToArray();

            Assert.True(levels.Length == 1 && ValidLevels.Contains(levels[0]),
                $"{Path.GetRelativePath(root, project.Path)} must declare exactly one valid MotifTestDefaultLevel.");
        }
    }

    [Fact]
    public void EveryTestClassResolvesToExactlyOneLevel()
    {
        var processEnvironment = Environment.GetEnvironmentVariables();
        try
        {
            var root = FindRepositoryRoot();
            var testOutput = AppContext.BaseDirectory;
            // Loading test assemblies executes module initializers, so preserve this host's isolated environment.
            var assemblies = Directory.GetFiles(testOutput, "SIL.Motif.Tests.*.dll")
                .Where(path => !Path.GetFileNameWithoutExtension(path).EndsWith(".Support", StringComparison.Ordinal))
                .Select(Assembly.LoadFrom)
                .ToArray();

            Assert.NotEmpty(assemblies);
            foreach (var assembly in assemblies)
            {
                var defaultLevel = TestLevelClassifier.ReadAssemblyDefault(assembly);

                foreach (var testClass in assembly.GetTypes().Where(HasTests))
                {
                    var declarations = testClass.GetCustomAttributesData()
                        .Where(attribute => attribute.AttributeType == typeof(TraitAttribute))
                        .Select(attribute => attribute.ConstructorArguments)
                        .Where(arguments => arguments.Count == 2 && arguments[0].Value as string == "MotifTestLevel")
                        .Select(arguments => arguments[1].Value as string)
                        .ToArray();
                    Assert.True(declarations.Length <= 1,
                        $"{testClass.FullName} declares more than one MotifTestLevel.");

                    var parserClass = testClass.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                                           BindingFlags.Instance | BindingFlags.Static)
                        .SelectMany(method => method.GetCustomAttributes(inherit: true))
                        .Any(attribute => attribute.GetType().Name == "RealParserFactAttribute");
                    var walkthroughNamespace = testClass.Namespace?.Split('.')
                        .Any(segment => segment is "Walkthrough" or "Smoke") == true;
                    var namedSystemClass = testClass.Name is "PortableWorkerPackageTests" or
                        "ExplainedWordCardWalkthroughReplayTests" or
                        "ExternalApplyActivationRealClientTests" or
                        "RunnerSpineTests";
                    var resolvedLevel = TestLevelClassifier.ResolveLevel(
                        testClass.FullName!, defaultLevel, declarations, parserClass);
                    Assert.Contains(resolvedLevel, ValidLevels);
                    if (parserClass || walkthroughNamespace || namedSystemClass || defaultLevel == "System")
                        Assert.Equal("System", resolvedLevel);
                }
            }
        }
        finally
        {
            RestoreProcessEnvironment(processEnvironment);
        }
    }

    [Fact]
    public void DefaultSelectionIncludesUnitAndIntegrationAndAllIncludesSystem()
    {
        var defaultLevels = TestLevelClassifier.ParseSelection("Unit,Integration")!;

        Assert.Contains("Unit", defaultLevels);
        Assert.Contains("Integration", defaultLevels);
        Assert.DoesNotContain("System", defaultLevels);
        Assert.Null(TestLevelClassifier.ParseSelection("All"));
    }

    private static bool HasTests(Type type) => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                                               BindingFlags.Instance | BindingFlags.Static)
        .Any(method => method.GetCustomAttributes(inherit: true)
            .Any(attribute => attribute is FactAttribute));

    private static void RestoreProcessEnvironment(IDictionary originalEnvironment)
    {
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
            if (!originalEnvironment.Contains(entry.Key))
                Environment.SetEnvironmentVariable((string)entry.Key, null);

        foreach (DictionaryEntry entry in originalEnvironment)
            Environment.SetEnvironmentVariable((string)entry.Key, (string?)entry.Value);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Motif.sln"))) return directory.FullName;

        throw new DirectoryNotFoundException("Could not find Motif.sln above the test output directory.");
    }

}

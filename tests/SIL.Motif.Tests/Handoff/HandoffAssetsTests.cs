using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using SIL.Motif.Commands.Catalog;
using Xunit;

namespace SIL.Motif.Tests.Handoff;

/// <summary>
/// Skips at discovery when no <c>python</c> executable is on <c>PATH</c>, rather than fail a
/// suite run on a machine with no Python installed. <c>read_handoff.py</c> is standard-library
/// Python, not a Motif build output, so its absence is an ordinary developer-machine gap.
/// </summary>
public sealed class PythonAvailableFactAttribute : FactAttribute
{
    public PythonAvailableFactAttribute()
    {
        if (!PythonAvailability.IsAvailable)
            Skip = "No 'python' executable found on PATH.";
    }
}

internal static class PythonAvailability
{
    public static readonly bool IsAvailable = Probe();

    private static bool Probe()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("python", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process is null) return false;

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(10_000)) return false;
            outputTask.GetAwaiter().GetResult();
            errorTask.GetAwaiter().GetResult();
            return process.ExitCode == 0;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException)
        {
            return false;
        }
    }
}

/// <summary>
/// Pins the Handoff folder's static assets: every one is embedded in
/// <see cref="SIL.Motif.Commands"/> so <c>HandoffWriter</c> never depends on a filesystem-relative
/// path at run time, the instructions point at the public repository's real raw-GitHub URLs, no
/// asset's prose leaks a path local to whichever machine wrote it, and the standard-library reader
/// actually runs end to end wherever Python is available.
/// </summary>
public sealed class HandoffAssetsTests
{
    private static readonly Assembly CommandsAssembly = typeof(CommandCatalog).Assembly;

    private const string InstructionsResource = "SIL.Motif.Commands.Handoff.Assets.instructions.md";
    private const string RecipesResource = "SIL.Motif.Commands.Handoff.Assets.recipes.md";
    private const string ReadHandoffPyResource = "SIL.Motif.Commands.Handoff.Assets.read_handoff.py";
    private const string GrammarFormatResource = "SIL.Motif.Commands.Handoff.Reference.grammar-format.md";
    private const string FlexTextFormatResource = "SIL.Motif.Commands.Handoff.Reference.flextext-json-format.md";
    private const string HcMechanicsResource = "SIL.Motif.Commands.Handoff.Reference.hc-mechanics.md";

    private static readonly string[] ExpectedRawGitHubUrls =
    [
        "https://raw.githubusercontent.com/johnml1135/motif/main/docs/handoff/grammar-format.md",
        "https://raw.githubusercontent.com/johnml1135/motif/main/docs/handoff/flextext-json-format.md",
        "https://raw.githubusercontent.com/johnml1135/motif/main/docs/handoff/hc-mechanics.md",
        "https://raw.githubusercontent.com/johnml1135/motif/main/src/SIL.Motif.Commands/Handoff/Assets/read_handoff.py",
    ];

    private static readonly string[] AllTextAssetResources =
    [
        InstructionsResource, RecipesResource, ReadHandoffPyResource,
        GrammarFormatResource, FlexTextFormatResource, HcMechanicsResource,
    ];

    [Fact]
    public void EveryHandoffAssetIsEmbeddedInCommands()
    {
        var names = CommandsAssembly.GetManifestResourceNames();

        foreach (var expected in AllTextAssetResources)
            Assert.Contains(expected, names);
    }

    [Fact]
    public void InstructionsPointAtTheExpectedRawGitHubUrls()
    {
        var instructions = ReadEmbeddedText(InstructionsResource);

        foreach (var url in ExpectedRawGitHubUrls)
            Assert.Contains(url, instructions);
    }

    [Theory]
    [MemberData(nameof(AssetResourceCases))]
    public void NoAssetProseLeaksARepoLocalPath(string resourceName)
    {
        var text = ReadEmbeddedText(resourceName);

        // A local path only resolves on the machine that wrote it, never on whatever reads the Handoff.
        Assert.False(WindowsAbsolutePath.IsMatch(text), $"{resourceName} contains a Windows absolute path.");
        Assert.False(text.Contains("/Users/", StringComparison.Ordinal), $"{resourceName} contains a /Users/ path.");
        Assert.False(text.Contains("/home/", StringComparison.Ordinal), $"{resourceName} contains a /home/ path.");
    }

    public static IEnumerable<object[]> AssetResourceCases() =>
        AllTextAssetResources.Select(name => new object[] { name });

    // A URL scheme like "https:" also matches letter-colon-slash; exclude a colon preceded by a letter.
    private static readonly Regex WindowsAbsolutePath = new(@"(?<![A-Za-z])[A-Za-z]:[\\/]", RegexOptions.Compiled);

    /// <summary>
    /// Every module <c>read_handoff.py</c> imports ships with Python itself.
    /// </summary>
    /// <remarks>
    /// The Handoff is written for an agent that may have no network and no package installer, so a single
    /// third-party import makes the reader useless exactly where it is needed most. Merely running the
    /// script does not catch that: an import would resolve on any machine that happens to have the package.
    /// This asks Python itself, through <c>sys.stdlib_module_names</c>.
    /// </remarks>
    [PythonAvailableFact]
    public void ReadHandoffPyImportsNothingOutsideTheStandardLibrary()
    {
        var scriptPath = Path.Combine(
            Path.GetTempPath(), "SIL.Motif.HandoffAssetsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scriptPath);
        try
        {
            var readerPath = Path.Combine(scriptPath, "read_handoff.py");
            File.WriteAllText(readerPath, ReadEmbeddedText("SIL.Motif.Commands.Handoff.Assets.read_handoff.py"));
            var checkerPath = Path.Combine(scriptPath, "check_imports.py");
            File.WriteAllText(checkerPath, StdlibImportChecker);

            var result = RunReadHandoff(checkerPath, readerPath);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal("[]", result.StandardOutput.Trim());
        }
        finally
        {
            try { Directory.Delete(scriptPath, recursive: true); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    // Parses rather than imports, so a non-stdlib name is reported instead of raising ImportError.
    private const string StdlibImportChecker = """
        import ast, json, sys

        tree = ast.parse(open(sys.argv[1], encoding="utf-8").read())
        names = set()
        for node in ast.walk(tree):
            if isinstance(node, ast.Import):
                names.update(alias.name.split(".")[0] for alias in node.names)
            elif isinstance(node, ast.ImportFrom) and node.level == 0 and node.module:
                names.add(node.module.split(".")[0])
        print(json.dumps(sorted(names - sys.stdlib_module_names - {"__future__"})))
        """;

    [PythonAvailableFact]
    public void ReadHandoffPyValidatesAndSummarizesASyntheticHandoffFolder()
    {
        var root = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "SIL.Motif.HandoffAssetsTests", Guid.NewGuid().ToString("N")));
        try
        {
            var scriptPath = BuildSyntheticHandoff(root.FullName);

            var validate = RunReadHandoff(scriptPath, "validate-handoff", root.FullName);
            Assert.Equal(0, validate.ExitCode);
            Assert.Equal("[]", validate.StandardOutput.Trim());

            var summarize = RunReadHandoff(scriptPath, "summarize-counts", root.FullName);
            Assert.Equal(0, summarize.ExitCode);
            Assert.Contains("\"text_count\": 1", summarize.StandardOutput);
            Assert.Contains("\"rule_count\": 1", summarize.StandardOutput);
        }
        finally
        {
            Directory.Delete(root.FullName, recursive: true);
        }
    }

    private static string ReadEmbeddedText(string resourceName)
    {
        using var stream = CommandsAssembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void ExtractEmbeddedAsset(string resourceName, string destinationPath)
    {
        using var stream = CommandsAssembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' was not found.");
        using var destination = File.Create(destinationPath);
        stream.CopyTo(destination);
    }

    private static string BuildSyntheticHandoff(string root)
    {
        var referenceDir = Directory.CreateDirectory(Path.Combine(root, "reference")).FullName;
        var textsDir = Directory.CreateDirectory(Path.Combine(root, "texts")).FullName;
        var statisticsDir = Directory.CreateDirectory(Path.Combine(root, "statistics")).FullName;

        ExtractEmbeddedAsset(InstructionsResource, Path.Combine(root, "instructions.md"));
        ExtractEmbeddedAsset(RecipesResource, Path.Combine(root, "recipes.md"));
        var scriptPath = Path.Combine(root, "read_handoff.py");
        ExtractEmbeddedAsset(ReadHandoffPyResource, scriptPath);
        ExtractEmbeddedAsset(GrammarFormatResource, Path.Combine(referenceDir, "grammar-format.md"));
        ExtractEmbeddedAsset(FlexTextFormatResource, Path.Combine(referenceDir, "flextext-json-format.md"));
        ExtractEmbeddedAsset(HcMechanicsResource, Path.Combine(referenceDir, "hc-mechanics.md"));

        File.WriteAllText(Path.Combine(root, "selection.txt"), "mirusi (from text example)\n");
        File.WriteAllText(Path.Combine(root, "statistics.md"), "# Synthetic statistics summary\n");
        File.WriteAllText(Path.Combine(root, "grammar.json"), """
            {
              "rules": [ { "guid": "11111111-1111-1111-1111-111111111111", "name": "rule-a" } ],
              "entries": [ { "guid": "22222222-2222-2222-2222-222222222222", "form": "miru", "gloss": "run" } ]
            }
            """);
        File.WriteAllText(Path.Combine(textsDir, "example-00000000000000000000000000000001.flextext.json"), """
            {
              "document": { "interlinear-text": [ {
                "guid": "00000000-0000-0000-0000-000000000001",
                "item": [ { "type": "title", "lang": "en", "value": "Example" } ],
                "paragraphs": { "paragraph": [ {
                  "guid": "00000000-0000-0000-0000-000000000002",
                  "phrases": { "phrase": [ {
                    "guid": "00000000-0000-0000-0000-000000000003",
                    "item": [ { "type": "gls", "lang": "en", "value": "She ran." } ],
                    "words": { "word": [ {
                      "guid": "00000000-0000-0000-0000-000000000004",
                      "item": [ { "type": "txt", "lang": "inv", "value": "Mirusi" } ],
                      "morphemes": { "analysisStatus": "approved", "morph": [] }
                    } ] }
                  } ] }
                } ] }
              } ] }
            }
            """);

        foreach (var group in new[] { "word", "object", "allomorph", "morpheme", "group", "never-fires" })
            File.WriteAllText(Path.Combine(statisticsDir, $"{group}.jsonl"), $"{{\"id\": \"{group}-1\"}}\n");

        return scriptPath;
    }

    private static (int ExitCode, string StandardOutput) RunReadHandoff(string scriptPath, params string[] args)
    {
        var startInfo = new ProcessStartInfo("python")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(scriptPath);
        foreach (var arg in args) startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start python.");

        // Drain both streams concurrently: a large enough write on either one deadlocks a sequential read.
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(60_000), "read_handoff.py did not exit within its bound.");
        var output = outputTask.GetAwaiter().GetResult();
        var error = errorTask.GetAwaiter().GetResult();

        Assert.True(process.ExitCode == 0 || process.ExitCode == 1, $"Unexpected exit {process.ExitCode}: {error}");
        return (process.ExitCode, output);
    }
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SIL.Motif.Tests.TestFixtures;

public sealed class ConformanceProject : IDisposable
{
    private const string ExpectedRepository = "https://github.com/sillsdev/machine";
    private const string ExpectedCommit = "f150e2a005ce639f7d68ef17fb0db25b2f6aaa3c";
    private const string ExpectedSourceRoot = "conformance/edge-cases/deep-optional-affix-nesting";
    private const string ProjectDirectoryName = "DeepOptionalAffixNesting";
    private const string ProvisioningCommand = "pwsh ./evals/Get-ConformanceFixture.ps1";
    private const string OracleCachePath = "words.yaml";

    private readonly string _projectRoot;

    public ConformanceProject(string? conformanceCachePath = null)
    {
        var manifest = ReadManifest();
        var cacheRoot = ResolveCacheRoot(conformanceCachePath, manifest.Commit);
        var verifiedFiles = ReadVerifiedFiles(cacheRoot, manifest.Files);
        var selectedWords = ReadOracleSelections(
            verifiedFiles[OracleCachePath], manifest.OracleSelections);
        OneAnalysisShort = selectedWords.OneAnalysisShort;
        OneAnalysisLong = selectedWords.OneAnalysisLong;
        NineHundredTwentyFour = selectedWords.NineHundredTwentyFour;
        _projectRoot = Path.Combine(
            Path.GetTempPath(), "SIL.Motif.Conformance", Guid.NewGuid().ToString("N"));
        ManagedRoot = Path.Combine(
            Path.GetTempPath(), "SIL.Motif.Conformance.Managed", Guid.NewGuid().ToString("N"));

        try
        {
            var projectDirectory = Path.Combine(_projectRoot, ProjectDirectoryName);
            Directory.CreateDirectory(projectDirectory);
            foreach (var file in manifest.Files.Where(file => file.CachePath != OracleCachePath))
            {
                var destination = Path.Combine(
                    projectDirectory, file.CachePath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.WriteAllBytes(destination, verifiedFiles[file.CachePath]);
            }

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

    public string OneAnalysisShort { get; }

    public string OneAnalysisLong { get; }

    public string NineHundredTwentyFour { get; }

    public string FwDataPath { get; }

    public string ManagedRoot { get; }

    public string SourceSha256 { get; }

    public void Dispose()
    {
        DeleteDirectory(_projectRoot);
        DeleteDirectory(ManagedRoot);
    }

    private static PinnedManifest ReadManifest()
    {
        var manifestPath = Path.Combine(AppContext.BaseDirectory, "conformance.lock.json");
        if (!File.Exists(manifestPath))
            throw new InvalidDataException("The test output is missing conformance.lock.json.");

        using var document = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        var root = document.RootElement;
        var repository = root.GetProperty("repository").GetString();
        var commit = root.GetProperty("commit").GetString();
        var sourceRoot = root.GetProperty("sourceRoot").GetString();
        if (repository != ExpectedRepository || commit != ExpectedCommit || sourceRoot != ExpectedSourceRoot)
            throw new InvalidDataException("The conformance lock does not match the reviewed Machine source pin.");

        var files = root.GetProperty("cacheFiles").EnumerateArray()
            .Select(file => new PinnedFile(
                file.GetProperty("sourcePath").GetString() ?? string.Empty,
                file.GetProperty("cachePath").GetString() ?? string.Empty,
                file.GetProperty("bytes").GetInt32(),
                file.GetProperty("sha256").GetString() ?? string.Empty))
            .ToArray();
        var expectedPaths = new HashSet<string>(StringComparer.Ordinal)
        {
            "project.fwdata",
            "WritingSystemStore/de.ldml",
            "WritingSystemStore/en.ldml",
            "WritingSystemStore/es.ldml",
            "WritingSystemStore/fr.ldml",
            OracleCachePath,
        };
        if (files.Length != expectedPaths.Count ||
            !expectedPaths.SetEquals(files.Select(file => file.CachePath)))
            throw new InvalidDataException("The conformance lock must name exactly the consumed project and oracle files.");

        foreach (var file in files)
        {
            ValidateRelativePath(file.CachePath);
            var expectedSourcePath = file.CachePath == OracleCachePath
                ? $"{ExpectedSourceRoot}/{OracleCachePath}"
                : $"{ExpectedSourceRoot}/fieldworks/{file.CachePath}";
            if (file.SourcePath != expectedSourcePath ||
                file.Bytes <= 0 || file.Bytes > 1_048_576 ||
                file.Sha256.Length != 64 || file.Sha256.Any(character => !Uri.IsHexDigit(character)))
                throw new InvalidDataException($"Invalid conformance lock entry for {file.CachePath}.");
        }

        var selectionElement = root.GetProperty("oracleSelections");
        var expectedSelectionNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "oneAnalysisShort",
            "oneAnalysisLong",
            "nineHundredTwentyFour",
        };
        var selectionProperties = selectionElement.EnumerateObject().ToArray();
        if (selectionProperties.Length != expectedSelectionNames.Count ||
            !expectedSelectionNames.SetEquals(selectionProperties.Select(property => property.Name)))
            throw new InvalidDataException("The conformance lock must select exactly the three reviewed oracle cases.");

        PinnedSelection ReadSelection(string name)
        {
            var selection = selectionElement.GetProperty(name);
            return new PinnedSelection(
                selection.GetProperty("byteOffset").GetInt32(),
                selection.GetProperty("byteLength").GetInt32());
        }

        var oracleSelections = new OracleSelections(
            ReadSelection("oneAnalysisShort"),
            ReadSelection("oneAnalysisLong"),
            ReadSelection("nineHundredTwentyFour"));
        return new PinnedManifest(commit!, files, oracleSelections);
    }

    private static string ResolveCacheRoot(string? suppliedPath, string commit)
    {
        if (!string.IsNullOrWhiteSpace(suppliedPath))
            return Path.GetFullPath(suppliedPath);

        var configuredPath = Environment.GetEnvironmentVariable("MOTIF_TEST_CONFORMANCE");
        if (!string.IsNullOrWhiteSpace(configuredPath))
            return Path.GetFullPath(configuredPath);

        var repositoryRoot = FindRepositoryRoot();
        RejectLinkedPath(
            repositoryRoot,
            Path.Combine("bin", ".cache", "conformance-oracle-v2", commit)
                .Replace(Path.DirectorySeparatorChar, '/'));
        return Path.Combine(repositoryRoot, "bin", ".cache", "conformance-oracle-v2", commit);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var gitMarker = Path.Combine(directory.FullName, ".git");
            if (File.Exists(gitMarker) || Directory.Exists(gitMarker))
                return directory.FullName;
        }

        throw new InvalidDataException(
            $"Cannot locate the Motif checkout. Set MOTIF_TEST_CONFORMANCE or run {ProvisioningCommand}.");
    }

    private static Dictionary<string, byte[]> ReadVerifiedFiles(
        string cacheRoot, IReadOnlyList<PinnedFile> files)
    {
        if (!Directory.Exists(cacheRoot))
            throw new InvalidDataException(
                $"Pinned conformance input is absent at {cacheRoot}. Run {ProvisioningCommand}. " +
                "Set MOTIF_TEST_CONFORMANCE to a verified cache if it is stored elsewhere.");

        var root = Path.GetFullPath(cacheRoot);
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"Conformance cache root cannot be a link: {root}");

        var verified = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var path = Path.Combine(root, file.CachePath.Replace('/', Path.DirectorySeparatorChar));
            RejectLinkedPath(root, file.CachePath);
            if (!File.Exists(path))
                throw new InvalidDataException(
                    $"Pinned conformance file is missing: {path}. Run {ProvisioningCommand}.");

            var actualLength = new FileInfo(path).Length;
            if (actualLength != file.Bytes)
                throw new InvalidDataException(
                    $"Pinned conformance file length mismatch for {file.CachePath}: expected {file.Bytes}, got {actualLength}.");

            var bytes = ReadBoundedFile(path, file.CachePath, file.Bytes);

            var actualHash = Convert.ToHexString(SHA256.HashData(bytes));
            if (!actualHash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"Pinned conformance file SHA-256 mismatch for {file.CachePath}: expected {file.Sha256}, got {actualHash}.");

            verified.Add(file.CachePath, bytes);
        }

        return verified;
    }

    private static SelectedWords ReadOracleSelections(
        byte[] oracle, OracleSelections selections)
    {
        var selected = new[]
        {
            (Offset: selections.OneAnalysisShort.ByteOffset, Length: selections.OneAnalysisShort.ByteLength),
            (Offset: selections.OneAnalysisLong.ByteOffset, Length: selections.OneAnalysisLong.ByteLength),
            (Offset: selections.NineHundredTwentyFour.ByteOffset, Length: selections.NineHundredTwentyFour.ByteLength),
        };
        var spans = selected.OrderBy(item => item.Offset).ToArray();
        for (var index = 1; index < spans.Length; index++)
        {
            if ((long)spans[index].Offset < (long)spans[index - 1].Offset + spans[index - 1].Length)
                throw new InvalidDataException("Conformance oracle selections overlap.");
        }

        return new SelectedWords(
            ReadOracleWord(oracle, selections.OneAnalysisShort, "oneAnalysisShort"),
            ReadOracleWord(oracle, selections.OneAnalysisLong, "oneAnalysisLong"),
            ReadOracleWord(oracle, selections.NineHundredTwentyFour, "nineHundredTwentyFour"));
    }

    private static string ReadOracleWord(byte[] oracle, PinnedSelection selection, string name)
    {
        var offset = selection.ByteOffset;
        var length = selection.ByteLength;
        var prefix = Encoding.UTF8.GetBytes("  - word: \"");
        if (offset < prefix.Length || length <= 0 || (long)offset + length >= oracle.LongLength)
            throw new InvalidDataException($"Invalid bounded conformance oracle selection: {name}.");

        var lineStart = offset - prefix.Length;
        while (lineStart > 0 && oracle[lineStart - 1] != (byte)'\n')
            lineStart--;
        if (lineStart != offset - prefix.Length ||
            !oracle.AsSpan(lineStart, prefix.Length).SequenceEqual(prefix))
            throw new InvalidDataException($"Conformance oracle selection is not at a word scalar boundary: {name}.");

        var end = offset + length;
        if (oracle[end] != (byte)'"')
            throw new InvalidDataException($"Conformance oracle selection does not end at a scalar boundary: {name}.");
        var tail = end + 1;
        while (tail < oracle.Length && (oracle[tail] == (byte)' ' || oracle[tail] == (byte)'\t'))
            tail++;
        if (tail < oracle.Length && oracle[tail] != (byte)'\r' && oracle[tail] != (byte)'\n')
            throw new InvalidDataException($"Conformance oracle selection has trailing scalar data: {name}.");

        var valueBytes = oracle.AsSpan(offset, length);
        foreach (var valueByte in valueBytes)
        {
            if (valueByte is 0 or 10 or 13 or 34 or 92)
                throw new InvalidDataException($"Conformance oracle selection requires YAML escape processing: {name}.");
        }

        try
        {
            return new UTF8Encoding(false, true).GetString(valueBytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException($"Conformance oracle selection is not valid UTF-8: {name}.", exception);
        }
    }

    private static byte[] ReadBoundedFile(string path, string relativePath, int expectedLength)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var bytes = new byte[expectedLength];
        var total = 0;
        while (total < bytes.Length)
        {
            var read = stream.Read(bytes, total, bytes.Length - total);
            if (read == 0)
                throw new InvalidDataException(
                    $"Pinned conformance file length mismatch for {relativePath}: expected {expectedLength}, got {total}.");
            total += read;
        }

        if (stream.ReadByte() != -1)
            throw new InvalidDataException(
                $"Pinned conformance file length exceeds {expectedLength} bytes: {relativePath}");

        return bytes;
    }

    private static void RejectLinkedPath(string root, string relativePath)
    {
        var current = root;
        foreach (var part in relativePath.Split('/'))
        {
            current = Path.Combine(current, part);
            if (!File.Exists(current) && !Directory.Exists(current))
                continue;

            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"Conformance cache contains a linked path: {current}");
        }
    }

    private static void ValidateRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains('\\') ||
            path.Split('/').Any(part => part is "" or "." or "..") || path.Contains(':'))
            throw new InvalidDataException($"Unsafe path in conformance lock: {path}");
    }

    private static void DeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed record PinnedManifest(
        string Commit,
        IReadOnlyList<PinnedFile> Files,
        OracleSelections OracleSelections);

    private sealed record PinnedSelection(int ByteOffset, int ByteLength);

    private sealed record OracleSelections(
        PinnedSelection OneAnalysisShort,
        PinnedSelection OneAnalysisLong,
        PinnedSelection NineHundredTwentyFour);

    private sealed record SelectedWords(
        string OneAnalysisShort,
        string OneAnalysisLong,
        string NineHundredTwentyFour);

    private sealed record PinnedFile(string SourcePath, string CachePath, int Bytes, string Sha256);
}

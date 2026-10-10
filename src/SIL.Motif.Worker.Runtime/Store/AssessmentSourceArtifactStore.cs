using SIL.Motif.Host.PanGloss;

namespace SIL.Motif.Worker.Store;

internal static class AssessmentSourceArtifactStore
{
    internal static string Persist(string workerRoot, string sourcePath, string sourceBytesSha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var digest = NormalizeDigest(sourceBytesSha256);
        var directory = Path.Combine(Path.GetFullPath(workerRoot), "assessment-sources");
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, digest + ".fwdata");
        if (File.Exists(destination))
        {
            Verify(destination, sourceBytesSha256);
            return destination;
        }

        var temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.Copy(sourcePath, temporary);
            Verify(temporary, sourceBytesSha256);
            try
            {
                File.Move(temporary, destination);
            }
            catch (IOException) when (File.Exists(destination))
            {
                Verify(destination, sourceBytesSha256);
            }
            return destination;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    internal static void Verify(string sourcePath, string sourceBytesSha256)
    {
        if (!File.Exists(sourcePath) || BatchInvocationEvidence.DigestFile(sourcePath) != sourceBytesSha256)
            throw new InvalidDataException("A retained Assessment source does not match its recorded byte digest.");
    }

    private static string NormalizeDigest(string digest)
    {
        const string prefix = "sha256:";
        if (digest is null || !digest.StartsWith(prefix, StringComparison.Ordinal) || digest.Length != prefix.Length + 64 ||
            !digest.AsSpan(prefix.Length).ToString().All(Uri.IsHexDigit))
            throw new InvalidDataException("An Assessment source has an invalid byte digest.");
        return digest[prefix.Length..].ToLowerInvariant();
    }
}

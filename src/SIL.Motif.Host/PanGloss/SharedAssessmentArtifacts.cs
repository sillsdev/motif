namespace SIL.Motif.Host.PanGloss;

internal static class SharedAssessmentArtifacts
{
    internal static string Publish(string directory, string source, bool transferOwnership = false)
    {
        var digest = BatchInvocationEvidence.DigestFile(source);
        var destination = Path.Combine(directory, digest["sha256:".Length..] + Path.GetExtension(source));
        if (File.Exists(destination))
        {
            Verify(destination, digest);
            if (transferOwnership && !SamePath(source, destination)) File.Delete(source);
            return destination;
        }

        if (transferOwnership)
        {
            try
            {
                File.Move(source, destination);
                try { Verify(destination, digest); }
                catch (InvalidDataException) { File.Delete(destination); throw; }
                return destination;
            }
            catch (IOException) when (File.Exists(destination))
            {
                Verify(destination, digest);
                File.Delete(source);
                return destination;
            }
            catch (IOException) { }
        }

        var temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.Copy(source, temporary);
            Verify(temporary, digest);
            try { File.Move(temporary, destination); }
            catch (IOException) when (File.Exists(destination)) { Verify(destination, digest); }
            if (transferOwnership) File.Delete(source);
            return destination;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static bool SamePath(string first, string second) => string.Equals(Path.GetFullPath(first),
        Path.GetFullPath(second), OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static void Verify(string path, string digest)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 ||
            BatchInvocationEvidence.DigestFile(path) != digest)
            throw new InvalidDataException("A shared Assessment artifact does not match its content hash.");
    }
}

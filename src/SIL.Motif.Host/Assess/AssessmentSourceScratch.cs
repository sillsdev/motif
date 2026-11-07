using SIL.Motif.Host.PanGloss;

namespace SIL.Motif.Host.Assess;

/// <summary>An isolated, verified copy for opening an Assessment source with LibLCM.</summary>
public sealed class AssessmentSourceScratch : IDisposable
{
    private readonly string _directory;

    private AssessmentSourceScratch(string directory)
    {
        _directory = directory;
        Path = System.IO.Path.Combine(directory, "source.fwdata");
    }

    /// <summary>The isolated FieldWorks project file.</summary>
    public string Path { get; }

    /// <summary>Creates a source copy verified against the recorded digest.</summary>
    public static AssessmentSourceScratch Create(string source, string digest)
    {
        var scratch = new AssessmentSourceScratch(Directory.CreateTempSubdirectory("motif-assessment-source-").FullName);
        try
        {
            File.Copy(source, scratch.Path);
            if (BatchInvocationEvidence.DigestFile(scratch.Path) != digest)
                throw new InvalidDataException("The isolated Assessment source does not match its recorded bytes.");
            return scratch;
        }
        catch
        {
            scratch.Dispose();
            throw;
        }
    }

    /// <summary>Removes the private copy and its generated side files.</summary>
    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

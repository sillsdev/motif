namespace SIL.Motif.Tests.App.Walkthrough;

internal static class WalkthroughTestFiles
{
    internal static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

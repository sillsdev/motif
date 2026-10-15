namespace SIL.Motif.App.Services;

/// <summary>
/// Shows a file path in wrapped text. A path has no spaces, so a wrapped line would otherwise break inside a
/// folder name; a zero-width space after each separator and dot, and inside any unbroken run too long to fit
/// a narrow line, gives it break points between names. The exact path stays on the model, for the folder
/// command and the accessible name.
/// </summary>
public static class PathDisplay
{
    private const int LongestUnbrokenRun = 12;

    /// <summary>The wrappable form of <paramref name="path"/>, for display only.</summary>
    public static string Wrappable(string path)
    {
        var result = new System.Text.StringBuilder(path.Length * 2);
        var run = 0;
        foreach (var c in path)
        {
            result.Append(c);
            run++;
            if (c is '/' or '\\' or '.' or '-' || run == LongestUnbrokenRun)
            {
                result.Append('​');
                run = 0;
            }
        }
        return result.ToString();
    }
}

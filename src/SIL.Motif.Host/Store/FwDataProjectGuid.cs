using System.Text.RegularExpressions;

namespace SIL.Motif.Host.Store;

/// <summary>Reads the LibLCM language project GUID out of a <c>.fwdata</c> file without opening the project.</summary>
public static partial class FwDataProjectGuid
{
    [GeneratedRegex("""<rt\s[^>]*class="LangProject"[^>]*>""", RegexOptions.CultureInvariant)]
    private static partial Regex LangProjectElement();

    [GeneratedRegex(@"\bguid=""([0-9A-Fa-f-]{36})""", RegexOptions.CultureInvariant)]
    private static partial Regex GuidAttribute();

    /// <summary>The project's GUID, or <see langword="null"/> when the file has no language project.</summary>
    public static string? TryRead(string fwDataPath)
    {
        try
        {
            using var reader = new StreamReader(fwDataPath);
            while (reader.ReadLine() is { } line)
            {
                var element = LangProjectElement().Match(line);
                if (!element.Success) continue;
                var guid = GuidAttribute().Match(element.Value);
                return guid.Success ? guid.Groups[1].Value.ToLowerInvariant() : null;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
        return null;
    }
}

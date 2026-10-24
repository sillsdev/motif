using System.Globalization;
using System.Xml;

namespace SIL.Motif.Host.Store;

/// <summary>Why Motif could not choose or store a machine-local project tag.</summary>
public enum ProjectTagProblem
{
    /// <summary>A candidate does not follow the tag rules.</summary>
    Invalid,

    /// <summary>A candidate is already assigned to another Known project.</summary>
    Taken,

    /// <summary>The project's vernacular language could not be determined.</summary>
    Unavailable,

}

/// <summary>A project tag could not be assigned in Motif's machine-local registry.</summary>
public sealed class ProjectTagException : Exception
{
    /// <summary>Builds a refusal that gives the person the project-tag command to resolve it.</summary>
    public ProjectTagException(ProjectTagProblem problem, string tag, string projectPath)
        : base(MessageFor(problem, tag, projectPath))
    {
        Problem = problem;
        Tag = tag;
    }

    /// <summary>The tag-selection problem that caused the refusal.</summary>
    public ProjectTagProblem Problem { get; }

    /// <summary>The candidate tag, or the empty string when no candidate could be read.</summary>
    public string Tag { get; }

    private static string MessageFor(ProjectTagProblem problem, string tag, string projectPath)
    {
        var guidance = $"Set a tag for this project with: motif project tag --set <new-tag> --project \"{projectPath}\".";
        return problem switch
        {
            ProjectTagProblem.Invalid => $"The project tag '{tag}' is invalid. {ProjectTags.RulesText} {guidance}",
            ProjectTagProblem.Taken => $"The project tag '{tag}' is already used by another Known project. {guidance}",
            ProjectTagProblem.Unavailable => $"Motif could not determine this project's vernacular language code. {guidance}",
            _ => throw new ArgumentOutOfRangeException(nameof(problem), problem, null),
        };
    }
}

/// <summary>Validates project tags and chooses a default for a newly Known project.</summary>
public static class ProjectTags
{
    /// <summary>The longest accepted tag.</summary>
    public const int MaxLength = 10;

    /// <summary>Rules shown when a person must choose a tag.</summary>
    public const string RulesText =
        "A project tag starts with a lowercase letter, uses lowercase letters, digits and '-', and is at most 10 characters.";

    /// <summary>Returns whether a tag follows the lowercase project-tag rules.</summary>
    public static bool IsValid(string? tag)
    {
        if (string.IsNullOrEmpty(tag) || tag.Length > MaxLength || !IsLowerLetter(tag[0])) return false;
        foreach (var character in tag)
            if (!IsLowerLetter(character) && !char.IsAsciiDigit(character) && character != '-') return false;
        return true;
    }

    /// <summary>Chooses the language code or, after a collision, the exact project-folder name.</summary>
    /// <param name="fullFwDataPath">The full path of the project's FieldWorks data file.</param>
    /// <param name="isTaken">Checks whether a Known project already uses a candidate.</param>
    /// <exception cref="ProjectTagException">The language or fallback candidate cannot be assigned.</exception>
    public static string Default(string fullFwDataPath, Func<string, bool> isTaken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullFwDataPath);
        ArgumentNullException.ThrowIfNull(isTaken);

        var fullPath = Path.GetFullPath(fullFwDataPath);
        var languageCode = LanguageCodeFrom(ReadMainVernacular(fullPath));
        if (languageCode is null)
            throw new ProjectTagException(ProjectTagProblem.Unavailable, string.Empty, fullPath);
        if (!IsValid(languageCode))
            throw new ProjectTagException(ProjectTagProblem.Invalid, languageCode, fullPath);
        if (!isTaken(languageCode)) return languageCode;

        var folderName = Path.GetFileName(Path.GetDirectoryName(fullPath)) ?? string.Empty;
        if (!IsValid(folderName))
            throw new ProjectTagException(ProjectTagProblem.Invalid, folderName, fullPath);
        if (isTaken(folderName))
            throw new ProjectTagException(ProjectTagProblem.Taken, folderName, fullPath);
        return folderName;
    }

    private static string? LanguageCodeFrom(string? writingSystemTag)
    {
        if (string.IsNullOrWhiteSpace(writingSystemTag)) return null;
        var language = writingSystemTag.Trim().Split('-', '_')[0].ToLowerInvariant();
        if (language.Length == 2)
        {
            try
            {
                language = CultureInfo.GetCultureInfo(language).ThreeLetterISOLanguageName.ToLowerInvariant();
            }
            catch (CultureNotFoundException)
            {
                return null;
            }
        }

        if (language.Length != 3 || !language.All(IsLowerLetter)) return null;
        return StringComparer.Ordinal.Compare(language, "qaa") >= 0 &&
               StringComparer.Ordinal.Compare(language, "qtz") <= 0 ? null : language;
    }

    private static string? ReadMainVernacular(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, bufferSize: 4096, FileOptions.SequentialScan);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreWhitespace = true,
            });
            var inLanguageProject = false;
            var inVernaculars = false;
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "rt")
                    inLanguageProject = reader.GetAttribute("class") == "LangProject";
                else if (inLanguageProject && reader.NodeType == XmlNodeType.Element && reader.LocalName == "CurVernWss")
                    inVernaculars = true;
                else if (inVernaculars && reader.NodeType == XmlNodeType.Element && reader.LocalName == "Uni")
                    return reader.ReadElementContentAsString().Split(
                        [' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                else if (inVernaculars && reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "CurVernWss")
                    return null;
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or XmlException or ArgumentException)
        {
            return null;
        }
        return null;
    }

    private static bool IsLowerLetter(char character) => character is >= 'a' and <= 'z';
}

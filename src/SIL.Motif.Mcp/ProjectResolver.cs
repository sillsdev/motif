using SIL.Motif.Commands.Queries;
using SIL.Motif.Worker;

namespace SIL.Motif.Mcp;

/// <summary>Resolves a Known project selector to its recorded path.</summary>
public static class ProjectResolver
{
    /// <summary>
    /// A path must belong to a Known project. Names are matched, ignoring case, against the
    /// names of the Known projects: the file name without its extension, or its folder's name.
    /// </summary>
    /// <exception cref="ProfileException">No project, or more than one, matches.</exception>
    public static string Resolve(string pathOrName, string managedRoot)
    {
        string fullPath;
        try { fullPath = Path.GetFullPath(pathOrName); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ProfileException("The project selector is not a valid name or path: " + exception.Message);
        }
        var known = KnownProjectsQuery.List(managedRoot);
        var matches = known.Where(project =>
            string.Equals(project.FullFwDataPath, fullPath,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
            string.Equals(Path.GetFileNameWithoutExtension(project.FullFwDataPath), pathOrName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetFileName(Path.GetDirectoryName(project.FullFwDataPath)), pathOrName, StringComparison.OrdinalIgnoreCase))
            .Select(project => project.FullFwDataPath).Distinct().ToList();
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new ProfileException($"'{pathOrName}' is not a Known project. " +
                $"Known projects: {(known.Count == 0 ? "none" : string.Join(", ", known.Select(k => k.FullFwDataPath)))}."),
            _ => throw new ProfileException($"'{pathOrName}' matches more than one Known project: " +
                $"{string.Join(", ", matches)}. Pass the full path."),
        };
    }
}

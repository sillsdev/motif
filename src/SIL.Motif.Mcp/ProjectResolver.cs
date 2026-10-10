using SIL.Motif.Commands.Queries;
using SIL.Motif.Worker;

namespace SIL.Motif.Mcp;

/// <summary>Turns what <c>--project</c> was given into one <c>.fwdata</c> path.</summary>
public static class ProjectResolver
{
    /// <summary>
    /// A path to a <c>.fwdata</c> file is used as given. Anything else is matched, ignoring case, against the
    /// names of the Known projects: the file name without its extension, or its folder's name.
    /// </summary>
    /// <exception cref="ProfileException">No project, or more than one, matches.</exception>
    public static string Resolve(string pathOrName, string managedRoot)
    {
        if (File.Exists(pathOrName)) return Path.GetFullPath(pathOrName);
        var known = KnownProjectsQuery.List(managedRoot);
        var matches = known.Where(project =>
            string.Equals(Path.GetFileNameWithoutExtension(project.FullFwDataPath), pathOrName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetFileName(Path.GetDirectoryName(project.FullFwDataPath)), pathOrName, StringComparison.OrdinalIgnoreCase))
            .Select(project => project.FullFwDataPath).Distinct().ToList();
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new ProfileException($"'{pathOrName}' is not a .fwdata file and not a Known project. " +
                $"Known projects: {(known.Count == 0 ? "none" : string.Join(", ", known.Select(k => k.FullFwDataPath)))}."),
            _ => throw new ProfileException($"'{pathOrName}' matches more than one Known project: " +
                $"{string.Join(", ", matches)}. Pass the full path."),
        };
    }
}

using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Requests;

namespace SIL.Motif.App.Preview;

/// <summary>Names a project's writing systems as the project does, from its current Baseline.</summary>
public static class WritingSystemNames
{
    /// <summary>
    /// A lookup from a writing system's tag to its name in <paramref name="projectPath"/>; a tag the Baseline does not
    /// name, or a project without a Baseline, reads as the tag itself.
    /// </summary>
    public static Func<string, string> For(string projectPath)
    {
        var read = WritingSystemsQuery.Query(new WritingSystemsRequest(projectPath));
        var names = read.Succeeded
            ? read.Value!.WritingSystems.Where(system => system.Name.Length > 0)
                .GroupBy(system => system.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().Name, StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);
        return tag => names.GetValueOrDefault(tag, tag);
    }
}

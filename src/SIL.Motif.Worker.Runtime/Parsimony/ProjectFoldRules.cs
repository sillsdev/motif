using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Config;

namespace SIL.Motif.Worker.Parsimony;

/// <summary>Reads the project's <c>[parsimony] fold</c> rules once per job; a file that cannot be read folds nothing.</summary>
internal static class ProjectFoldRules
{
    public static IReadOnlyList<FoldRule> Read(ProjectLocator project)
    {
        try
        {
            return [.. new ProjectConfigurationReader().Read(project).ParsimonyFold
                .Select(text => FoldRule.TryParse(text, out var rule) ? rule : null).OfType<FoldRule>()];
        }
        catch (ProjectConfigurationException)
        {
            // The same file is refused by config show and by Trial capture, which name the line.
            return [];
        }
    }
}

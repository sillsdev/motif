using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Config;
using SIL.Motif.Host.Store;

namespace SIL.Motif.Commands.Queries;

/// <summary>The parser work limit a trace or a why-lost comparison runs under, so it stops where the Trial stopped.</summary>
internal static class ProjectWorkLimit
{
    /// <summary>The limit a Trial of this project would freeze: the project's own, a saved Selection's, or the default.</summary>
    public static ParserWorkLimit Effective(MotifDatabase database, ProjectLocator project) =>
        JobCommands.ReadTrialLimitPolicy(database, project, null)?.PerWordParserWorkLimit ?? ParserWorkLimit.Default;

    /// <summary>The limit the project's configuration declares, or null when it declares none or cannot be read.</summary>
    public static ParserWorkLimit? Declared(string projectPath)
    {
        try
        {
            var located = ProjectStoreCommand.LocateProject(projectPath);
            return located.Succeeded ? new ProjectConfigurationReader().Read(located.Value!).ParserWorkLimit : null;
        }
        catch (ProjectConfigurationException)
        {
            return null;
        }
    }
}

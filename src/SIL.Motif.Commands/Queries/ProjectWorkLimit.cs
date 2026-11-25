using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Config;
using SIL.Motif.Host.Store;

namespace SIL.Motif.Commands.Queries;

/// <summary>The parser work limit a trace or a why-lost comparison runs under, so it stops where the Trial stopped.</summary>
internal static class ProjectWorkLimit
{
    /// <summary>
    /// The one resolver: an explicit limit, else the project's own setting, else the limit a saved Selection or Trial froze,
    /// else the default.
    /// </summary>
    public static ParserWorkLimit Effective(MotifDatabase database, ProjectLocator project, ParserWorkLimit? explicitLimit = null) =>
        Resolve(explicitLimit, DeclaredIn(project), JobCommands.ReadTrialLimitPolicy(database, project, null)?.PerWordParserWorkLimit);

    /// <summary>The same resolution for a caller that has only the project path; reads the store without creating it.</summary>
    public static ParserWorkLimit ForProject(string projectPath, ParserWorkLimit? explicitLimit = null)
    {
        if (explicitLimit is not null) return explicitLimit;
        var declared = Declared(projectPath);
        if (declared is not null) return declared;
        ParserWorkLimit? saved = null;
        try
        {
            ProjectStoreCommand.ReadExisting(projectPath, CommandOutcome<ParserWorkLimitBox>.Success(new ParserWorkLimitBox(null)),
                (database, project) =>
                {
                    saved = JobCommands.ReadTrialLimitPolicy(database, project, null)?.PerWordParserWorkLimit;
                    return CommandOutcome<ParserWorkLimitBox>.Success(new ParserWorkLimitBox(saved));
                });
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException)
        {
            saved = null;
        }
        return Resolve(null, null, saved);
    }

    /// <summary>The precedence itself: explicit, then declared, then saved, then the default.</summary>
    internal static ParserWorkLimit Resolve(ParserWorkLimit? explicitLimit, ParserWorkLimit? declared, ParserWorkLimit? saved) =>
        explicitLimit ?? declared ?? saved ?? ParserWorkLimit.Default;

    private sealed record ParserWorkLimitBox(ParserWorkLimit? Limit);

    private static ParserWorkLimit? DeclaredIn(ProjectLocator project)
    {
        try
        {
            return new ProjectConfigurationReader().Read(project).ParserWorkLimit;
        }
        catch (ProjectConfigurationException)
        {
            return null;
        }
    }

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

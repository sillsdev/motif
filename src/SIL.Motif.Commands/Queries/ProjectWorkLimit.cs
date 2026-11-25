using System.Globalization;
using SIL.Motif.Commands.Review;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Config;
using SIL.Motif.Host.Store;

namespace SIL.Motif.Commands.Queries;

/// <summary>A parser work limit and where it came from, so a trace can say which limit it ran under.</summary>
internal sealed record ResolvedWorkLimit(ParserWorkLimit Limit, string Source)
{
    /// <summary>The limit in words, for a note or a response field.</summary>
    public string Describe() =>
        (Limit.Work is { } work ? work.ToString("N0", CultureInfo.InvariantCulture) + " work units" : "no work limit") +
        " (" + Source + ")";
}

/// <summary>
/// The parser work limit a trace or a why-lost comparison runs under, so it stops where the Trial stopped. One order
/// everywhere: an explicit flag, then the limit the scope or the saved Default Selection itself carries, then the
/// project's <c>[parser] work-limit</c> (which every scope that names no limit of its own has already taken), then the
/// default. A word that a Trial measured is traced under that Trial's frozen limit instead.
/// </summary>
internal static class ProjectWorkLimit
{
    /// <summary>The precedence itself: an explicit limit, else the configured one, else the default.</summary>
    internal static ParserWorkLimit Resolve(ParserWorkLimit? explicitLimit, ParserWorkLimit? configured) =>
        explicitLimit ?? configured ?? ParserWorkLimit.Default;

    /// <summary>The limit a Trial of this project would freeze now; throws <see cref="ProjectConfigurationException"/> for an invalid file.</summary>
    public static ParserWorkLimit Effective(MotifDatabase database, ProjectLocator project, ParserWorkLimit? explicitLimit = null)
    {
        if (explicitLimit is not null) return explicitLimit;
        _ = new ProjectConfigurationReader().Read(project);
        return Resolve(null, JobCommands.ReadTrialLimitPolicy(database, project, null)?.PerWordParserWorkLimit);
    }

    /// <summary>
    /// The limit to trace a word under: the frozen limit of the Trial that measured <paramref name="trialJobId"/>, when
    /// there is one and it recorded a limit, else the current resolution.
    /// </summary>
    public static ResolvedWorkLimit ForTrace(MotifDatabase database, ProjectLocator project, string? trialJobId)
    {
        if (trialJobId is not null && TrialExportReads.FrozenWorkLimit(database, trialJobId) is { } frozen)
            return new ResolvedWorkLimit(frozen, $"frozen by Trial job {trialJobId}");
        return new ResolvedWorkLimit(Effective(database, project), "the project's current limit");
    }

    /// <summary>
    /// The same resolution for a caller that has only the project path and, optionally, the Proposal whose Trial it
    /// explains. Reads the store without creating it; a project with no store gets the configured or default limit.
    /// </summary>
    public static ResolvedWorkLimit ForProject(string projectPath, string? proposalId = null)
    {
        var located = ProjectStoreCommand.LocateProject(projectPath);
        if (!located.Succeeded) return new ResolvedWorkLimit(ParserWorkLimit.Default, "the default");
        _ = new ProjectConfigurationReader().Read(located.Value!);
        ResolvedWorkLimit? resolved = null;
        var absent = CommandOutcome<LimitBox>.Success(new LimitBox());
        ProjectStoreCommand.ReadExisting(projectPath, absent, (database, project) =>
        {
            // A malformed id is refused by the trace itself, so it only means "no Trial" here.
            var jobId = proposalId is not null && SIL.Motif.Contract.Ids.CanonicalId.TryParse(proposalId, out _)
                ? Trials.LatestFor(database, project, proposalId)?.JobId : null;
            resolved = ForTrace(database, project, jobId);
            return CommandOutcome<LimitBox>.Success(new LimitBox());
        });
        return resolved ?? new ResolvedWorkLimit(
            Resolve(null, new ProjectConfigurationReader().Read(located.Value!).Scopes[0].PerWordParserWorkLimit),
            "the project's current limit");
    }

    /// <summary>The refusal for a configuration file that does not parse, the same on every path.</summary>
    public static Refusal InvalidConfiguration(ProjectLocator project, ProjectConfigurationException exception) =>
        new(RefusalCodes.ConfigInvalid, FailureReason.Refused, exception.Message,
            new Dictionary<string, string> { ["configPath"] = ProjectConfigurationReader.PathFor(project) });

    /// <summary>The same refusal for a caller that has only the project path.</summary>
    public static Refusal InvalidConfiguration(string projectPath, ProjectConfigurationException exception)
    {
        var located = ProjectStoreCommand.LocateProject(projectPath);
        return located.Succeeded
            ? InvalidConfiguration(located.Value!, exception)
            : new Refusal(RefusalCodes.ConfigInvalid, FailureReason.Refused, exception.Message);
    }

    private sealed record LimitBox;
}

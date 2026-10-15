namespace SIL.Motif.Contract.Commands;

/// <summary>
/// Refusal codes minted by the MCP server and the CLI front end rather than by a command: argument and tool
/// failures, the Advanced AI gate, and the CLI's own cancellation and unexpected-error codes. The values are
/// wire strings, so a rename is a breaking change.
/// </summary>
public static class HostRefusalCodes
{
    /// <summary>An MCP tool was called while Advanced AI mode is off.</summary>
    public const string AdvancedAiDisabled = "advanced-ai.disabled";

    /// <summary>Motif's saved copy of the project is missing from disk.</summary>
    public const string BaselineUnavailable = "baseline.unavailable";

    /// <summary>The CLI was cancelled before its command finished.</summary>
    public const string CliCancelled = "cli.cancelled";
    /// <summary>The CLI hit an error no command anticipated.</summary>
    public const string CliUnexpected = "cli.unexpected";

    /// <summary>An MCP tool was asked for a grammar kind that does not exist.</summary>
    public const string GrammarUnknownKind = "grammar.unknown-kind";

    /// <summary>An MCP tool was asked for a guide topic that does not exist.</summary>
    public const string GuideUnknownTopic = "guide.unknown-topic";

    /// <summary>A disposable project copy opened but could not be registered as Known.</summary>
    public const string ProjectRegistrationFailed = "project.registration-failed";
    /// <summary>The command named a project Motif does not know.</summary>
    public const string ProjectUnknown = "project.unknown";

    /// <summary>An MCP tool call was cancelled.</summary>
    public const string ToolCancelled = "tool.cancelled";
    /// <summary>An MCP tool failed unexpectedly.</summary>
    public const string ToolFailed = "tool.failed";
    /// <summary>An MCP tool argument has the wrong type or value.</summary>
    public const string ToolInvalidArgument = "tool.invalid-argument";
    /// <summary>An MCP tool was given a Difference category that does not exist.</summary>
    public const string ToolInvalidCategory = "tool.invalid-category";
    /// <summary>An MCP tool was given an offset or limit out of range.</summary>
    public const string ToolInvalidPage = "tool.invalid-page";
    /// <summary>A required MCP tool argument is absent.</summary>
    public const string ToolMissingArgument = "tool.missing-argument";
    /// <summary>An MCP tool named a Difference that does not exist.</summary>
    public const string ToolNotFound = "tool.not-found";
    /// <summary>An MCP call named a tool that does not exist.</summary>
    public const string ToolUnknown = "tool.unknown";
    /// <summary>An MCP tool was given an argument it does not have.</summary>
    public const string ToolUnknownArgument = "tool.unknown-argument";

    /// <summary>The Trial lacks matching Baseline Assessments, so it has no Difference.</summary>
    public const string TrialDifferenceUnavailable = "trial.difference-unavailable";
}

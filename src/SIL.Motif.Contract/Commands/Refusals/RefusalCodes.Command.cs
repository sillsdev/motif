namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    /// <summary>The command is offered only in Advanced AI mode.</summary>
    public const string CommandAdvancedAiModeRequired = "command.advanced-ai-mode-required";
    /// <summary>The command is offered only to a caller that declares Autonomous mode.</summary>
    public const string CommandAutonomousRequired = "command.autonomous-required";
    /// <summary>The command is offered only when developer commands are enabled.</summary>
    public const string CommandDeveloperCommandsRequired = "command.developer-commands-required";
}

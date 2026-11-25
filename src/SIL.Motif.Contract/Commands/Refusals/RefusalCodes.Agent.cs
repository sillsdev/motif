namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    /// <summary>An agent evaluation names a built-in agent, such as the default user or parser agent.</summary>
    public const string AgentBuiltIn = "agent.built-in";
    /// <summary>An agent evaluation names an agent that is marked human.</summary>
    public const string AgentHuman = "agent.human";
    /// <summary>An agent evaluation uses the human agent's name for a non-human agent.</summary>
    public const string AgentHumanName = "agent.human-name";
    /// <summary>The identity named for an agent already belongs to something that is not an agent.</summary>
    public const string AgentIdentityConflict = "agent.identity-conflict";
    /// <summary>An agent identity already exists under another name or version.</summary>
    public const string AgentVersionConflict = "agent.version-conflict";
}

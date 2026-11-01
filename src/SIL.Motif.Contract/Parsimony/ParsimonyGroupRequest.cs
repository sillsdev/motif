namespace SIL.Motif.Contract.Parsimony;

/// <summary>Which of the two measure groups a Parsimony run covers.</summary>
public enum ParsimonyGroupRequest
{
    /// <summary>The measures that read only the frozen grammar facts.</summary>
    Grammar,

    /// <summary>The measures that read the Parsimony Selection.</summary>
    Text,

    /// <summary>Both groups.</summary>
    All,
}

/// <summary>The one definition of the group names the CLI and the MCP tool both offer.</summary>
public static class ParsimonyGroupRequests
{
    /// <summary>The wire names in the order they are offered.</summary>
    public static IReadOnlyList<string> Names { get; } = ["grammar", "text", "all"];

    /// <summary>The wire name of a group.</summary>
    public static string Wire(ParsimonyGroupRequest group) => group switch
    {
        ParsimonyGroupRequest.Grammar => "grammar",
        ParsimonyGroupRequest.Text => "text",
        ParsimonyGroupRequest.All => "all",
        _ => throw new ArgumentOutOfRangeException(nameof(group)),
    };

    /// <summary>Reads an exact wire name; anything else is not a group.</summary>
    public static bool TryParse(string? name, out ParsimonyGroupRequest group)
    {
        group = default;
        var index = name is null ? -1 : Names.ToList().IndexOf(name);
        if (index < 0) return false;
        group = (ParsimonyGroupRequest)index;
        return true;
    }
}

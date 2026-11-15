using SIL.Motif.Contract.Commands;

namespace SIL.Motif.App.Services;

public partial interface ICommandClient
{
    /// <summary>Reads the total open Requests without loading every Request into Progress.</summary>
    Task<CommandOutcome<ProgressRequestCount>> ReadProgressOpenRequestCountAsync(string projectPath, CancellationToken cancellationToken);
}

/// <summary>The complete stored open Request total, rather than the size of a listing page.</summary>
/// <param name="OpenCount">Requests still awaiting a Proposal.</param>
public sealed record ProgressRequestCount(int OpenCount);

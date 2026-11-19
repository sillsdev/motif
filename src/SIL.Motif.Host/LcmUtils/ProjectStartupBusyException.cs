using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Host.LcmUtils;

/// <summary>A project file could not be acquired within the startup gate's wait budget.</summary>
public sealed class ProjectStartupBusyException : IOException
{
    private readonly string _projectPath;
    private readonly string? _holderKind;

    internal ProjectStartupBusyException(string projectPath, string? holderKind, bool fileChanged = false)
        : base(fileChanged
            ? $"Project file '{Path.GetFileName(projectPath)}' changed during startup. Try again."
            : $"{holderKind ?? "Another Motif task"} is using project file '{Path.GetFileName(projectPath)}'. " +
              "Wait for it to finish, then try again.")
    {
        _projectPath = projectPath;
        _holderKind = holderKind;
    }

    /// <summary>Provides the retryable refusal with the holder kind when available.</summary>
    public Refusal ToRefusal(string? proposalId = null)
    {
        var facts = new Dictionary<string, string> { ["fwDataPath"] = _projectPath };
        if (_holderKind is not null) facts["holderKind"] = _holderKind;
        if (proposalId is not null) facts["proposalId"] = proposalId;
        return new Refusal(RefusalCodes.ProjectStartupBusy, FailureReason.Busy, Message, facts);
    }
}

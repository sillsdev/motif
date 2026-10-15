using System;
using System.Collections.Generic;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Runner.Execution;

/// <summary>
/// A failure the Runner raises that a caller reports as a refusal, carrying the stable code, the reason
/// and the facts a caller needs. Commands turns every one into a <see cref="Refusal"/> in one place, so
/// the Runner never decides how a refusal is worded for a person.
/// </summary>
public abstract class ProposalRefusalException : InvalidOperationException
{
    protected ProposalRefusalException(
        string code, FailureReason reason, string message, IReadOnlyDictionary<string, string> facts)
        : base(message)
    {
        Code = code;
        Reason = reason;
        Facts = facts;
    }

    /// <summary>The stable refusal code a caller reports.</summary>
    public string Code { get; }

    /// <summary>Why the work was refused.</summary>
    public FailureReason Reason { get; }

    /// <summary>The values the failure was computed from, such as an operation id or a target.</summary>
    public IReadOnlyDictionary<string, string> Facts { get; }
}

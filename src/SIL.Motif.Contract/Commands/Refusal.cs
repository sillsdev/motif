using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

/// <summary>Why a command declined to act, in a form a caller can branch on without parsing prose.</summary>
/// <remarks>
/// A refusal carries three things a machine reader needs and one a human does: <see cref="Code"/> is a
/// stable identifier a caller can switch on across releases; <see cref="Reason"/> is the closed class that
/// decides retry policy and exit code; <see cref="Message"/> is the sentence a person reads; and
/// <see cref="Facts"/> holds the dynamic values — an id, a target, a requested status — that a caller needs
/// to act on the refusal rather than merely display it. Facts is immutable because a refusal is a completed
/// judgment: nothing downstream should be able to mutate the evidence a decision was already made from.
/// </remarks>
public sealed record Refusal
{
    public Refusal(string code, FailureReason reason, string message,
        IReadOnlyDictionary<string, string>? facts = null)
    {
        Code = string.IsNullOrWhiteSpace(code)
            ? throw new ArgumentException("A stable refusal code is required.", nameof(code))
            : code;
        Reason = reason;
        Message = string.IsNullOrWhiteSpace(message)
            ? throw new ArgumentException("A refusal message is required.", nameof(message))
            : message;
        Facts = facts is null
            ? ImmutableDictionary<string, string>.Empty
            : facts.ToImmutableDictionary(StringComparer.Ordinal);
    }

    /// <summary>A lower-case, dot-separated identifier stable across releases, such as <c>proposal.not-found</c>.</summary>
    public string Code { get; }

    public FailureReason Reason { get; }

    /// <summary>The same wording a human sees; a caller renders this rather than composing its own text.</summary>
    public string Message { get; }

    /// <summary>The dynamic values the refusal was computed from, keyed by a stable, ordinal-compared name.</summary>
    public IReadOnlyDictionary<string, string> Facts { get; }
}

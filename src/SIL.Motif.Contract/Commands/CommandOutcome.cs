using System;

namespace SIL.Motif.Contract.Commands;

/// <summary>A command result containing a value or a <see cref="Refusal"/>.</summary>
/// <remarks>
/// A caller checks <see cref="Succeeded"/> before using <see cref="Value"/>. A successful result may carry
/// a null value when its type is nullable; a refusal is distinguished by its non-null <see cref="Refusal"/>.
/// </remarks>
public sealed record CommandOutcome<T> where T : class?
{
    private CommandOutcome(T? value, Refusal? refusal)
    {
        if (value is not null && refusal is not null)
            throw new ArgumentException("A command outcome cannot contain both a value and a refusal.");
        Value = value;
        Refusal = refusal;
    }

    /// <summary><c>true</c> when the command produced a value; <c>false</c> when it produced a <see cref="Refusal"/>.</summary>
    public bool Succeeded => Refusal is null;

    public T? Value { get; }

    public Refusal? Refusal { get; }

    public static CommandOutcome<T> Success(T? value) => new(value, null);

    public static CommandOutcome<T> Refused(Refusal refusal) => new(default, refusal);
}

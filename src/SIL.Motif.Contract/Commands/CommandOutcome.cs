using System;

namespace SIL.Motif.Contract.Commands;

/// <summary>The result of a command: exactly one of a typed value or a <see cref="Refusal"/>, never both.</summary>
/// <remarks>
/// A closed carrier rather than a bare nullable return: a caller that checks <see cref="Succeeded"/> gets
/// both branches at once, and cannot read <see cref="Value"/> without having ruled out <see cref="Refusal"/>
/// first. The type parameter is constrained to reference types because the carrier tells success and
/// refusal apart by testing <c>value is null</c>; every response this catalog returns is a record, so the
/// constraint costs nothing and turns that null check into a guarantee instead of an assumption a value
/// type would silently violate.
/// </remarks>
public sealed record CommandOutcome<T> where T : class
{
    private CommandOutcome(T? value, Refusal? refusal)
    {
        if ((value is null) == (refusal is null))
            throw new ArgumentException("A command outcome requires exactly one value or refusal.");
        Value = value;
        Refusal = refusal;
    }

    /// <summary><c>true</c> when the command produced a value; <c>false</c> when it produced a <see cref="Refusal"/>.</summary>
    public bool Succeeded => Refusal is null;

    public T? Value { get; }

    public Refusal? Refusal { get; }

    public static CommandOutcome<T> Success(T value) => new(value, null);

    public static CommandOutcome<T> Refused(Refusal refusal) => new(default, refusal);
}

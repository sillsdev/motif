using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

/// <summary>What the registry knows about one refusal code, apart from the sentence a particular refusal carries.</summary>
/// <param name="Code">The stable code, one of the constants of the refusal-code classes.</param>
/// <param name="DefaultReason">The reason most of the code's call sites give; the refusal's own reason wins.</param>
/// <param name="Next">What to do about it, or <see langword="null"/> when no single step applies.</param>
public sealed record RefusalSpec(string Code, FailureReason DefaultReason, NextStep? Next)
{
    /// <summary>Whether the window can show this refusal, so it needs a sentence in the window's words.</summary>
    public bool ShownInWindow { get; init; }
}

/// <summary>
/// The step that satisfies a refusal's missing precondition. <see cref="Command"/> is a catalog command name, or
/// <see cref="RepeatCall"/> for "the same call again", or <see cref="AskPerson"/> for something only a person can do.
/// A front end renders it for its caller: a tool call over MCP, a <c>motif</c> line on the CLI, or a request to
/// the person when the caller cannot make the call.
/// </summary>
/// <param name="Command">A catalog command name, <see cref="RepeatCall"/> or <see cref="AskPerson"/>.</param>
/// <param name="Args">Where each argument's value comes from, keyed by the argument's wire name.</param>
public sealed record NextStep(string Command, IReadOnlyDictionary<string, NextArg> Args)
{
    /// <summary>The step is the call that was just refused, made again, possibly with an argument changed.</summary>
    public const string RepeatCall = "@repeat";

    /// <summary>The step is for the person; the refusal's Help text says what to ask.</summary>
    public const string AskPerson = "@person";

    /// <summary>Repeats the refused call with the given arguments set.</summary>
    public static NextStep Repeat(params (string Name, NextArg Arg)[] args) =>
        new(RepeatCall, args.ToDictionary(arg => arg.Name, arg => arg.Arg, StringComparer.Ordinal));

    /// <summary>A step only a person can take.</summary>
    public static NextStep PersonStep() => new(AskPerson, new Dictionary<string, NextArg>());
}

/// <summary>Where a next step's argument takes its value.</summary>
public enum NextArgSource
{
    /// <summary>A fact the refusal carries, named by <see cref="NextArg.Value"/>.</summary>
    Fact,

    /// <summary>The same-named argument of the call that was refused, named by <see cref="NextArg.Value"/>.</summary>
    Caller,

    /// <summary>The text of <see cref="NextArg.Value"/> itself.</summary>
    Literal,
}

/// <summary>One argument of a <see cref="NextStep"/>: <c>fact:name</c>, <c>caller:arg</c> or a literal.</summary>
/// <param name="Source">Which of the three sources supplies the value.</param>
/// <param name="Value">The fact name, the caller argument name, or the literal text.</param>
public sealed record NextArg(NextArgSource Source, string Value)
{
    /// <summary>A value read from the refusal's fact of this name.</summary>
    public static NextArg Fact(string name) => new(NextArgSource.Fact, name);

    /// <summary>A value read from the refused call's argument of this name.</summary>
    public static NextArg Caller(string name) => new(NextArgSource.Caller, name);

    /// <summary>A fixed value.</summary>
    public static NextArg Literal(string value) => new(NextArgSource.Literal, value);

    /// <summary>The value this argument takes, or <see langword="null"/> when its source does not have it.</summary>
    public string? Resolve(IReadOnlyDictionary<string, string> facts, IReadOnlyDictionary<string, string> callerArgs) =>
        Source switch
        {
            NextArgSource.Fact => facts.GetValueOrDefault(Value),
            NextArgSource.Caller => callerArgs.GetValueOrDefault(Value),
            _ => Value,
        };
}

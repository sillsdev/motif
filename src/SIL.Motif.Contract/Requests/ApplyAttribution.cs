namespace SIL.Motif.Contract.Requests;

/// <summary>The two Apply modes a Receipt records, as stored and shown.</summary>
public static class ApplyModes
{
    /// <summary>A person applied it: the default, and the only mode the window records.</summary>
    public const string Person = "person";

    /// <summary>An agent applied it under its own authority and must say why.</summary>
    public const string Autonomous = "autonomous";

    /// <summary>The environment variable that declares Autonomous mode when set to <c>1</c> or <c>true</c>.</summary>
    public const string EnvironmentVariable = "MOTIF_AUTONOMOUS";

    /// <summary>Whether <paramref name="mode"/> is one of the two modes.</summary>
    public static bool IsKnown(string? mode) => mode is Person or Autonomous;
}

/// <summary>Who applied and what they said, carried by every Apply request.</summary>
/// <param name="Mode"><see cref="ApplyModes.Person"/> or <see cref="ApplyModes.Autonomous"/>.</param>
/// <param name="Message">
/// The message the Receipt records. Required in Autonomous mode; in Person mode <see langword="null"/> means the
/// Proposal's label.
/// </param>
/// <param name="WriteNote">Whether Apply also writes one Notebook record in the same unit of work.</param>
public sealed record ApplyAttribution(
    string Mode = ApplyModes.Person, string? Message = null, bool WriteNote = false);

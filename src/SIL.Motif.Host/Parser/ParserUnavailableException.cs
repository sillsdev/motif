namespace SIL.Motif.Host.Parser;

/// <summary>
/// Raised by the assess route, which the shipped binary does not have, when the parser could not be run at
/// all. Every other launch reports the same condition as a <see cref="PanGloss.PanGlossOutcome.Unavailable"/>.
/// </summary>
public sealed class ParserUnavailableException : Exception
{
    public ParserUnavailableException(string message) : base(message) { }
}

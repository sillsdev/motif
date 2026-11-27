using System.Collections.ObjectModel;

namespace SIL.Motif.Contract.Responses;

/// <summary>
/// One <c>id convert</c> argument. A valid argument carries both spellings of its identity; an argument that is neither
/// spelling carries <see cref="Code"/> and <see cref="Message"/> and no ids.
/// </summary>
/// <param name="Input">The argument exactly as given.</param>
/// <param name="Guid">The hyphenated guid the project file stores.</param>
/// <param name="Canonical">The canonical id a Proposal names.</param>
/// <param name="Code">The refusal code for an argument that is not an id.</param>
/// <param name="Message">What is wrong with that argument.</param>
public sealed record IdConvertItem(string Input, string? Guid, string? Canonical, string? Code = null, string? Message = null);

/// <summary>The <c>id convert</c> report: one item per argument, in argument order. It serialises as a JSON array.</summary>
public sealed class IdConvertResponse(IList<IdConvertItem> items) : ReadOnlyCollection<IdConvertItem>(items)
{
    /// <summary>The code an argument that is neither a guid nor a canonical id carries.</summary>
    public const string InvalidIdCode = SIL.Motif.Contract.Commands.RefusalCodes.IdConvertInvalidId;

    /// <summary>True when at least one argument was not an id.</summary>
    public bool HasInvalid => this.Any(item => item.Code is not null);
}

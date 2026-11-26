namespace SIL.Motif.Contract.Commands;

/// <summary>
/// The <see cref="Refusal.Code"/> values the window reaches, so a caller branches on a name the compiler
/// checks rather than on a string it retypes.
/// </summary>
/// <remarks>
/// This class, <see cref="HostRefusalCodes"/> and the Parsimony codes together are the closed registry of
/// every refusal code, pinned by `RegistryCoversEveryCodeTheSourceUses`: a code the source spells that no
/// constant names fails the suite. Some call sites still spell their code as a literal. Codes shown in the window
/// must appear as a literal or a reference in the commands or the window, pinned by
/// `EveryCatalogueCodeIsOneACommandOrTheWindowDeclares`. The CLI does not read this list; it prints each
/// command's own message.
/// The constants live in `Refusals/RefusalCodes.&lt;Area&gt;.cs`, one file per code prefix, so a feature adding codes edits
/// its own area file; a new prefix is a new file.
/// </remarks>
public static partial class RefusalCodes
{
}

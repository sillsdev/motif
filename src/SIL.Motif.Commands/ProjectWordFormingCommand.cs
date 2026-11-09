using SIL.LCModel;
using SIL.LCModel.Core.WritingSystems;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.WritingSystems;

namespace SIL.Motif.Commands;

/// <summary>
/// Declares characters word-forming in an existing project's vernacular writing system, such as the digits of an
/// orthography that writes numerals inside words (<c>ika-14</c>).
/// </summary>
/// <remarks>
/// <para>
/// FieldWorks treats a character as part of a word when it is a letter, a mark or a modifier symbol, or when the
/// writing system's LDML <c>main</c> character set (its Valid Characters) lists it. Digits are none of the first
/// three, so a text that writes <c>ika-14</c> splits at the digits until the writing system lists them. This adds to
/// that set, the same one <c>project create --word-forming</c> fills.
/// </para>
/// <para>
/// This is the direct, human-only way to add word-forming characters when no Dry Run or Trial is wanted. It only
/// adds characters. <c>compose-set-character-class</c> (ADR 0096) is the Proposal way, and the only way to move a
/// character back to punctuation. The parser needs more than the writing system: each character a word can
/// contain must also be a phoneme grapheme, which
/// <c>compose-author-phoneme</c> authors in a Proposal.
/// </para>
/// </remarks>
public static class ProjectWordFormingCommand
{
    /// <summary>Adds the characters with the default loader.</summary>
    public static CommandOutcome<ProjectWordFormingResponse> Add(ProjectWordFormingRequest request) =>
        Add(request, new FwDataProjectLoader());

    /// <summary>Adds the characters with an explicit loader for controlled callers.</summary>
    public static CommandOutcome<ProjectWordFormingResponse> Add(ProjectWordFormingRequest request, FwDataProjectLoader loader)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(loader);
        if (string.IsNullOrWhiteSpace(request.ProjectPath))
            return Refused("project.invalid", FailureReason.InvalidArgument, "A project path is required.");
        if (string.IsNullOrEmpty(request.Characters))
            return Refused("project.invalid", FailureReason.InvalidArgument,
                "Name at least one character to declare word-forming, such as 0123456789.");
        if (!ProjectCreateCommand.TryReadWordForming(request.Characters, out var characters, out var bad))
            return Refused("project-create.invalid-word-forming", FailureReason.InvalidArgument,
                "Word-forming characters must be single, visible characters; whitespace and control characters " +
                "cannot form words.", ("character", bad));
        var path = Path.GetFullPath(request.ProjectPath);
        if (!File.Exists(path))
            return Refused("project.not-found", FailureReason.NotFound,
                $"Project file not found: '{Path.GetFileName(path)}'.", ("fwDataPath", path));

        LcmCache cache;
        try
        {
            cache = loader.LoadCache(path);
        }
        catch (LcmFileLockedException)
        {
            return Refused(RefusalCodes.ProjectInUse, FailureReason.Busy,
                ProjectStoreCommand.ProjectInUseMessage(path, "declare word-forming characters"), ("fwDataPath", path));
        }
        using (cache)
        {
            FwDataProjectLoader.RequireProjectPath(cache, path);
            var vernacular = cache.ServiceLocator.WritingSystems.DefaultVernacularWritingSystem;
            var added = AddTo(vernacular, characters);
            if (added.Count > 0)
            {
                cache.ServiceLocator.WritingSystemManager.Save();
                loader.Save(cache, path);
            }
            var all = vernacular.CharacterSets.TryGet("main", out var main) ? main.Characters.ToArray() : [];
            return CommandOutcome<ProjectWordFormingResponse>.Success(
                new ProjectWordFormingResponse(path, vernacular.LanguageTag, added, all));
        }
    }

    private static List<string> AddTo(CoreWritingSystemDefinition vernacular, IReadOnlyList<string> characters)
    {
        if (!vernacular.CharacterSets.TryGet("main", out var main))
        {
            main = new CharacterSetDefinition("main");
            vernacular.CharacterSets.Add(main);
        }
        var added = new List<string>();
        foreach (var character in characters)
        {
            if (main.Characters.Contains(character)) continue;
            main.Characters.Add(character);
            added.Add(character);
        }
        return added;
    }

    private static CommandOutcome<ProjectWordFormingResponse> Refused(
        string code, FailureReason reason, string message, params (string Key, string? Value)[] facts) =>
        CommandOutcome<ProjectWordFormingResponse>.Refused(new Refusal(code, reason, message,
            facts.Where(fact => fact.Value is not null).ToDictionary(fact => fact.Key, fact => fact.Value!, StringComparer.Ordinal)));
}

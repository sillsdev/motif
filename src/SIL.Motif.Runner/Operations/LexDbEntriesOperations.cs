using System;
using System.Linq;
using System.Runtime.CompilerServices;
using SIL.LCModel;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Model.Snapshot;

namespace SIL.Motif.Runner.Operations;

/// <summary>
/// Names the operation kind that creates a lexical entry. LibLCM keeps entries unowned and lists them through
/// the lexicon's virtual <c>Entries</c> property, so the model carries no owning field for the generator to read.
/// </summary>
/// <remarks>
/// The kind targets the project's lexicon and its closed <c>after</c> payload is <c>{}</c>. Its effect records
/// only the created entry's membership, never the whole lexicon, so a Dry Run over thousands of entries stays
/// small. An entry needs a lexeme form, a sense and grammatical info before the parser can use it; the
/// <c>AuthorLexicalEntry</c> composer authors all of them in one Proposal.
/// </remarks>
public static class LexDbEntriesOperationKinds
{
    public const string Create = "lexical/lexDb/createEntries";

    [ModuleInitializer]
    internal static void Register()
    {
        OperationKindRegistry.Register(Create);
        OperationHandlerRegistry.Register(Create, new OwningCreateHandler<ILexDb>(
            Create, SnapshotFields.LexDbEntries, owner => owner.Entries.Cast<ICmObject>().ToArray(),
            (cache, owner, guid, _, _) =>
            {
                if (owner != cache.LangProject.LexDbOA)
                    throw new InvalidOperationException("Entries are created only in this project's lexicon.");
                cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create(guid, owner);
            }, false, false, Array.Empty<string>(), "LexEntry"));
        OperationSlotRegistry.RegisterOwningCreate(Create, SnapshotFields.LexDbEntries, false);
    }
}

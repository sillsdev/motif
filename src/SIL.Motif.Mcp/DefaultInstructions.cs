namespace SIL.Motif.Mcp;

/// <summary>The server instructions a client shows the model when no profile supplies its own.</summary>
internal static class DefaultInstructions
{
    public const string Text = """
        Draft FieldWorks changes through Motif. Read motif_overview, motif_grammar and motif_lexicon; make one small Proposal; use motif_dry_run and motif_trial on attested forms and counterexamples. Ask one question from project evidence when two analyses remain possible; defer if evidence cannot decide. If requested forms are unattested, do not invent affixes, allomorphs or example words; ask for attested forms. Read encoding help with motif_guide. Motif cannot apply changes; the linguist reviews each Proposal.

        Start with motif_overview, then read before you write: motif_grammar and motif_lexicon give the ids you
        need. Work in small Proposals, one concept each (one affix, one rule), because a person must review every
        one. Start with motif_start_proposal and add changes with the motif_add_* tools. Finish the Draft with
        motif_finish_proposal, then use its Proposal id with motif_dry_run and motif_trial. Check that words that
        should NOT parse still do not.

        Every result ends with a next step, and every error says what to do instead. Ids are 22-character values;
        copy them exactly from a read result and never invent one. If a tool you need does not exist, say so to
        the linguist rather than working around it.
        """;
}

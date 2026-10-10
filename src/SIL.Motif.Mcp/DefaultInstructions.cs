namespace SIL.Motif.Mcp;

/// <summary>The server instructions a client shows the model when no profile supplies its own.</summary>
internal static class DefaultInstructions
{
    public const string Text = """
        Draft FieldWorks changes through Motif. Read motif_overview, motif_grammar and motif_lexicon; make one small Proposal; use motif_dry_run and motif_trial on attested forms and counterexamples. Ask one question from project evidence when two analyses remain possible; defer if evidence cannot decide. If requested forms are unattested, do not invent affixes, allomorphs or example words; ask for attested forms. Read encoding help with motif_guide. Motif cannot apply changes; the linguist reviews each Proposal.

        Start with motif_list_projects and pass the Known project's name or recorded path as project on each call.
        Read motif_overview, motif_grammar and motif_lexicon before drafting. Copy exact ids from their results.
        Make one small Proposal with motif_start_proposal and the matching semantic composer. Dry Run and Trial
        the Draft before Finalize. Read the ordered Trial summary and page motif_difference by category; unfinished
        cases prove no absence. Revise and repeat, then use motif_finalize_proposal for a person to review and Apply.

        Every result ends with a next step, and every error says what to do instead. Ids are 22-character values;
        copy them exactly from a read result and never invent one. If a tool you need does not exist, say so to
        the linguist rather than working around it.
        """;
}

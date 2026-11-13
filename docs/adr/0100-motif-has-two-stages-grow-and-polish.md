# 0100. Motif has two stages, Grow and Polish

**In plain terms:** Motif is used in two stages. Grow is simple local analysis that gets the text in front of you
working, one chapter at a time. Polish makes the grammar ready for everyone. People move back and forth between them as
the work needs.

**Status:** accepted, 2026-11-11 (owner).

## Context

SIL LangTech describes language-technology work as "stealth to wealth". A translator may never think about FieldWorks
and only later ask "can I get a dictionary out of this?". For FLExTrans the product is the grammar. Both kinds of user
need Proposals and GDRs.

## Decision

1. **Two stages, Grow and Polish.** Both have Proposals and GDRs.
2. **Grow** is simple local analysis to get the text at hand working: one chapter at a time, Errors only, a speed
   light, Proposals and GDRs.
3. **Polish** makes the grammar ready for everyone: deployment, FST, Warnings, speed targets, coverage and Parsimony,
   with Proposals and GDRs.
4. **Taglines.** Grow: "Find the Pattern in the Words". Polish: "Make It Ready for Everyone".
5. **People move between the stages**, for example when they add a new domain or adapt a published grammar to another
   dialect.
6. **The AI is required**, and the plugin points people to Claude Desktop more than to Motif.

## Words to avoid

- Send: collides with Paratext and FieldWorks Send/Receive.
- Draft: already a Proposal state.
- Review: already the name of Motif's review report pages.
- Check: clashes with checking, which is Errors and Warnings.
- Publish: suggests a one-way release.
- Grammar Sketch: a FieldWorks feature.
- Build, Deploy, Working, Shared, Explore, Release, Workshop, Production: each names one step or a state, not a stage.

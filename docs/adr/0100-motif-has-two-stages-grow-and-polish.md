# 0100. Motif has two stages, Grow and Polish

**In plain terms:** Motif is used in two stages. Grow is simple local analysis that gets the text in front of you
working, one chapter at a time. Polish makes the grammar ready for everyone. People move back and forth between them as
the work needs.

**Status:** accepted for the two stage names and the split, 2026-11-11 (owner, Phase 1 personas decisions 5, 6, 7, 9,
10, 11, 12, 15, 16 and 19). The taglines are proposed, owner to choose. "Mode" in decisions 11 and 15 and "stage" here
mean the same thing.

## Context

SIL LangTech describes language-technology work as "stealth to wealth" (decision 15). The translator never sees
FieldWorks: a team of translators may never think about it and only later ask "can I get a dictionary out of this?"
(decision 7). For FLExTrans the product is the grammar (decision 9). Both kinds of user need Proposals and GDRs.

## Decision

1. **Two stages, named Grow and Polish.** Both have Proposals and GDRs (decision 11).
2. **Grow** is simple local analysis to get the text at hand working: one chapter at a time, Errors only, a speed
   light, Proposals and GDRs. The Errors and speed light follow decisions 5 and 6, written for the translator.
3. **Polish** makes the grammar ready for everyone: deployment, FST, Warnings, speed targets and coverage (decision
   11), and Parsimony (decision 5: only in full Motif; placing it here is the drafter's reading). It also has
   Proposals and GDRs.
4. **People move back and forth** between the stages, for example when they add a new domain or adapt a published
   grammar to another dialect (decision 15). How switching works, and whether a stage is a setting or only what is
   shown, is still open.
5. **The AI is required** (decision 19). This is an owner decision about Motif as a whole, not a property of a stage.
   The plugin points people to Claude Desktop more than to Motif.
6. **Taglines are proposed, owner to choose.** Best pick:
   - Grow: "Find the Pattern in the Words" (decision 19 asks to play this up)
   - Polish: "Make It Ready for Everyone"

   They stay out of CONTEXT.md until chosen.

## Consequences

- The plugin shows Proposals and GDRs and can Apply; the simple path is Claude Desktop, Paratext 10 and the plugin,
  with Motif desktop never opened. Other real work happens in Motif or through Claude Desktop (decision 10).
- Texts are handled, current or later (decision 12). Whether these belong to the core, and how texts and corpora
  interact with them, is worked out separately (decision 16).

## Open questions

- How a person switches stage, and whether the stage is a setting.
- Whether Grow should avoid depending on Warnings, deployment or FST.

## Alternatives considered

Names considered while choosing. The reasons are the drafter's, apart from Send, which the owner raised (decision 11).

- **Build / Deploy.** Deploy describes one step of Polish, not the whole stage, and Build reads as compiling.
- **Working / Shared.** Reads as a state of the project or its access, and Shared suggests sharing rights.
- **Explore / Release.** Release implies a gate and a version number; Explore undersells Grow's daily use.
- **Workshop / Production.** Reads as test versus live and suggests that Grow output is throwaway.
- **Send.** Collides with Paratext and FieldWorks Send/Receive.
- **Draft.** Already a Proposal state (Draft, Ready, Deferred, Applied, Declined).
- **Review.** Already the name of Motif's review report pages.
- **Check, Publish.** Check clashes with checking (Errors and Warnings); Publish suggests a one-way release.
- **Grammar Sketch.** A FieldWorks feature (its Grammar Sketch export), so it would confuse FieldWorks users.

Tagline candidates, drafted, best pick first (owner to choose):

- Grow: "Find the Pattern in the Words"; "Let the Words Show You the Pattern"; "Start with the Words You Have".
- Polish: "Make It Ready for Everyone"; "From Working to Dependable"; "A Grammar Others Can Use".

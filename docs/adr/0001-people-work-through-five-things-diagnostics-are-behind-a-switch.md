# 0001. People work through five things; Diagnostics are behind a switch

**Status:** Accepted, 2026-11-14.

## Context

People need to understand their texts and grammar and decide whether a proposed change helps. The grammar's
machinery supports that work without becoming the way people must work.

## Decisions

### D1. Five things are the person's working surface

People work only through texts (**Sources**), approving analyses, the **Grammar Guide**
([ADR 0005](0005-the-grammar-guide.md)), **Claude** (reached through the MCP in Claude Desktop, not in the window), and **Proposals**. They do not work through FieldWorks rules themselves in Motif. Diagnostic links lead back to FieldWorks. A working grammar and experience with many corpora must
precede deciding whether general audiences need another mode.

### D2. Diagnostics are a per-user switch, off by default

Grammar warnings, Parsimony, speed by rule and parser trace are hidden until the person enables Diagnostics.
Both **View › Diagnostics** and the health-strip hover offer the switch. **Progress** shows per-Passage counts
of approved, guess and none; there is no Matrix.

### D3. The bottom health strip is always visible

It shows a green/yellow/red speed light measured in parser steps, never milliseconds; **N grammar warnings**;
parser updated time; and FieldWorks open/closed. The warning count remains visible with
Diagnostics off; diagnostic detail does not. The parser-step count appears **only in the speed light's hover**.
This hover is the single exception to the glossary rule against calling parser work “steps”; thresholds and
aggregation remain Open below.

### D4. Proposals connect the person and Claude

The desktop window supports several Proposals at once and comparing alternatives: doing one thing versus
another. “One Proposal at a time” applies only to motif-Bible, whose shown-update rule is in ADR 0025.
A Proposal's **Conversation** consists of Comments. Claude is not in the window: Motif's desktop window has no
Ask Claude, no Assistant Connections and no API key. People work with Claude only through the MCP in Claude
Desktop, where they name a thing to Claude and it creates a **Request**
([ADR 0008](0008-requests-how-a-person-asks-claude-for-a-proposal.md)). Proposal lifecycle and collaboration
follow [ADR 0007](0007-a-proposal-has-one-lifecycle-a-conversation-citations-and-one-apply-path.md).

### D5. Show the next decision as a plain row

The window has no tutorials, onboarding or explanatory paragraphs. It shows the next things a person can decide
as plain rows. One short sentence is allowed for a state, a reason (why something is disabled) or a consequence
(what Apply will change, the backup notice).

### D6. Purposes are persona lenses only

Scripture, academic and general audience inform design; they are never displayed or stored as product terms.

### D7. Safe, repeatable grammar experimentation is a product requirement

A person or assistant can author a Proposal, inspect its Dry Run, measure it through PanGloss, compare its
results and Apply or discard it. Iteration speed, evidence bound to the exact content and project state, and
non-committing experiments are requirements. Atomic Apply and its durable record make trying and discarding
changes safe. The grammar is the primary purpose; lexical support and other consumers remain in scope.
Parser-facing coverage is specified by ADR 0013, and project authority by
[ADR 0003](0003-motif-writes-only-what-fieldworks-can-create.md).

### D8. Sources and motif-Bible share this product direction

Sources are read in place under ADR 0024. motif-Bible is the Paratext 10 plugin and follows this direction
with fewer capabilities; its exact subset belongs to ADR 0025. This does not grant every desktop capability
to the plugin.

## Open

- Which aggregation and thresholds make the light green, yellow or red (Fast and Slow), and what appears before a
  measurement exists? The plan: once there is data, set them from the Cebuano, Swahili and Tamil step counts, with
  green at or below the median steps per word and red above the 95th percentile. Until then they stay Open. The
  hover placement and its one vocabulary exception are decided in D3.

## Consequences

Progress describes work on texts. The health strip supplies the always-visible indication of the machinery;
Diagnostics supplies detail on request. The Guide, Claude and Proposals connect the story of the grammar to
its changes. Sources, Passage identity and the plugin subset remain with their own decisions.

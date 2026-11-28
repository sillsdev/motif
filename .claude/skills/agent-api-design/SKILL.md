---
name: agent-api-design
description: >-
  Use when adding, changing, renaming or removing a Motif CLI verb, MCP tool, option, argument,
  refusal or output shape, or when deciding which MCP profile a tool belongs in. Holds the decisions
  (one CommandCatalog, a small default profile, no migrations and no legacy), the checklist for a
  new or changed surface, the call-log practice and the retirement procedure. Trigger on "add a
  verb", "new tool", "rename this option", "retire this command", "should this be in the default
  profile".
---

# Agent API design

Motif has one agent API: the CommandCatalog renders the CLI, the window and MCP
([ADR 0018](../../../docs/adr/0018-one-command-catalog-behind-the-cli-window-and-mcp.md),
[ADR 0009](../../../docs/adr/0009-assistants-reach-motif-through-one-agent-api.md)). Every surface
change goes through the rules below. The default profile stays small; the surface does not grow by
one-offs.

## Rules

1. **No migrations, no legacy.** Owner standing order, 2026-11-27. A retired or renamed verb, tool,
   option or field is deleted in the same PR. No alias, shim, deprecation note, compat fallback or
   "formerly" text. The PR body lists every removed name, and the grammar lanes get that list with
   the rig build.
2. **One catalog.** Define a command once in the CommandCatalog. Never add a CLI-only or MCP-only
   copy. The window is not a reason to keep a verb; the GUI follows the catalog.
3. **Default profile stays small.** A tool joins the default profile only with a reason and, after
   its first week, call-log evidence. Everything else is reached by `motif_find_composer` or a full
   profile.
4. **Merge before adding.** If an existing tool covers the job, extend or merge it.
5. **Refusals carry `next`** on both surfaces, naming the call that fixes the problem.
6. **Sources are read in place** ([ADR 0024](../../../docs/adr/0024-sources-are-read-in-place-and-scripture-stays-in-paratext.md)).
   A verb never copies a source into the store to make reading easier.

## Before you add or change a surface

Work the checklist in [references/checklist.md](references/checklist.md). Same PR: help text, golden
files, skills and profile updates.

## Measuring and retiring

- Call-log fields, settings and queries: [references/call-log.md](references/call-log.md).
- Removing a verb or tool cleanly: [references/retiring.md](references/retiring.md).
- Decisions with their ADR sources, and outside practice: [references/decisions.md](references/decisions.md).

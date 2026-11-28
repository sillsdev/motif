# Retiring or renaming a verb or tool

**No migrations, no legacy.** Owner standing order, 2026-11-27, for Motif and PanGloss. Delete the
old name in the same PR. Do not leave an alias, a shim, a deprecation note, a compat fallback or
"formerly X" text in the surface, Help, skills, comments or docs. The generated tool-list differences that
[ADR 0009](../../../../docs/adr/0009-assistants-reach-motif-through-one-agent-api.md) D11 puts in release notes are
allowed: they record what was removed, not a way to keep using it.

## Find candidates

Count calls per tool over the lane logs (the usage query in
[docs/call-log.md](../../../../docs/call-log.md)). A verb or tool with no calls over a week of real
work, and no named caller, is a candidate. The retirement decision for a surface the owner has
named is the owner's; ask before removing one.

## Remove it, all in one PR

1. The CommandCatalog row, its handler and any code only it used.
2. The CLI Help content under `src/SIL.Motif.Help/Content/` and the Help golden files in
   `tests/SIL.Motif.Tests.Cli/Golden/`.
3. The MCP tool registration and the MCP golden tool lists in `tests/SIL.Motif.Tests.Mcp/Golden/`.
4. Entries in `profiles/default.json` and any other profile.
5. Skills and docs that name it (`plugin/skills/`, `.claude/skills/`, `docs/`). Build validation
   rejects unresolved tools, verbs and Help links
   ([ADR 0009](../../../../docs/adr/0009-assistants-reach-motif-through-one-agent-api.md) D10).
6. Tests that exercise it. Delete them; do not skip them.
7. Search the repo for the old name and delete every remaining mention.

Renames follow the same rule: the new name replaces the old one and the old one is gone. Stored
Proposals name Composers only as provenance, so retiring a Composer needs no data migration
([ADR 0009](../../../../docs/adr/0009-assistants-reach-motif-through-one-agent-api.md) D11).

## Tell the lanes

The PR body lists every removed or renamed name under a heading such as "Removed". After merge, the
list goes to the grammar lanes together with the READY rig build they switch to at an item
boundary. Do not describe the removal as a deprecation.

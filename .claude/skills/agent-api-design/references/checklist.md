# Checklist for a new or changed verb or tool

Answer each item in the PR description.

1. **Does an existing tool cover it?** Search the CommandCatalog (`src/SIL.Motif.Commands/`) and
   the MCP tools (`src/SIL.Motif.Mcp/`). Merge into the existing tool with an argument rather than
   adding a sibling.
2. **Define it once, in the catalog.** The CLI, window and MCP renderings come from that row
   ([ADR 0018](../../../../docs/adr/0018-one-command-catalog-behind-the-cli-window-and-mcp.md),
   [ADR 0009](../../../../docs/adr/0009-assistants-reach-motif-through-one-agent-api.md) D1).
3. **Name it by the convention.** MCP tools are `motif_<verb>` or `motif_<verb>_<noun>`, arguments
   are camelCase, and `proposal` and `project` mean the same thing everywhere. Return IDs exactly,
   with prefixes. `detail` is concise by default or detailed. A response above about 4000 tokens
   pages rather than inlining
   ([ADR 0009](../../../../docs/adr/0009-assistants-reach-motif-through-one-agent-api.md) D9).
   Epic #572 item #567 fixes the wider naming and paging convention; follow it as it lands.
4. **Failure contract.** Every refusal uses a code from the closed registry, names the missing
   precondition and the call that satisfies it in the caller's rendering, and puts `next` on both
   the CLI and MCP. A read that cannot look refuses with why; it never returns zeros or an empty
   list as if it looked (ADR 0009 D9; epic #572 item #568 for the CLI side).
5. **Help, goldens and skills in the same PR.** Update the Help content under
   `src/SIL.Motif.Help/Content/`, the Help golden files in
   `tests/SIL.Motif.Tests.Cli/Golden/`, the MCP golden tool lists in
   `tests/SIL.Motif.Tests.Mcp/Golden/`, and any skill that names the verb. Skill command material is
   generated and playbooks are authored once
   ([ADR 0009](../../../../docs/adr/0009-assistants-reach-motif-through-one-agent-api.md) D10).
6. **Which profile, and why.** The profile source is `profiles/default.json`, embedded by
   `src/SIL.Motif.Mcp/SIL.Motif.Mcp.csproj` and read by `src/SIL.Motif.Mcp/ToolProfile.cs`. The
   default holds the few tools an agent needs every session
   ([ADR 0009](../../../../docs/adr/0009-assistants-reach-motif-through-one-agent-api.md) D2).
   Say why this tool is always loaded, or leave it to `motif_find_composer` and the full profile.
7. **Do not keep it for the GUI.** If only the window uses it, it is not a reason to add or keep a
   verb.
8. **Renaming or removing something?** Follow [retiring.md](retiring.md); the same PR deletes the
   old name.

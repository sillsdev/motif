# Decisions and outside practice

## Decisions

| Decision | Source |
|---|---|
| One CommandCatalog renders the CLI, the window and MCP | [ADR 0018](../../../../docs/adr/0018-one-command-catalog-behind-the-cli-window-and-mcp.md); [ADR 0009](../../../../docs/adr/0009-assistants-reach-motif-through-one-agent-api.md) D1 |
| The default profile has seven always-loaded tools; the rest are found by goal or in the full profile | ADR 0009 D2 and D7; epic #572 items #565 and #570 |
| Responses use consistent names, page when large, and refuse with an actionable next step | ADR 0009 D9 |
| Command material in skills is generated; playbooks are authored once | ADR 0009 D10 |
| Surface changes carry no aliases or deprecated arguments, and use is measured locally | ADR 0009 D11 |
| Sources are read in place, and scripture stays in Paratext | [ADR 0024](../../../../docs/adr/0024-sources-are-read-in-place-and-scripture-stays-in-paratext.md) |
| No migrations, no legacy; delete old names in the same PR | owner standing order, 2026-11-27 |
| The window is paused and is not a reason to keep a verb | owner, 2026-11-27, epic #572 |

The epic is sillsdev/motif#572; this skill is #579.

## Outside practice

Sources collected for the API review in epic #572:

- Consolidate tools and avoid overlap, as in Anthropic's "Writing tools for agents"
  (anthropic.com/engineering/writing-tools-for-agents).
- Tool selection degrades past roughly 30 to 50 tools; tool search raised accuracy from 49% to 74%
  (anthropic.com/engineering/advanced-tool-use).
- Return actionable `isError` results, per the MCP specification 2025-06-18 tools page.
- Log tool calls, as OpenTelemetry's `execute_tool` span does.
- Shape responses for the agent: concise by default, identifiers it can reuse, paging for large
  results (OpenAI's function-calling guide; arXiv 2505.03275).

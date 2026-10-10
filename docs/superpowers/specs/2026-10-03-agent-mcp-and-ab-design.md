# Agents work on Motif through one MCP server, and we measure every design choice

**In plain terms:** a linguist will open Claude Desktop (or the ChatGPT/Codex desktop app), install Motif's
extension, and ask an AI to build or fix their grammar. The AI works through Motif's MCP server; every change
it makes lands in Motif as a Proposal that the linguist reviews and applies in Motif's window. There is no
chat inside Motif. About 90% of these users are not technical, so the MCP server, not the CLI, is the one
agent interface we design. Because we will keep asking "is way A or way B better for the agent?", we also
build an A/B harness that runs two AI agents on the same problems, one per way, and compares the results.

Owner decisions (2026-10-03), binding on every lane:

1. **One agent interface: MCP.** The CLI stays for developers and scripts, but nothing is designed for agents
   first in the CLI. The MCP server is a third front end over the in-process command catalog (ADR 0043), plus
   whatever read access to the project data the catalog lacks today. "The CLI is a pittance compared to the
   database behind it": the MCP surface is allowed (expected) to go beyond today's verbs.
2. **Local, stdio, no app hosting.** `motif mcp` is a subcommand of the existing CLI binary, spoken over
   stdio, packaged later as a Claude Desktop `.mcpb` and a Codex `config.toml` entry. It works whether or
   not the Motif window is open; the per-project database is the bridge to the window (ADR 0040 decision 4).
3. **No chat in Motif's UI.** The window shows Proposals an agent made; it never hosts a conversation.
4. **The agent drafts, only a human applies.** No apply, discard, store-delete or developer-only command is
   ever registered as an MCP tool. Enforced structurally (the tool never exists) and tested.
5. **Layer 1 only (ADR 0029).** No generic "set any field" tool in the product. A missing composer is a
   requirement: write it. (An A/B *arm* may test a broader write tool to measure the cost of the rule; such an
   arm never ships.)

## Research that shaped this (summary; sources in the 2026-10-03 conversation)

- Claude Desktop's no-terminal routes (chat, Cowork) can only reach a local tool through MCP; `.mcpb` is a
  one-click install. Third-party apps may not use a user's Claude subscription, so Motif hosting the agent
  loop is out.
- Published CLI-vs-MCP gaps came from schema bloat (40–90 tools), remote-server timeouts, and lack of
  composition on analysis tasks. A local stdio server with ~10–15 curated tools avoids the first two; give
  analysis tools filter/sort/limit and a `concise|detailed` switch to address the third.
- Tool design (Anthropic, "Writing effective tools for agents"): workflow-shaped tools, namespaced names,
  actionable errors, human-meaningful handles not GUIDs, descriptions written as onboarding, pagination and
  truncation with steering messages, iterate by evaluation.
- Agents edit text badly and typed calls well; review/history matters to the agent too. So tools are typed,
  and each tool result says what changed and what to do next. A `motif_activity` / history read gives the
  agent "what have I done so far".
- Grammar authoring by LLMs is an iterate-on-feedback loop: draft → trial on a sample → read failures →
  revise; full assessment before finalizing. Coverage alone is gameable: score held-out words, words that
  must NOT parse, and parsimony. Phonology is unexplored ground.
- Reviewers rubber-stamp over time: keep Proposals small and per-concept, regressions first.

## Architecture

```
SIL.Motif.Commands   CommandCatalog (single source) + agent metadata per command
        |  in-process dispatch (same as CLI and window)
        +-- SIL.Motif.Cli    verbs, --json, and `motif mcp` (stdio)
        +-- SIL.Motif.Mcp    tool factory over the catalog, refusal→isError mapper, data-read tools,
        |                    tool profiles, golden tools/list snapshot
        +-- SIL.Motif.App    shows agent-made Proposals (origin, activity) — later lane
SQLite per project: proposals, jobs, (later) agent_activity, ui_context
```

- **Agent metadata on the catalog.** Each descriptor gains an agent-facing name, a class
  (`Read | Draft | Evaluate | HumanOnly`), and a hint. `HumanOnly` never becomes a tool. Descriptions come
  from Help `commands.json` and `[Description]` on request records, so Help and tools never drift.
- **Refusals are tool errors.** A typed Refusal becomes `isError: true` with
  `"<code>: <sentence>\n<facts>\nNext: <hint>"` plus structured content.
- **Long jobs** (dry-run, trial, assess) stay Motif jobs: start → status → bounded wait with progress.
  Do not depend on the experimental MCP Tasks extension.
- **Tool profiles are the A/B lever.** `motif mcp --profile <file.json>` selects which tools exist, their
  names, descriptions, grouping (fine vs coarse), result verbosity defaults, and server instructions. The
  default profile ships; other profiles are experiments. An A/B question that is "same server, different
  surface" must be expressible as two profile files, no code change. A question needing code is two git refs
  of the server.
- **Isolation.** The server honours the same per-process environment the tests use
  (`MOTIF_WORKER_ROOT`, `MOTIF_RUNNER_NAMESPACE`, `MOTIF_WRITING_SYSTEM_REPOSITORY_PATH`, SLDR cache) so
  many trials run at once.
- **C# MCP SDK** (`ModelContextProtocol`, 1.x). Pin the version in central package management. Logs to stderr
  only; stdout is the protocol.

## Contracts between lanes

1. **Server launch:** `motif mcp --project <.fwdata path or Known project name> [--profile <file>]
   [--activity-log <file.jsonl>]`. The activity log records every tool call (name, args, result size,
   isError, duration) for the harness.
2. **Eval set layout** (`sillsdev/motif-test-grammars`, `evals/sets/<set-id>/` within that checkout):
   - Motif pins a tag and commit in `evals/test-grammars.lock.json`; `MOTIF_TEST_GRAMMARS` may name a local pinned checkout.
   - `language/` — the generator's input for one synthetic language (seed, phonology, affixes, rules) and its
     gold grammar.
   - `project/` is NOT committed; `evals/tools` builds it on demand into a temp root.
   - `words/train.txt`, `words/heldout.txt`, `words/negative.txt`, `gold/analyses.jsonl`
     (`{"word": ..., "analyses": [[morph, gloss]...]}`).
   - `tasks/<task-id>/task.yaml`:
     ```yaml
     id: t0-build-noun-plural
     family: build | edit | diagnose | lexicon | safety
     tier: T0 | T1 | T2 | T3 | T4
     set: <set-id>
     start: empty-grammar | gold-grammar | gold-minus:<defect-id>
     prompt: prompt.md            # identical across arms; written as a linguist would ask
     limits: { turns: 60, wall_seconds: 1800 }
     graders:
       - { type: coverage, words: heldout, weight: 0.5, pass: 0.8 }
       - { type: negatives, weight: 0.2, pass: 0.95 }
       - { type: parsimony, weight: 0.1 }
       - { type: answer, key: answer.yaml, weight: 0.2 }   # diagnose tasks
       - { type: safety }                                    # no forbidden attempts; hard fail
     ```
3. **Seed builder:** `dotnet run --project evals/tools/... -- build --set <dir> --start <start> --out <dir>`
   writes a `.fwdata` (+ writing systems) and prints its path as JSON. Grammar authoring follows the
   `fieldworks-grammar-authoring` skill.
4. **Arm file** (`evals/arms/<arm>.yaml`):
   ```yaml
   id: mcp-default
   host: codex            # codex | claude | fake
   model: gpt-6-luna      # host-specific; luna is the default agent
   effort: high
   server: { ref: HEAD, profile: profiles/default.json }   # or an absolute build path
   system_append: null    # optional extra instructions file
   ```
5. **A/B request** (`evals/questions/<q-id>.yaml`): `question`, `arm_a`, `arm_b`, `tasks` (glob or list),
   `trials` (default 3), and the primary metric. The runner writes `evals/results/<q-id>/<run-id>/` with
   per-trial `manifest.json`, `transcript.jsonl`, `activity.jsonl`, `proposals.json`, `grade.json`, and a
   `report.md` comparing the arms (paired per task, pass^k, CIs, cost, tool errors, review burden, and a
   transcript-excerpt section for failures). `evals/results/` is git-ignored.

## Lanes

| Lane | Worktree (WSL) | Worker | Delivers |
|---|---|---|---|
| `mcp` | `feat/agent-mcp` | Sonnet (Claude Code) | Inventory of every agent-relevant capability (catalog, Layer 0/1, projections, PanGloss reads); the v0 tool surface; `SIL.Motif.Mcp` + `motif mcp`; profiles; tests |
| `ab` | `feat/ab-harness` | luna-6 | `evals/` runner, hosts (`codex exec`, `claude -p`, `fake`), graders, report, docs |
| `sets` | `feat/eval-sets` | luna-6 | Synthetic-language generator, gold, 2 languages (T0, T1) and ≥12 tasks, then T2–T3 |

Integration order: sets and mcp are independent; ab builds against a stub server and a toy set first, then
switches to the real ones. Live trials (real models) run from the integrator, outside the jail: the jail has
no route to model APIs. Every harness path must therefore also run end to end with `host: fake`, which
replays a scripted sequence of MCP calls.

## Not in this round

`.mcpb` packaging, the window's agent badge and `ui_context`, MCP Apps, elicitation, and any change to the
product's apply path.

# Agent edit loop implementation plan

An assistant can measure a Draft, revise it, and Finalize it for a person. Evidence stays useful only while both the content and project state match.

> Execute inline with executing-plans. Implementation and review stay in this session, without subagents.

**Goal:** Implement ADR 0058 and the edit-loop lane brief.

**Architecture:** The command catalog determines eligible tools. Existing semantic handlers remain the execution boundary; MCP adds bounded presentation and Known-project routing. Evidence binds immutable content to a Baseline.

**Tech stack:** C# / .NET 10, MCP, SQLite, LibLCM, PowerShell.

**Spec:** `docs/adr/0058-the-agent-edit-loop.md` and the amendment to `docs/superpowers/specs/2026-10-03-agent-mcp-and-ab-design.md`.

## Global constraints

Use build.ps1/test.ps1 with compiler reuse disabled. No migrations, human-only tools, project registration through MCP, or live model runs. Work and commit only in the supplied worktree. Preserve the binding glossary.

## Review focus

Unknown or ambiguous project selectors refuse before handler invocation. Editing during measurement cannot bind older evidence to new content. Parser-incomplete words never count as lost positives. Finalize preserves only matching evidence. Applying any Proposal invalidates evidence against the old project state.

### Task 1: Catalog and MCP routing

Tools follow catalog surface and agent class, carry generated Help and request documentation, use Finalize vocabulary, and receive profile detail defaults.

- [x] Pin eligible command and HumanOnly exclusions in ToolSurfaceTests.
- [x] Replace hand selection with catalog-derived eligibility and typed adapters; complete Help text.
- [x] Add Known-project routing and list_projects; start with no tools when Advanced AI mode is off.
- [x] Verify MCP tests with `pwsh ./test.ps1 -Project SIL.Motif.Tests.Mcp`; commit.

### Task 2: Evidence lifecycle

A Draft can be measured repeatedly without handing it over, and evidence always says which content and project it describes.

- [x] Pin Draft Dry Run, edits during jobs, unchanged Finalize and Apply Drift.
- [x] Change JobCommands and worker DryRun bindings; refuse unsupported stored shapes.
- [x] Verify Commands and Worker tests through test.ps1; commit.

### Task 3: Trial results

An assistant reads the most consequential changes first, then pages through the complete Difference.

- [x] Pin ordered summary, unfinished cases, paged categories and assess without a Proposal.
- [x] Add Trial result projection and Difference reader over stored Assessments.
- [x] Update workflow resources and pin every named tool; verify MCP tests; commit.

### Task 4: Parsimony and waits

Parsimony findings use reproducible evidence digests, and recovery waits follow progress on slower machines.

- [x] Pin one versioned preimage per measure and agreement among evidence consumers.
- [x] Replace fixed recovery deadlines with task/process progress waits.
- [x] Verify relevant tests through test.ps1; commit.

### Task 5: Completion

The developer gate and a final review establish that the full edit loop works with fixtures.

- [x] Run `pwsh ./test.ps1` with required sandbox environment.
- [x] Review the complete diff against ADR 0058 and fix material findings.
- [x] Record commands, skips, commits, eval rename paths and remaining limitations in the lane report.

Final developer gate on source `6e87159eae798fc5ec8bace924abbd989461533e`: exit 0, 5,699 passed,
zero failed, 35 skipped. Build, comment and token gates, offline restore and artifact-plan checks passed.
The lane report records the exact command, skip reasons and remaining Release/System validation.
Review was a self-review under the explicit instruction to keep all work in this session.

# Documentation writing notes

**In plain terms:** these pages describe the Motif window and CLI as they work in this worktree, so they avoid promising controls or commands that are only planned. The current implementation and the content plan differ in a few places that should be resolved before the docs site is published.

## Page index

### Guide

- Get started: [What Motif does](../../../help/en/guide/what-is-motif.md), [Installing Motif](../../../help/en/guide/install.md), [Opening a project](../../../help/en/guide/open-a-project.md), [Choosing what to measure](../../../help/en/guide/first-run-setup.md), [Reading the Overview](../../../help/en/guide/reading-the-overview.md)
- Window pages: [Overview](../../../help/en/guide/overview.md), [Texts](../../../help/en/guide/texts.md), [Try a Word](../../../help/en/guide/try-a-word.md), [Timing](../../../help/en/guide/timing.md), [Warnings](../../../help/en/guide/warnings.md), [Review changes](../../../help/en/guide/review-changes.md), [AI Handoff](../../../help/en/guide/ai-handoff.md)
- Everyday tasks: [Refresh numbers](../../../help/en/guide/refresh-numbers.md), [Change an analysis](../../../help/en/guide/change-an-analysis.md), [Replace a pending change](../../../help/en/guide/replace-a-pending-change.md), [Apply changes](../../../help/en/guide/apply-to-fieldworks.md), [When a change no longer fits](../../../help/en/guide/when-a-change-no-longer-fits.md), [Switch projects](../../../help/en/guide/switch-projects.md), [Cancel a long run](../../../help/en/guide/cancel-a-long-run.md), [When something goes wrong](../../../help/en/guide/when-something-goes-wrong.md)
- Concepts: [Baseline](../../../help/en/guide/baseline.md), [Assessment](../../../help/en/guide/assessment.md), [Text Coverage](../../../help/en/guide/text-coverage.md), [Default Selection](../../../help/en/guide/default-selection.md), [Drift](../../../help/en/guide/drift.md), [Pending changes](../../../help/en/guide/pending-changes.md)

### For AI agents and scripts

- [Start here](../../../help/en/guide/agents/start-here.md)
- [Output and exit codes](../../../help/en/guide/agents/output-and-exit-codes.md)
- [Measure a grammar](../../../help/en/guide/agents/measure-a-grammar.md)
- [Work with jobs](../../../help/en/guide/agents/work-with-jobs.md)
- [Read a Handoff](../../../help/en/guide/agents/handoff.md)

## Gaps and source limitations

- The repository does not define an end-user Motif installer or download steps. It also does not document how a linguist obtains or installs PanGloss, or the installed application’s user-data folder. The Install page intentionally leaves those details to the package maintainer.
- The current CLI has no `help` verb and does not implement `motif help --all --json`; it prints its command catalog when invoked without arguments. No generated `llms.txt` is present. The start page points agents to the current usage/catalog surface and marks the planned help export as unavailable.
- `--json` is command-specific, not available on every command. Enqueue-only successes can be a bare job ID, and an unexpected top-level exception still prints plain text with exit code 4. The output page records those exceptions.
- The current `docs/cli-api.md` Handoff section and parts of its Assessment description do not match the code. The Handoff implementation requires `--invocation <id>` or `--no-assess`, exports the current five-file format (four files with `--no-assess`), and ignores the catalogued `--flextext` option. Current Assessments can include Correctness in addition to ParseTime and ObjectTiming. The agent pages follow the code and tests; the CLI API guide needs its own correction.
- The Timing page has a **Slowest** word-set choice and a slowest-words list, but no explicit “sort by time” control. The content plan’s `timing-slow-words` description should be revised to match those controls.
- Applying changes displays a receipt after success; the current window has no separate Apply confirmation dialog. The `apply-to-fieldworks` walkthrough description should say “Apply and read the receipt.”
- First-time setup follows a successful Baseline capture; opening a project does not itself capture or assess. FieldWorks does not need to be closed just to open or read a project, though the Baseline reflects the last saved file and Apply is blocked while FieldWorks holds the project. The open-project outline should separate those conditions.
- The Warnings page has no Cancel action during **Check the grammar**. The generic cancel guide names the cancellable actions separately and records this limit.
- The concepts outline includes “pending changes,” but that phrase is not a standalone glossary term and the requested allowed `term:` codes do not include it. The page uses the window’s wording and links that phrase to the closest available `term:` code, `proposal`.

## Walkthrough placeholders

The `shot:` links in the Guide use the walkthrough IDs from the content plan. Those scripts, step IDs, screenshots, and clips do not exist yet; the short step IDs are placeholders selected for these pages.

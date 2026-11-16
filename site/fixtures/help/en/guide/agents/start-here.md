# Using Motif from an agent

Call Motif as `motif <verb> ...`. Each process handles one command; durable project state is stored between calls, so commands do not depend on a setup command in the same process. Use an explicit project path with commands that accept `--project` or take the `.fwdata` path as a positional argument.

Prefer `--json` when the command’s usage line supports it, and read the response from stdout. Do not parse the human text rendering. For example:

```powershell
motif overview --project "C:\Projects\Sena.fwdata" --json
motif assess "C:\Projects\Sena.fwdata" --all-wordforms --json
motif jobs list --json
```

To list help entries in one call, run `motif help --all --json`. The JSON has a `locale`, a `siteRoot`, and `entries` for available CLI commands, glossary terms and Guide pages. Each entry has a kind, code, slug, title, description, a `helpPage` field containing the full Help page Markdown when a page exists, and a URL; command entries also include usage lines and a surface. For one entry, `motif help <code>` prints its title, description, URL, and (for a command) usage. Add `--full` to print the expanded page, or `--json` to get that entry's metadata, including its full Help page when available. The documentation website also publishes `llms.txt`, a plain-text index of every page. With no arguments, `motif` prints the usage catalog to stderr and exits with code 1.

Assistants need Advanced AI mode to use MCP tools. In the window, open **AI Assistants**, select **Let Assistants Use Motif's Tools**, then choose which assistants to connect. The **AI Assistants** Guide page explains the client steps. A person can also enable it with `motif settings advanced-ai on`. The MCP server starts with `motif mcp`; each project tool takes a Known project name or recorded path in its `project` argument. Use the workflow resource to draft, Dry Run, Trial, revise and Finalize for a person to review and Apply. Most commands run synchronously, but `baseline-refresh`, `dry-run`, and `trial` enqueue jobs. See **Working with jobs**. For errors and retry decisions, see **Output and exit codes**.

Before composing rules, read the encoding guide for the phenomenon: `motif help` lists the guides, and `motif help guide:encoding/workflow --full` shows the working order. Over MCP the same text is the `motif_guide` tool.

A completed Trial has a saved report. Read its summary and limitations, then use failures or full detail to page through the cases needed for review. In MCP, `motif_trial` reads and refreshes reports explicitly, and `motif_difference` reads a particular report or its cases; a trace is an explicit diagnostic. No red flags means only not bad, never good.

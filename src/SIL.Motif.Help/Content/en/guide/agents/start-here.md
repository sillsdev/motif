# Using Motif from an agent

Call Motif as `motif <verb> ...`. Each process handles one command; durable project state is stored between calls, so commands do not depend on a setup command in the same process. Use an explicit project path with commands that accept `--project` or take the `.fwdata` path as a positional argument.

Prefer `--json` when the command’s usage line supports it, and read the response from stdout. Do not parse the human text rendering. For example:

```powershell
motif overview --project "C:\Projects\Sena.fwdata" --json
motif assess "C:\Projects\Sena.fwdata" --all-wordforms --json
motif jobs list --all --json
```

To list help entries in one call, run `motif help --all --json`. The JSON has a `locale`, a `siteRoot`, and `entries` for Released commands and glossary terms. Each entry has a code, slug, title, description, a `helpPage` field containing the full Help page Markdown when a page exists, and a URL; command entries also include usage lines and a surface. For one entry, `motif help <code>` prints its title, description, URL, and (for a command) usage. Add `--full` to print the expanded page, or `--json` to get that entry's metadata, including its full Help page when available. The documentation website also publishes `llms.txt`, a plain-text index of every page. With no arguments, `motif` prints the usage catalog to stderr and exits with code 1.

Developer commands are not enabled by default. To use `dry-run` or `trial` in a development environment, set `MOTIF_DEVELOPER_COMMANDS` to exactly `1` before starting Motif. Most commands run synchronously, but `baseline-refresh`, `dry-run`, and `trial` enqueue jobs. See **Working with jobs**. For errors and retry decisions, see **Output and exit codes**.

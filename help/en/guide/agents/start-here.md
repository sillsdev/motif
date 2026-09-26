# Using Motif from an agent

Call Motif as `motif <verb> ...`. Each process handles one command; durable project state is stored between calls, so commands do not depend on a setup command in the same process. Use an explicit project path with commands that accept `--project` or take the `.fwdata` path as a positional argument.

Prefer `--json` when the command’s usage line supports it, and read the response from stdout. Do not parse the human text rendering. For example:

```powershell
motif overview --project "C:\Projects\Sena.fwdata" --json
motif assess "C:\Projects\Sena.fwdata" --all-wordforms --json
motif jobs list --all --json
```

The current CLI does not implement a `help` verb or `motif help --all --json`; with no arguments it prints the usage catalog to stderr and exits with code 1. The planned `llms.txt` index is also not generated in this worktree. For current syntax, use each command’s usage line and the command catalog; the checked-in API guide has known areas of drift.

Developer commands are not enabled by default. To use `dry-run` or `trial` in a development environment, set `MOTIF_DEVELOPER_COMMANDS` to exactly `1` before starting Motif. Most commands run synchronously, but `baseline-refresh`, `dry-run`, and `trial` enqueue jobs. See **Working with jobs**. For errors and retry decisions, see **Output and exit codes**.

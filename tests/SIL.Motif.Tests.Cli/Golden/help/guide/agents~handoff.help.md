# Reading a Handoff

Start with `handoff.md` for the included files and reading examples; use the separate `pastedHeader` text for the prepared chat prompt.

An assessed Handoff lists files alphabetically: `grammar.json`, `handoff.md`, `parse-results.json`, `read_results.py`, then `texts.json`. A Baseline-only Handoff omits `parse-results.json`. The App's one-word Handoff contains `grammar.json`, `handoff.md`, `read_results.py`, `texts.json`, then `traces/<word>.trace.json`; it carries the raw diagnostic and omits batch parse results.

| File | Description |
|---|---|
| `grammar.json` | The grammar Motif exports for PanGloss to parse. |
| `handoff.md` | Explains the included material and gives reading examples. |
| `parse-results.json` | One result for each selected word, including its parser outcome and timing. |
| `read_results.py` | Queries the Handoff's JSON files from a terminal. |
| `texts.json` | The selected Texts with the analyses the project stores. |
| `traces/<word>.trace.json` | The raw diagnostic for the selected word, including parser fields Motif does not interpret. |

Read the files as linguistic project data; they may contain real grammar rules, lexicon entries, and text.

Use the helper’s own help to see its commands, then ask focused questions against the supplied files:

```powershell
python read_results.py --help
python read_results.py word --help
```

For an assessed folder, the current CLI exports a retained Assessment invocation rather than silently running a fresh one. To create one, first run [assess](cmd:assess) with `--json`, read `invocationId` from its response, then pass that value to [handoff](cmd:handoff):

```powershell
motif handoff "C:\Projects\Sena.fwdata" --out "C:\handoffs\Sena" --invocation <invocationId> --json
```

The successful JSON response reports `outputDirectory`, `baseline`, `selection`, `files`, `assessmentIds`, optional `invocationId`, `pastedHeader`, and `handoffMarkdown`. Use `--no-assess` for a folder without `parse-results.json`; `--texts <guid,guid>` is accepted only with that option, to select texts.

Treat every file as potentially sensitive and share it only with a service approved for that project.

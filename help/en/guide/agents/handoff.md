# Reading a Handoff

A Motif Handoff is a folder for an agent to inspect without connecting to the project or running Motif. Start with `handoff.md`: it explains the included material and contains the question prepared for the chat model.

The folder includes `grammar.json`, `texts.json`, a Python reader named `parse_grammar_texts_assessment.py`, and the handoff notes. When the Handoff includes an Assessment, it also contains `assessment.json`. Read the files as linguistic project data; they may contain real grammar rules, lexicon entries, and text.

Use the helper’s own help to see its commands, then ask focused questions against the supplied files:

```powershell
python parse_grammar_texts_assessment.py --help
python parse_grammar_texts_assessment.py word --help
```

The current CLI exports a retained Assessment invocation rather than silently running a fresh one. To create an assessed folder, first run [assess](cmd:assess) with `--json`, read `invocationId` from its response, then pass that value to [handoff](cmd:handoff):

```powershell
motif handoff "C:\Projects\Sena.fwdata" --out "C:\handoffs\Sena" --invocation <invocationId> --json
```

The successful JSON response reports `outputDirectory`, `baseline`, `selection`, `files`, `assessmentIds`, optional `invocationId`, `pastedHeader`, and `handoffMarkdown`. Use `--no-assess` for a folder without `assessment.json`; `--texts <guid,guid>` can be used with that option to select texts.

Treat every file as potentially sensitive and share it only with a service approved for that project.

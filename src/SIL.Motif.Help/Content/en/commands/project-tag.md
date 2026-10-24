# Read a project tag

Read the tag Motif stores for this project on this machine. For a project without a tag, Motif uses its vernacular language code when that code is available. If the code is already used, Motif tries the exact folder name and refuses if that name is invalid or taken.

```text
motif project tag --project <fwdata>
```

Use `--json` to receive the tag as structured output. If Motif cannot choose a valid, unused tag, choose one with `project tag --set`.

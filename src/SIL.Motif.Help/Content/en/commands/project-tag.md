# Read a project tag

Read the tag Motif stores for this project on this machine. For a project without a tag, Motif uses its vernacular language code when that code is available. If the code is already used, Motif uses the folder name (lowercased, other characters turned into '-', cut to 10 characters, and numbered when that clashes) and refuses if the folder name is a valid tag already taken or yields no valid tag.

```text
motif project tag --project <fwdata>
```

Use `--json` to receive the tag as structured output. If Motif cannot choose a valid, unused tag, choose one with `project tag --set`.

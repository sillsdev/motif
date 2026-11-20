# Clear word type dispositions

`wordform undispose` removes the standing disposition of each word type you name, so the type is analysed like any other word again. The removed disposition is not deleted: it stays in the type's history with who cleared it, when, and the reason.

## Example

```text
motif wordform undispose Nairobi Mombasa --project <fwdata> --reason "attested as a common noun in the Gazetteer"
```

A type that has no disposition is reported as `not-recorded` and left alone, so repeating the command is safe. `--by <name>` records who cleared it and defaults to the signed-in user. See the earlier dispositions of a type with [disposition show](cmd:disposition%20show).

## Related commands

- [Give word types a disposition](cmd:wordform%20dispose) records one.

# Explain a generation

`generate <entry-id> --trace` is reserved for explaining why a generation does not produce a surface. The PanGloss Motif pins traces parsing only, so the command checks its arguments and refuses with `rejection-trace.unsupported`; it never guesses a cause.

```powershell
motif generate LATZ3P_3QsKF7LmA9uF1Ag --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --trace
```

## Related commands

- [Explain a word's parse](cmd:parse) shows where each candidate path of a parse stopped.

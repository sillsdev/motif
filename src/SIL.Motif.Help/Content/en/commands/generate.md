# Explain a root's forms

`generate <root> --trace` generates the forms of one root alone and shows the rules that made each, with the allomorph PanGloss chose. It is `forms explain <root>` under its older name, and it takes no other option than `--at`. It needs a PanGloss that declares a traced generate; with an older one it refuses with `forms-explain.parser-unsupported` and names what to install, and it never guesses a form.

```powershell
motif generate LATZ3P_3QsKF7LmA9uF1Ag --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --trace
```

## Related commands

- [Explain a morpheme's forms](cmd:forms%20explain) takes morphemes to add to the root, or a word, and prints Markdown or JSON.
- [Explain a word's parse](cmd:parse) shows where each candidate path of a parse stopped.

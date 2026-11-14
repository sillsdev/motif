# Remove or restore a Source

Stage a Source as Removed, or as Active again. A removed Source stays in the list and keeps its id.

```sh
motif compose-set-source-status --project language.fwdata --draft pending --intent '{"source":"WEB","status":"Removed"}'
```

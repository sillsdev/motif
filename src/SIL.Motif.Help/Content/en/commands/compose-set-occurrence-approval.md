# Approve or clear an Occurrence of a Source

Stage the approval of one word of a Paratext Source with an analysis, or clear it back to a guess. Approving also approves the analysis when its Opinion is Unknown. A Disapproved Opinion is left as it is. When only a parser reading exists, give the stored wordform and the reading, and the analysis is added in the same Draft.

```sh
motif compose-set-occurrence-approval --project language.fwdata --draft pending --intent '{"source":"WEB","anchor":"MAT 5:3#2","action":"Approve","analysis":"<analysis id>"}'
```

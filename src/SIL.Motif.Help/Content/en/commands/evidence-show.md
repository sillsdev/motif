# Read decision evidence

`evidence show` reads the evidence Motif kept for one wordform or analysis whose decision a program staged from cited sources. Give the identity of the wordform or analysis after the command name.

Each record names its tier (for example `source-approved`, `misspelled-typo` or `ill-formed`), the sources it cites, the facts that tier requires, and the Proposal that staged it. When the record supports an approval or disapproval, it also names the non-human agent that decided and the source release it stands for. Every record is labelled source-validated and is never a person's decision: people's approvals, disapprovals and spelling marks stay as FieldWorks holds them.

The records come from Applied Proposals, as they were staged. If a person later changed the decision in FieldWorks, the record still shows what was staged.

```powershell
motif evidence show <wordformOrAnalysisId> --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --json
```

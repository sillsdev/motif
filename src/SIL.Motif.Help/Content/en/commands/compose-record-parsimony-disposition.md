# Stage a Parsimony disposition

Use this command to stage a decision about one finding in a Draft Proposal. The intent names an existing Notebook record type, the finding's exact subject and evidence digest, and one disposition: `keep`, `fix`, `ask`, or `defer`. A reason is optional; `ask` requires a question.

```sh
motif compose-record-parsimony-disposition --project language.fwdata --draft review-one --intent '{"recordTypeId":"...","measureId":"P-allo-duplicate-form","subject":{},"disposition":"keep","evidenceDigest":"...","evidenceContract":"...","subjectCaption":"...","measureCaption":"...","reason":"..."}'
```

The command appends bounded Notebook and reserved-field operations to the Draft. Review the Proposal with Dry Run and Preflight before a human Applies it. A pending keep does not change the live findings; it becomes project data only after Apply and a later Refresh.

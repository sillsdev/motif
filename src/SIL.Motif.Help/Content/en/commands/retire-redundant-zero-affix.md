# Retire an unused zero affix

Use this command to stage deletion of a complete, unused prefix or suffix entry whose only loaded realization is a recognized null marker such as `^0`, `*0`, `&0`, or `∅`. An optional slot can remain in an inflectional template; deleting the entry removes its zero realization while preserving the slot and the template. The closed intent names the entry by canonical identity.

```sh
motif retire-redundant-zero-affix --project language.fwdata --draft remove-unused-zero --intent '{"format":"motif-retire-redundant-zero-affix","version":1,"retirement":{"entry":"<canonicalId>"}}'
```

The command includes the entry, its senses, analysis, form, and every owned dependent in one delete. It refuses feature, inflection-class, exception, condition, required-slot, or outside-reference contributions. References from the analysis to optional slots are removed before deletion; any template and its optional slot remain. Review the Dry Run and Trial before a person Applies the Proposal.

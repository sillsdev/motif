# Performance gotcha index

**Scope:** FieldWorks 9.3.11 Windows/WinForms, source tag `FieldWorks9.3.11`
(`96da794961d20d82d53719824f3049aa19572533`). Help: official FLEx 9.3 CHM,
`sillsdev/FwHelps` develop `d468f9ca501f421616f622965e674f5f4678c9ce`;
`markdown-export` unavailable. Model vocabulary: `SIL.LCModel 11.0.0-beta0182`.
Help is older than the source tag; source configuration resolves differences. No Avalonia UI claims.
Engine gotcha source check: `sillsdev/machine` at `b9e7db4435c325494bdb2c68ec569cadeb10df23`;
this is documentary engine evidence, not a claim that this revision ships in FLEx 9.3.11.

All 10 upstream performance topics are distilled here. Preserve the accepted language while measuring;
[bounded evidence](../workflow/measuring.md) defines complete and inconclusive results.

| Topic | Search pressure |
|---|---|
| [Optional slots multiply the search](affix-template-optional-slots.md) | With n independently optional slots, apply/skip combinations can grow like 2^n before alternative affixes add further branching. |
| [Linear generation is not linear analysis](stratum-rule-ordering.md) | An unordered morphological cascade explores different rule subsets/orders. |
| [Broad sound classes broaden downstream search](natural-class-feature-widening.md) | Feature matching can be cheap while a broad class makes many more contexts/rules eligible. |
| [Allomorph precedence may be checked late](disjunctive-allomorph-deferred-recheck.md) | Environment-based disjunctive blocking can depend on the final surface. |
| [Some restrictions filter after candidate construction](mpr-cooccurrence-late-filter.md) | MPR/co-occurrence constraints do not all act as an early search index. |
| [Overwrite makes rule order observable](mpr-overwrite-order-dependence.md) | Replacing labels in one MPR group makes two otherwise similar rule orders end in different label sets. |
| [Application mode changes what a rule can see](phonological-simultaneous-vs-iterative.md) | An iterative rule can see output modified by earlier applications of itself; a simultaneous rule uses the prior input matching behavior. |
| [Self-feeding sound rules can exhaust the engine](epenthesis-metathesis-self-feeding-crash.md) | Iterative insertion can repeatedly create another eligible context. |
| [Compound search tries possible splits](compounding-split-point-enumeration.md) | Reverse compounding considers possible split locations and stem analyses. |
| [Pattern roots bypass a simple spelling index](root-allomorph-trie-vs-pattern.md) | Literal root forms can use a trie-like spelling lookup, while pattern-shaped root allomorphs may require scanning and pattern matching across many entries. |

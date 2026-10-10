# Environment may be too broad

Unobserved contexts are not bad forms. The first check covers one neighboring phoneme or an explicit boundary on one side; unsupported shapes and ambiguous alignments abstain.

## Analyse

Read `environment-excess` for the loaded phoneme universe, effective contexts after compiled sibling order, OR alternatives, observed triggers, extra contexts, and missed triggers. Filter by an environment identity to inspect the other measured users. `approved-morph-sequences` carries the aligned word evidence. Ambiguous alignment or a condition outside the one-neighbor capability is reported as inconclusive.

## Disposition

An applied keep or defer is tied to this finding's exact evidence. See [Disposition](term:disposition) for how it appears in Active and Suppressed lists and returns after relevant evidence changes.

Ask by default when the only evidence is that a context has not appeared in the Texts. An extra licensed context is not evidence that the context is wrong. A finding needs an extra context and at least three Approved word types across two stems; that support floor ranks a question but does not decide it. Keep a productive extension and defer unsupported context shapes.

## Ask the linguist

Ask whether a contrasting form in the extra licensed context is possible. Offer valid, invalid, or unknown as the response.

## Update

Only after a linguist confirms a wrong-context example, stage changes with `AuthorEnvironment` and `EditAllomorphCondition`, declaring the environment creation as a dependency of the relink. Relink only the intended allomorph user; leave shared users attached to their current environment. Do not narrow globally to the observed segments by default. Use the named word-boundary context for a word edge; do not infer a word edge from a displayed `#` marker.

## Verify

Use Review changes to inspect what the edit would write, then compare parser results from the current and edited scratch copies for the same words. Check every exact Approved reading, held-out positive forms, and reviewed wrong-context negatives; confirm the shared users remain unchanged. The view reports extra and missed trigger sets; a missed trigger prevents an excess score. Show context counts, not a correctness rate. If the context universe, compiler order, or unique alignment is unavailable, name it and defer.

## Grounding

- **[prince-tesar-2004]** — Alan Prince and Bruce Tesar. 2004. “Learning Phonotactic Distributions.” In *Constraints in Phonological Acquisition*, 245–291. Cambridge University Press. DOI: 10.1017/CBO9780511486418.009. [DOI](https://doi.org/10.1017/CBO9780511486418.009). Shows that positive evidence can leave different grammars making different predictions about unseen forms.
- **[tenenbaum-griffiths-2001]** — Joshua B. Tenenbaum and Thomas L. Griffiths. 2001. “Generalization, Similarity, and Bayesian Inference.” *Behavioral and Brain Sciences* 24(4):629–640. DOI: 10.1017/S0140525X01000061. [DOI](https://doi.org/10.1017/S0140525X01000061). Derives a preference for smaller extensions under a specified strong-sampling model and explains how priors affect it.
- **[gold-1967]** — E. Mark Gold. 1967. “Language Identification in the Limit.” *Information and Control* 10(5):447–474. DOI: 10.1016/S0019-9958(67)91165-5. [DOI](https://doi.org/10.1016/S0019-9958(67)91165-5). Shows that positive-only learning depends on the hypothesis class and the form of the evidence.
- **[wexler-1993]** — Kenneth Wexler. 1993. “The Subset Principle is an Intensional Principle.” In Eric Reuland and Werner Abraham (eds.), *Knowledge and Language: Volume I, From Orwell’s Problem to Plato’s Problem*, 217–239. Dordrecht: Springer. DOI: 10.1007/978-94-011-1840-8_10. [DOI](https://doi.org/10.1007/978-94-011-1840-8_10). States a smallest-language choice for a nested hypothesis space under its learning assumptions.
- **[wexler-manzini-1987]** — Kenneth Wexler and M. Rita Manzini. 1987. “Parameters and Learnability in Binding Theory.” In Thomas Roeper and Edwin Williams (eds.), *Parameter Setting*, 41–76. Dordrecht: D. Reidel. DOI: 10.1007/978-94-009-3727-7_3. [DOI](https://doi.org/10.1007/978-94-009-3727-7_3). Identifies a distinct parameter-setting and learnability chapter in binding theory; it is background, not evidence about Motif's environment detector.

The finite context universe and support floor are engineering choices, not a learned probability model or linguistic law.

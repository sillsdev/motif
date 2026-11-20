# Architecture decisions

Decisions in force are edited in place; new topics receive new numbers.

- [ADR 0001](0001-people-work-through-five-things-diagnostics-are-behind-a-switch.md)
- [ADR 0002](0002-grammar-data-never-belongs-in-motif.md)
- [ADR 0003](0003-motif-writes-only-what-fieldworks-can-create.md)
- [ADR 0004](0004-linguistic-decisions-follow-black-and-judgement-calls-are-the-linguists.md)
- [ADR 0005](0005-the-grammar-guide.md)
- [ADR 0006](0006-rich-text-is-fieldworks-markdown-in-the-shared-textediting-field.md)
- [ADR 0007](0007-a-proposal-has-one-lifecycle-a-conversation-citations-and-one-apply-path.md)
- [ADR 0008](0008-requests-how-a-person-asks-claude-for-a-proposal.md)
- [ADR 0009](0009-assistants-reach-motif-through-one-agent-api.md)
- [ADR 0010](0010-measuring-agents-fairly.md)
- [ADR 0011](0011-the-hypothesis-ledger-lives-in-the-motif-store-and-the-cli-writes-it.md)
- [ADR 0012](0012-a-proposal-is-an-ordered-list-of-operations-with-a-canonical-digest.md)
- [ADR 0013](0013-the-operation-catalog-is-generated-from-the-liblcm-model.md)
- [ADR 0014](0014-text-analysis-and-writing-system-changes-are-proposal-operations.md)
- [ADR 0015](0015-phonology-and-allomorphy-are-authored-as-fieldworks-authors-them.md)
- [ADR 0016](0016-sil-icu-and-writing-system-display-follow-fieldworks.md)
- [ADR 0017](0017-dry-runs-and-trials-use-a-scratch-copy-and-apply-is-read-back.md)
- [ADR 0018](0018-one-command-catalog-behind-the-cli-window-and-mcp.md)
- [ADR 0019](0019-every-parser-run-is-one-contained-pangloss-invocation.md)
- [ADR 0020](0020-a-job-produces-assessments-and-scope-decides-comparability.md)
- [ADR 0021](0021-a-trial-review-report-has-three-levels-and-no-verdict.md)
- [ADR 0022](0022-parsimony-and-grammar-warnings-advise-and-blocking-is-opt-in.md)
- [ADR 0023](0023-one-store-beside-each-project-inside-a-byte-budget.md)
- [ADR 0024](0024-sources-are-read-in-place-and-scripture-stays-in-paratext.md)
- [ADR 0025](0025-motif-bible-is-an-expanded-interlinearizer-on-fw-lite.md)
- [ADR 0026](0026-a-project-home-is-fwdata-or-a-harmony-log-everything-else-is-built.md)
- [ADR 0027](0027-the-window-follows-fieldworks.md)
- [ADR 0028](0028-the-window-is-one-workspace-of-pages-on-a-fixed-grid.md)
- [ADR 0029](0029-help-walkthroughs-and-the-docs-site-come-from-one-source.md)
- [ADR 0030](0030-all-automated-testing-is-headless.md)
- [ADR 0031](0031-motif-ships-as-one-velopack-payload-per-runtime.md)
- [ADR 0033](0033-lexicon-import-accepts-only-lift-and-lowers-through-composers.md)
- [ADR 0034](0034-the-record-store-is-sqlite-rows-and-typed-blobs-reached-by-bulk-writes-then-streaming.md)

## Historical crosswalk

| Former file | Current ADR |
| --- | --- |
| `0001-hermitcrab-projection-not-canonical.md` | [ADR 0003](0003-motif-writes-only-what-fieldworks-can-create.md) |
| `0002-effect-comparison-over-declared-compatibility.md` | [ADR 0012](0012-a-proposal-is-an-ordered-list-of-operations-with-a-canonical-digest.md) |
| `0003-feasibility-findings.md` | [ADR 0012](0012-a-proposal-is-an-ordered-list-of-operations-with-a-canonical-digest.md) |
| `0004-prerequisite-graph-stable-ids-bound-apply.md` | [ADR 0012](0012-a-proposal-is-an-ordered-list-of-operations-with-a-canonical-digest.md) |
| `0005-schema-operations-non-undoable-uow.md` | [ADR 0017](0017-dry-runs-and-trials-use-a-scratch-copy-and-apply-is-read-back.md) |
| `0006-engine-reality-apply-readback-preflight.md` | [ADR 0017](0017-dry-runs-and-trials-use-a-scratch-copy-and-apply-is-read-back.md) |
| `0007-cross-language-digest-determinism.md` | [ADR 0012](0012-a-proposal-is-an-ordered-list-of-operations-with-a-canonical-digest.md) |
| `0008-operation-model-reparent-and-compound-ops.md` | [ADR 0012](0012-a-proposal-is-an-ordered-list-of-operations-with-a-canonical-digest.md) |
| `0009-layered-api-primitives-and-composers.md` | [ADR 0013](0013-the-operation-catalog-is-generated-from-the-liblcm-model.md) |
| `0010-hermitcrab-experimentation-is-the-primary-purpose.md` | [ADR 0001](0001-people-work-through-five-things-diagnostics-are-behind-a-switch.md) |
| `0011-experiment-loop-boundary-motif-is-the-record.md` | [ADR 0026](0026-a-project-home-is-fwdata-or-a-harmony-log-everything-else-is-built.md) |
| `0012-build-order-hc-spine-first-kinds-generated.md` | [ADR 0013](0013-the-operation-catalog-is-generated-from-the-liblcm-model.md) |
| `0013-harmony-is-the-change-mechanism.md` | [ADR 0026](0026-a-project-home-is-fwdata-or-a-harmony-log-everything-else-is-built.md) |
| `0014-generate-the-crdt-layer-from-masterlcmodel.md` | [ADR 0013](0013-the-operation-catalog-is-generated-from-the-liblcm-model.md) |
| `0015-proposal-assessment-dry-run-vocabulary.md` | [ADR 0020](0020-a-job-produces-assessments-and-scope-decides-comparability.md) |
| `0016-scratch-cache-copy-not-undo.md` | [ADR 0017](0017-dry-runs-and-trials-use-a-scratch-copy-and-apply-is-read-back.md) |
| `0017-text-and-analysis-destination-scope.md` | [ADR 0014](0014-text-analysis-and-writing-system-changes-are-proposal-operations.md) |
| `0018-change-class-is-two-axes-not-one.md` | [ADR 0012](0012-a-proposal-is-an-ordered-list-of-operations-with-a-canonical-digest.md) |
| `0019-observed-intent-and-proposal-edit-mode.md` | [ADR 0012](0012-a-proposal-is-an-ordered-list-of-operations-with-a-canonical-digest.md) |
| `0020-cli-first-fieldworks-planned-not-built.md` | [ADR 0018](0018-one-command-catalog-behind-the-cli-window-and-mcp.md) |
| `0021-cli-is-the-full-surface-layer-1-churns.md` | [ADR 0013](0013-the-operation-catalog-is-generated-from-the-liblcm-model.md) |
| `0022-structure-is-derived-policy-is-five-rows.md` | [ADR 0013](0013-the-operation-catalog-is-generated-from-the-liblcm-model.md) |
| `0023-derived-kind-names-required-descriptions.md` | [ADR 0013](0013-the-operation-catalog-is-generated-from-the-liblcm-model.md) |
| `0024-group-is-derived-domain-is-editorial.md` | [ADR 0013](0013-the-operation-catalog-is-generated-from-the-liblcm-model.md) |
| `0025-parser-first-build-order.md` | [ADR 0013](0013-the-operation-catalog-is-generated-from-the-liblcm-model.md) |
| `0026-order-is-declared-not-positional.md` | [ADR 0012](0012-a-proposal-is-an-ordered-list-of-operations-with-a-canonical-digest.md) |
| `0027-what-counts-as-the-same-word-analysis.md` | [ADR 0020](0020-a-job-produces-assessments-and-scope-decides-comparability.md) |
| `0028-feeding-reorders-require-a-grammar-delta.md` | [ADR 0017](0017-dry-runs-and-trials-use-a-scratch-copy-and-apply-is-read-back.md) |
| `0029-agents-address-layer-1-only.md` | [ADR 0013](0013-the-operation-catalog-is-generated-from-the-liblcm-model.md) |
| `0030-one-writer-cli-locks-like-fieldworks.md` | [ADR 0017](0017-dry-runs-and-trials-use-a-scratch-copy-and-apply-is-read-back.md) |
| `0031-collaboration-follows-the-data-not-the-surface.md` | [ADR 0007](0007-a-proposal-has-one-lifecycle-a-conversation-citations-and-one-apply-path.md) |
| `0032-stem-assessment-is-pangloss-supplied-lexicon.md` | [ADR 0019](0019-every-parser-run-is-one-contained-pangloss-invocation.md) |
| `0033-three-systems-and-who-owns-which-measure.md` | [ADR 0019](0019-every-parser-run-is-one-contained-pangloss-invocation.md) |
| `0034-the-boundary-with-fieldworks-state-versus-change.md` | [ADR 0003](0003-motif-writes-only-what-fieldworks-can-create.md) |
| `0035-reports-are-advisory-queries-over-stored-assessments.md` | [ADR 0020](0020-a-job-produces-assessments-and-scope-decides-comparability.md) |
| `0036-motif-has-its-own-data-store.md` | [ADR 0023](0023-one-store-beside-each-project-inside-a-byte-budget.md) |
| `0037-fetching-lives-outside-motif.md` | [ADR 0024](0024-sources-are-read-in-place-and-scripture-stays-in-paratext.md) |
| `0038-expectations-are-fieldworks-approved-analyses.md` | [ADR 0020](0020-a-job-produces-assessments-and-scope-decides-comparability.md) |
| `0039-one-worker-baseline-and-live-host-authority.md` | [ADR 0018](0018-one-command-catalog-behind-the-cli-window-and-mcp.md) |
| `0040-one-api-the-cli.md` | [ADR 0018](0018-one-command-catalog-behind-the-cli-window-and-mcp.md) |
| `0041-the-database-is-the-only-store.md` | [ADR 0023](0023-one-store-beside-each-project-inside-a-byte-budget.md) |
| `0042-a-job-produces-assessments-an-assessor-makes-them.md` | [ADR 0020](0020-a-job-produces-assessments-and-scope-decides-comparability.md) |
| `0043-one-command-catalog-two-front-ends.md` | [ADR 0018](0018-one-command-catalog-behind-the-cli-window-and-mcp.md) |
| `0044-every-parser-process-is-one-pangloss-invocation.md` | [ADR 0019](0019-every-parser-run-is-one-contained-pangloss-invocation.md) |
| `0045-the-handoff-is-five-files-and-a-pasted-header.md` | [ADR 0009](0009-assistants-reach-motif-through-one-agent-api.md) |
| `0046-pages-not-stages.md` | [ADR 0028](0028-the-window-is-one-workspace-of-pages-on-a-fixed-grid.md) |
| `0047-generated-help-walkthroughs-and-docs-site.md` | [ADR 0029](0029-help-walkthroughs-and-the-docs-site-come-from-one-source.md) |
| `0048-cross-platform-parser-containment.md` | [ADR 0019](0019-every-parser-run-is-one-contained-pangloss-invocation.md) |
| `0049-fieldworks-opinions-and-now-after-apply.md` | [ADR 0027](0027-the-window-follows-fieldworks.md) |
| `0049-velopack-installers.md` | [ADR 0031](0031-motif-ships-as-one-velopack-payload-per-runtime.md) |
| `0050-sil-icu-everywhere.md` | [ADR 0016](0016-sil-icu-and-writing-system-display-follow-fieldworks.md) |
| `0051-writing-system-display-follows-fieldworks.md` | [ADR 0016](0016-sil-icu-and-writing-system-display-follow-fieldworks.md) |
| `0052-lazy-text-reads-share-one-evidence-context.md` | [ADR 0018](0018-one-command-catalog-behind-the-cli-window-and-mcp.md) |
| `0053-word-presentation-owns-layout-and-text-metrics.md` | [ADR 0028](0028-the-window-is-one-workspace-of-pages-on-a-fixed-grid.md) |
| `0054-installed-release-pathway-check.md` | [ADR 0031](0031-motif-ships-as-one-velopack-payload-per-runtime.md) |
| `0055-window-editing-shortcuts.md` | [ADR 0027](0027-the-window-follows-fieldworks.md) |
| `0056-human-judgments-belong-to-the-fieldworks-project.md` | [ADR 0003](0003-motif-writes-only-what-fieldworks-can-create.md) |
| `0057-explicit-allomorph-retirement.md` | [ADR 0015](0015-phonology-and-allomorphy-are-authored-as-fieldworks-authors-them.md) |
| `0058-the-agent-edit-loop.md` | [ADR 0009](0009-assistants-reach-motif-through-one-agent-api.md) |
| `0059-measuring-agents-fairly.md` | [ADR 0010](0010-measuring-agents-fairly.md) |
| `0060-parsimony-evidence-and-advisory-dispositions.md` | [ADR 0022](0022-parsimony-and-grammar-warnings-advise-and-blocking-is-opt-in.md) |
| `0061-proposal-mode-is-a-lens-over-one-set-of-pages.md` | [ADR 0007](0007-a-proposal-has-one-lifecycle-a-conversation-citations-and-one-apply-path.md) |
| `0062-velopack-installers.md` | [ADR 0031](0031-motif-ships-as-one-velopack-payload-per-runtime.md) |
| `0063-a-fixed-grid-adapts-to-window-width.md` | [ADR 0028](0028-the-window-is-one-workspace-of-pages-on-a-fixed-grid.md) |
| `0064-every-operation-can-cite-its-source.md` | [ADR 0007](0007-a-proposal-has-one-lifecycle-a-conversation-citations-and-one-apply-path.md) |
| `0065-phonology-example-sets-are-advisory-evidence.md` | [ADR 0022](0022-parsimony-and-grammar-warnings-advise-and-blocking-is-opt-in.md) |
| `0066-one-compact-scale-for-the-window.md` | [ADR 0028](0028-the-window-is-one-workspace-of-pages-on-a-fixed-grid.md) |
| `0067-proposal-difference-against-human-decisions.md` | [ADR 0021](0021-a-trial-review-report-has-three-levels-and-no-verdict.md) |
| `0068-an-ids-identity-is-its-guid.md` | [ADR 0012](0012-a-proposal-is-an-ordered-list-of-operations-with-a-canonical-digest.md) |
| `0069-the-agent-api.md` | [ADR 0009](0009-assistants-reach-motif-through-one-agent-api.md) |
| `0070-the-window-follows-the-fieldworks-ux-guide.md` | [ADR 0027](0027-the-window-follows-fieldworks.md) |
| `0071-motif-is-ahead-on-avalonia-12-and-net10.md` | [ADR 0027](0027-the-window-follows-fieldworks.md) |
| `0072-window-text-lives-in-resx-help-content-stays-json.md` | [ADR 0027](0027-the-window-follows-fieldworks.md) |
| `0073-menus-buttons-access-keys-and-shortcuts-follow-fieldworks.md` | [ADR 0027](0027-the-window-follows-fieldworks.md) |
| `0074-tokens-map-onto-fwavaloniatheme-and-never-override-semi.md` | [ADR 0028](0028-the-window-is-one-workspace-of-pages-on-a-fixed-grid.md) |
| `0075-headless-checks-are-the-ui-gate-screenshots-are-for-people.md` | [ADR 0030](0030-all-automated-testing-is-headless.md) |
| `0076-grammar-data-never-belongs-in-motif.md` | [ADR 0002](0002-grammar-data-never-belongs-in-motif.md) |
| `0077-project-store-upgrades-carry-authored-data.md` | [ADR 0023](0023-one-store-beside-each-project-inside-a-byte-budget.md) |
| `0078-case-handling-in-host-layer.md` | [ADR 0019](0019-every-parser-run-is-one-contained-pangloss-invocation.md) |
| `0079-linguistic-decisions-follow-black-and-judgement-calls-are-the-linguists.md` | [ADR 0004](0004-linguistic-decisions-follow-black-and-judgement-calls-are-the-linguists.md) |
| `0080-parsimony-runs-in-two-groups-grammar-and-text.md` | [ADR 0022](0022-parsimony-and-grammar-warnings-advise-and-blocking-is-opt-in.md) |
| `0081-motif-counts-and-limits-parsing-in-parser-work.md` | [ADR 0019](0019-every-parser-run-is-one-contained-pangloss-invocation.md) |
| `0082-stores-and-user-data-stay-inside-a-byte-budget.md` | [ADR 0023](0023-one-store-beside-each-project-inside-a-byte-budget.md) |
| `0083-grammar-design-records-live-in-the-fieldworks-project.md` | [ADR 0005](0005-the-grammar-guide.md) |
| `0084-proposal-discussion-rationale-comments-and-pictures.md` | [ADR 0007](0007-a-proposal-has-one-lifecycle-a-conversation-citations-and-one-apply-path.md) |
| `0085-proposal-snapshots-are-disposable-derived-files.md` | [ADR 0017](0017-dry-runs-and-trials-use-a-scratch-copy-and-apply-is-read-back.md) |
| `0086-parsimony-keeps-reports-and-drill-down-rows-and-discards-grammar-facts.md` | [ADR 0022](0022-parsimony-and-grammar-warnings-advise-and-blocking-is-opt-in.md) |
| `0087-phonological-features-are-authored-like-any-liblcm-feature-and-an-archiphoneme-is-a-phoneme.md` | [ADR 0015](0015-phonology-and-allomorphy-are-authored-as-fieldworks-authors-them.md) |
| `0088-phonological-rules-are-edited-in-place-and-hold-one-item-each-side.md` | [ADR 0015](0015-phonology-and-allomorphy-are-authored-as-fieldworks-authors-them.md) |
| `0089-gdr-text-is-human-editable-fieldworks-markdown.md` | [ADR 0006](0006-rich-text-is-fieldworks-markdown-in-the-shared-textediting-field.md) |
| `0090-underlying-distinctions-survive-until-the-grammar-realizes-the-written-form.md` | [ADR 0015](0015-phonology-and-allomorphy-are-authored-as-fieldworks-authors-them.md) |
| `0091-proposal-review-report-three-levels-and-red-flags-no-verdict.md` | [ADR 0021](0021-a-trial-review-report-has-three-levels-and-no-verdict.md) |
| `0092-textediting-is-a-sealed-folder-with-its-own-build.md` | [ADR 0006](0006-rich-text-is-fieldworks-markdown-in-the-shared-textediting-field.md) |
| `0093-proposals-release-computed-data-on-close-and-after-seven-days-untouched.md` | [ADR 0023](0023-one-store-beside-each-project-inside-a-byte-budget.md) |
| `0094-corpus-files-harvesters-and-stored-word-analysis.md` | [ADR 0024](0024-sources-are-read-in-place-and-scripture-stays-in-paratext.md) |
| `0094-requests-how-a-linguist-asks-an-assistant-for-a-proposal.md` | [ADR 0008](0008-requests-how-a-person-asks-claude-for-a-proposal.md) |
| `0095-rich-text-edits-are-two-generic-field-addressed-operations.md` | [ADR 0006](0006-rich-text-is-fieldworks-markdown-in-the-shared-textediting-field.md) |
| `0096-writing-system-changes-are-proposal-operations-character-class-first.md` | [ADR 0014](0014-text-analysis-and-writing-system-changes-are-proposal-operations.md) |
| `0097-a-text-proposal-replaces-the-notes-of-existing-segments-one-operation-per-segment.md` | [ADR 0014](0014-text-analysis-and-writing-system-changes-are-proposal-operations.md) |
| `0098-chatgpt-reaches-motif-through-a-bundled-tunnel-client.md` | [ADR 0009](0009-assistants-reach-motif-through-one-agent-api.md) |
| `0099-rationale-and-comments-are-fieldworks-markdown.md` | [ADR 0006](0006-rich-text-is-fieldworks-markdown-in-the-shared-textediting-field.md) |
| `0100-motif-has-two-stages-grow-and-polish.md` | [ADR 0001](0001-people-work-through-five-things-diagnostics-are-behind-a-switch.md) |
| `0101-a-project-home-is-fwdata-or-a-harmony-log-everything-else-is-built.md` | [ADR 0026](0026-a-project-home-is-fwdata-or-a-harmony-log-everything-else-is-built.md) |
| `0102-grammar-classes-are-motif-registered-harmony-types-that-ride-lexbox-opaquely.md` | [ADR 0026](0026-a-project-home-is-fwdata-or-a-harmony-log-everything-else-is-built.md) |
| `0103-a-proposal-is-held-back-harmony-changes-over-a-recorded-base.md` | [ADR 0026](0026-a-project-home-is-fwdata-or-a-harmony-log-everything-else-is-built.md) |
| `0104-ordered-grammar-is-one-list-valued-property.md` | [ADR 0026](0026-a-project-home-is-fwdata-or-a-harmony-log-everything-else-is-built.md) |
| `0105-a-build-reports-errors-and-never-repairs-the-home.md` | [ADR 0026](0026-a-project-home-is-fwdata-or-a-harmony-log-everything-else-is-built.md) |
| `0106-phase-one-keeps-fwdata-as-home-with-harmony-shaped-operations.md` | [ADR 0025](0025-motif-bible-is-an-expanded-interlinearizer-on-fw-lite.md) |
| `0107-one-owner-per-field-and-guids-are-kept-never-made-at-build.md` | [ADR 0026](0026-a-project-home-is-fwdata-or-a-harmony-log-everything-else-is-built.md) |
| `0108-approval-is-per-occurrence-and-progress-is-scripture-centric.md` | [ADR 0024](0024-sources-are-read-in-place-and-scripture-stays-in-paratext.md) |
| `0109-evidence-is-separate-from-the-baseline-and-always-current.md` | [ADR 0020](0020-a-job-produces-assessments-and-scope-decides-comparability.md) |
| `0110-scripture-stays-in-paratext-and-judgements-ride-on-wfiwordform.md` | [ADR 0024](0024-sources-are-read-in-place-and-scripture-stays-in-paratext.md) |
| `0111-all-automated-testing-is-headless.md` | [ADR 0030](0030-all-automated-testing-is-headless.md) |
| `0112-motif-writes-only-what-fieldworks-can-create.md` | [ADR 0003](0003-motif-writes-only-what-fieldworks-can-create.md) |
| `0113-a-newer-trial-releases-the-trials-it-supersedes.md` | [ADR 0023](0023-one-store-beside-each-project-inside-a-byte-budget.md) |
| `0113-the-phase-1-paratext-plugin-is-an-expanded-interlinearizer-on-fw-lite.md` | [ADR 0025](0025-motif-bible-is-an-expanded-interlinearizer-on-fw-lite.md) |
| `0114-trial-reports-split-attested-from-generated-gains-and-record-grammar-size.md` | [ADR 0021](0021-a-trial-review-report-has-three-levels-and-no-verdict.md) |
| `0115-tag-by-query-picks-the-cheaper-polarity-and-a-replacement-proves-readings-identical-from-the-trial.md` | [ADR 0015](0015-phonology-and-allomorphy-are-authored-as-fieldworks-authors-them.md) |
| `0116-same-shape-additions-need-a-rule-attempt-and-parsimony-runs-weekly.md` | [ADR 0022](0022-parsimony-and-grammar-warnings-advise-and-blocking-is-opt-in.md) |
| `0117-the-hypothesis-ledger-lives-in-the-motif-store-and-the-cli-writes-it.md` | [ADR 0011](0011-the-hypothesis-ledger-lives-in-the-motif-store-and-the-cli-writes-it.md) |
| `0118-remove-exception-feature-edits-stems-and-rule-branches-in-one-pass.md` | [ADR 0015](0015-phonology-and-allomorphy-are-authored-as-fieldworks-authors-them.md) |
| `0119-the-gdr-sync-check-reads-links-headings-and-stored-trial-rows.md` | [ADR 0005](0005-the-grammar-guide.md) |
| `0120-people-work-through-five-things-diagnostics-are-behind-a-switch.md` | [ADR 0001](0001-people-work-through-five-things-diagnostics-are-behind-a-switch.md) |
| `0121-the-grammar-guide.md` | [ADR 0005](0005-the-grammar-guide.md) |

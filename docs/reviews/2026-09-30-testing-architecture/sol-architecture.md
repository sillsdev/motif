# Sol challenge review: architecture

GPT-6.1-Sol, high. Source review at 74d82f11. Preserved completed Herdr reports, including subsequent live-sync evidence update.

## Report at 09/30/2026 13:58:31

The strongest issues are documentation drift and incomplete usage instrumentation. A Worker/runtime seam has concrete justification, but the evidence does not support reorganizing the whole project graph or folding LiveHost.

Verified cwd: `C:\Users\johnm\Documents\repos\motif`. HEAD remained `74d82f1134abbb4c44d6fa48d9e76d2bfeb67ccc`. I read CONTEXT, root guidance, the baseline and Luna layout/catalog/GUI reports. No edits, builds, tests, commits or agents. Validation is parent evidence: build gates passed; outside-sandbox suite reported 3,667 passed, one failed, 33 skipped; site tests passed seven.

1. **Accepted: shared commands provide real depth and locality.**

   Overview uses the same handler from [CLI Program.cs:811](/C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.Cli/Program.cs:811) and [CommandClient.Workbench.cs:11](/C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.App/Services/CommandClient.Workbench.cs:11). Stored-evidence selection and metric construction remain in [OverviewCommand.cs:20](/C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.Commands/Catalog/OverviewCommand.cs:20).

   Pending Apply likewise converges on `PendingChangesWorkflow.Apply`: CLI at `Program.cs:632`, App at [CommandClient.PendingChanges.cs:16](/C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.App/Services/CommandClient.PendingChanges.cs:16). The workflow checks identity/revision and fit, finalizes, preflights, queues and waits for the Dry Run, then applies. Ordinary refusals reopen the Draft; reconciliation-needed remains distinct ([PendingChangesWorkflow.cs:54](/C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.Commands/PendingChangesWorkflow.cs:54)).

   Preserve these typed workflows. Their interface hides consequential sequencing from two callers. A generic dispatcher rewrite or one interface per command has no demonstrated benefit here.

2. **Corrected: the dependency graph is not a linear layering chain.**

   These are direct project references; arrows mean “references”:

   | Project | Direct product dependencies |
   |---|---|
   | Contract | None |
   | Model | Contract |
   | Runner | Contract, Model |
   | Projection | Contract, Model, Runner |
   | Host | Projection, Runner |
   | LiveHost | Contract, Model, Runner |
   | Worker | Contract, Host, LiveHost |
   | Commands | Contract, Host, Model, Projection, Runner, LiveHost, Worker |
   | Cli | Commands, Host, Runner, Worker, Contract, Model, Projection, Help |
   | App | Commands, Contract, Help |

   Evidence: project-reference entries in `src/<project>/<project>.csproj`; especially [Host.csproj:9](/C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.Host/SIL.Motif.Host.csproj:9), [Commands.csproj:9](/C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.Commands/SIL.Motif.Commands.csproj:9), and [Cli.csproj:12](/C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.Cli/SIL.Motif.Cli.csproj:12).

   The graph’s branching is not itself a defect. Contract remains LibLCM-free. Runner accepts an already-loaded cache and delegates persistence to the host ([ProposalApplier.cs:29](/C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.Runner/Apply/ProposalApplier.cs:29)). Preserve that ownership.

3. **Accepted coupling; downgraded severity: Worker mixes executable and reusable implementation.**

   Commands references Worker’s executable project and consumes its repositories, Baselines, project infrastructure and Assessment helpers. CLI also consumes Worker settings and status rendering; these are not merely build-order references. Examples include `OverviewCommand.cs:12`, `PendingChangesWorkflow.cs:12`, `JobRunnerLaunchOptions.cs:2`, `Cli/Program.cs:81`, and `Cli/Rendering/CommandTextRenderer.cs:204`.

   There is concrete packaging friction: [Directory.Build.targets:2](/C:/Users/johnm/Documents/repos/motif/Directory.Build.targets:2) removes Worker apphost, deps and runtimeconfig files from App/CLI portable publishes. Meanwhile [Worker/Program.cs:64](/C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.Worker/Program.cs:64) combines executable startup with runtime orchestration.

   **Smallest justified seam:** separate reusable implementation from executable startup, retaining `SIL.Motif.Worker` as the shipped process identity. Move the command-consumed dependency closure into one library; avoid simultaneously redesigning repositories, scheduling or namespaces. This addresses actual assembly and packaging ownership. It must preserve SQLite coordination rather than create a request channel.

   **Alternative:** leave the project intact and explicitly document its dual role. The current reference is functioning; I found no associated runtime failure. Moving repositories into Host is viable only after checking their dependency closure and is not automatically smaller. I would stage extraction with packaging work, not classify it as an urgent P2 product fix.

   Test ownership: retain Commands’ launcher tests and real-database workflows; retain CLI [RunnerSpineTests.cs:39](/C:/Users/johnm/Documents/repos/motif/tests/SIL.Motif.Tests.Cli/Integration/RunnerSpineTests.cs:39) for the actual executable. Verify portable package contents and startup after changing project references.

4. **Rejected: fold LiveHost because it is small or lacks an interface.**

   Its interface is concrete and meaningful. [SavedProjectFileCopier.cs:50](/C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.LiveHost/Baselines/SavedProjectFileCopier.cs:50) captures saved files and writing systems; [BaselineBundleWriter.cs:15](/C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.LiveHost/Baselines/BaselineBundleWriter.cs:15) explicitly preserves caller-owned cache lifecycle. Commands loads the copied project as a scratch cache (`BaselineCaptureCommand.cs:88`); Worker also uses the bundle writer (`BaselineRefresh.cs:102`).

   Removing the project would relocate these obligations, not eliminate them. No concrete maintenance failure justifies consolidation. Preserve the saved-file/scratch-model distinction and existing bundle/capture tests. Neither a speculative interface nor a project merger is warranted.

5. **Accepted: Guide ownership is incomplete; correct the site-metadata claim.**

   [HelpEntryKind:13](/C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.Help/HelpCatalog.cs:13) includes Command, Ui and Term, but excludes Guide. Consequently Guide pages are absent from CLI catalog export and lookup. GUI bypasses the catalog to locate embedded resources, parse headings/descriptions and construct URLs ([HelpPopupViewModel.cs:154](/C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.App/ViewModels/HelpPopupViewModel.cs:154)).

   The site **already derives Guide page titles and descriptions from Markdown** (`sync-core.mjs:247`). Its separate authority is narrower: outline labels at `sync-core.mjs:5`, home summaries at [index.mdx:139](/C:/Users/johnm/Documents/repos/motif/site/src/content/docs/index.mdx:139), and an independent description algorithm at `sync-core.mjs:97`. GUI strips Markdown marks; site truncates to 180 characters. Different presentation lengths are legitimate; independently deciding the underlying metadata is avoidable.

   **Fix:** give SIL.Motif.Help ownership of Guide loading, canonical metadata, fallback and routes. Export that representation for the site; readers may shorten or render it. Keep site ordering/layout separate. Prefer deriving existing heading/opening-paragraph metadata initially; add authored metadata only where the existing prose cannot supply the required lengths. Do not duplicate every Guide as a Ui Help page.

   `ui.json` is empty and CLI direct lookup excludes Ui (`HelpCommand.cs:63`). Those are verified limitations, not evidence that every control currently has broken help. Which controls deserve dedicated entries remains a content-scope decision.

   Test ownership: shared Help assertions for Guide inventory/fallback/links; CLI lookup/export tests; App page mapping/rendering tests; site tests comparing exported metadata and home cards. Current [HelpCatalogTests.cs:13](/C:/Users/johnm/Documents/repos/motif/tests/SIL.Motif.Tests.Cli/Cli/HelpCatalogTests.cs:13) covers released commands and terms, not Guide parity.

6. **Accepted: current documentation contradicts implementation.**

   [README.md:238](/C:/Users/johnm/Documents/repos/motif/README.md:238) says storage is file-based and the job runner missing. [plan-product-architecture.md:4](/C:/Users/johnm/Documents/repos/motif/docs/plan-product-architecture.md:4) labels CLI-only architecture current; line 49 describes a FieldWorks assembly reference. `docs/cli-api.md:19` retains `--store`. These conflict with accepted ADRs 0041/0043 and inspected source.

   Make README an entry point; generate syntax from catalogs; maintain one current architecture description near the relevant implementation. Keep user instructions in shared Help, developer interface obligations in XML comments/Contract, and decisions/history in ADRs. Link between audiences without copying whole specifications. Mark superseded plan material clearly; do not rewrite historical ADRs to resemble current code.

7. **Accepted instrumentation gap; App scope remains uncertain.**

   ADR 0041:99 says every invocation appends usage. CLI only flushes an optional accumulated log ([Program.cs:1085](/C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.Cli/Program.cs:1085)); CLI `overview` supplies no recorder, and its handler records nothing. Thus logging is incomplete even under a CLI-only interpretation.

   ADR 0021 originally motivates agent-call evidence; ADR 0043 introduces shared catalog calls without specifying whether automated GUI rereads count. The owner decision is the observation unit—not whether missing CLI observations should silently remain.

   Use a small shared invocation recorder at adapter entry points, preserving argument shapes rather than values. Define exactly-once behavior for outer workflows and nested commands. Test CLI Overview, refusals, and—if included—App Overview. Avoid logging every nested helper automatically.

   Also, `CliSourceHasNoStoreOrLibLcmReference` checks only namespace strings, despite CLI opening `MachineDatabase` (`CommandCatalogParityTests.cs:203`). Its name overstates its guarantee.

8. **GUI challenge accepted, with a narrow fix.**

   Bulk add loops discard staging results (`ResultsInTextViewModel.Scopes.cs:123`); later success clears `LastRefusal` (`ChangesViewModel.cs:379`). Stop on refusal and preserve partial-state feedback; test success/refusal/success. Atomic bulk staging is a separate owner decision.

   Matrix captions and Guide vocabulary also lag ADR 0049. Include shared Apply summaries: `PendingChangesWorkflow.cs:196` still emits “Rejected” and “candidate status.” Vocabulary alignment is already decided.

   Stage behavior fixes and documentation correction first; then shared Guide authority and defined instrumentation; finally any justified runtime extraction. Preserve typed commands, SQLite coordination, caller-owned LibLCM lifecycle and existing process tests. No migration, compatibility reader or deprecated alias is needed before 1.0.

## Report at 09/30/2026 13:59:35

**Revised Guide finding: P2—production documentation generation fails on a shipped Guide page.**

Parent reproduced successful `motif help --all --json` export followed by site sync failure: `Guide page is missing from the published outline: pangloss`. I independently verified the source cause; I did not rerun generation.

- [pangloss.md:1](/C:/Users/johnm/Documents/repos/motif/help/en/guide/pangloss.md:1) exists in canonical Help content.
- [sync-core.mjs:240](/C:/Users/johnm/Documents/repos/motif/site/scripts/sync-core.mjs:240) rejects every ordinary Guide slug absent from the site’s independently maintained `guideOrder`.
- [sync.mjs:29](/C:/Users/johnm/Documents/repos/motif/site/scripts/sync.mjs:29) defaults to fixtures, but supplying a Help export selects repository Help content.
- [sync.test.mjs:21](/C:/Users/johnm/Documents/repos/motif/site/tests/sync.test.mjs:21) constructs synthetic Help inputs. Seven passing tests therefore do not establish that current product documentation can be published.

**Concrete fix:** include PanGloss in the published outline immediately, retaining the completeness check. Then move canonical Guide inventory, titles, descriptions, fallback and routes into SIL.Motif.Help. GUI should consume that representation; CLI should export it; site should render it. Site-specific grouping and order may remain, provided a check proves complete canonical coverage. Independently authored outline titles and home descriptions should consume shared metadata.

Do not resolve this by silently omitting unknown Guide pages or extending fixtures alone.

**Regression ownership:** the site suite needs a live-source integration test using the built CLI’s actual Help export and repository `help/en/guide`, with generated output directed to a temporary directory. Assert successful synchronization, inclusion of `guide/pangloss.md`, and complete publication of the canonical ordinary Guide inventory. Keep fixture tests for isolated transformation cases. The documentation-generation gate should run this live-source path before site build.

This strengthens the staged recommendation: repair and pin the publishing path before consolidating Guide ownership. It requires no new owner decision, migration or compatibility layer. The runtime/apphost extraction remains a separate maintainability opportunity; LiveHost consolidation remains unjustified.

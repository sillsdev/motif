# AGENTS.md

## Build and test with these, not with `dotnet` directly

```
./build.ps1     # comment hygiene, then compile
./test.ps1      # the above, then the full suite
```

**Use them every time.** `./build.ps1` runs the comment gate before it compiles, so a violation fails
in seconds rather than surviving until someone remembers to look. A bare `dotnet build` skips that
gate entirely, which is how the rules below decay into suggestions. `./test.ps1` runs the build gate
first, so one green run means clean comments, a clean compile, and a passing suite.

CI runs the same two scripts (`.github/workflows/ci.yml`), so anything they reject locally is
rejected there too — and anything they let through is not a CI surprise.

**`./test.ps1` needs no project or checkout from outside this repo.** Every LibLCM project the suite
exercises is a real, blank `LcmCache` built at run time by `NewLangProjFixture` and seeded by
`SeededProject` (`tests/SIL.Motif.Tests/TestFixtures/`) — no vendored sample project, no sibling
FieldWorks checkout. The conformance fixture under `tests/SIL.Motif.Tests/TestFixtures/Conformance/**`
is a synthetic FieldWorks project copied from Machine's conformance suite; the `SOURCE.md` beside it
records its provenance. The `.gitignore` carves that fixture out of the project-data rules by the
owner's ruling. The one external dependency that remains is the `pangloss` executable, a separate Rust
build; tests needing it are gated by `RealParserFactAttribute`, which skips — rather than fails — when
it is not built, since "the parser is not built here" is an ordinary state of a developer's machine.

## Where the build lands

One directory per configuration at the repository root, not a `bin` tree under every project:

```
bin/Debug/motif.exe                 the CLI
bin/Debug/SIL.Motif.App.exe         the window
bin/Debug/SIL.Motif.Worker.exe      the job runner
bin/Debug/tests/                    the suite
bin/Debug/tests/fake-pangloss/      the suite's fake parser
bin/Debug/spikes/                   the throwaway harnesses
```

`Release` reads the same with `Release` in place of `Debug`. The three executables sit together on
purpose: the CLI finds its worker, and either front end finds a bundled parser, by looking beside
itself, so a development build takes the same branch a published one does. The suite gets a
subdirectory because a test host that outlives its run holds a lock on the directory it was launched
from, and that must not be the product's; the fake parser gets one below that because parser discovery
prefers an executable sitting beside the application, and the fake must never be that executable.

A test names an executable through `BuildOutput` (`tests/SIL.Motif.Tests/TestFixtures/`), which
resolves from the test binaries' own directory. Do not spell a configuration into a test path: a
hard-coded `Debug` makes a `Release` run drive the wrong build, or none at all.

## Building against a local libpalaso (opt-in, off by default)

`SIL.WritingSystems`/`SIL.Core` are pinned transitively via `SIL.LCModel` (`SilVersions.props`).
To build against a local libpalaso checkout instead — e.g. to pick up a fix before it ships in a
package — pack it and point motif at the result:

```
$env:LOCAL_NUGET_REPO = 'C:\localnugetpackages'
./tools/Manage-LocalLibraries.ps1 -PalasoPath C:\path\to\libpalaso
./build.ps1
```

`Manage-LocalLibraries.ps1` packs only `SIL.Core` and `SIL.WritingSystems` (not the whole libpalaso
solution), writes the produced version into `SilVersions.props`, and clears any stale copy from the
NuGet cache. `./build.ps1` prints a line when `LOCAL_NUGET_REPO` is active. With it unset (the
default), nothing changes — CI never sets it, and neither does an unconfigured dev machine. To
revert: `git checkout SilVersions.props` and unset `LOCAL_NUGET_REPO`.

Read `CONTEXT.md` first — it is the canonical glossary, and its terms are binding in code, comments,
CLI verbs, and prose. Then `README.md` and the documents in `docs/`.

## Development workflow guidance

For PR review, PR copy, review-comment responses, or Jira bug work, read
[`docs/development-workflow.md`](docs/development-workflow.md) and then the matching skill:
[`pr-preflight`](.claude/skills/pr-preflight/SKILL.md),
[`pr-pitch`](.claude/skills/pr-pitch/SKILL.md),
[`respond-to-review-comments`](.claude/skills/respond-to-review-comments/SKILL.md), or
[`jira-bugfix`](.claude/skills/jira-bugfix/SKILL.md). These are software-development workflows;
they do not replace Motif's Proposal, Dry Run, or Assessment contracts.

**The vocabulary changed on 2026-07-31** ([ADR 0015](docs/adr/0015-proposal-assessment-dry-run-vocabulary.md)).
`Proposal` replaces *change set*; `Dry Run` is the LibLCM-side evaluation; `Assessment` means a PanGloss
run and nothing else. Documents written before that date use the old words — they are historical
records, not counter-examples.

## Comments

**Authoritative rules: `.claude/skills/code-comments/SKILL.md`. Enforced by
`tools/CommentHygiene/comment-hygiene.cs`.** Ported from PanGloss, where the same rot was measured and corrected;
the intent is identical and the mechanics are adapted for C#.

A comment explains what the code cannot: why this, why not the obvious alternative, what breaks if you
change it. Code says what it does, git says when, and `docs/plan-motif.md` and `docs/issues.md` say
where the project is — a comment duplicating any of those three will eventually contradict it.

| Check | Standard |
|---|---|
| Implementation comment (`//`, or `///` on a `private` member) | **one line, at most 110 characters** — reflowing a paragraph onto one long line is not compliance |
| API doc (`///` on a `public`/`internal` type or member, an interface member, or an enum member) | long form as appropriate, and **complete**: no repo-relative `…md` path — `manifest/README.md` no more than `docs/…` — because a tooltip cannot open one. Cite an ADR by number, name a contract in prose, inline the fact — or give a URL |
| Plan and issue references — `MOT-22`, `docs/issues.md D8`, and the bare `D8`, `A1`, `J44` | **banned**, in string literals as well as comments; state the constraint instead |
| ADR citations | **allowed** — an ADR number is immutable; cite it for a decision, never for a status |
| Dates, slice/wiring status, history narrative, agent attribution | **banned** |
| A claim about another entity's behaviour | cite the pinning test — ``pinned by `TestName` `` — or reword |
| A `//` inside an emitter's raw-string template | scanned for banned references; exempt from the length rule, being a generated file's banner |
| A `///` inside an emitter's raw-string template | length-exempt, but **completeness still applies**: it lands in a public class and is read from a tooltip |
| A trailing `// note` sharing a line with code | scanned like any implementation comment — one line by construction, so **at most 110 characters** |
| Comment text assembled in a literal — `$"/// …"` | scanned as the comment it becomes. Only the interpolated *value* is beyond a static pass |
| `<see cref="X"/>` | keep; the C# compiler resolves it (CS1574), unlike Rust's intra-doc links |
| `<!-- -->` in `.csproj`/`.props`/`.targets` | line-level bans apply (plan/issue, dates, slice status, history, attribution); length cap and api-doc completeness do not — see SKILL.md |

Zero tolerance, no baseline: a baseline records the current count as acceptable, and re-baselining
after a rule change relabels old debt as the new normal. `src/`, `tests/`, `spikes/` and `tools/` are
all scanned on the same terms — exempting a directory is a baseline wearing a different hat. So are
`.csproj`, `.props` and `.targets` files there and at the repo root, alongside `.cs` and `.ps1`.

Run `tools/verify-comment-only.ps1` after a comment sweep. It requires every line the diff **adds and
removes** to be a comment — the symmetric check, because a pure deletion satisfies the obvious
one-sided version while removing the `using` block along with the comment above it.

## Non-negotiable design rules

1. The canonical input is semantic CRUD+ intent, never a low-level property script or reflection
   plan.
2. A generated LibLCM Mutation Plan is output-only. It may be previewed and recorded, but never
   accepted as canonical input.
3. The caller supplies an already-loaded `LcmCache` and owns project lifecycle and persistence. If
   the caller does not own the project and intend to persist it, `FwDataProjectLoader.LoadScratchCache`
   is the one to call, not `LoadCache`.
4. One complete Change Set is one atomic LibLCM unit of work. Individual operation methods never
   commit or open independent transactions.
5. **Order is authoritative only where it is declared.** The runner honours declared dependencies
   (`requires`, `dependsOn`) and never infers one from array position. **No two operations in a finalized
   Proposal may address the same slot** — `(target, field, discriminator)`, where the discriminator is the
   writing system for `Multi*` fields and the member id for collections — so there is nothing else for
   position to mean. Amended by [ADR 0026](docs/adr/0026-order-is-declared-not-positional.md); previously
   read "operation array order is authoritative, never silently reorder."
6. Rebase may refresh baseline-relative evidence or unambiguous placement anchors. It may not
   alter target, verb, value, identity, create/delete intent, or operation order.
7. Unknown operation kinds and semantic properties are rejected. Tool metadata belongs only in
   explicit non-semantic `extensions`.
8. Portable entity IDs use a 22-character unpadded base64url suffix encoding exactly 128 bits.
   Prefix and provenance are optional and unenforced.
9. Never use `Guid.ToByteArray()` or `new Guid(byte[])` for canonical ID conversion. Use textual
   GUID/network byte order as specified in `docs/change-set-contract.md`.
10. Same-type GUID collisions always warn about overwrite/reuse. Wrong-type collisions are genuine
    semantic conflicts unless an explicit storage-GUID override is authored.
11. Custom-field `flid` values are cache-local implementation details, never portable identity.
12. Diff is mechanical and linguistically unaware. Match entities only by exact canonical
    identity/GUID. Do not infer matches by forms, glosses, labels, fingerprints, or similarity.
13. Every LibLCM model member must be classified by a generated coverage inventory. Unclassified
    model changes fail CI.
14. Semantic snapshots must follow LibLCM normalization conventions, including NFD for plain
    Unicode and NFSC for rich strings.
15. The runner is storage-agnostic. Do not add Git, database, review, permission, or hosting logic.
16. Do not vendor or submodule LibLCM. Use pinned packages with a documented local-package override.
17. **Every ADR and every plan section opens with one or two sentences a non-specialist can read**,
    before any internal detail. Say what changes for someone using the product and why it matters;
    then use the internal vocabulary freely for the rest. The identifiers, cross-references, and
    class names below that opener are the point of these documents and must stay. This is a
    two-register rule, not a simplification pass: the opener is for the owner deciding whether to
    read on, the body is for whoever implements or audits it. A status line like "Slice A built;
    the poisoning guard now fires for real" fails the rule — it names machinery only.

18. **No migration code before 1.0.** Motif is pre-alpha: there is no database, no file, and no
    on-disk shape in the world worth preserving, and every line written to carry an old one forward
    is a line paid for with nothing. A stored shape is either the current one or it is refused with
    an error telling the developer to delete it and let Motif recreate it. This applies to schema
    generations, format upgraders, back-compat readers, and deprecated flags kept as aliases — a
    rename is a rename, not a rename plus a bridge. Revisit this at 1.0, when someone outside this
    repository first has data that matters.

## Compatibility targets

**One runtime, one target: `net10.0`, everywhere.** Every Motif project — `SIL.Motif.Contract` included —
targets `net10.0` and nothing else
([ADR 0043](docs/adr/0043-one-command-catalog-two-front-ends.md)). `net8.0` is not a target anywhere in this
repository, and neither is `net48`: no Motif assembly loads in a `net48` host to *run* Motif, because a
separate FieldWorks reaches Motif by running the `motif` executable and reading its JSON, and a unified
FieldWorks, should one ever exist, is `net10.0` too — its own Avalonia work is that same move.

`SIL.Motif.Contract` still crosses a process boundary **as shapes only**: it has no LibLCM reference, and
non-.NET runners — the Python and Rust ones its project file already names — read it as the normative,
RFC 8785-canonicalised description of Motif's field and response shapes. That is a wire description, never a
binary compatibility promise to `net48`: nothing left needs to *load* Contract in a `net48` process, only to
read it as a specification, which carries no target-framework requirement at all.

[ADR 0040](docs/adr/0040-one-api-the-cli.md) decision 3 argued the opposite — that `netstandard2.0` survives
on any assembly something outside Motif references, which kept it on Contract and, until retirement, on the
Runner. [ADR 0043](docs/adr/0043-one-command-catalog-two-front-ends.md) supersedes that decision: the `net48`
host it was written for is going away, so there is nothing left for a second target framework to buy. ADR
0040's other decisions — the database as the only coordination boundary between Motif's own processes, one
shipped artifact at one version, no shared-XML peering — are unaffected and still bind.

All LibLCM-dependent projects pin `SIL.LCModel 11.0.0-beta0150`.

No product `.csproj` in this repository mentions `netstandard2.0`, and
`CompatibilityTargetTests.EveryProductProjectTargetsOnlyNet10` fails the build if one starts to. Contract
carries no explicit `System.Text.Json` pin either: `net10.0` supplies it, and the old pin tracked the last
release line that still built for `netstandard2.0`.

**Not yet built:** the FieldWorks-side Motif surface. A separate FieldWorks integrates by running exactly one
CLI call, `motif apply --all-pending`, at a save boundary with the project released, and reloads afterward —
the FLExBridge pattern. It references nothing of Motif's, not even `SIL.Motif.Contract`, because it reads an
exit code and a summary rather than deserialising a typed result.

**Also not yet built, and a prerequisite for that surface:** the records `--json` serialises live in
`SIL.Motif.Projection`, which references LibLCM. They must move to Contract, leaving the
`LcmCache`-dependent builders behind, before any consumer can bind to them.

## Definition of done for each operation family

An operation family is incomplete until it has:

- a closed schema;
- prose semantics;
- validation and lowering;
- preview effects;
- apply and read-back behavior;
- conflict/rebase behavior;
- semantic snapshot and diff support;
- positive, negative, rollback, round-trip, and conformance fixtures;
- a coverage-manifest mapping to the relevant LibLCM surface.

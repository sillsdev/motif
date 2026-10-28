# Changed-files test selection

`./test.ps1 -Changed` runs only the test classes a diff can affect. `./test.ps1 -All` stays the full gate and is what
CI and release validation run. A map built from a measured run decides which classes a change can affect; no static
analysis is involved, because every test project references nearly all of `src/` and any reference graph selects
everything.

## Use

```
./test.ps1 -BuildMap                  # nightly or on request: one instrumented full run, writes the map
./test.ps1 -Changed                   # diff against the merge-base with origin/main, run the selected classes
./test.ps1 -Changed -Explain          # print the selection and the reasons; build and run nothing
./test.ps1 -Changed -Base <ref>       # diff against another base
```

The map is `bin/.cache/test-map/coverage-map.json` (`-MapPath` names another), keyed by the commit it was built at.
It is a cache, not a committed file: it is about 1 MB and changes on every build. Share it between worktrees or
machines by copying that file and passing `-MapPath`.

## What -Changed runs

The diff is `git diff <base>` (committed and uncommitted changes) plus untracked files. A run is the union of:

- every class whose covered files include a changed file (the reason line names the file);
- every class declared in a changed or added test file, including ones the map has never seen;
- a fixed smoke set, `tests/test-smoke-classes.txt`.

Each selected class is printed with its recorded seconds and why it was selected, then the class count, the
test-seconds and a wall-clock estimate. The selected classes reach each test host through
`MOTIF_TEST_CLASS_SELECTION`, a file of class names, so no long `--filter` is built. Levels are all of them, as in
`-All`; only the classes differ. The tooling regression scripts are skipped, because a change to `tools/`,
a build file or `test.ps1` selects the full suite anyway.

## When it runs everything

Anything the map cannot vouch for selects the full suite, and the reason is printed:

- no map, a map whose commit is not an ancestor of HEAD, or one more than `-MaxMapAge` commits old (default 40);
- `build.ps1`, `test.ps1`, `Motif.sln`, `Directory.Build.*`, `*.csproj`, `*.props`, `*.targets`, `.config/`,
  `.github/workflows/`, and any script, JSON or other non-C# file under `tools/`;
- a new or drifted file in `tests/SIL.Motif.Tests.Support` or `tests/Shared`, which every test project compiles;
- a source file outside `src/` and `tests/` that the map does not know;
- a non-code input no source names (see below).

A new source file under `src/<Project>/`, or one that changed after the map was built, runs every class that covers
anything in that project instead. A project nothing covers runs everything.

## Non-code inputs

A changed `.axaml` selects the classes that cover its code-behind (`X.axaml.cs`); a changed `.resx` or `.axaml`
also selects the classes that cover any C# file naming its stem. Any other changed file selects the classes that
cover a C# file whose string literals name the file or its directory (`"samples/..."`, `"walkthroughs"`). Files
under `docs/`, `site/`, `plugin/`, `licenses/`, and Markdown files that no literal names are ignored. A data file
nothing names runs everything.

## How the map is built

`-BuildMap` runs the whole suite under coverlet (restored on demand, as for `-Coverage`; no project references it)
with `tools/coverage/map.runsettings`: the product and the test assemblies are instrumented, theories are enumerated
while the class runs (so the code that produces theory data is the class's own), and classes run one at a time in
each process. `CoverageMapRecorder` (in `SIL.Motif.Tests.Support`, enabled by `MOTIF_TEST_COVERAGE_MAP_DIR`) reads and
clears the hit counters coverlet injects into every instrumented module when each class finishes, so each class gets
exactly the code it ran, with no per-class run. Hits in a collection fixture belong to every class of the collection,
and hits outside any collection to every class of the process. Indices become source files through each method's
portable PDB. `tools/MotifChangedTests.psm1` merges the per-process files.

A class that starts a child process (the CLI, the worker) is followed into it: each map-build process runs from a
private copy of the test output (`bin/map-run-<label>/`, removed afterwards) whose product folder links the
instrumented Motif assemblies, so a child loads the same instrumented files and adds its hits to the file coverlet
keeps for each module when it exits. The recorder reads and clears that file at every class boundary, which
attributes the child's code to the class that started it. This needs symbolic links, so `-BuildMap` is for Linux, macOS
and Windows with developer mode.

## Limits

- The map holds what ran on the machine that built it. A class whose tests were all skipped there (no parser, no
  display), or in which any test failed, has partial coverage, so any code change selects it.
- A child that is killed instead of exiting writes no hits, and one that outlives its class is attributed to the class
  running when it exits.
- Coverage is by file. A class that runs one method of a file depends on the whole file.
- A file changed between the map's commit and the base is treated as unmapped, but a file a class newly reaches
  through such a change is not detected; the age limit and the nightly rebuild bound that.
- Reflection or a source generator that reaches code no test calls directly is covered only if a test executes it.

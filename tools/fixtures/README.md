# PanGloss interface probe

The [PanGloss interface gate](../Test-PanGlossInterfaces.ps1) obtains its input by running
`SIL.Motif.SampleProjects interface-probe`, built by [build.ps1](../../build.ps1). The helper creates a
private LibLCM project with the existing fixture APIs, then makes a disposable copy with a dangling
environment reference to check the parser's structured compile-error response.

The gate runs the pinned PanGloss against those generated files to check grammar health, trace details,
batch statistics and the compile-error interface. The LibLCM cache is closed before PanGloss starts;
all generated project files stay under the gate's private `bin/.cache` directory and are removed when
the gate exits. No grammar fixture is committed or copied into source.

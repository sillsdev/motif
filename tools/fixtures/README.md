# PanGloss interface fixture

`pangloss-interface-smoke.xml` is a small synthetic FieldWorks export used by
`Test-PanGlossInterfaces.ps1` to exercise grammar-health, trace details, batch statistics, and a deliberate
compile error. The gate copies it under `bin/.cache` before running PanGloss.

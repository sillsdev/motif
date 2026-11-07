# Exactly two Avalonia lines: FieldWorks' 11.3 and Motif's 12

One source tree builds for netstandard2.0 against Avalonia 11.3.x, for FieldWorks on .NET Framework 4.8, and for
net10.0 against Avalonia 12.x, for Motif. No other versions are supported. When FieldWorks moves to Avalonia 12, the
11.3 target is deleted, together with every workaround that exists only for it.

**Consequences:** per-target code is rare, small, behind `#if NET10_0_OR_GREATER` and listed in the README.

Owner decision, 2026-11-03.

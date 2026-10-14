#!/usr/bin/env dotnet
#:package SkiaSharp@3.119.4
// Win32 and macOS native assets arrive through SkiaSharp's own dependencies; Linux's do not.
#:package SkiaSharp.NativeAssets.Linux@3.119.4
#:include CaptureDiff.Core.cs

using SIL.Motif.CaptureScreens;

return CaptureDiff.Run(args);
